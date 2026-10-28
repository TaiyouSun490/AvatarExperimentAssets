param([string]$UnityExe = '', [switch]$ValidateOnly)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if ($project -match '(^|[\\/])[^\\/]*~([\\/]|$)') { throw 'Use Tools/Create-MarbleWorkspace.ps1 to create a standalone workspace outside this repository, then build there.' }
if (!$UnityExe) {
    $UnityExe = @("$env:USERPROFILE/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe", "$env:ProgramFiles/Unity/Hub/Editor/6000.5.9f1/Editor/Unity.exe") | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
}
if (!$UnityExe) { throw 'Unity 6000.5.9f1 was not found. Pass -UnityExe.' }
if (!(Test-Path -LiteralPath (Join-Path $project 'Assets/ThirdParty/FPSHand/fp_male_hand.fbx'))) { throw 'Run Import-FPSArm.ps1 first.' }
New-Item -ItemType Directory -Force -Path (Join-Path $project 'Logs') | Out-Null
$log = Join-Path $project ('Logs/MarbleBuild-' + (Get-Date -Format yyyyMMdd-HHmmss) + '.log')
$method = if ($ValidateOnly) { 'MarbleHand.Editor.MarbleValidation.PrepareAndRun' } else { 'MarbleHand.Editor.MarbleBuild.Build' }
$arguments = '-batchmode -quit -force-d3d11 -job-worker-count 4 -buildTarget Win64 -projectPath "' + $project + '" -executeMethod ' + $method + ' -logFile "' + $log + '"'
$process = Start-Process -FilePath $UnityExe -ArgumentList $arguments -WorkingDirectory $project -WindowStyle Hidden -PassThru
Write-Output ('UNITY_LOG: ' + $log)
$process.WaitForExit()
if ($process.ExitCode -ne 0) { throw "Unity failed ($($process.ExitCode)). See $log" }
Write-Output 'MARBLE_BUILD_OR_VALIDATION_COMPLETE'
