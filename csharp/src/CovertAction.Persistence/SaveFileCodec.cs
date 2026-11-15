using CovertAction.Core.Shared;

namespace CovertAction.Persistence;

/// <summary>
/// A saved campaign (CV0.SVE to CV3.SVE, one per save slot) as the original writes it: first the 1008 bytes of the
/// hand-off block from byte 0x10 to 0x3ff (the campaign's scalar values and the message text; the sixteen bytes in
/// front, which only tell the launcher which part runs next, are not saved), then the campaign tables exactly as
/// they lie in the block.
/// </summary>
/// <remarks>
/// Proven on the original's own save files: each is read into the typed state and written back from it alone,
/// byte for byte (SaveFileTests).
/// </remarks>
// orig: the save file written by write_env_state and GAME's save_game: SHARED+0x10..0x3ff, then the tables of SHARED+0x400 (src/c/include/state.h; src/c/final/final_0100_a2f6.c)
public static class SaveFileCodec
{
    /// <summary>Where the saved part of the hand-off block starts.</summary>
    private const int HeaderStart = 0x10;

    /// <summary>The size of the header: the block's bytes 0x10 to 0x3ff.</summary>
    public const int HeaderSize = SharedState.TablesOffset - HeaderStart;

    /// <summary>Where the level of play (the hand-off block's word at 0x3a) lies in a save file: the one word of the head the slot list reads.</summary>
    public const int LevelOffset = 0x3a - HeaderStart;

    /// <summary>The size of a save file for a case with the given cast and events.</summary>
    public static int Size(int cast, int events) => HeaderSize + CampaignTablesCodec.Size(cast, events);

    /// <summary>
    /// Whether a save file holds a whole campaign: its head, a cast and events the parts' tables have room for (32 and
    /// 64 records, GAME's <c>ac</c> and <c>ev</c>), and every table its counts say. The original reads any file it can
    /// open: a shorter head is completed with what its stack held, counts beyond the tables are read past them, and a
    /// file that ends early leaves the rest of the tables as the part had them (D-105).
    /// </summary>
    /// <param name="header">The file's first bytes, as many as it has up to <see cref="HeaderSize"/>.</param>
    /// <param name="length">The file's length in bytes.</param>
    public static bool IsWhole(ReadOnlySpan<byte> header, long length)
    {
        if (header.Length < HeaderSize)
        {
            return false;
        }
        var state = new SharedState();
        LoadHeader(header, state);
        return state.NCast is >= 0 and <= Core.Campaign.CampaignState.CastCapacity
            && state.NEvents is >= 0 and <= Core.Campaign.CampaignState.EventCapacity
            && length >= Size(state.NCast, state.NEvents);
    }

    /// <summary>Reads a saved campaign into the state. What the state holds of the hand-off (which part runs next, and so on) stays.</summary>
    /// <param name="file">The whole save file.</param>
    /// <param name="state">Receives the campaign.</param>
    /// <exception cref="ArgumentException">The file is shorter than its own counts say.</exception>
    public static void Load(ReadOnlySpan<byte> file, SharedState state)
    {
        if (file.Length < HeaderSize)
        {
            throw new ArgumentException($"a save file of {file.Length} bytes is shorter than its header");
        }
        LoadHeader(file[..HeaderSize], state);
        CampaignTablesCodec.Load(file[HeaderSize..], state.NCast, state.NEvents, state.Campaign);
    }

    /// <summary>Reads the head of a save file into the state: bytes 0x10 to 0x3ff of the hand-off block. What the state holds in front of them stays.</summary>
    /// <param name="header">The first <see cref="HeaderSize"/> bytes of the file.</param>
    /// <param name="state">Receives the campaign's scalars and the message text.</param>
    public static void LoadHeader(ReadOnlySpan<byte> header, SharedState state)
    {
        // the header is a piece of the hand-off block: put it there, around the bytes the state has outside it
        var block = new byte[SharedState.Size];
        SharedBlockCodec.Layout.Store(state, block);
        header[..HeaderSize].CopyTo(block.AsSpan(HeaderStart));
        SharedBlockCodec.Layout.Load(block, state);
    }

    /// <summary>Writes the state's campaign over the bytes of a save file (the bytes no field covers stay).</summary>
    /// <param name="state">The campaign.</param>
    /// <param name="file">The file's bytes to write over; at least <see cref="Size"/> for the state's cast and events.</param>
    public static void Store(SharedState state, Span<byte> file)
    {
        var block = new byte[SharedState.Size];
        file[..HeaderSize].CopyTo(block.AsSpan(HeaderStart));
        SharedBlockCodec.Layout.Store(state, block);
        block.AsSpan(HeaderStart, HeaderSize).CopyTo(file);
        CampaignTablesCodec.Store(state.Campaign, file[HeaderSize..]);
    }

    /// <summary>The head of a save file: bytes 0x10 to 0x3ff of the hand-off block, as the state has them.</summary>
    /// <param name="state">The hand-off block.</param>
    public static byte[] Header(SharedState state)
    {
        var block = new byte[SharedState.Size];
        SharedBlockCodec.Layout.Store(state, block);
        return block.AsSpan(HeaderStart, HeaderSize).ToArray();
    }

    /// <summary>A new save file with the state's campaign.</summary>
    /// <param name="state">The campaign.</param>
    public static byte[] Write(SharedState state)
    {
        var file = new byte[Size(state.Campaign.Cast.Count, state.Campaign.Events.Count)];
        Store(state, file);
        return file;
    }
}
