using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using StepTrail.Shared;

namespace StepTrail.TestLab;

public sealed class TestLabStartupService : IHostedService
{
    private readonly IOptions<TestLabOptions> _options;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TestLabBootstrapper _bootstrapper;
    private readonly ILogger<TestLabStartupService> _logger;

    public TestLabStartupService(
        IOptions<TestLabOptions> options,
        IServiceScopeFactory scopeFactory,
        TestLabBootstrapper bootstrapper,
        ILogger<TestLabStartupService> logger)
    {
        _options = options;
        _scopeFactory = scopeFactory;
        _bootstrapper = bootstrapper;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.AutoSetupOnStartup)
            return;

        await WaitForDatabaseAsync(cancellationToken);

        try
        {
            var status = await _bootstrapper.EnsureDemoAssetsAsync(cancellationToken);
            _logger.LogWarning("TestLab startup setup completed. {Status}", status);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "TestLab startup setup failed.");
        }
    }

    private async Task WaitForDatabaseAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<StepTrailDbContext>();

        for (var attempt = 1; attempt <= 30; attempt++)
        {
            try
            {
                await db.WorkflowSecrets.AnyAsync(ct);
                _logger.LogInformation("TestLab database ready after {Attempts} check(s)", attempt);
                return;
            }
            catch (Exception) when (!ct.IsCancellationRequested)
            {
                if (attempt == 1)
                    _logger.LogInformation("TestLab waiting for database schema...");
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }

        _logger.LogWarning("TestLab proceeding after database wait timeout");
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
