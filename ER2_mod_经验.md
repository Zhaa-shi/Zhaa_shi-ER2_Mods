# ER2 Mod 开发 —— 经验篇

> 用途：Easy Red 2（IL2CPP）BepInEx 插件的**踩坑全集 + 已验证的游戏机制事实 + 版本号/发布/语言规则**。
> 全部条目均为本项目实测踩过/反编译确认过。命令与脚本见《ER2_mod_工具.md》，方法论见《ER2_mod_技能.md》。

---

## 一、致命陷阱全集（现象 → 原因 → 解法）

> 全部为实测/反编译确认。编号是唯一 ID，供各文档交叉引用；排序大致按"血量为先、IL2CPP/UI 居中、构建发布收尾"。

### 血量系统陷阱（1-3, 5-8, 10）

**陷阱 1｜写血量必须整体赋值**
- 现象：`life_total.Value = x` 编译报 **CS1612**。
- 原因：getter 返回的是值类型副本，直接对它赋值无意义。
- 解法：`soldier.life_total = new ProtectedInt(hp);`（血量字段 `Creature.life_total`，类型 `ProtectedInt` 加密防篡改包装；读用 `soldier.life_total.Value`）。

**陷阱 2｜血量只能渐进小步写**
- 现象：单帧大幅扣血（如 100→35 直接写字段）= 跳变 → 游戏判死/覆盖归零。
- 原因：游戏检测到外部修改，按"受损/死亡"处理。
- 解法：**每帧 ±1 内安全**，用 float 累积器模式逐帧逼近；绷带补血跳变安全（游戏认可"治疗"）。

**陷阱 3｜投降单位血量被游戏接管**
- 现象：对投降单位外部扣血无效。
- 原因：投降单位血量走游戏自己的流程。
- 解法：用计时器调 `Kill()` 等效"流血而死"。

**陷阱 5｜血量归零判死在 Damage 内部**
- 现象：拦 `Kill` 转向无效。
- 原因：系统在 `Creature.Damage` 内部判定死亡（不是 Kill 调用）；主动 `SetIncapacitated(true)`（血量>0）后续血量归零也会判死。
- 解法：判断生死走 `Creature.Damage` 路径，或读 `IsDead`；不要依赖拦 Kill。

**陷阱 6｜游戏内部血量状态会覆盖外部写入**
- 现象：外部写的 life_total 被覆盖回"游戏计算值"。
- 原因：伤害记录/同步路径在游戏内维护血量状态。
- 解法：**不要与游戏 Damage 路径打架**，优先走原生路径（`Damage()`/`SetBleeding`/`RecoverLife`），或渐进写。

**陷阱 7｜活体断肢不要调 DetachLimb**
- 现象：活体调 `Soldier.DetachLimb(...)` 后必死。
- 原因：原生 `DetachLimb` 自带失血 DoT（约 20-30/s，断肢必死机制）。
- 解法：活体断肢用**视觉隐藏**（手臂 `Soldier.leftArm/rightArm` Transform，localScale 归零 + Renderer 禁用）+ 自己控制血量；尸体/死亡断肢可以调 DetachLimb。

**陷阱 8｜原生流血特征与止血副作用**
- 现象：`isBleeding=true` 时游戏周期性调 `Damage(1.0)`（小值）；`SetBleeding(false)` 止血后游戏开始自然回血。
- 原因：`dam ≤ 5` 可作为**原生流血特征**区分敌人伤害；止血触发自然回血。
- 解法：断肢单位需拦截 `SetBleeding(false)`（Prefix return false）防止回血；`dam ≤ 5` 且 isBleeding → 可吞掉自行控制扣血（渐进）。

### IL2CPP 陷阱（4, 11, 12, 14, 13, 15, 22, 25, 26）

**陷阱 4｜IL2CPP 类型转换必须 TryCast<T>()**
- 现象：C# `as` 按 CLR 类型检查恒失败，interop 返回的基类包装转不成子类。
- 原因：interop 返回的是 IL2CPP 基类包装（如 `Get<VirtualItem>`）。
- 解法：一律 `obj.TryCast<T>()` 按 IL2CPP 类型转换。

**陷阱 11｜IL2CPP 动态资源不留藏 → 场景切换被销毁**
- 现象：日志全绿（加载成功、patch 生效）但游戏里什么都看不见听不见。
- 原因：Plugin.Load（主菜单场景）创建的 Texture2D/AudioClip 无保护，进战斗场景被 Unity 卸载销毁；
  `UnityEngine.Object == null` 对已销毁对象返回 true → 所有空值检查静默跳过绘制/播放。
- 解法：**运行时创建的 Texture2D/AudioClip 必须 `hideFlags = (HideFlags)61`**（或 DontDestroyOnLoad）；
  排查"代码执行了但看不见"先怀疑对象被销毁（日志打印对象尺寸/格式验证存活）。

**陷阱 12｜IL2CPP 裁剪 + GUIStyle 默认黑色**
- 现象：`new GUIStyle()` 的 `normal.textColor` 是黑色（GUI.color 是乘法 tint，黑×红=黑）；`new GUIStyle(其他style)` 拷贝构造报 `Method unstripping failed`。
- 原因：默认 textColor 黑 + IL2CPP 裁剪掉拷贝构造。
- 解法：显式 `normal.textColor = Color.white`；只能无参构造 GUIStyle。

**陷阱 13｜static setter patch 可拦属性 setter**
- 用例：`[HarmonyPatch(typeof(X), "set_PropertyName")]` 拦属性 setter（拦截战役改天气）。

**陷阱 14｜协程方法 patch 可能不触发**
- 现象：`ShowGrenadeSelectionMenu` 方法从未被调用，入口 Prefix 失效。
- 原因：原生直接构造 `_ShowGrenadeSelectionMenu_d__193` 协程状态机，方法体不走。
- 解法：改用**定时循环**（`PlayerController.Update` Postfix + 节流），保证快照前物品就位。

**陷阱 15｜不要在 Prefix 里递归重调原方法**
- 现象：Throw 重定向方案（Prefix 里 `__instance.Throw(proper)` + 递归守卫）在 IL2CPP 下重入参数异常，还干扰 AI 原生投掷。
- 原因：IL2CPP 重入后仍收到基类，参数异常。
- 解法：放弃该方案；`return false` 短路 + 在别处用自己的逻辑完成，别递归原方法。

**陷阱 22｜UnityAction 委托桥接在 IL2CPP 下不可靠**
- 现象：`UnityAction<bool>` 回调收 False 恒变 True；`UnityAction<float>` 回调参数是垃圾 float（E+35）。
- 原因：uGUI 事件桥接在 IL2CPP 下不可靠。
- 解法：交互控件不绑事件，改为**每帧轮询 value/isOn 对比 lastValue**。

**陷阱 25｜协程入口方法 patch 不触发（与 14 同源）**
- 同 陷阱 14：入口 patch 失效走定时循环。

**陷阱 26｜interop 不存在的 Unity 生命周期方法不能 patch**
- 现象：`SettingsGUI_V2.OnDisable` 未在 interop 生成 → Harmony `Undefined target method` → 整个插件加载失败。
- 原因：interop 里不存在的方法不能作为 patch 目标。
- 解法：先 `ilspycmd -t <Type>` 确认方法存在再 patch；关闭检测改用帧看门狗（Update 轮询 + 无操作 0.8s 落盘）。

**陷阱 50｜Ambiguous 重载多义性**
- 现象：`[HarmonyPatch(typeof(X), "Method")]` 无参数类型时遇到多重载 → 抛 `Ambiguous match` 异常、PatchAll 中断。
- 解法：重载必须显式 `new Type[]{...}` 或 `new Type[0]`（参数绑定详见《ER2_mod_技能.md》§1）。

### 物品/背包陷阱（16, 17, 20, 21）

**陷阱 16｜背包添加物品会降级成基类**
- 现象：`Inventory.AddVirtualItem(vi)` / `InventoryManager.AddItemToInventory(prefab)` 后物品变基类
  VirtualItem，原生转盘按 `VirtualThrowable` 类型过滤会无视（实测 `FindItemWithID` 也是基类）。
- 原因：原生 API 归一化成基类 VirtualItem。
- 解法：需要正确子类（VirtualGrenade 等）时**直接 `inv.items.Add(vi)`**
  （`Inventory.items` 是 `List<VirtualItem>`，可 Add/RemoveAt/按 id 数数）。

**陷阱 17｜原生回调拒绝外部替换的 UI 数据**
- 现象：`CircularMenu2.ShowCircle` 的数据替换后显示正常，但原生选择回调匹配不到条目（选中无任何反应、`Soldier.Throw` 都不被调用）。
- 原因：原生选择回调按它自己的条目匹配，外部替换数据匹配不上。
- 解法：**能走原生管线就走原生**（补货让原生协程自己构建转盘，选择/投掷全通）。

**陷阱 20｜BepInEx 自动落盘防不胜防**
- 现象：想在"按下才保存"却挡不住 BepInEx 持续写盘。
- 原因：`ConfigFile.SaveOnConfigSet=true`（默认）运行中自动写盘 + **游戏退出时 BepInEx 自动保存全部 cfg**。
- 解法：**暂存机制**——控件改动只进 staged 字典（不碰 `BoxedValue`），保存按钮才 `BoxedValue=` + `cfg.Save()`；
  `cfg.SaveOnConfigSet=false` 防运行中自动落盘；重置同样只进暂存；页面重开从暂存区读值。

**陷阱 21｜KeyCode 枚举配置没有 AcceptableValueList**
- 现象：cfg 里 "Acceptable values: None, Backspace..." 注释是 BepInEx 自动生成的，`entry.Description.AcceptableValues` 为 null。
- 原因：KeyCode 枚举无自定义可接受值；且 uGUI Dropdown 在 IL2CPP 下值变化检测不可靠。
- 解法：热键改键用**点击按钮 + `Input.GetKeyDown` 捕获**（候选键枚举 F 键/字母/数字/方向键/鼠标，Esc 取消）。

### UI/界面陷阱（见 §二 及 18, 19, 23, 24）

**陷阱 18｜ModManager 中文词典严禁重复键**
- 现象：`Dictionary<string,string>(Comparer.OrdinalIgnoreCase)` 加重复键（enabled vs Enabled、跨 mod 共用键）→ 静态构造抛异常 → 整个 MODS 页空白只剩页脚（**踩过两次**）。
- 解法：加键后必须跑重复键校验脚本（`Check_mm_dup.ps1`，见《ER2_mod_工具.md》）。

**陷阱 19｜原生 Hint 在设置界面打开时延迟显示**
- 现象：`Corvostudio.UI.Hint.Display` 在设置 GUI 打开时不渲染，关闭设置才在局内弹出。
- 解法：即时反馈用控件按钮文字闪烁（FlashItem：暂存 Text + 原文 + 截止时间，轮询恢复）。

**陷阱 23｜uGUI 文字溢出被滚动区裁切**
- 现象：`horizontalOverflow=Overflow` 的超长文字被滚动 Mask 裁掉（长 mod 名显示不全）。
- 解法：长名截断+省略号，完整名展开时另起小字行显示；**描述行高度必须保守估算**（420px/行、8px/字、+1 行余量），否则文字纵向溢出压到下一行按钮。

**陷阱 24｜DLL 文件名 ≠ 插件名**
- 现象：`ER2_RecoilOverhaul.dll` 里实际是 "Universal Recoil Control (Dynamic Curve)"。
- 原因：插件按 `[BepInPlugin(guid, name, version)]` 元数据识别，与文件名无关。
- 解法：查插件用日志 Loading 行或二进制搜 GUID，别按文件名猜。

### 构建 / 反编译 / 发布陷阱（2, 9, 27-31, 34, 36-49）

**陷阱 2｜反编译产物语法非法**
- 现象：`val..ctor(...)` 报错。
- 解法：改写为 `new Vector2(...)` 等合法 C# 构造。

**陷阱 9｜FindObjectsOfType 掉帧**
- 现象：0.5s 全场景扫描掉 7 帧。
- 解法：避免轮询扫描，用事件驱动/静态引用；需要枚举单位用 `Creature.allCreatures`/`aliveCreatures`。

**陷阱 27｜别用 PowerShell 重写含中文的源码文件**
- 现象：`Get-Content`/`Set-Content` 按系统 ANSI（GBK）编解码 UTF-8 文件 → 中文注释乱码（mojibake），
  可能破坏行结构导致 CS1513。
- 解法：源码文件一律用 write/edit 工具（UTF-8 安全）；PowerShell 只用于复制/编译/日志。

**陷阱 28｜地图/武器数据在 AssetBundle**
- 事实：地图/武器数据在 AssetBundle（er2items 3.7-3.9GB / er2bundle 4.5GB），用 UnityPy 读 typetree；
  **先读 manifest（0.1MB）**（见《ER2_mod_技能.md》§5）。

**陷阱 29｜资源清单读 manifest**
- 事实：er2items 3.7GB 全量 UnityPy 太重——`Easy Red 2_Data\StreamingAssets\CorvoBundles\er2items.manifest`
  （0.1MB）直接列出全部资源路径（投掷物 ID 全集就这么拿的）。

**陷阱 30｜battle 场景引用**
- 现象：主菜单也有 BattleManager。
- 解法：等场景实例（如 `DayNightCycle.instance`）存在时再操作。

**陷阱 31｜DLL 被锁**
- 现象：游戏运行时 plugins DLL 被占用，部署失败。
- 解法：用轮询重试脚本（每 10 秒，10 分钟上限），build.ps1 内置。

**陷阱 34｜Copy-Item -Recurse 嵌套坑**
- 现象：目标目录已存在时会把源文件夹复制成其子目录（plugins 里出现 `X\X\` 嵌套、DLL 重复加载风险）。
- 解法：先清理目标，或用"目标=父目录"写法。

**陷阱 36｜局内暂停时 Time.time 冻结**
- 现象：timeScale=0 时所有"节流/冷却/看门狗"在局内设置界面（暂停态）永久停摆；症状是"主菜单正常、
  局内完全不生效"，日志表现为诊断记录只出现一次。
- 解法：用 `Time.unscaledTime` 做节流时钟（滚动自愈、自动保存看门狗、查找冷却、日志节流全部踩过）。

**陷阱 37｜设置滚动范围由 ScrollRect.content 决定，不一定是 contentPage**
- 现象：设置页面高度设错，内容被裁切/不对齐。
- 原因：局内设置层级为 `Content(我们的页) → Content(父级) → Viewport(Mask) → ScrollView(ScrollRect)`。
- 解法：向上找 ScrollRect 组件再取其 content 设置高度；垂直拉伸锚点的 content 要改顶部锚定 +
  `ForceRebuildLayoutImmediate` 才生效；离开页面时还原锚点防影响原生页。

**陷阱 39｜别用字符串搜索验证构建语言**
- 现象：.NET 元数据字符串堆编码与直觉不同，ASCII/UTF-16 盲搜误报。
- 解法：用 `ilspycmd -t ER2ModManager.Plugin` 看 `DefaultChinese = true/false`，
  或看折叠后的页面标题字符串（`DefaultChinese` 是编译期常量，三元表达式被 const 折叠成单一字符串）。

**陷阱 40｜无条件编译的共享词典**
- 现象：`ChineseLabels` 词典（含全部中文）在 EN/CN 两种构建里都存在，别被"中文串存在"误导。
- 解法：判断界面语言只看 `DefaultChinese`，不要用字符串存在性判断构建版本。

**陷阱 41｜运行时资源的 hideFlags（与陷阱 11 同源）**
- 见陷阱 11。

**陷阱 42｜归属判定用 Creature.IsPlayer() 引用级比较**
- 事实：faction 字符串（`UnitedStates_allies`/`Germany_axis`，26 国）匹配能用但拿不到攻击者名字/武器；
  KillFeed+ 的 `lastHit` 归属表（受害者 InstanceID → HitRecord{attackerName/attackerFaction/attackerIsPlayer/
  weaponName/cat/time}，25s 窗口 + 8s 去重 + 400 条 Prune）是信息流类 mod 的标准架构。

**陷阱 43｜死亡事件用 Soldier.Kill/KillSynched Prefix 前置快照**
- 用例：`__state = !IsDead`，Postfix 里 `FeedManager.OnDeath`——比 Damage Postfix 轮询干净；
  Kill 只在真死亡时调用，覆盖击倒后死亡/流血死/投降死；`SetIncapacitated` 是击倒/救起事件源。

**陷阱 44｜子弹命中数据源**
- 事实：`Bullet.BulletDamage(ImpactSpecifier, Soldier shooter)`（带 shooter 引用）和
  `BodyPart.TryDamageWithExplosion(__instance, Soldier responsible, HitType)`（爆炸带 responsible），
  比 `BodyPart.HitPart(fromFaction)` 信息全（能拿攻击者身份/武器）；载具走 `VehicleDamagablePart.TryPenetrateArmor(shooter)` 系列。

**陷阱 45｜IMGUI 纯黑底可以画**
- 事实：`GUI.color = new Color(0,0,0,0.45f*alpha)` + 1x1 白贴图 DrawTexture 是半透明黑底做法
  （旧结论"纯黑 tint 被剔除"指全黑描边，半透明黑底实测可用）；文字发光用 12 偏移重复 GUI.Label，
  宽度用 `style.CalcSize(GUIContent)` 精确测量。IMGUI 程序贴图优先用 palette28 调色板 PNG（SetPixels32/SetPixels GPU 上传不可靠）。

**陷阱 46｜PS 5.1 十六进制字面量坑**
- 现象：`0xFFFFFFFF` 解析为 Int32 -1、`0xEDB88320` 为负 Int32 → CRC/掩码全错且不报错。
- 解法：必须用十进制（4294967295/3988292384）。

**陷阱 47｜PS 5.1 数组字面量解析坑**
- 现象：脚本块里 `@(a, b, c, d * $var)` 末元素 `*` 表达式不带括号 → 整个数组静默变空。
- 解法：必须 `@(a, b, c, (d * $var))` 逐元素加括号。

**陷阱 48｜PS 5.1 中文注释坑**
- 现象：无 BOM UTF-8 脚本被按 GBK 误读，含 `——` 等字符的注释行破坏解析（报错指向下一行 `Unexpected token`）。
- 解法：脚本保持纯 ASCII 注释，或用 write 工具 + 手动加 BOM（edit 工具重写会丢 BOM）。

**陷阱 49｜PowerShell byte 位移截断**
- 现象：`[byte]172 -shl 8` 按 byte 类型截断为 0 → 读二进制字段出错。
- 解法：先 `[int]` 转换再移位（validate_assets.ps1 已修）。

> （原文档另有若干重复编号/历史铺垫，已并入以上同源条目；全部陷阱以有"现象→原因→解法"结构的为准）

---

## 二、已验证的游戏机制事实

> 体系：`ER2_scene_objects_classification.md`（场景物体分类）与 `ER2_physics_system.md`（物理系统）
> 为解包实证的权威来源，以下为两篇的结论精华（避免三套机制描述）。

### 物理系统（解包实证）

**引擎物理配置（globalgamemanagers 实证）**：Unity 标准 3D 物理，无魔改参数。
| 配置 | 值 |
|---|---|
| 重力 | (0, **-9.81**, 0) |
| Fixed Timestep | **0.02（50Hz）**；MaximumAllowedTimestep 0.1 |
| Solver 迭代 | Default 6 / Velocity 1 |
| QueryHitTriggers | **true**（所有射线/Overlap 会命中 trigger） |

**图层与碰撞矩阵（16 物理层 + 3 Tag：`IL_units`/`IL_tanks`/`IL_all`）**：
0 Default / 1 TransparentFX / 2 Ignore Raycast / 4 Water / 5 UI / 6 Interaction /
7 WheelOnly / 8 bullet / 9 onlybullet / 10 item / 11 Wheel / 12 Vehicle / 13 Terrain /
14 InvisibleWall / 15 InvisibleTrepassableWall。
- **bullet(8)** 不碰 InvisibleWall/InvisibleTrepassableWall/WheelOnly/Wheel/UI（**子弹穿隐形墙**）；
- **item(10)** 不碰 InvisibleWall（**物品穿墙掉出地图**）；
- **Vehicle(12)** 碰撞 InvisibleWall 系（载具被隐形墙拦=地图边界）但 **不挡士兵(Default)**（士兵可穿）；
- **InvisibleTrepassableWall(15)** 反向：拦士兵不拦载具。

**物理载体分布**：士兵 `CharacterController`（无 Rigidbody）；AI 士兵 `NavMeshAgent`（导航非物理力）；
载具 `Rigidbody`（FixedUpdate 驱动，Wheel/WheelOnly 层）；布娃娃 `RagdollManager`
（Rigidbody+关节，原生优化 `SetCollidersLOD(bool)` + `ProcessPausePhysic()`）；
枪弹**自定义射线**（`BulletInstance.OnHit` + 静态 `Bullet.BulletDamage(...)` 结算，**小口径弹道不占物理引擎**）；
爆炸/炮击 `Explosion.CreateExplosion` 一次性 OverlapSphere + 士兵受击走 BodyPart/HitPart；
场景物 `MapPropReference` 按距离管理碰撞体与渲染（`UpdateColliders(RenderMode)` + `COLL_DISTANCE_CAM/OBJ/AI` + `LOD_DISTANCE_MULT` + `spawnDistance`）——原生"物理 LOD"。

### 场景物体分类体系（解包实证）

**资源包层分类（manifest 目录结构，bundle 内文件夹 = 第一层分类）**：
- `er2bundle`：`Props and Buildings/`(1337) + `Utility/`(6) + `VFXs/`(3)；
- `er2items`：`Uniforms/`(760，26 国子目录) + `Items/`(338) + `grenades/`(48，ThrowableWheel 全集来源)；
- `er2vehicles`：`Vehicles/`(380)；`er2units`：`Factions/`(19)；`er2battles`：`Battles/`(5)；
- `er2maps_placedprops`：`Scenes/`(20，每地图"已放置物"数据)。

**官方分类枚举（PropData，权威）**：
```csharp
enum PropType { environment=0, terrain=1, streets=2, buildings=3, props=4, vehicles=5,
    items=6, weapons=7, ammo=8, attachment=9, roadMaterial=10, vfx=11, deprecated=99 }  // 13 类
enum Category { Unknown, Headgear, Vest, Uniform, Wheeled, Tank, Plane,
    AutoTransport, StaticMg, StaticGun, Special, Unlisted }  // 12 类
```

**地图放置物运行时三层结构**：`MapProp`(作者数据: prop_id/position/rotation/scale/destructiblePhases sbyte[])
→ `MapPropReference`(运行时实例: connected_map_prop→MapProp / connected_prop_data→PropData /
prop_instance GameObject / destructionManager(DestructibleManager) / instance_life float[] /
override_destruction sbyte[] / colliders / doorStatus bool[] / SetSynchedLife/IsDestroyedForever/RefreshActualLife)
→ `PropData`(注册表分类)。

**物品体系**：`ItemsDatabase`（`Loaded`/`GetItemObject(id)`/`GetSpecificItemClass<T>`/
`GetAllItemsOfType<T>(PropType)`——PropType 是 items/weapons/ammo/attachment 的过滤键）。运行时物品分两套：
- `ItemObject`(世界物基类，`spawnedItems` 静态表)：`ItemObjectStackable→ItemObjectRecoverLife`(食物/药品)、
  `Weapon→HandheldItem→GenericGun`(含 Panzerfaust)、`Magazine`/`Attachment`、`ItemGrenade`、
  `DroppableAmmoBox`/`AmmoBox`/`AmmoRefillCrate`、`ItemClothing`/`ItemHelmet`、
  `ItemObjectBandages`/`ItemObjectSyringe`/`ItemObjectToolbox`、`ItemObjectDeployableVehicle`、
  `TurretWeaponBullet`/`VirtualAmmo`；
- `VirtualItem`(背包/虚拟)：`VirtualWeapon`/`VirtualAmmo`/`VirtualShellAmmo`/`VirtualMagazineItem`、
  `VirtualThrowable→VirtualGrenade/VirtualSmokeGrenade/VirtualATGrenade`。

**生物与载具**：士兵 `Soldier : Creature`（`Creature.allCreatures/aliveCreatures`，faction 字符串 26 国 × 阵营，`IsPlayer()` 引用级判定）；
载具 `Vehicle`→`VehicleWithWheels→VehicleTank`/`MovableVehicle→VehiclePlane`（`IsStatic/IsArtillery/IsAA/IsTransportVehicle/IsSPA`），
炮塔 `Turret→TurretGun→TurretMG`（固定火力点是 TurretMG），`TurretWeapon`(loadedAmmoCount/maxAmmoCount/ammos[].bullet_id)；
可破坏建筑 `DestructableBuilding`(life/Damage/DestroyBuilding/SetDestructionPhase)。

**原生可破坏结构（绝不能碰）**：`MapPropReference`（有 `DestructibleManager` + `instance_life` +
`destructiblePhases`）+ `DestructableBuilding` / `DestructibleBuildingPhased`——走游戏自己的分阶段破坏
（相位推进 + 特效 + 血量 + 联网同步），mod 覆盖 = 与原生打架（虚影/血量覆盖/同步错乱）。
mod 只做游戏没做破坏的**普通道具**（桌子、木箱、栅栏、长凳…）。

**给 mod 的运行时分类决策树**（拿一个 GameObject 归类）：
```
1. GetComponentInParent<Creature> 有 → 生物（Soldier → faction / IsPlayer）
2. GetComponentInParent<Vehicle>  有 → 载具（IsTank/IsStatic/… + Turret 体系）
3. GetComponentInParent<ItemObject>有 → 物品（子类=用途；PropData.propType=weapons/ammo/items…）
4. 命中 MapPropReference → connected_prop_data.propType/category（权威！）
   ├─ propType=buildings/props + destructionManager≠null → 原生可破坏（走原生动画）
   ├─ propType=environment/streets/roadMaterial → 地形道路（默认不可动）
   └─ propType=props（家具等）+ 无 destructionManager → "普通场景物"（名字启发式兜底）
5. 兜底：名称启发式（Road*/Trench*/Sandbags*/table/… 前缀）+ Collider/Renderer 存在性
```

### 血量 / 伤害系统（反编译确认）

| 事项 | 值/方法 |
|---|---|
| 血量字段 | `Creature.life_total`（`ProtectedInt`）；读 `.Value`，写整对象赋值（陷阱 1） |
| 失能阈值 | `Creature.INCAPACITATED_THREESHOLD`（约 <22，血低会失能/判死） |
| 部位倍率 | `BodyPart.GetBodyPartMultiplier(BodyPartType, HitType)`（static，可 patch） |
| 伤害入口 | `BodyPart.HitPart(damage, penetration, point, fromFaction, hitType)`；`Creature.Damage(float)`（判死在其内部） |
| HitType | Projectile / Explosion / Fire / Melee / VehicleCollision |
| 断肢 | `Soldier.DetachLimb(BodyPartType)`（head/chest/arm_l/arm_r/leg_l/leg_r）；活体勿调（陷阱 7） |
| 子弹数据 | `BulletData`: mmPenetration / penetrationDamage / explosionDamage / explosion_penetration / ActualSpeed |

**状态体系**：失能 `Creature.SetIncapacitated(bool)`；投降 `Soldier.Surrender()`/`UnSurrender(string)`；
出血 `Soldier.SetBleeding(bool)`/`isBleeding`（止血→自然回血）；死亡 `Kill()`/`KillSynched()`/`IsDead`；
姿态 `SetPose(SoldierPose)`（Idle/Crouch/Prone）。

**物品/食物**：`ItemObjectRecoverLife : ItemObjectStackable`（`recoverLife` 回血量、`isFood`）；
`VirtualRecoverLife : VirtualItemStackable : VirtualItem`；使用入口 `Creature.RecoverLife(VirtualRecoverLife)`。

**AI 决策**：AI 用绷带依赖 `isBleeding` 状态（直接设字段即可触发 AI 行为）；**AI 移动无法外部驱动**——
`Soldier.Move()`/`NavMeshAgent.SetDestination` 都会被游戏 AI 控制器覆盖（官方通道存在：`Lua_Soldier/Lua_Squad.moveTo`、
`findCover`、`DestinationWaypoint` 任务链、`AiParams.followCustomDirectCommands()/followCustomSquadOrders()`）。

### 深层研究索引速查（机制清单）

- **内置 Lua 任务脚本系统（MoonSharp）**：官方任务脚本在 `StreamingAssets\Missions\**\scripts\{general,mission,AI}` + `SHARED\`；
  `#include` 预处理、`er2.run()` 存活脚本（MY_PHASE 守卫）、`phase_N.lua` 阶段自动加载、`setBrain("xxx.lua")` 换 AI 大脑。
  BepInEx 注入：`new ER2ScriptRunner(code,name).RegisterNewScriptRunner().LoadAndRunScript(code)`。
- **全局 API**：`ItemsDatabase`、`SoundManager.SpawnAndPlay(pos,clip)`、`SaveDataManager`（JSON/Binary 静态序列化 +
  persistentModsPath，mod 存档首选）、`SpawnManager`、`EventManager`（string-key UnityEvent）、`AchievementManager.Unlock(id)`、
  `OutlineManager.AddGameObject(go,layer)`、`ArtilleryStrike.StartStrike()`、`VehicleDamagablePart.HitPart/TryPenetrateArmor`、
  `TurretGun.triggerPressed/ExtractOneBullet`+`TurretWeapon`、`SoldierAI.ProcessAiAccuracy`、`SoldierAI.TryStopBleeding`、
  `TemperatureEstimator.EstimateTemperature(...)`、`CensorshipManager.IsCensorshipNeeded()`、`RadioManager.IsNearRadio(pos)`、
  `MatchData.ForceNext/NextBattle`、`BattleManager.SetPhase/NextPhase/OnWin/AddBattleStat`。
- **统计/存档**：`SavableData.Statistics`（playerKills/Deaths/VehiclesDestroyed + 阵营计数器 + 成就计数）与 `GameProgresses`
  可读写，写完 `SavableData.SaveData()`；`BattleResults` 只是 3 字段 struct（无 K/D），战报走 `connectedBattle`。
- **原生已有勿重复**：DamageIndicator（受伤方向）、DeathPanel/RespawnPanel、EndBattleGUI、
  TaskIconDatabase+ObjectiveGUI、原生准星/命中标记、OutlineManager 高亮。
- **模型/视觉**：手臂骨骼 `Soldier.leftArm/rightArm`（隐藏=localScale 归零+Renderer 禁用）；
  FPS 手 `FPSGunManager.hands_mesh`（双手一体，无法单手隐藏）/`ShowRealArms(bool)`；
  玩家 `PlayerController.currentController`/`ControlledCharacter`/`SetPlayer(Soldier,float)`。
- **UI 三套**：uGUI（HUD 主体）、IMGUI（BattleManager.OnGUI）、Marker3DGUI（世界头顶标记，自带遮挡/缩放/排序）。
  原生复用 API：`Corvostudio.UI.Hint.Display`（通知弹窗）、`GuiExtension.OutlinedLabel`（描边文字）、
  `PhaseBarGUI.GetDefaultFont()`（原生字体）、`PlayerGUI.ShowObjectiveToPlayer/ShowShortText`、`BloodSplashGUI.PlayEffect()`、
  `Marker3DGUI.Draw(Il2CppObject key, int slot, Texture tex, Vector3 worldPos, float size, float alpha, Color tint, bool occlusionTest)`。
  详见《ER2_UI_design.md》。

---

## 三、版本号规则（极易踩坑）

- `[BepInPlugin(...)]` 版本必须 **`x.y.z`**（如 `2.13.93`），**禁止字母后缀**——带后缀（如 `2.13.22a`）=
  BepInEx `Skipping type ... version is invalid`，插件不加载（陷阱 2 版本类）。
- 每次改动递增版本号（z+1），zip 名一致；**启动日志里的版本字符串也要同步改**（容易漏）。

## 四、发布约定（用户已确认）

- 发布简介一律按 N 网格式：**Description / Installation instructions / Main features / Requirements / Shout outs**。
- 更新时只提**更新内容和达成效果**（简洁），完整 README 按需输出。
- 发布前清理调试/诊断日志：去掉高频诊断（插件列表 diag、页面高度、每次打开的日志、物品探测 PROBE、tick 状态行）；
  限频保留（补货日志、saved cfg、staged（仅非数值类型）、重置日志）——保留低频功能日志。
- 发布包在 `<输出目录>\<ModName>_v<版本>.zip`；zip 内 = DLL + README.txt + Nexus_description.md。
- 部署版 = 发布版（发布前把 CN/EN 切换干净）；打包后核对 zip 内 DLL 版本。
- **双语 mod（EN + CN `-Cn` 构建）**：每次 ModManager 更新必须双语；`DefaultChinese` 编译期常量（EN/CN 两构建，三句话见陷阱 39/40）。

## 五、语言一致性要求

- 配置简介来自插件代码（`entry.Description`）——**EN ModManager + CN 版插件 = 界面英文但简介中文**；
  发布截图需 ModManager 与目标 mod 同语言（切 EN 版供 N 网发布截图）。
- EN 版 GetDescription 应过滤 CJK 描述（用户装中文版插件时，EN 界面不显示中文行）——语言一致性根治。
- ModManager 中文词典 `ChineseLabels`：modNames/keys/sections/descriptions 四词典，**忽略大小写，严禁重复键**（陷阱 18）。

## 六、观察与边界（待验证/历史）

- `Time.time` 冻结/局内暂停节流 → 陷阱 36（已定案）。
- 以下来自已删除或历史 mod 的迭代结论，属"环境破坏类"经验存档（MorePhysics 19 版迭代教训）：
  场景物分类权威在 `PropData`/`MapPropReference`；原生可破坏物一律走原生；普通 props 才是 mod 空间（需处理
  LOD 兄弟节点、isStatic 级联、物品 kinematic 刚体、门 doorStatus）；威力分级用 `Explosion.CreateExplosion`
  的 `explosionMaxDamage`。**对未亲自验证的第三方/历史结论，使用前先加诊断日志实测确认。**

- **MorePhysics v0.1.7 → v0.1.8 修复清单（N 网发布版实测教训，2026-08-18）**：
  ① 禁止全场景 `FindObjectsOfType<Renderer>`（每次爆炸/近战 O(场景) 遍历）——用 `Physics.OverlapSphere`
  有界查询；② 禁止给无碰撞体物件硬加默认 `BoxCollider`（1×1×1 隐形墙）；③ `DestructableBuilding`
  子树完全排除（不扣 HP/不物理化/不爬父链加刚体）——否则整栋楼被物理化、原生破坏系统被打乱；
  ④ 尸体（`RagdollManager`，无 `Creature` 组件）需显式排除；⑤ 飞物砸人若直调 `Creature.Damage()`
  会绕过一切友军伤害保护（CombatTweaks FF off 形同虚设）——这类"旁路伤害"要么走原生路径要么不做；
  ⑥ HP 字典按实例 ID 只增不清 → 设上限清空；⑦ 排除关键词表必须对所有开启物理的路径生效（物品物理
  与场景物件物理要共用同一张表）；⑧ 近战/高频事件加冷却；⑨ 联机物理 mod 默认单机生效；⑩ 默认参数
  宁小勿大（击飞冲量 8→4）。

- **MorePhysics v0.1.9（触发器碰撞体 + 桌面落物，2026-08-18）**：
  ① 互动物（`AmmoRefillCrate : Interagible` 等）只有大范围 **trigger 碰撞体**（互动检测区）——
    子弹射线能打中（游戏射线含触发器），但 `bounds.extents > 2.5m` 的尺寸守卫把它当"巨型结构"静默拦下
    （不扣 HP、不物理化），且 trigger 不参与刚体碰撞（其他物理化物品直接穿过）——三个症状同源。
    教训：**尺寸守卫必须忽略 trigger 碰撞体**；触发器-only 物件物理化前要禁用触发器并按渲染边界补
    实心 `BoxCollider`（否则刚体加上了也直接穿地）。
  ② 桌子被打飞后桌上的收音机悬空：它是独立静态场景道具（非 ItemObject、非桌子子节点），
    打飞支撑物时用 `Physics.OverlapSphere`（顶面中心）找"顶面之上、可物理化、未动态"的小物件一并解锁
    （ItemObject 走 `EnablePhysic()`，其余走 `GetOrAddRigidbody`），靠重力自然落下。
  ③ 门控静默拦截难排查：命中但未处理的物件输出限频（5s）诊断日志（not-scene-object /
    not-physicalizable / oversized-collider / explosion-only），用日志定位而非猜。

- **MorePhysics v0.1.10（射击场靶子 + 无碰撞体桌面道具，2026-08-18）**：
  ① 训练场"箱子"实为 `TargetPractice` 靶子：游戏对靶子的命中反应（`OnHitted`）会翻倒/
    冷却/禁用碰撞体——症状"只能打一下，之后打不中"。修复：Harmony Prefix 跳过 `OnHitted`
    （mod 生效且开关开启时），HP 系统接管连打摧毁；再加保险丝——`DamageSceneObject` 门控
    通过时记录实心碰撞体，2s tick 里发现被游戏禁用就恢复（`NoteDamagedColliders`/
    `ReenableDamagedColliders`）。新配置键 `TargetPracticeOverride`（默认开；关闭保留原生
    靶子行为保打靶课程计分）。诊断：interop 存根的 `CallerCount` 元数据可反推方法被谁调用。
  ② 收音机不掉落：它是**无碰撞体**（或在 IgnoreRaycast 层）的静态道具——`OverlapSphere`
    永远找不到。修复：打飞家具类物件时加第二轮"限频（2s）全场景渲染器扫描"
    （`Object.FindObjectsOfType<Renderer>`，仅打飞桌子/架子等稀有事件触发，非每帧），
    筛选"底边贴着桌面、尺寸小"的渲染器 → `GetOrAddRigidbody` + `EnsureSolidCollider`
    （按渲染边界补实心盒）→ 自然落下。物理查询 vs 渲染扫描的分层组合方案。

- **MorePhysics v0.1.11（士兵击飞 + 摆件消失 + 木质工事，2026-08-18）**：
  ① **士兵刚体是动态的**（isKinematic=false）——"推动已物理化刚体"分支只查 kinematic
    会把玩家/小队当道具 AddForce 打上天。教训：**所有"推动动态刚体"的分支必须先过
    IsSceneObject（排除 Creature/Vehicle/RagdollManager）**，与伤害路径同一套门控。
  ② 掉落摆件误挂 PhysicsDebris 消失计时（attachDebris=true）→ 30 秒后"没了"。
    掉落 ≠ 摧毁：掉落的物件 attachDebris=false 永久留场。
  ③ 弹痕 Decal 是无碰撞体小渲染片，扫描会被误抓 → 名称硬排除。
  ④ 木质工事 BunkerWood 名字含 "bunker" 进了建筑部件类（默认关）+ explosion-only +
    超大体型三重拦截——用户想打坏它：按家具归类（bunkerwood/woodenbunker 前置判断），
    并让 IsOversizedCollider 对家具名免检（房子仍有名称关键词 + 渲染器数量守卫兜底）。
  ⑤ 落物候选判定从"顶面之上"改为"包围盒相交（Expand 0.2）"——架子里面的物件
    也会在架子被打飞时掉落，不只桌面。

- **MorePhysics v0.1.12（回退木质工事 + 落物加尺寸上限，2026-08-18）**：
  ① v0.1.11 把 "bunkerwood" 归入家具并免体型判断 → 打飞柜子时 DropItemsOnTop 把
    一整块 7.7m 的 BunkerWood_1 木棚板当"桌上摆件"物理化 → 整个木头棚子塌了、
    单位被砸飞。教训：**"打落候选"必须只针对小摆件**——尺寸上限（1.5m）+ 单位/
    载具/尸体硬排除（IsSceneObject）。木结构（bunker 等）恢复建筑部件类（默认不物理化）。
  ② 所有"解锁刚体"入口（GetOrAddRigidbody existing 分支）都要 IsSceneObject 硬排除，
    否则 DropItemsOnTop/爆炸路径可能解锁玩家/小队刚体。
  ③ "箱子是虚拟的"：日志无任何箱子命中记录——箱子要么无碰撞体要么被架子的
    碰撞体挡住（子弹永远打不到），需用户提供箱子的确切表现才能定。
  ④ 好教训：宁可放弃"打坏架子拿箱子"的需求，也不能为了它放宽结构守卫导致房子塌。
    取舍：场景结构完整 > 个别道具可破坏。

- **MorePhysics v0.1.13（幽灵道具补碰撞体，2026-08-18）**：
  用户的两个"箱子"完全打不到、摸不到——日志零命中。诊断：箱子**无碰撞体**
  （纯视觉模型），子弹射线、OverlapSphere 全碰不到。解：低频（12s）周期渲染器
  扫描 `FillGhostPropsScan`，对名字命中容器/道具关键词、渲染边界<1.5m、无任何
  碰撞体、且是场景物件（IsSceneObject）的小道具按渲染边界补 BoxCollider
  （复用 EnsureSolidCollider），补完即可被射击→HP→物理化。新配置 FillGhostProps
  （默认开）。触发不能依赖"打飞家具"（箱子旁无家具可打），必须周期扫描。
  教训：**"完全打不到"≠缺少物理化，而是缺少碰撞体**；EnsureSolidCollider
  （按渲染边界补实心盒）是通用解法。注意 C# 内字段与方法同名会 CS0102。

- **MorePhysics v0.1.14（空气墙回退，2026-08-18）**：
  ① 幽灵补盒（FillGhostProps）v0.1.13 默认开 → 巨大空气墙。日志实锤：
    ghost prop filled 'winchester_m12_lod'（展示枪）、'Barrel_Drum_lod'×2（油桶）、
    'ww2americanradios1_bc611'（电台）、'Props_BoxCarpet_LOD'（箱子地毯）——
    地图上"原本允许穿过"的装饰被全部补成实体 → 玩家被隐形墙挡住。
  ② 教训：**给无碰撞体物件补碰撞体的全场景扫描 = 把"可穿过装饰"变成"隐形墙"**，
    风险极高。此类功能必须：默认关闭 + 极窄词表 + 单渲染器边界轻量加盒
    （AddGhostBoxCollider，不合并子树、不禁用现有碰撞体）。
  ③ 部署后必须清 cfg：BepInEx 的 cfg 优先于代码默认值——v0.1.13 写入的
    FillGhostProps=true 会覆盖 v0.1.14 的默认 false；build.ps1 部署时清 cfg 解决。
  ④ 用户底线："再没有线索就回滚到 v0.1.9"——有线索（日志名单）则修，无线索则回滚。

- **MorePhysics v0.1.15（单位碰撞体独立对象，2026-08-18）**：
  ① "打桌子单位仍飞"根因：**单位碰撞体是独立对象**（挂在场景根下、父链没有
    Creature 组件）→ IsAttachedToUnit 漏检 → DropOne/GetOrAddRigidbody 把它当
    "摆件" AddComponent<Rigidbody> → 物理引擎与游戏位置同步打架 → 士兵飞。
    教训：**"父链找 Creature"不能覆盖所有单位判定**——需名字硬排除
    （collider/hitbox/capsule）+ 无渲染器对象排除（纯物理对象=单位碰撞体/互动区）
    + 新加刚体前强制 IsSceneObject。
  ② 验证手段：反编译部署 DLL（ilspycmd -t）逐行确认守卫行号，比字符串搜索可靠
    （字符串字面量编码在 #US heap，方法名在 #Strings，容易搜错编码）。
  ③ FillGhostProps 默认关后箱子回到无碰撞；用户要箱子可打 → 指导手动开启（词表
    已收紧）。安全默认与用户需求冲突时的折中：默认关 + 明确开启路径。

- **MorePhysics v0.1.16（真凶 + 诊断日志，2026-08-18）**：
  ① 真凶：名字排除加错了地方——IsPhysicalizable 有 collider/hitbox/capsule，
    但 push 分支与 existing 刚体解锁守卫走 IsSceneObject（无名字排除）。
    单位碰撞体独立对象（父链无 Creature）→ IsSceneObject 漏检 → kinematic 刚体
    被解锁 → 士兵飞。**教训：共享守卫必须收敛到同一函数**（IsSceneObject 是
    所有路径的唯一入口），排除规则加在调用方 = 必然漏。
  ② DropOne 语义修正：候选已有任何刚体（含 kinematic）→ 跳过。静态摆件不应
    有 rb；"解锁 kinematic"只应发生在被打飞的场景物件上（GetOrAddRigidbody
    主路径），绝不在落物路径。
  ③ 用户质问"你打日志了吗"——答：之前没有，一直在猜。铁律重申：**机制不确定
    时先加诊断日志让用户测一轮，用日志定位而非猜测**（AGENTS.md 工作流 4）。
    本次全路径加 MP: 前缀事件日志（扣血/加解锁刚体/落物候选/推动分支），
    定位后移除。
  ④ FillGhostProps 移除：任何"全场景给无碰撞体物件补盒"的功能都会制造障碍墙
    （空气墙、无法靠近），不可修复，只能删除。幽灵箱子问题放弃（用户选择 C
    已改为优先稳定性）。

- **MorePhysics v0.1.17（LOD 兄弟节点 = "打桌子全飞"真凶，2026-08-18）**：
  ① 诊断日志（MP: 前缀）立功：打桌子时 DropItemsOnTop 把 Table_01_LOD1/LOD2、
    Chair_02_LOD0/1/2、Radio...LOD0/1/2 全部当"摆件"加刚体——同一物体的
    LOD 级别是**兄弟节点**（bounds 几乎重合），8 个重叠刚体互相推挤爆开，
    物理冲量把附近单位弹飞。**教训：任何"附近物件"扫描必须排除同组基名的
    LOD 兄弟**（LodGroupBase 严格匹配 _LOD/.LOD+数字，防误伤 "Lodge"）。
  ② 修复：GetOrAddRigidbody 锚点爬升时**当前节点是 LOD 则无条件继续爬**
    到 LOD 组根（整组一个刚体）；DropItemsOnTop 候选排除锚点同组基节点；
    同组基只 drop 一次（droppedBases）。
  ③ 流程教训：上一轮日志过滤用 "MorePhysics" 模式把 "MP:" 前缀的诊断行全滤掉
    了，误以为日志没打——**过滤器模式必须匹配诊断前缀**。
  ④ 教训：诊断日志要保留到问题彻底闭环再删；用户"自己看"的指令 = 读日志，
    别再猜。

- **MorePhysics v0.1.18→v0.1.31（碰撞体积哲学 + 单位互碰 + 撞人伤害，2026-08-19）**：
  ① **碰撞体积该不该"自己做"？用户问出了真问题**：我们为让物理化物品贴合模型，
    一路尝试自算盒（逐 mesh AABB/convex）。用户点破：**碰撞体积原本正常、是物理化
    把原生碰撞体换坏**。定案：**尊重游戏原生碰撞体，缺了才补**。教训：功能破坏"原本
    正常"的东西时，别在"优化"上越走越远——先退回尊重原生。
  ② **convex 是贴合陷阱**：凸包必凸，桌腿间/栅栏等镂空必然填实（"镂空也能打了"）。
    设计阶段就该预判凸包对凹形/镂空物件的副作用。
  ③ **convex MeshCollider 掉地雷**：游戏多数 mesh isReadable=false → 凸包烹饪失败
    = 空碰撞 → 物理化物品全部穿地。只有 isReadable 的网格才可安全做凸包；Box 恒有效。
  ④ **士兵互穿根因 = 层碰撞矩阵**（诊断实证）：
    - 士兵 CC 在 layer=1，BodyPart 受击碰撞体在 layer=9（实心），尸体在 layer=10；
    - 游戏关闭 §CC层(1)↔受击层(9)§ 的碰撞矩阵 → 士兵互穿；子弹不受影响（raycast
      用 LayerMask，与矩阵独立）——所以"受击正常但物理互穿"。
    - 修复活体互碰：**打开 1↔9 矩阵**（复用原版受击碰撞体，零新增体积，最贴合）。
    - 尸体"推不开"根因：尸体在层10，1↔10 矩阵也被关 → CC 碰不到尸体。
      开 1↔10 + 尸体骨骼刚体强制动态（kinematic=false+useGravity）= 尸体可被推开。
    - **教训：诊断层级矩阵用 Physics.GetIgnoreLayerCollision(l1,l2)** 直接读，
      比猜测"CC 为什么不碰"可靠。单位 CC/碰撞体/尸体分层是游戏故意设计，
      别假定同层。
  ⑤ **"改了什么导致 X"要区分环境 vs 代码**：有一轮用户说活体/尸体碰撞全没了，
    最后发现是诊断模式(-2)没关导致的不执行——是测试环境，不是代码回归。
    排查回归前先确认测试条件一致（开关/模式/配置）。
  ⑥ **撞人伤害归属 vs 友军保护的边界**：v0.1.8 曾因"Creature.Damage 绕过友军
    保护"移除飞物伤人。v0.1.28 恢复：走 BodyPart.HitPart 原生路径、带速度缩放，
    **不做阵营判定——友军判定归"战场调整"mod**。教训：mod 职责边界要问用户，
    别把别的 mod 的职责塞进来（用户明确"友军保护是战场调整的事"）。
  ⑦ **SPAWNER 排除放错位置 = 回归**：name 排除若加在 IsSceneObject，会被任意
    父链节点触发（GAR 锚点爬到 TANK SPAWNER → 桌子被拦无法物理化）。
    **名字排除放 IsPhysicalizable（只判命中件本身），父链判定走 IsSceneObject
    （带单位守卫）**。
  ⑧ 发布基线确认：正式发布版是 Downloads 里带日期后缀的 zip（v0.1.7），不是
    每次 build.ps1 的产物——发布前核对"上一个发布版"是谁，别拿错对比基准。

---

## 2026-08-20/21 会话总结（数字字体 / 紫贴图 / 尸体与道具推动 / 碰撞体积）

### A. 游戏更新换字体 → mod UI 数字不显示（ModManager v1.0.74/75）
- **症状**：数字输入框数字不显示（中文标签正常）→ 定位 = 新字体无 ASCII 数字字形。
- **踩坑**：取"设置页标题字体 / 滑块模板字体"都白给——新字体全家都是
  'Easy Red 2 Font Simplified Chinese (Default)'（CJK 子集，无 ASCII 数字）。
- **关键教训**：`Text.preferredWidth` **不能**判断"字形是否真的渲染"——缺字形时
  uGUI 仍给占位宽度（误判有数字）。**唯一可靠 = 网格顶点数**：
  `TextGenerator.PopulateWithErrors("0123456789", settings, go)` 后 `vertexCount > 0`。
  更省事且 100% 可靠的兜底 = `Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf")`
  （引擎内置，ASCII 全字形，数字保证渲染）。
- **语言一致性**：ModManager 中文界面靠编译期 `DefaultChinese`（const）——EN DLL 在
  中文游戏里界面仍是英文（不像 MorePhysics 的 T() 运行时读 Language.currentlanguage）。
  判定"装的是哪个语言版"用 ilspycmd 看 const 值/看折叠字符串，别猜。

### B. 紫贴图 bug —— 第三方 Flesh Wounds（Nexus mods/59）材质销毁时序
- **症状**：战斗随机单位贴图消失变洋红，持续到本局结束；AI/玩家手/尸体都中招。
- **我们 9 个原创 mod 全量 grep 无任何材质/贴图操作** → 真凶在第三方：
  Flesh Wounds 命中士兵时**克隆士兵材质 + 新建伤口贴图替换全身 SMR**；
  玩家身体**不受 LRU 保护**（`_soldierHasPlayer` 只在玩家开枪命中别人时置位，
  玩家挨打走 AI 路径不置位）→ 贴图预算满时玩家体被淘汰 → 贴图被 `Destroy` 而
  渲染器仍引用 → 洋红。
- **修法**：从部署 DLL 反编译重建（`FleshWoundsFixed/`）：① 销毁前全面还原
  （缓存 SMR **+ 现场重扫**，FPS 手/制服 LOD 重建后不漏）② 玩家身体纳入 LRU 保护
  ③ 切场景先还原再销毁。
- **教训**：反编译重建要点——ILSpy 伪代码人工修（`(ref x)` cast 残留、`..ctor`、
  `op_Implicit` 显式调用、`ref flag`→`out flag`）；sdk 自动生成 AssemblyInfo 要
  `<GenerateAssemblyInfo>false` 关掉；`[module: RefSafetyRules]` 是编译器保留属性要去掉；
  `Object` 歧义用 `using Object = UnityEngine.Object;`。保真度核对：方法集对比（应只
  多不少）+ 关键方法体归一化 diff。

### C. "打无碰撞体道具（收音机）不物理化" —— 连环定位（MorePhysics v0.1.34→42）
- 收音机**没有碰撞体** → 子弹射线打不中 → OnHit 永不触发；且收音机是
  **TANK/MG SPAWNER 生成器的子节点**，生成器有个隐形小碰撞体（layer 0、
  名字含 "spawner" 被门控拦下）——命中的特效/弹孔其实是打在生成器上。
- **连环坑**：
  1. 句段"射线穿过包围盒"判定失败：收音机在弹道旁侧/上方，线相交永远够不到；
  2. `FindObjectsOfType<Renderer>()` 不含非激活渲染器（LOD 剔除的网格"看不见"），
     `FindObjectsOfType(Type, includeInactive)` 可行但 IL2CPP 要 Il2CppSystem.Type；
  3. **`IsEffectLike` 特效排除词表里 "clone" 匹配了场景根 `VirtualShootingRange_1(Clone)`
     → 全场景道具全被当特效挡住**——`(Clone)` 后缀是 Unity 给所有运行时实例加的，
     过宽的特效词会自锁整个功能。
- **正解**："被拦物体（生成器）的子树扫描"——`collider.transform.
  GetComponentsInChildren<Renderer>(true)` 找距命中点最近的可物理化无碰撞体小道具。
  结构关系（子节点）是铁定的，绕开全局扫描的 LOD/API 坑。

### D. 推尸体 / 推物理化道具（v0.1.32→44）
- **CC 不推动动态刚体**（Unity 语义），要 `OnControllerColliderHit` 手动加冲量；
  `ControllerColliderHit.moveDirection/velocity/normal` + `rb.GetComponentsInParent
  <RagdollManager>` + `Plugin.IsSceneObject`（游戏士兵也有动态刚体，必须排除单位）。
- **站物体上抽搐/飞走**：CC 静止时 OnControllerColliderHit 仍触发（重力接触），
  每帧冲量 → 抽搐。修：① 横向速度门槛（垂直重力不计）② 顶部接触 `normal.y>0.6`
  跳过 **③ 推击节流 0.3s ④ 推力纯横向 dir.y=0**。
- **尸体推不开"回归"**：顶部接触判定误杀平躺尸体（撞到其**上表面**法线朝上）。
  ——顶部判定**只对物品**、尸体不做。
- **AI 与物理对象无碰撞**：AI 走 NavMeshAgent 直移变换，不产生 CC 碰撞 → 对物品
  零交集。无法驱动 AI 停步 → 改**近距推开模拟**（1s tick 扫注册表，AI 1.3m 内道具
  横向拱开）。注册表（GetOrAddRigidbody 登记、上限 512、排除特效/武器系名词）比
  `FindObjectsOfType<Rigidbody>` 场景扫描便宜。

### E. 物理化碰撞体积"虚胖"导致道具飞起（v0.1.47）
- 无碰撞体道具补盒用"物理化时刻当前启用的渲染器"——远处时 LOD2/伪装网格启用，
  其包围盒比近景 LOD0 **虚胖**；且胖盒**底部穿地** → 物理插补把道具**向上弹飞**。
- 修：补盒**只用细 LOD**（识别 `_LOD1+`/`far_`/`impostor` 为粗，跳过），全粗才兜底；
  改动覆盖 `ComputeAnchorLocalBounds`（单盒）与 `RefitWithMeshBoxes`（逐 mesh 盒）。
- 教训：**"补碰撞体"必须问"用哪个渲染器求体积"**——LOD/视角会改变结果。

### F. 其他本会话要点
- 游戏崩溃 `ArgumentException: The Object you want to instantiate is null` =
  "销毁飞机时士兵正在下车 → ActivateParachute 的降落伞预制体 null"（原生 bug，
  `er2animations` 资源包未就位；2GB 核显 Vulkan 内存压力是土壤）。堆栈无 mod 帧。
- 发布前清理高频诊断日志（每秒 bullet hit / 限频 ai-push / ghost 定位日志），
  保留低频功能日志（GAR/ghost-prop hit）。用户的调参要**回写为代码默认值**。

---

## 物理化 mod 开发复盘（2026-08-21，已删除）

> 本文总结「在 ER2 上做一个『场景物品物理化 + 单位/物品/尸体互碰』mod」这一路的全部经验。
> 结论先行：**项目已终止并删除**（工作区 `Physics/`、部署 DLL、配置、发布包、build.ps1 入口全部清理干净）。
> 本节用途：下次任何人（或 AI）再碰 ER2 物理/环境破坏类功能时，直接读这里，避免重走 13 轮弯路。

### A. 需求与游戏现实的根本冲突（最重要的教训）

用户最初需求（逐字要点）：
1. 所有场景物品可物理化；
2. 有原生物理/破坏的保持原生；
3. **碰撞箱保持原生不变，不能添加新的碰撞箱**；
4. 单位/物品/尸体相互碰撞；
5. 分类 + 完全可配置；
6. 以真实为目标加细节。

**核心矛盾：需求 1（所有物品可物理化）与需求 3（不新增碰撞箱）在 ER2 上不可兼得。**

- ER2 大量道具（收音机等）**没有碰撞体**（纯渲染），子弹打不中、物理也挂不上。
- 不补碰撞体 → 这类物品永远无法物理化、支撑物被打飞后它们**悬空**（用户反复遇到的「收音机悬空」）。
- 补碰撞体（哪怕是贴合的盒）→ 早期 MorePhysics 的 `FillGhostProps` 全场景补盒造成**空气墙**（可穿过的装饰变实心），被官方 mod 定案为「不可修复，只能删除」。
- 用户中途松口「可以给没有碰撞箱的物品补，但是要贴合」，但补盒的定位（哪个节点、哪个 LOD、盒多大）在 IL2CPP 下极易错位，错位盒会把单位/物品**弹飞**、造成新 bug。

**结论：这个需求组合本身就是死路。要么放弃「不新增碰撞箱」，接受全场景补贴合盒（但空气墙风险不可控）；要么放弃「所有物品可物理化」，接受无碰撞体道具不可物理化。两条路用户都不满意时，项目就该止损。**

### B. 已验证的游戏机制事实（重做物理 mod 必读）

这些是本次 13 轮实测/反编译核实过的（补充 `ER2_physics_system.md` 之外的新事实）：

1. **角色碰撞层**：士兵 CharacterController 在 layer=1；BodyPart 受击碰撞体 layer=9（实心）；尸体骨骼 layer=`RagdollManager.ragdollizedLayer`（通常 10，与 item 同层）。
   - 游戏**故意关闭** 1↔9（士兵互穿不挡）和 1↔10（CC 碰不到尸体）。
   - 打开 `Physics.IgnoreLayerCollision(1,9,false)` 和 `(1,ragdollizedLayer,false)` → 士兵互挡、尸体可碰。实测有效（矩阵日志 `unit-collision matrix cc=1 body=9 corpse=10`）。
2. **CC 不推动动态刚体**（Unity 语义）：要 `OnControllerColliderHit` 手动 `AddForceAtPosition`，速度阈值 0.5、节流 0.3s、按 `mass` 缩放（重物难推）、items 顶部接触 `normal.y>0.6` 跳过、尸体不做顶部判定。
3. **AI 士兵走 NavMeshAgent 无 CC**：对物品零碰触，要「近距拱开」——用**物理化注册表**（`Plugin.PhysProps`，上限 512）每 tick 扫 AI 1.3m 内道具横向 `AddForce`。
4. **尸体骨骼必须强制 dynamic**（isKinematic=false+useGravity），且游戏 `ProcessPausePhysic` 会重新冻结 → 需周期性重申（v0.1.47 用 1s tick + 只在活人 3m 内才恢复碰撞体）。
5. **互动物/储物箱**（`Interagible`：WoodenStorage、GrenadeBoxGER_Refill 等）**自带 kinematic 刚体**——解锁它（isKinematic=false）会与游戏原生互动逻辑打架 → **悬空**。它们应该交给原生，不要物理化；普通静态道具（桌子：`Table_01_LOD0`，无刚体）才能安全挂动态刚体。
6. **LOD 兄弟节点是独立 GameObject**（`Table_01_LOD0/1/2`）：物理化必须爬到 LOD 组根（名称去 `_LOD\d` 后缀）只挂一个刚体，否则 8 个重叠刚体互相推挤爆开。
7. **静态批处理虚影**：给静态物体加 Rigidbody 后必须递归 `isStatic=false`。
8. **原生破坏**：`DestructableBuilding` 和 `MapPropReference.destructionManager`（`MapPropManager.MapPropReference`）都要排除——楼/可破坏物走游戏原生相位动画，mod 覆盖 = 虚影/悬空/同步错乱。
9. **幽灵道具处理**（v0.1.47 正解）：不搞全场景扫描，而是 patch `BulletInstance.RaycastAll`——射线没命中实体时，沿弹道分段找「无碰撞体、有渲染、可物理化分类、≤1.5m」的最近渲染器 → `GetOrAddRigidbody`（内部 `EnsureSolidCollider` 补贴合盒）。
10. **EnsureSolidCollider（物理化碰撞体哲学定案）**：
    - 有原生实心碰撞体 → 保留，**禁用 trigger**；
    - 无实心碰撞体 → 禁用全部，`RefitWithMeshBoxes`（逐 mesh 局部 AABB 盒，上限 12 个）兜底 `AddLocalBox`（渲染器包围盒单盒）。
    - 盒尺寸钳制 0.05–6m；`mesh.isReadable=false` 的网格不能做凸包（会穿地），补盒只用 BoxCollider。
11. **TargetPractice（射击场靶子）**：游戏 `OnHitted` 会翻倒/冷却/**禁用碰撞体**（「打一下就再也打不中」症状）。v0.1.47 用 Prefix 短路它（默认关），另加 `NoteDamagedColliders`/`ReenableDamagedColliders`（每 2s 捞回被禁用的碰撞体）。
12. **软硬物威力分级**：`Explosion.CreateExplosion` 的 `explosionMaxDamage` 是威力值，`IsExplosionOnly`（墙/混凝土/沙袋等名）只吃爆炸。

### C. 本次开发的方法论教训

1. **先部署参考实现，再谈改造**（最大教训）：
   - 用户手里有 **MorePhysics v0.1.7 + v0.1.47 发布包**，且工作区有 v0.1.31 反编译、完整踩坑史。
   - 从零重建 + 迭代 13 轮，每轮引入新 bug；用户最终说「你还是在 v0.1.47 的基础上改吧」。
   - **正确流程应该是：第一步就把 v0.1.47 DLL 部署上去让用户确认「这就是我要的」，然后只做增量差异化修改。** 从零重建一个已被验证的复杂 mod = 重复踩完它已经踩完的所有坑。
2. **「不新增碰撞箱」这条约束在动手前就该和用户摊牌成本**：
   - 它让「所有物品可物理化」在技术上不成立（无碰撞体道具）。
   - 直到第 8 轮用户才松口「可以补，但要贴合」——中间 8 轮都在一个死约束里打转。
3. **每轮改动要小而可验证**：13 轮里有多轮同时改 3-4 件事（碰撞体策略、推力、分类、诊断），出问题无法归因。
4. **用户测试环境与日志要对齐**：有一轮日志只有主菜单活动（`ccLayer=-1` 全程）却被告知「战斗里没物理化」——没确认用户是不是真的进了战斗、是不是私人房（`SingleplayerOnly` 默认挡联机）。**改配置默认值（如单机 only）前先确认用户的游玩模式。**
5. **诊断日志要一次给全**（三层定位法）：事件层（bullet hit/destroyed）、数据层（tick gate/items 计数）、状态层（ccLayer/corpse scan）。前几轮诊断日志零散，靠猜。
6. **IL2CPP 反编译重建的机械要点**：
   - `(Object)(object)x` → `x == null`；`(Component)x` → `x`；`op_Implicit(Il2CppArrayBase<T>)` → 直接 `foreach`；
   - 插值字符串 `BepInExXxxLogInterpolatedStringHandler` → 普通 `$"..."`；
   - `[module: RefSafetyRules]`、`EmbeddedAttribute`、assembly 属性全删；
   - `..ctor(...)` → `new ...`；
   - `Il2CppSystem.Collections.Generic.List<T>` 可用 `foreach`（interop 实现了枚举器）；与 C# `System.Collections.Generic` 并存时分别用全限定名。
7. **Harmony 参数按名注入**：`RaycastAll(Vector3 startPos, Vector3 dir, out RaycastHit raycastHit, float dist, float forwardShift=0f)` → Postfix 只写 `(__instance, startPos, dir, dist, __result)`，不存在的名字要事先 `ilspycmd -t` 核实。

### D. 如果再做的正确路径（prescription）

1. **部署用户手里的 v0.1.47 DLL 原样**，让用户体验确认「基线正确」。
2. 用户确认后，在 v0.1.47 **源码**（反编译重建）上做增量差异：
   - 差异只围绕用户需求：分类开关的键/默认值、`SingleplayerOnly` 行为、`TargetPracticeOverride` 默认值等配置层；
   - 物理层（GetOrAddRigidbody / EnsureSolidCollider / UnitCollision / GhostProp / CorpsePusher）**一行都别动**，它是 47 版迭代验证过的。
3. 每轮只改一个配置项 → 用户测试 → 日志对照。
4. 若需求与 v0.1.47 冲突（如「不新增碰撞体」），先把冲突摊开让用户拍板，而不是自己折中。

### E. 删除清单（已执行）

- 工作区 `Physics/`（源码 + csproj + README + Nexus_description.md）
- 部署 `Easy Red 2\BepInEx\plugins\ER2_Physics.dll`
- 配置 `Easy Red 2\BepInEx\config\er2.physics.cfg`
- 发布包 `<输出目录>\ER2_Physics_v*.zip`（v1.0.0 – v1.2.1 共 14 个）与 `ER2_Physics_CN_v1.0.0.zip`
- `scripts/build.ps1` 的 `Physics` 项（ValidateSet + switch）

参考资产仍保留：`research_out/thirdparty/ER2_MorePhysics_017/`（v0.1.7 源码）、`research_out/deployed_dump/ER2_MorePhysics/`（v0.1.31 反编译）、`research_out/morephysics_diag/`、Downloads 的 v0.1.7/v0.1.47 发布包（未动）。

---

## 2026-08-25 会话总结（HVT/Veteran HVT：M 大地图 + 头顶标记 + 发布准备）

> 高危目标 mod（v1.1.0 → v1.1.25，更名 ER2 Veteran HVT）。本轮核心：头顶标记渲染（深色/居中/遮挡半透明/提示位置）+ 大地图图标与遮挡排除（4 轮试错定案）+ Hide Anything 联动。

### A. 小地图/大地图 UI 坐标（本轮最大坑，4 轮定位）

1. **先问清"地图"是什么**：ER2 **没有常驻小地图，只有按 M 打开的大地图**（用户一句话点破，之前 3 轮全在错误前提下调试）。`MiniMapGUI.Instance.miniMap` 容器**一直 active**（600×600 UI 单位）但平时不可见——"地图矩形"永远拿得到但永远不对应屏幕。
2. **`GetWorldCorners` 在 IL2CPP interop 下传 C# 数组返回全零**（`new Vector3[4]` 实测全 0，诊断实证）→ 用 `Il2CppStructArray<Vector3>` 显式类型，或干脆 `TransformPoint(rect 四角)` 绕开。
3. **`MiniMapGUI.GetScreenCoordinatesOfCorners` 返回的不是屏幕像素**：600 容器返回 (-1253,-1636,6471×6471)（世界/UI 坐标，含祖先缩放），不能直接当屏幕矩形用。
4. **地图开关**：`MiniMapGUI.MiniMapOpened` 静态属性（interop 桩齐全），M 地图开/关直接读。
5. **"跟随游戏标记"方案（定案）**：`MiniMapGUI.unitsContainer`（Transform）下是**游戏自己每帧放置的单位标记**。做法：M 地图打开时收集 unitsContainer 下 active 子节点（localPosition + position）→ 单位用 `GetPositionInContainer(world,false)` 算容器坐标 → **匹配最近的游戏标记** → 用标记的 `position`（Overlay canvas = 屏幕像素 y-up）画自己的图标。**不猜映射公式，游戏放哪我们画哪**（此前"自校准÷容器尺寸×矩形"公式乱标，用户实测否掉）。
6. **IMGUI Y 翻转**：Unity 屏幕坐标 y 自底向上，`GUI.DrawTexture` 要自顶向下 → `sy = Screen.height - yUp`（曾漏翻导致图标上下颠倒类问题）。
7. **M 地图打开时头顶标记整体隐藏**（`MiniMapOpened` 为 true 直接 return），比"排除矩形"方案稳——看地图时本来不需要头顶标记。

### B. 头顶标记视觉（用户验收）

1. **标记背景固定最深色**（敌深红 0.7/0.02/0.02、友深蓝 0/0.12/0.7），**不随等级渐变**（用户明确要求"固定成 V 级深色"，等级靠罗马数字区分，颜色深浅不区分等级）。
2. **罗马数字贴图居中**：32×32 烘焙贴图中心 x=16；V 的顶点必须落在 x=16（原来画在 19，视觉偏右 3px）；IV 整体跨度也要以 16 对称。
3. **遮挡半透明**：相机→目标胸口 `Physics.Raycast`（命中目标自身 collider 算可见），被挡时 `GUI.color=(1,1,1,0.3)` 画标记后复位。
4. **提示不要用原生 `Hint.Display`（顶部）**：与原生"占领中"（objectiveGUI）重叠（用户实测）→ 全部改底部自绘 toast（`Screen.height-150` 起堆叠，支持 `\n` 多行）。
5. **toast 不要半透明黑底**（用户明确否掉）——纯文字即可。
6. **同帧多提示合并**：升级 + 被标记警告合并成一条多行 toast，避免"两条升级提示"。

### C. 跨 mod 联动（Hide Anything）

- 共享 `Shared/NoHintsHudLink.cs`：csproj `<Compile Include="..\Shared\NoHintsHudLink.cs" Link="NoHintsHudLink.cs" />`，反射调用 `ER2NoInteractionHints.HudCompat.IsHudHidden(id, 显示名)`，对方缺失静默 false。
- HVT 在 `OnGUI` 开头 `ER2Shared.NoHintsHudLink.IsHidden("er2.highvaluetarget", "ER2 Veteran HVT")` → true 时全部渲染（标记/图标/指示条/toast/闪烁）不画。首次查询自动在 Hide Anything 配置里建独立开关（ModManager 可逐 mod 关闭）。

### D. 更名（用户要求带"老兵"关键词）

- ER2 High-Value Target → **ER2 Veteran HVT**；DLL `ER2_HighValueTarget.dll` → `ER2_VeteranHVT.dll`（旧 DLL/旧资源目录手动删除，同 GUID 双 DLL 会加载冲突）；**GUID 保持不变**（cfg 文件名、ModManager 绑定、Harmony id 全保留）；命名空间同步改（测试驱动 using 别名一起改）；build.ps1 分支表（$dll/$pkg/$assetsDir）同步。

---

## 2026-08-25 会话补充（InventoryPause：ER2 暂停机制实测定案）

> 背包暂停 mod（v1.0.0 → v1.0.5）。核心成果：**ER2 暂停机制的完整实测图谱**——五种方案试错定案，通用性强，后续任何"暂停/冻结"需求直接引用。

### A. ER2 暂停机制（全部实测定案）

1. **原生暂停 = `Pause.SetPause(true)`**：内部 = `Time.timeScale=0`（实测 tsBefore=1.0 → tsAfter=0.0）+ 弹暂停菜单 + disableOnPause 禁用对象（含背包/输入）。**隐藏菜单后无恢复入口 = 死锁**（背包打不开、Esc 无效，用户实测）。
2. **手动 `Pause.isPaused = true`（静态字段 setter）**：**输入被禁用（原关闭键失效）但世界不冻结**（用户实测）——游戏世界暂停不读 isPaused，输入系统读。半吊子，不可用。
3. **`Time.timeScale = 0`**：世界全停（AI 的 Update 照跑但 deltaTime=0 → 移动/开火/手雷 fuse 全停；子弹停飞；物理 FixedUpdate 停）——**真暂停**。副作用：
   - **背包打开动画卡**（协程走 scaled 时间；背包面板 Animator 数量=0 排除 Animator，实锤是协程如 LerpCanvasColor/RefreshCR）。原生菜单不卡 = 菜单无动画直接显示。
   - **丢弃的武器浮空**（物理冻结，恢复后仍悬空）。
4. **`enableAiBehaviour(false)`（Lua_Soldier.getAiParams()）**：**无效**（AI 还在动，用户实测）——只验证过 `enableAiBehaviour(true)` 恢复方向有效（HVT 叛徒追猎）。

### B. 定案方案（背包暂停 v1.0.5）

- **延迟 timeScale 冻结**：打开背包 → 等 0.5s（PauseDelay 可配）打开动画完成 → `timeScale=0`。UI 不卡 + 真暂停 + 无无敌（不存在"开背包挡爆炸"滥用——威胁全停）。
- **丢弃道具自动落地**：暂停期间每 0.1s 扫描 **`ItemObject.spawnedItems`（游戏自维护的已生成道具静态列表，无需 FindObjectsOfType）**，离地 >0.8m 的射线向下找地面拉下 → 恢复后道具已在地面。
- **关键 API**：`InventoryPanel.isOpen`（静态 bool，覆盖自己背包 + 尸体"周围"背包两种打开路径）、`InventoryManager.DropItem(VirtualItem)`（丢弃入口）、`Pause.LastPauseStatusChange()`（暂停状态变化时间戳，跨 mod 防抖用）、`Pause.instance.pauseMenu`。
- 轮询用 `PlayerController.Update` Postfix + 10Hz 节流（入口 patch 不可靠——陷阱 14 同源）。
- 原生暂停菜单打开时不干预（isPaused=true 跳过轮询）。

---

## 2026-08-26 会话总结（CombatTweaks：敌人倒地被拦截 + ModManager 枚举配置）

### A. "敌人被打死不倒地、站定冻结"（CombatTweaks v1.2.2 修复）

- **现象**（玩家报告+复现）：敌人被枪打死 → 掉武器但不倒地（ragdoll 不触发），站定冻结。
- **根因**：`ExplosionFactionContextPatch`（CreateExplosion Prefix）建立"友军爆炸窗口"时，**responsible 非空就无条件用它的阵营**（不校验敌我）→ **敌人手雷/炮弹爆炸也会建立窗口**（FriendlyBlastFaction=敌人阵营，4s）。窗口期间 `FriendlyBlastRagdollPatch` 按 `faction == FriendlyBlastFaction` 拦截 Ragdolize → **敌人阵营死亡士兵倒地全被拦**。
- **教训**：任何"上下文窗口"类拦截（爆炸窗口/子弹上下文/近战上下文），**建立窗口前必须校验作用对象的身份边界**（敌我/玩家/阵营方），否则副作用会扩散到不该拦截的对象。v1.2.0 就存在，直到玩家报告才发现——上下文建立处的敌我校验是最容易被忽略的边界。
- **修复**：窗口只允许友军爆炸建立——responsible 存在时校验 `SideOf(responsible.faction) == SideOf(玩家 faction)`（新增 SideOf：faction 取 `_` 后缀，跨国家同盟同方），敌人爆炸直接 return。无归属爆炸的推断逻辑原本就要求"与玩家同阵营"。
- **附带结论**：`BodyPart.AllowDamage`/`HitPart` 的同阵营判定是**字符串全等**（`fromFaction == toFaction`）——跨国家同盟（US/UK 都是 allies）不在保护范围，属已知局限（v1.2.0 遗留，玩家未报告，未动）。

### B. ModManager 枚举配置支持（v1.1.1）

- **现象**：第三方 mod（Coax MG Hotkey）用 `UnityEngine.InputSystem.Key` 枚举做快捷键配置，ModManager 的 MODS 页**完全不显示该配置项**（cfg 正常生成）。
- **根因**：`AddSetting` 的类型分支只有 bool/float/int/string/KeyCode——**枚举类型直接跳过**（`ok` 保持 false 不渲染）。NativePage 反而有 `IsEnum` 分支（下拉）——两套渲染实现不一致。
- **修复**：①`AddSetting` 补 `t.IsEnum` 分支：键名含 key/toggle 的枚举 → "点击改键"按钮（与 KeyCode 热键统一），其他枚举 → 下拉（Enum.GetNames）；②按键捕获支持枚举写入：KeyCode 名 → 目标枚举名映射（`Alpha0-9→Digit0-9`、`Return→Enter`、`LeftControl→LeftCtrl`、`Menu→ContextMenu`、鼠标键无映射跳过）→ `Enum.Parse` 写枚举值；③NativePage 热键分支同步传 enumType；④`HotkeyValue` 支持枚举显示；⑤`ControlWatch` 加 enumType/enumNames，下拉轮询写回枚举值。
- **教训**：ModManager 两套渲染（Plugin.cs MODS 页 / NativePage）分支必须同步维护；新类型支持两边一起加。
- 用户之前承诺"ModManager 除非失效不再更新"——本次属真实兼容性缺陷，破例更新并简短道歉（发布文案已备）。

---

## 2026-08-26 会话总结（DirectControl：单位控制 mod 终止并删除）

> 单位控制 mod（`DirectControl/`，er2.directcontrol，v1.0.0 → v1.0.3）。结论先行：**项目已终止并删除**，
> 与 MorePhysics 物理化 mod 同款收尾。本节记录①核心需求澄清 ②已验证的游戏机制事实 ③为什么这条路走不通
> ④删除清单。下次任何人（或 AI）再碰"控制单位/上帝视角指挥"类需求，直接读这里，避免重走弯路。

### A. 核心需求澄清（最重要，用户明确强调）

用户最初说"像地狱之门的控制"，**当时被误解成"Gates of Hell 的直接控制（接管单个单位自己打）"**，
于是做成了"自由视角下左键点一个单位 → 接管它当第一人称打"（`PlayerController.SetPlayer` 走原生切换路径）。

用户现明确纠正：「**我说的地狱之门的控制指的是全 RTS 游戏通用的**。比如可以用鼠标**框选单位**，
指挥他们**去哪里或干什么**的**上帝视角实时指挥**mod。」

- 即用户想要的不是"占领/接管单个单位"，而是 **RTS 指挥层**：框选多个单位 + 下达移动/攻击指令 + 上帝视角。
- 之前迭代的 DirectControl 走的是"接管单单位 + 右键给上一单位下一条移动/攻击命令"，**与用户期望的方向不一致**，
  在错误的需求理解上做了 3 个版本，故终止。

### B. 已验证的游戏机制事实（做单位控制/指挥类 mod 必读）

1. **原生接管路径存在且稳定**：`PlayerController.SetPlayer(Soldier, delay)` 是游戏自己"切换小队成员/重生"
   用的同一条路径，可接管任意单位（含敌方）。`PlayerController.currentController` /
   `.ControlledCharacter` / `.ControlledVehicle` 读当前角色；`SetPlayer` 后相机/HUD/AI 交接全由游戏处理。
2. **接管后必须关掉该单位 AI**（否则 AI 抢移动，覆盖你的 WASD）：`target.aiController.enabled = false`。
   交还时恢复——组件在则 `enabled=true`，没了则 `soldier.SetAI()`。
3. **AI 移动无法外部驱动**（与陷阱一致，做"指令单位移动"的死壁）：`Soldier.Move()` /
   `NavMeshAgent.SetDestination` 都会被游戏 AI 控制器覆盖。官方通道只有 Lua 任务脚本 API：
   `Lua_Soldier/Lua_Squad.moveTo`、`findCover`、`DestinationWaypoint` 任务链、
   `AiParams.followCustomDirectCommands()/followCustomSquadOrders()`。`new Lua_Soldier(unit).moveTo(point)`
   是单单位的一次性指令通道，**不是为多单位/实时队列设计的**——这是 RTS 指挥层真正的障碍。
4. **自由视角信号多源**（进入上帝视角靠这些，缺一不可）：`CinematicCameraController.Enable/Disable`、
   `CinematicCameraGUI.cinematicCameraEnabled`（静态 bool，轮询）、`CameraInstructions.FreeCameraMovements`、
   `PlayerController.OnCharacterDeassigned`（离开角色=阵亡/切出/进自由视角）。需多信号同时检测（纯靠一个会漏）。
5. **接管时压制原生自由视角残留 UI**：`CinematicCameraController.Disable()` + `CinematicCameraGUI.instance.Close()`
   + `cinematicCameraPanel.SetActive(false)` + `UIBindingHelper.instance.ResetLocal()`（清按键提示条）。
   当心：`cinematicCameraEnabled` 静态标志可能是 false 而面板仍显示，必须**无条件 Close**，不能 gate 在该标志上。
6. **点击判定要"按下原地释放"**：`Input.GetKeyDown` 记按下位置/时间，`GetKeyUp` 时位移<14px 且<0.4s 才算一次
   点击——否则拖拽旋转视角会误触发接管。光标取屏幕正中（`Screen.width/2, Screen.height/2`），**不要用
   `pc.screenCenter`**（菜单/选兵界面里它不居中，实测偏移）。
7. **重生/选择界面 UI 优先**：`RespawnPanel.GUIenabled` + `CameraDirector.IsPointerOverRespawnUI()` 双检测，
   打开时接管/命令点击完全让位给原生 UI（否则点"选队友重生"会被我们的接管点击抢走、选成别人）。
   指针悬停任意 uGUI 元素（`EventSystem.current.IsPointerOverGameObject()`）同理不响应。
8. **战斗守卫**：场上无 `Creature.aliveCreatures`（主菜单/加载中/结算）不激活指挥官模式。

### C. 为什么"RTS 上帝视角指挥"这条路走不通（核心结论）

1. **游戏不是 RTS，AI 大脑是自主的**：ER2 是 FPS，玩家控制**一个**士兵；其余单位走独立 AI
   （`setBrain` 换大脑，任务脚本 `MY_PHASE` 守卫、`enableAiBehaviour`）。想"框选多单位 + 实时持续指令"
   需要把游戏 AI 长期置于"听玩家指令"模式，而官方只给了**单单位、一次性**的 `Lua_Soldier.moveTo/forceTarget`，
   没有多单位选择/阵型/持续指令队列的原生概念。
2. **"框选"能做，"指挥他们去干"做不到**：框选（屏幕矩形 + `Creature.aliveCreatures` 投影筛选）和上帝视角
   （自由视角信号 + 压制残留 UI）都能做。但"指挥单位持续去哪/干什么"要每单位/每帧对抗 AI 重规划，且
   `moveTo` 会被 AI 覆盖——正是陷阱 6 的死壁。做"实时持续 RTS 指挥"≈ 重写游戏 AI 层，超出一个 mod 范围。
3. **能落地的折中**（若用户仍要）：托管式接管单单位（本次已实现并稳定）+ 一次性右键命令（Lua 通道）。
   但这不是"全 RTS 通用的上帝视角框选指挥"，用户已确认不是其想要的东西。
4. **止损判断**：需求理解在 3 个版本后才对齐，方向错误（非缺陷驱动），继续在"接管单单位"上加码
   永远到不了"RTS 指挥"。及早停止，把机制事实存档，比硬做正确。

### D. 删除清单（已执行）

- 工作区 `DirectControl/`（Plugin.cs + ControlLogic.cs + csproj + README.txt + Nexus_description.md + bin/obj）
- 部署 `Easy Red 2\BepInEx\plugins\ER2_DirectControl.dll`
- 配置 `Easy Red 2\BepInEx\config\er2.directcontrol.cfg`
- 发布包 `<输出目录>\ER2_DirectControl_v*.zip`（v1.0.0–v1.0.3 共 4 个）与
  `ER2_DirectControl_CN_v*.zip`（v1.0.0–v1.0.3 共 4 个），以及两个中间产物目录 `ER2_DirectControl/`、
  `ER2_DirectControl_CN/`
- `scripts/build.ps1` 的 `DirectControl` 项（ValidateSet + switch 行）

**相关共享件保留**：`Shared/NoHintsHudLink.cs`（Hide Anything 联动，HVT 等仍用）；`ER2_mod_技能.md` 提到的
自由视角/接管信号事实留档备查（若未来做"观战接管/幽灵视角"类功能可复用）。

> 复盘里凡"再碰单位控制/上帝指挥"的需求，先请用户明确是 **接管单单位** 还是 **RTS 框选多单位指挥**，
> 两者技术跨度天差地别。

---

## 2026-09-25 会话总结（UniGen 灰字马拉松 → 发布准备：三个可复用的流程结论）

### A. 一次"发布"要动的不止代码——四个必查项（本轮全部命中）

1. **版本三处同步**（`BepInPlugin` / 启动日志 / 文档首行）。本轮源码已是 2.5.33，而 `README.txt` 首行还停在 **v2.5.19**、台账 §2.12 停在 v2.5.26 —— **13 个版本的文档漂移**。
   ⚠️ 关键：`build.ps1` 打出的 zip 名取自 `Plugin.cs`，所以"**包名对、文档错**"这种坏包不会被任何自动化拦住，只能靠发布前手工核对（AGENTS §6）。
2. **日志必须收口**：发布版 ≠ 开发版，周期性/限频诊断（2s / 5s / 15s 循环）必须进 `Debug/debugLog` 门控。
   清理时有**一条红线**：门控日志可以，**门控"存活计数"不行**——`ModCatalog` 的 `probeTicks++` 是看门狗的存活信号（陷阱 17d-2），只门控日志行、绝不动计数，否则会把"日志安静"变成"看门狗失灵"。
3. **双语顺序**：先 `-Cn` 后默认 EN，**EN 最后跑**（最终部署 = EN 构建）。
4. **验收要看字节，不看"打包成功"**：① 构建 = 部署 = 包内 DLL 的 sha256 逐字节一致；② 反编译复核版本双写；③ **拆包后把"包内文档"与"源文档"做 `cmp`** —— 只核对文件数量/大小是不够的：`build.ps1` 会**按包语言取不同的文档文件**（`README_CN.txt` vs `README.txt`），取错了大小看起来也"正常"。

### B. 本轮新陷阱：数值型 cfg 的"两个范围定义"（已入 AGENTS 17g52）

用户一句"应该是50%透明度"，查出 `uiPanelAlpha` 的 50% **从未真正生效**：

- cfg 侧 1.4.35 就已放宽为 **0.50 / 0.40~1.0**；
- 而 `Shared/Er2Ui.cs` 的 `SetPanelAlpha()` 里还留着 1.4.24 时代（默认 0.85）的 `Mathf.Clamp(v, 0.55f, 1f)` → 插件启动时把 0.50 **静默抬成 0.55**，且 cfg 的 0.40~0.55 整段是死区。

**为什么长期没被发现**：这个 bug **完全静默**——不报错、不告警、日志无痕；而我此前只核对"cfg 里写进去的值"。**流程漏洞的本质是"验证了输入、没验证输出"。**

> **通用律**：数值型配置项的取值链路上，**任何一处 clamp / 常量都是事实上的第二个"范围定义"**；改取值范围必须对整条链路 grep 一遍（`Config.Bind` 默认值 → `AcceptableValueRange` → `SettingChanged` setter → 内部 clamp → 使用点）。
> **检测手段**：反编译读 clamp 常量（本轮正是靠这一步定案的），或在 setter 里加一条一次性 `LogAlways` 打印**生效值**。
> 同族：陷阱 109（cfg 不随默认值更新）、陷阱 102（改了但没生效）、陷阱 78/113（同一个量的两个定义处必须合一）。

### C. 一个省事的自检姿势

把"**用户点名指定过的数值**"（透明度、线宽、字号、倍率…）列成一张小表，发布前逐个核对**实际生效值**而非源码字面值——成本约 1 分钟，能拦住整类"配了不生效"的问题。

### D. Downloads 发布包治理

本轮扫描：`ER2_*.zip` 共 **149 个**（其中被取代的 **129 个**）+ `build.ps1` 打包暂存目录 **10 个**（暂存目录每个 3~4 项，等于又占一份体积）。
**成因**：`build.ps1` **每次打包都留下 zip + 暂存目录，且从不自清理**；而 09-24 / 09-25 两天做了 40+ 轮迭代，每轮都出包。

**纪律（三条）**：
1. 迭代期用 `-SkipPackage`（只部署、不出包）；
2. **只有"要发出去的那一版"才出包**；
3. 定期按"**每个 mod 每个语言只留最新一版**"清理（台账 §4 的既有惯例）。

**另发现（需修台账）**：SquadCommand 的 `1.4.37~1.4.47` 与 UniGen 的 `2.5.18~2.5.32` 都是"**部署了但没打包**"——台账 §4 里那几条 `_v1.4.37 / _v1.4.38` 记录**在磁盘上并没有对应文件**。§4 需要按磁盘实际文件重建，而不是靠"我记得打过包"。

### E. 批量删除脚本的两条硬纪律（本轮实战场验证）

本轮实际执行：移除 **131 个旧包 + 10 个暂存目录**（其中 49 个是用户手动删的，82 个 + 10 个目录由脚本分批删除）。

1. **删除类脚本必须带"数量 / 路径"前置断言——否则会静默删错。**
   首次执行时脚本写的是 `assert len(drop) == 131`。结果实际只找到 82 个：**用户在此期间手动删掉了另外 49 个**（`Downloads` 目录 mtime 17:47:46，而我的扫描是 17:45）。
   **断言当场拦下，一个文件都没删。** 如果当时写的是"按最新版分组，删掉非最新的"，它会**照样正确执行**——所以真正救命的是那个显式计数断言：**它把"我的假设"与"磁盘现实"的偏差变成了一次硬失败，而不是一次静默的部分操作。**
   > **通用律**：破坏性批处理要先把"预期影响面"写成断言（条目数 + 路径前缀 + 保留集完好），执行中**每批后复核保留集仍在**，任一不符立即中止。宁可误停，不可误删。

2. **动手前先重新读一次现场，别拿几分钟前的扫描结果直接开删。**
   同样的原因：环境里有第三方（这里是用户本人）在并发改动。**扫描 → 展示 → 确认 → 执行** 之间只要有时间差，执行前就要重新取一次现场计数。

> 附：本机沙箱下 `Add-Type` 与 `New-Object -ComObject` 均被安全策略拦截 → **脚本无法调用系统回收站**。此类情况下禁用"永久删除"，必须先向用户二次确认；可选的中间态是"移入隔离目录"（可恢复、不释放空间）。

---

## 2026-09-25 SquadCommand 1.4.49 复盘（阵型拖动手感：灵敏度 + 掩体走廊）

**用户反馈**："长按右键拖动阵型时灵敏度太高，阵型常常无法展开。拉动让幽灵单位扩散时总是一字排开站在掩体边，根本散不开。"

### 两个根因、一个共同本质：分配的参照系与用户看到的目标形状错位

1. **灵敏度太高**：`UpdateEndPoint` 的像素→米比例只有一个变量（镜头高度），无衰减系数——高视角下轻拖就是几十米的箭头，阵型拉不准。修法：`formDragSens` cfg（默认 0.5）乘进 `dragPerPx`，代码侧再钳 0.05~4（陷阱 17g52：取值链上每处 clamp 都是第二范围定义）。
2. **幽灵挤墙散不开**：`AssignCovers` 以**锚点**为圆心就近贪心，与阵型线形状**完全无关**——掩体密集处（村庄/墙边）全军被吸进锚点旁同一排掩体，"阵型"退化成"抢墙"。修法不是调半径/数量参数，而是**把参照系从点换成线**：
   - 走廊过滤：掩体投影沿线 ±(halfSpan+2m)、纵深 ±corridor 才可用；
   - 步兵按**沿线投影**排序（与排线同序），匹配投影最近的掩体（纵深作 0.5 罚项）；
   - 最近匹配 >12m 放弃 → 照常排线，不为一个单位抢远掩体。

> **通用律（新）**：**"排布类"功能的分配算法，参照系必须与用户操作的目标形状一致**——用户拖的是一条线，分配却绕着圆心做，几何语义错位，调参数救不回来（与陷阱 114 同源：先确认"用户话题里的对象/形状"是什么，再动手）。
> 配套纪律：**预览（幽灵）与下发（IssueFromDrag）必须共用同一个分配函数**，否则"看到的不等于下发的"；步兵排序复用 `SortSoldiersByProj`（与 `BuildLine` 同序），顺带减少交叉走位。

### 流程备注

- 本环境 **PowerShell 工具的 stdout 不被捕获**（连 `Write-Output` 都空）——排查/验证一律 `*> 重定向到文件` + UTF-16→UTF-8 转码后 `Read`；`exit=0 且无输出` 时以**部署时间戳/sha256** 为准判成败（AGENTS §3 的"静默拒绝"教训在工具层的变体）。
- 验证：sha256 部署=构建（`08FFBE61…`，239,104 B）；反编译版本双写 1.4.49；cfg 已删待重启重生成；迭代期 `-SkipPackage` 不出包（经验文档 D 节纪律）。

### 1.4.50 增补（二轮反馈的教训：幽灵只画"掩体分配"本身就是设计错误）

1.4.49 上线后用户："现在很难再让幽灵单位出现了，就算出现了还是会排排站，不散开。" 复盘出**两层错误**：
- **参数层**：走廊过滤（|纵深|≤8m）+ 12m 沿线匹配上限**叠加** → 可用掩体窗口过窄，开阔地抓不到掩体 → 幽灵消失（对 1.4.48"全军挤墙"矫枉过正——收紧过滤时没做"掩体=0 的开阔地"压力推演）。
- **设计层（真正根因）**：幽灵只画掩体分配。掩体天然是"一排"，而**用户是拿幽灵判断"散没散开"的**——特例画得再对，整体缺席 = 永远"排排站"；真正散开的阵型线槽位只有 0.2m 小黄点，视觉上等于没有预览。

修法（1.4.50）：**语义反转 = 阵型优先、掩体吸附**——先给全部步兵排槽位（`ComputeLineSlots`，拖多宽散多宽），槽位 X 米内有空闲掩体才顺势占用（每槽一人/每掩体一人）；**幽灵覆盖全部步兵槽位**（`Apply(covers, lineSlots, facing)`，每帧刷新，克隆预算 2/帧防尖刺）。

> **通用律（补强上轮"参照系"律）**：**预览必须覆盖"全部落位"，不能只画"特例"**——用户拿预览判断整体；整体缺席时，特例的正确毫无意义。
> **流程律：给收紧类修复做"极端参数压力推演"**（本轮该推的三种地形：开阔地掩体=0 → 幽灵还剩几个？密集墙 → 会不会还是一排？超长线 → 两端怎么办？）——1.4.49 若做了第一步，"幽灵消失"当场就能推出来，不用等用户再报一轮。

### 1.4.51 增补（三轮同类反馈的终局：默认值反转，且要请用户给截图）

1.4.50 上线后用户发截图："还是散不开"——从移动目标圆圈向墙拖线，阵型线（竖直）与墙平行且距约 8m，吸附半径 8m 内**墙上每个掩体点对每个槽位都够得着** → 8 槽全吸上墙，又排成一列。

**结论：只要"阵型拖动默认做掩体吸附"，掩体密集地形里阵型必被吃掉——这是设计错误，不是参数问题。** 1.4.51 把 `formCoverCorridor` 默认 8 → 0（吸附变成可选微调；找掩体走 N 键/右键建筑两个专用入口）。

> **通用律 A：同一问题第三轮反馈时，停止调参，反转默认值**——三轮"散不开"说明该行为（阵型里主动找掩体）与用户的真实意图根本对立；继续调半径/罚项/过滤窗口都是在错误方向上加精度。
> **通用律 B：地形类 bug 请用户发截图**——"散不开"三个字定不了案，截图里"阵型线与墙平行、距 8m"一个几何事实就定案了（同陷阱 95/98/103：感知/行为问题要拿实证，别拿想象推演）。
> **通用律 C：一个功能里塞两套意图（阵型 + 找掩体）时，必有一套在特定地形压倒另一套**——把次要意图降级为可选（cfg/专用键），别让它在默认路径上与主意图竞争。

### 1.4.52 增补（虚线滞留：到位判定的参照点必须与单位实际落点一致）

用户："没有幽灵单位后那个虚线还显示，这不好看。" 挖出**两层**：
1. **设计层**：阵型下发画"每单位 → 锚点"的扇形虚线——幽灵已预览落点，这套线只剩杂乱 → `withRouteLines=false`。
2. **生命周期层（更隐蔽、通用性更强）**：到位判定 = 距**共享锚点** ≤ moveRadius，而散布类命令（阵型/进建筑掩体）的单位落在**各自槽位**上，距锚点可达线长一半 → **永远判不到位**，观察窗挂满 45s——虚线、进度行、目标点圈全部滞留。修法：观察登记带 `unitDests`（每单位落点），按各自落点判到位，全员到槽即清。

> **通用律 D：散布类命令的"完成判定"必须按每单位自己的落点算，不能按共享中心算**——中心判距对聚集类命令（普通移动）成立，对散布类命令恒假。凡"X/N 已到位"类进度，问一句：**判的是谁的到位、以什么为参照**。
> **通用律 E：预览消失后残留的反馈要专门审视**——用户对"预览期"与"执行期"的视觉预期不同：预览期信息越足越好，执行期只留必要（单位本身 + 进度行），多余连线都是杂乱。

### 1.4.53 增补（反面教训：关行为时别把它承载的信息一起关掉——通用律 A 的修正）

1.4.51 据"三轮散不开"把掩体吸附默认关成 0，下一轮用户就反馈："现在幽灵单位完全不能像之前那样在预定掩体位置展示了。"

**复盘**：吸附这个开关同时承载两件事——① 行为（单位去抢掩体，在掩体密集地形会吃掉阵型）；② 信息（幽灵显示"这位会进这个掩体点"，还带蹲/趴姿态）。用户要的是 ②、不是 ①；我把两者一起关了。
**修法**：吸附恢复，但**本地化**——默认 8m → **3m**，只吸真正挨着槽位的掩体（`DistXz(掩体点, 槽位) ≤ 3`）。挨着的吸（信息回来了），远的不吸（阵型不被拉走）。

> **通用律 A 修正：第三轮反馈要停调参、反转默认值——但反转前先拆开"行为"与"它承载的信息"**：只关行为、保留信息（降级为本地/可选/弱化），别一刀切。
> **通用律 F：默认值在两个极端之间来回摆（8→0→3）说明缺的是一个"语义阈值"而非"开/关"**——正确问题不是"要不要做"，而是"什么条件下做"：这里阈值 = "吸附不得把单位拉离它的阵型槽位超过 X 米"，X 取 2~3m（≈一个身位）即同时满足两端诉求。

### 1.4.54 增补（"不跟手"的比例参照量 + 长目标的查询盲区）

1. **"不跟手"的真因是比例参照量取错**：`dragPerPx` 按相机**垂直高度**算，而俯视倾斜时地面 1 像素对应的真实距离按**斜距**（相机→锚点视线）算——45° 俯角系统性偏小约 30%，再叠加默认 0.5 → 箭头端点只到光标一半。修法：`camH / sinDep`（`sinDep = |forward.y|` 钳 0.35~1；正俯视时与原式等价，是纯改进）。
2. **长目标的空间查询有盲区**：掩体查询只在锚点查一次、半径上限 35m → 长阵型线两端根本没被查过。修法：`QueryCoversAlongLine` 沿目标形状多点采样 + 指针去重。
3. **吸附的"挤堆"要显式防守**：掩体点常密集分布在同一段墙上，全吸会让人贴人 → `GapOk` 强制已选点之间 ≥ `max(1.5, 槽位间距×0.6)`，让阵型的疏密不被掩体改写（长线间距大→吸附自由；短线→少吸）。

> **通用律 G：屏幕→世界的换算，先问"参照量是哪个"**——透视投影下"地面 1 像素 = ? 米"取决于**沿视线的距离**，不是相机高度/到目标的水平距离；倾斜视角下用错参照量会得到一个"方向对、量级系统性偏小"的比例，表现为"跟不上手/慢半拍"，而这类错误**不会报错、只会手感差**（同族：陷阱 78/113 的"两个定义处"）。
> **通用律 H：对"长条状目标"做空间查询时，单点 + 固定半径上限 = 两端盲区**——要么沿形状采样多点，要么让半径随尺寸走；查不到 ≠ 没有（同陷阱 102"改了但没生效"的近亲：查了但没查全）。

### 1.4.55 增补（陷阱 118：兜底查询被同一把过滤器杀死 = 死代码）

用户截图提问："明明有掩体，不应该靠近掩体吗"——线穿过沙袋墙，8 幽灵全在线上、0 吸附。根因不在吸附，在**查询**：`QueryCovers` 有向查询空 → 无向兜底再查 → **兜底结果仍被同一把 `IsCoverAvailable(facing)` 有向过滤器再杀一遍**。朝向过滤一旦全灭，无论兜底多少遍可用掩体恒 0。
修法：两段查询各自过滤（`FilterCoverStates` 只留摧毁/被占/载具）；摘掉后置 `IsCoverAvailable`（原生语义不可考——interop 只有 IL2CPP 桩、实现在 GameAssembly 原生层；有向查询 `GetCovers` 本身已带 dir 参数，后置过滤属双重过滤）。

> **通用律 I："查询 A 失败 → 用宽松条件 B 兜底"之后，B 的结果必须用 B 的条件验收**——拿 A 的过滤器去验 B 的结果，兜底就是死代码；这类 bug 的症状是"明明有数据却恒空"，且**只有 A 的条件恰好全灭时才发作**（低频、难复现）。写兜底时先问：兜底结果会不会再过一遍主条件？
> **另：IL2CPP interop 只有桩，方法语义（连参数名都只有 `shootDirection`）不可考时，不要围绕它做双重过滤——把方向语义交给唯一知道它的原生入口（GetCovers 的 dir 参数）。**

### 1.4.56 补（参数的默认值沿革要写进台账，别靠记忆）

`formCoverCorridor` 四轮走了 8 → 3 → 6 → 10：8 太贪（整条线被吸上墙）、3 太紧（够不着）、6 仍偏紧、**10 就位**。每次调值都受"最近一轮反馈"影响，没有沿革记录就只剩反复试。
> **通用律 J：用户反复微调的阈值型参数，把"取值沿革 + 每个值对应的实测现象"写进台账条目**——下一轮调值时能一次到位，而不是在旧值上重新试一遍（同族：陷阱 109 的"改默认值"、陷阱 112 的"共享量"）。

