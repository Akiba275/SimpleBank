# Simple Bank — Beta 0.1.1

## 中文说明

当前源码版本为 **Beta 0.1.1**，尚未发布。下列文件名用于本版构建；已发布版本请查看下载列表。

本版将定期和贷款的到期时间改为“X 天后到期”，并简化申请确认内容。贷款确认页可选择“查看条款”阅读完整规则。

定期和贷款到期当天会弹出游戏内提示：定期显示到期笔数及已转入活期的本息总额；贷款显示待还本金与利息，并提醒次日逾期。同一次游戏会话中不会重复提醒同一笔业务，支持共享与独立账户，无需 MFM。

### 模组介绍

Simple Bank 是面向《星露谷物语》1.6 的银行模组。在村长家使用 ATM，即可管理活期存款、定期存款和贷款。模组提供简体中文与英文文本。

### 下载文件

- SimpleBank-0.1.1-beta.zip：主模组及必需的 CP 内容包，解压到 Mods 目录。
- SimpleBank-MailFramework-0.1.1-beta.zip：可选的一次性欢迎信，需要主模组和 MFM 1.20.0 或更高版本。
- SimpleBank-0.1.1-beta-source.zip：供开发者使用的源码包，不能直接作为模组安装。
- SHA256SUMS.txt：上述三个 ZIP 的 SHA-256 校验和。

### 主要功能

- 活期存款按日复利计息，不足 1g 的利息保留并继续累积。
- 提供 7、14、28、56、112 天定期存款，可从钱包或活期账户转入；提前支取按已完成天数和活期利率结算。
- 提供 7、28、112 天贷款，额度依据历史收益等因素计算；贷款按未偿本金计息。
- 支持共享与独立多人银行账户。
- 支持鼠标、键盘和手柄；可通过 Generic Mod Config Menu（GMCM）调整按键和银行参数。
- 可选安装村长 Lewis 的一次性欢迎信。

### 运行要求与安装

需要 Stardew Valley 1.6、SMAPI 4.1.0 或更高版本，以及 Content Patcher 2.9.0 或更高版本。

1. 下载主包 SimpleBank-0.1.1-beta.zip，解压到游戏的 Mods 目录。

2. 保留主包内的两个文件夹：Mods/SimpleBank/SimpleBank 和 Mods/SimpleBank/[CP] Simple Bank Map。

3. 可选安装 Generic Mod Config Menu，以便在游戏内修改设置。

4. 如需欢迎信，可选安装 Mail Framework Mod 1.20.0 或更高版本，以及单独的 SimpleBank-MailFramework-0.1.1-beta.zip。银行业务和逾期通知不依赖 MFM。

5. 通过 SMAPI 启动游戏。所有联机玩家应安装相同版本的主模组及随包提供的 Content Patcher 内容包。

### 开始使用

ATM 位于村长家墙面上的 (14,8)–(15,9) 格。站在相邻位置，面向 ATM，按游戏交互键打开菜单。通过“概览”“活期”“定期”“贷款”四个页面管理账户。

键盘默认使用 Tab 切换页面，Esc 或 E 关闭菜单。手柄默认使用左右肩键切换页面，A 确认，B 返回。

### 贷款规则：借款前请阅读

贷款逾期后默认有 7 天宽限期。之后的托收会依次使用超过配置保留额的活期余额、钱包中超过 1,000g 的资金，以及后续正向收入的 25%。借款确认页默认显示摘要，可选择“查看条款”阅读完整规则；这些条款会锁定在各份合同中。托收不会产生负钱包，也不会没收物品、建筑或未到期定存。

逾期利息继续按未偿本金、锁定利率和逾期倍率计算，直至达到合同约定的逾期利息上限。违约状态不免除债务。存在未结清定期或贷款时，不能在共享与独立钱包之间切换。银行交易不会增加游戏历史总收益，也不会伪造成就进度。

### Beta 状态

测试前请备份存档。这是 Beta 版本；真实游戏画面、界面缩放、手柄操作感、断线重连和长期联机仍需要更广泛的实机测试。

### 源码与文档

完整银行规则见玩家说明；源码构建方法和测试范围见开发说明与验证记录。

[GitHub 源码与问题反馈](https://github.com/Akiba275/SimpleBank) · [GitHub 下载](https://github.com/Akiba275/SimpleBank/releases) · [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/54796)

[玩家说明](SimpleBank-0.1.1-beta-source/README.md) · [开发说明](SimpleBank-0.1.1-beta-source/DEVELOPMENT.md) · [验证记录](SimpleBank-0.1.1-beta-source/VERIFICATION.md)

---

## English

The current source version is **Beta 0.1.1**, not yet published. The filenames below apply to this build; see the downloads list for published versions.

This update displays term and loan maturity as “due in X days” and simplifies application confirmations. Select “View terms” on the loan confirmation to read the full rules.

In-game popups now appear on the maturity day. Term deposits show the number matured and the total principal and interest transferred to savings; loans show the outstanding principal and interest and warn that they become overdue tomorrow. Each contract is notified once per game session, with support for shared and individual accounts and no MFM requirement.

### About the mod

Simple Bank is a banking mod for Stardew Valley 1.6. Visit the ATM inside the Manor House to manage savings, term deposits, and loans. Simplified Chinese and English text are included.

### Downloads

- SimpleBank-0.1.1-beta.zip: main mod and required CP content pack; extract into Mods.
- SimpleBank-MailFramework-0.1.1-beta.zip: optional one-time welcome letter; requires the main mod and MFM 1.20.0 or later.
- SimpleBank-0.1.1-beta-source.zip: source package for developers; not an installable mod.
- SHA256SUMS.txt: SHA-256 checksums for the three ZIP files above.

### Features

- Savings earn daily compound interest, with fractional interest below 1g carried forward.
- Choose 7, 14, 28, 56, or 112-day term deposits. Fund them from your wallet or savings; early withdrawal uses the savings rate for completed days.
- Choose 7, 28, or 112-day loans, with a credit limit based on historical earnings and other configured factors. Interest is calculated on outstanding principal.
- Shared and individual multiplayer bank accounts.
- Mouse, keyboard, and controller support; configurable controls and banking settings through Generic Mod Config Menu (GMCM).
- An optional one-time welcome letter from Mayor Lewis.

### Requirements and installation

Requires Stardew Valley 1.6, SMAPI 4.1.0 or later, and Content Patcher 2.9.0 or later.

1. Download SimpleBank-0.1.1-beta.zip and extract it into your game's Mods directory.

2. Keep both included folders: Mods/SimpleBank/SimpleBank and Mods/SimpleBank/[CP] Simple Bank Map.

3. Optionally install Generic Mod Config Menu for in-game settings.

4. For the optional welcome letter, install Mail Framework Mod 1.20.0 or later and the separate SimpleBank-MailFramework-0.1.1-beta.zip. MFM is not required for banking or overdue notices.

5. Launch the game through SMAPI. All multiplayer participants should install matching versions of the main mod and the included Content Patcher pack.

### Getting started

The ATM is on the Manor House wall at tiles (14,8)–(15,9). Stand beside it, face it, and use the game's interaction button. Manage your account through the Overview, Savings, Term Deposits, and Loans pages.

Keyboard defaults: Tab to change pages, Esc or E to close. Controller defaults: shoulder buttons to change pages, A to confirm, B to return.

### Loan rules: read before borrowing

Overdue loans have a 7-day grace period by default. Collection then uses savings above the configured reserve, wallet funds above 1,000g, and 25% of subsequent positive income. The loan confirmation shows a summary, with the full rules available through “View terms”; these terms are locked into each contract. Collection never creates a negative wallet or confiscates items, buildings, or unmatured term deposits.

Overdue interest continues on outstanding principal at the locked rate and multiplier, up to the contract's overdue-interest cap. Default status does not cancel debt. Outstanding term deposits or loans prevent switching between shared and separate wallets. Bank transactions do not inflate the game's total earnings or achievement progress.

### Beta status

Back up your save before testing. This is a beta version. Real-game rendering, UI scaling, controller feel, reconnects, and long multiplayer sessions still need broader testing.

### Source and documentation

See the player guide for the full banking rules, and the development guide and verification record for source build instructions and test coverage.

[GitHub source and issue reports](https://github.com/Akiba275/SimpleBank) · [GitHub downloads](https://github.com/Akiba275/SimpleBank/releases) · [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/54796)

[Player guide](SimpleBank-0.1.1-beta-source/README.md) · [Development guide](SimpleBank-0.1.1-beta-source/DEVELOPMENT.md) · [Verification record](SimpleBank-0.1.1-beta-source/VERIFICATION.md)
