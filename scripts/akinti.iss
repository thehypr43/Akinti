; Akıntı kurulum betigi (Inno Setup 6).
;
; Kurulum kullanici hesabina yapilir: yonetici istemez, UAC penceresi cikmaz.
; Dosyalari Inno kopyalar; motor ayari, tarayici koprusu ve zamanlanmis gorev
; gibi isler kurulum sonunda install.ps1 -ConfigureOnly ile GIZLI calisir,
; boylece siyah bir konsol penceresi acilmaz.
;
; Derlemek icin: scripts\make-package.ps1 (ISCC.exe'yi kendisi bulur)

#define AppName    "Akıntı"
#define AppVersion "1.0.0"
#define AppExe     "Akinti.exe"

[Setup]
AppId={{7C3A6F41-5C2E-4E3B-9A1D-3B9F6C2A81D4}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Burak
AppPublisherURL=https://github.com/
DefaultDirName={localappdata}\Programs\Akinti
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir={#OutputDirectory}
OutputBaseFilename=Akinti-Kurulum
SetupIconFile={#SourceRoot}\src\Akinti\akinti.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
WizardStyle=modern
Compression=lzma2/max
SolidCompression=yes
InfoAfterFile={#SourceRoot}\scripts\setup-info.txt
CloseApplications=yes
CloseApplicationsFilter=Akinti.exe,aria2c.exe

[Languages]
Name: "tr"; MessagesFile: "compiler:Languages\Turkish.isl"

[Tasks]
Name: "desktopicon"; Description: "Masaüstüne kısayol ekle"; GroupDescription: "Kısayollar:"
Name: "autostart";   Description: "İndirme motorunu Windows açılışında başlat"; GroupDescription: "Kısayollar:"

[Files]
Source: "{#SourceRoot}\publish\*";           DestDir: "{app}";            Flags: recursesubdirs ignoreversion
Source: "{#SourceRoot}\tools\aria2c.exe";    DestDir: "{app}\tools";      Flags: ignoreversion
Source: "{#SourceRoot}\extension\chrome\*";  DestDir: "{app}\extension";  Flags: recursesubdirs ignoreversion
Source: "{#SourceRoot}\scripts\*.ps1";       DestDir: "{app}\scripts";    Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";                  Filename: "{app}\{#AppExe}"
Name: "{group}\Eklenti kimliğini bağla";     Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\install.ps1"" -ConfigureOnly"; \
    IconFilename: "{app}\{#AppExe}"; Comment: "Chrome eklentisini yükledikten sonra bir kez çalıştır"
Name: "{group}\{#AppName} - Kaldır";         Filename: "{uninstallexe}"
Name: "{userdesktop}\{#AppName}";            Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
; Ayarlar, motor ve tarayici koprusu. runhidden: konsol penceresi gorunmez.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\install.ps1"" -ConfigureOnly"; \
    StatusMsg: "Motor ve tarayıcı köprüsü ayarlanıyor..."; Flags: runhidden waituntilterminated

Filename: "{app}\{#AppExe}"; Description: "{#AppName}'yı başlat"; \
    Flags: nowait postinstall skipifsilent

[UninstallRun]
; Zamanlanmis gorev, tarayici koprusu kaydi ve calisan motor. Dosyalari Inno siler.
Filename: "powershell.exe"; \
    Parameters: "-NoProfile -ExecutionPolicy Bypass -File ""{app}\scripts\uninstall.ps1"" -KeepData"; \
    Flags: runhidden waituntilterminated; RunOnceId: "AkintiCleanup"

[Code]
// "Windows acilisinda motoru baslat" secilmediyse kurulumun olusturdugu
// zamanlanmis gorev kaldirilir. Kullanicinin isaretlemedigi bir sey
// arka planda calismaya devam etmesin.
procedure CurStepChanged(CurStep: TSetupStep);
var
  ResultCode: Integer;
begin
  if (CurStep = ssPostInstall) and (not WizardIsTaskSelected('autostart')) then
    Exec('schtasks.exe', '/Delete /TN "Akıntı Motoru" /F', '', SW_HIDE,
         ewWaitUntilTerminated, ResultCode);
end;
