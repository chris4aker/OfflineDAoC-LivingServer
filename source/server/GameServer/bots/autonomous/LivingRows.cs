using System;
using System.Threading;
using DOL.AI.Brain;
using DOL.Events;
using DOL.GS.Keeps;
using DOL.GS.ServerProperties;
using DOL.Logging;

namespace DOL.GS;

// ---- Living Server verification rows, set A (issue #625 rows 1-5) ------------------------------------------------------
//
// Logging only, like the RvR record (RvrRecord.cs): nothing in these files changes what a player, a companion, a gamebot
// or a monster does. No game state is written, no target, path or aggro is set, no timer is started per event and no random
// number is drawn (sampling is a plain counter). Every row is one stable name with key=value fields, never a player or
// account name, and passes a per-row minute budget (RvrRecordBudget) whose drops RVR_HOUR counts (dropped=).
// source/tools/living_report_sections/ reads them into the morning report.
//
// Rows and their volume at about 3,000 bots (ceilings are per row and hard):
//   BG_POPULATION        every 5 min, one per battleground and realm: 12 per 5 min = 144 an hour.
//   CAST_INTERRUPT       every 10th cast an attacker's interrupt timer reached, at most 20 a minute (<= 1,200 an hour).
//   CC_BREAK             each break of a mez or breakable root, at most 20 a minute (<= 1,200 an hour).
//   BG_TARGET_PICK       every 10th battleground pick, at most 10 a minute (<= 600 an hour).
//   BG_RALLY_RESPONSE    each rallied bot's first engage of a posted contact, at most 10 a minute (<= 600 an hour).
//   PLAYER_LINE_OUTCOME  each line the player types to bots, at most 30 a minute (in play: tens an hour).

/// <summary>The sampling rates of the set A rows (#625). 0 turns a sampled row off. Logs only.</summary>
public static class LivingRowProperties
{
    [ServerProperty("living_rows", "cast_interrupt_sample_every", "Log one CAST_INTERRUPT row for every Nth cast an attacker's interrupt timer reached at its half-cast point (at most 20 a minute). 0 turns it off. Logs only.", 10)]
    public static int CastInterruptSampleEvery = 10;

    [ServerProperty("living_rows", "bg_target_pick_sample_every", "Log one BG_TARGET_PICK row for every Nth battleground target pick of an autonomous gamebot (at most 10 a minute). 0 turns it off. Logs only.", 10)]
    public static int BgTargetPickSampleEvery = 10;
}

/// <summary>Shared helpers of the set A rows: the log, the budget, the four kinds. Pure where it says so.</summary>
public static class LivingRows
{
    private static readonly Logger Log = LoggerManager.Create(typeof(LivingRows));

    /// <summary>Tests read the rows here (the server's logger is off in tests).</summary>
    public static Action<string> TestSink;

    /// <summary>Whether a row would go anywhere (the server log or a test).</summary>
    public static bool Enabled => TestSink != null || Log.IsInfoEnabled;

    /// <summary>Writes a row. Never throws.</summary>
    public static void Emit(string row)
    {
        try
        {
            TestSink?.Invoke(row);
            if (Log.IsInfoEnabled)
                Log.Info(row);
        }
        catch
        {
            // Logging only.
        }
    }

    /// <summary>The row's minute budget (drops are counted in RVR_HOUR's dropped=).</summary>
    public static bool Take(string tag, int perMinute) => RvrRecordBudget.Take(tag, Now(), perMinute);

    public static long Now()
    {
        try { return GameLoop.GameLoopTime; } catch { return 0; }
    }

    /// <summary>
    /// The four kinds the rows tell apart: "player" (a real player), "companion" (a hired companion, a /bot or /spawn
    /// helper, a No Lifer buffbot), "gamebot" (an autonomous gamebot) and "npc" (everything else, monsters, guards and
    /// pets included). "-" for none.
    /// </summary>
    public static string Kind(GameLiving living) => living switch
    {
        null => "-",
        GamePlayer => "player",
        GameBot bot => BotChatTranscript.KindOf(bot) == "gamebot" ? "gamebot" : "companion",
        _ => "npc",
    };

    /// <summary>A pet's owner (a player's, a companion's or a gamebot's pet), or null when it is no pet.</summary>
    public static GameLiving PetOwner(GameLiving living)
    {
        try
        {
            return living is GameNPC { Brain: IControlledBrain controlled } and not GameBot ? controlled.GetLivingOwner() : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>The kind of the one who acted: a pet counts as its owner's kind (pet=1 in the row).</summary>
    public static string ActorKind(GameLiving living, out bool pet)
    {
        GameLiving owner = PetOwner(living);
        pet = owner != null;
        return Kind(owner ?? living);
    }

    /// <summary>The battleground's name for a region, or "-" outside the four battlegrounds.</summary>
    public static string Battleground(ushort region) =>
        BattlegroundBrackets.IsBattlegroundRegion(region) ? BattlegroundBrackets.ForRegion(region)?.Name ?? "-" : "-";

    /// <summary>The kind of a battleground pick: person (player, gamebot, companion), pet, engine, door, guard or npc. Pure.</summary>
    public static string PickKind(GameLiving target) => target switch
    {
        null => "-",
        GameSiegeWeapon => "engine",
        GameKeepDoor => "door",
        GameKeepGuard => "guard",
        GamePlayer or GameBot => "person",
        GameNPC { Brain: IControlledBrain } => "pet",
        _ => "npc",
    };
}

/// <summary>Every Nth event, server-wide: a plain counter (no random numbers). Thread-safe.</summary>
public sealed class LivingRowSampler
{
    private long _seen;

    public long Seen => Interlocked.Read(ref _seen);

    /// <summary>True for every <paramref name="every"/>th call; never when <paramref name="every"/> is 0 or less.</summary>
    public bool Take(int every, out long sequence)
    {
        sequence = Interlocked.Increment(ref _seen);
        return every > 0 && sequence % every == 0;
    }

    public void Reset() => Interlocked.Exchange(ref _seen, 0);
}

/// <summary>
/// The set A rows' pulse: every <see cref="PulseMilliseconds"/> it settles the player's lines whose 30 s are up
/// (PLAYER_LINE_OUTCOME), and every five minutes it writes BG_POPULATION. One timer for all of them; it reads the world and
/// writes rows, nothing else.
/// </summary>
public static class LivingRowsPulse
{
    private static readonly Logger Log = LoggerManager.Create(typeof(LivingRowsPulse));

    public const int PulseMilliseconds = 10_000;

    private static Timer _timer;
    private static int _running;
    private static long _nextPopulation;

    [GameServerStartedEvent]
    public static void Start(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _nextPopulation = LivingRows.Now() + BgPopulationRows.IntervalMilliseconds;
        _timer = new Timer(_ => Pulse(), null, PulseMilliseconds, PulseMilliseconds);
    }

    [GameServerStoppedEvent]
    public static void Stop(DOLEvent e, object sender, EventArgs args)
    {
        _timer?.Dispose();
        _timer = null;
    }

    private static void Pulse()
    {
        if (Interlocked.Exchange(ref _running, 1) == 1)
            return;
        try
        {
            long now = LivingRows.Now();
            PlayerLineOutcomes.Settle(now);
            if (now >= _nextPopulation)
            {
                _nextPopulation = now + BgPopulationRows.IntervalMilliseconds;
                if (LivingRows.Enabled)
                    BgPopulationRows.Write(now);
            }
        }
        catch (Exception exception)
        {
            Log.Warn("Living rows pulse", exception);
        }
        finally
        {
            Volatile.Write(ref _running, 0);
        }
    }
}
