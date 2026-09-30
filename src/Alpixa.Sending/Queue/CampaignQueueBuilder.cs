using Alpixa.Core.Models;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.Sending.Queue;

public sealed class CampaignQueueBuilder(IDbContextFactory<AlpixaDbContext> dbFactory, ILogger<CampaignQueueBuilder> logger)
{
    public async Task<int> BuildAsync(int campaignId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var existing = await db.SendJobs.CountAsync(j => j.CampaignId == campaignId, ct);
        if (existing > 0) return existing;

        var campaign = await db.Campaigns.AsNoTracking().FirstAsync(c => c.Id == campaignId, ct);
        var recipients = await db.Contacts.AsNoTracking()
            .Where(c => c.ListId == campaign.ContactListId && c.Status == ContactStatus.Active)
            .Where(c => !db.Suppressions.Any(s => s.EmailNormalized == c.EmailNormalized))
            .Select(c => new { c.Id, c.EmailNormalized, c.Domain })
            .ToListAsync(ct);

        var ordered = DomainInterleaver.Interleave(recipients, r => r.Domain);

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        await using var tx = await connection.BeginTransactionAsync(ct);
        await using var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO SendJobs (CampaignId, ContactId, Email, RecipientDomain, Sequence, Status, Attempts, UpdatedUtc)
            VALUES ($c, $contact, $email, $domain, $seq, 0, 0, $now)
            """;
        var pCampaign = Add(cmd, "$c");
        var pContact = Add(cmd, "$contact");
        var pEmail = Add(cmd, "$email");
        var pDomain = Add(cmd, "$domain");
        var pSeq = Add(cmd, "$seq");
        var pNow = Add(cmd, "$now");
        cmd.Prepare();

        var now = DateTime.UtcNow;
        pCampaign.Value = campaignId;
        pNow.Value = now;
        var seq = 0;
        foreach (var r in ordered)
        {
            pContact.Value = r.Id;
            pEmail.Value = r.EmailNormalized;
            pDomain.Value = r.Domain;
            pSeq.Value = seq++;
            await cmd.ExecuteNonQueryAsync(ct);
        }
        await tx.CommitAsync(ct);

        logger.LogInformation("Queued {Count} recipients for campaign {CampaignId}", ordered.Count, campaignId);
        return ordered.Count;
    }

    private static System.Data.Common.DbParameter Add(System.Data.Common.DbCommand cmd, string name)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        cmd.Parameters.Add(p);
        return p;
    }
}
