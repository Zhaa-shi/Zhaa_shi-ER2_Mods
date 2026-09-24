# ER2 zcode 时代成果（2026-08-27 → 2026-09-06）

> 本文件补上**仓库文档的断档**：`ER2_mod_dev_guide.md` / `ER2_mod_经验.md` 的正文停在 **2026-08-30**，
> 之后到 09-06 的开发（通过 **zcode** 工具进行）只留在 git 提交信息里。本文件把这段成果固化为可复用知识。
> **git 依据**：`83cbd25`（08-29 首个版本库基线，SquadCommand 0.7.71 全功能）→ `1eccc5b`（09-06 末次）。

## 时间线（里程碑）

| 时间 | 提交 | 事件 |
|---|---|---|
| 08-29 | `83cbd25` | **首个 git 基线**：SquadCommand 0.7.71 全功能 + 各 mod 源码 + 文档入库 |
| 08-30 | `2ed9d3c`–`f48db85` | SquadCommand 0.9.0–0.9.11 高频迭代（UI 主题/单位标记/M 地图/行军压制/死亡链） |
| 09-05 | `bb1eb03` | **游戏 2.1.x 兼容**：NoInteractionHints 4.5.3 修 `PhaseBarGUI` 类型缺失导致的 TypeLoadException |
| 09-05 | `d6b6b51` | **全工作区移除 PhaseBarGUI 依赖**（字体兜底 + 条件 patch）——NoInteractionHints / ModManager / LimbTweaks / WeatherControl |
| 09-05 | `19e0677`–`10f7ce3` | ModManager 1.1.5→1.1.9（字母分组视图 / 滚动修复 / **空列表卡死真凶** / 坏高度泄漏） |
| 09-05 | `5a897e1` | 工作区大清理：research_out 平铺 250 类型、删 397 MB 一次性研究大件、SquadCommand 去 src 双源 |
| 09-05 | `1ef2702` | **双语发布包机制**：Nexus 描述拆中英两版，build.ps1 按包语言取文件 |
| 09-05 | `93ba347` | **UniversalGeneration 1.0.0 诞生** + SquadCommand 1.0.1（`externalGuiBlock` 挂点） |
| 09-06 | `faa5be2`–`813be7c` | SquadCommand 1.0.2–1.0.6（载具朝向拖动 / 路线显示 / 转向速度校准） |
| 09-06 | `4d8f2e4` | **SquadCommand 1.1.0 发布版**（功能定格 + 双语 README/Nexus） |
| 09-06 | `1eccc5b` | 末次：清理遗留 zip 与历史发布包 |

---

## 1. SquadCommand（Battlefield Commander）v1.2.19 — RTS 上帝视角指挥

> 源码：`Plugin.cs`（Harmony 装配）、`GodViewController.cs`（**核心**）、`SquadCmdLogic.cs`、`SceneMarkers.cs`、`VehicleFacing.cs`、`Formation.cs`（1.2.0 阵型）、`GhostPreview.cs`（1.2.0 幽灵预览）、`InfoPanel.cs`（1.2.0 信息面板）、`Ui.cs`。
> 上下文文档：`SquadCommand/GPT_CONTEXT.md`（交接说明 + 铁律 1-37）、`ROADMAP.md`（完成度评估）、`README.txt`/`README_CN.txt`。

### 1.1 功能全貌（1.1.0 → 1.2.0 增补见末尾）

- **F9 进出上帝视角**（含全军覆没时紧急退出）；WASD 移动 / 滚轮缩放 / 中键旋转 / Q·E 升降 / 空格暂停（暂停时相机仍可动）
- **选择**：左键单击（步兵=单人；载具=整个车组）/ 双击=整队 / Shift 追加 / **长按左键框选**（临时选择，**不拆原生小队**）/ 点空白取消。**左键只负责选择**
- **右键短按 = 直接指令**（无菜单无轮盘）：地面→移动；敌人→**持久集火标记**（只给火力优先，不自动推进）；友军/中立载具→交互环
- **右键双击**（同点 0.6s 内）= 原生「前往并防守」（`HoldArea`），每队一条原生命令
- ~~右键长按 0.35s = 命令环~~（**1.2.0 删除**，改快捷键）→ **右键长按+拖动 = 阵型箭头**（见 1.7）
- **载具朝向（1.0.2+）**：~~独立朝向拖动~~（**1.2.0 被阵型箭头上位替代**），机制保留：直驱车体 yaw，由阵型下发在载具到位后触发
- **行进路线显示（1.0.3+）**：移动/登车后从每个行进单位到目标画**灰色半透明细虚线**（登车线实时跟随目标载具），全部到位后消失
- **Ctrl+1~9 编组**（保存选择）、**1~9 召回**（阵亡自动剪枝）
- **右下小队列表**（编号 + □装甲 ○步兵，□ 恒在 ○ 前），单击选中/双击飞过去
- **左下信息面板 + 背包按钮（1.2.0/1.2.5，InfoPanel.cs）**：焦点单位姓名/职类/血量条/姿态/压制；【背包】按钮弹出分类列表（武器/弹药/爆炸物/医疗/装备工具/其他，取自 InventoryManager.inventory.items）；选中载具时另有下车/修理按钮；◀ ▶ 循环切换焦点
- **自定义光标（1.2.5→1.2.7，MouseCursor.cs）**：改为 **IMGUI 自绘**（`Cursor.visible=false` + OnGUI 画十字；`Cursor.SetCursor` 与游戏光标管理打架）
- **阵型箭头终点（1.2.6→1.2.7）**：屏幕空间映射，**起手冻结相机基向量**（全程不读相机，断开反馈回路）
- **火力点/火炮转向（1.2.7）**：可移动性改用 `VehicleTank`/`VehicleWithWheels`/`VehiclePlane` 判定（炮位也带 `AIVehicle`，旧判定失效）
- **驻守状态（1.2.7）**：`Formation.coverHolds` + 穿墙标记 + HUD「驻守 → x/y」
- **顶部 [控制该小队]**：随机接管一名选中成员并退出 RTS
- **RTS/FPS 共存**：下达的任务跨视角持续执行（登车完成检测、载具同步重试、持久集火）
- **UI 主题 cfg 可配色**：`colorBase`/`colorHover`/`colorText`（hex），热生效

### 1.2 铁律与陷阱（全部日志/实测定案 —— 再碰指挥类 mod 直接读这里）

1. **移动必须走 Lua 命令链 + AI 三连释放**：单兵 `Lua_Soldier.moveTo(pos)` 必须配合 `getAiParams().followCustomSquadOrders()` + `followCustomDirectCommands()` + `allowMovements(true)`，否则被 AI 状态机覆盖不动。
2. **对"已完成上一次移动"的单位重复 moveTo 无效**（实测：日志连续下发但单位不动）→ 追加移动前必须先 `Lua_Soldier.stop()` 复位任务状态。
3. **`forceTarget` 无效**（用户实测）→ 攻击一律走覆盖 `Soldier.GetBestVisibleEnemy`（标记集火）。
4. **`AIVehicle.setVehicleDestination` 是原生决策循环的输出位**（`VAI_Movable.UpdateMoveDestination` 以 `squadInside` 为输入每帧重写）→ 直写必被覆盖。**车辆移动唯一正道 = Squad 原生订单链** `Lua_Squad(squadInside).moveTo()`。
5. **`Lua_Squad(squad).moveTo` 会改写 `Squad.order`**（0→1 实证）——这是"命令已下达"的**可观测判据**；order 不变 = 命令没写进去。驾驶闭环判据 = `via=Lua_Squad` + `order 0→1` + 车辆实际位移。
6. **`Lua_Soldier.boardVehicle()` 进车是异步且不确定的**：同帧 `IsInfantry`（`GetComponentInParent<Vehicle>`/`isInsideVehicle`）对"首次从徒步登车"的乘员**永远不翻转** → 转队/登记**严禁依赖该检测**，用短延迟后无条件执行（Leave/Join 与 transform 无关）。
7. **屏幕坐标两套原点**：`Input.mousePosition`（Unity **左下**原点）→ 传给 `Camera.ScreenPointToRay` **必须用它**；`MouseGui()` = `(x, Screen.height - y)`（IMGUI **左上**原点）→ 只用于 GUI 绘制/命中测试。混用 = 标点上下颠倒。
8. **右键手势 = 状态机**（`RightLongPressSeconds=0.35`）：按下只计时，短按用**按下时坐标**下达；开环按压由 `WheelOpenGuard` 豁免；**关环必须吞掉剩余手势**（`swallowLeftGesture`）——否则同一次按压的"松开"落到下一帧普通点击逻辑里静默清空选择。
9. **IMGUI `e.Use()` 只管 IMGUI 事件**，同帧 legacy `Input` 照样为真。
10. **跟随没有原生"跟随指定单位"API**（`Squad.FollowLeader(bool)` 只跟自己队长；Join 对方小队会毁掉 RTS 控制权）→ 自建快照状态机：命令绑定下令时的单位集合，掉队超距或偏离引导点 ≥8m 时以 ≥3s 间隔纠正。
11. **接管态抑制窗口必须带"受控单位存活"检查**（否则 8s 抑制窗内死亡会挡住 RespawnPanel）。
12. **互操作异常 ≠ 对象失效**：清理逻辑（如 `PruneMark`）遇瞬时 NRE 需连续计数（3 次/1.5s）再判死，否则标记被连坐抹掉。
13. **登车不拆小队**：乘员名单跨多车时，显示/指挥要按"成员→所属全部载具→在车人员"展开。
14. **`rtsSquad` 是单指针**：多车各自上车会互相覆盖（`BoardVehicle` 每次 `CreateNewSquad`）；多车独立驾驶需另立多 RTS Squad 管理。
15. **Python 批量改源码必须 assert 每处替换 + 最后 grep 版本行**——曾三次静默失败（版本号漂移 / getter 没打上 / 删代码误删相邻函数）。
16. **删功能时要人工复核相邻成员**——曾因删冲刺误删 `IssueDirectCommand` 调用，导致右键五轮没修好。

### 1.3 冻结清单（除非出现新的可复现 Bug，一律不改）

- **死亡/换队链**：`ShowSquadList` / `ShowSwitchMemberSelection` / `DeathPanel` / `Respawn` / `EnsurePlayerSquadHasCandidates` / `ClearSquadList` / `DeathGuard`
- **载具链**：`DriveVehicleTo` 的 Squad 原生命令链 / `BoardPendingTick` 延迟转队 / `VehiclePendingTick` 移动补发
- 死亡观测设施（`[DeathTrace]`/`[SquadTrace]`/`[DeathGuard detected death]` 日志）保留在线

### 1.4 已知保留问题（用户决定保留）

- **RTS → ESC 菜单 → 返回主菜单后视角残留俯视**：0.9.11 的"无存活生物"自动退出判据未触发。**按 F9 一次即可完全还原**，用户决定不再修。
- **原版 M 地图在 god 视角强制显示为空**：显隐机制未明，已按设计在 RTS 内禁用；替代方案见 ROADMAP M3（用 `SceneMarkers` 网格自绘战术地图）。
- **死亡后"选择队友"0 候选**（复现：接管后用 HVT 叛徒机制让队友杀死自己）：已试无效——重指 `PlayerGUI.squad`/`GUISquad`、空候选拦截、LateUpdate 屏蔽（反而冻结重生）。**主因判断：死亡时原生把单位移出小队、`ControlledCharacter.joinedSquad` 断链，`ShowSquadList` 从该引用现取候选 = 0。** 下一手：Harmony patch `ShowSquadList`（前缀改 squad 参数）注入最大友军队；或死亡前预挂队。
- 其它 mod 的 `Soldier.OnDestroy` NRE（非本 mod）可忽略。

### 1.5 ROADMAP 剩余项（1.1.0 时点）

- **M1 稳定核验 / M2 边界与长稳**：长战斗 + 多场连续、全灭紧急退出→重生、登车途中载具被毁、合并进激活组后的驾驶资格、FPS 长挂机帧率 —— 均需用户实测轮次
- **M3（P1，可选）**：`godKey` 之外的键位可配置；自绘战术地图；大地图 M 联动标记
- **M4 发布工程**：M1/M2 全勾 + Nexus 素材（4-6 截图 + 30-60s 视频）→ 发 1.0.0（**注：实际已发 1.1.0，ROADMAP 的版本节奏未回填**）

### 1.6 转向控制技术演进（三轮实锤，值得单独记）

| 版本 | 方案 | 结果 |
|---|---|---|
| 1.0.2 | 原生 `AIVehicle.faceDirWhenStopped` 通道（停车转向）+ 到位/12s 超时清字段防回转 | 起步可行 |
| 1.0.3 | 托管驱动（日志实锤 `IsRotatedToward` 恒真 / `faceDir` 通道无效）→ C# 算夹角逐帧调 `RotateVehicleTowardEnemy`，<4° 判完成 | `RotateVehicleTowardEnemy` **外部直调无效** |
| 1.0.4+ | **直驱车体 yaw**：绕世界 Y 按转向速率逼近，保留地形俯仰 | ✅ 现行方案 |
| 1.0.5+ | 转向速率按各车 `rotationSpeed` 解析（`rotationSpeed` → 坦克 `curRotationSpeed` → 坦克 28/轮式 60 兜底，钳制 4-240 度/秒） | 原始读数随 issue 日志输出待校准 |
| 1.2.0 | 独立"朝向拖动"手势删除；转向由阵型下发在载具**到位后**（距槽≤7m）触发 | 上位替代 |

### 1.7 阵型箭头系统（1.2.0，2026-09-13）

**手势**：右键长按 0.35s + 拖动 = 阵型箭头（锚点射线失败 = 长按消耗、松手不下令；拖动 <1m = 普通移动）。**语义（用户定案）**：阵型线中心 = 长按点 A、线方向垂直于 AB、线总长 = |AB|、单位面向 B。命令环 8 项全部改为 cfg 可改键快捷键（Z/X/C 站蹲趴、V 停止、B 停火、N 就近掩体、M 集合、F 分散）。

**原生掩体系统（反编译确认，做掩体相关功能必读）**：
- `CoverManager.GetCovers(Vector3 pos, float radius, string faction, Vector3 coverDirection, bool nearestToPos=false)`（静态）→ 按位置+半径+阵营+**受敌方向**查掩体点，返回 `Il2CppSystem.Collections.IEnumerable`（元素 `TryCast<AiDestination>()`）。带方向查 0 结果时用 `Vector3.zero` 兜底再自过滤。
- 每个 `AiDestination`/`CombatCover` 暴露：`GetCoverPosition()`（站位）、`GetCoverPose()`（**建议姿态 SoldierPose**）、`IsCoverAvailable(shootDirection, faction)` / `IsCoverOccupied(faction)` / `IsCoverDestroyed()` / `IsVehicle()`。
- **逐兵进掩体 = `new Lua_Soldier(s).findCover(pos, 1.6f)`**（半径收小让 AI 取我们指定的点；原生走到位后自动按建议姿态/朝向驻守）——全程原生，不下 moveTo。降级路径：moveTo(掩体点) + 到位 SetPose。
- 沙袋等原版掩体与内容型道具包（Nexus **Combat Cover**）都注册进同一八叉树 → **无需兼容层**。

**阵型下发**：掩体分配（就近贪心）+ 无掩体步兵/载具沿阵型线垂直排开（步兵 1.4m/载具 7m 间距，溢出第二排后退 3m；按横向投影排序减少交叉）；步兵 `MoveUnits` 逐兵、载具 `DriveVehicleTo` 逐车 + `pendingFacings` 到位补发转向；观测 `RegisterMoveObservation(routeOnly:true)`（**阵型不加行军停火**——进掩体需要自由行为）。

**幽灵预览（GhostPreview.cs）**：克隆选中士兵整个 GameObject → 停用全部 Behaviour（保留 Renderer/Animator）+ Collider.enabled=false（不参与射线，不影响右键手势）→ 材质换共享 `Sprites/Default` 半透明白（hideFlags=61 陷阱 12）→ 尝试 `Animator.Play(crouch/prone clip)`；连续失败 2 次 cloneBroken 自动降级纯标记。

**信息面板（InfoPanel.cs）**：左下角，焦点单位 = 选中步兵+载具快照循环切换；血量 `life_total.Value`（**无最大血量字段**，按"观察上限"字典画条，同 UnitInfoOverlay 方案）、姿态 `Soldier.Pose`、压制 `Lua_Soldier.getSuppressionValue()`、装备 `GetAllHeldItems()` + `wearedItems` 的 item_id 去重。Soldier 的 Animator/SkinnedMeshRenderer API 需要 csproj 加 `UnityEngine.AnimationModule` 引用。

---

## 2. UniversalGeneration v1.0.14 — RTS 内自定义生成（作弊向）

> 源码：`Plugin.cs`、`GenCatalog.cs`（条目枚举/缓存/搜索）、`GenPanel.cs`（IMGUI 面板）、`Placer.cs`（落点模式）、`GenRunner.cs`（生成执行）、`HostLink.cs`（反射读宿主）、`FactionData.cs`、`Ui.cs`。
> 设计文档：`UniversalGeneration/DESIGN.md`（v1 草案，**部分已被 v1.0.0 实装超越**）。

### 2.1 实际功能（v1.0.0 已发布）

- **三击出车**：G（RTS 内）→ 选条目 → 点战场
- **全部可生成**：约 50 种官方小队类型（按战斗自动探测）+ 游戏与本体的全部载具（**运行时 `GetAllItemsOfType<PropData>` 自动枚举**，含 DLC 与内容 mod）
- **阵营选择器**：我方 / 敌方 / 中立（平民）——生成单位会按阵营交战
- **载具带正规车组**：按国家的坦克车组、精确座位数、走完整原生登车管线（**车组服从 RTS 指令**）
- **乘员可定制**：专职车组 / 任意步兵小队类型 / 空车
- **生成单位原地驻守 + 见敌即战**——不会自己跑去占目标点，且服从你的指令。**实装机制 = "受控参数"**（`GenRunner.ApplyControlled`，与宿主 `DisableNativeOrders` 同款）：对每个单位调 `new Lua_Soldier(s).getAiParams()` 的 `followCustomSquadOrders()` + `followCustomDirectCommands()` + `allowMovements(true)` 三连——即 AGENTS.md 陷阱 18 里那个"AI 三连释放"，**同一个模式既让单位听话、又让它不抢原生任务**
- **收藏**：星标任意条目，跨会话持久（`favorites` cfg 自动维护），显示在 Favs 页
- **一键清理**：面板标题栏 Clear 按钮删除本 mod 生成的一切
- **双语（EN/CN）**内置中文本地化

### 2.2 与宿主的联动（不改宿主源码的设计，实装为 `externalGuiBlock`）

- **互斥是最大联调点**：面板打开期间宿主 `IsMouseOverGui()` 必须覆盖本面板区域，否则点面板会同时在战场拉框选。实装方案 = 宿主 `GodViewController` 暴露 `internal static Func<Rect?> externalGuiBlock`，`IsMouseOverGui()` 尾部查询（**宿主改动 ≤10 行，远优于 patch 宿主**）。**这是附属 mod 与宿主协作的范式：宿主留一个静态委托挂点，附属 mod 反射注入。**
- 其余反射读宿主状态：`GodViewController.Active`（RTS 是否激活，false 时 mod 休眠）、`CurrentMark`（落点默认值）
- 生成的原生小队**自动进 RTS 可选集合**（宿主遍历 `Squad.AllSquads`，零额外工作）

### 2.3 生成实现（全部原生 API）

**⚠️ 首要陷阱：`SpawnManager.SpawnAI` / `SpawnAISquadGlobal` 返回的是原生协程对象（`Il2CppSystem.Collections.IEnumerator`）——只是协程对象，必须显式启动才会运行，否则回调永远不来（"调用了但什么都没生成"）。** 实装解法 = `GenRunner.StartCoroutineNative(routine)` 挂到常驻宿主 `MonoBehaviour` 上（宿主失效时自愈重建）；源码注释明确写了 *"直接交给 Unity 协程调度器，无需 WrapToIl2Cpp"*。

```
载具：go = new GameObject(); sp = go.AddComponent<VehicleSpawner>();
      sp.vehiclePrefabID = <条目id>; sp.camoId = 0; veh = sp.SpawnVehicle();  // 异步，当帧可能 null
      → 延迟确认 veh 非 null → veh.SetFaction(faction)
      → 带乘员：SpawnAISquadGlobal(..., spawnOnvehicle: veh, ...)（原生进出车管线）
步兵：SquadData sd = ItemsDatabase.GetSquadLoadouts(<小队类型>, 0);   // 类型无效→返回空，条目隐藏
      StartCoroutineNative(SpawnManager.SpawnAISquadGlobal(faction, null, sd, pos, 10f, null, cb, -1))
      → cb 里对全队 ApplyControlledToSquad（听话 + 原地驻守）
```

**API 实证索引**：`SpawnManager.SpawnAISquadGlobal(...)` / `SpawnManager.SpawnAI(...)`（static）/ `ItemsDatabase.GetSquadLoadouts(string,int)` / `GetAllItemsOfType<PropData>(PropType)` / `VehicleSpawner.vehiclePrefabID·camoId·SpawnVehicle()` / `Vehicle.GetOnVehicleBestPos(Soldier)`·`SetFaction`·`allVehicles` / `Lua_API.getInvadersFaction()·getDefendersFaction()`。载具 prefab 清单：`er2vehicles.manifest`（Tanks 173 / Wheeled 58 / Planes 59 / Artillery 30+）。

### 2.4 已知限制（官方 README 声明）

- 单人已验证；联机仅作主机可用
- **飞机在地面生成且可能坠毁**（谨慎使用）
- **生成的单位不保证跨阶段存活**

---

## 3. 游戏 2.1.x 兼容改造（2026-09-05，全工作区）

**根因**：游戏更新**移除了 `PhaseBarGUI` 类型** → 任何 `typeof(PhaseBarGUI)` 或直接 `[HarmonyPatch(typeof(PhaseBarGUI),...)]` 在类型加载期抛 `TypeLoadException` → **整个插件加载失败**。

**改造模式（现行标准做法，新 mod 遇"类型可能不存在"照抄）**：
1. **运行时探测**：`internal static readonly Type T = FindGameType("TypeName");` + `internal static readonly bool Present = T != null;`，加载时 `LogWarning` 提示降级
2. **条件 patch**：类型缺失时**不挂该 patch**，对应功能整体跳过（`NoInteractionHints/Plugin.cs:94`、`UiPatches.cs:188`、`RehidePatches.cs:66`、`UiHiders.cs:355`）
3. **字体回退链**：`PhaseBarGUI.GetDefaultFont()` 已不可用 → 改「战斗 HUD 活体 uGUI Text 的 font → `GUI.skin`/LegacyRuntime」多级回退（`LimbTweaks/NativeUi.cs`、`WeatherControl/NativeUi.cs`、`ModManager/Plugin.cs:1509/1641`）

**影响的 mod**：NoInteractionHints 4.5.3（首个修复）、ModManager、LimbTweaks、WeatherControl、UnitInfoOverlay。

---

## 4. ModManager 1.1.5→1.2.0 的界面攻坚（本时代最长的连续排查）

| 版本 | 内容 |
|---|---|
| 1.1.5 | 字母分组视图 + 滚动修复 + 新设置页异步防护 |
| 1.1.6 | 修"连点翻页空列表卡死" + 自由 string 配置项渲染 |
| 1.1.7 | 右侧间隙贴近原版 + **空列表卡死熔断器 / 证据 dump** |
| 1.1.8 | 修**原生 content 坏高度泄漏**（滑条重合根因）+ 间隙再收 |
| 1.1.9 | **空列表真凶修复**——快速右翻时停用 Content 链（`STUCK-EVIDENCE` 实锤） |
| 1.2.0 | 发布版：清理高频诊断日志 + 更新 README/Nexus |
| 1.1.1（早） | **枚举配置支持**：第三方 Coax MG Hotkey 用 `UnityEngine.InputSystem.Key` 枚举做快捷键，ModManager 原本**直接跳过枚举类型**（`AddSetting` 无 `IsEnum` 分支，而 `NativePage` 有——两套渲染不一致）→ 补 `IsEnum` 分支（热键类枚举→改键按钮；其他→`Enum.GetNames` 下拉）+ 按键捕获的枚举名映射（`Alpha0-9→Digit0-9`、`Return→Enter`、`LeftControl→LeftCtrl`、`Menu→ContextMenu`，鼠标键无映射跳过） |

**教训**：ModManager **两套渲染（`Plugin.cs` 的 MODS 页 / `NativePage`）分支必须同步维护**，新类型支持两边一起加。

---

## 5. 双语发布机制（2026-09-05 落地）

- `scripts/build.ps1 -Mod <X> -Cn`：`dotnet build -p:DefineConstants=CN_BUILD` → 部署 → 打包 `<pkg>_CN_v<ver>.zip`
- **发布包按语言取文档**：默认包取 `README.txt` / `Nexus_description.md`（EN）；`-Cn` 包取 `README_CN.txt` / `Nexus_description_CN.md`（中文）——文件不存在则回退英文
- **代码层双语**：`Ui.Tr("中文","英文")` / `T(cn,en)` 助手 + `#if CN_BUILD` 编译期常量折叠（两套字符串都编进元数据，靠 `DefaultChinese` 常量切换——**别用字符串搜索验证构建语言**，见 AGENTS.md 陷阱 17）
- **当前双语覆盖**：SquadCommand ✅、UniversalGeneration ✅（各有 `README_CN.txt` + `Nexus_description_CN.md`）；**其余 mod 只有英文文档**（CombatTweaks / HighValueTarget / InventoryPause / ModManager / NoInteractionHints / UnitCollision / UnitInfoOverlay / ZoomAnywhere）

---

## 6. 工作区整理（2026-09-05/06）

- `research_out/` **平铺化**：零散反编译目录合并到根（250 类型），反编译根目录**更新到 2026-09-05 游戏版本**
- 删除 397 MB 一次性研究大件（killfeed 运行时、il2cpp 全量 dump、`hvt_shots`、`mm_toggle_dump` 等）→ 现 **17.9 MB / 259 文件**
- SquadCommand 移除 `src/` 双源与旧构建脚本（**现在源码扁平在 mod 根目录**，`GPT_CONTEXT.md` §4/§6 里提到的 `src/ER2_SquadCommand.csproj` 已过时——**以 `SquadCommand/SquadCommand.csproj` 为准**）
- 删除根目录 `EasyRed2_BepInEx_Dependencies.zip`（with_deps 打包时代遗留，无脚本引用）；Downloads 清理 SquadCommand 全部历史版本包

---

## 7. 本时代踩过的坑（补充到 guide §3 之外的）

1. **未验证的 interop 信号不能当门控**——`DeathPanel.instance.gameObject.activeInHierarchy` 战斗中恒 true、`LoadingCircle.IsLoading()` 战斗中恒 false（UnitInfoOverlay v1.0.3）。**先加诊断日志实测，再当门控。**
2. **上下文窗口类拦截必须校验对象身份边界**——CombatTweaks 友军爆炸窗口未校验 responsible 阵营 → 敌人手雷也建窗口 → 敌方死亡士兵倒地全被拦（v1.2.2 修复）。
3. **游戏更新移除类型 = 插件加载失败**（不只是功能失效）——`TypeLoadException` 会让整个插件不加载，必须运行时探测 + 条件 patch。
4. **附属 mod 与宿主的正确协作方式**：宿主暴露静态委托挂点（`externalGuiBlock`），附属 mod 反射注入——不要 patch 宿主（丑且易碎）。
5. **删功能时的连锁误删**：删代码要人工复核相邻成员（曾误删 `IssueDirectCommand` 调用）。
6. **脚本批量改源码必须逐处 assert + 末次 grep 校验**（版本号漂移 / getter 没打上 / 误删函数，三次静默失败）。
