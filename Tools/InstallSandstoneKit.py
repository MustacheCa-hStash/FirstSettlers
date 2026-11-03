"""Copy only source-matched, validated SS01-SS11 assets; append catalog references."""
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / '.utmp/sandstone-kit/unity-project'
ART = 'ArtReferences/SandstoneBuildKit/Models'
manifest = json.loads((ROOT / ART / 'sandstone-models.json').read_text())
proof = json.loads((PROJECT / ART / 'unity-validation.json').read_text())
assert proof['passed'] and proof['parts'] == 11 and proof['representations'] == 12
assert proof['headings'] == 8 and proof['doorWalks'] == 16 and proof['triangles'] == 180
assert hashlib.sha256((ROOT / ART / 'SandstoneBuildKit.blend').read_bytes()).hexdigest() == manifest['blend_sha256']
assert (ROOT / ART / 'sandstone-models.json').read_bytes() == (PROJECT / ART / 'sandstone-models.json').read_bytes(), 'Stale validation source'
for name in ('SandstoneKitSetup', 'SandstoneKitValidation', 'BuildingPrototypeSetup'):
    path = 'Assets/Editor/' + name + '.cs'
    assert (ROOT / path).read_bytes() == (PROJECT / path).read_bytes(), 'Stale validation code: ' + name
assert (ROOT / 'Assets/Scripts/Building/BuildUiContent.cs').read_bytes() == (PROJECT / 'Assets/Scripts/Building/BuildUiContent.cs').read_bytes()
for part in manifest['parts']:
    model = 'Assets/Models/Buildings/Sandstone/' + part['file']
    assert hashlib.sha256((ROOT / model).read_bytes()).hexdigest() == part['sha256']
    assert (ROOT / model).read_bytes() == (PROJECT / model).read_bytes()
for name in ('Sandstone_ColourAO.png', 'Sandstone_Normal.png'):
    path = 'Assets/Textures/Buildings/Sandstone/' + name
    assert (ROOT / path).read_bytes() == (PROJECT / path).read_bytes(), 'Stale texture validation'
regressions = {
    '.utmp/stone-kit/validation.json': 'stone-regression.json',
    '.utmp/modular-kit/validation.json': 'wood-regression.json',
    '.utmp/interaction/validation.json': 'interaction-regression.json',
}
for path in regressions:
    assert json.loads((PROJECT / path).read_text())['passed'], path

def copy(relative):
    source, target = PROJECT / relative, ROOT / relative
    assert source.is_file(), source
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)

for kind in ('Models', 'Textures', 'Materials'):
    base = 'Assets/' + kind + '/Buildings/Sandstone'
    for source in (PROJECT / base).rglob('*'):
        if source.is_file(): copy(str(source.relative_to(PROJECT)))
    if not (ROOT / (base + '.meta')).exists(): copy(base + '.meta')

resource = 'Assets/Resources/Building/'
guids = []
for part in manifest['parts']:
    slug = part['slug']
    for source in (PROJECT / resource).glob(slug + '*'):
        if source.is_file(): copy(str(source.relative_to(PROJECT)))
    assert 'contentId: build.stone.' + slug in (PROJECT / resource / (slug + '.asset')).read_text()
    if part['slope'] != 'falling':
        guids.append(re.search(r'^guid: ([a-f0-9]{32})$', (PROJECT / resource / (slug + '.asset.meta')).read_text(), re.M).group(1))
    for suffix in ('.png', '.png.meta'): copy(resource + 'UI/Thumbnails/build_stone_' + slug + suffix)

catalog = ROOT / resource / 'PrototypeCatalog.asset'
original = catalog.read_bytes().decode('utf-8')
newline = '\r\n' if '\r\n' in original else '\n'
additions = ''.join('  - {fileID: 11400000, guid: ' + guid + ', type: 2}' + newline
                    for guid in guids if 'guid: ' + guid + ',' not in original)
assert len(guids) == 11 and original.count('  validPreview:') == 1
updated = original.replace('  validPreview:', additions + '  validPreview:', 1)
assert updated.replace(additions, '', 1) == original if additions else updated == original
catalog.write_bytes(updated.encode('utf-8'))
for source in (PROJECT / ART).glob('*.png'): copy(str(source.relative_to(PROJECT)))
copy(ART + '/unity-validation.json')
for source, name in regressions.items(): shutil.copy2(PROJECT / source, ROOT / ART / name)
print('INSTALLED SS01-SS11: eleven choices, twelve models, 180 selectable triangles, shared baked maps, collision, prefabs and icons.')
