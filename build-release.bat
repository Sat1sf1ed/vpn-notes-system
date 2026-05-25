@echo off
setlocal

if "%~1"=="" (
    echo Usage: build-release.bat ^<version^>
    echo Example: build-release.bat 1.1.0
    exit /b 1
)

set VERSION=%~1

echo ===============================================
echo Building VPN Notes System version %VERSION%
echo ===============================================
echo.

if exist publish rmdir /s /q publish
mkdir publish

echo [1/3] Building Cli...
dotnet publish src\VpnNotes.Cli\VpnNotes.Cli.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:Version=%VERSION% ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o publish ^
    --nologo /v:quiet
if errorlevel 1 (
    echo Build of Cli FAILED
    exit /b 1
)

echo [2/3] Building Watcher...
dotnet publish src\VpnNotes.Watcher\VpnNotes.Watcher.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:Version=%VERSION% ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o publish ^
    --nologo /v:quiet
if errorlevel 1 (
    echo Build of Watcher FAILED
    exit /b 1
)

echo [3/3] Building Updater...
dotnet publish src\VpnNotes.Updater\VpnNotes.Updater.csproj ^
    -c Release ^
    -r win-x64 ^
    --self-contained true ^
    -p:PublishSingleFile=true ^
    -p:Version=%VERSION% ^
    -p:IncludeNativeLibrariesForSelfExtract=true ^
    -o publish ^
    --nologo /v:quiet
if errorlevel 1 (
    echo Build of Updater FAILED
    exit /b 1
)

echo.
echo Copying config template...
copy /Y app-config.template.yml publish\app-config.yml >nul

echo Copying README...
copy /Y README.md publish\README.md >nul

echo.
echo Packaging vpn-notes-%VERSION%.zip...
if exist vpn-notes-%VERSION%.zip del vpn-notes-%VERSION%.zip
powershell -NoProfile -Command "Compress-Archive -Path publish\* -DestinationPath vpn-notes-%VERSION%.zip -Force"

if not exist vpn-notes-%VERSION%.zip (
    echo Failed to create zip archive
    exit /b 1
)

echo.
echo ===============================================
echo SUCCESS: vpn-notes-%VERSION%.zip created
echo ===============================================
echo.
echo Next steps:
echo  1. Go to https://github.com/USER/REPO/releases/new
echo  2. Tag: v%VERSION%
echo  3. Title: Release %VERSION%
echo  4. Attach the file: vpn-notes-%VERSION%.zip
echo  5. Publish release

endlocal