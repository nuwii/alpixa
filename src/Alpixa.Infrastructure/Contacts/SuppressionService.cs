using Alpixa.Core.Localization;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.Infrastructure.Contacts;

public sealed class SuppressionService(IDbContextFactory<AlpixaDbContext> dbFactory, ILogger<SuppressionService> logger) : ISuppressionService
{
    public async Task<bool> IsSuppressedAsync(string email, CancellationToken ct)
    {
        var normalized = Normalize(email);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Suppressions.AnyAsync(s => s.EmailNormalized == normalized, ct);
    }

    public async Task SuppressAsync(string email, SuppressionReason reason, string source, int? campaignId, CancellationToken ct)
    {
        var normalized = Normalize(email);
        await using var db = await dbFactory.CreateDbContextAsync(ct);

        var existing = await db.Suppressions.FirstOrDefaultAsync(s => s.EmailNormalized == normalized, ct);
        if (existing is null)
            db.Suppressions.Add(new SuppressionEntry { EmailNormalized = normalized, Reason = reason, Source = source });
        else if (Priority(reason) > Priority(existing.Reason))
            existing.Reason = reason;
        await db.SaveChangesAsync(ct);

        var status = reason switch
        {
            SuppressionReason.Unsubscribed => ContactStatus.Unsubscribed,
            SuppressionReason.HardBounce => ContactStatus.HardBounced,
            SuppressionReason.Complaint => ContactStatus.Complained,
            _ => ContactStatus.Suppressed
        };
        var now = DateTime.UtcNow;
        await db.Contacts.Where(c => c.EmailNormalized == normalized)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, status).SetProperty(c => c.UpdatedUtc, now), ct);

        await db.SendJobs.Where(j => j.Email == normalized && j.Status == SendJobStatus.Pending)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Status, SendJobStatus.Skipped)
                .SetProperty(j => j.LastError, Msg.T("Suppress_01"))
                .SetProperty(j => j.UpdatedUtc, now), ct);

        logger.LogInformation("Suppressed address ({Reason}) from {Source}", reason, source);
    }

    public async Task RemoveAsync(string email, CancellationToken ct)
    {
        var normalized = Normalize(email);
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Suppressions.Where(s => s.EmailNormalized == normalized).ExecuteDeleteAsync(ct);
        await db.Contacts.Where(c => c.EmailNormalized == normalized && c.Status != ContactStatus.Invalid)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, ContactStatus.Active).SetProperty(c => c.SoftBounceCount, 0), ct);
    }

    public async Task<List<SuppressionEntry>> ListAsync(string? search, int take, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Suppressions.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(search)) q = q.Where(s => s.EmailNormalized.Contains(search.ToLower()));
        return await q.OrderByDescending(s => s.CreatedUtc).Take(take).ToListAsync(ct);
    }

    public async Task<int> CountAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Suppressions.CountAsync(ct);
    }

    private static int Priority(SuppressionReason r) => r switch
    {
        SuppressionReason.Complaint => 4,
        SuppressionReason.HardBounce => 3,
        SuppressionReason.Unsubscribed => 2,
        SuppressionReason.SoftBounceLimit => 1,
        _ => 0
    };

    private static string Normalize(string email)
        => EmailAddressRules.TryNormalize(email, out var n, out _) ? n : email.Trim().ToLowerInvariant();
}
