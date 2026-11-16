# Release 1.0: what it is and what is left

Version 1.0 is the faithful rewrite: the game as the original plays it, and nothing the original does not have. It
stays the reference. Everything beyond the original (player options such as the mouse, the arrow keys walking in a
raid, music and effects on different sound cards) comes after 1.0, in rounds of improvements on top of it.

## Where the work happens

The repository is `covert-action-enhanced` (private for now). 1.0 is its first commit, with no earlier history.

* `release/1.0`: the faithful line. It starts at the commit "Covert Action 1.0", tagged `v1.0`. A fix to 1.0 (a
  difference from the original, a crash) is made here.
* `main`: the default branch, 1.0 plus the player options (D-610, D-620, D-630) and the later rounds of improvements.
  Each time `release/1.0` moves, it is merged into `main`; nothing goes the other way.
* The history before 1.0 (the repository `covert-action-restoration`, deleted on 15 November 2026 once 1.0 was
  pushed here) is kept outside the repository.

## What 1.0 means

1.0 can be tagged when all of these hold:

1. The whole game plays from the set-up screen to the end of a career, with every choice the original offers that the
   rewrite supports. Already true.
2. Every reachable function of the original is rewritten or set aside for a stated reason in the source map. Already
   true (`docs/OPEN-FUNCTIONS.md`: no real gap).
3. Every difference from the original is in the registers (`docs/faithfulness/`, `docs/original-bugs.md`), and none
   of them is a player option.
4. The open items below are done, or written down as known limits of 1.0 in the release notes.
5. All test projects pass, with and without the MT-32 ROMs, on a clean checkout of `release/1.0`.
6. The release (`tools/publish_release.py`) is built from that checkout, as version 1.0.0, and tried by hand.

## Open items

### Decided

- [x] **The EGA, CGA and Tandy screens (D-600).** Decided by the user on 15 November 2026: 1.0 is MCGA only, and the
  set-up screen asks no questions. The game starts with the set-up screen's framed title (the version and the
  copyright) and "One Moment Please..." for two seconds, then goes on (`HardwareSetup.ShowTitle`). The graphics answer
  is always MCGA: MCGA, EGA, the "VGA" choice (which loads EGRAPHIC, CA-48) and Tandy show the same picture, since the
  game only ever uses colour numbers 0..15 (13 distinct palettes over the 95 recorded sessions), which MGRAPHIC maps to
  the 16 RGBI colours EGRAPHIC's palette registers give (`docs/vga-check.md`); only CGA differs, and it is not offered.
  The sound card (`sound.card`, AdLib by default) and the controls (`input.controls`, keyboard and joystick by
  default) are the launcher's settings, written into the hand-off block as the questions' answers were (D-603). The
  original's questions stay in the rewrite, proven (`HardwareSetup.HardwareSetupMenu`, `IntroOracleTests`,
  `HardwareSetupTests`), and the whole-session proof still asks them with the recordings' keys
  (`GameOptions.AskSetUpQuestions`). To say in the release notes.

### The session proof's gaps

Proven function by function by the oracle, not yet in a recorded whole session (`docs/SESSION-PROOF.md`, summary):

- [x] The jailbreak raid (raid code 8): `docs/SESSION-PROOF.md` item 13, 5 sessions in a level-3 case (the Paymaster
  arrested with the Payoff's evidence, made Sprung by the plot, GAME's warning, the jail-break raid, GAME's return with
  the prisoner escaped), 1532 checkpoints, all agree; two C# bugs fixed in the raid (the plot object's guards, tile 63).
  The jail break foiled (the agent wins the raid): item 15, 2 sessions, 593 checkpoints, all agree.
- [x] Campaign wire taps at levels 1 and 3 (level 2 is done): `docs/SESSION-PROOF.md` item 10, 8 sessions from a new
  case at each level to the tap and back to GAME, all agree; one C# bug fixed (BUG's clue report source, `coded_flag`).
- [x] An ambush won: TAC/street-ambush-won (the five attackers shot, "You have eliminated all of your enemies.") and
  its return GAME/ambush-won-return (the attackers' building found), 417 checkpoints, all agree (SESSION-PROOF.md
  item 11).
- [x] DOS outside the modelled services: `docs/SESSION-PROOF.md` item 14. A save that cannot be made says "File Error" as
  the original (GAME/save-cannot-create) and the floppy prompt with TAC.EXE missing asks for disk C as the original
  (GAME/breakin-no-tac): 2 sessions, 655 checkpoints, agree. A full disk (the original says "Game has been saved." over a
  cut file; so does the rewrite), missing files (the launcher names the file) and saved games that are not whole (listed
  "(EMPTY)", D-105) are read from the original's code and tested (`DiskFailureTests`). Three C# bugs fixed. Left: a full
  disk is not recorded, since the C# side has no full disk to give.

### The original's devices

- [x] **The joystick** (the set-up's "1. Joystick and Keyboard", Alt-J): MISC.EXE's calibration and reading of the
  game port are rewritten (`KeyboardDriver`, proven on 616 readings of the player's MISC.EXE on a fed port,
  `MiscJoystickOracleTests`, vectors `build/oracle-joystick-20261115`); the SDL host gives a joystick or gamepad as
  the PC's analog joystick (`SdlJoystick`, the counts a model: U-12). D-8 keeps only the mouse, which the original
  never asks for.
- [x] A recorded whole session with the stick moving (SESSION-PROOF.md, item 12): the session `joystick-menu` puts a
  stick on the game port of both sides (the original side answers MISC.EXE's port 201h reads with the counts of
  `JoystickReading.FromStick`'s model, U-12; the headless host gets the same reading at each entry). The calibration at
  rest, the stick moving the main menu (pushed, held, up, a diagonal), both buttons and the practice menus agree at all
  70 checkpoints (INTRO, FINAL, CODE). The rewrite side alone is also tested
  (`LauncherTests.TheJoystickChosenInTheSettingsMovesThroughTheMainMenu`); `joystick-quit` covers the calibration
  without a stick.

### The function oracle's skips

None of the oracle's tests is skipped any more (vectors `build/oracle-next-20261116`; `COVERT_ORACLE_DIR` is now
`build/oracle-1.0`, the same with U-204's case added, see `build/oracle-1.0/README.txt`:
735 pass with the session recordings' saves; with `build/oracle-next-20261115` it was 709 pass, 17 skipped). `oracle-next-20261116` is a copy of
`oracle-next-20261115` with the vectors below added (`make_vectors-<MODULE>-copies.log`); the older folder is kept.

- [x] 15 copies of shared routines with no recorded cases in that module (tools/oracle/README.md, "State donors";
  `Harness/TwinCopies.cs`):
  - vectors on the module's own bytes: `read_random_variant` in FINAL, GAME, TAC and BUG (state donors: the coded
    messages' calls, `"text.dta"` and the program's own `*SLOC00`/`*RORG00`/`*FLUF00`/`*AIDD00` literals, on the
    states of a text being read); BUG `read_text_with_vars` and `read_cleared_text` (donors on BUG's menu state; the
    test now knows BUG's C library file internals, which no recorded BUG run had shown); `sub_final_0100_af04` in
    FINAL and GAME (an empty function no database had: `MERGED_ENTRIES`); GAME `trim_text_to_width` (donor,
    flagged: GAME has no caller). BUG's `load_event_text`, `expand_clue_text`, `activity_clue_text` and
    `lookup_clue_text` now compare on their existing synthetic vectors (they had failed on the same file internals).
  - proven the same routine as a recorded copy (a byte comparison on the player's programs that accounts for every
    difference as a call of the same routine, a pointer to the same text or a listed variable; `TwinCopiesTests`,
    and run by the copy's own test): GAME `play_animation` (`run_pan_scene` = FINAL's, no difference at all), GAME
    `draw_sprite` (`draw_actor` = FINAL's; every recorded GAME call draws a sprite whose parent is off the stage and
    takes its place from an uninitialised local, so none compares), GAME `trim_text_to_width` (= FINAL's), BUG's four
    clue and event texts (= TAC's).
  - The boundary fake took char_width's character as a signed char; `text_width` passes it unsigned. Every graphics
    driver reads only its low byte (`TwinCopiesTests.EveryGraphicsDriverReadsTheCharacterOfCharWidthAsOneByte`), so
    the fake now accepts either; no C# change.
- [x] 2 save-file tests need saved games made by the original (`SaveFileTests`): they now also read the saves the
  original program wrote in the session proof's recordings (`original-*/written/` under the session folder), and pass.

### Registers

- [x] Go through the uncertain points (`docs/faithfulness/UNCERTAIN*.md`), 15 November 2026: 23 entries, 15
  removed: 12 settled as the C# has them (each by a reading of the original's code, a recording or a run, said in
  its commit and in the registers' notes), one moved to the original's bugs (U-206, CA-57), one a C# fix (U-204: a suspect outside the cast
  with a known role read the neighbouring words, as the original does; its case is in `build/oracle-1.0`) and one a
  divergence (U-101, D-104: FINAL's exit code 130 when the game ends, never read). Kept as stated limits, 8: how the
  speaker, the AdLib, the MT-32, the Tandy chip and a real joystick behave (U-6, U-7, U-8, U-10, U-12), and what a real
  PC's memory holds where the original reads memory nothing wrote (U-9, U-11, U-500).

### Release

- [x] Release notes for 1.0 (`releases/1.0.0/release-notes.md`): what is in it, what is not (the known limits above),
  and the divergences a player could notice. The launcher's "What's new" shows them: the version is `1.0.0-dev`
  (`Directory.Build.props`) and `publish_release.py` builds 1.0.0 by default. To bring up to date as the open items close.
- [x] The README's status and the screenshots brought up to 1.0 (`docs/screenshots/start.png` from
  `HardwareSetupTests.TheTitleAloneRendersAndCanBeSavedAsAPicture` with COVERT_SCREENSHOT_DIR; `launcher.png` from
  the release's `CovertAction.exe --launcher-screenshot`).
- [x] A final read of `THIRD_PARTY_NOTICES.md` and the licences shipped in the zip (15 November 2026): the notices
  open on a table of what the release carries; `Mt32emu.dll` (the binding, LGPL) and CovertActionTools' MIT notice
  (the EXEPACK and PAN readers follow its code) were added to `licences/`; `publish_release.py` now refuses a release
  without every licence file.
- [x] Tag, the new repository, and the steps under "Where the work happens" (15 November 2026): 1.0.0 built from a
  clean checkout of `release/1.0` and tried by hand; `v1.0` on the first commit of `covert-action-enhanced`; the
  player options one commit on `main`; the old repository deleted.

## Not in 1.0

The player options (on `main`), and anything else the original does not do: the mouse beyond what the original
read, the arrow keys walking in a raid, music and effects on different sound cards, other platforms than Windows.
