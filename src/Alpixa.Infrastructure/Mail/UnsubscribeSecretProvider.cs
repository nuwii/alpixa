using Alpixa.Core.Abstractions;
using Alpixa.Core.Security;

namespace Alpixa.Infrastructure.Mail;

public sealed class UnsubscribeSecretProvider(ISecretStore secrets)
{
    public const string SecretKey = "unsubscribe:secret";
    public const string EndpointApiKey = "endpoint:apikey";

    public async Task<string> GetOrCreateAsync()
    {
        var existing = await secrets.GetAsync(SecretKey);
        if (!string.IsNullOrEmpty(existing)) return existing;
        var created = UnsubscribeTokenService.GenerateSecret();
        await secrets.SetAsync(SecretKey, created);
        return created;
    }
}
