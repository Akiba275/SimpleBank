# Simple Bank 开发说明

本文档描述 `0.1.2-beta` 的实际源码结构、构建入口、安全边界和后续开发约定。

Beta 0.1.2 精简 `message.term-matured` 和 `message.loan-due-today` 的中英文文案：定期只传入到期笔数，贷款不再传入金额。金额与合同诊断保留，定期金额汇总只在调试启用时计算；通知触发、去重、计息、多人权威及 schema 3 均不变。

最终中文提示统一采用“业务＋今日到期＋后续说明”的句式：“X 笔定期存款今日到期，已转入活期。”和“贷款今日到期，请及时还款，避免逾期。”英文同样先说明到期状态，再说明资金去向或还款操作。

## 项目结构

- `src/SimpleBank.Core`：无 SMAPI 依赖的银行领域模型、金额计算、合同、结算、配置和状态校验。
- `src/SimpleBank`：SMAPI 生命周期、存档、多人协调、ATM 菜单、游戏钱包适配和可选集成。
- `tests/SimpleBank.Core.Tests`：核心回归测试及测试专用的 `TestWallet`。
- `tests/SimpleBank.GameHarness`：SMAPI、Stardew Valley 与 XNA 桩，以及分屏/游戏层编译检查。
- `content/[CP] Simple Bank Map`：只写入 ATM 坐标属性的 Content Patcher 包。
- `optional/[MFM] Simple Bank Notice`：独立且可选的欢迎信内容包。
- `scripts/validate.ps1`：JSON、版本、文档、地图、输入、本地化、代码边界和打包不变量检查。
- `build`：构建与发行脚本；`documentation.json` 声明活动文档和源码包内容。

测试辅助类型不得放入 `src/SimpleBank.Core`。发行项目使用 `src/SimpleBank.Core/*.cs` 通配编译，因此测试替身必须保留在 `tests` 中。

## 构建与测试

使用 .NET 10 LTS SDK 编译，主模组与测试项目的目标框架保持 `net6.0`。SDK 是编译工具；目标框架决定产物使用的 .NET 接口。游戏和 SMAPI 当前使用 .NET 6，升级 SDK 不应同时提高模组的目标框架。参见 [SMAPI 开发指南](https://stardewvalleywiki.com/Modding:Modder_Guide/Get_Started#What_does_%22target_NET_6.0%22_mean?) 和 [微软 SDK 版本说明](https://learn.microsoft.com/en-us/dotnet/core/versions/selection)。

本机编译与生成实机测试包必须通过源码附带的 `build/build.ps1` 完成，以持续验证完整入口。在源码根目录运行：

```powershell
.\build\build.ps1 -GamePath 'C:\Path\To\Stardew Valley'
```

脚本兼容 Windows PowerShell 5.1 和 PowerShell 7，会依次执行静态校验、核心测试、GameHarness 测试、真实 SMAPI DLL 构建和主包/可选包打包。`GamePath` 指向装好 SMAPI 的完整游戏目录，也可通过 `STARDEW_VALLEY_PATH` 环境变量提供；保留游戏及 `smapi-internal` 中的依赖文件。没有有效游戏路径时，脚本只完成离线测试与可选包打包，并跳过真实主 DLL 和主安装包；脚本退出码为 0 不能单独证明完整编译成功。

脚本优先使用 `PATH` 中的 `dotnet`，找不到时检查源码目录及其父目录下的 `.dotnet/dotnet.exe`。日志中的 `BUILD SDK` 显示实际选择的 SDK 与路径，应确认使用 .NET 10 LTS。新旧 SDK 可并存；保留 .NET 6 运行时可继续在对应运行时执行测试。首次还原依赖需要访问 NuGet，后续可复用缓存；脚本尊重 `NUGET_PACKAGES`，未设置时使用源码目录下的 `NuGet/packages`。

如需仅运行静态校验或生成源码归档，可分别运行以下辅助脚本；它们不能替代完整构建入口：

```powershell
.\scripts\validate.ps1
.\build\package-source.ps1
```

分析器是构建阶段运行的代码检查工具。使用旧 SDK 时出现 `CS9057` 表示分析器要求的编译器版本高于当前版本，不能视为分析器检查通过。较新 SDK 还可能针对已结束支持的 `net6.0` 可执行测试项目提示 `NETSDK1138`；该生命周期提示不等同于分析器加载失败，也不应通过修改模组目标框架来消除。验证时分别记录编译错误、分析器诊断和框架生命周期提示，并以真实游戏验收确认运行行为。

发行包命名为：

```text
SimpleBank-0.1.2-beta.zip
SimpleBank-MailFramework-0.1.2-beta.zip
SimpleBank-0.1.2-beta-source.zip
```

## 发布渠道

- [GitHub 源码仓库](https://github.com/Akiba275/SimpleBank)与[发行列表](https://github.com/Akiba275/SimpleBank/releases)。
- [Nexus Mods](https://www.nexusmods.com/stardewvalley/mods/54796)。

主模组、CP 地图包与可选 MFM 欢迎信包的 `manifest.json` 均使用以下更新键，并保持版本同步：

```json
"UpdateKeys": [ "Nexus:54796", "GitHub:Akiba275/SimpleBank" ]
```

GitHub 标签使用机器版本号。GitHub 发布主包、可选欢迎信包、源码包及 `SHA256SUMS.txt`；Nexus 发布主包与可选欢迎信包。首页和两站发布说明保持完整中英对应，中文在前、英文在后。

发布前通过 `build/build.ps1` 验证，并使用真实 Stardew Valley/SMAPI 引用构建主包。核对包内版本、更新键和目录布局，生成源码包及校验和。构建产物位于源码目录的 `bin/Release/`，不加入版本控制。

后续代码更新使用新的机器版本和标签，保留已发布标签、ZIP 和校验和；仅修改文档时不替换已发布附件。

## 文档与打包约定

活动文档的角色和路径由 `build/documentation.json` 声明：

- 玩家说明：随主发行包复制。
- 开发说明：只进入源码包。
- 验证记录：只进入源码包。

源码包仅包含清单指定的公开文件和目录，并按清单排除本地辅助文件与构建缓存。

主安装包不携带 `config.json`，避免覆盖更新重置玩家的银行配置。模组入口调用 SMAPI `ReadConfig<BankConfig>()`，由 SMAPI 在文件缺失时生成默认配置，并为已有配置补齐缺失字段；已有无效配置仍按启动校验逻辑恢复默认值。源码及源码包保留 `config.json` 作为默认配置样本，并继续校验其与 `BankConfig` 内置默认值一致。`controls.json` 的打包和读取行为保持不变。

`package-release.ps1`、`package-source.ps1` 和 `validate.ps1` 都读取该清单。重命名或移动活动文档时，应修改清单，而不是在多个脚本中增加新的硬编码路径。

## 运行时架构

```text
ATM/UI 或远程请求
        ↓
MultiplayerCoordinator（身份、距离、频率、revision）
        ↓
BankService（原子交易、合同、日结、钱包模式迁移）
        ↓
IWalletAdapter + BankState
        ↓
SaveRepository（仅主机写入）
```

逾期贷款另有一条主机专用协调链：

```text
SaveLoaded（迁移、绑定、钱包基线；不托收）
        ↓
DayStarted（结息/状态推进 → 活期抵销 → 在线钱包扫款）
        ↓
OverdueCollectionCoordinator（稳定 ID 目标索引与在线钱包绑定）
        ↓
UpdateTicked（仅目标钱包整数余额增加时调用收入代扣）
```

关键约束：

- 主机负责所有状态变更；客户端快照按账户过滤。
- 每个交易带唯一 ID 和期望 revision；重复或陈旧请求不重复入账。
- 交易失败恢复受影响账户、钱包、revision 和交易 ID 列表；日结继续使用全状态回滚。
- 金额计算启用 checked arithmetic；状态校验限制账户数、定期数、消息长度和交易历史增长。
- `BankService` 构造及 `ReplaceState` 先完整校验输入；无效状态直接拒绝，状态替换成功时仍克隆输入。结构迁移由 `BankStateMigrator` 显式处理，服务入口不再追加隐式修补。
- 普通银行交易不得改变 Stardew Valley 的历史总收益字段。
- 托收还款统一经过 `ApplyLoanPayment`，始终先抵利息、再抵本金；计划托收以贷款 ID、游戏日和资金来源建立幂等检查点。
- `Defaulted` 是非终止状态。债务、托收和有上限的逐日逾期利息继续存在，直至 `Settled` 或主机显式 `Cancelled`。
- 运行时不使用网络监听、进程启动、注册表、P/Invoke、unsafe、Harmony 或反射注入。

## 定期资金来源

定期资金来源由当前 `AtmMenu` 实例保存，每次打开默认钱包，切换页面不重置，分屏菜单互不共享该选择。钱包来源发送 `term`，活期来源发送 `term-active`；主机执行身份、ATM 访问、revision 和重复交易检查，按账户模式定位资金，并在成功后先发送过滤快照再发送响应。

`CreateTermDeposit` 与 `CreateTermDepositFromActive` 共用 `PrepareTermDeposit`，统一产品开关、托收限制、存单数上限、定期总额溢出检查和合同字段锁定。活期来源通过 `MutateAccountState` 一次性扣减 `ActiveBalance` 并添加存单，失败恢复银行状态、revision 和交易 ID 历史，不调用钱包读写或回滚接口。没有个人账户时按活期余额不足拒绝，不创建空账户。`ActiveInterestRemainder` 留在原账户，新存单余数为零。

资金来源不持久化；到期和提前支取所得均进入活期账户。

## ATM 到期时间与申请确认

概览、定期详情和贷款详情共用 `FormatMaturity`，以合同到期日减去当前游戏日计算剩余天数；零天显示今天到期，负数显示已逾期天数。贷款详情仅显示一次到期情况，托收和违约提示同样使用剩余天数。此项只改变显示，不修改合同日期、业务规则或 schema。

定期确认压缩为来源/金额与期限/日复利两行。贷款确认使用完整内容区显示金额、期限、日单利和托收摘要；“查看条款”展开当前配置对应的完整条款，按字体测量结果和可用高度分页。条款按钮、翻页与返回仅改变当前菜单的阅读状态，确认按钮仍走原有交易流程；编辑金额或切换产品会取消待确认交易。中英文资源保持相同键集合。

## 到期当天通知

`SettleToDay` 通过 `TransactionResult.TermMaturities` 返回本次成功结算的定期记录，包含稳定玩家 ID（共享账户为 null）、存单 ID、实际结算日及最终取整后的到账金额。记录只在整次日结提交成功后返回；失败回滚、重复交易和同日空结算不产生新记录。补算多日时保留各笔实际结算日，避免将往日到期误报为今天到期。

主机的 `MultiplayerCoordinator` 只缓存当天记录，在过滤后的 `BankSyncMessage` 中按稳定玩家 ID 定向附带；共享记录对共享账户参与者可见，个人记录仅对原账户本人可见。当天晚加入或重连可以重收；客户端只接受已绑定主机的有效快照，主机不应用客户端快照。缓存随协调器会话清理，不写入 `BankState`，schema 保持 3。

`MaturityNotifier` 位于 `PerScreen<ScreenState>` 中，定期按存单 ID、贷款按合同 ID 去重；当天多笔定期合并显示笔数及已转入活期，贷款只显示今日到期与及时还款提醒。贷款仅在当天日结完成、合同仍为 `Current` 且到期日等于当前日时提醒。保存载入、日开始和状态同步后检查，并每 60 次更新补查一次，处理快照先于本屏游戏日期就绪的情况。返回标题只清理当前屏幕的记录和去重状态。金额汇总及诊断字符串均受调试开关控制，提示通过主模组 HUD 发出，不依赖 MFM。

通知不持久化为跨会话历史：主机退出后不保留已结算定期的通知记录；下一游戏日不再发送昨日记录。贷款逾期、托收和违约通知仍走原有逻辑。

## 钱包模式

`ModeMismatch` 是运行时的瞬态安全锁。它在游戏钱包拓扑与银行拓扑暂时不一致时阻止交易，并在切换成功或原版请求回滚后清除。

钱包模式协调的拒绝分支只保存一次最终状态。载入时恢复原版钱包字段可能同步触发拓扑回调；该回调保存带临时锁的状态，外层随后保存解锁后的状态。这两次内容不同的安全保存保留，不能按整个事件的写入次数一并删除。

共享与独立钱包之间的当前迁移规则只移动活期余额及其利息余数。合并多个个人账户时，余数达到整金币的部分进入合并余额，其余部分继续保存。存在未结清定期或贷款时，预检和最终迁移都会拒绝切换。迁移在克隆状态上执行，完成校验后一次性提交。

`BankState` 当前为 schema 3，保存活期、定期和贷款的小数利息余数，以及贷款原始本金、锁定托收条款、代扣余数、阶段日期、日度检查点、解决信息和独立信用限制。`SaveRepository` 支持显式 `1 → 2 → 3` 结构迁移：第一步把利息余数初始化为零，第二步按载入时配置为已有贷款锁定托收条款。未知 schema 或无效结构会保留原数据并锁定写入。

贷款日期固定为：`OverdueStartDay = MaturityDay + 1`、`CollectionStartDay = MaturityDay + GraceDays + 1`、`DefaultDay = CollectionStartDay + DefaultAfterDays`。逾期利息按剩余本金逐日计算，`OverdueInterestCharged` 保存累计精确逾期利息，使已偿还的利息不会重新打开上限空间；配置上限为 `0` 时表示不限额。

迁移和日结共用 `LoanContract.GetOpenStatusForDay` 推导未结清贷款的日期阶段；各自保留终止状态处理及提交流程。该内部方法不增加持久化字段。

信用限制不属于债务解决方式。`CancelDebt`、`SetCreditRestriction` 和 `ClearCreditRestriction` 各自独立，钱包模式迁移时按“永久优先，否则取最晚截止日”合并信用限制。

## 兼容与版本策略

GitHub 发布仓库为 `Akiba275/SimpleBank`，Nexus Mods 页面编号为 `54796`。主模组、CP 地图包和可选 MFM 包统一使用 `Nexus:54796` 与 `GitHub:Akiba275/SimpleBank` 更新键，应同步版本和发布。Release 标签使用机器版本（当前为 `0.1.2-beta`）。发布说明必须区分源码附件与可安装的主包，不得以离线测试程序集代替真实 SMAPI 构建。

机器版本遵循 SemVer。当前版本为 `0.1.2-beta`，玩家显示名称为 Beta 0.1.2。程序集版本由 SDK 从 `SimpleBank.csproj` 生成，不再维护独立的 `AssemblyInfo.cs`。

支持的数据基线为 Beta 0.1.0。未知 schema 和无效状态会保留原数据并锁定写入。

后续若改变持久化结构，应先增加 schema 版本并设计显式迁移；不得加入无期限的隐式兼容分支。

## 性能与诊断

ATM 菜单按银行 revision、配置 revision 和钱包计数缓存快照、排序和布局数据。调试日志由 `DebugLogging` 控制，统一通过 `ModDiagnostics` 输出 `debug:` 前缀；动态字符串、计时和聚合工作也必须置于同一开关之后。

概览和定期页共用排序后的存单缓存。收到银行状态时主动失效，包括版本号相同的初始快照；剩余天数仍在绘制时使用当前游戏日计算。金额汇总继续使用溢出取上限的保护。

`OverdueCollectionCoordinator` 只在权威主机运行。它缓存托收目标及 `UniqueMultiplayerID → Farmer/GameWalletAdapter` 绑定；没有托收目标时 tick 立即返回，有目标但钱包没有正向变化时只做整数读取、比较和必要的基线下调，不创建交易、不获取核心锁、不写日志或广播。每 60 tick 低频核对一次对象引用；银行托收写钱包时用重入保护，并在写入后直接更新基线。同一 tick 的所有变化只广播一轮过滤快照。

待重连补扫集合非空时才创建遍历副本，以允许补扫过程中删除已处理 ID；为空时跳过该循环，后续钱包收入采样仍照常执行。

数据损坏、回滚失败、非法远程消息和存档覆盖风险始终使用正常 Warn/Error，不受调试开关影响。

## 实机验收重点

- ATM 在村长家原墙面上正确显示，不遮挡玩家、NPC、路径或原地图图块。
- GMCM 开关能同步控制 CP 地图标记、世界精灵和主机交易授权。
- 鼠标、键盘、手柄、75%–150% UI 缩放和窗口调整均无重叠或误操作。
- 定期页两种来源可通过鼠标与手柄焦点切换；切换页面保留来源，重新打开恢复钱包。检查切换来源保留金额、所选余额提示、“最大”、确认内容，以及等待主机响应期间的编辑锁定。重点检查中英文、大金额和最小窗口下的文本布局。
- 共享/独立多人钱包、重连、双开菜单、陈旧 revision 与远离 ATM 的远程请求行为正确。
- 原版账本钱包模式切换在有未结清合同时被取消；清空合同后只迁移活期余额一次。
- 贷款逾期通知按玩家和会话去重，且不依赖可选 MFM 包。
- 检查同日多笔定期与贷款同时到期的 HUD，中英文内容、定期笔数和还款提醒；提前支取、提前还清、日结失败不能误报。共享、独立、分屏及远程客户端都应只显示应收通知，重复同步不重复弹出，当天晚加入或重连可补收，次日不重发定期提示。
- 在 `SaveLoaded → DayStarted`、断线重连、长期离线和其他模组改写钱包的场景检查托收检查点、保留额、单次共享钱包扣款及 `totalMoneyEarned` 不变。
- 检查到期前、到期当天和逾期后的中英文剩余天数显示；贷款确认页的摘要、完整条款翻页和返回操作在小窗口下应可读，查看条款不能提交交易，修改金额应取消旧确认。
- 检查托收中的 ATM 限制、借款确认页按需展开的完整条款、资金来源通知，以及 `simplebank_*` 主机命令的 `confirm` 二次确认和审计日志。
