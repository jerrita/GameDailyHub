using GameDailyHub.Core.Models;

namespace GameDailyHub.Core.Services;

public sealed class ScheduleEvaluator
{
    private DateTimeOffset? _previous;
    public bool ShouldRun(ScheduleConfig config, DateTimeOffset now)
    {
        var previous = _previous;
        _previous = now;
        if (!config.Enabled || previous is null || now - previous > TimeSpan.FromMinutes(1) || now <= previous) return false;
        if (!TimeOnly.TryParseExact(config.Time, "HH:mm", out var time)) return false;
        var zone = TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId);
        var localNow = TimeZoneInfo.ConvertTime(now, zone);
        var localPrevious = TimeZoneInfo.ConvertTime(previous.Value, zone);
        var date = localNow.ToString("yyyy-MM-dd");
        if (config.LastTriggeredDate == date || localPrevious.Date != localNow.Date) return false;
        var scheduled = localNow.Date.Add(time.ToTimeSpan());
        if (localPrevious.DateTime >= scheduled || localNow.DateTime < scheduled) return false;
        config.LastTriggeredDate = date;
        return true;
    }
}
