#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to the self-contained publish folder
#endif
#ifndef OutputDir
  #error OutputDir must point to the installer output folder
#endif

[Setup]
AppId={{DA56E2CF-5E59-486C-A3A4-21BBF19698D9}
AppName=Clipper98
AppVersion={#AppVersion}
AppPublisher=Clipper98
AppPublisherURL=https://github.com/mnzrsyq/Clipper98
DefaultDirName={localappdata}\Programs\Clipper98
DefaultGroupName=Clipper98
OutputDir={#OutputDir}
OutputBaseFilename=Clipper98-Setup-v{#AppVersion}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Clipper98
UninstallDisplayIcon={app}\Clipper98.exe

[Tasks]
Name: "desktopicon"; Description: "Buat shortcut di Desktop"; GroupDescription: "Shortcut tambahan:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Clipper98"; Filename: "{app}\Clipper98.exe"
Name: "{autodesktop}\Clipper98"; Filename: "{app}\Clipper98.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Clipper98.exe"; Description: "Jalankan Clipper98"; Flags: postinstall nowait skipifsilent
