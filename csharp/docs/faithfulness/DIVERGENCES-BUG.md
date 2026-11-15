# Divergences register: BUG

The electronics part's part of the register (DIVERGENCES.md says what an entry is). Ids D-300 and up.

| ID | Where | The original | The C# | Why |
|---|---|---|---|---|
| D-301 | `ElectronicsState.MessageBeforeFirst` (`ICampaignTables`) | The library's message log reads and may change "message -1", the 14 bytes in front of the message table (BUG DS:604a ... DS:6057): there BUG keeps the end of city 15's record (`OrgClues`, `clued`, `geoX`, `geoY`), a word of the message box, `Nperson` and `mode`, so a change of message -1's knowledge word changes city 15's map position. | Message -1 is read from those variables (its minute `ct[15].OrgClues`, its knowledge word `ct[15].geoX`/`geoY`, its second city the high byte of `mode`) into a record of its own each time; a change to it is not written back. | It needs message 0 to have no partner and a minute equal to city 15's organisation clues, then a change of message -1's knowledge word; no recorded wiretap comes near it. Writing back would need the record to alias BUG's variables. As D-103, D-200, D-500. |

D-300 (the four library screens a campaign wiretap shows, `reveal_cast_clue`, `show_suspect_dossier`,
`append_msg_log_entry`, `reveal_person_facts`, stopped the part with `NotSupportedException`) is gone: the library has
them (`LibraryDossiers`, `CampaignLibrary.AppendMsgLogEntry`), and the workbench calls them on its own campaign tables
and texts (`ElectronicsState.Library.cs`, `BugDgroup.Dossier.cs`). Found by the session proof (SESSION-PROOF.md item 6:
BUG/chase-tap-timeout, the first wiretap whose intelligence opens a dossier).
