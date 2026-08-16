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
dotnet msbuild "%~dp0webdav\webdav.csproj" -p:DeployOnBuild=true -p:Configuration=Release -p:PublishProfile=win
if errorlevel 1 (
    echo Publish failed.
    exit /b 1
)

echo Publish succeeded: %PUB_DIR%
endlocal
