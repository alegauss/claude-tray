; Inno Setup script for Claude Code Tray
; Build (from the repo root):
;   1) dotnet publish -c Release
;   2) "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" build\installer.iss
; Output: dist\ClaudeTray-Setup.exe
; Every relative path below is resolved against THIS file's directory (build\), so the
; three that point at the repo root are "..\"-relative — see IMPROVEMENTS §VIII.3.

#define MyAppName "Claude Code Tray"
#define MyAppPublisher "Alexandre Oliveira"
#define MyAppExeName "ClaudeTray.exe"
#define MyPublishDir "..\bin\Release\net10.0-windows\win-x64\publish"
; Version is read straight from the published .exe (set by <Version> in ClaudeTray.csproj),
; so there is no separate version to bump here. Requires the publish step to have run first.
#define MyAppVersion GetStringFileInfo(MyPublishDir + "\" + MyAppExeName, PRODUCT_VERSION)

[Setup]
AppId={{8F3C2A14-7B6D-4E9A-9C21-CLAUDETRAY001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
; Per-user install: no admin rights required.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\ClaudeTray
DisableProgramGroupPage=yes
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExeName}
SetupIconFile=..\ClaudeTray.ico
OutputDir=..\dist
OutputBaseFilename=ClaudeTray-Setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; In-app self-update: the running app exits itself before launching the installer; this is a
; backup that closes any leftover instance via Restart Manager, without forcing a reboot.
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

; Every line the wizard shows is a message looked up in the install's language (T433): written as literals,
; an English install offered "Iniciar ... com o Windows" and a Start-menu entry named "Desinstalar". The
; uninstall entry, the finish-page run and the shortcuts group use Inno's own translated messages; the
; startup task has none, so it is declared here once per language.
[CustomMessages]
english.StartupGroup=Startup:
english.StartupTask=Start %1 with Windows
english.StartMenuTask=Create a Start menu shortcut
brazilianportuguese.StartupGroup=Inicialização:
brazilianportuguese.StartupTask=Iniciar o %1 com o Windows
brazilianportuguese.StartMenuTask=Criar atalho no Menu Iniciar

[Tasks]
Name: "startupicon"; Description: "{cm:StartupTask,{#MyAppName}}"; GroupDescription: "{cm:StartupGroup}"
Name: "startmenuicon"; Description: "{cm:StartMenuTask}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"; Tasks: startmenuicon

[Registry]
; Autostart per-user (mirrors the in-app "Start with Windows" toggle).
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
    ValueType: string; ValueName: "ClaudeTray"; ValueData: """{app}\{#MyAppExeName}"""; \
    Flags: uninsdeletevalue; Tasks: startupicon

[Run]
; postinstall (no skipifsilent) so a silent self-update relaunches the app automatically;
; in an interactive install it appears as the usual checked "start now" finish-page option.
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#MyAppName}}"; \
    Flags: nowait postinstall

[UninstallRun]
; Best-effort: close the running tray app before uninstalling.
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#MyAppExeName} /F"; Flags: runhidden; RunOnceId: "KillTray"
