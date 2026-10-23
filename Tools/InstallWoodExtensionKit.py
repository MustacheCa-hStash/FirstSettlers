"""Install only validated W05/W06/W07/W08/W12 assets and append their catalog entries."""
import json,re,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'.utmp/wood-extension/unity-project'
ART='ArtReferences/IronAgeWood/Extension'
proof=json.loads((PROJECT/ART/'unity-validation.json').read_text())
assert proof['passed'] and proof['parts']==5 and proof['headings']==8
for relative in ['.utmp/modular-kit/validation.json','.utmp/stone-kit/validation.json']:
 assert json.loads((PROJECT/relative).read_text())['passed'],relative
def copy(relative):
 source=PROJECT/relative;target=ROOT/relative
 assert source.is_file(),source
 target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
models='Assets/Models/Buildings/Wood/Extension'
for source in (PROJECT/models).rglob('*'):
 if source.is_file():copy(str(source.relative_to(PROJECT)))
if not (ROOT/(models+'.meta')).exists():copy(models+'.meta')
resource='Assets/Resources/Building/'
parts=json.loads((ROOT/ART/'wood-extension-models.json').read_text())['parts']
guids=[]
for part in parts:
 if part['tag']=='Cover':continue
 slug=part['slug']
 for source in (PROJECT/resource).glob(slug+'*'):
  if source.is_file():copy(str(source.relative_to(PROJECT)))
 for suffix in ('.png','.png.meta'):copy(resource+'UI/Thumbnails/build_wood_'+slug+suffix)
 guids.append(re.search(r'^guid: ([a-f0-9]{32})$',(PROJECT/resource/(slug+'.asset.meta')).read_text(),re.M).group(1))
for suffix in ('.asset','.asset.meta'):copy(resource+'wood-extension-low-cover-mesh'+suffix)
catalog=ROOT/resource/'PrototypeCatalog.asset';original=catalog.read_bytes().decode('utf-8');newline='\r\n' if '\r\n' in original else '\n'
additions=''.join('  - {fileID: 11400000, guid: '+guid+', type: 2}'+newline for guid in guids if 'guid: '+guid+',' not in original)
assert len(guids)==5 and original.count('  validPreview:')==1
catalog.write_bytes(original.replace('  validPreview:',additions+'  validPreview:',1).encode('utf-8'))
for source in (PROJECT/ART).glob('*.png'):copy(str(source.relative_to(PROJECT)))
copy(ART+'/unity-validation.json')
for source,name in [('.utmp/modular-kit/validation.json','modular-regression.json'),('.utmp/stone-kit/validation.json','stone-regression.json')]:shutil.copy2(PROJECT/source,ROOT/ART/name)
print('INSTALLED W05/W06/W07/W08/W12: five definitions, authored prefabs, shared materials and mesh thumbnails.')
