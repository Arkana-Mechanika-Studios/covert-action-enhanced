using System.Text;
using CovertAction.Msc;
using CovertAction.Platform;

namespace CovertAction.Presentation.Library;

/// <summary>
/// A text file opened the way the game's text readers open one: the C library's buffered stream (<c>FILE</c>) in text
/// mode, read line by line with <c>fscanf(file, "%[^\n]\n", line)</c>. Only what the readers use is here, but that
/// exactly, because how the reading is cut into calls of the file system shows in the recorded runs: the stream reads
/// 512 bytes at a time, and asks again each time it finds the file's end.
/// </summary>
internal sealed class TextStream
{
    /// <summary>The stream's buffer: the C library's BUFSIZ.</summary>
    private const int BufferSize = 512;

    /// <summary>The permission bits <c>fopen</c> passes to <c>open</c> (read and write for the owner, read for others).</summary>
    private const int Permission = 0x1a4;

    /// <summary>The C library's end-of-file value of <c>getc</c> and <c>fscanf</c>.</summary>
    public const int EndOfFile = -1;

    /// <summary>The size of the readers' line buffer on the stack, end mark included.</summary>
    private const int LineCapacity = 256;

    private readonly IFiles _files;
    private readonly int _handle;
    private readonly byte[] _buffer = new byte[BufferSize];
    private int _count;
    private int _position;

    /// <summary>A character was given back since the buffer was last filled (the stream's own note of it).</summary>
    private bool _gaveBack;

    /// <summary>The last place asked for lay behind the file's end.</summary>
    private bool _behindEnd;

    private TextStream(IFiles files, int handle)
    {
        _files = files;
        _handle = handle;
    }

    /// <summary>Opens a file to read as text (<c>fopen(name, "rt")</c>).</summary>
    /// <param name="files">The game's files.</param>
    /// <param name="name">The file's DOS name.</param>
    /// <returns>The stream, or null when the file cannot be opened.</returns>
    // orig: FINAL 0100:cd80 fopen (SLIBCR.LIB fopen.c) lib; also GAME 00f7:e102, TAC 00e8:de1a, BUG 00e8:7be6
    public static TextStream? Open(IFiles files, string name)
    {
        var handle = files.Open(name, OpenMode.Read | OpenMode.Text, Permission);
        return handle == -1 ? null : new TextStream(files, handle);
    }

    /// <summary>
    /// Reads one line (<c>fscanf(file, "%[^\n]\n", line)</c>): the characters up to the end of the line, then every
    /// blank, tab and line end behind it, so that the next line starts at its first visible character.
    /// </summary>
    /// <param name="line">Receives the line; left as it was when nothing was read.</param>
    /// <returns>1 for a line; 0 for an empty line (only its line end is read, the line is left as it was); <see cref="EndOfFile"/> at the end of the file.</returns>
    /// <exception cref="MemoryOverrunException">The line is longer than the readers' buffer of 256 bytes: the original writes past it.</exception>
    // orig: FINAL 0100:cda8 fscanf (SLIBCR.LIB fscanf.c) lib; also GAME 00f7:e12a, TAC 00e8:de42, BUG 00e8:7c0e
    public int ScanLine(ref string line)
    {
        var c = Getc();
        if (c == EndOfFile)
        {
            return EndOfFile;
        }
        var read = new StringBuilder();
        while (c != EndOfFile && c != '\n')
        {
            read.Append((char)c);
            c = Getc();
        }
        if (read.Length == 0)
        {
            // C: an empty line matches nothing and fscanf answers 0; this library keeps the line end it looked at
            // (the recorded runs that read PROSE.DTA, whose first line is empty, go on with the next line)
            return 0;
        }
        Ungetc(c);
        if (read.Length >= LineCapacity)
        {
            throw new MemoryOverrunException($"a line of {read.Length} characters into a buffer of {LineCapacity} bytes");
        }
        line = read.ToString();
        if (c == EndOfFile)
        {
            // C: fscanf stops going through its format once it has met the file's end: behind a last line without
            // a line end it does not look for the blank space, and so does not ask the file again
            return 1;
        }
        // the format's "\n" skips all blank space
        do
        {
            c = Getc();
        }
        while (c != EndOfFile && IsSpace(c));
        Ungetc(c);
        return 1;
    }

    /// <summary>
    /// Reads one word (<c>fscanf(file, "%32s", word)</c> for a width of 32): skips blank space, then takes the
    /// characters up to the next blank space, at most <paramref name="width"/> of them.
    /// </summary>
    /// <param name="width">The most characters a word may have.</param>
    /// <param name="word">Receives the word; left as it was at the end of the file.</param>
    /// <returns>1 for a word; <see cref="EndOfFile"/> when the file ends before one.</returns>
    // The library's fscanf again (the coordinates are with ScanLine), for the other format the game reads with.
    public int ScanWord(int width, ref string word)
    {
        int c;
        do
        {
            c = Getc();
        }
        while (c != EndOfFile && IsSpace(c));
        if (c == EndOfFile)
        {
            return EndOfFile;
        }
        var read = new StringBuilder();
        while (true)
        {
            read.Append((char)c);
            if (read.Length == width)
            {
                // the width is used up: the character behind the word is not looked at
                break;
            }
            c = Getc();
            if (c == EndOfFile || IsSpace(c))
            {
                Ungetc(c);
                break;
            }
        }
        word = read.ToString();
        return 1;
    }

    /// <summary>
    /// Goes to a place of the file counted from its start (<c>fseek(file, offset, SEEK_SET)</c>) on a stream that is
    /// only read. The library does not simply move the file: it reads the 512-byte block the place lies in and
    /// steps through it to the place, and since the block has lost its carriage returns by then, it counts every
    /// line feed as two bytes of the file.
    /// </summary>
    /// <param name="offset">The place, in bytes of the file from its start.</param>
    /// <returns>0, or -1 when the file cannot be moved.</returns>
    // orig: TAC 00e8:f3cc fseek (SLIBCR.LIB fseek.c) lib
    // Checked against asm. Reconstructed for a stream opened to read as text and a place counted from the file's
    // start, which is all the game asks of it (TAC's read_text_line).
    public int Seek(int offset)
    {
        var gaveBack = _gaveBack;
        _gaveBack = false;
        var place = _files.Seek(_handle, 0, FromCurrent);
        if (place == -1)
        {
            return -1;
        }
        if (_behindEnd || offset > place)
        {
            // a place further on than the file stands: is it inside the file at all?
            var length = _files.Seek(_handle, 0, FromEnd);
            _files.Seek(_handle, place, FromStart);
            _behindEnd = offset > length;
        }
        if (offset < 0)
        {
            return -1;
        }
        if (_behindEnd)
        {
            // behind the file's end there is no block to read: the buffer is emptied and the file moved there
            _count = _position = 0;
            return _files.Seek(_handle, offset, FromStart) == -1 ? -1 : 0;
        }

        var inBlock = offset % BufferSize;
        var block = offset - inBlock;
        var bufferHoldsBlock = _count != _position && !gaveBack && (place - 1) / BufferSize * BufferSize == block;
        if (!bufferHoldsBlock)
        {
            if (_files.Seek(_handle, block, FromStart) == -1)
            {
                return -1;
            }
            _count = _files.Read(_handle, _buffer);
            if (_count == -1)
            {
                _count = 0;
                return -1;
            }
        }

        // C: the block's first line feed is one byte of the file when its carriage return was read before the block
        if (_files.ReadBeganWithLineFeed(_handle) && inBlock != 0)
        {
            inBlock++;
        }
        // C: the original steps on without looking at how many characters the block has: behind a block cut short
        // (the file's end, a Ctrl-Z) it counts what the read left in the buffer
        for (_position = 0; inBlock > 0; inBlock--, _position++)
        {
            if (_buffer[_position] == '\n')
            {
                inBlock--;
            }
        }
        return 0;
    }

    /// <summary>The origins of the library's <c>lseek</c>: the file's start, where it stands, its end.</summary>
    private const int FromStart = 0, FromCurrent = 1, FromEnd = 2;

    /// <summary>Closes the file (<c>fclose</c>).</summary>
    // orig: FINAL 0100:cc94 fclose (SLIBCR.LIB fclose.c) lib; also GAME 00f7:e016, TAC 00e8:dd2e, BUG 00e8:7afa
    public void Close() => _files.Close(_handle);

    /// <summary>The next character, refilling the buffer when it is empty (<c>getc</c> and <c>_filbuf</c>); every read past the end asks the file again.</summary>
    private int Getc()
    {
        if (_position < _count)
        {
            return _buffer[_position++];
        }
        _position = 0;
        _gaveBack = false;
        _count = _files.Read(_handle, _buffer);
        if (_count <= 0)
        {
            _count = 0;
            return EndOfFile;
        }
        return _buffer[_position++];
    }

    /// <summary>Gives the last character back to the buffer (<c>ungetc</c>); nothing at the end of the file.</summary>
    private void Ungetc(int c)
    {
        if (c != EndOfFile)
        {
            _position--;
            _gaveBack = true;
        }
    }

    /// <summary>The C library's <c>isspace</c>: blank, tab, line feed, vertical tab, form feed, carriage return.</summary>
    private static bool IsSpace(int c) => c is ' ' or >= '\t' and <= '\r';
}
