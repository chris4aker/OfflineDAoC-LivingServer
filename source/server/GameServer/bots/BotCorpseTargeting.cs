using System.Collections.Generic;
using DOL.GS.PacketHandler;

namespace DOL.GS
{
    /// <summary>
    /// A player's resurrection spell reaches a dead bot the client can't select (1.2.1; owner: dead bots "aren't able to be
    /// targeted or have their corpse selected so the player can rez").
    ///
    /// A dead bot is a GameNPC that stays in the world with 0 health (GameBot.RetainCorpseForResurrection). The server
    /// accepts it as a target and as a corpse (PlayerTargetHandler, SpellHandler.CheckBeginCast), and bots raise each
    /// other that way; but a player's cast took its target only from what the client had selected, and the original
    /// client doesn't select a dead NPC. With nothing dead selected the spell now goes to a dead bot of the caster's realm
    /// in the spell's range: the one named with /target, else one of the caster's group, else the nearest.
    /// Companions, hired companions and gamebots alike. Real players' corpses and a dead target the client did select are
    /// untouched.
    /// </summary>
    public static class BotCorpseTargeting
    {
        /// <summary>The dead bot a player named with /target (a GameBot): the next resurrection without a dead target goes
        /// to it first.</summary>
        public const string AskedForProperty = "BOT_CORPSE_ASKED_FOR";

        /// <summary>One dead bot a resurrection could go to.</summary>
        public readonly record struct Corpse(bool AskedFor, bool Grouped, int Distance);

        /// <summary>Pure: the cast looks for a dead bot only when the caster has nothing dead selected (no target, or a
        /// living one - the caster itself included). A dead target the client selected is never replaced.</summary>
        public static bool LooksForCorpse(bool hasTarget, bool targetAlive) => !hasTarget || targetAlive;

        /// <summary>Pure: which of <paramref name="corpses"/> the spell goes to, or -1. Only those within
        /// <paramref name="range"/>; the one asked for by name before a groupmate before a stranger, then the nearest.</summary>
        public static int Choose(IReadOnlyList<Corpse> corpses, int range)
        {
            int best = -1;
            if (corpses == null)
                return best;

            for (int i = 0; i < corpses.Count; i++)
            {
                Corpse corpse = corpses[i];
                if (corpse.Distance > range)
                    continue;
                if (best < 0 || Rank(corpse) < Rank(corpses[best]) ||
                    (Rank(corpse) == Rank(corpses[best]) && corpse.Distance < corpses[best].Distance))
                    best = i;
            }
            return best;
        }

        private static int Rank(Corpse corpse) => corpse.AskedFor ? 0 : corpse.Grouped ? 1 : 2;

        /// <summary>A dead bot of the caster's realm lying in the caster's region.</summary>
        public static bool IsRaisable(GamePlayer caster, GameBot bot) =>
            caster != null && bot != null && bot.ObjectState == GameObject.eObjectState.Active && !bot.IsAlive &&
            bot.Health <= 0 && bot.CurrentRegion == caster.CurrentRegion &&
            GameServer.ServerRules.IsSameRealm(caster, bot, true);

        /// <summary>/target named a dead bot: remember it for the caster's next resurrection.</summary>
        public static void Remember(GamePlayer player, GameBot bot)
        {
            if (IsRaisable(player, bot))
                player.TempProperties.SetProperty(AskedForProperty, bot);
        }

        /// <summary>A dead bot of the player's realm by name within <paramref name="radius"/> (for /target: a /bot
        /// companion is not in the gamebot registry).</summary>
        public static GameBot FindByName(GamePlayer player, string name, ushort radius)
        {
            if (player == null || string.IsNullOrWhiteSpace(name))
                return null;

            name = name.Trim();
            foreach (GameNPC npc in player.GetNPCsInRadius(radius))
            {
                if (npc is GameBot bot && IsRaisable(player, bot) &&
                    string.Equals(bot.Name, name, System.StringComparison.OrdinalIgnoreCase))
                    return bot;
            }
            return null;
        }

        /// <summary>
        /// The dead bot a player's resurrection goes to when the caster has nothing dead selected; false (and
        /// <paramref name="corpse"/> null) when the selection stands or no dead bot is in range.
        /// </summary>
        public static bool TryRedirect(GamePlayer caster, GameLiving selected, int range, bool quiet, out GameLiving corpse)
        {
            corpse = null;
            if (caster == null || range <= 0 || !LooksForCorpse(selected != null, selected?.IsAlive == true))
                return false;

            GameBot askedFor = caster.TempProperties.GetProperty<GameBot>(AskedForProperty);
            if (askedFor != null && !IsRaisable(caster, askedFor))
            {
                caster.TempProperties.RemoveProperty(AskedForProperty);
                askedFor = null;
            }

            List<GameBot> bots = new();
            List<Corpse> corpses = new();
            foreach (GameNPC npc in caster.GetNPCsInRadius((ushort) System.Math.Min(range, ushort.MaxValue)))
            {
                if (npc is not GameBot bot || !IsRaisable(caster, bot))
                    continue;
                bots.Add(bot);
                corpses.Add(new Corpse(bot == askedFor, caster.Group != null && bot.Group == caster.Group,
                    caster.GetDistanceTo(bot)));
            }

            int chosen = Choose(corpses, range);
            if (chosen < 0)
                return false;

            corpse = bots[chosen];
            if (!quiet)
            {
                caster.Out.SendMessage($"You have no dead target selected: your spell goes to {corpse.GetName(0, false)}.",
                    eChatType.CT_System, eChatLoc.CL_SystemWindow);
            }
            return true;
        }
    }
}
