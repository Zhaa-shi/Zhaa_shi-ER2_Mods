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
| 5 | `ModManager` | `er2.modmanager` | ER2 Mod Manager | **1.5.2** | `ER2_ModManager.dll` | 已部署 |
| 6 | `ThrowableWheel` | `er2.throwablewheel` | ER2 Throwable Wheel | **1.3.6** | `ER2_ThrowableWheel.dll` | 已部署 |
| 7 | `CombatTweaks` | `er2.combattweaks` | ER2 Combat Tweaks | **1.2.2** | `ER2_CombatTweaks.dll` | 已部署 |
| 8 | `ZoomAnywhere` | `er2.zoomanywhere` | ER2 Zoom Anywhere | **1.0.1** | `ER2_ZoomAnywhere.dll` | 已部署 |
| 9 | `HighValueTarget` | `er2.highvaluetarget` | ER2 Veteran HVT | **1.2.1** | `ER2_VeteranHVT.dll` | 已部署（+ Assets 目录） |
| 10 | `InventoryPause` | `er2.inventorypause` | ER2 Inventory Pause | **1.0.5** | `ER2_InventoryPause.dll` | 已部署 |
| 11 | `SquadCommand` | `er2.squadcommand` | ER2 Battlefield Commander | **1.2.15** | `ER2_BattlefieldCommander.dll` | 已部署 |
| 12 | `UniversalGeneration` | `er2.universalgeneration` | ER2 Universal Generation | **1.0.10** | `ER2_UniversalGeneration.dll` | 已部署 |
| 13 | `UnitCollision` | `er2.morephysics.unitcollision` | ER2 More Physics - Unit Collision | **1.0.8** | `ER2_MorePhysics_UnitCollision.dll` | 已部署 |
| 14 | `UnitInfoOverlay` | `er2.unitinfooverlay` | ER2 Unit Inspector | **1.0.5** | `ER2_UnitInfoOverlay.dll` | 已部署 |
| 15 | `FleshWoundsFixed` | `ER2_FleshWounds` | ER2 Flesh Wounds | **1.0.1** | （需手动构建部署，build.ps1 无条目） | 第三方修复版 |
| 16 | `MorePhysics` | `er2.morephysics` | ER2 More Physics | **0.1.49** | `ER2_MorePhysics.dll` | 复活（本地未部署，发 Nexus） |
| 17 | `Conquest` | `er2.conquest` | ER2 Conquest | **0.2.0** | `ER2_Conquest.dll` | **开发中**（战略层对齐 GoH 完整化，自检 15/15；M3 战斗桥接默认关闭待实测） |
| — | `HvtTestDriver` | `er2.hvt.testdriver` | HVT Test Driver | **1.0.0** | — | **内部自测工具，不发布** |
| — | `ConquestRecon` | `er2.conquest.recon` | ER2 Conquest Recon | **0.1.0** | `ER2_ConquestRecon.dll` | **内部侦察工具（M0），不发布** |

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

### 2.5 ModManager `er2.modmanager` v1.5.2
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

### 2.9 HighValueTarget（Veteran HVT）`er2.highvaluetarget` v1.2.1
**击杀归属**（`BulletInstance.OnHit` Prefix / 爆炸 `responsible` / 近战上下文 / `ItemHelmet.User` 兜底 + 玩家命中窗口 5s 防抢）+ **阈值标记**（敌 5 杀=HVT 橙标、友 2 杀=叛徒红标，阵营按 faction 的 `_` 后缀判定，跨国家同盟算友军）+ **仇恨聚焦**（覆盖 `Soldier.GetBestVisibleEnemy`，走原生目标选择）+ **标记强化**（减伤 0.6/加伤 1.4/命中 1.7/射速 1.4/移速 1.15/无视压制/不投降）+ **叛徒机制**（玩家可伤友军 + 友军集火玩家，穿透 CombatTweaks 友军保护）+ 玩家提示（Hint + 红屏 + 顶部常驻指示）+ M 大地图图标（跟随 `unitsContainer` 游戏标记，载具去重）+ **3D 头顶标记（v1.2.0）** + **载具乘员击杀共享（v1.2.0）**。
**v1.2.0（2026-09-13，玩家两点反馈）**：① **头顶标记改 3D 世界空间**——IMGUI 屏幕投影改为 billboard quad（`MeshFilter`+`MeshRenderer`，材质 `Shader.Find("Sprites/Default")`，回退 Unlit/Transparent，全缺置 `Marker3dUnavailable` 走旧 IMGUI `DrawMarkersScreen` 回退路径；quad 显式写入白色顶点色——Sprites/Default 片元色=纹理×顶点色，缺省属性不可依赖）。锚点步兵 +3.0m、载具 = 碰撞体最高点+0.9m（`VehicleTopOffset` 按载具缓存，排除 >30m 的巨型触发体）；**恒定屏占比 `scale=dist×0.03`**（约 28px @1080p/FOV60，全程无拐点）。每帧 `LateUpdate`（billboard/位置/缩放），对象池对账在 0.5s Tick（`RefreshDisplayEntries` 快照 + `ReconcileMarkers` 补建/回收）。贴图 32→128px + 3x3 超采样。门控与旧版一致：暂停/M 地图/Hide Anything 勾选/MarkerRange；**遮挡=深度测试自然消隐**（ZTest LEqual，被完全挡住时消失，无手动 raycast）。② **载具乘员合并标记 + 击杀共享**——乘员枚举走 `Soldier.GetCurrentVehicle()` + `Vehicle.seats[i].unitSet`（interop 正道，实测）；`OnUnitKilled` 归属到射手后若在载具内 → 全体乘员各 `CreditKill` 一次（等级同步提升；玩家命中窗口兜底排除已是乘员的玩家防双计）；显示层 `RefreshDisplayEntries` 把载具乘员合并为一条目（等级取乘员最高），3D 标记与 M 大地图图标共用该快照 → 一载具一枚标记。
**v1.2.0 追加修复（不升号，玩家实测三连反馈）**：① **近距离缩放不自然**——旧 `max(0.55m, dist*k)` 下限导致 18m 内标记停止缩小、屏占比膨胀；改纯 `dist×0.03` 恒定屏占比（原生 Marker3DGUI 有 `MarkerScreenSize()` 转换接口，同为恒定屏占比设计；interop 只有桩体，原生逻辑读不到，按设计意图对齐）。② **标记显示为白色菱形（根因定案）**——`GUI/Text Shader` 是**字体着色器**：RGB 取自材质 `_Color`、纹理只提供 alpha（property 名即暗示：`_MainTex ("Alpha (A)")` / `_Color ("Tint (RGB)")`），烘焙进纹理的深红/蓝底色全被无视。**教训：世界空间 quad 要显示纹理本色别用 GUI/Text Shader，用 Sprites/Default（片元=纹理×顶点色+片元内预乘，行为确定性最高）。** ③ **关闭叛徒机制仍被标叛徒**——根因是**归属记录污染**：友军伤害被友军保护拦截（不掉血 ✓）但 `RecordHit` 照写命中记录，友军稍后死于炮击/AI 互射时残留记录把死亡误归玩家 → 友军击杀 +1 → 标叛徒。修三处：`CreditKill` 友军分支顶部加 `TraitorFeature` 门控（关闭=友军击杀完全不计数不提示）；`RecordHit` 机制关闭时不记同方命中（源头堵住）；新增 `PardonTraitorsWhenDisabled` 每 tick 赦免现有叛徒状态（关闭开关=立即生效）。
**关键陷阱（实测定案）**：① `Bullet.BulletDamage` 在本作**不被调用**——入口必须实测，反编译有 ≠ 会调；② **归属必须 Prefix 记录**（瞬杀爆头的死亡发生在原生 `OnHit` 内部，Postfix 记录晚于 `Kill` 事件 → "爆头不计数"根因）；③ FF 放行的阵营改写会污染 `SideOf` 判定；④ `ForceTarget` 强制态三宗罪（鞭尸/打断射击/站桩）→ 弃用改覆盖 `GetBestVisibleEnemy`；⑤ **头盔是独立于士兵层级的 BodyPart**（`GetComponentInParent<Creature>` 为 null → 用 `ItemHelmet.User`）；⑥ **IL2CPP 托管 MonoBehaviour 必须先 `ClassInjector.RegisterTypeInIl2Cpp<T>()` 再 AddComponent**；⑦ 击杀归属表 25s 窗口 + `CountedDeaths` 防 `Kill`/`KillSynched` 双计；⑧ 坦克乘员各自击杀 → 每人头上一个标记（v1.2.0 前的老问题），乘员共享击杀后等级同步 + 显示按载具合并；⑨ **世界空间 quad 别用 GUI/Text Shader**（字体着色器：RGB=材质色、纹理只出 alpha → 纹理本色全丢变白；用 Sprites/Default，见 v1.2.0 追加修复②）；⑩ **被拦截的伤害仍污染归属记录**（友军伤害被拦不掉血但 RecordHit 照写 → 友军死于他因被误归玩家标叛徒，见 v1.2.0 追加修复③）。
**Assets**：`HighValueTarget/Assets` → 部署到 `plugins/ER2_VeteranHVT/`（build.ps1 的 `assetsDir`）。

**v1.2.1（2026-09-13，为征服模式加的公开接口）**：新增 **`public static class VeteranApi`**（`ER2VeteranHVT.VeteranApi`）——老兵系统的**公开门面**，供其他 mod 读写老兵等级/击杀。成员：`ApiVersion`(const=1) / 属性 `MaxLevel`·`KillsPerLevel`·`IsActive` / 读 `GetLevel`·`GetKills`·`IsMarked` / 写 `SetKills`·`SetLevel` / 班批量 `ApplySquadLevel(Squad,int)` / 班回读 `GetSquadKills`·`GetSquadMaxLevel`·`GetSquadAliveCount`·`GetSquadSize`。**全部方法自带 try/catch，绝不向调用方抛异常**。语义：等级由击杀推导（`EnemyKills / KillsPerLevel`，封顶 `MaxLevel`）；`SetLevel` 是"灌等级"（等级→反推击杀写入），**只应在单位刚生成、尚未参战时调用**。
**为什么需要它**：HVT 内部的 `LevelOf`/`TryGetState`/`UnitStates` 都是 `internal`，外部只能反射内部实现（不稳定，HVT 一重构就断）→ 把耦合点收敛到一个 public 类；且 HVT 自己有 `BattleEndResetPatch`（每场战斗结束重置等级），而**跨战斗的持久军队**（如征服模式）必须能把等级灌回来。**本版无玩法改动**，接口在无人调用时是惰性的。消费者：`Conquest`（见 §2.18，走 `Conquest/Game/VeteranLink.cs` 反射对接，无编译期依赖）。
**陷阱（本次踩到）**：`Squad` 的成员列表字段是 **`units`**（`Il2CppSystem.Collections.Generic.List<Soldier>`），**不是** `soldiers`；班成员数用 `CountMembers` 属性。写对接代码前先 `ilspycmd -t Squad` 核对字段名。

### 2.10 InventoryPause `er2.inventorypause` v1.0.5
打开背包（自己/尸体）时**真暂停**（延迟 timeScale 冻结，等打开动画完成）；暂停期间丢弃道具自动落地（扫描 `ItemObject.spawnedItems`）。
**ER2 暂停机制图谱（全部实测定案，做任何暂停功能前必读）**：原生 `Pause.SetPause` = timeScale=0 + 弹菜单 + `disableOnPause`（藏菜单 = 死锁）；手动 `Pause.isPaused=true` 禁用输入但**不冻结世界**；`timeScale=0` 真暂停但**卡 UI 协程动画** + 丢弃武器浮空（解法：延迟冻结等动画完成 + 扫描 `spawnedItems` 拉下道具）；`enableAiBehaviour(false)` **无效**（true 才有效）；背包开关读 `InventoryPanel.isOpen`。

### 2.11 SquadCommand（Battlefield Commander）`er2.squadcommand` v1.2.15
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
① **幽灵可能残留在场上**（用户反馈"即便取消右键长按"）→ 审计出多条泄漏路径：`ResetRightGesture` 只清 `formationDragActive`、**没清 `Formation.dragging` 与幽灵**；松手时若鼠标在 UI 上（`guiNow`）直接跳过、既不执行也不清理；退出 RTS 的路径同样只置标志。现在新增 **`Formation.CancelDrag(reason)`**（唯一的中断入口：置标志 + 清计划 + `GhostPreview.ClearAll()`），并在**所有**结束路径调用：手势复位、UI 上松手、退出 RTS；另加**看门狗**（拖动中若"已退出 RTS"或"右键已松开"→ 立即取消），兜住所有异常路径。
② **组件遍历合并（性能）**：`Ghostify`/`MakeGhost`/`DetachAndPacify`/`UnregisterGhost` 原来各做 5~8 次 `GetComponentsInChildren`，现合并为**单次遍历**（`ProcessComponents` 一次处理 Collider/Rigidbody/Joint/Camera/Light/Renderer/Behaviour）；`Apply` 的"计划外幽灵"比对由 O(n×m) 改 HashSet；幽灵指针表加 512 上限防无界增长。
③ **英文版发布**：`-Mod SquadCommand`（EN 默认包）构建部署并打包 `ER2_BattlefieldCommander_v1.2.15.zip`；`ER2_UniversalGeneration_v1.0.10.zip` 同步。验收：双端 sha256 一致 ✓、反编译确认 `Ui.Tr` 走英文字典（EN 构建）✓、zip 内 DLL+README+Nexus_description 齐全 ✓。
### 2.12 UniversalGeneration `er2.universalgeneration` v1.0.10
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
| `ER2_LeaveRedeploy`（第三方） | 曾在死亡时接管"小队选择→部署界面"流程（隔离验证用） |

## 4. 发布包状态（`C:\Users\71011\Downloads\`）

最新一批（2026-09-06）：

| 包 | 时间 |
|---|---|
| `ER2_BattlefieldCommander_v1.1.0.zip` / `ER2_BattlefieldCommander_CN_v1.1.0.zip` | 09-06（**最新**） |
| `ER2_UniversalGeneration_v1.0.0.zip` / `ER2_UniversalGeneration_CN_v1.0.0.zip` | 09-06 |
| `ER2_ModManager_v1.5.2.zip` | 09-18（**最新**：删掉自锁的开发开关 `NativeFull` + 整文件删除 `NativePage.cs`，MODS 页只剩一条建页路径；拆包核对 = DLL + README.txt + Nexus_description.md） |
| `ER2_ModManager_v1.5.1.zip` | 09-18（历史：删除遗留 PoC 测试页 + `NativePoc` 开关；新增 `Debug`/`debugLog` 开关，默认关） |
| `ER2_ModManager_v1.5.0.zip` / `ER2_ModManager_CN_v1.5.0.zip` | 09-12（历史：点击不再白闪 / 三层可见性 / 分区默认收起+箭头 / 标签统一字号+纯文本 / 值框 130×24 / 行内缩按滚动条绝对目标） |
| `ER2_ModManager_v1.2.0.zip` / `ER2_ModManager_CN_v1.2.0.zip` | 09-05（历史） |
| `ER2_UnitInfoOverlay_v1.0.5.zip`、`ER2_HideAnything_v4.5.4.zip`、`ER2_WeatherControl_v1.7.2.zip`、`ER2_LimbTweaks_v2.13.101.zip` | 09-05 |
| `ER2_VeteranHVT_v1.2.0.zip`（EN，**当前部署**）/ `ER2_VeteranHVT_CN_v1.2.0.zip`（CN） | 09-13（**最新**：头顶标记改 3D 世界空间 billboard（恒定屏占比 + 深度遮挡）+ 载具乘员击杀共享/一载具一标记 + 关闭叛徒机制完全失效；**本次补齐 `README_CN.txt`/`Nexus_description_CN.md`** → CN 包文档首次为中文；拆包核对 = DLL + README.txt + Nexus_description.md + bf1_kill.wav；EN/CN 包 DLL sha256 不同（`-Cn` 带 `CN_BUILD`）） |
| `ER2_MorePhysics_UnitCollision_v1.0.8.zip`、`ER2_MorePhysics_v0.1.49.zip` | 09-13（**热开关修复**：矩阵开→关即时恢复原状，不再"关了还有碰撞"；轻量版另修软推开关细分；拆包核对均 = DLL + README.txt + Nexus_description.md） |
| `ER2_CombatTweaks_v1.2.2.zip`（+CN）、`ER2_InventoryPause_v1.0.5.zip`（+CN） | 08-25/26 |

**历史清理**：SquadCommand 全部中间版本包与暂存目录已删（只留 1.1.0 双语）；工作区根目录 `EasyRed2_BepInEx_Dependencies.zip` 已删。
**2026-09-13 大清理（玩家要求）**：Downloads 里积压的全部**被取代旧版本包 + build.ps1 打包暂存目录**已删 —— 旧包 34 个（HVT 旧名 `ER2_HighValueTarget_v1.0.0/1.1.0/1.1.1` 及 CN 共 6 个、`ER2_VeteranHVT_v1.1.26` 双包、CombatTweaks v1.2.1 双包、HideAnything v4.5.2 双包、ModManager v1.1.1/v1.1.2/v1.2.0 + CN v1.1.1~v1.4.0 共 13 个、UnitInfoOverlay v1.0.0~v1.0.4 + CN v1.0.1~v1.0.4 共 9 个）+ 暂存目录 23 个（`ER2_*` 目录，build.ps1 生成 zip 后不自动清理，下次构建会重建）。**现 Downloads 只保留各 mod 最新版共 18 个包**。**注意：build.ps1 每次打包都会在 Downloads 留下 `ER2_<pkg>` 暂存目录且不自动删——定期清理或忽略即可。**

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

