param([string]$UnityExe = '', [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ($project -match '(^|[\\/])[^\\/]*~([\\/]|$)') {
    throw 'Build from a standalone workspace: run Tools/Create-TendonWorkspace.ps1 -Destination <new folder outside the repository>, then run Tools/Build-Tendon.ps1 there. Unity 6000.5 failed to import script assets in the nested package project during validation.'
}
if (!$UnityExe) {
    $UnityExe = @("$env:USERPROFILE/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe", "$env:ProgramFiles/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$UnityExe) { throw 'Unity 6000.5.9f1 was not found. Pass -UnityExe.' }
if (!(Test-Path -LiteralPath (Join-Path $project 'Assets/ThirdParty/FPSHand/fp_male_hand.fbx'))) { throw 'Run Import-FPSArm.ps1 first.' }
if (!(Test-Path -LiteralPath (Join-Path $project 'Assets/ThirdParty/ESP32Runtime/VibrationControllerClient.cs'))) { throw 'Run Import-ESP32.ps1 first.' }
New-Item -ItemType Directory -Force -Path (Join-Path $project 'Logs') | Out-Null
$log = Join-Path $project ('Logs/TendonBuild-' + (Get-Date -Format yyyyMMdd-HHmmss) + '.log')
$method = if ($ValidateOnly) { 'TendonHand.Editor.TendonValidation.PrepareAndRun' } else { 'TendonHand.Editor.TendonBuild.Build' }
$arguments = '-batchmode -quit -force-d3d11 -job-worker-count 4 -buildTarget Win64 -projectPath "' + $project + '" -executeMethod ' + $method + ' -logFile "' + $log + '"'
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru
Write-Output ('UNITY_LOG: ' + $log)
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity failed ($($process.ExitCode)). See $log" }
Write-Output 'TENDON_BUILD_OR_VALIDATION_COMPLETE'
