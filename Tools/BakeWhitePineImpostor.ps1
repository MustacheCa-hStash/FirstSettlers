param([switch]$PrepareOnly, [switch]$ValidateOnly, [switch]$InstallOnly)
$ErrorActionPreference='Stop'
$pineRoot=Split-Path -Parent $PSScriptRoot
$pineProject=Join-Path $pineRoot '.utmp/oak-materials/unity-project'
$pineProducts=@('Textures/Trees/Impostors/WhitePine','Materials/M_Trees/WhitePine/WhitePine_OctaImpostor_M.mat','Models/Trees/WhitePine_OctaImpostor_Runtime.mesh','Prefabs/WhitePine_OctaImpostor_Runtime.prefab')
function Copy-PineProduct($source,$destination){
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    if((Get-Item -LiteralPath $source).PSIsContainer){Copy-Item -LiteralPath $source -Destination (Split-Path -Parent $destination) -Recurse -Force}
    else{Copy-Item -LiteralPath $source -Destination $destination -Force}
    Copy-Item -LiteralPath ($source+'.meta') -Destination ($destination+'.meta') -Force
}
if(-not $InstallOnly){
    & (Join-Path $PSScriptRoot 'ValidateOakMaterials.ps1') -PrepareOnly | Out-Null
    $pineEditors=@('OakOctaImpostorBaker','OakImpostorValidation','OakImpostorColorValidation','WhitePineOctaImpostorBaker','WhitePineImpostorValidation','TreeImpostorAtlasCaptureWindow','TreeImpostorAmbientAtlasBakerWindow','MapleImpostorMaterialSettings','MapleImpostorColorValidation','SpruceImpostorGpuValidation','DistantTreeGpuValidation','TreeImpostorGroundingValidation')
    foreach($name in $pineEditors){
        $source=Join-Path $pineRoot "Assets/Editor/$name.cs"
        if(-not(Test-Path -LiteralPath ($source+'.meta'))){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath ($source+'.meta')}
        Copy-Item -LiteralPath $source,($source+'.meta') -Destination (Join-Path $pineProject 'Assets/Editor') -Force
    }
    foreach($path in @('Assets/Shaders/WhitePineImpostorSemanticCapture.shader')){
        if(-not(Test-Path -LiteralPath "$pineRoot/$path.meta")){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath "$pineRoot/$path.meta"}
        Copy-Item -LiteralPath "$pineRoot/$path.meta" -Destination "$pineProject/$path.meta" -Force
    }
    foreach($path in @('Prefabs/WhitePine_LOD0_v01.prefab','Models/Trees/Tree_Blender_007_WhitePine.fbx')){
        Copy-Item -LiteralPath "$pineRoot/Assets/$path","$pineRoot/Assets/$path.meta" -Destination (Split-Path -Parent "$pineProject/Assets/$path") -Force
    }
    foreach($folder in @('Settings','Materials/M_Trees/WhitePine','Materials/M_Trees/SugarMaple','Materials/M_Trees/RedMaple')){
        Copy-Item -LiteralPath "$pineRoot/Assets/$folder" -Destination (Split-Path -Parent "$pineProject/Assets/$folder") -Recurse -Force
    }
    foreach($path in @('Textures/Trees/Bark/T_WhitePineBark_Stylized.png','Textures/Trees/leafClumpAtlas_WhitePine.png')){
        Copy-Item -LiteralPath "$pineRoot/Assets/$path","$pineRoot/Assets/$path.meta" -Destination (Split-Path -Parent "$pineProject/Assets/$path") -Force
    }
    # Preserve generated Unity identities on subsequent bakes.
    foreach($path in $pineProducts){
        if(Test-Path -LiteralPath "$pineRoot/Assets/$path"){
            Copy-PineProduct "$pineRoot/Assets/$path" "$pineProject/Assets/$path"
        }
    }
    if($PrepareOnly){Write-Output $pineProject;return}
    $pineVersion=((Get-Content "$pineRoot/ProjectSettings/ProjectVersion.txt" -First 1)-split ': ')[1]
    $pineLog="$pineRoot/.utmp/oak-materials/white-pine-$(if($ValidateOnly){'validate'}else{'bake'})-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss')).log"
    $pineMethod=if($ValidateOnly){'WhitePineImpostorValidation.ValidateBatch'}else{'WhitePineOctaImpostorBaker.BakeBatch'}
    $pineArgs=@('-batchmode','-projectPath',$pineProject,'-executeMethod',$pineMethod,'-logFile',$pineLog)
    $pineProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$pineVersion/Editor/Unity.exe" -ArgumentList ($pineArgs|ForEach-Object{'"'+$_+'"'}) -WindowStyle Hidden -PassThru
    Write-Output "WhitePine bake process $($pineProcess.Id); log $pineLog"
    $pineTimer=[Diagnostics.Stopwatch]::StartNew()
    do{
        Start-Sleep -Seconds 3
        $pineTail=if(Test-Path -LiteralPath $pineLog){Get-Content -LiteralPath $pineLog -Tail 100}else{@()}
        if($pineTail -match 'WHITE PINE BAKE AND VALIDATION PASSED'){break}
        if($pineTail -match 'Aborting batchmode due to failure|InvalidOperationException:|error CS[0-9]|Application will terminate with return code 1'){throw "WhitePine bake/validation failed; inspect $pineLog"}
        if($pineTimer.Elapsed.TotalMinutes -gt 45){throw "WhitePine bake timed out; inspect $pineLog"}
    }while($true)
    if($ValidateOnly){Write-Output 'WhitePine validation passed.';return}
}
$pineRecordPath="$pineProject/Assets/Textures/Trees/Impostors/WhitePine/WhitePine_Octa_Validation.json"
if(-not(Test-Path -LiteralPath $pineRecordPath)){throw 'No successful pine validation record. Run the bake/validation before installation.'}
$pineRecord=Get-Content -LiteralPath $pineRecordPath -Raw | ConvertFrom-Json
foreach($signature in $pineRecord.inputs){
    $sourcePath=Join-Path $pineRoot $signature.path
    if(-not(Test-Path -LiteralPath $sourcePath) -or (Get-FileHash -LiteralPath $sourcePath).Hash -ne $signature.sha256){throw "WhitePine input changed since validation: $($signature.path)"}
}
foreach($signature in $pineRecord.outputs){
    $outputPath=Join-Path $pineProject $signature.path
    if((Get-FileHash -LiteralPath $outputPath).Hash -ne $signature.sha256){throw "WhitePine output changed since validation: $($signature.path)"}
}
foreach($path in $pineProducts){
    if(-not(Test-Path -LiteralPath "$pineProject/Assets/$path")){throw "Missing validated bake output: $path"}
    Copy-PineProduct "$pineProject/Assets/$path" "$pineRoot/Assets/$path"
}
$pineScenePath=Join-Path $pineRoot 'Assets/Scenes/SmearScene.unity'
$pineScene=[IO.File]::ReadAllText($pineScenePath)
$pineSourceGuid=((Get-Content "$pineRoot/Assets/Prefabs/WhitePine_LOD0_v01.prefab.meta"|Select-String '^guid:').ToString()-split ': ')[1]
foreach($slot in @('whitePineTreePrefab','grasslandWhitePineTreePrefab')){
    if($pineScene -notmatch ($slot+': \{fileID: [^,]+, guid: '+$pineSourceGuid+',')){throw "World Manager $slot no longer points to WhitePine_LOD0_v01; review before assigning this bake."}
}
$pineProxyGuid=((Get-Content "$pineRoot/Assets/Prefabs/WhitePine_OctaImpostor_Runtime.prefab.meta"|Select-String '^guid:').ToString()-split ': ')[1]
$pinePrefab=Get-Content "$pineRoot/Assets/Prefabs/WhitePine_OctaImpostor_Runtime.prefab" -Raw
$pineRootFileId=[regex]::Match($pinePrefab,'--- !u!1 &(-?\d+)').Groups[1].Value
foreach($slot in @('whitePineTreeBillboardPrefab','grasslandWhitePineTreeBillboardPrefab')){
    $pattern='(?m)(    '+$slot+': )\{[^\r\n]+\}'
    if([regex]::Matches($pineScene,$pattern).Count -ne 1){throw "Expected exactly one $slot in SmearScene."}
    $pineScene=[regex]::Replace($pineScene,$pattern,('${1}{fileID: '+$pineRootFileId+', guid: '+$pineProxyGuid+', type: 3}'))
}
[IO.File]::WriteAllText($pineScenePath,$pineScene,[Text.UTF8Encoding]::new($false))
Write-Output 'Installed validated pine atlases, material, mesh and prefab. Assigned both pine billboard slots in SmearScene.'
