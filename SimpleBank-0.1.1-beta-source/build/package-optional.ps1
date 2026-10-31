param([string]$Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
$ErrorActionPreference = 'Stop'

$manifestPath = Join-Path $Root 'optional\[MFM] Simple Bank Notice\manifest.json'
$modulePath = Split-Path -Parent $manifestPath
if (!(Test-Path -LiteralPath $manifestPath)) { throw "Optional MFM module not found at $modulePath." }

$manifest = [IO.File]::ReadAllText($manifestPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$releaseDirectory = Join-Path $Root 'bin\Release'
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null
$stageRoot = Join-Path $releaseDirectory 'optional-package-stage'
$groupRoot = Join-Path $stageRoot 'SimpleBank'
$targetPath = Join-Path $groupRoot '[MFM] Simple Bank Notice'
if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
[IO.Directory]::CreateDirectory($groupRoot) | Out-Null
Copy-Item -LiteralPath $modulePath -Destination $targetPath -Recurse
$zipPath = Join-Path $releaseDirectory ("SimpleBank-MailFramework-{0}.zip" -f $manifest.Version)
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }

Compress-Archive -LiteralPath $groupRoot -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $stageRoot -Recurse -Force
Write-Output "PACKAGED: $zipPath"
