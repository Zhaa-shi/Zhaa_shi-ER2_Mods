# 载具朝向控制（长按右键拖动）设计方案

> 参照《战争召唤：地狱之门东线》的"选中载具后按住右键拖动控制朝向"交互。
> 逆向依据：`research_out\AIVehicle.decompiled.cs`（2026-09-05 游戏版本 interop 桩）、`SquadCommand\research\report_vehicles_misc.md`、GPT_CONTEXT.md 载具专题铁律。
> **实施结果定案（1.0.2→1.0.4 实测）**：§3 的原生字段通道 `faceDirWhenStopped` 无效——
> 原生 `IsRotatedToward` 恒真（issue 后 elapsed=0.0s 实锤）、外部直调 `RotateVehicleTowardEnemy(夹角)` 不转车，
> 且 `StopAndClearPath` 会顺带停掉原生转向（表现为"拉线后车停止转动"）。
> **最终实现 = 直驱车体 yaw**：C# `Vector3.SignedAngle` 算夹角，每帧按 60°/s 绕世界 Y 轴
> `Quaternion.AngleAxis(Δ, up) * rotation` 逼近（保留地形俯仰/侧倾），<4° 判完成、15s 超时，完成/超时清 faceDir 字段。

---

## 1. 逆向结果：载具朝向的原生通道

### 1.1 AIVehicle（interop 全部公开直调，无需反射）

```csharp
public class AIVehicle : MonoBehaviour {
    // —— 停车朝向核心字段 ——
    public Il2CppSystem.Nullable<Vector3> faceDirWhenStopped { get; set; }  // "停止时朝向"
    public int faceDirMaxYAngle;                                            // 朝向最大夹角（限位，只读参考）
    // —— 观测 ——
    public float GetAngleToward(Vector3 direction);   // 车体 forward 与 direction 的有向夹角
    public bool  IsRotatedToward(Vector3 direction);  // 是否已转向到位
    // —— 行为函数（native 每帧状态机在用，mod 可直调）——
    public void StopAndClearPath();                          // 停车清路径（StopSelected 已实证）
    public void RotateVehicleTowardEnemy(float enemyAngleDir);        // 按角度转车体（含物理）
    public void RotateStaticVehicleTowardEnemy(float enemyAngleDir);  // 静态车（AT炮）版
    public void StopAndRotateTowardTarget();                 // 停车转向当前目标（enemy target）
    public void AcquireFaceVehicleRotation(Turret preSelectTurret = null);
    // 每帧状态机：StaticVehicleRoutine()（无目的地静止）/ MovableVehicleRoutine()（行驶）
    public AIVehicle.Vehicle? squadInside;  // 所在原生车组 Squad
}
```

### 1.2 语义推断（native 逻辑不可见，按命名+类型推断）

- `StaticVehicleRoutine`（静止车每帧例程）大概率读取 `faceDirWhenStopped` → 调 `RotateVehicleTowardEnemy(angle)` 系列原地转车体。这是唯一"名字+类型"完全吻合"停车朝向"的原生通道。
- `StopAndRotateTowardTarget()` 无参——"target"指 AI 当前敌目标，不是任意方向，不适合本功能（但证明"停车转向"是原生既有行为）。

### 1.3 命令链无朝向语义（排除项）

- `Lua_Vehicle`：brake/repair/damage/… 无转向；`Lua_Squad`：moveTo/charge/coverArea/holdFire/… 无朝向；`Squad.SynchOrder(byte, Vector3, float)` 订单数据也不含 facing。
- 陷阱 34（铁律）：`AIVehicle.setVehicleDestination` 是原生决策循环的输出位，直写会被每帧覆盖——**不要**试图用目的地坐标实现转向，走订单链的移动必然真开车。

### 1.4 组件获取

`Vehicle` 上无 AIVehicle 属性 → `veh.GetComponent<AIVehicle>()`（GetComponentInChildren 兜底）。StopSelected/DriveVehicleTo 已用同款写法。

---

## 2. 交互设计（右键手势状态机扩展）

### 2.1 现状（必须保持）

| 手势 | 行为 |
|---|---|
| 短按右键（<0.35s） | 直接指令：空白=移动、敌军=标记集火、友军载具=交互环 |
| 长按 ≥0.35s（按在空地） | 常驻命令环：站/蹲/趴/停止/掩体/集合/停火/分散 |
| 双击右键 | 前往并防守（HoldArea） |

### 2.2 新增：长按 + 拖动 = 朝向控制

| 手势 | 行为 |
|---|---|
| 按住右键并在 **0.35s 内拖出 >14px**，且选中含载具 | 进入**朝向拖动**：每辆选中载具到鼠标落点画箭头，随动 |
| 拖动中松开右键 | 下达朝向命令（车体转向鼠标落点方向），箭头消失 |
| 长按 0.35s 时鼠标未拖出阈值 | 照旧开命令环（现状不变） |
| 环已打开后再拖动 | 无效（环保持原语义） |

仲裁细节：
- 触发条件 = `dragFacing 配置开` && `selVehicleRefs.Count > 0` && `!rightDownOnUnit`（按在单位上不开，与环同规则）。
- 竞速规则：0.35s 定时器到点时，若位移已超阈值 → 朝向拖动；未超 → 命令环。自然"按住拖"总能在 0.35s 内超出 14px，不添迟滞。
- 混合选择（步兵+载具）：只转载具，步兵不动（步兵朝向无安全原生 API，超出本次范围）。

### 2.3 适用载具过滤

- 跳过：`IsAirVehicle()`（飞机不能原地转）、无 `AIVehicle` 组件（纯火力点 TurretMG）、`!HasDriverAlive`（无车组的车转不动）、玩家正接管的车（Pointer 与受控角色载具相同）。
- 全部被过滤 → cmdFlash 提示原因，不画箭头。

---

## 3. 命令执行与维护

### 3.1 下达（松开右键）

对每辆合格载具 v：

1. `ai.StopAndClearPath()` —— 先停车（打断既有移动；同时摘除 mod 侧该车的移动观察任务 obsVehicles，避免 FaceTick 与移动体检互相打架）；
2. 方向 = 车位置 → 鼠标落点地面点，取 XZ 归一化；
3. **写入兼容点**：`ai.faceDirWhenStopped = 车位置 + dir * 30m`（世界坐标点）。
   —— 兼容写法原因：若 native 把它当**方向向量**（atan2(x,z) 定航向），30m 外的点向量航向 = dir；若当**目标点**（LookAt），结果也是朝 dir。两种语义收敛同效；而传短向量在"目标点"语义下会指向世界原点（车屁股调头），必须避开；
4. 登记 `FacingTask { veh, ai, dirPoint, deadline = now+12s }`；
5. cmdFlash「载具转向 → N」+ LogAlways 一条（低频）。

### 3.2 维护（Tick 持久任务段，退 RTS 后仍生效）

- `ai.IsRotatedToward(dirPoint)` = true → **完成**：`faceDirWhenStopped = null`（防之后每次停车都自动回转）、日志、移除任务；
- 超时 12s → 清字段 + 移除 + 超时日志（兜底，防字段残留永久影响 AI）；
- 载具死亡/销毁（interop 异常按铁律 30 连续计数判死）→ 移除任务；
- **新移动命令覆盖**：`MoveCommandTo`/`DriveVehicleTo` 成功驱动某车时，同步清它的 faceDir 与任务（防止车到达后突然自行回转）。

### 3.3 降级方案（若 faceDirWhenStopped 实测无效）

- **方案 B**（首推）：FaceTick 每帧直调 `ai.RotateVehicleTowardEnemy(ai.GetAngleToward(dirPoint))`，直至 `IsRotatedToward` 或超时——native 自己的转车体函数，带物理，与 StaticVehicleRoutine 同款调用路径；
- **方案 C**（最后手段）：`DriveVehicleTo(v, 车位置 + dir * 12m)`——真开过去自然面向，改变位置非纯转向，仅兜底。

判定依据（首版日志）：写入后 1s/3s 打印 `GetAngleToward` 读数——角度递减=生效；`faceDirWhenStopped` 读回为 null=被 native 清掉/拒收。

---

## 4. 拖动可视化（SceneMarkers 扩展）

- 新增 `SceneMarkers.Arrow(key, from, to, color, width, visible)`：世界空间直线 LineRenderer + 箭头两翼（两短线），走现有对象池 + EndFrame 未刷新自动隐藏机制；
- 拖动期间：每辆合格载具一条箭头（key=`Facing{i}`，from=车位置+up1.2m，to=鼠标落点）+ 落点 Dot；
- 颜色用 uiColorHover 亮色系（与选中指示一致），线宽 0.25m 左右。

---

## 5. 配置 / 版本 / 文档

- 新配置 `General.dragFacing`（bool，默认 true）——冲突时可一键关闭；
- 版本 1.0.1 → 1.0.2（BepInPlugin 属性 + 启动日志字符串两处同步）；
- README.txt / Nexus 描述：实测通过后随发布更新加一行功能说明。

## 6. 首版诊断（按工作流：机制不确定先加日志让用户测一轮）

| 日志 | 级别 | 内容 |
|---|---|---|
| `[Facing] issue` | LogAlways | 车辆数、方向点坐标 |
| `[Facing] readback` | debugLog 门控 | 写入后 1s/3s：faceDir 读回值 + GetAngleToward 读数 |
| `[Facing] done / timeout` | LogAlways | 用时 / 残余夹角 |
| `[Facing] skip` | LogAlways（低频） | 全部载具被过滤的原因 |

### 验收清单

```
[ ] 选中载具长按右键拖动出箭头，松开车体原地转向拖动方向
[ ] 转向期间日志 issue → done（或判定降级到方案 B）
[ ] 转向后下达移动命令，车正常开走（faceDir 已清，无回转）
[ ] 步兵选择的长按右键=命令环、短按=移动/标记，均不受影响
[ ] 无 Skipping type / Ambiguous / Error loading；启动日志版本 1.0.2
```
