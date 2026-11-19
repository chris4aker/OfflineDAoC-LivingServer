using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Scripts;

// True 1.65 Prima encounters, set 2 (owner 2026-11-07). Evidence: tools\claude-version\true165\prima_batch_2026-11-07\
// ENCOUNTER SPECS.md. Values the period does not give are marked CHOICE.

namespace DOL.GS.Scripts
{
    // ------------------------------------------------------------------------------------------------------------------
    // Celain Eirich (Salisbury Plains, the SE outer stone of Stonehenge). Allakhazam 2003-07-17 / 2003-11-23: he spawns at
    // exactly 9am game time. Allakhazam 2002-11-12: he has a Zombie Lieutenant; "if left long enough he will raise more
    // undead and they will add if you attack him near them". Prima Atlas: Celain [36], the six undead [31], no loot.
    public class CelainEirich165 : GameNPC
    {
        /// <summary>Pure: real milliseconds from a game time (ms of the game day) to the next 09:00 game time.</summary>
        public static long MillisecondsToNineAm(uint gameTime, uint dayIncrement)
        {
            const long day = 24L * 60 * 60 * 1000, nine = 9L * 60 * 60 * 1000;
            long gameMs = (nine - gameTime % day + day) % day;
            if (gameMs == 0)
                gameMs = day;
            return gameMs / Math.Max(1, dayIncrement);
        }

        public override void Die(GameObject killer)
        {
            (Brain as CelainEirichBrain)?.OnCelainDied();
            // back at the next 09:00 game time (at least a minute from now)
            RespawnInterval = (int)Math.Max(60_000, Math.Min(int.MaxValue, MillisecondsToNineAm(WorldMgr.GetCurrentGameTime(), WorldMgr.GetDayIncrement())));
            base.Die(killer);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Anglator lure (Caillte Garran). Prima SI 2002 'Fishing with the Greater Angler' and Domnann 'Enhanced Monster AI':
    // the greater angler hides and baits players with a lure that looks like a low monster; "When they attack, they
    // reveal themselves, and the lure dies." Freddyshouse 2003-02-26: attack the lure and it falls down dead without being
    // hit, and the anglator pops. Warcry 1.62a: anglators no longer attack while invisible (it reveals before it swings).
    public class AnglatorLure165 : GameNPC
    {
        public const int AnglatorTemplate = 12413;                // angler (stats); the look is model 857 'Anglich'
        private bool _sprung;

        public override bool CanDropLoot => false;

        public override bool AddToWorld()
        {
            _sprung = false;
            return base.AddToWorld();
        }

        public override void OnAttackedByEnemy(AttackData ad)
        {
            if (!_sprung && ad?.Attacker is GameLiving attacker && IsAlive)
            {
                _sprung = true;
                double a = Util.RandomDouble() * 2 * Math.PI;
                int x = X + (int)(220 * Math.Cos(a)), y = Y + (int)(220 * Math.Sin(a));
                var brain = new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500 };
                GameNPC anglator = PrimaEncounter165.SpawnAdd(this, AnglatorTemplate, "greater anglator", (byte)Util.Random(33, 34), x, y, Z,
                    PrimaEncounter165.HeadingTo(x, y, attacker.X, attacker.Y), brain, npc =>
                    {
                        npc.Model = 857;
                        npc.Size = 60;
                        npc.BodyType = 9;                          // reptile
                        npc.Faction = FactionMgr.GetFactionByID(100); // Predators of Hy Brasil
                    });
                foreach (GameLiving l in Members(attacker))         // the attacker and their group
                    brain.AddToAggroList(l, 1);
                anglator.StartAttack(attacker);
                lock (XpGainersLock)
                    XPGainers.Clear();                             // the lure dies without giving credit
                Health = 0;
                Die(null);
                return;
            }
            base.OnAttackedByEnemy(ad);
        }

        private static IEnumerable<GameLiving> Members(GameLiving attacker)
        {
            if (attacker.Group != null)
                return attacker.Group.GetMembersInTheGroup().Where(m => m.IsAlive && m.IsWithinRadius(attacker, 2000));
            return [attacker];
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The Star Lake procession (Domnann). Prima SI 2002 'Right Place at the Right Time': every so often a small procession
    // of priests leaves Bann-didein, goes south along the road to the north shore of Star Lake, throws treant seeds into
    // the lake, treant sprouts emerge, and the procession returns with them. Botonids sometimes attack the ceremony; players
    // may help either side. Allakhazam 2003: central treant sprout / treant sprout, level 12, plant. CHOICE: every 45-90
    // minutes, 3 priests, 4 sprouts, botonids at 40% of processions, and only when a player is near.
    public class StarLakeProcession165 : GameNPC
    {
        public override bool AddToWorld()
        {
            Flags |= eFlags.CANTTARGET | eFlags.DONTSHOWNAME | eFlags.PEACE;
            Model = 1;                                             // an invisible controller
            SetOwnBrain(new StarLakeProcessionBrain());
            return base.AddToWorld();
        }
    }
}

namespace DOL.AI.Brain
{
    // ------------------------------------------------------------------------------------------------------------------
    public class CelainEirichBrain : StandardMobBrain
    {
        // the lieutenant and the five he raises (Allakhazam logged all six the night of 2002-11-12); looks from their templates
        public static readonly (string Name, int Template, ushort Model, byte Size)[] Raised =
        [
            ("undead armswoman", 60167408, 451, 50), ("undead cleric", 60167415, 451, 52), ("undead mercenary", 12741, 111, 49),
            ("undead paladin", 60167421, 28, 52), ("undead paladin lieutenant", 60167421, 28, 57),
        ];
        public const int SalisburyUndead = 55;
        private GameNPC _lieutenant;
        private bool _lieutenantThisLife;
        private readonly List<GameNPC> _raised = [];
        private long _nextRaise;

        public CelainEirichBrain()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (!Body.IsAlive)
                return;
            long now = GameLoop.GameLoopTime;
            if (!_lieutenantThisLife)
            {
                _lieutenantThisLife = true;
                _lieutenant = Spawn("undead mercenary lieutenant", 12741, 111, 49, 110);
                _nextRaise = now + Util.Random(60, 120) * 1000L;
            }
            if (_lieutenant != null && _lieutenant.IsAlive && _lieutenant.Brain is StandardMobBrain lb && !lb.HasAggro &&
                _lieutenant.FollowTarget != Body)
                _lieutenant.Follow(Body, 100, 3000);              // his pet follows him
            // "if left long enough he will raise more undead" - only while nobody fights him (CHOICE: every 60-120 s, cap 5)
            if (!HasAggro && !Body.InCombat && now >= _nextRaise)
            {
                _nextRaise = now + Util.Random(60, 120) * 1000L;
                PrimaEncounter165.Alive(_raised);
                var missing = Raised.Where(r => !_raised.Any(n => n.Name == r.Name)).ToList();
                if (missing.Count > 0)
                {
                    var r = missing[Util.Random(0, missing.Count - 1)];
                    _raised.Add(Spawn(r.Name, r.Template, r.Model, r.Size, Util.Random(150, 300)));
                }
            }
            // "they will add if you attack him near them": anyone fighting one of them fights all of them
            List<GameNPC> group = new List<GameNPC> { Body, _lieutenant }.Concat(_raised).Where(n => n != null && n.IsAlive && n.IsWithinRadius(Body, 1000)).ToList();
            if (group.Any(n => n.Brain is StandardMobBrain b && b.HasAggro))
                foreach (GameNPC from in group)
                    if (from.Brain is StandardMobBrain fb && fb.HasAggro)
                        foreach (GameNPC to in group)
                            if (to != from && to.Brain is StandardMobBrain tb)
                                fb.AddAggroListTo(tb);
            base.Think();
        }

        private GameNPC Spawn(string name, int template, ushort model, byte size, int distance)
        {
            double a = Util.RandomDouble() * 2 * Math.PI;
            int x = Body.X + (int)(distance * Math.Cos(a)), y = Body.Y + (int)(distance * Math.Sin(a));
            var brain = new PrimaAddBrain165 { AggroLevel = 0, AggroRange = 0, IdleRemoveMilliseconds = int.MaxValue };
            return PrimaEncounter165.SpawnAdd(Body, template, name, 31, x, y, Body.Z, Body.Heading, brain, npc =>
            {
                npc.Model = model;
                npc.Size = size;
                npc.BodyType = 11;
                npc.Faction = FactionMgr.GetFactionByID(SalisburyUndead);
            });
        }

        /// <summary>The lieutenant goes with him; the raised undead linger 10 minutes (CHOICE) and then crumble.</summary>
        public void OnCelainDied()
        {
            if (_lieutenant != null && _lieutenant.IsAlive)
                _lieutenant.Delete();
            _lieutenant = null;
            _lieutenantThisLife = false;
            List<GameNPC> left = [.. _raised];
            _raised.Clear();
            new ECSGameTimer(Body, _ =>
            {
                foreach (GameNPC n in left)
                    if (n.IsAlive && n.ObjectState == GameObject.eObjectState.Active)
                        n.Delete();
                return 0;
            }).Start(600_000);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // The Unseelie Envoy convoy (Bog of Cullen - Lough Gur). Freddyshouse 2002-05-05: every morning they patrol all the way
    // into Lough Gur and head back to the Siabra town in the afternoon; the stallions are aggressive; 2002-05-06: the
    // convoy chased a player into Innis and fought the guards. Patch 1.42: the escorts no longer cross through Innis on the
    // way home, and the group may come out by day or night. The envoy walks the stored path (PathID); the escorts and
    // stallions follow him, and the whole convoy fights together.
    public class UnseelieEnvoyBrain : StandardMobBrain
    {
        public const string Package = "UnseelieEnvoyConvoy";
        private List<GameNPC> _members = [];
        private long _nextScan;

        public UnseelieEnvoyBrain()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (Body.IsAlive)
            {
                long now = GameLoop.GameLoopTime;
                if (now >= _nextScan)
                {
                    _nextScan = now + 10_000;
                    _members = Body.GetNPCsInRadius(10000).Where(n => n != Body && n.PackageID == Package).ToList();
                }
                foreach (GameNPC m in _members)
                {
                    if (!m.IsAlive || m.Brain is not StandardMobBrain mb)
                        continue;
                    if (mb.HasAggro)
                        mb.AddAggroListTo(this);
                    if (HasAggro)
                        AddAggroListTo(mb);
                }
            }
            base.Think();
        }
    }

    public class UnseelieConvoyFollowerBrain : StandardMobBrain
    {
        private GameNPC _leader;
        private long _nextScan;

        public UnseelieConvoyFollowerBrain()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (!Body.IsAlive)
                return;
            long now = GameLoop.GameLoopTime;
            if ((_leader == null || !_leader.IsAlive || _leader.ObjectState != GameObject.eObjectState.Active) && now >= _nextScan)
            {
                _nextScan = now + 10_000;
                _leader = Body.GetNPCsInRadius(10000).FirstOrDefault(n => n.IsAlive && n.Brain is UnseelieEnvoyBrain);
            }
            if (_leader != null && _leader.IsAlive)
            {
                if (HasAggro && _leader.Brain is StandardMobBrain lb)
                    AddAggroListTo(lb);                            // the convoy fights together
                if (!HasAggro && !Body.InCombat)
                {
                    if (Body.FollowTarget != _leader)
                        Body.Follow(_leader, 150, 10000);
                    return;                                        // walking with the convoy, not home
                }
            }
            base.Think();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    public class StarLakeProcessionBrain : StandardMobBrain
    {
        public const int PriestTemplate = 60165023;               // Priestess of the Watch (Sylvan look, level 19)
        public const int SproutModel = 946;
        public const int BotonidTemplate = 60158656;              // botonid leaf (level 9-12)
        public const int TheWatcher = 111, Botonids = 69;
        // Bann-didein, south along the road, cross-country to the north shore of Star Lake (Prima SI 2002)
        public static readonly (int X, int Y, int Z)[] Route = [(427314, 407336, 4483), (427695, 410923, 4479), (432600, 414050, 4470), (436030, 416255, 4427), (438000, 416650, 4342)];
        public static readonly (int X, int Y, int Z) Lake = (438000, 417550, 4131);
        private enum Phase { Waiting, Out, Ritual, Back }
        private Phase _phase;
        private long _next;
        private int _leg;
        private readonly List<GameNPC> _priests = [];
        private readonly List<GameNPC> _sprouts = [];
        private readonly List<GameNPC> _botonids = [];

        public StarLakeProcessionBrain()
        {
            ThinkInterval = 2000;
            AggroLevel = 0;
            AggroRange = 0;
        }

        public override void Think()
        {
            long now = GameLoop.GameLoopTime;
            GameNPC leader = _priests.FirstOrDefault(p => p.IsAlive && p.ObjectState == GameObject.eObjectState.Active);
            switch (_phase)
            {
                case Phase.Waiting:
                    if (_next == 0)
                        _next = now + Util.Random(45, 90) * 60_000L;
                    if (now >= _next && Body.GetPlayersInRadius(9000).Count > 0)
                        Start();
                    break;
                case Phase.Out:
                    if (leader == null) { End(); break; }
                    if (leader.IsWithinRadius(new Point3D(Route[_leg].X, Route[_leg].Y, Route[_leg].Z), 200))
                    {
                        if (++_leg >= Route.Length)
                        {
                            _phase = Phase.Ritual;
                            _next = now + 15_000;
                            foreach (GamePlayer p in leader.GetPlayersInRadius(2000))
                                p.Out.SendMessage("The priests throw treant seeds into Star Lake.", eChatType.CT_Emote, eChatLoc.CL_ChatWindow);
                            break;
                        }
                    }
                    Walk(leader, Route[_leg]);
                    break;
                case Phase.Ritual:
                    if (leader == null) { End(); break; }
                    if (now >= _next)
                    {
                        Sprouts(leader);
                        if (Util.Chance(40))
                            SpawnBotonids(leader);
                        _phase = Phase.Back;
                        _leg = Route.Length - 1;
                    }
                    break;
                case Phase.Back:
                    if (leader == null) { End(); break; }
                    if (leader.IsWithinRadius(new Point3D(Route[_leg].X, Route[_leg].Y, Route[_leg].Z), 200))
                    {
                        if (--_leg < 0)
                        {
                            End();                                 // back at Bann-didein: the procession goes home
                            break;
                        }
                    }
                    Walk(leader, Route[_leg]);
                    break;
            }
        }

        private static void Walk(GameNPC leader, (int X, int Y, int Z) to)
        {
            if (leader.IsMoving || leader.InCombat)
                return;
            leader.PathTo(new Point3D(to.X, to.Y, to.Z), 100);
        }

        private void Start()
        {
            for (int i = 0; i < 3; i++)
            {
                var brain = new PrimaAddBrain165 { AggroLevel = 0, AggroRange = 0, IdleRemoveMilliseconds = int.MaxValue };
                GameNPC priest = PrimaEncounter165.SpawnAdd(Body, PriestTemplate, "Priestess of the Watch", 19, Body.X + i * 60, Body.Y, Body.Z, 0, brain,
                    npc => { npc.Faction = FactionMgr.GetFactionByID(TheWatcher); npc.MaxSpeedBase = 100; });
                if (_priests.Count > 0)
                    priest.Follow(_priests[0], 80, 6000);
                _priests.Add(priest);
            }
            _phase = Phase.Out;
            _leg = 0;
        }

        private void Sprouts(GameNPC leader)
        {
            for (int i = 0; i < 5; i++)
            {
                bool central = i == 0;
                var brain = new PrimaAddBrain165 { AggroLevel = 0, AggroRange = 0, IdleRemoveMilliseconds = int.MaxValue };
                GameNPC sprout = PrimaEncounter165.SpawnAdd(Body, 0, central ? "central treant sprout" : "treant sprout", 12,
                    Lake.X + (i - 2) * 150, Lake.Y, Lake.Z, 0, brain, npc =>
                    {
                        npc.Model = SproutModel;
                        npc.Size = (byte)(central ? 45 : 40);
                        npc.BodyType = 10;
                        npc.Faction = FactionMgr.GetFactionByID(TheWatcher);
                        npc.MaxSpeedBase = 100;
                    });
                sprout.Follow(leader, 120, 6000);                  // they go back with the procession
                _sprouts.Add(sprout);
            }
        }

        private void SpawnBotonids(GameNPC leader)
        {
            for (int i = 0; i < 3; i++)
            {
                var brain = new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 600 };
                GameNPC b = PrimaEncounter165.SpawnAdd(Body, BotonidTemplate, "botonid leaf", (byte)Util.Random(9, 12), leader.X + 700, leader.Y - 300 + i * 200, leader.Z,
                    PrimaEncounter165.HeadingTo(leader.X + 700, leader.Y, leader.X, leader.Y), brain,
                    npc => npc.Faction = FactionMgr.GetFactionByID(Botonids));
                brain.AddToAggroList(leader, 1);
                b.StartAttack(leader);
                _botonids.Add(b);
            }
        }

        private void End()
        {
            PrimaEncounter165.RemoveAll(_priests);
            PrimaEncounter165.RemoveAll(_sprouts);
            _botonids.Clear();                                     // botonids still fighting finish and leave on their own
            _phase = Phase.Waiting;
            _next = 0;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Arr (Gripklosa Mountains). Prima SI 2002: a griffon that patrols the frigid mountains and "swoops down to pick up
    // adventurers with its claws, only to drop them to their deaths"; tip: "he'll snatch you up in his talons and take you
    // to a nearby nest to handle you one-on-one". Freddyshouse 2003-12 (quoting a 2003 trophy guide): there is no way to
    // attack him; go where he flies and wait for him to attack you. Only players are snatched (bots could not find their
    // way back from the nest - EpicEncounterMechanics: no displacement for bot raids).
    public class ArrBrain : StandardMobBrain
    {
        public const int PatrolZ = 10200;
        public static readonly (int X, int Y)[] Patrol = [(288528, 285760), (297628, 287760), (314528, 286760), (330928, 286560)];
        public static readonly (int X, int Y, int Z) Nest = (300928, 284660, 9150);   // an unwalkable summit by his spot
        private int _leg;
        private int _dir = 1;
        private GamePlayer _prey;

        public ArrBrain()
        {
            ThinkInterval = 1000;
        }

        public override void Think()
        {
            if (!Body.IsAlive)
                return;
            if (HasAggro || Body.InCombat)
            {
                base.Think();
                return;
            }
            if (_prey != null)
            {
                Swoop();
                return;
            }
            if (EpicEncounterMechanics.FightIsOver(Body) && (Body.Flags & GameNPC.eFlags.CANTTARGET) == 0)
            {
                Body.Health = Body.MaxHealth;                      // reset: back up into the sky
                Body.Flags |= GameNPC.eFlags.FLYING | GameNPC.eFlags.CANTTARGET | GameNPC.eFlags.DONTSHOWNAME;
            }
            // patrol, out of reach: pick a player below him (CHOICE: 1800 units around, one in three turns)
            GamePlayer below = Body.GetPlayersInRadius(4000).Where(p => p.IsAlive && p.Client?.Account?.PrivLevel == 1 && !p.IsStealthed &&
                Math.Sqrt((double)(p.X - Body.X) * (p.X - Body.X) + (double)(p.Y - Body.Y) * (p.Y - Body.Y)) <= 1800).OrderBy(_ => Util.Random(0, 1000)).FirstOrDefault();
            if (below != null && Util.Chance(33))
            {
                _prey = below;
                Body.Flags &= ~(GameNPC.eFlags.CANTTARGET | GameNPC.eFlags.DONTSHOWNAME);
                Swoop();
                return;
            }
            if (!Body.IsMoving)
            {
                (int x, int y) = Patrol[_leg];
                if (Body.IsWithinRadius(new Point3D(x, y, PatrolZ), 300))
                {
                    if (_leg + _dir < 0 || _leg + _dir >= Patrol.Length)
                        _dir = -_dir;
                    _leg += _dir;
                    (x, y) = Patrol[_leg];
                }
                Body.WalkTo(new Point3D(x, y, PatrolZ), Body.MaxSpeed);
            }
        }

        private void Swoop()
        {
            GamePlayer p = _prey;
            if (p == null || !p.IsAlive || p.ObjectState != GameObject.eObjectState.Active || !p.IsWithinRadius(Body, 6000))
            {
                _prey = null;
                return;
            }
            if (!Body.IsWithinRadius(p, 250))
            {
                Body.WalkTo(new Point3D(p.X, p.Y, p.Z), 400);
                return;
            }
            // the snatch: carried off to the nest, where Arr lands and fights the victim one-on-one
            _prey = null;
            p.Out.SendMessage("Arr snatches you up in his talons and carries you off to his nest!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            p.MoveTo(Body.CurrentRegionID, Nest.X, Nest.Y, Nest.Z, p.Heading);
            Body.MoveTo(Body.CurrentRegionID, Nest.X + 120, Nest.Y, Nest.Z, Body.Heading);
            Body.Flags &= ~(GameNPC.eFlags.FLYING | GameNPC.eFlags.CANTTARGET | GameNPC.eFlags.DONTSHOWNAME);
            AddToAggroList(p, 1000);
            Body.StartAttack(p);
        }
    }
}
