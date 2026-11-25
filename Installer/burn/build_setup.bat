@echo off
REM Build the outer Setup.exe (Burn bootstrapper) that bundles the MSI.
REM Requires BuildInstaller\MinecraftTetris-1.0.msi (run Installer\msi\build_msi.bat first).
REM
REM Keep this file ASCII-only: cmd reads .bat in the OEM code page.

setlocal
set "WIX=D:\DeepSeek-Harness\tools\wix311"
set "ROOT=%~dp0..\.."
set "ART=%ROOT%\Installer\msi\art"
set "MSI=%ROOT%\BuildInstaller\MinecraftTetris-1.0.msi"
set "OUT=%ROOT%\BuildInstaller"
set "OBJ=%ROOT%\.msi-obj"

if not exist "%WIX%\candle.exe" (
    echo [ERROR] WiX not found at %WIX%
    exit /b 1
)
if not exist "%MSI%" (
    echo [ERROR] MSI not found - run Installer\msi\build_msi.bat first
    exit /b 1
)
if not exist "%OBJ%" mkdir "%OBJ%"
set "TMP=%OBJ%"
set "TEMP=%OBJ%"

"%WIX%\candle.exe" -nologo -ext WixBalExtension ^
    -dArtDir="%ART%" -dMsiPath="%MSI%" -out "%OBJ%\\" "%~dp0bundle.wxs"
if errorlevel 1 (
    echo [FAILED] candle bundle
    exit /b 1
)

"%WIX%\light.exe" -nologo -ext WixBalExtension -ext WixUtilExtension -sval ^
    -loc "%ROOT%\Installer\msi\WixUI_zh-CN.wxl" -cultures:zh-CN ^
    -out "%OUT%\MinecraftTetris-Setup-1.0.exe" "%OBJ%\bundle.wixobj"
if errorlevel 1 (
    echo [FAILED] light bundle
    exit /b 1
)

echo [OK] %OUT%\MinecraftTetris-Setup-1.0.exe
endlocal
