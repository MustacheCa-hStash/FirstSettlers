"""Install only source-matched, validated sandstone court/access/household assets."""
import hashlib, json, re, shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'.utmp/sandstone-extension/unity-project'
ART='ArtReferences/SandstoneBuildKit/Extension'
manifest=json.loads((ROOT/ART/'sandstone-extension-models.json').read_text())
proof=json.loads((PROJECT/ART/'unity-validation.json').read_text())
assert proof['passed'] and proof['designs']==11 and proof['parts']==14 and proof['headings']==8
assert proof['stairWalks']==112 and proof['pocketCases']==32 and proof['triangles']==468
assert hashlib.sha256((ROOT/ART/'SandstoneExtension.blend').read_bytes()).hexdigest()==manifest['blend_sha256']
assert (ROOT/ART/'sandstone-extension-models.json').read_bytes()==(PROJECT/ART/'sandstone-extension-models.json').read_bytes()
for path in ['Assets/Editor/'+name+'.cs' for name in ['SandstoneExtensionSetup','SandstoneExtensionValidation','BuildingPrototypeSetup']]+['Assets/Scripts/Building/'+name+'.cs' for name in ['BuildCorbelPocket','BuildResolution','BuildUiContent']]:
    assert (ROOT/path).read_bytes()==(PROJECT/path).read_bytes(),'Stale validation code: '+path
for part in manifest['parts']:
    path='Assets/Models/Buildings/SandstoneExtension/'+part['file']
    assert hashlib.sha256((ROOT/path).read_bytes()).hexdigest()==part['sha256']
    assert (ROOT/path).read_bytes()==(PROJECT/path).read_bytes()
for name in ['SandstoneExtension_ColourAO.png','SandstoneExtension_Normal.png']:
    path='Assets/Textures/Buildings/SandstoneExtension/'+name
    assert (ROOT/path).read_bytes()==(PROJECT/path).read_bytes()
regressions={
    'ArtReferences/SandstoneBuildKit/Models/unity-validation.json':'ss01-ss11-regression.json',
    '.utmp/stone-kit/validation.json':'stone-regression.json',
    '.utmp/modular-kit/validation.json':'wood-regression.json',
    '.utmp/interaction/validation.json':'interaction-regression.json',
}
for path in regressions:assert json.loads((PROJECT/path).read_text())['passed'],path
def copy(relative):
    source,target=PROJECT/relative,ROOT/relative
    assert source.is_file(),source
    target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
for kind in ['Models','Textures','Materials']:
    base='Assets/'+kind+'/Buildings/SandstoneExtension'
    for source in (PROJECT/base).rglob('*'):
        if source.is_file():copy(str(source.relative_to(PROJECT)))
    if not (ROOT/(base+'.meta')).exists():copy(base+'.meta')
resource='Assets/Resources/Building/'
guids=[]
for part in manifest['parts']:
    slug=part['slug'];prefix='furniture' if part['kind']==7 else 'stone'
    for source in (PROJECT/resource).glob(slug+'*'):
        if source.is_file():copy(str(source.relative_to(PROJECT)))
    assert 'contentId: build.'+prefix+'.'+slug in (PROJECT/resource/(slug+'.asset')).read_text()
    guids.append(re.search(r'^guid: ([a-f0-9]{32})$',(PROJECT/resource/(slug+'.asset.meta')).read_text(),re.M).group(1))
    for suffix in ['.png','.png.meta']:copy(resource+'UI/Thumbnails/build_'+prefix+'_'+slug+suffix)
catalog=ROOT/resource/'PrototypeCatalog.asset';original=catalog.read_bytes().decode('utf-8');newline='\r\n' if '\r\n' in original else '\n'
additions=''.join('  - {fileID: 11400000, guid: '+guid+', type: 2}'+newline for guid in guids if 'guid: '+guid+',' not in original)
assert len(guids)==14 and original.count('  validPreview:')==1
updated=original.replace('  validPreview:',additions+'  validPreview:',1)
assert updated.replace(additions,'',1)==original if additions else updated==original
catalog.write_bytes(updated.encode('utf-8'))
for source in (PROJECT/ART).glob('*.png'):copy(str(source.relative_to(PROJECT)))
copy(ART+'/unity-validation.json')
for source,name in regressions.items():shutil.copy2(PROJECT/source,ROOT/ART/name)
print('INSTALLED SS13 and SS15-SS24: eleven designs, fourteen choices, 468 triangles, hollow collision, stairs and reversible corbel fitting.')
