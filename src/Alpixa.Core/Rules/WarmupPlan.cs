namespace Alpixa.Core.Rules;

public sealed class WarmupStep
{
    public int FromDay { get; set; }
    public int DailyLimit { get; set; }
}

public sealed class WarmupPlan
{
    public List<WarmupStep> Steps { get; set; } = new();
    public double GrowthAfterPlan { get; set; } = 1.2;

    public static WarmupPlan Default() => new()
    {
        Steps =
        [
            new() { FromDay = 1, DailyLimit = 50 },
            new() { FromDay = 3, DailyLimit = 100 },
            new() { FromDay = 5, DailyLimit = 250 },
            new() { FromDay = 7, DailyLimit = 500 },
            new() { FromDay = 9, DailyLimit = 1000 },
            new() { FromDay = 11, DailyLimit = 2500 }
        ]
    };

    public int LimitForDay(int day)
    {
        if (Steps.Count == 0) return int.MaxValue;
        if (day < 1) day = 1;
        var ordered = Steps.OrderBy(s => s.FromDay).ToList();
        var last = ordered[^1];
        var lastPlannedDay = last.FromDay + 3;
        if (day > lastPlannedDay)
        {
            var extraDays = day - lastPlannedDay;
            var grown = last.DailyLimit * Math.Pow(GrowthAfterPlan, extraDays);
            return grown >= int.MaxValue ? int.MaxValue : (int)grown;
        }
        return (ordered.LastOrDefault(s => s.FromDay <= day) ?? ordered[0]).DailyLimit;
    }

    public static int DayNumber(DateTime warmupStartUtc, DateTime nowUtc)
        => Math.Max(1, (int)(nowUtc.Date - warmupStartUtc.Date).TotalDays + 1);
}
