using Alpixa.Core.Models;

namespace Alpixa.Infrastructure.Templates;

public static class BuiltInTemplates
{
    private const string Shell = """
        <!DOCTYPE html>
        <html lang="tr">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{ konu }}</title>
        </head>
        <body style="margin:0;padding:0;background:#f4f5f7;">
        <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background:#f4f5f7;">
        <tr><td align="center" style="padding:24px 12px;">
        <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px;width:100%;background:#ffffff;border-radius:8px;">
        <tr><td style="padding:32px;font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:1.6;color:#1f2933;">
        @@CONTENT@@
        </td></tr>
        </table>
        </td></tr>
        </table>
        </body>
        </html>
        """;

    public static IEnumerable<EmailTemplate> Create() =>
    [
        Build("Duyuru", "{{ firma ?? \"Sizin\" }} için yeni bir duyurumuz var", """
            <h1 style="font-size:22px;margin:0 0 16px;">Merhaba {{ ad ?? "" }},</h1>
            <p>Size kısa bir haberimiz var. Buraya duyurunuzun ana mesajını yazın. İki üç cümle yeterli.</p>
            <p>Daha fazla bilgi için web sitemizi ziyaret edebilirsiniz:</p>
            <p><a href="https://www.ornek.com" style="color:#1a56db;">www.ornek.com</a></p>
            <p>Saygılarımızla,<br>{{ gonderen_adi }}</p>
            """),
        Build("Bülten", "{{ ay }} bülteni", """
            <h1 style="font-size:22px;margin:0 0 16px;">{{ ay }} bülteni</h1>
            <p>Merhaba {{ ad ?? "" }}, bu ay sizin için seçtiklerimiz:</p>
            <h2 style="font-size:18px;margin:24px 0 8px;">1. İlk başlık</h2>
            <p>Kısa bir açıklama. Detaylar için <a href="https://www.ornek.com/haber-1" style="color:#1a56db;">haberin tamamını okuyun</a>.</p>
            <h2 style="font-size:18px;margin:24px 0 8px;">2. İkinci başlık</h2>
            <p>Kısa bir açıklama. Detaylar için <a href="https://www.ornek.com/haber-2" style="color:#1a56db;">haberin tamamını okuyun</a>.</p>
            <p>Gelecek ay görüşmek üzere,<br>{{ gonderen_adi }}</p>
            """),
        Build("Davet", "Davetlisiniz: etkinlik adı", """
            <h1 style="font-size:22px;margin:0 0 16px;">Sayın {{ ad ?? "" }} {{ soyad ?? "" }},</h1>
            <p>Sizi etkinliğimize davet etmekten mutluluk duyarız.</p>
            <table role="presentation" cellpadding="0" cellspacing="0" style="margin:16px 0;">
            <tr><td style="padding:4px 12px 4px 0;"><strong>Tarih:</strong></td><td>12 Ekim, 14:00</td></tr>
            <tr><td style="padding:4px 12px 4px 0;"><strong>Yer:</strong></td><td>Etkinlik adresi</td></tr>
            </table>
            <p><a href="https://www.ornek.com/kayit" style="display:inline-block;background:#1a56db;color:#ffffff;padding:12px 20px;border-radius:6px;text-decoration:none;">Katılımımı bildir</a></p>
            <p>Görüşmek dileğiyle,<br>{{ gonderen_adi }}</p>
            """),
        Build("Sade mektup", "Kısa bir bilgilendirme", """
            <p>Merhaba {{ ad ?? "" }},</p>
            <p>Buraya mesajınızı yazın. Sade ve kişisel görünen e-postalar genellikle gelen kutusuna daha iyi ulaşır.</p>
            <p>Sevgiler,<br>{{ gonderen_adi }}</p>
            """)
    ];

    private static EmailTemplate Build(string name, string subject, string content) => new()
    {
        Name = name,
        Subject = subject,
        HtmlBody = Shell.Replace("@@CONTENT@@", content),
        IsBuiltIn = true
    };
}
