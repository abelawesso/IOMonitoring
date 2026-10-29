using IOMonitoring.Web;
using IOMonitoring.Web.Services;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MudBlazor.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddMudServices();

// ----- Authentication (JWT kept in localStorage) -----
builder.Services.AddAuthorizationCore();
builder.Services.AddSingleton<TokenStore>();
builder.Services.AddScoped<JwtAuthenticationStateProvider>();
builder.Services.AddScoped<AuthenticationStateProvider>(sp => sp.GetRequiredService<JwtAuthenticationStateProvider>());
builder.Services.AddScoped<AuthService>();

// ----- API access (same origin: the API host serves this client) -----
builder.Services.AddTransient<BearerTokenHandler>();
builder.Services
    .AddHttpClient(ApiClient.HttpClientName, client => client.BaseAddress = new Uri(builder.HostEnvironment.BaseAddress))
    .AddHttpMessageHandler<BearerTokenHandler>();
builder.Services.AddScoped<ApiClient>();

await builder.Build().RunAsync();
