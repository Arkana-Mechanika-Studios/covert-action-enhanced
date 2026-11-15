using CovertAction.Core.Raid;
using CovertAction.Core.Shared;
using CovertAction.Msc;

namespace CovertAction.Presentation.Raid;

/// <summary>The people of the player's room on the screen, once per frame: their pictures, the shots' flashes, the radar's dots and the hits.</summary>
public sealed partial class BuildingRaid
{
    /// <summary>A person's picture is 15 pixels square, drawn 8 pixels up and left of the person's place.</summary>
    private const int ActorPicture = 0xf, ActorPictureOffset = 8;

    /// <summary>The pictures of a person: 20 per facing (1..8, the first at 0); a guard's 8 further on, a running player's 9.</summary>
    private const int PicturesPerFacing = 0x14, GuardPictures = 8, RunningPictures = 9;

    /// <summary>Picture frames of a person: aiming, shooting (two frames), sneaking; and the player's frame standing still in the portrait.</summary>
    private const int FrameAiming = 4, FrameShooting = 5, FrameSneaking = 7, PortraitStanding = 9;

    /// <summary>Pictures without facing: the puffs of a hit (four, from 0xa0), a guard down (0xa4 facing up, 0xa5 else), the player caught.</summary>
    private const int HitPuff = 0xa0, GuardDownUp = 0xa4, GuardDown = 0xa5, PlayerCaught = 0xa6, PortraitMuzzle = 0x36;

    /// <summary>The player's portrait strip: on the people's page from y 160, 24 pixels a frame, 25 by 40 pixels.</summary>
    private const int PortraitY = 0xa0, PortraitStep = 0x18, PortraitWidth = 0x19, PortraitHeight = 0x28;

    /// <summary><see cref="RaidState.Equip"/>: the Kevlar vest (bit 6: three wounds before the player is caught, else one) and the motion detector (bit 7).</summary>
    private const int KevlarVest = 0x40, MotionDetector = 0x80;

    /// <summary>The range in pixels within which the radar shows guards: with the motion detector, and without.</summary>
    private const int DetectorRange = 0x100, NearRange = 0x60;

    /// <summary>The sound of the pistol's click when it is empty.</summary>
    private const int SoundEmptyPistol = 5;

    /// <summary>
    /// Draws the people of the player's room for this frame: last frame's pictures are wiped; each active person of the
    /// room is drawn by facing and frame (shooting with the flash of the shot, hit, down, sneaking), the player also as
    /// the portrait at the screen's top left with the aim icon, the bar of being knocked out or of a lunge; the dots of
    /// the radar move (guards only near, or with the motion detector, in a bugged room or in the rescue raid); a person
    /// just hit shows four puffs and is then dead (a guard) or wounded (the player). Then the shots fly and a flash
    /// fires its shot.
    /// </summary>
    // orig: TAC 00e8:1b32 draw_actors (src/c/tac/tac_00e8_1b32.c) matched; tests: TacOracleTests
    public void DrawActors()
    {
        for (var c = 0; c < RaidState.Actors; c++)
        {
            if (state.ActorSpriteX[c] != -1)
            {
                RestoreBackground(state.ActorSpriteX[c], state.ActorSpriteY[c], ActorPicture);
            }
        }
        if (state.MuzzleOn)
        {
            RestoreBackground(state.MuzzleX, state.MuzzleY, ActorPicture);
        }
        state.MuzzleOn = false;
        // the player is drawn once before the guards (slot 9 stands for slot 0) and once after them
        for (var p = RaidState.Actors; p >= 0; p--)
        {
            var c = p == RaidState.Actors ? 0 : p;
            var actor = state.Bg[c];
            if (actor.Room != state.InRoom || (actor.Status & ActorFlags.Active) == 0)
            {
                state.ActorSpriteX[c] = -1;
                continue;
            }
            var room = state.Rm[state.InRoom];
            state.ActorSpriteX[c] = C16.Int(actor.Px - (room.X << 4) + state.BaseX - ActorPictureOffset);
            state.ActorSpriteY[c] = C16.Int(actor.Py - (room.Y << 4) + state.BaseY - ActorPictureOffset);
            int x = state.ActorSpriteX[c], y = state.ActorSpriteY[c];
            if (p == RaidState.Actors)
            {
                if (actor.Fire == 0 && WoundsAllowed() >= state.Wounds && state.ThrowTimer == 0)
                {
                    DrawSprite(PlayerPictureBase() + PicturesPerFacing * actor.Dir + StandingFrame(actor) - PicturesPerFacing, x, y);
                }
                continue;
            }
            var frame = actor.Fire == 0 ? StandingFrame(actor) : ShootingFrame(c, actor, x, y);
            if (c == 0)
            {
                frame = DrawPortrait(frame);
            }
            if (c == 0 && WoundsAllowed() < state.Wounds)
            {
                DrawSprite(PlayerCaught, x, y);
            }
            else if (c != 0 && actor.Stun > 0x20)
            {
                DrawSprite(state.Move8Y[actor.Dir] == -1 ? GuardDownUp : GuardDown, x, y);
            }
            else if ((actor.Status & ActorFlags.Alert) == 0)
            {
                DrawSprite((c != 0 ? GuardPictures : PlayerPictureBase()) + PicturesPerFacing * actor.Dir + frame - PicturesPerFacing, x, y);
            }
        }
        DrawRadarDots();
        ShowHits();
        UpdateProjectiles(1);
        if (state.MuzzleOn)
        {
            AimAndFire(state.MuzzleActor, 1);
        }
    }

    /// <summary>Copies the backdrop back over a square of the screen (draw_actors).</summary>
    private void RestoreBackground(int x, int y, int size) => world.Graphics.CopyRect(state.RpBack, x, y, size, size, state.Rp1, x, y);

    /// <summary>The wounds the player can take before he is caught: three with the Kevlar vest, else one (draw_actors).</summary>
    private int WoundsAllowed() => (state.Equip & KevlarVest) != 0 ? 3 : 1;

    /// <summary>The first picture of the player: the running ones while he runs on an odd sneaking step (draw_actors).</summary>
    private int PlayerPictureBase() => ((state.Running ? 1 : 0) & state.SneakSteps & 1) != 0 ? RunningPictures : 0;

    /// <summary>The frame of a person not shooting: the walk's pose, aiming while sighting the player, sneaking (draw_actors).</summary>
    private static int StandingFrame(BadGuy actor)
    {
        int frame = actor.Pose;
        if (actor.Sighting != 0)
        {
            frame = FrameAiming;
        }
        if ((actor.Status & ActorFlags.Sneak) != 0)
        {
            frame = FrameSneaking;
        }
        return frame;
    }

    /// <summary>
    /// The frame of a person shooting, which counts the shot's time down: the two shooting frames in turn, and at one
    /// moment of it the flash of the shot (if no other flashes and the person is not hit or down; the player's empty
    /// pistol only clicks), else the moment waits (draw_actors).
    /// </summary>
    private int ShootingFrame(int c, BadGuy actor, int x, int y)
    {
        if (c == 0)
        {
            state.SneakSteps = 0;
        }
        // C: the old count picks the frame, the new one the moment of the flash
        var frame = (actor.Fire & 2) == 2 ? FrameAiming : FrameShooting;
        actor.Fire--;
        if ((actor.Fire & 3) != 1)
        {
            return frame;
        }
        if (state.MuzzleOn || (actor.Status & ActorFlags.Hit) != 0 || actor.Stun >= 0x63)
        {
            actor.Fire++;
            return frame;
        }
        state.MuzzleActor = (short)c;
        state.MuzzleOn = true;
        state.MuzzleX = C16.Int(6 * state.Move8X[actor.Dir] + x);
        state.MuzzleY = C16.Int(6 * state.Move8Y[actor.Dir] + y);
        if (state.MuzzleActor != 0 || state.Bullets != 0)
        {
            DrawSprite(PicturesPerFacing * actor.Dir - 6, state.MuzzleX, state.MuzzleY);
        }
        if (state.MuzzleActor == 0 && state.Bullets == 0)
        {
            DoSound(SoundEmptyPistol);
        }
        return frame;
    }

    /// <summary>
    /// The player's portrait at the screen's top left (or a frame of his throw, which counts the throw down), the aim
    /// icon, the bar of being knocked out or of a lunge, and the flash of his shot (draw_actors).
    /// </summary>
    /// <returns>The frame of the player's picture (a throw picks its own).</returns>
    private int DrawPortrait(int frame)
    {
        var graphics = world.Graphics;
        if (state.ThrowTimer == 0)
        {
            var portrait = (state.PlayerMoving || frame != 0 ? frame : PortraitStanding) + ((state.Running ? 1 : 0) & state.SneakSteps & 1);
            graphics.CopyRect(state.RpGuys, PortraitStep * portrait, PortraitY, PortraitWidth, PortraitHeight, state.Rp1, 0, 0);
        }
        else
        {
            graphics.CopyRect(state.RpGuys, (0xd - state.ThrowTimer) * PortraitStep, PortraitY, PortraitWidth, PortraitHeight, state.Rp1, 0, 0);
            state.ThrowTimer--;
            frame = 0x13 - state.ThrowTimer;
            state.SneakSteps = 0;
        }
        AimAndFire(0, 0);
        var stun = state.Bg[0].Stun;
        if (stun >= 0x42 || (state.LungeTimer != 0 && state.LungeTimer < 6))
        {
            var bar = GameLibrary.Clamp((stun - 0x40) / 2, state.LungeTimer, 0x26);
            graphics.FillRect(state.Rp1, 1, 0x27 - bar, 0x17, bar, 7);
        }
        if (state.MuzzleOn && state.MuzzleActor == 0 && state.Bullets != 0)
        {
            DrawSprite(PortraitMuzzle, 0xa, 1);
        }
        return frame;
    }

    /// <summary>Moves the dots of the radar to where the people are: guards only when the radar can see them, the player blinking (draw_actors).</summary>
    private void DrawRadarDots()
    {
        for (var c = RaidState.Actors - 1; c >= 0; c--)
        {
            var actor = state.Bg[c];
            if ((actor.Status & ActorFlags.Active) == 0 || actor.Stun >= 999)
            {
                continue;
            }
            RestoreBackground(state.ActorRadarX[c], state.ActorRadarY[c], 2);
            // C: 16-bit products, divided as signed
            state.ActorRadarX[c] = C16.Int(C16.Div(C16.Int(3 * actor.Px), 16) + RadarLeft);
            state.ActorRadarY[c] = C16.Int(C16.Div(C16.Int(3 * actor.Py), 16) + RadarTop);
            int x = state.ActorRadarX[c], y = state.ActorRadarY[c];
            if (c != 0)
            {
                var distance = BuildingRules.ApproxDistance(actor.Px - state.Px, actor.Py - state.Py);
                var range = (state.Equip & MotionDetector) != 0 ? DetectorRange : NearRange;
                if ((range > distance || (state.Rm[actor.Room].Status & RoomStatus.Bugged) != 0 || state.GameMode == RescueMode)
                    && ((actor.Status & ActorFlags.Alert) == 0 || state.Diff == 0))
                {
                    var blinkOff = ((state.CGA ? 1 : 0) & state.Rtime & 1) != 0;
                    Output.FillRectCorners(x, y, x + 1, y + 1, blinkOff ? 0 : state.BuildingColor);
                }
            }
            else if ((state.Rtime & 1) != 0)
            {
                Output.FillRectCorners(x, y, x + 1, y + 1, 0);
            }
        }
    }

    /// <summary>The people of the room just hit: four puffs over each, then a guard is dead and the player wounded (draw_actors).</summary>
    private void ShowHits()
    {
        for (var c = RaidState.Actors - 1; c >= 0; c--)
        {
            var actor = state.Bg[c];
            if (actor.Room != state.InRoom || (actor.Status & ActorFlags.Active) == 0 || (actor.Status & ActorFlags.Hit) == 0)
            {
                continue;
            }
            actor.Status &= ~ActorFlags.Hit;
            int frame;
            if (actor.Fire == 0)
            {
                frame = actor.Pose;
                // C: the pose becomes the sneaking frame, the puffs still show the old one
                if ((actor.Status & ActorFlags.Sneak) != 0)
                {
                    actor.Pose = FrameSneaking;
                }
            }
            else
            {
                frame = ((actor.Fire + 1) & 2) == 2 ? FrameAiming : FrameShooting;
            }
            int x = state.ActorSpriteX[c], y = state.ActorSpriteY[c];
            for (var e = 0; e < 4; e++)
            {
                RestoreBackground(x, y, ActorPicture);
                DrawSprite(e + HitPuff, x, y);
                DrawSprite((c != 0 ? GuardPictures : 0) + PicturesPerFacing * actor.Dir + frame - PicturesPerFacing, x, y);
                Output.WaitVsync(2);
            }
            if (c != 0)
            {
                MarkGuardDead(c);
                state.ActorSpriteX[c] = state.ActorSpriteY[c] = 0;
            }
            else
            {
                state.Wounds++;
                DrawStatusPanel();
            }
        }
    }
}
