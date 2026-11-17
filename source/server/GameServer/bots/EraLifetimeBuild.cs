using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using OfflineDaoc.Shared;

namespace DOL.GS
{
    /// <summary>How a gamebot's lifetime plan was chosen at its first load (ERA_BUILD_ASSIGNED draw=).</summary>
    public enum EraBuildDraw
    {
        /// <summary>One of its class's era builds, by weight, seeded by the bot id.</summary>
        Era,
        /// <summary>The off-meta share (bot_era_offmeta_percent): one of today's class-file builds.</summary>
        OffMeta,
        /// <summary>It had trained lines before it had a plan: the era build of its two highest trained lines.</summary>
        Nearest,
        /// <summary>It had trained lines and no era build matches them: today's picker, aligned to its weapons.</summary>
        Legacy,
    }

    /// <summary>The first-load choice: an era plan, or null to keep today's class-file plan (off-meta, legacy).</summary>
    public sealed record EraBuildDecision(BotSpec Plan, EraBuildDraw Draw, EraBuild Build, string Jitter);

    /// <summary>
    /// Era builds 1.2, WP3 (docs/ERA-BUILDS-1.2.md, "The build draw"): which lifetime build a persistent gamebot takes at
    /// its first load, and the three log lines. Pure: the record constructor (GameBot) passes in what it knows.
    /// <list type="number">
    /// <item>A saved plan is restored exactly (BotLifetimeBuild.Restore), and nothing here runs: existing bots keep their
    /// class and build (owner's review D1).</item>
    /// <item>No saved plan, no trained line above 1: an era build drawn by weight, seeded by the bot id (a crash before
    /// the first save draws the same build again), with up to <see cref="EraBuildCatalog.JitterPoints"/> levels moved
    /// between its lower lines; or, for bot_era_offmeta_percent of bots (10), today's class-file build.</item>
    /// <item>No saved plan but trained lines (a world from before lifetime builds): the era build of the two highest
    /// trained lines; none matches, today's picker with AlignWithInvestedWeapons. No trained point is taken back.</item>
    /// </list>
    /// Only autonomous gamebots: never Epic Special Forces or buffbots (in memory only), hired companions (their player
    /// trains them) or /spawn and /bot companions (another constructor), and never the custom class (no era builds; bots
    /// never play it).
    /// </summary>
    public static class EraLifetimeBuild
    {
        public const string AssignedTag = "ERA_BUILD_ASSIGNED";
        public const string TrainedTag = "ERA_BUILD_TRAINED";
        public const string CensusTag = "ERA_BUILD_CENSUS";

        /// <summary>Whether a gamebot without a saved plan takes the era draw at all.</summary>
        public static bool Applies(bool eraBuilds, int classId, bool inMemoryOnly, bool hiredCompanion, EraBuildCatalog catalog) =>
            eraBuilds && !inMemoryOnly && !hiredCompanion && BotCustomClassRule.BotsMayPlay(classId) &&
            catalog?.GetClass(classId) != null;

        /// <summary>
        /// The plan for a gamebot with no saved plan. <paramref name="trained"/> is its trainable lines and their levels now;
        /// <paramref name="offMetaPercent"/> the bot_era_offmeta_percent setting. A null <see cref="EraBuildDecision.Plan"/>
        /// keeps the class-file plan the constructor already made.
        /// </summary>
        public static EraBuildDecision Choose(EraBuildCatalog catalog, eCharacterClass characterClass, long botId,
            IReadOnlyCollection<KeyValuePair<string, int>> trained, int offMetaPercent)
        {
            ulong seed = unchecked((ulong)botId);
            EraClass eraClass = catalog?.GetClass((int)characterClass);
            if (eraClass == null)
                return new EraBuildDecision(null, EraBuildDraw.Legacy, null, string.Empty);
            if (trained == null || trained.All(line => line.Value <= 1))
            {
                if (catalog.IsOffMeta(seed, Math.Clamp(offMetaPercent, 0, 100)))
                    return new EraBuildDecision(null, EraBuildDraw.OffMeta, null, string.Empty);
                EraBuild drawn = catalog.DrawBuild(eraClass.Name, seed);
                IReadOnlyList<KeyValuePair<string, int>> specs = catalog.Jitter(drawn, seed, out string moves);
                BotSpec plan = BotSpec.FromEraBuild(characterClass, drawn, specs, botId);
                return plan == null
                    ? new EraBuildDecision(null, EraBuildDraw.Legacy, null, string.Empty)
                    : new EraBuildDecision(plan, EraBuildDraw.Era, drawn, moves);
            }
            EraBuild nearest = catalog.NearestBuild(eraClass.Name, trained);
            if (nearest == null)
                return new EraBuildDecision(null, EraBuildDraw.Legacy, null, string.Empty);
            var levels = trained.GroupBy(line => line.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Max(line => line.Value), StringComparer.Ordinal);
            BotSpec nearestPlan = BotSpec.FromEraBuild(characterClass, nearest, nearest.Specs, botId,
                line => levels.TryGetValue(line, out int level) ? level : 0);
            return nearestPlan == null
                ? new EraBuildDecision(null, EraBuildDraw.Legacy, null, string.Empty)
                : new EraBuildDecision(nearestPlan, EraBuildDraw.Nearest, nearest, string.Empty);
        }

        // ---- the log (no diagnostics switch: at most one ASSIGNED and one TRAINED line per bot for life)

        /// <summary>
        /// <c>ERA_BUILD_ASSIGNED bot="Name" id=123 class=Sorcerer level=50 build=sorcerer-mind-body label="Mind/Body (AE mez)"
        /// role=cc-mezzer draw=era jitter="Body Magic+1,Mind Magic-1"</c>, when the plan is first saved. Today's builds
        /// (off-meta, legacy) show <c>build=today-&lt;SpecType&gt;</c> and an empty role.
        /// </summary>
        public static string AssignedLine(string name, long id, string className, int level, BotSpec plan, EraBuildDecision decision)
        {
            EraBuild build = decision?.Build;
            string buildId = build?.Id ?? $"today-{plan?.SpecType ?? eSpecType.None}";
            string label = build?.Label ?? "today's class-file build";
            return $"{AssignedTag} bot=\"{Clean(name)}\" id={id} class={className} level={level} build={buildId} " +
                   $"label=\"{Clean(label)}\" role={build?.Role ?? string.Empty} draw={Draw(decision?.Draw ?? EraBuildDraw.Legacy)} " +
                   $"jitter=\"{decision?.Jitter ?? string.Empty}\"";
        }

        public static string Draw(EraBuildDraw draw) => draw switch
        {
            EraBuildDraw.Era => "era",
            EraBuildDraw.OffMeta => "offmeta",
            EraBuildDraw.Nearest => "nearest",
            _ => "legacy",
        };

        /// <summary>True when every line of the plan reached its planned level (a line already above it, from before the
        /// plan, also counts: nothing is untrained).</summary>
        public static bool Matches(BotSpec plan, Func<string, int> trained) =>
            plan?.SpecLines != null && plan.SpecLines.Count > 0 &&
            plan.SpecLines.All(line => trained(line.Spec) >= line.SpecCap);

        /// <summary>
        /// <c>ERA_BUILD_TRAINED bot="Name" id=123 build=sorcerer-mind-body level=50 specs="Mind Magic:44,Body Magic:31"
        /// match=yes</c>, when an era-built bot first stands trained at 50 (a "+ Lv.50" arrival, or its first trainer visit at
        /// 50). With match=no, <c>short="Body Magic:29/31"</c> names each line below its plan.
        /// </summary>
        public static string TrainedLine(string name, long id, int level, BotSpec plan, Func<string, int> trained)
        {
            var specs = new StringBuilder();
            var shortLines = new StringBuilder();
            foreach (BotSpecLine line in plan.SpecLines.OrderByDescending(line => line.SpecCap)
                         .ThenBy(line => line.Spec, StringComparer.Ordinal))
            {
                int at = trained(line.Spec);
                if (specs.Length > 0) specs.Append(',');
                specs.Append(line.Spec).Append(':').Append(at);
                if (at < line.SpecCap)
                {
                    if (shortLines.Length > 0) shortLines.Append(',');
                    shortLines.Append(line.Spec).Append(':').Append(at).Append('/').Append(line.SpecCap);
                }
            }
            return $"{TrainedTag} bot=\"{Clean(name)}\" id={id} build={plan.EraBuildId} level={level} specs=\"{specs}\" " +
                   (shortLines.Length == 0 ? "match=yes" : $"match=no short=\"{shortLines}\"");
        }

        /// <summary>Whether this training is the one to log: an era plan, standing at 50 now and not before.</summary>
        public static bool FirstTrainedAtFifty(BotSpec plan, int previousTrainedLevel, int level) =>
            !string.IsNullOrEmpty(plan?.EraBuildId) && previousTrainedLevel < 50 && level >= 50;

        /// <summary>What the trainer status calls the plan: "era build Mind/Body (AE mez) (sorcerer-mind-body)" or "MindSorc plan".</summary>
        public static string PlanName(BotSpec plan)
        {
            if (plan == null)
                return "plan";
            EraBuild build = string.IsNullOrEmpty(plan.EraBuildId) ? null : EraBuildCatalog.Default.GetBuild(plan.EraBuildId);
            return build != null ? $"era build {build.Label} ({build.Id})" : $"{plan.SpecType} plan";
        }

        private static string Clean(string text) => (text ?? string.Empty).Replace('"', '\'');
    }

    /// <summary>
    /// Era builds 1.2, WP3: <c>ERA_BUILD_CENSUS</c>, one line a realm once per server start after the roster loads (the
    /// population controller's first read): the realm's gamebots by plan (era = an era build; kept = a plan without one,
    /// from an earlier build or today's class files; new = no plan yet, drawn at first login) and by class, each with its
    /// share now and its era target: <c>Healer=139(15.4%/15%)</c>. Classes without a target (the Sluaghbinder, always
    /// listed for Hibernia, so a census proves it is 0) show <c>/0%</c>. Then <c>eraClasses="Healer=12(16.0%/15%),..."</c>:
    /// the same count over the era-built bots only (review of WP3-5: an old world's evenly mixed bots would hide the new
    /// bots' mix). Pure.
    /// </summary>
    public static class EraBuildCensus
    {
        public readonly record struct Entry(int Realm, int ClassId, string SerializedBuildPlan);

        public static IReadOnlyList<string> Lines(IEnumerable<Entry> roster, EraBuildCatalog catalog)
        {
            Entry[] all = roster?.ToArray() ?? Array.Empty<Entry>();
            var lines = new List<string>(3);
            for (int realm = 1; realm <= 3; realm++)
            {
                Entry[] bots = all.Where(entry => entry.Realm == realm).ToArray();
                char[] kinds = bots.Select(bot => PlanKind(bot.SerializedBuildPlan)).ToArray();
                int era = kinds.Count(kind => kind == 'e'), kept = kinds.Count(kind => kind == 'k');
                int fresh = kinds.Length - era - kept;
                var line = new StringBuilder();
                line.Append(EraLifetimeBuild.CensusTag).Append(" realm=").Append(EraBuildCatalog.RealmName(realm))
                    .Append(" bots=").Append(bots.Length).Append(" era=").Append(era).Append(" kept=").Append(kept)
                    .Append(" new=").Append(fresh);
                IReadOnlyList<EraClassShare> shares = catalog?.Shares(realm) ?? Array.Empty<EraClassShare>();
                AppendClasses(line, bots, shares, realm == 3, ' ');
                // The same count over the era-built bots only, so a world's older bots don't hide the new bots' mix.
                line.Append(" eraClasses=\"");
                AppendClasses(line, bots.Where((_, i) => kinds[i] == 'e').ToArray(), shares, false, ',');
                line.Append('"');
                lines.Add(line.ToString());
            }
            return lines;
        }

        /// <summary>Each class's count, its share now and its target. <paramref name="separator"/> ' ' puts a space before
        /// every entry (the line's own fields); ',' joins them (inside a quoted field).</summary>
        private static void AppendClasses(StringBuilder line, Entry[] bots, IReadOnlyList<EraClassShare> shares, bool customClass,
            char separator)
        {
            var counts = bots.GroupBy(bot => bot.ClassId).ToDictionary(group => group.Key, group => group.Count());
            var listed = new HashSet<int>();
            bool first = true;
            void One(string className, int count, double target)
            {
                if (separator == ' ' || !first)
                    line.Append(separator);
                first = false;
                double share = bots.Length > 0 ? count * 100.0 / bots.Length : 0;
                line.Append(className).Append('=').Append(count).Append('(')
                    .Append(share.ToString("0.0", CultureInfo.InvariantCulture)).Append("%/")
                    .Append((target * 100).ToString("0.#", CultureInfo.InvariantCulture)).Append("%)");
            }
            foreach (EraClassShare share in shares.OrderByDescending(s => s.Share))
            {
                listed.Add(share.ClassId);
                One(share.ClassName, counts.GetValueOrDefault(share.ClassId), share.Share);
            }
            if (customClass)
                counts.TryAdd(BotCustomClassRule.CustomClassId, 0);
            foreach ((int classId, int count) in counts.Where(entry => !listed.Contains(entry.Key)).OrderBy(entry => entry.Key))
                One(((eCharacterClass)classId).ToString(), count, 0);
        }

        /// <summary>'e' an era plan, 'k' a plan without an era build, 'n' no plan.</summary>
        public static char PlanKind(string serializedBuildPlan)
        {
            if (string.IsNullOrWhiteSpace(serializedBuildPlan))
                return 'n';
            try
            {
                using JsonDocument plan = JsonDocument.Parse(serializedBuildPlan);
                return plan.RootElement.ValueKind == JsonValueKind.Object &&
                       plan.RootElement.TryGetProperty(nameof(BotSpec.EraBuildId), out JsonElement id) &&
                       id.ValueKind == JsonValueKind.String && !string.IsNullOrEmpty(id.GetString())
                    ? 'e' : 'k';
            }
            catch (JsonException)
            {
                return 'n'; // unreadable: the next load draws a plan (BotLifetimeBuild.Restore refuses it too)
            }
        }
    }
}
