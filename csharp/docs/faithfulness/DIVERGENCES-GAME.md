# Divergences register: GAME

The campaign part's part of the register (DIVERGENCES.md says what an entry is). Ids D-200 and up.

| ID | Where | The original | The C# | Why |
|---|---|---|---|---|
| D-200 | `CampaignState.MessageBeforeFirst` (`ICampaignTables`) | The library's message log reads and may change "message -1", the 14 bytes in front of the message log (GAME DS:7cb4 ... DS:7cc1): there GAME keeps the end of city 15's record, a word of the message box and `Nperson`, so a change of message -1's knowledge word changes city 15's sighting counter (`ct[15].clued`). | Message -1 is read from those variables (its minute `ct[15].police`, its knowledge word `ct[15].clued`, its second city the high byte of `Nperson`) into a record of its own each time; a change to it through the library is not written back. GAME's own message routines (`CampaignTexts`) do write it back. | It needs the first message of the log to have a partner but no pair at the same minute, a minute equal to city 15's police figure, and then a change of its knowledge word; no GAME vector reaches it. Writing back would need the record to alias GAME's variables. As D-500. |
