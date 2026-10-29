namespace IOMonitoring.Contracts;

// Shared between the API and the Blazor client. Enums travel as strings
// so the client never needs a reference to the Domain project.

// ---------- Auth ----------
public record LoginRequest(string Username, string Password);

public record LoginResponse(string Token, DateTime ExpiresAt, string Username, string DisplayName, string Role);

public record CurrentUserDto(string Username, string DisplayName, string Role);

// ---------- Dashboard ----------
public record DashboardSummaryDto(
    int TotalDevices,
    int ActiveDevices,
    int DevicesInMaintenance,
    int TotalTestRuns,
    int PassedRuns,
    int FailedRuns,
    int AbortedRuns,
    double PassRate,
    int RunsLast24h,
    double AverageDurationSeconds);

public record DailyTrendPointDto(DateOnly Date, int Passed, int Failed, int Aborted);

public record ChannelFailureDto(string ChannelType, int Total, int Failed, double FailureRate);

public record StationStatsDto(string Station, int Runs, int Passed, int Failed, double PassRate);

// ---------- Devices ----------
public record DeviceDto(
    Guid Id,
    string SerialNumber,
    string Name,
    string Model,
    string FirmwareVersion,
    string Location,
    string Status,
    DateTime CommissionedAt,
    int TotalRuns,
    double PassRate,
    string? LastOutcome,
    DateTime? LastTestedAt);

public record DeviceDetailDto(
    DeviceDto Device,
    IReadOnlyList<TestRunDto> RecentRuns,
    IReadOnlyList<DailyTrendPointDto> Trend);

public record CreateDeviceRequest(
    string SerialNumber,
    string Name,
    string Model,
    string FirmwareVersion,
    string Location);

// ---------- Test runs ----------
public record TestRunDto(
    Guid Id,
    Guid DeviceId,
    string DeviceSerial,
    string DeviceName,
    DateTime StartedAt,
    DateTime CompletedAt,
    double DurationSeconds,
    string Station,
    string Operator,
    string Outcome,
    int ChannelCount,
    int FailedChannels);

public record ChannelResultDto(int ChannelNumber, string Type, double Expected, double Measured, double Tolerance, bool Passed);

public record TestRunDetailDto(TestRunDto Run, string? Notes, IReadOnlyList<ChannelResultDto> Channels);

public record ChannelReading(int ChannelNumber, string Type, double Expected, double Measured, double Tolerance);

public record RecordTestRunRequest(
    Guid DeviceId,
    string Station,
    string Operator,
    DateTime StartedAt,
    DateTime CompletedAt,
    string? Notes,
    IReadOnlyList<ChannelReading> Channels);

public record PagedResult<T>(IReadOnlyList<T> Items, int TotalCount, int Page, int PageSize)
{
    public int PageCount => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}
