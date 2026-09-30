using Alpixa.Core.Localization;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Infrastructure.Data;
using Alpixa.Infrastructure.Settings;
using Microsoft.EntityFrameworkCore;
using MimeKit;

namespace Alpixa.Infrastructure.Templates;

public sealed class AssetTooLargeException(string message) : InvalidOperationException(message);

/// <summary>
/// Stores images and attachments used by templates in the data folder. Images are referenced from
/// template HTML as <c>src="asset:NAME"</c> and become inline (cid:) parts when the email is built.
/// </summary>
public sealed partial class AssetStore(IAppPaths paths, SettingsStore settings, IDbContextFactory<AlpixaDbContext> dbFactory)
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) { ".png", ".jpg", ".jpeg", ".gif", ".webp" };
    private readonly ConcurrentDictionary<string, byte[]> _cache = new();

    [GeneratedRegex(@"src\s*=\s*[""']asset:([A-Za-z0-9._-]+)[""']", RegexOptions.IgnoreCase)]
    private static partial Regex AssetSrc();

    public string FolderPath => Path.Combine(paths.DataDirectory, "files");

    public static bool IsImage(string path) => ImageExtensions.Contains(Path.GetExtension(path));

    public static string FormatSize(long bytes) => bytes >= 1024 * 1024
        ? (bytes / 1024d / 1024d).ToString("0.0", CultureInfo.CurrentCulture) + " MB"
        : (bytes == 0 ? 0 : Math.Max(1, bytes / 1024)).ToString(CultureInfo.CurrentCulture) + " KB";

    public async Task<string> ImportImageAsync(string sourcePath, CancellationToken ct = default)
    {
        if (!IsImage(sourcePath))
            throw new InvalidOperationException(Msg.T("Asset_01"));
        var limitKb = (await settings.GetSendingAsync(ct)).MaxImageKb;
        var size = new FileInfo(sourcePath).Length;
        if (size > limitKb * 1024L)
            throw new AssetTooLargeException(Msg.T("Asset_02", FormatSize(size), FormatSize(limitKb * 1024L)));
        return await CopyInAsync(sourcePath, ct);
    }

    public async Task<TemplateAttachment> AddAttachmentAsync(int templateId, string sourcePath, CancellationToken ct = default)
    {
        var size = new FileInfo(sourcePath).Length;
        var limitMb = (await settings.GetSendingAsync(ct)).MaxAttachmentMb;
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var current = await db.TemplateAttachments.Where(a => a.TemplateId == templateId).SumAsync(a => (long?)a.SizeBytes, ct) ?? 0;
        if (current + size > limitMb * 1024L * 1024L)
            throw new AssetTooLargeException(
                Msg.T("Asset_03", limitMb, FormatSize(current), FormatSize(size)));

        var attachment = new TemplateAttachment
        {
            TemplateId = templateId,
            FileName = Path.GetFileName(sourcePath),
            StoredFileName = await CopyInAsync(sourcePath, ct),
            ContentType = MimeTypes.GetMimeType(sourcePath),
            SizeBytes = size
        };
        db.TemplateAttachments.Add(attachment);
        await db.SaveChangesAsync(ct);
        return attachment;
    }

    public async Task<List<TemplateAttachment>> ListAsync(int templateId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        return await db.TemplateAttachments.AsNoTracking().Where(a => a.TemplateId == templateId).OrderBy(a => a.Id).ToListAsync(ct);
    }

    public async Task RemoveAttachmentAsync(int attachmentId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.TemplateAttachments.Where(a => a.Id == attachmentId).ExecuteDeleteAsync(ct);
    }

    public async Task CopyAttachmentsAsync(int fromTemplateId, int toTemplateId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var items = await db.TemplateAttachments.AsNoTracking().Where(a => a.TemplateId == fromTemplateId).ToListAsync(ct);
        foreach (var a in items)
            db.TemplateAttachments.Add(new TemplateAttachment
            {
                TemplateId = toTemplateId, FileName = a.FileName, StoredFileName = a.StoredFileName,
                ContentType = a.ContentType, SizeBytes = a.SizeBytes
            });
        await db.SaveChangesAsync(ct);
    }

    public async Task<string?> SnapshotAsync(int templateId, CancellationToken ct = default)
    {
        var items = await ListAsync(templateId, ct);
        return items.Count == 0
            ? null
            : JsonSerializer.Serialize(items.Select(a => new AttachmentRef(a.FileName, a.StoredFileName, a.ContentType, a.SizeBytes)).ToList());
    }

    public static IReadOnlyList<AttachmentRef> ParseSnapshot(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<List<AttachmentRef>>(json) ?? []; }
        catch (JsonException) { return []; }
    }

    public bool Exists(string storedName) => IsSafeName(storedName) && File.Exists(Path.Combine(FolderPath, storedName));

    public byte[] Read(string storedName)
    {
        if (!IsSafeName(storedName)) throw new InvalidOperationException(Msg.T("Asset_04"));
        return _cache.GetOrAdd(storedName, n => File.ReadAllBytes(Path.Combine(FolderPath, n)));
    }

    public static IEnumerable<string> ReferencedImages(string html)
        => AssetSrc().Matches(html ?? "").Select(m => m.Groups[1].Value).Distinct();

    /// <summary>Replaces asset references with data URIs so a WebView can show them.</summary>
    public string ToPreviewHtml(string html) => AssetSrc().Replace(html ?? "", m =>
    {
        var name = m.Groups[1].Value;
        if (!Exists(name)) return m.Value;
        return $"src=\"data:{MimeTypes.GetMimeType(name)};base64,{Convert.ToBase64String(Read(name))}\"";
    });

    /// <summary>Replaces asset references with cid: links and adds the images as inline parts.</summary>
    public string EmbedImages(string html, BodyBuilder builder) => AssetSrc().Replace(html ?? "", m =>
    {
        var name = m.Groups[1].Value;
        if (!Exists(name)) return m.Value;
        var part = builder.LinkedResources.Add(name, Read(name), ContentType.Parse(MimeTypes.GetMimeType(name)));
        part.ContentId = MimeKit.Utils.MimeUtils.GenerateMessageId();
        return $"src=\"cid:{part.ContentId}\"";
    });

    /// <summary>Builds the HTML for an image stored with <see cref="ImportImageAsync"/>, sized for a 600px email.</summary>
    public string ImageTag(string storedName, string? alt = null, int maxWidth = 536)
    {
        var size = Exists(storedName) ? ReadImageSize(Read(storedName)) : null;
        var width = size is { } s ? Math.Min(s.Width, maxWidth) : maxWidth;
        var altText = System.Net.WebUtility.HtmlEncode((alt ?? "").Replace('-', ' ').Replace('_', ' ').Trim());
        return $"<img src=\"asset:{storedName}\" width=\"{width}\" alt=\"{altText}\" style=\"display:block;width:100%;max-width:{width}px;height:auto;border:0;margin:16px 0;border-radius:6px;\">";
    }

    /// <summary>Inserts <paramref name="snippet"/> at the cursor, or after the first heading/paragraph when there is no cursor.</summary>
    public static (string Html, int Cursor) InsertAt(string html, string snippet, int cursor)
    {
        html ??= "";
        if (cursor <= 0 || cursor > html.Length)
        {
            var anchor = html.IndexOf("</h1>", StringComparison.OrdinalIgnoreCase) is var h and >= 0 ? h + 5
                : html.IndexOf("</p>", StringComparison.OrdinalIgnoreCase) is var p and >= 0 ? p + 4
                : html.Length;
            cursor = anchor;
        }
        var text = "\n" + snippet + "\n";
        return (html.Insert(cursor, text), cursor + text.Length);
    }

    /// <summary>Reads pixel dimensions from PNG, GIF or JPEG headers without decoding the image.</summary>
    public static (int Width, int Height)? ReadImageSize(byte[] data)
    {
        if (data.Length > 24 && data[0] == 0x89 && data[1] == 0x50)
            return (BigEndian(data, 16), BigEndian(data, 20));
        if (data.Length > 10 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
            return (data[6] | data[7] << 8, data[8] | data[9] << 8);
        if (data.Length > 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var i = 2;
            while (i + 9 < data.Length)
            {
                if (data[i] != 0xFF) { i++; continue; }
                var marker = data[i + 1];
                var length = data[i + 2] << 8 | data[i + 3];
                if (marker is >= 0xC0 and <= 0xCF && marker is not 0xC4 and not 0xC8 and not 0xCC)
                    return (data[i + 7] << 8 | data[i + 8], data[i + 5] << 8 | data[i + 6]);
                i += 2 + length;
            }
        }
        return null;
    }

    private static int BigEndian(byte[] d, int o) => d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3];

    private async Task<string> CopyInAsync(string sourcePath, CancellationToken ct)
    {
        Directory.CreateDirectory(FolderPath);
        var name = $"{Guid.NewGuid():N}{Path.GetExtension(sourcePath).ToLowerInvariant()}";
        await using (var src = File.OpenRead(sourcePath))
        await using (var dst = File.Create(Path.Combine(FolderPath, name)))
            await src.CopyToAsync(dst, ct);
        return name;
    }

    private static bool IsSafeName(string name)
        => !string.IsNullOrEmpty(name) && name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains("..");
}
