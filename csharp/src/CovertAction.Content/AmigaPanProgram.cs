using System.Buffers.Binary;

namespace CovertAction.Content;

/// <summary>
/// Turns the program of an Amiga animation (<see cref="AmigaPanFile.Program"/>) into the PC's encoding that
/// <see cref="PanPlayer"/> and the game's own animation routines run, and says whether an Amiga program is a PC program
/// (D-683). The Amiga's programs are the PC's small machine with wider fields (see <see cref="AmigaPanFile"/>), so nothing is
/// reinterpreted: each instruction is written again in the PC's byte form and every address (the start of a sprite's
/// program, a jump, a call, a loop's end) is looked up in the table of where the instructions moved to.
/// </summary>
/// <remarks>
/// A main program is followed by the programs of the sprites, which the main program starts by their address with seven
/// pushed values (where the sprite's program starts, its number, its parent, its place, its speed and its flag). The address
/// is the only thing the main program pushes that is an address, and it is always a constant, so it can be recognised: the
/// seventh push before each start. The lowest such address is where the main program ends. For 11 of the PC's own animations
/// the Amiga's program has, instruction for instruction and address for address, the PC's (<see cref="SameInstructions"/>);
/// it can have a rest or an image 0 more at the end of a part, which nothing reaches.
/// </remarks>
public static class AmigaPanProgram
{
    private const int Push = 5, Store = 6, JumpIf = 18, Jump = 19, Rest = 20, Call = 23, StartSprite = 0;
    private const int SpriteShow = 0, SpriteMoveTo = 1, SpriteMoveBy = 2, SpriteSetSpeed = 3, SpriteChangeSpeed = 4, SpriteRepeat = 5, SpriteLoop = 6, SpriteStop = 10;
    private const int MainOps = 24, StartValues = 7;

    private readonly record struct MainInstruction(int At, int Op, int Flag, int Value);

    private readonly record struct SpriteInstruction(int At, int Op, int First, int Second);

    /// <summary>A program taken apart: the main program's instructions and the sprites' (with their addresses in the program they came from).</summary>
    private sealed record Parts(List<MainInstruction> Main, List<SpriteInstruction> Sprites);

    /// <summary>Translates a program.</summary>
    /// <param name="amiga">The Amiga animation's program.</param>
    /// <returns>The program in the PC's encoding, padded with zeros to a multiple of 16 bytes as the PC's files have it.</returns>
    /// <exception cref="InvalidDataException">The program is not one this translator knows.</exception>
    public static byte[] ToPcProgram(ReadOnlySpan<byte> amiga)
    {
        try
        {
            var parts = Decode(amiga, amigaForm: true);
            // where each instruction goes in the PC's encoding
            var moved = new Dictionary<int, int>();
            var at = 0;
            foreach (var instruction in parts.Main)
            {
                moved[instruction.At] = at;
                at += MainSize(instruction.Op);
            }
            foreach (var instruction in parts.Sprites)
            {
                moved[instruction.At] = at;
                at += SpriteSize(instruction.Op);
            }
            var pc = new byte[(at + 15) & ~15];
            var pushes = new List<int>();
            var put = 0;
            foreach (var instruction in parts.Main)
            {
                pc[put++] = (byte)instruction.Op;
                switch (instruction.Op)
                {
                    case Push:
                        pushes.Add(put);
                        pc[put++] = (byte)instruction.Flag;
                        PutWord(pc, ref put, instruction.Value);
                        break;
                    case Store:
                        PutWord(pc, ref put, instruction.Value);
                        break;
                    case JumpIf or Jump or Call:
                        PutWord(pc, ref put, Moved(moved, instruction.Value));
                        break;
                    case StartSprite:
                        // the address of the sprite's program is the seventh push back
                        var address = pushes[^StartValues];
                        var value = BinaryPrimitives.ReadInt16LittleEndian(pc.AsSpan(address + 1));
                        BinaryPrimitives.WriteInt16LittleEndian(pc.AsSpan(address + 1), (short)Moved(moved, value));
                        break;
                }
            }
            foreach (var instruction in parts.Sprites)
            {
                pc[put++] = (byte)instruction.Op;
                switch (instruction.Op)
                {
                    case SpriteShow:
                        pc[put++] = instruction.First is >= 0 and <= 255 ? (byte)instruction.First : throw new InvalidDataException($"a sprite shows image {instruction.First}");
                        break;
                    case SpriteMoveTo or SpriteMoveBy:
                        PutWord(pc, ref put, instruction.First);
                        PutWord(pc, ref put, instruction.Second);
                        break;
                    case SpriteSetSpeed or SpriteChangeSpeed or SpriteRepeat:
                        PutWord(pc, ref put, instruction.First);
                        break;
                    case SpriteLoop:
                        PutWord(pc, ref put, Moved(moved, instruction.First));
                        break;
                }
            }
            return pc;
        }
        catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new InvalidDataException("the animation's program ends inside an instruction", e);
        }
    }

    /// <summary>
    /// Whether an Amiga program and a PC program are the same: the same instructions with the same values, and every address
    /// (a jump, a call, the start of a sprite's program, a loop's end) leading to the same instruction. A rest after a rest at
    /// the end of the main program, or an image 0 after the last sprite's program, is not an instruction of the program: no
    /// program counter reaches it.
    /// </summary>
    /// <param name="amiga">The Amiga animation's program.</param>
    /// <param name="pc">The PC animation's program.</param>
    /// <returns>Null when they are the same; else what differs.</returns>
    public static string? SameInstructions(ReadOnlySpan<byte> amiga, ReadOnlySpan<byte> pc)
    {
        try
        {
            var a = Normalised(Decode(amiga, amigaForm: true));
            var p = Normalised(Decode(pc, amigaForm: false));
            for (var i = 0; i < Math.Min(a.Count, p.Count); i++)
            {
                if (a[i] != p[i])
                {
                    return $"instruction {i} differs ({a[i]} and {p[i]})";
                }
            }
            return a.Count == p.Count ? null : $"it has {a.Count} instructions, the game's {p.Count}";
        }
        catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            return $"a program that cannot be read ({e.Message})";
        }
    }

    /// <summary>The instructions with each address replaced by the number of the instruction it leads to, and the padding left out.</summary>
    private static List<(string Part, int Op, int A, int B, int C)> Normalised(Parts parts)
    {
        var main = parts.Main;
        // a rest after a rest ends nothing that any jump reaches; an image 0 at the very end is padding
        while (main.Count > 1 && main[^1].Op == Rest && main[^2].Op == Rest && main.All(instruction => instruction.Op is not (JumpIf or Jump or Call) || instruction.Value != main[^1].At))
        {
            main.RemoveAt(main.Count - 1);
        }
        var mainAt = main.Select((instruction, index) => (instruction.At, index)).ToDictionary(pair => pair.At, pair => pair.index);
        var spriteAt = parts.Sprites.Select((instruction, index) => (instruction.At, index)).ToDictionary(pair => pair.At, pair => pair.index);
        // the sprites' programs the main program starts, in the order it first starts them; a program no start names is dead code
        var starts = new List<int>();
        var numbered = new Dictionary<int, int>();
        var pushed = new List<MainInstruction>();
        foreach (var instruction in main)
        {
            if (instruction.Op == Push)
            {
                pushed.Add(instruction);
            }
            else if (instruction.Op == StartSprite && pushed.Count >= StartValues && !numbered.ContainsKey(pushed[^StartValues].Value))
            {
                var address = pushed[^StartValues].Value;
                numbered[address] = starts.Count;
                starts.Add(spriteAt.GetValueOrDefault(address, -1));
            }
        }
        var result = new List<(string, int, int, int, int)>();
        var pushes = new List<int>();
        var pushedValues = new List<int>();
        foreach (var instruction in main)
        {
            var (a, b) = (instruction.Flag, instruction.Value);
            switch (instruction.Op)
            {
                case JumpIf or Jump or Call:
                    (a, b) = (0, mainAt.GetValueOrDefault(instruction.Value, -1));
                    break;
                case Push:
                    pushes.Add(result.Count);
                    pushedValues.Add(instruction.Value);
                    break;
                case StartSprite:
                    // the address of the program is replaced by the number of the program
                    var start = result[pushes[^StartValues]];
                    result[pushes[^StartValues]] = (start.Item1, start.Item2, start.Item3, numbered[pushedValues[^StartValues]], 1);
                    break;
            }
            result.Add(("main", instruction.Op, a, b, 0));
        }
        for (var program = 0; program < starts.Count; program++)
        {
            for (var i = starts[program]; i >= 0 && i < parts.Sprites.Count; i++)
            {
                var instruction = parts.Sprites[i];
                var first = instruction.Op == SpriteLoop ? spriteAt.GetValueOrDefault(instruction.First, -1) - starts[program] : instruction.First;
                result.Add(($"sprites {program}", instruction.Op, first, instruction.Second, 0));
                // each program ends where its sprite stops, rewinds, starts over or pauses for good
                if (instruction.Op is SpriteStop or 7 or 8 or 9)
                {
                    break;
                }
            }
        }
        return result;
    }


    private static int Moved(Dictionary<int, int> moved, int address) =>
        moved.TryGetValue(address, out var to) ? to : throw new InvalidDataException($"the address {address} of the animation's program is not the start of an instruction");

    private static void PutWord(byte[] program, ref int at, int value)
    {
        BinaryPrimitives.WriteInt16LittleEndian(program.AsSpan(at), (short)value);
        at += 2;
    }

    private static int MainSize(int op) => op switch
    {
        Push => 4,
        Store or JumpIf or Jump or Call => 3,
        _ => 1,
    };

    private static int SpriteSize(int op) => op switch
    {
        SpriteShow => 2,
        SpriteSetSpeed or SpriteChangeSpeed or SpriteRepeat or SpriteLoop => 3,
        SpriteMoveTo or SpriteMoveBy => 5,
        _ => 1,
    };

    // ---- taking a program apart, in either form ----

    /// <summary>Reads the fields of a program: the Amiga's (words, big-endian but for the main program's addresses) or the PC's (bytes, little-endian).</summary>
    private readonly ref struct Reader(ReadOnlySpan<byte> program, bool amigaForm)
    {
        private readonly ReadOnlySpan<byte> _program = program;

        public int Length => _program.Length;

        /// <summary>An operation, and how many bytes it took.</summary>
        public int Op(int at, out int size)
        {
            size = amigaForm ? 2 : 1;
            return amigaForm ? BinaryPrimitives.ReadUInt16BigEndian(_program[at..]) : _program[at];
        }

        /// <summary>The flag of a push, and how many bytes it took.</summary>
        public int Flag(int at, out int size)
        {
            size = amigaForm ? 2 : 1;
            return amigaForm ? BinaryPrimitives.ReadUInt16BigEndian(_program[at..]) : _program[at];
        }

        /// <summary>A signed value of a push, a store, a sprite's move, speed or repeat.</summary>
        public int Value(int at) => amigaForm ? BinaryPrimitives.ReadInt16BigEndian(_program[at..]) : BinaryPrimitives.ReadInt16LittleEndian(_program[at..]);

        /// <summary>An address of the main program: little-endian in both forms.</summary>
        public int MainAddress(int at) => BinaryPrimitives.ReadUInt16LittleEndian(_program[at..]);

        /// <summary>The end of a sprite's loop, or the number of an image: in the form's own order, unsigned.</summary>
        public int Unsigned(int at) => amigaForm ? BinaryPrimitives.ReadUInt16BigEndian(_program[at..]) : BinaryPrimitives.ReadUInt16LittleEndian(_program[at..]);

        /// <summary>An image number: a word of the Amiga's, a byte of the PC's.</summary>
        public int Image(int at, out int size)
        {
            size = amigaForm ? 2 : 1;
            return amigaForm ? BinaryPrimitives.ReadUInt16BigEndian(_program[at..]) : _program[at];
        }
    }

    private static Parts Decode(ReadOnlySpan<byte> program, bool amigaForm)
    {
        var reader = new Reader(program, amigaForm);
        var main = DecodeMain(reader, out var mainEnd);
        return new Parts(main, DecodeSprites(reader, mainEnd));
    }

    /// <summary>The main program: instructions from the start up to the lowest address a sprite is started at.</summary>
    private static List<MainInstruction> DecodeMain(Reader reader, out int mainEnd)
    {
        var instructions = new List<MainInstruction>();
        var pushes = new List<int>();
        mainEnd = reader.Length;
        var pc = 0;
        while (pc < mainEnd)
        {
            var at = pc;
            var op = reader.Op(pc, out var size);
            pc += size;
            if (op >= MainOps)
            {
                throw new InvalidDataException($"an unknown instruction {op} at {at} of the animation's program");
            }
            int flag = 0, value = 0;
            switch (op)
            {
                case Push:
                    flag = reader.Flag(pc, out size);
                    pc += size;
                    value = reader.Value(pc);
                    pc += 2;
                    pushes.Add(instructions.Count);
                    break;
                case Store:
                    value = reader.Value(pc);
                    pc += 2;
                    break;
                case JumpIf or Jump or Call:
                    value = reader.MainAddress(pc);
                    pc += 2;
                    break;
                case StartSprite:
                    if (pushes.Count < StartValues || instructions[pushes[^StartValues]].Flag != 0)
                    {
                        throw new InvalidDataException("a sprite is started without the address of its program among the seven values");
                    }
                    mainEnd = Math.Min(mainEnd, instructions[pushes[^StartValues]].Value);
                    break;
            }
            instructions.Add(new MainInstruction(at, op, flag, value));
        }
        if (pc != mainEnd)
        {
            throw new InvalidDataException($"the main program ends at {pc}, not where the first sprite's program starts ({mainEnd})");
        }
        return instructions;
    }

    /// <summary>The sprites' programs: instructions up to the first thing that is not one (the padding at the end of the program).</summary>
    private static List<SpriteInstruction> DecodeSprites(Reader reader, int from)
    {
        var instructions = new List<SpriteInstruction>();
        var pc = from;
        while (pc < reader.Length)
        {
            try
            {
                var at = pc;
                var op = reader.Op(pc, out var size);
                if (op > SpriteStop)
                {
                    break;
                }
                var next = pc + size;
                int first = 0, second = 0;
                switch (op)
                {
                    case SpriteShow:
                        first = reader.Image(next, out size);
                        next += size;
                        break;
                    case SpriteMoveTo or SpriteMoveBy:
                        first = reader.Value(next);
                        second = reader.Value(next + 2);
                        next += 4;
                        break;
                    case SpriteSetSpeed or SpriteChangeSpeed or SpriteRepeat:
                        first = reader.Value(next);
                        next += 2;
                        break;
                    case SpriteLoop:
                        first = reader.Unsigned(next);
                        next += 2;
                        break;
                }
                if (op == SpriteShow && first > 255)
                {
                    // an image number is a byte: this is the padding at the end (a word like 0x2000 after the last program)
                    break;
                }
                instructions.Add(new SpriteInstruction(at, op, first, second));
                pc = next;
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentOutOfRangeException)
            {
                // the padding ends inside an instruction
                break;
            }
        }
        // zeros after the last program read as "show image 0": padding, which no sprite reaches
        while (instructions.Count > 0 && instructions[^1] is { Op: SpriteShow, First: 0 })
        {
            instructions.RemoveAt(instructions.Count - 1);
        }
        return instructions;
    }
}
