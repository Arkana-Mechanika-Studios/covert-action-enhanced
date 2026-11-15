# Divergences register: CHASE

The car chase's part of the register (DIVERGENCES.md says what an entry is). Ids D-400 and up.

| ID | Where | The original | The C# | Why |
|---|---|---|---|---|
| D-400 | `ChaseRules.ImportSharedState` | Copies the six campaign tables as bytes, one `read_shared_or_file` call each (CHASE 00df:06ae): a far pointer into the hand-off block (`shared_src`, DS:23b2) and a byte cursor (`shared_cursor`, DS:23b6) that advances by each table's size; with the cursor at 0 it would read the bytes from a file instead (never: the only caller sets it to 0x400). | Copies the records of the typed hand-off (`SharedState.Campaign`) into this part's records; the cursor and the pointer are not kept (the oracle tests leave DS:23b2..23b7 out). | The hand-off is typed in the rewrite (ARCHITECTURE.md, "One program, typed hand-off"); the cursor and the pointer are working variables of the copy that nothing reads afterwards. The records copied are the same. |
| D-402 | `CarChase.Main` | At the end gives the memory of the cut gauge images back to DOS: `_dos_freemem` (CHASE 00df:57da) of the segment `gauge_images[0]` names. | Nothing: the images are objects, dropped with the part. | Images are typed objects in the rewrite, not DOS memory (D-11); nothing could observe the freed block. |
| D-401 | `CarChase.ChaseTick` | After the confirmed Alt-Q, `exit()` is called without an argument: the exit code DOS receives is a leftover word of the stack. | The exit code is 0. | As D-4: the launcher of the rewrite does not read the code. |
