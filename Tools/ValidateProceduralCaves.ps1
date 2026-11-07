param([switch]$CompileOnly, [switch]$SkipPrepare, [switch]$TravelChecks)
$ErrorActionPreference = 'Stop'
$caveRoot = Split-Path -Parent $PSScriptRoot
if (-not $SkipPrepare) {
    & (Join-Path $PSScriptRoot 'ValidateTerrainSculpting.ps1') -PrepareOnly
}
if ($CompileOnly) { return }
$caveProject = Join-Path $caveRoot '.utmp/terrain-sculpting/unity-project'
$caveScriptRoot = Join-Path $caveProject 'Assets/Scripts'
# Copy-Item does not retire scripts removed since an older validation run.
foreach ($caveSource in Get-ChildItem -LiteralPath $caveScriptRoot -Filter '*.cs' -Recurse) {
    $caveRelative = [IO.Path]::GetRelativePath($caveScriptRoot, $caveSource.FullName)
    if (-not (Test-Path -LiteralPath (Join-Path (Join-Path $caveRoot 'Assets/Scripts') $caveRelative))) {
        Remove-Item -LiteralPath $caveSource.FullName -Force
        if (Test-Path -LiteralPath ($caveSource.FullName + '.meta')) { Remove-Item -LiteralPath ($caveSource.FullName + '.meta') -Force }
    }
}
Copy-Item -LiteralPath (Join-Path $caveRoot 'Assets/Editor/TerrainCaveValidation.cs') -Destination (Join-Path $caveProject 'Assets/Editor') -Force
$caveVersion = ((Get-Content (Join-Path $caveRoot 'ProjectSettings/ProjectVersion.txt') -First 1) -split ': ')[1]
$caveEditor = "C:/Program Files/Unity/Hub/Editor/$caveVersion/Editor/Unity.exe"
$caveLog = Join-Path $caveRoot '.utmp/terrain-sculpting/cave-validation.log'
$caveMethod = if ($TravelChecks) { 'TerrainCaveValidation.RunTravelBatch' } else { 'TerrainCaveValidation.RunBatch' }
if ($TravelChecks) { $caveLog = Join-Path $caveRoot '.utmp/terrain-sculpting/cave-travel-validation.log' }
$caveArguments = @('-batchmode','-nographics','-projectPath',('"' + $caveProject + '"'),'-executeMethod',$caveMethod,'-terrainSourceRoot',('"' + $caveRoot + '"'),'-logFile',('"' + $caveLog + '"'))
$caveProcess = Start-Process -FilePath $caveEditor -ArgumentList $caveArguments -WindowStyle Hidden -PassThru
$caveProcess.WaitForExit()
if ($caveProcess.ExitCode -ne 0) { throw "Procedural cave validation failed; inspect $caveLog" }
if ($TravelChecks) {
    if (-not (Select-String -LiteralPath $caveLog -Pattern 'PROCEDURAL CAVES TRAVEL COMPLETE:' -Quiet)) { throw 'Unity exited without cave travel completion.' }
    Select-String -LiteralPath $caveLog -Pattern 'PROCEDURAL CAVES TRAVEL COMPLETE:' | ForEach-Object { $_.Line }
    return
}
if (-not (Select-String -LiteralPath $caveLog -Pattern 'PROCEDURAL CAVES P1 COMPLETE:' -Quiet)) { throw 'Unity exited without cave validation completion.' }
foreach ($cavePhase in 'P2','P3') { if (-not (Select-String -LiteralPath $caveLog -Pattern "PROCEDURAL CAVES $cavePhase COMPLETE:" -Quiet)) { throw "Unity exited without cave $cavePhase completion." } }
Select-String -LiteralPath $caveLog -Pattern 'PROCEDURAL CAVES .*COMPLETE:' | ForEach-Object { $_.Line }
