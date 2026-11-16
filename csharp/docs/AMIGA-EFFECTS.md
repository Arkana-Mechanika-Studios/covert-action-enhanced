# The Amiga version's sound effects (D-641)

A player's option, off by default: the sound effects of the Amiga version of Covert Action, played from the player's own
Amiga files, in the effects slot of the split drivers (D-620, SOUND-MIXING.md), built as the Amiga music (AMIGA-MUSIC.md,
D-640). The code: `Audio.Amiga.AmigaSoundBank` (a bank file), `AmigaEffectMap` (the table below), `AmigaEffectsDriver` (the
Paula voices), `AmigaEffectsRouter` (which command goes where), `Launcher.AmigaFolderSearch` (where the files are),
`Launcher.SoundDriverChoice` (the effects letter `G`, the part's bank); the settings: SETTINGS.md (`sound.effects` `amiga`,
`sound.amigaFiles`, `sound.amigaOriginalOnly`, shared with the music). No Amiga file, sample or rendering is in the repository
or shipped: the player's folder is read when the game starts.

## The files

Five sound banks, one per part of the game: `gamesnd.dat`, `tacsnd.dat`, `codesnd.dat`, `bugsnd.dat` (disk 2) and
`chasesnd.dat` (disk 3). All big-endian: a count of effects A and of samples B (words); A records of 10 bytes (the distance
from the record to its left and its right sample record, and a priority word); B sample records of 8 bytes (a Paula period,
the distance from the record to the sample's data, and a repeat word); the data, a 32-bit length of which the game uses the
low word, and that many signed 8-bit bytes. Each file ends exactly at its last sample.

| bank | effects | samples | part of the game | Amiga hunks of the program `game` |
|---|---|---|---|---|
| `gamesnd.dat` | 4 | 6 | the campaign (GAME) | 70 to 114 |
| `tacsnd.dat` | 14 | 14 | the building raid (TAC) | 25 to 45 |
| `codesnd.dat` | 4 | 4 | the cryptography workstation (CODE) | 4 to 9 |
| `bugsnd.dat` | 3 | 3 | the electronics workbench (BUG) | 10 to 15 |
| `chasesnd.dat` | 9 | 11 | the car chase (CHASE) | 16 to 24 |

The Amiga's dispatcher loads one bank when it starts a part (the numbers 1 TAC, 2 CODE, 3 BUG, 4 CHASE; the campaign loads
number 0) and unloads it at the end; FINAL (the menus part) loads none and has no sound call. So a number means what the
part's bank says: the PC game's command numbers are one table for all parts, and 46 is a different sound in CODE and BUG.
The launcher tells the driver the part before each part starts.

## What the Amiga's `DoSound(n, volume, x, pitch offset)` does

Read from the program (the routine at hunk 132+0x30d0, its wrapper at hunk 1+0x724):

* `n` is the effect's number in the part's bank; -1 stops everything. A number the bank does not have is ignored.
* **The effect record's word is its priority, not a volume.** The volume is the call's second argument, 0 to 64, straight to
  the voice. (The first plan of this option took the record's word, 40 to 100, for a volume; the program only ever compares it.)
* The pan position `x`, 0 to 319, picks a divisor for each side by its twentieth: from the middle to the left edge the left
  side keeps its volume and the right side's is divided by 1, 1.33, 1.6, 2, 2.7, 4, 8 and 64 (nothing), and the other way
  round. The side's volume is `(volume * 256) / divisor`.
* An effect is two samples: the first for a left voice, the second for a right voice (mostly the same sample, sometimes
  two periods 224 and 254, or 450 and 501, a detuned pair). Both play at the sample's period plus the call's offset (0 in
  every call).
* The four voices are taken in pairs: left voices are Paula's channels 0 and 3, right voices 1 and 2. A side takes its first
  voice if it is free, else its second if that is free, else the one of the two with the lower priority (the second when they
  are equal), if that priority is not above the new effect's; else that side is not played.
* The repeat word counts the interrupts a voice gets while it plays: one when its DMA starts and one at the end of each pass;
  the voice stops at 0. So a word of 2 is one pass, 3 two passes, and 0 plays until stopped. Every effect used by a matched
  call has the word 2 or 3, except `bug[1]` (the alarm), which plays until the module stops it. This count is an inference from
  the program and the data (no word is 1), not read off a running Amiga.

The driver renders four voices at the device's rate with linear interpolation (Paula holds each byte for its period) and the
A500's low-pass filter of the music, a level of 0.75 for a full voice, and no cross-mix.

## The table

Each row is a PC command of a part of the game against the Amiga's call for the same event. *matched*: the same routine, the
same condition, the same moment (the Amiga call found by the program's hunk and offset, the C# routine named). *probable*:
the same event, played differently. *none*: the Amiga game has no call. The volume is the Amiga call's (0 to 64) and the pan
position its `x` (160 is the middle; only the missed player is panned). The names of the routines are the C# game's.

| Part | PC command | Event | Match | Amiga effect | Volume | Evidence |
|---|---|---|---|---|---|---|
| TAC | 3 | the player is hit | matched | tac[3] | 63 | h28+0x2712; `BuildingRaid.Shoot`: a hit on the player |
| TAC | 4 | a guard is hit | matched | tac[3] | 30 | h28+0x273a: the same effect, at half volume, when the shot is not the Uzi's |
| TAC | 5 | empty pistol | matched | tac[9] | 30 | h28+0x1e3e; `ShootingFrame`: no muzzle flash and no bullets |
| TAC | 6 | Uzi shot | matched | tac[0] | 30 | h28+0x273a, 0x277c: hit or miss at a guard with the Uzi |
| TAC | 7 | blast in the player's room | matched | tac[11] | 63 | h28+0x2c2c; `Blast` (`blastRoom == InRoom`) |
| TAC | 8 | blast in another room | matched | tac[11] | 25 | the same call, the other volume |
| TAC | 9 | camera | matched | tac[7] | 63 | h31+0x6aa; `TakePhoto` |
| TAC | 10 | bug planted | matched | tac[10] | 63 | h31+0x870; `PlantBug` |
| TAC | 11 | trap laid | none | | | no call at the trap menu's choice |
| TAC | 12 | grenade thrown | matched | tac[5] | 63 | h28+0x2914, just before "Set trap"; `FireProjectile` |
| TAC | 13 | a door opens | matched | tac[1] | 63 | h31+0x36c (the player), h37+0x588 (`RedrawCell`) |
| TAC | 14 | a door closes | matched | tac[2] | 63 | h31+0x324, h37+0x588 |
| TAC | 16 | a cupboard or drawer opens | matched | tac[13] | 40 | h31+0x384; `UsePiece`, a piece that is not a door |
| TAC | 17 | a cupboard or drawer closes | matched | tac[12] | 40 | h31+0x33c |
| TAC | 18 | siren | matched | tac[6] | 50 | h28+0x9a4; `CountDownAlert` (every 16 frames, alert above 200) |
| TAC | 33 | the player is missed | matched | tac[4] | 40, x 100 | h28+0x2756 |
| TAC | 34 | a guard is missed | matched | tac[4] | 30 | h28+0x277c, 0x27cc (also a shot at nobody, `AimAndFire`) |
| TAC | 44 | gunfire, every frame | none | | | the Amiga plays tac[3] at each muzzle flash (h28+0x1de8); the PC sends the raid's mode every frame |
| CHASE | 22 | first gear | matched | chase[5] | 63 | h22+0x874 (frame 32), 0xa40 (a gear from 0, volume 50) |
| CHASE | 23 | the tyres in a turn | matched | chase[4] | 35 | h22+0xd18, 0x2d00, 0x2dde; `AnimateTurn`, the U-turn |
| CHASE | 24 | shift up | matched | chase[7] | 20 | h22+0xa7e; `GearUp` |
| CHASE | 25 | shift down | none | | | no call |
| CHASE | 26, 27 | the engines, by gear | none | | | the Amiga starts chase[6] (volume 20, a loop) once (h16+0x1fa) and has no call by gear |
| CODE | 45 | a letter is set | matched | code[2] | 63 | h7+0xc72; `GuessLetter` |
| CODE | 46 | a hint; the message is decoded | probable | code[1] | 40 | h7+0xcd4 is the decoding (`ShowSolution`); the hint (h7+0x8e2) plays code[0] at each of 200 blinks, the PC one sound before them, and the same command serves both |
| BUG | 28 | a chip is swapped | matched | bug[0] | 63 | h13+0x146a; `SwapChip` |
| BUG | 29 | a phone line goes dead | matched | bug[2] | 63 | h13+0xce4; `DrawTerminals` (the count equals the best, the line is dead) |
| BUG | 30 | alarm | matched | bug[1] | 63 | h13+0xe98; `BugStatusMessage`; plays until the part stops it |
| BUG | 46 | the board is finished | matched | bug[2] | 63 | h13+0xf60; `ShowResult` |
| GAME | 31 | the teleprinter's tick | none | | | the Amiga loops game[0] through the whole text and stops it at the end (h73+0x2b6); the PC clicks at each line |
| GAME | 32 | the teleprinter's line end | matched | game[1] | 63 | h73+0x3c8; `ShowBulletin` |
| GAME | 35 | the aeroplane takes off | matched | game[3] | 63 | h79+0x592; `AnimateTravel` |
| GAME | 36 | the aeroplane lands | matched | game[2] | 63 | h79+0x664: the same descent test (`height < lastHeight`, the height held to 8) |

27 matched, 1 probable and 6 with no Amiga twin: 34 rows. The `AmigaEffectMap` holds the same rows; its test counts them.
Not in the table because they are not sounds: the stop (0), the end of a scene (1), the tunes (2, 39 to 43), the pause and the
resume (37, 38). The Amiga plays its own beep (tac[8], chase[8]) when the player pauses and resumes; the PC's commands ask for
no sound, so none is played. The Amiga effects no PC command stands for: tac[8] and chase[8] (the pause beeps), chase[0] and chase[1] (played now and then at the
right and the left edge of the screen during the chase, h16+0x250 and 0x278), chase[6] (the engine loop), code[0] (the hint's
blinks), game[0] (the teleprinter's loop); chase[2], chase[3] and code[3] are called nowhere in the program.

Not decided by the table (the rest of the PC's commands, in any part): the options' clicks (19 and 20, in every part; the
Amiga plays nothing when an option is switched), the commands the animation cues (PAN files) ask for, the commands of FINAL (the
menus part has no Amiga effects), and the commands above 46 of the Roland driver. They have no Amiga twin.

## What plays where

* A command with an Amiga twin in the part's bank plays on the Amiga voices.
* A command without one plays on the sound card's effects driver (the sound card of the set-up, so the raid keeps its
  gunfire), or, with `sound.amigaOriginalOnly` on (the sub-setting shared with the music, "Only where the Amiga had it"), is
  not played.
* The stop (0), the end of a scene (1), the pause and the resume (37, 38) go to both. Changing the part silences the Amiga voices,
  as the Amiga unloads a bank.
* The Amiga music (`sound.music` `amiga`) and the Amiga effects may be chosen together; each reads the same folder.

## Without the files

The folder is the one named in the launcher, else `amiga/files` (or `amiga`) next to the program, else next to the game folder
and the two folders above it, each counting when it holds `gamesnd.dat` itself, in its `disk2` folder or in a folder directly
below it (for a folder named like a disk, `disk1`, the same below its parent). The other banks are looked for in that folder,
in the folders below it, in its parent and in the folders below that, so disk 3's `chasesnd.dat` is found beside disk 2. Without
all five banks the sound card's effects play instead, and the launcher says so (as for the MT-32 without ROMs, D-20).

## Listening

With `COVERT_AMIGA_DIR` (the folder with the disk folders) and `COVERT_AMIGA_WAV_DIR` set, the test
`AmigaEffectsFilesTests.TheMappedEffectsAreWrittenAsWavFilesWhenAskedTo` writes each matched or probable effect as
`<PC command>-<bank><index>.wav` (for example `09-tac7.wav`, the camera) into that folder, played through the driver.
