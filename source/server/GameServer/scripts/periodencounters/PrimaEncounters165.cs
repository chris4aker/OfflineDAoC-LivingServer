using System;
using System.Collections.Generic;
using System.Linq;
using DOL.AI.Brain;
using DOL.GS;
using DOL.GS.PacketHandler;
using DOL.GS.Scripts;

// True 1.65 Prima encounters (owner 2026-11-07: "add these and make sure they follow period scripts if possible").
// The behaviour of each encounter follows the period sources researched for it (Prima Shrouded Isles / Atlas 2002,
// Allakhazam and Freddyshouse 2002-2003); tools\claude-version\true165\prima_batch_2026-11-07\ENCOUNTER SPECS.md has the
// evidence. Values the period does not give (wave sizes, timers) are marked CHOICE.

namespace DOL.GS.Scripts
{
    /// <summary>Shared pieces of the Prima encounters: script-spawned adds that never respawn and clean themselves up.</summary>
    public static class PrimaEncounter165
    {
        /// <summary>A script-spawned add from an NPC template (look and stats), renamed and re-levelled.</summary>
        public static GameNPC SpawnAdd(GameNPC near, int templateId, string name, byte level, int x, int y, int z, ushort heading,
            StandardMobBrain brain, Action<GameNPC> tweak = null)
        {
            var npc = new GameNPC();
            INpcTemplate template = NpcTemplateMgr.GetTemplate(templateId);
            if (template != null)
                npc.LoadTemplate(template);
            npc.Name = name;
            npc.CurrentRegion = near.CurrentRegion;
            npc.X = x;
            npc.Y = y;
            npc.Z = z;
            npc.Heading = heading;
            npc.Realm = 0;
            npc.RespawnInterval = -1;
            npc.LoadedFromScript = true;
            tweak?.Invoke(npc);
            npc.Level = level;
            npc.SetOwnBrain(brain);
            npc.AddToWorld();
            npc.Health = npc.MaxHealth;
            return npc;
        }

        /// <summary>Points on a ring around a centre (the seedlings come out of Cuinn's town around his spawn point).</summary>
        public static IEnumerable<(int X, int Y)> Ring(int cx, int cy, int radius, int count)
        {
            for (int i = 0; i < count; i++)
            {
                double a = 2 * Math.PI * i / count;
                yield return (cx + (int)Math.Round(radius * Math.Cos(a)), cy + (int)Math.Round(radius * Math.Sin(a)));
            }
        }

        public static ushort HeadingTo(int fromX, int fromY, int toX, int toY) =>
            (ushort)(((int)(Math.Atan2(toX - fromX, -(toY - fromY)) * 2048 / Math.PI) + 4096) % 4096);

        public static void Say(GameNPC from, string message, int radius = 3000)
        {
            foreach (GamePlayer player in from.GetPlayersInRadius((ushort)radius))
                player.Out.SendMessage(message, eChatType.CT_Say, eChatLoc.CL_ChatWindow);
        }

        public static void RemoveAll(List<GameNPC> adds)
        {
            foreach (GameNPC add in adds)
                if (add != null && add.ObjectState == GameObject.eObjectState.Active && add.IsAlive)
                    add.Delete();
            adds.Clear();
        }

        public static int Alive(List<GameNPC> adds)
        {
            adds.RemoveAll(a => a == null || !a.IsAlive || a.ObjectState != GameObject.eObjectState.Active);
            return adds.Count;
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Talvor (Aegir's Landing): "once slain, his spirit rises up to seek revenge upon his killers" (Prima SI 2002,
    // 'Round Two, FIGHT!'); Allakhazam 2003: Talvor's Spirit "will appear once Talvor is killed", level 25, undead.
    public class Talvor165 : GameNPC
    {
        public const string SpiritPackage = "TalvorSpirit";

        public override void Die(GameObject killer)
        {
            List<GameLiving> revenge = (Brain as StandardMobBrain)?.GetOrderedAggroList().Select(e => e.Living)
                .Where(l => l != null && l.IsAlive).ToList() ?? [];
            int x = X, y = Y, z = Z;
            ushort heading = Heading;
            base.Die(killer);
            if (GetNPCsInRadius(3000).Any(n => n.IsAlive && n.PackageID == SpiritPackage))
                return;                                            // only one spirit at a time
            var brain = new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500 };
            GameNPC spirit = PrimaEncounter165.SpawnAdd(this, NPCTemplate?.TemplateId ?? 0, "Talvor's Spirit", 25, x, y, z, heading, brain,
                npc =>
                {
                    npc.Model = 929;
                    npc.Size = 49;
                    npc.BodyType = 11;                             // undead
                    npc.Race = 2009;
                    npc.Faction = Faction;
                    npc.MaxSpeedBase = 191;
                    npc.PackageID = SpiritPackage;
                    npc.Flags = 0;
                });
            foreach (GameLiving living in revenge)                 // "seek revenge upon his killers"
                brain.AddToAggroList(living, 1);
            if (revenge.Count > 0)
                spirit.StartAttack(revenge[0]);
        }
    }
}

namespace DOL.AI.Brain
{
    /// <summary>A script-spawned add: fights like a normal monster and leaves the world once it has been idle a while.</summary>
    public class PrimaAddBrain165 : StandardMobBrain
    {
        /// <summary>CHOICE (no period figure): an add with nobody to fight for this long leaves the world.</summary>
        public int IdleRemoveMilliseconds = 60_000;
        private long _idleSince;

        public PrimaAddBrain165()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (!Body.IsAlive)
                return;
            if (!HasAggro && !Body.InCombat)
            {
                long now = GameLoop.GameLoopTime;
                if (_idleSince == 0)
                    _idleSince = now;
                else if (now - _idleSince >= IdleRemoveMilliseconds)
                {
                    Body.Delete();
                    return;
                }
            }
            else
                _idleSince = 0;
            base.Think();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Cuinn (Cothrom Gorge, Outlander Town). Freddyshouse 2003-03-13/17: at 10% health he calls for help, the whole
    // town comes along with 6 treant seedlings (level 60, they drop the jewels). Patch 1.61 (2003-04-02): "garran treant
    // seedlings that are part of the Cuinn encounter". Groups pulled him away from town to finish him before the adds.
    public class CuinnBrain : StandardMobBrain
    {
        public const int SeedlingTemplate = 60161210;              // garran treant (look, stats)
        public const int TownRadius = 4500;
        public const int OutlandResidents = 98;
        private bool _called;
        private readonly List<GameNPC> _seedlings = [];

        public CuinnBrain()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (Body.IsAlive && HasAggro && !_called && Body.HealthPercent <= 10)
                CallForHelp();
            if (_called && !HasAggro && EpicEncounterMechanics.FightIsOver(Body))
            {
                PrimaEncounter165.RemoveAll(_seedlings);          // reset: the seedlings go back into town
                _called = false;
            }
            base.Think();
        }

        private void CallForHelp()
        {
            _called = true;
            PrimaEncounter165.Say(Body, $"{Body.Name} calls for help!");
            Point3D home = Body.SpawnPoint;
            // the town comes along: its living residents (not the merchants and other service NPCs), from the town -
            // a Cuinn pulled away to the crossroads has to wait for them to arrive
            foreach (GameNPC npc in WorldMgr.GetNPCsFromRegion(Body.CurrentRegionID))
            {
                if (npc == null || npc == Body || !npc.IsAlive || npc.Faction?.Id != OutlandResidents || npc.GetType() != typeof(GameNPC) ||
                    !string.IsNullOrEmpty(npc.GuildName) || !npc.IsWithinRadius(home, TownRadius) || npc.Brain is not StandardMobBrain sb)
                    continue;
                AddAggroListTo(sb);
            }
            // six seedlings, in town around Cuinn's spawn point; they walk to the fight and outlive him
            GameLiving target = Body.TargetObject as GameLiving;
            foreach ((int x, int y) in PrimaEncounter165.Ring(home.X, home.Y, 220, 6))
            {
                var brain = new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500 };
                GameNPC seedling = PrimaEncounter165.SpawnAdd(Body, SeedlingTemplate, "garran treant seedling", 60, x, y, home.Z,
                    PrimaEncounter165.HeadingTo(x, y, home.X, home.Y), brain, npc =>
                    {
                        npc.Model = 946;
                        npc.Size = 50;
                        npc.BodyType = 10;                         // plant
                        npc.Faction = FactionMgr.GetFactionByID(OutlandResidents);
                    });
                AddAggroListTo(brain);
                if (target != null)
                    seedling.StartAttack(target);
                _seedlings.Add(seedling);
            }
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Emmisary of Logi (Delling Crater). Prima SI 2002 p.136 'Great Balls of Fire': among the caldera lords, hurling great
    // balls of fire; "In times of need, obsidian doomward spawns run to his side in battle". Allakhazam 2003: "Throws
    // balls of fire!". The caldera lords are social. CHOICE: one wave of 2 doomwards at 75%, 50% and 25% health.
    public class EmmisaryOfLogiBrain : StandardMobBrain
    {
        public const int DoomwardTemplate = 60164578;             // obsidian golem (look, stats)
        public const int FireballSpell = 313;                     // the existing heat bolt
        public static readonly (int X, int Y, int Z)[] Anchors = [(361569, 311561, 3896), (360559, 310262, 3903), (362592, 310212, 3916)];
        private static readonly int[] Waves = [75, 50, 25];
        private int _wave;
        private bool _pulled;
        private readonly List<GameNPC> _doomwards = [];

        public EmmisaryOfLogiBrain()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (Body.IsAlive && HasAggro && Body.TargetObject is GameLiving target)
            {
                if (!_pulled)
                {
                    _pulled = true;
                    foreach (GameNPC npc in Body.GetNPCsInRadius(1500))
                        if (npc.IsAlive && npc.Name == "caldera lord" && npc.Brain is StandardMobBrain sb)
                            AddAggroListTo(sb);
                }
                while (_wave < Waves.Length && Body.HealthPercent <= Waves[_wave])
                {
                    for (int i = 0; i < 2; i++)
                    {
                        (int x, int y, int z) = Anchors[(_wave * 2 + i) % Anchors.Length];
                        var brain = new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500 };
                        GameNPC doomward = PrimaEncounter165.SpawnAdd(Body, DoomwardTemplate, "obsidian doomward", 55, x, y, z,
                            PrimaEncounter165.HeadingTo(x, y, Body.X, Body.Y), brain, npc => npc.Faction = Body.Faction);
                        brain.AddToAggroList(target, 1);               // they run to his side and take his target
                        doomward.StartAttack(target);
                        _doomwards.Add(doomward);
                    }
                    _wave++;
                }
                if (!Body.IsCasting && !Body.IsWithinRadius(target, Body.attackComponent.AttackRange) && SkillBase.GetSpellByID(FireballSpell) is Spell fireball)
                    Body.CastSpell(fireball, SkillBase.GetSpellLine(GlobalSpellsLines.Mob_Spells));
            }
            else if ((_pulled || _wave > 0) && !HasAggro && EpicEncounterMechanics.FightIsOver(Body))
            {
                PrimaEncounter165.RemoveAll(_doomwards);
                _wave = 0;
                _pulled = false;
                Body.Health = Body.MaxHealth;
            }
            base.Think();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Forgemaster Brungar (Dellingstad). Prima SI 2002 p.137: "If attacked, Brungar will call upon the Spirit of the Forge
    // to aid him"; Allakhazam 2003 the same. The Spirit is level 70 undead (Prima). One per fight.
    public class ForgemasterBrungarBrain : StandardMobBrain
    {
        public const int SpiritTemplate = 60166608;               // sulphur-stained skeleton (look, stats)
        private bool _called;
        private readonly List<GameNPC> _spirit = [];

        public ForgemasterBrungarBrain()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (Body.IsAlive && HasAggro && !_called && Body.TargetObject is GameLiving target)
            {
                _called = true;
                PrimaEncounter165.Say(Body, $"{Body.Name} calls upon the Spirit of the Forge!");
                var brain = new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500 };
                GameNPC spirit = PrimaEncounter165.SpawnAdd(Body, SpiritTemplate, "Spirit of the Forge", 70, Body.SpawnPoint.X + 100, Body.SpawnPoint.Y,
                    Body.SpawnPoint.Z, Body.Heading, brain, npc => npc.BodyType = 11);
                AddAggroListTo(brain);
                spirit.StartAttack(target);
                _spirit.Add(spirit);
            }
            else if (_called && !HasAggro && EpicEncounterMechanics.FightIsOver(Body))
            {
                PrimaEncounter165.RemoveAll(_spirit);
                _called = false;
            }
            base.Think();
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Weaponmaster Zargin (Dellingstad). Prima SI 2002 p.136: "able to summon and animate swords to assist him and his
    // students. ... expect a lot of BAF"; Allakhazam 2003 the same; 'Mastered Weapon' level 48, magical, thrust.
    // CHOICE: 3 swords when the fight starts (one per yard point), one more every 30 s while fewer than 3 are alive.
    public class WeaponmasterZarginBrain : StandardMobBrain
    {
        public const int SwordTemplate = 60157905;                // animate svard (look, stats)
        public const int Dellingstad = 155;
        public const int ResummonMilliseconds = 30_000;
        public static readonly (int X, int Y, int Z)[] Yard = [(363600, 322350, 6536), (363700, 322650, 6536), (363800, 322950, 6536)];
        private bool _fighting;
        private long _lastSummon;
        private readonly List<GameNPC> _swords = [];

        public WeaponmasterZarginBrain()
        {
            ThinkInterval = 1500;
        }

        public override void Think()
        {
            if (Body.IsAlive)
            {
                List<GameNPC> students = Body.GetNPCsInRadius(1500).Where(n => n.IsAlive && n.Name == "Zargin's student").ToList();
                // a student in a fight pulls Zargin in, and Zargin pulls all his students (BAF)
                foreach (GameNPC s in students)
                    if (s.Brain is StandardMobBrain sb && sb.HasAggro)
                        sb.AddAggroListTo(this);
                if (HasAggro && Body.TargetObject is GameLiving target)
                {
                    long now = GameLoop.GameLoopTime;
                    if (!_fighting)
                    {
                        _fighting = true;
                        foreach (GameNPC s in students)
                            if (s.Brain is StandardMobBrain sb)
                                AddAggroListTo(sb);
                        for (int i = 0; i < Yard.Length; i++)
                            Summon(Yard[i], target);
                        _lastSummon = now;
                    }
                    else if (now - _lastSummon >= ResummonMilliseconds && PrimaEncounter165.Alive(_swords) < Yard.Length)
                    {
                        Summon(Yard[Util.Random(0, Yard.Length - 1)], target);
                        _lastSummon = now;
                    }
                }
                else if (_fighting && EpicEncounterMechanics.FightIsOver(Body))
                {
                    PrimaEncounter165.RemoveAll(_swords);
                    _fighting = false;
                }
            }
            base.Think();
        }

        private void Summon((int X, int Y, int Z) at, GameLiving target)
        {
            var brain = new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500 };
            GameNPC sword = PrimaEncounter165.SpawnAdd(Body, SwordTemplate, "mastered weapon", 48, at.X, at.Y, at.Z,
                PrimaEncounter165.HeadingTo(at.X, at.Y, Body.X, Body.Y), brain, npc =>
                {
                    npc.Model = 667;
                    npc.BodyType = 8;                              // magical
                    npc.MeleeDamageType = eDamageType.Thrust;
                    npc.Faction = FactionMgr.GetFactionByID(Dellingstad);
                });
            AddAggroListTo(brain);
            sword.StartAttack(target);
            _swords.Add(sword);
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Panicked syhr'phint (Avalon City sewers). Allakhazam 2003-06-02: in the tunnel into Sys'sro's lair; if left alive
    // it runs into the lair and calls for help. Allakhazam 2003-05-23: if a scout spots you and runs for Sys'sro, mez it,
    // because everything for miles around will aggro you. It does not fight while it runs; mez/stun stop it.
    public class PanickedSyhrphintBrain : StandardMobBrain
    {
        public static readonly (int X, int Y, int Z)[] Route = [(40025, 42300, 8027), (40300, 41800, 8027), (40300, 39900, 8024), (41100, 39700, 8024)];
        private enum State { Idle, Fleeing, Alarmed }
        private State _state;
        private int _leg;
        private readonly List<GameLiving> _spotted = [];

        public PanickedSyhrphintBrain()
        {
            ThinkInterval = 1000;
        }

        public override void Think()
        {
            if (!Body.IsAlive)
                return;
            switch (_state)
            {
                case State.Idle:
                    if (HasAggro)
                    {
                        _spotted.Clear();
                        _spotted.AddRange(GetOrderedAggroList().Select(e => e.Living).Where(l => l != null));
                        _state = State.Fleeing;
                        _leg = 0;
                        Body.StopAttack();
                        Run();
                        return;
                    }
                    break;
                case State.Fleeing:
                    foreach (GameLiving l in GetOrderedAggroList().Select(e => e.Living))
                        if (l != null && !_spotted.Contains(l))
                            _spotted.Add(l);
                    Body.StopAttack();
                    (int x, int y, int z) = Route[_leg];
                    if (Body.IsWithinRadius(new Point3D(x, y, z), 150))
                    {
                        if (++_leg >= Route.Length)
                        {
                            Alarm();
                            break;
                        }
                    }
                    if (!Body.IsCrowdControlled && !Body.IsMoving)
                        Run();
                    return;                                        // no fighting while it runs for help
                case State.Alarmed:
                    if (!HasAggro && EpicEncounterMechanics.FightIsOver(Body))
                        _state = State.Idle;                       // walks home with the normal reset and re-arms
                    break;
            }
            base.Think();
        }

        private void Run()
        {
            (int x, int y, int z) = Route[Math.Min(_leg, Route.Length - 1)];
            Body.PathTo(new Point3D(x, y, z), Body.MaxSpeed);
        }

        private void Alarm()
        {
            _state = State.Alarmed;
            List<GameLiving> who = _spotted.Where(l => l != null && l.IsAlive).ToList();
            foreach (GamePlayer p in who.OfType<GamePlayer>())
                p.Out.SendMessage($"The {Body.Name} runs into the lair, crying for help!", eChatType.CT_Important, eChatLoc.CL_SystemWindow);
            foreach (GameNPC npc in Body.GetNPCsInRadius(3500))
            {
                if (!npc.IsAlive || npc.Brain is not StandardMobBrain sb || npc.Brain is PitMonsterBrain)
                    continue;
                bool lair = npc.Brain is SyssroRuthlessBrain || npc.PackageID == "SyssroBaf" ||
                            npc.Faction?.Id == 11 && npc.X is >= 39750 and <= 43000 && npc.Y is >= 38500 and <= 42750 && npc.Z is >= 7900 and <= 8150;
                if (!lair)
                    continue;
                foreach (GameLiving l in who)
                    sb.AddToAggroList(l, 1);
            }
            foreach (GameLiving l in who)
                AddToAggroList(l, 1);                              // then it fights like any syhr'phint
        }
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Ixthiar creeper (Vale of Balor). Prima SI 2002 'Enhanced Monster AI': "A creeper immediately leaves its brood the
    // moment it spawns. It then ventures out into the vale and starts spawning more ixthiar. If there are no players to
    // control the ixthiar, they will spread over 1/3 of the entire area." Allakhazam 2003: it wanders and spawns babies.
    // CHOICE (no period figures): legs of 1000-2500 units inside the ixthiar area of the vale, one ixthiar decimater
    // every 90-180 s while out of combat, at most 24 alive per creeper; after a fight it wanders on from where it is.
    public class IxthiarCreeperBrain : StandardMobBrain
    {
        public const int OffspringTemplate = 60162559;            // ixthiar decimater
        public const int MaxOffspring = 24;
        // the spread area in zone-local units (the vale's ixthiar wood plus a margin, about a third of the zone)
        public const int MinX = 8000, MaxX = 45000, MinY = 5000, MaxY = 37000;
        public const int AalidX = 49600, AalidY = 25000, AalidKeepOut = 4000;
        public const int NestX = 19500, NestY = 19700;
        private static readonly Random Rnd = new();
        private readonly List<GameNPC> _offspring = [];
        private long _nextSeed;
        private bool _leftBrood;

        public IxthiarCreeperBrain()
        {
            ThinkInterval = 2000;
        }

        /// <summary>Pure: keeps a zone-local point inside the spread area and away from the Grove of Aalid Feie.</summary>
        public static (int X, int Y) Clamp(int x, int y)
        {
            x = Math.Clamp(x, MinX, MaxX);
            y = Math.Clamp(y, MinY, MaxY);
            double d = Math.Sqrt((double)(x - AalidX) * (x - AalidX) + (double)(y - AalidY) * (y - AalidY));
            if (d < AalidKeepOut)
            {
                double k = AalidKeepOut / Math.Max(1, d);
                x = Math.Clamp(AalidX + (int)((x - AalidX) * k), MinX, MaxX);
                y = Math.Clamp(AalidY + (int)((y - AalidY) * k), MinY, MaxY);
            }
            return (x, y);
        }

        /// <summary>Pure: the next leg (zone-local); the first one leads away from the nest ("leaves its brood").</summary>
        public static (int X, int Y) NextLeg(int x, int y, bool first, Random rnd)
        {
            double a = first ? Math.Atan2(y - NestY, x - NestX) + (rnd.NextDouble() - 0.5) : rnd.NextDouble() * 2 * Math.PI;
            int len = first ? rnd.Next(1500, 3001) : rnd.Next(1000, 2501);
            return Clamp(x + (int)(len * Math.Cos(a)), y + (int)(len * Math.Sin(a)));
        }

        public override void Think()
        {
            if (!Body.IsAlive)
                return;
            Zone zone = Body.CurrentZone;
            long now = GameLoop.GameLoopTime;
            if (_nextSeed == 0)
                _nextSeed = now + Util.Random(90, 180) * 1000L;
            if (!HasAggro && !Body.InCombat && zone != null)
            {
                if (!Body.IsMoving)
                {
                    (int lx, int ly) = NextLeg(Body.X - zone.XOffset, Body.Y - zone.YOffset, !_leftBrood, Rnd);
                    _leftBrood = true;
                    Body.SpawnPoint = new Point3D(Body.X, Body.Y, Body.Z);   // after a fight it wanders on from here
                    Body.WalkTo(new Point3D(zone.XOffset + lx, zone.YOffset + ly, Body.Z), Body.MaxSpeed);
                }
                if (now >= _nextSeed)
                {
                    _nextSeed = now + Util.Random(90, 180) * 1000L;
                    if (PrimaEncounter165.Alive(_offspring) < MaxOffspring && !Body.GetNPCsInRadius(1500).Any(n => n.Realm != 0))
                    {
                        var brain = new PrimaAddBrain165 { AggroLevel = 100, AggroRange = 500, IdleRemoveMilliseconds = int.MaxValue };
                        GameNPC baby = PrimaEncounter165.SpawnAdd(Body, OffspringTemplate, "ixthiar decimater", (byte)Util.Random(39, 41),
                            Body.X + Util.Random(-100, 100), Body.Y + Util.Random(-100, 100), Body.Z, Body.Heading, brain,
                            npc => npc.RoamingRange = 200);
                        _offspring.Add(baby);
                    }
                }
                return;                                            // wandering, not the normal idle/return-home
            }
            base.Think();
        }
    }
}
