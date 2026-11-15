# Uncertain register: GAME

The campaign part's guesses (ids from U-200; the rules are those of UNCERTAIN.md).

| ID | Where | The guess | What would settle it |
|---|---|---|---|

Settled (15 November 2026), their markers removed:

* U-200 (`CastRules.PersonOfCastMember`): by reading. DS:9776 is written once, by `cut_face_sprites` (GAME 00f7:851f),
  which `game_main` (00f7:005a) runs through `init_sprite_sheets` before the campaign is loaded or the plot steps. The
  handle is what MGRAPHIC's `finish_image_build` (0000:099c) returns: the paragraph of the image in the block DOS gave
  `begin_image_cuts` (0000:0902, INT 21h AH=48h), far above 63; it is 0 only when image memory ran out (D-11). The C#
  was right; CA-33 now says the read has no effect.
* U-201 (`CodedMessages.ComposeMessageText`): by reading, unreachable. The menu lists only log records of type 0; GAME
  writes such records only in `queue_message(0, g)` from `advance_world_clock` (8755, g an event index, never
  negative), the other 13 calls of `queue_message` passing other kinds (the 14 call sites of 00f7:7834 in GAME.EXE), and
  FINAL's `add_msg_record` (0100:8c2c) has no call site. So the flag is always set before it is read.
* U-202 (`PlotEngine.EndTheCase`): by reading the launcher. `main` (COVERT 0000:0010) drops spawnl's result and loops on
  the hand-off block's `next_module`; the code (150 in the session GAME/mastermind-debrief) is read by no one. (The same
  reading answers FINAL's U-101.)
* U-203 (`CodedMessages.DecodeParts`): by reading, unreachable. Only sent, received and meeting events are decoded (the
  menu, 00f7:736f; the interrogation, 28a4); `advance_world_clock` gives such an event the step's `ChronTime` only
  together with its partner's (both are recorded in the same loop, at the same minute), or alone through EV_NO_WAIT
  (CRIME2's event 14, a received message) with `chron` 0, which equals `ChronTime` only at the case's first step
  (`start_case_briefing` sets -1), when no person can be out of play (FINAL sets every `alert` to 0; hiding needs more
  than 8 messages).
* U-204 (`PlotEngine.InterrogateSuspect`): the guess was wrong; settled by reading and an oracle case. A person outside
  the cast can have a known role: a double agent's event without partner (CRIME2 event 11, CRIME3 13, CRIME9 20) gives
  `sn[].who2` of its record, which `queue_message` does not set for such an event (a person of an earlier case's record
  in that slot), cast member 0's role bit (8755 253). The original then reads `ac[-1].status`, DS:979c, the carry word
  of the library's BIOS registers (`int86`, 00f7:f69a: 0 or 1, never the MasterMind's bit 100h), and `ac[-1].rank`,
  DS:97a2, NACTIVITY, for the chief's comment. The C# failed there (index -1); it now reads the same (CA-33). The case:
  build/oracle-u204-20261115/GAME/sub_game_00f7_28a4.json (the stasi-v1 interrogation with person 1 a look-alike of the
  suspect outside the cast, `rknown` 1), run by `GameOracleTests` with COVERT_ORACLE_DIR on that folder: it agrees.
* U-205 (`PlotEngine.ResolveInfiltration`): by reading, unreachable. `building_menu` (1e5a) calls it only when
  `entry_mode` is set or `D_9686` is 0, and `D_9686` is written once, to 1, by `game_main` (the four uses of DS:9686 in
  GAME.EXE are that write and the three tests).
* U-206 (`CityScreens.ChoosePlace`): not a guess of the C#, which does what `city_screen` does; the question of the
  intent moved to docs/original-bugs.md as CA-57.
* U-210 (`BuildingScreens.BuildingMenu`): by reading. `city_screen` calls `building_menu` right after `pass_time(0x78)`
  at the same stack depth (00f7:1e25, 1e41); `pass_time` ends with `wait_vretrace(0x1e)` once more than five messages
  are logged (as from the briefing on), whose `inp` (00f7:f7ae) saves `wait_vretrace`'s frame pointer exactly where
  `building_menu` keeps the level (its [bp-10h]). That is a stack offset (the stack lies at DS:aa60..b260), negative as
  an int, so `clamp(g - 2, 0, 3)` gives 0: `blding01.pan`, as the C# starts.
