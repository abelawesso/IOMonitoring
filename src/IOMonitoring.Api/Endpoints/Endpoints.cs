using System.Security.Claims;
using IOMonitoring.Api.Infrastructure;
using IOMonitoring.Application.Features.Auth;
using IOMonitoring.Application.Features.Dashboard;
using IOMonitoring.Application.Features.Devices;
using IOMonitoring.Application.Features.TestRuns;
using IOMonitoring.Contracts;
using IOMonitoring.Infrastructure.Security;
using MediatR;
using Microsoft.AspNetCore.Http.HttpResults;

namespace IOMonitoring.Api.Endpoints;

// Each endpoint only translates HTTP <-> MediatR request. All logic lives in the Application layer.

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth").WithTags("Auth");

        group.MapPost("/login", async Task<Results<Ok<LoginResponse>, UnauthorizedHttpResult>> (
                LoginRequest request, ISender sender, CancellationToken ct) =>
            {
                var result = await sender.Send(new LoginCommand(request.Username, request.Password), ct);
                return result is null ? TypedResults.Unauthorized() : TypedResults.Ok(result);
            })
            .AllowAnonymous();

        group.MapGet("/me", (ClaimsPrincipal user) => TypedResults.Ok(new CurrentUserDto(
                user.FindFirstValue(AppClaims.Username) ?? string.Empty,
                user.FindFirstValue(AppClaims.DisplayName) ?? string.Empty,
                user.FindFirstValue(AppClaims.Role) ?? string.Empty)))
            .RequireAuthorization();

        return app;
    }
}

public static class DashboardEndpoints
{
    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/dashboard").WithTags("Dashboard").RequireAuthorization();

        group.MapGet("/summary", async (ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetDashboardSummaryQuery(), ct)));

        group.MapGet("/trend", async (int? days, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetDailyTrendQuery(days ?? 14), ct)));

        group.MapGet("/channel-failures", async (ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetChannelFailuresQuery(), ct)));

        group.MapGet("/stations", async (ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetStationStatsQuery(), ct)));

        return app;
    }
}

public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/devices").WithTags("Devices").RequireAuthorization();

        group.MapGet("/", async (string? search, string? status, ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(new GetDevicesQuery(search, status), ct)));

        group.MapGet("/{id:guid}", async Task<Results<Ok<DeviceDetailDto>, NotFound>> (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var device = await sender.Send(new GetDeviceByIdQuery(id), ct);
            return device is null ? TypedResults.NotFound() : TypedResults.Ok(device);
        });

        group.MapPost("/", async (CreateDeviceRequest request, ISender sender, CancellationToken ct) =>
            {
                var device = await sender.Send(new CreateDeviceCommand(
                    request.SerialNumber, request.Name, request.Model, request.FirmwareVersion, request.Location), ct);
                return TypedResults.Created($"/api/devices/{device.Id}", device);
            })
            .RequireAuthorization(Policies.CanWrite);

        return app;
    }
}

public static class TestRunEndpoints
{
    public static IEndpointRouteBuilder MapTestRunEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/test-runs").WithTags("Test runs").RequireAuthorization();

        group.MapGet("/", async (
                Guid? deviceId, string? outcome, DateTime? from, DateTime? to, int? page, int? pageSize,
                ISender sender, CancellationToken ct) =>
            TypedResults.Ok(await sender.Send(
                new GetTestRunsQuery(deviceId, outcome, from, to, page ?? 1, pageSize ?? 20), ct)));

        group.MapGet("/{id:guid}", async Task<Results<Ok<TestRunDetailDto>, NotFound>> (
            Guid id, ISender sender, CancellationToken ct) =>
        {
            var run = await sender.Send(new GetTestRunByIdQuery(id), ct);
            return run is null ? TypedResults.NotFound() : TypedResults.Ok(run);
        });

        group.MapPost("/", async (RecordTestRunRequest request, ISender sender, CancellationToken ct) =>
            {
                var run = await sender.Send(new RecordTestRunCommand(
                    request.DeviceId, request.Station, request.Operator,
                    request.StartedAt, request.CompletedAt, request.Notes, request.Channels), ct);
                return TypedResults.Created($"/api/test-runs/{run.Id}", run);
            })
            .RequireAuthorization(Policies.CanWrite);

        return app;
    }
}
