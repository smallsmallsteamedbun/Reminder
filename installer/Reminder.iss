#define MyAppName "Reminder"
#define MyAppExeName "Reminder.App.exe"
#define MyAppPublisher "Reminder"

#ifndef MyAppVersion
  #define MyAppVersion "0.7.1"
#endif

#ifndef PublishDir
  #define PublishDir "..\artifacts\publish\win-x64"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts\installer"
#endif

[Setup]
AppId={{CF9ED244-A08A-4C93-A07E-B0B68581E0CC}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
AllowNoIcons=yes
OutputDir={#OutputDir}
OutputBaseFilename=Reminder-Setup-{#MyAppVersion}-win-x64
SetupArchitecture=x64
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
PrivilegesRequiredOverridesAllowed=dialog commandline
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
SetupLogging=yes
CloseApplications=no
RestartApplications=no
AppMutex=Local\Reminder.Desktop.SingleInstance.v1.Mutex
Uninstallable=yes
UninstallDisplayName={#MyAppName} {#MyAppVersion}
UninstallDisplayIcon={app}\{#MyAppExeName}
VersionInfoVersion={#MyAppVersion}.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} 安装程序
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}

[Languages]
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"

[Messages]
SetupAppRunningError=检测到 %1 正在运行。%n%n请在系统托盘中右键 Reminder 图标并选择“退出”，然后点击“确定”继续，或点击“取消”退出安装。
UninstallAppRunningError=检测到 %1 正在运行。%n%n请在系统托盘中右键 Reminder 图标并选择“退出”，然后点击“确定”继续，或点击“取消”退出卸载。

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "其他任务："; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Dirs]
Name: "{app}\Data"; Permissions: users-modify; Flags: uninsneveruninstall
Name: "{app}\Data\Updates"; Permissions: users-modify; Flags: uninsneveruninstall

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "启动 {#MyAppName}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  DeleteDataOnUninstall: Boolean;

function ShowDataChoiceDialog(): Boolean;
var
  DialogResult: Integer;
begin
  DialogResult := TaskDialogMsgBox(
    '卸载时如何处理 Reminder 数据？',
    '保留 Data 可在以后重新安装时恢复事件和设置。' + #13#10#13#10 +
    '删除 Data 将永久移除本机保存的 Reminder 数据。',
    mbConfirmation,
    MB_YESNOCANCEL, ['保留 Data', '删除 Data', '取消卸载'],
    0);

  DeleteDataOnUninstall := DialogResult = IDNO;
  Result :=
    (DialogResult = IDYES) or
    (DialogResult = IDNO);
end;

function DeleteDataParameterRequested(): Boolean;
var
  ParameterIndex: Integer;
  ParameterValue: String;
begin
  Result := False;
  for ParameterIndex := 1 to ParamCount do
  begin
    ParameterValue := ParamStr(ParameterIndex);
    if
      (CompareText(ParameterValue, '/DELETEDATA') = 0) or
      (CompareText(ParameterValue, '/DELETEDATA=1') = 0)
    then
    begin
      Result := True;
      Exit;
    end;
  end;
end;

function InitializeUninstall(): Boolean;
begin
  DeleteDataOnUninstall := False;

  if UninstallSilent then
  begin
    DeleteDataOnUninstall :=
      DeleteDataParameterRequested();
    if DeleteDataOnUninstall then
      Log('Silent uninstall will delete Data.')
    else
      Log('Silent uninstall will retain Data.');
    Result := True;
  end
  else
  begin
    Result := ShowDataChoiceDialog();
  end;
end;

procedure CurUninstallStepChanged(
  CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
  begin
    RegDeleteValue(
      HKCU,
      'Software\Microsoft\Windows\CurrentVersion\Run',
      'Reminder');
  end;

  if
    (CurUninstallStep = usPostUninstall) and
    DeleteDataOnUninstall
  then
  begin
    Log('Deleting the Reminder Data directory.');
    if not DelTree(
      ExpandConstant('{app}\Data'),
      True,
      True,
      True)
    then
      Log('The Reminder Data directory could not be fully deleted.');

    RemoveDir(ExpandConstant('{app}'));
  end;
end;
