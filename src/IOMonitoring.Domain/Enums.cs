namespace IOMonitoring.Domain;

/// <summary>Lifecycle state of an IO Box.</summary>
public enum DeviceStatus
{
    Active = 0,
    Maintenance = 1,
    Retired = 2
}

/// <summary>Overall result of a test run.</summary>
public enum TestOutcome
{
    Passed = 0,
    Failed = 1,
    Aborted = 2
}

/// <summary>Kind of IO channel exercised during a test.</summary>
public enum ChannelType
{
    DigitalInput = 0,
    DigitalOutput = 1,
    AnalogInput = 2,
    AnalogOutput = 3,
    Relay = 4
}

/// <summary>Application roles used for authorization.</summary>
public enum UserRole
{
    Viewer = 0,
    Operator = 1,
    Admin = 2
}
