#ifndef BundleNet
  #define BundleNet "0"
#endif

#define AppVersion "1.0.0"

[Setup]
AppId={{8C4E2A71-6B5D-4F0E-9A33-7D1E6C4B90A2}
AppName=KeySaver
AppVersion={#AppVersion}
AppPublisher=sovenov
AppPublisherURL=https://github.com/sovenov
AppSupportURL=https://github.com/sovenov/KeySaver
AppUpdatesURL=https://github.com/sovenov/KeySaver
VersionInfoVersion={#AppVersion}.0
DefaultDirName={autopf}\KeySaver
DefaultGroupName=KeySaver
DisableProgramGroupPage=yes
OutputDir=output
#if BundleNet == "1"
OutputBaseFilename=KeySaver-Setup-{#AppVersion}-net
#else
OutputBaseFilename=KeySaver-Setup-{#AppVersion}
#endif
SetupIconFile=..\Assets\KeySaver.ico
UninstallDisplayIcon={app}\KeySaver.exe
Compression=lzma2
SolidCompression=no
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=6.1sp1

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"

[Tasks]
Name: "desktopicon"; Description: "Создать значок на рабочем столе"; GroupDescription: "Дополнительно:"; Flags: unchecked
#if BundleNet == "1"
Name: "netframework"; Description: "Установить Microsoft .NET Framework 4.8"; GroupDescription: "Дополнительно:"; Flags: unchecked
#endif

[Files]
Source: "..\bin\Release\net48\KeySaver.exe"; DestDir: "{app}"; Flags: ignoreversion
#if BundleNet == "1"
Source: "..\c#\NDP48-x86-x64-AllOS-ENU.exe"; DestDir: "{tmp}"; DestName: "NDP48-x86-x64-AllOS-ENU.exe"; Flags: dontcopy nocompression
#endif

[Icons]
Name: "{group}\KeySaver"; Filename: "{app}\KeySaver.exe"
Name: "{autodesktop}\KeySaver"; Filename: "{app}\KeySaver.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\KeySaver.exe"; Description: "Запустить KeySaver"; Flags: nowait postinstall skipifsilent

[Code]
#if BundleNet == "1"
var
  NetTaskReady: Boolean;
#endif

function UserDataFolder: String;
var
  Folder: String;
begin
  if not RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Explorer\Shell Folders', 'Local AppData', Folder) then
    Folder := ExpandConstant('{localappdata}');
  Result := Folder + '\KeySaver';
end;

procedure CloseRunningApp;
var
  ResultCode: Integer;
  Folder: String;
begin
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/IM KeySaver.exe /F', '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
  Folder := UserDataFolder;
  DeleteFile(Folder + '\history.json');
  DeleteFile(Folder + '\preferences.json');
end;

function InitializeSetup: Boolean;
begin
  CloseRunningApp;
  Result := True;
end;

#if BundleNet == "1"
function IsDotNet48Installed: Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) and (Release >= 528040);
end;

procedure CurPageChanged(CurPageID: Integer);
var
  Index: Integer;
begin
  if (CurPageID <> wpSelectTasks) or NetTaskReady then
    Exit;
  NetTaskReady := True;
  for Index := 0 to WizardForm.TasksList.Items.Count - 1 do
  begin
    if Pos('.NET Framework 4.8', WizardForm.TasksList.ItemCaption[Index]) > 0 then
    begin
      WizardForm.TasksList.Checked[Index] := not IsDotNet48Installed;
      Break;
    end;
  end;
end;
#endif

procedure CurStepChanged(CurStep: TSetupStep);
#if BundleNet == "1"
var
  ResultCode: Integer;
  Installer: String;
#endif
begin
  if CurStep = ssInstall then
    CloseRunningApp;
#if BundleNet == "1"
  if (CurStep <> ssPostInstall) or (not WizardIsTaskSelected('netframework')) or IsDotNet48Installed then
    Exit;

  ExtractTemporaryFile('NDP48-x86-x64-AllOS-ENU.exe');
  Installer := ExpandConstant('{tmp}\NDP48-x86-x64-AllOS-ENU.exe');
  if not Exec(Installer, '/q /norestart', '', SW_SHOW, ewWaitUntilTerminated, ResultCode) then
  begin
    MsgBox('Не удалось запустить установщик .NET Framework 4.8.', mbError, MB_OK);
    Exit;
  end;
  if (ResultCode <> 0) and (ResultCode <> 3010) and (ResultCode <> 1641) then
    MsgBox('Не удалось установить .NET Framework 4.8. Код: ' + IntToStr(ResultCode) + '.', mbError, MB_OK);
#endif
end;
