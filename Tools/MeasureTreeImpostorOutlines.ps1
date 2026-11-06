param([ValidateSet('Bake','Validate','Measure')][string]$Mode = 'Validate')
$ErrorActionPreference = 'Stop'
$outlineRoot = Split-Path -Parent $PSScriptRoot
$outlineVersion = ((Get-Content -LiteralPath "$outlineRoot/ProjectSettings/ProjectVersion.txt" -First 1) -split ': ')[1]
$outlineUnity = "C:/Program Files/Unity/Hub/Editor/$outlineVersion/Editor/Unity.exe"
$outlineOutput = Join-Path $outlineRoot '.utmp/tree-outlines'
New-Item -ItemType Directory -Path $outlineOutput -Force | Out-Null
$outlineMethod = switch ($Mode) {
    'Bake' { 'TreeImpostorOutlineBaker.RunBatch' }
    'Validate' { 'TreeImpostorOutlineInvestigation.RunValidationBatch' }
    'Measure' { 'TreeImpostorOutlineInvestigation.RunPerformanceBatch' }
}
$outlineLog = Join-Path $outlineOutput ($Mode.ToLowerInvariant() + '.log')
$outlineArguments = @('-batchmode','-projectPath',$outlineRoot,'-executeMethod',$outlineMethod,'-logFile',$outlineLog)
$outlineProcess = Start-Process -FilePath $outlineUnity -ArgumentList ($outlineArguments | ForEach-Object { '"' + $_ + '"' }) -WindowStyle Hidden -PassThru -Wait
if ($outlineProcess.ExitCode -ne 0) { throw "Tree outline $Mode failed: $outlineLog" }
Write-Output "Tree outline $Mode passed. Results: $outlineOutput"
