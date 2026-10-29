using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using IOMonitoring.Contracts;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.JSInterop;

namespace IOMonitoring.Web.Services;

/// <summary>Claim names issued by the API (see IOMonitoring.Infrastructure.Security.AppClaims).</summary>
public static class AppClaims
{
    public const string Username = "unique_name";
    public const string DisplayName = "display_name";
    public const string Role = "role";
}

/// <summary>
/// Keeps the JWT in localStorage. Registered as a singleton because HttpClientFactory
/// builds message handlers in their own DI scope.
/// </summary>
public sealed class TokenStore(IJSRuntime js)
{
    private const string Key = "iomonitoring.token";
    private string? _cached;
    private bool _loaded;

    public async ValueTask<string?> GetTokenAsync()
    {
        if (!_loaded)
        {
            _cached = await js.InvokeAsync<string?>("localStorage.getItem", Key);
            _loaded = true;
        }

        return _cached;
    }

    public async ValueTask SetTokenAsync(string token)
    {
        _cached = token;
        _loaded = true;
        await js.InvokeVoidAsync("localStorage.setItem", Key, token);
    }

    public async ValueTask ClearAsync()
    {
        _cached = null;
        _loaded = true;
        await js.InvokeVoidAsync("localStorage.removeItem", Key);
    }
}

/// <summary>Builds the Blazor user from the JWT payload (signature is verified by the API, not here).</summary>
public sealed class JwtAuthenticationStateProvider(TokenStore store) : AuthenticationStateProvider
{
    private static readonly AuthenticationState Anonymous = new(new ClaimsPrincipal(new ClaimsIdentity()));

    public override async Task<AuthenticationState> GetAuthenticationStateAsync()
    {
        var token = await store.GetTokenAsync();
        if (string.IsNullOrWhiteSpace(token))
        {
            return Anonymous;
        }

        var claims = JwtPayload.ReadClaims(token);
        if (claims.Count == 0 || IsExpired(claims))
        {
            await store.ClearAsync();
            return Anonymous;
        }

        var identity = new ClaimsIdentity(claims, "jwt", AppClaims.Username, AppClaims.Role);
        return new AuthenticationState(new ClaimsPrincipal(identity));
    }

    public async Task SignInAsync(string token)
    {
        await store.SetTokenAsync(token);
        NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
    }

    public async Task SignOutAsync()
    {
        await store.ClearAsync();
        NotifyAuthenticationStateChanged(Task.FromResult(Anonymous));
    }

    private static bool IsExpired(IEnumerable<Claim> claims)
    {
        var exp = claims.FirstOrDefault(c => c.Type == "exp")?.Value;
        return long.TryParse(exp, out var seconds)
               && DateTimeOffset.FromUnixTimeSeconds(seconds) <= DateTimeOffset.UtcNow;
    }
}

internal static class JwtPayload
{
    public static List<Claim> ReadClaims(string jwt)
    {
        var claims = new List<Claim>();
        var parts = jwt.Split('.');
        if (parts.Length < 2)
        {
            return claims;
        }

        try
        {
            var json = DecodeBase64Url(parts[1]);
            using var doc = JsonDocument.Parse(json);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Array)
                {
                    claims.AddRange(property.Value.EnumerateArray().Select(v => new Claim(property.Name, ValueOf(v))));
                }
                else
                {
                    claims.Add(new Claim(property.Name, ValueOf(property.Value)));
                }
            }
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            claims.Clear();
        }

        return claims;
    }

    private static string ValueOf(JsonElement e) =>
        e.ValueKind == JsonValueKind.String ? e.GetString() ?? string.Empty : e.GetRawText();

    private static byte[] DecodeBase64Url(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        s = (s.Length % 4) switch
        {
            2 => s + "==",
            3 => s + "=",
            _ => s
        };
        return Convert.FromBase64String(s);
    }
}

/// <summary>Adds "Authorization: Bearer {token}" to every API call.</summary>
public sealed class BearerTokenHandler(TokenStore store) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var token = await store.GetTokenAsync();
        if (!string.IsNullOrWhiteSpace(token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await base.SendAsync(request, cancellationToken);
    }
}

public sealed class AuthService(IHttpClientFactory httpFactory, JwtAuthenticationStateProvider authState)
{
    public async Task<(bool Success, string? Error)> LoginAsync(string username, string password)
    {
        try
        {
            var http = httpFactory.CreateClient(ApiClient.HttpClientName);
            using var response = await http.PostAsJsonAsync("api/auth/login", new LoginRequest(username, password));

            if (!response.IsSuccessStatusCode)
            {
                return (false, response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                    ? "Invalid username or password."
                    : $"Login failed ({(int)response.StatusCode}).");
            }

            var login = await response.Content.ReadFromJsonAsync<LoginResponse>();
            if (login is null)
            {
                return (false, "Empty response from server.");
            }

            await authState.SignInAsync(login.Token);
            return (true, null);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Cannot reach the API: {ex.Message}");
        }
    }

    public Task LogoutAsync() => authState.SignOutAsync();
}
