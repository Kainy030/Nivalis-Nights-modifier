# Example: & .\Invoke-Dotnet.ps1 build path/to/project.csproj
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Enter-CSharpEnv.ps1')
$savedAppData = $env:APPDATA
$savedLocalAppData = $env:LOCALAPPDATA
try {
    $env:APPDATA = Join-Path $PSScriptRoot 'work/dotnet-appdata'
    $env:LOCALAPPDATA = Join-Path $PSScriptRoot 'work/dotnet-localappdata'
    New-Item -ItemType Directory -Force $env:APPDATA, $env:LOCALAPPDATA | Out-Null
    & (Join-Path $env:DOTNET_ROOT 'dotnet.exe') @args
    $result = $LASTEXITCODE
} finally {
    $env:APPDATA = $savedAppData
    $env:LOCALAPPDATA = $savedLocalAppData
}
exit $result
