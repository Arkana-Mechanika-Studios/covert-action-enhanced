# Covert Action (C# rewrite) 1.2

The third round of improvements on top of 1.0, the faithful rewrite of Sid Meier's Covert Action (1990). It is an
unofficial fan project that rebuilds the original from its own code, part by part. It reads your own copy of the game;
none of the game's files come with it. Everything new is a player option, off by default: with the options off, 1.2
plays exactly as 1.0 and the original.

## What's new

### The Amiga version's music, sound effects and pictures

Read from the Amiga version's files you own (none come with the program). On the launcher's Advanced tab, choose the
folder that holds the files of the Amiga disks, then turn on what you want:

- **Music** (D-640): the Amiga's tunes in place of the sound card's, played by a player of their own. Where the Amiga
  version has no music (the equipment and car choices, the vacations), another of its tunes plays, or nothing with
  "Only where the Amiga had it".
- **Sound effects** (D-641): the Amiga's effects where its game plays the same sound (27 of the 34 matched to the PC's
  by where the two programs play them); the sound card's effects elsewhere, or silence.
- **Pictures** (D-642): 32 pictures whose layout is the PC's: the faces, icons, three flight maps, the hotel, the raid's
  item icons, the car chase's windscreen and most city pictures, in the Amiga's colours. Pictures the Amiga redrew in
  another layout, those the game reads back, and the animations stay the PC's.

### Fixes for the original's bugs

Two options (D-650), independent of each other and off by default; each fix is confirmed by a test, and the list is
under "The bug fixes in detail" below. Several of the bugs were reported by players over the years and then found in
the original's code. A game saved with either option on loads without it, and in the original.

- **Fix the original's bugs** (`fixes.originalBugs`): twelve bugs that do not change the story, from a garbled clue to a
  board that cannot be won, a pause in a raid's walking and scores that were never saved.
- **Also fix campaign-logic bugs** (`fixes.campaignBugs`): fourteen bugs that change events, scores, layouts or the
  career, from events that never happen to a new case that hangs while it is laid out. A case then does not play as in
  the original.

### And the rest

- **Everything of 1.1.5, 1.1 and 1.0.** The mouse in the menus and the action parts, arrow keys that walk in a raid,
  music and effects on different sound cards, and the whole faithful game.

## Getting started

1. Unpack the zip into a folder of your own (not into the game's folder).
2. Start **CovertAction.exe**. If your copy of Covert Action is not found, the launcher asks for its folder (the one
   with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** opens the settings at any time, as does Shift held while the game starts. The player
   options, the Amiga ones and the bug fixes among them, are on its Advanced tab. Its **Play** starts the game.

Saves made with 1.0, 1.1, 1.1.5 (or with the original) load as they are.

## The bug fixes in detail

docs/original-bugs.md has each one with its evidence.

**Fix the original's bugs** (`fixes.originalBugs`), for the bugs that do not change the story:

- the Bagman's clue no longer reads "is information requires security clearance" (CA-16);
- the key that skips an animation is taken out of the keyboard buffer (CA-34);
- the chief's comment after a capture is the one for that rank, not the first capture's (CA-41);
- Delete in the name field takes out one character, not two (CA-43);
- in a car chase, F2 with a building you do not know yet no longer stops the game (CA-4);
- the wiretap puzzle's last evaluation at the end of the time always draws the terminals (CA-40);
- the Tandy driver ignores a second pause with no resume (CA-55; the game never does this);
- a desk or couch no longer has its other half drawn over a wall (CA-59);
- "Stasi" is found in the file room; the game spells it "Stassi" (CA-61);
- an electronics board that a search finds no way to win is drawn again (CA-62);
- a key held in a raid repeats as the raid meant to set it, 250 ms then 15 a second, so walking at the fast speed no
  longer stops between steps (CA-63);
- a missing Hall of Fame file is made in the saves folder, so scores are saved (CA-64).

**Also fix campaign-logic bugs** (`fixes.campaignBugs`), for the bugs that change events, scores, layouts or the
career. It changes the story: a case does not play as in the original. The save files keep their format.

- CRIME1's "Stole Blueprints" bulletin is no longer cancelled (CA-12);
- the receive events of CRIME2 and CRIME8 that name their own actor answer their sender (CA-13);
- CRIME12 ends as "crime carried out" when its last event happened, not always as "prevented" (CA-15);
- big buildings no longer get two doors between the same rooms (CA-28);
- intercepted messages pair with their own partner (CA-32);
- cast members are seen leaving a city (CA-35);
- new persons get a face of their own and start unalerted (CA-36);
- the training ratings shown are the ones kept (CA-42);
- the apartment scene searches the new plot after a new case is chosen (CA-44);
- a case rated 255 or 511 no longer gets overwritten in the career (CA-46);
- coming back from the cryptography workstation no longer costs extra hours left over from the last raid or car
  chase (CA-7);
- a new case can no longer hang while it is laid out (CA-58);
- the Mastermind's meeting in CRIME10 is with the Travel Agent, its pair, not the Extractor (CA-60);
- a new agent made after a practice mission has the skills chosen in training, not those added to Good (CA-65).

## Differences from the original a player could notice

With the player options off, the same as 1.0: see its release notes (csharp/releases/1.0.0/release-notes.md) and
csharp/docs/faithfulness/. With them on, the options above and those of 1.1 and 1.1.5 (D-610, D-620, D-630 to D-636,
D-640 to D-642, D-650).

## Known limits

- The Amiga options need the Amiga version's own files; without them the PC's music, effects and pictures play.
- Amiga pictures: the raid's screen flashes do not tint the Amiga-coloured parts (the face icons of the raid's panel).
  The Amiga's animations and the pictures it redrew are not used.
- Amiga effects: how often an effect repeats is read from the Amiga program, not heard on an Amiga; the
  cryptography workstation's effect is a probable match.
- The mouse works everywhere but the car chase. The joystick's readings of a real stick are still a model (U-12).
- The bug fixes:
  - With "Fix the original's bugs" off, the game keeps the original's bugs, also the car chase stop of CA-4 (the
    original reads a leftover value there; the rewrite stops with an error).
  - CA-62's search is cautious and has a limit: it may draw again a few boards that could have been won (about 1 in
    400 at the easiest level), never one it has solved.
  - CA-63's key repeat is checked by a test, not yet played by hand at every speed.
  - The campaign-logic option leaves CA-14 (two senders of one message id: the data reads as alternatives, one of the
    two) as the original has it.
  - CA-6 (the original's 12000-byte hand-off block is too small for two crimes at the hardest level) does not occur in
    this game, which does not keep that block; what the original does there was not tested.
- Windows only (64-bit).
