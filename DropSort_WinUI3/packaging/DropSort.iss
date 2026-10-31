; DropSort Windows installer (Inno Setup 6).
;
; Why Inno Setup: DropSort is an unpackaged WinUI 3 / Windows App SDK desktop app
; (WindowsPackageType=None), self-contained win-x64. Inno produces exactly one polished Setup EXE,
; installs per-user without administrator elevation, creates a Start-menu entry and an Add/Remove
; Programs uninstall entry, carries the app icon, and builds reproducibly from the command line
; (ISCC.exe). It never touches the user's library data: DropSort stores its SQLite database and
; settings under %LOCALAPPDATA%\DropSort, which is outside the install directory, so install and
; uninstall leave user data and media untouched.
;
; Build:  ISCC.exe packaging\DropSort.iss
; Output: artifacts\release\0.11.1\DropSortSetup.exe

#define MyAppName "TBD Movies"
#define MyAppVersion "0.11.1"
#define MyAppPublisher "TBD Movies"
#define MyAppExeName "DropSort.UI.exe"
; Payload = the self-contained Release BUILD output for win-x64. We package the build output, not the
; `publish` subfolder: for this unpackaged WinUI 3 app the Publish target drops the compiled resource
; index (DropSort.UI.pri) and XAML binaries (*.xbf), so a published build exits at startup, whereas the
; self-contained build output contains the PRI, the XBFs, the .NET runtime and the Windows App Runtime
; and launches correctly (verified). PDBs and the nested publish folder are excluded.
#ifndef PayloadDir
#define PayloadDir "..\src\DropSort.UI\bin\x64\Release\net10.0-windows10.0.22621\win-x64"
#endif
#define IconFile "..\src\DropSort.UI\Assets\DropSort.ico"

[Setup]
AppId={{1A127282-7849-4212-A5F1-A8FD87661B8D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
VersionInfoVersion=0.11.1.0
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayIcon={app}\{#MyAppExeName}
UninstallDisplayName={#MyAppName} {#MyAppVersion}
SetupIconFile={#IconFile}
OutputDir=..\artifacts\release\{#MyAppVersion}
OutputBaseFilename=DropSortSetup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
; Personal library data lives in %LOCALAPPDATA%\DropSort and is intentionally never installed here.

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[InstallDelete]
Type: files; Name: "{autodesktop}\DropSort.lnk"
Type: files; Name: "{group}\DropSort.lnk"

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion; Excludes: "*.pdb,publish\*,\publish"

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; AppUserModelID: "TBDMovies.TBDMovies"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon; AppUserModelID: "TBDMovies.TBDMovies"

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; Flags: nowait postinstall skipifsilent

