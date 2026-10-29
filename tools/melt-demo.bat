@echo off
rem 雪 / 冰融化演示：贴着岩浆就化成水，水再被岩浆烫成黑曜石
rem 用法：双击本文件，或命令行 tools\melt-demo.bat
setlocal
cd /d "%~dp0.."

if not exist "Build\MinecraftTetris.exe" (
    echo [x] Build\MinecraftTetris.exe not found.
    echo     Unity menu: MinecraftTetris - Build Windows
    pause
    exit /b 1
)

echo Snow / ice melting demo
echo.
echo   LEFT   snow block over a lava pool  -^> melts to water, lava turns it to obsidian
echo   MIDDLE ice block over a lava pool   -^> same
echo   RIGHT  snow block, pool left empty  -^> nothing happens (control group)
echo.
echo   Lava is poured 1.5 s after the field is built.
echo   F12 = save a screenshot, Esc = quit
echo.
echo Starting...
echo.

"Build\MinecraftTetris.exe" -melttest

echo.
echo Demo closed.
pause
