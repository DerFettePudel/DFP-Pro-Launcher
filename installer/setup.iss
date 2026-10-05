; Installer-Skript fuer den DFP Pro Launcher (Inno Setup 6.3 oder neuer)
; Wird vom GitHub-Workflow automatisch gebaut. Die Version kommt dann aus dem Tag.

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

[Setup]
AppName=DFP Pro Launcher
AppVersion={#MyAppVersion}
AppPublisher=Der_Fette_Pudel
DefaultDirName={autopf}\DFP Pro Launcher
DefaultGroupName=DFP Pro Launcher
OutputBaseFilename=DFP_Pro_Launcher_Setup
SetupIconFile=DFP_Pro_Launcher.ico
UninstallDisplayIcon={app}\DFPProLauncher.exe
Compression=lzma2
SolidCompression=yes
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
WizardStyle=modern
; Beim Update wird ein laufender Launcher geschlossen
CloseApplications=yes

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Desktop-Verknüpfung erstellen"; Flags: unchecked

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\DFP Pro Launcher"; Filename: "{app}\DFPProLauncher.exe"
Name: "{autodesktop}\DFP Pro Launcher"; Filename: "{app}\DFPProLauncher.exe"; Tasks: desktopicon

[Run]
; Normale Installation: Haekchen am Ende
Filename: "{app}\DFPProLauncher.exe"; Description: "DFP Pro Launcher starten"; Flags: nowait postinstall skipifsilent
; Update aus dem Launcher heraus (stille Installation): Launcher danach selbst wieder starten
Filename: "{app}\DFPProLauncher.exe"; Flags: nowait runasoriginaluser; Check: WizardSilent
