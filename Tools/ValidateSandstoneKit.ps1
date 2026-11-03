param([switch]$Install)
$ErrorActionPreference = 'Stop'
$sandstoneRoot = Split-Path -Parent $PSScriptRoot
Push-Location $sandstoneRoot
try {
    & Tools/ValidateBuildingPrototype.ps1 -PrepareOnly -UiAssets -Workspace sandstone-kit
    $sandstoneProject = Join-Path $sandstoneRoot '.utmp/sandstone-kit/unity-project'
    $sandstoneVersion = ((Get-Content ProjectSettings/ProjectVersion.txt -First 1) -split ': ')[1]
    $sandstoneCases = @(
        @{Method='SandstoneKitValidation.InstallAndRunBatch';Name='validation';Marker='SANDSTONE PASS:'}
        @{Method='StoneKitValidation.RunInstalledBatch';Name='stone-regression';Marker='STONE KIT PASS:'}
        @{Method='ModularWoodValidation.RunInstalledBatch';Name='wood-regression';Marker='MODULAR WOOD PASS:'}
        @{Method='BuildInteractionValidation.RunBatch';Name='interaction-regression';Marker='BUILD INTERACTION PASS:'}
    )
    foreach ($sandstoneCase in $sandstoneCases) {
        $sandstoneLog = Join-Path $sandstoneRoot ('.utmp/sandstone-kit/' + $sandstoneCase.Name + '.log')
        $sandstoneArgs = @('-batchmode','-force-d3d11','-projectPath',('"' + $sandstoneProject + '"'),'-executeMethod',$sandstoneCase.Method,'-logFile',('"' + $sandstoneLog + '"'))
        $sandstoneProcess = Start-Process -FilePath "C:/Program Files/Unity/Hub/Editor/$sandstoneVersion/Editor/Unity.exe" -ArgumentList $sandstoneArgs -WindowStyle Hidden -PassThru
        Write-Output "Sandstone validation PID $($sandstoneProcess.Id): $($sandstoneCase.Method)"
        while (-not $sandstoneProcess.WaitForExit(1000)) { }
        if ($sandstoneProcess.ExitCode -ne 0 -or -not (Select-String -LiteralPath $sandstoneLog -SimpleMatch $sandstoneCase.Marker -Quiet)) { throw "Sandstone validation failed; inspect $sandstoneLog" }
    }
    if ($Install) {
        & 'C:/Program Files/Blender Foundation/Blender 5.0/5.0/python/bin/python.exe' Tools/InstallSandstoneKit.py
        if ($LASTEXITCODE -ne 0) { throw 'Validated sandstone installation failed.' }
    }
}
finally { Pop-Location }
