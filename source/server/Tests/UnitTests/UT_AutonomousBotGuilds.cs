using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using DOL.Database;
using NUnit.Framework;
using Planner = DOL.GS.AutonomousBotGuildPlanner;

namespace DOL.GS.Tests
{
    [TestFixture, NonParallelizable]
    public class UT_AutonomousBotGuilds
    {
        // Guild and GameObject load data from GameServer in their static constructors, so every test runs
        // with an empty server double. Without it, the first test to touch them breaks those types for the
        // whole test run.
        private static readonly IObjectDatabase EmptyDatabase = DispatchProxy.Create<IObjectDatabase, DOL.UnitTests.UT_UnobservedConcentration.EmptyReads>();
        private sealed class Server : GameServer { protected override IObjectDatabase DataBaseImpl => EmptyDatabase; }
        private GameServer _previousServer;

        [SetUp]
        public void InstallServerDouble()
        {
            _previousServer = GameServer.Instance;
            GameServer.LoadTestDouble((Server)RuntimeHelpers.GetUninitializedObject(typeof(Server)));
        }

        [TearDown]
        public void RestoreServer() => GameServer.LoadTestDouble(_previousServer);

        [TestCase(0, 0.7, 0)]
        [TestCase(10, 0.7, 7)]
        [TestCase(100, 0.7, 70)]
        [TestCase(100, 1.5, 100)]
        [TestCase(100, -1.0, 0)]
        [TestCase(100, double.NaN, 0)]
        public void MemberTarget_IsTheClampedShare(int eligible, double share, int expected)
        {
            Assert.That(Planner.MemberTarget(eligible, share), Is.EqualTo(expected));
        }

        [TestCase(0, 0)]
        [TestCase(7, 0)]
        [TestCase(8, 1)]
        [TestCase(22, 1)]
        [TestCase(40, 2)]
        [TestCase(700, 32)]
        [TestCase(100_000, Planner.MaximumGuildsPerRealm)]
        public void DesiredGuildCount_NoneBelowFoundingSize_AboutOnePer22(int memberTarget, int expected)
        {
            Assert.That(Planner.DesiredGuildCount(memberTarget), Is.EqualTo(expected));
        }

        [Test]
        public void DesiredGuildCount_NeverAsksForAGuildUnderFoundingSize()
        {
            for (int members = 0; members <= 3000; members++)
            {
                int count = Planner.DesiredGuildCount(members);
                Assert.That(count == 0 || members / count >= Planner.FoundingMembers, Is.True, $"{members} members, {count} guilds");
            }
        }

        [TestCase(1, 8)]
        [TestCase(3, 70)]
        [TestCase(32, 700)]
        [TestCase(60, 3000)]
        public void TargetSizes_AddUp_LargestFirst_AndNoneUnderEight(int guilds, int members)
        {
            int[] sizes = Planner.TargetSizes(guilds, members);
            Assert.Multiple(() =>
            {
                Assert.That(sizes.Length, Is.EqualTo(guilds));
                Assert.That(sizes.Sum(), Is.EqualTo(members));
                Assert.That(sizes.Min(), Is.GreaterThanOrEqualTo(Planner.FoundingMembers));
                Assert.That(sizes[0], Is.EqualTo(sizes.Max()), "The first guild is the biggest.");
                if (guilds > 1)
                    Assert.That(sizes[0], Is.GreaterThan(sizes[^1]), "A few big guilds and many small ones.");
            });
        }

        [Test]
        public void TargetSizes_EmptyInputs()
        {
            Assert.That(Planner.TargetSizes(0, 50), Is.Empty);
            Assert.That(Planner.TargetSizes(3, 0), Is.Empty);
        }

        [Test]
        public void Charters_FollowTheSizeOrderPattern()
        {
            Assert.That(Planner.CharterFor(0), Is.EqualTo(BotGuildCharter.Rvr));
            Assert.That(Planner.CharterFor(1), Is.EqualTo(BotGuildCharter.Leveling));
            Assert.That(Planner.CharterFor(2), Is.EqualTo(BotGuildCharter.Social));
            Assert.That(Planner.CharterFor(3), Is.EqualTo(BotGuildCharter.Raiding));
            var firstTwelve = Enumerable.Range(0, 12).Select(Planner.CharterFor).ToHashSet();
            Assert.That(firstTwelve, Is.EquivalentTo(Enum.GetValues<BotGuildCharter>()), "Every realm gets every charter.");
        }

        [TestCase(eRealm.Albion)]
        [TestCase(eRealm.Midgard)]
        [TestCase(eRealm.Hibernia)]
        public void Names_AreUniqueValidAndDeterministic(eRealm realm)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var first = new List<string>();
            for (int ordinal = 0; ordinal < 120; ordinal++)
            {
                string name = Planner.NameFor(realm, Planner.CharterFor(ordinal), ordinal, taken.Contains);
                Assert.That(Planner.NameFormat.IsMatch(name), Is.True, name);
                Assert.That(name.Length, Is.LessThanOrEqualTo(Planner.MaximumNameLength), name);
                Assert.That(taken.Add(name), Is.True, $"Duplicate name {name}");
                first.Add(name);
            }

            // The same inputs, with the same names already taken, give the same names again.
            var again = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int ordinal = 0; ordinal < 120; ordinal++)
            {
                string name = Planner.NameFor(realm, Planner.CharterFor(ordinal), ordinal, again.Contains);
                Assert.That(name, Is.EqualTo(first[ordinal]));
                again.Add(name);
            }
        }

        [Test]
        public void Names_StillComeWhenEveryCombinationIsTaken()
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string name = Planner.NameFor(eRealm.Albion, BotGuildCharter.Rvr, 5, candidate => !candidate.Contains(" II") && !candidate.Contains(" III")
                ? true
                : !taken.Add(candidate));
            Assert.That(name, Does.EndWith(" II"));
            Assert.That(Planner.NameFormat.IsMatch(name), Is.True, name);
        }

        [Test]
        public void Place_FillsTargetsWithoutOverflow_AndNeverMovesMembers()
        {
            var slots = new List<Planner.GuildSlot>
            {
                new("rvr", BotGuildCharter.Rvr, 0, 10, 4),
                new("lvl", BotGuildCharter.Leveling, 1, 8, 0),
            };
            var candidates = Enumerable.Range(1, 40).Select(i => new Planner.Candidate(i, 5 + i % 45)).ToList();
            List<Planner.Placement> placements = Planner.Place(candidates, slots, 100);

            Assert.Multiple(() =>
            {
                Assert.That(placements.Count, Is.EqualTo(14), "Only the open places (6 + 8) are filled.");
                Assert.That(slots[0].Count, Is.EqualTo(10));
                Assert.That(slots[1].Count, Is.EqualTo(8));
                Assert.That(placements.Select(p => p.BotId).Distinct().Count(), Is.EqualTo(placements.Count));
            });
        }

        [Test]
        public void Place_RespectsTheRequestedCount_AndIsDeterministic()
        {
            Planner.GuildSlot[] Slots() => [new("a", BotGuildCharter.Social, 0, 50, 0), new("b", BotGuildCharter.Leveling, 1, 50, 0)];
            var candidates = Enumerable.Range(1, 60).Select(i => new Planner.Candidate(i * 7, 20)).ToList();
            List<Planner.Placement> first = Planner.Place(candidates, Slots(), 12);
            List<Planner.Placement> second = Planner.Place(Enumerable.Reverse(candidates), Slots(), 12);
            Assert.That(first.Count, Is.EqualTo(12));
            Assert.That(second, Is.EqualTo(first), "Input order doesn't matter.");
        }

        [Test]
        public void Place_SendsHighLevelsToRvrAndLowLevelsToLeveling()
        {
            var slots = new List<Planner.GuildSlot>
            {
                new("rvr", BotGuildCharter.Rvr, 0, 1, 0),
                new("lvl", BotGuildCharter.Leveling, 1, 1, 0),
            };
            List<Planner.Placement> placements = Planner.Place([new Planner.Candidate(1, 48), new Planner.Candidate(2, 12)], slots, 2);
            var byBot = placements.ToDictionary(p => p.BotId, p => p.GuildId);
            Assert.That(byBot[1], Is.EqualTo("rvr"));
            Assert.That(byBot[2], Is.EqualTo("lvl"));
        }

        [Test]
        public void Place_NothingToDo()
        {
            Assert.That(Planner.Place([new Planner.Candidate(1, 10)], [], 5), Is.Empty);
            Assert.That(Planner.Place([new Planner.Candidate(1, 10)], [new("a", BotGuildCharter.Social, 0, 5, 0)], 0), Is.Empty);
        }

        [Test]
        public void Ranks_LeaderIsTheHighestLevel_OfficersOnePerTwelve()
        {
            var members = Enumerable.Range(1, 30).Select(i => new Planner.Member(i, i, Planner.LowestRank)).ToList();
            var changes = Planner.RankChanges(members);
            Assert.That(changes, Does.Contain((30L, 0)));
            Assert.That(changes.Count(c => c.Rank == 1), Is.EqualTo(2), "30 members: 2 officers.");
            Assert.That(changes.Where(c => c.Rank == 1).Select(c => c.BotId), Is.EquivalentTo(new long[] { 29, 28 }));
        }

        [Test]
        public void Ranks_AreStable_AndNobodyIsDemoted()
        {
            var members = new List<Planner.Member>
            {
                new(1, 10, 0), new(2, 50, Planner.LowestRank), new(3, 40, 1), new(4, 30, Planner.LowestRank),
            };
            Assert.That(Planner.RankChanges(members), Is.Empty, "A leader and an officer already exist.");
        }

        [Test]
        public void Ranks_SmallGuildsHaveNoOfficers_AndAnEmptyGuildNoChanges()
        {
            Assert.That(Planner.RankChanges([]), Is.Empty);
            var changes = Planner.RankChanges([new Planner.Member(1, 20, 9), new Planner.Member(2, 25, 9)]);
            Assert.That(changes, Is.EqualTo(new List<(long, int)> { (2, 0) }));
        }

        [Test]
        public void RankTitles_HaveTenRanksForEveryCharter()
        {
            foreach (BotGuildCharter charter in Enum.GetValues<BotGuildCharter>())
                Assert.That(Planner.RankTitles(charter), Has.Length.EqualTo(10), charter.ToString());
        }

        [TestCase(false, true, false, false, -1, 20, 20, Planner.InviteAnswer.WrongRealm)]
        [TestCase(true, false, false, false, -1, 20, 20, Planner.InviteAnswer.Dead)]
        [TestCase(true, true, true, false, -1, 20, 20, Planner.InviteAnswer.Busy)]
        [TestCase(true, true, false, true, -1, 20, 20, Planner.InviteAnswer.AlreadyInPlayerGuild)]
        [TestCase(true, true, false, false, 0, 20, 20, Planner.InviteAnswer.LeadsOwnGuild)]
        [TestCase(true, true, false, false, 2, 20, 20, Planner.InviteAnswer.LeadsOwnGuild)]
        [TestCase(true, true, false, false, 9, 31, 20, Planner.InviteAnswer.LevelGap)]
        [TestCase(true, true, false, false, 9, 30, 20, Planner.InviteAnswer.Accept)]
        [TestCase(true, true, false, false, -1, 15, 25, Planner.InviteAnswer.Accept)]
        public void Invite_Answers(bool sameRealm, bool alive, bool inCombat, bool inPlayerGuild, int rank, int botLevel,
            int inviterLevel, Planner.InviteAnswer expected)
        {
            Assert.That(Planner.AnswerInvite(sameRealm, alive, inCombat, inPlayerGuild, rank, botLevel, inviterLevel), Is.EqualTo(expected));
        }

        [Test]
        public void Order_IsAStableMix()
        {
            Assert.That(Planner.Order(42), Is.EqualTo(Planner.Order(42)));
            var ordered = Enumerable.Range(1, 100).OrderBy(i => Planner.Order(i)).ToList();
            Assert.That(ordered, Is.Not.EqualTo(Enumerable.Range(1, 100).ToList()), "Placement doesn't follow creation order.");
        }

        private static GameBot OfflineBot(string id, eRealm realm)
        {
            var bot = (GameBot)RuntimeHelpers.GetUninitializedObject(typeof(GameBot));
            // Uninitialized objects skip field initializers; the NPC realm getter reads the brain list.
            typeof(GameNPC).GetField("m_brains", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(bot, new ArrayList(1));
            bot.ObjectState = GameObject.eObjectState.Inactive;
            bot.Realm = realm;
            bot.InternalID = id;
            return bot;
        }

        private static Guild TestGuild(eRealm realm)
        {
            var guild = new Guild(new DbGuild { GuildName = "Test Guild", GuildID = "test-guild", Realm = (byte)realm });
            guild.Ranks = Enumerable.Range(0, 10).Select(i => new DbGuildRank { GuildID = "test-guild", RankLevel = (byte)i, Title = $"Rank {i}" }).ToArray();
            return guild;
        }

        [Test]
        public void GuildBotMembers_AddAndRemove()
        {
            Guild guild = TestGuild(eRealm.Albion);
            GameBot bot = OfflineBot("offlinebot:1", eRealm.Albion);

            Assert.That(guild.AddBotMember(bot, guild.GetRankByID(9)), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(bot.Guild, Is.SameAs(guild));
                Assert.That(bot.GuildName, Is.EqualTo("Test Guild"));
                Assert.That(bot.GuildRank.RankLevel, Is.EqualTo(9));
                Assert.That(guild.GetOnlineBotMembers(), Is.EqualTo(new[] { bot }));
                Assert.That(guild.MemberOnlineCount, Is.EqualTo(1));
            });

            Assert.That(guild.AddBotMember(bot, guild.GetRankByID(1)), Is.True, "Re-adding updates the rank.");
            Assert.That(bot.GuildRank.RankLevel, Is.EqualTo(1));
            Assert.That(guild.MemberOnlineCount, Is.EqualTo(1));

            Assert.That(guild.RemoveBotMember(bot), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(bot.Guild, Is.Null);
                Assert.That(bot.GuildRank, Is.Null);
                Assert.That(bot.GuildName, Is.Empty);
                Assert.That(guild.GetOnlineBotMembers(), Is.Empty);
                Assert.That(guild.RemoveBotMember(bot), Is.False);
            });
        }

        [Test]
        public void GuildBotMembers_RealmAndOtherGuildAreRefused()
        {
            Guild guild = TestGuild(eRealm.Albion);
            Assert.That(guild.AddBotMember(OfflineBot("offlinebot:2", eRealm.Midgard), guild.GetRankByID(9)), Is.False);
            Assert.That(guild.AddBotMember(OfflineBot("offlinebot:3", eRealm.Albion), null), Is.False);

            GameBot other = OfflineBot("offlinebot:4", eRealm.Albion);
            other.Guild = TestGuild(eRealm.Albion);
            Assert.That(guild.AddBotMember(other, guild.GetRankByID(9)), Is.False, "A bot in another guild must leave it first.");
        }

        // Records rank saves; any other write fails the test.
        public class RecordingWrites : DispatchProxy
        {
            public static readonly List<DataObject> Saved = new();

            protected override object Invoke(MethodInfo method, object[] args)
            {
                if (method.Name == nameof(IObjectDatabase.SaveObject) && args[0] is DataObject saved)
                {
                    Saved.Add(saved);
                    return true;
                }
                if (!method.Name.StartsWith("Select") && !method.Name.StartsWith("Find"))
                    throw new InvalidOperationException("Unexpected database write: " + method.Name);
                Type type = method.ReturnType;
                return type.IsGenericType && typeof(IEnumerable).IsAssignableFrom(type)
                    ? Activator.CreateInstance(typeof(List<>).MakeGenericType(type.GetGenericArguments()[0])) : null;
            }
        }

        private static readonly IObjectDatabase RecordingDatabase = DispatchProxy.Create<IObjectDatabase, RecordingWrites>();
        private sealed class RecordingServer : GameServer { protected override IObjectDatabase DataBaseImpl => RecordingDatabase; }

        private static bool MemberRanksHearAlliance(Guild guild) =>
            Enumerable.Range(7, 2).Select(level => guild.GetRankByID(level)).All(rank => rank.AcHear && rank.GcHear && rank.GcSpeak);

        [Test]
        public void StartupRewrites_BotGuildsOff_LeaveTheSavedGuildAsItIs()
        {
            Guild guild = TestGuild(eRealm.Albion);

            // The server double throws on any database write, so this also proves nothing was saved.
            Assert.That(AutonomousBotGuilds.ApplyStartupRewrites(guild, BotGuildCharter.Rvr, enabled: false), Is.False);
            Assert.Multiple(() =>
            {
                Assert.That(Enumerable.Range(7, 2).Select(level => guild.GetRankByID(level))
                    .Any(rank => rank.AcHear || rank.GcHear || rank.GcSpeak), Is.False, "Rank rights are not rewritten.");
                Assert.That(guild.IsGuildDuesOn(), Is.False, "Dues are not turned on.");
                Assert.That(guild.GetGuildDuesPercent(), Is.Zero);
            });
            Assert.That(AutonomousBotGuilds.ApplyStartupRewrites(null, BotGuildCharter.Rvr, enabled: true), Is.False);
        }

        [Test]
        public void StartupRewrites_BotGuildsOn_GrantMemberRightsAndCharterDuesOnce()
        {
            GameServer.LoadTestDouble((RecordingServer)RuntimeHelpers.GetUninitializedObject(typeof(RecordingServer)));
            RecordingWrites.Saved.Clear();
            Guild guild = TestGuild(eRealm.Albion);

            Assert.That(AutonomousBotGuilds.ApplyStartupRewrites(guild, BotGuildCharter.Rvr, enabled: true), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(MemberRanksHearAlliance(guild), Is.True);
                Assert.That(RecordingWrites.Saved, Is.EqualTo(new[] { guild.GetRankByID(7), guild.GetRankByID(8) }));
                Assert.That(guild.IsGuildDuesOn(), Is.True);
                Assert.That(guild.GetGuildDuesPercent(), Is.EqualTo(AutonomousBotGuildEconomy.DefaultDuesPercent(BotGuildCharter.Rvr)));
            });

            // A second startup saves nothing that is already right and keeps dues a player changed.
            RecordingWrites.Saved.Clear();
            guild.SetGuildDuesPercent(10);
            Assert.That(AutonomousBotGuilds.ApplyStartupRewrites(guild, BotGuildCharter.Rvr, enabled: true), Is.True);
            Assert.That(RecordingWrites.Saved, Is.Empty);
            Assert.That(guild.GetGuildDuesPercent(), Is.EqualTo(10));
        }
    }
}
