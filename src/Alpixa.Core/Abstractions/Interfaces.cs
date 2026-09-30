using Alpixa.Core.Models;
using Alpixa.Core.Security;
using MimeKit;

namespace Alpixa.Core.Abstractions;

public interface IClock
{
    DateTime UtcNow { get; }
    DateTime LocalNow { get; }
}

public sealed class SystemClock : IClock
{
    public DateTime UtcNow => DateTime.UtcNow;
    public DateTime LocalNow => DateTime.Now;
}

public interface ISecretStore
{
    Task<string?> GetAsync(string key);
    Task SetAsync(string key, string value);
    Task RemoveAsync(string key);
}

public interface IMailTransport : IAsyncDisposable
{
    Task ConnectAsync(CancellationToken ct);
    Task<SendResult> SendAsync(MimeMessage message, CancellationToken ct);
    int MessagesOnConnection { get; }
}

public interface IMailTransportFactory
{
    Task<IMailTransport> CreateAsync(SenderProfile profile, bool dryRun, CancellationToken ct);
}

public interface IConnectionTester
{
    Task<(bool Ok, UserFacingError? Error)> TestSmtpAsync(SenderProfile profile, CancellationToken ct);
    Task<(bool Ok, UserFacingError? Error)> TestImapAsync(SenderProfile profile, CancellationToken ct);
}

public interface IOAuthTokenProvider
{
    bool IsConfigured(AuthMethod method);
    Task<string> GetAccessTokenAsync(SenderProfile profile, bool allowInteractive, CancellationToken ct);
    Task<string?> SignInAsync(SenderProfile profile, CancellationToken ct);
}

public interface IDomainHealthService
{
    Task<DomainHealthReport> CheckAsync(SenderProfile profile, CancellationToken ct);
}

public interface IMxChecker
{
    Task<bool> HasMailServerAsync(string domain, CancellationToken ct);
}

public interface ITemplateRenderer
{
    string Render(string template, IReadOnlyDictionary<string, object?> model, bool htmlEncodeValues = false);
    IReadOnlyList<string> Validate(string template);
}

public sealed record ComposeContext(UnsubscribeTokenService Tokens, string? EndpointBaseUrl);

public interface IMessageComposer
{
    MimeMessage Compose(Campaign campaign, SenderProfile profile, Contact contact, ComposeContext context);
    Task<ComposeContext> CreateContextAsync(CancellationToken ct);
}

public interface IConsentProvider
{
    string Name { get; }
    Task<ConsentCheckResult> CheckAsync(string email, CancellationToken ct);
}

public enum ConsentCheckResult
{
    NotChecked = 0,
    Allowed = 1,
    Denied = 2
}

public interface ISuppressionService
{
    Task<bool> IsSuppressedAsync(string email, CancellationToken ct);
    Task SuppressAsync(string email, SuppressionReason reason, string source, int? campaignId, CancellationToken ct);
    Task RemoveAsync(string email, CancellationToken ct);
}

public interface IBounceProcessor
{
    Task<int> ProcessAsync(SenderProfile profile, CancellationToken ct);
}

public interface IUnsubscribeSync
{
    Task<int> SyncAsync(CancellationToken ct);
}

public interface IPlatformService
{
    void SetKeepAwake(bool keepAwake);
    void ShowNotification(string title, string message);
    void SetProgress(double? fraction);
    void OpenFolder(string path);
    Task OpenBrowserAsync(Uri uri);
    void AcceptDragOver(object platformDragOverArgs);
    Task<IReadOnlyList<string>> GetDroppedFilePathsAsync(object platformDropArgs);
}

public interface IAppPaths
{
    string DataDirectory { get; }
    string DatabasePath { get; }
    string LogDirectory { get; }
}
