#define MyAppName "All You Can Do with Your Thermal Printer"
#define MyAppVersion "1.0.3"
#define MyAppExe "PrintToDo.exe"

[Setup]
AppId={{3DBB7E30-0D75-417A-B903-9A37AB7DA27D}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher=Rhahee
AppPublisherURL=https://github.com/Rhahee/aycd-with-your-thermal
AppUpdatesURL=https://github.com/Rhahee/aycd-with-your-thermal
DefaultDirName={localappdata}\Programs\{#MyAppName}
DisableDirPage=yes
DefaultGroupName={#MyAppName}
UninstallDisplayIcon={app}\{#MyAppExe}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
MinVersion=10.0
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
OutputDir=output
OutputBaseFilename=All-You-Can-Do-with-Your-Thermal-Printer-Setup-{#MyAppVersion}-win-x64

[Tasks]
Name: "desktopicon"; Description: "Buat pintasan desktop"; GroupDescription: "Pintasan tambahan:"; Flags: unchecked

[Files]
Source: "..\dist\{#MyAppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\update-commit.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\dist\assets\*"; DestDir: "{app}\assets"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "prerequisites\MicrosoftEdgeWebView2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{tmp}\MicrosoftEdgeWebView2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "Memasang Microsoft Edge WebView2 Runtime..."; Flags: waituntilterminated; Check: not WebView2Installed
Filename: "{app}\{#MyAppExe}"; Description: "Jalankan {#MyAppName}"; Flags: nowait postinstall skipifsilent; Check: WebView2Installed

[UninstallDelete]
Type: files; Name: "{app}\{#MyAppExe}.previous"
Type: files; Name: "{app}\{#MyAppExe}.incoming"
Type: filesandordirs; Name: "{app}\assets.previous"
Type: filesandordirs; Name: "{app}\assets.incoming"

[Code]
function WebView2Installed: Boolean;
var
  Version: String;
  Key: String;
begin
  Key := 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}';
  Result := False;
  if RegQueryStringValue(HKLM32, Key, 'pv', Version) then
  begin
    if (Version <> '') and (Version <> '0.0.0.0') then
    begin
      Result := True;
      Exit;
    end;
  end;
  if RegQueryStringValue(HKCU, Key, 'pv', Version) then
    Result := (Version <> '') and (Version <> '0.0.0.0');
end;
