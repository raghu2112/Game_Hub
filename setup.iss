[Setup]
AppName=Dell Gaming Control Center
AppVersion=1.0.0
DefaultDirName={autopf}\Dell Gaming Control Center
DefaultGroupName=Dell Gaming Control Center
OutputDir=installer
OutputBaseFilename=DellGamingControlCenter_Setup
Compression=lzma
SolidCompression=yes
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=admin

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop icon"; GroupDescription: "Additional icons:"; Flags: unchecked

[Files]
Source: "publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Dell Gaming Control Center"; Filename: "{app}\DellGamingControlCenter.exe"
Name: "{autodesktop}\Dell Gaming Control Center"; Filename: "{app}\DellGamingControlCenter.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\DellGamingControlCenter.exe"; Description: "Launch Dell Gaming Control Center"; Flags: nowait postinstall skipifsilent
