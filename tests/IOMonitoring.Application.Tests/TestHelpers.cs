using IOMonitoring.Domain;
using IOMonitoring.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IOMonitoring.Application.Tests;

internal sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}

internal static class TestDb
{
    public static readonly DateTime Now = new(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

    public static TimeProvider Clock => new FixedTimeProvider(new DateTimeOffset(Now));

    /// <summary>Each call gets its own isolated in-memory database.</summary>
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase($"tests-{Guid.NewGuid()}")
            .Options;
        return new AppDbContext(options);
    }

    public static Device AddDevice(AppDbContext db, string serial = "IOB-TEST-1", DeviceStatus status = DeviceStatus.Active)
    {
        var device = new Device
        {
            Id = Guid.NewGuid(),
            SerialNumber = serial,
            Name = "Test box",
            Model = "IOB-8DI8DO",
            FirmwareVersion = "2.0.0",
            Location = "QA Lab",
            Status = status,
            CommissionedAt = Now.AddDays(-100)
        };
        db.Devices.Add(device);
        db.SaveChanges();
        return device;
    }

    public static TestRun AddRun(AppDbContext db, Device device, TestOutcome outcome, DateTime startedAt)
    {
        var run = new TestRun
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            StartedAt = startedAt,
            CompletedAt = startedAt.AddSeconds(90),
            Station = "Bench-01",
            Operator = "Tester",
            Outcome = outcome
        };
        run.Channels.Add(new ChannelResult
        {
            Id = Guid.NewGuid(),
            TestRunId = run.Id,
            ChannelNumber = 1,
            Type = ChannelType.DigitalInput,
            Expected = 1,
            Measured = outcome == TestOutcome.Failed ? 0 : 1,
            Tolerance = 0,
            Passed = outcome != TestOutcome.Failed
        });
        db.TestRuns.Add(run);
        db.SaveChanges();
        return run;
    }
}
