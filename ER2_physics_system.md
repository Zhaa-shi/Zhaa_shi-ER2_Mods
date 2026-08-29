# Easy Red 2 物理系统运作方式与优化评估（解包实证）

> 2026-08-18 产出。来源：`globalgamemanagers`（UnityPy 解包 PhysicsManager/TimeManager/TagManager）
> + interop 反编译（RagdollManager/MapPropReference/Bullet/BulletInstance/载具体系）。

## 一、引擎物理配置（globalgamemanagers 实证）

| 配置 | 值 | 说明 |
|---|---|---|
| `m_Gravity` | (0, **-9.81**, 0) | 标准重力 |
| **Fixed Timestep** | **0.02（50Hz）** | 物理帧率 = 引擎标准；MaximumAllowedTimestep 0.1（慢动作上限） |
| `DefaultSolverIterations` / `VelocityIterations` | **6 / 1** | Unity 默认迭代（标准，非性能瓶颈） |
| `SleepThreshold` | 0.005 | 刚体睡眠阈值（默认） |
| `DefaultContactOffset` | 0.03 | 接触偏移（默认） |
| `m_QueriesHitTriggers` | **true** | 所有射线/Overlap 都会命中 trigger 碰撞体 |
| `m_QueriesHitBackfaces` | false | 背面不参与射线 |
| `m_ContactsGeneration` | 1 | 接触生成模式 |

**结论：物理引擎是 Unity 标准 3D 物理，无魔改参数。** 性能特征由用法（§三）而非配置决定。

## 二、图层与碰撞矩阵（关键语义）

16 个物理层 + 3 个 Tag（`IL_units`/`IL_tanks`/`IL_all`）：

```
0 Default  1 TransparentFX  2 Ignore Raycast  3 (空)  4 Water  5 UI
6 Interaction  7 WheelOnly  8 bullet  9 onlybullet  10 item
11 Wheel  12 Vehicle  13 Terrain  14 InvisibleWall  15 InvisibleTrepassableWall
```

**核心碰撞规则（解码自 LayerCollisionMatrix）**：
| 层 | 碰撞 | 不碰撞（设计意图） |
|---|---|---|
| **bullet(8)** | Default/Water/Interaction/bullet/onlybullet/item/Vehicle/Terrain | **InvisibleWall/InvisibleTrepassableWall（子弹穿隐形墙）**、WheelOnly/Wheel、UI |
| **onlybullet(9)** | 仅 bullet | 专用弹道辅助层 |
| **item(10)** | Default/bullet/item/Vehicle/Terrain | InvisibleWall（物品穿墙掉出地图） |
| **Vehicle(12)** | Default/Interaction/bullet/item/Vehicle/Terrain/InvisibleWall/InvisibleTrepassableWall | WheelOnly/Wheel |
| **Wheel(11)/WheelOnly(7)** | Wheel 系互碰 + Terrain | 车轮专用层 |
| **InvisibleWall(14)** | 挡 Vehicle/Terrain 系 | **不挡士兵(Default)**（士兵可穿，载具被拦=地图边界） |
| **InvisibleTrepassableWall(15)** | 挡 Default/Vehicle | 反向：拦士兵不拦载具 |
| **Interaction(6)** | 仅 bullet/Vehicle/TransparentFX | 互动提示不物理阻挡 |
| **Water(4)** | 仅 bullet/InvisibleWall | 水体几乎不参与物理 |

**弹道语义**：子弹层故意不碰隐形墙；`queriesHitTriggers=true` 使射线可命中 trigger——弹道以自定义射线结算为主（见 §三），碰撞矩阵主要约束刚体/角色物理。

## 三、物理载体分布（谁在用物理引擎）

| 载体 | 机制 | 证据 |
|---|---|---|
| **士兵** | `CharacterController`（无 Rigidbody）+ `TranslateWithCharacterController`；`CalculateControllerCenter(CharacterController)` | Soldier 反编译 |
| **AI 士兵** | `NavMeshAgent`（导航寻路，非物理力） | SoldierAI FixedUpdate 主事务 |
| **载具** | `Rigidbody`（MovableVehicle/VehicleWithWheels/VehiclePlane），物理帧驱动（FixedUpdate：Vehicle/VehicleTank/VehiclePlane/AIVehicle）；车轮 Wheel/WheelOnly 层 | 载具体系反编译 |
| **布娃娃** | `RagdollManager`：Rigidbody+关节；**原生物理优化：`SetCollidersLOD(bool)`（布娃娃碰撞体按 LOD 裁剪）+ `ProcessPausePhysic()`（暂停物理）** | RagdollManager 反编译 |
| **枪弹** | **自定义射线**：`BulletInstance.OnHit(RaycastHit)` + 静态 `Bullet.BulletDamage(ImpactSpecifier, BulletData, point, damage, shooter)` 结算——**小口径弹道不占物理引擎** | Bullet/BulletInstance 反编译 |
| **爆炸/炮击** | 静态 `Explosion.CreateExplosion(...)` 一次性 `OverlapSphere` 查询 + 士兵受击走 BodyPart/HitPart（非持续物理力）；炮弹/炸弹飞行同样射线式 | Explosion 反编译 |
| **场景物** | `MapPropReference`：**按距离管理碰撞体与渲染**（`UpdateColliders(RenderMode)` + `COLL_DISTANCE_CAM/OBJ/AI` + `LOD_DISTANCE_MULT` + `spawnDistance`）——原生"物理 LOD" | MapPropReference 反编译 |

**固定帧负载**：FixedUpdate 集中在 SoldierAI(8)/Soldier(4)/载具系/弹药箱类——每物理帧事务少，常规战斗下物理负载适中。

## 四、能优化吗？（按性价比评估）

### 游戏已内置的优化（不该重复造轮子）
1. 布娃娃物理 LOD + 暂停（`SetCollidersLOD`/`ProcessPausePhysic`）
2. 场景物碰撞体/渲染距离剔除（MapPropReference.UpdateColliders + RenderMode）
3. 弹道不占物理（射线结算）
4. 刚体睡眠阈值 0.005（静止物体自动休眠）

### 真正值得做的优化候选（mod 侧）
1. **尸体物理预算管理（性价比最高）**
   - 问题：尸体=完整布娃娃（每个 ~15-30 个 Rigidbody+关节）；大场面尸体堆积是最大物理负载增量
   - 做法：可配置的"尸体预算"——超过 N 具尸体时，最远/最旧的尸体调 `RagdollManager.SetCollidersLOD(true)` 或 `ProcessPausePhysic()`；或按玩家距离分级（近=完整、中=碰撞体 LOD、远=暂停物理）
   - 风险低：复用原生 API，不碰死亡流程
2. **mod 自身查询纪律**
   - 不要每帧大半径 `Physics.OverlapSphere` / `FindObjectsOfType`（MorePhysics 19 版教训）
   - 用静态注册表（`Creature.allCreatures`/`Vehicle.allVehicles`/`ItemObject.spawnedItems`）+ 事件驱动
3. **避免滥用 trigger 碰撞体**：`queriesHitTriggers=true` 使每条射线都要遍历 trigger——大场景放大量 trigger 会放大所有射线成本
4. **炮弹/手雷等投射物数量**：虽然射线结算不占物理，但每帧多条长距离射线+命中处理仍吃 CPU——考虑命中检测距离裁剪（尊重 spawnDistance 类配置）

### 不建议动的（风险高收益低）
- Fixed Timestep 50Hz（改 60Hz 改变全游戏手感/弹道/联网同步）
- Solver 迭代 6/1（改高只提升极端堆叠稳定性，常规场景无收益）
- 重力/接触偏移/图层碰撞矩阵（bullet/item/Vehicle/InvisibleWall 语义是游戏设计的一部分，改坏边界/弹道/载具行为）

### 验证方法
- BepInEx 计时器：对 `Physics.Simulate`/固定帧回调做耗时采样；或对比"尸体预算开/关"的帧耗时与 1% low FPS
- 观察 FixedUpdate 主事务（SoldierAI/载具）在千人场面的耗时

## 五、结论

物理系统 = **Unity 标准引擎 + 游戏内建分层优化**（布娃娃 LOD、场景物距离剔除、射线弹道）。
"优化"空间不在引擎参数，而在两点：**尸体物理预算**（最值得做的 mod）与 **mod 自身查询纪律**（勿重复造轮子、勿滥用 trigger/OverlapSphere）。
