using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Game_launcher
{
    /// <summary>
    /// Sehr kleiner HTTP-Server für die Handy-Fernbedienung. Läuft nur im Heimnetz:
    /// Anfragen von öffentlichen Adressen werden abgewiesen, jede API-Anfrage braucht den geheimen Schlüssel.
    /// Ohne HttpListener, damit keine Adminrechte (URL-Reservierung) nötig sind.
    /// </summary>
    internal sealed class RemoteServer
    {
        public sealed class Request
        {
            public string Method = "GET", Path = "/", Query = string.Empty, Body = string.Empty, Token = string.Empty, Host = string.Empty;
            public IPAddress Remote = IPAddress.None;
        }

        public sealed class Response
        {
            public int Status = 200;
            public string ContentType = "application/json; charset=utf-8";
            public byte[] Body = Array.Empty<byte>();
            public string CacheControl = "no-store";

            public static Response Json(string json) => new() { Body = Encoding.UTF8.GetBytes(json) };
            public static Response Error(int status, string message) => new() { Status = status, Body = Encoding.UTF8.GetBytes("{\"error\":" + System.Text.Json.JsonSerializer.Serialize(message) + "}") };
        }

        private readonly Func<Request, Task<Response>> handler;
        private readonly Func<string> page;
        private readonly byte[] token;
        private readonly ConcurrentDictionary<string, (int Fails, DateTime Until)> lockouts = new();
        private TcpListener? listener;
        private CancellationTokenSource? cancel;

        public int Port { get; private set; }
        public bool Running => listener != null;

        public RemoteServer(string token, Func<string> page, Func<Request, Task<Response>> handler)
        {
            this.token = Encoding.UTF8.GetBytes(token);
            this.page = page;
            this.handler = handler;
        }

        /// <summary>Startet auf dem Wunsch-Port oder einem der nächsten freien Ports. Wirft, wenn keiner frei ist.</summary>
        public void Start(int preferredPort)
        {
            Stop();
            Exception? last = null;
            for (int port = preferredPort; port < preferredPort + 10; port++)
            {
                try
                {
                    var l = new TcpListener(IPAddress.Any, port);
                    l.Start();
                    listener = l;
                    Port = port;
                    cancel = new CancellationTokenSource();
                    _ = AcceptLoopAsync(l, cancel.Token);
                    return;
                }
                catch (SocketException ex)
                {
                    last = ex;
                }
            }
            throw last ?? new InvalidOperationException("Kein freier Port");
        }

        public void Stop()
        {
            try { cancel?.Cancel(); } catch { }
            try { listener?.Stop(); } catch { }
            listener = null;
            cancel = null;
        }

        private async Task AcceptLoopAsync(TcpListener l, CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient client;
                try { client = await l.AcceptTcpClientAsync(ct); }
                catch { return; }
                _ = Task.Run(() => ServeAsync(client));
            }
        }

        /// <summary>Nur Heimnetz: Loopback, 10/8, 172.16/12, 192.168/16, 169.254/16 sowie IPv6 lokal.</summary>
        public static bool IsLocalAddress(IPAddress address)
        {
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            if (IPAddress.IsLoopback(address)) return true;
            if (address.AddressFamily == AddressFamily.InterNetworkV6)
                return address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || (address.GetAddressBytes()[0] & 0xFE) == 0xFC;

            byte[] b = address.GetAddressBytes();
            return b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168) || (b[0] == 169 && b[1] == 254);
        }

        private async Task ServeAsync(TcpClient client)
        {
            using (client)
            {
                try
                {
                    client.ReceiveTimeout = client.SendTimeout = 10000;
                    var remote = (client.Client.RemoteEndPoint as IPEndPoint)?.Address ?? IPAddress.None;
                    using var stream = client.GetStream();
                    if (!IsLocalAddress(remote))
                    {
                        await WriteAsync(stream, Response.Error(403, "Nur im Heimnetz erlaubt"));
                        return;
                    }

                    var request = await ReadRequestAsync(stream);
                    if (request == null) return;
                    request.Remote = remote;

                    Response response;
                    if (!HostIsAddress(request.Host))
                    {
                        response = Response.Error(403, "Bitte die IP-Adresse benutzen");
                    }
                    else if (request.Method == "GET" && (request.Path == "/" || request.Path == "/index.html"))
                    {
                        response = new Response { ContentType = "text/html; charset=utf-8", Body = Encoding.UTF8.GetBytes(page()) };
                    }
                    else if (!request.Path.StartsWith("/api/", StringComparison.Ordinal))
                    {
                        response = Response.Error(404, "Nicht gefunden");
                    }
                    else
                    {
                        string ip = remote.ToString();
                        if (lockouts.TryGetValue(ip, out var lockout) && lockout.Until > DateTime.UtcNow)
                        {
                            response = Response.Error(429, "Zu viele Fehlversuche");
                        }
                        else if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(request.Token), token))
                        {
                            // Nach 10 Fehlversuchen ist diese Adresse 5 Minuten gesperrt
                            int fails = lockouts.TryGetValue(ip, out var l) ? l.Fails + 1 : 1;
                            lockouts[ip] = fails >= 10 ? (0, DateTime.UtcNow.AddMinutes(5)) : (fails, DateTime.MinValue);
                            response = Response.Error(401, "Ungültiger Schlüssel");
                        }
                        else
                        {
                            lockouts.TryRemove(ip, out _);
                            response = await handler(request);
                        }
                    }

                    await WriteAsync(stream, response);
                }
                catch { }
            }
        }

        private async Task<Request?> ReadRequestAsync(NetworkStream stream)
        {
            var buffer = new byte[16384];
            int length = 0, headerEnd = -1;
            while (headerEnd < 0)
            {
                if (length == buffer.Length) return null;   // Kopf zu groß
                int read = await stream.ReadAsync(buffer.AsMemory(length, buffer.Length - length));
                if (read == 0) return null;
                length += read;
                headerEnd = IndexOf(buffer, length, "\r\n\r\n"u8);
            }

            string head = Encoding.ASCII.GetString(buffer, 0, headerEnd);
            var lines = head.Split("\r\n");
            var first = lines[0].Split(' ');
            if (first.Length < 2) return null;

            var request = new Request { Method = first[0].ToUpperInvariant() };
            string target = first[1];
            int q = target.IndexOf('?');
            request.Path = q >= 0 ? target[..q] : target;
            request.Query = q >= 0 ? target[(q + 1)..] : string.Empty;

            int contentLength = 0;
            string? supplied = null;
            foreach (var line in lines.Skip(1))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0) continue;
                string name = line[..colon].Trim(), value = line[(colon + 1)..].Trim();
                if (name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase)) int.TryParse(value, out contentLength);
                else if (name.Equals("X-Remote-Token", StringComparison.OrdinalIgnoreCase)) supplied = value;
                else if (name.Equals("Host", StringComparison.OrdinalIgnoreCase)) request.Host = value;
            }
            if (contentLength < 0 || contentLength > 65536) return null;

            request.Token = supplied ?? string.Empty;

            var body = new byte[contentLength];
            int have = Math.Min(contentLength, length - headerEnd - 4);
            Buffer.BlockCopy(buffer, headerEnd + 4, body, 0, have);
            while (have < contentLength)
            {
                int read = await stream.ReadAsync(body.AsMemory(have, contentLength - have));
                if (read == 0) return null;
                have += read;
            }
            request.Body = Encoding.UTF8.GetString(body);
            return request;
        }

        /// <summary>Schutz vor DNS-Rebinding: Nur Aufrufe über eine IP-Adresse oder „localhost“, nie über einen fremden Domainnamen.</summary>
        private static bool HostIsAddress(string host)
        {
            if (host.Length == 0) return false;
            string name = host.StartsWith('[') ? host[1..Math.Max(1, host.IndexOf(']'))] : host.Split(':')[0];
            return name.Equals("localhost", StringComparison.OrdinalIgnoreCase) || IPAddress.TryParse(name, out _);
        }

        public static string QueryValue(string query, string key)
        {
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                string name = eq >= 0 ? part[..eq] : part;
                if (name == key) return Uri.UnescapeDataString(eq >= 0 ? part[(eq + 1)..] : string.Empty);
            }
            return string.Empty;
        }

        private static int IndexOf(byte[] buffer, int length, ReadOnlySpan<byte> pattern)
        {
            int index = buffer.AsSpan(0, length).IndexOf(pattern);
            return index;
        }

        private static async Task WriteAsync(NetworkStream stream, Response response)
        {
            string reason = response.Status switch { 200 => "OK", 401 => "Unauthorized", 403 => "Forbidden", 404 => "Not Found", 429 => "Too Many Requests", _ => "Error" };
            string head = $"HTTP/1.1 {response.Status} {reason}\r\n" +
                          $"Content-Type: {response.ContentType}\r\n" +
                          $"Content-Length: {response.Body.Length}\r\n" +
                          $"Cache-Control: {response.CacheControl}\r\n" +
                          "X-Content-Type-Options: nosniff\r\n" +
                          "Referrer-Policy: no-referrer\r\n" +
                          "Connection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(head));
            await stream.WriteAsync(response.Body);
        }

        /// <summary>Neuer zufälliger Schlüssel (128 Bit, URL-tauglich).</summary>
        public static string NewToken()
            => Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        /// <summary>Beste Heimnetz-Adresse dieses PCs (Netzwerk mit Gateway bevorzugt).</summary>
        public static string? LocalAddress()
        {
            try
            {
                var candidates = new List<(string Address, bool Gateway)>();
                foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (nic.OperationalStatus != OperationalStatus.Up || nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var props = nic.GetIPProperties();
                    bool gateway = props.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork && !g.Address.Equals(IPAddress.Any));
                    foreach (var unicast in props.UnicastAddresses)
                    {
                        var a = unicast.Address;
                        if (a.AddressFamily != AddressFamily.InterNetwork || IPAddress.IsLoopback(a) || !IsLocalAddress(a)) continue;
                        if (a.GetAddressBytes()[0] == 169) continue;   // Ohne DHCP vergeben, nicht erreichbar
                        candidates.Add((a.ToString(), gateway));
                    }
                }
                return candidates.OrderByDescending(c => c.Gateway).Select(c => c.Address).FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }
    }
}
