using IOMonitoring.Application.Abstractions;
using IOMonitoring.Domain;
using Microsoft.EntityFrameworkCore;

namespace IOMonitoring.Infrastructure.Persistence;

/// <summary>
/// Generates deterministic dummy data: demo users, 24 IO Boxes and ~30 days of test runs.
/// A fixed random seed keeps the dashboards identical between restarts.
/// </summary>
public static class DataSeeder
{
    public const int DeviceCount = 24;
    public const int HistoryDays = 30;

    private static readonly (string Model, (ChannelType Type, int Count)[] Layout)[] Models =
    {
        ("IOB-8DI8DO", new[] { (ChannelType.DigitalInput, 8), (ChannelType.DigitalOutput, 8) }),
        ("IOB-16DI",   new[] { (ChannelType.DigitalInput, 16) }),
        ("IOB-4AI4AO", new[] { (ChannelType.AnalogInput, 4), (ChannelType.AnalogOutput, 4) }),
        ("IOB-MIX24",  new[] { (ChannelType.DigitalInput, 8), (ChannelType.DigitalOutput, 6), (ChannelType.AnalogInput, 4),
                               (ChannelType.AnalogOutput, 2), (ChannelType.Relay, 4) })
    };

    private static readonly string[] Locations =
        ["Line A - Assembly", "Line B - Assembly", "QA Lab", "Burn-in Room", "Repair Bench"];

    private static readonly string[] Stations = ["Bench-01", "Bench-02", "Bench-03", "Bench-04"];

    private static readonly string[] Operators = ["J. Roy", "L. Côté", "K. Diallo", "P. Nguyen", "M. Tremblay"];

    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher hasher,
        TimeProvider time,
        CancellationToken cancellationToken = default)
    {
        if (await db.Users.AnyAsync(cancellationToken))
        {
            return; // already seeded
        }

        var rng = new Random(20261028);
        var now = time.GetUtcNow().UtcDateTime;

        db.Users.AddRange(
            NewUser("admin", "Administrator", "Admin123!", UserRole.Admin, hasher),
            NewUser("operator", "Test Operator", "Operator123!", UserRole.Operator, hasher),
            NewUser("viewer", "Read-only Viewer", "Viewer123!", UserRole.Viewer, hasher));

        for (var i = 1; i <= DeviceCount; i++)
        {
            var (model, layout) = Models[i % Models.Length];
            var device = new Device
            {
                Id = Guid.NewGuid(),
                SerialNumber = $"IOB-{24000 + i}",
                Name = $"IO Box {i:D2}",
                Model = model,
                FirmwareVersion = $"2.{rng.Next(0, 4)}.{rng.Next(0, 10)}",
                Location = Locations[rng.Next(Locations.Length)],
                Status = i % 11 == 0 ? DeviceStatus.Retired
                       : i % 7 == 0 ? DeviceStatus.Maintenance
                       : DeviceStatus.Active,
                CommissionedAt = now.AddDays(-rng.Next(120, 900)).Date
            };

            // Every 5th box is a "problem unit" with a much higher per-channel failure probability.
            var channelFailureProbability = i % 5 == 0 ? 0.03 : 0.003;
            var runCount = device.Status == DeviceStatus.Retired ? rng.Next(5, 12) : rng.Next(25, 60);

            for (var r = 0; r < runCount; r++)
            {
                device.TestRuns.Add(CreateRun(device, layout, channelFailureProbability, now, rng));
            }

            db.Devices.Add(device);
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private static AppUser NewUser(string username, string displayName, string password, UserRole role, IPasswordHasher hasher) =>
        new()
        {
            Id = Guid.NewGuid(),
            Username = username,
            DisplayName = displayName,
            PasswordHash = hasher.Hash(password),
            Role = role
        };

    private static TestRun CreateRun(
        Device device,
        (ChannelType Type, int Count)[] layout,
        double failureProbability,
        DateTime now,
        Random rng)
    {
        var started = now
            .AddDays(-rng.NextDouble() * HistoryDays)
            .AddSeconds(-rng.Next(0, 60));
        var run = new TestRun
        {
            Id = Guid.NewGuid(),
            DeviceId = device.Id,
            StartedAt = started,
            CompletedAt = started.AddSeconds(rng.Next(45, 240)),
            Station = Stations[rng.Next(Stations.Length)],
            Operator = Operators[rng.Next(Operators.Length)]
        };

        var channels = new List<ChannelResult>();
        foreach (var (type, count) in layout)
        {
            for (var n = 1; n <= count; n++)
            {
                channels.Add(CreateChannel(run.Id, type, n, rng.NextDouble() < failureProbability, rng));
            }
        }

        // ~3% of runs are aborted part-way through (operator stop, fixture issue, ...).
        if (rng.NextDouble() < 0.03)
        {
            channels = channels.Take(rng.Next(1, channels.Count)).ToList();
            run.Outcome = TestOutcome.Aborted;
            run.Notes = "Run aborted by operator before completion.";
            run.CompletedAt = run.StartedAt.AddSeconds(rng.Next(10, 40));
        }
        else
        {
            run.Outcome = TestRun.EvaluateOutcome(channels);
            if (run.Outcome == TestOutcome.Failed)
            {
                run.Notes = $"{channels.Count(c => !c.Passed)} channel(s) out of tolerance.";
            }
        }

        foreach (var channel in channels)
        {
            run.Channels.Add(channel);
        }

        return run;
    }

    private static ChannelResult CreateChannel(Guid runId, ChannelType type, int number, bool fail, Random rng)
    {
        double expected, measured, tolerance;

        if (type is ChannelType.AnalogInput or ChannelType.AnalogOutput)
        {
            // 4-20 mA loop: within ±0.10 mA is a pass.
            tolerance = 0.10;
            expected = Math.Round(4 + rng.NextDouble() * 16, 2);
            var deviation = fail
                ? (0.2 + rng.NextDouble()) * (rng.Next(2) == 0 ? -1 : 1)
                : (rng.NextDouble() - 0.5) * 0.12;
            measured = Math.Round(expected + deviation, 3);
        }
        else
        {
            // Digital / relay: logic level 0 or 1, must match exactly.
            tolerance = 0;
            expected = rng.Next(2);
            measured = fail ? 1 - expected : expected;
        }

        return new ChannelResult
        {
            Id = Guid.NewGuid(),
            TestRunId = runId,
            ChannelNumber = number,
            Type = type,
            Expected = expected,
            Measured = measured,
            Tolerance = tolerance,
            Passed = ChannelResult.IsWithinTolerance(expected, measured, tolerance)
        };
    }
}
