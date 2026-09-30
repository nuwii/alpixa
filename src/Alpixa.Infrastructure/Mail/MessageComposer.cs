using Alpixa.Core.Localization;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Content;
using Alpixa.Core.Models;
using Alpixa.Core.Security;
using Alpixa.Infrastructure.Settings;
using Alpixa.Infrastructure.Templates;
using MimeKit;

namespace Alpixa.Infrastructure.Mail;

public sealed partial class MessageComposer(
    ITemplateRenderer renderer,
    UnsubscribeSecretProvider secretProvider,
    SettingsStore settings,
    AssetStore assets,
    IClock clock) : IMessageComposer
{
    private static readonly CultureInfo Turkish = new("tr-TR");

    [GeneratedRegex(@"\{\{\s*(unsubscribe_url|abonelik_iptal_url)", RegexOptions.IgnoreCase)]
    private static partial Regex UnsubscribePlaceholder();

    [GeneratedRegex(@"</body>", RegexOptions.IgnoreCase)]
    private static partial Regex BodyClose();

    public async Task<ComposeContext> CreateContextAsync(CancellationToken ct)
    {
        var secret = await secretProvider.GetOrCreateAsync();
        var general = await settings.GetGeneralAsync(ct);
        var endpoint = string.IsNullOrWhiteSpace(general.EndpointBaseUrl) ? null : general.EndpointBaseUrl.TrimEnd('/');
        return new ComposeContext(new UnsubscribeTokenService(secret), endpoint);
    }

    public MimeMessage Compose(Campaign campaign, SenderProfile profile, Contact contact, ComposeContext context)
    {
        var token = context.Tokens.Create(contact.EmailNormalized, campaign.Id);
        var mailboxForUnsubscribe = string.IsNullOrWhiteSpace(profile.ReplyTo) ? profile.FromAddress : profile.ReplyTo!;
        var mailtoUri = $"mailto:{mailboxForUnsubscribe}?subject=unsubscribe-{token}";
        var httpsUri = context.EndpointBaseUrl is null ? null : $"{context.EndpointBaseUrl}/u/{token}";
        var visibleUnsubscribe = httpsUri ?? mailtoUri;

        var model = BuildModel(campaign, profile, contact, visibleUnsubscribe);
        var subject = renderer.Render(campaign.Subject, model).Trim();
        model["konu"] = subject;

        var html = renderer.Render(campaign.HtmlBody, model, htmlEncodeValues: true);
        var templateHasUnsubscribe = UnsubscribePlaceholder().IsMatch(campaign.HtmlBody);
        if (!templateHasUnsubscribe)
            html = InsertBeforeBodyEnd(html, BuildHtmlFooter(profile, contact, visibleUnsubscribe));

        if (campaign.TrackOpens && context.EndpointBaseUrl is not null)
            html = InsertBeforeBodyEnd(html, $"<img src=\"{context.EndpointBaseUrl}/o/{token}.gif\" width=\"1\" height=\"1\" alt=\"\" style=\"display:block;border:0;width:1px;height:1px;\">");

        string text;
        if (!string.IsNullOrWhiteSpace(campaign.TextBody))
        {
            text = renderer.Render(campaign.TextBody!, model);
            if (!templateHasUnsubscribe) text += BuildTextFooter(profile, contact, visibleUnsubscribe);
        }
        else
        {
            text = HtmlToText.Convert(html);
        }

        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(profile.FromName, profile.FromAddress));
        message.To.Add(new MailboxAddress(DisplayName(contact), contact.Email));
        if (!string.IsNullOrWhiteSpace(profile.ReplyTo))
            message.ReplyTo.Add(MailboxAddress.Parse(profile.ReplyTo));
        message.Subject = subject;
        message.Date = new DateTimeOffset(clock.UtcNow);
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId(profile.FromDomain);

        message.Headers.Add("List-Unsubscribe", httpsUri is null ? $"<{mailtoUri}>" : $"<{httpsUri}>, <{mailtoUri}>");
        if (httpsUri is not null)
            message.Headers.Add("List-Unsubscribe-Post", "List-Unsubscribe=One-Click");
        message.Headers.Add("Feedback-ID", $"c{campaign.Id}:p{profile.Id}:alpixa:{profile.FromDomain}");

        var builder = new BodyBuilder { TextBody = text };
        builder.HtmlBody = assets.EmbedImages(html, builder);
        foreach (var attachment in AssetStore.ParseSnapshot(campaign.AttachmentsJson))
        {
            if (!assets.Exists(attachment.StoredFileName)) continue;
            builder.Attachments.Add(attachment.FileName, assets.Read(attachment.StoredFileName), ContentType.Parse(attachment.ContentType));
        }
        message.Body = builder.ToMessageBody();
        return message;
    }

    public static Dictionary<string, object?> BuildModel(Campaign campaign, SenderProfile profile, Contact contact, string unsubscribeUrl)
    {
        var model = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["email"] = contact.Email,
            ["ad"] = contact.FirstName,
            ["soyad"] = contact.LastName,
            ["firma"] = contact.Company,
            ["first_name"] = contact.FirstName,
            ["last_name"] = contact.LastName,
            ["company"] = contact.Company,
            ["gonderen_adi"] = profile.FromName,
            ["gonderen_adres"] = profile.PostalAddress,
            ["kampanya"] = campaign.Name,
            ["tarih"] = DateTime.Now.ToString("d MMMM yyyy", Turkish),
            ["ay"] = Turkish.TextInfo.ToTitleCase(DateTime.Now.ToString("MMMM", Turkish)),
            ["unsubscribe_url"] = unsubscribeUrl,
            ["abonelik_iptal_url"] = unsubscribeUrl
        };

        if (!string.IsNullOrWhiteSpace(contact.CustomFieldsJson))
        {
            try
            {
                var custom = JsonSerializer.Deserialize<Dictionary<string, string?>>(contact.CustomFieldsJson!);
                if (custom is not null)
                    foreach (var (k, v) in custom)
                        model.TryAdd(k, v);
            }
            catch (JsonException)
            {
            }
        }
        return model;
    }

    private static string DisplayName(Contact c)
        => string.Join(' ', new[] { c.FirstName, c.LastName }.Where(s => !string.IsNullOrWhiteSpace(s))).Trim();

    private static string InsertBeforeBodyEnd(string html, string fragment)
    {
        var match = BodyClose().Match(html);
        return match.Success ? html.Insert(match.Index, fragment) : html + fragment;
    }

    private static string BuildHtmlFooter(SenderProfile profile, Contact contact, string unsubscribeUrl)
    {
        var sender = WebUtility.HtmlEncode(profile.FromName);
        var address = string.IsNullOrWhiteSpace(profile.PostalAddress) ? "" : " &middot; " + WebUtility.HtmlEncode(profile.PostalAddress);
        return $"""
            <div style="max-width:600px;margin:24px auto 0;padding:16px 12px;border-top:1px solid #e5e7eb;font-family:Arial,Helvetica,sans-serif;font-size:12px;line-height:1.5;color:#6b7280;text-align:center;">
            {sender}{address}<br>
            {Html(Msg.T("Footer_SentTo", contact.Email))}<br>
            <a href="{WebUtility.HtmlEncode(unsubscribeUrl)}" style="color:#6b7280;text-decoration:underline;">{Html(Msg.T("Footer_Unsubscribe"))}</a>
            </div>
            """;
    }

    private static string BuildTextFooter(SenderProfile profile, Contact contact, string unsubscribeUrl)
    {
        var address = string.IsNullOrWhiteSpace(profile.PostalAddress) ? "" : " - " + profile.PostalAddress;
        return $"\n\n--\n{profile.FromName}{address}\n{Msg.T("Footer_TextSentTo", contact.Email)}\n{Msg.T("Footer_TextUnsubscribe", unsubscribeUrl)}\n";
    }

    // Escapes only markup characters so localized text keeps its letters readable in the HTML source.
    private static string Html(string text) => text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
