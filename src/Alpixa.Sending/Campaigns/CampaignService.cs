using Alpixa.Core.Localization;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Content;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Settings;
using Alpixa.Infrastructure.Templates;
using Alpixa.Sending.Engine;
using Alpixa.Sending.Queue;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Sending.Campaigns;

public sealed record PreflightResult(
    DomainHealthReport Health,
    IReadOnlyList<LintIssue> Lint,
    IReadOnlyList<string> TemplateErrors,
    int Recipients,
    QuotaStatus Quota,
    bool HealthBlocks,
    bool LintBlocks)
{
    public bool CanStart(bool healthOverrideAccepted) =>
        TemplateErrors.Count == 0 && !LintBlocks && Recipients > 0 && (!HealthBlocks || healthOverrideAccepted);
}

public sealed record TestSendResult(string Email, bool Success, string? Error);

public sealed class CampaignService(
    IDbContextFactory<AlpixaDbContext> dbFactory,
    IDomainHealthService health,
    ITemplateRenderer renderer,
    IMessageComposer composer,
    IMailTransportFactory transports,
    QuotaService quota,
    SettingsStore settings,
    CampaignRunner runner,
    AssetStore assets,
    IClock clock)
{
    public async Task<PreflightResult> PreflightAsync(Campaign draft, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profile = await db.SenderProfiles.AsNoTracking().FirstAsync(p => p.Id == draft.SenderProfileId, ct);
        var recipients = await db.Contacts.AsNoTracking()
            .Where(c => c.ListId == draft.ContactListId && c.Status == ContactStatus.Active)
            .Where(c => !db.Suppressions.Any(s => s.EmailNormalized == c.EmailNormalized))
            .CountAsync(ct);

        var report = await health.CheckAsync(profile, ct);
        draft.AttachmentsJson ??= await assets.SnapshotAsync(draft.TemplateId, ct);
        var lint = ContentLinter.Lint(draft.Subject, draft.HtmlBody, AssetStore.ParseSnapshot(draft.AttachmentsJson).Count > 0);
        var templateErrors = renderer.Validate(draft.Subject).Concat(renderer.Validate(draft.HtmlBody))
            .Concat(string.IsNullOrWhiteSpace(draft.TextBody) ? [] : renderer.Validate(draft.TextBody!)).ToList();
        var sending = await settings.GetSendingAsync(ct);
        var quotaStatus = await quota.GetStatusAsync(profile, sending.Warmup, ct);

        return new PreflightResult(report, lint, templateErrors, recipients, quotaStatus,
            HealthBlocks: !draft.DryRun && report.HasBlockingIssues,
            LintBlocks: lint.Any(i => i.Severity == LintSeverity.Error));
    }

    public async Task<Campaign> SaveDraftAsync(Campaign draft, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        if (string.IsNullOrWhiteSpace(draft.HtmlBody))
        {
            var template = await db.Templates.AsNoTracking().FirstAsync(t => t.Id == draft.TemplateId, ct);
            draft.HtmlBody = template.HtmlBody;
            draft.TextBody = template.TextBody;
            if (string.IsNullOrWhiteSpace(draft.Subject)) draft.Subject = template.Subject;
        }
        draft.AttachmentsJson ??= await assets.SnapshotAsync(draft.TemplateId, ct);
        if (draft.Id == 0) db.Campaigns.Add(draft);
        else db.Campaigns.Update(draft);
        await db.SaveChangesAsync(ct);
        return draft;
    }

    public async Task LaunchAsync(int campaignId, bool healthOverrideAccepted, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var campaign = await db.Campaigns.FirstAsync(c => c.Id == campaignId, ct);
        campaign.HealthOverrideAccepted = healthOverrideAccepted;
        var scheduledInFuture = campaign.ScheduledUtc is { } s && s > clock.UtcNow.AddSeconds(30);
        campaign.Status = scheduledInFuture ? CampaignStatus.Scheduled : CampaignStatus.Running;
        campaign.PauseReason = null;
        await db.SaveChangesAsync(ct);
        if (!scheduledInFuture) await runner.StartAsync(campaignId);
    }

    public async Task ResumeAsync(int campaignId, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Campaigns.Where(c => c.Id == campaignId)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, CampaignStatus.Running).SetProperty(c => c.PauseReason, (string?)null), ct);
        await runner.StartAsync(campaignId);
    }

    public async Task<List<Campaign>> ListAsync(CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.Campaigns.AsNoTracking().OrderByDescending(c => c.CreatedUtc).ToListAsync(ct);
    }

    public async Task DeleteAsync(int campaignId, CancellationToken ct)
    {
        if (runner.IsRunning(campaignId)) throw new InvalidOperationException(Msg.T("Campaign_01"));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.SendJobs.Where(j => j.CampaignId == campaignId).ExecuteDeleteAsync(ct);
        await db.Campaigns.Where(c => c.Id == campaignId).ExecuteDeleteAsync(ct);
    }

    public async Task<IReadOnlyList<TestSendResult>> SendTestAsync(Campaign draft, IEnumerable<string> seedAddresses, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var profile = await db.SenderProfiles.AsNoTracking().FirstAsync(p => p.Id == draft.SenderProfileId, ct);
        var sample = await db.Contacts.AsNoTracking()
            .Where(c => c.ListId == draft.ContactListId && c.Status == ContactStatus.Active)
            .OrderBy(c => c.Id).FirstOrDefaultAsync(ct);

        var context = await composer.CreateContextAsync(ct);
        var results = new List<TestSendResult>();
        await using var transport = await transports.CreateAsync(profile, dryRun: false, ct);
        var testCampaign = new Campaign
        {
            Id = draft.Id,
            Name = draft.Name,
            Subject = "[TEST] " + draft.Subject,
            HtmlBody = draft.HtmlBody,
            TextBody = draft.TextBody,
            AttachmentsJson = draft.AttachmentsJson ?? await assets.SnapshotAsync(draft.TemplateId, ct),
            TrackOpens = false
        };

        foreach (var seed in seedAddresses.Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => s.Trim()).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var contact = new Contact
            {
                Email = seed,
                EmailNormalized = seed.ToLowerInvariant(),
                FirstName = sample?.FirstName ?? "Test",
                LastName = sample?.LastName,
                Company = sample?.Company,
                CustomFieldsJson = sample?.CustomFieldsJson
            };
            try
            {
                var message = composer.Compose(testCampaign, profile, contact, context);
                var result = await transport.SendAsync(message, ct);
                results.Add(new TestSendResult(seed, result.Success, result.Success ? null : ErrorTranslator.DescribeSendFailure(result) + " " + result.Response));
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                var error = ErrorTranslator.Translate(ex, profile);
                results.Add(new TestSendResult(seed, false, $"{error.Message} {error.WhatToDo}"));
            }
        }
        return results;
    }
}
