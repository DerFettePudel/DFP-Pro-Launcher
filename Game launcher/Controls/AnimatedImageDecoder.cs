using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Game_launcher
{
    /// <summary>
    /// Liest bewegte Bilder (GIF, APNG, WebP) nur mit den Bildfunktionen von Windows.
    /// APNG- und WebP-Einzelbilder werden dafür als eigenständige PNG-/WebP-Datei verpackt und von Windows dekodiert,
    /// danach setzt diese Klasse sie wie ein Browser auf einer Leinwand zusammen.
    /// </summary>
    internal static class AnimatedImageDecoder
    {
        private const int MaxCanvasPixels = 4096 * 4096;

        private sealed class RawFrame
        {
            public int X, Y, Width, Height, DelayMs;
            public int Dispose;          // 0 = stehen lassen, 1 = Bereich leeren, 2 = vorherigen Zustand wiederherstellen
            public bool Blend = true;    // false = Bereich ersetzen statt überlagern
            public Func<BitmapSource> Decode = null!;
        }

        /// <summary>
        /// Setzt die Animation zusammen. Höchstens <paramref name="maxFrames"/> Bilder werden gelesen, davon höchstens etwa
        /// <paramref name="keepFrames"/> behalten (übersprungene Bilder verlängern die Anzeigedauer des vorigen).
        /// </summary>
        public static List<(BitmapSource Frame, int DelayMs)> Decode(string path, int maxWidth, int maxFrames, int keepFrames, out int totalFrames)
        {
            byte[] data = File.ReadAllBytes(path);
            var (canvasWidth, canvasHeight, frames) = Parse(data);
            if (canvasWidth <= 0 || canvasHeight <= 0 || (long)canvasWidth * canvasHeight > MaxCanvasPixels)
                throw new InvalidDataException("Ungültige Bildgröße " + canvasWidth + "x" + canvasHeight);

            totalFrames = Math.Min(frames.Count, maxFrames);
            int stride = Math.Max(1, (int)Math.Ceiling(totalFrames / (double)Math.Max(1, keepFrames)));
            double scale = maxWidth > 0 && canvasWidth > maxWidth ? maxWidth / (double)canvasWidth : 1.0;

            var result = new List<(BitmapSource, int)>();
            var canvas = new byte[canvasWidth * canvasHeight * 4];

            for (int i = 0; i < totalFrames; i++)
            {
                var frame = frames[i];
                byte[]? previous = frame.Dispose == 2 ? (byte[])canvas.Clone() : null;

                var source = frame.Decode();
                if (source.Format != PixelFormats.Pbgra32) source = new FormatConvertedBitmap(source, PixelFormats.Pbgra32, null, 0);
                int width = source.PixelWidth, height = source.PixelHeight;
                var pixels = new byte[width * height * 4];
                source.CopyPixels(pixels, width * 4, 0);
                Draw(canvas, canvasWidth, canvasHeight, pixels, width, height, frame.X, frame.Y, frame.Blend);

                if (i % stride == 0) result.Add((Snapshot(canvas, canvasWidth, canvasHeight, scale), frame.DelayMs));
                else if (result.Count > 0) result[^1] = (result[^1].Item1, result[^1].Item2 + frame.DelayMs);

                if (frame.Dispose == 1) Clear(canvas, canvasWidth, canvasHeight, frame.X, frame.Y, frame.Width, frame.Height);
                else if (previous != null) canvas = previous;
            }

            for (int i = 0; i < result.Count; i++) result[i] = (result[i].Item1, Math.Clamp(result[i].Item2, 20, 5000));
            return result;
        }

        /// <summary>Erstes Bild einer Animation (oder eines Standbilds) als PNG im Speicher, für BitmapImage.</summary>
        public static MemoryStream? FirstFrameAsPng(string path)
        {
            var frames = Decode(path, 0, 1, 1, out _);
            if (frames.Count == 0) return null;

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(frames[0].Frame));
            var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;
            return stream;
        }

        private static (int Width, int Height, List<RawFrame> Frames) Parse(byte[] data)
        {
            if (data.Length > 12 && Ascii(data, 0, 4) == "RIFF" && Ascii(data, 8, 4) == "WEBP") return ParseWebp(data);
            if (data.Length > 8 && data[0] == 0x89 && Ascii(data, 1, 3) == "PNG") return ParseApng(data);
            if (data.Length > 6 && Ascii(data, 0, 3) == "GIF") return ParseGif(data);
            throw new InvalidDataException("Unbekanntes Bildformat");
        }

        // ───────────────────────────── GIF ─────────────────────────────

        private static (int, int, List<RawFrame>) ParseGif(byte[] data)
        {
            var decoder = new GifBitmapDecoder(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            int width = data[6] | data[7] << 8;      // logischer Bildschirm laut GIF-Kopf
            int height = data[8] | data[9] << 8;

            var frames = new List<RawFrame>();
            foreach (var bitmap in decoder.Frames)
            {
                var meta = bitmap.Metadata as BitmapMetadata;
                int disposal = Query(meta, "/grctlext/Disposal");
                int delay = Query(meta, "/grctlext/Delay") * 10;
                frames.Add(new RawFrame
                {
                    X = Query(meta, "/imgdesc/Left"),
                    Y = Query(meta, "/imgdesc/Top"),
                    Width = bitmap.PixelWidth,
                    Height = bitmap.PixelHeight,
                    DelayMs = delay < 20 ? 100 : delay,      // wie Browser: 0 und 1 bedeuten 100 ms
                    Dispose = disposal == 2 ? 1 : disposal == 3 ? 2 : 0,
                    Decode = () => bitmap
                });
            }

            if (width == 0 || height == 0) { width = frames.Count > 0 ? frames[0].Width : 0; height = frames.Count > 0 ? frames[0].Height : 0; }
            return (width, height, frames);
        }

        private static int Query(BitmapMetadata? meta, string query)
        {
            try { return meta?.GetQuery(query) is object value ? Convert.ToInt32(value) : 0; }
            catch { return 0; }
        }

        // ───────────────────────────── APNG ─────────────────────────────

        private static (int, int, List<RawFrame>) ParseApng(byte[] data)
        {
            byte[]? ihdr = null;
            var headerChunks = new List<(string Type, byte[] Data)>();   // PLTE, tRNS, gAMA … gelten für alle Einzelbilder
            var frames = new List<RawFrame>();
            List<byte[]>? parts = null;
            bool seenImageData = false;

            int pos = 8;
            while (pos + 12 <= data.Length)
            {
                int length = (int)BigEndian32(data, pos);
                string type = Ascii(data, pos + 4, 4);
                if (length < 0 || pos + 12 + length > data.Length) break;
                var body = new byte[length];
                Buffer.BlockCopy(data, pos + 8, body, 0, length);
                pos += 12 + length;

                switch (type)
                {
                    case "IHDR":
                        ihdr = body;
                        break;
                    case "fcTL" when length >= 26:
                        int delayNum = BigEndian16(body, 20), delayDen = BigEndian16(body, 22);
                        if (delayDen == 0) delayDen = 100;
                        parts = new List<byte[]>();
                        var frameParts = parts;
                        var frame = new RawFrame
                        {
                            Width = (int)BigEndian32(body, 4),
                            Height = (int)BigEndian32(body, 8),
                            X = (int)BigEndian32(body, 12),
                            Y = (int)BigEndian32(body, 16),
                            DelayMs = delayNum == 0 ? 100 : delayNum * 1000 / delayDen,
                            Dispose = body[24] switch { 1 => 1, 2 => 2, _ => 0 },
                            Blend = body[25] == 1
                        };
                        frame.Decode = () => DecodePng(ihdr!, headerChunks, frame.Width, frame.Height, frameParts);
                        frames.Add(frame);
                        break;
                    case "IDAT":
                        seenImageData = true;
                        parts?.Add(body);   // IDAT ohne vorheriges fcTL ist nur ein Vorschaubild
                        break;
                    case "fdAT" when length > 4:
                        parts?.Add(body[4..]);
                        break;
                    case "IEND":
                        pos = data.Length;
                        break;
                    case "acTL":
                        break;
                    default:
                        if (!seenImageData && type != "fcTL") headerChunks.Add((type, body));
                        break;
                }
            }

            if (ihdr == null) throw new InvalidDataException("PNG ohne Kopf");
            int width = (int)BigEndian32(ihdr, 0), height = (int)BigEndian32(ihdr, 4);

            if (frames.Count == 0)
            {
                // Normales PNG: Windows liest es direkt
                frames.Add(new RawFrame
                {
                    Width = width, Height = height, DelayMs = 100,
                    Decode = () => BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0]
                });
            }

            return (width, height, frames);
        }

        private static BitmapSource DecodePng(byte[] ihdr, List<(string Type, byte[] Data)> headerChunks, int width, int height, List<byte[]> parts)
        {
            using var stream = new MemoryStream();
            stream.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

            var header = (byte[])ihdr.Clone();
            WriteBigEndian32(header, 0, (uint)width);
            WriteBigEndian32(header, 4, (uint)height);
            WritePngChunk(stream, "IHDR", header);
            foreach (var (type, body) in headerChunks) WritePngChunk(stream, type, body);

            int total = 0;
            foreach (var part in parts) total += part.Length;
            var imageData = new byte[total];
            int offset = 0;
            foreach (var part in parts) { Buffer.BlockCopy(part, 0, imageData, offset, part.Length); offset += part.Length; }
            WritePngChunk(stream, "IDAT", imageData);
            WritePngChunk(stream, "IEND", Array.Empty<byte>());

            stream.Position = 0;
            return new PngBitmapDecoder(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }

        private static void WritePngChunk(Stream stream, string type, byte[] body)
        {
            var head = new byte[8];
            WriteBigEndian32(head, 0, (uint)body.Length);
            Encoding.ASCII.GetBytes(type, 0, 4, head, 4);
            stream.Write(head);
            stream.Write(body);

            uint crc = Crc32(head, 4, 4, 0xFFFFFFFF);
            crc = Crc32(body, 0, body.Length, crc) ^ 0xFFFFFFFF;
            var tail = new byte[4];
            WriteBigEndian32(tail, 0, crc);
            stream.Write(tail);
        }

        private static uint[]? crcTable;

        private static uint Crc32(byte[] buffer, int offset, int count, uint crc)
        {
            var table = crcTable;
            if (table == null)
            {
                table = new uint[256];
                for (uint n = 0; n < 256; n++)
                {
                    uint c = n;
                    for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                    table[n] = c;
                }
                crcTable = table;
            }

            for (int i = offset; i < offset + count; i++) crc = table[(crc ^ buffer[i]) & 0xFF] ^ (crc >> 8);
            return crc;
        }

        // ───────────────────────────── WebP ─────────────────────────────

        private static (int, int, List<RawFrame>) ParseWebp(byte[] data)
        {
            var frames = new List<RawFrame>();
            int width = 0, height = 0;
            bool extended = false;

            int end = Math.Min(data.Length, 8 + (int)LittleEndian32(data, 4));
            int pos = 12;
            while (pos + 8 <= end)
            {
                string type = Ascii(data, pos, 4);
                int length = (int)LittleEndian32(data, pos + 4);
                int body = pos + 8;
                if (length < 0 || body + length > end) break;

                if (type == "VP8X" && length >= 10)
                {
                    extended = true;
                    width = 1 + LittleEndian24(data, body + 4);
                    height = 1 + LittleEndian24(data, body + 7);
                }
                else if (type == "ANMF" && length >= 16)
                {
                    int flags = data[body + 15];
                    int frameStart = body + 16, frameLength = length - 16;
                    var frame = new RawFrame
                    {
                        X = 2 * LittleEndian24(data, body),
                        Y = 2 * LittleEndian24(data, body + 3),
                        Width = 1 + LittleEndian24(data, body + 6),
                        Height = 1 + LittleEndian24(data, body + 9),
                        Dispose = (flags & 1) != 0 ? 1 : 0,
                        Blend = (flags & 2) == 0
                    };
                    int duration = LittleEndian24(data, body + 12);
                    frame.DelayMs = duration == 0 ? 100 : duration;
                    frame.Decode = () => DecodeWebpFrame(data, frameStart, frameLength, frame.Width, frame.Height);
                    frames.Add(frame);
                }

                pos = body + length + (length & 1);
            }

            if (frames.Count == 0)
            {
                // Standbild: Windows (WebP-Erweiterung) liest die Datei direkt
                var still = BitmapDecoder.Create(new MemoryStream(data), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                if (!extended || width == 0) { width = still.PixelWidth; height = still.PixelHeight; }
                frames.Add(new RawFrame { Width = width, Height = height, DelayMs = 100, Decode = () => still });
            }

            return (width, height, frames);
        }

        /// <summary>Verpackt ein WebP-Einzelbild (optional ALPH + VP8/VP8L) als eigene Datei und lässt Windows es dekodieren.</summary>
        private static BitmapSource DecodeWebpFrame(byte[] data, int start, int length, int width, int height)
        {
            bool hasAlphaChunk = length >= 4 && Ascii(data, start, 4) == "ALPH";

            using var stream = new MemoryStream();
            stream.Write(Encoding.ASCII.GetBytes("RIFF"));
            stream.Write(new byte[4]);
            stream.Write(Encoding.ASCII.GetBytes("WEBP"));

            if (hasAlphaChunk)
            {
                var vp8x = new byte[18];
                Encoding.ASCII.GetBytes("VP8X", 0, 4, vp8x, 0);
                vp8x[4] = 10;
                vp8x[8] = 0x10;   // Alpha vorhanden
                WriteLittleEndian24(vp8x, 12, width - 1);
                WriteLittleEndian24(vp8x, 15, height - 1);
                stream.Write(vp8x);
            }

            stream.Write(data, start, length);
            if ((length & 1) != 0) stream.WriteByte(0);

            var bytes = stream.ToArray();
            uint riffSize = (uint)(bytes.Length - 8);
            bytes[4] = (byte)riffSize; bytes[5] = (byte)(riffSize >> 8); bytes[6] = (byte)(riffSize >> 16); bytes[7] = (byte)(riffSize >> 24);

            return BitmapDecoder.Create(new MemoryStream(bytes), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        }

        // ───────────────────────────── Leinwand ─────────────────────────────

        private static void Draw(byte[] canvas, int canvasWidth, int canvasHeight, byte[] pixels, int width, int height, int x, int y, bool blend)
        {
            int startX = Math.Max(0, -x), endX = Math.Min(width, canvasWidth - x);
            int startY = Math.Max(0, -y), endY = Math.Min(height, canvasHeight - y);
            if (startX >= endX || startY >= endY) return;

            for (int row = startY; row < endY; row++)
            {
                int src = (row * width + startX) * 4;
                int dst = ((row + y) * canvasWidth + x + startX) * 4;

                if (!blend)
                {
                    Buffer.BlockCopy(pixels, src, canvas, dst, (endX - startX) * 4);
                    continue;
                }

                for (int col = startX; col < endX; col++, src += 4, dst += 4)
                {
                    int alpha = pixels[src + 3];
                    if (alpha == 0) continue;
                    if (alpha == 255)
                    {
                        canvas[dst] = pixels[src]; canvas[dst + 1] = pixels[src + 1]; canvas[dst + 2] = pixels[src + 2]; canvas[dst + 3] = 255;
                        continue;
                    }

                    int inverse = 255 - alpha;   // vormultipliziert: Ergebnis = Quelle + Ziel × (1 − Alpha)
                    for (int c = 0; c < 4; c++) canvas[dst + c] = (byte)(pixels[src + c] + (canvas[dst + c] * inverse + 127) / 255);
                }
            }
        }

        private static void Clear(byte[] canvas, int canvasWidth, int canvasHeight, int x, int y, int width, int height)
        {
            int startX = Math.Max(0, x), endX = Math.Min(canvasWidth, x + width);
            int startY = Math.Max(0, y), endY = Math.Min(canvasHeight, y + height);
            if (startX >= endX) return;
            for (int row = startY; row < endY; row++) Array.Clear(canvas, (row * canvasWidth + startX) * 4, (endX - startX) * 4);
        }

        private static BitmapSource Snapshot(byte[] canvas, int width, int height, double scale)
        {
            BitmapSource image = BitmapSource.Create(width, height, 96, 96, PixelFormats.Pbgra32, null, canvas, width * 4);
            if (scale < 1.0)
            {
                var scaled = new TransformedBitmap(image, new ScaleTransform(scale, scale));
                int scaledWidth = scaled.PixelWidth, scaledHeight = scaled.PixelHeight;
                var pixels = new byte[scaledWidth * scaledHeight * 4];
                scaled.CopyPixels(pixels, scaledWidth * 4, 0);
                image = BitmapSource.Create(scaledWidth, scaledHeight, 96, 96, PixelFormats.Pbgra32, null, pixels, scaledWidth * 4);
            }

            image.Freeze();
            return image;
        }

        // ───────────────────────────── Bytes lesen ─────────────────────────────

        private static string Ascii(byte[] data, int offset, int count) => Encoding.ASCII.GetString(data, offset, count);
        private static uint BigEndian32(byte[] d, int o) => (uint)(d[o] << 24 | d[o + 1] << 16 | d[o + 2] << 8 | d[o + 3]);
        private static int BigEndian16(byte[] d, int o) => d[o] << 8 | d[o + 1];
        private static uint LittleEndian32(byte[] d, int o) => (uint)(d[o] | d[o + 1] << 8 | d[o + 2] << 16 | d[o + 3] << 24);
        private static int LittleEndian24(byte[] d, int o) => d[o] | d[o + 1] << 8 | d[o + 2] << 16;

        private static void WriteBigEndian32(byte[] d, int o, uint v) { d[o] = (byte)(v >> 24); d[o + 1] = (byte)(v >> 16); d[o + 2] = (byte)(v >> 8); d[o + 3] = (byte)v; }
        private static void WriteLittleEndian24(byte[] d, int o, int v) { d[o] = (byte)v; d[o + 1] = (byte)(v >> 8); d[o + 2] = (byte)(v >> 16); }
    }
}
