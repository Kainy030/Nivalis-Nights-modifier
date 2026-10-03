"""World static-analysis inventory. Read game inputs, write workspace evidence only."""
from pathlib import Path
import re, json, hashlib, collections
ROOT=Path('D:/NightsHack')
OUT=ROOT/'work/world-investigation'
OUT.mkdir(exist_ok=True)
source=(ROOT/'work/il2cpp-validation-v1.1/dump.cs').read_text(encoding='utf-8-sig')
blocks=[]; line=1; last=0
for match in re.finditer(r'(?m)^// Namespace: (.*)\n([\s\S]*?)(?=^// Namespace: |\Z)',source):
    line+=source.count('\n',last,match.start()); last=match.start()
    decl=re.search(r'(?m)^(?:public|private|internal|protected).*? (?:class|struct|interface|enum) (.*?) // TypeDefIndex: (\d+)',match[0])
    if not decl or int(decl[2])<11635: continue
    name=decl[1].split(' : ')[0]; ns=match[1].strip()
    blocks.append(dict(namespace=ns,name=name,full_name=(ns+'.' if ns else '')+name,declaration=decl[1],index=int(decl[2]),line=line,text=match[0]))
pattern=r'World|Ghost|Scene|Transition|Travel|Location|ItemContainer|ItemStack|ItemInstance|ItemType|Holdable|EntityPool|Pooling|Prefab|SerializationManager|TimeOfDay|Weather|RentManager|BaseProperty|Venue|Apartment|Greenhouse|Furniture|EconomyManager|Spawn|Despawn|Culling'
extra={'ManagersBootstrap','GameObjectPool','GameObjectPool.Pool','PooledElement','SerializableObject','SavedObject','PortalKey','PlacementTracker','PlacementSpot','PlacementSlot','PlacementSetting','ItemCollectible','ItemEntity','InventoriesManager','InventorySave','InventoriesSave','ItemInstanceCollectionExtensions','FreshnessUtility','ItemCollectionOperationResult'}
related=[b for b in blocks if (re.search(pattern,b['name']) or b['name'] in extra or b['namespace']=='Nivalis.InventorySystem') and not b['namespace'].startswith(('Articy.', 'ES3', 'QFSW.'))]
(OUT/'world-type-index.json').write_text(json.dumps([{k:v for k,v in b.items() if k!='text'} for b in related],indent=2),encoding='utf-8')
core=[b for b in related if '<' not in b['name'] and not b['name'].startswith('ArticyValue') and not b['name'].endswith(('Template','Constraint','Feature'))]
for b in core:
    stem=re.sub(r'[^A-Za-z0-9_.-]','_',b['full_name'])
    (OUT/(stem+'.txt')).write_text('\n'.join(f'{b["line"]+i}: {row}' for i,row in enumerate(b['text'].splitlines())),encoding='utf-8')
(OUT/'core-types-brief.txt').write_text('\n\n'.join('\n'.join(f'{b["line"]+i}: {row}' for i,row in enumerate(b['text'].splitlines()) if 'TypeDefIndex' in row or (row.startswith('\t') and row.strip() and not row.lstrip().startswith(('//','[','|','/*','*/')))) for b in core),encoding='utf-8')
mapping=json.loads((ROOT/'work/il2cpp-validation-v1.1/script.json').read_text(encoding='utf-8-sig'))
owners={b['full_name'] for b in core if b['namespace']}
nested={'.'+b['name'] for b in core if not b['namespace']}
methods=[m for m in mapping['ScriptMethod'] if m['Name'].split('$$')[0] in owners or any(m['Name'].split('$$')[0].endswith(n) for n in nested)]
(OUT/'selected-methods.json').write_text(json.dumps(methods,indent=2),encoding='utf-8')
paths={
 'GameAssembly.dll':Path('D:/Steam/steamapps/common/Nivalis Nights/GameAssembly.dll'),
 'global-metadata.dat':Path('D:/Steam/steamapps/common/Nivalis Nights/Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat'),
 'dump.cs':ROOT/'work/il2cpp-validation-v1.1/dump.cs','script.json':ROOT/'work/il2cpp-validation-v1.1/script.json'}
hashes={}
for name,path in paths.items():
    with path.open('rb') as f: digest=hashlib.file_digest(f,'sha256').hexdigest().upper()
    hashes[name]={'path':str(path),'sha256':digest}
(OUT/'input-hashes.json').write_text(json.dumps(hashes,indent=2),encoding='utf-8')
print(json.dumps({'broad_related':len(related),'extracted_types':len(core),'method_records':len(methods),'hashes':hashes},indent=2))
print('\n'.join(f'{b["line"]}: {b["full_name"]}' for b in core))