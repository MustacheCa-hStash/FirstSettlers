"""Scoped copy of validated stone additions; preserves other assets and catalog references."""
import hashlib,json,re,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];PROJECT=ROOT/'.utmp/stone-completion/unity-project';ART='ArtReferences/StoneBuildKit/Completion'
proof=json.loads((PROJECT/ART/'unity-validation.json').read_text());assert proof['passed'] and proof['parts']==16 and proof['headings']==8 and proof['motorWalks']==160
reports={'.utmp/stone-kit/validation.json':'stone-regression.json','.utmp/modular-kit/validation.json':'wood-regression.json','ArtReferences/LimestoneBuildKit/Access/unity-validation.json':'access-regression.json'}
for path in reports:assert json.loads((PROJECT/path).read_text())['passed'],path
manifest=json.loads((ROOT/ART/'stone-completion-models.json').read_text());assert hashlib.sha256((ROOT/ART/'StoneCompletionKit.blend').read_bytes()).hexdigest()==manifest['blend_sha256']
def copy(relative):
    source=PROJECT/relative;target=ROOT/relative;assert source.is_file(),source
    target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
for kind in ('Models','Textures','Materials'):
    folder='Assets/'+kind+'/Buildings/StoneCompletion'
    for source in (PROJECT/folder).rglob('*'):
        if source.is_file():copy(str(source.relative_to(PROJECT)))
    if not (ROOT/(folder+'.meta')).exists():copy(folder+'.meta')
resource='Assets/Resources/Building/';guids=[]
for part in manifest['parts']:
    slug=part['slug']
    for source in (PROJECT/resource).glob(slug+'*'):
        if source.is_file():copy(str(source.relative_to(PROJECT)))
    if part.get('slope')!='falling':guids.append(re.search(r'^guid: ([a-f0-9]{32})$',(PROJECT/resource/(slug+'.asset.meta')).read_text(),re.M).group(1))
    prefix='wood' if slug.startswith(('s10-','s12-','s16-')) else 'stone'
    for suffix in ('.png','.png.meta'):copy(resource+'UI/Thumbnails/build_'+prefix+'_'+slug+suffix)
catalog=ROOT/resource/'PrototypeCatalog.asset';original=catalog.read_bytes().decode('utf-8');newline='\r\n' if '\r\n' in original else '\n'
additions=''.join('  - {fileID: 11400000, guid: '+guid+', type: 2}'+newline for guid in guids if 'guid: '+guid+',' not in original)
assert len(guids)==16 and original.count('  validPreview:')==1
catalog.write_bytes(original.replace('  validPreview:',additions+'  validPreview:',1).encode('utf-8'))
for source in (PROJECT/ART).glob('*.png'):copy(str(source.relative_to(PROJECT)))
copy(ART+'/unity-validation.json')
for source,name in reports.items():shutil.copy2(PROJECT/source,ROOT/ART/name)
print('INSTALLED STONE COMPLETION: 16 pieces, mirrored gable, static leaves, baked atlas, prefabs and icons.')
