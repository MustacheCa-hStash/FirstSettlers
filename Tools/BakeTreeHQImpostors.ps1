param([switch]$PrepareOnly,[switch]$ValidateOnly,[switch]$InstallOnly,[string]$Python)
$ErrorActionPreference='Stop'
$treeRoot=Split-Path -Parent $PSScriptRoot
$treeProject=Join-Path $treeRoot '.utmp/oak-materials/unity-project'
if(-not $Python){
    $treePython=Join-Path $env:LOCALAPPDATA 'Programs/Python/Python311/python.exe'
    $Python=if(Test-Path -LiteralPath $treePython){$treePython}else{(Get-Command python -ErrorAction Stop).Source}
}
Push-Location $treeRoot
try{
    if(-not $InstallOnly){
        & (Join-Path $PSScriptRoot 'ValidateOakMaterials.ps1') -PrepareOnly | Out-Null
        & $Python (Join-Path $PSScriptRoot 'PrepareTreeHQImpostors.py')
        if($LASTEXITCODE -ne 0){throw 'HQ preparation failed.'}
        if($PrepareOnly){return}
        $treeVersion=((Get-Content 'ProjectSettings/ProjectVersion.txt' -First 1)-split ': ')[1]
        $treeLog=Join-Path $treeRoot ".utmp/oak-materials/tree-hq-$([DateTime]::Now.ToString('yyyyMMdd-HHmmss')).log"
        $treeMethod=if($ValidateOnly){'TreeHQImpostorValidation.ValidateBatch'}else{'TreeHQImpostorBaker.BakeBatch'}
        $treeArgs=@('-batchmode','-projectPath',$treeProject,'-executeMethod',$treeMethod,'-logFile',$treeLog)
        $treeProcess=Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$treeVersion/Editor/Unity.exe" -ArgumentList ($treeArgs|ForEach-Object{'"'+$_+'"'}) -WindowStyle Hidden -PassThru
        Write-Output "HQ Unity process $($treeProcess.Id); log $treeLog"
        $treeTimer=[Diagnostics.Stopwatch]::StartNew()
        do{
            Start-Sleep -Seconds 3
            $treeTail=if(Test-Path -LiteralPath $treeLog){Get-Content -LiteralPath $treeLog -Tail 160}else{@()}
            if($treeTail -match 'ALL TREE HQ BAKES AND VALIDATION PASSED|TREE HQ VALIDATION PASSED'){break}
            if($treeTail -match 'Aborting batchmode due to failure|InvalidOperationException:|error CS[0-9]|Application will terminate with return code 1'){throw "HQ bake/validation failed; inspect $treeLog"}
            if($treeProcess.HasExited){throw "Unity exited without passing validation; inspect $treeLog"}
            if($treeTimer.Elapsed.TotalMinutes -gt 120){throw "HQ bake timed out; inspect $treeLog"}
        }while($true)
        if($ValidateOnly){Write-Output 'All HQ trees validated.';return}
    }
    & $Python (Join-Path $PSScriptRoot 'PrepareTreeHQImpostors.py') --install
    if($LASTEXITCODE -ne 0){throw 'HQ installation verification failed.'}
}finally{Pop-Location}
