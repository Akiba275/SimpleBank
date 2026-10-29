# Simple Bank development notes

Player-facing installation and usage are documented in `README.md`. This file contains source-build, architecture, verification, and acceptance details and is intentionally excluded from runtime packages.

## Project layout

- `src/SimpleBank.Core`: deterministic bank domain model and arithmetic with no game dependency.
- `src/SimpleBank`: SMAPI, Stardew Valley, input, menu, save, and multiplayer adapters.
- `assets/atm.png`: hand-authored native 32×32 transparent world sprite, displayed at exact 4× game scale.
- `content/[CP] Simple Bank Map`: required Content Patcher pack which writes only the coordinate-bearing ATM map property and loads or overlays no source map.
- `optional/[MFM] Simple Bank Notice`: independent optional Mail Framework Mod content pack.
- `tests/SimpleBank.Core.Tests`: standalone checked-arithmetic regression runner.
- `scripts/validate.ps1`: JSON, map, input, manifest, localization, and packaging-invariant validation.
- `build`: build and release-packaging scripts.

## Build and test

From PowerShell:

```powershell
.\scripts\validate.ps1
.\build\build.ps1
.\build\build.ps1 -GamePath 'C:\Path\To\Stardew Valley'
.\build\package-source.ps1
```

The build script first runs the core suite and creates the optional MFM ZIP. With a valid game path, it then builds the SMAPI DLL and creates the main ZIP. The project targets `net6.0` for Stardew Valley/SMAPI compatibility; the test runner uses .NET major-version roll-forward so a newer installed runtime can execute it. The script prefers `dotnet` and falls back to Visual Studio MSBuild.

The public SMAPI reference is the game-root `StardewModdingAPI.dll`. `smapi-internal/SMAPI.Toolkit.dll` is not a replacement for that API assembly. The project uses `Pathoschild.Stardew.ModBuildConfig` 4.4.0 for normal game-machine builds.

The custom release scripts intentionally disable ModBuildConfig's default ZIP and produce this shared parent layout:

```text
SimpleBank-1.7.5.zip
└─ SimpleBank/
   ├─ SimpleBank/
   └─ [CP] Simple Bank Map/

SimpleBank-MailFramework-1.7.5.zip
└─ SimpleBank/
   └─ [MFM] Simple Bank Notice/
```

Extracting both archives into `Mods` merges the outer grouping directory; no files overlap between the required and optional archives.

## Safety architecture

- The host is authoritative for saves, settlement, transaction validation, player identity, absolute day, and physical ATM proximity.
- Remote transaction messages are limited to 8 per player per 2-second window; full-state requests are limited to 2 per player per 5-second window. Limiter state is removed on disconnect/title return.
- Personal-wallet snapshots are filtered to the destination player's account. Processed transaction IDs never leave the host because clients do not need them.
- Transaction IDs and message strings are bounded before domain processing. Core code revalidates positive `long` amounts, enabled products, credit, balance, contract, and revision boundaries.
- Deposit rates are capped at 1% daily, loan rates at 10% daily, and overdue multiplication at 10. Aggregate term balances are checked with decimal accumulation and menu summaries saturate defensively instead of throwing.
- State growth is bounded to 100 open term deposits per account, 64 personal accounts, and 256 processed transaction IDs.
- A failed transaction restores only the affected account, wallet, revision, and bounded transaction-ID list. Shared-mode local-host changes use `addUnearnedMoney`; personal-mode host changes and all remote farmhand changes use `FarmerTeam.SetIndividualMoney`, which explicitly resolves the submitting player's synchronized wallet. Rollback always uses that explicit team API and preserves personal/shared lifetime-income fields. Daily settlement retains a whole-state rollback because it can modify every account.
- The runtime code has no HTTP/socket/listener/process/registry/P/Invoke/unsafe/Harmony/reflection-injection path.

## Performance notes

ATM drawing caches the cloned config, sorted product indices, account snapshot, and sorted term list by bank/config revision and wallet counters. The keypad controls are not constructed or drawn on the normal Overview page, and its information panel expands to the full workspace width with separate account-status, active-balance, term-summary, and loan-summary lines. Personal-account synchronization is targeted rather than broadcasting the full farm bank state, and ordinary transaction rollback no longer clones unrelated accounts.

`DebugLogging` is a local, disabled-by-default GMCM/config option. All diagnostic-only output goes through `ModDiagnostics` and uses SMAPI's Debug level with a `debug:` prefix. Callers guard dynamic message construction with `diagnostics.Enabled`; core diagnostics use injected enable/log delegates so `SimpleBank.Core` remains SMAPI-independent. Do not add per-draw or unconditional per-tick logging. Stopwatch sampling, collection aggregation, and other diagnostic-only work must remain behind the same gate. Errors which indicate data loss risk, malformed saves/messages, or a failed safety rollback remain normal warning/error output even when debug mode is off.

Daily catch-up remains proportional to missed days and active contracts by design. The hard account/contract limits keep that work bounded; normal saves are far below those limits.

## Save compatibility

Schema version remains 1. Wallet-mode changes are initiated by the vanilla Mayor's Manor ledger. The host observes `Game1.player.changeWalletTypeTonight`, preflights it before the overnight routine, rechecks an accepted pending request whenever the bank revision changes, and guards the final `FarmerTeam.useSeparateWallets` transition; a rejected request resets the vanilla flag, restores the previous topology, clears the transient mismatch marker, and shows an in-game explanation. Shared-to-personal moves the complete account to the host multiplayer ID, including every term deposit and its loan. Personal-to-shared rejects any open term deposit or unsettled loan and, when eligible, merges active balances only. Legacy personal-mode saves with a stranded shared account are repaired from the account topology without depending on `ModeMismatch`. The source is cleared only after the cloned result validates, so retries are idempotent and failures leave the original state intact. The removed `Notifications` field was never used; older JSON containing it is ignored by deserialization and omitted on the next valid save. Pre-1.4 product-count settings still migrate into per-slot enable flags, and existing contracts retain locked terms and rates.

## Manual in-game acceptance checklist

- Confirm the original Manor House wall remains unchanged across `(14,8)`–`(15,9)`, the ATM matches the restored v1.6.1 native silhouette (`x=5–26`, `y=1–30`) and centered anchor, the floor at `(14,10)`–`(15,10)` remains usable, and player/NPC sprites draw in front of the unit without path obstruction.
- Toggle ATM placement in the main Simple Bank GMCM page, leave and re-enter the Manor House, and confirm both the world sprite/CP marker and host transaction authorization follow the setting; the CP pack must expose no separate config option.
- Confirm all four top tabs retain readable text labels, only the outer menu has a heavy vanilla frame, buttons/tabs use medium borders, informational regions use fine borders, and every product/deposit selector's arrow points in its actual navigation direction.
- Confirm Overview contains only account information and no transaction controls; its full-width layout leaves no keypad gap. Confirm the keypad appears and fits without overlap on Active, Term, and Loan. On Active, confirm MAX enters the wallet balance and the three buttons below the keypad enter all, one-half, or one-quarter of the active balance without immediately executing a withdrawal.
- Confirm Overview reports account status, active balance, term count/total/nearest maturity, and maximum credit/current principal/current interest/loan maturity as four readable lines at both minimum and normal UI sizes.
- Enable debug mode and exercise save/load, map entry, every transaction type (including rejected input and rollback), daily settlement, wallet-mode migration, and a host/farmhand reconnect. Confirm each chain has ordered `debug:` entries. Disable debug mode and confirm routine diagnostics and ATM performance timing stop immediately while real warnings/errors remain visible.
- Create a loan, advance past its maturity day, and confirm the owning personal player receives one localized HUD warning in that play session. Repeat with shared wallets and confirm each connected split-screen/remote player receives the warning after the host snapshot; reconnect a player after maturity and confirm the first synchronized overdue state also warns them. The test must pass without the optional Mail Framework package installed.
- On the Loan page, confirm Repay first opens a second-stage confirmation, Cancel preserves balances, and Confirm applies payment to interest before principal. Repeat with mouse and controller focus.
- Start with the amount at `0g`; confirm one `+1k` press gives `1,000g`, one `-1k` press reverses it, `1g` active/term transactions succeed, and loans reject amounts below or between `1,000g` increments.
- Verify mouse and controller operation for every action; one controller-A press must enter one keypad digit, X must do nothing, and withdrawal must require A while the withdrawal button is focused.
- Verify controller B and keyboard Esc/E only close the ATM and never open the backpack, then rebind every exposed control in GMCM.
- Enter rates with more than four decimal places and confirm downward flooring; independently toggle every term/loan slot and confirm all durations have a minimum of one day.
- Deposit more than 100,000,000g, verify the Active all/half/quarter shortcuts can populate full-range `long` amounts, and confirm withdrawals above the vanilla wallet's remaining capacity fail atomically. Separately confirm Active MAX always enters the current wallet balance for deposits.
- Test single-player, shared-wallet co-op, personal-wallet co-op, reconnect, stale-revision refresh, and host/client attempts both near and far from the ATM. Verify host deposits no longer roll the bank balance back to `0g`; in separate-funds mode verify every non-host transaction updates that player's wallet exactly once; in shared mode keep both ATM menus open and verify either player's deposit appears immediately on both screens.
- Change the vanilla economy from shared to personal while the shared account has active savings, multiple term deposits, and one open loan. Confirm the complete account moves once to the host's stable multiplayer ID, the shared source becomes empty, and reloading cannot duplicate anything.
- While in personal-wallet mode, leave an open term deposit or loan and request shared funds from the Mayor's Manor ledger. Confirm the request is cancelled in-game before sleep, the game remains in personal-wallet mode after sleeping, every personal account remains unchanged, and the bank is still usable. After all terms are closed/matured and loans are settled, repeat the request and confirm only active balances merge into the shared account.
- In personal-wallet co-op, inspect client sync behavior and verify a client cannot see another player's account.
- Test normal transactions near the request limits and verify excess modified-client messages are dropped without settlement/save corruption.
- Test day settlement, skipped-day catch-up, maturity, early withdrawal, repayment, overdue interest, overflow rejection, and verify total-income values, achievements, and progression remain unchanged across deposit/withdraw/loan/repayment operations.
- Install the optional MFM pack and verify Lewis' localized letter arrives once; remove only that inner folder and verify the bank remains functional.
- Test 75%, 100%, 125%, and 150% UI scales and window resizing.

See `VERIFICATION.md` for the exact automated checks and the boundary between workspace verification and required in-game testing.
