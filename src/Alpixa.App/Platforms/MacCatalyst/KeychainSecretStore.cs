using System.Diagnostics;
using Foundation;
using Alpixa.Core.Abstractions;
using Security;

namespace Alpixa.App.Services;

/// <summary>
/// Stores secrets in the macOS Keychain. Signed builds use the Keychain API directly. Builds signed
/// without a provisioning profile (local development) are denied that API (MissingEntitlement), so
/// they fall back to /usr/bin/security, which writes to the same login keychain. Secrets are passed
/// on stdin, never as command-line arguments.
/// </summary>
public sealed class KeychainSecretStore : ISecretStore
{
    private const string Service = "com.alpixa.app";
    private const string LegacyService = "com.mailpilot.app"; // name before the Alpixa rebrand
    private const string SecurityTool = "/usr/bin/security";
    private bool _useCli;

    private static SecRecord Query(string key) => new(SecKind.GenericPassword) { Service = Service, Account = key };

    public async Task<string?> GetAsync(string key)
    {
        var value = await GetFromAsync(Service, key);
        if (value is not null) return value;
        value = await GetFromAsync(LegacyService, key);
        if (value is not null) await SetAsync(key, value);
        return value;
    }

    private async Task<string?> GetFromAsync(string service, string key)
    {
        if (!_useCli)
        {
            var record = SecKeyChain.QueryAsRecord(new SecRecord(SecKind.GenericPassword) { Service = service, Account = key }, out var code);
            if (code == SecStatusCode.Success && record?.ValueData is { } data)
                return NSString.FromData(data, NSStringEncoding.UTF8)?.ToString();
            if (code == SecStatusCode.MissingEntitlement) _useCli = true;
            // Without the keychain entitlement the API can report "not found" for items that the CLI
            // fallback wrote in an earlier session, so look there before giving up.
        }
        var (exit, output) = await RunAsync(["find-generic-password", "-s", service, "-a", key, "-w"], null);
        if (exit != 0) return null;
        _useCli = true;
        return output.TrimEnd('\n');
    }

    public async Task SetAsync(string key, string value)
    {
        if (!_useCli)
        {
            SecKeyChain.Remove(Query(key));
            var record = Query(key);
            record.ValueData = NSData.FromString(value, NSStringEncoding.UTF8);
            var code = SecKeyChain.Add(record);
            if (code == SecStatusCode.Success) return;
            if (code != SecStatusCode.MissingEntitlement)
                throw new InvalidOperationException($"Anahtar Zinciri'ne kaydedilemedi ({code}).");
            _useCli = true;
        }
        var command = $"add-generic-password -U -s {Quote(Service)} -a {Quote(key)} -w {Quote(value)}\n";
        var (exit, _) = await RunAsync(["-i"], command);
        if (exit != 0) throw new InvalidOperationException("Anahtar Zinciri'ne kaydedilemedi.");
    }

    public async Task RemoveAsync(string key)
    {
        if (!_useCli)
        {
            var code = SecKeyChain.Remove(Query(key));
            if (code != SecStatusCode.MissingEntitlement) return;
            _useCli = true;
        }
        await RunAsync(["delete-generic-password", "-s", Service, "-a", key], null);
    }

    private static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static async Task<(int Exit, string Output)> RunAsync(string[] args, string? stdin)
    {
        var info = new ProcessStartInfo(SecurityTool)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var a in args) info.ArgumentList.Add(a);
        using var process = Process.Start(info)!;
        if (stdin is not null) await process.StandardInput.WriteAsync(stdin);
        process.StandardInput.Close();
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, output);
    }
}
