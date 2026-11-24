@echo off
REM Build the Windows installer. Usage: Installer\build_installer.bat
REM
REM Requires Build\ to be present (run BuildScript.BuildWindows first).
REM NSIS is unpacked locally, not on PATH; change the NSIS line if you move it.
REM
REM NOTE: keep this file ASCII-only. cmd.exe reads .bat in the OEM code page,
REM so UTF-8 Chinese comments here would be parsed as garbage and break lines.

setlocal
set "NSIS=D:\DeepSeek-Harness\tools\nsis\makensis.exe"
if not exist "%NSIS%" (
    echo [ERROR] makensis.exe not found: %NSIS%
    echo Install NSIS 3.x or point the NSIS variable above at the real path.
    exit /b 1
)

REM Point TMP into the project: in some environments %TEMP% cannot host the
REM compiler's mmap and NSIS dies with "internal compiler error #12345".
set "TMP=%~dp0..\.msi-tmp"
set "TEMP=%TMP%"
if not exist "%TMP%" mkdir "%TMP%"
if not exist "%~dp0..\BuildInstaller" mkdir "%~dp0..\BuildInstaller"

"%NSIS%" "%~dp0installer.nsi"
if errorlevel 1 (
    echo [FAILED] installer was not produced
    exit /b 1
)
echo [OK] output is in BuildInstaller\
endlocal
