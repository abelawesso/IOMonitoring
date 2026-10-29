namespace IOMonitoring.Domain;

/// <summary>An electronic IO Box under test.</summary>
public class Device
{
    public Guid Id { get; set; }
    public string SerialNumber { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string FirmwareVersion { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public DeviceStatus Status { get; set; } = DeviceStatus.Active;
    public DateTime CommissionedAt { get; set; }

    public ICollection<TestRun> TestRuns { get; set; } = new List<TestRun>();
}

/// <summary>One execution of the test bench against a device.</summary>
public class TestRun
{
    public Guid Id { get; set; }
    public Guid DeviceId { get; set; }
    public Device? Device { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime CompletedAt { get; set; }
    public string Station { get; set; } = string.Empty;
    public string Operator { get; set; } = string.Empty;
    public TestOutcome Outcome { get; set; }
    public string? Notes { get; set; }

    public ICollection<ChannelResult> Channels { get; set; } = new List<ChannelResult>();

    /// <summary>A run passes only when every channel is within tolerance.</summary>
    public static TestOutcome EvaluateOutcome(IEnumerable<ChannelResult> channels) =>
        channels.All(c => c.Passed) ? TestOutcome.Passed : TestOutcome.Failed;
}

/// <summary>Measurement for a single IO channel within a test run.</summary>
public class ChannelResult
{
    public Guid Id { get; set; }
    public Guid TestRunId { get; set; }
    public TestRun? TestRun { get; set; }

    public int ChannelNumber { get; set; }
    public ChannelType Type { get; set; }
    public double Expected { get; set; }
    public double Measured { get; set; }
    public double Tolerance { get; set; }
    public bool Passed { get; set; }

    public static bool IsWithinTolerance(double expected, double measured, double tolerance) =>
        Math.Abs(measured - expected) <= tolerance + 1e-9;
}

/// <summary>Application user (draft auth: credentials stored in the in-memory DB).</summary>
public class AppUser
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Viewer;
}
