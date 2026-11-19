using System;

namespace DOL.GS.Keeps
{
	/// <summary>
	/// The guards and lord of a battleground's own keep are levelled for that battleground (owner, 2026-11-19: "I want the
	/// guards and lord levels appropriate for their battleground"). Owner's PC, build c81eb5d: Caer Caledon's lord was 57 and
	/// its guards up to 48 in Caledonia (levels 30-35); the gate clear stalled twice and the keep was never taken.
	/// <para>The rule is the shipped world's own: a battleground keep's base level is its bracket's top level plus
	/// <see cref="BaseLevelAboveBracketTop"/>. Dun Abermenai (15-19) has 21, Thidranki Faste (20-24) 26 and Dun Murdaigean
	/// (25-29) 31; only Caer Caledon (30-35) had 46, eleven above its bracket instead of two. The guard and lord formulas
	/// are DOL's, unchanged (<see cref="AbstractGameKeep.GetBaseLevel"/>, <see cref="AbstractGameKeep.SetGuardLevel"/>), so
	/// at keep level 1 the guards are the bracket top + 4 and the lord the bracket top + 9 (base below 30) or + 11.</para>
	/// <para>A battleground keep stays at level 1 (<see cref="AbstractGameKeep.BattlegroundKeepLevel"/>), but a claim with
	/// allow_bg_claim, a keep upgrade, a bigger keep_guard_level_multiplier, keep balancing or a GM's /keep command could still
	/// raise its guards: whatever does, a battleground keep's guards and lord never go above their level-1 values
	/// (<see cref="Cap"/>). Frontier keeps, towers, portal keeps and the other old battleground regions (Cathal Valley,
	/// Killaloe, Braemar, Molvik, which have no bracket in <see cref="BattlegroundBrackets"/>) are not touched.</para>
	/// </summary>
	public static class BattlegroundKeepLevels
	{
		/// <summary>How far above its bracket's top a battleground keep's base level sits (21/19, 26/24, 31/29 shipped).</summary>
		public const int BaseLevelAboveBracketTop = 2;

		/// <summary>DOL's keep guard multiplier (the shipped keep_guard_level_multiplier), used for the cap.</summary>
		public const double CapMultiplier = 1.6;

		public const string RepairLogTag = "BG_KEEP_BASE_LEVEL_REPAIRED";

		/// <summary>The base level a battleground keep of this bracket has. Pure.</summary>
		public static byte BaseLevelFor(BattlegroundBrackets.Bracket bracket) =>
			(byte)Math.Min(49, bracket.MaxLevel + BaseLevelAboveBracketTop);

		/// <summary>
		/// The bracket of a battleground's own keep (base level below 50, not a portal keep, in one of the four battleground
		/// regions), or null for every other keep. Decided by the database row, so it holds before and after a repair.
		/// </summary>
		public static BattlegroundBrackets.Bracket BracketOf(AbstractGameKeep keep) =>
			keep != null && keep.IsBattlegroundKeep && BattlegroundBrackets.IsBattlegroundRegion(keep.Region)
				? BattlegroundBrackets.ForRegion(keep.Region)
				: null;

		/// <summary>DOL's level for a keep's (not a tower's) guard or lord at this base level and keep level. Pure.</summary>
		public static byte KeepGuardLevel(int baseLevel, int keepLevel, double multiplier, bool lord)
		{
			int guardBase = lord ? baseLevel + (baseLevel / 10 + 1) * 2 : baseLevel + 1;
			return (byte)Math.Clamp((int)(guardBase + keepLevel * multiplier), 1, 255);
		}

		/// <summary>The highest level a guard or the lord of this battleground's keep may have: its level at keep level 1.</summary>
		public static byte Cap(BattlegroundBrackets.Bracket bracket, bool lord) =>
			KeepGuardLevel(BaseLevelFor(bracket), AbstractGameKeep.BattlegroundKeepLevel, CapMultiplier, lord);

		/// <summary><paramref name="level"/>, or the cap when it is higher. Pure.</summary>
		public static byte Capped(BattlegroundBrackets.Bracket bracket, bool lord, byte level) =>
			bracket == null ? level : Math.Min(level, Cap(bracket, lord));

		/// <summary>
		/// Puts a battleground keep's base level (row and memory) to its bracket's. Returns the base level it had, or null
		/// when nothing changed (every other keep, or a keep already right), so a second run changes nothing.
		/// </summary>
		public static byte? Repair(AbstractGameKeep keep)
		{
			BattlegroundBrackets.Bracket bracket = BracketOf(keep);
			if (bracket == null)
				return null;
			byte want = BaseLevelFor(bracket);
			byte was = keep.DBKeep.BaseLevel;
			if (was == want && keep.BaseLevel == want)
				return null;
			keep.DBKeep.BaseLevel = want;
			keep.BaseLevel = want;
			return was;
		}

		public static string RepairLine(AbstractGameKeep keep, byte was)
		{
			BattlegroundBrackets.Bracket bracket = BracketOf(keep);
			return $"{RepairLogTag} keep=\"{keep.Name}\" id={keep.KeepID} battleground={bracket?.Name} " +
				$"bracket={bracket?.MinLevel}-{bracket?.MaxLevel} baseLevel={was}->{keep.DBKeep.BaseLevel} " +
				$"guards={(bracket == null ? 0 : Cap(bracket, false))} lord={(bracket == null ? 0 : Cap(bracket, true))} at keep level 1";
		}
	}
}
