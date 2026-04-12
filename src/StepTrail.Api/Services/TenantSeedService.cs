using Microsoft.EntityFrameworkCore;
using StepTrail.Shared;
using StepTrail.Shared.Entities;

namespace StepTrail.Api.Services;

/// <summary>
/// Seeds a default tenant at startup for development and testing.
/// The default tenant ID is stable and can be used directly in API requests.
/// </summary>
public sealed class TenantSeedService : IHostedService
{
    /// <summary>
    /// The stable ID of the default tenant seeded at startup.
    /// </summary>
    public static readonly Guid DefaultTenantId = new("00000000-0000-0000-0000-000000000001");

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TenantSeedService> _logger;

    public TenantSeedService(IServiceScopeFactory scopeFactory, ILogger<TenantSeedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();

        var exists = await db.Tenants.AnyAsync(t => t.Id == DefaultTenantId, cancellationToken);
        if (exists)
            return;

        db.Tenants.Add(new Tenant
        {
            Id = DefaultTenantId,
            Name = "Default",
            CreatedAt = DateTimeOffset.UtcNow
        });

        await db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Seeded default tenant (Id: {TenantId})", DefaultTenantId);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
