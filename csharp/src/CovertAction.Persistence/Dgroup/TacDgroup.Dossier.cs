using CovertAction.Core.Raid;
using CovertAction.Persistence.Layout;

namespace CovertAction.Persistence.Dgroup;

// TAC's texts of the library's dossier, personnel file, clue and name routines, at their offsets
public static partial class TacDgroup
{
    /// <summary>The texts the table at DS:25c2 points at.</summary>
    private static (int Address, int Length)[] MonthNamesTexts =>
    [
        (0x248c, 4), (0x2490, 4), (0x2494, 4), (0x2498, 4), (0x249c, 4), (0x24a0, 4), (0x24a4, 4), (0x24a8, 4),
        (0x24ac, 4), (0x24b0, 4), (0x24b4, 4), (0x24b8, 4),
    ];

    /// <summary>The texts the table at DS:2a26 points at.</summary>
    private static (int Address, int Length)[] RankNamesTexts =>
    [
        (0x274c, 8), (0x2754, 10), (0x275e, 11), (0x2769, 6), (0x276f, 10), (0x2779, 14), (0x2787, 13), (0x2794, 11),
    ];

    /// <summary>The texts the table at DS:2b80 points at.</summary>
    private static (int Address, int Length)[] ClueMethodsTexts =>
    [
        (0x2ac6, 18), (0x2ad8, 19), (0x2aeb, 20), (0x2aff, 19), (0x2b12, 16), (0x2b22, 19), (0x2b35, 18),
    ];

    /// <summary>The texts the table at DS:2542 points at.</summary>
    private static (int Address, int Length)[] ActivityPhrasesTexts =>
    [
        (0x226c, 10), (0x2276, 16), (0x2286, 14), (0x2294, 15), (0x22a3, 16), (0x22b3, 10), (0x22bd, 16), (0x22cd, 12),
        (0x22d9, 16), (0x22e9, 16), (0x22f9, 10), (0x2303, 12), (0x230f, 16), (0x231f, 8), (0x2327, 12), (0x2333, 15),
        (0x2342, 15), (0x2351, 12), (0x235d, 15), (0x236c, 12), (0x2378, 14), (0x2386, 16), (0x2396, 12), (0x23a2, 10),
        (0x23ac, 12), (0x23b8, 15), (0x23c7, 15), (0x23d6, 10), (0x23e0, 17), (0x23f1, 16), (0x2401, 15), (0x2410, 15),
        (0x241f, 10), (0x2429, 17), (0x243a, 16), (0x244a, 14), (0x2458, 14), (0x2466, 14), (0x2474, 10), (0x247e, 14),
    ];

    /// <summary>The texts the table at DS:2bbc points at.</summary>
    private static (int Address, int Length)[] ClueTokensTexts =>
    [
        (0x2b8e, 5), (0x2b93, 6), (0x2b99, 5), (0x2b9e, 6), (0x2ba4, 6),
    ];

    /// <summary>The texts the table at DS:356c points at.</summary>
    private static (int Address, int Length)[] SurnamesTexts =>
    [
        (0x32ab, 7), (0x32b2, 6), (0x32b8, 8), (0x32c0, 9), (0x32c9, 8), (0x32d1, 7), (0x32d8, 7), (0x32df, 8),
        (0x32e7, 8), (0x32ef, 8), (0x32f7, 6), (0x32fd, 7), (0x3304, 7), (0x330b, 8), (0x3313, 10), (0x331d, 9),
        (0x3326, 7), (0x332d, 5), (0x3332, 7), (0x3339, 6), (0x333f, 6), (0x3345, 6), (0x334b, 7), (0x3352, 7),
        (0x3359, 5), (0x335e, 7), (0x3365, 7), (0x336c, 6), (0x3372, 8), (0x337a, 8), (0x3382, 6), (0x3388, 7),
        (0x338f, 6), (0x3395, 5), (0x339a, 6), (0x33a0, 7), (0x33a7, 6), (0x33ad, 7), (0x33b4, 7), (0x33bb, 7),
        (0x33c2, 5), (0x33c7, 6), (0x33cd, 6), (0x33d3, 7), (0x33da, 8), (0x33e2, 8), (0x33ea, 5), (0x33ef, 7),
        (0x33f6, 6), (0x33fc, 5), (0x3401, 6), (0x3407, 6), (0x340d, 8), (0x3415, 8), (0x341d, 7), (0x3424, 6),
        (0x342a, 7), (0x3431, 6), (0x3437, 8), (0x343f, 7), (0x3446, 9), (0x344f, 9), (0x3458, 8), (0x3460, 9),
    ];

    /// <summary>The texts the table at DS:346c points at.</summary>
    private static (int Address, int Length)[] FirstNames0Texts =>
    [
        (0x2f61, 9), (0x2f6a, 7), (0x2f71, 6), (0x2f77, 8), (0x2f7f, 6), (0x2f85, 7), (0x2f8c, 8), (0x2f94, 6),
        (0x2f9a, 8), (0x2fa2, 9), (0x2fab, 5), (0x2fb0, 9), (0x2fb9, 9), (0x2fc2, 9), (0x2fcb, 5), (0x2fd0, 6),
        (0x2fd6, 6), (0x2fdc, 4), (0x2fe0, 6), (0x2fe6, 7), (0x2fed, 6), (0x2ff3, 7), (0x2ffa, 6), (0x3000, 5),
        (0x3005, 8), (0x300d, 7), (0x3014, 5), (0x3019, 6), (0x301f, 4), (0x3023, 5), (0x3028, 8), (0x3030, 5),
        (0x3035, 6), (0x303b, 7), (0x3042, 7), (0x3049, 6), (0x304f, 7), (0x3056, 6), (0x305c, 7), (0x3063, 6),
        (0x3069, 7), (0x3070, 8), (0x3078, 6), (0x307e, 7), (0x3085, 8), (0x308d, 7), (0x3094, 9), (0x309d, 5),
        (0x30a2, 9), (0x30ab, 7), (0x30b2, 8), (0x30ba, 7), (0x30c1, 6), (0x30c7, 6), (0x30cd, 8), (0x30d5, 7),
        (0x30dc, 7), (0x30e3, 6), (0x30e9, 6), (0x30ef, 6), (0x30f5, 7), (0x30fc, 6), (0x3102, 8), (0x310a, 8),
    ];

    /// <summary>The texts the table at DS:34ec points at.</summary>
    private static (int Address, int Length)[] FirstNames1Texts =>
    [
        (0x3112, 7), (0x3119, 7), (0x3120, 5), (0x3125, 7), (0x312c, 7), (0x3133, 7), (0x313a, 7), (0x3141, 5),
        (0x3146, 6), (0x314c, 8), (0x3154, 8), (0x315c, 6), (0x3162, 7), (0x3169, 7), (0x3170, 9), (0x3179, 8),
        (0x3181, 5), (0x3186, 4), (0x318a, 5), (0x318f, 4), (0x3193, 4), (0x3197, 5), (0x319c, 5), (0x31a1, 6),
        (0x31a7, 8), (0x31af, 9), (0x31b8, 5), (0x31bd, 7), (0x31c4, 6), (0x31ca, 7), (0x31d1, 7), (0x31d8, 7),
        (0x31df, 5), (0x31e4, 4), (0x31e8, 6), (0x31ee, 6), (0x31f4, 5), (0x31f9, 4), (0x31fd, 6), (0x3203, 6),
        (0x3209, 7), (0x3210, 8), (0x3218, 6), (0x321e, 7), (0x3225, 8), (0x322d, 8), (0x3235, 9), (0x323e, 6),
        (0x3244, 7), (0x324b, 5), (0x3250, 7), (0x3257, 6), (0x325d, 5), (0x3262, 6), (0x3268, 5), (0x326d, 6),
        (0x3273, 8), (0x327b, 8), (0x3283, 7), (0x328a, 6), (0x3290, 7), (0x3297, 7), (0x329e, 7), (0x32a5, 6),
    ];

    /// <summary>The texts the table at DS:2a46 points at.</summary>
    private static (int Address, int Length)[] ClueLabels0Texts =>
    [
        (0x27bb, 14), (0x27c9, 13), (0x27d6, 16), (0x27e6, 10), (0x27f0, 12), (0x27fc, 14), (0x280a, 9), (0x2813, 11),
    ];

    /// <summary>The texts the table at DS:2a56 points at.</summary>
    private static (int Address, int Length)[] ClueLabels1Texts =>
    [
        (0x281e, 14), (0x282c, 19), (0x283f, 15), (0x284e, 15), (0x285d, 18), (0x286f, 12), (0x287b, 10), (0x2885, 13),
    ];

    /// <summary>The texts the table at DS:2a66 points at.</summary>
    private static (int Address, int Length)[] ClueLabels2Texts =>
    [
        (0x2892, 13), (0x289f, 13), (0x28ac, 10), (0x28b6, 12), (0x28c2, 14), (0x28d0, 13), (0x28dd, 14), (0x28eb, 12),
    ];

    /// <summary>The texts the table at DS:2a76 points at.</summary>
    private static (int Address, int Length)[] ClueLabels3Texts =>
    [
        (0x28f7, 8), (0x28ff, 9), (0x2908, 9), (0x2911, 12), (0x291d, 8), (0x2925, 6), (0x292b, 11), (0x2936, 7),
    ];

    /// <summary>The texts the table at DS:2a86 points at.</summary>
    private static (int Address, int Length)[] ClueLabels4Texts =>
    [
        (0x293d, 10), (0x2947, 6), (0x294d, 8), (0x2955, 8), (0x295d, 11), (0x2968, 10), (0x2972, 10), (0x297c, 10),
    ];

    /// <summary>The texts the table at DS:2a96 points at.</summary>
    private static (int Address, int Length)[] ClueLabels5Texts =>
    [
        (0x2986, 2), (0x2988, 2), (0x298a, 2), (0x298c, 2), (0x298e, 2), (0x2990, 2), (0x2992, 2), (0x2994, 2),
    ];

    /// <summary>The texts the table at DS:2aa6 points at.</summary>
    private static (int Address, int Length)[] ClueLabels6Texts =>
    [
        (0x2996, 2), (0x2998, 2), (0x299a, 2), (0x299c, 2), (0x299e, 2), (0x29a0, 2), (0x29a2, 2), (0x29a4, 2),
    ];

    /// <summary>The texts the table at DS:2ab6 points at.</summary>
    private static (int Address, int Length)[] ClueLabels7Texts =>
    [
        (0x29a6, 17), (0x29b7, 13), (0x29c4, 20), (0x29d8, 12), (0x29e4, 19), (0x29f7, 13), (0x2a04, 13), (0x2a11, 20),
    ];

    /// <summary>Places the texts of the library's screens about persons (<see cref="RaidState.Dossier"/>).</summary>
    private static void AddDossierTexts(StateLayout<RaidState> layout)
    {
        layout
            .Text(0x25dc, 1, "DS:25dc PlotPrefix", s => s.Dossier.PlotPrefix, (s, v) => s.Dossier.PlotPrefix = v)
            .Text(0x25dd, 52, "DS:25dd PlotSuffix", s => s.Dossier.PlotSuffix, (s, v) => s.Dossier.PlotSuffix = v)
            .Text(0x2611, 1, "DS:2611 IdentifiedPrefix", s => s.Dossier.IdentifiedPrefix, (s, v) => s.Dossier.IdentifiedPrefix = v)
            .Text(0x2612, 36, "DS:2612 IdentifiedMiddle", s => s.Dossier.IdentifiedMiddle, (s, v) => s.Dossier.IdentifiedMiddle = v)
            .Text(0x2636, 2, "DS:2636 IdentifiedSuffix", s => s.Dossier.IdentifiedSuffix, (s, v) => s.Dossier.IdentifiedSuffix = v)
            .Text(0x2638, 34, "DS:2638 PhotoPrefix", s => s.Dossier.PhotoPrefix, (s, v) => s.Dossier.PhotoPrefix = v)
            .Text(0x265a, 3, "DS:265a PhotoSuffix", s => s.Dossier.PhotoSuffix, (s, v) => s.Dossier.PhotoSuffix = v)
            .Text(0x265d, 1, "DS:265d SpottedPrefix", s => s.Dossier.SpottedPrefix, (s, v) => s.Dossier.SpottedPrefix = v)
            .Text(0x265e, 22, "DS:265e SpottedMiddle", s => s.Dossier.SpottedMiddle, (s, v) => s.Dossier.SpottedMiddle = v)
            .Text(0x2674, 2, "DS:2674 SpottedSuffix", s => s.Dossier.SpottedSuffix, (s, v) => s.Dossier.SpottedSuffix = v)
            .Text(0x2676, 1, "DS:2676 MemberPrefix", s => s.Dossier.MemberPrefix, (s, v) => s.Dossier.MemberPrefix = v)
            .Text(0x2677, 41, "DS:2677 MemberMiddle", s => s.Dossier.MemberMiddle, (s, v) => s.Dossier.MemberMiddle = v)
            .Text(0x26a0, 2, "DS:26a0 MemberSuffix", s => s.Dossier.MemberSuffix, (s, v) => s.Dossier.MemberSuffix = v)
            .Text(0x26a2, 52, "DS:26a2 InformationPrefix", s => s.Dossier.InformationPrefix, (s, v) => s.Dossier.InformationPrefix = v)
            .Text(0x26d6, 2, "DS:26d6 InformationSuffix", s => s.Dossier.InformationSuffix, (s, v) => s.Dossier.InformationSuffix = v)
            .Text(0x26d8, 32, "DS:26d8 RankPrefix", s => s.Dossier.RankPrefix, (s, v) => s.Dossier.RankPrefix = v)
            .Text(0x26f8, 2, "DS:26f8 RankSuffix", s => s.Dossier.RankSuffix, (s, v) => s.Dossier.RankSuffix = v)
            .Text(0x26fa, 39, "DS:26fa RecruitingPrefix", s => s.Dossier.RecruitingPrefix, (s, v) => s.Dossier.RecruitingPrefix = v)
            .Text(0x2721, 2, "DS:2721 RecruitingSuffix", s => s.Dossier.RecruitingSuffix, (s, v) => s.Dossier.RecruitingSuffix = v)
            .Text(0x2723, 39, "DS:2723 RecruiterPrefix", s => s.Dossier.RecruiterPrefix, (s, v) => s.Dossier.RecruiterPrefix = v)
            .Text(0x274a, 2, "DS:274a RecruiterSuffix", s => s.Dossier.RecruiterSuffix, (s, v) => s.Dossier.RecruiterSuffix = v)
            .Text(0x2b47, 5, "DS:2b47 ClueTitle", s => s.Dossier.ClueTitle, (s, v) => s.Dossier.ClueTitle = v)
            .Text(0x2b4c, 10, "DS:2b4c Source", s => s.Dossier.Source, (s, v) => s.Dossier.Source = v)
            .Text(0x2b56, 2, "DS:2b56 Slash", s => s.Dossier.Slash, (s, v) => s.Dossier.Slash = v)
            .Text(0x2b58, 2, "DS:2b58 ReportNewline", s => s.Dossier.ReportNewline, (s, v) => s.Dossier.ReportNewline = v)
            .Text(0x2b5a, 11, "DS:2b5a MethodLabel", s => s.Dossier.MethodLabel, (s, v) => s.Dossier.MethodLabel = v)
            .Text(0x2b65, 16, "DS:2b65 RelatedClues", s => s.Dossier.RelatedClues, (s, v) => s.Dossier.RelatedClues = v)
            .Text(0x2b77, 8, "DS:2b77 NoneRelated", s => s.Dossier.NoneRelated, (s, v) => s.Dossier.NoneRelated = v)
            .Text(0x2baa, 7, "DS:2baa ClueTag", s => s.Dossier.ClueTag, (s, v) => s.Dossier.ClueTag = v)
            .Text(0x2bc6, 2, "DS:2bc6 ActivityPeriod", s => s.Dossier.ActivityPeriod, (s, v) => s.Dossier.ActivityPeriod = v)
            .Text(0x2bc8, 11, "DS:2bc8 ThisFace", s => s.Dossier.ThisFace, (s, v) => s.Dossier.ThisFace = v)
            .Text(0x2bd3, 9, "DS:2bd3 CodeNameReal", s => s.Dossier.CodeNameReal, (s, v) => s.Dossier.CodeNameReal = v)
            .Text(0x2bdc, 12, "DS:2bdc CodeNameDecoy", s => s.Dossier.CodeNameDecoy, (s, v) => s.Dossier.CodeNameDecoy = v)
            .Text(0x2be8, 3, "DS:2be8 Hundreds", s => s.Dossier.Hundreds, (s, v) => s.Dossier.Hundreds = v)
            .Text(0x2beb, 4, "DS:2beb Thousands", s => s.Dossier.Thousands, (s, v) => s.Dossier.Thousands = v)
            .Text(0x2bef, 5, "DS:2bef ClueTagBasic", s => s.Dossier.ClueTagBasic, (s, v) => s.Dossier.ClueTagBasic = v)
            .Text(0x2bf4, 7, "DS:2bf4 ClueTagCase", s => s.Dossier.ClueTagCase, (s, v) => s.Dossier.ClueTagCase = v)
            .Text(0x2bfb, 7, "DS:2bfb ClueTagCast", s => s.Dossier.ClueTagCast, (s, v) => s.Dossier.ClueTagCast = v)
            .Text(0x2c02, 10, "DS:2c02 CluesFile", s => s.Dossier.CluesFile, (s, v) => s.Dossier.CluesFile = v)
            .Text(0x2c0d, 7, "DS:2c0d Name", s => s.Dossier.Name, (s, v) => s.Dossier.Name = v)
            .Text(0x2c14, 7, "DS:2c14 Rank", s => s.Dossier.Rank, (s, v) => s.Dossier.Rank = v)
            .Text(0x2c1b, 7, "DS:2c1b Org", s => s.Dossier.Org, (s, v) => s.Dossier.Org = v)
            .Text(0x2c22, 8, "DS:2c22 City", s => s.Dossier.City, (s, v) => s.Dossier.City = v)
            .Text(0x2c2a, 10, "DS:2c2a Recruited", s => s.Dossier.Recruited, (s, v) => s.Dossier.Recruited = v)
            .Text(0x2c34, 5, "DS:2c34 By", s => s.Dossier.By, (s, v) => s.Dossier.By = v)
            .Text(0x2c39, 5, "DS:2c39 In", s => s.Dossier.In, (s, v) => s.Dossier.In = v)
            .Text(0x2c3e, 2, "DS:2c3e Period", s => s.Dossier.Period, (s, v) => s.Dossier.Period = v)
            .Text(0x2c40, 8, "DS:2c40 Role", s => s.Dossier.Role, (s, v) => s.Dossier.Role = v)
            .Text(0x2c48, 20, "DS:2c48 RoleNotInvolved", s => s.Dossier.RoleNotInvolved, (s, v) => s.Dossier.RoleNotInvolved = v)
            .Text(0x2c5c, 15, "DS:2c5c RoleUnknown", s => s.Dossier.RoleUnknown, (s, v) => s.Dossier.RoleUnknown = v)
            .Text(0x2c6b, 15, "DS:2c6b UnderArrest", s => s.Dossier.UnderArrest, (s, v) => s.Dossier.UnderArrest = v)
            .Text(0x2c7a, 12, "DS:2c7a Hiding", s => s.Dossier.Hiding, (s, v) => s.Dossier.Hiding = v)
            .Text(0x2c86, 9, "DS:2c86 TurnedNote", s => s.Dossier.TurnedNote, (s, v) => s.Dossier.TurnedNote = v)
            .Text(0x2c90, 2, "DS:2c90 Quote", s => s.Dossier.Quote, (s, v) => s.Dossier.Quote = v)
            .Text(0x2c94, 22, "DS:2c94 NotDecoded", s => s.Dossier.NotDecoded, (s, v) => s.Dossier.NotDecoded = v)
            .Text(0x2caa, 8, "DS:2caa More", s => s.Dossier.More, (s, v) => s.Dossier.More = v)
            .Text(0x2efa, 9, "DS:2efa MessageFile", s => s.Dossier.MessageFile, (s, v) => s.Dossier.MessageFile = v)
            .Text(0x2f03, 9, "DS:2f03 MessageTemplate", s => s.Dossier.MessageTemplate, (s, v) => s.Dossier.MessageTemplate = v)
            .Text(0x2f24, 6, "DS:2f24 Ellipsis", s => s.Dossier.Ellipsis, (s, v) => s.Dossier.Ellipsis = v)
            .Text(0x35ec, 12, "DS:35ec MasterPlan", s => s.Dossier.MasterPlan, (s, v) => s.Dossier.MasterPlan = v)
            .Text(0x35f8, 11, "DS:35f8 Secret", s => s.Dossier.Secret, (s, v) => s.Dossier.Secret = v)
            .Text(0x3603, 9, "DS:3603 Arrested", s => s.Dossier.Arrested, (s, v) => s.Dossier.Arrested = v)
            .Text(0x360c, 10, "DS:360c InHiding", s => s.Dossier.InHiding, (s, v) => s.Dossier.InHiding = v)
            .Text(0x3616, 8, "DS:3616 Turned", s => s.Dossier.Turned, (s, v) => s.Dossier.Turned = v)
            .Text(0x361e, 15, "DS:361e PersonnelFile", s => s.Dossier.PersonnelFile, (s, v) => s.Dossier.PersonnelFile = v)
            .Text(0x362d, 13, "DS:362d ActionTeam", s => s.Dossier.ActionTeam, (s, v) => s.Dossier.ActionTeam = v)
            .Text(0x363a, 3, "DS:363a Comma", s => s.Dossier.Comma, (s, v) => s.Dossier.Comma = v)
            .Text(0x363d, 14, "DS:363d NoActivity", s => s.Dossier.NoActivity, (s, v) => s.Dossier.NoActivity = v)
            .Text(0x2cb3, 12, "DS:2cb3 Received", s => s.Dossier.Words.Received, (s, v) => s.Dossier.Words.Received = v)
            .Text(0x2cbf, 12, "DS:2cbf Sent", s => s.Dossier.Words.Sent, (s, v) => s.Dossier.Words.Sent = v)
            .Text(0x2ccb, 2, "DS:2ccb Space", s => s.Dossier.Words.Space, (s, v) => s.Dossier.Words.Space = v)
            .Text(0x2ccd, 9, "DS:2ccd Someone", s => s.Dossier.Words.Someone, (s, v) => s.Dossier.Words.Someone = v)
            .Text(0x2cd6, 9, "DS:2cd6 OfThe", s => s.Dossier.Words.OfThe, (s, v) => s.Dossier.Words.OfThe = v)
            .Text(0x2cdf, 5, "DS:2cdf In", s => s.Dossier.Words.In, (s, v) => s.Dossier.Words.In = v)
            .Text(0x2ce4, 5, "DS:2ce4 On", s => s.Dossier.Words.On, (s, v) => s.Dossier.Words.On = v)
            .Text(0x2ceb, 2, "DS:2ceb End", s => s.Dossier.Words.End, (s, v) => s.Dossier.Words.End = v)
            .Text(0x2f59, 8, "DS:2f59 AgentPlaceholder", s => s.Dossier.Names.AgentPlaceholder, (s, v) => s.Dossier.Names.AgentPlaceholder = v)
            .Text(0x3469, 2, "DS:3469 Space", s => s.Dossier.Names.Space, (s, v) => s.Dossier.Names.Space = v);
        TextTable(layout, 0x25c2, "DS:25c2 MonthNames", MonthNamesTexts, s => s.Dossier.Words.MonthNames);
        TextTable(layout, 0x2a26, "DS:2a26 RankNames", RankNamesTexts, s => s.Dossier.RankNames);
        TextTable(layout, 0x2b80, "DS:2b80 ClueMethods", ClueMethodsTexts, s => s.Dossier.ClueMethods);
        TextTable(layout, 0x2542, "DS:2542 ActivityPhrases", ActivityPhrasesTexts, s => s.Dossier.ActivityPhrases);
        TextTable(layout, 0x2bbc, "DS:2bbc ClueTokens", ClueTokensTexts, s => s.Dossier.ClueTokens);
        TextTable(layout, 0x356c, "DS:356c Surnames", SurnamesTexts, s => s.Dossier.Names.Surnames);
        TextTable(layout, 0x346c, "DS:346c FirstNames0", FirstNames0Texts, s => s.Dossier.Names.FirstNames[0]);
        TextTable(layout, 0x34ec, "DS:34ec FirstNames1", FirstNames1Texts, s => s.Dossier.Names.FirstNames[1]);
        TextTable(layout, 0x2a46, "DS:2a46 ClueLabels0", ClueLabels0Texts, s => s.Dossier.ClueLabels[0]);
        TextTable(layout, 0x2a56, "DS:2a56 ClueLabels1", ClueLabels1Texts, s => s.Dossier.ClueLabels[1]);
        TextTable(layout, 0x2a66, "DS:2a66 ClueLabels2", ClueLabels2Texts, s => s.Dossier.ClueLabels[2]);
        TextTable(layout, 0x2a76, "DS:2a76 ClueLabels3", ClueLabels3Texts, s => s.Dossier.ClueLabels[3]);
        TextTable(layout, 0x2a86, "DS:2a86 ClueLabels4", ClueLabels4Texts, s => s.Dossier.ClueLabels[4]);
        TextTable(layout, 0x2a96, "DS:2a96 ClueLabels5", ClueLabels5Texts, s => s.Dossier.ClueLabels[5]);
        TextTable(layout, 0x2aa6, "DS:2aa6 ClueLabels6", ClueLabels6Texts, s => s.Dossier.ClueLabels[6]);
        TextTable(layout, 0x2ab6, "DS:2ab6 ClueLabels7", ClueLabels7Texts, s => s.Dossier.ClueLabels[7]);
    }
}
