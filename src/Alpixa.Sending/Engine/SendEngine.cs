using Alpixa.Core.Localization;
using System.Collections.Concurrent;
using System.Threading.Channels;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Core.Settings;
using Alpixa.Infrastructure.Bounces;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Settings;
using Alpixa.Sending.Queue;
using Alpixa.Sending.Throttling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.CircuitBreaker;
using Polly.Retry;

namespace Alpixa.Sending.Engine;

public sealed record RunResult(CampaignStatus Status, string? Reason = null, DateTime? NextRunUtc = null);

public sealed class SendEngine(
    IDbContextFactory<AlpixaDbContext> dbFactory,
    IMailTransportFactory transports,
    IMessageComposer composer,
    SettingsStore settings,
    QuotaService quota,
    CampaignQueueBuilder queueBuilder,
    ThrottleRegistry throttles,
    DeliveryEventService events,
    IConsentProvider consent,
    IClock clock,
    IDelayProvider delay,
    ILogger<SendEngine> logger)
{
    public async Task<RunResult> RunAsync(int campaignId, IProgress<CampaignProgress>? progress, CancellationToken ct)
    {
        Campaign campaign;
        SenderProfile profile;
        await using (var db = await dbFactory.CreateDbContextAsync(ct))
        {
            campaign = await db.Campaigns.AsNoTracking().FirstAsync(c => c.Id == campaignId, ct);
            profile = await db.SenderProfiles.AsNoTracking().FirstAsync(p => p.Id == campaign.SenderProfileId, ct);
        }

        await queueBuilder.BuildAsync(campaignId, ct);
        var sending = await settings.GetSendingAsync(ct);
        await UpdateCampaignAsync(campaignId, CampaignStatus.Running, null, null, ct);

        var dry = campaign.DryRun;
        var quotaStatus = await quota.GetStatusAsync(profile, sending.Warmup, ct);
        var remaining = dry ? int.MaxValue : quotaStatus.Remaining;
        if (remaining <= 0)
            return await FinishWaitingForQuotaAsync(campaignId, quotaStatus, ct);

        var throttle = dry ? null : throttles.Get(profile, sending, await quota.SentInLastHourAsync(profile.Id, ct));
        var workerCount = campaign.Mode == SendMode.Sequential ? 1 : Math.Clamp(campaign.Parallelism, 1, Math.Max(1, sending.MaxBulkParallelism));
        var context = await composer.CreateContextAsync(ct);
        var stats = await RunStats.LoadAsync(dbFactory, campaignId, clock, ct);
        var pipeline = BuildPipeline(sending);

        using var stop = CancellationTokenSource.CreateLinkedTokenSource(ct);
        string? safetyReason = null;
        var quotaExhausted = 0;
        var warmupMarked = profile.WarmupStartedUtc is not null;

        var channel = Channel.CreateBounded<(SendJob Job, Contact Contact)>(new BoundedChannelOptions(Math.Max(8, workerCount * 4))
        {
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.Wait
        });

        var producer = Task.Run(() => ProduceAsync(campaignId, channel.Writer, stop.Token), CancellationToken.None);

        async Task WorkerAsync()
        {
            await using var transport = await transports.CreateAsync(profile, dry, stop.Token);
            await foreach (var (job, contact) in channel.Reader.ReadAllAsync(stop.Token))
            {
                if (Interlocked.Decrement(ref remaining) < 0)
                {
                    Interlocked.Exchange(ref quotaExhausted, 1);
                    await stop.CancelAsync();
                    break;
                }

                if (throttle is not null) await throttle.WaitForSlotAsync(job.RecipientDomain, stop.Token);

                if (await consent.CheckAsync(job.Email, stop.Token) == ConsentCheckResult.Denied)
                {
                    Interlocked.Increment(ref remaining);
                    await MarkAsync(job.Id, SendJobStatus.Skipped, null, Msg.T("Engine_01"), null, CancellationToken.None);
                    stats.Skipped(progress);
                    continue;
                }

                if (!await ClaimAsync(job.Id, stop.Token))
                {
                    Interlocked.Increment(ref remaining);
                    continue;
                }

                var message = composer.Compose(campaign, profile, contact, context);
                SendResult result;
                var inFlight = false;
                try
                {
                    result = await pipeline.ExecuteAsync(async token =>
                    {
                        inFlight = true;
                        try
                        {
                            return await transport.SendAsync(message, token);
                        }
                        finally
                        {
                            inFlight = false;
                        }
                    }, ct);
                }
                catch (BrokenCircuitException)
                {
                    await MarkAsync(job.Id, SendJobStatus.Pending, null, null, null, CancellationToken.None);
                    safetyReason ??= Msg.T("Engine_02", sending.ErrorRateThreshold * 100);
                    await stop.CancelAsync();
                    break;
                }
                catch (OperationCanceledException)
                {
                    if (inFlight)
                        await MarkAsync(job.Id, SendJobStatus.Uncertain, null, Msg.T("Engine_03"), message.MessageId, CancellationToken.None);
                    else
                        await MarkAsync(job.Id, SendJobStatus.Pending, null, null, null, CancellationToken.None);
                    throw;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unexpected send error in campaign {CampaignId}", campaignId);
                    await MarkAsync(job.Id, SendJobStatus.Pending, null, null, null, CancellationToken.None);
                    var error = ErrorTranslator.Translate(ex, profile);
                    safetyReason ??= $"{error.Message} {error.WhatToDo}";
                    await stop.CancelAsync();
                    break;
                }

                await CompleteAsync(job, result, message.MessageId);

                if (result.Success)
                {
                    throttle?.OnSuccess();
                    stats.Sent(progress);
                    if (!dry && !warmupMarked)
                    {
                        warmupMarked = true;
                        await MarkWarmupStartedAsync(profile.Id);
                    }
                }
                else
                {
                    Interlocked.Increment(ref remaining);
                    stats.Failed(progress);
                    if (result.Outcome == SendOutcome.RateLimited) throttle?.OnRateLimited();
                    if (!dry && result.Outcome == SendOutcome.PermanentFailure && SmtpResponseClassifier.IsRecipientProblem(result.SmtpCode ?? 550, result.EnhancedStatus))
                        await events.RecordAsync(new ParsedFeedback(job.Email, DeliveryEventKind.HardBounce, result.EnhancedStatus, result.Response, message.MessageId, campaignId), "smtp", CancellationToken.None);
                    if (result.Outcome == SendOutcome.AuthenticationFailure)
                    {
                        safetyReason ??= Msg.T("Engine_04");
                        await stop.CancelAsync();
                        break;
                    }
                }

                if (!dry && stats.SentCount % 50 == 0 && stats.SentCount >= DeliverabilityThresholds.RateCheckMinimumSent)
                {
                    var reason = await CheckReputationAsync(campaignId, stats.SentCount, sending);
                    if (reason is not null)
                    {
                        safetyReason ??= reason;
                        await stop.CancelAsync();
                        break;
                    }
                }

                if (campaign.Mode == SendMode.Sequential && !dry)
                {
                    var min = Math.Max(0, sending.SequentialDelayMinSeconds);
                    var max = Math.Max(min, sending.SequentialDelayMaxSeconds);
                    await delay.DelayAsync(TimeSpan.FromSeconds(min + Random.Shared.NextDouble() * (max - min)), stop.Token);
                }
            }
        }

        var workers = Enumerable.Range(0, workerCount).Select(_ => Task.Run(WorkerAsync, CancellationToken.None)).ToList();
        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
        }
        finally
        {
            await stop.CancelAsync();
            try { await producer; } catch (OperationCanceledException) { }
        }

        ct.ThrowIfCancellationRequested();

        if (safetyReason is not null)
        {
            await UpdateCampaignAsync(campaignId, CampaignStatus.PausedBySafety, safetyReason, null, CancellationToken.None);
            return new RunResult(CampaignStatus.PausedBySafety, safetyReason);
        }

        if (quotaExhausted == 1)
            return await FinishWaitingForQuotaAsync(campaignId, await quota.GetStatusAsync(profile, sending.Warmup, CancellationToken.None), CancellationToken.None);

        var pending = await CountPendingAsync(campaignId);
        if (pending == 0)
        {
            await UpdateCampaignAsync(campaignId, CampaignStatus.Completed, null, null, CancellationToken.None, completed: true);
            return new RunResult(CampaignStatus.Completed);
        }

        await UpdateCampaignAsync(campaignId, CampaignStatus.Paused, Msg.T("Engine_05"), null, CancellationToken.None);
        return new RunResult(CampaignStatus.Paused);
    }

    private ResiliencePipeline<SendResult> BuildPipeline(SendingSettings sending)
    {
        return new ResiliencePipelineBuilder<SendResult>()
            .AddCircuitBreaker(new CircuitBreakerStrategyOptions<SendResult>
            {
                FailureRatio = Math.Clamp(sending.ErrorRateThreshold, 0.01, 1.0),
                MinimumThroughput = Math.Max(2, sending.ErrorRateMinimumSamples),
                SamplingDuration = TimeSpan.FromMinutes(30),
                BreakDuration = TimeSpan.FromHours(1),
                ShouldHandle = args => ValueTask.FromResult(
                    args.Outcome.Exception is not null and not OperationCanceledException ||
                    args.Outcome.Result is { Success: false })
            })
            .AddRetry(new RetryStrategyOptions<SendResult>
            {
                MaxRetryAttempts = Math.Max(1, sending.MaxRetries),
                BackoffType = DelayBackoffType.Exponential,
                UseJitter = true,
                Delay = TimeSpan.FromSeconds(Math.Max(0, sending.RetryBaseDelaySeconds)),
                ShouldHandle = args => ValueTask.FromResult(args.Outcome.Result is { IsTransient: true })
            })
            .Build();
    }

    private async Task ProduceAsync(int campaignId, ChannelWriter<(SendJob, Contact)> writer, CancellationToken ct)
    {
        try
        {
            var lastSequence = -1;
            while (!ct.IsCancellationRequested)
            {
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var page = await db.SendJobs.AsNoTracking()
                    .Where(j => j.CampaignId == campaignId && j.Status == SendJobStatus.Pending && j.Sequence > lastSequence)
                    .OrderBy(j => j.Sequence)
                    .Take(200)
                    .Join(db.Contacts.AsNoTracking(), j => j.ContactId, c => c.Id, (j, c) => new { j, c })
                    .ToListAsync(ct);
                if (page.Count == 0) break;
                foreach (var row in page)
                    await writer.WriteAsync((row.j, row.c), ct);
                lastSequence = page[^1].j.Sequence;
            }
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private async Task<bool> ClaimAsync(long jobId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        var rows = await db.SendJobs.Where(j => j.Id == jobId && j.Status == SendJobStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SendJobStatus.Sending)
                .SetProperty(j => j.Attempts, j => j.Attempts + 1)
                .SetProperty(j => j.UpdatedUtc, now), ct);
        return rows == 1;
    }

    private async Task CompleteAsync(SendJob job, SendResult result, string? messageId)
    {
        var status = result.Success ? SendJobStatus.Sent : SendJobStatus.Failed;
        var error = result.Success ? null : Truncate($"{ErrorTranslator.DescribeSendFailure(result)} {result.Response}".Trim());
        await MarkAsync(job.Id, status, result.SmtpCode, error, messageId, CancellationToken.None, result.Success ? clock.UtcNow : null);
    }

    private async Task MarkAsync(long jobId, SendJobStatus status, int? code, string? error, string? messageId, CancellationToken ct, DateTime? sentUtc = null)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = clock.UtcNow;
        await db.SendJobs.Where(j => j.Id == jobId)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, status)
                .SetProperty(j => j.SmtpCode, code)
                .SetProperty(j => j.LastError, error)
                .SetProperty(j => j.MessageId, messageId)
                .SetProperty(j => j.SentUtc, sentUtc)
                .SetProperty(j => j.UpdatedUtc, now), ct);
    }

    private async Task MarkWarmupStartedAsync(int profileId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var now = clock.UtcNow;
        await db.SenderProfiles.Where(p => p.Id == profileId && p.WarmupStartedUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.WarmupStartedUtc, now));
    }

    private async Task<string?> CheckReputationAsync(int campaignId, int sent, SendingSettings sending)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var hard = await db.DeliveryEvents.CountAsync(e => e.CampaignId == campaignId && e.Kind == DeliveryEventKind.HardBounce);
        var complaints = await db.DeliveryEvents.CountAsync(e => e.CampaignId == campaignId && e.Kind == DeliveryEventKind.Complaint);
        var bounceRate = (double)hard / sent;
        var complaintRate = (double)complaints / sent;
        if (bounceRate > sending.HardBounceRateLimit)
            return Msg.T("Engine_06", bounceRate * 100, sending.HardBounceRateLimit * 100);
        if (complaintRate > sending.ComplaintRateLimit)
            return Msg.T("Engine_07", complaintRate * 100, sending.ComplaintRateLimit * 100);
        return null;
    }

    private async Task<RunResult> FinishWaitingForQuotaAsync(int campaignId, QuotaStatus status, CancellationToken ct)
    {
        var next = quota.NextQuotaResetUtc();
        var reason = status.WarmupActive
            ? Msg.T("Engine_08", status.DailyLimit, status.WarmupDay)
            : Msg.T("Engine_09", status.DailyLimit);
        await UpdateCampaignAsync(campaignId, CampaignStatus.WaitingForQuota, reason, next, ct);
        return new RunResult(CampaignStatus.WaitingForQuota, reason, next);
    }

    private async Task<int> CountPendingAsync(int campaignId)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.SendJobs.CountAsync(j => j.CampaignId == campaignId && (j.Status == SendJobStatus.Pending || j.Status == SendJobStatus.Sending));
    }

    private async Task UpdateCampaignAsync(int campaignId, CampaignStatus status, string? reason, DateTime? nextRunUtc, CancellationToken ct, bool completed = false)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId, ct);
        campaign.Status = status;
        campaign.PauseReason = reason;
        campaign.NextRunUtc = nextRunUtc;
        if (status == CampaignStatus.Running) campaign.StartedUtc ??= clock.UtcNow;
        if (completed) campaign.CompletedUtc = clock.UtcNow;
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Campaign {CampaignId} -> {Status}", campaignId, status);
    }

    private static string? Truncate(string? s) => s is null ? null : s.Length > 480 ? s[..480] : s;

    private sealed class RunStats
    {
        private readonly int _campaignId;
        private readonly IClock _clock;
        private readonly ConcurrentQueue<DateTime> _recent = new();
        private readonly Lock _lock = new();
        private int _total, _sent, _failed, _pending, _skipped;

        private RunStats(int campaignId, IClock clock)
        {
            _campaignId = campaignId;
            _clock = clock;
        }

        public int SentCount => Volatile.Read(ref _sent);

        public static async Task<RunStats> LoadAsync(IDbContextFactory<AlpixaDbContext> dbFactory, int campaignId, IClock clock, CancellationToken ct)
        {
            await using var db = await dbFactory.CreateDbContextAsync(ct);
            var counts = await db.SendJobs.AsNoTracking().Where(j => j.CampaignId == campaignId)
                .GroupBy(j => j.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
            int C(SendJobStatus s) => counts.FirstOrDefault(c => c.Key == s)?.Count ?? 0;
            return new RunStats(campaignId, clock)
            {
                _total = counts.Sum(c => c.Count),
                _sent = C(SendJobStatus.Sent),
                _failed = C(SendJobStatus.Failed) + C(SendJobStatus.Uncertain),
                _skipped = C(SendJobStatus.Skipped),
                _pending = C(SendJobStatus.Pending) + C(SendJobStatus.Sending)
            };
        }

        public void Sent(IProgress<CampaignProgress>? progress)
        {
            Interlocked.Increment(ref _sent);
            Interlocked.Decrement(ref _pending);
            _recent.Enqueue(_clock.UtcNow);
            Report(progress);
        }

        public void Failed(IProgress<CampaignProgress>? progress)
        {
            Interlocked.Increment(ref _failed);
            Interlocked.Decrement(ref _pending);
            Report(progress);
        }

        public void Skipped(IProgress<CampaignProgress>? progress)
        {
            Interlocked.Increment(ref _skipped);
            Interlocked.Decrement(ref _pending);
            Report(progress);
        }

        private void Report(IProgress<CampaignProgress>? progress)
        {
            if (progress is null) return;
            double perMinute;
            lock (_lock)
            {
                var window = _clock.UtcNow.AddMinutes(-5);
                while (_recent.TryPeek(out var t) && t < window) _recent.TryDequeue(out _);
                var count = _recent.Count;
                var span = count == 0 ? 1 : Math.Max(0.1, (_clock.UtcNow - (_recent.TryPeek(out var first) ? first : _clock.UtcNow)).TotalMinutes);
                perMinute = count <= 1 ? count : count / span;
            }
            var pending = Math.Max(0, Volatile.Read(ref _pending));
            TimeSpan? eta = perMinute > 0 ? TimeSpan.FromMinutes(pending / perMinute) : null;
            progress.Report(new CampaignProgress(_campaignId, CampaignStatus.Running, _total, _sent, _failed, pending, _skipped, perMinute, eta, null));
        }
    }
}
