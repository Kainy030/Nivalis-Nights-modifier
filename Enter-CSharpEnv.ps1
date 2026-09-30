# Dot-source this file: . .\Enter-CSharpEnv.ps1
$env:DOTNET_ROOT = Join-Path $PSScriptRoot '.tools/dotnet'
$env:DOTNET_ROOT_X64 = $env:DOTNET_ROOT
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot 'work/dotnet-home'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot 'work/nuget-packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
$env:DOTNET_CLI_WORKLOAD_UPDATE_NOTIFY_DISABLE = 'true'
if (($env:PATH -split ';') -notcontains $env:DOTNET_ROOT) {
    $env:PATH = $env:DOTNET_ROOT + ';' + $env:PATH
}
