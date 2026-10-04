# Lokal-bauen.ps1 - baut den DFP Pro Launcher und den Installer auf diesem PC
# Aufruf (im Lösungsordner):
#   powershell -ExecutionPolicy Bypass -File .\Lokal-bauen.ps1 -Version 1.0.0 -Repo DEINNAME/DFP-Pro-Launcher
# Ohne -Repo entsteht eine Testversion, die keine Updates sucht.

param(
    [string]$Version = "1.0.0",
    [string]$Repo = ""
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

$project = (Get-ChildItem -Recurse -Filter *.csproj | Select-Object -First 1).FullName
if (-not $project) { throw "Es wurde keine .csproj-Datei gefunden." }

Write-Host ""
Write-Host "Schritt 1 von 4: Programm wird gebaut (das dauert ein bis zwei Minuten) ..." -ForegroundColor Cyan
if (Test-Path "publish") { Remove-Item "publish" -Recurse -Force }

$publishArgs = @(
    "publish", $project,
    "-c", "Release",
    "-r", "win-x64",
    "--self-contained", "true",
    "-p:PublishSingleFile=true",
    "-p:IncludeNativeLibrariesForSelfExtract=true",
    "-p:Version=$Version",
    "-o", "publish"
)
if ($Repo) { $publishArgs += "-p:UpdateRepository=$Repo" }

& dotnet @publishArgs
if ($LASTEXITCODE -ne 0) { throw "Das Bauen ist fehlgeschlagen. Die Fehlermeldung steht weiter oben." }

$exe = Join-Path (Get-Location) "publish\DFPProLauncher.exe"
if (-not (Test-Path $exe)) { throw "publish\DFPProLauncher.exe wurde nicht erzeugt." }

Write-Host ""
Write-Host "Schritt 2 von 4: Symbol für den Installer bereitstellen ..." -ForegroundColor Cyan
$icon = Get-ChildItem -Recurse -Filter "DFP_Pro_Launcher.ico" | Where-Object { $_.FullName -notlike "*\installer\*" } | Select-Object -First 1
if (-not $icon) { throw "DFP_Pro_Launcher.ico wurde nicht gefunden." }
Copy-Item $icon.FullName "installer\DFP_Pro_Launcher.ico" -Force

Write-Host ""
Write-Host "Schritt 3 von 4: Installer wird gebaut ..." -ForegroundColor Cyan
$candidates = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
$iscc = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $iscc) {
    Write-Host "Inno Setup 6 ist nicht installiert. Bitte von jrsoftware.org installieren und das Skript erneut starten." -ForegroundColor Yellow
    Write-Host "Das Programm selbst liegt trotzdem fertig unter: $exe" -ForegroundColor Yellow
    exit 1
}
& $iscc "/DMyAppVersion=$Version" "installer\setup.iss"
if ($LASTEXITCODE -ne 0) { throw "Der Installer konnte nicht gebaut werden." }

Write-Host ""
Write-Host "Schritt 4 von 4: Prüfsumme erstellen ..." -ForegroundColor Cyan
$installer = Join-Path (Get-Location) "installer\Output\DFP_Pro_Launcher_Setup.exe"
$hash = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLower()
Set-Content -NoNewline -Path "$installer.sha256" -Value $hash

Write-Host ""
Write-Host "Fertig!" -ForegroundColor Green
Write-Host "  Programm (ohne Installation):  $exe"
Write-Host "  Installer:                     $installer"
Write-Host "  Prüfsumme:                     $installer.sha256"
