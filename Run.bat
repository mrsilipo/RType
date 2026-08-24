@echo off
setlocal

cd /d "%~dp0"

set "GAME_EXE=bin\Release\net8.0-windows\RType.exe"
set "GAME_DLL=bin\Release\net8.0-windows\RType.dll"
set "DEBUG_GAME_EXE=bin\Debug\net8.0-windows\RType.exe"
set "DEBUG_GAME_DLL=bin\Debug\net8.0-windows\RType.dll"

if exist "%GAME_DLL%" (
    start "" /b dotnet "%GAME_DLL%" %* >nul 2>&1
    exit /b 0
)

if exist "%GAME_EXE%" (
    start "" /b "%GAME_EXE%" %* >nul 2>&1
    exit /b 0
)

if exist "%DEBUG_GAME_DLL%" (
    start "" /b dotnet "%DEBUG_GAME_DLL%" %* >nul 2>&1
    exit /b 0
)

if exist "%DEBUG_GAME_EXE%" (
    start "" /b "%DEBUG_GAME_EXE%" %* >nul 2>&1
    exit /b 0
)

echo Built game not found: %GAME_EXE%
echo Run dotnet build -c Release first.
exit /b 1
