using CovertAction.Core.Shared;

namespace CovertAction.Persistence.Layout;

/// <summary>
/// Where a program keeps the texts of the library's screens about a case (<see cref="DossierTexts"/>): the part of a
/// data segment's layout that goes with them. One table of places per program.
/// </summary>
/// <remarks>
/// So far the campaign program's texts of the chronology, the first of these screens it shows through the library.
/// The places of the other programs and texts are known from the library's oracle tests
/// (LibraryTextOracleTests.CampaignAddresses, LibraryModuleOracleTests); they join here as each program is wired.
/// </remarks>
public static class DossierLayout
{
    /// <summary>A text of the program: where it starts and how many bytes it has with its end mark.</summary>
    private readonly record struct TextAt(int Address, int Length);

    /// <summary>Adds the campaign program's (GAME.EXE) texts of the chronology to its layout.</summary>
    /// <typeparam name="TState">The program's state.</typeparam>
    /// <param name="layout">The program's layout.</param>
    /// <param name="texts">The state's texts of the library's screens.</param>
    // orig: GAME DS:3895 "Chronology" ... DS:38e8 "none", the texts of show_chronology
    public static StateLayout<TState> AddGame<TState>(StateLayout<TState> layout, Func<TState, DossierTexts> texts)
    {
        void Text(TextAt at, string name, Func<DossierTexts, string> get, Action<DossierTexts, string> set) =>
            layout.Text(at.Address, at.Length, $"DS:{at.Address:x4} {name}", s => get(texts(s)), (s, v) => set(texts(s), v));

        Text(new(0x3895, 11), "chronology title", t => t.ChronologyTitle, (t, v) => t.ChronologyTitle = v);
        Text(new(0x38a0, 5), "news title", t => t.NewsTitle, (t, v) => t.NewsTitle = v);
        Text(new(0x38a5, 14), "\" sent msg to \"", t => t.SentMsgTo, (t, v) => t.SentMsgTo = v);
        Text(new(0x38b3, 11), "\" met with \"", t => t.MetWith, (t, v) => t.MetWith = v);
        Text(new(0x38be, 2), "news quote, opening", t => t.NewsQuoteOpen, (t, v) => t.NewsQuoteOpen = v);
        Text(new(0x38c0, 2), "news quote, closing", t => t.NewsQuoteClose, (t, v) => t.NewsQuoteClose = v);
        Text(new(0x38c2, 14), "\"Meeting Noted\"", t => t.MeetingNoted, (t, v) => t.MeetingNoted = v);
        Text(new(0x38d0, 8), "\"...more\"", t => t.ChronologyMore, (t, v) => t.ChronologyMore = v);
        Text(new(0x38d8, 11), "chronology title of a further page", t => t.ChronologyHeader, (t, v) => t.ChronologyHeader = v);
        Text(new(0x38e3, 5), "news title of a further page", t => t.NewsHeader, (t, v) => t.NewsHeader = v);
        Text(new(0x38e8, 5), "\"none\"", t => t.NoEntries, (t, v) => t.NoEntries = v);
        return layout;
    }
}
