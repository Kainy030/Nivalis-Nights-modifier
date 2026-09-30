$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')
$catalog = Get-Content 'src/NightsHack.PlayerHook/PlayerCatalog.json' -Raw | ConvertFrom-Json
$path = 'outputs/REFERENCE.md'
$marker = '<!-- PLAYERHOOK_CATALOG_APPENDIX -->'
$existing = Get-Content $path -Raw
if (-not $existing.Contains($marker)) { throw 'Reference appendix marker missing.' }
$lines = [Collections.Generic.List[string]]::new()
$lines.Add($existing.Substring(0,$existing.IndexOf($marker)) + $marker)
$lines.Add('')
$lines.Add('## 附录 A：候选方法清单（静态目录，不是实机命中清单）')
$lines.Add('')
$lines.Add('以下完整签名来自本地 DummyDll；RVA 经 script.json 共享地址筛选。采样“是”表示按配置间隔限频，包括部分低频 Update 命名入口。')
$lines.Add('')
foreach ($group in ($catalog.Methods | Group-Object Group | Sort-Object Name)) {
    $lines.Add("### $($group.Name)（$($group.Count) 个候选）")
    $lines.Add('')
    $lines.Add('| 完整类型与方法参数 | 返回类型 | 本版 RVA | 采样 |')
    $lines.Add('|---|---|---|---|')
    foreach ($method in $group.Group) {
        $parameters = for($i=0; $i -lt $method.Parameters.Count; $i++) {
            $direction = if ($method.OutParameters[$i]) { 'out ' } else { '' }
            "$direction$($method.Parameters[$i]) $($method.ParameterNames[$i])"
        }
        $sampling = if ($method.Sampled) { '是' } else { '否' }
        $lines.Add('| `' + $method.Type + '.' + $method.Name + '(' + ($parameters -join ', ') + ')` | `' + $method.ReturnType + '` | `' + $method.Rva + '` | ' + $sampling + ' |')
    }
    $lines.Add('')
}
$lines.Add('## 附录 B：明确排除的条目')
$lines.Add('')
$lines.Add('此表不包含普遍过滤的构造器、普通 getter 和事件 add/remove。PlayerCatalog.json 保存共享入口的别名总数及至多 16 个样例；完整别名见原始 script.json。泛型与静态方法目前不由实例观察器直接安装。')
$lines.Add('')
$lines.Add('| 类型与方法 | RVA | 原因 |')
$lines.Add('|---|---|---|')
foreach ($method in $catalog.Excluded) {
    $lines.Add('| `' + $method.Type + '.' + $method.Name + '(' + ($method.Parameters -join ', ') + ')` | `' + $method.Rva + '` | ' + $method.Reason + ' |')
}
$lines.Add('')
$lines.Add('## 附录 C：字段 schema')
$lines.Add('')
$lines.Add('仅列所选类型的非静态字段，并过滤 Action/Func 事件委托。支持类型不代表对它安装了全部方法钩子。字段实际可读性由加载时检查决定；其他对象引用通常不展开，基类可合并到派生实例快照。')
$lines.Add('')
foreach ($type in ($catalog.Types | Sort-Object Name)) {
    $lines.Add('### `' + $type.Name + '`')
    $lines.Add('')
    $lines.Add('| 原生字段名 | 元数据类型 |')
    $lines.Add('|---|---|')
    foreach ($field in $type.Fields) { $lines.Add('| `' + $field.Name + '` | `' + $field.Type + '` |') }
    $lines.Add('')
}
$lines | Set-Content -LiteralPath $path -Encoding utf8
Write-Output "REFERENCE appendix updated: $($catalog.Methods.Count) candidates, $($catalog.Excluded.Count) exclusions, $($catalog.Types.Count) schemas."
