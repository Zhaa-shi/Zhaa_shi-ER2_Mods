@echo off
REM ==============================================================
REM  ER2 Launcher - switch mod state, then start the game
REM
REM  Usage:
REM    launch-er2.bat            open the toggle menu, then launch
REM    launch-er2.bat vanilla    disable ALL mods, then launch
REM    launch-er2.bat all        enable ALL mods, then launch
REM    launch-er2.bat mine       only my own mods, then launch
REM    launch-er2.bat play       no changes, just launch
REM    launch-er2.bat pure       TRUE VANILLA: disable all mods AND turn off
REM                              BepInEx (removes the "unofficially modified"
REM                              warning). Use "restore" to undo.
REM    launch-er2.bat restore    turn BepInEx back on (no game launch)
REM
REM  Note: keep this file ASCII-only. cmd.exe reads .bat as ANSI/GBK,
REM        so non-ASCII comments would be garbled and executed as commands.
REM ==============================================================

setlocal
set "SCRIPT=%~dp0scripts\er2-mods.ps1"
set "MODE=%~1"

REM ---- locate the game -----------------------------------------
if "%ER2_GAME_DIR%"=="" (
    set "GAME=E:\SteamLibrary\steamapps\common\Easy Red 2"
) else (
    set "GAME=%ER2_GAME_DIR%"
)
set "EXE=%GAME%\Easy Red 2.exe"

if not exist "%EXE%" (
    echo.
    echo   Game not found: %EXE%
    echo   Set the ER2_GAME_DIR environment variable to your game folder.
    echo.
    pause
    exit /b 1
)

if not exist "%SCRIPT%" (
    echo.
    echo   Toggle script not found: %SCRIPT%
    echo.
    pause
    exit /b 1
)

REM ---- dispatch by mode ----------------------------------------
if /i "%MODE%"=="play"    goto launch
if /i "%MODE%"=="vanilla" goto do_vanilla
if /i "%MODE%"=="all"     goto do_all
if /i "%MODE%"=="mine"    goto do_mine
if /i "%MODE%"=="pure"    goto do_pure
if /i "%MODE%"=="restore" goto do_restore

REM default: interactive menu, then ask whether to launch
powershell -ExecutionPolicy Bypass -File "%SCRIPT%"
echo.
set /p "GO=Launch the game now? (Y/n): "
if /i "%GO%"=="n" exit /b 0
goto launch

:do_pure
powershell -ExecutionPolicy Bypass -File "%SCRIPT%" -Pure
goto launch

:do_restore
powershell -ExecutionPolicy Bypass -File "%SCRIPT%" -RestoreBepInEx
exit /b 0

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
echo   Launching: %EXE%
start "" "%EXE%"
exit /b 0
