using Alpixa.Core.Localization;
using System.Data.Common;
using System.Globalization;
using System.Text.Json;
using Alpixa.Core.Abstractions;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Alpixa.Infrastructure.Contacts;

public sealed class ContactImportService(
    IDbContextFactory<AlpixaDbContext> dbFactory,
    IMxChecker mxChecker,
    ILogger<ContactImportService> logger)
{
    private const int BatchSize = 5000;
    private static readonly string[] DateFormats = ["yyyy-MM-dd", "dd.MM.yyyy", "d.M.yyyy", "dd/MM/yyyy", "d/M/yyyy", "MM/dd/yyyy", "yyyy-MM-ddTHH:mm:ss", "yyyy-MM-dd HH:mm:ss"];

    public ImportPreview Preview(IEnumerable<string[]> rows, int sampleSize = 20)
    {
        var buffer = rows.Take(sampleSize + 1).Select(r => (IReadOnlyList<string>)r).ToList();
        if (buffer.Count == 0) return new ImportPreview();

        IReadOnlyList<string> headers;
        List<IReadOnlyList<string>> samples;
        if (ColumnGuesser.LooksLikeHeader(buffer[0]))
        {
            headers = buffer[0];
            samples = buffer.Skip(1).ToList();
        }
        else
        {
            var width = buffer.Max(r => r.Count);
            headers = Enumerable.Range(1, width).Select(i => $"kolon{i}").ToList();
            samples = buffer.Take(sampleSize).ToList();
        }
        return new ImportPreview { Headers = headers, SampleRows = samples, Mapping = ColumnGuesser.Guess(headers, samples) };
    }

    public async Task<ImportSummary> ImportAsync(
        int listId,
        IEnumerable<string[]> rows,
        ColumnMapping mapping,
        bool checkMx,
        IProgress<int>? progress,
        CancellationToken ct)
    {
        if (mapping.EmailColumn < 0) throw new InvalidOperationException(Msg.T("Import_01"));

        var summary = new ImportSummary();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var suppressed = new HashSet<string>(await db.Suppressions.AsNoTracking().Select(s => s.EmailNormalized).ToListAsync(ct), StringComparer.Ordinal);
        var domains = new HashSet<string>(StringComparer.Ordinal);

        var connection = db.Database.GetDbConnection();
        await connection.OpenAsync(ct);
        DbTransaction? tx = null;
        DbCommand? cmd = null;
        var inBatch = 0;
        var first = true;
        var now = DateTime.UtcNow;

        try
        {
            foreach (var row in rows)
            {
                ct.ThrowIfCancellationRequested();
                if (first)
                {
                    first = false;
                    if (ColumnGuesser.LooksLikeHeader(row)) continue;
                }

                summary.TotalRows++;
                var rawEmail = Cell(row, mapping.EmailColumn);
                if (string.IsNullOrWhiteSpace(rawEmail))
                {
                    summary.MissingEmail++;
                    continue;
                }

                var status = ContactStatus.Active;
                var flags = ContactFlags.None;
                if (!EmailAddressRules.TryNormalize(rawEmail, out var normalized, out var domain))
                {
                    summary.InvalidSyntax++;
                    status = ContactStatus.Invalid;
                    flags |= ContactFlags.InvalidSyntax;
                    normalized = rawEmail.Trim().ToLowerInvariant();
                    domain = EmailAddressRules.DomainOf(normalized);
                }
                else
                {
                    if (AddressClassifier.IsDisposableDomain(domain))
                    {
                        summary.Disposable++;
                        status = ContactStatus.Invalid;
                        flags |= ContactFlags.Disposable;
                    }
                    if (AddressClassifier.IsRoleAddress(normalized))
                    {
                        summary.RoleAddresses++;
                        flags |= ContactFlags.RoleAddress;
                    }
                    if (status == ContactStatus.Active && suppressed.Contains(normalized))
                    {
                        summary.Suppressed++;
                        status = ContactStatus.Suppressed;
                    }
                }

                if (tx is null)
                {
                    tx = await connection.BeginTransactionAsync(ct);
                    cmd = CreateInsertCommand(connection, tx);
                }

                SetParameters(cmd!, listId, rawEmail.Trim(), normalized, domain, row, mapping, status, flags, now);
                var affected = await cmd!.ExecuteNonQueryAsync(ct);
                if (affected == 0)
                {
                    summary.Duplicates++;
                }
                else
                {
                    summary.Imported++;
                    if (status == ContactStatus.Active) domains.Add(domain);
                }

                if (++inBatch >= BatchSize)
                {
                    await tx.CommitAsync(ct);
                    await tx.DisposeAsync();
                    await cmd.DisposeAsync();
                    tx = null;
                    cmd = null;
                    inBatch = 0;
                    progress?.Report(summary.TotalRows);
                }
            }

            if (tx is not null) await tx.CommitAsync(ct);
        }
        finally
        {
            if (cmd is not null) await cmd.DisposeAsync();
            if (tx is not null) await tx.DisposeAsync();
        }
        progress?.Report(summary.TotalRows);

        if (checkMx && domains.Count > 0)
            summary.NoMx = await MarkDomainsWithoutMxAsync(db, listId, domains, ct);

        logger.LogInformation("Imported {Imported}/{Total} rows into list {ListId}", summary.Imported, summary.TotalRows, listId);
        return summary;
    }

    private async Task<int> MarkDomainsWithoutMxAsync(AlpixaDbContext db, int listId, HashSet<string> domains, CancellationToken ct)
    {
        var bad = new List<string>();
        var gate = new SemaphoreSlim(16);
        var tasks = domains.Select(async d =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (!await mxChecker.HasMailServerAsync(d, ct))
                    lock (bad) bad.Add(d);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogDebug(ex, "MX check failed for {Domain}", d);
            }
            finally
            {
                gate.Release();
            }
        });
        await Task.WhenAll(tasks);

        var total = 0;
        foreach (var chunk in bad.Chunk(200))
        {
            total += await db.Contacts
                .Where(c => c.ListId == listId && c.Status == ContactStatus.Active && chunk.Contains(c.Domain))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(c => c.Status, ContactStatus.Invalid)
                    .SetProperty(c => c.Flags, c => c.Flags | ContactFlags.NoMx), ct);
        }
        return total;
    }

    private static DbCommand CreateInsertCommand(DbConnection connection, DbTransaction tx)
    {
        var cmd = connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = """
            INSERT OR IGNORE INTO Contacts
            (ListId, Email, EmailNormalized, Domain, FirstName, LastName, Company, CustomFieldsJson, ConsentSource, ConsentDate,
             Status, Flags, SoftBounceCount, CreatedUtc, UpdatedUtc)
            VALUES ($list, $email, $norm, $domain, $first, $last, $company, $custom, $csource, $cdate, $status, $flags, 0, $now, $now)
            """;
        foreach (var name in new[] { "$list", "$email", "$norm", "$domain", "$first", "$last", "$company", "$custom", "$csource", "$cdate", "$status", "$flags", "$now" })
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            cmd.Parameters.Add(p);
        }
        cmd.Prepare();
        return cmd;
    }

    private static void SetParameters(DbCommand cmd, int listId, string email, string normalized, string domain, string[] row,
        ColumnMapping mapping, ContactStatus status, ContactFlags flags, DateTime now)
    {
        Dictionary<string, string?>? custom = null;
        foreach (var (col, key) in mapping.CustomColumns)
        {
            var v = Cell(row, col);
            if (string.IsNullOrWhiteSpace(v)) continue;
            custom ??= new Dictionary<string, string?>();
            custom[key] = v;
        }

        cmd.Parameters["$list"].Value = listId;
        cmd.Parameters["$email"].Value = email;
        cmd.Parameters["$norm"].Value = normalized;
        cmd.Parameters["$domain"].Value = domain;
        cmd.Parameters["$first"].Value = NullIfEmpty(Cell(row, mapping.FirstNameColumn));
        cmd.Parameters["$last"].Value = NullIfEmpty(Cell(row, mapping.LastNameColumn));
        cmd.Parameters["$company"].Value = NullIfEmpty(Cell(row, mapping.CompanyColumn));
        cmd.Parameters["$custom"].Value = custom is null ? DBNull.Value : JsonSerializer.Serialize(custom);
        cmd.Parameters["$csource"].Value = NullIfEmpty(Cell(row, mapping.ConsentSourceColumn));
        cmd.Parameters["$cdate"].Value = ParseDate(Cell(row, mapping.ConsentDateColumn)) is { } d ? d : DBNull.Value;
        cmd.Parameters["$status"].Value = (int)status;
        cmd.Parameters["$flags"].Value = (int)flags;
        cmd.Parameters["$now"].Value = now;
    }

    private static string? Cell(string[] row, int index) => index >= 0 && index < row.Length ? row[index]?.Trim() : null;

    private static object NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? DBNull.Value : value;

    internal static DateTime? ParseDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        if (DateTime.TryParseExact(value, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var exact)) return exact;
        if (DateTime.TryParse(value, new CultureInfo("tr-TR"), DateTimeStyles.AssumeLocal, out var tr)) return tr;
        return null;
    }
}
