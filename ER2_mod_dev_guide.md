# Easy Red 2 BepInEx Mod 开发经验总结

> 适用：Easy Red 2（IL2CPP）BepInEx 6 插件开发。本文件汇总环境、工具、机制、API、踩坑与工作流，用于快速恢复项目上下文。

## 1. 环境与工具

- 游戏根目录：`E:\SteamLibrary\steamapps\common\Easy Red 2`
- 插件目录：`<Game>\BepInEx\plugins\`
- 配置目录：`<Game>\BepInEx\config\`
- 日志：`<Game>\BepInEx\LogOutput.log` / `ErrorLog.log`
- interop：`<Game>\BepInEx\interop\`（IL2CPP 反编译包装，启动时重新生成）
- 编译：dotnet SDK，`<TargetFramework>net6.0</TargetFramework>`，引用 interop/core 程序集（`Private=false`）
- 反编译：`ilspycmd -t <Type> <dll> -o <dir>`（列出类型 `ilspycmd -l c <dll>`）；**可从已部署的 plugins DLL 恢复源码**
- 资源解包：UnityPy（Python）读 AssetBundle（er2items/er2bundle 等）
- rar 解压：`"C:\Users\71011\新建文件夹\WinRAR.exe" x -o+ -y <rar> "<out>\"`（系统 WinRAR 装在非标准路径）
- BepInEx 插件以 `[BepInPlugin(guid, name, version)]` 识别，文件名任意

## 2. 关键机制（反编译确认）

### 状态体系
| 状态 | 方法/字段 | 说明 |
|---|---|---|
| 失能 | `Creature.SetIncapacitated(bool)` | 趴地等救；主动调用（血量>0）会被游戏判死（失能+血量归零=死亡） |
| 投降 | `Soldier.Surrender()` / `UnSurrender(string)` | 投降；解除时游戏自动装备副武器 |
| 出血 | `Soldier.SetBleeding(bool)` / `isBleeding` 字段 | 原生流血状态；**止血（false）后触发游戏自然回血** |
| 死亡 | `Kill()` / `KillSynched()` / `IsDead` | 血量归零判死在 `Damage` 内部 |
| 姿态 | `SetPose(SoldierPose)` | Idle/Crouch/Prone |

### 血量系统（重要）
- 血量字段：`Creature.life_total`（类型 `ProtectedInt`，加密防篡改包装）
- 读：`soldier.life_total.Value`（int）
- **写：必须整体赋值 `soldier.life_total = new ProtectedInt(hp);`**（getter 返回值类型副本，`life_total.Value = x` 编译报 CS1612）
- 失能阈值：`Creature.INCAPACITATED_THREESHOLD`（约 <22，血量低于会失能/判死）
- 护甲/部位倍率：`BodyPart.GetBodyPartMultiplier(BodyPartType, HitType)`（static，可 patch）

### 伤害体系
- `BodyPart.HitPart(float damage, float penetration, Vector3 point, string fromFaction, HitType hitType)`
- `HitType`: Projectile / Explosion / Fire / Melee / VehicleCollision
- `Creature.Damage(float dam)`：原生扣血入口；血量归零在其内部判死
- 伤害结算：基础伤害 × 部位倍率 × 穿透/爆炸倍率；未击穿护甲大幅减免
- `BulletData`: mmPenetration / penetrationDamage（击穿后伤害）/ explosionDamage / explosion_penetration / ActualSpeed（初速）
- 断肢：`Soldier.DetachLimb(BodyPartType)`（head/chest/arm_l/arm_r/leg_l/leg_r）

### 流血机制（实测）
- `isBleeding=true` 时游戏周期性调 `Damage(1.0)`（小值；**dam ≤ 5 可作为原生流血特征**区分敌人伤害）
- 原生流血频率约 20 次/s（1.0/次）；`SetBleeding(false)` 止血 → 启动游戏自然回血
- **原生 `DetachLimb` 自带失血 DoT**（约 20-30/s，断肢必死机制）——活体断肢不要调它
- 游戏"断肢不死回满保护"：判死时若单位有断肢状态 → 血量回满（"血量重置"现象根源）

### 物品/食物系统（AI Food 用）
- 食物物品：`ItemObjectRecoverLife : ItemObjectStackable`（`recoverLife` 回血量、`isFood` 食物标记）
- 虚拟物品：`VirtualRecoverLife : VirtualItemStackable : VirtualItem`
- 使用入口：`Creature.RecoverLife(VirtualRecoverLife)`（原生吃+回血+消耗+动画）
- 背包：`Creature.inventory`（InventoryManager）
  - `FindItemOfType<T>()` / `GetItemsOfType<T>()`（泛型；返回的条目**必须 TryCast**）
  - `GetNearItems()`（附近 ItemObject）、`TakeIntoInventory(VirtualItem, source)`（跨背包转移）
  - `AddItemToInventoryAndDestroyInstance(ItemObject)`（拾取地面物品）、`FindItems(string id)`
- 单位列表：`Creature.allCreatures`（含尸体）/ `Creature.aliveCreatures`（活体，static 列表，替代 FindObjectsOfType）

### AI 决策（实测）
- AI 用绷带依赖 `isBleeding` 状态（直接设字段即可触发 AI 行为）
- **AI 移动无法直接驱动**：`Soldier.Move()` / `NavMeshAgent.SetDestination` 都会被游戏 AI 控制器覆盖；但**官方通道存在**：`Lua_Soldier/Lua_Squad.moveTo`、`findCover`、`DestinationWaypoint` 任务链、`AiParams.followCustomDirectCommands()/followCustomSquadOrders()`（详见研究索引）

### 单兵 AI 数值调参 / 精英单位钩子（SuperSoldiers 会话，反编译确认，2026-08-24）
> "勇气/精度/反应时间"这类数值**不在 AiParams**（AiParams 全是 bool 能力开关）——单兵数值调参走以下钩子：
- **精度总线**：`SoldierAI.ProcessAiAccuracy(Soldier user, AiAccuracyMode mode)`（**static**，返回精度倍率；`AiAccuracyMode`: infantry/mg/cannon/spa）→ Postfix `ref float __result` 乘倍率 = 改某士兵命中率（`user` 是射手）
- **射速/反应**：`SoldierAI.CalculateNextShootDelay()`（单发间隔 float，实例方法）→ Postfix 除倍率
- **压制**：`SoldierAI.OnSuppressed()`（无参）→ Prefix return false = 无视压制；`Soldier.suppress_time_end`（float 时间戳）可周期清零兜底
- **士气**：`Soldier.Surrender()` 与 `Soldier.SurrenderSynched()`（均无参）→ Prefix return false = 永不投降（**两个都要拦**）
- **受击方归属**：`BodyPart.GetUnit()`（→Creature）→ `TryCast<Soldier>()` 拿受害者；伤害入口 `HitPart(damage,pen,point,fromFaction,hitType)` / `TryDamageWithExplosion(maxPenetration,explosionDamage,explosionPosition,Soldier responsible,hitType)`——`ref float damage/explosionDamage` Prefix 缩放（ref 修改在 IL2CPP 生效，CombatTweaks v1.2.0 实证）
- **攻击方归属**：`Bullet.BulletDamage(ImpactSpecifier, BulletData, Vector3, int damage, Soldier shooter)`（**static**，带 shooter）→ `ref int damage` 缩放=精英子弹加伤；爆炸用 `TryDamageWithExplosion` 的 `responsible` 参数
- **玩家阵营兜底**：`Vehicle.GetVehicleFaction()`——玩家在载具里时 `PlayerController.ControlledCharacter` 可能不是 Soldier
- **原生英雄系统**（SpawnManager）：任务配置 `spawn_hero` 字段触发；`TrySpawnHero(bool calculateTask=true)`（同步返回 Soldier）、`SpawnHero(DogTag, Action<Soldier>)`（协程异步）——命名英雄单位官方通道（DogTags 档案管名字/配装）
- **靶场假人**：`Target : Soldier` 也在 `Creature.aliveCreatures` 里——遍历晋升/统计类逻辑必须 `TryCast<Target>()` 排除
- **精英单位实现模式**（SuperSoldiers 设计，未实测定案）：耐久用**受击伤害倍率**而非写 `life_total` 上限（陷阱 6/10：伤害同步会覆盖外部写入）；晋升掷骰**每士兵一生一次**（周期巡逻重复掷骰→全员晋升）；全局存活上限防扎堆；`Creature.aliveCreatures` 巡逻 + `Time.unscaledTime` 节流（陷阱 36）；运行时常驻对象/贴图 `hideFlags=(HideFlags)61`（陷阱 41）

### 深层研究索引（2026-08-16 会话产出，新增机制速查）
> 完整文档：`ER2_mod_research.md`（§7 合并机会清单）；六份分报告：`research_out/report_{lua,battle,ai,items,ui_stats,vehicles_misc}.md` + `weapon_catalog.txt`（338 ID）+ `research_types.txt`（1159 类型）+ `code*/` 反编译源。

- **内置 Lua 任务脚本系统（MoonSharp）**：官方任务脚本在 `StreamingAssets\Missions\**\scripts\{general,mission,AI}` + `SHARED\`；`#include` 预处理、`er2.run()` 存活脚本（MY_PHASE 守卫）、`phase_N.lua` 阶段自动加载、`setBrain("xxx.lua")` 换 AI 大脑（Photon 同步）。BepInEx 可注入：`new ER2ScriptRunner(code,name).RegisterNewScriptRunner().LoadAndRunScript(code)`（生命周期/UI 仍归插件管）。
- **全局 API 速查**：`ItemsDatabase`（物品注册表，GetItemObject/GetSpecificItemClass\<T\>/GetAllItemsOfType\<T\>/GetLoadout）、`SoundManager.SpawnAndPlay(pos,clip)`（自定义音效最干净入口）、`SaveDataManager`（JSON/Binary 静态序列化 + persistentModsPath，mod 存档首选）、`SpawnManager`（static 生成单位/载具，异步回调）、`EventManager`（string-key UnityEvent StartListening/TriggerEvent）、`AchievementManager.Unlock(id)`、`OutlineManager.AddGameObject(go,layer)`（原生描边高亮，做"标记敌人"直接复用）、`ArtilleryStrike.StartStrike()`（字段可覆盖=呼叫炮击）、`VehicleDamagablePart.HitPart/TryPenetrateArmor`（载具弹道主入口）、`TurretGun.triggerPressed/ExtractOneBullet`+`TurretWeapon` 字段（炮塔增强）、`SoldierAI.ProcessAiAccuracy`（static AI 精度总线）、`SoldierAI.TryStopBleeding`（LimbTweaks 止血挂钩）、`TemperatureEstimator.EstimateTemperature(...)`、`CensorshipManager.IsCensorshipNeeded()`（血腥开关）、`RadioManager.IsNearRadio(pos)`、`MatchData.ForceNext/NextBattle`、`BattleManager.SetPhase/NextPhase/OnWin(winner,faction,forced_end)/AddBattleStat`。
- **统计/存档**：`SavableData.Statistics`（playerKills/Deaths/VehiclesDestroyed + 阵营计数器 + 成就计数）与 `GameProgresses` 可读写，写完调 `SavableData.SaveData()`；`BattleResults` 只是 3 字段 struct（无 K/D），战报数字走 `connectedBattle`。
- **原生已有勿重复**：DamageIndicator（受伤方向指示）、DeathPanel/RespawnPanel（死亡重生）、EndBattleGUI（战报）、TaskIconDatabase+ObjectiveGUI（目标指示）、原生准星/命中标记贴图、OutlineManager 高亮。
- **方法学**：interop 方法体全是 IL2CPP 原生转发壳（只有签名/字段/继承关系，行为靠实测）；磁盘无 AI 参数 xml（AiParams 全 bool 开关）；物品定义在 er2items bundle 的 PropData；22 个 `CorvoBundles\*.manifest`（0.1MB）可替代 3.7GB 全量解包；mods/ 目录 = 原生 ModsLoader 的 Steam Workshop AssetBundle 加载源。

### 模型/视觉
- 手臂骨骼：`Soldier.leftArm/rightArm`（Transform）；隐藏=localScale 归零+Renderer 禁用
- FPS 手：`FPSGunManager.hands_mesh`（双手一体，无法单手隐藏）；`ShowRealArms(bool)`
- 玩家控制：`PlayerController.currentController` / `ControlledCharacter` / `SetPlayer(Soldier unit, float)`

### 互动提示（NoInteractionHints 用）
- `InteractionGUI2.SetInteraction` / `InteractionGUI.SetInteractions`（拦截点）
- `InputDisplayer.OnEnable`（按键提示显示）
- `InteractionGUI2.instance.interactionWindow` / `inputDisplayer`
- **v2 跨 mod 联动契约**：见 §3.6（`HudCompat.IsHudHidden` 逐 mod 开关，取代旧式直接读 `Plugin.HudEnabled` 字段）

### 设置界面（ModManager 用）
- `SettingsGUI_V2.Update` / `SettingsTabRight` / `SettingsTabLeft`（原生设置界面，patch 注入 MODS 页）
- 配置重置：遍历插件 cfg 条目写回默认值（BepInEx.Configuration）

### 投掷物轮盘（ThrowableWheel 用）
- `CircularMenu2.ShowCircle`（原生轮盘显示，替换内容）
- `PlayerController.Update`（热键轮询）、`Soldier.Throw`（投掷拦截）

## 3. 踩坑记录（重要）

1. **Ambiguous match**：`[HarmonyPatch(typeof(X), "Method")]` 无参数类型时遇到多重载会抛异常、PatchAll 中断——重载必须 `new Type[]{...}` 或 `new Type[0]`
2. **版本号非法**：`"2.13.22a"` 带后缀 → BepInEx `Skipping type ... version is invalid` → 插件不加载。必须标准 SemVer `x.y.z`
3. **Harmony 参数按名注入**：Prefix/Postfix 参数名必须与目标方法 interop 参数名一致（如 `SetBleeding(bool bleeding)` 不是 `value`）——名字不匹配=注入 null，拦截静默失效
4. **FindObjectsOfType 掉帧**：0.5s 全场景扫描掉 7 帧——避免轮询扫描，用事件驱动/静态引用；需要枚举单位用 `Creature.allCreatures/aliveCreatures`
5. **血量归零判死**：系统在 `Damage` 内部判定死亡（不是 Kill 调用）——拦 Kill 转向无效；主动 `SetIncapacitated(true)`（血量>0）后续血量归零也会判死
6. **写 life_total 的 CS1612**：getter 返回值类型副本 → 必须整体赋值 `new ProtectedInt(hp)`
7. **跳变写血量=判死**：单帧大幅扣血（如 100→35 的直接写字段）→ 游戏检测到外部修改 → 判死/覆盖归零；**渐进小步写（每帧 ±1，用 float 累积器）安全**；绷带补血跳变安全（游戏认可治疗）
8. **游戏内部血量状态**：伤害记录/同步会把 life_total 覆盖为"游戏计算值"——不要与游戏 Damage 路径打架；**优先走游戏原生路径**（Damage()/SetBleeding/RecoverLife），或渐进写
9. **活体断肢别调 DetachLimb**：原生失血 DoT 约 20-30/s 秒杀；用视觉隐藏（HideArm）+ 自己控制血量；尸体/死亡断肢可以调
10. **IL2CPP 类型转换必须 TryCast<T>()**：interop 返回基类包装（如 `Get<VirtualItem>`），C# `as` 按 CLR 类型检查恒失败；`obj.TryCast<T>()` 按 IL2CPP 类型转换
11. **GUIStyle 默认 textColor 是黑色**：`new GUIStyle()` 的 normal.textColor 为黑，GUI.color 是乘法 tint（黑×红=黑）→ 必须显式设 white；`new GUIStyle(其他)` 拷贝构造被 IL2CPP 裁剪，只能无参构造
12. **static setter patch**：`[HarmonyPatch(typeof(X), "set_PropertyName")]` 可拦属性 setter（拦截战役改天气）
13. **battle 场景引用**：主菜单也有 BattleManager——等场景实例（如 DayNightCycle.instance）就再操作
14. **DLL 被锁**：游戏运行时 plugins DLL 被占用——部署用轮询重试脚本（每 10 秒，10 分钟上限）
15. **IL2CPP 裁剪**：interop 里不存在的方法/属性不可用（如 GUIStyle 拷贝构造、部分 GUI API）——报 `Method unstripping failed`
16. **il2cpp 反编译产物语法**：`val..ctor(...)` 非法——改写为 `new Vector2(...)`
17. **投降单位血量被游戏接管**：外部扣血无效 → 用计时器 Kill 等效"流血而死"
18. **跨 mod 联动用反射**：`AppDomain.CurrentDomain.GetAssemblies()` 找程序集 + `GetField("字段", Static|Public|NonPublic)` 读静态字段；缓存 type/FieldInfo 防每帧反射开销；对方 mod 缺失时自动跳过
19. **原生流血特征判定**：`isBleeding=true` 且 `dam ≤ 5` → 可吞掉并自行控制扣血（渐进）；敌人伤害（dam>5）放行
20. **地图/武器数据**：在 AssetBundle（er2items 3.9GB / er2bundle 4.5GB），用 UnityPy 读 typetree
21. **不要用 PowerShell 重写含中文的源码文件**：`Get-Content`/`Set-Content` 默认按系统 ANSI（GBK）编解码 UTF-8 文件 → 中文注释变乱码（mojibake）且可能破坏行结构导致 CS1513。源码文件一律用 write/edit 工具（UTF-8 安全）；PowerShell 只用于复制/编译/日志
22. **UnityAction 委托桥接在 IL2CPP 下不可靠**：`UnityAction<bool>` 回调收 False 恒变 True；`UnityAction<float>` 回调参数是垃圾 float（E+35）——交互控件不要绑事件，改为每帧轮询 `value/isOn` 对比 lastValue（详见 ER2_UI_design.md §8）
23. **背包添加物品会降级成基类**：`Inventory.AddVirtualItem(vi)` 与 `InventoryManager.AddItemToInventory(prefab)` 都会把物品归一化成基类 VirtualItem（实测 `FindItemWithID` 也是基类）——原生转盘按 `VirtualThrowable` 类型过滤会无视；需要正确子类（VirtualGrenade 等）时**直接 `inv.items.Add(vi)`**（`Inventory.items` 是 `List<VirtualItem>`，可 Add/RemoveAt/按 id 数数）
24. **原生回调拒绝外部替换的 UI 数据**：`CircularMenu2.ShowCircle` 的数据替换后显示正常，但原生选择回调匹配不到条目（选中无任何反应、`Soldier.Throw` 都不被调用）——**能走原生管线就走原生**（补货让原生协程自己构建转盘，选择/投掷全通）
25. **协程入口方法 patch 可能不触发**：`PlayerController.ShowGrenadeSelectionMenu` 从未被调用（IL2CPP 直接构造 `_ShowGrenadeSelectionMenu_d__193` 状态机）→ 入口 Prefix 失效；改用**定时循环**（`PlayerController.Update` Postfix + 2s 节流）保证快照前物品就位
26. **不要在 Prefix 里递归重调原方法**：Soldier.Throw 重定向方案（Prefix 里 `__instance.Throw(proper)` + 递归守卫）在 IL2CPP 下重入参数异常（重入后仍收到基类），还会干扰 AI 原生投掷——放弃该方案
27. **BepInEx 自动落盘防不胜防**：`ConfigFile.SaveOnConfigSet=true`（默认）运行中自动写盘；**游戏退出时 BepInEx 自动保存全部 cfg**——"按下才保存"必须用**暂存机制**（控件改动只进 staged 字典，保存按钮才 `BoxedValue=` + `cfg.Save()`；未保存 = 连内存都不改，退出保存写入的还是旧值）
28. **KeyCode 枚举配置没有 AcceptableValueList**：cfg 里 "Acceptable values: None, Backspace..." 注释是 BepInEx 自动生成的，`entry.Description.AcceptableValues` 为 null；且 uGUI Dropdown 在 IL2CPP 下值变化检测不可靠（轮询不到）→ 热键改键用**点击按钮 + `Input.GetKeyDown` 捕获**（候选键枚举 F1-F12/字母/数字/方向键/鼠标，Esc 取消）——比下拉可靠得多
29. **ModManager 中文词典严禁重复键**：`new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)` 加重复键（enabled vs Enabled、跨 mod 共用键如 Debug Logging）→ 静态构造抛异常 → 整个 MODS 页空白只剩页脚（**踩过两次**）——加键后必须跑重复键校验脚本
30. **原生 Hint 在设置界面打开时延迟显示**：`Corvostudio.UI.Hint.Display` 在设置 GUI 打开时不渲染，关闭设置才在局内弹出 → 即时反馈用控件按钮文字闪烁（FlashItem：暂存 Text+原文+截止时间，轮询恢复）
31. **uGUI 文字溢出被滚动区裁切**：`horizontalOverflow=Overflow` 的超长文字被滚动 Mask 裁掉（长 mod 名显示不全）→ 截断+省略号，展开时另起小字行显示全名；**描述行高度必须保守估算**（420px/行、8px/字、+1 行余量），否则文字纵向溢出压到下一行按钮
32. **DLL 文件名 ≠ 插件名**：插件按 BepInPlugin 元数据识别（ER2_RecoilOverhaul.dll 里是 "Universal Recoil Control (Dynamic Curve)"）——查插件用日志 Loading 行或二进制搜 GUID，别按文件名猜
33. **语言一致性**：配置简介来自插件代码（entry.Description）——EN ModManager + CN 版插件 = 界面英文但简介中文；发布截图需 ModManager 与目标 mod 同语言
34. **Copy-Item -Recurse 嵌套坑**：目标目录已存在时会把源文件夹复制成其子目录（plugins 里出现 `X\X\` 嵌套、DLL 重复加载风险）——先清理目标或用"目标=父目录"写法
35. **资源清单读 manifest**：er2items 3.7GB 全量 UnityPy 太重——`Easy Red 2_Data\StreamingAssets\CorvoBundles\er2items.manifest`（0.1MB）直接列出全部资源路径（投掷物 ID 全集就这么拿的）
36. **局内暂停时 `Time.time` 冻结**（timeScale=0）：所有"节流/冷却/看门狗"逻辑在局内设置界面（暂停态）会永久停摆——用 `Time.unscaledTime` 做节流时钟（滚动自愈、自动保存看门狗、查找冷却、日志节流全部踩过；症状是"主菜单正常、局内完全不生效"，日志表现为诊断记录只出现一次）
37. **设置滚动范围由 ScrollRect.content 决定，不一定是 contentPage**：局内设置层级为 `Content(我们的页) → Content(父级) → Viewport(Mask) → ScrollView(ScrollRect)`——必须向上找 ScrollRect 组件再取其 content 设置高度；且垂直拉伸锚点的 content 要改顶部锚定 + ForceRebuildLayoutImmediate 才会生效（离开页面时还原锚点防影响原生页）
38. **interop 不存在的 Unity 生命周期方法不能 patch**：`SettingsGUI_V2.OnDisable` 未在 interop 生成 → Harmony `Undefined target method` → 整个插件加载失败。先 `ilspycmd -t <Type>` 确认方法存在再 patch；关闭检测改用帧看门狗（Update 轮询 + 无操作 0.8s 落盘）
39. **别用字符串搜索验证构建语言**：.NET 元数据字符串堆编码与直觉不同，ASCII/UTF-16 盲搜会误报（中文词典字符串两种构建都编译进去；`DefaultChinese` 是编译期常量，三元表达式被 const 折叠成单一字符串）。验证 EN/CN 用 `ilspycmd -t ER2ModManager.Plugin` 看 `DefaultChinese = true/false` 或看折叠后的页面标题字符串
40. **无条件编译的共享词典**：ModManager 的 `ChineseLabels` 词典（含全部中文）在 EN/CN 两种构建里都存在——判断界面语言只看 `DefaultChinese`，不要被"中文串存在"误导
41. **运行时创建的 Texture2D/AudioClip 必须设 `hideFlags=(HideFlags)61`**（KillFeed+ 同款）：Plugin.Load（主菜单场景）里创建的对象在进战斗场景时被 Unity 卸载销毁，`UnityEngine.Object == null` 对已销毁对象返回 true → 所有空值检查静默跳过绘制/播放——症状是"日志全绿（加载成功、patch 生效）但游戏里什么都看不见听不见"（v1.0.1 诊断实测：加载成功 `SetData=True`，进战斗后 `tex=NULL`/`clip missing`）。排查"代码执行了但看不见"先怀疑对象被销毁
42. **归属判定用 `Creature.IsPlayer()` 引用级比较**，别只靠 faction 字符串：faction 格式是 `UnitedStates_allies`/`Germany_axis`（26 国）字符串匹配能用但拿不到攻击者名字/武器；KillFeed+ 的 `lastHit` 归属表（受害者 InstanceID → HitRecord{attackerName/attackerFaction/attackerIsPlayer/weaponName/cat/time}，25s 窗口 + 8s 去重 + 400 条 Prune）是信息流类 mod 的标准架构
43. **死亡事件用 `Soldier.Kill/KillSynched` Prefix 前置快照**（`__state = !IsDead`，Postfix 里 `FeedManager.OnDeath`）比 Damage Postfix 轮询干净——Kill 只在真死亡时调用，天然覆盖击倒后死亡/流血死/投降死；`SetIncapacitated` 是击倒/救起事件源
44. **子弹命中数据源用 `Bullet.BulletDamage(ImpactSpecifier, Soldier shooter)`**（带 shooter 引用）和 `BodyPart.TryDamageWithExplosion(__instance, Soldier responsible, HitType)`（爆炸带 responsible），比 `BodyPart.HitPart(fromFaction)` 信息全（能拿攻击者身份/武器）；载具走 `VehicleDamagablePart.TryPenetrateArmor(shooter)` 系列
45. **IMGUI 纯黑底可以画**：`GUI.color = new Color(0,0,0,0.45f*alpha)` + 1x1 白贴图 DrawTexture 是 KillFeed+ 的半透明黑底做法（旧结论"纯黑 tint 被剔除"指全黑描边，半透明黑底实测可用）；文字发光用 12 偏移重复 GUI.Label，宽度用 `style.CalcSize(GUIContent)` 精确测量
46. **PS 5.1 十六进制字面量坑**（素材生成脚本实测）：`0xFFFFFFFF` 解析为 Int32 -1、`0xEDB88320` 为负 Int32 → CRC/掩码全错且不报错；**必须十进制**（4294967295/3988292384）
47. **PS 5.1 数组字面量解析坑**：脚本块里 `@(a, b, c, d * $var)` 的末元素 `*` 表达式不带括号 → 整个数组静默变空（像素函数返回 null 导致渲染全透明）；必须 `@(a, b, c, (d * $var))` 逐元素加括号
48. **PS 5.1 中文注释坑**：无 BOM UTF-8 脚本被按 GBK 误读，含 `——` 等字符的注释行会破坏解析（报错指向下一行 `Unexpected token`）——脚本保持纯 ASCII 注释，或用 write 工具 + 手动加 BOM（edit 工具重写会丢 BOM）
49. **PowerShell byte 位移截断**：`[byte]172 -shl 8` 按 byte 类型截断为 0 → 读二进制字段先 `[int]` 转换再移位；PNG/WAV 等二进制解析脚本全部踩过（validate_assets.ps1 已修）

## 3.5 UI / IMGUI 设计（原生观感）

> 详细文档见 `ER2_UI_design.md`（含 API 清单、改造记录、踩坑）。要点速查：

- 游戏 UI 分三套：**uGUI**（HUD 主体：PlayerGUI/InteractionGUI2/Hint/PhaseBarGUI）、**IMGUI**（BattleManager.OnGUI，游戏自己画得少）、**Marker3DGUI**（世界头顶标记，自带遮挡/缩放/排序）。
- 原生复用 API（全部 public static，interop 可直接调）：
  - 通知弹窗：`Corvostudio.UI.Hint.Display(text, duration, showOnGUIdisabled, force_this)`——最像原生的通知通道（淡入淡出+排队）；调用前判 `Hint.instance != null`；**参数全传**，显示成功后清自己的重绘标记防每帧入队。
  - 描边文字：`GuiExtension.OutlinedLabel(rect, text, style, outlineSize)`——游戏自己的 IMGUI 描边，替代手写 ±2px 黑字。
  - 原生字体：`PhaseBarGUI.GetDefaultFont()`——赋给 GUIStyle.font 立即原生感（IMGUI 默认字体一眼是 mod）。
  - 目标横幅：`PlayerGUI.ShowObjectiveToPlayer(t, n, force)`；短消息：`ShowShortText(t, force)`；受伤血屏：`BloodSplashGUI.PlayEffect()`；世界标记：`Marker3DGUI.Draw(...)`。
- 回退链：原生通道 → 原生字体+OutlinedLabel → 裸 GUI.Label，每层 try/catch。
- UI 相关 csproj 引用：`UnityEngine.UI.dll`（Text 类型）、`UnityEngine.TextRenderingModule.dll`（Font 类型）——缺了分别报 CS0012/CS0246。
- `using Corvostudio.UI;` 才能用 Hint（不在全局命名空间）。
- 文字颜色用 `Hint.fullColor`；背景用半透明黑（a≈0.7）；尊重 NoInteractionHints 的 F5 HUD 联动。

## 3.6 跨 mod UI 联动契约（Hide Everything v3，mod 作者必读）

**背景**：原名 NoInteractionHints，v3 改名 **ER2 Hide Everything**（GUID 不变）。F5 隐藏时**哪些原生 UI 类别**
（`[Hide UI]` 分区，13 类）与**哪些 mod 的 UI**（`[Hide Other Mods UI]` 分区）被隐藏，都由玩家在
ModManager 逐项开关（原生类别默认开，操作/反馈类默认关；mod 默认开）。
本工作区生态（LimbTweaks/WeatherControl/HealthBars）已接入；第三方/未来 mod 按下面任一方式即可获得兼容。

**方式 A（推荐，查询契约，无编译期依赖）**：显示自己的 UI 前查询——

```csharp
// 反射调用 ER2NoInteractionHints.HudCompat.IsHudHidden(id, displayName)
// 返回 true = 主开关关（F5 隐藏中）且该 mod 独立开关开 → 隐藏你的 UI；false = 正常显示
if (ER2Shared.NoHintsHudLink.IsHidden("er2.你的modid", "显示名")) return; // 跳过绘制/入队
```

- 共享辅助 `Shared/NoHintsHudLink.cs`（csproj 加 `<Compile Include="..\Shared\NoHintsHudLink.cs" Link=... />`）
  或自己写等价的反射缓存（找程序集名含 `NoInteractionHints` → 类型 `ER2NoInteractionHints.HudCompat`
  → 方法 `IsHudHidden(string,string)`，缓存 MethodInfo，try/catch 兜底返回 false）。
- **首次查询自动注册**该 id 的配置项（默认开），无需任何注册代码；每帧查询开销可接受（热读取）。
- 提示型 UI（如 `Hint.Display`）：在显示前查一次；持久型 UI（如每帧 OnGUI 绘制）：每帧开头查一次。
- **即时性**：mod 走原生 `Hint.Display` 的通知，F5 时由 Hide Everything 直接清屏（停协程+清队列），
  正在显示的立刻消失；mod 自绘回退路径靠每帧查询（F5 下一帧即停画）——两者都即时。

**方式 B（零代码，惯例字段自动发现）**：插件程序集中定义 `public/internal static bool`
名为 `HudEnabled` / `HudVisible` / `ShowHud` 的字段（你的 UI 显示开关），Hide Everything
加载时 + 设置界面/战斗帧自动扫描（仅扫带 BepInPlugin 属性的插件程序集）。被发现后生成配置项
（**默认关**，需玩家在 ModManager 显式开启），隐藏时写 false、恢复时写回 true。
注意：字段语义必须是"true=显示 UI"，且不要与自己逻辑打架。

**旧式兼容**：`Plugin.HudEnabled` 静态字段仍保留 = 主开关（读它的旧 mod 跟随主开关全部隐藏，
不受逐 mod 开关控制）。新代码请走方式 A。

**陷阱**：
1. 别在 Load() 里"注册"（Hide Everything 可能还没加载，反射找不到）——查询式自动注册没有此问题。
2. 配置项键名 = mod id（如 `er2.limbtweaks`），ModManager CN 词典按键名映射中文，新 mod 未映射时显示原文（可接受）。
3. 隐藏/恢复由各 mod 自己查询驱动（无推送回调）：玩家在 ModManager 改开关，下一次查询即生效。
4. **原生 UI 分类（[Hide UI]）由 Hide Everything 自己管理**：mod 不要碰 `UiGroups`（内部实现）；
   想让自己的 UI 进分类列表请用方式 A。

**契约逻辑测试**：`tmp_hudcompat_test/` 有独立测试宿主（编译生产源码 HudCompat.cs + UiGroups.cs +
NoHintsHudLink.cs，`dotnet build` + 运行 `ER2HudCompatTestApp.exe`，34 项断言覆盖主开关/逐 mod 开关/
总开关/反射路径/字段发现写入恢复/UiGroups 注册开关巡检/cfg 落盘）。改契约后先跑它再部署。

## 4. 各 Mod 状态

### ER2_LimbTweaks（Realistic Limb Injuries & Dismemberment）
- 源码：`LimbTweaks/Plugin.cs`；版本：2.13.100
- 功能：活体断肢（累积 60 伤害，断肢枪全额结算无锁血）、流血系统（断肢 7/s、玩家 3.5/s、非断肢 4/s；绷带 4/s→1/s→无效）、断腿锁卧倒、断右臂投降+10s 流血死、断臂行为限制（换弹/捡枪/投掷/绷带）、四肢伤害减半、尸体断肢（伤害+初速×0.08 ≥80）、禁止断肢单位回血（拦 SetBleeding(false)）、屏幕提示（原生 Hint 通道，见 ER2_UI_design.md）
- 配置：General.enabled / Corpse Shooting.corpseEnabled / Living Soldiers.aliveEnabled / Bleeding.bleedEnabled（流血开关）/ Bleeding.affectsPlayer（玩家免疫开关）/ Limbs.damageNeeded / Limbs.corpseDamageThreshold
- 关键 patch：HitPart / BulletOnHit / BattleStartReset / Ragdolize×2 / SetBleeding / UseBandages / PickUp / SwitchTo / Reload×2 / Throw / UnSurrender×2 / SetPose / SetPlayer / BleedTick / NotifyGui / NativeDamageControl（Creature.Damage）/ LimbMultiplier（GetBodyPartMultiplier）/ SetBleedingTrack
- v2.13.100：屏幕提示联动改用 NoInteractionHints v2 契约（`NoHintsHudLink.IsHidden("er2.limbtweaks")`，逐 mod 开关）

### ER2_WeatherControl（Battlefield Sky Control）
- 源码：`WeatherControl/Plugin.cs`；版本：1.7.1
- 功能：每局强制天气（Clear/Rain/Snow）+ 时间氛围（Midday/Sunset/Dawn/Night/Cloudy/Foggy via SetDayTime）、热键切换组合、拦截战役天气重置（set_WeatherType/SetDayTime patch）、屏幕提示（原生 Hint 通道，与 NoInteractionHints 联动）
- v1.7.1：屏幕提示联动改用 NoInteractionHints v2 契约（`NoHintsHudLink.IsHidden("er2.weathercontrol")`）

### ~~ER2_HealthBars~~（⚠️ 2026-08-16 已删除，mod 与源码已移除；以下仅保留 Marker3DGUI 原生标记 API 经验，供后续 UI mod 参考）
- 源码：`HealthBars/Plugin.cs` + `DamageFx.cs`；版本：1.5.0（双语：EN 版 + CN 中文版 `-Cn` 构建，`DefaultChinese` 编译期常量）
- 功能：所有士兵头顶血条+数字（绿/黄/红渐变），遍历 aliveCreatures + Camera.WorldToScreenPoint，observedMax 自适应血量上限；数字用原生字体 + GuiExtension.OutlinedLabel 描边（见 ER2_UI_design.md）
- v1.1.2：F5 隐藏联动（`NoHintsHudLink.IsHidden("er2.healthbars")` 每帧查询；默认 F5 时血条一起隐藏，可在 ModManager 逐 mod 关闭）
- v1.2.0：血条视觉升级（连续渐变色、受伤残影延迟条 ghost 下滑、掉血闪红、外框/高光、距离缩放 80→46px）；**战地风格伤害反馈**：`BodyPart.HitPart` Prefix（玩家 faction 匹配记 marker，hpBefore 基线 + estDamage + head 标记）→ `Creature.Damage` Postfix 按实际扣血差值生成数字（`Soldier.Damage` 重写虚方法需补 patch；HitPart Postfix 兜底未走 Damage 的路径）；数字命中点跳出（追踪世界坐标 0.18s）→ 抛物线飞落底部计数器（0.7s，先上抛后坠落）→ 落地累加（弹跳放大）；计数器显示一段时间内伤害总量，`counterResetSeconds`（默认 4s）无伤害后淡出清零；爆头金色大数字、击杀红色 + `KILL xN`；同目标 0.08s 内连续结算合并为一个数字（爆炸多部位）
- v1.3.0：CN 构建（中文配置说明 + 击杀标记"击杀 xN"，`Plugin.T(cn,en)` 双语助手）；计数器图标重制为**程序生成抗锯齿贴图**（Texture2D RGBA32 + SetPixels + Apply，外环+内环+四向指针+中心点，1px 平滑过渡 + 投影双层绘制）；新配置 `numberScale`(0.5-2) / `counterScale`(0.7-1.6)；float 配置全部带 `AcceptableValueRange`（ModManager 渲染滑条）；ModManager ChineseLabels 词典已覆盖全部新键（keys/sections/descriptions 三词典）
- v1.4.0：血条改**血红色胶囊体**（圆角矩形 SDF 贴图 `GetRoundedTex` 共用血条/计数器面板，深黑红框+暗血红底+亮血红填充按比例、浅血红残影、闪红脉冲）；新配置 `showOnlyDamaged`（仅受伤后显示，BarState.lastDamage 计时）/ `damagedShowSeconds`(1-30)；数字动画**上升放大（0.6→1.3 easeOut）下降缩小（1.3→0.5）**近大远小；**爆头金色优先于击杀红**（`n.head ? headStyle : (kill ? killStyle : normal)`）；计数器重做：**金色圆角边框（落地脉冲增亮）+ 深色圆角底 + 金色准星**
- v1.5.0：**头盔等非 BodyPart 碰撞体不经过 HitPart → 补 `BulletInstance.OnHit` Prefix 兜底归属**（`bi.shooter` 玩家判定 + `GetComponentInParent<Creature>` + 子弹伤害估算 `penetrationDamage+explosionDamage+ActualSpeed*0.08` 作 estDamage 上限，与 HitPart marker 合并窗口一致不重复）；血条颜色加深（亮红长条外框 + 饱和血红填充 0.62→0.98 按比例）；**showOnlyDamaged 只认玩家伤害**（DamageFx 消费 marker 时 `HealthBarGuiPatch.NotifyPlayerDamage(ptr)` 刷新计时，AI 互打/流血不再触发）；**掉血缓冲**（`BarState.smooth` Lerp dt*6 ≈0.4s，填充+数字用 smooth，残影 ghost dt*4 更慢）；计数器改**战争氛围**：深铁灰军规面板（0.09,0.09,0.11 α0.94）+ 暗红细边框（落地脉冲）+ 顶部暗红识别条 + 灰白准星（击杀时数字转暗红警示色）；`GetRoundedTex(float radius)` 参数化按半径缓存（血条胶囊 16 / 军规面板 3）
- v2.0.0（**渲染层原生优先重写，重要 API：Marker3DGUI**）：`Marker3DGUI.Draw(Il2CppObject key, int slot, Texture tex, Vector3 worldPos, float size, float alpha, Color tint, bool occlusionTest)` —— 游戏原生世界头顶标记系统（uGUI RawImage 池，`Entry{img RawImage, worldPos, alpha, targetAlpha, popT, lastFrame}`），自带屏幕投影、淡入淡出（alpha→targetAlpha 按 fadeSpeed）、弹出动画（popTime）、遮挡检测（OcclusionProbe）、帧过期清理（lastFrame）；`Marker3DGUI.instance/EnsureInstance/globalSizeMult/fadeSpeed/popTime/MarkerScreenSize`。**血条改用此通道**（`MarkerBars.cs`：17 级比例白色透明条纹理 0..16 + tint 染色，slot0=ghost 浅红 tint/slot1=fill 暗红 tint，掉血闪红=tint 提亮，showOnlyDamaged 超时=Draw alpha 0 原生淡出；每帧 BattleManager.Update Postfix 调用）；**计数器/击杀提示去黑框**——透明图标 + 原生描边文字（GuiExtension.OutlinedLabel），IMGUI 只保留伤害数字/计数器/击杀提示（无面板）。用户明确：IMGUI 自绘黑色半透明面板观感不对（"都有一个半透明黑色的框"），**原生优先**
- 踩坑：`OnGUI` 每帧多次事件（Layout/Repaint）→ 动画/衰减必须用**纯时间函数**（绝对时间戳差值），固定步长会随事件数加速；`Soldier.Damage(float dam)` 是 Creature.Damage 的重写（虚分派可能绕过基类 patch），双 patch + marker 幂等（先到先消费）防重复；`fontSize *= scale` 每帧累积缩水 → 从基准字号乘；**IMGUI Label 矩形高度不随字号缩放会裁切大字号**（numberScale 2.0 时 40px 字被 28px 矩形裁掉 → rect 高度 = fontSize + 12）；CN zip 命名 `<pkg>_CN_v<ver>.zip`（build.ps1 -Cn 自动处理）

### ER2_AIFood（AI Auto-Eat & Heal）
- 源码：`AIFood/Plugin.cs`；版本：1.4.0
- 功能：AI 血量 <40 自动吃背包食物（FindItemOfType<VirtualRecoverLife> + TryCast + RecoverLife），2s 检查循环，跳过玩家

### ER2_HideAnything（原 ER2_NoInteractionHints_DoneProMaxEnd）
- 源码：`NoInteractionHints/Plugin.cs` + `HudCompat.cs` + `UiGroups.cs` + `UiHiders.cs` + `UiPatches.cs`；版本：**4.3.0**（v4.3 更名 Hide Anything，保留 GUID com.ryan.er2.nointeractionhints，配置延续）
- 功能：**勾选制**（无热键、无锁定按钮）——13 个原生 UI 分类 + 逐-mod 开关，勾选 = 始终隐藏且严格生效（勾选即锁定，Enforce 持续重藏防游戏重新显示）；地图与小地图合并为一类（MiniMapGUI.Instance.miniMap/组件本体/detailsContainer/mapName + MapGUI.renderersParent）；通知弹窗即时清屏（停 Hint 协程+清队列）；修改自动保存（ModManager 看门狗 0.8s 落盘）
- 关键机制：`UiGroups` 注册表（Register/IsHidden/ApplyAll/Enforce）；`UiHiders`（ElementHider 记忆式隐藏 + RefCache 5s 冷却查找）；`UiPatches`（一次性显示拦截全用无参 Prefix）；**所有节流用 Time.unscaledTime**（陷阱 36）
- 联动：LimbTweaks/WeatherControl/HealthBars 已接入 v2 契约（见 §3.6）

### ER2_ModManager（in-game mod settings page）
- 源码：`ModManager/Plugin.cs`；版本：1.1.0（双版本发布：英文版 + CN 中文版，`build.ps1 -Mod ModManager -Cn` 直接编译+部署+打包中文版 `ER2_ModManager_CN_v<ver>.zip`；**v1.1.0 起删除中文词典，配置项统一英文人性化，双版本仅剩 UI 按钮文字差异**）
- 功能：游戏原生设置界面（SettingsGUI_V2）末尾注入 "Mod Settings"/"模组设置" 页；自动枚举所有 BepInEx 插件（`IL2CPPChainloader.Plugins`）并渲染其配置项（折叠分区+点击展开）；开关/滑条/改键按钮/下拉（克隆原生设置控件模板）；**显式保存语义**（改动只进暂存区，退出设置时自动保存）；选项下小字介绍（直接显示 mod 自带 BepInEx 描述原文）；展开页底部【重置全部】【复制全部文本】；每个子选项独立【重置】【复制】；关闭设置界面再打开自动留在 MODS 页；R 翻页按钮强制可用
- v1.0.12：第三方 mod 中文映射 98 键（枪伤创口/命中特效/受击布娃娃/移除血渍/子弹穿透/阵亡黑屏/弹痕弹壳/后坐力控制）；滑块最大值钳制 ≤1000（`min(原max,1000)`，min/小数/步进保留原设计）
- v1.0.13：热键冲突检测（KeyCode 或键名含 key/toggle 的配置，按值分组，页面顶部黄色警告行）；每个子选项独立【复制】【重置】按钮（复制内容=名称+简介）；复制反馈即时（按钮文字闪烁 1.5s，FlashItem 轮询恢复，不依赖 Hint 延迟）
- v1.0.15：**显式保存语义 v1**——所有修改只写内存，落盘仅由【保存】按钮触发；**v1.0.17 升级为暂存机制**：改动只进暂存区（`staged` 字典），【保存】才写入 `BoxedValue`+`cfg.Save()`（BepInEx 退出时自动保存写入的仍是旧值 → 不按保存=真不保存）；`cfg.SaveOnConfigSet=false` 防运行中自动落盘；重置同样只进暂存；页面重开控件从暂存区读值（未保存改动可见）
- v1.0.18：快捷键交互**弃用下拉，改为"点击改键按钮+按下捕获"**（`AddHotkeyRebind`/`PollKeyCapture`，直接轮询 `Input.GetKeyDown`，绕开 uGUI 下拉交互问题）；按键候选为真实按键（None+F1-F12+字母+数字+方向键+常用键+鼠标），不再有 F13-F24；全类型暂存日志 `ModManager staged: key = value`
- v1.0.19：**热键控件统一**——string 类型热键（键名含 key/toggle 且 AcceptableValueList 像按键列表）也走改键按钮（`LooksLikeKeyList` 检测：值含 None 或 F\d+/Alpha\d+/Mouse\d+）；我们自研 mod 的 hotSwitchKey/toggleKey 自动命中，与第三方 KeyCode 热键同控件
- v1.0.20：发布版清理调试日志（去掉插件列表 diag、页面高度、按键捕获日志；暂存日志仅非数值类型）
- v1.0.21：描述行高度保守估算（420px/行、8px/字、+1 行余量）防文字溢出压到【重置】按钮；改键行标签留白 190→214px；发布包 v1.0.21（EN + CN）
- v1.0.22：CN 词典新增 NoInteractionHints v2 逐 mod UI 隐藏项（er2.limbtweaks / er2.weathercontrol / er2.healthbars 键名+简介映射）
- v1.0.35：**语言一致性修复**——EN 版 GetDescription 过滤 CJK 描述（用户装中文版插件时，插件自带描述是中文，EN 界面不再显示中文行；陷阱 #21/#33 的根治）；modNames 补全全部已装第三方 mod（World HUD/Fire Coaxial/Melee Tweaks/Realistic Effects/Aim Over Cover/Push It To The Limit/Realistic Blood/FPS Body Shadows Fix/ER2 Kill Feed）；keys+descriptions 补 Easy Red 2 AI Tweaks 4 键（er2.aitweaks，防 cfg 残留/重装无翻译）；sections 补 AI；词典 26/126/126/11 内部重复校验通过（check_mm_dup.ps1）
- v1.0.47：**数值输入框替代滑条**（float/int 配置直接手输数字，轮询 text 绕开 InputField 事件桥接——`UnityEngine.UI.InputField` + 自建 GameObject/Text，注意 IL2CPP 下 `new GameObject(name, params Type[])` 的 Type[] 是 Il2CppSystem.Type[] 编译不过，必须先 new 再逐个 AddComponent；限定范围用 AcceptableValueRange，无范围不限；无效输入不暂存）；**英文分类提示行修显示**（AddCategoryRow 小字号 16 + Wrap + 40px 行高，替代原 20px/Overflow 的长文本行被滚动区裁切）
- v1.0.48：数字输入框与标签重叠修复（标签矩形改用 offsetMin/offsetMax 非对称收缩：右端内缩 208px 恰好让出输入框宽度，此前用对称 sizeDelta + anchoredPosition 偏移导致标签右端伸进输入框区域、文字压在输入框上）
- v1.0.49：数字输入框改**双行布局**（上行标签整行、下行右输入框+左范围提示如 "0.2 – 10000"），文字与输入框彻底不重叠
- 关键 patch：SettingsGUI_V2.Update（注入+轮询+模板缓存+重开接管+R按钮）/ SettingsTabRight（接管翻页+越界拦截）/ SettingsTabLeft（越界回翻接管）
- 详见 `ER2_UI_design.md` §8

### 新 mod 兼容指南（ModManager 自动适配，mod 作者无需配合）
**ModManager 对任意标准 BepInEx 插件自动适配：**
1. **枚举自适应**：读 `IL2CPPChainloader.Plugins` + 插件标准 ConfigFile；无配置的 mod 显示为只读"已安装"条目（展开提示"没有可调设置"）
2. **类型自动映射**：bool→开关；float/int→数字输入（`AcceptableValueRange` 给范围，0-1 保留小数）；string+`AcceptableValueList`→下拉；**热键（KeyCode，或 string 且键名含 key/toggle 且选项是按键列表）→ 点击改键按钮**；未知类型→跳过（不崩）
3. **简介**：有 BepInEx 描述→小字显示原文（中文 mod 中文简介，英文 mod 英文简介，不过滤）；无描述→不显示简介行
4. **名称人性化**：`HumanizeKey` 把键名/分区名/下拉选项值统一转标题文本（分隔符/大小写/数字字母边界拆词 + 每词首字母大写其余小写，enableAIVaulting → Enable Ai Vaulting、AAMult → Aa Mult）
5. **热键冲突检测**：同键多 mod 使用→页面顶部黄色警告
6. **保存语义**：修改进暂存→退出设置时自动落盘；mod 自己读 `ConfigEntry.Value` 的热加载逻辑在保存后生效；启动时缓存值的 mod 需重启游戏
7. **零维护（v1.1.0 起）**：无任何硬编码翻译表/mod 名单——新 mod（含第三方）加载即自动适配，无需更新 ModManager
8. **不侵入**：只读写标准 cfg（BoxedValue/cfg.Save()），不 patch 第三方逻辑
**给新 mod 作者的适配建议（显示效果最好）：** 每个 `Config.Bind` 写清楚 description；数值给 `AcceptableValueRange`；热键用 KeyCode 或 string+`AcceptableValueList`（F1-F12+None）且键名含 key/toggle——ModManager 自动给改键按钮
**v1.1.0 已实现"未来方向"**：删除 ChineseLabels 四词典（modNames/keys/descriptions/sections）→ 配置项标签/分区/选项统一 HumanizeKey 英文人性化，简介用 mod 自带描述原文——彻底零维护（此前"生态闭环"补词典的流程废弃）

### ER2_ThrowableWheel（Custom Throwable Wheel）
- 源码：`ThrowableWheel/Plugin.cs` + `WheelLogic.cs` + `SelectionPatches.cs`；版本：1.3.6（发布包 CN v1.3.6；ModManager 双语适配，mod 本体语言不影响）
- 功能：替换原生手雷转盘（长按 G）为自定义投掷物列表；每 2 秒自动补货玩家背包（`PlayerController.Update` Postfix 节流 + `ShowCircle` 补货双保险），把配置的投掷物以**正确子类直接注入 `inv.items` 列表**（`AddVirtualItem`/`AddItemToInventory` 都会降级成基类 VirtualItem，原生转盘按 VirtualThrowable 类型过滤会无视——直接注入绕过）；原生转盘完全原生（显示/选择/投掷全原生管线，原生 Throw 基类也能扔）
- 配置：Enabled / ReplaceWheelContent（先清原生投掷物）/ RefillBeforeWheelOpens / AllowInMultiplayer / **LoadoutPreset 预设下拉**（自定义/经典14/扩充34/全部48/反坦克/燃烧/烟雾，`AcceptableValueList` 中文值，ModManager 渲染为原生下拉）/ ItemIds（自定义清单，仅预设=自定义时生效）；每 tick `RebuildItemIds()` 热重载配置
- 可用投掷物 ID 全集（er2items.manifest `Prefabs/grenades/` 48 个）：grenade_{ger,usa,eng,rus,jap,ita,f1,rg42,type91,ger_m43,ger_m43_frag,ehg_mod_39,ehg_mod_39y,f1_fr,wz24,wz33,of_mle_1915,ita2,usa_y,no74_sticky,gammon_n82,rus_at,ger_at,rpg40,rpg43}、coconutgrenade_{1,2}、molotov、kaenbin、dynamite、tnt、m37_satchel_charge、geballte_ladung_3kg、at_hhl_{3,3_5}、tankmine{tankmine,tankmine_t99}、smokegrenade_{ger,usa,eng_77,eng_79,rus,jap,ita,ita2,nhg42,bk2h}、grenade_mle1916_smoke_fr
- 子类启发式 MakeVirtualItem：smokegrenade_*/mle1916_smoke/nhg42→Smoke；at_/*_at/tankmine*/satchel/geballte/*_hhl/39a_flamegrenade/*_rpg→AT；其余→Grenade
- 关键 API：`Inventory.items`（List<VirtualItem>，直接 Add/RemoveAt）、`ItemsDatabase.Loaded` / `GetItemObject(id)`（探测物品有效性）、manifest 路径 `Easy Red 2_Data\StreamingAssets\CorvoBundles\er2items.manifest`、`CircularMenu2.ShowCircle(data, key, callback, desc)`（Prefix 替换 data，__0 为 Il2CppReferenceArray<CircularMenuData>）、`VirtualItem.Create(id)` / `GetItemPrefab()`、`CircularMenuData(Sprite, string, Object)`（returnData=ItemObject）、`Inventory.AddVirtualItem(vi)` / `CountItems(id)` / `RemoveItemsOfType<T>()`、`PlayerController.currentController.ControlledCharacter.inventory.inventory`
- 投掷物体系（研究底库 `throwable_research/`）：`VirtualThrowable:VirtualItem` → `VirtualGrenade/VirtualSmokeGrenade/VirtualATGrenade`（ctor(item_id)）；世界物 `ItemGrenade:ItemObject`（grenadeType/fuseType/explodeAfter/explosionDamage/explosionMaxPenetration/explosionRadius/explosion_fx/explosion_bundle/throw_mult）；`Soldier.Throw(vi, aimTarget)` / `InstantiateAndThrowAfter(prefab, time, speed, target)` / `CalculateBallisticThrow` / `GetGrenadeThrowRange`；爆炸 `Explosion.CreateExplosion(pos, maxDmg, maxPen, radius, responsible, ignoreHittable, hitType, canDamage)` 静态方法；枚举 `GrenadeType`(infantryGrenate/smokeGrenade/ATGrenade/phosporusGrenade)、`FuseType`(TimeFuse/InfantryPressionFuse/VehiclePressionFuse/ExplodeOnImpact/StickyTimeFuse)；`ItemsDatabase.GetItemObject(id)` / `GetSpecificItemClass<T>(id)` / `GetAllItemsOfType<T>()`
- 踩坑：`ShowGrenadeSelectionMenu` 方法 patch 不触发（原生直接构造协程状态机）；`PlayerController.Update` patch 可用（2s 节流）；原生回调拒绝外部替换的转盘数据；重定向 Throw（prefix 内递归调原方法）行为异常——不要用

### ER2_MorePhysics（More Physics）
- 源码：`MorePhysics/*.cs`；版本：**0.1.31**（英文 + _CN_ 中文版，ModManager CN/EN 双语适配）
- 功能：给场景物品/物体加血量，血量归零后被子弹/近战/爆炸赋予物理并击飞；飞行物理化物件撞人造成伤害；大件物理物体按配置时间自动消失；按分类控制哪些物体可物理化；**士兵互碰（UnitCollision）**——活体互不重叠、尸体可被推开
- 关键 API/机制：
  - `ItemObject.spawnedItems`（静态列表，避免每帧 FindObjectsOfType）+ `EnablePhysic()/IsPhysicEnabled()`
  - `BulletInstance.OnHit(hitDirection, impactSpeed, hit)` Postfix（子弹命中点；`CalculateDamage()` 拿伤害）
  - `Soldier.Melee()` Postfix（近战触发点；用 `Physics.OverlapSphere` 找附近物体）
  - `Explosion.CreateExplosion(position, maxDamage, maxPen, radius, ...)` Postfix（爆炸范围）
  - `ClassInjector.RegisterTypeInIl2Cpp<PhysicsDebris>()` + MonoBehaviour（碰撞伤害 + 自动消失）
  - **静态批处理虚影**：给静态物体加 Rigidbody 后必须递归 `isStatic=false`，否则原地残留虚影
  - **物体根判断**：不要给命中的子碰撞体挂 Rigidbody，要爬到有 `DestructableBuilding` 的根或仍有 Collider/Renderer 的最高节点
- v0.1.9：**碎片砸飞玩家/尸体修复**——PhysicsDebris 新增防砸机制：①每 0.15s 检查快速飞行（speed²≥16）的碎片，1.6m 内有任何 Creature（含尸体）即把速度×0.05 + 角速度清零；②OnCollisionEnter 命中生物造成伤害后碎片立即停下（velocity 归零不再推人）。背景：士兵是 CharacterController（无 Rigidbody），玩家"被撞飞无伤害"实为碎片砸中死亡布娃娃尸体→尸体被物理弹飞（死亡镜头跟随）；尸体销毁时游戏原生 `Creature.OnDestroy` 抛 NullReferenceException（IL2CPP 原生异常，10 次/会话，紧跟在 [coop-death] 流程后）→ 死亡清理被打断，可能连带"死亡后无法切换小队成员"。另发现第三方 ER2 Leave & Redeploy 在死亡时自动"关闭小队选择→打开部署界面"，会接管原生切小队流程（验证方式：临时移出该 mod 的 DLL 对比）
- v0.1.10：防砸检查加密（0.05s/次 + 半径 2.5m，堵高速碎片穿透检查间隙）；同时确认 0.1.9 会话实证：防砸 0 次触发、NRE 归零、碎片命中生物 8+ 次（说明击飞主因不是碎片，是游戏爆炸 Ragdolize 击飞，已由 CombatTweaks v1.0.2 拦截）
- v0.1.11：①**友军爆炸不发射紧邻友军（≤5m）的物体**（反射读 CombatTweaks 上下文：FriendlyBlastWindow()/FriendlyBlastFaction，跨 mod 联动走反射缓存，缺 mod 自动跳过）——根治"炸弹炸物理化物品把活人一起带飞"（发射后延迟砸中玩家→游戏撞击 Ragdolize，上下文窗口 0.75s 兜底）；②**爆炸灰迹剥离**——物理化时递归移除子物体（decal/scorch/soot/burnmark/blastmark/cinder 等关键词），防"灰迹浮空"；③确认 NRE 是主菜单场景切换阶段的原生噪声（每会话 0~10 次等间隔、与死亡无关，非本 mod 引起）
- v0.1.12：①友军邻近不发射半径 5m→8m；②**防砸急停强化**（0.03s 探测、3.5m 减速 ×0.02、1.8m 内速度/角速度归零 + 触发日志）——在接触前压停碎片，杜绝接触瞬间游戏碰撞布娃娃（"被带飞"实测 208 次 debris hit 证明碎片是主凶）；③友军爆炸发射的碎片打中同阵营士兵不再造成伤害（FriendlyLaunched 标记 + 反射读阵营）；④**排除特效对象物理化**（ImpactFX(Clone)/_fx/fx_ 等——第三方 ImpactFX 特效曾被误物理化打飞并剥掉它的 Decal 子物体）
- v0.1.14：被抑制的友军邻近物体**直接转 kinematic**（游戏原生爆炸冲量对 kinematic 刚体无效——之前我们抑制了自己的发射，但游戏原生物理仍会发射这些刚体，脚下物体被原生冲量带飞=“带飞”真相；转 kinematic 后物理上不可能再被任何冲量发射）
- v0.1.15：**与生物重叠的物体不物理化/不发射**（二分测试实证：禁用全部跳跃后"脚下炸弹"已解决，但"炸弹+物理化物品"仍飞——证明物品通道与跳跃系统无关；Unity 穿透解析会在物体碰撞体与玩家胶囊重叠时推动控制器=被带飞的物理通道）。`OverlapsCreature(go)`：子碰撞体 bounds 中心 OverlapSphere（半径钳制 0.6-3m）查 Creature；物理化入口（GetOrAddRigidbody/FromRenderer）与爆炸发射前（Collider/Renderer 两条路径）全部拦截
- v0.1.25【碰撞体积哲学定案】：**物理化保留游戏原生碰撞体**（用户定案："碰撞体积原本正常、物理化后才不正常"——自算盒替换掉了原本正常的原生碰撞体）。有原生实心碰撞体 → 保留（只禁触发器）；无 → 才补逐 mesh 局部 AABB 贴合盒。碰撞体积迭代史（0.1.18→0.1.25）教训：convex 必填镂空（桌腿/栅栏被填实）、不可读 mesh 凸包烹暴跌地、不要用自算盒替换正常原生碰撞体——详见 `ER2_mod_经验.md` 0.1.18→0.1.31 复盘
- v0.1.26~27：排除 MG/TANK/ARTILLERY SPAWNER 生成器（架重机枪/载具屏幕 pixelated flicker 修复）。**名字排除必须放 IsPhysicalizable（只判命中件本身）**，放 IsSceneObject 会被任意父链误伤（GAR 锚点爬到 SPAWNER → 正常道具无法物理化回归）
- v0.1.28：**飞行物理化物件撞人伤害**（PhysicsDamage 配置，默认10）——走 BodyPart.HitPart(damage,pen,point,fromFaction) 原生路径 + 速度缩放（<2.5m/s 蹭到不扣血，0.4x~2x）。**不做阵营判定——友军保护归 CombatTweaks**（职责边界由用户定）
- v0.1.29~31【士兵互碰 UnitCollision，默认开】：**Unity 机制**——士兵用 CharacterController（CC），CC 之间默认互穿；子弹 raycast 用 LayerMask 与碰撞矩阵无关（所以"受击正常但物理互穿"）。诊断实证：CC 层=1、BodyPart 受击碰撞体层=9（实心）、尸体层=10，且游戏关闭了 `1↔9` 与 `1↔10` 矩阵。**修复 = 开矩阵**：`Physics.IgnoreLayerCollision(1,9,false)` 活体互碰（复用原版受击碰撞体零体积）；`(1,10,false)` + 尸体骨骼刚体强制动态（kinematic=false+useGravity）= 尸体可被推开不挡活人。诊断模式：UnitCollisionLayer=-2（只打矩阵日志不修改）。**排查单位碰撞用 `Physics.GetIgnoreLayerCollision(l1,l2)` 直接读，别猜**
- 配置（ModManager “物理”分区）：DefaultHealth / MeleeDamage / Bullet/ExplosionDamageMultiplier / Bullet/Melee/ExplosionKnockback / **PhysicsDamage** / DespawnTime / EnableItemPhysics / EnableScenePhysics / ExplosionOnlyHardObjects / 分类开关（Furniture/Containers/Debris/BuildingParts/MiscProps）/ ExcludedNameKeywords / TargetPracticeOverride / **UnitCollision / UnitCollisionLayer**
- v0.1.8：**爆炸"范围外物体被炸飞"修复**——①同起爆点 0.6s 内重复调用 CreateExplosion 只处理一次（游戏可能一次引爆逐目标多次调用）；②物理作用半径按 ExplosionPhysicsRadiusScale 收紧（游戏技术半径可能远大于可见爆炸，如建筑坍塌整片爆开）；附限频诊断日志（EXP diag: pos/techR/effR/objects/knocked + 每 ≥1s 一条 knocked 距离采样）用于实测确认真实原因
- 踩坑：
  1. 无脑给所有 ItemObject 开物理会把士兵身上的弹夹/头盔也弄飞——必须跳过 `Creature` 子物体
  2. 场景固定物品不是 ItemObject，光靠 spawnedItems 不够——用子弹/近战/爆炸命中点或 Renderer 扫描补 Collider+Rigidbody
  3. 地面炮灰（草/碎石/壳）不该物理化——用分类开关 + ExcludedNameKeywords 控制
  4. 物理物体不消失会堆积——用 PhysicsDebris 组件按 DespawnTime 销毁

### ~~ER2_MorePhysics~~（⚠️ 2026-08-18 已删除：用户定案"都删了吧，不要了"——物理化/碎屑化/炸毁系统反复迭代仍不满足（下沉/虚影/悬空/威力/手感），全部移除；以下保留经验供未来环境破坏类 mod 参考）
- 曾迭代 0.1.x→0.10.0 共 19 版（构建脚本条目、ModManager 词典、部署 DLL/cfg、Downloads zip 已全部清理）
- **可复用经验**：
  1. IL2CPP 运行时给静态物体加 Rigidbody 必须 `UnstaticUpDown`（向上级联 + 向下递归去 isStatic），且**LOD 多级对象（`Table_01_LOD0`）必须禁用同名兄弟节点 Renderer**，否则原地虚影
  2. `ItemObject.EnablePhysic()` 产物是 kinematic 刚体（打不飞/悬空的同源根因）——须强制 isKinematic=false + useGravity
  3. 爆炸对场景物的作用应按威力分级（explosionMaxDamage 即威力值）；玩家对"物理化打飞"的预期极不稳定，爆炸类效果优先考虑删除/幽灵化（碰撞体全转 trigger 穿透掉出地图）而不是物理推飞
  4. 跨 mod 上下文反射联动（CombatTweaks.FriendlyBlastWindow）方案可用，但会随功能删除被连带移除
- 原 §4 条目、§6.5-6.15 状态记录保留作迭代历史

### ER2_UnitInfoOverlay（Unit Inspector 单位状态悬浮显示）
- 源码：`UnitInfoOverlay/Plugin.cs` + `OverlayLogic.cs`；版本：1.0.2（双语：EN 版 + CN 版 `-Cn` 构建）
- 功能：开发者调试工具——单位头顶悬浮信息面板：名称（[YOU]/[AI]/[DEAD] 标签）、血量（当前/观察上限/失能阈值 thr）、状态标签（BLEED/DOWN/SURR/SPRINT/RUN/MOVE/CRAWL/AIM/RELOAD/THROW/FIRE/VEH/CARRY/CARRIED/TALK/STAM!/WATER）、姿态（Pose: Idle/Crouch/Prone）、阵营字符串、兵种标签（MEDIC/GUNNER/AT/SAPPER/MARKSMAN/RADIO/LEADER）、坐标+距离、速度（平滑估算）、实例 ID+指针；左上角摘要行（开关/计数/范围）
- 显示模式：All（范围内全部单位，maxUnits 上限）/ Targeted（准星 aimRadius 内最近单位，双框高亮）；同阵营绿/敌阵营红/玩家天蓝/尸体灰；F10 热键开关（KeyCode 配置，ModManager 可改键）
- 关键机制：`PlayerController.Update` Postfix（热键轮询+数据刷新，内部 0.1s `Time.unscaledTime` 节流）+ `PlayerController.OnGUI` Postfix（IMGUI 绘制，与原生同上下文，WorldHUD 同款方案）；遍历 `Creature.allCreatures` 静态列表（无 FindObjectsOfType）；相机判定用 `ResourcesManager.mainCamera` 回退 `Camera.main`，`WorldToScreenPoint` + Y 翻转 + 朝向点积 + 屏幕边界裁剪；逐单位 try/catch（遍历中单位可能被销毁）；文字用原生字体（PhaseBarGUI.GetDefaultFont 回退 GUI.skin）+ `GuiExtension.OutlinedLabel(rect,text,style,1)` 描边；半透明黑底（1x1 白贴图 tint，hideFlags=61 + 判空重建，陷阱 41）；GUIStyle 显式 normal.textColor（陷阱 5）
- 配置：General（enabled/toggleKey/displayMode/showAlive/showCorpses/showPlayer/maxDistance/maxUnits/fontSize/bgOpacity/aimRadius/showSummary）+ Info Lines（showHp/showState/showPose/showFaction/showRole/showPos/showVelocity/showId，逐行开关）
- 踩坑/边界：**游戏 interop 无单位最大血量字段** → 血量上限用"观察上限"（首次见到的 HP 起，取历史最大值，observedMax 字典按指针跟踪，单位消失即清理）；`IsReloading/IsAiming/IsThrowing/IsCrawling/IsOnFire` 是**属性不是方法**（编译期即暴露，CS1955）；`IsIncapacitated` 无 getter → DOWN 由"存活且 HP < INCAPACITATED_THREESHOLD"推断（README 已注明）；尸体也是 Soldier 对象（TryCast 可用）；state 行坏状态（BLEED/DOWN/SURR/FIRE）整行染红；F5 隐藏联动走 `NoHintsHudLink.IsHidden("er2.unitinfooverlay")` 契约（Shared 文件随 csproj 编译）
- v1.0.1：**设置/暂停菜单打开时悬浮框未隐藏修复**——原生单位名称在游戏暂停时消失（打开设置即暂停），本 mod 改用同款可见性规则：`Pause.isPaused` 静态字段门控 + 兜底 `Time.timeScale <= 0.001f`（陷阱 36 同源：局内设置暂停 timeScale=0）。**没有直接照搬"原生名称绘制路径"（PlayerController.OnGUI 内 GuiExtension.OutlinedLabel(Rect,string,float,int) 0.4f 调用，WorldHUD 拦截点）**——那条路径只画小队成员（25m 内），照搬会丢失"全部单位"功能，门控方案效果相同且保留全单位能力
- v1.0.2：**加载中/死亡时仍显示 + 中文版内容仍英文 两个问题修复**——①`IsHiddenState()` 扩展门控：`LoadingCircle.IsLoading()`（战斗加载/换场黑屏）、`DeathPanel.instance.gameObject.activeInHierarchy`（死亡结算界面）、`PlayerController.ControlledCharacter.IsDead`（死亡→重生期间；注意 CT v1.0.4 实测死亡瞬间 IsDead 可能滞后一帧，DeathPanel 兜底）；②**悬浮内容双语化**：所有行文本（血量/阈值/姿态名站立蹲姿卧倒/阵营/状态标签/兵种/坐标/速度/ID/摘要行）改走 `T(cn,en)`（编译期常量折叠，方法体按构建折叠，调用点双字面量无害——CombatTweaks 同款模式）；坏状态染色改用 bool 标记（不再字符串搜索 "BLEED"，双语后搜不了）
- v1.0.3：**悬浮窗完全消失修复（重要教训）**——v1.0.2 新增的 `LoadingCircle.IsLoading()` 与 `DeathPanel.activeInHierarchy` 门控**语义未验证**，其一在战斗中恒为 true → 悬浮窗永不绘制。修复：①门控只保留**实证/语义确定**信号——`Pause.isPaused` + `timeScale`（v1.0.1 用户实测战斗中为 false 的铁证）+ `ControlledCharacter.IsDead`（语义确定）；②加载尾巴隐藏改**确定性实现**：`firstVisibleTime`（场景从无单位→有单位的时刻）+ `hideAfterLoad` 配置（默认 6s，0-30），不依赖任何原生加载信号；③诊断日志定位。**诊断实证（关键结论）**：`LoadingCircle.IsLoading()` 战斗中恒 **false**（安全但加载中语义未验证）；**`DeathPanel.instance.gameObject.activeInHierarchy` 战斗中恒 true（v1.0.2 消失根因，不可用作门控）**；`Pause.isPaused`/`timeScale`/`IsDead` 战斗中被抑制日志证实只在设置打开/死亡时触发（正常战斗零抑制日志）——方法学：**未验证的 interop 信号不能直接当门控，先加诊断日志实测**
- v1.0.4（发布版）：**用户 ModManager 实测调好的配置烤进代码默认值**（displayMode=Targeted / showCorpses=true / maxUnits=1 / aimRadius=160 / hideAfterLoad=6 / showRole=true / showVelocity=true）+ 移除全部诊断日志（DiagRawStates/LogSuppressOnce），保留低频功能日志（加载行/开关切换）；发布包 `ER2_UnitInfoOverlay_v1.0.4.zip` + `ER2_UnitInfoOverlay_CN_v1.0.4.zip`
- 构建：`scripts\build.ps1 -Mod UnitInfoOverlay`（部署 `ER2_UnitInfoOverlay.dll` + 打包 EN zip）；`-Cn` 打包 `_CN_` zip

### ER2_CombatTweaks（Combat Tweaks: 单机关友军伤害 + 弹药调整 + 战场调整）
- 源码：`CombatTweaks/Plugin.cs`；版本：1.2.0（双语：EN 版 + CN 中文版 `-Cn` 构建，`DefaultChinese` 编译期常量；ModManager 词典已映射）
- 功能：①**单机友军伤害关闭**——同阵营单位互相无法造成伤害（子弹/近战/爆炸/炮击/撞击全覆盖；AI 友军伤不到玩家，玩家也伤不到队友；敌军不受影响），默认仅单机（联机走 `PhotonNetwork.IsConnectedAndReady && !OfflineMode` 判离线，与 ThrowableWheel 同款）；②**弹药倍率/无限**——火力点（固定机枪）/ 火炮 / 防空炮 / 自行火炮 / 坦克机枪 / 坦克主炮炮弹 / 飞机炸弹 / 步兵武器，各独立倍率（1.0 原版）+ 无限开关（优先于倍率）；③**爆炸范围倍率**（Explosion.CreateExplosion 参数 + 静态字段）；④**步兵受击伤害倍率**（HitPart + TryDamageWithExplosion）
- 配置：General.Enabled / Friendly Fire.FfEnabled、FfSingleplayerOnly / Ammo.AmmoEnabled、EmplacementMult、ArtilleryMult、AAMult、SPAMult、TankMgMult、TankShellsMult、InfantryMult（0.2-10000，float 全带 AcceptableValueRange）、各 Infinite 开关、PlaneBombInfinite / Explosion.ExplosionRadiusMult(0.2-10) / Weapon.WeaponDamageMult(0.1-100)
- 关键 patch：`BodyPart.AllowDamage`（**原生静态判定点**，Prefix 同阵营→false）/ `BodyPart.HitPart` / `BodyPart.TryDamageWithExplosion(responsible)` / `VehicleDamagablePart.HitPart` / `VehicleDamagablePart.TryPenetrateArmor(...,shooter,...)`（载具/步兵双保险兜底）/ `TurretGun.ExtractOneBullet(TurretWeapon)`（炮塔弹药消耗唯一入口，Prefix 存旧值 + Postfix 回补）/ `Magazine.ExtractOneBullet` + `Magazine.ExtractBullets`（步兵弹药真入口）/ `VehiclePlane.DropBombs`（飞机投弹）/ `Explosion.CreateExplosion`（爆炸范围 + 友军上下文）
- 关键机制：`TurretMG : TurretGun` 即火力点子类（TryCast 判定）；火炮/防空/自行火炮按 `Vehicle.IsArtillery()/IsAA()/IsSPA()` 分类；炮弹/机枪区分=弹槽 `ammos[].bullet_id`（`TurretWeaponBullet`）正则口径 ≥20mm=炮弹，兜底 `maxAmmoCount≥50`=机枪；每发回补 `(1-1/倍率)` 用 float 累积器（Spare，≥1 才还一整发，<1 倍率时负累积额外扣发）；无限=打一发补一发永不换弹；`ConditionalWeakTable` 按引用相等缓存状态，武器销毁自动回收；默认倍率=1 时热路径零开销
- v1.0.1：FF 拦截补丁加 `[HarmonyPriority(Priority.First)]`（**跨 mod 补丁顺序坑**：默认优先级下 LimbTweaks 的 HitPart Prefix 先执行，把友军命中记进它的伤害累计 → 关友伤后队友仍会断肢；First=4000 先短路 return false，后续所有 Prefix（含对方 mod）与原生方法全部跳过，累计不再触发）；弹药倍率上限 1000→10000（配合 ModManager 手输数字）
- v1.0.2：**活体被自己/友军爆炸炸飞（无伤）修复**——游戏的爆炸击飞走 `RagdollManager.Ragdolize(Vector3)/AddForce(Vector3)` 独立于伤害路径；友军保护拦了伤害但击飞照发。新增 `Explosion.CreateExplosion` Prefix 记录"友军爆炸上下文"（responsible 同阵营时置位），期间拦截同阵营士兵的 Ragdolize/AddForce（敌人照常被炸飞）。RagdollManager 全局命名空间；Creature 基类无 faction，须 `TryCast<Soldier>()` 后读 faction
- v1.0.3：①**友军子弹整链拦截**——`BulletInstance.OnHit` First 短路（LimbTweaks 的断肢累计除 HitPart 前缀外还有 OnHit 路径（尸体断肢在 OnHit Postfix），只拦 HitPart 不够）；②友军爆炸击飞窗口 0.75s（`friendlyBlastUntil` + `FriendlyBlastWindow()` 供反射，Postfix 清场 Priority.Last 保证同帧其他 mod 后置补丁还能读到上下文——**跨 mod 同方法 Postfix 的执行顺序由优先级决定，清场必须 Last**）；③死亡切换小队诊断（SetPlayer/死亡状态 5s 节流，发布前清理）
- v1.0.4：死亡流程诊断升级——`DeathPanel.ShowDeath(Soldier,string,bool)` Postfix 记录死亡瞬间"可切换的存活同阵营人数"（switchable=N 直接判定是列表空还是 UI 没出）；实测诊断发现死亡屏幕出现时 ControlledCharacter.IsDead 仍为 false（死亡状态机独立），SetPlayer 只在开局触发
- v1.0.5：飞起机制探针——①Ragdolize/AddForce 全量日志（0.5s 节流：方向/是否拦截/窗口状态）；②**玩家位移看门狗**（<0.15s 位移 >2.5m 记录"PLAYER MOVED FAST"）——任何机制（含游戏原生、物理、传送）导致的飞起都会被抓到；实证确认：防砸正常（0 debris hit）、友军邻近抑制生效（knocked=0）、死亡瞬间 switchable=14（列表有候选，UI 未显示 → 指向 UI 层问题，非数据问题）
- v1.0.6：**放置炸药（responsible=null）归属推断**——无归属爆炸按爆心 4m 内最近的活体士兵推断阵营，与玩家同阵营则建立友军上下文（之前 placed-TNT 无上下文→游戏原生击飞照发，解释"单独不会、有物品会"的谜团）
- v1.0.7：**爆炸"跳飞"拦截（用户实测定案：飞起=爆炸把跳跃系统初速调大，与物理化物品无关）**——Soldier 有完整跳跃系统（Jump()/OnJumpSynch(Vector3)/jumpVelocity/m_JumpPower/IsJumping），爆炸把 jumpVelocity 调大→士兵保持可操控地滞空飞行（"全队在天上正常走路，过一会才掉下来"）。新增三个拦截：`Soldier.Jump`(无参)、`OnJumpSynch(Vector3)`、`set_jumpVelocity`，友军爆炸窗口内同阵营士兵全部短路（敌军照常被炸飞）
- v1.0.9：**地面吸附确定性化**（用户实测 1.0.8 时灵时不灵——游戏战斗管理对击飞的施加时序不固定，单点 Update 夹持是竞态）——①`Soldier.Update` Postfix 士兵自身夹持 + `PlayerController.Update` Postfix 全量夹持，双路覆盖不同写入时序；②**离地位置兜底**：窗口内射线向下找地面，离地 >1.5m 直接插值拉回（无论发射是写速度还是改位置都按回地上）；③窗口 2.5s→4s。确认：plugins 无"战场管理"mod，击飞为游戏原生战斗管理行为
- v1.0.10：**"一动就飞"根因修复**（用户实测：爆炸后不动不飞、一移动立刻上天）——爆炸把 `m_JumpPower`/`jumpTimeEnd` 写成**待触发跳跃状态**（静止不消耗，移动时运动代码读 IsJumping() 应用初速）。`Soldier.Update` 改为 **Prefix 先清状态**（m_JumpPower=0 + jumpTimeEnd=0，在游戏 Update 读取前）+ Postfix 清速度 + 位置兜底，三管齐下
- v1.1.0：正式发布版（2026-08-18）——FF 拦截五层（AllowDamage/HitPart/OnHit/爆炸击飞/跳跃窗口/地面吸附）+ 弹药倍率定稿
- v1.2.0：**弹药覆盖扩展 + 战场调整**（2026-08-21）——①炮塔分类新增火炮/防空/自行火炮（`Vehicle.IsArtillery()/IsAA()/IsSPA()`）；②飞机炸弹无限（`VehiclePlane.DropBombs` Postfix + `RefillBombBay`）；③步兵武器弹药（**关键坑见下**）；④爆炸范围倍率；⑤步兵受击伤害倍率
- **v1.2.0 关键踩坑（步兵弹药入口）**：步兵开枪耗弹走 **`Magazine.ExtractOneBullet`/`ExtractBullets`**（弹匣），**不是** `GenericGun.ExtractOneBullet`（枪本体，CallerCount≈0，开枪不调用）也不是 `VirtualAmmo.ExtractOneBullet`（背包弹药，只在换弹时消耗）。诊断日志（节流 Diag）定位：Magazine 日志刷屏、GenericGun/VirtualAmmo 零日志。**教训：CallerCount 对判断 native 层调用不可靠**（native C++ 调用统计不到），但"入口选错"要先用日志实证再改，别猜
- **v1.2.0 关键踩坑（ref 参数）**：Harmony Prefix 用 `ref float damage`/`ref float explosionRadius` 修改值类型参数，在 IL2CPP 下**生效**（诊断日志 before=6 after=60 实证）。原理：Harmony patch 的是托管 interop 桩（不是 native 方法），ref 在托管层修改，桩再把改后的值传给 `il2cpp_runtime_invoke`
- **v1.2.0 关键踩坑（爆炸范围测试干扰）**：爆炸范围倍率一直生效，但用户测"没生效"——因为**友军免伤开启**时，玩家扔的手雷是"友军爆炸"，FF 拦截了它对友军士兵的伤害/击飞，观察"队友被炸范围"看不到变化。关闭友军免伤后正常。**教训：测试爆炸类功能要排除 FF 拦截的干扰，或用敌军做目标**
- **v1.2.0 关键踩坑（爆炸范围机制）**：爆炸实际范围由多层决定——`CreateExplosion.explosionRadius` 参数 + `Explosion.FULLDAMAGE_RADIUS_MULT`(默认1)/`FRAGMENTS_RADIUS_MULT`(默认2)/`CAM_SHAKE_RADIUS_MULT`(默认15) 静态字段 + `MatchData.Difficulty.explosionRadiusMultiplier`(难度乘数，默认1)。只改参数或只改静态字段都可能不够，需参数+静态字段同步改；难度乘数有成就副作用（`ExplosionRadiusAllowsAch`）不建议动
- 死亡列表缺失定案（2026-08-18）：**Hide Anything cfg 的 `hud=true`** 隐藏 `PlayerGUI.squadGUI_Panel`/`squadData_Panel` → 取消勾选后列表恢复 ✓（用户实测确认）
- 踩坑备忘：Harmony prefix 里 `return false` 跳过原方法即拦截（对 void 方法不需 __result）；GetFaction/GetVehicleFaction 属实例方法带 try/catch 兜底

### ER2_VeteranHVT（ER2 Veteran HVT：老兵高危目标标记 + 叛徒机制）
- 源码：`HighValueTarget/Plugin.cs`；版本：**1.1.26**（原名 ER2_HighValueTarget，v1.1.20 更名 ER2 Veteran HVT；双语 EN/CN，`build.ps1 -Mod HighValueTarget`）
- 2026-08-25 迭代要点：M 地图图标走"跟随游戏标记"（unitsContainer 匹配）、头顶标记固定深色/罗马数字居中/遮挡半透明、提示全改底部 toast、Hide Anything 联动（NoHintsHudLink）。详见《ER2_mod_经验.md》2026-08-25 会话总结。

### ER2_InventoryPause（ER2 Inventory Pause：背包暂停）
- 源码：`InventoryPause/Plugin.cs`；版本：**1.0.5**（双语 EN/CN，`build.ps1 -Mod InventoryPause`）
- 功能：打开背包（自己/尸体）时真暂停（延迟 timeScale 冻结，等打开动画完成）；暂停期间丢弃道具自动落地（扫描 ItemObject.spawnedItems）。暂停机制实测图谱见《ER2_mod_经验.md》2026-08-25 会话补充。
- 功能：**击杀归属**（子弹 OnHit Prefix / 爆炸 responsible / 近战上下文 / 头盔 ItemHelmet.User 兜底 + 玩家命中窗口 5s 防抢）+ **阈值标记**（敌 5 杀=HVT 橙标、友 2 杀=叛徒红标，阵营按 `_` 后缀判定，跨国家同盟算友军）+ **仇恨聚焦**（覆盖 `Soldier.GetBestVisibleEnemy`：仇恨方 AI 把标记单位当最佳可见敌人，走原生行为）+ **标记强化**（减伤 0.6/加伤 1.4/命中 1.7/射速 1.4/移速 1.15/无视压制/不投降）+ **叛徒机制**（玩家可伤友军 + 友军集火玩家，穿透 CombatTweaks 友军保护）+ **玩家提示**（Hint + 红屏 + 顶部占点条下方常驻指示）
- 关键 patch：`BulletInstance.OnHit`（归属 Prefix + FF 放行 900）/ `BodyPart.HitPart`+`TryDamageWithExplosion`（归属/减伤/放行）/ `Soldier.Kill`+`KillSynched`（计数）/ `Soldier.GetBestVisibleEnemy`（聚焦）/ `SoldierAI.ProcessAiAccuracy`+`CalculateNextShootDelay`+`OnSuppressed`（强化）/ `Soldier.Surrender`×2 / `Soldier.Melee` / `BattleManager.OnWin`（战斗重置）
- **关键踩坑（全部实测定案）**：
  1. **`Bullet.BulletDamage` 在本作不被调用**——入口必须实测（diag 日志验证），反编译有 ≠ 会调
  2. **归属必须 Prefix 记录**：瞬杀（爆头）的死亡发生在原生 OnHit 内部，Postfix 记录晚于 Kill 事件 → 击杀时查不到归属（"爆头不计数"根因）
  3. **FF 放行的阵营改写污染 SideOf 判定**：放行窗口内 shooter.faction=标记值 → 友军击杀被误判成敌方击杀（玩家被标成假 HVT 且仇恨方=友军）；定案：标记值=friendly 按构造判定，叛徒仇恨方=受害者阵营
  4. **ForceTarget 强制态三宗罪**：目标死后鞭尸、班长反复"标记地点"动作打断射击、AI 站桩不机动——弃用，改覆盖 `GetBestVisibleEnemy`（原生目标选择，无强制态）
  5. **头盔是独立于士兵层级的 BodyPart**：`GetComponentInParent<Creature>` 为 null → 用 `ItemHelmet.User` 拿所属士兵
  6. **IL2CPP 托管 MonoBehaviour 必须先 `ClassInjector.RegisterTypeInIl2Cpp<T>()` 再 AddComponent**（否则 MethodInfoStoreGeneric 静态构造异常，tick 全灭）
  7. 击杀归属表 25s 窗口 + CountedDeaths 防 Kill/KillSynched 双计；玩家死亡清空自身标记（重新做人）
- 配置：General（Enabled/ApplyInMultiplayer）/ Marking（KillsToMark=5/FriendlyKillsToMark=2/PlayerHitWindow=5s）/ Focus（FocusEnabled/FocusRadius=500/FocusChance 兼容保留）/ Buffs ×7 / Traitor.TraitorFeature / Visual ×3

### ~~ER2_SuperSoldiers~~（⚠️ 2026-08-24 已删除，用户测试前即删除；以下保留经验供"精英/难度增强"类 mod 参考）
- 源码：`SuperSoldiers/Plugin.cs`；版本 1.0.0（双语 EN/CN，`DefaultChinese` 编译期常量）——构建/部署/打包全部完成但未进游戏验证
- 功能（概率精英"各国超人"）：每个刷出的士兵**一生一次掷骰**（敌 8% / 友 3%，不分国家，`MaxActive=12` 全局存活上限防扎堆）→ 晋升后：受击减半（`DamageTakenMult` 0.5，HitPart/TryDamageWithExplosion 的 ref damage 缩放）+ 子弹/手雷 ×1.5（`Bullet.BulletDamage` 的 shooter / 爆炸的 responsible 归属）+ 命中 ×2（`ProcessAiAccuracy` static Postfix）+ 射速 ×1.5（`CalculateNextShootDelay` Postfix）+ 移速 ×1.15（`SoldierAI.speed` 字段维护回写，被 AI 覆盖则自动失效）+ 无视压制（`OnSuppressed` Prefix + `suppress_time_end` 周期清零）+ 绝不投降（`Surrender`/`SurrenderSynched` 双 Prefix）+ 头顶菱形标记（IMGUI 程序贴图，红=敌/绿=友）+ 刷新屏幕提示（`Corvostudio.UI.Hint.Display(text, 3.5f, true, true)`）
- 架构：`SuperSoldiersBehaviour : MonoBehaviour`（Load 里 `new GameObject` + `DontDestroyOnLoad` + `hideFlags=61` + `AddComponent`，陷阱 41 同源）0.7s `Time.unscaledTime` 节流遍历 `Creature.aliveCreatures`：`TryCast<Soldier>`（排除 `Target` 假人/玩家单位）→ 掷骰 → 晋升注册 `InstanceID→Soldier` 字典 → 死亡剪枝（不在 aliveCreatures 即移除，`_rolled` HashSet 同步剪）；敌我判定=玩家阵营字符串比较（玩家在载具时 `Vehicle.GetVehicleFaction()` 兜底）；联机门控 `PhotonNetwork.IsConnectedAndReady && !OfflineMode`（CombatTweaks 同款）
- 配置：General（Enabled/ApplyInMultiplayer）/ Spawn（Chance/AllyChance/MaxActive）/ Stats（DamageTakenMult/EliteDamageMult）/ AI（AccuracyMult/FireRateMult/SpeedMult/SuppressionImmune/NeverSurrender）/ Visual（ShowMarkers/ShowPromotionHint）——float/int 全带 `AcceptableValueRange`，ModManager 自动适配
- **可复用经验**：
  1. **AI 数值调参三钩子**（详见 §2"单兵 AI 数值调参"）：`ProcessAiAccuracy`（static 精度总线）/ `CalculateNextShootDelay` / `OnSuppressed`——改"命中率/射速/压制"不碰原生数值、不写血量
  2. **精英耐久别写 `life_total` 上限**（陷阱 6/10：伤害同步覆盖外部写入）→ 用"受击伤害倍率"（ref damage 缩放）保证抗揍；伤害输出同理走攻击方归属
  3. **晋升掷骰每士兵一生一次 + 全局存活上限**：周期巡逻若每 tick 重掷 → 全员晋升；无上限 → 超人扎堆破坏平衡
  4. **身份注册用 InstanceID→对象字典 + aliveCreatures 剪枝**（无组件开销、死亡自动回收）；遍历中逐单位 try/catch（单位可能被销毁）；`UnityEngine.Random.value` 掷骰
  5. **patch 优先级**：与 CombatTweaks 友军保护（`Priority.First`）错开——缩放伤害用 `Priority.Normal`，先短路友军再缩放
  6. 头顶标记定位：`Camera.main.WorldToScreenPoint(head)` + `Screen.height - y` 翻转 + `vp.z>0` 在镜头前才画
- 清理：源码目录、构建脚本条目、AGENTS.md 条目、部署 DLL/cfg、Downloads zip 已全部移除（以下经验条目保留）

### ~~ER2_BattlefieldHud~~（⚠️ 2026-08-16 会话末已删除：用户判定 mod 不重要；以下保留经验供后续 UI/音频 mod 参考）
- 源码：`BattlefieldUI/`（已删：Plugin.cs + BfAssets.cs + BfAudio.cs + BfEvents.cs + BfHud.cs + BfNativeUi.cs）；最后版本 1.0.2（双语 EN/CN）
- 功能（全部原生空白机会，实现可复用）：命中标记（HitMarkerFX Prefix 替换原生）、击杀归属（HitPart + Damage 判死 + CWT）、击杀卡/得分/军规血弹面板（读 PlayerGUI 文本）、受击/低血量反馈、9 种合成音效（WAV 自解码 + AudioClip.Create + 常驻 2D AudioSource）
- **最有价值的产出 = 陷阱 #41-45**（hideFlags 资源保护/IsPlayer 归属/Kill 事件/BulletDamage 数据源/IMGUI 绘制细节）+ `ER2_KillFeed_analysis.md`（参考 mod 完整分析 + 借鉴清单）
- 素材工作流（可复用）：`scripts/validate_assets.ps1` / `probe_png.ps1`（PNG chunk CRC 校验 + 像素采样工具，通用）；palette28 PNG 生成器随 mod 删除，需要时按 §4.1 重建
- 部署残留已清理（plugins DLL/Assets/cfg、Downloads zip 全部移除）；`research_out/bfui_research/`（Soldier/Creature/BodyPart/PlayerController/HitmarkerGUI/PlayerGUI/SoundManager/AudioClip 等反编译）与 `research_out/killfeed_ref/` 保留作 API 参考


### ~~ER2_BattlefieldHud~~（已删除 2026-08-16：游戏有原生准心，命中标记 mod 无意义；后被上方新 ER2_BattlefieldHud 取代）
- 源码/部署/打包全部移除；保留以下**通用引擎教训**（对任何未来 IMGUI UI mod 有效）：
  1. **IMGUI 程序贴图唯一可靠路径 = palette28 调色板 PNG**（28 项 PLTE + tRNS 27 字节，与 game-icons 图标同构，LoadImage 解码正确）；`SetPixels32`/`SetPixels` GPU 上传不可靠、RGBA PNG 含 alpha=0 像素渲染成白块、`GetPixel` 读回对透明贴图返回垃圾值（三项均实测）
  2. **`GUI.color` 纯黑 (0,0,0) 被引擎剔除**——描边 tint 用深灰 (0.1~0.15)
  3. **`GUIUtility.RotateAroundPivot` 不可靠**——旋转绘制从未渲染；需要旋转的形状（刻度/弧线）把角度**烘焙进贴图内容**（像素级旋转 + 角度分桶缓存）
  4. **诊断方法论**：F9 预览常驻 + ScreenCapture 自动截图 → 像素射线扫描（注意白云背景污染白色测量）→ 识图模型交叉验证（模型对大小/位置会幻觉，结论必须像素复核）
  5. 事件数据层（击杀归属/击杀信息流/击杀卡片）全部可靠可用，可复用：`Bullet.BulletDamage`、`BodyPart.HitPart`/`TryDamageWithExplosion`、`Soldier.Damage`/`Kill`/`Creature.KillSynched`、`HitmarkerGUI.HitMarkerFX`、`ChatMessage.NewKilledMessage`、`ConditionalWeakTable` 归属追踪

### 其他（用户自制，非本工作区源码）
- ER2_WorldHUD、ER2_Fire_Coaxial_Spacebar、ER2_MeleeTweaks、ER2_RecoilOverhaul（内含 Universal Recoil Control，GUID UniversalRecoilControl）、BloodWorks 血迹包（ER2_FleshWoundsBW / ER2_ImpactFXBW / TRCompatBW / ReactiveRagdollBW / RemoveStainsBW）等（仅部署 DLL）

### 参考：ER2 KillFeed+（他人成品，反编译研究 2026-08-16）
- 来源：`Downloads\ER2 KillFeed+ V1.18.2B(高战地化).rar`（GUID com.lwb.er2.killfeed）；反编译在 `research_out/killfeed_ref/`
- 完整分析：`ER2_KillFeed_analysis.md`（功能清单/机制/差距表/借鉴清单）
- 一句话：**"高战地化" = 击杀信息流 + BF1 击杀卡（毛玻璃）+ 击倒流 + 载具链路 + 小队/小地图/目标指示/计分板 + 自定义音效音乐语音**，
  60+ 配置项；技术核心是 `KillFeedUI : MonoBehaviour`（引擎回调 OnGUI/Update）+ `FeedManager` 归属表 + 全链路 patch + hideFlags 资源保护
- 已沉淀到本指南的陷阱：41（hideFlags）、42（IsPlayer 归属）、43（Kill 事件）、44（BulletDamage 数据源）、45（IMGUI 绘制细节）

### 文档
- `ER2_UI_design.md` — 游戏 UI/界面机制研究（ModManager 用，含 §8 设计细节、§9 HideAnything 研究补充）
- `ER2_KillFeed_analysis.md` — KillFeed+ 参考 mod 完整分析（功能/机制/差距/借鉴清单）
- `ER2_scene_objects_classification.md` — **场景物体分类体系解包报告**（manifest 目录分类 + PropData.PropType/Category 官方枚举 + MapProp→PropData→MapPropReference 运行时三层结构 + ItemObject 类层级 + mod 运行时分类决策树；2026-08-18 产出）
- `ER2_physics_system.md` — **物理系统解包报告**（引擎配置：重力 -9.81/50Hz/solver 6-1/queriesHitTriggers；16 层碰撞矩阵解码：bullet 穿隐形墙、item 穿墙、InvisibleWall 只挡载具等；物理载体分布：士兵 CharacterController/AI NavMesh/载具 Rigidbody/布娃娃原生 LOD/弹道射线结算；优化评估：尸体物理预算=性价比最高，mod 查询纪律，不建议动引擎参数；2026-08-18 产出）
- `ER2_mod_dev_guide.md` — 本文件（工作流/机制/陷阱/状态总览）


### 临时/研究目录（可清理）
- `tmp_nointeract/`（rar 解压临时，已空可删）、`throwable_research/`（投掷物体系研究）、`ui_research/`（UI 机制研究，含全部反编译产物）、`tmp_hudcompat_test/`（契约逻辑测试宿主，**保留**，§3.6 引用）

## 4.5 配置化设计原则（ModManager 时代新增）

**应该做成配置项（ConfigEntry）的**：
1. 数值参数：阈值、距离、间隔、伤害量（如 damageNeeded / showDistance / checkInterval）
2. 功能开关：总开关、子功能开关、作用对象开关（如 bleedEnabled / affectsPlayer / corpseEnabled）
3. 按键：所有热键必须配置化（AcceptableValueList<string> 枚举 F 键 + None）—— WeatherControl.hotSwitchKey ✓、NoInteractionHints.toggleKey ✓（1.8.0 起）
4. 有界选项：天气/氛围等枚举（AcceptableValueList）
5. 配置描述（description）写清楚含义与单位 —— ModManager 中文版会显示为小字介绍

**不应该做成配置项的**：
1. 游戏机制性常量：流血频率、伤害倍率等平衡核心（做成配置 = 破坏设计意图 + 支持负担爆炸）
2. 影响存档/状态一致性的（改动后旧存档崩溃类）
3. 依赖 IL2CPP 静态结构的（patch 目标类型/字段名 —— 运行时改不了）
4. 单次初始化不可重载的（字体/资源加载路径）
5. 需要每帧高性能读取的（配置读取有装箱开销，热路径字段要缓存）

**热生效原则**：配置项应尽量在运行时每帧/每 tick 读 `.Value`（ConfigEntry.Value 是热读取），
这样 ModManager 修改立即生效；仅在 Load 时读一次的需要标注"重启生效"。

## 5. 部署/发布流程

1. 编译：`dotnet build -c Release <proj>.csproj`（CN 版加 `-p:DefineConstants=CN_BUILD`）
2. 部署：`powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字>`（构建+部署+清cfg+打包一体；`-SkipDeploy` 只打包；**中文版加 `-Cn` 开关**：编译 CN + 部署 + 打包 `_CN_` 命名 zip，一条命令完成，无需手动复制轮询）
3. 发布包：`C:\Users\71011\Downloads\<ModName>_v<版本>.zip`（zip = DLL + README.txt + Nexus_description.md；双语 mod 出 4 个包：EN/CN × 2）
4. 发布简介按 N 网格式：Description / Installation instructions / Main features / Requirements / Shout outs
5. 发布前清理调试/诊断日志；启动日志版本字符串与 BepInPlugin 一致
6. **发布前清理清单**（本次会话经验）：
   - 去掉高频诊断：插件列表 diag、页面高度、每次打开的日志、物品探测 PROBE、tick 状态行
   - 限频保留：补货日志、saved cfg、staged（仅非数值类型）、重置日志
   - 版本号 z+1（含启动日志字符串）；打包后核对 zip 内 DLL 版本
   - 部署版 = 发布版（发布前把 CN/EN 切换干净）

## 6.15 发布状态（MorePhysics 0.10.0 LOD 虚影修复）

**MorePhysics 0.10.0**（用户反馈两连）：
- **虚影再修**：`DisableLodSiblings`——家具是 LOD 多级对象（日志实证 `Table_01_LOD0`）：把 LOD0 物理化打飞后，同名的 LOD1/LOD2 兄弟仍静态渲染 = 原地虚影。物理化时按去 LOD 后缀的基名禁用全部兄弟 Renderer
- **桌上物品悬空修复**：tick 的 `EnablePhysic()` 产物是 kinematic 刚体（0.4.0"打不飞枪"同源）——桌子被掀翻后物品不落地。tick 里强制 `isKinematic=false + useGravity=true`
- 发布包：`ER2_MorePhysics_v0.10.0.zip`（ModManager 无改动，仍 1.0.59）

## 6.14 发布状态（MorePhysics 0.9.0 幽灵尸体版）

**MorePhysics 0.9.0**（用户定案四连）：
- **虚影修复**：物理化改 `UnstaticUpDown`（向上级联去静态化到根 + 向下递归——父级残留静态渲染是虚影根因）
- **炸尸体 = 幽灵尸体**：`GhostCorpse` 替换炸肢方案——碰撞体全转 trigger → 穿透一切 → 掉出地图（y<-80 或 20s 销毁，GhostBody 组件）；威力门槛 CorpseBlastMinPower 不变
- **删除爆炸碎屑系统**（ExplosionDebris 组件删除）：爆炸只物理化推倒物体（无碎屑、无删除、无 trigger）；高威力（≥LightDebrisMinPower）冲击 ×1.6
- **速度伤害只属于飞行物品**：KnockedItem 伤害改为 速度×DebrisSpeedDamage（删除固定 PhysicsDamage 配置）
- **威力数值全部保持用户调好的值**：PhysicizeMinPower/LightDebrisMinPower/HeavyDebrisMinPower/CorpseBlastMinPower/DebrisSpeedDamage/ItemKnockForce 键与默认值一律不动
- 配置：删除 PhysicsDamage（死配置）；ModManager 1.0.59
- 发布包：`ER2_MorePhysics_v0.9.0.zip`、`ER2_ModManager_v1.0.59.zip`+CN

## 6.13 发布状态（MorePhysics 0.8.0 物理化中间档）

**MorePhysics 0.8.0**（用户反馈四连调）：
- **手雷=物理化不炸毁**：新增 `PhysicizeMinPower`（默认 40）中间档——威力达标但未达炸毁门槛的爆炸把物体**推倒（刚体+径向冲击，保留实体碰撞）**；`LightDebrisMinPower` 默认 80→200（手雷级不再碎屑化轻家具）
- **枪冲击力减半**：ItemKnockForce 默认 5→2.5，威力系数 1+power/25 → 1+power/60
- **尸体炸躯干**：`BlastCorpse` 重写——躯干（chest）必炸 + 随机肢体/头，按距离 3-6 肢
- **发射方向真实化**：`BlastDirection` 径向为主（dir.y = y×0.3+0.2），不再把物品都炸上天（碎屑化与物理化共用）
- 威力分级链：<40 不动 → ≥40 物理化（手雷）→ ≥200 轻家具碎屑化（TNT）→ ≥350 重型碎屑化 → ≥300 尸体炸肢（含躯干）→ 建筑原生
- 配置新增 PhysicizeMinPower(0-2000,40)；ModManager 1.0.58
- 发布包：`ER2_MorePhysics_v0.8.0.zip`、`ER2_ModManager_v1.0.58.zip`+CN

## 6.12 发布状态（MorePhysics 0.7.0 尸体重度炸毁）

**MorePhysics 0.7.0**（用户定案：让尸体可以被重度炸毁）：
- 爆炸威力 ≥ `CorpseBlastMinPower`（默认 300，TNT 级；手雷达不到）时，范围内尸体按落点距离炸飞 2-4 条肢体（`BlastCorpse`：DetachLimb 随机肢体，爆心极近 50% 概率连头共 5 肢）；同一尸体每次爆炸只处理一次（GetInstanceID 去重）
- 威力分级链完整：小掉落物（无条件删）→ 轻家具（≥80 碎屑化）→ 重型沙袋/路障（≥350 碎屑化）→ 尸体（≥300 炸肢）→ 建筑（原生动画）
- 配置新增 CorpseBlastMinPower(0-5000,300)；ModManager 1.0.57
- 发布包：`ER2_MorePhysics_v0.7.0.zip`、`ER2_ModManager_v1.0.57.zip`+CN

## 6.11 发布状态（MorePhysics 0.6.0 威力分级版）

**MorePhysics 0.6.0**（用户定案：爆炸威力分级——手雷炸不坏沙袋、炸药可以）：
- **威力 = CreateExplosion 的 explosionMaxDamage**；物体按韧性分两档：
  - 轻家具/杂物：`LightDebrisMinPower`（默认 80，手雷通常高于此）→ 达不到则完好保留
  - 重型（名称含 sandbag/barricade）：`HeavyDebrisMinPower`（默认 350，手雷达不到、TNT/爆破筒可以）
- 破坏优先级不变：有原生破坏效果（DestructableBuilding）走原生动画；没有的走我们的碎屑化/删除
- 小掉落物（ItemObject）仍无条件删除；配置新增 LightDebrisMinPower(0-2000,80)/HeavyDebrisMinPower(0-5000,350)；ModManager 1.0.56
- 发布包：`ER2_MorePhysics_v0.6.0.zip`、`ER2_ModManager_v1.0.56.zip`+CN

## 6.10 发布状态（MorePhysics 0.5.0 碎屑化版）

**MorePhysics 0.5.0**（用户定案：断臂可打飞、枪打不远修复、家具爆炸碎屑化）：
- **打飞判定放宽**：`IsKnockable` = 松散小物体（有碰撞体、bounds.extents ≤1.5m、非建筑/载具/地形/特效/排除词）——**断臂等非 ItemObject 小物体也能被子弹/近战打飞**
- **打不远修复**：`KnockItem` 改用 `GetComponentInChildren<Rigidbody>`（子级刚体优先，解决 tick 给子级挂 rb 导致根刚体空推）+ `AddForceAtPosition`（命中点施力带旋转）+ 威力系数 1+power/25、默认力度 5
- **家具爆炸碎屑化** `ExplosionDebris`（ClassInjector 注册）：爆炸范围内非物品/非建筑的场景物 → 物理化+**全部碰撞体转 trigger**（物理不阻挡、只产生触发）→ 发射（falloff×maxDmg×0.2，钳 5-50）→ **撞到单位：消失 + 速度×DebrisSpeedDamage(默认1) 伤害** → 无碰撞：穿透一切（重力下落）掉出地图（y<-80 或 20s 兜底销毁）；同物去重
- 爆炸仍删除小掉落物（ItemObject）；DestructableBuilding 走原生动画不插手；配置新增 DebrisSpeedDamage(0-5,1)；ModManager 1.0.55
- 发布包：`ER2_MorePhysics_v0.5.0.zip`、`ER2_ModManager_v1.0.55.zip`+CN

## 6.9 发布状态（MorePhysics 0.4.0 真实性版）

**MorePhysics 0.4.0**（用户定案：爆炸只删小掉落物；大场景破坏走游戏原生动画——反编译确认 `DestructableBuilding.Damage/DestroyBuilding` 原生破坏管线，mod 不插手；子弹/近战恢复"打飞物品"）：
- **爆炸**：`DestroyLooseItem`（只删世界中的 ItemObject 小掉落物，effR + 同点去重）；大场景（DestructableBuilding）由游戏原生爆炸破坏动画处理
- **子弹**：命中松散物品 → 物理化+冲击（`KnockItem`）；**冲击力与弹丸威力挂钩**（penetrationDamage + explosionDamage + ActualSpeed×0.08，force = ItemKnockForce × (1+power/40)）
- **近战**：挥击 2.2m/120° 内松散物品打飞（force ×1.5）
- **真实性新增**：`KnockedItem` 组件（ClassInjector 注册）——被击飞的物品撞到士兵造成 `PhysicsDamage`（默认 5）小伤害并立刻停下；物品 tick 物理化保留
- 配置精简：删 8 个死配置（EnableScenePhysics/EnableNoColliderObjects/ExplosionOnlyHardObjects/5 分类开关），新增 ItemKnockForce(0-20,4)/PhysicsDamage(0-50,5)；ModManager 词典同步（1.0.54）
- 发布包：`ER2_MorePhysics_v0.4.0.zip`、`ER2_ModManager_v1.0.54.zip`+CN

## 6.8 发布状态（MorePhysics 0.3.0 第三版设计）

**MorePhysics 0.3.0**（用户定案：彻底放弃"物理化打飞"——下沉 bug + 打不飞枪）：
- **删除血量系统**：DefaultHealth/MeleeDamage/BulletDamageMultiplier/BulletKnockback/MeleeKnockback/DespawnTime/PhysicsDamage 全部移除（ModManager 词典同步清理，1.0.53）
- **任何命中（子弹/近战/爆炸）→ 可物理化对象直接摧毁（删除）**：`DestroyPhysicalObject(go, fromExplosion)`（无血量、无 HP 门槛）；爆炸无视硬质门控（fromExplosion=true），子弹/近战受 ExplosionOnlyHardObjects 门控；EnableScenePhysics 门控非 ItemObject；ItemObject 也摧毁（用户要"打不飞的枪→删除"）
- **物理化只保留物品自动 tick**（"检查状态是否改变，改变后赋予物理化"）：`ItemObject.spawnedItems` 每 2s 检查未物理化物品 → EnablePhysic()（掉落物自然受重力）
- 移除：GetOrAddRigidbody/FromRenderer/AttachPhysicsDebris/UnstaticRecursive/PhysicsDebris/StripDecals/DamageSceneObject/sceneHp/DamageSource/ExplosionDamageMultiplier/ExplosionKnockback 全部删除；保留 FindPhysicalRoot（整栋摧毁）、分类/排除词/去重/半径倍率
- 发布包：`ER2_MorePhysics_v0.3.0.zip`、`ER2_ModManager_v1.0.53.zip`+CN

## 6.7 发布状态（HideAnything 死亡列表豁免 + MorePhysics 0.2.0 重设计）

**MorePhysics 0.2.0**（用户定案：爆炸=直接摧毁环境，只有枪械/近战物理化）：
- 爆炸路径重写：`ExplosionImpactPatch` 不再物理化/发射——范围内物理化物体（按分类+排除词）**直接 Destroy**（`DestroyEnvironmentObject`，爬到 FindPhysicalRoot 整栋摧毁；ItemObject 可拾取物品保留不毁）；Renderers 路径同名处理；保留同点去重 + ExplosionPhysicsRadiusScale
- 子弹/近战路径不变（hp→物理化→打飞），**移除 OverlapsCreature 拒绝**（修复"掉在地上的东西打不飞"——物品在玩家脚下与胶囊重叠被拒物理化）；防砸急停/碰撞即停保留
- 移除：与 CombatTweaks 的反射联动（IsFriendlyBlastActive/FriendlyBlastNearSoldier/MarkFriendlyLaunched/OverlapsCreature 全部删除）、ExplosionDamageMultiplier/ExplosionKnockback 配置（ModManager 词典同步清理，1.0.52）
- 发布包：`ER2_MorePhysics_v0.2.0.zip`、`ER2_HideAnything_v4.5.1.zip`、`ER2_ModManager_v1.0.52.zip`+CN

**HideAnything 4.5.1**：`UiHiders.SetPlayerHud/RehidePlayerHud` 新增 `IsDeathScreenActive()` 守卫（DeathPanel 激活或受控角色 IsDead）——死亡界面期间不再隐藏 squadGUI_Panel/squadData_Panel，hud 勾选时死亡后仍能选择其他小队成员

## 6.6 发布状态（Combat Tweaks 正式发布）

**CombatTweaks 1.1.0 发布版**（2026-08-18）：
- 清理：撤掉 DisableAllJumps 实验开关（用户定案"不搞了"）；删除全部 CT 诊断补丁（SetPlayer/DeathState/DeathPanel/Ragdolize 日志）；MorePhysics 只删诊断日志不动逻辑（EXP diag/knocked/slowed/suppressed/stripped/debris hit 全清）
- 加固：爆炸归属推断在 responsible 阵营为空时同样执行（覆盖放置炸药场景——1.0.10"时灵时不灵"的疑似缺口）
- 最终机制：FF 拦截（AllowDamage/HitPart/OnHit/爆炸击飞/跳跃窗口/地面吸附五层）+ 弹药倍率；MP 0.1.16（半径倍率/去重/防砸急停/重叠不物理化/灰迹剥离/特效排除/友军不发射）
- 发布包：`ER2_CombatTweaks_v1.1.0.zip`+CN、`ER2_ModManager_v1.0.51.zip`+CN（词典移除 DisableAllJumps 残留）、`ER2_MorePhysics_v0.1.16.zip`
- 已知边界（用户确认可接受）：爆炸对玩家仍可能偶发"跳飞"（游戏原生战斗管理行为，物理化物品场景已由重叠检测根治；脚下炸弹由窗口抑制）

## 6.5 发布状态（Combat Tweaks 会话）

| Mod | 版本 | 变更 | 运行态 |
|---|---|---|---|
| ER2 Combat Tweaks（新） | 1.0.1 | 单机关友军伤害 + 火力点/坦克弹药倍率与无限弹药；v1.0.1 拦截优先级 First（修 LimbTweaks 联动断肢）+ 倍率上限 10000 | 已部署（待用户实测） |
| ER2 Mod Manager | 1.0.48 | 数值输入框与标签重叠修复 | 已部署（待用户实测） |
| ER2 Combat Tweaks | 1.0.10 | "一动就飞"根因：Prefix 清待触发跳跃状态（m_JumpPower/jumpTimeEnd） | 已部署（待用户实测） |
| ER2 More Physics | 0.1.15 | 与生物重叠物体不物理化/不发射（物品通道根因） | 已部署（待用户实测） |
| ER2 More Physics | 0.1.10 | 防砸检查加密（0.05s/2.5m） | 已部署 |
| ER2 Hide Anything | 4.5.0（用户配置） | **死亡列表缺失根因：cfg hud=true 隐藏 squadGUI/squadData 面板**（需用户取消勾选） | 已定位 |
| ER2 Mod Manager | 1.0.49 | 数字输入行改双行布局（标签/输入框/范围提示各占一行） | 已部署 |
| 第三方 | ER2 Leave & Redeploy 0.1.0 | 疑似接管死亡时的小队选择流程（配合原生 Creature.OnDestroy NRE，需隔离验证） | — |

- 发布包：`ER2_CombatTweaks_v1.0.0.zip` + `ER2_CombatTweaks_CN_v1.0.0.zip`、`ER2_ModManager_v1.0.46.zip` + CN
- 词典内部重复校验通过（modNames 31 / keys 159 / descriptions 159 / sections 45，check_mm_dup.ps1）

## 6. 发布状态（2026-08-16 会话末）

| Mod | 版本 | 发布包（Downloads） | 运行态 |
|---|---|---|---|
| ~~ER2 Battlefield HUD~~（新，BattlefieldUI/） | 已删除 | 已删除（2026-08-16 会话末：用户判定不重要；部署残留已清理，经验沉淀在 §4 该条目 + 陷阱 #41-45 + ER2_KillFeed_analysis.md） | — |
| ~~ER2 Battlefield HUD~~（旧） | 已删除 | 已删除（2026-08-16：游戏有原生准心，命中标记 mod 无意义；通用引擎教训保留在 §4 该条目） | — |
| ER2 Throwable Wheel | 1.3.2 | v1.3.2.zip + CN v1.3.2.zip（含 DeepSeek 致谢） | CN |
| ER2 Mod Manager | 1.0.21 | v1.0.21.zip + CN v1.0.21.zip | CN |
| BloodWorks 血迹包 | 1.0.2 | 用户 zip，已装载（FleshWoundsBW/ImpactFXBW/TRCompatBW/ReactiveRagdollBW/RemoveStainsBW） | 已装 |
| 其余自研 mod | 未动 | — | — |

- 中文版适配：16 个 mod、98 个配置项（modNames/keys/descriptions 词典）
- 未发布但已部署：LimbTweaks 2.13.99 / WeatherControl 1.7.0 / HealthBars 1.1.1 / AIFood 1.4.0 / NoInteractionHints 1.8.1（上次会话的发布版本）

## 6.2 发布状态（Hide Anything v4 会话）

| Mod | 版本 | 变更 | 运行态 |
|---|---|---|---|
| ER2 Hide Anything（原 No Interaction Hints / Hide Everything） | **4.3.0** | 勾选制（勾选=隐藏且严格生效，无热键无锁定按钮）；13 类原生 UI + 逐-mod 开关；地图与小地图合并；通知即时清屏；自动保存 | 已部署，实测通过（含局内滚动修复确认：scrollbar 0.00 到底） |
| ER2 Mod Manager | **1.0.32** | 无保存按钮（看门狗自动保存）；分区小标题默认收起；局内滚动修复（Time.unscaledTime + ScrollRect.content 锚点修正） | 已部署（截图用 EN 版，发布后可按需换回 CN） |
| ER2 Limb Tweaks / Weather Control / Health Bars | 2.13.100 / 1.7.1 / 1.1.2 | 未动（v2 契约兼容） | 已部署 |

- 逻辑测试 31/31；发布包：`ER2_HideAnything_v4.3.0.zip`、`ER2_ModManager_v1.0.32.zip`、`ER2_ModManager_CN_v1.0.32.zip`
- 关键踩坑（本会话新增，见 §3 陷阱 36~40）：局内暂停 Time.time 冻结；滚动范围由 ScrollRect.content 决定；interop 缺失生命周期方法不能 patch；字符串搜索不能验证构建语言；共享词典两种构建都有
- 截图：ModManager 切 EN 版供 N 网发布截图（语言一致性，陷阱 33）；截图由用户自取

## 6.3 会话经验总结（Hide Anything v4 全流程）

**产品/交互设计演进（用户驱动，每轮实测反馈迭代）**：
1. F5 热键开关 → 每类独立开关 → 勾选制（勾选=隐藏且严格生效）：需求从"一键隐藏"演化为"常驻可配置的隐藏清单"；
   最终形态去掉了热键、锁定按钮、保存按钮三个概念——每个概念的存废都来自实际使用体验（锁定=严格隐藏合并进勾选；
   保存按钮被"退出即保存"取代），**功能减法比加法更难也更重要**
2. 地图与小地图：用户视角"都是地图"→ 合并为一个开关（内部仍分别控制两个不同 UI 类）
3. 状态同步语义：勾选是"状态变量"——未锁定时外部重新显示会取消勾选（后来因简化改为勾选即锁定）

**技术验证方法论**：
1. **逻辑测试宿主先行**（tmp_hudcompat_test/）：纯 .NET 编译生产源码，契约/注册表/巡检逻辑 31 项断言，
   改契约先跑测试再部署——IL2CPP 运行时问题（加载、patch）与纯逻辑问题分层隔离
2. **诊断日志三件套**：目标找不到（"X target not found"）、动作执行（"X hidden"）、数值状态（scroll state）——
   每次"不生效"都能从日志定位是"没找到目标/没执行/执行了但无效"三层中的哪一层
3. **反编译确认再动手**：interop 方法/字段清单先行（ilspycmd -t），避免 patch 不存在的目标（陷阱 38）
4. **部署验证闭环**：哈希校验（bin==部署）、启动日志版本串、cfg 落盘检查、用户实测日志回读

## 6.1 发布状态（NoInteractionHints v2 会话）

| Mod | 版本 | 变更 | 运行态 |
|---|---|---|---|
| ER2 No Interaction Hints | 2.0.0 | v2 跨 mod UI 隐藏契约（HudCompat.IsHudHidden + 惯例字段自动发现 + 逐 mod 开关 + 已装生态 mod 预注册） | 已部署（最终版，含预注册） |
| ER2 Limb Tweaks | 2.13.100 | 提示联动迁移到 v2 契约 | 已部署 |
| ER2 Weather Control | 1.7.1 | 提示联动迁移到 v2 契约 | 已部署 |
| ER2 Health Bars | 1.1.2 | 新增 F5 隐藏联动（默认隐藏，可逐 mod 关） | 已部署 |
| ER2 Mod Manager | 1.0.22 | CN 词典新增逐 mod 隐藏项映射 | 已部署（CN） |

- 实测（用户会话，日志确认）：F5 多轮切换正常、ModManager 正常、逐 mod 开关自动注册（er2.healthbars 已落盘）、零错误
- 契约逻辑 24/24 测试通过（tmp_hudcompat_test/，编译生产同源代码）
- 发布包 6 个已生成并验证（Downloads\*_v*.zip：NoInteractionHints v2.0.0 / LimbTweaks v2.13.100 / WeatherControl v1.7.1 / HealthBars v1.1.2 / ModManager v1.0.22 EN+CN）
