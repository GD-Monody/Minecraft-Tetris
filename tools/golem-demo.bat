@echo off
rem 傀儡演示：南瓜头压在雪块 / 铁块 / 铜块上召出对应傀儡
rem 用法：双击本文件，或命令行 tools\golem-demo.bat
setlocal
cd /d "%~dp0.."

if not exist "Build\MinecraftTetris.exe" (
    echo [x] Build\MinecraftTetris.exe not found.
    echo     Unity menu: MinecraftTetris - Build Windows
    pause
    exit /b 1
)

echo Golem summoning demo
echo.
echo   Field, left to right - a pumpkin head sitting on:
echo     1. snow block  -^> snow golem
echo     2. iron block  -^> iron golem
echo     3. copper block-^> copper golem
echo     4. stone       -^> nothing (control group)
echo.
echo   The head and the block vanish, the golem shows up on the right,
echo   outside the field. Summon runs 1.5 s after the field is built.
echo   F12 = save a screenshot, Esc = quit
echo.
echo Starting...
echo.

"Build\MinecraftTetris.exe" -golemtest

echo.
echo Demo closed.
pause
