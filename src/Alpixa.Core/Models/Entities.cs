namespace Alpixa.Core.Models;

public class SenderProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public TransportKind Kind { get; set; }
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public ConnectionSecurity Security { get; set; } = ConnectionSecurity.StartTls;
    public AuthMethod Auth { get; set; } = AuthMethod.Password;
    public string Username { get; set; } = "";

    public string FromName { get; set; } = "";
    public string FromAddress { get; set; } = "";
    public string? ReplyTo { get; set; }
    public string? PostalAddress { get; set; }

    public int DailyLimit { get; set; } = 500;
    public int HourlyLimit { get; set; } = 100;
    public int PerMinuteLimit { get; set; } = 10;

    public bool DkimEnabled { get; set; }
    public string? DkimDomain { get; set; }
    public string? DkimSelector { get; set; }
    public string? DkimPrivateKeyPath { get; set; }

    public bool ImapEnabled { get; set; }
    public string? ImapHost { get; set; }
    public int ImapPort { get; set; } = 993;
    public ConnectionSecurity ImapSecurity { get; set; } = ConnectionSecurity.SslOnConnect;
    public string? ImapUsername { get; set; }
    public uint ImapLastUid { get; set; }
    public DateTime? ImapLastCheckedUtc { get; set; }

    public bool WarmupEnabled { get; set; } = true;
    public DateTime? WarmupStartedUtc { get; set; }

    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;

    public string FromDomain => FromAddress.Contains('@') ? FromAddress[(FromAddress.LastIndexOf('@') + 1)..].ToLowerInvariant() : "";
    public string SmtpSecretKey => $"profile:{Id}:smtp";
    public string ImapSecretKey => $"profile:{Id}:imap";
    public string OAuthSecretKey => $"profile:{Id}:oauth";
}

public class ContactList
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public bool ConsentConfirmed { get; set; }
    public DateTime? ConsentConfirmedUtc { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<Contact> Contacts { get; set; } = new();
}

public class Contact
{
    public long Id { get; set; }
    public int ListId { get; set; }
    public string Email { get; set; } = "";
    public string EmailNormalized { get; set; } = "";
    public string Domain { get; set; } = "";
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Company { get; set; }
    public string? CustomFieldsJson { get; set; }
    public string? ConsentSource { get; set; }
    public DateTime? ConsentDate { get; set; }
    public ContactStatus Status { get; set; } = ContactStatus.Active;
    public ContactFlags Flags { get; set; }
    public int SoftBounceCount { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class SuppressionEntry
{
    public int Id { get; set; }
    public string EmailNormalized { get; set; } = "";
    public SuppressionReason Reason { get; set; }
    public string? Source { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public class EmailTemplate
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string? TextBody { get; set; }
    public bool IsBuiltIn { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<TemplateAttachment> Attachments { get; set; } = new();
}

public class TemplateAttachment
{
    public int Id { get; set; }
    public int TemplateId { get; set; }
    public string FileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed record AttachmentRef(string FileName, string StoredFileName, string ContentType, long SizeBytes);

public class Campaign
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public int SenderProfileId { get; set; }
    public int ContactListId { get; set; }
    public int TemplateId { get; set; }
    public string Subject { get; set; } = "";
    public string HtmlBody { get; set; } = "";
    public string? TextBody { get; set; }
    public string? AttachmentsJson { get; set; }
    public SendMode Mode { get; set; } = SendMode.Sequential;
    public int Parallelism { get; set; } = 2;
    public bool DryRun { get; set; }
    public bool TrackOpens { get; set; }
    public bool HealthOverrideAccepted { get; set; }
    public DateTime? ScheduledUtc { get; set; }
    public DateTime? NextRunUtc { get; set; }
    public CampaignStatus Status { get; set; } = CampaignStatus.Draft;
    public string? PauseReason { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public DateTime? StartedUtc { get; set; }
    public DateTime? CompletedUtc { get; set; }
}

public class SendJob
{
    public long Id { get; set; }
    public int CampaignId { get; set; }
    public long ContactId { get; set; }
    public string Email { get; set; } = "";
    public string RecipientDomain { get; set; } = "";
    public int Sequence { get; set; }
    public SendJobStatus Status { get; set; } = SendJobStatus.Pending;
    public int Attempts { get; set; }
    public int? SmtpCode { get; set; }
    public string? LastError { get; set; }
    public string? MessageId { get; set; }
    public DateTime? SentUtc { get; set; }
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public class DeliveryEvent
{
    public long Id { get; set; }
    public string EmailNormalized { get; set; } = "";
    public DeliveryEventKind Kind { get; set; }
    public int? CampaignId { get; set; }
    public string? Diagnostic { get; set; }
    public string Source { get; set; } = "";
    public DateTime OccurredUtc { get; set; } = DateTime.UtcNow;
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}
