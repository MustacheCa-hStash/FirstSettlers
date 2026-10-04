param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$terrainRoot = Split-Path -Parent $PSScriptRoot
$terrainValidationProject = Join-Path $terrainRoot '.utmp/terrain-debug-validation/unity-project'
$terrainValidationPlugins = Join-Path $terrainValidationProject 'Assets/Plugins'
$terrainEditorVersion = ((Get-Content (Join-Path $terrainRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$terrainEditorExecutable = "C:/Program Files/Unity/Hub/Editor/$terrainEditorVersion/Editor/Unity.exe"
if (-not (Test-Path -LiteralPath $terrainEditorExecutable)) { throw 'Install the project Unity version before validation.' }
New-Item -ItemType Directory -Path $terrainValidationPlugins,(Join-Path $terrainValidationProject 'Assets/Editor'),(Join-Path $terrainValidationProject 'Packages'),(Join-Path $terrainValidationProject 'ProjectSettings'),(Join-Path $terrainRoot 'Logs') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $terrainRoot 'Assets/Scripts') -Destination (Join-Path $terrainValidationProject 'Assets') -Recurse -Force
foreach ($terrainFile in 'PerformanceDebugValidation.cs','DistantTreeGroundingValidation.cs','TerrainErrorMeasurement.cs','TerrainErrorMeasurementValidation.cs','WorldManagerInspectorValidation.cs','WorldManagerInspectorRules.cs','WorldManagerEditor.cs','GrassStreamingValidation.cs','BillboardGrassStreamingValidation.cs') {
    Copy-Item -LiteralPath (Join-Path $terrainRoot "Assets/Editor/$terrainFile") -Destination (Join-Path $terrainValidationProject 'Assets/Editor')
}
Copy-Item -LiteralPath (Join-Path $terrainRoot 'ProjectSettings/ProjectVersion.txt') -Destination (Join-Path $terrainValidationProject 'ProjectSettings')
Copy-Item -LiteralPath (Join-Path $terrainRoot 'ProjectSettings/TagManager.asset') -Destination (Join-Path $terrainValidationProject 'ProjectSettings')
Get-ChildItem (Join-Path $terrainRoot 'Library/ScriptAssemblies') -Filter '*.dll' |
    Where-Object { $_.Name -notmatch 'Assembly-CSharp|Editor|CodeGen|TestFramework|TestRunner|DocCodeSamples|DocCodeExamples' } |
    Copy-Item -Destination $terrainValidationPlugins
foreach ($terrainAssembly in 'UnityEditor.UI.dll','UnityEngine.TestRunner.dll','UnityEditor.TestRunner.dll','Unity.AI.Navigation.Editor.ConversionSystem.dll','Unity.AI.Navigation.Editor.dll') {
    Copy-Item -LiteralPath (Join-Path $terrainRoot "Library/ScriptAssemblies/$terrainAssembly") -Destination $terrainValidationPlugins
}
$terrainResponse = Get-ChildItem (Join-Path $terrainRoot 'Library/Bee/artifacts') -Filter Assembly-CSharp.rsp -Recurse | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $terrainResponse) { throw 'Let the main project compile once before preparing validation dependencies.' }
foreach ($terrainLine in (Get-Content -LiteralPath $terrainResponse.FullName)) {
    if ($terrainLine -match '^-r:"(Library/PackageCache/[^\"]+\.dll)"$') {
        Copy-Item -LiteralPath (Join-Path $terrainRoot $Matches[1]) -Destination $terrainValidationPlugins
    }
}
$terrainBurstPackage = Get-ChildItem (Join-Path $terrainRoot 'Library/PackageCache') -Directory -Filter 'com.unity.burst@*' | Select-Object -First 1
if (-not $terrainBurstPackage) { throw 'The project Burst package cache is required.' }
Copy-Item -LiteralPath (Join-Path $terrainBurstPackage.FullName 'Unity.Burst.Unsafe.dll') -Destination $terrainValidationPlugins
$terrainBurstTarget = Join-Path $terrainValidationProject 'Packages/com.unity.burst'
New-Item -ItemType Directory -Path $terrainBurstTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $terrainBurstPackage.FullName '.Runtime') -Destination $terrainBurstTarget -Recurse -Force
$terrainManifest = Get-Content (Join-Path $terrainRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
$terrainBuiltins = [ordered]@{}
foreach ($terrainDependency in $terrainManifest.dependencies.PSObject.Properties) {
    if ($terrainDependency.Name.StartsWith('com.unity.modules.')) { $terrainBuiltins[$terrainDependency.Name] = $terrainDependency.Value }
}
@{dependencies=$terrainBuiltins} | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $terrainValidationProject 'Packages/manifest.json')
if ($PrepareOnly) { Write-Output "Prepared $terrainValidationProject"; return }
$terrainLog = Join-Path $terrainRoot 'Logs/terrain-debug-validation.log'
$terrainArguments = @('-batchmode','-nographics','-projectPath',('"' + $terrainValidationProject + '"'),'-executeMethod','PerformanceDebugValidation.RunBatch','-terrainSourceRoot',('"' + $terrainRoot + '"'),'-logFile',('"' + $terrainLog + '"'))
$terrainProcess = Start-Process -FilePath $terrainEditorExecutable -ArgumentList $terrainArguments -WindowStyle Hidden -PassThru
$terrainProcess.WaitForExit()
if ($terrainProcess.ExitCode -ne 0) { throw "Terrain debug validation failed; inspect $terrainLog" }
Write-Output "Terrain debug validation passed: $terrainLog"
