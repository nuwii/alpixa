using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Settings;
using Alpixa.Sending.Engine;
using Microsoft.Extensions.Logging;

namespace Alpixa.App.Services;

public sealed class AppStartup(
    DatabaseInitializer database,
    CampaignRunner runner,
    BackgroundJobs background,
    SettingsStore settings,
    ILogger<AppStartup> logger)
{
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _started;

    public Task Ready => _ready.Task;

    public void Start()
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _ = Task.Run(async () =>
        {
            try
            {
                await database.InitializeAsync();
                _ready.TrySetResult();
                await runner.RecoverAsync();
                background.Start();
                logger.LogInformation("Alpixa started");
            }
            catch (Exception ex)
            {
                logger.LogCritical(ex, "Startup failed");
                _ready.TrySetException(ex);
            }
        });
    }

    public async Task<bool> NeedsSetupAsync()
    {
        await Ready;
        return !(await settings.GetGeneralAsync()).SetupCompleted;
    }

    public async Task StopAsync()
    {
        background.Stop();
        await runner.ShutdownAsync();
    }
}
