namespace Alpixa.Core.Rules;

public static class DeliverabilityThresholds
{
    public const double ComplaintRateTarget = 0.001;
    public const double ComplaintRateMax = 0.003;
    public const double HardBounceRateTarget = 0.02;
    public const double HardBounceRateMax = 0.05;
    public const int SoftBounceSuppressAfter = 3;
    public const int HtmlSizeWarningBytes = 100 * 1024;
    public const int RateCheckMinimumSent = 50;
}
