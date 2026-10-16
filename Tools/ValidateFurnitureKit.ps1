param([switch]$Install)
$ErrorActionPreference='Stop'
$furnRoot=Split-Path -Parent $PSScriptRoot
$furnWork=Join-Path $furnRoot '.utmp/furniture-kit'
$furnProject=Join-Path $furnWork 'unity-project'
$furnCatalog=Join-Path $furnRoot 'Assets/Resources/Building/PrototypeCatalog.asset'
$furnCatalogHash=(Get-FileHash -LiteralPath $furnCatalog).Hash
$furnProtected=@('Assets/Materials/Buildings/Wood/SplitPlankWood.mat','Assets/Textures/Buildings/Wood/ordinary-wood-trim-meadow-timber.png','Assets/Shaders/BuildingWoodMatte.shader')
$furnProtected+=@(Get-ChildItem -LiteralPath (Join-Path $furnRoot 'Assets') -Filter *.unity -Recurse | ForEach-Object { $_.FullName })
$furnHashes=@{}
foreach($furnPath in $furnProtected) { $furnAbsolute=if([IO.Path]::IsPathRooted($furnPath)){$furnPath}else{Join-Path $furnRoot $furnPath};$furnHashes[$furnAbsolute]=(Get-FileHash -LiteralPath $furnAbsolute).Hash }
& (Join-Path $PSScriptRoot 'ValidateBuildingPrototype.ps1') -PrepareOnly -UiAssets -Workspace furniture-kit
$furnVersion=((Get-Content (Join-Path $furnRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$furnUnity="C:/Program Files/Unity/Hub/Editor/$furnVersion/Editor/Unity.exe"
$furnLog=Join-Path $furnWork 'furniture.log'
$furnArgs=@('-batchmode','-projectPath',('"'+$furnProject+'"'),'-executeMethod','FurnitureKitValidation.RunBatch','-logFile',('"'+$furnLog+'"'))
$furnProcess=Start-Process -FilePath $furnUnity -ArgumentList $furnArgs -WindowStyle Hidden -PassThru
Write-Output "Furniture validation PID $($furnProcess.Id); log $furnLog"
while(-not $furnProcess.WaitForExit(1000)) {}
if($furnProcess.ExitCode -ne 0) { throw "Furniture validation failed; inspect $furnLog" }
$furnReport=Join-Path $furnProject '.utmp/furniture-kit/validation.json'
if(-not (Test-Path -LiteralPath $furnReport)) { throw 'Furniture runtime validation report missing.' }
$furnDestination=Join-Path $furnRoot 'ArtReferences/IronAgeFurniture/Models'
Copy-Item -LiteralPath $furnReport -Destination (Join-Path $furnDestination 'unity-validation.json') -Force
$furnUi=Join-Path $furnProject '.utmp/building-prototype/stone-ui'
foreach($furnSize in '1280x720','1920x1080','3840x2160','1024x768') {
    Copy-Item -LiteralPath (Join-Path $furnUi ("furniture-"+$furnSize+'.png')) -Destination $furnDestination -Force
}
if($Install) {
    if((Get-FileHash -LiteralPath $furnCatalog).Hash -ne $furnCatalogHash) { throw 'Main catalog changed during validation; reconcile it before installing.' }
    foreach($furnPart in (Get-Content (Join-Path $furnDestination 'furniture-models.json') -Raw | ConvertFrom-Json).models) {
        foreach($furnSuffix in '.asset','.asset.meta','.prefab','.prefab.meta') {
            $furnRelative='Assets/Resources/Building/'+$furnPart.slug+$furnSuffix
            Copy-Item -LiteralPath (Join-Path $furnProject $furnRelative) -Destination (Join-Path $furnRoot $furnRelative) -Force
        }
        $furnThumb='Assets/Resources/Building/UI/Thumbnails/'+$furnPart.content_id.Replace('.','_')+'.png'
        foreach($furnSuffix in '','.meta') { Copy-Item -LiteralPath (Join-Path $furnProject ($furnThumb+$furnSuffix)) -Destination (Join-Path $furnRoot ($furnThumb+$furnSuffix)) -Force }
        $furnMeta='Assets/Models/Buildings/Furniture/'+$furnPart.file+'.meta'
        Copy-Item -LiteralPath (Join-Path $furnProject $furnMeta) -Destination (Join-Path $furnRoot $furnMeta) -Force
    }
    $furnFolderMeta='Assets/Models/Buildings/Furniture.meta'
    Copy-Item -LiteralPath (Join-Path $furnProject $furnFolderMeta) -Destination (Join-Path $furnRoot $furnFolderMeta) -Force
    Copy-Item -LiteralPath (Join-Path $furnProject 'Assets/Resources/Building/PrototypeCatalog.asset') -Destination $furnCatalog -Force
}
foreach($furnPath in $furnHashes.Keys) { if((Get-FileHash -LiteralPath $furnPath).Hash -ne $furnHashes[$furnPath]) { throw "Unrelated source changed during validation: $furnPath" } }
Write-Output ('Furniture validated'+$(if($Install){' and installed'}else{''})+'; saved scenes, wood material, texture and shader retained.')

