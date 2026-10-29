using IOMonitoring.Application.Common;
using IOMonitoring.Application.Features.Auth;
using IOMonitoring.Application.Features.Dashboard;
using IOMonitoring.Application.Features.Devices;
using IOMonitoring.Application.Features.TestRuns;
using IOMonitoring.Contracts;
using IOMonitoring.Domain;
using IOMonitoring.Infrastructure.Persistence;
using IOMonitoring.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace IOMonitoring.Application.Tests;

public class RecordTestRunTests
{
    [Fact]
    public async Task Run_passes_when_every_channel_is_within_tolerance()
    {
        using var db = TestDb.Create();
        var device = TestDb.AddDevice(db);
        var handler = new RecordTestRunCommandHandler(db);

        var result = await handler.Handle(Command(device.Id,
            new ChannelReading(1, "AnalogInput", 12.0, 12.05, 0.10),
            new ChannelReading(1, "DigitalInput", 1, 1, 0)), CancellationToken.None);

        Assert.Equal("Passed", result.Outcome);
        Assert.Equal(0, result.FailedChannels);
        Assert.Equal(1, await db.TestRuns.CountAsync());
    }

    [Fact]
    public async Task Run_fails_when_one_channel_is_out_of_tolerance()
    {
        using var db = TestDb.Create();
        var device = TestDb.AddDevice(db);
        var handler = new RecordTestRunCommandHandler(db);

        var result = await handler.Handle(Command(device.Id,
            new ChannelReading(1, "AnalogInput", 12.0, 12.5, 0.10),
            new ChannelReading(2, "AnalogInput", 8.0, 8.0, 0.10)), CancellationToken.None);

        Assert.Equal("Failed", result.Outcome);
        Assert.Equal(1, result.FailedChannels);
    }

    [Fact]
    public async Task Unknown_device_throws_not_found()
    {
        using var db = TestDb.Create();
        var handler = new RecordTestRunCommandHandler(db);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(Command(Guid.NewGuid(), new ChannelReading(1, "Relay", 1, 1, 0)), CancellationToken.None));
    }

    [Fact]
    public void Validator_rejects_end_before_start_and_unknown_channel_type()
    {
        var validator = new RecordTestRunCommandValidator();
        var command = Command(Guid.NewGuid(), new ChannelReading(1, "Banana", 1, 1, 0)) with
        {
            CompletedAt = TestDb.Now.AddMinutes(-10)
        };

        var result = validator.Validate(command);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.PropertyName == nameof(RecordTestRunCommand.CompletedAt));
        Assert.Contains(result.Errors, e => e.PropertyName.Contains("Type"));
    }

    private static RecordTestRunCommand Command(Guid deviceId, params ChannelReading[] channels) =>
        new(deviceId, "Bench-01", "Tester", TestDb.Now, TestDb.Now.AddMinutes(2), null, channels);
}

public class DashboardTests
{
    [Fact]
    public async Task Summary_counts_devices_and_outcomes()
    {
        using var db = TestDb.Create();
        var active = TestDb.AddDevice(db, "IOB-1");
        TestDb.AddDevice(db, "IOB-2", DeviceStatus.Maintenance);
        TestDb.AddRun(db, active, TestOutcome.Passed, TestDb.Now.AddHours(-2));
        TestDb.AddRun(db, active, TestOutcome.Passed, TestDb.Now.AddDays(-3));
        TestDb.AddRun(db, active, TestOutcome.Failed, TestDb.Now.AddDays(-4));
        TestDb.AddRun(db, active, TestOutcome.Aborted, TestDb.Now.AddDays(-5));

        var summary = await new GetDashboardSummaryHandler(db, TestDb.Clock)
            .Handle(new GetDashboardSummaryQuery(), CancellationToken.None);

        Assert.Equal(2, summary.TotalDevices);
        Assert.Equal(1, summary.ActiveDevices);
        Assert.Equal(1, summary.DevicesInMaintenance);
        Assert.Equal(4, summary.TotalTestRuns);
        Assert.Equal(2, summary.PassedRuns);
        Assert.Equal(1, summary.FailedRuns);
        Assert.Equal(1, summary.AbortedRuns);
        Assert.Equal(50.0, summary.PassRate);
        Assert.Equal(1, summary.RunsLast24h);
    }

    [Fact]
    public async Task Trend_returns_one_point_per_day_including_empty_days()
    {
        using var db = TestDb.Create();
        var device = TestDb.AddDevice(db);
        TestDb.AddRun(db, device, TestOutcome.Failed, TestDb.Now.AddHours(-1));

        var trend = await new GetDailyTrendHandler(db, TestDb.Clock)
            .Handle(new GetDailyTrendQuery(7), CancellationToken.None);

        Assert.Equal(7, trend.Count);
        Assert.Equal(DateOnly.FromDateTime(TestDb.Now), trend[^1].Date);
        Assert.Equal(1, trend[^1].Failed);
        Assert.All(trend.Take(6), p => Assert.Equal(0, p.Passed + p.Failed + p.Aborted));
    }
}

public class DeviceTests
{
    [Fact]
    public async Task Creating_a_duplicate_serial_number_is_a_conflict()
    {
        using var db = TestDb.Create();
        TestDb.AddDevice(db, "IOB-42");
        var handler = new CreateDeviceCommandHandler(db, TestDb.Clock);

        await Assert.ThrowsAsync<ConflictException>(() => handler.Handle(
            new CreateDeviceCommand("iob-42", "Dup", "IOB-16DI", "1.0.0", "QA Lab"), CancellationToken.None));
    }

    [Fact]
    public async Task Device_list_can_be_filtered_by_status_and_search()
    {
        using var db = TestDb.Create();
        TestDb.AddDevice(db, "IOB-100");
        TestDb.AddDevice(db, "IOB-200", DeviceStatus.Retired);

        var handler = new GetDevicesHandler(db);
        var retired = await handler.Handle(new GetDevicesQuery(Status: "retired"), CancellationToken.None);
        var search = await handler.Handle(new GetDevicesQuery(Search: "iob-1"), CancellationToken.None);

        Assert.Single(retired);
        Assert.Equal("IOB-200", retired[0].SerialNumber);
        Assert.Single(search);
        Assert.Equal("IOB-100", search[0].SerialNumber);
    }
}

public class AuthTests
{
    private static readonly IOptions<JwtOptions> Jwt = Options.Create(new JwtOptions
    {
        SigningKey = "unit-test-signing-key-0123456789-abcdefghij"
    });

    [Fact]
    public void Password_hasher_round_trips_and_rejects_wrong_password()
    {
        var hasher = new Pbkdf2PasswordHasher();
        var hash = hasher.Hash("Secret123!");

        Assert.True(hasher.Verify("Secret123!", hash));
        Assert.False(hasher.Verify("secret123!", hash));
        Assert.False(hasher.Verify("Secret123!", "not-a-hash"));
    }

    [Fact]
    public async Task Login_returns_token_for_valid_credentials_and_null_otherwise()
    {
        using var db = TestDb.Create();
        var hasher = new Pbkdf2PasswordHasher();
        await DataSeeder.SeedAsync(db, hasher, TestDb.Clock);

        var handler = new LoginCommandHandler(db, hasher, new JwtTokenService(Jwt, TestDb.Clock));

        var ok = await handler.Handle(new LoginCommand("Admin", "Admin123!"), CancellationToken.None);
        var bad = await handler.Handle(new LoginCommand("admin", "wrong"), CancellationToken.None);

        Assert.NotNull(ok);
        Assert.Equal("Admin", ok!.Role);
        Assert.Equal(3, ok.Token.Split('.').Length);
        Assert.Null(bad);
    }
}

public class SeederTests
{
    [Fact]
    public async Task Seeder_creates_devices_runs_and_users_once()
    {
        using var db = TestDb.Create();
        var hasher = new Pbkdf2PasswordHasher();

        await DataSeeder.SeedAsync(db, hasher, TestDb.Clock);
        await DataSeeder.SeedAsync(db, hasher, TestDb.Clock); // second call is a no-op

        Assert.Equal(DataSeeder.DeviceCount, await db.Devices.CountAsync());
        Assert.Equal(3, await db.Users.CountAsync());
        Assert.True(await db.TestRuns.CountAsync() > DataSeeder.DeviceCount * 5);
        Assert.True(await db.TestRuns.AnyAsync(r => r.Outcome == TestOutcome.Failed));
        Assert.True(await db.TestRuns.AllAsync(r => r.StartedAt <= TestDb.Now));
    }
}
