# Easy Red 2 AI 系统研究详细报告

> 目的：为"实现更多 mod 功能"提供可落地的 AI 系统细节。
> 数据来源：反编译 `Assembly-CSharp.dll`（BepInEx interop）+ 磁盘调查（mission Lua 脚本、SHARED 目录）。

## 0. 关键方法论结论（重要）

- **interop DLL 的方法体是空的**：所有方法是 `IL2CPP.il2cpp_runtime_invoke(NativeMethodInfoPtr…)` 原生转发壳，**真正的 AI 逻辑 IL 不在此 DLL**（在被混淆/裁剪的原生 GameAssembly 中）。所以从 interop 只能拿到**签名/字段/继承关系**，这正是 Harmony patch 与运行时操控 AI 所需的全部信息；想看具体行为只能靠 DLL 指针追踪或实测。
- **游戏有官方的"AI 脚本"通道**：AI 大脑是**Lua 脚本**（`setBrain("xxx.lua")`），存放在 `Easy Red 2_Data/StreamingAssets/Missions/**/scripts/AI/` 和 `.../Missions/SHARED/`。这对 mod 远比 patch 编译层强（见 §4）。

## 1. AI 决策结构（SoldierAI / ISAI / SAI_Default / AiParams）

### 1.1 单兵 AI 控制器：`SoldierAI : MonoBehaviour`（每个士兵一个）

核心字段（运行时可直接读写/Harmony 后期改写）：
- `character`(Soldier)、`AI`(ISAI 行为大脑)、`path`/`pathRequest`（路径）、`visibleTarget`(Spottable 当前可见目标)
- 移动态：`MoveDestination`、`actualMoveDest`、`speed`、`dir`、`moveCharacter`、`lodMovement`
- 战斗态：`targetInWeaponRange`、`distFromTarget`、`aimingEnemy`、`allowFireAtEnemy`、`fireDir`、`nextAvailableShotFires`、`shotFiresEnd`
- 压制/路径锁：`nextSuppressionCheckTime`、`lockPathCalculationRetryTime`、`mustTeleport`、`minTeleportDistance`(static)
- 节奏：`loop_250ms`、`loop_2000ms`、`sequentialUpdateCount`（顺序更新计数，配合 LOD 分帧）

主循环入口（更新/逐帧）：
- `Awake()`、`Update()`（主决策驱动）、`SequentialUpdate()`（分帧/轮询，LOD 用）、`FixedUpdate()`（物理，含协程 `_FixedUpdate_b__39_0`）
- `UpdatePath()`、`CheckReachedNode()`、`MovementRoutine(dt)`、`MoveFPSOptimized`/`MoveOptimized`、`RotateAndFireToward`、`OnDestinationChanged()`、`OnSuppressed()`、`TryStopBleeding()`（止血，LimbTweaks 可挂钩）

**Harmony 推荐 patch 点**：
- 全局行为入口：`SoldierAI::SequentialUpdate` / `FixedUpdate`（Postfix 改状态/目标）
- 单帧战绩再平衡：`ProcessAiAccuracy(Soldier user, AiAccuracyMode mode)`（**static**，精度总线）——改 AI 命中率的核心钩子；`AiAccuracyMode` 枚举：`infantry / mg / cannon / spa`
- 护甲/止血整合：`TryStopBleeding()`（Prefix 阻断再回血，见陷阱 7）
- 姿态：`GetFavouriteFightingPose(bool hasTarget, byte suppressionValue)`（决定卧/蹲/站）
- 射击节奏：`CalculateNextShootDelay()`（单发间隔）

### 1.2 行为接口：`ISAI`（虚方法类，非真 interface）
- 单一抽象方法 `Run(SoldierAI soldierAi, Soldier character)` —— 这就是"状态机/行为树"的**每帧决策入口**。
- `SAI_Default : Il2CppSystem.Object` 是默认实现（`Run` 是 `virtual final new`，可由 Lua 系统换成别的 brain）。**替换 `SoldierAI.AI` 或劫持其 `Run` 即替换整个单兵决策**。

### 1.3 `AiParams`（每个士兵的能力开关，不是数值调参）
- 全是 **bool**：`canMove, canGiveOrders, canFollowForsers, canDoMedic, canUseRadio, changePoseAutomatically, checkForEnemies, openWindowsAroundWhenInCover, targetAircaftsViolently, findCoverWhenSuppressed, neverLeaveVehicle, allowBeingTargetedByEnemy, netUpdateRequested`；`forcedPose`(Nullable<SoldierPose>)
- 方法：`enableAiBehaviour(bool)`、**`followCustomDirectCommands()`、`followCustomSquadOrders()`**（官方"自定义指令/自定义小队命令"切换入口）、`allowGiveOrders/allowFollowOrders/allowDoMedic/allowRadioOrders/allowMovements/allowChangePose/allowCheckForEnemies/allowOpenWindows/allowFindCoverWhenSuppressed/allowLeaveVehicle/setTargetPlanesOften/allowBeingTargeted`(全带 bool)、`SyncParam(Soldier)`、`UpdateSynchedParam(byte[])`
- 落地方式（LimbTweaks 已知）：士兵身上挂 AiParams，脚本里 `getAiParams()` 取回即可按需改能力。
- **注意**：所谓"勇气/精度/反应时间"这类数值参数**不在 AiParams 里**，而是由 `SoldierAI.ProcessAiAccuracy`（static 精度总线）+ 配装/等级 + Lua brain 控制。

### 1.4 `AiParamsSerializer` —— 不是 XML 加载器
- 公开静态方法：`Serialize(AiParams) -> byte[]`、`Deserialize(AiParams, byte[])`、`GetSortedBoolFields()`（用反射按名字排序 AiParams 的 bool 字段）。
- 它把一堆 bool **打包成字节数组做网络同步**（`SoldierAI` 交互/MP 用），与"磁盘 xml 参数文件"无关。
- **磁盘上不存在"AI 参数 xml"**：单兵 AI 参数全在 `AiParams` 对象内（由 spawn/AiParamsSerializer 网络同步），行为默认值由原生代码内置，**可改的 AI 配置主要是 `SoldierAI` 的 static 字段**（如 `SoldierAI.minTeleportDistance`、`SoldierAI.nodeReachedDist`）和 Lua brain。

## 2. 任务/目的地系统（SquadTask / AiDestination / 特化目的地）

### 2.1 `SquadTask`（抽象）——HUD 目标任务描述符
- 虚方法：`GetTaskString()/GetTaskStringShort()`（HUD 文本）、`GetTaskDestination()`、`GetTaskAreaRadius()`、`GetTaskIcon()`、`IsInsideTaskArea(pos,resized=1)`、`GetTaskCompletationPercentage()`、`UpdateTaskStatus()`、`RemoveTaskForever()`。
- 它更接近"战役/小队 **目标点**"（UI 标记 + 到达判定），**不是**逐帧移动指令。mod 可实现子类并注入 Squad 的任务列表（配合 `Squad._UpdateSquadTaskUI_d__142`）。

### 2.2 `AiDestination`（抽象基类）——AI 真正"要去哪/占哪"的载体（核心）
虚方法：`IsVehicle()`、`CanBeRegisteredInOctaTree()`、`IsCoverAvailable(Vector3 shootDirection, string faction)`、`IsCoverOccupied(string faction)`、`GetCoverConnectedVehicle()`、`GetCoverPosition()`、`GetCoverIconPosition()`、`GetCoverPose()`、`IsCoverDestroyed()`、`OccupyCover(string faction)`、`IsReached(Vector3)`、`OnCoverReached(Soldier)`、`IsFinalDestination()`、`PrioritizeReachingDestination()`、`IsUnsafeCover()`。
- 派生子类：
  - `CombatCover : MonoBehaviour`（战场掩体，字段：`occupyFaction, destroyed, localPosition, localLookDirection, localLookAngleWideness, suggestedPose`，方法 `GetLookRotation()`）——地图上真实掩体（墙/窗/战壕）。
  - `DestinationWaypoint : DestinationWithoutCover`（`ctor(Vector3 position, SoldierPose pose, AiDestination nextCover, float expireTime=100)`；`coverReached, expireTime`）——**链式路点目的地**，`nextCover` 指向下一跳，天然形成"路点链"，正是"让 AI 走指定路线"的官方对象。
  - `ChargeDestination`（`ctor(Soldier charger, Vector3 destination)`）——冲峰目标（`Squad.charge` 生成的临时目标）。
  - `MedicHealingDestination`（`ctor(Soldier soldierToHealUp, Soldier medic)`；字段 `soldierToHealUp, medic, coverReached`）——**医疗特化目标**：Medic/Lua `isMedic` 士兵据此跑到伤者身边。
  - `VehicleRepairTask`（抽象）——维修特化（Lua `getNearestVehicleToRepair` + `Squad.repairVehicle` 配合）。
- 归宿：所有 `AiDestination` 会被 `CoverManager.RegisterCoverInOctaTree` 注册进八叉树，供 AI "就近找掩体/目的地"。

### 2.3 `CoverManager`（static 全局八叉树，强大 mod 目标）
- static 字段：`vehicleCovers`、`staticCovers`（`PointOctree<AiDestination>`）、`coversToRegister`(List)、`nextOctatreeUpdate`
- static 方法：**`GetCovers(Vector3 pos, float radius, string faction, Vector3 coverDirection, bool nearestToPos=false)`**、`RegisterCoverInOctaTree(AiDestination)`、`ResetCoversOctatree()`、`UpdateInTreeAsync()`/`UpdateInTreeAsyncCr()`（异步重建）
- 意义：mod 可**动态增删 AI 可见掩体/目的地**（比如临时性构筑物、被破坏的掩体标记 destroyed）。

### 2.4 与"AI 移动无法外部驱动"结论的对照（重要修正）
- 直接 `Soldier.Move()`/`SetDestination` 会被 AI 控制器覆盖（陷阱 6，成立）。
- **但有官方合法任务通道**：脚本层 `Lua_Soldier.moveTo(Vector3)` / `stop()` / `findCover(pos,radius)` 和 `Lua_Squad.moveTo/charge/coverArea/attackFromPoint` 会走**领导/小队订单例程**（`SoldierAI.SquadOrdersRoutine`、`CalculateMoveOrderDestination`、`GetPositionInsideFormation`、`AiParams.followCustom*Orders`）把目的地算进原生 AI 管线，**不会被覆盖**。LimbTweaks 类需求若要让某个 AI 去某处，应走 `Lua_Soldier.moveTo`／构造 `DestinationWaypoint` 设进 AI，而不是硬调 Move()。

## 3. AI 感知（Spottable / IShotTarget / Target / 强制目标）

- `Spottable`（抽象，所有可被 AI 发现/锁定单位实现的接口）：`GetPosition()`、`GetCenterOfUnit()`、`PredictCenterOfUnit(float distance, float bulletSpeed)`（弹道提前量）、`CamoDistance()`（伪装距离，与 CamouflageManager 联动）、`SpotPosition()`、`IsAirVehicle()`、`IsWheeledVehicleOrTank()`、`CanFight()`。
  - `SoldierAI.visibleTarget` 就是一个 `Spottable` —— **改这个引用即可让 AI"无视/锁定某目标"**。
- `IShotTarget`（开火目标接口）：`GetCenter(), IsAlive(), IsFiring(), IsRunning(), IsAlerted(), HasActiveTurret(), IsAirTarget(), TryGetRigidbody(out), GetBestVisibleEnemy() -> Spottable, HasEnemyInSight(), RawTransform, DebugName`。
  - `GetBestVisibleEnemy()` 是炮塔/武器选目标的官方判定点，可 patch 改成"只看某阵营/某人/无视某单位"。
- `Target : Soldier` 是**靶场假人**（实现 CamoDistance/GetCenterOfUnit/PredictCenterOfUnit/FacePosition），不是目标选择逻辑。
- 警觉（Alert）由 `Lua_Soldier` 暴露：`alertFor(float)`、`stopAlert()`、`setNotAlerted()`、`isAlerted()`、`getSuppressionValue()`、`isSuppressed()` —— **控制 AI 是否发现/袭击玩家**的直接 API。
- 强制目标（官方）：`SoldierAI.ForceTarget(Lua_Soldier)`、`SoldierAI.ForceVehicleTarget(Lua_Vehicle)`，及脚本层 `Lua_Soldier.forceTarget(Object) -> bool`。"让某 AI 锁定/忽略某目标"有原生支持。

## 4. 载具 AI（AIVehicle / IVAI / VAI_* / TurretAI）

- `IVAI`（虚接口）：`OnAssign()`、`Run(AIVehicle aiVehicle, Vehicle vehicle)` —— 与单兵 ISAI 同构的**每帧决策入口**。
- `AIVehicle : MonoBehaviour`（每辆 AI 车一个）：
  - 字段：`veh`(Vehicle)、`AI`(IVAI)、`squadInside`、`hasEnemy`、`hasEnemyTank`、`distFronCurrentNode`、`stopPhase/stopPeriod`、`noDestinationFoundCount`、`rerollDestination`、`setVehicleDestination`(backing, protected)、`orbitTimer`、`unstuckAttempts/stuckTimer/lastStuckPos`、`going_in_retro/retroBehaviour`、`slidingToNode`
  - 主循环：`Update()`、`MovableVehicleRoutine()`、`StaticVehicleRoutine()`、`UpdatePath()`、`MoveTowardCurrentNodeTank/WheeledVehicle()`、`RotateVehicleTowardEnemy`、`RotateStaticVehicleTowardEnemy`
  - 排序/任务：`GetCurrentSquadDestination()`、`GetRandomDestinationInTaskArea()`、`SetVehicleDestination`、`StopAndClearPath()`、`CheckReachedNode()`、`IsArrivedToDestination()`、`DestinationHasChanged`
  - 解卡：`TryUnstuck()`、`PrepareUnstuckTeleport()/ExecuteUnstuckTeleport()`、`TrySlideToNode()`、`VehicleStuckCheck()`；驱动数据 `GetDriveData(out throttle, out gear, out steer, out brake)`（可 patch 改变 AI 驾驶输入）
  - 乘员：`RetreatCrew()`、static `SwitchSeatAi(bool, Vehicle)`、`RequiresDriver`
  - 枚举 `RetroBehaviour`: `backward, backAlignWithNode, backLeft, backRight, forward, forwardLeft, forwardRight`
- `VAI_Vehicle`（抽象基类，interop 为空体）→ 派生：
  - `VAI_Movable`（轮式/履带通用移动 AI，override `OnOrderCheckUpdate(AIVehicle,Vehicle,Squad,Soldier)`、`UpdateMoveDestination(AIVehicle,Vehicle,Squad)`）
  - `VAI_MovableArtillery`（移动火炮；`VehicleCanAttack(Vehicle)`、`FriendlyAreAttacking(string faction)`，override `UpdateMoveDestination`——选择射程内开火 vs 撤退）
  - `VAI_Static`（固定炮位/防御，`OnAssign()`）
  - `VAI_Scripted`（**脚本路径 AI**：`ctor(string script)`、`MoveTo()`、`AddWaypoint()`、`Run(AIVehicle,Vehicle)`）——载具 AI 也支持路径脚本（对应 Lua 脚本化的车辆移动）。
- **炮塔控制**：
  - `SoldierAI`/AI 用 `GetShotDirToTarget()`、`RotateAndFireToward`，载具炮塔由 `Turret.decompiled.cs`（code/Turret）暴露 **`Turret.ForceTargetRotation(LookParameters)`** —— 强制炮塔转向指定 look 参数，是**炮塔自动化/手动覆盖炮塔**的关键钩子。
  - 炮塔组件族：`TurretController / TurretAim / TurretGun / TurretGunRotator / TurretBase / VehicleTurret / TurretWeapon`（详见 research_out/code/）。
  - `IShotTarget.HasActiveTurret()` 让 AI/玩家知道敌车炮塔是否在转（`TurretAim` 参与）。
- **车辆移动不暴露给脚本层**：`Lua_Vehicle` 只有损伤/维修/乘员/刹车 API（`damage*, repair(), brake(), kickEveryoneOut(), canShootInDirection()`），**没有 setVehicleDestination**。即：想让 AI 车去哪，官方通道是 `VAI_Scripted` 路径脚本（或接管 `AIVehicle.GetsCurrentSquadDestination`/`UpdateMoveDestination`），脚本层做不到直接指派目的地。

## 5. 对 mod 的机会清单

1. **AI 精度/移速调参**：patch `SoldierAI.ProcessAiAccuracy`（返回精度倍率）+ static 字段（`nodeReachedDist`、`minTeleportDistance`）+ `SoldierAI.CalculateNextShootDelay` → "老兵更容易命中/射速差异化"，无需触碰原生数值。
2. **AI 能力开关**：`SoldierAI.character` 上取 `AiParams`，设 `canMove=false / checkForEnemies=false / findCoverWhenSuppressed` 等 → "让 AI 站桩当背景"/"无视敌人压制"，比 patch 直观且网络同步安全。
3. **自定义 SquadTask / AiDestination**：实现子类（如 `MyDestination : DestinationWaypoint`）注册进 `CoverManager`，让 AI 走向自定义目标；配合 `Lua_Squad.moveTo/charge` 走原生队列不被覆盖。
4. **强制/锁定目标**：调用 `SoldierAI.ForceTarget(luasoldier)`/`ForceVehicleTarget`（或脚本 `forceTarget`）让某 AI 优先打某个单位；patch `IShotTarget.GetBestVisibleEnemy()` 实现"掉向/无视某阵营或玩家"。
5. **AI 数量与重生**：AI 属于 Squad/Soldier spawn 管线（RESpawnPanel/RespawnButton 存在）；结合 `AiParamsSerializer` 注意多机同步——本地增兵若需同步要斟酌。
6. **炮塔自动化**：掉 `Turret.ForceTargetRotation(LookParameters)` 让 AI/无人炮塔追瞄；改 `TurretAim` 让玩家座炮塔自动提前量。最有直接价值。
7. **AI 警觉/伪装**：`Lua_Soldier.alertFor/setNotAlerted` + `Spottable.CamoDistance()` → 潜行 mod（降低 AI 发现玩家的距离），Patch `CamoDistance` 返回按玩家姿态放大的伪装距离。
8. **Lua AI 大脑（最推荐的官方通道）**：写 `scripts/AI/*.lua`，在任务里 `me:setBrain("...")` 替换默认 `SAI_Default`——可做完全自定义的单兵"剧本 AI"（指定巡逻路点链 `moveTo`/`sleep`/`isSquadLeader`/`global`）。比 Harmony 改 C# 更稳、跨版本更抗混淆。
9. **止血/AI 伤病联动**（LimbTweaks 路线）：patch `SoldierAI.TryStopBleeding` 与 `applyBleeding/isBleeding/removeBleeding`（AI 也会失血并自行止血），可让 AI 更频繁求医/倒地。
10. **载具解卡/驾驶输入**：patch `AIVehicle.GetDriveData(out…)` 与 `TryUnstuck/PrepareUnstuckTeleport`；或给车辆挂 `VAI_Scripted` 脚本实现固定巡逻路径。

## 6. 与现有认知的冲突/修正
- **"AI 移动无法外部驱动"需加限定**：直接 `Move()/SetDestination` 会被覆盖；但 `Lua_Soldier/Lua_Squad.moveTo、findCover、DestinationWaypoint 链、AiParams.followCustom*Orders` 是官方且有效的任务通道。
- **AiParams 不是 xml 文件**：磁盘无此类 xml；AI 数值调参的入口在 `SoldierAI.ProcessAiAccuracy` + static 字段 + Lua brain，而非 AiParams（AiParams 全是能力开关，仅网络字节同步）。

## 附：反编译产物
- 全部在 `<工作区根>\research_out\`（`*.decompiled.cs`，根目录为本次清单，`battle/`、`code/`、`code2/` 为附带解出的相关类型）。涉及文件：SoldierAI、ISAI、SAI_Default、AiParams、AiParamsSerializer、SquadTask、AiDestination、DestinationWaypoint、DestinationWithoutCover、ChargeDestination、MedicHealingDestination、VehicleRepairTask、CombatCover、CoverManager、AIVehicle、IVAI、VAI_Vehicle/Movable/MovableArtillery/Static/Scripted、TurretAI、Spottable、IShotTarget、Target、Turret（code/）。
- 磁盘脚本：`Easy Red 2_Data/StreamingAssets/Missions/**/scripts/AI/*.lua`、`Missions/SHARED/*.lua`（官方 AI 大脑示例，已读 `briefing.lua` 验证 `setBrain`/`myself()`/`global`/`isSquadReady` 等 API）。
