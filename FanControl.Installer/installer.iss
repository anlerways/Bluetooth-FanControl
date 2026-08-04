; FanControl Inno Setup installer script
; Compile examples:
;   ISCC.exe installer.iss /DAppSource="...\selfcontained" /DOutputName="FanControl-setup-selfcontained" /DRequireNet8=0
;   ISCC.exe installer.iss /DAppSource="...\framework" /DOutputName="FanControl-setup-framework" /DRequireNet8=1

#define MyAppName "FanControl"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "anlerways"
#define MyAppExeName "FanControl.exe"

#ifndef AppSource
  #define AppSource "D:\PROJECT\CODEX\FAN\artifacts\release\selfcontained"
#endif
#ifndef OutputName
  #define OutputName "FanControl-setup"
#endif
#ifndef OutputDir
  #define OutputDir "C:\Users\ASUS\Desktop\FanControl-Release"
#endif
; 1 = framework-dependent (check .NET 8 Desktop Runtime), 0 = self-contained
#ifndef RequireNet8
  #define RequireNet8 0
#endif

[Setup]
AppId={{8F2B6C41-7E5A-4D2F-9B31-2A8C6E1D5F90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL=https://github.com/anlerways/Bluetooth-FanControl
AppSupportURL=https://github.com/anlerways/Bluetooth-FanControl
DefaultDirName={autopf}\FanControl
DefaultGroupName=FanControl
DisableProgramGroupPage=yes
PrivilegesRequired=admin
CloseApplications=yes
OutputDir={#OutputDir}
OutputBaseFilename={#OutputName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
SetupIconFile=D:\PROJECT\CODEX\FAN\FanControl.UI\app.ico
UninstallDisplayIcon={app}\FanControl.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional tasks:"

; 安装前清空旧目录：避免自带运行库版与无环境版混装后残留 coreclr/hostfxr 等运行时文件，
; 导致无环境版启动时报“需要安装 .NET 8”。
[InstallDelete]
Type: filesandordirs; Name: "{app}\*"

[Files]
Source: "{#AppSource}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[Code]
#if RequireNet8
function IsDotNet8DesktopRuntimeInstalled(): Boolean;
var
  SubkeyNames: TArrayOfString;
  I: Integer;
  DisplayName: String;
begin
  Result := False;
  if RegGetSubkeyNames(HKLM64, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall', SubkeyNames) then
  begin
    for I := 0 to GetArrayLength(SubkeyNames) - 1 do
    begin
      if RegQueryStringValue(HKLM64,
          'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\' + SubkeyNames[I],
          'DisplayName', DisplayName) then
      begin
        if (Pos('Windows Desktop Runtime', DisplayName) > 0) and (Pos('8.', DisplayName) > 0) then
        begin
          Result := True;
          Exit;
        end;
      end;
    end;
  end;
end;

#endif

// 安装/覆盖/卸载前先关闭正在运行的 FanControl.exe，避免文件被占用
procedure KillFanControl();
var
  ResultCode: Integer;
begin
  Exec('taskkill.exe', '/IM FanControl.exe /T /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  KillFanControl();
  Result := '';
end;

function PrepareToUninstall(var Msg: String): Boolean;
begin
  KillFanControl();
  Result := True;
end;

#if RequireNet8
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNet8DesktopRuntimeInstalled() then
  begin
    MsgBox('Missing .NET 8 Desktop Runtime (Microsoft.WindowsDesktop.App 8.x).' + #13#10 +
           'This WPF app needs the DESKTOP runtime, which is separate from the base' + #13#10 +
           '.NET 8 runtime / SDK. Please install it first:' + #13#10 +
           'https://dotnet.microsoft.com/download/dotnet/8.0' + #13#10 + #13#10 +
           'Or use the self-contained installer instead.', mbInformation, MB_OK);
    Result := False;
  end;
end;
#endif
