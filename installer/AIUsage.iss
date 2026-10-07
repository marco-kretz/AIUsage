; Per-user installer. Build: iscc /DAppVersion=<version> installer\AIUsage.iss (after dotnet publish to artifacts\publish).
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif

[Setup]
AppId={{B6F1C9E4-2D7A-4E3B-9C51-8A0F4D6E2B17}
AppName=AI Usage
AppVersion={#AppVersion}
AppPublisher=Marco Kretz
AppPublisherURL=https://github.com/marco-kretz/AIUsage
DefaultDirName={autopf}\AIUsage
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
CloseApplications=force
SetupIconFile=..\src\AIUsage.App\Assets\AIUsage.ico
UninstallDisplayIcon={app}\AIUsage.exe
OutputDir=..\artifacts
OutputBaseFilename=AIUsage-{#AppVersion}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern

[Languages]
Name: "en"; MessagesFile: "compiler:Default.isl"
Name: "de"; MessagesFile: "compiler:Languages\German.isl"

[Files]
Source: "..\artifacts\publish\*"; Excludes: "*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{autoprograms}\AI Usage"; Filename: "{app}\AIUsage.exe"

[Registry]
; Written by the app itself (autostart toggle, toast AUMID); only removed here on uninstall.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "AIUsage"; Flags: dontcreatekey uninsdeletevalue
Root: HKCU; Subkey: "Software\Classes\AppUserModelId\AIUsage.App"; ValueType: none; Flags: dontcreatekey uninsdeletekey

[Run]
Filename: "{app}\AIUsage.exe"; Description: "{cm:LaunchProgram,AI Usage}"; Flags: nowait postinstall skipifsilent
