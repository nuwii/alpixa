using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Infrastructure.Bounces;

public sealed class DeliveryEventService(IDbContextFactory<AlpixaDbContext> dbFactory, ISuppressionService suppression)
{
    public async Task RecordAsync(ParsedFeedback feedback, string source, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var campaignId = feedback.CampaignId;
        if (campaignId is null && feedback.OriginalMessageId is not null)
        {
            campaignId = await db.SendJobs.AsNoTracking()
                .Where(j => j.MessageId == feedback.OriginalMessageId)
                .Select(j => (int?)j.CampaignId)
                .FirstOrDefaultAsync(ct);
        }
        if (campaignId is null)
        {
            campaignId = await db.SendJobs.AsNoTracking()
                .Where(j => j.Email == feedback.Email && j.Status == SendJobStatus.Sent)
                .OrderByDescending(j => j.SentUtc)
                .Select(j => (int?)j.CampaignId)
                .FirstOrDefaultAsync(ct);
        }

        if (feedback.Kind == DeliveryEventKind.Open && campaignId is not null &&
            await db.DeliveryEvents.AnyAsync(e => e.EmailNormalized == feedback.Email && e.CampaignId == campaignId && e.Kind == DeliveryEventKind.Open, ct))
            return;

        db.DeliveryEvents.Add(new DeliveryEvent
        {
            EmailNormalized = feedback.Email,
            Kind = feedback.Kind,
            CampaignId = campaignId,
            Diagnostic = feedback.Status is null ? feedback.Diagnostic : $"{feedback.Status} {feedback.Diagnostic}".Trim(),
            Source = source
        });
        await db.SaveChangesAsync(ct);

        switch (feedback.Kind)
        {
            case DeliveryEventKind.HardBounce:
                await suppression.SuppressAsync(feedback.Email, SuppressionReason.HardBounce, source, campaignId, ct);
                break;
            case DeliveryEventKind.Complaint:
                await suppression.SuppressAsync(feedback.Email, SuppressionReason.Complaint, source, campaignId, ct);
                break;
            case DeliveryEventKind.Unsubscribe:
                await suppression.SuppressAsync(feedback.Email, SuppressionReason.Unsubscribed, source, campaignId, ct);
                break;
            case DeliveryEventKind.SoftBounce:
                await db.Contacts.Where(c => c.EmailNormalized == feedback.Email)
                    .ExecuteUpdateAsync(s => s.SetProperty(c => c.SoftBounceCount, c => c.SoftBounceCount + 1), ct);
                var maxSoft = await db.Contacts.Where(c => c.EmailNormalized == feedback.Email)
                    .Select(c => (int?)c.SoftBounceCount).MaxAsync(ct) ?? 0;
                if (maxSoft >= DeliverabilityThresholds.SoftBounceSuppressAfter)
                    await suppression.SuppressAsync(feedback.Email, SuppressionReason.SoftBounceLimit, source, campaignId, ct);
                break;
        }
    }
}
