namespace Alpixa.Core.Options;

public sealed class AlpixaOptions
{
    public OAuthOptions OAuth { get; set; } = new();
    public string HelpBaseUrl { get; set; } = "https://github.com/nuwii/alpixa#";
    public int BackgroundCheckMinutes { get; set; } = 15;
    public string[] DnsblIpZones { get; set; } = ["zen.spamhaus.org", "b.barracudacentral.org", "bl.spamcop.net"];
    public string[] DnsblDomainZones { get; set; } = ["dbl.spamhaus.org"];
}

public sealed class OAuthOptions
{
    public string GoogleClientId { get; set; } = "";
    public string GoogleClientSecret { get; set; } = "";
    public string MicrosoftClientId { get; set; } = "";
    public string MicrosoftTenant { get; set; } = "common";
}
