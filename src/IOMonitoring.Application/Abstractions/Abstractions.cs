using IOMonitoring.Domain;
using Microsoft.EntityFrameworkCore;

namespace IOMonitoring.Application.Abstractions;

/// <summary>Persistence port used by the CQRS handlers. Implemented in Infrastructure.</summary>
public interface IAppDbContext
{
    DbSet<Device> Devices { get; }
    DbSet<TestRun> TestRuns { get; }
    DbSet<ChannelResult> ChannelResults { get; }
    DbSet<AppUser> Users { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);
}

public interface ITokenService
{
    (string Token, DateTime ExpiresAt) CreateToken(AppUser user);
}
