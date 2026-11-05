#ifndef AppVersion
  #error AppVersion must be set by tools/build.ps1
#endif
#ifndef NumericVersion
  #error NumericVersion must be set by tools/build.ps1
#endif
#ifndef PublishDir
  #error PublishDir must be set by tools/build.ps1
#endif
#ifndef OutputDir
  #error OutputDir must be set by tools/build.ps1
#endif

[Setup]
AppId={{D0B67792-242D-42D7-AE76-092A20124CE8}
AppName=PoE Flip Overlay
SetupIconFile=..\src\FlipOverlay.App\Assets\app.ico
AppVersion={#AppVersion}
VersionInfoVersion={#NumericVersion}
AppPublisher=PoE Flip Overlay contributors
DefaultDirName={localappdata}\Programs\PoE Flip Overlay
DefaultGroupName=PoE Flip Overlay
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
OutputDir={#OutputDir}
OutputBaseFilename=PoE-Flip-Overlay-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
DisableProgramGroupPage=yes
CloseApplications=yes
RestartApplications=no
UninstallDisplayIcon={app}\FlipOverlay.App.exe
SetupLogging=yes

[Tasks]
Name: desktopicon; Description: "Create a desktop shortcut"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "portable.flag,user-data\*"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\PoE Flip Overlay"; Filename: "{app}\FlipOverlay.App.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\PoE Flip Overlay"; Filename: "{app}\FlipOverlay.App.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\FlipOverlay.App.exe"; Description: "Open PoE Flip Overlay"; Flags: nowait postinstall skipifsilent unchecked

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usPostUninstall) and (not UninstallSilent) then
    if MsgBox('Also delete your PoE Flip Overlay settings, observations, database and logs? Choose No to keep them.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
      DelTree(ExpandConstant('{localappdata}\PoEFlipOverlay'), True, True, True);
end;
