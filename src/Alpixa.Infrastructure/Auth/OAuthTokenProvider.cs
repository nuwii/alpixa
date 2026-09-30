using Alpixa.Core.Localization;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Options;
using Microsoft.Identity.Client;

namespace Alpixa.Infrastructure.Auth;

public sealed class OAuthNotConfiguredException(string message) : Exception(message);

public sealed class OAuthSignInRequiredException(string message) : Exception(message);

public sealed class OAuthTokenProvider(
    AlpixaOptions options,
    ISecretStore secrets,
    IPlatformService platform,
    IHttpClientFactory httpFactory) : IOAuthTokenProvider
{
    private static readonly string[] MicrosoftScopes =
        ["https://outlook.office.com/SMTP.Send", "https://outlook.office.com/IMAP.AccessAsUser.All"];

    private const string GoogleScope = "https://mail.google.com/";
    private readonly ConcurrentDictionary<int, (string Token, DateTime ExpiresUtc)> _googleCache = new();
    private readonly ConcurrentDictionary<int, IPublicClientApplication> _msalApps = new();

    public bool IsConfigured(AuthMethod method) => method switch
    {
        AuthMethod.OAuthGoogle => !string.IsNullOrWhiteSpace(options.OAuth.GoogleClientId),
        AuthMethod.OAuthMicrosoft => !string.IsNullOrWhiteSpace(options.OAuth.MicrosoftClientId),
        _ => true
    };

    public async Task<string?> SignInAsync(SenderProfile profile, CancellationToken ct)
    {
        EnsureConfigured(profile.Auth);
        return profile.Auth switch
        {
            AuthMethod.OAuthGoogle => (await GoogleInteractiveAsync(profile, ct)).Email ?? profile.Username,
            AuthMethod.OAuthMicrosoft => (await MicrosoftAcquireAsync(profile, interactive: true, ct)).Account?.Username,
            _ => null
        };
    }

    public async Task<string> GetAccessTokenAsync(SenderProfile profile, bool allowInteractive, CancellationToken ct)
    {
        EnsureConfigured(profile.Auth);
        switch (profile.Auth)
        {
            case AuthMethod.OAuthGoogle:
                if (_googleCache.TryGetValue(profile.Id, out var cached) && cached.ExpiresUtc > DateTime.UtcNow.AddMinutes(2))
                    return cached.Token;
                var refresh = await secrets.GetAsync(profile.OAuthSecretKey);
                if (!string.IsNullOrEmpty(refresh))
                    return await GoogleRefreshAsync(profile, refresh, ct);
                if (!allowInteractive) throw new OAuthSignInRequiredException("Google sign-in required.");
                return (await GoogleInteractiveAsync(profile, ct)).AccessToken;

            case AuthMethod.OAuthMicrosoft:
                return (await MicrosoftAcquireAsync(profile, allowInteractive, ct)).AccessToken;

            default:
                throw new InvalidOperationException("Profile does not use OAuth.");
        }
    }

    private void EnsureConfigured(AuthMethod method)
    {
        if (!IsConfigured(method))
            throw new OAuthNotConfiguredException($"OAuth client id missing for {method}.");
    }

    private async Task<(string AccessToken, string? Email)> GoogleInteractiveAsync(SenderProfile profile, CancellationToken ct)
    {
        var port = GetFreePort();
        var redirect = $"http://127.0.0.1:{port}/";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        listener.Start();

        var url = "https://accounts.google.com/o/oauth2/v2/auth" +
                  $"?client_id={Uri.EscapeDataString(options.OAuth.GoogleClientId)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirect)}" +
                  "&response_type=code" +
                  $"&scope={Uri.EscapeDataString(GoogleScope + " openid email")}" +
                  $"&code_challenge={challenge}&code_challenge_method=S256" +
                  "&access_type=offline&prompt=consent" +
                  $"&state={state}" +
                  (string.IsNullOrWhiteSpace(profile.Username) ? "" : $"&login_hint={Uri.EscapeDataString(profile.Username)}");
        await platform.OpenBrowserAsync(new Uri(url));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));
        var context = await listener.GetContextAsync().WaitAsync(timeout.Token);
        var query = System.Web.HttpUtility.ParseQueryString(context.Request.Url?.Query ?? "");
        await RespondAsync(context.Response, query["error"] is null);

        if (query["state"] != state || string.IsNullOrEmpty(query["code"]))
            throw new OAuthSignInRequiredException(query["error"] ?? "Google sign-in was cancelled.");

        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["code"] = query["code"]!,
            ["client_id"] = options.OAuth.GoogleClientId,
            ["client_secret"] = options.OAuth.GoogleClientSecret,
            ["redirect_uri"] = redirect,
            ["grant_type"] = "authorization_code",
            ["code_verifier"] = verifier
        }, ct);

        if (!string.IsNullOrEmpty(token.RefreshToken))
            await secrets.SetAsync(profile.OAuthSecretKey, token.RefreshToken);
        _googleCache[profile.Id] = (token.AccessToken, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
        return (token.AccessToken, ReadEmailFromIdToken(token.IdToken));
    }

    private async Task<string> GoogleRefreshAsync(SenderProfile profile, string refreshToken, CancellationToken ct)
    {
        var token = await PostTokenAsync(new Dictionary<string, string>
        {
            ["client_id"] = options.OAuth.GoogleClientId,
            ["client_secret"] = options.OAuth.GoogleClientSecret,
            ["refresh_token"] = refreshToken,
            ["grant_type"] = "refresh_token"
        }, ct);
        _googleCache[profile.Id] = (token.AccessToken, DateTime.UtcNow.AddSeconds(token.ExpiresIn));
        return token.AccessToken;
    }

    private async Task<GoogleTokenResponse> PostTokenAsync(Dictionary<string, string> form, CancellationToken ct)
    {
        using var http = httpFactory.CreateClient("oauth");
        using var response = await http.PostAsync("https://oauth2.googleapis.com/token", new FormUrlEncodedContent(form), ct);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized)
            throw new OAuthSignInRequiredException("Google rejected the token request.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(ct)
               ?? throw new OAuthSignInRequiredException("Empty token response.");
    }

    private async Task<AuthenticationResult> MicrosoftAcquireAsync(SenderProfile profile, bool interactive, CancellationToken ct)
    {
        var app = _msalApps.GetOrAdd(profile.Id, _ => CreateMsalApp(profile));
        var accounts = await app.GetAccountsAsync();
        var account = accounts.FirstOrDefault(a => string.Equals(a.Username, profile.Username, StringComparison.OrdinalIgnoreCase))
                      ?? accounts.FirstOrDefault();
        if (account is not null)
        {
            try
            {
                return await app.AcquireTokenSilent(MicrosoftScopes, account).ExecuteAsync(ct);
            }
            catch (MsalUiRequiredException) when (interactive)
            {
            }
            catch (MsalUiRequiredException)
            {
                throw new OAuthSignInRequiredException("Microsoft sign-in required.");
            }
        }
        if (!interactive) throw new OAuthSignInRequiredException("Microsoft sign-in required.");

        return await app.AcquireTokenInteractive(MicrosoftScopes)
            .WithUseEmbeddedWebView(false)
            .WithLoginHint(string.IsNullOrWhiteSpace(profile.Username) ? null : profile.Username)
            .WithSystemWebViewOptions(new SystemWebViewOptions { OpenBrowserAsync = platform.OpenBrowserAsync })
            .ExecuteAsync(ct);
    }

    private IPublicClientApplication CreateMsalApp(SenderProfile profile)
    {
        var app = PublicClientApplicationBuilder.Create(options.OAuth.MicrosoftClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, options.OAuth.MicrosoftTenant)
            .WithRedirectUri("http://localhost")
            .Build();

        var key = profile.OAuthSecretKey;
        app.UserTokenCache.SetBeforeAccessAsync(async args =>
        {
            var data = await secrets.GetAsync(key);
            if (!string.IsNullOrEmpty(data)) args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(data));
        });
        app.UserTokenCache.SetAfterAccessAsync(async args =>
        {
            if (args.HasStateChanged)
                await secrets.SetAsync(key, Convert.ToBase64String(args.TokenCache.SerializeMsalV3()));
        });
        return app;
    }

    private static async Task RespondAsync(HttpListenerResponse response, bool success)
    {
        var (title, body) = success ? (Msg.T("OAuth_DoneTitle"), Msg.T("OAuth_DoneBody")) : (Msg.T("OAuth_CancelTitle"), Msg.T("OAuth_CancelBody"));
        var html = "<html><head><meta charset='utf-8'></head><body style='font-family:sans-serif;text-align:center;padding-top:80px'>" +
                   $"<h2>{WebUtility.HtmlEncode(title)}</h2><p>{WebUtility.HtmlEncode(body)}</p></body></html>";
        var bytes = Encoding.UTF8.GetBytes(html);
        response.ContentType = "text/html; charset=utf-8";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static string? ReadEmailFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken)) return null;
        var parts = idToken.Split('.');
        if (parts.Length < 2) return null;
        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload += (payload.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
            using var doc = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(payload));
            return doc.RootElement.TryGetProperty("email", out var e) ? e.GetString() : null;
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    private static int GetFreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private sealed class GoogleTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
        [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("id_token")] public string? IdToken { get; set; }
    }
}
