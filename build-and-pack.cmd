@echo off
setlocal enabledelayedexpansion

set "PROJECT=%~dp0TuringMonitor.csproj"
set "CONFIGURATION=Release"
set "RUNTIME=win-x64"
set "PUBLISH_DIR_FDE=%~dp0bin\Publish"
set "PUBLISH_DIR_SC=%~dp0bin\Publish-standalone"
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

set "ZIP_PATH_FDE=%~dp0TuringMonitor-%VERSION%.zip"
set "ZIP_PATH_SC=%~dp0TuringMonitor-%VERSION%-standalone.zip"

echo ============================================
echo  Building TuringMonitor %VERSION% (%CONFIGURATION%, framework-dependent)
echo ============================================

if exist "%PUBLISH_DIR_FDE%" rd /s /q "%PUBLISH_DIR_FDE%"

dotnet publish "%PROJECT%" -c %CONFIGURATION% -o "%PUBLISH_DIR_FDE%" --self-contained false
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

copy /y "%~dp0README.md" "%PUBLISH_DIR_FDE%\" >nul
copy /y "%~dp0LICENSE" "%PUBLISH_DIR_FDE%\" >nul

if exist "%ZIP_PATH_FDE%" del /f /q "%ZIP_PATH_FDE%"

echo.
echo ============================================
echo  Packing into %ZIP_PATH_FDE%
echo ============================================

"%SEVENZIP%" a -tzip "%ZIP_PATH_FDE%" "%PUBLISH_DIR_FDE%\*"
if errorlevel 1 (
    echo [ERROR] Packing failed.
    exit /b 1
)

echo.
echo ============================================
echo  Building TuringMonitor %VERSION% (%CONFIGURATION%, standalone, .NET %RUNTIME% runtime included)
echo ============================================

if exist "%PUBLISH_DIR_SC%" rd /s /q "%PUBLISH_DIR_SC%"

dotnet publish "%PROJECT%" -c %CONFIGURATION% -o "%PUBLISH_DIR_SC%" -r %RUNTIME% --self-contained true -p:RestoreAdditionalProjectSources=https://api.nuget.org/v3/index.json
if errorlevel 1 (
    echo [ERROR] Build failed.
    exit /b 1
)

copy /y "%~dp0README.md" "%PUBLISH_DIR_SC%\" >nul
copy /y "%~dp0LICENSE" "%PUBLISH_DIR_SC%\" >nul

if exist "%ZIP_PATH_SC%" del /f /q "%ZIP_PATH_SC%"

echo.
echo ============================================
echo  Packing into %ZIP_PATH_SC%
echo ============================================

"%SEVENZIP%" a -tzip "%ZIP_PATH_SC%" "%PUBLISH_DIR_SC%\*"
if errorlevel 1 (
    echo [ERROR] Packing failed.
    exit /b 1
)

echo.
echo Done:
echo   %ZIP_PATH_FDE%  (requires .NET Desktop Runtime installed)
echo   %ZIP_PATH_SC%  (standalone, .NET runtime included)

endlocal
