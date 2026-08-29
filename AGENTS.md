# DeepSeek Harness 工作区配置

项目根目录：`C:\Users\71011\Documents\ER2_Mods`（将此目录设为 harness 的 workspace root）

## 1. 启动时必读（上下文加载）

1. `AGENTS.md` — 工作流、关键事实、致命陷阱（**第一轮必须读**）
2. `ER2_mod_dev_guide.md` — 完整机制/API/踩坑参考（按需查）
3. 对应 mod 的 `Plugin.cs` — 修改前通读

## 2. 目录约定

```
ER2_Mods/
├── LimbTweaks/          # mod 1：肢体断肢+流血系统（er2.limbtweaks）
├── WeatherControl/      # mod 2：天气/氛围控制（er2.weathercontrol）
├── AIFood/              # mod 4：AI 自动吃食物回血（er2.aifood）
├── NoInteractionHints/  # mod 5：F5 隐藏互动提示（com.ryan.er2.nointeractionhints）
├── ModManager/          # mod 6：游戏内 mod 设置管理页（er2.modmanager，嵌原生设置界面）
├── ThrowableWheel/      # mod 7：自定义投掷物转盘（er2.throwablewheel，替换原生手雷轮盘+背包补货）
├── CombatTweaks/        # mod 8：战场调整（友军伤害保护/弹药调整，er2.combattweaks）
├── ZoomAnywhere/        # mod 10：任意位置放大（er2.zoomanywhere）
├── HighValueTarget/     # mod 14：ER2 Veteran HVT（老兵高危目标：击杀追踪+集火+叛徒机制，er2.highvaluetarget）
├── InventoryPause/      # mod 15：背包暂停（打开自己/尸体背包时冻结世界，er2.inventorypause）
├── UnitCollision/       # mod 11：MorePhysics 附属轻量版——单位/尸体碰撞（er2.morephysics.unitcollision）
├── UnitInfoOverlay/     # mod 12：单位状态悬浮显示（开发者调试工具，er2.unitinfooverlay）
├── Shared/              # 跨 mod 共享（NoHintsHudLink 等）
├── FleshWoundsFixed/    # 第三方 Flesh Wounds 重建修复版（v1.0.1，紫贴图 bug 修复，见其 README.md）
├── scripts/build.ps1    # 一体化构建脚本（支持全部 10 个原创 mod；FleshWoundsFixed 需手动构建部署）
├── AGENTS.md / ER2_mod_dev_guide.md / ER2_physics_system.md 等文档
└── research_out/        # 游戏/第三方 mod 反编译研究（deployed_dump/ 是部署 DLL 的批量反编译）
```

**部署目录注意**：`游戏\BepInEx\plugins\` 里除上述原创 mod 外还有一批第三方 mod（Flesh Wounds/ImpactFX/Reactive Ragdoll/Remove Stains/Realistic Blood/Decals & Shells/Bullet Penetration/Death Screen Effect 等，其 DLL 名 ≠ 插件名）。第三方 mod 中 **Flesh Wounds（ER2_FleshWoundsBW.dll，子目录内）会在运行时克隆/销毁士兵材质**——2026-08 曾导致"单位贴图变紫"恶性 bug，修复版在 FleshWoundsFixed/。诊断紫贴图问题先看它。

## 3. 命令白名单（harness 允许执行的命令）

| 命令 | 用途 |
|---|---|
| `powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字>` | 构建+部署+清cfg+打包（一次完成；`-SkipDeploy` 只打包不部署；`-SkipPackage` 只部署；**`-Cn` 编译中文版并打包 `_CN_` 命名 zip**） |
| `dotnet build -c Release <proj>` | 仅编译（workdir 为 mod 目录；错误信息用 `2>&1 \| Select-String "error"` 查看） |
| `Select-String <log> -Pattern "..."` | 查日志/过滤本 mod 日志（**每次测试后必查**） |
| `ilspycmd -t <Type> <dll> -o <dir>` | 反编译查 API；**也可反编译已部署的 plugins DLL 恢复源码**（`-l c <dll>` 列类型） |
| `Get-Item <game>\BepInEx\plugins\<name>.dll` | 确认部署 |
| `"C:\Users\71011\新建文件夹\WinRAR.exe" x -o+ -y <rar> "<out>\"` | 解压 rar 发布包（用户 mod 有时只有 rar） |

**禁止**：修改游戏原文件；删除 plugins 其他 mod；`FindObjectsOfType` 类每帧扫描代码（用 `Creature.allCreatures/aliveCreatures` 或按需 `Physics.OverlapSphere`）。

## 4. 致命陷阱（本次开发验证，全部踩过）

1. **写血量必须整体赋值**：`soldier.life_total = new ProtectedInt(hp);`（getter 返回值类型副本，`life_total.Value = x` 编译报 CS1612）
2. **血量只能渐进小步写**：单帧大幅扣血（跳变）→ 游戏判死/覆盖；每帧 ±1 内安全（累积器模式）；绷带补血跳变安全（游戏认可治疗）
3. **活体断肢不要调 `DetachLimb`**：原生自带失血 DoT（约 20-30/s，断肢必死）——活体断肢用视觉隐藏（HideArm）+ 自己控制血量
4. **IL2CPP 类型转换必须 `TryCast<T>()`**：interop 返回基类包装，C# `as` 按 CLR 类型检查恒失败
5. **`new GUIStyle()` 默认 normal.textColor 是黑色**（GUI.color 是乘法 tint）→ 必须显式 `normal.textColor = Color.white`；`new GUIStyle(其他style)` 拷贝构造被 IL2CPP 裁剪
6. **AI 移动无法外部驱动**：`Soldier.Move()` / `NavMeshAgent.SetDestination` 都会被游戏 AI 控制器覆盖——不要做"让 AI 走过去"的功能
7. **游戏原生流血特征**：`isBleeding=true` 时游戏调 `Damage(1.0)` 级小伤害（dam ≤ 5 可作特征区分）；`SetBleeding(false)` 止血后触发游戏自然回血——断肢单位需拦截止血（Prefix return false）防回血
8. **投降单位血量被游戏接管**：外部扣血无效 → 用计时器 Kill 等效"流血而死"
9. **跨 mod 联动用反射**：AppDomain.GetAssemblies 找程序集 + GetField 读静态字段（缓存引用），避免编译期依赖（对方 mod 缺失时自动跳过）
10. **游戏内部血量状态**：伤害记录/同步会覆盖外部写入的 life_total——不要与游戏 Damage 路径打架，优先走游戏原生路径（Damage()/SetBleeding/RecoverLife）
11. **背包添加物品会降级成基类**：`Inventory.AddVirtualItem` 和 `InventoryManager.AddItemToInventory` 都会把物品归一化成基类 VirtualItem（原生转盘按 VirtualThrowable 类型过滤会无视）→ 需要正确子类时**直接 `inv.items.Add(vi)`**
12. **原生回调拒绝外部替换的 UI 数据**：`CircularMenu2.ShowCircle` 的数据替换后，原生选择回调匹配不到条目（选了什么都不发生）——能走原生管线就走原生（补货让原生自己构建转盘）
13. **协程方法 patch 可能不触发**：`ShowGrenadeSelectionMenu` 方法从未被调用（原生直接构造状态机）→ 入口 patch 失效，改用**定时循环**（PlayerController.Update Postfix + 节流）
14. **不要在 Prefix 里递归重调原方法**（Throw 重定向方案）：IL2CPP 下重入参数异常，还会破坏原生调用链（AI 投掷被干扰）
15. **BepInEx 自动落盘防不胜防**：`SaveOnConfigSet=true`（默认）运行中自动写盘 + **游戏退出时自动保存全部 cfg**——要实现"按下才保存"必须用**暂存机制**（改动不碰 BoxedValue，保存按钮才写入+Save）
16. **KeyCode 枚举配置没有 AcceptableValueList**：cfg 里 "Acceptable values" 注释是自动生成的；uGUI Dropdown 在 IL2CPP 下值变化检测不可靠 → 热键改键用**点击按钮+Input.GetKeyDown 捕获**方案
17. **ModManager 中文词典严禁重复键**：`Dictionary<string,string>(OrdinalIgnoreCase)` 加重复键（如 enabled/Enabled）→ 静态构造抛异常 → 整个 MODS 页空白只剩页脚（踩过两次）
18. **原生 Hint 在设置界面打开时延迟显示**（关闭设置才出现）→ 即时反馈用控件按钮文字闪烁（FlashItem 轮询恢复）
19. **uGUI 文字溢出被滚动区裁切**：`horizontalOverflow=Overflow` 超长文字会被滚动 Mask 裁掉 → 长名截断+省略号，完整名展开时另起一行显示；描述行高度必须保守估算（420px/行、8px/字、+1 行余量），否则文字压到下一行按钮
20. **DLL 文件名 ≠ 插件名**：插件以 BepInPlugin 元数据识别（ER2_RecoilOverhaul.dll 里是 Universal Recoil Control）——查插件用日志 Loading 行或二进制搜 GUID，别按文件名猜
21. **语言一致性**：配置简介来自插件代码——EN ModManager + CN 版插件 = 界面英文但简介中文；发布截图需双端同语言
22. **Copy-Item -Recurse 嵌套坑**：目标目录已存在时会把源文件夹复制成子目录 → 部署前先确认/清理目标，或用"目标=父目录"方式
23. **资源清单读 manifest**：er2items 3.7GB 全量读太重，`CorvoBundles\*.manifest`（0.1MB）直接列出全部资源路径
24. **GetWorldCorners 传 C# 数组返回全零**：IL2CPP interop 下 `RectTransform.GetWorldCorners(new Vector3[4])` 实测全 0 → 用 `Il2CppStructArray<Vector3>` 显式类型，或 `TransformPoint(rect 四角)` 绕开；`GetScreenCoordinatesOfCorners` 返回世界/UI 坐标不是屏幕像素
25. **ER2 无常驻小地图**：只有按 M 的大地图；`MiniMapGUI.miniMap` 容器一直 active（600×600 UI 单位）但平时不可见；开关读 `MiniMapGUI.MiniMapOpened`；大地图图标用"跟随 unitsContainer 游戏标记"方案（GetPositionInContainer 匹配最近标记 + 标记 position 绘制），别猜映射公式
26. **ER2 暂停机制（全实测定案）**：原生 `Pause.SetPause` = timeScale=0+弹菜单+disableOnPause（藏菜单=死锁）；手动 `Pause.isPaused=true` 禁用输入但不冻结世界；`timeScale=0` 真暂停但卡 UI 协程动画 + 丢弃武器浮空（用"延迟冻结等动画完成"+扫描 `ItemObject.spawnedItems` 拉下道具解决）；`enableAiBehaviour(false)` 无效（true 才有效）；背包开关读 `InventoryPanel.isOpen`

## 5. 工作流契约（每次修改必须遵守）

1. 改 `Plugin.cs` → 跑 build.ps1（或 dotnet build + 部署轮询脚本）
2. **部署时游戏可能运行** → build.ps1 已内置 10 分钟轮询；若失败告知用户退出游戏
3. 用户测试后 → **读 LogOutput.log 验证**（不要猜）
4. 诊断规则：
   - patch 不生效 → 查参数名（guide 陷阱 2）、版本号（陷阱 1）、Ambiguous（陷阱 3）
   - 无日志/加载失败 → 查 `Skipping type`/`Ambiguous`/`Error loading`
   - 功能时灵时不灵 → 查游戏重置/覆盖（如战役改天气）；**机制不确定时先加诊断日志让用户测一轮，用日志定位而非猜测**
5. 修改后同步：源码已在 ER2_Mods（原位），发布包由 build.ps1 自动生成，无需额外备份

## 6. 版本号规则（极易踩坑）

- `[BepInPlugin(...)]` 版本必须 `x.y.z`（如 `2.13.93`），**禁止字母后缀**（带后缀=BepInEx 跳过插件）
- 每次改动递增版本号（z+1），zip 名一致；**启动日志里的版本字符串也要同步改**（容易漏）

## 7. 发布约定（用户已确认）

- 发布简介一律按 N 网格式：**Description / Installation instructions / Main features / Requirements / Shout outs**
- 更新时只提**更新内容和达成效果**（简洁），完整 README 按需输出
- 发布前清理调试/诊断日志（高频日志、限频诊断），保留低频功能日志
- 发布包在 `C:\Users\71011\Downloads\<ModName>_v<版本>.zip`；zip 内 = DLL + README.txt + Nexus_description.md

## 8. 快速验证清单

```
[ ] 日志有 "Loading [ER2 Xxx x.y.z]"
[ ] 无 "Skipping type" / "Ambiguous" / "Error loading"
[ ] 功能触发日志（如 "Limb detached" / "Weather set to Rain" / "AI ate food"）
[ ] 发布包 zip 已更新（版本号一致）
[ ] 启动日志版本字符串与 BepInPlugin 一致
```
