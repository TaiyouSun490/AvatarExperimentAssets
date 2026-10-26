param([Parameter(Mandatory=$true)][string]$Repository)
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$package = Join-Path (Resolve-Path -LiteralPath $Repository).Path 'unity/ESP32-Vibration-Controller-1.0.2.unitypackage'
$expected = '46C7A35DDD513396DE99EF8CEE789CCB844E9554DA9303D9F6BB82A0F4EFE636'
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne $expected) { throw 'Unexpected package version; expected upstream 1.0.2.' }
$destination = Join-Path $project 'Assets/ThirdParty/ESP32Runtime'
if (Test-Path -LiteralPath $destination) { throw 'ESP32Runtime already exists; existing imports are preserved.' }
$temp = Join-Path $project ('Temp/ESP32Package-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
tar -xf $package -C $temp
if ($LASTEXITCODE -ne 0) { throw 'Could not extract Unity package.' }
$sources = @{}
foreach ($item in Get-ChildItem -LiteralPath $temp -Directory) {
    $pathname = Join-Path $item.FullName 'pathname'
    if (!(Test-Path -LiteralPath $pathname)) { continue }
    $name = (Get-Content -LiteralPath $pathname -Raw).Trim()
    if ($name -match '/Runtime/(VibrationProtocol|VibrationControllerClient|WindowsSerialConnection)\.cs$') {
        $sources[[IO.Path]::GetFileName($name)] = $item.FullName
    }
}
if ($sources.Count -ne 3) { throw 'Expected three upstream runtime source files.' }
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach ($name in $sources.Keys) {
    Copy-Item -LiteralPath (Join-Path $sources[$name] 'asset') -Destination (Join-Path $destination $name)
    Copy-Item -LiteralPath (Join-Path $sources[$name] 'asset.meta') -Destination (Join-Path $destination ($name + '.meta'))
}
Write-Output ('Imported local ESP32 runtime: ' + $destination)
