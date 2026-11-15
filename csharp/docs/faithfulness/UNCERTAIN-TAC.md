# Uncertain register: TAC (building raid)

The TAC package's part of `UNCERTAIN.md` (ids from U-500). Same rules as there.

| ID | Where | The guess | What would settle it |
|---|---|---|---|
| U-500 | `BuildingRules.AddWallSegments` (its off-the-map read) | The start test of an inner wall reads the wall bits of up to three columns east of the map: the bytes after `wall_flags` in TAC's data segment, which are `AtLoc`, four unused bytes and then the bottom of the stack (TAC DS:d490 on). The C# takes those stack bytes as 0, as every recorded run has them. Traced in the whole session mastermind-debrief: the deepest stack TAC reaches before `generate_building` is DS:d82e, nowhere near DS:d490..d4c9, and no instruction of the launcher, INTRO, FINAL or GAME writes that memory before TAC starts (TAC is loaded where GAME was; the bytes lie above GAME's image and stack): TAC's start-up clears only its BSS, so the bytes are what the memory held before the game ran, 0 on the session machine. | Not TAC's code any more but the PC's memory: the bytes at TAC's load segment +0x1e2c0..+0x1e2fa on a real PC when TAC starts (they depend on DOS and what ran before the game), or a route in which a part run before the raid (a picture GAME allocates, CHASE, BUG) writes there. |
