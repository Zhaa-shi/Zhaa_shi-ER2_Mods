# 征服模式参考拆解 —— 《地狱之门》解包数据 + 《人间地狱》规则

> 目的：为 `Conquest/`（er2.conquest）的**战略层补全**与**战斗层取样**提供事实依据。
> 本文分三部分：① GoH Dynamic Conquest **解包实证**（本机 `.pak` 解出的原始定义文件）② HLL 大战场模式**联网检索结果**（含不确定性标注）③ 两者的机制 → ER2 实现的映射表。
> 配套：`ER2_征服模式_设计方案.md`（本项目自身方案）、`research_out/goh_unpack/`（解包产物，163 个文件）。

---

## 0. 解包方法与产物

**关键发现：GoH 的 `.pak` 就是标准 ZIP**（文件头 `50 4B 03 04` = `PK\x03\x04`），不需要专用解包器。

```
E:\SteamLibrary\steamapps\common\Call to Arms - Gates of Hell\resource\
  gamelogic.pak      13.0 MB   8949 个条目（脚本 + set 定义）★ 战略层数据在这里
  properties.pak     72.9 MB   单位属性
  map.pak            14.7 MB   地图
  dlc1.pak / dlc3.pak  3.9 GB / 4.6 GB  战役内容
  music.pak / video.pak / shader.pak
```

解包命令（`.NET ZipFile`；**WinRAR 在这些 pak 上会卡死，别用**）：

```powershell
Add-Type -AssemblyName System.IO.Compression.FileSystem
$z=[System.IO.Compression.ZipFile]::OpenRead("...\resource\gamelogic.pak")
$z.Entries | Where-Object { $_.FullName -like 'script/*' -or $_.FullName -like 'set/dynamic_campaign/*' } |
  ForEach-Object { [System.IO.Compression.ZipFileExtensions]::ExtractToFile($_, (Join-Path $out ($_.FullName -replace '/','\')), $true) }
```

**战略层的定义文件全在 `set/dynamic_campaign/`**（16 个文件，这就是原版征服模式的全部数值）：

| 文件 | 内容 |
|---|---|
| `values.set` | 战区/配对（Regions+AvailableMatchups）、gamemode 引用、全部字段语义注释 |
| `resources_standard/low/high/very_high.set` | **难度档**：CP/资源/收益/风险全表 |
| `duration_short/normal/long/very_long/unlimited.set` | **战役长度档**：旗数曲线、AI 研究阶段曲线、地图选择、地图边界 |
| `unit_research_ger/rus/usa/eng/fin.set` | **五国研究树**（DAG：requires + costs + 网格坐标） |
| `map_points.set` | 战区 → 地图池（17 KB） |

战斗层（多人 `campaign_capture_the_flag` gamemode）在 `set/multiplayer/games/`，单位目录在 `set/multiplayer/units/conquest/`。

---

## 1. GoH Dynamic Conquest 机制实证

### 1.1 资源是**四种**，不是两种

`resources_standard.set:54-101`：

| 资源 | 全称 | 起始 | 胜利收益（按"已打场次"阶梯） | 失败收益 | 用途 |
|---|---|---|---|---|---|
| **MP** | Manpower | 900 | `1:200 4:250 6:300 8:350 10:400 12:600 14:800` | `1:100` | 通用"钱"：买单位、补兵员 |
| **SP** | Special Points | 3 | `1` | `0` | 空袭/炮击等 off-map 支援 |
| **AP** | Ammo Points | 1000 | `5:500 10:900 15:1300` | `1:250` | 弹药：载具补弹、零件修复 |
| **RP** | Research Points | 6 | `3` | `2` | **研究树解锁** |

> 注意阶梯写法 `"起始场次:数值"`：收益随"已打场次"增长，不是固定值。这是"战役越打越大"的数值骨架。

另有一套 **CP（Command Points）**，它**不是国库资源，而是单场战斗的出兵预算**：
`resources_standard.set:96-100`：

```
Budget  { Start "0:125 4:150 8:200 12:250 16:300 20:350" }   // 场次:开局 CP
        { PointsPerSecond 0.25 }                              // 战斗中 CP 收入
        { Limit 500 }                                         // CP 上限
GlobalMaxCP 550
StageCP 60 80 100 120 140     // 5 个阶段，每阶段可用 CP
SpecialCP 50 / EmplacementCP 50
```

**注意 `StageCP` 有 5 个条目 = 5 个阶段**（`values.set:16-19` 的注释明确写了 "number of entries determine the number of stages"）。

### 1.2 地图奖励：省份有"类型"

`resources_standard.set:54-95` 的 `MapRewards`——每场战斗会随机带一个**地图类型**，攻占后额外给资源：

| 类型 | MP | AP | SP | RP |
|---|---|---|---|---|
| Airfield（机场） | 200 | 100 | **6** | — |
| Ammodepot（弹药库） | 200 | **900** | — | — |
| Factory（工厂） | 300 | 500 | — | — |
| Research（研究设施） | 200 | 100 | — | **4** |
| Bonus（普通加成） | **400** | 200 | — | **2** |

### 1.3 风险/难度档（RiskFactor）

`resources_standard.set:37-53`：

| 档 | BotResources | **BotVeterancy** | **Rewards（全部收益倍率）** |
|---|---|---|---|
| Low | 1.0 | 0 | 1.0 |
| Standard | 1.0 | **1** | **1.25** |
| High | 1.0 | **2** | **1.5** |

### 1.4 战损返还与解散返还

`resources_standard.set:34-36`（格式 `{factor MP / SP / AP / RP}`）：

```
PaybackFactor 0.4 0 0 0    // 单位战损时返还 40% MP —— 关键：让"打光"不至于毁灭性
SellFactor    0.6 0 0 0    // 主动解散返还 60% MP
ScavengedVehicleSellFactor 0.6 0 0 0   // 缴获敌车解散返还 60%
```

### 1.5 阶段与 AI 研究阶段曲线

`duration_normal.set`：

```
FlagCount      "2:5 2:5 ... 3:5 ..."    // 每场战斗的旗点数：#启用:总数（前 9 场 2 旗，第 10 场起 3 旗）
ResearchStages "0:1 1:1 2:2 3:2 4:3 ... 27:15"   // 场次:AI 可用的研究等级
MapSelection   "0:1"                     // 场次:地图选择值
MapBorder      "0:1"                     // 场次:使用哪套地图边界
```

`values.set:45-48` 给出**阶段解锁**语义：`StageUnlock "3:2"` = 打满 3 场后解锁第 2 阶段；`ResearchStages` 同理。

### 1.6 研究树：带前置的 DAG，用 RP 购买

`unit_research_ger.set`（17 KB）的真实语法：

```
{IconGap 28}
{positions {"single_officer(ger)" 0 2}}                      // 网格坐标（UI 布局用）

;//---TECH UPGRADES---
{ tech "defense_level_1"  requires "reinforcement_stage_2"                        costs 1  position 2 0}
{ tech "defense_level_2"  requires "reinforcement_stage_3 defense_level_1"        costs 5  position 4 0}
{ tech "reinforcement_stage_2" requires "single_officer(ger)"                     costs 0  position 1 1}
...
;//---Off Map Support---
{"conquest_bf109"  requires "squad_officer_con(ger)"  costs 1  position 2 4}
{"105mm_lefh18_artillery_barrage" requires "squad_officer_kubel_con" costs 1 position 4 3}
;//---SQUADS---
{"squad_regular_con(ger)" requires "squad_sicherung_con(ger)" costs 2 position 1 18}
{"squad_grenadier_con(ger)" requires "squad_regular_armor_con" costs 5 position 8 17}
{"squad_pzgren_armor_con" requires "squad_pzgren_motor_con" costs 1 position 11 17}
```

要点：
- 节点三要素 = **requires（前置集合）+ costs（RP 花费）+ position（网格 x y）**；
- 两类节点：`tech`（科技节点，如 `reinforcement_stage_N`/`defense_level_N`）与**具体单位节点**（解锁某小队/某支援）；
- 链式前置形成**纵向科技线**（如 `sicherung → regular → regular_vet → … → grenadier → pzgren → sturm_pzgren`），横向的 `position` 把线并排铺开成"树"；
- 成本 0-8 RP，起点节点成本 0-2。

### 1.7 单位 = 小队/载具，带"研究阶段"与 CP

`set/multiplayer/units/conquest/units_ger.set` 与 `settings.set` 的注释给出了完整字段语义（`settings.set:5-30`）：

```
side()       阵营
period()     early / mid / late（时期）
n()          乘员数
cw()         capture weight（占点权重！）
cp()         Command Points（出兵预算占用）
{research_stage N}      / {research_stage_max M}   动态征服里 AI 可用的研究阶段窗口
{level 6}              玩家档案等级门槛
{button}               inf1 / inf2 / empl / vehicles / tanks / doctrine（UI 分类）
{cost -}               MP 成本
{squad_cost_factor 1}  小队成本 = 成员成本之和 × 系数
{round_multiple 5.0}   成本取整到 5 的倍数
```

**单位成本是按武器逐件累加的**（`set/multiplayer/units/prices.txt`，注释原样）：

```
; Standard prices below are for tier2 infantry
k98 = 8      mp40/mp38 = 10   g41 = 13    g43 = 13
mkb42(h) = 20   stg44 = 20    fg42 = 22
mg34 drum = 40  mg34 belt = 45  mg42 drum = 40  mg42 belt = 45
rifle grenade = +20    any pzfaust = +5     any flamer = 60
engineer = 50   miner = 40    marksman = +2

; price modifiers (If not tier2)
tier 0 = -3, MG -10      tier 1 = -1, MG -10    tier 1 vet = -1, MG -5
tier 2 vet = +2, MG +5   tier 3 = +5, MG +10    tier 3 vet = +8, MG +15
tier 4 = +10, MG +20     tier 4 vet = +13, MG +25
```

→ **"精锐单位更贵"是 tier 修正项**，不是另设一张表；MG 的修正幅度是步枪的 2 倍（重武器溢价）。

### 1.8 防守方有"防御等级"（随场次/进度解锁）

`resources_standard.set:15-31, 19-31`：守方部队预算 = `BotStartMP × DefenseBudget × 旗点数`，等级 1/2/3 的系数 = **0.5 / 0.75 / 1.0**，解锁条件 = 打满 0/4/8 场**或**战役进度 0.0/0.4/0.6。

### 1.9 单场战斗的规则（战斗层）

`set/multiplayer/games/campaign_capture_the_flag.set`：

```
parameters "...flag_capture_time=75;flag_release_time=25;flag_capture_factor=0.9;
              points_table_player=0/0.000,0.333/0.416,0.5/0.555,0.667/0.667,1.00/0.833;
              points_table_ai=0/0.000,0.333/0.139,0.50/0.167,0.667/0.208,1.00/0.277;
              kill_score_multiplier=0"
{scoreFinal 1000}
{preparationTime 1200}   ; 玩家当防守方时，AI 开始进攻前的准备时间（秒）= 20 分钟
{impregnableTimeout 10}  ; 出生保护
{buttons "inf1 inf2 empl vehicles tanks"}
{budgets {vehicle {resource {start %mpStart} {finish %mpFinish} {payback {cp "0 10 20 30 40 51"}}}}}
```

要点：
- **占点时间 75 秒、释放时间 25 秒、占领系数 0.9**；
- 分数上限 1000，玩家/ AI 各有"分数增长速率表"（AI 慢得多：0.139~0.277 vs 玩家 0.416~0.833）；
- 开局 20 分钟准备期（防守方布防），可设 `pauseDuringPreparation`；
- **payback 表按 CP 档位返还**（`0 10 20 30 40 51`）——即"单位被击毁时按它占的 CP 返还预算"，这与战略层的 PaybackFactor 是两套（战斗内 vs 战役间）。

### 1.10 AI 行为（`script/multiplayer/modes/conquest.lua`）

| 常量 | 值 |
|---|---|
| AI 首购等待 | 防守 5-7 秒；进攻 1 秒 |
| 波次间隔 | 2.0–2.5 分钟 |
| 单次生成间隔 | 2–7 秒 |
| 一波兵力 | **7–10 个单位** |
| 单位等待上限 | 1.5 分钟 |
| 改命令周期 | 2.5 分钟 |

AI 选点逻辑（同文件 `GetFlagToCapture`）：进攻方**优先把它遇到的第一个敌方旗标 ×1**，其余旗标优先级 ×0；防守方给敌旗 ×2、己旗 ×0.5。

**推广价值**：这套"波次 + 优先级"直接可用于 ER2 征服战斗的 AI 增援节奏（若做 M4 生成式战斗）。

---

## 2. 人间地狱（Hell Let Loose）机制 —— 联网检索结果

> ⚠️ **来源限制声明**：本机网络下 `hellletloose.fandom.com` 与 `www.reddit.com` **均连接超时**，`steamcommunity.com` 被 modsearch 判定为私有网络目标而拒绝抓取，`hellletloose.com` 博客正文由 JS 渲染、抓不到。因此本节的数字**主要来自搜索引擎返回的摘要片段**，未逐页核对原文。凡未经二次确认的条目都标了「未核实」。

### 2.1 两种大战场模式

| | **Warfare** | **Offensive** |
|---|---|---|
| 结构 | 地图横向分若干**扇区（sector）**，每扇区中心一个**据点（strongpoint）** | 同扇区结构，但**只有据点算数** |
| 占领判定 | 扇区/据点内**人数多的一方**推进占领条；友军多于敌军即推进 | 攻守**双方都必须待在据点内**才推进（来源：[Offensive wiki 摘要](https://hellletloose.fandom.com/wiki/Offensive)） |
| 已失扇区 | 可反复争夺 | **守方不能夺回已失扇区**（单向推进，来源：[HLL:Vietnam 官方 game modes 博文摘要](https://www.hellletloose.com/blog/hllv-game-modes)） |
| 时间/票数 | 无逐点计时 | 攻方每据点 **30 分钟**；拿下后计时器重置为 30 分钟；攻方起始票数较低（摘要称 attackers 500 / defenders 1000，**未核实**） |

占领判定的原话（[r/HellLetLoose Game mode guide](https://www.reddit.com/r/HellLetLoose/comments/ek6hz8/game_mode_guide/) 摘要）：
> "Capturing a sector requires that a team has more players in it than the other: eg, five Germans will capture a sector if there are four or less …"

### 2.2 三种资源

| 资源 | 用途（摘要口径） |
|---|---|
| **Manpower** | 人员：重生/补充（reinforce）——"how fast people respawn"（[r/HLL 资源帖](https://www.reddit.com/r/HellLetLoose/comments/dldzc7/is_there_a_guide_on_manpower_munitions_and_fuel/)） |
| **Munitions** | 弹药：指挥官技能、炮兵 |
| **Fuel** | 燃料：坦克等载具生成 |

### 2.3 节点（Nodes）

- **每个节点产 10 资源/分钟**；每名工兵**每种节点只能造 1 个**，全队**每种上限 3 个**（[Manpower Node wiki 摘要](https://hellletloose.fandom.com/wiki/Manpower_Node)）。
- 节点只能建在**己方领土**，同类节点之间需间隔 50 米（[r/HLL Garrison mechanics](https://www.reddit.com/r/HellLetLoose/comments/mktxmj/garrison_mechanics/)）。
- 指挥官技能对节点的利用："Nodes will generate x2 resources for 5 …"（[Commander wiki 摘要](https://hellletloose.fandom.com/wiki/Commander)，完整句子被截断，**具体是哪种增益未核实**）。

### 2.4 集结点（Garrison）/前哨（Outpost）

- **Garrison 造价 50 补给（supplies）**，在**敌占区翻倍**；由小队长 + 补给兵协同建造（同上 Reddit 摘要）。
- 敌人在 **50 米**内时 Garrison 图标变红；（另一处摘要提到红区 **100 米**为完全锁定范围）。
- 有摘要称"**距敌 200 米**"是可建造距离限制——**该数字未核实**，与 50 m 锁定是两回事（限制"能否建" vs 触发"不可用"）。

### 2.5 指挥官技能

每个技能消耗 manpower / munitions / fuel 之一（[Commander wiki 摘要](https://hellletloose.fandom.com/wiki/Commander)），常见技能：Strafing Run、Bombing Run、Supply Drop、Airhead（空投集结点）、Reinforce、Air Superiority、Encourage、Final Stand。**具体数值本次未能核实**。

### 2.6 对 ER2 的三条可迁移结论

1. **「人多者推进占领条」是最可复制的核心手感**——ER2 原生已有 `conquer_areas`（`secureTime` 控制占领时长），可用原生目标区 + mod 侧人数比较复现；
2. **资源 → 指挥官技能的映射**天然适合 ER2：`Special`（SP）资源买战场支援（炮击/空袭），与 GoH 的 SP 语义重合；
3. **节点的"每类上限 3 个、每个 +10/分钟"** 是"领土产出"的战术层版本 —— ER2 可用"省份类型（GoH MapRewards）"承担战略层产出，不必再做战场建造。

---

## 3. 机制映射表（参考 → ER2 征服模式实现)

| 参考机制 | 来源 | ER2 实现 | 状态 |
|---|---|---|---|
| 四种资源 MP/AP/RP/SP | GoH `resources_standard.set` | `ConquestCampaign.Manpower/Ammo/Research/Special` + `Rules` 起始值与阶梯收益 | ✅ 本轮实现 |
| 胜/负收益按场次阶梯 | GoH `WinGain/LoseGain "n:v"` | `Rules.GainFromCurve(curve, battlesPlayed)` | ✅ 本轮实现 |
| 地图类型奖励（5 类） | GoH `MapRewards` | `Province.Kind`（`ProvinceKind`）+ 攻占时按表发放 | ✅ 本轮实现 |
| 风险档 Low/Standard/High | GoH `RiskFactor` | `ConquestCampaign.Risk` → AI 老兵 + 收益倍率 | ✅ 本轮实现 |
| 战损返还 40% / 解散返还 60% | GoH `Payback/SellFactor` | `Rules.PaybackFactor/SellFactor` + `Disband()` | ✅ 本轮实现 |
| 出兵预算 CP（含阶梯与上限） | GoH `Budget`/`StageCP`/`GlobalMaxCP` | `Rules.DeploymentBudget(battles, stage)` + `UnitTemplate.CpCost` + 出击编成校验 | ✅ 本轮实现 |
| 阶段（5 阶段，按场次解锁） | GoH `StageCP`/`StageUnlock` | `ConquestCampaign.Stage` + `Rules.StageUnlockGames` | ✅ 本轮实现 |
| AI 研究阶段曲线 | GoH `ResearchStages "n:v"` | `Rules.AiResearchStageCurve` → AI 可用单位上限 | ✅ 本轮实现 |
| 研究树（requires + costs + 网格坐标） | GoH `unit_research_*.set` | `Core/ResearchTree.cs`：`ResearchNode{Id,Cost,Requires,Tier,PosX,PosY}` + RP 购买 | ✅ 本轮实现 |
| 单位成本按武器累加 + tier 修正 | GoH `prices.txt` | `UnitTemplate.MpCost/CpCost/Tier`（由编制规模 + 类别启发式推算，原生无武器价表） | ✅ 本轮实现（近似） |
| 占点时间 75s / 释放 25s | GoH gamemode | 战斗层目标区参数（M4 生成战斗时写入 `secureTime`） | ⏳ M4 |
| 人多者推进占领条 | HLL Warfare | 战斗层 `BattleBridge` 人数比较 | ⏳ M3/M4 |
| 资源买战场支援 | HLL Commander / GoH SP | `Special` 资源 → 战斗内炮击/空袭（M3 之后） | ⏳ 后续 |
| 节点（+10/分，每类上限 3） | HLL Nodes | **不实现**：ER2 无战场建造系统，产出改由省份类型承担 | ❌ 明确不做 |
| 旗数按场次递增 | GoH `FlagCount` | M4 生成战斗时按 `Rules` 决定目标区数量 | ⏳ M4 |

**结论**：GoH 的战略层是**可直接照搬的数据模型**（它就是一组 `.set` 数值表），HLL 的价值在**战斗层手感**（人多者推进 + 单向推进 + 资源换支援）。本轮把 GoH 战略层完整落地，战斗层留接口给 M3/M4。
