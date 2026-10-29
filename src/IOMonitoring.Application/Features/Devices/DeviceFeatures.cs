using FluentValidation;
using IOMonitoring.Application.Abstractions;
using IOMonitoring.Application.Common;
using IOMonitoring.Contracts;
using IOMonitoring.Domain;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IOMonitoring.Application.Features.Devices;

// ---------------------------------------------------------------------------
// Query: list devices (optional search + status filter)
// ---------------------------------------------------------------------------
public sealed record GetDevicesQuery(string? Search = null, string? Status = null) : IRequest<IReadOnlyList<DeviceDto>>;

internal sealed class GetDevicesHandler(IAppDbContext db) : IRequestHandler<GetDevicesQuery, IReadOnlyList<DeviceDto>>
{
    public async Task<IReadOnlyList<DeviceDto>> Handle(GetDevicesQuery request, CancellationToken cancellationToken)
    {
        IQueryable<Device> query = db.Devices.AsNoTracking().Include(d => d.TestRuns);

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var s = request.Search.Trim().ToLower();
            query = query.Where(d =>
                d.Name.ToLower().Contains(s) ||
                d.SerialNumber.ToLower().Contains(s) ||
                d.Location.ToLower().Contains(s) ||
                d.Model.ToLower().Contains(s));
        }

        if (Enum.TryParse<DeviceStatus>(request.Status, ignoreCase: true, out var status))
        {
            query = query.Where(d => d.Status == status);
        }

        var devices = await query.OrderBy(d => d.SerialNumber).ToListAsync(cancellationToken);
        return devices.Select(d => d.ToDto()).ToList();
    }
}

// ---------------------------------------------------------------------------
// Query: one device with recent runs and a 30-day trend
// ---------------------------------------------------------------------------
public sealed record GetDeviceByIdQuery(Guid Id, int TrendDays = 30, int RecentRuns = 15) : IRequest<DeviceDetailDto?>;

internal sealed class GetDeviceByIdHandler(IAppDbContext db, TimeProvider time)
    : IRequestHandler<GetDeviceByIdQuery, DeviceDetailDto?>
{
    public async Task<DeviceDetailDto?> Handle(GetDeviceByIdQuery request, CancellationToken cancellationToken)
    {
        var device = await db.Devices.AsNoTracking()
            .Include(d => d.TestRuns).ThenInclude(r => r.Channels)
            .FirstOrDefaultAsync(d => d.Id == request.Id, cancellationToken);

        if (device is null)
        {
            return null;
        }

        var recent = device.TestRuns
            .OrderByDescending(r => r.StartedAt)
            .Take(request.RecentRuns)
            .Select(r => r.ToDto(device))
            .ToList();

        var days = Math.Clamp(request.TrendDays, 1, 90);
        var firstDay = DateOnly.FromDateTime(time.GetUtcNow().UtcDateTime).AddDays(-(days - 1));
        var trend = Mappings.BuildDailyTrend(
            device.TestRuns.Select(r => (r.StartedAt, r.Outcome)), firstDay, days);

        return new DeviceDetailDto(device.ToDto(), recent, trend);
    }
}

// ---------------------------------------------------------------------------
// Command: register a new device
// ---------------------------------------------------------------------------
public sealed record CreateDeviceCommand(
    string SerialNumber,
    string Name,
    string Model,
    string FirmwareVersion,
    string Location) : IRequest<DeviceDto>;

internal sealed class CreateDeviceCommandValidator : AbstractValidator<CreateDeviceCommand>
{
    public CreateDeviceCommandValidator()
    {
        RuleFor(x => x.SerialNumber).NotEmpty().MaximumLength(32);
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(50);
        RuleFor(x => x.FirmwareVersion).NotEmpty().MaximumLength(20);
        RuleFor(x => x.Location).NotEmpty().MaximumLength(100);
    }
}

internal sealed class CreateDeviceCommandHandler(IAppDbContext db, TimeProvider time)
    : IRequestHandler<CreateDeviceCommand, DeviceDto>
{
    public async Task<DeviceDto> Handle(CreateDeviceCommand request, CancellationToken cancellationToken)
    {
        var serial = request.SerialNumber.Trim().ToUpperInvariant();

        if (await db.Devices.AnyAsync(d => d.SerialNumber == serial, cancellationToken))
        {
            throw new ConflictException($"A device with serial number '{serial}' already exists.");
        }

        var device = new Device
        {
            Id = Guid.NewGuid(),
            SerialNumber = serial,
            Name = request.Name.Trim(),
            Model = request.Model.Trim(),
            FirmwareVersion = request.FirmwareVersion.Trim(),
            Location = request.Location.Trim(),
            Status = DeviceStatus.Active,
            CommissionedAt = time.GetUtcNow().UtcDateTime
        };

        db.Devices.Add(device);
        await db.SaveChangesAsync(cancellationToken);

        return device.ToDto();
    }
}
