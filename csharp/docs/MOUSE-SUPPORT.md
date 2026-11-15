# Mouse support: how it could be added

Study of 14 November 2026; **phases 0, 1 and 2 implemented on 15 November 2026** (section 6): the mouse works in the
menus, the lists and on the flight map as a player option, off by default. Phases 3 to 5 are still planned. Question: the original's shared library carries int 33h mouse
routines that no program calls (`LibraryInput.cs` "DORMANT", `docs/cut-content.md` CC-15). Could the rewrite offer
a mouse, how much of it is the original's own code, and what does it cost?

Short answer: feasible, in phases. The original gives **the driver calls only** (nine small services, no caller, no
screen layout, no hit test); everything that decides what a click means is **new code**. The cheapest and most
useful first step is a mouse for the message boxes (`LibraryMenus.ShowBox`), which every part shares.

## 1. What the original's mouse routines are

### 1.1 The routines (all matched, all ported, none called)

Each is a few lines of C around `int86(0x33, mouse_regs, mouse_regs)`; the register block `mouse_regs`
(DS:1d80) and four variables (DS:1db4 buttons, 1db6 x, 1db8 y, 1dba press count) exist in every copy.

| routine (CODE addr; same code in FINAL, CHASE) | int 33h function | what it does | C# |
|---|---|---|---|
| `mouse_reset` 0000:27f0 | 0 | reset/detect; pointer hidden, centred, no limits (the result in AX is not read) | `LibraryInput.MouseReset` |
| `mouse_show_cursor` 0000:2806 | 1 | show the driver's pointer | `MouseShowCursor` |
| `mouse_hide_cursor` 0000:281c | 2 | hide it | `MouseHideCursor` |
| `mouse_get_status` 0000:2832 | 3 | BX buttons (bit 0 left, 1 right), CX x, DX y into `mouse_buttons/x/y`; returns y | `MouseGetStatus` |
| `mouse_set_position` 0000:285a | 4 | CX = x, DX = y | `MouseSetPosition` |
| `mouse_button_press` 0000:2880 | 5 | per button (BX): presses since last call (BX out), position of the last press; into the variables; returns y | `MouseButtonPress` |
| `mouse_button_release` 0000:28b8 | 6 | the same for releases | `MouseButtonRelease` |
| `mouse_set_range` 0000:28f0 | 7 and 8 | horizontal limit (CX..DX) then vertical limit | `MouseSetRange` |

* **Coordinates.** Nothing converts them. The driver's own units apply: in the game's modes (13h MCGA, 0Dh EGA,
  both 320 wide) int 33h counts 0..639 across and 0..199 down, so game pixel x = `mouse_x / 2`, game pixel
  y = `mouse_y`. `IMouseDriver` documents the same ("0..639 across and 0..199 down"). The routines never
  scale; a caller has to.
* **Where they exist.** Only in **CODE** (cryptography), **FINAL** (headquarters) and **CHASE** (car chase)
  (`CODE 0000:27ee` ... `28f0`, `FINAL 0100:beb1` ... `bfd0`,
  `CHASE 00df:403c` ... `413e`; `docs/source-map/original-functions.csv`). **GAME (campaign: city,
  travel map, clues), TAC (raid) and BUG (electronics) do not link them at all**, and INTRO/COVERT/MISC have no
  int 33h. So even "wiring up the original's own code" would have to add the routines to the other programs'
  paths; in the C# this is free (one `LibraryInput`), but it is not what the original binaries would do.
* **Nothing else is left.** The related stubs are `animation_begin` (CODE 0000:27ec) and `animation_end`
  (27ee), both empty (RET); the notes call them the stubs of "the mouse/animation support ... not in this
  build", which is our guess (CC-15 says exactly: "Whether a mouse option was planned for the game is not shown by
  anything"). `tac_00e8_a948.c` and `ab21.c` say "probably hide/show the mouse cursor" around drawing: also a
  guess; their bodies call no mouse routine.

### 1.2 Which screens they were written for: not shown

The routines are a generic library (the same eight routines, same order, in three programs). No caller, no
range set anywhere, no pointer shape. What can be said from the callers' *shape* instead:

* The original's input model is **"the joystick is a keyboard"**: `poll_joystick_key` (CODE 0000:259f, and a copy in
  every program) turns the stick into Up/Down/Return/Esc and is polled in the same loops that wait for a key:
  `while (key == -1 && no_key_waiting())` (menu box `quit_prompt`, the travel map, the clue list, the car offers,
  the kit screen, the electronics board, the raid). A mouse written for this game would very probably have been the
  same kind of device (a pointer that fills in `key`), in the same loops.
* Comments in the readable C that mention "mouse" (`final_0100_8f74.c` `quit_prompt`: "reads a key or a mouse
  click", `game_00f7_98c8.c`: "wait for a mouse event or a key", `final_0100_1394.c` copy-protection faces: "keypad,
  joystick or mouse", `game_00f7_c6ae.c`) were written by the decompiler stage before it was known that those loops
  poll the **joystick**; the code under them calls `poll_joystick_key`/`joystick_key`/`read_joystick_dir`. They are
  not evidence of a mouse. (Worth correcting in the C comments when someone touches them.)
* Geometry the library would have used is in the box code: a box's text lines are 8 px apart; a choice's
  highlight rectangle is at `y = (firstItemRow + choice) * 8 + boxY + 4`, `x = boxX + 3`, width `menuTextWidth + 3`,
  height 9 (`recolor_rect` in `quit_prompt`); the box's right and bottom edges are in `MenuRight`/`MenuBottom`
  (`LibraryMenus.MeasureBox`). That is exactly the data a mouse hit test needs; the original never used it for one.

Verdict: **dormant library code; the screens it was written for are not recoverable.** What it gives a mouse option:
the nine service signatures (already in `IMouseDriver`), the variables, and the oracle-proven port. What it does not
give: pointer drawing, units conversion, hit tests, any wiring.

## 2. Every input point and how a mouse could drive it

Legend: **O** = the original's dormant calls would be used as they are (driver reading by `mouse_get_status` /
`mouse_button_press`); **N** = new code (hit test or screen-specific meaning). Note even an "O" screen needs N for
what a click means: the O part is only reading the pointer.

| # | input point | where (C#) | keys today | mouse use | O/N |
|---|---|---|---|---|---|
| 1 | **Message / menu boxes** (`quit_prompt` / `ShowBox`): city places, hotel, headquarters and data menus, Alt-Q "quit?" boxes, "press a key" boxes, disk prompts | `Library/LibraryMenus.cs` (`ShowBox`, `WaitForInput`, `MoveHighlight`) | Up/Down, Return/Space/F1, Esc, Alt-A/S/J/Q | **pointer over a choice line moves the highlight; left click = Return; right click = Esc.** Hit rectangle per choice from `MenuLineStart`, line height 8, text x from box x+5. A click anywhere on a title-only box ("press a key") = any key | reading O, hit test N |
| 2 | **Travel map** (flight): world map left (x < 0xc0), list of 16 cities + "stay here" + "check data" at x 0xc0..0x140, line `i` highlight at `y = 8*i + 0x21` (line -1 stays at 0x19, line 16 = data); dot of city `i` at (`GeoX`, `GeoY`), 3x3 | `Game/CityScreens.cs` `ChooseDestination`, `CampaignScreens.DrawMapCity` | Up/Down, Return/Space, Esc | hover/click the list line (as box); **click a city dot on the map** (hit box 7x7 around the dot, or the name label drawn beside it) = pick that city; right click = stay here. Needs the cities' `GeoX/GeoY` (in state) | N |
| 3 | **City screen** (places) | the same box (1) over the city picture | as 1 | as 1 | N |
| 4 | **Clue list / interrogation lists** (`Clues` list, rows 6 px) | `Game/ClueScreens.cs`, GAME 98c8 | Up/Down, Enter/Space, Esc | rows as lines; hit test from `6*i+7`, x 0xc/0xac columns | N |
| 5 | **Copy-protection faces** (FINAL 1394) | `Final` (not yet a screen in C#) | keypad moves a frame over 5x3 cells of 64x60 px, Return/Space/Esc | click a cell = pick (cell from x/64, y/60); the check is patched out in the shipped EXE | N |
| 6 | **Headquarters**: roster, agent skills (training), briefing, debrief, hall of fame | `Final/Headquarters.*.cs` | box keys; the agent screen `EditTextField` | boxes as 1; the roster and skill pages' own loops (`Headquarters.Agent.cs:253`, `Briefing.cs:348`) as lists | N |
| 7 | **Code-name entry** (`EditTextField` FINAL 0ed2, cell 7 px, field 9 px high) and the raid terminal's `InputLine` (TAC) | `Final/Headquarters.Agent.cs`, `Raid/BuildingRaid.Terminal.cs` | printable keys, Left/Right/Ins/Del/Backspace, Return, Esc | **keyboard only.** At most: click inside the field moves the cursor (`x / 7`); right click = Esc. No on-screen keyboard (out of scope) | N |
| 8 | **Crypto workstation** (CODE, has the dormant routines) | `Crypto/CryptoWorkstation.cs` `CryptoKeyStep`, `WaitForKey`, `GuessLetter` | letter = cipher letter, then plain letter; Esc leave, F1 hint, F10 clear, Alt-S/P/Q | layout: letter column x 4 (guess), 12 (letter), 20 (count), row `y = 9 + 7*cipher`; cipher text cells `x = 44 + 6*col`, `y = 16 + 16*line` (42 columns, 11 lines), the guess under each at +7. **Click a letter row or a text cell = choose that cipher letter** (same as typing it); the plain letter stays a key, or a small alphabet strip is drawn (new graphics); right click = clear the guess (the "any other key" path); small on-screen "Hint / Clear / Leave" hot spots in the top bar (y 0..8, x >= 34) | N (O only for reading the pointer) |
| 9 | **Electronics workbench** (BUG) | `Electronics/ElectronicsWorkbench.Board.cs` `PuzzleInputStep`/`HandleKey`/`MoveCursor`/`SwapChip` | cursor keys and the 8 diagonals, Return/Space = swap, Esc, Alt-S/P/Q | chip slots at `(32*col + 40, 32*row + 8)`, 16x16, cols 0..5, rows 0..4: **click a slot = move the cursor there and swap** (hover = cursor); right click or a "done" hot spot = Esc | N |
| 10 | **Building raid** (TAC): walking, facing, running, firing, F1..F10, Alt-keys; arrest prompt; kit/equipment screen; file-room terminal | `Raid/BuildingRaid.Turn.cs` `ActOnKey`, `ReadJoystickKey`; `Kit.cs` `WaitForKitKey`; `Terminal.cs` | keypad digits (8 directions, run key 5/'5' = `KeyRun`), Space, F-keys | the real-time part has **no pointer concept**: a mouse is new design. Options: hold the left button = walk toward the pointer (8 octants from the player's screen position to the pointer), right click = Space (action), wheel or middle = F10 (weapon), left double click = run toggle. Equipment screen: the kit icons (positions `equip_icon_pos`) as a grid with the box keys | N (largest and riskiest) |
| 11 | **Car chase** (CHASE, has the dormant routines) | `Chase/CarChase.Tick.cs` `TakeKey`, `CarChase.Offer.cs` `PickOffer` | arrows = turn, `-`/`=` gear, Space = switch car, `a`/F1 = arrest, `e`/F2/Enter = enter building, `f d s` speed, Alt-keys | street view at (200, 100); offers list (rows `OfferRowHeight`) as a list; in play: left/right click = turn left/right, wheel = gear, left button held = ... (needs a design: turn relative to the car's heading, queued like `QueueTurn`) | N |
| 12 | **Hardware set-up** (INTRO, text mode 3) | `Intro/HardwareSetup.cs` (`ReadKeyRaw`, the digit is the answer) | digits 1..5 / 1..6 / 1..2 | the choice lines are text rows (graphics lines at row `m + 14`, text column 0x1b); the text page is 720x400 shown at the same 4:3 place (`PresentText`): column = `x * 80 / 720`, row = `y * 25 / 400`. **Click a line = type its digit** (no game logic involved; a host-level key injection) | N |
| 13 | **"Press any key" waits and animation skips** (`LibraryScreens.WaitForKey`, `LibraryAnimation.AnimationStep`'s skip test, the briefing's `Getch`) | `Library/*` | any key | left click = any key (inject Space) | N |
| 14 | **Alt-A / Alt-S / Alt-J / Alt-P toggles** | box loop, each part | Alt-letters | not mapped; Alt-S (sound) could be a hot spot on the box only if wanted | - |

What "the original's own dormant code wired up" would mean concretely: create a `HostMouseDriver : IMouseDriver`
(§4), pass it instead of `NoMouseDriver` in `Launcher/Game.cs:103`, and have the loops of rows 1, 8 and 11 (CODE,
FINAL, CHASE, the three programs that carry the routines) call `MouseGetStatus` / `MouseButtonPress` instead of
only `PollJoystickKey`; that is the one part of the plan that mirrors the original's structure. Rows 2, 4, 9, 10 are
in programs without the routines (GAME, TAC, BUG): new code through the same `LibraryInput` methods.

## 3. Project rules

* **Purist / oracle mode stays exactly the original: mouse off.** `ARCHITECTURE.md` "Faithful by default, fixable by
  choice" and `OriginalBehaviour` (`Faithful` = what the oracle runs, `Player` = fixes). The mouse is not a bug
  fix, so it does not belong in `OriginalBehaviour` (that class is "the original's bugs the rewrite can reproduce or
  fix"). It is a **host/player option**, like the sound and joystick options: the Launcher passes
  `NoMouseDriver` (today's behaviour, register D-8) unless the player turns the mouse on. Tests and the oracle
  build the world with `NoMouseDriver` as now; an architecture test should pin "the `Faithful` world has no
  pointer".
* **Player modes may add it as a documented option.** Same pattern as Darklands (the Darklands Restoration Project,
  `Darklands.Platform/IHost.cs` `MouseState`, `GameSettings.MouseWheel`/`input.mouseWheel`, `--no-mouse-wheel`,
  `DisplayLayout.ToScreen`): there the original *does* use the mouse, and the extra "host comfort" (wheel as arrow
  keys, hide the pointer when it leaves the window) carries a `// DIVERGENCE:` / "Host enhancement" comment with the
  user's decision and a settings key. Covert needs the same: a setting (suggested `input.mouse`, `--no-mouse`, off in
  purist, on by default in player mode, the user's call), a row in `csharp/docs/faithfulness/DIVERGENCES.md`
  (a new D-n "mouse, a player option; the original's mouse routines are never called"), the `// DIVERGENCE:`
  comment at every new branch, new-code rows in `new-code-*.csv`, and an update of CC-15 ("explained" stays; add
  "an optional mouse exists since ...").
* **Keep the game logic untouched.** Every mouse meaning should end as the same action a key causes (the same
  state change, the same sound, the same redraw). The simplest way to guarantee it: a click becomes the **key the
  game already understands** (`Keys.Up`, `Keys.Return`, `Keys.Esc`, the digit) or sets the same variable the key
  handler sets (`a = choice`), never a parallel code path. Then the oracle tests, which never move the pointer,
  stay valid, and the sessions on the headless host can be scripted with pointer events the same way as with keys.
* **Traceable.** Every new routine states "new code" in its `// orig:` line (as `KeyboardDriver.Getch` does), not an
  `orig:` address.
* **Neutral comments.** Nothing in committed text names the reference decompilation.

## 4. Platform and host changes

Needed pieces (all small; most of SDL's side is already declared in `SdlNative.cs`: `EventMouseMotion/ButtonDown/
ButtonUp/Wheel`, `EventWindowMouseLeave`, `MouseButtonLeft/Right`, `SDL_ShowCursor/HideCursor`, `SDL_GetMouseState`):

1. **`IInput`** (`Platform/IHost.cs`): add a `Pointer` with the position in **game pixels** (0..319, 0..199), the
   buttons held, and the **latched** presses/releases since the last read (the original driver latches presses:
   functions 5/6 count them; the Darklands host does the same trick, `_releaseNextPump`, so a tap inside one frame
   is never lost). Add a "pointer inside the picture" flag. `HeadlessHost` implements it with scripted events.
2. **`SdlHost.Pump`**: handle the motion, button and leave/enter events; scale from window to render coordinates
   (the window uses `WindowHighPixelDensity`; `PicturePlace()` is in render-output pixels, so convert the event's
   window coordinates by `renderOutputSize / windowSize`, as Darklands' `Whole()` does).
3. **The 4:3 picture-to-game mapping.** `PicturePlace()` gives the rectangle (largest 4:3 that fits, centred). The
   inverse: `gx = (px - place.X) * 320 / place.Width`, `gy = (py - place.Y) * 200 / place.Height`, clamped to the
   picture; outside the rectangle (the black bars) the pointer is "not in the picture". **Use pixel-centre
   arithmetic, not truncation**: `SdlHost` scales with pixel-art mode (`ScaleModePixelArt`) and the text page with
   linear sampling, at non-whole factors, so the pixel under a window position must be taken at the window pixel's
   centre. Darklands has exactly this solved and tested: `Darklands.Platform/DisplayLayout.cs` `ToScreen`/`Under`
   (the tie rule, a `MouseMappingTests` that compares the mapping with the drawn picture); copy it with a
   "Reused from" header and adapt (`REUSED.md`). The text page (720x400, set-up screen) uses the same rectangle, so
   one function serves both: for text, a second step `col = gx * 80 / 320`, `row = gy * 25 / 200`.
4. **`HostMouseDriver : IMouseDriver`** in `CovertAction.Drivers` (like `KeyboardDriver`): `GetStatus` from the
   pointer state, `ButtonPresses/Releases` from the latches, units x2 horizontally (0..639), `SetPosition` and
   `SetRange` keep the game's pointer in a rectangle (the SDL pointer is the user's; the game's limits clamp what it
   *reads*, optionally `SDL_WarpMouseInWindow` as Darklands does), `ShowCursor/HideCursor` toggle the **host's**
   pointer (`SDL_ShowCursor`) while it is over the picture.
5. **The pointer picture.** DOS drew it in the driver. Here: show the operating system's pointer over the window
   (new code, nothing drawn into the 320x200 picture, so the screen-comparison tests and the 4:3 picture stay
   untouched). A game-drawn arrow (Darklands' way, needed there because the game moves it) is not needed unless the
   game must warp it (`MouseSetPosition` is never called by anything).
6. **Settings.** A `input.mouse` setting, `--no-mouse` flag; Launcher `Game.cs` picks `NoMouseDriver` or
   `HostMouseDriver`. Nothing in `Core` changes; the hit tests live in `Presentation` (they know the layouts).

Dependencies stay legal (`LayerTests`): Platform interface, Drivers implementation, Presentation consumer, Host.Sdl
and Host.Headless producers.

## 5. Phased plan, sizes, risks

Sizes are rough (new lines incl. tests; one developer-day = about 300 lines of reviewed code).

| phase | content | size | risk |
|---|---|---|---|
| 0 (**done**) | **Plumbing**: `IInput.Pointer`, `SdlHost` events + mapping (copy `DisplayLayout`), `HeadlessHost` scripted pointer, `HostMouseDriver`, setting + flag, divergence/register rows, mapping test against the drawn picture. Nothing consumes it yet; faithful mode unchanged | ~450 lines, 1.5 days | low; the mapping details (pixel-art scaling, text page, high-DPI) are the only subtle part, and Darklands already solved them |
| 1 (**done**) | **Menu boxes** (rows 1, 3, 13): in `LibraryMenus.ShowBox` input loop add a pointer branch beside `PollJoystickKey`: hover moves the highlight (reusing `MoveHighlight`), left click accepts, right click = Esc; "press a key" boxes close on a click. One place serves city, hotel, HQ, data menus, quit boxes in all parts | ~200 lines, 1 day | low-medium: must not change timing (`WaitVsync(0xf)` after a move), the animation-skip and the Alt-key paths; the C# `Menus` is shared with `Func<int>? readKey` hooks used by tests |
| 2 (**done**) | **Lists and maps** (rows 2, 4, 5): travel map list + city dots, clue/briefing lists, car offers, kit selection, faces grid; a small `PointerHit` helper (rectangles in game pixels -> choice index) | ~350 lines, 1.5 days | medium: each loop has its own redraw and sound order; the travel map needs the dot hit test and a visible hover cue (the highlight already exists) |
| 3 | **Mini-games** (rows 8, 9): crypto (click letter or cell chooses the cipher letter, right click clears, hot spots for hint/clear/leave) and electronics (click slot = cursor + swap, hot spot = done) | ~350 lines, 1.5 days | medium: the crypto plain-letter step needs a decision (keep typing, or draw an alphabet strip: new graphics and a new faithfulness boundary: the picture would differ from the original's, only with the option on); timer loops must keep their `WaitVsync` cadence |
| 4 | **Real-time parts** (rows 10, 11): raid walking by pointer, car-chase turning; needs game design (no analogue in the original) | ~600 lines, 3 days + play testing | high: easy to make the game easier or harder than the original; keep it an opt-in "mouse steering" sub-setting; verify with sessions on the headless host |
| 5 | **Set-up screen** (row 12): click a choice line = its digit; code-name field click-to-position (row 7, optional) | ~120 lines, 0.5 day | low |

Total about 7-8 developer-days for everything; phases 0+1 (2.5 days) already make the whole game usable with the
mouse for every menu, which is most of the game's input.

Risks common to all phases:

* **Faithfulness.** Each mouse branch must produce the same state as its key. Test: scripted pointer session =
  scripted key session (same final state); and "pointer never touched" = existing tests unchanged.
* **Ambiguity of the original.** No evidence what the authors intended; document the option as an addition
  ("not in the original, or not shown by it") so no purist expects it. Do not claim it restores cut content.
* **Faithful-mode leakage.** The settings default must be off wherever `OriginalBehaviour.Faithful` is selected;
  a test that builds the faithful world and asserts `NoMouseDriver`.
* **Coordinates.** The driver's 0..639 x range is an assumption of the mode's int 33h units; a game-pixel-based
  internal API (the proposed `Pointer`) avoids spreading the factor 2 around; only `HostMouseDriver` multiplies.
* **Layouts per colour mode.** CGA (`state.CGA`) and the 200-line EGA path draw some boxes differently (outline
  frames); hit tests should come from the same measured values the drawing uses, not hard-coded numbers
  (`MenuRight`, `MenuBottom`, `MenuStartItem`, `MenuTextWidth`).
* **Scope creep.** Text entry (row 7) and the Alt-letter toggles stay keyboard-only.

## 6. What is implemented (phases 0, 1, 2) and what is left

**The option.** `input.mouse` in the settings file, `--mouse` / `--no-mouse` on the command line, the launcher's
Advanced tab, Player options, **Mouse in the menus** (with a line saying it is not in the original). Off by default.
Off, `Launcher.Game` gives the game's world `NoMouseDriver` (`MouseOptionTests.WithoutTheOptionTheGamesWorldHasNoMouse`)
and no menu calls the mouse driver at all (`LibraryInput.PointerOn` is asked first), so the oracle, the session proof
and every key session are unchanged. Divergences D-630 (the option, the driver, the hosts), D-631 (the boxes), D-632
(the lists and maps), D-633 ("press a key").

**Phase 0, plumbing.**

* `Platform`: `IInput.Pointer` (an `IPointer`: position in game pixels, inside the picture or not, buttons held,
  presses and releases latched until taken), `PointerLatch` (the latch both hosts keep), `IMouseDriver.Installed` (the
  reset's answer the original never reads; false by default).
* `Drivers.HostMouseDriver`: the nine services on the host's pointer, in the driver's units (x times 2, 0..639), the
  game's ranges clamp what it reads; `SetPosition`, `ShowCursor`, `HideCursor` do nothing (nothing in the game calls
  them; the player's pointer is never moved).
* `Host.Sdl`: `PictureLayout` (adapted from Darklands' `DisplayLayout`, docs/REUSED.md): the picture's 4:3 place (the
  host's own arithmetic, now in one place), SDL's window coordinates to drawing-area pixels (high pixel density),
  the game pixel under the window pixel's centre (pixel-art and nearest scaling alike; SDL's software renderer's
  16.16 stepping where it draws), the black bars outside the picture, the text page's cells over the same place.
  `MouseMappingTests` checks it at nine window sizes and densities against the picture as drawn. `SdlHost` handles
  motion, left and right buttons and the window's mouse-leave event.
* **The pointer picture: the system's pointer**, shown over the game's window only while the option is on (hidden
  with it off: the game has no mouse then). Nothing is drawn into the 320x200 picture, so the screen comparisons and
  the 4:3 picture are untouched.
* `Host.Headless`: the scripted mouse: key-list entries `HeadlessHost.PointerMove`, `PointerPress`, `PointerRelease`,
  `Click` (the game finds no key at such a moment), `PointerReads` (how often the game looked), and in the command-line
  program `--mouse on` with `{move x y}`, `{click x y}`, `{rclick x y}`. The key list is read as the game asks for it,
  so a test can compute an entry from the screen.

**Phase 1, the boxes** (`LibraryMenus.QuitPrompt`, every part's boxes): `LibraryInput.BeginPointer` when the box
starts waiting (forgets earlier clicks, as `FlushInput` forgets keys), `ReadPointer` beside `PollJoystickKey` in the
wait. A move onto a choice moves the highlight (`selected`, then the box's own `MoveHighlight`; no joystick pause, as
for the keys), a left click on a choice is Return on it, a left click on a box without a choice to take (a "press a
key" box, a box without highlight, an OK box) is Return, a right click is Esc. The hit rectangles are the highlight
bars' (`ChoiceBar`, which `RecolorChoice` draws with). The animation step and the Alt keys are unchanged. The
"press a key" waits (`LibraryScreens.WaitForKey`) take a left click as Space, a right click as Esc. The menus read the
driver's services directly rather than through the ported `mouse_get_status` routines, which would also write the
part's mouse variables: the state with the option on stays the state the keys give.

**Phase 2, lists and maps** (`PointerHit`, `HitArea`; each screen's rectangles from the values it draws with): the
flight map (its 18 lines, and a city's dot within three pixels), the clue list, the car offers (the first pick cannot
be pointed at; there is no Esc there), the equipment screen's items (click = Space: take or give back; right click =
Esc), the copy-protection faces (click = Return; right click = Esc, which picks too).

**Proof.** `MouseOptionTests`: the menus part's Practice / level / skill path by clicks and by pointing ends in the
same hand-off block as by the keys; a right click is Esc (same block and picture); the campaign's walk (city screen,
flight map by its list and by the city's dot, hotel, quit) ends in the same block as by the keys.

**Left (planned, not in this version):**

* Phase 3, the workstations: cryptography (click a letter or a text cell) and electronics (click a slot).
* Phase 4, the real-time parts: the raid's walking and the car chase's steering (needs game design; a sub-setting).
* Phase 5, the set-up screen (click a choice line = its digit; `PictureLayout.TextCell` is ready) and the code-name
  field.
* Unchanged on purpose: text entry (row 7, keyboard only), the Alt-letter toggles (row 14), the animation skips and
  the bulletins' ticker (a key stops them; a click does not).
