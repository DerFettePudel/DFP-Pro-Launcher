# DFP Pro Launcher - Pruefung der Uebersetzungstabelle (LocData.Text in Localization.cs)
# Prueft pro Zeile:
#   - genau 8 Spalten (Deutsch || Englisch || Chinesisch || Spanisch || Franzoesisch || Portugiesisch || Russisch || Japanisch)
#   - keine leere Spalte
#   - gleiche Platzhalter in allen Sprachen ({#0} im Schluessel entspricht {0} in den Uebersetzungen)
#   - kein Schluessel doppelt
# Rueckgabewert: 0 = alles in Ordnung, 1 = Fehler gefunden, 2 = Tabelle nicht gefunden.
#
# Aufruf: powershell -NoProfile -ExecutionPolicy Bypass -File Tools\PruefeUebersetzung.ps1 [-Datei <Pfad>]

param(
    [string]$Datei = ''
)

# Standard: die Tabelle liegt in Localization.cs (früher in MainWindow.xaml.cs)
if (-not $Datei) {
    $projekt = Split-Path $PSScriptRoot -Parent
    $Datei = Join-Path $projekt 'Localization.cs'
    if (-not (Test-Path -LiteralPath $Datei)) { $Datei = Join-Path $projekt 'MainWindow.xaml.cs' }
}

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }

$sprachen = @('Deutsch', 'Englisch', 'Chinesisch', 'Spanisch', 'Franzoesisch', 'Portugiesisch', 'Russisch', 'Japanisch')
$spaltenSoll = $sprachen.Count

if (-not (Test-Path -LiteralPath $Datei)) {
    Write-Host "FEHLER: Die Datei wurde nicht gefunden: $Datei" -ForegroundColor Red
    exit 2
}

$zeilen = [System.IO.File]::ReadAllLines($Datei, [System.Text.Encoding]::UTF8)

# Anfang und Ende der Tabelle suchen: "internal static class LocData" -> Zeile mit """ -> Zeile mit """;
$start = -1
$ende = -1
for ($i = 0; $i -lt $zeilen.Count; $i++) {
    if ($start -lt 0) {
        if ($zeilen[$i] -match '\bclass\s+LocData\b') {
            for ($j = $i; $j -lt [Math]::Min($i + 10, $zeilen.Count); $j++) {
                if ($zeilen[$j] -match '"""\s*$') { $start = $j + 1; break }
            }
        }
    } elseif ($zeilen[$i] -match '^\s*""";') {
        $ende = $i
        break
    }
}

if ($start -lt 0 -or $ende -lt 0) {
    Write-Host 'FEHLER: Die Uebersetzungstabelle (LocData.Text) wurde in der Datei nicht gefunden.' -ForegroundColor Red
    exit 2
}

# Platzhalter einer Spalte als sortierte Liste ohne Doppelte, {#0} wird wie {0} behandelt
function Get-Platzhalter([string]$text) {
    $treffer = [regex]::Matches($text, '\{#?(\d+)\}') | ForEach-Object { '{' + $_.Groups[1].Value + '}' }
    return (@($treffer) | Sort-Object -Unique) -join ' '
}

$fehler = New-Object System.Collections.Generic.List[string]
# Gross-/Kleinschreibung zaehlt wie im Launcher (Ordinal), darum kein @{}
$schluessel = New-Object 'System.Collections.Generic.Dictionary[string,int]' ([System.StringComparer]::Ordinal)
$anzahl = 0

for ($i = $start; $i -lt $ende; $i++) {
    $nr = $i + 1                       # Zeilennummer wie in Visual Studio
    $zeile = $zeilen[$i].Trim()
    if ($zeile.Length -eq 0 -or $zeile.StartsWith('#')) { continue }
    $anzahl++

    $spalten = $zeile -split ' \|\| '

    if ($spalten.Count -ne $spaltenSoll) {
        $fehler.Add("Zeile ${nr}: $($spalten.Count) statt $spaltenSoll Spalten  ->  $zeile")
    }

    for ($s = 0; $s -lt $spalten.Count; $s++) {
        if ($spalten[$s].Trim().Length -eq 0) {
            $name = if ($s -lt $sprachen.Count) { $sprachen[$s] } else { "Spalte $($s + 1)" }
            $fehler.Add("Zeile ${nr}: Spalte $($s + 1) ($name) ist leer")
        }
    }

    # Platzhalter vergleichen
    $soll = Get-Platzhalter $spalten[0]
    for ($s = 1; $s -lt $spalten.Count; $s++) {
        $ist = Get-Platzhalter $spalten[$s]
        if ($ist -ne $soll) {
            $name = if ($s -lt $sprachen.Count) { $sprachen[$s] } else { "Spalte $($s + 1)" }
            $sollText = if ($soll) { $soll } else { 'keine' }
            $istText = if ($ist) { $ist } else { 'keine' }
            $fehler.Add("Zeile ${nr}: Platzhalter in $name passen nicht (Deutsch: $sollText, $name`: $istText)")
        }
    }

    # Doppelte Schluessel (so verglichen, wie der Launcher sie laedt: Leerraum zusammengefasst)
    $key = ([regex]::Replace($spalten[0].Replace('\n', "`n"), '\s+', ' ')).Trim()
    if ($schluessel.ContainsKey($key)) {
        $fehler.Add("Zeile ${nr}: Der Schluessel ist doppelt (zuerst in Zeile $($schluessel[$key])): $($spalten[0])")
    } else {
        $schluessel[$key] = $nr
    }
}

if ($fehler.Count -gt 0) {
    Write-Host ''
    Write-Host "Uebersetzungstabelle: $($fehler.Count) Fehler in $anzahl Eintraegen gefunden" -ForegroundColor Red
    foreach ($f in $fehler) { Write-Host "  $f" -ForegroundColor Yellow }
    Write-Host ''
    Write-Host "Bitte die genannten Zeilen in $(Split-Path $Datei -Leaf) (Klasse LocData) korrigieren." -ForegroundColor Red
    exit 1
}

Write-Host "Uebersetzungstabelle in Ordnung: $anzahl Eintraege, je $spaltenSoll Sprachen." -ForegroundColor Green
exit 0
