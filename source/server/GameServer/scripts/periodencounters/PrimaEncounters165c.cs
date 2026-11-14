using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Scripts;

// True 1.65 Prima encounters, set 3: the timed world events (owner 2026-11-07). Evidence: tools\claude-version\true165\
// prima_batch_2026-11-07\ENCOUNTER SPECS.md. Values the period does not give are marked CHOICE. Each event runs from one
// hidden controller row, only while a player is near (light at bot scale).

namespace DOL.GS.Scripts
{
    /// <summary>A hidden event controller: invisible, untargetable, peaceful; its brain runs the event.</summary>
    public abstract class PrimaEventController165 : GameNPC
    {
        protected abstract StandardMobBrain CreateBrain();

        public override bool AddToWorld()
        {
            Flags |= eFlags.CANTTARGET | eFlags.DONTSHOWNAME | eFlags.PEACE;
            Model = 1;
            SetOwnBrain(CreateBrain());
            return base.AddToWorld();
        }
    }

    /// <summary>Thergus the Inspirer: a visible, peaceful ogre who gives the speech and sends the squads to war.</summary>
    public class ThergusOgreWar165 : GameNPC
    {
        public override bool AddToWorld()
        {
            Flags |= eFlags.PEACE;
            SetOwnBrain(new ThergusOgreWarBrain());
            return base.AddToWorld();
        }
    }

    public class GormrorRaid165 : PrimaEventController165 { protected override StandardMobBrain CreateBrain() => new GormrorRaidBrain(); }
    public class HamorikRitual165 : PrimaEventController165 { protected override StandardMobBrain CreateBrain() => new HamorikRitualBrain(); }
}

namespace DOL.AI.Brain
{
    /// <summary>A squad: when one member is attacked, all members within 1500 aggro the attacker (Freddyshouse 2003-02-26:
    /// root one ogre and the entire ogre army storms you).</summary>
    public class PrimaSquadBrain165 : StandardMobBrain
    {
        public PrimaSquadBrain165()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (Body.IsAlive && HasAggro && !string.IsNullOrEmpty(Body.PackageID))
                foreach (GameNPC n in Body.GetNPCsInRadius(1500))
                    if (n != Body && n.IsAlive && n.PackageID == Body.PackageID && n.Brain is StandardMobBrain sb)
                        AddAggroListTo(sb);
            base.Think();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Ogres vs. Orc battle (Aldland). Prima SI 2002 'The Right Place at the Right Time': Thergus the Inspirer gives a long
    // speech to the ogres outside Krondon; then the ogres gather in formation and march to battle, and halfway down the
    // western coastline they run into an orc coast guard squad. Freddyshouse 2003-08-23: 4 or 5 teams of about 10 ogres,
    // each with a squad leader, head for the water and fight the orc coastguards; the ogres get badly creamed. 2003-02-26:
    // both sides stood in formation, glaring, and only fight at certain times. The existing faction war (ogres 8 vs orcs
    // 37) does the fighting. CHOICE: every 2-4 hours when a player is near, half the time skipped; 4 squads; the speech is
    // narrated (only a paraphrase of it survives); stand-off 2-5 minutes; 15 minutes to the end.
    public class ThergusOgreWarBrain : StandardMobBrain
    {
        public const int ThresherTemplate = 60167050, LeaderTemplate = 60167043, ChildrenOfOrylie = 8;
        public static readonly (int X, int Y, int Z)[] Slots = [(384800, 482100, 3137), (384800, 482700, 3159), (384300, 482100, 3118), (384300, 482700, 3162)];
        public static readonly (int X, int Y, int Z)[] March =
        [
            (384500, 482500, 3143), (382500, 482500, 3096), (380500, 482500, 3082), (378500, 482500, 3082), (376500, 483750, 3082),
            (374500, 485750, 3173), (372500, 487750, 3204), (370500, 489750, 3183), (370500, 491750, 3130), (370250, 493750, 3155),
            (369750, 495750, 3104), (368000, 497750, 3080), (367000, 499750, 3064), (367000, 501750, 3276), (367500, 503000, 3280),
        ];
        public static readonly (int X, int Y, int Z) OrcCamp = (367019, 506762, 3200);
        private enum Phase { Idle, Speech, March, StandOff, Battle }
        private Phase _phase;
        private long _next, _end;
        private int _leg, _speechLine;
        private readonly List<GameNPC> _leaders = [];
        private readonly List<GameNPC> _ogres = [];
        private static readonly string[] Speech =
        [
            "Thergus the Inspirer climbs onto a rock and begins a long speech to the gathered ogres.",
            "Thergus tells the ogres that the humans deserted them, and that from now on the ogres will fend for themselves.",
            "Thergus tells them that access to the water gives power, and that the orcs stand between the ogres and the water.",
            "Thergus concludes his speech. The ogres gather in formation and march to battle!",
        ];

        public ThergusOgreWarBrain()
        {
            ThinkInterval = 3000;
            AggroLevel = 0;
            AggroRange = 0;
        }

        public override void Think()
        {
            long now = GameLoop.GameLoopTime;
            switch (_phase)
            {
                case Phase.Idle:
                    if (_next == 0)
                        _next = now + Util.Random(120, 240) * 60_000L;
                    if (now >= _next)
                    {
                        _next = 0;
                        if (Util.Chance(50) && Body.GetPlayersInRadius(20000).Count > 0)
                        {
                            _phase = Phase.Speech;
                            _speechLine = 0;
                            _end = now;
                        }
                    }
                    break;
                case Phase.Speech:
                    if (now >= _end)
                    {
                        Announce(Speech[_speechLine++]);
                        _end = now + 25_000;
                        if (_speechLine >= Speech.Length)
                            FormUp();
                    }
                    break;
                case Phase.March:
                    GameNPC lead = _leaders.FirstOrDefault(l => l.IsAlive);
                    if (lead == null || now >= _end) { Finish(); break; }
                    if (lead.IsWithinRadius(new Point3D(March[_leg].X, March[_leg].Y, March[_leg].Z), 250) && ++_leg >= March.Length)
                    {
                        _phase = Phase.StandOff;                   // glaring and taunting at each other
                        _next = now + Util.Random(2, 5) * 60_000L;
                        break;
                    }
                    for (int i = 0; i < _leaders.Count; i++)
                        if (_leaders[i].IsAlive && !_leaders[i].IsMoving && !_leaders[i].InCombat)
                            _leaders[i].PathTo(new Point3D(March[_leg].X + i * 300 - 450, March[_leg].Y, March[_leg].Z), 120);
                    break;
                case Phase.StandOff:
                    if (now >= _next)
                    {
                        _phase = Phase.Battle;
                        _end = now + 15 * 60_000L;
                        foreach (GameNPC o in _ogres.Concat(_leaders).Concat(Standby()))
                        {
                            if (!o.IsAlive) continue;
                            if (o.Brain is StandardMobBrain sb) { sb.AggroRange = 500; sb.AggroLevel = 100; }
                            o.StopFollowing();
                            o.PathTo(new Point3D(OrcCamp.X + Util.Random(-300, 300), OrcCamp.Y, OrcCamp.Z), 191);
                        }
                    }
                    break;
                case Phase.Battle:
                    if (now >= _end || !_ogres.Concat(_leaders).Any(o => o.IsAlive))
                        Finish();
                    break;
            }
        }

        private IEnumerable<GameNPC> Standby() =>
            WorldMgr.GetNPCsFromRegion(Body.CurrentRegionID).Where(n => n.IsAlive && n.PackageID == "ThrawnOgreStandby");

        private void Announce(string text)
        {
            foreach (GamePlayer p in Body.GetPlayersInRadius(5000))
                p.Out.SendMessage(text, eChatType.CT_Emote, eChatLoc.CL_ChatWindow);
        }

        private void FormUp()
        {
            for (int s = 0; s < Slots.Length; s++)
            {
                (int x, int y, int z) = Slots[s];
                string squad = "ThrawnOgreSquad" + s;
                GameNPC leader = PrimaEncounter165.SpawnAdd(Body, LeaderTemplate, "thrawn ogre squad leader", 47, x, y, z, 1024, Marching(), npc =>
                {
                    npc.Faction = FactionMgr.GetFactionByID(ChildrenOfOrylie);
                    npc.PackageID = squad;
                });
                _leaders.Add(leader);
                for (int i = 0; i < 9; i++)
                {
                    int tx = x + 120 * (i % 3 + 1), ty = y + 120 * (i / 3 - 1);
                    GameNPC t = PrimaEncounter165.SpawnAdd(Body, ThresherTemplate, "thrawn ogre thresher", (byte)Util.Random(44, 48), tx, ty, z, 1024, Marching(), npc =>
                    {
                        npc.Faction = FactionMgr.GetFactionByID(ChildrenOfOrylie);
                        npc.PackageID = squad;
                    });
                    t.Follow(leader, 120, 4000);                   // in formation behind its leader
                    _ogres.Add(t);
                }
            }
            _phase = Phase.March;
            _leg = 0;
            _end = GameLoop.GameLoopTime + 25 * 60_000L;
        }

        // while marching they don't start fights with the monsters on the coast road (they still defend themselves)
        private static PrimaSquadAddBrain165 Marching() => new() { AggroLevel = 0, AggroRange = 0, IdleRemoveMilliseconds = int.MaxValue };

        private void Finish()
        {
            PrimaEncounter165.RemoveAll(_ogres);
            PrimaEncounter165.RemoveAll(_leaders);
            _phase = Phase.Idle;
            _next = 0;
        }
    }

    /// <summary>An event add that also shares its squad's aggro.</summary>
    public class PrimaSquadAddBrain165 : PrimaAddBrain165
    {
        public override void Think()
        {
            if (Body.IsAlive && HasAggro && !string.IsNullOrEmpty(Body.PackageID))
                foreach (GameNPC n in Body.GetNPCsInRadius(1500))
                    if (n != Body && n.IsAlive && n.PackageID == Body.PackageID && n.Brain is StandardMobBrain sb)
                        AddAggroListTo(sb);
            base.Think();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Gormror's raid on Dyrfjell (Aegir's Landing). Prima SI 2002: Gormror inspires the morvalts to attack Dyrfjell; the
    // guards need help; if you lose, they leave the village intact to raid another day; the Fomorians hurl disease into
    // the town. 1.62 notes: the morvalt no longer linger after their leaders have left or been killed. Freddyshouse 2003:
    // disease clouds in town until the raiders die; about 30 adds; Gormror yells "Gather! Together we are strong! Seperate
    // we are weak! March as one! Fight as one!" and "All races will die". CapnBry: lieutenants Horgal, Oireg, Kargil
    // (25-26) by him; "Gormror's disease" objects inside Dyrfjell (one spot, 276326,326601, is under a building's floor and is left out). CHOICE: every 60-180 minutes when a player is near the
    // town; 30 morvalt; 8 clouds; 25 minutes before they leave.
    public class GormrorRaidBrain : StandardMobBrain
    {
        public const int GormrorTemplate = 5020, MorvaltSamhalle = 150, DiseaseSpell = 61028, CloudModel = 941;
        public static readonly (int X, int Y, int Z) Camp = (277037, 330107, 5214);
        public static readonly (int X, int Y, int Z)[] Route = [(277200, 327500, 5180), (277276, 325107, 5136)];
        public static readonly (string Name, int X, int Y, int Z)[] Lieutenants =
            [("Horgal", 278160, 330060, 4710), ("Oireg", 277809, 330876, 4714), ("Kargil", 276476, 331221, 4617), ("Rulod", 277137, 330207, 5214)];
        public static readonly (string Name, int Template, int Min, int Max)[] Raiders =
            [("morvalt krigsman", 60164219, 16, 20), ("morvalt jagare", 60164217, 16, 18), ("morvalt spjutman", 60164234, 13, 16), ("morvalt jakthast", 60164218, 13, 15)];
        public static readonly (int X, int Y, int Z)[] CloudSpots =
        [
            (279788, 324768, 5136), (278566, 324089, 5136), (277141, 324429, 5139), (276113, 323198, 5143), (277412, 325650, 5136), (278928, 327089, 5136),
            (277752, 326940, 5136), (279053, 327447, 5136), (275942, 325068, 5144), (276981, 325722, 5136), (278906, 324293, 5136), (278566, 324496, 5136),
            (279449, 323342, 5143), (278295, 324225, 5136), (279664, 322899, 5095), (277276, 325107, 5136), (276837, 323198, 5148), (275783, 324564, 5144),
            (275512, 325650, 5136), (275036, 326533, 5136), (278249, 325958, 5130), (279177, 325379, 5136), (275647, 327008, 5136), (276609, 326325, 5136),
            (276937, 324904, 5144), (277073, 322596, 5166),
        ];
        private GameNPC _gormror;
        private readonly List<GameNPC> _leaders = [], _raiders = [], _clouds = [];
        private long _next, _end, _nextYell;
        private bool _running;
        private int _leg;

        public GormrorRaidBrain()
        {
            ThinkInterval = 2500;
            AggroLevel = 0;
            AggroRange = 0;
        }

        public override void Think()
        {
            long now = GameLoop.GameLoopTime;
            if (!_running)
            {
                if (_next == 0)
                    _next = now + Util.Random(60, 180) * 60_000L;
                if (now >= _next)
                {
                    _next = 0;
                    if (Body.GetPlayersInRadius(8000).Count > 0)
                        Muster(now);
                }
                return;
            }
            if (!_leaders.Any(l => l.IsAlive) || now >= _end)
            {
                End();                                             // won, or they leave the village to raid another day
                return;
            }
            GameNPC lead = _leaders.FirstOrDefault(l => l.IsAlive);
            if (_leg < Route.Length && lead != null && !lead.InCombat)
            {
                if (lead.IsWithinRadius(new Point3D(Route[_leg].X, Route[_leg].Y, Route[_leg].Z), 250))
                    _leg++;
                else if (!lead.IsMoving)
                    lead.PathTo(new Point3D(Route[_leg].X, Route[_leg].Y, Route[_leg].Z), 160);
            }
            if (_gormror != null && _gormror.IsAlive && now >= _nextYell)
            {
                _nextYell = now + Util.Random(30, 60) * 1000L;
                PrimaEncounter165.Say(_gormror, $"{_gormror.Name} yells, \"All races will die!\"");
            }
            // the raiders go for the town guards (and the guards fight back when hit)
            foreach (GameNPC r in _raiders.Concat(_leaders))
                if (r.IsAlive && r.Brain is StandardMobBrain rb && !rb.HasAggro)
                {
                    GameNPC guard = r.GetNPCsInRadius(900).FirstOrDefault(n => n.IsAlive && n.Realm == eRealm.Midgard && (n.Name.Contains("Guard") || n.Name.Contains("guard")));
                    if (guard != null)
                    {
                        rb.AddToAggroList(guard, 1);
                        r.StartAttack(guard);
                    }
                }
            // the disease clouds sicken players who walk into them
            if (SkillBase.GetSpellByID(DiseaseSpell) is Spell disease)
                foreach (GameNPC cloud in _clouds)
                {
                    if (!cloud.IsAlive || cloud.IsCasting) continue;
                    GamePlayer p = cloud.GetPlayersInRadius(150).FirstOrDefault(pl => pl.IsAlive && !pl.effectListComponent.ContainsEffectForEffectType(eEffect.Disease));
                    if (p == null) continue;
                    cloud.TargetObject = p;
                    cloud.CastSpell(disease, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells));
                }
        }

        private void Muster(long now)
        {
            _running = true;
            _leg = 0;
            _end = now + 25 * 60_000L;
            Faction morvalt = FactionMgr.GetFactionByID(MorvaltSamhalle);
            _gormror = PrimaEncounter165.SpawnAdd(Body, GormrorTemplate, "Gormror", 30, Camp.X, Camp.Y, Camp.Z, 2048,
                new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500, IdleRemoveMilliseconds = int.MaxValue },
                npc => { npc.Model = 768; npc.Size = 68; npc.BodyType = 6; npc.Faction = morvalt; npc.PackageID = "GormrorRaid"; });
            _leaders.Add(_gormror);
            foreach ((string name, int x, int y, int z) in Lieutenants)
            {
                GameNPC lt = PrimaEncounter165.SpawnAdd(Body, Raiders[0].Template, name, (byte)Util.Random(25, 27), x, y, z, 2048,
                    new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500, IdleRemoveMilliseconds = int.MaxValue },
                    npc => { npc.Faction = morvalt; npc.PackageID = "GormrorRaid"; });
                lt.Follow(_gormror, 150, 5000);
                _leaders.Add(lt);
            }
            for (int i = 0; i < 30; i++)
            {
                var t = Raiders[i % Raiders.Length];
                GameNPC r = PrimaEncounter165.SpawnAdd(Body, t.Template, t.Name, (byte)Util.Random(t.Min, t.Max),
                    Camp.X + Util.Random(-400, 400), Camp.Y + Util.Random(-400, 400), Camp.Z, 2048,
                    new PrimaSquadAddBrain165 { AggroLevel = 100, AggroRange = 500, IdleRemoveMilliseconds = int.MaxValue },
                    npc => { npc.Faction = morvalt; npc.PackageID = "GormrorRaid"; });
                r.Follow(_gormror, 200, 6000);
                _raiders.Add(r);
            }
            foreach (var spot in CloudSpots.OrderBy(_ => Util.Random(0, 1000)).Take(8))
            {
                GameNPC cloud = PrimaEncounter165.SpawnAdd(Body, 0, "Gormror's disease", 30, spot.X, spot.Y, spot.Z, 0,
                    new PrimaAddBrain165 { AggroLevel = 0, AggroRange = 0, IdleRemoveMilliseconds = int.MaxValue },
                    npc => { npc.Model = CloudModel; npc.Flags = GameNPC.eFlags.PEACE | GameNPC.eFlags.CANTTARGET | GameNPC.eFlags.DONTSHOWNAME; });
                _clouds.Add(cloud);
            }
            PrimaEncounter165.Say(_gormror, "Gormror yells, \"Gather! Together we are strong! Seperate we are weak! March as one! Fight as one!\"", 6000);
        }

        private void End()
        {
            PrimaEncounter165.RemoveAll(_clouds);
            PrimaEncounter165.RemoveAll(_raiders);                  // they don't linger once the leaders are gone (1.62)
            PrimaEncounter165.RemoveAll(_leaders);
            _gormror = null;
            _running = false;
            _next = 0;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Hamorik (Caldey, the broken tower). Prima SI 2002: if you find the ritual area you may witness his emergence: a
    // spectacular lightshow starts, and if you are there when he arrives the real fun begins. Prima / Illia list the
    // ritual helpers (Tower Spirit, Lich Effects, Hamorik Summoning, Invis Dead Check). Allakhazam 2003-07-17: the broken
    // tower with the light show at night. Level 61. No adds, phases or loot in any period source. CHOICE: once per night
    // with a player near, a 45 s light show, an hour after a kill before the next; at daybreak an idle Hamorik leaves.
    public class HamorikRitualBrain : StandardMobBrain
    {
        public const int HamorikTemplate = 60167731;              // Vortanos' template (stats only)
        public const int LightEffect = 9103;
        private GameNPC _hamorik;
        private long _showEnd, _readyAt, _nextFlash;
        private bool _showing, _thisNight;

        public HamorikRitualBrain()
        {
            ThinkInterval = 2500;
            AggroLevel = 0;
            AggroRange = 0;
        }

        public override void Think()
        {
            long now = GameLoop.GameLoopTime;
            bool night = Body.CurrentRegion?.IsNightTime == true;
            if (!night)
                _thisNight = false;
            if (_hamorik != null)
            {
                if (!_hamorik.IsAlive || _hamorik.ObjectState != GameObject.eObjectState.Active)
                {
                    _hamorik = null;                               // killed: the next emergence an hour later at the earliest
                    _readyAt = now + 60 * 60_000L;
                }
                else if (!night && !_hamorik.InCombat && _hamorik.Brain is StandardMobBrain hb && !hb.HasAggro)
                {
                    _hamorik.Delete();                             // daybreak: he goes back
                    _hamorik = null;
                }
                return;
            }
            if (_showing)
            {
                if (now >= _nextFlash)
                {
                    _nextFlash = now + 5000;
                    foreach (GamePlayer p in Body.GetPlayersInRadius(3000))
                        p.Out.SendSpellEffectAnimation(Body, Body, LightEffect, 0, false, 1);
                }
                if (now >= _showEnd)
                {
                    _showing = false;
                    _hamorik = PrimaEncounter165.SpawnAdd(Body, HamorikTemplate, "Hamorik", 61, Body.X, Body.Y, Body.Z, Body.Heading,
                        new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 600, IdleRemoveMilliseconds = int.MaxValue },
                        npc => { npc.Model = 61; npc.Size = 60; npc.BodyType = 6; npc.MaxSpeedBase = 250; npc.Faction = null; });
                }
                return;
            }
            if (night && !_thisNight && now >= _readyAt && Body.GetPlayersInRadius(3000).Count > 0)
            {
                _thisNight = true;
                _showing = true;
                _showEnd = now + 45_000;
                _nextFlash = 0;
                foreach (GamePlayer p in Body.GetPlayersInRadius(3000))
                    p.Out.SendMessage("A spectacular light show begins at the broken tower...", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            }
        }
    }
}
