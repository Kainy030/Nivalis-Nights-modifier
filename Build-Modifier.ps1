param(
    [ValidatePattern('^v[0-9]+[.][0-9]+([.][0-9]+)?[-A-Za-z0-9.]*$')][string]$Version = 'v0.6-Dev',
    [string]$Zig = (Join-Path $PSScriptRoot '.tools/zig/zig-x86_64-windows-0.14.1/zig.exe'),
    [switch]$SkipHooks
)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    if (!(Test-Path -LiteralPath $Zig)) { throw 'Install the verified official Zig 0.14.1 Windows x64 archive under .tools/zig, or supply -Zig.' }
    if (!$SkipHooks) { & ./Build-Hooks.ps1; if ($LASTEXITCODE -ne 0) { throw 'Hook build failed' } }
    & ./Invoke-Dotnet.ps1 restore src/NightsHack.Bootstrap/NightsHack.Bootstrap.csproj --source work/nuget-packages
    if ($LASTEXITCODE -ne 0) { throw 'Offline bootstrap restore failed' }
    & ./Invoke-Dotnet.ps1 build src/NightsHack.Bootstrap/NightsHack.Bootstrap.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Bootstrap build failed' }
    & ./Invoke-Dotnet.ps1 restore tests/NightsHack.Modifier.Checks/NightsHack.Modifier.Checks.csproj --source work/nuget-packages
    if ($LASTEXITCODE -ne 0) { throw 'Modifier checks restore failed' }
    & ./Invoke-Dotnet.ps1 run --project tests/NightsHack.Modifier.Checks/NightsHack.Modifier.Checks.csproj -c Release --no-restore
    if ($LASTEXITCODE -ne 0) { throw 'Modifier checks failed' }
    $release = Join-Path $PSScriptRoot "outputs/BuildRelease/$Version"
    $destination = Join-Path $PSScriptRoot ("work/modifier-build/" + $Version + '-' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Force $destination | Out-Null
    & ./Invoke-Dotnet.ps1 restore src/NightsHack.Modifier/NightsHack.Modifier.csproj --source work/nuget-packages
    if ($LASTEXITCODE -ne 0) { throw 'Offline GUI restore failed' }
    & ./Invoke-Dotnet.ps1 publish src/NightsHack.Modifier/NightsHack.Modifier.csproj -c Release -r win-x64 --self-contained false --no-restore -o $destination
    if ($LASTEXITCODE -ne 0) { throw 'GUI publish failed' }
    # Package the two donation images only when this release script is explicitly run.
    $donationImages = @(Get-ChildItem -LiteralPath 'D:\Pay' -File |
        Where-Object { $_.Extension.ToLowerInvariant() -in @('.png', '.jpg', '.jpeg', '.bmp', '.gif') } | Sort-Object Name)
    if ($donationImages.Count -ne 2) { throw 'D:\Pay must contain exactly two supported donation images (PNG/JPEG/BMP/GIF).' }
    $donationDestination = Join-Path $destination 'assets/donation'
    New-Item -ItemType Directory -Force $donationDestination | Out-Null
    for ($i = 0; $i -lt 2; $i++) {
        Copy-Item -LiteralPath $donationImages[$i].FullName -Destination (Join-Path $donationDestination ("donation-{0}.image" -f ($i + 1)))
    }
    # Private runtime layout recognized by the SDK-generated apphost. No machine-wide .NET installation is needed.
    $localRuntime = Join-Path $destination 'runtime'
    New-Item -ItemType Directory -Force "$localRuntime/shared", "$localRuntime/host" | Out-Null
    Copy-Item -LiteralPath '.tools/dotnet/shared/Microsoft.NETCore.App', '.tools/dotnet/shared/Microsoft.WindowsDesktop.App' -Destination "$localRuntime/shared" -Recurse
    Copy-Item -LiteralPath '.tools/dotnet/host/fxr' -Destination "$localRuntime/host" -Recurse
    Copy-Item -LiteralPath '.tools/dotnet/LICENSE.txt', '.tools/dotnet/ThirdPartyNotices.txt' -Destination $localRuntime
    $bootstrap = Join-Path $destination 'payload/bootstrap'
    New-Item -ItemType Directory -Force $bootstrap | Out-Null
    $savedZigCache = $env:ZIG_GLOBAL_CACHE_DIR
    try {
        $env:ZIG_GLOBAL_CACHE_DIR = Join-Path $PSScriptRoot 'work/zig-cache'
        & $Zig cc -target x86_64-windows-gnu -O2 -Wall -Wextra -Werror -shared src/NightsHack.Native/bootstrap.c -o "$bootstrap/NightsHack.Native.dll" -luser32 -lkernel32
    } finally { $env:ZIG_GLOBAL_CACHE_DIR = $savedZigCache }
    if ($LASTEXITCODE -ne 0) { throw 'Native bootstrap build failed' }
    Copy-Item -LiteralPath 'src/NightsHack.Bootstrap/bin/Release/net6.0-windows/NightsHack.Bootstrap.dll' -Destination $bootstrap
    $runtime = Join-Path $destination 'payload/runtime'
    $dependency = Join-Path $PSScriptRoot 'work/hook-dependencies/bepinex-6.0.0-pre.2'
    New-Item -ItemType Directory -Force "$runtime/BepInEx", "$destination/payload/hooks" | Out-Null
    Copy-Item -LiteralPath "$dependency/BepInEx/core" -Destination "$runtime/BepInEx" -Recurse
    Copy-Item -LiteralPath "$dependency/dotnet" -Destination $runtime -Recurse
    foreach ($name in @('PlayerHook','WorldHook','ItemHook','GameRuntimeHook','NightsHack.HookRuntime')) {
        Copy-Item -LiteralPath "outputs/Hooks/$name.dll" -Destination "$destination/payload/hooks"
    }
    $manifest = [ordered]@{}
    Get-ChildItem -LiteralPath "$destination/payload" -File -Recurse | Sort-Object FullName | ForEach-Object {
        $relative = [IO.Path]::GetRelativePath($destination, $_.FullName).Replace([char]92, [char]47)
        $manifest[$relative] = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    }
    $manifest | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath "$destination/payload.sha256.json" -Encoding utf8
    Copy-Item -LiteralPath 'src/NightsHack.Modifier/README.md' -Destination "$destination/README.md"
    Copy-Item -LiteralPath 'LICENSE' -Destination "$destination/LICENSE"
    [ordered]@{ Version=$Version; BuiltAt=[DateTimeOffset]::Now.ToString('o'); Architecture='win-x64'; Entry='NivalisNightsTrainer.exe'; PayloadFiles=$manifest.Count } |
        ConvertTo-Json | Set-Content -LiteralPath "$destination/build.json" -Encoding utf8
    # Publish only a completed package. Keep the previous completed build outside the delivery directory.
    New-Item -ItemType Directory -Force (Split-Path $release), 'work/modifier-build-history' | Out-Null
    $backup = $null
    if (Test-Path -LiteralPath $release) {
        $backup = Join-Path $PSScriptRoot ('work/modifier-build-history/' + $Version + '-' + [Guid]::NewGuid().ToString('N'))
        Move-Item -LiteralPath $release -Destination $backup
    }
    try { Move-Item -LiteralPath $destination -Destination $release }
    catch { if ($backup) { Move-Item -LiteralPath $backup -Destination $release }; throw }
    Write-Output "Built modifier: $release/NivalisNightsTrainer.exe"
} finally { Pop-Location }
