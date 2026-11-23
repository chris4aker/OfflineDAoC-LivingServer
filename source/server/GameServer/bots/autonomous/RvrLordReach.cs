using DOL.GS.Keeps;

namespace DOL.GS;

/// <summary>
/// How near a keep or tower lord a bot strikes him from (#416 follow-up). A lord takes no damage from an attacker (or its
/// pet) farther than <see cref="GuardLord.KeepDamageRadius"/> (400) in a keep or <see cref="GuardLord.TowerDamageRadius"/>
/// (300) in a tower (GuardLord.TakeDamage, "immune to damage from this range"). Frontier siege attackers took an exposed lord
/// from wherever they saw him (FindRvrTarget, up to 5,000 with line of sight; ranged bots need no walk to him) and stopped
/// there to cast and shoot, for nothing. The reach is <see cref="Margin"/> inside the radius, for the bot's last step and
/// the 3D distance: 350 in a keep, 250 in a tower. Pure.
/// </summary>
public static class RvrLordReach
{
    public const int Margin = 50;

    /// <summary>The strike reach at a keep's lord (<paramref name="keep"/>) or a tower's.</summary>
    public static float For(bool keep) => GuardLord.DamageRadiusFor(keep) - Margin;

    /// <summary>Whether a bot this far from the lord strikes him from where it stands.</summary>
    public static bool InReach(bool keep, float distance) => distance <= For(keep);
}
