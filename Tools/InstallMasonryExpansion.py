"""Install only this kit's validated assets and append its eleven catalog references."""
import hashlib,json,re,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT/'.utmp/masonry-expansion/unity-project';ART='ArtReferences/MasonryExpansion'
proof=json.loads((PROJECT/ART/'unity-validation.json').read_text())
assert proof['passed'] and proof['parts']==11 and proof['headings']==8 and proof['motorWalks']==128
manifest=json.loads((ROOT/ART/'masonry-expansion-models.json').read_text())
assert hashlib.sha256((ROOT/ART/'MasonryExpansion.blend').read_bytes()).hexdigest()==manifest['blend_sha256']
assert (PROJECT/ART/'masonry-expansion-models.json').read_bytes()==(ROOT/ART/'masonry-expansion-models.json').read_bytes(),'Validation source is stale'
reports={'.utmp/modular-kit/validation.json':'wood-regression.json','.utmp/stone-kit/validation.json':'stone-regression.json','.utmp/interaction/validation.json':'interaction-regression.json'}
for path in reports:assert json.loads((PROJECT/path).read_text())['passed'],path
def copy(relative):
    source=PROJECT/relative;target=ROOT/relative;assert source.is_file(),source
    target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
for kind in ('Models','Textures','Materials'):
    folder='Assets/'+kind+'/Buildings/MasonryExpansion'
    for source in (PROJECT/folder).rglob('*'):
        if source.is_file():copy(str(source.relative_to(PROJECT)))
    if not (ROOT/(folder+'.meta')).exists():copy(folder+'.meta')
resource='Assets/Resources/Building/';guids=[]
for part in manifest['parts']:
    slug=part['slug']
    for source in (PROJECT/resource).glob('mx-'+slug+'*'):
        if source.is_file():copy(str(source.relative_to(PROJECT)))
    guids.append(re.search(r'^guid: ([a-f0-9]{32})$',(PROJECT/resource/('mx-'+slug+'.asset.meta')).read_text(),re.M).group(1))
    prefix='wood' if part['style']=='wood' else 'stone'
    for suffix in ('.png','.png.meta'):copy(resource+'UI/Thumbnails/build_'+prefix+'_'+slug+suffix)
catalog=ROOT/resource/'PrototypeCatalog.asset';original=catalog.read_bytes().decode('utf-8');newline='\r\n' if '\r\n' in original else '\n'
additions=''.join('  - {fileID: 11400000, guid: '+guid+', type: 2}'+newline for guid in guids if 'guid: '+guid+',' not in original)
assert len(guids)==11 and original.count('  validPreview:')==1
catalog.write_bytes(original.replace('  validPreview:',additions+'  validPreview:',1).encode('utf-8'))
for source in (PROJECT/ART).glob('*.png'):copy(str(source.relative_to(PROJECT)))
copy(ART+'/unity-validation.json')
for source,name in reports.items():shutil.copy2(PROJECT/source,ROOT/ART/name)
print('INSTALLED MASONRY EXPANSION: eleven static components, source-matched FBXs, prefabs, collision, textures and catalog icons.')
