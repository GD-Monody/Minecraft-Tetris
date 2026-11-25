@echo off
REM Build the MSI installer (WiX 3.x). Usage: Installer\msi\build_msi.bat
REM
REM Requires Build\ (run BuildScript.BuildWindows first) and the WiX toolchain.
REM WiX is used from its binary-only zip, not installed system-wide.
REM
REM Why MSI as well as NSIS: some security suites block unsigned EXEs that create
REM many files at once (ransomware heuristic). MSI file writes are performed by the
REM Windows Installer service (msiexec), not by our exe, so they usually pass.
REM
REM Keep this file ASCII-only: cmd reads .bat in the OEM code page.

setlocal
set "WIX=D:\DeepSeek-Harness\tools\wix311"
set "ROOT=%~dp0..\.."
set "SRC=%ROOT%\Build"
set "OUT=%ROOT%\BuildInstaller"
set "OBJ=%ROOT%\.msi-obj"

if not exist "%WIX%\candle.exe" (
    echo [ERROR] WiX not found at %WIX%
    exit /b 1
)
if not exist "%SRC%\MinecraftTetris.exe" (
    echo [ERROR] Build\MinecraftTetris.exe not found - run BuildWindows first
    exit /b 1
)
if not exist "%OUT%" mkdir "%OUT%"
if not exist "%OBJ%" mkdir "%OBJ%"

REM light.exe builds its temp files under %TEMP%; on machines where that is locked
REM down it dies with UnauthorizedAccessException. Point TMP inside the project.
set "TMP=%OBJ%"
set "TEMP=%OBJ%"

REM heat: list every file under Build\ as an MSI component.
REM -x excludes dev helpers. UnityCrashHandler64.exe is deliberately KEPT here:
REM the write is done by msiexec, not by our exe, so it does not hit the AV rule
REM that blocked the NSIS build - and keeping it matches what Unity ships.
"%WIX%\heat.exe" dir "%SRC%" -cg GameFiles -dr INSTALLFOLDER -gg -g1 -sf -srd ^
    -x *.bat -x *.log ^
    -var var.SrcDir -out "%OBJ%\Files.wxs"
if errorlevel 1 (
    echo [FAILED] heat
    exit /b 1
)

"%WIX%\candle.exe" -nologo -dSrcDir="%SRC%" -out "%OBJ%\\" "%OBJ%\Files.wxs" "%~dp0main.wxs"
if errorlevel 1 (
    echo [FAILED] candle
    exit /b 1
)

"%WIX%\light.exe" -nologo -b "%SRC%" -ext WixUIExtension -sval ^
    -out "%OUT%\MinecraftTetris-1.0.msi" "%OBJ%\main.wixobj" "%OBJ%\Files.wixobj"
if errorlevel 1 (
    echo [FAILED] light
    exit /b 1
)

echo [OK] %OUT%\MinecraftTetris-1.0.msi
endlocal
