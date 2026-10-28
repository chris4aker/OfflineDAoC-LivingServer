using System;
using System.Collections.Generic;
using DOL.GS.ServerProperties;

namespace DOL.GS;

/// <summary>
/// Holiday chatter (bot_living_holidays, owner 2026-10-28): around a real holiday on the PC's calendar, bots talk about
/// it the way 2003 players did ("brb trick or treaters", "merry christmas realm") and answer a "happy halloween".
/// The calendar only sets voice flags: <c>holiday</c>, <c>holiday_&lt;name&gt;</c> for the days around it and
/// <c>holiday_today</c> on the day itself. The lines live in data/chat/holidays.lines.
/// </summary>
public static class AutonomousHolidays
{
    public static bool On => Properties.BOT_LIVING_HOLIDAYS;

    /// <summary>GM test (/lstest holiday): the date the bots treat as today, or null for the PC's real date.</summary>
    public static DateTime? TestDate { get; set; }

    /// <summary>The date holiday chatter goes by: the GM's test date when set, otherwise now.</summary>
    public static DateTime Now => TestDate ?? DateTime.Now;

    /// <summary>A day inside the named holiday in the year of <paramref name="from"/>, for /lstest (its main day). Null for an unknown name.</summary>
    public static DateTime? DayOf(string name, DateTime from)
    {
        foreach (Window window in WindowsAround(new DateTime(from.Year, 6, 30)))
            if (string.Equals(window.Name, name, StringComparison.OrdinalIgnoreCase) && window.Day.Year == from.Year)
                return window.Day;
        foreach (Window window in WindowsAround(new DateTime(from.Year, 1, 1)))
            if (string.Equals(window.Name, name, StringComparison.OrdinalIgnoreCase))
                return window.Day;
        return null;
    }

    public static readonly string[] Names = ["newyear", "valentines", "stpatricks", "aprilfools", "easter", "midsummer", "halloween", "thanksgiving", "christmas"];

    /// <summary>A holiday's window: from <see cref="First"/> to <see cref="Last"/>, its own day(s) <see cref="Day"/> to <see cref="DayLast"/>.</summary>
    public sealed record Window(string Name, DateTime First, DateTime Last, DateTime Day, DateTime DayLast);

    /// <summary>Share of unprompted realm and /say lines that are holiday lines: in the window, and on the day itself.</summary>
    public const double WindowShare = 0.10, DayShare = 0.22;

    /// <summary>The holiday the date falls in, or null. Pure: the date's year decides the movable ones.</summary>
    public static Window For(DateTime date)
    {
        DateTime d = date.Date;
        foreach (Window window in WindowsAround(d))
            if (d >= window.First && d <= window.Last)
                return window;
        return null;
    }

    /// <summary>The voice flags for that date (empty outside every holiday).</summary>
    public static IReadOnlyCollection<string> Flags(DateTime date)
    {
        Window window = For(date);
        if (window == null)
            return [];
        var flags = new List<string> { "holiday", "holiday_" + window.Name };
        if (date.Date >= window.Day && date.Date <= window.DayLast)
            flags.Add("holiday_today");
        return flags;
    }

    /// <summary>How often an unprompted line is a holiday line on that date (0 outside a holiday).</summary>
    public static double AmbientShare(DateTime date)
    {
        Window window = For(date);
        if (window == null)
            return 0;
        return date.Date >= window.Day && date.Date <= window.DayLast ? DayShare : WindowShare;
    }

    private static IEnumerable<Window> WindowsAround(DateTime d)
    {
        // New Year spans two years: the one starting and the one ending.
        foreach (int year in new[] { d.Year - 1, d.Year })
            yield return new("newyear", new(year, 12, 30), new(year + 1, 1, 2), new(year, 12, 31), new(year + 1, 1, 1));
        int y = d.Year;
        yield return new("valentines", new(y, 2, 12), new(y, 2, 14), new(y, 2, 14), new(y, 2, 14));
        yield return new("stpatricks", new(y, 3, 15), new(y, 3, 17), new(y, 3, 17), new(y, 3, 17));
        yield return new("aprilfools", new(y, 4, 1), new(y, 4, 1), new(y, 4, 1), new(y, 4, 1));
        DateTime easter = EasterSunday(y);
        yield return new("easter", easter.AddDays(-2), easter.AddDays(1), easter, easter);
        yield return new("midsummer", new(y, 6, 19), new(y, 6, 24), new(y, 6, 21), new(y, 6, 21));
        yield return new("halloween", new(y, 10, 24), new(y, 11, 1), new(y, 10, 31), new(y, 10, 31));
        DateTime thanksgiving = Thanksgiving(y);
        yield return new("thanksgiving", thanksgiving.AddDays(-2), thanksgiving.AddDays(3), thanksgiving, thanksgiving);
        yield return new("christmas", new(y, 12, 18), new(y, 12, 26), new(y, 12, 24), new(y, 12, 25));
    }

    /// <summary>Easter Sunday (Gregorian, the anonymous algorithm).</summary>
    public static DateTime EasterSunday(int year)
    {
        int a = year % 19, b = year / 100, c = year % 100, d = b / 4, e = b % 4, f = (b + 8) / 25, g = (b - f + 1) / 3;
        int h = (19 * a + b - d - g + 15) % 30, i = c / 4, k = c % 4, l = (32 + 2 * e + 2 * i - h - k) % 7;
        int m = (a + 11 * h + 22 * l) / 451;
        int month = (h + l - 7 * m + 114) / 31, day = (h + l - 7 * m + 114) % 31 + 1;
        return new DateTime(year, month, day);
    }

    /// <summary>US Thanksgiving: the fourth Thursday of November.</summary>
    public static DateTime Thanksgiving(int year)
    {
        var first = new DateTime(year, 11, 1);
        int toThursday = ((int)DayOfWeek.Thursday - (int)first.DayOfWeek + 7) % 7;
        return first.AddDays(toThursday + 21);
    }
}
