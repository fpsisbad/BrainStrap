using BrainStrap.Core.Perf;

namespace BrainStrap.Core;

/// <summary>
/// BrainRank: a level, a daily streak and badges, worked out from your own session history on this PC.
/// It is only for fun. Nothing is sent anywhere and it has no effect on Roblox.
/// </summary>
internal static class Rank
{
    internal sealed record Badge(string Icon, string Name, string How, bool Earned);

    internal sealed record State(int Level, string Title, int Xp, int LevelStart, int LevelEnd, int Streak, double Hours, int Sessions, List<Badge> Badges)
    {
        public double Progress => LevelEnd > LevelStart ? Math.Clamp((double)(Xp - LevelStart) / (LevelEnd - LevelStart), 0, 1) : 0;
        public int ToNext => Math.Max(0, LevelEnd - Xp);
    }

    internal sealed record Recap(double Hours, int Sessions, int Days, string? TopGame, double TopGameMinutes, double? BestFps, double LongestMinutes, double[] MinutesPerDay);

    /// <summary>Total XP needed to reach a level (level 1 starts at 0).</summary>
    static int XpFor(int level) => 60 * (level - 1) * level;

    static string TitleFor(int level) => level >= 50 ? "Legend" : level >= 35 ? "Master" : level >= 20 ? "Elite" : level >= 10 ? "Pro" : level >= 5 ? "Regular" : "Rookie";

    public static State Get(Settings s)
    {
        var all = History.Sessions();
        // a minute played is a point; finishing a session and a smooth session earn a little extra
        var xp = (int)all.Sum(r => r.Minutes + 20 + (r.Fps is { Stability: >= 85 } ? 30 : 0));
        var level = 1;
        while (level < 99 && XpFor(level + 1) <= xp) level++;

        var days = all.Select(r => r.Start.Date).Distinct().OrderByDescending(d => d).ToList();
        var streak = 0;
        if (days.Count > 0 && (DateTime.Today - days[0]).TotalDays <= 1)
        {
            streak = 1;
            for (var i = 1; i < days.Count && (days[i - 1] - days[i]).TotalDays == 1; i++) streak++;
        }
        var hours = all.Sum(r => r.Minutes) / 60;
        var badges = new List<Badge>
        {
            new("play", "First launch", "Play once through BrainStrap", all.Count > 0),
            new("globe", "Explorer", "Play 5 different games", all.Where(r => r.PlaceId != null).Select(r => r.PlaceId).Distinct().Count() >= 5),
            new("clock", "Marathon", "One session of 2 hours or more", all.Any(r => r.Minutes >= 120)),
            new("pulse", "Smooth operator", "A session with stability 90+", all.Any(r => r.Fps is { Stability: >= 90 })),
            new("bolt", "On a roll", "Play 7 days in a row", streak >= 7),
            new("heart", "Night owl", "Start a session after midnight", all.Any(r => r.Start.Hour < 5)),
            new("flag", "Tuner", "Set up a graphics profile", s.Profiles.Values.Any(p => p.Values.Count > 0)),
            new("flow", "Automator", "Create a BrainFlow automation", s.Platform.Flows.Count > 0),
            new("shield", "Centurion", "100 hours played", hours >= 100),
        };
        return new State(level, TitleFor(level), xp, XpFor(level), XpFor(level + 1), streak, hours, all.Count, badges);
    }

    /// <summary>The last seven days, for the weekly recap card.</summary>
    public static Recap Week()
    {
        var from = DateTime.Today.AddDays(-6);
        var week = History.Sessions().Where(r => r.Start >= from).ToList();
        var perDay = new double[7];
        foreach (var r in week) perDay[Math.Clamp((int)(r.Start.Date - from).TotalDays, 0, 6)] += r.Minutes;
        var top = week.GroupBy(r => r.GameName ?? (r.PlaceId != null ? "Place " + r.PlaceId : "Roblox")).OrderByDescending(g => g.Sum(x => x.Minutes)).FirstOrDefault();
        return new Recap(week.Sum(r => r.Minutes) / 60, week.Count, week.Select(r => r.Start.Date).Distinct().Count(), top?.Key, top?.Sum(x => x.Minutes) ?? 0,
            week.Where(r => r.Fps != null).Select(r => (double?)r.Fps!.AvgFps).DefaultIfEmpty(null).Max(), week.Select(r => r.Minutes).DefaultIfEmpty(0).Max(), perDay);
    }
}
