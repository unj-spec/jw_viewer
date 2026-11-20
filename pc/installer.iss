; 소설 뷰어 PC 버전 설치 파일 (Inno Setup 6). pc\build.ps1 이 부른다.
; 관리자 권한 없이 사용자 폴더(%LOCALAPPDATA%\Programs\NovelViewer)에 설치한다.
; 설정·책갈피·GitHub 연결은 %APPDATA%\NovelViewer\pc.json 에 있고, 지워도 남겨 둔다(다시 설치하면 이어서 씀).

#define AppName "소설 뷰어"
#ifndef AppVer
  #define AppVer "0.0.0"
#endif
; AppVer 는 build.ps1 이 저장소 맨 위 VERSION 파일 값을 넘겨준다.

[Setup]
AppId={{8C1B2E64-5F0A-4C7D-9B3E-6A1D2F4E7B90}
AppName={#AppName}
AppVersion={#AppVer}
AppPublisher=소설 뷰어
DefaultDirName={localappdata}\Programs\NovelViewer
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=yes
PrivilegesRequired=lowest
OutputDir=..
OutputBaseFilename=소설뷰어_PC_설치
SetupIconFile=build\icon.ico
UninstallDisplayIcon={app}\소설뷰어.exe
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "ko"; MessagesFile: "compiler:Languages\Korean.isl"

[Tasks]
Name: "desktopicon"; Description: "바탕화면에 바로가기 만들기"

[InstallDelete]
; 예전 시험판(브라우저 창 방식)이 남긴 파일
Type: files; Name: "{app}\소설뷰어.apk"

[Files]
Source: "build\NovelViewer.exe"; DestDir: "{app}"; DestName: "소설뷰어.exe"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\소설뷰어.exe"
Name: "{group}\{#AppName} 지우기"; Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}"; Filename: "{app}\소설뷰어.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\소설뷰어.exe"; Description: "소설 뷰어 실행"; Flags: nowait postinstall
; 자동 업데이트(/SILENT)로 깔았을 때도 다시 켜지게 skipifsilent 는 붙이지 않는다.
