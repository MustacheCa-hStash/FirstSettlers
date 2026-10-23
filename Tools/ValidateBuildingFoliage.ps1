param([switch]$PrepareOnly,[switch]$IntersectionChecks)
$ErrorActionPreference='Stop'
$foliageRoot=Split-Path -Parent $PSScriptRoot
Push-Location $foliageRoot
try {
    & "$PSScriptRoot/ValidateBuildingPrototype.ps1" -PrepareOnly -IntersectionChecks:$IntersectionChecks -Workspace building-foliage
    $foliageProject=Join-Path $foliageRoot '.utmp/building-foliage/unity-project'
    foreach($foliageFile in 'Assets/Editor/BuildingFoliageValidation.cs','Assets/Editor/ResidentGrassCullingValidation.cs','Assets/Shaders/GrassCompact.compute','Assets/Shaders/GrassIndirectInstance.hlsl','Assets/Shaders/S_Grass/GrassInstancedTerrainTint.shader','Assets/Resources/Foliage/ForestScatterCompact.compute','Assets/Shaders/ForestScatterInstance.hlsl') {
        $foliageDestination=Join-Path $foliageProject $foliageFile
        New-Item -ItemType Directory -Path (Split-Path -Parent $foliageDestination) -Force | Out-Null
        Copy-Item -LiteralPath $foliageFile -Destination $foliageDestination -Force
        if(Test-Path -LiteralPath "$foliageFile.meta") { Copy-Item -LiteralPath "$foliageFile.meta" -Destination "$foliageDestination.meta" -Force }
    }
    if($PrepareOnly) { return }
    $foliageVersion=((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $foliageEditor="C:/Program Files/Unity/Hub/Editor/$foliageVersion/Editor/Unity.exe"
    $foliageLog=Join-Path $foliageRoot $(if($IntersectionChecks){'.utmp/building-foliage/intersection-regression.log'}else{'.utmp/building-foliage/validation.log'})
    $foliageMethod=if($IntersectionChecks){'BuildIntersectionRegressionValidation.RunBatch'}else{'BuildingFoliageValidation.RunBatch'}
    $foliageProcess=Start-Process -FilePath $foliageEditor -ArgumentList @('-batchmode','-force-d3d11','-projectPath',('"'+$foliageProject+'"'),'-executeMethod',$foliageMethod,'-logFile',('"'+$foliageLog+'"')) -WindowStyle Hidden -PassThru
    Write-Output "Building foliage validation PID: $($foliageProcess.Id); log: $foliageLog"
    $foliageProcess.WaitForExit()
    if($foliageProcess.ExitCode -ne 0) { Get-Content -LiteralPath $foliageLog -Tail 65;throw 'Building foliage validation failed.' }
    if($IntersectionChecks) { Select-String -LiteralPath $foliageLog -Pattern 'BUILD INTERSECTION PLAY MODE PASS' }
    else { Get-Content -LiteralPath (Join-Path $foliageProject '.utmp/building-foliage-result.txt') }
} finally { Pop-Location }
