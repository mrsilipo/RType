@echo off
setlocal

cd /d "%~dp0"

set "GAME_EXE=bin\Debug\net8.0-windows\RetroRacer.exe"
if not exist "%GAME_EXE%" (
    set "GAME_EXE=bin\Debug\net8.0\RetroRacer.exe"
)

if not exist "%GAME_EXE%" (
    echo Built game not found: %GAME_EXE%
    echo Run dotnet build first.
    exit /b 1
)

"%GAME_EXE%" %*
exit /b %ERRORLEVEL%
