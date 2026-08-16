@echo off
setlocal

set "PUB_DIR=%~dp0pub"

if exist "%PUB_DIR%" (
    echo Cleaning "%PUB_DIR%" ...
    rmdir /s /q "%PUB_DIR%"
    if errorlevel 1 (
        echo Failed to clean pub directory.
        exit /b 1
    )
)

echo Publishing webdav with profile win.pubxml ...
dotnet publish "%~dp0webdav\webdav.csproj" -c Release -p:PublishProfile=win -o "%PUB_DIR%"
if errorlevel 1 (
    echo Publish failed.
    exit /b 1
)

echo Publish succeeded: %PUB_DIR%
endlocal
