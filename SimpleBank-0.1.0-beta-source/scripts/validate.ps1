param([string]$Root = (Join-Path $PSScriptRoot '..'))
$ErrorActionPreference = 'Stop'
$jsonFiles = Get-ChildItem -LiteralPath $Root -Recurse -Filter '*.json' -File | Where-Object { $_.FullName -notmatch '[\\/](bin|obj|NuGet)[\\/]' }
foreach ($file in $jsonFiles) {
  try {
    $text = [IO.File]::ReadAllText($file.FullName, [Text.UTF8Encoding]::new($false, $true))
    $text | ConvertFrom-Json | Out-Null
  } catch { throw "Invalid JSON or UTF-8: $($file.FullName): $($_.Exception.Message)" }
}
$defaultI18n = [IO.File]::ReadAllText((Join-Path $Root 'i18n\default.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$zhI18n = [IO.File]::ReadAllText((Join-Path $Root 'i18n\zh.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$defaultKeys = @($defaultI18n.PSObject.Properties.Name | Sort-Object)
$zhKeys = @($zhI18n.PSObject.Properties.Name | Sort-Object)
$keyDifferences = @(Compare-Object $defaultKeys $zhKeys)
if ($keyDifferences.Count -gt 0) { throw 'English and Chinese i18n keys do not match.' }
$atm = Join-Path $Root 'assets\atm.png'
if (!(Test-Path -LiteralPath $atm)) { throw 'Missing ATM PNG.' }
Add-Type -AssemblyName System.Drawing
$atmImage = [Drawing.Bitmap]::FromFile($atm)
try {
  if ($atmImage.Width -ne 32 -or $atmImage.Height -ne 32) { throw 'ATM sprite must be exactly 32x32 pixels (two native tiles wide by two native tiles high).' }
  if (($atmImage.PixelFormat -band [Drawing.Imaging.PixelFormat]::Alpha) -eq 0) { throw 'ATM sprite must have an alpha channel.' }
  $transparentPixels = 0
  $visiblePixels = 0
  $partialAlphaPixels = 0
  $colors = [Collections.Generic.HashSet[int]]::new()
  for ($y = 0; $y -lt $atmImage.Height; $y++) {
    for ($x = 0; $x -lt $atmImage.Width; $x++) {
      $pixel = $atmImage.GetPixel($x, $y)
      if ($pixel.A -eq 0) { $transparentPixels++ }
      elseif ($pixel.A -eq 255) { $visiblePixels++; $colors.Add($pixel.ToArgb()) | Out-Null }
      else { $partialAlphaPixels++ }
    }
  }
  if ($transparentPixels -lt 300 -or $visiblePixels -lt 250) { throw 'ATM sprite must contain both substantial transparent margins and a readable centered machine.' }
  if ($partialAlphaPixels -ne 0) { throw 'ATM sprite must use hard native-pixel alpha edges without interpolation.' }
  if ($colors.Count -lt 10) { throw 'ATM sprite palette is too sparse to retain readable controls and Stardew-style shading.' }
} finally { $atmImage.Dispose() }
$manifest = [IO.File]::ReadAllText((Join-Path $Root 'manifest.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$documentation = [IO.File]::ReadAllText((Join-Path $Root 'build\documentation.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$versionPattern = '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)(?:-[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?(?:\+[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*)?$'
if ($manifest.Version -notmatch $versionPattern) { throw "Main manifest version is not valid semantic versioning: $($manifest.Version)" }
$projectXml = [xml][IO.File]::ReadAllText((Join-Path $Root 'SimpleBank.csproj'), [Text.UTF8Encoding]::new($false, $true))
$projectVersion = [string]($projectXml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ($projectVersion -ne $manifest.Version) { throw "Project and manifest versions differ: $projectVersion vs $($manifest.Version)" }
if ($manifest.Author -ne 'Akiba275' -or $manifest.UniqueID -ne 'Akiba275.SimpleBank') { throw 'Unexpected main author or UniqueID.' }
if (!($manifest.Dependencies | Where-Object { $_.UniqueID -eq 'Pathoschild.ContentPatcher' -and $_.IsRequired -eq $true })) { throw 'Main mod must require Content Patcher.' }
$cp = [IO.File]::ReadAllText((Join-Path $Root 'content\[CP] Simple Bank Map\manifest.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
if ($cp.ContentPackFor.UniqueID -ne 'Pathoschild.ContentPatcher') { throw 'Missing CP dependency.' }
if ($cp.Version -ne $manifest.Version) { throw 'Main and Content Patcher pack versions must match.' }
if ($cp.Author -ne 'Akiba275' -or $cp.UniqueID -ne 'Akiba275.SimpleBank.Map') { throw 'Unexpected Content Patcher author or UniqueID.' }
if (!($cp.Dependencies | Where-Object { $_.UniqueID -eq 'Akiba275.SimpleBank' -and $_.IsRequired -eq $true })) { throw 'Content Patcher pack must require the Akiba275 main mod.' }
$content = [IO.File]::ReadAllText((Join-Path $Root 'content\[CP] Simple Bank Map\content.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
if ($content.Changes[0].Action -ne 'EditMap' -or $content.Changes[0].Target -ne 'Maps/ManorHouse') { throw 'Map-property patch target changed.' }
if ($content.Changes[0].MapProperties.SimpleBankAtmArea -ne '14,8,2,2') { throw 'ATM map-property marker must retain the documented 2x2 coordinates at (14,8)-(15,9).' }
if ($content.Changes[0].PSObject.Properties.Name -contains 'FromFile' -or $content.Changes[0].PSObject.Properties.Name -contains 'ToArea' -or $content.Changes[0].PSObject.Properties.Name -contains 'PatchMode' -or $content.Changes[0].PSObject.Properties.Name -contains 'MapTiles') { throw 'The marker-only CP patch must not load or merge a source map or edit any map tile.' }
if (Test-Path -LiteralPath (Join-Path $Root 'content\[CP] Simple Bank Map\assets\atm-overlay.tmx')) { throw 'The obsolete empty TMX overlay must not be loaded or packaged.' }
if (Test-Path -LiteralPath (Join-Path $Root 'content\[CP] Simple Bank Map\assets\atm-tileset.tsx')) { throw 'Obsolete external ATM TSX should not be packaged.' }
$obsoleteAtmAssets = @('atm.png', 'atm-source.png', 'atm-tilesheet.png', 'atm-tilesheet-v2.png', 'atm-tilesheet-v3.png', 'atm-tilesheet-v4.png', 'atm-tilesheet-v5.png', 'atm-tilesheet-v6.png')
foreach ($name in $obsoleteAtmAssets) {
  if (Test-Path -LiteralPath (Join-Path $Root "content\[CP] Simple Bank Map\assets\$name")) { throw "Obsolete ATM asset should not be packaged: $name" }
}
if ($content.ConfigSchema -or $content.Changes[0].When.'Akiba275.SimpleBank/EnableAtm' -ne 'true') { throw 'ATM enablement must come from the main mod custom token instead of CP config.' }
if (Test-Path -LiteralPath (Join-Path $Root 'content\[CP] Simple Bank Map\config.json')) { throw 'The CP pack must not retain its old independent EnableAtm config.' }
$projectText = [IO.File]::ReadAllText((Join-Path $Root 'SimpleBank.csproj'), [Text.UTF8Encoding]::new($false, $true))
if (!$projectText.Contains('Pathoschild.Stardew.ModBuildConfig')) { throw 'Missing official Stardew ModBuildConfig package reference.' }
if ($projectText.Contains('<Reference Include="StardewModdingAPI"')) { throw 'Manual SMAPI assembly reference should be handled by ModBuildConfig.' }
if (!$projectText.Contains('<ContentPacks Include="content\[CP] Simple Bank Map"')) { throw 'The release build must bundle the Content Patcher pack.' }
if (!$projectText.Contains('<Content Include="assets\**\*"')) { throw 'The main mod must copy the native ATM sprite into its output.' }
if (!$projectText.Contains('<EnableModZip>false</EnableModZip>')) { throw 'The default ModBuildConfig ZIP must stay disabled in favor of the grouped release packager.' }
$menuText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank\AtmMenu.cs'), [Text.UTF8Encoding]::new($false, $true))
if (!$menuText.Contains('protected override void cleanupBeforeExit()')) { throw 'IClickableMenu cleanup override has the wrong access level.' }
if ($menuText.Contains('SpriteText.')) { throw 'The compact ATM menu must not depend on the oversized SpriteText layout.' }
if (!$menuText.Contains('private readonly string[] keypadLabels')) { throw 'The controller-compatible on-screen keypad is missing.' }
if (!$menuText.Contains('private long amount;') -or $menuText.Contains('private long amount = 1000') -or !$menuText.Contains('AdjustAmount(1000)') -or !$menuText.Contains('AdjustAmount(-1000)') -or !$menuText.Contains('long.MaxValue')) { throw 'ATM amount input must start at zero, use exact +/-1k controls, and saturate safely across the full long range.' }
if ($menuText.Contains('return tab <= 1 ? "deposit"') -or $menuText.Contains('return tab <= 1 ? "withdraw"')) { throw 'Overview must not reuse active deposit or withdrawal actions.' }
if (!$menuText.Contains('return tab == 1 ? "deposit"') -or !$menuText.Contains('return tab == 1 ? "withdraw"') -or !$menuText.Contains('if (!RequiresAmountInput()) return;') -or !$menuText.Contains('return tab != 0;')) { throw 'Active-only operations or conditional keypad behavior is missing.' }
if (!$menuText.Contains('DrawOverviewContent')) { throw 'The full-width informational Overview is missing.' }
if (!$menuText.Contains('menu.overview.account-line') -or !$menuText.Contains('menu.overview.active-line') -or !$menuText.Contains('menu.overview.term-line') -or !$menuText.Contains('menu.overview.loan-line') -or !$menuText.Contains('FormatMaturity')) { throw 'Overview must separately show account status, active savings, term totals/nearest maturity, and loan credit/debt/maturity.' }
if (!$menuText.Contains('if (tab == 1)') -or !$menuText.Contains('amount = snapshot.WalletBalance;')) { throw 'Active-page MAX must use the current wallet balance without comparing active savings.' }
if (!$menuText.Contains('ActiveWithdrawAllId') -or !$menuText.Contains('ActiveWithdrawHalfId') -or !$menuText.Contains('ActiveWithdrawQuarterId') -or !$menuText.Contains('BankMath.RoundedWithdrawalFraction')) { throw 'Active-page all, half, and quarter withdrawal amount shortcuts are missing.' }
if (!$menuText.Contains('awaitingConfirmation && id != PrimaryId && id != SecondaryId') -or !$menuText.Contains('CancelConfirmation();')) { throw 'Selecting a different focused control must cancel a pending confirmation before activating that control.' }
if (!$menuText.Contains('action == "term-early" || action == "repay"') -or !$menuText.Contains('menu.confirm.repay')) { throw 'Loan repayments must use the same explicit confirmation state machine as early term withdrawals.' }
if (!$menuText.Contains('atm-ui: tab-reselected') -or !$menuText.Contains('atm-ui: snapshot-refresh') -or !$menuText.Contains('wallet-field-changed')) { throw 'ATM diagnostics must distinguish current-tab reselection and delayed wallet-field snapshot refreshes.' }
$handleConfirmStart = $menuText.IndexOf('internal void HandleConfirm()')
$handleNavigationStart = $menuText.IndexOf('internal void HandleNavigation(', $handleConfirmStart)
if ($handleConfirmStart -lt 0 -or $handleNavigationStart -le $handleConfirmStart -or $menuText.Substring($handleConfirmStart, $handleNavigationStart - $handleConfirmStart).Contains('ConfirmOrExecute();')) { throw 'Controller confirm must activate only the focused control instead of globally confirming a pending transaction.' }
if ($menuText.Contains('key == Keys.Enter') -or $menuText.Contains('key == Keys.Escape') -or $menuText.Contains('key == Keys.Space')) { throw 'Keyboard action shortcuts other than amount entry and the configurable page key are not allowed.' }
if (!$menuText.Contains('farmer.team.GetIndividualMoney(farmer)') -or !$menuText.Contains('ReferenceEquals(farmer, Game1.player)') -or !$menuText.Contains('farmer.addUnearnedMoney(delta)') -or !$menuText.Contains('team.SetIndividualMoney(farmer, balance)') -or $menuText.Contains('Game1.player.team.GetIndividualMoney(farmer)') -or $menuText.Contains('Game1.player.team.SetIndividualMoney(farmer, balance)')) { throw 'Wallet changes must use the local unearned-money path for the current player and the synchronized team API for a remote farmhand.' }
if (!$menuText.Contains('var personalTotal = farmer.totalMoneyEarned') -or !$menuText.Contains('var team = farmer.team') -or !$menuText.Contains('var sharedTotal = team.totalMoneyEarned.Value') -or !$menuText.Contains('farmer.totalMoneyEarned = personalTotal') -or !$menuText.Contains('team.totalMoneyEarned.Value = sharedTotal')) { throw 'Wallet adapters must preserve both personal and shared historical earnings while changing only the synchronized wallet balance.' }
$inputText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank\InputConfig.cs'), [Text.UTF8Encoding]::new($false, $true))
if (!$inputText.Contains('KeybindList KeyboardNextPage') -or !$inputText.Contains('KeybindList KeyboardClose') -or !$inputText.Contains('KeybindList ControllerConfirm') -or !$inputText.Contains('KeybindList ControllerPreviousPage')) { throw 'Configurable keyboard/controller bindings are incomplete.' }
if ($inputText.Contains('ControllerSecondary') -or $inputText.Contains('ControllerX')) { throw 'Controller X must not invoke an unfocused secondary ATM action.' }
if ($inputText.Contains('KeybindList.Parse(')) { throw 'Default and clone keybinds must use typed SButton values so malformed JSON recovery cannot throw while constructing defaults.' }
$modEntryText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank\ModEntry.cs'), [Text.UTF8Encoding]::new($false, $true))
if (!$modEntryText.Contains('internal static bool IsPlayerNearAtm') -or !$modEntryText.Contains('PeerDisconnected') -or !$modEntryText.Contains('HasAtmMarker')) { throw 'ATM marker/proximity authorization or peer limiter cleanup is missing.' }
if (!$modEntryText.Contains('new Point(14, 8)') -or !$modEntryText.Contains('new Point(14, 9), new Point(15, 9)')) { throw 'Code-side ATM origin and lower-row interaction range do not match the 2x2 map patch.' }
if (!$modEntryText.Contains('TemporaryAnimatedSprite') -or !$modEntryText.Contains('AtmAssetName') -or !$modEntryText.Contains('new Rectangle(0, 0, 32, 32)') -or !$modEntryText.Contains('4f') -or !$modEntryText.Contains('removeTemporarySpritesWithIDLocal')) { throw 'Native 32x32 world-sprite rendering or duplicate cleanup is missing.' }
if (!$modEntryText.Contains('AtmMarkerRetryTicks') -or $modEntryText -notmatch '0f,\s+false\)\s*\{\s*id = AtmSpriteId') { throw 'ATM rendering must retry the CP marker briefly and use world coordinates instead of local screen coordinates.' }
if ($modEntryText.Contains('atm-tilesheet-v6') -or $modEntryText.Contains('AlwaysFront')) { throw 'Runtime ATM rendering must not depend on the obsolete baked map composite or player-obscuring layers.' }
if (!$modEntryText.Contains('SuppressActiveKeybinds') -or !$modEntryText.Contains('KeyboardClose') -or $modEntryText.Contains('ControllerSecondary')) { throw 'ATM menu commands must suppress native duplicate input and expose keyboard close without a controller-X shortcut.' }
if (!$modEntryText.Contains('AddTextOption') -or !$modEntryText.Contains('FloorRateToFourDecimalPlaces') -or !$modEntryText.Contains('AddBoolOption')) { throw 'GMCM must use text rates with four-place flooring and boolean options.' }
if (!$modEntryText.Contains('RegisterToken(ModManifest, "EnableAtm"') -or !$modEntryText.Contains('config.atm-enabled.name') -or !$modEntryText.Contains('InvalidateCache("Maps/ManorHouse")')) { throw 'Main-mod GMCM ATM enablement or Content Patcher token refresh is missing.' }
if (!$modEntryText.Contains('config.debug-enabled.name') -or !$modEntryText.Contains('config.DebugLogging') -or !$modEntryText.Contains('new ModDiagnostics') -or !$modEntryText.Contains('diagnostics.Enabled && Context.IsWorldReady')) { throw 'GMCM debug mode or its low-overhead runtime gate is missing.' }
if (!$modEntryText.Contains('manor-map-invalidation-ignored') -or !$modEntryText.Contains('!Context.IsWorldReady && bank == null')) { throw 'Title-screen ManorHouse invalidation must not leave an ATM refresh queued for the next save.' }
if (!$modEntryText.Contains('atm-performance: manor-warp') -or !$modEntryText.Contains('atm-performance: refresh-completed') -or !$modEntryText.Contains('atm-performance: slow-update') -or !$modEntryText.Contains('AtmRefreshCoalescedRequests')) { throw 'Bounded first-entry ATM performance diagnostics or refresh-request coalescing are missing.' }
if (!$modEntryText.Contains('PerScreen<ScreenState>') -or !$modEntryText.Contains('private ScreenState CurrentScreen') -or $modEntryText.Contains('private BankService bank;') -or $modEntryText.Contains('private MultiplayerCoordinator multiplayer;')) { throw 'Split-screen bank and multiplayer session state must be isolated through SMAPI PerScreen state.' }
if (!$modEntryText.Contains('screenId=" + Context.ScreenId') -or !$modEntryText.Contains('daily-settlement: screenId=')) { throw 'Temporary split-screen lifecycle and settlement diagnostics are incomplete.' }
if (!$modEntryText.Contains('BankStateChanged += CheckLoanOverdueNotification') -or !$modEntryText.Contains('NotifiedOverdueLoanIds.Add(notificationKey)') -or !$modEntryText.Contains('Game1.addHUDMessage(new HUDMessage(message, HUDMessage.error_type))') -or !$modEntryText.Contains('loan-notification: overdue-shown') -or $defaultI18n.PSObject.Properties.Name -notcontains 'message.loan-overdue') { throw 'The main mod must provide localized, status-deduplicated overdue/default HUD notifications without relying on the optional mail pack.' }
$depositSectionIndex = $modEntryText.IndexOf('config.deposit.section')
$loanSectionIndex = $modEntryText.IndexOf('config.loan.section')
$creditSectionIndex = $modEntryText.IndexOf('config.credit.section')
if ($depositSectionIndex -lt 0 -or $loanSectionIndex -le $depositSectionIndex -or $creditSectionIndex -le $loanSectionIndex) { throw 'GMCM must visibly separate deposit products, loan products, and loan credit settings in that order.' }
$requiredTooltips = @(
  'config.atm.section.tooltip', 'config.atm-enabled.tooltip',
  'config.debug.section.tooltip', 'config.debug-enabled.tooltip',
  'config.controls.section.tooltip', 'config.keyboard-page.tooltip', 'config.keyboard-close.tooltip', 'config.controller-previous-page.tooltip',
  'config.controller-next-page.tooltip', 'config.controller-confirm.tooltip',
  'config.controller-back.tooltip', 'config.controller-up.tooltip', 'config.controller-down.tooltip',
  'config.controller-left.tooltip', 'config.controller-right.tooltip', 'config.deposit.section.tooltip',
  'config.activity-rate.tooltip', 'config.term-enabled.tooltip', 'config.term-days.tooltip', 'config.term-rate.tooltip',
  'config.loan.section.tooltip', 'config.loan-enabled.tooltip', 'config.loan-days.tooltip', 'config.loan-rate.tooltip',
  'config.overdue-multiplier.tooltip', 'config.collection.section.tooltip', 'config.collection-enabled.tooltip',
  'config.collection-grace.tooltip', 'config.collection-active-setoff.tooltip', 'config.collection-active-reserve.tooltip',
  'config.collection-wallet-reserve.tooltip', 'config.collection-garnishment.tooltip', 'config.collection-interest-cap.tooltip',
  'config.collection-default-days.tooltip', 'config.credit.section.tooltip', 'config.base-credit.tooltip',
  'config.earnings-ratio.tooltip', 'config.credit-cap.tooltip'
)
foreach ($key in $requiredTooltips) {
  $english = [string]$defaultI18n.$key
  $chinese = [string]$zhI18n.$key
  if ([string]::IsNullOrWhiteSpace($english) -or $english -notmatch '[.!?]\s*$') { throw "GMCM English tooltip '$key' must be a complete explanatory sentence." }
  if ([string]::IsNullOrWhiteSpace($chinese) -or $chinese -notmatch '[\u3002\uFF01\uFF1F]\s*$') { throw "GMCM Chinese tooltip '$key' must be a complete explanatory sentence." }
  if (!$modEntryText.Contains('i18n.Get("' + $key + '"')) { throw "GMCM tooltip '$key' is not wired into the menu." }
}
$controls = [IO.File]::ReadAllText((Join-Path $Root 'controls.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
if ($controls.KeyboardClose -ne 'Escape, E' -or $controls.PSObject.Properties.Name -contains 'ControllerSecondary') { throw 'ATM close defaults must be Escape/E and controller X must remain unbound.' }
$expectedDirections = @{
  ControllerUp = 'DPadUp, LeftThumbstickUp'
  ControllerDown = 'DPadDown, LeftThumbstickDown'
  ControllerLeft = 'DPadLeft, LeftThumbstickLeft'
  ControllerRight = 'DPadRight, LeftThumbstickRight'
}
foreach ($binding in $expectedDirections.GetEnumerator()) {
  if ($controls.($binding.Key) -ne $binding.Value) { throw "Invalid SMAPI controller direction binding '$($binding.Key)': expected '$($binding.Value)'." }
  if (!$inputText.Contains("SButton.$($binding.Value.Split(', ')[1])")) { throw "Typed default for '$($binding.Key)' is missing." }
}
$mfmRoot = Join-Path $Root 'optional\[MFM] Simple Bank Notice'
$mfmManifest = [IO.File]::ReadAllText((Join-Path $mfmRoot 'manifest.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$mfmMail = [IO.File]::ReadAllText((Join-Path $mfmRoot 'mail.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$mfmDefault = [IO.File]::ReadAllText((Join-Path $mfmRoot 'i18n\default.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
$mfmZh = [IO.File]::ReadAllText((Join-Path $mfmRoot 'i18n\zh.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
if ($mfmManifest.ContentPackFor.UniqueID -ne 'DIGUS.MailFrameworkMod') { throw 'Optional notice must be a Mail Framework Mod content pack.' }
if ($mfmManifest.Version -ne $manifest.Version) { throw 'Main and optional mail module versions must match.' }
if ($mfmManifest.Author -ne 'Akiba275' -or $mfmManifest.UniqueID -ne 'Akiba275.SimpleBank.Mail') { throw 'Unexpected optional mail author or UniqueID.' }
if (@($mfmMail).Count -ne 1 -or $mfmMail[0].Repeatable -ne $false -or $mfmMail[0].Id -ne 'Akiba275.SimpleBank.Mail.LewisWelcome') { throw 'Optional notice must contain one stable, non-repeatable Lewis letter.' }
if (!($mfmMail[0].HasMods -contains 'Akiba275.SimpleBank')) { throw 'Optional notice must require the main Simple Bank mod at runtime.' }
if ($mfmDefault.PSObject.Properties.Name -notcontains $mfmMail[0].Title -or $mfmDefault.PSObject.Properties.Name -notcontains $mfmMail[0].Text) { throw 'MFM English translations do not contain the configured title/text keys.' }
if (@(Compare-Object @($mfmDefault.PSObject.Properties.Name | Sort-Object) @($mfmZh.PSObject.Properties.Name | Sort-Object)).Count -gt 0) { throw 'MFM English and Chinese i18n keys do not match.' }
$configObject = [IO.File]::ReadAllText((Join-Path $Root 'config.json'), [Text.UTF8Encoding]::new($false, $true)) | ConvertFrom-Json
if ($configObject.EnableAtm -ne $true) { throw 'Main config must own enabled-by-default ATM placement.' }
if ($configObject.PSObject.Properties.Name -notcontains 'DebugLogging' -or $configObject.DebugLogging -ne $false) { throw 'Debug logging must be present and disabled by default.' }
if ($configObject.PSObject.Properties.Name -contains 'EnabledTermProducts' -or $configObject.PSObject.Properties.Name -contains 'EnabledLoanProducts') { throw 'Released config must use per-slot product enablement.' }
if (@($configObject.TermProducts | Where-Object { $_.Days -lt 1 -or $null -eq $_.Enabled }).Count -ne 0 -or @($configObject.LoanProducts | Where-Object { $_.Days -lt 1 -or $null -eq $_.Enabled }).Count -ne 0) { throw 'Every released product slot needs an enable checkbox and a duration of at least one day.' }
$termRates = @($configObject.TermProducts | Select-Object -First 5 | ForEach-Object { [decimal]$_.DailyRatePercent })
$loanRates = @($configObject.LoanProducts | Select-Object -First 3 | ForEach-Object { [decimal]$_.DailyRatePercent })
$expectedTermRates = [decimal[]](0.01, 0.015, 0.02, 0.025, 0.03)
$expectedLoanRates = [decimal[]](0.15, 0.10, 0.06)
$ratesMatch = $termRates.Count -eq $expectedTermRates.Count -and $loanRates.Count -eq $expectedLoanRates.Count
for ($index = 0; $ratesMatch -and $index -lt $expectedTermRates.Count; $index++) {
  $ratesMatch = $termRates[$index] -eq $expectedTermRates[$index]
}
for ($index = 0; $ratesMatch -and $index -lt $expectedLoanRates.Count; $index++) {
  $ratesMatch = $loanRates[$index] -eq $expectedLoanRates[$index]
}
if ([decimal]$configObject.ActivityDailyRatePercent -ne 0.005 -or !$ratesMatch -or [decimal]$configObject.OverdueRateMultiplier -ne 1.5) {
  throw "Released config does not contain the balanced fractional-interest default rates. Actual: activity=$($configObject.ActivityDailyRatePercent); term=$($termRates -join ','); loan=$($loanRates -join ','); overdue=$($configObject.OverdueRateMultiplier)."
}
if ($configObject.EnableOverdueCollection -ne $true -or [int]$configObject.OverdueGraceDays -ne 7 -or $configObject.EnableActiveBalanceSetoff -ne $true -or [long]$configObject.MinimumActiveBalanceReserve -ne 0 -or [long]$configObject.MinimumWalletReserve -ne 1000 -or [decimal]$configObject.IncomeGarnishmentPercent -ne 25 -or [decimal]$configObject.MaximumOverdueInterestPercent -ne 100 -or [int]$configObject.DefaultAfterDays -ne 28) {
  throw 'Released config does not match the schema-3 overdue-collection defaults.'
}
$coreSource = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank.Core\BankModels.cs'), [Text.UTF8Encoding]::new($false, $true))
if ($coreSource.Contains('Notifications')) { throw 'Unused notification save state must not be shipped.' }
if (!$coreSource.Contains('CurrentSchemaVersion = 3') -or !$coreSource.Contains('ActiveInterestRemainder') -or !$coreSource.Contains('InterestRemainder') -or !$coreSource.Contains('GarnishmentRemainder') -or !$coreSource.Contains('CreditRestrictionMode') -or !$coreSource.Contains('source.SchemaVersion = 2') -or !$coreSource.Contains('source.SchemaVersion = 3')) { throw 'Schema 3 collection persistence or explicit 1-to-2-to-3 migration is incomplete.' }
$mathSource = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank.Core\BankMath.cs'), [Text.UTF8Encoding]::new($false, $true))
if (!$mathSource.Contains('RoundedWithdrawalFraction') -or !$mathSource.Contains('MidpointRounding.AwayFromZero') -or !$mathSource.Contains('AccrueDailyInterest') -or !$mathSource.Contains('decimal.Floor(amount)')) { throw 'Whole-gold rounding or fractional-interest carry is missing or inconsistent with bank rounding.' }
$limitsSource = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank.Core\BankLimits.cs'), [Text.UTF8Encoding]::new($false, $true))
if (!$limitsSource.Contains('MaximumTermDepositsPerAccount') -or !$limitsSource.Contains('MaximumPersonalAccounts') -or !$limitsSource.Contains('MaximumDepositDailyRatePercent') -or $limitsSource.Contains('MaximumTransactionAmount')) { throw 'Core safety/growth limits are incomplete or the obsolete 100,000,000g cap remains.' }
$multiplayerText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank\MultiplayerCoordinator.cs'), [Text.UTF8Encoding]::new($false, $true))
if (!$multiplayerText.Contains('RequestLimiter(8, 2000)') -or !$multiplayerText.Contains('RequestLimiter(2, 5000)')) { throw 'Multiplayer transaction or sync rate limits are missing.' }
if (!$multiplayerText.Contains('bank.IsAtmEnabled')) { throw 'Host transaction authorization must honor the main ATM enable setting without cloning the full config.' }
if (!$multiplayerText.Contains('CreateClientState') -or !$multiplayerText.Contains('filtered.PersonalAccounts[playerId]')) { throw 'Personal-account snapshot filtering is missing.' }
if (!$multiplayerText.Contains('sync.State.Revision >= bank.State.Revision') -or !$multiplayerText.Contains('bank.ReplaceState(sync.State)')) { throw 'A client must reject stale bank snapshots before replacing its current state.' }
if (!$multiplayerText.Contains('SharedAccount = source.WalletMode == WalletMode.Shared ? source.SharedAccount.Clone()')) { throw 'Shared-mode snapshots must carry the shared account instead of an empty placeholder.' }
$collectionText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank\OverdueCollectionCoordinator.cs'), [Text.UTF8Encoding]::new($false, $true))
if (!$collectionText.Contains('ReconciliationIntervalTicks = 60') -or !$collectionText.Contains('collectionTargetIds.Count == 0') -or !$collectionText.Contains('current <= binding.Baseline') -or !$collectionText.Contains('CollectIncomeGarnishment') -or !$collectionText.Contains('multiplayer.BroadcastSnapshot()') -or $collectionText.Contains('Harmony')) {
  throw 'The host-only event plus on-demand tick collection coordinator is incomplete.'
}
$bankServiceText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank.Core\BankService.cs'), [Text.UTF8Encoding]::new($false, $true))
$bankTestsText = [IO.File]::ReadAllText((Join-Path $Root 'tests\SimpleBank.Core.Tests\BankTests.cs'), [Text.UTF8Encoding]::new($false, $true))
$diagnosticsText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank\ModDiagnostics.cs'), [Text.UTF8Encoding]::new($false, $true))
$bankConfigText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank.Core\BankConfig.cs'), [Text.UTF8Encoding]::new($false, $true))
$walletText = [IO.File]::ReadAllText((Join-Path $Root 'src\SimpleBank.Core\Wallet.cs'), [Text.UTF8Encoding]::new($false, $true))
if ($bankConfigText.Contains('EnabledTermProducts') -or $bankConfigText.Contains('EnabledLoanProducts') -or $bankConfigText.Contains('MigrateLegacyProductEnablement')) { throw 'Pre-Beta product-count compatibility code must not return.' }
if ($bankServiceText.Contains('MoveSharedAccountToPersonal') -or $bankServiceText.Contains('wallet-mode-repaired')) { throw 'Pre-Beta stranded-account repair code must not return.' }
if ($walletText.Contains('class TestWallet') -or !(Test-Path -LiteralPath (Join-Path $Root 'tests\SimpleBank.Core.Tests\TestWallet.cs'))) { throw 'The test wallet must remain test-only.' }
if (Test-Path -LiteralPath (Join-Path $Root 'src\SimpleBank.Core\GameDate.cs')) { throw 'Unused runtime GameDate code must not return.' }
if (Test-Path -LiteralPath (Join-Path $Root 'src\SimpleBank\AssemblyInfo.cs')) { throw 'Assembly metadata must be generated from the project version.' }
if (!$diagnosticsText.Contains('LogLevel.Debug') -or !$diagnosticsText.Contains('isEnabled') -or !$bankServiceText.Contains('diagnosticsEnabled') -or !$bankTestsText.Contains('TestDiagnosticsGate')) { throw 'The diagnostic gate must cover SMAPI output and the pure core, with a regression test for disabled/enabled behavior.' }
if (!$bankServiceText.Contains('ConfigsEquivalent(config, clone)') -or !$bankTestsText.Contains('an equivalent synchronized config does not advance the config revision')) { throw 'Equivalent host config snapshots must not create false client config revisions.' }
if (!$bankServiceText.Contains('CollectScheduledPayment') -or !$bankServiceText.Contains('CollectIncomeGarnishment') -or !$bankServiceText.Contains('ApplyLoanPayment') -or !$bankServiceText.Contains('CancelDebt') -or !$bankServiceText.Contains('SetCreditRestriction') -or !$bankServiceText.Contains('ClearCreditRestriction') -or !$menuText.Contains('menu.confirm.loan') -or !$menuText.Contains('IsActionAllowedDuringCollection')) {
  throw 'Collection core operations, independent administration, or ATM contract/restriction UI is incomplete.'
}
if (!$modEntryText.Contains('data.ModeMismatch = data.WalletMode != currentMode') -or !$modEntryText.Contains('EnsureWalletModeAndPersist()') -or !$multiplayerText.Contains('EnsureWalletMode()') -or !$bankServiceText.Contains('MigrateWalletMode(WalletMode targetMode, long ownerPlayerId, long expectedRevision)')) { throw 'Wallet-mode changes must be detected, migrated by the host, and persisted before settlement or saving.' }
if (!$bankServiceText.Contains('state.SharedAccount = new Account { Id = "shared" }') -or !$bankServiceText.Contains('state.PersonalAccounts.Clear()') -or !$bankServiceText.Contains('HasUnsettledProducts') -or !$bankServiceText.Contains('MergeActiveSavings(merged, account.ActiveBalance, account.ActiveInterestRemainder)')) { throw 'Wallet-mode migration must reject open products before merging and move active balances plus fractional interest atomically.' }
if (!$bankTestsText.Contains('TestMigrationAndSettlementBalanceAccounting') -or !$bankTestsText.Contains('ActiveBalance == 10000') -or !$bankTestsText.Contains('ActiveInterestRemainder == 0.5m')) { throw 'Migration and next-day fractional interest must remain separately regression-tested for the 10,000g balance.' }
if (!$modEntryText.Contains('useSeparateWallets.fieldChangeEvent += OnWalletModeChanged') -or !$modEntryText.Contains('changeWalletTypeTonight.fieldChangeEvent += OnWalletModeChangeRequested') -or !$modEntryText.Contains('field.Value = false') -or !$modEntryText.Contains('Game1.drawObjectDialogue(message)') -or !$bankServiceText.Contains('CheckWalletModeMigration') -or !$bankServiceText.Contains('ClearModeMismatch')) { throw 'The vanilla wallet-mode request must be preflighted before the overnight routine, reverted atomically, and explained in an in-game dialogue when rejected.' }
if (!$multiplayerText.Contains('BankStateChanged') -or !$multiplayerText.Contains('NotifyBankStateChanged()') -or !$multiplayerText.Contains('SendSnapshotTo(farmer.UniqueMultiplayerID, configSnapshot)')) { throw 'Host/client menus must refresh from authoritative revisions and every farmhand must receive a targeted snapshot.' }
$transferSurface = $menuText + $multiplayerText + $bankServiceText
if ($transferSurface.Contains('TransferPersonalActive') -or $transferSurface.Contains('TryExecuteTransferOrRequest') -or $transferSurface.Contains('TargetPlayerId') -or $transferSurface.Contains('transferMode') -or @($defaultI18n.PSObject.Properties.Name | Where-Object { $_ -match '(^|\.)transfer($|\.)' }).Count -gt 0) { throw 'The redundant Simple Bank player-to-player transfer feature or UI text is still present.' }
$mainPackageText = [IO.File]::ReadAllText((Join-Path $Root 'build\package-release.ps1'), [Text.UTF8Encoding]::new($false, $true))
$optionalPackageText = [IO.File]::ReadAllText((Join-Path $Root 'build\package-optional.ps1'), [Text.UTF8Encoding]::new($false, $true))
if (!$mainPackageText.Contains("'SimpleBank'") -or !$mainPackageText.Contains("'[CP] Simple Bank Map'")) { throw 'Main package must use the shared SimpleBank grouping directory.' }
if (!$optionalPackageText.Contains("'SimpleBank'") -or !$optionalPackageText.Contains("'[MFM] Simple Bank Notice'")) { throw 'Optional package must merge into the shared SimpleBank grouping directory.' }
$documentPaths = @(
  $documentation.Documents.PlayerGuide,
  $documentation.Documents.DevelopmentGuide,
  $documentation.Documents.VerificationRecord
)
foreach ($documentPath in $documentPaths) {
  if ([string]::IsNullOrWhiteSpace($documentPath) -or !(Test-Path -LiteralPath (Join-Path $Root $documentPath) -PathType Leaf)) {
    throw "Declared active document is missing: $documentPath"
  }
}
$archivePath = Join-Path $Root $documentation.Documents.ArchiveDirectory
if (!(Test-Path -LiteralPath $archivePath -PathType Container)) { throw 'Declared documentation archive is missing.' }
$playerReadme = [IO.File]::ReadAllText((Join-Path $Root $documentation.Documents.PlayerGuide), [Text.UTF8Encoding]::new($false, $true))
if ($playerReadme.Contains($documentation.Documents.VerificationRecord) -or $playerReadme.Contains('## Build and test')) { throw 'Player guide contains development-only material.' }
$textExtensions = @('.cs', '.csproj', '.json', '.md', '.ps1', '.tmx', '.tsx')
$obsoleteAuthorToken = 'Open' + 'AI'
$forbiddenAuthorMatches = @(Get-ChildItem -LiteralPath $Root -Recurse -File | Where-Object { $textExtensions -contains $_.Extension -and $_.FullName -notmatch '[\\/](bin|obj)[\\/]' } | Select-String -SimpleMatch $obsoleteAuthorToken)
if ($forbiddenAuthorMatches.Count -gt 0) { throw "Found obsolete author fields: $($forbiddenAuthorMatches[0].Path):$($forbiddenAuthorMatches[0].LineNumber)" }
if (!$menuText.Contains('IconKind.ArrowLeft ? 1 : -1') -or !$menuText.Contains('drawTextureBox(b, Game1.menuTexture') -or !$menuText.Contains('DrawBorder') -or !$menuText.Contains('DrawWorkspacePanels')) { throw 'Correctly oriented arrows, one vanilla outer frame, or tiered menu borders are missing.' }
if ([regex]::Matches($menuText, 'drawTextureBox\(b, Game1\.menuTexture').Count -ne 1) { throw 'Only the outer ATM frame may use the heavy vanilla texture box.' }
Write-Output "PASS: validated $($jsonFiles.Count) JSON files, native world ATM, schema-3 collection defaults/migration, on-demand host collection sampling, localized collection UI/notices, atomic wallet-mode migration, filtered multiplayer snapshots, grouped packaging, and SMAPI build references."
