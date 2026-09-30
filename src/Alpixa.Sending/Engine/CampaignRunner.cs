using Alpixa.Core.Localization;
using System.Collections.Concurrent;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.Sending.Engine;

public sealed class CampaignRunner(
    SendEngine engine,
    IDbContextFactory<AlpixaDbContext> dbFactory,
    IPlatformService platform,
    ILogger<CampaignRunner> logger)
{
    private enum StopRequest { None, Pause, Cancel, Shutdown }

    private sealed class Running(CancellationTokenSource cts)
    {
        public CancellationTokenSource Cts { get; } = cts;
        public StopRequest Request { get; set; }
        public Task Task { get; set; } = Task.CompletedTask;
    }

    private readonly ConcurrentDictionary<int, Running> _running = new();
    private readonly ConcurrentDictionary<int, CampaignProgress> _lastProgress = new();

    public event EventHandler<CampaignProgress>? ProgressChanged;
    public event EventHandler<int>? StateChanged;

    public bool IsRunning(int campaignId) => _running.ContainsKey(campaignId);
    public bool AnyRunning => !_running.IsEmpty;
    public CampaignProgress? LastProgress(int campaignId) => _lastProgress.GetValueOrDefault(campaignId);

    public Task StartAsync(int campaignId)
    {
        var running = new Running(new CancellationTokenSource());
        if (!_running.TryAdd(campaignId, running)) return Task.CompletedTask;

        UpdatePlatformState();
        var progress = new Progress<CampaignProgress>(p =>
        {
            _lastProgress[campaignId] = p;
            ProgressChanged?.Invoke(this, p);
            UpdatePlatformProgress();
        });

        running.Task = Task.Run(async () =>
        {
            string? notifyTitle = null, notifyBody = null;
            try
            {
                var result = await engine.RunAsync(campaignId, progress, running.Cts.Token);
                (notifyTitle, notifyBody) = result.Status switch
                {
                    CampaignStatus.Completed => (Msg.T("Runner_01"), Msg.T("Runner_02")),
                    CampaignStatus.PausedBySafety => (Msg.T("Runner_03"), result.Reason ?? ""),
                    CampaignStatus.WaitingForQuota => (Msg.T("Runner_04"), result.Reason ?? ""),
                    _ => (null, null)
                };
            }
            catch (OperationCanceledException)
            {
                await ApplyStopRequestAsync(campaignId, running.Request);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Campaign {CampaignId} failed", campaignId);
                await SetStatusAsync(campaignId, CampaignStatus.PausedBySafety,
                    Msg.T("Runner_05"));
                (notifyTitle, notifyBody) = (Msg.T("Runner_06"), Msg.T("Runner_07"));
            }
            finally
            {
                _running.TryRemove(campaignId, out _);
                running.Cts.Dispose();
                UpdatePlatformState();
                StateChanged?.Invoke(this, campaignId);
                if (notifyTitle is not null) platform.ShowNotification(notifyTitle, notifyBody ?? "");
            }
        });

        StateChanged?.Invoke(this, campaignId);
        return Task.CompletedTask;
    }

    public Task PauseAsync(int campaignId) => StopAsync(campaignId, StopRequest.Pause);

    public Task CancelAsync(int campaignId) => StopAsync(campaignId, StopRequest.Cancel);

    public async Task ShutdownAsync()
    {
        foreach (var id in _running.Keys.ToList())
            await StopAsync(id, StopRequest.Shutdown);
    }

    public async Task RecoverAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        var orphaned = await db.SendJobs.Where(j => j.Status == SendJobStatus.Sending)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SendJobStatus.Uncertain)
                .SetProperty(j => j.LastError, Msg.T("Runner_08"))
                .SetProperty(j => j.UpdatedUtc, now), ct);
        if (orphaned > 0) logger.LogWarning("{Count} jobs were in flight at shutdown and marked uncertain", orphaned);

        var toResume = await db.Campaigns.Where(c => c.Status == CampaignStatus.Running).Select(c => c.Id).ToListAsync(ct);
        foreach (var id in toResume) await StartAsync(id);
    }

    private async Task StopAsync(int campaignId, StopRequest request)
    {
        if (_running.TryGetValue(campaignId, out var running))
        {
            running.Request = request;
            await running.Cts.CancelAsync();
            try { await running.Task; } catch (OperationCanceledException) { }
            return;
        }
        if (request != StopRequest.Shutdown) await ApplyStopRequestAsync(campaignId, request);
        StateChanged?.Invoke(this, campaignId);
    }

    private async Task ApplyStopRequestAsync(int campaignId, StopRequest request)
    {
        switch (request)
        {
            case StopRequest.Pause:
                await SetStatusAsync(campaignId, CampaignStatus.Paused, Msg.T("Runner_09"));
                break;
            case StopRequest.Cancel:
                await SetStatusAsync(campaignId, CampaignStatus.Cancelled, Msg.T("Runner_10"));
                await using (var db = await dbFactory.CreateDbContextAsync())
                {
                    await db.SendJobs.Where(j => j.CampaignId == campaignId && j.Status == SendJobStatus.Pending)
                        .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SendJobStatus.Skipped).SetProperty(j => j.LastError, Msg.T("Runner_11")));
                }
                break;
        }
    }

    private async Task SetStatusAsync(int campaignId, CampaignStatus status, string? reason)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        await db.Campaigns.Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status).SetProperty(c => c.PauseReason, reason));
    }

    private void UpdatePlatformState()
    {
        try
        {
            platform.SetKeepAwake(AnyRunning);
            if (!AnyRunning) platform.SetProgress(null);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Platform state update failed");
        }
    }

    private void UpdatePlatformProgress()
    {
        try
        {
            var active = _running.Keys.Select(k => _lastProgress.GetValueOrDefault(k)).Where(p => p is not null).ToList();
            if (active.Count == 0) return;
            var total = active.Sum(p => p!.Total);
            var done = active.Sum(p => p!.Sent + p!.Failed + p!.Skipped);
            platform.SetProgress(total == 0 ? null : (double)done / total);
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Platform progress update failed");
        }
    }
}
