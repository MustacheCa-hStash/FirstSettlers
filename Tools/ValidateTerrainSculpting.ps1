param([switch]$CompileOnly, [switch]$PrepareOnly, [switch]$MeshChecks, [switch]$IntegrationChecks, [switch]$PersistenceChecks, [switch]$InteractionChecks, [switch]$PerformanceChecks, [switch]$WorldChecks)
$ErrorActionPreference = 'Stop'
$sculptRoot = Split-Path -Parent $PSScriptRoot
$sculptWork = Join-Path $sculptRoot '.utmp/terrain-sculpting'
$sculptProject = Join-Path $sculptWork 'unity-project'
$sculptPlugins = Join-Path $sculptProject 'Assets/Plugins'
$sculptVersion = ((Get-Content (Join-Path $sculptRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$sculptEditor = "C:/Program Files/Unity/Hub/Editor/$sculptVersion/Editor"
if (-not (Test-Path -LiteralPath "$sculptEditor/Unity.exe")) { throw 'Install the project Unity editor version before validation.' }
New-Item -ItemType Directory -Path $sculptWork -Force | Out-Null

# Compile the actual complete runtime/editor sources against Unity's established reference sets.
Push-Location $sculptRoot
try {
    foreach ($sculptKind in 'runtime','editor','player') {
        $sculptAssembly = if ($sculptKind -eq 'editor') { 'Assembly-CSharp-Editor' } else { 'Assembly-CSharp' }
        $sculptDag = if ($sculptKind -eq 'player') { 'PDevDbg\.dag' } else { 'E\.dag' }
        $sculptResponse = Get-ChildItem 'Library/Bee/artifacts' -Filter "$sculptAssembly.rsp" -Recurse |
            Where-Object { $_.FullName -match $sculptDag } | Select-Object -First 1
        if (-not $sculptResponse) { throw "Missing Unity $sculptKind reference response. Compile that target in Unity first." }
        $sculptLines = @(Get-Content -LiteralPath $sculptResponse.FullName | Where-Object {
            $_ -notmatch '^".*\.cs"$' -and $_ -notmatch '^-(out|refout):'
        })
        if ($sculptKind -eq 'editor') {
            $sculptLines = @($sculptLines | ForEach-Object {
                $_ -replace 'Library/Bee/artifacts/[^/]+/Assembly-CSharp(\.ref)?\.dll', '.utmp/terrain-sculpting/Assembly-CSharp.dll'
            })
        }
        if ($sculptKind -eq 'player') {
            # Older cached player responses can predate the separate profiling asmdef.
            # Compile it against the player reference set rather than borrowing an editor DLL.
            $sculptProfilingDir = Join-Path $sculptWork 'player-profile'
            New-Item -ItemType Directory -Path $sculptProfilingDir -Force | Out-Null
            $sculptProfilingLines = @($sculptLines | Where-Object { $_ -notmatch '^-r:.*FirstSettlers\.Profiling' })
            $sculptProfilingLines += '-unsafe+'
            $sculptProfilingLines += '-out:".utmp/terrain-sculpting/player-profile/FirstSettlers.Profiling.dll"'
            foreach ($sculptProfilingSource in Get-ChildItem 'Assets/Scripts/Profiling' -Filter '*.cs' -Recurse) {
                $sculptProfilingLines += '"' + $sculptProfilingSource.FullName.Replace('\','/') + '"'
            }
            $sculptProfilingRsp = Join-Path $sculptWork 'player-profile.rsp'
            $sculptProfilingLines | Set-Content -LiteralPath $sculptProfilingRsp -Encoding utf8
            & "$sculptEditor/Data/netcorerun/netcorerun.exe" "$sculptEditor/Data/DotNetSdkRoslyn/csc.dll" "@$sculptProfilingRsp"
            if ($LASTEXITCODE -ne 0) { throw 'Player profiling dependency compilation failed.' }
            $sculptLines = @($sculptLines | Where-Object { $_ -notmatch '^-r:.*FirstSettlers\.Profiling' })
            $sculptLines += '-r:".utmp/terrain-sculpting/player-profile/FirstSettlers.Profiling.dll"'
        }
        $sculptOutput = if ($sculptKind -eq 'player') { 'Assembly-CSharp-Player' } else { $sculptAssembly }
        $sculptLines += '-out:".utmp/terrain-sculpting/' + $sculptOutput + '.dll"'
        $sculptFolder = if ($sculptKind -eq 'editor') { 'Assets/Editor' } else { 'Assets/Scripts' }
        $sculptSources = @(Get-ChildItem $sculptFolder -Filter '*.cs' -Recurse)
        if ($sculptKind -ne 'editor') { $sculptSources += @(Get-ChildItem 'Assets/Models' -Filter '*.cs' -Recurse) }
        $sculptAssemblyFolders = @(Get-ChildItem $sculptFolder -Filter '*.asmdef' -Recurse | ForEach-Object { $_.Directory.FullName + [IO.Path]::DirectorySeparatorChar })
        foreach ($sculptSource in $sculptSources) {
            $sculptSourcePath = $sculptSource.FullName
            if (@($sculptAssemblyFolders | Where-Object { $sculptSourcePath.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) }).Count) { continue }
            $sculptLines += '"' + $sculptSource.FullName.Replace('\','/') + '"'
        }
        $sculptRsp = Join-Path $sculptWork "$sculptKind.rsp"
        $sculptLines | Set-Content -LiteralPath $sculptRsp -Encoding utf8
        & "$sculptEditor/Data/netcorerun/netcorerun.exe" "$sculptEditor/Data/DotNetSdkRoslyn/csc.dll" "@$sculptRsp"
        if ($LASTEXITCODE -ne 0) { throw "$sculptKind compilation failed." }
        Write-Output "Sculpting $sculptKind compilation passed."
    }
} finally { Pop-Location }
if ($CompileOnly) { return }

New-Item -ItemType Directory -Path $sculptPlugins,(Join-Path $sculptProject 'Assets/Editor'),(Join-Path $sculptProject 'Packages'),(Join-Path $sculptProject 'ProjectSettings') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sculptRoot 'Assets/Scripts') -Destination (Join-Path $sculptProject 'Assets') -Recurse -Force
foreach ($sculptFixture in 'TerrainSculptingValidation.cs','TerrainSculptingMeshValidation.cs','TerrainSculptingIntegrationValidation.cs','TerrainSculptingPersistenceValidation.cs','TerrainSculptingInteractionValidation.cs','TerrainSculptingPerformanceValidation.cs','TerrainSculptingWorldValidation.cs','TerrainSkyAccessValidation.cs','TerrainHeightSamplingValidation.cs') {
    Copy-Item -LiteralPath (Join-Path $sculptRoot "Assets/Editor/$sculptFixture") -Destination (Join-Path $sculptProject 'Assets/Editor') -Force
}
if ($InteractionChecks) {
    New-Item -ItemType Directory -Path (Join-Path $sculptProject 'Assets/Resources'),(Join-Path $sculptProject 'Assets/Shaders/S_Terrain') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $sculptRoot 'Assets/Resources/TerrainSculpting') -Destination (Join-Path $sculptProject 'Assets/Resources') -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $sculptRoot 'Assets/Shaders/S_Terrain/TerrainSculptPreview.shader'),(Join-Path $sculptRoot 'Assets/Shaders/S_Terrain/TerrainSculptPreview.shader.meta') -Destination (Join-Path $sculptProject 'Assets/Shaders/S_Terrain') -Force
}
foreach ($sculptSetting in 'ProjectVersion.txt','TagManager.asset') {
    Copy-Item -LiteralPath (Join-Path $sculptRoot "ProjectSettings/$sculptSetting") -Destination (Join-Path $sculptProject 'ProjectSettings') -Force
}
Get-ChildItem (Join-Path $sculptRoot 'Library/ScriptAssemblies') -Filter '*.dll' |
    Where-Object { $_.Name -notmatch 'Assembly-CSharp|Editor|CodeGen|TestFramework|TestRunner|DocCodeSamples|DocCodeExamples|FirstSettlers\.Profiling' } |
    Copy-Item -Destination $sculptPlugins -Force
foreach ($sculptEditorDependency in 'UnityEditor.UI.dll','UnityEngine.TestRunner.dll','UnityEditor.TestRunner.dll','Unity.AI.Navigation.Editor.ConversionSystem.dll','Unity.AI.Navigation.Editor.dll') {
    Copy-Item -LiteralPath (Join-Path $sculptRoot "Library/ScriptAssemblies/$sculptEditorDependency") -Destination $sculptPlugins -Force
}
$sculptRuntimeResponse = Get-ChildItem (Join-Path $sculptRoot 'Library/Bee/artifacts') -Filter Assembly-CSharp.rsp -Recurse |
    Where-Object { $_.FullName -match 'E\.dag' } | Select-Object -First 1
foreach ($sculptReference in Get-Content -LiteralPath $sculptRuntimeResponse.FullName) {
    if ($sculptReference -match '^-r:"(Library/PackageCache/[^\"]+\.dll)"$') {
        Copy-Item -LiteralPath (Join-Path $sculptRoot $Matches[1]) -Destination $sculptPlugins -Force
    }
}
$sculptBurst = Get-ChildItem (Join-Path $sculptRoot 'Library/PackageCache') -Directory -Filter 'com.unity.burst@*' | Select-Object -First 1
if (-not $sculptBurst) { throw 'The existing Burst package cache is required.' }
Copy-Item -LiteralPath (Join-Path $sculptBurst.FullName 'Unity.Burst.Unsafe.dll') -Destination $sculptPlugins -Force
$sculptBurstTarget = Join-Path $sculptProject 'Packages/com.unity.burst'
New-Item -ItemType Directory -Path $sculptBurstTarget -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $sculptBurst.FullName '.Runtime') -Destination $sculptBurstTarget -Recurse -Force
$sculptManifest = Get-Content (Join-Path $sculptRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
$sculptBuiltins = [ordered]@{}
foreach ($sculptDependency in $sculptManifest.dependencies.PSObject.Properties) {
    if ($sculptDependency.Name.StartsWith('com.unity.modules.')) { $sculptBuiltins[$sculptDependency.Name] = $sculptDependency.Value }
}
@{ dependencies = $sculptBuiltins } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $sculptProject 'Packages/manifest.json')
if ($PrepareOnly) { Write-Output "Prepared sculpting validation: $sculptProject"; return }

$sculptLog = Join-Path $sculptWork 'validation.log'
$sculptArguments = @('-batchmode','-nographics','-projectPath',('"' + $sculptProject + '"'),'-executeMethod','TerrainSculptingValidation.RunBatch','-terrainSourceRoot',('"' + $sculptRoot + '"'),'-logFile',('"' + $sculptLog + '"'))
if ($MeshChecks) { $sculptArguments += '-sculptMeshChecks' }
if ($IntegrationChecks) { $sculptArguments += '-sculptIntegrationChecks' }
if ($PersistenceChecks) { $sculptArguments += '-sculptPersistenceChecks' }
if ($InteractionChecks) { $sculptArguments += '-sculptInteractionChecks' }
if ($PerformanceChecks) { $sculptArguments += '-sculptPerformanceChecks' }
if ($WorldChecks) { $sculptArguments += '-sculptWorldChecks' }
$sculptProcess = Start-Process -FilePath "$sculptEditor/Unity.exe" -ArgumentList $sculptArguments -WindowStyle Hidden -PassThru
$sculptProcess.WaitForExit()
if ($sculptProcess.ExitCode -ne 0) { throw "Sculpting validation failed; inspect $sculptLog" }
if (-not (Select-String -LiteralPath $sculptLog -Pattern 'TERRAIN SCULPTING P1 COMPLETE:' -Quiet)) { throw 'Unity exited without the sculpting completion marker.' }
if ($MeshChecks -and -not (Select-String -LiteralPath $sculptLog -Pattern 'TERRAIN SCULPTING P2 COMPLETE:' -Quiet)) { throw 'Unity exited without the P2 mesh completion marker.' }
if ($IntegrationChecks -and -not (Select-String -LiteralPath $sculptLog -Pattern 'TERRAIN SCULPTING P3 COMPLETE:' -Quiet)) { throw 'Unity exited without the P3 integration completion marker.' }
if ($PersistenceChecks -and -not (Select-String -LiteralPath $sculptLog -Pattern 'TERRAIN SCULPTING P4 COMPLETE:' -Quiet)) { throw 'Unity exited without the P4 persistence completion marker.' }
if ($InteractionChecks -and -not (Select-String -LiteralPath $sculptLog -Pattern 'TERRAIN SCULPTING P5 INTERACTION PASS:' -Quiet)) { throw 'Unity exited without the P5 interaction completion marker.' }
if ($PerformanceChecks -and -not (Select-String -LiteralPath $sculptLog -Pattern 'TERRAIN SCULPTING PERFORMANCE PASS:' -Quiet)) { throw 'Unity exited without the performance completion marker.' }
if ($WorldChecks -and -not (Select-String -LiteralPath $sculptLog -Pattern 'TERRAIN SCULPTING P6 CONSUMERS PASS:' -Quiet)) { throw 'Unity exited without the P6 consumer completion marker.' }
Write-Output "Sculpting density validation passed: $sculptLog"
