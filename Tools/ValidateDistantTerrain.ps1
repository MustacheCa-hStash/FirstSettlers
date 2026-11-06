param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$cliffRoot = Split-Path -Parent $PSScriptRoot
$cliffWork = Join-Path $cliffRoot '.utmp/distant-terrain'
$cliffProject = Join-Path $cliffWork 'unity-project'
$cliffVersion = ((Get-Content "$cliffRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
New-Item -ItemType Directory -Path "$cliffProject/Assets/Editor","$cliffProject/Packages","$cliffProject/ProjectSettings" -Force | Out-Null
foreach ($cliffDirectory in 'Scripts','Shaders','Settings') {
    Copy-Item -LiteralPath "$cliffRoot/Assets/$cliffDirectory" -Destination "$cliffProject/Assets" -Recurse -Force
}
foreach ($cliffFile in 'CliffTextureValidation.cs','TerrainMaterialInspector.cs','TerrainMaterialInspectorRules.cs','TerrainMaterialInspectorValidation.cs') {
    Copy-Item -LiteralPath "$cliffRoot/Assets/Editor/$cliffFile" -Destination "$cliffProject/Assets/Editor" -Force
}
foreach ($cliffSetting in 'ProjectVersion.txt','ProjectSettings.asset','TagManager.asset') {
    Copy-Item -LiteralPath "$cliffRoot/ProjectSettings/$cliffSetting" -Destination "$cliffProject/ProjectSettings" -Force
}
# Copy only the base material's texture dependencies, preserving their GUIDs.
$cliffMaterial = "$cliffRoot/Assets/Materials/M_Terrain/M_TerrainBase.mat"
$cliffGuids = @([regex]::Matches((Get-Content $cliffMaterial -Raw), 'guid: ([a-f0-9]{32})') | ForEach-Object { $_.Groups[1].Value })
foreach ($cliffMeta in Get-ChildItem "$cliffRoot/Assets/Textures" -Filter '*.meta' -Recurse -File) {
    if ((Get-Content $cliffMeta.FullName -First 2) -match ('guid: (' + ($cliffGuids -join '|') + ')')) {
        $cliffDestination = Join-Path $cliffProject $cliffMeta.FullName.Substring($cliffRoot.Length + 1)
        New-Item -ItemType Directory -Path (Split-Path -Parent $cliffDestination) -Force | Out-Null
        Copy-Item -LiteralPath $cliffMeta.FullName -Destination $cliffDestination -Force
        Copy-Item -LiteralPath $cliffMeta.FullName.Substring(0,$cliffMeta.FullName.Length-5) -Destination $cliffDestination.Substring(0,$cliffDestination.Length-5) -Force
    }
}
New-Item -ItemType Directory -Path "$cliffProject/Assets/Materials/M_Terrain" -Force | Out-Null
Copy-Item -LiteralPath $cliffMaterial,"$cliffMaterial.meta" -Destination "$cliffProject/Assets/Materials/M_Terrain" -Force
$cliffManifest = Get-Content "$cliffRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
$cliffDependencies = [ordered]@{}
foreach ($cliffDependency in $cliffManifest.dependencies.PSObject.Properties) {
    if ($cliffDependency.Name.StartsWith('com.unity.modules.')) { $cliffDependencies[$cliffDependency.Name] = $cliffDependency.Value }
}
foreach ($cliffPackage in Get-ChildItem "$cliffRoot/Library/PackageCache" -Directory) {
    $cliffPackageJson = Join-Path $cliffPackage.FullName 'package.json'
    if (Test-Path -LiteralPath $cliffPackageJson) {
        $cliffInfo = Get-Content $cliffPackageJson -Raw | ConvertFrom-Json
        $cliffDependencies[$cliffInfo.name] = 'file:' + $cliffPackage.FullName.Replace('\','/')
    }
}
@{ dependencies=$cliffDependencies } | ConvertTo-Json -Depth 4 | Set-Content "$cliffProject/Packages/manifest.json"
if ($PrepareOnly) { Write-Output "Prepared $cliffProject"; return }
$cliffLog = Join-Path $cliffWork 'validation.log'
$cliffOutput = Join-Path $cliffRoot 'Documentation/Images/DistantTerrain'
$cliffArguments = @('-batchmode','-force-d3d11','-burst-force-sync-compilation','-projectPath',('"'+$cliffProject+'"'),
    '-executeMethod','CliffTextureValidation.RunBatch','-cliffOutput',('"'+$cliffOutput+'"'),'-logFile',('"'+$cliffLog+'"'))
$cliffProcess = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$cliffVersion/Editor/Unity.exe" -ArgumentList $cliffArguments -WindowStyle Hidden -PassThru
$cliffProcess.WaitForExit()
if ($cliffProcess.ExitCode -ne 0) { throw "Distant terrain validation failed: $cliffLog" }
foreach ($cliffMarker in 'CLIFF VALIDATION PASS','CLIFF DISTANCE COLOR PASS','CLIFF MIP PASS','CLIFF GENERATED TERRAIN PASS','TERRAIN MATERIAL INSPECTOR PASS') {
    if (-not (Select-String -LiteralPath $cliffLog -Pattern $cliffMarker -Quiet)) { throw "Missing $cliffMarker in $cliffLog" }
}
Select-String -LiteralPath $cliffLog -Pattern 'Cliff shader warmup:|CLIFF .*PASS|TERRAIN MATERIAL INSPECTOR PASS' |
    ForEach-Object { $_.Line } | Set-Content (Join-Path $cliffOutput 'validation.txt')
Write-Output "Distant terrain validation passed: $cliffOutput"
