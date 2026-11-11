using System.Security.Cryptography;
using System.Text;
using System.Windows;

namespace Game_launcher
{
    // ───────────────────────────── Name und Untertitel gesperrt (Freischaltung per Code) ─────────────────────────────
    // Name und Untertitel heißen immer „DFP PRO“ / „LAUNCHER“. Mit dem Code des Entwicklers (Suche: „/code …“) lässt sich das
    // Ändern wieder freischalten. Im Programm steht nur ein Fingerabdruck des Codes, nicht der Code selbst.
    // Gespeichert wird ein abgeleiteter Schlüssel; ein einfaches „true“ in der Einstellungsdatei schaltet nichts frei.

    public partial class MainWindow
    {
        private const string DefaultBrandName = "DFP PRO";
        private const string DefaultBrandSubtitle = "LAUNCHER";

        /// <summary>SHA-256 von „dfp-brand|“ + Code.</summary>
        private const string BrandCodeHash = "e05d72892470bfcb84b97e699b0c836e4d8f40f06d17599a57d3a1be188a3b06";
        /// <summary>SHA-256 des gespeicherten Schlüssels (der Schlüssel ist SHA-256 von „dfp-brand-unlock|“ + Code).</summary>
        private const string BrandUnlockCheck = "0e980468c8551cbd895a252de92b41ae69f082363f4dcc2a2b3ff4a84e44842b";

        private static string Sha256Hex(string text)
            => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();

        private static string NormalizeCode(string code) => code.Trim().ToUpperInvariant();

        private bool BrandTextUnlocked
            => !string.IsNullOrEmpty(settings.BrandUnlockKey) && Sha256Hex(settings.BrandUnlockKey) == BrandUnlockCheck;

        /// <summary>Ohne Freischaltung immer die Originalnamen (setzt auch früher geänderte Namen zurück).</summary>
        private void EnforceBrandLock()
        {
            if (BrandTextUnlocked) return;
            settings.BrandName = DefaultBrandName;
            settings.BrandSubtitle = DefaultBrandSubtitle;
        }

        /// <summary>Suchbefehl „/code …“. Erscheint nur bei genau diesem Befehl, damit niemand zufällig darauf stößt.</summary>
        private bool AddCodeCommand(string query, List<SpotlightEntry> entries)
        {
            string[] parts = query.Substring(1).Trim().Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !parts[0].Equals("code", StringComparison.OrdinalIgnoreCase)) return false;

            string code = parts[1];
            entries.Add(new SpotlightEntry
            {
                Icon = "🔑",
                Title = Loc.T("Code einlösen"),
                Subtitle = Loc.T("Mit Enter bestätigen"),
                Action = () => RedeemCode(code),
                Score = 100
            });
            return true;
        }

        private void RedeemCode(string code)
        {
            code = NormalizeCode(code);
            if (Sha256Hex("dfp-brand|" + code) != BrandCodeHash)
            {
                ShowToast("🔑", Loc.T("Code ungültig"), Loc.T("Dieser Code passt nicht."), 5);
                return;
            }

            if (BrandTextUnlocked)
            {
                ShowToast("🔑", Loc.T("Schon freigeschaltet"), Loc.T("Name und Untertitel kannst du bereits ändern."), 5);
                return;
            }

            settings.BrandUnlockKey = Sha256Hex("dfp-brand-unlock|" + code);
            SaveSettings();
            UpdateBrandLockUi();
            ShowToast("🔓", Loc.T("Freigeschaltet"), Loc.T("Name und Untertitel kannst du jetzt unter Einstellungen → Logo und Name ändern."), 8);
        }

        /// <summary>Felder für eigenen Namen nur mit Freischaltung bedienbar.</summary>
        private void UpdateBrandLockUi()
        {
            if (TxtBrandName == null) return;

            bool unlocked = BrandTextUnlocked;
            TxtBrandName.IsEnabled = unlocked;
            TxtBrandSub.IsEnabled = unlocked;
            BtnApplyBrandText.IsEnabled = unlocked;
            TxtBrandTextHint.Text = Loc.T(unlocked
                ? "Ändere den Namen und den Untertitel neben dem Logo."
                : "Name und Untertitel sind fest: „DFP PRO“ und „LAUNCHER“.");
            TxtBrandName.Text = settings.BrandName;
            TxtBrandSub.Text = settings.BrandSubtitle;
        }

#if DEBUG
        /// <summary>Entwickler: Freischaltung zurücknehmen (Namen werden wieder auf das Original gesetzt).</summary>
        private void DevRelockBrand()
        {
            settings.BrandUnlockKey = string.Empty;
            EnforceBrandLock();
            SaveSettings();
            UpdateBrandLockUi();
            ApplyBrand();
            ShowToast("🔒", "Gesperrt", "Name und Untertitel sind wieder fest.", 5);
        }
#endif
    }
}
