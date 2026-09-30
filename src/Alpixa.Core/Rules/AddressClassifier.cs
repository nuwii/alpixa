namespace Alpixa.Core.Rules;

public static class AddressClassifier
{
    private static readonly HashSet<string> RoleLocalParts = new(StringComparer.OrdinalIgnoreCase)
    {
        "info", "admin", "administrator", "noreply", "no-reply", "donotreply", "do-not-reply", "postmaster",
        "hostmaster", "webmaster", "abuse", "support", "sales", "contact", "office", "help", "billing",
        "marketing", "root", "security", "hr", "jobs", "careers", "iletisim", "destek", "satis", "muhasebe",
        "bilgi", "ik", "kariyer", "mailer-daemon", "newsletter", "team", "hello", "merhaba"
    };

    private static readonly HashSet<string> DisposableDomains = new(StringComparer.OrdinalIgnoreCase)
    {
        "10minutemail.com", "10minutemail.net", "20minutemail.com", "33mail.com", "anonbox.net", "burnermail.io",
        "discard.email", "dispostable.com", "dropmail.me", "emailondeck.com", "fakeinbox.com", "fakemail.net",
        "getairmail.com", "getnada.com", "guerrillamail.biz", "guerrillamail.com", "guerrillamail.de",
        "guerrillamail.info", "guerrillamail.net", "guerrillamail.org", "guerrillamailblock.com", "harakirimail.com",
        "inboxbear.com", "incognitomail.org", "jetable.org", "mail-temp.com", "mail.tm", "mailcatch.com",
        "maildrop.cc", "mailinator.com", "mailinator.net", "mailinator2.com", "mailnesia.com", "mailpoof.com",
        "mailsac.com", "mintemail.com", "mohmal.com", "moakt.com", "mytemp.email", "mytrashmail.com", "nada.email",
        "nowmymail.com", "sharklasers.com", "spam4.me", "spambog.com", "spambox.us", "spamgourmet.com",
        "spamex.com", "tempail.com", "tempinbox.com", "tempmail.com", "tempmail.net", "tempmail.plus",
        "tempmailo.com", "temp-mail.io", "temp-mail.org", "tempr.email", "throwawaymail.com", "trash-mail.com",
        "trashmail.com", "trashmail.de", "trashmail.net", "yopmail.com", "yopmail.fr", "yopmail.net",
        "emailfake.com", "emailtemporanea.net", "fexbox.org", "grr.la", "guerrillamail.email", "inboxkitten.com",
        "mailforspam.com", "mailnull.com", "mvrht.net", "owlymail.com", "rootfest.net", "spamdecoy.net",
        "tmail.ws", "tmpmail.net", "tmpmail.org", "wegwerfmail.de", "wegwerfmail.net", "zetmail.com",
        "byom.de", "cuvox.de", "dayrep.com", "einrot.com", "fleckens.hu", "gustr.com", "jourrapide.com",
        "rhyta.com", "superrito.com", "teleworm.us", "armyspy.com", "tempmailaddress.com", "mailexpire.com",
        "spamfree24.org", "mailmetrash.com", "trbvm.com", "linshiyouxiang.net", "tempmail.dev", "1secmail.com",
        "1secmail.net", "1secmail.org", "emlpro.com", "emltmp.com", "laafd.com", "vjuum.com", "txcct.com"
    };

    public static bool IsRoleAddress(string email) => RoleLocalParts.Contains(EmailAddressRules.LocalPartOf(email));

    public static bool IsDisposableDomain(string domain) => DisposableDomains.Contains(domain);

    public static int DisposableDomainCount => DisposableDomains.Count;
}
