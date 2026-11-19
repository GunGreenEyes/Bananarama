; Bananarama installer (Inno Setup 6). Built by scripts\build.ps1, which passes
;   /DMajor=<whole-number version>  /DStage=<folder with the files to install>
; Per-user install, no admin rights needed.

#ifndef Major
  #define Major "0"
#endif
#ifndef Stage
  #define Stage "..\build\stage"
#endif

[Setup]
AppId={{6B0E5C1A-8F3D-4A27-9B1E-B4A2A2A1A7E2}
AppName=Bananarama
AppVersion={#Major}
AppVerName=Bananarama v{#Major}
AppPublisher=GunGreenEyes
AppPublisherURL=https://gungreeneyes.github.io/Bananarama/
AppSupportURL=https://github.com/GunGreenEyes/Bananarama/issues
AppUpdatesURL=https://github.com/GunGreenEyes/Bananarama/releases
VersionInfoVersion={#Major}.0.0.0
DefaultDirName={localappdata}\Programs\Bananarama
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputBaseFilename=Bananarama-Setup-v{#Major}
SetupIconFile=..\tray\banana.ico
UninstallDisplayIcon={app}\Bananarama.exe
UninstallDisplayName=Bananarama
WizardStyle=modern
Compression=lzma2/ultra64
SolidCompression=yes
CloseApplications=no
LicenseFile=..\LICENSE

[Tasks]
Name: startup; Description: "Start Bananarama when Windows starts (runs in the tray)"
Name: claude; Description: "Connect to Claude (adds /image to Claude Desktop and Claude Code)"
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#Stage}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[InstallDelete]
; files from an older version that a new one no longer ships
Type: filesandordirs; Name: "{app}\app\src"
Type: filesandordirs; Name: "{app}\app\skills"

[Icons]
Name: "{autoprograms}\Bananarama"; Filename: "{app}\Bananarama.exe"; Comment: "Gemini Nano Banana images for Claude"
Name: "{autoprograms}\Bananarama Settings"; Filename: "{app}\Bananarama.exe"; Parameters: "--settings"
Name: "{autodesktop}\Bananarama"; Filename: "{app}\Bananarama.exe"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "Bananarama"; ValueData: """{app}\Bananarama.exe"""; Tasks: startup

[Run]
Filename: "{app}\node\node.exe"; Parameters: """{app}\app\src\setup.js"" install"; WorkingDir: "{app}\app"; Flags: runhidden waituntilterminated; Tasks: claude; StatusMsg: "Connecting to Claude..."
Filename: "{app}\Bananarama.exe"; Description: "Start Bananarama now"; Flags: nowait postinstall

[UninstallRun]
Filename: "{app}\node\node.exe"; Parameters: """{app}\app\src\setup.js"" uninstall"; WorkingDir: "{app}\app"; Flags: runhidden waituntilterminated; RunOnceId: "Unregister"

[Messages]
FinishedLabel=Bananarama is in your tray (look for the banana).%n%nRight-click it for Settings. If you connected Claude, fully quit Claude Desktop and reopen it, then type /image followed by a description.

[Code]
procedure StopRunning(const Dir: String);
var
  Code: Integer;
begin
  { the tray, then any node.exe from our folder (middleware, or the MCP server Claude Desktop started) }
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM Bananarama.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'),
    '-NoProfile -NonInteractive -Command "Get-Process node -ErrorAction SilentlyContinue | Where-Object { $_.Path -like ''' + Dir + '\*'' } | Stop-Process -Force"',
    '', SW_HIDE, ewWaitUntilTerminated, Code);
  Sleep(500);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  StopRunning(ExpandConstant('{app}'));
  Result := '';
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    StopRunning(ExpandConstant('{app}'));
  if CurUninstallStep = usPostUninstall then
    RegDeleteValue(HKEY_CURRENT_USER, 'Software\Microsoft\Windows\CurrentVersion\Run', 'Bananarama');
end;
