param(
    [Parameter(Mandatory=$true)][string]$UnityEditor,
    [Parameter(Mandatory=$true)][string]$ProjectPath,
    [string]$EditorVersion = '6000.0.74f1'
)
$ErrorActionPreference = 'Stop'
$taskRepo = Split-Path $PSScriptRoot -Parent
$taskProject = [IO.Path]::GetFullPath($ProjectPath)
if (Test-Path -LiteralPath $taskProject) { throw 'Choose a new validation directory; existing projects are never overwritten.' }
if (-not (Test-Path -LiteralPath $UnityEditor)) { throw 'Unity Editor executable not found.' }
New-Item -ItemType Directory -Path (Join-Path $taskProject 'Assets'),(Join-Path $taskProject 'Packages'),(Join-Path $taskProject 'ProjectSettings') | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRepo 'Runtime/Perspectives') -Destination (Join-Path $taskProject 'Assets/Perspectives') -Recurse
Copy-Item -LiteralPath (Join-Path $taskRepo 'Editor/Perspectives') -Destination (Join-Path $taskProject 'Assets/PerspectivesEditor') -Recurse
$taskDependencies = @{ dependencies = @{
    'com.unity.ugui'='2.0.0'; 'com.unity.modules.ui'='1.0.0'; 'com.unity.modules.uielements'='1.0.0';
    'com.unity.modules.imgui'='1.0.0'; 'com.unity.modules.jsonserialize'='1.0.0';
    'com.unity.modules.physics'='1.0.0'; 'com.unity.modules.physics2d'='1.0.0';
    'com.unity.modules.audio'='1.0.0'; 'com.unity.modules.animation'='1.0.0'
} }
[IO.File]::WriteAllText((Join-Path $taskProject 'Packages/manifest.json'), ($taskDependencies | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $taskProject 'ProjectSettings/ProjectVersion.txt'), "m_EditorVersion: $EditorVersion")
$taskLog = Join-Path $taskProject 'perspective-validation.log'
$taskArguments = @('-batchmode','-nographics','-projectPath',('"'+$taskProject+'"'),'-executeMethod',
    'AvatarExperiments.Perspectives.Editor.PerspectiveSmokeTests.RunBatch','-logFile',('"'+$taskLog+'"'))
$taskProcess = Start-Process -FilePath $UnityEditor -ArgumentList $taskArguments -WindowStyle Hidden -Wait -PassThru
$taskOutput = [IO.File]::ReadAllText($taskLog)
if ($taskProcess.ExitCode -ne 0 -or -not $taskOutput.Contains('PERSPECTIVES_SMOKE_PASS')) {
    throw "Perspective validation failed. Exit $($taskProcess.ExitCode). Read $taskLog"
}
$taskOutput -split "`n" | Where-Object { $_ -like '*PERSPECTIVES_SMOKE_PASS*' }
Write-Output 'Isolated perspective module only; no XR device, SDK integration or rendering verification.'
