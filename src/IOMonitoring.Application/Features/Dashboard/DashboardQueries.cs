using IOMonitoring.Application.Abstractions;
using IOMonitoring.Application.Common;
using IOMonitoring.Contracts;
using IOMonitoring.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IOMonitoring.Application.Features.Dashboard;


public sealed record GetDashboardSummaryQuery : IRequest<DashboardSummaryDto>;

internal sealed class GetDashboardSummaryHandler(IAppDbContext db, TimeProvider time)
    : IRequestHandler<GetDashboardSummaryQuery, DashboardSummaryDto>
{
    public async Task<DashboardSummaryDto> Handle(GetDashboardSummaryQuery request, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow().UtcDateTime;

        var statuses = await db.Devices.AsNoTracking()
            .Select(d => d.Status)
            .ToListAsync(cancellationToken);

        var runs = await db.TestRuns.AsNoTracking()
            .Select(r => new { r.Outcome, r.StartedAt, r.CompletedAt })
            .ToListAsync(cancellationToken);

        var passed = runs.Count(r => r.Outcome == TestOutcome.Passed);
        var failed = runs.Count(r => r.Outcome == TestOutcome.Failed);
        var aborted = runs.Count(r => r.Outcome == TestOutcome.Aborted);
        var avgDuration = runs.Count == 0
            ? 0
            : Math.Round(runs.Average(r => (r.CompletedAt - r.StartedAt).TotalSeconds), 1);

        return new DashboardSummaryDto(
            TotalDevices: statuses.Count,
            ActiveDevices: statuses.Count(s => s == DeviceStatus.Active),
            DevicesInMaintenance: statuses.Count(s => s == DeviceStatus.Maintenance),
            TotalTestRuns: runs.Count,
            PassedRuns: passed,
            FailedRuns: failed,
            AbortedRuns: aborted,
            PassRate: Mappings.Percent(passed, runs.Count),
            RunsLast24h: runs.Count(r => r.StartedAt >= now.AddHours(-24)),
            AverageDurationSeconds: avgDuration);
    }
}

/**
 * Daily trend of test runs (passed/failed/aborted) for the last N days.
 * Default is 14 days, max is 90 days.
 */
public sealed record GetDailyTrendQuery(int Days = 14) : IRequest<IReadOnlyList<DailyTrendPointDto>>;

internal sealed class GetDailyTrendHandler(IAppDbContext db, TimeProvider time)
    : IRequestHandler<GetDailyTrendQuery, IReadOnlyList<DailyTrendPointDto>>
{
    public async Task<IReadOnlyList<DailyTrendPointDto>> Handle(GetDailyTrendQuery request, CancellationToken cancellationToken)
    {
        var days = Math.Clamp(request.Days, 1, 90);
        var today = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime);
        var firstDay = today.AddDays(-(days - 1));
        var from = firstDay.ToDateTime(TimeOnly.MinValue);

        var runs = await db.TestRuns.AsNoTracking()
            .Where(r => r.StartedAt >= from)
            .Select(r => new { r.StartedAt, r.Outcome })
            .ToListAsync(cancellationToken);

        return Mappings.BuildDailyTrend(runs.Select(r => (r.StartedAt, r.Outcome)), firstDay, days);
    }
}

// ---------------------------------------------------------------------------
// Failure rate by channel type
// ---------------------------------------------------------------------------
public sealed record GetChannelFailuresQuery : IRequest<IReadOnlyList<ChannelFailureDto>>;

internal sealed class GetChannelFailuresHandler(IAppDbContext db)
    : IRequestHandler<GetChannelFailuresQuery, IReadOnlyList<ChannelFailureDto>>
{
    public async Task<IReadOnlyList<ChannelFailureDto>> Handle(GetChannelFailuresQuery request, CancellationToken cancellationToken)
    {
        var channels = await db.ChannelResults.AsNoTracking()
            .Select(c => new { c.Type, c.Passed })
            .ToListAsync(cancellationToken);

        return channels
            .GroupBy(c => c.Type)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var total = g.Count();
                var failed = g.Count(c => !c.Passed);
                return new ChannelFailureDto(g.Key.ToString(), total, failed, Mappings.Percent(failed, total));
            })
            .ToList();
    }
}

// ---------------------------------------------------------------------------
// Pass rate per test station
// ---------------------------------------------------------------------------
public sealed record GetStationStatsQuery : IRequest<IReadOnlyList<StationStatsDto>>;

internal sealed class GetStationStatsHandler(IAppDbContext db)
    : IRequestHandler<GetStationStatsQuery, IReadOnlyList<StationStatsDto>>
{
    public async Task<IReadOnlyList<StationStatsDto>> Handle(GetStationStatsQuery request, CancellationToken cancellationToken)
    {
        var runs = await db.TestRuns.AsNoTracking()
            .Select(r => new { r.Station, r.Outcome })
            .ToListAsync(cancellationToken);

        return runs
            .GroupBy(r => r.Station)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var total = g.Count();
                var passed = g.Count(r => r.Outcome == TestOutcome.Passed);
                var failed = g.Count(r => r.Outcome == TestOutcome.Failed);
                return new StationStatsDto(g.Key, total, passed, failed, Mappings.Percent(passed, total));
            })
            .ToList();
    }
}
