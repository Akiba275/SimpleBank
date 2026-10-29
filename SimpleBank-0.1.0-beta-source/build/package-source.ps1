param([string]$Root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..')))
$ErrorActionPreference = 'Stop'

$manifest = [IO.File]::ReadAllText((Join-Path $Root 'manifest.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$documentation = [IO.File]::ReadAllText((Join-Path $Root 'build\documentation.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$releaseDirectory = Join-Path $Root 'bin\Release'
[IO.Directory]::CreateDirectory($releaseDirectory) | Out-Null
$stageRoot = Join-Path $releaseDirectory 'source-package-stage'
$sourceRoot = Join-Path $stageRoot ("SimpleBank-{0}-source" -f $manifest.Version)
if (Test-Path -LiteralPath $stageRoot) { Remove-Item -LiteralPath $stageRoot -Recurse -Force }
[IO.Directory]::CreateDirectory($sourceRoot) | Out-Null

foreach ($file in $documentation.SourceFiles) {
  $source = Join-Path $Root $file
  if (!(Test-Path -LiteralPath $source -PathType Leaf)) { throw "Source-package file not found: $file" }
  Copy-Item -LiteralPath $source -Destination $sourceRoot
}
foreach ($directory in $documentation.SourceDirectories) {
  $source = Join-Path $Root $directory
  if (!(Test-Path -LiteralPath $source -PathType Container)) { throw "Source-package directory not found: $directory" }
  Copy-Item -LiteralPath $source -Destination (Join-Path $sourceRoot $directory) -Recurse
}

$sourceRootPrefix = [IO.Path]::GetFullPath($sourceRoot).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
$excludedDirectoryNames = @($documentation.SourceExcludeDirectories)
$generatedDirectories = @(Get-ChildItem -LiteralPath $sourceRoot -Recurse -Directory | Where-Object { $excludedDirectoryNames -contains $_.Name } | Sort-Object { $_.FullName.Length } -Descending)
foreach ($generatedDirectory in $generatedDirectories) {
  $resolved = [IO.Path]::GetFullPath($generatedDirectory.FullName)
  if (!$resolved.StartsWith($sourceRootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to remove a generated directory outside the source staging root: $resolved"
  }
  if (Test-Path -LiteralPath $resolved) { Remove-Item -LiteralPath $resolved -Recurse -Force }
}

$zipPath = Join-Path $releaseDirectory ("SimpleBank-{0}-source.zip" -f $manifest.Version)
if (Test-Path -LiteralPath $zipPath) { Remove-Item -LiteralPath $zipPath -Force }
Compress-Archive -LiteralPath $sourceRoot -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $stageRoot -Recurse -Force
Write-Output "PACKAGED: $zipPath"
