# The Amiga version's music (D-640)

A player's option, off by default: the music of the Amiga version of Covert Action, played from the player's own Amiga
files, in the music slot of the split drivers (D-620, SOUND-MIXING.md). The code: `Audio.Amiga.GmcTune` (the file),
`GmcPlayer` (the tick player and the four Paula voices), `AmigaMusicDriver` (the game's commands),
`Launcher.AmigaFolderSearch` (where the files are), `Launcher.SoundDriverChoice` (the music letter `G`); the settings:
SETTINGS.md (`sound.music` `amiga`, `sound.amigaFiles`, `sound.amigaOriginalOnly`). No Amiga file, sample or rendering
is in the repository or shipped: the player's folder is read when the game starts.

## The files

Three Game Music Creator modules from the Amiga version's disk 1: `thememusic.dat` (50,462 bytes), `finalmusic.dat`
(50,462) and `spy6music.dat` (75,742). A module is 15 instrument headers of 16 bytes (+4 length in words, +7 volume,
+12 loop length in words), three zero bytes, the number of orders, 100 order words (the pattern number times 1024), the
patterns (64 rows of 4 voices, 4 bytes a cell: a 12-bit period, a 4-bit instrument, a 4-bit command and its parameter),
then the instruments' signed 8-bit samples one after the other. All three parse with no bytes left over.

| file | orders | patterns | samples with data | length |
|---|---|---|---|---|
| `thememusic.dat` | 8, the last 63 | 4 | 6 | about 45 s |
| `finalmusic.dat` | 7 | 4 | the same 6 as the theme | about 45 s |
| `spy6music.dat` | 12 | 5 | 8 | about 77 s |

The quirks kept: an order of pattern 63 is not a pattern and ends the song; a cell of 0xFFFE is a note cut; a loop of two
words or fewer is "no loop". The only commands the tunes use are 0 and 8 (set the speed), so no other is played.

## The player

A tick lasts 20 ms (125 beats a minute); a row lasts the speed's ticks, 6 until a cell's command 8 sets another (for its own
row too). A voice plays its sample at Paula's clock (3,546,895 Hz, PAL) divided by the note's period, at the note's
volume 0 to 64; a note with a period starts the sample over, an instrument alone changes the volume of the running voice.
Voices 0 and 3 sound on the left, 1 and 2 on the right. The samples are resampled to the device's rate with linear
interpolation; a fixed one-pole low-pass at 4.9 kHz stands in for the A500's filter; a third of each voice also goes to the
other side. The tunes repeat from their first order until stopped.

The timing was checked against a reference render of the same files (nearest-neighbour resampling): the loudness
envelopes agree at a correlation of 0.99.

## Which command plays which tune

| Command | PC game | Amiga music |
|---|---|---|
| 2 | the theme: title, menus, debriefing | `thememusic` |
| 39 | the selection screens' tune (TAC's equipment, CHASE's car choice) | `spy6music` |
| 40, 42 | vacations | `spy6music` |
| 41, 43 | vacations | `finalmusic` |
| 0, 1 | stop, end of scene | stop |
| 37, 38 | pause, resume | pause, resume |

The Amiga plays `thememusic` on the title and the main menus, `finalmusic` on FINAL's other scenes and `spy6music` in one of
FINAL's pop-ups; TAC, CHASE, BUG and CODE have no music. So 39 to 43 have no Amiga twin: the mapping above is a design
choice, and the sub-setting `amigaOriginalOnly` leaves them silent. FINAL's debriefing sends the same command 2 as its
menus (`Headquarters.Debrief`, `Headquarters.Career`), so the driver cannot tell it apart and plays `thememusic` there; telling
it apart would mean changing what the game asks of its sound driver, which the session proof compares.

## Without the files

The folder is the one named in the launcher, else `amiga/files` (or `amiga`) next to the program, else next to the game folder
and the two folders above it, each counting when it holds thememusic.dat, itself or in a `disk1` folder (or any folder) below.
Without all three files the sound card's music plays instead, and the launcher says so (as for the MT-32 without ROMs, D-20).
