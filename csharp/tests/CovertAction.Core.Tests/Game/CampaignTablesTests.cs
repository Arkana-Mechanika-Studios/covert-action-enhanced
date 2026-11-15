using CovertAction.Core.Shared;
using CovertAction.Core.Campaign;

namespace CovertAction.Core.Tests.Game;

/// <summary>
/// GAME's campaign tables as the library sees them: message -1 is read from the variables in front of the message
/// log (GAME DS:7cb4 ... DS:7cc1), as GAME's own message routines read sn[-1] (CampaignTexts).
/// </summary>
public sealed class CampaignTablesTests
{
    [Fact]
    public void MessageMinusOneIsCityFifteensPoliceAndSightingsAndTheHighByteOfTheCountOfPersons()
    {
        var state = new CampaignState { Nperson = 0x0234 };
        state.Ct[CampaignState.WorldSize - 1].Police = 1234;
        state.Ct[CampaignState.WorldSize - 1].Clued = 0x0105;

        var message = ((ICampaignTables)state).MessageBeforeFirst;

        Assert.Equal(1234, message.Time);      // DS:7cb6
        Assert.Equal(0x0105, message.Clues);   // DS:7cba
        Assert.Equal(2, message.Loc2);         // DS:7cc1
    }
}
