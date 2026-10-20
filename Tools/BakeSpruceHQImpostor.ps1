param([switch]$PrepareOnly, [switch]$ValidateOnly, [switch]$InstallOnly)
$ErrorActionPreference='Stop'
$spruceRoot=Split-Path -Parent $PSScriptRoot
$spruceProject=Join-Path $spruceRoot '.utmp/oak-materials/unity-project'
$spruceProducts=@('Textures/Trees/Impostors/SpruceHQ','Materials/M_Trees/SpruceHQ','Models/Trees/SpruceHQ_OctaImpostor_Runtime.mesh','Prefabs/SpruceHQ_OctaImpostor_Runtime.prefab')
function Copy-SpruceProduct($source,$destination){
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    if((Get-Item -LiteralPath $source).PSIsContainer){Copy-Item -LiteralPath $source -Destination (Split-Path -Parent $destination) -Recurse -Force}
    else{Copy-Item -LiteralPath $source -Destination $destination -Force}
    Copy-Item -LiteralPath ($source+'.meta') -Destination ($destination+'.meta') -Force
}
if(-not $InstallOnly){
    & (Join-Path $PSScriptRoot 'ValidateOakMaterials.ps1') -PrepareOnly | Out-Null
    $spruceEditors=@('OakOctaImpostorBaker','OakImpostorValidation','OakImpostorColorValidation','SpruceHQOctaImpostorBaker','SpruceHQImpostorValidation','TreeImpostorAtlasCaptureWindow','TreeImpostorAmbientAtlasBakerWindow','MapleImpostorMaterialSettings','MapleImpostorColorValidation','SpruceImpostorGpuValidation','DistantTreeGpuValidation','TreeImpostorGroundingValidation')
    foreach($name in $spruceEditors){
        $source=Join-Path $spruceRoot "Assets/Editor/$name.cs"
        if(-not(Test-Path -LiteralPath ($source+'.meta'))){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath ($source+'.meta')}
        Copy-Item -LiteralPath $source,($source+'.meta') -Destination (Join-Path $spruceProject 'Assets/Editor') -Force
    }
    foreach($path in @('Assets/Shaders/SpruceHQImpostorSemanticCapture.shader')){
        if(-not(Test-Path -LiteralPath "$spruceRoot/$path.meta")){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath "$spruceRoot/$path.meta"}
        Copy-Item -LiteralPath "$spruceRoot/$path.meta" -Destination "$spruceProject/$path.meta" -Force
    }
    foreach($path in @('Prefabs/Spruce_LOD0_v06.prefab','Models/Trees/Spruce_LOD0_P3310_B2930_L380_v06.fbx')){
        Copy-Item -LiteralPath "$spruceRoot/Assets/$path","$spruceRoot/Assets/$path.meta" -Destination (Split-Path -Parent "$spruceProject/Assets/$path") -Force
    }
    foreach($folder in @('Settings','Materials/M_Trees/Spruce','Materials/M_Trees/SugarMaple','Materials/M_Trees/RedMaple')){
        Copy-Item -LiteralPath "$spruceRoot/Assets/$folder" -Destination (Split-Path -Parent "$spruceProject/Assets/$folder") -Recurse -Force
    }
    foreach($path in @('Textures/Trees/Bark/SpruceBarkTextureSeamless.png','Textures/Trees/Bark/SpruceBark.png','Textures/Trees/leafClumpAtlas_Spruce.png','Textures/Trees/leafCard_Spruce_03.png','Textures/Trees/leafCard_Spruce_03_Snow.png')){
        Copy-Item -LiteralPath "$spruceRoot/Assets/$path","$spruceRoot/Assets/$path.meta" -Destination (Split-Path -Parent "$spruceProject/Assets/$path") -Force
    }
    foreach($folder in @('ScriptableObjects/WorldObjects','UI/Icons','Models/Primitives','Textures/Trees/Impostors/Spruce')){
        New-Item -ItemType Directory -Force (Split-Path -Parent "$spruceProject/Assets/$folder") | Out-Null
        Copy-Item -LiteralPath "$spruceRoot/Assets/$folder" -Destination (Split-Path -Parent "$spruceProject/Assets/$folder") -Recurse -Force
    }
    foreach($path in @('Prefabs/Spruce_OctaImpostor_Runtime.prefab','Models/Trees/Spruce_OctaImpostor_Runtime.mesh')){
        Copy-Item -LiteralPath "$spruceRoot/Assets/$path","$spruceRoot/Assets/$path.meta" -Destination (Split-Path -Parent "$spruceProject/Assets/$path") -Force
    }
    # Preserve generated Unity identities on subsequent bakes.
    foreach($path in $spruceProducts){
        if(Test-Path -LiteralPath "$spruceRoot/Assets/$path"){
            Copy-SpruceProduct "$spruceRoot/Assets/$path" "$spruceProject/Assets/$path"
        }
    }
    if($PrepareOnly){Write-Output $spruceProject;return}
    $spruceVersion=((Get-Content "$spruceRoot/ProjectSettings/ProjectVersion.txt" -First 1)-split ': ')[1]
    $spruceLog="$spruceRoot/.utmp/oak-materials/spruce-hq-$(if($ValidateOnly){'validate'}else{'bake'})-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss')).log"
    $spruceMethod=if($ValidateOnly){'SpruceHQImpostorValidation.ValidateBatch'}else{'SpruceHQOctaImpostorBaker.BakeBatch'}
    $spruceArgs=@('-batchmode','-projectPath',$spruceProject,'-executeMethod',$spruceMethod,'-logFile',$spruceLog)
    $spruceProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$spruceVersion/Editor/Unity.exe" -ArgumentList ($spruceArgs|ForEach-Object{'"'+$_+'"'}) -WindowStyle Hidden -PassThru
    Write-Output "SpruceHQ bake process $($spruceProcess.Id); log $spruceLog"
    $spruceTimer=[Diagnostics.Stopwatch]::StartNew()
    do{
        Start-Sleep -Seconds 3
        $spruceTail=if(Test-Path -LiteralPath $spruceLog){Get-Content -LiteralPath $spruceLog -Tail 100}else{@()}
        if($spruceTail -match 'SPRUCE HQ BAKE AND VALIDATION PASSED'){break}
        if($spruceTail -match 'Aborting batchmode due to failure|InvalidOperationException:|error CS[0-9]|Application will terminate with return code 1'){throw "SpruceHQ bake/validation failed; inspect $spruceLog"}
        if($spruceTimer.Elapsed.TotalMinutes -gt 45){throw "SpruceHQ bake timed out; inspect $spruceLog"}
    }while($true)
    if($ValidateOnly){Write-Output 'SpruceHQ validation passed.';return}
}
$spruceRecordPath="$spruceProject/Assets/Textures/Trees/Impostors/SpruceHQ/SpruceHQ_Octa_Validation.json"
if(-not(Test-Path -LiteralPath $spruceRecordPath)){throw 'No successful spruce HQ validation record. Run the bake/validation before installation.'}
$spruceRecord=Get-Content -LiteralPath $spruceRecordPath -Raw | ConvertFrom-Json
foreach($signature in $spruceRecord.inputs){
    $sourcePath=Join-Path $spruceRoot $signature.path
    if(-not(Test-Path -LiteralPath $sourcePath) -or (Get-FileHash -LiteralPath $sourcePath).Hash -ne $signature.sha256){throw "SpruceHQ input changed since validation: $($signature.path)"}
}
foreach($signature in $spruceRecord.outputs){
    $outputPath=Join-Path $spruceProject $signature.path
    if((Get-FileHash -LiteralPath $outputPath).Hash -ne $signature.sha256){throw "SpruceHQ output changed since validation: $($signature.path)"}
}
foreach($path in $spruceProducts){
    if(-not(Test-Path -LiteralPath "$spruceProject/Assets/$path")){throw "Missing validated bake output: $path"}
    Copy-SpruceProduct "$spruceProject/Assets/$path" "$spruceRoot/Assets/$path"
}
$spruceScenePath=Join-Path $spruceRoot 'Assets/Scenes/SmearScene.unity'
$spruceScene=[IO.File]::ReadAllText($spruceScenePath)
$spruceSourceGuid=((Get-Content "$spruceRoot/Assets/Prefabs/Spruce_LOD0_v06.prefab.meta"|Select-String '^guid:').ToString()-split ': ')[1]
foreach($slot in @('spruceTreePrefab')){
    if($spruceScene -notmatch ($slot+': \{fileID: [^,]+, guid: '+$spruceSourceGuid+',')){throw "World Manager $slot no longer points to Spruce_LOD0_v06; review before assigning this bake."}
}
$spruceProxyGuid=((Get-Content "$spruceRoot/Assets/Prefabs/SpruceHQ_OctaImpostor_Runtime.prefab.meta"|Select-String '^guid:').ToString()-split ': ')[1]
$sprucePrefab=Get-Content "$spruceRoot/Assets/Prefabs/SpruceHQ_OctaImpostor_Runtime.prefab" -Raw
$spruceRootFileId=[regex]::Match($sprucePrefab,'--- !u!1 &(-?\d+)').Groups[1].Value
foreach($slot in @('spruceTreeBillboardPrefab')){
    $pattern='(?m)(    '+$slot+': )\{[^\r\n]+\}'
    if([regex]::Matches($spruceScene,$pattern).Count -ne 1){throw "Expected exactly one $slot in SmearScene."}
    $spruceScene=[regex]::Replace($spruceScene,$pattern,('${1}{fileID: '+$spruceRootFileId+', guid: '+$spruceProxyGuid+', type: 3}'))
}
[IO.File]::WriteAllText($spruceScenePath,$spruceScene,[Text.UTF8Encoding]::new($false))
Write-Output 'Installed separate spruce HQ atlases, material, mesh and prefab. Assigned the spruce-specific billboard slot; original fallback assets remain intact.'
