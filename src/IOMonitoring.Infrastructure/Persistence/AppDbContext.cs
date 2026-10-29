using IOMonitoring.Application.Abstractions;
using IOMonitoring.Domain;
using Microsoft.EntityFrameworkCore;

namespace IOMonitoring.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<Device> Devices => Set<Device>();
    public DbSet<TestRun> TestRuns => Set<TestRun>();
    public DbSet<ChannelResult> ChannelResults => Set<ChannelResult>();
    public DbSet<AppUser> Users => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Device>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.SerialNumber).IsUnique();
            e.Property(x => x.SerialNumber).HasMaxLength(32).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Model).HasMaxLength(50).IsRequired();
            e.Property(x => x.FirmwareVersion).HasMaxLength(20);
            e.Property(x => x.Location).HasMaxLength(100);
            e.HasMany(x => x.TestRuns)
                .WithOne(r => r.Device)
                .HasForeignKey(r => r.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TestRun>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.StartedAt);
            e.Property(x => x.Station).HasMaxLength(50).IsRequired();
            e.Property(x => x.Operator).HasMaxLength(100).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(1000);
            e.HasMany(x => x.Channels)
                .WithOne(c => c.TestRun)
                .HasForeignKey(c => c.TestRunId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChannelResult>(e => e.HasKey(x => x.Id));

        modelBuilder.Entity<AppUser>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.Username).IsUnique();
            e.Property(x => x.Username).HasMaxLength(64).IsRequired();
            e.Property(x => x.DisplayName).HasMaxLength(100);
        });
    }
}
