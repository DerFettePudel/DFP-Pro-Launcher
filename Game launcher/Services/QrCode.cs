using System;
using System.Collections.Generic;
using System.Text;

namespace Game_launcher
{
    /// <summary>
    /// Kleiner QR-Code-Erzeuger ohne Zusatzpaket: Byte-Modus, Fehlerkorrektur „M“, Versionen 1 bis 10 (bis 213 Bytes).
    /// Aufbau nach ISO/IEC 18004, angelehnt an den bekannten Algorithmus von Project Nayuki.
    /// </summary>
    internal sealed class QrCode
    {
        public int Size { get; }
        private readonly bool[,] modules;     // [y, x], true = dunkel
        private readonly bool[,] isFunction;

        public bool this[int x, int y] => modules[y, x];

        // Fehlerkorrektur „M“ für die Versionen 1 bis 10
        private static readonly int[] EccPerBlock = { 0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26 };
        private static readonly int[] BlockCount = { 0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5 };

        public static QrCode Encode(string text)
        {
            byte[] data = Encoding.UTF8.GetBytes(text);
            for (int version = 1; version <= 10; version++)
            {
                int capacityBits = DataCodewords(version) * 8;
                int countBits = version <= 9 ? 8 : 16;
                if (4 + countBits + data.Length * 8 <= capacityBits) return new QrCode(version, data);
            }
            throw new ArgumentException("Text zu lang für einen QR-Code");
        }

        private QrCode(int version, byte[] data)
        {
            Size = version * 4 + 17;
            modules = new bool[Size, Size];
            isFunction = new bool[Size, Size];

            DrawFunctionPatterns(version);
            byte[] codewords = AddEccAndInterleave(version, BuildDataCodewords(version, data));
            DrawCodewords(codewords);

            int bestMask = 0;
            long bestPenalty = long.MaxValue;
            for (int mask = 0; mask < 8; mask++)
            {
                ApplyMask(mask);
                DrawFormatBits(mask);
                long penalty = Penalty();
                if (penalty < bestPenalty) { bestPenalty = penalty; bestMask = mask; }
                ApplyMask(mask);   // XOR macht die Maske wieder rückgängig
            }
            ApplyMask(bestMask);
            DrawFormatBits(bestMask);
        }

        // ───────── Feste Muster ─────────

        private void Set(int x, int y, bool dark)
        {
            modules[y, x] = dark;
            isFunction[y, x] = true;
        }

        private void DrawFunctionPatterns(int version)
        {
            for (int i = 0; i < Size; i++)
            {
                Set(6, i, i % 2 == 0);
                Set(i, 6, i % 2 == 0);
            }

            DrawFinder(3, 3);
            DrawFinder(Size - 4, 3);
            DrawFinder(3, Size - 4);

            int[] positions = AlignmentPositions(version);
            int count = positions.Length;
            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    if ((i == 0 && j == 0) || (i == 0 && j == count - 1) || (i == count - 1 && j == 0)) continue;
                    for (int dy = -2; dy <= 2; dy++)
                        for (int dx = -2; dx <= 2; dx++)
                            Set(positions[i] + dx, positions[j] + dy, Math.Max(Math.Abs(dx), Math.Abs(dy)) != 1);
                }
            }

            DrawFormatBits(0);   // reserviert den Platz, die echten Bits kommen nach der Maskenwahl
            if (version >= 7)
            {
                int remainder = version;
                for (int i = 0; i < 12; i++) remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
                int bits = version << 12 | remainder;
                for (int i = 0; i < 18; i++)
                {
                    bool bit = ((bits >> i) & 1) != 0;
                    int a = Size - 11 + i % 3, b = i / 3;
                    Set(a, b, bit);
                    Set(b, a, bit);
                }
            }
        }

        private void DrawFinder(int x, int y)
        {
            for (int dy = -4; dy <= 4; dy++)
            {
                for (int dx = -4; dx <= 4; dx++)
                {
                    int distance = Math.Max(Math.Abs(dx), Math.Abs(dy));
                    int xx = x + dx, yy = y + dy;
                    if (xx >= 0 && xx < Size && yy >= 0 && yy < Size) Set(xx, yy, distance != 2 && distance != 4);
                }
            }
        }

        private int[] AlignmentPositions(int version)
        {
            if (version == 1) return Array.Empty<int>();
            int count = version / 7 + 2;
            int step = (int)Math.Ceiling((version * 4 + 4) / (double)(count * 2 - 2)) * 2;
            var result = new int[count];
            result[0] = 6;
            for (int i = count - 1, pos = Size - 7; i >= 1; i--, pos -= step) result[i] = pos;
            return result;
        }

        private void DrawFormatBits(int mask)
        {
            int data = 0 << 3 | mask;   // Fehlerkorrektur „M“ hat die Kennung 00
            int remainder = data;
            for (int i = 0; i < 10; i++) remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
            int bits = (data << 10 | remainder) ^ 0x5412;
            bool Bit(int i) => ((bits >> i) & 1) != 0;

            for (int i = 0; i <= 5; i++) Set(8, i, Bit(i));
            Set(8, 7, Bit(6));
            Set(8, 8, Bit(7));
            Set(7, 8, Bit(8));
            for (int i = 9; i < 15; i++) Set(14 - i, 8, Bit(i));

            for (int i = 0; i < 8; i++) Set(Size - 1 - i, 8, Bit(i));
            for (int i = 8; i < 15; i++) Set(8, Size - 15 + i, Bit(i));
            Set(8, Size - 8, true);
        }

        // ───────── Daten und Fehlerkorrektur ─────────

        private static int RawDataModules(int version)
        {
            int result = (16 * version + 128) * version + 64;
            if (version >= 2)
            {
                int count = version / 7 + 2;
                result -= (25 * count - 10) * count - 55;
                if (version >= 7) result -= 36;
            }
            return result;
        }

        private static int DataCodewords(int version) => RawDataModules(version) / 8 - EccPerBlock[version] * BlockCount[version];

        private static byte[] BuildDataCodewords(int version, byte[] data)
        {
            var bits = new List<bool>();
            void Append(int value, int length)
            {
                for (int i = length - 1; i >= 0; i--) bits.Add(((value >> i) & 1) != 0);
            }

            Append(0b0100, 4);
            Append(data.Length, version <= 9 ? 8 : 16);
            foreach (byte b in data) Append(b, 8);

            int capacity = DataCodewords(version) * 8;
            Append(0, Math.Min(4, capacity - bits.Count));
            Append(0, (8 - bits.Count % 8) % 8);
            for (int pad = 0xEC; bits.Count < capacity; pad ^= 0xEC ^ 0x11) Append(pad, 8);

            var result = new byte[bits.Count / 8];
            for (int i = 0; i < bits.Count; i++)
                if (bits[i]) result[i >> 3] |= (byte)(0x80 >> (i & 7));
            return result;
        }

        private static byte[] AddEccAndInterleave(int version, byte[] data)
        {
            int blocks = BlockCount[version], eccLength = EccPerBlock[version];
            int rawCodewords = RawDataModules(version) / 8;
            int shortBlocks = blocks - rawCodewords % blocks;
            int shortLength = rawCodewords / blocks;

            byte[] divisor = ReedSolomonDivisor(eccLength);
            var all = new List<byte[]>();
            for (int i = 0, k = 0; i < blocks; i++)
            {
                int length = shortLength - eccLength + (i < shortBlocks ? 0 : 1);
                var block = new byte[shortLength + 1];
                Array.Copy(data, k, block, 0, length);
                k += length;

                byte[] ecc = ReedSolomonRemainder(block, length, divisor);
                // Kurze Blöcke bekommen eine Lücke, damit alle gleich lang sind (wird beim Verschränken übersprungen)
                Array.Copy(ecc, 0, block, shortLength + 1 - eccLength, eccLength);
                all.Add(block);
            }

            var result = new List<byte>();
            for (int i = 0; i < shortLength + 1; i++)
            {
                for (int j = 0; j < blocks; j++)
                {
                    if (i != shortLength - eccLength || j >= shortBlocks) result.Add(all[j][i]);
                }
            }
            return result.ToArray();
        }

        private static byte[] ReedSolomonDivisor(int degree)
        {
            var result = new byte[degree];
            result[degree - 1] = 1;
            int root = 1;
            for (int i = 0; i < degree; i++)
            {
                for (int j = 0; j < degree; j++)
                {
                    result[j] = Multiply(result[j], root);
                    if (j + 1 < degree) result[j] ^= result[j + 1];
                }
                root = Multiply(root, 0x02);
            }
            return result;
        }

        private static byte[] ReedSolomonRemainder(byte[] data, int length, byte[] divisor)
        {
            var result = new byte[divisor.Length];
            for (int n = 0; n < length; n++)
            {
                int factor = data[n] ^ result[0];
                Array.Copy(result, 1, result, 0, result.Length - 1);
                result[^1] = 0;
                for (int i = 0; i < result.Length; i++) result[i] ^= Multiply(divisor[i], factor);
            }
            return result;
        }

        private static byte Multiply(int x, int y)
        {
            int z = 0;
            for (int i = 7; i >= 0; i--)
            {
                z = (z << 1) ^ ((z >> 7) * 0x11D);
                z ^= ((y >> i) & 1) * x;
            }
            return (byte)z;
        }

        private void DrawCodewords(byte[] data)
        {
            int i = 0;
            for (int right = Size - 1; right >= 1; right -= 2)
            {
                if (right == 6) right = 5;
                for (int vertical = 0; vertical < Size; vertical++)
                {
                    for (int j = 0; j < 2; j++)
                    {
                        int x = right - j;
                        bool upward = ((right + 1) & 2) == 0;
                        int y = upward ? Size - 1 - vertical : vertical;
                        if (isFunction[y, x] || i >= data.Length * 8) continue;
                        modules[y, x] = ((data[i >> 3] >> (7 - (i & 7))) & 1) != 0;
                        i++;
                    }
                }
            }
        }

        // ───────── Maske ─────────

        private void ApplyMask(int mask)
        {
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    if (isFunction[y, x]) continue;
                    bool invert = mask switch
                    {
                        0 => (x + y) % 2 == 0,
                        1 => y % 2 == 0,
                        2 => x % 3 == 0,
                        3 => (x + y) % 3 == 0,
                        4 => (x / 3 + y / 2) % 2 == 0,
                        5 => x * y % 2 + x * y % 3 == 0,
                        6 => (x * y % 2 + x * y % 3) % 2 == 0,
                        _ => ((x + y) % 2 + x * y % 3) % 2 == 0
                    };
                    if (invert) modules[y, x] = !modules[y, x];
                }
            }
        }

        /// <summary>Vereinfachte Bewertung nach der Norm: lange gleiche Reihen, 2×2-Blöcke, Finder-ähnliche Muster, Hell-Dunkel-Verhältnis.</summary>
        private long Penalty()
        {
            long result = 0;
            for (int pass = 0; pass < 2; pass++)
            {
                for (int a = 0; a < Size; a++)
                {
                    int run = 0;
                    bool last = false;
                    int history = 0;
                    for (int b = 0; b < Size; b++)
                    {
                        bool dark = pass == 0 ? modules[a, b] : modules[b, a];
                        if (b > 0 && dark == last) run++;
                        else
                        {
                            if (run >= 5) result += run - 2;
                            run = 1;
                            last = dark;
                        }

                        history = ((history << 1) | (dark ? 1 : 0)) & 0x7FF;
                        if (b >= 10 && (history == 0b10111010000 || history == 0b00001011101)) result += 40;
                    }
                    if (run >= 5) result += run - 2;
                }
            }

            int darkCount = 0;
            for (int y = 0; y < Size; y++)
            {
                for (int x = 0; x < Size; x++)
                {
                    if (modules[y, x]) darkCount++;
                    if (x < Size - 1 && y < Size - 1)
                    {
                        bool c = modules[y, x];
                        if (c == modules[y, x + 1] && c == modules[y + 1, x] && c == modules[y + 1, x + 1]) result += 3;
                    }
                }
            }

            int total = Size * Size;
            int k = (int)Math.Ceiling(Math.Abs(darkCount * 20L - total * 10L) / (double)total) - 1;
            result += Math.Max(0, k) * 10;
            return result;
        }
    }
}
