using System.Data.SQLite;
using System.Reflection;
using System.Text.RegularExpressions;
using NUnit.Framework;
using DAoCLivingServer.Launcher;

[TestFixture, NonParallelizable, Apartment(ApartmentState.STA)]
public class SocialExpansionSettingsTests
{
    private string _folder = null!, _database = null!;
    private bool _stopped;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    [SetUp] public void Setup()
    {
        _folder = Path.Combine(Path.GetTempPath(), "social-expansion-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_folder);
        _database = Path.Combine(_folder, "opendaoc.sqlite3.db");
        _stopped = true;
        // The world database's ServerProperty table: Key is the primary key and ServerProperty_ID is unique.
        Exec("""
            CREATE TABLE ServerProperty (Category TEXT NOT NULL, `Key` TEXT NOT NULL PRIMARY KEY, Description TEXT NOT NULL,
                DefaultValue TEXT NOT NULL, Value TEXT NOT NULL, LastTimeRowUpdated TEXT, ServerProperty_ID TEXT NOT NULL UNIQUE);
            INSERT INTO ServerProperty VALUES ('rates', 'xp_rate', 'flat', '1', '1', '', '');
            """);
    }

    [TearDown] public void Cleanup()
    {
        SQLiteConnection.ClearAllPools();
        if (Directory.Exists(_folder)) Directory.Delete(_folder, true);
    }

    private void Exec(string sql)
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private Dictionary<string, string> Rows()
    {
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = "SELECT `Key`, Value FROM ServerProperty";
        using var reader = command.ExecuteReader();
        var rows = new Dictionary<string, string>();
        while (reader.Read()) rows[reader.GetString(0)] = reader.GetString(1);
        return rows;
    }

    private static readonly SocialExpansionSettings AllOn = new()
    {
        BotGuilds = true, GuildSharePercent = 60, GuildMinLevel = 10, Personalities = true, Voices = true,
        GuildAlliances = false, PlayerXpByLevel = SocialExpansionSettings.SuggestedPlayerXp,
        BotXpByLevel = SocialExpansionSettings.SuggestedBotXp,
        LivingMemory = true, LivingGrats = true, LivingFirsts = true, LivingDeath = true, LivingAlarms = true,
        LivingNemesis = true, ScheduleMode = "clock", LivingBreaks = true, LivingBodyLanguage = true, LivingBeggars = true,
        LivingDeeds = true, LivingStorylines = true, LivingCharacters = true, LivingRumours = true,
        LivingHolidays = true, LivingLeeroy = true,
    };

    [Test] public void NoRowsMeansTheServerDefaults()
    {
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(SocialExpansionSettings.Defaults));
        Assert.That(problems, Is.Empty);
        // Since Living Server 1.2 (owner 2026-11-16: "all our living server features should be in by default if someone
        // downloads our installer") every feature is on with no row: guilds, personalities, every Server life part and the bot
        // crafters with their orders. The XP ranges are rates, not switches: empty (Setup sets them). The bot schedule is off
        // since 1.2.1 (owner 2026-11-21: "set the server settings so the 'bot schedule' based on time or sessions is off by
        // default"): the whole roster online.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.Voices && d.LivingMemory && d.LivingGrats && d.LivingBeggars, Is.True);
        Assert.That(d.BotGuilds && d.Personalities && d.LivingFirsts && d.LivingDeath && d.LivingAlarms && d.LivingNemesis &&
                    d.LivingBreaks && d.LivingBodyLanguage && d.BotCrafters && d.BotCraftOrders, Is.True);
        Assert.That(d.ScheduleMode, Is.EqualTo("off"));
        Assert.That(d.ManaHalvedBelowHalf || d.CraftingSecondaryCaps || d.CraftingClassicQuality || d.CraftingClassicLosses, Is.False,
            "the classic rules stay off");
        Assert.That(d.PlayerXpByLevel + d.BotXpByLevel, Is.Empty);
    }

    [Test] public void RecommendedTurnsEveryFeatureOnAndSavesAsIs()
    {
        // Setup applies these on a first install (owner 2026-10-28): every switch on, the suggested XP ranges. The power
        // regeneration halving is a classic rule, not a feature: off, as the 1.1.2 default (owner 2026-11-03).
        var r = SocialExpansionSettings.Recommended;
        // Crafting 2.0: the classic crafting options (secondary caps, classic quality, classic losses) are rules, off by
        // default like the halving (design decision 11).
        string[] classicRules =
        [
            nameof(SocialExpansionSettings.ManaHalvedBelowHalf), nameof(SocialExpansionSettings.CraftingSecondaryCaps),
            nameof(SocialExpansionSettings.CraftingClassicQuality), nameof(SocialExpansionSettings.CraftingClassicLosses),
            // Not a feature switch: marking bots as bots stays off so they look like players (owner decision 2026-11-24, #485).
            nameof(SocialExpansionSettings.ShowBotsAsBots),
        ];
        var switches = typeof(SocialExpansionSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(bool) && p.CanWrite && !classicRules.Contains(p.Name)).ToArray();
        Assert.That(switches.Length, Is.GreaterThanOrEqualTo(24));
        Assert.That(switches.Where(p => !(bool)p.GetValue(r)!).Select(p => p.Name), Is.Empty);
        Assert.That(r.PlayerXpByLevel, Is.EqualTo(SocialExpansionSettings.SuggestedPlayerXp));
        Assert.That(r.BotXpByLevel, Is.EqualTo(SocialExpansionSettings.SuggestedBotXp));
        Assert.That(r.ScheduleMode, Is.EqualTo("off"), "the bot schedule is off for a first install too (owner 2026-11-21)");
        Assert.That((r.ManaRegenModifier, r.ManaHalvedBelowHalf), Is.EqualTo((1.5, false)));
        Assert.That((r.BotCrafters, r.BotCraftOrders, r.ExchangeTaxPercent, r.BotCrafterSharePercent), Is.EqualTo((true, true, 0, 7)),
            "bot crafters and their orders are on for a first install too (owner 2026-11-16)");
        Assert.That(r.Validate(), Is.Null);
        r.Save(_database, null, () => true);
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(r));
        Assert.That(problems, Is.Empty);
    }

    [Test] public void SaveWritesWhatTheServerReadsAndLoadsItBack()
    {
        AllOn.Save(_database, null, () => _stopped);
        var rows = Rows();
        Assert.That(rows["bot_guilds_enabled"], Is.EqualTo("True"));
        Assert.That(rows["bot_guild_share"], Is.EqualTo("0.6"));
        Assert.That(rows["bot_guild_min_level"], Is.EqualTo("10"));
        Assert.That(rows["bot_guild_alliances"], Is.EqualTo("False"));
        Assert.That(rows["bot_personalities_enabled"], Is.EqualTo("True"));
        Assert.That(rows["xp_rate_by_level"], Is.EqualTo("1-19:3;20-34:5;35-50:12"));
        Assert.That(rows["xp_rate"], Is.EqualTo("1"), "the flat rate is not touched");
        Assert.That(rows["bot_living_memory"], Is.EqualTo("True"));
        Assert.That(rows["bot_schedule_mode"], Is.EqualTo("clock"));
        Assert.That(rows["bot_living_breaks"], Is.EqualTo("True"));
        Assert.That(rows["bot_living_body_language"], Is.EqualTo("True"));
        Assert.That(rows["bot_living_beggars"], Is.EqualTo("True"));
        Assert.That(rows["bot_living_deeds"] + rows["bot_living_storylines"] + rows["bot_living_characters"] + rows["bot_living_rumours"],
            Is.EqualTo("TrueTrueTrueTrue"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(AllOn));
        Assert.That(problems, Is.Empty);
    }

    [Test] public void SaveOnlyWritesWhatChanged()
    {
        Exec("INSERT INTO ServerProperty VALUES ('guild', 'bot_guild_share', 'd', '', '0.655', '', 'bot_guild_share')");
        var loaded = SocialExpansionSettings.Load(_database, new List<string>());
        Assert.That(loaded.GuildSharePercent, Is.EqualTo(66));
        (loaded with { Personalities = false }).Save(_database, loaded, () => _stopped);
        var rows = Rows();
        Assert.That(rows["bot_guild_share"], Is.EqualTo("0.655"), "an untouched setting keeps its exact value");
        Assert.That(rows["bot_personalities_enabled"], Is.EqualTo("False"));
        Assert.That(rows.ContainsKey("bot_guilds_enabled"), Is.False);
    }

    /// <summary>
    /// From 1.2 a save clears DefaultValue on a row the server wrote, so the server's settings upgrade (which only moves a
    /// row whose Value and DefaultValue are both the old default) never overrides a choice made in this tab.
    /// </summary>
    [Test] public void ASaveMarksTheRowAsThePlayersChoice()
    {
        Exec("INSERT INTO ServerProperty VALUES ('bots', 'bot_crafters', 'd', 'True', 'True', '2026-11-01 10:00:00', 'bot_crafters')");
        var loaded = SocialExpansionSettings.Load(_database, new List<string>());
        (loaded with { BotCrafters = false }).Save(_database, loaded, () => _stopped);
        using var c = new SQLiteConnection($"Data Source={_database};Pooling=False");
        c.Open();
        using var command = c.CreateCommand();
        command.CommandText = "SELECT Value, DefaultValue FROM ServerProperty WHERE `Key`='bot_crafters'";
        using var reader = command.ExecuteReader();
        Assert.That(reader.Read(), Is.True);
        Assert.That((reader.GetString(0), reader.GetString(1)), Is.EqualTo(("False", "")));
    }

    [Test] public void BotLeadersInvitesAndTheLootShareAreSavedReadAndChecked()
    {
        // Living Server 1.2, bot-led groups: invites on and a loot share of 2 by default (owner 2026-11-04).
        Assert.That((SocialExpansionSettings.Defaults.BotGroupInvites, SocialExpansionSettings.Defaults.PlayerLootWeight), Is.EqualTo((true, 2)));
        (SocialExpansionSettings.Defaults with { BotGroupInvites = false, PlayerLootWeight = 3 }).Save(_database, null, () => _stopped);
        var rows = Rows();
        Assert.That((rows["bot_group_invite_player"], rows["bot_group_player_loot_weight"]), Is.EqualTo(("False", "3")));
        var problems = new List<string>();
        var loaded = SocialExpansionSettings.Load(_database, problems);
        Assert.That((loaded.BotGroupInvites, loaded.PlayerLootWeight), Is.EqualTo((false, 3)));
        Assert.That(problems, Is.Empty);
        Assert.That((SocialExpansionSettings.Defaults with { PlayerLootWeight = 9 }).Validate(), Does.Contain("loot share"));
        Assert.That(SocialExpansionSettings.Defaults.AllOff().BotGroupInvites, Is.False);
        Exec("UPDATE ServerProperty SET Value = 'x' WHERE `Key` = 'bot_group_player_loot_weight'");
        problems.Clear();
        Assert.That(SocialExpansionSettings.Load(_database, problems).PlayerLootWeight, Is.EqualTo(2), "the server uses its default (1.2.3)");
        Assert.That(problems, Has.Count.EqualTo(1));
    }

    /// <summary>
    /// 1.2.3, from a r/daoc report (no endurance regeneration at all, Tireless and songs included): the endurance rate is in
    /// the tab (1 by default, 1 to 10), an unreadable or too-low row is reported and repaired by a save, and the tab shows it.
    /// </summary>
    [Test] public void TheEnduranceRateIsSavedReadCheckedAndRepaired()
    {
        var d = SocialExpansionSettings.Defaults;
        Assert.That((d.EnduranceRegenModifier, SocialExpansionSettings.Recommended.EnduranceRegenModifier), Is.EqualTo((1.0, 1.0)));
        Assert.That(d.ToStoredValues()["endurance_regen_amount_modifier"], Is.EqualTo("1"));
        Assert.That(d.AllOff().EnduranceRegenModifier, Is.EqualTo(1.0), "ALL OFF leaves the rates alone");
        Assert.That((d with { EnduranceRegenModifier = 0.5 }).Validate(), Does.Contain("endurance regeneration"));
        Assert.That((d with { EnduranceRegenModifier = 11 }).Validate(), Does.Contain("endurance regeneration"));
        (d with { EnduranceRegenModifier = 1.5 }).Save(_database, null, () => _stopped);
        Assert.That(Rows()["endurance_regen_amount_modifier"], Is.EqualTo("1.5"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).EnduranceRegenModifier, Is.EqualTo(1.5));
        Assert.That(problems, Is.Empty);

        foreach (var (stored, shown) in new[] { ("", 1.0), ("fast", 1.0), ("0", 1.0), ("0.2", 1.0), ("25", 10.0) })
        {
            Exec($"UPDATE ServerProperty SET Value='{stored}' WHERE `Key`='endurance_regen_amount_modifier'");
            problems.Clear();
            var keys = new HashSet<string>();
            var loaded = SocialExpansionSettings.Load(_database, problems, keys);
            if (stored == "")
            {
                Assert.That((loaded.EnduranceRegenModifier, problems.Count), Is.EqualTo((1.0, 0)), "a blank row keeps the default");
                continue;
            }
            Assert.That(loaded.EnduranceRegenModifier, Is.EqualTo(shown), stored);
            Assert.That(keys, Is.EquivalentTo(new[] { "endurance_regen_amount_modifier" }), stored);
            if (stored is "0" or "0.2")
                Assert.That(problems[0], Does.Contain("regenerates no endurance"), stored);
            if (stored == "fast")
                Assert.That(problems[0], Does.Contain("uses its default 1"));
            loaded.Save(_database, loaded, () => _stopped, keys);
            problems.Clear();
            Assert.That(SocialExpansionSettings.Load(_database, problems).EnduranceRegenModifier, Is.EqualTo(shown), stored);
            Assert.That(problems, Is.Empty, "SAVE SETTINGS repaired " + stored);
        }

        var type = Assembly.Load("DAoCLivingServer").GetType("DAoCLivingServer.Launcher.SocialExpansionSettingsControl")!;
        Exec("UPDATE ServerProperty SET Value='2.5' WHERE `Key`='endurance_regen_amount_modifier'");
        using var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
        var rate = (NumericUpDown)type.GetField("_enduranceRate", Hidden)!.GetValue(panel)!;
        Assert.That(rate.Value, Is.EqualTo(2.5m));
        rate.Value = 3m;
        type.GetMethod("SaveSettings", Hidden)!.Invoke(panel, null);
        Assert.That(Rows()["endurance_regen_amount_modifier"], Is.EqualTo("3"));
    }

    [Test] public void TheJoinModeIsSavedReadAndChecked()
    {
        // Living Server 1.2, bot-led groups B2: follow by default (owner 2026-11-04, decision 1).
        Assert.That(SocialExpansionSettings.Defaults.JoinMode, Is.EqualTo("follow"));
        (SocialExpansionSettings.Defaults with { JoinMode = "lead" }).Save(_database, null, () => _stopped);
        Assert.That(Rows()["bot_led_group_join_mode"], Is.EqualTo("lead"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).JoinMode, Is.EqualTo("lead"));
        Assert.That(problems, Is.Empty);
        Assert.That((SocialExpansionSettings.Defaults with { JoinMode = "lurk" }).Validate(), Does.Contain("follow or lead"));
        Assert.That(SocialExpansionSettings.Defaults.AllOff().JoinMode, Is.EqualTo("follow"), "a choice, not a feature: all off keeps it");
        Exec("UPDATE ServerProperty SET Value = 'LEAD ' WHERE `Key` = 'bot_led_group_join_mode'");
        Assert.That(SocialExpansionSettings.Load(_database, problems).JoinMode, Is.EqualTo("lead"));
        Exec("UPDATE ServerProperty SET Value = 'x' WHERE `Key` = 'bot_led_group_join_mode'");
        problems.Clear();
        Assert.That(SocialExpansionSettings.Load(_database, problems).JoinMode, Is.EqualTo("follow"));
        Assert.That(problems, Has.Count.EqualTo(1));
    }

    [Test] public void RaidLeadersInvitesAreSavedReadAndTurnedOffWithAllOff()
    {
        // Living Server 1.2, bot-led groups B3: on by default.
        Assert.That(SocialExpansionSettings.Defaults.BotRaidInvites, Is.True);
        (SocialExpansionSettings.Defaults with { BotRaidInvites = false }).Save(_database, null, () => _stopped);
        Assert.That(Rows()["bot_raid_invite_player"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).BotRaidInvites, Is.False);
        Assert.That(problems, Is.Empty);
        Assert.That(SocialExpansionSettings.Defaults.AllOff().BotRaidInvites, Is.False);
    }

    [Test] public void SightSizeUpIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #662 (owner 2026-11-26): bot_rvr_sight_size_up, a bot combat rule rather than a Living Server life feature, on by
        // default and in Setup; no checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.SightSizeUp && SocialExpansionSettings.Recommended.SightSizeUp, Is.True);
        Assert.That(d.AllOff().SightSizeUp, Is.True);
        (d with { SightSizeUp = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_rvr_sight_size_up"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).SightSizeUp, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void BotsAsPeopleSwitchesAreOnSavedReadAndTurnedOffWithAllOff()
    {
        // Bots as people (docs/BOTS-AS-PEOPLE-1.2.md, "Settings"): the card's four master switches, on by default and in Setup.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.BotSkillProfiles && d.BotPlaceKnowledge && d.BotMovementHabits && d.LivingMilestones, Is.True);
        var r = SocialExpansionSettings.Recommended;
        Assert.That(r.BotSkillProfiles && r.BotPlaceKnowledge && r.BotMovementHabits && r.LivingMilestones, Is.True);
        (d with { BotSkillProfiles = false, LivingMilestones = false }).Save(_database, d, () => _stopped);
        var rows = Rows();
        Assert.That((rows["bot_skill_profiles"], rows["bot_living_milestones"]), Is.EqualTo(("False", "False")));
        Assert.That(rows.ContainsKey("bot_place_knowledge"), Is.False, "an unchanged default is not written");
        var problems = new List<string>();
        var loaded = SocialExpansionSettings.Load(_database, problems);
        Assert.That((loaded.BotSkillProfiles, loaded.BotPlaceKnowledge, loaded.BotMovementHabits, loaded.LivingMilestones),
            Is.EqualTo((false, true, true, false)));
        Assert.That(problems, Is.Empty);
        var off = d.AllOff();
        Assert.That(off.AnyBotsAsPeople, Is.False);
        Assert.That(off.IsAllOff, Is.True);
        Assert.That((off with { BotMovementHabits = true }).IsAllOff, Is.False);
    }

    [Test] public void BardHealerSlotIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #567 B7 (owner 2026-11-26): bot_bard_fills_healer_slot, a bot combat rule rather than a Living Server life feature,
        // on by default and in Setup; no checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.BardFillsHealerSlot && SocialExpansionSettings.Recommended.BardFillsHealerSlot, Is.True);
        Assert.That(d.AllOff().BardFillsHealerSlot, Is.True);
        (d with { BardFillsHealerSlot = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_bard_fills_healer_slot"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).BardFillsHealerSlot, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void HealerPeriodRulesAreOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #567 B1-B5 (owner 2026-11-26): bot_healer_period_rules, a bot combat rule rather than a Living Server life feature,
        // on by default and in Setup; no checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.HealerPeriodRules && SocialExpansionSettings.Recommended.HealerPeriodRules, Is.True);
        Assert.That(d.AllOff().HealerPeriodRules, Is.True);
        (d with { HealerPeriodRules = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_healer_period_rules"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).HealerPeriodRules, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void PartyMainAssistIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #569 step 6: bot_party_main_assist, a bot combat rule like bot_leader_focus, on by default and in Setup; no
        // checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.PartyMainAssist && SocialExpansionSettings.Recommended.PartyMainAssist, Is.True);
        Assert.That(d.AllOff().PartyMainAssist, Is.True);
        (d with { PartyMainAssist = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_party_main_assist"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).PartyMainAssist, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void WeaponSetRulesAreOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #569 step 7: bot_weapon_set_rules, a bot combat rule like bot_healer_period_rules, on by default and in Setup;
        // no checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.WeaponSetRules && SocialExpansionSettings.Recommended.WeaponSetRules, Is.True);
        Assert.That(d.AllOff().WeaponSetRules, Is.True);
        (d with { WeaponSetRules = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_weapon_set_rules"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).WeaponSetRules, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void LeaderFocusIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #571 step 1: bot_leader_focus, a bot combat rule like bot_healer_period_rules, on by default and in Setup; no
        // checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.LeaderFocus && SocialExpansionSettings.Recommended.LeaderFocus, Is.True);
        Assert.That(d.AllOff().LeaderFocus, Is.True);
        (d with { LeaderFocus = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_leader_focus"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).LeaderFocus, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void CompanionRvrEngageIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #664 step 1: bot_companion_rvr_engage, a bot combat rule like bot_leader_focus, on by default and in Setup; no
        // checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.CompanionRvrEngage && SocialExpansionSettings.Recommended.CompanionRvrEngage, Is.True);
        Assert.That(d.AllOff().CompanionRvrEngage, Is.True);
        (d with { CompanionRvrEngage = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_companion_rvr_engage"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).CompanionRvrEngage, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void CriticalShotOpenerIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #569 step 8: bot_critical_shot_opener, a bot combat rule like bot_leader_focus, on by default and in Setup; no
        // checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.CriticalShotOpener && SocialExpansionSettings.Recommended.CriticalShotOpener, Is.True);
        Assert.That(d.AllOff().CriticalShotOpener, Is.True);
        (d with { CriticalShotOpener = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_critical_shot_opener"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).CriticalShotOpener, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void PartyPullerCampTurnIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #569 step 9: bot_party_puller_camp_turn, a bot combat rule like bot_leader_focus, on by default and in Setup; no
        // checkbox, so the tab keeps what it read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.PartyPullerCampTurn && SocialExpansionSettings.Recommended.PartyPullerCampTurn, Is.True);
        Assert.That(d.AllOff().PartyPullerCampTurn, Is.True);
        (d with { PartyPullerCampTurn = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_party_puller_camp_turn"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).PartyPullerCampTurn, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void MorganaQuestHelpIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // #611 (owner decision 2026-11-25): bot_morgana_quest_help, bots in the player's group fight Morgana's demons when the
        // player's epic event starts. A bot combat rule: on by default and in Setup; no checkbox, so the tab keeps what it
        // read, and ALL OFF leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.MorganaQuestHelp && SocialExpansionSettings.Recommended.MorganaQuestHelp, Is.True);
        Assert.That(d.AllOff().MorganaQuestHelp, Is.True);
        (d with { MorganaQuestHelp = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_morgana_quest_help"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).MorganaQuestHelp, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void DeathFallIsOnByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // 1.2.1 (owner 2026-11-21, "Death animations need fixed absolutely"): bot_death_animation, a display fix in the
        // original client, on by default and in Setup; ALL OFF (the classic server's features) leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.DeathFall && SocialExpansionSettings.Recommended.DeathFall, Is.True);
        Assert.That(d.AllOff().DeathFall, Is.True);
        (d with { DeathFall = false }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_death_animation"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).DeathFall, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void ShowBotsAsBotsIsOffByDefaultSavedReadAndLeftAloneByAllOff()
    {
        // 1.2.3 (owner 2026-11-24, #485): the [Playerbot] tag in /who and "gamebot"/"buffbot" in refusals are a launcher
        // setting, hidden by default, in Setup too; ALL OFF (a display choice, not a feature) leaves it as it is.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.ShowBotsAsBots || SocialExpansionSettings.Recommended.ShowBotsAsBots, Is.False);
        Assert.That((d with { ShowBotsAsBots = true }).AllOff().ShowBotsAsBots, Is.True);
        (d with { ShowBotsAsBots = true }).Save(_database, d, () => _stopped);
        Assert.That(Rows()["bot_show_as_bots"], Is.EqualTo("True"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).ShowBotsAsBots, Is.True);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void RaidLearningIsOnByDefaultAndAllOffTurnsItOff()
    {
        // Bots as people WP7 (coordinator's decision, 2026-11-17): ALL OFF is the classic server, so it also turns
        // raid_learns_from_raiders off, a server key under no master switch.
        var d = SocialExpansionSettings.Defaults;
        Assert.That(d.RaidLearnsFromRaiders && SocialExpansionSettings.Recommended.RaidLearnsFromRaiders, Is.True);
        var off = d.AllOff();
        Assert.That(off.RaidLearnsFromRaiders, Is.False);
        Assert.That(off.IsAllOff, Is.True);
        Assert.That((off with { RaidLearnsFromRaiders = true }).IsAllOff, Is.False, "raid learning on is not ALL OFF");
        off.Save(_database, d, () => _stopped);
        Assert.That(Rows()["raid_learns_from_raiders"], Is.EqualTo("False"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).RaidLearnsFromRaiders, Is.False);
        Assert.That(problems, Is.Empty);
    }

    [Test] public void TheChatRateIsSavedReadAndChecked()
    {
        // Beta 1.1.2: bot_voices_chat_rate in the tab; 2 by default (owner 2026-11-04, after the playtest).
        Assert.That(SocialExpansionSettings.Defaults.ChatRate, Is.EqualTo(2.0));
        Assert.That(SocialExpansionSettings.ChatRateDescription, Does.Contain("2 (the default)").And.Not.Contain("quiet"));
        (SocialExpansionSettings.Defaults with { ChatRate = 2.5 }).Save(_database, null, () => _stopped);
        Assert.That(Rows()["bot_voices_chat_rate"], Is.EqualTo("2.5"));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems).ChatRate, Is.EqualTo(2.5));
        Assert.That(problems, Is.Empty);
        var bad = new Dictionary<string, string?> { ["bot_voices_chat_rate"] = "7" };
        var keys = new HashSet<string>();
        Assert.That(SocialExpansionSettings.FromStoredValues(bad, problems, keys).ChatRate, Is.EqualTo(3.0));
        Assert.That(keys, Does.Contain("bot_voices_chat_rate"));
        Assert.That(SocialExpansionSettings.FromStoredValues(new Dictionary<string, string?> { ["bot_voices_chat_rate"] = "lots" }, problems).ChatRate,
            Is.EqualTo(2.0), "what the server uses: its default (1.2.3; before, 0)");
    }

    [Test] public void UnreadableValuesAreReportedAndShownAsDefaults()
    {
        Exec("""
            INSERT INTO ServerProperty VALUES ('guild', 'bot_guilds_enabled', 'd', '', 'maybe', '', 'bot_guilds_enabled');
            INSERT INTO ServerProperty VALUES ('guild', 'bot_guild_share', 'd', '', '1.5', '', 'bot_guild_share');
            INSERT INTO ServerProperty VALUES ('rates', 'bot_xp_rate_by_level', 'd', '', '50-1:2', '', 'bot_xp_rate_by_level');
            """);
        var problems = new List<string>();
        var keys = new HashSet<string>();
        var loaded = SocialExpansionSettings.Load(_database, problems, keys);
        Assert.That(loaded.BotGuilds, Is.True, "the server uses its default for an unreadable bool (1.2.3; before, False)");
        Assert.That(loaded.GuildSharePercent, Is.EqualTo(100), "a share above 1 shows as 100%");
        Assert.That(problems, Has.Count.EqualTo(2));
        Assert.That(keys, Is.EquivalentTo(new[] { "bot_guilds_enabled", "bot_guild_share" }));
        Assert.That(loaded.BotXpByLevel, Is.EqualTo("50-1:2"), "an invalid range setting is kept so its error can be shown");
        Assert.That(loaded.Validate(), Does.Contain("ends before it starts"));
    }

    [Test] public void LivingServerScheduleIsReadLikeTheServerAndAllOffSwitchesEveryPartOff()
    {
        Exec("INSERT INTO ServerProperty VALUES ('bots', 'bot_schedule_mode', 'd', '', 'sometimes', '', 'bot_schedule_mode')");
        var problems = new List<string>();
        var keys = new HashSet<string>();
        var loaded = SocialExpansionSettings.Load(_database, problems, keys);
        Assert.That(loaded.ScheduleMode, Is.EqualTo("off"), "the server reads an unknown mode as off");
        Assert.That(keys, Is.EquivalentTo(new[] { "bot_schedule_mode" }));
        Assert.That(AllOn with { ScheduleMode = "weekly" }, Has.Property("IsAllOff").False);
        Assert.That((AllOn with { ScheduleMode = "weekly" }).Validate(), Does.Contain("off, session or clock"));
        var off = AllOn.AllOff();
        Assert.That(off.AnyLiving, Is.False);
        Assert.That(off.IsAllOff, Is.True);
        Assert.That((SocialExpansionSettings.Defaults with { ScheduleMode = "session" }).IsAllOff, Is.False);
        Assert.That((SocialExpansionSettings.Defaults with { LivingBodyLanguage = true }).IsAllOff, Is.False);
        Assert.That(off.LivingBodyLanguage, Is.False);
        Assert.That(off.LivingBeggars || off.LivingDeeds || off.LivingStorylines || off.LivingCharacters || off.LivingRumours, Is.False);
        Assert.That(off.LivingHolidays || off.LivingLeeroy, Is.False);
        Assert.That((SocialExpansionSettings.Defaults.AllOff() with { LivingBeggars = true }).IsAllOff, Is.False);
    }

    [Test] public void NothingIsWrittenWhileTheServerRunsOrWithInvalidRanges()
    {
        _stopped = false;
        Assert.Throws<InvalidOperationException>(() => AllOn.Save(_database, null, () => _stopped));
        _stopped = true;
        Assert.Throws<InvalidOperationException>(() => (AllOn with { PlayerXpByLevel = "1-19:3;10-20:2" }).Save(_database, null, () => _stopped));
        Assert.Throws<InvalidOperationException>(() => (AllOn with { BotXpByLevel = "1-50:0" }).Save(_database, null, () => _stopped));
        Assert.That(Rows().Keys, Is.EquivalentTo(new[] { "xp_rate" }));
    }

    [Test] public void RepairRewritesOnlyTheBadRowsAndAllOffKeepsTheChoices()
    {
        Exec("""
            INSERT INTO ServerProperty VALUES ('guild', 'bot_guilds_enabled', 'd', '', 'maybe', '', 'bot_guilds_enabled');
            INSERT INTO ServerProperty VALUES ('guild', 'bot_guild_share', 'd', '', '0.655', '', 'bot_guild_share');
            """);
        var keys = new HashSet<string>();
        var loaded = SocialExpansionSettings.Load(_database, new List<string>(), keys);
        loaded.Save(_database, loaded, () => _stopped, keys);
        var rows = Rows();
        Assert.That(rows["bot_guilds_enabled"], Is.EqualTo("True"), "the unreadable row is repaired, to the default the server uses (1.2.3)");
        Assert.That(rows["bot_guild_share"], Is.EqualTo("0.655"), "a readable row is not rewritten by a repair");
        Assert.That(rows.ContainsKey("bot_personalities_enabled"), Is.False);

        var custom = AllOn with { GuildSharePercent = 40, GuildMinLevel = 20, GuildClaims = false };
        var off = custom.AllOff();
        Assert.That(off.IsAllOff, Is.True);
        Assert.That((off.GuildSharePercent, off.GuildMinLevel, off.GuildClaims, off.Debates), Is.EqualTo((40, 20, false, true)),
            "ALL OFF switches the features off but keeps the player's share, level and parts");
    }

    [Test] public void AFailedLoadNeverSavesOverTheRealSettings()
    {
        Exec("DROP TABLE ServerProperty");
        var type = Assembly.Load("DAoCLivingServer").GetType("DAoCLivingServer.Launcher.SocialExpansionSettingsControl")!;
        using var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
        var save = (Button)type.GetField("_save", Hidden)!.GetValue(panel)!;
        ((CheckBox)type.GetField("_guilds", Hidden)!.GetValue(panel)!).Checked = true;
        Assert.That(save.Enabled, Is.False, "nothing can be saved over settings that couldn't be read");
        Assert.That(type.GetProperty("HasUnsavedChanges")!.GetValue(panel), Is.False, "the start guard isn't blocked by a failed read");
        Exec("""
            CREATE TABLE ServerProperty (Category TEXT NOT NULL, `Key` TEXT NOT NULL PRIMARY KEY, Description TEXT NOT NULL,
                DefaultValue TEXT NOT NULL, Value TEXT NOT NULL, LastTimeRowUpdated TEXT, ServerProperty_ID TEXT NOT NULL UNIQUE);
            INSERT INTO ServerProperty VALUES ('guild', 'bot_personalities_enabled', 'd', '', 'True', '', 'bot_personalities_enabled');
            """);
        type.GetMethod("SaveSettings", Hidden)!.Invoke(panel, null);
        Assert.That(Rows().Keys, Is.EquivalentTo(new[] { "bot_personalities_enabled" }), "SaveSettings refuses after a failed read");
        ((Button)type.GetField("_undo", Hidden)!.GetValue(panel)!).PerformClick();
        Assert.That(((CheckBox)type.GetField("_personalities", Hidden)!.GetValue(panel)!).Checked, Is.True, "UNDO EDITS reads them again");
    }

    /// <summary>Living Server 1.1.2 (owner 2026-11-03, a r/daoc report): bots in your guild, and the power regeneration rate.</summary>
    [Test] public void PowerRegenAndBotsInYourGuildAreSavedAndReadLikeTheServer()
    {
        var d = SocialExpansionSettings.Defaults;
        Assert.That((d.PlayerGuildEconomy, d.ManaRegenModifier, d.ManaHalvedBelowHalf), Is.EqualTo((true, 1.5, false)));
        var classic = d with { PlayerGuildEconomy = false, ManaRegenModifier = 1, ManaHalvedBelowHalf = true };
        classic.Save(_database, d, () => _stopped);
        var rows = Rows();
        Assert.That((rows["bot_player_guild_economy"], rows["mana_regen_amount_modifier"], rows["mana_regen_amount_halved_below_50_percent"]),
            Is.EqualTo(("False", "1", "True")));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(classic));
        Assert.That(problems, Is.Empty);
        Assert.That(classic.AllOff() is { ManaRegenModifier: 1, ManaHalvedBelowHalf: true }, Is.True, "ALL OFF leaves the rates alone");

        (classic with { ManaRegenModifier = 1.75 }).Save(_database, classic, () => _stopped);
        Assert.That(Rows()["mana_regen_amount_modifier"], Is.EqualTo("1.75"));
        Assert.That((d with { ManaRegenModifier = 0.1 }).Validate(), Does.Contain("power regeneration"));
        Assert.That((d with { ManaRegenModifier = 11 }).Validate(), Does.Contain("power regeneration"));

        Exec("UPDATE ServerProperty SET Value='fast' WHERE `Key`='mana_regen_amount_modifier'");
        var keys = new HashSet<string>();
        problems.Clear();
        var bad = SocialExpansionSettings.Load(_database, problems, keys);
        Assert.That(keys, Is.EquivalentTo(new[] { "mana_regen_amount_modifier" }));
        Assert.That(bad.ManaRegenModifier, Is.EqualTo(SocialExpansionSettings.DefaultManaRegenModifier), "the server uses its default (1.2.3; before, 0)");
        Exec("UPDATE ServerProperty SET Value='25' WHERE `Key`='mana_regen_amount_modifier'");
        Assert.That(SocialExpansionSettings.Load(_database, new List<string>()).ManaRegenModifier, Is.EqualTo(SocialExpansionSettings.MaxManaRegenModifier));

        // The tab shows and saves them.
        var type = Assembly.Load("DAoCLivingServer").GetType("DAoCLivingServer.Launcher.SocialExpansionSettingsControl")!;
        Exec("UPDATE ServerProperty SET Value='1.5' WHERE `Key`='mana_regen_amount_modifier'");
        using var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
        var rate = (NumericUpDown)type.GetField("_manaRate", Hidden)!.GetValue(panel)!;
        var halved = (CheckBox)type.GetField("_manaHalved", Hidden)!.GetValue(panel)!;
        var inYourGuild = (CheckBox)type.GetField("_playerGuildEconomy", Hidden)!.GetValue(panel)!;
        Assert.That((rate.Value, halved.Checked, inYourGuild.Checked), Is.EqualTo((1.5m, true, false)));
        rate.Value = 2m;
        halved.Checked = false;
        inYourGuild.Checked = true;
        type.GetMethod("SaveSettings", Hidden)!.Invoke(panel, null);
        rows = Rows();
        Assert.That((rows["bot_player_guild_economy"], rows["mana_regen_amount_modifier"], rows["mana_regen_amount_halved_below_50_percent"]),
            Is.EqualTo(("True", "2", "False")));
    }

    /// <summary>
    /// Crafting 2.0, the player's side (WP3): era start, secondary caps, crafting pace, repair upkeep, coin by level and the
    /// two classic options are saved as the server reads them, loaded back, checked, kept by ALL OFF, and shown and saved
    /// by the tab.
    /// </summary>
    [Test] public void CraftingRowsAreSavedReadAndChecked()
    {
        var d = SocialExpansionSettings.Defaults;
        Assert.That((d.CraftingEraStart, d.CraftingSecondaryCaps, d.CraftingPace, d.PlayerUpkeep, d.PlayerCoinByLevel, d.CraftingClassicQuality, d.CraftingClassicLosses),
            Is.EqualTo((true, false, 1.0, "light", true, false, false)));
        var classic = d with
        {
            CraftingEraStart = false, CraftingSecondaryCaps = true, CraftingPace = 3, PlayerUpkeep = "classic",
            PlayerCoinByLevel = false, CraftingClassicQuality = true, CraftingClassicLosses = true,
        };
        Assert.That(classic.Validate(), Is.Null);
        classic.Save(_database, d, () => _stopped);
        var rows = Rows();
        Assert.That((rows["crafting_era_start"], rows["crafting_secondary_caps"], rows["crafting_skill_rate"], rows["player_upkeep"],
                rows["player_coin_by_level"], rows["crafting_classic_quality"], rows["crafting_classic_losses"]),
            Is.EqualTo(("False", "True", "3", "classic", "False", "True", "True")));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(classic));
        Assert.That(problems, Is.Empty);
        var off = classic.AllOff();
        Assert.That((off.CraftingEraStart, off.CraftingSecondaryCaps, off.CraftingPace, off.PlayerUpkeep, off.CraftingClassicLosses),
            Is.EqualTo((false, true, 3.0, "classic", true)), "ALL OFF leaves the crafting rules alone");

        // Only what changed is written.
        (classic with { PlayerUpkeep = "free" }).Save(_database, classic, () => _stopped);
        Assert.That(Rows()["player_upkeep"], Is.EqualTo("free"));

        Assert.That((d with { CraftingPace = 0 }).Validate(), Does.Contain("crafting pace"));
        Assert.That((d with { CraftingPace = 11 }).Validate(), Does.Contain("crafting pace"));
        Assert.That((d with { CraftingPace = 1.5 }).Validate(), Does.Contain("whole number"));
        Assert.That((d with { PlayerUpkeep = "cheap" }).Validate(), Does.Contain("classic, light or free"));

        // Unreadable rows: reported, shown as what the server then uses, and repaired by a save.
        Exec("""
            UPDATE ServerProperty SET Value='maybe' WHERE `Key`='crafting_era_start';
            UPDATE ServerProperty SET Value='fast' WHERE `Key`='crafting_skill_rate';
            UPDATE ServerProperty SET Value='Expensive' WHERE `Key`='player_upkeep';
            """);
        var keys = new HashSet<string>();
        problems.Clear();
        var bad = SocialExpansionSettings.Load(_database, problems, keys);
        Assert.That(keys, Is.EquivalentTo(new[] { "crafting_era_start", "crafting_skill_rate", "player_upkeep" }));
        Assert.That((bad.CraftingEraStart, bad.CraftingPace, bad.PlayerUpkeep), Is.EqualTo((true, 1.0, "light")),
            "the server keeps its defaults (era start on, pace 1) and reads an unknown upkeep as light");
        Exec("UPDATE ServerProperty SET Value='25' WHERE `Key`='crafting_skill_rate'; UPDATE ServerProperty SET Value=' FREE ' WHERE `Key`='player_upkeep'");
        problems.Clear();
        var clamped = SocialExpansionSettings.Load(_database, problems);
        Assert.That((clamped.CraftingPace, clamped.PlayerUpkeep), Is.EqualTo((10.0, "free")));
        Assert.That(problems, Has.Count.EqualTo(2), "the pace out of range, era start still unreadable");

        // The tab shows and saves them.
        Exec("""
            UPDATE ServerProperty SET Value='True' WHERE `Key`='crafting_era_start';
            UPDATE ServerProperty SET Value='2' WHERE `Key`='crafting_skill_rate';
            UPDATE ServerProperty SET Value='free' WHERE `Key`='player_upkeep';
            """);
        var type = Assembly.Load("DAoCLivingServer").GetType("DAoCLivingServer.Launcher.SocialExpansionSettingsControl")!;
        using var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
        T Field<T>(string name) => (T)type.GetField(name, Hidden)!.GetValue(panel)!;
        Assert.That((Field<CheckBox>("_craftEraStart").Checked, Field<NumericUpDown>("_craftPace").Value, Field<ComboBox>("_upkeep").SelectedIndex,
                Field<CheckBox>("_craftSecondaryCaps").Checked, Field<CheckBox>("_coinByLevel").Checked),
            Is.EqualTo((true, 2m, 2, true, false)));
        Field<CheckBox>("_craftEraStart").Checked = false;
        Field<NumericUpDown>("_craftPace").Value = 4m;
        Field<ComboBox>("_upkeep").SelectedIndex = 1;
        Field<CheckBox>("_coinByLevel").Checked = true;
        Field<CheckBox>("_classicQuality").Checked = false;
        type.GetMethod("SaveSettings", Hidden)!.Invoke(panel, null);
        rows = Rows();
        Assert.That((rows["crafting_era_start"], rows["crafting_skill_rate"], rows["player_upkeep"], rows["player_coin_by_level"], rows["crafting_classic_quality"]),
            Is.EqualTo(("False", "4", "light", "True", "False")));
    }

    /// <summary>
    /// Crafting 2.0, the gamebots' side and the economy (WP9): bot crafters and their share, pace, orders, commissions, posts and
    /// chat, the Exchange tax, the ledger, the level-50 kit and the No Lifers' buffbot share and range are saved as the server
    /// reads them, loaded back, checked, switched off (crafters and orders only) by ALL OFF, and shown and saved by the tab.
    /// </summary>
    [Test] public void EconomyRowsAreSavedReadAndChecked()
    {
        var d = SocialExpansionSettings.Defaults;
        Assert.That((d.BotCrafters, d.BotCrafterSharePercent, d.BotCraftingPace, d.BotCraftOrders, d.RealmCommissions, d.BotCrafterExchangeListings),
            Is.EqualTo((true, 7, "xp", true, "doubled", 6)), "crafters and orders on by default since 1.2 (owner 2026-11-16)");
        Assert.That((d.BotCrafterChatRate, d.ExchangeTaxPercent, d.EconomyLedger, d.Level50Kit, d.BuffBotPercent, d.BuffBotEraConcRange),
            Is.EqualTo((1.0, 0, true, true, 30, true)));
        var on = d with
        {
            BotCrafters = true, BotCrafterSharePercent = 12, BotCraftingPace = "classic", BotCraftOrders = true, RealmCommissions = "off",
            BotCrafterExchangeListings = 9, BotCrafterChatRate = 0.5, ExchangeTaxPercent = 5, EconomyLedger = false, Level50Kit = false,
            BuffBotPercent = 50, BuffBotEraConcRange = false,
        };
        Assert.That(on.Validate(), Is.Null);
        on.Save(_database, null, () => _stopped);   // every row, the defaults (crafters and orders on) included
        var rows = Rows();
        Assert.That((rows["bot_crafters"], rows["bot_crafter_share_percent"], rows["bot_crafting_pace"], rows["bot_craft_orders"],
                rows["realm_commissions"], rows["bot_crafter_exchange_listings"]),
            Is.EqualTo(("True", "12", "classic", "True", "off", "9")));
        Assert.That((rows["bot_crafter_chat_rate"], rows["exchange_tax_percent"], rows["economy_ledger_enabled"], rows["launcher_level50_kit"],
                rows["bot_nolifer_buffbot_percent"], rows["bot_nolifer_buffbot_era_conc_range"]),
            Is.EqualTo(("0.5", "5", "False", "False", "50", "False")));
        var problems = new List<string>();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(on));
        Assert.That(problems, Is.Empty);

        var off = on.AllOff();
        Assert.That((off.BotCrafters, off.BotCraftOrders), Is.EqualTo((false, false)), "ALL OFF switches bot crafters and orders off");
        Assert.That((off.ExchangeTaxPercent, off.EconomyLedger, off.Level50Kit, off.BotCrafterSharePercent, off.BuffBotPercent),
            Is.EqualTo((5, false, false, 12, 50)), "and keeps the tax, the ledger, the kit and the shares as chosen");
        Assert.That((d with { BotCrafters = true }).IsAllOff, Is.False);

        Assert.That((d with { BotCrafterSharePercent = 101 }).Validate(), Does.Contain("crafters among new bots"));
        Assert.That((d with { BotCraftingPace = "fast" }).Validate(), Does.Contain("xp or classic"));
        Assert.That((d with { RealmCommissions = "tripled" }).Validate(), Does.Contain("classic, doubled or off"));
        Assert.That((d with { BotCrafterExchangeListings = 2 }).Validate(), Does.Contain("3 to 100"));
        Assert.That((d with { BotCrafterChatRate = 4 }).Validate(), Does.Contain("0 to 3"));
        Assert.That((d with { ExchangeTaxPercent = 51 }).Validate(), Does.Contain("0 to 50"));
        Assert.That((d with { BuffBotPercent = -1 }).Validate(), Does.Contain("buffbot"));

        // Unreadable and out-of-range rows: reported, shown as what the server then uses, and repaired by a save.
        Exec("""
            UPDATE ServerProperty SET Value='lots' WHERE `Key`='bot_crafter_share_percent';
            UPDATE ServerProperty SET Value='200' WHERE `Key`='exchange_tax_percent';
            UPDATE ServerProperty SET Value='1' WHERE `Key`='bot_crafter_exchange_listings';
            UPDATE ServerProperty SET Value='turbo' WHERE `Key`='bot_crafting_pace';
            UPDATE ServerProperty SET Value='NONE' WHERE `Key`='realm_commissions';
            UPDATE ServerProperty SET Value='x' WHERE `Key`='bot_nolifer_buffbot_percent';
            UPDATE ServerProperty SET Value='nah' WHERE `Key`='bot_crafters';
            """);
        var keys = new HashSet<string>();
        problems.Clear();
        var bad = SocialExpansionSettings.Load(_database, problems, keys);
        Assert.That(keys, Is.EquivalentTo(new[] { "bot_crafter_share_percent", "exchange_tax_percent", "bot_crafter_exchange_listings",
            "bot_crafting_pace", "bot_nolifer_buffbot_percent", "bot_crafters" }), "none is read as off by the server too, so it is no problem");
        Assert.That((bad.BotCrafterSharePercent, bad.ExchangeTaxPercent, bad.BotCrafterExchangeListings, bad.BotCraftingPace, bad.RealmCommissions,
                bad.BuffBotPercent, bad.BotCrafters),
            Is.EqualTo((7, 50, 3, "xp", "off", 30, true)),
            "the server keeps its defaults (7, 30 buffbots, crafters on), clamps the tax and posts, reads xp");
        bad.Save(_database, bad, () => _stopped, keys);
        problems.Clear();
        Assert.That(SocialExpansionSettings.Load(_database, problems), Is.EqualTo(bad));
        Assert.That(problems, Is.Empty, "SAVE SETTINGS repaired the rows");

        // The tab shows and saves them.
        var type = Assembly.Load("DAoCLivingServer").GetType("DAoCLivingServer.Launcher.SocialExpansionSettingsControl")!;
        using var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
        T Field<T>(string name) => (T)type.GetField(name, Hidden)!.GetValue(panel)!;
        Assert.That((Field<CheckBox>("_botCrafters").Checked, Field<NumericUpDown>("_crafterShare").Value, Field<ComboBox>("_commissions").SelectedIndex,
                Field<NumericUpDown>("_exchangeTax").Value, Field<CheckBox>("_level50Kit").Checked, Field<NumericUpDown>("_buffBotPercent").Value),
            Is.EqualTo((true, 7m, 2, 50m, false, 30m)));
        Color muted = Color.FromArgb(166, 154, 128), parchment = Color.FromArgb(215, 199, 157);
        Assert.That(Field<CheckBox>("_craftOrders").ForeColor, Is.EqualTo(parchment), "craft orders are bright while bot crafters are on");
        Field<CheckBox>("_botCrafters").Checked = false;
        Assert.That(Field<CheckBox>("_craftOrders").ForeColor, Is.EqualTo(muted), "craft orders are dimmed while bot crafters are off");
        Field<CheckBox>("_botCrafters").Checked = true;
        Assert.That(Field<CheckBox>("_craftOrders").ForeColor, Is.EqualTo(parchment));
        Field<CheckBox>("_craftOrders").Checked = true;
        Field<NumericUpDown>("_exchangeTax").Value = 5m;
        Field<ComboBox>("_botCraftPace").SelectedIndex = 1;
        Field<NumericUpDown>("_crafterChat").Value = 2.5m;
        Field<CheckBox>("_level50Kit").Checked = true;
        Field<NumericUpDown>("_buffBotPercent").Value = 30m;
        type.GetMethod("SaveSettings", Hidden)!.Invoke(panel, null);
        rows = Rows();
        Assert.That((rows["bot_crafters"], rows["bot_craft_orders"], rows["exchange_tax_percent"], rows["bot_crafting_pace"], rows["bot_crafter_chat_rate"],
                rows["launcher_level50_kit"], rows["bot_nolifer_buffbot_percent"]),
            Is.EqualTo(("True", "True", "5", "classic", "2.5", "True", "30")));
    }

    /// <summary>The keys and defaults here must be the server's (ServerProperties.cs), or the tab would show the wrong state.</summary>
    [Test] public void KeysAndDefaultsMatchTheServer()
    {
        string? dir = TestContext.CurrentContext.TestDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "source/server/GameServer/serverproperty/ServerProperties.cs")))
            dir = Path.GetDirectoryName(dir);
        if (dir == null) Assert.Ignore("Server source not found beside the tests.");
        // Crafting 2.0's settings are declared in CraftingEconomyProperties (WP0), in the same attribute form.
        // Hired companions' switch is declared beside their code (HiredCompanionsMode, 1.2; in the tab since the chat audit).
        string source = File.ReadAllText(Path.Combine(dir!, "source/server/GameServer/serverproperty/ServerProperties.cs")) +
                        File.ReadAllText(Path.Combine(dir!, "source/server/GameServer/craft/economy/CraftingEconomyProperties.cs")) +
                        File.ReadAllText(Path.Combine(dir!, "source/server/GameServer/mode165/HiredCompanionsMode.cs"));
        var defaults = SocialExpansionSettings.Defaults.ToStoredValues();
        foreach (var (key, category, _) in typeof(SocialExpansionSettings)
                     .GetField("Settings", BindingFlags.Static | BindingFlags.NonPublic)!
                     .GetValue(null) as (string Key, string Category, string Description)[] ?? [])
        {
            var match = Regex.Match(source, $"\\[ServerProperty\\(\"(\\w+)\", \"{key}\", \"(?:[^\"\\\\]|\\\\.)*\", ([^)]*)\\)\\]");
            Assert.That(match.Success, Is.True, key + " is not a server setting");
            Assert.That(match.Groups[1].Value, Is.EqualTo(category), key + " category");
            string serverDefault = match.Groups[2].Value.Trim().Trim('"');
            string expected = serverDefault switch
            {
                "true" => "True",
                "false" => "False",
                _ when double.TryParse(serverDefault, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double d)
                    => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture),
                _ => serverDefault,
            };
            Assert.That(defaults[key], Is.EqualTo(expected), key + " default");
        }
    }

    [Test] public void TheTabFollowsTheServerStateAndDependencies()
    {
        var type = Assembly.Load("DAoCLivingServer").GetType("DAoCLivingServer.Launcher.SocialExpansionSettingsControl")!;
        using var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
        T Field<T>(string name) => (T)type.GetField(name, Hidden)!.GetValue(panel)!;
        void Call(string name) => type.GetMethod(name, Hidden | BindingFlags.Public)!.Invoke(panel, null);

        var guilds = Field<CheckBox>("_guilds");
        var claims = Field<CheckBox>("_guildClaims");
        var voices = Field<CheckBox>("_voices");
        var debates = Field<CheckBox>("_debates");
        var save = Field<Button>("_save");
        Color muted = Color.FromArgb(166, 154, 128), parchment = Color.FromArgb(215, 199, 157);
        // Since 1.2 bot guilds are on by default, so their parts start bright and dim when bot guilds are switched off.
        // Since Beta 1.1 voices are on by default, so debates start bright and dim when voices are switched off.
        Assert.That(guilds.Checked, Is.True, "bot guilds are on by default");
        Assert.That(claims.ForeColor, Is.EqualTo(parchment));
        Assert.That(voices.Checked, Is.True, "bot voices are on by default");
        Assert.That(debates.ForeColor, Is.EqualTo(parchment));
        Assert.That(save.Enabled, Is.False, "nothing to save yet");
        guilds.Checked = false;
        Assert.That(claims.ForeColor, Is.EqualTo(muted), "guild parts are dimmed until bot guilds are on");
        voices.Checked = false;
        Assert.That(debates.ForeColor, Is.EqualTo(muted), "debates are dimmed until voices are on");

        guilds.Checked = true;
        voices.Checked = true;
        Assert.That(claims.ForeColor, Is.EqualTo(parchment));
        Assert.That(debates.ForeColor, Is.EqualTo(parchment));
        Assert.That(save.Enabled, Is.False, "back on the defaults: nothing to save");
        Field<CheckBox>("_personalities").Checked = false;
        Assert.That(save.Enabled, Is.True);
        Assert.That(new[] { guilds, claims, voices, debates }.All(box => box.Enabled), Is.True, "switches are never disabled (unreadable on the dark theme)");

        var playerXp = type.GetField("_playerXp", Hidden)!.GetValue(panel)!;
        var mode = (ComboBox)playerXp.GetType().GetField("_mode", Hidden)!.GetValue(playerXp)!;
        var text = (TextBox)playerXp.GetType().GetField("_text", Hidden)!.GetValue(playerXp)!;
        mode.SelectedIndex = 2;
        text.Text = "1-19:3;15-30:2";
        Assert.That(save.Enabled, Is.False, "overlapping ranges can't be saved");
        mode.SelectedIndex = 1;
        Assert.That(text.Text, Is.EqualTo(SocialExpansionSettings.SuggestedPlayerXp));

        _stopped = false;
        Call("UpdateState");
        Assert.That(save.Enabled, Is.False, "nothing is saved while the server runs");
        Assert.That(Field<Label>("_status").Text, Does.StartWith("SERVER RUNNING"));
        Call("SaveSettings");
        Assert.That(Rows().ContainsKey("bot_personalities_enabled"), Is.False);

        _stopped = true;
        Call("UpdateState");
        Call("SaveSettings");
        var rows = Rows();
        Assert.That(rows["bot_personalities_enabled"], Is.EqualTo("False"));
        // Guilds and voices were switched off and on again: still the default (on since 1.2 and Beta 1.1), so nothing is written.
        Assert.That(rows.ContainsKey("bot_guilds_enabled") || rows.ContainsKey("bot_voices_enabled"), Is.False);
        Assert.That(rows["xp_rate_by_level"], Is.EqualTo(SocialExpansionSettings.SuggestedPlayerXp));
        Assert.That(type.GetProperty("HasUnsavedChanges")!.GetValue(panel), Is.False);

        Field<Button>("_allOff").PerformClick();
        Call("SaveSettings");
        rows = Rows();
        Assert.That(rows["bot_guilds_enabled"], Is.EqualTo("False"));
        Assert.That(rows["bot_voices_enabled"], Is.EqualTo("False"), "ALL OFF switches the new default off too");
        Assert.That(rows["xp_rate_by_level"], Is.EqualTo(""));
    }

    /// <summary>
    /// Renders the real launcher (Options, Bot Goals Setting and Living Server tabs) and the tab in several
    /// states to PNG files in TestResults' work folder (ui-preview\), so the look can be checked against the
    /// other settings screens. CI uploads them as an artifact.
    /// </summary>
    [Test] public void RendersPreviewsBesideTheOtherSettingsTabs()
    {
        string folder = Path.Combine(TestContext.CurrentContext.WorkDirectory, "ui-preview");
        Directory.CreateDirectory(folder);
        var launcher = Assembly.Load("DAoCLivingServer");

        var form = (Form)Activator.CreateInstance(launcher.GetType("DAoCLivingServer.Launcher.MainForm")!)!;
        try
        {
            form.ShowInTaskbar = false;
            form.StartPosition = FormStartPosition.Manual;
            form.Location = new Point(-20_000, -20_000);
            form.Show();
            Application.DoEvents();
            TabControl tabs = All(form).OfType<TabControl>().First(t => t.TabPages.Cast<TabPage>().Any(p => p.Text == "Living Server"));
            foreach (var (tab, file) in new[] { ("Options", "1-options.png"), ("Bot Goals Setting", "2-bot-goals.png"), ("Living Server", "3-living-server-in-launcher.png") })
            {
                tabs.SelectedTab = tabs.TabPages.Cast<TabPage>().First(p => p.Text == tab);
                Application.DoEvents();
                using var image = new Bitmap(form.Width, form.Height);
                form.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
                image.Save(Path.Combine(folder, file));
            }
        }
        finally
        {
            DisposeWhenSettled(form, TimeSpan.FromSeconds(30));
        }

        var type = launcher.GetType("DAoCLivingServer.Launcher.SocialExpansionSettingsControl")!;
        void Render(string file, Action<Control> setup, bool bottom = false, int scroll = 0)
        {
            using var host = new Form { ShowInTaskbar = false, StartPosition = FormStartPosition.Manual, Location = new Point(-20_000, -20_000),
                ClientSize = new Size(1180, 1060), BackColor = Color.FromArgb(35, 31, 26) };
            var panel = (Control)Activator.CreateInstance(type, _database, (Func<bool>)(() => _stopped))!;
            host.Controls.Add(panel);
            host.Show();
            setup(panel);
            type.GetMethod("UpdateState")!.Invoke(panel, null);
            Application.DoEvents();
            if (scroll > 0)
            {
                var scroller = panel.Controls.OfType<Panel>().First(c => c.AutoScroll);
                scroller.AutoScrollPosition = new Point(0, scroll);
                Application.DoEvents();
            }
            if (bottom)
            {
                // The CI desktop is small: show the XP card by scrolling the cards to the end.
                var scroller = panel.Controls.OfType<Panel>().First(c => c.AutoScroll);
                scroller.AutoScrollPosition = new Point(0, 10_000);
                Application.DoEvents();
            }
            using var image = new Bitmap(host.ClientSize.Width, host.ClientSize.Height);
            panel.DrawToBitmap(image, new Rectangle(Point.Empty, image.Size));
            image.Save(Path.Combine(folder, file));
        }
        T F<T>(Control panel, string name) => (T)type.GetField(name, Hidden)!.GetValue(panel)!;
        void Xp(Control panel, string row, int mode, string? text = null)
        {
            var xp = type.GetField(row, Hidden)!.GetValue(panel)!;
            ((ComboBox)xp.GetType().GetField("_mode", Hidden)!.GetValue(xp)!).SelectedIndex = mode;
            if (text != null) ((TextBox)xp.GetType().GetField("_text", Hidden)!.GetValue(xp)!).Text = text;
        }

        Render("4-tab-all-off.png", _ => { });
        Render("5-tab-editing.png", panel =>
        {
            F<CheckBox>(panel, "_guilds").Checked = true;
            F<CheckBox>(panel, "_voices").Checked = true;
            F<CheckBox>(panel, "_personalities").Checked = true;
            Xp(panel, "_playerXp", 1);
            Xp(panel, "_botXp", 2, "1-19:1.5;15-34:2.5");
        });
        Render("5b-tab-editing-xp.png", panel =>
        {
            Xp(panel, "_playerXp", 1);
            Xp(panel, "_botXp", 2, "1-19:1.5;15-34:2.5");
        }, bottom: true);
        Render("5c-tab-editing-living.png", panel =>
        {
            F<CheckBox>(panel, "_memory").Checked = true;
            F<CheckBox>(panel, "_death").Checked = true;
            F<ComboBox>(panel, "_schedule").SelectedIndex = 2;
        }, scroll: 600);
        // Crafting 2.0 (WP9): the bot crafters and economy card, below the crafting card.
        Render("5d-tab-editing-economy.png", panel =>
        {
            F<CheckBox>(panel, "_botCrafters").Checked = true;
            F<NumericUpDown>(panel, "_exchangeTax").Value = 5m;
        }, scroll: 2400);
        AllOn.Save(_database, null, () => true);
        Render("6-tab-saved-on.png", _ => { });
        Render("6c-tab-saved-on-living.png", _ => { }, scroll: 600);
        Render("6b-tab-saved-on-xp.png", _ => { }, bottom: true);
        _stopped = false;
        Render("7-tab-server-running.png", _ => { });
        TestContext.Progress.WriteLine("UI previews: " + folder);
    }

    /// <summary>
    /// Disposes a shown launcher window once it has settled. When shown, the launcher starts its own work (the dashboard
    /// and Realm Exchange reads, then the update check), and Dispose throws "Dispose() cannot be called while doing
    /// CreateHandle()" when it meets a control whose window handle is still being created (CI, 2026-11-05, twice; a
    /// re-run passed). So the test pumps the message loop until that start-up work has reached its timers and no control
    /// is creating its handle, and if Dispose still meets one, pumps again and retries, naming the control in the test
    /// log. Only the test waits; the launcher is unchanged.
    /// </summary>
    private static void DisposeWhenSettled(Form form, TimeSpan timeout)
    {
        DateTime until = DateTime.UtcNow + timeout;
        void Pump() { Application.DoEvents(); Thread.Sleep(20); }
        // The launcher starts its auto-refresh timer once the Shown handler's reads are done (MainForm's Shown).
        var autoRefresh = form.GetType().GetField("_autoRefresh", Hidden)?.GetValue(form) as System.Windows.Forms.Timer;
        DateTime readsUntil = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (autoRefresh is { Enabled: false } && DateTime.UtcNow < readsUntil) Pump();
        for (int i = 0; i < 5; i++) Pump();
        for (int attempt = 1; ; attempt++)
        {
            while (CreatingHandle(form) != null && DateTime.UtcNow < until) Pump();
            try
            {
                form.Dispose();
                return;
            }
            catch (InvalidOperationException e) when (e.Message.Contains("CreateHandle") && attempt < 20 && DateTime.UtcNow < until)
            {
                Control busy = CreatingHandle(form);
                TestContext.Progress.WriteLine($"Dispose met a control still creating its handle ({(busy == null ? "not found" : busy.GetType().Name + " '" + busy.Name + "'")}); " +
                                               $"pumping and trying again (attempt {attempt}).");
                for (int i = 0; i < 10; i++) Pump();
            }
        }
    }

    // Control.GetState(States.CreatingHandle), read by reflection: null when this WinForms has no such state to read.
    private static readonly MethodInfo GetStateMethod = typeof(Control).GetMethods(Hidden)
        .FirstOrDefault(m => m.Name == "GetState" && m.GetParameters() is [{ ParameterType.IsEnum: true }] && m.ReturnType == typeof(bool));
    private static readonly object CreatingHandleState = GetStateMethod != null &&
        Enum.TryParse(GetStateMethod.GetParameters()[0].ParameterType, "CreatingHandle", out object state) ? state : null;

    /// <summary>The first control under root (root included) whose window handle is being created now, or null.</summary>
    private static Control CreatingHandle(Control root)
    {
        if (CreatingHandleState == null) return null;
        try
        {
            return new[] { root }.Concat(All(root)).FirstOrDefault(c => (bool)GetStateMethod.Invoke(c, [CreatingHandleState])!);
        }
        catch (InvalidOperationException)
        {
            return null; // the control tree changed while it was read (another thread); the retry still covers it
        }
    }

    private static IEnumerable<Control> All(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (Control nested in All(child)) yield return nested;
        }
    }
}
