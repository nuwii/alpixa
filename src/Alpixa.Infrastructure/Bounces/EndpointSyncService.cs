using System.Net.Http.Json;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Security;
using Alpixa.Infrastructure.Mail;
using Alpixa.Infrastructure.Settings;
using Microsoft.Extensions.Logging;

namespace Alpixa.Infrastructure.Bounces;

public sealed record EndpointEvent(long Id, string Kind, string Token, DateTime OccurredUtc);

public sealed class EndpointSyncService(
    SettingsStore settings,
    ISecretStore secrets,
    UnsubscribeSecretProvider secretProvider,
    DeliveryEventService events,
    IHttpClientFactory httpFactory,
    ILogger<EndpointSyncService> logger) : IUnsubscribeSync
{
    private const string CursorKey = "endpoint:cursor";

    public async Task<int> SyncAsync(CancellationToken ct)
    {
        var general = await settings.GetGeneralAsync(ct);
        if (string.IsNullOrWhiteSpace(general.EndpointBaseUrl)) return 0;
        var apiKey = await secrets.GetAsync(UnsubscribeSecretProvider.EndpointApiKey);
        if (string.IsNullOrWhiteSpace(apiKey)) return 0;

        var tokens = new UnsubscribeTokenService(await secretProvider.GetOrCreateAsync());
        var cursor = await settings.GetAsync<Cursor>(CursorKey, ct);
        using var http = httpFactory.CreateClient("endpoint");
        using var request = new HttpRequestMessage(HttpMethod.Get, $"{general.EndpointBaseUrl.TrimEnd('/')}/api/events?after={cursor.LastId}");
        request.Headers.Add("X-Api-Key", apiKey);
        using var response = await http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        var items = await response.Content.ReadFromJsonAsync<List<EndpointEvent>>(ct) ?? [];

        var handled = 0;
        foreach (var item in items.OrderBy(i => i.Id))
        {
            if (tokens.TryRead(item.Token, out var payload) && payload is not null)
            {
                var kind = item.Kind == "open" ? DeliveryEventKind.Open : DeliveryEventKind.Unsubscribe;
                await events.RecordAsync(new ParsedFeedback(payload.Email, kind, null, "https", null, payload.CampaignId), "endpoint", ct);
                handled++;
            }
            cursor.LastId = Math.Max(cursor.LastId, item.Id);
        }
        await settings.SetAsync(CursorKey, cursor, ct);
        if (handled > 0) logger.LogInformation("Endpoint sync applied {Count} events", handled);
        return handled;
    }

    private sealed class Cursor
    {
        public long LastId { get; set; }
    }
}
