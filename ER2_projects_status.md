# ER2_Mods 项目台账（全 mod 现状 · 版本 · 发布状态）

> **单一事实源**。版本号一律以本文件为准（核对日期 **2026-09-06**，对应 git `1eccc5b`）。
> 文档与代码不一致时**以代码为准**，并回来修本文件。
> 复核命令：见文末「§5 台账自检」。

## 1. 原创 mod 一览（17 个目录 / 16 个发布 mod + 1 个内部工具）

| # | 目录 | GUID | 显示名 | 版本 | DLL | 部署状态 |
|---|---|---|---|---|---|---|
| 1 | `LimbTweaks` | `er2.limbtweaks` | ER2 Limb Tweaks | **2.13.101** | `ER2_LimbTweaks.dll` | 已部署 |
| 2 | `WeatherControl` | `er2.weathercontrol` | ER2 Weather Control | **1.7.2** | `ER2_WeatherControl.dll` | 已部署 |
| 3 | `AIFood` | `er2.aifood` | ER2 AI Food | **1.4.0** | `ER2_AIFood.dll` | 已部署 |
| 4 | `NoInteractionHints` | `com.ryan.er2.nointeractionhints` | ER2 Hide Anything | **4.5.4** | `ER2_NoInteractionHints_DoneProMaxEnd.dll` | 已部署 |
| 5 | `ModManager` | `er2.modmanager` | ER2 Mod Manager | **1.7.9** | `ER2_ModManager.dll` | 已部署 |
| 6 | `ThrowableWheel` | `er2.throwablewheel` | ER2 Throwable Wheel | **1.3.6** | `ER2_ThrowableWheel.dll` | 已部署 |
| 7 | `CombatTweaks` | `er2.combattweaks` | ER2 Combat Tweaks | **1.2.2** | `ER2_CombatTweaks.dll` | 已部署 |
| 8 | `ZoomAnywhere` | `er2.zoomanywhere` | ER2 Zoom Anywhere | **1.0.1** | `ER2_ZoomAnywhere.dll` | 已部署 |
| 9 | `HighValueTarget` | `er2.highvaluetarget` | ER2 Veteran HVT | **1.2.2** | `ER2_VeteranHVT.dll` | 已部署（+ Assets 目录） |
| 10 | `InventoryPause` | `er2.inventorypause` | ER2 Inventory Pause | **1.0.5** | `ER2_InventoryPause.dll` | 已部署 |
| 11 | `SquadCommand` | `er2.squadcommand` | **Easy Red Gate**（原 ER2 Battlefield Commander） | **1.4.57** | `ER2_BattlefieldCommander.dll` | **已发布（双语双包 EN 部署）** |
| 12 | `UniversalGeneration` | `er2.universalgeneration` | ER2 Universal Generation | **2.5.48** | `ER2_UniversalGeneration.dll` | 已部署 |
| 13 | `UnitCollision` | `er2.morephysics.unitcollision` | ER2 More Physics - Unit Collision | **1.0.8** | `ER2_MorePhysics_UnitCollision.dll` | 已部署 |
| 14 | `UnitInfoOverlay` | `er2.unitinfooverlay` | ER2 Unit Inspector | **1.0.5** | `ER2_UnitInfoOverlay.dll` | 已部署 |
| 15 | `FleshWoundsFixed` | `ER2_FleshWounds` | ER2 Flesh Wounds | **1.0.1** | （需手动构建部署，build.ps1 无条目） | 第三方修复版 |
| 16 | `MorePhysics` | `er2.morephysics` | ER2 More Physics | **0.1.49** | `ER2_MorePhysics.dll` | 复活（本地未部署，发 Nexus） |
| 17 | `Conquest` | `er2.conquest` | ER2 Conquest | **0.2.0** | — | **已终止并删除（2026-09-19）**——战略层复杂度失控 + 战斗桥接未实测；源码快照在 `research_out/conquest_salvage/`（回收清单见其 README），复盘见 §2.18，续作 = 无尽模式 |
| — | `HvtTestDriver` | `er2.hvt.testdriver` | HVT Test Driver | **1.0.0** | — | **内部自测工具，不发布** |
| 18 | `Endless` | `er2.endless` | ER2 Endless | **0.3.0** | `ER2_Endless.dll` | **M0 尖峰 v16（待实测）**：0.2.2 实测=交接链全通（设置面板弹出，文件日志实证）；用户要求**原生战役页全程不可见 + mod 任务不出现在战役页**→ v16=交接全程在本页遮罩后进行（自动点行/卡/「开始」）+ 战役注册临时制（主档在 endless/，交接时复制进 mission_editor，战斗开始后删除注册），原版战役页永久干净（详见方案 §14.16） |

**注意**：`NoInteractionHints` 的 DLL 名 `ER2_NoInteractionHints_DoneProMaxEnd.dll` 与显示名 "Hide Anything" 完全不同——**DLL 名 ≠ 插件名**，按 GUID 识别。`HighValueTarget` 目录产出 `ER2_VeteranHVT.dll`（v1.1.20 起改名 "Veteran HVT"），旧发布包名 `ER2_HighValueTarget_*` 是历史遗留。

## 2. 各 mod 功能与关键机制

### 2.1 LimbTweaks `er2.limbtweaks` v2.13.101
肢体断肢 + 流血系统。活体断肢（累积 60 伤害）、流血分级（断肢 7/s、玩家 3.5/s、非断肢 4/s）、绷带递减疗效（4/s→1/s→无效）、断腿锁卧倒、断右臂投降+10s 流血死、断臂行为限制（换弹/捡枪/投掷/绷带）、四肢伤害减半、尸体断肢（伤害+初速×0.08 ≥80）、禁止断肢单位回血（拦 `SetBleeding(false)`）、屏幕提示走原生 Hint 通道。
**patch 面**：`HitPart` / `BulletOnHit` / `BattleStartReset` / `Ragdolize`×2 / `SetBleeding` / `UseBandages` / `PickUp` / `SwitchTo` / `Reload`×2 / `Throw` / `UnSurrender`×2 / `SetPose` / `SetPlayer` / `BleedTick` / `NotifyGui` / `NativeDamageControl`（`Creature.Damage`）/ `LimbMultiplier`（`GetBodyPartMultiplier`）/ `SetBleedingTrack`。
**配置**：`General.enabled` / `Corpse Shooting.corpseEnabled` / `Living Soldiers.aliveEnabled` / `Bleeding.bleedEnabled` / `Bleeding.affectsPlayer` / `Limbs.damageNeeded` / `Limbs.corpseDamageThreshold`。

### 2.2 WeatherControl `er2.weathercontrol` v1.7.2
每局强制天气（Clear/Rain/Snow）+ 时间氛围（Midday/Sunset/Dawn/Night/Cloudy/Foggy，走 `SetDayTime`）、热键切换组合、**拦截战役天气重置**（patch `set_WeatherType` / `SetDayTime`）、原生 Hint 提示。

### 2.3 AIFood `er2.aifood` v1.4.0
AI 血量低于 `eatBelowHp`（默认 **40**）自动吃背包食物回血：`FindItemOfType<VirtualRecoverLife>()`（**返回条目必须 `TryCast`**）+ `Creature.RecoverLife(vre)`（原生吃+回血+消耗+动画），`checkInterval` 默认 **2s** 轮询，跳过玩家。

### 2.4 NoInteractionHints（Hide Anything）`com.ryan.er2.nointeractionhints` v4.5.4
**勾选制** UI 隐藏（无热键、无锁定按钮）：**14 类原生 UI**（`hints` / `notifications` / `objectiveBanner` / `hud` / `phaseBar` / `objectives` / `map` / `vehicle` / `misc` / `hitmarker` / `bloodSplash` / `chat` / `scope` / `worldMarkers`）+ 逐 mod 开关；勾选 = 始终隐藏且严格生效（Enforce 持续重藏防游戏重新显示）；地图与小地图合并为一类；通知弹窗即时清屏；修改自动保存（0.8s 看门狗落盘）。
**源码**：`Plugin.cs` + `HudCompat.cs` + `UiGroups.cs` + `UiHiders.cs` + `UiPatches.cs` + `RehidePatches.cs`。
**关键机制**：`UiGroups` 注册表（Register/IsHidden/ApplyAll/Enforce）；`UiHiders`（ElementHider 记忆式隐藏 + RefCache 5s 冷却）；所有节流用 `Time.unscaledTime`。
**v4.5.3+ 游戏 2.1.x 兼容**：`FindGameType("PhaseBarGUI")` 运行时探测，类型缺失时该类别跳过（不再 TypeLoadException）。

### 2.5 ModManager `er2.modmanager` v1.7.9（**延迟建页转正：默认开启**——玩家实测通过）
**v1.7.9（2026-09-26，玩家实测 1.7.8"没问题"后按建议转正）**

`Ui / lazyBuild` 默认值 **false → true**，cfg 描述去掉 "EXPERIMENTAL, not recommended yet"。
**转正依据（按"改默认值前先确认它当初为什么是那个值"的判据逐条核对）**：
- 当初默认关闭的理由 = "展开时插行"三次"点开没内容"（v1.5.9/1.5.19/1.5.23）——真因已定案并修复
  （v1.7.4：插入点被自己的兜底钳掉）；
- 安全网在位（v1.7.3：结构/布局自检 + 连续两次失败自动弃用并整页重建，最坏 = 一次常规建页）；
- v1.7.5 探针四采样点全干净（yUniq==active / hitAlphaMax=0 / contentH 恒定 / 无白块告警）；
- 玩家多轮实测确认（1.7.5"翻页不卡了"、1.7.8"没问题"）。
收益固化：进页建页 942 行 / 0.3~0.6s → 71 行 / 35~98ms。常规建页路径一字未动（关开关即回）。
部署 sha256 `4162db70…`（11:44）；双语双包 1.7.9；四份发布文档同步。

### 2.5 ModManager `er2.modmanager` v1.7.8（冲突提示块：**块头计数 + 可隐藏**；行高估算真因 = **按 500px 行宽估，实际只有 ~400px**）
**v1.7.8（2026-09-26，玩家"本来是因为，这个功能即便有多个按键冲突也只会显示一个，和无法隐藏的"）**

**玩家点的两个问题，机制都已定位**
1. **"多个冲突只显示一个"的真因是渲染几何**：`AddWarningRow` 的行高按**写死的 500px** 行宽估行数，
   而标签实际可用只有 ~400px（行宽 425 − 标签左右各 12）→ 行数被低估 → 文本 `verticalOverflow=Overflow`
   **溢出行底、叠进下一条警告** → 好几条在视觉上糊成一条。修法：可用宽度从 contentPage 实测
   （`container.parent.rect.width − 8 − 24`，取不到退 380 = 宁可多留空白也不叠加）。
   1.7.7 的"一键一行聚合"（消除两两配对的重复行）+ 本轮行高修复，两个因素都清掉。
2. **"无法隐藏"**：新增块头行 `⚠ 热键冲突 N 组` + 右侧小按钮（复用 `MakeSmallTextButton`）。
   按钮切换 `Ui / hotkeyWarnings`（bool，默认 true）→ `Plugin.OwnConfig.Save()` **立即落盘**（重启保持）
   → 明细行**原地 SetActive + `RelayoutContainer(blockRows)`**（与分区开合同一条重排链，**不整页重建**）
   → 按钮文案"隐藏/显示"随态切换。块头**始终显示**：收起了也能看到"共 N 组"（顺带解决"看不到总数"）。
   同一 cfg 条目也会出现在 ModManager 自己的设置页里 → 两处入口天然同步，不会出现半状态。

**实现要点**：`AddWarningRow` 改为**返回行对象**（块头要登记）；块头行 = `MM_WarnHead`（标签用硬机制 0
的 B 写法：中线锚点 + 显式尺寸）；`Plugin.OwnConfig`（static ConfigFile 引用）供即时 Save——BepInEx 不保证
条目变更即时写盘。

**验证**：0 warning 0 error；EN 构建 = 部署 = EN 包 DLL 逐字节一致 sha256 `c6917402…`（113,664 B，11:38）；
双语双包 1.7.8；四份发布文档 + 台账同步。
**待实测**：① 页顶应出现块头"⚠ 热键冲突 3 组"与明细 3 行（F / F9 / G 各一行，不再糊成一条）；
② 点"隐藏"→ 明细消失、块头保留、按钮变"显示"，**重启游戏后仍是隐藏态**；③ 隐藏/显示切换时下方行
平滑让位（无整页重建闪动，页顶淡入不重放）。

### 2.5 ModManager `er2.modmanager` v1.7.7（按键冲突提示审计：**扫描端比渲染端宽松**；一对多聚合）
**v1.7.7（2026-09-26，玩家"看看那个按键冲突提示，有没有什么问题"）**

**审计方法**：写脚本按 `IsHotkeyEntry` 的规则扫**本机全部真实 cfg**（`E:\...\BepInEx\config\*.cfg`），
模拟检测器跑一遍——拿现实数据核对，不靠读代码脑补。

**两个实锤缺陷（都已修）**
1. **扫描端与渲染端口径不一致（扫描端更宽松）**：String 条目只要键名含 key/toggle 就被当热键扫描，
   而渲染端还有 `LooksLikeKeyList`（要求条目自带 AcceptableValueList 且候选项像键）这道守卫。
   本机实锤：`er2.morephysics.cfg [Physics] ExcludedNameKeywords`（String，键名含 "key"，值是逗号分隔的
   排除名单）一直参与冲突比对——**两个 mod 的名单默认值若相同就会报一条假"热键冲突"**。
   修法：String 条目在扫描时增加 `LooksLikeKeyValue(value)` 形状校验（单词 F5/Space/Mouse0、A+B 组合、
   空值算热键；含 `,;\/:=` 或 >24 字符的名单/路径/句子排除）。**只动扫描端，渲染端不碰**
   （渲染端本来就不会把它画成改键按钮，所以只是虚警，不是功能误伤）。
2. **一对多配对不全**：A/B/C 三个绑定同用一键，旧实现报"A 与 B""A 与 C"两行（map 不更新），
   B 与 C 的关系看不到且 A 重复出现。修法：**按键值聚合，一键一行**列出全部绑定（自冲突即同一 mod
   两个条目同键也会列出）。

**审计顺带的产出（不是缺陷，但玩家该知道）**：本机当前 **3 组真实冲突**（页面上有列出）——
`F`：AdvancedCombatMovement(Follow Or Marker) × **SC keyScatter**；`F9`：ACM(Restart Mission) × **SC godKey**；
`G`：**SC keyPack** × **UniGen panelKey**。后两组是自家 mod 之间的（SC 的默认键与 ACM/UniGen 相撞），
要不要改默认值由用户定，ModManager 只负责把话说清楚。

**核对过没问题的**：渲染端对 String 的改键按钮有 `LooksLikeKeyList` 守卫（ExcludedNameKeywords 显示为
下拉而非改键按钮 ✓）；`KeyCode=None` 正确跳过 ✓；`AddWarningRow` 的行高按字符宽度估算
（CJK 16px / 拉丁 9px，fontSize 15）没有实锤问题，未动；全工作区无 `KeyboardShortcut`（BepInEx 5 的
热键类型）使用，"不认 KeyboardShortcut"不构成本机的漏报。

**验证**：0 warning 0 error；模拟脚本复扫 = `ExcludedNameKeywords` 被排除、3 组聚合输出与预期一致；
EN 构建 = 部署 = EN 包内 DLL 逐字节一致 sha256 `6f422d79…e1d9`（112,128 B，11:17）；双语双包 1.7.7。
**待实测**：页顶警告应变成 3 行聚合格式（一键一行）；`debugLog` 下出现
`hotkey conflict scan: 3 group(s)`；不再有任何把名单当按键的行。

### 2.5 ModManager `er2.modmanager` v1.7.6（淡入推广到翻页/分组/选项 + 翻页闪烁硬化：**`Destroy` 帧末才生效 = 污染行被画一帧**）
**v1.7.6（2026-09-26，玩家"翻页闪烁又出现了。现在动画我挺满意的，给翻页也加上，和子选项等。"）**

**1.7.5 探针的答卷（展开路径定案干净）**：实测日志里每个展开的 4 个采样点全部满足
`yUniq == active`（无未落定）、`hitAlphaMax=0.00`（无白块；唯一的 0.01 是悬停过渡进行中的一帧，良性）、
`contentH` 四点恒定（无整列位移），且全程**零条**"命中区颜色不透明"告警 → 展开路径排除三类成因。
玩家也确认"现在动画我挺满意的"。剩下的问题在**翻页路径**——它没有展开那套保护。

**翻页闪烁的硬化（修一条实锤机制）**：每帧清理"游戏异步填充协程灌进我们页面的原生行"用的是
`Object.Destroy(child.gameObject)`——**`Destroy` 要到帧末才生效，那一帧它仍会被渲染**
→ 原生行被画一帧 = 翻页闪一下（与 v1.5.8 定案的"原生重填整个设置面板 = 白闪"同血统，只是这次从
"渲染时机"上修）。改为 **`SetActive(false)`（立即生效）+ `Destroy`**。两处调用点（每帧巡检清理、
`ClearContent`）都改了。

**淡入推广（玩家点名）**：`BeginRevealRows(tag, targets, blockGroup)` 通用化——
1. **整页**（翻页进入 / 重进设置 / 自愈重建）：`RevealWholePage` 用**容器上的一个 CanvasGroup** 整块淡入
   （比逐行写 N 个便宜）；带防重放保护（`lastPageRevealAt` + 已有 page reveal 则跳过）——自愈重建可能
   连着触发，每次都重放会变成"页面反复淡出"，比闪烁更糟。
2. **设置分组**（`ToggleSection`）、**子选项**（`ToggleEntry` 的简介行 + 还原行）：与展开 mod 同一套
   （逐行 CanvasGroup）。收起路径一律 `FinishRevealForRows` 立即收尾（防"行在、看不见"）。
3. 上限保护保留并通用化：单次 >300 行 → gate 模式（只静默一帧，`hold=0.02s dur=0`）——
   整页 lazy 关闭时约 970 行会自动走这条，动画永远不会成为更贵的选项。
4. 翻页路径也跑 `ReassertRowHitColors`（白块类在"停用→再启用"路径上会复发，建页=一次上百行的批量启用）。

**探针扩展（翻页路径不再裸奔）**：`[MM-flash]` 现在每次建页也采 4 个点，新增判据
`foreign`（内容页里非 MM_Container 的子物体个数 = 原生填充污染；因 Destroy 延迟到帧末，同帧采样能看见它）
与 `f=<帧号>`；`tab` 追踪与 `page built in` 日志同样带 `f=` → "建页比按键晚一帧"这类问题可以直接看出。

**验证**：0 warning 0 error；EN 构建 = 部署 = EN 包内 DLL 逐字节一致 sha256 `d7dd8a58…7d884`（111,104 B，
10:51 部署，仍走 cp+sha256 以保住 cfg）；双语双包 `ER2_ModManager_v1.7.6.zip` / `_CN_v1.7.6.zip`，
四份发布文档同步 1.7.6。

**待实测（判据）**：① 翻页进出 MODS 页不应再闪；② 日志 `[MM-flash] 'page'` 四个 stage 应满足
`yUniq == active`、`hitAlphaMax=0.00`、`foreign=0`（若 `foreign>0` = 原生污染实锤，但已立即隐藏不再可见）、
`contentH` 恒定；③ 分组/子选项展开有与 mod 展开一致的淡入；④ 若翻页仍闪，日志的 `f=` + `foreign=`
能直接指名是哪一类。

### 2.5 ModManager `er2.modmanager` v1.7.5（展开闪烁：**修命中区掉色 + 展开淡入（玩家提议做成动画）+ 装定案探针**）
**v1.7.5（2026-09-26，玩家"展开时选项都会闪一下；本来我都想做一个动画来掩饰一下加载"）**

**本轮取证（用的是现场日志，不是猜）**：日志尾部一整段实测记录（`debugLog=true`、`lazyBuild=true`、页 71 行 / 28 mod）
把展开路径的每一步都打出来了，据此确认了三件事：
1. **延迟建页本身现在健康**：`[MM-lazy] 已构建 … insertAt=28 before=71` ✓、`自检通过` ✓（v1.7.4 的正效果）；
2. **几何在展开后不再变**：`几何 relayout | content 433x1806 container 425x1798` 与 `几何 deferred | … 1806 / 1798`
   两次采样**数值完全相同** → 延迟重排(+0.12s)与延迟自检(+0.36s)那两拍**不产生位移**，不是"整列被后置重排"那一类；
3. **行在建行当帧确实处于"未落定"状态**：`[MM-lazy] 状态 … [0]y=0,h=18 [1]y=-380,h=32 [2]y=0,h=46`
   —— 新建行此时还带着**克隆模板继承来的位置**（克隆来的行继承源锚点 = -380，`new GameObject` 建的是 0），
   真正的位置要等同帧后面的 `RelayoutContainer` 写下来。
   即：**"建行当帧"与"落定之后"的外观/几何是两套值**，只要有任何一拍慢一帧就渲染出半成品 = 玩家看到的闪。

**本轮改动（三件，先修最可疑的链、再按玩家建议做动画、再装探针）**
1. **命中区颜色重新确认（修 + 诊断二合一）** `ReassertRowHitColors(rows)`：每次展开遍历该 mod 的行，
   读 `HitArea` 的 `canvasRenderer.GetColor().a`；**只读、异常才写**（健康路径零写入零重建）。
   机理：`Graphic.OnDisable → canvasRenderer.Clear()` 会丢掉渲染色，再启用靠
   `Selectable.OnEnable → DoStateTransition` 重新写上；这条链只要漏一拍（典型：上一次过渡的协程被停用打断，
   `TweenRunner.m_Running` 卡在 true → 新的即时过渡不再同步应用），命中区就恢复成**不透明白**
   —— 与 **v1.5.13 定案的白块同源**。发现异常当场按各自 `ColorBlock.normalColor` 纠正，并打**故障级告警**
   （不受 `debugLog` 门控，因为这是故障不是诊断）。
2. **展开淡入**（玩家提议"做个动画掩饰加载"，本轮做成可控的淡入）：`Ui / revealAnim`（默认开）+
   `Ui / revealMs`（默认 200ms）。`BeginReveal` 只作用于**本次展开新建的行**（`mb.rows[rowsBefore..]`，
   常规建页路径恒为空 → 不重放动画），给每行挂 `CanvasGroup` 从 alpha 0 起，`hold=0.07s` 静默 + `dur` 淡入；
   alpha 量化到 1/8 步（只有真的变了才写，避免每帧无意义的 Canvas 重建）。
   **关键设计：CanvasGroup 不参与布局** → 行照常占位，所以"先隐藏"不会让下面的行多跳一次。
   安全网：① **收起时 `FinishRevealFor` 立即收尾**（行若带 alpha<1 被停用，下次展开就是"行在、看不见"
   ——本项目最忌讳的故障形态）；② 硬 deadline（hold+dur+0.8s）兜底；③ 整页重建时 `ClearReveals()` 作废旧引用；
   ④ 单次 >300 行直接不淡入（宁可瞬时显示，也不能让"掩饰"本身变成卡顿源——本项目已多次为性能付代价）。
3. **`[MM-flash]` 展开窗口探针**（debugLog 门控）：一次展开采 4 个点——同帧 / +0.03s / +0.13s（延迟重排那拍）
   / +0.36s（延迟自检那拍），每条给出闪烁三类物理成因的判据：
   `yUniq`（不同 y 的行数 < active = **未落定**）、`hitAlphaMax`（>0.5 = **白块**）、
   `contentH`（展开后若再变 = **整列位移**）。**下一轮无论闪不闪，都能直接给出是哪一类，不用再猜。**

**验证**：`dotnet build` 0 warning 0 error；EN 构建 = 部署 = EN 包内 DLL 逐字节一致
sha256 `29f07a45…1fbb3`（109,056 B，10:37 部署；**本轮刻意不用 `build.ps1 -Mod ModManager` 全量部署**——
它会 `Remove-Item` 掉 cfg，而本轮复现条件必须保住 `lazyBuild=true` + `debugLog=true`，故改用 `cp` + sha256 核对）。
双语双包：`ER2_ModManager_v1.7.5.zip`（EN，109,056 B DLL + README.txt + Nexus_description.md）/
`ER2_ModManager_CN_v1.7.5.zip`（CN，`CN_BUILD` 108,544 B + 中文文档），四份发布文档已同步 1.7.5。

**待实测（两条判据）**：① 展开任意 mod，**不应再有可见闪动**，日志**不应出现**
`命中区颜色不透明`（出现 = 白块类确实存在，已被当帧修好）；② 日志出现 `[MM-flash]`，
四个 stage 的 `yUniq == active`、`hitAlphaMax=0.00`、`contentH` **四个点一致**
→ 三类成因全部排除，剩下若有闪感就是淡入本身的观感（可调 `revealMs` 或用 `revealAnim=false` 关掉）。

### 2.5 ModManager `er2.modmanager` v1.7.4（延迟建页真因：**自己的"兜底"把正确插入点钳掉了**；自检立功）
**v1.7.4（2026-09-26，玩家"一开始打不开，过了一会再点就开了"）**
1.7.3 的自检 + 有界回退**按设计工作了**，并在实测中直接抓到真因：
```
page built in 281.1ms - rows=71 mods=28 bodiesBuilt=0/28 lazy=on | bodies=0ms fav=36ms warn=18ms other=227ms
[MM-lazy] 已构建 'ER2 AI Food' 正文: rows=11 insertAt=71 before=71 titleRow=ok
[MM-lazy] 自检失败（第 1 次）：row[0] 位置不连续(idx=71 ≠ 28)
[MM-lazy] 自检失败（第 2 次）：…（Battlefield Commander）
[MM-lazy] 连续两次自检失败 → 本会话弃用延迟建页，整页重建回常规路径
page built in 800.3ms - rows=970 bodiesBuilt=28/28 lazy=off
```
即：**行被插到容器末尾（71）而不是标题下方（28 = 标题行下标+1）** → 玩家"点开看不到"；自检连续两次抓到 →
自动弃用 + 整页重建 → 他"过了一会再点就开了"（那时所有正文已按常规路径建好）。

**根因：插入点算对了，却被我自己的"兜底"钳掉**
```csharp
insertAt = body.titleRow.transform.GetSiblingIndex() + 1;   // = 28，正确
if (insertAt < before) insertAt = before;                   // before = 71 → 28 < 71 恒成立 → 取 71 = 末尾
```
新建的行**总是先追加在容器末尾**，而标题行的下标天然小于 `before` → **这条兜底在延迟路径上必然命中**，
于是"正确插入点"被换成"末尾"。**它从来不是兜底，它就是行为本身。**
**为什么常规路径一直正常**：建页时行本来就是在标题之后紧接着建的，`titleIdx+1 == before` → 兜底是**空操作**
—— 这正是该缺陷只出现在延迟路径、且四轮都没被发现的原因。
**修法**：删掉这条兜底，位置直接采信；唯一保留的是硬性防御（下标非法 → 退化为追加）。真正的安全网交给自检。

**教训（可复用，已入技能）**
1. **"保护性钳制"的条件必须实测其命中率**：若它在正常路径上恒命中，它就不是兜底而是行为本身，且必然
   与它想保护的正确逻辑冲突。**写兜底时要在日志里打出"是否命中"**（本次靠 `insertAt=71 before=71` 一眼看出）。
2. **自检 + 有界回退的价值被本轮实证**：同一现象过去四轮（v1.5.9/1.5.19/1.5.21/1.5.23）都靠"玩家报症状 →
   我猜 → 改 → 再测"，这次**一条 `row[0] 位置不连续(idx=71 ≠ 28)` 直接给出物理成因**，且玩家侧最坏只是
   "一次常规建页"（他甚至以为只是"稍后再点就开了"）。

**建页耗时的意外发现（下一轮的候选）**：lazy 生效时仍要 281ms，其中 `bodies=0ms` 但 **`other=227ms`**
（28 个 mod 标题行 + 26 个字母分组标题 + 收藏区 36ms + 警告 18ms）。标题行每行约 2.8ms，远高于正文行的
0.75ms/行 —— 嫌疑是 `AddSectionButton` 里的**文本测量**（`MeasureTextWidth` **每次都 `new TextGenerator()`**，
配合 `MeasuresWiderThan`/`EllipsizeToFit` 的二分测量）。**典型修法是复用一个 static TextGenerator**；
本轮不顺手改（保持"一轮只验一个变量"），待 lazy 路径确认后单独做。

**待实测**：`page built in` 应约 120~280ms 且 `lazy=on / bodiesBuilt=0/28`；展开 2~3 个 mod **应当立即**在
标题下方看到设置，日志出现 `[MM-lazy] 自检通过`、且**不再有**"自检失败"。

### 2.5 ModManager `er2.modmanager` v1.7.3（延迟建页**加装自检与自愈**；建页耗时拆到分阶段）
**v1.7.3（2026-09-26，用户"正常。继续"）**
1.7.2 获玩家确认（日志实证：`收藏跳转 → 'ER2 AI Food'（已展开并滚到它，rows=11）`、`收藏已恢复（1 项）`）。
本轮处理 ModManager **最后一个已知手感问题**：进设置页的建页成本。本轮日志实测
`page built in 560.3 / 592.9ms - rows=970 mods=28`（早前 351ms；收藏区 + 28 颗星标又添了开销），且**每次进页都付**。

**做法：不"重开"这个优化，而是先给它装安全网**
延迟建页（`Ui / lazyBuild`）史上三次翻车（v1.5.9 / v1.5.19 / v1.5.23）**均已定案且修好**：
① v1.5.21 只重建容器一层（宿主未算）→ 两层重建 + 隔帧补排；② v1.5.24 插入锚点用"容器末位"猜 → 改用标题父级；
③ v1.5.23 的 `active=4` 其实是**分区默认收起**，与延迟建页无关。
但项目自己的教训是"反复制造同类故障的优化应当回退，不要继续叠修复"，故本轮**不恢复默认**，而是：
- **每次延迟构建后隔一帧自检**（`PollLazyVerify` + `LazyBuildProblem`，挂在既有每帧链上）：
  - **结构**：行必须在容器内、且**紧跟在标题行之后连续排列**（直接对应 v1.5.24 的物理成因）；
  - **布局**：宿主（contentPage）实际高度必须跟上需求高度（对应 v1.5.21）。
- 失败 → 先 `MarkPendingRelayout()` 补救一次；**连续两次失败 → 本会话 `lazyDisabled = true` + 整页重建**，
  并把原因写进 `LogWarning`（不受 debugLog 门控）。
  ⇒ **最坏代价 = 一次常规建页**（玩家今天本来就要付的那一下），而**不是页面坏掉**。
- **整页重建时清空待自检队列**——否则旧 body 已随页面销毁，自检会拿到"container 已销毁"从而**误判失败**
  （进而误弃用延迟建页）。这条是本轮自己发现的坑。
- 建页日志加**分阶段耗时**：`bodies=…ms fav=…ms warn=…ms other=…ms`（不受 debugLog 门控、沿用 5s 节流）——
  以后"进设置页顿一下"这类反馈直接用数据回答，不必猜。
- 开关仍**默认关闭**；按项目约定（"能由机器直接改配置就别让玩家去 UI 里找"）已直接把玩家 cfg 置
  `lazyBuild=true` + `debugLog=true` 供实测，**实测通过再议默认值**（v1.5.19 那次就是让玩家自己去开开关，白测一轮）。

**方法论沉淀**：**给"曾经翻过车的优化"装自动自检 + 有界回退，是恢复它的正确前置条件**——
把"最坏结果"从"功能坏掉"降级为"付一次老路径的代价"，失败就从事故变成一条日志。自检的判据要直接来自
历史故障的**物理成因**（结构不连续 / 宿主高度没跟上），而不是泛泛的"看起来对不对"。

**待实测**：进 MODS 页看 `page built in` 应降到约 60~120ms、`lazy=on`、`bodiesBuilt=0/28`；
展开 2~3 个 mod 应正常显示内容，日志出现 `[MM-lazy] 自检通过`；若出现
`[MM-lazy] 自检失败 …`（含原因）或"连续两次失败 → 本会话弃用延迟建页"，请把该行发回作者。

### 2.5 ModManager `er2.modmanager` v1.7.2（收藏跳转"要点两下"根因：**状态有两个入口**；收藏区悬停反馈）
**v1.7.2（2026-09-26，玩家"被收藏的 mod 要点击两下才能展开 + 鼠标放上去没有任何互动效果"）**

**① 两下才能展开——"设置展开态"被拆成两处、各写一半**
`ToggleMod`（点标题）做全套：`rows.SetActive` + 标题箭头 + `ReapplyInnerVisibility` + `RelayoutContainer`；
而 `JumpToFav`（点收藏条目）**只改了 `expandedMods` 与 `ModBody.expanded` 两个标志**，没做任何展开动作
→ 出现"状态已展开、界面仍折叠"。此后点标题：`expandedMods.Add` 失败 → 判定为"收起"（界面毫无变化），
再点一下才真正展开 —— 玩家体感即"要点两下"。
**修法**：抽出**唯一入口** `SetModExpanded(mb, now)`（点标题与收藏跳转共用），`ToggleMod` 只负责算"下一个状态"。
**教训：凡"设置某个东西的状态"的操作，只允许有一个入口。** 两处各写一半必然产生半状态，而半状态从日志和
界面都很难直接看出来（本次是"点击计数"与"预期差一次"才暴露）。**动手前的判据：搜索该状态的所有写入点，
多于一个就先合并再改行为。**

**② 悬停没反应——反馈机制/强度选错**
- 收藏行原本只有"行底色 3.5% 白"一层反馈：深色底上等于看不见（mod 标题行同值，只是一直没人报）。
- ★ / 还原默认 / 重置全部 / 复制全部这些小按钮是 `Transition.None`（v1.3.1 为规避"创建瞬间被默认配色染白"
  而干脆关掉过渡）→ 划过**完全没有**反馈。
**修法**：新增 `ApplyButtonHoverTint`（**normal 纯白 = 原色不变、不闪白**；hover 乘 >1 提亮；pressed 变暗）
用于所有小按钮；收藏行的 HitArea 改为**彻底透明、只负责接收指针**，并把 `targetGraphic` 指向标签文字 →
悬停时**文字变亮**。
**为什么不用 EventTrigger 绑 PointerEnter**：本项目 `UnityAction<T>` 的委托桥接有**已知 marshaling 缺陷**
（滑条/下拉的注释里早记过），而 `EventTrigger.Entry.callback` 需要 `UnityAction<BaseEventData>`——编译期
直接报"不是委托类型"，即便绕开也不可靠。**结论：uGUI 的互动反馈优先用 `Selectable` 的 ColorTint（乘法），
不要绑事件。**

**③ 顺带**：★ 收藏态改**金色**（原先 ★/☆ 同色，列表里看不出哪些已收藏）；收藏跳转加一条动作日志
`ModManager: 收藏跳转 → 'X'（已展开并滚到它，rows=N）`，下次可直接判定"点击有没有到达控件"。

**待复测**：① 点收藏条目**一下**即展开并滚到它；② 鼠标经过收藏条目文字变亮；③ 划过 ★ / 还原默认等按钮有
明显明暗变化；④ 收藏过的 mod 星标是金色。

### 2.5 ModManager `er2.modmanager` v1.7.1（**零高度标签**导致收藏区不可见；mod 展开态不再跨启动）
**v1.7.1（2026-09-26，玩家反馈"收藏后只有一条空白 + 为什么 AI Food 默认展开"）**

**① 收藏区"占着版面却看不见"——标签矩形高度为 0（一类缺陷，共 3 处）**
玩家给的两张截图（收藏前/后）用**行亮度剖面互相关**测得：**图 2 的内容整体比图 1 低 95px，且那段区域全黑**
（ASCII 亮度图确认只有右侧滚动条有像素）⇒ 收藏区确实占了高度、却什么都没画。

根因：标签 RectTransform 用的是
```csharp
anchorMin = (0, 0.5); anchorMax = (1, 0.5);      // 纵向锚点收紧在中间
offsetMin = new Vector2(x, 0f); offsetMax = new Vector2(-y, 0f);   // 上下偏移都是 0
```
`anchorMin.y == anchorMax.y` 时，两个 offset **直接决定矩形的下边与上边** → **高度恒为 0** → Text（默认
`verticalOverflow = Truncate`）把整行裁掉：**一个字都不显示，行却照占布局高度**（那 95px 空白）。
**对照实证**：玩家确认可见的"元信息行"（v1.5.18，`AddMetaRow`）用的是
`sizeDelta = new Vector2(-(li+ri), 20f)` + `anchoredPosition`，**显式给高度** —— 同一批代码里两种写法并存，
正是这条区分的活证据。

**修法**：三处标签统一改为元信息行那套定高写法——
`AddFavArea` 标题（金「★ 收藏的 mod（点击跳转）」）、`AddFavArea` 槽位标签、**`AddRestoreRow` 的"默认值 …"**
（v1.5.5 起该灰字一直不可见，只是没人报——行里还有"还原默认"按钮，看起来"正常"）。
另加 `RefreshFavArea` 收尾自检（debugLog 门控）：打印标题行高/激活态、首项标签实测 `w/h` —— **标签高 0
即该类缺陷的直接判据**，不必再靠截图量像素。
（核查过：`RefreshFavArea()` 在建页末尾本来就已调用，"重建后不刷新"这条担心不成立。）

**② "AI Food 进游戏就展开"——mod 展开态被跨启动记忆**
`expanded.txt` 实测 `m\tER2 Combat Tweaks` / `m\tReactive Ragdoll`（会话起始 `展开的 mod=2`）。
机制：v1.5.17 起的"展开状态记忆"把**mod 展开态与分区展开态写在同一个文件**，于是上个会话展开过的 mod
下次启动就带着展开打开，把它下面所有 mod 推下去。玩家早前也质疑过这点保存的必要性（"玩家都重启游戏了"）。
**修法**：**记忆范围收窄到分区** —— `LoadExpandedState` 忽略 `m\t`；`SaveExpandedState` 只写 `s\t`；
`ToggleMod`/`JumpToFav` 不再落盘（分区仍由 `ToggleSection` 保存）。玩家机器上那份旧文件的 `m\t` 行已清掉。
**为什么保留分区记忆**：否则每个 mod 里的分组每次启动都要重新点开（v1.5.20 关掉整块记忆导致"点开 mod
只看到几行分组标题"，即玩家说的"没内容"——那是同一开关的另一半作用）。

**教训（可复用）**
1. **`anchorMin.y == anchorMax.y` 时不要用 offsetMin/offsetMax 定位**：那等于直接设上下边 → 高度 0。
   要么改锚点为 `(0,0)-(1,1)`（拉伸），要么用 `sizeDelta` 给显式高度 + `anchoredPosition`。
   **同"行"不同用途不要混用两套锚点写法**（本项目元信息行对、收藏区错，并存了一周）。
2. **"看不见"要用像素证据定案**：本次靠"两张截图的互相关位移 + 空白区 ASCII 图"在两分钟内锁定
   "占了版面但没画东西"，而不是继续读代码猜。位移量（95px）还顺带给出了度量标尺。
3. **一个持久化开关若同时是两个独立状态的唯一存储，收窄它的作用范围时要显式处理另一半**（本次：分区留、
   mod 不留）。

### 2.5 ModManager `er2.modmanager` v1.7.0（**搜索整体撤除**；收藏改为**收藏 mod**）
**v1.7.0（2026-09-26，玩家"回滚。不要搜索功能，收藏不做选项收藏，做 MOD"）**

**① 搜索框整体移除**（v1.6.0 引入 → v1.7.0 撤除；存活一个版本周期，从未对外发布）
撤除理由（玩家实测结论）：用关键词过滤这个列表**对页面形态的扰动远大于帮助**——列表是近千行的竖向布局，
任何过滤都会改变可见集合与整体高度；v1.6.0 的"mod 名命中 → 整个 mod 摊开"更让一次按键激活数百行。
**结论：对"超高密度、单一长列表"的界面，"过滤"不如"直达"。**
代码侧已全部移除（`AddSearchRow` / `PollSearchField` / `ApplyFilter` / `SetModNormal` / `SetModFiltered` /
`HideModFiltered` / `EntryMatches` / `LogSearch` / `NormText` / `GetDescriptionSafe`，以及
`EntryExtras.searchText`、`ReapplyInnerVisibility` 的过滤分支、`PollControls`/`FillContent` 的挂载点、
`searchField`/`searchQuery`/防抖字段），**残留引用检查 = 0**。
**同时删掉了 1.6.0/1.6.1 的发布文档条目**（未发布过，留着会让变更史变成"加了又撤"）。

**② 收藏改为"收藏 mod"**（原先做成"收藏设置项"，玩家明确要求改）
- ★ 从"设置项操作行最左"移到 **mod 标题行右侧**；设置项操作行恢复原样（只在"已改动"时出现）。
- 收藏键 = mod 全名；页首"★ 收藏的 mod（点击跳转）"区；点击 = 展开该 mod + 滚到它的标题行。
- 持久化沿用 `er2.modmanager.favorites.txt`（每行一个 mod 名），上限 24 个；旧格式文件已删除。
- 收藏区仍为**固定槽位**（预建 24 行、只改文本与显隐）。

**教训（跨功能复用）**
1. **"可搜"的前提是"结果是小集合、且呈现方式稳定"。** 对近千行的长列表做就地过滤，用户看到的不是
   "筛选结果"而是"页面崩坏"（大幅位移 + 高度跳变 + 无从判断剩下什么）。
2. **"收藏"比"搜索"更适合这类界面**：它把"常用的几个"提升到固定位置，且**不改变其它任何东西的位置**
   ——这正是"直达"与"过滤"的区别。
3. **撤功能时同步撤文档条目**：未发布过的中间版本不必留痕，否则变更史会自相矛盾。

### 2.5 ModManager `er2.modmanager` v1.6.1（**新增收藏**；并修掉搜索的三个毛病）
**v1.6.1（2026-09-26，玩家"我对收藏有执念"+"搜索糟糕透了"）**

**① 收藏**（按"收藏**设置项**"实现）
- 展开任一项 → 操作行最左出现 ★；点击加入收藏，页首出现"★ 收藏（点击跳转）"区。
- 点收藏区条目 → 展开它所属的 mod 与分组 → `ReapplyInnerVisibility` → 重排 → 按
  `vnp = 1 - (offset-60)/(contentH-vpH)` 滚到该项可见。**跳转前先清空搜索词**（过滤视图会把它藏起来）。
- 持久化 `BepInEx/config/er2.modmanager.favorites.txt`（每行 `mod 名|条目键`），上限 24 项。
- **收藏区用固定槽位**（预建 24 行，之后只改文本与显隐）——**不做事后插行**，那是本项目三次翻车的根源。
- 顺带：操作行（原"还原行"）改为**常驻**（★ 挂在上面），"还原默认"按钮仍由 dirty 门控。

**② 搜索：三个症状一个根因**
玩家报"过滤结果不对 + 输入卡顿/闪 + 搜索后全部 mod 都被展开"——根因是 v1.6.0 把"mod 名命中"实现成了
"整个 mod 摊开显示"：**输入一个字母即命中一批 mod、946 行同时激活并重排**。
三处修正：mod 名命中改用 `SetModNormal`（按该 mod **自己的展开态**显示）／**防抖 0.22s**（停止输入才过滤）／
逐项搜索文本（键 + 标签 + 描述）**在建页时预拼**进 `EntryExtras.searchText`（此前每次按键都重读 cfg 描述）。

**教训**：**"命中一个容器"不等于"要把它整个展开"**——搜索结果该按用户自己的展开态呈现，否则一次按键就把
列表撑成几百行：表现出来像"结果不对"，实质是"结果太多、全被摊开"。诊断判据：**输入一个字符后的激活行数**。

### 2.5 ModManager `er2.modmanager` v1.6.0（**新增页首搜索框**：按 mod 名 / 设置项过滤）
**v1.6.0（2026-09-26，用户"继续更新 modmanager"）**
装 28 个 mod 之后"想找某一项设置只能靠滚"——补上此前清单里最后一项用户可感知的功能。

**实现要点（三处都可复用）**
1. **搜索框照搬本项目既有的 InputField 做法**：数值框与文本项已有两处实现，且踩过关键坑——
   **`onValueChanged` 的事件桥接在 IL2CPP 下不可靠**，所以一律"**每帧轮询 `.text`**"
   （`PollSearchField` 挂在 `PollControls` 既有每帧链上，只在文本真的变化时才重算）。新控件不必从零试错。
2. **过滤规则**：mod 名/短名命中 → 该 mod 整体显示；否则按条目的**键 / 显示标签 / 描述**三处匹配 →
   只显示命中项 + 它们所属的分组标题；都不命中 → 隐藏该 mod。空词回落到 `ReapplyInnerVisibility`（三层展开态）。
3. **`EntryExtras` 补 `row` 字段**（在 `FinishEntryRow` 里赋值）——此前只记了"行容器"，按条目控制行显隐
   必须先拿到行本体。

**踩到的两个坑（已处理）**
- **打开顺序**：`ModBody.rows` 里**同时含分组标题行**，所以"先按命中打开分组标题、之后再整块隐藏所有行"
  会被后者盖掉。正确顺序是**先整块隐藏、再按模式打开**。
- **状态联动**：过滤进行中时，展开/折叠会走 `ToggleMod` → `ReapplyInnerVisibility`，把"三层展开态"的常规
  显隐盖到过滤结果上（搜索后点开一个 mod 就会看到过滤被冲掉）。已在 `ReapplyInnerVisibility` 开头加分支：
  有搜索词时改为重算过滤（空词分支不会回头调用它，故无递归）。

**待复测**：① 输入关键词实时过滤；② 清空后完整恢复（分组状态不变）；③ 搜索状态下点开 mod 不冲掉过滤。

### 2.5 ModManager `er2.modmanager` v1.5.25（分组默认态**按玩家选择还原为收起**；与恢复的展开记忆配套）
**v1.5.25（2026-09-26，玩家"为什么现在 mod 子文件夹都是默认打开的"）**
玩家在 1.5.24 之后**实际手动收起了 17 个分组**（状态文件里 17 条 `c\t` 行）——这个行为本身就是答案：
他偏好原来的"默认收起"。确认后还原。

**背景（两版之间的取舍记录）**
v1.5.23 把分组默认态从"收起"翻转成"展开"，是为了绕开"点开 mod 只看到 4~7 行分组标题"的现象。
但那个现象的真因是 **v1.5.20 关掉了展开记忆**（记忆一关，每次启动都退回从零开始的收起态）；
记忆在 v1.5.22 已恢复，所以"默认收起"不再有当时的副作用——**展开过的分组会被记住**。
**"默认收起 + 记忆开启"**才是原始设计意图的正确组合。

**实现**：`expandedSections`（记"展开的分组"）语义还原；持久化行 `c\t` → `s\t`；v1.5.22~24 写下的
`c\t` 行**忽略**（在新语义下等于默认行为，玩家那 17 条的结果与他的意图一致）；下次保存时文件被整体
重写，旧行自动清理。

**教训**：**改一个"默认值"之前，先确认它当初为什么是那个值。** v1.5.23 的翻转发生在"真因已由另一处
修复（记忆）"之后，属于多余的补偿性修改，结果被还原一次——白改一轮。

### 2.5 ModManager `er2.modmanager` v1.5.24（lazyBuild **默认关闭并标记不推荐**；修掉其插入锚点缺陷）
**v1.5.24（2026-09-26，玩家"还是打不开"）**
新判据（`activeSpan` + `visible`）一次定案：
```
state mod 'ER2 Bullet Penetration' rows=81 active=31
      activeSpan=1955..3054    ← 31 行已激活（"分区默认展开"生效了）
      visible=0..642           ← 屏幕只显示内容顶部 642px
```
**内容存在、也激活了，但整段落在列表底部 1955~3054px 处**，而玩家看的是顶部 → 他描述的"打不开"。

**根因（`已构建` 行的恒等式）**
```
insertAt=27  containerChild=44  (rows=17)     ← 27  = 展开前的 childCount
insertAt=63  containerChild=74  (rows=11)     ← 63  = 展开前的 childCount
insertAt=193 containerChild=274 (rows=81)     ← 193 = 展开前的 childCount
```
`containerChild = insertAt + rows` **恒成立** ⇒ `insertAt` 永远等于"展开前容器的子物体数" = **末尾**。
即 `BuildModBody` 的兜底分支（`titleRow == null` → `insertAt = childCount`）被走到 → **新行被追加到列表最后，而不是标题下方**。
`body.titleRow` 之所以失效：它由 `container.GetChild(container.childCount - 1)` **猜**出来，依赖"这一刻刚建的标题行就是容器末位"这个隐含假设；引用一旦漂移/失效就落空。
**修法**：改用 `titleTxt.transform.parent`（标题自身的父级 = 该 mod 的标题行），不可能漂移；诊断行补 `before` 与 `titleRow=NULL/ok`。

**止损决定（重要）**
`lazyBuild` **改为默认关闭并标记"暂不推荐"**（代码与诊断保留）。理由：同一现象已第三次复现（v1.5.9 / v1.5.19 / v1.5.23），而它换来的只是"进页 0.3 秒 → 0.06 秒"。**可用性 > 300 毫秒。** 本轮修复在游戏内确认通过后，再议是否恢复默认。
玩家侧影响：进 MODS 页恢复约 0.3 秒；展开回到一直稳定的"原地显隐"（不插入行）。

**两条可复用教训**
1. **别用"容器的最后一个子物体"当锚点**：它只在"刚好就是你要找的那一个"时成立，属于隐含假设。要引用对象就用对象自身的关系（`transform.parent`）。
2. **诊断要给出"区间"而不是"数量"**：本轮 `activeSpan`（激活行在容器内的偏移区间）与 `visible`（当前可见区间）并排一看就定案；此前只打 `rows`/`active` 数量，三次都看不出"内容其实在屏幕外"。
3. **反复制造同类故障的优化应当回退，而不是继续叠修复**：三次同类、两轮未确认，成本已超过收益。

### 2.5 ModManager `er2.modmanager` v1.5.23（**"点开 mod 没内容"真因：分区默认收起 × 展开记忆被关**；1.5.22 顺带修诊断本身）
**v1.5.22 / v1.5.23（2026-09-26，玩家"还是没内容"）**
日志给出决定性数字：`toggle mod 'ER2 Unit Inspector' -> open rows=67`，但 `state ... rows=67 active=4` —— **建了 67 行、只有 4 行是激活的**。那 4 行是**分区标题**，配置项全部折在下一层。

**根因（两半，其一是自己引入的回归）**
1. **分区默认收起**（v1.5.0 的设定）：从没动过分区的 mod，展开后必然只显示 4~7 行分区标题 → 玩家读作"没有内容"。
2. **v1.5.20 把 `rememberExpanded` 默认改成 false**：当时依据"这点记忆只在重启后才有意义"的质疑。误判在于——它同时是"哪些 mod 展开 / 哪些分区被手动收起"的**唯一持久化来源**；关掉后每次启动都从零开始，于是连"以前展开过分区、所以能直接看到配置项"这条退路也一起没了。

**修法**
- **分区语义反转**：`expandedSections`（记"展开的"、其余一律收起）→ **`collapsedSections`（记"被手动收起的"，其余默认展开）**。持久化行 `s\t` → `c\t`；旧 `s\t` 行直接忽略（新语义下它就等于默认行为）。
- `rememberExpanded` **恢复默认 true**（默认值改动必须手工改玩家 cfg —— BepInEx 只在新建 cfg 时写默认值）。
- 折叠能力与记忆都保留：主动收起的分区会保持收起；**分区标题行不在 `sb.rows`**（`secStart` 在 `AddSectionHeader` 之后取），所以收起后仍能点开——这条在动手前先确认过。

**1.5.22 顺带修掉的诊断缺陷**：`LogToggleState` 用 `GetWorldCorners` 判定 `onScreen`，而**本机 IL2CPP 下 `Vector3[]` 出参回不来**（几何诊断里所有对象的 `worldY` 都打印成 `0..0`，我曾据此估算"行落在屏幕外"，方向是错的）。改为在 content 局部坐标系计算：可见区间 `[(1-vnp)*(contentH-viewportH), +viewportH]`、行偏移 `-anchoredPosition.y`；并新增 `activeSpan`（激活行在容器内的偏移区间）——**"展开后只看到标题"的正确判据是 `active` 数，不是行数**。

**教训（§0 级）**
1. **"只有重启后才需要"不等于"没用"**：一个持久化开关若同时是某块内存状态的**唯一来源**，关掉它会改变每次运行的冷启动行为，而不只是"跨重启"。
2. **诊断手段本身也要验证**：`GetWorldCorners` 恒返回 0 这件事让我两轮都在错误方向上估算可见性。探针读数必须先对照已知量自证可信，再用于推断。
3. **"默认收起"这类设计要连同"第一次看到什么"一起复核**：默认收起 + 记忆丢失 = 玩家看到一个空 mod。

**待复测**：展开任意 mod 应直接看到配置项（日志 `state ... active=` 应接近 `rows`，而不是 4~7）。

### 2.5 ModManager `er2.modmanager` v1.5.21（**偶发闪定案**：行内缩状态翻转时同帧重排整列；并修延迟建页"点开没内容"）
**v1.5.21（2026-09-26，两个问题，都有日志/代码证据）**
玩家反馈：① lazyBuild 下"mod 展开后不出内容"；② "之前的选项闪烁 bug 又出现了，但触发概率不再是 100%"。

**① 偶发闪 = 行内缩状态翻转 → 同帧强制重排整列（`ApplyScrollbarInset`）**
`row inset writes=3~4 in last 5s (target right=24.0, content 450) / (target right=8.0, content 433)` —— 原生在"预留滚动条 17px / 不预留"两种 Viewport 状态间来回切换，目标值随之在 24↔8 跳；旧代码每次 `changed` 都写 sizeDelta 并 `ForceRebuildLayoutImmediate(crt)` → **整列行在同一帧重排 = 闪一下**，且只在翻转那一下发生（概率非 100% 的由来）。
关键观察：**行宽几乎不变（426 ↔ 425）**——补偿本身是对的，错的是"补偿时顺手强制重排"；真正需要的只是让 LayoutGroup 知道宽度变了，那件事 Unity 在正常布局阶段会自己做。
修法：强制重排只在 **`float.IsNaN(lastInsetW)` 的首次应用**保留（建页后几何未定型时需要），之后改 `MarkLayoutForRebuild`。

**② 延迟建页"点开没内容" = 重建只覆盖一层（`RelayoutContainer`）**
诊断（v1.5.19 布）证明：行已建（rows=11/119/81/17/70/28）、已激活（active=5~8）、父链干净（`inactiveInHierarchy=0 firstInactiveParent=ok`）、内容高度也增长了（1644→1806）、**无任何异常**——但行世界坐标整体落在**负值区**（`rowY=-1722..-381`；正常路径对照值是 `rowY=129..1060` 正值）。
根因：`RelayoutContainer` 只 `ForceRebuildLayoutImmediate(container)`。container 带 `ContentSizeFitter`（高度按 preferred 自设），**同时它又是 contentPage 的子物体、位置与高度受 contentPage 那层布局支配**——宿主那层没重算，新行就落在可见范围之外。
修法：完整重建链 = 重建 container → `Canvas.ForceUpdateCanvases` → 重建 **contentPage**（宿主层）→ `Canvas.ForceUpdateCanvases` → `SelfHealScroll`；并 `MarkPendingRelayout()` 在 **0.12s 后补排一次**（嵌套布局里的 ContentSizeFitter 常需一帧落定），挂在既有 `PollControls` 每帧链上（`PollPendingRelayout`）。
⚠️ 注意：因为"没有重排"不等于"看得见"，本轮把**同帧（`relayout`）与下一帧（`deferred`）两份几何快照**都写进日志（`LogLazyGeometry`：canvas scale/size、contentPage、viewport、container 的 rect + 世界 Y + anchoredY + childCount），下一轮一次判定。

**可复用教训（写入 §0 级）**：**"重建容器"≠"重建布局"** —— 带 ContentSizeFitter 的容器同时是父级的孩子；只重建它内部会让它的**外表几何**停留在旧值，子物体虽然排好了却整体落在错误位置。凡"插行/改行数"的场合，重建必须覆盖**容器 + 其宿主**两层。

### 2.5 ModManager `er2.modmanager` v1.5.20（展开状态记忆**默认关闭**——玩家质疑成立；并更正 1.5.19 的实测结论）
**玩家原话**："说实话，玩家都重启游戏了，这点展开页的保存真的有必要吗。" —— **他说得对，默认关掉**（功能与开关都保留，想用随时开）。三个理由：
1. **会话内本来就不会丢**：`expandedMods` / `expandedSections` 是静态集合，翻页、重开设置菜单都在——落盘只对"重启游戏之后"有意义，收益仅"少点几下展开"。
2. **与延迟建页互相抵消**：被记住展开的 mod 必须在**进页那一刻**就建出正文（`Combat Tweaks` 一次 70 行、`Limb Tweaks` 28 行），那正是 `lazyBuild` 要省掉的开销；默认开着 = 每次进页替它付账。
3. 附带：页面开局状态变得不可预测（自己这一局没点开过的 mod 也展开着）。
**注意**：BepInEx 只在**新建 cfg** 时写默认值，已存在的 cfg 会保留旧值 → 已直接改玩家 cfg 的 `rememberExpanded = false`。

**同时更正 1.5.19 的实测结论**：延迟建页**一次都没跑起来**——日志零 `[MM-lazy]` 行、建页仍 945 行 / 276~354ms，cfg 实测 `lazyBuild = false`（玩家把 `debugLog` 打开了，但没打开这个实验开关）。
**教训**：**交付"实验开关"时，能由机器直接改配置就不要让玩家去 UI 里找**——本轮已直接写入 cfg（`lazyBuild = true` + `debugLog` 保持 true），下轮启动即可产出诊断。

版本 1.5.20（两处同步，grep 复核）；编译 0/0；部署 sha256 `C7DE5799…` == bin；四份发布文档同步（变更条目 + 功能说明标注"默认关闭"）。
**待实测**：下轮日志应出现 `[MM-lazy] 已构建 …` 与 `[MM-lazy] 状态 …`，且 `page built in` 应从 ~300-430ms/945 行降到预计 <150ms/约 300 行；展开任意 mod 应正常显示内容。

### 2.5 ModManager `er2.modmanager` v1.5.19（进页顿挫：**延迟建页放进开关后面**，并把 v1.5.9 那个谜所需的量全部写进日志）
**玩家实测（1.5.18）**："进设置页时顿一下。" 日志已量化：`page built in 288~432ms - rows=942 mods=28 watches=242 extras=258`——**每次进 MODS 页建 942 行、0.3~0.4 秒**；一局建了 3 次（含一次 `reason=container-missing` 的自愈重建）。这是 v1.5.0"建页一次建好、点击只原地显隐"的既定代价。

**做法（不盲改，双轨交付）**
① **新增 cfg `Ui / lazyBuild`（默认关闭）**：折叠的 mod 只建标题行，正文**首次展开时补建**。实现：把建正文抽成 `BuildModBody(body)`（常规路径与延迟路径共用）；`ModBody` 重新加上 `plugin/container/titleRow/built`（v1.5.12 曾当脚手架删掉，如今是真实功能）；`FillContent` 按开关决定建不建；`ToggleMod` 首次展开补建。**插入点现查标题行的当前下标**（v1.5.10 教训）、**先收集对象引用再搬位置**（`rows` 登记对象而非下标）。
② **诊断**：开关打开时，每次构建输出
```
[MM-lazy] 已构建 '<mod>' 正文: rows=N expanded=… insertAt=… containerChild=…
[MM-lazy] 状态 '<mod>': containerActive=… containerH=… preferred=… rows=… inactiveInHierarchy=… firstInactiveParent=… [0]y=…,h=…,act=…
```
把 v1.5.9"点开没反应"所需的**每个量**都打出来：层级可见性（含父链上第一个停用节点——v1.1.9 的 STUCK-EVIDENCE 就是"建在停用父级下"）、布局有没有算过（`rect` 高 vs **偏好高**）、行是否堆在同一点（浅色值框叠成白块的成因）。配合既有的 `LogToggleState`（按行报告 active/onScreen/rowY/vnp）即可三分法定位：行没激活 / 行在视口外 / 行在视口里却没画。

**为什么默认关闭**：v1.5.9 该方案上线后出现"点开没反应"（日志证明回调到达、rows 非空、无异常，内容却不可见），**谜未解开前不设为默认**。这次把它变成"开关 + 诊断"的可测量实验，而不是又一次盲赌。

**通用教训**：**降低开销的改动若落在既有故障区，必须先能观测再动**——开关与诊断一起交付，让玩家一次测试既验证新路径、又给旧谜提供证据，而默认行为一字不动（回归风险为零）。

版本 1.5.19（两处同步，grep 复核）；编译 0/0；部署 sha256 `739D2DC4…` == bin；四份发布文档同步（含 `Ui / lazyBuild` 说明与变更条目；顺带修掉上一轮漏改的 README 版本头 1.5.17→1.5.19）。
**待实测**：开 `Ui / lazyBuild` + `Debug / debugLog` → 进 MODS 页，`page built in` 应明显下降（预计 <150ms、行数约 300）；展开任意 mod 应正常显示；若仍"点开没反应"，`[MM-lazy]` + `LogToggleState` 两组日志会直接指出是三种里的哪一种。

### 2.5 ModManager `er2.modmanager` v1.5.18（元信息行排版修复：**单行定高 + 实测宽度截断**）
**玩家截图反馈**："第一次进入，元信息行排版不好看。" 截图里那行 `ER2 AI FOOD · ER2.AIFOOD · V1.4.0 · 3 SETTINGS` 又高又宽，把下面的条目整体下推。
**根因**：v1.5.17 图省事复用了给"配置项说明"写的 `AddDescriptionRow`——而它**按字符数估高**（`ceil(宽/400)+1` 行，最少 2 行、另加余量），说明文本可以换行、这个估法没问题，但**元信息行只该是一行**，于是它高了一倍；宽度也用了说明行的 `+18px` 缩进与 `Wrap` 模式。
**修法（1.5.18）**：
① 新增 `AddMetaRow`：**定高 18px、字号 12、`MiddleLeft` + `Overflow/Truncate`**，左内缩 = `LabelLeft + RowIndent`（与行标签对齐）、右内缩 = `ControlRight`（避开滚动条）；
② 超长用**实测宽度**（`MeasureTextWidth`）二分截断 + 省略号，不用字符数猜；
③ 新增 `BuildModMetaShort`：UI 行**不再重复标题已有的 mod 名**（那部分占了它大半长度），只留 `GUID · v版本 · N 项设置`；完整信息（全名/GUID/版本/条目数）仍进"复制全部"。
**通用教训**：**同"行"不同用途不要共用同一个测量/布局函数**——说明行可以换行、可以按字符数粗估；单行信息行必须定高 + 实测宽度截断。复用省下的那点代码，代价是玩家一眼就看得出的排版错误。
版本 1.5.18（两处同步，grep 复核）；编译 0/0；部署 sha256 `1B8DD78A…` == bin；四份发布文档同步。

### 2.5 ModManager `er2.modmanager` v1.5.17（四项体验改进：缩写白名单 / 尊重第三方 CM 属性 / mod 元信息 / 展开状态持久化）
**本轮（2026-09-26，"继续做 ModManager"）清掉 1.5.5 之后的收尾清单**（**搜索过滤仍未做**——它需要新增控件，值得独立一轮单独验证）。**⚠ 后续更正（v1.7.0）：玩家明确要求撤除搜索功能（"不要搜索功能"），此项作废**；ModManager 当前只剩两项纯工程项：第三方"假页"探测通用化、拆 5225 行单文件。
1. **缩写白名单**（`HumanizeKey`）：原实现逐词 Title Case → AI→Ai、HUD→Hud、UI→Ui、FOV→Fov、`MG42`→"Mg 42"。现在用 `KnownAcronyms` 白名单保持大写，并把**"缩写 + 紧随数字"合并成一个词**（MG+42→MG42、M+1→M1）。
   **判据刻意用白名单而不是"整词全大写就保留"**：配置文件里 SCREAMING_SNAKE_CASE 很常见，后者会把 `MAX_COUNT` 变成 "MAX COUNT"（比 Title Case 更不像标签）。
2. **尊重第三方 `ConfigurationManagerAttributes`**：`Browsable = false` 的配置项不再列出（第三方 mod 用它藏内部/调试项）；`Order` 生效。**全部反射读取**（`CmAttributes` / `EntryBrowsable` / `EntryOrder`），既不引入编译期依赖、也不要求对方真的装了 ConfigurationManager。
   **只有至少一项声明了 Order 才重排**——否则保持 cfg 绑定顺序（我们自己的 mod 都是刻意排过序的，无差别按字母重排是倒退）。全部项都被隐藏时回退为"没有可调设置"行（否则展开后空无一物）。
3. **mod 元信息行**（`BuildModMetaText`）：展开 mod 后第一行 = **全名 · GUID · 版本 · 条目数**；"复制全部"头部同款——报 bug 只需截图或贴文本即可定位到具体版本。
   **踩坑**：`PluginInfo.Metadata.Version` 的类型是 BepInEx 的 `SemanticVersioning.Version`，本工程未引用该程序集 → 直接 `.ToString()` 编译报 **CS0012**；改为反射取 `Version` 属性（`GetValue` 返回 `object`，不把该类型嵌进元数据即可通过）。
4. **展开状态持久化**：新 cfg `Ui / rememberExpanded`（默认开）+ 小文件 `BepInEx/config/er2.modmanager.expanded.txt`（按行 `m\t<mod 全名>` / `s\t<mod 全名|分区>`）。启动 `LoadExpandedState()`，每次展开/折叠 `SaveExpandedState()`。
   **刻意不写进 cfg**：状态是一长串内部名，出现在设置页的文本框里既难看又容易被误编辑。

版本 1.5.17（`BepInPlugin` + 启动日志两处 **先 grep 实际值再替换、替换后再 grep 复核**——本轮特意避开 1.5.15/16 与 UniGen 2.5.40/41 那两次 sed no-op 的坑）；编译 0/0；部署 sha256 `023DCF2C…` == bin；四份发布文档（EN/CN × README/Nexus）已同步 1.5.17。
**待复测**：① 标签显示（AI / HUD / MG42 / M1 等）；② 展开记忆（重开游戏仍保持展开）；③ 元信息行与"复制全部"头部；④ 若装了声明 `Browsable=false` 的第三方 mod，其内部项应从列表消失。

### 2.5 ModManager `er2.modmanager` v1.5.16（翻页音效定案：**延迟播音**；版本号归正 + 发布文档补齐）
**1.5.14 → 1.5.16（2026-09-26，翻页音效从无声到恢复）**
- **1.5.14**（诊断）：`PlayClick` 的空 catch 加 `LogWarning`——所有翻页分支都调了 `ClickSound` 且 trace 带 `[sound]`，唯一能吞掉线索的就是那个空 catch。结果探针**零输出**：调用成功、无异常。
- **1.5.15**（第一次修）：把播音挪到页面动作之后（`EnterMyPage` 重建后 / `LeaveModsBackward`、`WrapToFirstPage` 切页后）——**仍无声**。
- **1.5.16**（定案）：**延迟播音**——`ScheduleClick()` 入队 0.25 秒，`PollScheduledClick()` 在 `PollControls` 每帧轮询补播。玩家确认音效恢复（"声音正常了"）。
  **机制**：整页重建（`FillContent` 上千个对象）在一帧内冲击 UI/音频侧，紧跟着的 `ClickSound()` 会被吞掉；而**原生页间翻页有声**（玩家做的对照实验）证明 `ClickSound` 本身在设置菜单里有效，问题只在时机。
  **通用教训：任何"先播音再做破坏性 UI 操作"都不可靠**——播音必须排在"最后一个可能销毁音频对象/冲击音频系统的操作"之后，必要时延迟一帧以上（本项目 ModManager 与 UniGen 各踩过一次）。
- **版本号归正**：源码早已含 1.5.16 的修复，但 `BepInPlugin` 与启动日志的版本字符串停在 **1.5.14**（两轮 sed 的源串与文件实际值不匹配 → 静默 no-op）→ 已改为 1.5.16 并重新编译部署（sha256 `0E945768…` == bin）。
  **教训：批量改版本号必须先 grep 实际值再替换，替换后立即 grep 验证**（本项目在 UniGen 2.5.40/41 上踩过完全相同的坑，两次都是"代码已新、版本号还旧"）。
- **发布文档补齐（本轮"清文档债"）**：
  - ModManager：`README.txt` / `Nexus_description.md` 从 1.5.13 → **1.5.16**（补 1.5.14–1.5.16 变更史）；**新建中文版 `README_CN.txt` / `Nexus_description_CN.md`**——此前 ModManager 是唯一没有中文文档的发布 mod，CN 包用户拿到的是全英文说明（长期缺口，本次填上）。
  - UniGen：中文版 `README_CN.txt`（2.5.34）/ `Nexus_description_CN.md` 同步到 **2.5.46**（上轮只同步了英文版，属遗漏）。
  - 已核实 **SquadCommand 四份发布文档实际都已是 v1.4.56**（AGENTS.md 快照里"仍停在 v1.4.38"是过时信息，已更正）——本轮无需补。

### 2.5 ModManager `er2.modmanager` v1.5.13（**闪白真凶定案：HitArea 初始白色一帧**——录屏逐帧实锤）
玩家提供了 4.7s 录屏（`屏幕录制 2026-09-25 231737.mp4`）。**逐帧分析（ffmpeg 4fps 全片 + 30fps 转场段 + 每帧平均亮度）**：
```
h001-h013: 亮度 ~125（原生 SETTINGS 页）
h014-h015: 亮度 343.7/343.5 ← 【闪白帧，暴涨 2.7 倍，持续 2 帧 ≈ 67ms】
h016-h036: 亮度 ~97（MODS 页正常）
```
**闪白帧内容**：MODS 页每个 mod 标题行上盖着**全行宽白色矩形**、mod 名被压在下面——形状正是每行的 `HitArea`（透明点击区：`Image.color=white` + Button ColorBlock 压 alpha）。
**机制（终于想通，也解释了所有历史现象）**：`ApplyRowHoverTint` 设置 ColorBlock（normal=透明）**只是存储配置、不立即应用**；`AddComponent<Button>()` 那一刻 `OnEnable` 用**默认 ColorBlock（normal=不透明白）**把 CanvasRenderer 染成白色；我们的透明 normal 要等**第一次状态变化**（鼠标划过该行）才被应用 → **凡"创建行/重新显示行"的时刻（进 MODS 页、翻页重建、点击展开），HitArea 白色显形 1-2 帧**。这同时解释了：v1.3.1 的"点击闪烁光污染"（当时修的是常驻灰带，初始白帧问题潜伏至今）、v1.5.0 的"点击后全部选项闪烁变白"、以及用户"闪烁和点击选项后的闪烁完全相同"（本就是同一机制）。
**修法（一行）**：`ApplyRowHoverTint` 配置完 ColorBlock 后立即 `img.canvasRenderer.SetColor(cb.normalColor)`——创建那一刻就是透明的，不再依赖 OnEnable/状态变化链（该链在 IL2CPP 下不可靠）。
**方法论教训（高价值）**：**录屏 + 逐帧亮度分析**是定位"瞬时视觉故障"的正解——4fps 全片定位区间 → 30fps 细查 → 帧均值一拉，闪帧立刻现形（125→344）。此前 6 轮静态排查全都错过它，因为这只存在于**某一帧的渲染状态**里，任何"读代码/读布局日志"都看不到。
版本 1.5.12→1.5.13（四处同步）；编译 0/0；部署 sha256 `26488F32…` == bin。
**待复测**：录屏同样的操作路径再看——进 MODS 页与点击展开都不应再有白帧。卡顿（"进游戏固定时间后卡几秒"）由 UniGen 2.5.35 探针（`GetAllItemsOfType >150ms` Warning + 补漏重扫总耗时）下一轮日志定案。

### 2.5 ModManager `er2.modmanager` v1.5.12（**回退延迟构建与滚动锚定**，换上能一锤定音的诊断）
玩家："还是闪烁，这个闪烁和点击选项后的闪烁完全相同。mod 选项完全不能展开（**箭头动了，但是打不开**）"。
**日志定案（决定性）**：`toggle mod 'ER2 AI Food' -> open rows=10` / `'ER2 Battlefield Commander' -> open rows=118` —— **点击到达了、回调执行了、行集合非空、无任何异常**，但内容看不见。且"打不开"从 v1.5.9（延迟构建）才开始出现。
**决策：回退两处未经验证的改动**：
① **回退 v1.5.9 延迟构建**（`FillContent` 恢复"建页一次建好、点击只原地显隐"；`BuildModBody` 与 `ModBody` 的 plugin/container/titleRow/built 一并删除——按"开发脚手架用完即删"约定，死代码不留；两个关键坑已记入本台账备查）。理由：日志证明它以"功能正确性"换"翻页速度"，而 1.5.10 起翻页卡顿的真因（每帧 inset 重写）已另行修复，这个有风险的下策不再必要。代价：翻页成本回升（几十 ms 级），可接受。
② **移除 v1.5.11 的滚动锚定**（`CaptureScrollOffset/ApplyScrollOffset` 停用）。它是在问题未理解时叠加的未验证猜测，且本身可能移动视口（与"打不开"症状吻合）。
**换上能一锤定音的诊断 `LogToggleState`**：每次展开后输出 `state mod 'X' rows=118 active=118 onScreen=118 rowY=.. viewportY=.. vnp=.. contentH=..`——直接区分三种失败：**行没激活（active=0）/ 行在视口外（onScreen=0）/ 行在视口里却看不见（onScreen>0，渲染/遮挡/尺寸问题）**。上一轮只有 rows=N，三种情况无法分辨，又白测一轮。
**教训（已入台账，本次连犯两次同类错）**：在根因未定案前，**不要叠加未验证的修复**——每叠一层，下一轮排查就多一个变量（本轮"打不开"就分不清是延迟构建还是滚动锚定）。正确顺序：先修确认的回归，其余用诊断锁定后再动。
版本 1.5.11→1.5.12（四处同步）；编译 0/0；部署 sha256 `B4270914…` == bin；cfg `debugLog=true`。
**待复测**：① mod 选项应恢复正常展开（回退预期）；② 若仍闪，看 `state ...` 日志——active/onScreen/contentH 三个数直接指向根因。

### 2.5 ModManager `er2.modmanager` v1.5.11（闪动机制定案：滚动位置用归一化值导致整屏位移）
**证据（v1.5.10 实测日志）**：`row inset writes=0/1/5 per 5s` → 行内缩抖动**已止住**（不再是每帧 ~300 次）；`page built in 35.8ms - rows=46 mods=28 bodiesBuilt=0` → 延迟构建生效（28 个 mod 只建 46 行）。但**每次点击后都紧跟一条 `content height jump`**（1644→1784→1644→1856）。
**根因（本次定案）**：Unity `ScrollRect` 的 `verticalNormalizedPosition` 是**归一化**值。展开/折叠改变 content 高度后，同一个归一化值对应的**绝对位置就变了** → 整屏内容瞬间位移 = 玩家看到的"闪"（所以他才说"和点击选项后的闪烁完全相同"——两者本就是同一机制）。
**修法**：新增 `FindScrollRect / CaptureScrollOffset / ApplyScrollOffset` —— 在展开/折叠**之前**记录"视口顶部距内容顶部的**像素**偏移"，布局刷新**之后**按新高度换算回去。挂到三处：`ToggleMod`（含延迟构建）、`ToggleSection`、`ToggleEntry`（最高频）。
**"点了不展开"取证**：新增 `LogToggle(what, name, now, rows)`（debugLog 门控，每次操作一条），用来区分两种可能——① 点击根本没到达回调；② 回调到了但 `rows` 为空。日志形如 `toggle mod 'X' -> open rows=69`。**静查已排除**：`ToggleMod / ToggleSection / ReapplyInnerVisibility / RelayoutContainer` 逻辑完整，`BuildModBody` 也确实构建成功（日志有 `built body of 'ER2 Combat Tweaks' (69 rows, deferred)`），所以只剩"点击没到达"与"行被别的路径关掉"两种可能，靠这一条日志即可分辨。
版本 1.5.10→1.5.11（四处同步）；编译 0/0；部署 sha256 `67A601A6…` == bin；cfg `debugLog=true`（取证用）。
**待复测**：① 点击展开时内容不再整体位移（闪是否消失）；② 日志里应出现成对的 `toggle mod/section/entry ... -> open rows=N`，若点某处**完全没有 toggle 日志** → 点击没到达（射线/层级问题）；若有日志但 rows=0 → 行集合为空（构建或登记问题）。

### 2.5 ModManager `er2.modmanager` v1.5.10（修 v1.5.9 回归 + 二修选项闪动）
**① 修 v1.5.9 引入的回归（玩家："点击这个选项但是展开另一个选项"）**：延迟构建用 `body.titleIndex`（**建页时**记录的静态下标）定位插入点。一旦有别的 mod 先展开、在它前面插入了若干行，**它下面所有 mod 的旧下标全部错位** → 行被插到别的 mod 名下 → 点 A 却展开了 B 的位置。**修法：改成插入时现查标题行的当前下标**（`titleRow.transform.GetSiblingIndex()`，标题行对象不会变），并加兜底（算出的下标早于本批起点时不往前插）。`ModBody` 字段 `titleIndex`（int）→ `titleRow`（GameObject）。
**② 二修选项闪动（每帧重排）**：`ApplyScrollbarInset` 的写入判据是 `|crt.sizeDelta.x + w| > 0.5`——**与"当前实际值"比**。而容器尺寸归父级布局（contentPage 的 LayoutGroup）管，我们写进去的值**下一帧就被它算回去** → 判据每帧都成立 → 每帧重设 sizeDelta/anchoredPosition 并 `ForceRebuildLayoutImmediate(crt)` → **整列行每帧重排 = 选项持续闪动**。**修法：判据改为与"我们上次写入的目标值"比**（`lastInsetW/lastInsetP`）——目标没变就一个字都不动，不再被别人的覆盖带着反复重写。新容器时重置（FillContent 里置 NaN）保证首次必写。
**③ 诊断形态修正（重要教训）**：上一版把 inset 日志做成"5 秒节流一条"，**节流恰好把最关键的信息——"每帧写了几次"——遮住了**（这已是第二次犯同类错误）。改为**计数聚合**：每 5 秒输出 `row inset writes=N in last 5s`；N≈1~2 为正常（不再抖动），N≈300 说明仍在每帧重写。
**教训（已入台账）**：① 延迟/按需插入行时，**任何"建页时记下的下标"都会在后续插入后失效**，必须存对象引用、用时现查下标；② **判定"要不要写"要跟自己的意图比，不要跟被别人改过的当前值比**——后者在有父级布局/Another 控制器时必然抖动；③ **节流只解决"刷屏"，会丢掉频率信息**；要判断"是否每帧发生"，用计数聚合而不是节流。
版本 1.5.9→1.5.10（四处同步）；编译 0/0；部署 sha256 `B8EEF04E…` == bin；cfg 置 `debugLog=true`（只为了取 `row inset writes` 这一条证据，就位后关掉）。
**待复测**：① 展开折叠的 mod 不再串位；② 日志里 `row inset writes=` 应为个位数；③ 选项是否还闪（若还闪，说明还有第三条每帧路径，下一步针对每帧路径逐一计数）。

### 2.5 ModManager `er2.modmanager` v1.5.9（选项闪动 + 翻页卡顿，两个根因都来自日志证据）
玩家反馈（本轮关键）："**从始至终都是选项在闪动**" + "翻页还未响应"——第一次明确了症状位置：**闪的是选项、不是翻页/不是整页重建**。结合 1.5.8 的日志，两个根因都定位到：
**① 选项闪动 = 高度被写到错的对象上（振荡）**：`SelfHealScroll(contentPage, force)` 的调用点（`ToggleEntry` / `ToggleMod`→`RelayoutContainer` / `ApplyEntryDirtyUi`→`RelayoutRowsOf`）**一直传的是 `MM_Container` 而不是 contentPage** → 函数末尾 `target.sizeDelta` 写到了容器自己身上，而容器挂着 `ContentSizeFitter` → 写入值下一帧被 Fitter 算回真实值覆盖 → **高度来回跳**（日志实锤：`content height jump 1644 -> 1784 (force=True)` 紧接着 `1784 -> 1644`），每次跳变整列行重排 = 选项闪动。**修法：在 `SelfHealScroll` 入口统一纠正**（传进来是 `MM_Container` 就改用其父级 contentPage）——比逐个改调用点更不容易漏。
**② 翻页不响应 = 每次进页都重建全部条目**：`FillContent` 对每个 mod **无条件** `FillModEntries + AddFooterRow`（v1.5.0 为消灭白闪定的"正文始终建好"），折叠内容也照建 → 二十个 mod ≈ 上千 GameObject/次翻页。**修法：延迟构建** —— `ModBody` 增加 `plugin/container/titleIndex/built`，折叠的 mod 只登记标题行；`ToggleMod` 首次展开时调 `BuildModBody(body)` 只建这一个 mod 的行（并按 `SetSiblingIndex(titleIndex+1+k)` 插到标题之后；**先收集行对象引用再搬**，rows 登记的是对象不是下标，所以改顺序不影响登记）。展开过的仍然原地显隐，不违反 v1.5.0 的"点击不重建"。
**③ 新增建页成本日志 `page built in Xms - rows=.. mods=.. bodiesBuilt=.. watches=.. extras=..`**（**不受 debugLog 限制**，5s 节流）——玩家报的是性能问题，这条是判定延迟构建收益的证据。
**顺带**：`debugLog` 已改回 `false`（玩家归因日志；且关键日志本就不受门控）。
版本 1.5.8→1.5.9（四处同步）；编译 0/0；部署 sha256 `C7B27D62…` == bin。
**待复测**：① 建页耗时与 `rows` 数应显著下降（翻页应跟手）；② 不应再出现成对的 `content height jump`（A→B 再 B→A）——若还出现，说明还有别的路径在写错对象；③ 展开一个折叠过的 mod 时看是否正常（延迟构建的行序、分区层级、滚动高度）。

### 2.5 ModManager `er2.modmanager` v1.5.8（白闪**真因**：MODS 页左翻被交还原生）
**v1.5.8（2026-09-25，白闪定案）**：再次实读 `LogOutput.log`，这次盯的是**翻页分支序列**：
```
tab left:wrap-mods  cur=0 myIndex=4   ← 第一页左翻进入 MODS（我们接管 ✓）
tab left:pass-native cur=4 myIndex=4  ← 在 MODS 页按左键 → **交还原生**（×2 次）
tab right:enter-mods cur=3 myIndex=4  ← 原生把它丢回第 3 页，玩家再右翻才回 MODS
```
**根因**：`TabLeftPatch` 只处理了 `cur == myIndex + 1` 与 `cur == 0` 两种情形，**`cur == myIndex`（正站在 MODS 页按左箭头）没有任何分支** → 一路掉到末尾的 `left:pass-native` 并 `return true` → 原生 `SettingsTabLeft` 拿着**越界的 cur=4**（原生只认识 0..3，MODS 是我们追加的末页）执行左翻 → 原生按未知状态**重填整个设置面板** = 整屏重绘 = 玩家看到的那记白闪。每次离开 MODS 页必现。
**修法（与"末页右翻进入 MODS"完全对称）**：新增 `LeaveModsBackward(s)` —— `RestoreScrollAnchors()`（v1.2.1 教训：不还原会把坏高度泄漏进原生页）→ `currentOpenedMenu = myIndex - 1` → 用容错的 `CallUpdateOpenedMenu(s, true, false)` 切回原生最后一页 → `return false` **拦住原生**。音效与音效规则同 `WrapToFirstPage`；离开前 `ThirdPartyPage.Detach()`。
**补证据缺口**：会话重开那条 `OpenMyPage`（设置界面重开时重建）**此前没挂探针** → v1.5.7"没有重建"的结论实际只覆盖了巡检那一条路，证据不完整。已补 `LogRebuild("session-reopen")`。
**教训（两条，已入台账）**：① 追加型页面必须在**每个翻页入口**都自洽处理"正站在自己页上"的情形，漏一个就是"越界交还原生"；② 判定"某类事件没发生"之前，先确认探针覆盖了**该类事件的全部路径**——只覆盖一条就下结论，等于用半个证据定案。
版本 1.5.7→1.5.8（四处同步）；编译 0/0；部署 sha256 `025A028E…` == bin；cfg 仍置 `debugLog=true`（遗留待玩家定位后关闭）。
**待复测**：离开 MODS 页（左箭头）时不应再有 `left:pass-native`，应出现 `left:leave-mods`；若日志里还能看到 `rebuilding MODS page ... reason=session-reopen`，说明重开设置时也在闪，下一轮针对它。

### 2.5 ModManager `er2.modmanager` v1.5.7（白闪取证版，**无行为改动**）
**v1.5.7（2026-09-25，白闪：第二次否定，转"取证优先"）**：玩家复测 v1.5.6 仍报"还是有白闪"。**关键动作：直接读 `<Game>\BepInEx\LogOutput.log` 取证**（不必等玩家贴日志——本机就能读），结果是：
- **`ModManager: rebuilding MODS page` 一条都没有** → **v1.5.6 的"每帧自愈误杀短列表"假设被证伪**：整页重建根本没发生。白闪另有来源。
- 静态复核排除两项：① 行 `HitArea` 的 `Image.color=White` 看似可疑（铺满整行的一大块白），但 `ApplyRowHoverTint` 的 normal/selected/disabled **全为 alpha=0**、悬停 0.035 → 不可见；② `AddEntryActions` 内含 `OpenMyPage` 但**无任何调用点**（v1.4.0 起死代码）。
- 于是改为"取证优先"，加两个**只在 debugLog 下运行**的探针：
  ① `ProbeStackedRows(cont, where)`（挂在 `SelfHealScroll` 里所有强制重排之后 —— ToggleMod/Section/Entry 最终都会经过这里）：统计可见行里 anchoredPosition.y 相同的最大行数，若 `>=3 且 ≈全部可见行` → 判定"行被摆在同一位置"并打 LogWarning。**这就是白闪的物理成因**：某一帧行位置未定型、浅色值框互叠成一大块白。日志形如 `rows stacked at one y after 'SelfHealScroll' - visible=.. sameY=.. containerH=..`。
  ② 把 `row inset applied`（原先**每帧一条**）节流到 5 秒一条 —— 玩家一旦开 debugLog，这条会把日志灌满、反而看不到别的信息。
- 版本 1.5.6→1.5.7（四处同步）；编译 0/0；部署 sha256 `06585DFD…` == bin；**手动写入 cfg 把 `Debug/debugLog` 置 true**（build.ps1 每次会清 cfg），取证结束后由玩家在 MODS 页或 cfg 里关掉。
- **待复测**：重启游戏 → 操作到白闪 → 我看 `LogOutput.log`（本机可直接读）。判定路径：出现 `rows stacked at one y` → 摞行成立，往下查为什么那一帧 layout 没定型；不出现 → 白闪与我们的重排无关，需截图做像素测量定位。

### 2.5 ModManager `er2.modmanager` v1.5.6
**v1.5.6（2026-09-25，追查残留白闪）**：玩家实测 feedback"除了残留白闪其他正常"（三项新功能验收通过）。按"不猜、先取证"的原则查剩余重建路径，比对全部 `OpenMyPage` 调用点后锁定 **每帧自愈的"布局死"判定**（`InjectPollPatch.Postfix` 尾部）：
① **根因（误杀短列表）**：判定只看绝对高度——`容器 rect.height <= 101 && content rect.height <= 201` 即判"布局从未算过"→ 重建。但 `FillContent` 里 content 高是 `Math.Max(200f, pref+8f)`，**content 高恒 >= 200 → 该条件恒真**；于是只要列表"真的短"（装的 mod 少、或大部分折叠），容器高就容易落在 101 以内 → 被判死 → **每帧 OpenMyPage 重建 = 白闪**（熔断只在 0.5s 窗口限流，表现为连闪一小波）。**修法：改判"需求与实际的落差"** —— 只有 `LayoutUtility.GetPreferredHeight(ours) > rect.height + 8`（要的比画出来的高得多）才算真没算出来；preferred 本身就小 = 合法短列表，不再误杀。
② **每次重建都留下证据**：新增 `LogRebuild(reason)`，**不受 debugLog 限制**（重建 = 玩家看得见的故障），同原因 5 秒节流一条，带 `reason=layout-dead(containerH=.. contentH=.. preferred=..)` / `container-missing` / `empty-container(rows=0)` 与 burst 计数 → 下次再闪可以直接读日志定位，不用猜。
③ **顺带补料**：`OpenMyPage` 建页后补一次 `Canvas.ForceUpdateCanvases + ForceRebuildLayoutImmediate(容器)`，让首帧布局先落定、不再被巡检误判；`SelfHealScroll` 增加"内容高跳变 >= 40px"诊断（debugLog 门控 + 节流），用来区分"重建造成的闪"与"重排造成的闪"。
④ 排除项：`AddEntryActions`（内含 OpenMyPage）**无任何调用点**，是 v1.4.0 起的死代码，与本次无关。
版本 1.5.5→1.5.6（BepInPlugin + 启动日志 + README + Nexus 四处同步）；编译 0 warning 0 error；已部署，`plugins\ER2_ModManager.dll` 与 `bin\Release\net6.0` sha256 一致（`BA5B5C96…`）。**待玩家复测**：看 `LogOutput.log` 里 `ModManager: rebuilding MODS page ... reason=` 是否还出现——出现则按 reason 追下一条原因；不出现说明白闪已不由重建引起，需截图做像素测量定位到高度/重排侧（届时有 `content height jump` 日志兜底）。

### 2.5 ModManager `er2.modmanager` v1.5.5
**v1.5.5（2026-09-25，可靠性三件套：改动标记 / 单项还原 / 重启提示 + Reset 二次确认）**：用户选题动的是"改了什么、改了算不算、怎么找回"这层（此前 ModManager 只是个渲染器）。四处落地：
① **改动标记**：`IsDirty(cfg, entry)` 用 `GetStaged(...) != DefaultValue` 判定（必须走 GetStaged——`ApplyAndSave` 之前 `BoxedValue` 还没变），改动过的项标签尾部加 `" •"`。**标记必须是纯文本**：v1.5.0 已实证这些 Text 上富文本 `<color>`/`<size>` 不生效、会原样打印标签串，所以放弃了彩色富文本方案。为此给 `FitRowLabel` 加 `suffix` 参数——**标记不参与截断**（否则 Ellipsize 会把 suffix 割掉）。
② **单项还原**：新增 `AddRestoreRow`（左灰字显示"默认值 X"，右侧"还原默认"按钮），行体与简介行一样**一次建好、只切 SetActive**（不敢动态插行——`ModBody.rows`/`SectionBody.rows` 是建页时按 childCount 区间登记的，事后插行会漏登记、串层级）。可见性条件 = `dirty && VisibleNow()`，已并入 `ReapplyInnerVisibility`。
③ **标记/还原的实时刷新**：关键洞察是 **`StageValue` 是所有写值的唯一入口**（含按键捕获、下拉、开关、滑条、输入框、None 按钮、单项还原），所以只在它末尾调 `RefreshEntryDirtyUi` 就覆盖全部路径；内部用 `ex.dirty` 缓存做比较，**只在状态翻转时才动 UI**（滑条每帧 StageValue 但不会重复布局）。
④ **单项还原不重建页面**：新增 `ApplyValueToControls(cfg, entry, val)` 把值写回 Toggle/Slider/InputField/Dropdown（下拉下标用 `DropdownIndexFor`，与 PollControls 的写回互为逆运算）以及热键行的 `valueButton`（热键控件没有 watch）。**一条都没命中时打 LogWarning**——界面显示旧值这种事必须能被看见。据此把 `ResetModSettings` 与热键 `None` 按钮也从"整页重建"改成原地更新，**消灭了 v1.5.0 之后残留的最后两处白闪源**。
⑤ **"需重启生效"提示**：`RequiresRestart(entry)` 扫 mod 自带简介里的 restart/reboot/重启/重开游戏/下次启动（宁宽不紧——漏判才是问题）→ 该项简介行追加提示；`FlushAllStaged` 在 **ApplyAndSave 之前**（清 staged 就查不到了）统计这批改动里的重启项，用原生 `Corvostudio.UI.Hint.Display` 提示数量，日志列出具体项 `ModManager: saved, N change(s) require a game restart to take effect: ...`。
⑥ **Reset all 二次确认**：新增 `ArmItem`/`armedResets` + `IsResetArmed/ArmReset/DisarmReset/PollArmedResets`（挂载在既有 `PollControls` 每帧链里），第一下只把按钮文案换成"确认重置?/Confirm?"，3 秒未确认自动撤销；页面重建会销毁旧 Text，`PollArmedResets` 用 `Equals(null)` 判活并清理。
**顺带修掉的陈年 bug**：`SetupControl` 在 `FitRowLabel` 之后又无条件 `t.text = label`，把**截断结果覆盖回未截断原文** → v1.5.0 声称的"省略号截断"在原生模板路径上从未生效（长标签照旧压到值控件上）。改为只在 FitRowLabel 没写入时兜底赋值。另删掉 `EntryExtras.actionsRow`（v1.4.0 起的死字段）消除 CS0649。
版本 1.5.4→1.5.5（BepInPlugin + 启动日志 + README + Nexus 四处同步）；已构建部署，`plugins\ER2_ModManager.dll` 与 `bin\Release\net6.0` sha256 一致（`63A97034…`）。**未实测**（游戏未运行），待验证点：改动标记随输入实时出现/消失、"重置全部"两段式手感与 3 秒撤销、含 restart 的项改后退出时的 Hint。

**v1.5.4（2026-09-24，翻页音效被第三方吞掉）**：玩家接着报"翻页到 mod 管理器页面时没有音效了"。**根因不是我们漏播，而是原生音效被第三方 Prefix 连带吞掉**：`SettingsGUI_V2.SettingsTabRight/TabLeft` **自身会播点击音效**（v1.1.4 已实证——我们拦截原生翻页后必须手动补 `SoundManager.ClickSound()`），而 ACM 的 `SettingsTabRight` Prefix 在原生第 3 页**恒 `return false`** → 原生不执行 → 音效随之消失，而它的 `ResponsiveOrdersNativeSettingsPage.cs`（1629 行）**全文没有任何 `SoundManager` 调用** → 第 3 页 → ACM I → ACM II 整段静音。**修法（规则化，不是打补丁）**：**凡是"这次原生不会执行"的分支，都由我们补一声；会走到原生的分支一律不补**（否则和原生音效叠成双击）。新增 `ModRegistry.TabSound()`（补音 + 分支追踪）/ `TabTrace()`（只追踪不补音），落在 6 个分支上：`right:yield-thirdparty-open`（打开它的首页）/ `right:yield-thirdparty-next`（假页 1→2）/ `left:yield-thirdparty`（从它的假页左翻）补音；`right:enter-mods` / `left:enter-mods` / `left:wrap-mods` / `right:wrap-first` 由 `EnterMyPage`/`WrapToFirstPage` 内部已有的 `ClickSound` 覆盖；`right:pass-native` / `left:pass-native` 交给原生。追踪日志形如 `ModManager: tab <branch> cur=.. myIndex=.. thirdParty=open/2|closed|none`，实测一眼看出走的哪条分支。版本 1.5.3→1.5.4（BepInPlugin + 启动日志 + README + Nexus）。

**v1.5.3（2026-09-24，第三方原生设置页共存）**：玩家来报想让本 mod 与 **Advanced Combat Movement 1.2.2**（GUID `AdvancedCombatMovement`，内含 Responsive Orders / Slower Vehicles）共存。反编译比对补丁目标后的定案：它**在原生第 3 页劫持 `SettingsGUI_V2.SettingsTabRight` 并恒 `return false`**，而它的 DLL 按字母序（A < E）先于我们加载、同优先级下先执行 → 它的补丁先跑并短路 → 我们追加在最末的 MODS 页**再也无法用右箭头翻到**（只剩"第一页左翻绕回"这一条旁路）。**修法（反射桥 `ThirdPartyPage`，无编译期依赖）**：① 我们的 `TabRightPatch`/`TabLeftPatch` 提到 `[HarmonyPriority(Priority.First)]` 先判状态；② 它的假页（`CurrentFakePage` 1=主设置 / 2=续页，都是 **public static 字段**，必须 `GetField`）停在末页时右翻 → 交给我们 `EnterMyPage`；③ 它的假页还没到末页 → 放行走它自己的翻页；④ 停在它的入口页（原生第 3 页）→ 让位，否则我们抢先接管、它的页面永远打不开；⑤ 交接/离开时用 `Detach()` 把它的 `IsOpen=false`/`CurrentFakePage=0` 复位（不清会有两个后果：从 MODS 页左翻被它的 `TabLeft` 抢走、以及下次误判"还在它的末页"而跳过它的页面）。未安装该 mod 时 `Present=false`，原有行为一字不变。版本 1.5.2→1.5.3（BepInPlugin + 启动日志 + README + Nexus）。
**v1.5.2（2026-09-18 发布：删掉自锁的开发开关 NativeFull + 整文件删除 NativePage.cs）**（下略，见下一段完整原文）

### 2.5x ModManager `er2.modmanager` v1.5.2（历史）
**v1.5.2（2026-09-18 发布：删掉最后一个开发开关 NativeFull —— 自锁陷阱）**：v1.5.1 把 `NativeFull` 判为"文案正常、功能可用"的**备案例外**保留了下来，**当天即被推翻**：用户"不小心把 NativeFull 打开了，现在 mod 管理器不能使用了"。根因是**自锁** —— `NativeFull` 是 `er2.modmanager` 自己的配置项，因此**会出现在 ModManager 自己的 MODS 页上**：打开它就把那一页弄坏，而关掉它**只能靠那一页** → 界面上无法自救，只能手改 cfg。**修法：删除 `NativeFull` 开关（字段 + `Config.Bind` + `FillContent` 分支 + 每帧清理里的 `nativeMode` 分支），并整文件删除 `NativePage.cs`**（它只被这条路径引用：`BuildAndRender` + `PruneExtras`），MODS 页从此只有一条建页路径。**教训（已写入 AGENTS.md §7.1 判定标准）：任何会改变"你用来操作它的那个界面"的开关，默认关是不够的 —— 它必须不存在。判定时多问一句"这个开关打开后，我还有办法关掉它吗？"答不上来就删。**
**v1.5.1（2026-09-18 发布：删除遗留 PoC 测试页 + 接入 debugLog 开关）**：玩家反馈 "mod menu is broken. with 'poc'"，MODS 页只剩四行 —— `poc.header`（空白框）/ `poc.toggle`（拨不动）/ `poc.slider`（拖不动）/ `poc.button`（写着 click、点了没反应）。**根因不是第三方 mod 的新方法，而是 ModManager 自己的开发期脚手架 `NativePage.RenderPoc`**：硬编码上述四行，标签是没走 `HumanizeKey` 的生 key，三个回调只写 `LogInfo` 不干事；`AddHeaderRow` 又是用空 `content` 的 `SettingButton` 冒充标题 → 所以是"空白框"。它随发布版一起发了出去，cfg 里 `NativePoc=true` 即命中。**本机 cfg 为 `NativePoc=false`，所以复现不出**（这就是"我试了没问题"的原因）；报告里连按钮的 "click" 文案都逐字对上，可排除第三方 mod 巧合。**修法：删除 `RenderPoc` / `DumpStructure` / `CompSummary` 与 `NativePoc` 开关（字段 + `Config.Bind` + `FillContent` 分支 + `nativeMode` 引用），发布版不再可达 —— 不是靠"默认关"兜底。** 同时按新约定接入 `Debug`/`debugLog` 开关（默认 `false`）+ `Plugin.DebugOn` 属性，把建页/模板/取值/chainloader/native row 五处诊断 `LogInfo` 收进开关（`LogError`/`LogWarning` 不门控）。版本 1.5.0→1.5.1，四处同步（`BepInPlugin` + 启动日志 + README + Nexus）。**教训：开发脚手架用完即删，绝不能靠"默认关"兜底发版**（已写入 AGENTS.md §7.1 强制约定）。
**v1.5.0 发布状态（2026-09-12 23:27 定稿）**：部署 = **英文构建**（`DefaultChinese=false`，玩家要求），`ER2_ModManager.dll` 与 `bin\Release\net6.0` sha256 一致（`980989EA…`）；两个发布包 `C:\Users\71011\Downloads\ER2_ModManager_v1.5.0.zip`（EN）/ `ER2_ModManager_CN_v1.5.0.zip`（CN，`DefaultChinese=true`），拆包核对均为 **DLL + README.txt + Nexus_description.md**。**注意：ModManager 没有 `README_CN.txt`/`Nexus_description_CN.md`，CN 包内的文档仍是英文**（build.ps1 缺文件时回退英文）——如需中文文档要另写。发布前**已清掉全部临时诊断**（LAYOUT/SCROLL dump），README/Nexus 的 v1.5.0 变更说明已覆盖全部修复项。
**v1.5.0 追加修复（玩家实测反馈，按要求不升版本号）**：① **展开配置项后名字消失**——`ToggleEntry` 里把整行标签 text 直接**覆盖**成箭头（`ex.arrow.text = "▾  "`），名称与范围富文本全被抹掉，且因为原地切换不重建页面所以"重新打开也不显示"。修法：`StripArrowPrefix` 剥掉旧前缀再拼新前缀（名称保留）。**教训：原地切换行的"前缀指示"必须做前缀替换，绝不能覆盖整行文本。** ② **分区默认收起**（玩家要求；我上一版误改成默认展开，已改回 `expandedSections` 缺省收起，`NativePage` 同步）。③ **分区箭头不反映展开状态**——`ToggleSection` 只切了行的显隐、没更新标题箭头 → `AddSectionHeader` 改为返回标题 Text、存进 `SectionBody.title`，切换时用 `StripArrowPrefix` 重拼 ▸/▾。④ **分区与配置项只有颜色差异** → 分区标题加 ▸/▾ 箭头、分隔线加深（0.10→0.16）、行高 30→32，其下配置项**再缩进一级**（RowIndent +12）。
**v1.5.0（发布版：点击白闪根治 + 层次区分 + 清诊断）**：玩家反馈"点击后全部选项都会闪烁变白"——根因是**任何点击都会整页重建**（mod/分区标题走 `OpenMyPage`），重建那一帧新行重排、浅色值框瞬间叠在一起 = 白闪。修法：**所有正文一次建好、点击只原地 SetActive**——`ModBody`/`SectionBody` 行集合登记表（FillContent 里按 `container.childCount` 区间收集，收起时整体 `SetActive(false)`），`ToggleMod`/`ToggleSection` 改为原地显隐 + `ForceRebuildLayoutImmediate` + `SelfHealScroll(force)`，**页面上再无任何"点击即重建"路径**。另：mod 名下方加 1px 横线（同时把 mod 名与首字母分组拉开层次），首字母分组降为 12px 更暗的字；**删掉 v1.2.1 引入的临时诊断**（`LogLayoutSnapshot`/`LogScrollChain` 两个方法与全部调用点，`WorldRect` 保留给 `ApplyScrollbarInset`）。发布包：`ER2_ModManager_v1.5.0.zip`（EN，当前部署）+ `ER2_ModManager_CN_v1.5.0.zip`（CN），拆包核对 = DLL + README.txt + Nexus_description.md ✓。
**v1.4.0（按玩家原则重做视觉：简洁优先、每个元素都要有用）**：玩家原话"我想要简洁，而不是随意堆砌，每一个元素应该有它的作用，而不是只为了好看"，并逐项点名了 值框样式 / 标题·分隔线·徽章太吵 / 行距·缩进·密度 / 仍有灰条底块 / 配色对比度。据此**删除纯装饰**：字母分组行的渐隐线、分区标题的项数徽章与箭头、mod 标题的 hash 彩色条（`AccentColor` 保留但不再使用）、每行开头的 ▸（只在展开时留一个 ▾）、**每项的【重置】【复制】按钮**（mod 末尾"复制全部/重置全部"已覆盖同一用途，`AddEntryActions` 保留但不再调用）。**收敛配色**：两级文字色 `TextPrimary(0.86)` / `TextDim(0.52)` + 浅色值框 `ValueFill(0.74)`/`ValueText(0.10)`，无强调色、无常驻底色。**压缩密度**：行高 38→32、间距 4→2、缩进 14→10、控件列 220×30→200×28、分区标题 48→30、字母行 28→24。**页脚动作**从灰底按钮改纯文字按钮（`MakeSmallTextButton`），改键控件改"值框样式"（新增 `MakeValueButton`：浅底深字 + 悬停提亮）。**保留（都有信息作用）**：分区标题下极淡分隔线（分组）、悬停高亮（可点性反馈）、点击音效、点行看简介（信息按需）。
**v1.4.0（按玩家原则重做视觉：简洁优先、每个元素都要有用）**：玩家原话"我想要简洁，而不是随意堆砌，每一个元素应该有它的作用，而不是只为了好看"，并逐项点名了 值框样式 / 标题·分隔线·徽章太吵 / 行距·缩进·密度 / 仍有灰条底块 / 配色对比度。据此**删除纯装饰**：字母分组行的渐隐线、分区标题的项数徽章与箭头、mod 标题的 hash 彩色条（`AccentColor` 保留但不再使用）、每行开头的 ▸（只在展开时留一个 ▾）、**每项的【重置】【复制】按钮**（mod 末尾"复制全部/重置全部"已覆盖同一用途，`AddEntryActions` 保留但不再调用）。**收敛配色**：两级文字色 `TextPrimary(0.86)` / `TextDim(0.52)` + 浅色值框 `ValueFill(0.74)`/`ValueText(0.10)`，无强调色、无常驻底色。**压缩密度**：行高 38→32、间距 4→2、缩进 14→10、控件列 220×30→200×28、分区标题 48→30、字母行 28→24。**页脚动作**从灰底按钮改纯文字按钮（`MakeSmallTextButton`），改键控件改"值框样式"（新增 `MakeValueButton`：浅底深字 + 悬停提亮）。**保留（都有信息作用）**：分区标题下极淡分隔线（分组）、悬停高亮（可点性反馈）、点击音效、点行看简介（信息按需）。
**v1.3.1（玩家反馈三连修：丑 / 点击闪烁光污染 / 无音效）**：截图逐像素量出"整页灰带"= v1.3.0 给每行铺的 HitArea 底色（mod 标题 rgb39 vs 条目 rgb30，底色其实是面板近黑 rgb3）→ `ApplyRowHoverTint`：**normal/selected 一律 alpha=0**，只在悬停 0.035 / 按下 0.06 短暂提亮。"点击全屏闪烁"= 每次点配置项都 `OpenMyPage` 整页重建，且 `ClearContent` 用 `Destroy`（帧末生效）→ 旧行与新行**同帧叠着画** → ① 配置项展开改**原地 SetActive 切换**（`EntryExtras` 登记简介行，点行只 ForceRebuildLayoutImmediate，不重建页面）；② `ClearContent` 先 `SetParent(null)` 再 `Destroy`。"没音效"= 自建点击区没声 → `PlayClick()`（`SoundManager.ClickSound`）接进 `MakeFooterButton`/`MakeSmallTextButton` 包装与 `ToggleMod`/`ToggleSection`/`ToggleEntry`。
**v1.3.0（排版改造 A+B 档，玩家拿别的 mod 设置页当参考提的需求）**：核心是**统一右列**——`ControlWidth / ControlHeight / ControlRight` 三常量控制，所有值控件（数值框、下拉、开关、改键按钮）右对齐成同一列（以前 200/280/原生模板各不同，参差）；行改**单行**（标签左、控件右），范围提示从"独占一行"降级为标签尾部的富文本小灰字（`<color=…><size=13>`，Unity Text 支持富文本）；mod 标题展开内容缩进（`RowIndent` 全局缩进位）。**坑（当场修掉）**：`PlaceLabel` 一开始只用 `offsetMin/offsetMax` 表达——水平轴是拉伸锚点没问题，但**垂直轴是固定锚点，两个 offset 都设 0 会把行高压成 0，标签直接不可见**；必须"水平用 sizeDelta/anchoredPosition、垂直用 sizeDelta.y"。另：`Selectable.fadeDuration` 在这个 interop 里没暴露，别设。
**v1.3.1（玩家反馈三连修：丑 / 点击闪烁光污染 / 无音效）**：截图逐像素量出"整页灰带"= v1.3.0 给每行铺的 HitArea 底色（mod 标题 rgb39 vs 条目 rgb30，底色其实是面板近黑 rgb3）→ `ApplyRowHoverTint`：**normal/selected 一律 alpha=0**，只在悬停 0.035 / 按下 0.06 短暂提亮。"点击全屏闪烁"= 每次点配置项都 `OpenMyPage` 整页重建，且 `ClearContent` 用 `Destroy`（帧末生效）→ 旧行与新行**同帧叠着画** → ① 配置项展开改**原地 SetActive 切换**（`EntryExtras` 登记简介行/操作行，建页时一次建好，点行只 ForceRebuildLayoutImmediate，不重建页面）；② `ClearContent` 先 `SetParent(null)` 再 `Destroy`。"没音效"= 自建点击区没声 → `PlayClick()`（`SoundManager.ClickSound`）接进 `MakeFooterButton`/`MakeSmallTextButton` 包装与 `ToggleMod`/`ToggleSection`/`ToggleEntry`。另按参考图把值框改成**浅底(0.78) + 深色数字(0.10)** + 顶部高光 + 底部暗边。
**v1.3.0（排版改造 A+B 档，玩家拿别的 mod 设置页当参考提的需求）**：核心是**统一右列**——`ControlWidth=220 / ControlHeight=30 / ControlRight=8`，所有值控件（数值框、下拉、开关、改键按钮）右对齐成同一列（以前 200/280/原生模板各不同，参差）；行改**单行 38px**（标签左、控件右），范围提示从"独占一行"降级为标签尾部的富文本小灰字（`<color=#6E6E78><size=13>`，Unity Text 支持富文本）；分区标题改参考图样式（16px 粗体 + 标题下 1px 全宽分隔线 + 右侧项数徽章 + 上方留白，行高 48）；字母分组行降为 14px 灰字 + 右侧渐隐细线；**降噪**：每项的简介与【重置】【复制】收进 `expandedEntries`（点该行才展开，行首 ▸/▾ 指示），列表本体每项只占一行；mod 标题改**卡片头**（4px 哈希色条 + ▸/▾ + 展开内容缩进 14px 形成父子层次，`RowIndent` 全局缩进位）；所有行加**悬停高亮**（整行透明 HitArea `SetAsFirstSibling()` 垫底 + Button ColorTint，控件在上会先吃掉点击，所以点值框不会误触展开）。
**坑（v1.3.0 踩到并当场修掉）**：`PlaceLabel` 一开始只用 `offsetMin/offsetMax` 表达——水平轴是拉伸锚点没问题，但**垂直轴是固定锚点，两个 offset 都设 0 会把行高压成 0，标签直接不可见**；必须"水平用 sizeDelta/anchoredPosition、垂直用 sizeDelta.y"。另：`Selectable.fadeDuration` 在这个 interop 里没暴露，别设。
把 mod 设置页**嵌进游戏原生设置界面**（`SettingsGUI_V2.Update` / `SettingsTabRight` / `SettingsTabLeft` patch）：自动枚举 `IL2CPPChainloader.Plugins` 渲染全部配置项（折叠分区 + 小字简介直接用 mod 自带 BepInEx 描述原文）。
**v1.3.0（排版改造 A+B 档，玩家拿别的 mod 设置页当参考提的需求）**：核心是**统一右列**——`ControlWidth=220 / ControlHeight=30 / ControlRight=8`，所有值控件（数值框、下拉、开关、改键按钮）右对齐成同一列（以前 200/280/原生模板各不同，参差）；行改**单行 38px**（标签左、控件右），范围提示从"独占一行"降级为标签尾部的富文本小灰字（`<color=#6E6E78><size=13>`，Unity Text 支持富文本）；分区标题改参考图样式（16px 粗体 + 标题下 1px 全宽分隔线 + 右侧项数徽章 + 上方留白，行高 48）；字母分组行降为 14px 灰字 + 右侧渐隐细线；**降噪**：每项的简介与【重置】【复制】收进 `expandedEntries`（点该行才展开，行首 ▸/▾ 指示），列表本体每项只占一行；mod 标题改**卡片头**（4px 哈希色条 + ▸/▾ + 展开内容缩进 14px 形成父子层次，`RowIndent` 全局缩进位）；所有行加**悬停高亮**（整行透明 HitArea `SetAsFirstSibling()` 垫底 + Button ColorTint，控件在上会先吃掉点击，所以点值框不会误触展开）。
**坑（本次踩到并当场修掉）**：`PlaceLabel` 一开始只用 `offsetMin/offsetMax` 表达——水平轴是拉伸锚点没问题，但**垂直轴是固定锚点，两个 offset 都设 0 会把行高压成 0，标签直接不可见**；必须"水平用 sizeDelta/anchoredPosition、垂直用 sizeDelta.y"。另：`Selectable.fadeDuration` 在这个 interop 里没暴露，别设。
把 mod 设置页**嵌进游戏原生设置界面**（`SettingsGUI_V2.Update` / `SettingsTabRight` / `SettingsTabLeft` patch）：自动枚举 `IL2CPPChainloader.Plugins` 渲染全部配置项（折叠分区 + 小字简介直接用 mod 自带 BepInEx 描述原文）。
**v1.2.5（内缩纠正提速）**：用户实测"宽深色缝过 1 秒左右才恢复正常" = 建页那一瞬原生 Viewport 宽度/滚动条位置还没定型（量成 450 就缩 24，随后原生定到 433），而纠正只能等 `SelfHealScroll` 那一拍 0.5s 节拍。既然内缩公式已无反馈（基准 contentPage），就改成**每帧重算**（`RefreshRowInset`，在 `InjectPollPatch` 的 MODS 页分支里逐帧调用）→ 当帧纠正。
把 mod 设置页**嵌进游戏原生设置界面**（`SettingsGUI_V2.Update` / `SettingsTabRight` / `SettingsTabLeft` patch）：自动枚举 `IL2CPPChainloader.Plugins` 渲染全部配置项（折叠分区 + 小字简介直接用 mod 自带 BepInEx 描述原文）。
**v1.2.4（内缩改成绝对目标，收尾）**：v1.2.3 的**单调锁存**在原生切回"收窄态"时行宽短掉 25px（用户截图**逐像素实测**：灰色名称栏右缘 1849 vs 图一正常的 1874，露出一条深色缝）。改为**无反馈的绝对目标**：`desiredLocal = (contentPage 右缘 − (滚动条左缘 − 6 世界像素)) / lossyScale.x`，`Mathf.Clamp(…, 8, 60)` + 行宽 ≥200px 安全阀。基准是 contentPage（不随我们的写入变化）→ 原生在"`Viewport 433` 预留 / `Viewport 450` 不预留"两态间来回切时，本地内缩自动给出 8 ↔ 24，而**行右缘的世界坐标恒定 ≈1875**：既不压住滚动条，也不短一截、更不振荡。**方法学沉淀：这类"按外部几何调自己布局"的逻辑，目标要用绝对量（世界坐标）表达，基准必须不被自己改动影响；单纯加锁存/滞后只能压住振荡，换个原生状态就变成另一种错。**
**截图逐像素取证法（本次新增，值得复用）**：DSH 把用户贴的图存到 `~/.dsh/attachments/v1/objects/<sha前2>/<sha>`（原始分辨率，本机 1918x1092），用 `System.Drawing` 逐点扫同一 y 的 RGB 跳变即可量出"行右缘 / 滚动条左缘 / 手柄区间"的确切像素——**比肉眼比对截图可信得多**，三次迭代的结论都靠它定的。
**v1.2.3（内缩闪烁根治）**：v1.2.2 的 `ApplyScrollbarInset` **拿容器自己的当前 rect 当基准量重叠** → 缩进去之后就算不出重叠 → 下一帧退回基础值 8 → 24/8 每帧横跳（用户实测"一直重叠与不重叠之间切换/闪烁"，日志里 9 轮交替实锤）。修法：① 基准改为**固定参考**（`contentPage` 右缘 − 基础内缩，不随我们改动变化）；② 内缩值**按页面会话单调锁存**（只增不减，建页时 `ResetInsetLatch` 重置），原生在"预留/不预留滚动条"两态间切换也不会来回跳；③ 滚动条查找不再要求 `activeInHierarchy`（隐藏时原生不收窄 Viewport，但它随时会显示，按矩形位置一律预留）；④ 单次额外内缩上限 26 本地像素（滚动条竖条约 20px，超过必是过场瞬态）。**教训：任何"测量 → 写回几何"的自愈逻辑，测量基准必须是**不被自己改动影响**的量，否则就是振荡器。**
**v1.2.2（"滑条压住 mod 名称栏"根治，含实测几何证据）**：原生只在部分路径把滚动条那 17px 收窄（日志实测正常态 `Viewport sd=-17` → Viewport 433、容器右缘世界 x=1875 vs 滚动条 1882 = 间隙 7px；**"退出设置再进去"那条路径不收窄** → `Viewport 450`、容器右缘 1900 与滚动条区间 1882..1912 **重叠 18px**，而滚动条此时 `active=1`）→ 名称栏灰色背景被滚动条及其背景盖住。修法 `ApplyScrollbarInset`：按滚动条实际世界区间算容器内缩（`over = 容器右缘 - 滚动条左缘 + 6` 世界像素，除以 `lossyScale.x` 换回本地单位；基础内缩左 0/右 8 与原硬编码等价），带 200px 安全阀。**方法学：先加几何 dump（LAYOUT/SCROLL），用世界坐标重叠量定位，再改代码——不要靠肉眼猜"重叠"。**
**v1.2.1（2026-09-12 游戏更新适配）**：① **原生方法签名变更踩雷**——游戏把 `SettingsGUI_V2.UpdateOpenedMenu(bool)` 改成 `UpdateOpenedMenu(bool setFirstButtonSeected, bool preservePosition)`、`FillSettingPage(bool)` 改成 `FillSettingPage(bool, bool restorePosition, float selectedY, float scrollbarValue)`（另加 `GetSettingRow(Transform)`/`lastScrollbarValue`）。旧编译产物里 `UpdateOpenedMenu(true)` 的调用点运行时解析不到方法 → `MissingMethodException` **从 Harmony Prefix 逃逸**（异常发生在 JIT 解析调用点，前缀里的 try/catch 拦不住）→ 整个 Prefix 失败、原生 `SettingsTabRight` 一并中断 = **按右翻页键不能翻页**。现改为**反射按名 + 参数个数自适应**调用（未来再加参数也不会复发）。**教训：游戏更新后凡是"编译期直接调用原生方法"的点都要复查签名，翻页/设置这类核心交互优先反射或 `AccessTools` 按名取。** ② 行高归一化（`Templates.Instantiate` → `NormalizeRowHeight`）：行容器是 `VerticalLayoutGroup(childControlHeight=false)`，uGUI 取的行高是 `sizeDelta[1]`，克隆来的原生行若带垂直拉伸锚点/零 sizeDelta → 行高 0 → 所有行叠在同一点。③ `FillSettingPage` 也纳入 MODS 页拦截（原生重开设置会直接起这条协程恢复滚动位置，不经 `UpdateOpenedMenu`）。④ 重进设置界面前先 `RestoreScrollAnchors()` 再重建（用户在 MODS 页直接关设置时 `Update` 停跑，锚点/高度还原没有机会执行 → 坏高度泄漏）。**注意**：两套渲染实现（`Plugin.cs` 的 MODS 页 / `NativePage`）**分支必须同步维护**，新类型支持两边一起加。
**零维护设计（v1.1.0 起）**：无任何硬编码翻译表/mod 名单，新 mod（含第三方）加载即自动适配。类型自动映射：bool→开关；float/int→**数值输入框 + 范围提示**（非滑条）；string+`AcceptableValueList`→下拉；热键（KeyCode / string 键名含 key·toggle 且选项像按键表 / **枚举类型**）→**点击改键按钮 + `Input.GetKeyDown` 捕获**；未知类型→跳过不崩。
**显式保存语义**：改动只进 `staged` 暂存字典，【保存】才写 `BoxedValue` + `cfg.Save()`；`SaveOnConfigSet=false` 防运行中自动落盘（**BepInEx 退出时会自动保存全部 cfg**，所以必须暂存机制才能实现"按下才保存"）。
**其他**：热键冲突检测（页面顶部黄色警告）；每项独立【重置】【复制】+ 按钮文字闪烁反馈（不依赖延迟的 Hint）；字母分组视图；空列表卡死熔断器；`DefaultChinese` 编译期常量区分 EN/CN 构建。
**注意**：两套渲染实现（`Plugin.cs` 的 MODS 页 / `NativePage`）**分支必须同步维护**，新类型支持两边一起加。

### 2.6 ThrowableWheel `er2.throwablewheel` v1.3.6
替换原生手雷转盘（长按 G）为自定义投掷物列表 + 每 2s 自动补货。**关键**：投掷物必须以**正确子类直接 `inv.items.Add(vi)`** 注入（`AddVirtualItem`/`AddItemToInventory` 都会降级成基类 `VirtualItem`，原生转盘按 `VirtualThrowable` 过滤会无视）；显示/选择/投掷全走**原生管线**。
**配置**：`Enabled` / `ReplaceWheelContent` / `RefillBeforeWheelOpens` / `AllowInMultiplayer` / `LoadoutPreset`（自定义/经典14/扩充34/全部48/反坦克/燃烧/烟雾）/ `ItemIds`；每 tick `RebuildItemIds()` 热重载。
**投掷物 ID 全集**：`er2items.manifest` 的 `Prefabs/grenades/` 48 个（清单见 guide §4）。子类启发式 `MakeVirtualItem`：`smokegrenade_*`/`mle1916_smoke`/`nhg42`→Smoke；`at_*`/`*_at`/`tankmine*`/`satchel`/`geballte`/`*_hhl`/`*_rpg`→AT；其余→Grenade。

### 2.7 CombatTweaks `er2.combattweaks` v1.2.2
① **单机友军伤害关闭**（子弹/近战/爆炸/炮击/撞击全覆盖；联机走 `PhotonNetwork.IsConnectedAndReady && !OfflineMode` 判离线）；② 弹药倍率/无限（火力点/火炮/防空/自行火炮/坦克机枪/坦克主炮/飞机炸弹/步兵武器各自独立）；③ 爆炸范围倍率；④ 步兵受击伤害倍率。
**FF 拦截五层**：`BodyPart.AllowDamage`（**原生静态判定点**，同阵营→false，`Priority.First` 先短路）+ `HitPart` + `BulletInstance.OnHit` + 爆炸击飞（`RagdollManager.Ragdolize`/`AddForce` 窗口拦截）+ 跳跃窗口（`Soldier.Jump` / `OnJumpSynch` / `set_jumpVelocity` / Prefix 清 `m_JumpPower`+`jumpTimeEnd`）+ 地面吸附兜底。
**弹药入口**：炮塔 `TurretGun.ExtractOneBullet(TurretWeapon)`；步兵 `Magazine.ExtractOneBullet`/`ExtractBullets`（**不是** `GenericGun.ExtractOneBullet`，也不是 `VirtualAmmo`——后者只在换弹时消耗）；飞机 `VehiclePlane.DropBombs`；爆炸 `Explosion.CreateExplosion`。
**v1.2.2 修复**：`ExplosionFactionContextPatch` 建立友军爆炸窗口时**必须校验 responsible 阵营**，否则敌人手雷也会建立窗口 → 敌方死亡士兵倒地全被拦（"敌人被打死不倒地、站定冻结"根因）。**教训：任何"上下文窗口"类拦截，建立窗口前必须校验作用对象的身份边界。**
**已知局限**：`AllowDamage`/`HitPart` 的同阵营判定是**字符串全等**——跨国家同盟（US/UK 都是 allies）不在保护范围（玩家未报告，未动）。

### 2.8 ZoomAnywhere `er2.zoomanywhere` v1.0.1
**任意姿态/移动状态下都能屏息 + 武器放大**（原版只允许站定瞄准时屏息），可选额外放大倍率。默认键 **Left Shift**。

### 2.9 HighValueTarget（Veteran HVT）`er2.highvaluetarget` v1.2.2
**击杀归属**（`BulletInstance.OnHit` Prefix / 爆炸 `responsible` / 近战上下文 / `ItemHelmet.User` 兜底 + 玩家命中窗口 5s 防抢）+ **阈值标记**（敌 5 杀=HVT 橙标、友 2 杀=叛徒红标，阵营按 faction 的 `_` 后缀判定，跨国家同盟算友军）+ **仇恨聚焦**（覆盖 `Soldier.GetBestVisibleEnemy`，走原生目标选择）+ **标记强化**（减伤 0.6/加伤 1.4/命中 1.7/射速 1.4/移速 1.15/无视压制/不投降）+ **叛徒机制**（玩家可伤友军 + 友军集火玩家，穿透 CombatTweaks 友军保护）+ 玩家提示（Hint + 红屏 + 顶部常驻指示）+ M 大地图图标（跟随 `unitsContainer` 游戏标记，载具去重）+ **3D 头顶标记（v1.2.0）** + **载具乘员击杀共享（v1.2.0）**。
**v1.2.0（2026-09-13，玩家两点反馈）**：① **头顶标记改 3D 世界空间**——IMGUI 屏幕投影改为 billboard quad（`MeshFilter`+`MeshRenderer`，材质 `Shader.Find("Sprites/Default")`，回退 Unlit/Transparent，全缺置 `Marker3dUnavailable` 走旧 IMGUI `DrawMarkersScreen` 回退路径；quad 显式写入白色顶点色——Sprites/Default 片元色=纹理×顶点色，缺省属性不可依赖）。锚点步兵 +3.0m、载具 = 碰撞体最高点+0.9m（`VehicleTopOffset` 按载具缓存，排除 >30m 的巨型触发体）；**恒定屏占比 `scale=dist×0.03`**（约 28px @1080p/FOV60，全程无拐点）。每帧 `LateUpdate`（billboard/位置/缩放），对象池对账在 0.5s Tick（`RefreshDisplayEntries` 快照 + `ReconcileMarkers` 补建/回收）。贴图 32→128px + 3x3 超采样。门控与旧版一致：暂停/M 地图/Hide Anything 勾选/MarkerRange；**遮挡=深度测试自然消隐**（ZTest LEqual，被完全挡住时消失，无手动 raycast）。② **载具乘员合并标记 + 击杀共享**——乘员枚举走 `Soldier.GetCurrentVehicle()` + `Vehicle.seats[i].unitSet`（interop 正道，实测）；`OnUnitKilled` 归属到射手后若在载具内 → 全体乘员各 `CreditKill` 一次（等级同步提升；玩家命中窗口兜底排除已是乘员的玩家防双计）；显示层 `RefreshDisplayEntries` 把载具乘员合并为一条目（等级取乘员最高），3D 标记与 M 大地图图标共用该快照 → 一载具一枚标记。
**v1.2.0 追加修复（不升号，玩家实测三连反馈）**：① **近距离缩放不自然**——旧 `max(0.55m, dist*k)` 下限导致 18m 内标记停止缩小、屏占比膨胀；改纯 `dist×0.03` 恒定屏占比（原生 Marker3DGUI 有 `MarkerScreenSize()` 转换接口，同为恒定屏占比设计；interop 只有桩体，原生逻辑读不到，按设计意图对齐）。② **标记显示为白色菱形（根因定案）**——`GUI/Text Shader` 是**字体着色器**：RGB 取自材质 `_Color`、纹理只提供 alpha（property 名即暗示：`_MainTex ("Alpha (A)")` / `_Color ("Tint (RGB)")`），烘焙进纹理的深红/蓝底色全被无视。**教训：世界空间 quad 要显示纹理本色别用 GUI/Text Shader，用 Sprites/Default（片元=纹理×顶点色+片元内预乘，行为确定性最高）。** ③ **关闭叛徒机制仍被标叛徒**——根因是**归属记录污染**：友军伤害被友军保护拦截（不掉血 ✓）但 `RecordHit` 照写命中记录，友军稍后死于炮击/AI 互射时残留记录把死亡误归玩家 → 友军击杀 +1 → 标叛徒。修三处：`CreditKill` 友军分支顶部加 `TraitorFeature` 门控（关闭=友军击杀完全不计数不提示）；`RecordHit` 机制关闭时不记同方命中（源头堵住）；新增 `PardonTraitorsWhenDisabled` 每 tick 赦免现有叛徒状态（关闭开关=立即生效）。
**关键陷阱（实测定案）**：① `Bullet.BulletDamage` 在本作**不被调用**——入口必须实测，反编译有 ≠ 会调；② **归属必须 Prefix 记录**（瞬杀爆头的死亡发生在原生 `OnHit` 内部，Postfix 记录晚于 `Kill` 事件 → "爆头不计数"根因）；③ FF 放行的阵营改写会污染 `SideOf` 判定；④ `ForceTarget` 强制态三宗罪（鞭尸/打断射击/站桩）→ 弃用改覆盖 `GetBestVisibleEnemy`；⑤ **头盔是独立于士兵层级的 BodyPart**（`GetComponentInParent<Creature>` 为 null → 用 `ItemHelmet.User`）；⑥ **IL2CPP 托管 MonoBehaviour 必须先 `ClassInjector.RegisterTypeInIl2Cpp<T>()` 再 AddComponent**；⑦ 击杀归属表 25s 窗口 + `CountedDeaths` 防 `Kill`/`KillSynched` 双计；⑧ 坦克乘员各自击杀 → 每人头上一个标记（v1.2.0 前的老问题），乘员共享击杀后等级同步 + 显示按载具合并；⑨ **世界空间 quad 别用 GUI/Text Shader**（字体着色器：RGB=材质色、纹理只出 alpha → 纹理本色全丢变白；用 Sprites/Default，见 v1.2.0 追加修复②）；⑩ **被拦截的伤害仍污染归属记录**（友军伤害被拦不掉血但 RecordHit 照写 → 友军死于他因被误归玩家标叛徒，见 v1.2.0 追加修复③）。
**Assets**：`HighValueTarget/Assets` → 部署到 `plugins/ER2_VeteranHVT/`（build.ps1 的 `assetsDir`）。

**v1.2.1（2026-09-13，为征服模式加的公开接口）**：新增 **`public static class VeteranApi`**（`ER2VeteranHVT.VeteranApi`）——老兵系统的**公开门面**，供其他 mod 读写老兵等级/击杀。成员：`ApiVersion`(const=1) / 属性 `MaxLevel`·`KillsPerLevel`·`IsActive` / 读 `GetLevel`·`GetKills`·`IsMarked` / 写 `SetKills`·`SetLevel` / 班批量 `ApplySquadLevel(Squad,int)` / 班回读 `GetSquadKills`·`GetSquadMaxLevel`·`GetSquadAliveCount`·`GetSquadSize`。**全部方法自带 try/catch，绝不向调用方抛异常**。语义：等级由击杀推导（`EnemyKills / KillsPerLevel`，封顶 `MaxLevel`）；`SetLevel` 是"灌等级"（等级→反推击杀写入），**只应在单位刚生成、尚未参战时调用**。
**为什么需要它**：HVT 内部的 `LevelOf`/`TryGetState`/`UnitStates` 都是 `internal`，外部只能反射内部实现（不稳定，HVT 一重构就断）→ 把耦合点收敛到一个 public 类；且 HVT 自己有 `BattleEndResetPatch`（每场战斗结束重置等级），而**跨战斗的持久军队**（如征服模式）必须能把等级灌回来。**本版无玩法改动**，接口在无人调用时是惰性的。消费者：`Conquest`（见 §2.18，走 `Conquest/Game/VeteranLink.cs` 反射对接，无编译期依赖）。
**陷阱（本次踩到）**：`Squad` 的成员列表字段是 **`units`**（`Il2CppSystem.Collections.Generic.List<Soldier>`），**不是** `soldiers`；班成员数用 `CountMembers` 属性。写对接代码前先 `ilspycmd -t Squad` 核对字段名。

**v1.2.2（2026-09-19，玩家反馈标记缩放反直觉）**：① **3D 标记改固定世界尺寸**——旧 `scale = dist * MarkerDistScale(0.03)` 是"恒定屏占比"公式（世界尺寸随距离线性放大以抵消透视缩小），玩家实测观感**反成"近小远大"**（近处偏小、远处膨胀过度），且不符合真实透视直觉。改 `MarkerWorldSize = 0.8f` 常量 → 屏上大小 = 纯透视投影，真实近大远小（0.8m 为用户实测定稿值，初版 1.6m 偏大）。锚点/遮挡/门控逻辑不变。**教训：恒定屏占比公式 `dist*k` 在数学上正确，但实际 FOV/视场动态变化与锚点偏移（+3.0m）叠加后观感会失真——涉及"屏幕恒定"的设计必须实测，不能只看公式。** ② **接入 `Debug/debugLog` 统一调试开关**（AGENTS.md §7.1 约定；原 6 处 `LogInfo` 全部门控，`LogWarning` 不门控）——HVT 从"尚未接入"名单移出。

### 2.10 InventoryPause `er2.inventorypause` v1.0.5
打开背包（自己/尸体）时**真暂停**（延迟 timeScale 冻结，等打开动画完成）；暂停期间丢弃道具自动落地（扫描 `ItemObject.spawnedItems`）。
**ER2 暂停机制图谱（全部实测定案，做任何暂停功能前必读）**：原生 `Pause.SetPause` = timeScale=0 + 弹菜单 + `disableOnPause`（藏菜单 = 死锁）；手动 `Pause.isPaused=true` 禁用输入但**不冻结世界**；`timeScale=0` 真暂停但**卡 UI 协程动画** + 丢弃武器浮空（解法：延迟冻结等动画完成 + 扫描 `spawnedItems` 拉下道具）；`enableAiBehaviour(false)` **无效**（true 才有效）；背包开关读 `InventoryPanel.isOpen`。

### 2.11 SquadCommand（**Easy Red Gate**，原 Battlefield Commander）`er2.squadcommand` v1.4.57
**1.4.57（2026-09-26，玩家：godKey 默认换键 + mod 改名 Easy Red Gate）**：
1. **改名**：显示名 `ER2 Battlefield Commander` → **`Easy Red Gate`**。**GUID / cfg 文件名 / DLL 文件名
   全部不变**（er2.squadcommand / er2.squadcommand.cfg / ER2_BattlefieldCommander.dll）——改 GUID 会丢
   玩家现有 cfg，改 DLL 名会让老玩家更新时残留旧 DLL 造成同 GUID 双加载。ModManager 的显示名/收藏区
   自动跟随元数据（玩家当前 0 收藏，无按名收藏需要迁移）；build.ps1 包名 `$pkg` 改为 `EasyRedGate`
   （zip：`EasyRedGate_v1.4.57.zip` / `EasyRedGate_CN_v1.4.57.zip`）。
2. **godKey 默认 F9 → F8**：F9 与 AdvancedCombatMovement 的 Restart Mission 相撞（ModManager 1.7.7
   冲突提示抓到的三组之一）。选 F8 的依据：全机无占用（F5=UniGen/F9/F10 已占，F11/F12 是系统键）、
   功能键在本游戏实测可用（F5/F9/F10 在跑）、与旧键相邻迁移成本最低；字母键（H/T 等）与游戏原生键位
   无法核对（原生键位无磁盘清单），不冒险。**已装玩家 cfg 保留自己的键**（BepInEx 只在新建 cfg 时写
   默认值）——本机 cfg 已手工改 `godKey = F8`。
3. 四份发布文档：改名 + 版本 1.4.57 + 顶部新增 1.4.57 变更条目 + 全文 F9→F8 引用清理（含 Nexus 描述
   与 CN 词条）。
4. 验证：0 error；EN 构建 = 部署 = EN 包内 DLL 逐字节一致 sha256 `ea26bc05…`（242,176 B，12:10 部署，
   走 cp+sha 以免 build.ps1 清掉玩家 cfg）；CN 包 222,720 B。
**待实测**：进游戏日志出现 `Loading […… Easy Red Gate 1.4.57]`；F8 进/出上帝视角；ModManager 页顶
冲突警告从 3 组降为 2 组（F、G）。

### 2.11 SquadCommand（Battlefield Commander）`er2.squadcommand` v1.4.56
**1.4.56（2026-09-25，用户反馈："幽灵单位靠近掩体的判定范围太小了"）**：
- `formCoverCorridor` 默认 **6 → 10m**（上限仍 25，可继续加）。放宽的底气是 1.4.54 起的两道保证：① 吸附**槽位锚定**（只在自己阵型槽位近旁占用掩体，不是围着锚点抢）；② `GapOk` 强制已选掩体点之间 ≥ `max(1.5, 槽位间距×0.6)`，不会被吸成一堆。超过 12m 后掩体密集地形仍会把整条线拉到掩体边——需要时往下调。
- 默认值沿革（供后续调参参考）：8（1.4.50，实测整条线被吸上墙）→ 3（1.4.53，太紧）→ 6（1.4.54）→ **10（1.4.56）**。cfg 描述与 Ui.cs EN 词条同步。
- 验证：编译 0 error；部署 DLL 242,176 B，sha256 `C5A0A4543274136138801D8F5D2E1F7026366A05D8D12F2EFC43E8992204BE0E` 与构建产物逐字节一致；反编译复核：版本双写 **1.4.56** ✓、`Config.Bind<float>("Control","formCoverCorridor", 10f, …)` ✓。
- **发布（1.4.56，本轮收口版）**：① 四份发布文档（README / Nexus × 中英）版本号同步 1.4.56 并补齐 **1.4.39~1.4.56 共 18 版变更史**；② 日志收口复核——新增诊断（拖动基准/掩体吸附/按槽位判到位）全部 `debugLog` 门控，无条件日志均为"每次操作 1 条"的事件日志；③ **先 `-Cn` 后 EN，EN 最后跑**（部署 = EN）；④ 双语双包 `ER2_BattlefieldCommander_v1.4.56.zip`（EN，242,176 B，`C5A0A454…`）/ `ER2_BattlefieldCommander_CN_v1.4.56.zip`（CN，222,720 B，`059B0F34…`），**构建=部署=包内 DLL 逐字节一致**，包内文档与源文档 sha256 全等（CN 包按语言路由取中文文档），EN/CN 双包反编译版本双写均为 1.4.56 ✓。迭代期 `-SkipPackage` 不出包。

**1.4.55（2026-09-25，用户截图提问："明明有掩体，不应该靠近掩体吗"——线穿过沙袋墙，8 幽灵全在线上、0 吸附）**：
- **根因（陷阱 118）：`QueryCovers` 的无方向兜底是死代码**——原流程：有向查询空 → 无向兜底再查 → 但兜底结果仍被同一把 `IsCoverAvailable(facing)` 有向过滤器**再杀一遍**。只要朝向过滤全灭（如沙袋墙的掩体点不认当前受敌方向），无论查多少遍可用掩体恒 0。
- **修法**：① 两段查询**各自过滤**（新增 `FilterCoverStates`：摧毁/被占/载具）；② **摘掉后置 `IsCoverAvailable` 过滤**——其原生语义不可考（interop 仅桩、参数名 `shootDirection`，实现在 GameAssembly 原生层），且有向查询 `GetCovers(center, radius, fac, dir, false)` 本身已带 dir 参数，后置再过滤属双重过滤。朝向适配完全交给原生 dir；无向兜底从此真的兜底。
- **待观察**：若修复后仍 0 吸附，开 `Debug/debugLog` 看 `[Formation] 掩体吸附 … 可用掩体=N`——**N=0 = 这批沙袋在地图/内容包里根本没注册掩体点**（游戏侧数据问题，非 mod 侧；需 Combat Cover 类内容包或地图自带）。
- 验证：编译 0 error；部署 DLL 242,176 B，sha256 `CA97B12E8C1F522563E6297C3D4EB893D0799D0643CD8A5D5E9D46F787F9D66A` 与构建产物逐字节一致；反编译复核：版本双写 **1.4.55** ✓、`FilterCoverStates` 两段调用 ✓、`IsCoverAvailable` 引用 **0 处** ✓。迭代期 `-SkipPackage` 不出包。

**1.4.54（2026-09-25，用户反馈："1.拉出的线操作不跟手。2.线拉很长幽灵单位也不靠近掩体"）**：
- **① 不跟手 = 像素→米比例的参照量取错（陷阱 117）**：`dragPerPx` 原按相机**垂直高度**算，而俯视倾斜时地面 1 像素对应的真实距离按**斜距**（相机→锚点视线距离）算——45° 俯角下系统性偏小约 30%；再叠加 1.4.49 把 `formDragSens` 默认压到 0.5，箭头端点只到光标一半距离。修法：比例改 `camH / sinDep`（俯视度 `|forward.y|` 钳 0.35~1，防近水平视角失真；正俯视时与原式等价），**`formDragSens` 默认 0.5 → 1（跟手）**。
- **② 长线不近掩体：吸附 3m 太紧 + 只查锚点一个圆心**：吸附半径默认 3 → **6m**；新增 `QueryCoversAlongLine`——沿阵型线**多点采样**（半跨 > 0.8×半径时加两端采样点），结果按掩体指针去重、总量仍受 `MaxCoverResults=24` 限制（此前长线两端根本没被查过掩体）。
- **③ 防挤堆**：新增 `GapOk`——已选掩体点之间强制保持 `minGap = max(1.5, 槽位间距×0.6)`。掩体点常密集分布在同一段墙上，全吸会让人贴人；有了这个间距，阵型的疏密不会被掩体改写（长线间距大 → 吸附更自由；短线间距小 → 少吸，不会挤）。
- 验证：编译 0 error；部署 DLL 242,176 B，sha256 `6F1ABCE7BED86E8F3F710D3E5BEAF648DDFE5D2048E29AEFD7C4F2A22F369787` 与构建产物逐字节一致；反编译复核：版本双写 **1.4.54** ✓、`Mathf.Clamp(Abs(forward.y),0.35,1)` 斜距修正 ✓、`QueryCoversAlongLine`/`GapOk` ✓、cfg 默认 `formDragSens=1f`/`formCoverCorridor=6f` ✓。迭代期 `-SkipPackage` 不出包。

**1.4.53（2026-09-25，用户反馈："现在幽灵单位完全不能像之前那样在预定掩体位置展示了"）**：
- **1.4.51 的"默认 0"关掉了两件东西**（陷阱 116）：行为（阵型单位去抢掩体）+ **它承载的信息**（幽灵显示"这位会进这个掩体点"，还带蹲/趴姿态）。用户要的是**那份信息**，不是那个行为——我把它们一起关掉了。
- **修法：吸附恢复，但本地化——默认 8 → 3m**：只吸**真正挨着槽位**的掩体（`DistXz(掩体点, 槽位) ≤ 3`，每槽至多一人、每掩体至多一人）。槽位压在沙袋/墙边上的单位 → 幽灵显示在该掩体点并摆出掩体姿态；离得远的单位留在阵型线上。这样"掩体预览"回来了，而 1.4.50 截图里那种"线与墙平行且距 8m → 8 槽全被吸上墙"不会再现（3m 够不着）。
- cfg 描述与 Ui.cs EN 词条同步改写（键名不变）：**0 = 纯阵型线 / 3（默认）= 只吸挨着槽位的掩体 / 6~8 = 更积极找掩体，但掩体密集地形会把整条线吸到掩体边**。
- 验证：编译 0 error；部署 DLL 240,640 B，sha256 `9AF194B1F335A582F9C8C0F01A7593158CB2DA8DE5FC9BBC6DB32127FD8524AC` 与构建产物逐字节一致；反编译复核：版本双写 **1.4.53** ✓、`Config.Bind<float>("Control","formCoverCorridor", 3f, …)` 默认值 ✓；cfg 已删待重生成。迭代期 `-SkipPackage` 不出包。

**1.4.52（2026-09-25，用户反馈："没有幽灵单位后那个虚线还显示，这不好看"）**：
- **根因一（设计）**：阵型下发走 `RegisterMoveObservation(routeOnly:true)` 会画**每个单位 → 锚点**的路线虚线（扇形）——幽灵已经预览过落点，下发后这套虚线只剩杂乱。修法：`RegisterMoveObservation` 新增 `withRouteLines` 参数，阵型下发传 **false**（普通移动/双击防守/登车路线不受影响；`showPathLines` cfg 语义不变）。
- **根因二（生命周期 bug，更隐蔽）**：到位判定 = 距 `obsTarget`（锚点）≤ `moveRadius`（默认 8m），而阵型单位散在槽位上、距锚点可达**线长一半**（最长 60m）→ **永远判不到位**，观察窗挂满 45s——虚线、"移动 → X/N 已到位"进度行、目标点圈全部滞留。修法：`RegisterMoveObservation` 新增 `unitDests`（与 units 按下标平行的每单位落点），`ObsMoveTick` 优先按各自槽位判到位（无登记回落锚点）→ **全员到槽即清**。`AssaultCovers`（右键建筑进掩体）同样接入（虚线保留——指向建筑有指向意义，但能准时消失了）。
- 验证：编译 0 error；部署 DLL 240,128 B，sha256 `C9612344FE45F52D5E268CF9A3A5B4782EE745B466841240F26FFF4CA8522DDB` 与构建产物逐字节一致；反编译复核：版本双写 **1.4.52** ✓、`obsUnitDest`/`obsDrawRoutes`/新签名（含默认参数）✓；迭代期 `-SkipPackage` 不出包。

**1.4.51（2026-09-25，用户三轮反馈"还是散不开"+ 截图实锤）**：
- **截图定案（陷阱 115）**：用户从移动目标圆圈向墙拖线（箭头长约 12m），阵型线（竖直、过锚点）与墙**平行且距约 8m**——1.4.50 吸附半径 8m 内，**墙上每个掩体点对每个槽位都够得着** → 8 槽全被吸上墙，幽灵又贴墙排成一列。结论：**只要吸附默认开，掩体密集地形（村庄/墙边/沙袋线）里阵型必然被吃掉**——这不是半径大小问题，是"阵型拖动里默认做掩体吸附"这个设计本身错了。
- **修法：`formCoverCorridor` 默认 8 → 0（吸附默认关闭，纯阵型线）**：散开优先；找掩体走专用入口（`keyCover`=N 就近掩体、右键建筑 `AssaultCovers`，均不受影响）。想保留吸附的用户可调 2~8（每槽至多一人、每掩体至多一人的限制保留）。cfg 描述与 Ui.cs EN 词条同步改写（键名不变）。
- 验证：编译 0 error；部署 DLL 239,104 B，sha256 `B4BB14970F3B7B909E08879EA3153C58A26D39ACBB4E19970C311F67C56C4A88` 与构建产物逐字节一致；反编译复核：版本双写 **1.4.51** ✓、`Config.Bind<float>("Control","formCoverCorridor", 0f, …)` 默认值 ✓；cfg 已删待重生成。迭代期 `-SkipPackage` 不出包。

**1.4.50（2026-09-25，用户二轮反馈："现在很难再让幽灵单位出现了，就算出现了还是会排排站，不散开"）**：
- **1.4.49 走廊方案的两层缺陷**：① **参数层**——走廊过滤（|纵深|≤8m）+ 沿线匹配≤12m 叠加，可用掩体窗口过窄 → 开阔地几乎抓不到掩体 → 幽灵消失（对 1.4.48"全军挤墙"矫枉过正）；② **设计层（真正根因）**——幽灵只画掩体分配：掩体天然是"一排"，用户以幽灵判断"散没散开"，看到的永远是排排站；真正散开的阵型线槽位只有 0.2m 小黄点，视觉上等于没有预览。
- **语义反转：阵型优先、掩体吸附（`RebuildCoverAssignment` 重写）**：先给**全部步兵**按 BuildLine 同款数学排好槽位（新 `ComputeLineSlots`，拖多宽散多宽），槽位 `formCoverCorridor` 米内有空闲掩体才"顺势占用"（`DistXz(掩体, 槽位) ≤ snap`，每槽至多一人、每掩体至多一人）；其余单位留在槽位上。无论有没有掩体，整条线的展开始终成立。cfg 描述同步改写（键名/默认值 8m 不变，0=完全不用掩体）。
- **幽灵覆盖全部步兵槽位（`GhostPreview.Apply` 改签名）**：`Apply(covers, lineSlots, facing)` 两表合一——掩体槽带建议姿态、阵型线槽站姿；**每帧调用**（位置跟随阵型线移动），克隆创建预算 6→2/帧（防 Instantiate 尖刺，16 个约 8 帧建满）。Apply 唯一调用方是 `Formation.DragTick`（UniGen 走反射只调 `Ghostify`，不受影响）。
- **布局数学收口**：BuildLine 步兵支路抽成 `ComputeLineSlots(units, spacing, dst)`（每帧排线 + 节流吸附判定共用同一份，保证"预览=下发"），载具支路独立为 `BuildVehicleLine`；1.4.49 的 `AssignCoversAlongLine`/`CoverEndMargin`/`CoverAlongMatchMax` 删除。诊断日志改"掩体吸附 X/Y（snap/可用掩体/半径）"（debugLog 门控）。
- 验证：编译 0 error；部署 DLL 239,104 B，sha256 `842CA49B3BF05065D5EAFF9493AE338C3BC4FDA8E05F8ACA4EA1F9DA04467B4A` 与构建产物逐字节一致；反编译复核：版本双写 **1.4.50** ✓、`ComputeLineSlots(infBuf, 1.4f, snapScratch)`（吸附走全量槽位）✓、旧 `BuildLine(`/`AssignCoversAlongLine` **0 处** ✓；cfg 已删待重生成。迭代期 `-SkipPackage` 不出包。

**1.4.49（2026-09-25，用户反馈："长按右键拖动阵型时灵敏度太高，阵型常常无法展开。拉动让幽灵单位扩散时总是一字排开站在掩体边，根本散不开"）**：
- **阵型拖动灵敏度 cfg 化（`Control/formDragSens`，默认 0.5，范围 0.1~2）**：`CaptureDragBasis` 的像素→米基准保持不变（锚点地面比例，随镜头高度），再乘 cfg 倍率；代码侧再钳 0.05~4 防手改 cfg 拉出爆长箭头（陷阱 17g52：取值链上每处 clamp 都是第二范围定义）。1 = 旧比例，越小越跟手。
- **掩体分配改"沿阵型线走廊"（`Control/formCoverCorridor`，默认 8m，范围 0~25，0=关闭掩体优先）**：
  - 旧行为：`AssignCovers` 以**锚点**为圆心就近贪心、与阵型线形状完全无关 → 掩体密集处（村庄/墙边）全军被吸进锚点旁的同一排掩体，阵型线只剩零星单位——即用户看到的"总是一字排开站在掩体边，散不开"。
  - 新 `AssignCoversAlongLine`：① **走廊过滤**——掩体投影 |along| ≤ halfSpan+2m（端部余量 `CoverEndMargin`）、纵深 |depth| ≤ corridor 才可用；② 步兵按**沿线投影**排序（与 `BuildLine` 排线同序，减少交叉走位），逐个匹配"沿线投影最近"的空闲掩体（纵深偏移作 0.5 罚项，优先贴近阵型线本体）；③ 最近匹配 > 12m（`CoverAlongMatchMax`）就放弃该单位 → 照常排线，不抢远掩体。
  - **建筑进掩体（`AssaultCovers`）不受影响**，仍走旧就近贪心（点语义，本就该以点击点为中心）。
  - 已知限制：查询半径仍受 `MaxCoverQueryRadius=35m` 上限，>60m 的超长阵型线两端查不到掩体——两端单位照常排线，不影响展开。
- **诊断日志（全部 debugLog 门控，§3.5）**：拖动基准 perPx/sens（每次拖动 1 行）；走廊内掩体 kept/total（0.35s 节流）；covers 分配统计含 halfSpan/corridor/radius。
- 验证：编译 0 error；部署 DLL 239,104 B，sha256 `08FFBE61E7EEC421A699DA3893CC5A259555520A1DF6C87952DD8BCCE096122E`，与构建产物逐字节一致；反编译复核版本双写 **1.4.49** ✓；cfg 已删待重启重生成（新增 2 键）。按经验文档 D 节纪律，迭代期 `-SkipPackage` 不出包。

**1.4.48（2026-09-25，共享层钳位修复 → 同步重建；用户定案**不出发布包**）**：
- **共享层 `Shared/Er2Ui.cs` 的 `SetPanelAlpha()` 钳位修正**：陈旧下界 `Mathf.Clamp(v, 0.55f, 1f)` → **`0.40f, 1f`**，与 cfg 的 `AcceptableValueRange(0.40f, 1f)` 对齐。
  此前 `uiPanelAlpha = 0.50` 被**静默抬成 0.55**，面板从未真正到过 50%，且 cfg 的 0.40~0.55 整段是死区（详见新陷阱 **AGENTS 17g52**）。
- 本 mod 侧无其它改动。因该文件由两个 mod **源码级共享**（`<Compile Include="..\Shared\Er2Ui.cs">`），SC 的 DLL 内容随之变化 → 按 §7 `z+1` 升版本并重建部署；**用户选择"两个 mod 同步重建"但 SC 不出包**。
- 验证：编译 0 error；部署 DLL 236,032 B，sha256 `1b63abbf6ad9b21656f374f61de894de394f828cb7fc9c6a06b9655e35542502`，与构建产物逐字节一致；反编译复核版本双写 **1.4.48** ✓ + `SetPanelAlpha` 内 `Mathf.Clamp(v, 0.4f, 1f)` ✓。
- ✅ **已补（1.4.56 出包时完成）**：SC 的 `README.txt` / `README_CN.txt` / `Nexus_description.md` / `Nexus_description_CN.md` 版本号已同步 **1.4.56**，并补齐 **1.4.39~1.4.56 共 18 版变更史**（中英四份）。本条欠账关闭。

**1.4.46（2026-09-25，用户第 40-42 轮：武器拾取方案反转 + 灰字终审联动）**：
- **1.4.40-1.4.42 的"武器只进背包"拦截全部撤销（用户定案"只用原生方法"）**：探针实锤单位拿枪路径含 `Lua_Soldier.LoadAndSetWeapon`（且该通道对非玩家静默无效——触发 2 次枪没上手）；地面菜单「Take Into Right Hand」被 1.4.31 防隔空改道送进背包 = "不能拾取并放置于右手"。LootPolicy 只留 4 个只读探针（PickUpCR / PickUpItemFromInventory / AddItemInHand / LoadAndSetWeapon，全部 LogInfo）。
- **1.4.43**：BackpackPanel 右手类条目（原文含 hand/右手）改 **"走过去 → 到达后执行原生交互本体"**（新 `lootNativeCall` 任务，防隔空初衷保留）；背包武器合成操作改标 **"拿起至右手"**（原"穿上"是穿戴件文案）。
- **1.4.44**：背包武器上手改原生 `Soldier.PickUpItemFromInventory(vi, invMgr, 0)`（LoadAndSetWeapon 对 AI 无效），手持快照（getHeldWeaponId）前后对比 + 失败兜底，LogAlways 全程。
- **1.4.41 教训（F9 失效事故）**：Harmony 跳过协程工厂方法必须 `ref __result` 且类型用 interop 的 `Il2CppSystem.Collections.IEnumerator`（空协程用 `Il2CppSystem.Collections.ArrayList().GetEnumerator()`）——托管 IEnumerator 类型不匹配 = IL 编译失败 = **PatchAll 中断全插件报废**。另：`Il2CppSystem.Collections.ArrayList` 在 Il2Cppmscorlib；`Lua_Soldier.connectedSoldier` 取真实 Soldier。
- **1.4.45/1.4.46**：共享 Er2Ui 灰字终审（见 UniGen 2.5.25/2.5.26）联动重构建。
**1.4.38（2026-09-25，用户第 39 轮："谁告诉你是信息面板去底板了？我说的是屏幕底的提示，截图里截的也是，你那看出来我要你把信息栏去底板了。"）**：
- **纠正 1.4.37 的误读（陷阱 114）**：用户上一轮说"既然都可以隐藏了干脆做成左上角显示的样子，不要背景了"——他前一句在谈【屏幕底的提示条】（"可以隐藏"正是提示条的互斥特性），主语就是提示条；我却把"左上角的样子"当成了【样式参考来源】，把"不要背景"执行到了【信息面板】上，还把提示条的底板留着——**两个都改错了对象**。
  - 修法：① **信息面板底板恢复**（回滚 1.4.37 的误删，`DrawHudPlate(pr)` 加回）；② **提示条去底板** → `DrawShadowLabel` 白字 + 阴影直接叠场景（与左上角原生选中信息同款）。
- **互斥规则不变**：有选中 → 信息面板显示（提示条不画）；没选中 → 提示条（无背景样式）。
- **新陷阱入指南**：114（**"做成 X 的样子"的主语 = 当前话题里的对象；"我理解的改造对象"与"用户话题对象"不一致时，要么复述确认要么默认后者**——改错对象 = 零收益 + 信任损耗；同源 101/102）。
- 验证：编译 0 error（SC + UG）；部署 DLL 231,424 B，sha256 `335B45EECCB1753E43C38391BD735EAE20787644BA7BE64D9318033631D4707F`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.38.zip`。
  反编译复核：版本双写 1.4.38 ✓；`InfoPanel.Visible` 互斥在位 ✓；提示条 `DrawShadowLabel`（白字阴影）✓；`DrawHudPlate` 3 处（定义 + InfoPanel + 小队列表；提示条的 0 处）✓。

**1.4.37（2026-09-25，用户第 38 轮："这个怎么还是灰色字体，既然都可以隐藏了干脆做成左上角显示的样子，不要背景了。通用生成mod的UI还没改。"）**：
- **提示条文字是最后一处灰色（陷阱 113 的"旁路"再现）**：`SquadCmdLogic.HudStyleSmall()` 与 `BackpackPanel.EnsureStyles()` 的 `tipStyle` 仍**硬编码军绿遗产色** `new Color(0.85f, 0.9f, 0.85f, 0.95f)`——它们**不经过 `uiText`**，所以 1.4.36 的文字统一漏掉了。修法：两处都改 `Er2Ui.Text`。
  - **全项目扫描**：`grep MakeLabel|MakeButton` 过滤 `Er2Ui.Text / Color.white / TextOnActive / TextDisabled / TextDim / TextHover / Warn / Danger / Accent` 后，**非白色文字源已清零**。**"色源收编"是"收编 + 扫尾"两步**（陷阱 113 补）。
- **单位信息面板去底板（用户："做成左上角显示的样子，不要背景了"）**：与游戏自带的左上角选中信息同款——白字 + 阴影直接叠在场景上，`DrawHudPlate(pr)` 调用已删（反编译 0 处引用；`PanelRect()` 保留用于命中判定/实测回写）。
- **"通用生成mod的UI还没改"**：全项目扫描确认 UniGen 的**所有文字色源已是纯白**（title/text/rowText/help/button/active/row/tab/flash/star 全部 `Er2Ui.Text` 或 `Color.white`）——用户看到的灰应是未重启的旧 DLL；如重启后仍有具体哪一块灰，请指出位置。
- 验证：编译 0 error（SC + UG）；部署 DLL 231,424 B，sha256 `E39D2AE7B5EFD99E942F0CC0631DA920DF360F27FEF392ED030E4A7672184DC7`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.37.zip`。
  反编译复核：版本双写 1.4.37 ✓；`HudStyleSmall` 内 `Er2Ui.Text` ✓；`tipStyle` → `Er2Ui.Text` ✓；`DrawHudPlate` 只剩 3 处（InfoPanel 的已删）✓；军绿遗产色 `(0.85, 0.9, …)` **0 处** ✓。

**1.4.36（2026-09-25，用户第 37 轮："文字还是灰色的。同时我不希望信息栏与下面的提示在竖轴上同时存在。UI改了后所有的都要改，包括附属mod。"）**：
- **文字还是灰色 = HUD 文字色走 cfg（陷阱 109 的再现 + 陷阱 113）**：HUD 文字色来自 cfg `colorText`，而用户 cfg 里是**旧灰值**（cfg 不随代码默认值更新）。修法两层：① `colorText` 加入迁移链（`#E8E8E8` / `#F1EBE2` / `#F0F0F2` → `#FFFFFF`）；② **色源收编**——`ApplyUiTheme()` 里 `uiText = Er2Ui.Text`（不再读 cfg），HUD 文字与 UniGen 面板**共用同一个令牌**；三个颜色 cfg（`colorText`/`colorBase`/`colorHover`）保留但**不再被读取**（描述标注"已停用"）。
- **信息栏与底部提示条互斥（"不希望信息栏与下面的提示在竖轴上同时存在"）**：新增 `InfoPanel.Visible`（= `SelInfantryCountPublic() > 0 || VehicleRefCountPublic() > 0`，与 `GVC.HasSelection` 同源——陷阱 78）。没选中 → **信息面板整个不画**（`contentH` 复位为最小），提示条显示；有选中 → 信息面板显示，**提示条整条让位**（底板 + 文字都不画）。
- `colorBase` 默认 → `#00000080`（黑 50%，与 `PanelAlpha 0.50` 一致）；迁移链补上一轮默认 `#000000B8`；`legacyHover` 集合补 `#3A3A3AEE`。
- **新陷阱入指南**：113（**"统一"要落到色源上——走 cfg 的消费点永远改不到**）。
- 验证：编译 0 error（SC + UG）；部署 DLL 231,424 B，sha256 `00C8213223A83E72AF93A08058C088E85CF5CB487DA05989FDF9BE35C9EA8DED`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.36.zip`。
  反编译复核：版本双写 1.4.36 ✓；`InfoPanel.Visible` 属性 + GVC 引用（互斥）✓；`uiText = Er2Shared.Er2Ui.Text` ✓；`colorBase` 默认 `#00000080` ✓；`legacyText` 迁移数组（`#E8E8E8`）✓；`MonoTextDim` 纯白 ✓。

**1.4.35（2026-09-25，用户第 36 轮："重叠了。你不觉得黑色背景加灰色字体很难看清吗，背景透明度改成50%，字体改成白色。"）**：
- **修重叠（我上一轮"对称"改出来的）**：1.4.34 把左下信息面板与小队列表的底距都改成 14——但**底部提示条的顶边在离屏幕底 30f*Scale** 处，14 的底距让两块 HUD 直接**穿过提示条**（用户截图："重叠了"）。修法：两块底距统一 **`36f * Scale`**（提示条 30 + 6 间隙），左右依旧对称。**通用律：同屏堆叠的 HUD 块，垂直位置要用"从底往上累加"的公式表达——每块的"底距"是推导值，不是独立常量；改任何一块前列出"它下面还有什么"**（陷阱 112）。
- **背景透明度 50%（用户指定）**：`PanelAlpha 0.72 → 0.50`（cfg 范围下限 `0.55 → 0.40`）；`Scrim` 同步 `0.72 → 0.50`（提示条也是这个背景）；历史默认 `0.85 / 0.72` 都加入迁移链（两 mod 的 Plugin 各自更新）。
- **字体纯白**：`MonoTextDim #E4E4E8 → #FFFFFF`（次要文字也纯白）。**连带修复**：悬停"变暗"反馈用的是 `hov ? TextDim : Text`——TextDim 提纯白后两色相同、**反馈静默失效**；新增专用 `TextHover #B4B4BA`（比 normal 暗一档），四处悬停点全部换用。
- **新陷阱入指南**：112（同屏 HUD 块共享"垂直空间预算"；改共享色时全局搜引用它的反馈逻辑）。
- 验证：编译 0 error（SC + UG）；部署 DLL 230,912 B，sha256 `C2779A0FA42693BF615B922B279E2BCC1C70225698C3B9877FC4E7A980BFCD23`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.35.zip`。
  反编译复核：版本双写 1.4.35 ✓；`BottomGap => 36f * Er2Ui.Scale` ✓；小队列表底距 `36f * ... - totalH` ✓；`PanelAlpha = 0.5f` ✓；`Scrim = (0,0,0,0.5f)` ✓；`MonoTextDim` 纯白 ✓；`TextHover` 4 处 ✓；cfg 范围 `0.4f, 1f` ✓。

**1.4.34（2026-09-25，用户第 35 轮："你不觉得这样不对称很丑吗，而且信息显示也不会动态调整，下面空这么多。让他们对称啊。2.还有，我说鼠标放在上面的提示不是现在这个先删了，而是放上去后文本会变暗。"）**：
- **左下单位信息面板：动态高度 + 左右对称（用户："让他们对称啊"）**：
  - 原状：`H` 固定 `190f * Scale`、`BottomGap 34f * Scale`；而右下小队列表 = `行数 × (22*Scale + Gap)`、`BottomGap 14f * Scale` → **内容少时左下大片空白**，两块底边也不齐。
  - 修法：① `BottomGap 34 → 14`（与右侧同，两块贴底位置一致）；② **高度实测回写**——`Draw()` 每帧在内容绘制完后 `contentH = Clamp((y + 10f*Scale) − 面板顶, 96f*Scale, 340f*Scale)`，`PanelRect()` 用 `contentH` 画底。**命令式布局测高度用"实测回写"最便宜**（晚一帧收敛，肉眼无感），不必为算高度写第二份布局公式。
  - **通用律**：屏幕上成对出现的块（左/右、上/下），其**贴边距离 / 内边距 / 高度规则必须同源**——两套各写各的早晚会不对称。
- **悬停反馈恢复 = 文字变暗（用户指明，并要求删掉 tooltip 框）**：1.4.30 描边重构后，页签/列表行的文字由 `LabelOutlined` 单独画——**描边文字感知不到 GUI 样式的 hover**，原"悬停文字变暗"反馈消失；1.4.32 我又加了 tooltip 框，方向也不对（用户："不是现在这个，先删了"）。修法：**删 `DrawHoverTip`（含方法体，反编译 0 处）**；页签（`Er2Ui.TabGrid`）与三处列表行**逐行判 `rect.Contains(Event.current.mousePosition)`**，悬停时文字色从纯白换 `TextDim`（变暗）。
- **新陷阱入指南**：111（HUD 成对元素要对称；内容高度用实测回写）。
- 验证：编译 0 error（SC + UG）；部署 DLL 230,912 B，sha256 `535BAAB38BA5EB7A36DC17D8778CE8BBF77C7D8A09BAC6E64142D43270A92A83`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.34.zip`。
  反编译复核：版本双写 1.4.34 ✓；`DrawHoverTip` **0 处** ✓；`GUIContent(string.Empty, null, …)` **0 处**（已恢复 `GUIContent.none`）✓；`TabGrid` 悬停变暗 1 处 ✓；UG 列表悬停变暗 3 处 ✓；`InfoPanel.contentH` 3 处（声明 / `PanelRect` / 实测回写）+ `BottomGap => 14f` ✓。

**1.4.33（2026-09-25，用户第 34 轮："为什么UI没改完。同时你不觉得字体和背景区分不开了吗。同时幽灵物品，单位，载具等部分模型并没有被替换材质，还是原材质（比如士兵身上的装备）"）**：
- **"UI 没改完" 的根因 = cfg 不会随代码默认值更新（陷阱 109，本轮最重要）**：BepInEx 首次运行把默认值写进 `.cfg`，此后**只读文件**——代码里改默认值**对老用户毫无影响**。所以面板（走共享 `Er2Ui` 令牌）自动变新，而 HUD 按钮/小队列表（走 cfg `colorBase`）**永远停在旧值**（用户本地还是军绿 `#0E1C0EB4`）→ 表现为「UI 只改了一半」，且**只在这个老用户身上出现**（截图实证：顶部 Merge/Take Command/Split 仍是军绿）。
  - **修法**：新增 `GodViewController.MigrateLegacyUiCfg()`（在 `ApplyUiTheme()` 最开头调用）——`colorBase` / `colorHover` 命中**历史默认值集合**（军绿 → 灰黑 → 黑棕 → 中性黑共 7 个基色 / 6 个悬停色）时写入当前默认值；**不命中一律不动**（不覆盖用户意图）；每次迁移**打 `LogAlways`** 便于追溯。`uiPanelAlpha` 旧默认 0.85 → 0.72 同样迁移；UG 侧 `Plugin.cs` 加同款判断。
  - **通用律**：**任何"改默认值"的提交都要问一句"老用户的 cfg 会怎样"**——默认值只在第一次运行生效（同源陷阱 102/81）。
- **幽灵材质只替换了第 1 个槽（陷阱 110）**：原代码 `r.sharedMaterial = ghostMat` —— `sharedMaterial`（单数）**只作用索引 0**；士兵＝身体+装备+头盔、载具＝车体+履带+细节，**每部件一个槽** → "只换第一个"＝模型一部分变灰、其余保持原色（用户："士兵身上的装备还是原材质"）。修法：读 `r.sharedMaterials.Length`，`<=1` 走单数赋值，否则**构造同长数组全填** `ghostMat` 再赋 `sharedMaterials`。用 shared 而非 `material`（后者会实例化材质，克隆体多时成倍占内存）。
- **字体与背景对比（"区分不开"）**：① `Er2Ui.LabelOutlined` 的描边由 **2 方向（左上/右下对角）→ 4 方向**（上下左右各一次，共 5 次 `GUI.Label`），描边色提到纯黑不透明 `(0,0,0,1)`——把文字轮廓整个包住，小字号 + 半透明底上区分度明显提升；② 列表行文字 `FontBody + 3 → + 4`（15 → 16px）。
- **新陷阱入指南**：109（cfg 不随默认值更新 → 必须做配置迁移）/ 110（`sharedMaterial` 只改槽 0 → 多槽模型要填 `sharedMaterials`）。
- 验证：编译 0 error（SC + UG）；部署 DLL 230,912 B，sha256 `F2342D516DA9C754A28448CE11B1A034625C606B40512789E645A623DACF2537`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.33.zip`。
  反编译复核：版本双写 1.4.33 ✓；`MigrateLegacyUiCfg` 定义 + 调用（在 `ParseThemeColor` 之前）✓；`sharedMaterials` 2 处（幽灵多槽）✓；`LabelOutlined` 内 **5 次 `GUI.Label`**（4 描边 + 正文）✓。

**1.4.32（2026-09-25，用户第 33 轮："1.原来鼠标放在通用生成选项上会有提示，现在没了。2.幽灵物体材质不好，太亮了，同时不够透明。让左下角的单位信息加上背景，并与右边的小队列表对齐。3.最下面的提示背景我喜欢，统一一下。"）**：
- **幽灵预览 = shader 取样来源搞错（陷阱 108）**：材质用 `Sprites/Default`，其片元是 `tex × IN.color` —— **读顶点色、不读 `_Color`**；幽灵是 Mesh（无顶点色 ≡ 白）→ `ghostMat.color`（含 alpha 0.32）**完全无效** → 渲染成**实心亮白**（用户截图实证"太亮了，同时不够透明"）。修法：shader 优先链改为 `Particles/Standard Unlit`（unlit + 读 `_Color` + 支持 alpha）→ `Legacy Shaders/Transparent/Diffuse` → `Unlit/Transparent` → `Sprites/Default`（兜底，注释说明只在有顶点色的物体上正确）→ URP Unlit → `Hidden/Internal-Colored`；并**显式配置 Fade**（`_Mode=2` / `_SrcBlend=SrcAlpha` / `_DstBlend=OneMinusSrcAlpha` / `_ZWrite=0` / `EnableKeyword("_ALPHABLEND_ON")` / URP 的 `_Surface=1` + `_SURFACE_TYPE_TRANSPARENT` / `renderQueue=Transparent`）。`WGhost` 由 `#B8BCC0@0.32` → **`#8C9196@0.20`**（更暗更透）。
- **左下角单位信息（InfoPanel）：加整块背景 + 与小队列表左右对称**：① 原来**完全没有整块底**（文字直接叠在战场上）→ 现在画 `DrawHudPlate(pr)`；② 左边缘由 `x=0`（贴屏幕边）改为 `PanelPad = 10f * Scale`，与右下小队列表"离右边缘 `10f * Scale`"**对称**（用户："与右边的小队列表对齐"）。
- **HUD 统一底板（"最下面的提示背景我喜欢，统一一下"）**：新增 `GodViewController.DrawHudPlate(Rect)` = `Er2Ui.Scrim`（**纯黑 72%**，即底部提示条那个背景）+ `Leather(0.08)` + `PanelBorder` 描边；**三处共用**——底部提示条、右下小队列表（原 `uiBase@0.82` 单层平涂）、左下单位信息（新增）。`Scrim` 归一为 `(0,0,0,0.72)`；HUD cfg `colorBase` 默认 `#101010EE → #000000B8`（同一套纯黑 72%）。
- **悬停提示恢复（陷阱 107，UniGen 侧同批）**：1.4.30 为给文字描边把控件内容换成 `GUIContent.none` → **顺带清掉 tooltip 通道**。修法：控件改携带 `new GUIContent(string.Empty, null, tooltip)`（文本空、tooltip 保留）+ 面板帧末自绘提示框。**⚠️ IL2CPP 只保留三参构造**（`GUIContent(string, string)` 被裁剪 → CS7036），必须写 `(text, null, tooltip)`。
- **新陷阱入指南**：107（`GUIContent.none` 丢 tooltip；tooltip 须帧末统一读）/ 108（shader 取样来源：`Sprites/*` 读顶点色不读 `_Color`）。
- 验证：编译 0 error（SC + UG）；部署 DLL 229,376 B，sha256 `AB34EEA85B375CBCB18F5262FA5FC80F03EF7B5EDD9F256F884A12DB66DB5D64`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.32.zip`。
  反编译复核：版本双写 1.4.32 ✓；`Particles/Standard Unlit` 在 shader 链首位 ✓；`_Mode`（Fade）设置在位 ✓；`WGhost` α = `0.2f` ✓；`DrawHudPlate` 定义 + 3 处调用 ✓；`InfoPanel.PanelPad` 在位 ✓；`colorBase` = `#000000B8` ✓；`GUIContent(string.Empty, null, ...)` 三参形式在位 ✓。

**1.4.31（2026-09-25，用户第 32 轮："1.让士兵捡枪还是隔空捡的。2.把指挥官mod和他的附属modUI风格统一一下。3.绘制的鼠标太糊了，同时不能变色了。"）**：
- **捡枪隔空 = 语言相关回归（本轮最重要，陷阱 106）**：1.4.17 的判定是 `raw.StartsWith("拾起")`——**中文前缀**；**用户游戏是英文**，`Interaction.GetInteractionText()` 返回 `"Pick up ..."` → 判定**恒 false** → 该条目落回原生 `Interaction.Call()`，而原生接口**没有距离校验** → 隔空捡枪。
  - **修法**：新增 `IsPickupInteraction(idx, raw)` —— **结构优先**：取 `Interaction.classType`（发起交互的 `MonoBehaviour`，对地面物品就是那个 `ItemObject`/`Item` 组件），比较其 `gameObject` 是否就是该物品（或 `IsChildOf` 物品），**与显示语言无关**；再以中/英前缀（`拾起` / `Pick up` / `Take ` / `Grab`）兜底。
  - 配套：新增 `List<Interaction> menuInts` 与 `menuLabels`/`menuRaw` 平行保存（剪枝 `RemoveAt`、合成"穿上"条目压 `null` 占位、`Clear` 同步），使结构与文案三个列表始终等长。
- **光标太糊**：`TexSize 32 → 64`——32 正是 Windows 硬件光标上限，系统 DPI 缩放 / 游戏全屏缩放一拉伸就糊；64 会让 Unity 自动走**软件光标**（按屏幕坐标绘制，锐利）。`RingRadius 7 → 14`、`RingHalfWidth 1.1 → 2.2` 等比放大。
- **光标"不能变色"**：真因是 1.4.19 为配合灰黑 UI 把**所有光标状态压成灰阶**（只留敌军红、工事橙）→ 看起来"不变色了"。现恢复**逐状态语义色**：友军青绿 `(0.35,0.95,0.55)` / 敌军红 `(1,0.32,0.28)` / 可驾驶载具亮青 `(0.35,0.85,1)` / 建筑灰白 `(0.78,0.78,0.80)` / 工事橙 `(1,0.68,0.28)` / 可交互黄 `(1,0.92,0.35)` / 默认白。（光标画在 3D 场景上，不在面板里，本就不必跟着面板去色。）
- **两 mod UI 风格统一**：① cfg 默认色对齐 `Er2Ui`（`colorBase #101010EE` / `colorHover #3A3A3AEE`）；② HUD 按钮描边由**文字色**改用共享令牌 `Er2Ui.PanelBorder`（原来两个 mod 的边框不是同一套）；③ `InfoPanel` 底板补 `Er2Ui.Leather + EdgeSoft` 受光边；④ `BackpackPanel` 底板补受光边——四处合起来等于把 `Er2Ui.PanelBase`（近黑 + 皮革 + 受光边）贯彻到指挥官侧全部面板。
- **新陷阱入指南**：106（**不要用"显示文本"做逻辑判断**——它随语言变；判断"这是什么"要用结构：类型/组件/枚举/ID/层级。附：测试必须在目标语言下做）。
- 验证：编译 0 error（SC + UG）；部署 DLL 228,352 B，sha256 `219915CAC05FC7435B0A4AA55A3186EEE7F0C1E3D520A6F97D7008B70182C99D`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.31.zip`。
  反编译复核：版本双写 1.4.31 ✓；`IsPickupInteraction` 定义 + 调用 ✓；`MonoBehaviour classType = val.classType` + gameObject 比较在位 ✓；`menuInts` 9 处（声明/剪枝/占位×3/填充/清理/判定）✓；`TexSize = 64` + `RingRadius = 14f` ✓；光标六个状态色值全为恢复后的语义色 ✓。

**1.4.30（2026-09-25，用户第 31 轮："我说的是文字太暗了，你到底改了什么！一点区别都没有" + "我发现这个的背景色挺好的，改成这个"（指 HUD 底部提示条））**：
- **⚠️ 关键：`FontStyle.Bold` 被静默忽略（陷阱 105）**：IMGUI 的粗体要么靠字体自带 Bold 字形、要么靠 `Font.dynamic` 合成；**游戏原生字体往往单字重且非 dynamic** → `style.fontStyle = FontStyle.Bold` **不报错、不警告、完全无效**。我上一版"加粗 + 字号 +1"因此看起来毫无变化（用户"一点区别都没有"）。
  - **修法**：新增 `Er2Ui.LabelOutlined(r, text, style, fg, outline)` —— **描边双绘**（暗色偏移副本左上 + 右下各一次，再压白色正文）；描边**不依赖字体变体，一定生效**。
  - **Button 自带的文字没法描边** → 改为「`GUI.Button(r, GUIContent.none, style)` 画底 + `LabelOutlined` 单独画字」。落地 4 处：单位列表 / 物品列表 / 收藏文件夹列表（UG）+ `Er2Ui.TabGrid` 页签（共享层）。
  - 行文字字号同时 `FontBody+1 → FontBody+3`（13 → 15px）。
- **面板配色对齐 HUD 提示条（用户指着它说"背景色挺好的"）**：**去掉所有面的蓝调**（三通道相等）——`PanelBg #1A1A22 → #101010` / `TitleBar → #1C1C1C` / `Surface → #282828` / `SurfaceHover → #3A3A3A` / `SurfaceActive → #585858` / `RowBg → #1E1E1E` / `RowBgAlt → #2A2A2A`；**`PanelAlpha` 默认 `0.85 → 0.72`**（与 `Scrim` 黑@0.72 一致）——面板因此与 HUD 提示条一样会把地形色带出来（暖灰褐），不再发冷。
- 验证：编译 0 error（SC + UG）；部署 DLL 227,328 B，sha256 `A26B8AB52DAF9808B58AC87713EAA2E4BD7C0B2BF533060D2E3B2582ACE25576`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.30.zip`。
  反编译复核：版本双写 1.4.30 ✓；`LabelOutlined` 定义 + `TabGrid` 调用 ✓；`MonoPanelBg` = (0.0627, 0.0627, 0.0627) 即 `#101010`（R=G=B 无蓝调）✓；`MonoSurface` = `#282828` ✓；`MonoRowBg` = `#1E1E1E` ✓；`PanelAlpha = 0.72f` ✓。

**1.4.29（2026-09-25，用户第 30 轮："每个选项下不都有一个小横线吗，收藏里怎么没了。文本和星星还是太暗了，你到底改了什么。"）**：
- **底色再提亮一档**：`PanelBg #121218 → #1A1A22` / `TitleBar → #26262F` / `Surface → #32323C` / `SurfaceHover → #47474F` / `SurfaceActive → #6E6E80` / `RowBg → #24242C` / `RowBgAlt → #2E2E38`；`ListBg` α `0.50 → 0.42`（减少"黑洞"感）。
- **⚠️ 关键认知修正（"文本太暗"的真正原因）**：我前一版把文字提纯到 `#FFFFFF`、把面板底连提两档、把金色提亮——**都没解决"感觉不亮"**。真因是**笔画粗细**：纯白叠在 `#1A1A22` 上**对比度已达约 15:1**（远超可读阈值），缺的是**笔画厚度**——12px `FontStyle.Normal` 的笔画只有 1 像素宽，在大面积深色上会显得灰弱。
  修法：**列表行 `FontStyle.Bold` + 字号 +1**（`FontBody+1`）。
  新陷阱 103：**"亮不亮" = 颜色对比 + 笔画覆盖率两个独立量；用户说"暗"时两个都要查、先查笔画**（同陷阱 95/98——感知问题要用感知的办法解）。
- **星标提亮（同类问题）**：`starSize 14 → 17`、加 `FontStyle.Bold`、金色 `#FFD800 → #FFE81A`——小号 ★ 字形笔画极细，颜色再正也显得暗。
- **新陷阱入指南**：104（分隔线要"每行都画"，不要只在行间画；"语义正确"≠"视觉一致"）。
- 验证：编译 0 error（SC + UG）；部署 DLL 227,328 B，sha256 `4CFE49D71C5AEF5959F7FF81E6A4A4C5A1029CA5C1F72162EE33904842D15780`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.29.zip`。
  反编译复核：版本双写 1.4.29 ✓；`MonoPanelBg` = `#1A1A22` ✓；`MonoRowBg` = `#24242C` ✓；`MonoRowBgAlt` = `#2E2E38` ✓；`StarOn` = `#FFE81A` ✓。

**1.4.28（2026-09-25，用户第 29 轮："还是很暗，同时下面的文字超出还没修好。收藏改为每一个生成文件夹的子文件夹，在打开主文件夹后显示。"）**：
- **面板提亮一档（"还是很暗"）**：底色整体上一档且仍为中性黑——`PanelBg #08080A → #121218` / `TitleBar #101013 → #1C1C24` / `Surface #18181C → #26262E` / `SurfaceHover → #3E3E48` / `SurfaceActive → #626272` / `RowBg #0E0E11 → #1A1A20` / `SurfaceDisabled → #16161A@0.66`；文字保持纯白、线条保持白（1.4.27 已定）。提示：若仍嫌暗，可把 `UI/uiPanelAlpha` 往 1.0 调（更实＝更亮）。
- **⚠️ "下面的文字超出还没修好" —— 比对截图后判定为「跑的是旧 DLL」**：用户截图里的英文文案仍是 1.4.27 **之前**的 99 字符旧句（`…or on the ground to spawn it`），而 1.4.27 已缩到 66 字符。BepInEx 插件**必须重启游戏**才会重新加载 → 不是改动无效。已在回复中给出核对方法（看启动日志里的版本号）。**本轮仍把这条彻底做死**：`helpStyle.wordWrap = true` + 行高 38 → **56**（两行）+ 对齐改 `UpperLeft`——**窄面板 + 长英文的组合不可能靠缩字号/缩短文案根治**（前两轮都在那个方向上打转）。
- **新陷阱入指南**：102（用户说"改了但没变"时先核对版本号 / 是否重启；"代码写了" ≠ "代码在跑"，与陷阱 81 同源）。
- 验证：编译 0 error（SC + UG）；部署 DLL 227,328 B，sha256 `F59AC09D3A54FAF9C9B22EC03D2399504C84CBAA09954AC862B5DE19B552F442`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.28.zip`。
  反编译复核：版本双写 1.4.28 ✓；`MonoPanelBg` = (6/85, 6/85, 8/85) 即 `#121218` ✓；`MonoSurface` = `#26262E` ✓；`MonoRowBgAlt` = `#23232A` ✓。

**1.4.27（2026-09-25，用户第 28 轮："我说下面的文字超出了，你怎么还没改。还有菜单里文本和灰色线条改成白色，现在对比太低…收藏的黄色星星也太暗了，我说透明只是指背景透明。"）**：
- **"透明只指背景"（用户的澄清 = 本轮的设计修正）**：1.4.24 把**线条的 α 也绑到了 `PanelAlpha`**（`WithA(MonoPanelBorder, PanelAlpha + 0.10f)` / `WithA(MonoEdge, PanelAlpha + 0.08f)`）——调面板透明度会**连边框和分隔线一起变淡**，这正是"对比太低"的根因。修法：**面**（`PanelBg`/`TitleBar`/`Surface`/`SurfaceHover`/`SurfaceActive`/`RowBg`）继续跟随 `PanelAlpha`；**前景（文字 + 线条）完全不透明**——`PanelBorder`/`Edge` 直接返回固定色，不再过 `WithA`。
- **前景改纯白**：`MonoText #F0F0F2 → #FFFFFF`（纯白）；`MonoTextDim #CACAD0 → #E4E4E8`（α 1）；`MonoTextDisabled #92929A → #A8A8B0@0.90`；`MonoPanelBorder #555560 → 白@0.85`；`MonoEdge #45454E → 白@0.65`；`MonoEdgeSoft 白@0.15 → 白@0.30`；HUD cfg `colorText #F0F0F2 → #FFFFFF`。
- **收藏星星太暗（根因：两处颜色相乘）**：`StarButton` 用 `GUI.contentColor = StarOn(#FFD45E)` 上色，而 `starStyle` 的 `normal.textColor` 是 `TextDim(#CACAD0)` → **逐通道相乘** = `#CAA84D`（浑浊暗黄）。修法：**星标样式 textColor 改纯白**（白色是乘法单位元），`contentColor` 完全决定颜色；`StarOn` 同时提亮为 **`#FFD800`**。
- **"下面的文字超出"真因是横向（我上轮改错了方向）**：第 26 轮我按"贴边"改了**底部留白**（8→18），但真正的问题是**横向溢出**——英文文案 99 字符在 12 号下约 **570px**，而面板内宽只有约 **450px**，右边缘被裁断。修法：① 英文文案缩短为 `"Click to pick up - drop on a unit (backpack) or the ground (spawn)"`（99 → 66 字符）；② 该行改用**专用 `helpStyle`** + `Er2Ui.FitSize` 按可用宽度自缩字号（**不能改共享 `textStyle` 的字号**——会污染列表行，陷阱 76 同源）；③ 底部留白同时 18 → **22**（`30f * Scale`，两手都做，但**先修主因**）。
- **新陷阱入指南**：100（`GUI.contentColor` 与 `GUIStyle.normal.textColor` 相乘——想精确上色样式必须纯白）/ 101（"文字超出容器"先量哪个方向；横向溢出改纵向留白是白费功夫）。
- 验证：编译 0 error（SC + UG）；部署 DLL 226,816 B，sha256 `B1D65DED63894E37E70862FF23E28F3F83C49FC70AFA25E99AEFC5464F8A7FAD`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.27.zip`。
  反编译复核：版本双写 1.4.27 ✓；`MonoText` = 纯白 (1,1,1,1) ✓；`MonoPanelBorder` = 白@0.85 ✓；`MonoEdge` = 白@0.65 ✓；`MonoEdgeSoft` = 白@0.30 ✓；`StarOn` = (1, 72/85, 0) 即 `#FFD800` ✓；`PanelBorder`/`Edge` 属性**不再出现 `WithA(.., PanelAlpha + ..)`**（仅面保留）✓。

**1.4.26（2026-09-25，用户第 27 轮："改成近大原小"＝近大远小）**：
- **"近小圆大"的根因 = 两套尺度规则混用（本轮核心，也是 1.4.21~1.4.25 那条线的根治）**：
  - 标记半径 `UnitRingRadius(s)` / `VehicleRingRadius(v)` 是**固定世界值**（从 Collider 的 bounds 量出，clamp 0.35~1.1m / 1.6~4.2m）→ 环天然**近大远小**。
  - 而线宽 1.4.24 起用"**屏幕像素恒定**"（`width_m ∝ dist`）→ **世界宽度随距离增大**：1.5px @60m ≈ 0.10m、@200m ≈ 0.34m，而环半径只有 0.6m → **线宽逼近半径 → 环被填成实心大圆盘**。用户原话"这个标记近小圆大"精确描述了这个现象。
  - **修法：线宽回到固定世界米**。`Er2Ui.LineWidth(baseMeters, camDist)` 改为 `Clamp(baseMeters, 0.003f, 3f)`（camDist 保留在签名里但**不再参与换算**——线宽回到世界空间后，距离就不该是它的输入）。13 处基准由像素值换算为米：步兵脚环 0.032 / 载具脚环 0.050 / 选中步兵角标 0.042 / 选中载具角标 0.060 / 集火环 0.065 / 移动目标点 0.042 / 步兵路径 0.030 / 载具路径 0.038 / 登车线 0.038 / 阵型主线 0.060 / 阵型虚线 0.030 / 阵型角标 0.048（×2）。**代价是有意的**：极远处细到亚像素——那就是真实透视的表现，不做人为补偿。
- **顺带清掉 FOV 注入链**（死的了）：`Er2Ui.RefScreenH` / `camFovDeg` / `CamFovDeg` / `SetCamFov` 全部删除，`GodViewController.CameraGroundDist()` 里的注入调用一并去掉——线宽不再依赖 FOV 后，这些就是纯死代码（陷阱 81）。`CameraGroundDist()` 本身保留（`MarkerCamDist()` 仍用它，且 `camDist` 参数还在签名里）。
- `markerLineWidth` cfg 语义与说明同步（"在世界空间基准线宽之上再乘"），EN 表键同步，复核 0 缺失。
- **新陷阱入指南**：99（同一"物体+轮廓"的尺度规则必须统一；混用世界/屏幕 → 远处崩坏）——陷阱 97 的根治。
- 验证：编译 0 error（SC + UG）；部署 DLL 226,816 B，sha256 `C32C79C148DB0E3A58E44577389776FAE7E4769041A1AE8B37A1E1452E56D194`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.26.zip`。
  反编译复核：版本双写 1.4.26 ✓；`LineWidth` = `Clamp(baseMeters, 0.003f, 3f)`（纯世界米）✓；`SetCamFov`/`camFovDeg`/`RefScreenH`/`LineWidthUnused` **0 处** ✓；13 处世界米线宽（0.030×2 / 0.032 / 0.038×2 / 0.042×2 / 0.048×2 / 0.050 / 0.060×2 / 0.065）✓。

**1.4.25（2026-09-25，用户第 26 轮："还可以，但是对比不明显，同时最下面的字都超出菜单了。还有3D 标记改为上一版，同时之所以看起来细是因为这个标记近小圆大，我又把镜头拉太近导致的。"）**：
- **线宽回退到 1.4.23 的数值**（用户明确要求）：2.4→1.5 / 3.2→2.0 / 3.5→2.2（×2）/ 4.2→2.6 / 4.8→3.0 / 2.7→1.7 / 2.9→1.8（×2）/ 4.0→2.5 / 3.8→2.4（×2）。**"太细"的真因（用户自己定位）= 镜头拉太近**：标记半径是世界固定值，拉近后圆环在屏幕上占很大面积，而线宽是"屏幕像素恒定" → **线宽/孔径比例骤降** → 观感变细。像素恒定在跨分辨率意义上仍正确，但"粗不粗"的感知是**相对**的（陷阱 97）。保留 `[Markers] markerLineWidth` 供玩家统一放大。
- **修"最下面的字超出菜单"（底部呼吸余量）**：`PanelRect` 高度 = `16*Scale + Σ(H+Gap)`，内容从 `+8*Scale` 起画 → 末行底部距面板底**恰好 8px**——数学上没溢出，但视觉上就是贴边。改为 **26f * Scale**（顶部 8 + 底部 18），`ItemHelp` 行高 34 → **38**。
- **对比度一轮（"对比不明显"）**：
  - 面板描边 `#3C3C42@0.95` → **`#555560@0.95`**，宽度 `Max(1f, s)` → **`Max(1.5f, 2f*s)`**（约 2px）——半透明面板在亮背景（石头/水泥地）上边界会被吃掉，一圈更亮的粗边框是最直接的"面板到此为止"信号。
  - 选中底 `#3A3A42` → **`#52525E`**（选中态更突出）；悬停 `#28282E` → `#32323A`。
  - 行底 `#111114` → `#0E0E11`（更沉）；列表底 α 0.40 → **0.50**（更实）；行分隔线 `EdgeSoft` 0.10 → **0.15**。
  - 次要文字 `#B8B8BE@0.92` → `#CACAD0@0.94`；禁用文字 `#85858C@0.80` → `#92929A@0.82`；分隔线 `#38383E` → `#45454E`。
- **新陷阱入指南**：97（像素恒定 ≠ 感知粗细；近处环大线细）/ 98（贴边读作溢出；感知问题不能用几何正确性回答）。
- **踩坑（连续第二次）**：线宽替换脚本里 plan 的新值已含 `f`（`'1.5f'`），格式化又拼 `'%sf'` → 产出 `1.5ff` → CS1003 ×13。**教训：写这类脚本时"新值是否带单位后缀"必须固定一种约定**，且**批量改完必须编译**。
- 验证：编译 0 error（SC + UG）；部署 DLL 227,328 B，sha256 `D30FD7FABA8F90B6E0C5FDE5A8C28434541CD12C21FD4B54D90DEE4CAB9752DC`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.25.zip`。
  反编译复核：版本双写 1.4.25 ✓；13 处线宽全部回到 1.5~3.0（1.5/1.7×2/1.8×2/2/2.2×2/2.4×2/2.5/2.6/3）✓；`MonoPanelBorder` = (1/3, 1/3, 32/85) 即 `#555560` ✓；`MonoSurfaceActive` = `#52525E` ✓；`MonoEdge` = `#45454E` ✓；UG 侧 `26f * Er2Ui.Scale`（底部留白）✓、`ItemHelp H = 38f * Er2Ui.Scale` ✓、`Frame(r, PanelBorder, Max(1.5f, 2f*scale))` ✓。

**1.4.24（2026-09-25，用户第 25 轮："3D标记还是太细了。同时把面板UI改成半透明黑色，不要棕色了。"）**：
- **线宽再 +60%（"还是太细"）**：1.4.23 的 1.5~3.0px 仍偏细 → 13 处基准整体 ×1.6：步兵脚环 1.5→2.4 / 载具脚环 2.0→3.2 / 选中步兵角标 2.2→3.5 / 选中载具角标 2.6→4.2 / 集火环 3.0→4.8 / 移动目标点 2.2→3.5 / 步兵路径 1.7→2.7 / 载具路径 1.8→2.9 / 登车线 1.8→2.9 / 阵型主线 2.5→4.0 / 阵型虚线 1.7→2.7 / 阵型角标 2.4→3.8（×2）。主力标记落在 3.5~4.8px。
- **面板改中性黑（"不要棕色了"）**：所有结构色改为 **R=G=B**（去色相）——`PanelBg #08080A` / `PanelBorder #3C3C42` / `TitleBar #101013` / `Surface #1A1A1E` / `SurfaceHover #28282E` / `SurfaceActive #3A3A42` / `RowBg #111114` / `ListBg 黑@0.40` / `Edge #38383E`；文字 `#F0F0F2` / `#B8B8BE` / `#85858C`；`Accent` 近纯白；`Scrim` 中性黑@0.72。**皮革噪声纹理同步去暖调**（`new Color(g, g*0.96f, g*0.90f)` → `new Color(g,g,g)`）——中性黑底上叠暖色只会发黄。
- **面板不透明度做成 cfg（"半透明"+"黑"是一对矛盾轴）**：新增 `UI/uiPanelAlpha`（默认 **0.85**，范围 0.55~1.0）。实现：色板常量**只存 RGB**，α 由 `Er2Ui.PanelAlpha` 统一推出（标题条 `+0.05` / 悬停 `+0.07` / 选中 `+0.13` / 行底 `−0.05` / 描边 `+0.10` / 分隔线 `+0.08`），属性写 `WithA(MonoXxx, PanelAlpha + 偏移)`。两个 mod 各自 Bind 同名同义 cfg 并调 `SetPanelAlpha`。**玩家只调一个数，相对层次固定。**
- HUD cfg 默认色同步改中性黑：`colorBase #0A0A0DEE` / `colorHover #2A2A31F5` / `colorText #F0F0F2`。
- **EN 表同步**：本轮 cfg 文案变更 5 条（含 4 条键漂移）+ 新增 `uiPanelAlpha` 键 → 脚本删旧键插新键，复核两个 mod **均 0 缺失**。
- 新陷阱：96 补"矛盾轴交给 cfg"（不要替玩家猜）。
- 验证：编译 0 error（SC + UG）；部署 DLL 227,328 B，sha256 `A00BF6F38FCCC72C324CC369886D5A9D077C89FF0C5E4B12860CE52B82552DCE`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.24.zip`。
  反编译复核：版本双写 1.4.24 ✓；`MonoPanelBg` = (0.0314, 0.0314, 0.0392) 即 `#08080A`（R=G，B 略高）✓；`MonoSurface`/`MonoEdge` 同为中性 ✓；皮革像素 `new Color(num4, num4, num4, 1f)`（去暖调）✓；`PanelAlpha` 16 处 + `SetPanelAlpha` + `WithA` 在位 ✓；13 处线宽全为上调后值（2.4/2.7×2/2.9×2/3.2/3.5×2/3.8×2/4/4.2/4.8）✓；`uiPanelAlpha` 5 处 ✓。

**1.4.23（2026-09-25，用户第 24 轮："现在太细了，再粗一点点" + "这完全就是棕色，黑色呢，我要皮革的那种感觉，然后更黑一点" + "菜单最上面的按钮你没发现重叠了吗"）**：
- **线宽 +40%（"太细了"）**：1.4.22 把相机距离修对后线宽回到真实量级（1.1~2.2px），用户觉得偏细 → 13 处基准整体上调约 40%：步兵脚环 1.1→1.5 / 载具脚环 1.4→2.0 / 选中步兵角标 1.6→2.2 / 选中载具角标 1.9→2.6 / 集火环 2.2→3.0 / 移动目标点 1.6→2.2 / 步兵路径 1.2→1.7 / 载具路径 1.3→1.8 / 登车线 1.3→1.8 / 阵型主线 1.8→2.5 / 阵型虚线 1.2→1.7 / 阵型角标 1.7→2.4（×2）。
- **"完全是棕色"的根因 = 半透明被地形染色**：α 0.82 的面板叠在**棕色泥地**上时 `dst = src×0.82 + terrain×0.18`，地形是棕的、面板就永远是棕的——**压配色板没用**（只压了那 82%）。"半透明"与"黑"在亮色背景上互相矛盾。修法：α 0.82 → **0.92**（保住透出战场的观感，同时压住染色），底色再压到**近黑** `#0C0906`（`PanelBg` 由 `#1E1813` 换掉）；配套 `PanelBorder #46382A@0.95` / `TitleBar #140F0B@0.94` / `Surface #1B150F@0.90` / `SurfaceHover #2A2017@0.94` / `SurfaceActive #3B2C1F@0.97` / `RowBg #130F0B@0.86` / `ListBg 黑@0.45` / `Edge #3A2E22@0.92`。
- **皮革质感（"我要皮革的那种感觉"）**：近黑矩形仍只是"一块色板"，皮革观感来自**斑驳 + 受光边缘**。新增 `Er2Ui.LeatherTex()`（64×64 程序化 value noise，两层倍频 `PerlinNoise(x*0.09)` + `PerlinNoise(x*0.37+31.7)`，灰度刻意 0.34~0.66 只压暗不泛灰，`wrapMode=Repeat`、`hideFlags=61` 陷阱 12）、`Leather(Rect, alpha)`（`GUI.DrawTextureWithTexCoords` 平铺，默认 0.10）、`PanelBase(Rect)`（近黑填充 + 皮革 0.10 + 顶部 1px 受光边 `EdgeSoft`）。接入点：`GenPanel` 面板底 + 标题条（0.14）、`BackpackPanel` 底板 + 标题条、HUD 底部提示条（0.10）。
- **标题按钮重叠（"菜单最上面的按钮你没发现重叠了吗"）**：标题条高 26px，而内容"从 +8 开始、高 22px" → 按钮底部 30 > 26，**压在标题条下的分隔线上**（Clear / × 两个按钮）。修法：Title 行高 26 → **34f * Scale**（= 8 上留白 + 22 内容 + 4 下留白）。
- **cfg 默认色同步压黑**：`colorBase #201812D9 → #0F0B08EA`、`colorHover #44352AE6 → #2A2017F0`（描述文字同步为"默认近黑皮革色"/"默认深棕"）。
- **新陷阱入指南**：96（半透明被背景染色；质感靠纹理不靠颜色；容器高度 ≥ 内容 + 留白）。
- 验证：编译 0 error（SC + UG）；部署 DLL 226,816 B，sha256 `7BC2553ADBCD24CA62CBB2B613A438D58CB0853607902FF7C581EA8BB5818DA9`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.23.zip`。
  反编译复核：版本双写 1.4.23 ✓；`LeatherTex`/`Leather`/`PanelBase` 三个成员在位 ✓；`MonoPanelBg` = (4/85, 3/85, 2/85, 0.92) 即 `#0C0906@0.92` ✓；`MonoPanelBorder` = `#46382A@0.95` ✓；13 处线宽全为上调后值（1.5/1.7×2/1.8×2/2/2.2×2/2.4×2/2.5/2.6/3）✓；`Leather` 调用点在 GVC（提示条）+ BackpackPanel ✓。

**1.4.22（2026-09-25，用户第 23 轮："我不是说了太粗了吗，还有为什么还有这么多锯齿。要黑棕色半透明的UI。还有UI各元素区分不明显，还有背景与文字的重叠。"）**：
- **"还是太粗"的真因 = 相机距离取错（本轮核心）**：`MarkerCamDist()` 原返回 `cam.transform.position.magnitude`——那是相机到**世界原点**的距离，不是到标记的距离；ER2 地图原点离战区可达数百米 → 该值永远是错误的大数。**旧版被 `Clamp(0.6, 2.5)` 倍率掩盖**（最多 2.5 倍，只是"有点粗"）；1.4.21 改线性像素公式后错值直接进线宽 → 环被填成实心圆盘、角标糊成粗 X（截图实证）。修法：新增 `GodViewController.CameraGroundDist()`——`Physics.Raycast(cam.position, cam.forward, 3000f)` 命中点求真实距离，打不中时用相机高度兜底，0.1s 缓存；`MarkerCamDist()` 与 `Formation.MarkerCamDistForFormation()` **共用同一份**（原为两处各写一遍）。
- **`Ring` 同款父缩放修复**：环原先仍 `localScale = (radius,1,radius)` → 线宽被 `lossyScale` 连带放大（载具 ≤4.2 倍），这是"环糊成圆盘"的另一半成因。改父 scale 恒 `Vector3.one` + 半径写进顶点（复用 `Mark.bracketPts[0]`），并加 `lastRadius` 缓存——半径没变就不重写 64 个顶点（脉动环每帧变、脚环恒定，各取所需）。
- **抗锯齿（"这么多锯齿"）**：`LineRenderer`/Mesh 不吃 MSAA。新增 `FeatherTex()`（2×16 竖向 alpha 渐变，两侧各 25% `SmoothStep`，**RGB 预乘 alpha** 防黑边）供线材质按宽度方向羽化（`LineRenderer` 的 UV.y 恰好跨宽度 0..1）；新增 `DotTex()`（32×32 径向羽化）供圆盘 + mesh 补 UV 辐射。环分段 48 → 64（近距离折角可见）。
- **黑棕半透明 UI（"要黑棕色半透明的UI"）**：Mono 预设整体换暖色 + 半透明——`PanelBg #1E1813@0.82` / `PanelBorder #6E5844@0.92` / `TitleBar #2A2119@0.86` / `Surface #32271E@0.84` / `SurfaceHover #44352A@0.90` / `SurfaceActive #584331@0.96` / `RowBg #241C15@0.80` / `Edge #57452F@0.92`；文字暖白 `#F1EBE2` / `#C9BCAB@0.92` / `#96897A@0.80`；`Scrim` 改暖黑 `#140F0A@0.72`；`Accent` 改暖白。HUD cfg 默认色同步：`colorBase #201812D9` / `colorHover #44352AE6` / `colorText #F1EBE2`。`uiMono` 描述同步为"黑棕半透明 UI"。
- **元素区分（"UI各元素区分不明显"）**：`TabGrid` 里**每个页签都描边**（未选中 `Edge` 暖棕 / 选中 `Accent` 暖白），页签内缩 6f → `19f * Scale`（长标签不再贴边，专治 EN 版 "Medical/Food"）；阵营三按钮与乘员按钮同款描边；两个列表行间加 `EdgeSoft` 极细分隔线；列表行/星标/空列表提示的硬编码像素全部 × Scale；`DrawUiButton` 描边宽 1.5f 硬编码 → `Max(1f, 1.5f*Scale)`。
- **文字与背景重叠（"背景与文字的重叠"）**：根因两条——① `DrawCrewRow`/`DrawPreviewRow`/`DrawTitleRow`/`DrawFactionRow`/两个列表体/`Er2Ui.Pager` 里大量硬编码像素（`90f`/`24f`/`220f`/`20f`/`30f`/`38f`/`76f`）**未乘 `Scale`** → 缩放后行变大了行内控件没变，文字压到邻行；② **行间距只内嵌在部分行高里**（`List`/`SubTabs` 有 `+ Gap`，`Crew`/`Preview` 没有）→ 后者紧贴。修法：全部尺寸 × Scale；行高定义去掉内嵌 Gap，**间距统一在行循环加一次**（`y += row.H + (i + 1 < rows.Count ? Gap : 0)`，面板总高用同一条规则累加）。
- **EN 表补全**：写脚本机械比对 `Ui.Tr` 字面量 vs 字典键（陷阱 17g7），SquadCommand 缺 9 条、UniGen 缺 1 条 → 全部补英文，并清掉 3 条漂移旧键（`UI 主色…`/`UI 悬停…`/`UI 文字/描边颜色。`）。复核后**两个 mod 均 0 缺失**。
- **新陷阱入指南**：93（"距离"必须问清参照系）/ 94（自适应 UI 里尺寸与间距都要单一数据源）/ 95（线不吃 MSAA，抗锯齿靠贴图羽化）。
- 验证：编译 0 error（SC + UG）；部署 DLL 225,792 B，sha256 `49C9C886197AEE126D2166C5973032EF2519B75AE57E51AAD70722797C966385`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.22.zip`。
  反编译复核：版本双写 1.4.22 ✓；`position.magnitude` **0 处** ✓；`CameraGroundDist` 定义 + 2 个调用点（GVC / Formation）✓；`localScale = Vector3.one` 2 处（Bracket + Ring）✓；`FeatherTex`/`DotTex` 在位 ✓；`Segments = 64` ✓；黑棕色值（`#1E1813@0.82` / `#6E5844@0.92` / `#57452F@0.92`）✓；`TabGrid` 内 `Frame(r, on ? Accent : Edge, …)` ✓；`Pager` 的 `30f * Scale` / `8f * Scale` ✓；UG 行间 Gap 两处（绘制 + 高度）✓；UG `EdgeSoft` 分隔线 2 处 ✓。

**1.4.21（2026-09-25，用户第 22 轮："1.绘制的线条都太粗了 2.颜色浅了，而且只有单一的色块没有设计感 3.选中单位后的标记怎么变成箭头了 4.半透明灰色质感不好"）**：
- **线宽改「像素」语义（"线太粗"）**：`LineRenderer.widthMultiplier` 是世界单位，屏幕像素宽 = `width_m × screenH / (2·dist·tan(fov/2))`。写死世界单位近粗远细；用经验倍率（`dist/30`）补偿则与分辨率强耦合（1440p 粗 1.33×、4K 粗 2×）。**第一轮曾把"改除数 30→60"与"基准值砍 55%"叠加，实际落到 ~0.6px（细到看不见）**——量纲算清后改为：调用点只写 1080p 目标像素宽，`Er2Ui.LineWidth(px, dist) = px × 2 × dist × tan(fov/2) / 1080`（screenH 约掉 → 占屏比例与分辨率无关）。层次：步兵脚环 1.1 / 虚线 1.2~1.3 / 载具脚环 1.4 / 选中角标 1.6~1.9 / 阵型 1.7~1.8 / 集火环 2.2。FOV 由 `Er2Ui.SetCamFov(cam.fieldOfView)` 从 `MarkerCamDist()` 与 `MarkerCamDistForFormation()` 注入，取不到按 60° 兜底。旧 `WidthScale` 经验倍率删除。
- **选中角标不再是"箭头"（根因回源码核实，非照截图猜）**：角标根对象 `localScale = (radius,1,radius)`，而线宽被父 `lossyScale` 连带放大 → 载具上（radius ≤ 4.2）0.1 m 变 ~0.5~1.0 m，折角臂长仅 0.34×radius ≈ 1 m → 两条粗臂糊成实心三角块。**这也是"线太粗"的同一个根因。** 修法：父 scale 恒 `Vector3.one`、半径写进顶点（`Mark.bracketPts` 缓冲复用，零分配），臂长 0.34 → 0.42；顺带把每帧 `new float[4]` 提为 `static readonly BracketSX/SZ`。
- **世界标记从半透明改灰阶实色（"半透明灰色质感不好"）**：α 0.26~0.48 会被草地/雪地/沙地吃掉（颜色随背景漂移、边界发虚）。改 α ≥ 0.80 的灰阶实色，层次靠灰度值：`WPath #9AA1A8` → `WFriendly #8E959C` → `WBoard #C6CBD0` → `WVehicle #CBCFD0` → `WFormation #E2E6EA` → `WMove #F2F5F7` → `WSelected 纯白`；仅 `WGhost #B8BCC0@0.32` 保留半透（预览语义）。
- **面板补结构线（"只有单一色块没设计感"）**：新增 `Frame`（矩形描边）/ `HLine`（分隔线）/ `AccentBar`（左竖条）三个原语 + `Edge`/`EdgeSoft` 令牌；`GenPanel` 加标题条独立底色 + 标题下分隔线 + 面板外框 + 两个列表内凹边框 + 收藏行左竖条；`BackpackPanel` 底板不再 ×0.5、加标题条、描边/分隔线走共享令牌、格底改 `Er2Ui.ListBg`。层次 = 面（明度档）+ 线（边框/分隔）+ 条（选中）。
- **配色压回中深灰（"颜色浅了"）**：1.4.20 提亮过头。`PanelBg #14181D@0.95` / `TitleBar #1F252C@0.97` / `Surface #262D35@0.95` / `SurfaceHover #333B45@0.97` / `SurfaceActive #46505C` / `RowBg #1B2027@0.92` / `ListBg #050708@0.42`；文字 `#E9EDF1` / `#B2BAC3@0.92` / `#7B838C@0.80`。HUD cfg `colorBase #1E1E1EE6 → #262A30F0`、`colorHover #3A3A3AF2 → #454E59F5`。
- **顺带**：`GenPanel.BuildRows` 6 处固定行高漏乘 `Scale` 修掉；清掉 `SquadCmdLogic.resMultCache/resMultNext` 两个死字段（陷阱 81）；补 EN 表缺失的 12 条 Markers cfg 描述。
- **新陷阱入指南**：89（线宽应为像素语义）/ 90（父 lossyScale 放大线宽，别用缩放传参）/ 91（半透明在 3D 差，层次移到灰度值）/ 92（缺结构线 = 没设计感）。
- 验证：编译 0 error（SC + UG）；部署 DLL 222,720 B，sha256 `3CFA3DDCF6B2BA51BAAD239A6BCD49EDD91618470E8D8C98AAC0C3DF7B86D1D3`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.21.zip`。
  反编译复核：版本双写 1.4.21 ✓；`LineWidth` 像素模型（`pxAt1080 * 2f * d * tanHalf / 1080f`）✓；`SetCamFov` 注入 2 处 ✓；旧 `WidthScale` 0 处 ✓；`localScale = Vector3.one` ✓；臂长内联 `0.58`（=1−0.42）✓；`bracketPts` 复用 ✓；`BracketSX/SZ` 静态 ✓；13 处像素线宽（1.1 / 1.2×2 / 1.3×2 / 1.4 / 1.6×2 / 1.7×2 / 1.8 / 1.9 / 2.2）✓；`Frame/HLine/AccentBar` 在位 ✓；`colorBase #262A30F0` / `colorHover #454E59F5` ✓。

**1.4.20（2026-09-25，用户第 21 轮："UI 不能是死的，要是可以动态调整的。不要加多余的数值显示等。现在的UI太黑了。全部都要改，不要漏，用游戏原生方式实现" + 澄清"动态调整指自适应"）**：
- **UI 自适应（本轮主题）**：
  - **跟随游戏原生倍率**：`Er2Ui.NativeResMult()` 读 `ResourcesManager.ResolutionMult`（就是玩家在游戏设置里调的 UI 大小，原生 HUD 全部按它缩放），取不到才按 `min(Screen.w/1920, Screen.h/1080)` 兜底；0.5s 缓存；clamp `[0.75, 1.6]`。原 `SquadCmdLogic.ResMult()` 改为转发，实现下沉到共享层（两 mod 共用一份缓存，算法不再漂移）。
  - **令牌属性化**：`Pad/Gap/RowH/TabH/BtnH/FontTitle/FontBody/FontSmall/FontTabMax/FontTabMin/PanelW` 由 `const` → 随 `Scale` 的**属性**——这是"UI 是死的"的根因（`const float PanelW = Er2Ui.PanelW` 在编译期钉死，运行时推不动）。新增 `Scale/SetScale(clamp+清缓存)/ScaleChangedSince/PanelW/ScreenMult/NativeResMult/AutoScale/ScreenFit`。
  - **接入范围（"不要漏"）**：`GodViewController.DrawHud`（底部提示条 `1400px → ScreenFit(1400)`，HUD 位置/尺寸/框选描边全部 × Scale，控制/分队/合并三个按钮矩形 × Scale）、`InfoPanel`（W/H/BottomGap/LeftX 属性化 + 样式重建）、`BackpackPanel`（Cell/Gap/Margin/TitleH/WinW/WinH 属性化；倍率变化时**保留玩家拖动位置**只改尺寸并 clamp 回屏幕；菜单 `MenuW/MenuRow` 与命中判定共用一份；tooltip/拖拽幽灵/格子内像素量 × Scale）、`SquadCmdLogic.ResMult` 转发。
  - **自适应入口**：SC 三处（`DrawHud` / `InfoPanel.Draw` / `BackpackPanel.Draw`）、UG 两处（`GenRunner.Draw` 总入口 / `GenPanel.Draw`）——UG 的"携带/放置"两支不经过 `GenPanel.Draw()`，故必须挂在总入口。
  - **样式重建各判各的**：`ScaleChangedSince(styleScale)`，不用全局 dirty（多面板共用一个会被第一个清掉 → "一半变了一半没变"）。
- **配色提亮（"现在的UI太黑了"）**：Mono 预设整体上抬并拉开层次差——`PanelBg #1C2024@0.93` / `TitleBar #2C3137@0.96` / `Surface #363C43@0.93` / `SurfaceHover #454C55` / `SurfaceActive #5A636E` / `RowBg #2A2F35@0.90` / `ListBg #080A0C@0.32`；文字 `Text #F2F4F6` / `TextDim #C2C8CE@0.92` / `TextDisabled #8A9199@0.80`。仍是中性灰黑（不回军绿），靠明度差而非色相分层。
- **UniGen 残留绿色清零**：携带徽标底（旧军绿 → `Scrim`）、拖拽目标环（亮绿 `0.45,1,0.5` → `WSelected` 白）、生成反馈文字色（淡绿 → `Er2Ui.Text` / 错误 → `Er2Ui.Danger`）。
- **新陷阱入指南**：84（const 令牌让自适应失效）/ 85（跟随原生倍率而非自己算）/ 86（入口挂总入口）/ 87（各判各的重建）/ 88（硬编码定宽窄屏溢出）。
- 验证：编译 0 error；部署 DLL 219,648 B，sha256 `8EC63CEBF9A7BBB170944228656D9ACD23B25287802F81539EC890D3863B28C0`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.20.zip`。
  反编译复核：版本双写 1.4.20 ✓；`SetScale/ScaleChangedSince/NativeResMult/AutoScale/ScreenFit` 在位 ✓；`ResourcesManager.ResolutionMult` 1 处 ✓；`positionCount = 3` ✓；旧弧公式 `22.5` 0 处 ✓；`new GUIStyle(带参)` 0 处（陷阱 5）✓；14 项 cfg 键 ✓；军绿字面量仅剩 `LegacyPanelBg`（回退预设，有意保留）✓。

### 2.11.0 SquadCommand v1.4.19（历史）
**1.4.19（2026-09-24，用户第 20 轮："标记点等都用白色或半透明的灰色。我要那种灰黑色的UI。都给 cfg 开关" + "你这选中标记画的跟台风一样"）**：
- **UI 重绘一期落地**：
  - 灰黑单色 UI——`Shared/Er2Ui.cs` 结构色改中性灰黑（Panel/Surface/Row/Text 三组各两套：Mono 与 Legacy 由 `uiMono` 分派）；HUD 按钮色源默认值 `#1E1E1EE6/#3A3A3AF2/#E8E8E8`；底部提示条去掉军绿（`0.03,0.06,0.03` → `Scrim`）。
  - 选中标记形状修正：`SceneMarkers.GetBracket` 由 4 段 9 点圆弧（`start=q*90+22.5`、`45*i/8`）改为**直角折角**（`positionCount 9→3`，三点 `[edge 端点, 角顶点, 另一 edge 端点]`，`loop=false`）——直修用户"跟台风一样"。4 个调用点签名不变。
  - 世界空间颜色全量令牌化：`WFriendly(.28)/WFriendlyVeh(.34)/WSelected(.92)/WFocus/WMove(.85)/WPath(.26)/WBoard(.48)/WFormation(.80)/WVehicle(.60)/WGhost/WLabelPlate`；GVC 与 Formation 共 12 处字面量清零（反编译实证）。
  - 路线/登车线拆分：新增 `DashTex(bool shortDash)`（长划 14/32、短划 22/32）+ `DashTexLong/Short` 双槽；`Line(...)` 加 `shortDash` 参数（默认 false）；平铺周期长划 2m / 短划 1.2m。修此前 `pathC == boardC` 字面量完全相同。
  - 线宽距离补偿：`Er2Ui.WidthScale(camDist)=Clamp(camDist/30,0.6,2.5)`、`LineWidth(base,camDist)`；每帧一次 `camDist` 全帧复用。
  - 脉动错相：`Er2Ui.Pulse(key,t)` 按 FNV-1a 哈希错相（修"所有标记同频同相一起呼吸"）。
  - 名签底板：`SceneMarkers.EnsurePlate` 程序化 2 三角形面片 + 顶点色烘焙 `WLabelPlate`。
  - 光标灰阶：`MouseCursor.StateColor` 8 形状改灰阶（仅 Enemy 红 / Emplacement 橙）。
  - 幽灵预览：`(0.58,0.64,0.72,0.20)` → `Er2Ui.WGhost`（中性灰 @0.35，去掉蓝调）。
- **新增 14 项 cfg**：`[Markers]` 13 项（markersEnabled/showFriendlyRing/showSelectedBracket/showFocusRing/showMoveTarget/showPathLines/showFormationMarkers/showNamePlates/markerPulse/markerScale/markerLineWidth/markerThroughWall/markerColorMode）+ `[UI] uiMono`；全部 `SettingChanged → GodViewController.ApplyMarkerConfig()`。
- 新增 `SquadCmdLogic.LogWarning`（不受 debugLog 门控，失败可观测）。
- 验证：编译 0 error（3 warning，均为既有）；部署 DLL 217,088 B，sha256 `F3513EEE935B889099DEFDBAB17DD42327172BEB508CCE60F2A15B91ADDECB77`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.19.zip`。
  反编译复核：版本双写 1.4.19 ✓；14 项 cfg 键全在 ✓；`positionCount = 3`（角标）✓；旧弧公式 `22.5` 0 处 ✓；旧字面量 12 处清零 ✓；`new GUIStyle(带参)` 0 处（陷阱 5）✓；`_ZTest` 1 处（穿墙材质仍在）✓。

### 2.11.1 SquadCommand v1.4.18（历史）
**1.4.18（2026-09-24，用户第 19 轮：2.4.1 运行良好，要求优化代码 + 为两个 mod 的 UI 重绘做准备）**：
- **无玩法/视觉变化，纯地基**：`SquadCmdLogic.HudStyle/HudStyleSmall/ButtonStyle`、`InfoPanel.MakeSmall`、`BackpackPanel.MakeStyle` 四处手写 `GUIStyle` 全部改为转发共享层 `Er2Ui.MakeLabel`（`using ER2Shared;`），字号/对齐/配色/粗体语义逐项不变；csproj 加 `<Compile Include="..\Shared\Er2Ui.cs" Link="Shared\Er2Ui.cs" />`。
- 验证：编译 0 error；部署 DLL 206,336 B，sha256 `3E306522BDAA185AD6036397AFB7B78FD47EFBD6C41727BA53491A15DFDE9DD2`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.18.zip`；反编译复核：`[BepInPlugin]`+启动日志双 1.4.18、`Er2Ui.MakeLabel` 5 处调用在位 ✓。

**（以下为历史版本条目，标题保持 1.4.17）**
**1.4.17（2026-09-24，用户第 17 轮：让单位拾取枪械时可以隔空拾取）**：
- **根因（结构定案）**：地面物品右键按交互数分流——单交互 → `RequestItemPickup`（联动半径内即时 / 超出派兵走过去，正确）；多交互 → `OpenGroundMenu`，点条目 = **原样 `Interaction.Call()`**。**`HandheldItem`（`Weapon` 父类）覆写了 `GetInteractions`**（普通 `ItemObject` 不覆写，已反编译证实）→ 地面枪械天生多交互（"拾起置于右手"等，Ui 词典既有条目可证）→ **永远走菜单路径**；而原生拾起交互是为 FPS 玩家设计的，距离由玩家自身保证，`Call()` **无距离检查** → 隔空吸进交互者背包。普通物品不受影响，所以用户观察到"只有枪械隔空"。
- **修法**：`BackpackPanel` 菜单增 `menuRaw`（与 `menuLabels` 严格等长的**未翻译原文**平行列表——剪枝与三处合成"穿上"条目同步增删）；`ExecuteInteraction` 里 `menuGroundItem != null` 且原文以 **"拾起"** 开头 → 改调 `RequestItemPickup(item, item.transform.position)`（联动半径内即时 / 超出派最近选中士兵走过去到达再捡，与单交互物品**同一条链路**），`LogAlways` 记录改道；其余交互（弹药箱"补充弹药"等）保持 `Call()`。格子物品菜单（`menuWin != null`）不受影响。新增 Ui 词条 `["物品已失效"] = "That item is no longer there"`。
- 验证：编译 0 error；部署 DLL 203,264 B，sha256 `BDD7F1C26CCFCB4D0F7E0C3604238E6C25EC00342AC316C009F041AC0CFE8FBC`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.17.zip`（20:23 重打包含新 README/Nexus）；反编译复核：`[BepInPlugin]`+启动日志双 1.4.17、`menuRaw` 列表 + 三处 `"穿上"` 占位 + `StartsWith("拾起")` 重路由 + `LogAlways` 改道日志 + Ui 词条全部在位 ✓。

**（以下为历史版本条目，标题保持 1.4.16）**
**1.4.16（2026-09-24，配合 UniGen 2.3.0：放置/携带中滚轮被冻结）**：
- **用户反馈（经 UniGen 侧）"预放置时不能滚动滚轮改变视角"** → 根因：UniGen 放置/携带物品期间把 `externalGuiBlock` 报成**全屏 Rect**（防投放点击误触框选/指令），而 `UiPointerCapture()` 直接复用 `IsMouseOverGui()` → 同一个布尔既吞点击手势**又冻结相机**（`HandleHeight` 滚轮、`HandleDrag` 中键起手）→ 全屏互斥期间相机全死。
- **修法（契约扩展）**：新增 `externalCameraPass`（`Func<bool>`，附属 mod 反射赋值，CS0649 预期）——`UiPointerCapture()` 命中 UI 后先问它，返回 true = "这次全屏是拖放手势不是面板" → 相机放行（滚轮/中键照常），**点击手势仍被 `externalGuiBlock` 吞掉**（互斥语义不变）。未赋值（旧附属 mod）= 行为与 1.4.15 完全一致。
- 验证：编译 0 error；部署 DLL 202,752 B，sha256 `CEA6D4AD8E33802D808E1519C7805182634A9D5E997476E7EF167131CDBB9AE1`，与构建产物逐字节一致；`ER2_BattlefieldCommander_v1.4.16.zip`；反编译复核版本 1.4.16、`externalCameraPass` 字段与 `UiPointerCapture` 放行分支在位 ✓。

**（以下为历史版本条目，标题保持 1.4.15）**
**1.4.15（2026-09-24，修复 + 第三方 mod 兼容）**：
- **① 上帝视角打开设置后滚轮仍能控制视角（玩家报）** → 根因：`Tick()` 里 `HandleMove/HandleHeight/HandleDrag` **完全不受 `escMenuOpen` 约束**（只有空格暂停、快捷键、IMGUI 绘制做了让位），于是原生 ESC 设置菜单开着时 WASD 平移、滚轮升降、中键旋转照旧生效。**修法：新增 `UiPointerCapture()` 让位判定**——`menuCapture`（ESC 菜单）= 冻结**全部**相机输入；`uiCapture`（指针停在我们的面板/背包窗口/信息面板/外部 mod 面板上，**复用已有的 `IsMouseOverGui()`**）= 只冻结**鼠标驱动**的相机操作（滚轮、中键起手），键盘 WASD/Q-E 保持可用（否则光标恰好压在小队列表上会让人以为"相机坏了"）。中键已在旋转中的手势不被打断（指针捕获惯例），只是不能在 UI 上起手。
- **② 兼容 Advanced Combat Movement（Responsive Orders）的"永久停火"**：它对 `Squad.SetHoldFireOrder` 加了 Prefix，命中 `setEnabled=false && play_order_arnim=false`（正是我们"恢复开火"的调用签名）且**小队长 == 当前操控兵**时只记一次危险记忆就 `return false` → 下过移动令的小队可能**永远不再还击**。**修法：新增 `ResumeFire(Squad)`** = 「调用 → 回读校验（`sq.HoldFire`/`sq.holdFire`）→ 仍停火就直写原生 `holdFire` 字段」（字段写入不走方法，Harmony 前缀拦不住）。没装该 mod 时校验必然通过，行为零变化。两处调用点（`ClearMoveObservation()`、行军停火保险丝到期）已改。
- **③ 键位冲突结论（实测无误伤）**：其 F（标记/跟随）、H（停火）、F1（重开任务）走原生 `PlayerController.Update`，而上帝视角内该 Update 被我们的 `GodViewSkipUpdatePatch` 跳过 → RTS 内 F=分散（我们）、FPS 内 F=它的标记命令，**互不干扰**。另：我们的 `ToggleHoldFireSelected`（B 键）本就直写 `sq.holdFire` 字段，不受影响。它的 AI 防守驻留（Defensive Hold / Danger Memory / Artillery Evasion）会给小队下**它自己的**移动令，属设计层冲突 → 启动时检测到该 mod 会打一条共存提示日志，README/Nexus 已写明在它的设置页关掉对应开关。
- 验证：编译 0 error；`ER2_BattlefieldCommander_v1.4.15.zip` 已产出并部署（plugins DLL 18:29）；启动日志版本串已反编译复核为 1.4.15。

**（以下为历史版本条目，标题保持 1.4.14）**
**本工作区最大工程**（`GodViewController.cs` 150+ KB + `Formation.cs`/`GhostPreview.cs`/`InfoPanel.cs`）。F9 进上帝视角的 RTS 小队指挥层，操作仿 Gates of Hell。详见 `ER2_zcode_era.md` §1 与 `SquadCommand/README.txt`、`GPT_CONTEXT.md`、`ROADMAP.md`。
**1.2.0（2026-09-13）**：① 删除右键长按命令环 → 命令快捷键（cfg Hotkeys 可改键：Z/X/C 站蹲趴、V 停止、B 停火、N 就近掩体、M 集合、F 分散）；② 长按+拖动 = 阵型箭头（线中心=长按点、垂直箭头、长度=箭头长，上位替代旧载具朝向拖动，`dragFacing` cfg 已删）；③ 有原生掩体（`CoverManager.GetCovers` 按阵营+受敌方向查询）→ 步兵进掩体，无掩体沿阵型线垂直排开；拖动中白色半透明幽灵模型预掩体位（克隆士兵 GameObject 换半透明白材质，失败自动降级为标记）；载具到位（距槽≤7m）后按箭头方向直驱转向；④ 左下角新增选中单位信息面板 + 只读装备/背包（`InfoPanel.cs`）。
**1.2.1（2026-09-13，用户实测修复）**：① **`Lua_Soldier.findCover` 实测不下移动令**（分配掩体的单位原地不动）→ 掩体改走 `MoveUnits` + 到位（≤1.8m）`setPose(建议姿态)`（计入 poseLockedUnits，退出还原）；② 拖动视角抽搐 = 掩体查询/幽灵克隆的帧尖刺 → 查询半径钳 35m + 枚举 24 上限 + 幽灵池化（拖动中只建不毁、6 个/刷新预算、16 封顶、计划外隐藏）；③ 信息面板去底板、文字贴左缘（x=12）双描影（用户要求）；④ 行军/登车虚线改为只对当前选中单位显示；⑤ 死代码清理（wheelAnchorWorld/hasWheelAnchor）。
**1.2.2（2026-09-13，用户实测修复二轮）**：① **拖到屏幕外/指向天空时抽搐** → 箭头终点改「物理命中（限距 400m + 法线过滤）→ 与锚点等高平面求交」双通道，并**把箭头长度钳到 120m**（超远落点会把阵型线拉到几千米 → 标记被视锥裁切 = 用户看到的"标记被截断"，且端点大跳变）；② **标记不跟手** → `Formation.DragTick()` 移入 Tick 持久段且在 `SceneMarkersFrame()` **之前**（原在 HandleClick 后，标记慢一帧），布局改为每帧重算（纯数学零分配），只有掩体查询+幽灵刷新按 0.35s 节流；③ **SceneMarkers 池化重构** → 池条目由裸 GameObject 改为 `Mark` 结构**缓存 LineRenderer/Material/TextMesh 引用**（原每帧 `GetComponent` + `Arrow` 里 `GetComponentsInChildren` 每帧分配数组 → GC 尖刺）；材质统一 `sharedMaterial`（`LineRenderer.material` 每次访问都克隆）；④ **目标点标记与行进连线同源**：`moveViz` 统一判据 + `ClearMoveObservation()` 一并清 `hasCmdTarget` + 双击防守补 `RecordCmdTarget` + 登车期间标记跟随载具；⑤ 幽灵调暗（0.45 → 0.20 淡灰蓝，无光照 shader 下 0.45 白几乎自发光）。

**1.2.3（2026-09-13，用户实测修复三轮）**：① **箭头"延伸反了"导致视角翻转** → 终点增加**相机前向半空间约束**：终点必须落在相机水平前向一侧，拖向自己时退化为零长（不再掉头 180°）；② **右键交互环整体删除** → 右键友军/中立载具=**直接上车/进入**（火力点同），车内友军=补员上车；**下车/修理移到左下角信息面板**（`InfoPanel.DrawVehicleActions`）；③ **不可移动的火力点/火炮**（无 `AIVehicle`）→ 不进阵型线槽，改为**原地按箭头方向转向**（`VehicleFacing.IsEligible` 放宽：无 AIVehicle 时改判"车上有存活乘员"）；④ **右键建筑/房屋** → 选中步兵进入并找掩体防守（`Formation.AssaultCovers` 复用掩体分配管线；`IsBuildingHit` 识别 `DestructableBuilding`/`BuildingFurnitureSpawner`/`InteragibleDoor`）；⑤ `GhostPreview.Ghostify` 开放给附属 mod 复用（放置预览）。

**1.2.4（2026-09-13，用户实测修复四轮）**：① **箭头恢复 360° 可拖**——移除 1.2.3 的"相机前向半空间"约束与 1.2.2 的"等高平面兜底"（后者在近水平视角下把落点解到相机背后 = 1.2.3 那轮"翻转"的真根因）；现在射线未命中即**保持上一帧终点**。② **轮式车不原地旋转**——`VehicleTank`（履带）到位后原地转向；轮式改为沿期望朝向**续驶 14m 摆正**（`Formation.TickPendingFacings`，`WheeledRollDist`），解决"目标点太近就原地打转"。③ **固定火力点/火炮改为"从单位伸出箭头"**——选中只含不可移动火力点时，长按右键**不需要点空地**，锚点自动取单位自身（`Formation.TryBeginDrag` 的 `facingOnlyMode` + `SelectionIsFacingOnly`）。④ **左键可选中非单位可交互物**（空载具/`ItemObject.CanInteract` 物品）→ `ClickActOrCancel` 新增 `SelectProp` 分支与 `propVehicle/propItem` 选择槽（与单位选择互斥），面板显示其信息、右键对其下上车/进入。⑤ **背包改为按钮 + MC 风格格子面板**（`InfoPanel` 重写）——原只读列表只覆盖穿戴项（"只能看见 Bar"），改从 `InventoryManager.inventory.items` 取全部条目并按 item_id 归并计数；6×4 格子 + `ItemObject.icon` 图标 + 数量角标 + 悬停名称 + 翻页。⑥ **建筑目标点落屋顶** → `GroundPointUnder` 沿 XZ 向下重投影取地面。⑦ `GhostPreview.Ghostify` 物理剥离补全（见 UniGen 1.0.5①）。
**1.2.5（2026-09-13，用户实测修复五轮）**：① **火力点/火炮又不能转向了** → 1.2.4 的"非单位可交互物"改动把**非敌对载具**整体路由到可交互物槽 → `selVehicleRefs` 为空 → `SelectionIsFacingOnly` 不成立、阵型/转向链路全断（回归）。现在**非敌对载具一律进载具选择**（含空载具、无车组/阵营判定不出的火力点），`SelectProp` 只服务 `ItemObject`。另修长按仲裁：`rightDownOnUnit` 时默认不开阵型，但**选中只含固定火力点时允许按在单位上起手**（`Formation.SelectionIsFacingOnlyNow()`）——用户就是从炮位上长按拉箭头的。② **背包改回分类列表**（用户要求）：按 item_id 关键字分 武器/弹药/爆炸物/医疗/装备工具/其他 六类，每类带标题+下划线，未识别项归入其他（不丢条目），列表 10 行分页。③ **左键点生成列表条目直接放置** → 面板点选那一下的"松开"落进了放置状态机（被当成放置点击）。新增 `Placer.ignoreUntilRelease`：从面板进入放置模式后吞掉该次按压直到真正松开。④ **自定义光标落地**（`MouseCursor.cs`，用户定案用 `Cursor.SetCursor(Texture2D, hotspot, CursorMode.Auto)`）：32×32 程序化贴图（箭头/友军绿箭头/敌军准星/载具车门/建筑门框/火力点转向弧/拖动十字/可交互手形），每帧在 Tick 重申防原生重设，退出 RTS 恢复系统光标；cfg `customCursor` 默认开；放置模式经 `UniversalGenProbe` 反射探测显示十字。
**1.2.6（2026-09-13，用户实测修复六轮）**：① **右键长按拖动"视角乱飞"** → 1.2.4 去掉平面兜底后，端点改为"射线命中则取命中点、未命中保持上一帧"，而**命中/未命中会逐帧交替**（尤其近水平视角与地形边缘）→ 端点大跳 = 乱飞。现改为**纯屏幕空间映射**：锚点投影到屏幕 → 方向取"锚点屏幕位置→鼠标屏幕位置"的二维向量 → 用相机右/前基向量映射回世界（天然 360°、逐帧连续、不依赖任何射线），长度按相机高度把像素换算成米并钳制 `MaxArrowLen`。② **背包列表重叠/丑** → 原实现把背包画在**信息面板同一位置**（用户截图里的文字叠文字）。现在背包**堆叠在信息面板上方**（`PackPanelRect` 以 `infoTop` 为基准向上排），并用不透明底板 + 描边 + 分类色标题 + 数量右对齐 + 隔行微亮底重排。③ **光标形状**（用户："长得什么鬼"，载具/建筑的也不好看，且指定默认与建筑都用十字）→ 全部形状统一为**十字准星**（中心留空 + 四向短线 + 1px 黑描边），仅以颜色与少量点缀区分：白=默认/拖动、绿=友军、红=敌军(中心点)、青=载具(小方框)、黄=建筑、橙=火力点(下弧)、浅蓝=可交互(中心点)。旧的箭头/门框/手形绘制全部删除。④ **UniGen 预览幽灵被打死 / 转朝向时跟着动** → ①幽灵是**真实 Soldier 克隆体**，敌人会把它当目标：新增 `GhostPreview.DetachAndPacify`（脱队 `joinedSquad=null` + `allowBeingTargeted(false)` + 关 AI），宿主 `Ghostify` 与 UniGen 侧各兜一次；②旋转朝向时 `UpdatePreview` 被冻结（`if (rotating) return`）且 `MovePreviewTo` 在旋转期间不调用 —— 只改 yaw 不改位置。
**1.2.7（2026-09-13，用户实测修复七轮）**：① **视角乱飞（第三次）** → 两处根因：`AssertFreeCursor`/`FrameEndGuard` **在右键按住时直接 return**（拖动期间无人维持 `lockState=None`，游戏把光标锁回中央 → 鼠标位置突变 → 端点暴走），且屏幕映射**每帧读相机**与相机互相激发。现在：光标维持不再跳过右键；起手时**冻结相机基向量与像素比例**（`Formation.CaptureDragBasis`），拖动全程不读相机。② **光标改 IMGUI 自绘**（用户定案）：废弃 `Cursor.SetCursor`（与游戏光标管理打架、贴图显示异常），改 `Cursor.visible=false` + OnGUI 末段画十字；形状靠颜色区分（白默认/绿友军/红敌军/青载具/黄建筑/橙火力点/浅蓝可交互），默认与建筑都是十字。③ **火炮/火力点仍不能转向** → 旧判定用"有无 `AIVehicle`"，而炮位**也带 AIVehicle** → 被当成可驾驶。改用 `Formation.IsMobileVehicle`（`VehicleTank`/`VehicleWithWheels`/`VehiclePlane` 才算可移动），其余一律进"只转向"；`VehicleFacing.IsEligible` 同步：可移动载具要求驾驶员，火力点只要有人操作；并支持**直接按在炮位上**长按起手。④ **放置：地形稍不平就放不下** → 法线阈值 0.4 → 0.15。⑤ **相机在预览上方持续上升** → `GroundHeightAt` 改 `RaycastAll` 并**跳过幽灵**（幽灵把"地面"抬高 → 相机被反复往上推）；幽灵对象加 `ER2Ghost_`/`UniGenPreview_` 前缀供识别。⑥ **进建筑后看不见、不知是否完成防御** → 新增**驻守记录**（`Formation.coverHolds`）+ **穿墙标记**（`SceneMarkers.LineMatNoDepth`，不做深度测试，屋顶挡不住）+ HUD **"驻守 → x/y 已进入掩体"** 状态行（绿色=全部到位）。⑦ 幽灵伤害免疫补丁（`Creature.Damage` Prefix，见 UniGen 1.0.8①）。
**1.2.8（2026-09-13，用户实测修复八轮）**：① **残留标记（消除不掉的圈/箭头/虚线）** → 根因：持久段整块共用一个 try，**任一步抛异常就跳过 `SceneMarkersFrame()`**，而 `SceneMarkers.EndFrame()`（隐藏本轮未刷新标记）在它内部 → 已画出的阵型箭头、路线虚线、掩体圈**永久卡在画面上**。现在每步独立 `SafeStep`，且 `SceneMarkersFrame` 的 `finally` 里**无条件调用 `EndFrame()`**（清理必跑）。② **掩体圈只对选中单位显示**（`Formation.DrawCoverHoldMarkers` 加 `IsSelectedUnit` 过滤），并加两道自动清除：超时 40s（`CoverHoldSeconds`）、单位离开掩体点 4m（`CoverHoldLeashDist`）视为放弃驻守。③ **视角变化时的异常（图一）** → 与 ① 同源：持久段异常导致标记清理中断、屏幕残留旧标记，观感即"视角变化时画面出错"；现在异常被逐步隔离并单独记日志（`[SquadCmd] <步骤> 错误: ...`），便于下次精确定位。
**1.2.9（2026-09-13，用户实测修复九轮）**：① **所有 3D 随动标记消失** → 这是我 1.2.8 引入的 bug：`SceneMarkersFrame()` **内部自己会调 `SceneMarkers.EndFrame()`**（并 `used.Clear()`），而 1.2.8 在它的 `finally` 里又无条件调了一次——第二次 `used` 已空 → 本帧刚画好的**全部标记被立刻隐藏**。现在只在绘制中途失败（没走到它自己的 EndFrame）时补清理（`markersDrawn` 标志）。**教训：给"帧末清理"类函数加兜底前，必须先确认它是否已被内部调用——重复调用会反向清空。** ② **长按右键拖动改变视角** → god view 把玩家设为"无单位"（`SetPlayer(null)`），游戏在该状态下会启用它自己的自由/窥视相机，右键拖动即旋转视角，与我们的 `camPos/camRot` 互相打架（用户多轮反馈的"视角乱飞"）。**修法：帧末强制复位相机**——`FrameEndGuard` 在 `WaitForEndOfFrame`（所有 LateUpdate/协程之后、渲染之前）把相机压回我们的姿态，不依赖"猜哪个原生组件在写相机"；同时加 `[CamIntrusion]` 限频诊断（帧末发现相机被外部改动 >1m 就记 delta/位置/按键状态），供下一轮实证定位。
**1.2.10（2026-09-13，用户实测修复十轮：相机真凶 + 性能）**：① **"长按右键拖动视角被带动"的真凶 = `TerrainCamera`**——游戏内置的**自由/地形相机**（字段：`orbit`/`mouseRotate`/`distanceToTarget`/`mainSpeed`/`GetBaseInput`）。god view 用 `SetPlayer(null)` 把玩家设为"无单位"后，游戏启用了它，按住鼠标即旋转视角，与我们的 `camPos/camRot` 逐帧互斗。此前只 patch 了 `CameraDirector`/`SimpleCameraController`，**漏了这一个**（这正是"之前修好了、现在又出现"的原因：当时那轮改动把 `FrameEndGuard` 的相机兜底拿掉了，就再没人压住它）。现在 `Plugin.cs` 新增 `GodViewSkipTerrainCameraPatch`（`TerrainCamera.Update` Prefix → `!Active`），从源头跳过；并移除 1.2.9 那个"帧末强制复位相机"兜底（与原生互斗，且掩盖真因）。**教训：`SetPlayer(null)` 之后要清点**所有**会被游戏启用的相机控制器，逐个 patch；"帧末兜底压相机"只能掩盖症状。**
② **卡顿排查与优化**（本轮共 4 处热点）：
  - `GroundHeightAt` 原用 `Physics.RaycastAll`（**每帧分配数组** + 逐命中走父链判幽灵）→ 改单次 `Physics.Raycast`，仅命中幽灵时补一发（最多 2 次、零分配）。
  - 维护段**每帧**调 `SelectedVehicleOccupants()`（内部 `GetComponentsInChildren` 分配数组）+ 3 个 `List` 分配 → 改 0.25s 节流（`ctrlSyncNext` + 复用缓冲 `ctrlSyncBuf`），无载具选择时直接跳过乘员展开。
  - `InfoPanel` 的 `FocusCount/FocusAt` 被 OnGUI **每个事件**（多次 Layout/Repaint）各调一次，每次 `GetSelectedInfantry()`+`GetVehicleRefsSnapshot()` → 每帧十几次 List 分配 + 逐单位 `GetComponentInParent`。改为 0.15s 缓存的 `focusInf/focusVeh`（`RefreshFocusCache`）。
  - 小队列表面板 `SquadSymbols` 每行每事件重算，内部逐成员 `IsInfantry()`（两次 interop）→ 10 行×8 人×多次事件 ≈ 数百次 interop/帧。加 2s 缓存 `SquadSymbolsCached`。
  - `DrawHud` 里每帧冗余调用 `PruneSelection()`（Tick 已在 Update 阶段跑过）→ 去掉。
  - `GhostPreview.UnregisterGhost` 只删 `Creature` 指针，而 `DetachAndPacify` 同时登记了 `Soldier`/`Vehicle` → 那些指针**永不移除**（幽灵表无界增长）→ 补齐按类型移除，`ClearAll` 也走注销。
③ **删除"驻守圆环"**（用户要求）：`Formation` 的 `CoverHold` 类/`coverHolds` 列表/`AddCoverHold`/`MarkCoverArrived`/`DrawCoverHoldMarkers`/两个常量，以及 HUD 的"驻守 → x/y"行全部移除；`SceneMarkersFrame` 里对应的穿墙标记调用一并删除（`SceneMarkers.LineMatNoDepth` 保留备用）。

**1.2.11（2026-09-13）**：**相机最终接管 + 幽灵剥离相机组件**。① 1.2.9 用 `WaitForEndOfFrame` 压相机没能解决 → 说明原生写入发生在那之后；改用 **`Camera.onPreRender`**（每个相机**渲染前最后一刻**，晚于所有 `LateUpdate` 与 `WaitForEndOfFrame` 协程），在此把主相机压回 `camPos/camRot` = 最终画面一定由我们决定（只接管主相机）。② **幽灵剥离相机类组件**：用户反馈"只有选中单位并拖动后才乱飞"，而幽灵正是拖动时才创建、且是**克隆整具士兵**——若士兵身上带 `Camera` 会被一起克隆，残留相机可能劫持渲染视角。现在幽灵身上的 `Camera`/`AudioListener`/`Light` **直接销毁**（不是禁用）；`AudioListener` 在未引用的 AudioModule 里，按 `Behaviour` 基类 + 类型名反射处理。
**1.2.12（2026-09-13）**：① **自绘光标"不跟手"** → 根因是**绘制时机**：1.2.7 用 IMGUI `OnGUI` 画光标，而 `OnGUI` 在帧**早期**执行，相对真实鼠标位置**慢一帧**。改为在 **`Camera.onPreRender`**（渲染前最后一刻）用 `GL.LoadPixelMatrix` + `Graphics.DrawTexture` 绘制，位置与当帧鼠标完全一致；语义探测（射线判指向对象）仍 0.1s 节流——形状慢一拍无感，但**位置**必须每帧最新（绘制与探测解耦）。钩子安装失败自动回退 IMGUI。② **诊断修正**：1.2.11 的 `[FormDiag]` 用 `SquadCmdLogic.Log`（受 `debugLog` 门控，默认关闭）→ 一行都没记下来；改为**仅异常时**（光标被锁/被显示、相机偏离期望）用 `LogAlways` 输出，平时静默不刷屏；`[CamAuthority]` 同样只记偏离。
**1.2.13（2026-09-13）**：① **光标完全看不见** → 1.2.12 改用 `Camera.onPreRender` + `Graphics.DrawTexture` 绘制，而 `onPreRender` 在相机渲染**之前**执行，画的内容随即被相机清屏覆盖（所以什么都看不到）。**改回 IMGUI 绘制**（`OnGUI` Repaint 阶段覆盖在最终画面之上，稳定可见）。**教训：`onPreRender` 里画的东西会被该相机自己的清屏吃掉——要覆盖画面必须用 IMGUI 或在所有相机之后。** ② **样式重做**（用户反馈"十字不好看"）：默认改为**箭头光标**（RTS/Gates of Hell 惯用形态：经典指针多边形 + 1px 黑描边 + 状态色填充，热点在尖端）；新增 cfg `cursorStyle`（`Arrow` 默认 / `Cross`），选 `Cross` 时用重画的**细线十字**（1px 主色 + 描边 + 中心点，不再是又粗又白的方块感）。拖动阵型与放置模式**强制十字**（精度场景）。状态色：白默认/绿友军/红敌军/青载具(带小方框)/黄建筑/橙火力点(带转向弧)/浅蓝可交互。`InvalidateCache()` 让样式切换即时生效。
**1.2.14（2026-09-13）**：**光标样式改为"空心半透明圆环"**（用户定案，箭头样式不理想）。环内不填充、主色半透明（alpha 0.55）+ 外侧一圈更淡的暗色描边（浅色地形上也能分辨），环心即热点。cfg `cursorStyle` 三选：`Circle`（**默认**，空心半透明圆）/ `Arrow`（箭头）/ `Cross`（细线十字）；`InvalidateCache()` 让切换即时生效。颜色仍随指向对象变化（白默认/绿友军/红敌军/青载具/黄建筑/橙火力点/浅蓝可交互）。
**1.2.15（2026-09-13，发布预览版）**：**幽灵残留收口 + 组件遍历合并 + 英文版发布**。
**1.2.16（2026-09-18，用户实测修复：幽灵原色/碰撞 + 光标遮挡 + 选多人拖动异常）**：① **幽灵变回原色** → 1.2.15 把材质换进 `Component[]` 单次遍历，实测"`c is Renderer` 未命中"（IL2CPP 下泛型遍历的类型判别不可靠）→ **材质改回已验证的强类型 `GetComponentsInChildren<Renderer>()`**，并加多 shader 兜底（Sprites/Default → Legacy Transparent → Unlit/Transparent → Particles → URP Unlit → Internal-Colored）。② **诊断补齐**：材质创建失败此前走 `debugLog` 门控日志 → **静默失败**（表现为幽灵原色/附属预览不显示却查不到原因）；现在幽灵化完成时无条件 `LogAlways` 一次（shader 名 / 换材质渲染器数 / 停用碰撞体数）。③ **光标被菜单遮挡** → `MouseCursor.Draw` 里把 `GUI.depth` 压到 -30000（IMGUI 中 depth 越小越后画），光标压到其它 IMGUI 之上。④ **选中人多时右键拖动异常** → 克隆整具士兵（骨骼+蒙皮）很贵，人多+快速拖动会叠成帧尖刺：幽灵总量 16→10、每轮新建 6→3、新增"**手稳**"判据（鼠标本帧位移 <20px 且上一帧不慢才允许新建，否则只 `MoveOnly` 平移已有幽灵）。⑤ 拖动起止各记一次 `[DragCam]` 相机状态（无条件、每次拖动两行），用于判定"镜头乱飞"到底是相机被移动还是标记在跳。
**1.2.17（2026-09-18，用户实测修复：阵型失效 + 幽灵回归原色/带碰撞）**：① **阵型功能完全失效（1.2.15 引入）** → 1.2.15 加的看门狗（`DragTick` 检测"右键已松开"即取消）**在松开的那一帧抢先于 `HandleClick` 执行**（`DragTick` 在 Tick 持久段、`HandleClick` 在其后）——同帧把 `dragging` 置 false，随后 `IssueFromDrag` 直接 return → **阵型永远不下发**。修法：看门狗加 0.15s 宽限（`rmbUpSince`），只有"松开后迟迟没被收尾"才取消。**教训：给每帧轮询的函数加"状态检测"兜底时，必须先厘清同帧内其它消费方的执行顺序——兜底不能比正常路径先跑。** ② **幽灵变原色/带碰撞/带物理（1.2.15 引入，1.2.16 只修了一半）** → 1.2.16 只把**材质**改回强类型遍历，`Rigidbody`/`Joint`/`Collider` 仍留在 `Component[]`+`is` 判别里（同样不可靠）→ 幽灵仍带碰撞与物理（僵体乱动 = "视角乱飞"的实因，用户在 1.2.15 观察到的"选多人拖动时"正是幽灵多的时候）。现在 `ProcessComponents`/`DetachAndPacify`/`UnregisterGhost` **全部改回强类型 `GetComponentsInChildren<T>()`**。③ **幽灵数量对不上绿圈** → 1.2.16 的"手稳"判据（鼠标本帧位移 <20px 才建幽灵）在拖动中恒 false → 幽灵建不出来；且总量/每轮配额被砍到 10/3。已撤判据、恢复 16/6（幽灵按单位指针池化，本身只建一次；真正的惰性化靠强类型遍历实现）。④ 删除无调用方的 `MoveOnly`。
**1.2.18（2026-09-19，用户实测修复：阵型失效真因 + 绿圈删除 + 光标置顶）**：① **阵型仍然失效（真因）** → 1.2.17 只修了看门狗时序，**漏了 `ResetRightGesture`**：1.2.15 在它里面加了"formationDragActive 时 CancelDrag"，而**松手收尾路径正是先 `ResetRightGesture()` 再 `IssueFromDrag()`** → 计划被清空、`dragging=false` → `IssueFromDrag` 直接 return。修法：`ResetRightGesture(bool cancelFormationDrag = true)`，**松手路径传 false**（由 Issue/Cancel 自己收尾）；其余中断路径（`ResetInputState` 等）仍取消。② **删除拖动中的绿色掩体圈**（`FMC` 环，用户要求）；删除时把共用循环变量 `int i` 的声明一并带走 → 补回。③ **自绘光标被通用生成面板遮挡** → `DrawPatch` 加 `[HarmonyPriority(800)]`（postfix 高优先级=后执行），让我们的 HUD（含光标）画在其它 mod 的 OnGUI 之后。
**1.2.19（2026-09-19，用户实测修复：光标遮挡 + 预览碰撞/大班型直接生成）**：① **自绘光标被通用生成面板遮挡**（1.2.18 的 `HarmonyPriority(800)` 无效）→ 根因：我们的 HUD 与 UniGen 面板都是 `PlayerController.OnGUI` 的 Postfix，**在同一个 IMGUI 回调上下文里执行**，`GUI.depth`/patch 顺序都管不到"同一回调内的先后"。修法：**光标改回 `Cursor.SetCursor`（系统级光标）**——由 OS 合成，永远在一切内容之上且零延迟；圆环贴图是纯距离函数生成（可靠，1.2.5 时代的白块是箭头多边形算错）。形状变化或每 2s 重申一次 SetCursor；`CursorVisiblePatch` 改为强制 `visible=true`（SetCursor 贴图只在 visible 时显示）；退出 RTS `SetCursor(null)` 还原。② **幽灵与已有单位碰撞** → 见 UniGen 1.0.14。
① **幽灵可能残留在场上**（用户反馈"即便取消右键长按"）→ 审计出多条泄漏路径：`ResetRightGesture` 只清 `formationDragActive`、**没清 `Formation.dragging` 与幽灵**；松手时若鼠标在 UI 上（`guiNow`）直接跳过、既不执行也不清理；退出 RTS 的路径同样只置标志。现在新增 **`Formation.CancelDrag(reason)`**（唯一的中断入口：置标志 + 清计划 + `GhostPreview.ClearAll()`），并在**所有**结束路径调用：手势复位、UI 上松手、退出 RTS；另加**看门狗**（拖动中若"已退出 RTS"或"右键已松开"→ 立即取消），兜住所有异常路径。
② **组件遍历合并（性能）**：`Ghostify`/`MakeGhost`/`DetachAndPacify`/`UnregisterGhost` 原来各做 5~8 次 `GetComponentsInChildren`，现合并为**单次遍历**（`ProcessComponents` 一次处理 Collider/Rigidbody/Joint/Camera/Light/Renderer/Behaviour）；`Apply` 的"计划外幽灵"比对由 O(n×m) 改 HashSet；幽灵指针表加 512 上限防无界增长。
③ **英文版发布**：`-Mod SquadCommand`（EN 默认包）构建部署并打包 `ER2_BattlefieldCommander_v1.2.15.zip`；`ER2_UniversalGeneration_v1.0.10.zip` 同步。验收：双端 sha256 一致 ✓、反编译确认 `Ui.Tr` 走英文字典（EN 构建）✓、zip 内 DLL+README+Nexus_description 齐全 ✓。
**1.3.0（2026-09-19，大功能：格子背包系统，新增 `BackpackPanel.cs`）**：
① **格子背包多窗口（MC 风格）**——容器统一走 `InventoryManager`（士兵背包/尸体背包/载具货舱一套代码），可同时开多个窗口：每页 6×4 格 + 翻页 + 可拖动标题栏 + 负重条（`GetWeightAndMaxWeight`）+ 右上关闭；物品 = 游戏原生图标（`ItemObject.icon` 同步实测路径为主，null 时 `LoadIconAsync` 异步补，绘制 `GUI.DrawTextureWithTexCoords` 防 atlas 整图）+ 数量角标（弹匣=弹药数、堆叠物=stackCount）+ 悬停提示；穿戴/手持（`vi.IsWearedItem()`）灰框锁定不可拖。
② **鼠标拖拽交换**——拿起→放到其他格：空格=移动/同窗重排（`List.RemoveAt+Insert`）、有物=交换、同 id 可堆叠=合并（`Get/SetStackCount`，上限 `GetMaxAmmoStackCount`/prefab `maxStack`），全部带负重校验（`GetWeightAndMaxWeight` 自算，取不到退 `HasSpaceFor`）；**拖到所有窗外松手=扔地上**（`DropItem`，未生效兜底 `ExtractAndInstantiate`+手动移除）；右键/ESC 取消拖拽。
③ **搬移通道（关键决策）**——不走 `TakeIntoInventory`/`AddVirtualItem`（陷阱 16：归一化成基类会废弹匣/弹药子类），直接 `items.RemoveAt/Add`（ThrowableWheel 已验证注入法）；debugLog 记录搬移前后子类类型名（`GetIl2CppType().Name`）验证无降级。
④ **锚点规则（用户定案）**——第一个打开的背包为锚点：其余背包距锚点超过 `packRange`（cfg `Control/packRange`，默认 25m，5–100）拒绝打开；打开后每 0.5s（unscaled）复检，超距自动关闭；锚点窗口永不受范围规则约束，锚点关闭后由最旧余窗接任。
⑤ **尸体可左键选中**——`ClickActOrCancel` 加尸体分支（阵亡 Soldier，不分阵营）→ `SelectedCorpseUnit` 选择槽（与单位/物品选择互斥），信息面板尸体行 + [背包] 开窗（此前尸体点不中 → 尸体背包开不了）。
⑥ **入口与清理**——信息面板 [背包]/[货舱] 按钮 + 新热键 G（`Hotkeys/keyPack`）；分类列表背包面板（1.2.5）删除；`BackpackPanel.WantsMouse()` 挂进 `IsMouseOverGui`（拖拽中全屏吞手势防误框选）；`CloseAll()` 幂等单入口挂 Exit/TakeControlSelected/Enter；拖拽态收口 `ResetDrag`（ESC 菜单打开即收）。窗口内格子快照与 `items` 列表做一致性守卫（AI 0.25s 间隙动过背包 → 提示重试）。
验证：EN/CN 双包重建（zip 内 README/Nexus 版本号 1.3.0 ✓、DLL 与部署 sha256 一致 ✓）。**1.3.0 首轮实测（用户反馈 5 项）→ 1.3.1 修订，见下**。
**1.3.1（2026-09-19，用户实测修订五连）**：
① **图标不显示** → 取图链改多级兜底：`GetItemPrefab()`（null 再试 `ItemsDatabase.GetItemObject`）→ `ItemsDatabase.cachedLoadedSprites` 按 id 直取/扫 name → `LoadAndCacheSprite(id/id_icon/icon_id, "er2gui")` → `LoadIconAsync` 异步；**全链失败无条件 `LogAlways` 一次/id**（prefab 有无 + cachedSprites 数 + 异步挂起，不被 debugLog 门控），失败后 2s 节流重试同步链。**待日志定真因**。
② **无法选中尸体** → 三层修复：`IsAlive` 访问加 try/catch（尸体上 interop 属性异常会吞掉整个点击分支）；点击兜底 `NearestDeadSoldier(hit.point, 1.2m)`（布娃娃层级脱离/点到尸体上的枪时按 `Creature.allCreatures` 就近找阵亡士兵，禁 FindObjectsOfType）；未命中可选物时记 debugLog 命中诊断。
③ **武器不能丢弃** → 穿戴/手持物品**允许拿起**，但只能**丢到地上**：首选士兵原生卸下路径 `DropItemNow(wearedItems 索引)`（正确处理手持模型），兜底 `DropItem` → `ExtractAndInstantiate`+手动移除；放回格子提示「装备中的物品只能丢弃到地上」。
④ **物品太杂乱** → 格子改**同 id 合并成一格**（×N=总数：弹匣 Σ弹药数/堆叠 ΣstackCount，格内 N 件 VirtualItem），按 武器→弹药→爆炸物→医疗→装备→其他 分类排序；**拖动=整组移动/交换**（放下时按 id 活取成员，AI 间隙消耗过也一致）；删掉逐格堆叠合并逻辑（合并=同 id 移动）。
⑤ **联动半径太大** → `packRange` 默认 25→**3m**（范围 1–100），提示改「距离太远（需距锚点 {0}m 内）」。
验证：EN/CN 双包重建 1.3.1（DLL 与部署 sha256 一致 ✓）。**1.3.1 复测：武器丢弃 ✓（日志 DropItemNow 全成功）、图标仍不显示、尸体仍选不中 → 1.3.2，见下**。
**1.3.2（2026-09-19，图标根因定位 + 尸体交互改右键）**：
① **图标不显示（根因定位）** → 回读 LogOutput：1.3.1 的「图标未命中」诊断 **0 次触发** 且无任何异常 → **Sprite 实际取到了，是绘制调用不生效**：`GUI.DrawTextureWithTexCoords` 在 IL2CPP interop 下画不出像素（1.2.4 用整图 `GUI.DrawTexture` 是可见的）。修法：**整图优先**——`textureRect` 覆盖全贴图（独立贴图图标，绝大多数情况）直接 `GUI.DrawTexture(fit, tex, ScaleMode.ScaleToFit)`，仅图集子区域才走 TexCoords；每个 id 首次绘制/失败路径各记一次无条件诊断（tex 尺寸/name/tr）。
② **尸体交互改右键（用户定案）** → 尸体**不可左键选中**（`SelectedCorpseUnit` 选择槽/信息面板尸体行/左键兜底全删，左键点尸体=空白清选）；**选中单位后右键尸体=开背包窗口**（直接组件查找 + `NearestDeadSoldier` 1.2m 就近兜底），无选中提示「先框选/选中单位」；`RightPressHitsUnit` 把尸体/物品也算"按在对象上"→ 长按不起阵型。
③ **右键地上物品=拾取** → `IssueDirectCommand` 加物品分支：`PickupItemBySelected` 最近选中士兵（无步兵回退最近选中载具货舱）走原生 `AddItemToInventoryAndDestroyInstance`（实体消失入包）；`IssueDirectCommand` 重构为先射线后早退（无选中时对尸体/物品也能给提示）。
验证：EN/CN 双包 1.3.2（sha256 一致 ✓）。**1.3.2 复测：右键尸体开背包 ✓；图标仍不显示（诊断行抓到真因）、拾取被尸体兜底拦截、隔空开尸包 → 1.3.3，见下**。
**1.3.3（2026-09-19，图标 GC 真因修复 + 拾取拦截修复 + 派兵翻尸体）**：
① **图标不显示（真因实锤）** → 1.3.2 诊断行输出：图标全是 **256×256 图集子区域**（`icon_carbine_m1` 等，走 TexCoords 路径），随后 **`绘制异常 Object was garbage collected in IL2CPP domain`**——图标 Sprite/Texture 只存在 C# Dictionary 的包装类里，**IL2CPP Boehm GC 把对象回收了**（1.3.0/1.3.1 的空 catch 吞掉同款异常 = 此前"不显示"的完整因果链）。修法：**IL2CPP 侧强引用保活**——`Il2CppSystem.Collections.Generic.List<Sprite/Texture>` 静态登记（CacheIcon 统一入口），跨分支生效（模板 icon/全局缓存/bundle 直取/LoadIconAsync 回调）。
② **右键物品拾取不触发** → 日志「拾取」零条 = 分支没进：掉落物就在尸体旁，**尸体的 NearestDeadSoldier(1.2m) 兜底抢先拦截**（开了尸包没拾取）。修法：**物品分支提到尸体判定之前**。
③ **隔空开尸包（用户定案不许）** → 右键尸体：最近选中士兵已在 packRange 内 → 直接开窗；**否则原生 `new Lua_Soldier(best).moveTo(corpsePos)` 派他走过去**，`BackpackPanel.LootTick()`（Tick 持久段 0.25s 节流）检测到达（≤packRange）自动开窗；90s 超时/士兵死亡取消并提示。
④ **尸体悬停无光标** → `MouseCursor.Probe` 补：阵亡 Soldier → Shape.Interactable（浅蓝，与物品一致）。
验证：EN/CN 双包 1.3.3（sha256 一致 ✓）。**1.3.3 复测：IL2CPP 侧保活 List 仍被 GC（诊断行依旧满屏 "Object was garbage collected"）→ 1.3.4 终案**。
**1.3.4（2026-09-19，图标终案：光栅化自建贴图 + 右键友军会合开双背包）**：
① **图标（终案）** → 1.3.3 日志实锤 `Il2CppSystem.List` 保活也拦不住 Boehm GC → 放弃保活外国对象：图标解析成功后**立刻光栅化**——`Graphics.Blit(图集) → RenderTexture → ReadPixels(子区域)` 拷进**自建 Texture2D**（hideFlags 61 陷阱 12 + C# 侧 keep-alive），之后 `GUI.DrawTexture` 只画自建贴图（与 whiteTexture 同一条全程已验证路径，绘制零外国对象）；光栅化只在 IMGUI Layout 阶段做（不打断绘制状态），每 id 首次记无条件诊断（`光栅化 OK WxH` / 异常）。
② **右键徒步友军=会合开双背包（用户定案）** → 原"视为地面移动"移除：`RequestLoot(sol, pos, openBoth:true)`——最近选中士兵走到联动半径内到达后**同时开目标与 walker 自己的背包**（`ForceOpenSoldier` 强开，区别于按钮的 toggle，防同指针二次调用把窗关掉；walker==目标时只开一个）；车内友军仍是补员上车，右键地面仍是移动。
③ 翻尸/会合统一为一个状态机（`lootBoth`/`lootSkipWalker`），90s 超时提示改「没有走到目标旁，已取消」。
验证：EN/CN 双包 1.3.4（sha256 一致 ✓）。**1.3.4 复测：图标 ✓（光栅化方案实锚生效，截图可见全部物品图标）、会合流程 ✓；新需求：物品原生交互 → 1.4.0**。
**1.4.0（2026-09-19，原生交互菜单）**：
① **右键格子 = 游戏自己的交互菜单**——`vi.GetInventoryInteractions(所属背包, 所属背包)` 取该物品全部原生交互（穿戴/吃/医疗/卸下…，游戏本地化文案），IMGUI 弹出小菜单，点条目 = **`Interaction.Call()` 原生执行**（能走原生管线就走原生，陷阱 17）。点菜单外/再次拾取/ESC 菜单/窗口关闭 → 收菜单（CloseMenu 单入口，挂 CloseAll 与 ESC 钩子）；菜单 Rect 纳入 WantsMouse 防手势穿透。执行后 Flash 文案 + LogAlways（`[Backpack] 交互执行/交互失败`，失败不门控）+ 刷新窗口。
② 已知限制（待实测）：interactor 传的是**所属士兵自己的背包**（兵用自己的东西）；长按类交互（IsHoldAction）在 RTS 下可能只触发一次；无交互的物品提示「该物品没有可用交互」。
验证：EN/CN 双包 1.4.0（sha256 一致 ✓）。**1.4.0 复测 8 项反馈：日志零条「交互执行」→ 菜单点击被底下格子吃掉（穿透），穿戴/刺刀"不生效"其实是菜单根本点不到 → 1.4.1**。
**1.4.1（2026-09-19，菜单模态化（总根因）+ 8 项反馈修复）**：
① **交互菜单模态化（总根因）** → 菜单画在窗口之后但点击裁决在格子之后 → 菜单底下格子先吃掉点击（误触拾取拖拽=「操作丢失」、菜单项永远点不到=「穿不上/刺刀装不上」）。修法：Draw() **进格子前先裁决**——菜单开着时 MouseDown：命中条目=执行+收菜单；点菜单外=只收菜单并吞掉事件。DrawMenu 变纯绘制。
② **选择与窗口生命周期（用户定案）** → `ClearSelection()` 挂 `BackpackPanel.CloseAll()`（取消/更换选择=关窗）；**背包窗口开着时左键空地不清选**（`ClickActOrCancel` 尾部 `HasOpenWindows` 守卫，保护拖拽/交互工作流）。
③ **地上物品** → a) `HandleGroundItemClick`：`ItemObject.GetInteractions` 多于 1 条（弹药箱=补充弹药…）→ **地面交互菜单**（与格子菜单同一套模态机制）；单一拾取交互 → b) **拾取距离化**：联动半径内即时捡，超半径 `moveTo` 派最近士兵走过去到点自动捡（复用会合状态机，`lootItem` 标志），90s 超时取消。
④ **光标太大** → `RingRadius` 10.5→**7**（描边 1.25→1.1）。
⑤ **多窗重叠** → 级联偏移 26→**38px**。
验证：EN/CN 双包 1.4.1（sha256 一致 ✓）。**1.4.1 复测：模态菜单本身 ✓；「穿上」菜单项根本不存在（原生列表只给脱下/丢弃）+ 会合到达不自动开窗 + UI 打磨 → 1.4.2**。
**1.4.2（2026-09-19，合成「穿上」+ 会合到达修复 + UI 打磨）**：
① **穿上（根因+方案）** → 原生 `GetInventoryInteractions` 对非玩家 interactor **不生成装备动作**（菜单只有脱下/丢弃）。补**合成条目**，走士兵的**官方 Lua 脚本通道**：武器=`Lua_Soldier.LoadAndSetWeapon(id, 0)`（官方协程，`FrameEndRunner.RunNativeCoroutine` 显式启动——interop 返回的只是协程对象，陷阱 25 同源）；衣/甲/盔=`wearUniform/wearVest/wearHeadgear(id)`（带 id 的官方重载）。菜单执行列表重构为统一 `menuExec`（原生=Interaction.Call，合成=Lambda），去重（原生已有 wear/equip 文案则不补）。
② **会合到达不自动开窗** → `Lua_Soldier.moveTo(Vector3)` **无半径重载**，默认停距可能 >packRange(3m) → 到达判定永不满足。到达阈值放宽为 `max(4m, packRange)`（开窗规则本身不变）；`OpenMeet`/拾取到达补 LogAlways（`会合到达`），下轮可从日志判定到达是否触发。
③ **UI 打磨** → 背包窗口标题栏加分隔线；信息面板血条与数字分离（数字右对齐独立框，不再叠在条上）；姿态/压制行加「 · 」分隔（EN 词条同步）。
验证：EN/CN 双包 1.4.2（sha256 一致 ✓）。**1.4.2 复测：日志证实「Wear」有执行但无效（wearHeadgear 后头部模型没刷新）、胸挂 us_marine_gear_4a 不含 vest 字样→没合成条目、会合到达有触发但开窗结果日志撒谎 → 1.4.3**。
**1.4.3（2026-09-19，穿戴子类判定 + 会合开窗放行 + 平铺布局 + 交互文案英文化）**：
① **头盔/胸挂穿不上（两个叠加根因）** → a) **关键字匹配漏**：胸挂 id `us_marine_gear_4a` 不含 "vest" → 没合成穿戴项；改**按物品子类判定**（`TryCast<VirtualHelmet>/VirtualClothing/VirtualWeapon`），clothing 再按 id 分 uniform/vest；b) **戴了但头上没有**：日志「Wear」执行成功 = wearHeadgear(id) 数据层生效但**头部模型没刷新** → 戴后补 `RefreshHeadgearVisibility()`。
② **会合到达仍要再点一下** → 日志「会合到达」有触发，但旧日志写「已打开」是**假话**（OpenWindow 可能被远处旧窗口的锚点检查拦掉而静默）。修法：到达开窗 **bypassAnchor**（走过去≤4m 已证明距离）；开窗被拒（上限/锚点）改 LogAlways 如实记录；`ForceOpenSoldier` 返回真实结果；**30s 新窗豁免**范围自动关闭（会合后 units 还在动，宽限期后回归规则）。
③ **窗口重叠** → 26→38px 级联实测仍叠 → 改**槽位平铺**：开新窗取最低空闲槽位（列×行，屏幕内换列），互相不压；手动拖过的窗保留位置但槽位不释放。
④ **EN 版交互菜单显示中文** → `GetInteractionText()` 返回的是游戏**中文源串**（不经本地化）。修法：标签过 `TrInteraction`——常见动作进 Ui 词典（丢弃/脱下/摘下/使用/吃/喝/补充弹药/装填/安装配件/拾起置于右手），前缀规则翻译（拆下X→Detach X 等），未知保留原文。
验证：EN/CN 双包 1.4.3（sha256 一致 ✓）。**1.4.3 复测：右键点到别人头盔 → 头盔被当掉落物捡走；日志零条「派兵前往」→ 远距会合全被物品分支劫持（穿戴物=ItemObject，判定顺序物品在士兵前）→ 1.4.4**。
**1.4.4（2026-09-19，右键判定顺序重构）**：`IssueDirectCommand` 顺序改为 载具 → **活人士兵（友军=会合/敌军=标记）** → 尸体（尸包）→ 地面物品（此时 sol 必为 null，真掉落物）→ 尸体就近兜底 → 其余。点中头盔/胸挂=点中那个兵（身上穿戴也是 ItemObject，旧顺序把人家头盔当掉落物捡走=「头盔消失」，且远距会合永远启动不了=「走过去不自动开」的同根因）。日志佐证：1.4.3 三条「会合到达」全是近距即时开窗，「派兵前往」零条。
验证：EN/CN 双包 1.4.4（sha256 一致 ✓）。**1.4.4 复测：日志只有 3 条「会合到达」且全部 target=True walker=True，但「派兵前往」仍零条 → 远距右键根本没走到会合分支——**真正根因：高视角远景下兵太小，"右键他"实际点中的是他脚边的地面** → 走了普通移动命令，兵到了但没人知道要开包 =「走过去了还不自动打开，还要再按一遍」**。
**1.4.5（2026-09-19，右键点偏容错）**：空白地面移动之前加**友军点偏容错**——命中点 2.5m 内有徒步友军（`NearestFriendlySoldier`，allCreatures 静态表，排除车内兵）→ 按会合处理（走过去开双背包），不再落进普通移动；尸体就近容错 1.2→2.5m 对齐。**注意**：想右键移动到友军身边 2.5m 内的点位会被会合劫持——点远一点即可（用户操作模式优先）。
验证：EN/CN 双包 1.4.5（sha256 一致 ✓）。**1.4.5 复测：用户否决"点偏地面"理论（光标变色才点的，点的就是本人）→ 真凶锁定**到达判定 4m vs 原生 moveTo 停距 8m**：`Lua_Soldier.moveTo(Vector3)` 无半径参数，兵在 moveRadius（默认 8m，本 mod 移动系统的到达半径就是这个值）处停住 → 4m 判定永不满足 → 90s 静默超时 =「明明走到旁边却不自动开」**。
**1.4.6（2026-09-19，到达判定对齐真实停距 + 会合请求全程日志）**：① LootTick 到达阈值 `max(4,packRange)` → **`max(moveRadius, packRange)`**（默认 8m——这是游戏原生 moveTo 的真实停距量级，本 mod 移动观察同源参数）；② `RequestLoot` 两个分支各加一条无条件日志（`会合请求 <title> 距离=X.Xm → 即时开窗/派 X 前往`）——下轮日志可完整还原：点击是否进入会合、距离多少、走没走、停在多远。开窗 3m 联动规则本身不变（到达开窗本就 bypassAnchor）。
验证：EN/CN 双包 1.4.6（sha256 一致 ✓）。**1.4.6 复测：用户抓到规则矛盾——「到达 8m 才开窗」vs「3m 自动关闭」互相打架（会合开的窗 30s 后又被 3m 规则关掉）→ 1.4.7 统一半径**。
**1.4.7（2026-09-19，联动半径对齐移动停距）**：`packRange` 默认 3→**10m**（cfg 仍可 1–100 调）——3m 是在不知道「原生 moveTo 会让兵停在 ~8m 处」时定的；现在开窗门槛、自动关闭、到达判定（`max(moveRadius, packRange)`=10m）三者同一半径：会合开窗不再被更小的半径马上关掉。规则本质不变：隔着半个地图的背包照样自动关闭，只是"近"的定义对齐了游戏移动粒度。
验证：EN/CN 双包 1.4.7（sha256 一致 ✓）。**1.4.7 被用户否决：「单位的手长 10m？」——放大半径是逃避修 bug，规则回到 3m，用停驻重派令把兵真正带进圈 → 1.4.8**。
**1.4.8（2026-09-19，半径回 3m + 停驻重派移动）**：① `packRange` 默认 **回 3m**（用户规则；10m 方案废弃）；② 到达判定回归 packRange（3m）；③ **停驻检测 + 重派移动**：LootTick 每 1.2s 检查 walker 位移，若 <0.5m 且仍在圈外 → 重新下达 `moveTo(目标点)`（最多 8 次，每次无条件 LogAlways 停驻距离）——把兵一步步带进 3m 圈才开窗；④ 超时提示带最后停驻距离（`没有走到目标旁（停在第 Xm），已取消`），重派失败/粒度不足时日志有精确数据。
验证：EN/CN 双包 1.4.8（sha256 一致 ✓）。**1.4.8 复测：走过去了仍不开窗（用户：「近到都把那个单位挤开了，还是没有自动背包」）；但用户第二次右键时日志 `距离=0.9m → 即时开窗` 证明 walker 确实走到了贴身——距离判定本该早就满足 → 真凶不是距离，而是「到达检测整段没执行」**。
**1.4.9（2026-09-19，会合自动开窗：任务静默失效排查 + 停驻即开窗）**：
① **双路驱动 LootTick**：`Tick` 里的调用点位于 `if (!Active || flyingToSquad) return;` 门控之后，历史遗留静默失效风险；OnGUI 渲染循环（`BackpackPanel.Draw`，图标一直在画 = 全程验证存活）也调一次 → 谁活谁驱动（0.25s 节流，双调幂等）。
② **ResetLoot 全程记原因**：此前任务是**静默**被清掉的（一条日志都没有）；现在每次重置打 `会合任务重置（原因）`，LootTick 另每 2s 打一条 `会合进行中 dist=X.Xm 重派=N/8` 全程跟踪。
③ **任务与窗口解耦（关键修复）**：`CloseAll()` 原来顺手 `ResetLoot` —— 而**取消/更换选择链（`ClearSelection` → `CloseAll`）也走这里**，会合途中任何一次选择变动都会静默清掉在途任务（walker 照走，但再没人判定到达）。现在 `CloseAll` 只关窗口/拖拽态；新增 `CancelLoot(reason)` 才杀任务，只在「进 RTS / 接管单位 / 退出 RTS」三处调用。
④ **停驻即开窗**（用户定案「就不能在停止后再触发一次开背包解决吗」）：独立于 packRange 的第二通道——0.25s 采样位移 <0.2m 连续 0.8s（任务开始后 1.5s 宽限）判「停了」，停在 `max(packRange, 5m)` 内直接开窗并记实际距离；停得远了才继续重派/等超时。
⑤ 到达开窗抽成唯一收口 `ArriveOpen(walker, why)`（先取任务态局部量再 ResetLoot，避免收口过程被重入清空）。
验证：1.4.9 编译部署（`ER2_BattlefieldCommander.dll` 11:28，本地/游戏目录 sha256 一致 ✓；cfg 缺失将按默认 3m 重建）。**1.4.9 实测（12:01 日志）：会合自动开窗成功 ✓**——活体单位两例全部 `会合进行中 dist=6.6m/7.2m → 会合到达，开窗 target=True walker=True`；但尸体那例暴露出新根因（见 1.4.10）。
**1.4.10（2026-09-19，车内乘员三连修 + 背包标题叠字）**（用户三条反馈：①背包文字重叠 ②无法打开尸体背包 ③让单位上坦克后又会立刻下车）：
- **共同真凶 = 把「车里的兵」当徒步单位**（1.4.9 日志实锤）：`会合请求 ... (KIA) 距离=8.1m → 派 Frank Nelson 前往` 之后 `dist` 恒为 8.1m、重派 4 次毫无变化 —— 因为挑中的 walker 是**坦克里的乘员**，对他 `moveTo` 不会走过去（原生 AI 的反应是**下车步行**）。同一份日志里 `[SquadCmd] 移动 ... via=Fallback` 也把乘员卷进逐兵 `moveTo` → 正是「上车后立刻下车」。
  → 新增 `GodViewController.IsOnFoot(s)`（`GetComponentInParent<Vehicle>` + `Lua_Soldier.isInsideVehicle()` 双检测，`IsInfantry` 改为它的别名）与 `FilterOnFoot(list, out embarked)`；`MoveUnits`、`MoveCommandTo`（含登车 pending 摘除）全部先过滤；`BackpackPanel.RequestLoot`/`RequestItemPickup` 的候选集只从徒步单位里挑（都选自 `anyBest` 兜底，仅在「已在半径内→即时开窗/拾取」时用）；**选中的单位全在载具里时改派最近的徒步友军**（`NearestOnFootFriendly` = `NearestFriendlySoldier` 暴露版，30m 内；上车后「转选」到载具是常态，不改派的话那个背包永远打不开），附近也没有则提示并记日志。
- **背包标题叠字**（截图：`Backpack · Melvin McCampbell` 压住 `44.2/60`）：根因是 IMGUI `GUI.Label` **不裁剪**，长标题直接画到右边。新增 `FitTitle`：按可用宽度三轮适配（缩字号 12→9 → 去掉「背包 · 」前缀保留「（阵亡）」后缀 → 9 号字省略号截断），结果按 (文本,宽度) 缓存；同时把负重/迷你条从 `xMax-194(宽92)` 收窄到 `xMax-166(宽64)`、标题可用宽 `WinW-182`。
验证：1.4.10 编译部署（`ER2_BattlefieldCommander.dll` 12:10，196096 字节，sha256 一致 ✓，cfg 重置为默认）。**1.4.10 实测（12:20 日志）：尸体背包修好了 ✓** —— `会合请求 Backpack · Wade Malarkey (KIA) 距离=14.5m → 派 Desmond Harmon 前往` → `会合进行中 dist=14.5m → 7.9m` → `会合到达，开窗 target=True`（兵真的走过去了，不再是恒定的 8.1m）；后续 6 具尸体全部一次点开。
**1.4.11（2026-09-19，尸包开双方背包 + 关窗不再取消选择）**（用户两条新反馈）：
- **① 打开尸体背包时同时打开选中单位的背包**：两处尸体分支 `RequestLoot(corpse…)` / `RequestLoot(corpseNear…)` 补 `openBoth: true`（原先只有徒步友军传 true）→ 与"右键友军开双方背包"一致。到达/停驻/即时三条路径都走同一个 `OpenMeet(target, walker, true)`，walker 窗口 `bypassAnchor=true` 不会被锚点规则拒。
- **② 关闭背包不再取消选中单位**（真凶是**跨帧残留手势**）：点 ✕ 时按下帧 `guiNow=true` 所以不建立选择手势，但 **OnGUI 里窗口在同一帧被关掉** → 松手那一帧 `guiNow` 已变 false → 松手被当成"空地点击" → `ClickActOrCancel()` → `ClearSelection()`。修法：`BackpackPanel` 里凡 `e.Use()` 吃掉事件（✕/◀/▶、标题栏拖动、交互菜单项、格子、拖拽落地）都在 `Draw()` 末尾 `MarkConsumed(e)`（`e.type == EventType.Used` 即命中）→ 调新增的 `GodViewController.SwallowLeftGesture()`，让战场手势吞掉这一按的剩余部分（与既有的 `swallowLeftGesture` 收尾机制同源）。
验证：1.4.11 编译部署（`ER2_BattlefieldCommander.dll` 12:24，196608 字节，sha256 一致 ✓）。
**1.4.12（2026-09-19，穿戴「只能脱不能穿」诊断+双通道修复）**（用户反馈：让单位装备还是不行，只能脱下来）：
- 现状证据（12:30 日志）：`交互执行 'Wear'` 有执行、`Take Off` 正常、无任何异常 → **原生 Lua 穿戴通道静默 no-op**；`debugLog=false` 导致 `合成穿戴项` 这类 `Log` 级诊断一条都看不到（**诊断也得用 LogAlways**）。
- 三个怀疑与对应动作（一次实测即可定案）：
 ① **虚拟物品没有活体实例**（头号嫌疑）：原生 `wearXxx` 拿到只有 `item_id`、`instance==null` 的 `VirtualItem` 会静默 return → 新增 `EnsureItemInstance`（`IsInstance/GetInstance/GetItemPrefab/SetInstance`）。
 ② **菜单里那条"Wear"可能是原生条目**（原生 `Interaction.Call()` 无参=interactor 已绑定，对非玩家静默无效）→ 新增**剪除原生 wear/equip 条目**（保留 Unwear），并 `LogAlways` 打印**原生菜单原文**，一次就能看出"Wear"来源。
 ③ **原生改的是数据、视觉不刷新** → 穿戴后无条件 `TriggerClothingObjRefresh` + `RefreshHeadgearVisibility`。
- **直写兜底**：调用原生后比对 `WearSnapshot`（`hasHeadgear/headgear_ref.item_id`、`hasUniform/uniform_ref`、`hasVest/vest_ref`），**状态没变就直接写原生字段**：盔=`headgear_ref`+`SetHelmetObject(ItemHelmet)`；衣=`uniform_ref`+`uniform_Obj`；甲=`vest_ref`+`vest_Obj`（六个字段反编译确认都有 setter），末尾再 `TriggerClothingObjRefresh(kind==0, kind==2, kind==1)`。
- 每次穿戴留一行完整快照：`穿戴 id=… 类型=盔 实例=True/False 取到物件=… 路径=原生/直写兜底 前[盔=- 衣=x 甲=-] 后[盔=x ...] 穿戴标记=True/False`。
验证：1.4.12 编译部署（`ER2_BattlefieldCommander.dll` 12:35，sha256 一致 ✓）。
**1.4.12 实测（12:39 日志）定案三件事**：
① 原生菜单原文 = `Drop Item | Take Off` → **原生列表里根本没有穿戴条目**（菜单里那条 "Wear" 确实是我们合成的）→ 假设 ② 排除。
② `实例=False`（虚拟物品确实没有活体实例）+ `路径=直写兜底` + **`前[盔=- …] 后[盔=- …]` 完全没变** → `Lua_Soldier.wearHeadgear(id)` 与直写 `headgear_ref/SetHelmetObject` **全都无效**；第三次尝试后 `穿戴标记=True`（虚拟物品被标记为已穿）但盔槽仍为空 = 用户看到的「模型没出现在头上」。
③ **`debugLog=false` 把 `合成穿戴项` 这类 `Log` 级诊断全吞了** → 排查阶段的诊断必须用 LogAlways（本次已统一改回受开关控制）。
**1.4.13（2026-09-19，穿戴改用反编译确认的原生入口 + 日志开关收口）**：
- **反编译 interop 找到真正的原生穿戴 API**（此前两轮用的都不是它）：
  `Soldier.SetWerable(VirtualItem, bool sync=false)` / `SetWerableCR(…)`（原生"穿上虚拟衣物"，CR 版负责异步加载 prefab）、
  `Soldier.PickUpItemFromInventory(VirtualItem, InventoryManager source, int wearedItemIndex=0)`、`IsWearing(VirtualItem)`、
  `UpdateWearedItemsToInventory()`、`GetHeldItemIndex(VirtualItem)`；
  `WearedItem`（struct）= `{ ItemObject itemInstance; VirtualItem inventoryReference; }`，即**穿戴记录里存的是"活体物件"**——
  这就是"直写 `headgear_ref` 没用"的原因（缺 `itemInstance`，游戏侧无法建模型）。
- `WearItem` 改成**阶梯式尝试 + 每步验证**（验证=穿戴快照变化 或 `IsWearing(vi)` 翻转），第一个见效即停：
  ① `SetWerable(vi)` → ② Lua `wearXxx(id)` → ③ `PickUpItemFromInventory(vi, invMgr, 0)` → ④ 直写字段（仅在已有活体实例时）
  → ⑤ `SetWerableCR(vi)`（协程，异步，交给下一帧）。日志记 `生效级=…`，**下一轮实测看完即可把无效的几级删掉**。
- 不再用 `GetItemPrefab()` 冒充实例（`SetInstance(prefab)` 是错的，已删除该步骤）。
- **日志开关收口（用户要求）**：所有排查用诊断（`原生菜单项`/`合成穿戴项`/`穿戴 id=…`/`会合进行中`/`会合任务重置`/停驻系列/`图标诊断`/`移动：跳过车内乘员`）统一走 `SquadCmdLogic.Log`，由 cfg **`debugLog`** 控制、**默认 false**；用户可见的状态事件（开窗成功/到达/超时/丢弃）仍 `LogAlways`。本轮测试已在本地 cfg 临时置 `debugLog = true`（发布前改回 false 或删 cfg 即可）。
验证：1.4.13 编译部署（`ER2_BattlefieldCommander.dll` 12:44，sha256 一致 ✓）。用户实测确认**穿戴修好了**。
**1.4.14（2026-09-19，性能优化 + 发布）**——用户定案「修好了。优化一下游戏与代码，性能优先，准备发布」。本轮**不动任何行为**，只消除每帧/每流程的重复 interop 调用与临时分配：
- **① 友军小队枚举加 0.3s 缓存**（`SquadCmdLogic.GetAllFriendlySquads`）：原实现每次调用都 `CollectSquads()` → 遍历**全场景所有 `Creature`** 并逐个 `TryCast<Soldier>`（大战场数百次 interop）+ 分配 `Dictionary`/`List`，而它在「进 RTS / 接管 / 编组 / 死亡重挂」等流程里会被**连续多次**调用 → 数百毫秒卡顿感的主因。新增 `GetAllFriendlySquadsCopy()` 供需要长期持有的调用方（现有 5 个调用点全部只做 `foreach` 遍历，不修改，安全）。
- **② `ResMult()` 加 0.5s 缓存**（分辨率倍率）：`HudStyle/HudStyleSmall/ButtonStyle/InfoPanel/BackpackPanel` **每次**取样式都读一次 `ResourcesManager.ResolutionMult` interop 属性，而 OnGUI 一帧多次事件 × 每帧十几处取样式 → 每秒上千次 interop 读。
- **③ `MainCam()` 加 0.5s 缓存**：每帧被调 5 次（`HandleMove`/`HandleHeight`/`ApplyCam`/`LateApply`/`OnPreRenderCamera`），兜底路径 `Camera.main` 内部是 `FindGameObjectWithTag`（很贵）。只在拿到有效相机时缓存，拿不到保持重试。
- **④ `MySideFaction()` 加 2s 缓存** + **⑤ `CachedPlayerSoldier()`（0.5s）**：这两个是**最热**的 interop 路径——`Soldier.GetBestVisibleEnemy` Postfix 与 `Vehicle.CurrentVisibleTarget` Postfix **每兵每帧**各跑一次，集火标记生效期间战场上数百单位 → 每秒上万次 `PlayerController.currentController` / `ControlledCharacter.faction` 读取 + 字符串封送。两处 Postfix 已改为走缓存；`SetPlayer` 三处调用点（进 RTS / 退出 / 接管）插入 `InvalidatePlayerSoldier()` 立即失效，`SavedFaction` 变化时阵营缓存自动失效。
- **⑥ 每帧标记绘制去分配**：`SceneMarkersFrame` 里 `new List<Vehicle>(selVehicleRefs)`（每帧一次）改为按下标遍历；`smSelected` 从"每帧 Clear+逐单位 Add"改为与 `markerCache` 同批 0.2s 刷新；顺带在同批算出 `smSelectedInVehicle`，**取代**"每个选中兵每帧一次 `GetComponentInParent<Vehicle>()`"。
- **⑦ `PruneMark` 的 `new List<Soldier>()`（每 0.5s 一次）改为复用缓冲区**。
- **⑧ `IsSelectedUnit` 加缓存快路**：先查 `smSelected`（与 `GetCommandUnits` 同源的 0.2s 指针集），未命中再走原 interop 兜底 —— **保持语义不变**（缓存只加速，不是唯一判据）。
- 验证：编译 0 error；EN 构建 sha256 `462f1f59…9094`，已部署（13:01）+ 双包产出（`ER2_BattlefieldCommander_v1.4.14.zip` / `_CN_v1.4.14.zip`）；CN 构建 `Ui.Tr` 反编译确认返回中文原文（`CN_BUILD` 生效 ✓）。
- 发布收尾：本地 cfg `debugLog` **已复位 `false`**（上一轮临时开的），README/Nexus 描述版本号与性能说明已同步。
**1.4.14 性能优化清单（供后续在同一量级工程上复用）**：
  1. 判断"这个 interop 读会不会在每帧/每兵每帧被调到"——`PlayerController.currentController`、`ControlledCharacter`、`ResourcesManager.*`、`Camera.main`、任何 `.faction`/`.name` 字符串读都是重点嫌疑；
  2. 枚举型工具函数（`CollectSquads` 这类"遍历全局静态表"的）一律加短缓存，否则被连续调用时浏览器级开销；
  3. OnGUI 路径里 `new List<>`/`new Dictionary<>`/`ToArray()` 逐个清掉，复用静态缓冲区；
  4. 缓存失效点要显式（`InvalidatePlayerSoldier`/`InvalidateMainCam`），并让"拿不到值的缓存"不写入（避免把 null 缓存成半秒的真相）。
### 2.12 UniversalGeneration `er2.universalgeneration` v2.5.36
**2.5.36（2026-09-25，"进游戏固定时间卡几秒"定案 + 磁盘物品目录缓存）**：2.5.35 的耗时探针一锤定音——
```
GetAllItemsOfType<PropData>(items)      单次 4533ms
GetAllItemsOfType<ItemObject>(weapons)  单次 5463ms
GetAllItemsOfType<ItemObject>(ammo)     单次  242ms / attachment 406ms
物品目录就绪合计 1509 条；探针总耗时 64173ms
```
**根因**：游戏 API `ItemsDatabase.GetAllItemsOfType<T>(PropType)` **每次调用都全量过滤数据库，单次最高 5.5 秒且为原子调用（时间片无法切分）**——ilspycmd 反编译确认 `ItemsDatabase` 没有暴露物品集合字段，这是唯一全量入口。而 UniGen 此前调它的次数远超必要：ProbeCR 每轮 SampleCounts 8 次 + EnumerateAll 8 次（最多 5 轮）、就绪 20s 后 EnrichCR 补漏再 8 次、看门狗重启再来。
**修法（三件套）**：
① **会话内 API 结果缓存** `typeCache`（key=类型名|propType）：同一 (T, type) 一次会话只调一次真实 API，其余全走缓存；`GetAllCached<T>` 统一入口（>150ms 仍打性能 Warning，但注明"已缓存不再重调"）。
② **磁盘物品目录缓存** `BepInEx/config/er2.universalgeneration.items.cache.tsv`（TSV：bucket/sub/id/title，转义完备）：ProbeCR 开头 `TryLoadCacheFromDisk()` 命中 → **Ready + 零枚举调用**；枚举成功后 `WriteCacheToDisk()` 写盘（跳过 "mod" 桶——mod 物品每次启动由 ModCatalog 重扫，写盘会陈旧）。cfg 新增 `Catalog.refreshItemCache`（默认 false，游戏更新新增物品后勾一次刷新）。
③ **废弃"就绪 20s 后自动补漏"**（EnrichCR 保留但无任何自动触发，重扫前 `InvalidateTypeCache()`；其使命由磁盘缓存取代）+ **SampleCounts/Count* 改为只读缓存**（miss 返回 -3，绝不在观测路径触发 5.5 秒原子调用）+ 删除死字段 `Failed/nextEnrichAt/enrichDone`（0 警告）。
**预期效果**：首次启动（无缓存文件）仍有首轮 8 次原子调用（约 10 秒分散冻结，首次枚举不可避免）；**第二次启动起，进游戏后卡顿完全消失**（读盘 <10ms）。
版本 2.5.35→2.5.36（BepInPlugin + 启动日志两处；发布文档下轮出包前补 2.5.35/2.5.36 条目——本版仅诊断+缓存，无新功能）；编译 0 警告 0 错误；部署 sha256 `1B53BF17…` == bin。
**待复测**：第二次启动起，进游戏后不再有固定时间的卡顿；日志出现 `物品目录自磁盘缓存加载: N 条——本次启动零枚举调用`；游戏内物品页签内容与之前一致（1509 条量级）。

### 2.12.1 UniversalGeneration `er2.universalgeneration` v2.5.38（bundle 等待 = 枚举完整性的硬保证）+ v2.5.37（修缓存固化残表回归）+ ModManager 1.5.14（音效取证）
**玩家报**：① "不知道什么时候开始，翻页到 mod 设置页面没有音效了"；② "mod 物品加载要在战斗开始后，进入不够快列表就少很多"；③（v2.5.37 复测）"好像并没有改善"。
**② 的根因链（三层，逐层挖出）**：
- 第一层（v2.5.36 回归）：磁盘缓存固化了主菜单阶段的**不完整枚举**（475/1509 条，写盘条件不问场景）→ 2.5.37 加场景条件；
- 第二层（v2.5.37 复测发现）：**战斗场景中枚举也只有 560 条**（weapons=229 ammo=204 gear=127，单次调用最高 8362ms）——"战斗场景"判据仍不够；
- 第三层（v2.5.38 定案）：**物品定义在 `er2bundle` asset bundle 里渐进加载**——任何时刻"顺手枚举"都可能拿到半库。**反编译 ItemsDatabase 找到 `WaitForAssetBundleLoaded(string)`**：挂起等待直到 bundle 完全加载 = 枚举完整性的硬保证。
**v2.5.38 修法**：
① ProbeCR 开头 `yield return ItemsDatabase.WaitForAssetBundleLoaded("er2bundle")`；
② **首轮只枚举核心三类**（ItemObject × weapons/ammo/attachment，实测贡献 1451/1509=96%）→ Ready 后面板立即可用；**删除 PropData(weapons/ammo/attachment) 兜底路径**（2.0.7 实测恒 0，纯浪费 3 次慢调用）；
③ **items(6) 类**（投掷物/食物/杂项 58 条，两次调用实测 4.5~8 秒是全场最慢）拆成**延迟补漏轮**：Ready 后 30s 由看门狗触发一次 `LateEnrichCR`（InvalidateTypeCache + 枚举 items + 有增长则更新页签并重写磁盘缓存），错峰不占面板首开；
④ 读盘命中的会话（磁盘缓存已含 items 类）连补漏都不跑。
**① 音效**：v1.5.14 的 `ClickSound failed` 探针**零输出**——ClickSound 调用成功（无异常）但无声，问题在原生 SoundManager 内部（黑盒）。下一步：反编译其原生实现不可行（IL2CPP 编译后的 C++），改为**行为对照**（让玩家确认原生页间翻页是否有声）+ 必要时改用 `SpawnAndPlayAsync` 播已知音效 id 的替代方案（需先找到 UI click 的 sound_name_id）。
版本：UniGen 2.5.37→2.5.38（两处同步）；ModManager 1.5.14 已部署（音效探针）；编译 0/0；UniGen 部署 sha256 `697156C1…` == bin。
**待复测**：① 物品目录应恢复 ~1509 条量级且进战斗后无固定时间卡顿（bundle 加载完成的枚举一次到位；items 类 30s 后补）；② 若日志出现 `er2bundle 已就绪` 后枚举条数仍 <1500 → bundle 加载完也不完整，需再挖；③ 音效等玩家确认 + 日志。

### 2.12.2 UniversalGeneration `er2.universalgeneration` v2.5.37（修 v2.5.36 缓存固化残表回归）+ ModManager 1.5.14（音效取证）
**玩家报**：① "不知道什么时候开始，翻页到 mod 设置页面没有音效了"；② "mod 物品加载要在战斗开始后，进入不够快列表就少很多"。
**② 的真相比描述严重**（读日志发现）：磁盘缓存里只有 **475 条（实时枚举是 1509 条）**——**v2.5.36 把主菜单阶段的不完整枚举固化进了缓存**（主菜单时数据库只加载一部分，而写盘条件不问场景），之后每次启动都读这份残表 = **整个物品目录少 2/3**，mod 物品晚到只是次要因素。
**修法（v2.5.37，三处）**：
① **主菜单不做探测**（`ProbeWatchdog` 开头 `InBattleScene()` 判定，`MainMenu.instance == null`，与 Endless 同判据）——主菜单枚举既不完整也无意义（面板只在战斗 RTS 内可用）；
② **只有战斗场景中完成的枚举才写盘**；主菜单枚举仅作预热并打日志"不写磁盘缓存，进入战斗后自动重扫"；
③ **场景完整性检查**：`Ready && !cacheWritten && InBattleScene()` → `ResetForFullRescan()`（清桶/缓存/复位）→ 战斗场景中完整重扫 → 写盘 → `cacheWritten=true` 永不再扫。**缓存文件头新增 `full=1` 完整标记**：读盘时无标记（旧版/主菜单期写入）不采信——否则旧残表会被"已就绪"标记永久固化。**已手动删除玩家机器上的 475 条坏缓存文件**。
预期：首次启动进战斗后重扫一次（一次性的数秒冻结）→ 写完整缓存；之后每次启动读盘零调用、目录完整（1509 条量级）。
**① 音效**：所有翻页分支都调了 `ClickSound`（trace 带 [sound]）但玩家听不到——唯一能吞掉线索的是 `PlayClick` 的**空 catch**。v1.5.14 在 catch 里加 `LogWarning`（失败必留痕）。若下轮日志出现 `ClickSound failed` → 异常定案；若无 Warning 但仍无声 → 问题在原生 SoundManager 侧（届时反编译 `SoundManager.ClickSound` 查它的内部条件）。
版本：UniGen 2.5.36→2.5.37、ModManager 1.5.13→1.5.14（各两处同步）；双双编译 0/0；部署 sha256 `961EC02D…` / `76F49910…` == bin。
**待复测**：① 物品目录应恢复 1509 条量级（日志 `物品目录自磁盘缓存加载` 的数字），且**无固定时间卡顿**（第二次启动起）；② 翻页音效——若仍无声，看日志有无 `ClickSound failed`。

### 2.12.3 UniversalGeneration `er2.universalgeneration` v2.5.46（物品目录**真正长齐**；玩家确认"现在全了"）
**2.5.37→2.5.46 连续 10 个版本都在修同一件事：物品目录条目不完整。** 最终实测（单会话三轮累积）：
```
验证第 2 轮: 1164 → 1921（新增 757）  weapons=612 ammo=204 gear=473 food=4 mod=628
验证第 3 轮: 2109 → 2923（新增 814）  weapons=1164 ammo=209 throwables=49 gear=666 food=18 misc=1 mod=816
```
官方条目 **1164 → 2107**（史称"完整"的 1451 其实也是早期快照）；分类也补齐（投掷物 49 / 医疗食物 18 / 其他 1）。玩家确认"现在全了"。

**根因链（四层，每层都被下一轮日志推翻过一次）**
1. **缓存固化半库**（v2.5.36/37）：写盘条件不问时机，把主菜单阶段（库只加载约 1/3）的枚举结果存成"已就绪"→ 之后每次启动都读残表。修：`full=1` 标记 + 场景条件（后被证伪，见 3）。
2. **bundle 等待 ≠ 条目完整**（v2.5.38）：`WaitForAssetBundleLoaded("er2bundle")` 只保证**资源包已加载**，不保证 `ItemsDatabase` 已登记全部条目——两件事不是一回事。修：加挂起等待（必要但**不充分**）。
3. **场景判定连续两次被实测证伪**（v2.5.40/41）：`MainMenu.instance == null`（它是常驻对象，恒 false）→ 换 `BattleManager.IsBattleActive()`（RTS 上帝视角下也是 false）→ 探测/写盘/验证**整体被跳过**，物品页签直接消失（玩家："大选项 4 行变 3 行"）。修：**彻底删除场景判定**，完整性只由 bundle 等待保证。
4. **触发块是死代码 + 守卫值恒为 0**（v2.5.42/43）：`ProbeWatchdog` 第一行 `if (Ready) return;`，而验证/补漏触发块写在它**之后** → 读盘命中使 Ready 启动即 true → 每秒都在第一行返回；即便挪到前面，守卫 `readyAt > 0f` 也恒 false，因为读盘路径的 `readyAt` 取自**插件 Awake 那一刻**（游戏一帧未跑，`Time.unscaledTime = 0`）。修：触发块前置 + 基线自愈。
5. **单轮快照 ≠ 全量**（v2.5.45）：官方物品库**随进程逐步长成**（开局 23s 枚举 1164 条，久玩后 2107 条）。修：**多轮累积验证**——单会话最多 3 轮（间隔 30s/120s），每轮只增不减地并入（`Add` 按 Id 去重），某轮无新增才判定稳定。
6. **验证轮重叠执行**（v2.5.46）：下一轮按固定间隔触发、不等上一轮结束，而单轮耗时 30~60s → 间隔早已过期 → 两轮并发（日志两行同号"第 2 轮"，冻结翻倍）。修：`verifyActive` 计数守卫 + 间隔从**上一轮结束时刻**起算 + 轮次编号在轮内捕获。

**免重复代价的关键设计（v2.5.44，务必保留）**：缓存头记 `verified=<0|1>` + **游戏构建签名**（`GameAssembly.dll` + `global-metadata.dat` + `StreamingAssets/CorvoBundles/er2bundle` 的 大小:修改时间秒）。签名一致且 verified=1 → **本会话完全跳过枚举**（省下 4 次原子调用、实测合计 54.6 秒冻结）；游戏更新则自动重验；cfg `Catalog/refreshItemCache` 手动强制。**只有"某轮无新增"才允许写 verified=1**——未稳定不得声称可信，否则"跳过工作"的标记会把没长齐的快照永久固化（本项目在 2.5.36/2.5.37 已经踩过一次）。

**方法论沉淀（跨 mod 通用）**
- **数据源逐步长成时，单次采样既不能证明完整、也不能证明稳定**，必须"累积 + 稳定判定"；写入"可跳过重算"的标记前，必须确认当前状态真的达标。
- **给 early-return 函数加分支要先画控制流**：服务于"已就绪/已完成"状态的逻辑必须放在 `if (Ready) return;` **之前**——就绪态恰恰是它最先吞掉的状态（编译器对不可达语句几乎不警告）。
- **守卫值必须连同"谁在什么时机写入"一起审**：`x > 0f` 形式的判据，若存在一条路径在"时钟尚未开始"时写入 x，该路径会被静默拒绝。
- **"某事件没发生"的结论前，先确认探针覆盖了该事件的全部路径**；本段两次因为"半份证据"定错方向。
- **反编译游戏里实际加载的 DLL（`ilspycmd -t <命名空间>.<类型>`）是排除"部署/构建不一致"的最快手段**——本轮靠它把范围从"代码路径"收敛到"运行时取值"。
- **瞬时视觉故障用录屏逐帧亮度分析定位**（ModManager 白闪那次：4fps 定区间 → 30fps 细查 → 帧均亮度 125→344 直接抓到闪帧）。

版本：2.5.46（BepInPlugin + 启动日志两处同步）；编译 0/0；部署 sha256 `E29C45CC…` == bin；README / Nexus_description 已同步到 2.5.46（补 2.5.35–2.5.46 变更史，此前停在 2.5.34）；台账版本行已更新。

### 2.12.4 UniversalGeneration `er2.universalgeneration` v2.5.47（**稳定性判据选错了比较对象** → 每次启动都重跑验证枚举）
**玩家**："不知道为什么进游戏后总是要卡一会。" 日志（08:07 会话）末尾正是元凶：
```
[UniGen] 开始验证物品目录（第 1 轮，当前 2222 条，等 er2bundle 加载后实时枚举并并入）…
[Warning] GetAllItemsOfType<ItemObject>(items) 单次耗时 8616ms（原子调用，结果已缓存）
```
**根因**：验证轮的收敛判据是 `TotalCount()` **前后差 > 0 即"仍在增长"**。而 `TotalCount()` **包含 `mod` 桶**——mod 物品由 ModCatalog **异步分轮**补入（每轮几百条），于是每一轮验证都必然"有新增"，**"官方目录已稳定"永远判不出来** → 每会话都跑满 3 轮、每轮数次原子调用（单次最高 8.6 秒）→ 玩家体感的"进游戏后卡一会"。
**修法（2.5.47）**：
① 新增 `OfficialCount()`（**排除 mod 桶**），稳定性判定改用它；日志同时打印两侧数字并注明"mod 不计入判定"；
② 新增 `MaxStableGrowth = 5`：官方新增 ≤ 5 即判稳定。**刻意不是"必须恰好 0"**——游戏内部还会零星登记几条，要求 0 则判据永不成立（正是本轮踩的坑）；漏掉的几条下次验证自然并入；
③ 稳定那轮写盘并标 `verified=1` → 后续启动（签名不变）**完全跳过枚举：零数据库调用、零冻结**。

**通用教训（本节的第三类"判据"错误，值得单列）**：本节先后栽在三种判据上——
1. **触发时机判据**（`readyAt > 0` 在读盘路径恒 0）；
2. **场景启发式判据**（`MainMenu.instance == null`、`BattleManager.IsBattleActive()` 连续被实测证伪）；
3. **比较对象判据**（用 `TotalCount()` 判"官方目录是否稳定"，被异步的 mod 桶污染）。
它们表面毫不相干，**归纳起来是同一条**：**判据涉及的每一个量，都必须追问"它由谁写、在什么时机写"**。写的人不对（Awake 时钟为 0）、时机不对（场景状态猜错）、或来源不对（异步旁路写入），判据就会静默失效——而且全都**不报错、日志无痕**，只能靠"把量打印出来"定位。
版本 2.5.47（两处同步，grep 复核）；编译 0/0；部署 sha256 `34FE5BC5…` == bin；四份发布文档（EN/CN × README/Nexus）同步。
**实测确认（08:17 会话，玩家反馈「卡顿仍在」= 这一轮一次性代价）**：
```
验证第 1 轮: 官方 2107 → 2107（新增 0；判定依据）· mod 0 → 816（不计入判定）耗时 62085ms
单次原子调用: items 2224ms / PropData(items) 1523ms / weapons 5749ms / ammo 244ms / attachment 196ms
物品目录已写磁盘缓存: 2923 条（verified=1，签名=35344384…）
官方目录已稳定（本轮新增 0 ≤ 5）→ 标记 verified=1：后续启动不再做验证枚举，零原子调用、零冻结。
```
缓存头已落 `verified=1` + 当前构建签名（实测），数据行 2107（mod 桶按设计不写盘）。**判据修正在首次运行即收敛（新增 0）**——官方库在上一会话三轮累积后已饱和，本轮枚举纯属"再确认一遍"。

**关键认知**：**收敛判据必须先跑一轮才有数据可判**，所以"最后一次卡顿"无法省掉——用户体感的"仍在"其实是这一轮一次性代价。之后启动走 `verified=1 → 跳过` 分支（日志会打 `缓存校验状态: verified=1 签名一致=True → 本会话跳过验证枚举`）。游戏更新使签名变化时会再跑一轮，那是发现新物品的唯一途径，属设计内的必要成本。
**另一个观察**：本轮单次调用明显小于上一轮（weapons 5.7s vs 8.1s、items 2.2s vs 5.3s），数据库"热"起来后过滤更快——枚举成本本身也随游戏进程下降。

**待复测**：下次启动日志应出现 `缓存校验状态: verified=1 签名一致=True → 本会话跳过验证枚举`，且**完全没有** `开始验证物品目录` 与 `GetAllItemsOfType` 性能 Warning。

### 2.12 UniversalGeneration `er2.universalgeneration` v2.5.34
**2.5.34（2026-09-25，面板透明度 50% 真正生效 + 发布包重出）**：
- **修复"设了 50%、实际是 55%"**（用户："应该是50%透明度"）：cfg 侧 2.5.16 起就是 `0.50 / 0.40~1.0`，但共享层 `Er2Ui.SetPanelAlpha()` 仍钳在 `0.55~1.0`（默认 0.85 时代的遗留）→ `SetPanelAlpha(0.50f)` 被**静默抬到 0.55**，且 0.40~0.55 整段是死区；**不报错、不告警、日志无痕**。修法：`Mathf.Clamp(v, 0.40f, 1f)` 与 cfg 范围对齐（新陷阱 **AGENTS 17g52**）。
- 文档四份（`README.txt` / `README_CN.txt` / `Nexus_description.md` / `Nexus_description_CN.md`）首行版本 + **2.5.34 条目**同步；行尾/编码保持（CRLF、UTF-8 无 BOM）。
- 双语打包（先 `-Cn` 后默认 EN，**EN 最后跑=最终部署**）：`ER2_UniversalGeneration_v2.5.34.zip`（EN，**当前部署**）/ `ER2_UniversalGeneration_CN_v2.5.34.zip`（CN）。
  **验收**：EN 构建 = 部署 = EN 包内 DLL **逐字节一致** sha256 `fcc9a489eca8c5d4d99604f0972c794f0f31f19e708388ebba928cb292278c27`（128,000 B）；CN 包 DLL `a5198575…`（120,320 B，`CN_BUILD`）。
  两包均 = DLL + README.txt + Nexus_description.md，**包内文档与源文档 `cmp` 全等**（按包语言正确路由）；反编译复核版本双写 2.5.34 ✓ + `Mathf.Clamp(v, 0.4f, 1f)` ✓。
- ⚠️ **2.5.33 双包已被本版取代**（2026-09-25 Downloads 清理一并移除）。

**2.5.33（2026-09-25，**发布版**：诊断日志收口 + 文档补齐 + 双语重打包）**：
- **发布前日志清理（AGENTS.md §7「高频日志、限频诊断全清」）**：把 UniGen 侧仍未受开关约束的**周期性 / 限频诊断**全部收进 `Debug/debugLog` —— `GenPanel` 阵营行 2s 状态探针、`ItemCatalog.LogProbeDiag`（5s 就绪探测）、`ModCatalog` 第三方内容每轮扫描行（15s/60s 循环）、`ModCatalog` 小队覆盖统计。
  **保留**（判定为低频锚点或异常触发，不算"限频诊断"）：面板首建 UI 诊断快照（一次性；§3.5#4 明文指定它为例范）、图形状态污染告警（异常才触发，且是 LogWarning 不受门控）、物品库可枚举计数（一次性）。门控写在**调用点 + 函数内两处**（调用点门控顺带省掉字符串拼接开销；`ModCatalog` 的 `probeTicks` 存活计数不受门控影响——它是看门狗信号）。
- **版本升 2.5.33**（源码改动 → §7 `z+1`），Edit 后回读复核双写 ✓。
- **文档补齐（§6 发布前查文档三项之①）**：`README.txt` / `README_CN.txt` / `Nexus_description.md` / `Nexus_description_CN.md` 首行版本 + **变更史一次性补齐 2.5.20~2.5.33 共 14 条**（此前 README 停在 2.5.19、本台账 §2.12 停在 2.5.26 = 典型版本漂移）；物品 Shift 连续放置同步进 How to use / Main features。四份文档行尾/编码保持原样（CRLF、UTF-8 无 BOM）。
- **双语打包**（先 `-Cn` 后默认 EN，**EN 最后跑=最终部署**）：`ER2_UniversalGeneration_v2.5.33.zip`（EN，**当前部署**）/ `ER2_UniversalGeneration_CN_v2.5.33.zip`（CN）。
  **验收**：EN 构建 = 游戏目录部署 = EN 包内 DLL **逐字节一致** sha256 `f61b8bf617FFE55364E7E32C57A214E173D878CD462B943C239945A7FE28A5EE`（128,000 B）；CN 包 DLL 因 `CN_BUILD` 不同 `f4dd3e3b…`（120,320 B）。
  两包拆包均 = DLL + `README.txt` + `Nexus_description.md` 3 文件，**包内文档与源文档 `cmp` 全等**，且文档按包语言正确路由（CN 包 README 31,993 B = `README_CN.txt`，EN 包 36,079 B = `README.txt`）。
  反编译复核：版本双写 2.5.33 ✓；**5 处日志门控全部在位**（`debugLog.Value && …` / `!debugLog.Value` 早退 / `debugLog.Value || passes == 1`）✓；入口三色复位未受影响 ✓。

**2.5.27-2.5.32（2026-09-25，灰字收尾 + 开关按钮 + Shift 连续放置，逐轮）**：
- **2.5.27**：整数对齐**补全到最后一批裸绘制调用点**（标题行 / 阵营行 / `ItemHelp` / `CrewRow` / `Er2Ui.Pager`）——走 `LabelShadowed` 的文字已对齐，这些裸 `GUI.Label`/`GUI.Button` 仍落在分数像素上（像素实证：Allies 整数位 255 纯白、Neutral 分数位 229 灰）。
- **2.5.28**：阵营行绘制前**再强制复位**三色状态 + 挂 2s 节流状态探针（分界线 = 是否经过 `LabelShadowed` → 污染发生在入口复位之后、这些裸绘制之前）。
- **2.5.29**：① 左缘「生成 [G]」开关按钮适配 mono 风格（矩形整数对齐 + `Edge` 描边，与页签/阵营同款）；② **物品 Shift 连续放置**——地面分支在 Shift 按住且生成成功时**不 Cancel、保持携带与幽灵模型**，可连续丢，右键结束。
- **2.5.30**：**英文版回退中文修复**——2.5.29 把" · Shift 连续"并进原有字面量致字典键缺失（机械比对实锤）；拆回两个 `Tr` 调用，缺失 = 0。`Ui.Tr` 缺失告警**去掉 debugLog 门控**（无条件每键一次 LogWarning），键漂移不再静默。
- **2.5.31**：探针证实**状态纯白而文字仍灰** → 定性为**无阴影衬底的白字在半透明浅底上对比度天然低一档**；标题 / Clear / × / 阵营三钮 / Crew / 无匹配条目 / `ItemHelp` **全部收编 `LabelShadowed`**（按钮只当点击区），面板内 100% 文字走同一原语。
- **2.5.32**：用户澄清"灰"从头到尾指的是**左缘开关按钮本身**（2.5.31 的描边已生效）→ 按钮加**纯黑 72% 底板**（`Fill(black, 0.72f)`，与 SC 底部提示条同款配方），按钮本体只当点击区、文字走 `LabelShadowed`、描边保留。
**2.5.20-2.5.26（2026-09-25，用户第 40-42 轮：灰字三层根因终审 + 与 SC 配置统一）**：
- **2.5.20**：`uiPanelAlpha` 与 SC 统一 **0.5**（用户拍板）；**删除默认值迁移链**——build.ps1 部署即删 cfg，两边代码默认值必须一字不差，迁移链只会把对齐值改写掉（分叉元凶）。
- **2.5.21（灰字第一层：色彩空间）**：游戏跑 **Linear 色彩空间**，IMGUI 顶点色与 `SetPixels` 按线性解释 → 深灰被提亮近 3 倍（#282828 显示 ~110）。修复：`Er2Ui.Col()`（sRGB→linear）应用于 Fill/MakeLabel；`Solid()/LeatherTex()` 改 `linear:false` 构造 + `SetPixels32` 写原始字节。纯黑/纯白两端点免疫——这就是 SC 信息面板"看着正常"的原因。
- **2.5.22-2.5.25（灰字第二层：描边与亚像素）**：① LabelShadowed（SC 阴影配方：黑影右下 +1.5px）替换四向黑描边（描边压灰细笔画）；② 字号全部加下限（Title≥13/Body≥11/TabMin≥9）；③ **文字矩形 x/y 整数对齐**——可拖拽 panelPos 浮点 → 文字亚像素模糊，白字掉 50%（像素实证：同帧整数位置 235-255，分数位置 134-152）。
- **2.5.26（灰字第三层：IMGUI 全局状态污染）**：标题/阵营/列表行文字 = 恰好 50% alpha = **宿主/游戏/其他 OnGUI 留下的半透明 GUI.color 跨事件残留渗进绘制**。修复：GenRunner.Draw 总入口强制复位 GUI.color/contentColor/backgroundColor = white + **污染探针**（首帧非白即 LogWarning 点名）；LabelShadowed 正文 `GUI.color = Color.white` 强制（不信任进入态）。**教训：IMGUI 状态跨事件残留，Postfix 链绘制必须入口复位；IMGUI 文字必须整数像素；可拖拽窗口 pos 必须取整。**
- **日志强制令（用户指令，AGENTS.md §3.5）**：新功能/修 bug 必须带日志（入口/分支/结果）+ cfg 日志开关；不确定路径先挂探针；UI 问题附截图像素测量；补丁上线必查 Failed to patch。
**2.5.19（2026-09-25，用户第 39 轮，与 SquadCommand 1.4.38 同批）**：
- 本 mod 侧无独立变化；纠正（信息面板底板恢复、提示条去背景）在指挥官 mod。
- 验证：编译 0 error；部署 DLL 124,928 B，sha256 `7E6A73A1996FBA0FAB8D6E9B1201063163FD8B00FA8F8D74CBAACE1BC79AB92F`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.19.zip`。
  反编译复核：版本双写 2.5.19 ✓。

**2.5.18（2026-09-25，用户第 38 轮，与 SquadCommand 1.4.37 同批）**：
- 本 mod 侧无独立变化（全项目扫描确认 UniGen 所有文字色源已是纯白）；最后两处灰字修复在指挥官 mod 的旁路样式。
- 验证：编译 0 error；部署 DLL 124,928 B，sha256 `068EB8B33C9B34AA4047CFDACA386C03EE6324FDC626C91ED0BE8E8639AECC79`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.18.zip`。
  反编译复核：版本双写 2.5.18 ✓。

**2.5.17（2026-09-25，用户第 37 轮，与 SquadCommand 1.4.36 同批）**：
- 本 mod 侧无独立变化；文字色统一与信息栏/提示条互斥在共享工具层与指挥官 mod（UniGen 面板本就走 `Er2Ui` 令牌）。
- 验证：编译 0 error；部署 DLL 124,928 B，sha256 `A95C1CE6AFE9A95EC4B0BB3913593BD85C73FDB235BEF67BC56743F16F33CAC9`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.17.zip`。
  反编译复核：版本双写 2.5.17 ✓。

**2.5.16（2026-09-25，用户第 36 轮反馈，与 SquadCommand 1.4.35 同批）**：
- **悬停反馈保住**：共享层文字全白后，悬停"变暗"改用新增的专用 `TextHover` 令牌（否则 `hov ? TextDim : Text` 两色相同、反馈失效）；UG 三处列表行全部换用。
- `uiPanelAlpha` 默认 0.72 → **0.50**（范围 0.40~1.0）+ 迁移链补 0.72。
- 验证：编译 0 error；部署 DLL 124,928 B，sha256 `CD7C2B5E43FC52A50DB7B02AB037AACC10BE706508F2E53CC359E1C8DCA3F32B`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.16.zip`。
  反编译复核：版本双写 2.5.16 ✓；`TextHover` 7 处（令牌 + 3 处调用）✓；`0.50f` 默认 + 迁移 ✓。

**2.5.15（2026-09-25，用户第 35 轮反馈，与 SquadCommand 1.4.34 同批）**：
- **删掉悬停提示框**；页签与列表条目的悬停反馈恢复为**文字变暗**（`TextDim`，逐行判鼠标所在）。
- 验证：编译 0 error；部署 DLL 124,928 B，sha256 `DB886F4FE291CEF9B8705ADF0CB57729FF1F2FF3939006A73645CAC8C7BCB35B`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.15.zip`。
  反编译复核：版本双写 2.5.15 ✓；`DrawHoverTip` 0 处 ✓；三参 `GUIContent` 0 处 ✓；列表悬停变暗 3 处 ✓。

**2.5.14（2026-09-25，用户第 34 轮反馈，与 SquadCommand 1.4.33 同批）**：
- **面板不透明度迁移**：`uiPanelAlpha` 若仍是旧默认 0.85（＝玩家从未自定义）→ 更新为 0.72（与底部提示条一致）；**玩家改过的不动**。
- **文字对比加强**：`LabelOutlined` 描边 2 方向 → **4 方向**（共享层）+ 列表行文字 `FontBody+4`（16px）。
- 验证：编译 0 error；部署 DLL 125,440 B，sha256 `4B815479CFCBDC7E1B9193B69C37B834D11B65685EB7741E16F30307F47DAA51`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.14.zip`。
  反编译复核：版本双写 2.5.14 ✓；`Er2Ui.FontBody + 4` 在位 ✓；0.85→0.72 迁移判断在位 ✓。

**2.5.13（2026-09-25，用户第 33 轮："鼠标放在通用生成选项上会有提示，现在没了"，与 SquadCommand 1.4.32 同批）**：
- **悬停提示恢复（陷阱 107）**：1.4.30 为了让文字能描边，把页签/条目/文件夹行的控件内容由字符串换成 `GUIContent.none` → **tooltip 通道被一起清掉**。修法：
  · `Er2Ui.TabGrid`（共享层）与三处列表行改传 `new GUIContent(string.Empty, null, label)`（文本空、tooltip 保留）；面板标题也带上说明。
  · 新增 `DrawHoverTip()`：在 `Draw()` 帧末读一次 `GUI.tooltip` 自绘提示框（`Scrim` 底 + 皮革 + `PanelBorder` 描边，自动收进屏幕边界）。**必须帧末读**，否则会被后续控件覆盖。
  · **⚠️ IL2CPP 裁剪了 `GUIContent(string, string)` 两参构造**（编译报 CS7036，提示需要 `GUIContent(string, Texture, string)`）→ 必须写三参 `(text, null, tooltip)`。
- **EN 表**：补 `通用生成：拖到单位身上放入背包，拖到地上则生成实体`，复核两个 mod 0 缺失。
- 验证：编译 0 error；部署 DLL 124,928 B，sha256 `65BFF661AE08318C649556FCBEBB5D746F9E1D8971B99E59FE25A37036A6BD2D`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.13.zip`。
  反编译复核：版本双写 2.5.13 ✓；`DrawHoverTip` 2 处（定义 + 调用）✓；`GUIContent` 三参构造在位 ✓。

**2.5.12（2026-09-25，用户第 32 轮：UI 风格统一，与 SquadCommand 1.4.31 同批）**：
- **与指挥官 mod 共用手同一套面板风格**：随共享令牌（近黑 `#101010` 系 + 白描边 + 皮革 + 受光边）。UniGen 侧本就用 `Er2Ui.PanelBase`，本轮把指挥官侧补齐（cfg 色值 / HUD 按钮描边 / InfoPanel 与背包底板），两边至此为同一套视觉。
- 玩法无变化。UG 无独立 UI 代码改动（线宽/配色/皮革全部走共享层）。
- 验证：编译 0 error；部署 DLL 124,416 B，sha256 `DDD65A0DE5C020CC50C8233D68B7E8393467D52024A3F78E6B6AC6FF8E6EC9C9`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.12.zip`。

**2.5.11（2026-09-25，用户第 31 轮反馈，与 SquadCommand 1.4.30 同批）**：
- **文字改描边双绘（陷阱 105）**：三处列表行（单位 / 物品 / 收藏文件夹）由「Button 带文字」改为「`GUI.Button(r, GUIContent.none, rowStyle)` 画底 + `Er2Ui.LabelOutlined(...)` 单独画字」；新增 `rowTextStyle`（`FontBody+3`、`MiddleLeft`）。共享层 `Er2Ui.TabGrid` 同步改为空按钮 + 描边文字。
- **面板配色对齐 HUD 提示条**：随共享令牌去蓝调（`#101010` 系），`PanelAlpha` 默认 0.72。
- 验证：编译 0 error；部署 DLL 124,416 B，sha256 `30F21190032A1388DE2D3675A7A56793F093A79119C7B60B03D99ECB2AD23A57`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.11.zip`。
  反编译复核：版本双写 2.5.11 ✓；`GUIContent.none` 4 处 ✓；`rowTextStyle` 在位 + `Er2Ui.LabelOutlined` 3 处 ✓。

**2.5.10（2026-09-25，用户第 30 轮反馈，与 SquadCommand 1.4.29 同批）**：
- **行分隔线改为"每行都画"（用户："每个选项下不都有一个小横线吗，收藏里怎么没了"）**：原实现 `if (i + 1 < to) HLine(...)` 只在**非最后一行**画底线 → **只有一条目的列表一条线都没有**，视觉上等同"这个列表没有分隔线"；且 `DrawFavFolderList` **完全没画线**（多套列表各写一遍，漏了一处，陷阱 78）。修法：① 单位/物品列表去掉 `i + 1 < to` 条件，**每行都画**（反编译复核该条件 0 处残留）；② 文件夹列表补同款横线（`Er2Ui.HLine` 现共 4 处）。
- **列表文字加粗 + 字号 +1**：`rowStyle = MakeButton(FontBody + 1, SurfaceRow, Text, FontStyle.Bold, MiddleLeft)`——解决"文本太暗"的正解（见陷阱 103）。
- **星标加大**：`starSize 14 → 17`、加 `Bold`；`StarOn #FFD800 → #FFE81A`。
- **底色再提亮一档**（随共享令牌）。
- 验证：编译 0 error；部署 DLL 123,392 B，sha256 `F370F678365C5F6C1F01EF0F7EE62BD601C62A908F0F56D09B1FF3CF501ADEA7`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.10.zip`。
  反编译复核：版本双写 2.5.10 ✓；`i + 1 < to` **0 处** ✓；`Er2Ui.HLine` 4 处 ✓；`MakeLabel(max(10, round(17*Scale)), MiddleCenter, 纯白, Bold)` ✓；`MakeButton(FontBody + 1, ..., Bold, MiddleLeft)` ✓。

**2.5.9（2026-09-25，用户第 29 轮反馈，与 SquadCommand 1.4.28 同批）**：
- **收藏改为主文件夹 + 子文件夹两级（本轮主功能）**：打开【收藏】→ 先列**每个分类一个文件夹**（`▸ 名称 (数量)`）；数量由 `FavCountOf()` 现算（单位走 `GenCatalog.favorites` 按 `Category` 计数、物品走 `ItemCatalog.Favs(bucket).Count`）；点文件夹 → 进该分类条目列表，顶部插一行面包屑 `◀ 收藏 / 分类名`（点 ◀ 返回主文件夹）。配套：
  · **移除 `RowKind.FavTabs` 页签行** + `FavTabsPerRow` 常量 + `DrawFavTabs()` 方法（反编译复核三者均 **0 处**）；
  · `RebuildFavTabs()` **不再自动把 `favCat` 设为 `favCats[0]`**——`favCat == ""` 现在表示"停在主文件夹"；仅在原 `favCat` 已不存在时复位为 `""`；
  · `DrawRows` 的 List 分支先判 `category == "favorites" && string.IsNullOrEmpty(favCat)` → 走 `DrawFavFolderList()`，否则才走原单位/物品列表。
- **列表斑马纹**：单位列表 / 物品列表 / 收藏文件夹列表三处，相邻行交替 `RowBg` / `RowBgAlt`（新增令牌）——小字号下仅靠 1px 分隔线很难跟读。
- **帮助行 wordWrap + 行高 56（两行）**——根治横向溢出。
- **EN 表**：补 `◀ 收藏` 与 `（还没有收藏：点条目右侧的 ☆ 添加）`，复核 0 缺失。
- 验证：编译 0 error；部署 DLL 123,392 B，sha256 `19F54152CC0CA8A9251E8556C6499B6E896D36BAD2BB520B112F36648D3D0ABD`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.9.zip`。
  反编译复核：版本双写 2.5.9 ✓；`DrawFavFolderList`/`DrawFavCrumb`/`FavCountOf` 在位 ✓；`FavTabsPerRow`/`RowKind.FavTabs`/`DrawFavTabs` 均 0 处 ✓；`Er2Ui.RowBgAlt` 3 处 ✓；`helpStyle.wordWrap` + `56f * Er2Ui.Scale` ✓。

**2.5.8（2026-09-25，用户第 28 轮反馈，与 SquadCommand 1.4.27 同批）**：
- **前景纯白 + 透明只作用于背景**：随共享令牌改（文字纯白、描边/分隔线白色且不透明）。
- **收藏星星提亮**：`starStyle` 的 `textColor` 由 `TextDim` 改**纯白**——`GUI.contentColor` 与 `GUIStyle.normal.textColor` 是**相乘**关系，原来把金色乘成了 `#CAA84D`；`StarOn` 同时提亮为 `#FFD800`。
- **帮助行溢出修复（真因是横向）**：新增专用 `helpStyle` 字段 + 绘制时 `Er2Ui.FitSize(helpStyle, txt, rect.width, FontBody, FontSmall)` 按可用宽度自缩字号；英文文案 99 → 66 字符；底部留白 `26f*Scale → 30f*Scale`。
- 验证：编译 0 error；部署 DLL 122,368 B，sha256 `D4A6C05AF11DD4228681F45847882E48B62B85421101ED9F0296BC558357E9C7`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.8.zip`。
  反编译复核：版本双写 2.5.8 ✓；`helpStyle` 字段 + `FitSize` 调用在位 ✓；底部留白 `30f * Er2Ui.Scale` ✓。

**2.5.7（2026-09-25，用户第 27 轮，与 SquadCommand 1.4.26 同批）**：
- **标记线宽随共享层改用固定世界米**——与同样固定世界尺寸的标记半径同尺度，近大远小、比例恒定。
- 验证：编译 0 error；部署 DLL 122,368 B，sha256 `5245A86B8812E467880D60E5FA4F2387B9965C149D194F970CA27C0714272E4D`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.7.zip`。
  反编译复核：版本双写 2.5.7 ✓；`LineWidth` 纯世界米（共享源码同步）✓。

**2.5.6（2026-09-25，用户第 26 轮反馈，与 SquadCommand 1.4.25 同批）**：
- **底部呼吸余量**：`PanelRect` 高度 `16f*Scale` → **`26f*Scale`**（顶部 8 + 底部 18）——修"最下面的字都超出菜单了"（原末行距底边仅 8px，数学上没溢出但视觉上贴边）；`ItemHelp` 行高 34 → **38f*Scale**。
- **面板描边加粗**：`Frame(r, PanelBorder, Max(1f, s))` → **`Max(1.5f, 2f*s)`**（约 2px）——半透明面板在亮背景上边界被吃掉，粗一点亮一点的边框才能界定面板。
- **对比度**：随共享令牌提升（选中底 `#52525E`、行底更沉、行分隔线 0.15、列表底 α 0.50）。
- **标记线宽回退**到 1.4.23 数值（共享层）。
- 验证：编译 0 error；部署 DLL 122,368 B，sha256 `AB482BCC69828CA2A06E45BC252B7A7BB0D650CD01EC9A3BED463DFD6D31ADBD`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.6.zip`。
  反编译复核：版本双写 2.5.6 ✓；`26f * Er2Ui.Scale`（底部留白）✓；`38f * Er2Ui.Scale`（ItemHelp）✓；`Max(1.5f, 2f * scale)` 面板描边 ✓。

**2.5.5（2026-09-25，用户第 25 轮反馈，与 SquadCommand 1.4.24 同批）**：
- **面板改中性黑**：随共享令牌去色相（`#08080A` 系，R=G=B）；皮革纹理去暖调。
- **新增 `UI/uiPanelAlpha`**（默认 0.85 / 0.55~1.0）——与宿主同名同义，各 Bind 各的 cfg、共用 `Er2Ui.PanelAlpha`。
- **标记线宽随共享层再 +60%**（UG 无独立线宽调用点）。
- 验证：编译 0 error；部署 DLL 122,368 B，sha256 `7DD008DF306B761A25CE5757A94CAA56584D03F9F0A828A976E6374C18C2DDF0`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.5.zip`。
  反编译复核：版本双写 2.5.5 ✓；`uiPanelAlpha` 5 处 ✓；`PanelAlpha`/`SetPanelAlpha` 在位（共享源码同步）✓。

**2.5.4（2026-09-25，用户第 24 轮反馈，与 SquadCommand 1.4.23 同批）**：
- **近黑皮革面板**：面板底改 `Er2Ui.PanelBase(r)`（近黑填充 + 64×64 皮革噪声 0.10 + 顶部受光边），标题条额外叠 0.14 皮革；配色随共享令牌压到 `#0C0906@0.92` 系。
- **标题行高 26 → 34f * Scale**：修"菜单最上面的按钮重叠"（Clear / × 越过分隔线）。
- **标记线宽随共享层上调 40%**（UG 无独立线宽调用点，共享源码同步编译）。
- 验证：编译 0 error；部署 DLL 121,344 B，sha256 `3414B396BD319EF04ED191E491C1B7049EA1A4B2FB9390DAB354B36895020118`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.4.zip`。
  反编译复核：版本双写 2.5.4 ✓；`LeatherTex`/`Leather`/`PanelBase` 在位 ✓；`34f * Er2Ui.Scale`（Title 行）✓。

**2.5.3（2026-09-25，用户第 23 轮视觉反馈，与 SquadCommand 1.4.22 同批）**：
- **黑棕半透明 UI**：随共享令牌一并换暖色 + 半透明（`#1E1813@0.82` / `#2A2119@0.86` / `#32271E@0.84` / `#584331@0.96`）。
- **元素区分**：页签（`Er2Ui.TabGrid` 内描边，未选暖棕 / 选中暖白）、阵营三按钮、乘员按钮全部加描边；两个列表行间加 `EdgeSoft` 分隔线。
- **文字与背景重叠修复**：`DrawTitleRow` / `DrawFactionRow` / `DrawCrewRow` / `DrawPreviewRow` / 两个列表体 / 空列表提示里大量硬编码像素（`100f`/`90f`/`24f`/`220f`/`230f`/`20f`/`8f`/`6f`/`38f`/`30f`）全部 × `Er2Ui.Scale`；`BuildRows` 去掉 `SubTabs`/`List` 行内嵌的 `Gap`，行间距统一在 `DrawRows` 与面板高度累加里各加一次（同一规则）；面板留白 `16f * Er2Ui.Scale`。
- **EN 表补全**：补 6 条（`uiMono` 描述 + 5 条历史欠债），复核 0 缺失。
- 验证：编译 0 error；部署 DLL 120,320 B，sha256 `66E8858120A451BD1A02B1709669AF4719B87D51C7D3CAB056D6126772D903EF`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.3.zip`。
  反编译复核：版本双写 2.5.3 ✓；共享层 `FeatherTex`/`CameraGroundDist`（UG 同步编译）✓；行间 Gap 2 处 ✓；`Er2Ui.EdgeSoft` 分隔线 2 处 ✓；`90f * Scale`（乘员按钮）✓。

**2.5.2（2026-09-25，用户第 22 轮视觉反馈，与 SquadCommand 1.4.21 同批）**：
- **面板补结构线（"只有单一色块没设计感"）**：共享层新增 `Frame`/`HLine`/`AccentBar` + `Edge`/`EdgeSoft`；`GenPanel.Draw()` 改为：铺 `PanelBg` → 画**标题条独立底色** → 标题条下 `HLine` 分隔 → 画行 → 最后 `Frame` 外框；两个列表（单位/物品）加内凹边框；收藏行加左竖条 `AccentBar`。层次从"只有面"变"面 + 线 + 条"。
- **配色压回中深灰（"颜色浅了"）**：随共享令牌一并压回（面板 `#14181D` / 标题条 `#1F252C` / 控件 `#262D35` / 悬停 `#333B45` / 选中 `#46505C`），与宿主完全同一套。
- **修掉漏乘倍率的固定行高**：`BuildRows` 的 Title 26 / Faction 36 / Pager 26 / ItemHelp 34 / Crew 32 / Preview 24 与 List 内 8px 内缩此前是常量，自适应下不跟随 → 全部 × `Er2Ui.Scale`。
- 验证：编译 0 error；部署 DLL 118,272 B，sha256 `B157D4F27084B7E1C183EE7390B8B136DE49FBAC0359DA3F83630E7FF97BC85D`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.2.zip`。
  反编译复核：版本双写 2.5.2 ✓；`Frame/HLine/AccentBar` 成员在位 ✓；`Er2Ui.Frame/HLine/AccentBar` 调用点 6 处 ✓；`LineWidth` 像素模型在位（共享源码同步）✓。

**2.5.1（2026-09-25，用户第 21 轮：UI 自适应 + 提亮，与 SquadCommand 1.4.20 同批）**：
- **自适应**：`GenPanel` 的 `PanelW/RowH/TabH` 由 `const` → 随 `Er2Ui.Scale` 的属性（原本 `const float PanelW = Er2Ui.PanelW` 编译期钉死）；字母行尺寸收成单一定义（`LetterBtnW/LetterGap/LetterRowH/LetterBtnH`）；`flash/star` 字号 × Scale；`BadgeRect` 走 `ScreenFit(430)`、`ToggleButtonRect` × Scale。
- **入口挂在总入口**：`GenRunner.Draw()` 开头 `AutoScale()`——"携带"与"放置"两支不经过 `GenPanel.Draw()`，只放那里会一直用旧倍率。
- **定宽收敛**：`ItemDragger.BadgeRect` 由硬编码 560px → `ScreenFit(560)`（窄屏不再溢出）。
- **配色提亮 + 残留绿色清零**：携带徽标底（旧军绿 → `Scrim`）、拖拽目标环（亮绿 → `WSelected`）、生成反馈文字（淡绿 → `Er2Ui.Text`，错误 → `Er2Ui.Danger`）、`flashStyle` 字色并入共享令牌。
- **交付**：`build.ps1 -Mod UniversalGeneration`（EN）0 error；部署 DLL 117,248 B，sha256 `17BACD4266FE5868CD4D4826AC570F4A94AC0E65B0B93500EFEFBA80CD0876F4`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.1.zip`。
  反编译复核：版本双写 2.5.1 ✓；`SetScale/ScaleChangedSince/NativeResMult/AutoScale/ScreenFit` 在位 ✓；`ResourcesManager.ResolutionMult` 1 处 ✓；`0.45f,1f,0.5f`（旧亮绿）0 处 ✓；`0.85f,1f,0.85f`（旧淡绿）0 处 ✓；军绿字面量仅剩 `LegacyPanelBg` ✓。

### 2.12.0 UniversalGeneration v2.5.0（历史）
**2.5.0（2026-09-24，用户第 20 轮：UI 重绘一期，与 SquadCommand 1.4.19 同批）**：
- **面板换灰黑单色**：全部结构色经 `Shared/Er2Ui.cs` 的 Mono 预设（`PanelBg #0C0C0C@0.94`/`Surface #1E1E1E@0.90`/`SurfaceActive #3A3A3A`/`RowBg #121212`/`Text #E8E8E8`/`TextDim #A0A0A0`…），靠明度区分层次。
- **新增 `UI/uiMono` cfg**（默认 true）；false 回退旧军绿预设（`Er2Ui.SetMono`，两 mod 同名同义）。
- **零业务逻辑改动**——只换令牌与开关，行计划/页签自适应/物品枚举等 2.4.x 行为逐字不变。
- **交付**：`build.ps1 -Mod UniversalGeneration`（EN）0 error；部署 DLL 115,200 B，sha256 `50623EFA6F6BC9D6F40D4E044756B571E35E4674A7A13B83B2D5D9D0E787AF96`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.5.0.zip`。
  反编译复核：版本双写 2.5.0 ✓；`Er2Ui.*` 成员（Fill/TabGrid/TabGridH/Pager/MakeLabel/MakeButton + 全部配色令牌）✓；`Er2Ui.SetMono` 2 处（SettingChanged + 启动）✓。

### 2.12.1 UniversalGeneration v2.4.2（历史）

**2.4.2（2026-09-24，用户第 19 轮："它运行的不错，优化一下代码，总结经验。为之后指挥官mod和通用生成mod的ui重绘做准备，现在太丑了"）**：
- **① 布局单一数据源（结构性消灭陷阱 75 复发）**：2.4.0 修的是"高度公式漏算动态行"，但**修法是镜像**——`PanelRect()` 里重新算一遍 y 累加，绘制里再算一遍，两边任何一处改动都会再次脱节。
  **改法**：先建**行计划**再画——`enum RowKind { Title, Faction, UnitTabs, ItemTabs, FavTabs, SubTabs, Letters, List, Pager, Crew, Preview, ItemHelp }` + `struct Row { RowKind Kind; float H; }` + `static List<Row> rows`；`BuildRows()` 按当前状态排这一帧要画哪些行；`PanelRect()` = `8 + Σrows[i].H + 8`；`DrawRows(r)` 遍历同一份 `rows`，每行只拿自己的 Rect，**不再自行累加 y**。**加/删行的动作收敛为三步**：加枚举 → `BuildRows` 里排一行 → 写一个 `DrawXxxRow`，高度自动正确。
  分页上下文改为字段 `curPages/curTotal`（`List` 行写、`Pager` 行读，行计划里两者必定相邻），不再靠全局变量隐式传参。
- **② 共享 UI 工具层（重绘的地基）**：新增 `Shared/Er2Ui.cs`，两个 csproj 以 `<Compile Include="..\Shared\Er2Ui.cs" Link="Shared\Er2Ui.cs" />` **源码级链接**（两程序集互不引用，只能源码共享；各自 internal，互不影响）。内容：① 设计令牌（`Pad/Gap/RowH/TabH/BtnH/PanelW`；`FontTitle/Body/Small/TabMax/TabMin`；配色）② 按**颜色**建字典的纯色贴图缓存（修掉单槽缓存换掉别的样式的隐患 17g3，`hideFlags=(HideFlags)61` 防陷阱 12）③ 样式工厂 `MakeLabel/MakeButton`（显式 `normal.textColor`——默认黑）④ 控件原语 `Fill/TabGrid/TabGridH/Pager` ⑤ `FitSize`（`CalcSize` 精确测量 + 按 `(宽|max|min|文本)` 缓存，正数=放得下/负数=放不下）。
  `GenPanel` 删除 `MakeSolidButton/SolidTexture/ItemListHeight/DrawItemList/DrawButton/TabFontSize/DrawTabButton` 七个本地实现，全部上移；`EnsureStyles()` 全走 `Er2Ui` 工厂；类别名改静态数组 `UnitCats/UnitCatRaw/UnitCatNames`（避免每帧翻译与分配）。
- **③ 页签适配改为精确测量**：2.4.1 的字符系数估宽（CJK 1.0em / 拉丁 0.56em）不准 → 改用 `GUIStyle.CalcSize` 实测（设字号→测量→`finally` 还原），结果缓存（每帧每控件调 `CalcSize` 太贵且有 `GUIContent` 分配；>4000 条清空）。
- **交付**：`build.ps1 -Mod UniversalGeneration`（EN）0 error；部署 DLL 110,592 B，sha256 `F6C37E8260F222B621AFF7A2E629BFDDF5679544D565DB62183920E9463A431A`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.4.2.zip`。
  反编译复核：`[BepInPlugin]`+启动日志双 2.4.2 ✓；`Er2Ui.*` 成员（Fill/TabGrid/TabGridH/Pager/MakeLabel/MakeButton + 全部配色令牌）✓；`BuildRows`/`DrawRows` ✓；全仓 `new GUIStyle(` 无参外调用 0 处（陷阱 5 无违规）✓。
- **④ UI 重绘方案**：新增 `ER2_UI_redesign.md`——设计系统（配色 19 令牌 hex + 对比度验算 / 排版 4 档 / 间距 4-8-12-16 / 缩放）、组件规范、两个 mod 的信息架构重组、三期落地路线、IMGUI 做不到项的替代做法。
- **本轮教训（guide 陷阱 78/79、AGENTS 17g14/17g15 已记）**：① "高度与绘制对齐"这种**靠纪律维持的一致性**迟早复发，必须改成**单一数据源**；② 两个不互相引用的程序集要统一观感，**共享源码 + 源码级链接**是唯一可行路径（不能用共享程序集，addon 与宿主只按反射联动）。

**2.4.1（2026-09-24，用户第 18 轮：同轮授权提交 git；报"这个标签页有重叠，显示不完整。同时是空的"）**：
- **① 页签重叠/显示不完整（截图：`Mod Vehicles` 选中态文字压出按钮框）** → **根因 = 定宽页签 + 固定字号**：类别页签 4 列网格每格 ≈72px（`PanelW=320` → `w=300` → `(300-12)/4`），字号固定 12；英文 "Mod Vehicles"（12 拉丁字符）≈78~84px 超宽——`GUI.Button` 文字 MiddleCenter 且**不按矩形裁剪** → 溢出压到相邻页签。中文 "Mod载具" 放得下 → **只在英文版暴露**。物品页签 "Medical/Food" 同病。
  **修法**：新增 `DrawTabButton`/`TabFontSize`——按字符估宽（CJK≈1.0em，拉丁≈0.56em）取塞得进 `(w-6)` 的最大字号（12→8 下限）；应用到单位页签/物品页签/收藏子页签/物品子分类行四处。⚠️ 用**专用样式实例** `tabStyle/tabActiveStyle` 逐次改写 `fontSize`（不能复用共享样式；不能 `new GUIStyle(style)` 拷贝——陷阱 5）。
- **② Mod载具页签恒空** → **根因 = 探测挂在 `ModsLoader.mods_installed` + 终态**：该静态字典**进战斗后才填充**；主菜单探测等 30s 拿到 mod=0（日志第 126 行）→ `ready=true`（第 128 行"目录就绪: mod=0 载具候选=0"）→ 看门狗见 ready 直接 return，永不重试；而 mod 内容进战斗后（物品库 949→2107）才可用 → 页签整个会话空着。工坊实测 51 个 mod 里 7+ 个注册载具，内容存在。
  **修法**：`ModCatalog` 重写——① 目录**磁盘直扫**：`<游戏根>/../..` 推出 `<Steam库>/steamapps/workshop/content/<appid>/*/index.xml`（appid 优先 `steam_appid.txt`，兜底 1324780；解析 `libraryfolders.vdf` 其他库）+ `<游戏>/Mods|mods|LocalMods`，运行时 `mods_installed` 只兜底；② 解析与**校验解耦**——候选缓存复用（`seenIndex`/`srcs`/`gotItems`/`gotVeh`），校验延后到 `ItemCatalog.Ready`，没就绪本轮跳过 15s 再来（**不写终态**）；③ 空闲 60s 重扫（新订阅 mod 不重启也会出现），每轮载具校验上限 10 个（单个最坏 8s），失败 5 次放弃该候选并打日志。
- **交付**：`build.ps1 -Mod UniversalGeneration`（EN），0 error；部署 DLL 107,520 B，sha256 `8AE3A54157400677DBFE23212394BE1CBEDF12DFCF774084611427A025824CEA`，与构建产物逐字节一致。
  **反编译复核**：`[BepInPlugin]`+启动日志双 2.4.1 ✓；`TabFontSize`/`DrawTabButton`（5 处调用）✓；`ModRoots`/`OtherLibraries`/`steam_appid.txt`/`libraryfolders.vdf`/`ItemCatalog.Ready` 门控 ✓。
- **本轮教训（guide 陷阱 76/77、AGENTS 17g12/17g13 已记）**：① 定宽容器 + 可变长文本，字号必须绘制前定案、按最长语言验证；② "扫完了"≠"扫到了东西"，扫完为空不是终态；依赖运行时状态的数据源先在主菜单验证。

**2.4.0（2026-09-24，用户第 17 轮：仍有中文/列表选项跑到菜单外；同轮宿主 1.4.17 修武器隔空拾取）**：
- **① 面板列表溢出（用户第 17 轮第 2 条）** → **根因 = `PanelRect()` 固定高度没算动态行**：OnGUI 的 y 逐段累加——物品页签族 `irows*(TabH+4)`（6 桶 2 行 = 56px）、收藏子页签 `frows*(TabH+4)`、物品子分类行（28px）、字母索引行（3 行 = 68px）、物品帮助行（34px）——**全都不在固定公式里**；物品页实际 ~630px vs 面板背景 ~504px，下半段列表/分页/帮助画到背景外（截图实证）。单位页几乎不暴露（只差 4px 间隙）。
  **修法**：高度**按内容实算**——`PanelRect` 镜像 OnGUI 每一段累加；物品列表高度 `ItemListHeight(bucket, favOnly)` 用与绘制**同一判据**（`ItemCatalog.SubsOf`/`LettersOf`）计算，不重复猜。每帧多两次线性扫描，成本可忽略（绘制本身每帧还在 `Query`）。
- **② 物品名仍中文（用户第 17 轮第 1 条，截图：81-1突击步枪/85式微声冲锋枪等）** → 双层结论：
  - **结构修正**：取名链 `io.name`（GameObject 内部名，如 "arisaka t38carbine"）→ 改为**优先 `ItemObject.GetMappedResourcesName()`**（映射 `MappedResources.prefs` → `PropData.name` 登记名，游戏 UI 的 4 处原生调用方同款，反编译证实 `[CallerCount(4)]`），取不到再退 `io.name` → 原版物品显示登记名。
  - **数据结论（非 bug，向用户复述）**：截图里的中文名条目是**第三方工坊武器 mod**（81式/85式是现代中式枪械，非原版内容）——mod 作者把物品登记成中文名，任何游戏语言下都显示中文；这是 mod 数据不是本 mod UI 文案，本 mod 不改写。README/Nexus 已写明。
- **交付**：`build.ps1 -Mod UniversalGeneration`（EN），0 error；部署 DLL 104,448 B，sha256 `703D067136E9A63FE273B31E86A321009F11C8BCB33ED82FBBE8A203829FDA48`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.4.0.zip`（20:23 重打包含新 README/Nexus）。
  **反编译复核**：`[BepInPlugin]`+启动日志双 2.4.0 ✓；`GenPanel` 含 `ItemListHeight` 与按内容分支 ✓；`ItemCatalog` 含 `GetMappedResourcesName()` 调用 ✓。
- **本轮教训（guide 陷阱 74/75、AGENTS 17g10/17g11 已记）**：① 原生交互无距离校验 → 见 §2.11 1.4.17；② IMGUI 固定高度必须与绘制逐项镜像。

**2.3.0（2026-09-24，用户第 16 轮：仍有中文/物品 3D 模型不显示/预放置不能滚轮）**：**三连修（前两条同轮改宿主 1.4.16）**。
- **① 物品 3D 幽灵预览不显示（2.2.0 起）** → **根因 = `TrackGhost` 与落位顺序写反**：`SpawnItemGhostCR` 里先 `TrackGhost(inst, pos)` 再 `inst.transform.position = pos + up*0.25`——而克隆体 `Instantiate` 时在 **prefab 模板原始坐标**（通常世界原点附近），于是记录的偏移 = 模板坐标 − 锚点（巨大），**下一帧 `MovePreviewTo` 每帧按错误偏移把幽灵挪走** → 永远不在镜头里。单位/载具预览没踩中：它们生成即在锚点，偏移天然 ≈0。
  **修法**：先落位、再 `RegisterGhost`+`TrackGhost`（偏移 = (0,0.25,0)）；命名统一 `UniGenPreview_` 前缀（与宿主 `IsGhostTransform` 射线豁免一致）；失败路径（GetItemObject null / 实例化异常 / Ghostify false）**无条件 `LogWarning`**（对齐 Placer 1.0.11 规则）；`ItemDragger` 加 `!GhostAvailable` 一次性警告（`warnedNoGhost`）。
- **② 放置/携带中滚轮与中键被冻结** → 根因在宿主：全屏 `externalGuiBlock`（防投放点击误触框选）同时命中 `UiPointerCapture()` → `HandleHeight` 滚轮、`HandleDrag` 中键全被冻结。**修法 = 宿主 1.4.16 新增 `externalCameraPass` 契约**（见 §2.11），UniGen `HostLink.Init` 反射注册 `CameraPassThrough()` = `Placer.Placing || ItemDragger.Carrying`——放置/拖放中相机照常（滚轮缩放/中键旋转），点击手势照吞；宿主旧版无字段则静默回退旧行为。
- **③ 英文版「可穿戴」子页签漏中文** → `GenPanel.SubLabel` 返回中文原串，**调用点没包 `Ui.Tr()`**（与 2.2.2 的"字典漂移"不同的另一种漏翻：词条在字典里有，但调用根本没查表）。修法：`Ui.Tr(SubLabel(subs[i]))`。
  **教训补全**：EN 漏中文有两种形态——**字典键漂移**（查了表但键对不上）与**调用点漏包 `Ui.Tr`**（根本没查）。自检日志只抓前者；后者要靠扫描所有用户可见串的包装情况（本轮全量扫过：`BucketLabel`/`favCatNames`/`catNames` 调用点均已包，仅 `SubLabel` 漏）。
- **"生成物中的中文"结论（非 bug）**：物品/小队/载具**名称**来自游戏数据库与各 mod 自己的 `index.xml` `DisplayName`（如 PLA 服装 mod 的中文名）——是数据不是 mod UI 文案，跟随游戏语言设置与 mod 作者用词，无法由本 mod 翻译；README/Nexus 已写明。
- **交付**：`build.ps1 -Mod UniversalGeneration`（EN），0 error；部署 DLL 103,936 B，sha256 `CFD5049DA870DB8467E1DF947AE143BFF87FF7505EF83A2DCB8678CCF6892C6E`，与构建产物逐字节一致；`ER2_UniversalGeneration_v2.3.0.zip`（重打包以纳入更新后的 README/Nexus）。
  **反编译复核**：版本双写 2.3.0 ✓；`SpawnItemGhostCR` 确为 name → position → `RegisterGhost` → `TrackGhost` 顺序 ✓；`HostLink` 含 `fiCameraPass`/`CameraPassThrough` ✓；`ItemDragger.warnedNoGhost` ✓；`GenPanel` 子分类按钮确为 `Ui.Tr(SubLabel(...))` ✓。
- **环境坑（新增）**：本机 PowerShell 执行策略为 **Restricted**——PowerShell 工具里 `& build.ps1` **静默被拒**（exit 0、无输出、无效果），必须 `powershell.exe -NoProfile -ExecutionPolicy Bypass -File …`。已记入 AGENTS.md。

**2.2.2（2026-09-24，用户第 15 轮：「英文版为什么还有中文」）**：**修英文版残留中文**。
- **先证伪"部署了中文版"**：反编译已部署 DLL 的 `Ui` —— 字典在位（`步枪→Rifles` 等）、`Tr` 走查表 → **确实是 EN 构建**。
- **真因 = 字典键漂移**：`Ui` 的 EN 字典键必须与代码里的中文串**逐字一致**，不一致就静默回退中文（陷阱 17）。
  两条配置说明（`panelKey` / `noAttackNeutral`）的措辞后来改过，**字典没跟着改** → 查表落空 →
  Mod 配置界面显示中文。另「物品生成失败：」压根没词条。
- **修法**：键改为与代码逐字一致；补「物品生成失败：」→"Item spawn failed: "；
  **新增缺失自检**——`Ui.Tr` 查不到时（开 debugLog）打一条 `[UniGen] 英文词条缺失（回退中文）: <串>`，
  每条只打一次，以后再漂移立刻暴露（同类：脚本化比对 `Ui.Tr("…")` 全量串 vs 字典键，本次用一次性脚本扫出 3 条）。
- **不属 mod 文案、无法翻译的部分**：物品/小队/载具**名称**来自游戏数据库（`ItemObject.name`、`SquadsArchive`）
  与各 mod 自己的 `index.xml` 的 `DisplayName` → 跟随**游戏语言设置**与 mod 作者用词；
  若游戏设为中文，这些名字就是中文（这是数据不是 UI 文案）。
- **交付**：EN 部署 DLL 103,424 B，sha256 `B235F402FA8D32BBC9A348E13A3C27D1F33B218DF5C483A9F54596C3CEB92A5E`，
  与构建产物逐字节一致；`ER2_UniversalGeneration_v2.2.2.zip`。
  **反编译复核**：版本双写 2.2.2 ✓；部署产物中两条配置说明的字典键**与代码串逐字一致** ✓；
  `物品生成失败：`→`Item spawn failed: ` ✓。

**2.2.1（2026-09-24，用户第 14 轮：「原本正常的物品生成列表也不见了，都不显示」）**：**修 2.2.0 字母行下标越界回归**。
- **根因（截图即诊断）**：2.2.0 把「全部」改为占两格（i==0），但循环写的是 `else { letters[i - 2] }`
  —— **i=1 时 `letters[-1]` 抛 ArgumentOutOfRangeException** → 整帧 OnGUI 中断 → 字母与整个物品列表全消失。
  截图特征：子分类页签 + **孤零零一个「全部」按钮**（画在 i=0，异常发生在 i=1）——
  与 2.1.1 的 TextField 翻车同一模式：**"某段之后全空白" = 那段抛了异常**。
  （讽刺：修复"按钮重叠"时引入了比重叠严重得多的越界——改布局索引必须把"占位格"和"数据下标"分开算。）
- **修法**：`else` → `else if (i >= 2)`；i==1 只是「全部」的第二格占位，跳过。
- **交付**：EN 部署 DLL 102,912 B，sha256 `09EA30C533C75F36123996DFED1671A1D19D5B8DA73DC8D78719D8348EBAC851`，
  与构建产物逐字节一致；`ER2_UniversalGeneration_v2.2.1.zip`。
  **反编译复核**：版本双写 2.2.1 ✓；部署产物中字母行确为 `else if (j >= 2) { text = list2[j - 2]; }` ✓。
- **待实测**：物品列表恢复 + 字母行不再重叠且不越界。

**2.2.0（2026-09-24，用户三条反馈：物品预览改纯模型 / 菜单按钮重叠 / 重启 mod 内容生成）**：
- **① 物品携带预览 = 3D 幽灵模型**（去掉地面光圈 + 光标图标）。`GenRunner.SpawnItemGhost`：
  裸实例化 prefab（纯视觉，不走 ToVirtualItem 生成链）→ 显式冻结刚体（防预览穿地）→
  宿主 `Ghostify` + `RegisterGhost`（与单位/载具同一套幽灵视觉；射线豁免防自遮挡）。
  `ItemDragger` 管 3 态生命周期（悬停地面=跟随显示 / 悬停单位或落点无效=收起 / 失败 1.5s 退避）；
  删除 `DrawGhostMarker`/`DrawCursorIcon`；悬停士兵的绿色目标环保留（背包指向反馈，非落点预览）。
- **② 按钮重叠修复**：字母索引行「全部」按钮原宽 34px > 27px 网格间距 → 压住相邻按钮。
  改为**占两格**（51px），字母从第 3 格起排，整行同一网格。
- **③ 第三方内容支持回归**（1.3.2 按用户决定砍掉，本轮要求重启；新文件 `ModCatalog.cs`）：
  - **发现 = 磁盘解析每个已装 mod 的 `index.xml`**（`ModData/Metadata/{ModName,BundleName}` +
    `Prefabs/RegisteredPrefab/{PrefabName,DisplayName,Type}`；从 `ModData.assetBundleFolder` 向上最多 4 级找）。
    不走运行时数据库的原因：2.0.7 定案 `GetAllItemsOfType<PropData>` 恒空（库按泛型 T 过滤），
    `ItemObject` 上无 mod_id —— **运行时无法区分官方/mod 条目**；index.xml 是游戏自己启动消费的同一份清单。
  - **id = 游戏公开 API `ModsLoader.GenerateModItemId(bundleName, prefabName)`**（裸 prefab 名作第二候选）。
  - **物品**（Type 3/4/5/6/7 = items/weapons/ammo/attachment/uniforms）：`GetItemObject(id) != null` 才入
    「Mod物品」桶（ItemCatalog 新桶 `mod`，BucketOrder 尾部追加）；时间片 3ms/帧。
  - **载具**（Type 2）：`VehicleSpawner.GetVehiclePrefabAsync` 异步验证 prefab 可加载
    （`DelegateSupport.ConvertDelegate` 转托管委托；转换失败退化为不验证直接列出；8s 超时；临时 spawner
    **不能提前销毁**——会杀死内部协程导致回调不来）；验证通过的 id 直接可喂 `vehiclePrefabID` →「Mod载具」页签。
  - **小队**：mod/自定义小队本就在 `SquadsArchive.squads` 全量 key 里（GenCatalog 步兵页 1.2.1 起即枚举）——
    不重复建桶，输出覆盖诊断日志（总数 / 非官方枚举键数）。
  - **UI**：单位页签族 7→8（`modveh`，两行 4 列正好）；物品桶追加 `mod`；收藏分类子页签同步；
    GenCatalog.GetBucket/ResolveFav 接入 ModCatalog.vehicles；Ui 新词条 Mod载具/Mod物品（EN 双语）。
  - 看门狗同款 tick 判活（无放弃路径）；已装 mod 数 / 每 mod 候选数 / 验证通过数全部进日志。
- **交付**：`build.ps1 -Mod UniversalGeneration`（EN），0 error（1 个既有 CS0649）。
  部署 DLL **102,912 B**，sha256 `B9CD69DB9650C606ADE11028D5595DA4A02169B918B7A81A23F328D51478D8D3`，
  与构建产物**逐字节一致**；`ER2_UniversalGeneration_v2.2.0.zip`。
  **反编译复核**：两处版本均 2.2.0 ✓；`SpawnItemGhost` 在位且 `DrawCursorIcon`/`DrawGhostMarker` 已消失 ✓；
  `GenerateModItemId`/`GetVehiclePrefabAsync`/`DelegateSupport` ✓；Ui 英文词条 ✓。
- **待实测**：① 携带物品悬停地面是否显示半透明模型、松手放置正常；② 字母行是否不再重叠；
  ③ 日志 `第三方内容: 已装 mod=N` 各 mod 候选/验证数 —— **用户机器上装了 40 个工坊 mod**，
  首轮日志将定案物品/载具各自能验证出多少条；④ Mod载具能否正常放置带乘员；⑤ mod 小队是否已在步兵页。

### 2.12.1 UniversalGeneration v2.1.1（历史）
RTS 上帝视角内**自定义生成单位/载具**（作弊向）。详见 `ER2_zcode_era.md` §2。
1.0.1 = "生成"开关按钮从左下角移到左缘中段（左下角让位给宿主信息面板）；1.0.2 = 生成的敌/我单位不主动攻击中立（Civilian）阵营（cfg `noAttackNeutral` 可关）。
**1.0.3**：① **中立保护不再拦玩家指令**——玩家手动标记的目标照打（反射读宿主 `GodViewController.CurrentMark` 的 `Spottable` 字段比对，`IsPlayerMarkedTarget`）；② **载具乘员直接生在车上**——`SpawnManager.SpawnAISquadGlobal(..., spawnOnvehicle: veh, ...)`（原生"出生即入座"通道，战役增援同款），取代"车旁落地 + 逐员 boardVehicle"（`BoardCR` 已删，收尾改 `FinishCrewCR`：等 fullySpawned → 超员裁剪 → `AIVehicle.squadInside` 授驾驶资格 → 登记宿主 `rtsSquadSet`）。
**1.0.4**：**放置预览改用幽灵模型**（用户要求，替代绿色圈）——`GenRunner.SpawnPreviewGhost` 真实生成一次 → `HostLink.Ghostify`（反射调宿主 `GhostPreview.Ghostify`）幽灵化 → 跟随光标整体平移（保留班成员队形）；确认时销毁预览再走正式生成；宿主缺失时自动回退自绘圈。载具收尾抽出为 `FinalizeVehicle` 以便复用。
**1.0.5**：① **幽灵预览"不断变大和上升、把镜头顶飞"** → 根因是 `Rigidbody`/`Joint` **不是 Behaviour**，`Ghostify` 原来没停用它们（车体骨架被物理拉扯变形并持续位移）——现在 Rigidbody 全部置 kinematic + 关重力 + `FreezeAll` + `detectCollisions=false`，Joint 关预处理并置零断裂力（`GhostPreview.Ghostify`）；② **删除绿色预览圈**（只留幽灵模型）；③ **左键长按拖动 = 旋转生成物朝向**（`Placer` 0.28s 长按判定，横向拖动 3px=1°，松手不放置；短按仍为放置），生成时 `GenRunner.ApplyYaw` 应用朝向。
**1.0.6**：**左键点列表条目直接放置**（用户反馈"无法拖出来"）——面板点选那一下的左键"松开"会落进放置状态机，被当成一次放置点击。新增 `Placer.ignoreUntilRelease`：进入放置模式后吞掉该次按压直到左键真正松开，之后短按放置、长按拖动旋转朝向才正常生效。
**1.0.7**：① **预览幽灵被敌人打死** → 幽灵是真实生成物，会被 AI 索敌。宿主 `GhostPreview.Ghostify` 增加 `DetachAndPacify`（脱队 + `allowBeingTargeted(false)` + 关 AI），UniGen 侧再兜一次 `allowBeingTargeted(false)`。② **旋转朝向时幽灵跟着鼠标移动**（用户："应该在原地只转向"）→ `Placer.UpdatePreview` 在 `rotating` 期间直接 return（落点冻结），且旋转期间不调用 `MovePreviewTo`，只 `SetPreviewYaw`。
**1.0.8**：① **预览仍被敌人打掉** → `allowBeingTargeted(false)` 挡不住"已锁定的敌人"；宿主新增 `Creature.Damage` Prefix 伤害免疫（按幽灵指针表拒绝伤害），UniGen 侧登记/注销（`HostLink.RegisterGhost/UnregisterGhost`）。② **转朝向时乱转** → `SetPreviewYaw` 只改 `transform.rotation`，而成员位置是"锚点+固定偏移"（班成员弧形散布）→ 看起来在乱转/散开；现在偏移随 yaw 增量一起旋转，整队原地刚性转向。③ **中立仍被双方主动攻击** → 两个根因：阵营判定用精确等值（实际可能是 `Civilian_id` 等变体，全部漏判）→ 改**含 "civil" 即中立**（不区分大小写）；且保护范围原先只限"本 mod 生成物" → 改为**全局生效**（开关打开时所有射手都不主动打中立）。④ 预览对象加 `UniGenPreview_` 前缀（供宿主地面射线豁免）。
**1.0.9**：① **放置预览无法消失** → 预览是**异步生成**的：确认/取消后它才创建完成，就再也没人销毁（孤儿预览）。新增**预览代数**（`previewGeneration`）：`DestroyPreview()` 自增作废在途请求，异步回调完成时校验 `StalePreview(gen)`，过期即自毁（载具直接销毁、步兵整队销毁）。② **中立单位自己主动攻击** → 补上射手侧：`Soldier.GetBestVisibleEnemy` Postfix 增加"中立射手不获取任何目标"（`IsCivilianShooter`，玩家手动标记仍放行），并对生成的中立单位 `allowCheckForEnemies(false)`。
**1.0.10**：**预览生命周期收口（防残留）**。审计出两条泄漏路径：① `exitOnRelease` 分支退出放置时只置标志、**没销毁预览**；② `Cancel` 在"RTS 退出"分支提前 return，标志未清。现在 `Cancel` 无论 `placing` 状态如何都幂等清理（清标志 + `DestroyPreview`），`exitOnRelease` 退出时一并销毁，并新增**看门狗**（放置中宿主 RTS 已退出 → 强制取消），`DestroyPreview` 空表快速返回。
**1.0.11（2026-09-18）**：**预览失败可观测**——幽灵预览创建失败此前只在 `debugLog` 开启时打印（默认关闭）→ "预览不显示/点了没反应"无从定位。现在：失败时 `LogWarning` 无条件输出（含原因指向宿主 `GhostPreview.Ghostify` 返回 false），宿主预览不可用时再给一次性提示。
**1.0.12（2026-09-18）**：随宿主 1.2.17 同步——预览不显示的根因是宿主幽灵化在 1.2.15/1.2.16 失效（Component[]+is 判别不可靠），宿主改回强类型遍历后预览恢复；此前"点列表直接生成"实为预览不可见的观感问题。
**1.0.13（2026-09-19）**：**预览不显示真因** → `SpawnPreviewGhostCR` 开头调用 `DestroyPreview()`，而它会把 `previewGeneration` 自增——本协程的 `gen` 是调用时捕获的（更小）→ 刚生成即被判"过期"销毁 = 预览永远不出现。已移除该调用（旧预览清理由 `Placer.Begin` 在捕获 gen 之前负责）。
**1.0.14（2026-09-19，用户实测修复：大班型直接生成/幽灵碰撞）**：**步兵预览改为"边生成边幽灵化"** → 根因：`SpawnAISquadGlobal` 的回调触发时，大班型（>2 人）的成员可能**还没落齐**——`GetMemberClamped` 只能拿到已落地的成员，未赶上幽灵化轮次的成员就是**真人士兵**（带碰撞、会自行走动）= 用户反馈的"超过两个人的小队直接生成""幽灵与已有单位碰撞"。修法：预览协程每 0.25s 补一轮幽灵化（按指针去重），直到 `fullySpawned` 且成员数两轮一致且全部幽灵化才交给 `onReady`；取消/过期时整队（含已幽灵化成员）销毁。
**1.1.0（2026-09-19，玩家需求：火力点 + 自定义小队）**：① **火力点（机枪）类目**——游戏 `Assets/Prefabs/Vehicles/MGs/` 有 **32 个固定机枪 prefab**（MG34/MG42/勃朗宁/马克沁/九二式/维克斯/ZB37/Breda37 等的 地面/三脚架/碉堡 形态 + 高射机枪 + M45 四联装防空），此前目录正则只认 `Tanks|Wheeled|Planes|Artillery` 把整类漏掉；现加 `mgs` 桶 + 「机枪」页签（7 页签，宽度按数量自适应），走与载具完全相同的 VehicleSpawner 管线，乘员默认按座位数 spawnOnvehicle 入座。② **自定义小队（自动探测路线）**——游戏原生自带 `CustomSquad : SquadData`（`SquadEditorScene` 编辑器的数据模型，成员=`CustomSquadMember`（uniform/vest/headgear/weap1+配件/weap2/otherItems），`ToLoadout()` 虚方法）+ `SquadsArchive.squads`（**静态** `Dictionary<string, SquadDataTable>`）+ `ItemsDatabase.GetSquadLoadouts` 的 **string 重载**——即 SquadData 可用任意 loadout id 数组直接构造，SpawnAISquadGlobal 无需改动即可吃自定义班。现在 `ProbeInfantryTypes` 枚举 `SquadsArchive.squads` 全量 key：不在 SquadType 枚举里的 key（= 游戏注册的玩家自定义班）校验 `CountLoadouts>0` 后进步兵列表（标题取 `squadTypeLabel`），生成/预览走 string 重载同一条管线；`RemoveInfantry(SquadType)` 改 `RemoveInfantryEntry(GenEntry)`（按 Id，兼容自定义 key）；乘员选择池 `crewPool` 只收官方班型（自定义班不作乘员来源）。**待实测**：a) MGs 生成 + 机枪手是否开火（TurretGun 座位与 AIVehicle 行为）；b) SquadsArchive 里是否真有自定义班 key（开 debugLog 看 `[custom]` dump——若游戏自建班不在此处，下一步走 `new SquadData(name, loadoutIds)` 组合方案，Kit 池取各班型 loadouts 并集）。

**1.1.1（2026-09-19，用户实测反馈二轮）**：① **战役生成的敌方单位站桩在出生点** → 根因：`ApplyControlled` 的"受控参数"（`followCustomSquadOrders()`+`followCustomDirectCommands()`+`allowMovements(true)`）把单位锁进**听令模式**——只有被下令才移动；我方有 RTS 指挥所以正常，**敌方没人下令 = 永远站在出生点**。且前两个方法是**无参启用式**（Lua API 风格，无 false 重载），不能事后"释放"。修法：`side`（面板所选 mine/enemy/neutral）从 GenPanel→Placer→GenRunner 全程贯通，**敌方默认不套受控**（新生单位默认即原生 AI：随战役任务推进/进攻），我方/中立保持驻守；cfg `General.enemyNativeAI`（默认 true）可关回。**注意：受控参数对载具乘员班从来就没套过**（`FinishCrewCR` 无 ApplyControlled），敌方载具乘员本来就是原生 AI——本轮只影响步兵。② **自定义班列表为空定案**：反编译+落盘实证——**自定义班内嵌在任务编辑器战斗文件里**（`.mer2` = BinaryFormatter，内含 `customSquads: CustomSquad[]` + 成员 `CustomSquadMember`），随战斗加载后挂在各出生点 `SpawnManager.custom_squad` 实例属性上（`SpawnManager.activeSpawns` 静态可枚举）；**战役里根本没有自定义班**（gamedata.er2 只有 settings/statistics/progresses），战役里列表为空是**预期行为**。修法：探测时枚举 `SpawnManager.activeSpawns` 收集 `custom_squad != null` 的班（按 squad_id 去重，标题取 `GetSquadName()`），**直接持有 SquadData 引用生成**（`GenEntry.DirectSquadData`，不经 GetSquadLoadouts）；探测日志改为「官方 X + 库自定义 A + 场上自定义班 B」。**待实测**：敌方原生 AI 是否推进；任务编辑器/自定义战斗里建自定义班后能否在场上捕获并生成（debugLog 看 `[custom@field]`）。

**1.2.0（2026-09-19，用户实测反馈三轮：菜单太密 + 第三方类目）**：① **页签两行 4 列**（`TabsPerRow=4`，8 页签：收藏/步兵/机枪/坦克/轮式/飞机/火炮/第三方）——此前 7 个塞一行（每格仅 ~39px，"Tanks Wheeled" 挤在一起，用户截图实证）；列表行高 26→28；面板总高 +~50px。② **新增「第三方」类目**（category=`modded`）——**运行时枚举 `ItemsDatabase.GetAllItemsOfType<PropData>(PropType.vehicles)` 取 `mod_id != 0` 的条目**（内容 mod 注册进游戏数据库的载具；`PropData` 有 prefab_name/name/mod_id/deprecated/Category 字段，官方 Category 枚举 = Tank/Wheeled/Plane/StaticMg/StaticGun/Special/AutoTransport/Unlisted）；**此前 manifest 方案只读 CorvoBundles，从来没枚举到创意工坊内容 mod**（README 的 "content mods" 说法是错的，本轮修正文档）。非官方班组也归入第三方桶：SquadsArchive 额外 key（库自定义班）+ 场上 `SpawnManager.custom_squad` 捕获（`GenEntry.DirectSquadData` 直接引用生成）——不再混进步兵列表。官方四桶（tanks/wheeled/planes/artillery/mgs）继续走 manifest 正则（已实证），不受影响。`ResolveFav`/`RemoveInfantryEntry`/`GetBucket` 同步适配 modded 桶。**内容 mod 结构事实**：工坊条目 `workshop/content/1324780/<id>/index.xml`（`<ModData>`+`RegisteredPrefab Type`），`ModsLoader.LoadWorkshopMods/RegisterModObject(ModPropType, prefab_name, item_id, ws_file_id)` 注册，`mods_installed : Dictionary<uint, ModData>`；`ModPropType` 枚举无 squad 类型（内容 mod 不能加班型，第三方班=自定义班）。**待实测**：mod 载具 prefab_name 走 VehicleSpawner 是否可生成；第三方页签布局观感。

**1.2.1（2026-09-19，用户实测反馈四轮：打开面板游戏无响应很久）**：日志定案 = **「库自定义班 466」**——`SquadsArchive.squads` 里有 466 个非枚举 key（游戏完整班型库：季节/战场变体如 win/dday/early 后缀 + 可能的战斗内注册班），1.2.0 的探测对每个 key 调了一次 `GetSquadLoadouts(key,0)`（每次都原生建一整套班数据）→ **466 次原生建班调用在打开面板的主线程上同步跑 = 卡死数秒**。修法：① 探测**只读表元数据**（`SquadDataTable.squadTypeLabel` + `IsUnlockedOnThisBranch()` 过滤未拥有 DLC，均无建班开销），有效性校验推迟到生成时（`GetSquadLoadouts` 返回空 → LogError + 自动移除，路径已有）；② 466 个扩展班型**归位步兵页**（它们是官方班型，1.2.0 误归第三方）——步兵页现在 = 55 官方枚举 + ~466 小队库扩展（分页 ~52 页）；第三方页只留真第三方（场上自定义班 + mod 载具）。`IsCustomSquad` 改名 `SpawnByKey`（语义 = 按字符串 key 生成，不再暗示"自定义"）。**教训：每 key 一次原生"构建型"调用（GetSquadLoadouts 会实例化 SquadData+解析 loadout）× 数百 = 主线程冻结；探测只读元数据，构建推迟到用点。**

**1.2.2（2026-09-19，用户实测反馈五轮：点条目条目消失+没生成 + 仍然卡顿）**：日志双定案——① **「第三方」4 个条目 = 场上捕获的自定义班，但生成全败**：错误行 `GetSquadLoadouts(squad_0x…) 返回空小队` ×4 → **编辑器建的 `CustomSquad` 其 `CountLoadouts()` 返回 0**（且 squad_id 为空退化成指针 id），而生成/预览路径都以 `CountLoadouts()>0` 为前置 → 必败，且失败即 `RemoveInfantryEntry` = 点一个少一个（用户看到的"选择后消失"）。**修法 = 装备转换**：`TryConvertCustomSquad` 逐成员 `FixMember()`+`ToLoadout()` → 以临时 id 注册进 `LoadoutsArchive.loadouts`（静态字典可写）→ `new SquadData(id, ids)` 得到普通 SquadData（CountLoadouts=成员数）→ 存 `GenEntry.DirectSquadData`，生成/预览直接引用，走与官方班型完全相同的管线；members 为 0 的坏班不列 + LogWarning。② **仍然卡顿**：预览失败后 Placer **每帧重试**幽灵创建（本局 636 条"幽灵预览创建失败"刷屏，9MB 日志）+ 探测仍在主线程同步跑。修法：预览失败 1.5s 退避；步兵预览 `ghosted==0` 时销毁刚生成的真实小队（不留隐形真兵）；**探测整体改分帧协程**（`BeginProbe`/`ProbeCR`：枚举同步 55 个 → 小队库 32/帧 → 场上班 → mod 载具 64/帧，CS1626 注意 yield 不能落在带 catch 的 try 里——收集与批处理分离）+ **无条件计时日志**（`耗时 枚举 Xms / 小队库 Yms / 场上班 Zms / mod载具 Wms`，下轮实测直接定位残余卡顿）。**教训：失败重试必须有退避；预览失败路径要清理已生成的真实单位；"探测"类操作一律分帧。**

**1.3.0（2026-09-19，用户实测反馈六轮：仍卡 + 场上班没列出来 + "不行就砍"通牒）**：① **探测整体搬到游戏启动时**（用户点名"就不能在游戏启动的时候完成吗"）——`Plugin.Load`（主菜单）即启动 `BeginStartupProbe` 后台协程：官方 55 枚举（逐项切片）→ 小队库 466 key（只读元数据）→ mod 载具；**每帧时间片 3ms**（`budgetUntil` 模式）；**物品数据库未就绪自动每 2s 重试**（最多 2 分钟，放弃后复位 probeState 由面板打开补跑）；面板打开只做 `EnsureBattleSquads`（场上班捕获，几个原生调用）。② **看门狗**：场景切换会静默杀死探测协程（probeState 永远停在 1）→ 面板打开时若 `state==1 && 已过 45s` 判死收尾。③ **场上班诊断一锤定音**：每次签名变化打一条 `场上班点扫描: 出生点 N，带自定义班 M，成员合计 K`（无条件）——1.2.2 的转换修法（ToLoadout→LoadoutsArchive→SquadData）保留，但 **1.2.2 用户实测"一个班组都没有"且日志被覆盖无实证**；若下轮日志显示 members 全 0（运行时 CustomSquad 是空壳），**按用户指示砍掉自定义班生成功能**（第三方页只留 mod 载具）。**待实测：启动后按 G 应零卡顿；看 `场上班点扫描` 行定生死。**

**1.3.1（2026-09-19，用户实测反馈七轮：仍空列表 → 终审判决）**：1.3.0 诊断日志定案两件事——① **`场上班点扫描: 出生点 4，带自定义班 4，成员合计 0` + `无成员数据（members=0）` ×8**：场上 `SpawnManager.custom_squad` 是**空壳**（成员数据不挂在出生点对象上，游戏生成自己的班走的是出生点内部 `loadout` 字段等私有路径），装备转换无从转换 → **自定义班生成判死，功能已砍**（`TryConvertCustomSquad`/`EnsureBattleSquads`/`DirectSquadData` 全删，第三方页 = mod 载具 only；README 已知限制注明）。② **`启动探测协程被场景切换中断，已接受部分结果（步兵 1 条）`**：注入的协程宿主在「主菜单→加载→战斗」场景切换时被 Unity 连 DontDestroyOnLoad 一起清掉，协程只来得及扫 1 条——旧看门狗"接受部分结果"导致步兵页近乎全空。修法：看门狗移到 `GenDriver.Tick`（每秒，任何场景）+ **判死后自动重启续跑**（`probeRestarts>6` 放弃；重启不清列表，条目去重 guard 幂等，战斗场景内一次跑完）；`ProbeWatchdog` 判死阈值 20s（正常全程 <5s）。**本轮实测预期：启动后步兵页应有 55 官方 + ~466 变体、第三方页 mod 载具（如有）、机枪页 32；面板打开零卡顿。**
**教训（探测三连坑全占）**：探测别放面板打开路径（卡）；"构建型"原生调用逐 key × 数百 = 冻结（1.2.0）；协程跨场景不可靠——注入 MonoBehaviour 即使 DontDestroyOnLoad 也可能被清，长后台任务必须有 Tick 看门狗 + 幂等续跑（1.3.0→1.3.1）。

**1.3.2（2026-09-19，用户决定：不要所有第三方生成）**：**「第三方」页签与 mod 载具枚举整体移除**（`modded` 桶/`GetAllItemsOfType` mod_id 枚举/页签/Ui 词条/README·Nexus 全清）——本 mod 只生成官方内容：步兵 = 55 官方枚举 + ~466 小队库变体，载具 = manifest 四桶（tanks/wheeled/planes/artillery）+ mgs 火力点。页签回到 7 个两行（4+3）。历史脉络：1.2.0 加第三方页（用户要求）→ 1.3.1 砍自定义班（运行时空壳）→ 1.3.2 砍 mod 载具（用户决定）。**mod 内容注册事实留存备查**：工坊条目 `index.xml`（ModData/RegisteredPrefab）→ `ModsLoader.RegisterModObject(ModPropType, prefab_name, item_id, ws_file_id)` → `PropData`（`mod_id != 0` 可识别，Category 官方枚举含 StaticMg/StaticGun）；将来若重启第三方生成，按此路径恢复即可。

**2.1.1（2026-09-19，用户反馈十三轮：「完全没有显示。部署英文版」）**：**去掉 `GUI.TextField`，改纯点击首字母索引；部署英文版**。
- **根因定案（用户截图 + 日志）**：截图里子分类页签（全部/步枪/手枪/可穿戴）**画出来了**，但其下的过滤框与整个物品列表**全空白**；
  日志为 `[Error :ER2 Universal Generation] [UniGen] OnGUI 异常: Method unstripping failed` ×8。
  → **`GUI.TextField` 在本游戏的 IL2CPP 构建里被 Unity 裁剪**（同 `RectOffset` 那个坑）：一调用就抛，
  异常冒泡到面板外层 try/catch，**整帧 OnGUI 中断**，后画的列表自然全无。页签在 TextField 之前绘制，所以幸存。
- **修法**：彻底放弃键盘输入。`ItemCatalog` 删掉 `Matches()`，新增
  `LettersOf(bucket, sub, favOnly)` / `LetterOf(e)` / `HasLetter(e, letter)`；`Query` 的 `filter` 参数改为 `letter`。
  UI 侧画**首字母索引行**：只列当前桶/子分类**实际出现过的字母** + 「全部」，按钮固定 24px 宽、按面板宽度自动换行
  （字母最多 30+ 个，塞单行会窄到不可点）；子分类或桶切换时若字母行消失则自动复位 `letterFilter`，避免空列表。
  删掉 `fieldStyle`（及其底图 Texture2D）与 `GUI.SetNextControlName`。
- **教训**：**IMGUI 里"某控件一画就整块空白"，先怀疑该控件的原生方法被裁剪**，而不是布局/数据问题。
  判据是日志里的 `Method unstripping failed`；排查顺序=看异常发生在哪个绘制段之后。
- **交付**：`build.ps1 -Mod UniversalGeneration`（**英文版，未加 `-Cn`**），0 warning 0 error。
  部署 DLL 93,696 B，sha256 `D292BEF5E55AE1BFFF1A70ED11A3AB00D57E3652C9850ACE221FE438BC32990C`，
  与构建产物**逐字节一致**；`ER2_UniversalGeneration_v2.1.1.zip`。
  **反编译复核**：`[BepInPlugin]`=2.1.1 **且**启动日志=2.1.1 ✓；`GUI.TextField`/`SetNextControlName`/`fieldStyle`
  在产物中**已全部消失** ✓；`letterFilter` 全套逻辑在位 ✓。
- **待实测**：物品列表是否恢复显示；字母索引行点击是否生效；★ 收藏与持久化。

**2.1.0（2026-09-19，用户反馈十二轮：「物品实在太多了，翻起来很麻烦。收藏也应该分类」）**：**物品收藏 + 收藏分类 + 物品子分类 + 关键字过滤**。
- **背景数据（2.0.7 实测日志定案）**：`items=IO:963 weapons=IO:143 ammo=IO:137 attachment=IO:63`（PD 全 0，
  证明 2.0.7 改用 `ItemObject` 是正确修复）；补漏后**合计 2106 条**
  （`weapons=698 ammo=208 throwables=29 gear=542 food=8 misc=1` → 补漏 1486→2106）→ 用户翻找困难。
- **① 物品可收藏**：物品行右侧加 ★（`ItemCatalog.favIds` + `ToggleFav`），持久化进同一配置项 `Plugin.favorites`
  （物品用 `t:<id>` 前缀，单位沿用 `v:`/`i:`）。**写入点合并到 `GenCatalog.SaveFavs` 一处**——
  两个模块各写一次会互相覆盖（`ItemCatalog.FavIdsPrefixed()` 供其拼接）。
- **② 收藏按类别分组**：`RebuildFavTabs()` 生成收藏分类子页签（单位 6 类 + 物品 6 类，**只列有收藏的**），
  选中后分别走单位/物品各自的渲染路径——**刻意不做混合列表**（单位点击=放置、物品点击=拿起，混排易误操作）。
- **③ 物品子分类**：子分类**只用游戏官方字段，不猜名字**（吸取 2.0.2"猜 id 全错"的教训）：
  `Weapon : HandheldItem : ItemObject`（已反编译确认继承链）→ 枚举到的实例可
  `TryCast<Weapon>()`（陷阱 5：IL2CPP 下 C# `as` 恒失败）取 `weaponPose`（`rifle=1/pistol=2`）→ 步枪/手枪；
  再用 `Interagible.IsWerable()` → 可穿戴。`WeaponPose` 只有 rifle/pistol 两档，故粒度到此为止。
- **④ 关键字过滤框**：物品 id 全 ASCII（`mp44`/`kar`），故 `GUI.TextField` 够用、**不依赖中文输入法**；
  id 与显示名都匹配、忽略大小写。切换分类时复位 `itemSub`/`filter`，避免"切过去是空列表"。
- **踩坑**：`new RectOffset(int,int,int,int)` 在 IL2CPP interop 下**被裁剪 → CS1729**，改用 `contentOffset`；
  运行时创建的 Texture2D 按陷阱 12 设 `hideFlags=(HideFlags)61`，且**不复用 `SolidTexture` 的单槽缓存**以免动到别的样式。
- **交付**：`build.ps1 -Mod UniversalGeneration -Cn`，0 error（仅 1 个既有 CS0649）。部署 DLL 88,576 B，
  sha256 `343A8B7CF8DB8246E43E10DC2E3AA500792D95EACFAF374970641D40C46EA0E3`，与构建产物**逐字节一致**；
  `ER2_UniversalGeneration_CN_v2.1.0.zip`。**反编译复核**：两处版本均 2.1.0 ✓；`favCats`/`favCat`/`itemSub` ✓；
  `GUI.SetNextControlName("UniGenFilter")` + `GUI.TextField` ✓；`((Il2CppObjectBase)io).TryCast<Weapon>()` +
  `weaponPose==2→pistol` + `IsWerable()` ✓；`ItemCatalog.ToggleFav`（物品星标）✓。
- **待实测**：物品行 ★ 能否点击收藏并持久化（重开游戏后保留）；收藏页签分类子页签是否只列已收藏分类；
  武器桶是否出现 全部/步枪/手枪 子页签；过滤框能否输入（**若游戏吞键则输入框无反应，需改焦点处理**）。

**2.0.7（2026-09-19，用户实测反馈十一轮续三：「还是没有」）**：**根因终于定案，且不是协程问题 —— 是「就绪闸门条件恒不成立」+「问错了类型」。前四轮修的全是"等闸门的协程活不活"，而闸门本身永远不开。**
- **决定性证据（2.0.6 新增的诊断日志，连续 154 秒、跨 Menu→LoadingScene→Aberdeen 三场景）**：
  `物品库就绪探测: Loaded=true 但 items 枚举 n=0（null=-1）（已等 153.9s, scene=Aberdeen）`
  → `ItemsDatabase.Loaded` **恒为 true**；`GetAllItemsOfType<PropData>(PropType.items)`(=6) **永远返回空数组**。
  同时 L120 `物品目录探测已停止（tick 停在 1），自动重启补齐（第 1 次，已有 0 条）` 证明 **2.0.6 的 tick 看门狗本身工作正常**——
  即：协程活得好、重启也正常，**但闸门永远不放行，枚举代码一次都没跑到**。
- **两处病灶与修法**：
  1. **闸门整体移除**（`ProbeDatabaseReady` 删除 → `SampleCounts` 只观测不返回布尔）。
     此前每版都要等「库已就绪 **且** items 类别非空」才开工，而这个条件**永远不可能成立**。
     现改为**无条件枚举**（`ProbeCR` 直接进 attempt 循环，`SampleCounts` 只写入日志），拿不到就下一轮重试。
     **这消除了"闸门条件猜错"这一整类故障，而不是再猜一个条件。**
  2. **泛型实参问错了类型**：一直调用 `GetAllItemsOfType<PropData>`，而该原生方法极可能**按泛型 T 过滤**。
     数据库里真正存的是 **`ItemObject`**——`GetItemObject(id)` 返回的就是它，带 `item_id`（生成键）与 `icon`
     （2.0.2 已发现"图标非空 = 条目真在库里"）。现 **ItemObject 为主数据源，PropData 兜底**，两边按 id 去重。
- **诊断升级**：`SampleCounts` 打印 **两种类型 × 四个类别** 的真实条数：
  `物品库可枚举(IO=ItemObject, PD=PropData): items=IO:12/PD:0 weapons=IO:80/PD:0 ammo=… attachment=…`
  （`CountItemObjects` / `CountPropDatas`，null=-1，异常=-2）→ **下一次日志会直接告诉我哪一类型有数据**。
- **新增"补漏"重扫**：`Ready` 后 20s 一次性 `EnrichCR`（`nextEnrichAt`），防数据库分批加载导致首轮只拿到一部分；
  条目数有增长才 `GenPanel.RebuildItemTabsPublic()` 重建页签。
- **交付**：`build.ps1 -Mod UniversalGeneration -Cn`，0 error（仅 1 个既有 CS0649 警告）。部署 DLL 82,944 B，
  sha256 `56737DA250DA0B8738A74954E7A278EA2560D6B69FC2EF5B8DC3310419D08F3A`，与构建产物**逐字节一致**；
  `ER2_UniversalGeneration_CN_v2.0.7.zip`（48,383 B）。**反编译复核**：`[BepInPlugin]`=2.0.7 且启动日志=2.0.7 ✓；
  `ProbeDatabaseReady` **已消失** ✓；`ProbeCR` 无阻塞等待循环 ✓；`GetAllItemsOfType<ItemObject>` + `val.item_id` ✓。
- **待实测**：日志应出现 `物品库可枚举(IO=ItemObject, PD=PropData): …` 与 `物品目录就绪(来源=运行时 ItemsDatabase 枚举): …`，
  **面板出现物品页签**。若 IO/PD 全为 0/-1，说明 `GetAllItemsOfType` 在运行时确实取不到东西，需换数据源（如 AssetBundle 资源名 + `GetItemObject` 校验）。

**2.0.6（2026-09-19，用户实测反馈十一轮续二：「改几轮了，现在一个显示物品生成的分类都没有」）**：**根因 = 2.0.5 的看门狗判断逻辑写反了 —— `if (probeState == 1) return;` 让它在协程已死时反而什么都不做。**
- **证据链（靠"缺失的日志"定案）**：L92 `Loading [ER2 Universal Generation 2.0.5]` → 2.0.5 在跑；L97 `物品数据库未就绪，逐帧轮询等待` → 协程启动过；**但此后 `物品库就绪探测: …`（2.0.5 新加的诊断）一条都没打、`物品目录探测被中断` 的重启日志也消失** → **看门狗一次都没动作**。而 L122/L142 证明 GenCatalog 那条链路在同一局里正常完成。
- **我自己的逻辑错误**：2.0.5 为"移除放弃路径"把看门狗写成：
  `if (Ready||Failed) return; if (probeState == 1) return; if (probeState == 0) Begin();`
  —— **但协程被场景切换杀死时，没有任何代码把 `probeState` 复位**（该复位的是已死的协程自己）→ `probeState` **永远卡在 1** → 看门狗永远认为"它在跑"，**一次都不重启**。
  **讽刺之处**：2.0.4 之所以还能重启，靠的正是那个"时间阈值"顺带复位 `probeState`——我把它当 bug 删掉，等于把唯一能发现"协程已死"的机制一起删了。
- **修法（2.0.6，判活判据第四次迭代，这次终于不依赖时间）**：新增 `probeTicks`（协程每帧 `++`，两处：就绪等待循环、`EnumerateAll` 时间片）+ `watchdogLastTicks`（看门狗上次观察值）。
  `ProbeWatchdog()`：`probeState==0` → 启动；`probeState==1` → **比对 tick 计数**，`watchdogLastTicks < 0`（首次，只建基线）或 `probeTicks != watchdogLastTicks` → 活着，更新基线返回；**计数与上次完全相同 → 协程一帧都没跑 → 真死，`probeState=0` + `Begin()` 重启**。
  **为什么这次对**：判据是"**协程自己跑的帧数**"，与时间无关。同时满足两个看似矛盾的需求——① 场景加载期间看门狗自己也没被调用，不会去比对（不误杀合法停摆）；② 看门狗被调用时协程必须也在涨（真死必被发现）。**`Time.timeScale`/场景加载/墙上时钟全都骗不过它。**
- **四次判据迭代的教训（已写进 guide 陷阱 67 + AGENTS 17d-2）**：2.0.3「150s 无进展」→ 2.0.4「20s 心跳停跳」→ 2.0.5「不判死只续跑（等于不检测）」→ 2.0.6「**tick 计数**」。**前三轮共同错误 = 拿"时间流逝"当"协程死亡"的判据**，而场景加载期间主线程被占、协程停摆但时钟照走 → 必然误判。**正解 = 让被检测者自己产出与时间无关的存活信号（帧计数），检测者只在"自己也在跑"的时刻比对。**
- **交付**：`build.ps1 -Mod UniversalGeneration -Cn`，0 error（1 个 CS0649 警告：`Failed` 字段现已只读不写，因放弃路径全删——无害）。部署 DLL 81,920 B，sha256 `93E8FD0494B95D172006A63D`，与构建产物**逐字节一致**；`ER2_UniversalGeneration_CN_v2.0.6.zip`。**反编译复核**：① `[BepInPlugin]`=2.0.6 **且**启动日志=2.0.6；② `ProbeWatchdog` 确含 tick 比对分支（`watchdogLastTicks < 0 || probeTicks != watchdogLastTicks` → 返回；否则重启）✓
- **待实测**：启动后日志应出现 `物品数据库已就绪，开始枚举物品目录（等待 xxxms）` + `物品目录就绪(来源=运行时 ItemsDatabase 枚举): weapons=… ammo=…`，**面板出现 6 个物品页签**。若仍无，日志必有 `物品目录探测已停止（tick 停在 N）` 或 `物品库就绪探测: …` 指明卡点。

**2.0.5（2026-09-19，用户实测反馈十一轮续：**"根本没有对应的选项"**——仍是 7 个页签、零物品页签）**：**根因 = 2.0.4 的看门狗在场景加载期间误判"心跳停跳"→ 重启 → 6 次触顶后 probeState=3 永久放弃。**
- **证据链（日志直接命中）**：第 92 行 `Loading [ER2 Universal Generation 2.0.4]` → **2.0.4 确实在跑**；第 97 行 `物品数据库未就绪，逐帧轮询等待`（**2.0.4 新串 → 逐帧轮询代码已生效**）；第 122 行 `物品目录探测被中断（第 1 次），自动重启补齐（已有 0 条）` ← **心跳看门狗误杀**；第 123 行重启后再次 `物品数据库未就绪，逐帧轮询等待`；第 133 行 `scene=Aberdeen active=True`（**数据库此时必然已就绪**）→ 但**此后再无任何 ItemCatalog 日志**，物品页签一个都没有。
- **误杀机理（关键）**：加载战斗场景期间**主线程被 Unity 占住** → 探测协程拿不到 tick、**心跳无法刷新**；而 `Time.unscaledTime` 是**墙上时钟、照常前进** → 心跳"停跳"是**假象**，不是协程死了。看门狗据此重启，重启次数递增；**6 次触顶后 `probeState=3` 永久放弃**——此后即使进了战斗场景也永不重试。**2.0.3 的 150s 硬阈值与 2.0.4 的 20s 心跳判活，本质是同一个错误：用"时间流逝"当"协程死亡"的判据，而二者都不可靠。**
- **修法（`ItemCatalog.cs`）**：
  ① **彻底移除"自动放弃"**——`probeState` 从 0/1/2/3 四态收缩为 **0/1/2 三态（无放弃路径）**；`ProbeWatchdog()` 改为"**不判死、只续跑**"：`if (Ready||Failed) return; if (probeState==1) return; if (probeState==0) Begin();`，**删除 `probeRestarts>=6` 放弃分支与全部时间阈值**；`Ensure()` 里 `probeState==3` 不再视为终态（复位为 0 重新开始）。
  ② 协程内"枚举 0 条"也不再 `Failed=true`，改为复位 `probeState=0` 交看门狗下一轮重试（`Add` 按 Id 去重保证幂等）。
  ③ **新增 `LogProbeDiag()` 诊断**：`ProbeDatabaseReady()` 的每条失败路径（`Loaded=false` / `Loaded 抛异常` / `枚举 n=0` / `枚举抛异常`）都输出原始值，**每 5s 节流**一条，附已等待秒数 + 当前场景名——前两轮都卡在"闸门永不放行却不知为何"，现在让日志自己说话。
- **⚠️ 三次同型教训（写进 guide 陷阱 67）**：2.0.2 一次跑死 → 2.0.3 判据修好但轮询太慢被杀 → 2.0.4 逐帧轮询却"误判死亡 + 触顶放弃"。**三次都栽在"探测链路里存在一个永久终态"**。结论：**长时后台任务绝不能有"放弃"分支**，重启必须幂等且无限次；"是否卡死"要靠**诊断日志**判断，不能靠猜时间阈值。
- **交付**：`build.ps1 -Mod UniversalGeneration -Cn`（0 warning 0 error）；部署 DLL 81,920 B，sha256 `E15E864D9FB7C3816AD04616`，与构建产物**逐字节一致**；`ER2_UniversalGeneration_CN_v2.0.5.zip`（47,309 B）。**反编译三重复核**：① `[BepInPlugin]`=2.0.5 **且**启动日志=2.0.5；② `ProbeWatchdog` **确无 `probeRestarts>=6` 放弃分支、无任何时间阈值**；③ `ProbeDatabaseReady` 四条失败路径**均带 `LogProbeDiag`** ✓。
- **待实测**：若成功——日志出现 `物品数据库已就绪，开始枚举物品目录（等待 xxxms）` + `物品目录就绪(来源=运行时 ItemsDatabase 枚举)`，面板出现 6 个物品页签；若仍失败——**新增的 `物品库就绪探测: …` 诊断行会直接指出是哪一条判据卡住**（这是本轮最大的排查收益）。

**2.0.4（2026-09-19，用户实测反馈十一轮：**根本不能生成物品**——用户怀疑"部署的中文版没和最新英文版同步"）**：**先证伪用户的假设，再定位真因 = 2.0.3 的轮询节奏太慢，输给了场景切换。**
- **假设证伪（三段硬证据）**：① 部署 DLL sha256 `5013e1cfd0e76e13ebc08b13…` / 80,896 B，与 EN 构建产物**逐字节一致** → **不存在中英不同步**；② 当时（15:37）日志第 92 行 `Loading [ER2 Universal Generation 2.0.3]` + 第 98 行 `2.0.3 loaded. panelKey=G` → 跑的确实是 2.0.3；③ 第 97 行 `物品数据库未就绪，2s 后重试` 是 **2.0.3 新增的日志串**、2.0.2 没有 → **2.0.3 的新代码确实生效并在运行**。结论：不是部署问题，是**2.0.3 的修法本身没修好**。
- **真因（日志时间线定案）**：第 97 行（主菜单）打印"未就绪，2s 后重试"后，第 120/121 行是 `GenCatalog` 的探测**被场景切换中断**并重启，第 122 行 GenCatalog 重启后**几秒内就跑完**（官方枚举 55 + 小队库 466）——**同一时刻、同一宿主，GenCatalog 已成功，ItemCatalog 却毫无动静**。关键：第 136 行 `物品目录探测被中断（第 1 次），自动重启补齐（已有 0 条）` 是 **2.0.3 的 150s 看门狗**触发的（`已有 0 条`），而第 143 行场景已在 `LoadingScene`、第 148 行已到 `Aberdeen active=True`（**物品数据库那时必然已加载完**）——但 ItemCatalog 毫无输出。即：**2.0.3 的 `WaitForSeconds(2f)` 轮询卡在等待里，随场景切换被杀死；看门狗 150s 后才重启，此时 60 次 × 2s 的预算已在主菜单空耗光**（第 148 行场景早已就绪，却再也没有任何 ItemCatalog 日志）。对比 `GenCatalog` 用 `if (enumCount > 0) break;` + 60×2s 能成功，差别在于 **GenCatalog 重启后立刻拿到结果**，而 ItemCatalog 重启后仍要先等 2s 才检查——窗口正好落在场景切换上。
- **修法（`ItemCatalog.cs` 三处）**：① `ProbeCR()` 就绪等待改 **每帧轮询**（`while(!ProbeDatabaseReady()) { ...; yield return null; }`）——成本极低（一次 bool 读 + 一次数组长度检查），数据库一就绪**当帧开始枚举**，等待窗口 < 1 帧，场景切换再无处截断；删除 `WaitForSeconds(2f)`。② **看门狗改心跳判活**：新增 `lastHeartbeat`（在 `Begin()`、每帧轮询、`EnumerateAll` 时间片三处刷新），`ProbeWatchdog()` 判据从"150s 无进展"改为"**心跳停跳 > 20s**"——既不把"数据库确实还没加载"的合法等待误判为死亡，也不放过真死的协程（替代 2.0.3 的 150s 硬阈值）。③ 就绪后最多重试 5 轮（每轮间 `yield null`），新增 `loggedDbReady` 就绪日志；重启时重置 `loggedDbWait`/`loggedDbReady`（2.0.3 重启后全程静默，日志上无从判断协程是否在跑）；**枚举预算改为数据库就绪之后才起算**。
- **交付**：`scripts/build.ps1 -Mod UniversalGeneration -Cn`（0 warning 0 error），DLL 已部署（81,408 B，sha256 `2D9D95A8B1CC6F122F798875…`，与构建产物**逐字节一致**）；`ER2_UniversalGeneration_CN_v2.0.4.zip`（含 README.txt + Nexus_description.md ✓）。**反编译三重复核**：① `[BepInPlugin]` = `2.0.4` **且**启动日志 = `2.0.4`；② `ProbeDatabaseReady` 确含 `ItemsDatabase.Loaded` + `Count > 0` 双判据；③ `ProbeCR` 确为 `while(!ProbeDatabaseReady()) { … yield return null; }` **逐帧轮询**（无 `WaitForSeconds`）+ `for (attempt < 5)` 重试 + `lastHeartbeat` 心跳三处刷新 ✓。
- **⚠️ 本轮踩坑（同类第二次，务必记住）**：**`[BepInPlugin]` 那行的编辑第一次没有落盘**——反编译发现特性是 `2.0.3` 而启动日志是 `2.0.4`（**两处不一致 = 陷阱 14 的经典形态**）。当时直接 `build.ps1` 会产出一个"日志说 2.0.4、BepInEx 实际识别 2.0.3"的坏包。修法：重新 Edit 后**必须回读文件确认落盘**，再构建；**构建后必须反编译复核两处版本**。**别信 Edit 工具的"成功"回执，要信磁盘。**
- **待实测**：启动日志应出现 `物品数据库已就绪，开始枚举物品目录（等待 xxxms）`（等待毫秒数应很小）+ `物品目录就绪(来源=运行时 ItemsDatabase 枚举): weapons=… ammo=… …`（各类目数 > 0，预计合计约 300±）；面板应出现 6 个物品页签；点选物品能否拿起并拖放入包/落地。

**2.0.3（2026-09-19，用户实测反馈十轮：**物品生成整体不可用**）**：**根因 = 2.0.2 的就绪闸门太弱，被"非 null 空数组"骗过，物品目录一次跑死 → 物品页签一个都建不起来。**
- **证据链（三段闭合）**：① 日志第 97 行 `[UniGen] 物品枚举到 0 条——ItemsDatabase.GetAllItemsOfType 可能不可用或数据库未就绪。`，发生在 `Plugin.Load()`（主菜单阶段）；且此后**再无任何 ItemCatalog 行**（第 120 行是 GenCatalog 的），说明协程一次就到 `probeState=3` 死路，**不是"数据库慢"**。② 反编译 `ItemsDatabase` 确认 `GetAllItemsOfType<T>` 签名正确（不是 API 不可用），且存在官方就绪标志 `public unsafe static bool Loaded`（`ItemsDatabase.decompiled.cs:1705`）——2.0.2 完全没用上。③ 与 `GenCatalog`（工作正常）对比：`GenCatalog` 判据是 `if (enumCount > 0) break;`（要求真收到条目）+ 60 次重试；`ItemCatalog` 判据是 `return l != null;`（非 null 即放行）→ 数据库未加载时该方法返回**非 null 空数组**，瞬间放行 → 枚举 0 条 → `Failed=true` + `probeState=3` **永久放弃**。
- **修法（`ItemCatalog.cs` 三处）**：① `ProbeDatabaseReady()` 改为双判据——`ItemsDatabase.Loaded`（权威，try 包裹以便属性不可用时退化）+ 实枚举 `Count > 0`（空数组不算就绪）；② `ProbeCR()` 从"一锤子"改 **attempt 循环**（对齐 `GenCatalog.StartupProbeCR`）：未就绪等 2s 重试、枚举到 0 条也等 2s 重试，上限 60 次（约 2 分钟）才 `Failed`；枚举体抽出为 `EnumerateAll()` 迭代器（时间片跨帧保留，`budgetUntil` 按值传递每轮重置无害）；③ **看门狗阈值 20s → 150s**：2.0.3 的 attempt 循环会**合法等待**最长 2 分钟，20s 会把"正常等待"误判成"协程被场景切换杀死"并反复重启（白白耗尽 `probeRestarts`）；另加 `lastWatchedCount` 进度基线——条目在增长即视为存活。
- **交付**：EN 构建 0 warning 0 error，DLL 已部署（80,896 B，sha256 `5013e1cfd0e76e13ebc08b13…`，与构建产物**逐字节一致**）；`ER2_UniversalGeneration_CN_v2.0.3.zip`（45,050 B，拆包 = DLL + README.txt + Nexus_description.md ✓）。**反编译三重复核**：① `[BepInPlugin]` = `2.0.3` 且启动日志 = `2.0.3`（陷阱 14 两处）；② 部署 DLL 的 `ProbeDatabaseReady` 确含 `ItemsDatabase.Loaded` + `Count > 0`；③ `ProbeCR` 确为 `for (attempt < 60)` + 两条 `WaitForSeconds(2f)` 的重试结构 ✓。文档同步 README/_CN（标题+Changelog）+ 两个 Nexus（Changelog）+ 本台账。
- **本轮排查副产物（避免下次误判）**：同批截图里火炮页签被怀疑"名称渲染空白"，**实为误读**——把 mod 自己的 manifest 正则做本地复现，排序+10 行分页后**与截图 10/10 精确匹配**（`15mm GPF`…`Ger Pak 38`，全 ASCII）；再对截图做像素采样，列表区有 **1704 个纯白 (255,255,255) 文字像素**、按 10 条 y 带分布 → **文字渲染完全正常**，此前是我对 346×531 缩略图的误判（**已向用户更正**）。教训：**判断"UI 文字是否渲染"不要靠缩略图肉眼，要看像素**；另该截图里"无物品页签"本就是 2.0.2 就绪失败的直接后果，与渲染无关。
- **待实测**：启动日志应出现 `物品目录就绪(来源=运行时 ItemsDatabase 枚举): weapons=… ammo=… …` **且各类目数 > 0**（预计合计约 300±，远少于旧 1147）；面板应出现 6 个物品页签；点选物品能否拿起并拖放入包/落地。

**2.0.2（2026-09-19，用户实测反馈九轮：**只有带图标的物品能生成，其余全部无效**）**：**根因 = 物品 id 猜错了来源**。用户截图实证规律"有图标=能生成、无图标=无效"，日志一锤定音（本局失败行全为 `GetItemObject 返回 null`：ArisakaT38/Carcano/Syringe/Thompson_M1928/commonwealth/aus_infantry_uniform_1/box aspirine/DroppableRefillCrate…；成功行全为 bar_1918/bandages/37mm_ns37_ammo/ToolBox）。**画句号的证据**：这些 id 在 `er2items.manifest` 里**确实存在**（`Items/ Carcano.prefab`、`Items/ Syringe.prefab`、`Items/ArisakaT38.prefab` 实盘 grep 可见）——即 2.0.0 的正则解析**没错**，错的是**把"磁盘预制品文件名"当成了"运行时物品数据库的键"**；两者并非一一对应。`ItemObject.icon` 是物品对象自带字段 → **图标非空 ⟺ 该 id 真能查到物品**，于是图标无意中成了"有效"的天然标志物（这正是用户看到的现象）。另一大类：`Uniforms/` 全部 757 条无效（服装是 `Loadout`/`CustomSquadMember.uniform` 字段，**根本不是 ItemObject**）。**2.0.1 的 `BeginValidate` 抽样剔除为何没救回来**：日志里**完全没有 `物品校验完成` 行**，只有 `物品目录就绪`——校验协程在场景切换中被杀且无看门狗续跑（1.3.0/1.3.1 同款坑，本次又踩一次），于是列表原样保留 1147 条，玩家点了才报无效；且"失败即剔除"的设计会让用户**点一个少一个**（1.2.2 已踩过）。**修法 = 停止猜 id，直接向游戏要**：`ItemCatalog` 整体重写，改用运行时 `ItemsDatabase.GetAllItemsOfType<PropData>((PropData.PropType)t)` 枚举 `items=6 / weapons=7 / ammo=8 / attachment=9` 四类，取 `PropData.prefab_name` 作 Id（= 数据库键）、`PropData.name` 作标题，**分类直接用游戏自己的 `PropType`**（不再靠字符串启发式），并过滤 `deprecated` 与 `mod_id != 0`。列表从源头只含真实条目 → "点了才发现无效"结构上不可能再发生；`Uniforms` 桶整体消失（不再有服装页签）。**性能**：保留分帧（每帧 3ms 时间片）+ 数据库未就绪每 2s 重试（上限 2 分钟）+ **看门狗接进 `GenDriver.Tick`**（与 GenCatalog 同款，20s 判死、最多重启 6 次、`Add` 按 Id 去重保证幂等续跑）。**编译踩坑**：`GetAllItemsOfType<T>` 返回的是 `Il2CppArrayBase<T>`（不是 `Il2CppSystem...List<T>`），须用全名 `Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<PropData>`；另 **`ilspycmd -o <mod目录>` 会被 SDK 通配符 glob 进编译** → `CS0101 Plugin 已定义` + `CS0579 BepInPlugin 特性重复`，**反编译产物必须落在项目目录之外**（本次改到 `%TEMP%`）。**交付**：EN 构建 0 warning 0 error；反编译核对 `[BepInPlugin]` 与启动日志**两处版本均 2.0.2**（陷阱 14；本轮首次编辑该行未生效、靠反编译才发现，**必须反编译复核版本**）。**待实测**：物品页条目数（预计 300±，远少于旧 1147）、是否所有条目都能生成、拖放入包与落地是否正常、启动日志 `物品目录就绪(来源=运行时 ItemsDatabase 枚举)` 各类目数量。

**2.0.1（2026-09-19，用户实测反馈八轮：点一下就放置 + 很多物品无效）**：三条修复，全部由实测日志定案（本局 `物品已落地 40 / 物品已入背包 0` —— **从未成功入包**、全是落地，直接指向 ① 与 ③）。
- **① "按下左键选取后直接就放置了"**：`ItemDragger.Tick()` 里 `ignoreUntilRelease` 分支**形同虚设**——命中条件后只把标志置 false 却没有 `return`，同一帧继续往下走到 `if (leftUp) Drop();`。根因是**时间线**：`GenPanel.BeginCarry` 跑在 `OnGUI`，`GenDriver.Tick` 跑在 `Update`，而终止面板点击的那次 `MouseUp` 在**两个时间线里同帧都成立** → 拿起即投放。修法：吞掉该次松手后**立即 return**（本帧到此为止）。
- **② "很多物品提示无效"**（两个独立成因）：
  - **(a) 61 件枪械被错归「其他」桶**：`Classify` 的武器关键字表是**英文/音译名**（kar/mosin/garand…），而游戏用**本国名**（`Gewehr43`/`Mannlicher_95_31`/`Springfield_1903`/`PPSH41`/`MAS_36`/`VZ24`/`MAB38`…）→ 落不进表就掉进 misc。实测分桶 `weapons=92 / misc=62`，玩家在武器页找不到就报"无效"。修法：**武器判定改为兜底默认**（弹药/投掷物/医疗/装备/服装以外一律归武器），misc 只留真正零星物（`DroppableRefillCrate` 等）。修正后实测 **`weapons=153 / misc=1`**。
  - **(b) manifest ≠ 运行时数据库**：`er2items.manifest` 列的是**磁盘上全部预制品**，而 `ItemsDatabase.GetItemObject(id)` 查的是**游戏运行时注册表**，两者并非一一对应（尤其 `Uniforms/` 760 条：服装在游戏里是 `Loadout`/`CustomSquadMember.uniform` 字段，很可能根本不是 ItemObject）。修法：新增 **`ItemCatalog.BeginValidate`** 启动后**分帧抽样核对**（每帧 20 条，约 1 秒跑完 1147 条；同步跑会卡 = 1.2.0 教训），无效条目**直接从列表剔除**，完成后回调 `RebuildItemTabsPublic` 重建页签（含"当前物品页签被清空则退回首个可用页签"）。面板只显示能生成的。
- **③ 拖到单位身上经常失败**：RTS 上帝视角相机离地很远，鼠标指向士兵时射线**常先命中他脚下的地形**，原实现只在"命中的不是士兵"时用 `NearestSoldier(hit.point, 1.6f)` 兜底 → 命中点在地面、1.6m 半径够不到人 → `hoverSoldier` 恒 null → 永远走落地分支（**这正是 40/0 的来源**）。修法：搜索半径**按相机距离反算**（`SearchRadiusFor`：屏高 3% × 距离，FOV 修正，夹在 1.6~6m）。同时 `GroundUnderMouse` 由 `Raycast` 改 **`RaycastAll` + 由近及远遍历**，命中士兵不再直接判否而是**继续往后找真正的地面**（原先想丢在单位旁边很容易先打到人 → "此处无法放置"）。另：`DrawItemList` 改**取快照遍历**（校验协程后台删条目 vs OnGUI 渲染同一 List → 防"集合被修改"异常）。
- **可观测性补齐（工作区教训：失败必须无条件可观测）**：`GiveToInventory` 三条失败分支、`DropAt` 的 `prefab == null` 分支（**「物品无效」的唯一出口，原先完全无日志**）全部加 `LogWarning` 并带原因；`Drop()` 第三分支加 debug 日志。
- **交付**：`ER2_UniversalGeneration_v2.0.1.zip`（44.0 KB）/ `_CN_`（42.8 KB）；DLL 已部署（85.5 KB）；反编译核对 `BepInPlugin` 与启动日志**两处版本均 2.0.1**（陷阱 14）。文档同步 README/_CN（含 Changelog）+ 两个 Nexus（含 Changelog，并把"380+"改为"启动核对后只列可生成的"）。**待实测**：点选是否不再秒放、拖到单位身上能否入包、武器页条目数是否明显变多、启动日志 `物品校验完成` 的剔除数。

**2.0.0（2026-09-19，用户需求：物品生成 + 鼠标拖放）**：新增**物品生成能力**——面板加物品页签，展示游戏全量物品（含真实图标），点选后**鼠标拖动**：拖到单位身上 = 进他的背包，拖到地上 = 生成世界实体。
- **物品目录（`ItemCatalog.cs`，新增）**：走**磁盘 manifest 解析**（与载具同款已验证技术）——`Application.dataPath/StreamingAssets/CorvoBundles/er2items.manifest`，正则 `-\s+Assets/.+Prefabs/(Items|grenades|Uniforms)/(.+?)\.prefab` 抓 id（= prefab 文件名去扩展名）。**实测规模：Items 339 + grenades 48 + Uniforms 760 = 1,147 条**；启动时毫秒级解析，零原生调用、零卡顿（对比 1.2.0 卡顿教训：探测只用只读数据）。分桶（`Classify` 启发式；**2.0.1 修正后实测分布：武器 153 / 弹药 113 / 装备 63 / 投掷物 48 / 医疗食物 8 / 其他 1 / 服装 757**。修正前为 武器 92 / 其他 62 —— 见 2.0.1 段）：`ammo_*`/`_mag`/`_belt`→弹药，`grenade*`/`satchel`/`tnt`/`tankmine`/`at_*`→投掷物，`bandage`/`medkit`/`morphine`/`ration`/`can`→医疗食物，`scope`/`bipod`/`bayonet`/`bag`/`radio`/`helmet`/`gear`/`pouch`/`tripod`→装备，其余按目录（Uniforms→服装、grenades→投掷物）；**2.0.1 起「其余」一律兜底归武器**（原关键字表用英文名，游戏用本国名 → 61 件枪械被错归其他桶）。过滤 `test_` 前缀、大小写去重。页签两组各 4 个两行排（`ItemTabsPerRow=4`），**页签数随库存/收藏变化动态重建**（`RebuildItemTabs`）。
- **进背包（`ItemSpawner.cs`，新增）**：**绝不走 `AddVirtualItem`/`AddItemToInventory`**（两者把物品归一化成基类 `VirtualItem`，弹匣/手雷子类语义全废 = 陷阱 23，ThrowableWheel 实证）——一律**直接 `inventory.items.Add(vi)`**（`Inventory.items` 已反编译确认 = `public unsafe List<VirtualItem>`）。**子类正确性三级兜底**：① `ItemsDatabase.GetItemObject(id).ToVirtualItem()`（**原生自己产子类**，最准）→ ② `VirtualItem.Create(id)` → ③ `new VirtualItem(id)` 基类兜底。放进前校验负重（`GetWeightAndMaxWeight`），放进后**复核 `ContainsId`**（`Add` 静默失败时给准确反馈而不是假成功）。背包定位：`Soldier.inventory` 快路（**注意该字段继承自 `Creature`，不在 `Soldier` 自身**）+ `InventoryManager.activeInventories` 反查归属（与宿主 InfoPanel 同款）。
- **拖到地上（`ItemSpawner.DropAt`）**：`ItemObject.ToVirtualItem()` → `VirtualItem.InstantiatePrefab()`（**坑：`InstantiatePrefab()` 在 `VirtualItem` 上，不在 `ItemObject` 上** —— `ItemObject` 只有 `ToVirtualItem()`，反编译实证 `ItemObject.decompiled.cs:313`；编译期即报 CS1061）→ 兜底 `Object.Instantiate(prefab.gameObject)`。落点 = 地面 + `up*0.25` + 随机偏航。
- **拖放状态机（`ItemDragger.cs`，新增）**：与 `Placer` 完全同款手势契约，避免两套状态机打架——① 拿起时 `GenPanel.SetOpen(false)` + `BlockRect()` 返回**全屏 Rect** 给宿主 `externalGuiBlock` → 拖放期间宿主完全让位，投放点击不会触发原生框选/指令；② **`ignoreUntilRelease`**：面板里点条目那一下左键的"松开"会落进状态机（Placer 1.0.6 同款教训），必须吞到真正松开；③ **修正了一个会致死功能的时序 bug**——松开那一帧 `GetMouseButton(0)` 已为 false，原来的 `if (!leftHeld && !leftUp) return;` 会吞掉唯一一次放置判定，改为 `leftUp` 优先判、`leftHeld` 兜后；④ 右键/ESC/G = 放弃携带（G 键由 `GenDriver` 分发，携带优先于放置）；⑤ **看门狗**：宿主 RTS 退出 → `Cancel("RTS 退出", false)` 强制收口（不重开面板）。投放判定**士兵优先于地面**；两者都不中 → 给明确文案「此处无法放置（对准单位或地面）」且**不收手**（允许继续找落点）。
- **交互反馈**：目标单位脚下**绿色光环**、地面落点**琥珀光环**（世界空间 28 段圆环 → `WorldToScreenPoint` 投影，绕开 `GetWorldCorners` 全零陷阱 14；IMGUI 画线用 `RotateAroundPivot`）；光标处**跟手图标**；底部提示条实时显示"松手 → 放入 X 的背包 / 丢到地上"。目标名读取 0.2s 节流（`Time.unscaledTime`，陷阱 20）。
- **图标（关键复用宿主终案）**：游戏图标是**图集子区域**，直接持有 `Sprite` 会 "Object was garbage collected in IL2CPP domain" → 必须**立刻光栅化成自建 `Texture2D`**（`RenderTexture.GetTemporary` + `Graphics.Blit` + `ReadPixels`）并 `hideFlags=(HideFlags)61`（陷阱 12），再用 `GUI.DrawTexture` 画。本 mod **不在 OnGUI 里做 GPU 光栅化**（面板每帧重绘，避免打断 IMGUI 状态）——改**惰性后台协程**逐个解析、就绪即画；解析三级：`prefab.icon` → `ItemsDatabase.cachedLoadedSprites` → `ItemsDatabase.LoadAndCacheSprite(name,"er2gui")`。
- **构建事实**：`UniversalGeneration.csproj` 里 `UnityEngine.SpriteModule` 是**无效引用**（interop 目录下不存在该 dll，只有 SpriteMask/SpriteShape）——它一直只是 MSB3245 警告，本轮顺手删除。`Sprite` 经 `Assembly-CSharp`/`CoreModule` 传递解析。
- **交付**：`ER2_UniversalGeneration_v2.0.0.zip`（42.5 KB）/ `ER2_UniversalGeneration_CN_v2.0.0.zip`（41.3 KB），双语包内容核对 ✓（EN 包 README 含 "Item spawning"、CN 包含"物品生成"）；反编译核对 EN 构建 `Ui.Tr` 走英文字典且新增物品词条（武器/弹药/投掷物/装备/医疗食物/服装…）**全部有英文值**、CN 构建 `Tr` 直通中文 ✓；DLL 已部署（78 KB）。**待实测**：物品图标解码、拖放入包后弹匣/手雷子类行为、拖到地上的实体能否被拾取。

### 2.13 UnitCollision `er2.morephysics.unitcollision` v1.0.8
单位/尸体碰撞（MorePhysics 删除后的轻量保留版）。**修复原理 = 开碰撞矩阵**：`Physics.IgnoreLayerCollision(1,9,false)` 活体互碰（复用原版受击碰撞体）+ `(1,10,false)` + 尸体骨骼刚体强制动态 = 尸体可推开不挡活人。诊断模式 `UnitCollisionLayer=-2`（只打矩阵日志不修改）。**排查单位碰撞用 `Physics.GetIgnoreLayerCollision(l1,l2)` 直接读，别猜。**
**层事实**：CharacterController 层=1、BodyPart 受击碰撞体层=9（实心）、尸体层=10；游戏原本关闭了 `1↔9` 与 `1↔10` 矩阵。子弹 raycast 用 LayerMask，**与碰撞矩阵无关**（所以"受击正常但物理互穿"）。
**2.1 玩家反馈"还能穿过 AI"调查（2026-09-13，已定案）**：① 轻量版 v1.0.7 在 2.1 **实测无问题**（用户单机实测）。静态+运行时双验证：mod API 签名全部未变；全量反编译确认游戏代码**无任何** `IgnoreLayerCollision`/`IgnoreCollision` 调用点（矩阵纯工程设置，运行时状态与 2.1 前一致）；BodyPart 字段变化全是物品协程编译器字段，碰撞/层级零变化。② **完整版 MorePhysics v0.1.47 在 2.1 也能正常加载**（6 个 Harmony patch 目标全存活：`Corvostudio.Weapons.BulletInstance.OnHit/RaycastAll`、`Corvostudio.Weapons.Explosion.CreateExplosion`、`Soldier.Melee`、`PlayerController.Update`、`TargetPractice.OnHitted`；静态 API `Creature.aliveCreatures`/`ItemObject.spawnedItems`/`RagdollManager.ragdollizedLayer` 全存活；tick 全程 try/catch）——但它的单位碰撞是**旧机制：没有 `ResolveOverlaps`/AI 位置级软推**（那是轻量版 v1.0.1 才补的），AI 走 NavMeshAgent 直写 transform 不受任何阻挡，**AI 穿玩家/互相穿是 v0.1.47 的固有行为**，不是 2.1 回归。③ **玩家反馈最可能 = 用了完整版**。处置：建议玩家改装独立的轻量版 Unit Collision v1.0.6+；另注意 `SingleplayerOnly` 默认开（联机下两个 mod 都静默不生效）。④ v1.0.7 顺带修了 CC 层探测的兜底锁定问题（开局单位未生成时锁进固定层 1 后永不重探 → 现在兜底后保持重探自动改判），并给诊断模式（`-2`）加了活体受击碰撞体状态 dump（`UC: living-dump` 行）。未发 Nexus。
**v1.0.8（2026-09-13 热开关修复，重要）**：玩家实测"Mod Manager 关了两个 mod 还有碰撞"定案 = **碰撞矩阵单向开闸**——矩阵只有 `IgnoreLayerCollision(...,false)` 打开路径、无恢复路径，物理矩阵是进程级状态，本局打开过就一直保留到重启。修复：① `GateBlocked` 从 tick 前置拦截折叠进 `Apply()`（总开关关掉后 Apply 仍进来，恢复逻辑才有机会执行）；② 新增 `_unitMatrixOpen`/`_corpseMatrixOpen` 标记，任一开关（总开关/UnitCollision/PushCorpses）从开→关立即把本 mod 打开过的矩阵对恢复成游戏默认 ignore=true（游戏自身从不改矩阵，恢复安全）；③ 位置级软推按开关细分——活体-活体严格挂 `UnitCollision`（此前 `PushCorpses` 开着就会跑）、AI-尸体严格挂 `PushCorpses`；④ 尸体物理/推尸组件挂载改为仅 `PushCorpses` 开启时执行。
**排错备忘（本次沉淀）**：`ilspycmd -t <短名>` 对带命名空间的 interop 类型（`Corvostudio.Weapons.*`）会报 "Could not find type definition"——**必须用全名**；`ilspycmd -l c` 的类型清单可用来快速判定类型是否存在/挪过命名空间。

### 2.14 UnitInfoOverlay（Unit Inspector）`er2.unitinfooverlay` v1.0.5
开发者调试工具：单位头顶悬浮信息（名称/血量+观察上限+失能阈值/状态标签/姿态/阵营/兵种/坐标/速度/实例 ID）。`PlayerController.Update` Postfix 刷新（0.1s `Time.unscaledTime` 节流）+ `PlayerController.OnGUI` Postfix 绘制；遍历 `Creature.allCreatures`。
**关键教训（v1.0.3 悬浮窗完全消失）**：**未验证的 interop 信号不能直接当门控**——`DeathPanel.instance.gameObject.activeInHierarchy` 战斗中**恒 true**（v1.0.2 消失根因），`LoadingCircle.IsLoading()` 战斗中恒 false。现在门控只用实证信号（`Pause.isPaused` + `timeScale<=0.001` + `ControlledCharacter.IsDead`）+ 确定性加载尾巴（`firstVisibleTime` + `hideAfterLoad` 配置）。**方法学：先加诊断日志实测，再当门控用。**
**边界**：interop 无单位最大血量字段 → 用"观察上限"（`observedMax` 字典按指针跟踪历史最大 HP）；`IsReloading/IsAiming/IsThrowing/IsCrawling/IsOnFire` 是**属性不是方法**；`IsIncapacitated` 无 getter → DOWN 由"存活且 HP < `INCAPACITATED_THREESHOLD`"推断。

### 2.15 FleshWoundsFixed `ER2_FleshWounds` v1.0.1
第三方 Flesh Wounds 的**重建修复版**（Nexus mods/59）。修复 **2026-08 的"单位贴图变紫"恶性 bug**：原版在运行时克隆/销毁士兵材质，销毁时序与游戏冲突导致材质丢失。**诊断紫贴图问题先看它。**
**部署注意**：build.ps1 **无**该条目 → 需手动 `dotnet build` + 部署。部署目录里相关的是 `plugins/ER2_FleshWoundsBW/`（子目录版）——注意 `ER2_FleshWounds_TRCompatBW.dll` 是第三方兼容件，不要混淆。

### 2.16 HvtTestDriver `er2.hvt.testdriver` v1.0.0（内部工具）
进战斗后自动：把离玩家最近的敌方 AI 设为 Lv.V 老兵（5 杀）→ 触发红/金闪烁 → 弹两条底部 toast → 自动截图 6 张到 `research_out/hvt_shots/`。用途：配合外部截图 + 视觉模型验证 HVT 渲染输出。**不发布。**

### 2.17 MorePhysics `er2.morephysics` v0.1.49（2026-09-13 复活）
完整物理化 mod（曾于 2026-08 MorePhysics 19 版迭代后删除）。**复活方式 = 从 `research_out/ER2_MorePhysics.decompiled.cs`（v0.1.47 发布版反编译）重建源码**（源码从未进 git，Downloads 发布包也已清理），随后把 `UnitCollision` 类整体替换为轻量版 v1.0.7 实现（AI 位置级软推 `ResolveOverlaps`/`PushCorpseWhole`、尸体三层合并检测、CC 层兜底重探、`-2` 诊断含 `living-dump`），**保留完整版专属的 `PushItemsNearAI`（AI 推物理化道具）与带 `PushPhysItems` 的 `CorpsePusher`**。2.1 兼容性已静态验证（6 个 Harmony 目标 + 静态 API 全存活，见 §2.13 调查）。**反编译回编译要点（复用）**：剥 `//IL_` 注释；剥 assembly 属性块与 `Microsoft.CodeAnalysis`/`RefSafetyRules` 桩（csproj GenerateAssemblyInfo 会撞车）；`using Il2CppSystem(.Collections.Generic)` 删掉、IL2CPP 集合改全限定名；`((T)(ref x)).ctor(...)` → `x = new T(...)`、`((T)(ref x)).成员` → `x.成员`；`Il2CppArrayBase<T>.op_Implicit(ARG)` → `((T[])ARG)`；旧 API `Type.internal_from_handle(...)` → `Il2CppType.Of<T>()`；csproj 需 `<AllowUnsafeBlocks>true`；补 `using Object = UnityEngine.Object;` 消歧。**配置节名 "Physics" 与键全部沿用 v0.1.47，玩家旧 cfg 兼容**。注意：与轻量版 UnitCollision 功能重叠，**建议只装一个**。
**v0.1.49（2026-09-13）**：同步轻量版 v1.0.8 的热开关修复（`GateBlocked` 折叠进 `Apply` + `_unitMatrixOpen`/`_corpseMatrixOpen` 矩阵恢复 + 软推按开关细分）；`PhysicsTickPatch.Postfix` 重构为**先** `UnitCollision.Apply()` **再** `GateBlocked` 短路（物件物理/场景物理仍归总开关管，不受影响）；`PushItemsNearAI` 从 pushCorpses 块内移到块后（它自带 `PushPhysItems` 检查，不应被尸体开关牵连）。发布包 `ER2_MorePhysics_v0.1.49.zip`（EN，拆包核对 = DLL + README.txt + Nexus_description.md）；**本地 plugins 不部署**（与轻量版重叠，用户要求只部署轻量版）。

### 2.18 Conquest `er2.conquest` v0.2.0（2026-09-13；战略层对齐 GoH 完整化 + M3 战斗桥接待实测）

> **【2026-09-19 项目终止】**：用户决定放弃征服模式开发。原因：战略层（206 省/回合/研究树/四资源）复杂度失控、五轮实测返工主要在解决"看不懂"、最高风险的合成战斗启动链路（M3/M4）始终未实测。**本文档以下内容保留为复盘与侦察档案**；已实测验证的 UI 基建与 Core 组件由无尽模式回收（`ER2_无尽模式_设计方案.md` §1.2）。**同日已卸载游戏内 DLL/cfg 并删除工作区源码**（`Conquest/`、`ConquestRecon/`），源码快照在 `research_out/conquest_salvage/`（回收清单见其 README）。

**把《Gates of Hell: Ostfront》的征服模式搬进 ER2**：持久化军队 + 领土 + 资源 + 回合制推进，战斗只是战略层的一次结算事件。方案见 **`ER2_征服模式_设计方案.md`**（15 节，含全部反编译与实测证据）；**参考拆解见 `ER2_征服模式_参考拆解.md`**（GoH `.pak` 解包实证 + HLL 检索 + 机制映射表）。

**已定架构（用户 2026-09-13 拍板 7 项）**：① FPS/RTS 双视角可切（RTS 复用 SquadCommand，缺失自动降级）② 省份池用**原生战役/战斗** ③ 直接在原生战役/地图上改，**核心是持续性养成系统** ④ 老兵等级归 **HVT**，征服只做兼容对接 ⑤ 单位为**小队/班**（与 SquadCommand 同粒度）⑥ 不做 COOP ⑦ **必须支持自动结算**（用户测试需要）。

**代码结构**：
- `Conquest/Core/`（**纯 C#，不引用 Unity/Il2Cpp**）：`Model` / `ConquestCampaign`（含 `ConquestRules` 全部平衡参数 + `DeterministicRng`）/ `ProvinceGraph`（经纬度 k 近邻邻接 + 全局连通兜底 + `FirstStepTowardEnemy`）/ `BattleSim`（对称攻守自动结算）/ `TurnResolver`（回合心跳 + 玩家进攻调动 + AI + 胜负）/ `ConquestSetup` / `ConquestSave`（行式键值存档）/ `CoreSelfTest`
- `Conquest/Game/`：`VeteranLink`（HVT 反射对接，失败可重试）/ `NativeBattleSource`（原生战斗 → 省份池，三级降级）
- `Conquest/UI/`：`UiTheme`（原生字体+描边+克制配色）/ `StrategyMapView`（战略地图）/ `ConquestRoot`（征服页）/ `MenuEntry`（主菜单入口）
- `Conquest/tools/CoreTest/`：**离线验证宿主**（纯 C#，脱离游戏跑完整战役）

**M0 侦察实测（`research_out/conquest_recon/dump/recon_latest.txt`，195 KB）**：① 主菜单 `MainMenu/buttons/` 竖排按钮，`Campaigns` i=6 y=270、`Multiplayer` i=7 y=222、间距 48，模板 = Button+Text+icon，onClick → `MainMenu.OpenTab` ② `vanilla_campaign_data.Length=17` 个原生战役、**共 206 场战斗**，`LoadBattle()` 抽样 40 次 **0 失败**，`BattleData` 自带**真实经纬度**（如 39.47/−76.14 = 马里兰 Aberdeen）③ **`.mer2` 读取实测通过**：`SaveDataManager.LoadFromFile<MissionEditorBattle>(带扩展名的文件名, out data, 目录, BinaryFormatter)`，读出 4 阶段 / unit_spawns / vehicle_spawns / conquer_areas（**M4 地基已验证**）④ `SquadType` 56 个成员**全部有效**（征兵表零内容制作）⑤ `CampaignsTree` 只在「战役」页打开时存在（不影响，S2 已够用）。

**M1 入口（已实现）**：`UI/MenuEntry.cs` 克隆 `Campaigns` 按钮 → `SetSiblingIndex(7)` → 改文字「征服模式」→ 换 onClick → 图标染金 → Campaigns 上移一个间距腾位（间距从实际 y 差推导，不硬编码）。幂等 = `buttons` 下已有 `Conquest` 子物体则跳过（场景重载自动重注入）；父物体有 LayoutGroup 时跳过手动定位。

**M2 战略层（已完成，离线自检 8/8）**：因 Core 是纯 C#，战略层**可脱离游戏验证**——`Conquest/tools/CoreTest/`：`dotnet run -c Release --project CoreTest.csproj -- 30 48`。检查项：邻接图（对称+连通）/ 初始部队 / 省份归属 / 数值不变量 / **战役有推进** / 自动结算可复现 / 存档往返 / 养成有产出。

**M2-UI + M5 养成闭环（已实现）**：征服页（IMGUI 原生观感）= 新建战役屏（选阵营/战区）+ 战略地图（经纬度投影、势力着色、前线高亮、悬停省份卡）+ 军队管理（列表/补充兵员/征兵）+ 战史。临时入口 **F8**，正式入口是主菜单按钮。

**自检抓出的真实缺陷（这就是它存在的价值）**：① 邻接图 0 边（自检自身顺序错误）；② 多战区时 30/40 省不可达（连通兜底只在战区内做 → 跨战区战役永远打不完，改为全局兜底）；③ **25 回合仅 3 场战斗 0 攻占**——根因是**部队只能靠"打下来"前进**，推进到死胡同就永久卡住（新增部队调动 `TryMoveUnit`/`AdvanceIdleUnits`，BFS 穿己方领土；起始部队改为撒在 4 个省而非 1 个）；④ **5 回合内我方部队 6→0** 之后战略层彻底死水（损失率 0.18/0.55 过高且无补充手段 → 降到 0.10/0.30，自检 bot 加【补充兵员】+【采购】，AI 加兵员回补）。修复后同场景：**20→58 省（共 60）、107 场战斗、43 次攻占、部队 8→27、老兵打到 Lv.5**。

**关键平衡参数**（集中在 `ConquestRules`）：`VetPowerBonus=0.15`（每级老兵 +15% 战力）/ `DefenseMultiplier=1.25` / `WinnerLossRate=0.10`、`LoserLossRate=0.30`（刻意压低——单位是持久资产）/ `MaxAttacksPerTurn=3`、`AiMaxAttacksPerTurn=2` / `AiAdvantageRequired=0.90` / `AiReplenishPerTurn=2` / `KillsPerLevel=5`、`MaxVeterancy=5`（**与 HVT 默认一致**，老兵语义归 HVT，这里只做镜像计算）。

**⚠️ 关键陷阱（本次实测，值得写进工作区通用陷阱）：BepInEx 插件按名字母序加载**。`"ER2 Conquest"` 排在 `"ER2 Veteran HVT"` **之前** → 在 `Load()` 里查 HVT 必然失败；若把"找不到"**永久缓存**（最初写法 `_lookupDone = true`）就**永远接不上**（日志实证："未检测到 Veteran HVT"，下一行 HVT 才刚加载）。**对策：跨 mod 懒对接的否定结果绝不能缓存**——`VeteranLink` 改为失败可重试（节流 1s / 上限 60 次 / 成功即停）+ `OnBecameAvailable` 回调让"晚到的 HVT"接上后重新对齐规则。`Shared/NoHintsHudLink.cs` 没踩到是因为它把查找放在**首次查询**（进战斗后）而非 `Load()`。

**⚠️ 首轮实测暴露的 UI 三宗罪（2026-09-13，全部已修；用户反馈"点击后跳出原生战役界面"+"界面还有许多 bug"）**：
1. **点一下同时开出原生战役页 + 征服页**——克隆 `Campaigns` 按钮时其**原生持久监听器**被一并克隆，而 `RemoveAllListeners()` **清不掉持久监听器**（我的回调确实触发了，日志有 `从主菜单打开征服页`，原生 `OpenTab` 也触发）。修法：`SetPersistentListenerState(i, Off)` 逐个关闭 + `RemoveAllListeners()` + 重新 `AddListener`，**并事后打印持久监听器数量自证**；另清掉 `EventTrigger`。
2. **军队页一打开就报错（294 次/帧）**——`GUI.BeginScrollView(Rect,Vector2,Rect)` 的 3 参重载在本游戏 IL2CPP 构建里**被代码裁剪** → `System.NotSupportedException: Method unstripping failed`。修法：**不用任何滚动/裁剪 API**，自算偏移 + 只绘制可见行 + 自绘滚动条 + `EventType.ScrollWheel`。**推广：IL2CPP 裁剪是隐形地雷，只有实测过的 UnityEngine 方法才能用**；本项目实测可用 = `GUI.DrawTexture`/`GUI.Label`/`GUI.Button(Rect,GUIContent,GUIStyle)`/`GUI.color`/`GUI.matrix`/`GUIUtility.RotateAroundPivot`/`GuiExtension.OutlinedLabel`，禁用 = `GUI.BeginScrollView`。
3. **默认阵营是 Civilian（平民）**——阵营列表取字母序第一个。修法：过滤 `civilian`/`neutral`，默认取正规军 id，默认战区取省份最多者。

**相机运镜（对齐原生"先移相机、再展开界面"）**：`UI/MenuCamera.cs` —— **不调**原生 `SetCameraMenuPos`（副作用未知，可能顺带切 `currentTab`），改为**只读**地在 `MainMenu.cameraPositions` 里按名字找战役页机位（含距离校验防瞬移），找不到退化为轻微推镜；进入记录原机位、退出精确还原。动画在 **`Update`** 推进（`ConquestRoot.Tick()`），**不在 `OnGUI`**（OnGUI 一帧调用多次，会漂移）。背景压暗改**半透明 0.80** 保留 3D 场景可见（原生菜单观感，之前铺满不透明是"不像原生"主因）。

**第二轮反馈（2026-09-13）：UI 重做成原生排版 + 修点击穿透**。用户给原生战役页截图要求"保留这样的排版与 UI，用原生的方式"，并报告"面板弹出来后点击还会点到原生那些按钮"。
- **排版对齐原生战役页**：`左上「◀ 返回」` + `左侧战区列表（选中高亮 + ★星标 + 控制/总数）` + `顶部居中大标题` + `内容区省份卡片网格`（每卡 = 日期 + 双方旗帜 + 省份名，左边框=归属方颜色）+ `右上页签` + `顶部状态条` + `点卡片→右侧滑出省份详情与【出击】`。之前是自绘的全屏面板，完全不像原生。
- **新增 `Game/NativeAssets.cs`（全部走原生 API）**：`Language.GetText/GetBattleName`（本地化，`anzio_battle`→「安齐奥战役」、`bn_training01`→「基础教程」，已核对本地化 XML）、`ResourcesManager.GetFactionData(f).flag`（**原生阵营旗帜** Sprite）、`.names[]`（阵营本地化名）、`ResourcesManager.GetGUISTyle(anchor,size,color,fstyle)`（**游戏自己的 IMGUI 样式**）、`LocalizationManager.GetFont(bool)`（原生字体）。日期排版照原生卡片（`12 五月 1945`，中文构建"五月"/英文"May"）。
- **修点击穿透（`UI/InputBlocker.cs`）**：根因是 **IMGUI 与 uGUI 是两套独立输入系统**——征服页画在原生之上，但原生按钮点击由 `EventSystem`+`GraphicRaycaster` 处理，**它看不见 IMGUI 画了什么** → 点击穿过面板落到原生按钮。解法：`sortingOrder=30000` 的独立 Canvas 放一张全屏 `Image`（`raycastTarget=true`、alpha≈0），成为射线命中的最上层图元 → 原生收不到点击；IMGUI 走自己的通道不受影响。页面打开才激活。
- **顺带修**：旗帜来自**图集**，直接 `DrawTexture(sprite.texture)` 会画出整张图集 → 改用 `sprite.textureRect` 换算 UV 走 `DrawTextureWithTexCoords`；该方法也可能被裁剪，故**首次调用探一次**，失败永久退化为整图（避免每帧每面旗抛异常）。`Province` 新增 `NameId` 字段（原生 `BattleData.location_name_id`）供本地化，已同步存档读写。

**第三轮反馈（2026-09-13）：相机打架 + UI 重叠 → 改用原生页签系统**。用户实测"相机转了一圈又回到原地"+"UI 重叠"（征服页画在主菜单之上，原生左侧菜单与按钮全透出来）。
- **S6 侦察给出决定性数据**：`cameraPositions`（Length=7）与页签**按名字一一对应** —— `CAM_POS_menu`↔MainMenu、`CAM_POS_settings`、**`CAM_POS_campaigns`↔CampaignMenu**、`CAM_POS_credits`/`statistics`/`editor`/`multiplayer`。`camaigns_tab` 组件 = RectTransform+CanvasRenderer+Image+**CampaignsTree**。Canvas 只有 2 个（`Canvas` order=0、`ErrorCanvas` order=1）→ 拦截层 30000 安全。按钮组件真名（须用 `GetIl2CppType().Name`）：Campaigns/Conquest 都带 `SetUiSelectedOnEnable`，Conquest 已插到 [7]。
- **根因**：① 相机——我逐帧写相机去"运镜"，而**原生主菜单自己在驱动相机**，互写导致来回转，最终原生赢；② 重叠——IMGUI 画在主菜单之上但**从未隐藏原生主菜单页签**。
- **修法（新增 `UI/NativeTabHost.cs`，删除 `UI/MenuCamera.cs`）**：**借游戏自己的页签切换**。进入 = `MainMenu.OpenTab(camaigns_tab, true)`（玩家点「战役」的原生路径）→ 原生代码自己移相机、自己隐藏主菜单、自己播过场，**我们一行相机代码都不写**；然后停用 `CampaignsTree` 组件 + 隐藏页签下所有子物体（记录进入前激活的，退出只还原那些），页签**根**保持激活让原生状态自洽（IMGUI 与页签 active 无关，照常绘制）；`Enforce()` 看门狗 0.5s 节流持续压住（原生会自己刷新重显示）；退出 = 还原 + `OpenTab(mainMenu_tab, true)`。
- **教训（可推广）**：**当原生系统已经在驱动某个状态（相机/选中/布局）时，不要自己再写它——找到原生切换入口借它的力**；自己写就会打架，且原生通常赢。这正是用户要的"用原生的方式"。
- **另一踩坑**：`FactionDetails.names` **不是阵营名**，是**士兵名字**数组（界面显示成 "George"/"Hans"）；本地化 XML 里也无阵营名 key → 改为 `Language.GetText(faction)` 优先 + **驼峰分词**兜底（`UnitedStates_allies`→`United States`）。

**第四轮反馈（2026-09-13）：流程重构 + 布局分带 + "弹窗关不掉"根因**。用户："UI 很多重叠。而且这个进攻失利关不掉。同时我甚至看不懂这是在干什么，为什么有原版战役。不应该是选择国家后再研发和配置军队，然后跳出来几个选项然后选择战斗吗？为什么界面这么杂乱？"（产品层返工，非单纯修 bug）
- **流程按用户描述重构**：删掉**战区侧边栏**（17 战区 + 0/15 计数，毫无意义）与**右侧常驻详情面板**；把 206 张省份卡片网格改为**只列当前可进攻的目标**（2-5 张，带胜率 + 每卡自带【出击】）；新建战役改**三步流程**（①选国家 ②选战区 ③开始）+ 顶部流程说明 + 解释"战场取自原生战役，所以会看到安齐奥/斯大林格勒这些熟悉地图"；标题改「**征服 · 安齐奥战役**」避免误认为在玩原版战役。页面骨架 = 战略（可进攻目标 + 我的领土）· 军队（补充/征兵）· 战史。默认战区改为**跳过教学战区**（`aberdeen_training` 只有 5 省，玩起来像死水）。
- **"弹窗关不掉"真正根因（本轮最有价值的发现）**：**IMGUI 里先画的控件先拿到鼠标**（`GUIUtility.hotControl` 被第一个命中的控件抢走），**不是"后画的在上层"**。所以弹窗虽然最后绘制，底下的省份卡片按钮（**先**画的）把点击全吞了，【关闭】永远收不到事件。修法：新增 `UiTheme.InputEnabled` 全局交互开关——弹窗打开时置 false 让下层 `Button`/新增的 `ClickArea` 全部失效，画弹窗前再置回 true；并把所有裸 `GUI.Button` 统一换成 `UiTheme.ClickArea`（已确认 3→0）。弹窗另加多重关闭：大按钮 / 点任意处 / ESC / **20 秒自动关闭**兜底，+0.3s 保护窗防"触发【出击】那一下被误判成关闭"。
- **布局重叠根因 = 缺分带**：之前各元素按"内容区百分比"各自算坐标 → 必然重叠。改为**硬编码横向分带**：`BandTopH=88`（返回 y18..64 + 居中标题 y22..76 + 页签 y26..58 + 分隔线 y82）、`BandStatusH=42`（y88..126 + 分隔线）、`ContentY=138` 起为内容带。带间留安全间隔，结构上不可能重叠。
- 全部文案改走 `Plugin.T()` 双语；本次部署 **CN 构建**（用户游戏为中文，避免"硬编码中文 + T() 英文"混杂）。

**其它踩坑**：`BattleData.map` 是 `Gamemap` 枚举、`month` 是 `MonthName` 枚举、双方阵营是 `Faction` 枚举（**都不是 string**）→ 统一 `ToString()`；月份**按枚举名映射**而非强转 `(int)`（底层值从 0 还是 1 开始不明确）。**`Component.GetType().Name` 在 IL2CPP 下退化成 "Component"**（M0 dump 里全是 `!Component` 就是这个坑）→ 必须用 `GetIl2CppType().Name`。`Conquest/tools/` 必须从 mod csproj 排除（`<Compile Remove="tools\**" />`），否则其 `obj/` 生成文件引发 `CS0579 AssemblyInfo 特性重复`。`MainMenu.fadeAnim` 是 `Animation` 类型 → csproj 需引用 `UnityEngine.AnimationModule`。

#### v0.2.0（2026-09-13）：解包 GoH → 战略层按原版补全 + M3 战斗桥接

**起点 = 真正解包《地狱之门》**（此前只靠二手描述）。关键发现：GoH 的 `resource\*.pak`
**就是标准 ZIP**（文件头 `PK\x03\x04`）→ .NET `ZipFile` 直接读，**WinRAR 在这些 pak 上会卡死**。
战略层数值全在 `gamelogic.pak` 的 `set/dynamic_campaign/`（`values.set` / `resources_*.set` /
`duration_*.set` / `unit_research_*.set` / `map_points.set`），战斗层在 `set/multiplayer/games/`。
解包产物 `research_out/goh_unpack/`（163 文件）。**联网检索**《人间地狱》：本机
`hellletloose.fandom.com` / `reddit.com` **连接超时**、`steamcommunity.com` 被判私有网络目标拒绝抓取
→ 只能取搜索引擎摘要，**已在文档里逐条标注"未核实"**（`ER2_征服模式_参考拆解.md` §2）。

**按 GoH 原版补全的机制**（全部有 `.set` 文件行号实证）：

| 机制 | GoH 依据 | 实现 |
|---|---|---|
| **四种资源 MP/AP/RP/SP** | `resources_standard.set:54-95` | `Manpower/Ammo/Research/Special` + `StepCurve` 阶梯收益 |
| 胜/负收益按场次阶梯 | `WinGain "1:200 4:250 …"` | `StepCurve`（`"场次:值"` 解析 + 存档往返） |
| **地图类型奖励** 5 类 | `MapRewards` | `ProvinceKind` + 攻占发放 + 卡片预览 |
| **风险档** Low/Standard/High | `RiskFactor`（BotVeterancy 0/1/2、Rewards ×1.0/1.25/1.5） | 新建战役第三步选难度 |
| **战损返还 40% / 解散返还 60%** | `PaybackFactor/SellFactor` | `BattleSim` 全灭时返还 + `TryDisband` |
| **研究树**（requires+costs+网格坐标） | `unit_research_*.set` | `Core/ResearchTree.cs` + `ResearchTreeBuilder`（由原生 SquadType 目录**生成**树）+ 研发页 |
| **出兵预算 CP**（含阶梯与上限） | `Budget`/`StageCP`/`GlobalMaxCP` | `UnitTemplate.CpCost` + `DeploymentBudget` + 出击裁剪/拦截 |
| **阶段推进**（5 阶段，按场次解锁） | `StageCP`/`StageUnlock` | `Stage` + `GarrisonCapFor`（阶段抬驻军上限＝GoH 的 DefenseLevel） |
| **AI 研究阶段曲线** | `ResearchStages "n:v"` | `AiResearchStageFor` → AI 部队科技等级随场次成长 |

**离线自检 8/8 → 15/15**（新增 7 项：省份类型分配 / 研究树生成与前置约束 / 经济四资源与战果结算 /
研发与阶段推进 / **研发门槛**（未研发不能征募精锐、研发后可征募）/ **出兵预算**（CP 上限生效 +
随场次增长）/ **解散返还**（= MpCost×0.6））。存档 v2 兼容 v1（`meta.supply` → `meta.ammo` 迁移）。

**自检在本轮又抓出 3 个真缺陷（护栏价值再次验证）**：
① **经济失控**——领土产出 15/8/2 时 30 回合后 MP 涨到 4.7 万，征兵花不完 → 产出降到 **4/3/1**，让"打胜仗"而非"蹲地皮"成为主要收入；
② **战略层变死水（反向）**——驻军 1 点折算 9 战力时，满级驻军省 = 112 战力 ≈ 10 个班，30 回合只打得起 **4 场仗** → 降到 **2/点**；
③ **驻军上限随阶段增长后，自检不变量仍按基础上限校验** → 报"驻军越界"假故障 → 把上限收进唯一来源 `Rules.GarrisonCapFor(stage)`，回合恢复与不变量共用。

**自检 bot 也必须像玩家一样打**（否则掩盖真实平衡）：只从**兵力最多的出发省**出击
（`PickStagingForce`，分散的部队凑不出攻势）+ 胜率 <45% 不打。加这两条前，bot 要么第 2 回合全军覆没，
要么 30 回合只打 4 场。最终：**16→48 省 / 69 场战斗 / 41 次攻占 / 阶段 5 / 科技 T5**。

**M3 战斗桥接（新增，默认关闭）**：`Game/BattleLauncher.cs` + `Game/BattleBridge.cs` + `Game/RosterSpawner.cs`。
- **开打路径**：`CampaignsTree.cachedBattles`（战役页缓存）→ `ICampaignTreeBattleData.LoadBattle()` →
  `CampaignsTree.StartMissionCR(IBattle, Transform, Transform)`（原生协程）。**能拿到 `CampaignsTree`
  实例的原因**：征服页借用原生「战役」页签（`NativeTabHost.Enter`），所以战役页对象是活的、缓存是填好的。
- **投送**：`SpawnManager.SpawnAISquadGlobal` + `DelegateSupport.ConvertDelegate`（照抄 UniGen 范式，陷阱 25）；
  出生点取场景里**我方阵营**的 `SpawnManager.wayPoint`（**是 `Il2CppSystem.Nullable<Vector3>`，不是 Transform**——
  本轮编译期抓到的类型错误），兜底 `BattleManager.instance.GetBorderCenter(currentPhaseNum)`。
- **结算**：轮询 `BattleManager.IsBattleEnded()`（static）+ `instance.GetCurrentWinnerFactionBasedOnCurrentPhase()`
  → `BattleSim.ResolveRealBattle`（新增 `forcedAttackerWin` 重载，与自动结算**同构**，养成输入源一致）。
- **兜底**：10 分钟超时 / 启动失败 → 日志写原因 + **自动回退自动结算**（战役绝不卡死）；
  回主菜单后 `MainMenu.instance != null` 触发自动重开征服页并弹出跨场景保留的战果。
- ⚠️ **`StartMissionCR` 的两个 Transform 参数语义无法从 interop 反编译看出**（原生协程体在 native 侧）
  → 传 null，日志报告实际返回值；因此该路径 **cfg `Battle.RealBattles` 默认 false**，由玩家逐场试。

**顺带补的两块产品缺口**：① **读档列表**（此前重启游戏后战役无法恢复——`ConquestSave.ListSlots` 一直在但 UI 没用）；
② **双语文档**（`README_CN.txt` / `Nexus_description_CN.md`）。**踩坑实录**：第一次打 CN 包时
`README_CN.txt` 还不存在，`build.ps1` 的 `if (Test-Path $localized)` **静默回退到英文**（正是 AGENTS §6 记录的
"对缺失文档静默跳过"）→ 两个 zip 的 README 字节数完全一样才暴露 → 补中文文档后 CN 包内容才正确。

**部署与发布**：EN 构建部署（sha256 源码=部署 `C0C21C4B…`），包 = `ER2_Conquest_v0.2.0.zip`（EN）/
`ER2_Conquest_CN_v0.2.0.zip`（CN，DLL 内容不同 `B3EDB9FE…`），zip 内均含 DLL+README.txt+Nexus_description.md（已拆包核对）。

**开发期注意**：`Diagnostics.CoreSelfTestOnLoad` **开发期默认 true**（游戏内跑一次自检写日志）——**发布前必须改回 false**。

**下一步**：用户实测 M1 入口 + 征服页 → 按反馈修 → M3 战斗桥接（原生战斗开打 + 编成投送 + 战果回写 + FPS/RTS 视角切换）。

### 2.19 ConquestRecon `er2.conquest.recon` v0.1.0（内部侦察工具，不发布）

M0 侦察工具，同 `HvtTestDriver` 定位。**刻意不打任何 Harmony 补丁**——M0 只需要"看"，不 patch 原生方法，避免污染被观察对象。`DontDestroyOnLoad` MonoBehaviour 轮询 `MainMenu.instance`，就绪 3s 后自动全量 dump；**F10** = 全量、**F9** = 只 dump 战役树缓存。输出 `research_out/conquest_recon/dump/recon_latest.txt`。

**五个 dump 段**：S1 主菜单层级（`MainMenu` 全字段 + `mainMenu_tab` 整树递归 + 7 个 tab 子树，每节点带 `Rect`/`Text(内容)`/`Button[onClick 目标.方法]`/自定义组件名 → 定 M1 注入点）· S2 原生战役/战斗全表（`vanilla_campaign_data` 数组 + `VisibleVanillaCampaigns()` 枚举对照，逐战斗 `LoadBattle()` → `BattleData` 全字段 → **省份池**）· S3 `CampaignsTree.cachedBattles`（`List<ValueTuple<BattleData, ICampaignTreeBattleData>>`，最富数据源，需战役页打开）· S4 `SquadType` 全枚举 × `ItemsDatabase.GetSquadLoadouts`（单位表）· S5 `.mer2` 读取 spike（4 种 (fileName, path) 组合逐一尝试 → M4 地基）。每字段独立 try/catch，单点失败不影响其余。

## 3. 部署目录里的第三方 mod（DLL 名 ≠ 插件名）

除原创 mod 外，`plugins/` 里还有一批第三方 mod（**不是本工作区源码，不要改/删**）：

| DLL | 说明 |
|---|---|
| `ER2_FleshWoundsBW/`（子目录）、`ER2_FleshWounds_TRCompatBW.dll` | Flesh Wounds 血迹包（见 §2.15） |
| `ER2_ImpactFXBW/`（子目录） | ImpactFX 命中特效 |
| `ER2_ReactiveRagdollBW.dll` | Reactive Ragdoll |
| `ER2_RemoveStainsBW.dll` | Remove Stains |
| `ER2_RealisticBlood.dll` | Realistic Blood |
| `ER2_RealisticEffects.dll` | Realistic Effects |
| `ER2_BulletPenetration.dll` | Bullet Penetration |
| `ER2_DeathScreenEffect.dll` | Death Screen Effect |
| `ER2_RecoilOverhaul.dll` | **内含 "Universal Recoil Control"**（GUID `UniversalRecoilControl`）——DLL 名 ≠ 插件名典型 |
| `ER2_Coax_MG_Hotkey.dll` | Coax MG Hotkey（用 `UnityEngine.InputSystem.Key` 枚举做快捷键——ModManager 枚举支持就是为它加的） |
| `ER2_MeleeTweaks.dll` | Melee Tweaks |
| `ER2_FPSBodyShadowsFix.dll` | FPS Body Shadows Fix |
| `AdvancedCombatMovement_1.2.2_StablePatch.dll` | **Advanced Combat Movement 1.2.2**（GUID `AdvancedCombatMovement`，DLL 内合并了 Responsive Orders + Slower Vehicles 两套补丁；2026-09-24 装机供兼容实测）。**已知互打点**：① 在第 3 页劫持 `SettingsGUI_V2.SettingsTabRight`（恒 false）→ 与 ModManager 翻页链冲突（1.5.3 已桥接）；② 同上这一跳**连同原生点击音效一起吞掉**（它自己不播）→ 翻页静音（1.5.4 已兜底补音）；③ 吞 `Squad.SetHoldFireOrder(false,false,false,false)` → 与 Battlefield Commander 恢复开火冲突（1.4.15 已兜底）；④ 自带 AI 防守驻留会覆盖我方的移动令（设计层，需在它的设置页关掉 Defensive Hold / Danger Memory）。**另注：它编译时用的是 2026-09-12 之前的 interop**（`SettingsTabLeft` 里写死 1 参 `__instance.UpdateOpenedMenu(true)`，当前游戏是 2 参）→ 它自己的左翻在当前游戏版本上有 MissingMethodException 风险（未实测，属它自己的版本兼容问题，非我方拦截） |
| `ER2_LeaveRedeploy`（第三方） | 曾在死亡时接管"小队选择→部署界面"流程（隔离验证用） |

## 4. 发布包状态（`C:\Users\71011\Downloads\`）

最新一批（2026-09-06）：

| 包 | 时间 |
|---|---|
| `ER2_BattlefieldCommander_v1.4.56.zip`（EN，**当前部署**）/ `ER2_BattlefieldCommander_CN_v1.4.56.zip`（CN） | 09-25 19:56（**发布版：阵型拖动五轮手感收口**——① 像素→米比例改**斜距**（俯视不再系统性偏短）+ `formDragSens` 默认 1（箭头跟手）；② 幽灵覆盖**全部步兵槽位**（掩体槽+阵型线槽），每帧刷新；③ 掩体**槽位锚定吸附**（默认 `formCoverCorridor` 10m）+ 沿长线多点采样 + 已选掩体点防挤堆；④ 修掩体查询死兜底（`IsCoverAvailable` 后置过滤把无向兜底杀成死代码，陷阱 118）；⑤ 阵型下发不画路线虚线、按各人槽位判到位（修"全员到位永不成立→挂满 45s"）。**四份发布文档补齐 1.4.39~1.4.56 共 18 版变更史**（原停在 1.4.38）。EN DLL 242,176 B，sha256 `C5A0A454…`，**构建=部署=包内逐字节一致**；CN DLL 222,720 B `059B0F34…`（`CN_BUILD`）。两包拆包核对 = DLL + README.txt + Nexus_description.md（**包内文档与源文档 sha256 全等**，按包语言路由：CN 包装的是 README_CN/Nexus_CN，打包时改回通用名）；反编译复核版本双写 1.4.56 ✓（EN/CN 双包均已核对）；诊断日志全部 `debugLog` 门控 ✓） |
| `ER2_UniversalGeneration_v2.5.34.zip`（EN，**当前部署**）/ `ER2_UniversalGeneration_CN_v2.5.34.zip`（CN） | 09-25 17:44（**面板透明度 50% 真正生效**：`Er2Ui.SetPanelAlpha` 陈旧下界 `0.55f → 0.40f`——此前用户设的 0.50 被静默抬成 0.55，cfg 的 0.40~0.55 是死区（新陷阱 AGENTS 17g52）；同批 **SquadCommand 1.4.48 同步重建、按用户定案不出包**。EN DLL 128,000 B，sha256 `fcc9a489eca8…78c27`，**构建=部署=包内逐字节一致**；CN DLL 120,320 B `a5198575…`（`CN_BUILD`）。两包拆包核对 = DLL + README.txt + Nexus_description.md（**包内文档与源文档 `cmp` 全等**，按包语言路由）；反编译复核版本双写 2.5.34 + `Clamp(v, 0.4f, 1f)` ✓） |
| `ER2_UniversalGeneration_v2.5.33.zip`（EN，历史 · 已被 2.5.34 取代）/ `ER2_UniversalGeneration_CN_v2.5.33.zip`（CN） | 09-25 17:28（**发布版**：**诊断日志收口**——4 处周期性/限频诊断全进 `Debug/debugLog` 门控（阵营行 2s 探针 / 物品库 5s 就绪探测 / 第三方内容每轮扫描 / 小队覆盖统计），发布版默认安静、排查时开开关即得同样观测；**文档补齐**——README/Nexus 中英四份首行版本 + 变更史补 2.5.20~2.5.33 共 14 条（原 README 停在 2.5.19、台账停在 2.5.26 的漂移）；物品 Shift 连续放置进 How to use / Main features。EN DLL 128,000 B，sha256 `f61b8bf617FF…8A5EE`，**构建=部署=包内逐字节一致**；CN DLL 120,320 B `f4dd3e3b…`（`CN_BUILD`）。两包拆包核对 = DLL + README.txt + Nexus_description.md（**包内文档与源文档 `cmp` 全等**，按包语言正确路由）；反编译复核版本双写 2.5.33 + 5 处日志门控在位 + 入口三色复位未受损 ✓） |
| `ER2_UniversalGeneration_v2.5.19.zip`（EN，历史） | 09-25（**最新**：随共享层与指挥官 mod 同批（本 mod 侧无独立变化）。DLL 124,928 B，sha256 `7E6A73A1996F…B92F`，与构建产物逐字节一致；反编译复核版本双写 2.5.19 ✓） | 09-25（**最新**：随共享层与指挥官 mod 同批（本 mod 侧无独立变化，扫描确认文字色源全白）。DLL 124,928 B，sha256 `068EB8B33C9B…CC79`，与构建产物逐字节一致；反编译复核版本双写 2.5.18 ✓） | 09-25（**最新**：随共享层与指挥官 mod 同批（本 mod 侧无独立变化）。DLL 124,928 B，sha256 `A95C1CE6AFE9…CAC9`，与构建产物逐字节一致；反编译复核版本双写 2.5.17 ✓） | 09-25（**最新**：悬停反馈保住（新增 `TextHover`，否则文字全白后悬停变暗静默失效）；`uiPanelAlpha 0.72 → 0.50` + 迁移链。DLL 124,928 B，sha256 `CD7C2B5E43FC…F32B`，与构建产物逐字节一致；反编译复核版本双写 2.5.16 + `TextHover` 7 处 ✓） | 09-25（**最新**：删 tooltip 框；悬停反馈恢复为文字变暗。DLL 124,928 B，sha256 `DB886F4FE291…C35B`，与构建产物逐字节一致；反编译复核版本双写 2.5.15 + 三参 `GUIContent` 0 处 + 悬停变暗 3 处 ✓） | 09-25（**最新**：`uiPanelAlpha` 旧默认 0.85 → 0.72 迁移；文字描边 4 方向 + 列表行 `FontBody+4`。DLL 125,440 B，sha256 `4B815479CFCB…AA51`，与构建产物逐字节一致；反编译复核版本双写 2.5.14 + 迁移判断 + `FontBody + 4` ✓） | 09-25（**最新**：**悬停提示恢复**——`GUIContent.none` 曾把 tooltip 通道一起清掉；控件改 `new GUIContent("", null, tooltip)`（IL2CPP 仅三参构造）+ 帧末 `DrawHoverTip()` 自绘；EN 表补 1 条。DLL 124,928 B，sha256 `65BFF661AE08…6BD2D`，与构建产物逐字节一致；反编译复核版本双写 2.5.13 + `DrawHoverTip` 2 处 + 三参 `GUIContent` ✓） | 09-25（**最新**：与指挥官 mod 共用同一套面板风格（近黑 + 白描边 + 皮革 + 受光边）；玩法无变化。DLL 124,416 B，sha256 `DDD65A0DE5C0…C9C9`，与构建产物逐字节一致；反编译复核版本双写 2.5.12 ✓） | 09-25（**最新**：行文字改描边双绘（空按钮画底 + `LabelOutlined`，字号 `FontBody+3`）；页签同步（共享 `TabGrid`）；面板去蓝调 + α 0.72。DLL 124,416 B，sha256 `30F21190032A…3A57`，与构建产物逐字节一致；反编译复核版本双写 2.5.11 + `GUIContent.none` 4 处 + `LabelOutlined` 3 处 ✓） | 09-25（**最新**：行分隔线**每行都画**（原只在行间画 → 单条目列表无线；文件夹列表原 0 处）；列表文字 **Bold + 字号 +1**；星标 17px + Bold + `#FFE81A`；底色再提亮。DLL 123,392 B，sha256 `F370F678365C…DEA7`，与构建产物逐字节一致；反编译复核版本双写 2.5.10 + `i + 1 < to` 0 处 + `HLine` 4 处 + Bold/17px ✓） | 09-25（**最新**：**收藏改两级文件夹**——主文件夹列分类（`▸ 名称 (数量)`）、点进去看条目 + 面包屑 `◀ 收藏`；移除原 FavTabs 页签行、不再自动跳第一个分类；列表斑马纹；帮助行 wordWrap + 两行高根治溢出。DLL 123,392 B，sha256 `19F54152CC0C…0ABD`，与构建产物逐字节一致；反编译复核版本双写 2.5.9 + 三个收藏成员在位 + FavTabs 三项 0 处 + `RowBgAlt` 3 处 ✓） | 09-25（**最新**：前景纯白 + 透明只作用于背景；星标 textColor 改纯白（相乘致暗根因）+ `StarOn #FFD800`；帮助行**横向**溢出修复（`helpStyle` + `FitSize`、英文 99→66 字符、留白 26→30）。DLL 122,368 B，sha256 `D4A6C05AF11D…E9C7`，与构建产物逐字节一致；反编译复核版本双写 2.5.8 + `helpStyle`/`FitSize` 在位 ✓） | 09-25（**最新**：标记线宽随共享层改固定世界米（近大远小）。DLL 122,368 B，sha256 `5245A86B8812…2E4D`，与构建产物逐字节一致；反编译复核版本双写 2.5.7 ✓） | 09-25（**最新**：底部留白 16→26（修"最下面的字超出菜单"）、帮助行 34→38；面板描边加粗到 ~2px；对比度提升（随共享令牌）；线宽回退。DLL 122,368 B，sha256 `AB482BCC6982…ADBD`，与构建产物逐字节一致；反编译复核版本双写 2.5.6 + `26f`/`38f * Er2Ui.Scale` + `Max(1.5f, 2f*scale)` ✓） | 09-25（**最新**：面板中性黑（随共享令牌去色相）+ 皮革去暖调；新增 `UI/uiPanelAlpha`（默认 0.85）；线宽随共享层再 +60%；EN 表同步。DLL 122,368 B，sha256 `7DD008DF306B…2DDF0`，与构建产物逐字节一致；反编译复核版本双写 2.5.5 + `uiPanelAlpha` 5 处 ✓） | 09-25（**最新**：面板底走 `PanelBase()` 近黑皮革 + 标题条皮革叠加；标题行高 26→34 修顶部按钮重叠；线宽随共享层 +40%。DLL 121,344 B，sha256 `3414B396BD31…0118`，与构建产物逐字节一致；反编译复核版本双写 2.5.4 + `34f * Er2Ui.Scale` Title 行 ✓） | 09-25（**最新**：黑棕半透明 UI + 元素区分 + 文字重叠修复（硬编码像素全部 × Scale、行间距统一加一次）；EN 表补 6 条。DLL 120,320 B，sha256 `66E8858120A4…03EF`，与构建产物逐字节一致；反编译复核版本双写 2.5.3 + 行间 Gap 2 处 + `EdgeSoft` 分隔线 2 处 ✓） | 09-25（**最新**：与宿主同批视觉打磨——面板补结构线（标题条独立底色 + 标题下分隔线 + 外框 + 两个列表内凹边框 + 收藏行左竖条）；配色压回中深灰与宿主完全同一套；修掉 `BuildRows` 6 处漏乘 `Scale` 的固定行高。DLL 118,272 B，sha256 `B157D4F27084…85D`，与构建产物逐字节一致；反编译复核版本双写 2.5.2 + `Frame/HLine/AccentBar` 成员与 6 处调用点 ✓） | 09-25（**最新**：UI 自适应——跟随游戏原生 `ResourcesManager.ResolutionMult`，令牌 `const`→属性（`const` 是"UI 是死的"根因）、入口挂 `GenRunner.Draw()` 总入口、携带徽标 560px 走 `ScreenFit`、样式各判各的重建；配色提亮；携带徽标底/拖拽目标环/生成反馈文字的残留绿色清零。DLL 117,248 B，sha256 `17BACD4266FE…76F4`，与构建产物逐字节一致；反编译复核版本双写 2.5.1 + 自适应成员在位 + 旧亮绿/淡绿 0 处 ✓） |
| `ER2_UniversalGeneration_v2.4.2.zip`（EN，历史） | 09-24（**最新**：无可见改动，为 UI 重绘打底——布局改**行计划单一数据源**（高度 = 行计划求和，绘制遍历同一列表，结构性消灭"画到面板外"复发）；新增 `Shared/Er2Ui.cs` 共享令牌/原语层，两个 csproj 源码级链接；页签适配改 `CalcSize` 精确测量 + 缓存。DLL 110,592 B，sha256 `F6C37E8260F2…A431A`，与构建产物逐字节一致；反编译复核版本双写 2.4.2 + `Er2Ui.*` 成员 + `BuildRows`/`DrawRows` ✓） |
| `ER2_BattlefieldCommander_v1.4.38.zip`（EN，**当前部署**） | 09-25（**最新**：**纠正 1.4.37 的误读**——"做成左上角显示的样子，不要背景了"指的是【屏幕底的提示条】（用户原话："谁告诉你是信息面板去底板了？我说的是屏幕底的提示"）。修法：信息面板底板**恢复**；提示条**去底板** → `DrawShadowLabel` 白字 + 阴影（与左上角原生信息同款）；互斥规则不变（陷阱 114）。DLL 231,424 B，sha256 `335B45EECCB1…070F`，与构建产物逐字节一致；反编译复核版本双写 1.4.38 + 互斥在位 + 提示条 `DrawShadowLabel` ✓ + `DrawHudPlate` 3 处（提示条 0 处）✓） | 09-25（**最新**：**最后一处灰字**——提示条 `HudStyleSmall()` 与背包 `tipStyle` 硬编码军绿遗产 `(0.85,0.9,0.85)`（不经过 `uiText`，1.4.36 统一漏掉）→ 都改 `Er2Ui.Text`；**全项目扫描**确认无旁路色源（陷阱 113 补：色源收编是"收编 + 扫尾"两步）；**信息面板去底板**（与游戏原生左上角同款，白字 + 阴影叠场景）。DLL 231,424 B，sha256 `E39D2AE7B5EF…4DC7`，与构建产物逐字节一致；反编译复核版本双写 1.4.37 + `HudStyleSmall` 纯白 ✓ + `DrawHudPlate` 3 处（InfoPanel 已删）✓ + 灰绿 0 残留 ✓） | 09-25（**最新**：**文字还是灰色的真因**——HUD 文字色走 cfg `colorText`，而用户 cfg 里是旧灰值（陷阱 109）；修法两层：`colorText` 加入迁移链 + **色源收编**（`uiText = Er2Ui.Text`，HUD 与 UniGen 面板共用同一个纯白令牌，三个颜色 cfg 保留但不再被读取，陷阱 113）；**信息栏与提示条互斥**（没选中不画信息面板、提示条显示；有选中反之，`InfoPanel.Visible`）；`colorBase` 默认 → `#00000080`。DLL 231,424 B，sha256 `00C8213223A8…8DED`，与构建产物逐字节一致；反编译复核版本双写 1.4.36 + `InfoPanel.Visible` + 互斥引用 + `uiText = Er2Ui.Text` ✓） | 09-25（**最新**：**修重叠**——上轮"对称"把底距改成 14，而提示条顶在离底 30，两块 HUD 直接穿过提示条；改 **36**（= 30 + 6 间隙），左右依旧对称（陷阱 112：HUD 垂直空间预算共享）；**背景透明度 50%**（`PanelAlpha 0.72→0.50`、`Scrim` 同步、cfg 范围 0.40~1.0、迁移链补 0.72）；**文字全白**（`TextDim → #FFFFFF`）+ 新增 `TextHover #B4B4BA` 保住悬停变暗反馈。DLL 230,912 B，sha256 `C2779A0FA426…CD23`，与构建产物逐字节一致；反编译复核版本双写 1.4.35 + `BottomGap 36` ✓ + `PanelAlpha 0.5f` ✓ + `Scrim (0,0,0,0.5f)` ✓ + `TextHover` ✓） | 09-25（**最新**：**左下信息面板动态高度 + 左右对称**（`BottomGap 34→14` 与小队列表同；`contentH` 每帧实测内容底部回写，内容少不再留大空白，陷阱 111）；**删悬停提示框**（用户要求）并**恢复悬停反馈 = 文字变暗**（`TabGrid` + 三处列表逐行判鼠标 → `TextDim`）。DLL 230,912 B，sha256 `535BAAB38BA5…2A83`，与构建产物逐字节一致；反编译复核版本双写 1.4.34 + `DrawHoverTip` 0 处 + 悬停变暗 4 处 + `contentH` 3 处 ✓） | 09-25（**最新**：**cfg 默认值迁移**——BepInEx 的 cfg 生成后不随代码默认值更新，老用户 `colorBase`/`colorHover` 仍是军绿旧值 → HUD 按钮"UI 只改了一半"；现命中历史默认值集合才迁移（不覆盖用户自定义）+ 打日志（陷阱 109）；**幽灵全材质槽**——`sharedMaterial` 只改槽 0，多槽模型（士兵装备/履带）保持原色；改 `sharedMaterials` 数组全填（陷阱 110）；**文字对比**——描边 2 → **4 方向**、列表行 `FontBody+4`。DLL 230,912 B，sha256 `F2342D516DA9…2537`，与构建产物逐字节一致；反编译复核版本双写 1.4.33 + `MigrateLegacyUiCfg` 2 处 + `sharedMaterials` 2 处 + `LabelOutlined` 5 次 Label ✓） | 09-25（**最新**：**幽灵修复**——`Sprites/Default` 读**顶点色**不读 `_Color`，Mesh 无顶点色 → 实心亮白；改 `Particles/Standard Unlit` + Fade 显式配置，`WGhost` → `#8C9196@0.20`（陷阱 108）；**左下单位信息加整块底 + 左边缘 `10*Scale`**（与右下小队列表左右对称）；**HUD 统一底板** `DrawHudPlate`（`Scrim` 纯黑 72% + 皮革 + 描边）三处共用（提示条 / 小队列表 / 单位信息），`colorBase` 默认 → `#000000B8`；**悬停提示恢复**（`GUIContent.none` 曾清掉 tooltip 通道，陷阱 107）。DLL 229,376 B，sha256 `AB34EEA85B37…5D64`，与构建产物逐字节一致；反编译复核版本双写 1.4.32 + shader 链 + `_Mode` + `WGhost 0.2f` + `DrawHudPlate` 4 处 + `PanelPad` ✓） | 09-25（**最新**：**捡枪隔空真因＝中文前缀判定在 EN 版失效**（`StartsWith("拾起")` 恒 false → 落回原生 `Call()`（无距离校验））→ 改**结构判据** `Interaction.classType`（比较 gameObject/子物体，与语言无关）+ 中英前缀兜底，新增 `menuInts` 平行列表（陷阱 106）；**光标** `TexSize 32 → 64`（32 是硬件光标上限，被 DPI 缩放拉伸即糊）+ 环半径等比；**光标恢复逐状态语义色**（友军青绿/敌军红/载具亮青/建筑灰白/工事橙/可交互黄/默认白，1.4.19 曾整体压灰阶）；**两 mod UI 统一**——cfg 色值对齐、HUD 按钮描边改 `PanelBorder`、InfoPanel/背包底板补皮革 + 受光边。DLL 228,352 B，sha256 `219915CAC05F…C99D`，与构建产物逐字节一致；反编译复核版本双写 1.4.31 + `IsPickupInteraction`/`classType` 在位 + `menuInts` 9 处 + `TexSize 64` + 六色语义色 ✓） | 09-25（**最新**：**文字改描边双绘**（`Er2Ui.LabelOutlined`，暗色偏移 ×2 + 白色正文）——真因是 `FontStyle.Bold` 被**静默忽略**（游戏字体单字重非 dynamic，见陷阱 105）；Button 文字没法描边 → 改「空按钮画底 + 单独画字」（列表 3 处 + `TabGrid`）；行文字 `FontBody+3`；**面板配色对齐 HUD 提示条**——所有面去蓝调（`#101010` 系）+ `PanelAlpha` 默认 `0.85 → 0.72`（与 `Scrim` 一致）。DLL 227,328 B，sha256 `A26B8AB52DAF…5576`，与构建产物逐字节一致；反编译复核版本双写 1.4.30 + `LabelOutlined` 在位 + `#101010`(R=G=B) ✓ + `PanelAlpha 0.72f` ✓） | 09-25（**最新**：底色再提亮一档（`#1A1A22` 系）；**列表文字改 Bold + 字号 +1**——"文本太暗"的真因是**笔画太细**而非对比不足（纯白叠 `#1A1A22` 已约 15:1，12px Normal 笔画仅 1px 宽，见陷阱 103）；星标 14→17px + Bold + `#FFE81A`；**行分隔线每行都画** + 收藏文件夹列表补线（陷阱 104）。DLL 227,328 B，sha256 `4CFE49D71C5A…5780`，与构建产物逐字节一致；反编译复核版本双写 1.4.29 + `#1A1A22`/`#24242C`/`#2E2E38`/`#FFE81A` ✓） | 09-25（**最新**：面板再提亮一档（`#121218` / `#1C1C24` / `#26262E` / `#1A1A20`）；列表**斑马纹**（新增 `RowBgAlt`）。⚠️ 关于"下面的文字超出"——比对截图确认跑的是旧 DLL（文案仍为 1.4.27 前的 99 字符旧句），已在回复中给出日志版本号核对法；本轮仍把该行做成 **wordWrap + 两行高（56）** 根治。DLL 227,328 B，sha256 `F59AC09D3A54…F442`，与构建产物逐字节一致；反编译复核版本双写 1.4.28 + `#121218` ✓ + `#23232A`(RowBgAlt) ✓） | 09-25（**最新**：**透明只作用于背景**——1.4.24 曾把线条 α 也绑到 `PanelAlpha`（调透明度连边框一起变淡，这是"对比太低"的根因），现在面跟随 `PanelAlpha`、**前景不透明**；文字/描边/分隔线**改纯白**（#FFFFFF / 白@0.85 / 白@0.65 / 白@0.30）；**星标提亮**（`starStyle.textColor` 改纯白——`contentColor` 与 `textColor` 相乘曾把金色乘成 #CAA84D；`StarOn` → #FFD800）；**帮助行溢出真因是横向**（英文 99 字符 ≈570px vs 内宽 450px）→ 缩短文案 + 专用 `helpStyle` 走 `FitSize` + 留白 26→30。DLL 226,816 B，sha256 `B1D65DED6389…7FAD`，与构建产物逐字节一致；反编译复核版本双写 1.4.27 + 前景纯白 ✓ + `StarOn #FFD800` ✓ + 线条不再绑 `PanelAlpha` ✓） | 09-25（**最新**：标记线宽**回到固定世界米**（0.030~0.065m）——"近小圆大"的根因是**两套尺度规则混用**：标记半径是固定世界值（Collider 量出，0.35~1.1m）而线宽是"屏幕像素恒定"（`width_m ∝ dist`），60m 处 1.5px ≈ 0.10m、200m 处 ≈ 0.34m 逼近环半径 0.6m → 环被填成实心圆盘（陷阱 99）；FOV 注入链（`SetCamFov`/`camFovDeg`/`RefScreenH`）一并清掉。DLL 226,816 B，sha256 `C32C79C148DB…D194`，与构建产物逐字节一致；反编译复核版本双写 1.4.26 + `Clamp(baseMeters, 0.003f, 3f)` ✓ + 旧 FOV 链 0 处 ✓ + 13 处世界米线宽 ✓） | 09-25（**最新**：标记线宽**回退到 1.4.23 数值**（1.5~3.0px）——"太细"的真因是**镜头拉太近**（环在世界空间是固定半径，近处屏幕占比大而线宽像素恒定，线宽/孔径比例骤降，陷阱 97）；**修底部文字贴边**（底部留白 8→18、帮助行 34→38）；**对比度一轮**（面板描边 `#555560` + ~2px、选中底 `#52525E`、行底更沉、行分隔线 0.15、列表底 α 0.50）。DLL 227,328 B，sha256 `D30FD7FABA8F…52DC`，与构建产物逐字节一致；反编译复核版本双写 1.4.25 + 13 处线宽回退 ✓ + `#555560`/`#52525E`/`#45454E` ✓） | 09-25（**最新**：面板改**中性半透明黑**（去棕，R=G=B，皮革纹理同步去暖调）；**面板不透明度做成 cfg** `UI/uiPanelAlpha`（默认 0.85 / 0.55~1.0，色板只存 RGB、α 由 `PanelAlpha` 推出，标题条+0.05 / 悬停+0.07 / 选中+0.13 / 行底−0.05）——"透 ↔ 黑"这对矛盾轴交给玩家；标记线宽再 +60%（主力 3.5~4.8px）；EN 表同步 5 条键漂移 + 新增 1 条。DLL 227,328 B，sha256 `A00BF6F38FCC…2DCE`，与构建产物逐字节一致；反编译复核版本双写 1.4.24 + `#08080A` 中性 ✓ + 皮革 `(g,g,g)` ✓ + `PanelAlpha`/`SetPanelAlpha`/`WithA` ✓ + 13 处线宽 ✓） | 09-25（**最新**：近黑皮革面板——"完全是棕色"的根因是 α 0.82 的半透明被棕色地形染色（`dst = src×0.82 + terrain×0.18`），压配色板没用；改 α 0.92 + 底色 `#0C0906`，并新增 `LeatherTex()`/`Leather()`/`PanelBase()` （程序化 64×64 皮革噪声平铺 + 顶部受光边）——皮革观感来自斑驳与受光边、不是颜色；标题行高 26→34 修"最上面的按钮重叠"；标记线宽 +40%。DLL 226,816 B，sha256 `7BC2553ADB…8DA9`，与构建产物逐字节一致；反编译复核版本双写 1.4.23 + 三个皮革成员在位 + `#0C0906@0.92` + 13 处线宽上调 ✓） | 09-25（**最新**：修复"线还是太粗"的真因——相机距离原取 `transform.position.magnitude`（到**世界原点**，地图原点离战区数百米 → 永远是错的大数，旧版被 `Clamp(0.6,2.5)` 掩盖、1.4.21 去掉 clamp 后爆表），改 `CameraGroundDist()`（视线-地面交点真实距离，0.1s 缓存，GodView 与阵型共用）；`Ring` 同款父缩放修复（半径写进顶点 + `lastRadius` 缓存）；抗锯齿——线材质宽度方向羽化贴图 + 圆盘径向羽化 + 环分段 48→64；**黑棕半透明 UI**（#1E1813@0.82 系）；元素区分——页签/阵营/乘员按钮描边 + 列表行分隔线；文字重叠修复——所有硬编码像素 × Scale + 行间距统一加一次；EN 表补 9 条。DLL 225,792 B，sha256 `49C9C886197A…6385`，与构建产物逐字节一致；反编译复核版本双写 1.4.22 + `position.magnitude` 0 处 + `CameraGroundDist` 3 处 + `localScale=Vector3.one` ×2 + `Segments=64` + `Frame` 页签描边 + `Pager` ×Scale ✓） | 09-25（**最新**：视觉打磨——线宽改「1080p 目标像素」语义（旧经验倍率与分辨率强耦合，1440p 粗 1.33×/4K 粗 2×），`Er2Ui.LineWidth(px,dist)=px×2×dist×tan(fov/2)/1080`，FOV 从相机注入；选中角标根因修复——父 `lossyScale` 放大线宽导致载具上两条粗臂糊成三角块（"变成箭头"），改为父 scale 恒 1、半径写进顶点、臂长 0.34→0.42；世界标记 α 0.26~0.48 半透明 → α≥0.80 灰阶实色（层次从 α 移到灰度值）；面板补 `Frame`/`HLine`/`AccentBar` 结构线 + 标题条独立底色 + 列表内凹边框 + 收藏行竖条；配色压回中深灰（1.4.20 提亮过头）。DLL 222,720 B，sha256 `3CFA3DDCF6B2…D1D3`，与构建产物逐字节一致；反编译复核版本双写 1.4.21 + 像素线宽模型 + `SetCamFov` 2 处 + 旧 `WidthScale` 0 处 + `localScale=Vector3.one` + 臂长内联 0.58 + `Frame/HLine/AccentBar` 在位 ✓） | 09-25（**最新**：UI 自适应 + 提亮——跟随游戏原生 UI 倍率、令牌属性化、底部提示条 1400px 走 `ScreenFit`、HUD/背包/信息面板/菜单全量接入、倍率变化时背包保留拖动位置重排；灰黑配色提亮并拉开层次差。DLL 219,648 B，sha256 `8EC63CEBF9A7…28C0`，与构建产物逐字节一致；反编译复核版本双写 1.4.20 + 自适应成员在位 + `positionCount=3` + 旧弧公式 0 处 + `new GUIStyle(带参)` 0 处 ✓） |
| `ER2_BattlefieldCommander_v1.4.18.zip`（EN，历史） | 09-24（**最新**：内部调整，四处手写 `GUIStyle` 改走共享层 `Er2Ui.MakeLabel`，语义不变。DLL 206,336 B，sha256 `3E306522BDAA…DE9DD2`，与构建产物逐字节一致；反编译复核版本双写 1.4.18 + `Er2Ui.MakeLabel` ✓） |
| `ER2_UniversalGeneration_v1.3.2.zip` / `ER2_UniversalGeneration_CN_v1.3.2.zip` | 09-19（**待发 Nexus**：火力点页签 + 小队库 466 变体 + 敌方原生 AI + 启动时零卡顿探测；1.3.1 砍自定义班生成——运行时空壳，1.3.2 按用户决定砍全部第三方生成；拆包核对 = DLL + README.txt + Nexus_description.md ✓ 双语双包 ✓） |
| `ER2_BattlefieldCommander_v1.4.15.zip` / `ER2_BattlefieldCommander_CN_v1.4.15.zip` | 09-24（**最新**：上帝视角打开设置后相机输入不再穿透菜单（滚轮/WASD/中键）；兼容 Advanced Combat Movement 吞掉"恢复开火"调用 → 改「调用→回读→直写 holdFire 字段」。EN 构建 = **当前部署**（sha256 `f474a84f…`，与 EN 包内 DLL 一致；CN 包 `f9600279…` 更小，仅打包不部署）；拆包核对均 = DLL + README.txt + Nexus_description.md（build.ps1 按包语言取文档，双语包各 3 文件）） |
| `ER2_ModManager_v1.5.4.zip` / `ER2_ModManager_CN_v1.5.4.zip` | 09-24（**最新**：翻页音效兜底 —— 第三方假页吞掉原生 Tab 方法时由我们补 `ClickSound()`，并打印翻页分支追踪日志；拆包核对 = DLL + README.txt + Nexus_description.md（ModManager 无中文文档，CN 包文档仍为英文）） |
| `ER2_ModManager_v1.5.3.zip` / `ER2_ModManager_CN_v1.5.3.zip` | 09-24（与第三方"假页"式原生设置页（Advanced Combat Movement）共享翻页链 —— MODS 页不再被它的 TabRight 劫持挡住） |
| `ER2_BattlefieldCommander_v1.4.14.zip` / `_CN_v1.4.14.zip` | 09-19（历史：性能优化） |
| `ER2_BattlefieldCommander_v1.1.0.zip` / `ER2_BattlefieldCommander_CN_v1.1.0.zip` | 09-06 |
| `ER2_UniversalGeneration_v1.0.0.zip` / `ER2_UniversalGeneration_CN_v1.0.0.zip` | 09-06 |
| `ER2_ModManager_v1.5.2.zip` | 09-18（**最新**：删掉自锁的开发开关 `NativeFull` + 整文件删除 `NativePage.cs`，MODS 页只剩一条建页路径；拆包核对 = DLL + README.txt + Nexus_description.md） |
| `ER2_ModManager_v1.5.1.zip` | 09-18（历史：删除遗留 PoC 测试页 + `NativePoc` 开关；新增 `Debug`/`debugLog` 开关，默认关） |
| `ER2_ModManager_v1.5.0.zip` / `ER2_ModManager_CN_v1.5.0.zip` | 09-12（历史：点击不再白闪 / 三层可见性 / 分区默认收起+箭头 / 标签统一字号+纯文本 / 值框 130×24 / 行内缩按滚动条绝对目标） |
| `ER2_ModManager_v1.2.0.zip` / `ER2_ModManager_CN_v1.2.0.zip` | 09-05（历史） |
| `ER2_UnitInfoOverlay_v1.0.5.zip`、`ER2_HideAnything_v4.5.4.zip`、`ER2_WeatherControl_v1.7.2.zip`、`ER2_LimbTweaks_v2.13.101.zip` | 09-05 |
| `ER2_VeteranHVT_v1.2.0.zip`（EN，**当前部署**）/ `ER2_VeteranHVT_CN_v1.2.0.zip`（CN） | 09-13（**最新**：头顶标记改 3D 世界空间 billboard（恒定屏占比 + 深度遮挡）+ 载具乘员击杀共享/一载具一标记 + 关闭叛徒机制完全失效；**本次补齐 `README_CN.txt`/`Nexus_description_CN.md`** → CN 包文档首次为中文；拆包核对 = DLL + README.txt + Nexus_description.md + bf1_kill.wav；EN/CN 包 DLL sha256 不同（`-Cn` 带 `CN_BUILD`）） |
| `ER2_MorePhysics_UnitCollision_v1.0.8.zip`、`ER2_MorePhysics_v0.1.49.zip` | 09-13（**热开关修复**：矩阵开→关即时恢复原状，不再"关了还有碰撞"；轻量版另修软推开关细分；拆包核对均 = DLL + README.txt + Nexus_description.md） |
| `ER2_UniversalGeneration_v2.2.1.zip`（EN，**当前部署**） | 09-24（**2.2.1 修 2.2.0 回归**：字母行「全部」占两格后循环 `letters[i-2]` 从 i=1 起读 → `letters[-1]` 越界 → 整帧 OnGUI 中断 → 物品列表全空白（截图=子分类页签+孤零零「全部」）。修=字母从 i>=2 起排。**教训：改"按钮占位格"时必须把占位格与数据下标分开算**） |
| `ER2_UniversalGeneration_v2.2.0.zip`（EN） | 09-24（**2.2.0 三条用户反馈**：① 物品携带预览改 3D 幽灵模型（裸实例化+冻结刚体+宿主 Ghostify，删光圈/光标图标）；② 字母行「全部」占两格修按钮重叠；③ **mod 内容支持回归**（新 `ModCatalog.cs`）：磁盘解析各 mod `index.xml`（Runtime DB 无法区分官方/mod，2.0.7 定案 PD 恒空）→ `GenerateModItemId` 算 id → 物品 `GetItemObject` 校验入「Mod物品」、载具 `GetVehiclePrefabAsync` 验证入「Mod载具」；mod 小队已在步兵页（SquadsArchive 全量 key）。DLL 102,912 B，sha256 `B9CD69DB…478D8D3`，反编译复核 ✓） |
| `ER2_UniversalGeneration_v2.1.1.zip`（EN） | 09-19（**2.1.1 修复"完全没有显示"**：日志 `[UniGen] OnGUI 异常: Method unstripping failed` ×8 → **`GUI.TextField` 被 IL2CPP 裁剪**，一抛异常整帧 OnGUI 中断（子分类页签在它之前绘制故幸存，列表全空）。**彻底弃用键盘输入**，改纯点击首字母索引：`LettersOf/LetterOf/HasLetter` + 24px 按钮自动换行、只列实际出现的字母 + 「全部」；`Query` 的 `filter`→`letter`；删 `fieldStyle` 与 `SetNextControlName`。**按用户要求部署英文版**（不加 `-Cn`）。DLL 93,696 B，sha256 `D292BEF5…32990C`，与构建产物逐字节一致；反编译复核版本双写 2.1.1 且 TextField 已消失 ✓） |
| `ER2_UniversalGeneration_CN_v2.1.0.zip` | 09-19（**2.1.0 物品收藏 + 收藏分类 + 子分类 + 过滤框**：2.0.7 修好后物品 2106 条（weapons 698 / gear 542）翻找困难。物品行加 ★；收藏页签下多一行分类子页签（只列已收藏分类，单位/物品各走各渲染不混排）；物品子分类用**游戏官方字段**（`TryCast<Weapon>().weaponPose` → 步枪/手枪，`IsWerable()` → 可穿戴，不猜名字）；过滤框用 `GUI.TextField`（物品 id 全英文，无需中文输入法）。踩坑：`new RectOffset(4参)` 被 IL2CPP 裁剪 → CS1729，改 `contentOffset`；收藏**写入点合并到 `GenCatalog.SaveFavs` 一处**防互相覆盖） |
| `ER2_UniversalGeneration_CN_v2.0.7.zip` | 09-19（**2.0.7 修复"还是没有"——真正根因定案**：2.0.6 诊断日志连续 154s 跨三场景打印 `Loaded=true 但 items 枚举 n=0` → **就绪闸门条件恒不成立**（`Loaded` 恒 true、`items`(6) 恒空），闸门永不开 → 枚举从未执行（tick 看门狗本身工作正常，它重启了但每次都卡同一处）。修法两条：① **闸门整体移除**（`ProbeDatabaseReady`→`SampleCounts` 只观测），无条件枚举 + 空则重试；② **泛型实参问错类型**——`GetAllItemsOfType<PropData>` 改为 **`<ItemObject>`**（`GetItemObject(id)` 返回的类型，带 `item_id`/`icon`），PropData 兜底、按 id 去重。另加 Ready 后 20s 一次性补漏重扫。诊断打印两种类型×四类别真实条数。**教训：前四轮只查"等闸门的循环活不活"，没查"闸门条件本身对不对"**） |
| `ER2_UniversalGeneration_v2.0.6.zip` / `ER2_UniversalGeneration_CN_v2.0.6.zip` | 09-19（**2.0.6 修复"一个物品分类都没有"**：根因 = **2.0.5 看门狗逻辑写反**——`if (probeState==1) return;` 但协程被场景切换杀死时无人复位 `probeState` → 永远卡 1 → 看门狗永远不重启（证据 = 2.0.5 新加的诊断日志一条都没打）。修法 = **tick 计数判活**（协程每帧 `probeTicks++`，看门狗比对是否增长；不增长即真死）。**判据第四次迭代**：150s 硬阈值 → 20s 心跳 → 不判死 → **tick 计数**；前三轮共同错误 = 拿时间流逝当协程死亡判据。反编译复核两处版本 + tick 比对分支 ✓） |
| `ER2_UniversalGeneration_v2.0.5.zip` / `ER2_UniversalGeneration_CN_v2.0.5.zip` | 09-19（**2.0.5 修复"根本没有对应的选项"**：2.0.4 已生效且逐帧轮询在跑，但**看门狗在场景加载期间误判"心跳停跳"→ 重启 → 6 次触顶后 probeState=3 永久放弃** → 物品页签永不建起。修法 = 移除放弃路径 + 新增 `LogProbeDiag` 就绪诊断。**但本轮引入新 bug：看门狗"不判死只续跑"导致协程真死时永不重启 → 2.0.6 再修**） |
| `ER2_UniversalGeneration_v2.0.4.zip` / `ER2_UniversalGeneration_CN_v2.0.4.zip` | 09-19（**2.0.4 修复"仍然不能生成物品"**：先证伪"中英不同步"（部署 DLL 与 EN 构建**逐字节一致**、日志证明 2.0.3 在跑）→ 真因 = 2.0.3 的 `WaitForSeconds(2f)` 轮询卡在等待里、随场景切换被杀，150s 看门狗重启时预算已空耗；改**逐帧轮询** + 心跳判活 + 预算就绪后起算。**本轮踩坑：`[BepInPlugin]` 首次编辑未落盘，反编译才发现特性 2.0.3 / 日志 2.0.4 不一致**） |
| `ER2_UniversalGeneration_v2.0.3.zip` / `ER2_UniversalGeneration_CN_v2.0.3.zip` | 09-19（**2.0.3 修复物品生成整体不可用**：就绪闸门 `l != null` 被"非 null 空数组"骗过 → 一次跑死、页签建不起来；改双判据 `ItemsDatabase.Loaded` + `Count>0`，重试上限 60 次/2 分钟，看门狗 20s→150s 并加进度基线；拆包核对 = DLL + README.txt + Nexus_description.md；反编译三重复核版本/闸门/重试结构 ✓） |
| `ER2_UniversalGeneration_v2.0.2.zip` / `ER2_UniversalGeneration_CN_v2.0.2.zip` | 09-19（**2.0.2 物品目录重写**：停止猜 id，改运行时 `ItemsDatabase.GetAllItemsOfType<PropData>` 枚举四类；`Uniforms` 桶整体移除） |
| `ER2_UniversalGeneration_v2.0.1.zip` / `ER2_UniversalGeneration_CN_v2.0.1.zip` | 09-19（**2.0.1 修复**：点选不再秒放（`ignoreUntilRelease` 补 `return`）、武器判定改兜底默认 + `BeginValidate` 启动剔除无效条目、士兵命中半径按相机距离反算 + `RaycastAll` 找地面；失败路径补全日志） || `ER2_UniversalGeneration_v2.0.0.zip` / `ER2_UniversalGeneration_CN_v2.0.0.zip` | 09-19（**物品生成 + 鼠标拖放**：物品页签 380+ 条目带真实图标，拖到单位身上进背包 / 拖到地上生成实体；Direct`items.Add` 注入保子类；双语包内容与 Tr 字典反编译核对 ✓） |
| `ER2_CombatTweaks_v1.2.2.zip`（+CN）、`ER2_InventoryPause_v1.0.5.zip`（+CN） | 08-25/26 |

**历史清理**：SquadCommand 全部中间版本包与暂存目录已删（只留 1.1.0 双语）；工作区根目录 `EasyRed2_BepInEx_Dependencies.zip` 已删。
**2026-09-13 大清理（玩家要求）**：Downloads 里积压的全部**被取代旧版本包 + build.ps1 打包暂存目录**已删 —— 旧包 34 个（HVT 旧名 `ER2_HighValueTarget_v1.0.0/1.1.0/1.1.1` 及 CN 共 6 个、`ER2_VeteranHVT_v1.1.26` 双包、CombatTweaks v1.2.1 双包、HideAnything v4.5.2 双包、ModManager v1.1.1/v1.1.2/v1.2.0 + CN v1.1.1~v1.4.0 共 13 个、UnitInfoOverlay v1.0.0~v1.0.4 + CN v1.0.1~v1.0.4 共 9 个）+ 暂存目录 23 个（`ER2_*` 目录，build.ps1 生成 zip 后不自动清理，下次构建会重建）。**现 Downloads 只保留各 mod 最新版共 18 个包**。**注意：build.ps1 每次打包都会在 Downloads 留下 `ER2_<pkg>` 暂存目录且不自动删——定期清理或忽略即可。**

### 4.1 2026-09-25 清理（本次会话）

Downloads 里 `ER2_*.zip` 已累计到 **151 个**（09-24 / 09-25 两天 40+ 轮迭代打包的产物），按**"每个 mod 每个语言只留最新一版"**规则：

- **保留 20 个**：BattlefieldCommander `EN v1.4.36` / `CN v1.4.15`、UniversalGeneration `EN v2.5.34` / `CN v2.5.34`、ModManager `EN/CN v1.5.4`、VeteranHVT `EN/CN v1.2.2`，以及其余为单一版本的 mod（LimbTweaks / WeatherControl / AIFood / HideAnything / CombatTweaks / ZoomAnywhere / InventoryPause / UnitCollision / UnitInfoOverlay / MorePhysics / MorePhysics_UnitCollision / Conquest EN+CN）。
- **移除 131 个**被取代的旧包 + **10 个** `build.ps1` 打包暂存目录（`ER2_BattlefieldCommander[_CN]` / `ER2_Conquest[_CN]` / `ER2_ModManager[_CN]` / `ER2_UniversalGeneration[_CN]` / `ER2_VeteranHVT[_CN]`）。

⚠️ **本表按磁盘实际文件重建**：SquadCommand 的 `1.4.37~1.4.48` 与 UniGen 的 `2.5.18~2.5.33` 均为"**部署了但未出包**"或"已被取代"，**磁盘上并不存在对应文件**——此前本表里凭记忆写下的 `_v1.4.37 / _v1.4.38` 条目是错的，**不要再按记忆补写**。

**新增纪律（防复发，三条）**：① 迭代期一律用 `-SkipPackage`（只构建+部署、不出包）；② **只有"要发出去的那一版"才出包**；③ 定期按"每个 mod 每个语言只留最新一版"清理，并同时删掉 `build.ps1` 的 `ER2_<pkg>` 暂存目录。

**执行结果（2026-09-25 17:52 完成）**：合计移除 **131 个旧包 + 10 个暂存目录**，其中 **49 个由用户手动删除**（`BattlefieldCommander` 的 `EN 1.4.0~1.4.35` 与 `CN 1.4.0~1.4.14`，目录 mtime 17:47:46），其余 **82 个包 + 10 个目录**由脚本分 **10 批（每批 ≤10）** 删除，**每批删除后均校验**（文件确已消失 + 20 个保留包完好），全程无失败。
**终态**：Downloads 内 `ER2_*.zip` = **20 个**（各 mod 各语言最新版）+ **0 个** `build.ps1` 暂存目录。
⚠️ 环境限制备查：本机沙箱下 **`Add-Type` 与 `New-Object -ComObject` 均被安全策略拦截 → 无法调用系统回收站**，永久删除前已按安全规则取得用户二次确认。

## 5. 台账自检（改完 mod 后跑这个核对）

### 文档漂移（2026-09-06 发现 → 2026-09-12 已修，以源码为准）

**README 版本号 vs `BepInPlugin` 版本**：

| mod | 源码版本 | 原 README | 处置 |
|---|---|---|---|
| `NoInteractionHints` | **4.5.4** | `v4.5.3` ❌ | ✅ 已修（首行 + 补 v4.5.4 变更说明） |
| `UnitInfoOverlay` | **1.0.5** | `v1.0.4` ❌ | ✅ 已修（首行 + 补 v1.0.5 说明 + 去 F5 旧术语） |
| `ModManager` | 1.2.0 | 第 3 行写 `Version 1.2.0` ✅ | 无需修（先前误报：只查了首行） |

**补齐的缺失 README.txt**（2026-09-12，4 个可打包 mod）：`LimbTweaks`、`WeatherControl`、`AIFood`、`ThrowableWheel`
**补齐的缺失 Nexus_description.md**（2026-09-12，同样 4 个）：`LimbTweaks`、`WeatherControl`、`AIFood`、`ThrowableWheel`
> `FleshWoundsFixed` 已有详尽 `README.md` 且**不在 build.ps1**（手动构建部署、不打包），故无需 README.txt；`HvtTestDriver` 是内部工具不发布。
> **实锤影响**（拆包核对）：`ER2_LimbTweaks_v2.13.101.zip` 与 `ER2_WeatherControl_v1.7.2.zip` 内**只有 DLL**——`build.ps1` 对缺失文档**静默跳过**。文档已补齐，**需重打包**才会进 zip（见 TODOS）。
> **注意**：新补的 README / Nexus 描述均为**英文**（与其余 mod 一致）；若要出 `_CN_` 包需另建 `README_CN.txt` / `Nexus_description_CN.md`。

### 版本一致性核对脚本

```powershell
cd D:\Users\71011\Documents\ER2_Mods
# 源码版本（权威）
Get-ChildItem -Directory | Where-Object { Test-Path (Join-Path $_.FullName "Plugin.cs") } | ForEach-Object {
  $t = Get-Content (Join-Path $_.FullName "Plugin.cs") -Raw
  $m = [regex]::Match($t, 'BepInPlugin\("([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"')
  if ($m.Success) { "{0,-22} {1,-34} v{2}" -f $_.Name, $m.Groups[1].Value, $m.Groups[3].Value }
}
# README 版本漂移检查
Get-ChildItem -Directory | Where-Object { Test-Path (Join-Path $_.FullName "Plugin.cs") } | ForEach-Object {
  $t = Get-Content (Join-Path $_.FullName "Plugin.cs") -Raw
  $m = [regex]::Match($t, 'BepInPlugin\("([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"')
  $src = if($m.Success){$m.Groups[3].Value}else{"?"}
  $r = Join-Path $_.FullName "README.txt"
  $rd = if (Test-Path $r) { (Get-Content $r -TotalCount 1) } else { "(no README)" }
  $flag = if ($rd -match [regex]::Escape($src)) { "OK" } else { "** DRIFT **" }
  "{0,-22} src=v{1,-10} README='{2}' {3}" -f $_.Name,$src,$rd,$flag
}
# 部署是否跟上（时间戳）
Get-ChildItem "E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\plugins" -Filter "ER2_*.dll" | Sort-Object LastWriteTime -Descending | Select-Object Name,LastWriteTime -First 12
```


**2.5.47 发布包补齐（2026-09-26 12:55）**：此前发布包停在 2.5.34（2.5.35~2.5.47 迭代期 -SkipPackage 不出包）。
本轮补打双语双包 `ER2_UniversalGeneration_v2.5.47.zip`（EN，138,240 B，`4B9A8168…`）/
`ER2_UniversalGeneration_CN_v2.5.47.zip`（CN，130,048 B，`C8C93CC3…`）；文档四件套本就已同步 2.5.47（变更史
2.5.30~2.5.47 齐全）。因重编译哈希漂移，部署已重新对齐 EN 构建（构建=部署=EN 包内 DLL 逐字节一致）；
拆包核对全 PASS（条目三件/文档=源/版本双写/显示名写入）。

### 2.12.5 UniversalGeneration `er2.universalgeneration` v2.5.48（**启动报错刷屏真因：映射名负缓存缺失**）
**2.5.48（2026-09-26，玩家："启动游戏一直报错，很多很多"——`Prop ID '...' not found! - MappedResources contains: False`）**：
1. **真因**：显示名优先走游戏自己的 `ItemObject.GetMappedResourcesName()`（2.4.0 起），而游戏对映射表
   （MappedResources）里没有的道具**每次调用都打一条 Error**——WW1 制服（`uniforms.ww1_It_*`）、
   `Ger_Schutze Rifleman(1916)` 这类士兵/制服道具不在映射表里。目录 2.5.44 起扩到 2107 条 + **多轮累积
   枚举**，失败调用一轮一轮重复 → 启动刷屏。缓存命中时不枚举（零调用），所以有的启动干净——这正是
   "有的会话报有的不报"的原因。**纯日志噪声**：显示名本来就会回退 `io.name`，功能无损失。
2. **修法（映射名负缓存）**：`MappedNameFailed`（HashSet，本会话）+ 磁盘缓存持久化（`#u\t<id>` 行，
   读回恢复）——查不到的 id 此后**不再问游戏**，直接走 `io.name` 兜底（显示结果与过去一字不差）。
   `ResetForFullRescan`（cfg `refreshItemCache` 手动重建）清空重试。旧缓存文件无 `#u` 行 = 兼容。
3. 顺带：宿主更名 Easy Red Gate——UniGen 四份文档的**当前态**引用同步（历史变更条目保留原名），
   并注明宿主 1.4.57 起上帝键默认 F8。
4. 验证：0 error；双语双包 `ER2_UniversalGeneration_v2.5.48.zip`（EN，138,240 B，`9EACB3C8…`）/
   `_CN_v2.5.48.zip`（CN，130,560 B，`332B844E…`），拆包核对 PASS；部署 = 构建 = EN 包内 DLL
   （`9EACB3C8…`，cp+sha）。
**待实测**：启动日志不再出现 `MappedResources contains` 刷屏；物品列表显示名与 2.5.47 完全一致。

**2.5.48 补充定案（同日 13:30，玩家反馈"启动游戏加载的时候报错、以前没有的"）**：
- 触发时机确认为**启动加载阶段**（2.5.44-2.5.47 把目录扩到 2107 条并把枚举挪到启动期，当日 08:11 部署），
  且这批制服/士兵道具不在游戏映射表里 → "以前没有" ✓，确系 UniGen 引入的噪声（调用的是游戏自己的
  `GetMappedResourcesName()`，Error 也是游戏自己打的）。
- 玩家观察"按空格就不刷" = **Windows 控制台 QuickEdit 冻结**（点/选控制台窗口会暂停输出），并非游戏行为。
- 2.5.48 负缓存的生效节奏：第一次 2.5.48 会话仍会刷一遍（学习期，每条只错一次），失败 id 写入磁盘缓存
  （`#u` 行）后，后续启动零报错——13:16/13:25 两次启动已实测零报错（缓存命中零枚举 + 负缓存已武装）。
  游戏本体将来更新使缓存失效时，枚举会重跑，但 `#u` 集合随缓存恢复 → 只有全新内容可能零星报几条。
- **待复测**：再重启 2~3 次，启动日志应无 `MappedResources contains`。
