# 单位攻击/交战逻辑逆向分析（0.9.11 时点，纯元数据 + 实测反推）

> 来源：Il2CppDumper 全量元数据（research_out/il2cpp_native_20260830/dump.cs + DummyDll）。
> IL2CPP 方法体不可读，本文所有"行为"结论均由【成员结构 + 指针偏移 + mod 实测】反推，
> 标注 [推] 的为推断，其余为元数据实锤。

## 1. 已还原的命令与决策结构

### 订单枚举（Squad.Order，实锤）
```
follow = 0, defend = 1, attackFromSide = 2, charge = 3
```
- `Squad.order` 是 **public 字段（0x34）**，可直接从 mod 读写。
- mod 现有命令与订单的对应 [推]：moveTo→defend(1)、HoldArea→defend(1)、
  Charge→charge(3)、FollowLeader→follow(0)、attackFromPoint→attackFromSide(2)。
- `Squad.SynchOrder(byte orderID, Vector3 pos, float radius)` 是把订单同步给成员的底层通道。

### 小队任务系统（实锤）
- `Squad.currentTask : SquadTask`（public 字段，0x60）——小队当前执行的任务。
- `SquadTask` 是接口：GetTaskDestination / UpdateTaskStatus / IsInsideTaskArea / GetTaskString。
- 任务实现：MissionTask(ConquerArea / PushFrontline / Scripted)、MissionTaskDestroyVehicles、
  MissionTaskRetreat、MissionTaskClearArea、MissionTaskHighlightPosition。
- 任务下发：`Squad.CalculateSquadTask(SquadTaskBehaviour behaviour = auto, Vector3? closePos)`，
  behaviour ∈ {auto=0, closer=1, random=2}（只决定任务点选取方式，不是交战优先级）。
- 任务吸引：`GetClosestNonSecuredMissionObjective` / `GetRandomNonSecuredMissionObjective` /
  `TaskCanStillAttract(faction)`——战役目标（未占领区等）会持续把小队拉向任务区。
- **`SquadLeaderRoutine(Soldier character, bool orderAttackTask = true)`**——班长例程，
  默认参数就是"下达攻击任务"。
- 目标（Objective）：Lua_Squad.hasObjective / getObjectivePosition / getObjectiveRadius /
  setClosestObjective / setRandomObjective——小队带战役目标时 [推] 会自主推进并接战。

### 士兵级决策（SoldierAI，实锤字段）
- `visibleTarget : Spottable`（0x48）——当前可视目标；`GetBestVisibleEnemy()` 负责获取
  （**本 mod 集火功能已在 Harmony patch 此方法**，Postfix 覆盖返回值）。
- `allowFireAtEnemy`、`aimingEnemy`、`targetInWeaponRange`、`moveLookingTarget`、
  `ShouldLookTargetWhileMoving()`——士兵具备"移动中瞄准/射击"的原生能力。
- `MovementRoutine(dt)` / `CalculateMoveOrderDestination()`——移动主循环按"移动订单目的地"驱动。
- AiParams（每兵开关）：allowCheckForEnemies / allowFindCoverWhenSuppressed /
  allowOpenWindows / changePoseAutomatically / allowMovements …（0.9.11 已用的两个）。

## 2. "移动命令被打断"的两条候选路径

### 路径 A：班长例程的攻击任务 [推，与实测吻合]
SquadLeaderRoutine(orderAttackTask=true) 周期运行 → 班长/成员看见敌人
（visibleTarget 非空）→ 下发 attackFromSide/charge 类任务 → `currentTask` 覆盖
per-soldier 移动订单 → 全队脱离行军队列接战。
**证据**：0.9.11 已每秒重申 allowCheckForEnemies(false)+allowFindCoverWhenSuppressed(false)
仍被打断——AiParams 是士兵级开关，管不住小队级任务下发。

### 路径 B：战役目标吸引 [推]
小队带 Objective（hasObjective=true）时，TaskCanStillAttract + MissionTask
（ConquerArea/PushFrontline）持续把小队拉回任务区，顺路接战。
**证据**：Lua_Squad 专门暴露了 objective 读写面；MissionTask 家族存在且名为
"占领/推线/清区"。

## 3. 控制点排名（下一轮实现的候选，按强度排序）

1. **Soldier.GetBestVisibleEnemy 条件前缀**：行军单位（obsNoEngage 名单内）
   Prefix 跳过原方法、返回 null——目标获取被彻底切断，A 路径的触发源消失，
   B 路径的"顺路接战"也不会发生。本 mod 已 patch 该方法，加条件即可。
   预期效果：行军=绝对移动优先（代价：行军完全不还手，与现状说明一致）。
2. **Squad.currentTask 周期置空**：obs 活跃时每秒把受控小队的 currentTask 置 null，
   针对已分配任务的兜底（任务可能被 CalculateSquadTask 反复重算，需与 1 组合并重申）。
3. **order 字段直写**：`sq.order = Order.defend` 显式设防型订单（不再依赖 moveTo 的隐式订单）。
4. **拦截 SquadLeaderRoutine / CalculateSquadTask**（Harmony Prefix，仅对受控小队跳过）：
   强度最高但副作用面最大（例程可能还承担语音/姿态等），仅在前两个不奏效时考虑。

## 4. 实证探针（先诊断后动手，一轮测试定位路径 A or B）

打断瞬间（"x/y 已到位"进度停滞且单位脱队时）记录受控小队状态：
```
[SquadCmd] 探针 小队=0x… order=Defend/Attack… currentTask=<类型名或null>
           hasObjective=… visibleTarget=非空/空
```
- currentTask 非空 + 变化 → 路径 A 实锤 → 上控制点 1+2。
- hasObjective=true → 路径 B 参与 suffered → 控制点 2 + 评估清除 objective 的副作用。
- 两者都无 → 打断在士兵级（覆盖/姿态/卡路径），回到 AiParams 层继续查。

## 5. 结论

- 现状（0.9.11 的 AiParams 压制）管不住小队级任务，升级到控制点 1（GetBestVisibleEnemy
  条件前缀）+ 控制点 2（currentTask 置空）是下一轮的正解组合。
- 全部为论文，未改任何行为代码（遵用户指示）。
