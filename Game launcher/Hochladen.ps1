# DFP Pro Launcher - alles mit einem Doppelklick auf GitHub hochladen
# Ablauf: Testbau -> Version bestimmen -> Text abfragen -> speichern (commit) -> hochladen (push) -> Version anlegen (tag)
# GitHub baut danach selbst den Installer und veroeffentlicht das Update.

$ErrorActionPreference = 'Stop'
Set-Location -Path $PSScriptRoot
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8

function Fail($message) {
    Write-Host ''
    Write-Host "FEHLER: $message" -ForegroundColor Red
    exit 1
}
function Step($message) {
    Write-Host ''
    Write-Host "== $message" -ForegroundColor Cyan
}

Write-Host 'DFP Pro Launcher - Auf GitHub hochladen' -ForegroundColor Magenta

# Git suchen: Visual Studio bringt ein eigenes Git mit, das nicht im Windows-Suchpfad steht
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    $candidates = @(
        "$env:ProgramFiles\Git\cmd\git.exe",
        "${env:ProgramFiles(x86)}\Git\cmd\git.exe",
        "$env:LOCALAPPDATA\Programs\Git\cmd\git.exe"
    )
    foreach ($root in @($env:ProgramFiles, ${env:ProgramFiles(x86)})) {
        if ($root) {
            $pattern = Join-Path $root 'Microsoft Visual Studio\*\*\Common7\IDE\CommonExtensions\Microsoft\TeamFoundation\Team Explorer\Git\cmd\git.exe'
            $candidates += @(Get-ChildItem -Path $pattern -ErrorAction SilentlyContinue | ForEach-Object { $_.FullName })
        }
    }
    $found = $candidates | Where-Object { $_ -and (Test-Path $_) } | Select-Object -First 1
    if ($found) {
        $env:PATH = (Split-Path $found) + ';' + $env:PATH
        Write-Host "Git gefunden: $found" -ForegroundColor DarkGray
    }
}
if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Fail 'Git wurde nicht gefunden. Installiere "Git for Windows" (https://git-scm.com) und starte das Skript noch einmal.'
}
git rev-parse --is-inside-work-tree 2>$null | Out-Null
if ($LASTEXITCODE -ne 0) {
    Fail 'Dieser Ordner ist kein Git-Projekt. Lege Hochladen.cmd und Hochladen.ps1 in den Hauptordner deines Projekts (dort, wo die .sln-Datei liegt).'
}

# 1) Testbau, damit kein kaputter Stand veroeffentlicht wird
$project = Get-ChildItem -Path $PSScriptRoot -Recurse -Filter '*.csproj' |
    Where-Object { $_.FullName -notmatch '\\(bin|obj)\\' } | Select-Object -First 1

if ($project -and (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    Step 'Teste, ob sich das Projekt fehlerfrei bauen laesst'
    dotnet build $project.FullName -c Release --nologo -v quiet
    if ($LASTEXITCODE -ne 0) {
        Fail 'Das Projekt laesst sich nicht bauen. Behebe die Fehler in Visual Studio und starte das Hochladen danach noch einmal.'
    }
    Write-Host 'Der Bau ist fehlerfrei.' -ForegroundColor Green
} else {
    Write-Host 'Hinweis: Der Testbau wurde uebersprungen (dotnet wurde nicht gefunden).' -ForegroundColor Yellow
}

# 2) Version bestimmen
Step 'Version bestimmen'
git fetch --tags --quiet 2>$null
$latest = git tag --list 'v*' --sort=-v:refname | Select-Object -First 1
if ($latest -match '^v(\d+)\.(\d+)\.(\d+)$') {
    $next = 'v{0}.{1}.{2}' -f $Matches[1], $Matches[2], ([int]$Matches[3] + 1)
} else {
    $next = 'v1.0.1'
}
Write-Host "Letzte Version auf GitHub: $latest"
$answer = Read-Host "Neue Version [$next] (Enter = uebernehmen)"
if ($answer) {
    $next = $answer.Trim()
    if ($next -notmatch '^v') { $next = "v$next" }
}
if (git tag --list $next) { Fail "Die Version $next gibt es schon. Waehle eine andere Nummer." }

# 3) Text fuer das Update-Fenster
Step 'Update-Text'
Write-Host 'Dieser Text erscheint spaeter im Update-Fenster der Nutzer.'
$message = Read-Host 'Was ist neu?'
if ([string]::IsNullOrWhiteSpace($message)) { Fail 'Ohne Text gibt es kein Update.' }

# 4) Uebersicht und Bestaetigung
Step 'Das wird hochgeladen'
git status --short
Write-Host ''
$confirm = Read-Host "Jetzt als $next hochladen? (J/N)"
if ($confirm -notmatch '^[JjYy]') {
    Write-Host 'Abgebrochen. Es wurde nichts veraendert.'
    exit 0
}

# 5) Speichern, hochladen, Version anlegen
Step 'Aenderungen speichern'
git add -A
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) {
    git commit -m $message
    if ($LASTEXITCODE -ne 0) { Fail 'Das Speichern (commit) ist fehlgeschlagen.' }
} else {
    Write-Host 'Keine neuen Aenderungen. Es wird nur die neue Version angelegt.'
}

Step 'Auf GitHub hochladen'
$branch = git branch --show-current
if (-not $branch) { Fail 'Es ist kein Zweig ausgewaehlt. Wechsle in Visual Studio auf deinen Zweig (zum Beispiel main) und starte neu.' }
Write-Host "Zweig: $branch"
# HEAD in den gleichnamigen Zweig auf GitHub schieben und die Verknuepfung dabei richtig setzen
git push -u origin HEAD
if ($LASTEXITCODE -ne 0) {
    Fail 'Das Hochladen (push) ist fehlgeschlagen. Steht oben "rejected", liegt auf GitHub etwas Neueres: Fuehre in Visual Studio unter Git-Aenderungen einen Pull aus und starte neu. Sonst pruefe deine Anmeldung bei GitHub.'
}

Step "Version $next anlegen"
git tag $next
git push origin $next
if ($LASTEXITCODE -ne 0) { Fail 'Die Version konnte nicht hochgeladen werden.' }

# 6) Fertig
$remote = git remote get-url origin
$repo = ''
if ($remote -match 'github\.com[:/](.+?)(\.git)?$') { $repo = $Matches[1] }

Write-Host ''
Write-Host "Fertig! GitHub baut jetzt den Installer fuer $next (etwa 2 Minuten)." -ForegroundColor Green
if ($repo) {
    Write-Host "Fortschritt: https://github.com/$repo/actions"
    Write-Host "Download:    https://github.com/$repo/releases/tag/$next"
    Start-Process "https://github.com/$repo/actions"
}
