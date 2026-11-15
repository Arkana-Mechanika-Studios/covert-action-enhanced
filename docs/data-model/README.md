# Data model (data pass, 13 November 2026)

> **Names now in the code.** Since 13 November 2026 the reconstruction uses MicroProse's
> original names where the Amiga symbol table gives them (certain and likely lines of
> `symbols/amiga-names.txt`, method in `docs/amiga.md`): tables `ac`, `cl`, `ps`, `org`,
> `ct`, `ev`, `lc`, `sn`, `mc`, `pl`, scalars such as `InCity`, `AtLoc`, `diff`, struct
> types such as `Activity` (our cast), `Clue` (our activity), `Synopsis` (our message),
> `Location` (our link), `RasterPort`, and their members. The specs below still use our
> earlier names; `symbols/names.txt` gives each renamed label as "formerly <our name>",
> and the reconstructed headers note the former name at every renamed declaration.

The specification the C# data model will be written from: for each module, every table and
important global with its DGROUP offset, record size and count, fields with meanings and
units, writers, readers, evidence and what is still unknown. Written per
module and checked by hand. The reconstruction carries the same model as types: the shared headers
the reconstructed `state.h` (the block at 0000:04F0) and `campaign.h` (the saved campaign
records), and one header per module (`game.h`, `final.h`, `tac.h`, `bug.h`, `chase.h`,
`code.h`).

| Module | Spec | Data addresses the C uses: before / after | Unnamed: before / after |
|---|---|---|---|
| GAME | [game.md](game.md) | 354 / 307 | 260 / 60 |
| FINAL | [final.md](final.md) | 418 / 323 | 336 / 10 |
| TAC | [tac.md](tac.md) | 422 / 375 | 295 / 106 |
| BUG | [bug.md](bug.md) | 294 / 223 | 181 / 8 |
| CHASE | [chase.md](chase.md) | 176 / 177 | 47 / 0 |
| CODE, COVERT, INTRO | [code-covert-intro.md](code-covert-intro.md) | 122 / 103 | 45 / 4 |
| all | | 1786 / 1508 | 1164 / 188 |

Fewer addresses after the pass mostly means placeholders that were really fields of a table
(`D_0096` after `persons` at `0094`) now read as struct members (`persons[i].look`). 709 names
with evidence were added to `symbols/names.txt`. All 809 files still match and all eight
modules still rebuild byte-identical.

## Decisions taken while merging

- `activity.when` (+6) is renamed `key`: it is the clue's key, a packed code like `what`
  (kind in the high byte), from the cast record or `clues.txt`, printed through the table
  of case-item names and shown as `$KEY`. BUG read it as an item code, FINAL as a
  `clues.txt` text id; both are halves of the same thing, and it is not a time. With it,
  `_when_phrases` (BUG 1f44) became `_key_phrases` and `_append_when_text` (BUG 2f44)
  `_append_key_text` (the old names stay in symbols/names.txt as `formerly`). The helper
  notes below still use the old name `when`.

## Shared headers: decisions

Settled on 13 November 2026 against the reconstructed C of every module.
No compiled byte changed: every reconstructed function still compiles to the original's bytes.
"Accepted" = applied as proposed, "adjusted" = applied in another form, "rejected" = not applied.

**New shared header `gfx.h`: `struct gfx_port`** (CODE, CHASE, BUG proposals) - accepted.
One 18-byte record `page, x0, y0, x1, y1, text_flag, text_color, fill_color, font` (CODE's
names). The copies in `code.h` (`struct gfx_port`), `chase.h` (`struct port`) and `bug.h`
(`struct port`, 0x14 bytes with a `self` member that is really the pointer variable after
the record) are gone; the headers include `gfx.h`. Renamed members: BUG `mode` -> `page`,
`text_background` -> `text_flag`, `text_colour` -> `text_color`, `fill_colour` ->
`fill_color`; CHASE `bg_color` -> `fill_color`. GAME, FINAL and TAC had no type, only raw
offsets (`*(int *)((char *)screen_port + 0xe)`, `screen_port[7]`, `*screen_port`) and
nine file-local copies (`struct Z/F/W/X/fnt`): the 64 files that reach into the record now
declare `extern struct gfx_port *screen_port;` and use the members; the local structs are
removed. Files that only pass `screen_port` on keep `extern int screen_port;`. Key codes
and SCREEN_W/H stay in the module headers (not checked across modules).

**`state.h`**
- Bytes 08, 09, 0x0a, 0x0b (TAC, FINAL, CHASE) - adjusted. They are an in/out area: GAME
  fills them for the next module, the minigame overwrites them with its result, GAME reads
  the result by `game_mode`. The header now lists, per module, what goes in and what comes
  out (08: person in, TAC -1 = captured / bug value, BUG 5 * phones percent; 09: CHASE start
  org in, TAC suspicion, BUG alarms, CHASE outcome, CODE solved; 0a: quarry's sex in, TAC
  result bits, CHASE alert percent). Names `person`, `value`, `result` kept (no single
  meaning); 0x0b `count` -> `wounds` (only TAC writes it: hits taken; GAME passes 6 hours
  each, 1608 163-168).
- 0x28..0x3c (FINAL, TAC) - accepted: 0x28 `org_2` -> `plot_org_2`, 0x2a `plot_org_3`,
  0x2c `plot_city_2`, 0x2e `plot_city_3` (FINAL 3f4c 116-131; placement bits of the cast,
  46df), 0x3c `random_seed` (FINAL 0056 84). 0x38 stays `u_38`: FINAL setup_new_case sets
  -1 (3f4c 104), nobody reads it. Module globals renamed with `rename_name.py`: `_org_2` ->
  `_plot_org_2` in all five modules; GAME `campaign_word_2a/2c/2e` and CHASE `saved_2a/2c/2e`
  -> `plot_org_3`, `plot_city_2`, `plot_city_3`; GAME `campaign_word_3c` and CHASE `saved_3c`
  -> `saved_random_seed` (CHASE already has its own `random_seed`); GAME `campaign_word_38`
  -> `saved_38` (same as FINAL, CHASE).
- Campaign flag bits (CODE) - accepted and completed; one set `OPT_*` in `state.h`
  replaces the copies in `code.h` and `chase.h`: 0x02 `OPT_FEMALE` (FINAL 067e), 0x04 sound,
  0x08 animation, 0x10 joystick, 0x20 `OPT_PRACTICE` (FINAL start_practice_module sets it;
  the modules return to FINAL; was `OPT_TO_FINAL` in CODE, `OPT_END_OF_CASE` in CHASE),
  0x40 `OPT_SAVED_IN_FINAL` (FINAL 175e 263, 0056 171; probably), 0x80 `OPT_CLEARED`
  (copy-protection face check passed, FINAL 499e 79).
- `skill_bits` (CODE, TAC) - accepted and completed: two bits per skill in the order of
  FINAL's skill_names (Combat 0-1 TAC, Driving 2-3 CHASE, Cryptography 4-5 CODE,
  Electronics 6-7 BUG), game speeds TAC 8-9 and CHASE 10-11. `SKILL_*_SHIFT`,
  `SPEED_TAC_SHIFT`, `SPEED_CHASE_SHIFT` in `state.h`; CHASE's `SKILL_EASY_SHIFT` /
  `SKILL_SPEED_SHIFT` replaced.
- Block size and regions (COVERT) - accepted: `SHARED_SIZE` 12000, `SHARED_TEXT` 0x200,
  `SHARED_TABLES` 0x400, and the module code table, in the header comment. `level` 0..3
  now cites FINAL's difficulty menu (175e 252).

**`campaign.h`**
- `cast` +0x24 (BUG, FINAL) - adjusted: `char role[32]` + `union word2 key_seed`. FINAL
  writes the word (`random(0x7fff)`, unique in bits 1-6, 46df 39, 3f4c 235); GAME, FINAL,
  BUG and TAC read only the low byte as the low part of the clue key. Neither BUG's two
  bytes nor FINAL's `clue_text` fits both uses.
- `cast.u_26` (FINAL) - accepted as `role_flags` with `CAST_*` bits: placement 0x01, 0x02,
  0x10, 0x20, 0x40, 0x80 (FINAL 46df), 0x08 `CAST_TRAVELS` (probably; GAME 8755 301), 0x100
  MasterMind, 0x200 female, 0x4000 listed on the Clues screen (GAME 98c8), 0x8000 revealed
  (FINAL 2d63, the "+0x27 bit 0x80"; no reader found). Kept an `int` (not a `word2`): only
  2d63 touches a byte. The file side agrees with CovertActionTools (byte 0x27 bit 0
  mastermind, bit 1 female); its "bit 6 = assassin" (0x4000) has no reader in the C: GAME
  uses that bit as a scratch mark and clears it.
- `org` +0x20 (FINAL) - accepted: `union word2 flags` (lo = MasterMind number, hi = plot
  mask in the WORLD file; after setup_new_case clears it, bit 0 = known). Byte users now
  write `.flags.b.lo`, word users `.flags.w`. Field name kept.
- `person.u_08` (FINAL; GAME unsure) - accepted as `cast_mask` (bit n = cast member n,
  0x8000 = decoy). The only writer of non-zero values is FINAL's case setup (46df 76 `1 <<
  n`, 3f4c 231 0x8000); GAME's and TAC's `find_or_add_person_quiet` have no callers.
- `person.score` reused by the debrief (FINAL) - accepted as a comment (175e 111, 2e06 90).
- `links[].u_08/u_0a` (TAC) - adjusted: `union dword2 rooms_seen` (`.l` in TAC, `.w.lo` /
  `.w.hi` where GAME, FINAL, BUG, TAC clear the two words in one statement; a plain `long`
  would change those statements).
- `msg.person` / `msg.other` (BUG) - accepted: already person indices in the header; the
  comments now say so with BUG's evidence.
- `org.short_name` (CHASE) - accepted: comment says CHASE and BUG display it.
- `game.h`'s `CAST_U26_*` were replaced by the `CAST_*` set (no C used them).

## Still open

- 0x0a after CODE: GAME passes `0x1e * result` minutes (6458 62) but CODE never writes
  byte 0x0a, so GAME uses whatever the previous hand-off left there. Check with a run.
- Bytes 05, 06, 0x10..0x1f, 0x54..0x5f and word 0x38 have no known use.
- Probably an overrun of the shared block: the tables at +0x400 need 8758 + 54 * n_cast +
  44 * n_events bytes, the block leaves 10976. At level 3 (12 decoys) CRIME3 (8 cast, 29
  events) needs 138 bytes more and CRIME8 (8, 30) 182 more; they would run past COVERT's
  33a0 (bios_regs, then whatever follows). Possibly harmless; not yet in the register.
- CHASE writes its game speed back with `skill_bits & 0xf7ff | speed << 10` (001c 142),
  which clears only bit 11: "slow" is not kept and "fast" comes back as 3 (step 0x20/5
  instead of 0x20/4 in 29d8). A quirk; not yet in the register.
- `msg.told` low nibble: GAME reads parts of the message, BUG 1882/37fd "learned facts"
  (1 date, 2 person, 4 organisation, 8 city); not reconciled.
- GAME's proposal to move `PSTATUS_*` / `KNOWN_*` from `game.h` to `campaign.h` was not
  part of this pass.
- Many files still declare their own copies of campaign records (`struct T`, `C48`, `R48`,
  ...) or of `screen_port` as `int`; they match, and converting them is a later clean-up.

## Possible original bugs found (to check before or during the C# port)

Tracked in the register [docs/original-bugs.md](../original-bugs.md) (CA-1 to CA-7).

- CA-1, confirmed by reading: TAC reuses element 0 of `city_known_count` as its
  awake-guards counter and saves it with the campaign; GAME reads city 0's counter in the
  chief's advice and the Activity Report. Data-damaging (minor): exact behind a switch,
  fixed by default.
- COVERT's code 0x63 (`mcv.exe`) path reads `module_names[0x63]`, far past the 18-entry
  table ([code-covert-intro.md](code-covert-intro.md)).
- CHASE `can_move` tests side bits 0 and 3, while the map generator and the street view
  number the sides differently ([chase.md](chase.md)).
- CHASE `check_location_entry` reuses an uninitialised distance when a location is unknown.
