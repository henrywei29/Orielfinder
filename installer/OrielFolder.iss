#ifndef Payload
  #error Payload directory must be supplied by the build script
#endif
#ifndef Output
  #error Output directory must be supplied by the build script
#endif
[Setup]
AppId=OrielFolder.Setup
AppName=OrielFolder
AppVersion=1.0.1
AppPublisher=OrielFolder
DefaultDirName={commonpf64}\OrielFolder Setup
DisableDirPage=yes
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UsePreviousAppDir=no
MinVersion=10.0.19041
OutputDir={#Output}
OutputBaseFilename=OrielFolder-1.0.1-x64-Setup
SetupIconFile={#Payload}\OrielFolder.ico
Uninstallable=no
CreateUninstallRegKey=no
Compression=lzma2/fast
SolidCompression=yes
DiskSpanning=yes
DiskSliceSize=85000000
SlicesPerDisk=1
CloseApplications=no
RestartApplications=no
WizardStyle=modern
InfoBeforeFile={#Payload}\install-notice.txt
[Languages]
Name: "zhcn"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
Name: "en"; MessagesFile: "compiler:Default.isl"
[Tasks]
Name: desktopicon; Description: "创建桌面快捷方式 / Create desktop shortcut"
Name: listary; Description: "安装 Listary 6 集成 / Install Listary 6 integration"; Flags: unchecked
[Files]
Source: "{#Payload}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Code]
function GetFileAttributes(Name: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
var TrustPage: TInputOptionWizardPage;
procedure InitializeWizard;
begin
  TrustPage := CreateInputOptionPage(wpInfoBefore, '安装证书 / Certificate trust',
    '此测试发行版使用自签名证书 / This build uses a self-signed certificate',
    '将 OrielFolder 公钥证书加入 Windows Trusted People，并开启 Windows“允许安装受信任的应用”（旁加载）设置，用于安装此应用。不会加入根证书库、开启开发者模式、关闭安全防护或修改默认文件关联。本向导需要管理员权限；仅在勾选同意后才会进行上述更改。', False, False);
  TrustPage.Add('我同意安装此应用的签名证书 / I agree to trust this app certificate');
end;
function NextButtonClick(CurPageID: Integer): Boolean;
begin
  Result := True;
  if (CurPageID = TrustPage.ID) and not TrustPage.Values[0] then begin
    MsgBox('需要同意证书安装才能继续。也可取消，不更改系统。', mbInformation, MB_OK);
    Result := False;
  end;
end;
procedure CurStepChanged(CurStep: TSetupStep);
var Args: String; Code: Integer;
begin
  if CurStep = ssPostInstall then begin
    if not TrustPage.Values[0] then RaiseException('Certificate consent is required; silent installation is not supported.');
    Args := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Install-OrielFolder-Machine.ps1') + '"';
    if WizardIsTaskSelected('listary') then Args := Args + ' -InstallListary';
    if not Exec(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('Cannot start certificate installation');
    if Code <> 0 then RaiseException('Certificate or Listary installation did not complete.');
    Args := '-NoProfile -ExecutionPolicy Bypass -File "' + ExpandConstant('{app}\Install-OrielFolder.ps1') + '"';
    if WizardIsTaskSelected('desktopicon') then Args := Args + ' -DesktopShortcut';
    if not ExecAsOriginalUser(ExpandConstant('{sys}\WindowsPowerShell\v1.0\powershell.exe'), Args, '', SW_HIDE, ewWaitUntilTerminated, Code) then
      RaiseException('Cannot start installation for the original user');
    if Code <> 0 then RaiseException('OrielFolder 安装未完成，可修正提示的问题后重新运行安装程序。');
  end;
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var Current, Parent: String; Attr: Cardinal;
begin
  Result := '';
  Current := ExpandConstant('{app}');
  if CompareText(Current, ExpandConstant('{commonpf64}\OrielFolder Setup')) <> 0 then begin
    Result := 'For security this setup must use its protected Program Files directory.';
    Exit;
  end;
  while Length(Current) > 3 do begin
    Attr := GetFileAttributes(Current);
    if (Attr <> $FFFFFFFF) and ((Attr and $400) <> 0) then begin
      Result := 'The setup directory must not contain links or junctions.';
      Exit;
    end;
    Parent := ExtractFileDir(Current);
    if Parent = Current then Break;
    Current := Parent;
  end;
end;
