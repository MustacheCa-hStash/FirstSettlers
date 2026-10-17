"""Copy validated isolated Unity imports; edit only two definitions' visual references.
Run after BakedFurnishingsValidation.InstallAndRunBatch and local flame Play checks.
"""
import json,re,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'.utmp/baked-stone-wall/unity-project'
proof=json.loads((PROJECT/'ArtReferences/BakedFurnishings/unity-validation.json').read_text())
assert proof['passed'] and proof['torchTriangles']==280 and proof['platformTriangles']==320
assert json.loads((PROJECT/'.utmp/furniture-kit/validation.json').read_text())['passed']
flame_log=(ROOT/'.utmp/baked-furnishings-flame.log').read_text()
flame_checks=re.search(r'LOCAL FLAME PLAY PASS: (\d+) checks',flame_log)
assert flame_checks
def copy(relative):
    src=PROJECT/relative;dst=ROOT/relative;dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst)
for folder,name,mat in [('Lighting','MountedTorch','Assets/Resources/Lighting/BakedMountedTorch.mat'),('Furniture','SleepingPlatform','Assets/Materials/Buildings/Wood/BakedSleepingPlatform.mat')]:
    for asset in [f'Assets/Models/Buildings/{folder}/{name}_Baked.fbx',f'Assets/Textures/Buildings/{folder}/{name}_Baked_ColourAO.png',f'Assets/Textures/Buildings/{folder}/{name}_Baked_Normal.png',mat]:
        for suffix in ['', '.meta']:copy(asset+suffix)
copy('Assets/Textures/Buildings/Furniture.meta')
for slug,key in [('wall-torch','build_lighting_wall-torch'),('f05-sleeping-platform','build_furniture_f05-sleeping-platform')]:
    relative=f'Assets/Resources/Building/{slug}'
    tested=(PROJECT/(relative+'.asset')).read_text();path=ROOT/(relative+'.asset');original=path.read_text();changed=original
    refs={}
    for field in ['mesh','material']:
        refs[field]=re.search(r'^  '+field+r': (.+)$',tested,re.M).group(1)
        changed,count=re.subn(r'^  '+field+r': .+$','  '+field+': '+refs[field],changed,flags=re.M);assert count==1
    assert re.sub(r'^  (mesh|material): .+$','',changed,flags=re.M)==re.sub(r'^  (mesh|material): .+$','',original,flags=re.M)
    path.write_text(changed)
    path=ROOT/(relative+'.prefab');original=path.read_text();changed,count=re.subn(r'^  m_Mesh: .+$','  m_Mesh: '+refs['mesh'],original,flags=re.M);assert count==1
    changed,count=re.subn(r'(  m_Materials:\n  - )[^\n]+',lambda m:m.group(1)+refs['material'],changed);assert count==1
    def nonvisual(text):return re.sub(r'(  m_Materials:\n  - )[^\n]+','',re.sub(r'^  m_Mesh: .+$','',text,flags=re.M))
    assert nonvisual(changed)==nonvisual(original),'Nonvisual prefab data changed'
    path.write_text(changed)
    copy(f'Assets/Resources/Building/UI/Thumbnails/{key}.png')
for file in (PROJECT/'ArtReferences/BakedFurnishings').iterdir():
    if file.suffix in ['.png','.json','.tsv']:copy(str(file.relative_to(PROJECT)))
shutil.copy2(PROJECT/'.utmp/furniture-kit/validation.json',ROOT/'ArtReferences/BakedFurnishings/furniture-runtime-validation.json')
(ROOT/'ArtReferences/BakedFurnishings/flame-runtime-validation.json').write_text(json.dumps({'passed':True,'checks':int(flame_checks.group(1)),'editorStartupNote':'Unity SearchDatabase indexer threw a startup exception; the independent gameplay checks completed successfully.' if 'UnityEditor.Search.SearchDatabase' in flame_log else None},indent=2))
print('INSTALLED: visual references only; original collision, flame, placement and prefab IDs preserved.')
