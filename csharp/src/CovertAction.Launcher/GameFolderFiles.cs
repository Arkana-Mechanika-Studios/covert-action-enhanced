using CovertAction.Content;
using CovertAction.Platform;

namespace CovertAction.Launcher;

/// <summary>
/// The game's files for a host that keeps the whole game in one folder (the player's copy on a hard disk): the
/// game's own files are read from that folder, and what the game writes (saved campaigns) goes to a folder of
/// its own, so that the player's copy stays as it is. A file the game wrote is read back from there.
/// </summary>
/// <remarks>New code: what DOS and the C library do for the original. There are no disks to swap.</remarks>
/// <param name="folder">The player's game folder.</param>
/// <param name="writeFolder">The folder for the files the game writes; null: nothing can be written.</param>
public sealed class GameFolderFiles(GameFolder folder, string? writeFolder = null) : IFiles, IDisposable
{
    private const int DriveC = 3;
    private const int FirstHandle = 5;
    private const int Failed = -1;
    private const byte EndOfText = 0x1a;

    /// <summary>The open files by their numbers less <see cref="FirstHandle"/>; null where a file was closed.</summary>
    private readonly List<OpenFile?> _open = [];

    /// <summary>What the game printed on the console.</summary>
    private readonly System.Text.StringBuilder _console = new();

    private sealed record OpenFile(Stream Stream, bool Text)
    {
        /// <summary>The bytes the last read took from a text file began with a line feed.</summary>
        public bool ReadBeganWithLineFeed { get; set; }
    }

    /// <inheritdoc/>
    /// <remarks>A file is found when it is in the folder, whatever drive is asked for; the game then never shows its insert-disk prompt.</remarks>
    public bool FindFileOnDisk(string name, int drive) => folder.Find(name) is not null;

    /// <inheritdoc/>
    /// <remarks>Always drive C: the game then never asks the player to wait for a floppy disk.</remarks>
    public int CurrentDrive() => DriveC;

    /// <inheritdoc/>
    /// <remarks>Always readable: the saved games are in a folder, whatever drive the game asks about.</remarks>
    public int VerifyDisk(int drive) => 0;

    /// <inheritdoc/>
    public void ResetDisks()
    {
    }

    /// <inheritdoc/>
    /// <remarks>DOS ignores blanks at the end of a name: the menus part builds the names of the crime and world files with one ("crime0.dta ").</remarks>
    public int Open(string name, OpenMode mode, int? permission = null)
    {
        name = WithoutDrive(name.TrimEnd(' '));
        var writes = (mode & (OpenMode.Write | OpenMode.ReadWrite | OpenMode.Create)) != 0;
        var written = writeFolder is null ? null : Path.Combine(writeFolder, name.ToUpperInvariant());
        Stream stream;
        try
        {
            if (writes)
            {
                var players = File.Exists(written) ? null : folder.Find(name);
                if (written is null || (!File.Exists(written) && players is null && !mode.HasFlag(OpenMode.Create)))
                {
                    return Failed;
                }
                Directory.CreateDirectory(writeFolder!);
                if (players is not null && !mode.HasFlag(OpenMode.Truncate))
                {
                    // a file of the player's folder opened to write (the Hall of Fame, FINAL 0100:4cd0) is that file under
                    // DOS: its bytes are taken into the write folder, so that they are read back and kept (the player's
                    // folder itself is never changed)
                    File.Copy(players, written);
                }
                stream = new FileStream(written, mode.HasFlag(OpenMode.Truncate) ? FileMode.Create : FileMode.OpenOrCreate, FileAccess.ReadWrite);
            }
            else if ((written is not null && File.Exists(written) ? written : folder.Find(name)) is { } path)
            {
                stream = File.OpenRead(path);
            }
            else
            {
                return Failed;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // a file Windows does not let the game make or open (a read-only save, a saves folder that cannot be written
            // to, a full disk, a file another program holds) fails as DOS's open does: the game answers it as the original
            // does (the save screens' "File Error", an empty Hall of Fame)
            return Failed;
        }
        var free = _open.IndexOf(null);
        if (free < 0)
        {
            _open.Add(null);
            free = _open.Count - 1;
        }
        _open[free] = new OpenFile(stream, mode.HasFlag(OpenMode.Text));
        return FirstHandle + free;
    }

    /// <inheritdoc/>
    public int Read(int handle, Span<byte> buffer)
    {
        if (OpenFileOf(handle) is not { } file)
        {
            return Failed;
        }
        try
        {
            return file.Text ? ReadText(file, buffer) : file.Stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        }
        catch (IOException)
        {
            // a file Windows cannot read on (a disk that went away) fails as DOS's read does
            return Failed;
        }
    }

    private static int ReadText(OpenFile file, Span<byte> buffer)
    {
        // text: the bytes asked for are read, then the carriage returns before line feeds are taken out, and
        // Ctrl-Z ends the file (so fewer bytes than asked for can come back in the middle of a file)
        var read = file.Stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        file.ReadBeganWithLineFeed = read > 0 && buffer[0] == (byte)'\n';
        var kept = 0;
        for (var i = 0; i < read; i++)
        {
            if (buffer[i] == EndOfText)
            {
                file.Stream.Seek(0, SeekOrigin.End);
                break;
            }
            var beforeLineFeed = i + 1 < read ? buffer[i + 1] == (byte)'\n' : file.Stream.ReadByte() is var next && Unread(file.Stream, next) == '\n';
            if (buffer[i] != (byte)'\r' || !beforeLineFeed)
            {
                buffer[kept++] = buffer[i];
            }
        }
        return kept;
    }

    /// <inheritdoc/>
    public bool ReadBeganWithLineFeed(int handle) => OpenFileOf(handle) is { ReadBeganWithLineFeed: true };

    /// <inheritdoc/>
    public int Write(int handle, ReadOnlySpan<byte> data)
    {
        if (handle is 1 or 2)
        {
            // the console: the rewrite has none to print on; what the game prints there (the name of a file it could
            // not read, before it ends) is kept for the launcher to say
            _console.Append(System.Text.Encoding.Latin1.GetString(data));
            return data.Length;
        }
        if (OpenFileOf(handle) is not { } file || !file.Stream.CanWrite)
        {
            return Failed;
        }
        try
        {
            file.Stream.Write(data);
        }
        catch (IOException)
        {
            // a full disk: the C library's write answers -1 when DOS wrote nothing (GAME 00f7:f0f3, errno 28); the game
            // does not look (GAME 00f7:aad4 save_game, FINAL 0100:a2f6 write_env_state), and neither does the rewrite
            return Failed;
        }
        return data.Length;
    }

    /// <summary>What the game printed on the console (DOS's handles 1 and 2) since the last <see cref="TakeConsoleOutput"/>.</summary>
    public string TakeConsoleOutput()
    {
        var text = _console.ToString();
        _console.Clear();
        return text;
    }

    /// <inheritdoc/>
    /// <remarks>
    /// A file the game wrote is looked for first, then the player's own. To be written, a file must be one the
    /// game wrote (the player's copy is never changed), which is what the game asks before it writes the Hall of Fame.
    /// </remarks>
    public int Access(string name, int mode)
    {
        const int toWrite = 2;
        name = WithoutDrive(name);
        var written = writeFolder is null ? null : Path.Combine(writeFolder, name.ToUpperInvariant());
        if (written is not null && File.Exists(written))
        {
            // a read-only file (one the player marked so) cannot be written, as under DOS
            return (mode & toWrite) != 0 && File.GetAttributes(written).HasFlag(FileAttributes.ReadOnly) ? Failed : 0;
        }
        return (mode & toWrite) == 0 && folder.Find(name) is not null ? 0 : Failed;
    }

    /// <inheritdoc/>
    public int Close(int handle)
    {
        if (OpenFileOf(handle) is not { } file)
        {
            return Failed;
        }
        _open[handle - FirstHandle] = null;
        try
        {
            file.Stream.Dispose();
        }
        catch (IOException)
        {
            // the last bytes did not fit on the disk: the file is closed with what it holds (the game does not look)
            return Failed;
        }
        return 0;
    }

    /// <inheritdoc/>
    public int Seek(int handle, int offset, int origin)
    {
        if (OpenFileOf(handle) is not { } file || origin is < 0 or > 2)
        {
            return Failed;
        }
        try
        {
            return (int)file.Stream.Seek(offset, (SeekOrigin)origin);
        }
        catch (IOException)
        {
            // before the file's start, as DOS refuses it
            return Failed;
        }
    }

    private const int NoSuchFile = 2;
    private const int NoSuchHandle = 6;

    /// <inheritdoc/>
    public int DosOpen(string name, int mode, out int handle)
    {
        handle = Open(name, OpenMode.Binary | (OpenMode)(mode & 3));
        return handle == Failed ? NoSuchFile : 0;
    }

    /// <inheritdoc/>
    public int DosRead(int handle, Span<byte> buffer, out int count)
    {
        count = Math.Max(0, Read(handle, buffer));
        return OpenFileOf(handle) is null ? NoSuchHandle : 0;
    }

    /// <inheritdoc/>
    public int DosClose(int handle) => Close(handle) == Failed ? NoSuchHandle : 0;

    /// <inheritdoc/>
    public int DosSeek(int handle, int offset) => Seek(handle, offset, 0) == Failed ? NoSuchHandle : 0;

    /// <summary>Closes the files the game left open (the original leaves that to DOS when it ends).</summary>
    public void Dispose()
    {
        foreach (var file in _open)
        {
            try
            {
                file?.Stream.Dispose();
            }
            catch (IOException)
            {
                // a file whose last bytes did not fit: closed with what it holds
            }
        }
        _open.Clear();
    }

    /// <summary>The name without the drive letter the saved games' names start with ("c:cv0.sve"): every drive is the one folder.</summary>
    private static string WithoutDrive(string name) => name.Length > 1 && name[1] == ':' && char.IsAsciiLetter(name[0]) ? name[2..] : name;

    private OpenFile? OpenFileOf(int handle) => handle >= FirstHandle && handle - FirstHandle < _open.Count ? _open[handle - FirstHandle] : null;

    /// <summary>Puts a byte that was read ahead back; gives it (or -1 at the file's end).</summary>
    private static int Unread(Stream stream, int value)
    {
        if (value >= 0)
        {
            stream.Seek(-1, SeekOrigin.Current);
        }
        return value;
    }
}
