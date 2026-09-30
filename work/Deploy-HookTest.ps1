$ErrorActionPreference = 'Stop'
$gameRoot = 'D:\Steam\steamapps\common\Nivalis Nights'
$source = 'D:\NightsHack\work\hook-dependencies\bepinex-6.0.0-pre.2'
$package = 'D:\NightsHack\outputs\Hooks'
$evidence = 'D:\NightsHack\outputs\hook-test-20260930'
New-Item -ItemType Directory -Force $evidence | Out-Null
$expected = @{
    'GameAssembly.dll' = '9A0E32C2D09A5025F867D29BF39B9BEDD0715B513456617FBFD82C581E1A376D'
    'Nivalis Nights_Data\il2cpp_data\Metadata\global-metadata.dat' = 'C8BD44F74B47136AEAD259DC2B88F289C12CB01E083FEEECDCD096A6FC1B2CF9'
}
foreach ($entry in $expected.GetEnumerator()) {
    $actual = (Get-FileHash -LiteralPath (Join-Path $gameRoot $entry.Key) -Algorithm SHA256).Hash
    if ($actual -ne $entry.Value) { throw ('Game input mismatch: ' + $entry.Key) }
    Write-Output ('Verified game SHA256: ' + $entry.Key)
}
foreach ($entry in (Get-Content (Join-Path $package 'build-sha256.json') -Raw | ConvertFrom-Json)) {
    if ((Get-FileHash -LiteralPath $entry.Path -Algorithm SHA256).Hash -ne $entry.Hash) { throw ('Package mismatch: ' + $entry.Path) }
}
$loaderEntries = @('BepInEx','dotnet','.doorstop_version','doorstop_config.ini','winhttp.dll')
foreach ($entry in $loaderEntries) {
    if (Test-Path -LiteralPath (Join-Path $gameRoot $entry)) { throw ('Refusing to overwrite existing loader entry: ' + $entry) }
}
$gameProcess = Get-Process -Id 40208 -ErrorAction Stop
if ($gameProcess.Path -ne (Join-Path $gameRoot 'Nivalis Nights.exe')) { throw 'PID executable identity could not be verified.' }
if (-not $gameProcess.CloseMainWindow()) { throw 'Normal close request was not accepted; no force termination performed.' }
if (-not $gameProcess.WaitForExit(30000)) { throw 'Game did not exit within 30 seconds; no force termination performed.' }
Write-Output 'PID 40208 exited following normal close request.'
foreach ($entry in $loaderEntries) { Copy-Item -LiteralPath (Join-Path $source $entry) -Destination $gameRoot -Recurse }
$plugins = Join-Path $gameRoot 'BepInEx\plugins\NightsHack'
$config = Join-Path $gameRoot 'BepInEx\config'
New-Item -ItemType Directory -Force $plugins,$config | Out-Null
Get-ChildItem -LiteralPath $package -File | Where-Object Extension -In '.dll','.pdb' | Copy-Item -Destination $plugins
foreach ($id in @('playerhook','worldhook','itemhook','gameruntimehook')) {
    Set-Content -LiteralPath (Join-Path $config ('nightshack.' + $id + '.cfg')) -Value "[Diagnostics]`nExportIntervalSeconds = 5`n" -Encoding utf8
}
$manifest = foreach ($entry in $loaderEntries) {
    $path = Join-Path $gameRoot $entry
    Get-ChildItem -LiteralPath $path -Recurse -File | Get-FileHash -Algorithm SHA256 | Select-Object Path,Hash
}
$manifest | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $evidence 'deployment-sha256.json')
Write-Output ('Deployed loader, four plugins, shared runtime and diagnostics configuration. Manifest: ' + $evidence)
