param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$oakRoot = Split-Path -Parent $PSScriptRoot
$oakProject = Join-Path $oakRoot '.utmp/oak-materials/unity-project'
foreach ($folder in @('Assets/Editor','Assets/Materials/M_Trees','Assets/Textures/Trees/Bark','Assets/Prefabs','Assets/Models/Trees','Packages','ProjectSettings')) {
    New-Item -ItemType Directory -Force -Path (Join-Path $oakProject $folder) | Out-Null
}
# Use cached local packages so this check needs no registry/network access.
$oakManifest = Get-Content -LiteralPath (Join-Path $oakRoot 'Packages/manifest.json') -Raw | ConvertFrom-Json
$oakDependencies = [ordered]@{}
$oakPending = [Collections.Generic.Queue[string]]::new()
foreach ($dependency in $oakManifest.dependencies.PSObject.Properties.Name) {
    if ($dependency.StartsWith('com.unity.modules.')) { $oakDependencies[$dependency] = '1.0.0' }
}
foreach ($dependency in @('com.unity.render-pipelines.universal','com.unity.inputsystem','com.unity.ai.navigation','com.unity.ugui')) { $oakPending.Enqueue($dependency) }
while ($oakPending.Count -gt 0) {
    $dependency = $oakPending.Dequeue()
    if ($oakDependencies.Contains($dependency)) { continue }
    if ($dependency.StartsWith('com.unity.modules.')) { $oakDependencies[$dependency] = '1.0.0'; continue }
    $directory = Get-ChildItem -LiteralPath (Join-Path $oakRoot 'Library/PackageCache') -Directory |
        Where-Object { $_.Name.StartsWith($dependency + '@') } | Select-Object -First 1
    if (!$directory) { throw "Missing cached package: $dependency" }
    $oakDependencies[$dependency] = 'file:' + $directory.FullName.Replace('\','/')
    $package = Get-Content -LiteralPath (Join-Path $directory.FullName 'package.json') -Raw | ConvertFrom-Json
    if ($package.dependencies) { foreach ($child in $package.dependencies.PSObject.Properties.Name) { $oakPending.Enqueue($child) } }
}
@{dependencies=$oakDependencies} | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $oakProject 'Packages/manifest.json')
foreach ($folder in @('Scripts','Shaders')) {
    Copy-Item -LiteralPath (Join-Path $oakRoot "Assets/$folder") -Destination (Join-Path $oakProject 'Assets') -Recurse -Force
}
foreach ($folder in @('Materials/M_Trees/Oak','Textures/Trees/Oak')) {
    Copy-Item -LiteralPath (Join-Path $oakRoot "Assets/$folder") -Destination (Split-Path -Parent (Join-Path $oakProject "Assets/$folder")) -Recurse -Force
    Copy-Item -LiteralPath (Join-Path $oakRoot "Assets/$folder.meta") -Destination (Join-Path $oakProject "Assets/$folder.meta") -Force
}
foreach ($file in @('Editor/OakMaterialValidation.cs','Editor/OakMaterialValidation.cs.meta','Textures/Trees/Bark/T_WhiteOakBark_Stylized.png','Textures/Trees/Bark/T_WhiteOakBark_Stylized.png.meta','Prefabs/Tree_Blender_012_Oak.prefab','Prefabs/Tree_Blender_012_Oak.prefab.meta','Models/Trees/Tree_Blender_012_Oak.fbx','Models/Trees/Tree_Blender_012_Oak.fbx.meta','Models/Trees/Oak_LOD0_P2557_B1800_L600_v02.fbx','Models/Trees/Oak_LOD0_P2557_B1800_L600_v02.fbx.meta')) {
    Copy-Item -LiteralPath (Join-Path $oakRoot "Assets/$file") -Destination (Join-Path $oakProject "Assets/$file") -Force
}
foreach ($file in @('ProjectVersion.txt','ProjectSettings.asset','TagManager.asset')) {
    Copy-Item -LiteralPath (Join-Path $oakRoot "ProjectSettings/$file") -Destination (Join-Path $oakProject "ProjectSettings/$file") -Force
}
if ($PrepareOnly) { Write-Output $oakProject; return }
$oakVersion = ((Get-Content -LiteralPath (Join-Path $oakRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$oakUnity = "C:/Program Files/Unity/Hub/Editor/$oakVersion/Editor/Unity.exe"
$oakLog = Join-Path $oakRoot '.utmp/oak-materials/validation.log'
$oakArgs = @('-batchmode','-projectPath',$oakProject,'-executeMethod','OakMaterialValidation.RunBatch','-logFile',$oakLog)
$oakProcess = Start-Process -FilePath $oakUnity -ArgumentList ($oakArgs | ForEach-Object { '"' + $_ + '"' }) -WindowStyle Hidden -PassThru
Write-Output "Oak Unity validation process $($oakProcess.Id); log $oakLog"
