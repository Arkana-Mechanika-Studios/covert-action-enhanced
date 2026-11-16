# Covert Action (C# rewrite) 1.2

The third round of improvements on top of 1.0, the faithful rewrite of Sid Meier's Covert Action (1990). It is an
unofficial fan project that rebuilds the original from its own code, part by part. It reads your own copy of the game;
none of the game's files come with it. Everything new is a player option, off by default: with the options off, 1.2
plays exactly as 1.0 and the original.

## What's new

- **The Amiga version's music, sound effects and pictures**, read from the Amiga version's files you own (none come
  with the program; on the launcher's Advanced tab, choose the folder that holds the files of the Amiga disks).
  - **Music** (D-640): the Amiga's tunes in place of the sound card's, played by a player of their own. Where the
    Amiga version has no music (the equipment and car choices, the vacations), another of its tunes plays, or nothing
    with "Only where the Amiga had it".
  - **Sound effects** (D-641): the Amiga's effects where its game plays the same sound (27 of the 34 matched to the
    PC's by where the two programs play them); the sound card's effects elsewhere, or silence.
  - **Pictures** (D-642): 32 pictures whose layout is the PC's: the faces, icons, three flight maps, the hotel, the
    raid's item icons, the car chase's windscreen and most city pictures, in the Amiga's colours. Pictures the Amiga
    redrew in another layout, those the game reads back, and the animations stay the PC's.
- **Fix the original's bugs** (D-650, off by default; on the Advanced tab, `fixes.originalBugs`). The original's bugs that
  do not change the story, each confirmed by a test (docs/original-bugs.md has the list and the evidence):
  the Bagman's clue no longer reads "is information requires security clearance" (CA-16); the key that skips an
  animation is taken out of the keyboard buffer (CA-34); the chief's comment after a capture is the one for that
  rank, not the first capture's (CA-41); Delete in the name field takes out one character, not two (CA-43); in a car
  chase F2 with a building you do not know yet no longer stops the game (CA-4); the wiretap puzzle's last evaluation
  at the end of the time always draws the terminals (CA-40); the Tandy driver ignores a second pause with no resume
  (CA-55; the game never does this); a desk or couch no longer has its other half drawn over a wall (CA-59);
  "Stasi" is found in the file room (CA-61); an electronics board nobody can win is drawn again (CA-62); a held
  key in a raid repeats as the raid meant to set it, 250 ms then 15 a second, so walking at the fast speed does not
  stop between steps (CA-63); a Hall of Fame file that is missing is made in the saves folder, so scores are
  saved (CA-64). A game saved with the option on loads without it and in the original. The
  second option, **Also fix campaign-logic bugs** (`fixes.campaignBugs`, independent of the first), fixes the bugs
  that change events, scores, layouts or the career, each confirmed by a test on the game's own data: CRIME1's "Stole
  Blueprints" bulletin is no longer cancelled (CA-12); the receive events of CRIME2 and CRIME8 that name their own actor
  answer their sender (CA-13); CRIME12 ends as "crime carried out" when its last event happened, not always as
  "prevented" (CA-15); big buildings no longer get two doors between the same rooms (CA-28); intercepted messages pair
  with their own partner (CA-32); cast members are seen leaving a city (CA-35); new persons get a face of their own and
  start unalerted (CA-36); the training ratings shown are the ones kept (CA-42); the apartment scene searches the new
  plot after a new case is chosen (CA-44); a case rated 255 or 511 no longer gets overwritten in the career (CA-46); coming back
  from the cryptography workstation no longer costs extra hours left over from the last raid or car chase (CA-7); a new
  case can no longer hang while it is laid out (CA-58); the Mastermind's meeting of CRIME10 is with the Travel Agent,
  as its pair, not the Extractor (CA-60); a new agent made after a practice mission has the skills he chose, not
  those added to Good (CA-65). A
  game saved with it loads without it and in the original; the files keep their format. It changes the story: a case
  does not play as in the original.
- **Everything of 1.1.5, 1.1 and 1.0.** The mouse in the menus and the action parts, arrow keys that walk in a raid,
  music and effects on different sound cards, and the whole faithful game.

## Getting started

1. Unpack the zip into a folder of your own (not into the game's folder).
2. Start **CovertAction.exe**. If your copy of Covert Action is not found, the launcher asks for its folder (the one
   with COVERT.EXE, INTRO.EXE, FINAL.EXE, MGRAPHIC.EXE and FONTS.CV).
3. **CovertAction Launcher.exe** opens the settings at any time, as does Shift held while the game starts. The player
   options, the Amiga ones among them, are on its Advanced tab. Its **Play** starts the game.

Saves made with 1.0, 1.1, 1.1.5 (or with the original) load as they are.

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
- The bug fixes: with "Fix the original's bugs" off the game keeps the original's bugs, also the car chase stop of CA-4
  (the original reads a leftover value there; the rewrite stops with an error). The campaign-logic option leaves CA-14 (two senders
  of one message id: the data reads as alternatives, one of the two) as the original has it. CA-6 (the original's 12000-byte hand-off block is too small for two crimes at the hardest level) does not occur
  in this game, which does not keep that block; what the original does there was not tested.
- Windows only (64-bit).
