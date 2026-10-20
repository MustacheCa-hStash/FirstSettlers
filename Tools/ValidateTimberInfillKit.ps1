param([switch]$Install)
$ErrorActionPreference = 'Stop'
$timberRoot = Split-Path -Parent $PSScriptRoot
Push-Location $timberRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace limestone-kit
    $timberProject = Join-Path $timberRoot '.utmp/limestone-kit/unity-project'
    $timberVersion = ((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $timberCases = @(
        ,@('TimberInfillKitValidation.InstallAndRunBatch','timber-infill-unity.log','TIMBER INFILL PASS:')
        ,@('LimestoneKitExtensionValidation.RunInstalledBatch','timber-l04-l09-regression.log','LIMESTONE EXTENSION PASS:')
        ,@('LimestoneKitValidation.RunInstalledBatch','timber-l01-l03-regression.log','LIMESTONE PASS:')
        ,@('StoneKitValidation.RunInstalledBatch','timber-stone-regression.log','STONE KIT PASS:')
        ,@('ModularWoodValidation.RunInstalledBatch','timber-modular-regression.log','MODULAR WOOD PASS:')
    )
    foreach ($timberCase in $timberCases) {
        $timberLog = Join-Path $timberRoot ('.utmp/limestone-kit/' + $timberCase[1])
        $timberArgs = @('-batchmode','-force-d3d11','-projectPath',('"' + $timberProject + '"'),'-executeMethod',$timberCase[0],'-logFile',('"' + $timberLog + '"'))
        $timberProcess = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$timberVersion/Editor/Unity.exe" -ArgumentList $timberArgs -WindowStyle Hidden -PassThru
        Write-Output "Timber validation PID $($timberProcess.Id): $($timberCase[0])"
        while (-not $timberProcess.WaitForExit(1000)) { }
        if ($timberProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $timberLog -SimpleMatch $timberCase[2] -Quiet)) { throw "Timber validation failed; inspect $timberLog" }
    }
    if ($Install) {
        & 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallTimberInfillKit.py
        if ($LASTEXITCODE -ne 0) { throw 'Validated timber asset copy failed.' }
    }
} finally { Pop-Location }
