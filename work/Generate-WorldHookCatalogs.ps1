$ErrorActionPreference = 'Stop'
Push-Location (Join-Path $PSScriptRoot '..')
try {
    [void][Reflection.Assembly]::LoadFrom((Resolve-Path 'work/hook-dependencies/bepinex-6.0.0-pre.2/BepInEx/core/Mono.Cecil.dll'))
    $assembly = [Mono.Cecil.AssemblyDefinition]::ReadAssembly((Resolve-Path 'work/il2cpp-validation-v1.1/DummyDll/Assembly-CSharp.dll'))
    function Flatten($types) { foreach ($t in $types) { $t; Flatten $t.NestedTypes } }
    $all = @(Flatten $assembly.MainModule.Types)
    $scope = Get-Content work/world-investigation/hook-scope.json -Raw | ConvertFrom-Json
    $player = Get-Content src/NightsHack.PlayerHook/PlayerCatalog.json -Raw | ConvertFrom-Json
    $mapping = Get-Content work/il2cpp-validation-v1.1/script.json -Raw | ConvertFrom-Json -AsHashtable
    $aliases = @{}
    foreach ($m in $mapping.ScriptMethod) {
        $key = [string][long]$m.Address
        if (-not $aliases.ContainsKey($key)) { $aliases[$key] = [Collections.Generic.HashSet[string]]::new() }
        [void]$aliases[$key].Add($m.Name)
    }
    $playerIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $reserved = @{}
    foreach ($m in $player.Methods) {
        $id = "$($m.Type).$($m.Name)($($m.Parameters -join ','))"
        [void]$playerIds.Add($id)
        $reserved[('0x{0:X}' -f [Convert]::ToInt64($m.Rva,16))] = "PlayerHook: $id"
    }
    $catalogs = @{}
    foreach ($owner in @('WorldHook','ItemHook','GameRuntimeHook')) {
        $catalogs[$owner] = @{Types=[Collections.Generic.List[object]]::new(); Methods=[Collections.Generic.List[object]]::new(); Excluded=[Collections.Generic.List[object]]::new()}
    }
    $audit = [Collections.Generic.List[object]]::new()
    $seenTypes = [Collections.Generic.HashSet[string]]::new()
    foreach ($s in $scope) {
        $matches = @($all | Where-Object { $_.FullName.Replace('/','.') -eq $s.Type -or $_.FullName.Replace('/','.').EndsWith('.'+$s.Type) })
        if ($matches.Count -ne 1) { throw "Scope type not unique: $($s.Type), matches=$($matches.Count)" }
        $type = $matches[0]
        if (-not $seenTypes.Add($type.FullName)) { throw "Repeated scope type: $($type.FullName)" }
        $catalog = $catalogs[$s.Owner]
        $typeName = $type.FullName.Replace('/','+')
        if (-not $type.IsValueType -and -not $type.IsInterface -and -not $type.HasGenericParameters) {
            $fields = @($type.Fields | Where-Object { -not $_.IsStatic -and $_.FieldType.FullName -notmatch '^System.Action|^System.Func' } | ForEach-Object {
                @{Name=$_.Name; Type=$_.FieldType.FullName.Replace('/','+'); ValueType=$_.FieldType.IsValueType}
            })
            $catalog.Types.Add(@{Name=$typeName; Fields=$fields})
        }
        foreach ($method in $type.Methods) {
            if ($s.OnlyMethods -and $method.Name -notin $s.OnlyMethods) { continue }
            $attr = $method.CustomAttributes | Where-Object { $_.AttributeType.Name -eq 'AddressAttribute' }
            $rvaText = ($attr.Fields | Where-Object Name -eq 'RVA').Argument.Value
            $rva = if ($rvaText) { [Convert]::ToInt64($rvaText,16) } else { 0L }
            $params = @($method.Parameters | ForEach-Object { $_.ParameterType.FullName.Replace('/','+') })
            $id = "$typeName.$($method.Name)($($params -join ','))"
            $aliasNames = @($aliases[[string]$rva] | Sort-Object)
            $reason = if ($playerIds.Contains($id)) { 'Already covered by PlayerHook signature' }
                elseif ($rva -ne 0 -and $reserved.ContainsKey(('0x{0:X}' -f $rva))) { 'Native entry already owned by ' + $reserved[('0x{0:X}' -f $rva)] }
                elseif ($method.IsConstructor) { 'Construction/finalization is outside observer scope' }
                elseif ($method.Name -eq 'Finalize') { 'Construction/finalization is outside observer scope' }
                elseif ($method.IsAbstract -or $type.IsInterface) { 'Abstract/interface declaration; observe implementation' }
                elseif ($type.IsValueType -and -not $method.IsStatic) { 'Value-type instance ABI not supported by object observer' }
                elseif ($type.HasGenericParameters -or $method.HasGenericParameters) { 'Open generic entry; observe concrete caller' }
                elseif ($rva -eq 0) { 'No native RVA in baseline metadata' }
                elseif ($aliasNames.Count -ne 1) { 'Shared or unmapped native RVA; ambiguous entry' }
                elseif ($method.Name -match '^(add_|remove_|get_)' -and $method.Name -notin @('get_Item','get_StackCount','get_ItemCount','get_Freshness','get_FreshnessFactor','get_CanTravel','get_IsIllegal','get_AllowPicking','get_SceneIsLoaded','get_SceneIndex','get_HasDecay','get_RequiresRefridgeration')) { 'Accessor/event plumbing; fields or mutation endpoints observed instead' }
                elseif ($method.Name.Contains('.') -or $method.Name -match 'OnDrawGizmos|OnValidate|^Editor|^DrawGizmo|^<.*>b__|^<.*>g__') { 'Editor/compiler helper or explicit interface wrapper outside scope' }
                elseif ($type.Name -match '^<' -and $method.Name -ne 'MoveNext') { 'Iterator plumbing; observe MoveNext' }
                elseif ($method.ReturnType.IsByReference -or $method.ReturnType.IsPointer -or @($method.Parameters | Where-Object { $_.ParameterType.IsPointer }).Count -gt 0) { 'Pointer/byref return ABI outside observer scope' }
                else { '' }
            $entry = [ordered]@{
                Group=$type.Name; Type=$typeName; Name=$method.Name; ReturnType=$method.ReturnType.FullName.Replace('/','+')
                Parameters=$params; ParameterNames=@($method.Parameters | ForEach-Object Name)
                OutParameters=@($method.Parameters | ForEach-Object { [bool]$_.IsOut })
                Rva=('0x{0:X}' -f $rva); IsStatic=[bool]$method.IsStatic
                # Business mutations, including decay updates, are not time sampled.
                Sampled=($method.Name -match '^(Update|LateUpdate|FixedUpdate|ManualUpdate|MoveNext|UpdatePosition|UpdateAgentActions|UpdateWeatherForCurrentTime|Check.*|Find.*|get_.*)$')
                SkipAfterInstance=($method.Name -match 'Destroy|Release|Unlink|Deregister|Unload|^Store$|^Use$|^DoInteraction$|^Clear$|^Load$|^MoveNext$')
            }
            $audit.Add(@{Owner=$s.Owner; Id=$id; Rva=$entry.Rva; Disposition=$(if($reason){'Excluded'}else{'Candidate'}); Reason=$reason})
            if ($reason) {
                $entry.Reason=$reason; $entry.AliasCount=$aliasNames.Count; $entry.Aliases=@($aliasNames | Select-Object -First 16)
                $catalog.Excluded.Add($entry)
            } else {
                $catalog.Methods.Add($entry)
                if ($rva -ne 0) { $reserved[('0x{0:X}' -f $rva)] = "$($s.Owner): $id" }
            }
        }
    }
    foreach ($owner in @('WorldHook','ItemHook','GameRuntimeHook')) {
        $c = $catalogs[$owner]
        @{Types=@($c.Types.ToArray()); Methods=@($c.Methods.ToArray()); Excluded=@($c.Excluded.ToArray())} |
            ConvertTo-Json -Depth 14 | Set-Content "src/NightsHack.$owner/Catalog.json" -Encoding utf8
        Write-Output "$owner : $($c.Types.Count) schemas, $($c.Methods.Count) candidate methods, $($c.Excluded.Count) exclusions"
    }
    $audit.ToArray() | ConvertTo-Json -Depth 8 | Set-Content work/world-investigation/hook-coverage-audit.json -Encoding utf8
    Write-Output "Player overlaps: $(@($audit | Where-Object Reason -Like '*PlayerHook*').Count); audited declarations: $($audit.Count)"
    $assembly.Dispose()
} finally { Pop-Location }