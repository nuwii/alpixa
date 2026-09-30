using Alpixa.Core.Localization;
using Alpixa.Core.Abstractions;

namespace Alpixa.Infrastructure.Consent;

public sealed class NoopConsentProvider : IConsentProvider
{
    public string Name => Msg.T("Consent_01");

    public Task<ConsentCheckResult> CheckAsync(string email, CancellationToken ct) => Task.FromResult(ConsentCheckResult.NotChecked);
}
