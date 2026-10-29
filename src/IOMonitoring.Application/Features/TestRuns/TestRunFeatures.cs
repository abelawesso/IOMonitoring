using FluentValidation;
using IOMonitoring.Application.Abstractions;
using IOMonitoring.Application.Common;
using IOMonitoring.Contracts;
using IOMonitoring.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IOMonitoring.Application.Features.TestRuns;

// ---------------------------------------------------------------------------
// Query: paged + filtered list of test runs (newest first)
// ---------------------------------------------------------------------------
public sealed record GetTestRunsQuery(
    Guid? DeviceId = null,
    string? Outcome = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20) : IRequest<PagedResult<TestRunDto>>;

internal sealed class GetTestRunsHandler(IAppDbContext db) : IRequestHandler<GetTestRunsQuery, PagedResult<TestRunDto>>
{
    public async Task<PagedResult<TestRunDto>> Handle(GetTestRunsQuery request, CancellationToken cancellationToken)
    {
        var page = Math.Max(1, request.Page);
        var pageSize = Math.Clamp(request.PageSize, 1, 200);

        IQueryable<TestRun> query = db.TestRuns.AsNoTracking();

        if (request.DeviceId is { } deviceId)
        {
            query = query.Where(r => r.DeviceId == deviceId);
        }

        if (Enum.TryParse<TestOutcome>(request.Outcome, ignoreCase: true, out var outcome))
        {
            query = query.Where(r => r.Outcome == outcome);
        }

        if (request.From is { } from)
        {
            var start = from.Date;
            query = query.Where(r => r.StartedAt >= start);
        }

        if (request.To is { } to)
        {
            var endExclusive = to.Date.AddDays(1);
            query = query.Where(r => r.StartedAt < endExclusive);
        }

        var total = await query.CountAsync(cancellationToken);

        var runs = await query
            .Include(r => r.Device)
            .Include(r => r.Channels)
            .OrderByDescending(r => r.StartedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<TestRunDto>(runs.Select(r => r.ToDto()).ToList(), total, page, pageSize);
    }
}

// ---------------------------------------------------------------------------
// Query: one test run with channel details
// ---------------------------------------------------------------------------
public sealed record GetTestRunByIdQuery(Guid Id) : IRequest<TestRunDetailDto?>;

internal sealed class GetTestRunByIdHandler(IAppDbContext db) : IRequestHandler<GetTestRunByIdQuery, TestRunDetailDto?>
{
    public async Task<TestRunDetailDto?> Handle(GetTestRunByIdQuery request, CancellationToken cancellationToken)
    {
        var run = await db.TestRuns.AsNoTracking()
            .Include(r => r.Device)
            .Include(r => r.Channels)
            .FirstOrDefaultAsync(r => r.Id == request.Id, cancellationToken);

        if (run is null)
        {
            return null;
        }

        var channels = run.Channels
            .OrderBy(c => c.Type)
            .ThenBy(c => c.ChannelNumber)
            .Select(c => c.ToDto())
            .ToList();

        return new TestRunDetailDto(run.ToDto(), run.Notes, channels);
    }
}

// ---------------------------------------------------------------------------
// Command: record a test run coming from a test bench
// ---------------------------------------------------------------------------
public sealed record RecordTestRunCommand(
    Guid DeviceId,
    string Station,
    string Operator,
    DateTime StartedAt,
    DateTime CompletedAt,
    string? Notes,
    IReadOnlyList<ChannelReading> Channels) : IRequest<TestRunDto>;

internal sealed class RecordTestRunCommandValidator : AbstractValidator<RecordTestRunCommand>
{
    public RecordTestRunCommandValidator()
    {
        RuleFor(x => x.DeviceId).NotEmpty();
        RuleFor(x => x.Station).NotEmpty().MaximumLength(50);
        RuleFor(x => x.Operator).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Notes).MaximumLength(1000);
        RuleFor(x => x.CompletedAt)
            .GreaterThanOrEqualTo(x => x.StartedAt)
            .WithMessage("CompletedAt must be after StartedAt.");
        RuleFor(x => x.Channels).NotEmpty();
        RuleForEach(x => x.Channels).ChildRules(channel =>
        {
            channel.RuleFor(c => c.ChannelNumber).GreaterThan(0);
            channel.RuleFor(c => c.Tolerance).GreaterThanOrEqualTo(0);
            channel.RuleFor(c => c.Type)
                .Must(t => Enum.TryParse<ChannelType>(t, ignoreCase: true, out _))
                .WithMessage("Type must be one of: " + string.Join(", ", Enum.GetNames<ChannelType>()));
        });
    }
}

internal sealed class RecordTestRunCommandHandler(IAppDbContext db) : IRequestHandler<RecordTestRunCommand, TestRunDto>
{
    public async Task<TestRunDto> Handle(RecordTestRunCommand request, CancellationToken cancellationToken)
    {
        var device = await db.Devices.FirstOrDefaultAsync(d => d.Id == request.DeviceId, cancellationToken)
                     ?? throw new NotFoundException(nameof(Device), request.DeviceId);

        var run = new TestRun
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            Station = request.Station.Trim(),
            Operator = request.Operator.Trim(),
            StartedAt = request.StartedAt,
            CompletedAt = request.CompletedAt,
            Notes = request.Notes
        };

        foreach (var reading in request.Channels)
        {
            run.Channels.Add(new ChannelResult
            {
                Id = Guid.NewGuid(),
                TestRunId = run.Id,
                ChannelNumber = reading.ChannelNumber,
                Type = Enum.Parse<ChannelType>(reading.Type, ignoreCase: true),
                Expected = reading.Expected,
                Measured = reading.Measured,
                Tolerance = reading.Tolerance,
                Passed = ChannelResult.IsWithinTolerance(reading.Expected, reading.Measured, reading.Tolerance)
            });
        }

        run.Outcome = TestRun.EvaluateOutcome(run.Channels);

        db.TestRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);

        return run.ToDto(device);
    }
}
