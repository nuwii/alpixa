using MailKit;
using MailKit.Net.Imap;
using MailKit.Search;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Security;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Mail;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.Infrastructure.Bounces;

public sealed class ImapBounceProcessor(
    IDbContextFactory<AlpixaDbContext> dbFactory,
    MailAuthenticator authenticator,
    UnsubscribeSecretProvider secretProvider,
    DeliveryEventService events,
    ILogger<ImapBounceProcessor> logger) : IBounceProcessor
{
    private const int MaxMessagesPerRun = 500;

    public async Task<int> ProcessAsync(SenderProfile profile, CancellationToken ct)
    {
        if (!profile.ImapEnabled || string.IsNullOrWhiteSpace(profile.ImapHost)) return 0;

        var tokens = new UnsubscribeTokenService(await secretProvider.GetOrCreateAsync());
        using var client = TlsPolicy.Apply(new ImapClient { Timeout = 60_000 });
        await client.ConnectAsync(profile.ImapHost, profile.ImapPort, SmtpMailTransport.ToMailKit(profile.ImapSecurity), ct);
        await authenticator.AuthenticateAsync(client, profile, profile.ImapUsername ?? profile.Username, profile.ImapSecretKey, allowInteractive: false, ct);

        var inbox = client.Inbox;
        await inbox.OpenAsync(FolderAccess.ReadWrite, ct);

        SearchQuery query = profile.ImapLastUid > 0
            ? SearchQuery.Uids(new UniqueIdRange(new UniqueId(profile.ImapLastUid + 1), UniqueId.MaxValue))
            : SearchQuery.DeliveredAfter(DateTime.Now.AddDays(-14));
        var uids = (await inbox.SearchAsync(query, ct)).Where(u => u.Id > profile.ImapLastUid).OrderBy(u => u.Id).Take(MaxMessagesPerRun).ToList();

        var handled = 0;
        var lastUid = profile.ImapLastUid;
        foreach (var uid in uids)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                var message = await inbox.GetMessageAsync(uid, ct);
                var results = BounceMessageParser.Parse(message, tokens);
                foreach (var feedback in results)
                {
                    await events.RecordAsync(feedback, $"imap:{profile.Id}", ct);
                    handled++;
                }
                if (results.Count > 0)
                    await inbox.AddFlagsAsync(uid, MessageFlags.Seen, true, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "Could not process IMAP message {Uid}", uid.Id);
            }
            lastUid = Math.Max(lastUid, uid.Id);
        }

        await client.DisconnectAsync(true, ct);

        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var now = DateTime.UtcNow;
        await db.SenderProfiles.Where(p => p.Id == profile.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.ImapLastUid, lastUid).SetProperty(p => p.ImapLastCheckedUtc, now), ct);
        profile.ImapLastUid = lastUid;

        logger.LogInformation("IMAP scan for profile {ProfileId}: {Count} feedback events", profile.Id, handled);
        return handled;
    }
}
