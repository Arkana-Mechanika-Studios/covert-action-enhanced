# Divergences register: TAC

The building raid's part of the register (DIVERGENCES.md says what an entry is). Ids D-500 and up.

| ID | Where | The original | The C# | Why |
|---|---|---|---|---|
| D-500 | `RaidState.MessageBeforeFirst` | The library's message log reads and may change "message -1", the 14 bytes in front of the message table (TAC DS:a1d8): there the raid keeps `room_last_visit[21..23]`, `open_door_count`, a word and `GameMode`, so a change of message -1's knowledge word changes the count of open doors. | Message -1 is read from those variables (its minute, its knowledge word, its second city) into a record of its own each time; a change to it is not written back. | It needs message 0 to have no partner and the same minute as the last visit of room 22, and then a change of its knowledge word; no recorded run of the raid comes near it. Writing back would need the record to alias the raid's variables. |
| D-501 | `BuildingRaid.FinishSetUp` (`tac_init`) | Just before the first frame the raid asks the video BIOS for the cursor's position (INT 10h, function 3) and uses no part of the answer. | The call is left out. | The rewrite has no video BIOS, and nothing reads the registers the call fills in. |
