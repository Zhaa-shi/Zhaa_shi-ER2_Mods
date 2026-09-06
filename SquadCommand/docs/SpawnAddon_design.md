# 战场指挥官附属 mod：自定义生成单位/载具（Reinforcements）设计方案

> 逆向依据：`research_out\`（SpawnManager / SquadData / SquadDataTable / VehicleSpawner / ItemsDatabase / Lua_API 反编译桩）、`report_battle.md`、`report_vehicles_misc.md`、`report_lua.md`，及游戏 `StreamingAssets\CorvoBundles\*.manifest` 实查（2026-09-05 游戏版本）。
> 结论先行：**游戏原生生成 API 全部公开且带静态"任意位置"变体，附属 mod 无需 patch 引擎，纯调用即可实现玩家自定义刷兵/刷车**。

---

## 1. 原生生成管线逆向结果

### 1.1 步兵 / 小队（SpawnManager）

`SpawnManager : MonoBehaviour` 挂在每个出生点上，但**静态全局方法可在任意位置生成**（report_battle.md §4 实证签名）：

```csharp
// 小队（核心入口，静态）
public static IEnumerator SpawnAISquadGlobal(
    string faction_id,            // 阵营名（如 "usa"/"germany"，string 型）
    string script_file,           // AI 脚本；一般任务为空 → mod 传 null/"" 需实测
    SquadData sd,                 // 小队配置（loadout 数组包装）
    Vector3 pos,                  // 生成中心
    float radius,                 // 散布半径
    Vehicle spawnOnvehicle,       // 直接生成到载具座位（可 null）
    Action<Squad> generatedSquad, // 回调：拿到生成好的 Squad
    int player_pos = -1)          // 玩家占第几号位（-1 = 无玩家）

// 单兵（静态）
public static IEnumerator SpawnAI(
    Loadout loadout, Rank rank, Vehicle spawnVehicle,
    Vector3 position, string faction, string script_file,
    Action<Soldier> soldier)
```

- 全部是**异步协程**，结果经 `Action<Squad/Soldier>` 回调返回；需在 Unity 主线程 `StartCoroutine` 驱动。
- 实例变体（`SpawnAISquad(calculateTask, squadIsForPlayer, startPhaseSpawn, forceSpawn)`）绑定出生点与阶段逻辑，mod 用静态变体即可，不要碰实例变体。
- 生成的小队自带原生 AI 与任务计算（`calculateTask`），生成后自动接战——符合陷阱 6（AI 移动不可外部驱动，靠原生管线）。

**配置数据从哪来：**

```csharp
// SquadData：3 个字段 loadouts / squadCode_id / squadName
public SquadData(string squadName_id, string[] loadouts)   // mod 自配小队：直接填 loadout id 数组
public string[] GetLoadoutIDs()                             // 读出已有配置

// ItemsDatabase（静态数据库）
public static Loadout GetLoadout(string loadout_id)         // 按 id 取 loadout
public static Loadout GetRandomLoadout()
public static SquadData GetSquadLoadouts(string type, int overwriteSquadSize = 0)
                                                            // 按官方小队类型 label 取原生配置！
                                                            // 另有 SquadType 枚举重载
```

- `SquadDataTable`（struct）字段 `squadTypeLabel / standardSquadSize / allowOverwriteSquadSize / loadouts / dlc` 表明游戏有全局小队类型表；`GetSquadLoadouts(type, size)` 是"刷一班标准德军步兵"的最短路径。
- 官方小队类型 label 的合法取值反编译里看不到（native 构造），**需运行时 dump**：遍历 `Squad.AllSquads` 读 `squadCode`，或日志打 `GetSquadLoadouts` 尝试值。首版可先自配 loadout 数组绕开。

### 1.2 载具（VehicleSpawner + ItemsDatabase）

```csharp
public class VehicleSpawner : MonoBehaviour {
    public string vehiclePrefabID;   // prefab 名，如 "Panther"（见 1.3）
    public int camoId;               // 涂装 id，0 为默认
    public Vehicle spawnedVehicle;
    public Vehicle SpawnVehicle();   // 同步生成（内部异步加载 prefab）
    public string GetVehicleId();
    public bool IsAvailable();  // 实例版用的占用检查，新建的随便生成
}

// prefab 加载底层（ItemsDatabase，静态）
public static UnityEngine.Object Load(string name, string bundle = "er2bundle", string folder = null);
public static GameObject GetPropPrefabCached(string prop_id);
public static IEnumerator LoadAndCachePropAsync(string prop_id, Action<Object> cb);
```

- **mod 复用方式（官方"部署载具"同款管线）**：新建空 GameObject → `AddComponent<VehicleSpawner>()` → 设 `vehiclePrefabID` / `camoId` → `SpawnVehicle()` → 生成物 `SetFaction(faction)`。
- 生成后乘员/操作全套原生 API 可用：`GetOnVehicle(int pos, Soldier)` / `GetOnVehicleBestPos(Soldier)`（Vehicle.decompiled.cs:2758/2771 实证）、`EjectAll`、`Vehicle.allVehicles` 静态注册表。
- **AI 乘员填充** = 先 `SpawnAI` 单兵到载具旁 → 回调里 `vehicle.GetOnVehicleBestPos(soldier)`。跟随原生进出车流程，不要直接写 `Seats.unitSet`（同步会打架）。

### 1.3 载具 prefab ID 清单（已实查）

`er2vehicles` bundle manifest 直接列出全部 prefab（`StreamingAssets\CorvoBundles\er2vehicles.manifest`）：

| 子目录 | 数量 | 示例 |
|---|---|---|
| `Vehicles/Tanks/` | 173 | Panther、Char B1、CV33、Churchill AVRE、Bren Carrier×5 变体 |
| `Vehicles/Wheeled/` | 58 | DUKW、ZisTruck、AS42 Sahariana、M3 Half-Track、BM-13 |
| `Vehicles/Planes/` | 59 | StukaG2、IL-2、BF-109G6、P38、F4F Wildcat |
| `Vehicles/Artillery/` | 30+ | Ger Flak 88、Pak 40、Mortar USA、Nebelwerfer |

- ID 即 prefab 文件名（去后缀）；与 `.mer2` 任务里 `MissionEditorProp` 的 prefab_name（"LCVP"、"Ger Pak 36"）互相印证。
- **优先运行时枚举**而非硬编码：`ItemsDatabase.GetAllItemsOfType<PropData>(PropType.vehicles)` 返回全部载具 `PropData`，其 `Category` 枚举正好是 `{Wheeled, Tank, Plane, AutoTransport, StaticGun, ...}`——可直接做分类 UI，且自动含 DLC 与创意工坊内容 mod。manifest 文本清单仅作离线参考/兜底。
- 阵营是 string：`Lua_API.getInvadersFaction()/getDefendersFaction()`（静态）或读 `BattleManager` / 现有 `GodViewController.MySideFaction()`。

### 1.4 生成后的归属与指挥

- `Squad.AllSquads`（`Dictionary<string, Squad>` 静态）+ `Squad.FindOrNew`：可枚举/检索生成结果。
- `SpawnAISquadGlobal` 生成的是**原生阵营小队**，天然敌我分明、自动接战、可被原生 radio/order 管线指挥。
- 附属 mod 与 SquadCommand 联动：回调拿到 Squad/Soldier 后 → 反射调宿主 `SquadCmdLogic.RegisterControlledSquad/RegisterControlledUnit`，新兵立即进入上帝视角的可选集合。
- 平衡挂钩（可选）：`BattleManager.AddBattleStat(BattleSide, BattleStatistic, tickets)` 可扣票；`Lua_API` 侧有 `getSettingUnitCountMultiplier/getSettingMaxAxis/getSettingMaxAllies` 等价静态（C# 在 MatchData/设置系统）。

### 1.5 为什么不走 Lua spawn API

`Lua_API` 的 `spawnSoldierWithCallback` 等是 MoonSharp 包装（参数走 `CallbackArguments`，需构造 DynValue），且实测官方任务脚本几乎不直接调用（SHARED/gamemode_defend.lua 只用 `spawnMissionObjective`）——生成主要走 .mer2 数据。**C# 静态方法更直接，签名完整，无需绕 Lua。**

---

## 2. 附属 mod 方案：`er2.squadcommand.reinforcements`（SquadCommand 增援调度台）

### 2.1 定位与形态

- 独立 BepInEx 插件（GUID `er2.squadcommand.reinforcements`），**反射联动宿主**（陷阱 9 模式，`Shared/NoHintsHudLink.cs` 先例）：装了 SquadCommand → 集成进上帝视角；没装 → 独立快捷键 + IMGUI 简易面板仍可用。
- 沿用 SquadCommand 的 IMGUI + HudStyle 体系（SquadCmdLogic.cs:386-432 已有现成样式）。

### 2.2 功能设计

**入口**：上帝视角（GodView Active）内按面板键呼出"增援调度台"；无宿主时全局热键。

**面板三级选择**（IMGUI 网格，参照 ModManager 的分页/截断经验）：

1. **类别页**：步兵小队 / 坦克 / 轮式 / 飞机 / 火炮（来源 `PropData.Category`）
2. **条目页**：该类别全部 prefab（运行时 `GetAllItemsOfType<PropData>(PropType.vehicles)` 按分类过滤；步兵页 = 小队类型列表）。搜索框过滤 + 长名截断省略号（陷阱 19）。
3. **确认页**：阵营（我方/敌方切换）、数量（小队 1-4 / 载具 1-2）、落点（=上帝视角当前标记点，无宿主则准星/相机前方地面）、[生成] 按钮。

**生成流程**（全部主线程协程）：

```
载具：new GameObject("ReinSpawner") → AddComponent<VehicleSpawner>()
      → vehiclePrefabID=<选中>, camoId=0 → SpawnVehicle()
      → veh.SetFaction(faction)
      →（可选）填充乘员：SpawnAI(loadout, rank, veh, pos, faction, null, cb
           → cb 里 veh.GetOnVehicleBestPos(soldier))
      → 反射调 SquadCmdLogic.RegisterControlledSquad/Unit（若乘员有 squad）

步兵：SquadData sd = GetSquadLoadouts(type, size)  // 或 new SquadData(name, loadoutIds)
      → SpawnManager.SpawnAISquadGlobal(faction, null, sd, markPos, 10f, null,
           squad => { RegisterControlledSquad(squad); }, -1)
```

**落点拾取**：上帝视角标记点直接用 `GodViewController.CurrentMark`（host 已有 500m MarkRadius 与视线缓存）；无宿主时从相机射线打地面（`er2.raycast` 的 C# 等价：主相机 ScreenPointToRay + Physics.Raycast，再向下跌到地形高度），并做 `IsInsideMainTerrainBounds` 式边界检查。

### 2.3 平衡约束（可配置，BepInEx cfg）

- `扣票`：每生成一次 `BattleManager.AddBattleStat(己方侧, BattleStatistic.Deceased, n)` 等效扣士气票（默认关）。
- `冷却`：全局冷却秒数（默认 30s）。
- `上限`：己方 AI 总数超过 `getSettingMaxAllies` 等价值时拒绝（默认开，防卡顿）。
- `允许敌方`：默认关（刷敌测试用）。

### 2.4 分期实施

| 版本 | 内容 | 验证 |
|---|---|---|
| **v0.1 MVP** | 上帝视角呼出 → 载具分类列表 → 标记点生成**空载具**（我方） | 日志 "Spawned vehicle <id> @ <pos>"；能上车开走 |
| **v0.2** | AI 乘员填充 + 步兵小队生成 + 敌方阵营选项 + 联动 RegisterControlled | 生成小队自动接战；上帝视角能选中指挥 |
| **v0.3** | 平衡（扣票/冷却/上限）、涂装 camoId、飞机（空中生成+直线航向）、搜索过滤 | 长时间战斗无性能回退 |

### 2.5 风险与实测点（对接 AGENTS.md 陷阱体系）

1. **`script_file` 传 null/"" 的行为未实证**——官方 AI 脚本目录在任务包内；先加诊断日志各传法测一轮，用日志定位而非猜测（工作流契约 4）。
2. **vehiclePrefabID 的精确形态**（纯名 vs 带目录）需首次实测：`VehicleSpawner.GetVehiclePrefab()` 加载失败的日志是第一排查点。manifest 名与 `.mer2` prefab_name 互证大概率是纯名。
3. **IL2CPP 陷阱**：`AddComponent<VehicleSpawner>()` 在 IL2CPP 侧对 interop 类型可行（MonoBehaviour 子类）；`PropData.Category` 读值注意 TryCast（陷阱 4）。
4. **异步回调时序**：SpawnVehicle 内部加载 prefab，生成物可能当帧 `spawnedVehicle == null` → 一律走回调/延迟帧确认，不要同步读。
5. **生成即接战**：`SpawnAISquadGlobal` 出来就是活的小队，半径内贴脸生成会被秒——默认落点=标记点外扩（如半径 40m 随机散布），UI 上提示"远离敌人"。
6. **联机语义**：spawn 系列走 master 上下文（report_lua §2.7），客户端行为未定义；首版注明单人模式，联机仅 host 生效。
7. **数量上限与性能**：禁止每帧扫描（命令白名单禁令）；枚举一次缓存列表；上限检查用 `Creature.allCreatures`/`Vehicle.allVehicles` 计数。
8. **飞机特殊**：落地 prefab 直接生成会摔/卡地——v0.3 单独处理（空中 + 初速度 + `VehiclePlane` 字段），MVP 先不做飞机。
9. **中文版**：双语随 build.ps1 `-Cn`（面板文本走 `Ui.Tr` 同款结构）。

### 2.6 宿主（SquadCommand）侧需要的配合

无需改宿主即可工作（全反射）。可选的宿主侧增强（后续版本）：
- 上帝视角 HUD 显示"增援可用/冷却中"一行；
- `GodViewController` 暴露 `CurrentMark` 只读包装给附属 mod 的公共入口（现为 internal static，反射取值无碍）。

---

## 附：本次逆向关键文件索引

- `research_out\SpawnManager.decompiled.cs` — SpawnAISquadGlobal(4749) / SpawnAI(4560,4528,4544) / SpawnUnit(4603) / SpawnHero(4781)
- `research_out\SquadData.decompiled.cs` — ctor(95) / GetLoadoutIDs；`research_out\SquadDataTable.decompiled.cs` — 小队类型表结构
- `research_out\ItemsDatabase.decompiled.cs` — GetSquadLoadouts(2093,2106) / GetLoadout(2144) / GetAllItemsOfType / LoadAndCachePropAsync
- `research_out\VehicleSpawner.decompiled.cs` — vehiclePrefabID/camoId/SpawnVehicle
- `research_out\Vehicle.decompiled.cs` — GetOnVehicle(2758) / GetOnVehicleBestPos(2771) / SetFaction / allVehicles
- `research_out\Squad.decompiled.cs` — AllSquads / FindOrNew / SetFullySpawned
- `research_out\report_battle.md` §4 / `report_vehicles_misc.md` §8 / `report_lua.md` §2.7
- 游戏实查：`StreamingAssets\CorvoBundles\er2vehicles.manifest`（396 prefab：Tanks 173 / Wheeled 58 / Planes 59 / Artillery 30+）
