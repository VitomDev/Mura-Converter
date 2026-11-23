; Inno Setup script for Mura Converter. Built by build.ps1.
#define AppName "Mura Converter"
#define AppVersion "2.3.1"
#define AppExe "MuraConverter.exe"

[Setup]
AppId={{6E0B9C1A-3F7D-4C55-9A0E-4D2B7C1E8F21}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=VitomDev
AppPublisherURL=https://github.com/VitomDev/Mura-Converter
; Per-user install: no admin prompt, and yt-dlp can update itself in place.
PrivilegesRequired=lowest
DefaultDirName={localappdata}\Programs\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\dist
OutputBaseFilename=MuraSetup
LicenseFile=..\LICENSE
SetupIconFile=..\Mura Converter\appicon.ico
UninstallDisplayIcon={app}\{#AppExe}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"
Name: "contextmenu"; Description: "Add ""Download copied link with Mura"" to the Explorer right-click menu"; GroupDescription: "{cm:AdditionalIcons}"

[Registry]
; Right-click on a folder background (or the desktop) / on a folder: downloads the copied link into that folder.
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MuraConverter"; ValueType: string; ValueName: ""; ValueData: "Download copied link with Mura"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MuraConverter"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\Background\shell\MuraConverter\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --clipboard --dir ""%V"""; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MuraConverter"; ValueType: string; ValueName: ""; ValueData: "Download copied link here with Mura"; Flags: uninsdeletekey; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MuraConverter"; ValueType: string; ValueName: "Icon"; ValueData: "{app}\{#AppExe}"; Tasks: contextmenu
Root: HKCU; Subkey: "Software\Classes\Directory\shell\MuraConverter\command"; ValueType: string; ValueName: ""; ValueData: """{app}\{#AppExe}"" --clipboard --dir ""%1"""; Tasks: contextmenu

[Messages]
; Shown above the license on the wizard's license page.
LicenseLabel3=Mura is free and open source. If you paid for it, you were scammed: the official download is only at github.com/VitomDev. Please read the license below and accept it to continue.

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}\tools"
