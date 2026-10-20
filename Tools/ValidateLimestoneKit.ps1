param([switch]$PrepareOnly,[switch]$Install,[switch]$Extension)
$ErrorActionPreference = 'Stop'
$limestoneRoot = Split-Path -Parent $PSScriptRoot
Push-Location $limestoneRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace limestone-kit
    if ($PrepareOnly) { return }
    $limestoneProject = Join-Path $limestoneRoot '.utmp/limestone-kit/unity-project'
    $limestoneVersion = ((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $limestoneCases = @()
    if ($Extension) { $limestoneCases += ,@('LimestoneKitExtensionValidation.InstallAndRunBatch','limestone-extension-unity.log','LIMESTONE EXTENSION PASS:') }
    $limestoneBaseMethod = if ($Extension) { 'LimestoneKitValidation.RunInstalledBatch' } else { 'LimestoneKitValidation.InstallAndRunBatch' }
    $limestoneCases += ,@($limestoneBaseMethod,'limestone-unity.log','LIMESTONE PASS:')
    $limestoneCases += ,@('StoneKitValidation.RunInstalledBatch','limestone-stone-regression.log','STONE KIT PASS:')
    foreach ($limestoneCase in $limestoneCases) {
        $limestoneLog = Join-Path $limestoneRoot ('.utmp/limestone-kit/' + $limestoneCase[1])
        $limestoneArgs = @('-batchmode','-force-d3d11','-projectPath',('"' + $limestoneProject + '"'),
            '-executeMethod',$limestoneCase[0],'-logFile',('"' + $limestoneLog + '"'))
        $limestoneProcess = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$limestoneVersion/Editor/Unity.exe" -ArgumentList $limestoneArgs -WindowStyle Hidden -PassThru
        Write-Output "Limestone validation PID $($limestoneProcess.Id): $($limestoneCase[0])"
        while (-not $limestoneProcess.WaitForExit(1000)) { }
        if ($limestoneProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $limestoneLog -SimpleMatch $limestoneCase[2] -Quiet)) {
            throw "Limestone validation failed; inspect $limestoneLog"
        }
    }
    if ($Install) {
        $limestoneInstallArgs = @('Tools/InstallLimestoneKit.py')
        if ($Extension) { $limestoneInstallArgs += '--extension' }
        & 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' @limestoneInstallArgs
        if ($LASTEXITCODE -ne 0) { throw 'Validated limestone asset copy failed.' }
    }
} finally { Pop-Location }
