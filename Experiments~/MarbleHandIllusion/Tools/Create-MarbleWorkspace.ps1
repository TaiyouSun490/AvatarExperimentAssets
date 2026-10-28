param([Parameter(Mandatory=$true)][string]$Destination)
$ErrorActionPreference = 'Stop'
$sourceProject = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$repository = (Resolve-Path (Join-Path $sourceProject '../..')).Path
$destinationPath = [IO.Path]::GetFullPath($Destination)
if (Test-Path -LiteralPath $destinationPath) { throw 'Destination already exists. Choose a new workspace; existing edits are not overwritten.' }
if ($destinationPath.StartsWith($repository + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a destination outside the package repository.' }
if ($destinationPath -match '(^|[\\/])[^\\/]*~([\\/]|$)') { throw 'Choose a workspace path without folders ending in ~.' }
New-Item -ItemType Directory -Path $destinationPath | Out-Null
foreach ($folder in @('Assets','Packages','ProjectSettings','Tools','Documentation')) {
    Copy-Item -LiteralPath (Join-Path $sourceProject $folder) -Destination $destinationPath -Recurse
}
Copy-Item -LiteralPath (Join-Path $sourceProject 'README.md') -Destination $destinationPath
foreach ($file in Get-ChildItem -LiteralPath $sourceProject -Filter 'Start-*.cmd' -File) {
    Copy-Item -LiteralPath $file.FullName -Destination $destinationPath
}
$manifestPath = Join-Path $destinationPath 'Packages/manifest.json'
$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$manifest.dependencies.'com.taiyousun490.avatar-experiments' = 'file:' + $repository.Replace('\','/')
$manifest | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath $manifestPath -Encoding utf8
Write-Output "MARBLE_WORKSPACE: $destinationPath"
Write-Output 'Open this folder in Unity Hub, or run its Tools/Build-Marble.ps1. Edit this copy; copy intended source changes back into the repository before committing.'
