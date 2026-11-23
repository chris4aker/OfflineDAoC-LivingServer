using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>
/// The lord fight's formation (1.2.2, owner: "add the rule about standing behind melee"). Inside a keep or tower lord's
/// reach (<see cref="RvrLordReach"/>: 350 / 250, his damage radius 400 / 300 less 50) the attackers stood wherever their
/// walk ended: every gamebot targeting a lord walked to within 280 of him (BotBrain) and fought from there, casters,
/// archers and healers in one pile with the melee on the line they came up by, where the lord's and his guards' area
/// spells caught them all.
/// <para>Now the tank and the melee fight him at melee range, in front (they come up from the raid's side); casters and
/// archers stand behind them on the raid's side (toward the door of his keep or tower, the way the raid came in) in the
/// <see cref="Band"/> inside his reach, spread over an arc of <see cref="Slots"/> spots in two rows; healers
/// stand behind the casters, still inside his reach (the storm's and the siege's strike rules count every attacker alike,
/// and a healer's own offensive cast then still counts) and well within healing range of the melee. Pets fight beside
/// their owners as before (the lord counts a pet's hits only while its owner stands within his radius too, which every
/// band keeps). Nothing of the lord changes: his radii and stats are upstream's.</para>
/// <para>The spot is picked here; the caller snaps it to the navmesh on the lord's floor with a straight floor line to
/// him and takes the next candidate when it has none (<see cref="Candidates"/>), and with none at all keeps the old
/// behaviour. Pure.</para>
/// </summary>
public static class RvrLordFormation
{
    public enum Role { Tank, Melee, Ranged, Healer }

    /// <summary>Spots on the arc's nearer row; the farther row has one fewer, each between two of the nearer row's.</summary>
    public const int Columns = 5;
    /// <summary>Rows of the arc, the nearer first.</summary>
    public const int Rows = 2;
    /// <summary>Spots on the arc in all (5 + 4).</summary>
    public const int Slots = Columns + Columns - 1;
    /// <summary>Half the arc's angle in a keep's lord room (larger) and a tower's top (smaller), in degrees.</summary>
    public const float KeepArcHalfDegrees = 60f, TowerArcHalfDegrees = 45f;
    /// <summary>A bot within this (flat) of its spot is at it.</summary>
    public const float Tolerance = 60f;
    /// <summary>The spot is re-picked once the lord stands this far (flat) from where it was picked.</summary>
    public const float Reanchor = 100f;
    /// <summary>Melee and tanks close to within this of the lord before their own melee chase takes over (the old rule).</summary>
    public const float MeleeHold = 280f;
    /// <summary>A candidate is moved this much nearer the lord each step when the floor refuses it.</summary>
    public const float ShrinkStep = 30f;

    /// <summary>The bot's place in the lord's fight by its class. Healers are the pure healing classes (and the shaman and
    /// bard, who heal from the back); casters (list casters) and archers are ranged; the tank classes tank; everyone else,
    /// the melee hybrids that heal (friar, warden, paladin) included, fights in melee.</summary>
    public static Role RoleOf(eCharacterClass characterClass, bool listCaster)
    {
        if (characterClass is eCharacterClass.Cleric or eCharacterClass.Druid or eCharacterClass.Healer or
            eCharacterClass.Shaman or eCharacterClass.Bard)
            return Role.Healer;
        if (BotPartyRoles.For(characterClass) == BotPartyRole.Tank) return Role.Tank;
        if (listCaster || characterClass is eCharacterClass.Hunter or eCharacterClass.Ranger or eCharacterClass.Scout or
            eCharacterClass.Animist)
            return Role.Ranged;
        return Role.Melee;
    }

    /// <summary>Whether the role stands back on a formation spot (casters, archers, healers) rather than in melee.</summary>
    public static bool StandsBack(Role role) => role is Role.Ranged or Role.Healer;

    /// <summary>
    /// The band (distance from the lord) the role stands in: casters and archers 230-300 in a keep, 160-205 in a tower;
    /// healers behind them, 305-345 / 210-245. Every band lies inside <see cref="RvrLordReach.For"/> (350 / 250), so each
    /// bot's hits, and its pet's, count. Melee and tanks have none (0, 0): they fight at melee range.
    /// </summary>
    public static (float Min, float Max) Band(Role role, bool keep) => role switch
    {
        Role.Ranged => keep ? (230f, 300f) : (160f, 205f),
        Role.Healer => keep ? (305f, 345f) : (210f, 245f),
        _ => (0f, 0f)
    };

    /// <summary>How near the lord a bot of the role closes before it fights: a standing-back role from anywhere inside his
    /// reach (it then takes its spot), melee and tanks <see cref="MeleeHold"/> (their melee chase does the rest).</summary>
    public static float HoldRadius(Role role, bool keep) => StandsBack(role) ? RvrLordReach.For(keep) : MeleeHold;

    /// <summary>
    /// The raid's side of the lord as a unit vector from him (flat): toward the door of his keep or tower
    /// (<paramref name="door"/>, the way in), else toward the bot itself, else east.
    /// </summary>
    public static Vector2 Approach(Vector2 lord, Vector2? door, Vector2 bot)
    {
        if (door is { } d && Vector2.DistanceSquared(d, lord) > 1f) return Vector2.Normalize(d - lord);
        if (Vector2.DistanceSquared(bot, lord) > 1f) return Vector2.Normalize(bot - lord);
        return new Vector2(1f, 0f);
    }

    /// <summary>The bot's slot on the arc by a stable id: its column and row (the nearer row's five first, then the
    /// farther row's four).</summary>
    public static (int Column, int Row) Slot(long id)
    {
        int n = (int)Math.Abs(id % Slots);
        return n < Columns ? (n, 0) : (n - Columns, 1);
    }

    /// <summary>The slot's angle off the approach, in degrees: the nearer row's columns spread evenly over the arc, the
    /// farther row's each half a step on, between two of them.</summary>
    public static float SlotAngle(int column, int row, bool keep)
    {
        float half = keep ? KeepArcHalfDegrees : TowerArcHalfDegrees;
        float step = 2 * half / (Columns - 1);
        float angle = -half + column * step + (row % 2 == 1 ? step / 2 : 0f);
        return Math.Clamp(angle, -half, half);
    }

    /// <summary>The slot's distance from the lord: the nearer row a quarter into the band, the farther three quarters.</summary>
    public static float SlotDistance(Role role, bool keep, int row)
    {
        var (min, max) = Band(role, keep);
        return min + (max - min) * (row % 2 == 0 ? 0.25f : 0.75f);
    }

    /// <summary>The bot's formation spot (flat) and its distance from the lord; null for melee and tanks.</summary>
    public static (Vector2 Spot, float Distance)? Spot(Vector2 lord, Vector2 approach, Role role, bool keep, long id)
    {
        if (!StandsBack(role)) return null;
        var (column, row) = Slot(id);
        float distance = SlotDistance(role, keep, row);
        return (At(lord, approach, SlotAngle(column, row, keep), distance), distance);
    }

    /// <summary>
    /// The spots to try on the floor, best first: the bot's own spot; then the same angle nearer the lord in
    /// <see cref="ShrinkStep"/>s down to the band's minimum; then the same at angles nearer the approach's centre line (a
    /// narrow lord room); last the centre line itself from the band's maximum down to its minimum. Empty for melee.
    /// </summary>
    public static IReadOnlyList<(Vector2 Spot, float Distance)> Candidates(Vector2 lord, Vector2 approach, Role role, bool keep, long id)
    {
        var list = new List<(Vector2 Spot, float Distance)>();
        if (!StandsBack(role)) return list;
        var (min, max) = Band(role, keep);
        var (column, row) = Slot(id);
        float angle = SlotAngle(column, row, keep);
        float start = SlotDistance(role, keep, row);
        for (float a = angle; ; a = Math.Abs(a) <= 15f ? 0f : a - Math.Sign(a) * 15f)
        {
            for (float d = start; d >= min - 0.01f; d -= ShrinkStep)
                list.Add((At(lord, approach, a, d), d));
            if (a == 0f) break;
        }
        for (float d = max; d >= min - 0.01f; d -= ShrinkStep)
            list.Add((At(lord, approach, 0f, d), d));
        return list;
    }

    /// <summary>The point <paramref name="distance"/> from the lord at <paramref name="degrees"/> off the approach.</summary>
    public static Vector2 At(Vector2 lord, Vector2 approach, float degrees, float distance)
    {
        double r = degrees * Math.PI / 180.0;
        float cos = (float)Math.Cos(r), sin = (float)Math.Sin(r);
        Vector2 dir = new(approach.X * cos - approach.Y * sin, approach.X * sin + approach.Y * cos);
        return lord + dir * distance;
    }

    /// <summary>What a standing-back bot does this turn: walk to its spot when it is off it (beyond
    /// <see cref="Tolerance"/>) and free to (not casting, stunned or mesmerized); else fight from where it stands.</summary>
    public static bool WalksToSpot(float distanceToSpot, bool casting, bool cannotMove) =>
        distanceToSpot > Tolerance && !casting && !cannotMove;

    /// <summary>Whether the spot is picked anew: none yet, another lord, or he has moved <see cref="Reanchor"/> from where it
    /// was picked.</summary>
    public static bool NeedsSpot(bool hasSpot, bool sameLord, float lordMovedFlat) =>
        !hasSpot || !sameLord || lordMovedFlat > Reanchor;

    /// <summary>A tank holds the lord's attention: it taunts in a group (as before) and against a keep or tower lord alone
    /// too, so the lord stays on it in front instead of turning on the casters behind it.</summary>
    public static bool TankTaunts(bool isTank, int groupSize, bool targetIsLord) =>
        isTank && (groupSize > 1 || targetIsLord);
}
