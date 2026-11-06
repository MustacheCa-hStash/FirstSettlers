param([string]$BaselineRevision = '29f7c3a7715dfcc8b5280de171f6122ed0f28747')
$ErrorActionPreference = 'Stop'
$treeRoot = Split-Path -Parent $PSScriptRoot
$treeOutput = Join-Path $treeRoot '.utmp/tree-resident'
$treeVersion = ((Get-Content -LiteralPath "$treeRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
$treeUnity = "C:/Program Files/Unity/Hub/Editor/$treeVersion/Editor/Unity.exe"
$treeAssets = @(
    "$treeRoot/Assets/Editor/TreeResidentBenchmark.cs",
    "$treeRoot/Assets/Editor/TreeResidentBenchmarkBaseline.cs",
    "$treeRoot/Assets/Editor/TreeResidentBenchmarkBaseline.compute"
)
# Never overwrite an existing fixture or its Unity identity.
foreach ($treeFile in $treeAssets) {
    if ((Test-Path -LiteralPath $treeFile) -or (Test-Path -LiteralPath ($treeFile + '.meta'))) {
        throw "Temporary benchmark asset already exists: $treeFile"
    }
}
New-Item -ItemType Directory -Path $treeOutput -Force | Out-Null
$treeOldSource = & git -C $treeRoot show "${BaselineRevision}:Assets/Scripts/FoliageGeneration/DistantTreeGpuBatch.cs"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read baseline tree batch.' }
$treeOldCompute = & git -C $treeRoot show "${BaselineRevision}:Assets/Shaders/DistantTreeCompact.compute"
if ($LASTEXITCODE -ne 0) { throw 'Cannot read baseline compute shader.' }
($treeOldSource -join "`n").Replace('DistantTreeGpuBatch', 'TreeResidentBenchmarkBaseline') |
    Set-Content -LiteralPath "$treeOutput/TreeResidentBenchmarkBaseline.cs"
$treeOldCompute | Set-Content -LiteralPath "$treeOutput/TreeResidentBenchmarkBaseline.compute"
Copy-Item -LiteralPath "$PSScriptRoot/Fixtures/TreeResidentBenchmark.cs" -Destination $treeOutput -Force
& git -C $treeRoot rev-parse $BaselineRevision | Set-Content -LiteralPath "$treeOutput/baseline-revision.txt"
try {
    foreach ($treeFile in $treeAssets) {
        Copy-Item -LiteralPath (Join-Path $treeOutput (Split-Path -Leaf $treeFile)) -Destination $treeFile
    }
    $treeArguments = @('-batchmode', '-projectPath', $treeRoot, '-executeMethod',
        'TreeResidentBenchmark.RunBatch', '-logFile', "$treeOutput/benchmark.log")
    $treeProcess = Start-Process -FilePath $treeUnity -ArgumentList ($treeArguments | ForEach-Object { '"' + $_ + '"' }) -WindowStyle Hidden -PassThru -Wait
    if ($treeProcess.ExitCode -ne 0) { throw "Unity benchmark failed: $treeOutput/benchmark.log" }
    Get-Content -LiteralPath "$treeOutput/benchmark.csv"
}
finally {
    # Only these explicitly owned temporary files are removed; no recursive cleanup.
    foreach ($treeFile in $treeAssets) {
        foreach ($treeOwned in @($treeFile, ($treeFile + '.meta'))) {
            if (Test-Path -LiteralPath $treeOwned) { Remove-Item -LiteralPath $treeOwned }
        }
    }
}
