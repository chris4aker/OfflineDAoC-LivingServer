using System;
using System.Collections.Generic;
using System.Threading;
using System.Timers;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS;
using DOL.Database;
using DOL.GS.Movement;
using DOL.GS.ServerProperties;
using Timer = System.Timers.Timer;

//Mob with packageid="HostBaf" in same region as bosss will come if he is pulled/aggroed
//Make sure to add that packageid to Host rooms, unless it will not bring a friends!
//DO NOT REMOVE Host Initializator from ingame or encounter will  not work!
namespace DOL.GS
{
    public class HostInitializator : GameNPC
    {
        public HostInitializator() : base()
        {
        }

        #region Initializator Timer Cycle + SpawnHostCopy

        public void StartTimer()
        {
            ECSGameTimer myTimer = new(this, DisplayTimeEvent);
            myTimer.Start();
        }

        public int DisplayTimeEvent(ECSGameTimer timer)
        {
            if (!IsAlive)
                return 0;

            DoStuff();
            return 10000;
        }

        public static bool Spawnhost = false;
        public static bool DoRespawnTimer = false;

        public void DoStuff()
        {
            if (Host.HostCount == 0)
            {
                pickhostcheck = false;
                set_realhost = false;
                if (ChooseHost.Count > 0)
                {
                    ChooseHost.Clear();
                }
            }

            if (Spawnhost == false && Host.HostCount == 0)
            {
                SpawnHostCopy();
                Spawnhost = true;
            }

            if (pickhostcheck == false && Host.HostCount > 0)
            {
                pickhostcheck = PickHost();
            }

            if (Host.HostCount == 0 && DoRespawnTimer == false)
            {
                RespawnChecker();
                DoRespawnTimer = true;
            }
        }

        public void SpawnHostCopy()
        {
            DoRespawnTimer = false;
            for (Host.HostCount = 0; Host.HostCount < 8; Host.HostCount++)
            {
                Host Add = new Host();
                Add.X = this.X;
                Add.Y = this.Y;
                Add.Z = this.Z;
                Add.CurrentRegion = this.CurrentRegion;
                Add.Heading = this.Heading;
                Add.AddToWorld();
                Add.PackageID = "HostCopy" + Host.HostCount;
            }
        }

        #endregion

        #region Pick real Host

        List<GameNPC> ChooseHost = new List<GameNPC>();
        public static bool set_realhost = false;
        public static bool pickhostcheck = false;

        public bool PickHost()
        {
            foreach (GameNPC host in GetNPCsInRadius(8000))
            {
                if (host != null)
                {
                    if (host.Brain is HostBrain)
                    {
                        if (!ChooseHost.Contains(host))
                        {
                            ChooseHost.Add(host);
                        }
                    }
                }
            }

            if (ChooseHost.Count > 0)
            {
                if (set_realhost == false)
                {
                    GameNPC RealHost = ChooseHost[Util.Random(0, ChooseHost.Count - 1)];
                    RealHost.PackageID = "HostReal";
                    set_realhost = true;
                    return true;
                }
            }

            return false;
        }

        #endregion

        #region Respawn Timer and method

        public int RespawnTime(ECSGameTimer timer)
        {
            Spawnhost = false;
            return 0;
        }

        public void RespawnChecker()
        {
            int time = Properties.SET_SI_EPIC_ENCOUNTER_RESPAWNINTERVAL * 60000; //1min is 60000miliseconds        
            new ECSGameTimer(this, new ECSGameTimer.ECSTimerCallback(RespawnTime), time);
        }

        #endregion

        #region AddToWorld + ScriptLoaded

        public override bool AddToWorld()
        {
            StartTimer();
            HIBrain hi = new HIBrain();
            SetOwnBrain(hi);
            base.AddToWorld();
            return true;
        }

        [ScriptLoadedEvent]
        public static void ScriptLoaded(DOLEvent e, object sender, EventArgs args)
        {
            GameNPC[] npcs;
            npcs = WorldMgr.GetNPCsByNameFromRegion("Host Initializator", 60, (eRealm)0);
            if (npcs.Length == 0)
            {
                log.Warn("Host Initializator not found, creating it...");

                log.Warn("Initializing Host Initializator...");
                HostInitializator CO = new HostInitializator();
                CO.Name = "Host Initializator";
                CO.GuildName = "DO NOT REMOVE!";
                CO.RespawnInterval = 5000;
                CO.Model = 665;
                CO.Realm = 0;
                CO.Level = 50;
                CO.Size = 50;
                CO.CurrentRegionID = 60; //caer sidi
                CO.Flags ^= eFlags.CANTTARGET;
                CO.Flags ^= eFlags.FLYING;
                CO.Flags ^= eFlags.DONTSHOWNAME;
                CO.Flags ^= eFlags.PEACE;
                CO.Faction = FactionMgr.GetFactionByID(64);
                CO.X = 26995;
                CO.Y = 29733;
                CO.Z = 17871;
                HIBrain ubrain = new HIBrain();
                CO.SetOwnBrain(ubrain);
                CO.AddToWorld();
                CO.SaveIntoDatabase();
                CO.Brain.Start();
            }
            else
                log.Warn(
                    "Host Initializator exist ingame, remove it and restart server if you want to add by script code.");
        }

        #endregion
    }
}

namespace DOL.AI.Brain
{
    public class HIBrain : StandardMobBrain
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public HIBrain()
            : base()
        {
            ThinkInterval = 2000;
        }

        public override void Think()
        {
            base.Think();
        }
    }
}

///////////////////////////////////////////////////Host Copy////////////////////////////////////
namespace DOL.GS
{
    public class Host : GameEpicBoss
    {
        public Host() : base()
        {
        }



        public override int MeleeAttackRange => 350;

        public override int GetResist(eDamageType damageType)
        {
            switch (damageType)
            {
                case eDamageType.Slash: return 40;// dmg reduction for melee dmg
                case eDamageType.Crush: return 40;// dmg reduction for melee dmg
                case eDamageType.Thrust: return 40;// dmg reduction for melee dmg
                default: return 50;// dmg reduction for rest resists
            }
        }
        public override int MaxHealth
        {
            get { return 30000; }
        }
        public override double GetArmorAF(eArmorSlot slot)
        {
            return 250;
        }
        public override double GetArmorAbsorb(eArmorSlot slot)
        {
            // 85% ABS is cap.
            return 0.20;
        }

        public override void ProcessDeath(GameObject killer)
        {
            if (PackageID == "HostReal")
            {
                foreach (GameNPC boss in WorldMgr.GetNPCsByNameFromRegion("Host", this.CurrentRegionID, 0))
                {
                    if (boss != null)
                    {
                        if (boss.IsAlive)
                        {
                            if (boss.Brain is HostBrain)
                            {
                                if (boss.PackageID != "HostReal")
                                {
                                    boss.Die(killer); //kill rest of copies if real one dies
                                }
                            }
                        }
                    }
                }
            }

            Interlocked.Decrement(ref HostCount);
            base.ProcessDeath(killer);
        }

        public override void ReturnToSpawnPoint(short speed)
        {
            return;
        }
        public static int HostCount = 0;

        /// <summary>He walks his patrol at this speed.</summary>
        public const short PATROL_SPEED = 100;

        /// <summary>
        /// His patrol through the tower (OpenDAoC-Core cadc7b8c0, the points of the old HostPath's 52 WalkTo stages): from his
        /// spawn on the 3rd floor around it, down the stairs to the 2nd, around it, down to the 1st, around it, then all the
        /// way back up, round and round. Every copy walks its own chain of it (EpicEncounterTactics.HostPatrol predicts it).
        /// </summary>
        public static readonly (int X, int Y, int Z)[] PatrolPoints =
        [
            (26995, 29733, 17871),
            (26749, 29730, 17871),
            (26180, 30241, 17871),
            (25743, 30447, 17861),
            (25154, 30151, 17861),
            (24901, 29673, 17861),
            (25376, 29310, 17861),
            (25360, 29635, 17866),
            (25608, 29967, 17702),
            (25984, 29902, 17534),
            (26121, 29617, 17405),
            (25889, 29309, 17251),
            (25453, 29390, 17051),
            (25372, 29775, 16897),
            (25946, 29958, 16638),
            (26116, 29523, 16495),
            (26106, 29305, 16495),
            (25061, 29335, 16495),
            (25046, 30229, 16495),
            (25686, 30428, 16495),
            (26832, 29793, 16495),
            (25718, 29012, 16495),
            (25358, 29563, 16495),
            (25426, 29842, 16406),
            (25842, 29983, 16223),
            (26129, 29643, 16039),
            (25714, 29267, 15796),
            (25345, 29587, 15588),
            (25711, 29995, 15357),
            (26123, 29645, 15122),
            (25796, 28979, 15120),
            (24729, 29725, 15119),
            (25695, 30592, 15119),
            (26792, 29721, 15119),
            (26102, 29302, 15120),
            (26085, 29802, 15192),
            (25487, 29903, 15457),
            (25370, 29483, 15625),
            (25873, 29309, 15872),
            (26103, 29695, 16058),
            (25693, 29975, 16284),
            (25352, 29538, 16495),
            (25775, 29107, 16495),
            (26114, 29597, 16495),
            (25730, 29985, 16722),
            (25368, 29610, 16957),
            (25705, 29283, 17169),
            (26109, 29587, 17393),
            (25759, 30023, 17632),
            (25359, 29578, 17871),
            (25809, 29142, 17871),
            (26344, 29391, 17871)
        ];

        public override bool AddToWorld()
        {
            Model = 26;
            MeleeDamageType = eDamageType.Crush;
            Name = "Host";
            PackageID = "HostCopy";
            RespawnInterval = -1;
            TetherRange = 0;
            Size = 60;
            Level = 79;
            MaxSpeedBase = 300;
            Flags = eFlags.GHOST;

            Faction = FactionMgr.GetFactionByID(64);
            BodyType = 6;
            Realm = eRealm.None;

            Strength = 5;
            Dexterity = 200;
            Constitution = 100;
            Quickness = 125;
            Piety = 220;
            Intelligence = 220;
            Empathy = 200;

            SetScriptedPath(MovementMgr.CreatePath(EPathType.Loop, PATROL_SPEED, PatrolPoints));

            HostBrain adds = new HostBrain();
            SetOwnBrain(adds);
            base.AddToWorld();
            return true;
        }
    }
}

namespace DOL.AI.Brain
{
    public class HostBrain : StandardMobBrain
    {
        private static readonly Logging.Logger log = Logging.LoggerManager.Create(System.Reflection.MethodBase.GetCurrentMethod().DeclaringType);

        public HostBrain()
            : base()
        {
            AggroLevel = 100;
            AggroRange = 400;
            ThinkInterval = 2500;
        }
        public static bool BafHost = false;
        public static bool BafMobs = false;

        #region Set Baf Mob stats
        public void SetMobstats()
        {
            if (Body.TargetObject != null && (Body.InCombat || HasAggro || Body.attackComponent.AttackState == true)) //if in combat
            {
                foreach (GameNPC npc in WorldMgr.GetNPCsFromRegion(Body.CurrentRegionID))
                {
                    if (npc != null)
                    {
                        if (npc.IsAlive && npc.PackageID == "HostBaf" && npc.NPCTemplate != null)
                        {
                            if (BafMobs == true && npc.TargetObject == Body.TargetObject)
                            {
                                npc.TetherRange = 0; //set tether to not return to home
                                if (!npc.IsWithinRadius(Body.TargetObject, 100))
                                {
                                    npc.MaxSpeedBase = 300; //speed is is not near to reach target faster
                                }
                                else
                                    npc.MaxSpeedBase = npc.NPCTemplate.MaxSpeed; //return speed to normal
                            }
                        }
                    }
                }
            }
            else //if not in combat
            {
                foreach (GameNPC npc in WorldMgr.GetNPCsFromRegion(Body.CurrentRegionID))
                {
                    if (npc != null)
                    {
                        if (npc.IsAlive && npc.PackageID == "HostBaf" && npc.NPCTemplate != null)
                        {
                            if (BafMobs == false)
                            {
                                npc.TetherRange = npc.NPCTemplate.TetherRange; //return tether to normal
                                npc.MaxSpeedBase = npc.NPCTemplate.MaxSpeed; //return speed to normal
                            }
                        }
                    }
                }
            }
        }
        #endregion

        public override void Think()
        {
            if (!CheckProximityAggro() && DOL.GS.EpicEncounterMechanics.FightIsOver(Body))
            {
                BafHost = false;
                BafMobs = false;
                Body.Health = Body.MaxHealth;
            }
            if (Body.IsMoving)
            {
                foreach (GameLiving player in EncounterTargets.InRadius(Body, (ushort)AggroRange))
                {
                    if (player != null)
                    {
                        if (player.IsAlive && EncounterTargets.NotGm(player) && !AggroList.ContainsKey(player))
                            AddToAggroList(player, 10);
                    }
                }
            }
            if (HasAggro && Body.TargetObject != null)
            {
                if (BafHost == false)//baf all copies to pulled host
                {
                    foreach (GameNPC npc in WorldMgr.GetNPCsFromRegion(Body.CurrentRegionID))
                    {
                        if (npc != null)
                        {
                            if (npc.IsAlive && npc.Brain is HostBrain && Body.PackageID != npc.PackageID)
                            {
                                GameLiving target = Body.TargetObject as GameLiving;
                                HostBrain brain = (HostBrain)npc.Brain;
                                if (target != null)
                                {
                                    brain.AddToAggroList(target, 10);
                                    npc.StartAttack(target);
                                }
                                BafHost = true;
                            }
                        }
                    }
                }
                if (BafMobs == false)//baf linked mobs to boss
                {
                    foreach (GameNPC npc2 in WorldMgr.GetNPCsFromRegion(Body.CurrentRegionID))
                    {
                        if (npc2 != null)
                        {
                            if (npc2.IsAlive && npc2.PackageID == "HostBaf")
                            {
                                AddAggroListTo(npc2.Brain as StandardMobBrain);
                                BafMobs = true;
                            }
                        }
                    }
                }
            }
            // Out of a fight each copy walks his loop (Host.PatrolPoints): the brain's patrol state, not a chain of WalkTo
            // stages (OpenDAoC-Core cadc7b8c0).
            base.Think();
        }
    }
}
