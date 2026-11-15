namespace CovertAction.Platform;

/// <summary>
/// The sound driver as the game sees it. The original loads one driver program for the player's sound hardware (PC
/// speaker, Tandy, AdLib, Roland). The game only ever names an effect or tune by number; what it sounds like is the
/// driver's business.
/// </summary>
/// <remarks>The members here are the calls the cryptography part makes; the others are added with their first caller.</remarks>
public interface ISoundDriver
{
    /// <summary>Starts a sound effect or tune.</summary>
    /// <param name="effect">The effect's number (0x13 and 0x14: an option switched off and on; 0x2d: a key; 0x2e: success).</param>
    // orig: CODE 0000:03cf play_sound thunk; also TAC 0000:045b, BUG 0000:045b, GAME 0000:03cf, CHASE 0000:03cf, FINAL 0000:045b
    void PlaySound(int effect);

    /// <summary>Starts a sound effect or tune with a second value, as the car chase calls the same routine (the engine's pitch, for one).</summary>
    /// <param name="effect">The effect's number.</param>
    /// <param name="parameter">The second value.</param>
    void PlaySound(int effect, int parameter);

    /// <summary>Starts a sound effect with two further values, as the building raid calls the same routine.</summary>
    /// <param name="effect">The effect's number.</param>
    /// <param name="mode">The second value.</param>
    /// <param name="variant">The third value.</param>
    void PlaySound(int effect, int mode, int variant);
}
