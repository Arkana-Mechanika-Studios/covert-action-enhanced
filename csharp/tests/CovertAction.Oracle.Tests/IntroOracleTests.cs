using CovertAction.Core.Intro;
using CovertAction.Oracle.Tests.Harness;
using CovertAction.Persistence.Dgroup;
using Xunit.Abstractions;

namespace CovertAction.Oracle.Tests;

/// <summary>
/// The set-up program (INTRO) and the launcher (COVERT) against their recorded runs. What the vectors reach: the data
/// segment INTRO ran with, through which the set-up screen takes its texts. What they do not reach: every recorded
/// run of <c>hardware_setup_menu</c> and of COVERT's <c>main</c> ends in the overlay loader, whose request for DOS
/// memory the oracle refuses ("MS-DOS lied to us about how much memory was available"), before the first question;
/// the second test holds that, so that a regeneration that gets further is noticed. Skipped without vectors.
/// </summary>
public sealed class IntroOracleTests(ITestOutputHelper output)
{
    private const string ProgramEnd = "int21.4c", MemoryRequest = "int21.48";

    private static IReadOnlyList<OracleVector> Vectors(string module, string name)
    {
        var vectors = OracleVectors.ForFunction(module, name);
        return vectors.Count > 0 ? vectors : throw new SkipException($"no vectors of {module} {name} in {OracleVectors.Folder}");
    }

    [SkippableFact]
    public void TheLayoutReadsTheSetUpTextsFromTheRecordedDataSegmentAndWritesItBackUnchanged()
    {
        var cases = Vectors("INTRO", "hardware_setup_menu").SelectMany(v => v.Cases).ToList();
        foreach (var c in cases)
        {
            var state = new IntroState();
            IntroDgroup.Layout.Load(c.DgroupIn, state);
            Assert.Equal("NITARX", state.SoundLetters);
            Assert.Equal("EMETC", state.GraphicsLetters);
            Assert.All(state.SoundChoices, text => Assert.StartsWith(") ", text));
            Assert.Equal(") Custom sound driver", state.SoundChoices[^1]);
            Assert.All(state.TitleRows, row => Assert.Equal(80, row.Length));
            Assert.Equal($"{IntroState.ColourChange}O", state.TitleColour);
            Assert.Equal("2. Keyboard only", state.ControlChoices[1]);

            var image = c.DgroupIn.ToArray();
            IntroDgroup.Layout.Store(state, image);
            Assert.True(image.AsSpan().SequenceEqual(c.DgroupIn), $"{c.Id}: storing the texts changed the data segment");
        }
        output.WriteLine($"TALLY INTRO DGROUP (hardware_setup_menu's data): {cases.Count} of {cases.Count} cases read and written back");
    }

    [SkippableTheory]
    [InlineData("INTRO", "hardware_setup_menu")]
    [InlineData("COVERT", "main")]
    public void TheRecordedRunsEndInTheOverlayLoaderBeforeTheSetUpScreen(string module, string name)
    {
        var cases = Vectors(module, name).SelectMany(v => v.Cases).ToList();
        foreach (var c in cases)
        {
            var targets = c.Calls.Select(call => call.Target).ToList();
            Assert.Contains(MemoryRequest, targets);
            Assert.Equal(ProgramEnd, targets[^1]);
            // no video BIOS call: the screen was never drawn
            Assert.DoesNotContain(targets, target => target.StartsWith("int10", StringComparison.Ordinal));
        }
        output.WriteLine($"TALLY {module} {name}: 0 of {cases.Count} cases comparable (all end in the overlay loader's memory failure)");
    }
}
