param([switch]$PrepareOnly, [switch]$TerrainIssues, [switch]$Baseline, [switch]$Probe, [switch]$Glacial, [switch]$Edges, [switch]$Excavation, [switch]$ExcavationWorld)
if ($ExcavationWorld) { $Excavation = $true }
$ErrorActionPreference = 'Stop'
$landformRoot = Split-Path -Parent $PSScriptRoot
$landformWork = Join-Path $landformRoot '.utmp/world-landforms'
$landformProject = Join-Path $landformWork 'unity-project'
$landformVersion = ((Get-Content "$landformRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
New-Item -ItemType Directory -Path "$landformProject/Assets/Editor","$landformProject/Packages","$landformProject/ProjectSettings" -Force | Out-Null
Copy-Item -LiteralPath "$landformRoot/Assets/Scripts" -Destination "$landformProject/Assets" -Recurse -Force
Copy-Item -LiteralPath "$landformRoot/Assets/Shaders" -Destination "$landformProject/Assets" -Recurse -Force
Copy-Item -LiteralPath "$landformRoot/Assets/Settings" -Destination "$landformProject/Assets" -Recurse -Force
foreach ($landformFile in 'WorldLandformValidation.cs','TerrainRidgeValidation.cs','GlacialLandformValidation.cs','TerrainEdgeValidation.cs','GlacialSurfaceSetup.cs','DesertSurfaceSetup.cs','TerrainSlopeValidation.cs','WorldErosionValidation.cs','TerrainHeightSamplingValidation.cs','MountainSnowValidation.cs','TerrainMaterialInspector.cs','TerrainMaterialInspectorRules.cs','WorldManagerEditor.cs','WorldManagerInspectorRules.cs') {
    Copy-Item -LiteralPath "$landformRoot/Assets/Editor/$landformFile" -Destination "$landformProject/Assets/Editor" -Force
}
if ($Excavation) {
    Get-ChildItem -LiteralPath "$landformRoot/Assets/Editor" -Filter 'TerrainSculpting*Validation.cs' -File | Copy-Item -Destination "$landformProject/Assets/Editor" -Force
    Copy-Item -LiteralPath "$landformRoot/Assets/Editor/TerrainSteepExcavationValidation.cs" -Destination "$landformProject/Assets/Editor" -Force
    foreach ($landformFile in 'TerrainSkyAccessValidation.cs','TerrainMaterialInspectorValidation.cs','ResidentGrassCullingValidation.cs','CliffTextureValidation.cs') {
        Copy-Item -LiteralPath "$landformRoot/Assets/Editor/$landformFile" -Destination "$landformProject/Assets/Editor" -Force
    }
    Copy-Item -LiteralPath "$landformRoot/Assets/Editor/TerrainSkyAccessDebug.shader","$landformRoot/Assets/Editor/TerrainSkyAccessDebug.shader.meta" -Destination "$landformProject/Assets/Editor" -Force
    New-Item -ItemType Directory -Path "$landformProject/Assets/Resources" -Force | Out-Null
    Copy-Item -LiteralPath "$landformRoot/Assets/Resources/TerrainSculpting" -Destination "$landformProject/Assets/Resources" -Recurse -Force
    New-Item -ItemType Directory -Path "$landformProject/Assets/Resources/Foliage" -Force | Out-Null
    Copy-Item -LiteralPath "$landformRoot/Assets/Resources/Foliage/ForestScatterCompact.compute","$landformRoot/Assets/Resources/Foliage/ForestScatterCompact.compute.meta" -Destination "$landformProject/Assets/Resources/Foliage" -Force
}
New-Item -ItemType Directory -Path "$landformProject/Assets/Textures/Ground" -Force | Out-Null
Copy-Item -LiteralPath "$landformRoot/Assets/Textures/Ground/Desert" -Destination "$landformProject/Assets/Textures/Ground" -Recurse -Force
Copy-Item -LiteralPath "$landformRoot/Assets/Textures/Ground/Glacial" -Destination "$landformProject/Assets/Textures/Ground" -Recurse -Force
Copy-Item -LiteralPath "$landformRoot/Assets/Editor/WorldLandformDebug.shader","$landformRoot/Assets/Editor/WorldLandformDebug.shader.meta" -Destination "$landformProject/Assets/Editor" -Force
foreach ($landformSetting in 'ProjectVersion.txt','ProjectSettings.asset','TagManager.asset') {
    Copy-Item -LiteralPath "$landformRoot/ProjectSettings/$landformSetting" -Destination "$landformProject/ProjectSettings" -Force
}
# Copy the terrain material and just its texture dependencies, preserving GUIDs.
$landformMaterial = "$landformRoot/Assets/Materials/M_Terrain/M_TerrainBase.mat"
$landformGuids = @([regex]::Matches((Get-Content $landformMaterial -Raw), 'guid: ([a-f0-9]{32})') | ForEach-Object { $_.Groups[1].Value })
if ($Excavation) {
    $landformGuids += @([regex]::Matches((Get-Content "$landformRoot/Assets/Resources/TerrainSculpting/ExcavatedStone.asset" -Raw), 'guid: ([a-f0-9]{32})') | ForEach-Object { $_.Groups[1].Value })
}
foreach ($landformMeta in Get-ChildItem "$landformRoot/Assets/Textures" -Filter '*.meta' -Recurse -File) {
    if ((Get-Content $landformMeta.FullName -First 2) -match ('guid: (' + ($landformGuids -join '|') + ')')) {
        $landformRelative = $landformMeta.FullName.Substring($landformRoot.Length + 1)
        $landformDestination = Join-Path $landformProject $landformRelative
        New-Item -ItemType Directory -Path (Split-Path -Parent $landformDestination) -Force | Out-Null
        Copy-Item -LiteralPath $landformMeta.FullName -Destination $landformDestination -Force
        Copy-Item -LiteralPath $landformMeta.FullName.Substring(0,$landformMeta.FullName.Length-5) -Destination $landformDestination.Substring(0,$landformDestination.Length-5) -Force
    }
}
New-Item -ItemType Directory -Path "$landformProject/Assets/Materials/M_Terrain" -Force | Out-Null
Copy-Item -LiteralPath $landformMaterial,"$landformMaterial.meta" -Destination "$landformProject/Assets/Materials/M_Terrain" -Force
$landformManifest = Get-Content "$landformRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
$landformDependencies = [ordered]@{}
foreach ($landformDependency in $landformManifest.dependencies.PSObject.Properties) {
    if ($landformDependency.Name.StartsWith('com.unity.modules.')) { $landformDependencies[$landformDependency.Name] = $landformDependency.Value }
}
# Use installed package sources so this fixture needs no registry downloads.
foreach ($landformPackage in Get-ChildItem "$landformRoot/Library/PackageCache" -Directory) {
    $landformPackageJson = Join-Path $landformPackage.FullName 'package.json'
    if (Test-Path -LiteralPath $landformPackageJson) {
        $landformInfo = Get-Content $landformPackageJson -Raw | ConvertFrom-Json
        $landformDependencies[$landformInfo.name] = 'file:' + $landformPackage.FullName.Replace('\','/')
    }
}
@{ dependencies=$landformDependencies } | ConvertTo-Json -Depth 4 | Set-Content "$landformProject/Packages/manifest.json"
if ($PrepareOnly) { Write-Output "Prepared $landformProject"; return }
$landformLog = Join-Path $landformWork 'validation.log'
if ($TerrainIssues) {
    $landformLog = Join-Path $landformWork 'ridge-validation.log'
    if ($Baseline) { $landformLog = Join-Path $landformWork 'ridge-baseline.log' }
    if ($Probe) { $landformLog = Join-Path $landformWork 'ridge-probe.log' }
}
if ($Glacial) { $landformLog = Join-Path $landformWork 'glacial-validation.log' }
if ($Edges) { $landformLog = Join-Path $landformWork 'edge-validation.log' }
if ($Excavation) { $landformLog = Join-Path $landformWork 'excavation-validation.log' }
if ($ExcavationWorld) { $landformLog = Join-Path $landformWork 'excavation-world-validation.log' }
$landformOutput = Join-Path $landformRoot 'Documentation/Images/WorldLandforms'
$landformMethod = 'WorldLandformValidation.RunBatch'
if ($Glacial) {
    $landformMethod = 'GlacialLandformValidation.RunBatch'
    $landformOutput = Join-Path $landformRoot 'Documentation/Images/GlacialLandforms'
}
if ($Edges) {
    $landformMethod = 'TerrainEdgeValidation.RunBatch'
    $landformOutput = Join-Path $landformRoot 'Documentation/Images/TerrainEdges'
}
if ($Excavation) {
    $landformMethod = 'TerrainSteepExcavationValidation.RunBatch'
    $landformOutput = Join-Path $landformRoot 'Documentation/Images/SteepExcavation'
}
if ($ExcavationWorld) { $landformMethod = 'TerrainSteepExcavationValidation.RunWorldBatch' }
if ($TerrainIssues) {
    $landformMethod = 'TerrainRidgeValidation.RunBatch'
    if ($Probe) { $landformMethod = 'TerrainRidgeValidation.ProbeBatch' }
    $landformOutput = Join-Path $landformRoot 'Documentation/Images/TerrainRidges/After'
    if ($Baseline) { $landformOutput = Join-Path $landformRoot 'Documentation/Images/TerrainRidges/Before' }
}
$landformArguments = @('-batchmode','-force-d3d11','-burst-force-sync-compilation','-projectPath',('"'+$landformProject+'"'),
    '-executeMethod',$landformMethod,'-landformOutput',('"'+$landformOutput+'"'),'-logFile',('"'+$landformLog+'"'))
if ($Excavation) { $landformArguments += @('-terrainSourceRoot',('"'+$landformRoot+'"')) }
if ($TerrainIssues -or $Glacial -or $Edges) {
    $landformArguments += @('-terrainIssueScene',('"'+$landformRoot+'/Assets/Scenes/SmearScene.unity"'))
    if ($Baseline) { $landformArguments += '-terrainIssueBaseline' }
}
$landformProcess = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$landformVersion/Editor/Unity.exe" -ArgumentList $landformArguments -WindowStyle Hidden -PassThru
$landformProcess.WaitForExit()
if ($landformProcess.ExitCode -ne 0) { throw "World landform validation failed: $landformLog" }
if ($ExcavationWorld) {
    if (-not (Select-String -LiteralPath $landformLog -Pattern 'STEEP EXCAVATION WORLD PASS' -Quiet)) { throw "Missing excavation world completion marker: $landformLog" }
    Write-Output "Excavation world and graphics validation passed: $landformLog"
    return
}
if ($Excavation) {
    if (-not (Select-String -LiteralPath $landformLog -Pattern 'STEEP EXCAVATION PASS' -Quiet)) { throw "Missing excavation completion marker: $landformLog" }
    Write-Output "Steep excavation validation passed: $landformOutput"
    return
}
if ($Edges) {
    if (-not (Select-String -LiteralPath $landformLog -Pattern 'TERRAIN EDGES PASS' -Quiet)) { throw "Missing edge completion marker: $landformLog" }
    Write-Output "Terrain edge validation passed: $landformOutput"
    return
}
if ($Glacial) {
    if (-not (Select-String -LiteralPath $landformLog -Pattern 'GLACIAL LANDFORMS PASS' -Quiet)) { throw "Missing glacial completion marker: $landformLog" }
    Copy-Item -LiteralPath "$landformProject/Assets/Materials/M_Terrain/M_TerrainBase.mat" -Destination $landformMaterial -Force
    Get-ChildItem -LiteralPath "$landformProject/Assets/Textures/Ground/Glacial" -File | Copy-Item -Destination "$landformRoot/Assets/Textures/Ground/Glacial" -Force
    Write-Output "Glacial landform validation passed: $landformOutput"
    return
}
if ($TerrainIssues) {
    if ($Probe) { Write-Output "Terrain ridge probe: $landformLog"; return }
    if (-not (Select-String -LiteralPath $landformLog -Pattern 'TERRAIN RIDGES (PASS|BASELINE)' -Quiet)) { throw "Missing terrain ridge completion marker: $landformLog" }
    Write-Output "Terrain ridge validation passed: $landformOutput"
    return
}
if (-not (Select-String -LiteralPath $landformLog -Pattern 'WORLD LANDFORMS PASS' -Quiet)) { throw "Missing completion marker: $landformLog" }
Write-Output "World landform validation passed: $landformLog"
# Publish only the requested baked assets and terrain-material bindings after validation.
Copy-Item -LiteralPath "$landformProject/Assets/Materials/M_Terrain/M_TerrainBase.mat" -Destination $landformMaterial -Force
Get-ChildItem -LiteralPath "$landformProject/Assets/Textures/Ground/Desert" -File | Copy-Item -Destination "$landformRoot/Assets/Textures/Ground/Desert" -Force
