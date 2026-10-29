# Simple Bank — Beta 0.1.0

## 中文说明

### 模组介绍

Simple Bank 是面向《星露谷物语》1.6 的银行模组。在村长家使用 ATM，即可管理活期存款、定期存款和贷款。模组提供简体中文与英文文本。

### 下载文件

- SimpleBank-0.1.0-beta.zip：主模组及必需的 CP 内容包，解压到 Mods 目录。
- SimpleBank-MailFramework-0.1.0-beta.zip：可选的一次性欢迎信，需要主模组和 MFM 1.20.0 或更高版本。
- SimpleBank-0.1.0-beta-source.zip：供开发者使用的源码包，不能直接作为模组安装。
- SHA256SUMS.txt：上述三个 ZIP 的 SHA-256 校验和。

### 主要功能

- 活期存款按日复利计息，不足 1g 的利息保留并继续累积。
- 提供 7、14、28、56、112 天定期存款，可从钱包或活期账户转入；提前支取按已完成天数和活期利率结算。
- 提供 7、28、112 天贷款，额度依据历史收益等因素计算；贷款按未偿本金计息。
- 支持共享与独立多人银行账户，交易由主机校验。
- 支持鼠标、键盘和手柄；可通过 Generic Mod Config Menu（GMCM）调整按键和银行参数。
- 可选安装村长 Lewis 的一次性欢迎信。

### 运行要求与安装

需要 Stardew Valley 1.6、SMAPI 4.1.0 或更高版本，以及 Content Patcher 2.9.0 或更高版本。

1. 下载主包 SimpleBank-0.1.0-beta.zip，解压到游戏的 Mods 目录。

2. 保留主包内的两个文件夹：Mods/SimpleBank/SimpleBank 和 Mods/SimpleBank/[CP] Simple Bank Map。

3. 可选安装 Generic Mod Config Menu，以便在游戏内修改设置。

4. 如需欢迎信，可选安装 Mail Framework Mod 1.20.0 或更高版本，以及单独的 SimpleBank-MailFramework-0.1.0-beta.zip。银行业务和逾期通知不依赖 MFM。

5. 通过 SMAPI 启动游戏。所有联机玩家应安装相同版本的主模组及随包提供的 Content Patcher 内容包。

### 开始使用

ATM 位于村长家墙面上的 (14,8)–(15,9) 格。站在相邻位置，面向 ATM，按游戏交互键打开菜单。通过“概览”“活期”“定期”“贷款”四个页面管理账户。

键盘默认使用 Tab 切换页面，Esc 或 E 关闭菜单。手柄默认使用左右肩键切换页面，A 确认，B 返回。

### 贷款规则：借款前请阅读

贷款逾期后默认有 7 天宽限期。之后的托收会依次使用超过配置保留额的活期余额、钱包中超过 1,000g 的资金，以及后续正向收入的 25%。这些条款会在借款前展示，并锁定在各份合同中。托收不会产生负钱包，也不会没收物品、建筑或未到期定存。

逾期利息继续按未偿本金、锁定利率和逾期倍率计算，直至达到合同约定的逾期利息上限。违约状态不免除债务。存在未结清定期或贷款时，不能在共享与独立钱包之间切换。银行交易不会增加游戏历史总收益，也不会伪造成就进度。

### Beta 状态与兼容性

测试前请备份存档。这是首次受支持的公开 Beta；此前内部使用的 1.x 开发版本不属于受支持的数据导入来源。真实游戏画面、界面缩放、手柄操作感、断线重连和长期联机仍需要更广泛的实机测试。

### 构建与验证

主 DLL 由作者在真实游戏环境构建并提供。发布时仅更新 manifest 中的更新键，DLL 保持不变。本地验证通过 388 项核心测试断言和 86 项游戏层模拟测试断言，两套构建均为零警告、零错误。离线检查不能替代真实游戏中的画面、输入、界面缩放和多人联机测试。

### 源码与文档

当前机器版本为 0.1.0-beta。源码位于 SimpleBank-0.1.0-beta-source/，公开构建入口为该目录下的 build/build.ps1。生成可安装主包需要真实 Stardew Valley 和 SMAPI 引用。玩家说明包含完整银行规则，开发说明介绍架构与构建流程，验证记录列出已检查范围和实机待测项。

[GitHub 源码与问题反馈](https://github.com/Akiba275/SimpleBank) · [GitHub 下载](https://github.com/Akiba275/SimpleBank/releases/tag/0.1.0-beta) · [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/54796)

[玩家说明](SimpleBank-0.1.0-beta-source/README.md) · [开发说明](SimpleBank-0.1.0-beta-source/DEVELOPMENT.md) · [验证记录](SimpleBank-0.1.0-beta-source/VERIFICATION.md)

---

## English

### About the mod

Simple Bank is a banking mod for Stardew Valley 1.6. Visit the ATM inside the Manor House to manage savings, term deposits, and loans. Simplified Chinese and English text are included.

### Downloads

- SimpleBank-0.1.0-beta.zip: main mod and required CP content pack; extract into Mods.
- SimpleBank-MailFramework-0.1.0-beta.zip: optional one-time welcome letter; requires the main mod and MFM 1.20.0 or later.
- SimpleBank-0.1.0-beta-source.zip: source package for developers; not an installable mod.
- SHA256SUMS.txt: SHA-256 checksums for the three ZIP files above.

### Features

- Savings earn daily compound interest, with fractional interest below 1g carried forward.
- Choose 7, 14, 28, 56, or 112-day term deposits. Fund them from your wallet or savings; early withdrawal uses the savings rate for completed days.
- Choose 7, 28, or 112-day loans, with a credit limit based on historical earnings and other configured factors. Interest is calculated on outstanding principal.
- Shared and individual multiplayer bank accounts, with the host validating transactions.
- Mouse, keyboard, and controller support; configurable controls and banking settings through Generic Mod Config Menu (GMCM).
- An optional one-time welcome letter from Mayor Lewis.

### Requirements and installation

Requires Stardew Valley 1.6, SMAPI 4.1.0 or later, and Content Patcher 2.9.0 or later.

1. Download SimpleBank-0.1.0-beta.zip and extract it into your game's Mods directory.

2. Keep both included folders: Mods/SimpleBank/SimpleBank and Mods/SimpleBank/[CP] Simple Bank Map.

3. Optionally install Generic Mod Config Menu for in-game settings.

4. For the optional welcome letter, install Mail Framework Mod 1.20.0 or later and the separate SimpleBank-MailFramework-0.1.0-beta.zip. MFM is not required for banking or overdue notices.

5. Launch the game through SMAPI. All multiplayer participants should install matching versions of the main mod and the included Content Patcher pack.

### Getting started

The ATM is on the Manor House wall at tiles (14,8)–(15,9). Stand beside it, face it, and use the game's interaction button. Manage your account through the Overview, Savings, Term Deposits, and Loans pages.

Keyboard defaults: Tab to change pages, Esc or E to close. Controller defaults: shoulder buttons to change pages, A to confirm, B to return.

### Loan rules: read before borrowing

Overdue loans have a 7-day grace period by default. Collection then uses savings above the configured reserve, wallet funds above 1,000g, and 25% of subsequent positive income. These terms are shown before borrowing and locked into each contract. Collection never creates a negative wallet or confiscates items, buildings, or unmatured term deposits.

Overdue interest continues on outstanding principal at the locked rate and multiplier, up to the contract's overdue-interest cap. Default status does not cancel debt. Outstanding term deposits or loans prevent switching between shared and separate wallets. Bank transactions do not inflate the game's total earnings or achievement progress.

### Beta status and compatibility

Back up your save before testing. This is the initial supported public beta; earlier internal 1.x development builds are not supported import sources. Real-game rendering, UI scaling, controller feel, reconnects, and long multiplayer sessions still need broader testing.

### Build and verification

The main DLL was supplied by the author from a real-game build. Publication only updates manifest update keys; the DLL remains unchanged. Local verification passed 388 core assertions and 86 game-layer harness assertions, with zero build warnings or errors. These offline checks do not replace real-game rendering, input, UI scaling, and multiplayer testing.

### Source and documentation

The current machine-readable version is 0.1.0-beta. Source code is in SimpleBank-0.1.0-beta-source/, with build/build.ps1 as the public build entry point. Building the installable main package requires real Stardew Valley and SMAPI references. The player guide contains the full banking rules, the development guide covers architecture and builds, and the verification record describes the checked scope and remaining real-game tests.

[GitHub source and issue reports](https://github.com/Akiba275/SimpleBank) · [GitHub downloads](https://github.com/Akiba275/SimpleBank/releases/tag/0.1.0-beta) · [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/54796)

[Player guide](SimpleBank-0.1.0-beta-source/README.md) · [Development guide](SimpleBank-0.1.0-beta-source/DEVELOPMENT.md) · [Verification record](SimpleBank-0.1.0-beta-source/VERIFICATION.md)
