param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$p5Root = Split-Path -Parent $PSScriptRoot
# Reuse the established graphics/Input System isolation setup and its cached packages/assets.
& (Join-Path $PSScriptRoot 'ValidateBuildingPrototype.ps1') -PrepareOnly -Workspace building-prototype
$p5Project = Join-Path $p5Root '.utmp/building-prototype/unity-project'
foreach ($p5Name in 'TerrainSculptingValidation','TerrainSculptingMeshValidation','TerrainSculptingIntegrationValidation','TerrainSculptingPersistenceValidation','TerrainSculptingInteractionValidation','TerrainSculptingPerformanceValidation','TerrainSculptingWorldValidation','TerrainSkyAccessValidation','TerrainSculptingInputValidation','TerrainHeightSamplingValidation') {
    Copy-Item -LiteralPath (Join-Path $p5Root "Assets/Editor/$p5Name.cs"),(Join-Path $p5Root "Assets/Editor/$p5Name.cs.meta") -Destination (Join-Path $p5Project 'Assets/Editor') -Force
}
New-Item -ItemType Directory -Path (Join-Path $p5Project 'Assets/P5Fixture') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Fixtures/TerrainSculptInputValidationDriver.cs') -Destination (Join-Path $p5Project 'Assets/P5Fixture') -Force
Copy-Item -LiteralPath (Join-Path $p5Root 'Assets/Resources/TerrainSculpting') -Destination (Join-Path $p5Project 'Assets/Resources') -Recurse -Force
Copy-Item -LiteralPath (Join-Path $p5Root 'Assets/Settings') -Destination (Join-Path $p5Project 'Assets') -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $p5Project 'Assets/Shaders/S_Terrain') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $p5Root 'Assets/Shaders/S_Terrain/TerrainSculptPreview.shader'),(Join-Path $p5Root 'Assets/Shaders/S_Terrain/TerrainSculptPreview.shader.meta') -Destination (Join-Path $p5Project 'Assets/Shaders/S_Terrain') -Force
if ($PrepareOnly) { return }
$p5Version = ((Get-Content (Join-Path $p5Root 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$p5Unity = "C:/Program Files/Unity/Hub/Editor/$p5Version/Editor/Unity.exe"
$p5Log = Join-Path $p5Root '.utmp/terrain-sculpting/p5-input.log'
$p5Args = @('-batchmode','-force-d3d11','-projectPath',('"'+$p5Project+'"'),'-executeMethod','TerrainSculptingInputValidation.RunBatch','-terrainSourceRoot',('"'+$p5Root+'"'),'-logFile',('"'+$p5Log+'"'))
$p5Process = Start-Process -FilePath $p5Unity -ArgumentList $p5Args -WindowStyle Hidden -PassThru
$p5Process.WaitForExit()
if ($p5Process.ExitCode -ne 0) { throw "Native P5 input/render validation failed: $p5Log" }
if (-not (Select-String -LiteralPath $p5Log -Pattern 'TERRAIN SCULPTING P5 NATIVE INPUT PASS:' -Quiet)) { throw 'Unity exited without the P5 native completion marker.' }
Write-Output "Native P5 input/render validation passed: $p5Log"
