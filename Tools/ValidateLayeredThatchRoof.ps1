param([switch]$Install)
$ErrorActionPreference='Stop'
$roofRoot=Split-Path -Parent $PSScriptRoot
$roofProject=Join-Path $roofRoot '.utmp/building-prototype/unity-project'
& (Join-Path $PSScriptRoot 'ValidateBuildingPrototype.ps1') -PrepareOnly -RoofChecks
$roofBaked=Test-Path -LiteralPath (Join-Path $roofRoot 'Assets/Textures/Buildings/Wood/thatch-roof-detail.png')
if($roofBaked) {
    Copy-Item -LiteralPath (Join-Path $roofRoot 'ArtReferences/ThatchRoof/Redesign/Baked/DetailedReference.fbx') -Destination (Join-Path $roofProject 'Assets/Models/Buildings/Wood/Roofs/DetailedReference.fbx') -Force
}
$roofVersion=((Get-Content (Join-Path $roofRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$roofEditor="C:/Program Files/Unity/Hub/Editor/$roofVersion/Editor/Unity.exe"
foreach($roofMethod in 'ThatchRoofValidation.RunLayeredBatch','ModularWoodValidation.RunInstalledBatch') {
    $roofLog=Join-Path $roofRoot ('.utmp/roof/'+$roofMethod+'.log')
    $roofArgs=@('-batchmode','-projectPath',('"'+$roofProject+'"'),'-executeMethod',$roofMethod,'-logFile',('"'+$roofLog+'"'))
    $roofProcess=Start-Process -FilePath $roofEditor -ArgumentList $roofArgs -WindowStyle Hidden -PassThru
    while(-not $roofProcess.WaitForExit(1000)){}
    if($roofProcess.ExitCode -ne 0){throw "Roof validation failed: $roofLog"}
}
$roofArt=Join-Path $roofRoot 'ArtReferences/ThatchRoof/Redesign'
if($roofBaked){$roofArt=Join-Path $roofArt 'Baked'}
$roofEvidence=Join-Path $roofProject '.utmp/building-prototype'
Copy-Item -LiteralPath (Join-Path $roofEvidence 'layered-roof-validation.json') -Destination $roofArt -Force
foreach($roofCapture in 'roof-4x8-instanced','roof-distant','roof-join-close','roof-ridge','roof-underside','roof-uphill-chain','roof-uphill-join') {
    Copy-Item -LiteralPath (Join-Path $roofEvidence ($roofCapture+'.png')) -Destination $roofArt -Force
}
if($roofBaked) {
    foreach($roofCapture in 'roof-baked-comparison.png','roof-detailed-comparison.png','roof-flat-control.png','roof-bake-comparison.json') {
        Copy-Item -LiteralPath (Join-Path $roofEvidence $roofCapture) -Destination $roofArt -Force
    }
}
if($Install) {
    # Existing definitions/prefabs already reference these GUIDs. Collision and
    # reference metadata are unchanged; install only the two visuals and artwork.
    foreach($roofRelative in 'Assets/Resources/Building/thatch-roof-panel-mesh.asset','Assets/Resources/Building/thatch-roof-panel-joined-mesh.asset','Assets/Textures/Buildings/Wood/thatch-roof-atlas.png','Assets/Resources/Building/UI/Thumbnails/build_wood_thatch-roof-panel.png') {
        Copy-Item -LiteralPath (Join-Path $roofProject $roofRelative) -Destination (Join-Path $roofRoot $roofRelative) -Force
    }
    if($roofBaked) {
        foreach($roofRelative in 'Assets/Textures/Buildings/Wood/thatch-roof-detail.png.meta','Assets/Materials/Buildings/Wood/ThatchRoof.mat') {
            Copy-Item -LiteralPath (Join-Path $roofProject $roofRelative) -Destination (Join-Path $roofRoot $roofRelative) -Force
        }
    }
}
Write-Output 'Detailed roof geometry, joins, shadows and current modular-building regressions passed.'
