from pathlib import Path
import re, json, struct, collections
root=Path('D:/NightsHack')
out=root/'work/money-investigation'
out.mkdir(exist_ok=True)
source=(root/'work/il2cpp-validation-v1.1/dump.cs').read_text(encoding='utf-8-sig')
blocks=[]
lastpos=0
lineno=1
for m in re.finditer(r'(?m)^// Namespace: (.*)\n([\s\S]*?)(?=^// Namespace: |\Z)',source):
    s=m.group(0); t=re.search(r'(?m)^(?:public|private|internal|protected).*? (?:class|struct|interface|enum) (.*?) // TypeDefIndex: (\d+)',s)
    lineno+=source.count('\n',lastpos,m.start()); lastpos=m.start()
    if t: blocks.append(dict(namespace=m.group(1).strip(), declaration=t.group(1),index=int(t.group(2)),line=lineno,text=s))
game=[b for b in blocks if b['index']>=11635]
money=[b for b in game if re.search(r'\bmoney\b|_money|Money|Receipt|Debt',b['text'])]
(out/'money-types.json').write_text(json.dumps([{k:v for k,v in b.items() if k!='text'} for b in money],indent=2),encoding='utf-8')
names=['PlayerInventory','PlayerManager.PlayerSave','PlayerManager.Player','PlayerManager','Shop','ShopTransaction','ShopTradeRequest','EconomyManager','AIOwner','ArticyGlobalInventoryLinker','PlayerMoneyDisplayUI','ReceiptBase','ReceiptType','VenueFinances','PlayerDebt','ShopTradeHandler','SerializationManager','RentManager','Venue']
chosen=[b for b in game if any(b['declaration'].split(' : ')[0]==n for n in names) or ('Money;' in b['text'])]
(out/'selected-types.txt').write_text('\n'.join('SOURCE LINE '+str(b['line'])+'\n'+b['text'] for b in chosen),encoding='utf-8')
summary={'game_type_count':len(game),'namespaces':collections.Counter(b['namespace'] for b in game).most_common(25)}
(out/'assembly-summary.json').write_text(json.dumps(summary,indent=2),encoding='utf-8')
print(json.dumps(summary,indent=2))
data=Path('D:/Steam/steamapps/common/Nivalis Nights/GameAssembly.dll').read_bytes()
pe=struct.unpack_from('<I',data,0x3c)[0]; n=struct.unpack_from('<H',data,pe+6)[0]; opt=struct.unpack_from('<H',data,pe+20)[0]; sec=pe+24+opt
sections=[]
for i in range(n):
    p=sec+i*40; vs,va,rs,raw=struct.unpack_from('<IIII',data,p+8); sections.append((va,max(vs,rs),raw))
def rawof(rva):
    for va,size,raw in sections:
        if va<=rva<va+size:return raw+rva-va
    raise ValueError(hex(rva))
hexes={}
for name,rva,size in [('get_Money',0x95c750,16),('set_Money',0x2f0b9a0,0x120),('ChangeMoneyWithoutReceipt',0x2f0bfe0,16),('TakeMoney',0x2f0c460,16),('ChangeMoneyWithReceipt',0x1a32180,0x240)]:
    raw=rawof(rva); payload=data[raw:raw+size];hexes[name]={'rva':hex(rva),'file_offset':hex(raw),'hex':payload.hex(' ')}
(out/'money-machine-bytes.json').write_text(json.dumps(hexes,indent=2),encoding='utf-8')
mapping=json.loads((root/'work/il2cpp-validation-v1.1/script.json').read_text(encoding='utf-8-sig'))
byaddr=collections.defaultdict(list)
for m in mapping['ScriptMethod']: byaddr[m['Address']].append(m['Name'])
checks=[]
for name in ['ChangeMoneyWithoutReceipt','TakeMoney','ChangeMoneyWithReceipt','set_Money']:
    h=hexes[name]; payload=bytes.fromhex(h['hex']); rva=int(h['rva'],16)
    for i,b in enumerate(payload):
        if b in (0xe8,0xe9) and i+5<=len(payload):
            target=rva+i+5+struct.unpack_from('<i',payload,i+1)[0]
            if target in byaddr: checks.append({'function':name,'offset':hex(i),'opcode':hex(b),'target_rva':hex(target),'symbols':byaddr[target][:12],'note':'Byte-pattern candidate; instruction alignment requires manual confirmation'})
(out/'branch-candidates.json').write_text(json.dumps(checks,indent=2),encoding='utf-8')
print(json.dumps(checks,indent=2))
brief=[]
for b in chosen:
    rows=[x for x in b['text'].splitlines() if x.startswith('// Namespace') or 'TypeDefIndex' in x or (x.startswith('\t') and not x.lstrip().startswith(('//','[','|','/*','*/')) and x.strip())]
    brief.append('SOURCE LINE '+str(b['line'])+'\n'+'\n'.join(rows))
(out/'selected-types-brief.txt').write_text('\n\n'.join(brief),encoding='utf-8')
selected_methods=[]
for m in mapping['ScriptMethod']:
    name=m['Name']
    if ('PlayerManager' in name and any(x in name for x in ['GetSaveDataFromPlayer','get_LocalPlayer','get_Inventory','get_Money','TryMakePurchase','TryMakeSale','PayRent','$$Initialize','$$WriteToPacket'])) or ('PlayerInventory' in name and 'OnMoneyEventLockChanged' in name):
        selected_methods.append(m)
(out/'selected-methods.json').write_text(json.dumps(selected_methods,indent=2),encoding='utf-8')
for m in selected_methods:
    rva=m['Address']; raw=rawof(rva)
    if any(x in m['Name'] for x in ['get_LocalPlayer','get_Inventory','get_Money','GetSaveDataFromPlayer']):
        size=0x130 if 'GetSaveData' in m['Name'] else 16
        print(m['Name'],hex(rva),data[raw:raw+size].hex(' '))
print('Selected method names:', [(m['Name'],hex(m['Address'])) for m in selected_methods])