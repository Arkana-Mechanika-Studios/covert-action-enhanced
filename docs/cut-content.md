# Cut content and dormant branches of the original

A living register of things the shipped game (MicroProse, 1990, version 447.01) contains but never uses, or uses
only in a form that looks like what was left of a larger design: files nothing names, texts nothing asks for,
branches nothing can reach, whole modes nothing starts. It is kept next to `docs/original-bugs.md` (quirks that do
happen) and is updated by anyone who finds more.

## What counts

An entry is something the code or the data carries and the game as shipped never shows, with a reason to think it
was meant to be shown (or was, in an earlier build). Ordinary leftovers (library routines linked into every module,
the same source file compiled into five programs) are listed too, but marked **explained**, so that nobody
rediscovers them as cut content.

* **Confidence.** *confirmed*: the fact is proven from the game's bytes or the readable C (nothing reaches it); that
  it was *cut*, rather than never finished or always a stub, is stated separately in each entry as fact or guess.
  *likely*: the facts are checked, the reading as cut content is the best explanation but not the only one.
  *possible*: a pattern worth a look; there may be nothing behind it.
* **Status.** *player option (D-n)*: the game offers it as an option of the launcher's "Cut content" tab, off by
  default (csharp/docs/SETTINGS.md); the entry says what the option does and what it leaves out. *open*: nobody has run or rendered it to settle it. *verified*: confirmed by a run of the original, a
  render or a patched run (the entry says which). *explained*: the reason is known (library code, shared source,
  documented by the publisher) and nothing more is expected from it.
* **Facts and guesses are kept apart.** Under *Evidence* every line can be checked from the named file, offset or
  C line. Under *Reading* a guess is written as a guess.
* Offsets: `DS:` is an offset in the module's data group, `0000:xxxx` a code frame and offset in load-image paragraphs, as in the names of the reconstructed functions
  (`<module>_<frame>_<offset>`); a load-image offset is called that.

How the entries were found: a scan of the game's files (not published) produced
the data-side lists (file names, text keys against the crime files, shadowed tags, plot table, unread record
fields) and, since the second pass, the sheet cells no `cut_image` takes (section 5; `--render <folder>` writes
the pictures), the re-check of the 41 dead functions (6), the sound commands (7) and the replay of every `.PAN`
(8). The code-side items come from `csharp/docs/OPEN-FUNCTIONS.md` (c), the `// DORMANT:` markers of
`csharp/src` and a reading of the reconstructed C that carries them.

## Summary

| ID | Title | Kind | Confidence | Status |
|---|---|---|---|---|
| CC-1 | CAPTRD01.PAN, an animation no program names ("CAPTURED" stamp on a dossier photo) | unused asset | confirmed | player option (D-660) |
| CC-2 | CHASE's stand-alone mode (`campaign_mode`, "Loc<n>" labels, random start and goal) | dormant mode | confirmed | player option (D-661) |
| CC-3 | Module code 5: a second CHASE mode (arrest-only) that nothing starts | dormant mode | confirmed | open |
| CC-4 | Module code 0x63: `mcv.exe cv /2 <module>` (CA-2) | dormant mode | confirmed | open |
| CC-5 | Plots built for six variants (and a seventh in Max's apartment); three shipped | cut variants | confirmed (reserved slots); likely (cut) | open |
| CC-6 | INTRO: seventh and later sound cards, and the flag that skips the sound question | dormant branch | confirmed | open |
| CC-7 | Custom sound driver (XSOUND) announced, never shipped | cut feature | confirmed | explained |
| CC-8 | INTRO: unused graphics file-name table and the H/V letter folding | dormant data | likely | open |
| CC-9 | CODE: 56 sentences of the graphics library's design notes | embedded developer text | confirmed | explained |
| CC-10 | 41 functions nothing calls (message builder x3, `gather_intelligence`, file and picture helpers, empty stubs) | dead code | confirmed | explained |
| CC-11 | TEXT.DTA `*FLUF12`..`*FLUF15`: four filler sentences the code never draws | unreachable text | confirmed | open |
| CC-12 | TEXT.DTA message texts nothing asks for, and events without a text (CRIME1 is a trimmed CRIME4) | orphan texts | likely | open |
| CC-13 | Shadowed duplicate tags: `*MSG0102`, `*C0102`, `*C0104`, `*PL0815`, and the stray `*C0105` | unreachable text | confirmed | open |
| CC-14 | Cast and event fields of the crime files no module reads | unread data | confirmed | open |
| CC-15 | Mouse support in the graphics library (int 33h), never called | cut feature | likely | explained |
| CC-16 | BUG circuit difficulty 4 and the TAC "wait for a visited room" walk | dormant branches | confirmed | open |
| CC-17 | Plain dormant statements and arms (the remaining `// DORMANT:` markers) | dormant code | confirmed | explained |
| CC-18 | Prose variants `*nice3`..`*nice7` the reader could ask for | missing text | possible | open |
| CC-19 | CAPTRD01.PAN: a woman's photo (image 6) and the sex switch the script itself defeats | dormant branch | confirmed; likely (cut) | verified (replay); player option (D-660) |
| CC-20 | Key-press exit sequences of COMM, CRYPTO, HQ and RESEARCH (`ignore_skip` is never set) | dormant branch | confirmed | verified (replay) |
| CC-21 | Animations: no sound instruction in any of the 24 `.PAN`; one unstarted blink program; no unshown image | negative result and leftovers | confirmed | explained |
| CC-22 | Faces sheets: two 32 x 26 icons no `cut_image` takes, the lower half of FACESF.PIC, duplicate head cells | unused sprite cells | confirmed (cut_image); possible (any use) | open |
| CC-23 | Sound commands 15 and 21 are never sent; the sent ones the drivers leave empty (gear shifts, 0x27, vacation, 0x2c) | unused and silent commands | confirmed | explained |

Related registers: `docs/original-bugs.md` (CA-n), `csharp/docs/faithfulness/DIVERGENCES*.md` (D-n),
`csharp/docs/faithfulness/UNCERTAIN*.md` (U-n), `csharp/docs/OPEN-FUNCTIONS.md`, `symbols/names.txt`.

---

## CC-1 CAPTRD01.PAN: an animation no program names

**What it is.** `CAPTRD01.PAN` (7476 bytes), a 96 x 200 animation: a tilted dossier page with a paper clip, a
photograph of a man in a dark suit and tie whose face is a grey smudge, columns of grey text lines under it, and
from about the 20th tick a red stamp "CAPTURED" across the text. It loops (the player runs 100 000 ticks without it
ending), like the other `.PAN` scenes the game plays inside a frame.

**Evidence.**
* The game folder has 24 `.PAN` files; every one but this is named in some program (`BRIEFING.PAN`, `BUSTOUT.PAN`,
  `SURNDR01.PAN`, `WOUNDED.PAN`, ... in the data of GAME, FINAL, CODE and CHASE). the project's scan
  section 1 lists `captrd01.pan` among the files no program names. The three files that look unnamed besides
  (`BLDING02..04.PAN`) are made from `"blding01.pan"` by GAME 1e5a (GAME 00f7:1e5a lines 106-108,
  digit at index 7 set from the building's guard level 0..3), so they are used.
* No EXE image or driver contains the byte string `captrd` (any case) or `capt` followed by `rd`; no data file
  does; the project's own sources, symbols and docs never mention it (searched `docs`, `symbols`, `src`, `csharp`).
* The file is real, not a stub: 96 x 200 animation with a stamp frame sequence.

**Why it looks cut.** A finished scene with a title that says what it is (a capture), in the same naming scheme as
`SURNDR01.PAN` ("surrender") and `BLDING01..04.PAN`, shipped on the disks but never started. The game's capture
paths show only text (GAME 1608 `*carcap` prose, `popup_menu_on_clean_screen`) and the car chase's arrest text
("make the arrest!", CHASE 001c) with no picture.

**Reading (guess).** It was meant for a capture or arrest scene; CHASE's arrest-only mode (CC-3) is one place it
could have been shown. No fact ties them.

**How to verify.** Render it with the animation player (`PanPlayer`) at ticks 1, 10, 20, 40, 60, 80 and 120. To settle that nothing plays it, run the game with a
breakpoint on DOS open (int 21h/3Dh) and look for the name over a full campaign; the byte search above already
shows no program could build the name.

**Related.** CC-3; `csharp/src/CovertAction.Content/PanFile.cs`.

**Player option (D-660, `cutContent.capturedDossier`, off by default).** When a cast member is captured the page plays in
the frame of the neighbouring scenes (96 pixels at the right, x = 0xe0), after the jail scene ("taken to jail", the
capture text) and before the chief's comment, with the person's name in the box. A raid that ends in an arrest and the car
chase's arrest both end in `InterrogateSuspect`, which is where it is shown; a suspect who is no cast member gets none.

## CC-2 CHASE's stand-alone mode

**What it is.** CHASE can build its map and locations without a campaign: random seed, locations labelled
`Loc1`..`Loc15`, random start and goal, every location visible. The shipped program always switches the campaign
on, so none of it runs.

**Evidence.**
* `campaign_mode` (CHASE DS:5bfc) is set to 1 unconditionally in `main`, before the map is made
  (CHASE 00df:001c line 91); nothing else writes it (names.txt `D_chase_5bfc`).
* `generate_map` (chase_00df_2c51.c): `if (campaign_mode) srand(0x7b * InCity + (map_seed & 0x3fff))`; the labels
  `strcpy(message_buf, "Loc"); strcat(.., itoa(lh, ..))` are drawn only when `campaign_mode == 0` (line 132); the
  random `start_loc` / `dest_loc` (lines 142-146) are overwritten when `campaign_mode` is set (lines 149-156);
  every location is plotted when it is 0 (line 130) and not only the known ones.
* `check_location_entry` (chase_00df_3190.c line 30) stores the reached place in the campaign hand-off only in
  campaign mode.
* csharp: `// DORMANT:` in `CarChase.cs:280`.

**Why it looks cut rather than ordinary leftover.** Three separate places carry the non-campaign arm, written out in
full (labels, seeding, random goal), the mode is selected by one variable, and the variable is stored as a constant
right where the map is made: the signature of a program first written as a stand-alone chase game and then wired
into the campaign.

**Reading (guess).** The developers' test harness for the chase, before the campaign existed.

**How to verify.** Patch the immediate of the store at CHASE 001c line 91 (`campaign_mode = 1`) to 0 in a copy of
CHASE.EXE and start the chase from the practice menu; expect `Loc1`.. labels on the minimap and no hand-off
update. Not yet done.

**Related.** `csharp/docs/faithfulness`: none; CA-4 (`check_location_entry`).

**Player option (D-661, `cutContent.standaloneChase`, off by default).** The practice menu (main menu, Practice, the level)
has a fifth line, "Free Chase", which starts the driving practice's hand-off and runs CHASE with `campaign_mode` clear. Played:
a new random map on each start, `Loc1`..`Loc15` on the map (all 15 dotted), a random start and goal, no hand-off written.
Verified by rendering it (the map shows the labels) and by `CutContentChaseTests`. One adaptation: the F2 "enter the
building" test, which in the original's own non-campaign arm still reads the city's known organisations, takes every place as
known, so the places the map shows can be entered; the F2 leaves the chase as an "enter the building" in a practice does.
The only new text is the name of the menu line.

## CC-3 Module code 5, a second CHASE mode

**What it is.** The launcher's module table names `chase.exe` for codes 3, 5, 6 and 0x0e. Code 6 is "you are followed"
(GAME 1e5a 190), 3 starts a tail (GAME 23c9 122) and 0x0e is FINAL's practice chase (`0x0d + 1`,
final_0100_04a7.c). Code 5 is handled by CHASE as `CHASE_VARIANT` and nothing ever starts it.

**Evidence.** `module_names` (COVERT DS:0124) lists `chase.exe` for 3, 5, 6 and 0x0e. Every writer of the hand-off
byte 0 in the C: GAME `exit_mode` values 1, 2, 3, 4, 6, 7, 8, 0x0a, 0x0c, -1; `next_module = 9` (GAME 8755 159);
FINAL `practice_kind + 0x0d` (kinds 0..3: 0x0d..0x10); 0x0b (COVERT first start), 0x11 and 0 (return);
a search of the reconstructed C for `exit_mode =` and `next_module =` finds no 5. CHASE main (chase_00df_001c.c 54-64) maps 5 to
`CHASE_VARIANT`; the variant differs from the tail in one way: the closing texts for losing and being spotted
(lines 108-125) are made only for `CHASE_TAIL` and `CHASE_FLEE`, so variant 5 would show text only after an
arrest (line 129).

**Why it looks cut.** A table slot and a `case` in the consumer with no producer.

**Reading (guess).** An arrest-only chase (the quarry is to be caught, nothing else ends it); see CC-1.

**How to verify.** In a copy of GAME.EXE change one `exit_mode = 3` store (23c9 line 122) to 5 and follow a car:
the chase should end with no "lost track" text. Not yet done.

**Related.** CC-1, CC-4, the reconstructed `state.h` (module codes).

**Not offered (decided with D-661).** The code 5 plays as a tail with no closing text for losing or being spotted (the C
makes the texts only for the tail and the flee), so a chase that ends so shows an empty box, and only an arrest has its
text: nothing in it says what the mode was for, and it would need text written for it. Left as the original has it.

## CC-4 Module code 0x63 (`mcv.exe`)

**What it is.** The launcher runs `mcv.exe` with arguments `cv`, `/2` and the module's own name when the next
module code is 0x63.

**Evidence.** COVERT 0000:0010 (COVERT 0000:0010 line 62-63); string `mcv.exe` at COVERT DS:0106
(load-image 0x18a6); CA-2 (the name table has 18 entries, `module_names[0x63]` reads past it). No file named
`MCV.EXE` ships; no writer produces 0x63 (see CC-3's list).

**Reading (guess).** A developer's alternative front end (`cv`, `/2`: second video mode?), left in the launcher.

**How to verify.** Not testable without MCV.EXE; the evidence above is complete.

**Related.** CA-2.

## CC-5 Six variants per plot; three shipped; variant 7

**What it is.** Each of the 15 plots (and the tutorial) has a table of eight crime-file slots. Only slots 0..2 are
filled. The code draws a variant from `random(6)`, rejects the empty ones, and has a seventh variant, "Max
Remington's apartment", that nothing can draw.

**Evidence.**
* FINAL's plot table (DS:1e9e, 16 x 74 bytes; the scan, section 3): `crime_file` is
  `(a, b, c, -1, -1, -1, -1, 1)` for plots 1-9 and `(a, b, c, -1, -1, -1, -1, -1)` for plots 10-15; the tutorial
  has `(1, -1, ...)`. Slots 3..6 are `-1` in every plot; slot 7 holds crime file 1 (the tutorial's) for plots 1-9.
  The object-text pairs `item[3..7]` are empty strings in all plots.
* `apartment_scene` (FINAL 0100:0906 41-75): `c = random(6)`, a variant is accepted only when
  `crime_file[c] != -1`, so 3..5 are always redrawn; mask arithmetic works for up to eight variants (`0xff >> ..`);
  the `c < 7` / else arm copies the hard-coded `apartment_item_1/2` ("Max Remington's apartment", "a mattress full
  of cash") and `c == 7` re-shows the briefing (CA-9, csharp `Headquarters.Briefing.cs:180`).
* PLOT.TXT (416 tags): the variant digit of `*PLppvk` is 0, 1 or 2 for every plot (and 9 for the introductions);
  there is no tag for variants 3..7 (scan section 2, last line).
* Crime file 1 ("Stole Blueprints", the tutorial's script) is named only by the tutorial and by slot 7 (scan
  section 3: "named only by slot 7 or by the tutorial").

**Why it looks cut.** The structure is sized for six (random(6)) to eight (masks, slots) variants and shipped with
three; slot 7 is the one place that names the tutorial script again, in the player's own apartment, with texts that
live in the program instead of PLOT.TXT: an unfinished or removed "replay at home" case.

**Reading (guess).** Plots were to have more variants (the data shows 13 crime scripts used 45 times, with heavy
reuse); variant 7 was an apartment practice case reusing the tutorial script.

**How to verify.** Patch `crime_file[3]` of one plot to a real file number in FINAL's data and play that plot until
variant 3 comes up; there is no PLOT.TXT text for it (the page loop reads nothing). For variant 7 set the slot to
a non-negative number and `random(6)`'s upper bound to 8. Not yet done.

**Related.** CA-9; D-/U- none; `final.h` struct `plot`.

## CC-6 INTRO: seventh and later sound cards, and the skip flag

**What it is.** The sound menu loop builds six lines but contains the code for a seventh and later: it would patch
character 15 of the menu line and take the driver letter from a second table.

**Evidence.** `hardware_setup_menu` (INTRO 0000:0010): `do { ... if (e >= 7) { snd_menu_text[e][0xf] =
snd_extra_letters[e]; snd_letter_by_choice[m] = snd_extra_letters[e]; } ... } while (e < 6);` `e` is at most 5
inside the loop, so the branch never runs. `snd_extra_letters` is INTRO DS:081b, in the BSS (no data in the file).
`h`, the flag that asks for the sound mode at all, is set to 1 just before it is tested (`h = 1; ... if (h == 1)`).
Both are `// DORMANT:` at `csharp/src/CovertAction.Presentation/Intro/HardwareSetup.cs:64`.

**Why it looks cut.** Menu code written for a variable number of sound cards (and for omitting the question), with
the loop bound later fixed at six; the table for the extra letters was never filled.

**How to verify.** Reading is complete; the INTRO menu shows six lines (visible in any run).

**Related.** CC-7, CC-8; csharp `Intro/HardwareSetup.cs`.

## CC-7 Custom sound driver

**What it is.** Menu line "6) Custom sound driver" (letter X) makes COVERT load `XSOUND.CA`. The file never shipped.

**Evidence.** INTRO DS:0103 `") Custom sound driver"` (load-image 0x13c3), driver letters `"NITARX"` at DS:04ea;
COVERT builds `?sound.ca` from the template at COVERT DS:0156 (load-image 0x18f6); the folder has A/I/N/R/T `SOUND.CA` only. COVERT prints "Filename not
found" and quits (D-18, `csharp/docs/faithfulness/DIVERGENCES.md`). README.DOC (the publisher's last-minute
notes) announces it: "In the future, MicroProse may supply custom drivers ... rename the driver program to
XSOUND.EXE", which names a different file than the code loads (`XSOUND.CA`).

**Status.** Explained: announced by the publisher as a future option. The README's `.EXE` against the code's `.CA`
is a documentation slip, or a change of mind about the driver format.

**How to verify.** Choose option 6 in the original (done by the project, D-18).

**Related.** D-18, CC-6.

## CC-8 INTRO: unused graphics file-name table, and H/V folding

**What it is.** A table of five file names inside INTRO that nothing reads, and two letter conversions that can
never trigger.

**Evidence.**
* INTRO DS:04fc: five pointers to `"Egraphic.exe"`, `"Mgraphic.exe"`, `"Egraphic.exe"`, `"Tgraphic.exe"`,
  `"Cgraphic.exe"` (the strings are at INTRO DS:0119..), in menu order VGA, MCGA, EGA, Tandy, CGA (the intro's header
  comment, "unused by the C"). The letters actually used are `"EMETC"` (DS:04f6): VGA gets `E`, so COVERT loads the
  EGA driver (CA-48). The unused table agrees with the letters, so the VGA = Egraphic pairing was written down
  twice.
* After the choice the C folds the letter: `H` becomes `C`, `V` becomes `M` (intro_0000_0010.c 89-94). The letters
  come only from `"EMETC"`, so neither can occur. `H` and `V` letters appear in the code and the state.h note
  (`'C' CGA ('H' remapped), 'M' MCGA ('V' remapped)`).

**Reading (guess).** An earlier menu offered Hercules ("H", drawn with the CGA driver) and a VGA entry ("V", drawn
with the MCGA driver); neither driver exists in the folder (`HGRAPHIC`, `VGRAPHIC` are not shipped and nothing
names them). Only a guess: the letters could equally be keys of an older key table.

**How to verify.** Run INTRO.EXE under a debugger and read the table; patch `"EMETC"` to `"HVETC"` and see which
driver COVERT asks for.

**Related.** CA-48; D-18; `csharp/src/CovertAction.Presentation/Intro/HardwareSetup.cs`.

## CC-9 CODE: the graphics library's design notes

**What it is.** CODE.EXE's data holds 56 English sentences of the library's documentation, 0x006c..0x0eef of its
data group, from "The MicroProse Graphics Library is intended to be a collection of high quality routines" to "It
is still an open issue as to whether or not a separate mask plane is required."

**Evidence.** `D_code_006c _graphics_library_docs` in `symbols/names.txt` ("no code reads them"); none of CODE's
14 referenced strings (string table, `build/code-analysis/CODE/strings.json`) lies in it. No other module carries
the block (searched all 13 unpacked images for "It would be nice"). Some lines are developer asides:
"It would be nice if this code was used system-wide.", "There was a conceptual problem with building sprites
off-line. But I can't remember what it is.", "Currently two file formats are supported: IFF for development and
CRUNCHED for production."

**Status.** Explained: comment text of the library's assembly source compiled into CODE's data. It documents
design intent (IFF picture loading, an off-line sprite compiler, a separate mask plane) that the shipped library
does not have; none of it is a game feature. Worth keeping as a quotation source for the project history.

**Related.** `symbols/names.txt`; CA-19/22/23 (the library's quirks).

## CC-10 Functions nothing calls

**What it is.** The 41 functions of `csharp/docs/OPEN-FUNCTIONS.md` (c): no call, far call or pointer reaches them.

**Evidence (re-checked, partly).** The original function database (`build/code-analysis/<MODULE>/functions.json`)
gives 0 callers for `FINAL 0100:524c`, `FINAL 0100:7668`, `TAC 00e8:a522`, `BUG 00e8:3c9e`; their live
counterparts have one caller (GAME 7cf4 `compose_message_text`, called once). The file helpers are dead as a chain
(each `create_file` is reached only from a dead `save_file`/`write_file`). Raw caller counts are not enough: 100
functions of kind C have no direct caller, 59 of them are reached through tables and pointers (for example FINAL
85ec, the "Master Plan" screen).

**Re-check of all 41** (the scan, section 6, independent of the project's scan): in each unpacked
program, every `E8`/`E9` whose target is the function, every far call (`9A` offset, frame) and every other 16-bit word
equal to the function's offset. Result: no function is reached from live code. Five (the `create_file` of FINAL, GAME,
TAC, BUG and CHASE) have one near call each, and each lies inside the dead `write_file_from_far`, `write_new_file`,
`write_file`, `save_file`, `save_file` respectively (load-image 0xd2e1, 0xe5d3, 0xe1fb, 0x7fc7, 0x523f, each inside the
caller's range). The 9 stray equal words (1 in FINAL, 3 in TAC, 5 in CHASE) are not references: the letters `LR` inside
`*ALRT0` in FINAL's data, the byte pair `e8 d2` straddling two instructions in TAC, and in CHASE the constant 4400h
(`mov ax, 4400h; int 21h`, a DOS IOCTL call, and data). No table entry or
pushed address names any of the 41; nothing turned out reachable.

Groups:
* **Intercepted-message text builder, three dead copies**: FINAL 7668, TAC a522, BUG 3c9e (`load_msg_endpoints`).
  The live builder is GAME 7cf4. Each module carries a copy of the shared campaign source (state reader/writer,
  text keys), so the copies are the same source file compiled into five programs, not cut features.
* **`gather_intelligence` in FINAL (524c, 871 instructions)**: TAC and BUG call their own. Same reason; FINAL's
  copy would be the intelligence report if FINAL ever needed it.
* **File and picture helpers** (`create_file`, `read_file`, `load_picture_*`, ...): the shared file library.
* **Empty functions**: CHASE 04a1 (an empty function), PAN engine `SubFinal0100Af04` (empty in two programs,
  csharp `LibraryAnimation.cs:289`), TAC 06a0 `set_palette_register` (palette registers; unused on VGA/EGA).
* **Replacement and twin helpers** in the csharp markers: `FindOrAddPersonQuiet` (no caller in GAME;
  `find_or_create_person` does the same and announces), `FindOrCreatePersonQuiet` (BUG), `FormatGameTime` (BUG,
  the twin of the chase's clock), `ClearEventSteps` (FINAL sets steps up at case start).

**Status.** Explained. Nothing here is a hidden feature. The one open thought: the older, quiet
`find_or_create_person` copies in GAME and BUG suggest the announcement ("X has been ...") was added later and the
old form left in the shared file.

**Related.** `csharp/docs/OPEN-FUNCTIONS.md`; CC-17.

## CC-11 TEXT.DTA `*FLUF12`..`*FLUF15`

**What it is.** Four filler sentences of the message texts the code never draws.

**Evidence.** TEXT.DTA has `*FLUF00..*FLUF15` (16 entries). GAME `compose_message_text` (7cf4 line 118) calls
`read_random_variant("text.dta", "*FLUF00", 0xc)`, 12 variants: `FLUF00..11`; the other modules' copies are dead
(CC-10). The unread entries: "Extreme caution is advised. Message security must be maintained.", "Do not hesitate
to act in a decisive manner.", "Your request for promotion is being considered.", "Further communications may
follow." (the scan, section 2). The comment in the C already says the same.

**Why it looks cut.** The file was written for 16; the count in the code is 12. Either the program's count
was never raised, or four lines were dropped for length.

**How to verify.** Patch the count byte (`push 0xc` at GAME 7cf4's call) to 16 and read many coded messages; the
four lines appear. Not yet done.

**Related.** None.

## CC-12 Message texts nothing asks for, events without a text

**What it is.** TEXT.DTA message texts that no event of any crime file can ask for, and events whose text is missing.

**Evidence** (the scan, section 2; tag `*MSGccmm` = crime number cc, event message id mm,
GAME 80df `load_message_text`):
* Unasked tags: `*MSG0104`, `*MSG0111`, `*MSG0310`, `*MSG0611`. `*MSG0400` is asked only by the end-of-file marker
  record of CRIME4 (message id 0), which no one plays.
* Events with no tag: `*MSG0208` (CRIME2 E11, "Purchase weapon", kind 0) and `*MSG0314` (CRIME3 E2 and E7, "Rcvd
  Package fm" 0x501 and "Sent Package to" 0x400).
* CRIME1 is a trimmed copy of CRIME4: both carry the objects Blueprints / Alarm Bypass / Stolen Goods; TEXT.DTA
  files each CRIME4 text under two tags, `*MSG04nn` and `*MSG01nn`, and CRIME1 has no events with ids
  4 or 11, so `*MSG0104` ("It is vital that you provide us details of the security alarm ...") and `*MSG0111`
  ("Our operation went off like clockwork ...") are twins of CRIME4's alarm and delivery messages that the shorter
  tutorial case never uses.
* Package events (kinds 0x400, 0x500) exist in CRIME3 only (two events); the C names `EV_PACKAGE` "no specific
  reader" (campaign.h).

**Why it looks cut.** Texts for events removed from a script, and a script (CRIME1) shortened without removing the
texts of the removed part.

**Reading (guess).** `*MSG0310`/`*MSG0611` (a drug delivery arrival, "photographs ... delivered to the Mastermind")
may belong to events deleted from CRIME3 and CRIME6; `*MSG0314` may be a text that was lost. Package events were a
mechanism only CRIME3 used, probably removed from other scripts.

**How to verify.** Read the text of each tag in TEXT.DTA beside the surrounding messages of its crime and look
for a gap in the event chain.

**Related.** CA-12, CA-14 (message ids and shared pairs).

## CC-13 Shadowed duplicate tags

**What it is.** Tags that occur twice in a text file. The original's reader takes the first match
(`read_cleared_text`, FINAL 0100:750d; csharp `TextFile.Entry`), so the second text is never read.

**Evidence** (the scan, section 2):
* TEXT.DTA `*MSG0102` twice: the first (standing alone right after the `*MSG0400 *MSG0100` group) is "Rumors
  indicate that Max Remington may be assisting the CIA on this case.", the second ("Bulletin: INTERPOL NewsNet. A
  set of floor plan blueprints were reported missing ...", under `*MSG0402`) is unreachable. CRIME1 E0, E8 ("Stole
  Blueprints", CA-12) and E15 ask for it, so these events get the rumour text, not the bulletin (a reading of the reader's first-match rule, not yet seen in a run).
* CLUES.TXT `*C0102` twice (home of a blueprint inspector; "Telegram sent to a newly recruited agent") and
  `*C0104` twice ("Telegram ... traced to a criminal Mastermind!"; "Enemy agents have been using $KEY"). CRIME1
  has five members (0..4), so the tag `*C0105` is also beyond its cast.
* PLOT.TXT `*PL0815` twice (plot 8, variant 1, page 5): the second ("We don't want any more excuses.", filed with
  `*PL101B`) is unreachable.
* Every other crime has exactly one tag per cast member, except that the last member (the MasterMind) has none in
  eleven crimes (CA-16).

**Why it looks cut.** Texts filed under the wrong tag when the files were assembled; CRIME1's clue block looks
like another crime's block pasted in.

**How to verify.** Reading the files; run the tutorial and read the bulletin of the blueprint theft.

**Related.** CA-12, CA-16, CA-17.

## CC-14 Crime-file fields no module reads

**What it is.** Bytes of the 13 crime files that no code reads, plus kinds of events only one file uses.

**Evidence** (scan section 4; field notes in the reconstructed `campaign.h`):
* Placement bit `0x04` (`CAST_UNUSED_04`, "no reader in GAME, FINAL, TAC, BUG, CHASE or CODE") is set on four
  members, all in the first-authored files: CRIME0 member 0 "Initiated plan.", CRIME3 members 0 "Drug Supplier",
  3 "Chemist", 5 "Drug KingPin".
* The word at cast +0x2e (`dummy`) is non-zero (2..7) for most members of CRIME0..3 and 6 or 0 in CRIME4..12.
* The key word at cast +0x24 holds 1, 3, 4, 9, 13 or -1 in CRIME0..3; it is overwritten by `random(0x7fff)` at
  case set-up (`place_cast_member`), so the file value is never read.
* Type bit `0x40` ("Assassin", CRIME2 member 5) has no reader (CA-18).
* In CRIME0..3 the cast `role` text is often the name of an event ("Initiated plan.", "Contacted parties",
  "Financed operation", "Purchase weapon", "Delivered Money"), later files use job names.
* Event kinds (high byte): 0x2 and 0x3 messages (131), 0x8 and 0x9 meetings (112), 0x20 crime (41), 0x4/0x5
  packages (1 each, CRIME3), 0x0 (3: "Purchase weapon", "Processed Drugs", "Created Forgery"), one 0x13xx event
  (CRIME2 E14).

**Reading (guess).** CRIME0..3 are the oldest files, written under an earlier set-up that used the key word,
the +0x2e word and the 0x04 bit; the later files and the code dropped them. The fields are cut data structure,
not content players could see.

**How to verify.** Reading the C (done); a dynamic check would be a write watch on the 48-byte cast table during
a CRIME3 case.

**Related.** CA-18; `campaign.h` struct `Activity`.

## CC-15 Mouse support in the library

**What it is.** int 33h routines (reset, show/hide cursor, position, buttons, range) in the graphics library
copies of CODE and CHASE; none is called.

**Evidence.** `symbols/names.txt` `sub_code_0000_27f0` ... (`_mouse_reset` to `_mouse_set_range`); the C files
`CODE 0000:27ee`, `code_0000_285a.c`, `chase/chase_00df_403c.c` ..; csharp `LibraryInput.cs:148`
"no caller in CODE, like every mouse routine below". INTRO offers only "Joystick and Keyboard" or "Keyboard
only"; README.DOC lists no mouse.

**Status.** Explained as library code. Whether a mouse option was planned for the game is not shown by anything.
Since 15 November 2026 the C# rewrite has an optional mouse in the menus, lists and maps (a player option, off by
default; csharp/docs/MOUSE-SUPPORT.md, divergence D-630): an addition, not a restoration of this cut content.

**Related.** CC-9 (the library's design notes mention nothing about mice).

## CC-16 Dormant levels and flags in BUG and TAC

**What it is.** Two branches that assume more than the game provides.

**Evidence.**
* BUG circuit difficulty 4: `CircuitRules.cs:270` carries the original's test
  `(column != locked_column || difficulty == 4)` for fixing a chip in column 5; BUG's start routine limits the
  difficulty to 0..3, so the arm for 4 never runs ("difficulty is limited to 0..3 when the part starts (bug_init)").
* TAC's building generator: a flag kept in the original (`BuildingRules.cs:315`, `waitForVisited`) that would make the
  corridor walk go on "until it reached a room the player had visited" is never set.
* BUG start: one variable set to 1 and never read (`ElectronicsWorkbench.Start.cs:59`).

**Reading (guess).** A fifth difficulty tier of the circuit board and a pathing mode of the guards that were
designed and not used.

**How to verify.** Set the difficulty byte of BUG's hand-off to 4 after `bug_init` (patch the clamp) and open a
circuit; patch the flag to 1 in TAC and watch the guards' routes.

## CC-17 The remaining dormant statements

The other `// DORMANT:` markers of `csharp/src` are single statements or switch arms with no way to be reached;
they are listed so the register is complete. None is evidence of a cut feature.

| Marker (file:line) | What is unreachable | Why |
|---|---|---|
| `ClueRules.cs:58`, `CampaignLibrary.cs:551` | a switch fall-through for a clue kind other than the five | the only caller passes one of the five |
| `PlotRules.cs:525`, `PlotEngine.cs:636` | a suspect leaves with the program's own text (CA-35) | the departure check examines "person -1" |
| `ChaseRules.cs:158`, `CarChase.Tick.cs:570` | a heading other than 0..3 | all callers pass 0..3 |
| `GuardRules.cs:84`, `:606` | an unknown guard behaviour; the guard's x set to 1 after a return | no other mode; statement after the return |
| `BuildingRaid.cs:115`, `BuildingScreens.cs:204`, `CityScreens.cs:280` | mode tests repeated after the mode was cleared | the mode was changed earlier in the round |
| `Headquarters.Briefing.cs:180` | variant 7 of the briefing (CC-5, CA-9) | `random(6)` |
| `Headquarters.Debrief.cs:285`, `Headquarters.Fame.cs:72` | end mark of a full career record; `return -1` of the hall-of-fame search | 120 cases at most; the search stops at the last place |
| `LibraryMenus.cs:168`, `:216`, `LibraryOutput.cs:35`, `LibraryPictures.cs:129`, `LibrarySaves.cs:231` | library routines with no caller in CODE/FINAL/GAME | shared library (CC-10, CC-15); `load_picture_surface` expects a file form no shipped file has |

The README (README.DOC) mentions one removed mechanism: "Double Agents within the CIA no longer provide false
clues". No code of the shipped programs makes a false clue from a double agent (the `Doubles` mask and
alert -3 are used only for the alert and the interrogation outcome), so nothing of it remains.

## CC-18 Prose variants `*nice3`..`*nice7`

**What it is.** PROSE.DTA has `*nice0`, `*nice1`, `*nice2` (the debrief after a capture). GAME 28a4 line 181 reads
`*nice0` with variant `(rank - 1) / 3`; the title index of a cast member is 0..7.

**Evidence.** `load_prose_text` (GAME 00f7:2eb9) adds the variant to the last character of the key; for
rank above 9 the key `*nice3`..`*nice7` would be looked up and does not exist. CA-41 notes the literal is patched
in place and never restored, so after the first capture the same variant shows. Whether a captured person can
have rank above 9 was not checked.

**Status.** Possible; open. If a rank above 9 can be captured, the text is empty.

## CC-19 CAPTRD01.PAN: the woman's photo and a switch the script defeats

**What it is.** CC-1's animation holds two photo frames of the same size (70 x 66): image 4, a man in a dark suit
and tie, and image 6, a woman in a dark jacket with a necklace. The script chooses between them by script variable 0
(the game's way of passing "male or female": `face & 1` gives -1 or 1), and never shows the woman.

**Evidence** (the scan, section 8; replay of the script in the scan's own VM, which follows
C# `PanPlayer`).
* The images present: 2, 4, 6, 8, 9, 12, 14, 16, 18, 19; played with variable 0 in -1..4 and variable 10 in 0..4 for
  1500 frames each, image 6 is never selected by a Show instruction.
* The program's very first instruction is `PUSH -1; STORE var0` (bytes `05 00 ff ff 06 00 00`). Its photo switch (offset
  123: `PUSH var0`, then the cases `1` -> sprite program at 243 and `-1` -> sprite program at 240) therefore always takes
  case -1, the man (the program at 240 shows only image 4; the one at 243 shows only image 6). The instructions of case 1
  (offsets 145..174) are never executed.
* INTRGATE.PAN, which GAME starts after `script_set_global(0, face & 1 ? -1 : 1)` (game_00f7_28a4.c 69-70, and INJAIL
  at 166-167), is built the same way on the same variable and works: replayed with variable 0 = 1 it shows its image 5,
  with -1 its image 6.
* Render: `build/cut-content/CAPTRD01.PAN_images.png` (all ten images; not committed).

**Why it looks cut.** The scene was drawn for both sexes and its switch written, then the variable was forced to one
value, as a developer does to test one branch. CAPTRD01 is itself unused (CC-1), so nothing the player could see is
lost.

**Reading (guess).** A test line left in an unfinished scene.

**Player option (D-660).** With the option the man's or the woman's photograph is shown by the person's face bit: for a woman
the loaded copy of the script has its first instruction push 1 instead of -1 (the file is not changed). Checked in
`CutContentDossierTests` on the game's own file: the woman's page differs from the man's; with the option off the page
is never shown.

**Related.** CC-1, CC-3.

## CC-20 Key-press exit sequences of four scenes

**What it is.** COMM, CRYPTO, HQ and RESEARCH (the four 152 x 200 room scenes of GAME 6458/231a) each have, in their
main program, a block that runs when script variable 6 is -1 ("a skip key was swallowed"): it starts two sprite
programs and ends the animation (opcode END). The block never runs.

**Evidence.**
* The programs begin each pass with `PUSH var6; PUSH 1; ==; JIF` (COMM offsets 128..140) and go to the block (COMM
  offset 380) only when variable 6 is not 1, and again after each animation wait when it is -1 (COMM 344..359). Replayed without anyone setting variable 6, 22/22/22/20 of their
  main instructions are never run (the rest of each file's unreached instructions are the padding `REST` at the end
  or compiler `JMP`s that follow a `JMP`, see CC-21). Forcing the variable to -1 during a replay makes them end (55 to 130 frames).
* Variable 6 is written only by the skip test: FINAL `animation_skip_pressed` (final_0100_ac20.c 26-28) and GAME
  `skip_requested` (game_00f7_b3fe.c 27-30) set it to -1 when a word (`ignore_skip`: FINAL DS:ae30, GAME DS:a622) is
  non-zero, and clear that word. In the unpacked programs the only two instructions that touch the word are
  `cmp word [ae30], 0` and `mov word [ae30], 0` (FINAL load-image 0xbc55, 0xbc5c; GAME `[a622]` at 0xc3a3, 0xc3aa):
  nothing writes it non-zero, so the swallow-one-key branch and everything it feeds never happens.
* The game ends these scenes itself when the key press is not swallowed (`skip_requested` returns 1).

**Reading (guess).** A "press a key and the actors leave the room" ending was scripted into the animations and the
game side (`ignore_skip` set before an animation) never finished. The replay shows that no new image would appear;
the end sequence reuses images the scenes already show.

**How to verify.** Under a debugger set GAME's word at DS:a622 to 1 before a COMM scene starts and press a key
once: the swallowed key should make the scene run its exit block (two sprites, then the end) instead of ending
at once. Not yet done (the replay above is the tool's own VM, not the original).

**Related.** CA-34 (the skip test), CC-21.

## CC-21 Animations: the negative results and the leftovers

**What it is.** The answer to "frames inside the `.PAN` files never shown", and to the sound entries of the scripts.

**Evidence** (scan section 8).
* The 24 files hold 740 images in all. Played as the game plays them (script variable 0 in -1..4 and 10 in 0..4, the
  values the game passes: FINAL 1, 3, 4; GAME -1, 1, 2 and the guard level 1..4 for the building scenes), every image
  of every file is selected by some sprite program, except CAPTRD01 image 6 (CC-19). A static walk of every sprite
  program reachable from the started ones agrees (BLDING01..04 use all 75/79/77/90 images, depending on the guard
  level; BRIEFING shows 0 images for variable 0 = 0 or -1 and 8..13 for 1..4; INJAIL needs the sex variable).
* No main program of any `.PAN` contains the Sound opcode (3): the "sound events" of the scripts are not used. All
  scene sounds the game plays come from its own code (`DoSound`, CC-23).
* One sprite program no start reaches: BRIEFING.PAN offsets 1848..1928 (81 bytes), a blink of images 30 and 31 in a
  pattern (25 frames open, 4 closed, 30 open, ...). The same two images are shown by the started programs, so no
  frame is lost.
* The remaining unreached main instructions are one trailing `REST` (padding to 16 bytes) and, in BLDING01..04, five
  `JMP` after a `JMP` in the compiled `switch` on variable 10 (all four guard levels and the default are reached).
* Palette block kinds 1 and 2 (none, and the 0x306-byte block that C# `PanFile` reads) occur in no file: every one of the
  24 carries the 17-byte kind-0 block or none.

**Status.** Explained: nothing hidden. The unstarted blink is a leftover of the same kind as CC-20.

**Related.** CC-1, CC-19, CC-20.

## CC-22 Face sheets: icons and a half sheet no cut touches

**What it is.** FACES.PIC and FACESF.PIC (320 x 200 each) are cut up by `load_faces_pic` (GAME 851f, TAC ad31, BUG
44ad, FINAL 7e77: the same loop in four programs). The tool lists what no `cut_image` call takes
(`build/cut-content/uncut_FACES.PIC.png`, `uncut_FACESF.PIC.png`: uncut pixels bright, cut ones dimmed).

**Evidence** (scan section 5; the cut rectangles are the ones in `face_cuts`, read from the C).
* Both sheets: two icons at about x 288..319, y 122..147 (a camera on a blue tile) and y 148..173 (a satellite
  dish or aerial on a magenta tile): no `cut_image` takes them (`build/cut-content/icons_FACES.PIC.png`). The seven
  32 x 26 strips of the bottom row (camera, wiretap trace, binoculars, a bug, a silhouette, a computer, an officer) are
  cut from FACES.PIC (camera, wiretap trace, binoculars, a box, a silhouette, a computer, an officer) and drawn as the clue-method pictures `0x60 + method` (FINAL 62db 90, GAME, TAC, BUG) and 0x65
  (GAME 63ad). The two side icons are the same size and kind of picture as the strips and are in no cut table.
* FACESF.PIC: its bottom row (y 174..199: the strips, the 16 small tiles and the single icons) is never cut from it: the
  program loads FACES.PIC after FACESF.PIC into the same page and cuts those pictures only from the second
  (tac_00e8_ad31.c 39-58). The FACESF copy of that row is a recoloured duplicate that no code reaches.
* Head cells: of the eight cells of the head row (`image 0x20..0x27` of FACESF, `0x48..0x4f` of FACES) only 0 and 4 can be
  drawn (`((look & 2) << 1) + 0x20`, final_0100_7afe.c 22 and 28, same in TAC/BUG/GAME); the pixels of cells 0-3 are
  identical, so are those of 4-7. Explained: only two head shapes exist.
* FACES.PIC face cell 0 (first hair cell) is blank: reachable (variant 0 of the hair layer), a bald head.

**Why it may be cut.** The camera and the dish look like two more of the clue-method pictures, outside the seven the
cut table takes (`clue_method = Txt[a] & 0xf`, final_0100_69b3.c 79, can be larger than the seven). Only a guess.

**Status.** Possible; open. Not checked: pixels the programs copy straight from the page (a `copy_rect` from the sprite
page) instead of cutting them. For the face sheets the sheet page is overwritten by the next picture load, which makes
that unlikely.

**Not covered.** The other sheets (SPRITES/SPRITESF, BUGS, CAMERA, EQUIP2, CHASE.PIC) were rendered the same way
(`uncut_<NAME>.png`); their uncut areas are dominated by cells the programs copy with `copy_rect` (TAC 5674/5962, BUG
09cc/14e8, CHASE 2012/2400/2496) and were not resolved cell by cell. The first cell-by-cell result: CAMERA.PIC is
entirely taken by its three cuts.

## CC-23 Sound commands: never sent, and sent into silence

**What it is.** The drivers have 47 commands (0..46). The game sends 45 of them. Four drivers' command tables show
which of the sent ones are empty routines.

**Evidence** (scan section 7; `DoSound` arguments of the reconstructed C, the 47-word tables of the unpacked drivers).
* Never sent by any program: command 15 and command 21. ISOUND carries sounds for both (a sweep at DS:02dc, a noise at
  DS:0324; csharp `IbmSoundDriver.SoundOf`); TSOUND's 21 and RSOUND's 21 are empty.
* ISOUND (PC speaker): commands 39..44 jump to one `ret` at 025f and 26, 27 to a bare `ret` (they only store the engine gear).
  The game sends all of them: 0x27 (CHASE 0842 car-choice screen, TAC 79ea equipment screen), 0x28..0x2b (FINAL 3bcc, one per
  vacation place), 0x2c (TAC 0bcb, ASOUND's gunfire), 26/27 (the car engines in every chase). On the speaker those moments are silent.
* TSOUND (Tandy): 21, 22, 24, 25 are bare `ret`s at 0130. ASOUND (AdLib): 22, 24, 25 do nothing (csharp
  `AdlibSoundDriver.cs`, routines 11bb, 11c9, 0f48). The game sends 22, 24 and 25 in the car chase: 22 when a car starts off in gear 0 or at
  frame 0x20 (chase 0dc6 65, 105), 24 on shifting up from a gear, 25 on shifting down (0dc6 107, 120). Only the speaker driver has
  sounds for them (noise sounds at DS:0330, 0348, 039a); AdLib and Tandy players hear no gear shifts.
* RSOUND 21 is empty (round brief); its other empty entries were not examined here (it is ported in parallel).
* No sound number above 46 is sent; no `.PAN` sends one (CC-21).

* TSOUND (Tandy), from its port (14 Nov 2026, csharp `Audio/Tandy`): the empty commands 21, 22, 24, 25 are
  confirmed; three more pieces are never reached: the overlay priority check at 05ba, the noise overlay at 0669 and a
  mute flag at driver DS:00F0 that nothing sets.
* RSOUND (MT-32), from its port: command 21 is its only empty entry (54 distinct routines for 55 commands).

**Status.** Explained as a difference between drivers. Whether the missing commands 15 and 21 were once game effects
(a sweep and a noise exist for them on the speaker) is not shown by anything.

**Not searched.** Tune and effect data blocks of each driver that no command reaches (the C# `TunesOf` / `Noise` /
`Sweep` tables are the starting point); the signatures and dates are in "Checked".

---

## Checked, nothing found

* **File names.** All 26 `.PIC` files and all `.PAN` files but CAPTRD01 are named by a program (scan section 1); the
  names that look absent are built at run time: `crimeN`/`worldN` (digit patched into `"crime0.dta"`/`"world0.dta"`),
  `blding0N` (CC-1), `?graphic.exe` / `?sound.ca` (letters from INTRO), `<place>back.pic`/`hand`/`babe`/`dude`
  (FINAL 3bcc), `<city>.pic` (GAME 3cfd), `back.pic`, `f.pic`, `m.pic`, `laudude1.pic` (all inside FINAL3/4.CAT),
  `animation.pan` and `env.sve` (buffers, not files). `FAME.DTA` is 0 bytes by design (written when a case
  ends). `MISCOVR.EXE` is the internal name of `MISC.EXE` (a 468-byte keyboard and joystick overlay: int 21h, int 16h,
  port 201h).
* **Catalogues.** FINAL3.CAT (22 pictures), FINAL4.CAT (18): every picture is reachable from FINAL's
  `vacation_table` (21 rows) and the `laudude1.pic` overlay. CITIES.CAT (39): exactly the 39 distinct cities of the
  three WORLD files, names matching.
* **Plot table.** All 13 crime files are named by some plot slot (CRIME1 only by the tutorial and slot 7: CC-5).
* **Texts.** All 21 PROSE.DTA keys are asked for; TEXT.DTA `SLOC/RLOC/SORG/RORG 00..07`, `ALRT00..04` and
  `AIDD00..01` are all reachable by GAME 7cf4 (the `FLUF` ones are CC-11); the 32 two-digit CLUES.TXT tags
  (`*C00..*C73`) and the 4-digit ones are asked for by 570d.
* **Fonts.** FONTS.CV has three fonts (characters 32..126, 32..127, 32..122); the programs use font numbers 1, 2, 3.
* **Pictures.** All 26 `.PIC` sheets were rendered and looked at (`build/cut-content/sheet_0..4.png`): nothing
  unexplained. `AD.PIC` ("other exciting games from MicroProse") is shown when the game ends (GAME 034e).
* **Strings.** A scan of every module's data group for text no code and no pointer reaches found only the library
  notes (CC-9), table entries reached by index (room, tile and plot names) and C library messages. No developer or
  debug messages, no hidden credits, no cheat strings in any EXE or driver; the driver signatures carry dates only
  (`CGRAPHIC.EXE08-08-89`, other graphics drivers 02-20-89, sound drivers 09-13-90..10-18-90).
* **README.DOC claims.** "Hall of Fame ... '1' through '5' will recall Max's vacation snapshot" is implemented (FINAL
  4d67 line 110 calls `post_case_vacation`). Double-agent false clues: CC-17.
* **Launcher tables.** Module codes 1, 2, 3, 4, 6, 7, 8, 9, 0x0a, 0x0b..0x11 each have a producer; 0 and -1 too; 5 and
  0x63 do not (CC-3, CC-4).
* **WORLD files.** Cities and organisations parse as 16 x 34 and 16 x 36 bytes after a 4-byte header; no
  unlisted record; the sizes of all three files are equal (1124).
* **INSTALL.BAT / BINSTALL.BAT / README.BAT.** Copy `*.*` from the disks; name no file that is missing.

* **Animations (second pass).** All 24 `.PAN` files replayed in the tool's VM with the script variables the game
  passes: every image is shown but CAPTRD01's image 6 (CC-19); no sound instruction anywhere; the unreached code is
  CC-20/21 and compiler artefacts (CC-21).
* **The dead list.** Re-checked from the programs' bytes (CC-10): all 41 dead.
* **Sound commands.** CC-23: the numbers sent, the empty routines of ISOUND and TSOUND.

## Not searched (for the next pass)

* Sprite sheets, the rest: the face sheets are done (CC-22; their head, strip and tile indexes were read from the
  callers). Not done: which cells of SPRITES/SPRITESF (TAC), BUGS, EQUIP2, CHASE.PIC, GUYS2/3, ICONS, CARS the code
  draws by `copy_rect` or by a computed handle; the uncut-pixel renders exist (`uncut_<NAME>.png`) but the blit
  rectangles were not tabulated, so the large bright areas of those sheets mean nothing yet.
* Tune and effect data of the four sound drivers that no command reaches; RSOUND's and ASOUND's command tables
  (not at offset 0x48 like ISOUND's and TSOUND's); the graphics drivers' routines.
* Fields of the WORLD files beyond the ones the code names (organisation `ethnic`, `strength`, `divisor`, city
  `size` values) against what the code can reach.
* Dead branches inside live functions of GAME, FINAL, TAC, BUG, CHASE that are not named by a `// DORMANT:`
  marker (the markers are the ones the C# port met; a systematic pass over the unported-looking arms of the C would
  find more).
* Event `score` values, `exposure` values and the `items` word (destroy/receive masks) against the code's tests.
* Runs of the original for CC-1..CC-3, CC-5, CC-8, CC-11, CC-16, CC-20 (every entry says what to patch). The `.PAN`
  results of CC-19..21 come from the tool's own replay of the script machine, not from the original.
