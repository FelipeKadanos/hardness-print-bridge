#define MyAppName "Hardness Print Bridge"
#define MyAppPublisher "Hardness"
#define MyAppExeName "Hardness.PrintBridge.App.exe"
#define MyAgentExeName "Hardness.PrintBridge.Agent.exe"
#define MyServiceName "HardnessPrintBridgeAgent"
#define MyDefaultQueueRootPath "C:\Hardness-Print-Brige\print-agent"
#define MyDefaultPrinterName "Microsoft Print to PDF"
#define MyDefaultRemoteListUrl "http://localhost/api/rel/list_files?API_AUTH=REPLACE_ME"
#define MyDefaultRemoteDownloadUrlTemplate "http://localhost/api/rel/select_file?API_AUTH=REPLACE_ME&file={fileName}"
#define MyDefaultHardnessCallbackUrl "http://localhost/api/rel/callback?API_AUTH=REPLACE_ME"
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
OutputDir=..\artifacts\installer
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
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\scripts\write-agent-config.ps1"" -QueueRootPath ""{code:GetQueueRootPath}"" -DefaultPrinterName ""{code:GetDefaultPrinterName}"" -RemoteListUrl ""{code:GetRemoteListUrl}"" -RemoteDownloadUrlTemplate ""{code:GetRemoteDownloadUrlTemplate}"" -HardnessCallbackUrl ""{code:GetHardnessCallbackUrl}"" -ApiAuthToken ""{code:GetApiAuthToken}"""; Flags: runhidden waituntilterminated
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\scripts\install-service.ps1"" -ExecutablePath ""{app}\{#MyAgentExeName}"""; Flags: runhidden waituntilterminated
Filename: "{app}\{#MyAppExeName}"; Description: "Executar o aplicativo ao concluir a instalacao"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "powershell.exe"; Parameters: "-ExecutionPolicy Bypass -File ""{app}\scripts\uninstall-service.ps1"""; Flags: runhidden waituntilterminated

[Code]
var
  QueueRootPage: TInputDirWizardPage;
  ApiQueryPage: TInputQueryWizardPage;
  UrlQueryPage: TInputQueryWizardPage;
  PrinterPage: TWizardPage;
  PrinterComboBox: TNewComboBox;
  PrinterNames: TStringList;

procedure PopulatePrinterList;
var
  ResultCode: Integer;
  OutputPath: string;
  PowerShellCommand: string;
  Index: Integer;
begin
  PrinterNames := TStringList.Create;
  OutputPath := ExpandConstant('{tmp}\hpb-printers.txt');
  PowerShellCommand :=
    'Get-Printer | Select-Object -ExpandProperty Name | Out-File -FilePath ''' +
    OutputPath + ''' -Encoding utf8';

  if Exec(
    'powershell.exe',
    '-NoProfile -ExecutionPolicy Bypass -Command "' + PowerShellCommand + '"',
    '',
    SW_HIDE,
    ewWaitUntilTerminated,
    ResultCode) and FileExists(OutputPath) then begin
    PrinterNames.LoadFromFile(OutputPath);
  end;

  if PrinterNames.Count = 0 then begin
    PrinterNames.Add('{#MyDefaultPrinterName}');
  end;

  for Index := 0 to PrinterNames.Count - 1 do begin
    PrinterComboBox.Items.Add(PrinterNames[Index]);
  end;

  Index := PrinterComboBox.Items.IndexOf('{#MyDefaultPrinterName}');
  if Index < 0 then begin
    Index := 0;
  end;
  PrinterComboBox.ItemIndex := Index;
end;

procedure InitializeWizard;
var
  PrinterLabel: TNewStaticText;
begin
  QueueRootPage := CreateInputDirPage(
    wpSelectTasks,
    'Fila de impressao',
    'Defina a pasta raiz da fila de impressao.',
    'O instalador vai criar inbox, processing, printed e error dentro desse caminho.',
    False,
    '');
  QueueRootPage.Add('');
  QueueRootPage.Values[0] := '{#MyDefaultQueueRootPath}';

  ApiQueryPage := CreateInputQueryPage(
    QueueRootPage.ID,
    'Integracao Hardness',
    'Informe o token API_AUTH usado nas URLs do backend.',
    'O valor informado aqui sera usado para substituir REPLACE_ME nas requisicoes do Agent.');
  ApiQueryPage.Add('API_AUTH:', False);
  ApiQueryPage.Values[0] := '';

  UrlQueryPage := CreateInputQueryPage(
    ApiQueryPage.ID,
    'Endpoints REST',
    'Revise ou ajuste os caminhos REST usados pelo Agent.',
    'Esses campos aceitam REPLACE_ME, que sera trocado pelo API_AUTH informado na tela anterior.');
  UrlQueryPage.Add('RemoteListUrl:', False);
  UrlQueryPage.Add('RemoteDownloadUrlTemplate:', False);
  UrlQueryPage.Add('HardnessCallbackUrl:', False);
  UrlQueryPage.Values[0] := '{#MyDefaultRemoteListUrl}';
  UrlQueryPage.Values[1] := '{#MyDefaultRemoteDownloadUrlTemplate}';
  UrlQueryPage.Values[2] := '{#MyDefaultHardnessCallbackUrl}';

  PrinterPage := CreateCustomPage(
    UrlQueryPage.ID,
    'Impressora padrao',
    'Selecione a impressora ativa que o Agent usara por padrao.');

  PrinterLabel := TNewStaticText.Create(PrinterPage);
  PrinterLabel.Parent := PrinterPage.Surface;
  PrinterLabel.Left := 0;
  PrinterLabel.Top := ScaleY(8);
  PrinterLabel.Caption := 'Impressora padrao:';

  PrinterComboBox := TNewComboBox.Create(PrinterPage);
  PrinterComboBox.Parent := PrinterPage.Surface;
  PrinterComboBox.Left := 0;
  PrinterComboBox.Top := PrinterLabel.Top + PrinterLabel.Height + ScaleY(8);
  PrinterComboBox.Width := PrinterPage.SurfaceWidth;
  PrinterComboBox.Style := csDropDownList;

  PopulatePrinterList;
end;

function GetQueueRootPath(Value: string): string;
begin
  Result := QueueRootPage.Values[0];
end;

function GetApiAuthToken(Value: string): string;
begin
  Result := ApiQueryPage.Values[0];
end;

function GetRemoteListUrl(Value: string): string;
begin
  Result := UrlQueryPage.Values[0];
end;

function GetRemoteDownloadUrlTemplate(Value: string): string;
begin
  Result := UrlQueryPage.Values[1];
end;

function GetHardnessCallbackUrl(Value: string): string;
begin
  Result := UrlQueryPage.Values[2];
end;

function GetDefaultPrinterName(Value: string): string;
begin
  Result := PrinterComboBox.Text;
end;

procedure DeinitializeSetup;
begin
  if Assigned(PrinterNames) then begin
    PrinterNames.Free;
  end;
end;
