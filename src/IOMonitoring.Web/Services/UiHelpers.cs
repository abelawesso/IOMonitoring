using MudBlazor;

namespace IOMonitoring.Web.Services;

/// <summary>Shared colours and labels so every chart and chip uses the same status vocabulary.</summary>
public static class Ui
{
    public const string PassedHex = "#2E7D32";
    public const string FailedHex = "#C62828";
    public const string AbortedHex = "#9E9E9E";
    public const string PrimaryHex = "#1565C0";

    public static readonly string[] OutcomePalette = [PassedHex, FailedHex, AbortedHex];

    public static Color OutcomeColor(string? outcome) => outcome switch
    {
        "Passed" => Color.Success,
        "Failed" => Color.Error,
        "Aborted" => Color.Default,
        _ => Color.Default
    };

    public static Color StatusColor(string? status) => status switch
    {
        "Active" => Color.Success,
        "Maintenance" => Color.Warning,
        "Retired" => Color.Default,
        _ => Color.Default
    };

    public static Color PassRateColor(double passRate) => passRate switch
    {
        >= 95 => Color.Success,
        >= 85 => Color.Warning,
        _ => Color.Error
    };

    public static string ChannelLabel(string type, int number) => type switch
    {
        "DigitalInput" => $"DI{number}",
        "DigitalOutput" => $"DO{number}",
        "AnalogInput" => $"AI{number}",
        "AnalogOutput" => $"AO{number}",
        "Relay" => $"RL{number}",
        _ => $"{type}{number}"
    };

    public static string ChannelTypeLabel(string type) => type switch
    {
        "DigitalInput" => "Digital in",
        "DigitalOutput" => "Digital out",
        "AnalogInput" => "Analog in",
        "AnalogOutput" => "Analog out",
        "Relay" => "Relay",
        _ => type
    };

    public static string Local(DateTime utc) => DateTime.SpecifyKind(utc, DateTimeKind.Utc).ToLocalTime().ToString("yyyy-MM-dd HH:mm");

    public static string Duration(double seconds) =>
        seconds >= 60 ? $"{(int)(seconds / 60)}m {(int)(seconds % 60):D2}s" : $"{seconds:0}s";
}
