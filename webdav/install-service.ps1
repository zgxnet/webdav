#requires -Version 5.1

[CmdletBinding()]
param(
    [ValidatePattern('^[a-zA-Z0-9_.-]+$')]
    [string]$ServiceName = 'WebDav',

    [string]$DisplayName = 'WebDAV Server',

    [string]$Description = 'WebDAV server and web file manager',

    [ValidateSet('Automatic', 'Manual')]
    [string]$StartupType = 'Automatic',

    [switch]$NoStart
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([Environment]::OSVersion.Platform -ne [PlatformID]::Win32NT) {
    throw 'This script can only install a service on Windows.'
}

$currentIdentity = [Security.Principal.WindowsIdentity]::GetCurrent()
$currentPrincipal = [Security.Principal.WindowsPrincipal]::new($currentIdentity)
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Administrator privileges are required. Open PowerShell as administrator and run this script again.'
}

$executablePath = Join-Path $PSScriptRoot 'webdav.exe'
if (-not (Test-Path -LiteralPath $executablePath -PathType Leaf)) {
    throw "Cannot find webdav.exe next to this script: $executablePath"
}
$executablePath = (Resolve-Path -LiteralPath $executablePath).Path

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    throw "Service '$ServiceName' already exists. Remove it or choose another name with -ServiceName."
}

$binaryPath = '"' + $executablePath + '" --windows-service'

New-Service `
    -Name $ServiceName `
    -BinaryPathName $binaryPath `
    -DisplayName $DisplayName `
    -Description $Description `
    -StartupType $StartupType | Out-Null

Write-Host "Installed service '$ServiceName'."
Write-Host "Executable: $executablePath"

if (-not $NoStart) {
    try {
        Start-Service -Name $ServiceName
        Write-Host "Started service '$ServiceName'."
    }
    catch {
        Write-Warning "The service was installed but could not be started: $($_.Exception.Message)"
        throw
    }
}
else {
    Write-Host "The service was not started. Start it with: Start-Service -Name '$ServiceName'"
}
