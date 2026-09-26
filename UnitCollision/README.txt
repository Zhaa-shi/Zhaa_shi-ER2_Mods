ER2 More Physics - Unit Collision v1.0.9
=========================================

【中文】

ER2 More Physics 的轻量附属模组：只保留"单位/尸体碰撞"功能，其余
（场景物件物理化、物品物理、击飞/碰撞伤害等）全部移除。

- 士兵互相阻挡：活体士兵复用原版贴合人体的受击碰撞体互相阻挡，
  玩家/AI 不再互相穿过（零新增体积）。
- 尸体不挡活人：士兵/玩家撞到尸体时把它推开，不会从尸体上直接走过。
- 独立开关：士兵互碰（UnitCollision）与推开尸体（PushCorpses）可分别关闭。

v1.0.9 改动
-----------
- **修复：日志刷屏**。单位矩阵探测的心跳日志此前**无条件**每 10 秒打一条
  （`UC: unit-matrix playerCcLayer=... `），一场战斗几十条，看起来像报错刷屏。
  现在挂到新的 `Debug / debugLog` 开关下（默认关）——探测逻辑一字未动，只是不再刷日志。
  排查碰撞问题时把开关打开即可恢复这些诊断。

v1.0.8 改动
-----------
- 修复（重要）：碰撞矩阵改双向——此前只开不关，在 Mod Manager 里关闭
  开关后本局已打开的矩阵仍保留（表现为"关了还有碰撞"）。现在任一开关
  （总开关/士兵互碰/推开尸体）从开→关时，立即把本 mod 打开过的矩阵对
  恢复成游戏默认状态，无需重启游戏。
- 修复：士兵互碰的位置级防重叠此前只要"推开尸体"开着就会生效；现在
  严格挂在 UnitCollision 下，AI-尸体交互严格挂在 PushCorpses 下。
- 尸体物理（刚体动态化/推尸组件挂载）也改为仅在 PushCorpses 开启时执行。

v1.0.7 改动
-----------
- 诊断增强：战斗开局单位未生成时 CC 层探测会锁进兜底固定层 1——兜底后
  保持重探，拿到真实 CharacterController 层自动改判（层矩阵开错一对的
  话玩家照样穿过 AI）。
- 诊断模式（UnitCollisionLayer=-2）新增活体受击碰撞体状态 dump：活体
  士兵的 CC 层/启用状态 + 受击碰撞体的 层/是否触发器/是否启用 聚合
  计数（10 秒一条），用于排查"还能穿过 AI"类反馈。

v1.0.6 改动
-----------
- 性能优化：活体两两检测的位置/玩家标记/半径改为每帧预计算缓冲
  （O(n²) 次 IL2CPP 调用降为 O(n)），大场面不掉帧。
- 移除冗余方法，日志保持 10 秒限频。

v1.0.5 改动
-----------
- 修复（根因）：ragdollized 尸体不在 aliveCreatures 里，导致 AI-尸体
  分支从未持续执行（扫描结果还被每帧清空丢弃）。现在尸体列表每帧可用：
  数据源改为 aliveCreatures + allCreatures（全单位表）+ 全场景扫描缓存
  三层合并去重，扫描缓存 1 秒刷新且跨帧复用。

v1.0.4 改动
-----------
- 修复：AI 推尸体的数据源——尸体不再只从 aliveCreatures 找（游戏可能
  把 ragdollized 单位移出该表），找不到时自动用全场景 RagdollManager
  扫描兜底，日志标注来源（src 0=单位表 / 1=场景扫描）。
- 修复：尸体推动改为混合策略——游戏冻结（kinematic）的刚体直接平移
  transform（不依赖物理引擎，怎么冻结都推得动）；动态刚体速度直写。
- 尸体判定改用 IsDead 优先（比 ragdollized 可靠）。

v1.0.3 改动
-----------
- 修复：AI 撞尸体改为对尸体所有骨骼刚体"速度直写"同一水平速度（整体
  平移），不再依赖冲量/关节传力——AI 移动时会真正把尸体推开。
- 静止的 AI 与尸体重叠时会把 AI 推出尸体（不再站在尸体上穿模）。
- 诊断日志加入 living/corpses/aiPairs 计数，便于确认触发链路。

v1.0.2 改动
-----------
- 修复：AI 撞尸体时把尸体整体推开（此前只推单个骨骼刚体，会被布娃娃
  关节吃掉；且只在 AI 移动时才推，避免静止 AI 反复推导致尸体抖动）。
- 修复：尸体抽搐——尸体物理（刚体动态化 + 碰撞体）只在活人 5 米内启用，
  远处尸体交还游戏原生管理，不再与游戏尸体物理暂停机制打架。
- 新增诊断日志：`UC: overlaps resolved (... corpse pushes N)` 与
  `UC: corpse physics active (...)`（均为 10 秒一条）。

v1.0.1 改动
-----------
- 修复：AI 单位此前不参与碰撞（AI 走 NavMeshAgent 导航、不走物理）。
  现在 AI 之间互不穿过、AI 撞玩家不再把玩家推着走（只推 AI）、
  AI 撞尸体同样会把尸体推开。
- 尸体碰撞体在 AI 附近也会启用（此前只在玩家附近启用）。
- 层探测优先取玩家再兜底，兼容 AI 无 CharacterController 的情况。

安装
----
1. 安装 BepInEx 6（IL2CPP）。
2. 把 ER2_MorePhysics_UnitCollision.dll 放入 <Easy Red 2 目录>/BepInEx/plugins/。
3. 启动游戏，在游戏内 Mod Manager（模组设置页）里配置。

配置
----
游戏内 Mod Manager 或 BepInEx/config/er2.morephysics.unitcollision.cfg。

General：
- Enabled：总开关。
- SingleplayerOnly：默认仅单机生效（默认开）。

Collision：
- UnitCollision：士兵互相阻挡（默认开）。
- UnitCollisionLayer：调试项；-1 = 正常，-2 = 诊断模式（只打印碰撞
  矩阵与碰撞体信息，不改物理）。
- PushCorpses：单位推开尸体（默认开）。
- CorpsePushForce：推开尸体的冲量力度（0 = 尸体只阻挡不被推开）。

语言适配
--------
中文版（_CN_ 包）固定中文；英文版会自动跟随游戏当前语言。

与 ER2 More Physics 的关系
--------------------------
本模组是 ER2 More Physics 的附属轻量版，仅含其单位/尸体碰撞功能。
若同时安装完整版 More Physics，两者的碰撞逻辑各自生效
（建议只装其中一个，避免重复开启碰撞矩阵）。

=======================================================================

ER2 More Physics - Unit Collision v1.0.9
=========================================

A lightweight companion mod to ER2 More Physics that ONLY keeps the
unit/corpse collision features (scene-object physicalization, item physics,
knockback and impact damage are all removed).

- Living units block each other: reuses the vanilla body colliders so the
  player and AI cannot pass through each other (zero added volume).
- Corpses do not block living units: soldiers/player shove them aside
  instead of walking over them.
- Independent toggles: UnitCollision (unit blocking) and PushCorpses
  (corpse shoving) can be turned off separately.

v1.0.8 changes
--------------
- Fix (important): the collision matrix is now two-way - previously it was
  only ever opened, so disabling the toggles in the Mod Manager left the
  already-open matrix in place for the rest of the session ("turned it off
  but units still collide"). Any switch (master / unit blocking / corpse
  shoving) going off now immediately restores the vanilla collision matrix -
  no game restart needed.
- Fix: the position-level living-unit separation previously ran whenever
  corpse shoving was on; it is now strictly tied to UnitCollision, and the
  AI-corpse interaction is strictly tied to PushCorpses.
- Corpse physics (rigidbody activation / pusher component mounting) now only
  runs while PushCorpses is on.

v1.0.7 changes
--------------
- Diagnostics: the CC-layer probe could lock onto the fallback layer (1)
  when probed before any unit had spawned; it now keeps re-probing after a
  fallback lock and re-judges once a real CharacterController layer shows
  up (a wrong matrix pair would let units pass through each other).
- Diagnostics mode (UnitCollisionLayer=-2) now dumps living units' hit
  collider state (CC layer/enabled plus per-collider layer/trigger/enabled
  aggregates, throttled to one line per 10s) to troubleshoot "can still
  pass through AI" reports.

v1.0.6 changes
--------------
- Performance: per-pair positions/player flags/radii are now precomputed
  per frame into buffers (O(n^2) IL2CPP calls reduced to O(n)) - large
  battles stay smooth.
- Removed a redundant helper; logs remain throttled to 10s.

v1.0.5 changes
--------------
- Fixed (root cause): ragdollized corpses are not in aliveCreatures, so the
  AI-corpse branch never ran persistently (the scene-scan result was also
  being cleared every frame). The corpse list is now available every frame:
  sources are merged and de-duplicated from aliveCreatures +
  allCreatures (full unit table) + a cached full-scene RagdollManager scan
  (refreshed every 1s and reused across frames).

v1.0.4 changes
--------------
- Fixed: corpse data source - corpses are no longer only looked up in
  aliveCreatures (the game may remove ragdollized units from that list);
  a throttled full-scene RagdollManager scan is used as fallback, and the
  log marks the source (src 0 = unit table / 1 = scene scan).
- Fixed: corpse shoving now uses a hybrid strategy - frozen (kinematic)
  rigidbodies are translated directly (no physics engine needed, works no
  matter how the game freezes them); dynamic rigidbodies get their velocity
  written directly.
- Corpse detection now prefers IsDead (more reliable than ragdollized).

v1.0.3 changes
--------------
- Fixed: AI now shoves corpses by writing the same horizontal velocity to
  every corpse bone rigidbody (whole-body translation), instead of relying
  on impulses that ragdoll joints absorb - a moving AI really pushes the
  corpse aside now.
- Idle AI overlapping a corpse is pushed out of it (no more standing
  inside the corpse).
- Diagnostic log now includes living/corpses/aiPairs counters to confirm
  the trigger chain.

v1.0.2 changes
--------------
- Fixed: AI now shoves the whole corpse aside (previously only a single
  bone rigidbody was pushed, which ragdoll joints absorbed; pushes only
  happen while the AI is actually moving, so idle AI no longer jitters
  corpses).
- Fixed: corpse twitching - corpse physics (dynamic rigidbodies +
  colliders) is only activated within 5m of a living unit; distant corpses
  are left to the game's native management instead of fighting its corpse
  physics pause logic.
- Added diagnostic logs (10s interval): "UC: overlaps resolved (... corpse
  pushes N)" and "UC: corpse physics active (...)".

v1.0.1 changes
--------------
- Fixed: AI units previously did not participate in collision at all
  (AI moves via NavMeshAgent, not physics). Now AI units block each other,
  AI no longer shoves the player around when walking into them (only the
  AI is pushed), and AI shoves corpses aside just like the player.
- Corpse colliders are now also enabled near AI units (previously only
  near the player).
- Layer probing now prefers the player's layer with a documented fallback,
  compatible with AI units that have no CharacterController.

Installation
------------
1. Install BepInEx 6 (IL2CPP) for Easy Red 2.
2. Copy ER2_MorePhysics_UnitCollision.dll into:
   <Easy Red 2 folder>/BepInEx/plugins/
3. Start the game. Configure settings in the in-game Mod Manager (MODS page).

Configuration
-------------
Open the in-game Mod Manager (MODS page) or edit
BepInEx/config/er2.morephysics.unitcollision.cfg.

General:
- Enabled: Master switch.
- SingleplayerOnly: Apply only in singleplayer (default on).

Collision:
- UnitCollision: Units physically block each other (default on).
- UnitCollisionLayer: Debug only; -1 = normal, -2 = diagnostics (logs
  collision matrices and collider details, changes nothing).
- PushCorpses: Units push corpses aside (default on).
- CorpsePushForce: Impulse strength used to shove corpses
  (0 = corpses block but are not shoved).

Language
--------
The _CN_ package is fixed Chinese; the regular build follows the game's
current language at runtime.

Relation to ER2 More Physics
----------------------------
This mod is the companion lightweight version of ER2 More Physics and only
contains its unit/corpse collision module. If both are installed, their
collision logic acts independently - it is recommended to run only one.
