using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ═════════════════════════════ „Läuft das auf meinem PC?“ und Update-Größe ═════════════════════════════
        //
        // Anforderungen kommen aus dem Steam-Shop (pc_requirements), der eigene PC aus Registry/GC.
        // Prozessoren und Grafikkarten werden über eine grobe Leistungszahl verglichen (GTX 1060 ≈ 100, i5-4460 ≈ 100).
        // Unbekannte Modelle zeigen ehrlich "?" statt zu raten.

        private sealed class RequirementRow
        {
            public string Label = string.Empty;
            public string Minimum = string.Empty;
            public string Recommended = string.Empty;
            public string Mine = string.Empty;
            public int State = -1;   // 2 = Empfohlen erfüllt, 1 = Minimum erfüllt, 0 = darunter, -1 = unbekannt
        }

        private sealed class SpecSheet
        {
            public string Cpu = string.Empty, Gpu = string.Empty, Ram = string.Empty;
        }

        private sealed class StoreDetails
        {
            public SpecSheet Min = new(), Rec = new();
            public bool HasRequirements;
            public readonly HashSet<int> Categories = new();
            public int Achievements;
            public string Languages = string.Empty;   // HTML-Liste aus dem Shop, * = mit Sprachausgabe
        }

        private static readonly Dictionary<string, StoreDetails?> storeDetailsCache = new();
        private static (string Cpu, string Gpu, double RamGb)? myPcSpecs;

        // ───────── Eigener PC ─────────

        private static (string Cpu, string Gpu, double RamGb) MyPcSpecs()
        {
            if (myPcSpecs is { } cached) return cached;

            string cpu = string.Empty;
            try
            {
                cpu = (Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", null) as string ?? string.Empty).Trim();
                cpu = Regex.Replace(cpu, @"\s+", " ");
            }
            catch { }

            // Alle Grafikkarten aus der Geräteklasse "Display"; die stärkste zählt (dedizierte vor integrierter)
            string gpu = string.Empty;
            try
            {
                using var root = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
                if (root != null)
                {
                    double best = -1;
                    foreach (string sub in root.GetSubKeyNames())
                    {
                        if (!Regex.IsMatch(sub, @"^\d{4}$")) continue;
                        try
                        {
                            using var key = root.OpenSubKey(sub);
                            string name = key?.GetValue("DriverDesc") as string ?? string.Empty;
                            if (name.Length == 0 || name.Contains("Basic", StringComparison.OrdinalIgnoreCase) || name.Contains("Virtual", StringComparison.OrdinalIgnoreCase)) continue;
                            double score = GpuScores(name).DefaultIfEmpty(0).Max();
                            if (score > best)
                            {
                                best = score;
                                gpu = name;
                            }
                        }
                        catch { }
                    }
                }
            }
            catch { }

            double ram = 0;
            try { ram = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / 1024.0 / 1024 / 1024; } catch { }

            myPcSpecs = (cpu, gpu, ram);
            return myPcSpecs.Value;
        }

        // ───────── Leistungszahlen ─────────

        private static readonly (Regex Pattern, int Score)[] GpuPatterns = BuildGpuTable()
            .Select(e => (new Regex(@"(?<![A-Z0-9])" + Regex.Escape(e.Key) + @"(?![A-Z0-9])", RegexOptions.Compiled), e.Score))
            .ToArray();

        private static (string Key, int Score)[] BuildGpuTable()
        {
            const string data =
                "RTX 5090:600,RTX 5080:430,RTX 5070 TI:370,RTX 5070:300,RTX 5060 TI:230,RTX 5060:200,RTX 5050:150," +
                "RTX 4090:520,RTX 4080:400,RTX 4070 TI:320,RTX 4070:270,RTX 4060 TI:200,RTX 4060:170," +
                "RTX 3090 TI:350,RTX 3090:330,RTX 3080 TI:320,RTX 3080:300,RTX 3070 TI:245,RTX 3070:230,RTX 3060 TI:200,RTX 3060:155,RTX 3050:110," +
                "RTX 2080 TI:240,RTX 2080:200,RTX 2070:175,RTX 2060:145," +
                "GTX 1660 TI:125,GTX 1660:115,GTX 1650:85,GTX 1080 TI:180,GTX 1080:150,GTX 1070 TI:140,GTX 1070:125,GTX 1060:100,GTX 1050 TI:65,GTX 1050:50," +
                "GTX 980 TI:125,GTX 980:95,GTX 970:85,GTX 960:60,GTX 950:50,GTX 780 TI:70,GTX 780:60,GTX 770:50,GTX 760:40,GTX 750 TI:35,GTX 750:30," +
                "GTX 680:45,GTX 670:40,GTX 660 TI:36,GTX 660:32,GTX 650 TI:25,GTX 650:20,GTX 580:30,GTX 570:26,GTX 560:20,GTX 460:15," +
                "RX 9070 XT:420,RX 9070:370,RX 9060 XT:230,RX 7900 XTX:450,RX 7900 XT:390,RX 7900 GRE:330,RX 7800 XT:300,RX 7700 XT:250,RX 7600 XT:180,RX 7600:170," +
                "RX 6950 XT:330,RX 6900 XT:310,RX 6800 XT:290,RX 6800:250,RX 6750 XT:210,RX 6700 XT:200,RX 6700:185,RX 6650 XT:165,RX 6600 XT:160,RX 6600:140,RX 6500 XT:70,RX 6400:55," +
                "RX 5700 XT:175,RX 5700:155,RX 5600 XT:135,RX 5500 XT:85,RX VEGA 64:135,RX VEGA 56:120,RX 590:95,RX 580:90,RX 570:80,RX 480:85,RX 470:75,RX 560:45,RX 460:40,RX 550:30," +
                "R9 FURY:100,R9 390X:80,R9 390:75,R9 290X:70,R9 290:65,R9 380X:55,R9 380:50,R9 285:48,R9 280X:50,R9 280:45,R9 270X:42,R9 270:40,R7 370:30,R7 265:30,R7 260X:26," +
                "HD 7970:45,HD 7950:40,HD 7870:33,HD 7850:28,HD 7790:24,HD 7770:18,HD 6970:22,HD 6870:18,HD 5870:16," +
                "ARC B580:180,ARC B570:160,ARC A770:170,ARC A750:150,ARC A580:130,ARC A380:55,RADEON 780M:60,RADEON 680M:50,IRIS XE:22,UHD GRAPHICS:12,HD GRAPHICS:8";

            return data.Split(',')
                .Select(e => e.Split(':'))
                .Select(p => (p[0], int.Parse(p[1])))
                .OrderByDescending(p => p.Item1.Length)
                .ToArray();
        }

        private static string NormalizeHardware(string text)
        {
            string t = " " + text.ToUpperInvariant().Replace("®", " ").Replace("™", " ").Replace("(R)", " ").Replace("(TM)", " ") + " ";
            t = Regex.Replace(t, @"GEFORCE|RADEON(?!\s*\d{3}M)|NVIDIA|AMD|INTEL", " ");
            t = Regex.Replace(t, @"\b(GTX|RTX|RX|R9|R7|HD|ARC)\s*-?\s*([A-Z]?\d)", "$1 $2");
            t = Regex.Replace(t, @"(\d)\s*-?\s*(TI|XTX|XT|GRE)\b", "$1 $2");
            t = Regex.Replace(t, @"\b(\d{3,4})TI\b", "$1 TI");
            return Regex.Replace(t, @"\s+", " ");
        }

        private static List<double> GpuScores(string text)
        {
            var scores = new List<double>();
            string t = NormalizeHardware(text);
            foreach (var (pattern, score) in GpuPatterns)
            {
                // Längere Namen zuerst; Treffer werden überschrieben, damit "RTX 4070 TI" nicht zusätzlich als "RTX 4070" zählt
                t = pattern.Replace(t, _ =>
                {
                    scores.Add(score);
                    return " # ";
                });
            }
            return scores;
        }

        private static List<double> CpuScores(string text)
        {
            var scores = new List<double>();
            string t = text.ToUpperInvariant().Replace("®", " ").Replace("™", " ").Replace("(R)", " ").Replace("(TM)", " ");
            double Tier(int digit) => digit switch { 3 => 70, 5 => 100, 7 => 125, 9 => 150, _ => 90 };

            foreach (Match m in Regex.Matches(t, @"\bI([3579])\s*-?\s*(\d{4,5})([A-Z]*)"))
            {
                string number = m.Groups[2].Value;
                int gen = number.Length == 5 ? int.Parse(number.Substring(0, 2)) : number[0] - '0';
                double factor = gen switch
                {
                    <= 1 => 0.6, 2 => 0.75, 3 => 0.82, 4 => 0.9, 5 => 0.92, 6 => 1.0, 7 => 1.05, 8 => 1.25, 9 => 1.3,
                    10 => 1.4, 11 => 1.55, 12 => 1.9, 13 => 2.1, _ => 2.2
                };
                scores.Add(Tier(m.Groups[1].Value[0] - '0') * factor);
            }

            foreach (Match m in Regex.Matches(t, @"ULTRA\s*([579])\s*(\d{3})"))
                scores.Add(Tier(m.Groups[1].Value[0] - '0') * 2.2);

            foreach (Match m in Regex.Matches(t, @"RYZEN\s*(?:THREADRIPPER\s*)?([3579])\s*-?\s*(?:PRO\s*)?(\d{4})([A-Z0-9]*)"))
            {
                int gen = m.Groups[2].Value[0] - '0';
                double factor = gen switch { 1 => 1.05, 2 => 1.15, 3 => 1.4, 4 => 1.4, 5 => 1.75, 6 => 1.75, 7 => 2.1, 8 => 2.1, _ => 2.4 };
                if (m.Groups[3].Value.Contains("X3D")) factor *= 1.2;
                scores.Add(Tier(m.Groups[1].Value[0] - '0') * factor);
            }

            if (Regex.IsMatch(t, @"\bFX\s*-?\s*\d{4}")) scores.Add(70);
            if (Regex.IsMatch(t, @"PHENOM|ATHLON\s*II|CORE\s*2|CORE2")) scores.Add(45);
            return scores;
        }

        // ───────── Anforderungen aus dem Steam-Shop ─────────

        private static SpecSheet ParseRequirementHtml(string html)
        {
            var sheet = new SpecSheet();
            foreach (string part in Regex.Split(html, @"<li>|<br\s*/?>", RegexOptions.IgnoreCase))
            {
                string line = StripHtml(part);
                var m = Regex.Match(line, @"^\s*(Processor|CPU|Memory|Graphics|Video Card|Video)\s*:\s*(.+)$", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                string value = m.Groups[2].Value.Trim();
                switch (m.Groups[1].Value.ToLowerInvariant())
                {
                    case "processor":
                    case "cpu":
                        if (sheet.Cpu.Length == 0) sheet.Cpu = value;
                        break;
                    case "memory":
                        if (sheet.Ram.Length == 0) sheet.Ram = value;
                        break;
                    default:
                        if (sheet.Gpu.Length == 0) sheet.Gpu = value;
                        break;
                }
            }
            return sheet;
        }

        private static double? ParseRamGb(string text)
        {
            var m = Regex.Match(text, @"(\d+(?:[.,]\d+)?)\s*(GB|MB|G)\b", RegexOptions.IgnoreCase);
            if (!m.Success) return null;
            double value = double.Parse(m.Groups[1].Value.Replace(',', '.'), System.Globalization.CultureInfo.InvariantCulture);
            return m.Groups[2].Value.Equals("MB", StringComparison.OrdinalIgnoreCase) ? value / 1024 : value;
        }

        private async Task<string> StoreAppIdAsync(GameItem game)
            => game.Source == "Steam" && game.AppId.Length > 0 ? game.AppId : await FindSteamAppIdAsync(game.Name);

        /// <summary>Eine Abfrage im Steam-Shop für Anforderungen, Eigenschaften, Erfolge und Sprachen (pro Sitzung gemerkt).</summary>
        private async Task<StoreDetails?> FetchStoreDetailsAsync(GameItem game)
        {
            string appId = await StoreAppIdAsync(game);
            if (appId.Length == 0) return null;

            lock (storeDetailsCache)
            {
                if (storeDetailsCache.TryGetValue(appId, out var cached)) return cached;
            }

            StoreDetails? result = null;
            string json = await Http.GetStringAsync($"https://store.steampowered.com/api/appdetails?appids={appId}&l=english");
            using (var doc = JsonDocument.Parse(json))
            {
                if (TryPath(doc.RootElement, out var data, appId, "data") && data.ValueKind == JsonValueKind.Object)
                {
                    result = new StoreDetails();
                    if (data.TryGetProperty("pc_requirements", out var req) && req.ValueKind == JsonValueKind.Object)
                    {
                        result.Min = ParseRequirementHtml(GetJsonString(req, "minimum"));
                        result.Rec = ParseRequirementHtml(GetJsonString(req, "recommended"));
                        result.HasRequirements = result.Min.Cpu.Length + result.Min.Gpu.Length + result.Min.Ram.Length
                            + result.Rec.Cpu.Length + result.Rec.Gpu.Length + result.Rec.Ram.Length > 0;
                    }
                    if (data.TryGetProperty("categories", out var categories) && categories.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var category in categories.EnumerateArray())
                            if (category.TryGetProperty("id", out var id) && id.TryGetInt32(out int number)) result.Categories.Add(number);
                    }
                    if (TryPath(data, out var total, "achievements", "total") && total.TryGetInt32(out int achievements)) result.Achievements = achievements;
                    result.Languages = GetJsonString(data, "supported_languages");
                }
            }

            lock (storeDetailsCache) storeDetailsCache[appId] = result;
            return result;
        }

        private async Task<(SpecSheet Min, SpecSheet Rec, string Source)?> FetchRequirementsAsync(GameItem game)
        {
            try
            {
                var details = await FetchStoreDetailsAsync(game);
                if (details is { HasRequirements: true }) return (details.Min, details.Rec, "Steam");
            }
            catch { }

            // Kein Steam-Spiel oder keine Angaben im Shop (z.B. Ubisoft, EA, Battle.net): PCGamingWiki fragen
            var wiki = await FetchWikiRequirementsAsync(game.Name);
            return wiki != null ? (wiki.Value.Min, wiki.Value.Rec, "PCGamingWiki") : null;
        }

        // ───────── Ersatzquelle PCGamingWiki (freies Wiki, Vorlage {{System requirements}}) ─────────

        private static readonly Dictionary<string, (SpecSheet Min, SpecSheet Rec)?> wikiRequirementCache = new(StringComparer.OrdinalIgnoreCase);
        private const string WikiApi = "https://www.pcgamingwiki.com/w/api.php";

        private async Task<(SpecSheet Min, SpecSheet Rec)?> FetchWikiRequirementsAsync(string name)
        {
            lock (wikiRequirementCache)
            {
                if (wikiRequirementCache.TryGetValue(name, out var cached)) return cached;
            }

            (SpecSheet, SpecSheet)? result = null;
            string cleaned = CleanStoreTitle(name);
            if (cleaned.Length == 0) cleaned = name;

            string title = string.Empty;
            using (var doc = JsonDocument.Parse(await Http.GetStringAsync(
                $"{WikiApi}?action=query&list=search&srsearch={Uri.EscapeDataString(cleaned)}&srlimit=8&format=json")))
            {
                if (TryPath(doc.RootElement, out var hits, "query", "search") && hits.ValueKind == JsonValueKind.Array)
                {
                    string wanted = NormalizeTitle(cleaned);
                    string full = NormalizeTitle(name);
                    string loose = string.Empty;
                    foreach (var hit in hits.EnumerateArray())
                    {
                        string candidate = GetJsonString(hit, "title");
                        string normal = NormalizeTitle(CleanStoreTitle(candidate));
                        if (normal == wanted || normal == full)
                        {
                            title = candidate;
                            break;
                        }
                        // z.B. "Rainbow Six Siege" ↔ "Tom Clancy's Rainbow Six Siege": einer enthält den anderen
                        if (loose.Length == 0 && wanted.Length >= 6 && (normal.EndsWith(wanted) || wanted.EndsWith(normal) && normal.Length >= 6))
                            loose = candidate;
                    }
                    if (title.Length == 0) title = loose;
                }
            }

            if (title.Length > 0)
            {
                using var page = JsonDocument.Parse(await Http.GetStringAsync(
                    $"{WikiApi}?action=parse&page={Uri.EscapeDataString(title)}&prop=wikitext&format=json&redirects=1"));
                if (TryPath(page.RootElement, out var text, "parse", "wikitext", "*"))
                    result = ParseWikiRequirements(text.GetString() ?? string.Empty);
            }

            lock (wikiRequirementCache) wikiRequirementCache[name] = result;
            return result;
        }

        private static (SpecSheet Min, SpecSheet Rec)? ParseWikiRequirements(string wikitext)
        {
            // Es kann mehrere Blöcke geben (Windows, macOS, Linux) – nur Windows zählt
            foreach (Match block in Regex.Matches(wikitext, @"\{\{System requirements(.*?)\n\}\}", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string body = block.Groups[1].Value;
                if (!Regex.IsMatch(body, @"\|\s*OSfamily\s*=\s*Windows", RegexOptions.IgnoreCase)) continue;

                var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (Match field in Regex.Matches(body, @"^[ \t]*\|[ \t]*(\w+)[ \t]*=[ \t]*(.*)$", RegexOptions.Multiline))
                {
                    string value = CleanWikiValue(field.Groups[2].Value);
                    if (value.Length > 0) fields[field.Groups[1].Value] = value;
                }

                string Join(string prefix) => string.Join(" / ",
                    new[] { "", "2", "3", "4" }.Select(n => fields.TryGetValue(prefix + n, out var v) ? v : null).Where(v => v != null));

                var min = new SpecSheet { Cpu = Join("minCPU"), Gpu = Join("minGPU"), Ram = fields.TryGetValue("minRAM", out var minRam) ? minRam : string.Empty };
                var rec = new SpecSheet { Cpu = Join("recCPU"), Gpu = Join("recGPU"), Ram = fields.TryGetValue("recRAM", out var recRam) ? recRam : string.Empty };
                if (min.Cpu.Length + min.Gpu.Length + min.Ram.Length + rec.Cpu.Length + rec.Gpu.Length + rec.Ram.Length > 0)
                    return (min, rec);
            }
            return null;
        }

        private static string CleanWikiValue(string value)
        {
            value = Regex.Replace(value, @"<ref[^>]*/>|<ref[^>]*>.*?</ref>|<ref[^>]*>.*$", string.Empty, RegexOptions.IgnoreCase);
            value = Regex.Replace(value, @"\{\{[^{}]*\}\}", string.Empty);
            value = Regex.Replace(value, @"\[\[(?:[^|\]]*\|)?([^\]]*)\]\]", "$1");
            value = Regex.Replace(value, "<.*?>", " ");
            return Regex.Replace(System.Net.WebUtility.HtmlDecode(value), @"\s+", " ").Trim();
        }

        private static RequirementRow CompareRow(string label, string min, string rec, string mine, Func<string, List<double>> scorer)
        {
            var row = new RequirementRow { Label = label, Minimum = min, Recommended = rec, Mine = mine };
            double my = scorer(mine).DefaultIfEmpty(0).Max();
            var minScores = scorer(min);
            var recScores = scorer(rec);
            if (my <= 0 || (minScores.Count == 0 && recScores.Count == 0)) return row;

            // Mehrere Modelle in einer Zeile ("GTX 1060 / RX 580") = jedes davon reicht → das schwächste zählt
            if (recScores.Count > 0 && my >= recScores.Min() * 0.95) row.State = 2;
            else if (minScores.Count > 0) row.State = my >= minScores.Min() * 0.95 ? 1 : 0;
            else row.State = 0;   // nur "Empfohlen" bekannt und nicht erreicht: Minimum unklar → vorsichtig
            if (row.State == 0 && minScores.Count == 0) row.State = -1;
            return row;
        }

        private List<RequirementRow> CompareWithMyPc((SpecSheet Min, SpecSheet Rec) req)
        {
            var pc = MyPcSpecs();
            var rows = new List<RequirementRow>
            {
                CompareRow(Loc.T("Prozessor"), req.Min.Cpu, req.Rec.Cpu, pc.Cpu, CpuScores),
                CompareRow(Loc.T("Grafikkarte"), req.Min.Gpu, req.Rec.Gpu, pc.Gpu, GpuScores)
            };

            var ram = new RequirementRow
            {
                Label = Loc.T("Arbeitsspeicher"),
                Minimum = req.Min.Ram,
                Recommended = req.Rec.Ram,
                Mine = pc.RamGb > 0 ? Math.Round(pc.RamGb).ToString("0", Loc.Culture) + " GB" : string.Empty
            };
            double? minRam = ParseRamGb(req.Min.Ram), recRam = ParseRamGb(req.Rec.Ram);
            // Windows meldet etwas weniger als verbaut (reservierter Speicher) → 10 % Spielraum
            if (pc.RamGb > 0 && (minRam != null || recRam != null))
            {
                double mine = pc.RamGb * 1.1;
                ram.State = recRam != null && mine >= recRam ? 2 : minRam != null ? (mine >= minRam ? 1 : 0) : -1;
            }
            rows.Add(ram);

            return rows.Where(r => r.Minimum.Length > 0 || r.Recommended.Length > 0).ToList();
        }

        // ───────── Anzeige (beide Themen) ─────────

        /// <summary>Baut den Inhalt „Läuft das auf meinem PC?“; lädt im Hintergrund nach.</summary>
        private StackPanel BuildRequirementCheck(GameItem game)
        {
            var body = new StackPanel();
            var status = new TextBlock { Text = Loc.T("Anforderungen werden geprüft …"), Foreground = BrushSubtle, FontSize = 12.5, TextWrapping = TextWrapping.Wrap };
            body.Children.Add(status);

            if (!settings.OnlineFeatures)
            {
                status.Text = Loc.T("Online-Funktionen sind in den Einstellungen deaktiviert.");
                return body;
            }

            _ = FillRequirementCheckAsync(game, body, status);
            return body;
        }

        private async Task FillRequirementCheckAsync(GameItem game, StackPanel body, TextBlock status)
        {
            (SpecSheet Min, SpecSheet Rec, string Source)? req = null;
            try { req = await FetchRequirementsAsync(game); }
            catch
            {
                status.Text = Loc.T("Steam-Shop nicht erreichbar. Später noch einmal öffnen.");
                return;
            }

            if (req == null)
            {
                status.Text = Loc.T("Weder im Steam-Shop noch bei PCGamingWiki stehen Anforderungen für dieses Spiel.");
                return;
            }

            var sheets = (req.Value.Min, req.Value.Rec);
            var rows = await Task.Run(() => CompareWithMyPc(sheets));
            body.Children.Clear();
            if (rows.Count == 0)
            {
                body.Children.Add(new TextBlock { Text = Loc.T("Für dieses Spiel stehen im Steam-Shop keine Anforderungen."), Foreground = BrushSubtle, FontSize = 12.5 });
                return;
            }

            var known = rows.Where(r => r.State >= 0).ToList();
            string verdict;
            string verdictColor;
            if (known.Count == 0) { verdict = Loc.T("Dein PC konnte nicht sicher eingeordnet werden."); verdictColor = "#9CA3AF"; }
            else if (known.Any(r => r.State == 0)) { verdict = Loc.T("Dein PC liegt teilweise unter dem Minimum."); verdictColor = "#F87171"; }
            else if (known.All(r => r.State == 2)) { verdict = Loc.T("Dein PC schafft die empfohlenen Einstellungen."); verdictColor = "#34D399"; }
            else { verdict = Loc.T("Dein PC reicht mindestens für das Minimum."); verdictColor = "#FBBF24"; }

            body.Children.Add(new TextBlock { Text = verdict, Foreground = MakeBrush(verdictColor), FontWeight = FontWeights.SemiBold, FontSize = 14, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });

            bool hidePc = SHide(settings.StreamHidePc);
            foreach (var row in rows)
            {
                var (icon, color, text) = row.State switch
                {
                    2 => ("✔", "#34D399", Loc.T("Empfohlen erreicht")),
                    1 => ("✔", "#FBBF24", Loc.T("Minimum erreicht")),
                    0 => ("⚠", "#F87171", Loc.T("Unter dem Minimum")),
                    _ => ("?", "#9CA3AF", Loc.T("Nicht sicher erkannt"))
                };

                var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.Children.Add(new TextBlock { Text = icon, Foreground = MakeBrush(color), FontSize = 15, FontWeight = FontWeights.Bold });

                var texts = new StackPanel();
                Grid.SetColumn(texts, 1);
                texts.Children.Add(new TextBlock
                {
                    Text = row.Label + "  ·  " + text,
                    Foreground = System.Windows.Media.Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });

                var details = new List<string>();
                if (row.Minimum.Length > 0) details.Add(Loc.T("Minimum:") + " " + row.Minimum);
                if (row.Recommended.Length > 0) details.Add(Loc.T("Empfohlen:") + " " + row.Recommended);
                if (!hidePc && row.Mine.Length > 0) details.Add(Loc.T("Dein PC:") + " " + row.Mine);
                texts.Children.Add(new TextBlock { Text = string.Join("\n", details), Foreground = BrushSubtle, FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 2, 0, 0) });
                grid.Children.Add(texts);
                body.Children.Add(grid);
            }

            body.Children.Add(new TextBlock
            {
                Text = req.Value.Source == "Steam" ? Loc.T("Grobe Schätzung nach den Angaben im Steam-Shop.") : Loc.T("Grobe Schätzung nach den Angaben bei PCGamingWiki."),
                Foreground = BrushSubtle,
                FontSize = 11,
                Opacity = 0.8
            });
        }

        // ───────── Update-Größe (Steam) ─────────

        /// <summary>Liest den aktuellen Stand direkt aus der appmanifest-Datei; null = kein Update offen.</summary>
        private static string? SteamUpdateText(GameItem game)
        {
            if (game.Source != "Steam" || game.AppId.Length == 0 || game.InstallDir.Length == 0) return null;

            try
            {
                string? library = Directory.GetParent(Directory.GetParent(game.InstallDir.TrimEnd('\\', '/'))?.FullName ?? string.Empty)?.FullName;
                if (library == null) return null;
                string acf = Path.Combine(library, $"appmanifest_{game.AppId}.acf");
                if (!File.Exists(acf)) return null;

                string text = File.ReadAllText(acf);
                long flags = ReadAcfNumber(text, "StateFlags");
                long build = ReadAcfNumber(text, "buildid");
                long target = ReadAcfNumber(text, "TargetBuildID");
                bool pending = (flags & (2 | 512 | 1024)) != 0 || (target > 0 && target != build);
                if (!pending) return null;

                long toDownload = ReadAcfNumber(text, "BytesToDownload");
                long downloaded = ReadAcfNumber(text, "BytesDownloaded");

                // Die Byte-Angaben gehören nur dann zum offenen Update, wenn Steam schon eine neue Zielversion kennt
                if (target > 0 && target != build && toDownload > 0)
                {
                    string size = Loc.T("Update ausstehend:") + " " + FormatBytes(toDownload);
                    if (downloaded > 0 && downloaded < toDownload)
                        size += "  (" + Loc.T("noch") + " " + FormatBytes(toDownload - downloaded) + ")";
                    return size;
                }

                return Loc.T("Update ausstehend (Steam nennt die Größe erst, wenn der Download beginnt)");
            }
            catch
            {
                return null;
            }
        }
    }
}
