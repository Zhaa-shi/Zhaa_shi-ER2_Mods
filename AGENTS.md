# ER2_Mods 工作区 — AI 记忆与工作流（第一轮必读）

> **当前状态快照：2026-09-06（zcode 时代末次工作）**。本文件是**唯一每轮必读**的入口。
> 项目：Easy Red 2（Unity 2022.3.62f3 / IL2CPP）的 BepInEx 6 插件集合。
> 详细机制/API/全部踩坑 → `ER2_mod_dev_guide.md`（编码规范/机制大全）、`ER2_mod_经验.md`（陷阱全集 + 会话复盘）。
> 各 mod 现状与版本台账 → `ER2_projects_status.md`；zcode 时代成果 → `ER2_zcode_era.md`。

## 0. 环境事实（先确认，别猜）

| 项 | 值 |
|---|---|
| 工作区根 | `D:\Users\71011\Documents\ER2_Mods`（**2026-09 从 C 盘迁到 D 盘**，旧文档里的 `C:\Users\71011\Documents\ER2_Mods` 已失效） |
| 游戏根 | `E:\SteamLibrary\steamapps\common\Easy Red 2`（Steam appid 1324780，buildid 25255578） |
| 插件/配置/日志 | `<Game>\BepInEx\plugins\` / `config\` / `LogOutput.log` |
| interop | `<Game>\BepInEx\interop\`（`Assembly-CSharp.dll` 约 8.7 MB，游戏启动时重新生成） |
| BepInEx | 6.0.0.0（IL2CPP 版），插件 `<TargetFramework>net6.0</TargetFramework>` |
| 目标框架 | 引用 interop/core 程序集，全部 `Private=false` |
| 分支 | `master`（**无远端**，纯本地版本库，66 个提交） |
| 反编译 | `ilspycmd -t <Type> <dll> -o <dir>`（`-l c <dll>` 列类型）；**可从已部署 plugins DLL 恢复源码** |
| rar 解压 | `"C:\Users\71011\新建文件夹\WinRAR.exe" x -o+ -y <rar> "<out>\"`（WinRAR 在非标准路径） |

**游戏 2.1.x 重大变更（2026-09-05 起）**：游戏移除了 `PhaseBarGUI` 类型 —— 任何 `typeof(PhaseBarGUI)` / 直接 patch 会 `TypeLoadException` 导致**整个插件加载失败**。现行做法：`NoInteractionHints` 用 `FindGameType("PhaseBarGUI")` 运行时探测 + 条件 patch；字体获取改「活体 uGUI Text → GUI.skin」回退链（`LimbTweaks/NativeUi.cs`、`WeatherControl/NativeUi.cs`、`ModManager/Plugin.cs` 四处）。

**游戏静默更新（2026-09-12，`GameAssembly.dll` 17:21 重写、buildid 未变）**：BepInEx 下次启动重新生成 interop；原生签名变了（`SettingsGUI_V2.UpdateOpenedMenu` 由 1 参变 2 参、`FillSettingPage` 由 1 参变 4 参，新增 `GetSettingRow`/`lastScrollbarValue`）→ 旧编译产物运行时 `MissingMethodException`（**详见 §4 陷阱 29**）。**判定"游戏是否更新过"看 `GameAssembly.dll` / `global-metadata.dat` 的修改时间，别只看 buildid。**

## 1. 启动时必读（上下文加载顺序）

1. **本文件**（`AGENTS.md`）—— 现况、陷阱、工作流契约
2. `ER2_projects_status.md` —— 每个 mod 是什么/什么版本/什么状态（**别凭记忆猜版本号**）
3. `ER2_mod_dev_guide.md` —— 完整机制/API/踩坑参考（按需查；§2 机制、§3 陷阱 1-49、§3.6 跨 mod UI 契约）
4. `ER2_mod_经验.md` —— 致命陷阱全集 + 逐会话复盘（**动手前尤其要读对应 mod 的复盘**）
5. 目标 mod 的 `Plugin.cs`（**改动前通读**）

**两套记忆的分工（2026-09-12 起）**：
- **仓库文档（本文件 + 台账 + zcode 成果）= 事实源**，随 git 走、任何 AI 工具都能读、跨工具迁移不丢。改代码后同步更新它。
- **DSH 记忆插件（`dsh-memory-evolve`）= 会话记忆层**：项目关键记忆自动注入（`~/.dsh/memories/projects/0f0497177829/KEY.md`），项目日志与待办按需读写（同目录 `MEMORY.md` / `TODOS.md`）。它只在 DSH 里可见。
- 两者冲突时**以仓库文档和源码为准**；把长期有效的结论回写进仓库文档，别只留在插件记忆里。

## 2. 目录结构

```
ER2_Mods/
├── LimbTweaks/           er2.limbtweaks               肢体断肢+流血系统
├── WeatherControl/       er2.weathercontrol           天气/氛围控制
├── AIFood/               er2.aifood                   AI 自动吃食物回血
├── NoInteractionHints/   com.ryan.er2.nointeractionhints  Hide Anything：勾选制 UI 隐藏
├── ModManager/           er2.modmanager               游戏内 mod 设置页（嵌原生设置界面）
├── ThrowableWheel/       er2.throwablewheel           自定义投掷物转盘 + 背包补货
├── CombatTweaks/         er2.combattweaks             友军伤害保护 / 弹药与战场调整
├── ZoomAnywhere/         er2.zoomanywhere             任意姿态/移动中屏息与武器放大
├── HighValueTarget/      er2.highvaluetarget          Veteran HVT：老兵高危目标 + 叛徒机制
├── InventoryPause/       er2.inventorypause           背包暂停（开背包冻结世界）
├── SquadCommand/         er2.squadcommand             Battlefield Commander：上帝视角 RTS 小队指挥（最大工程）
├── UniversalGeneration/  er2.universalgeneration      RTS 内自定义生成单位/载具（作弊向，SquadCommand 附属）
├── UnitCollision/        er2.morephysics.unitcollision 单位/尸体碰撞（MorePhysics 轻量保留版）
├── MorePhysics/          er2.morephysics                 完整物理化（v0.1.48 复活：源码自反编译重建 + 单位碰撞对齐轻量版）
├── UnitInfoOverlay/      er2.unitinfooverlay          单位状态悬浮显示（开发者调试工具）
├── HvtTestDriver/        er2.hvt.testdriver           HVT 自测工具（内部，不发布）
├── Conquest/             er2.conquest                 ER2 Conquest：地狱之门征服模式移植（开发中；Core 为纯 C#，可离线验证）
├── ConquestRecon/        er2.conquest.recon           M0 侦察工具（内部，不发布）
├── FleshWoundsFixed/     ER2_FleshWounds              第三方 Flesh Wounds 重建修复（紫贴图 bug）
├── Shared/NoHintsHudLink.cs                           跨 mod F5 隐藏联动（反射，无编译期依赖）
├── scripts/build.ps1                                   一体化构建：编译+部署+清cfg+打包
├── research_out/                                       反编译/解包研究（17.9 MB，259 文件）
└── *.md                                                知识文档（见 §1）
```

**文档地图**：`ER2_mod_dev_guide.md`（工作流/机制/陷阱/各 mod 状态，102 KB）· `ER2_mod_经验.md`（陷阱全集 + 复盘，73 KB）· `ER2_mod_技能.md`（可复用技能）· `ER2_mod_工具.md`（工具速查）· `ER2_physics_system.md`（16 层碰撞矩阵解包）· `ER2_scene_objects_classification.md`（场景物分类体系）· `ER2_UI_design.md`（UI/IMGUI 机制）· `ER2_征服模式_设计方案.md`（征服模式方案 + 逐轮实测复盘）· **`ER2_征服模式_参考拆解.md`**（《地狱之门》征服模式 `.pak` 解包实证 + 《人间地狱》检索 + 机制映射表）。

**第三方游戏解包（2026-09-13 新增能力）**：《Call to Arms - Gates of Hell》装在
`E:\SteamLibrary\steamapps\common\Call to Arms - Gates of Hell`，其 `resource\*.pak`
**就是标准 ZIP**（文件头 `PK\x03\x04`）→ 用 .NET `ZipFile` 直接读，**别用 WinRAR**（会卡死）。
战略层数值全在 `resource\gamelogic.pak` 的 `set/dynamic_campaign/`；解包产物见
`research_out/goh_unpack/`（163 文件），拆解结论见 `ER2_征服模式_参考拆解.md`。

## 3. 命令白名单

| 命令 | 用途 |
|---|---|
| `powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字>` | **构建+部署+清cfg+打包一次完成**；`-SkipDeploy` 只打包；`-SkipPackage` 只部署；**`-Cn` 编译中文版并出 `_CN_` 包** |
| `dotnet build -c Release <proj>` | 仅编译（workdir 为 mod 目录；查错 `2>&1 \| Select-String "error"`） |
| `Select-String <log> -Pattern "..."` | 查日志 / 过滤本 mod 日志（**每次实测后必查，不要猜**） |
| `ilspycmd -t <Type> <dll> -o <dir>` | 反编译查 API；也可反编译已部署 plugins DLL 恢复源码 |
| `Get-Item <game>\BepInEx\plugins\<name>.dll` | 确认部署（比对时间戳/sha256） |
| `"C:\Users\71011\新建文件夹\WinRAR.exe" x -o+ -y <rar> "<out>\"` | 解压 rar 发布包 |

**禁止**：修改游戏原文件 · 删除 plugins 里其他 mod · `FindObjectsOfType` 类每帧全场景扫描（用 `Creature.allCreatures` / `Creature.aliveCreatures` 静态列表，或按需 `Physics.OverlapSphere`）。

## 4. 致命陷阱（全部实测定案；完整 49 条见 guide §3）

**写游戏状态类**
1. **写血量必须整体赋值**：`soldier.life_total = new ProtectedInt(hp);`（getter 返回值类型副本，`.Value = x` 报 CS1612）
2. **血量只能渐进小步写**：单帧大幅跳变 → 游戏判死/覆盖；每帧 ±1 累积器安全（绷带补血跳变安全，游戏认可治疗）
3. **活体断肢不要调 `DetachLimb`**：原生自带 20-30/s 失血 DoT（必死）——用视觉隐藏 + 自己控血；尸体可以调
4. **不要和游戏 Damage 路径打架**：优先走原生路径（`Damage()`/`SetBleeding`/`RecoverLife`）

**IL2CPP / Harmony 类**
5. **类型转换必须 `TryCast<T>()`**：interop 返回基类包装，C# `as` 按 CLR 类型检查恒失败
6. **Harmony 参数按名注入**：参数名必须与 interop 参数名完全一致（`SetBleeding(bool bleeding)` 不是 `value`），不匹配 = 静默注入 null
7. **重载方法必须显式类型**：`[HarmonyPatch(typeof(X), "M", new Type[]{...})]`，否则 Ambiguous 异常中断整个 PatchAll
8. **interop 不存在的方法不能 patch**：先 `ilspycmd` 确认签名再写，否则 `Undefined target method` → 插件加载失败
9. **跨 mod 补丁顺序用优先级**：`[HarmonyPriority(Priority.First)]` 先短路、`Priority.Last` 做清场（同方法多 Postfix 顺序由优先级决定）
10. **`ref` 参数在 IL2CPP 下生效**：改的是托管 interop 桩，实测 before=6→after=60（CombatTweaks 弹药/伤害倍率实证）

**UI / 渲染类**
11. **`new GUIStyle()` 默认 `normal.textColor` 是黑**，`GUI.color` 是乘法 tint → 必须显式 `normal.textColor = Color.white`；拷贝构造被 IL2CPP 裁剪
12. **运行时创建的 Texture2D/AudioClip 必须 `hideFlags=(HideFlags)61`**：否则进战斗场景被 Unity 卸载 → "日志全绿但什么都看不见"（排查此症状先怀疑对象被销毁）
13. **`GetWorldCorners(new Vector3[4])` 返回全零**：改用 `Il2CppStructArray<Vector3>` 或 `TransformPoint` 四角绕开

**构建 / 流程类**
14. **`[BepInPlugin]` 版本必须 `x.y.z`**，禁字母后缀（带后缀 = BepInEx 直接跳过插件）；**启动日志里的版本字符串也要同步改**（最容易漏）
15. **DLL 文件名 ≠ 插件名**：按 BepInPlugin 元数据识别，别按文件名猜（`ER2_RecoilOverhaul.dll` 里是 Universal Recoil Control）
16. **源码文件一律用 write/edit 工具**，不要用 PowerShell `Get-Content`/`Set-Content`（默认 ANSI/GBK 编解码 → 中文乱码 + CS1513）
17. **别用字符串搜索验证构建语言**：元数据字符串堆编码反直觉，EN/CN 两种构建都含中文串；看 `ilspycmd -t ER2ModManager.Plugin` 的 `DefaultChinese` 常量

**游戏机制类（改功能前必查）**
18. **AI 移动无法外部驱动**：`Soldier.Move()`/`NavMeshAgent.SetDestination` 都会被 AI 控制器覆盖——**唯一官方通道是 Lua API**（`Lua_Soldier/Lua_Squad.moveTo` + AI 三连释放，见 `ER2_zcode_era.md`）
19. **单位移动途中本来就会边走边打**（原生行为）——不要加"攻击移动"指令
20. **局内暂停时 `Time.time` 冻结**（timeScale=0）：所有节流/冷却/看门狗必须用 `Time.unscaledTime`，否则局内设置界面里永久停摆
21. **投降单位血量被游戏接管**：外部扣血无效 → 用计时器 `Kill` 等效"流血而死"
22. **背包添加物品会降级成基类**：`AddVirtualItem`/`AddItemToInventory` 都归一成 `VirtualItem` → 需要正确子类时**直接 `inv.items.Add(vi)`**
23. **原生回调拒绝外部替换的 UI 数据**：`CircularMenu2.ShowCircle` 换数据后原生选择回调匹配不到（选了没反应）——能走原生管线就走原生
24. **`BattleManager` 在主菜单也存在**：等场景实例（如 `DayNightCycle.instance`）就绪再操作
25. **`SpawnManager.SpawnAI`/`SpawnAISquadGlobal` 返回的是原生协程对象**（`Il2CppSystem.Collections.IEnumerator`）——**必须显式 `StartCoroutine` 启动才会运行**，否则回调永不来（"调了但什么都没生成"）。范式见 `UniversalGeneration/GenRunner.cs` 的 `StartCoroutineNative`
26. **未验证的 interop 信号不能当门控**：`DeathPanel.instance.gameObject.activeInHierarchy` 战斗中恒 true、`LoadingCircle.IsLoading()` 战斗中恒 false —— **先加诊断日志实测，再当门控用**（UnitInfoOverlay v1.0.2 悬浮窗消失根因）
27. **"上下文窗口"类拦​截必须校验对象身份边界**：CombatTweaks 友军爆炸窗口未校验 responsible 阵营 → 敌人手雷也建窗口 → 敌方死亡士兵倒地全被拦（v1.2.2 修复）
28. **`Creature.SetIncapacitated` 主动调用（血量>0）会被游戏判死**（失能 + 血量归零 = 死亡），不是"只是趴下"
29. **游戏更新会静默改原生方法签名 → 旧编译产物运行时 `MissingMethodException`**（2026-09-12 实测定案）：游戏把 `SettingsGUI_V2.UpdateOpenedMenu(bool)` 改成 `(bool, bool)`、`FillSettingPage(bool)` 改成 `(bool, bool, float, float)`（`GameAssembly.dll` 当天 17:21 更新，BepInEx 下次启动重新生成 interop）。ModManager 里 `s.UpdateOpenedMenu(true)` 的直接调用点运行时解析不到方法 → 异常**从 Harmony Prefix 逃逸**（发生在 JIT 解析调用点，前缀自己的 try/catch 拦不住）→ 整个 Prefix 失败 + 原生 `SettingsTabRight` 一起中断 = "按右翻页键不能翻页"。**对策：核心交互的原生调用走反射/`AccessTools` 按名取值 + 参数个数自适应；每次游戏更新后先看 `LogOutput.log` 有没有 `MissingMethodException`，再 `ilspycmd -t <Type> <Game>\BepInEx\interop\Assembly-CSharp.dll` 对比签名。**

## 5. 跨 mod UI 联动契约（改任何 mod 的 UI 前必读）

原生 UI 隐藏由 **Hide Anything**（`com.ryan.er2.nointeractionhints`，GUID 不变）统一管理，**14 类原生 UI**（`hints`/`notifications`/`objectiveBanner`/`hud`/`phaseBar`/`objectives`/`map`/`vehicle`/`misc`/`hitmarker`/`bloodSplash`/`chat`/`scope`/`worldMarkers`）+ 逐 mod 开关。

> **术语坑**：v4 起**已无 F5 热键**，改为「勾选 = 始终隐藏且严格生效」。但 `Shared/NoHintsHudLink.cs` 的注释仍写"F5 隐藏"、部分代码注释也残留 "F5"——**语义以 `HudCompat`（勾选制）为准**，别被旧注释误导。

新 mod 接入方式（**推荐方式 A，无编译期依赖**）：

```csharp
// csproj 加 <Compile Include="..\Shared\NoHintsHudLink.cs" Link="NoHintsHudLink.cs" />
if (ER2Shared.NoHintsHudLink.IsHidden("er2.你的modid", "显示名")) return; // 跳过绘制/入队
```

- 反射调用 `ER2NoInteractionHints.HudCompat.IsHudHidden(id, displayName)`，对方缺失/未就绪时**静默返回 false**（本 mod 独立运行不受影响，每帧查询开销可接受）
- **首次查询自动注册**该 id 的配置项（默认**不勾选**=显示），无需注册代码
- **想让它立刻出现在 ModManager**（不等进战斗懒注册）：在 Hide Anything 的 `HudCompat.knownNames` 预注册表**加一行**（`{"er2.你的modid","显示名"}`）——**新接入的 mod 必须加在这里**，否则开关要等首次查询才出现
- 持久型 UI（每帧 OnGUI）每帧开头查一次；提示型（`Hint.Display`）显示前查一次
- **方式 B（零代码）**：插件程序集里定义 `public/internal static bool` 字段，名为 `HudEnabled` / `HudVisible` / `ShowHud`（语义 true=显示 UI），会被**自动发现**；勾选后写 false 隐藏并被 `Enforce()` 锁定（外部改回 true 立即再写 false）
- 陷阱：别在 `Load()` 里"注册"（对方可能还没加载）——查询式自动注册无此问题
- 契约逻辑测试宿主在 guide §3.6 引用（改契约先跑测试再部署）

## 6. 工作流契约（每次改动必须遵守）

1. 改 `Plugin.cs`/源码 → 跑 `build.ps1`（或 `dotnet build` + 手动部署）
2. **部署时游戏可能在运行**（DLL 被锁）→ build.ps1 已内置 10 分钟轮询；失败就告知用户退出游戏
3. 请用户实测 → **回读 `LogOutput.log` 验证，不要猜**
4. 诊断规则：
   - patch 不生效 → 查参数名（陷阱 6）、版本号（陷阱 14）、Ambiguous（陷阱 7）
   - 无日志/加载失败 → 查 `Skipping type` / `Ambiguous` / `Error loading` / `TypeLoadException`
   - 功能时灵时不灵 → 查游戏重置/覆盖（战役改天气等）；**机制不确定时先加诊断日志让用户测一轮，用日志定位而非猜测**
5. `research_out/` 的反编译产物用完即清或并入根目录（.gitignore 已忽略 `tmp_*`/`deployed_dump/` 等）

**验收三件套**（漏一 = 没部署完）：① 源码与部署 DLL sha256 一致 ② 日志有 `Loading [<插件名> x.y.z]` 且与 `BepInPlugin` 一致 ③ 源码内版本字符串 grep 计数 ≥2（`BepInPlugin` + 启动日志）。

**发布前查文档三项**（2026-09-12 已修一轮；此后新增 mod 照此自检，脚本见 `ER2_projects_status.md` §5）：
① README 版本号 = `BepInPlugin` 版本（曾有两处漂移：`NoInteractionHints` 4.5.4 / README 4.5.3、`UnitInfoOverlay` 1.0.5 / README 1.0.4）；
② `README.txt` 与 `Nexus_description.md` 都存在 —— `build.ps1` 对缺失文档**静默跳过**，曾导致 `ER2_LimbTweaks_v2.13.101.zip` / `ER2_WeatherControl_v1.7.2.zip` 里**只有 DLL**；
③ 打包后拆 zip 核对内容（DLL + README.txt + Nexus_description.md），别只看"打包成功"。

## 7. 版本号与发布约定

- 版本号 `x.y.z` 纯数字；每次改动 z+1（大功能可进位）；**zip 名、启动日志字符串、Plugin.cs 三处同步**
- 发布简介按 N 网格式：**Description / Installation instructions / Main features / Requirements / Shout outs**
- 更新说明只讲**更新内容与达成效果**（简洁）；完整 README 按需
- **发布前清理调试/诊断日志**（高频日志、限频诊断全清），保留低频功能日志
- 发布包在 `C:\Users\71011\Downloads\<pkg>_v<版本>.zip`（zip 内 = DLL + README.txt + Nexus_description.md）
- **双语发布**（2026-09-05 起）：默认包 EN，`-Cn` 出中文包 → `README_CN.txt` / `Nexus_description_CN.md` 按包语言取（build.ps1 自动）；已做双语：SquadCommand、UniversalGeneration、**HighValueTarget（2026-09-13 补）**，其余 mod 只有 EN 文档
- **`-Cn` 会带 `CN_BUILD` 编译定义**（`DefaultChinese=true` → 游戏内提示为中文）→ **EN/CN 两个包的 DLL 内容不同**。决定最终部署语言靠构建顺序：惯例部署 **EN 构建**（ModManager 玩家要求，HVT 同此），故先跑 `-Cn`、最后跑默认包

### 7.1 每个 mod 必须有调试日志开关（2026-09-18 起为强制约定）

**背景**：ModManager 把开发期的 PoC 测试页（硬编码 `poc.header`/`poc.toggle`/`poc.slider`/`poc.button` 四行 + 只写日志的空回调）随发布版一起发了出去，玩家在 cfg 里开了 `NativePoc` 就看到「mod 菜单坏了」——满屏生 key + 点了没反应。**开发脚手架绝不能靠"默认关"来兜底，必须从发布版里删掉**；诊断日志则统一收进开关。

- **统一约定**：`Config.Bind("Debug", "debugLog", false, ...)` —— 节名 `Debug`、键名 `debugLog`、**默认 `false`**，语义「true = 输出诊断日志」
- **门控写法**：诊断日志一律 `if (Plugin.DebugOn) Plugin.ModLog.LogInfo(...)`（ModManager 用 `Plugin.DebugOn` 属性；其它 mod 用 `Plugin.debugLog.Value`）
- **`LogError` / `LogWarning` 不门控**——错误必须无条件可见；只门控 `LogInfo` 里的**高频/诊断**输出
- **保留**低频功能日志（启动横幅、页面注入、自动保存、重置等），这些是排查问题的锚点
- **发布版禁止**：`#if DEBUG` 之外仍可达的开发/测试页面、硬编码生 key 的临时行、只写日志不干事的空回调。**开发脚手架用完即删，不留"默认关"的开关**
- 已接入：SquadCommand、UniversalGeneration、Conquest（节名 `Diagnostics`，历史遗留）、FleshWoundsFixed（键名 `Debug Logging`，历史遗留）、**ModManager（2026-09-18）**
- **尚未接入（15 个）**：AIFood、CombatTweaks、ConquestRecon、HighValueTarget、HvtTestDriver、InventoryPause、LimbTweaks、MorePhysics、NoInteractionHints、ThrowableWheel、UnitCollision、UnitInfoOverlay、WeatherControl、ZoomAnywhere —— 后续改动这些 mod 时**顺手补上**

## 8. 快速验证清单

```
[ ] 日志有 "Loading [<插件名> x.y.z]"，版本与 BepInPlugin 一致
[ ] 无 "Skipping type" / "Ambiguous" / "Error loading" / "TypeLoadException"
[ ] 有本 mod 的功能触发日志
[ ] 源码/部署 DLL sha256 一致
[ ] 发布包 zip 已更新且版本号三处一致
```

## 9. 历史教训（避免重走弯路）

- **已删除并终止的项目**：`MorePhysics` 完整物理化（19 版迭代后废弃；**2026-09-13 以 v0.1.48 复活**——源码从 v0.1.47 反编译重建，单位碰撞模块对齐轻量版，见台账 §2.17）、`DirectControl` 单位接管、`SuperSoldiers` 精英单位、`HealthBars` 血条、`BattlefieldHud` 命中标记 —— **复盘全部保留在 `ER2_mod_经验.md`**，再碰同类需求先读。
- **BattleJournal（勋章/战报 mod）已放弃**：做完全流程后被用户发现 Nexus 有平替 → **提新 mod 方向前先确认生态里没有现成方案**（先搜 Nexus/问用户），别再主动提"勋章/战报/生涯统计"方向。
- **需求理解偏差的代价**：`DirectControl` 在错误理解（"接管单单位" vs 用户要的"框选多单位 RTS 指挥"）上做了 3 个版本才对齐 → **指挥/控制类需求先问清是「接管单个」还是「RTS 框选指挥」**（两者技术跨度天差地别）。
- **功能减法比加法更难也更重要**：Hide Anything 从"F5 热键+锁定+保存按钮"演化到"勾选制"，三个概念全被砍掉——每个存废都来自实际使用体验。
