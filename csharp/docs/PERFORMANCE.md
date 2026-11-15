# Performance of the SDL host: why play felt laggy, and what was changed

Study of 15 November 2026. The report: the SDL build (`src/CovertAction.Host.Sdl`) felt "a bit laggy" in play (the
menus, the campaign, a raid). The game's logic and its pace are proven frame by frame against the original
(SESSION-PROOF.md), so nothing in the game was looked for or changed. The work was in how the host shows frames,
reads keys, plays sound and waits.

Short answer. The game ran at the original's pace. Four things in the host were slower or less steady than the PC:

1. **Held keys piled up.** Windows repeats a held key about 30 times a second. The PC's keyboard repeats it 10.9 times a
   second, and the BIOS holds at most 15 keys. In a raid the agent takes one key a turn, 8.75 turns a second at the
   middle speed. So each second a key was held, about 21 keys were left over, with no limit. After a 3-second walk the
   agent went on walking for nearly 6 seconds after the key was let go. This is the most likely cause of the reported
   lag.
2. **400 frames a second handed to the window** while the game waited for a key or for the PC's clock. That is six
   times the display's 70. It costs CPU and GPU time and slows the window on a slow renderer, without showing anything
   more.
3. **Frame waits in whole milliseconds.** The time was read in whole milliseconds and the wait slept with the system's
   timer, so a 14.27 ms frame came 11 to 18 ms after the one before. Fine scrolling (the car chase) was less even than
   it needs to be.
4. **Sound up to 33 ms late.** The sound was rendered in whole 1024-frame buffers, so up to 23 ms of sound waited in
   SDL's stream on top of the device's own 10 ms.

All four are fixed in the host layer (section 4). The game's frames, keys and sounds are what they were: the session
proof and every test pass unchanged.

## 1. How it was measured

**The trace.** `COVERT_FRAME_TRACE=<file>` makes the SDL host write one line per frame it hands to SDL. Without the
variable nothing is measured (`FrameTrace.cs`). Each line gives:

* when the frame was shown, the gap since the previous frame, and the gap between the starts of the two frames;
* how the gap was spent: the game's code, the waits asked for and how long they took, and the time to show the
  frame (colours to pixels, texture upload, drawing and `SDL_RenderPresent`);
* the key polls, the keys taken, and each key's time from its event to the next frame shown;
* the time the garbage collector paused the program;
* the sound device's callbacks (how long each took, how long since the one before).

A summary at the end gives percentiles, and the frames a second for each part of the game.

**Scripted input.** `COVERT_INPUT_SCRIPT=<inputs.json>` plays a recorded session's keys in the real window, one entry
per key poll, the same way the headless host plays them (`ScriptedInput.cs`). The time of day is the recording's, so
the game follows the recorded path, at real-time speed. The recordings are those of the session proof
(`build/session/COVERT/<name>/original-*/inputs.json`):

| run | what it plays | sound |
|---|---|---|
| `new-case-chase` | set-up, FINAL's menus and a new case (music), the GAME city screens, a 6013-frame car chase, GAME, quit | none |
| `new-case-chase-adlib` / `-roland` | the same | AdLib (OPL3) / Roland MT-32 (Munt, the Darklands ROMs, read only) |
| `practice-return-speaker` | the menus, wiretap and cryptography practice, the returns | PC speaker |
| `new-case-tap-save`, then `loaded-game-raid` | a new case saved; that save loaded, a raid in Washington | none |

Machine: Windows 11, 16 logical CPUs, SDL 3.4.20 (`direct3d11` renderer), WASAPI at 44100 Hz float, a 441-frame
(10 ms) device buffer. Other programs were running builds and tests at the same time, so single spikes (a present or
wait of 20 to 140 ms) appear in every run. Before/after comparisons interleave the two builds, run for run.

To repeat a run (PowerShell):

```
$env:COVERT_FRAME_TRACE = "$PWD\trace.txt"
$env:COVERT_INPUT_SCRIPT = "build\session\COVERT\new-case-chase-adlib\original-20261115-055314\inputs.json"
dotnet run -c Release --project csharp/src/CovertAction.Host.Sdl -- "<game folder>" --saves <empty folder>
```

The same trace works in real play without `COVERT_INPUT_SCRIPT`: then the key latency is measured from SDL's own
event time.

## 2. What the original does

* **Frames.** The game's one way of waiting is the VGA's vertical retrace: `wait_vsync(n)` (CODE 0000:29a2) waits
  for n retraces of the 70.086 Hz display (25.175 MHz / 800 / 449). The raid's turn is
  `wait_vsync(6 | 4 | 2)` twice (TAC 00e8:0018: 171, 114 or 57 ms a turn for slow, medium and fast). The car chase
  waits a frame per step. The dissolve changes 533 pixels a frame (120 frames, 1.7 s). The library animations wait
  for the PC clock's 18.2 Hz ticks.
* **The screen.** The display reads video memory by itself 70 times a second. Whatever the game draws is visible
  within 14.3 ms, without the game doing anything.
* **Keys.** The BIOS buffer holds 15 keys; a key typed when it is full is lost (with a beep). A held key repeats at
  the keyboard's typematic rate, which the game leaves at the AT keyboard's power-on default: 500 ms, then 10.9 a
  second. The raid tries to set it to 250 ms and 15 a second, but it calls INT 10h instead of INT 16h (TAC 00e8:0018,
  AX=0305h, BX=0008h; SESSION-PROOF.md), so the default stays. At 10.9 a second a held key goes into the raid at about
  the rate it is used.
* **Sound.** The driver runs on the PIT timer's interrupt. A sound the game starts is heard at the next interrupt,
  within a few milliseconds.

## 3. What the rewrite did (before)

Measured on the runs of section 1 (HEAD 34ab5ce), per part of the game:

| | FINAL (menus) | GAME (city) | CHASE | TAC (raid) |
|---|---|---|---|---|
| frames handed to SDL a second (the display: 70) | 380 to 436 | 189 to 238 | 75 to 86 | 79 |
| frame start to frame start, the frames a frame wait showed (sd) | | | 1.0 to 1.7 ms | 1.5 ms |
| ... within 1 ms of 14.27 ms | | | 60 to 73 % | 60 % |

* **Game code** is cheap: 0.1 to 0.3 ms a frame on average. The long stalls are at part starts, where the original
  loaded from disk: 1.1 to 2.4 s at start-up (the first part and the JIT), about 330 ms when GAME starts, about 50 ms
  when the chase starts. A few 25 to 85 ms stalls in the middle of play happened in some runs only.
* **GC**: one collection in a whole session, a pause of 9 to 25 ms. Not a cause.
* **Showing a frame**: 0.2 to 0.8 ms (median), 1.5 to 5 ms (99th percentile). It is cheap; the cost was the count.
* **Waits**: `Thread.Sleep` overshot by 0.5 ms (median) and 1.5 ms (99th percentile), with spikes of 33 to 73 ms
  when the machine was busy. On top of that the deadline was cut to whole milliseconds: a frame due in 14.27 ms
  slept 14, and the time read was whole milliseconds too.
* **Sound**: SDL asks every 10.0 ms (no late callback in any run). Rendering takes 0.15 to 0.9 ms
  (99th percentile), Munt included: the MT-32 is not a cause. But each ask got whole 1024-frame buffers, so up to
  1023 frames (23 ms) waited in SDL's stream: a sound was heard 10 to 33 ms after the game started it.
* **Keys**: Windows repeats at its setting here (KeyboardSpeed 31: about 30 a second, after 500 ms), and the host
  queued every repeat without a limit.

### 3.1 The causes, by what the player sees

| what the player sees | cause | size |
|---|---|---|
| the agent (or a menu's cursor) goes on moving after the key is let go | Windows' 30 repeats a second into an unbounded queue, against 8.75 turns a second (raid, medium speed) | about 21 keys a second held; 3 s held, 5.7 s more walking |
| uneven scrolling and movement | frame waits in whole milliseconds, slept with the system timer | frames 11 to 18 ms apart instead of 14.27 |
| busy CPU/GPU, a slower window on weak hardware | the screen handed to SDL on every turn of the key and clock loops | 380 to 436 frames a second in the menus |
| sound a little behind the picture | whole 1024-frame buffers | up to 23 ms extra |

What did **not** cause lag: the game's code, the garbage collector, texture uploads (0.05 to 0.6 ms), the sound
callbacks (never late), Munt (under 1 ms per 10 ms), and the waits the game asks for (all as the original's).

## 4. The fixes

All in the host and the driver layer between game and host. The game's code and its frame counts are unchanged.

| fix | where | why |
|---|---|---|
| A held key repeats at the PC keyboard's rate: after 500 ms, 10.9 a second. Windows' repeats are ignored. `COVERT_KEY_REPEAT=system` keeps Windows' rate | `SdlHost.Pump` | the original's typematic; the raid takes keys at about that rate |
| The key queue holds 15 keys, like the BIOS buffer; a key typed when it is full is lost | `SdlHost.Type` | bounds a pile-up to what the original allows |
| Frames are due on a fractional clock (`IClock.PreciseMilliseconds`). The wait ends at the deadline: `IClock.WaitUntil`, which the SDL host does with `SDL_DelayPrecise` (sleeps short of the deadline, then spins the rest) | `HostLink.WaitForFrame`, `SdlHost` | 14.27 ms frames, not 14 or 15; no oversleep |
| Other waits use `SDL_DelayNS` (a high-resolution waitable timer on Windows) instead of `Thread.Sleep` | `SdlHost.Sleep` | a millisecond whatever the system's timer resolution (15.6 ms where no program asks for a finer one) |
| The key-wait and clock-tick loops show the screen only when the game has changed it (picture, colours or text page), else once a frame of the display | `HostLink.PresentIfChangedOrDue`, `KeyboardDriver`, `GameTimer.ClockTicks` | a change is shown at once; an unchanged screen at 70 Hz, which keeps the window drawn and the cursor blinking |
| The sound callback renders what SDL asks for, in pieces of up to 1024 frames, with no leftovers | `SdlAudioOutput.Pull` | a sound is heard after the device's 10 ms buffer only |

The default `IClock.PreciseMilliseconds` and `IClock.WaitUntil` are the whole milliseconds and the sleep used before.
The headless host, which the session proof and the tests run on, keeps exactly the time it kept
(`HostPacingTests.TheHeadlessHostKeepsItsWholeMilliseconds`). Its checkpoints read the game's screen, not what was
shown, so showing fewer frames does not change them.

Tests (`tests/CovertAction.Drivers.Tests/HostPacingTests.cs`): frames exactly one display frame apart on a precise
clock; a game busy for more than a frame does not catch up; the headless host's whole milliseconds; a key wait shows
an unchanged screen about 70 times a second and a changed one at the next poll; the clock-tick loop the same.

## 5. After

The same sessions, the two builds run in turn (the "A/B" runs: before = HEAD 34ab5ce with the trace added, after =
this change):

| | before | after |
|---|---|---|
| frames handed to SDL a second, FINAL's menus | 380 to 410 | 76 to 78 |
| ... GAME's city screens | 219 to 232 | 72 to 73 |
| ... the car chase (one a frame, a second one where the chase polls the keyboard) | 84 to 86 | 82 to 86 |
| frames handed to SDL in the whole `new-case-chase` session | 17564 | 8303 |
| a frame wait's overshoot of its deadline: median / 99th percentile / most | 0.48 / 1.48 / 101 ms, plus up to 1 ms cut off the deadline | 0.00 / 0.37 / 1 to 26 ms (one spike while the machine was busy) |
| chase: frame start to frame start, standard deviation | 1.00 to 1.83 ms | 0.58 to 0.79 ms |
| chase: frames within 1 ms of 14.27 ms | 66 to 71 % | 93 to 94 % |
| key (scripted) to the screen showing it: median / 90th percentile | 0.9 / 8.7 ms | 1.1 / 4.9 ms |
| sound: frames waiting in SDL's stream after a callback | 0 to 1023 (up to 23 ms) | 0 |
| sound callback: 99th percentile (AdLib / MT-32) | 0.58 / 0.88 ms | 0.25 / 0.81 ms |

The raid (`loaded-game-raid`) and the PC speaker's practice session (`practice-return-speaker`) gave the same picture.
The raid's frames were within 1 ms of 14.27 ms 60 % of the time before and 75 % after. The wiretap's frames had a
standard deviation of 2.23 ms before and 0.75 ms after.

Showing a frame takes about 0.4 ms longer per present after (median 0.8 ms instead of 0.4 to 0.6 ms). It does not
delay the next frame: the frame's start is on time, and the wait absorbs the difference. The spin at the end of a
frame wait costs at most about a millisecond of one CPU core per frame.

Held keys cannot be scripted (a script feeds the game's polls directly). By arithmetic, a direction key held for 3
seconds in a raid at medium speed (8.75 turns a second):

| | keys typed | keys taken in the 3 s | left when the key is let go | walking after |
|---|---|---|---|---|
| before: Windows' repeat (500 ms, then 30 a second), unbounded queue | 76 | 26 | 50 | 5.7 s |
| after: the PC's (500 ms, then 10.9 a second), 15 at most | 28 | 26 | 2 | 0.2 s |
| the original on a PC | 28 | 26 | 2 | 0.2 s |

## 6. What is left, for the player to check by eye

* **A 60 Hz monitor cannot show 70 frames a second.** Windows composes the window at the monitor's rate, so of each 7
  frames one is never seen, about 10 times a second: a small regular judder in the chase and in scrolling, which no
  timing in the host removes. A monitor at 70 Hz or 120/144 Hz and above shows every frame. The host does not wait
  for the monitor's refresh (vsync), on purpose: the game's pace is the 70 Hz display's.
* **Held keys.** Walk the agent in a raid with the keypad held and let go: it should stop within a turn or two.
  Hold a cursor key in a menu: the cursor should stop when the key is let go. If 10.9 a second feels slow,
  `COVERT_KEY_REPEAT=system` gives Windows' rate (and the old pile-up).
* **Sound** with AdLib and MT-32: it should start with the picture (about 10 ms behind it).
* **A graphics driver forcing vsync** ("always on" in the driver's control panel) makes every present wait for the
  monitor. The frames then come at the monitor's pace, 60 a second on a 60 Hz monitor, and the game runs about 14 %
  slow. Leave vsync to the application.
* **Part starts** take 0.3 s (GAME) to 2 s (the first start, with the JIT). The original loaded from disk there. A
  ReadyToRun build (`dotnet publish -p:PublishReadyToRun=true`) would shorten the JIT part. Not done here.
* In the chase and other parts that poll the keyboard every frame, a frame is handed to SDL twice: once when the poll
  finds the changed screen, once by the frame wait (about 1 ms apart). It is harmless (under 1 ms of GPU time). Leaving
  out the second would change the headless host's frame count, which some tests count.
* A present occasionally takes 20 to 140 ms in every run, before and after. This happened while other programs used
  the machine. It looks like Windows' composition or the GPU driver, not the game. On a quiet machine, a trace (section 1)
  should show whether it remains.
