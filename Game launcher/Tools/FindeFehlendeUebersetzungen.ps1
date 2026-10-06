# DFP Pro Launcher - sucht sichtbare Texte ohne Eintrag in der Uebersetzungstabelle (LocData.Text)
# Prueft:
#   - MainWindow.xaml: Text=, Content=, ToolTip=, Header=, Title=
#   - MainWindow*.cs: deutsche Texte in Anfuehrungszeichen (ohne Kommentare); die Tabelle steht in Localization.cs
# Die Suche ist eine Schaetzung: Sie kann Texte melden, die nie sichtbar sind (zum Beispiel Werte in Vergleichen).
# Sie aendert nichts und gibt immer 0 zurueck.
#
# Aufruf: powershell -NoProfile -ExecutionPolicy Bypass -File Tools\FindeFehlendeUebersetzungen.ps1 [-Ausgabe <Datei>]

param(
    [string]$Projekt = (Split-Path $PSScriptRoot -Parent),
    [string]$Ausgabe = ''
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

# Die Tabelle liegt in Localization.cs (früher in MainWindow.xaml.cs); durchsucht werden alle MainWindow*.cs-Dateien
$tabDatei = Join-Path $Projekt 'Localization.cs'
if (-not (Test-Path -LiteralPath $tabDatei)) { $tabDatei = Join-Path $Projekt 'MainWindow.xaml.cs' }
$codeDateien = @(Get-ChildItem -Path $Projekt -Filter 'MainWindow*.cs' | Sort-Object Name)
$xamlDatei = Join-Path $Projekt 'MainWindow.xaml'
$cs = [System.IO.File]::ReadAllLines($tabDatei, [System.Text.Encoding]::UTF8)
$xaml = [System.IO.File]::ReadAllLines($xamlDatei, [System.Text.Encoding]::UTF8)

# ───────── Tabelle laden (wie Loc.Load im Launcher) ─────────
$tStart = -1; $tEnde = -1
for ($i = 0; $i -lt $cs.Count; $i++) {
    if ($tStart -lt 0) {
        if ($cs[$i] -match '\bclass\s+LocData\b') {
            for ($j = $i; $j -lt $i + 10; $j++) { if ($cs[$j] -match '"""\s*$') { $tStart = $j + 1; break } }
        }
    } elseif ($cs[$i] -match '^\s*""";') { $tEnde = $i; break }
}
if ($tStart -lt 0 -or $tEnde -lt 0) { Write-Host 'Die Uebersetzungstabelle wurde nicht gefunden.' -ForegroundColor Red; exit 0 }

function Normalize([string]$t) { return ([regex]::Replace($t, '\s+', ' ')).Trim() }

$exact = New-Object 'System.Collections.Generic.HashSet[string]' ([System.StringComparer]::Ordinal)
$templates = New-Object System.Collections.Generic.List[regex]
for ($i = $tStart; $i -lt $tEnde; $i++) {
    $line = $cs[$i].Trim()
    if ($line.Length -eq 0 -or $line.StartsWith('#')) { continue }
    $parts = $line -split ' \|\| '
    if ($parts.Count -lt 2) { continue }
    $key = Normalize ($parts[0].Replace('\n', "`n"))
    if ($key.Contains('{')) {
        $sb = New-Object System.Text.StringBuilder '^'
        $last = 0
        foreach ($m in [regex]::Matches($key, '\{(#?)(\d)\}')) {
            [void]$sb.Append([regex]::Escape($key.Substring($last, $m.Index - $last)))
            if ($m.Groups[1].Value -eq '#') { [void]$sb.Append('[\d.,:]+') } else { [void]$sb.Append('.+?') }
            $last = $m.Index + $m.Length
        }
        [void]$sb.Append([regex]::Escape($key.Substring($last))).Append('$')
        $templates.Add((New-Object regex ($sb.ToString(), 'Singleline, CultureInvariant')))
    } else {
        [void]$exact.Add($key)
    }
}

function Test-Uebersetzt([string]$text, [int]$tiefe = 0) {
    $n = (Normalize $text).Trim(' ', '·')
    if ($n.Length -eq 0 -or $tiefe -gt 3) { return $true }
    if ($exact.Contains($n)) { return $true }
    foreach ($t in $templates) { if ($t.IsMatch($n)) { return $true } }
    if ($n.Contains(' · ')) {
        foreach ($p in ($n -split ' · ')) { if ((Test-Sichtbar $p) -and -not (Test-Uebersetzt $p ($tiefe + 1))) { return $false } }
        return $true
    }
    return $false
}

# Grobe Schaetzung, ob ein Text deutscher Oberflaechentext ist
$ignorieren = @('Steam', 'Epic Games', 'GOG', 'Ubisoft Connect', 'EA App', 'Battle.net', 'Xbox', 'Discord', 'Spotify',
    'YouTube', 'Twitch', 'OBS', 'GitHub', 'Windows', 'DFP Pro Launcher', 'CPU', 'GPU', 'RAM', 'VRAM', 'FPS', 'OK', 'WebView2')
function Test-Sichtbar([string]$t) {
    $t = $t.Trim(' ', '·', "`t")
    if ($t.Length -lt 2) { return $false }
    if ($t -match '^Segoe UI|^[dMyHhmst.:\- ]+$') { return $false }       # Schriftarten, Datumsformate
    if ($t -match '^[-/][A-Za-z]') { return $false }                       # Befehlszeilen-Parameter
    if ($t -match 'Write-Host|\b(Get|Set|Remove|Clear)-[A-Z]\w+|ipconfig|netsh|\bsfc\b|DISM|chkdsk|\bping\b|^SELECT |document\.|<[a-z!/]|[a-z]\{|=>') { return $false }
    if ($t -notmatch '[A-Za-zÄÖÜäöüß]{2}') { return $false }
    if ($ignorieren -contains $t) { return $false }
    if ($t -match '^[a-z][A-Za-z0-9_]*$') { return $false }                 # Bezeichner wie "dashboard"
    if ($t -match '^[A-Z][a-z]+[A-Z]\w*$') { return $false }                # PascalCase wie "SettingsCards"
    if ($t -match '^[\w.\-]+\.(exe|dll|json|png|jpg|gif|ico|txt|log|lnk|url|wav|mp3|cmd|ps1|xml|ini|db|vdf|acf|dat)$') { return $false }
    if ($t -match '\\|://|^#[0-9A-Fa-f]{3,8}$|^\{|^[A-Z_]{2,}$|^[\w\-]+=') { return $false }
    if ($t -match '^(?:[A-Za-z]+\.)+[A-Za-z]+$') { return $false }            # Namespaces, Dateinamen
    if ($t -match '^(?:[A-Z0-9][\w\-.]*)(?: [A-Z0-9][\w\-.]*)+$' -and $t -notmatch '[ÄÖÜäöüß]') { return $false }   # Namen wie "Google Chrome"
    if ($t -match '[a-zäöüß]' -and ($t -match '\s' -or $t -match '[ÄÖÜäöüß]' -or $t -match '^[A-ZÄÖÜ][a-zäöüß]{2,}$')) { return $true }
    return $false
}

$treffer = New-Object System.Collections.Generic.List[object]
function Melde($datei, $nr, $text, $art) {
    $treffer.Add([pscustomobject]@{ Datei = $datei; Zeile = $nr; Art = $art; Text = $text })
}

# ───────── XAML ─────────
for ($i = 0; $i -lt $xaml.Count; $i++) {
    foreach ($m in [regex]::Matches($xaml[$i], '\b(Text|Content|ToolTip|Header|Title)="([^"]*)"')) {
        $wert = [System.Net.WebUtility]::HtmlDecode($m.Groups[2].Value)
        if ($wert.StartsWith('{')) { continue }
        if (-not (Test-Sichtbar $wert)) { continue }
        if (-not (Test-Uebersetzt $wert)) {
            $art = if ($m.Groups[1].Value -in 'Header', 'Title') { "$($m.Groups[1].Value) (wird nicht automatisch uebersetzt)" } else { $m.Groups[1].Value }
            Melde 'MainWindow.xaml' ($i + 1) $wert $art
        }
    }
}

# ───────── C# ─────────
foreach ($codeDatei in $codeDateien) {
$codeZeilen = [System.IO.File]::ReadAllLines($codeDatei.FullName, [System.Text.Encoding]::UTF8)
$istTabelle = $codeDatei.FullName -eq (Resolve-Path $tabDatei).Path
$imDebug = $false
for ($i = 0; $i -lt $codeZeilen.Count; $i++) {
    if ($istTabelle -and $i -ge $tStart - 1 -and $i -le $tEnde) { continue }          # die Tabelle selbst
    $zeile = $codeZeilen[$i]
    $code = $zeile.Trim()
    if ($code.StartsWith('//') -or $code.StartsWith('///') -or $code.StartsWith('[')) { continue }
    if ($code -match '^#if DEBUG') { $imDebug = $true } elseif ($code -match '^#endif') { $imDebug = $false }
    # Zeilenkommentar am Ende abschneiden (grob, nur wenn kein Anfuehrungszeichen danach kommt)
    $ohneKommentar = [regex]::Replace($zeile, '//[^"]*$', '')
    foreach ($m in [regex]::Matches($ohneKommentar, '(\$@|@\$|\$|@)?"((?:[^"\\]|\\.)*)"')) {
        $prefix = $m.Groups[1].Value
        $wert = $m.Groups[2].Value
        if ($prefix.Contains('@')) { continue }                        # wortgetreue Texte sind fast immer Pfade/Regex
        $vorher = $ohneKommentar.Substring(0, $m.Index)
        if ($ohneKommentar -match 'FontFamily|PerformanceCounter|ManagementObjectSearcher|ExecuteScriptAsync|Arguments\s*=|ArgumentList') { continue }
        if ($vorher -match '(Regex\.\w+\(|GetValue\(|SetValue\(|OpenSubKey\(|CreateSubKey\(|nameof\(|DllImport|EntryPoint|GetProperty\(|TryGetProperty\(|GetJsonString\([^,]*,\s*|JsonLong\([^,]*,\s*|FindName\(|Tag\s*==\s*|case\s+|\bis\s+|==\s*|!=\s*|StartsWith\(|EndsWith\(|Contains\(|Equals\()\s*$') { continue }
        $wert = $wert.Replace('\n', "`n").Replace('\"', '"').Replace('\\', '\')
        $pruef = $wert
        if ($prefix.Contains('$')) {
            # Platzhalter im interpolierten Text: einmal als Zahl, einmal als Text probieren
            if ($wert.Contains('{') -and -not [regex]::IsMatch($wert, '\{[^{}]+\}')) { continue }   # Bruchstueck mit Anfuehrungszeichen im Platzhalter
            $nurText = [regex]::Replace($wert, '\{[^{}]+\}', ' ')
            if (-not (Test-Sichtbar $nurText)) { continue }
            $mitZahl = [regex]::Replace($wert, '\{[^{}]+\}', '1')
            $mitText = [regex]::Replace($wert, '\{[^{}]+\}', 'Xy')
            if ((Test-Uebersetzt $mitZahl) -or (Test-Uebersetzt $mitText)) { continue }
            $pruef = $wert
        } else {
            if (-not (Test-Sichtbar $wert)) { continue }
            if (Test-Uebersetzt $wert) { continue }
        }
        $art = if ($imDebug) { 'Code (nur Entwickler-Bau)' } else { 'Code' }
        Melde $codeDatei.Name ($i + 1) $pruef $art
    }
}
}

# ───────── Ausgabe ─────────
$text = New-Object System.Text.StringBuilder
[void]$text.AppendLine("Moegliche Texte ohne Uebersetzung: $($treffer.Count)")
foreach ($gruppe in ($treffer | Group-Object Art | Sort-Object Name)) {
    [void]$text.AppendLine('')
    [void]$text.AppendLine("== $($gruppe.Name): $($gruppe.Count)")
    foreach ($t in $gruppe.Group) {
        $kurz = $t.Text.Replace("`n", '\n')
        [void]$text.AppendLine(("  {0}:{1}  {2}" -f $t.Datei, $t.Zeile, $kurz))
    }
}

if ($Ausgabe) {
    [System.IO.File]::WriteAllText($Ausgabe, $text.ToString(), (New-Object System.Text.UTF8Encoding($true)))
    Write-Host "Liste gespeichert: $Ausgabe ($($treffer.Count) Eintraege)"
} else {
    Write-Host $text.ToString()
}
exit 0
