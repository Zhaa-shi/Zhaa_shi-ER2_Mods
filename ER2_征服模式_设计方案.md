# ER2 征服模式（Conquest）设计方案

> **⚠️ 2026-09-19：本项目终止**（用户决定）。战略层（省/回合/研究树/四资源）复杂度失控、五轮 UI 返工仍难懂、最高风险的战斗桥接（M3/M4）始终未实测。
> 本文档**保留为完整复盘与侦察档案**——`§14` 五轮实测教训、`§2` API 侦察结论全部有效。
> **资产回收**：`UI/MenuEntry.cs`（入口按钮）、`NativeTabHost`、`InputBlocker`、`UiTheme`、`NativeAssets`、`VeteranLink`、Core 的存档/RNG/自检宿主、`research_out/conquest_recon/` 侦察产物——全部由 **无尽模式（`ER2_无尽模式_设计方案.md`）** 直接继承。
> **源码已删除（同日）**：`Conquest/`、`ConquestRecon/` 工作区目录已移除，游戏内 DLL/cfg 已卸载；完整源码快照在 `research_out/conquest_salvage/`（回收清单见其 README）。

> 原目标：把《Gates of Hell: Ostfront》的**征服模式（Conquest / Dynamic Conquest）**完整搬进 Easy Red 2。
> 入口：主菜单按钮列表里，**「战役」与「多人」之间**新增一项「征服模式」。
> 拟建 mod：目录 `Conquest/`，GUID `er2.conquest`，显示名 `ER2 Conquest`，DLL `ER2_Conquest.dll`。
> 本文件是**方案**（未动手）。所有 API 断言均来自本机 interop 反编译（`research_out/conquest_recon/`），标注了证据文件。

---

## 0. 一句话定位

ER2 原生是「一场一场打」；征服模式给 ER2 加**战略层**：
**持久化的军队 + 领土 + 资源 + 回合制推进**，战斗只是战略层的一次结算事件。

这决定了两条铁律：
1. **战略层是这个 mod 的本体**，战斗层是它的执行器。先让「回合→资源→军队→领土」循环跑通，再谈战斗多还原。
2. **绝不重做 ER2 的战斗系统**。战斗尽可能走原生管线（原生地图、原生 AI、原生目标区、原生结算），mod 只负责「派谁上场」和「打完之后怎么算」。

---

## 1. 原版征服模式拆解（要还原的机制清单）

《地狱之门》的征服模式（官方描述："Conquer enemy territories in randomized skirmish battles"）由六层组成：

| # | 层 | 原版机制 | 优先级 |
|---|---|---|---|
| 1 | **战略地图** | 战区地图分省份/区域；每省有归属方与驻军；省份相邻关系决定可进攻方向 | P0 |
| 2 | **回合制推进** | 玩家回合：选一个相邻敌省进攻（或调动/整补）；AI 回合：AI 从相邻省反攻，玩家打防守战 | P0 |
| 3 | **资源** | 每回合按控制领土产出（人力/补给）；资源用于买新单位、补充战损、修复载具 | P0 |
| 4 | **持久军队** | 单位是**实体**：有番号、有老兵等级、会伤亡、会补充；被打光就永久消失 | P0 |
| 5 | **战斗派兵** | 出击时从军队里挑单位（受点数/单位上限约束），战斗中被击毁的单位从军队里扣除 | P0 |
| 6 | **研究/解锁** | 用研究点解锁新单位类型（按年份/战役进度推进科技） | P1（v1 用「年份+胜利数」近似，不做研究树） |

参考：[Gates of Hell Wiki · Game Modes](https://gatesofhell.fandom.com/wiki/Game_Modes)、[Dynamic Conquest](https://gatesofhell.fandom.com/wiki/Dynamic_Conquest)（"the player must research their own units in order to call them into the battle"）。

**ER2 化的关键差异**（必须提前定，见 §10）：GoH 战斗是 RTS 视角，ER2 是 FPS。玩家在征服战斗里是**一名士兵**，还是**RTS 指挥官**（可复用 SquadCommand 的上帝视角），这是最大的体验分叉。

---

## 2. ER2 侧技术落点（反编译证据）

### 2.1 主菜单入口 —— 可行

`MainMenu`（`conquest_recon/MainMenu.decompiled.cs`）是 tab 式主菜单，**全部字段 public**：

| 成员 | 说明 |
|---|---|
| `MainMenu.instance` | 静态单例（主菜单场景） |
| `mainMenu_tab` / `camaigns_tab`（游戏自己的拼写错误）/ `multiplayer_tab` / `settings_tab` / `credits_tab` / `statistics_tab` / `missioneditor_tab` | 各页内容 GameObject |
| `currentTab`、`cameraPositions` | 当前页 / 每页相机位 |
| `OpenTab(GameObject, bool)` / `OpenTabCR(GameObject, float, bool)` / `CloseAllTabsNow()` / `SetCameraMenuPos(GameObject)` | 切页 |
| `SpawnNotification(string, MenuNotification.NotificationType)` | 主菜单原生通知 |
| `LoadMap(string)` / `LoadMainMenu(bool)` | 场景加载 |

**做法**：`mainMenu_tab` 里是按钮列表（战役/多人/设置/统计/制作人员…）。启动时 dump 一次层级（子物体名 + `Button` + `Text` + `onClick` 目标），定位「战役」按钮 → `Instantiate` 克隆 → 改文本为「征服模式」→ `SetSiblingIndex` 插到「战役」与「多人」之间 → 换掉 `onClick` 回调指向本 mod 的征服页。
**M0 必须先做 dump 实测**（按钮是不是可克隆的原生模板、点击回调挂在 `Button` 还是自定义组件上——反编译看不到，只能实测）。

### 2.2 战斗启动链路 —— 可行

| API | 证据 | 用途 |
|---|---|---|
| `CampaignsTree.StartMissionCR(IBattle, Transform, Transform)`（static IEnumerator） | `CampaignsTree.decompiled.cs` | **原生「开打」协程**（战役树点开战就是它） |
| `CampaignsTree.StartMission(Transform, Transform)`（static） | 同上 | 同上（无参 battle 版） |
| `CampaignsTree.GetBattleLoaderFromCachedBattleData(ICampaignTreeBattleData, BattleData)` | 同上 | battle 数据 → 可加载对象 |
| `CampaignTreeCustomBattleData{path, filename, workshop_id, battleDate}` + `LoadBattle()` / `LoadBattleAsync(cb)` / `GetContentPath()` | `conquest_recon/CampaignTreeCustomBattleData.decompiled.cs` | **自定义战斗加载器**：给路径就能加载一个 `.mer2` 战斗 |
| `CampaignTreeVanillaBattleData{battlePrefab, allow_multiplayer, allowFullCampaign}` | `conquest_recon/` | 原生战役战斗加载器 |
| `IBattle`：`GetBattleData()` / `GetBattleID()` / `GetBattleName()` / `GetMapName()` / `GetMapToLoadName(ref string)` | `IBattle.decompiled.cs` | 战斗元数据 |

**关键结论**：mod 可以**自己造一个战斗**（见 2.3），存成 `.mer2`，再用 `CampaignTreeCustomBattleData` + `StartMissionCR` 走**完全原生的加载/开局路径**（原生 LoadingScreen、原生 mod/地图依赖处理、原生 Lua 脚本、原生阶段与目标区）。不需要自己写场景加载。

### 2.3 战斗数据结构 —— 这是「完全体」的地基

`.mer2` 是 **BinaryFormatter 序列化的 `MissionEditorBattleData.MissionEditorBattle`**（已实测文件头：`...FAssembly-CSharp...MissionEditorBattleData.MissionEditorBattle...vphases...placed_objects...customSquads...`），由任务编辑器产生，落在
`Easy Red 2_Data/StreamingAssets/Missions/ME_<名>_<id>/<同名>.mer2`（本机已有 **206** 个，含创意工坊内容）。

`MissionEditorBattle` 的字段与方法（`conquest_recon/MissionEditorBattleData.MissionEditorBattle.decompiled.cs`）：

- 字段：`vphases`（阶段表）、`placed_objects`、`customSquads`、`startPhase`、`forcedInvadersFaction` / `forcedDefendersFaction`、`invadersIsAllies`、`fileNameID`、`workshopDownloadsPath`、`battleData`
- 方法：`AddCustomSquad(int, CustomSquad)` / `FindCustomSquad(string)` / `AddMissionEditorObject(...)`、**`SaveBattleDataToFile(ref string, string)`**（游戏自己的存盘）、`SaveWithoutUpdatingDetails(string)`、`GetSavePath(string,string)`、`Unpack(BattleManager, ref List<RoomObjectSpawnData>, bool)`、`GetMapToLoadName(ref string)`

阶段/生成点/目标区（`MissionEditorBattleData.MissionEditorPhase*`）：

| 类型 | 字段 |
|---|---|
| `MissionEditorPhase` | `unit_spawns` / `vehicle_spawns` / `conquer_areas` |
| `MissionEditorPhaseUnitSpawn` | `faction`、`faction_id`、`squad`、`squad_id`、`custom_squad_id`、`destination`、`spawnRadiusX/Y`、`respawn_delay`、`persistent`、`forceFirstSpawn`、`spawnOnVehicle`、`ai_script_file`、`dog_tag_hero_id` |
| `MissionEditorPhaseVehicleSpawn` | `vehicle_id`、`destination`、`camoId` |
| `MissionEditorPhaseObjectiveArea` | `objectiveName`、`objectiveRadius`、**`secureTime`**（占领所需时间） |

→ **征服战斗可以用 mod 在运行时「合成」**：阶段 0 里塞「玩家派出的部队（我方 spawns）+ 省份驻军（敌方 spawns）」+「若干 `conquer_areas` 目标区（带 `secureTime`）」，然后 `SaveBattleDataToFile` 落盘 → `CampaignTreeCustomBattleData` 加载 → `StartMissionCR` 开打。
这等价于「用游戏的关卡格式写关卡」，而不是硬编码刷兵。

`BattleData`（基类，`BattleData.decompiled.cs`）字段：`_map`/`map`、`battle_id`、`invadersFaction(_id)`/`defendersFaction(_id)`、`invadersTickets`、`maxAllies`/`maxAxis`、`gameMode`、`location_name(_id)`、**`latitude` / `longitude`**、`year`/`month`/`day`/`TimeHH`/`TimeMM`、`weatherType`/`seasonType`、`*Batalion(_id)`、`previousMission_id`、`addTicketsOnConquer`、`neededMods`/`neededDLCs`。
→ **每个战斗自带经纬度**：战略地图的省份节点可以直接按真实地理坐标布局（§6）。

### 2.4 省份/战斗内容池 —— 零成本

| API | 证据 | 用途 |
|---|---|---|
| `VanillaCampaignsList.VisibleVanillaCampaigns`（static `IEnumerable<CampaignTreeData>`） | `conquest_recon/VanillaCampaignsList.decompiled.cs` | **枚举全部原生战役** |
| `CampaignTreeData{campaing_id, battles, top_bar_id, ugcType, author, needed_dlc, hidden_if_not_owned, one_life_ach}` | `conquest_recon/CampaignTreeData.decompiled.cs` | 战役 = 一个**战区**；`battles` 是战斗列表 |
| `ICampaignTreeBattleData.LoadBattle()` → `IBattle.GetBattleData()` | `ICampaignTreeBattleData.decompiled.cs` | 取每个战斗的完整元数据 |
| `CustomCampaignsList` / `CampaignMenuInjector` / `MissionLister.ListMissions(DisplayCampaign[], int, bool)` | `conquest_recon/` | 自定义/工坊战役枚举（可作扩展省份源） |

→ **v1 的省份池直接由原生战斗表生成**：一个原生战役 = 一个战区（诺曼底 / 斯大林格勒 / 阿登 / 中国 / 觉醒…），一场原生战斗 = 一个省份（地图、日期、阵营、双方上限、经纬度全齐）。100+ 省份，**零内容制作**。

### 2.5 战斗内：派兵 / 战果 / 老兵

| API | 证据 | 用途 |
|---|---|---|
| `SpawnManager.SpawnAISquadGlobal(string faction, string faction_id, SquadData sd, Vector3 pos, float radius, Vehicle spawnOnvehicle, Action<Squad> cb, int -1)` | `SpawnManager.decompiled.cs` + `UniversalGeneration/GenRunner.cs:387` | 运行时生成整班（**返回原生协程，必须显式 StartCoroutine**，陷阱 25） |
| `ItemsDatabase.GetSquadLoadouts(SquadType, int)` | `UniversalGeneration/GenCatalog.cs:215` | 兵种/编成目录（→ 可购买单位表） |
| `CustomSquad(string, string[])` + `AddMember(int, CustomSquadMember)`；`CustomSquadMember{weap1_id, weap2_id, weap1_scope, weap1_bipod, weap1_bayonet, uniform_id, headgear_id, vest_id, loadout_type, otherItems_ids}` | `CustomSquad.decompiled.cs` / `CustomSquadMember.decompiled.cs` | 自定义班（逐人装备）→ 「军队里的单位」可精确定义 |
| `SquadData{squadCode_id, squadName, loadouts}` / `SquadDataTable` / `SquadsArchive` / `LoadoutsArchive` | `research_out/` | 编成目录 |
| `BattleManager.IsBattleEnded()` / `IsBattleActive()` / `GetCurrentWinnerFactionBasedOnCurrentPhase()` / `GetEndBattleGUI()` / `OnWin(string, bool)` / `SetPhase` / `NextPhase` / `SetTickets(float, BattleData)` / `TakeControlOfSpawned(SpawnManager, int)` / `GetBorder(int)` / `GetBorderCenter(int)` | `BattleManager.decompiled.cs` | **战斗结算与目标区读数的原生挂点** |
| `BattleResults{winnerFaction, playerFaction, connectedBattle}` | `BattleResults.decompiled.cs` | 战果 |
| `GameProgresses.CompleteLevel(string, bool, int)` / `IsLevelCompleted(string)` | `GameProgresses.decompiled.cs` | 原生进度（我们不写它，但可读） |
| 击杀归属（`BulletInstance.OnHit` Prefix + 爆炸 `responsible` + 玩家命中窗口） | **HVT 已实测定案**（`HighValueTarget/Plugin.cs`） | 「哪个单位拿了击杀」→ 老兵经验 |
| 老兵等级（击杀数 → 等级 → 伤害/精度/射速/移速倍率） | **HVT 已实现**（`LevelOf`/`Pow`） | 直接复用这套「mod 侧老兵」模型，**不依赖游戏 Rank** |

### 2.6 存档 —— 用游戏自己的通道

`SaveDataManager`（`SaveDataManager.decompiled.cs`）是 public static 的通用序列化工具：

- `SaveToFile<T>(T, string, string, SerializationMethod)` / `LoadFromFile<T>(string, ref T, string, SerializationMethod)`
- `ObjectToJson<T>` / `JsonToObject<T>` / `SerializeToString<T>` / `DeserializeFromString<T>`
- `GetPersistentPathTo(string, string)`、`persistentSavePath` / `persistentModsPath` / `persistentMissionEditorPath`（= `%LocalLow%\CorvoStudio\Easy Red 2\{saves,mods,mission_editor}`）

→ 征服存档 = **mod 自己的 DTO（纯 C# 类）+ JSON**，写进游戏持久化目录（多存档位 = 多文件）。**绝不碰 `saves/gamedata.er2`**（那是 BinaryFormatter 的 `SavableData`，动它等于毁档）。

### 2.7 UI / 观感

- 本工作区已定案的 ER2 大 UI 范式 = **IMGUI + 原生字体 + `GuiExtension.OutlinedLabel` 描边**（SquadCommand 的上帝视角面板、InfoPanel 全是这套，`SquadCommand/GodViewController.cs`、`InfoPanel.cs`），通知走原生 `Hint.Display`。
- 战略地图需要「一张图 + 若干节点」：**IMGUI 自绘最省事且可控**（`ER2_UI_design.md` 的回退链：原生 Hint → 原生字体+描边 → 裸 GUI.Label）。
- 需要按 `Hide Anything` 契约接入（`Shared/NoHintsHudLink.cs`，勾选制，**并在 `HudCompat.knownNames` 预注册**）。
- 陷阱：`new GUIStyle()` 默认黑字（11）、运行时 Texture2D 必须 `hideFlags=61`（12）、节流一律 `Time.unscaledTime`（20）、`BattleManager` 在主菜单也存在（24）。

---

## 3. 总体架构

```
Conquest/                                 er2.conquest  ER2 Conquest  ER2_Conquest.dll
├── Plugin.cs                  入口：Harmony 挂点、生命周期、配置项
├── Menu/
│   ├── MenuDump.cs            M0 诊断：dump mainMenu_tab 层级（一次性，验证后删）
│   └── ConquestEntry.cs       克隆「战役」按钮 → 插入「征服模式」→ 打开征服页
├── Core/                      战略层（纯 C#，不依赖 Unity 场景，可单测）
│   ├── ConquestCampaign.cs    战役状态根：战区/省份/回合/资源/军队
│   ├── Province.cs            省份：id、名、地图、lat/lon、归属、驻军、邻接
│   ├── ProvinceGraph.cs       邻接生成（地理距离 + 战区归属）
│   ├── ArmyRoster.cs          军队：单位实体（番号/兵种/老兵/编制人数/损伤）
│   ├── UnitCatalog.cs         单位目录（SquadType/载具 → 成本/解锁年份/编制）
│   ├── ResourceLedger.cs      每回合收支
│   ├── TurnResolver.cs        玩家回合 → AI 回合（AI 选目标、兵力对比、自动结算）
│   └── ConquestSave.cs        DTO + JSON 存档（SaveDataManager 通道，多存档位）
├── Battle/
│   ├── BattleFactory.cs       ★合成 MissionEditorBattle（阶段/生成点/目标区/驻军）
│   ├── BattleLauncher.cs      .mer2 落盘 → CampaignTreeCustomBattleData → StartMissionCR
│   ├── BattleBridge.cs        战斗内：玩家部队注入、目标区跟踪、结算判定
│   ├── RosterSpawner.cs       用 SpawnManager.SpawnAISquadGlobal 投送编成（UniGen 范式）
│   └── BattleOutcome.cs       战果 → 伤亡/经验/领土/资源 回写战略层
├── UI/
│   ├── ConquestRoot.cs        征服页根：主菜单入口 → 战区选择/读档/新战役
│   ├── StrategyMapView.cs     战略地图（节点+连线+势力色+驻军+回合）
│   ├── ArmyView.cs            军队管理（单位列表/老兵/战损/补充/新购）
│   ├── DeployView.cs          出击编成（选单位、点数校验、进攻/防守）
│   ├── BattleHud.cs           战斗内薄条 HUD（回合/目标区/剩余兵力）
│   └── UiTheme.cs             原生字体/描边/配色/按钮（照抄 SquadCommand 范式）
└── Content/
    ├── provinces.json         战区+省份表（M0 由 dump 工具生成，之后人工润色）
    ├── units.json             单位表（兵种/载具 → 成本/年份/上限）
    └── ai_templates.json      AI 行为模板（进攻倾向/难度系数）
```

**分层原则**：`Core/` 不引用任何 `Il2Cpp*`/Unity 类型（纯 POCO + 数学），这样战略层逻辑能脱离游戏验证；`Battle/` 是唯一与游戏战斗系统耦合的层；`UI/` 只读 Core、只调 Battle 的入口。

---

## 4. 核心流程

```
主菜单
  └─[征服模式] → 征服页
       ├─ 继续战役（读档列表）
       └─ 新建战役：战区(原生战役) / 阵营 / 难度 / 起始年份
            ↓
       战略地图（回合 N，玩家回合）
       ├─ 点省份 → 看归属/驻军/地形/日期
       ├─ 点相邻敌省 → 出击编成（选单位，点数上限）→ 进攻
       ├─ 军队页 → 买新单位 / 补充战损 / 升级老兵（花资源）
       └─ 结束回合
            ↓
       AI 回合：AI 从相邻省反攻 → 生成「防守战」待办
            ↓
       战斗（原生加载 → 原生地图 → 我方部队按编成入场 → 目标区争夺）
            ↓
       战斗结算（胜负 / 单位伤亡 / 击杀经验）
            ↓
       回战略地图：领土易主、资源结算、老兵升级、战损扣除 → 存档
```

**战斗结束后如何回到征服页**：不跟原生返回流程打架。mod 状态挂在 BepInEx 插件实例上（跨场景存活），战斗结束后原生照常回主菜单，我们在主菜单场景 `Update` 里检测「有未结算的征服战斗」→ 自动重开征服页并弹出结算面板。这条兜底路径最稳（原生 `EndBattleGUI` 的后续跳转不透明）。

---

## 5. 战斗层设计（本方案最关键的部分）

### 5.1 三条路线，先兜底再完全体

| 路线 | 做法 | 风险 | 阶段 |
|---|---|---|---|
| **B（兜底）** | 不造战斗：直接用该省份对应的**原生战斗**开打，战斗开始后用 `SpawnManager.SpawnAISquadGlobal` 把玩家编成投送到我方出生区，用 mod 侧逻辑跟踪「目标区/胜负」 | 低（UniGen 已验证投送；原生战斗加载是原生路径） | M3 |
| **A（完全体）** | 合成 `.mer2`：阶段表 = 玩家编成 + 驻军，`conquer_areas` = 征服目标区（带 `secureTime`），`SaveBattleDataToFile` 落盘 → 原生加载 | 中（`SaveBattleDataToFile` 的可用性/校验需实测） | M4 |
| C | 用任务编辑器预先生成若干战斗当模板，运行时只改数值 | 低但内容僵硬 | 备选 |

**推荐节奏：M3 走 B 把整条循环跑通，M4 上 A 换掉战斗内容层**——两条路共用 `BattleBridge`/`BattleOutcome`，切换成本可控。

### 5.2 征服语义 → 战斗参数的映射

| 征服概念 | 战斗实现 |
|---|---|
| 省份 | 战斗的 `map` + `location_name` + 日期/天气（全部来自原生 `BattleData`） |
| 玩家派出的单位 | 我方 `MissionEditorPhaseUnitSpawn`（`faction` = 玩家阵营，`squad_id`/`custom_squad_id`，`destination` = 我方出生区，`persistent=false` 表示不自动补员） |
| 省份驻军强度 | 敌方 spawn 的**班数 × 老兵等级**（驻军 3 = 3 个班，Lv.2 驻军 = 生成时按 HVT 老兵倍率强化） |
| 单位上限 | `BattleData.maxAllies`/`maxAxis` 与编成点数校验 |
| 目标区 | `MissionEditorPhaseObjectiveArea{objectiveName, objectiveRadius, secureTime}`（占领 = 控制该区域） |
| 增援波次 | 多阶段 `vphases`（阶段推进 = 波次） |
| 胜负 | 原生 `BattleManager.GetCurrentWinnerFactionBasedOnCurrentPhase()` / `IsBattleEnded()`；目标区全控 或 敌方 tickets 归零 |
| 伤亡 | 战斗中按 squad 跟踪存活人数 → 战后回写 roster（全灭 = 单位永久损失） |
| 老兵经验 | 复用 HVT 的击杀归属 → 单位经验 → 等级；生成时按等级施加倍率 |

### 5.3 玩家在战斗中的角色 —— **已定：两者可切**

- **默认 FPS 步兵**：玩家是自己部队里的一名士兵，靠 AI 友军打（最贴近 ER2 本体）。
- **可切 RTS 指挥官**：按键切上帝视角，复用 `SquadCommand`（F9）指挥自己的小队（最贴近 GoH 体验）。
- SquadCommand 缺失时**自动降级为纯 FPS**（软依赖，不硬绑）；此时切换键不出现、对应配置项隐藏。

实现要点：切换 = 反射调 SquadCommand 的上帝视角入口/出口（同 `NoHintsHudLink` 范式，缺失静默降级），并同步隐藏/显示征服自己的 HUD。

### 5.4 战斗层的风险与对策

| 风险 | 对策 |
|---|---|
| `StartMissionCR` 依赖战役树上下文（`Transform from/to` 参数语义不明） | M0 最小验证：造一个假 battle 直接调，看能否进 LoadingScreen；不行则改走 `MainMenu.LoadMap` + 战斗内注入 |
| `SaveBattleDataToFile` 可能校验编辑器版本/作者，或被裁剪 | M0 实测：构造最小 `MissionEditorBattle` → 存盘 → 用 `CampaignTreeCustomBattleData` 读回；失败退路线 B/C |
| 战斗中投送部队的时机（原生 AI 初始化可能覆盖） | 只投送到**我方出生区**、等 `BattleManager.IsBattleActive()` 且首阶段稳定后再投；全程诊断日志（陷阱 26：未验证信号不当门控） |
| 原生协程不启动就永不回调 | 统一走 `GenRunner.StartCoroutineNative` 范式（陷阱 25） |
| 老兵等级被游戏重置 | 老兵是 **mod 侧状态**（HVT 已证明可行），每 tick 重申，不写游戏 Rank |
| 玩家中途死亡/投降 | `BattleManager.IsBattleEnded` + `ControlledCharacter.IsDead` 组合判定；死亡后按「战线结果」结算而非「玩家生死」 |
| 战斗结束回主菜单丢上下文 | mod 单例跨场景存活 + 主菜单自动重开征服页（§4） |
| 场景加载期 `Time.time` 冻结 | 所有节流用 `Time.unscaledTime`（陷阱 20） |

---

## 6. UI 设计

### 6.1 主菜单入口
克隆原生按钮模板（同字体/同高度/同悬停态），文本「征服模式 / CONQUEST」，插在「战役」与「多人」之间；点开进 `ConquestRoot`（自建 tab 页，切页走 `MainMenu.OpenTabCR` 或自绘全屏层 + 隐藏原生 tab）。

### 6.2 战略地图（`StrategyMapView`，IMGUI）
- **底图**：程序化绘制（战区色块 + 经纬网 + 地名），**节点按 `BattleData.latitude/longitude` 真实地理投影**——原版征服模式的地图观感，零美术成本。
- **节点**：势力色圆点 + 驻军数字 + 老兵星标；**连线** = 邻接（地理距离阈值生成，可人工修正）。
- **交互**：悬停显示省份卡（名/地图/日期/驻军/地形）；点选 = 打开省份面板（出击 / 查看）；右键 = 取消。
- **顶栏**：回合数、人力、补给、控制省份数、结束回合按钮；底部滚动日志（"第 3 回合：AI 进攻 卡昂，我军防守"）。
- **配色/字体**：`UiTheme` 统一（原生字体 + `OutlinedLabel` + 半透明黑底），战斗内 HUD 同源。

### 6.3 军队页（`ArmyView`）
单位列表（番号 / 兵种 / 编制人数 / 老兵等级 / 状态），操作：补充兵员（花人力）、升级老兵（花补给）、新购单位（按年份解锁）、解散。**战损单位标红、全灭单位划掉并留档（战史）**——这是征服模式的情感核心。

### 6.4 出击编成页（`DeployView`）
左：可用单位（含战损状态）；右：本战出击槽（点数上限 + 单位数上限）；底部：敌情预估（驻军强度 / 防御加成）；【出击】按钮 → `BattleLauncher`。

### 6.5 战斗内 HUD（`BattleHud`）
顶部薄条：战区/省份名、目标区占领进度、我方剩余兵力、老兵等级提示。走 Hide Anything 契约（可隐藏）。

---

## 7. 内容与数据表

| 文件 | 来源 | 说明 |
|---|---|---|
| `provinces.json` | M0 由 `ConquestRecon`（§11）的 S2/S3 段生成 | 战区 / 省份 / 地图 / 经纬度 / 阵营 / 日期 / 上限 |
| `units.json` | M0 由 `ConquestRecon` 的 S4 段枚举 `SquadType` + `ItemsDatabase.GetSquadLoadouts` + 载具目录 | 单位 → 成本 / 解锁年份 / 编制 / 阵营 |
| `ai_templates.json` | 手写 | AI 进攻倾向、难度系数、驻军生成规则 |

dump 工具 = `ConquestRecon`（§11，内部工具，不发布），产物直接写进 `Content/`。

---

## 8. 里程碑（每阶段都有可验收产物）

| 阶段 | 内容 | 验收标准 |
|---|---|---|
| **M0 侦察** ✅**完成** | 五个 dump 段全部拿到实测数据（见 §14） | ✅ 证据落盘 `research_out/conquest_recon/dump/recon_latest.txt`（195 KB） |
| **M1 入口** ✅**已实现待实测** | 主菜单「战役」与「多人」之间插入「征服模式」按钮（克隆原生模板） | 待游戏内实测：按钮出现在正确位置、点击打开征服页 |
| **M2 战略层** ✅**核心完成** | 省份/邻接图/资源/回合/AI/自动结算/部队调动/补充/采购/老兵/存档 | ✅ 离线自检 **8/8 通过**（见 §13） |
| **M2-UI** ✅**已实现待实测** | 征服页：新建战役屏 + 战略地图 + 军队管理 + 战史（IMGUI 原生观感） | 待实测：能新建战役、点省出击、结束回合、征兵/补充 |
| **M5 养成闭环** ✅**已实现待实测** | 老兵等级（对接 HVT）+ 战损补充 + 新购单位 + 战史 | 待实测：部队越打越强、打光就没了 |
| **M3 战斗桥接** | 原生战斗开打 + 我方编成投送 + 战果回写 + FPS/RTS 视角切换 | 一次完整「出击→打→结算→回地图」闭环 |
| **M4 生成式战斗** ✅**技术已验证** | `BattleFactory` 合成 `.mer2`（`.mer2` 读取已实测通过，见 §14.3） | 战斗内容由 mod 决定，编成上限生效 |
| **M6 打磨发布** | 多存档位、难度、平衡、双语（EN/CN）、README + Nexus 文档、发布包 | 三件套验收，Nexus 可发布 |

**顺序理由（按已定决策 #3/#7 调整）**：养成系统是核心诉求，而它**只依赖自动结算**就能完整验证。所以 M2+M5 先行、M3/M4 后置——最快让核心乐趣可玩，且把风险最高的「战斗桥接」推到养成系统已验证之后。

**实际推进顺序**：M0（侦察）→ M2（战略层，纯 C# 可离线验证）→ M2-UI + M5（养成闭环 UI）→ M1（主菜单入口，依赖 M0 dump）→ M3/M4。把"等用户实测"的时间用在了能独立推进的部分上。

---

## 9. 与现有 mod 的关系（契约）

| mod | 关系 |
|---|---|
| **SquadCommand** | 软依赖（可选）：战斗内切上帝视角指挥（§5.3）。**不硬依赖**，缺失时降级为 FPS 步兵 |
| **UniversalGeneration** | **代码范式复用**（不硬依赖）：`StartCoroutineNative`、`SpawnAISquadGlobal` 调用、幽灵预览。征服自己实现 `RosterSpawner`，避免玩家必须装 UniGen |
| **HighValueTarget** | **老兵等级的唯一归属方（已定，✅ 已实现对接）**：HVT v1.2.1 新增 public `VeteranApi` 门面；征服走 `Game/VeteranLink.cs` 反射对接（无编译期依赖），并把 HVT 的 `KillsPerLevel`/`MaxLevel` 抄进 Core 规则做语义对齐（见 §9.1） |
| **Hide Anything** | ✅ 已接入注册：`HudCompat.knownNames` 加了 `{"er2.conquest","ER2 Conquest"}`（这样开关立刻出现在 ModManager，不必等首次查询懒注册）。战斗内 HUD 绘制时走 `NoHintsHudLink.IsHidden("er2.conquest", "ER2 Conquest")` |
| **ModManager** | 零维护自动接入（配置项自动出现），无需额外代码 |
| **CombatTweaks / MorePhysics 等** | 无冲突；CombatTweaks 的友军伤害保护在征服战斗里应保持默认（避免"打不死自己人"破坏战损逻辑），文档里说明 |

### 9.1 HVT 老兵兼容层（**已定：老兵等级在 HVT 上改，征服只对接**）

**分工**：HVT = 老兵系统的唯一事实源（等级定义、倍率、标记渲染、击杀归属）；征服 = 老兵系统的**消费者 + 持久化载体**。

**为什么必须持久化在征服侧**：HVT 的老兵状态挂在 `Soldier` 实例上，且它自己有 `BattleEndResetPatch`（每场战斗结束重置）。征服的军队是**跨战斗的持久实体**，所以：

```
战略层 ArmyUnit{ unitId, squadType, veterancyLevel, kills, strength }   ← 持久（存档）
        │ ① 开战时下发给 HVT
        ▼
战斗内 Soldier（HVT 挂等级/倍率）→ 击杀 → HVT 归属统计
        │ ② 战斗结束回读
        ▼
战略层 ArmyUnit 更新 kills / level / 战损                        ← 落盘
```

**HVT 侧的公开接口**（`ER2VeteranHVT.VeteranApi`，public static）—— ✅ **已实现（HVT v1.2.1）**：

| 成员 | 用途 |
|---|---|
| `ApiVersion`（const = 1） | 兼容判断 |
| `MaxLevel` / `KillsPerLevel` / `IsActive`（属性） | 语义对齐（征服侧把这两个值抄进 Core 规则） |
| `int GetLevel(Soldier)` / `int GetKills(Soldier)` / `bool IsMarked(Soldier)` | 读当前等级/击杀 |
| `bool SetLevel(Soldier, int)` / `bool SetKills(Soldier, int)` | **开战时把持久等级灌进新生成的士兵**（关键） |
| `int ApplySquadLevel(Squad, int)` | 按班批量灌等级（一次调用覆盖全队） |
| `int GetSquadKills(Squad)` / `int GetSquadMaxLevel(Squad)` / `int GetSquadAliveCount(Squad)` / `int GetSquadSize(Squad)` | 战斗结束回读（击杀合计 / 最高等级 / 存活与编制） |

**全部方法自带 try/catch，绝不向调用方抛异常**——对方 mod 不应因 HVT 内部问题而崩。

**征服侧实现**：`Conquest/Game/VeteranLink.cs` —— 反射查找 `ER2VeteranHVT.VeteranApi`（同 `Shared/NoHintsHudLink.cs` 范式：缓存 `MethodInfo`、懒查找、全程 try/catch 静默降级）。**不在编译期引用 HVT**，没装 HVT 时降级为「无老兵系统」，征服其余功能不受影响。

**规则对齐**（`Plugin.AlignVeterancyRulesWithHvt()`）：HVT 在场时，把它的 `KillsPerLevel` / `MaxLevel` 抄进 `ConquestRules`，保证 Core 的镜像计算与 HVT 语义一致——否则会出现「战略层显示 Lv.3、战斗里实际是 Lv.1」的错位。没装 HVT 时用征服自己的默认值（5 / 5，与 HVT 默认相同）。

**配置**：`Compatibility.VeteranHVT`（默认开）= 关闭后征服完全自包含，老兵只走内部镜像计算。

**注意**：HVT 现有内部成员是 `internal static`（`LevelOf`/`TryGetState` 等），反射可及但属于内部实现，不稳定 → 所以新增**显式 public 门面**，把耦合点收敛到一处；HVT 内部重构不影响征服。

**HVT 侧踩坑（已记录）**：`Squad` 的成员字段是 **`units`**（`Il2CppSystem.Collections.Generic.List<Soldier>`），**不是** `soldiers`；班成员数用 `CountMembers`。写对接代码前先 `ilspycmd -t Squad` 核对。

---

## 10. 已定决策（2026-09-13 用户拍板）与遗留项

| # | 问题 | **决定** |
|---|---|---|
| 1 | 战斗视角 | **两者可切**：默认 FPS 步兵，按键切 RTS 指挥官（复用 SquadCommand） |
| 2 | 省份来源 | **原生战役/战斗**当省份池（`VanillaCampaignsList` → `CampaignTreeData.battles` → `BattleData`） |
| 3 | 内容策略 | **直接在原生战役/地图上改**；**核心是持续性的养成系统**（战略循环 > 战斗还原度） |
| 4 | 老兵等级归属 | **在 HVT 上改**（HVT 是老兵系统唯一事实源），征服只做兼容对接（§9.1） |
| 5 | 单位粒度 | **小队/班**，与 SquadCommand 同粒度 |
| 6 | 联机 | **不做 COOP**（纯单机） |
| 7 | 自动结算 | **必须支持**（用户测试需要） |

**由 #3 导出的优先级调整**（重要）：M5「军队养成」不再是最后一块，而是**与 M2 并列的核心**。实施顺序改为：
`M0 侦察 → M1 入口 → M2 战略层（含军队/资源/回合/自动结算） → M5 养成闭环（老兵/补充/战史，靠自动结算即可验证） → M3 战斗桥接 → M4 生成式战斗 → M6 打磨`。
这样**不依赖任何战斗桥接就能先把养成系统跑通并试玩**——风险最低，也最快让用户摸到核心乐趣。

**仍然开放的细节**（可在 M2 实施中定，不阻塞开工）：
- v1 先做哪个战区当样板（建议诺曼底；或干脆「全战役可选」）
- 战败的代价（丢省份 / 只损失单位 / 可重打）——建议：**丢省份 + 单位战损**，但玩家部队不全灭（保留重建能力）
- 资源种类（人力/补给两种起步，够不够）


---

## 11. M0 侦察工具（已实现并部署）

**目录** `ConquestRecon/` · **GUID** `er2.conquest.recon` · **DLL** `ER2_ConquestRecon.dll` · **内部工具，不发布**（同 `HvtTestDriver` 定位）

**设计**：**不打任何 Harmony 补丁**——M0 只需要「看」，不 patch 原生方法，避免污染被观察对象。一个 `DontDestroyOnLoad` 的 MonoBehaviour 轮询 `MainMenu.instance`，就绪后延迟自动 dump。

| 键 | 行为 |
|---|---|
| （自动） | 进主菜单 3s 后自动全量 dump |
| **F10** | 全量 dump（**先打开「战役」页再按**，可抓到 S3 战役树缓存） |
| **F9** | 只 dump S3（战役页缓存） |

**输出**：`research_out/conquest_recon/dump/recon_latest.txt`（每次覆盖）+ `recon_<tag>_<时间>.txt`

**五个 dump 段**：
- **S1 主菜单层级** — `MainMenu` 单例全字段 + `MainMenu.transform` 整树递归 + 7 个 tab 各自子树；每个节点带 `Rect`/`Text(内容)`/`Button[onClick 目标.方法]`/自定义组件名 → **M1 靠它定位「战役」按钮并确认能否克隆**
- **S2 原生战役/战斗全表** — `vanilla_campaign_data`（数组）+ `VisibleVanillaCampaigns()`（枚举，对照），逐战役逐战斗输出 `ICampaignTreeBattleData` 元数据 + `LoadBattle()` → `IBattle` + `BattleData` 全字段 → **这就是省份池**
- **S3 战役树缓存** — `CampaignsTree.cachedBattles`（`List<ValueTuple<BattleData, ICampaignTreeBattleData>>`，**最富的数据源**，需战役页打开）
- **S4 兵种目录** — `SquadType` 全枚举 × `ItemsDatabase.GetSquadLoadouts` → 有效兵种 + `squadCode_id`/`squadName`/编成数 → **M2/M5 的单位表**
- **S5 `.mer2` 读取 spike** — `SaveDataManager.LoadFromFile<MissionEditorBattle>` 四种 (fileName, path) 组合逐一尝试，成功后 dump `vphases`/`unit_spawns`/`vehicle_spawns`/`conquer_areas` 结构 → **M4 的地基**

每个字段独立 try/catch（`F()` 助手），**单点失败不影响其余**——侦察工具的价值就在于告诉我们「哪里会失败」。

---

## 13. M2 战略层实现状态（已完成）

**目录** `Conquest/Core/`（**纯 C#，不引用任何 Unity / Il2Cpp 类型**）· **mod** `Conquest/`（`er2.conquest` / ER2 Conquest / `ER2_Conquest.dll` v0.1.0）

| 文件 | 职责 |
|---|---|
| `Model.cs` | `Province`（省份=一场原生战斗）/ `ArmyUnit`（小队=养成载体）/ `BattleLogEntry` / `UnitTemplate` / `CampaignPhase` |
| `ConquestCampaign.cs` | 存档根 + `ConquestRules`（全部平衡参数）+ `DeterministicRng` + 经济/军队操作（补充/采购/老兵重算） |
| `ProvinceGraph.cs` | 经纬度 → k 近邻邻接图 + 全局连通兜底；`FirstStepTowardEnemy`（BFS 穿己方领土，部队调动用） |
| `BattleSim.cs` | 自动结算（对称攻守模型：部队 + 驻军）+ 胜率预估 |
| `TurnResolver.cs` | 回合心跳 + 玩家进攻/调动 + AI（进攻 + 前推）+ 驻军恢复 + AI 兵员回补 + 胜负判定 |
| `ConquestSetup.cs` | 原生战斗表 → 初始战役（归属=历史守方、关键省标记、邻接、起始部队撒点） |
| `ConquestSave.cs` | 行式键值存档（零依赖、人类可读、缺字段容错）+ 多存档槽 |
| `CoreSelfTest.cs` | 战略层自检（不变量 + 平衡护栏） |

### 13.1 离线验证（关键工程收益）

因为 Core 是纯 C#，**战略层可以脱离游戏验证**——`Conquest/tools/CoreTest/` 是一个控制台宿主，
直接编译 `Core/*.cs` 跑完整战役：

```
cd Conquest\tools\CoreTest
dotnet run -c Release --project CoreTest.csproj -- 30 48
```

**当前 8/8 通过**：邻接图（对称+连通）/ 初始部队 / 省份归属 / 数值不变量 / **战役有推进** / 自动结算可复现 / 存档往返 / 养成有产出。

### 13.2 自检抓出的三个真实缺陷（这就是它存在的价值）

| # | 症状 | 根因 | 修复 |
|---|---|---|---|
| 1 | 邻接图 0 条边 | 自检在建图前就校验（测试自身顺序错误） | 先 `ProvinceGraph.Build` 再校验 |
| 2 | 多战区时 30/40 省不可达 | 连通兜底只在战区内做 → 跨战区战役永远打不完 | 连通兜底改为**全局**（顺带生成"战区走廊"边） |
| 3 | **25 回合仅 3 场战斗、0 次攻占** | ① 每个势力只在 1 个省有部队 → 全图静态；② 更致命：**部队只能靠"打下来"前进**，推进到死胡同就永久卡住 | ① 起始部队撒在 4 个省（关键省+边境省）；② 新增**部队调动**（`TryMoveUnit` / `AdvanceIdleUnits`，BFS 穿己方领土） |
| 4 | 5 回合内我方部队从 6 支掉到 0，之后战略层彻底死水 | 损失率过高（胜 0.18 / 败 0.55）且**没有任何补充手段** | 损失率降到 0.10/0.30；自检 bot 加入【补充兵员】+【采购】；AI 加兵员回补 |

修复后同一场景：**20 → 58 省（共 60）、107 场战斗、43 次攻占、部队 8 → 27、老兵打到 Lv.5**。

### 13.3 平衡参数（全部集中在 `ConquestRules`，可被 cfg 覆盖）

| 参数 | 值 | 说明 |
|---|---|---|
| `ManpowerPerProvince` / `SupplyPerProvince` | 15 / 8 | 每省每回合产出 |
| `VetPowerBonus` | 0.15 | 每级老兵 +15% 战力（养成的可感知出口） |
| `DefenseMultiplier` | 1.25 | 防守方加成 |
| `WinnerLossRate` / `LoserLossRate` | 0.10 / 0.30 | **刻意压低**（单位是持久资产） |
| `MaxAttacksPerTurn` / `AiMaxAttacksPerTurn` | 3 / 2 | 回合节奏 |
| `AiAdvantageRequired` | 0.90 | AI 有优势才开打 |
| `AiReplenishPerTurn` | 2 | AI 兵员回补（玩家需花人力补充） |
| `KillsPerLevel` / `MaxVeterancy` | 5 / 5 | **与 HVT 默认一致**（老兵语义归 HVT，这里只做镜像计算） |

### 13.4 下一步（M1 入口）

M1 需要 M0 的 recon dump 才能确定主菜单注入点（`mainMenu_tab` 里「战役」按钮的位置与克隆方式）。
recon 工具已部署，**进主菜单 3 秒后自动 dump**；打开「战役」页再按 **F10** 可抓到 `cachedBattles`。

---

## 14. M0 侦察实测结果（2026-09-13，`recon_latest.txt` 195 KB）

侦察工具已在游戏内跑通（进主菜单 3s 自动 dump）。以下全部是**实测数据**，不是推断。

### 14.1 S1 主菜单结构 —— M1 的注入点

```
MainMenu.instance.name   = Canvas
mainMenu_tab = MainMenu / camaigns_tab = CampaignMenu / multiplayer_tab = MultiplayerMenu
settings_tab = SettingsMenu / statistics_tab = StatisticsMenu
missioneditor_tab = MissionEditorMenu / credits_tab = CreditsMenu
cameraPositions.Length = 7
```

`MainMenu/buttons/` 是竖排按钮列表（**手动 anchoredPosition 定位，间距 48**）：

| 子物体 | siblingIndex | anchoredPosition | 尺寸 | onClick 目标 |
|---|---|---|---|---|
| RandomWeather | 0 | (35,−31) | 40×40 | `Super DayNight Cycle.Randomize` |
| VERSION | 1 | (66,−31) | 200×35 | — |
| Credits | 2 | (0,−123) | 200×41 | `MainMenu.OpenTab` |
| Statistics | 3 | (0,−163) | 200×41 | `MainMenu.OpenTab` |
| Roadmap | 4 | (0,−203) | 200×41 | `MainMenu.OpenEasyRed2Roadmap` |
| Discord | 5 | (0,−243) | 200×41 | `MainMenu.OpenEasyRed2Discord` |
| **Campaigns** | **6** | **(0, 270)** | 270×50 | `MainMenu.OpenTab` |
| **Multiplayer** | **7** | **(0, 222)** | 270×50 | `MainMenu.OpenTab` |
| Mission Editor | 8 | (0, 174) | 270×50 | `MainMenu.OpenTab` |
| Settings | 9 | (0, 126) | 270×50 | `MainMenu.OpenTab` |
| Exit | 10 | (0, 78) | 270×50 | `MainMenu.Quit` |

每个按钮 = `Button` + `Text` 子物体（i=0）+ `icon` 子物体（i=1）。

**M1 做法（已实现 `UI/MenuEntry.cs`）**：克隆 `Campaigns`（原生模板，字体/尺寸/悬停态全一致）→ `SetSiblingIndex(7)` → 改 Text 为「征服模式」→ `onClick.RemoveAllListeners()` + 绑定自己的回调 → 图标染金 → **把 Campaigns 上移一个间距**（270→318）给新按钮腾位（间距从 Campaigns/Multiplayer 实际 y 差推导，不硬编码）。父物体若有 LayoutGroup 则跳过手动定位。幂等判断 = `buttons` 下是否已有名为 `Conquest` 的子物体（场景重载自动重注入）。

### 14.2 S2 原生战役/战斗表 —— 省份池（决策 #2 落地）

- `vanilla_campaign_data.Length = **17**`（17 个原生战役），**共 206 场战斗**
- `LoadBattle()` **可用且稳定**：抽样 40 次调用，**0 次返回 null**（其余 166 次是我设的预算上限，不是失败）
- 战役 id 样例：`aberdeen_training` / `anzio_battle` / `cassino_battle` / `tunisia_battle_south` / `tunisia_battle`
- `BattleData` 实测样例（`tutorial_1`）：`map=Aberdeen`、`location_name=Basic Infantry Training`、`latitude=39.47`、`longitude=−76.14`（= 马里兰 Aberdeen 试验场，**坐标真实可用**）、`year=1945`、`month=may`、`day=12`、`invadersFaction=UnitedStates_allies`、`defendersFaction=Civilian`

**编译期类型真相（踩坑记录）**：`BattleData.map` 是 **`Gamemap` 枚举**、`month` 是 **`MonthName` 枚举**、`invadersFaction`/`defendersFaction` 是 **`Faction` 枚举**——都不是 string。统一 `ToString()` 落到 mod 的字符串模型；月份**按枚举名映射**（january..december）而不是强转 `(int)`，因为底层值从 0 还是 1 开始不明确。

### 14.3 S5 `.mer2` 读取 spike —— **M4 地基已验证**

```
ME_* 任务目录 = 205 个
```

四种 (fileName, path) 组合实测：

| 组合 | 结果 |
|---|---|
| A: 无扩展名 + 目录 | ❌ `ok=False data=null` |
| **B: 带扩展名 + 目录** | ✅ **`ok=True`** |
| C: 全路径 + 目录 | ✅ `ok=True` |
| D: 无扩展名 + 目录+`\` | ❌ `ok=False` |

**结论：`SaveDataManager.LoadFromFile<MissionEditorBattle>(文件名带扩展名, out data, 目录, BinaryFormatter)` 可用。**

读出的结构（`ME_Anzio Rework_100pbcpb.mer2`）：
```
GetBattleName()=Near Tor San Lorenzo   GetMapName()=Anzio
GetDefendersFaction()=Germany_axis     GetInvadersFaction()=England_allies
GetCurrentPhasesCount()=4   startPhase=0   editor_version=210   game_version=2.1.0
customSquads.Count=1   placed_objects.Count=46   vphases.Count=4
phase[0]: unit_spawns=18 vehicle_spawns=13 conquer_areas=3
phase[1]: unit_spawns=13 vehicle_spawns=5  conquer_areas=3
phase[2]: unit_spawns=11 vehicle_spawns=7  conquer_areas=1
phase[3]: unit_spawns=10 vehicle_spawns=6  conquer_areas=1
```
→ **生成式战斗（M4）所需的一切都在**：阶段、生成点、目标区。

### 14.4 S4 兵种目录 —— 单位表

`SquadType` 枚举 **56 个成员，全部有效**（`GetSquadLoadouts` 全部返回非空编成，0 跳过）。
→ 征兵表直接由原生目录生成，零内容制作（成本按编制人数启发式推算）。

### 14.5 S3 战役树缓存

`CampaignsTree instance not found` —— **该实例只在「战役」页打开时才存在**。
不影响：S2 的 `vanilla_campaign_data` 已提供完整的省份池（206 场战斗）。
`NativeBattleSource` 仍保留 cachedBattles 为**首选路线**（更省，无需 `LoadBattle`），拿不到就退到 S2 路线。

### 14.6 ⚠️ 关键陷阱：**BepInEx 插件按名字母序加载**

实测日志（同一次启动）：
```
[Info : BepInEx] Loading [ER2 Conquest 0.1.0]
[Info :ER2 Conquest] [Conquest] 未检测到 Veteran HVT —— 老兵养成降级为征服内部镜像计算。
[Info : BepInEx] Loading [ER2 Veteran HVT 1.2.1]      ← HVT 在我们之后才加载！
```

`"ER2 Conquest"` 字母序排在 `"ER2 Veteran HVT"` **之前**，所以：
- 在 `Load()` 里查一次 HVT 必然失败；
- 若把"找不到"**永久缓存**（我最初的写法 `_lookupDone = true`），就**永远接不上**。

**对策（已修）**：`VeteranLink` 的查找改为**失败可重试**——节流 1s、上限 60 次、成功即停止扫描；并提供 `OnBecameAvailable` 回调，让"晚到的 HVT"一旦接上就把老兵规则重新对齐进 Core（首次对齐发生在 HVT 加载之前，会漏）。

**推广结论（写进工作区陷阱）**：**任何跨 mod 的懒对接都不能缓存否定结果**。工作区文档早有"别在 `Load()` 里注册（对方可能还没加载）"的告诫，本次是它在**读取方向**上的同类翻车——`Shared/NoHintsHudLink.cs` 之所以没踩到，是因为它把查找放在**首次查询**（进战斗后）而不是 `Load()`。

### 14.7 ⚠️ 首次实测暴露的 UI 三宗罪（2026-09-13，全部已修）

用户实测反馈："点击后跳出了原生的战役界面"、"界面还有许多 bug"。日志定位到三个根因，**全部是"想当然"造成的**：

| # | 症状 | 根因（日志实证） | 修法 |
|---|---|---|---|
| 1 | **点一下同时开出原生战役页 + 征服页** | 克隆 `Campaigns` 按钮时，其**原生持久监听器**（Inspector 里那种）被一并克隆；`RemoveAllListeners()` **清不掉持久监听器** → 我的回调触发了（日志有 `从主菜单打开征服页`），原生 `OpenTab` 也触发了 | `SetPersistentListenerState(i, Off)` 逐个关闭 + `RemoveAllListeners()` + 重新 `AddListener`；**事后打印持久监听器数量**（`持久 1 → 1（已全部置 Off）`）让日志自证；另清掉 `EventTrigger` |
| 2 | **军队页一打开就报错**（294 次/帧） | `GUI.BeginScrollView(Rect, Vector2, Rect)` 的 3 参重载在本游戏 IL2CPP 构建里**被代码裁剪** → `System.NotSupportedException: Method unstripping failed`（栈：`BeginScrollView(8参)` ← `BeginScrollView(3参)` ← `DrawArmyTab`） | **不用任何滚动/裁剪 API**：自算偏移 + 只绘制可见行（`RowVisible`）+ 自绘滚动条 + `EventType.ScrollWheel` 处理滚轮 |
| 3 | **默认阵营是 Civilian（平民）** | 阵营列表取字母序第一个 → `Civilian`；日志 `新战役：Conquest[Civilian 战役 Civilian T1 省206 部队16]` | 过滤 `civilian`/`neutral` 等非战斗方；默认阵营取"带下划线的正规军 id"；默认战区取省份最多的那个（避免一开局 200+ 省） |

**推广结论**：
- **IL2CPP 裁剪是 mod 的隐形地雷**——凡是游戏自己用不到的 UnityEngine 方法都可能没有原生实现，调用即 `NotSupportedException`。**只有实测过的方法才能用**。本项目实测**可用**清单：`GUI.DrawTexture` / `GUI.Label` / `GUI.Button(Rect,GUIContent,GUIStyle)` / `GUI.color` / `GUI.matrix` / `GUIUtility.RotateAroundPivot` / `GuiExtension.OutlinedLabel`；**禁用**：`GUI.BeginScrollView`（3 参重载）。
- **克隆原生按钮必须显式接管点击**：持久监听器不是运行时监听器，`RemoveAllListeners` 不管它。

### 14.8 相机运镜（对齐原生"先移相机、再展开界面"）

用户指出原生菜单的节奏是"选择选项后相机移动、然后展开界面"。实现见 `UI/MenuCamera.cs` + `ConquestRoot.Tick()`：

- **不做**：调用原生 `MainMenu.SetCameraMenuPos` —— 副作用未知（可能顺带切 `currentTab`，在玩家背后把原生页签换掉）。
- **做**：**只读**地在 `MainMenu.cameraPositions`（7 个 tab 对应 7 个机位）里按名字找"战役页"机位（匹配 `campaign`/`map`/`strategy`，并校验距原机位 <60m 防瞬移）；找不到就退化为**轻微推镜**（原机位 + 前 0.85m + 上 0.22m）。进入时记录原机位，退出时**精确还原**。
- 动画在 **`Update`** 里推进（`ConquestRoot.Tick()`），**不在 `OnGUI`**——OnGUI 一帧被调用多次（Layout/Repaint/各种输入事件），在里面推进会让速度随事件数量漂移。
- 界面侧：面板横向展开（`inset` 随动画收敛）+ 缓出曲线；背景压暗改为**半透明（alpha 0.80）**保留 3D 场景可见——原生菜单就是这个观感，之前铺满不透明底色是"不像原生"的主因。

**S6 侦察段**（`ConquestRecon` 新增）dump `cameraPositions[i]` 的名字与世界坐标、各 tab 的 `CanvasGroup` 状态、以及**按钮上组件的真实类型名**（`GetIl2CppType().Name`——M0 的 dump 里全是 `!Component` 就是因为用了 `GetType().Name`，IL2CPP 下会退化成基类名），用于下一轮把机位精确绑定。

### 14.9 UI 改成原生排版 + 修点击穿透（2026-09-13 第二轮实测反馈）

用户给了原生战役页截图，要求"保留这样的排版与 UI，用原生的方式"，并报告"面板弹出来后点击还会点到原生那些按钮"。

**原生战役页的排版骨架**（从截图读出，征服页照此重做）：
```
左上「◀ 返回」        顶部居中大标题（当前战役名）
左侧竖排战役列表（选中高亮 + ★ 星标）
内容区：战斗卡片网格 —— 每张卡 = 日期 + 双方旗帜 + 战斗名，背景透出 3D 场景
```

**新排版**：`左上返回` + `左侧战区列表（★=我方有省份，右侧显示 控制/总数）` + `顶部大标题（当前战区名）` + `内容区省份卡片网格` + `右上页签（战略/军队/战史）` + `顶部状态条（回合/资源/结束回合）` + `点卡片 → 右侧滑出省份详情与【出击】`。

**关键：改用原生资源**（新增 `Game/NativeAssets.cs`，全部实测确认存在的 API）：

| 原生 API | 用途 |
|---|---|
| `Language.GetText(id)` / `Language.GetBattleName(id)` | **本地化**——`anzio_battle` → 「安齐奥战役」、`bn_training01` → 「基础教程」（已核对 `Translations/LocalizationChineseSimplified.xml` 确有这些 key） |
| `ResourcesManager.GetFactionData(faction).flag`（Sprite） | **原生阵营旗帜**，直接画在卡片上 |
| `ResourcesManager.GetFactionData(faction).names[]` | 阵营本地化名 |
| `ResourcesManager.GetGUISTyle(anchor, size, color, fstyle)` | **游戏自己的 IMGUI 样式**（比自建 GUIStyle 更贴原生） |
| `LocalizationManager.GetFont(bool)` | 原生字体（优先于之前的"活体 uGUI Text"回退链） |

**日期排版**也照原生卡片做（`12 五月 1945`）：中文构建用"五月"，英文构建用"May"。

**修点击穿透**（新增 `UI/InputBlocker.cs`）——根因：**IMGUI 与 uGUI 是两套独立输入系统**。征服页画在原生之上，但原生按钮的点击由 `EventSystem` + `GraphicRaycaster` 处理，**它看不见 IMGUI 画了什么**，于是点击穿过面板落到下面的原生按钮上。
解法：在 `sortingOrder=30000`（接近 short 上限 32767）的独立 Canvas 上放一张全屏 `Image`（`raycastTarget=true`、alpha≈0 不可见）。它成为射线命中的最上层图元 → 原生按钮收不到点击；IMGUI 走自己的输入通道，我的按钮照常工作。只在页面打开时激活，关闭即隐藏。

**顺带修的一个正确性问题**：旗帜来自**图集**，直接 `GUI.DrawTexture(sprite.texture)` 会把整张图集画出来（几十面旗拼在一起）→ 必须用 `sprite.textureRect` 换算 UV 走 `GUI.DrawTextureWithTexCoords`。但该方法也可能被裁剪，所以**首次调用探一次**，失败永久退化为整图绘制（避免每帧每面旗抛异常）。

### 14.10 第三轮实测反馈：相机打架 + UI 重叠 → 改用**原生页签系统**（2026-09-13）

用户实测："相机转了一圈又回到原地了"+"UI 重叠"（截图：征服页画在主菜单之上，原生左侧菜单与按钮全部透出来，和我的文案叠在一起）。

**S6 侦察段给出了决定性数据**（`recon_latest.txt`）：

```
cameraPositions 与页签按名字一一对应（Length=7）：
  [0] CAM_POS_menu        (-23.41, 6.01, 70.01)   ↔ MainMenu
  [1] CAM_POS_settings                            ↔ SettingsMenu
  [2] CAM_POS_campaigns   ( 0.77, 4.88, 58.78)    ↔ CampaignMenu      ← 战役页机位
  [3] CAM_POS_credits / [4] CAM_POS_statistics / [5] CAM_POS_editor / [6] CAM_POS_multiplayer
各 tab 状态：mainMenu_tab self=True（唯一激活），其余 self=False；camaigns_tab 组件 = RectTransform, CanvasRenderer, Image, CampaignsTree
Canvas：只有 2 个（Canvas sortingOrder=0、ErrorCanvas=1）→ 我的拦截层用 30000 足够安全
按钮组件真名（用 GetIl2CppType().Name 才拿到）：Campaigns/Conquest 都带 SetUiSelectedOnEnable，Conquest 已正确插到 [7]
```

**根因**：
1. **相机打架**——我逐帧写相机位置去"运镜"，而**原生主菜单自己在驱动相机**，两边互写 → 来回转，最终原生赢（回到原机位）。
2. **UI 重叠**——我用 IMGUI 画在主菜单之上，但**从未隐藏原生主菜单页签**，所以原生按钮/图标全部透出来。

**修法（新增 `UI/NativeTabHost.cs`，删掉 `UI/MenuCamera.cs`）**：**借用游戏自己的页签切换**。
- 进入：`MainMenu.OpenTab(camaigns_tab, true)` —— 这正是玩家点「战役」时游戏走的原生路径，它让**原生代码**把相机移到 `CAM_POS_campaigns`、原生地隐藏主菜单页签、播放原生过场。**我们一行相机代码都不写**。
- 然后把战役页的原生内容让位：停用 `CampaignsTree` 组件（否则它会异步重建列表、并把内容重新显示）+ 隐藏页签下所有子物体（记录进入前激活的那些，退出时只还原它们）。页签**根**保持激活 → 原生状态自洽；IMGUI 与页签 active 无关，照常绘制。
- 看门狗 `Enforce()`（0.5s 节流，走 `Time.unscaledTime`）：原生会自己刷新并重新显示内容，所以只要页面开着就持续压住（同 Hide Anything 的 Enforce 思路）。
- 退出：还原子物体与组件 → `OpenTab(mainMenu_tab, true)` 原生返回主菜单（相机也随之移回 `CAM_POS_menu`）。

**教训（可推广）**：**当原生系统已经在驱动某个状态（相机/选中/布局）时，不要自己再去写它——找到原生切换入口，借它的力。** 自己写就会打架，且原生通常赢。这也是用户要的"用原生的方式"。

**另一个实测踩坑**：`FactionDetails.names` **不是阵营名**，是**士兵名字**数组——用户界面因此显示成 "George"/"Hans"。本地化文件里也没有阵营名 key（已核对 `Translations/*.xml`）→ 改为 `Language.GetText(faction)` 优先 + **驼峰分词**兜底（`UnitedStates_allies` → `United States`）。

**顺带**：全部界面文案改走 `Plugin.T()` 双语；本次部署 **CN 构建**（用户游戏是中文，避免"硬编码中文 + T() 英文"混杂）。

### 14.11 第四轮反馈：流程重构 + "弹窗关不掉"的真正根因（2026-09-13）

用户反馈（附截图）："UI 很多重叠。而且这个进攻失利关不掉。同时我甚至看不懂这是在干什么，为什么有原版战役。**不应该是选择国家后再研发和配置军队，然后跳出来几个选项然后选择战斗吗？为什么界面这么杂乱？**"

这一轮是**产品层**的返工，不只是修 bug。

#### A. 流程重构（按用户描述）

| 之前（杂乱） | 现在 |
|---|---|
| 左侧常驻**战区侧边栏**（17 个战区 + 0/15 计数） | **删掉**——战役已锁定在一个战区，列别的毫无意义 |
| 全量**省份卡片网格**（206 张，看不懂） | 只列**当前可进攻的目标**（通常 2-5 个），每张卡带胜率与【出击】 |
| 右侧**常驻省份详情面板** | **删掉**——每张卡自带【出击】，不再需要选中态 |
| 新建战役 = 阵营网格 + 战区网格（无说明） | **三步流程**：① 选国家 ② 选战区 ③ 开始；顶部一行说明"选国家 → 组建并养成军队 → 选择战斗 → 打完继续养成"，并解释"战场取自原生战役（每场原生战斗 = 一个省份），所以会看到安齐奥、斯大林格勒这些熟悉的地图" |
| 标题 = 战区名（易误认为在玩原版战役） | 标题 = **「征服 · 安齐奥战役」**（明确是征服模式） |

页面骨架：**战略**（可进攻目标 + 我的领土）· **军队**（补充兵员 / 征兵，养成核心）· **战史**。

#### B. "弹窗关不掉"的真正根因 —— IMGUI 的点击归属

**这是本轮最有价值的发现**：我一直以为"后画的控件在上层"，所以把弹窗放在最后绘制就以为它能收到点击。**实际相反——IMGUI 里先画的控件先拿到鼠标**（`GUIUtility.hotControl` 在 MouseDown 时被第一个命中的控件抢走）。于是弹窗底下的省份卡片按钮（**先**画的）把点击全吞了，弹窗的【关闭】永远收不到事件。

**修法**：新增 `UiTheme.InputEnabled` 全局交互开关——弹窗打开时置 `false`，让下层所有按钮/点击区（`UiTheme.Button` / 新增的 `UiTheme.ClickArea`）直接失效；画弹窗之前再置回 `true`。同时把所有裸 `GUI.Button` 调用统一换成 `UiTheme.ClickArea`，确保没有漏网的交互点。

另外给弹窗加了多重关闭路径，彻底避免卡死：大号【关闭】按钮、**点弹窗任意处**、**ESC**、以及**显示 20 秒后自动关闭**的兜底；并加 0.3s 保护窗，防止触发【出击】的那一下鼠标事件在弹窗出现的同一帧被误判成关闭。

#### C. 布局重叠的根因 —— 缺分带

之前所有元素用"内容区高度百分比"堆叠，状态行与卡片网格各自算坐标 → 必然重叠。现在改为**硬编码横向分带**：

```
BandTopH    = 88   返回(y18..64) + 居中标题(y22..76) + 页签(y26..58) + 分隔线(y82)
BandStatusH = 42   回合信息 / 资源 Chips / 结束回合（y88..126）+ 分隔线(y126)
ContentY    = 138  内容带从这里开始，直到 Screen.height - 22
```

带与带之间留了安全间隔，**结构上不可能重叠**。

---

## 15. 明确不做（v1 边界）



- 不改游戏原文件；不读写 `saves/gamedata.er2`
- 不重做 ER2 原生战役/多人（征服模式是**并列入口**）
- 不做联机 COOP（v1）
- 不做完整研究树（用「年份 + 胜利数」近似解锁）→ **v0.2.0 已推翻：现在有真正的 research 树**（见 §16）
- 不做新地图/新美术（复用原生地图与原生战斗元数据）
- 不接管原生战斗 AI（走原生 AI，mod 只决定"谁上场"）

---

## 16. v0.2.0（2026-09-13）：解包《地狱之门》后按原版补全 + M3 战斗桥接

本节记录第五轮工作：**不再靠二手描述，而是真正解包了 GoH**，据此把战略层按原版补全，
并实现了此前一直挂着的 M3 战斗桥接。

### 16.1 解包方法（新能力，值得复用）

**GoH 的 `resource\*.pak` 就是标准 ZIP**（文件头 `PK\x03\x04`）。用 .NET 直接读：

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z=[System.IO.Compression.ZipFile]::OpenRead("...\resource\gamelogic.pak")
$z.Entries | Where-Object { $_.FullName -like 'set/dynamic_campaign/*' } | ForEach-Object {
  [System.IO.Compression.ZipFileExtensions]::ExtractToFile($_, (Join-Path $out $_.FullName), $true) }
```

⚠️ **WinRAR 在这些 pak 上会卡死**（13 MB 的包 120 秒超时）——别用。

**战略层全部数值在 `gamelogic.pak` 的 `set/dynamic_campaign/`（16 文件）**，这正是原版征服模式的
完整定义；战斗层在 `set/multiplayer/games/`，单位目录在 `set/multiplayer/units/conquest/`。
产物落 `research_out/goh_unpack/`（163 文件），拆解结论见 **`ER2_征服模式_参考拆解.md`**。

### 16.2 照搬进 Core 的机制（每条都有 `.set` 行号实证）

| 机制 | GoH 依据 | ER2 实现 |
|---|---|---|
| 四种资源 MP/AP/RP/SP | `resources_standard.set:54-95` | `Manpower/Ammo/Research/Special` |
| 收益随场次阶梯 `"n:v"` | `WinGain "1:200 4:250 6:300…"` | `StepCurve`（可解析、可存档往返） |
| 地图类型奖励 5 类 | `MapRewards`（Airfield/Ammodepot/Factory/Research/Bonus） | `ProvinceKind` + 攻占发放 + 卡片预览 |
| 风险档 Low/Standard/High | `RiskFactor`（BotVeterancy 0/1/2，Rewards ×1.0/1.25/1.5） | 新建战役第③步 |
| 战损返还 40% / 解散返还 60% | `PaybackFactor` / `SellFactor` | 全灭返还 + `TryDisband` |
| 研究树（requires + costs + position） | `unit_research_ger.set` | `ResearchTree` + `ResearchTreeBuilder`（**由原生 SquadType 目录生成**）+ 研发页 |
| 出兵预算 CP（阶梯 + 上限 + 每阶段） | `Budget`/`StageCP`/`GlobalMaxCP` | `UnitTemplate.CpCost` + `DeploymentBudget` + 出击裁剪 |
| 5 阶段按场次解锁 | `StageCP` 5 项 + `StageUnlock` | `Stage` + `GarrisonCapFor`（阶段抬驻军上限 ≈ GoH DefenseLevel） |
| AI 研究阶段曲线 | `ResearchStages "0:1 1:1 2:2…"` | `AiResearchStageFor` → AI 部队科技等级 |

**明确不做的**：HLL 的**节点（Nodes）**系统——ER2 没有战场建造系统，产出改由省份类型承担
（见 `ER2_征服模式_参考拆解.md` §3 映射表）。

### 16.3 联网检索《人间地狱》的诚实结论

**本机网络拿不到 HLL 的一手页面**：`hellletloose.fandom.com` 与 `www.reddit.com` **连接超时**，
`steamcommunity.com` 被 modsearch 判定为私有网络目标而拒绝抓取，`hellletloose.com` 博客正文由 JS 渲染。
因此只取到搜索引擎摘要（"扇区/据点内人数多的一方推进占领条"、"Offensive 守方不能夺回已失扇区"、
"每个节点 +10 资源/分钟、每类上限 3 个"、"Garrison 50 补给"等），**全部在文档里标注了"未核实"**。

**可迁移的三条**：①「人多者推进占领条」= ER2 原生 `conquer_areas` + mod 侧人数比较；
② 资源买战场支援（对应 SP）；③ 节点系统**不做**。

### 16.4 离线自检 8/8 → 15/15

新增 7 项：省份类型分配 / 研究树生成与前置约束 / 经济四资源与战果结算 / 研发与阶段推进 /
**研发门槛**（未研发不能征募精锐、研发后可征募）/ **出兵预算**（CP 上限生效 + 随场次增长）/
**解散返还**（= MpCost×0.6）。

**自检又抓出 3 个真缺陷**（护栏价值第 N 次验证）：

| # | 症状 | 根因 | 修法 |
|---|---|---|---|
| 1 | 30 回合后 MP 涨到 4.7 万，征兵花不完 | 领土产出 15/8/2 太高，经济失去约束力 | 产出降到 **4/3/1**，让"打胜仗"成为主要收入 |
| 2 | 30 回合只打得动 **4 场仗** | 驻军 1 点折算 9 战力 → 满级驻军省 = 112 战力 ≈ 10 个班 | 降到 **2/点**（满级驻军 ≈ 25 战力 ≈ 5 个班） |
| 3 | 自检报"驻军越界"假故障 | 驻军上限改为随阶段增长，但不变量仍按基础上限校验 | 上限收进**唯一来源** `Rules.GarrisonCapFor(stage)` |

**外加一条方法论**：**自检 bot 也必须像玩家一样打**——只从**兵力最多的出发省**出击
（`PickStagingForce`；分散的部队凑不出攻势）+ 胜率 <45% 不打。加这两条之前，bot 要么第 2 回合
全军覆没，要么 30 回合只打 4 场，**两种都会掩盖真实的平衡问题**。

### 16.5 M3 战斗桥接（已实现，默认关闭）

`Game/BattleLauncher.cs`（开打）+ `Game/BattleBridge.cs`（生命周期）+ `Game/RosterSpawner.cs`（投送）。

- **开打路径**：`CampaignsTree.cachedBattles` → `ICampaignTreeBattleData.LoadBattle()` → `IBattle`
  → `CampaignsTree.StartMissionCR(battle, null, null)`（原生协程，需显式 StartCoroutine）。
  **能拿到 `CampaignsTree` 实例的原因**：征服页借用原生「战役」页签（`NativeTabHost.Enter`），
  所以战役页对象是活的、`cachedBattles` 是填好的。
- **投送**：`SpawnManager.SpawnAISquadGlobal` + `DelegateSupport.ConvertDelegate`（照抄 UniGen 范式）。
  出生点 = 场景里**我方阵营**的 `SpawnManager.wayPoint`，兜底 `GetBorderCenter(currentPhaseNum)`。
- **结算**：轮询 `BattleManager.IsBattleEnded()` + `instance.GetCurrentWinnerFactionBasedOnCurrentPhase()`
  → `BattleSim.ResolveRealBattle`（新增 `forcedAttackerWin` 重载）——**与自动结算同构**，养成输入源统一。
- **兜底**：启动失败 / 10 分钟超时 → 日志写原因 + **自动回退自动结算**（战役绝不卡死）；
  回主菜单后自动重开征服页并弹出跨场景保留的战果。

**⚠️ 未验证点（如实记录）**：`StartMissionCR` 的两个 `Transform` 参数语义**无法从 interop 反编译看出**
（原生协程体在 native 侧）→ 传 null 并在日志报告实际返回值。因此该路径 **cfg `Battle.RealBattles` 默认 false**，
由玩家逐场试；本文档与台账都不把它当作"已验证"。

**编译期抓到的类型错误**：`SpawnManager.faction` 是 `Faction` **枚举**（不是 string）、
`wayPoint` 是 `Il2CppSystem.Nullable<Vector3>`（**不是 Transform**）——两者都是"想当然"就会踩的坑。

### 16.6 顺带补的产品缺口

- **读档列表**：此前重启游戏后战役无法恢复（`ConquestSave.ListSlots` 早已实现但 UI 从未使用）。
- **双语文档**：`README_CN.txt` / `Nexus_description_CN.md`。
  **踩坑实录**：第一次打 CN 包时 `README_CN.txt` 尚不存在，`build.ps1` 的
  `if (Test-Path $localized)` **静默回退英文**（正是 AGENTS §6 记录的"对缺失文档静默跳过"）
  → 两个 zip 的 README 字节数完全相同才暴露 → 补中文文档后 CN 包内容才正确。

### 16.7 下一步

- **请用户实测**：新建战役 → 研发 → 征兵 → 自动结算 → 结束回合的完整循环（战略层已离线验证，需实测 UI 与手感）。
- 若愿意，开 `Battle.RealBattles` 试一场【亲自出战】，**把日志发回**——那一步决定 M3 是否成立。
- 之后：战斗内按班回读伤亡（现在战损由结果推导）、SP 换战场支援（炮击/空袭）、载具征募。
