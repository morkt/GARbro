Unicode true
!include "MUI2.nsh"
!define RELEASE_DIR bin\Release

Name "GARbro"
OutFile GARbro-setup.exe

RequestExecutionLevel admin
ShowInstDetails show
BrandingText "$(^Name)"
InstallDir "$PROGRAMFILES\$(^Name)"

Var StartMenuFolder

!define MUI_FINISHPAGE_SHOWREADME
;!define MUI_FINISHPAGE_SHOWREADME $INSTDIR\README.txt
!define MUI_FINISHPAGE_SHOWREADME_TEXT "Create desktop shortcut"
!define MUI_FINISHPAGE_SHOWREADME_FUNCTION CreateDesktopShortCut
!define MUI_FINISHPAGE_SHOWREADME_NOTCHECKED

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_STARTMENU GARbro $StartMenuFolder
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH

; Uninstaller
;!insertmacro MUI_UNPAGE_WELCOME
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_UNPAGE_FINISH

!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Russian"
!insertmacro MUI_LANGUAGE "Korean"
!insertmacro MUI_LANGUAGE "SimpChinese"
!insertmacro MUI_LANGUAGE "Japanese"

!macro InstallSubDir dir
    CreateDirectory $INSTDIR\${dir}
    SetOutPath "$INSTDIR\${dir}"
    File "${RELEASE_DIR}\${dir}\*.*"
!macroend

Function CreateDesktopShortCut
    CreateShortCut "$DESKTOP\$(^Name).lnk" "$INSTDIR\GARbro.GUI.exe"
FunctionEnd

Section "install"
    SetOutPath $INSTDIR

    File "${RELEASE_DIR}\GARbro.GUI.exe"
    File "${RELEASE_DIR}\GARbro.GUI.exe.config"
    File "${RELEASE_DIR}\ArcExtra.dll"
    File "${RELEASE_DIR}\ArcFormats.dll"
    File "${RELEASE_DIR}\ArcFormats.dll.config"
    File "${RELEASE_DIR}\ArcLegacy.dll"
    File "${RELEASE_DIR}\Concentus.dll"
    File "${RELEASE_DIR}\Concentus.Oggfile.dll"
    File "${RELEASE_DIR}\GameRes.dll"
    File "${RELEASE_DIR}\GameRes.dll.config"
    File "${RELEASE_DIR}\ICSharpCode.SharpZipLib.dll"
    File "${RELEASE_DIR}\Microsoft.Deployment.Compression.dll"
    File "${RELEASE_DIR}\Microsoft.Deployment.Compression.Cab.dll"
    File "${RELEASE_DIR}\Microsoft.WindowsAPICodePack.dll"
    File "${RELEASE_DIR}\Microsoft.WindowsAPICodePack.Shell.dll"
    File "${RELEASE_DIR}\NAudio.dll"
    File "${RELEASE_DIR}\Net20.dll"
    File "${RELEASE_DIR}\NVorbis.dll"
    File "${RELEASE_DIR}\System.Data.SQLite.dll"
    File "${RELEASE_DIR}\System.IO.FileSystem.dll"
    File "${RELEASE_DIR}\System.Security.Cryptography.Primitives.dll"
    File "${RELEASE_DIR}\System.Windows.Controls.Input.Toolkit.dll"
    File "${RELEASE_DIR}\WPFToolkit.dll"
    File "${RELEASE_DIR}\README.txt"
    File "${RELEASE_DIR}\LICENSE.txt"
    File "${RELEASE_DIR}\supported.html"

    !insertmacro InstallSubDir GameData
    !insertmacro InstallSubDir ja-JP
    !insertmacro InstallSubDir ko-KR
    !insertmacro InstallSubDir ru-RU
    !insertmacro InstallSubDir zh-Hans
    !insertmacro InstallSubDir x64
    !insertmacro InstallSubDir x86

    SetOutPath $INSTDIR
    WriteUninstaller "$INSTDIR\uninstall.exe"

    !insertmacro MUI_STARTMENU_WRITE_BEGIN GARbro
	CreateDirectory "$SMPROGRAMS\$StartMenuFolder"
	CreateShortCut "$SMPROGRAMS\$StartMenuFolder\$(^Name).lnk" "$INSTDIR\GARbro.GUI.exe"
	CreateShortCut "$SMPROGRAMS\$StartMenuFolder\Read me.lnk" "$INSTDIR\README.txt"
	CreateShortCut "$SMPROGRAMS\$StartMenuFolder\Supported formats.lnk" "$INSTDIR\supported.html"
	CreateShortCut "$SMPROGRAMS\$StartMenuFolder\Uninstall $(^Name).lnk" "$INSTDIR\uninstall.exe"
    !insertmacro MUI_STARTMENU_WRITE_END
SectionEnd

Section "uninstall"
    !insertmacro MUI_STARTMENU_GETFOLDER GARbro $StartMenuFolder
    Delete "$SMPROGRAMS\$StartMenuFolder\$(^Name).lnk"
    Delete "$SMPROGRAMS\$StartMenuFolder\Read me.lnk"
    Delete "$SMPROGRAMS\$StartMenuFolder\Supported formats.lnk"
    Delete "$SMPROGRAMS\$StartMenuFolder\Uninstall $(^Name).lnk"
    RMDir "$SMPROGRAMS\$StartMenuFolder"
    Delete "$DESKTOP\$(^Name).lnk"
    ClearErrors

    Delete $INSTDIR\GARbro.GUI.exe
    Delete $INSTDIR\GARbro.GUI.exe.config
    Delete $INSTDIR\ArcExtra.dll
    Delete $INSTDIR\ArcFormats.dll
    Delete $INSTDIR\ArcFormats.dll.config
    Delete $INSTDIR\ArcLegacy.dll
    Delete $INSTDIR\Concentus.dll
    Delete $INSTDIR\Concentus.Oggfile.dll
    Delete $INSTDIR\GameRes.dll
    Delete $INSTDIR\GameRes.dll.config
    Delete $INSTDIR\ICSharpCode.SharpZipLib.dll
    Delete $INSTDIR\Microsoft.Deployment.Compression.dll
    Delete $INSTDIR\Microsoft.Deployment.Compression.Cab.dll
    Delete $INSTDIR\Microsoft.WindowsAPICodePack.dll
    Delete $INSTDIR\Microsoft.WindowsAPICodePack.Shell.dll
    Delete $INSTDIR\NAudio.dll
    Delete $INSTDIR\NVorbis.dll
    Delete $INSTDIR\System.Data.SQLite.dll
    Delete $INSTDIR\System.IO.FileSystem.dll
    Delete $INSTDIR\System.Security.Cryptography.Primitives.dll
    Delete $INSTDIR\System.Windows.Controls.Input.Toolkit.dll
    Delete $INSTDIR\WPFToolkit.dll
    Delete $INSTDIR\README.txt
    Delete $INSTDIR\LICENSE.txt
    Delete $INSTDIR\supported.html
    Delete $INSTDIR\uninstall.exe
    RMDir /r $INSTDIR\GameData
    RMDir /r $INSTDIR\ja-JP
    RMDir /r $INSTDIR\ko-KR
    RMDir /r $INSTDIR\ru-RU
    RMDir /r $INSTDIR\zh-Hans
    RMDir /r $INSTDIR\x64
    RMDir /r $INSTDIR\x86
    RMDir $INSTDIR
SectionEnd
