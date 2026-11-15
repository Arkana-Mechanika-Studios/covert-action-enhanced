# TAC data model

TAC.EXE is the real-time raid on one building. Everything is in DGROUP; offsets below are DGROUP offsets
(`D_tac_xxxx`). Types are in the reconstructed `tac.h`; names in `symbols/names.txt`. "ev:" = the reconstructed function's file stem and the line.

A raid has three layers of data:

1. **the campaign tables TAC loads from `env.sve`** (persons, cities, organisations, cast, events, links...,
   see "Campaign copy"), read by `read_env_state` (cbb9) and written back by `write_env_state` (c7d2);
2. **the generated building** (map arrays, rooms, doors), rebuilt from a seed derived from city and
   organisation, so the same building always looks the same (ev: 0018 165);
3. **the live raid state** (actors, projectiles, alarm, player, equipment).

Coordinates: the map is **40 x 32 cells** (`MAP_W` x `MAP_H`), 16 x 16 pixels each, every per-cell array is
indexed `[x][y]` with row stride 32 (ev: 45da, 7155 `random_below(40)`, `random_below(32)`). Pixel position
of a cell centre = `cell*16 + 8`. Facings are 1..8, **1 = north, going clockwise** (ev: dir_dx/dir_dy:
1 = (0,-1), 3 = (1,0), 5 = (0,1), 7 = (-1,0)). Sides (walls, doors) are 0..3 = N, E, S, W (ev: nbr4_dx/dy,
534f).

## 1. Building: per-cell arrays (all 1280 bytes unless noted)

| name | DGROUP | type | meaning |
|---|---|---|---|
| `cell_map` | 91ee | char[40][32] | tile in the cell. Low 6 bits = `tile_types` index, `0x40` CELL_USED (already photographed / examined; also "password letter given"), `0x80` CELL_OPEN (door, drawer, safe open). `0xff` = empty floor. Written by the generator (534f walls, 4931 doors, 4f8d furniture) and by the player (2d10 toggles 0x80, 3081 sets 0x40, 070e opens the entry door). Read by movement (0f4c), drawing (5674, 5962, 540c), guards (5ca6, 6ec9), line of sight (7615), shots (26b0). Row 39 follows at 96ce, which is `cell_map[39]` (44e4 border pass). |
| `room_at_cell` | b40a | char[40][32] | room number of the cell, 0 = outside every room. Written by label_rooms (4838), cleared by remove_room (4ead). `room_at_cell[x-1][y]` and `[x][y-1]` were the placeholders b3ea / b409. |
| `wall_flags` | cf8a | char[40][32] | generation only: `WALL_N 1, E 2, S 4, W 8` around the cell. Row 39 is d46a. Cleared again for removed rooms. |
| `path_dist` | 438e | char[9][40][32] | one breadth-first distance map per actor slot (0x500 bytes each, 11520 total): cell value = steps+1 to the guard's target cell, 0 = not reached. Built by build_path_map (6ec9) in one room only; the guard walks to the neighbour with value-1 (5ca6). |
| `bfs_queue_x/y` | 709a / 719a | unsigned char[256] | ring queue of the flood fill (6ec9). |
| `wall_tile_table` | 1ff0 | char[16] | wall-bit mask -> wall tile number: `[0,0,1,4,2,0,5,0,3,7,0,0,6,0,0,0]` (0..3 straight wall by side, 4..7 corners NE, ES, SW, NW). Used by set_wall_edge (534f). |
| `nbr4_dx/dy` | 06ae / 06b8 | int[5] | neighbour offsets by side N,E,S,W (`0,1,0,-1` / `-1,0,1,0`), entry 4 repeats entry 0. The placeholders `D_06b0` / `D_06ba` were `&nbr4_dx[1]` / `&nbr4_dy[1]` (the perpendicular direction along a wall, 46cb, 4931) and are now written `nbr4_dx[h + 1]`. |
| `dir_dx/dir_dy` | 068a / 069c | int[9] | unit step per facing 1..8 (index 0 = none). Used for the **slow step** (player with the `5` toggle on), shot flash offset, hit animation. |
| `walk_dx/walk_dy` | 0666 / 0678 | int[9] | normal step in pixels per frame per facing: x `0,0,2,3,2,0,-2,-3,-2`, y `0,-3,-2,0,2,3,2,0,-2` (3 pixels per frame straight up/down/left/right, 2 + 2 on the diagonals). Used by player (0f4c), guards (5ca6 314), grenades (26b0), lunge scan, 'front cell' (2fc9). |
| `dir_from_signs` | 2028 | int[3][3] | `[sign(dx)][sign(dy)]` -> facing (0 for both zero), index = sign+1 via `_sign`. |

## 2. Tile types: `tile_types[62]` at 018e, 20 bytes each (`struct tile_type`)

`char name[12]; int sx, sy; int flags; int rooms;` Offsets: name 00, `sx` 0c (pixel x of the 16x16 picture in the tile sheet),
`sy` 0e (the **open** variant is 16 pixels lower), `flags` 10, `rooms` 12. The old placeholders 0190 (flag byte), 019a (sx),
019e (`objtype_flags`), 063e (name of 0x3c), 064a (sx of 0x3c), 065e (sx of 0x3d) are all fields of this table.

* Numbers 0..7 "Wall" (flags 0), 8..11 "Door" (flags 0x12: openable, double-cell door), 12..59 furniture in groups of 4
  orientations (12 = Chair, 16 Terminal, 17 Typewriter, 21 Plant, 24..27 Files, 28 Floor Safe, 29/31 Wall Safe, 30 Picture,
  32/33 Table, 34..37 Desk, 38/39 Couch, 40/41 Toilet, 42/43 Sink, 44/45 Bathtub, 46/47 Bed, 48/49 Range, 50/51 Refrigerator,
  52 Stereo, 53/55 Television, 54 Lamp, 56/58 Computer, 57/59 Drawers), **0x3c MacGuffin** (the plot object; its `name`
  is overwritten with the plot object's name and `sx` shifted by `name[16] << 4`, 4f8d), **0x3d "Local Agent"** (the person to
  arrest; `sx += 0x10` for a woman, 4f8d).
* flags: `TF_SOLID 1` (blocks walking and guard paths, partial cover for sight), `TF_OPENABLE 2`, `TF_BUGGABLE 4`, `TF_PHOTO 8`,
  `TF_DOOR 0x10` (formerly TF_DOUBLE_DOOR: set only on the four Door tiles; opening a door also toggles its
  partner cell in the other room, 5962), `TF_TALL 0x20` (Files, Floor Safe, Refrigerator and Computer: blocks
  sight completely, stops a lunge), `TF_WIDE 0x40` (two cells wide), `TF_AT_WALL 0x100` (stands against a wall), `TF_TERMINAL 0x200`
  (password letter, F4). Bit 0x80 is set on the two Table tiles (0x81) and read by no code.
* `rooms` = bit n set if the piece may be placed in a room of type n (furnish_room, 4f8d).

## 3. Room types: `room_types[10]` at 00b2, 22 bytes (`struct room_type`)

`char name[16]; int value; int size_mask; int building_mask;` Office, File Room, Computer Room, Cafeteria, Lounge, Cipher Room,
Bed Room, Bathroom, Kitchen, Storage Room. `value` (4,6,8,2,2,12,6,12,4,4) is the score added per photo (`photo_value`) and per
bug (`bug_value`) and also how long guards loiter (5ca6 245). `size_mask`: 1 small, 2 above 40 cells, 4 above 72 cells (4f8d picks
a random type whose mask contains the size class and whose `building_mask` contains `building_kind_mask`). `building_kind_mask` (1bca) is the constant 1 and
`building_mask` is 7 for eight types but 0 for Bed Room and Kitchen, so those two are never generated (original-bugs.md CA-8). Placeholders 00c2/00c4/00c6
and `room_type_value` are its fields.

## 4. Rooms: `rooms[0x18]` at a05e, 14 bytes (`struct room`), index 1..`room_count`, 0 = outside

| off | field | meaning (ev) |
|---|---|---|
| 00 | `x` | first cell column (4838 label_rooms) |
| 01 | `y` | first cell row |
| 02 | `w` | width in cells (rooms are at most 10 wide/high, else the layout is retried, 4838) |
| 03 | `h` | height in cells |
| 04 | `linked` (long) | bit n = a door joins this room with room n (4931) |
| 08 | `flags` | `ROOM_REMOVED 1` (dropped by 4ead), `ROOM_VISITED 2` (the player was inside, or was in a previous raid on this building; drawn on the minimap; saved to `links[].u_08`), `ROOM_BUGGED 4` (guards in it are shown on the radar), `ROOM_BODIES 8` (a guard lies there: other guards raise the alarm), `ROOM_GAS 0x10` (gas cloud; cleared randomly, gas harms the player without gas mask) |
| 09 | `u_09` | always 0; flags are sometimes accessed as a word (`ROOM_FLAGS_W`) |
| 0a | `type` | index into `room_types[]` (4f8d) |
| 0c | `alarm_dist` | door steps from `alarm_room`, -1 unreachable, 999 for room 0 (raise_alert 6cb1); guards run towards smaller values |

Companions: `room_count` (b3fa), `room_door_count[24]` (doors per room, 4354), `room_last_visit[24]` (a1ae: frame_tick+1 of the last guard visit, patrol cost),
`exit_dist[24]` (b91a: the same distance map measured from `guard_home_room`, copied at frame 0), `target_room` (b912: the biggest inner room;
holds the MacGuffin and the Local Agent), `guard_home_room` (96ee), `player_room` (bdde), `alarm_room` (c86e).
`room_left/right/top/bottom` (b910, 9b6a, b916, 9b74) are the pixel bounds of the room currently shown; `room_origin_x/y` (c5fa, c602) its screen offset (5674).

## 5. Doors: `doors[0x60]` at 96f8, 5 bytes (`struct door`), `door_count` (cf46)

`room_a, room_b, x, y, side`: `room_a == 0` means the building exterior. (x,y) is the cell on `room_a`'s side; side is that cell's side (0..3).
For the other side use `nbr4_dx/dy[side]`. Written by connect_rooms (4931: the walls bits are cleared, door tiles `8+side` are put in
`cell_map` on both sides), room numbers set to 0 by remove_room, read by choose_door (070e: entrances = records with one side 0),
spawn_guard (7155: guards of kind 2 arrive through them), patrol routing (6b78), raise_alert (6cb1), find_door_cell (6e34).
The placeholder 96f9 is `doors[i].room_b`.

## 6. Actors: `actors[9]` at c870, 28 bytes (`struct actor`); slot 0 = player, 1..8 guards

| off | field | meaning |
|---|---|---|
| 00 | `fl` (byte) + `fl_hi` | `ACT_LEAVING 1` (walking to the exit door of its room), `ACT_HIT 2` (hit this frame), `ACT_SNEAK 4` (player: the `5` toggle is on; guard: running/alert pose), `ACT_ALERT 8` (alert marker over the guard), `ACT_ACTIVE 0x80`. The player code reads the pair as one word (`ACT_FLW`). |
| 02,04 | `x`,`y` | pixel position |
| 06,08 | `tx`,`ty` | target cell, `tx = -1` none |
| 0a | `u` | animation frame 0..7 (sprite = `0x14*dir + u - 0x14`, +8 for guards, +9 sneaking player) |
| 0c | `dir` | facing 1..8 |
| 0e | `state` | player: 0 stand, 1 walk, 2 arrest prompt, 3 (set nowhere found), 4 lunge |
| 10 | `k` | firing / aiming timer (frames 4/5 aiming pose); player: `fire_timer` |
| 12 | `id` | room the actor stands in |
| 14 | `wait` | pause counter (also 9 after the guard decided to shoot) |
| 16 | `chase` | frames the actor has had the player in view; a guard shoots when it exceeds `(5-level)*7-6` |
| 18 | `seen` | frames in a row the player is in sight (also hit chance, 225d) |
| 1a | `down` | knocked-out timer; `< 0x18` fully active, `> 0x20` lying; `>= 0x3e7` out for good / dead (`0x270f` written by mark_guard_dead); for the player `> 0x40` lies down, `> 0x100` ends the raid |

Slot use: player = `player_x/y/facing/state/room` (bb72, bb7c, 1c42, b90e, bdde) copied into slot 0 each frame (0f4c 530); guards spawned by
spawn_guard (7155), advanced by update_guard (5ca6), killed/knocked by 225d/26b0/0f4c, removed (`fl` cleared) when they step on a cell of room 0.
Per-actor screen caches: `actor_radar_x/y[9]` (42c2, 42d4: radar dot, 3/16 scale at (0xb4,0x64)) and `actor_sprite_x/y[9]` (42e6, 42f8) in 1b32.

## 7. Projectiles: `projectiles[16]` at 9d78, 10 bytes (`struct projectile`)

`x, y, dir, t, kind`. `kind`: grenade type 0 **fragmentation** (kills guards / hurts the player), 1 **gas** (sets `ROOM_GAS`), 2 **stun** (adds knock-out time);
`+0x80` when thrown by a guard; -1 free. `t`: `> 0` flight frames left (10*power), `-0x3f..-1` burst animation, `-9999` remote-controlled trap,
`-9998` booby trap (shown as an X on the minimap, picked up by a guard standing on it, 5ca6 271). F5/F6/F7 throw with power 1/2/3,
F9 places a trap (2553), F8 arms remote traps. Placeholders 9d7e (`t`) and 9d80 (`kind`) are fields.

## 8. Equipment and player state

* `equipment_bits` (9b72): bit n selects `item_names[n]`: 0 Pistol (kept as "not Uzi"), 1 Uzi (+1 hit chance), 2 Camera, 3 Bugs (`bugs_left`=10),
  4 Safecracking Tools, 5 Gas Mask, 6 Kevlar Vest (hit limit 3 instead of 1), 7 Motion Detector (radar range 0x100 instead of 0x60),
  8..10 full grenade ammo of kind 0..2 (8 each), 11 Assorted Grenades (2 each), 15 Floor Plan (0x8000, whole minimap; no way to set it was found: probably unused).
* `ammo[3]` (b94c) grenades per kind, `cur_weapon` (9d76), `rounds_loaded` (96f6, pistol rounds, 6 per clip), `spare_clips` (8dd6, 3), `bugs_left` (bb6e), `film_used` (a038, 36 shots).
* `hit_count` (cbac) hits taken; raid ends when it exceeds 1 (3 with the vest). `result_bits` (bb7a) -> shared byte 0x0a: 1 plot object taken,
  2 target arrested, 4 hurt. `disguised` (91dc) set after knocking a guard out (guards ignore you until you act). `player_acted` (cf44) noisy action this frame.
* Speed: `game_speed` (1bc8) 0/1/2 -> 6/4/2 vsync waits per half frame, stored in `skill_bits` bits 8-9. Space = fire the pistol (`fire_timer` 4), `5` toggles the slow step
  (named `running` by the earlier pass: probably it is a *sneak*; it uses `dir_dx` steps and sets `ACT_SNEAK`), arrows lunge (`lunge_timer`).
* Alarm: `alert_level` (91ea, counts down from `(amount/4)*(level+4)`, cap 9999; 0 = no alarm), `alarm_room`, `alert_cell_x/y` (a03a, a05c: last known player cell),
  `violence_count` (cbb8), `guards_left` (cf88, how many more guards may spawn), `level` (91e0, 0..4 difficulty), `campaign_level` (cf42), `shared_alert` (8dd0, shared+0x48).
* Mission kinds (`mission_kind`, a1e4): 0 ordinary break-in, 1 (selector 7) arrival phase then 0, 2 (selector 8) kill/rescue with the target room alarmed, 3 (selector 0xa) one-room mission.

## 9. Campaign copy and hand-off

TAC reads the tables of campaign.h from `env.sve` in the order of campaign.h (cbb9 118-134): `scalar_block` (98d8, 0x28e), `cast` (bdf6, 0x30*n_cast), `cast_prog` (caea),
`events` (a8f0, 0x2c*n_events), `cities` (9e18), `org_table` (c604, orgs; `orgs` c60a is its name field), `persons` (06fe), `links` (0afe), `activity` (16fe), `msg_table` (a1e6),
`plot_objects` (c99e, 4 x 18), counters (cbbc, 0x40). Word scalars: building_city = shared+0x20, building_org +0x22, option_flags +0x24, case_no +0x26,
`org_2` +0x28, +0x2a, +0x2c, +0x2e (round-tripped, names `shared_2a/2c/2e`), game_clock +0x30, n_persons +0x32, n_links +0x34, plot_step +0x36, `shared_38`, level +0x3a,
`shared_3c`, n_cast +0x3e, n_events +0x40, sel_person +0x42, gfx letter +0x44, n_msgs +0x46, city_mask +0x4a, org_seen_mask +0x4c, skill_bits +0x4e, heat +0x52.
The other DGROUP fields of these tables seen as placeholders (persons +2,+4,+6,+8, cities +0x18,+0x1c,+0x20, orgs +0x1a..+0x20, activity +2..+6) are `struct person/city/org/activity` members.

* **Explored rooms**: `links[i].u_08` and `u_0a` (two words at link offset 8, placeholder 0b06) hold a **32-bit mask of the rooms explored in the raid on that
  org/city** (tac_init 196-203 reads it into `ROOM_VISITED`, 244-248 writes it back). campaign.h lists them as "cleared on creation" only.
* Result written to the shared block (0018 233-241): `[8]` = `result_value` (`-1` if the player was stopped inside, else `bug_value` = sum of room values of the bugs planted;
  state.h calls this byte "person"), `[9]` strength 0..100 = `clamp(violence_count*10+20, 0, guards_left*10)`, `[0xa]` result_bits, `[0xb]` hit_count, `[0xd]` not_hurt,
  `[0xf]` frame_tick/60, `[0]` = 0 or 0x11. `photo_value` (8dd2) is only displayed.

## 10. The address with conflicting types: `cast` (bdf6)

`cast` is the start of the **cast table** (n_cast records of 48 bytes, `struct cast`). It is declared as a record array in the dossier/clue files (8106, 90b2, 9195, 94f3, 95a6,
986d, 9a3c, a522, a84c, b4a6), as `char[]` in cbb9/c7d2 (the block is moved with read_chunk/write_chunk as raw bytes: `0x30 * n_cast`), and as a plain `int` in
b321, which only reads the first word of the table: `persons[cast]` there is `persons[cast[0].person]` (the person of cast member 0, the "home" record). The three
views are the same bytes; b321 now declares `cast[]` like the others.

## 11. Findings and unknowns

* `city_known_count` (cbbc, int[16], campaign counters block) is also used by update_guard/update_player/tac_end_message as a **scalar counting awake guards**
  (5ca6 98, 121; 0f4c 563): its element 0 (counter of city 0) is overwritten with that count and written back by write_env_state (c7d2 102). This looks like an
  original bug (corrupts the first per-city counter); not corrected. Needs a check of how GAME uses the counters before it is called a bug.
* Files that use `equipment_bits`, `frame_tick` or `hit_count` as `char` (1b32, 2d10, 34e2, 0018) cannot include the `int` declarations of tac.h without changing bytes;
  1b32 therefore keeps its local declarations and raw `rooms[0xe*room]` offsets (comments say what they are). The actor flag byte is read as a byte by the guard code
  and as a word by the player code (hence `ACT_FLW`); the room flag byte likewise (`ROOM_FLAGS_W`).
* `alert_cached_room` (2032) is -1 and never written: raise_alert's "same room as last time" shortcut never fires.
* Unknown: `actors[].state == 3`, tile flag 0x80, the use of `persons[].u_08` (2d10 ORs it into `seen_by`; 8eab stores the initial known bits there), `cga_map_menu_a/b` exact format,
  `1c24` (handle from sub_0198 stored once), the many text-engine words 7304..735e, 738c/738e/7390, b914, c3f6, c3fa (picture loader callbacks), 36da, 3792, 379c, a1e0, 8b96.
* Not converted to shared types (local layouts kept): the dossier/clue text files (8106, 9a3c, a081, a522, a84c, b4a6, 986d, 9195...) still use their own struct views of persons/cast/events/
  messages; `strcpy(msg_subst[n], 0x24*org+0xc60a)` style offsets remain. They match as written.

## Proposals for shared headers

* **campaign.h** `struct link`: rename `u_08`/`u_0a` to one `long rooms_seen` (explored-rooms mask of the raid, section 9).
* **campaign.h** `struct person`: `u_08` is also written by TAC's person generator (8eab 41) with the initial known bits and ORed into `seen_by` by the arrest prompt (2d10); mark it as "known bits of the raid / mask of cast members who see him" once GAME confirms.
* **state.h** byte 0x08 `person`: TAC stores `result_value` there (`-1`, or the planted bugs' room-value sum); byte 0x09 is the 0..100 strength; byte 0x0b = `hit_count`; byte 0x0f = `frame_tick/60`.
* **state.h** word 0x4e `skill_bits`: bits 8-9 hold the TAC game speed (0 slow, 1 medium, 2 fast); bits 0-1 lower TAC's level (0018 118).
