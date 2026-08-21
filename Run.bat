@echo off
setlocal

cd /d "%~dp0"

set "GAME_EXE=bin\Debug\net8.0-windows\RetroRacer.exe"
set "GAME_DLL=bin\Debug\net8.0-windows\RetroRacer.dll"

if exist "%GAME_DLL%" (
    dotnet "%GAME_DLL%" %*
    exit /b %ERRORLEVEL%
)

if not exist "%GAME_EXE%" (
    echo Built game not found: %GAME_EXE%
    echo Run dotnet build first.
    exit /b 1
)

"%GAME_EXE%" %*
exit /b %ERRORLEVEL%
