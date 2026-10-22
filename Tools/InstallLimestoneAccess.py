"""Install only the validated L18-L24 assets, preserving unrelated catalog bytes."""
import hashlib,json,re,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'.utmp/limestone-kit/unity-project'
ART='ArtReferences/LimestoneBuildKit/Access'
proof=json.loads((PROJECT/ART/'unity-validation.json').read_text())
assert proof['passed'] and proof['parts']==7 and proof['stairWalks']==160 and proof['corniceCases']==56 and proof['terrainCases']==16
regressions={
    'courtyard-regression.json':'ArtReferences/LimestoneBuildKit/Courtyard/unity-validation.json',
    'timber-regression.json':'ArtReferences/LimestoneBuildKit/TimberInfill/unity-validation.json',
    'l01-l03-regression.json':'ArtReferences/LimestoneBuildKit/Models/unity-validation.json',
    'l04-l09-regression.json':'ArtReferences/LimestoneBuildKit/Extension/unity-validation.json',
    'stone-regression.json':'.utmp/stone-kit/validation.json',
    'modular-regression.json':'.utmp/modular-kit/validation.json'}
for source in regressions.values():assert json.loads((PROJECT/source).read_text())['passed'],source
manifest=json.loads((ROOT/ART/'access-models.json').read_text())
assert hashlib.sha256((ROOT/ART/'LimestoneAccess.blend').read_bytes()).hexdigest()==manifest['blend_sha256']
def copy(relative):
    source,target=PROJECT/relative,ROOT/relative
    assert source.is_file(),source
    target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
for kind in ('Models','Textures','Materials'):
    base='Assets/'+kind+'/Buildings/LimestoneAccess'
    for source in (PROJECT/base).rglob('*'):
        if source.is_file():copy(str(source.relative_to(PROJECT)))
    if not(ROOT/(base+'.meta')).exists():copy(base+'.meta')
guids=[];resource='Assets/Resources/Building/'
for p in manifest['parts']:
    slug=p['slug']
    for source in (PROJECT/resource).glob(slug+'*'):
        if source.is_file():copy(str(source.relative_to(PROJECT)))
    assert 'contentId: build.stone.'+slug in (PROJECT/resource/(slug+'.asset')).read_text()
    if p.get('slope')!='falling':guids.append(re.search(r'^guid: ([a-f0-9]{32})$',(PROJECT/resource/(slug+'.asset.meta')).read_text(),re.M).group(1))
    for suffix in ('.png','.png.meta'):copy(resource+'UI/Thumbnails/build_stone_'+slug+suffix)
catalog=ROOT/resource/'PrototypeCatalog.asset';original=catalog.read_bytes().decode('utf-8');newline='\r\n' if '\r\n' in original else '\n'
additions=''.join('  - {fileID: 11400000, guid: '+guid+', type: 2}'+newline for guid in guids if 'guid: '+guid+',' not in original)
assert len(guids)==7 and original.count('  validPreview:')==1
changed=original.replace('  validPreview:',additions+'  validPreview:',1)
assert changed.replace(additions,'',1)==original if additions else changed==original
catalog.write_bytes(changed.encode('utf-8'))
for source in (PROJECT/ART).glob('*.png'):copy(str(source.relative_to(PROJECT)))
copy(ART+'/unity-validation.json')
for name,source in regressions.items():shutil.copy2(PROJECT/source,ROOT/ART/name)
print('INSTALLED L18-L24: seven catalog entries, reversible cornice, solid stairs, bedded landing, retaining wall, buttress, baked material and thumbnails.')
