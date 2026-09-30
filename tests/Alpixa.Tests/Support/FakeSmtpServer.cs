using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;
using MimeKit;

namespace Alpixa.Tests.Support;

public sealed class FakeSmtpServer : IAsyncDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _acceptLoop;
    private int _connections;

    public FakeSmtpServer()
    {
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public int Port { get; }
    public ConcurrentQueue<MimeMessage> Messages { get; } = new();
    public ConcurrentDictionary<string, int> TemporaryFailuresRemaining { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> UnknownRecipients { get; } = new(StringComparer.OrdinalIgnoreCase);
    public int Connections => _connections;

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(_cts.Token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }
            Interlocked.Increment(ref _connections);
            _ = Task.Run(() => HandleAsync(client));
        }
    }

    private async Task HandleAsync(TcpClient client)
    {
        using var _ = client;
        var stream = client.GetStream();
        var reader = new StreamReader(stream, Encoding.ASCII);
        var writer = new StreamWriter(stream, Encoding.ASCII) { NewLine = "\r\n", AutoFlush = true };

        await writer.WriteLineAsync("220 localhost FakeSMTP ready");
        string? recipient = null;
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(_cts.Token);
                if (line is null) return;
                var command = line.Length >= 4 ? line[..4].ToUpperInvariant() : line.ToUpperInvariant();

                switch (command)
                {
                    case "EHLO":
                        await writer.WriteLineAsync("250-localhost");
                        await writer.WriteLineAsync("250-8BITMIME");
                        await writer.WriteLineAsync("250 SIZE 52428800");
                        break;
                    case "HELO":
                        await writer.WriteLineAsync("250 localhost");
                        break;
                    case "MAIL":
                        recipient = null;
                        await writer.WriteLineAsync("250 2.1.0 OK");
                        break;
                    case "RCPT":
                        var address = ExtractAddress(line);
                        if (UnknownRecipients.Contains(address))
                        {
                            await writer.WriteLineAsync("550 5.1.1 <" + address + ">: Recipient address rejected: User unknown");
                            break;
                        }
                        if (TemporaryFailuresRemaining.TryGetValue(address, out var remaining) && remaining > 0)
                        {
                            TemporaryFailuresRemaining[address] = remaining - 1;
                            await writer.WriteLineAsync("451 4.7.1 Try again later");
                            break;
                        }
                        recipient = address;
                        await writer.WriteLineAsync("250 2.1.5 OK");
                        break;
                    case "DATA":
                        if (recipient is null)
                        {
                            await writer.WriteLineAsync("554 5.5.1 No valid recipients");
                            break;
                        }
                        await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");
                        var data = new StringBuilder();
                        while (true)
                        {
                            var dataLine = await reader.ReadLineAsync(_cts.Token);
                            if (dataLine is null || dataLine == ".") break;
                            data.Append(dataLine.StartsWith("..") ? dataLine[1..] : dataLine).Append("\r\n");
                        }
                        using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(data.ToString())))
                            Messages.Enqueue(await MimeMessage.LoadAsync(ms));
                        await writer.WriteLineAsync("250 2.0.0 Ok: queued");
                        break;
                    case "RSET":
                        recipient = null;
                        await writer.WriteLineAsync("250 2.0.0 OK");
                        break;
                    case "NOOP":
                        await writer.WriteLineAsync("250 2.0.0 OK");
                        break;
                    case "QUIT":
                        await writer.WriteLineAsync("221 2.0.0 Bye");
                        return;
                    default:
                        await writer.WriteLineAsync("502 5.5.2 Command not recognized");
                        break;
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private static string ExtractAddress(string line)
    {
        var start = line.IndexOf('<');
        var end = line.IndexOf('>');
        return start >= 0 && end > start ? line[(start + 1)..end] : line[(line.IndexOf(':') + 1)..].Trim();
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        try { await _acceptLoop; } catch (OperationCanceledException) { }
        _cts.Dispose();
    }
}
