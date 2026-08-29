# ER2 SquadCommand 开发总结（经验教训）

> 来源：0.6.x 上帝视角（RTS 指挥）mod 开发过程中实测踩坑，按"是什么坑 / 怎么发现 / 怎么解"记录。

## 1. IL2CPP 反编译的本质限制
- 游戏 `Assembly-CSharp.dll` 是 IL2CPP interop 桩：**只有类型/方法名与签名，没有方法体**（实现都在原生 `GameAssembly` 里）。
- 反编译的用途 = **方法清单 + 签名**（`NativeMethodInfoPtr_<方法名>_...`），据此选 Harmony patch 目标；不要指望读出逻辑。
- 反编译**部署的 mod DLL（BepInEx 插件，普通托管 DLL）**可以完整还原 C# 源码 → 用"部署 DLL 反编译 vs 当前源码"diff，能精确定位未完成的改动/上一版本差异。本次就是用这招发现 v0.6.2 部署 DLL 里 `GodViewCameraPatch` 还是 Postfix、源码已改 Prefix 却缺 `SuppressSwitchMemberUntil` 字段（编译不过）。

## 2. 光标抢锁：找到"每一处"锁源，而不是只补一帧
- 症状：上帝视角下每次点击，光标被重置到屏幕中央并闪烁。
- 认知：这是**多个原生控制器每帧抢**（锁回中央）与 mod 每帧放开的对抗。
- 已禁用的抢锁/抢相机源（都是上帝视角时 Prefix 返回 false 跳过原方法）：
  - `PlayerController.Update / LateUpdate / FixedUpdate`
  - `FPSGunManager.Update / LateUpdate`
  - `CameraDirector.Update / LateUpdate / FixedUpdate / UpdateDOF`
  - `SimpleCameraController.Update`
  - `CinematicCameraController.Update / FixedUpdate`
- 结论：只跳前两个"主"控制器不够——**要做的事：枚举该帧所有会动相机/光标的 MonoBehaviour 控制器并全部跳过**。
- 兜底：`AssertFreeCursor()`（lockState=None + visible=true）每帧在 `Tick`（Update 阶段）和 `LateApply`（LateUpdate 阶段）各强制一次，保证顺序上也能压住。
- 诊断法：每帧检查"上帝视角中 cursor 仍被锁"并限频打日志，用于确认是否还有漏网之鱼。

## 3. "选择队友"面板：屏蔽≠关闭，必须走原生 Close
- 症状：接管小队后"选择队友"提示 + 背景仍弹出（甚至列表显示不全）。
- 根因一：mod 只 **Prefix 屏蔽**了 `ShowSquadList / StartSquadSelection / UpdateSquadGUI`——这只能挡住"再次打开"，**关不掉已打开/残留的选择状态**。原生状态机还挂在"选择中"。
- 根因二：`PlayerGUI.LateUpdate` **每帧**会把面板重新打开——没禁它，屏蔽窗口一过就复发。
- 正确解：
  1. 接管时调用 **`PlayerGUI.CloseSquadSelection()` + `PlayerGUI.ClearSquadList()`**（用 `IsSelectingSquad()` 判断），真正关闭选择流程；
  2. `PlayerGUI.LateUpdate` 在 `SuppressingUi`（上帝视角 + 接管后抑制窗口）内整体跳过；
  3. 抑制窗口内每帧兜底 `PostTakeoverGuard()`：再检测到 `IsSelectingSquad()` 就再关 + `HideSquadPanel()`。
- 通用结论：**"打开类"方法用 Prefix 屏蔽只能防再开；要"关闭"必须调用原生自己的 Close/Disable 方法，并禁掉每帧刷新的入口（LateUpdate）**。

## 4. 单纯"时间窗口"不可靠
- 原方案：接管后 5s 内屏蔽一切相关 GUI → 5s 过后残留状态复发。
- 改进：`SuppressingUi = Active || now < suppressSwitchMemberUntil`（统一开关）+ 窗口内每帧兜底守卫；窗口本身拉长到 8s。**凡是"状态残留"问题，用"关闭+守卫"而不是纯计时窗口。**

## 5. GUI 面板从底部堆叠：先算总高，再起画
- 症状：右下角小队面板"显示不全"（分类列表跑到屏幕外）。
- 根因：`y = Screen.height - 100` 后误写成 `y += 行高`（向下增长）→ 底部条目出屏；注释写"往上排"实现却是向下。
- 正确：**先遍历统计总行数 → `y = Screen.height - 14 - totalH`（底部对齐向上）→ 超出时 clamp 顶部**，hit 矩形一次性给全高。

## 6. 版本/发布纪律（本次重申）
- 任何功能改动：`BepInPlugin` 版本 + 启动日志字符串**两处同步** z+1（漏一处 = 日志版本不一致，排查时骗自己）。
- 每次改动用 `build.ps1 -Mod SquadCommand`（build+deploy+清 cfg+打 zip 一体）。
- 发布前清理诊断日志；本次保留的 `[SquadCmd]` 日志用于用户测后回读定位。

## 7. 调试方法沉淀
- **不要猜机制**：机制不确定先加限频诊断日志（如"光标被锁回""IsSelectingSquad 又为真"），用户测一轮回读日志定位，比盲 patching 快。
- 反编译部署 DLL vs 源码 diff = 检查"上次到底改了什么/有没有改完"的最快手段。
- Harmony patch 大量同类目标时，全部用 `Prefix => !GodViewController.Active/SuppressingUi` 模式，语义统一、出事好排查。

## 8. 命令层级的正确用法（0.6.8→0.6.10 实测）
- **`Lua_Squad.moveTo(pos, radius)`（小队级）**：有效，但会让**整队（含未选成员）一起走** → 只适合"整队下达"。
- **`Lua_Soldier.moveTo(pos)`（单兵级）**：被 AI 状态机覆盖，必须**先释放两种命令**才能生效：
  - `followCustomSquadOrders()`（小队任务）
  - `followCustomDirectCommands()`（驻守/防守等直接命令 —— 0.6.8 只放了小队任务所以单位不动）
  - `allowMovements(true)`（确保允许移动）
- 结论：**"只动选中的单位"必须用单兵级 moveTo + 三连释放**；"整队移动"才用小队级。

## 9. 虚拟选择 vs 真实新小队（0.6.9→0.7 方向决策）
- 0.6.x 用"虚拟选择"（`selInfantry`/`selVehicles` 列表，不物理改游戏小队）→ 用户反馈不直观、没建立新小队。
- v0.7 方向：**框选真实组建新小队**（`Squad.FindOrNew`/`Join`/`Leave`），把框到的单位从原小队抽离并编入新队 → 这才是 RTS 直觉（地狱之门式）。
- 代价：必须处理原小队为空自动清理（`RemoveNullUnits`）、载具车组特殊（车组不拆散）、火力点成员归属。

## 10. 右键闪烁/重置中央（0.6.10 仍未根治）
- 已跳过的锁源：PlayerController.Update/LateUpdate/FixedUpdate、FPSGunManager.Update/LateUpdate、CameraDirector.Update/FixedUpdate/LateUpdate/UpdateDOF、SimpleCameraController.Update、CinematicCameraController.Update/FixedUpdate。
- 右键按住时 mod 跳过解锁（0.6.10）→ 用户仍报"右键导致闪烁+重置中央"。
- 意味着：**右键本身触发原生某个未跳过的逻辑**（可能不是光标控制器，而是"命令确认"流程在 Update 里 SetCursor(Locked) + 相机回中）。
- 下一步：找 `GameInput`/`PlayerController.CursorRaycast`/原生右键处理入口，确认是"谁在右键时锁光标+回中相机"。

## 11. v0.6.10 实证：三连释放让单兵 moveTo 生效（0.7 沿用）
- 实测日志（0.6.10）：`右键移动 ... 单位数=12` → 中心 `(196.9→124.6)`、距目标 `121.5→16.6` —— **单位真的移动了**。
- 根因确认：单兵 `Lua_Soldier.moveTo` 必须配合 `followCustomSquadOrders() + followCustomDirectCommands() + allowMovements(true)` 三连释放，AI 状态机才会执行；只放小队任务（0.6.8）无效。
- v0.7 的 `MoveUnits` 沿用此路径 ✓。

## 12. 追踪超时半途而废 → 提前到达判定（0.7.2）
- 症状：14s 追踪到期时单位停在距目标 ~16m 处（还在走但命令停止重发，AI 转自主行为）。
- 修复：`TickTracker`/`TickTrackerUnits` 增加**提前到达判定**——单位组中心距目标 ≤ radius 时立即结束追踪并记录"已到达"，不再重发命令；超时仍兜底结束。

## 13. v0.7.2 用户实测全通过（0.7 架构确认）
- 日志实证（10:56）：
  - `新建小队 ptr=0x...` + `框选: 步兵=N 载具=M` —— **框选真实建队成功**（`Squad.FindOrNew` + `Join` 管道有效）
  - `TRACK END ... 已到达，距目标=7.9/5.5` —— 提前到达判定生效，单位走到目标附近干净停下
  - `按钮下车: 1`、载具框选 `框选: 步兵=0 载具=1`、接管流程全部正常
- 结论：v0.7 的"框选建队 + 逐员 moveTo + 提前到达 + 接管"主链路已验证，用户认可框选操作。

## 14. v0.7.3 六项反馈修复（待用户实测）
1. **火炮被当攻击目标**：`VehicleFriendly` 只读 `GetVehicleFaction()`（火炮等静态火力点常返回空→判敌）→ 加**乘员阵营兜底**。
2. **超员无法上车**：小队级 `Lua_Squad.boardVehicle` 在自建小队上不稳定 → 改**逐员 boardVehicle + countEmptySeats 限流**（上几个算几个），附 `SetLocked(false)` 防静默失败。
3. **下车立刻又上车**：车组还挂着旧"开过去"命令 → 先 `StopTracking()`，下车后给车组**新的步行目标**（载具斜后方 10m）。
4. **暂停后镜头不动**：timeScale=0 → deltaTime=0 → 改用 `Time.unscaledDeltaTime`。
5. **长按右键指令轮盘**：原生 `CircularMenu2.ShowCircle(data, key, callback)` 支持自定义回调，但 GameInput 无鼠标右键键值、原生轮盘绑定玩家角色 → **OnGUI 自绘 6 项轮盘**（移动/攻击/上车/下车/停止/暂停，角度高亮，松开执行）；短按右键仍是常规指令（长短按用 0.35s+位移 25px 区分）。
6. **小队列表重做**：恢复右下角列表，去掉分类标题，改为 `编号 + □(装甲)○(步兵) + (人数)`，□ 总在 ○ 前；单击选中、双击飞行。
