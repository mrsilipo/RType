@echo off
setlocal

cd /d "%~dp0"

set "PROJECT=RType.csproj"
set "CONFIG=Release"
set "OUTPUT_DIR=bin\%CONFIG%\net8.0-windows"
set "GAME_EXE=%OUTPUT_DIR%\RType.exe"
set "LOG_DIR=%OUTPUT_DIR%\Logs"
set "BUILD_LOG=%LOG_DIR%\run_build.log"
set "RUN_LOG=%LOG_DIR%\last_run.log"

if not exist "%LOG_DIR%" mkdir "%LOG_DIR%"

dotnet build "%PROJECT%" -c "%CONFIG%" --nologo > "%BUILD_LOG%" 2>&1
if errorlevel 1 (
    echo RType build failed. Details:
    type "%BUILD_LOG%"
    exit /b 1
)

if not exist "%GAME_EXE%" (
    echo Built game not found: %GAME_EXE%
    echo Build log: %BUILD_LOG%
    exit /b 1
)

start "" "%GAME_EXE%" %* > "%RUN_LOG%" 2>&1
exit /b 0
