param([string]$GamePath = $env:STARDEW_VALLEY_PATH)
$ErrorActionPreference = 'Stop'
$projectRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
if ([string]::IsNullOrWhiteSpace($env:DOTNET_CLI_HOME)) { $env:DOTNET_CLI_HOME = Join-Path $projectRoot 'NuGet\dotnet-cli-home' }
if ([string]::IsNullOrWhiteSpace($env:NUGET_PACKAGES)) { $env:NUGET_PACKAGES = Join-Path $projectRoot 'NuGet\packages' }
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = '0'
& (Join-Path $projectRoot 'scripts\validate.ps1') -Root $projectRoot
$project = Join-Path $projectRoot 'SimpleBank.csproj'
$testProject = Join-Path $projectRoot 'src\SimpleBank.Core.Tests.csproj'
$gameHarnessProject = Join-Path $projectRoot 'src\SimpleBank.GameHarness.csproj'
$packageOptional = { & (Join-Path $projectRoot 'build\package-optional.ps1') -Root $projectRoot }
$packageRelease = { & (Join-Path $projectRoot 'build\package-release.ps1') -Root $projectRoot }
$dotnetCommand = Get-Command dotnet -ErrorAction SilentlyContinue
$dotnetPath = if ($dotnetCommand) { $dotnetCommand.Source } else { $null }
if (!$dotnetPath) {
  $localDotnetCandidates = @(
    (Join-Path $projectRoot '.dotnet\dotnet.exe'),
    (Join-Path $projectRoot '..\.dotnet\dotnet.exe')
  )
  foreach ($candidate in $localDotnetCandidates) {
    if (Test-Path -LiteralPath $candidate) { $dotnetPath = [IO.Path]::GetFullPath($candidate); break }
  }
}
if ($dotnetPath) {
  $sdks = @(& $dotnetPath --list-sdks)
  if ($LASTEXITCODE -eq 0 -and $sdks.Count -gt 0) {
    & $dotnetPath build $testProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Core test build failed.' }
    $testDll = Join-Path $projectRoot 'src\bin\Release\net6.0\SimpleBank.Core.Tests.dll'
    if (!(Test-Path -LiteralPath $testDll)) { throw "Core test assembly not found at $testDll." }
    & $dotnetPath --roll-forward Major $testDll
    if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
    & $dotnetPath build $gameHarnessProject --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'GameHarness build failed.' }
    $gameHarnessDll = Join-Path $projectRoot 'src\bin\GameHarness\Release\net6.0\SimpleBank.GameHarness.dll'
    if (!(Test-Path -LiteralPath $gameHarnessDll)) { throw "GameHarness assembly not found at $gameHarnessDll." }
    & $dotnetPath --roll-forward Major $gameHarnessDll
    if ($LASTEXITCODE -ne 0) { throw 'GameHarness tests failed.' }
    & $packageOptional
    if ([string]::IsNullOrWhiteSpace($GamePath) -or !(Test-Path -LiteralPath (Join-Path $GamePath 'Stardew Valley.dll'))) {
      Write-Warning 'Stardew Valley.dll was not found. Core and GameHarness tests passed; the real SMAPI DLL build was skipped.'
      exit 0
    }
    $smapi = Join-Path $GamePath 'StardewModdingAPI.dll'
    if (!(Test-Path -LiteralPath $smapi)) { throw "StardewModdingAPI.dll not found in $GamePath. Install SMAPI into that game folder first." }
    & $dotnetPath build $project --configuration Release "-p:GamePath=$GamePath" -p:EnableModDeploy=false
    if ($LASTEXITCODE -ne 0) { throw 'dotnet build failed.' }
    & $packageRelease
    exit 0
  }
}
$msbuildPath = $null
$msbuildCommand = Get-Command msbuild -ErrorAction SilentlyContinue
if ($msbuildCommand) { $msbuildPath = $msbuildCommand.Source }
$programFilesX86 = [Environment]::GetFolderPath('ProgramFilesX86')
$vswhere = Join-Path $programFilesX86 'Microsoft Visual Studio\Installer\vswhere.exe'
if (!$msbuildPath -and (Test-Path -LiteralPath $vswhere)) {
  $msbuildPath = & $vswhere -latest -products '*' -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
}
if (!$msbuildPath) {
  $visualStudioRoots = @(
    (Join-Path ([Environment]::GetFolderPath('ProgramFiles')) 'Microsoft Visual Studio\2022'),
    (Join-Path $programFilesX86 'Microsoft Visual Studio\2022')
  ) | Where-Object { Test-Path -LiteralPath $_ }
  foreach ($root in $visualStudioRoots) {
    $msbuildPath = Get-ChildItem -LiteralPath $root -Recurse -Filter MSBuild.exe -File -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName
    if ($msbuildPath) { break }
  }
}
if (!$msbuildPath) { throw 'No .NET SDK or MSBuild installation was found. Install the .NET 6 SDK, then reopen PowerShell.' }
& $msbuildPath $testProject /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'Core test build failed.' }
$testExe = Join-Path $projectRoot 'src\bin\Release\net6.0\SimpleBank.Core.Tests.exe'
$testDll = Join-Path $projectRoot 'src\bin\Release\net6.0\SimpleBank.Core.Tests.dll'
if ($dotnetPath -and (Test-Path -LiteralPath $testDll)) {
  & $dotnetPath --roll-forward Major $testDll
} else {
  if (!(Test-Path -LiteralPath $testExe)) { throw "Core test executable not found at $testExe." }
  & $testExe
}
if ($LASTEXITCODE -ne 0) { throw 'Core tests failed.' }
& $msbuildPath $gameHarnessProject /restore /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'GameHarness build failed.' }
$gameHarnessExe = Join-Path $projectRoot 'src\bin\GameHarness\Release\net6.0\SimpleBank.GameHarness.exe'
$gameHarnessDll = Join-Path $projectRoot 'src\bin\GameHarness\Release\net6.0\SimpleBank.GameHarness.dll'
if ($dotnetPath -and (Test-Path -LiteralPath $gameHarnessDll)) {
  & $dotnetPath --roll-forward Major $gameHarnessDll
} else {
  if (!(Test-Path -LiteralPath $gameHarnessExe)) { throw "GameHarness executable not found at $gameHarnessExe." }
  & $gameHarnessExe
}
if ($LASTEXITCODE -ne 0) { throw 'GameHarness tests failed.' }
& $packageOptional
if ([string]::IsNullOrWhiteSpace($GamePath) -or !(Test-Path -LiteralPath (Join-Path $GamePath 'Stardew Valley.dll'))) {
  Write-Warning 'Stardew Valley.dll was not found. Core and GameHarness tests passed; the real SMAPI DLL build was skipped.'
  exit 0
}
$smapi = Join-Path $GamePath 'StardewModdingAPI.dll'
if (!(Test-Path -LiteralPath $smapi)) { throw "StardewModdingAPI.dll not found in $GamePath. Install SMAPI into that game folder first." }
& $msbuildPath $project /restore /p:GamePath=$GamePath /p:EnableModDeploy=false /p:Configuration=Release
if ($LASTEXITCODE -ne 0) { throw 'SMAPI build failed.' }
& $packageRelease
