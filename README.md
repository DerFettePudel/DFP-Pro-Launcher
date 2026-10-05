# DFP Pro Launcher

Ein anpassbarer Spiele-Launcher für Windows von **Der_Fette_Pudel**. Er sammelt Spiele aus Steam, Epic, GOG, EA, Ubisoft, Battle.net, Rockstar und Xbox an einem Ort, zählt die Spielzeit, lädt Cover und vieles mehr.

## Installieren
1. Öffne den Bereich **Releases** dieses Repositorys.
2. Lade `DFP_Pro_Launcher_Setup.exe` herunter und starte sie.
3. Danach meldet sich der Launcher selbst, sobald es eine neue Version gibt. Das Update wird direkt im Launcher heruntergeladen und installiert.

Windows 10 oder neuer, 64 Bit. .NET muss nicht installiert werden.

## Selbst bauen
- Visual Studio 2022 mit der Arbeitsauslastung **.NET-Desktopentwicklung**
- Projektmappe öffnen, F5 drücken
- Lokale Testversionen suchen keine Updates

## Lokal bauen und testen (ohne GitHub)
Voraussetzung: .NET 8 SDK (kommt mit Visual Studio) und [Inno Setup 6](https://jrsoftware.org/isdl.php).

Im Lösungsordner in einem PowerShell-Fenster:
```
powershell -ExecutionPolicy Bypass -File .\Lokal-bauen.ps1 -Version 1.0.0
```
Ergebnis:
- `publish\DFPProLauncher.exe` - das Programm als Einzeldatei, ohne Installation lauffähig
- `installer\Output\DFP_Pro_Launcher_Setup.exe` - der Installer

Eine so gebaute Testversion sucht keine Updates. Mit `-Repo BENUTZER/REPOSITORY` bekommt sie die Update-Quelle eingebaut.

## Neue Version veröffentlichen (für den Entwickler)
1. Änderungen committen und pushen. Die Commit-Nachrichten erscheinen später im Update-Fenster der Nutzer, schreibe sie also verständlich.
2. Tag anlegen und hochladen, zum Beispiel:
   ```
   git tag v1.0.1
   git push origin v1.0.1
   ```
3. GitHub baut daraufhin automatisch den Installer und veröffentlicht ihn als Release (Reiter **Actions** zeigt den Fortschritt, etwa 5 bis 8 Minuten).
4. Nutzer sehen beim nächsten Start oder spätestens nach ein paar Stunden das Update-Fenster.

Die Versionsnummer kommt aus dem Tag. In der Projektdatei musst du nichts ändern.

## Hinweise
- Das Repository muss **öffentlich** sein, damit der Launcher Updates ohne Anmeldung laden kann.
- Der Launcher prüft jede heruntergeladene Datei mit einer SHA-256-Prüfsumme, bevor er sie installiert.
- Eingebundene Pakete: LibreHardwareMonitorLib (MPL 2.0), SixLabors.ImageSharp (Six Labors Split License), System.Management.
