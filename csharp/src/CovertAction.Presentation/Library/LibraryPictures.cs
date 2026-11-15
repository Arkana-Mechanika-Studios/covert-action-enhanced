using CovertAction.Content;
using CovertAction.Core.Graphics;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Library;

/// <summary>
/// Loading pictures: the routines that open a picture file, by itself or inside the archive a part has open, decode
/// it and put it on a page row by row. Every part that shows pictures carries them.
/// </summary>
/// <remarks>
/// <para>
/// The original decodes a row at a time in assembly while it reads the file in blocks of 512 bytes. Here the file
/// is read to its end and decoded at once by <see cref="PictureFile"/>, which is proven on every picture of the game
/// against the project's verified decoders; the rows then go to the page exactly as the original puts them. What
/// differs is only how the reading is cut into calls, which nothing shows.
/// </para>
/// <para>
/// Against the original's recorded runs (LibraryOracleTests) the files opened, every seek and every byte read are
/// compared, and the rows put on the page in their order; where a read stands among the drawing calls is not, since
/// the reading is cut differently (D-12).
/// </para>
/// </remarks>
/// <param name="world">The drivers and the files.</param>
public sealed class LibraryPictures(Boundaries world)
{
    private const int NoArchive = -1;
    private const int BlockSize = 0x200;
    private const int ArchiveEntrySize = 24;
    private const int ArchiveNameSize = 12;

    /// <summary>The row being put on the page; the original keeps one buffer for all rows, and so does this.</summary>
    private readonly byte[] _lineBuffer = new byte[McgaWidth + 1];

    private const int McgaWidth = 320;

    /// <summary>The number of the archive the part has open, or -1: pictures are looked for in it first.</summary>
    // orig: CHASE DS:archive_handle and its copies (src/c/include/chase.h)
    public int ArchiveHandle { get; set; } = NoArchive;

    /// <summary>Opens an archive; from then on <see cref="OpenResource"/> looks in it first.</summary>
    /// <param name="name">The archive's file name.</param>
    /// <returns>Whether it could be opened.</returns>
    // orig: CHASE 00df:4746 open_archive (src/c/chase/chase_00df_4746.c) matched; also TAC 00e8:d672, BUG 00e8:743e; tests: LibraryPicturesTests, LibraryOracleTests
    public bool OpenArchive(string name)
    {
        if (world.Files.DosOpen(name, 0, out var handle) != 0)
        {
            return false;
        }
        ArchiveHandle = handle;
        return true;
    }

    /// <summary>Opens a picture or other resource: the entry of that name in the open archive, else the file itself.</summary>
    /// <param name="name">The resource's file name.</param>
    /// <param name="mode">DOS's access mode for a file of its own: 0 to read.</param>
    /// <returns>The number to read it with (the archive's own number for an entry, with the archive moved to it).</returns>
    // orig: CHASE 00df:430e open_resource (src/c/chase/chase_00df_430e.c) matched; also CHASE 00df:479c (find_in_archive), FINAL 0100:c1a0, GAME 00f7:d522, TAC 00e8:d23a, BUG 00e8:7006, TAC 00e8:d6c8, BUG 00e8:7494, FINAL 0100:c62e (find_in_catalog), GAME 00f7:d9b0 (find_catalog_entry); tests: LibraryPicturesTests, LibraryOracleTests
    public int OpenResource(string name, int mode)
    {
        if (FindInArchive(name) is { } offset)
        {
            if (world.Files.DosSeek(ArchiveHandle, offset) != 0)
            {
                FatalError(name);
            }
            return ArchiveHandle;
        }
        if (world.Files.DosOpen(name, mode, out var handle) != 0)
        {
            FatalError(name);
        }
        return handle;
    }

    /// <summary>Closes a resource, unless it is the archive (which stays open for the next one).</summary>
    /// <param name="handle">The number <see cref="OpenResource"/> gave.</param>
    // orig: CHASE 00df:4398 close_resource (src/c/chase/chase_00df_4398.c) matched; also FINAL 0100:c22a, TAC 00e8:d2c4, BUG 00e8:7090, GAME 00f7:d5ac; tests: LibraryPicturesTests, LibraryOracleTests
    public void CloseResource(int handle)
    {
        if (handle != ArchiveHandle && world.Files.DosClose(handle) != 0)
        {
            FatalError("");
        }
    }

    /// <summary>Loads a picture onto a page, its top left corner in the page's.</summary>
    /// <param name="page">The page number.</param>
    /// <param name="name">The picture's file name.</param>
    // orig: CHASE 00df:4488 load_picture (src/c/chase/chase_00df_4488.c) matched; also FINAL 0100:c31a, GAME 00f7:d69c, TAC 00e8:d3b4, BUG 00e8:7180; tests: LibraryPicturesTests, LibraryOracleTests
    // DIVERGENCE: D-12 the file is read to its end and decoded at once instead of row by row in 512-byte blocks.
    public void LoadPicture(int page, string name)
    {
        var handle = OpenResource(name, 0);
        var picture = PictureFile.Read(ReadToEnd(handle));
        if (picture.FourColourTable is { } table)
        {
            // a picture that brings a table for the four-colour display hands it to the graphics driver before its
            // first row (the picture decoder does, with no first table); the 256-colour driver has no use for it
            world.Graphics.SetColorMaps([], table);
        }
        for (var row = 0; row < picture.Height; row++)
        {
            picture.Pixels.AsSpan(row * picture.Width, picture.Width).CopyTo(_lineBuffer);
            world.Graphics.PutPicLine(_lineBuffer, page, row, 0, picture.Width);
        }
        CloseResource(handle);
    }

    /// <summary>The same with the arguments the other way round, as some callers have them.</summary>
    /// <param name="name">The picture's file name.</param>
    /// <param name="page">The page number.</param>
    // orig: CHASE 00df:4598 load_picture_swapped (src/c/chase/chase_00df_4598.c) matched; also FINAL 0100:c42a, GAME 00f7:d7ac, TAC 00e8:d4c4, BUG 00e8:7290; tests: LibraryPicturesTests, LibraryOracleTests
    public void LoadPictureSwapped(string name, int page) => LoadPicture(page, name);

    /// <summary>
    /// Loads a block of bytes that is compressed like a picture: a file with a count in its head instead of a
    /// picture's width and height (<see cref="PictureFile.ReadBlock"/>).
    /// </summary>
    /// <param name="name">The file's name.</param>
    /// <returns>
    /// The block. The original writes it into a buffer of its caller, which it is given as a far address, and
    /// answers with the count from the file's head.
    /// </returns>
    // orig: CHASE 00df:44ec load_picture_surface (src/c/chase/chase_00df_44ec.c) matched; also GAME 00f7:d700, BUG 00e8:71e4, FINAL 0100:c37e, TAC 00e8:d418; tests: LibraryOracleTests, ContentTests
    // DIVERGENCE: D-12 the file is read to its end and decoded at once instead of in 512-byte blocks.
    // DORMANT: no program calls it, and no file of the game has this form; it would run for a caller that hands it such a file.
    public byte[] LoadPictureSurface(string name)
    {
        var handle = OpenResource(name, 0);
        var block = PictureFile.ReadBlock(ReadToEnd(handle));
        CloseResource(handle);
        return block;
    }

    // ---- the two sheets of faces ----

    /// <summary>The page the sheets of faces are loaded onto to be cut up.</summary>
    private const int FacesPage = 2;

    /// <summary>The parts of a face: 40 on a sheet, eight to a row, each 30 x 33 pixels in a cell of 32 x 35 with a frame of one pixel.</summary>
    private const int FaceParts = 40, FacePartsPerRow = 8, FacePartCellWidth = 32, FacePartCellHeight = 35, FacePartWidth = 30, FacePartHeight = 33;

    /// <summary>The clothes: four on each sheet, one under the other at the sheet's right, each 44 x 18 pixels in a row of 20.</summary>
    private const int Clothes = 4, ClothesX = 0x101, ClothesRowHeight = 20, ClothesWidth = 44, ClothesHeight = 18;

    /// <summary>The seven strips at the foot of the men's sheet: 32 x 26 pixels each, side by side.</summary>
    private const int Strips = 7, StripsX = 0x60, StripsY = 0xae, StripWidth = 32, StripHeight = 26;

    /// <summary>Where each group starts in the table of images: the women's and the men's face parts, the strips, the men's clothes, four single pictures, the women's clothes.</summary>
    private const int WomenParts = 0, MenParts = 0x28, FirstStrip = 0x60, MenClothes = 0x68, FirstSingle = 0x6c, WomenClothes = 0x70;

    /// <summary>
    /// Loads the two sheets of faces and cuts them into the part's table of images: from the women's sheet 40 face
    /// parts and four pictures of clothes, from the men's the same and besides seven strips and four single
    /// pictures (the last of them the envelope of an incoming message).
    /// </summary>
    /// <param name="sprite">The part's table of images, 116 entries or more (FINAL's image_table, TAC's icon_handles, BUG's sprite).</param>
    /// <param name="womenSheet">The file name of the women's sheet, a text of the program ("facesf.pic").</param>
    /// <param name="menSheet">The file name of the men's sheet ("faces.pic").</param>
    // orig: FINAL 0100:7e77 load_faces_pic (src/c/final/final_0100_7e77.c) matched; also TAC 00e8:ad31, BUG 00e8:44ad; tests: LibraryOracleTests, LibraryPicturesTests
    public void LoadFacesPic(IList<Image?> sprite, string womenSheet, string menSheet)
    {
        LoadPicture(FacesPage, womenSheet);
        CutFaceParts(sprite, WomenParts);
        CutClothes(sprite, WomenClothes);

        LoadPicture(FacesPage, menSheet);
        CutFaceParts(sprite, MenParts);
        for (var i = 0; i < Strips; i++)
        {
            sprite[FirstStrip + i] = world.Graphics.CutImage(FacesPage, StripsX + i * StripWidth, StripsY, StripWidth, StripHeight);
        }
        CutClothes(sprite, MenClothes);
        // four single pictures, each with its own place and size on the men's sheet
        sprite[FirstSingle] = world.Graphics.CutImage(FacesPage, 0x49, 0xaf, 21, 22);
        sprite[FirstSingle + 1] = world.Graphics.CutImage(FacesPage, 0x133, 0, 11, 8);
        sprite[FirstSingle + 2] = world.Graphics.CutImage(FacesPage, 0x133, 9, 12, 8);
        sprite[FirstSingle + 3] = world.Graphics.CutImage(FacesPage, ClothesX, 0xa0, 26, 14);
    }

    /// <summary>The sixteen small pictures at the foot of the men's sheet: 8 x 8 pixels, eight to a row, 9 pixels apart, from row 0xaf.</summary>
    private const int SmallTiles = 16, SmallTilesPerRow = 8, SmallTileStep = 9, SmallTilesY = 0xaf, SmallTileSize = 8, FirstSmallTile = 0x50;

    /// <summary>The face beside the next message box: none.</summary>
    private const short NoFace = -1;

    /// <summary>
    /// Loads the pictures a part shows of persons when it starts: the two sheets of faces (<see cref="LoadFacesPic"/>)
    /// and, from the men's sheet, sixteen small pictures of 8 x 8 pixels into the table's entries 0x50 to 0x5f; no
    /// face is left waiting to be shown beside the next message box.
    /// </summary>
    /// <param name="state">The part's variables (the face beside the next box).</param>
    /// <param name="sprite">The part's table of images (FINAL's image_table, TAC's icon_handles, BUG's sprite).</param>
    /// <param name="womenSheet">The file name of the women's sheet, a text of the program ("facesf.pic").</param>
    /// <param name="menSheet">The file name of the men's sheet ("faces.pic").</param>
    // orig: FINAL 0100:7a8e load_pictures (src/c/final/final_0100_7a8e.c) matched; also BUG 00e8:40c4, TAC 00e8:a948; tests: LibraryOracleTests, LibraryPicturesTests
    public void LoadPictures(ILibraryState state, IList<Image?> sprite, string womenSheet, string menSheet)
    {
        world.Graphics.BeginImageCuts();
        LoadFacesPic(sprite, womenSheet, menSheet);
        for (var i = 0; i < SmallTiles; i++)
        {
            var x = i % SmallTilesPerRow * SmallTileStep;
            var y = i / SmallTilesPerRow * SmallTileStep + SmallTilesY;
            sprite[FirstSmallTile + i] = world.Graphics.CutImage(FacesPage, x, y, SmallTileSize, SmallTileSize);
        }
        world.Graphics.EndImageCuts();
        state.Animation.BackdropCode = NoFace;
    }

    /// <summary>Cuts the 40 face parts of the sheet on the page into the table from <paramref name="first"/> on.</summary>
    private void CutFaceParts(IList<Image?> sprite, int first)
    {
        for (var i = 0; i < FaceParts; i++)
        {
            var x = i % FacePartsPerRow * FacePartCellWidth + 1;
            var y = i / FacePartsPerRow * FacePartCellHeight + 1;
            sprite[first + i] = world.Graphics.CutImage(FacesPage, x, y, FacePartWidth, FacePartHeight);
        }
    }

    /// <summary>Cuts the four pictures of clothes of the sheet on the page into the table from <paramref name="first"/> on.</summary>
    private void CutClothes(IList<Image?> sprite, int first)
    {
        for (var i = 0; i < Clothes; i++)
        {
            sprite[first + i] = world.Graphics.CutImage(FacesPage, ClothesX, ClothesRowHeight * i, ClothesWidth, ClothesHeight);
        }
    }

    /// <summary>
    /// Where the entry of a name starts in the open archive, or null when there is no archive or no such entry.
    /// The archive's list is read from the file each time: a word with the number of entries, then 24 bytes per
    /// entry, the first 12 its name (compared without regard to letter case).
    /// </summary>
    private int? FindInArchive(string name)
    {
        if (ArchiveHandle == NoArchive)
        {
            return null;
        }
        Span<byte> count = stackalloc byte[2];
        if (world.Files.DosSeek(ArchiveHandle, 0) != 0 || world.Files.DosRead(ArchiveHandle, count, out _) != 0)
        {
            FatalError("");
        }
        Span<byte> entry = stackalloc byte[ArchiveEntrySize];
        for (var left = count[0] | count[1] << 8; left > 0; left--)
        {
            if (world.Files.DosRead(ArchiveHandle, entry, out _) != 0)
            {
                FatalError("");
            }
            var entryName = entry[..ArchiveNameSize];
            var end = entryName.IndexOf((byte)0);
            var text = System.Text.Encoding.Latin1.GetString(end < 0 ? entryName : entryName[..end]);
            if (string.Equals(text, name.Length > ArchiveNameSize ? name[..ArchiveNameSize] : name, StringComparison.OrdinalIgnoreCase))
            {
                const int offsetAt = 20;
                return entry[offsetAt] | entry[offsetAt + 1] << 8 | entry[offsetAt + 2] << 16 | entry[offsetAt + 3] << 24;
            }
        }
        return null;
    }

    /// <summary>The bytes of a file from where it stands to its end, read in the original's blocks.</summary>
    /// <param name="handle">The number <see cref="OpenResource"/> gave.</param>
    public byte[] ReadToEnd(int handle)
    {
        var bytes = new List<byte>();
        var block = new byte[BlockSize];
        while (world.Files.DosRead(handle, block, out var count) == 0 && count > 0)
        {
            bytes.AddRange(block.AsSpan(0, count));
        }
        return [.. bytes];
    }

    /// <summary>The exit code the original ends the program with when a file fails.</summary>
    private const int FileFailed = 99;

    /// <summary>
    /// The original's last resort when a file fails: it names the file on the console and ends the program (with
    /// the exit code 99). The parts call it themselves where they open a file without the routines above.
    /// </summary>
    /// <param name="name">The file's name, or what else to say.</param>
    // orig: CHASE 00df:46f4 fatal_error (src/c/chase/chase_00df_46f4.c) matched; also FINAL 0100:c586, GAME 00f7:d908, BUG 00e8:73ec, TAC 00e8:d620; tests: LibraryOracleTests, LibraryPicturesTests
    // DIVERGENCE: D-12 the original also switches the display to text and prints DOS's reason; here the name is printed and the program ends.
    public void FatalError(string name)
    {
        world.Files.Write(2, System.Text.Encoding.Latin1.GetBytes(name));
        throw new ProgramExitException(FileFailed);
    }
}
