using CovertAction.Core.Electronics;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

// BUG's texts of the library's dossier, personnel file, clue and name routines, at their offsets: TAC's
// (TacDgroup.Dossier.cs) 0xb02 bytes lower, byte for byte (the month and person names, the clue labels (key_phrases), "00" and "000",
// the message file and tag template and " ... " are placed in BugDgroup.cs)
public static partial class BugDgroup
{
    /// <summary>The texts the table at DS:1f24 points at.</summary>
    private static (int Address, int Length)[] RankNamesTexts =>
    [
        (0x1c4a, 8), (0x1c52, 10), (0x1c5c, 11), (0x1c67, 6), (0x1c6d, 10), (0x1c77, 14), (0x1c85, 13), (0x1c92, 11),
    ];

    /// <summary>The texts the table at DS:207e points at.</summary>
    private static (int Address, int Length)[] ClueMethodsTexts =>
    [
        (0x1fc4, 18), (0x1fd6, 19), (0x1fe9, 20), (0x1ffd, 19), (0x2010, 16), (0x2020, 19), (0x2033, 18),
    ];

    /// <summary>The texts the table at DS:1a40 points at.</summary>
    private static (int Address, int Length)[] ActivityPhrasesTexts =>
    [
        (0x176a, 10), (0x1774, 16), (0x1784, 14), (0x1792, 15), (0x17a1, 16), (0x17b1, 10), (0x17bb, 16), (0x17cb, 12),
        (0x17d7, 16), (0x17e7, 16), (0x17f7, 10), (0x1801, 12), (0x180d, 16), (0x181d, 8), (0x1825, 12), (0x1831, 15),
        (0x1840, 15), (0x184f, 12), (0x185b, 15), (0x186a, 12), (0x1876, 14), (0x1884, 16), (0x1894, 12), (0x18a0, 10),
        (0x18aa, 12), (0x18b6, 15), (0x18c5, 15), (0x18d4, 10), (0x18de, 17), (0x18ef, 16), (0x18ff, 15), (0x190e, 15),
        (0x191d, 10), (0x1927, 17), (0x1938, 16), (0x1948, 14), (0x1956, 14), (0x1964, 14), (0x1972, 10), (0x197c, 14),
    ];

    /// <summary>The texts the table at DS:20ba points at.</summary>
    private static (int Address, int Length)[] ClueTokensTexts =>
    [
        (0x208c, 5), (0x2091, 6), (0x2097, 5), (0x209c, 6), (0x20a2, 6),
    ];

    /// <summary>Places the texts of the library's screens about persons (<see cref="ElectronicsState.Dossier"/>).</summary>
    private static void AddDossierTexts(StateLayout<ElectronicsState> layout)
    {
        layout
            .Text(0x1ada, 1, "DS:1ada PlotPrefix", s => s.Dossier.PlotPrefix, (s, v) => s.Dossier.PlotPrefix = v)
            .Text(0x1adb, 52, "DS:1adb PlotSuffix", s => s.Dossier.PlotSuffix, (s, v) => s.Dossier.PlotSuffix = v)
            .Text(0x1b0f, 1, "DS:1b0f IdentifiedPrefix", s => s.Dossier.IdentifiedPrefix, (s, v) => s.Dossier.IdentifiedPrefix = v)
            .Text(0x1b10, 36, "DS:1b10 IdentifiedMiddle", s => s.Dossier.IdentifiedMiddle, (s, v) => s.Dossier.IdentifiedMiddle = v)
            .Text(0x1b34, 2, "DS:1b34 IdentifiedSuffix", s => s.Dossier.IdentifiedSuffix, (s, v) => s.Dossier.IdentifiedSuffix = v)
            .Text(0x1b36, 34, "DS:1b36 PhotoPrefix", s => s.Dossier.PhotoPrefix, (s, v) => s.Dossier.PhotoPrefix = v)
            .Text(0x1b58, 3, "DS:1b58 PhotoSuffix", s => s.Dossier.PhotoSuffix, (s, v) => s.Dossier.PhotoSuffix = v)
            .Text(0x1b5b, 1, "DS:1b5b SpottedPrefix", s => s.Dossier.SpottedPrefix, (s, v) => s.Dossier.SpottedPrefix = v)
            .Text(0x1b5c, 22, "DS:1b5c SpottedMiddle", s => s.Dossier.SpottedMiddle, (s, v) => s.Dossier.SpottedMiddle = v)
            .Text(0x1b72, 2, "DS:1b72 SpottedSuffix", s => s.Dossier.SpottedSuffix, (s, v) => s.Dossier.SpottedSuffix = v)
            .Text(0x1b74, 1, "DS:1b74 MemberPrefix", s => s.Dossier.MemberPrefix, (s, v) => s.Dossier.MemberPrefix = v)
            .Text(0x1b75, 41, "DS:1b75 MemberMiddle", s => s.Dossier.MemberMiddle, (s, v) => s.Dossier.MemberMiddle = v)
            .Text(0x1b9e, 2, "DS:1b9e MemberSuffix", s => s.Dossier.MemberSuffix, (s, v) => s.Dossier.MemberSuffix = v)
            .Text(0x1ba0, 52, "DS:1ba0 InformationPrefix", s => s.Dossier.InformationPrefix, (s, v) => s.Dossier.InformationPrefix = v)
            .Text(0x1bd4, 2, "DS:1bd4 InformationSuffix", s => s.Dossier.InformationSuffix, (s, v) => s.Dossier.InformationSuffix = v)
            .Text(0x1bd6, 32, "DS:1bd6 RankPrefix", s => s.Dossier.RankPrefix, (s, v) => s.Dossier.RankPrefix = v)
            .Text(0x1bf6, 2, "DS:1bf6 RankSuffix", s => s.Dossier.RankSuffix, (s, v) => s.Dossier.RankSuffix = v)
            .Text(0x1bf8, 39, "DS:1bf8 RecruitingPrefix", s => s.Dossier.RecruitingPrefix, (s, v) => s.Dossier.RecruitingPrefix = v)
            .Text(0x1c1f, 2, "DS:1c1f RecruitingSuffix", s => s.Dossier.RecruitingSuffix, (s, v) => s.Dossier.RecruitingSuffix = v)
            .Text(0x1c21, 39, "DS:1c21 RecruiterPrefix", s => s.Dossier.RecruiterPrefix, (s, v) => s.Dossier.RecruiterPrefix = v)
            .Text(0x1c48, 2, "DS:1c48 RecruiterSuffix", s => s.Dossier.RecruiterSuffix, (s, v) => s.Dossier.RecruiterSuffix = v)
            .Text(0x2045, 5, "DS:2045 ClueTitle", s => s.Dossier.ClueTitle, (s, v) => s.Dossier.ClueTitle = v)
            .Text(0x204a, 10, "DS:204a Source", s => s.Dossier.Source, (s, v) => s.Dossier.Source = v)
            .Text(0x2054, 2, "DS:2054 Slash", s => s.Dossier.Slash, (s, v) => s.Dossier.Slash = v)
            .Text(0x2056, 2, "DS:2056 ReportNewline", s => s.Dossier.ReportNewline, (s, v) => s.Dossier.ReportNewline = v)
            .Text(0x2058, 11, "DS:2058 MethodLabel", s => s.Dossier.MethodLabel, (s, v) => s.Dossier.MethodLabel = v)
            .Text(0x2063, 16, "DS:2063 RelatedClues", s => s.Dossier.RelatedClues, (s, v) => s.Dossier.RelatedClues = v)
            .Text(0x2075, 8, "DS:2075 NoneRelated", s => s.Dossier.NoneRelated, (s, v) => s.Dossier.NoneRelated = v)
            .Text(0x20a8, 7, "DS:20a8 ClueTag", s => s.Dossier.ClueTag, (s, v) => s.Dossier.ClueTag = v)
            .Text(0x20c4, 2, "DS:20c4 ActivityPeriod", s => s.Dossier.ActivityPeriod, (s, v) => s.Dossier.ActivityPeriod = v)
            .Text(0x20c6, 11, "DS:20c6 ThisFace", s => s.Dossier.ThisFace, (s, v) => s.Dossier.ThisFace = v)
            .Text(0x20d1, 9, "DS:20d1 CodeNameReal", s => s.Dossier.CodeNameReal, (s, v) => s.Dossier.CodeNameReal = v)
            .Text(0x20da, 12, "DS:20da CodeNameDecoy", s => s.Dossier.CodeNameDecoy, (s, v) => s.Dossier.CodeNameDecoy = v)
            .Text(0x20ed, 5, "DS:20ed ClueTagBasic", s => s.Dossier.ClueTagBasic, (s, v) => s.Dossier.ClueTagBasic = v)
            .Text(0x20f2, 7, "DS:20f2 ClueTagCase", s => s.Dossier.ClueTagCase, (s, v) => s.Dossier.ClueTagCase = v)
            .Text(0x20f9, 7, "DS:20f9 ClueTagCast", s => s.Dossier.ClueTagCast, (s, v) => s.Dossier.ClueTagCast = v)
            .Text(0x2100, 10, "DS:2100 CluesFile", s => s.Dossier.CluesFile, (s, v) => s.Dossier.CluesFile = v)
            .Text(0x210b, 7, "DS:210b Name", s => s.Dossier.Name, (s, v) => s.Dossier.Name = v)
            .Text(0x2112, 7, "DS:2112 Rank", s => s.Dossier.Rank, (s, v) => s.Dossier.Rank = v)
            .Text(0x2119, 7, "DS:2119 Org", s => s.Dossier.Org, (s, v) => s.Dossier.Org = v)
            .Text(0x2120, 8, "DS:2120 City", s => s.Dossier.City, (s, v) => s.Dossier.City = v)
            .Text(0x2128, 10, "DS:2128 Recruited", s => s.Dossier.Recruited, (s, v) => s.Dossier.Recruited = v)
            .Text(0x2132, 5, "DS:2132 By", s => s.Dossier.By, (s, v) => s.Dossier.By = v)
            .Text(0x2137, 5, "DS:2137 In", s => s.Dossier.In, (s, v) => s.Dossier.In = v)
            .Text(0x213c, 2, "DS:213c Period", s => s.Dossier.Period, (s, v) => s.Dossier.Period = v)
            .Text(0x213e, 8, "DS:213e Role", s => s.Dossier.Role, (s, v) => s.Dossier.Role = v)
            .Text(0x2146, 20, "DS:2146 RoleNotInvolved", s => s.Dossier.RoleNotInvolved, (s, v) => s.Dossier.RoleNotInvolved = v)
            .Text(0x215a, 15, "DS:215a RoleUnknown", s => s.Dossier.RoleUnknown, (s, v) => s.Dossier.RoleUnknown = v)
            .Text(0x2169, 15, "DS:2169 UnderArrest", s => s.Dossier.UnderArrest, (s, v) => s.Dossier.UnderArrest = v)
            .Text(0x2178, 12, "DS:2178 Hiding", s => s.Dossier.Hiding, (s, v) => s.Dossier.Hiding = v)
            .Text(0x2184, 9, "DS:2184 TurnedNote", s => s.Dossier.TurnedNote, (s, v) => s.Dossier.TurnedNote = v)
            .Text(0x218e, 2, "DS:218e Quote", s => s.Dossier.Quote, (s, v) => s.Dossier.Quote = v)
            .Text(0x2192, 22, "DS:2192 NotDecoded", s => s.Dossier.NotDecoded, (s, v) => s.Dossier.NotDecoded = v)
            .Text(0x21a8, 8, "DS:21a8 More", s => s.Dossier.More, (s, v) => s.Dossier.More = v)
            .Text(0x2aea, 12, "DS:2aea MasterPlan", s => s.Dossier.MasterPlan, (s, v) => s.Dossier.MasterPlan = v)
            .Text(0x2af6, 11, "DS:2af6 Secret", s => s.Dossier.Secret, (s, v) => s.Dossier.Secret = v)
            .Text(0x2b01, 9, "DS:2b01 Arrested", s => s.Dossier.Arrested, (s, v) => s.Dossier.Arrested = v)
            .Text(0x2b0a, 10, "DS:2b0a InHiding", s => s.Dossier.InHiding, (s, v) => s.Dossier.InHiding = v)
            .Text(0x2b14, 8, "DS:2b14 Turned", s => s.Dossier.Turned, (s, v) => s.Dossier.Turned = v)
            .Text(0x2b1c, 15, "DS:2b1c PersonnelFile", s => s.Dossier.PersonnelFile, (s, v) => s.Dossier.PersonnelFile = v)
            .Text(0x2b2b, 13, "DS:2b2b ActionTeam", s => s.Dossier.ActionTeam, (s, v) => s.Dossier.ActionTeam = v)
            .Text(0x2b38, 3, "DS:2b38 Comma", s => s.Dossier.Comma, (s, v) => s.Dossier.Comma = v)
            .Text(0x2b3b, 14, "DS:2b3b NoActivity", s => s.Dossier.NoActivity, (s, v) => s.Dossier.NoActivity = v)
            .Text(0x21b1, 12, "DS:21b1 Received", s => s.Dossier.Words.Received, (s, v) => s.Dossier.Words.Received = v)
            .Text(0x21bd, 12, "DS:21bd Sent", s => s.Dossier.Words.Sent, (s, v) => s.Dossier.Words.Sent = v)
            .Text(0x21c9, 2, "DS:21c9 Space", s => s.Dossier.Words.Space, (s, v) => s.Dossier.Words.Space = v)
            .Text(0x21cb, 9, "DS:21cb Someone", s => s.Dossier.Words.Someone, (s, v) => s.Dossier.Words.Someone = v)
            .Text(0x21d4, 9, "DS:21d4 OfThe", s => s.Dossier.Words.OfThe, (s, v) => s.Dossier.Words.OfThe = v)
            .Text(0x21dd, 5, "DS:21dd In", s => s.Dossier.Words.In, (s, v) => s.Dossier.Words.In = v)
            .Text(0x21e2, 5, "DS:21e2 On", s => s.Dossier.Words.On, (s, v) => s.Dossier.Words.On = v)
            .Text(0x21e9, 2, "DS:21e9 End", s => s.Dossier.Words.End, (s, v) => s.Dossier.Words.End = v);
        TextTable(layout, 0x1f24, "DS:1f24 RankNames", RankNamesTexts, s => s.Dossier.RankNames);
        TextTable(layout, 0x207e, "DS:207e ClueMethods", ClueMethodsTexts, s => s.Dossier.ClueMethods);
        TextTable(layout, 0x1a40, "DS:1a40 ActivityPhrases", ActivityPhrasesTexts, s => s.Dossier.ActivityPhrases);
        TextTable(layout, 0x20ba, "DS:20ba ClueTokens", ClueTokensTexts, s => s.Dossier.ClueTokens);
    }
}
