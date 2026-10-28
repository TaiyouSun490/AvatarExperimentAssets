param([string]$Source = '')
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
if (!$Source) { throw 'Pass -Source with your locally licensed FPSHand directory containing fp_male_hand.fbx.' }
$Source = (Resolve-Path -LiteralPath $Source).Path
if (!(Test-Path -LiteralPath (Join-Path $Source 'fp_male_hand.fbx'))) { throw 'Choose the locally licensed FPSHand directory containing fp_male_hand.fbx.' }
$destination = Join-Path $project 'Assets/ThirdParty/FPSHand'
if (Test-Path -LiteralPath $destination) { throw 'FPSHand already exists. Existing imports are preserved.' }
New-Item -ItemType Directory -Force -Path (Join-Path $project 'Assets/ThirdParty') | Out-Null
Copy-Item -LiteralPath $Source -Destination $destination -Recurse
Write-Output ('Imported local FPS hand assets: ' + $destination)
