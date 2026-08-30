# ER2 SquadCommand — 项目交接说明（给 GPT）

> Easy Red 2（IL2CPP 引擎）的 BepInEx 插件：上帝视角 RTS 小队指挥。
> 本文档是给接手开发的 AI/开发者看的完整上下文。源码在根目录扁平结构（`Plugin.cs`/`GodViewController.cs`/`SquadCmdLogic.cs`），参考文档在 `docs/` 和 `research/`。

## 1. 项目是什么

按 F9 进入"上帝视角"（自由俯瞰战场的 RTS 视角），长按左键框选友军做临时选择（不拆原生小队），右键下达指令（移动/集火标记/上车/前往并防守），空格暂停，顶部按钮接管任意选中单位继续第一人称战斗。行动逻辑尽量走游戏原生命令链（moveTo/HoldArea/Charge/boardVehicle），Mod 不自建移动编排。

- 插件名：`ER2 Squad Command`，GUID `er2.squadcommand`，当前版本 **0.8.0**（扁平结构，对齐其它 mod）
- 源码：`Plugin.cs`（Harmony patch 装配）、`GodViewController.cs`（上帝视角/选择/指令核心，最大）、`SquadCmdLogic.cs`（小队查询/控制登记/日志）
- 构建：`scripts/build.ps1 -Mod SquadCommand`（构建+部署+清cfg+打包；游戏在跑会自动轮询）或 `dotnet build -c Release SquadCommand.csproj`
- csproj 引用游戏 interop：`E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\interop\*.dll`，已排除 `research/docs/bin/obj/src/deps` 避免污染

## 2. 当前功能状态（0.7.10）

### 已工作
- F9 进入/退出上帝视角；WASD 移动、滚轮缩放、中键旋转、空格暂停
- 左键：单击选中友军步兵/载具、Shift 追加、框选真实建队（`Squad.FindOrNew`+`Join`）、双击选整队、点空白清空选择。**左键只负责选择/取消选择**
- 右键（**直接下达指令，无菜单无轮盘**）：
  - 空白地面 → 选中步兵 `Lua_Soldier.moveTo`（三连释放 AI 命令）+ 选中载具车组 `Lua_Squad.moveTo` 开过去；**标点 ◎ 在点击处**（0.7.9 修复了 Y 轴上下颠倒：`IssueDirectCommand` 必须用 `Input.mousePosition`（Unity 左下原点）传给 `ScreenPointToRay`，不能用 `MouseGui()`（IMGUI 左上原点））
  - 敌军士兵/载具 → **标记集火**（替代原 forceTarget 攻击，用户实测 forceTarget 从未生效）
  - 友军载具未选 → 逐员 `Lua_Soldier.boardVehicle` 上车（先 `SetLocked(false)` + `countEmptySeats` 限流）
  - 中立载具/火力点（阵营空或非对立）→ 上车，不当敌人打
  - 右击已选载具 → 开过去（不下车）；友军士兵坐在载具里 → 上车
- 标记集火机制（HVT 老兵团已实证有效）：
  - `Plugin.cs` 的 `MarkedTargetSelectionPatch`：Postfix 覆盖 `Soldier.GetBestVisibleEnemy`，**只对选中的单位**（`IsSelectedUnit`）生效，把被标记目标当作最佳可见敌人（半径 500m + 视线内）
  - `MarkedVehicleTargetPatch`：Postfix 覆盖 `Vehicle.CurrentVisibleTarget`（getter），**只对选中的载具**（`IsSelectedVehicle`）生效，让载具炮塔/武器也集火标记目标（解决"载具里单位不行动"）
  - 标记目标：`GodViewController.MarkEnemySoldier/MarkEnemyVehicle`，记录 `Spottable`+位置+阵营，持续 `markDuration`（默认 20s），红色 ◆ 标记显示
- 右下角小队列表（编号 + □装甲○步兵 符号，无人数括号），单击选中/双击飞过去
- 顶部 [控制该小队] 按钮随机接管一名选中成员并退出上帝视角

### 已知问题 / 用户需求（按重要性）
1. **载具内单位打标记目标**：`Vehicle.CurrentVisibleTarget` 覆盖已加，但**未实测是否真的让炮塔开火**——用户说"载具里单位攻击目标但单位不会行动"，需实测确认
2. **原生班长指挥菜单**：用户多次要求"原生的班长指挥菜单"。0.7.7 尝试过反射调用 `PlayerController.OpenOrdersMenu(true,true)`（`AccessTools.Method`），但用户反馈"标点不在点击处"（原生指令目标是玩家瞄准点而非右键点击点）+ "不要转轮，形式不重要，我要的是原版功能的使用"→ 0.7.8/0.7.9 改为右键直接指令。**如果用户还要原生菜单，需解决"指令目标=点击点"问题**（见 research/PlayerController 的 `OpenOrdersMenu`/`_OpenOrdersMenu_b__182_0`）
3. 上车对**无载具父级的纯火力点（TurretGun）**仍可能失败：`Lua_Soldier.boardVehicle` 需要 `Vehicle`，纯 TurretMG 没有 → 目前 `NearbyFriendlyVehicle`（OverlapSphere 5m）兜底找附近载具
4. 用户不满沟通历史：做过自定义转轮（被否）、自定义垂直菜单（被否）、原生菜单反射调用（标点错位）——**结论：右键直接指令，不要任何菜单/转轮 UI**

### 用户明确的操作约定
- 左键 = 只选择/取消选择（不攻击不乘车）
- 右键 = 直接指令（空白移动/敌军标记/载具上车），标点必须就在点击处
- 空格 = 暂停（菜单里不要再加暂停项）
- 攻击功能已废 → 全部替换为标记集火
- 不要转轮/自定义菜单 UI

## 3. 关键机制与陷阱（踩过的坑，改代码前必读）

### 命令体系
- 移动：单兵 `Lua_Soldier.moveTo(pos)` 必须配合 AI 三连释放：`getAiParams().followCustomSquadOrders()` + `followCustomDirectCommands()` + `allowMovements(true)`，否则被 AI 状态机覆盖不动（`GodViewController.MoveUnits` 已封装）
- **对"已完成上一次移动"的单位重复 moveTo 无效**（0.7.16 实测：纠正日志连续下发但单位不动）→ 追加移动前必须先 `Lua_Soldier.stop()` 复位任务状态（0.7.17）
- 小队移动：`Lua_Squad.moveTo(pos, radius)`（`GodViewController.DriveVehicleTo`）
- **forceTarget 无效**（用户实测），攻击一律走 GetBestVisibleEnemy 覆盖（标记）
- **跟随无原生"跟随指定单位"API**（`Squad.FollowLeader(bool)` 只跟自己队长；Join 对方小队会毁掉 RTS 控制权）→ 自建快照状态机：命令绑定下令时的单位集合（换选不取消），掉队超距或目标偏离上次引导点≥8m 时以 ≥3s 间隔纠正（`GodViewController.FollowTick`）
- **轮盘点击的时序陷阱**：OnGUI `e.Use()` 只管 IMGUI 事件，同帧 legacy Input 照样为真；且执行动作关环后，同一次按压的"松开"落在下一帧普通点击逻辑里会静默清空选择 → 关环必须吞掉剩余手势（`swallowLeftGesture`）
- `PlayerGUI.squad` / `PlayerGUI.GUISquad` = 原生"当前指挥小队"，原生指令（OpenOrdersMenu 回调）作用对象

### 屏幕坐标（0.7.9 刚踩的坑）
- `Input.mousePosition`：Unity 左下原点，**传给 `Camera.ScreenPointToRay` 必须用它**
- `MouseGui()` = `(mouse.x, Screen.height - mouse.y)`：IMGUI 左上原点，只用于 GUI 绘制/命中测试
- 两者混用 = 标点上下颠倒（已修，别再犯）

### IL2CPP
- interop 返回基类包装，类型转换必须 `TryCast<T>()`，`as` 恒失败
- 所有 interop 访问 try/catch（单位可能在遍历中被销毁）
- Harmony patch 大量目标统一 `Prefix => !GodViewController.Active` 模式
- 版本号必须 `x.y.z`（禁字母后缀），BepInPlugin 版本 + 启动日志字符串两处同步

### 原生菜单（如需重做）
- `PlayerController.OpenOrdersMenu(bool is_leader, bool is_radioman)` 是 private 原生方法（managed stub），可反射调用；菜单是 `CircularMenuSelection.CircularMenu2.ShowCircle(Il2CppReferenceArray<CircularMenuData>, GameInput)`
- 陷阱：`CircularMenu2.ShowCircle` 数据替换后原生回调匹配不到条目（选了没反应）——能走原生管线就走原生
- 用户"原版功能"诉求 = 想让游戏原生指令（移动/进攻/固守/跟随/停火等）作用到框选单位；但原生指令目标=玩家瞄准点，与 RTS 点击点不符（0.7.7 实测反馈）

## 4. 文件清单（已扁平化，对齐 HighValueTarget/AIFood 等）

- 根目录 — 插件源码（`Plugin.cs` / `GodViewController.cs` / `SquadCmdLogic.cs` / `SquadCommand.csproj` 扁平，无 `src/` 嵌套；`SquadCommand.csproj` 已排除 `research/docs/bin/obj/deps/src`）
- `docs/` — `SquadCommand_lessons.md`（开发经验教训）、`v0.7_design.md`（交互设计）[ `README.txt`/`Nexus_description.md` 已去重，仅保留根目录一份 ]
- `research/` — 反编译的游戏 API 参考（已在 csproj 中 `Compile Remove`，不参与编译）：
  - `PlayerController.decompiled.cs`（OpenOrdersMenu / ShowCircle 相关）
  - `Squad.decompiled.cs`（order 枚举字段、SynchOrder、SquadAnswerMoveOrder/ChargeOrder、SetHoldFireOrder、OrderLeaveAllVehiclesAndCovers、Leader_OrderAttackCurrentTask、FollowLeader）
  - `Lua_Soldier.decompiled.cs`（moveTo/boardVehicle/forceTarget/getAiParams/isInsideVehicle）
  - `Lua_Squad.decompiled.cs`（moveTo/boardVehicle/followLeader/holdFire/fireAtWill）
  - `Lua_Vehicle.decompiled.cs`（countEmptySeats/repair/brake）
  - `report_ai.md`（AI 系统报告：GetBestVisibleEnemy 是炮塔/武器选目标官方判定点）
  - `report_vehicles_misc.md`（载具系统报告：Vehicle 方法/继承链）

## 5. 构建与测试

```powershell
# 构建+部署+清cfg+打包（游戏运行中自动轮询等待）
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod SquadCommand
# 或仅编译
dotnet build -c Release SquadCommand\SquadCommand.csproj
# 测试后查日志
Select-String "E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\LogOutput.log" -Pattern "SquadCmd"
```


## 6. v0.7.34 交接快照（2026-08-28，交给网页 GPT 的续作上下文）

### 当前功能态（实测可用）
- F9 上帝视角（相机/光标 patch 面稳定）；框选=临时指挥（不建队，虚拟选择）
- 右键短按=直接指令（地面移动/敌我判定/单位类标记），**长按0.35s=单位环**（站起[resetPose]/蹲下/趴下[setPose+还原名单]/停止）
- 右键载具/车内兵=交互环：上车（逐员+限流+成功后**转选车组**，车组成员抽成新真实小队）/下车/修理（Squad.OrderRepairVehicle，门控=纯原生小队）/合并
- 右键徒步友军=友军环：**合并**（并入目标所在小队，上限12人，溢出留守原队；坦克可并入坦克车组）
- 叛徒标记（右键敌人/中立 Spottable 单位）：持久优先集火（GetBestVisibleEnemy/Vehicle.CurrentVisibleTarget Postfix + MarkLosCached 0.5s 缓存）+ M7 推进（mvFromMark 状态机：距离停带→只修正停滞单位）
- M7 移动 Command State：TickMove 每2s体检，全员到位=完成，停滞=逐单位 stop+三连释放+moveTo
- 退出 RTS：RestoreOriginalSquads（原队映射还原）+ RestoreAllPoses + ClearFollow/Mark + 独苗转移

### 死亡/换队链（0.7.41 稳定基线，用户已冻结）
- 空候选卡死已解决：真因=我们调用 ClearSquadList() 清空原生标签池（0.7.40 停用）+ EnsurePlayerSquadHasCandidates 劫持原生"选择新小队"（0.7.41 停用）
- A/B 双场景实机验证通过（有队友→选择队友正常；全灭→原生选择新小队正常）
- **冻结清单（除非新的可复现死亡 Bug）**：ShowSquadList / ShowSwitchMemberSelection / DeathPanel / Respawn / EnsurePlayerSquadHasCandidates / ClearSquadList / DeathGuard 一律不再改
- 死亡观测设施保留：[DeathTrace]/[SquadTrace]/[DeathGuard detected death] 日志仍在线

### 已知未解（下一手）
- **死亡后"选择队友"0 候选**（复现：接管后用 HVT 叛徒机制让队友杀死自己）：
  已试无效：重指 PlayerGUI.squad/GUISquad（EnsurePlayerSquadHasCandidates）、空候选拦截、LateUpdate 屏蔽（反而冻结重生）。
  主因判断：**死亡时原生把单位移出小队，ControlledCharacter.joinedSquad 断链，ShowSquadList 从该引用现取候选=0**。
  下一手：Harmony patch ShowSquadList(前缀改 squad 参数)注入最大友军队；或死亡前预挂队。
  注意：EnsureSquadSelectionClosed/DeadNoCandidates 残迹可删；SuppressingUi 已做"死亡即放行"。
- 其它 mod 的 Soldier.OnDestroy NRE（非本 mod）可忽略。

### 关键陷阱新增（在既有 #1-26 之外）
27. 右键手势=状态机（RightLongPressSeconds=0.35）：按下只计时，短按用**按下时坐标**下达；开环按压由 WheelOpenGuard 豁免；关环 swallowLeftGesture 吞同手势余段（否则松开泄入 ClickActOrCancel 清空选择）
28. Python 批量改源码必须 assert 每处替换+最后 grep 版本行——0.7.20~0.7.28 曾三次静默失败（版本号漂移、Active getter 没打上、删代码误删相邻函数）
29. 删功能时删除范围要人工复核相邻成员（0.7.27 删冲刺误删 IssueDirectCommand 调用，右键五轮没修好的真因）
30. 互操作异常≠对象失效：PruneMark 类清理逻辑失败需连续计数(3次/1.5s)再判死，否则标记被瞬时 NRE 连坐抹掉
31. 登车不拆小队：乘员名单跨多车时显示/指挥要按"成员→所属全部载具→在车人员"展开
32. 接管后 8s 抑制窗内死亡会挡 RespawnPanel——SuppressingUi 必须带"受控单位存活"检查

### 构建/部署/验收（每轮必做）
- 构建源：SquadCommand/src/ER2_SquadCommand.csproj（源码在父目录，Link 引用）
- `dotnet build src/ER2_SquadCommand.csproj -c Release` → cp 到 `E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\plugins\`
- **验收三件套（漏一=没部署完）**：双端 sha256 一致 + 日志 `Loading [ER2 Squad Command x.y.z]` 与 Plugin.cs 版本一致 + Plugin.cs 内版本字符串 grep 计数≥2
- 测试后 `Select-String LogOutput.log -Pattern "SquadCmd|RMB↓"`


## 7. Vehicle/分队专题归档（0.7.43-0.7.55，已闭环）

### 最终架构（0.7.55）
- 选择隔离：`selVehicleRefs`（被点 Vehicle 真实引用）；◆光标/下车/标签按 refs 实时展开，不经共享 Squad
- 控制权：Vehicle 选择 → 乘员 Soldier 级注册（controlledSquads 不因 Vehicle 选择增加）
- **上车 = 自动分队**：BoardVehicle 发起 boardVehicle → 排队 1s → 快照全员 Leave 原队/Join crewSq → `rtsSquad = crewSq`
- 车辆驾驶：`Lua_Squad(ai.squadInside).moveTo()`（=rtsSquad）→ SynchOrder → AIVehicle 决策链（order 0→1 实证）
- squadInside 收敛慢/未收敛 → VehiclePendingTick 0.25s 重试补发；登车未完成 → BoardPendingTick 超时提示
- 手动【分队】按钮：当前选中 Soldier[] → 新 Squad，上限 12 人，坦克可并坦克

### 铁律（三轮日志实锤，勿再犯）
33. **`Lua_Soldier.boardVehicle()` 进车是异步且不确定的**——同帧 `IsInfantry`（GetComponentInParent<Vehicle>/isInsideVehicle）对"首次从徒步登车"的乘员**永远不翻转**（0.7.52 A车3/4 是"此前已在车"的乘员）。转队/登记严禁依赖该检测，用短延迟后无条件执行（Leave/Join 与 transform 无关）
34. **`AIVehicle.setVehicleDestination` 是原生决策循环的输出位**（VAI_Movable.UpdateMoveDestination 以 squadInside 为输入每帧重写）——直写会被覆盖（0.7.45/0.7.47 DESTINATION_OVERWRITTEN 实锤）。车辆移动唯一正道 = Squad 原生订单链（Lua_Squad(squadInside).moveTo）
35. **rtsSquad 是单指针**：多车各自上车会互相覆盖（BoardVehicle 每次CreateNewSquad）；多车独立驾驶需另立多 RTS Squad 管理任务
36. **Lua_Squad(squad).moveTo 改写 Squad.order**（0→1 实证）——这是"命令已下达"的可观测判据；order 不变=命令没写进去
37. 驾驶成功闭环判据：`[VehicleMove] via=Lua_Squad` + `[VehicleOrderCheck] BEFORE order=0 → AFTER order=1` + 车辆位移

### 冻结清单追加
- DriveVehicleTo 的 Squad 原生命令链 / BoardPendingTick 延迟转队 / VehiclePendingTick 移动补发：与死亡链同级冻结，除非出现新的可复现 Bug
