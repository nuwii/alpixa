namespace Alpixa.Core.Models;

public enum TransportKind
{
    CustomSmtp = 0,
    Gmail = 1,
    Microsoft365 = 2,
    AmazonSes = 3,
    Brevo = 4,
    Mailgun = 5,
    SendGrid = 6,
    LocalTest = 7
}

public enum ConnectionSecurity
{
    StartTls = 0,
    SslOnConnect = 1,
    None = 2
}

public enum AuthMethod
{
    Password = 0,
    OAuthGoogle = 1,
    OAuthMicrosoft = 2,
    None = 3
}

public enum ContactStatus
{
    Active = 0,
    Unsubscribed = 1,
    HardBounced = 2,
    Complained = 3,
    Invalid = 4,
    Suppressed = 5
}

[Flags]
public enum ContactFlags
{
    None = 0,
    RoleAddress = 1,
    Disposable = 2,
    NoMx = 4,
    InvalidSyntax = 8
}

public enum SuppressionReason
{
    Unsubscribed = 0,
    HardBounce = 1,
    Complaint = 2,
    SoftBounceLimit = 3,
    Manual = 4
}

public enum SendMode
{
    Sequential = 0,
    Bulk = 1
}

public enum CampaignStatus
{
    Draft = 0,
    Scheduled = 1,
    Running = 2,
    Paused = 3,
    PausedBySafety = 4,
    WaitingForQuota = 5,
    Completed = 6,
    Cancelled = 7
}

public enum SendJobStatus
{
    Pending = 0,
    Sending = 1,
    Sent = 2,
    Failed = 3,
    Skipped = 4,
    Uncertain = 5
}

public enum DeliveryEventKind
{
    HardBounce = 0,
    SoftBounce = 1,
    Unsubscribe = 2,
    Complaint = 3,
    Open = 4
}

public enum CheckStatus
{
    Pass = 0,
    Info = 1,
    Warning = 2,
    Fail = 3
}

public enum LintSeverity
{
    Info = 0,
    Warning = 1,
    Error = 2
}
