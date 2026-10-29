param(
  [string]$Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')),
  [string]$DllPath
)
$ErrorActionPreference = 'Stop'

$manifestPath = Join-Path $Root 'manifest.json'
$manifest = [IO.File]::ReadAllText($manifestPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$documentationPath = Join-Path $Root 'build\documentation.json'
$documentation = [IO.File]::ReadAllText($documentationPath, [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$releaseDirectory = Join-Path $Root 'bin\Release'
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null

if ([string]::IsNullOrWhiteSpace($DllPath)) {
  $candidates = @(
    (Join-Path $Root 'bin\Release\net6.0\SimpleBank.dll'),
    (Join-Path $Root 'bin\SimpleBank.dll')
  )
  $DllPath = $candidates | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
}
if ([string]::IsNullOrWhiteSpace($DllPath) -or !(Test-Path -LiteralPath $DllPath -PathType Leaf)) {
  throw 'SimpleBank.dll was not found. Build the SMAPI project before packaging the main release.'
}

$stageRoot = Join-Path $releaseDirectory 'main-package-stage'
$groupRoot = Join-Path $stageRoot 'SimpleBank'
$mainRoot = Join-Path $groupRoot 'SimpleBank'
$contentRoot = Join-Path $groupRoot '[CP] Simple Bank Map'
if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
[IO.Directory]::CreateDirectory($mainRoot) | Out-Null

Copy-Item -LiteralPath $manifestPath -Destination $mainRoot
Copy-Item -LiteralPath $DllPath -Destination (Join-Path $mainRoot 'SimpleBank.dll')
Copy-Item -LiteralPath (Join-Path $Root 'config.json') -Destination $mainRoot
Copy-Item -LiteralPath (Join-Path $Root 'controls.json') -Destination $mainRoot
foreach ($document in $documentation.PlayerPackage) {
  $source = Join-Path $Root $document.Source
  $destination = Join-Path $mainRoot $document.Destination
  if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Player-package document not found: $($document.Source)" }
  Copy-Item -LiteralPath $source -Destination $destination
}
Copy-Item -LiteralPath (Join-Path $Root 'i18n') -Destination $mainRoot -Recurse
Copy-Item -LiteralPath (Join-Path $Root 'assets') -Destination $mainRoot -Recurse
Copy-Item -LiteralPath (Join-Path $Root 'content\[CP] Simple Bank Map') -Destination $contentRoot -Recurse

$zipPath = Join-Path $releaseDirectory ("SimpleBank-{0}.zip" -f $manifest.Version)
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -LiteralPath $groupRoot -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $stageRoot -Recurse -Force
Write-Output "PACKAGED: $zipPath"
