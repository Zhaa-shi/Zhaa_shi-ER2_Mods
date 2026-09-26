@echo off
REM ══════════════════════════════════════════════════════════════
REM  ER2 启动器 —— 先切换 mod 状态，再启动游戏
REM
REM  用法：
REM    启动ER2.bat              打开切换菜单（选完再启动）
REM    启动ER2.bat vanilla      纯原版启动（禁用全部 mod）
REM    启动ER2.bat all          全部 mod 启动
REM    启动ER2.bat mine         只启用自己开发的 mod
REM    启动ER2.bat play         不改动，直接启动
REM ══════════════════════════════════════════════════════════════

setlocal
set "SCRIPT=%~dp0scripts\er2-mods.ps1"
set "MODE=%~1"

REM ── 游戏可执行文件 ──────────────────────────────────────────
if "%ER2_GAME_DIR%"=="" (
    set "GAME=E:\SteamLibrary\steamapps\common\Easy Red 2"
) else (
    set "GAME=%ER2_GAME_DIR%"
)
set "EXE=%GAME%\Easy Red 2.exe"

if not exist "%EXE%" (
    echo.
    echo   找不到游戏: %EXE%
    echo   请设置环境变量 ER2_GAME_DIR 指向游戏目录。
    echo.
    pause
    exit /b 1
)

REM ── 按模式切换 ──────────────────────────────────────────────
if /i "%MODE%"=="play"    goto launch
if /i "%MODE%"=="vanilla" goto do_vanilla
if /i "%MODE%"=="all"     goto do_all
if /i "%MODE%"=="mine"    goto do_mine

REM 默认：打开交互菜单
powershell -ExecutionPolicy Bypass -File "%SCRIPT%"
echo.
set /p "GO=现在启动游戏？(Y/n): "
if /i "%GO%"=="n" exit /b 0
goto launch

:do_vanilla
powershell -ExecutionPolicy Bypass -File "%SCRIPT%" -Vanilla
goto launch

:do_all
powershell -ExecutionPolicy Bypass -File "%SCRIPT%" -All
goto launch

:do_mine
powershell -ExecutionPolicy Bypass -File "%SCRIPT%" -OnlyMine
goto launch

:launch
echo.
echo   启动游戏: %EXE%
start "" "%EXE%"
exit /b 0
