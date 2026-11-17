using System.Text;
using CovertAction.Content;
using CovertAction.Core;
using CovertAction.Core.Campaign;
using CovertAction.Drivers;
using CovertAction.Platform;
using CovertAction.Presentation;
using CovertAction.Presentation.Game;

namespace CovertAction.Launcher.Tests;

/// <summary>
/// The filler sentences of the coded messages (docs/cut-content.md CC-11; DIVERGENCES.md D-663), a player option: the code
/// draws one of twelve of TEXT.DTA's sixteen; with the option one of all sixteen. Hand-made data in a temporary folder
/// shows the count; the game's own TEXT.DTA (skipped without it) holds the four the code never draws.
/// </summary>
public sealed class CutContentFillerTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "covert-filler-" + Guid.NewGuid().ToString("N"));
    private readonly List<GameFolderFiles> _files = [];

    public CutContentFillerTests()
    {
        Directory.CreateDirectory(_folder);
        var text = new StringBuilder();
        for (var variant = 0; variant < 16; variant++)
        {
            text.Append($"*FLUF{variant:00}\r\nFiller {variant}.\r\n");
        }
        File.WriteAllText(Path.Combine(_folder, "TEXT.DTA"), text.ToString());
    }

    public void Dispose()
    {
        _files.ForEach(f => f.Dispose());
        Directory.Delete(_folder, true);
    }

    private SortedSet<int> SentencesSeen(OriginalBehaviour behaviour, int draws = 600)
    {
        var files = new GameFolderFiles(new GameFolder(_folder));
        _files.Add(files);
        var world = new Boundaries(OriginalBugFixesTests.Recorder<IGraphicsDriver>.Create(), new SilentSoundDriver(), new NoMouseKeys(), new NoMouseDriver(), null!, files);
        var state = new CampaignState { TextFileName = "TEXT.DTA", ProseFileName = "PROSE.DTA" };
        state.FillerTag.Text = "*FLUF00";
        for (var i = 0; i < state.VariableNames.Length; i++)
        {
            state.VariableNames[i] = "$VAR" + i;
            state.VariableValues[i] = "";
        }
        state.Random.Srand(7);
        var messages = new CampaignScreens(state, world, null, behaviour).Messages;
        var seen = new SortedSet<int>();
        for (var i = 0; i < draws; i++)
        {
            state.Txt.Text = "";
            messages.ReadFiller();
            var line = state.Txt.Text.Trim();
            Assert.StartsWith("Filler ", line, StringComparison.Ordinal);
            seen.Add(int.Parse(line["Filler ".Length..].TrimEnd('.'), System.Globalization.CultureInfo.InvariantCulture));
        }
        return seen;
    }

    [Fact]
    public void TheCodeDrawsOneOfTwelveAndTheOptionOneOfSixteen()
    {
        Assert.False(new GameOptions().CutFillerTexts);
        Assert.False(OriginalBehaviour.Faithful.CutContent.FillerTexts);
        Assert.True(new GameOptions(CutFillerTexts: true).Behaviour.CutContent.FillerTexts);
        Assert.Equal(Enumerable.Range(0, 12), SentencesSeen(OriginalBehaviour.ForPlayer(false, false)));
        Assert.Equal(Enumerable.Range(0, 12), SentencesSeen(OriginalBehaviour.Faithful));
        Assert.Equal(Enumerable.Range(0, 16), SentencesSeen(OriginalBehaviour.ForPlayer(false, false, new CutContentOptions(FillerTexts: true))));
    }

    [SkippableFact]
    public void TheGamesOwnTextFileHoldsTheFourSentencesTheCodeNeverDraws()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "TEXT.DTA")))
            {
                folder = new GameFolder(candidate);
            }
        }
        Skip.If(folder is null, "no game files: set COVERT_GAME_DIR to a folder with the original game");
        var text = Encoding.Latin1.GetString(folder!.Read("TEXT.DTA"));
        foreach (var tag in new[] { "*FLUF12", "*FLUF13", "*FLUF14", "*FLUF15" })
        {
            Assert.Contains(tag + "\r\n", text, StringComparison.Ordinal);
        }
        Assert.Contains("Further communications may follow.", text, StringComparison.Ordinal);
    }

    private sealed class NoMouseKeys : IInputDriver
    {
        public bool NoKeyWaiting() => true;

        public int ReadKey() => 0;

        public int ReadKeyRaw() => 0;

        public void CalibrateJoystick()
        {
        }

        public int ReadJoystickAxes() => 0;

        public int ReadJoystickButtons() => 0xff;

        public bool Kbhit() => false;

        public int Getch() => 0;

        public bool NumLock() => true;

        public void SetNumLock(bool on)
        {
        }
    }
}
