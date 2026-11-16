using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ═════════════════════════════ Spiel-Fakten: Steam-Bewertung mit Trend, Eigenschaften, Laufwerk ═════════════════════════════

        private sealed class ReviewSummary
        {
            public long Up, Down, RecentUp, RecentDown;
        }

        private static readonly Dictionary<string, ReviewSummary?> reviewCache = new();
        private static readonly Dictionary<string, (string Kind, bool Slow)?> driveKindCache = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Baut Bewertung und Info-Kärtchen; die Daten kommen im Hintergrund nach.</summary>
        private StackPanel BuildGameFacts(GameItem game)
        {
            var body = new StackPanel();
            var review = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 0, 0, 10) };
            var chips = new WrapPanel();
            var hint = new TextBlock { Visibility = Visibility.Collapsed, Foreground = MakeBrush("#FBBF24"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) };
            body.Children.Add(review);
            body.Children.Add(chips);
            body.Children.Add(hint);

            _ = FillDriveChipAsync(game, chips, hint);
            if (settings.OnlineFeatures)
            {
                _ = FillStoreChipsAsync(game, chips);
                _ = FillReviewAsync(game, review);
            }
            return body;
        }

        private Border FactChip(string text, string? color = null)
        {
            var chip = new Border
            {
                Background = MakeBrush("#1FFFFFFF"),
                BorderBrush = MakeBrush(color != null ? "#66" + color.TrimStart('#') : "#33FFFFFF"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(IsConsole ? 2 : 9),
                Padding = new Thickness(10, 4, 10, 4),
                Margin = new Thickness(0, 0, 8, 8),
                Child = new TextBlock { Text = text, FontSize = 12.5, Foreground = color != null ? MakeBrush(color) : MakeBrush("#E5E7EB") }
            };
            if (IsConsole) ((TextBlock)chip.Child).SetResourceReference(TextBlock.FontFamilyProperty, "DisplayFont");
            return chip;
        }

        // ───────── Eigenschaften aus dem Steam-Shop ─────────

        private async Task FillStoreChipsAsync(GameItem game, WrapPanel chips)
        {
            StoreDetails? details;
            try { details = await FetchStoreDetailsAsync(game); }
            catch { return; }
            if (details == null) return;

            var c = details.Categories;
            var list = new List<(string Text, string? Color)>();

            if (c.Contains(28)) list.Add(("🎮  " + Loc.T("Volle Controller-Unterstützung"), "#34D399"));
            else if (c.Contains(18)) list.Add(("🎮  " + Loc.T("Teilweise Controller-Unterstützung"), "#FBBF24"));

            if (c.Contains(39) || c.Contains(24)) list.Add(("🛋  " + Loc.T("Splitscreen"), null));
            if (c.Contains(38)) list.Add(("🤝  " + Loc.T("Koop online"), null));
            else if (c.Contains(9)) list.Add(("🤝  " + Loc.T("Koop"), null));
            if (c.Contains(36) || c.Contains(49)) list.Add(("⚔  " + Loc.T("Online-PvP"), null));
            if (c.Contains(27)) list.Add(("🔗  " + Loc.T("Crossplay"), null));
            if (c.Contains(2) && !c.Overlaps(new[] { 1, 9, 24, 27, 36, 38, 39, 44, 49 })) list.Add(("👤  " + Loc.T("Nur Einzelspieler"), null));
            if (c.Contains(44)) list.Add(("📡  " + Loc.T("Remote Play Together"), null));
            if (c.Contains(62)) list.Add(("👪  " + Loc.T("Familienfreigabe"), null));
            if (details.Achievements > 0) list.Add(("🏆  " + string.Format(Loc.T("{0} Erfolge"), details.Achievements.ToString(Loc.Culture)), null));
            if (c.Contains(23)) list.Add(("☁  " + Loc.T("Cloud-Speicher"), null));
            if (c.Contains(61)) list.Add(("☀  HDR", null));

            var language = LanguageSupport(details.Languages);
            if (language != null) list.Add(language.Value);

            foreach (var (text, color) in list) chips.Children.Add(FactChip(text, color));
        }

        private (string, string?)? LanguageSupport(string html)
        {
            if (html.Length == 0) return null;
            string steamName = ResolveLanguage(settings.Language) switch
            {
                "de" => "German",
                "zh" => "Simplified Chinese",
                "es" => "Spanish - Spain",
                "fr" => "French",
                "pt" => "Portuguese - Brazil",
                "ru" => "Russian",
                "ja" => "Japanese",
                _ => "English"
            };

            // Einträge: "German<strong>*</strong>" = mit Sprachausgabe
            foreach (string part in html.Split(','))
            {
                string clean = StripHtml(part.Replace("<strong>*</strong>", "*")).Replace("*", string.Empty).Trim();
                int br = clean.IndexOf("languages with full audio", StringComparison.OrdinalIgnoreCase);
                if (br >= 0) clean = clean.Substring(0, br).Trim();
                if (!clean.Equals(steamName, StringComparison.OrdinalIgnoreCase)) continue;

                return part.Contains('*')
                    ? ("💬  " + Loc.T("Deine Sprache: Text + Sprachausgabe"), "#34D399")
                    : ("💬  " + Loc.T("Deine Sprache: nur Text"), null);
            }
            return ("💬  " + Loc.T("Nicht in deiner Sprache"), "#FBBF24");
        }

        // ───────── Steam-Bewertung mit Trend ─────────

        private async Task<ReviewSummary?> FetchReviewsAsync(string appId)
        {
            lock (reviewCache)
            {
                if (reviewCache.TryGetValue(appId, out var cached)) return cached;
            }

            // Die alte Abfrage /appreviews leitet inzwischen auf die Startseite um; das Diagramm der Shop-Seite liefert
            // Monatswerte seit Erscheinen (rollups) und Tageswerte der letzten 30 Tage (recent)
            string json = await Http.GetStringAsync($"https://store.steampowered.com/appreviewhistogram/{appId}?l=english&review_score_preference=0");
            ReviewSummary? summary = null;
            using (var doc = JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Object)
                {
                    summary = new ReviewSummary();
                    if (results.TryGetProperty("rollups", out var rollups) && rollups.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in rollups.EnumerateArray())
                        {
                            summary.Up += JsonLong(item, "recommendations_up");
                            summary.Down += JsonLong(item, "recommendations_down");
                        }
                    }
                    if (results.TryGetProperty("recent", out var recent) && recent.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in recent.EnumerateArray())
                        {
                            summary.RecentUp += JsonLong(item, "recommendations_up");
                            summary.RecentDown += JsonLong(item, "recommendations_down");
                        }
                    }
                    if (summary.Up + summary.Down == 0) summary = null;
                }
            }

            lock (reviewCache) reviewCache[appId] = summary;
            return summary;
        }

        /// <summary>Einstufung wie im Steam-Shop.</summary>
        private static (string Label, string Color) ReviewLabel(long up, long down)
        {
            long total = up + down;
            if (total < 10) return ("Zu wenige Bewertungen", "#9CA3AF");
            double pct = up * 100.0 / total;
            if (pct >= 95 && total >= 500) return ("Äußerst positiv", "#34D399");
            if (pct >= 80) return (total >= 50 ? "Sehr positiv" : "Positiv", "#34D399");
            if (pct >= 70) return ("Größtenteils positiv", "#86EFAC");
            if (pct >= 40) return ("Ausgeglichen", "#FBBF24");
            if (pct >= 20) return ("Größtenteils negativ", "#F87171");
            return (total >= 500 ? "Äußerst negativ" : total >= 50 ? "Sehr negativ" : "Negativ", "#F87171");
        }

        private async Task FillReviewAsync(GameItem game, StackPanel host)
        {
            ReviewSummary? summary;
            try
            {
                string appId = await StoreAppIdAsync(game);
                if (appId.Length == 0) return;
                summary = await FetchReviewsAsync(appId);
            }
            catch { return; }
            if (summary == null) return;

            long total = summary.Up + summary.Down;
            double pct = summary.Up * 100.0 / total;
            var (label, color) = ReviewLabel(summary.Up, summary.Down);

            var line = new WrapPanel();
            line.Children.Add(new TextBlock { Text = "👍  " + Loc.T("Steam-Bewertung:") + " ", Foreground = BrushSubtle, FontSize = 13.5, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            line.Children.Add(new TextBlock { Text = Loc.T(label), Foreground = MakeBrush(color), FontWeight = FontWeights.SemiBold, FontSize = 13.5, VerticalAlignment = System.Windows.VerticalAlignment.Center });
            line.Children.Add(new TextBlock { Text = $"  ({pct.ToString("0", Loc.Culture)} %, {total.ToString("N0", Loc.Culture)})", Foreground = BrushSubtle, FontSize = 12.5, VerticalAlignment = System.Windows.VerticalAlignment.Center });

            long recentTotal = summary.RecentUp + summary.RecentDown;
            string? trend = null;
            string trendColor = "#9CA3AF";
            if (recentTotal >= 10)
            {
                double recentPct = summary.RecentUp * 100.0 / recentTotal;
                var (recentLabel, recentColor) = ReviewLabel(summary.RecentUp, summary.RecentDown);
                line.Children.Add(new TextBlock { Text = "   ·   " + Loc.T("Letzte 30 Tage:") + " ", Foreground = BrushSubtle, FontSize = 13.5, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                line.Children.Add(new TextBlock { Text = Loc.T(recentLabel), Foreground = MakeBrush(recentColor), FontWeight = FontWeights.SemiBold, FontSize = 13.5, VerticalAlignment = System.Windows.VerticalAlignment.Center });
                line.Children.Add(new TextBlock { Text = $"  ({recentPct.ToString("0", Loc.Culture)} %)", Foreground = BrushSubtle, FontSize = 12.5, VerticalAlignment = System.Windows.VerticalAlignment.Center });

                if (recentTotal >= 20 && recentPct <= pct - 10)
                {
                    trend = "📉  " + Loc.T("Die neuen Bewertungen sind deutlich schlechter. Vielleicht gibt es Probleme mit dem letzten Update.");
                    trendColor = "#F87171";
                }
                else if (recentTotal >= 20 && recentPct >= pct + 10)
                {
                    trend = "📈  " + Loc.T("Die neuen Bewertungen sind deutlich besser als früher.");
                    trendColor = "#34D399";
                }
            }

            host.Children.Add(line);
            if (trend != null)
                host.Children.Add(new TextBlock { Text = trend, Foreground = MakeBrush(trendColor), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
            host.Visibility = Visibility.Visible;
        }

        // ───────── Laufwerk: NVMe, SSD oder Festplatte ─────────

        private async Task FillDriveChipAsync(GameItem game, WrapPanel chips, TextBlock hint)
        {
            string folder = game.InstallDir.Length > 0 ? game.InstallDir : Path.GetDirectoryName(game.ExecutablePath) ?? string.Empty;
            if (folder.Length == 0) return;

            string root;
            try { root = Path.GetPathRoot(Path.GetFullPath(folder)) ?? string.Empty; }
            catch { return; }
            if (root.Length < 2 || root.StartsWith(@"\\", StringComparison.Ordinal)) return;

            var info = await Task.Run(() => DriveKind(root));
            if (info == null) return;

            string letter = root.TrimEnd('\\');
            string text = "💾  " + letter + "  " + Loc.T(info.Value.Kind);
            try
            {
                var drive = new DriveInfo(root);
                if (drive.IsReady) text += "  ·  " + string.Format(Loc.T("{0} frei"), FormatBytes(drive.AvailableFreeSpace));
            }
            catch { }

            chips.Children.Insert(0, FactChip(text, info.Value.Slow ? "#FBBF24" : null));
            if (info.Value.Slow)
            {
                hint.Text = "⚠  " + Loc.T("Das Spiel liegt auf einer Festplatte (HDD). Ladezeiten können lang sein, auf einer SSD läuft es flüssiger.");
                hint.Visibility = Visibility.Visible;
            }
        }

        private static (string Kind, bool Slow)? DriveKind(string root)
        {
            string letter = root.Substring(0, 2).ToUpperInvariant();
            lock (driveKindCache)
            {
                if (driveKindCache.TryGetValue(letter, out var cached)) return cached;
            }

            (string, bool)? result = null;
            try
            {
                using var handle = CreateFileW(@"\\.\" + letter, 0, 3, IntPtr.Zero, 3, 0, IntPtr.Zero);
                if (!handle.IsInvalid)
                {
                    int bus = QueryBusType(handle);
                    bool? seekPenalty = QuerySeekPenalty(handle);

                    if (bus == 7) result = ("USB-Laufwerk", seekPenalty == true);           // BusTypeUsb
                    else if (bus == 17) result = ("NVMe-SSD", false);                       // BusTypeNvme
                    else if (seekPenalty == true) result = ("Festplatte (HDD)", true);
                    else if (seekPenalty == false) result = ("SSD", false);
                }
            }
            catch { }

            lock (driveKindCache) driveKindCache[letter] = result;
            return result;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct StoragePropertyQuery
        {
            public int PropertyId;
            public int QueryType;
            public byte AdditionalParameters;
        }

        private const uint IoctlStorageQueryProperty = 0x002D1400;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFileW(string name, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle device, uint code, ref StoragePropertyQuery input, int inputSize,
            byte[] output, int outputSize, out int returned, IntPtr overlapped);

        private static byte[]? QueryStorageProperty(Microsoft.Win32.SafeHandles.SafeFileHandle handle, int propertyId, int size)
        {
            var query = new StoragePropertyQuery { PropertyId = propertyId, QueryType = 0 };
            var buffer = new byte[size];
            return DeviceIoControl(handle, IoctlStorageQueryProperty, ref query, Marshal.SizeOf<StoragePropertyQuery>(), buffer, buffer.Length, out int returned, IntPtr.Zero) && returned > 0
                ? buffer
                : null;
        }

        // STORAGE_DEVICE_DESCRIPTOR: BusType liegt an Byte 28
        private static int QueryBusType(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
            => QueryStorageProperty(handle, 0, 1024) is byte[] data && data.Length >= 32 ? BitConverter.ToInt32(data, 28) : -1;

        // DEVICE_SEEK_PENALTY_DESCRIPTOR: Version, Size, IncursSeekPenalty (Byte 8)
        private static bool? QuerySeekPenalty(Microsoft.Win32.SafeHandles.SafeFileHandle handle)
            => QueryStorageProperty(handle, 7, 12) is byte[] data ? data[8] != 0 : null;
    }
}
