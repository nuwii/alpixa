using System.Net;
using System.Security.Cryptography;
using System.Text;
using Alpixa.Core.Security;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
var secret = builder.Configuration["Alpixa:Secret"];
var apiKey = builder.Configuration["Alpixa:ApiKey"];
var dbPath = builder.Configuration["Alpixa:DatabasePath"] ?? "unsubscribe.db";
var senderName = builder.Configuration["Alpixa:SenderName"] ?? "Gönderen";

if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(apiKey))
    throw new InvalidOperationException("Alpixa:Secret ve Alpixa:ApiKey ayarları zorunludur. Alpixa > Ayarlar > Abonelikten çıkış sunucusu ekranındaki değerleri kullanın.");

var tokens = new UnsubscribeTokenService(secret);
var store = new EventStore($"Data Source={dbPath}");
store.EnsureCreated();

var app = builder.Build();

app.MapGet("/", () => Results.Text("Alpixa unsubscribe endpoint", "text/plain"));

app.MapGet("/u/{token}", (string token) =>
{
    if (!tokens.TryRead(token, out _)) return Results.Content(Page("Geçersiz bağlantı", "Bu abonelikten çıkış bağlantısı geçersiz veya bozulmuş."), "text/html; charset=utf-8", statusCode: 400);
    var form = $"""
        <p>{WebUtility.HtmlEncode(senderName)} e-posta listesinden çıkmak istediğinizi onaylayın.</p>
        <form method="post" action="/u/{WebUtility.HtmlEncode(token)}">
          <button type="submit" style="padding:12px 20px;font-size:16px;border:0;border-radius:6px;background:#1a56db;color:#fff;cursor:pointer">Abonelikten çık</button>
        </form>
        """;
    return Results.Content(Page("Abonelikten çık", form), "text/html; charset=utf-8");
});

app.MapPost("/u/{token}", (string token) =>
{
    if (!tokens.TryRead(token, out _)) return Results.BadRequest();
    store.Add("unsubscribe", token);
    return Results.Content(Page("Abonelikten çıktınız", "Bu listeden bir daha e-posta almayacaksınız. İşlem en geç birkaç saat içinde tamamlanır."), "text/html; charset=utf-8");
}).DisableAntiforgery();

app.MapGet("/o/{file}", (string file) =>
{
    var token = file.EndsWith(".gif", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    if (tokens.TryRead(token, out _)) store.Add("open", token);
    return Results.File(TransparentGif, "image/gif");
});

app.MapGet("/api/events", (HttpRequest request, long? after) =>
{
    var provided = request.Headers["X-Api-Key"].ToString();
    if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(apiKey)))
        return Results.Unauthorized();
    return Results.Ok(store.ListAfter(after ?? 0, 1000));
});

app.Run();

static string Page(string title, string body) => $"""
    <!DOCTYPE html>
    <html lang="tr"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width, initial-scale=1"><title>{WebUtility.HtmlEncode(title)}</title></head>
    <body style="font-family:Arial,Helvetica,sans-serif;max-width:520px;margin:80px auto;padding:0 16px;color:#1f2933;line-height:1.6">
    <h1 style="font-size:22px">{WebUtility.HtmlEncode(title)}</h1>
    {body}
    </body></html>
    """;

internal partial class Program
{
    private static readonly byte[] TransparentGif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");
}

internal sealed record EndpointEvent(long Id, string Kind, string Token, DateTime OccurredUtc);

internal sealed class EventStore(string connectionString)
{
    public void EnsureCreated()
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Events (Id INTEGER PRIMARY KEY AUTOINCREMENT, Kind TEXT NOT NULL, Token TEXT NOT NULL, OccurredUtc TEXT NOT NULL);
            CREATE UNIQUE INDEX IF NOT EXISTS IX_Events_Kind_Token ON Events (Kind, Token);
            """;
        cmd.ExecuteNonQuery();
    }

    public void Add(string kind, string token)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Events (Kind, Token, OccurredUtc) VALUES ($k, $t, $d)";
        cmd.Parameters.AddWithValue("$k", kind);
        cmd.Parameters.AddWithValue("$t", token);
        cmd.Parameters.AddWithValue("$d", DateTime.UtcNow.ToString("O"));
        cmd.ExecuteNonQuery();
    }

    public List<EndpointEvent> ListAfter(long after, int take)
    {
        using var c = Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT Id, Kind, Token, OccurredUtc FROM Events WHERE Id > $a ORDER BY Id LIMIT $n";
        cmd.Parameters.AddWithValue("$a", after);
        cmd.Parameters.AddWithValue("$n", take);
        using var r = cmd.ExecuteReader();
        var list = new List<EndpointEvent>();
        while (r.Read())
            list.Add(new EndpointEvent(r.GetInt64(0), r.GetString(1), r.GetString(2), DateTime.Parse(r.GetString(3), null, System.Globalization.DateTimeStyles.RoundtripKind)));
        return list;
    }

    private SqliteConnection Open()
    {
        var c = new SqliteConnection(connectionString);
        c.Open();
        return c;
    }
}
