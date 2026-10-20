param([switch]$PrepareOnly, [switch]$ValidateOnly, [switch]$InstallOnly)
$ErrorActionPreference='Stop'
$birchRoot=Split-Path -Parent $PSScriptRoot
$birchProject=Join-Path $birchRoot '.utmp/oak-materials/unity-project'
$birchProducts=@('Textures/Trees/Impostors/Birch','Materials/M_Trees/Birch/Birch_OctaImpostor_M.mat','Models/Trees/Birch_OctaImpostor_Runtime.mesh','Prefabs/Birch_OctaImpostor_Runtime.prefab')
function Copy-BirchProduct($source,$destination){
    New-Item -ItemType Directory -Path (Split-Path -Parent $destination) -Force | Out-Null
    if((Get-Item -LiteralPath $source).PSIsContainer){Copy-Item -LiteralPath $source -Destination (Split-Path -Parent $destination) -Recurse -Force}
    else{Copy-Item -LiteralPath $source -Destination $destination -Force}
    Copy-Item -LiteralPath ($source+'.meta') -Destination ($destination+'.meta') -Force
}
if(-not $InstallOnly){
    & (Join-Path $PSScriptRoot 'ValidateOakMaterials.ps1') -PrepareOnly | Out-Null
    $birchEditors=@('OakOctaImpostorBaker','OakImpostorValidation','OakImpostorColorValidation','BirchOctaImpostorBaker','BirchImpostorValidation','BirchImpostorColorValidation','TreeImpostorAtlasCaptureWindow','TreeImpostorAmbientAtlasBakerWindow','MapleImpostorMaterialSettings','MapleImpostorColorValidation','SpruceImpostorGpuValidation','DistantTreeGpuValidation','TreeImpostorGroundingValidation')
    foreach($name in $birchEditors){
        $source=Join-Path $birchRoot "Assets/Editor/$name.cs"
        if(-not(Test-Path -LiteralPath ($source+'.meta'))){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath ($source+'.meta')}
        Copy-Item -LiteralPath $source,($source+'.meta') -Destination (Join-Path $birchProject 'Assets/Editor') -Force
    }
    foreach($path in @('Assets/Shaders/BirchImpostorSemanticCapture.shader')){
        if(-not(Test-Path -LiteralPath "$birchRoot/$path.meta")){"fileFormatVersion: 2`nguid: $([guid]::NewGuid().ToString('N'))" | Set-Content -LiteralPath "$birchRoot/$path.meta"}
        Copy-Item -LiteralPath "$birchRoot/$path.meta" -Destination "$birchProject/$path.meta" -Force
    }
    foreach($path in @('Prefabs/Birch_LOD0_v04.prefab','Models/Trees/Birch_LOD0_P2640_B1800_L800_v04.fbx')){
        Copy-Item -LiteralPath "$birchRoot/Assets/$path","$birchRoot/Assets/$path.meta" -Destination (Split-Path -Parent "$birchProject/Assets/$path") -Force
    }
    foreach($folder in @('Settings','Materials/M_Trees/Birch','Materials/M_Trees/SugarMaple','Materials/M_Trees/RedMaple')){
        Copy-Item -LiteralPath "$birchRoot/Assets/$folder" -Destination (Split-Path -Parent "$birchProject/Assets/$folder") -Recurse -Force
    }
    foreach($path in @('Textures/Trees/Bark/BirchBarkTextureSeamless.png','Textures/Trees/leafClumpAtlas_Birch.png')){
        Copy-Item -LiteralPath "$birchRoot/Assets/$path","$birchRoot/Assets/$path.meta" -Destination (Split-Path -Parent "$birchProject/Assets/$path") -Force
    }
    # Preserve generated Unity identities on subsequent bakes.
    foreach($path in $birchProducts){
        if(Test-Path -LiteralPath "$birchRoot/Assets/$path"){
            Copy-BirchProduct "$birchRoot/Assets/$path" "$birchProject/Assets/$path"
        }
    }
    if($PrepareOnly){Write-Output $birchProject;return}
    $birchVersion=((Get-Content "$birchRoot/ProjectSettings/ProjectVersion.txt" -First 1)-split ': ')[1]
    $birchLog="$birchRoot/.utmp/oak-materials/birch-$(if($ValidateOnly){'validate'}else{'bake'})-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss')).log"
    $birchMethod=if($ValidateOnly){'BirchImpostorValidation.ValidateBatch'}else{'BirchOctaImpostorBaker.BakeBatch'}
    $birchArgs=@('-batchmode','-projectPath',$birchProject,'-executeMethod',$birchMethod,'-logFile',$birchLog)
    $birchProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$birchVersion/Editor/Unity.exe" -ArgumentList ($birchArgs|ForEach-Object{'"'+$_+'"'}) -WindowStyle Hidden -PassThru
    Write-Output "Birch bake process $($birchProcess.Id); log $birchLog"
    $birchTimer=[Diagnostics.Stopwatch]::StartNew()
    do{
        Start-Sleep -Seconds 3
        $birchTail=if(Test-Path -LiteralPath $birchLog){Get-Content -LiteralPath $birchLog -Tail 100}else{@()}
        if($birchTail -match 'BIRCH BAKE AND VALIDATION PASSED'){break}
        if($birchTail -match 'Aborting batchmode due to failure|InvalidOperationException:|error CS[0-9]|Application will terminate with return code 1'){throw "Birch bake/validation failed; inspect $birchLog"}
        if($birchTimer.Elapsed.TotalMinutes -gt 45){throw "Birch bake timed out; inspect $birchLog"}
    }while($true)
    if($ValidateOnly){Write-Output 'Birch validation passed.';return}
}
$birchRecordPath="$birchProject/Assets/Textures/Trees/Impostors/Birch/Birch_Octa_Validation.json"
if(-not(Test-Path -LiteralPath $birchRecordPath)){throw 'No successful birch validation record. Run the bake/validation before installation.'}
$birchRecord=Get-Content -LiteralPath $birchRecordPath -Raw | ConvertFrom-Json
foreach($signature in $birchRecord.inputs){
    $sourcePath=Join-Path $birchRoot $signature.path
    if(-not(Test-Path -LiteralPath $sourcePath) -or (Get-FileHash -LiteralPath $sourcePath).Hash -ne $signature.sha256){throw "Birch input changed since validation: $($signature.path)"}
}
foreach($signature in $birchRecord.outputs){
    $outputPath=Join-Path $birchProject $signature.path
    if((Get-FileHash -LiteralPath $outputPath).Hash -ne $signature.sha256){throw "Birch output changed since validation: $($signature.path)"}
}
foreach($path in $birchProducts){
    if(-not(Test-Path -LiteralPath "$birchProject/Assets/$path")){throw "Missing validated bake output: $path"}
    Copy-BirchProduct "$birchProject/Assets/$path" "$birchRoot/Assets/$path"
}
$birchScenePath=Join-Path $birchRoot 'Assets/Scenes/SmearScene.unity'
$birchScene=[IO.File]::ReadAllText($birchScenePath)
$birchSourceGuid=((Get-Content "$birchRoot/Assets/Prefabs/Birch_LOD0_v04.prefab.meta"|Select-String '^guid:').ToString()-split ': ')[1]
foreach($slot in @('birchAspenTreePrefab','grasslandBirchAspenTreePrefab')){
    if($birchScene -notmatch ($slot+': \{fileID: [^,]+, guid: '+$birchSourceGuid+',')){throw "World Manager $slot no longer points to Birch_LOD0_v04; review before assigning this bake."}
}
$birchProxyGuid=((Get-Content "$birchRoot/Assets/Prefabs/Birch_OctaImpostor_Runtime.prefab.meta"|Select-String '^guid:').ToString()-split ': ')[1]
$birchPrefab=Get-Content "$birchRoot/Assets/Prefabs/Birch_OctaImpostor_Runtime.prefab" -Raw
$birchRootFileId=[regex]::Match($birchPrefab,'--- !u!1 &(-?\d+)').Groups[1].Value
foreach($slot in @('birchAspenTreeBillboardPrefab','grasslandBirchAspenTreeBillboardPrefab')){
    $pattern='(?m)(    '+$slot+': )\{[^\r\n]+\}'
    if([regex]::Matches($birchScene,$pattern).Count -ne 1){throw "Expected exactly one $slot in SmearScene."}
    $birchScene=[regex]::Replace($birchScene,$pattern,('${1}{fileID: '+$birchRootFileId+', guid: '+$birchProxyGuid+', type: 3}'))
}
[IO.File]::WriteAllText($birchScenePath,$birchScene,[Text.UTF8Encoding]::new($false))
Write-Output 'Installed validated birch atlases, material, mesh and prefab. Assigned both birch billboard slots in SmearScene.'
