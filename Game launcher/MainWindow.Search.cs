using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Game_launcher
{
    public partial class MainWindow
    {
        // ───────────────────────────── Schlaue Suche (Spiele-Seite und Spotlight) ─────────────────────────────
        //
        // - ®, ™, Doppelpunkte, Bindestriche und Groß-/Kleinschreibung spielen keine Rolle („witcher 3“ findet „The Witcher® 3: Wild Hunt“)
        // - Leerzeichen sind egal („farcry“ findet „Far Cry 6“)
        // - Abkürzungen aus den Anfangsbuchstaben („gta“, „rdr2“, „ets2“)
        // - kleine Tippfehler werden verziehen („cyberpnuk“)
        // - Filter direkt in der Suche: steam:, xbox:, epic: … (jeder Launcher), fav:, neu:, ungespielt:, backlog:, spiele:, durchgespielt:

        private sealed class SearchKey
        {
            public string Text = string.Empty;       // normalisiert, mit Leerzeichen
            public string Compact = string.Empty;    // ohne Leerzeichen
            public string Acronym = string.Empty;    // Anfangsbuchstaben, Zahlen ganz („gtav“, „rdr2“)
            public string[] Words = Array.Empty<string>();
        }

        private sealed class ParsedSearch
        {
            public string Text = string.Empty;
            public string Compact = string.Empty;
            public List<string> Tokens = new();
            public List<Func<GameItem, bool>> Filters = new();
        }

        private readonly Dictionary<string, SearchKey> searchKeys = new(StringComparer.Ordinal);

        /// <summary>Kleinbuchstaben, Akzente weg, alles außer Buchstaben und Ziffern wird zu Leerzeichen.</summary>
        private static string NormalizeSearch(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            string decomposed = text.ToLowerInvariant().Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder(decomposed.Length);
            bool space = true;
            foreach (char c in decomposed)
            {
                var category = System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c);
                if (category == System.Globalization.UnicodeCategory.NonSpacingMark) continue;   // é → e
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(c);
                    space = false;
                }
                else if (!space && c != '\'' && c != '’')   // „Assassin's“ → „assassins“
                {
                    builder.Append(' ');
                    space = true;
                }
            }
            return builder.ToString().Trim().Replace("ß", "ss");
        }

        private SearchKey GetSearchKey(string name)
        {
            if (searchKeys.TryGetValue(name, out var cached)) return cached;

            string text = NormalizeSearch(name);
            var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var acronym = new StringBuilder();
            foreach (string word in words)
            {
                if (word.All(char.IsDigit)) acronym.Append(word);   // „Euro Truck Simulator 2“ → „ets2“
                else acronym.Append(word[0]);
            }

            var key = new SearchKey { Text = text, Compact = text.Replace(" ", string.Empty), Acronym = acronym.ToString(), Words = words };
            searchKeys[name] = key;
            return key;
        }

        /// <summary>Zerlegt die Eingabe in Filter („steam:“, „fav:“ …) und Suchwörter.</summary>
        private ParsedSearch ParseSearch(string input)
        {
            var parsed = new ParsedSearch();
            var words = new List<string>();
            // Auch bekannte Launcher ohne Spiele, damit „ea:“ dann „keine Treffer“ zeigt statt nach „ea“ im Namen zu suchen
            var sources = allGames.Select(g => g.Source).Where(s => !string.IsNullOrEmpty(s))
                .Concat(KnownLaunchers).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            foreach (string raw in input.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (raw.Length > 1 && raw.EndsWith(':'))
                {
                    var filter = SearchFilter(raw.TrimEnd(':'), sources);
                    if (filter != null)
                    {
                        parsed.Filters.Add(filter);
                        continue;
                    }
                }
                words.Add(raw);
            }

            parsed.Text = NormalizeSearch(string.Join(" ", words));
            parsed.Compact = parsed.Text.Replace(" ", string.Empty);
            parsed.Tokens = parsed.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
            return parsed;
        }

        private Func<GameItem, bool>? SearchFilter(string name, List<string> sources)
        {
            string key = NormalizeSearch(name).Replace(" ", string.Empty);
            if (key.Length == 0) return null;

            switch (key)
            {
                case "fav": case "favorit": case "favoriten": case "favorite": case "favorites":
                    return g => g.IsFavorite;
                case "neu": case "new":
                    return IsNewGame;
                case "ungespielt": case "unplayed": case "nie":
                    return g => g.PlaySeconds < 60 && g.LaunchCount == 0;
                case "backlog":
                    return g => g.Status == "backlog";
                case "spiele": case "playing": case "aktiv":
                    return g => g.Status == "playing";
                case "durchgespielt": case "done": case "fertig":
                    return g => g.Status == "done";
            }

            // Launcher: erst genau („ea:“), sonst Anfang („epic:“, „battle:“)
            string? source = sources.FirstOrDefault(s => NormalizeSearch(s).Replace(" ", string.Empty) == key)
                             ?? sources.FirstOrDefault(s => NormalizeSearch(s).Replace(" ", string.Empty).StartsWith(key, StringComparison.Ordinal));
            if (source != null) return g => string.Equals(g.Source, source, StringComparison.OrdinalIgnoreCase);
            return null;
        }

        /// <summary>Wie gut passt das Spiel? -1 = gar nicht; höher = besser (für die Reihenfolge der Treffer).</summary>
        private int GameSearchScore(GameItem game, ParsedSearch search)
        {
            foreach (var filter in search.Filters)
                if (!filter(game)) return -1;
            if (search.Compact.Length == 0) return 50;   // nur Filter eingegeben

            var key = GetSearchKey(game.Name);

            if (key.Text.StartsWith(search.Text, StringComparison.Ordinal) || key.Compact.StartsWith(search.Compact, StringComparison.Ordinal)) return 100;
            if (key.Acronym == search.Compact && search.Compact.Length >= 2) return 95;
            if (key.Words.Any(w => w.StartsWith(search.Tokens[0], StringComparison.Ordinal)) && key.Text.Contains(search.Text, StringComparison.Ordinal)) return 85;
            if (key.Acronym.StartsWith(search.Compact, StringComparison.Ordinal) && search.Compact.Length >= 2) return 80;
            if (key.Text.Contains(search.Text, StringComparison.Ordinal) || key.Compact.Contains(search.Compact, StringComparison.Ordinal)) return 70;

            // Jedes Wort für sich: im Namen, als Abkürzung oder mit kleinem Tippfehler
            bool fuzzy = false;
            foreach (string token in search.Tokens)
            {
                if (key.Compact.Contains(token, StringComparison.Ordinal)) continue;
                if (token.Length >= 2 && key.Acronym.StartsWith(token, StringComparison.Ordinal)) continue;

                int allowed = token.Length >= 7 ? 2 : token.Length >= 4 ? 1 : 0;
                if (allowed > 0 && key.Words.Any(w => CloseEnough(token, w, allowed)))
                {
                    fuzzy = true;
                    continue;
                }
                return -1;
            }
            return fuzzy ? 35 : 55;
        }

        /// <summary>Tippfehler-Abstand (vertauschte Buchstaben zählen als ein Fehler). Längere Wörter werden auch am Anfang verglichen („witchr“ ~ „witcher“).</summary>
        private static bool CloseEnough(string token, string word, int allowed)
        {
            if (Math.Abs(token.Length - word.Length) > allowed)
            {
                // Eingabe ist der Anfang eines langen Wortes: nur den Anfang vergleichen
                if (word.Length > token.Length) word = word.Substring(0, Math.Min(word.Length, token.Length + allowed));
                if (Math.Abs(token.Length - word.Length) > allowed) return false;
            }
            return EditDistance(token, word) <= allowed;
        }

        private static int EditDistance(string a, string b)
        {
            var d = new int[a.Length + 1, b.Length + 1];
            for (int i = 0; i <= a.Length; i++) d[i, 0] = i;
            for (int j = 0; j <= b.Length; j++) d[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1), d[i - 1, j - 1] + cost);
                    if (i > 1 && j > 1 && a[i - 1] == b[j - 2] && a[i - 2] == b[j - 1])
                        d[i, j] = Math.Min(d[i, j], d[i - 2, j - 2] + 1);
                }
            }
            return d[a.Length, b.Length];
        }
    }
}
