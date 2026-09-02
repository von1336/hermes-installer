; Hermes Workspace - full Windows setup wizard (Inno Setup 6)
; Build: installer\build-setup.ps1 -> installer\dist\HermesWorkspaceSetup.exe

#define MyAppName "Hermes Workspace"
#define MyAppVersion "2026.08.30.9"
#define MyAppPublisher "Hermes"
#define MyAppURL "https://github.com/outsourc-e/hermes-workspace"
#define MyAppExeName "HermesWorkspaceSetup.exe"

[Setup]
AppId={{8F3C2A91-7B4E-4D1A-9C6F-2E8A1B0D5F47}}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={localappdata}\hermes
DefaultGroupName=Hermes Workspace
DisableProgramGroupPage=no
AllowNoIcons=yes
LicenseFile=
InfoBeforeFile=wizard-info.txt
OutputDir=dist
OutputBaseFilename=HermesWorkspaceSetup
SetupIconFile=assets\hermes-setup.ico
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardImageFile=assets\wizard-side.bmp
WizardSmallImageFile=assets\wizard-small.bmp
WizardImageStretch=no
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UninstallDisplayName={#MyAppName}
VersionInfoVersion=2026.8.30.9
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription=Hermes Workspace installer (agent + MemOS + Tailscale + optional Ollama/Obsidian)
DisableWelcomePage=no
ShowLanguageDialog=yes
DirExistsWarning=no
UsePreviousAppDir=yes
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Types]
Name: "full"; Description: "Full installation (recommended)"
Name: "compact"; Description: "Compact (core + Tailscale only)"
Name: "custom"; Description: "Custom"; Flags: iscustom

[Components]
Name: "core"; Description: "Hermes Agent + Workspace (required)"; Types: full compact custom; Flags: fixed
Name: "tailscale"; Description: "Tailscale (phone connect via 100.x IP - recommended)"; Types: full compact custom
Name: "ollama"; Description: "Ollama (local models for Hermes on this PC)"; Types: full
Name: "memos"; Description: "MemOS memory plugin (local or cloud provider)"; Types: full
Name: "obsidian"; Description: "Obsidian vault editor"; Types: full
Name: "obsidian_skills"; Description: "Obsidian Skills pack for Hermes Agent"; Types: full
Name: "firewall"; Description: "Windows Firewall rules (ports 3000 / 8642 / 9119)"; Types: full compact custom
Name: "services"; Description: "Start Hermes services after install"; Types: full compact custom

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut to Connect QR"; GroupDescription: "Additional shortcuts:"; Flags: checkedonce
Name: "openconnect"; Description: "Open Connect QR page when finished"; GroupDescription: "After install:"; Flags: checkedonce

[Files]
Source: "install-hermes.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "uninstall-hermes.ps1"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "lib\InstallComponents.ps1"; DestDir: "{app}\installer\lib"; Flags: ignoreversion
Source: "wizard-info.txt"; DestDir: "{app}\installer"; Flags: ignoreversion
Source: "assets\hermes-logo.png"; DestDir: "{app}"; DestName: "hermes-logo.png"; Flags: ignoreversion
Source: "assets\hermes-setup.ico"; DestDir: "{app}"; DestName: "hermes-logo.ico"; Flags: ignoreversion

[Icons]
Name: "{group}\Hermes Connect QR"; Filename: "{app}\connect.html"; WorkingDir: "{app}"; IconFilename: "{app}\hermes-logo.ico"; Flags: createonlyiffileexists
Name: "{group}\Uninstall Hermes Workspace"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Hermes Connect QR"; Filename: "{app}\connect.html"; IconFilename: "{app}\hermes-logo.ico"; Tasks: desktopicon; Flags: createonlyiffileexists

[Run]
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{code:GetLauncherPath}"""; \
  StatusMsg: "Installing Hermes (this can take several minutes)..."; \
  Flags: waituntilterminated; \
  Components: core

[UninstallRun]
Filename: "powershell.exe"; \
  Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\installer\uninstall-hermes.ps1"" -KeepUserData"; \
  Flags: waituntilterminated runhidden; RunOnceId: "HermesUninstallPs1"

[Code]
var
  WorkspacePage: TInputDirWizardPage;
  MemOSModePage: TInputOptionWizardPage;
  MemOSProviderPage: TInputQueryWizardPage;
  SummaryPage: TOutputMsgMemoWizardPage;
  LaunchScriptPath: string;

function MemOSSelected: Boolean;
begin
  Result := WizardIsComponentSelected('memos');
end;

function MemOSProviderMode: Boolean;
begin
  Result := MemOSSelected and (MemOSModePage.SelectedValueIndex = 1);
end;

function EscapePsSingleQuoted(const S: string): string;
var
  T: string;
begin
  T := S;
  StringChangeEx(T, '''', '''''', True);
  Result := T;
end;

procedure SaveStringListUtf8(const Path: string; Lines: TStringList);
var
  I: Integer;
  Content: string;
  Utf8: AnsiString;
  FS: TFileStream;
begin
  Content := '';
  for I := 0 to Lines.Count - 1 do
  begin
    if I > 0 then Content := Content + #13#10;
    Content := Content + Lines[I];
  end;
  Utf8 := AnsiString(UTF8Encode(Content));
  FS := TFileStream.Create(Path, fmCreate);
  try
    if Length(Utf8) > 0 then
      FS.WriteBuffer(Utf8[1], Length(Utf8));
  finally
    FS.Free;
  end;
end;

procedure CreateConnectShortcuts;
var
  ConnectHtml, IconFile, Desktop: string;
begin
  ConnectHtml := ExpandConstant('{app}\connect.html');
  if not FileExists(ConnectHtml) then Exit;
  IconFile := ExpandConstant('{app}\hermes-logo.ico');
  if WizardIsTaskSelected('desktopicon') then
  begin
    Desktop := ExpandConstant('{autodesktop}');
    CreateShellLink(Desktop + '\Hermes Connect QR.lnk', 'Hermes Connect QR', ConnectHtml, '', ExpandConstant('{app}'), IconFile, 0, SW_SHOWNORMAL);
  end;
  CreateShellLink(ExpandConstant('{group}') + '\Hermes Connect QR.lnk', 'Hermes Connect QR', ConnectHtml, '', ExpandConstant('{app}'), IconFile, 0, SW_SHOWNORMAL);
end;

function BoolLit(Selected: Boolean): string;
begin
  if Selected then
    Result := '$true'
  else
    Result := '$false';
end;

function IfThen(Cond: Boolean; A, B: string): string;
begin
  if Cond then Result := A else Result := B;
end;

procedure ApplyHermesBrandingText;
begin
  WizardForm.WelcomeLabel1.Font.Style := [fsBold];
  WizardForm.WelcomeLabel1.Font.Size := 13;
  WizardForm.WelcomeLabel2.Font.Size := 9;

  if ActiveLanguage = 'russian' then
  begin
    WizardForm.WelcomeLabel1.Caption := 'Hermes Workspace';
    WizardForm.WelcomeLabel2.Caption :=
      'Professional setup in Hermes style.'#13#10 +
      'Tailnet-first connect, secure defaults, and guided install.';
  end
  else
  begin
    WizardForm.WelcomeLabel1.Caption := 'Hermes Workspace';
    WizardForm.WelcomeLabel2.Caption :=
      'Professional setup in Hermes style.'#13#10 +
      'Tailnet-first connect, secure defaults, and guided install.';
  end;
end;

procedure InitializeWizard;
begin
  LaunchScriptPath := ExpandConstant('{tmp}\hermes-install-launch.ps1');
  ApplyHermesBrandingText;

  WorkspacePage := CreateInputDirPage(
    wpSelectDir,
    'Workspace folder',
    'Where should the hermes-workspace project be cloned?',
    'Choose a folder for the web workspace source (pnpm project).'#13#10 +
      'Install directory (previous page) stores config, logs, and connect.html.',
    False,
    ''
  );
  WorkspacePage.Add('&Workspace directory:');
  WorkspacePage.Values[0] := AddBackslash(GetEnv('USERPROFILE')) + 'hermes-workspace';

  MemOSModePage := CreateInputOptionPage(
    wpSelectComponents,
    'MemOS mode',
    'Choose how MemOS should access language models',
    'Local mode uses Ollama on this PC when available. Provider mode uses an OpenAI-compatible API.',
    True,
    False
  );
  MemOSModePage.Add('Local model (Ollama / offline embedding)');
  MemOSModePage.Add('Cloud provider (OpenAI-compatible API)');
  MemOSModePage.SelectedValueIndex := 0;

  MemOSProviderPage := CreateInputQueryPage(
    MemOSModePage.ID,
    'MemOS provider',
    'Enter provider credentials for MemOS',
    'These values are written to MemOS config.yaml on this PC only.'
  );
  MemOSProviderPage.Add('API base URL (e.g. https://api.openai.com/v1):', False);
  MemOSProviderPage.Add('API key:', True);
  MemOSProviderPage.Add('Model name:', False);
  MemOSProviderPage.Values[0] := 'https://api.openai.com/v1';
  MemOSProviderPage.Values[1] := '';
  MemOSProviderPage.Values[2] := 'gpt-4o-mini';

  SummaryPage := CreateOutputMsgMemoPage(
    MemOSProviderPage.ID,
    'Ready to install',
    'Review your configuration',
    'The selected configuration is shown below.',
    ''
  );
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := False;
  if PageID = MemOSModePage.ID then
    Result := not MemOSSelected;
  if PageID = MemOSProviderPage.ID then
    Result := (not MemOSSelected) or (MemOSModePage.SelectedValueIndex = 0);
end;

procedure WriteLaunchScript;
var
  Lines: TStringList;
  InstallPs1, Ws, AppDir, MemMode, ProviderUrl, ProviderKey, ProviderModel: string;
begin
  InstallPs1 := ExpandConstant('{app}\installer\install-hermes.ps1');
  AppDir := ExpandConstant('{app}');
  Ws := WorkspacePage.Values[0];

  if not MemOSSelected then
    MemMode := 'skip'
  else if MemOSModePage.SelectedValueIndex = 1 then
    MemMode := 'provider'
  else
    MemMode := 'local';

  ProviderUrl := MemOSProviderPage.Values[0];
  ProviderKey := MemOSProviderPage.Values[1];
  ProviderModel := MemOSProviderPage.Values[2];

  Lines := TStringList.Create;
  try
    Lines.Add('# Auto-generated by HermesWorkspaceSetup - do not edit');
    Lines.Add('$ErrorActionPreference = ''Stop''');
    Lines.Add('try {');
    Lines.Add(Format('  & ''%s'' `', [EscapePsSingleQuoted(InstallPs1)]));
    Lines.Add(Format('    -InstallDir ''%s'' `', [EscapePsSingleQuoted(AppDir)]));
    Lines.Add(Format('    -WorkspaceDir ''%s'' `', [EscapePsSingleQuoted(Ws)]));
    Lines.Add(Format('    -InstallTailscale:%s `', [BoolLit(WizardIsComponentSelected('tailscale'))]));
    Lines.Add(Format('    -InstallOllama:%s `', [BoolLit(WizardIsComponentSelected('ollama'))]));
    Lines.Add(Format('    -InstallMemOS:%s `', [BoolLit(MemOSSelected)]));
    Lines.Add(Format('    -MemOSMode %s `', [MemMode]));
    Lines.Add(Format('    -InstallObsidian:%s `', [BoolLit(WizardIsComponentSelected('obsidian'))]));
    Lines.Add(Format('    -InstallObsidianSkills:%s `', [BoolLit(WizardIsComponentSelected('obsidian_skills'))]));
    Lines.Add(Format('    -ConfigureFirewall:%s `', [BoolLit(WizardIsComponentSelected('firewall'))]));
    Lines.Add(Format('    -StartServices:%s `', [BoolLit(WizardIsComponentSelected('services'))]));
    Lines.Add(Format('    -EnableAutoStart:%s `', [BoolLit(WizardIsComponentSelected('services'))]));
    Lines.Add(Format('    -CreateShortcuts:%s `', [BoolLit(WizardIsTaskSelected('desktopicon'))]));
    Lines.Add(Format('    -OpenConnect:%s `', [BoolLit(WizardIsTaskSelected('openconnect'))]));
    if Trim(ProviderUrl) <> '' then
      Lines.Add(Format('    -MemOSProviderUrl ''%s'' `', [EscapePsSingleQuoted(ProviderUrl)]));
    if Trim(ProviderKey) <> '' then
      Lines.Add(Format('    -MemOSProviderKey ''%s'' `', [EscapePsSingleQuoted(ProviderKey)]));
    if Trim(ProviderModel) <> '' then
      Lines.Add(Format('    -MemOSProviderModel ''%s'' `', [EscapePsSingleQuoted(ProviderModel)]));
    Lines.Add('    -NoPause');
    Lines.Add('  if ($LASTEXITCODE -ne 0) { throw "install-hermes.ps1 exited $LASTEXITCODE" }');
    Lines.Add('} catch {');
    Lines.Add('  $err = Join-Path ''' + EscapePsSingleQuoted(AppDir) + ''' ''install-error.txt''');
    Lines.Add('  if (-not (Test-Path $err)) { $err = Join-Path $env:LOCALAPPDATA ''hermes\install-error.txt'' }');
    Lines.Add('  Write-Host ''''');
    Lines.Add('  Write-Host ''INSTALLATION FAILED'' -ForegroundColor Red');
    Lines.Add('  Write-Host $_ -ForegroundColor Red');
    Lines.Add('  if (Test-Path $err) {');
    Lines.Add('    Write-Host ("Error report: {0}" -f $err) -ForegroundColor Yellow');
    Lines.Add('    try { Start-Process notepad.exe $err } catch {}');
    Lines.Add('  } else {');
    Lines.Add('    Write-Host ''Press Enter to close this window...''');
    Lines.Add('    try { [void][Console]::ReadLine() } catch { Start-Sleep -Seconds 20 }');
    Lines.Add('  }');
    Lines.Add('  exit 1');
    Lines.Add('}');
    SaveStringListUtf8(LaunchScriptPath, Lines);
  finally
    Lines.Free;
  end;
end;

function GetLauncherPath(Param: string): string;
begin
  WriteLaunchScript;
  Result := LaunchScriptPath;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if CurPageID = WorkspacePage.ID then
  begin
    if Trim(WorkspacePage.Values[0]) = '' then
    begin
      MsgBox('Please choose a workspace folder.', mbError, MB_OK);
      Result := False;
    end;
  end;
  if CurPageID = MemOSProviderPage.ID then
  begin
    if MemOSProviderMode and (Trim(MemOSProviderPage.Values[1]) = '') then
    begin
      if MsgBox('MemOS provider API key is empty. Continue anyway (MemOS may run in degraded mode)?',
        mbConfirmation, MB_YESNO) = IDNO then
        Result := False;
    end;
  end;
end;

procedure CurPageChanged(CurPageID: Integer);
var
  SummaryText: string;
begin
  if CurPageID = SummaryPage.ID then
  begin
    SummaryText :=
      'Install folder: ' + ExpandConstant('{app}') + #13#10 +
      'Workspace: ' + WorkspacePage.Values[0] + #13#10#13#10 +
      'Components:' + #13#10 +
      '  Tailscale: ' + IfThen(WizardIsComponentSelected('tailscale'), 'yes', 'no') + #13#10 +
      '  Ollama: ' + IfThen(WizardIsComponentSelected('ollama'), 'yes', 'no') + #13#10 +
      '  MemOS: ' + IfThen(MemOSSelected, 'yes', 'no');
    if MemOSSelected then
      SummaryText := SummaryText + ' (' +
        IfThen(MemOSModePage.SelectedValueIndex = 1, 'provider', 'local') + ')';
    SummaryText := SummaryText + #13#10 +
      '  Obsidian: ' + IfThen(WizardIsComponentSelected('obsidian'), 'yes', 'no') + #13#10 +
      '  Obsidian Skills: ' + IfThen(WizardIsComponentSelected('obsidian_skills'), 'yes', 'no') + #13#10#13#10 +
      'Click Next to begin installation.';
    SummaryPage.RichEditViewer.Text := SummaryText;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  ErrorFile: string;
  ErrorCode: Integer;
begin
  if CurStep = ssPostInstall then
  begin
    ErrorFile := ExpandConstant('{app}\install-error.txt');
    if not FileExists(ErrorFile) then
      ErrorFile := ExpandConstant('{localappdata}\hermes\install-error.txt');
    if FileExists(ErrorFile) then
    begin
      MsgBox(
        'Hermes installation failed.'#13#10#13#10 +
        'Error report:'#13#10 + ErrorFile + #13#10#13#10 +
        'A copy is on the Desktop: Hermes-install-error.txt'#13#10#13#10 +
        'Notepad will open the report so you can see the exact error.',
        mbError, MB_OK);
      ShellExec('', 'notepad.exe', '"' + ErrorFile + '"', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
    end
    else
      CreateConnectShortcuts;
  end;
end;


