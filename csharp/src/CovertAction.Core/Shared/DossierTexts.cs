namespace CovertAction.Core.Shared;

/// <summary>
/// The texts of the screens about persons and organisations that several parts show (the personnel file, the
/// suspect dossier) and the buffers those screens write: texts of the part's program, filled in from it (never
/// spelled in the C#).
/// </summary>
public sealed class DossierTexts
{
    /// <summary>The part's name tables.</summary>
    public PersonNameTexts Names { get; } = new();

    /// <summary>The words of a line of the message log.</summary>
    public MessageLogTexts Words { get; } = new();

    /// <summary>The part's buffer of <c>itoa</c> (a day's digits); a part whose buffer is longer than ten bytes hands in its own.</summary>
    public TextBuffer NumberText { get; init; } = new(10);

    /// <summary>The part's buffer of a message's tag ("*MSG0412").</summary>
    public TextBuffer MessageTag { get; } = new(9);

    /// <summary>The template of a message's tag, "*MSG0000".</summary>
    public string MessageTemplate { get; set; } = "";

    /// <summary>The file of the messages' texts, "text.dta".</summary>
    public string MessageFile { get; set; } = "";

    /// <summary>What stands for the words of a message the player does not know: " ... ".</summary>
    public string Ellipsis { get; set; } = "";

    /// <summary>The ranks of an organisation's members, "Recruit" .. (eight).</summary>
    public string[] RankNames { get; } = new string[8];

    // ---- the personnel file ----

    /// <summary>"Personnel File", the title of an organisation's file.</summary>
    public string PersonnelFile { get; set; } = "";

    /// <summary>" Action Team", behind the organisation's name.</summary>
    public string ActionTeam { get; set; } = "";

    /// <summary>"Master Plan", the title of the MasterMind's file.</summary>
    public string MasterPlan { get; set; } = "";

    /// <summary>"--SECRET--", five times across the Master Plan.</summary>
    public string Secret { get; set; } = "";

    /// <summary>"ARRESTED", over a photo.</summary>
    public string Arrested { get; set; } = "";

    /// <summary>"IN HIDING", over a photo.</summary>
    public string InHiding { get; set; } = "";

    /// <summary>" TURNED", over a photo.</summary>
    public string Turned { get; set; } = "";

    /// <summary>", " between a member's name and city.</summary>
    public string Comma { get; set; } = "";

    /// <summary>"(No activity)", for an organisation without members in the case.</summary>
    public string NoActivity { get; set; } = "";

    // ---- the suspect dossier ----

    /// <summary>"Name:" and its tab character.</summary>
    public string Name { get; set; } = "";

    /// <summary>"Rank:" and its tab character.</summary>
    public string Rank { get; set; } = "";

    /// <summary>"Org: " and its tab character.</summary>
    public string Org { get; set; } = "";

    /// <summary>"City: " and its tab character.</summary>
    public string City { get; set; } = "";

    /// <summary>"Recruited".</summary>
    public string Recruited { get; set; } = "";

    /// <summary>" by ".</summary>
    public string By { get; set; } = "";

    /// <summary>" in ".</summary>
    public string In { get; set; } = "";

    /// <summary>".".</summary>
    public string Period { get; set; } = "";

    /// <summary>"Role: " and its tab character.</summary>
    public string Role { get; set; } = "";

    /// <summary>"Role: NOT INVOLVED", with the tab character.</summary>
    public string RoleNotInvolved { get; set; } = "";

    /// <summary>"Role: unknown", with the tab character.</summary>
    public string RoleUnknown { get; set; } = "";

    /// <summary>"(Under Arrest)".</summary>
    public string UnderArrest { get; set; } = "";

    /// <summary>"(In Hiding)".</summary>
    public string Hiding { get; set; } = "";

    /// <summary>"(Turned)".</summary>
    public string TurnedNote { get; set; } = "";

    /// <summary>"(message not decoded)".</summary>
    public string NotDecoded { get; set; } = "";

    /// <summary>"...more", when the page is full.</summary>
    public string More { get; set; } = "";

    /// <summary>The quote before and after a decoded message's text: "'".</summary>
    public string Quote { get; set; } = "";

    // ---- the chronology ----

    /// <summary>"Chronology", the title of the crime chronology.</summary>
    public string ChronologyTitle { get; set; } = "";

    /// <summary>"News", the title of the news.</summary>
    public string NewsTitle { get; set; } = "";

    /// <summary>The title of a further page of the chronology.</summary>
    public string ChronologyHeader { get; set; } = "";

    /// <summary>The title of a further page of the news.</summary>
    public string NewsHeader { get; set; } = "";

    /// <summary>" sent msg to " between two cast members.</summary>
    public string SentMsgTo { get; set; } = "";

    /// <summary>" met with " between two cast members.</summary>
    public string MetWith { get; set; } = "";

    /// <summary>The quote before a decoded message's text.</summary>
    public string NewsQuoteOpen { get; set; } = "";

    /// <summary>The quote after it.</summary>
    public string NewsQuoteClose { get; set; } = "";

    /// <summary>"Meeting Noted", for a meeting of which only some words are known.</summary>
    public string MeetingNoted { get; set; } = "";

    /// <summary>"...more", when the chronology's page is full.</summary>
    public string ChronologyMore { get; set; } = "";

    /// <summary>"none", for an empty chronology.</summary>
    public string NoEntries { get; set; } = "";

    // ---- the clue sentences ----

    /// <summary>The labels of clue items, eight per kind of item (sixteen kinds): "Ford Escort #", "Walther PPK #" ...</summary>
    public string[][] ClueLabels { get; } = [.. Enumerable.Range(0, 16).Select(_ => new string[8])];

    /// <summary>What follows a sum of money in hundreds: "00".</summary>
    public string Hundreds { get; set; } = "";

    /// <summary>What follows a sum of money in thousands: "000".</summary>
    public string Thousands { get; set; } = "";

    /// <summary>The file of the clue sentences, "clues.txt".</summary>
    public string CluesFile { get; set; } = "";

    /// <summary>The placeholders of a clue sentence ($KEY ...; five).</summary>
    public string[] ClueTokens { get; } = new string[5];

    /// <summary>The template of a clue's tag by item and subject kind, "*C00".</summary>
    public string ClueTagBasic { get; set; } = "";

    /// <summary>The template of a cast member's clue tag of the case, "*C0000".</summary>
    public string ClueTagCase { get; set; } = "";

    /// <summary>The template of a decoy's clue tag, "*C0000".</summary>
    public string ClueTagCast { get; set; } = "";

    /// <summary>The part's variable: the source of the last clue sentence read (its first character's low four bits).</summary>
    public short ClueMethod { get; set; }

    // ---- the clue report ----

    /// <summary>The template of a decoy's clue item tag, "*C0000".</summary>
    public string ClueTag { get; set; } = "";

    /// <summary>"Clue", the report's title.</summary>
    public string ClueTitle { get; set; } = "";

    /// <summary>"Source: " and its tab character.</summary>
    public string Source { get; set; } = "";

    /// <summary>"/" between organisation and city.</summary>
    public string Slash { get; set; } = "";

    /// <summary>A line end.</summary>
    public string ReportNewline { get; set; } = "";

    /// <summary>"Method: ", with a line end and a tab character before it.</summary>
    public string MethodLabel { get; set; } = "";

    /// <summary>The sources of clues: "Clandestine Photo", "Electronic WireTap" ... (seven).</summary>
    public string[] ClueMethods { get; } = new string[7];

    /// <summary>"Related Clues:" and its tab character.</summary>
    public string RelatedClues { get; set; } = "";

    /// <summary>"...none", when there are no related clues.</summary>
    public string NoneRelated { get; set; } = "";

    /// <summary>"this face.", the subject of a photo clue.</summary>
    public string ThisFace { get; set; } = "";

    /// <summary>The code name of a real cast member as a clue's subject.</summary>
    public string CodeNameReal { get; set; } = "";

    /// <summary>The code name of a decoy as a clue's subject.</summary>
    public string CodeNameDecoy { get; set; } = "";

    /// <summary>The phrases that join a clue's item and subject, five per kind of item.</summary>
    public string[] ActivityPhrases { get; } = new string[40];

    /// <summary>The full stop of a clue's sentence.</summary>
    public string ActivityPeriod { get; set; } = "";

    /// <summary>The part's variable: 1 when the intelligence just gathered was shown as a coded message (the clue's source is then a wiretap).</summary>
    public short CodedFlag { get; set; }

    // ---- the new facts about a person ----

    /// <summary>The part's variable <c>paused</c>, cleared while new facts are told.</summary>
    public short Paused { get; set; }

    /// <summary>Around the name: "... has been identified as a participant in the plot".</summary>
    public string PlotPrefix { get; set; } = "";

    /// <summary>See <see cref="PlotPrefix"/>.</summary>
    public string PlotSuffix { get; set; } = "";

    /// <summary>Before the code name: "... has been positively identified as ...".</summary>
    public string IdentifiedPrefix { get; set; } = "";

    /// <summary>Between the code name and the real name.</summary>
    public string IdentifiedMiddle { get; set; } = "";

    /// <summary>After the real name.</summary>
    public string IdentifiedSuffix { get; set; } = "";

    /// <summary>Around the name: "We have acquired a photograph of ...".</summary>
    public string PhotoPrefix { get; set; } = "";

    /// <summary>See <see cref="PhotoPrefix"/>.</summary>
    public string PhotoSuffix { get; set; } = "";

    /// <summary>Before the name: "... has been spotted in" the city.</summary>
    public string SpottedPrefix { get; set; } = "";

    /// <summary>Between the name and the city.</summary>
    public string SpottedMiddle { get; set; } = "";

    /// <summary>After the city.</summary>
    public string SpottedSuffix { get; set; } = "";

    /// <summary>Before the name: "... has been identified as a member of the" organisation.</summary>
    public string MemberPrefix { get; set; } = "";

    /// <summary>Between the name and the organisation.</summary>
    public string MemberMiddle { get; set; } = "";

    /// <summary>After the organisation.</summary>
    public string MemberSuffix { get; set; } = "";

    /// <summary>Around the name: "We have obtained additional information concerning ...".</summary>
    public string InformationPrefix { get; set; } = "";

    /// <summary>See <see cref="InformationPrefix"/>.</summary>
    public string InformationSuffix { get; set; } = "";

    /// <summary>Around the name: "We have determined the rank of ...".</summary>
    public string RankPrefix { get; set; } = "";

    /// <summary>See <see cref="RankPrefix"/>.</summary>
    public string RankSuffix { get; set; } = "";

    /// <summary>Around the name: "We found recruiting information about ..." (where the person was recruited).</summary>
    public string RecruitingPrefix { get; set; } = "";

    /// <summary>See <see cref="RecruitingPrefix"/>.</summary>
    public string RecruitingSuffix { get; set; } = "";

    /// <summary>Around the name: "We found recruiting information about ..." (who recruited the person).</summary>
    public string RecruiterPrefix { get; set; } = "";

    /// <summary>See <see cref="RecruiterPrefix"/>.</summary>
    public string RecruiterSuffix { get; set; } = "";
}
