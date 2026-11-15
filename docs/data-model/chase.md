# CHASE data model

Module CHASE = `CHASE.EXE`, the car-chase minigame. Source: the 71 byte-matched functions of the module. Types: the reconstructed `chase.h` (includes `campaign.h`).
Offsets below are DGROUP offsets. "Verified" means the compiled bytes depend on it (stride, offset, order);
"read" means read from the code, not byte-checked.

## 0. Summary of what the game does

The player tails a suspect ("the quarry") through a 24 x 24 tile city, or (mode 2) drives away from two
pursuers. Three cars exist for the whole run: car 0 = the quarry (modes 0, 1) or the player's car (mode 2);
cars 1 and 2 = the two tail cars the player picked in the car offer screen (modes 0, 1) or the two pursuers
(mode 2). The screen shows a minimap (left, 200 x 200), a 120 x 100 street-view window (bottom right,
at screen 200,100), and a cockpit dashboard (top right). One `chase_tick` runs per frame; the frame
loop waits 8, 4 or 0 vertical retraces (game_speed 0, 1, 2).

Hand-off (state.h block, `SHARED`):
- byte 0 `next_module` selects the mode: 3 (and 0xe) -> mode 0 tail; 5 -> mode 1; 6 -> mode 2 (flee).
  Origins found in GAME: 3 = "Follow car" after "You notice someone leaving the building" (game_00f7_23c9:
  stores `value` = organisation the person left, `result` bit 0 = sex, `person`); 6 = the player is
  "followed" when leaving an organisation with alert > 50 (game_00f7_1e5a; stores `value` = cur_org).
  Where 5 and 0xe come from is not traced.
- on entry CHASE reads: bytes 0, 9 (`value`), 0xa (`result`), the scalars 0x20..0x4e (import_shared_state),
  word 0x48 `alert`, byte 0x44 gfx letter, segments 0x60/0x62/0x64; then six table blocks from +0x400 (section 5).
- on exit it writes: byte 0 = -1 then 0 or 0x11 (0x11 when option bit 0x20), byte 9 `value` = outcome (0..2),
  byte 0xa `result` = alert percent `clamp(25 * alert_ratio, 0, 100)`, byte 0xd `not_hurt` = 1 (set on entry
  and again on exit), byte 0xf `elapsed` = frame / 60, byte 0xe `org_out` (written by check_location_entry
  when a building is entered in a campaign), word 0x4e skill_bits (game speed written back into bits 10-11).
  NOTE for state.h: `value` (9) and `result` (0xa) mean different things on entry and on exit (see Proposals).

Units: world position 32 units per tile, 24 tiles = 768 units; minimap 8 px per tile (a dot is drawn at
x/4, y/4); street view 1 px per unit. Cars move at most one unit per frame (see `speed_pattern`).

## 1. Cars: `cars[3]` at 4290 (20 bytes each, 60 bytes, up to 42cc) - verified stride 20

```
00 x         world x of the car centre (units)
02 y
04 dir       0 up, 1 right, 2 down, 3 left (dir_dx/dir_dy index)
06 flag      player-driven car (cars 1,2 in modes 0/1; car 0 in mode 2): 1 = "auto-follow" lamp (F10 sets it,
             an arrow key clears it); init_cars sets it to 1 for cars 1,2.
             opposing car (car 0 in modes 0/1; cars 1,2 in mode 2): 1 = a tail car sees it this frame
             (its mark is refreshed from its real position next frame); cleared after use every frame.
08 mark_x    opposing car: where it was last seen, -1 = none. The street view and the minimap show the
0a mark_y    opposing car AT ITS MARK, not at its real position, so a car that is not watched appears frozen.
             player-driven car: mark_x/y is only compared with the car position (see aliasing below).
0c turn      pending turn direction taken at the next tile centre, -1 = none (arrow key; also the stored
             heading of an opposing car's mark, used to pick its sprite)
0e alert     tail car: how long the quarry has been watching it (see below); car 0: the quarry's suspicion
10 gear      0..4, index into speed_pattern; dashboard speed = 20 * gear mph
12 attr      CARATTR_*: bits 0-1 speed class (top gear = 2 + class: 40, 60, 80 mph; 3 is never offered),
             bit 2 tracking device, bit 3 "Excellent" handling (may turn around on the spot),
             bits 4-5 conspicuousness 0..3 (Low, Moderate, High, Extreme). Set only for cars 1,2 (the picks);
             car 0 = 0 (init_cars). In mode 2 cars 1,2 keep attr 0 (BSS).
```

Aliasing found (all verified by FILE MATCH after rewriting the uses):
- old `D_42b4` = cars[1].gear and `D_42c8` = cars[2].gear (both set to 2 at frame 0x20 in modes 0/1).
- old `cars_attr[i]` (42a2, 20-byte records) = cars[i].attr.
- old `follow_x`, `follow_y` (42ac/42ae) = cars[1].mark_x, cars[1].mark_y. They hold the "follow point"
  (quarry position + 2 * its heading, refreshed whenever a tail car sees the quarry choose a direction)
  toward which the auto-driving tail cars steer. Because it is cars[1].mark, it is also set to -1 when
  car 1 reaches its own mark in tail modes, and init_cars sets cars[1..2].mark_x = -1. The names
  `_follow_x`/`_follow_y` stay in symbols/names.txt but the C no longer uses them.

Initial state (init_cars, verified): alert_limit = 1000 / (difficulty + 3), then
`10 * alert_limit / (alert_level + 100) * 10`; outcome = 0; car 0 on location start_loc, dir = 0 (up) if
loc_y[dest_loc] < its y else 2 (down), mark = own position, turn = -1, alert 0, attr 0, gear 2;
cars 1,2 stand `8 * i` units behind car 0 (against its heading), same dir, flag 1, mark_x -1, turn -1, alert 0,
gear 0; dest_x/y = loc_x/loc_y[dest_loc]; in mode 2 cur_car = 0 (otherwise `cur_car` starts at 1 from the data
segment).

### Movement
`speed_pattern[5]` (17b0) = {0, 1, 9, 21, 27}: bit k set = the car advances one unit when `frame % 6 == k`
(gear 0..4 move 0, 1, 2, 3, 4 units per 6 frames). A car decides a new direction only when x and y are
both multiples of 32 (tile centre). `can_move(tx, ty, dir)` (see 3.3) gates every move; the player's pending
`turn` is taken at the tile centre if allowed. A reversal (turn = dir + 2) is only executed at once when
the car has CARATTR_HANDLING (sound 0x17), otherwise the request is dropped.

### Opposing-car logic (read from chase_tick)
- `sight_distance(i)` (verified): 0 unless car i lines up with car 0 on a row or column (x equal and
  x % 32 == 0, or y equal and y % 32 == 0), < 6 tiles (0xc0 units) apart (at night > 0x40 units = 2 tiles
  gives 0) and every tile between is open; returns the distance in units. Sets `sight_flag` = 1 unless the
  cars are on the same spot.
- Tail modes: a tail car i that sees the quarry (and has gear > 0) adds to its own `alert`: +1, +1 more
  if distance <= 0x20, +1 more if gear > 2, plus `((attr & 0x30) >> 4) - 1` (conspicuousness Low -1,
  Moderate 0, High +1, Extreme +2). The quarry gets `flag = 1`. When a tail car's alert
  exceeds 2 * alert_limit, the quarry's own alert (cars[0].alert) grows by 1 per frame; above
  alert_limit / 2 it speeds up (gear 3) and above alert_limit it flees at gear 4 (not while arrival_timer
  runs). A tail car that does not see the quarry loses 1 alert per frame (down to 0). The quarry's alert
  never decays.
- `ram_ready` is set (and F1 / 'a' then arrests the quarry: outcome 2, done_flag 1) when a tail car sees the
  quarry, is within Manhattan distance 8 and has the OPPOSITE heading (`(car0.dir + 2) & 3 == car.dir`), i.e. it is
  meeting it head-on. In mode 2 the same test ends the run at once (outcome 1: the pursuers caught you).
- The quarry also copies the gear of a tail car that is in sight if that is faster than its own (not during
  the arrival countdown).
- Arrival: when the quarry reaches (dest_x, dest_y) with no countdown running it stops (gear 0) and
  `arrival_timer = 256 - 64 * difficulty` frames start (difficulty 0..3 -> 256, 192, 128, 64). When it hits 0
  the run ends (done_flag 1). While the countdown runs and a tail car sees the quarry, outcome = 1
  ("pulls over ... quietly park nearby" = success) and watch_count counts the frames seen; > 16 ends the
  run. If nobody saw it arrive, outcome stays 0 ("totally lost track").
- Getting away: after the loop, if car 0.flag == 0 (no tail car sees the quarry this frame), cars[0].alert >
  2 * alert_limit and no countdown: done_flag = 2 ("turns the corner and dashes into a crowded restaurant").
- Quarry steering at a tile centre (mode 0): picks the direction towards (dest_x, dest_y), x or y first at
  random, switching when blocked; with probability alert / (4 * alert_limit) it picks a random direction
  instead; never a U-turn unless 9 retries failed; then it tells the tail cars where it went (follow point).
- Tail car steering (mode 0): pending turn first; else, if flag == 1 (auto-follow), towards the follow
  point (cars[1].mark); with a tracking device (attr bit 4) and a stale follow point (signs of the vectors
  to the follow point and to the quarry disagree) it aims at the quarry with a small offset instead.
- Mode 2: car 0 is the player (pending turn, else forward, else a random open direction), cars 1,2 compute
  forward/sideways offsets (l, b) to the player in the player's heading frame and try to cut it off;
  `weave_toggle` alternates which side they swerve to when exactly in line. A pursuer that sees the player gets
  `flag = 1`, its mark = its position and gear `clamp((frame >> 3) - i, 0, distance < 0x20 ? 2 : 3)` (so they
  speed up over time); one that does not see the player drives at gear 3 (difficulty 0: 2 or 3 by frame bit
  `0x10 << i`; difficulty 3: 3 or 4) and gets `flag = 1` when its Manhattan distance is at most
  `((5 - difficulty) << 5) / 2`.
  They are drawn at their marks (so you see them only while "seen"); the player's car is always drawn at its
  real position (`h = c > 0` in draw_street_view).
- The player's top gear in mode 2 is capped at 2.

### Keys (chase_tick, only after frame 0x20): arrows set `turn` (and clear flag), `=` / `-` change gear
(top gear = (attr & 3) + 2), F10 sets flag 1, space switches cur_car between 1 and 2 (modes 0/1; view_frame
+= 4), F1/'a' arrest (if ram_ready) else like F2/'e' = enter the building within 6 units of car 0
(check_location_entry; ends the run with outcome 0), Alt-Q quit prompt, Alt-S sound, Alt-P pause (sounds
0x25/0x26), S/D/F game speed, ESC ends in the tail modes. Sound effects used: 0x13/0x14 option off/on,
0x16 start/first gear, 0x17 tyres, 0x18/0x19 shift up/down, 0x1a/0x1b engine of car 1/2 (parameter =
`sound_param` = gear), 0x25/0x26 pause, 0x27 car offer screen, 0 = stop, 1 after the pick.

## 2. Ports (drawing surfaces)

Each of five 18-byte records is followed by a pointer variable that points back to it; the far drawing
calls receive the pointer first. Verified from the initial data: pointer values 0x1600, 0x17dc, 0x17f0,
0x1804, 0x19f6. Layout (`struct port`, chase.h; words):

```
00 page       0 screen, 1 backup surface, 2 street.pic sheet, 3 chase.pic sheet (the page the draw calls use)
02 x0  04 y0  06 x1  08 y1     clip window, inclusive (319, 199 for a full surface)
0a text_flag  1 normally, draw_text_flag0 sets 0 around a draw call; generate_map clears it for map labels
0c text_color
0e bg_color   fill / background colour (menus set 5)
10 font       1 or 2 (main sets 1 for loading, 2 for the game)
```

| record | page | window | variable | uses |
|---|---|---|---|---|
| 1600 | 1 | full | `screen_backup` 1612 | off-screen render target of the street view; backup of the screen under menus |
| 17dc | 3 | full | `tile_sheet` 17ee | **chase.pic**: tile textures, car sprites, gauge needle images, dashboard parts |
| 17f0 | 2 | full | `view_sheet` 1802 | **street.pic**: 8 first-person cockpit frames 116 x 44 (2 columns x 4 rows of 125 x 47 cells) |
| 1804 | 0 | (201,1)-(318,75) | `gauge_sheet` 1816 | screen window of the cockpit; the needle is drawn through it |
| 19f6 | 0 | full | `screen_port` 1a08 | the screen |

CORRECTION to symbols/names.txt: the comment of `tile_sheet` says "from street.pic"; it is chase.pic
(main loads chase.pic into page 3 and street.pic into page 2; `view_sheet` is street.pic).
`draw_street_view` sets `screen_port->page = 1` while it draws its frame lines, then back to 0.

- `gauge_rects[16]` 170c (struct rect4 {x1,y1,x2,y2}, 8 bytes, verified): source rectangles in chase.pic for the 16
  needle images; `gauge_images[16]` 5c12 holds the image handles that `0000:011a` returns.
  Needle choice: index = clamp(1.5 * distance / 4 - 2, 0, 15).
- `car_level_colors[4]` 1862 (struct color_pair, verified stride 4): per conspicuousness level, the colours
  replacing 0xb and 3 in the car picture of the offer screen (not CGA).
- `palette_chase` 16fa, `palette_plain` 178e, `palette_menu` 191a, `palette_menu_alt` 192c: 17-byte EGA palette
  tables (16 registers + overscan) for `0000:01c2`. palette_chase has register 0 = 0x0e, the others 0
  (palette_menu_alt also register 12 = 6). `cga_pattern_*` 1634/1644/16e4: tables for `0000:01c9` (probably
  colour -> CGA dither bytes: 16e4 = 16 bytes {0,0x11,0x22,0x33...}, 1634 = 16 bytes, 1644 = 160 bytes).
  Roles of these two tables are not verified (the driver code is outside CHASE's own segment).
- `chase_pic_buffer` 178c: result of driver call `0000:010c(3)`, handed back with `0000:01b4(3, .)`; page 3 is
  released with `0000:0144(3)` at the end. Probably the memory block of the chase.pic page.

## 3. The map

### 3.1 `map_cells` 4788: 24 x 24 bytes, index `x * 24 + y` (verified: can_move, generate_map, draw_street_view)
Low nibble = four "side" bits, high nibble = ground texture 0..15 (cell texture number selects a 32 x 32 tile in
chase.pic: 10 tiles per row, row step 0x20 at y 0x11).
Initial value (generate_map): `(random(16) << 4) + 0xf`. The 192 "carve" attempts (`attempt & 3` = direction n,
for n = 2, 3 only when both cells still have low nibble 0xf) clear bit n in the cell and bit (n + 2) & 3 in its
neighbour and set the cell texture to 8 or 9 (`(x + y) & 1`). The minimap draws the street grid in colour 5 over a
grey field first, then paints the segment over in the field colour, so a cleared bit = a CLOSED street edge
(kerb) and a set bit = open. The street view draws a colour-7 kerb strip on every side whose bit is clear.
Sides: 0 up, 1 right, 2 down, 3 left (tile_edge_x/y strips: top, right, bottom, left).

### 3.2 Map constants
`dir_dx` 17a0 = {0, 1, 0, -1}, `dir_dy` 17a8 = {-1, 0, 1, 0}. `road_x_ends` 17ba / `road_y_ends` 17ca
(struct line_ends {from,to}, 4 entries each): offsets in minimap pixels of the segment drawn over the
street grid for direction n: x: {2,7} {8,8} {2,7} {0,0}; y: {0,0} {2,7} {8,8} {2,7}.
`tile_edge_x` 189c / `tile_edge_y` 18ac (struct span {offset, size}, 4 entries): kerb strip n of a 32 x 32 tile:
x {2,28} {30,2} {2,28} {0,2}; y {0,2} {2,28} {30,2} {2,28}.

### 3.3 UNRESOLVED: can_move vs the generator (read carefully before porting)
`can_move(tx, ty, dir)` (byte-verified) returns, for a cell not on the outer ring:
up `cell(tx, ty-1) & 8`, right `cell(tx, ty) & 1`, down `cell(tx, ty) & 8`, left `cell(tx-1, ty) & 1`.
(The old C used `map_prev_row[24][24]` at 4770 = map_cells - 24; that was only an index trick, 4770 is
not a real array; it lies inside the last city record.) So only bits 0 and 3 are ever tested, bit 0 for
horizontal moves (edge between x and x+1 = bit 0 of the left cell) and bit 3 for vertical moves (edge between y
and y+1 = bit 3 of the upper cell). The generator and the street view, however, number the sides 0 up, 1 right,
2 down, 3 left, so the edge x | x+1 is bit 1 of the left cell / bit 3 of the right cell, and y | y+1 is bit 2 of
the upper cell / bit 0 of the lower cell. The two do not agree: bit 0 of a cell is cleared by "up" carves but used
by can_move for moving right. Whether this is an original quirk or a misreading cannot be decided statically.
A C# port must reproduce can_move exactly as coded; check against a recorded run before trusting any
"wall = kerb" interpretation. Outer ring: tx == 0 allows only dir 1, ty == 0 only dir 2, tx == 23 only
dir 3, ty == 23 only dir 0 (cars cannot drive along the border).

### 3.4 Locations: `loc_x[16]` 6802, `loc_y[16]` 6822 (words, 32 bytes each)
generate_map picks location 0..15: with difficulty < 2, x = 192 + 32 * rand(12), y = 208 + 32 * rand(12);
otherwise x = 96 + 32 * rand(18), y = 112 + 32 * rand(18). So x is a tile corner (multiple of 32) and y is
half-way along a tile (y % 32 == 16). A candidate must satisfy `can_move(x / 32, y / 32, 2)` and Manhattan
distance >= `(difficulty << 5) + 0x40` to locations 1..lh-1 (location 0 is not compared); retries until
found. Location lh (1..15) is drawn on the minimap (dot at world (x + 8, y) for odd lh, (x - 8, y) for even lh, label at
pixel (x/4 + 4, y/4 - 3)) only if
`cities[city].orgs_seen & (1 << lh)` (known organisation) in a campaign; the label is
`org_records[lh].short_name` ("Loc<n>" if campaign_mode were 0; main always sets it to 1).
`start_loc` 5c02, `dest_loc` 428c: random 1..15 and different, then (campaign) start = `SHARED->value`
(the organisation the player came from), unless option bit 0x20 (end of case) where start = random(16) != org;
dest = `org` (shared word 0x22). `dest_x`/`dest_y` 69ce/69d0 = loc_x/loc_y[dest_loc].
`check_location_entry(x, y)`: location b = 1..15 with a set `orgs_seen` bit and Manhattan distance <= 6;
original quirk: when bit b is clear the distance of the previous iteration is reused (uninitialised for b = 1).
On success `org` = b and `SHARED->org_out` = b (campaign only).

### 3.5 Camera / street view
`cam_x`, `cam_y` 23ba/23bc: top-left of the 120 x 100 window in units, moves at most 4 units per step toward
`clamp(car.x - 60, 0, 0x288)`, `clamp(car.y - 50, 0, 0x29c)` (draw_street_view loops until it arrives;
cam_x == 0 means "not initialised", the first call snaps). `cam_tile_x/y` 23be/23c0: tile origin of the last
5 x 5 tile redraw into `screen_backup` (left at 0 initially, then the redraw happens when the camera tile
changes). Sprites: car sprite index = dir + 4 (dimmed) or + 8 (current car, flashes when `!(frame & 7)`)
or + 0 when its alert > alert_limit, + 0xc / + 0x18 for a blinker (pending turn right/left, frame bit 4);
sprites are 6 x 4 (horizontal) or 4 x 6 in chase.pic at x = sprite * 8, y rows 1/2/9/10 (colour swapped in
mode 2). A car with alert > 2 * alert_limit blinks (skipped when `frame & 2`).

## 4. Dashboard variables (draw_dashboard)
Static scratch (were file-scope variables, now named): `shown_view_frame` 18be and `shown_needle_y` 18c0 (both
initially -1 = nothing drawn), `gauge_depth` 23c2, `gauge_dist` 23c4, `needle_x` 23c6, `needle_y` 23c8,
`quarry_dx` 23ca, `quarry_dy` 23cc, `dash_scratch` 23ce (needle image index, later alert bar length).
`view_frame` 50c8: 0..7, +1 per unit moved by cur_car, + 4 on car switch. `speed_prefix` 18bc: empty string,
the speed text is the number only (the unit is part of the picture). `clock_text` 18c2 = "0:00:00"
(bytes 0, 2, 3, 5, 6 are written by format_game_time: hour digit, minute tens/units, second tens/units; the
old placeholders 18c4, 18c5, 18c7, 18c8 are `clock_text[2], [3], [5], [6]`). The clock shows
`(frame / 60 + clock) % 1440` minutes and `frame % 60` as the seconds field. Screen positions: cockpit
picture (0xc9,1), hood strip (0xca,0x2d), speed text (0xe6,0x4b), clock (0x122,0x4b), alert bar (0x120,0x54)
30 x 2 (0..10 steps of 3 px = 5 * alert / alert_limit), auto-follow lamp (0x113,0x54), tracking blip box
(0x111,0x4a) 14 x 10.

## 5. Campaign data imported by `import_shared_state` (04a4)

Scalars (shared word -> global): 0x20 city (6844), 0x22 org (69d2), 0x24 option_flags (455e), 0x26 case_no (5c06),
0x28 org_2 (5c0a), 0x2a/0x2c/0x2e saved_2a (5c0e) / saved_2c (5bee) / saved_2e (5bf0), 0x30 clock (428e), 0x32
n_persons (4786), 0x34 n_links (5be6), 0x36 plot_step (5be8), 0x38 saved_38 (684c), 0x3a difficulty (4288), 0x3c
saved_3c (5be4), 0x3e n_cast (5c66), 0x40 n_events (5bfe), 0x42 sel_person (684a), 0x44 gfx_word (5bea), 0x46
n_msgs (5be0), 0x4a city_mask (5c64), 0x4c mask_4c (6848), 0x4e skill_bits (647e). Really used by CHASE:
city, org, option_flags, clock, difficulty, skill_bits, n_cast and n_events (block sizes). All the others are
only stored. Word 0x48 `alert` is read directly into `alert_level` (3e80) and byte 0x44 into `gfx_letter` (67fe).

Tables copied from the shared block at +0x400 (`shared_cursor` 23b6; 0 = read from a file instead via
`shared_fd` 23b8) in this order (same order and sizes as campaign.h):

| block | DGROUP | bytes | CHASE use |
|---|---|---|---|
| scalar block `scalar_block` | 42ce | 0x28e | none |
| cast `cast_records` | 5c6a | 0x30 * n_cast (room for 32) | none |
| cast progress `cast_progress` | 673e | 6 * n_cast (room for 32) | none |
| events `event_records` | 50e0 | 0x2c * n_events (room for 64) | none |
| cities `cities` | 4562 | 0x220 (16 x 34) | only `.orgs_seen` (+0x1c, old `city_loc_mask` 457e) |
| organisations `org_records` | 648a | 0x240 (16 x 36) | only `.short_name` (+0) for map labels |

`shared_src` 23b2/23b4 is the far source pointer built by `read_shared_or_file` (shared block + cursor).
Order check: the cast table at 5c6a starts 2 bytes after `menu_marked_items` (5c68); cast progress at 673e
starts 2 bytes after `menu_right` (673c). Both are correct: those two scalars are separate variables.

## 6. Car offer screen (`chase_dashboard`, file 0842; the name in names.txt is historical)
Shown for modes 0 and 1. Deals 4 different attribute words `c = random(64)` with: speed class `c & 3` != 3 and
quality `(c & 3) + 2 * tracker + handling - conspicuousness == 3 - difficulty` (higher difficulty = worse cars).
Per offer it draws a row of `cars.pic` (4 rows of 50 px; row 2 default, row 3 if conspicuousness 0, row 1 if
handling && class != 0, row 0 if class == 2), "(Tracking)", "<40/60/80> mph", "Excellent"/"Fair",
"Low"/"Moderate"/"High"/"Extreme"; the player picks two different cars (Up/Down, Return/space); the pick `i`
(1, 2) stores `cars[i].attr = l[b]` and prints "Car #i". CGA uses outline boxes instead of recolouring.

## 7. UI / system library (same code as CODE/BUG/TAC; strings and state)
| name | offset | size | meaning |
|---|---|---|---|
| default_disk | 16f8 | int, init 2 | disk to ask for after a failed probe |
| disk_file_names | 19ea | 5 near pointers | indexed by disk number 1..4: "final.exe", "game.exe", "tac.exe", "hq.pan"; entry 0 unused |
| disk_letter | 19f4 | "A" + NUL | patched with `disk + 0x40` |
| drive_number | 1914 | int, init -1 | DOS drive from `_dos_getdrive`, -1 = unknown |
| animation_restart_mark | 1912 | int, init -1 | set to -1 when an animation is restarted; never read |
| save_width | 1916 | int, init 320 | width of the strip saved by `save_screen_area` |
| save_extra_area | 1952 | int | 1 after start_animation on a "bld..." file: also saves 160 x 30 |
| briefing_pan_name / bld_prefix / animation_file | 1940 / 194d / 1954 | strings | "briefing.pan" (needs no disk 4), "bld", "animation.pan" |
| animation_name 23e0, animation_x 23e8, animation_y 23ea, animation_aux_1 23e6, animation_aux_2 23ec | | words | state of the .pan player; aux words are set to -1 and never read |
| animation_active | 5bf2 | int | 1 while an animation runs behind a menu |
| pan_resume_id 50ca, pan_resume_a 647a, pan_resume_b 6488 | | words | arguments of the (empty) animation hook sub_04a3 in quit_prompt |
| menu_* | 23fe..2444, 5be2..5c0c, 5c68, 647c, 66d0..6800 | see names | menu box state: `menu_line_start[32]` 23fe, text/item/selected colour 243e/2440/2444, `menu_line_count` 2442, `menu_first_item` 5be2, `menu_item_count` 5bf8, `menu_first_draw` 5c04, `menu_hilite_color` 5c08, `menu_text_width` 5c0c, `menu_marked_items` 5c68 (bit n = item n drawn with '^'), `menu_message_only` 5bf4, `menu_pause_mode` 647c, `menu_hotkeys[32]` 66d0, `menu_start_item` 66f0, `menu_right` 673c, `menu_bottom` 6800 |
| menu_close_box | 4782 | int | 1: small "OK" box at the bottom right of the menu; forces result -1 |
| str_ok | 193d | "OK" | the close box label |
| str_quit_confirm, str_quit_confirm2 | 18e3, 1872 | two copies of "Are you sure\nyou want to Quit?\n No\n Yes\n" | |
| str_one_moment | 18ce | "One moment please..." | |
| joystick_dir_table | 196a (centre) | 9 ints at 1962..1972, index 3 * sx + sy (sx, sy in -1..1) | compass code 1 up, 2 up-right, 3 right, 4 down-right, 5 down, 6 down-left, 7 left, 8 up-left, -1 centre |
| joystick_raw | 3e82 | word | low byte x axis, high byte y axis (old placeholder 3e83) |
| mouse_regs | 50ce | 14 bytes (7 words: AX BX CX DX SI DI flags) | int 33h block; old 50d0/50d2/50d4 = mouse_regs[1..3] (BX, CX, DX) |
| mouse_buttons/x/y/count | 6480/6482/6484/6486 | words | results of int 33h functions 3, 5, 6 |
| bios_regs | 5c54 | 14 bytes | int 10h block; AL = [0], AH = [1] (old 5c55) |
| archive_handle | 1b34 | int, init -1 | data archive handle |
| pic_handle 2456, pic_buf_pos 5bec, pic_read_hook 6276 (far), pic_read_buf 627a (512 bytes), pic_buf_end 1b32 (init 0x647a = end of the buffer), pic_width 3c5c, pic_height 3c5e, pic_surface 3c60, pic_mem_pos 2458, pic_mem_buf 245a, pic_line_buf 684e | | | picture decoder state (same as BUG's) |
| mode_keep_word | 1f53 | unaligned word | saved and restored around the int 10h mode change |
| message_buf | 3e86 | 1024 | text scratch, one use at a time |
| itoa_buf 626c, itoa_buf2 23d0 | | 10 / 16 | itoa targets |
| random_seed | 455c | int | time(0) & 0x7fff |
| map_seed | 5c00 | unsigned | never written: always 0, so the map seed is `0x7b * city` |
| mark_follow_all | 626a | int | never written: always 0; chase_tick would copy position and heading into the mark of every opposing car |
| campaign_mode | 5bfc | int | main sets it to 1 before anything else reads it: it is always 1 in this build |

## 8. Other chase state, short
`chase_mode` 16f4, `game_speed` 16f6 (from skill_bits >> 10 & 3 then changed with S/D/F), `difficulty` 4288
(`clamp(level - (skill_bits >> 2 & 3) + 1, 0, 3)`), `night` 4286 (clock hour < 8 or >= 20), `is_cga` 4560,
`alert_level` 3e80 (shared 0x48), `alert_limit` 6846, `alert_ratio` 6842 (= alert / limit of the last
opposing car processed; result byte = clamp(25 * ratio, 0, 100)), `frame` 50de, `outcome` 50dc, `done_flag` 66ce
(1 normal, 2 escaped, -1 quit), `cur_car` 17da, `ram_ready` 50cc, `arrival_timer` 5c62, `watch_count` 4784,
`weave_toggle` 42cc, `sound_param` 5bfa. Unreferenced gaps seen: 428a (2 bytes after difficulty), the range
4562+0x220 .. 4788 contains 4770 which is only an indexing artefact (3.3).

## 9. Unknown / not verified
- 3.3: the bit-numbering disagreement between can_move and the generator/street view.
- Roles of the `cga_pattern_*` and `chase_pic_buffer` driver arguments; exact meaning of the first word of the
  port record in the driver (`page`) beyond "which surface".
- Where hand-off selectors 5 and 0xe come from, and what mode 1 changes (the C treats it exactly like mode 0, but it
  prints no result text for outcomes 0 and 1: message_buf keeps whatever it had).
- `mode_keep_word` (1f53), `animation_aux_*`, `animation_restart_mark`, `pan_resume_*` are only written / passed on.
- 42ce scalar block, cast, cast progress and events are copied but never read by CHASE.
- 428a (2 bytes after `difficulty`) is not referenced by any file.

## 10. Proposals for shared headers
campaign.h (GAME helper):
- `struct org.short_name` is read by CHASE (generate_map prints it as the minimap label of location n), so it
  is displayed, just not by GAME. Please change the comment "not shown by GAME" to "shown as the location label by CHASE".
- `struct city.orgs_seen` is used by CHASE as "location n is known" (bit n, `1 << lh`), consistent with the existing comment.
- CHASE needs `struct city` stride 34 and `struct org` stride 36 and the field offsets above; chase.h depends on the
  member names `orgs_seen` and `short_name` (a rename would break its compile).
state.h (structure helper):
- byte 9 `value`: on entry to CHASE it is the start organisation (GAME 23c9 / 1e5a store cur_org), on exit CHASE
  stores the outcome (0 lost/entered, 1 spotted/caught, 2 arrest). byte 0xa `result`: on entry bit 0 = sex of the
  quarry; on exit CHASE stores the alert percent 0..100 (clamp(25 * alert / limit)).
- byte 0xd `not_hurt` is set to 1 by CHASE both on entry and on exit.
- selector 3 = CHASE mode 0 ("Follow car"), 6 = CHASE mode 2 ("followed"), 0xe also mode 0, 5 = mode 1.
- word 0x22 `org` is the destination location (dest_loc) in CHASE; word 0x24 bit 0x20 changes the start location.

## 11. Changes made to the C (all FILE MATCH)
chase_00df_001c, 0dc6, 0708, 2012, 2496, 1ceb, 1ede, 2c51, 3190, 04a4, 06ae, 0842, 29d8, 2400, 2bd0, 36eb, 321c,
3c60, 3b4d, 3a9c, 3c0e, 3f08, 41a6, 41c7, 4186, 4262, 403c, 40ce, 4106, 413e, 40a8, 0482, 3e6a, 4488, 452c, 45aa
(plus the renames of apply_names in 12 more files). New: `struct car`/`struct port`/... in chase.h; SHARED-> member
accesses in main, import_shared_state, check_location_entry, generate_map; `cars[i].attr`, `cars[1].mark_x` etc. for
the aliased globals; `clock_text[k]`, `mouse_regs[k]`, `bios_regs[1]`; string and table names instead of literal
addresses in the car offer screen; `cities[city].orgs_seen` / `org_records[lh].short_name` for the campaign tables.
