using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using IOMonitoring.Contracts;
using Microsoft.AspNetCore.Components;

namespace IOMonitoring.Web.Services;

public sealed record TestRunFilter(
    Guid? DeviceId = null,
    string? Outcome = null,
    DateTime? From = null,
    DateTime? To = null,
    int Page = 1,
    int PageSize = 20);

/// <summary>Typed wrapper around the Minimal API. A 401 signs the user out and returns to the login page.</summary>
public sealed class ApiClient(IHttpClientFactory httpFactory, AuthService auth, NavigationManager nav)
{
    public const string HttpClientName = "IOMonitoring.Api";

    private HttpClient Http => httpFactory.CreateClient(HttpClientName);

    // ----- Dashboard -----
    public Task<DashboardSummaryDto?> GetSummaryAsync() => GetAsync<DashboardSummaryDto>("api/dashboard/summary");

    public Task<List<DailyTrendPointDto>?> GetTrendAsync(int days) => GetAsync<List<DailyTrendPointDto>>($"api/dashboard/trend?days={days}");

    public Task<List<ChannelFailureDto>?> GetChannelFailuresAsync() => GetAsync<List<ChannelFailureDto>>("api/dashboard/channel-failures");

    public Task<List<StationStatsDto>?> GetStationStatsAsync() => GetAsync<List<StationStatsDto>>("api/dashboard/stations");

    // ----- Devices -----
    public Task<List<DeviceDto>?> GetDevicesAsync() => GetAsync<List<DeviceDto>>("api/devices");

    public Task<DeviceDetailDto?> GetDeviceAsync(Guid id) => GetAsync<DeviceDetailDto>($"api/devices/{id}");

    public async Task<(DeviceDto? Device, string? Error)> CreateDeviceAsync(CreateDeviceRequest request)
    {
        using var response = await Http.PostAsJsonAsync("api/devices", request);
        if (await HandleUnauthorizedAsync(response))
        {
            return (null, "Session expired.");
        }

        if (response.StatusCode == HttpStatusCode.Forbidden)
        {
            return (null, "Your role is not allowed to add devices.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return (null, await ReadProblemAsync(response));
        }

        return (await response.Content.ReadFromJsonAsync<DeviceDto>(), null);
    }

    // ----- Test runs -----
    public Task<PagedResult<TestRunDto>?> GetTestRunsAsync(TestRunFilter filter)
    {
        var query = new List<string>
        {
            $"page={filter.Page}",
            $"pageSize={filter.PageSize}"
        };
        if (filter.DeviceId is { } deviceId) query.Add($"deviceId={deviceId}");
        if (!string.IsNullOrWhiteSpace(filter.Outcome)) query.Add($"outcome={Uri.EscapeDataString(filter.Outcome)}");
        if (filter.From is { } from) query.Add($"from={from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");
        if (filter.To is { } to) query.Add($"to={to.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}");

        return GetAsync<PagedResult<TestRunDto>>("api/test-runs?" + string.Join("&", query));
    }

    public Task<TestRunDetailDto?> GetTestRunAsync(Guid id) => GetAsync<TestRunDetailDto>($"api/test-runs/{id}");

    // ----- Helpers -----
    private async Task<T?> GetAsync<T>(string url)
    {
        using var response = await Http.GetAsync(url);

        if (await HandleUnauthorizedAsync(response) || response.StatusCode == HttpStatusCode.NotFound)
        {
            return default;
        }

        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<T>();
    }

    private async Task<bool> HandleUnauthorizedAsync(HttpResponseMessage response)
    {
        if (response.StatusCode != HttpStatusCode.Unauthorized)
        {
            return false;
        }

        await auth.LogoutAsync();
        nav.NavigateTo("login");
        return true;
    }

    private static async Task<string> ReadProblemAsync(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var root = doc.RootElement;
            var sb = new StringBuilder();

            if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in errors.EnumerateObject())
                {
                    foreach (var message in field.Value.EnumerateArray())
                    {
                        sb.AppendLine(message.GetString());
                    }
                }
            }
            else if (root.TryGetProperty("detail", out var detail))
            {
                sb.Append(detail.GetString());
            }
            else if (root.TryGetProperty("title", out var title))
            {
                sb.Append(title.GetString());
            }

            return sb.Length > 0 ? sb.ToString().Trim() : $"Request failed ({(int)response.StatusCode}).";
        }
        catch (JsonException)
        {
            return $"Request failed ({(int)response.StatusCode}).";
        }
    }
}
