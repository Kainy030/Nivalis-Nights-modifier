param([string]$Trial, [string[]]$EnabledPlugins = @(), [int]$DiagnosticsSeconds = 0, [int]$ObserveSeconds = 45, [string[]]$DisabledGroups = @(), [string]$ExcludedRvas = '', [switch]$Deploy)
$ErrorActionPreference = 'Stop'
$gameRoot = 'D:\Steam\steamapps\common\Nivalis Nights'
$target = Join-Path 'D:\NightsHack\outputs\hook-test-20260930' $Trial
if (Test-Path -LiteralPath $target) { throw 'Trial already exists; use a new name.' }
New-Item -ItemType Directory $target | Out-Null
$running = @(Get-Process -Name 'Nivalis Nights' -ErrorAction SilentlyContinue)
foreach ($p in $running) {
    if ($p.Path -ne (Join-Path $gameRoot 'Nivalis Nights.exe')) { throw 'Unexpected process path.' }
    if (-not $p.CloseMainWindow()) { throw 'Normal close request rejected.' }
    if (-not $p.WaitForExit(30000)) { throw 'Normal close timed out; no force termination.' }
}
foreach ($id in @('playerhook','worldhook','itemhook','gameruntimehook')) {
    $cfg = Join-Path $gameRoot ('BepInEx\config\nightshack.'+$id+'.cfg')
    $content = Get-Content -LiteralPath $cfg -Raw
    $enabled = if ($id -in $EnabledPlugins) { 'true' } else { 'false' }
    $content = [regex]::Replace($content, '(?m)^Enabled\s*=.*$', ('Enabled = '+$enabled))
    $content = [regex]::Replace($content, '(?m)^ExportIntervalSeconds\s*=.*$', ('ExportIntervalSeconds = '+$DiagnosticsSeconds))
    if ($content -match '(?m)^ExcludedRvas\s*=') {
        $content = [regex]::Replace($content, '(?m)^ExcludedRvas\s*=.*$', ('ExcludedRvas = '+$ExcludedRvas))
    } else {
        $content = $content.Replace('[Diagnostics]', "[Diagnostics]`nExcludedRvas = $ExcludedRvas")
    }
    $inGroups = $false
    $content = (($content -split '\r?\n') | ForEach-Object {
        if ($_ -match '^\[(.+)\]') { $inGroups = $Matches[1] -eq 'Groups' }
        if ($inGroups -and $_ -match '^([^#=]+?)\s*=') {
            $groupName = $Matches[1].Trim()
            $groupEnabled = if ($groupName -in $DisabledGroups) { 'false' } else { 'true' }
            $groupName + ' = ' + $groupEnabled
        } else { $_ }
    }) -join "`n"
    Set-Content -LiteralPath $cfg -Value $content
    Copy-Item -LiteralPath $cfg -Destination $target
}
if ($Deploy) {
    Get-ChildItem 'D:\NightsHack\outputs\Hooks' -File | Where-Object Extension -In '.dll','.pdb' | Copy-Item -Destination (Join-Path $gameRoot 'BepInEx\plugins\NightsHack')
}
$started = Get-Date
$p = Start-Process -FilePath (Join-Path $gameRoot 'Nivalis Nights.exe') -WorkingDirectory $gameRoot -PassThru
Write-Output ('Trial '+$Trial+' PID '+$p.Id+' enabled='+($EnabledPlugins -join ',')+' diagnostics='+$DiagnosticsSeconds)
$exited = $p.WaitForExit($ObserveSeconds * 1000)
$p.Refresh()
$result = [pscustomobject]@{Trial=$Trial;PID=$p.Id;Started=$started;Checked=Get-Date;EnabledPlugins=$EnabledPlugins;DiagnosticsSeconds=$DiagnosticsSeconds;Exited=$exited;ExitCode=$(if($exited){$p.ExitCode}else{$null});Responding=$(if(-not $exited){$p.Responding}else{$false})}
$result | ConvertTo-Json | Set-Content (Join-Path $target 'result.json')
Copy-Item -LiteralPath (Join-Path $gameRoot 'BepInEx\LogOutput.log') -Destination $target -ErrorAction SilentlyContinue
Copy-Item -LiteralPath 'C:\Users\Administrator\AppData\LocalLow\ION LANDS\Nivalis Nights\Player.log' -Destination $target -ErrorAction SilentlyContinue
Get-ChildItem -LiteralPath (Join-Path $gameRoot 'BepInEx\diagnostics') -Filter '*.json' -ErrorAction SilentlyContinue | ForEach-Object {
    $r = Get-Content -LiteralPath $_.FullName -Raw | ConvertFrom-Json
    if ($r.ProcessId -eq $p.Id) { Copy-Item -LiteralPath $_.FullName -Destination $target }
}
Get-WinEvent -FilterHashtable @{LogName='Application';StartTime=$started;Id=1000,1001} -ErrorAction SilentlyContinue | Where-Object Message -Match 'Nivalis Nights.exe' | Select-Object TimeCreated,Id,Message | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $target 'events.json')
$result | Format-List
Get-Content (Join-Path $target 'LogOutput.log') -Tail 12
