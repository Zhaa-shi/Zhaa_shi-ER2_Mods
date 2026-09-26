# ER2 Mods

《Easy Red 2》BepInEx 6 插件合集 —— 源码、构建脚本与开发文档。

游戏为 Unity 2022.3.62f3 / IL2CPP，因此所有 Mod 都是 **C# BepInEx 6 插件**
（经 Harmony 打补丁），**不是** JSON 配置型 Mod。目标框架 `net6.0`。

---

## 插件一览

| 目录 | 插件 GUID | 显示名 | 版本 | 说明 |
|---|---|---|---|---|
| [`SquadCommand/`](SquadCommand/) | `er2.squadcommand` | Easy Red Gate | 1.4.57 | 上帝视角 RTS 小队指挥（最大工程） |
| [`UniversalGeneration/`](UniversalGeneration/) | `er2.universalgeneration` | ER2 Universal Generation | 2.5.49 | RTS 内自定义生成单位 / 载具 / 物品（含鼠标拖放） |
| [`NoInteractionHints/`](NoInteractionHints/) | `com.ryan.er2.nointeractionhints` | ER2 Hide Anything | 4.5.4 | 原生 UI 隐藏（14 类），被其他插件复用 |
| [`ModManager/`](ModManager/) | `er2.modmanager` | ER2 Mod Manager | 1.7.9 | 游戏内 Mod 管理面板（EN/CN 双语） |
| [`LimbTweaks/`](LimbTweaks/) | `er2.limbtweaks` | ER2 Limb Tweaks | 2.13.101 | 肢体与伤害系统调整 |
| [`WeatherControl/`](WeatherControl/) | `er2.weathercontrol` | ER2 Weather Control | 1.7.2 | 天气 / 氛围 / 光照控制 |
| [`AIFood/`](AIFood/) | `er2.aifood` | ER2 AI Food | 1.4.0 | AI 补给与食物系统 |
| [`ThrowableWheel/`](ThrowableWheel/) | `er2.throwablewheel` | ER2 Throwable Wheel | 1.3.6 | 自定义投掷物转盘 + 背包补货 |
| [`HighValueTarget/`](HighValueTarget/) | `er2.highvaluetarget` | ER2 Veteran HVT | 1.2.2 | Veteran 高危目标标记（EN/CN 双语） |
| [`CombatTweaks/`](CombatTweaks/) | `er2.combattweaks` | ER2 Combat Tweaks | 1.2.2 | 战斗手感调整 |
| [`ZoomAnywhere/`](ZoomAnywhere/) | `er2.zoomanywhere` | ER2 Zoom Anywhere | 1.0.1 | 视野与武器放大 |
| [`InventoryPause/`](InventoryPause/) | `er2.inventorypause` | ER2 Inventory Pause | 1.0.5 | 开背包时冻结世界 |
| [`UnitInfoOverlay/`](UnitInfoOverlay/) | `er2.unitinfooverlay` | ER2 Unit Inspector | 1.0.5 | 单位状态悬浮显示（开发者调试工具） |
| [`MorePhysics/`](MorePhysics/) | `er2.morephysics` | ER2 More Physics | 0.1.49 | 完整物理化 |
| [`UnitCollision/`](UnitCollision/) | `er2.morephysics.unitcollision` | ER2 More Physics - Unit Collision | 1.0.9 | 单位 / 尸体碰撞（MorePhysics 轻量版） |
| [`Endless/`](Endless/) | `er2.endless` | ER2 Endless | 0.3.0 | 无尽模式（开发中，M0 战斗尖峰） |
| [`HvtTestDriver/`](HvtTestDriver/) | `er2.hvt.testdriver` | HVT Test Driver | 1.0.0 | HVT 自测工具（内部，不发布） |
| [`FleshWoundsFixed/`](FleshWoundsFixed/) | `ER2_FleshWounds` | ER2 Flesh Wounds | 1.0.1 | 第三方 Flesh Wounds 重建修复（紫贴图 bug） |

---

## 环境要求

| 项 | 值 |
|---|---|
| 游戏 | Easy Red 2（Steam appid 25255578） |
| BepInEx | 6.0.0.0（IL2CPP 版） |
| .NET SDK | 6.0 或更高（实测 dotnet 10 SDK 可正常编译 net6.0 工程） |
| 目标框架 | `net6.0` |

### ⚠️ 关于 csproj 中的路径

**每个 `.csproj` 里的 `<HintPath>` 都硬编码了游戏安装的绝对路径**，例如：

```xml
<HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\interop\Assembly-CSharp.dll</HintPath>
```

编译前请**全部替换为你自己的游戏路径**。这些引用统一为 `Private=false`
（避免把游戏程序集复制进产物）。

依赖的程序集位于：

```
<Game>\BepInEx\interop\Assembly-CSharp.dll      （约 8.7 MB，游戏启动时生成）
<Game>\BepInEx\interop\Il2Cppmscorlib.dll
<Game>\BepInEx\interop\UnityEngine.dll
<Game>\BepInEx\core\BepInEx.Core.dll
<Game>\BepInEx\core\BepInEx.Unity.IL2CPP.dll
<Game>\BepInEx\core\0Harmony.dll
<Game>\BepInEx\core\Il2CppInterop.Runtime.dll
```

---

## 构建

一体化脚本（编译 + 部署 + 清 cfg + 打包）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <ModName>
```

| 参数 | 作用 |
|---|---|
| `-SkipDeploy` | 只打包，不部署到游戏 |
| `-SkipPackage` | 只编译并部署，不打包 |
| `-Cn` | 编译中文版并产出 `_CN_` 包 |

可用 `-Mod` 名见 `scripts/build.ps1` 顶部的 `ValidateSet`。

仅编译单个工程：

```powershell
cd SquadCommand
dotnet build -c Release SquadCommand.csproj
```

> `build.ps1` 会把产物部署到 `<Game>\BepInEx\plugins\` 并删除对应的
> `config\er2.<mod>.cfg`。**游戏运行时会锁定 DLL**，脚本内置了重试等待。

`.vscode/tasks.json` 提供了等价的 VS Code 任务，其中游戏路径由
`ER2_GAME_DIR` 环境变量统一控制，改一处即可。

---

## 仓库结构

```
ER2_Mods/
├── <ModName>/              一个 Mod 一个顶级目录
│   ├── <modname>.csproj    net6.0 类库，输出 ER2_<ModName>.dll
│   ├── Plugin.cs           [BepInPlugin] 入口类，改功能主要改这里
│   ├── *.cs                拆分出的补丁 / UI / 逻辑文件
│   ├── README.txt          发布必需
│   ├── Nexus_description.md
│   ├── README_CN.txt       可选：中文包用
│   └── Nexus_description_CN.md
├── Shared/                 跨 Mod 共享源码（以 <Compile Include> 链接进各工程）
│   ├── Er2Ui.cs            共享 IMGUI 控件
│   └── NoHintsHudLink.cs   跨 Mod UI 隐藏联动（反射，无编译期依赖）
├── scripts/build.ps1       一体化构建脚本
├── ER2_mod_dev_guide.md    机制 / API / 陷阱参考（约 200 KB）
├── ER2_mod_经验.md          致命陷阱全集 + 逐会话复盘
├── ER2_mod_技能.md          可复用技能
├── ER2_mod_工具.md          工具速查
├── ER2_UI_design.md        UI / IMGUI 机制
├── ER2_physics_system.md   碰撞矩阵解包结果
└── ER2_projects_status.md  各 Mod 版本台账
```

---

## 新增 Mod 清单

1. 建目录 `<ModName>/`，照抄现有工程的 `.csproj`（改 `AssemblyName` / `RootNamespace`）。
2. 写 `Plugin.cs`，`[BepInPlugin("<guid>", "<显示名>", "x.y.z")]`。
   **版本号必须是纯 `x.y.z`**，带字母后缀会被 BepInEx 跳过。
3. 写 `README.txt` + `Nexus_description.md`（**缺失时打包会静默丢文档**）。
4. 在 `scripts/build.ps1` 的 `ValidateSet` 与 `switch ($Mod)` 里各加一行。

---

## 开发文档

仓库根目录的 Markdown 文档记录了完整的技术积累，改代码前建议先读：

- **`ER2_mod_dev_guide.md`** —— 机制、API、陷阱 1-67
- **`ER2_mod_经验.md`** —— 致命陷阱全集与逐会话复盘（排查问题首选）
- **`ER2_mod_技能.md`** —— 方法论
- **`ER2_mod_工具.md`** —— 命令速查
- **`ER2_projects_status.md`** —— 各 Mod 当前版本与状态台账

### 几个反复踩到的坑

- **游戏版本变更会导致插件整体加载失败**：游戏 2.1.x 移除了 `PhaseBarGUI` 类型，
  任何 `typeof(PhaseBarGUI)` 会抛 `TypeLoadException` 使**整个插件加载失败**。
  现行做法是运行时探测类型 + 条件 patch。
- **静默更新会改原生方法签名**：判定「游戏是否更新过」要看 `GameAssembly.dll` /
  `global-metadata.dat` 的**修改时间**，不能只看 buildid。
- **IL2CPP 下类型转换必须 `TryCast<T>()`**：interop 返回的是基类包装，
  C# 的 `as` 按 CLR 类型检查会恒失败。
- **`Time.time` 在 `timeScale=0` 时冻结**：所有节流 / 冷却 / 看门狗必须用
  `Time.unscaledTime`。

---

## 版权与许可

本项目为**非官方粉丝作品**，与 Corvostudio di Amadei Marco 无隶属关系。

Easy Red 2 及其全部游戏资源、程序集、美术与音频资产的版权归
**Corvostudio di Amadei Marco** 所有。本仓库**不包含**任何游戏本体资源 ——
构建所需的游戏程序集需从你自己的正版游戏安装目录获取。

Mod 源码本身以 [MIT 许可证](LICENSE) 发布。
