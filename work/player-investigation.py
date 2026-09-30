"""Read-only game analysis; writes reproducible excerpts inside the workspace."""
from pathlib import Path
import collections
import hashlib
import json
import re

ROOT = Path('D:/NightsHack')
OUT = ROOT / 'work/player-investigation'
OUT.mkdir(exist_ok=True)
source = (ROOT / 'work/il2cpp-validation/dump.cs').read_text(encoding='utf-8-sig')
blocks = []
line = 1
last = 0
for match in re.finditer(r'(?m)^// Namespace: (.*)\n([\s\S]*?)(?=^// Namespace: |\Z)', source):
    line += source.count('\n', last, match.start())
    last = match.start()
    declaration = re.search(r'(?m)^(?:public|private|internal|protected).*? (?:class|struct|interface|enum) (.*?) // TypeDefIndex: (\d+)', match[0])
    if not declaration or int(declaration[2]) < 11635:
        continue
    name = declaration[1].split(' : ')[0]
    namespace = match[1].strip()
    blocks.append(dict(namespace=namespace, name=name,
        full_name=(namespace + '.' if namespace else '') + name,
        declaration=declaration[1], index=int(declaration[2]), line=line, text=match[0]))

related = [b for b in blocks if 'Player' in b['name'] or b['namespace'] == 'Nivalis.Player']
(OUT / 'player-type-index.json').write_text(json.dumps([
    {k:v for k,v in b.items() if k != 'text'} for b in related], indent=2), encoding='utf-8')
names = {'PlayerManager', 'PlayerManager.Player', 'PlayerManager.PlayerSave',
    'PlayerManagerSave', 'PlayerManager.OrderData', 'PlayerManager.MealProcessingStep',
    'PlayerCharacter', 'PlayerGhost', 'PlayerCharacterController', 'BaseCharacter',
    'PlayerInputManager', 'PlayerInputManagerSave', 'PlayerObjectHolder',
    'PlayerState', 'PlayerState.StatValue', 'PlayerStat', 'PlayerInteraction',
    'PlayerCameraController', 'PlayerEnvironmentTracker', 'PlayerNavMesh',
    'PlayerInventory', 'PlayerHandsAnimator', 'SkillLevelController',
    'SkillLevelController.PlayerExperience', 'SkillLevelController.PlayerSkillExperience',
    'PlayerManager.PlayerKnowledge', 'PlayerCharacterController.ControllerState',
    'FocusRaycaster', 'FocusRaycasterSettings', 'IInteractable', 'OverrideableBool',
    'OverrideableBool.OverrideLock', 'Ghost', 'CustomerGhost', 'SkillDefinition',
    'SkillLevelControllerSave', 'PlayerConfiguration', 'PlayerManagerConfiguration',
    'PlayerManager.MealPreparationProcessingType', 'TimeOfDayManager',
    'SkillLevelController.SkillLevelsControllerSave', 'SettingsManager', 'Venue',
    'SkillLevels<TLevelData>', 'SkillLevelData', 'KeyPressOption', 'TimeOfDayManager.TimeStamp'}
selected = [b for b in blocks if b['name'] in names or b['namespace'] == 'Nivalis.Player']
for b in selected:
    text = '\n'.join(f'{b["line"]+i}: {row}' for i,row in enumerate(b['text'].splitlines()))
    filename = re.sub(r'[<>:"/\\|?*]', '_', b['full_name']) + '.txt'
    (OUT / filename).write_text(text, encoding='utf-8')
(OUT / 'selected-types-brief.txt').write_text('\n\n'.join(
    '\n'.join(f'{b["line"]+i}: {row}' for i,row in enumerate(b['text'].splitlines())
        if 'TypeDefIndex' in row or (row.startswith('\t') and row.strip() and not row.lstrip().startswith(('//','[','|','/*','*/'))))
    for b in selected), encoding='utf-8')
mapping = json.loads((ROOT / 'work/il2cpp-validation/script.json').read_text(encoding='utf-8-sig'))
methods = [m for m in mapping['ScriptMethod'] if
    any(m['Name'].split('$$')[0] == b['full_name'] or
        (not b['namespace'] and m['Name'].split('$$')[0].endswith('.'+b['name']))
        for b in selected)]
(OUT / 'selected-methods.json').write_text(json.dumps(methods, indent=2), encoding='utf-8')
hashes = {}
for name,path in {
    'GameAssembly.dll': Path('D:/Steam/steamapps/common/Nivalis Nights/GameAssembly.dll'),
    'global-metadata.dat': Path('D:/Steam/steamapps/common/Nivalis Nights/Nivalis Nights_Data/il2cpp_data/Metadata/global-metadata.dat'),
    'dump.cs': ROOT / 'work/il2cpp-validation/dump.cs',
    'script.json': ROOT / 'work/il2cpp-validation/script.json',
}.items():
    with path.open('rb') as stream:
        hashes[name] = {'path':str(path), 'sha256':hashlib.file_digest(stream, 'sha256').hexdigest().upper()}
(OUT / 'input-hashes.json').write_text(json.dumps(hashes, indent=2), encoding='utf-8')
print(json.dumps({'related_types':len(related), 'selected_types':len(selected), 'selected_methods':len(methods), 'hashes':hashes}, indent=2))
print('\n'.join(f'{b["line"]}: {b["full_name"]}' for b in selected))
