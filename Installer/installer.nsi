; 我的世界俄罗斯方块 —— Windows 安装包脚本（NSIS 3.x）
;
; 编译：Installer\build_installer.bat（或 makensis Installer\installer.nsi）
; 产物：BuildInstaller\MinecraftTetris-Setup-<版本>.exe
; 前置：先跑 BuildScript.BuildWindows，Build\ 里要有 MinecraftTetris.exe + MinecraftTetris_Data\
;
; 几个刻意的选择：
; · RequestExecutionLevel user + 装到 %LOCALAPPDATA%\Programs —— 不弹 UAC，装完就能玩，
;   卸载也不需要管理员。存档在 %USERPROFILE%\AppData\LocalLow\MinecraftTetris，
;   不放在安装目录里，所以「卸载重装」不会丢进度。
; · 卸载时 RMDir /r 只删安装目录，绝不碰存档目录。
; · 只装游戏本体：File 里排除 *.bat / *.log —— Build\ 下那两个 play-portrait.bat 之类是
;   开发时用的启动脚本，不该跟着发布出去。
; · 写文件失败时不再是系统那句「无法打开要写入的文件」，而是给一句中文：
;   绝大多数情况是游戏还开着、exe 被占用。安装前先探一次，探到就直说。

Unicode true
!include "MUI2.nsh"
!include "FileFunc.nsh"

!define APP_NAME   "我的世界俄罗斯方块"
!define APP_ID     "MinecraftTetris"
!define APP_VER    "1.0"
!define APP_EXE    "MinecraftTetris.exe"
!define APP_PUB    "GD-Monody"
!define SRC_DIR    "..\Build"
!define ICON_FILE  "..\docs\icon\app_icon.ico"
!define OUT_FILE   "..\BuildInstaller\MinecraftTetris-Setup-${APP_VER}.exe"

Name "${APP_NAME} ${APP_VER}"
OutFile "${OUT_FILE}"
InstallDir "$LOCALAPPDATA\Programs\${APP_ID}"
InstallDirRegKey HKCU "Software\${APP_ID}" "InstallDir"
RequestExecutionLevel user
SetCompressor /SOLID lzma
BrandingText "${APP_NAME} ${APP_VER}"

VIProductVersion "1.0.0.0"
VIAddVersionKey "ProductName"     "${APP_NAME}"
VIAddVersionKey "FileDescription" "${APP_NAME} 安装程序"
VIAddVersionKey "FileVersion"     "${APP_VER}"
VIAddVersionKey "ProductVersion"  "${APP_VER}"
VIAddVersionKey "CompanyName"     "${APP_PUB}"
VIAddVersionKey "LegalCopyright"  "源码 MIT；Minecraft 素材与方正字体版权另述"

!define MUI_ICON   "${ICON_FILE}"
!define MUI_UNICON "${ICON_FILE}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\${APP_EXE}"
!define MUI_FINISHPAGE_RUN_TEXT "现在开始游戏"

!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_DIRECTORY
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES

!insertmacro MUI_LANGUAGE "SimpChinese"

Function .onInit
  ; 快捷方式写到当前用户，不留公共桌面的痕迹
  SetShellVarContext current

  ; 目标目录里已经有游戏、而且 exe 打不开（被占用）就当场说清楚。
  ; 用「以追加方式打开」来探：正在运行的 exe 会被共享锁挡住，这里就会失败。
  IfFileExists "$INSTDIR\${APP_EXE}" 0 done
    FileOpen $0 "$INSTDIR\${APP_EXE}" a
    IfErrors locked
    FileClose $0
    Goto done
  locked:
    MessageBox MB_ICONSTOP|MB_OK \
      "目标目录里的 ${APP_EXE} 正被占用，装不进去。$\r$\n$\r$\n多半是游戏还开着 —— 请先把它关掉，再重新安装。$\r$\n如果确定没开，就换一个安装目录。"
    Abort
  done:
FunctionEnd

Section "游戏主程序（必需）" SEC_MAIN
  SectionIn RO
  SetOutPath "$INSTDIR"

  ; /nonfatal：单个文件失败不立刻抛系统弹窗，攒到最后统一报，
  ; 这样能给出「是不是游戏还开着」这种有用的提示，而不是一串文件名。
  ;
  ; 另外两样不带：
  ; · *.bat / *.log —— Build\ 下的 play-portrait.bat 之类是开发脚本；
  ; · UnityCrashHandler64.exe —— 只是崩溃报告器，游戏本体不需要它。
  ;   而且它经常被杀软判定成可疑文件、写入直接被拒（实测本机就被卡在这一步），
  ;   不带它反而更容易装成功。
  ClearErrors
  File /nonfatal /r /x *.bat /x *.log /x UnityCrashHandler64.exe "${SRC_DIR}\*.*"
  IfErrors 0 write_ok
    MessageBox MB_ICONSTOP|MB_OK \
      "有些文件没能写入 $INSTDIR。$\r$\n$\r$\n常见原因：$\r$\n· 游戏正在运行（先退出再装）；$\r$\n· 目标目录被别的程序占用，或者没有写权限（换一个目录试试）。"
    Abort
  write_ok:

  WriteUninstaller "$INSTDIR\uninstall.exe"

  CreateDirectory "$SMPROGRAMS\${APP_NAME}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"
  CreateShortcut "$SMPROGRAMS\${APP_NAME}\卸载 ${APP_NAME}.lnk" "$INSTDIR\uninstall.exe"
  CreateShortcut "$DESKTOP\${APP_NAME}.lnk" "$INSTDIR\${APP_EXE}"

  ; 控制面板「应用和功能」里要显示的信息
  WriteRegStr HKCU "Software\${APP_ID}" "InstallDir" "$INSTDIR"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "DisplayName"     "${APP_NAME}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "DisplayVersion"  "${APP_VER}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "Publisher"       "${APP_PUB}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "DisplayIcon"     "$INSTDIR\${APP_EXE}"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "UninstallString" "$INSTDIR\uninstall.exe"
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "NoModify" 1
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "NoRepair" 1

  ${GetSize} "$INSTDIR" "/S=0K" $0 $1 $2
  IntFmt $0 "0x%08X" $0
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}" "EstimatedSize" "$0"
SectionEnd

Section "Uninstall"
  SetShellVarContext current

  Delete "$DESKTOP\${APP_NAME}.lnk"
  RMDir /r "$SMPROGRAMS\${APP_NAME}"

  ; 先删程序再删目录；存档在 LocalLow，不在这里，别误伤
  RMDir /r "$INSTDIR"

  DeleteRegKey HKCU "Software\${APP_ID}"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"
SectionEnd
