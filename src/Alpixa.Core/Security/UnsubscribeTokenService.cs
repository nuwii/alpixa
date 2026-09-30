using System.Security.Cryptography;
using System.Text;

namespace Alpixa.Core.Security;

public sealed record UnsubscribeTokenPayload(string Email, int CampaignId);

public sealed class UnsubscribeTokenService
{
    private readonly byte[] _key;

    public UnsubscribeTokenService(string secret)
    {
        if (string.IsNullOrWhiteSpace(secret)) throw new ArgumentException("Secret is required.", nameof(secret));
        _key = SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    public static string GenerateSecret() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public string Create(string email, int campaignId)
    {
        var plain = Encoding.UTF8.GetBytes($"{campaignId}|{email.ToLowerInvariant()}");
        var nonce = RandomNumberGenerator.GetBytes(AesGcm.NonceByteSizes.MaxSize);
        var cipher = new byte[plain.Length];
        var tag = new byte[AesGcm.TagByteSizes.MaxSize];
        using (var aes = new AesGcm(_key, tag.Length))
            aes.Encrypt(nonce, plain, cipher, tag);

        var buffer = new byte[nonce.Length + tag.Length + cipher.Length];
        nonce.CopyTo(buffer, 0);
        tag.CopyTo(buffer, nonce.Length);
        cipher.CopyTo(buffer, nonce.Length + tag.Length);
        return Base64UrlEncode(buffer);
    }

    public bool TryRead(string token, out UnsubscribeTokenPayload? payload)
    {
        payload = null;
        try
        {
            var buffer = Base64UrlDecode(token);
            var nonceSize = AesGcm.NonceByteSizes.MaxSize;
            var tagSize = AesGcm.TagByteSizes.MaxSize;
            if (buffer.Length <= nonceSize + tagSize) return false;

            var nonce = buffer.AsSpan(0, nonceSize);
            var tag = buffer.AsSpan(nonceSize, tagSize);
            var cipher = buffer.AsSpan(nonceSize + tagSize);
            var plain = new byte[cipher.Length];
            using (var aes = new AesGcm(_key, tagSize))
                aes.Decrypt(nonce, cipher, tag, plain);

            var text = Encoding.UTF8.GetString(plain);
            var sep = text.IndexOf('|');
            if (sep <= 0 || !int.TryParse(text[..sep], out var campaignId)) return false;
            payload = new UnsubscribeTokenPayload(text[(sep + 1)..], campaignId);
            return true;
        }
        catch (Exception ex) when (ex is FormatException or CryptographicException or ArgumentException)
        {
            return false;
        }
    }

    private static string Base64UrlEncode(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string text)
    {
        var s = text.Replace('-', '+').Replace('_', '/');
        s += (s.Length % 4) switch { 2 => "==", 3 => "=", _ => "" };
        return Convert.FromBase64String(s);
    }
}
