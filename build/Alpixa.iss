; Alpixa Windows kurulum betigi (Inno Setup 6)
; build-windows.ps1 tarafindan SourceDir, OutputDir ve AppVersion degerleriyle cagrilir.

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif

[Setup]
AppId={{6B0B5F4A-7C1E-4F55-9E2B-5B1A2C3D4E5F}
AppName=Alpixa
AppVersion={#AppVersion}
AppPublisher=Alpixa
DefaultDirName={localappdata}\Programs\Alpixa
DefaultGroupName=Alpixa
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir={#OutputDir}
OutputBaseFilename=AlpixaSetup
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.17763
WizardStyle=modern
UninstallDisplayIcon={app}\Alpixa.App.exe
UninstallDisplayName=Alpixa

[Languages]
Name: "turkish"; MessagesFile: "compiler:Languages\Turkish.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Alpixa"; Filename: "{app}\Alpixa.App.exe"
Name: "{autodesktop}\Alpixa"; Filename: "{app}\Alpixa.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Alpixa.App.exe"; Description: "{cm:LaunchProgram,Alpixa}"; Flags: nowait postinstall skipifsilent
