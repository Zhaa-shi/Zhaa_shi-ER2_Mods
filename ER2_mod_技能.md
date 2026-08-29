# ER2 Mod 开发 —— 技能篇

> 用途：Easy Red 2（IL2CPP）BepInEx 插件开发的**方法论合集**——怎么打补丁、怎么定位故障、
> 怎么做反编译思路、怎么实现网格真碎裂、怎么加载资源/FX、怎么做跨 mod 联动、怎么算性能预算。
> 具体命令/路径见《ER2_mod_工具.md》，踩过的坑与既定事实见《ER2_mod_经验.md》。

## 1. Harmony 补丁写法与参数绑定

**参数按名注入**：
- Prefix/Postfix 参数名必须与目标方法 interop 参数名**完全一致**（例如 `SetBleeding(bool bleeding)`，
  参数名是 `bleeding` 不是 `value`）——名字不匹配 = 注入 null，拦截静默失效。

**重载多义性**：
- `[HarmonyPatch(typeof(X), "Method")]` 无参数类型时，遇到多重载会抛 `Ambiguous` 异常、PatchAll 中断。
- 重载必须显式 `new Type[]{...}` 或 `new Type[0]` 标注（见《ER2_mod_经验.md》陷阱 50）。

**拦截（短路）语义**：
- Prefix `return false` 跳过原方法即拦截；对 void 方法不需要 `__result`。
- static setter 可 patch：`[HarmonyPatch(typeof(X), "set_PropertyName")]` 拦属性 setter（例如拦截战役改天气）。
- 死亡/事件前置用 `__state = !IsDead` 快照，Postfix 里读 `__state` 触发 OnDeath——比轮询干净。

**实例字段**：
- 用 `__instance`（目标实例）与 `__result`（返回值）；`ConditionalWeakTable<TKey,TValue>`
  按引用相等缓存"每个实例一份状态"，实例销毁自动回收，避免 Dictionary 泄漏。

**补丁顺序（跨 mod 联动必读）**：
- 多个 mod patch 同一方法时，执行顺序由 `[HarmonyPriority(Priority.*)]` 决定。
  - 想"最先短路阻止后续所有 Prefix"用 `[HarmonyPriority(Priority.First)]`（First=4000）。
    CombatTweaks 关友军伤害就这么拦截，否则 LimbTweaks 的 HitPart Prefix 先执行会把友军命中记进伤害累计。
  - Postfix 想"最晚执行（读同帧其他 mod 写的结果）"用 `Priority.Last`——清理上下文时用 Last 保证其他 mod 后置补丁还能读到。

**协程方法不一定能 patch**：被编译成状态机的方法（如 `ShowGrenadeSelectionMenu`）原生直接构造
`_ShowGrenadeSelectionMenu_d__193`，方法体从未被调用 → 入口 patch 失效，改定时循环
（参见《ER2_mod_经验.md》陷阱 14/25）。

**先确认目标存在再 patch**：interop 里不存在的方法/生命周期（如 `SettingsGUI_V2.OnDisable`）不能 patch——
会 `Undefined target method` 导致整个插件加载失败。打 patch 前先 `ilspycmd -t <Type>` 确认方法存在
（见《ER2_mod_经验.md》陷阱 26）。

## 2. 诊断排障流程（读日志 / 加诊断日志 / 反编译定位）

**三层定位法**：任何"不生效"先从日志判断是哪一层失效——
1. 目标找不到（日志 "X target not found"）→ 反编译/对象名对不上；
2. 动作没执行（日志缺 "X hidden"/"X set to"）→ patch 没触发（查参数名/版本号/Ambiguous）；
3. 执行了但无效（日志有动作，效果没变）→ 被游戏覆盖/重置，或对象被销毁。

**日志关键字映射**：
- patch 不生效 → 查参数绑定（§1）、版本号（《ER2_mod_经验.md》§三）、Ambiguous（经验陷阱 50）。
- 无日志/加载失败 → 查 `Skipping type` / `Ambiguous` / `Error loading`。
- 功能时灵时不灵 → 查游戏重置/覆盖（如战役改天气）；**机制不确定时先加诊断日志让用户测一轮，
  用日志定位而非猜测**。

**加诊断日志的原则**：
- 三个通道各给独立诊断：事件层（如 "HitPart fired ... fromFaction=..."）、数据层（如 "kill reported ..."）、
  渲染/资源层（如 "tex=NULL"/"clip missing"）——三层日志能一轮定位问题。
- 高频日志发布前清理，限频诊断保留（见《ER2_mod_经验.md》发布约定）。
- 诊断日志节流：0.5s / 2s 节流，避免刷屏。

**部署验证闭环**：哈希校验（bin==部署）、启动日志版本串、cfg 落盘检查、用户实测日志回读。

## 3. 反编译分析流程（分析思路）

> ilspycmd 的**具体用法**见《ER2_mod_工具.md》；本篇讲"怎么分析"。

**先列类型再查目标**：`ilspycmd -l c <dll>` 拿类型清单，再 `ilspycmd -t <Type>` 精读单个类型。
**interop 方法体的本质**：interop 里方法体全是 IL2CPP 原生转发壳——**只有签名/字段/继承关系可信，
行为必须靠实测**。别看到字段就以为能写，写之前先想"游戏会不会覆盖"。

**反编译产物的语法修正**：IL2CPP 反编译产物里 `val..ctor(...)` 是非法语法，要改写为 `new Vector2(...)`
（见《ER2_mod_经验.md》陷阱 2）。

**验证构建/常量用反编译看折叠结果**：不要用字符串搜索验证构建语言（.NET 元数据字符串堆编码坑，
见《ER2_mod_经验.md》陷阱 39）——用 `ilspycmd -t ER2ModManager.Plugin` 看 `DefaultChinese` 常量或折叠后的页面标题。

**磁盘上找参数/资源的套路**：
- 无独立 AI 参数 xml——AiParams 全是 bool 开关；
- 物品定义在 er2items bundle 的 PropData；
- 22 个 `CorvoBundles\*.manifest`（0.1MB）可替代 3.7GB 全量解包查资源路径；
- mods/ 目录 = 原生 ModsLoader 的 Steam Workshop AssetBundle 加载源。

**灵感来源**：interop 只给签名，未知 API 从「同类型其它方法/字段 + 原生行为反推」入手，
必要时对已部署的第三方 plugins DLL 反编译借鉴（见 killfeed_ref 分析）。

## 4. 网格真碎裂算法（MeshCutter 全流程）

> 背景：没有能直接塞进 IL2CPP 游戏的现成插件（OpenFracture 等都要 Unity 编辑器组件），
> 核心算法是纯 C#，可在 BepInEx mod 里自实现。
> 网上可参考：OpenFracture（MIT，Slicer=平面切割 / Fragmenter=Voronoi 破碎）、unity-mesh-fracture（Voronoi + 水密碎片 + 凸包缓存）等。

**平面切割**（对网格 `M` 与切面：法线 `n`、面上一点 `p`）：
1. **顶点分类**：每顶点求带符号距离 `d = dot(n, v - p)`；`d >= 0` 为保留侧（留 1e-4 容差）。
2. **三角形裁剪**（逐三角形）：
   - 3 顶点全在保留侧 → 原样保留（含原法线/UV/绕序）；全在丢弃侧 → 丢弃；
   - 混合 → 对穿越边插值出交点 `t = dA / (dA - dB)`，位置/法线/UV 全部 `LerpUnclamped`；
     按绕序收集"保留顶点 + 交点"成 3~4 点多边形，**扇形三角化**。
3. **补洞（切面封口）**：每个被切的三角形贡献一条"交线段"（两个交点）→ 段连成闭合环 →
   环投影到切面基（`u = normalize(cross(n, up))`，`v = cross(n, u)`），绕环心按 `atan2` 排序 →
   环心 + 环点扇形三角化。
   - **法线朝向**：cap 法线 = 保留侧外部方向（保留 `d>=0` 侧时 cap 朝 `-n`），用首三点叉积核对绕序，反了就倒序。
4. **交点缓存（关键）**：相邻三角形共享同一条边 → 按边（min,max 顶点索引）缓存交点，
   保证补洞与切面边缘**逐点重合、无裂缝/T 形接缝**。
5. 收尾：`RecalculateBounds`；**不要 RecalculateNormals**（会把棱边糊成平滑着色，木片要硬棱边；
   保留源法线 + cap 平面法线即可）。

**碎片化策略**：逐次平面切割——按 2~3 个"本地轴 + 随机抖动 (±10% 偏移、±0.12 法线抖)"依次切开，
2 次切 = 4 块、3 次切 = 8 块（比 Voronoi 简单、稳、够好看）。过薄方向（size < 0.08m）不切；
碎渣保护：三角形数 < 24 或 extents < 0.03m 的碎片直接丢弃。后续可升级 Voronoi 剖分或按命中点偏移切割。

**IL2CPP / Unity API 注意事项**（实测级）：
- `mesh.isReadable` 必须先查（bundle 网格不一定可读）；
- `new GameObject(name)` 后逐个 `AddComponent`（不能带 Type[] 数组构造）；
- **MeshCollider 先 `sharedMesh = m` 再 `convex = true`**（反序运行时烘焙出问题）；
- 顶点超 65535 需 `indexFormat = UInt32`（POC 直接限制源网格 ≤ 8000 顶点规避）；
- 交点为值拷贝即可（同一公式算出位级相同结果），无需手工共享顶点；法线缺失时按三角形算平面法线兜底。

**ER2 原生碎裂资源**（er2fxs bundle，manifest 已确认存在）
| 资源名 | 用途 |
|---|---|
| `DestructionWoodTinyFX_01` / `DestructionWoodSmallFX_01` / `DestructionWoodMediumFX_01` / `DestructionWoodBigFX_01` | 木屑飞溅（按物块大小选） |
| `DestructionDustTinyFX_01` / `DestructionDustSmallFX_01` / `DestructionDustMediumFX_01` | 扬尘 |
| `StoneDestructionSmall` | 石质碎裂（后续按材质分） |

## 5. 资源与 FX 加载方法

**物品注册表读取**：`ItemsDatabase`（`Loaded`/`GetItemObject(id)`/`GetSpecificItemClass<T>(id)`/
`GetAllItemsOfType<T>(PropType)`/`GetLoadout`）——探测物品有效性用 `ItemsDatabase.Loaded` +
`GetItemObject(id)`。

**运行时物品/背包**：
- `Inventory.items`（`List<VirtualItem>`，可直接 `Add`/`RemoveAt`/按 id 数数）；
- `Inventory.AddVirtualItem(vi)` / `InventoryManager.AddItemToInventory(prefab)` 会把物品归一化成基类
  VirtualItem——需要正确子类（VirtualThrowable 等）时**直接 `inv.items.Add(vi)`**（详见《ER2_mod_经验.md》陷阱 16）。
- `VirtualItem.Create(id)` / `GetItemPrefab()`；`CountItems(id)` / `RemoveItemsOfType<T>()`。

**原生 FX 播放**（游戏原生接口，KillFeed 已验证可用）：
```
ResourcesManager.PlayImpactEffect(pos, Quaternion.identity, "DestructionWoodTinyFX_01", false, true, "er2fxs", 300, true)
```
参数含位置/旋转/特效名/两个 bool/bundle 名（`er2fxs`）/数值/最后一个 bool——按需照抄，具体资源名见 §4 表。

**自定义音效最干净入口**：`SoundManager.SpawnAndPlay(pos, clip)`。
**自定义音频管线**：自解码（WAV PCM16 手写解析 或 MP3 用 NLayer.MpegFile）→ float[] →
`AudioClip.Create(name, samples/ch, ch, rate, false)` → `SetData(...)` → 常驻 2D AudioSource → `PlayOneShot(clip, Clamp01(音量))`。
**★动态资源三件套（失败前提）**：所有运行时创建的 Texture2D / AudioClip 必须设
`hideFlags = (HideFlags)61`，否则主菜单→战斗场景切换时被 Unity 卸载销毁，`== null` 对已销毁对象返回 true，
所有空值检查静默跳过 → "日志全绿但游戏里什么都看不见听不见"（详见《ER2_mod_经验.md》陷阱 41）。

**资源清单读 manifest**：er2items 3.7GB 全量解包太重，`Easy Red 2_Data\StreamingAssets\CorvoBundles\er2items.manifest`
（0.1MB）直接列出全部资源路径（投掷物 ID 全集就这么拿的）。资源定位也可借 `PropData.assetBundleName`/`settedAssetBundleFolder`。

## 6. 跨 mod 反射联动

原则：**避免编译期依赖**（对方 mod 缺失时自动跳过），不 patch 第三方逻辑。
- 找程序集：`AppDomain.CurrentDomain.GetAssemblies()`；
- 读静态字段：`GetField("字段", Static | Public | NonPublic)`，**缓存 type/FieldInfo 防每帧反射开销**；
- 调用静态方法：缓存 MethodInfo 后 Invoke；try/catch 兜底（找不到/不兼容返回默认值 false）。
- 应用实例：CombatTweaks 的 `FriendlyBlastWindow()`/`FriendlyBlastFaction`（MorePhysics 反射读上下文）；
  ModManager 读 `IL2CPPChainloader.Plugins` 自动枚举插件。

**Hide Everything 跨 mod UI 隐藏契约（mod 作者必读）**——两种接入方式：
- **方式 A（推荐，查询契约，无编译期依赖）**：显示 UI 前反射调用
  `IsHudHidden(id, displayName)`——返回 true = 主开关关（F5 隐藏中）且该 mod 独立开关开 → 隐藏你的 UI。
  共享辅助 `Shared/NoHintsHudLink.cs`（csproj 加 `<Compile Include="..\Shared\NoHintsHudLink.cs" Link=... />`）
  或自写等价反射缓存（找程序集名含 `NoInteractionHints` → 类型 `ER2NoInteractionHints.HudCompat` →
  方法 `IsHudHidden(string,string)`，缓存 MethodInfo，try/catch 兜底返回 false）。**首次查询自动注册**
  该 id 的配置项（默认开）；提示型 UI 显示前查一次，持久型每帧开头查一次。
- **方式 B（零代码，惯例字段自动发现）**：插件程序集定义 `public/internal static bool`
  名为 `HudEnabled` / `HudVisible` / `ShowHud` 的字段（true=显示 UI），Hide Everything 自动扫描
  （仅扫带 BepInPlugin 属性的程序集）、生成配置项（默认关）、隐藏时写 false 恢复写 true。
- 契约逻辑测试宿主：`tmp_hudcompat_test/`（编译生产源码 HudCompat.cs + UiGroups.cs + NoHintsHudLink.cs，
  `dotnet build` + 运行 `ER2HudCompatTestApp.exe`）——改契约先跑测试再部署。

## 7. 性能预算方法

**物理预算**（详见《ER2_mod_经验.md》物理篇；给未来物理/尸体 mod 的方法论）：
1. **尸体物理预算（性价比最高）**：尸体=完整布娃娃（每具 ~15-30 个 Rigidbody+关节），大场面尸体堆积是
   最大物理负载增量。做法：超过 N 具时最远/最旧尸体调 `RagdollManager.SetCollidersLOD(true)` 或
   `ProcessPausePhysic()`；或按玩家距离分级（近=完整、中=碰撞体 LOD、远=暂停物理）。风险低，复用原生 API，不碰死亡流程。
2. **mod 自身查询纪律**：不要每帧大半径 `Physics.OverlapSphere` / `FindObjectsOfType`——
   用静态注册表（`Creature.allCreatures`/`Vehicle.allVehicles`/`ItemObject.spawnedItems`）+ 事件驱动。
3. **避免滥用 trigger 碰撞体**：`queriesHitTriggers=true` 使每条射线都要遍历 trigger，大场景大量 trigger 放大所有射线成本。
4. **投射物数量**：虽然弹道是射线（不占物理），但每帧多条长距离射线+命中处理仍吃 CPU——考虑命中检测距离裁剪。
   **不建议动**：Fixed Timestep 50Hz / Solver 6-1 / 重力 / 碰撞矩阵——改坏全游戏手感/弹道/联网同步。

**碎片预算**（网格真碎裂）：
- 单次切割 O(三角形数)：2k 三角形 < 1ms（命中瞬间执行可接受）；
- 上限：源网格 ≤ 8000 顶点 / 16000 三角形；物体 extents ≤ 1.5m；爆炸碎裂按半径 × 1.6（封顶 12m）、
  单次爆炸最多 8 个物体、距玩家 > 90m 跳过；
- 碎片存活 4s 后销毁；碎片带 ttl 组件自清理，掉出世界（y < -30）立即销毁；正式版再做碎片池。

**时间预算 / 节流通用纪律**：
- 局内暂停（timeScale=0）时 `Time.time` 冻结，所有节流/冷却/看门狗会停摆——必须用 `Time.unscaledTime`
  做节流时钟（详见《ER2_mod_经验.md》陷阱 36）。
- `OnGUI` 每帧多次事件（Layout/Repaint）→ 动画/衰减必须用**绝对时间戳差值**的纯时间函数，
  固定步长会随事件数加速。
