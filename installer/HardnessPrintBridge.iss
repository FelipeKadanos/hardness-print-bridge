#define MyAppName "Hardness Print Bridge"
#define MyAppPublisher "Hardness"
#define MyAppExeName "Hardness.PrintBridge.App.exe"
#define MyAgentExeName "Hardness.PrintBridge.Agent.exe"
#define MyServiceName "HardnessPrintBridgeAgent"
#ifndef MyAppVersion
  #define MyAppVersion "0.0.0"
#endif

[Setup]
AppId={{E9D6B2D0-5D47-4FBF-8EFD-4E4A813CB692}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Hardness Print Bridge
DefaultGroupName=Hardness Print Bridge
DisableProgramGroupPage=yes
OutputDir=artifacts\installer
OutputBaseFilename=HardnessPrintBridge-Setup-{#MyAppVersion}
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=admin
SetupLogging=yes
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"

[Tasks]
Name: "desktopicon"; Description: "Criar atalho na Area de Trabalho"; GroupDescription: "Opcoes:"; Flags: checkedonce
Name: "startmenuicon"; Description: "Criar atalho no Menu Iniciar"; GroupDescription: "Opcoes:"; Flags: checkedonce
Name: "autorunapp"; Description: "Iniciar automaticamente com o Windows"; GroupDescription: "Opcoes:"; Flags: checkedonce

[Files]
Source: "..\artifacts\package\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autodesktop}\Hardness Print Bridge"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon
Name: "{group}\Hardness Print Bridge"; Filename: "{app}\{#MyAppExeName}"; Tasks: startmenuicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "HardnessPrintBridge"; ValueData: """{app}\{#MyAppExeName}"""; Tasks: autorunapp; Flags: uninsdeletevalue

[Run]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\scripts\install-service.ps1"" -ExecutablePath ""{app}\{#MyAgentExeName}"""; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Executar o aplicativo ao concluir a instalacao"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\scripts\uninstall-service.ps1"""; Flags: runhidden waituntilterminated
