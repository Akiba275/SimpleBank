# Simple Bank

Simple Bank adds a compact wall-mounted ATM and a bank system to Mayor Lewis' house in Stardew Valley 1.6.x.

## Requirements

- SMAPI 4.1 or later.
- Content Patcher 2.9 or later.
- Generic Mod Config Menu is optional.
- Mail Framework Mod 1.20 or later is required only for the optional Lewis notice letter.

## Install

Extract the main ZIP directly into the game's `Mods` folder. The result should be:

```text
Mods/
└─ SimpleBank/                       grouping folder (no manifest.json)
   ├─ SimpleBank/                    main SMAPI mod
   └─ [CP] Simple Bank Map/          required Content Patcher pack
```

For the optional letter, install Mail Framework Mod and then extract the optional Simple Bank Mail Framework ZIP into the same `Mods` folder. Let your archive tool merge the existing outer `SimpleBank` folder. The completed layout is:

```text
Mods/
└─ SimpleBank/
   ├─ SimpleBank/
   ├─ [CP] Simple Bank Map/
   └─ [MFM] Simple Bank Notice/      optional; may be removed independently
```

The three inner folders remain separate SMAPI/content-pack units, so removing the MFM folder disables only the letter and does not affect bank data. When upgrading from 1.2.x or earlier, remove the old top-level `SimpleBank`, `[CP] Simple Bank Map`, and `[MFM] Simple Bank Notice` folders before installing this grouped layout.

All multiplayer participants should install the same main-mod version. The host owns the bank save and daily settlement state.

## Features

- Shared-wallet and personal-wallet accounts, following the farm's current wallet mode.
- Active savings with daily compound interest.
- Configurable term deposits with locked rates and early withdrawal.
- Configurable loans, credit limits, daily simple interest, repayments, and overdue pricing.
- Host-authoritative multiplayer transactions and reconnect synchronization.
- A compact mouse/controller-friendly ATM interface whose on-screen keypad appears only when an amount is needed.
- An optional, localized, one-time letter from Mayor Lewis announcing the ATM.

## Using the ATM

The ATM occupies the `2×2` wall area from `(14,8)` through `(15,9)` in the Manor House. The main mod draws a native 32×32-pixel transparent wall-mounted sprite at exact tile scale over the untouched original wall, so no map tile is erased or recolored. Stand on the open floor below it at about `(14,10)` or `(15,10)`, face either lower tile, and use the normal action button.

- Mouse: click the tabs, selectors, keypad when shown, action buttons, and close button. The wheel changes term/loan products.
- Keyboard: number row or numpad enters an amount, Backspace removes one digit, Delete clears it, Tab changes page, and Esc/E closes the ATM by default. Keyboard keys do not execute financial actions.
- Controller: D-pad/left stick moves focus, A activates only the focused control, B cancels or closes, and shoulder buttons change pages. X has no ATM action, and the conditional on-screen keypad supports all amount entry.

When a term deposit, loan, early withdrawal, or loan repayment is awaiting confirmation, only the focused **Confirm** button submits it. Moving focus to another control and pressing A first cancels the pending transaction, then performs the newly selected control. Repayment confirmation also states that the payment is applied to accrued interest before principal.

GMCM can enable or disable the Manor House ATM, turn debug mode on or off, change the page key and close keys, and configure every active controller binding. The host controls ATM placement in multiplayer, while debug output is a local diagnostic preference. After changing placement, leave and re-enter the Manor House so its map reloads. Without GMCM, placement and debug mode remain editable in the main mod's `config.json`, while controls remain editable in `controls.json`.

The amount field starts at `0g`: pressing `+1k` once produces exactly `1,000g`, and `-1k` changes it by exactly `1,000g` in the other direction. On the Active page, **MAX** enters the current wallet balance for a deposit; **All out**, **1/2 out**, and **1/4 out** enter a withdrawal amount calculated from the current active balance. Fractional results round to the nearest whole gold with exact halves rounded away from zero, and a nonzero balance always produces at least `1g`. Active deposits, active withdrawals, and term deposits accept any positive whole-gold amount. Only loans have a denomination rule: they must be at least `1,000g` and use exact `1,000g` increments.

The Overview page is information-only. Its four separate lines show account availability and ownership, the active balance, the number and total value of open term deposits with the nearest maturity, and maximum loan credit with outstanding principal, interest, and time to maturity. It exposes no transaction actions or keypad; active deposits and withdrawals remain exclusively on the Active page.

Debug mode is off by default. When enabled, Simple Bank writes `debug:` entries to the SMAPI console for lifecycle/configuration, save reads and writes, content/map refresh, ATM authorization, menu navigation and confirmations, wallet mutations and rollback, core transaction validation/results, daily settlement, wallet-mode preflight/migration, and multiplayer request/response/snapshot flow. Routine drawing and ordinary per-frame updates do not log; performance timing, aggregate scans, and diagnostic string construction are skipped while debug mode is off.

When a loan becomes overdue, the main mod shows a localized in-game HUD warning to the player whose personal account owns the loan. In shared-wallet mode, each connected local or remote player receives the shared-account warning through the normal synchronized bank snapshot. A player who reconnects after the due date is warned when that overdue state is first synchronized in the current play session. This notification is built into Simple Bank and does not require the optional Mail Framework package.

## Defaults and limits

Active savings earns 0.01% per day. Term products are 7/14/28/56/112 days at 0.02%/0.03%/0.05%/0.08%/0.12% per day. Loans are 7/28/112 days at 0.25%/0.20%/0.15% per day, with a 1.5 overdue multiplier.

For arithmetic and save safety, deposit rates are limited to 0%–1% per day, loan rates to 0%–10%, and the overdue multiplier to 1–10. GMCM rate fields accept typed text and round downward to four decimal places; each term/loan slot has its own enable checkbox and a minimum duration of one day. Bank amounts use signed 64-bit integers and no longer have the old 100,000,000g transaction cap. Withdrawals and loan payouts into the vanilla wallet are still limited by the game's `int` wallet capacity. Each account may hold at most 100 open term deposits, and a personal-wallet save may retain at most 64 bank accounts.

The credit formula is:

```text
min(AbsoluteCreditCap,
    floor(max(0, BaseCredit + HistoricalTotalEarnings × EarningsRatio) / 1000) × 1000)
```

In shared-wallet mode, Simple Bank uses the game's unearned-money path for the current local player and the synchronized team-wallet API for a remote farmhand. In personal-wallet mode, the host and farmhands both use the explicit individual-wallet API so the correct player's balance is selected. Every path preserves personal and farm-wide historical total-earnings counters, so bank transfers do not trigger total-income achievements or related progression.

## Multiplayer and data safety

Clients submit requests, but the host independently verifies the player identity, current account revision, product, amount, and that the player is standing near the real Manor House ATM. A local host uses the unearned-money path only for a shared wallet; all personal-wallet writes and remote farmhand writes use the synchronized individual-wallet API. A failed wallet write restores the previous wallet and bank state without advancing the revision. Every applied shared-account transaction invalidates the host ATM cache and sends an authoritative targeted snapshot to each connected farmhand. In personal-wallet mode, each client receives only their own bank account; other players' balances and contracts are not broadcast.

Simple Bank does not open internet connections, listen on network ports, run external programs, or read arbitrary files. Multiplayer data travels only through SMAPI's connected-player messaging API.

Save data uses schema version 1. Unsupported or structurally unsafe data is preserved unchanged and transactions are locked. The vanilla wallet mode is changed through the original Mayor's Manor ledger, so the host preflights the pending request, rechecks it whenever bank revision changes before sleep, and also guards the final synchronized wallet-mode field. A personal-to-shared request is cancelled in-game if any account has an open term deposit or an unsettled loan; no term deposit or loan is transferred. If the check passes, only active balances are merged into the shared account, settled loan records are not copied, and the personal source containers are cleared atomically. Shared-to-personal moves the complete shared account—active savings, every term deposit, and its loan—to the host's stable multiplayer ID, then clears the shared source. Legacy saves that recorded personal mode while leaving the old shared account populated are repaired from the durable account shape even if the old mismatch flag is absent. Back up the save before changing wallet mode or manually editing bank data.

## Compatibility notes

The Content Patcher pack only writes the map-level `SimpleBankAtmArea` marker whose value identifies the `2×2` area `(14,8)`–`(15,9)` on `Maps/ManorHouse`; it loads no source map and changes no `Buildings`, `Front`, `AlwaysFront`, or other map tile. The main mod draws the ATM as a world-positioned sprite with its lower edge sorted behind players standing in front. This keeps the current map's own wall art, recolors, and other compatible edits intact. The main mod owns `EnableAtm` and exposes it through its GMCM page; the CP pack has no independent config.

## Changelog

### 1.7.5

- Added disabled-by-default GMCM debug mode with gated end-to-end diagnostics for lifecycle, saves, ATM/content, UI, wallets, core transactions/settlement, wallet-mode migration, and multiplayer synchronization.
- Expanded Overview into separate account-status, active-balance, term count/total/nearest-maturity, and loan credit/principal/interest/maturity lines.
- Added a focused second-stage confirmation for loan repayments, including the actual payment and interest-first allocation rule.
- Removed the obsolete empty Manor House TMX merge and retained only the Content Patcher map-property marker; ATM refresh requests are coalesced and their performance measurements now run only in debug mode.
- Fixed host transactions in personal-wallet mode by writing through the explicit individual-wallet API; failed writes now roll back without masking the original rejection or leaving a wallet-only debit.
- Hardened shared-to-personal migration so active savings, all term deposits, and the loan move exactly once to the host account, including legacy saves whose shared data was stranded without a mismatch marker.
- Personal-to-shared requests with open term deposits or loans are now cancelled before sleep, rechecked after every bank revision, and rolled back at the final wallet field as a last resort with an in-game explanation.
- A migration that advances the bank revision is broadcast even when the following client request is stale, preventing peers from retaining the pre-migration account graph.

### 1.7.4

- Rejected vanilla wallet-mode changes now open an in-game dialogue explaining whether open term deposits or an unsettled loan prevented the switch, while the original request remains cancelled.

### 1.7.3

- Wallet-mode requests now validate the pending vanilla `changeWalletTypeTonight` flag before the overnight wallet routine can run. Rejected personal-to-shared changes are reset in-game, while shared-to-personal changes migrate the bank state at the final vanilla topology change.
- The host applies the first ATM transaction against the post-migration revision, avoiding a stale-revision rejection immediately after a wallet switch.

### 1.7.2

- Changed vanilla personal-to-shared wallet switching to preflight every personal account at the moment the game's Mayor's Manor ledger changes the wallet-mode field. Open term deposits and unsettled loans reject the switch immediately; eligible switches merge active balances only and never transfer term deposits or loans.

### 1.7.1

- Split wallet mutation into the correct local and remote paths: the host now uses `Farmer.addUnearnedMoney`, while remote farmhands use `FarmerTeam.SetIndividualMoney`; both preserve historical earnings and rollback safely.
- Added authoritative bank-state invalidation and targeted peer snapshots so host and farmhand ATM menus immediately observe shared-account changes without accepting stale client state.
- Added atomic, revisioned shared/personal wallet-mode migration for active savings, every term deposit, and unambiguous loan contracts. Unsafe multi-loan or overflow merges are rejected without changing the original state.
- Refined the 32×32 ATM with a wider warm-brown surround, wall-pattern alignment, recessed inner shading, and attached contact shadows while preserving the exact centered v1.6.1 cabinet bounds.

### 1.7.0

- Fixed farmhand deposits, withdrawals, term deposits, loans, and repayments in separate-funds multiplayer by replacing remote `Farmer._money` mutation with Stardew Valley's synchronized `FarmerTeam.GetIndividualMoney`/`SetIndividualMoney` API while preserving all lifetime-income counters.
- Removed Simple Bank's player-to-player account-transfer UI, messages, request operation, and domain method; Stardew Valley's own separate-funds transfer feature remains available outside the ATM.
- Restored the ATM's exact v1.6.1 silhouette and placement (`x=5–26`, `y=1–30`) while retaining the newer low-contrast Manor House walnut palette, muted sage display, and attached right/bottom contact shadow.

### 1.6.4

- Stopped trying to cover the Manor House's existing wall fixture, since that fixture can render on a separate in-game map layer. The original fixture and ATM now occupy adjacent, non-overlapping native-pixel regions.
- Moved the ATM away from the upper-left placement, narrowed it to a compact wall terminal beside the fixture, and retained only an attached right/bottom contact shadow.

### 1.6.3

- Reworked the ATM's wall attachment and lighting: the original wall fixture is now fully covered, the detached dark silhouette is gone, upper-left highlights are subdued, and a narrow right/bottom contact shadow sits directly against the machine instead of leaving a transparent gap.
- Reduced the sprite to 387 opaque native pixels and matched its walnut values more closely to the surrounding Manor House paneling, while retaining the muted sage display and a readable wall-mounted silhouette.

### 1.6.2

- Changed pending confirmations so controller A and mouse clicks only submit through the focused **Confirm** button. Activating any other focused control now cancels the pending transaction first, then performs that control's normal action.
- Reauthored the native 32×32 ATM as a narrower walnut wall cabinet using consolidated Manor House wall/trim colors, a muted sage display sampled from nearby furniture, and restrained brass details for better environmental integration.

### 1.6.1

- Fixed the ATM sprite coordinate mode so `(14,8)` is interpreted as a Manor House world position instead of an off-target screen position, and added a bounded retry while the Content Patcher map marker finishes loading.
- Updated the shared-account Overview explanation and Active-page amount shortcuts.
- Changed Active-page **MAX** to use the wallet balance, and added **All out**, **1/2 out**, and **1/4 out** amount shortcuts based on active savings with tested whole-gold rounding.

### 1.6.0

- Replaced the screenshot-derived opaque wall composite with a hand-authored native 32×32 transparent ATM sprite; Content Patcher now marks the interaction area without replacing any Manor House wall tile.
- Anchored the sprite directly at `(14,8)` with 4× nearest pixel scale and a player-safe world depth, eliminating screenshot-grid offset, interpolation blur, baked lighting colors, and player obstruction.
- Simplified menu framing: the vanilla outer frame remains heavy, interactive tabs/buttons use medium borders, and informational areas use one-pixel borders or no additional frame.
- Made the amount keypad conditional and reflowed the content area, so Overview uses the full width while Active, Term, and Loan pages expose numeric input without overlap.
- Removed active deposit/withdraw actions from Overview; those operations now exist only on Active.

### 1.5.0

- Rebuilt the ATM map asset as a fully opaque composite using the unobstructed original Manor House wall pixels supplied from an in-game screenshot, preventing the replaced wall tiles from becoming transparent.
- Moved ATM enablement from the CP pack into the Simple Bank GMCM/config, backed by a Content Patcher custom token and host-side transaction enforcement.
- Corrected the term/loan selector arrow geometry and removed the duplicate arrow glyph so each control points unambiguously in its actual direction.
- Refined the ATM menu with vanilla parchment texture boxes, text-labeled tabs, a continuous balance strip, restrained sage/amber states, and simpler section framing.
- Kept the overview keypad, overview deposit/withdraw actions, and existing page-dependent MAX behavior unchanged pending a separate behavior decision.

### 1.4.1

- Replaced the freestanding ATM art with a shallow wall-mounted walnut-and-brass unit styled to match the Manor House, with no legs or floor base and eight transparent pixel rows beneath it.
- Moved the ATM from `AlwaysFront` to `Buildings`, so players and NPCs render in front of the machine instead of being obscured by it.

### 1.4.0

- Moved ATM rendering to a non-destructive `AlwaysFront` overlay so transparent pixels reveal, rather than erase, the original Manor House wall.
- Suppressed handled SMAPI bindings to prevent duplicate controller-A keypad input and B/E/Esc input leaking into the inventory; removed the controller-X secondary-action shortcut.
- Switched wallet mutations to Stardew Valley's unearned-money path so bank movement does not trigger total-income achievements or progression.
- Removed the 100,000,000g bank transaction cap, added overflow-safe full-`long` amount entry, and made page-aware MAX values respect bank balances and vanilla wallet capacity.
- Replaced rate sliders with text fields rounded downward to four decimal places, and replaced product-count sliders with independent per-slot checkboxes and one-day minimum durations.
- Added automatic migration from legacy enabled-product counts to the new per-slot flags.

### 1.3.1

- Moved the ATM to the `2×2` Manor House wall area `(14,8)`–`(15,9)` and made both lower tiles interactive.
- Replaced the old narrow sprite with a clearer centered 32×32 ATM and fully transparent surrounding pixels, eliminating the rendered black background.
- Changed the amount field's initial value from `1,000g` to `0g`, so the `+1k` and `-1k` controls now match their visible labels from the first press.
- Added regression coverage proving that active and term transactions accept arbitrary positive integer gold while loans alone require `1,000g` increments.

### 1.3.0

- Grouped the main mod, required CP pack, and optional MFM pack beneath one `Mods/SimpleBank` management folder while preserving separate manifests.
- Added host-side physical ATM authorization and per-peer transaction/sync rate limits.
- Stopped personal-wallet snapshots from exposing other players' bank accounts.
- Added rate, transaction, account, term-count, and aggregate-balance bounds with overflow-safe summaries.
- Cached ATM snapshots/config/product ordering, removed per-frame term sorting, and reduced transaction rollback copies to the affected account.
- Removed unused save/config fields and separated player documentation from development records.

### 1.2.0

- Fixed the visible two-tile ATM overlay, completed GMCM section/tooltips, and standardized the author and IDs as Akiba275.

### 1.1.x

- Moved and resized the ATM, rebuilt its interface and controller controls, added GMCM bindings, and introduced the optional MFM letter.

### 1.0.0

- Initial release.
