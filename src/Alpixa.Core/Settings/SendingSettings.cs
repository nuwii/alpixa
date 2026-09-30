using Alpixa.Core.Rules;

namespace Alpixa.Core.Settings;

public sealed class SendingSettings
{
    public int SequentialDelayMinSeconds { get; set; } = 3;
    public int SequentialDelayMaxSeconds { get; set; } = 8;
    public int DefaultBulkParallelism { get; set; } = 2;
    public int MaxBulkParallelism { get; set; } = 5;
    public int ReconnectAfterMessages { get; set; } = 75;
    public int MaxRetries { get; set; } = 3;
    public double RetryBaseDelaySeconds { get; set; } = 30;
    public double ErrorRateThreshold { get; set; } = 0.05;
    public int ErrorRateMinimumSamples { get; set; } = 50;
    public double HardBounceRateLimit { get; set; } = DeliverabilityThresholds.HardBounceRateMax;
    public double ComplaintRateLimit { get; set; } = DeliverabilityThresholds.ComplaintRateMax;
    public Dictionary<string, int> DomainGroupPerMinute { get; set; } = new()
    {
        [RecipientDomainGroups.Gmail] = 20,
        [RecipientDomainGroups.Microsoft] = 20,
        [RecipientDomainGroups.Yahoo] = 15,
        [RecipientDomainGroups.Yandex] = 15,
        [RecipientDomainGroups.Other] = 30
    };
    public WarmupPlan Warmup { get; set; } = WarmupPlan.Default();
    public int MaxAttachmentMb { get; set; } = 10;
    public int MaxImageKb { get; set; } = 2048;
}

public sealed class GeneralSettings
{
    public bool SetupCompleted { get; set; }
    public bool OpenTrackingDefault { get; set; }
    public List<string> SeedAddresses { get; set; } = new();
    public string? EndpointBaseUrl { get; set; }
    public bool ShowAdvanced { get; set; }
}
