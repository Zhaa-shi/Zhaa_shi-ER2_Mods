# ER2 无尽模式（Endless）设计方案

> ⛔ **本项目已终止（2026-09-26），源码已删除。**
>
> 终止原因：**全流程复杂度失控** —— 设计野心超过了单人可维护的规模，而非技术不可行。
> 走完 v1–v16 共 16 版迭代，M0 战斗尖峰已打通（合成战斗文件 + 原生加载链 + 自建页隐形交接），
> 但 M1 之后的战略层（兵营/经济/持久存档/状态桥）体量过大。
>
> **本文件保留为技术复盘**，§14 是完整的逐轮实测实录（16 轮踩坑，含大量原生 UI 与
> 战役页生命周期的第一手结论）。**再碰"注入原生菜单/页面"类需求前请先读 §3 与 §14。**
>
> **可回收资产**已提取到 `Shared/NativeUi/`（原生字体/旗帜/日期排版/页签宿主/输入拦截），
> 见其 README。
>
> 三条最通用的教训：
> 1. **手工 `new` 原生数据对象是半成品** —— 形能画出来但内部状态缺失，点击链静默失败。
> 2. **原生工厂会拒绝外来输入**（`AddToCampaignData` 返回 null），必须落盘成真实文件让原生自己包装。
> 3. **别和原生页生命周期抢控制权** —— 原生在页签打开时会重建对象，手里的指针必然失配。

> 2026-09-19 起草。前置：**征服模式开发终止**（复盘见 `ER2_征服模式_设计方案.md` §14/§16 与本文 §1）。
> 拟建 mod：目录 `Endless/`，GUID `er2.endless`，显示名 `ER2 Endless`，DLL `ER2_Endless.dll`。
> 本文件是**方案**（未动手）。API 断言来自本机 interop 反编译（`research_out/conquest_recon/` + 本次补充核对），标注了验证状态。

---

## 0. 一句话定位

**Roguelite 式遭遇战循环**：组建自己的队伍 → mod 挑一张随机原生地图、生成一场敌军实力与队伍相当的战斗 → 打完拿奖励 → 用奖励扩大队伍 → **阵亡永久**。
没有战略地图、没有回合制、没有研究树、没有领土——就是征服模式砍掉战略层之后剩下的那个核心循环。

**两条铁律**（从征服模式继承）：
1. **循环一句话能讲清**："组队 → 打一仗 → 领赏 → 扩队"。任何新想法先问"它属于这句话里的哪一步"，不属于就不做。
2. **战斗全走原生管线**（原生地图、原生加载、原生 AI、原生目标区、原生胜负判定），mod 只负责"派谁上场、敌人是谁、打完怎么算"。

---

## 1. 从征服模式继承什么、抛弃什么

### 1.1 终止原因（如实记录）

- **战略层是复杂度主源**：17 战区 / 206 省 / 回合制 / 四资源 / 研究树——系统间的相互作用让平衡和 UI 都指数变难；五轮实测返工里四轮都在解决"玩家看不懂这是在干什么"。
- **最高风险的技术环节（合成战斗的启动链路）被排到最后**（M3/M4），养成系统做完后战斗桥接仍未实测 → 核心乐趣始终无法验收。
- 用户真正想玩的是**"持久队伍 + 随机战斗 + 阵亡永久"**，即本方案。

### 1.2 直接回收的资产（已实测验证，不重交学费）

| 资产 | 验证状态 | 无尽模式用途 |
|---|---|---|
| `Conquest/UI/MenuEntry.cs`（克隆 Campaigns 按钮 → 插位 [7] → 接管持久监听器） | ✅ 游戏内实测，按钮位置正确 | 改个文本就是无尽模式入口 |
| `Conquest/UI/NativeTabHost.cs`（借原生战役页签 + 看门狗 Enforce） | ✅ 第三轮实测定稿 | 无尽页宿主，相机由原生驱动 |
| `Conquest/UI/InputBlocker.cs` + `UiTheme.InputEnabled` | ✅ 第四轮实测定稿 | 防点击穿透 / 弹窗交互门 |
| `Conquest/UI/UiTheme.cs`（带式布局 + 原生字体 + 描边 + 点击归属） | ✅ 多轮定稿 | 全部页面骨架 |
| `Conquest/Game/NativeAssets.cs`（`LocalizationManager.GetFont` / `ResourcesManager.GetGUISTyle` / 阵营旗帜 sprite UV / `Language.GetText`） | ✅ 已实测 | 原生观感 |
| `Conquest/Game/VeteranLink.cs`（HVT 反射对接，失败可重试不缓存否定结果） | 已实现未实测 | 老兵系统（软依赖） |
| `Conquest/Core` 的 `DeterministicRng` / 行式键值存档 / `StepCurve` | ✅ 自检覆盖 | 存档与随机 |
| `Conquest/tools/CoreTest` 离线自检宿主模式 | ✅ 抓出过 6 个真缺陷 | Core 自检 |
| `research_out/conquest_recon/`（43 个反编译文件 + 195 KB dump） | ✅ 实测数据 | 侦察起点 |
| UniGen 的 `SpawnAISquadGlobal` + `StartCoroutineNative` 范式 | ✅ UniGen 长期验证 | 运行时投送兜底路线 |

### 1.3 本次新核对的 API（2026-09-19，interop 现版）

| API | 结论 |
|---|---|
| `GameMode` 枚举 = `Unknown / Operation / PushTheFrontline / Custom` | 合成战斗用 **`GameMode.Custom`**（任务编辑器战斗即此模式） |
| `MissionEditorBattle.SaveBattleDataToFile(out string error, string overridePath = null)` → bool | 存盘签名干净；**返回值+error 待尖峰实测** |
| `CampaignTreeCustomBattleData{path, filename, workshop_id}` → `LoadBattle()` → `IBattle` / `LoadBattleAsync(Action<IBattle>)` | 自定义战斗加载器，战役菜单加载 ME 战斗走它——**主启动路线** |
| `SelectedMissionProcesser.OnProcessMission(MissionEditorBattle)`（virtual） | 原生有直接"吃内存 MissionEditorBattle"的入口——**若可用则连落盘都不需要**（尖峰验证） |
| `Creature.IsAlive` / `Creature.IsDead`（属性） | 战后伤亡回读可行（`Squad.units` 逐个判） |
| `Squad.units`（`Il2CppSystem.Collections.Generic.List<Soldier>`）/ `squadName` / `CountMembers` | 与 HVT 对接时的既有结论一致 |
| `BattleManager.IsBattleEnded()` / `IsBattleActive()` / `GetCurrentWinnerFactionBasedOnCurrentPhase()` / `OnWin(string, bool)` / `SetTickets(float, BattleData)` | 原生胜负挂点（已反编译确认存在） |

---

## 2. 核心循环

```
主菜单 [无尽模式]（「战役」与「多人」之间，克隆原生按钮）
  → 无尽页（借原生战役页签宿主，原生观感）
     ├─ 新建档案（仅首次）：选阵营（开始后固定）→ 送 2 个满编班
     ├─ 兵营：班列表（番号/兵种/编制/存活/老兵）· 征募 · 补员 · 解散
     ├─ 回档：从战斗前快照恢复（§7.2）
     └─ 出击：【开始战斗】
           ↓
  生成任务（mod 自创）：
     随机原生地图（206 战斗池抽样，跳过教学）
     + 敌军编成（预算 = 我军战力 × 难度系数）
     + 任务目标（v1：夺取全部目标区）
           ↓
  战斗：原生加载 .mer2 → 原生 AI / 目标区 / 胜负
     玩家 = 自己班里的一个兵；阵亡 → 接管本班另一名存活士兵
           ↓
  结算：胜负 · 我方各班伤亡逐人回读（死亡即除名，全灭即撤编）
        · 奖励 = 基础 + 击杀 + 目标区 + 连胜倍率
           ↓
  自动存档 → 回兵营
```

---

## 3. "UI 等完全用游戏原生"的落地定义

这是**本 mod 的第一设计约束**（用户明确要求）。技术上"原生 UI"拆成六条可验收标准：

1. **入口** = 克隆原生按钮模板（字体/尺寸/悬停态与原生完全一致），插「战役」「多人」之间。✅ 已有实测代码。
2. **页面宿主** = 借用原生页签系统：`MainMenu.OpenTab(camaigns_tab, true)` 让**原生代码**驱动相机与页签切换，mod 只隐藏战役页内容并借壳作画。✅ 已有实测代码。**不自己写一行相机代码**（征服第三轮教训：原生在驱动的状态自己再写必然打架）。
3. **视觉语言** = 原生资产：`LocalizationManager.GetFont` 字体、`ResourcesManager.GetGUISTyle` 样式、`ResourcesManager.GetFactionData().flag` 旗帜、`Language.GetText` 本地化文案、原生日期排版（`12 五月 1945`）。
4. **布局骨架** = 照抄原生战役页：左上「◀ 返回」+ 顶部居中大标题 + 左侧竖排列表（选中高亮）+ 内容区卡片网格 + 右上页签；**带式布局**（BandTop/BandStatus/ContentY 硬分带）从结构上杜绝重叠。
5. **交互** = `InputBlocker`（独立 Canvas 全屏图元挡 uGUI 射线）+ `UiTheme.InputEnabled`（IMGUI 点击归属：先画的先拿事件，弹窗打开时关掉下层交互）+ 弹窗多重关闭路径（按钮/任意点击/ESC/超时兜底）。
6. **禁自定义滚动**（IL2CPP 裁剪 `BeginScrollView`）→ 列表分页或只绘可见行。

**验收方式**：每个新页面截图与原生战役页并排比对；任何"不像 ER2"的元素（配色/字体/间距/文案）当场改掉。

---

## 4. 队伍系统（兵营）

### 4.1 单位粒度 = 小队/班（沿用征服已定决策 #5，与 SquadCommand 同粒度）

每班 = 一条持久实体：

| 字段 | 说明 |
|---|---|
| 番号 | 如「第 1 班 EASY」，自动分配，可改名（情感核心：死一个班有感觉） |
| squadType | 原生 `SquadType`（56 个全部有效，`ItemsDatabase.GetSquadLoadouts` 实测非空） |
| 编制/存活 | 编制人数来自目录；存活数战后逐人回读 |
| 老兵 | 班级击杀累计 → Lv（KillsPerLevel=5 / MaxLevel=5，与 HVT 语义一致）；HVT 在场时灌进战斗，不在场时 mod 自己施加倍率（镜像计算） |

### 4.2 阵亡规则（用户核心要求："单位死亡后就真的死亡了"）

- **逐人持久**：战后按班回读存活数（`Squad.units` × `Creature.IsDead`，时机：`IsBattleEnded()` 首帧立即抓，缓存——防结算界面出现时对象已销毁）。
- **缺员保留**：8 人班战剩 5 人 → 下场就是 5 人（不用回存档点重置）。
- **全灭 = 撤编**：班从名册永久移除，进战史（"第 3 班 FOX · 1945.6.12 · 卡昂 · 全员阵亡"）。
- **补员**：花钱把缺员班补满；单价 < 新班（鼓励保留老兵班）。
- **玩家自己**：玩家是自己班里的一个兵；阵亡 → 尝试原生接管（`TakeControlOfSpawned`，签名待核对）切到本队另一名存活士兵；**玩家个人死亡不等于战斗失败**（按战线结果结算，沿用征服 §5.4 决策）。

### 4.3 出击上限

每场最多带 N 个班（默认 3，cfg 可调）。队伍可以大于出击规模 → 伤亡管理、轮换、保留老兵班才有意义（"扩大队伍"不只等于"场上更多"）。

### 4.4 阵营与开局（2026-09-19 用户已定）

- **阵营**：新建档案时可选（原生战斗方），**开始后固定**——写死进存档，后续战斗阵营、兵种目录、敌军阵营都由它派生。
- **开局**：送 2 个满编普通步兵班 + 首场战斗锁定低强度（§5.3），保证前 10 分钟就能体验完整循环。
- **战败**：没有奖励也没有额外惩罚——伤亡本身就是代价；想撤销上一场用回档（§7.2），**阵亡永久是默认规则，回档是玩家给自己的豁免权，两者并存**。

### 4.5 跨 mod 单位状态保留（2026-09-19 用户明确要求）

**别的 mod 给单位上的状态，在无尽模式里也要保留**（典型：Veteran HVT 的老兵等级/击杀）。做成"状态桥"机制，不写死 HVT：

```
开战生成时：roster → 逐班向各状态桥灌状态（如 HVT ApplySquadLevel / SetKills）
战后结算时：从存活班组回读各状态桥 → 写进该班的存档字段
```

- 每个状态桥 = 一个反射对接类（同 `VeteranLink` 范式：懒查找、失败可重试、**绝不缓存否定结果**、全程 try/catch）；查找失败 = 该 mod 未装，对应字段跳过，其余功能不受影响。
- **v1 实现 HVT 桥**（kills/level，`VeteranApi` 已有公开接口）；桥列表可扩展，存档里每班留通用 `mod_states` 键值段，未来其他 mod 同模式接入。
- 语义约束：状态的**最终归属是无尽模式存档**（持久实体），战斗内实例状态每次由桥重建——HVT 每场战斗结束会重置等级（其 `BattleEndResetPatch`），正好由桥在开战时灌回。

---

## 5. 战斗生成（mod 自创任务）——**风险最高，放第一个尖峰**

### 5.1 地图与壳

- **地图来源**：原生 206 场战斗池（`VanillaCampaignsList.VisibleVanillaCampaigns` → `BattleData`），带 map/阵营/日期/天气/经纬度；过滤 `aberdeen_training` 等教学图。**随机抽**（种子进战史，可复现）。
- **战斗壳**：程序内构造 `MissionEditorBattle`（`GameMode.Custom`）：
  - `battleData`：map/阵营/日期/天气 来自抽中的原生战斗；`maxAllies`/`maxAxis` 按出击规模设置；tickets 给足（胜负由目标区决定，不让票数先耗尽）。
  - `vphases[0]`：`conquer_areas`（1-3 个目标区 + `secureTime`）+ 敌军 `unit_spawns`（防守方）+ 我方 `unit_spawns`（进攻方，`custom_squad_id` 指向 roster 生成的 `CustomSquad`，`persistent=false` 不自动补员）。
  - `customSquads`：roster 逐人 → `CustomSquadMember{weap1_id, uniform_id, ...}`（样本 `.mer2` 里 `customSquads.Count=1` 证明该列表可被原生序列化往返）。
- **落盘**：`SaveBattleDataToFile(out error)`。存哪：优先游戏自己的 ME 目录命名约定（`StreamingAssets/Missions/` 下，S5 已验证可读）；`overridePath` 备选指向 `SaveDataManager.persistentMissionEditorPath`（LocalLow，保证可写）。**以尖峰实测定**。
- **启动**：`CampaignTreeCustomBattleData{path, filename}.LoadBattle()`（战役菜单加载 ME 战斗的原生路径）。备选：`SelectedMissionProcesser.OnProcessMission(MissionEditorBattle)` 内存直启（成立则免落盘）。征服验证过的 `StartMissionCR` 不作首选（两个 Transform 参数语义始终未明）。
- **胜负**：全原生——目标区占领（`secureTime`）+ `IsBattleEnded()` / `GetCurrentWinnerFactionBasedOnCurrentPhase()`。

### 5.2 兜底路线（尖峰失败时逐级降级）

| 路线 | 做法 | 触发条件 |
|---|---|---|
| **A（主）** | 上述合成 .mer2，双方 spawns 全烤进阶段 | 默认 |
| **A'** | 同 A 但不落盘，走 `OnProcessMission` 内存对象 | A 的存盘校验失败 |
| **B** | 加载抽中的**原生战斗**当壳（敌方原样）+ 运行时 `SpawnAISquadGlobal` 投送我方 roster + mod 侧改判胜负 | A/A' 全败（B 就是征服路线 B 的设计，投送范式 UniGen 已验证） |

### 5.3 敌军匹配（"与玩家队伍实力相似"）

- **我军战力** = Σ 班( 编制存活人数 × 兵种权重 × (1 + 0.15×Lv) )。兵种权重：基础 1.0，机枪/狙击 1.3，工兵/通信 0.8，反坦克 +0.5（进 `UnitCatalog` 可调）。
- **敌军预算** = 我军战力 × 难度系数：
  - **首场锁定 0.4 且敌军只出普通步兵班**（保底教学仗，§4.4 开局配套）；第 2 场起 0.8，每连胜 +0.1 封顶 1.4，连败 −0.1 下限 0.6——**自动贴合玩家水平**，这就是"无尽"的难度曲线，不需要难度选项。
- **编成生成**：按抽中地图的守方阵营 + 年代，从兵种目录随机抽班花掉预算（允许 ±10% 溢出），部署在目标区周边。
- 我方出生区 = 地图进攻方原生出生带；敌方部署在目标区（`conquer_areas` 中心散布）。

### 5.4 任务类型（mod 自创任务，v1 只做一种）

- **v1 · 进攻-占领**：夺取全部目标区 = 胜；我方全灭 = 败；超时（无原生计时则按 tickets）判负。
- v1.1 候选：防守-固守（撑 X 分钟 / 歼灭来敌）、歼灭战。**不加进 v1**（铁律 1）。

---

## 6. 奖励与经济

- **单一货币：军费**。战胜 = 基础 100 + 击杀 × 10 + 每目标区 50，× 连胜倍率（1 + 0.1×连胜，封顶 1.5）；**战败 = 什么都没有**（无奖励、无额外惩罚，2026-09-19 已定——想撤销上一场用回档 §7.2）。数值全部进 Core 规则类 + cfg 可调。
- **成本**（沿用 Conquest UnitCatalog 启发式）：新班 = 编制人数 × 兵种权重 × 40；补员 = 缺额 × 25（约为新班单价的 6 折）。
- **节奏锚点**：开局送 2 个满编普通步兵班（§4.4）；一场胜仗 ≈ 0.6~1 个新班的钱 → "打一仗就能感觉到队伍在长大"。
- **自检**：Core 纯 C# → `tools/CoreTest` 离线跑 30 场模拟战役，护栏：经济不爆炸、连胜期敌预算不超过可击败范围、阵亡率不把队伍在 5 场内打光。

---

## 7. 持久化

- **格式**：行式键值（`ConquestSave` 同款：零依赖、人类可读、缺字段容错、版本号头行）。
- **位置**：`SaveDataManager.persistentSavePath`（= `%LocalLow%\CorvoStudio\Easy Red 2\saves\`）下 `endless_save_1.txt`；**绝不碰 `gamedata.er2`**。
- **时机**：战斗结算后 + 每次征募/补员/解散后自动存。v1 单存档位（无尽模式一个档就够，三槽是伪需求）；写临时文件 + 原子替换。
- **内容**：军费 / 阵营（建档后固定）/ roster（逐班全字段 + `mod_states` 跨 mod 状态段 §4.5）/ streak / 战史（最近 50 条）/ 存档版本 / **规则参数快照**（平衡参数变更后旧档标记，避免隐性语义漂移）。

### 7.2 回档（2026-09-19 已定，配合"阵亡永久"）

- **自动快照**：每次【开始战斗】前把当前存档复制进 `saves/endless_backup/`（环形保留最近 5 份，文件名带时间与场次号）。
- **恢复入口**：无尽页「回档」列表展示各快照（时间 / 场次 / 队伍概况 / 军费），选择恢复 = **先快照当前档**（防误操作），再覆盖。
- 快照只涉及 mod 自己的存档文件，不碰游戏原生存档；战斗中途退出/崩溃的保护 = 结算后立刻落盘（§7 时机不变）。

---

## 8. 架构

```
Endless/                                  er2.endless  ER2 Endless  ER2_Endless.dll
├── Plugin.cs                  入口：生命周期、配置（§7.1 约定 debugLog 开关）、Harmony 挂点
├── Core/                      纯 C#（不引用 Unity/Il2Cpp，可离线自检）
│   ├── Model.cs               SquadUnit / Roster / Profile / BattleLogEntry / MissionSpec
│   ├── UnitCatalog.cs         SquadType → 成本/权重/编制（数据由 Game 侧注入）
│   ├── Economy.cs             奖励 / 成本 / 补员
│   ├── Matchmaker.cs          我军战力 → 敌军预算与编成
│   ├── OutcomeResolver.cs     战果 → 逐班伤亡回写 / 老兵经验 / 军费
│   ├── EndlessSave.cs         行式存档
│   └── SelfTest.cs            离线自检（§6 护栏 + 存档往返 + 匹配单调性）
├── Game/
│   ├── NativeBattleSource.cs  原生 206 战斗池抽样（复用 Conquest）
│   ├── BattleFactory.cs       MissionSpec → MissionEditorBattle（阶段/目标区/CustomSquad）
│   ├── BattleLauncher.cs      SaveBattleDataToFile → CampaignTreeCustomBattleData.LoadBattle
│   ├── BattleBridge.cs        生命周期 / IsBattleEnded 首帧抓伤亡 / 玩家死亡接管 / 战果跨场景带回
│   ├── RosterSpawner.cs       兜底路线 B 的运行时投送（UniGen 范式）
│   └── VeteranLink.cs         HVT 对接（复用 Conquest）
└── UI/
    ├── MenuEntry.cs           克隆原生按钮（复用 Conquest，改文本「无尽模式」）
    ├── NativeTabHost.cs       原生页签宿主（复用）
    ├── NativeAssets.cs        原生字体/旗帜/样式/本地化（复用）
    ├── InputBlocker.cs / UiTheme.cs（复用）
    ├── EndlessRoot.cs         页面框架（返回/标题/页签，带式布局）
    ├── BarracksPage.cs        兵营（左班列表 + 右详情/征募卡片）
    └── ResultPanel.cs         战果弹窗（伤亡名单 + 奖励明细 + 多重关闭）
```

**分层原则**（继承征服）：`Core/` 不碰 Unity；`Game/` 是唯一与游戏耦合层；`UI/` 只读 Core、只调 Game 入口。**我方班的战斗内识别**：`CustomSquad` 命名带前缀（`endless_<unitId>`），战斗内按 `Squad.squadName` 前缀匹配（生成回调拿不到时兜底）。

---

## 9. 与现有 mod 的契约

| mod | 关系 |
|---|---|
| **SquadCommand** | 软依赖：战斗内可切上帝视角指挥自己的班（反射调用，缺失静默降级为纯 FPS）——用户已装，体验加成显著 |
| **HighValueTarget** | 软依赖（`VeteranLink` + §4.5 状态桥）：战后快照老兵 kills/level、开战灌回；没装时 mod 侧镜像倍率降级 |
| **Hide Anything** | `HudCompat.knownNames` 预注册 `{"er2.endless","ER2 Endless"}`；战斗内若有 HUD 绘制则每帧查 `IsHidden` |
| **ModManager** | 按约定接 `Config.Bind("Debug","debugLog",false)`；高频诊断日志全门控，失败路径日志不门控 |
| **UniversalGeneration** | 不依赖，只复用范式 |
| 通用 | BepInEx 字母序加载 → 跨 mod 查找**绝不缓存否定结果**（Conquest 实测陷阱）；所有节流 `Time.unscaledTime` |

---

## 10. 已定决策与开放问题

**已定（用户 2026-09-19 提出）**：① UI 等完全用游戏原生 ② 入口在「战役」「多人」之间 ③ 随机原生地图 ④ 敌军实力≈我方队伍 ⑤ 战后奖励扩队 ⑥ mod 自创任务 ⑦ 全程状态保留 ⑧ 阵亡永久。

**开放问题已全部裁决（用户 2026-09-19）**：
1. **玩家阵营**：开始前可选，开始后固定 → §4.4。
2. **开局条件**：送 2 个满编班 + 首场锁定低强度 → §4.4 / §5.3。
3. **战败**：无奖励也无额外惩罚；**支持回档** → §6 / §7.2。
4. **队伍规模**：无上限（出击每场 3 班的取舍不变）→ §4.3。
5. **（追加）跨 mod 单位状态也要保留**（如 HVT 老兵）→ §4.5 状态桥。

---

## 11. 里程碑（**尖峰前置**——征服最大的流程教训）

| 阶段 | 内容 | 验收标准 |
|---|---|---|
| **M0 战斗尖峰** | 合成最小 `MissionEditorBattle`（1 目标区 + 敌 2 班 + 我 1 个 CustomSquad 班）→ 落盘 → 启动 → 完整打完 → 结算界面 → 回主菜单；顺带观察：玩家死亡的原生表现、`EndBattleGUI` 流向、`Squad.units` 伤亡回读时机 | **游戏内完整打完一场合成战斗**（一票否决项：失败则评估 A'/B 路线，形态确定后再继续）。**尖峰工具已实现**（`Endless/DevSpike.cs`，临时脚手架）：F6=启动链验证（原生 ME 战斗 → `LoadBattle` → `StartMissionCR(battle,null,null)`，三组 path/filename 组合自动尝试）；F7=克隆改造（读源 → 裁到 1 阶段/≤3 生成点 → `SaveBattleDataToFile` → 全树定位落盘 → 读回校验 → 开打）；F8=`SelectedMissionProcesser.OnProcessMission` 内存直启探针；另有 0.5s 低频生命周期观察窗（`IsBattleActive`/`IsBattleEnded` 迁移 + 胜方判定）。**2026-09-19 编译通过已部署，待游戏实测** |
| **M1 壳 + 存档** | mod 骨架 + 入口按钮 + 页签宿主 + 新建档案（选阵营，开始后固定）+ 兵营页（静态）+ 存档往返 | 游戏内新建/读档；按钮位置正确；无点击穿透 |
| **M2 兵营闭环** | 征募/补员/解散/番号 + 经济 + 回档（快照/恢复）+ 自检宿主 | 花钱买班、重进游戏还在；回档恢复到上场战斗前；离线自检全绿 |
| **M3 出击闭环** | Matchmaker（首场锁定低强度）+ BattleFactory 接 roster + BattleBridge + 结算弹窗 + 自动存档 + 战前自动快照 | 完整循环连打 3 场：伤亡持久、全灭班消失、奖励入账、难度随连胜爬升 |
| **M4 打磨发布** | 跨 mod 状态保留（HVT 桥灌回/回读）+ SquadCommand 联动 + 战史页 + 双语文档 + 验收三件套 | Nexus 可发布 |

M0 期间可并行 M1（纯壳工作，互不依赖）。**M3 之前不写任何"锦上添花"**（任务类型、载具、阵营选择……全在 M4 之后的欲望清单里）。

---

## 12. 风险表

| 风险 | 等级 | 对策 |
|---|---|---|
| `SaveBattleDataToFile` 拒绝程序合成的对象（编辑器版本/字段校验） | **高** | M0 尖峰第一项；失败 → A' 内存路线 → B 原生壳 |
| 合成战斗里敌军 AI 行为异常（`ai_script_file` 为空、不守点） | 中 | M0 观察；目标区自带吸引 + 必要时填 `destination`；再不行退 B |
| 玩家死亡在 Custom 模式的原生行为未知（可能强制结算） | 中 | M0 观察；接管 API 兜底；最坏接受"玩家死=战线结算"并写进说明 |
| 伤亡回读时机（结算时 Squad 对象已销毁） | 中 | `IsBattleEnded()` 首帧立即抓全量快照缓存；诊断日志核对 |
| 出击时机/场景竞态（陷阱 24/26：主菜单存在 BattleManager、未验证信号不当门控） | 中 | 只用实测过的信号；每步诊断日志 |
| 游戏 2.1.x 静默更新改签名（陷阱 29） | 低 | 核心原生调用点集中 + 反射化；更新后先查 `MissingMethodException` |
| 经济/难度失衡 | 低 | Core 离线 30 场模拟 + 实测校准（征服自检模式已证明有效） |

---

## 13. 明确不做（v1 边界）

- 无战略地图 / 回合 / 领土 / 研究树 / 多资源（**这是放弃征服的原因，不允许长回来**）
- 无 COOP / 联机
- v1 无载具征募、无多种任务类型（阵营已定：建档时可选、之后固定，§4.4）
- 不改游戏原文件；不读写 `gamedata.er2`
- 不重做任何原生战斗系统（AI / 弹道 / 目标区全原生）

---

## 14. M0 尖峰实测记录（2026-09-19 起）

### 14.1 首轮实测（热键版）——存盘/读回/加载全通，卡在"战斗未激活"

| 环节 | 结果 |
|---|---|
| 克隆改造（4 阶段→1、18 生成点→3、关补员） | ✅ |
| `SaveBattleDataToFile(out err, null)` | ✅ **ok=True error=无**（A 路线的存盘风险解除） |
| 落盘位置 | ✅ `GetSavePath` = `%LocalLow%\Corvostudio\Easy Red 2\mission_editor\<fileNameID>\<fileNameID>.mer2`（自建目录，LocalLow 保证可写） |
| 读回校验（`LoadFromFile` + `LoadBattle`） | ✅ phases=1 spawns=3 |
| `StartMissionCR(battle, null, null)` | ⚠️ **加载了场景但战斗未激活**：mod 侧日志见"new battle"/碰撞矩阵初始化，但 `BattleManager.IsBattleActive()` 始终 false，最终弹回主菜单。**原生完整 ME 战斗（阶段①）同样弹回 → 是启动调用缺上下文，不是合成内容问题** |
| 路径组合 | ✅ "目录+带扩展名文件名"（A 组合）即成功，无需尝试相对路径 |
| 细节 | 生成点 `faction` 枚举打印 Unknown——真实阵营在 `faction_id` 字符串里（与 HVT 阵营串陷阱一致）；`SelectedMissionProcesser` 在主菜单场景找不到实例（可能在战役页对象上） |

### 14.2 第二轮改造（应用户要求去热键）

- **入口**：克隆原生「战役」按钮 → 插「战役」「多人」之间 → 文本「无尽模式/ENDLESS」（回收 Conquest M1 的 `MenuEntry.cs`，含持久监听器三重保险）。**主菜单不再有任何热键**。
- **页面**：借原生战役页签（`NativeTabHost`）+ 原生观感 IMGUI（`UiTheme`/`NativeAssets`，均回收自 Conquest 快照）+ `InputBlocker` 防穿透。尖峰三按钮：①原生 ME 战斗开打 ②合成战斗开打 ③任务处理器直启（`OnProcessMission`，借页签后处理器可能存在）。
- **深度观测器**：启动后 25s 内每 0.5s 采样 **活动场景名 + BattleManager 状态 + 主菜单是否存在**，只记变化——用于定位"弹回主菜单"的准确时刻与场景切换轨迹（上轮只有 `IsBattleActive` 一个信号，太粗）。
- 工程坑（1h 排查）：`Endless.csproj` 漏引 `UnityEngine.UI.dll` → uGUI 代码全报 CS0234，且报错位置（`using` 行）极具误导性；interop 里 `UnityEventCallState` 在 `CoreModule`（无独立 Events 程序集）。**从别的 mod 抄 csproj 引用清单时逐行核对**。

### 14.3 第二轮实测（2026-09-19 用户测试）与 UI 定案

- **实测结果**：用户点了尖峰按钮（截图证据：状态行显示 ③ 运行中），但 **`LogOutput.log` 里一条 `[Spike]` 都没有**——两个原因：① 适配 CoreLog 时忘了把它的输出委托接到 BepInEx 日志器（Conquest 的 Plugin.Load 有这步，搬代码时漏了）→ 所有 `CoreLog.Inf/Err` 静默丢弃；② 当轮日志疑似被游戏异常退出截断（文件尾部有 Soldier::OnDestroy il2cpp 异常）。**已修**：CoreLog 接线补上；CoreLog 断线教训适用于一切"从快照搬代码"——跨层日志出口必须在 Load() 显式接线。
- **UI 定案（用户 2026-09-19 明确）**：「**除了文本内容，UI 要与战役模式一模一样**」——IMGUI 仿制（原生字体+描边那套）不被接受。**弃用 EndlessRoot 的 IMGUI 面板、NativeTabHost 藏内容、InputBlocker 全部方案**。

### 14.4 架构 v3：把合成战斗注入**真实**战役页（100% 原生控件）

依据侦察 dump 的 CampaignMenu 子树（`MissionsPanel`=小地图+战斗卡片 ScrollRect+标题、`LeftPanel`=战役列表、原生 `BackButton`）与 CampaignsTree 公开 API：

| 步骤 | API | 说明 |
|---|---|---|
| 合成战斗 | 克隆→裁剪→`SaveBattleDataToFile` | 已实测全通（§14.1），产出 `mission_editor/EndlessSpike2/EndlessSpike2.mer2` |
| 进战役页 | `MainMenu.OpenTab(camaigns_tab, true)` | **不藏任何东西**——整页原生，含背景/相机/返回键 |
| 战斗加载器 | `CampaignTreeCustomBattleData{path,filename}` → `LoadBattle()` | 已实测 |
| **登记原生缓存** | `CampaignsTree.cachedBattles.Add((bd, loader))`（**public 可写属性**） | 原生点击卡片时 `GetBattleLoaderFromCachedBattleData` 靠它找 loader；`bd.battle_id` 改为 `endless_1` 防撞 |
| **侧栏行** | `tree.TryAddCampaignToLeftMenu(CampaignTreeData)`（原生协程） | `CampaignTreeData{campaing_id="无尽模式", top_bar_id=取样原生值, ugcType=Local, battles=[loader]}`——**原生 CampaignLabel 控件** |
| **战斗卡片** | `tree.GoToCampaign(ctd)` + `tree.AlignMinimap(bd)` | 走原生 `ExploreBranch`/`AddBattleButton`——**原生 BattleLabel_V2 卡片**（日期+旗帜+名字全来自我们的 BattleData） |
| 开打 | 用户点卡片 → 原生 `OnBattleSelected` 链 | **完整原生上下文**——这同时是「加载完弹回」的正解候选 |

**已踩的 interop 坑**（写码前先核对这些）：
- `CampaignTreeCustomBattleData` 在 interop 里**不继承** `ICampaignTreeBattleData`（C# 层面）→ 传接口参数必须 `TryCast<ICampaignTreeBattleData>()`（陷阱 5 的接口版）。
- `VanillaCampaignsList.VisibleVanillaCampaigns()` 是**方法**且返回 interop 泛型枚举（**foreach 会编译错/运行坏**）→ 用 `vanilla_campaign_data`（static `Il2CppReferenceArray<CampaignTreeData>`）+ for 循环。
- 托管协程必须 `.WrapToIl2Cpp()`（`BepInEx.Unity.IL2CPP.Utils.Collections`）才能 `StartCoroutine`；原生协程（TryAddCampaignToLeftMenu 返回值）直接传。
- `MonoBehaviour.StartCoroutine` 的 interop 不收托管 `System.Collections.IEnumerator`，不匹配时报"无法转换为 string"（选中了 `StartCoroutine(string)` 重载）——报错位置极具误导性。

**待实测**：① 侧栏「无尽模式」行显示正常吗（campaing_id 直接当显示文本，本地化键不存在——若显示异常，下一轮克隆原生行的 Text 直接改字）；② 点我们的卡片走原生链能否正常开打（弹回问题的终局判定）；③ ugcType=Local / top_bar_id 取样值是否被原生接受。

### 14.5 第三轮实测（v3）结论与 UI 二次定案

- **实测结果**（截图 + 日志）：手工注入的「ENDLESS」行**出现在了原生 LOCAL 分区**（ugcType=Local 触发原生分区），但 **① 行不可点、GoToCampaign 静默无效**（无异常、内容区仍显示上一个战役）；**② LOCAL 分区把 vanilla 列表拦腰切开**，马金环礁战役等被挤到无尽模式下面。
- **定案教训**：原生控件依赖**原生管线构造的完整对象**——手工 `new CampaignTreeData` 是半成品，行能画出来但内部状态（完成度扫描/战役集合注册）缺失，点击链静默失败；而且插行位置由原生排序决定，会切开 vanilla 列表。**手工构造+插行的路子废弃。**
- **用户 UI 补充要求**：「无尽模式一栏只要无尽模式的选项」——无尽分区里不能混进 vanilla 战役。

### 14.6 架构 v4：全走原生本地战役管线

发现 `CustomCampaignsList`（原生自定义战役管理，static）：

| API | 用途 |
|---|---|
| `AddToCampaignData(f_path, f_filename, f_workshop_id, MissionEditorBattle meb)` → CampaignTreeData | **游戏自己把单个 ME 任务包装成本地战役的入口**（工坊战役同款），产出**完整**的 CampaignTreeData（内部状态齐全）并注册进 `local_campaign_data` |
| `local_campaign_data` / `workshop_campaign_data`（static List） | 原生 LOCAL 分区的数据源——侧栏 LOCAL 分区天生就是「自定义战役」专区 |
| `RefreshCustomCampaigns(local, workshop, callOnAdded)` | 原生扫描刷新（我们不用它——避免把用户自己的 ME_Montcornet 等也列出来） |
| `CampaignsTree.OnNewCampaignListed(ctd)` | 工坊下载完成后原生加行的入口，行完全原生可点 |

v4 流程：合成战斗（不变）→ **落盘到独立目录** `LocalLow/Corvostudio/Easy Red 2/endless/`（`SaveBattleDataToFile` 的 `overridePath` 参数；不在 mission_editor 里，避免被原生扫描重复列出）→ 进战役页 → `AddToCampaignData(dir, file, 0, meb)` → `OnNewCampaignListed(ctd)` → `GoToCampaign(ctd)` + `AlignMinimap`。
显示名：直接改 `meb.battleData.location_name = "无尽模式"`，让侧栏行与卡片原生显示该文本。
防重：重复点入口时先扫 `local_campaign_data` 里是否已有我们的战役（按 id 含 `EndlessSpike2`），有则直接 `GoToCampaign` 不重复加。

**待实测**：① LOCAL 分区是否只出现「无尽模式」一行（用户要求"只要无尽模式"）；② 行可点、卡片可点、原生开打链是否正常（弹回问题终局判定）；③ 侧栏行/卡片显示名是否为「无尽模式」（location_name 直改生效性）；④ `overridePath` 落盘语义（若独立目录失败会自动退回 mission_editor）。

### 14.7 v5：可见性修正（2026-09-19 第五轮）

- **第四轮实测疑点**：截图是原生战役页（靶场教程选中），但日志里**没有入口点击记录**——用户没点「无尽模式」按钮（或点了没触发）。底部 LOCAL 分区在无注入时也存在（游戏自己列本地任务），我们注册的战役埋在列表最底部，**不滚动根本看不见**。
- **v5 修正**：① OpenTab 后**轮询等待**侧栏列表填充完（最长 5s，替代固定 0.8s）；② 注入后**侧栏滚到底**（`ScrollRect.verticalNormalizedPosition=0`，原生控件属性）+ **按文本找到我们的行并 `Button.onClick.Invoke()`**——与玩家点击完全同路径的原生选择链；③ 1.2s 后读标题 Text 核对是否切到位，没切成自动再试一次（共两次）；④ 行查找同时 dump 行清单（名字=文本），失败时日志能直接定位行结构。
- **v5 实测（0.1.1 日志实证）**：版本横幅 ✓、独立目录落盘 ✓（`overridePath` 语义确认：直接写 `<dir>/<fileNameID>.mer2` 不建子目录）、**用户随后手动玩的安齐奥/阿伯丁战役 `active=True` 完整跑通**（原生开打链无弹回问题——v1 的弹回纯属手搓 StartMissionCR）。**卡点唯一化：`AddToCampaignData(独立目录, 文件名, 0, 内存meb) 返回 null`**——原生工厂拒绝输入，后续全没执行。另：侧栏 Content 只查到 6 个子物体（非 17+），行结构存疑，待 dump。

### 14.8 v6（0.1.2）：AddToCampaignData 多组合试探

v5 的 null 有三个嫌疑变量，一次性全试：**内存对象 vs 磁盘回读对象**（存盘可能污染内存对象）、**规范目录 mission_editor vs 独立目录 endless/**（原生工厂可能校验规范位置）、**文件名带/不带扩展名**。流程改为：规范目录存盘（v1/v3 验证过的位置）→ 磁盘回读干净对象 → 依次试 V1(规范+回读) V2(规范+内存) V3(独立+回读) V4(独立+无扩展名)，命中即继续 OnNewCampaignListed+行选中；**全部失败则调 `RefreshCustomCampaigns(true,false)` 原生扫描器做诊断**，dump `local_campaign_data` 注册结果 + 侧栏行结构，用数据代替猜测。版本 0.1.2（部署前确认游戏已退出）。

### 14.9 v6 实测（0.1.2）与 v7（0.1.3）：改用原生扫描器注册

**v6 四组合全 null**（日志逐条实测），但诊断 dump 给出两条决定性数据：
1. **原生扫描器成功**：`RefreshCustomCampaigns(true,false)` 后 `local_campaign_data=[0] id=Anzio name=Anzio battles=1`——这就是我们的 EndlessSpike2（安齐奥克隆），**本地任务按地图归组成战役，我们的文件被原生管线接受了**。`AddToCampaignData` 直调的参数空间猜不全（native 体不可读，疑似校验 meb.workshop 字段与 f_workshop_id 的一致性等），**弃直调，改走扫描器**。
2. **行文本匹配失败的根因**：每行第一个 Text 是分区标签（9 行全是 "Workshop"），战役名在别的 Text 里——必须 `GetComponentsInChildren<Text>()` 全读。

**v7（0.1.3）流程**：合成时把 `battle_id=endless_1` 写进文件（身份标识）→ `RefreshCustomCampaigns(true,false)` → 遍历 `local_campaign_data` 逐战役 `LoadBattle()` 按 battle_id **认领**我们的战役 → `OnNewCampaignListed` 加行 → 滚底+全文本匹配行+`onClick.Invoke()` 原生选中 → 标题核对；标题若显示地图名（如 "Anzio"，扫描器按地图命名战役），**把行名与标题 Text 修正为「无尽模式」**（纯文本替换、控件不动——符合"除文本外一模一样"）。

### 14.10 v7 实测（0.1.3）：**原生链全通** + v8（0.1.4）只留无尽行

**v7 实测（用户截图）**：ENDLESS 战役出现在 Local 分区并被自动选中，标题 "ENDLESS"（location_name 生效），战斗卡片（22 一月 1944 + 国旗 + 单位数角标）正常显示，点击卡片后**原生的「自定义任务设置」面板出现**（多人模式/游玩方式/兼容性/最大单位数/开始按钮）——M0 的「合成战斗 → 原生展示 → 原生设置 → 原生开打」整条链路打通。

**v8（0.1.4）按用户要求收尾两件事**：
1. **侧栏只留无尽行**：选中成功后隐藏 Content 里除我们行以外的全部战役行（`SetActive(false)` + `RefreshCampaignsContentSize`）；**离开战役页签自动还原**（Update 轮询 `MainMenu.currentTab != camaigns_tab` → 逐个 SetActive(true)），停在无尽页期间有 1s 看门狗压住原生重新显示的行——原版「战役」页完全不受影响。
2. **重复入口防重**：进页前先按 battle_id 认领已注册战役，命中则跳过 RefreshCustomCampaigns（防止每次点击重复扫描追加）。

**遗留观察项**：① 战斗卡片上的日期是源任务克隆带来的（22 一月 1944 = 安齐奥登陆日），M3 换成 Matchmaker 生成的随机地图/日期；② "08:03" 与「多人模式」开关等设置项为原生面板自带，无尽模式不需要的项后续评估隐藏；③ 点「开始」后的完整开打链这轮未实测（观测器会记录 scene/active 迁移）。

### 14.11 v8 实测（0.1.4）失败根因与 v9（0.1.5）：对象级行操作

**v8 日志实证**：认领成功（`[0.0] id=Anzio battle_id=endless_1`，注意**游戏自己在启动时就会扫描本地任务**——不用显式调 RefreshCustomCampaigns 也认领得到）、OnNewCampaignListed 已调用、但 `第1次找行: 未找到` → 隐藏/改名带 null 直接返回，全没执行。找行失败的两个原因：① 行文本是 "Local/Anzio"（扫描器按地图命名战役），按 "ENDLESS" 匹配必落空；② Button 挂在行内子物体上，行根 `GetComponent<Button>()` 为 null，兜底也落空。

**决定性发现——`CampaignLabel_V2` 的字段就是行操作的正解**：
| 字段 | 用途 |
|---|---|
| `treeData`（CampaignTreeData） | **行 → 战役对象的直接引用**：`label.treeData.Pointer == ctd.Pointer` 按指针精确认行，零文本依赖 |
| `btn`（Button） | 行按钮本体（在子物体上），`label.btn.onClick.Invoke()` 原生点击 |
| `camapaignTitle`（Text） | 行名文本（原生拼写），直接改写为「无尽模式」 |
| `campaignType`（Text） | 分区标签（"Local"/"Workshop"）——v6 把它当行名的根源 |

**v9 流程**：轮询（8s）按 treeData 指针找行 → `label.btn` 点击 → `camapaignTitle.text=无尽模式` + 顶部标题同步 → 隐藏其余行；看门狗每秒维持（行被重新显示则压回、行名被改回则重写）；离开战役页签/场景重载时还原并清引用。**方法论教训：找到原生控件的字段就用字段，别用文本/层级猜。**

### 14.12 v9 实测（0.1.5）与 v10（0.1.6）：自愈式看门狗 + 存档列表定案

**v9 日志显示全链成功**（认行→点击→行名 Anzio→ENDLESS→标题→隐藏 23 行→就绪），但用户随后截图暴露两个问题：① 原生在导航过程中**重建了列表**，新行是新实例，v9 藏的旧引用全变死引用 → 杂乱回返；② `OnNewCampaignListed` 无条件加行，而**游戏启动扫描已经为本战役建过行** → 出现两个 ANZIO。

**v10 修正**：
1. **自愈式看门狗**：不再记"藏了哪些行"，停在我们页签期间每秒重扫 Content——`CampaignLabel_V2.treeData.Pointer != _ourCtd.Pointer` 的行一律压掉，我们的行保证可见且行名正确（被改回就重写）。列表重建、新行、原生刷新全部被下一秒的重扫覆盖。
2. **行去重**：加行前先按指针找行（轮询 2s），已存在就跳过 OnNewCampaignListed。
3. 还原逻辑改为全量：离开页签时把 Content 里所有隐藏行 SetActive(true)。

**设计定案（用户 2026-09-19）**：**「那一栏应该显示的是不同的无尽模式存档」**——侧栏 LOCAL 区 = 无尽存档列表，**一个存档 = 一条本地战役**（行名 = 存档名），行下挂该存档当前的"下一场战斗"卡片。v1 先单存档；M1 兵营/存档系统落地时扩多存档（每存档一个 .mer2 + 一条战役行；注意扫描器按地图归组的未知行为——同地图两存档是否合并需实测，必要时按存档起不同的 location_name 规避）。

### 14.13 v10 实测（0.1.6）与 v11（0.1.7）：遮罩流 + 重建免疫

**v10 实测（日志 + 三张截图）**：
1. **首次进入完美**：行已存在跳过加行（去重生效）、认行/点击/改名/标题/压回 22 行全部成功——只剩一行 LOCAL/ENDLESS。
2. **二次进入破功**：用户返回后重进，自愈"压回 13 行+10 行"却越压越少——**原生在页签打开时重建了战役对象**（local_campaign_data 换新实例），我们手里的旧 `_ourCtd` 指针失配，把自己的新行也当外人压掉 → 图二空侧栏+教程内容。教训：**指针只对"当次注册的对象"有效，跨导航必须按 battle_id 重新认领**（battle_id 写在文件里，重建也认得）。
3. **黄色竖条**：战役列表自带**左侧**滚动条，内容只剩 1 行时手柄被拉满高。
4. **出戏**：隐藏发生在原生加载动画之后——用户先看到满列表再被裁掉。

**v11（0.1.7）四项修正**：
1. **进页遮罩流**：OpenTab 后立即隐藏 `LeftPanel`（侧栏）+ `Scroll View Missions`（卡片/标题区）→ 原生动画期间这两块是空的 → 等构建、认领、裁剪完成 → 揭示。用户第一眼看到的就是只剩「无尽模式」的页面。12s 超时兜底揭示。
2. **重建免疫**：自愈时若列表里 0 行匹配当前指针 → 先按 battle_id 重新认领（拿到新对象）再压行；认领不到不动手。
3. **滚动条**：无尽模式期间隐藏列表的纵/横滚动条，离开页签还原。
4. **战斗文件只建一次**：`mission_editor/EndlessSpike2/EndlessSpike2.mer2` 存在即复用（不再每回合重写触发潜在重扫）；M3 起由 Matchmaker 决定何时重建该文件。

### 14.14 v11 测试事故与 v12（0.1.8）

**v11 未获有效测试**：会话日志有 0.1.7 横幅但**零点击记录**，日志尾部连续 `Soldier::OnDestroy` NullReferenceException（游戏在阿伯丁战斗中崩溃，疑似 UniGen 场上班点相关，非本 mod 代码路径），日志被截断；用户截图（标题 ENDLESS + 教程内容 + 全列表）不对应 v11 任何代码路径——证据链被崩溃污染。

**v12 修正（日志证据支持的 两处）**：
1. **还原判定**：v10 日志曾出现"刚就绪就触发还原 0 个"——`MainMenu.currentTab` 语义不可靠。改为以 `camaigns_tab.activeSelf` 为准；currentTab 不一致但页签仍激活时，输出诊断行且不还原。
2. **遮罩兜底**：遮罩期间若原生因侧栏被藏而不建行（8s 后 childCount 仍 0）→ 立即揭示，改为可见裁剪（宁可闪一下也不卡死）。

**Plan B（若 v12 仍与原生页生命周期打架即启动）**：放弃"注入原生战役页"，改为**克隆原生控件自建页面**——用 CampaignLabel_V2/BattleLabel_V2 预幻体克隆列表行与战斗卡、原生字体/旗帜/滚动条，布局与刷新完全由 mod 控制。观感与原生一致（同一套预制体），但生命周期不再受原生重建/扫描/动画的牵制。M0 的核心成果（合成战斗文件 + 原生加载链 + CustomSquad/目标区机制）不受影响。

### 14.15 Plan B 落地（0.2.0，2026-09-19 用户拍板"走新路"）

**架构**：
- **无尽页 = 自建 uGUI 页面**（`UI/EndlessPage.cs`）：独立 Canvas（sortingOrder=30000，打开期间背景 Image 同时挡原生 uGUI 点击），内容：背景压暗 + 大标题 + **克隆原生 BackButton**（CampaignMenu/left_buttons/BackButton，接管持久监听器后绑关闭）+ 存档行（v1 单行「无尽模式」，M1 扩多存档循环建行）+ **战斗卡**（原生排版：日期 NativeAssets.DateText / 双方旗帜 NativeAssets.Flag / 战斗名，数据实时从我们的 .mer2 读）。
- **开打 = 交接链**（`Spike.HandoffCr`）：点卡 → 关本页 → `OpenTab(camaigns_tab)` → 按 battle_id 认领战役（重建免疫）→ `CampaignLabel_V2.btn` 选中行（行名改「无尽模式」）→ 按 treeData 指针压掉非无尽行（原生页净化，离开自动还原）→ **按 `BattleLabel_V2.battleData.battle_id` 找到我们的战斗卡并 `btn.Invoke()`** → 原生「自定义任务设置」面板 → 用户按「开始」。全程原生加载链。
- 战斗文件只建一次（v11 引入，保留）。

**interop 备忘**：`new GameObject(name, typeof(...))` 在 interop 下不可用（组件参数要 `Il2CppReferenceArray<Il2CppSystem.Type>`）→ 一律 `AddComponent`；`Button.transition = Selectable.Transition.ColorTint`。

**遗留观察项**：① 克隆返回键的悬停态/导航是否正常；② 战斗卡的旗帜 sprite 来自图集（uGUI Image 对 atlas region 原生支持，比 IMGUI 稳）；③ 交接链全程约 2-4s（页面切换+两次自动点击），观感是否可接受；④ 原生设置面板出现后按「开始」的完整开打链实测。

### 14.16 0.2.0 首测事故与 v14（0.2.1）：证据链与退出兜底

**0.2.0 首测**（用户反馈"页面打开了但看不见选项、无法退出"）：BepInEx 日志里**零构建记录**（"无尽页已打开/构建完成"均缺失）、Player.log 亦无异常——症状指向**构建中途抛异常**（只有画布+背景建出来：有遮罩、点不动、没有返回键），但常规日志无法定位。**根本教训：BepInEx LogOutput.log 每次启动被覆盖，跨启动的测试证据反复丢失。**

**v14（0.2.1）三件套**：
1. **mod 自带文件日志**：`%LocalLow%\Corvostudio\Easy Red 2\endless\endless_log.txt`（每会话截断重开，Step/Detail/CoreLog 双写），证据链不再依赖会被覆盖的 LogOutput.log。
2. **构建逐元素打点 + 退出优先**：返回键最先建（哪怕后续全炸也能退出），背景/标题/存档行/战斗卡各自独立 try/catch，哪步失败日志直接写明元素名与异常全文；字体解析结果（字体名或 null）入日志。
3. **ESC 关页兜底**：页面打开时按 ESC 随时退出。

### 14.17 0.2.2 实测（交接链全通）与 v16（0.3.0）：隐形交接 + 注册临时制

**0.2.2 实测（文件日志完整实证）**：自建页渲染成功（返回键克隆 NRE 仍存在但自建兜底顶上——待修）；点卡交接**全链成功**：认领→点行→行名修正→压 22 行→点战斗卡→**「已交接到原生自定义任务设置面板」**。用户此时提出两条新要求：
1. **原生战役页全程不可见**（交接时跳过去很出戏）；
2. **mod 产生的任务不出现在战役页**（LOCAL 区的 ANZIO 是我们的注册残留）。

**v16（0.3.0）**：
1. **隐形交接**：点卡后**本页保持打开**（状态行"正在进入战场…"），原生页在遮罩后打开并自动作业：选行 → 选卡 → **自动按「开始」**（扫 camaigns_tab 下 activeInHierarchy 的 Button，文本匹配 开始/Start/START）→ 场景切换后本页自动关闭。交接失败任一步 → 关本页露出原生页让用户手动完成（兜底）。
2. **注册临时制**：战斗主档移到 `endless/` 目录（原生扫描范围之外）；交接开始时才复制进 mission_editor 注册，**战斗开始后立即删除副本并从 local_campaign_data 移除** → 原版战役页永久干净；启动扫描也扫不到（无文件）。
3. 返回键 NRE 修复：BuildBackButton 全程独立兜底（克隆失败→自建，自建失败只记日志不炸页面）。

**遗留**：① 自动「开始」依赖按钮文本匹配（开始/Start/START），游戏改版文案变化需跟进；② 战斗中若玩家从暂停菜单"重新开始战斗"，临时副本已被删——是否导致重开失败待实测（M1 存档系统后重新审视）。
