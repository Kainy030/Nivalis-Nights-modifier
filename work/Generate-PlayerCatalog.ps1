$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
[void][Reflection.Assembly]::LoadFrom((Resolve-Path 'work/hook-dependencies/bepinex-6.0.0-pre.2/BepInEx/core/Mono.Cecil.dll'))
$assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path 'work/il2cpp-validation-v1.1/DummyDll/Assembly-CSharp.dll'))
function Flatten($types) { foreach ($t in $types) { $t; Flatten $t.NestedTypes } }
$all = @(Flatten $assembly.MainModule.Types)
$groups = [ordered]@{
    'Nivalis.PlayerManager' = 'Lifecycle'
    'Nivalis.PlayerManager/Player' = 'Player'
    'Nivalis.PlayerState' = 'Stats'
    'Nivalis.PlayerCharacter' = 'Character'
    'Nivalis.PlayerCharacterController' = 'Movement'
    'Nivalis.PlayerInteraction' = 'Interaction'
    'Nivalis.FocusRaycaster' = 'Focus'
    'Nivalis.PlayerObjectHolder' = 'Holding'
    'Nivalis.PlacementSystem' = 'Placement'
    'Nivalis.PlayerHandsAnimator' = 'Animation'
    'Nivalis.PlayerCameraController' = 'Camera'
    'Nivalis.PlayerEnvironmentTracker' = 'Environment'
    'Nivalis.PlayerNavMesh' = 'Navigation'
    'Nivalis.InventorySystem.PlayerInventory' = 'Inventory'
    'Nivalis.SkillSystem.SkillLevelController' = 'Skills'
    'Nivalis.PlayerGhost' = 'Ghost'
    'Nivalis.PlayerManager/PlayerSave' = 'Save'
    'Nivalis.PlayerManagerSave' = 'Save'
    'Nivalis.SkillSystem.SkillLevelController/SkillLevelsControllerSave' = 'Save'
    'Nivalis.PlayerInputManager' = 'Input'
    'Nivalis.PlayerInputManagerSave' = 'Save'
}
$support = @('Nivalis.PlayerStat','Nivalis.PlayerState/StatValue','Nivalis.PlayerManager/PlayerKnowledge',
    'Nivalis.SkillSystem.SkillLevelController/PlayerExperience','Nivalis.SkillSystem.SkillLevelController/PlayerSkillExperience',
    'Nivalis.OverrideableBool','Nivalis.OverrideableBool/OverrideLock','Nivalis.InventorySystem.InventoryData',
    'Nivalis.BaseCharacter','Nivalis.GhostSystem.Ghost','Nivalis.GhostSystem.Ai.CustomerGhost')
$mapping = Get-Content 'work/il2cpp-validation-v1.1/script.json' -Raw | ConvertFrom-Json -AsHashtable
$aliases = @{}
foreach ($m in $mapping.ScriptMethod) {
    $key = [string][long]$m.Address
    if (-not $aliases.ContainsKey($key)) { $aliases[$key] = [Collections.Generic.HashSet[string]]::new() }
    [void]$aliases[$key].Add($m.Name)
}
$types = @(); $methods = @(); $excluded = @()
foreach ($type in $all) {
    if (-not $groups.Contains($type.FullName) -and $type.FullName -notin $support) { continue }
    $fields = @($type.Fields | Where-Object { -not $_.IsStatic -and $_.FieldType.FullName -notmatch '^System.Action|^System.Func' } | ForEach-Object {
        @{ Name=$_.Name; Type=$_.FieldType.FullName.Replace('/','+'); ValueType=$_.FieldType.IsValueType }
    })
    $types += @{Name=$type.FullName.Replace('/','+'); Fields=$fields}
    if (-not $groups.Contains($type.FullName)) { continue }
    foreach ($method in $type.Methods) {
        if ($method.IsConstructor -or $method.Name -match '^(add_|remove_)') { continue }
        if ($method.IsGetter -and $method.Name -ne 'get_MaxSpeed') { continue }
        $attr = $method.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'AddressAttribute' }
        $rvaText = ($attr.Fields | Where-Object Name -eq 'RVA').Argument.Value
        $rva = if ($rvaText) { [Convert]::ToInt64($rvaText,16) } else { 0 }
        $aliasNames = @($aliases[[string]$rva] | Sort-Object)
        $reason = if ($method.IsStatic) { 'Static entry (instance observer only)' }
            elseif ($method.HasGenericParameters) { 'Generic entry; observe non-generic caller' }
            elseif ($rva -eq 0) { 'No native RVA in baseline metadata' }
            elseif ($aliasNames.Count -gt 1) { 'Shared native RVA; ambiguous entry' }
            else { '' }
        $entry = [ordered]@{
            Group=$groups[$type.FullName]; Type=$type.FullName.Replace('/','+'); Name=$method.Name
            ReturnType=$method.ReturnType.FullName.Replace('/','+')
            Parameters=@($method.Parameters | ForEach-Object { $_.ParameterType.FullName.Replace('/','+') })
            ParameterNames=@($method.Parameters | ForEach-Object Name)
            OutParameters=@($method.Parameters | ForEach-Object { [bool]$_.IsOut })
            Rva=('0x{0:X}' -f $rva)
            Sampled=($method.Name -match 'Update|ManualUpdate|Move$|HeadBob|GetStatValue|Find|Raycast|Snap|CheckPlayer|GetGround|RotationOffset|PositionOffset')
        }
        if ($reason) {
            $entry.Reason=$reason
            # Empty/accessor bodies can have thousands of aliases. Preserve the count and a bounded sample.
            $entry.AliasCount=$aliasNames.Count
            $entry.Aliases=@($aliasNames | Select-Object -First 16)
            $excluded += $entry
        }
        else { $methods += $entry }
    }
}
foreach ($name in $groups.Keys) { if (-not ($all | Where-Object FullName -eq $name)) { throw "Missing type: $name" } }
$catalog = @{ Types=$types; Methods=$methods; Excluded=$excluded }
$catalog | ConvertTo-Json -Depth 12 | Set-Content 'src/NightsHack.PlayerHook/PlayerCatalog.json' -Encoding utf8
Write-Output "Catalog: $($types.Count) types, $($methods.Count) eligible methods, $($excluded.Count) explicitly excluded."