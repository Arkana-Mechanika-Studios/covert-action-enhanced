# Mixing the sound drivers: music on one card, effects on another

A player's option (D-620, off by default): the game's sound commands go to two drivers at once, a **music** driver and
an **effects** driver, each one of the reconstructed drivers (none, PC speaker, Tandy, AdLib, Roland MT-32) on a device
of its own, the two mixed into the host's sound output. This file is the evidence for which command is which, and what
a driver keeps between commands. The code: `Audio.SoundCommands` (the table), `Audio.SplitSoundDriver` (the routing),
`Audio.PcmMixer` (the mix), `Launcher.SoundDriverChoice` (the two drivers); the settings: SETTINGS.md.

## The game's side

Every part reaches the driver through one wrapper, `DoSound(n)`, which calls the `play_sound` thunk only while the
sound option (campaign flag 4, Alt-S) is on: `src/c/game/game_00f7_0337.c`, and its copies in BUG, CHASE, CODE, TAC
(`play_sound(effect, sound_param)` in CHASE, `play_sound(effect, sound_mode, sound_variant)` in TAC). The numbers the
game's code sends (`grep DoSound src/c`):

* **2**: the theme, FINAL's menus and scenes (`final_0100_0056.c`, `175e`, `3a49`), each ended by **1**.
* **39**: the selection screens' tune: TAC's equipment screen (`tac_00e8_79ea.c`), CHASE's car choice
  (`chase_00df_0842.c`), each ended by 1.
* **40-43**: the four vacations' tunes, `DoSound(a + 0x28)` (`final_0100_3bcc.c`).
* **0**: stop everything: the end of BUG, CHASE and TAC, and Alt-S turning sound off.
* **37, 38**: Alt-P's pause and resume (`chase_00df_0dc6.c`, `tac_00e8_0f4c.c`).
* **35, 36**: the aeroplane's take-off and landing (`game_00f7_3791.c`); **26, 27**: the cars' engines in a gear
  (with the gear as second value); **44**: the raid's gunfire (with its level and intensity), every raid tick.
* the other numbers 3-34, 45, 46: one-off effects (doors, shots, the workbench, the wiretaps, CODE's teleprinter 46,
  the option switches); the animations (PAN scripts, `script_play_sound`, `play_scene_sound`) send numbers of their data.

## The drivers' side

The four drivers' command tables (47 words; RSOUND 55) agree on one split:

| Command | ISOUND (speaker) | TSOUND (Tandy) | ASOUND (AdLib) | RSOUND (MT-32) |
|---|---|---|---|---|
| 2, 39-43 | 2: the theme, five times through; 39-43: nothing (025f) | `PlaySong`: every voice (082e, 06a6-0716) | a song: voices 0-6 (10dc, 168f-17e1) | a song: up to 8 voices (0601-0735) |
| 3-36, 45, 46 | one sound on the single tone (noise or sweep) | one or two voices (noise, tone 3, tone 1) | one or two of voices 3-6 | one or two voices |
| 26, 27 / 35, 36 / 44 | gears kept, nothing played / take-off drone / nothing | the engines / plane / gunfire | the engines on channels 7-8 / plane / gunfire on voices 0-2 | channel 8 / plane / gunfire on voices 0-2 |
| 0 | silence (020b) | clear and silence (0618) | engines off, card reset (1832) | channels reset (0acd) |
| 1 | silence | every voice ends at its note's end (094f) | every voice to the ending tune (10a6) | voices 0-6 to the ending tune (05cb) |
| 37, 38 | pause, resume (ticks, speaker) | pause, resume (voices kept) | pause, resume (carriers, noise voices) | pause, resume (channel volumes) |
| 47-54 | beyond the table | beyond the table | beyond the table | restart voice 0-7 (0b13-0b44) |

The songs are the only commands that start every voice (the Tandy driver names them: `PlaySong`); every other starting
command starts one or two voices for one sound. Hence the classification (`SoundCommands.RoleOf`):

| Role | Commands | Sent to |
|---|---|---|
| music | 2, 39, 40, 41, 42, 43 | the music driver |
| effect | 3-36, 44, 45, 46 | the effects driver |
| both | 0, 1, 37, 38; 47 and above, and below 0 | both drivers, the music driver first |

0 and 1 stop "whatever plays" in every driver, and the game sends 1 at the end of a scene whose tune may still be
playing and whose effects may too; pause and resume concern everything. 47-54 restart a voice that may hold either
(no part of the game sends them).

## What a driver keeps between commands

* **A tune that plays**: the voices' places in their tunes, their repeats, slides and bends (`Voice` in ASOUND,
  RSOUND, TSOUND; the noise and sweep voices of ISOUND). They go on at every timer interrupt until 0 or 1, or until a
  command starts another tune on the same voice. In one driver an effect **takes a voice of the song** (AdLib's
  effects on voices 3-6, the theme's on 0-6): that part of the tune is gone until the next song. On the speaker an
  effect replaces the theme outright, or is refused while a theme note of higher priority plays (`_priority`).
* **The timer's rate**: each driver sets its own (ISOUND and ASOUND slow 60.9 Hz or fast 300 Hz while a noise plays;
  RSOUND 60.9 Hz). With two drivers each has its own `ClockedSoundDriver`, so each its own timer: neither speeds up
  or slows the other.
* **The engines, the gunfire, the aeroplane**: gear, glide, gunfire level and its rewritten tune bytes; effects only.
* **The pause**: a paused driver keeps its voices and waits for 38.
* **ISOUND's teleprinter**: any command but 31 ends its ticking. With two drivers the songs (sent to the music driver
  only) no longer end it; no part of the game starts a song while CODE's teleprinter ticks.
* **The wait of command 28** (ISOUND's workbench swap: the game waits until the sound has played): it waits for the
  effects driver alone.

## What the player hears

With the option off (the default) one driver plays everything, as the original: nothing changes, the session proof
and the tests are byte for byte as before. With it on, the six tunes (the theme of the title, the menus and the
debriefing; the selection screens' tune; the four vacations) come from the music card, and everything else from the
effects card: the aeroplane, the cars' engines and gear changes, doors, shots and the raid's gunfire, the workbench,
the wiretaps, CODE's teleprinter. Listen for the tune keeping all its parts when a scene's sound cue plays during it
(in one driver the cue takes one of the tune's voices, or on the speaker the whole tone), and for the effects in the
other card's voice (an MT-32 theme with the AdLib's or the speaker's effects). Both cards are mixed at their own levels
into one output; two loud parts at once may clip. A Roland MT-32 without the player's ROMs plays the AdLib driver in
its place, music or effects alike (D-20).
