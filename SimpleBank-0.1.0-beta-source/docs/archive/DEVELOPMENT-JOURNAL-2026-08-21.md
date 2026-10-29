# Agent 1 开发进展记录

## 记录原则

- 本文件从 2026-08-21 起记录 Agent 1 对项目所做的每一次修改，以及修改依据、思路、风险和验证结果。
- `HANDOFF.md` 仅作为历史背景和待验证线索，不作为事实来源或根因结论。
- 对项目现状的判断优先采用：可复现的实机现象与完整日志、当前源码行为、自动化测试结果、静态检查结果；文档描述必须与这些证据交叉验证。
- 对尚未复现的问题明确标记为“现象”“假设”或“待验证”，不得把相关性直接写成因果关系。
- 后续每次修改都在本文件追加记录，不覆盖既有记录；如果推翻旧判断，新增一条更正说明。

## 2026-08-21：建立开发记录与隔离过期文档

### 修改内容

1. 将 `README.md` 临时重命名为 `README.outdated.md`。
2. 将 `DEVELOPMENT.md` 临时重命名为 `DEVELOPMENT.outdated.md`。
3. 将 `VERIFICATION.md` 临时重命名为 `VERIFICATION.outdated.md`。
4. 新建本文件 `Agent_1_development.md`。

### 修改思路与依据

- 上述三份文档生成于 2026-08-19，晚于它们的当前源码在 2026-08-21 又发生了钱包模式迁移与多人处理改动。
- 旧文档仍描述“共享转个人时迁移完整账户”，而当前 `BankService` 和核心测试采用“两个方向均阻止未结清定期/贷款，合法切换只迁移活期”的新规则。
- `VERIFICATION.md` 记录的是 283 个断言，当前核心测试实际通过 288 个断言。
- 临时改名用于避免后续开发误把旧文档当成当前规范；文件内容完整保留，待功能和实机行为稳定后再据实重写并恢复正式名称。
- `HANDOFF.md` 没有改名或修改。其关于 `bank == null`、`ReturnedToTitle`、账户重置及客户端等待之间关系的描述只保留为调查线索。虽然当前源码中 `ModEntry.bank` 确实只在 `OnReturnedToTitle` 明确置空，但这不足以证明实机故障一定由该事件触发，也不足以证明多个现象共享同一根因。

### 影响范围

- 本次只调整文档文件名并新增开发日志，没有修改 C#、JSON、资源、构建脚本或银行业务行为。
- 旧构建/打包脚本如果硬编码正式文档名，后续运行前需要检查其行为；这是临时文档隔离可能带来的预期影响。

### 验证结果

- 改名前确认三个 `*.outdated.md` 目标均不存在，避免覆盖已有文件。
- 已确认 `README.outdated.md`、`DEVELOPMENT.outdated.md`、`VERIFICATION.outdated.md` 和 `Agent_1_development.md` 均存在，三个旧文档原名均已不存在；`HANDOFF.md` 保持原名和原内容。
- 已检查构建工具对文档名的依赖：
  - `scripts/validate.ps1` 会读取 `README.md`，并明确要求 `DEVELOPMENT.md` 存在；旧文档隔离期间该脚本会预期失败。
  - `build/package-source.ps1` 的源码包文件清单包含三个正式文档名。
  - `build/package-release.ps1` 会把 `README.md` 复制进发行目录。
- 因此，在正式文档根据当前实现重写并恢复原名以前，不应把上述文档缺失导致的校验或打包失败判断为业务代码回归。本次未修改这些脚本，以免临时状态进入正式构建规则。

## 2026-08-21：撤销旧文档临时改名

### 修改内容

1. 将 `README.outdated.md` 恢复为 `README.md`。
2. 将 `DEVELOPMENT.outdated.md` 恢复为 `DEVELOPMENT.md`。
3. 将 `VERIFICATION.outdated.md` 恢复为 `VERIFICATION.md`。

### 修改思路与依据

- 用户明确要求优先保证构建脚本正常运行。
- `scripts/validate.ps1`、`build/package-source.ps1` 和 `build/package-release.ps1` 均依赖这些正式文件名；继续使用临时文件名会人为破坏静态校验和打包流程。
- 本次只撤销文件名变更，不把旧文档内容重新认定为当前事实。旧文档与当前源码不一致的部分仍需通过源码、测试及实机证据逐项核对。
- `HANDOFF.md` 仍只作为调查参考；其中提出的问题链路和根因假设在获得可复现证据前不视为结论。

### 风险与影响

- 构建及打包脚本重新获得其预期的文档路径。
- 正式文件名恢复后，阅读者可能误以为内容完全最新；开发时必须继续遵循本文件“记录原则”中的证据优先级。

### 验证结果

- 已确认 `README.md`、`DEVELOPMENT.md`、`VERIFICATION.md` 均存在，对应的三个 `*.outdated.md` 文件均不存在。
- 已重新运行 `scripts/validate.ps1`：通过。脚本成功校验 23 个 JSON 文件，以及 ATM、钱包写入路径、钱包模式迁移、多人快照、Overview、分组打包和 SMAPI 构建引用等静态不变量。

## 2026-08-21：根据首轮实机日志修复分屏会话串扰

### 收到的实测现象

1. 共享转独立后，活期从 `10000g` 变为 `10001g`，该数值是在过夜读档后观察到的。
2. 切换后的首次登录中，主机交易正常；非主机交易已经在权威账户生效，但 ATM 一直显示“等待主机确认”，重开 ATM 后才能看到结果。
3. 独立经济下非主机退出后，主机 ATM 因 `bank == null` 无法打开。
4. 非主机重连后，主机再次表现为账户状态被重置，非主机后续请求不再改动权威账户并持续等待。

### 日志证据与判断过程

- 同一份运行日志先记录主机 `save-loaded ... bankNull=True`，随后记录非主机 `save-loaded ... bankNull=False`。对于应拥有独立客户端快照的非主机屏幕，首次载入时已经持有非空 `bank` 不合理；它表明两个本地屏幕正在读取同一个 `ModEntry.bank` 普通字段。
- 日志随后记录非主机 `returned-to-title`，紧接着主机的 `peer-disconnected ... bankNull=True`。当前 `OnPeerDisconnected` 只清理消息限流器，不会将银行置空；`OnReturnedToTitle` 才会执行 `bank = null`。因此证据支持“非主机屏幕的标题返回处理清空了同一进程中主机共用的字段”，而不是“PeerDisconnected 清空了账户”。
- 这也解释了首次等待：非主机 `SaveLoaded` 看到主机的非空 `bank` 后从方法提前返回，没有按客户端身份重新 `Attach` 多人协调器或请求快照；交易仍可能被主机应用，但共享协调器保留了主机角色，客户端响应分支不会触发 ATM 的 `ResponseReceived`，所以 `remoteAction` 无法清除。
- 非主机重连时又会反向覆盖同一个普通字段和协调器引用，主机随后可能读取客户端临时空状态，并失去权威消息处理角色，从而同时表现为“账户重置”和所有客户端请求等待。
- 上述判断与 SMAPI 官方多人文档一致：分屏会为每个本地玩家切换完整游戏状态，但模组实例跨屏幕运行；模组自己的会话字段必须使用 `PerScreen<T>` 隔离。参考：[SMAPI multiplayer API — Per-screen data](https://stardewvalleywiki.com/Modding%3AModder_Guide/APIs/Multiplayer#Per-screen_data)。
- 该判断只覆盖本次日志能够串联的同进程分屏问题，不推断远程联机一定存在相同故障。

### 代码修改

#### `src/SimpleBank/ModEntry.cs`

- 新增 `PerScreen<ScreenState>`，把以下运行态改为当前屏幕独立保存：
  - `BankService`；
  - `MultiplayerCoordinator`；
  - ATM 刷新、标记警告和拦截原因；
  - 钱包模式监听的 Farmer/Team 引用；
  - 钱包模式回滚中的重入标志。
- `MultiplayerCoordinator` 改为每个屏幕惰性创建并只绑定一次 ATM 可用性回调。
- `OnReturnedToTitle` 现在只会清理当前屏幕的银行、协调器和生命周期状态，不会影响仍在游戏中的主机屏幕。
- 生命周期日志增加 `screenId`，用于下一轮实机明确区分主机屏幕和分屏农场工人。
- 日结前后记录 `activeBefore`、`activeAfter`、`activeDelta`、日期和 revision，用于区分迁移变化与过夜利息。

#### `src/SimpleBank/MultiplayerCoordinator.cs`

- 保留每屏幕协调器已有的权威角色、本地玩家 ID 和主机 ID 缓存。
- 新增临时诊断：协调器 attach/detach、因 bank-null 丢弃消息、权威响应发送、客户端响应接收、客户端快照应用。
- 钱包模式迁移日志现在记录源/目标模式、精确迁移活期、源账户数量、迁移产品数量和 revision。

#### `src/SimpleBank/AtmMenu.cs`

- 客户端收到权威响应并清除等待状态时记录 action、status 和 message，便于确认请求是否完整走完“发送→主机应用→响应→UI 解锁”链路。

#### 测试与静态检查

- `tests/SimpleBank.Core.Tests/BankTests.cs` 新增 `TestMigrationAndSettlementBalanceAccounting`：
  1. 证明共享转独立迁移把 `10000g` 精确迁为 `10000g`，迁移本身没有产生 `1g`；
  2. 证明下一天日结按默认活期日利率 `0.01%` 将其变为 `10001g`。
- `tests/SimpleBank.GameHarness/Stubs.Smapi.cs` 增加与官方行为一致的最小 `PerScreen<T>`/`Context.ScreenId` stub。
- `tests/SimpleBank.GameHarness/Program.cs` 增加 4 个分屏会话断言，验证非主机使用独立银行实例，且非主机清理后主机的 `10000g` 权威实例仍保持不变。
- `tests/SimpleBank.GameHarness/Stubs.Stardew.cs` 让 `NetBool` stub 在值变化时触发事件，消除未使用事件警告并更接近真实 NetBool 行为。
- `scripts/validate.ps1` 新增分屏状态隔离、screenId/日结诊断以及 `10000g→10001g` 因果分离测试的不变量检查。

### 关于 `10000g → 10001g`

- 当前证据不支持迁移多生成了 `1g`。默认活期日利率为 `0.01%`，`10000 × 0.01% = 1`，并且该现象是在过夜读档后看到的，时间点包含正常日结。
- 新测试已经把两个步骤分离：迁移完成立即检查仍为 `10000g`，下一日结后才为 `10001g`。
- 为避免只凭数学吻合下结论，本次保留了精确迁移日志和日结前后日志；下一轮实机应使用日志顺序确认实际变化确实发生在 `daily-settlement`，而不是 `wallet-mode-migration`。
- 在没有出现相反实机证据前，不修改正常活期计息规则。

### 自动验证结果

- 核心项目构建：0 警告，0 错误。
- 核心测试：`PASS: 292 assertions`。
- 游戏层 harness 构建：0 警告，0 错误。
- 分屏会话 harness：`PASS: 4 split-screen session assertions; game layer compiled.`
- `scripts/validate.ps1`：通过，校验 23 个 JSON 文件及新增分屏/迁移静态不变量。

### 尚未完成的验证

- 本环境没有可运行的 Stardew Valley/SMAPI，不能宣称分屏实机问题已经验证修复。
- 下一轮实机需要按原问题顺序复测：共享转独立过夜、非主机首次交易、非主机退出、主机交易、非主机重连、双方再次交易。
- 预期关键证据：两个屏幕分别出现不同 `screenId` 的 `multiplayer-lifecycle: attach`；非主机退出只出现其屏幕的 detach；之后主机 `peer-disconnected` 必须显示 `bankNull=False`；客户端每笔交易都应出现权威端 `multiplayer-response: sent`、客户端 `received` 和 ATM `remote response`。

## 2026-08-21：调查主机首次进入 ManorHouse 卡顿

### 新实机结果

- 上一轮分屏会话问题已由新日志确认解决：
  - 非主机退出时只 detach 对应的 `screenId=1/2`；
  - 随后的主机 `peer-disconnected` 均为 `bankNull=False`；
  - 每笔非主机交易均出现主机 `multiplayer-response: sent`、客户端 `received` 和 `ATM remote response`；
  - 非主机重连后获得最新 revision，并能继续完成交易。
- `10000g→10001g` 也得到实机确认：日志明确显示 `daily-settlement ... activeBefore=10000, activeAfter=10001, activeDelta=1`，因此变化来自正常日结，不是迁移误差。
- 新现象：主机当日第一次进入刘易斯家（`ManorHouse`）时开始卡顿。

### 现有日志能够与不能够证明的内容

- 当前日志没有记录 `Warped`、`Maps/ManorHouse` 资产失效、CP 标记等待、ATM 精灵创建耗时或 Simple Bank 每 tick 耗时，无法定位卡顿发生在哪一阶段。
- `multiplayer-message-dropped ... bank-null` 发生在新分屏的 `SaveLoaded` 之前，之后该屏幕 attach 并成功应用 revision 9/22 的快照。这是主机主动快照早于客户端银行初始化的可恢复竞态，本日志没有显示它造成 ManorHouse 卡顿。
- 不能仅凭“进入刘易斯家时卡顿”就认定是 ATM 精灵、Content Patcher 或其他模组；本轮先减少一项能够从源码证明的冗余地图工作，并增加低频性能诊断。

### 源码审计结果

- Content Patcher 包的唯一用途是给 `Maps/ManorHouse` 写入地图级属性 `SimpleBankAtmArea=14,8,2,2`。
- 旧配置同时要求 Content Patcher 加载 `assets/atm-overlay.tmx`，并把一张四格全为 0 的空 `Buildings` 层合并到 `(14,8)–(15,9)`。该 TMX 不提供贴图、tile、碰撞或交互属性，视觉和业务行为均来自主模组。
- Content Patcher 官方 `EditMap` 文档明确将 `MapProperties` 作为独立编辑类型；仅写地图属性时 `FromFile`、`ToArea` 和 `PatchMode` 都是非必需的。参考：[Content Patcher EditMap — Edit map properties](https://github.com/Pathoschild/StardewMods/blob/develop/ContentPatcher/docs/author-guide/action-editmap.md#edit-map-properties)。
- ATM 刷新请求可能由存档载入、warp、地图失效、钱包模式变化和配置变化同时触发。旧 `QueueAtmRefresh` 每次调用都会把 120-tick 标记等待重新置满；如果存在重复失效通知，等待窗口会被反复延长。单次检查本身很轻，但缺少次数和耗时数据。

### 修改内容

#### Content Patcher 地图补丁

- `content/[CP] Simple Bank Map/content.json` 改为仅使用 `MapProperties` 写入坐标标记。
- 删除不再引用的空白 `content/[CP] Simple Bank Map/assets/atm-overlay.tmx`。
- ATM 坐标、标记值、启用 token、主模组世界精灵、碰撞和交互逻辑均未改变。
- 这会消除首次处理 ManorHouse 补丁时对额外 TMX 文件的读取、解析和空图层合并；由于尚无新实机耗时数据，本记录不把它写成已经确认的唯一卡顿根因。

#### ATM 刷新与性能诊断

- `QueueAtmRefresh` 现在给每次刷新分配 sequence 和原因；已有刷新等待中收到的新请求会合并，不再把完整 120-tick 等待反复重置。
- 合并请求达到 10 次及以后每 60 次时输出 `atm-performance: refresh-queue-storm`，用于发现资产失效风暴，同时避免逐次刷屏。
- 进入 ManorHouse 输出 `atm-performance: manor-warp`，记录 screenId、标记是否存在、是否已有待处理刷新以及当前临时精灵数。
- 精灵刷新完成输出 `atm-performance: refresh-completed`，记录：
  - C# 刷新耗时与从排队到完成的总耗时；
  - 等待标记 tick 数和合并请求数；
  - marker 状态、是否添加精灵、刷新前后临时精灵数量。
- 在 ManorHouse 中，只有 Simple Bank 的整个 `OnUpdateTicked` 超过 4ms 才输出 `atm-performance: slow-update`；连续慢帧按两秒节流并汇总被抑制次数。
- 性能计时使用 `Stopwatch`，正常帧不产生日志字符串；诊断按 `PerScreen` 隔离。

#### 文档和静态规则

- 更新 `README.md`、`DEVELOPMENT.md`、`VERIFICATION.md` 中“空 TMX overlay”的描述，改为属性级地图标记。
- 更新 `scripts/validate.ps1`：要求 CP 补丁不包含 `FromFile`、`ToArea`、`PatchMode` 或 `MapTiles`，禁止空 TMX 回归，并检查性能诊断与刷新合并仍存在。

### 自动验证结果

- 游戏层 harness 构建：0 警告，0 错误。
- 分屏会话 harness：4 个断言通过。
- 核心测试：292 个断言通过。
- `scripts/validate.ps1`：通过，23 个 JSON 文件和所有静态不变量通过。

### 下一轮实机判定方法

- 如果没有 `atm-performance: slow-update`，且 `refresh-completed` 的 `refreshMs/queuedMs` 很低，但进入过程仍卡顿，则现有证据不支持 Simple Bank 的 tick/精灵刷新是卡顿源，应继续对比禁用 CP 包或其他 ManorHouse 编辑模组。
- 如果出现 `refresh-queue-storm`，说明地图资产在重复失效，需要根据 sequence 和触发原因继续定位发起方。
- 如果 `refresh-completed` 的 `refreshMs` 明显偏高，应进一步拆分纹理首次加载、临时精灵清理和创建耗时。
- 如果持续出现 `slow-update`，应根据 `pendingRefresh` 和 `markerRetryTicks` 判断是刷新等待还是钱包模式 guard，并继续细分计时。

## 2026-08-21：新增可开关的全链条调试模式、完整概览与还款确认

### 用户补充要求与实现原则

- 用户明确要求不能只保留上一轮 ManorHouse 性能日志，而要综合覆盖模组的其它所有功能链条；因此本轮先按真实代码入口逐项盘点启动/配置、存档、地图与 ATM 授权、菜单、钱包、银行核心、每日结算、钱包模式迁移和多人同步，再决定诊断点。
- 调试输出的目的不是记录每一帧，而是能把一次业务操作从“入口”串到“最终状态或安全回滚”。常规绘制、普通每 tick 和逐个数字键输入不输出日志，避免即使主动开启调试也产生难以阅读的噪声。
- 关闭调试时必须低开销：动态日志字符串、Stopwatch 计时、账户汇总和刷新统计均在 `Enabled` 判断之后才执行。数据损坏、无效快照、迁移安全回滚失败等真正需要玩家知道的警告/错误不受调试开关影响。

### 配置与统一日志入口

#### `src/SimpleBank.Core/BankConfig.cs`、`config.json`

- 新增 `DebugLogging`，默认值为 `false`；`Clone()` 会完整保留该值。
- 该选项是本地诊断偏好，不改变银行产品、余额、权限或主机权威规则。

#### `src/SimpleBank/ModDiagnostics.cs`

- 新增统一诊断封装，所有调试输出使用 SMAPI `Debug` 等级并带 `debug:` 前缀。
- `Enabled` 通过委托实时读取当前配置，因此 GMCM 开关保存后无需重载存档即可生效；读取异常按关闭处理，不能影响业务逻辑。

#### `src/SimpleBank/ModEntry.cs` 与 GMCM

- GMCM 新增“诊断设置 / 调试模式”分区、英文/中文名称和完整 tooltip。
- `ModEntry` 将诊断器传入存档仓库、多人协调器、ATM 菜单和核心银行服务。
- 增加启动、SaveLoaded 完成、Saving、ReturnedToTitle、钱包模式 guard 绑定/预检/拓扑变化、ATM 打开批准/拒绝、peer 连接/断开、ATM 纹理请求、ManorHouse 地图失效、Content Patcher token 注册、GMCM 注册/保存的诊断。
- ATM 打开拒绝仍按连续相同原因去重，并且关闭调试时在计算原因之前返回。
- 上一轮 `atm-performance` 的 warp、排队、合并风暴、刷新完成和慢 update 输出全部纳入调试开关。关闭时不调用 Stopwatch、不维护仅供诊断的合并计数，也不读取临时精灵数量；ATM 实际刷新与去重行为不变。
- 每日结算的活期前后汇总只在调试开启时计算；结算失败仍保留正常 Error 输出。

### 存档、银行核心与钱包诊断

#### `src/SimpleBank/SaveRepository.cs`

- 增加无旧存档、有效存档读取和成功写入诊断，记录 schema、钱包模式、revision 和个人账户数。
- 反序列化失败、未知 schema、校验失败或为保护旧数据而跳过写入仍使用正常 Warn/Error，不依赖调试开关。

#### `src/SimpleBank.Core/BankService.cs`

- 构造函数新增可选的 `Func<bool>`/`Action<string>` 诊断委托，保持核心程序集不依赖 SMAPI，现有调用和测试仍兼容。
- 为 deposit、withdraw、term、term-early、loan、repay 标注明确 operation，并记录：事务开始、前置校验拒绝（含 Duplicate/StaleRevision）、成功后的钱包/活期/定期条目与总额/贷款本金利息状态、异常后的原子回滚。
- 记录配置热更新、权威状态替换和按 revision 缓存的账户快照。
- 每日结算记录目标日、处理天数及结算前后账户数、活期、定期条数/总额和贷款本金/利息汇总；跨账户汇总使用 `decimal`，避免诊断本身引入 long 溢出并改变业务结果。
- 所有动态核心字符串都先检查诊断开关。新增辅助返回方法确保 DataInvalid、ModeMismatch、stale revision、无效 transaction ID 和 duplicate 等在尚未进入 mutation 前也留下链路终点。

#### `src/SimpleBank/AtmMenu.cs` 中的 `GameWalletAdapter`

- 记录钱包加/减的拒绝原因与前后余额、实际写入结果，以及业务异常触发的余额回滚。
- 钱包 setter 抛异常时记录异常类型/消息；随后记录钱包余额和个人/共享历史收入计数是否恢复成功。
- 保持原有共享本机 `addUnearnedMoney`、个人或远程 `SetIndividualMoney` 路由和历史收入保护逻辑不变。

### ATM UI 与多人链路

#### `src/SimpleBank/AtmMenu.cs`

- 记录菜单打开/关闭、页签、产品和存单选择、action 选择、是否需要确认、确认/取消、请求提交、本地结果、等待主机、主机响应及 UI 缓存失效。
- 不记录 draw、hover、普通导航焦点移动或每个数字键，避免无业务价值的刷屏；最终提交日志始终包含实际 amount/product/revision。

#### `src/SimpleBank/MultiplayerCoordinator.cs`

- 记录 attach/detach、客户端发往主机的消息、同步请求是否被限流、事务限流、无效请求身份、主机收到的 operation/amount/revision、事务结果、响应发送/接收、定向快照发送、广播接收者数量、快照应用与 stale/null 快照忽略原因。
- 保留 `screenId`、本地/主机/player ID 和 revision，确保分屏与远程联机都能重建“客户端提交→主机校验/应用→先广播权威状态→响应→客户端 UI 解锁”的顺序。
- 钱包模式迁移继续记录来源/目标模式、移动的活期金额、源账户数、定期/贷款数量和新 revision；迁移失败的保护性 Error 不被调试开关隐藏。
- 最终审计补充了 `EnsureWalletMode` 的进入、已经一致和迁移预检结果，使“为什么没有迁移”与“为什么拒绝迁移”也有链路终点，而不只记录成功移动的情况。

### 概览界面

- 原先单段 `menu.overview.hint` 信息不完整，本轮改为四条分别绘制并按实际字体高度向下排列：
  1. 账户状态（正常、存档无效锁定、钱包模式同步锁定）与共享/个人归属；
  2. 活期余额；
  3. 未到期定期条目数、总额和最近一条距离到期天数；
  4. 最大贷款额度、当前待还本金、待还利息和距离到期天数。
- 已到期未清项目显示逾期天数，无定期/未结贷款显示“无”。所有金额继续通过 `FormatGold` 显示 `g` 与本地化千位分隔符。
- 新增中英文账户状态、四条概览、到期/逾期/无的本地化键；保留旧键以避免破坏旧翻译包，但新界面不再依赖旧 hint。

### 贷款还款二次确认

- `BeginSecondaryAction` 现在对 `repay` 与 `term-early` 都进入 `awaitingConfirmation`，因此鼠标和手柄共用原有确认状态机。
- 确认文本显示本次实际最多可偿还的金额，并明确“先抵扣利息、再抵扣本金”。
- Cancel 不提交请求；只有聚焦 Confirm 后才执行。切换页签、产品、键盘项或其它控件会先取消待确认动作，沿用并扩大原安全规则。

### 文档、规则和自动测试

- 更新 `README.md`：GMCM 调试开关、诊断覆盖面与低开销原则、四条概览、还款确认顺序。
- 更新 `DEVELOPMENT.md`：诊断代码约束和实机清单；更新 `VERIFICATION.md` 的静态验证结论。
- `scripts/validate.ps1` 现在要求：
  - `DebugLogging=false` 必须随配置发布；
  - GMCM 调试名称/tooltip 与低开销 gate 必须存在；
  - `ModDiagnostics`、核心委托和诊断 gate 测试必须存在；
  - 概览四条信息和到期格式必须存在；
  - repay 必须进入与 term-early 相同的确认状态机。
- `tests/SimpleBank.Core.Tests/BankTests.cs` 新增 `TestDiagnosticsGate`：证明关闭时事务和快照不产生核心诊断，运行中开启会出现 begin/applied，重新关闭后立即停止；配置 clone 测试也覆盖 `DebugLogging`。

### 本轮验证结果

- `SimpleBank.GameHarness.csproj`：构建成功，0 警告、0 错误。
- `SimpleBank.Core.Tests.csproj`：构建成功，0 警告、0 错误。
- 核心测试：`PASS: 296 assertions`。
- 分屏游戏层 harness：`PASS: 4 split-screen session assertions; game layer compiled.`
- `scripts/validate.ps1`：通过，校验 25 个 JSON 文件以及新增诊断/概览/还款确认不变量。
- 尚未在真实 SMAPI/游戏画面中验证 GMCM 展示、最小分辨率概览换行、鼠标/手柄还款确认和远程主机日志顺序；这些项目已列入 `DEVELOPMENT.md` 实机清单，不能用 harness 结果替代实测。

### 原始完整构建脚本复核

- 按“优先保证构建脚本正常运行”的既有要求，最后没有以手工 build 命令替代项目入口，而是原样运行 `build/build.ps1`。
- 第一次在文件沙箱内运行时，.NET restore 因无法读取 `C:\Users\admin\AppData\Roaming\NuGet\NuGet.Config` 而失败；错误发生在 NuGet 用户配置权限边界，不是脚本或源码编译错误。未修改构建脚本绕过该行为。
- 使用相同脚本并允许其读取用户级 NuGet 配置后：静态验证通过、Release 核心项目 0 警告/0 错误、296 个断言通过，并成功生成可选 MFM ZIP。
- 环境未提供 `Stardew Valley.dll`，脚本按自身设计输出警告并跳过真实 SMAPI DLL 编译；游戏层源码已另由 `SimpleBank.GameHarness.csproj` 编译通过，但这仍不等同于真实游戏程序集构建或实机验证。

## 2026-08-21：根据首份全链条日志复核诊断语义并监测取消钱包模式请求

### 严谨排查结论

- 逐笔核对实机日志中的 revision、钱包、活期、定期和贷款：revision 31→42 单调推进；一次钱包不足的存款在 revision 37 原子拒绝且不推进 revision；非主机 `5000g` 本金、`13g` 利息偿还 `100g` 后变为本金 `4913g`、利息 `0g`，符合利息优先规则；后续结清与每日结算也一致。没有证据支持交易、余额或权威同步错误。
- 同一银行 revision 下，非主机快照偶尔先读到旧钱包、随后读到新钱包，是 Simple Bank 状态消息与 Stardew NetField 钱包同步的两条通道到达顺序不同。现有缓存会按钱包值变化再次刷新并得到正确余额，因此这是预期的短暂观测，不修改交易顺序或“先广播银行状态再响应”的安全规则。
- 客户端每次快照都调用 `UpdateConfig` 并增加 `configRevision`，即使配置完全不变；这不是配置 revision 的设计意图，会造成错误的缓存失效和日志噪声。
- 点击当前页签会执行原有重置选择/确认状态的行为，因此该动作本身是有效 UI 输入；但记录成 `from=term, to=term` 的 `tab-changed` 语义不正确。
- `ReturnedToTitle` 已清除 pending ATM 刷新，但随后的 ManorHouse 资产失效事件会再次排队并跨标题画面保留到下一存档，可能夸大 `queuedMs`；标题画面没有可刷新的世界，该排队不是必要功能。
- 玩家确认“预检成功但未迁移”来自手动取消切换。仅凭没有迁移不能推断取消；可靠观测点是原版 `changeWalletTypeTonight` 从 `true` 回到 `false`。

### 最小修复与风险边界

#### 取消切换监测

- 复用已经负责“申请切换前预检”的同一个 `changeWalletTypeTonight.fieldChangeEvent`，不增加轮询、UI patch 或 Harmony：
  - `false→true` 保持现有预检；
  - `true→false` 新增 `wallet-mode: request-cleared`。
- 监测分支只读状态，不写 NetBool，不改变允许/拒绝决定。`restoringWalletModeRequest=true` 时标记 `source=simple-bank`；其它来源诚实标记为 `source=game-or-player`，因为原版也可能在成功处理后清除标志，不能武断归因给玩家。
- 日志同时记录 bankMode、gameMode 和 revision，结合后续 `topology-changed`/migration 判断最终结果。该方案直接复用已实测能在原版受理时同步触发的安全边界。

#### 等价配置快照

- `BankService.UpdateConfig` 在完成原有 clone、旧配置迁移和完整校验后，比较所有运行时配置字段以及每个定期/贷款槽的 Enabled/Days/Rate；完全等价则直接返回，不替换配置、不增加 revision、不触发虚假 UI 配置失效。
- 实际 ATM、利率、额度、逾期倍数、调试开关或任一产品变化仍走原更新路径。比较发生在已规范化、已验证的 clone 上，不放宽输入校验，也不改变多人消息协议，避免远程兼容风险。

#### 页签与快照诊断

- 点击不同页签仍输出 `tab-changed`；点击当前页签输出 `tab-reselected`。原有选择索引、确认取消和布局重建行为完全不变，只修正诊断名称。
- ATM 快照刷新新增 reason 和四个 cause 布尔值。银行同步主动失效显示 `bank-state-changed`；同 revision 下只有钱包变化显示 `wallet-field-changed`，明确说明实机日志中的先旧后新是 NetField 追赶。缓存条件和余额读取路径不变。
- 从 `ModEntry` 注入核心诊断时补充 screenId，使分屏中两个独立 BankService 的 `core-config/core-state` 不再混淆；纯核心仍不依赖 SMAPI。

#### 标题画面地图失效

- `OnAssetsInvalidated` 在 `!Context.IsWorldReady && bank == null` 时记录一次 ignored 原因并返回，不调用 `QueueAtmRefresh`。
- SaveLoaded 本身仍无条件排队必要刷新；若加载阶段 worldReady 尚未建立但 bank 已创建，也不会被拦截。因此初始加载、分屏晚初始化、GMCM ATM 变化、实际 ManorHouse warp 与游戏内地图失效路径均保留。

### 测试补充

- 核心配置测试新增两条断言：等价 clone 不增加 config revision；真实产品利率变化仍增加 revision 并生效。
- 静态验证新增取消请求中性来源、标题画面失效防护、tab-reselected、wallet-field-changed 快照原因和 ConfigsEquivalent 的不变量。
- 最终复核结果：游戏层 harness 与 Release 核心项目均 0 警告/0 错误；`PASS: 298 assertions`；`PASS: 4 split-screen session assertions`；25 个 JSON 与全部静态不变量通过。
- 原始 `build/build.ps1` 再次完整运行：静态验证、Release 核心构建和 298 条断言通过，并重新生成 `SimpleBank-MailFramework-1.7.5.zip`。环境仍缺少 `Stardew Valley.dll`，所以脚本按设计跳过真实 SMAPI DLL 构建；没有修改脚本或用另一套入口替代它。

## 2026-08-22：撤销无法可靠归因的“玩家取消切换”监测

- 用户指出：如果 `changeWalletTypeTonight: true→false` 不能区分玩家主动取消与原版游戏清除，请求清除监测就没有足够诊断意义。复核后确认该判断成立。
- `useSeparateWallets` 是否随后变化只能说明请求最终有没有执行，不能证明是谁清除了请求标志。要严格识别玩家点击取消，需要侵入原版账本 UI/方法；仅为诊断引入 Harmony 或 UI 注入会增加版本兼容与输入流程风险，不符合最小修复原则。
- 撤销 `OnWalletModeChangeRequested` 中的 `true→false` 分支和 `wallet-mode: request-cleared/source=...` 输出，恢复为只处理已经验证有安全价值的 `false→true` 申请前预检。
- 删除 README、DEVELOPMENT、VERIFICATION 和静态验证中关于请求清除/玩家取消监测的说明与要求，避免文档宣称无法证明的能力。
- 保留与该功能独立、且已由源码与实机日志证明合理的修正：等价配置不增加 revision、当前页签使用 `tab-reselected`、标题画面地图失效不排队刷新、钱包 NetField 追赶使用 `wallet-field-changed` 标明快照原因、核心日志包含 screenId。
- 撤销后最终验证：游戏层与 Release 核心构建 0 警告/0 错误，298 条核心断言、4 条分屏断言、25 个 JSON 与静态规则全部通过；原始 `build/build.ps1` 也再次通过并完成可选包打包。真实 SMAPI DLL 仍因环境缺少 `Stardew Valley.dll` 按脚本设计跳过。
## 2026-08-22：双语文本审计与贷款逾期游戏内通知

- 对主模组 `i18n/default.json` 与 `i18n/zh.json`、可选 MFM 信件包的中英文键进行了集合对比；两组翻译键均一一对应。确认玩家界面、GMCM、错误提示与刘易斯欢迎信支持中英文，但 SMAPI 调试/内部诊断、清单元数据和开发文档不是本地化的玩家界面文本，仍以英文为主，因此不能笼统称为“项目中的每一个字符串都双语化”。
- 追踪每日结算后确认：旧实现只在 `SettleOneDay` 中将贷款标记为 `Matured` 并按逾期倍率继续计息，ATM 能显示逾期状态，但没有主动的游戏内通知。
- 采用不依赖 Mail Framework Mod、也不改变存档 schema 的最小方案：每个本地屏幕维护当前游戏会话内已提示的贷款 ID；当主机日结完成、主机载档，或农场手收到主机同步后的银行状态时，只检查当前玩家对应的有效账户。发现 `Matured` 贷款后显示一次本地化 HUD 错误提示，列出逾期天数、待还本金和待还利息。
- 独立经济只检查当前玩家 ID 对应的个人账户；共享经济的同步快照包含共享账户，因此每个已连接的本地分屏或远程玩家都会在各自屏幕收到提醒。逾期时不在线的玩家在之后首次同步该逾期状态时仍会收到提醒。返回标题界面时清空会话去重集合；重新进入存档会再次提醒仍未结清的逾期贷款，避免长期逾期因错过首次日期而永久失去通知。
- 通知路径复用现有 `BankStateChanged` 同步事件，不新增网络消息、不修改交易或结算结果、不修改贷款利息计算，也不依赖可选 MFM 包。调试模式开启时新增 `loan-notification: overdue-shown`，关闭时不构造该诊断字符串。
- 静态验证新增逾期 HUD 的主模组接线、会话去重、双语键和调试链路不变量；25 个 JSON、主/可选包中英文键对齐及全部静态规则通过。
- 当前执行环境在本轮缺少任何 .NET SDK/runtime：原始 `build/build.ps1` 已运行并先通过静态验证，但随后因系统 MSBuild 无法解析 `Microsoft.NET.Sdk` 而停止；已有 net6 测试程序也因缺少 runtime 无法启动。使用系统 Roslyn 对游戏层进行补充语法检查时只遇到现有代码的 `.NET 6 Environment.TickCount64` 无法由旧 .NET Framework 引用程序集解析，没有报告本次修改位置的编译错误。该环境限制不能替代完整构建，需在恢复 .NET 6+ SDK 后重新运行原始构建脚本。
