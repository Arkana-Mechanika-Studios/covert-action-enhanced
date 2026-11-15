namespace CovertAction.Platform;

/// <summary>
/// Time as the game's code uses it: the time of day, which only seeds the random numbers, and the display's frames,
/// which are the game's one way of waiting (a blink, a pause, the speed of an animation are all counted in frames).
/// </summary>
/// <remarks>
/// The original has no timer of its own for this. It watches the display's retrace signal: 70 frames a second on the
/// VGA's 320x200 mode. A host's timer keeps that pace; a test's timer answers at once.
/// </remarks>
public interface IGameTimer
{
    /// <summary>The current time in whole seconds since 1 January 1970 (the C library's <c>time</c>).</summary>
    // orig: CODE 0000:32b6 time (SLIBCR.LIB dos\time.asm) lib; also FINAL 0100:e42a, GAME 00f7:f7bc, TAC 00e8:f85c, BUG 00e8:9166, CHASE 00df:5378
    int Time();

    /// <summary>
    /// Waits until the display has shown one more frame: the wait the original does by watching for the next vertical
    /// retrace to begin and to end. The screen as the game has drawn it so far is what the player sees meanwhile.
    /// </summary>
    void WaitForFrame();

    /// <summary>
    /// The PC's own clock: ticks since midnight, 18.2 of them a second (the library's <c>_bios_timeofday</c>, asked
    /// for the count). The picture animations pace their frames by it. A caller that waits for a tick asks again and
    /// again; an implementation lets a little time pass in each call.
    /// </summary>
    /// <param name="ticks">Receives the count.</param>
    /// <returns>Whether midnight has passed since the clock was last asked (the count has started again).</returns>
    // orig: FINAL 0100:e972 __bios_timeofday (SLIBCR.LIB dos\b_timofd.asm) lib; also GAME 00f7:fce4
    bool ClockTicks(out int ticks);
}
