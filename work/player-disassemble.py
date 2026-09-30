"""Instruction-aligned x64 CFG extraction from the existing, read-only PE file.

No process attachment or MCP transport. The next known IL2CPP symbol is an upper
bound. PE exception records are recorded as unwind fragments, not method boundaries.
Indirect branches and split/cold blocks may remain unresolved. This is not C# recovery.
"""
from pathlib import Path
import bisect
import collections
import json
import re
import struct
import sys

ROOT = Path('D:/NightsHack')
sys.path.insert(0, str(ROOT / 'work/analysis-python'))
import capstone as cs
from capstone.x86 import X86_OP_IMM, X86_OP_MEM, X86_REG_RIP

OUT = ROOT / 'work/player-investigation'
ASM = OUT / 'asm'
ASM.mkdir(exist_ok=True)
data = Path('D:/Steam/steamapps/common/Nivalis Nights/GameAssembly.dll').read_bytes()
mapping = json.loads((ROOT / 'work/il2cpp-validation/script.json').read_text(encoding='utf-8-sig'))
pe = struct.unpack_from('<I', data, 0x3c)[0]
section_count = struct.unpack_from('<H', data, pe+6)[0]
optional_size = struct.unpack_from('<H', data, pe+20)[0]
optional = pe + 24
assert struct.unpack_from('<H', data, optional)[0] == 0x20b
image_base = struct.unpack_from('<Q', data, optional+24)[0]
sections = []
for i in range(section_count):
    p = optional + optional_size + i*40
    virtual_size, rva, raw_size, raw = struct.unpack_from('<IIII',data,p+8)
    sections.append((rva, virtual_size, raw, raw_size))

def rawof(rva, size=1):
    for va,vs,raw,rs in sections:
        if va <= rva and rva+size <= va+rs:
            return raw+rva-va
    raise ValueError(f'Not file-backed RVA: {rva:x}')

byaddr = collections.defaultdict(list)
for method in mapping['ScriptMethod']:
    byaddr[method['Address']].append(method['Name'])
starts = sorted(byaddr)
references = collections.defaultdict(list)
for category in ('ScriptString', 'ScriptMetadata', 'ScriptMetadataMethod'):
    for entry in mapping[category]:
        references[entry['Address']].append(entry.get('Name', repr(entry.get('Value'))))
exception_rva, exception_size = struct.unpack_from('<II',data,optional+112+3*8)
unwind = {}
if exception_rva:
    raw = rawof(exception_rva, exception_size)
    for p in range(raw,raw+exception_size,12):
        start,end,info = struct.unpack_from('<III',data,p)
        unwind[start] = end

engine = cs.Cs(cs.CS_ARCH_X86, cs.CS_MODE_64)
engine.detail = True
requests = json.loads((OUT / 'method-requests.json').read_text(encoding='utf-8'))
selected = []
for method in mapping['ScriptMethod']:
    owner, _, name = method['Name'].partition('$$')
    if any((owner == item['class'] or owner.endswith('.'+item['class']))
        and (name in item['methods'] or '*' in item['methods']) for item in requests):
        selected.append(method)

results = []
for method in selected:
    start = method['Address']
    next_index = bisect.bisect_right(starts,start)
    next_start = starts[next_index] if next_index < len(starts) else start+0x8000
    end = min(next_start, start+0x8000)
    boundary = 'next mapped IL2CPP symbol (upper bound), reachable instruction traversal'
    pending = [start]
    instructions = {}
    edges = []
    unresolved = []
    while pending:
        pc = pending.pop()
        while start <= pc < end and pc not in instructions:
            raw = rawof(pc)
            decoded = list(engine.disasm(data[raw:raw+min(15,end-pc)],pc,count=1))
            if not decoded:
                unresolved.append({'rva':hex(pc),'reason':'decode failure'})
                break
            instruction = decoded[0]
            annotations = []
            operands = instruction.operands
            branch = instruction.group(cs.CS_GRP_JUMP)
            call = instruction.group(cs.CS_GRP_CALL)
            if (branch or call) and operands and operands[0].type == X86_OP_IMM:
                target = operands[0].imm
                aliases = byaddr.get(target,[])
                edge = {'site':hex(pc),'kind':instruction.mnemonic,'target':hex(target),
                    'symbols':aliases,'within_bounds':start<=target<end}
                edges.append(edge)
                if aliases:
                    annotations.append(' | '.join(aliases[:5]) + (f' [+{len(aliases)-5} aliases]' if len(aliases)>5 else ''))
                if branch and start<=target<end:
                    pending.append(target)
            elif branch or call:
                unresolved.append({'rva':hex(pc),'reason':'indirect '+instruction.mnemonic, 'operand':instruction.op_str})
            for operand in operands:
                if operand.type == X86_OP_MEM and operand.mem.base == X86_REG_RIP:
                    target = pc + instruction.size + operand.mem.disp
                    if target in references:
                        annotations.extend(references[target])
                    else:
                        try:
                            raw_string = rawof(target)
                            candidate = data[raw_string:raw_string+256].split(b'\x00',1)[0]
                            if len(candidate) >= 6 and all(32 <= c < 127 for c in candidate):
                                annotations.append('native ASCII: '+candidate.decode('ascii'))
                        except ValueError:
                            pass
                    if target not in references and instruction.mnemonic.endswith(('ss','sd')):
                        try:
                            raw_const = rawof(target,8)
                            annotations.append(f'const@{target:X}: f32={struct.unpack_from("<f",data,raw_const)[0]:.9g}; bytes={data[raw_const:raw_const+8].hex()}')
                        except ValueError:
                            pass
            instructions[pc] = {'rva':hex(pc),'bytes':instruction.bytes.hex(' '),
                'mnemonic':instruction.mnemonic,'operands':instruction.op_str,'annotations':annotations}
            if instruction.group(cs.CS_GRP_RET) or instruction.mnemonic in ('jmp','int3','ud2'):
                break
            pc += instruction.size
    stem = re.sub(r'[^A-Za-z0-9_.-]', '_', method['Name']) + f'_{start:X}'
    result = {'method':method, 'start':hex(start),'end_upper_bound':hex(end),
        'boundary_source':boundary,'entry_unwind_fragment_end':hex(unwind[start]) if start in unwind else None,
        'image_base':hex(image_base),
        'instructions':[instructions[a] for a in sorted(instructions)],'edges':edges,'unresolved':unresolved}
    (ASM/(stem+'.json')).write_text(json.dumps(result,indent=2),encoding='utf-8')
    rows = [method['Name'],method['Signature'], f'RVA {start:X}..{end:X}; {boundary}',
        'RVA operands; not runtime VAs. Aliases/indirect calls need contextual interpretation.']
    for ins in result['instructions']:
        rows.append(f'{int(ins["rva"],16):08X}  {ins["bytes"]:42} {ins["mnemonic"]:9} {ins["operands"]}' +
            (' ; '+'; '.join(ins['annotations']) if ins['annotations'] else ''))
    (ASM/(stem+'.txt')).write_text('\n'.join(rows),encoding='utf-8')
    results.append({k:v for k,v in result.items() if k != 'instructions'} | {'instruction_count':len(instructions), 'file':stem+'.txt'})
(OUT/'disassembly-index.json').write_text(json.dumps(results,indent=2),encoding='utf-8')
print(json.dumps({'selected_method_records':len(results),'unique_rvas':len({r['start'] for r in results}),
    'decoded_instructions':sum(r['instruction_count'] for r in results), 'capstone':cs.__version__},indent=2))
print('\n'.join(f'{r["start"]} {r["instruction_count"]:5} {r["method"]["Name"]}' for r in results))
