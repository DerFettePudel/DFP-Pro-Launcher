using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Game_launcher
{
    /// <summary>
    /// Discord-Status („Spielt …“) über die lokale Schnittstelle der Discord-App (Named Pipe discord-ipc-N), ohne Zusatzpaket.
    /// Läuft Discord nicht, passiert einfach nichts. Alle Aufrufe sind im Hintergrund und werfen nie.
    /// </summary>
    internal sealed class DiscordPresence : IDisposable
    {
        private readonly string clientId;
        private readonly SemaphoreSlim gate = new(1, 1);
        private NamedPipeClientStream? pipe;

        public DiscordPresence(string clientId) => this.clientId = clientId;

        public bool Configured => clientId.Length > 0;

        public Task SetAsync(string details, string state, DateTime startedAt)
        {
            var activity = new
            {
                details,
                state,
                timestamps = new { start = new DateTimeOffset(startedAt).ToUnixTimeSeconds() },
                assets = new { large_image = "logo", large_text = "DFP Pro Launcher" }
            };
            return SendActivityAsync(activity);
        }

        public Task ClearAsync() => SendActivityAsync(null);

        private async Task SendActivityAsync(object? activity)
        {
            if (!Configured) return;

            await gate.WaitAsync().ConfigureAwait(false);
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    try
                    {
                        if (!await EnsureConnectedAsync().ConfigureAwait(false)) return;

                        var payload = new
                        {
                            cmd = "SET_ACTIVITY",
                            args = new { pid = Environment.ProcessId, activity },
                            nonce = Guid.NewGuid().ToString()
                        };
                        await WriteFrameAsync(1, JsonSerializer.Serialize(payload)).ConfigureAwait(false);
                        await ReadFrameAsync().ConfigureAwait(false);   // Antwort abholen, sonst läuft der Puffer voll
                        return;
                    }
                    catch
                    {
                        Disconnect();   // Discord neu gestartet: einmal neu verbinden
                    }
                }
            }
            finally
            {
                gate.Release();
            }
        }

        private async Task<bool> EnsureConnectedAsync()
        {
            if (pipe is { IsConnected: true }) return true;
            Disconnect();

            for (int i = 0; i < 10; i++)
            {
                var candidate = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
                try
                {
                    await candidate.ConnectAsync(150).ConfigureAwait(false);
                    pipe = candidate;
                    await WriteFrameAsync(0, JsonSerializer.Serialize(new { v = 1, client_id = clientId })).ConfigureAwait(false);
                    var (opcode, _) = await ReadFrameAsync().ConfigureAwait(false);
                    if (opcode == 1) return true;   // READY
                }
                catch { }

                candidate.Dispose();
                pipe = null;
            }
            return false;
        }

        private async Task WriteFrameAsync(int opcode, string json)
        {
            byte[] body = Encoding.UTF8.GetBytes(json);
            byte[] frame = new byte[8 + body.Length];
            BitConverter.GetBytes(opcode).CopyTo(frame, 0);
            BitConverter.GetBytes(body.Length).CopyTo(frame, 4);
            body.CopyTo(frame, 8);
            await pipe!.WriteAsync(frame).ConfigureAwait(false);
            await pipe.FlushAsync().ConfigureAwait(false);
        }

        private async Task<(int Opcode, string Json)> ReadFrameAsync()
        {
            using var timeout = new CancellationTokenSource(3000);
            byte[] header = await ReadExactAsync(8, timeout.Token).ConfigureAwait(false);
            int opcode = BitConverter.ToInt32(header, 0);
            int length = BitConverter.ToInt32(header, 4);
            if (length < 0 || length > 1 << 20) throw new InvalidDataException();
            byte[] body = await ReadExactAsync(length, timeout.Token).ConfigureAwait(false);
            if (opcode == 2) throw new IOException("Discord hat die Verbindung geschlossen");
            return (opcode, Encoding.UTF8.GetString(body));
        }

        private async Task<byte[]> ReadExactAsync(int count, CancellationToken token)
        {
            byte[] buffer = new byte[count];
            int read = 0;
            while (read < count)
            {
                int n = await pipe!.ReadAsync(buffer.AsMemory(read, count - read), token).ConfigureAwait(false);
                if (n == 0) throw new EndOfStreamException();
                read += n;
            }
            return buffer;
        }

        private void Disconnect()
        {
            try { pipe?.Dispose(); } catch { }
            pipe = null;
        }

        public void Dispose() => Disconnect();
    }
}
