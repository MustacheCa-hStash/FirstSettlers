"""Prepare/install isolated HQ tree bakes and preserve the original assets."""
from pathlib import Path
import argparse, hashlib, json, re, shutil, subprocess, uuid

ROOT=Path(__file__).resolve().parents[1]
PROJECT=ROOT/'.utmp/oak-materials/unity-project'
LOGS=ROOT/'Logs/TreeHQ'
SPECS=[
 ('RedMaple','RedMaple_LOD0_v03','M_RedMaple_Leaf','M_RedMapleBark_Stylized',0,'mapleTreePrefab',['mapleTreeBillboardPrefab','grasslandMapleTreeBillboardPrefab'],'MapleTree'),
 ('SugarMaple','SugarMaple_LOD0_v04','M_SugarMapleLeaf','M_SugarMapleBark_Stylized',1,'sugarMapleTreePrefab',['sugarMapleTreeBillboardPrefab'],'SugarMapleTree'),
 ('Oak','Oak_LOD0_v03','M_Oak_Leaf','M_Oak_Bark',2,'oakTreePrefab',['oakTreeBillboardPrefab','grasslandOakTreeBillboardPrefab'],'OakTree'),
 ('Birch','Birch_LOD0_v04','M_Birch_Leaf','M_Birch_Bark',3,'birchAspenTreePrefab',['birchAspenTreeBillboardPrefab','grasslandBirchAspenTreeBillboardPrefab'],'BirchAspenTree'),
 ('WhitePine','WhitePine_LOD0_v01','M_WhitePineLeaves','M_WhitePineBark',-1,'whitePineTreePrefab',['whitePineTreeBillboardPrefab','grasslandWhitePineTreeBillboardPrefab'],'WhitePineTree'),
]
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest().upper()
def copy(path):
 src=ROOT/path;dst=PROJECT/path
 dst.parent.mkdir(parents=True,exist_ok=True)
 shutil.copy2(src,dst)
 meta=Path(str(src)+'.meta')
 if meta.exists():shutil.copy2(meta,Path(str(dst)+'.meta'))
def guid(path):return re.search(r'^guid: (\w+)',Path(str(ROOT/path)+'.meta').read_text(),re.M)[1]
def map_guid():
 paths=subprocess.check_output(['rg','--files','Assets','-g','*.meta'],cwd=ROOT,text=True).splitlines()
 result={}
 for name in paths:
  p=ROOT/name
  m=re.search(r'^guid: (\w+)',p.read_text(errors='ignore'),re.M)
  if m:result[m[1]]=p.with_suffix('')
 return result
def configs(index):
 result=[]
 for name,prefab,leaf,bark,mode,slot,slots,variant in SPECS:
  leaf=f'Assets/Materials/M_Trees/{name}/{leaf}.mat';bark=f'Assets/Materials/M_Trees/{name}/{bark}.mat'
  def texture(material):
   m=re.search(r'- _BaseMap:\s*\n\s*m_Texture: \{[^}]*guid: (\w+)',(ROOT/material).read_text())
   assert m and m[1] in index,material+' has no resolvable base map'
   return str(index[m[1]].relative_to(ROOT)).replace('\\','/')
  result.append(dict(species=name,sourcePrefab=f'Assets/Prefabs/{prefab}.prefab',leafPath=leaf,barkPath=bark,
   leafTexture=texture(leaf),barkTexture=texture(bark),paletteMode=mode,sourceSlot=slot,billboardSlots=slots,variant=variant,
   expansion=.75 if mode<0 else .5,shaderName=f'Hidden/TreeImpostor/{name}HQSemanticCapture' if name in ['RedMaple','SugarMaple'] else f'Hidden/TreeImpostor/{name}SemanticCapture'))
 return result
def old_paths():
 paths=set()
 for name,*_ in SPECS:
  for p in [ROOT/f'Assets/Textures/Trees/Impostors/{name}',ROOT/f'Assets/Materials/M_Trees/{name}',ROOT/f'Assets/Prefabs/{name}_OctaImpostor_Runtime.prefab',ROOT/f'Assets/Models/Trees/{name}_OctaImpostor_Runtime.mesh']:
   if p.is_dir():paths.update(q for q in p.rglob('*') if q.is_file())
   elif p.exists():paths.add(p)
   if Path(str(p)+'.meta').exists():paths.add(Path(str(p)+'.meta'))
 # Also preserve the approved spruce HQ and shared runtime behavior.
 for base in ['Assets/Textures/Trees/Impostors/SpruceHQ','Assets/Materials/M_Trees/SpruceHQ','Assets/Prefabs/SpruceHQ_OctaImpostor_Runtime.prefab','Assets/Models/Trees/SpruceHQ_OctaImpostor_Runtime.mesh','Assets/Shaders/SpruceOctaImpostor.shader']:
  p=ROOT/base
  if p.is_dir():paths.update(q for q in p.rglob('*') if q.is_file())
  elif p.exists():paths.add(p)
  if Path(str(p)+'.meta').exists():paths.add(Path(str(p)+'.meta'))
 return paths
def prepare():
 LOGS.mkdir(parents=True,exist_ok=True)
 copy('ProjectSettings/QualitySettings.asset')
 baseline=LOGS/'OriginalAssetHashes.json'
 if not baseline.exists():baseline.write_text(json.dumps({str(p.relative_to(ROOT)).replace('\\','/'):digest(p) for p in old_paths()},indent=2))
 index=map_guid();cs=configs(index)
 config=ROOT/'Tools/TreeHQSpecies.json';config.write_text(json.dumps({'species':cs},indent=2));copy('Tools/TreeHQSpecies.json')
 seen=set();pending=[]
 for c in cs:
  pending += [ROOT/c['sourcePrefab'],ROOT/c['leafPath'],ROOT/c['barkPath'],ROOT/f"Assets/Prefabs/{c['species']}_OctaImpostor_Runtime.prefab"]
 pending += [ROOT/'Assets/Settings/PC_RPAsset.asset']
 while pending:
  p=pending.pop()
  if p in seen or not p.is_file():continue
  seen.add(p);rel=str(p.relative_to(ROOT)).replace('\\','/');copy(rel)
  text=Path(str(p)+'.meta').read_text(errors='ignore') if Path(str(p)+'.meta').exists() else ''
  if p.suffix in ['.prefab','.mat','.asset','.shader','.hlsl','.cs','.json']:text+='\n'+p.read_text(errors='ignore')
  for g in re.findall(r'guid:\s*([0-9a-f]{32})',text):
   if g in index:pending.append(index[g])
  for include in re.findall(r'#include\s+"(Assets/[^"\n]+)"',text):pending.append(ROOT/include)
 for name in ['TreeHQImpostorBaker','TreeHQImpostorValidation','TreeImpostorAtlasCaptureWindow','TreeImpostorAmbientAtlasBakerWindow','MapleImpostorColorValidation','OakImpostorColorValidation','BirchImpostorColorValidation','SpruceImpostorGpuValidation','DistantTreeGpuValidation','TreeImpostorGroundingValidation','MapleImpostorMaterialSettings','OakOctaImpostorBaker','BirchOctaImpostorBaker','SpruceHQOctaImpostorBaker']:
  path=f'Assets/Editor/{name}.cs';p=ROOT/(path+'.meta')
  if not p.exists():p.write_text('fileFormatVersion: 2\nguid: '+uuid.uuid4().hex+'\n')
  copy(path)
 for name in ['RedMaple','SugarMaple']:copy(f'Assets/Shaders/{name}HQImpostorSemanticCapture.shader')
 copy('Assets/Shaders/TreeHQOctaImpostor.shader')
 # Keep identities when rebaking an installed HQ version.
 for c in cs:
  name=c['species']+'HQ'
  for path in [f'Assets/Textures/Trees/Impostors/{name}',f'Assets/Materials/M_Trees/{name}',f'Assets/Models/Trees/{name}_OctaImpostor_Runtime.mesh',f'Assets/Prefabs/{name}_OctaImpostor_Runtime.prefab']:
   p=ROOT/path
   if p.is_dir():
    for f in p.rglob('*'):
     if f.is_file() and f.suffix!='.meta':copy(str(f.relative_to(ROOT)).replace('\\','/'))
    if Path(str(p)+'.meta').exists():copy(path+'.meta')
   elif p.exists():copy(path)
 print('Prepared',len(seen),'source dependencies for',', '.join(c['species'] for c in cs))
def install():
 baseline=json.loads((LOGS/'OriginalAssetHashes.json').read_text())
 for p,h in baseline.items():assert digest(ROOT/p)==h,'Original changed: '+p
 cs=json.loads((ROOT/'Tools/TreeHQSpecies.json').read_text())['species']
 scene_path=ROOT/'Assets/Scenes/SmearScene.unity';scene=scene_path.read_text()
 # Verify every record and source assignment before changing the scene.
 for c in cs:
  n=c['species']+'HQ';record=json.loads((PROJECT/f'Assets/Textures/Trees/Impostors/{n}/{n}_Octa_Validation.json').read_text())
  assert json.loads(record['configuration'])==c,'Configuration changed after validation: '+c['species']
  for sig in record['inputs']:assert digest(ROOT/sig['path'])==sig['sha256'],'Input changed: '+sig['path']
  for sig in record['outputs']:assert digest(PROJECT/sig['path'])==sig['sha256'],'Output changed: '+sig['path']
  assert re.search(c['sourceSlot']+r': \{[^}]*guid: '+guid(c['sourcePrefab']),scene),c['sourceSlot']
 for c in cs:
  n=c['species']+'HQ'
  for rel in [f'Assets/Textures/Trees/Impostors/{n}',f'Assets/Materials/M_Trees/{n}',f'Assets/Models/Trees/{n}_OctaImpostor_Runtime.mesh',f'Assets/Prefabs/{n}_OctaImpostor_Runtime.prefab']:
   src=PROJECT/rel;dst=ROOT/rel;dst.parent.mkdir(parents=True,exist_ok=True)
   if src.is_dir():shutil.copytree(src,dst,dirs_exist_ok=True)
   else:shutil.copy2(src,dst)
   shutil.copy2(Path(str(src)+'.meta'),Path(str(dst)+'.meta'))
  prefab=f'Assets/Prefabs/{n}_OctaImpostor_Runtime.prefab';fid=re.search(r'--- !u!1 &(-?\d+)',(ROOT/prefab).read_text())[1]
  for slot in c['billboardSlots']:
   scene,count=re.subn(r'(?m)(    '+slot+r': )\{[^\r\n]+\}',lambda m:m[1]+f'{{fileID: {fid}, guid: {guid(prefab)}, type: 3}}',scene)
   assert count==1,slot
 # Unconfigured species share spruce's source prefab and now its approved HQ fallback.
 p='Assets/Prefabs/SpruceHQ_OctaImpostor_Runtime.prefab';fid=re.search(r'--- !u!1 &(-?\d+)',(ROOT/p).read_text())[1]
 for slot in ['treeBillboardPrefab','grasslandTreeBillboardFallbackPrefab']:
  scene,count=re.subn(r'(?m)(    '+slot+r': )\{[^\r\n]+\}',lambda m:m[1]+f'{{fileID: {fid}, guid: {guid(p)}, type: 3}}',scene);assert count==1,slot
 scene_path.write_text(scene)
 for p,h in baseline.items():assert digest(ROOT/p)==h,'Original changed after install: '+p
 output=PROJECT.parent/'render-results'
 for f in output.glob('tree-hq-*'):shutil.copy2(f,LOGS/f.name)
 print('Installed five HQ tree impostors, assigned nine species slots and both spruce fallback slots; originals preserved.')
if __name__=='__main__':
 ap=argparse.ArgumentParser();ap.add_argument('--install',action='store_true');args=ap.parse_args()
 install() if args.install else prepare()
