using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DOL.GS.Keeps;

namespace DOL.GS.Quests
{
    /// <summary>
    /// The captains who give the battleground and frontier kill and keep quests (1.2, owner 2026-11-15, "Rename to
    /// captains"). Until 1.2 these quests came from "Pazz" (the Thidranki and Caledonia portal keeps) and "Rey" (Castle
    /// Sauvage, Svasud Faste, Druim Ligen), a skeleton titled "Bone Collector" who gathered bones for his master Fen: the
    /// Atlas freeshard's daily-quest giver, brought in by OpenDAoC in 2022. Mythic's own battleground quests were given by
    /// a named captain in each realm's portal keep ("Speak with Captain Circa/Captain Cody/Captain Astridr", the Camelot
    /// Herald's battleground quest guide), so each post now has a captain of its realm, dressed as that realm's keep
    /// commander, and the quests are unchanged (same classes, so saved progress and the daily reset are untouched).
    ///
    /// <para>Ensure finds the post's captain or makes it, and retires the old giver at the post. The scripts made their
    /// giver with new GameNPC(), whose SaveIntoDatabase does nothing (LoadedFromScript), so a world normally has no Pazz or
    /// Rey row. A world where one was saved anyway (a GM's /mob save, an imported world) gets that row renamed in place to
    /// the captain, keeping its Mob_ID and position; any further Pazz or Rey at the post is removed from the world and, if
    /// saved, from the database. Only an NPC of the post's realm, within QuestNpcPlacement.SearchRadius of it, named Pazz
    /// or Rey and titled "Bone Collector" is touched. A second start finds the captain by name and changes nothing.</para>
    /// </summary>
    public static class QuestCaptains
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(MethodBase.GetCurrentMethod().DeclaringType);

        public const string LegacyGuild = "Bone Collector";
        public static readonly IReadOnlyList<string> LegacyNames = new[] { "Pazz", "Rey" };

        public const ushort Thidranki = 252;
        public const ushort Caledonia = 250;
        public const ushort CamelotHills = 1;   // Castle Sauvage
        public const ushort Vale = 100;         // Svasud Faste
        public const ushort Connacht = 200;     // Druim Ligen

        public sealed record Post(eRealm Realm, ushort Region, string Place, int X, int Y, int Z, ushort Heading,
            string Name, string GuildName, ushort Model, eGender Gender);

        /// <summary>One captain per realm and post, standing where Pazz or Rey stood.</summary>
        public static readonly IReadOnlyList<Post> Posts = new[]
        {
            // Thidranki: the Herald's names. The guide lists each battleground's captains Albion, Hibernia, Midgard (its
            // Norse names, Kynja, Astridr, Grattir, Kormarg, always come last), so Circa is Albion's and Cody Hibernia's.
            new Post(eRealm.Albion, Thidranki, "the Thidranki portal keep", 37283, 51881, 3944, 4090,
                "Captain Circa", "Thidranki Garrison", (ushort)eLivingModel.BritonFemale, eGender.Female),
            new Post(eRealm.Hibernia, Thidranki, "the Thidranki portal keep", 18658, 18710, 4320, 1424,
                "Captain Cody", "Thidranki Garrison", (ushort)eLivingModel.CeltMale, eGender.Male),
            new Post(eRealm.Midgard, Thidranki, "the Thidranki portal keep", 54259, 25234, 4319, 1744,
                "Captain Astridr", "Thidranki Garrison", (ushort)eLivingModel.NorseFemale, eGender.Female),

            // Caledonia: the Herald's guide covers the later battlegrounds only, so these names are new, in each realm's style.
            new Post(eRealm.Albion, Caledonia, "the Caledonia portal keep", 37283, 51881, 3944, 4090,
                "Captain Aldwyn", "Caledonia Garrison", (ushort)eLivingModel.HighlanderMale, eGender.Male),
            new Post(eRealm.Hibernia, Caledonia, "the Caledonia portal keep", 18658, 18710, 4320, 1424,
                "Captain Fiachra", "Caledonia Garrison", (ushort)eLivingModel.FirbolgMale, eGender.Male),
            new Post(eRealm.Midgard, Caledonia, "the Caledonia portal keep", 54259, 25234, 4319, 1744,
                "Captain Thorgrim", "Caledonia Garrison", (ushort)eLivingModel.DwarfMale, eGender.Male),

            // The frontier portal keeps (Rey's realm kill quests): new names in each realm's style.
            new Post(eRealm.Albion, CamelotHills, "Castle Sauvage", 583867, 477355, 2600, 3054,
                "Captain Edric", "Castle Sauvage Garrison", (ushort)eLivingModel.BritonMale, eGender.Male),
            new Post(eRealm.Midgard, Vale, "Svasud Faste", 766491, 670375, 5736, 2358,
                "Captain Hallvard", "Svasud Faste Garrison", (ushort)eLivingModel.NorseMale, eGender.Male),
            new Post(eRealm.Hibernia, Connacht, "Druim Ligen", 334866, 420749, 5184, 1640,
                "Captain Eithne", "Druim Ligen Garrison", (ushort)eLivingModel.CeltFemale, eGender.Female),
        };

        public static Post For(eRealm realm, ushort region) =>
            Posts.FirstOrDefault(p => p.Realm == realm && p.Region == region)
            ?? throw new ArgumentException($"No quest captain for {realm} in region {region}.");

        /// <summary>An NPC at a post, as the decision below sees it.</summary>
        public readonly record struct Standing(string Name, string GuildName, bool Saved);

        /// <summary>What Ensure does at a post: keep one NPC (index into the NPCs there, or -1 to make the captain), rename
        /// it to the captain if it is an old giver, and remove the others that are old givers.</summary>
        public sealed record Decision(int Keep, bool Rename, IReadOnlyList<int> Remove)
        {
            public bool Create => Keep < 0;
        }

        public static bool IsLegacyGiver(string name, string guildName) =>
            name != null && LegacyNames.Contains(name) && string.Equals(guildName, LegacyGuild, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The post's captain if one stands there; otherwise an old giver renamed (a saved one first, so its row is the one
        /// kept); otherwise a new captain. Every other old giver goes. NPCs that are neither are never touched.
        /// </summary>
        public static Decision Decide(Post post, IReadOnlyList<Standing> there)
        {
            int captain = -1;
            for (int i = 0; i < there.Count && captain < 0; i++)
                if (there[i].Name == post.Name)
                    captain = i;

            var legacy = new List<int>();
            for (int i = 0; i < there.Count; i++)
                if (IsLegacyGiver(there[i].Name, there[i].GuildName))
                    legacy.Add(i);

            if (captain >= 0)
                return new Decision(captain, false, legacy);
            if (legacy.Count == 0)
                return new Decision(-1, false, Array.Empty<int>());

            int keep = legacy.FirstOrDefault(i => there[i].Saved, legacy[0]);
            return new Decision(keep, true, legacy.Where(i => i != keep).ToList());
        }

        /// <summary>The post's captain in the world, made or renamed if needed (see the class summary).</summary>
        public static GameNPC Ensure(eRealm realm, ushort region)
        {
            Post post = For(realm, region);
            var there = new List<GameNPC>();
            foreach (string name in new[] { post.Name }.Concat(LegacyNames))
                foreach (GameNPC npc in WorldMgr.GetNPCsByNameFromRegion(name, region, realm) ?? Array.Empty<GameNPC>())
                    if (QuestNpcPlacement.IsAt(npc, region, post.X, post.Y) && !there.Contains(npc))
                        there.Add(npc);

            Decision decision = Decide(post, there.Select(n => new Standing(n.Name, n.GuildName, IsSaved(n))).ToList());

            foreach (int i in decision.Remove)
            {
                GameNPC old = there[i];
                if (log.IsInfoEnabled)
                    log.Info($"Quest captains: removing the old giver {old.Name} ({old.GuildName}) at {post.Place} ({realm}); {post.Name} gives its quests.");
                if (IsSaved(old))
                    old.DeleteFromDatabase();
                old.Delete();
            }

            GameNPC captain;
            if (decision.Create)
            {
                captain = new GameNPC();
                captain.CurrentRegionID = post.Region;
                captain.X = post.X;
                captain.Y = post.Y;
                captain.Z = post.Z;
                captain.Heading = post.Heading;
                Dress(captain, post);
                captain.AddToWorld();
            }
            else
            {
                captain = there[decision.Keep];
                if (decision.Rename)
                {
                    if (log.IsInfoEnabled)
                        log.Info($"Quest captains: the saved {captain.Name} ({captain.GuildName}) at {post.Place} ({realm}) is now {post.Name}.");
                    Dress(captain, post);
                    if (IsSaved(captain))
                        captain.SaveIntoDatabase();
                    captain.BroadcastLivingEquipmentUpdate();
                }
            }
            return captain;
        }

        private static bool IsSaved(GameNPC npc) => !npc.LoadedFromScript && !string.IsNullOrEmpty(npc.InternalID);

        private static void Dress(GameNPC npc, Post post)
        {
            npc.Name = post.Name;
            npc.GuildName = post.GuildName;
            npc.Realm = post.Realm;
            npc.Model = post.Model;
            npc.Gender = post.Gender;
            npc.Size = 50;
            npc.Level = 50;
            npc.Flags |= GameNPC.eFlags.PEACE;
            GameNpcInventoryTemplate outfit = post.Realm switch
            {
                eRealm.Albion => ClothingMgr.Albion_Commander,
                eRealm.Midgard => ClothingMgr.Midgard_Commander,
                _ => ClothingMgr.Hibernia_Commander,
            };
            // The keep commanders' outfit (ClothingMgr loads with the keeps, before the quest scripts start).
            if (outfit != null && outfit.IsClosed)
            {
                npc.Inventory = outfit.CloneTemplate();
                npc.SwitchWeapon(eActiveWeaponSlot.Standard);
            }
        }
    }
}
