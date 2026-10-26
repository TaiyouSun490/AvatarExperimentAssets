param([Parameter(Mandatory=$true)][string]$Destination)
# Uses the shared project exporter. ESP32 assets are optional for the DHT variants.
& (Join-Path $PSScriptRoot 'Create-TendonWorkspace.ps1') -Destination $Destination
