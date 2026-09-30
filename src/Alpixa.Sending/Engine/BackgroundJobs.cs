using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Options;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.Sending.Engine;

public sealed class BackgroundJobs(
    CampaignRunner runner,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    IBounceProcessor bounces,
    IUnsubscribeSync unsubscribeSync,
    AlpixaOptions options,
    IClock clock,
    ILogger<BackgroundJobs> logger)
{
    private CancellationTokenSource? _cts;

    public DateTime? LastFeedbackCheckUtc { get; private set; }

    public void Start()
    {
        if (_cts is not null) return;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => SchedulerLoopAsync(_cts.Token));
        _ = Task.Run(() => FeedbackLoopAsync(_cts.Token));
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts = null;
    }

    public async Task RunSchedulerOnceAsync(CancellationToken ct)
    {
        var now = clock.UtcNow;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var due = await db.Campaigns.AsNoTracking()
            .Where(c => (c.Status == CampaignStatus.Scheduled && c.ScheduledUtc <= now) ||
                        (c.Status == CampaignStatus.WaitingForQuota && c.NextRunUtc <= now))
            .Select(c => c.Id)
            .ToListAsync(ct);
        foreach (var id in due)
        {
            logger.LogInformation("Starting due campaign {CampaignId}", id);
            await runner.StartAsync(id);
        }
    }

    public async Task<int> RunFeedbackOnceAsync(CancellationToken ct)
    {
        var handled = 0;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            var profiles = await db.SenderProfiles.AsNoTracking().Where(p => p.ImapEnabled).ToListAsync(ct);
            foreach (var profile in profiles)
            {
                try
                {
                    handled += await bounces.ProcessAsync(profile, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogWarning(ex, "Bounce processing failed for profile {ProfileId}", profile.Id);
                }
            }
        }

        try
        {
            handled += await unsubscribeSync.SyncAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Unsubscribe endpoint sync failed");
        }

        LastFeedbackCheckUtc = clock.UtcNow;
        return handled;
    }

    private async Task SchedulerLoopAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
        do
        {
            try
            {
                await RunSchedulerOnceAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Scheduler iteration failed");
            }
        } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }

    private async Task FeedbackLoopAsync(CancellationToken ct)
    {
        await Task.Delay(TimeSpan.FromSeconds(20), ct);
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.BackgroundCheckMinutes)));
        do
        {
            try
            {
                await RunFeedbackOnceAsync(ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogError(ex, "Feedback iteration failed");
            }
        } while (await timer.WaitForNextTickAsync(ct).ConfigureAwait(false));
    }
}
