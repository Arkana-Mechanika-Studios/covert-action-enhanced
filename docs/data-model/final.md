# FINAL data model

Module FINAL: title screen, character creation, case briefing (plot and
apartment scene), end-of-case debrief, efficiency report, career summary, Hall of Fame,
save/load menus, the pop-up menu driver, the picture/animation (.pan) engine. Header:
the reconstructed `final.h` (plus `campaign.h`, `state.h`).
All DGROUP offsets below are FINAL's own (not GAME's). "ev:" = file stem `final_0100_xxxx.c`.

## 1. Campaign tables (same records as GAME, saved in this order by `write_env_state` a2f6)

The save file / `env.sve` is: header (shared+0x10, 0x3f0 bytes), then the ranges below, in this
order (a2f6 102-117, read back by a6dd). The records are the types of `campaign.h`.

| range | name | offset | elements | record |
|---|---|---|---|---|
| 0x28e | career block | 7df2 (`plot_no`) | see section 3 | scalars + strings |
| 0x30*N | `cast` | 9fa6 | N = `n_cast` (9fa2) x 48 | `struct cast` |
| 6*N | `cast_prog` | ad6c | N x 6 | `struct cast_prog` |
| 0x2c*M | `events` | 8bca | M = `n_events` (9e88) x 44 | `struct event` |
| 0x220 | `cities` | 8294 | 16 x 34 | `struct city` (WORLDn.DTA) |
| 0x240 | `orgs` | a7be | 16 x 36 | `struct org` (WORLDn.DTA), name at +6 |
| 0x400 | `persons` | 0188 | 64 x 16 | `struct person` |
| 0xc00 | `links` | 0588 | 256 x 12 | `struct link` |
| 0x3c0 | `clues` | 1188 | 80 x 12 | `struct activity` (see below) |
| 0x700 | `msg_table` | 84b8 | 128 x 14 | `struct msg` |
| 0x48 | `plot_objects` | ac20 | 4 x 18 | `struct object` |
| 0x40 | `city_counters`, `org_counters` | ae38, ae58 | 16 + 16 ints | ++ when a city / organisation becomes known |

Everything in the old placeholder list that sat inside these tables (018a..0196, 118a..118e,
82ac/82b0/82b4, a7c4/a7d8/a7de/a7e0/a7fc, 84ba..84be, 8bcc..8bf1, 9fcd/9fce/9fd0, ...) is now a
member access (`persons[i].look`, `clues[i].cast`, `cities[i].grid`, ...).

### FINAL-specific readings of the shared records
* `clues` (name given earlier by someone else; layout of `struct activity`): +0 org (char), +1 city
  (char), +2 cast index (-1 = free slot), +4 `what` = clue target code `(kind << 8) + index`,
  kind 1 look, 2 name, 4 organisation, 8 city, 0x10 cast (ev: 6639, 62db); **+6 is the clues.txt text
  id** (`lookup_clue_text`, ev: 62db 68), not "when" (proposal below).
* `orgs[i]` word at +0x20 (campaign.h: `flags`, `u_21`): in the WORLD file the low byte is the number
  (0..25) of the MasterMind that sits in organisation i, the high byte (bits 8-11) is a mask of the
  plots he may be used for (`plots[].org_mask`); -1 = no MasterMind (ev: 0734 85-91). At run time
  bit 0 is set when the organisation becomes known (ev: 499e 60, 5caa, 524c). `orgs[1].grid` (a7fc)
  is the map key of the home organisation; used as reference point by `setup_new_case`.
* `persons[i].u_08`: bit mask of the cast members mapped onto this person (bit n = cast n), 0x8000 =
  decoy; the person is free when `u_08 & 0x7fff == 0` (ev: 5ff1, 5c77, 85ec).
* `persons[i].score` is overwritten with the known bits by the debrief (175e 114) before the case
  log mutates them; the efficiency report reads its low byte back (ev: 2e06 100-103: `& 0xe == 0xe`,
  `& 1`). `persons[0]` is Mr. Big; his known bits are kept in `big_known` (7df6).
* `cast[i]`: +0x24 word (role[32..33]) is the clue text number (low 7 bits / >>1, ev: 66ec) and a
  random word for decoys (3f4c 236); `u_26` holds the placement flags set by the plot (1 = organisation
  A, 2/0x10/0x20 city 1/3/2, 0x40 org C, 0x80 org B, 0x100 the MasterMind, 0x200 clear person bit 0,
  ev: 46df) and is later tested as 0x100 by the dossier; `flags` (+0x28) low 5 bits = clue types
  found; the byte at +0x27 (high byte of `u_26`) gets 0x80 in `reveal_case_state` (2d63; meaning
  unknown); `clue_type` (+0x2a) tested == 2 in 61f8; `rank` (+0x2c) 0..7 indexes
  `rank_names`.
* `events[i].target_kind` word: low byte target cast index, high nibble kind (0x200/0x300 message
  sent/received, 0x800/0x900 met, 0x2000 flag "no cast name"), bit 0x80 of the high byte set by
  `reveal_case_state` (ev: 2d63, 8d04). `events[].score` (+0x2a) = crime score, 0x28 EP per point.
* `msg_table[i]`: `kind` 0 = plot event (`event` = event index), -1 unused; `told` bits 1-3 name,
  organisation, city of the other party, bit 0 date; 0x8000 decoded; `strength` is what campaign.h
  calls the +8 word (clue value; the alert text picks "*ALRT00"+strength/20).
* `plot_objects[i].name[16]` (last byte of the name field) is the icon number of the object
  (ev: 2941 38); `holder` = cast index holding it, -1 none.

## 2. Plots, MasterMinds, case set-up

| name | offset | size | content |
|---|---|---|---|
| `plots` | 1e9e | 16 x 74 | `struct plot`: title[24], `org_mask`, `crime_file[8]` (per variant, -1 none), `item[8]` pairs of text pointers. Plot 0 "Welcome Max" = first case. Plot titles: Welcome Max, 'KeyHole' Sattelite, Freedom Games, SuperDrug, Liberian Election, Summit Conference, Rampant Inflation, Stolen Nuke, Super Cannon, Build Nuclear Weapon, Terror Campaign, Virus X, Drug Shortage, Train Wreck, Prison Break, Stealth Fighter (the C comments used D_1eb6 = plots[].org_mask) |
| `mastermind_looks` | 1cfe | 26 x 16 | `struct mastermind_look` (female flag, look bits, 4 face parts, clothes picture) packed into a look word by sub_11b7 |
| `mastermind_names` | 2d76 | 26 pointers | names shown in the career summary |
| `crime_names` | 2d5c | 13 pointers | crime title by CRIMEn.DTA number |
| `fame_table` | b1c0 | 6 x 44 | `struct fame_entry` (FAME.DTA is the same 0x108 bytes) |
| `vacation_table` / `vacation_places` | 2f3e / 2f36 | 21 x 5 ints / 4 pointers | rating row -> place (0 laundromat, 1 secretarial pool, 2 beach, 3 casino) + up to 4 extra pictures |
| `skill_names`, `skill_colors` | 247e, 24a8 | 4 pointers, 4 ints | training menu and rating strip |
| `first_names`, `surnames` | 4526, 4626 | [2][64] / 64 pointers (128 + 64) | first names by sex; surnames for both sexes; index = 2 * `org[].race` (+0x22, WORLD values 0..24) + 4 look bits, so the 16-name windows overlap (ev: 8562) |
| `rank_names` | 3ae0 | 8 pointers | Recruit .. MasterMind |
| `clue_labels`, `clue_methods`, `clue_phrases` | 3b00, 3c3a, 35fc | [8][8], 7, 40 pointers | clue text pieces |
| `macro_tokens`, `macro_values` | 3c76, 3ed8 | 5 and 7 pointers | `$KEY $NAME $ORG $CITY $ROLE` and the buffers (at 3de0..) they are replaced by; text.dta tags use slots 1-6 differently (ev: 7668) |
| `image_table` | 9e9c | 116 words | picture handles: 0-39 female face parts, 40-79 male, 80-95 8x8 tiles, 96-102 strips, 104-107 / 112-115 clothes (0x68 / 0x70 + look&3), 108-111 icons |

`plot_org_1` (abf6), `org_2` (9e94), `plot_org_3` (9e98), `plot_city_1` (ac1e), `plot_city_2` (96da),
`plot_city_3` (96dc): the three organisations / cities chosen by `setup_new_case` (3f4c) for the plot
(the saved ones are state words 0x28, 0x2a, 0x2c, 0x2e). `big_org`/`big_city` (7df8/7dfa) = Mr. Big.

State words (state.h offsets, saved in a2f6/a6dd): 0x20 `city`, 0x22 `org`, 0x24 `campaign_flags`,
0x26 `case_no`, 0x28 `org_2`, 0x2a `plot_org_3`, 0x2c `plot_city_2`, 0x2e `plot_city_3`, 0x30 `clock`,
0x32 `n_persons`, 0x34 `n_links`, 0x36 `plot_step`, 0x38 `saved_38` (-1 after set-up), 0x3a `level`,
0x3c `random_seed` (time & 0x7fff), 0x3e `n_cast`, 0x40 `n_events`, 0x42 `sel_person`, 0x44
`gfx_letter`, 0x46 `n_msgs`, 0x4a `city_mask`, 0x4c `mask_4c`, 0x4e `skill_bits`, 0x52 `heat`.

## 3. The career block (0x28e bytes at 7df2, saved first; read into `slot_block` 9bf0 for the slot list)

| offset | name | meaning |
|---|---|---|
| 7df2 | `plot_no` | plot number 0..15 (digits 3-4 of the `*PLnn` tag, ev: 0734, 0906) |
| 7df4 | `variants_done_mask` | bit v = variant v of this plot already played (0906 49) |
| 7df6 | `big_known` | known bits of Mr. Big kept across the case reset (175e 116) |
| 7df8/7dfa | `big_org`, `big_city` | MasterMind's organisation (4..15) and city |
| 7dfc | `big_look` | his look word (0734 89) |
| 7dfe | `mastermind_no` | 0..25 |
| 7e00 | `last_variant` | variant of the case (0..7) |
| 7e02 | `crime_no` | CRIMEn.DTA number (copied to `case_no`) |
| 7e04 | `region` | world file 0..2 |
| 7e06 | `career_points` | += rating*(level+1)/16 per case |
| 7e08 | `career_months` | finished cases; month name `% 12`, year 1990 + n/12 |
| 7e0a | `career_clock` | copy of `clock` made by `write_env_state` |
| 7e0c | `save_city_name` | 12 bytes, city name for the slot list |
| 7e18 | `rank` | CIA rank 1..99 |
| 7e1c / 7e44 | `plot_item_1/2` | 0x28 bytes each: names of the two objects of the case |
| 7e6c | `code_name` | player's code name |
| 7e7c | `career_records` | 0xff-terminated records (4 or 5 bytes) of finished cases: (plot<<4)+variant, flags (low nibble arrests, 0x40 crime committed, 0x80 MasterMind caught), EP hi, EP lo, [MasterMind no] |
| 807c | `mastermind_mask` | long, bit n = MasterMind n caught (807e is its high word) |

Slot list offsets inside `slot_block`: +0x16 career_months, +0x18 clock copy, +0x1a city name, +0x26
rank, +0x7a code name (ev: 9d3b).

## 4. Animation / picture engine (ac84..bdc4, 98a5, 8f74)
* `struct anim_params` (14 bytes) at 5d76 (`animation`, written by `select_animation`, used by the menu
  driver) and 5e32 (`pan_params`, written by `play_pan_picture` via sub_abd2): file name pointer, +4
  screen, +6 speed (-1 endless), +8 x, +10 y, +12 frame length (-1 keep). Twin of CODE's
  `_animation_name/_x/_y`.
* `.pan` header: magic (5e4e, compared with `pan_magic` 4b40), version (5e4a, must be 3 = `pan_version`
  5bba), packed flag (5e4c). Palette block: `pan_has_palette` 96de, type 0/1/2 (b30a), data 98e2
  (0x306 bytes max). Row buffers `pan_row_buffer` 5e54 and `picture_row_buffer` b080 (320 bytes each).
* Ports: `pan_port` 4afc (visible page, first word toggles each frame), `pan_work_port` 4afe,
  `pan_screen_copy` 4b36, `pan_saved_screen` 4b38, `pan_background` 4b3a. Pacing: `frame_length` 96d4,
  `frame_deadline` 5e40, `frame_parity` 4b3e, `frame_budget` 5e20.
* Script: `script_pc/sp/state/wait/done`, `script_vars` b30e (51), far segment 0fb4:0000 (stack of
  1000 words, 4 queued sound values, 50 sprite records of 0x34 bytes; ev: b01a).
* Mouse: `mouse_regs` 8bba = `union REGS` (ax, bx, cx, dx..) for int 33h; results in
  `mouse_buttons/x/y` (a7b4..a7b8).

## 5. Menu driver globals
`menu_*` (named earlier) plus `menu_title_color` 5dd4, `menu_item_color` 5dd6, `menu_selected_color`
5dda, `menu_first_draw` 9e8e, `menu_marked_mask` 9fa4, `menu_ok_button` 84b4, `menu_clear_width` 47c0,
`pending_portrait` 8bb8 (look word drawn over the animation, -1 none), `portrait_x/y` a7ae/a7bc.
Colour tables for the graphics thunks (16 entries each): `cga_map_*` (thunk 0255 takes two, values
0..3) and `ega_palette*` (thunk 024e takes one, values 0..15).

## Proposals for shared headers
campaign.h (GAME helper):
* `struct activity`: field at +6 (`when`) is the clues.txt text id of the clue in FINAL
  (`lookup_clue_text`); rename or document (GAME's use of +6 should be checked: 5035/9c61).
* `struct cast`: bytes 0x24-0x25 (inside `role[34]`) hold the clue text number (word) / random word;
  suggest `char role[32]; int clue_text;` and `u_26` = `place_flags`; add `union word2` to `u_26`
  (byte at +0x27 gets 0x80). `flags` low byte = clue types found.
* `struct org`: the word at +0x20 (`flags` + `u_21`) is `union word2 mastermind` (lo = MasterMind number,
  hi = plot mask in the WORLD file; bit 0 = known at run time).
* `struct msg.told` byte access (low byte |= 0xf) occurs in FINAL (2d63); an `union word2` would help.
* `struct person.u_08` is the cast bit mask (+0x8000 decoy flag); `score` is reused as saved known bits
  by the debrief.
state.h: offsets 0x28/0x2a/0x2c/0x2e are `org_2`, `plot_org_3`, `plot_city_2`, `plot_city_3`;
0x3c is the random seed (`time(0) & 0x7fff`); 0x38 is set to -1 by `setup_new_case`.

## Unknowns
* 000e (declared in 0434, unused), 47bc (set to -1 before a menu animation), 5f96, 779e (result of
  the picture unpacker), 96d6 and ae7a (cleared at the start of a new case, ae7a = -1 after the
  apartment scene), 9bee/9e86 (set to -1 / 1 at start-up), abf4 (file argument stored by
  `read_pan_header`), 807e (high word of `mastermind_mask`).
* Meaning of `cast[].flags` bit 0x80 of `u_26` high byte, the exact units of `persons[].score`
  outside the debrief, the full contents of the 0x28e career block between 7e6c and 7e7c.
* Names are valid for FINAL only; they follow campaign.h as of this pass (msg.strength, person.score,
  event.target_kind, org.grid ...). If the GAME helper renames fields again, files that include
  `final.h` must follow.
