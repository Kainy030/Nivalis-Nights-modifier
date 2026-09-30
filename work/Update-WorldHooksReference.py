"""Refresh the generated catalog appendix without modifying the hand-written reference."""
from pathlib import Path
import json, collections
root=Path(__file__).resolve().parent.parent
path=root/'outputs/HOOKS-REFERENCE.md'
marker='<!-- GENERATED CATALOG -->'
header=path.read_text(encoding='utf-8').split(marker)[0]+marker+'\n'
rows=['','## 生成清单统计','', '| 插件 | 字段 schema | 字段数 | 方法候选 | 明确排除 | 静态候选 |', '|---|---:|---:|---:|---:|---:|']
catalogs={}
for name in ['PlayerHook','WorldHook','ItemHook','GameRuntimeHook']:
    file='PlayerCatalog.json' if name=='PlayerHook' else 'Catalog.json'
    c=json.loads((root/f'src/NightsHack.{name}/{file}').read_text(encoding='utf-8-sig'));catalogs[name]=c
    rows.append(f'| {name} | {len(c["Types"])} | {sum(len(t["Fields"]) for t in c["Types"])} | {len(c["Methods"])} | {len(c["Excluded"])} | {sum(m.get("IsStatic",False) for m in c["Methods"])} |')
rows+=['','字段 schema 可包含只用于读取的支撑类型；没有同名方法不代表没有字段 schema。字段条目按各插件分别计数。','', '### 保留于 PlayerHook 的重复入口','']
for c in catalogs.values():
    for e in c['Excluded']:
        if 'PlayerHook' in e['Reason']:
            rows.append(f'- `{e["Type"]}.{e["Name"]}({", ".join(e["Parameters"])})`，`{e["Rva"]}`。')
for name,c in catalogs.items():
    rows+=['','## '+name+' 候选方法','', '| 完整方法签名 | 返回类型 | RVA | 静态 | 采样 | 跳过 After 实例读取 |','|---|---|---|---|---|---|']
    for m in c['Methods']:
        signature=m['Type']+'.'+m['Name']+'('+', '.join(m['Parameters'])+')'
        flags=[m.get('IsStatic',False),m['Sampled'],m.get('SkipAfterInstance',False) or 'Destroy' in m['Name']]
        rows.append('| `'+signature+'` | `'+m['ReturnType']+'` | `'+m['Rva']+'` | '+' | '.join('是' if flag else '否' for flag in flags)+' |')
    rows+=['','### 排除原因分布','']
    for reason,count in collections.Counter(e['Reason'] for e in c['Excluded']).items():rows.append(f'- {reason}：{count}。')
path.write_text(header+'\n'.join(rows)+'\n',encoding='utf-8')
print('Updated',path)
