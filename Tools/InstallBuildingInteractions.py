"""Copy only the validated interaction-dimension assets; preserve scenes, catalog and unrelated art."""
import argparse
import hashlib
from pathlib import Path
import re
import shutil

ROOT = Path(__file__).resolve().parents[1]
NAMES = [
    'l01-coursed-limestone', 'l02-dressed-limestone', 'l03-narrow-limestone',
    'l07-limewashed-wall', 'l08-limestone-plinth', 'l09-limestone-doorway',
    'l10-arched-limestone-doorway', 'l11-narrow-limestone-window',
    'l12-arched-limestone-window', 'l13-low-courtyard-wall', 'l14-short-courtyard-wall',
    'l15-chamfered-limestone-coping', 'l17-limestone-gable-rising', 'l17-limestone-gable-falling',
    'l18-limestone-eaves-cornice', 'l19-threshold-steps',
    'l20-half-storey-limestone-stair', 'l21-full-storey-limestone-stair',
    'l22-solid-bedded-limestone-landing', 'l35-braced-door-canopy',
]

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.exists() else None

def guid(path):
    return re.search(r'^guid: (\w+)', path.read_text(), re.M)[1]

def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--project', default='.utmp/building-interactions/unity-project')
    parser.add_argument('--dry-run', action='store_true')
    args = parser.parse_args()
    project = (ROOT / args.project).resolve()
    if not project.is_relative_to(ROOT / '.utmp'):
        raise ValueError('Validated source project must be inside this workspace .utmp directory')
    report = project / '.utmp/interaction/validation.json'
    import json
    if not json.loads(report.read_text()).get('passed'):
        raise ValueError('Interaction validation has not passed')
    files = set()
    for name in NAMES:
        rel = Path('Assets/Resources/Building') / name
        source = project / rel.with_suffix('.asset')
        target = ROOT / rel.with_suffix('.asset')
        if not source.exists() or not target.exists():
            raise ValueError(f'Missing installed definition: {name}')
        if guid(Path(str(source)+'.meta')) != guid(Path(str(target)+'.meta')):
            raise ValueError(f'Definition GUID changed: {name}')
        if re.search(r'^  contentId: (.+)$', source.read_text(), re.M)[1] != re.search(r'^  contentId: (.+)$', target.read_text(), re.M)[1]:
            raise ValueError(f'Definition identity changed: {name}')
        for suffix in ['.asset', '.prefab']:
            files.add(rel.with_suffix(suffix))
        files.update(p.relative_to(project) for p in source.parent.glob(name+'-modular*.asset'))
        family='wood' if name.startswith('l35-') else 'stone'
        files.add(Path('Assets/Resources/Building/UI/Thumbnails') / ('build_'+family+'_'+name+'.png'))
    files |= {Path(str(p)+'.meta') for p in files}
    planned=[]
    for rel in sorted(files):
        source, target = project/rel, ROOT/rel
        if not source.exists():
            raise ValueError(f'Missing validated dependency: {rel}')
        if target.exists() and rel.suffix=='.meta' and guid(source)!=guid(target):
            raise ValueError(f'Asset GUID changed: {rel}')
        if digest(source)!=digest(target):
            planned.append((rel, source, target))
    if not args.dry_run:
        backup=ROOT/'.utmp/building-interactions/install-backup'
        for rel, source, target in planned:
            if target.exists() and not (backup/rel).exists():
                (backup/rel).parent.mkdir(parents=True, exist_ok=True)
                shutil.copy2(target, backup/rel)
            target.parent.mkdir(parents=True, exist_ok=True)
            shutil.copy2(source, target)
    print(('Would install ' if args.dry_run else 'Installed ')+str(len(planned))+' changed files for 20 definitions; scene and catalog preserved.')

if __name__=='__main__':
    main()
