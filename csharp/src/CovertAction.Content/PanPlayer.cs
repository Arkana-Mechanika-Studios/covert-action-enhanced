namespace CovertAction.Content;

/// <summary>
/// Plays an animation frame by frame: runs its program, moves its sprites, and keeps the picture of the current
/// frame. This is the data-level player: it shows what an animation file contains, as the project's verified
/// reference player does, and is proven on every animation of the game against that player's frames. The game's
/// own animation routines, with their sound requests and the values the game hands in, are in the launcher's library.
/// </summary>
/// <remarks>
/// <para>
/// An animation is a small machine. Its main program works on a stack of numbers and 51 registers; it starts and
/// stops up to 50 sprites and says when a frame is over. Each sprite runs a little program of its own (show this
/// image, move there, repeat) at its own speed. A sprite can be "stamped": drawn onto the background for good.
/// </para>
/// <para>
/// A frame: the program runs until it rests; every sprite takes its step; the stamped sprites go onto the
/// background; the rows that sprites covered in the last frame are put back from the background; the sprites are
/// drawn.
/// </para>
/// </remarks>
public sealed class PanPlayer
{
    private const int Sprites = 50;
    private const int Registers = 51;
    private const int StackDepth = 256;
    private const int LoopDepth = 10;
    private const int NoImage = 255;
    private const int NoParent = -1;

    /// <summary>What a sprite's speed is counted against: a sprite takes a step whenever its speed has added up to more than this.</summary>
    private const int StepCost = 255;

    /// <summary>The most instructions run for one frame: a program that does not come to rest is damaged.</summary>
    private const int InstructionBudget = 10000;

    /// <summary>The operations of the main program.</summary>
    private enum Op
    {
        StartSprite = 0, StopSprite = 1, Wait = 2, Sound = 3, Stamp = 4, Push = 5, Store = 6, Duplicate = 7,
        Equal = 8, NotEqual = 9, Greater = 10, Less = 11, GreaterOrEqual = 12, LessOrEqual = 13,
        Add = 14, Subtract = 15, Multiply = 16, Divide = 17,
        JumpIf = 18, Jump = 19, Rest = 20, End = 21, Return = 22, Call = 23,
    }

    /// <summary>The operations of a sprite's program.</summary>
    private enum SpriteOp
    {
        Show = 0, MoveTo = 1, MoveBy = 2, SetSpeed = 3, ChangeSpeed = 4, Repeat = 5, Loop = 6, StartOver = 7, Rewind = 8, Pause = 9, Stop = 10,
    }

    private sealed class Sprite
    {
        public bool Active, Paused, Stamped, Untracked;
        public int Image = NoImage, Parent = NoParent;
        public short X, Y, HomeX, HomeY, Speed, StartSpeed, Credit;
        public int Pc, Start;
        public readonly Stack<short> Loops = new();
    }

    private readonly PanFile _file;
    private readonly byte[] _background;
    private readonly Sprite[] _sprites = new Sprite[Sprites + 1];   // sprite numbers start at 1
    private readonly Stack<short> _stack = new();
    private readonly short[] _registers = new short[Registers];
    private readonly int[] _coveredFrom;
    private readonly int[] _coveredTo;
    private int _pc;
    private short _wait;

    /// <summary>Gets an animation ready to play: the first frame is its background.</summary>
    /// <param name="file">The animation.</param>
    public PanPlayer(PanFile file)
    {
        _file = file;
        _background = (byte[])file.Background.Clone();
        Frame = (byte[])file.Background.Clone();
        _coveredFrom = new int[file.Height];
        _coveredTo = new int[file.Height];
        Array.Fill(_coveredFrom, file.Width);
        Array.Fill(_coveredTo, -1);
        for (var i = 0; i < _sprites.Length; i++)
        {
            _sprites[i] = new Sprite();
        }
    }

    /// <summary>Sets one of the program's variables before it runs (D-684): the game sets variable 0 for the scenes that show a man or a woman.</summary>
    /// <param name="variable">The variable's number, 0 to 50.</param>
    /// <param name="value">Its value.</param>
    public void SetVariable(int variable, int value) => _registers[Checked(variable, Registers, "variable")] = (short)value;

    /// <summary>The picture of the current frame: one palette index per pixel, row by row.</summary>
    public byte[] Frame { get; }

    /// <summary>Whether the program has ended.</summary>
    public bool Ended { get; private set; }

    /// <summary>How many sounds the program has asked for so far.</summary>
    public int SoundRequests { get; private set; }

    /// <summary>Plays one frame.</summary>
    /// <returns>Whether a frame was drawn; false once the program has ended.</returns>
    /// <exception cref="InvalidDataException">The program is damaged.</exception>
    public bool Tick()
    {
        if (Ended)
        {
            return false;
        }
        foreach (var sprite in _sprites)
        {
            sprite.Stamped = false;
        }
        RunProgram();
        if (Ended)
        {
            return false;
        }
        foreach (var sprite in _sprites.Where(s => s.Active))
        {
            Step(sprite);
        }
        foreach (var sprite in _sprites.Where(s => s.Active && s.Stamped))
        {
            Draw(sprite, _background);
        }
        RestoreCoveredRows();
        foreach (var sprite in _sprites.Where(s => s.Active && !s.Stamped))
        {
            Draw(sprite, Frame);
        }
        return true;
    }

    // ---- the main program ----

    private void RunProgram()
    {
        for (var budget = 0; budget < InstructionBudget; budget++)
        {
            if (_wait != 0)
            {
                _wait--;
                return;
            }
            var op = (Op)Byte(ref _pc);
            switch (op)
            {
                case Op.StartSprite:
                    StartSprite();
                    break;
                case Op.StopSprite:
                    if (SpriteNumbered(Pop()) is { } stopped)
                    {
                        stopped.Active = false;
                    }
                    break;
                case Op.Wait:
                    _wait = Pop();
                    break;
                case Op.Sound:
                    Pop();
                    SoundRequests++;
                    break;
                case Op.Stamp:
                    if (SpriteNumbered(Pop()) is { } stamped)
                    {
                        stamped.Stamped = true;
                    }
                    break;
                case Op.Push:
                    var fromRegister = Byte(ref _pc) != 0;
                    var operand = (short)Word(ref _pc);
                    Push(fromRegister ? _registers[Checked(operand, Registers, "register")] : operand);
                    break;
                case Op.Store:
                    var register = (short)Word(ref _pc);
                    var stored = Pop();
                    if (register is >= 0 and < Registers)
                    {
                        _registers[register] = stored;
                    }
                    break;
                case Op.Duplicate:
                    Push(_stack.Count > 0 ? _stack.Peek() : throw Damaged("an empty stack"));
                    break;
                case >= Op.Equal and <= Op.Divide:
                    var right = Pop();
                    var left = Pop();
                    Push(Calculate(op, left, right));
                    break;
                case Op.JumpIf:
                    var target = Word(ref _pc);
                    if (Pop() != 0)
                    {
                        _pc = target;
                    }
                    break;
                case Op.Jump:
                    _pc = Word(ref _pc);
                    break;
                case Op.Rest:
                    _pc--;      // the program stays on this instruction: every later frame rests here too
                    return;
                case Op.End:
                    Ended = true;
                    return;
                case Op.Return:
                    _pc = (ushort)Pop();
                    break;
                case Op.Call:
                    var routine = Word(ref _pc);
                    Push(_pc);
                    _pc = routine;
                    break;
                default:
                    throw Damaged($"an unknown instruction {(int)op}");
            }
        }
        throw Damaged("a program that never ends its frame");
    }

    /// <summary>Starts a sprite with the seven values on the stack: where its program starts, its number (-1: the first free one), its parent, its place, its speed and whether it leaves the background alone.</summary>
    private void StartSprite()
    {
        int flags = Pop(), speed = Pop(), y = Pop(), x = Pop(), parent = Pop(), number = Pop();
        var start = (ushort)Pop();
        if (number == -1)
        {
            number = Enumerable.Range(1, Sprites).FirstOrDefault(n => !_sprites[n].Active, Sprites);
        }
        if (number is < 1 or > Sprites)
        {
            return;
        }
        var alone = parent == NoParent;
        _sprites[number] = new Sprite
        {
            Active = true, Parent = parent, Untracked = flags != 0,
            // a sprite with a parent keeps its own place as an offset from the parent's
            X = (short)(alone ? x : 0), Y = (short)(alone ? y : 0), HomeX = (short)x, HomeY = (short)y,
            Speed = (short)speed, StartSpeed = (short)speed, Credit = StepCost, Pc = start, Start = start,
        };
    }

    private short Calculate(Op op, int left, int right) => (short)(op switch
    {
        Op.Equal => left == right ? 1 : 0,
        Op.NotEqual => left != right ? 1 : 0,
        Op.Greater => left > right ? 1 : 0,
        Op.Less => left < right ? 1 : 0,
        Op.GreaterOrEqual => left >= right ? 1 : 0,
        Op.LessOrEqual => left <= right ? 1 : 0,
        Op.Add => left + right,
        Op.Subtract => left - right,
        Op.Multiply => left * right,
        _ => right != 0 ? left / right : throw Damaged("a division by 0"),
    });

    // ---- sprites ----

    /// <summary>Lets a sprite's program run up to its next image, when the sprite's speed has earned it a step.</summary>
    private void Step(Sprite sprite)
    {
        if (sprite.Paused)
        {
            return;
        }
        sprite.Credit += sprite.Speed;
        if (sprite.Credit <= StepCost)
        {
            return;
        }
        sprite.Credit -= StepCost;
        for (var budget = 0; budget < InstructionBudget; budget++)
        {
            var op = (SpriteOp)Byte(ref sprite.Pc);
            switch (op)
            {
                case SpriteOp.Show:
                    sprite.Image = Byte(ref sprite.Pc);
                    return;
                case SpriteOp.MoveTo:
                    sprite.X = (short)Word(ref sprite.Pc);
                    sprite.Y = (short)Word(ref sprite.Pc);
                    break;
                case SpriteOp.MoveBy:
                    sprite.X += (short)Word(ref sprite.Pc);
                    sprite.Y += (short)Word(ref sprite.Pc);
                    break;
                case SpriteOp.SetSpeed:
                    sprite.Speed = (short)Word(ref sprite.Pc);
                    break;
                case SpriteOp.ChangeSpeed:
                    sprite.Speed += (short)Word(ref sprite.Pc);
                    break;
                case SpriteOp.Repeat:
                    if (sprite.Loops.Count == LoopDepth)
                    {
                        throw Damaged("loops nested too deep");
                    }
                    sprite.Loops.Push((short)Word(ref sprite.Pc));
                    break;
                case SpriteOp.Loop:
                    var again = Word(ref sprite.Pc);
                    if (sprite.Loops.Count == 0)
                    {
                        throw Damaged("a loop's end without its start");
                    }
                    var left = (short)(sprite.Loops.Pop() - 1);
                    if (left != 0)
                    {
                        sprite.Loops.Push(left);
                        sprite.Pc = again;
                    }
                    break;
                case SpriteOp.StartOver:
                    var alone = sprite.Parent == NoParent;
                    (sprite.X, sprite.Y) = (alone ? sprite.HomeX : (short)0, alone ? sprite.HomeY : (short)0);
                    sprite.Speed = sprite.StartSpeed;
                    sprite.Credit = StepCost;
                    goto case SpriteOp.Rewind;
                case SpriteOp.Rewind:
                    sprite.Pc = sprite.Start;
                    sprite.Loops.Clear();
                    break;
                case SpriteOp.Pause:
                    sprite.Paused = true;
                    return;
                case SpriteOp.Stop:
                    sprite.Active = false;
                    return;
                default:
                    throw Damaged($"an unknown sprite instruction {(int)op}");
            }
        }
        throw Damaged("a sprite program that never shows an image");
    }

    /// <summary>Draws a sprite's image on a page; its pixels of colour 0 are see-through. A sprite with a parent is placed relative to it and not drawn while the parent is gone.</summary>
    private void Draw(Sprite sprite, byte[] page)
    {
        if (sprite.Image == NoImage)
        {
            return;
        }
        var image = sprite.Image < PanFile.ImagePlaces && _file.Images[sprite.Image] is { } found ? found : throw Damaged($"image {sprite.Image}, which the file does not have");
        int x = sprite.X, y = sprite.Y;
        if (sprite.Parent != NoParent)
        {
            if (SpriteNumbered(sprite.Parent) is not { Active: true } parent)
            {
                return;
            }
            x += sprite.HomeX + parent.X;
            y += sprite.HomeY + parent.Y;
        }
        // what a sprite covers is put back from the background in the next frame, unless the sprite asked not to be
        // tracked; a stamp is always tracked, so that the stamped background shows in this very frame
        var tracked = ReferenceEquals(page, _background) || !sprite.Untracked;
        for (var row = 0; row < image.Height; row++)
        {
            var pageRow = y + row;
            if (pageRow < 0 || pageRow >= _file.Height)
            {
                continue;
            }
            if (tracked)
            {
                int from = Math.Max(x, 0), to = Math.Min(x + image.Width - 1, _file.Width - 1);
                if (from <= to)
                {
                    _coveredFrom[pageRow] = Math.Min(_coveredFrom[pageRow], from);
                    _coveredTo[pageRow] = Math.Max(_coveredTo[pageRow], to);
                }
            }
            for (var column = 0; column < image.Width; column++)
            {
                var pageColumn = x + column;
                var colour = image.Pixels[row * image.Width + column];
                if (colour != 0 && pageColumn >= 0 && pageColumn < _file.Width)
                {
                    page[pageRow * _file.Width + pageColumn] = colour;
                }
            }
        }
    }

    /// <summary>Puts the parts of the rows that sprites covered back from the background, and forgets them.</summary>
    private void RestoreCoveredRows()
    {
        for (var row = 0; row < _file.Height; row++)
        {
            if (_coveredFrom[row] <= _coveredTo[row])
            {
                var at = row * _file.Width + _coveredFrom[row];
                Array.Copy(_background, at, Frame, at, _coveredTo[row] - _coveredFrom[row] + 1);
            }
            _coveredFrom[row] = _file.Width;
            _coveredTo[row] = -1;
        }
    }

    private Sprite? SpriteNumbered(int number) => number is >= 1 and <= Sprites ? _sprites[number] : null;

    // ---- the program's bytes and the stack ----

    private int Byte(ref int pc) => pc < _file.Program.Length ? _file.Program[pc++] : throw Damaged("a program that runs past its end");

    private int Word(ref int pc)
    {
        var low = Byte(ref pc);
        return low | Byte(ref pc) << 8;
    }

    private short Pop() => _stack.Count > 0 ? _stack.Pop() : throw Damaged("an empty stack");

    private void Push(int value)
    {
        if (_stack.Count == StackDepth)
        {
            throw Damaged("a full stack");
        }
        _stack.Push((short)value);
    }

    private static int Checked(int value, int count, string what) => value >= 0 && value < count ? value : throw Damaged($"{what} {value}");

    private static InvalidDataException Damaged(string what) => new("the animation's program is damaged: " + what);
}
