param([switch]$SkipPrepare, [switch]$ReceiverChecks)
$ErrorActionPreference = 'Stop'
$caveGraphicsRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipPrepare) { & (Join-Path $PSScriptRoot 'ValidateTerrainSculptingWorld.ps1') -PrepareOnly }
$caveGraphicsProject = Join-Path $caveGraphicsRoot '.utmp/building-prototype/unity-project'
Copy-Item -LiteralPath (Join-Path $caveGraphicsRoot 'Assets/Editor/TerrainCaveValidation.cs'),(Join-Path $caveGraphicsRoot 'Assets/Editor/TerrainCaveGraphicsValidation.cs') -Destination (Join-Path $caveGraphicsProject 'Assets/Editor') -Force
Copy-Item -LiteralPath (Join-Path $caveGraphicsRoot 'Assets/Editor/TerrainCaveLightDebug.shader') -Destination (Join-Path $caveGraphicsProject 'Assets/Editor') -Force
$caveGraphicsScriptRoot = Join-Path $caveGraphicsProject 'Assets/Scripts'
foreach ($caveGraphicsSource in Get-ChildItem -LiteralPath $caveGraphicsScriptRoot -Filter '*.cs' -Recurse) {
    $caveGraphicsRelative = [IO.Path]::GetRelativePath($caveGraphicsScriptRoot, $caveGraphicsSource.FullName)
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $caveGraphicsRoot 'Assets/Scripts') $caveGraphicsRelative))) {
        Remove-Item -LiteralPath $caveGraphicsSource.FullName -Force
        if (Test-Path -LiteralPath ($caveGraphicsSource.FullName + '.meta')) { Remove-Item -LiteralPath ($caveGraphicsSource.FullName + '.meta') -Force }
    }
}
$caveGraphicsVersion = ((Get-Content (Join-Path $caveGraphicsRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$caveGraphicsLog = Join-Path $caveGraphicsRoot '.utmp/terrain-sculpting/cave-graphics.log'
$caveGraphicsMethod = if ($ReceiverChecks) { 'TerrainCaveGraphicsValidation.RunReceiverPlaneBatch' } else { 'TerrainCaveGraphicsValidation.RunBatch' }
if ($ReceiverChecks) { $caveGraphicsLog = Join-Path $caveGraphicsRoot '.utmp/terrain-sculpting/cave-receiver-validation.log' }
$caveGraphicsArguments = @('-batchmode','-force-d3d11','-projectPath',('"' + $caveGraphicsProject + '"'),'-executeMethod',$caveGraphicsMethod,'-terrainSourceRoot',('"' + $caveGraphicsRoot + '"'),'-logFile',('"' + $caveGraphicsLog + '"'))
$caveGraphicsProcess = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$caveGraphicsVersion/Editor/Unity.exe" -ArgumentList $caveGraphicsArguments -WindowStyle Hidden -PassThru
$caveGraphicsProcess.WaitForExit()
if ($caveGraphicsProcess.ExitCode -ne 0) { throw "Procedural cave graphics failed; inspect $caveGraphicsLog" }
if ($ReceiverChecks) {
    if (-not (Select-String -LiteralPath $caveGraphicsLog -Pattern 'CAVE RECEIVER PRECISION:' -Quiet)) { throw 'Unity exited without receiver validation completion.' }
    Select-String -LiteralPath $caveGraphicsLog -Pattern 'CAVE RECEIVER PRECISION:' | ForEach-Object { $_.Line }
    return
}
if (-not (Select-String -LiteralPath $caveGraphicsLog -Pattern 'PROCEDURAL CAVES P4 GRAPHICS COMPLETE:' -Quiet)) { throw 'Unity exited without cave graphics completion.' }
Select-String -LiteralPath $caveGraphicsLog -Pattern 'PROCEDURAL CAVES P4 GRAPHICS COMPLETE:' | ForEach-Object { $_.Line }
