# ER2_Mods 工作区 — AI 记忆与工作流（第一轮必读）

> **当前状态快照：2026-09-24（UniGen 2.5.0 / SquadCommand 1.4.19：UI 重绘一期落地——灰黑单色 UI（`uiMono` 可回退旧军绿）、选中标记由四段圆弧改直角角标、世界空间标记全改白/半透灰（明度档 + 虚线节奏 + 形状三重编码）、路线与登车线拆分、线宽距离补偿、脉动错相、名签底板、光标灰阶；新增 14 项视觉 cfg 开关）**。本文件是**唯一每轮必读**的入口。
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
3. `ER2_mod_dev_guide.md` —— 完整机制/API/踩坑参考（按需查；§2 机制、§3 陷阱 1-67、§3.6 跨 mod UI 契约）
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
├── SquadCommand/         er2.squadcommand             Battlefield Commander：上帝视角 RTS 小队指挥（最大工程，v1.4.14）
├── UniversalGeneration/  er2.universalgeneration      RTS 内自定义生成单位/载具/物品（含鼠标拖放，作弊向，SquadCommand 附属）
├── UnitCollision/        er2.morephysics.unitcollision 单位/尸体碰撞（MorePhysics 轻量保留版）
├── MorePhysics/          er2.morephysics                 完整物理化（v0.1.48 复活：源码自反编译重建 + 单位碰撞对齐轻量版）
├── UnitInfoOverlay/      er2.unitinfooverlay          单位状态悬浮显示（开发者调试工具）
├── HvtTestDriver/        er2.hvt.testdriver           HVT 自测工具（内部，不发布）
├── Endless/              er2.endless                  ER2 Endless：无尽模式（**当前主项目**，方案见 ER2_无尽模式_设计方案.md；M0 战斗尖峰已实现待实测——F6/F7/F8）
├── FleshWoundsFixed/     ER2_FleshWounds              第三方 Flesh Wounds 重建修复（紫贴图 bug）
├── Shared/NoHintsHudLink.cs                           跨 mod F5 隐藏联动（反射，无编译期依赖）
├── scripts/build.ps1                                   一体化构建：编译+部署+清cfg+打包
├── research_out/                                       反编译/解包研究 + conquest_salvage/（Conquest 源码快照，回收清单见其 README）
└── *.md                                                知识文档（见 §1）
```

**文档地图**：`ER2_mod_dev_guide.md`（工作流/机制/陷阱/各 mod 状态，102 KB）· `ER2_mod_经验.md`（陷阱全集 + 复盘，73 KB）· `ER2_mod_技能.md`（可复用技能）· `ER2_mod_工具.md`（工具速查）· `ER2_physics_system.md`（16 层碰撞矩阵解包）· `ER2_scene_objects_classification.md`（场景物分类体系）· `ER2_UI_design.md`（UI/IMGUI 机制）· `ER2_征服模式_设计方案.md`（征服模式方案 + 逐轮实测复盘；**已终止**，资产回收见文首注记）· **`ER2_征服模式_参考拆解.md`**（《地狱之门》征服模式 `.pak` 解包实证 + 《人间地狱》检索 + 机制映射表）· `ER2_无尽模式_设计方案.md`（无尽模式方案，当前主项目）。

**第三方游戏解包（2026-09-13 新增能力）**：《Call to Arms - Gates of Hell》装在
`E:\SteamLibrary\steamapps\common\Call to Arms - Gates of Hell`，其 `resource\*.pak`
**就是标准 ZIP**（文件头 `PK\x03\x04`）→ 用 .NET `ZipFile` 直接读，**别用 WinRAR**（会卡死）。
战略层数值全在 `resource\gamelogic.pak` 的 `set/dynamic_campaign/`；解包产物见
`research_out/goh_unpack/`（163 文件），拆解结论见 `ER2_征服模式_参考拆解.md`。

## 3. 命令白名单

| 命令 | 用途 |
|---|---|
| `powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字>` | **构建+部署+清cfg+打包一次完成**；`-SkipDeploy` 只打包；`-SkipPackage` 只部署；**`-Cn` 编译中文版并出 `_CN_` 包**。⚠️ 必须带 `-ExecutionPolicy Bypass -File`：本机执行策略 Restricted，直接 `& scripts\build.ps1` 会被**静默拒绝**（exit 0、无输出、无任何效果——部署时间戳不变是唯一线索） |
| `dotnet build -c Release <proj>` | 仅编译（workdir 为 mod 目录；查错 `2>&1 \| Select-String "error"`） |
| `Select-String <log> -Pattern "..."` | 查日志 / 过滤本 mod 日志（**每次实测后必查，不要猜**） |
| `ilspycmd -t <Type> <dll> -o <dir>` | 反编译查 API；也可反编译已部署 plugins DLL 恢复源码 |
| `Get-Item <game>\BepInEx\plugins\<name>.dll` | 确认部署（比对时间戳/sha256） |
| `"C:\Users\71011\新建文件夹\WinRAR.exe" x -o+ -y <rar> "<out>\"` | 解压 rar 发布包 |

**禁止**：修改游戏原文件 · 删除 plugins 里其他 mod · `FindObjectsOfType` 类每帧全场景扫描（用 `Creature.allCreatures` / `Creature.aliveCreatures` 静态列表，或按需 `Physics.OverlapSphere`）。

## 4. 致命陷阱（全部实测定案；完整 71 条见 guide §3）

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
10b. **跳过原生方法 = 连同它的副作用一起没**（音效/计数/状态机）。`SettingsTabRight/Left` 自带点击音效 → 任何 `return false`（自己的或第三方的）都会静音；判据只有一条：**这次原生会不会执行**——会执行就一个字都别补，不会执行就全补上（含"让位给第三方但对方 return false"）

**UI / 渲染类**
11. **`new GUIStyle()` 默认 `normal.textColor` 是黑**，`GUI.color` 是乘法 tint → 必须显式 `normal.textColor = Color.white`；拷贝构造被 IL2CPP 裁剪
12. **运行时创建的 Texture2D/AudioClip 必须 `hideFlags=(HideFlags)61`**：否则进战斗场景被 Unity 卸载 → "日志全绿但什么都看不见"（排查此症状先怀疑对象被销毁）
13. **`GetWorldCorners(new Vector3[4])` 返回全零**：改用 `Il2CppStructArray<Vector3>` 或 `TransformPoint` 四角绕开。
   **相关**：世界空间标记的「恒定屏占比」公式 `scale = dist×k` 观感会失真——数学上正确（世界尺寸∝距离抵消透视缩小），但实测成"近小远大"反直觉（FOV 动态变化 + 锚点偏移 + 屏高归一）。**要真实参照物直觉就用固定世界尺寸常量**（HVT v1.2.2 定为 `MarkerWorldSize=0.8f`）。详见 guide 陷阱 61

**构建 / 流程类**
14. **`[BepInPlugin]` 版本必须 `x.y.z`**，禁字母后缀（带后缀 = BepInEx 直接跳过插件）；**启动日志里的版本字符串也要同步改**（最容易漏）
15. **DLL 文件名 ≠ 插件名**：按 BepInPlugin 元数据识别，别按文件名猜（`ER2_RecoilOverhaul.dll` 里是 Universal Recoil Control）
16. **源码文件一律用 write/edit 工具**，不要用 PowerShell `Get-Content`/`Set-Content`（默认 ANSI/GBK 编解码 → 中文乱码 + CS1513）
17. **别用字符串搜索验证构建语言**：元数据字符串堆编码反直觉，EN/CN 两种构建都含中文串；看 `ilspycmd -t ER2ModManager.Plugin` 的 `DefaultChinese` 常量
17b. **`ilspycmd` 的 `-o` 绝不能指向 mod 目录内**：反编译出的 `.cs` 会被 SDK 通配符 glob 进编译 → `CS0101 类型已定义` + `CS0579 特性重复`（像源码错，实为产物）。**输出到项目外**（如 `%TEMP%`）用完即删。另：`Edit` 偶发"报成功但未生效"，改版本号等关键行后**必须 Read 复核 + 反编译复核**（陷阱 14 的实际执行方式）。详见 guide 陷阱 66
17c. **`GetAllItemsOfType<T>` 返回 `Il2CppArrayBase<T>`**（不是 `Il2CppSystem...List<T>`），须用全名 `Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<PropData>`（否则 CS0029/CS0246）。
17d. **等外部系统就绪，"非 null"不是有效判据**：`ItemsDatabase.GetAllItemsOfType` 在**数据库加载完成前返回非 null 的空数组**（不是 null、不抛异常）→ 用 `l != null` 当闸门会被骗过，一次跑死且永久放弃（UniGen 2.0.2 实证：整个物品生成功能不可用）。**正解 = 官方 `Loaded` 标志 + 实枚举非空双判据**（`ItemsDatabase.Loaded`），且就绪等待/零条都必须是**可重试的 attempt 循环**。详见 guide 陷阱 67
17d-2. **⚠️ 协程判活：绝不能用"时间流逝"，要用"tick 计数"**（UniGen 物品功能**栽了四次**才定案）：判据迭代 150s 硬阈值(2.0.3) → 20s 心跳(2.0.4) → "不判死只续跑"(2.0.5) → **tick 计数(2.0.6)**。前三轮共同错误 = **拿时间流逝当协程死亡的判据**——场景加载期间主线程被占、协程停摆但**墙上时钟照走**，二者不等价，必然误判。2.0.5 更糟："不判死"等于**不检测**（协程真死时 `probeState` 永远卡 1，看门狗一次都不重启，症状 = 日志停在第一条后**再无任何输出**）。**正解**：协程每帧 `probeTicks++`；看门狗比对计数是否增长，**不涨即真死**（`watchdogLastTicks < 0` 首次只建基线）。这样两个矛盾需求同时成立——场景加载时看门狗自己也没被调用（不误杀），看门狗被调用时协程必须也在涨（真死必发现）。**通用律：判活要让"被检测者"自己产出与时间无关的存活信号，检测者只在"自己也在跑"时比对。** 另：**任何"后台补齐型"任务都不该有永久终态**（2.0.2 一次跑死 → 2.0.4 触顶放弃，两次教训）。详见 guide 陷阱 67
17g. **⚠️ "流程永远走不到下一步"时，先怀疑"前置条件恒假"，别只查"循环活不活"**（UniGen 物品功能**栽了五轮**，前四轮全修错地方）：2.0.6 把 tick 判活做对后，诊断日志连续 154s 跨三场景打印 `Loaded=true 但 items 枚举 n=0` → **闸门条件永远不成立**（`ItemsDatabase.Loaded` 恒 true；`GetAllItemsOfType<PropData>(PropType.items)` 恒空）→ 协程健康、看门狗也正常重启，**但枚举代码一次都没执行**。同期发现更深的坑：该原生方法**按泛型 T 过滤**，库里存的是 **`ItemObject`**（`GetItemObject(id)` 返回的类型，带 `item_id`/`icon`），一直问 `PropData` 所以恒 0。**修法：把闸门删掉而不是换个条件**——`ProbeDatabaseReady`(返回 bool，阻塞) → `SampleCounts`(void，只写日志)，流程无条件开工 + 幂等重试。**通用律：① 排查顺序 = 先问"条件本身可能成立吗"，再问"等条件的循环还活着吗"；② 能观测就别决策（观测进日志，决策不依赖可能错的前提）；③ 无条件开工 + 去重，通常优于"等一个自认为正确的条件"。** 详见 guide 陷阱 67
17e. **判断"UI 文字是否渲染"别靠缩略图肉眼**：UniGen 2.0.2 排查中曾把 346×531 截图里的火炮页签误判为"名称空白"（实为完全正常）。**两个硬手段：① 把 mod 自己的解析逻辑（正则/排序/分页）本地复现并与截图逐行比对；② 对截图做像素采样**（正常渲染的文字会留下成片的纯白像素，按行高分布）。
17g2. **⚠️ 分类/子分类只准用"游戏自己的判定"，绝不用字符串启发式**（UniGen 2.1.0 定案）：2.0.2 曾用"文件名猜 item_id"→**大面积失效**（`ArisakaT38.prefab` 与运行时键不是同一套）。2.1.0 做物品子分类时改为**先反编译找官方字段**：`Weapon : HandheldItem : ItemObject` → 枚举到的实例可 `((Il2CppObjectBase)io).TryCast<Weapon>()`（陷阱 5：C# `as` 恒失败）取 `weaponPose`（`rifle=1/pistol=2`）；`Interagible.IsWerable()` 判可穿戴。**给物品分类先看 `IsWeapon()`/`IsWerable()`/`weaponPose`，比任何正则可靠。** 另 `WeaponPose` 只有两档，别指望更细的官方口径。
17g3. **IL2CPP interop 下部分构造函数被裁剪**：`new RectOffset(int,int,int,int)` → **CS1729**（无此重载），改用 `GUIStyle.contentOffset = new Vector2(...)`；`new GUIStyle(GUIStyle)` 拷贝构造同样被裁（陷阱 5/11）。**"看起来该有的构造"报 CS1729，先怀疑被裁剪，找替代属性。** 另：自建 Texture2D 给 GUIStyle 用要 `hideFlags=(HideFlags)61`（陷阱 12），且**别复用 `SolidTexture` 这类单槽缓存**——会把别的样式一起换掉。
17g11. **⚠️ IMGUI 固定面板高度必须与绘制逐项镜像——动态行数不计入高度就溢出**（UniGen 2.4.0 定案，用户报"菜单列表的选项都跑到菜单外了"）：`PanelRect()` 高度是固定求和，而 OnGUI 的 y 逐段累加——物品页签族/收藏子页签/物品子分类行/字母索引行/物品帮助行**全都不在公式里** → 物品页实际高 ~630px、面板背景 ~504px，下半段画到背景外（单位页只差 4px 所以没暴露）。**修法：高度按内容实算**——`ItemListHeight(bucket, favOnly)` 用与绘制**同一判据**（`ItemCatalog.SubsOf`/`LettersOf`）计算，`PanelRect` 镜像每一段累加。**通用律：IMGUI 的 rect 计算与绘制命令是同一份布局信息的两个消费者，加一行 UI 必须同笔提交改高度镜像。** 详见 guide 陷阱 75
17g12. **⚠️ 定宽页签 + 固定字号 = 长标签溢出压邻居（IMGUI 按钮文字不裁剪）**（UniGen 2.4.1 定案，用户报"这个标签页有重叠，显示不完整"）：页签定宽网格每格 ≈72px、字号固定 12，英文 "Mod Vehicles" ≈78~84px 超宽，`GUI.Button` 文字 MiddleCenter 且**不按矩形裁剪** → 压到相邻页签；中文 "Mod载具" 放得下 → **只在英文版暴露**。**修法**：`DrawTabButton` 逐格算字号（CJK≈1.0em/拉丁≈0.56em 估宽，下限 8）；⚠️ 用**专用样式实例**（`tabStyle/tabActiveStyle`）逐次改写 `fontSize`——不能复用共享样式，也不能 `new GUIStyle(style)` 拷贝（陷阱 5 拷贝构造被裁剪）。**通用律：定宽容器 + 可变长文本，字号/换行/截断三选一必须在绘制前定案；本地化宽度按最长语言验证。** 详见 guide 陷阱 76
17g13. **⚠️ 第三方内容发现不能挂运行时"已装列表"——主菜单恒空 + 一次性探测写终态 = 页签永久为空**（UniGen 2.4.1 定案，用户报"Mod载具页签是空的"）：`ModsLoader.mods_installed` **进战斗后才填充**，探测在主菜单等 30s 拿到 mod=0 后 `ready=true` 一锤定音（看门狗见 ready 直接 return）→ mod 内容（战斗时物品库 949→2107）永远进不来。**修法**：① 目录**磁盘直扫**（`<Steam库>/steamapps/workshop/content/<appid>/*/index.xml`，appid 读 `steam_appid.txt` 兜底 1324780；解析 `libraryfolders.vdf` 其他库；+ `<游戏>/Mods`；运行时列表只兜底）；② 解析与**校验**解耦——校验等 `ItemCatalog.Ready`，没就绪 15s 后再来；③ 空闲 60s 重扫，候选缓存复用 + `HashSet` 去重，载具校验失败 5 次放弃该条（打日志）。**通用律："扫完了"≠"扫到了东西"；扫完为空不是终态，是下一轮再扫；依赖运行时状态的数据源先在主菜单验证是否已填充。** 详见 guide 陷阱 77
17g14. **⚠️ 靠纪律维持的"两处一致"迟早复发——必须改成单一数据源**（UniGen 2.4.2 定案，2.4.0/2.4.1 两次都是这一类）：2.4.0 修的是"高度公式漏算动态行"，但修法是**镜像**——`PanelRect()` 里重算一遍 y 累加，绘制里再算一遍；任何一次加行/改行都要记得改两处，漏一处就复发。**改法**：先建**行计划**再画——`enum RowKind` + `struct Row{Kind,H}` + `List<Row> rows`；`BuildRows()` 排这一帧的行，`PanelRect()` = `Σrows[i].H + 留白`，`DrawRows(r)` 遍历同一份 `rows`、每行只拿自己的 Rect、不再自行累加 y。**加/删行收敛为三步**：加枚举 → `BuildRows` 排一行 → 写一个 `DrawXxxRow`，高度自动正确。**通用律：同一个量的两个消费者（算高度 / 去绘制）必须读同一份数据；"记得同步改两处"不是修复，是延期复发。** 详见 guide 陷阱 78
17g15. **⚠️ 两个互不引用的程序集要统一观感 → 共享源码 + `<Compile Include>` 源码级链接**（UniGen 2.4.2 / SquadCommand 1.4.18 定案）：宿主与 addon **只按反射联动、互不引用**（`HostLink`），不能靠共享程序集（会引入加载顺序/缺失依赖问题），所以把 `Shared/Er2Ui.cs` 用 `<Compile Include="..\Shared\Er2Ui.cs" Link="Shared\Er2Ui.cs" />` 分别编进两个 DLL；类声明 `internal` → 两边各一份、互不影响（**这也让指挥官侧被玩家改主题色时不会污染生成面板**）。**纪律：设计令牌（间距/字号/配色）与绘制原语只有一处定义**——两边各写一份必然变成"两个面板长得不一样"。**通用律：共享代码前先问"两边能不能互相引用"；不能引用就用源码链接，别复制粘贴。** 详见 guide 陷阱 79
17g16. **⚠️ 文本自适应不要按字符系数估宽，用 `CalcSize` 实测并缓存**（UniGen 2.4.2 改进 2.4.1）：2.4.1 用 CJK≈1.0em / 拉丁≈0.56em 估宽→不准（`TabGrid` 落错字号）。`GUIStyle.CalcSize` 在 IL2CPP 下**可用**（BackpackPanel 1.4.9 起长期验证），但要"设字号 → 测量 → `finally` 还原"（样式是共享实例，测量不能留副作用），且结果**必须缓存**（每次调用有 `GUIContent` 分配；按 `(宽|max|min|文本)` 做 key，>4000 条清空）。**缓存编码技巧：正数 = 放得下（值即字号），负数 = 放不下**，一个 int 同时带出两个结论。**通用律：能测量的别估算；测量有副作用的必须还原并缓存。** 详见 guide 陷阱 80
17g17. **⚠️ 单色主题下色相这一维整体作废——语义改走「明度档 + 虚线节奏 + 形状」三重编码**（SquadCommand 1.4.19 / UniGen 2.5.0 定案，用户："标记点等都用白色或半透明的灰色。我要那种灰黑色的UI"）：原标记全靠色相区分（友军灰/选中白/移动黄/集火红/阵型橙），确定灰黑单色后**色相不可用**。**改法**：① 明度档——越"正在操作"越亮（友军 .28 → 路线 .26 → 登车 .48 → 阵型 .60/.80 → 移动 .85 → 选中 .92）；② 虚线节奏——路线长划(14/32 实)、登车线短划(22/32 实)（原两者**颜色字面量完全相同**，是实锤 bug）；③ 形状——角标/小圈+点/大环+名签。仅保留危险(集火红)与权限(火力点橙)两类彩色，且 cfg 可关。**通用律：先确定"能用几维编码"再分配语义；色相只是一维，别把所有语义堆在它上面。** 详见 guide 陷阱 81
17g18. **⚠️ 审阅"形状/几何"改动必须回到生成代码逐行核对，别凭截图想象**（SquadCommand 1.4.19 定案，用户："你这选中标记画的跟台风一样"）：`GetBracket` 原几何 = 4 段 9 点圆弧（`start=q*90+22.5`、`a=(start+45*i/8)°`）→ 四段同心弧 + 中心点，像气象符号；而方案文档里我写成"四角圆弧，RTS 辨识度高"——语义没错、视觉全错。**修法**：改**直角折角**（`positionCount 9→3`，三点 `[edge 端点, 角顶点, 另一 edge 端点]`，`loop=false`），斜视角下弧退化成椭圆、**直角在任何视角都保持 L 形**。只改 `GetBracket` 几何，4 个调用点签名不变。**通用律：形状类改动必须核对点数/角度公式/坐标序列；文档里严格区分"我确认的"与"我推测的"。** 详见 guide 陷阱 82
17g19. **⚠️ 世界空间 `LineRenderer.widthMultiplier` 是世界单位；且"代码里有某功能" ≠ "功能在生效"**（SquadCommand 1.4.19 定案）：`widthMultiplier` 不是像素——RTS 拉远后 0.05m 的线细成一丝，必须 `WidthScale(camDist)=Clamp(camDist/30,0.6,2.5)` 补偿（每帧算一次 `camDist` 复用）。另：`LineMatNoDepth()`（ZTest Always）**定义了却从未被调用**，`Bracket/Line/Arrow/Dot` 连 `throughWall` 参数都没有 → **实际所有标记都参与深度测试，"穿墙"从未生效**（我在上一版方案里把它当成"穿墙材质混用无规则"，是错的）。**通用律：动世界空间渲染前先确认参数单位；读代码判断功能是否存在时，必须顺着调用点查一遍——私有/未调用方法就是死代码。** 详见 guide 陷阱 83
17g20. **⚠️ 想让 UI "活"起来，令牌到使用点之间的每一环都不能是 `const`——否则自适应彻底失效**（SquadCommand 1.4.20 / UniGen 2.5.1，用户："UI 不能是死的"→ 澄清为**自适应，不是手动拖拽**）：`Er2Ui` 的 `Pad/Gap/RowH/TabH/FontTitle/FontBody` 原全是 `const`，更致命的是**调用点写 `private const float PanelW = Er2Ui.PanelW;`**——`const` 在编译期钉死，即使后来把 `Er2Ui.PanelW` 改成属性，调用点拿到的仍是编译时的 1.0 倍值。**表现：加了 `Scale` 字段、界面一动不动。** 修法：① 令牌 `const` → `=> base * Scale`（字号 int 必须 `RoundToInt`，直接截断会让 0.8 倍下 12→9）；② **全仓扫 `const float`/`const int` 里的像素量**，调用点的 `const` 副本同样改属性（漏一处 = 那处仍钉死）；③ 改倍率只走 `SetScale()`（clamp + 值未变早退 + **清 `FitSize` 缓存**，否则拿到旧字号下的测量结果）。**通用律：某个量要"运行时可变"，它到使用点之间的整条链上都不能有编译期常量。** 详见 guide 陷阱 84
17g21. **⚠️ 自适应要跟随游戏原生倍率 `ResourcesManager.ResolutionMult`，别自己按屏幕另算一套**（SquadCommand 1.4.20 / UniGen 2.5.1，用户："用游戏原生方式实现"）：它就是玩家在游戏设置里调的那个 UI 大小，**原生 HUD 全部按它缩放**；自己按 `min(Screen.w/1920, Screen.h/1080)` 算的倍率和它**不是同一个量**——玩家调了游戏 UI 大小而 mod 不动，两边观感立刻脱节，这正是"不像原生"的来源。**修法**：`NativeResMult()` 优先读它、取不到才按屏幕兜底（仍是自适应，别钉死 1.0），0.5s 缓存（OnGUI 每帧多次事件 × 十几处取样式 = 每秒上千次 interop），clamp `[0.75, 1.6]`。兜底取 `min(w/1920, h/1080)` 而非只按高度——只按高度会在 21:9 超宽屏低估横向空间。**通用律：mod 要"像原生"，先找原生自己用的那个量；不要另立算法，哪怕你的更合理。** 详见 guide 陷阱 85
17g22. **⚠️ "每帧一次的初始化"要挂在所有绘制路径的公共祖先上**（UniGen 2.5.1 定案）：`GenRunner.Draw()` 三个分支——携带中（`ItemDragger.Draw` + `DrawFlash`）、放置中（`DrawPlacingBadge` + `DrawFlash`）、常规（`DrawToggleButton` + `GenPanel.Draw`）。**只有最后一支经过 `GenPanel.Draw()`**，把 `AutoScale()` 放那里 → 前两支永远用旧倍率。**修法**：放总入口开头（幂等，值没变无副作用），子入口保留调用无害。**通用律：列全分支再决定挂点，别默认主分支是唯一路径。** 详见 guide 陷阱 86
17g23. **⚠️ 样式缓存重建要"各判各的"，全局 dirty 标志会被第一个面板清掉**（SquadCommand 1.4.20 / UniGen 2.5.1）：倍率变了要重建 `GUIStyle`（字号写死在样式里）。用**全局** `styleDirty` 时，背包/信息面板/生成面板共用它，第一个重建后清掉标志 → **其余本帧不重建** → "缩放后一半变了、一半没变"，且随哪个面板先画而变、极难复现。**修法**：各存 `styleScale`，判 `Er2Ui.ScaleChangedSince(styleScale)` 各自重建。**通用律：多个消费者共享一个一次性标志是经典竞态；改成各消费者记录自己上次见到的版本号。** 详见 guide 陷阱 87
17g24. **⚠️ 硬编码定宽在窄屏会画到屏幕外——居中布局的隐含前提是"元素宽 ≤ 容器宽"**（SquadCommand 1.4.20 / UniGen 2.5.1）：底部提示条 `new Rect((Screen.width - 1400f) * 0.5f, …)` 在 1366 屏上 **x 变负数**（居中公式对超宽元素直接失效，不是"两边各裁一点"）；携带徽标 560px 同理。**这类写法在 1920 开发机上永远测不出来。** 修法：`ScreenFit(designedW, margin=40) => Min(designedW * Scale, Screen.width - margin)`，所有"设计稿上的固定宽"都过一遍；高度乘 `Scale`。**通用律：写死宽度时要么证明它永远装得下，要么做收敛。** 详见 guide 陷阱 88
17g28. **⚠️ "没有设计感"先补结构线，别先调颜色**——面板层次要面 + 线 + 条三层（SquadCommand 1.4.21 / UniGen 2.5.2 定案，用户："只有单一的色块没有设计感"）：所有面都是纯色填充、没有边框/分隔线/强调条时，层次只能靠明度差硬撑；叠在雪地/沙地这类亮背景上明度差被压平 → 一眼就是"几个灰块"。**修法**：新增三个绘制原语 `Frame(Rect,Color,float)`（矩形描边）、`HLine(Rect,Color)`（分隔线）、`AccentBar(Rect,Color,float)`（左竖条），再配独立标题条底色 + 标题下分隔线 + 列表内凹边框 + 收藏行左竖条。**通用律：暗色 UI 的设计感八成来自分隔与对齐，不是配色；先画结构线再调色，顺序反了怎么调都像色块。** 详见 guide 陷阱 92
17g27. **⚠️ 半透明在 3D 场景里质感差——层次从 α 移到灰度值**（SquadCommand 1.4.21 定案，用户："半透明灰色质感不好"）：α 0.26~0.48 的世界标记会被草地/雪地/沙地"吃掉"——颜色随背景漂移、边界发虚、重叠互相穿透、亮背景几乎消失。**半透明在 UI 面板里是优点（看得见战场），在世界空间标记里是缺点。** 修法：世界标记改 **α ≥ 0.80 的灰阶实色**，层次靠灰度值（`#9AA1A8` → `#C6CBD0` → `#E2E6EA` → 纯白），形状/虚线节奏不变；只给"预览"类（幽灵单位）留半透明——那正是它读作预览的依据。**通用律：单色主题有三个层次维度（灰度值 / α / 形状节奏），3D 场景优先灰度值 + 形状，把 α 留给"临时/未确定"的语义。** 详见 guide 陷阱 91
17g26. **⚠️ 不要用 `transform.localScale` 当"参数"传——它会连带放大子对象的一切**（SquadCommand 1.4.21 定案，用户："选中单位后的标记怎么变成箭头了"）：角标根对象 `localScale = (radius,1,radius)` 放大标记，而 `LineRenderer` 线宽会被父 `lossyScale` 连带放大 → 载具上（radius ≤ 4.2）0.1 m 的线变 ~0.5~1.0 m，而折角臂长只有 0.34×radius ≈ 1 m → **两条粗臂糊成实心三角块**，斜视角下正是"箭头"。**这也是"线太粗"的同一个根因**（改前先回源码核实成因，别照截图猜——陷阱 82）。修法：父 scale 恒 `Vector3.one`、半径写进顶点（缓冲复用零分配），臂长 0.34 → 0.42。**通用律：缩放作用于子对象的一切（线宽/字号/粒子 size），参数就写进数据（顶点/字段），别塞进 transform。** 详见 guide 陷阱 90
17g25. **⚠️ 3D 元素的"粗细"参数应该是屏幕像素，世界单位由代码反算**（SquadCommand 1.4.21 定案，用户："绘制的线条都太粗了"）：`LineRenderer.widthMultiplier` 是世界单位，屏幕像素宽 = `width_m × screenH / (2·dist·tan(fov/2))`。写死世界单位 → 近粗远细；用"经验倍率"（`dist/30`）补偿 → 像素宽是恒定了但**与分辨率强耦合**（1440p 粗 1.33×、4K 粗 2×），两种因素叠加时靠调数值根本调不好。**修法**：调用点只写 **1080p 目标像素宽**（1.1 最细 … 2.2 强调），`LineWidth(px, dist) = px × 2 × dist × tan(fov/2) / 1080`（screenH 在推导里约掉 → 占屏比例与分辨率无关）；FOV 从相机注入、取不到按 60° 兜底。**教训：1.4.21 第一轮把"改除数"和"砍基准值"叠加，实际落到 ~0.6px（细到看不见）——改视觉参数前先把量纲算清楚，别同时动两个互相耦合的因子。** 详见 guide 陷阱 89

17g32. **⚠️ 半透明面板会被背景染色——要黑必须提高不透明度；"质感"来自纹理不是颜色**（SquadCommand 1.4.23 / UniGen 2.5.4 定案，用户："这完全就是棕色，黑色呢，我要皮革的那种感觉，然后更黑一点"）：① **黑不下来**——α 0.82 的面板叠在棕色泥地上，`dst = src×0.82 + terrain×0.18`，地形是棕的、面板就永远是棕的，**压配色板没用**（只压了那 82%）；"半透明"与"黑"在亮背景上互相矛盾。修法：α → 0.92 保观感、底色压到近黑 `#0C0906`。② **不像皮**——近黑矩形仍只是色板；皮革观感来自**斑驳 + 受光边缘**。修法：`PanelBase(r)` = 近黑填充 + `Leather(r,0.10f)`（64×64 程序化 value noise 两层倍频，灰度 0.34~0.66 只压暗不泛灰）+ 顶部 1px 受光边；平铺用 `GUI.DrawTextureWithTexCoords` + `wrapMode=Repeat` + `hideFlags=61`。③ **容器高度必须 ≥ 内容 + 上下留白**——标题行原高 26px 而内容"从 +8 开始、高 22"→ 底 30 > 26 压在分隔线上（用户："菜单最上面的按钮你没发现重叠了吗"）；行高 = 上留白 + 内容高 + 下留白，别凭感觉。**通用律：半透明层叠在非中性色背景上，实际观感 = 混合色；要"黑"就提 α，要"质感"就加纹理，颜色两件都做不到。** 详见 guide 陷阱 96

17g31. **⚠️ 线/网格不吃 MSAA——3D 标记抗锯齿靠贴图羽化**（SquadCommand 1.4.22 定案，用户："为什么还有这么多锯齿"）：`LineRenderer` 与自建 Mesh 走硬边光栅化，玩家 MSAA 未必开 → 斜线/圆盘边缘就是楼梯。**这不是线宽问题，调细只是让锯齿变小、消不掉。** 修法：给线材质换**宽度方向 alpha 渐变贴图**（`LineRenderer` 的 UV.y 恰好跨宽度 0..1，2×16 竖向渐变、两侧各 25% `SmoothStep` = 手工抗锯齿），圆盘用**径向**羽化贴图 + UV 辐射（中心 0.5、边缘 0.5+0.5cos/sin）。**RGB 必须预乘 alpha**（`Sprites/Default` 是 `Blend One OneMinusSrcAlpha`，非预乘出黑边）。闭合圆分段 48→64（近看折角可见）。**通用律：程序化几何想"看起来干净"，就在颜色里留渐变（alpha 羽化），别指望后处理抗锯齿。** 详见 guide 陷阱 95
17g30. **⚠️ 自适应 UI 里"尺寸"和"间距"都必须单一数据源**（SquadCommand 1.4.22 / UniGen 2.5.3 定案，用户："UI 各元素区分不明显"+"背景与文字的重叠"）：① **尺寸**——行高已随 `Scale` 但行**内部**控件仍是硬编码（`90f`/`24f`/`220f`/`20f`），缩放后"行"变大了"行里的东西"没变 → 控件错位、文字压到邻行。逐个矩形搜一遍：`new Rect(` 里没有 `* Scale` 的都是嫌疑。② **间距**——行间距只内嵌在**部分**行高度里（`List`/`SubTabs` 有 `+ Gap`，`Crew`/`Preview` 没有）→ 前者正常、后者紧贴。修法：行高定义保持纯粹（不含间距），**间距统一在行循环加一次** `y += row.H + (i + 1 < rows.Count ? Gap : 0)`，面板总高用**同一条**规则累加。**通用律：布局的每类规则（尺寸/间距/对齐）只能有一个施加点；分散的"顺手加一点"必然漏。** 详见 guide 陷阱 94
17g29. **⚠️ 只要叫"距离"就必须问清参照系——`position.magnitude` 是到世界原点的距离**（SquadCommand 1.4.22 定案，用户第二次："我不是说了太粗了吗"）：标记线宽由相机距离推导，而 `MarkerCamDist()` 返回 `cam.transform.position.magnitude` = 相机到**世界原点**的模长。ER2 地图原点离战区可达数百米 → 永远是错误的大数。**旧版被 `Clamp(0.6,2.5)` 掩盖**（最多 2.5 倍，看着只是"有点粗"）；1.4.21 改线性像素公式后错值直接进线宽 → 环填成实心圆盘、角标糊成粗 X。修法：取**视线与地面交点的真实距离**（`Physics.Raycast(cam.position, cam.forward)` 命中点求距离，打不中时用相机高度兜底）0.1s 缓存；**GodView 与阵型标记共用同一个函数**（两处各写一份必漂移，陷阱 78）。**通用律：改任何"距离/尺寸/倍率"前先在注释里写下它的参照系；被 clamp 掩盖的量，去掉 clamp 的瞬间就会暴露它一直是错的。** 详见 guide 陷阱 93

17g10. **⚠️ 原生 `Interaction.Call()` 没有距离校验——地面交互菜单里的"拾起"必须改走自己的走过去链路**（SquadCommand 1.4.17 定案，用户报"让单位拾取枪械时可以隔空拾取"）：地面物品右键按交互数分流（单交互 → `RequestItemPickup` 走过去捡；多交互 → 原生交互菜单，点条目 = 原样 `Call()`）。**`HandheldItem`（Weapon 父类）覆写了 `GetInteractions`** → 枪械天生多交互（"拾起置于右手"）→ 永远走菜单 → 原生交互为 FPS 玩家设计、无距离检查 → 隔空吸包。**修法**：菜单另存**未翻译原文**列表（`menuRaw`，与 `menuLabels` 严格等长——剪枝/合成"穿上"条目都要同步），`ExecuteInteraction` 里 `menuGroundItem != null` 且原文以**"拾起"**开头 → 改调 `RequestItemPickup`（联动半径内即时/超出走过去），其余交互（补充弹药等）保持 `Call()`。**通用律：替玩家执行原生交互前，先问它原生靠什么保证前置条件（距离/朝向/停稳）——上帝视角没有这些保证。** 详见 guide 陷阱 74
17g9. **EN 漏中文有两种形态，自检日志只抓第一种**（UniGen 2.3.0 定案，用户截图"可穿戴"仍中文）：① **字典键漂移**——查了表但键对不上 → `Tr` 缺失自检能抓；② **调用点漏包 `Ui.Tr`**——`SubLabel` 返回中文原串直接上屏，根本没查表 → 自检抓不到。后者要扫"所有用户可见串的出口"（按钮/标签/徽标的字符串表达式）确认都过 `Tr`；2.3.0 全量核对过 `BucketLabel`/`favCatNames`/`catNames` 均已包，仅 `SubLabel` 调用点漏。
17g8. **预览幽灵必须"先落位、再 `TrackGhost`"——顺序反了幽灵每帧被挪到世界原点附近**（UniGen 2.3.0 定案，用户报"物品的 3D 模型不显示"，2.2.0 起从未显示过）：`TrackGhost(g, anchor)` 记录的是 `g.transform.position − anchor`，而 `Instantiate(prefab)` 的克隆体在 **prefab 模板的原始坐标**（通常原点附近），不在锚点——先 TrackGhost 后落位 → 偏移=模板坐标−锚点（巨大）→ 下一帧 `MovePreviewTo` 每帧按错误偏移把幽灵挪走。单位/载具预览天然不踩坑（生成即在锚点，偏移≈0），**任何新预览类型（物品/空投/建筑）都要先把 `transform.position` 设到落点再 TrackGhost**。配套：失败路径（prefab null / 实例化失败 / Ghostify false）必须无条件 `LogWarning`；幽灵命名统一 `UniGenPreview_` 前缀以吃宿主 `IsGhostTransform` 射线豁免。
17g7. **EN 字典的键必须与代码里 `Ui.Tr("…")` 的串逐字一致——措辞一改就静默回退中文**（UniGen 2.2.2 定案，用户报"英文版为什么还有中文"）：`Ui.Tr` 查不到就 `return cn`，**不会报错也不会有任何痕迹**（陷阱 17）。排查顺序：① 先反编译部署 DLL 的 `Ui` 证伪"部署成 CN 版"（CN 版 `Tr` 直接 `return cn`，EN 版有字典+查表）；② 用一次性脚本比对**全量 `Ui.Tr("字面量")` 串 vs 字典键**（注意：还有 `Ui.Tr(SubLabel(x))` 这类**变量传参**，字面量扫描扫不到，要单独核）。修完仍可能在**游戏数据**里看到中文——物品/小队/载具名称来自游戏库与 mod 自己的 index.xml，跟随游戏语言，不是 mod 文案。**长效防复发：`Ui.Tr` 查表失败时打一条日志点名该串（去重要只打一次），字典漂移立刻可见。**
17g6. **mod/第三方内容：运行时数据库无法区分"官方"与"mod"，用 mod 自带清单 + 运行时校验**（UniGen 2.2.0 定案）：`GetAllItemsOfType<PropData>` 恒空（库按泛型 T 过滤，陷阱 67 同源）、`ItemObject` 上**没有 mod_id** → 运行时无法筛选 mod 条目。**正解：磁盘解析每个已装 mod 的 `index.xml`**（`ModsLoader.mods_installed` 给出 assetBundleFolder，向上找 index.xml；`Prefabs/RegisteredPrefab/{PrefabName,DisplayName,Type}`，Type=ModPropType 枚举序号 2=载具 3~7=物品类），条目 id 用游戏公开 API **`ModsLoader.GenerateModItemId(bundleName, prefabName)`** 计算，再逐一运行时校验（物品 `GetItemObject(id) != null`；载具 `VehicleSpawner.GetVehiclePrefabAsync`，**临时 spawner 不能提前销毁——会杀死内部协程导致回调不来**，等回调或超时再删）。委托用 `Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<T>` 转托管 lambda，失败要退化路径。另：mod 小队/自定义小队注册进 `SquadsArchive.squads` 后**已被全量 key 枚举覆盖**，别重复建桶，打诊断日志证实即可。
17g5. **⚠️ IMGUI 里"某个控件一画，后面整块全空白"→ 先怀疑该控件原生方法被裁剪**（UniGen 2.1.1 定案，2.1.0 翻车）：症状是**子分类页签画出来了、下面的列表全空**，日志 `[UniGen] OnGUI 异常: Method unstripping failed` ×8 → **`GUI.TextField`（连带 `GUI.SetNextControlName`）在本游戏 IL2CPP 构建中被 Unity 裁剪**，一调用就抛，异常冒泡到 OnGUI 外层 catch，**整帧中断**，之后绘制的控件全部消失；**先于它绘制的控件幸存**——这正是"页签在、列表不在"的原因。**判据只有一个：日志里的 `Method unstripping failed`。** 修法：彻底弃用键盘输入，改**纯点击**方案（UniGen 用「首字母索引行」：只列当前桶/子分类实际出现过的字母 +「全部」，按钮 24px 宽按面板宽自动换行，30+ 字母也不挤）。**通用律：① 面板里能用按钮就别用输入框，IL2CPP 游戏的 `GUI.TextField`/`EditorGUI` 类方法风险极高；② 排查顺序 = 看异常发生在"哪一段绘制之后"，幸存/消失的分界线就是出事控件的位置。** 另：字母/筛选类按钮行**别按数量均分宽度**（字母多时窄到不可点），固定宽 + 自动换行。
17g4. **两个模块写同一个 ConfigEntry 必须合并成单一写入点**：UniGen 单位收藏（`v:`/`i:`）与物品收藏（`t:`）同存 `Plugin.favorites` 一条字符串；两处各自 `Value = ...` 会**整条互相覆盖**（症状：收藏了物品，重开后单位收藏没了）。**只留一处写入，另一处提供 `FavIdsPrefixed()` 供拼接。**
17f. **`Edit` 报"成功"不等于落盘——改版本号必须"回读 + 反编译"双复核**：UniGen 2.0.4 本轮 `[BepInPlugin]` 那行**首次 Edit 未落盘**，反编译才发现**特性还是 2.0.3、启动日志已是 2.0.4**（陷阱 14 经典形态）。若直接打包会产出"日志说新版、BepInEx 按旧版注册"的坏包。**流程：Edit → Read 回读确认 → 构建 → 反编译两处版本字符串比对。** 另：用户报"中文版没和英文版同步"时，先比对**部署 DLL 与构建产物的 sha256**（本轮逐字节一致 → 直接证伪该假设）。

**游戏机制类（改功能前必查）**
18. **AI 移动无法外部驱动**：`Soldier.Move()`/`NavMeshAgent.SetDestination` 都会被 AI 控制器覆盖——**唯一官方通道是 Lua API**（`Lua_Soldier/Lua_Squad.moveTo` + AI 三连释放，见 `ER2_zcode_era.md`）
19. **单位移动途中本来就会边走边打**（原生行为）——不要加"攻击移动"指令
20. **局内暂停时 `Time.time` 冻结**（timeScale=0）：所有节流/冷却/看门狗必须用 `Time.unscaledTime`，否则局内设置界面里永久停摆
21. **投降单位血量被游戏接管**：外部扣血无效 → 用计时器 `Kill` 等效"流血而死"
22. **背包添加物品会降级成基类**：`AddVirtualItem`/`AddItemToInventory` 都归一成 `VirtualItem` → 需要正确子类时**直接 `inv.items.Add(vi)`**
22b. **"磁盘资源名"≠"运行时数据库键"**：按 `er2items.manifest` 文件名当 item_id 去查会大量返回 null（`Carcano`/`Syringe`/`ArisakaT38` 都查不到，`bar_1918`/`bandages` 才查得到）。**要列游戏内容就枚举运行时库**：`ItemsDatabase.GetAllItemsOfType<PropData>((PropData.PropType)t)`（`items=6/weapons=7/ammo=8/attachment=9`），取 `PropData.prefab_name` 作键。返回类型是 **`Il2CppArrayBase<T>`** 不是 `List<T>`。服装不是 ItemObject（是 `Loadout` 字段）。详见 guide 陷阱 65
22b. **`InstantiatePrefab()` 在 `VirtualItem` 上，不在 `ItemObject` 上**：世界实体生成链 = `GetItemObject(id).ToVirtualItem().InstantiatePrefab()`；`ItemObject` 只有 `ToVirtualItem()`。**另：`Soldier` 没有 `inventory` 字段，它继承自 `Creature`**——`ilspycmd -t Soldier` 查不到继承成员，别误判"不存在"（先确认继承链）
22c. **"松手即投放"判据必须 `leftUp` 优先**：松手那帧 `GetMouseButton(0)` 已 false，`if (!leftHeld && !leftUp) return;` 会吞掉唯一一次投放（UniGen v2.0.0 自查）。面板点条目起步的拖放还要先吞那次松手（`ignoreUntilRelease`，Placer 1.0.6 教训）
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
- **已备案例外**：**无**（2026-09-18 曾把 `NativeFull` 列为例外，当天即被推翻并删除 —— 见下条）。
- **⚠️ 自锁陷阱（2026-09-18 实测，最有价值的一条）**：ModManager 的 `NativeFull` 曾被判为"文案正常、功能可用"而保留。**但它是 er2.modmanager 自己的配置项 → 会出现在 ModManager 自己的 MODS 页上**：打开它就把那一页弄坏，而关掉它**只能靠那一页** → 用户"不小心打开"后管理器直接不可用，且无法从界面自救。**教训：任何会改变"你用来操作它的那个界面"的开关，默认关是不够的 —— 它必须不存在。** 判定时多问一句：**"这个开关打开后，我还有办法关掉它吗？"** 答不上来就删。
- **判定标准（照这个判，别照"是不是开发开关"判）**：**「生 key / 空回调 / 点了没反应」= 必须从发布版删掉**；**「文案正常、功能可用、只是另一种实现」= 可保留，但必须默认关**；**「开关会影响操作它的界面」= 无条件删掉**。
- 已接入：SquadCommand、UniversalGeneration、Conquest（节名 `Diagnostics`，历史遗留）、FleshWoundsFixed（键名 `Debug Logging`，历史遗留）、**ModManager（2026-09-18）**、**HighValueTarget（2026-09-19）**
- **尚未接入（13 个）**：AIFood、CombatTweaks、ConquestRecon、HvtTestDriver、InventoryPause、LimbTweaks、MorePhysics、NoInteractionHints、ThrowableWheel、UnitCollision、UnitInfoOverlay、WeatherControl、ZoomAnywhere —— 后续改动这些 mod 时**顺手补上**

## 8. 快速验证清单

```
[ ] 日志有 "Loading [<插件名> x.y.z]"，版本与 BepInPlugin 一致
[ ] 无 "Skipping type" / "Ambiguous" / "Error loading" / "TypeLoadException"
[ ] 有本 mod 的功能触发日志
[ ] 源码/部署 DLL sha256 一致
[ ] 发布包 zip 已更新且版本号三处一致
```

## 9. 历史教训（避免重走弯路）

- **已删除并终止的项目**：`Conquest` 征服模式（2026-09-19 终止：战略层复杂度失控、战斗桥接未实测；**源码已删，快照在 `research_out/conquest_salvage/`**，已验证 UI 基建由 Endless 回收，见台账 §2.18）、`MorePhysics` 完整物理化（19 版迭代后废弃；**2026-09-13 以 v0.1.48 复活**——源码从 v0.1.47 反编译重建，单位碰撞模块对齐轻量版，见台账 §2.17）、`DirectControl` 单位接管、`SuperSoldiers` 精英单位、`HealthBars` 血条、`BattlefieldHud` 命中标记 —— **复盘全部保留在 `ER2_mod_经验.md`**，再碰同类需求先读。
- **BattleJournal（勋章/战报 mod）已放弃**：做完全流程后被用户发现 Nexus 有平替 → **提新 mod 方向前先确认生态里没有现成方案**（先搜 Nexus/问用户），别再主动提"勋章/战报/生涯统计"方向。
- **需求理解偏差的代价**：`DirectControl` 在错误理解（"接管单单位" vs 用户要的"框选多单位 RTS 指挥"）上做了 3 个版本才对齐 → **指挥/控制类需求先问清是「接管单个」还是「RTS 框选指挥」**（两者技术跨度天差地别）。
- **功能减法比加法更难也更重要**：Hide Anything 从"F5 热键+锁定+保存按钮"演化到"勾选制"，三个概念全被砍掉——每个存废都来自实际使用体验。
