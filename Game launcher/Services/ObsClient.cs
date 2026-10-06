using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Game_launcher
{
    /// <summary>Ergebnis eines Verbindungsversuchs mit OBS.</summary>
    internal enum ObsConnectResult
    {
        Connected,
        NotReachable,      // OBS läuft nicht oder der WebSocket-Server ist aus
        PasswordRequired,  // OBS verlangt ein Passwort, es ist aber keins eingetragen
        AuthFailed,        // Passwort falsch
        Failed             // sonstiger Fehler (Zeitüberschreitung, unerwartete Antwort)
    }

    /// <summary>
    /// Schlanker Client für OBS WebSocket v5 (ab OBS 28). Ohne zusätzliche Pakete, nur ClientWebSocket und System.Text.Json.
    /// Alle Methoden laufen im Hintergrund; Ereignisse werden auf einem Hintergrund-Thread gemeldet.
    /// </summary>
    internal sealed class ObsClient : IDisposable
    {
        // Ereignis-Gruppen laut Protokoll: General (1), Scenes (4), Inputs (8), Outputs (64)
        private const int EventSubscriptions = 1 | 4 | 8 | 64;

        private ClientWebSocket? socket;
        private CancellationTokenSource? receiveCts;
        private readonly SemaphoreSlim sendLock = new(1, 1);
        private readonly ConcurrentDictionary<string, TaskCompletionSource<JsonElement>> pending = new();
        private TaskCompletionSource<JsonElement>? helloWaiter;
        private TaskCompletionSource<bool>? identifiedWaiter;
        private int requestCounter;
        private volatile bool connected;

        /// <summary>Ein Ereignis von OBS: Typ (zum Beispiel "StreamStateChanged") und Daten.</summary>
        public event Action<string, JsonElement>? EventReceived;

        /// <summary>Die Verbindung ist abgerissen (OBS beendet, Netzwerk weg).</summary>
        public event Action? Disconnected;

        public bool IsConnected => connected && socket?.State == WebSocketState.Open;

        public async Task<ObsConnectResult> ConnectAsync(string host, int port, string password, CancellationToken token)
        {
            await DisconnectAsync().ConfigureAwait(false);

            var ws = new ClientWebSocket();
            ws.Options.AddSubProtocol("obswebsocket.json");
            ws.Options.KeepAliveInterval = TimeSpan.FromSeconds(15);
            socket = ws;

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
            timeout.CancelAfter(TimeSpan.FromSeconds(5));

            try
            {
                await ws.ConnectAsync(new Uri($"ws://{host}:{port}"), timeout.Token).ConfigureAwait(false);
            }
            catch
            {
                CleanupSocket();
                return ObsConnectResult.NotReachable;
            }

            helloWaiter = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            identifiedWaiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            receiveCts = new CancellationTokenSource();
            var receiveTask = Task.Run(() => ReceiveLoopAsync(ws, receiveCts.Token));

            try
            {
                // 1) Hello abwarten, darin steht, ob ein Passwort nötig ist
                var hello = await helloWaiter.Task.WaitAsync(timeout.Token).ConfigureAwait(false);

                string? auth = null;
                if (hello.TryGetProperty("authentication", out var authInfo) && authInfo.ValueKind == JsonValueKind.Object)
                {
                    if (string.IsNullOrEmpty(password))
                    {
                        await DisconnectAsync().ConfigureAwait(false);
                        return ObsConnectResult.PasswordRequired;
                    }
                    string challenge = authInfo.GetProperty("challenge").GetString() ?? string.Empty;
                    string salt = authInfo.GetProperty("salt").GetString() ?? string.Empty;
                    auth = BuildAuth(password, salt, challenge);
                }

                // 2) Identify senden
                var identify = new System.Collections.Generic.Dictionary<string, object>
                {
                    ["rpcVersion"] = 1,
                    ["eventSubscriptions"] = EventSubscriptions
                };
                if (auth != null) identify["authentication"] = auth;
                await SendAsync(new { op = 1, d = identify }, timeout.Token).ConfigureAwait(false);

                // 3) Identified abwarten (bei falschem Passwort schließt OBS die Verbindung mit Code 4009)
                bool ok = await identifiedWaiter.Task.WaitAsync(timeout.Token).ConfigureAwait(false);
                if (!ok)
                {
                    var status = ws.CloseStatus;
                    await DisconnectAsync().ConfigureAwait(false);
                    return (int?)status == 4009 ? ObsConnectResult.AuthFailed : ObsConnectResult.Failed;
                }

                connected = true;
                return ObsConnectResult.Connected;
            }
            catch
            {
                var status = ws.CloseStatus;
                await DisconnectAsync().ConfigureAwait(false);
                return (int?)status == 4009 ? ObsConnectResult.AuthFailed : ObsConnectResult.Failed;
            }
        }

        /// <summary>Sendet eine Anfrage und liefert die Antwortdaten (oder null bei Fehler/Zeitüberschreitung).</summary>
        public async Task<JsonElement?> RequestAsync(string requestType, object? data = null, int timeoutMs = 5000)
        {
            if (!IsConnected) return null;

            string id = "dfp" + Interlocked.Increment(ref requestCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var waiter = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
            pending[id] = waiter;

            try
            {
                object payload = data == null
                    ? new { requestType, requestId = id }
                    : new { requestType, requestId = id, requestData = data };
                await SendAsync(new { op = 6, d = payload }, CancellationToken.None).ConfigureAwait(false);

                var response = await waiter.Task.WaitAsync(TimeSpan.FromMilliseconds(timeoutMs)).ConfigureAwait(false);
                if (response.TryGetProperty("requestStatus", out var status)
                    && status.TryGetProperty("result", out var result) && result.ValueKind == JsonValueKind.True)
                {
                    return response.TryGetProperty("responseData", out var responseData) ? responseData : default(JsonElement);
                }
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                pending.TryRemove(id, out _);
            }
        }

        public async Task DisconnectAsync()
        {
            var ws = socket;
            bool wasConnected = connected;
            connected = false;

            if (ws != null && ws.State == WebSocketState.Open)
            {
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                    await ws.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, string.Empty, cts.Token).ConfigureAwait(false);
                }
                catch { }
            }

            CleanupSocket();
            if (wasConnected) Disconnected?.Invoke();
        }

        private void CleanupSocket()
        {
            try { receiveCts?.Cancel(); } catch { }
            receiveCts = null;

            try { socket?.Abort(); } catch { }
            try { socket?.Dispose(); } catch { }
            socket = null;

            helloWaiter?.TrySetCanceled();
            identifiedWaiter?.TrySetResult(false);
            foreach (var waiter in pending.Values) waiter.TrySetCanceled();
            pending.Clear();
        }

        private async Task SendAsync(object message, CancellationToken token)
        {
            var ws = socket;
            if (ws == null || ws.State != WebSocketState.Open) throw new InvalidOperationException("Nicht verbunden");

            byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(message);
            await sendLock.WaitAsync(token).ConfigureAwait(false);
            try
            {
                await ws.SendAsync(bytes, WebSocketMessageType.Text, true, token).ConfigureAwait(false);
            }
            finally
            {
                sendLock.Release();
            }
        }

        private async Task ReceiveLoopAsync(ClientWebSocket ws, CancellationToken token)
        {
            var buffer = new byte[16 * 1024];
            using var message = new System.IO.MemoryStream();

            try
            {
                while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
                {
                    message.SetLength(0);
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await ws.ReceiveAsync(buffer, token).ConfigureAwait(false);
                        if (result.MessageType == WebSocketMessageType.Close) throw new WebSocketException("closed");
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    HandleMessage(message.ToArray());
                }
            }
            catch { }

            // Verbindung beendet: alle Wartenden freigeben und melden
            bool wasConnected = connected;
            connected = false;
            helloWaiter?.TrySetCanceled();
            identifiedWaiter?.TrySetResult(false);
            foreach (var waiter in pending.Values) waiter.TrySetCanceled();
            if (wasConnected && !token.IsCancellationRequested) Disconnected?.Invoke();
        }

        private void HandleMessage(byte[] bytes)
        {
            JsonElement root;
            try
            {
                using var doc = JsonDocument.Parse(bytes);
                root = doc.RootElement.Clone();
            }
            catch
            {
                return;
            }

            if (!root.TryGetProperty("op", out var opElement) || !root.TryGetProperty("d", out var d)) return;

            switch (opElement.GetInt32())
            {
                case 0: // Hello
                    helloWaiter?.TrySetResult(d);
                    break;
                case 2: // Identified
                    identifiedWaiter?.TrySetResult(true);
                    break;
                case 5: // Event
                    string type = d.TryGetProperty("eventType", out var t) ? t.GetString() ?? string.Empty : string.Empty;
                    var eventData = d.TryGetProperty("eventData", out var ed) ? ed : default;
                    try { EventReceived?.Invoke(type, eventData); } catch { }
                    break;
                case 7: // RequestResponse
                    if (d.TryGetProperty("requestId", out var idElement)
                        && pending.TryGetValue(idElement.GetString() ?? string.Empty, out var waiter))
                        waiter.TrySetResult(d);
                    break;
            }
        }

        /// <summary>Anmeldung laut Protokoll: base64(sha256(base64(sha256(passwort + salt)) + challenge)).</summary>
        internal static string BuildAuth(string password, string salt, string challenge)
        {
            string secret = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(password + salt)));
            return Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(secret + challenge)));
        }

        public void Dispose()
        {
            connected = false;
            CleanupSocket();
            sendLock.Dispose();
        }
    }

    /// <summary>Speichert Geheimnisse (OBS-Passwort) verschlüsselt mit Windows DPAPI, nur für den aktuellen Windows-Benutzer lesbar.</summary>
    internal static class SecretStore
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("DFP-Pro-Launcher/OBS");

        public static string Protect(string plain)
        {
            if (string.IsNullOrEmpty(plain)) return string.Empty;
            try
            {
                byte[] data = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(data);
            }
            catch
            {
                return string.Empty;
            }
        }

        public static string Unprotect(string stored)
        {
            if (string.IsNullOrEmpty(stored)) return string.Empty;
            try
            {
                byte[] data = ProtectedData.Unprotect(Convert.FromBase64String(stored), Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(data);
            }
            catch
            {
                return string.Empty;
            }
        }
    }
}
