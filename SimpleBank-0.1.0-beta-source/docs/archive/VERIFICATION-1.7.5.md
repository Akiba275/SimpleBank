# Verification record

Date: 2026-08-19

## 1.7.5 change verification

- Reproduced the three reported failure chains against the 1.7.4 source and separated them into core migration, game-wallet mutation, and vanilla wallet-request handling.
- Compiled and ran the checked core suite with Roslyn and `checked` arithmetic: `PASS: 283 assertions`. New coverage includes shared-to-personal transfer of active savings, multiple term deposits, and a loan; missing legacy mismatch markers; destination conflicts; idempotent retry; revision overflow; and the first post-migration transaction.
- Compiled all 1.7.5 runtime sources against the local Stardew Valley 1.6/SMAPI reference set with optimization, deterministic output, checked arithmetic, warnings as errors, and `SMAPI.ModBuildConfig.Analyzer`: 0 errors and 0 warnings. The resulting `SimpleBank.dll` is 130,560 bytes, version `1.7.5.0`, SHA-256 `7164132455DC65CF317BE227D8CF68F182A5F6ABFFBE6E4A21679BE0307FE485`.
- `scripts\validate.ps1` passed for all 12 strict UTF-8 JSON files, 134-key English/Chinese parity, manifests, CP marker, wallet-path invariants, migration markers, grouped packaging rules, and build references.
- Statically verified that a personal-wallet host now uses `FarmerTeam.SetIndividualMoney`, shared-wallet local-host writes retain `Farmer.addUnearnedMoney`, rollback always uses the explicit team API, and neither personal nor farm-wide historical earnings may change.
- Statically verified the dual wallet-mode guard: the host preflights `Game1.player.changeWalletTypeTonight`, rechecks an accepted request after every bank revision, restores a rejected final `useSeparateWallets` change, clears transient mismatch state, cancels the original request, and displays a localized in-game explanation.

## 1.7.4 change verification

- `scripts\validate.ps1` passed after adding preflight on the vanilla `changeWalletTypeTonight` request, final `useSeparateWallets` migration, rollback enforcement, modal in-game rejection dialogue, and post-migration host revision handling.
- The core source and updated migration tests compiled with the installed Roslyn compiler and ran successfully: `PASS: 258 assertions`.
- The core tests now also verify that the first host transaction after shared-to-personal migration reaches the migrated personal account.
- The game-layer DLL was not rebuilt in this environment because the .NET SDK and a Stardew Valley 1.6 reference installation are unavailable; the existing `build\build.ps1 -GamePath ...` path remains the required final game-machine compilation check.

## Passed in this workspace

- Parsed all 12 shipped JSON files as strict UTF-8 JSON and verified English/Chinese key parity for the main mod and optional MFM pack.
- Validated the Akiba275 manifests and dependency chain, exact `Maps/ManorHouse` 2×2 coordinate marker at `(14,8)`–`(15,9)`, a property-only CP patch which loads no source map and changes no visual tile, main-mod GMCM/Content Patcher token enablement, typed controller defaults, and one-time Lewis letter.
- Replaced the screenshot-derived wall composite with a hand-authored native 32×32 RGBA ATM sprite. The v1.7.1 art keeps the exact centered v1.6.1 cabinet bounds at native pixels `x=5–26`, `y=1–30`, widens the warm-brown surround, aligns its base with the lower wall pattern, and adds recessed inner shading plus an attached contact shadow. Pixel verification found 640 opaque machine/shadow pixels, 384 fully transparent wall-revealing pixels, 0 partially transparent/interpolated pixels, and 15 deliberately limited palette colors.
- Verified that the runtime loads the sprite through a namespaced SMAPI asset, draws it at exact 4× scale at the world position `(14,8)`, sorts its lower edge behind players in front, removes duplicate sprites on refresh or warp, retries the CP marker for up to 120 ticks, and never edits `Buildings`, `Front`, or `AlwaysFront` artwork.
- Used no generated UI bitmap or generated shipping sprite; the menu and ATM art are deterministic code/native-pixel assets.
- Compiled and ran the checked-arithmetic core suite: `PASS: 283 assertions`.
- The core suite covers credit rounding, wallet/history isolation, host/farmhand wallet roles, shared-account identity, personal-account isolation, idempotency, stale revisions/snapshots, catch-up settlement, term/loan lifecycle, atomic shared↔personal migration, migration retry idempotency, multi-loan rollback, date boundaries, config migration, malformed state, transaction-ID limits, bounded growth, arbitrary 1g transactions, and operations above the old 100,000,000g cap.
- Compiled every v1.7.5 C# file against Stardew Valley 1.6 and SMAPI reference assemblies with checked arithmetic, optimization, deterministic output, warnings as errors, and the official `SMAPI.ModBuildConfig.Analyzer`: 0 errors and 0 warnings; the resulting 130,560-byte assembly version is `1.7.5.0`.
- Statically verified that host transaction handling checks the SMAPI sender identity, online farmer, authoritative day, Manor House CP marker/proximity, revision, operation allowlist, bounded strings, and core financial rules.
- Verified fixed-window limits of 8 transaction requests per player per 2 seconds and 2 state requests per player per 5 seconds, with limiter cleanup on disconnect/title return.
- Verified that every successful host transaction invalidates the open host ATM cache before acknowledgment, shared mode sends an authoritative targeted snapshot to every non-host peer, and personal mode sends only the destination peer's account; processed transaction IDs stay host-side.
- Verified that ATM drawing caches config/product ordering/snapshot/term sorting by revision and omits all keypad/action controls on normal Overview. Ordinary rollback copies only affected data, and persistent collections have explicit bounds.
- Verified that handled keybinds call `SuppressActiveKeybinds` before changing or closing the menu, controller X has no transaction binding, and keyboard Escape/E share the configurable close-only path with controller B.
- Verified that wallet reads resolve `farmer.team`; a shared-wallet local host is changed through `addUnearnedMoney(delta)`, while a personal-wallet host and every remote farmhand use `FarmerTeam.SetIndividualMoney`. Rollback always uses the explicit team API. Every path restores/checks personal and shared lifetime-income fields and never enters the earned-income `Farmer.Money` setter.
- Verified that wallet-mode changes are detected on load, on the pending vanilla request, after bank revisions while a request is pending, at the final team topology field, before daily settlement, before host transactions, and before saving. Migration operates on a clone, advances revision only after validation, clears its source topology on success, repairs unambiguous legacy stranded shared data, and atomically rejects conflicts, count limits, or checked overflow.
- Verified that rate settings use GMCM text options with four-decimal downward flooring, product slots use independent boolean options, and every duration option has a minimum of one day.
- Verified that the main mod registers `Akiba275.SimpleBank/EnableAtm`, exposes the option in its own GMCM page, invalidates the Manor House map cache after changes, and rejects host transactions while disabled; the CP pack has no independent config file.
- Verified that product/deposit arrow icons use corrected geometry and no longer draw a contradictory text glyph, while every top tab retains its translated text label. Only the outer menu uses the heavy vanilla texture box; buttons/tabs use two-pixel frames and informational panels use one-pixel frames.
- Scanned runtime source for external HTTP/socket/listener/process/registry/P/Invoke/unsafe/Harmony/reflection-injection paths; none are present.
- Removed the unused `Notifications`, `DebugLogging`, `AccountResolver`, unused multiplayer constructor/property/response fields, old audit binaries, old test binaries, old ZIPs, and empty documentation directory.
- Verified that the ATM amount field has no nonzero initializer, its `+1k`/`-1k` controls call exact `+1000`/`-1000` adjustments, digit entry saturates safely at `long.MaxValue`, and Active-page MAX uses only the current wallet balance. Active savings also expose all/half/quarter withdrawal amount shortcuts; midpoint values round away from zero and a nonzero balance produces at least `1g`.
- Checked the Active keypad geometry at menu heights 540, 560, 580, 600, and 620 pixels; the new withdrawal shortcut row retains at least a six-pixel gap above the financial action buttons at every height.
- Verified that Overview has no transaction action mapping or amount controls and expands across the workspace. It separately renders account status, active balance, term count/total/nearest maturity, and credit/principal/interest/loan maturity. The player-to-player transfer UI, localization keys, request operation, target field, and core transfer method are absent.
- Verified that a pending term/loan/early-withdrawal/repayment confirmation can only execute through the focused confirmation button. Activating any tab, selector, keypad key, close button, or other non-confirmation control cancels the pending transaction before performing the selected control; the same policy applies to controller and mouse input.
- Verified that disabled-by-default GMCM debug mode gates detailed lifecycle, save, map/ATM, UI, wallet, core transaction/settlement, wallet-mode, and multiplayer diagnostics. Core regression tests prove that disabled diagnostics emit nothing and can be enabled/disabled at runtime; ATM stopwatch and aggregate diagnostic work are also gated.
- Verified that equivalent synchronized configs no longer create false config revisions, same-tab clicks are labeled as reselection, wallet-only cache refreshes identify the NetField cause, and title-screen ManorHouse invalidation cannot queue work for the next save.
- Executed all release packagers and streamed every ZIP entry. The main and optional archives share one outer `SimpleBank` directory, contain separate inner manifests, have no overlapping files, and include no source, tests, build scripts, cache, symbols, or development records. The main archive contains `SimpleBank/SimpleBank/assets/atm.png`; the CP archive contains no obsolete baked ATM tilesheet.

## Verified install layout

```text
Mods/
└─ SimpleBank/                  outer grouping folder; no manifest
   ├─ SimpleBank/               main code mod
   ├─ [CP] Simple Bank Map/     required content pack
   └─ [MFM] Simple Bank Notice/ optional independent content pack
```

SMAPI added support for organizing mods into subfolders in SMAPI 2.8, so each inner `manifest.json` remains independently discovered. The optional archive can be extracted later into the same `Mods` directory or its inner folder can be removed without altering the main/CP files.

## Not run here

- This workspace has no playable Stardew Valley installation. Live world-sprite alignment/depth over the original wall, Content Patcher custom-token refresh after GMCM changes, controller focus, host/farmhand wallet synchronization, shared-menu refresh, vanilla wallet-mode switching, and MFM delivery still require the checklist in `DEVELOPMENT.md`.
- The release DLL was compiled against official reference assemblies rather than the user's installed game files. Running `build.ps1 -GamePath ...` remains the final environment-specific build check.
- The release DLL is not Authenticode-signed; integrity should be established through the distributed archive checksum or a trusted release host.

## Reproduction on a game machine

```powershell
cd path\to\SimpleBank-source
.\scripts\validate.ps1
.\build\build.ps1 -GamePath 'C:\Path\To\Stardew Valley'
```

The build creates `SimpleBank-1.7.5.zip` and `SimpleBank-MailFramework-1.7.5.zip` under `bin\Release`. Run `build\package-source.ps1` to create the clean source archive.
