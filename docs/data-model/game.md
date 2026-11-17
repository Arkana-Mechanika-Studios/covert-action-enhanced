# GAME data model

Source of truth for the C# data model of the GAME module. Record layouts are in
the reconstructed `campaign.h` (shared), constants and externs in
the reconstructed `game.h`. "ev:" = GAME file stem (`game_00f7_xxxx.c`)
and line. Offsets are DGROUP offsets of the original GAME.EXE. Everything below is checked
against the 204 byte-matched functions of the module (all of them use the tables as structs now).

## Campaign state: where it lives and how it is saved
The whole campaign is in DGROUP. `save_game` (aad4) writes it to `CVn.SVE` after the 0x3f0-byte
header (shared block +0x10, see state.h) in this order (`write_table`, `read_table` in aebb):

| order | DGROUP | bytes | content |
|---|---|---|---|
| 1 | 760a `scalar_block` | 0x28e | scalar block, see below |
| 2 | 97a6 `cast` | 0x30*N | N participants (N = `cast_count`) |
| 3 | a55e `cast_progress` | 6*N | per participant progress |
| 4 | 83c6 `events` | 0x2c*M | M events (`event_count`) |
| 5 | 7a9e `cities` | 0x220 | 16 cities |
| 6 | 9fb4 `orgs` | 0x240 | 16 organisations |
| 7 | 0094 `persons` | 0x400 | 64 persons |
| 8 | 0494 `links` | 0xc00 | 256 (org, city) links |
| 9 | 1094 `activity` | 0x3c0 | 80 clue entries |
| 10 | 7cc2 `messages` | 0x700 | 128 messages |
| 11 | a414 `plot_objects` | 0x48 | 4 objects |
| 12 | a628 `city_counters` + a648 `org_counters` | 0x40 | 16 + 16 words |

For the hand-off to the other modules ("env.sve") the same ranges are copied into the shared
block at +0x400 (`save_mem_offset` = 0x400). Total file size 9766 + 54N + 44M (campaign.h).

## Tables

### persons: 0094, 16 bytes x 64 (`struct person`)
One person the player can learn about. Cast members map onto persons (`cast[i].person`).
A slot is free when `look == 0` (4899 52-55, 4d4b 36). Slot 0 is the mastermind's own
org/city pair (`mastermind_org/city`): that pair always uses slot 0.
Fields: `org` 00 (organisation index, -1 after `release_person`), `look` 02 (bit0 = male; bits 1-4 first-name
index, 5-8 surname index; the other bits pick the face parts; 08ee, 81a6), `city` 04, `known` 06 (word,
bit set: see KNOWN_* in game.h; the low byte is also accessed bytewise: `known.b.lo`), `u_08` 08
(bit mask set by `find_or_add_person_quiet`; `& 0x7fff` != 0 keeps the record, 49d1/9e58; unknown meaning, probably
"cast members this person has dealt with"), `seen_by` 0a (bit n = cast member n saw him; bit 15 unused),
`score` 0c (investigation score, 3fa6: `+= d*d/(score/2+d+1)`; halved by `clear_person_knowledge`),
`status` 0e (see PSTATUS_*; >= 0 = trail strength that decays by `status/(level+3)` at every plot step (8755 100)).
Writers: 4899/4d4b (create), 3fa6 (search), 28a4/1608/3077/323b (arrest), 8755 (plot step), 91fa
(forget), 49d1 (release). Readers: nearly every screen (0978, 716a, 98c8, 9c61, 9e58, 6cfa).
Unknown: meaning of `u_08` bits; 0x63 stored in `status` by the arrest code.

### links: 0494, 12 bytes x 256 (`struct link`)
(organisation, city) pairs created by `find_or_add_link(org, city, flags)` (4e4f). `org` 00, `city` 02, `flags` 04
(OR of the flags passed; bit 4 = building known, 2728/28a4 pass 0xc), `percent` 06 (wiretap
percentage, 1e5a 174 adds to it, 8755 255 compares with `random_below(50)`, "WireTaps" screen lists
percent > 1 (692a)), `u_08`/`u_0a` 08/0a (cleared on creation; TAC stores a 32-bit bit mask of explored
rooms there, tac.h). `link_count` (8ece) = used entries. Removal: 4ef0 (`remove_link` clears flags/percent
and the `orgs_seen` bit of the city).

### activity: 1094, 12 bytes x 80 (`struct activity`)
A clue the player has collected (shown as "Clue"/"Activity" lists, 5035 `add_clue`, 9c61, 98c8, 5513).
`org` 00 (char; -1 removed), `city` 01 (char), `cast` 02 (cast index; -1 = free slot), `what` 04 =
(kind << 8) + index (SUBJECT_*: 1/2 person, 4 organisation, 8 city, 0x10 cast member), made by
`make_clue_subject` (5393); `when` 06 = second code made by `cast_clue_code` and printed through
`clue_label_table` (5668) (the name is a misnomer; probably the role/rank code of the cast member);
`u_08[4]` unused. Free slot search: first `cast == -1` (5035 60). Cleared by 91fa.

### cities: 7a9e, 34 bytes x 16 (`struct city`, WORLDn.DTA)
`name[12]` 00 (also "<name>.pic", 3cfd), `country[12]` 0c, `grid` 18 (map cell: low nibble column, high nibble
row; only the low byte is used, `map_distance` 06b3), `size` 1a (weight in `org_presence`: `(size+4) *
org.strength / (distance+1)`, 07f3), `orgs_seen` 1c (bit n = organisation n is known to operate here), `seen_today`
1e (bit n = visited organisation n here; zeroed for all cities at every plot step, 8755 99), `x`, `y` 20/21 (pixel
position on the map, 36d3/3791). The world is chosen by the third letter of city 0's name (33aa: 's' Europe,
'm' Africa, 'g' Central America).

### orgs: 9fb4, 36 bytes x 16 (`struct org`, WORLDn.DTA)
`short_name[6]` 00 (printed by 6cfa 62 / 716a 43 / 5f21 as "/<short>"), `name[20]` 06, `grid` 1a (map cell as
for cities), `strength` 1c and `divisor` 1e (presence formula, 07f3), `flags` 20 (char; bit0 = known to the player; also
accessed as a word by 3fa6 253: `*(int *)&orgs[x].flags |= 1`), `ethnic` 22 (a start 0..24 of a window of 16 names in the first-name and surname
tables, `2 * ethnic`, 08ee; not a group 0..3), `u_21` unused, `u_23` (`race_hi`) always 0 and read by nothing. org 0 is not listed by the screens (6cfa 53), org 1 = the player's agency:
`orgs[1].name` is "$US" (2eb9) and `orgs[1].grid` is the agency's base (3a95, 1e5a 73).

### cast: 97a6, 48 bytes x N (`struct cast`, CRIMEn.DTA participants)
`person` 00 (index into persons; 0xffff in the file), `exposure` 02, `role[34]` 04 (printed as the
participant's role/name, 0978 80, 6cfa 81), `u_26` 26 (file: byte 38 = mastermind flag; runtime bit 0x100 = the
flagged member, 0x4000 = listed on the Clues screen), `flags` 28 (one bit per clue method found; 0x1f = all
found), `clue_type` 2a (2 = special rule in 4f52), `rank` 2c (1..24, title index (rank-1)/3, 28a4 184), `u_2e`.
Readers: everywhere; writer: 8755 (plot step), 5035 (clues).

### cast_progress: a55e, 6 bytes x N (`struct cast_prog`)
`step` 00 (plot step at which the member acts, -1 = none; set at the first event, 8755 172), `u_02` 02
(progress counter; `+= 5000/(level+3)/k`, halved by 91fa, 3fa6 128), `u_04` unused.

### events: 83c6, 44 bytes x M (`struct event`, CRIMEn.DTA events; last record is a marker)
`actor` 00 (cast index), `step` 02 (plot step it happened, 999 = not yet, -1 cancelled, 0 = immediate),
`msg_id` 04, `text[32]` 06, `target_kind` 26 (word: low byte = cast index it happens to, high byte = kind 2 sent, 3
received, 8 met, 9 was met by, 0x20 crime; the whole word is masked with 0xfff/0xf00/0x1000/0x2000), `items` 28
(low byte items received, high byte destroyed, bit tests in 8755 210-232), `score` 2a.

### messages: 7cc2, 14 bytes x 128 (`struct msg`)
Created by `queue_message(kind, arg)` (7834): `kind` 00 (0 = event message; 1 departure, 5 arrival, 6/0xc
interrogation result, 8 hiding, 11 sighting; -1 unused), `due` 02 (game minute queued; /0x5a0+1 = day), `event`
04 (event index or the argument), `told` 06 (bits 1-3 parts known, 0xe all, 0xf complete, 0x8000 decoded),
`strength` 08 (trail value of person[0a] when queued; /20 feeds the hint counter 7cf4 126), `person` 0a (event actor's person),
`city` 0b (his city), `other` 0c (the other party's person), `other_city` 0d. Pairs of messages (sender
and receiver record of one event) are adjacent (5f21 33-36). Log full at 128 (message "NSYN Overflow").
Readers: 3fa6 (what the player learns), 7cf4/5f21 (text), 790c (Chronology), 9e58 (advisor), 28a4, 7554.

### plot_objects: a414, 18 bytes x 4 (`struct object`)
`name[17]`, `holder` 11 (file: picture id; runtime: person index holding it, -1 none; 8755 220-232, 2fb6 61).

### city_counters a628 / org_counters a648: 16 words each
Sighting counters (`++` when a person is spotted in a city / tied to an organisation; 3fa6 248, 4a04 68-80, 5393).

## Scalar block (760a, 0x28e bytes, first range of the save)
Known members (offset from 760a): +0x04 `mastermind_known` (760e), +0x06 `mastermind_org` (7610),
+0x08 `mastermind_city` (7612), +0x0a `mastermind_look` (7614) (the pair that always uses person slot 0),
+0x16 `clock_month` (7620, months since Jan 1990 = 0x7c6), +0x18 `saved_minutes` (7622), +0x1a `saved_location`
(7624, current city name, listed in the slot menu), +0x26 agent rank number (shown "00<n> rank", a519 45), +0x2a..
`victim_text` (7634, 40 bytes) and `object_text` (765c) (copied into `$VICTIM`/`$OBJECT`), +0x7a codename
(a519 43). Layout of the rest: unknown. The listing in a519 reads the block into 93f0 with
`slot_*` aliases (9406 = +0x16 ... 946a = +0x7a).

## Text substitution
`text_var_names` (3a10) = 7 names ("$VICTIM", "$SNDORG", "$RCVORG", "$SNDLOC", "$RCVLOC", "$HLPORG", "$OBJECT"), `text_var_values` (3a1e) =
7 pointers to the buffers filled by `compose_message_text` (7cf4) and `describe_event` (801e); `clue_var_names` (31b8)
+ the first 5 slots serve "clues.txt" (570d: [0] subject text, [1] person, [2] org, [3] city). Keys of text.dta
are built in `text_key` (5554) with `text_key_digit_1..4` (80df).

## Globals with their own meaning (see build/names/dg.txt for the evidence of each)
Campaign scalars (shared block words, state.h): `cur_city`, `cur_org`, `campaign_flags`, `game_minutes`,
`person_count`, `link_count`, `plot_step`, `level`, `cast_count`, `event_count`, `selected_person`, `message_count`,
`city_mask`, `mask_4c`, `skill_bits`, `heat`, `case_no`, `org_2`, `campaign_word_2a/2c/2e/38/3c` (no use found).
Tables of data: `bit_count_table` (popcount of 0..31), `first_name_table`/`surname_table` (pointers),
`rank_name_table`, `clue_label_table`, `activity_phrase_table`, `building_palettes`, sprite handle tables
(`table_image_sprites` 40, `face_sprites` 40, `icon_sprites` 16, ...).
Popup/screen state: `screen_state`, `popup_no_select`, `popup_no_wait`, `face_x/face_y`, `status_bar_y`, `cached_city` and the picture cache.
Other: `excluded_person`/`flagged_cast_person` (event choice), `in_hotel_wait`, `errno_value`, `current_drive`,
the pan engine requests (`scene_request_*`, `pan_*`), the save-slot menu (`save_*`, `slot_*`), the menu box (`menu_*`).

## Renames made in campaign.h (other modules that include it must use the new names)
`struct msg`: state -> kind, u_06 -> told, u_08 -> strength, to -> person, city (0b) unchanged, u_0b/u_0c replaced by
`other`, `other_city`, event unchanged. `struct person`: u_0c -> score. `struct event`: target/kind -> union word2
`target_kind` (b.lo = target, b.hi = kind, w = both). `struct city`: u_18 -> grid, u_1a -> size. `struct org`: u_00 -> short_name,
u_1a -> grid, u_1c[4] -> strength, divisor. No file of BUG/CHASE/FINAL/TAC used the old names (checked by grep and by
compiling every file that includes campaign.h or the module headers; the two TAC files that include it still match).

## Unknown or unverified
- `person.u_08` meaning; `cast.u_26` high byte; `cast.u_2e`; `cast_prog.u_04`; `org.u_21/u_23` and the file values of org +0x20 (uniqueId) vs
  the runtime flag byte; `city.size` vs the file's unknown2; `activity.when` exact meaning; `activity.u_08`; `msg.strength` units.
- the rest of the 0x28e scalar block (about 0x180 bytes after +0x2a are only known at 7634, 765c, +0x7a).
- pan engine and menu internals left unnamed (their files belong to the engine, not the campaign model): 14ae/149e palette tables, 2764,
  42a6-42ac, 42fe/4302/4314, 55d6-55e4, 55a6, 55b0, 5602-5608, 5750/5756, 57b2/57b4, 6fba, 71ff-7203, 720a/720d, 7cbe, 9684,
  9686, 968e, 9774-977a (single sprites), 97a4, a3e8, a3ea, a3f0, a45e, a622, a9fe, 4236.
- a few short text fragments (" ", "\n ", "." ...) of 716a, 736f and 790c are unnamed.

## Proposals for shared headers
- state.h: none needed; the shared block words 0x2a, 0x2c, 0x2e, 0x38, 0x3c still have no use outside save/load.
- campaign.h: when TAC/BUG/FINAL/CHASE adopt `struct msg`, they should use the names above; `union word2 target_kind` replaces
  hand-made `kind_target` ints (bug 1882, tac 8106).
- The `status` codes (PSTATUS_*) and `known` bits (KNOWN_*) are in game.h; moving them to campaign.h would let TAC/BUG/FINAL use them.
