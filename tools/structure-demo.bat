@echo off
rem 结构方块演示：整场记忆，还原时先一格一格收掉，再一格一格掉回来
rem 用法：双击本文件，或命令行 tools\structure-demo.bat
setlocal
cd /d "%~dp0.."

if not exist "Build\MinecraftTetris.exe" (
    echo [x] Build\MinecraftTetris.exe not found.
    echo     Unity menu: MinecraftTetris - Build Windows
    pause
    exit /b 1
)

echo Structure block demo
echo   TNT blows up the structure block. The field then empties cell by cell
echo   from bottom to top, and the remembered blocks drop back one by one.
echo   F5 = replay, F12 = save a screenshot, Esc = quit
echo.
echo Starting...
echo.

"Build\MinecraftTetris.exe" -structuretest

echo.
echo Demo closed.
pause
