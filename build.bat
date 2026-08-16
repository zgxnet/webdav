@echo off
setlocal

set "PUB_DIR=%~dp0pub"
set "HAS_NET10_SDK="

where dotnet >nul 2>&1
if errorlevel 1 (
    echo Error: dotnet was not found in PATH.
    echo Install the .NET 10 SDK ^(not only the Runtime or Hosting Bundle^) and try again.
    echo Download: https://dotnet.microsoft.com/download/dotnet/10.0
    exit /b 1
)

for /f "tokens=1" %%V in ('dotnet --list-sdks 2^>nul') do (
    echo %%V | findstr /r /b "1[0-9]\." >nul && set "HAS_NET10_SDK=1"
)

if not defined HAS_NET10_SDK (
    echo Error: a compatible .NET SDK was not found.
    echo This project targets net10.0 and requires the .NET 10 SDK or later.
    echo Installing only the .NET Runtime, ASP.NET Core Runtime, or Hosting Bundle is not sufficient.
    echo.
    echo dotnet in use:
    where dotnet
    echo.
    echo Installed SDKs:
    dotnet --list-sdks
    echo.
    echo Download: https://dotnet.microsoft.com/download/dotnet/10.0
    exit /b 1
)

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
