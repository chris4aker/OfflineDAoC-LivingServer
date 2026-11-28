using System;
using System.Collections.Generic;
using System.Numerics;

namespace DOL.GS;

/// <summary>What a gamebot bomber does about a mezzed enemy pile (#531 step 2f).</summary>
public enum RvrBombAction : byte
{
    /// <summary>Today's rules: bomb only enemies already around it, and never walk in among enemy players.</summary>
    None,
    /// <summary>Walk in on the mezzed pile and bomb it now.</summary>
    Bomb,
    /// <summary>Walk in on the mezzed pile and wait there until the mez is about to wear off.</summary>
    Wait,
}

/// <summary>
/// PBAoE bombers on a mezzed pile (#531 step 2f; owner answer 53A, 2026-11-27: "groups with 2 or more bombers bomb at
/// once; a group with one bomber walks in and waits"). Period: Hibernia ran "2 PBAoE casters (Eldritch/Enchanter/
/// Mentalist)" and caster groups mezzed, then bombed (reference.md 3.6); but "any damage breaks [a mez], so AE damage on a
/// mezzed group is the classic mistake" (3.3). So: once the group's area mez has landed (two or more enemies mezzed in one
/// bomb's reach), a group with two or more bombers walks them in and bombs the pile at once (the first to arrive holds
/// a moment for the others, and a bomber whose pile wakes before it gets there gives the walk up); a group with one bomber walks it in and waits beside the
/// pile, and bombs when the first mez there is about to wear off. Without a mezzed pile nothing changes. Pure. Our design values (no source measured them): the 2 s wake window,
/// the 1,500 walk (a caster now stands about that far back, step 2e) and the timers.
/// </summary>
public static class RvrBombPlan
{
    /// <summary>The owner's line between bombing at once and waiting (53A).</summary>
    public const int MinimumBombers = 2;
    /// <summary>A mezzed pile: this many mezzed enemies in one bomb's reach (as RvrOpener.LandedCount).</summary>
    public const int MinimumMezzed = 2;
    /// <summary>A lone bomber bombs once the shortest mez in its reach has this little left.</summary>
    public const long WakeWindowMilliseconds = 2_000;
    /// <summary>The farthest a bomber walks in for a pile.</summary>
    public const float WalkLimit = 1_500f;
    /// <summary>A walk in that has not arrived by then is given up.</summary>
    public const long WalkMilliseconds = 9_000;
    /// <summary>A lone bomber waits this much past the moment it should have bombed before it gives up.</summary>
    public const long WaitMarginMilliseconds = 1_000;
    /// <summary>The longest a lone bomber waits beside the pile (past a 60 s mez on an unresisted target).</summary>
    public const long MaxWaitMilliseconds = 65_000;
    /// <summary>Two or more bombers: the first to arrive holds this long at most for the others still walking in (about
    /// one bomb's cast time), so the bombs of bombers already near land together.</summary>
    public const long SyncHoldMilliseconds = 3_000;

    /// <summary>
    /// How long a lone bomber waits beside the pile, from the shortest mez left in its reach when it starts waiting: until
    /// that mez is inside the wake window, plus <see cref="WaitMarginMilliseconds"/>, at least the margin and at most
    /// <see cref="MaxWaitMilliseconds"/>. Pure.
    /// </summary>
    public static long WaitFor(long shortestMezLeftMs) =>
        Math.Clamp(shortestMezLeftMs - WakeWindowMilliseconds + WaitMarginMilliseconds, WaitMarginMilliseconds, MaxWaitMilliseconds);

    /// <summary>
    /// Two or more bombers bomb at once (53A): one that has arrived holds while <paramref name="matesComing"/> of the
    /// group's other bombers are not yet ready at the pile (within the walk limit, neither holding nor bombing), for at most
    /// <see cref="SyncHoldMilliseconds"/> since it arrived (<paramref name="heldMs"/>). Pure.
    /// </summary>
    public static bool Hold(int matesComing, long heldMs) => matesComing > 0 && heldMs < SyncHoldMilliseconds;

    /// <summary>A pile: the mezzed enemy it is centred on, and how many mezzed and free enemies a bomb there reaches.</summary>
    public readonly record struct Pile(RvrCombatant Centre, int Mezzed, int Free);

    /// <summary>
    /// The decision. <paramref name="bombers"/>: the group's bombers (this one included); <paramref name="mezzed"/> and
    /// <paramref name="free"/>: the enemies the bomb would reach; <paramref name="shortestMezLeftMs"/>: the least mez time
    /// left among them. None without a bomber, without two targets or without a mezzed pile. Pure.
    /// </summary>
    public static RvrBombAction Decide(int bombers, int mezzed, int free, long shortestMezLeftMs)
    {
        if (bombers <= 0 || mezzed + free < BotAoePolicy.MinimumTargets || mezzed < MinimumMezzed)
            return RvrBombAction.None;
        if (bombers >= MinimumBombers)
            return RvrBombAction.Bomb;
        return shortestMezLeftMs <= WakeWindowMilliseconds ? RvrBombAction.Bomb : RvrBombAction.Wait;
    }

    /// <summary>How many of <paramref name="enemies"/> a bomb of <paramref name="radius"/> at <paramref name="at"/> reaches
    /// (inside the edge margin), mezzed and free. Pets count: a bomb hits them too. Pure.</summary>
    public static (int Mezzed, int Free) Reach(IReadOnlyList<RvrCombatant> enemies, Vector2 at, int radius)
    {
        float reach = Math.Max(1, radius - BotAoePolicy.EdgeMargin);
        int mezzed = 0, free = 0;
        foreach (RvrCombatant enemy in enemies)
        {
            if (enemy?.Living == null || Vector2.Distance(at, new Vector2(enemy.X, enemy.Y)) > reach)
                continue;
            if ((enemy.Control & RvrControlState.Mezzed) != 0)
                mezzed++;
            else
                free++;
        }
        return (mezzed, free);
    }

    /// <summary>
    /// Whether the pile centred at <paramref name="centre"/> is still asleep: a bomb of <paramref name="radius"/> there still
    /// reaches <see cref="MinimumMezzed"/> mezzed enemies. A bomber walking in recounts this on every tick and gives the walk
    /// up once the pile wakes (another hit broke the mez, or the first bomb landed before it got there), so it does not
    /// walk on into awake enemies. Pure.
    /// </summary>
    public static bool StillAsleep(IReadOnlyList<RvrCombatant> enemies, Vector2 centre, int radius) =>
        enemies != null && Reach(enemies, centre, radius).Mezzed >= MinimumMezzed;

    /// <summary>
    /// The pile to walk in on: a mezzed enemy within <see cref="WalkLimit"/> of <paramref name="from"/> whose bomb reaches
    /// the most mezzed enemies (then the most in all, then the nearer, then the lower key). Default (no centre) when none
    /// reaches <see cref="MinimumMezzed"/>. Pure.
    /// </summary>
    public static Pile Choose(IReadOnlyList<RvrCombatant> enemies, Vector2 from, int radius)
    {
        Pile best = default;
        float bestDistance = float.MaxValue;
        if (enemies == null)
            return best;
        foreach (RvrCombatant centre in enemies)
        {
            if (centre?.Living == null || centre.IsPet || (centre.Control & RvrControlState.Mezzed) == 0)
                continue;
            Vector2 at = new(centre.X, centre.Y);
            float distance = Vector2.Distance(from, at);
            if (distance > WalkLimit)
                continue;
            (int mezzed, int free) = Reach(enemies, at, radius);
            if (mezzed < MinimumMezzed)
                continue;
            bool better = best.Centre == null || mezzed > best.Mezzed ||
                          mezzed == best.Mezzed && (mezzed + free > best.Mezzed + best.Free ||
                                                    mezzed + free == best.Mezzed + best.Free &&
                                                    (distance < bestDistance || distance == bestDistance && centre.Key < best.Centre.Key));
            if (better)
            {
                best = new Pile(centre, mezzed, free);
                bestDistance = distance;
            }
        }
        return best;
    }
}
