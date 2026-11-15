using CovertAction.Content;
using CovertAction.Host.Headless;
using CovertAction.Platform;

namespace CovertAction.Launcher.Tests;

/// <summary>The player option "arrow keys walk in a raid" (divergence D-610) on a whole practice raid on the headless host.</summary>
public sealed class RaidArrowKeysTests
{
    private static GameFolder GameFiles()
    {
        var folder = GameFolder.FromEnvironment();
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); folder is null && dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "game", "Covert Action", "Covert");
            if (File.Exists(Path.Combine(candidate, "CODE.EXE")))
            {
                folder = new GameFolder(candidate);
            }
        }
        return folder ?? throw new SkipException("no game files: set COVERT_GAME_DIR to a folder with the original game");
    }

    /// <summary>A practice raid: the equipment screen is left, the first door taken, then the keys given; the screen it ends on.</summary>
    private static byte[] Raid(bool arrowKeysWalk, params int[] keys)
    {
        int[] pause = [HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet, HeadlessHost.NoKeyYet];
        var script = new List<int>([.. pause, HostKeys.Escape, .. pause, HostKeys.Enter, .. Enumerable.Repeat(HeadlessHost.NoKeyYet, 10)]);
        foreach (var key in keys)
        {
            script.Add(key);
            script.AddRange(Enumerable.Repeat(HeadlessHost.NoKeyYet, 6));
        }
        script.AddRange(Enumerable.Repeat(HeadlessHost.NoKeyYet, 20));
        var host = new HeadlessHost([.. script], unixSeconds: 600_000_000);
        new Game(GameFiles(), host, null, new GameOptions(ArrowKeysWalk: arrowKeysWalk)).PlayRaidPractice(0);
        return (byte[])host.Pixels.Clone();
    }

    private const int Grey = HostKeys.GreyArrow, Shift = HostKeys.ShiftHeld, Keypad = HostKeys.Keypad;

    [SkippableFact]
    public void ARaidWalkedWithTheArrowsReachesTheStateOfTheSameRaidWalkedWithTheKeypad()
    {
        var arrows = Raid(true, HostKeys.Right | Grey, HostKeys.Right | Grey, HostKeys.Down | Grey, HostKeys.Left | Grey, HostKeys.Up | Grey, HostKeys.Home | Grey);
        var keypad = Raid(false, HostKeys.Right | Keypad, HostKeys.Right | Keypad, HostKeys.Down | Keypad, HostKeys.Left | Keypad, HostKeys.Up | Keypad, HostKeys.Home | Keypad);
        var still = Raid(false);
        Assert.True(arrows.AsSpan().SequenceEqual(keypad), "the arrow keys did not do what the keypad does");
        Assert.False(arrows.AsSpan().SequenceEqual(still), "the agent did not move");
    }

    [SkippableFact]
    public void ShiftWithAnArrowLungesAsTheGreyArrowsDidAndTheOptionOffChangesNothing()
    {
        // Shift+arrow with the option on, and the plain arrow with it off, are both the original's lunge
        var shifted = Raid(true, HostKeys.Right | Grey | Shift, HostKeys.Down | Grey | Shift);
        var original = Raid(false, HostKeys.Right | Grey, HostKeys.Down | Grey);
        Assert.True(shifted.AsSpan().SequenceEqual(original));
        // and the keypad walks the same whether the option is on or off
        var keypadOn = Raid(true, HostKeys.Right | Keypad, HostKeys.Down | Keypad);
        var keypadOff = Raid(false, HostKeys.Right | Keypad, HostKeys.Down | Keypad);
        Assert.True(keypadOn.AsSpan().SequenceEqual(keypadOff));
    }
}
