using Microsoft.Extensions.Options;

namespace StepTrail.TestLab;

public sealed class TestLabStartupService : IHostedService
{
    private readonly IOptions<TestLabOptions> _options;
    private readonly TestLabBootstrapper _bootstrapper;
    private readonly ILogger<TestLabStartupService> _logger;

    public TestLabStartupService(
        IOptions<TestLabOptions> options,
        TestLabBootstrapper bootstrapper,
        ILogger<TestLabStartupService> logger)
    {
        _options = options;
        _bootstrapper = bootstrapper;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.AutoSetupOnStartup)
            return;

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

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
