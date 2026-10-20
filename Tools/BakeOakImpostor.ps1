param([switch]$PrepareOnly, [switch]$ValidateOnly, [switch]$InstallOnly)
$ErrorActionPreference='Stop'
$oakRoot=Split-Path -Parent $PSScriptRoot
$oakProject=Join-Path $oakRoot '.utmp/oak-materials/unity-project'
$oakProducts=@('Textures/Trees/Impostors/Oak','Materials/M_Trees/Oak/Oak_OctaImpostor_M.mat','Models/Trees/Oak_OctaImpostor_Runtime.mesh','Prefabs/Oak_OctaImpostor_Runtime.prefab')
function Copy-OakProduct($source,$destination){
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    if((Get-Item -LiteralPath $source).PSIsContainer){Copy-Item -LiteralPath $source -Destination (Split-Path -Parent $destination) -Recurse -Force}
    else{Copy-Item -LiteralPath $source -Destination $destination -Force}
    Copy-Item -LiteralPath ($source+'.meta') -Destination ($destination+'.meta') -Force
}
if(-not $InstallOnly){
    & (Join-Path $PSScriptRoot 'ValidateOakMaterials.ps1') -PrepareOnly | Out-Null
    $oakEditors=@('OakOctaImpostorBaker','OakImpostorValidation','OakImpostorColorValidation','TreeImpostorAtlasCaptureWindow','TreeImpostorAmbientAtlasBakerWindow','MapleImpostorMaterialSettings','MapleImpostorColorValidation','SpruceImpostorGpuValidation','DistantTreeGpuValidation','TreeImpostorGroundingValidation')
    foreach($name in $oakEditors){
        $source=Join-Path $oakRoot "Assets/Editor/$name.cs"
        if(-not(Test-Path -LiteralPath ($source+'.meta'))){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath ($source+'.meta')}
        Copy-Item -LiteralPath $source,($source+'.meta') -Destination (Join-Path $oakProject 'Assets/Editor') -Force
    }
    foreach($path in @('Assets/Shaders/OakLeafSeasonPalette.hlsl','Assets/Shaders/OakImpostorSemanticCapture.shader')){
        if(-not(Test-Path -LiteralPath "$oakRoot/$path.meta")){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath "$oakRoot/$path.meta"}
        Copy-Item -LiteralPath "$oakRoot/$path.meta" -Destination "$oakProject/$path.meta" -Force
    }
    foreach($path in @('Prefabs/Oak_LOD0_v03.prefab','Models/Trees/Oak_LOD0_P2461_B1800_L530_v03.fbx')){
        Copy-Item -LiteralPath "$oakRoot/Assets/$path","$oakRoot/Assets/$path.meta" -Destination (Split-Path -Parent "$oakProject/Assets/$path") -Force
    }
    foreach($folder in @('Settings','Materials/M_Trees/SugarMaple','Materials/M_Trees/RedMaple')){
        Copy-Item -LiteralPath "$oakRoot/Assets/$folder" -Destination (Split-Path -Parent "$oakProject/Assets/$folder") -Recurse -Force
    }
    # Preserve generated Unity identities on subsequent bakes.
    foreach($path in $oakProducts){
        if(Test-Path -LiteralPath "$oakRoot/Assets/$path"){
            Copy-OakProduct "$oakRoot/Assets/$path" "$oakProject/Assets/$path"
        }
    }
    if($PrepareOnly){Write-Output $oakProject;return}
    $oakVersion=((Get-Content "$oakRoot/ProjectSettings/ProjectVersion.txt" -First 1)-split ': ')[1]
    $oakLog="$oakRoot/.utmp/oak-materials/$(if($ValidateOnly){'validate'}else{'bake'})-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss')).log"
    $oakMethod=if($ValidateOnly){'OakImpostorValidation.ValidateBatch'}else{'OakOctaImpostorBaker.BakeBatch'}
    $oakArgs=@('-batchmode','-projectPath',$oakProject,'-executeMethod',$oakMethod,'-logFile',$oakLog)
    $oakProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$oakVersion/Editor/Unity.exe" -ArgumentList ($oakArgs|ForEach-Object{'"'+$_+'"'}) -WindowStyle Hidden -PassThru
    Write-Output "Oak bake process $($oakProcess.Id); log $oakLog"
    $oakTimer=[Diagnostics.Stopwatch]::StartNew()
    do{
        Start-Sleep -Seconds 3
        $oakTail=if(Test-Path -LiteralPath $oakLog){Get-Content -LiteralPath $oakLog -Tail 100}else{@()}
        if($oakTail -match 'OAK BAKE AND VALIDATION PASSED'){break}
        if($oakTail -match 'Aborting batchmode due to failure|InvalidOperationException:|error CS[0-9]|Application will terminate with return code 1'){throw "Oak bake/validation failed; inspect $oakLog"}
        if($oakTimer.Elapsed.TotalMinutes -gt 45){throw "Oak bake timed out; inspect $oakLog"}
    }while($true)
    if($ValidateOnly){Write-Output 'Oak validation passed.';return}
}
$oakRecordPath="$oakProject/Assets/Textures/Trees/Impostors/Oak/Oak_Octa_Validation.json"
if(-not(Test-Path -LiteralPath $oakRecordPath)){throw 'No successful oak validation record. Run the bake/validation before installation.'}
$oakRecord=Get-Content -LiteralPath $oakRecordPath -Raw | ConvertFrom-Json
foreach($signature in $oakRecord.inputs){
    $sourcePath=Join-Path $oakRoot $signature.path
    if(-not(Test-Path -LiteralPath $sourcePath) -or (Get-FileHash -LiteralPath $sourcePath).Hash -ne $signature.sha256){throw "Oak input changed since validation: $($signature.path)"}
}
foreach($signature in $oakRecord.outputs){
    $outputPath=Join-Path $oakProject $signature.path
    if((Get-FileHash -LiteralPath $outputPath).Hash -ne $signature.sha256){throw "Oak output changed since validation: $($signature.path)"}
}
foreach($path in $oakProducts){
    if(-not(Test-Path -LiteralPath "$oakProject/Assets/$path")){throw "Missing validated bake output: $path"}
    Copy-OakProduct "$oakProject/Assets/$path" "$oakRoot/Assets/$path"
}
$oakScenePath=Join-Path $oakRoot 'Assets/Scenes/SmearScene.unity'
$oakScene=[IO.File]::ReadAllText($oakScenePath)
$oakSourceGuid=((Get-Content "$oakRoot/Assets/Prefabs/Oak_LOD0_v03.prefab.meta"|Select-String '^guid:').ToString()-split ': ')[1]
foreach($slot in @('oakTreePrefab','grasslandOakTreePrefab')){
    if($oakScene -notmatch ($slot+': \{fileID: [^,]+, guid: '+$oakSourceGuid+',')){throw "World Manager $slot no longer points to Oak_LOD0_v03; review before assigning this bake."}
}
$oakProxyGuid=((Get-Content "$oakRoot/Assets/Prefabs/Oak_OctaImpostor_Runtime.prefab.meta"|Select-String '^guid:').ToString()-split ': ')[1]
$oakPrefab=Get-Content "$oakRoot/Assets/Prefabs/Oak_OctaImpostor_Runtime.prefab" -Raw
$oakRootFileId=[regex]::Match($oakPrefab,'--- !u!1 &(-?\d+)').Groups[1].Value
foreach($slot in @('oakTreeBillboardPrefab','grasslandOakTreeBillboardPrefab')){
    $pattern='(?m)(    '+$slot+': )\{[^\r\n]+\}'
    if([regex]::Matches($oakScene,$pattern).Count -ne 1){throw "Expected exactly one $slot in SmearScene."}
    $oakScene=[regex]::Replace($oakScene,$pattern,('${1}{fileID: '+$oakRootFileId+', guid: '+$oakProxyGuid+', type: 3}'))
}
[IO.File]::WriteAllText($oakScenePath,$oakScene,[Text.UTF8Encoding]::new($false))
Write-Output 'Installed validated oak atlases, material, mesh and prefab. Assigned both oak billboard slots in SmearScene.'
