using System;
using DOL.GS.ServerProperties;

namespace DOL.GS
{
    /// <summary>
    /// The Living Server switch for hired companions (owner 2026-11-16). The hired companions are ported from
    /// shadowofze's True 1.65 v0.7 (f68cc75), via True165, GPL-3.0, where every rule checked Classic165Mode.IsOn (its
    /// whole world mode). Here they check <see cref="IsOn"/> instead, and no other True 1.65 rule comes with them: XP,
    /// death penalty, freeshard command locks and the rest stay as they are.
    ///
    /// Owner rule (2026-11-16): Living Server features default on for installer users, so the switch is True by default.
    /// The feature lives on its own branch until it has been seen working on the owner's PC.
    ///
    /// Hired companions are a fourth kind of character beside real players, autonomous gamebots and the temporary
    /// /spawn and /raid companions: a persistent offline_world_bots row owned by one player character through an
    /// offline_hired_companions row. Off, nobody can hire, no hired companion is summoned at login and every AI Options
    /// reader answers Automatic, so the rest of the world behaves as before; the rows stay in the database.
    /// </summary>
    public static class HiredCompanionsMode
    {
        // One line, as every launcher setting's attribute is (the launcher's KeysAndDefaultsMatchTheServer reads it that way).
        [ServerProperty("bots", "hired_companions", "Hired companions: players hire up to 7 persistent companions (slots open at levels 5, 10, 15, 20, 30, 40 and 45; the first is free) with /hire near a healer of their realm (every capital and large town has one). They level beside their player, keep their gear and coin, and have AI Options. /spawn and /raid are unchanged.", true)]
        public static bool HIRED_COMPANIONS = true;

        /// <summary>Tests can force the switch without touching the server properties.</summary>
        public static bool? Override { get; set; }

        public static bool IsOn => Override ?? HIRED_COMPANIONS;
    }
}
