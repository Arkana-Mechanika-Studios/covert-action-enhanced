# Open functions of the C# rewrite

Written by the triage of the 591 functions of FINAL, GAME, TAC, BUG and CHASE that `docs/source-map/original-functions.csv` listed with neither a C# member nor a reason. Every one has been read against the original (instructions of the EXE, the reconstructed C, the call sites) and put in one category:

* **a** untraced copy: the routine is already reconstructed under another coordinate. The coordinate is now an `also` entry of the member's `// orig:` comment (bodies compared as instructions with the places of data and code left out; a copy that differs only in addresses and constants counts).
* **b** library, run-time or not meaningful in one C# program: a rule in `docs/source-map/not-ported-<MODULE>.csv` with its reason.
* **c** dead: no call instruction, no far call and no pointer in the program reaches it (the whole code image was scanned, not only the reconstructed functions): a rule "no caller in the original (dead code)".
* **d** real gap: reachable code not yet ported.

## Summary

| Module | Open before | a (also entries) | b (rule) | c (dead) | d (real gap) | a, left for the Drivers folder |
|---|---:|---:|---:|---:|---:|---:|
| FINAL | 128 | 25 | 92 | 9 | 0 | 2 |
| GAME | 131 | 29 | 92 | 7 | 0 | 3 |
| TAC | 127 | 32 | 84 | 9 | 0 | 2 |
| BUG | 119 | 28 | 81 | 8 | 0 | 2 |
| CHASE | 86 | 22 | 54 | 8 | 0 | 2 |
| **all** | 591 | 136 | 403 | 41 | 0 | 11 |

**There is no real gap (d) among them.** What the player can reach in these five programs is ported; the rows that were open were the C library and run-time, the picture and animation file readers (replaced as a whole, D-12 and D-13), the DOS-only pieces (driver loading, the critical-error handler, the text-mode switch), copies of routines the C# already has, and dead code. The real gaps of the rewrite are not found by this list: it holds only functions that have a place in the symbol files, and ported-but-partial functions (a branch left out) are a matter for the `DIVERGENCE` and `UNCERTAIN` registers.

### How the groups were decided

* **Library routines** (416): 104 became `also` entries, 78 of them the same instructions as a routine another module has ported (`strcpy`, `strlen`, `rand`, `time`, ...; see `CovertAction.Msc` and `CovertAction.Platform`) and 26 the file calls of `IFiles` (`open`, `read`, `write`, `close`, `lseek`, `_dos_open`, `_dos_read`), which now carry their own `// orig:` comment with every copy; 312 got reasons (stdio and scanf internals behind the text reader, the heap, start-up and exit, DOS wrappers, copy, compare and search routines the ported code does with spans).
* **Assembly, thunks and data words** (60 thunks, 31 assembly routines, 5 data words): 20 became `also` entries (graphics-driver thunks the C# has under another coordinate, and `_dos_close`), 11 more are copies awaiting the Drivers folder (below), 60 got reasons: the picture decoder and its read hooks (D-12), the driver-call trampolines and `attach_driver_segment` (D-2), the EGA/Tandy palette loader, the INT 24h handler, the C run-time table walker, the library DOS wrappers and long-shift helpers.
* **Readable C** (79 were `matched`): 12 are copies of ported functions (the picture surface loader, the archive search, `close_resource`, `fatal_error`, `dos_seek`, `find_or_add_person_quiet`), 26 are the 512-byte readers and the text-mode switch of the fatal error, replaced by D-12 and D-13, and 41 are dead.

## (a) left for the Drivers folder

These eleven are copies of routines whose member lives in `CovertAction.Drivers`; they were added to the `also` lists after the sound work merged (14 November 2026), so every module now has no open function. For the record, each was added to the `// orig:` comment named; the instructions match (the first group on the whole range from the CODE routine to the next one, constants and addresses left out).

| Member (file) | Add to `also` |
|---|---|
| `McgaGraphicsDriver` comment of `CODE 0000:05fe` (the edge-table routines, `CODE 0000:05fe` to `0938`, with the clipper inside) | `FINAL 0000:068a`, `GAME 0000:05fe`, `TAC 0000:068a`, `BUG 0000:068a`, `CHASE 0000:05fe` |
| `LineClipper.cs`, comment of `CODE 0000:0938` | `FINAL 0000:09c4`, `GAME 0000:0938`, `TAC 0000:09c4`, `BUG 0000:09c4`, `CHASE 0000:0938` |
| `SpanTracker.cs`, comment of `FINAL 0000:0d8b` | `GAME 0000:0cff` |

## (d) real gaps

None. A later pass over the `DIVERGENCE`, `UNCERTAIN` and `DORMANT` markers is the place to look for reachable branches that are not ported.

## (c) dead code, for the record

Functions nothing calls. Two groups are worth a thought before anyone doubts this list: FINAL, TAC and BUG each carry their own copy of the intercepted-message text builder (`load_msg_endpoints`) and none calls it, and FINAL carries a copy of `gather_intelligence` (871 instructions) that it never calls (TAC and BUG call theirs; those are ported).

| Module | Function | Coordinate | C file (lines) | Why it is not reached |
|---|---|---|---|---|
| FINAL | `gather_intelligence` | `FINAL 0100:524c` | `final/final_0100_524c.c` (299) | no caller in the original (dead code); FINAL contains this copy of gather_intelligence but never calls it (TAC and BUG call their own) |
| FINAL | `load_msg_endpoints` | `FINAL 0100:7668` | `final/final_0100_7668.c` (123) | no caller in the original (dead code); the text builder of an intercepted message; no program calls its copy (TAC a522, BUG 3c9e) |
| FINAL | `create_file` | `FINAL 0100:c200` | `final/final_0100_c200.c` (20) | no caller in the original (dead code); its only caller is write_file_from_far, which has none |
| FINAL | `load_file` | `FINAL 0100:c24e` | `final/final_0100_c24e.c` (25) | no caller in the original (dead code) |
| FINAL | `read_file_prefix` | `FINAL 0100:c292` | `final/final_0100_c292.c` (25) | no caller in the original (dead code) |
| FINAL | `write_file_from_far` | `FINAL 0100:c2d8` | `final/final_0100_c2d8.c` (26) | no caller in the original (dead code) |
| FINAL | `load_picture_image` | `FINAL 0100:c3be` | `final/final_0100_c3be.c` (31) | no caller in the original (dead code) |
| FINAL | `sub_final_0100_c43c` | `FINAL 0100:c43c` | `final/final_0100_c43c.c` (24) | no caller in the original (dead code) |
| FINAL | `draw_open_picture` | `FINAL 0100:c498` | `final/final_0100_c498.c` (26) | no caller in the original (dead code) |
| GAME | `create_file_or_die` | `GAME 00f7:d582` | `game/game_00f7_d582.c` (20) | no caller in the original (dead code); its only caller is write_new_file, which has none |
| GAME | `read_whole_file` | `GAME 00f7:d5d0` | `game/game_00f7_d5d0.c` (30) | no caller in the original (dead code) |
| GAME | `read_file_block` | `GAME 00f7:d614` | `game/game_00f7_d614.c` (28) | no caller in the original (dead code) |
| GAME | `write_new_file` | `GAME 00f7:d65a` | `game/game_00f7_d65a.c` (28) | no caller in the original (dead code) |
| GAME | `load_picture_file` | `GAME 00f7:d740` | `game/game_00f7_d740.c` (30) | no caller in the original (dead code) |
| GAME | `load_picture_to_port` | `GAME 00f7:d7be` | `game/game_00f7_d7be.c` (31) | no caller in the original (dead code) |
| GAME | `decode_picture_rows` | `GAME 00f7:d81a` | `game/game_00f7_d81a.c` (30) | no caller in the original (dead code) |
| TAC | `set_palette_register` | `TAC 00e8:06a0` | `tac/tac_00e8_06a0.c` (25) | no caller in the original (dead code) |
| TAC | `load_msg_endpoints` | `TAC 00e8:a522` | `tac/tac_00e8_a522.c` (139) | no caller in the original (dead code); the text builder of an intercepted message; no program calls its copy (FINAL 7668, BUG 3c9e) |
| TAC | `create_file` | `TAC 00e8:d29a` | `tac/tac_00e8_d29a.c` (20) | no caller in the original (dead code); its only caller is write_file, which has none |
| TAC | `read_file_all` | `TAC 00e8:d2e8` | `tac/tac_00e8_d2e8.c` (25) | no caller in the original (dead code) |
| TAC | `read_file` | `TAC 00e8:d32c` | `tac/tac_00e8_d32c.c` (25) | no caller in the original (dead code) |
| TAC | `write_file` | `TAC 00e8:d372` | `tac/tac_00e8_d372.c` (25) | no caller in the original (dead code) |
| TAC | `load_pic_to_memory` | `TAC 00e8:d458` | `tac/tac_00e8_d458.c` (32) | no caller in the original (dead code) |
| TAC | `load_pic_keep_open` | `TAC 00e8:d4d6` | `tac/tac_00e8_d4d6.c` (27) | no caller in the original (dead code) |
| TAC | `load_pic_from_memory` | `TAC 00e8:d532` | `tac/tac_00e8_d532.c` (27) | no caller in the original (dead code) |
| BUG | `load_msg_endpoints` | `BUG 00e8:3c9e` | `bug/bug_00e8_3c9e.c` (114) | no caller in the original (dead code); the text builder of an intercepted message; no program calls its copy (FINAL 7668, TAC a522) |
| BUG | `create_file` | `BUG 00e8:7066` | `bug/bug_00e8_7066.c` (24) | no caller in the original (dead code); its only caller is save_file, which has none |
| BUG | `read_file_all` | `BUG 00e8:70b4` | `bug/bug_00e8_70b4.c` (30) | no caller in the original (dead code) |
| BUG | `read_file` | `BUG 00e8:70f8` | `bug/bug_00e8_70f8.c` (29) | no caller in the original (dead code) |
| BUG | `save_file` | `BUG 00e8:713e` | `bug/bug_00e8_713e.c` (27) | no caller in the original (dead code) |
| BUG | `load_pic_to_memory` | `BUG 00e8:7224` | `bug/bug_00e8_7224.c` (34) | no caller in the original (dead code) |
| BUG | `load_pic_keep_open` | `BUG 00e8:72a2` | `bug/bug_00e8_72a2.c` (29) | no caller in the original (dead code) |
| BUG | `load_pic_from_memory` | `BUG 00e8:72fe` | `bug/bug_00e8_72fe.c` (37) | no caller in the original (dead code) |
| CHASE | `sub_chase_00df_04a1` | `CHASE 00df:04a1` | `chase/chase_00df_0482.c` (28) | no caller in the original (dead code); an empty function |
| CHASE | `create_file` | `CHASE 00df:436e` | `chase/chase_00df_436e.c` (22) | no caller in the original (dead code); its only caller is save_file, which has none |
| CHASE | `load_file` | `CHASE 00df:43bc` | `chase/chase_00df_43bc.c` (29) | no caller in the original (dead code) |
| CHASE | `load_file_far` | `CHASE 00df:4400` | `chase/chase_00df_4400.c` (27) | no caller in the original (dead code) |
| CHASE | `save_file` | `CHASE 00df:4446` | `chase/chase_00df_4446.c` (26) | no caller in the original (dead code) |
| CHASE | `load_picture_to_memory` | `CHASE 00df:452c` | `chase/chase_00df_452c.c` (34) | no caller in the original (dead code) |
| CHASE | `load_picture_keep_open` | `CHASE 00df:45aa` | `chase/chase_00df_45aa.c` (30) | no caller in the original (dead code) |
| CHASE | `load_picture_from_memory` | `CHASE 00df:4606` | `chase/chase_00df_4606.c` (33) | no caller in the original (dead code) |

Lines are those of the C file the function is in (some files hold two small functions).

## Regenerating

`python -B csharp/tools/source_map.py` rewrites `original-functions.csv`; the open rows of a module are the ones with `not ported` and an empty note. With the eleven `also` entries above, FINAL, GAME, TAC, BUG and CHASE have none left.
