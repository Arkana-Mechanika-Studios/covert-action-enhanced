using System.Globalization;
using CovertAction.Core.Chase;
using CovertAction.Core.Shared;
using CovertAction.Presentation.Library;

namespace CovertAction.Presentation.Chase;

// The car offer screen before a tailing chase: four cars, of which the player picks the two tail cars.
public sealed partial class CarChase
{
    private const int OfferSound = 0x27, PickedSound = 1;
    private const int CarsPage = 1;
    private const int Offers = 4;
    private const int AttributeValues = 0x40;

    // the screen: a row of 50 pixels per offered car; the picture on the left (154 pixels wide), the text at x 256
    private const int OfferRowHeight = 0x32, OfferPictureWidth = 0x9a, OfferTextX = 0x100, OfferTextTop = 0xb, OfferLineHeight = 9;
    private const int OfferScreenWidth = 320, OfferScreenHeight = 200;

    /// <summary>The row of cars.pic for the fastest cars, the agile ones, the plain ones and the inconspicuous ones.</summary>
    private const int FastRow = 0, AgileRow = 1, PlainRow = 2, QuietRow = 3;

    /// <summary>The light and dark body colours of a car picture, repainted per level of conspicuousness.</summary>
    private const int BodyLight = 0xb, BodyDark = 3;

    // the highlight of the offer under the cursor: recoloured background, or on the four-colour display a frame
    private const int HighlightOff = 1, HighlightOn = 9;
    private const int FrameTop = 9, FrameRight = 0x13f, FrameHeight = 0x25;
    private const int FrameLit = 0xf, FrameDark = 0, FrameWaiting = 4;
    private const int BlinkFrames = 10;
    private const int MphBase = 40, MphPerClass = 20;

    /// <summary>
    /// The car offer screen (the original's name for it is historical): deals four different cars, all of the same
    /// overall quality, and lets the player pick two of them as the tail cars 1 and 2.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A car is a random attribute word (<see cref="Car.Attr"/>) whose quality, the speed class plus 2 for a tracking
    /// device plus 1 for excellent handling less the conspicuousness, is 3 less the difficulty; speed class 3 is
    /// never offered. Each offer shows a picture of cars.pic (the fastest, agile, plain or inconspicuous kind), painted
    /// in its level of conspicuousness the first time that level comes up (not on the four-colour display), with
    /// "(Tracking)", the top speed, the handling and the conspicuousness beside it.
    /// </para>
    /// <para>
    /// The highlighted offer blinks while no key is pressed; up and down move it, Return or space picks it. The second
    /// pick skips over the first.
    /// </para>
    /// </remarks>
    // orig: CHASE 00df:0842 chase_dashboard (src/c/chase/chase_00df_0842.c) matched; tests: ChaseOracleTests
    public void ChaseDashboard()
    {
        var state = _state;
        DoSound(OfferSound);
        Pictures.LoadPictureSwapped(state.CarsPicName, CarsPage);
        _world.Graphics.CopyRect(state.RpBack, OfferPictureWidth, 0, OfferScreenWidth - OfferPictureWidth, OfferScreenHeight, state.Rp1, OfferPictureWidth, 0);

        var painted = new bool[4];
        var offers = new int[Offers];
        for (var slot = 0; slot < Offers; slot++)
        {
            offers[slot] = DealCar(offers, slot);
            ShowOffer(offers[slot], slot, painted);
        }

        Input.FlushInput();
        _world.Graphics.SetColorMaps(state.CgaPatternCars, state.CgaPatternCars);
        var firstPick = -1;
        for (var pick = 1; pick <= 2; pick++)
        {
            var chosen = PickOffer(firstPick);
            state.Cars[pick].Attr = (short)offers[chosen];
            firstPick = chosen;
            state.MessageText.Text = state.StrCarNo + pick.ToString(CultureInfo.InvariantCulture);
            Output.DrawTextFlag0(state.MessageText.Text, OfferPictureWidth, OfferRowHeight * chosen + FrameTop, White);
        }
        DoSound(PickedSound);
    }

    /// <summary>Draws random attribute words until one has the quality the difficulty asks for and differs from the earlier offers.</summary>
    private int DealCar(int[] offers, int slot)
    {
        int attr, quality;
        do
        {
            attr = GameLibrary.RandomBelow(_state.Random, AttributeValues);
            quality = attr & Car.SpeedClassMask;
            if ((attr & Car.Tracker) != 0)
            {
                quality += 2;
            }
            if ((attr & Car.Handling) != 0)
            {
                quality++;
            }
            quality -= (attr & Car.ConspicuousnessMask) >> Car.ConspicuousnessShift;
            for (var earlier = 0; earlier < slot; earlier++)
            {
                if (offers[earlier] == attr)
                {
                    quality = -1;
                }
            }
        }
        while (quality != 3 - _state.Diff || (attr & Car.SpeedClassMask) == Car.SpeedClassMask);
        return attr;
    }

    /// <summary>Draws one offer: the car's picture and its four lines of text.</summary>
    private void ShowOffer(int attr, int slot, bool[] painted)
    {
        var state = _state;
        var row = PlainRow;
        if ((attr & Car.ConspicuousnessMask) == 0)
        {
            row = QuietRow;
        }
        if ((attr & Car.Handling) != 0 && (attr & Car.SpeedClassMask) != 0)
        {
            row = AgileRow;
        }
        if ((attr & Car.SpeedClassMask) == 2)
        {
            row = FastRow;
        }
        var top = OfferRowHeight * slot;
        _world.Graphics.CopyRect(state.RpBack, 0, OfferRowHeight * row, OfferPictureWidth, OfferRowHeight, state.Rp1, 0, top);
        var level = (attr & Car.ConspicuousnessMask) >> Car.ConspicuousnessShift;
        if (!painted[level] && !state.CGA)
        {
            var colors = state.CarLevelColors[level];
            _world.Graphics.RecolorRect(state.Rp1, 0, top, OfferPictureWidth, OfferRowHeight, BodyLight, colors.ForLight);
            _world.Graphics.RecolorRect(state.Rp1, 0, top, OfferPictureWidth, OfferRowHeight, BodyDark, colors.ForDark);
        }
        painted[level] = true;

        var y = top + OfferTextTop;
        if ((attr & Car.Tracker) != 0)
        {
            Output.DrawTextFlag0(state.StrTracking, OfferTextX, y, White);
        }
        y += OfferLineHeight;
        var topSpeed = (attr & Car.SpeedClassMask) * MphPerClass + MphBase;
        state.MessageText.Text = state.StrEmpty + topSpeed.ToString(CultureInfo.InvariantCulture) + state.StrMph;
        Output.DrawTextFlag0(state.MessageText.Text, OfferTextX, y, White);
        y += OfferLineHeight;
        state.MessageText.Text = (attr & Car.Handling) != 0 ? state.StrExcellent : state.StrFair;
        Output.DrawTextFlag0(state.MessageText.Text, OfferTextX, y, White);
        y += OfferLineHeight;
        state.MessageText.Text = state.StrConspicuousness[level];
        Output.DrawTextFlag0(state.MessageText.Text, OfferTextX, y, White);
    }

    /// <summary>Lets the player choose an offer other than <paramref name="firstPick"/> (-1: none yet).</summary>
    /// <returns>The offer's slot 0..3.</returns>
    private int PickOffer(int firstPick)
    {
        var slot = firstPick == 0 ? 1 : 0;
        MarkOffer(slot, FrameLit, HighlightOff, HighlightOn);
        var picked = false;
        Input.BeginPointer();
        int OfferAt(int x, int y)
        {
            // the offers' rows, as MarkOffer highlights them; the first pick cannot be pointed at
            var offer = PointerHit.Row(x, y, new HitArea(0, 0, OfferScreenWidth, OfferRowHeight), OfferRowHeight, Offers);
            return offer == firstPick ? -1 : offer;
        }
        do
        {
            var pointed = PointerEvent.Nothing;
            while (_world.Input.NoKeyWaiting() && pointed.Action is PointerAction.None or PointerAction.Cancel)
            {
                MarkOffer(slot, FrameDark, HighlightOn, HighlightOff);
                Output.WaitVsync(BlinkFrames);
                MarkOffer(slot, FrameLit, HighlightOff, HighlightOn);
                Output.WaitVsync(BlinkFrames);
                // DIVERGENCE: D-632 with the mouse option on, the pointer on an offer moves the highlight there; a click takes it (no key here is Esc).
                pointed = Input.ReadPointer(OfferAt);
            }
            MarkOffer(slot, FrameWaiting, HighlightOn, HighlightOff);
            var key = pointed.Action switch
            {
                PointerAction.Point => -1,
                PointerAction.Accept when pointed.Choice >= 0 => Keys.Return,
                PointerAction.Accept => -1,
                _ => Input.ReadKey(),
            };
            if (pointed.Choice >= 0)
            {
                slot = pointed.Choice;
            }
            switch (key)
            {
                case Keys.Down:
                    if (slot < Offers - 1)
                    {
                        slot++;
                    }
                    break;
                case Keys.Up:
                    if (slot > 0)
                    {
                        slot--;
                    }
                    break;
                case Keys.Return:
                case Keys.Space:
                    picked = true;
                    break;
            }
            // the first pick cannot be taken again: the cursor jumps over it
            if (slot == firstPick)
            {
                if ((key == Keys.Down && slot < Offers - 1) || slot == 0)
                {
                    slot++;
                }
                else
                {
                    slot--;
                }
            }
            MarkOffer(slot, FrameLit, HighlightOff, HighlightOn);
        }
        while (!picked);
        return slot;
    }

    /// <summary>Changes the highlight of an offer: a frame in a colour on the four-colour display, else a recolouring of its background.</summary>
    private void MarkOffer(int slot, int frameColor, int from, int to)
    {
        if (_state.CGA)
        {
            Output.DrawRectOutline(0, OfferRowHeight * slot + FrameTop, FrameRight, FrameHeight, frameColor);
        }
        else
        {
            _world.Graphics.RecolorRect(_state.Rp1, 0, OfferRowHeight * slot, OfferScreenWidth, OfferRowHeight, from, to);
        }
    }
}
