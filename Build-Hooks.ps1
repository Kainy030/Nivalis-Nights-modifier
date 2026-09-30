param([switch]$Restore)
$ErrorActionPreference = 'Stop'
Push-Location $PSScriptRoot
try {
    $names = @('PlayerHook','WorldHook','ItemHook','GameRuntimeHook')
    foreach ($name in $names) {
        $project = "src/NightsHack.$name/NightsHack.$name.csproj"
        if ($Restore) {
            & ./Invoke-Dotnet.ps1 restore $project
            if ($LASTEXITCODE -ne 0) { throw "$name restore failed." }
        }
        & ./Invoke-Dotnet.ps1 build $project -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "$name build failed." }
    }
    foreach ($suite in @('PlayerHook','WorldHooks')) {
        $project = "tests/NightsHack.$suite.Checks/NightsHack.$suite.Checks.csproj"
        if ($Restore) {
            & ./Invoke-Dotnet.ps1 restore $project
            if ($LASTEXITCODE -ne 0) { throw "$suite checks restore failed." }
        }
        & ./Invoke-Dotnet.ps1 run --project $project -c Release --no-restore
        if ($LASTEXITCODE -ne 0) { throw "$suite managed/static checks failed." }
    }
    # One canonical package, plus refreshed legacy package locations. Never write the game directory.
    foreach ($package in @('Hooks','PlayerHook','WorldHooks')) {
        $target = Join-Path $PSScriptRoot "outputs/$package"
        New-Item -ItemType Directory -Force $target | Out-Null
        $members = if ($package -eq 'PlayerHook') { @('PlayerHook') }
            elseif ($package -eq 'WorldHooks') { @('WorldHook','ItemHook','GameRuntimeHook') } else { $names }
        foreach ($name in $members) {
            foreach ($ext in @('dll','pdb')) {
                Copy-Item -LiteralPath "src/NightsHack.$name/bin/Release/net6.0/$name.$ext" -Destination $target
            }
            $catalog = if ($name -eq 'PlayerHook') { 'PlayerCatalog.json' } else { 'Catalog.json' }
            $destination = if ($package -eq 'PlayerHook') { 'PlayerCatalog.json' } else { "$name.Catalog.json" }
            Copy-Item -LiteralPath "src/NightsHack.$name/$catalog" -Destination (Join-Path $target $destination)
        }
        foreach ($ext in @('dll','pdb')) {
            Copy-Item -LiteralPath "src/NightsHack.HookRuntime/bin/Release/net6.0/NightsHack.HookRuntime.$ext" -Destination $target
        }
        $readme = if ($package -eq 'PlayerHook') { 'src/NightsHack.PlayerHook/README.md' } else { 'src/NightsHack.HookRuntime/README.md' }
        Copy-Item -LiteralPath $readme -Destination $target
        Copy-Item -LiteralPath 'outputs/REFERENCE.md','outputs/HOOKS-REFERENCE.md' -Destination $target
        if ($package -ne 'PlayerHook') {
            Copy-Item -LiteralPath 'work/world-investigation/hook-coverage-audit.json' -Destination $target
        }
        Get-ChildItem -LiteralPath $target -Filter '*.dll' | Get-FileHash -Algorithm SHA256 |
            Select-Object Hash,Path | ConvertTo-Json | Set-Content (Join-Path $target 'build-sha256.json')
    }
    Write-Output 'Four plugins share HookRuntime. Built and checked; not deployed or verified in-game.'
} finally { Pop-Location }
