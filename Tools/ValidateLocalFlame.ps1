param([switch]$PrepareOnly,[switch]$BuildBenchmark,[switch]$Benchmark,[switch]$InstallTorch,[switch]$PlayChecks,[switch]$SurfaceChecks,[switch]$Comparison,[switch]$ComparisonBuildOnly)
$ErrorActionPreference='Stop'
$flameRoot=Split-Path -Parent $PSScriptRoot
$flameProject=Join-Path $flameRoot '.utmp/local-flame/unity-project'
$flameWork=Join-Path $flameRoot '.utmp/local-flame'
$flameCatalog=Join-Path $flameRoot 'Assets/Resources/Building/PrototypeCatalog.asset'
$flameCatalogHash=(Get-FileHash -LiteralPath $flameCatalog).Hash
& (Join-Path $PSScriptRoot 'ValidateBuildingPrototype.ps1') -PrepareOnly -UiAssets -Workspace local-flame
foreach($flameRelative in @('Assets/Shaders','Assets/Resources/Lighting')) {
    Copy-Item -LiteralPath (Join-Path $flameRoot $flameRelative) -Destination (Join-Path $flameProject (Split-Path -Parent $flameRelative)) -Recurse -Force
}
foreach($flameFile in @('Assets/Editor/LocalFlameValidation.cs','Assets/Editor/LocalFlameValidation.cs.meta','Assets/Editor/LocalFlameSurfaceValidation.cs','Assets/Editor/LocalFlameSurfaceValidation.cs.meta','Assets/Editor/TorchKitSetup.cs','Assets/Editor/TorchKitSetup.cs.meta')) {
    if(Test-Path -LiteralPath (Join-Path $flameRoot $flameFile)) { Copy-Item -LiteralPath (Join-Path $flameRoot $flameFile) -Destination (Join-Path $flameProject $flameFile) -Force }
}
if($PrepareOnly){return}
$flameVersion=((Get-Content (Join-Path $flameRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$flameUnity="C:/Program Files/Unity/Hub/Editor/$flameVersion/Editor/Unity.exe"
$flameMethod=if($InstallTorch){'TorchKitSetup.InstallBatch'}elseif($PlayChecks){'LocalFlameValidation.RunPlayBatch'}elseif($SurfaceChecks){'LocalFlameSurfaceValidation.RunBatch'}elseif($Comparison){'LocalFlameValidation.BuildComparison'}elseif($BuildBenchmark){'LocalFlameValidation.BuildBenchmark'}else{'LocalFlameValidation.RunBatch'}
$flameLog=Join-Path $flameWork $(if($InstallTorch){'torch-install.log'}elseif($PlayChecks){'play.log'}elseif($SurfaceChecks){'surface.log'}elseif($Comparison){'comparison-build.log'}elseif($BuildBenchmark){'benchmark-build.log'}else{'validation.log'})
$flameArgs=@('-batchmode','-force-d3d11','-projectPath',('"'+$flameProject+'"'),'-executeMethod',$flameMethod,'-logFile',('"'+$flameLog+'"'))
$flameProcess=Start-Process -FilePath $flameUnity -ArgumentList $flameArgs -WindowStyle Hidden -PassThru
Write-Output "Local flame Unity PID $($flameProcess.Id); log $flameLog"
while(-not $flameProcess.WaitForExit(1000)){}
if($flameProcess.ExitCode -ne 0){throw "Local flame validation failed: $flameLog"}
if($InstallTorch) {
    if((Get-FileHash -LiteralPath $flameCatalog).Hash -ne $flameCatalogHash){throw 'Catalog changed during validation; preserving the main copy.'}
    foreach($flameAsset in Get-ChildItem -LiteralPath (Join-Path $flameProject 'Assets/Resources/Lighting') -File | Where-Object {$_.Extension -in '.mat','.asset','.prefab'}) {
        foreach($flameSuffix in '','.meta') {Copy-Item -LiteralPath ($flameAsset.FullName+$flameSuffix) -Destination (Join-Path $flameRoot ('Assets/Resources/Lighting/'+$flameAsset.Name+$flameSuffix)) -Force}
    }
    foreach($flameRelative in @('Assets/Resources/Building/wall-torch.asset','Assets/Resources/Building/wall-torch.prefab','Assets/Resources/Building/UI/Thumbnails/build_lighting_wall-torch.png')) {
        foreach($flameSuffix in '','.meta'){Copy-Item -LiteralPath (Join-Path $flameProject ($flameRelative+$flameSuffix)) -Destination (Join-Path $flameRoot ($flameRelative+$flameSuffix)) -Force}
    }
    foreach($flameRelative in @('Assets/Resources/Lighting/TorchFlameAtlas.png.meta','Assets/Textures/Buildings/Lighting/TorchAtlas.png.meta',
        'Assets/Models/Buildings/Lighting/MountedTorch.fbx.meta','Assets/Models/Buildings/Lighting/HandTorch.fbx.meta',
        'Assets/Resources/Lighting.meta','Assets/Models/Buildings/Lighting.meta','Assets/Textures/Buildings/Lighting.meta','Assets/Scripts/Lighting.meta')) {
        if(Test-Path -LiteralPath (Join-Path $flameProject $flameRelative)){Copy-Item -LiteralPath (Join-Path $flameProject $flameRelative) -Destination (Join-Path $flameRoot $flameRelative) -Force}
    }
    Copy-Item -LiteralPath (Join-Path $flameProject 'Assets/Resources/Building/PrototypeCatalog.asset') -Destination $flameCatalog -Force
    Copy-Item -LiteralPath (Join-Path $flameProject '.utmp/local-flame/torch-validation.json') -Destination (Join-Path $flameRoot 'ArtReferences/Torch/unity-validation.json') -Force
    Copy-Item -LiteralPath (Join-Path $flameProject '.utmp/local-flame/mounted-torch.png') -Destination (Join-Path $flameRoot 'ArtReferences/Torch/mounted-torch.png') -Force
}
if($Benchmark -or ($Comparison -and -not $ComparisonBuildOnly)) {
    $flameExe=Join-Path $flameProject '.utmp/local-flame/player/FlameBenchmark.exe'
    $flameReport=Join-Path $flameWork $(if($Comparison){'comparison.json'}else{'benchmark.json'})
    $flamePlayerLog=Join-Path $flameWork $(if($Comparison){'comparison-player.log'}else{'benchmark-player.log'})
    $flamePlayerArgs=@('-batchmode','-force-d3d11','-screen-width','1920','-screen-height','1080','-screen-fullscreen','0','-flameReport',('"'+$flameReport+'"'),'-logFile',('"'+$flamePlayerLog+'"'))
    $flamePlayer=Start-Process -FilePath $flameExe -ArgumentList $flamePlayerArgs -WindowStyle Hidden -PassThru
    Write-Output "Flame benchmark player PID $($flamePlayer.Id); report $flameReport"
    while(-not $flamePlayer.WaitForExit(1000)){}
    if($flamePlayer.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $flameReport)){throw 'Benchmark player failed to produce its report.'}
    if($Comparison) {
        if(-not (Select-String -LiteralPath $flamePlayerLog -Pattern 'LOCAL FLAME COMPARISON COMPLETE' -Quiet)){throw 'Comparison did not complete all cases.'}
        $flameComparison=Get-Content -LiteralPath $flameReport -Raw | ConvertFrom-Json
        if(@($flameComparison.measurements | Where-Object {$_.mode -ne 'coverage-probe'}).Count -ne 42){throw 'Comparison case count was incomplete.'}
        $flameComparison | Add-Member -NotePropertyName nativeShadowWarnings -NotePropertyValue @((Select-String -LiteralPath $flamePlayerLog -Pattern 'URP removed|Reduced additional punctual light shadows').Line) -Force
        $flameComparison | Add-Member -NotePropertyName activeCounterMeaning -NotePropertyValue 'active counts custom-manager slots. Native enabled light count is sources; consult nativeShadowWarnings before comparing shadow coverage.' -Force
        $flameComparison | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $flameReport
    }
}
Write-Output "Local flame run passed: $flameLog"
