using Alpixa.Core.Localization;
using System.Text.Json;
using Alpixa.Core.Models;
using Alpixa.Core.Rules;
using Alpixa.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace Alpixa.Infrastructure.Contacts;

public sealed record ContactListSummary(int Id, string Name, DateTime CreatedUtc, int Total, int Active, int Invalid, int Suppressed, bool ConsentConfirmed);

public sealed class ContactService(IDbContextFactory<AlpixaDbContext> dbFactory)
{
    public async Task<List<ContactListSummary>> GetListSummariesAsync(CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var lists = await db.ContactLists.AsNoTracking().OrderByDescending(l => l.CreatedUtc).ToListAsync(ct);
        var counts = await db.Contacts.AsNoTracking()
            .GroupBy(c => new { c.ListId, c.Status })
            .Select(g => new { g.Key.ListId, g.Key.Status, Count = g.Count() })
            .ToListAsync(ct);

        return lists.Select(l =>
        {
            var mine = counts.Where(c => c.ListId == l.Id).ToList();
            return new ContactListSummary(l.Id, l.Name, l.CreatedUtc,
                mine.Sum(c => c.Count),
                mine.Where(c => c.Status == ContactStatus.Active).Sum(c => c.Count),
                mine.Where(c => c.Status == ContactStatus.Invalid).Sum(c => c.Count),
                mine.Where(c => c.Status is not ContactStatus.Active and not ContactStatus.Invalid).Sum(c => c.Count),
                l.ConsentConfirmed);
        }).ToList();
    }

    public async Task<ContactList> CreateListAsync(string name, bool consentConfirmed, CancellationToken ct = default)
    {
        if (!consentConfirmed) throw new InvalidOperationException(Msg.T("Contacts_01"));
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var list = new ContactList { Name = name.Trim(), ConsentConfirmed = true, ConsentConfirmedUtc = DateTime.UtcNow };
        db.ContactLists.Add(list);
        await db.SaveChangesAsync(ct);
        return list;
    }

    public async Task DeleteListAsync(int listId, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        await db.Contacts.Where(c => c.ListId == listId).ExecuteDeleteAsync(ct);
        await db.ContactLists.Where(l => l.Id == listId).ExecuteDeleteAsync(ct);
    }

    public async Task<List<Contact>> GetContactsAsync(int listId, string? search, ContactStatus? status, int skip, int take, CancellationToken ct = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var q = db.Contacts.AsNoTracking().Where(c => c.ListId == listId);
        if (status is not null) q = q.Where(c => c.Status == status);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.EmailNormalized.Contains(s) || (c.FirstName != null && c.FirstName.ToLower().Contains(s))
                                                          || (c.LastName != null && c.LastName.ToLower().Contains(s))
                                                          || (c.Company != null && c.Company.ToLower().Contains(s)));
        }
        return await q.OrderBy(c => c.Id).Skip(skip).Take(take).ToListAsync(ct);
    }

    public async Task<int> ForgetAsync(string email, CancellationToken ct = default)
    {
        var normalized = EmailAddressRules.TryNormalize(email, out var n, out _) ? n : email.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var removed = await db.Contacts.Where(c => c.EmailNormalized == normalized).ExecuteDeleteAsync(ct);
        await db.DeliveryEvents.Where(e => e.EmailNormalized == normalized).ExecuteDeleteAsync(ct);
        await db.SendJobs.Where(j => j.Email == normalized)
            .ExecuteUpdateAsync(s => s.SetProperty(j => j.Email, "silindi")
                .SetProperty(j => j.RecipientDomain, "")
                .SetProperty(j => j.LastError, (string?)null), ct);
        return removed;
    }

    public async Task<string> ExportPersonalDataAsync(string email, CancellationToken ct = default)
    {
        var normalized = EmailAddressRules.TryNormalize(email, out var n, out _) ? n : email.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var contacts = await db.Contacts.AsNoTracking().Where(c => c.EmailNormalized == normalized).ToListAsync(ct);
        var listIds = contacts.Select(c => c.ListId).ToList();
        var listNames = await db.ContactLists.AsNoTracking().Where(l => listIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, l => l.Name, ct);
        var jobs = await db.SendJobs.AsNoTracking().Where(j => j.Email == normalized)
            .Join(db.Campaigns, j => j.CampaignId, c => c.Id, (j, c) => new { Kampanya = c.Name, j.Status, j.SentUtc })
            .ToListAsync(ct);
        var events = await db.DeliveryEvents.AsNoTracking().Where(e => e.EmailNormalized == normalized)
            .Select(e => new { e.Kind, e.OccurredUtc, e.Source }).ToListAsync(ct);
        var suppression = await db.Suppressions.AsNoTracking().FirstOrDefaultAsync(s => s.EmailNormalized == normalized, ct);

        var payload = new
        {
            Email = normalized,
            OlusturmaTarihi = DateTime.UtcNow,
            Kayitlar = contacts.Select(c => new
            {
                Liste = listNames.GetValueOrDefault(c.ListId),
                c.Email,
                Ad = c.FirstName,
                Soyad = c.LastName,
                Firma = c.Company,
                OzelAlanlar = c.CustomFieldsJson,
                IzinKaynagi = c.ConsentSource,
                IzinTarihi = c.ConsentDate,
                Durum = c.Status.ToString(),
                c.CreatedUtc
            }),
            Gonderimler = jobs.Select(j => new { j.Kampanya, Durum = j.Status.ToString(), j.SentUtc }),
            Olaylar = events.Select(e => new { Tur = e.Kind.ToString(), e.OccurredUtc, e.Source }),
            BastirmaListesi = suppression is null ? null : new { Neden = suppression.Reason.ToString(), suppression.CreatedUtc }
        };
        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    public async Task<List<(Campaign Campaign, SendJob Job)>> GetHistoryAsync(string email, CancellationToken ct = default)
    {
        var normalized = EmailAddressRules.TryNormalize(email, out var n, out _) ? n : email.Trim().ToLowerInvariant();
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var rows = await db.SendJobs.AsNoTracking().Where(j => j.Email == normalized)
            .Join(db.Campaigns.AsNoTracking(), j => j.CampaignId, c => c.Id, (j, c) => new { c, j })
            .OrderByDescending(x => x.j.UpdatedUtc)
            .ToListAsync(ct);
        return rows.Select(r => (r.c, r.j)).ToList();
    }
}
