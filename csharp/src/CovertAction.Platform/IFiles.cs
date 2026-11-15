namespace CovertAction.Platform;

/// <summary>
/// The game's files as the original's code reaches them. A host maps the names to the player's game folder; a test
/// answers from what the original was given. The game's code never sees a path of the host.
/// </summary>
/// <remarks>
/// The disk checks, and the five file calls of the C library everything else the game does with files comes down
/// to: its pictures, texts and archives are read with them, its saved games written. A file in use is named by a
/// small number the game gets when it opens the file (5 for the first, as under DOS, whose numbers 0 to 4 are the
/// console, the printer and the like).
/// </remarks>
public interface IFiles
{
    /// <summary>
    /// Looks for a file on the current drive, and on the other floppy drive when it is not there (MicroProse's
    /// assembly routine behind the insert-disk prompt). A host with the whole game in one folder finds every file.
    /// </summary>
    /// <param name="name">The file's name.</param>
    /// <param name="drive">The drive to try when the file is not on the current one (0 = A); the game passes 0.</param>
    /// <returns>Whether the file was found.</returns>
    // orig: CODE 0000:0002 find_file_on_disk thunk; also FINAL 0000:0002, GAME 0000:0002, TAC 0000:0002, BUG 0000:0002, CHASE 0000:0002
    bool FindFileOnDisk(string name, int drive);

    /// <summary>
    /// The drive the game was started from (the library's <c>_dos_getdrive</c>). The game asks in order to tell the
    /// player to wait while it loads from a floppy disk.
    /// </summary>
    /// <returns>1 for A, 2 for B, 3 for C, ...</returns>
    // orig: CODE 0000:3678 __dos_getdrive (SLIBCR.LIB dos\getdrive.asm) lib; also FINAL 0100:ea18, GAME 00f7:fd8a, TAC 00e8:fde6, BUG 00e8:968c, CHASE 00df:57e8
    int CurrentDrive();

    /// <summary>
    /// Asks the PC whether a floppy drive can read its disk: the BIOS's disk service "verify" on one sector at the
    /// start of the disk (the library's <c>_bios_disk</c>). The game asks before it lists or writes saved games on
    /// a floppy disk.
    /// </summary>
    /// <param name="drive">The drive: 0 for A, 1 for B.</param>
    /// <returns>0 when the sector could be read, else the BIOS's error number (0x80: the drive does not answer, no disk).</returns>
    // orig: FINAL 0100:e946 __bios_disk (SLIBCR.LIB dos\b_disk.asm) lib; also GAME 00f7:fcb8
    int VerifyDisk(int drive);

    /// <summary>
    /// Sets the PC's disk drives back to their start state, as the game does after a disk could not be read (the
    /// same library routine with the BIOS's disk service "reset").
    /// </summary>
    void ResetDisks();

    /// <summary>Opens a file (the library's <c>open</c>).</summary>
    /// <param name="name">The file's DOS name.</param>
    /// <param name="mode">How: to read or to write, to create, as text or as bytes.</param>
    /// <param name="permission">The permission bits the original passes when it may create the file; null where it passes none.</param>
    /// <returns>The file's number, or -1 when it cannot be opened.</returns>
    // orig: FINAL 0100:d9e2 open (SLIBCR.LIB dos\open.asm) lib; also GAME 00f7:ed64, TAC 00e8:ea7c, BUG 00e8:87ce
    int Open(string name, OpenMode mode, int? permission = null);

    /// <summary>Reads the next bytes of a file (the library's <c>read</c>).</summary>
    /// <param name="handle">The file's number.</param>
    /// <param name="buffer">Receives the bytes; its length is how many are asked for.</param>
    /// <returns>How many bytes were read (fewer than asked for at the file's end, 0 behind it), or -1 for a number that names no open file.</returns>
    // orig: FINAL 0100:db86 read (SLIBCR.LIB dos\read.asm) lib; also GAME 00f7:ef08, TAC 00e8:ec20, BUG 00e8:8972, CHASE 00df:4e02
    int Read(int handle, Span<byte> buffer);

    /// <summary>
    /// Whether the last read of a file opened as text began with a line feed: the note the library keeps of a line
    /// end cut in two by its reading (the carriage return was the last byte of the read before). Its <c>fseek</c>
    /// counts with it, because such a line feed is one byte of the file where every other is two.
    /// </summary>
    /// <param name="handle">The file's number.</param>
    /// <returns>True when the bytes the last read took from the file began with a line feed; false before the first read.</returns>
    // orig: TAC DS:3d0a _osfile, bit 4 (FCRLF), set and cleared by the library's read (SLIBCR.LIB dos\read.asm)
    bool ReadBeganWithLineFeed(int handle);

    /// <summary>Writes bytes to a file (the library's <c>write</c>). The numbers 1 and 2 are the console, where the original prints its last words before it gives up.</summary>
    /// <param name="handle">The file's number.</param>
    /// <param name="data">The bytes.</param>
    /// <returns>How many bytes were written, or -1.</returns>
    // orig: FINAL 0100:dc64 write (SLIBCR.LIB dos\write.asm) lib; also GAME 00f7:efe6, TAC 00e8:ecfe, BUG 00e8:8a50, CHASE 00df:4ee0
    int Write(int handle, ReadOnlySpan<byte> data);

    /// <summary>Closes a file (the library's <c>close</c>).</summary>
    /// <param name="handle">The file's number.</param>
    /// <returns>0, or -1 for a number that names no open file.</returns>
    // orig: FINAL 0100:d948 close (SLIBCR.LIB dos\close.asm) lib; also GAME 00f7:ecca, TAC 00e8:e9e2, BUG 00e8:87ae
    int Close(int handle);

    /// <summary>Moves to another place in a file (the library's <c>lseek</c>).</summary>
    /// <param name="handle">The file's number.</param>
    /// <param name="offset">How far, in bytes.</param>
    /// <param name="origin">From where: 0 the file's start, 1 the current place, 2 the file's end.</param>
    /// <returns>The new place, counted from the file's start, or -1.</returns>
    // orig: FINAL 0100:d968 lseek (SLIBCR.LIB dos\lseek.asm) lib; also GAME 00f7:ecea, TAC 00e8:ea02
    int Seek(int handle, int offset, int origin);

    /// <summary>Asks whether a file is there and may be used in a given way (the library's <c>access</c>).</summary>
    /// <param name="name">The file's DOS name.</param>
    /// <param name="mode">What for: 0 to be there, 2 to be written, 4 to be read, 6 both.</param>
    /// <returns>0 when it may, -1 when the file is missing or may not be used that way.</returns>
    // orig: FINAL 0100:e900 access (SLIBCR.LIB dos\access.asm) lib
    int Access(string name, int mode);

    // ---- the same through the library's thin DOS calls, which the picture and archive routines use: each answers
    //      with 0 or DOS's error number and gives its result through an "out" value ----

    /// <summary>Opens a file as it is, to read (the library's <c>_dos_open</c>).</summary>
    /// <param name="name">The file's DOS name.</param>
    /// <param name="mode">DOS's access mode: 0 to read.</param>
    /// <param name="handle">Receives the file's number.</param>
    /// <returns>0, or DOS's error number (2: no such file).</returns>
    // orig: FINAL 0100:e9ce __dos_open (SLIBCR.LIB dos\dosopen.asm) lib; also GAME 00f7:fd40, TAC 00e8:fdaa, BUG 00e8:9650, CHASE 00df:579e
    int DosOpen(string name, int mode, out int handle);

    /// <summary>Reads the next bytes of a file (the library's <c>_dos_read</c>).</summary>
    /// <param name="handle">The file's number.</param>
    /// <param name="buffer">Receives the bytes; its length is how many are asked for.</param>
    /// <param name="count">Receives how many bytes were read.</param>
    /// <returns>0, or DOS's error number (6: no such open file).</returns>
    // orig: FINAL 0100:e9e6 __dos_read (SLIBCR.LIB dos\dosread.asm) lib; also GAME 00f7:fd58, TAC 00e8:fdc2, BUG 00e8:9668, CHASE 00df:57b6
    int DosRead(int handle, Span<byte> buffer, out int count);

    /// <summary>Closes a file (the library's <c>_dos_close</c>).</summary>
    /// <param name="handle">The file's number.</param>
    /// <returns>0, or DOS's error number.</returns>
    // orig: FINAL 0100:e9a0 __dos_close asm; also GAME 00f7:fd12, TAC 00e8:fd7c, BUG 00e8:9622, CHASE 00df:5770; checked against asm (int 21h function 3eh on the handle at bp+4)
    int DosClose(int handle);

    /// <summary>Moves to a place in a file, counted from its start (the game's own small routine over DOS).</summary>
    /// <param name="handle">The file's number.</param>
    /// <param name="offset">The place, in bytes from the file's start.</param>
    /// <returns>0, or DOS's error number.</returns>
    // orig: CHASE 00df:485c dos_seek (src/c/chase/chase_00df_485c.c) matched; also FINAL 0100:c6ee, GAME 00f7:da70, TAC 00e8:d788, BUG 00e8:7554; tests: LibraryPicturesTests, LibraryOracleTests
    int DosSeek(int handle, int offset);
}

/// <summary>How a file is opened: the bits of the C library's <c>open</c>.</summary>
[Flags]
public enum OpenMode
{
    /// <summary>To read.</summary>
    Read = 0,

    /// <summary>To write.</summary>
    Write = 1,

    /// <summary>To read and write.</summary>
    ReadWrite = 2,

    /// <summary>Make the file when it is not there.</summary>
    Create = 0x100,

    /// <summary>Empty the file first.</summary>
    Truncate = 0x200,

    /// <summary>As text: a carriage return before a line feed is dropped on reading and added on writing, and Ctrl-Z ends the file.</summary>
    Text = 0x4000,

    /// <summary>As the bytes it has.</summary>
    Binary = 0x8000,
}
