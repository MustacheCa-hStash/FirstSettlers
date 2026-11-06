param([switch]$PrepareOnly)
$ErrorActionPreference = 'Stop'
$waterRoot = Split-Path -Parent $PSScriptRoot
$waterWork = Join-Path $waterRoot '.utmp/water-flow'
$waterProject = Join-Path $waterWork 'unity-project'
New-Item -ItemType Directory -Path "$waterProject/Assets/Editor","$waterProject/Assets/Materials","$waterProject/Packages","$waterProject/ProjectSettings" -Force | Out-Null
foreach ($waterDirectory in 'Scripts','Shaders','Settings') {
    Copy-Item -LiteralPath "$waterRoot/Assets/$waterDirectory" -Destination "$waterProject/Assets" -Recurse -Force
}
foreach ($waterFile in 'WaterFlowValidation.cs','WaterWaveMeshValidation.cs','WaterMeshValidation.cs','WaterGenerationValidation.cs','WaterReflectionVisibilityValidation.cs','DistantFogValidation.cs','DistantFogSetup.cs') {
    Copy-Item -LiteralPath "$waterRoot/Assets/Editor/$waterFile" -Destination "$waterProject/Assets/Editor" -Force
}
foreach ($waterFile in 'WaterMeshBaseMaterial.mat','WaterMeshBaseMaterial.mat.meta') {
    Copy-Item -LiteralPath "$waterRoot/Assets/Materials/$waterFile" -Destination "$waterProject/Assets/Materials" -Force
}
New-Item -ItemType Directory -Path "$waterProject/Assets/Prefabs/VincentPrefabs/Boulders","$waterProject/Assets/Resources/Rocks" -Force | Out-Null
Copy-Item -LiteralPath "$waterRoot/Assets/Prefabs/VincentPrefabs/Boulders/Boulder1.fbx","$waterRoot/Assets/Prefabs/VincentPrefabs/Boulders/Boulder1.fbx.meta" -Destination "$waterProject/Assets/Prefabs/VincentPrefabs/Boulders" -Force
Copy-Item -LiteralPath "$waterRoot/Assets/Resources/Rocks/Stone_Bare.mat","$waterRoot/Assets/Resources/Rocks/Stone_Bare.mat.meta" -Destination "$waterProject/Assets/Resources/Rocks" -Force
# Fog regression uses the actual terrain material and its texture dependencies.
$waterTerrain = "$waterRoot/Assets/Materials/M_Terrain/M_TerrainBase.mat"
New-Item -ItemType Directory -Path "$waterProject/Assets/Materials/M_Terrain" -Force | Out-Null
Copy-Item -LiteralPath $waterTerrain,"$waterTerrain.meta" -Destination "$waterProject/Assets/Materials/M_Terrain" -Force
$waterGuids = @([regex]::Matches((Get-Content $waterTerrain -Raw), 'guid: ([a-f0-9]{32})') | ForEach-Object { $_.Groups[1].Value })
foreach ($waterMeta in Get-ChildItem "$waterRoot/Assets/Textures" -Filter '*.meta' -Recurse -File) {
    if ((Get-Content $waterMeta.FullName -First 2) -match ('guid: (' + ($waterGuids -join '|') + ')')) {
        $waterDestination = Join-Path $waterProject $waterMeta.FullName.Substring($waterRoot.Length + 1)
        New-Item -ItemType Directory -Path (Split-Path -Parent $waterDestination) -Force | Out-Null
        Copy-Item -LiteralPath $waterMeta.FullName -Destination $waterDestination -Force
        Copy-Item -LiteralPath $waterMeta.FullName.Substring(0,$waterMeta.FullName.Length-5) -Destination $waterDestination.Substring(0,$waterDestination.Length-5) -Force
    }
}
foreach ($waterFile in 'ProjectVersion.txt','ProjectSettings.asset','TagManager.asset') {
    Copy-Item -LiteralPath "$waterRoot/ProjectSettings/$waterFile" -Destination "$waterProject/ProjectSettings" -Force
}
$waterDependencies = [ordered]@{}
$waterManifest = Get-Content "$waterRoot/Packages/manifest.json" -Raw | ConvertFrom-Json
foreach ($waterDependency in $waterManifest.dependencies.PSObject.Properties) {
    if ($waterDependency.Name.StartsWith('com.unity.modules.')) { $waterDependencies[$waterDependency.Name] = $waterDependency.Value }
}
foreach ($waterPackage in Get-ChildItem "$waterRoot/Library/PackageCache" -Directory) {
    $waterPackageJson = Join-Path $waterPackage.FullName 'package.json'
    if (Test-Path -LiteralPath $waterPackageJson) {
        $waterInfo = Get-Content $waterPackageJson -Raw | ConvertFrom-Json
        $waterDependencies[$waterInfo.name] = 'file:' + $waterPackage.FullName.Replace('\','/')
    }
}
@{ dependencies=$waterDependencies } | ConvertTo-Json -Depth 4 | Set-Content "$waterProject/Packages/manifest.json"
# Baseline belongs to the validation fixture, never the shipping shader directory.
if (-not (Test-Path -LiteralPath "$waterWork/Before.shader")) {
    git -C $waterRoot show HEAD:Assets/Shaders/S_River/MurkyPlanarWater.shader | Set-Content "$waterWork/Before.shader"
}
if ($PrepareOnly) { Write-Output "Prepared $waterProject"; return }
$waterVersion = ((Get-Content "$waterRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
$waterLog = Join-Path $waterWork 'validation.log'
$waterOutput = Join-Path $waterRoot 'Documentation/Images/WaterFlow'
$waterArguments = @('-batchmode','-force-d3d11','-burst-force-sync-compilation','-projectPath',('"'+$waterProject+'"'),
    '-executeMethod','WaterFlowValidation.RunBatch','-waterSourceRoot',('"'+$waterRoot+'"'),'-waterOutput',('"'+$waterOutput+'"'),'-logFile',('"'+$waterLog+'"'))
$waterProcess = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$waterVersion/Editor/Unity.exe" -ArgumentList $waterArguments -WindowStyle Hidden -PassThru
$waterProcess.WaitForExit()
if ($waterProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $waterLog -Pattern 'WATER FLOW GPU PASS' -Quiet)) { throw "Water flow validation failed: $waterLog" }
Add-Type -AssemblyName PresentationCore
$waterGif = New-Object System.Windows.Media.Imaging.GifBitmapEncoder
foreach ($waterFrame in Get-ChildItem -LiteralPath $waterOutput -Filter 'Flow-*.png' | Sort-Object Name) {
    $waterBitmap = New-Object System.Windows.Media.Imaging.BitmapImage
    $waterBitmap.BeginInit()
    $waterBitmap.CacheOption = [System.Windows.Media.Imaging.BitmapCacheOption]::OnLoad
    $waterBitmap.UriSource = [Uri]$waterFrame.FullName
    $waterBitmap.EndInit()
    $waterMetadata = New-Object System.Windows.Media.Imaging.BitmapMetadata('gif')
    $waterMetadata.SetQuery('/grctlext/Delay', [uint16]17)
    $waterMetadata.SetQuery('/grctlext/Disposal', [byte]2)
    if ($waterGif.Frames.Count -eq 0) {
        $waterMetadata.SetQuery('/appext/application', [System.Text.Encoding]::ASCII.GetBytes('NETSCAPE2.0'))
        $waterMetadata.SetQuery('/appext/data', [byte[]](3,1,0,0,0))
    }
    $waterGif.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($waterBitmap,$null,$waterMetadata,$null))
}
$waterStream = [System.IO.File]::Create((Join-Path $waterOutput 'WaterFlow.gif'))
try { $waterGif.Save($waterStream) } finally { $waterStream.Dispose() }
Write-Output "Water flow validation passed: $waterOutput"
