using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Sending.Queue;

public sealed record QuotaStatus(int DailyLimit, int SentToday, int WarmupDay, bool WarmupActive)
{
    public int Remaining => Math.Max(0, DailyLimit - SentToday);
}

public sealed class QuotaService(IDbContextFactory<AlpixaDbContext> dbFactory, IClock clock)
{
    public async Task<QuotaStatus> GetStatusAsync(SenderProfile profile, WarmupPlan plan, CancellationToken ct)
    {
        var sentToday = await CountSentSinceAsync(profile.Id, LocalMidnightUtc(), ct);
        var day = profile.WarmupStartedUtc is null ? 1 : WarmupPlan.DayNumber(profile.WarmupStartedUtc.Value, clock.UtcNow);
        var limit = profile.WarmupEnabled ? Math.Min(profile.DailyLimit, plan.LimitForDay(day)) : profile.DailyLimit;
        return new QuotaStatus(limit, sentToday, day, profile.WarmupEnabled);
    }

    public Task<int> SentInLastHourAsync(int profileId, CancellationToken ct) => CountSentSinceAsync(profileId, clock.UtcNow.AddHours(-1), ct);

    public DateTime NextQuotaResetUtc()
    {
        var local = clock.LocalNow.Date.AddDays(1).AddMinutes(1);
        return DateTime.SpecifyKind(local, DateTimeKind.Local).ToUniversalTime();
    }

    private DateTime LocalMidnightUtc() => DateTime.SpecifyKind(clock.LocalNow.Date, DateTimeKind.Local).ToUniversalTime();

    private async Task<int> CountSentSinceAsync(int profileId, DateTime sinceUtc, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.SendJobs.AsNoTracking()
            .Where(j => j.Status == SendJobStatus.Sent && j.SentUtc >= sinceUtc)
            .Join(db.Campaigns.Where(c => c.SenderProfileId == profileId && !c.DryRun), j => j.CampaignId, c => c.Id, (j, c) => j.Id)
            .CountAsync(ct);
    }
}
