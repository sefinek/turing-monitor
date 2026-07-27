@echo off
setlocal enabledelayedexpansion

set "PROJECT=%~dp0TuringMonitor.csproj"
set "CONFIGURATION=Release"
set "PUBLISH_DIR=%~dp0bin\Publish"
set "SEVENZIP=C:\Program Files\7-Zip\7z.exe"

if not exist "%SEVENZIP%" (
    for /f "delims=" %%P in ('where 7z.exe 2^>nul') do set "SEVENZIP=%%P"
)
if not exist "%SEVENZIP%" (
    echo [ERROR] 7-Zip not found. Install it, or edit SEVENZIP at the top of this script.
    exit /b 1
)

for /f "tokens=3 delims=<>" %%V in ('findstr /r "<Version>" "%PROJECT%"') do set "VERSION=%%V"
if not defined VERSION set "VERSION=0.0.0.0"

set "ZIP_PATH=%~dp0TuringMonitor-%VERSION%.zip"

echo ============================================
echo  Building TuringMonitor %VERSION% (%CONFIGURATION%)
echo ============================================

if exist "%PUBLISH_DIR%" rd /s /q "%PUBLISH_DIR%"

dotnet publish "%PROJECT%" -c %CONFIGURATION% -o "%PUBLISH_DIR%" --self-contained false
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

copy /y "%~dp0README.md" "%PUBLISH_DIR%\" >nul
copy /y "%~dp0LICENSE" "%PUBLISH_DIR%\" >nul

if exist "%ZIP_PATH%" del /f /q "%ZIP_PATH%"

echo.
echo ============================================
echo  Packing into %ZIP_PATH%
echo ============================================

"%SEVENZIP%" a -tzip "%ZIP_PATH%" "%PUBLISH_DIR%\*"
if errorlevel 1 (
    echo [ERROR] Packing failed.
    exit /b 1
)

echo.
echo Done: %ZIP_PATH%

endlocal
