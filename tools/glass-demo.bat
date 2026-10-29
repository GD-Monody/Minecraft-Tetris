@echo off
rem 玻璃演示：放下来不碎，之后有方块压到上面才碎
rem 用法：双击本文件，或命令行 tools\glass-demo.bat
setlocal
cd /d "%~dp0.."

if not exist "Build\MinecraftTetris.exe" (
    echo [x] Build\MinecraftTetris.exe not found.
    echo     Unity menu: MinecraftTetris - Build Windows
    pause
    exit /b 1
)

echo Glass demo - when does glass break?
echo.
echo   LEFT   glass placed first, stone dropped on it  -^> glass breaks
echo   MIDDLE stone was already there, glass slides under -^> glass stays
echo   RIGHT  glass on glass                          -^> nothing breaks
echo.
echo   Crush check runs 1.5 s after the field is built.
echo   F12 = save a screenshot, Esc = quit
echo.
echo Starting...
echo.

"Build\MinecraftTetris.exe" -glassdroptest

echo.
echo Demo closed.
pause
