"""Install validated L01-L03, or L04-L09 with --extension, and append catalog references.

Only the selected kit's assets are copied; unrelated assets and catalog properties remain intact.
"""
import json
import re
import shutil
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / '.utmp/limestone-kit/unity-project'
extension = '--extension' in sys.argv
evidence = 'Extension' if extension else 'Models'
proof = json.loads((PROJECT / ('ArtReferences/LimestoneBuildKit/' + evidence + '/unity-validation.json')).read_text())
regression = json.loads((PROJECT / '.utmp/stone-kit/validation.json').read_text())
assert proof['passed'] and proof['parts'] == (6 if extension else 3)
assert proof['placementCases'] == (48 if extension else 24) and regression['passed']
if extension:
    assert proof['runtimeTriangles'] == 96 and proof['doorWalks'] == 16 and proof['terrainFootings'] == 8
    assert json.loads((PROJECT / 'ArtReferences/LimestoneBuildKit/Models/unity-validation.json').read_text())['passed']
else:
    assert proof['trianglesPerWall'] == 12


def copy(relative):
    source, target = PROJECT / relative, ROOT / relative
    assert source.is_file(), source
    target.parent.mkdir(parents=True, exist_ok=True)
    shutil.copy2(source, target)


for folder in ('Models', 'Textures', 'Materials'):
    base = f'Assets/{folder}/Buildings/Limestone'
    for source in (PROJECT / base).iterdir():
        if source.is_file() and (not extension or source.name.startswith(tuple(['L0' + str(i) + '_Baked' for i in range(4,10)] + ['LimestoneExtension']))):
            copy(str(source.relative_to(PROJECT)))
    if not (ROOT / (base + '.meta')).exists(): copy(base + '.meta')

slugs = ['l04-limestone-pier','l05-limestone-paving','l06-limestone-footing','l07-limewashed-wall','l08-limestone-plinth','l09-limestone-doorway'] if extension else ['l01-coursed-limestone', 'l02-dressed-limestone', 'l03-narrow-limestone']
guids = []
for slug in slugs:
    base = 'Assets/Resources/Building/' + slug
    text = (PROJECT / (base + '.asset')).read_text()
    assert 'contentId: build.stone.' + slug in text
    guid = re.search(r'^guid: ([a-f0-9]{32})$', (PROJECT / (base + '.asset.meta')).read_text(), re.M).group(1)
    guids.append(guid)
    for suffix in ('.asset', '.asset.meta', '.prefab', '.prefab.meta'):
        copy(base + suffix)
    for suffix in ('.png', '.png.meta'):
        copy('Assets/Resources/Building/UI/Thumbnails/build_stone_' + slug + suffix)

catalog = ROOT / 'Assets/Resources/Building/PrototypeCatalog.asset'
original = catalog.read_bytes().decode('utf-8')
newline = '\r\n' if '\r\n' in original else '\n'
additions = ''.join('  - {fileID: 11400000, guid: ' + guid + ', type: 2}' + newline
                    for guid in guids if 'guid: ' + guid + ',' not in original)
assert original.count('  validPreview:') == 1
changed = original.replace('  validPreview:', additions + '  validPreview:', 1)
assert changed.replace(additions, '', 1) == original if additions else changed == original
catalog.write_bytes(changed.encode('utf-8'))

for source in (PROJECT / ('ArtReferences/LimestoneBuildKit/' + evidence)).iterdir():
    if source.suffix in ('.png', '.json'):
        copy(str(source.relative_to(PROJECT)))
shutil.copy2(PROJECT / '.utmp/stone-kit/validation.json', ROOT / ('ArtReferences/LimestoneBuildKit/' + evidence + '/stone-regression.json'))
if extension:
    shutil.copy2(PROJECT / 'ArtReferences/LimestoneBuildKit/Models/unity-validation.json', ROOT / 'ArtReferences/LimestoneBuildKit/Extension/l01-l03-regression.json')
print('INSTALLED ' + ('L04-L09: 18 FBXs and six components' if extension else 'L01-L03: 9 FBXs and three walls') + '; scoped maps/material, prefabs/icons and catalog references.')
