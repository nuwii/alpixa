namespace Alpixa.Core.Models;

public enum SendOutcome
{
    Sent = 0,
    TransientFailure = 1,
    RateLimited = 2,
    PermanentFailure = 3,
    ConnectionFailure = 4,
    AuthenticationFailure = 5
}

public sealed record SendResult(SendOutcome Outcome, int? SmtpCode = null, string? EnhancedStatus = null, string? Response = null)
{
    public bool Success => Outcome == SendOutcome.Sent;
    public bool IsTransient => Outcome is SendOutcome.TransientFailure or SendOutcome.RateLimited or SendOutcome.ConnectionFailure;
    public static SendResult Ok() => new(SendOutcome.Sent, 250);
}

public sealed record DomainCheck(string Code, string Title, CheckStatus Status, string Message, string? FixHint = null, string? SuggestedRecord = null);

public sealed class DomainHealthReport
{
    public string Domain { get; init; } = "";
    public DateTime CheckedUtc { get; init; } = DateTime.UtcNow;
    public List<DomainCheck> Checks { get; } = new();

    public CheckStatus Overall => Checks.Count == 0 ? CheckStatus.Info : Checks.Max(c => c.Status);
    public bool HasBlockingIssues => Checks.Any(c => c.Status == CheckStatus.Fail);
}

public sealed record LintIssue(string Code, LintSeverity Severity, string Message);

public sealed class ImportSummary
{
    public int TotalRows { get; set; }
    public int Imported { get; set; }
    public int Duplicates { get; set; }
    public int InvalidSyntax { get; set; }
    public int Disposable { get; set; }
    public int RoleAddresses { get; set; }
    public int NoMx { get; set; }
    public int Suppressed { get; set; }
    public int MissingEmail { get; set; }
}

public sealed class ImportPreview
{
    public IReadOnlyList<string> Headers { get; init; } = Array.Empty<string>();
    public IReadOnlyList<IReadOnlyList<string>> SampleRows { get; init; } = Array.Empty<IReadOnlyList<string>>();
    public ColumnMapping Mapping { get; init; } = new();
}

public sealed class ColumnMapping
{
    public int EmailColumn { get; set; } = -1;
    public int FirstNameColumn { get; set; } = -1;
    public int LastNameColumn { get; set; } = -1;
    public int CompanyColumn { get; set; } = -1;
    public int ConsentSourceColumn { get; set; } = -1;
    public int ConsentDateColumn { get; set; } = -1;
    public Dictionary<int, string> CustomColumns { get; set; } = new();
}

public sealed record CampaignProgress(
    int CampaignId,
    CampaignStatus Status,
    int Total,
    int Sent,
    int Failed,
    int Pending,
    int Skipped,
    double MessagesPerMinute,
    TimeSpan? EstimatedRemaining,
    string? Message)
{
    public double Fraction => Total == 0 ? 0 : (double)(Sent + Failed + Skipped) / Total;
}

public sealed class CampaignReport
{
    public int CampaignId { get; init; }
    public string Name { get; init; } = "";
    public CampaignStatus Status { get; init; }
    public int Total { get; init; }
    public int Sent { get; init; }
    public int Failed { get; init; }
    public int Pending { get; init; }
    public int Skipped { get; init; }
    public int Uncertain { get; init; }
    public int HardBounces { get; init; }
    public int SoftBounces { get; init; }
    public int Unsubscribes { get; init; }
    public int Complaints { get; init; }
    public int Opens { get; init; }
    public double BounceRate => Sent == 0 ? 0 : (double)HardBounces / Sent;
    public double ComplaintRate => Sent == 0 ? 0 : (double)Complaints / Sent;
}

public sealed record UserFacingError(string Message, string WhatToDo, string? HelpUrl = null);
