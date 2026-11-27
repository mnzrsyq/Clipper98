#ifndef AppVersion
  #define AppVersion "1.1.1"
#endif
[Setup]
AppId={{DA56E2CF-5E59-486C-A3A4-21BBF19698D9}
AppName=Clipper98
AppVersion={#AppVersion}
AppPublisher=Clipper98
AppPublisherURL=https://github.com/mnzrsyq/Clipper98
DefaultDirName={localappdata}\Programs\Clipper98
DefaultGroupName=Clipper98
OutputDir=..\artifacts\installer
OutputBaseFilename=Clipper98-Setup-v{#AppVersion}
SetupIconFile=..\assets\clipper98.ico
PrivilegesRequired=lowest
ArchitecturesAllowed=x64os
ArchitecturesInstallIn64BitMode=x64os
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
RestartApplications=no
UninstallDisplayName=Clipper98
UninstallDisplayIcon={app}\Clipper98.exe

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "..\artifacts\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Clipper98"; Filename: "{app}\Clipper98.exe"; IconFilename: "{app}\Clipper98.exe"
Name: "{autodesktop}\Clipper98"; Filename: "{app}\Clipper98.exe"; IconFilename: "{app}\Clipper98.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Clipper98.exe"; Description: "Launch Clipper98"; Flags: postinstall nowait skipifsilent
