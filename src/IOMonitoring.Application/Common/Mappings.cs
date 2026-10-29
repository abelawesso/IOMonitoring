using IOMonitoring.Contracts;
using IOMonitoring.Domain;

namespace IOMonitoring.Application.Common;

internal static class Mappings
{
    public static DeviceDto ToDto(this Device device)
    {
        var runs = device.TestRuns;
        var total = runs.Count;
        var passed = runs.Count(r => r.Outcome == TestOutcome.Passed);
        var last = runs.OrderByDescending(r => r.StartedAt).FirstOrDefault();

        return new DeviceDto(
            device.Id,
            device.SerialNumber,
            device.Name,
            device.Model,
            device.FirmwareVersion,
            device.Location,
            device.Status.ToString(),
            device.CommissionedAt,
            total,
            Percent(passed, total),
            last?.Outcome.ToString(),
            last?.CompletedAt);
    }

    public static TestRunDto ToDto(this TestRun run, Device? device = null)
    {
        var d = device ?? run.Device;
        return new TestRunDto(
            run.Id,
            run.DeviceId,
            d?.SerialNumber ?? string.Empty,
            d?.Name ?? string.Empty,
            run.StartedAt,
            run.CompletedAt,
            Math.Round((run.CompletedAt - run.StartedAt).TotalSeconds, 1),
            run.Station,
            run.Operator,
            run.Outcome.ToString(),
            run.Channels.Count,
            run.Channels.Count(c => !c.Passed));
    }

    public static ChannelResultDto ToDto(this ChannelResult c) =>
        new(c.ChannelNumber, c.Type.ToString(), c.Expected, c.Measured, c.Tolerance, c.Passed);

    public static double Percent(int part, int total) =>
        total == 0 ? 0 : Math.Round(part * 100.0 / total, 1);

    /// <summary>Builds one point per day (oldest first), filling days with no runs with zeros.</summary>
    public static IReadOnlyList<DailyTrendPointDto> BuildDailyTrend(
        IEnumerable<(DateTime StartedAt, TestOutcome Outcome)> runs,
        DateOnly firstDay,
        int days)
    {
        var buckets = runs
            .GroupBy(r => DateOnly.FromDateTime(r.StartedAt))
            .ToDictionary(g => g.Key, g => g.ToList());

        var points = new List<DailyTrendPointDto>(days);
        for (var i = 0; i < days; i++)
        {
            var day = firstDay.AddDays(i);
            buckets.TryGetValue(day, out var dayRuns);
            dayRuns ??= [];
            points.Add(new DailyTrendPointDto(
                day,
                dayRuns.Count(r => r.Outcome == TestOutcome.Passed),
                dayRuns.Count(r => r.Outcome == TestOutcome.Failed),
                dayRuns.Count(r => r.Outcome == TestOutcome.Aborted)));
        }

        return points;
    }
}
