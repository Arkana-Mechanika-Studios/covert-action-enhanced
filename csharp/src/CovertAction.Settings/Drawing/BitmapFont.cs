namespace CovertAction.Settings.Drawing;

/// <summary>
/// The launcher's text: the VGA's 8x16 text-mode font (the one the set-up screen shows), drawn at whole multiples so
/// that its dots stay square. Text is Unicode; it is drawn in code page 437, the font's own, with a few characters
/// put in its place (… as ..., © drawn by the launcher's own glyph). New code.
/// </summary>
internal sealed class BitmapFont
{
    public const int CellWidth = 8;
    public const int CellHeight = 16;

    private readonly byte[] _rows;

    /// <summary>The copyright sign: code page 437 has none (8x16, a row a byte).</summary>
    private static readonly byte[] CopyrightGlyph = [0x00, 0x00, 0x00, 0x3C, 0x42, 0x99, 0xA5, 0xA1, 0xA1, 0xA5, 0x99, 0x42, 0x3C, 0x00, 0x00, 0x00];

    /// <summary>The font: 256 characters of 16 rows, one byte a row (bit 7 the leftmost dot).</summary>
    public BitmapFont(byte[] rows)
    {
        if (rows.Length != 256 * CellHeight)
        {
            throw new ArgumentException("an 8x16 font is 4096 bytes", nameof(rows));
        }
        _rows = rows;
    }

    /// <summary>Unicode text as the font draws it: one code page 437 character (or the copyright sign, 256) each.</summary>
    public static int[] Codes(string text)
    {
        var codes = new List<int>(text.Length);
        foreach (var c in text)
        {
            switch (c)
            {
                case '…':
                    codes.AddRange([(int)'.', '.', '.']);
                    break;
                case '©':
                    codes.Add(256);
                    break;
                default:
                    codes.Add(Code(c));
                    break;
            }
        }
        return [.. codes];
    }

    private static int Code(char c) => c switch
    {
        < '\u007F' and >= ' ' => c,
        '‹' or '◄' => 0x11,
        '›' or '►' => 0x10,
        '■' => 0xFE,
        '•' or '·' => 0xF9,
        '×' => 'x',
        '✓' or '√' => 0xFB,
        '→' => 0x1A,
        '←' => 0x1B,
        '↑' => 0x18,
        '↓' => 0x19,
        'é' => 0x82,
        'è' => 0x8A,
        'ê' => 0x88,
        'à' => 0x85,
        'ç' => 0x87,
        'ü' => 0x81,
        'ö' => 0x94,
        'ä' => 0x84,
        '–' or '—' => '-',
        '‘' or '’' => '\'',
        '“' or '”' => '"',
        _ => '?',
    };

    /// <summary>How wide <paramref name="text"/> is at <paramref name="scale"/>.</summary>
    public static int Measure(string text, int scale = 1) => Codes(text).Length * CellWidth * scale;

    /// <summary>The height of a line at <paramref name="scale"/>.</summary>
    public static int LineHeight(int scale = 1) => CellHeight * scale;

    /// <summary>Draws <paramref name="text"/> with its top left at (x, y); bold: each dot doubled to the right. Returns the x after it.</summary>
    public int Draw(Canvas canvas, string text, int x, int y, uint colour, int scale = 1, bool bold = false)
    {
        foreach (var code in Codes(text))
        {
            for (var row = 0; row < CellHeight; row++)
            {
                var bits = code == 256 ? CopyrightGlyph[row] : _rows[code * CellHeight + row];
                if (bold)
                {
                    bits |= (byte)(bits >> 1);
                }
                for (var column = 0; column < CellWidth; column++)
                {
                    if ((bits & (0x80 >> column)) != 0)
                    {
                        canvas.Fill(new Rect(x + column * scale, y + row * scale, scale, scale), colour);
                    }
                }
            }
            x += CellWidth * scale;
        }
        return x;
    }

    /// <summary>Splits <paramref name="text"/> into lines no wider than <paramref name="width"/> (at spaces; a word longer than a line is broken anywhere).</summary>
    public static List<string> Wrap(string text, int width, int scale = 1)
    {
        var columns = Math.Max(1, width / (CellWidth * scale));
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var raw in paragraph.Split(' '))
            {
                var word = raw;
                while (Codes(word).Length > columns)
                {
                    // a long path: as much as fits on the line, the rest on the next
                    var room = line.Length == 0 ? columns : columns - Codes(line).Length - 1;
                    if (room <= 0)
                    {
                        lines.Add(line);
                        line = "";
                        continue;
                    }
                    line = line.Length == 0 ? word[..room] : line + " " + word[..room];
                    lines.Add(line);
                    line = "";
                    word = word[room..];
                }
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && Codes(candidate).Length > columns)
                {
                    lines.Add(line);
                    line = word;
                }
                else
                {
                    line = candidate;
                }
            }
            lines.Add(line);
        }
        return lines;
    }

    /// <summary>
    /// <paramref name="text"/> cut with "..." to fit <paramref name="width"/>: its end kept (a path: its last folders) when
    /// <paramref name="fromEnd"/>, else its start.
    /// </summary>
    public static string Fit(string text, int width, bool fromEnd, int scale = 1)
    {
        var columns = width / (CellWidth * scale);
        if (Codes(text).Length <= columns)
        {
            return text;
        }
        if (columns <= 3)
        {
            return columns <= 0 ? "" : new string('.', columns);
        }
        // cut by characters (text with "…" or "©" is a little shorter in characters than in codes: cut one more)
        var keep = columns - 3;
        while (keep > 0)
        {
            var cut = fromEnd ? "..." + text[^keep..] : text[..keep] + "...";
            if (Codes(cut).Length <= columns)
            {
                return cut;
            }
            keep--;
        }
        return "...";
    }
}
