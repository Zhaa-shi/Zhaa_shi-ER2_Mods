# ER2_Mods 项目台账（全 mod 现状 · 版本 · 发布状态）

> **单一事实源**。版本号一律以本文件为准（核对日期 **2026-09-06**，对应 git `1eccc5b`）。
> 文档与代码不一致时**以代码为准**，并回来修本文件。
> 复核命令：见文末「§5 台账自检」。

## 1. 原创 mod 一览（16 个目录 / 15 个发布 mod + 1 个内部工具）

| # | 目录 | GUID | 显示名 | 版本 | DLL | 部署状态 |
|---|---|---|---|---|---|---|
| 1 | `LimbTweaks` | `er2.limbtweaks` | ER2 Limb Tweaks | **2.13.101** | `ER2_LimbTweaks.dll` | 已部署 |
| 2 | `WeatherControl` | `er2.weathercontrol` | ER2 Weather Control | **1.7.2** | `ER2_WeatherControl.dll` | 已部署 |
| 3 | `AIFood` | `er2.aifood` | ER2 AI Food | **1.4.0** | `ER2_AIFood.dll` | 已部署 |
| 4 | `NoInteractionHints` | `com.ryan.er2.nointeractionhints` | ER2 Hide Anything | **4.5.4** | `ER2_NoInteractionHints_DoneProMaxEnd.dll` | 已部署 |
| 5 | `ModManager` | `er2.modmanager` | ER2 Mod Manager | **1.2.2** | `ER2_ModManager.dll` | 已部署 |
| 6 | `ThrowableWheel` | `er2.throwablewheel` | ER2 Throwable Wheel | **1.3.6** | `ER2_ThrowableWheel.dll` | 已部署 |
| 7 | `CombatTweaks` | `er2.combattweaks` | ER2 Combat Tweaks | **1.2.2** | `ER2_CombatTweaks.dll` | 已部署 |
| 8 | `ZoomAnywhere` | `er2.zoomanywhere` | ER2 Zoom Anywhere | **1.0.1** | `ER2_ZoomAnywhere.dll` | 已部署 |
| 9 | `HighValueTarget` | `er2.highvaluetarget` | ER2 Veteran HVT | **1.1.26** | `ER2_VeteranHVT.dll` | 已部署（+ Assets 目录） |
| 10 | `InventoryPause` | `er2.inventorypause` | ER2 Inventory Pause | **1.0.5** | `ER2_InventoryPause.dll` | 已部署 |
| 11 | `SquadCommand` | `er2.squadcommand` | ER2 Battlefield Commander | **1.1.0** | `ER2_BattlefieldCommander.dll` | 已部署 |
| 12 | `UniversalGeneration` | `er2.universalgeneration` | ER2 Universal Generation | **1.0.0** | `ER2_UniversalGeneration.dll` | 已部署 |
| 13 | `UnitCollision` | `er2.morephysics.unitcollision` | ER2 More Physics - Unit Collision | **1.0.6** | `ER2_MorePhysics_UnitCollision.dll` | 已部署 |
| 14 | `UnitInfoOverlay` | `er2.unitinfooverlay` | ER2 Unit Inspector | **1.0.5** | `ER2_UnitInfoOverlay.dll` | 已部署 |
| 15 | `FleshWoundsFixed` | `ER2_FleshWounds` | ER2 Flesh Wounds | **1.0.1** | （需手动构建部署，build.ps1 无条目） | 第三方修复版 |
| — | `HvtTestDriver` | `er2.hvt.testdriver` | HVT Test Driver | **1.0.0** | — | **内部自测工具，不发布** |

**注意**：`NoInteractionHints` 的 DLL 名 `ER2_NoInteractionHints_DoneProMaxEnd.dll` 与显示名 "Hide Anything" 完全不同——**DLL 名 ≠ 插件名**，按 GUID 识别。`HighValueTarget` 目录产出 `ER2_VeteranHVT.dll`（v1.1.20 起改名 "Veteran HVT"），旧发布包名 `ER2_HighValueTarget_*` 是历史遗留。

## 2. 各 mod 功能与关键机制

### 2.1 LimbTweaks `er2.limbtweaks` v2.13.101
肢体断肢 + 流血系统。活体断肢（累积 60 伤害）、流血分级（断肢 7/s、玩家 3.5/s、非断肢 4/s）、绷带递减疗效（4/s→1/s→无效）、断腿锁卧倒、断右臂投降+10s 流血死、断臂行为限制（换弹/捡枪/投掷/绷带）、四肢伤害减半、尸体断肢（伤害+初速×0.08 ≥80）、禁止断肢单位回血（拦 `SetBleeding(false)`）、屏幕提示走原生 Hint 通道。
**patch 面**：`HitPart` / `BulletOnHit` / `BattleStartReset` / `Ragdolize`×2 / `SetBleeding` / `UseBandages` / `PickUp` / `SwitchTo` / `Reload`×2 / `Throw` / `UnSurrender`×2 / `SetPose` / `SetPlayer` / `BleedTick` / `NotifyGui` / `NativeDamageControl`（`Creature.Damage`）/ `LimbMultiplier`（`GetBodyPartMultiplier`）/ `SetBleedingTrack`。
**配置**：`General.enabled` / `Corpse Shooting.corpseEnabled` / `Living Soldiers.aliveEnabled` / `Bleeding.bleedEnabled` / `Bleeding.affectsPlayer` / `Limbs.damageNeeded` / `Limbs.corpseDamageThreshold`。

### 2.2 WeatherControl `er2.weathercontrol` v1.7.2
每局强制天气（Clear/Rain/Snow）+ 时间氛围（Midday/Sunset/Dawn/Night/Cloudy/Foggy，走 `SetDayTime`）、热键切换组合、**拦截战役天气重置**（patch `set_WeatherType` / `SetDayTime`）、原生 Hint 提示。

### 2.3 AIFood `er2.aifood` v1.4.0
AI 血量低于 `eatBelowHp`（默认 **40**）自动吃背包食物回血：`FindItemOfType<VirtualRecoverLife>()`（**返回条目必须 `TryCast`**）+ `Creature.RecoverLife(vre)`（原生吃+回血+消耗+动画），`checkInterval` 默认 **2s** 轮询，跳过玩家。

### 2.4 NoInteractionHints（Hide Anything）`com.ryan.er2.nointeractionhints` v4.5.4
**勾选制** UI 隐藏（无热键、无锁定按钮）：**14 类原生 UI**（`hints` / `notifications` / `objectiveBanner` / `hud` / `phaseBar` / `objectives` / `map` / `vehicle` / `misc` / `hitmarker` / `bloodSplash` / `chat` / `scope` / `worldMarkers`）+ 逐 mod 开关；勾选 = 始终隐藏且严格生效（Enforce 持续重藏防游戏重新显示）；地图与小地图合并为一类；通知弹窗即时清屏；修改自动保存（0.8s 看门狗落盘）。
**源码**：`Plugin.cs` + `HudCompat.cs` + `UiGroups.cs` + `UiHiders.cs` + `UiPatches.cs` + `RehidePatches.cs`。
**关键机制**：`UiGroups` 注册表（Register/IsHidden/ApplyAll/Enforce）；`UiHiders`（ElementHider 记忆式隐藏 + RefCache 5s 冷却）；所有节流用 `Time.unscaledTime`。
**v4.5.3+ 游戏 2.1.x 兼容**：`FindGameType("PhaseBarGUI")` 运行时探测，类型缺失时该类别跳过（不再 TypeLoadException）。

### 2.5 ModManager `er2.modmanager` v1.2.2
把 mod 设置页**嵌进游戏原生设置界面**（`SettingsGUI_V2.Update` / `SettingsTabRight` / `SettingsTabLeft` patch）：自动枚举 `IL2CPPChainloader.Plugins` 渲染全部配置项（折叠分区 + 小字简介直接用 mod 自带 BepInEx 描述原文）。
**v1.2.2（"滑条压住 mod 名称栏"根治，含实测几何证据）**：原生只在部分路径把滚动条那 17px 收窄（日志实测正常态 `Viewport sd=-17` → Viewport 433、容器右缘世界 x=1875 vs 滚动条 1882 = 间隙 7px；**"退出设置再进去"那条路径不收窄** → `Viewport 450`、容器右缘 1900 与滚动条区间 1882..1912 **重叠 18px**，而滚动条此时 `active=1`）→ 名称栏灰色背景被滚动条及其背景盖住。修法 `ApplyScrollbarInset`：不再依赖原生预留，按**滚动条实际世界区间**算容器内缩（`over = 容器右缘 - 滚动条左缘 + 6` 世界像素，除以 `lossyScale.x` 换回本地单位；基础内缩左 0/右 8 与原硬编码等价），带 200px 安全阀，值变化 >0.5px 才写并打日志。**方法学：先加几何 dump（LAYOUT/SCROLL），用世界坐标重叠量定位，再改代码——不要靠肉眼猜"重叠"。**
**v1.2.1（2026-09-12 游戏更新适配）**：① **原生方法签名变更踩雷**——游戏把 `SettingsGUI_V2.UpdateOpenedMenu(bool)` 改成 `UpdateOpenedMenu(bool setFirstButtonSeected, bool preservePosition)`、`FillSettingPage(bool)` 改成 `FillSettingPage(bool, bool restorePosition, float selectedY, float scrollbarValue)`（另加 `GetSettingRow(Transform)`/`lastScrollbarValue`）。旧编译产物里 `UpdateOpenedMenu(true)` 的调用点运行时解析不到方法 → `MissingMethodException` **从 Harmony Prefix 逃逸**（异常发生在 JIT 解析调用点，前缀里的 try/catch 拦不住）→ 整个 Prefix 失败、原生 `SettingsTabRight` 一并中断 = **按右翻页键不能翻页**。现改为**反射按名 + 参数个数自适应**调用（未来再加参数也不会复发）。**教训：游戏更新后凡是"编译期直接调用原生方法"的点都要复查签名，翻页/设置这类核心交互优先反射或 `AccessTools` 按名取。** ② 行高归一化（`Templates.Instantiate` → `NormalizeRowHeight`）：行容器是 `VerticalLayoutGroup(childControlHeight=false)`，uGUI 取的行高是 `sizeDelta[1]`，克隆来的原生行若带垂直拉伸锚点/零 sizeDelta → 行高 0 → 所有行叠在同一点。③ `FillSettingPage` 也纳入 MODS 页拦截（原生重开设置会直接起这条协程恢复滚动位置，不经 `UpdateOpenedMenu`）。④ 重进设置界面前先 `RestoreScrollAnchors()` 再重建（用户在 MODS 页直接关设置时 `Update` 停跑，锚点/高度还原没有机会执行 → 坏高度泄漏）。**注意**：两套渲染实现（`Plugin.cs` 的 MODS 页 / `NativePage`）**分支必须同步维护**，新类型支持两边一起加。
**零维护设计（v1.1.0 起）**：无任何硬编码翻译表/mod 名单，新 mod（含第三方）加载即自动适配。类型自动映射：bool→开关；float/int→**数值输入框 + 范围提示**（非滑条）；string+`AcceptableValueList`→下拉；热键（KeyCode / string 键名含 key·toggle 且选项像按键表 / **枚举类型**）→**点击改键按钮 + `Input.GetKeyDown` 捕获**；未知类型→跳过不崩。
**显式保存语义**：改动只进 `staged` 暂存字典，【保存】才写 `BoxedValue` + `cfg.Save()`；`SaveOnConfigSet=false` 防运行中自动落盘（**BepInEx 退出时会自动保存全部 cfg**，所以必须暂存机制才能实现"按下才保存"）。
**其他**：热键冲突检测（页面顶部黄色警告）；每项独立【重置】【复制】+ 按钮文字闪烁反馈（不依赖延迟的 Hint）；字母分组视图；空列表卡死熔断器；`DefaultChinese` 编译期常量区分 EN/CN 构建。
**注意**：两套渲染实现（`Plugin.cs` 的 MODS 页 / `NativePage`）**分支必须同步维护**，新类型支持两边一起加。

### 2.6 ThrowableWheel `er2.throwablewheel` v1.3.6
替换原生手雷转盘（长按 G）为自定义投掷物列表 + 每 2s 自动补货。**关键**：投掷物必须以**正确子类直接 `inv.items.Add(vi)`** 注入（`AddVirtualItem`/`AddItemToInventory` 都会降级成基类 `VirtualItem`，原生转盘按 `VirtualThrowable` 过滤会无视）；显示/选择/投掷全走**原生管线**。
**配置**：`Enabled` / `ReplaceWheelContent` / `RefillBeforeWheelOpens` / `AllowInMultiplayer` / `LoadoutPreset`（自定义/经典14/扩充34/全部48/反坦克/燃烧/烟雾）/ `ItemIds`；每 tick `RebuildItemIds()` 热重载。
**投掷物 ID 全集**：`er2items.manifest` 的 `Prefabs/grenades/` 48 个（清单见 guide §4）。子类启发式 `MakeVirtualItem`：`smokegrenade_*`/`mle1916_smoke`/`nhg42`→Smoke；`at_*`/`*_at`/`tankmine*`/`satchel`/`geballte`/`*_hhl`/`*_rpg`→AT；其余→Grenade。

### 2.7 CombatTweaks `er2.combattweaks` v1.2.2
① **单机友军伤害关闭**（子弹/近战/爆炸/炮击/撞击全覆盖；联机走 `PhotonNetwork.IsConnectedAndReady && !OfflineMode` 判离线）；② 弹药倍率/无限（火力点/火炮/防空/自行火炮/坦克机枪/坦克主炮/飞机炸弹/步兵武器各自独立）；③ 爆炸范围倍率；④ 步兵受击伤害倍率。
**FF 拦截五层**：`BodyPart.AllowDamage`（**原生静态判定点**，同阵营→false，`Priority.First` 先短路）+ `HitPart` + `BulletInstance.OnHit` + 爆炸击飞（`RagdollManager.Ragdolize`/`AddForce` 窗口拦截）+ 跳跃窗口（`Soldier.Jump` / `OnJumpSynch` / `set_jumpVelocity` / Prefix 清 `m_JumpPower`+`jumpTimeEnd`）+ 地面吸附兜底。
**弹药入口**：炮塔 `TurretGun.ExtractOneBullet(TurretWeapon)`；步兵 `Magazine.ExtractOneBullet`/`ExtractBullets`（**不是** `GenericGun.ExtractOneBullet`，也不是 `VirtualAmmo`——后者只在换弹时消耗）；飞机 `VehiclePlane.DropBombs`；爆炸 `Explosion.CreateExplosion`。
**v1.2.2 修复**：`ExplosionFactionContextPatch` 建立友军爆炸窗口时**必须校验 responsible 阵营**，否则敌人手雷也会建立窗口 → 敌方死亡士兵倒地全被拦（"敌人被打死不倒地、站定冻结"根因）。**教训：任何"上下文窗口"类拦截，建立窗口前必须校验作用对象的身份边界。**
**已知局限**：`AllowDamage`/`HitPart` 的同阵营判定是**字符串全等**——跨国家同盟（US/UK 都是 allies）不在保护范围（玩家未报告，未动）。

### 2.8 ZoomAnywhere `er2.zoomanywhere` v1.0.1
**任意姿态/移动状态下都能屏息 + 武器放大**（原版只允许站定瞄准时屏息），可选额外放大倍率。默认键 **Left Shift**。

### 2.9 HighValueTarget（Veteran HVT）`er2.highvaluetarget` v1.1.26
**击杀归属**（`BulletInstance.OnHit` Prefix / 爆炸 `responsible` / 近战上下文 / `ItemHelmet.User` 兜底 + 玩家命中窗口 5s 防抢）+ **阈值标记**（敌 5 杀=HVT 橙标、友 2 杀=叛徒红标，阵营按 faction 的 `_` 后缀判定，跨国家同盟算友军）+ **仇恨聚焦**（覆盖 `Soldier.GetBestVisibleEnemy`，走原生目标选择）+ **标记强化**（减伤 0.6/加伤 1.4/命中 1.7/射速 1.4/移速 1.15/无视压制/不投降）+ **叛徒机制**（玩家可伤友军 + 友军集火玩家，穿透 CombatTweaks 友军保护）+ 玩家提示（Hint + 红屏 + 顶部常驻指示）+ M 大地图图标（跟随 `unitsContainer` 游戏标记）+ 头顶标记。
**关键陷阱（实测定案）**：① `Bullet.BulletDamage` 在本作**不被调用**——入口必须实测，反编译有 ≠ 会调；② **归属必须 Prefix 记录**（瞬杀爆头的死亡发生在原生 `OnHit` 内部，Postfix 记录晚于 `Kill` 事件 → "爆头不计数"根因）；③ FF 放行的阵营改写会污染 `SideOf` 判定；④ `ForceTarget` 强制态三宗罪（鞭尸/打断射击/站桩）→ 弃用改覆盖 `GetBestVisibleEnemy`；⑤ **头盔是独立于士兵层级的 BodyPart**（`GetComponentInParent<Creature>` 为 null → 用 `ItemHelmet.User`）；⑥ **IL2CPP 托管 MonoBehaviour 必须先 `ClassInjector.RegisterTypeInIl2Cpp<T>()` 再 AddComponent**；⑦ 击杀归属表 25s 窗口 + `CountedDeaths` 防 `Kill`/`KillSynched` 双计。
**Assets**：`HighValueTarget/Assets` → 部署到 `plugins/ER2_VeteranHVT/`（build.ps1 的 `assetsDir`）。

### 2.10 InventoryPause `er2.inventorypause` v1.0.5
打开背包（自己/尸体）时**真暂停**（延迟 timeScale 冻结，等打开动画完成）；暂停期间丢弃道具自动落地（扫描 `ItemObject.spawnedItems`）。
**ER2 暂停机制图谱（全部实测定案，做任何暂停功能前必读）**：原生 `Pause.SetPause` = timeScale=0 + 弹菜单 + `disableOnPause`（藏菜单 = 死锁）；手动 `Pause.isPaused=true` 禁用输入但**不冻结世界**；`timeScale=0` 真暂停但**卡 UI 协程动画** + 丢弃武器浮空（解法：延迟冻结等动画完成 + 扫描 `spawnedItems` 拉下道具）；`enableAiBehaviour(false)` **无效**（true 才有效）；背包开关读 `InventoryPanel.isOpen`。

### 2.11 SquadCommand（Battlefield Commander）`er2.squadcommand` v1.1.0
**本工作区最大工程**（`GodViewController.cs` 157 KB）。F9 进上帝视角的 RTS 小队指挥层，操作仿 Gates of Hell。详见 `ER2_zcode_era.md` §1 与 `SquadCommand/README.txt`、`GPT_CONTEXT.md`、`ROADMAP.md`。

### 2.12 UniversalGeneration `er2.universalgeneration` v1.0.0
RTS 上帝视角内**自定义生成单位/载具**（作弊向）。详见 `ER2_zcode_era.md` §2。

### 2.13 UnitCollision `er2.morephysics.unitcollision` v1.0.6
单位/尸体碰撞（MorePhysics 删除后的轻量保留版）。**修复原理 = 开碰撞矩阵**：`Physics.IgnoreLayerCollision(1,9,false)` 活体互碰（复用原版受击碰撞体）+ `(1,10,false)` + 尸体骨骼刚体强制动态 = 尸体可推开不挡活人。诊断模式 `UnitCollisionLayer=-2`（只打矩阵日志不修改）。**排查单位碰撞用 `Physics.GetIgnoreLayerCollision(l1,l2)` 直接读，别猜。**
**层事实**：CharacterController 层=1、BodyPart 受击碰撞体层=9（实心）、尸体层=10；游戏原本关闭了 `1↔9` 与 `1↔10` 矩阵。子弹 raycast 用 LayerMask，**与碰撞矩阵无关**（所以"受击正常但物理互穿"）。

### 2.14 UnitInfoOverlay（Unit Inspector）`er2.unitinfooverlay` v1.0.5
开发者调试工具：单位头顶悬浮信息（名称/血量+观察上限+失能阈值/状态标签/姿态/阵营/兵种/坐标/速度/实例 ID）。`PlayerController.Update` Postfix 刷新（0.1s `Time.unscaledTime` 节流）+ `PlayerController.OnGUI` Postfix 绘制；遍历 `Creature.allCreatures`。
**关键教训（v1.0.3 悬浮窗完全消失）**：**未验证的 interop 信号不能直接当门控**——`DeathPanel.instance.gameObject.activeInHierarchy` 战斗中**恒 true**（v1.0.2 消失根因），`LoadingCircle.IsLoading()` 战斗中恒 false。现在门控只用实证信号（`Pause.isPaused` + `timeScale<=0.001` + `ControlledCharacter.IsDead`）+ 确定性加载尾巴（`firstVisibleTime` + `hideAfterLoad` 配置）。**方法学：先加诊断日志实测，再当门控用。**
**边界**：interop 无单位最大血量字段 → 用"观察上限"（`observedMax` 字典按指针跟踪历史最大 HP）；`IsReloading/IsAiming/IsThrowing/IsCrawling/IsOnFire` 是**属性不是方法**；`IsIncapacitated` 无 getter → DOWN 由"存活且 HP < `INCAPACITATED_THREESHOLD`"推断。

### 2.15 FleshWoundsFixed `ER2_FleshWounds` v1.0.1
第三方 Flesh Wounds 的**重建修复版**（Nexus mods/59）。修复 **2026-08 的"单位贴图变紫"恶性 bug**：原版在运行时克隆/销毁士兵材质，销毁时序与游戏冲突导致材质丢失。**诊断紫贴图问题先看它。**
**部署注意**：build.ps1 **无**该条目 → 需手动 `dotnet build` + 部署。部署目录里相关的是 `plugins/ER2_FleshWoundsBW/`（子目录版）——注意 `ER2_FleshWounds_TRCompatBW.dll` 是第三方兼容件，不要混淆。

### 2.16 HvtTestDriver `er2.hvt.testdriver` v1.0.0（内部工具）
进战斗后自动：把离玩家最近的敌方 AI 设为 Lv.V 老兵（5 杀）→ 触发红/金闪烁 → 弹两条底部 toast → 自动截图 6 张到 `research_out/hvt_shots/`。用途：配合外部截图 + 视觉模型验证 HVT 渲染输出。**不发布。**

## 3. 部署目录里的第三方 mod（DLL 名 ≠ 插件名）

除原创 mod 外，`plugins/` 里还有一批第三方 mod（**不是本工作区源码，不要改/删**）：

| DLL | 说明 |
|---|---|
| `ER2_FleshWoundsBW/`（子目录）、`ER2_FleshWounds_TRCompatBW.dll` | Flesh Wounds 血迹包（见 §2.15） |
| `ER2_ImpactFXBW/`（子目录） | ImpactFX 命中特效 |
| `ER2_ReactiveRagdollBW.dll` | Reactive Ragdoll |
| `ER2_RemoveStainsBW.dll` | Remove Stains |
| `ER2_RealisticBlood.dll` | Realistic Blood |
| `ER2_RealisticEffects.dll` | Realistic Effects |
| `ER2_BulletPenetration.dll` | Bullet Penetration |
| `ER2_DeathScreenEffect.dll` | Death Screen Effect |
| `ER2_RecoilOverhaul.dll` | **内含 "Universal Recoil Control"**（GUID `UniversalRecoilControl`）——DLL 名 ≠ 插件名典型 |
| `ER2_Coax_MG_Hotkey.dll` | Coax MG Hotkey（用 `UnityEngine.InputSystem.Key` 枚举做快捷键——ModManager 枚举支持就是为它加的） |
| `ER2_MeleeTweaks.dll` | Melee Tweaks |
| `ER2_FPSBodyShadowsFix.dll` | FPS Body Shadows Fix |
| `ER2_LeaveRedeploy`（第三方） | 曾在死亡时接管"小队选择→部署界面"流程（隔离验证用） |

## 4. 发布包状态（`C:\Users\71011\Downloads\`）

最新一批（2026-09-06）：

| 包 | 时间 |
|---|---|
| `ER2_BattlefieldCommander_v1.1.0.zip` / `ER2_BattlefieldCommander_CN_v1.1.0.zip` | 09-06（**最新**） |
| `ER2_UniversalGeneration_v1.0.0.zip` / `ER2_UniversalGeneration_CN_v1.0.0.zip` | 09-06 |
| `ER2_ModManager_v1.2.0.zip` / `ER2_ModManager_CN_v1.2.0.zip` | 09-05 |
| `ER2_UnitInfoOverlay_v1.0.5.zip`、`ER2_HideAnything_v4.5.4.zip`、`ER2_WeatherControl_v1.7.2.zip`、`ER2_LimbTweaks_v2.13.101.zip` | 09-05 |
| `ER2_CombatTweaks_v1.2.2.zip`（+CN）、`ER2_InventoryPause_v1.0.5.zip`（+CN）、`ER2_VeteranHVT_v1.1.26.zip`（+CN） | 08-25/26 |

**历史清理**：SquadCommand 全部中间版本包与暂存目录已删（只留 1.1.0 双语）；工作区根目录 `EasyRed2_BepInEx_Dependencies.zip` 已删。

## 5. 台账自检（改完 mod 后跑这个核对）

### 文档漂移（2026-09-06 发现 → 2026-09-12 已修，以源码为准）

**README 版本号 vs `BepInPlugin` 版本**：

| mod | 源码版本 | 原 README | 处置 |
|---|---|---|---|
| `NoInteractionHints` | **4.5.4** | `v4.5.3` ❌ | ✅ 已修（首行 + 补 v4.5.4 变更说明） |
| `UnitInfoOverlay` | **1.0.5** | `v1.0.4` ❌ | ✅ 已修（首行 + 补 v1.0.5 说明 + 去 F5 旧术语） |
| `ModManager` | 1.2.0 | 第 3 行写 `Version 1.2.0` ✅ | 无需修（先前误报：只查了首行） |

**补齐的缺失 README.txt**（2026-09-12，4 个可打包 mod）：`LimbTweaks`、`WeatherControl`、`AIFood`、`ThrowableWheel`
**补齐的缺失 Nexus_description.md**（2026-09-12，同样 4 个）：`LimbTweaks`、`WeatherControl`、`AIFood`、`ThrowableWheel`
> `FleshWoundsFixed` 已有详尽 `README.md` 且**不在 build.ps1**（手动构建部署、不打包），故无需 README.txt；`HvtTestDriver` 是内部工具不发布。
> **实锤影响**（拆包核对）：`ER2_LimbTweaks_v2.13.101.zip` 与 `ER2_WeatherControl_v1.7.2.zip` 内**只有 DLL**——`build.ps1` 对缺失文档**静默跳过**。文档已补齐，**需重打包**才会进 zip（见 TODOS）。
> **注意**：新补的 README / Nexus 描述均为**英文**（与其余 mod 一致）；若要出 `_CN_` 包需另建 `README_CN.txt` / `Nexus_description_CN.md`。

### 版本一致性核对脚本

```powershell
cd D:\Users\71011\Documents\ER2_Mods
# 源码版本（权威）
Get-ChildItem -Directory | Where-Object { Test-Path (Join-Path $_.FullName "Plugin.cs") } | ForEach-Object {
  $t = Get-Content (Join-Path $_.FullName "Plugin.cs") -Raw
  $m = [regex]::Match($t, 'BepInPlugin\("([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"')
  if ($m.Success) { "{0,-22} {1,-34} v{2}" -f $_.Name, $m.Groups[1].Value, $m.Groups[3].Value }
}
# README 版本漂移检查
Get-ChildItem -Directory | Where-Object { Test-Path (Join-Path $_.FullName "Plugin.cs") } | ForEach-Object {
  $t = Get-Content (Join-Path $_.FullName "Plugin.cs") -Raw
  $m = [regex]::Match($t, 'BepInPlugin\("([^"]+)"\s*,\s*"([^"]+)"\s*,\s*"([^"]+)"')
  $src = if($m.Success){$m.Groups[3].Value}else{"?"}
  $r = Join-Path $_.FullName "README.txt"
  $rd = if (Test-Path $r) { (Get-Content $r -TotalCount 1) } else { "(no README)" }
  $flag = if ($rd -match [regex]::Escape($src)) { "OK" } else { "** DRIFT **" }
  "{0,-22} src=v{1,-10} README='{2}' {3}" -f $_.Name,$src,$rd,$flag
}
# 部署是否跟上（时间戳）
Get-ChildItem "E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\plugins" -Filter "ER2_*.dll" | Sort-Object LastWriteTime -Descending | Select-Object Name,LastWriteTime -First 12
```

