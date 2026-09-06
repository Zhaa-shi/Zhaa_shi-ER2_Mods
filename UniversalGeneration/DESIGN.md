# Universal Generation 设计文档（v1 草案，2026-09-05）

> **定位**：独立作弊 mod `er2.universalgeneration`（GUID）——玩家自定义生成单位/载具。
> **生效条件**：只在战场指挥官（SquadCommand）的 RTS 上帝视角内工作；RTS 模式关闭时整个 mod 休眠（不 patch 任何游戏逻辑，只"调用"原生生成 API + 画自己的 IMGUI）。
> **不是补丁/附属整合包**：不依赖 SquadCommand 源码编译，运行时反射读宿主状态（Active/CurrentMark/RTS 组）；宿主缺失时 mod 加载但不激活（提示日志）。
> 逆向依据见本文末尾附录与 `SquadCommand\docs\SpawnAddon_design.md`（API 细节已实证）。

---

## 1. 功能范围（作弊向，只做"正常生成"）

| 类别 | 内容 | 阵营 |
|---|---|---|
| 步兵小队 | 按官方小队类型生成一班 AI（`ItemsDatabase.GetSquadLoadouts` / 运行时捕获的原生配置） | 我方 / 敌方 |
| 载具 | 全部 396+ prefab（Tanks/Wheeled/Planes/Artillery，运行时 `GetAllItemsOfType<PropData>` 枚举） | 我方 / 敌方 |
| 载具乘员 | 生成载具时可选"带 AI 乘员"（默认带） | 跟随载具阵营 |

不做：直接刷单兵散兵（小队已覆盖）、英雄单位、空投/运输机演出、平衡扣票（作弊 mod 不惩罚）、删除/传送单位（后续可选扩展）。

**阵营区分**（数据源已实证）：
- 双方阵营名静态可取：`Lua_API.getInvadersFaction()/getDefendersFaction()`（等价 C# 侧 `BattleManager`/`MatchData` 读法，二选一实测）。
- UI 上永远是两块：**我方** / **敌方**，生成前明确选择；敌方默认红色标记。

---

## 2. 玩家操作设计（核心）

### 2.1 总原则

1. **零冲突**：RTS 视角已占用 左键(选/框)、右键(指令/命令环)、中键(旋转)、WASD/QE/Shift(相机)、空格(暂停)、ESC(菜单)、1-9(组编号)。Universal Generation 用 **G 键**（已确认空闲）+ 自有面板鼠标操作，不抢任何现有手势。
2. **UI 打开时冻结 RTS 手势**：面板打开期间，宿主的 `IsMouseOverGui()` 必须覆盖本面板区域（见 §4 联动），否则点面板会同时框选单位。这是第一个要联调的点。
3. **两段式操作**：先在面板里"配好单子"，再"点战场落点"——面板是安全区，战场只有最后一次点击。
4. **所见即所得**：落点实时画预览圈（绿=我方/红=敌方），生成瞬间圈闪一下消失 + 左上角反馈行（复用宿主 cmdFlash 的视觉语言）。

### 2.2 打开/关闭

- **G 键**：RTS 视角内按 G 开关生成面板（默认键，cfg 可改）。
- 关闭途径：再按 G、面板右上 ×、ESC（宿主 ESC 菜单打开时面板自动隐藏，跟随宿主 `escMenuOpen` 行为）。
- 面板**不暂停世界**（作弊 mod，保持战场节奏；玩家自己有空格）。

### 2.3 面板布局（屏幕左侧，竖向抽屉）

```
┌──────────────────────────────┐
│ UNIVERSAL GENERATION        × │   ← 标题栏（可拖动，可选）
├──────────────────────────────┤
│ [我方 🇺🇸] [敌方 🇩🇪]          │   ← 阵营切换（两个大按钮，当前方高亮）
├──────────────────────────────┤
│ [步兵] [坦克] [轮式] [飞机] [火炮] │  ← 类别页签（坦克/轮式/飞机/火炮 =
│                                  │    PropData.Category 映射）
├──────────────────────────────┤
│ 🔍 [搜索____________]           │   ← 实时过滤
│ ┌──────────────────────────┐ │
│ │ Panther              ▸   │ │   ← 条目列表（滚动区，长名截断+
│ │ Char B1               ▸  │ │    省略号；陷阱 19 经验）
│ │ Churchill AVRE        ▸  │ │   ← 步兵页显示小队类型+人数
│ │ ...                       │ │
│ └──────────────────────────┘ │
├──────────────────────────────┤
│ 数量 [−] 1 [+]    乘员 [✓]     │   ← 数量 1-4；带乘员开关(载具页)
│ 落点 [标记点 ▾]                │   ← 落点模式（见 2.4）
│ [ 生成 ]                       │   ← 大按钮，确认后进入落点模式
└──────────────────────────────┘
```

- **默认值智能填充**：打开面板时阵营=我方、类别=坦克、数量=1、乘员=开——作弊 mod 要"三击出车"（G → 点条目 → 点战场）。
- **最近生成置顶**：每次生成后该条目移到列表顶部（会话内记忆，不落盘）。

### 2.4 落点模式（生成确认后的战场交互）

点 [生成] 后面板收起为左上角小徽标（"点击战场放置：Panther ×1 [取消:G]"），进入落点模式：

| 操作 | 行为 |
|---|---|
| **移动鼠标** | 地面实时预览：地面画 6m 半径圈（我方绿/敌方红），圈随地形贴合；空中单位显示 3D 十字浮标（高度=cfg 默认 150m） |
| **左键单击** | **确认生成** → 圈闪光 → 面板恢复完整状态（可继续下一单） |
| **右键 / G / ESC** | 取消落点模式，面板恢复 |
| **Shift+左键** | 连续放置：生成后不退出落点模式（配 2 辆以上时流式放车），右键才退出 |
| **滚轮** | （仅飞机）调整生成高度 50-400m |

- 落点合法性：射线打地面（同宿主 2675 行 ScreenPointToRay+Physics.Raycast 模式）；打不到地面（天上）时圈变灰且左键无效。
- 生成间距：同一单多数量时以落点为中心按 formation 自动散布（步兵半径 10m，载具 15m 网格）。

### 2.5 反馈与状态

- **左上反馈行**（跟随宿主 cmdFlash 位置风格）："已生成 Panther ×1（我方）" / "生成失败：此处无法放置"。
- **冷却/上限不设**（作弊 mod），但留 cfg 开关 `respectLimits`（默认关）供想守规矩的人开——开了就按 `getSettingMaxAllies/getSettingMaxAxis` 限额+30s 冷却。
- 生成成功后日志（低频，每次生成一条）：`[UniGen] Spawned vehicle Panther @ (x,z) faction=... crew=4`。

### 2.6 快捷流（进阶，v2 可选）

- **Ctrl+G**：重复上一次生成（同条目同数量），直接进落点模式——"再给我三辆谢尔曼"场景一键化。
- **Alt+左键点条目**：跳过确认页直接进落点模式（等效点[生成]）。

---

## 3. 生成实现（全部原生 API 调用，已实证签名）

```
载具：go = new GameObject(); sp = go.AddComponent<VehicleSpawner>();
      sp.vehiclePrefabID = <条目id>; sp.camoId = 0;
      veh = sp.SpawnVehicle();                     // 内部异步加载，当帧 spawnedVehicle 可能为 null
      → 延迟帧/协程确认 veh 非 null → veh.SetFaction(faction)
      → 带乘员：循环 SpawnAI(loadout, rank, veh, pos, faction, null, cb)
           → cb: veh.GetOnVehicleBestPos(soldier)   // 原生进出车管线
      loadout 来源：营配置（BattleData.invadersBatalion 等 string）对应的
      GetSquadLoadouts(...) 或运行时捕获的 SquadData.loadouts[0]

步兵：sd = GetSquadLoadouts(<小队类型>, 0)
      SpawnManager.SpawnAISquadGlobal(faction, null, sd, pos, 10f, null,
          squad => {...}, -1)
```

**必须实测的三件事**（MVP 第一轮加诊断日志）：
1. `script_file` 传 null/"" 的行为（AI 是否正常初始化）。
2. `vehiclePrefabID` 形态：manifest 显示 `Vehicles/Tanks/Panther.prefab`，`.mer2` 里是 `"Ger Pak 36"` 纯名——先按"纯文件名去后缀"实现，日志验证。
3. 飞机空中生成的初始化（`VehiclePlane.throttle`/初速度），MVP 先禁用飞机类别或允许生成但标注"实验性"。

## 4. 与宿主（SquadCommand）的联动点（反射，不改宿主源码）

| 需求 | 反射目标 | 备注 |
|---|---|---|
| 判断 RTS 是否激活 | `GodViewController.Active`（internal static bool） | false 时 mod 休眠 |
| 面板打开时吞掉战场手势 | **宿主 `IsMouseOverGui()` 读不到本 mod 的 Rect** → 需要暴露 | 两个方案：a) 宿主加一个 `internal static Func<Rect?> ExtraGuiBlock` 钩子（推荐，一行改动）；b) 附属 mod patch 宿主的 IsMouseOverGui Postfix（丑但零宿主改动） |
| 落点默认值=当前标记点 | `GodViewController.CurrentMark`（internal static） | 有标记时预填落点；无标记走射线 |
| 生成后进 RTS 可选集合 | 宿主对"原生小队"本就自动收录（ GetAllFriendlySquads 遍历 Squad.AllSquads）→ **大概率零工作**；实测确认，不行再调 `RegisterControlledSquad` | — |

**决策**：推荐方案 a（宿主加 1 个静态委托钩子 + G 键让位声明），改动 ≤10 行，远优于 patch 宿主。Host 侧改动清单：
- `GodViewController` 加 `internal static Func<Rect?> externalGuiBlock;`，`IsMouseOverGui()` 尾部 `if (externalGuiBlock?.Invoke() is Rect r && r.Contains(m)) return true;`
- cfg/文档声明 G 键为附属 mod 保留（宿主不占）。

## 5. mod 结构（对照 SquadCommand 工程习惯）

```
UniversalGeneration/
├── Plugin.cs            # BepInEx 入口：cfg（G键/默认高度/respectLimits）、RTS 激活轮询（低频）、语言
├── GenCatalog.cs        # 条目枚举与缓存：载具=GetAllItemsOfType<PropData>(vehicles) 按 Category 分桶；
│                        #   步兵=小队类型列表（运行时捕获+GetSquadLoadouts 尝试表）；搜索索引
├── GenPanel.cs          # IMGUI 面板（阵营/类别页签/列表/数量/生成按钮），HudStyle 同款配色
├── Placer.cs            # 落点模式：预览圈渲染(Gizmo 材质线圈)、点击确认、连续放置、取消
├── Spawner.cs           # 生成执行：VehicleSpawner 封装 / SpawnAISquadGlobal 封装 / 乘员填充协程
├── HostLink.cs          # 反射层：GodViewController.Active/CurrentMark、externalGuiBlock 注入、缓存句柄
└── UniversalGeneration.csproj
```

- **日志门控**照宿主规矩：高频路径默认关，仅生成动作 + 错误必打。
- **版本**：`1.0.0` 起步；cfg 改键走 ModManager 同款"点击捕获"方案（陷阱 16，不做 Dropdown）。
- **双语**：Ui.Tr 模式 + build.ps1 `-Cn`。

## 6. 分期

| 版本 | 内容 | 验收 |
|---|---|---|
| **v0.1** | 面板+载具（坦克/轮式/火炮）我方生成、落点预览、单次放置；宿主 externalGuiBlock 钩子 | G→条目→点战场→车落地能开；RTS 内点击面板不误框选 |
| **v0.2** | 敌方阵营、AI 乘员、步兵小队页、连续放置(Shift)、最近生成置顶 | 敌方车会攻击我方；生成小队 RTS 能选中指挥 |
| **v0.3** | 飞机（空中生成实验性）、搜索过滤、Ctrl+G 重复、respectLimits、涂装 | 一场大仗连刷 20 单位不卡 |

## 7. 风险

1. **guiNow 互斥是最大联调点**：面板 Rect 必须精确注册给宿主，否则每次点面板都在战场拉框选。v0.1 第一项就做这个。
2. **panel 期间宿主 hotkey**（1-9/空格）仍会触发——面板打开时属正常（玩家可能想边看边暂停），保持不动。
3. IL2CPP `AddComponent<VehicleSpawner>` 需实测（interop MonoBehaviour 子类一般可行，失败则改为:定位场景里任一 VehicleSpawner 克隆改字段——保底方案）。
4. 步兵小队类型列表的运行时捕获可能拿不全（官方表在 native）——保底：硬编码常见 label + GetSquadLoadouts 逐个试，失败条目隐藏。
5. 生成单位永久存在，存档/阶段推进兼容性未知——作弊 mod 接受；文档注明"生成单位不保证跨阶段存活"。

---

## 附：API 实证索引

- `SpawnManager.SpawnAISquadGlobal(faction_id, script_file, SquadData, pos, radius, Vehicle, Action<Squad>, int player_pos=-1)`（SpawnManager.decompiled.cs:4749）
- `SpawnManager.SpawnAI(Loadout, Rank, Vehicle, Vector3, string faction, string script_file, Action<Soldier>)`（:4560 静态）
- `ItemsDatabase.GetSquadLoadouts(string type, int overwriteSquadSize=0)`（ItemsDatabase.decompiled.cs:2106）/ `GetLoadout`(:2144) / `GetAllItemsOfType<PropData>(PropType)`
- `VehicleSpawner.vehiclePrefabID / camoId / SpawnVehicle()`（VehicleSpawner.decompiled.cs）
- `Vehicle.GetOnVehicleBestPos(Soldier)`（Vehicle.decompiled.cs:2771）/ `SetFaction` / `allVehicles`
- `BattleData.invadersBatalion/defendersBatalion`（string，营名）、`Lua_API.getInvadersFaction/getDefendersFaction`
- 载具 prefab 清单：`er2vehicles.manifest`（Tanks 173 / Wheeled 58 / Planes 59 / Artillery 30+）
- 宿主交互占用：GodViewController.cs HandleClickCore(2496-) 相机(2409-2446) 命令环(3738-) 小队列表右下(3302) 顶栏三按钮(2654-2661)
