using System.Globalization;
using System.Text;
using Alpixa.Core.Models;

namespace Alpixa.Infrastructure.Contacts;

public static class ColumnGuesser
{
    private static readonly string[] EmailNames = ["email", "e-posta", "eposta", "e posta", "mail", "e-mail", "email address", "e_posta", "e-mail adresi", "eposta adresi", "mail adresi"];
    private static readonly string[] FirstNames = ["ad", "adi", "isim", "first name", "firstname", "first_name", "name", "given name"];
    private static readonly string[] LastNames = ["soyad", "soyadi", "last name", "lastname", "last_name", "surname", "family name"];
    private static readonly string[] Companies = ["firma", "sirket", "company", "kurum", "organization", "organisation", "firma adi", "sirket adi"];
    private static readonly string[] ConsentSources = ["izin_kaynagi", "izin kaynagi", "consent source", "consent_source", "kaynak", "source"];
    private static readonly string[] ConsentDates = ["izin_tarihi", "izin tarihi", "consent date", "consent_date", "opt-in date", "optin date"];

    public static bool LooksLikeHeader(IReadOnlyList<string> row) => row.Count > 0 && !row.Any(c => c.Contains('@'));

    public static ColumnMapping Guess(IReadOnlyList<string> headers, IReadOnlyList<IReadOnlyList<string>> samples)
    {
        var mapping = new ColumnMapping();
        var normalized = headers.Select(Normalize).ToList();

        mapping.EmailColumn = IndexOf(normalized, EmailNames);
        mapping.FirstNameColumn = IndexOf(normalized, FirstNames);
        mapping.LastNameColumn = IndexOf(normalized, LastNames);
        mapping.CompanyColumn = IndexOf(normalized, Companies);
        mapping.ConsentSourceColumn = IndexOf(normalized, ConsentSources);
        mapping.ConsentDateColumn = IndexOf(normalized, ConsentDates);

        if (mapping.EmailColumn < 0)
        {
            for (var col = 0; col < headers.Count; col++)
            {
                if (samples.Count > 0 && samples.Count(r => col < r.Count && r[col].Contains('@')) * 2 >= samples.Count)
                {
                    mapping.EmailColumn = col;
                    break;
                }
            }
        }

        var used = new HashSet<int>
        {
            mapping.EmailColumn, mapping.FirstNameColumn, mapping.LastNameColumn, mapping.CompanyColumn,
            mapping.ConsentSourceColumn, mapping.ConsentDateColumn
        };
        for (var i = 0; i < headers.Count; i++)
        {
            if (used.Contains(i)) continue;
            var key = ToFieldKey(headers[i]);
            if (key.Length > 0) mapping.CustomColumns[i] = key;
        }
        return mapping;
    }

    public static string ToFieldKey(string header)
    {
        var n = Normalize(header);
        var sb = new StringBuilder();
        foreach (var ch in n)
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(ch);
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var key = sb.ToString().Trim('_');
        if (key.Length > 0 && char.IsAsciiDigit(key[0])) key = "alan_" + key;
        return key;
    }

    public static string Normalize(string header)
    {
        var s = header.Trim().ToLower(new CultureInfo("tr-TR"))
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u').Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c');
        var decomposed = s.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder();
        foreach (var ch in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark) sb.Append(ch);
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static int IndexOf(List<string> normalized, string[] candidates)
    {
        for (var i = 0; i < normalized.Count; i++)
            if (candidates.Contains(normalized[i]))
                return i;
        return -1;
    }
}
