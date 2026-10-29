using IOMonitoring.Application.Abstractions;
using IOMonitoring.Infrastructure.Persistence;
using IOMonitoring.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace IOMonitoring.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var databaseName = configuration["Database:Name"] ?? "IOMonitoring";

        // Draft project: in-memory database. Swap for UseSqlServer(...) later without touching the handlers.
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher, Pbkdf2PasswordHasher>();

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<ITokenService, JwtTokenService>();

        return services;
    }

    /// <summary>Creates the in-memory database and fills it with dummy data.</summary>
    public static async Task SeedDatabaseAsync(this IServiceProvider services, CancellationToken cancellationToken = default)
    {
        using var scope = services.CreateScope();
        var provider = scope.ServiceProvider;

        await DataSeeder.SeedAsync(
            provider.GetRequiredService<AppDbContext>(),
            provider.GetRequiredService<IPasswordHasher>(),
            provider.GetRequiredService<TimeProvider>(),
            cancellationToken);
    }
}
