"""Copy only the validated timber/infill kit; append six stable catalog GUIDs."""
import hashlib
import json
import re
import shutil
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / '.utmp/limestone-kit/unity-project'
ART = 'ArtReferences/LimestoneBuildKit/TimberInfill'
proof = json.loads((PROJECT / ART / 'unity-validation.json').read_text())
assert proof['passed'] and proof['parts'] == 6 and proof['doorWalks'] == 16 and proof['roofJoinCases'] == 32 and proof['canopyMountCases'] == 32
regressions = {
    'l01-l03-regression.json': 'ArtReferences/LimestoneBuildKit/Models/unity-validation.json',
    'l04-l09-regression.json': 'ArtReferences/LimestoneBuildKit/Extension/unity-validation.json',
    'stone-regression.json': '.utmp/stone-kit/validation.json',
    'modular-regression.json': '.utmp/modular-kit/validation.json',
}
for source in regressions.values(): assert json.loads((PROJECT / source).read_text())['passed'], source
manifest = json.loads((ROOT / ART / 'timber-infill-models.json').read_text())
assert hashlib.sha256((ROOT / ART / 'TimberInfillKit.blend').read_bytes()).hexdigest() == manifest['blend_sha256']


def copy(relative):
    source, target = PROJECT / relative, ROOT / relative
    assert source.is_file(), source
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


for kind in ('Models', 'Textures', 'Materials'):
    base = 'Assets/' + kind + '/Buildings/TimberInfill'
    for source in (PROJECT / base).rglob('*'):
        if source.is_file(): copy(str(source.relative_to(PROJECT)))
    if not (ROOT / (base + '.meta')).exists(): copy(base + '.meta')

guids = []
resource = 'Assets/Resources/Building/'
for part in manifest['parts']:
    slug = part['slug']
    for source in (PROJECT / resource).glob(slug + '*'):
        if source.is_file(): copy(str(source.relative_to(PROJECT)))
    text = (PROJECT / resource / (slug + '.asset')).read_text()
    assert 'contentId: build.wood.' + slug in text
    if part.get('slope') != 'falling':
        guid = re.search(r'^guid: ([a-f0-9]{32})$', (PROJECT / resource / (slug + '.asset.meta')).read_text(), re.M).group(1)
        guids.append(guid)
    for suffix in ('.png', '.png.meta'): copy(resource + 'UI/Thumbnails/build_wood_' + slug + suffix)

catalog = ROOT / resource / 'PrototypeCatalog.asset'
original = catalog.read_bytes().decode('utf-8')
newline = '\r\n' if '\r\n' in original else '\n'
additions = ''.join('  - {fileID: 11400000, guid: ' + guid + ', type: 2}' + newline for guid in guids if 'guid: ' + guid + ',' not in original)
assert len(guids) == 6 and original.count('  validPreview:') == 1
changed = original.replace('  validPreview:', additions + '  validPreview:', 1)
assert changed.replace(additions, '', 1) == original if additions else changed == original
catalog.write_bytes(changed.encode('utf-8'))
for source in (PROJECT / ART).glob('*.png'): copy(str(source.relative_to(PROJECT)))
copy(ART + '/unity-validation.json')
for name, source in regressions.items(): shutil.copy2(PROJECT / source, ROOT / ART / name)
print('INSTALLED L25/L27/L28/L31/L33/L35: six menu items, two gable slopes, shared trim/normal maps, prefab collision hulls and thumbnails.')
