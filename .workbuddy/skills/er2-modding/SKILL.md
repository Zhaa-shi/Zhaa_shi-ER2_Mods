---
name: er2-modding
description: |
  Easy Red 2（ER2）Mod 开发全流程手册。ER2 是 Unity 2022.3.62f3 / IL2CPP 游戏，
  其 Mod 是 C# BepInEx 6 插件（Harmony patch），不是 JSON 配置。
  当任务涉及：新建 ER2 Mod 工程、修改插件源码、编译/部署/打包、查 LogOutput.log 排障、
  ilspycmd 反编译查 API、跨 mod UI 联动、版本发布时，加载本 skill。
  触发词：ER2、Easy Red 2、BepInEx 插件、Harmony patch、Plugin.cs、build.ps1、
  plugins 部署、LogOutput.log、ilspycmd、mod 版本发布。
agent_created: true
---

# ER2 Mod 开发手册

## 0. 铁律（先读，违反必炸）

1. **不要臆造 API**。写任何 patch 前，先用 `ilspycmd -t <Type>` 确认方法签名存在。
2. **不要修改游戏原文件**。只往 `<Game>\BepInEx\plugins\` 写自己的 DLL。
3. **不要删除 plugins 里其他 mod 的 DLL**。
4. **源码一律用 write/edit 工具**，禁用 PowerShell `Get-Content`/`Set-Content` 写含中文的源码
   （默认 ANSI/GBK → 中文乱码 + CS1513）。
5. **动手前先读目标 mod 的 `Plugin.cs` 全文**，不要凭记忆改。
6. **改完必须回读 `LogOutput.log` 验证，不要猜**。
7. **不要每帧 `FindObjectsOfType`**，用 `Creature.allCreatures` / `Vehicle.allVehicles` /
   `ItemObject.spawnedItems` 静态表，或按需 `Physics.OverlapSphere`。

## 1. 环境事实（先用 §1.1 命令核实，不要依赖本表快照）

| 项 | 值 |
|---|---|
| 工作区根 | `<工作区根>` |
| 游戏根 | `E:\SteamLibrary\steamapps\common\Easy Red 2`（Steam appid 1324780） |
| 插件 / 配置 / 日志 | `<Game>\BepInEx\plugins\` / `config\` / `LogOutput.log` |
| interop | `<Game>\BepInEx\interop\`（`Assembly-CSharp.dll` ~8.7MB，游戏启动时重新生成） |
| BepInEx | 6.0.0.0（IL2CPP 版），插件 `<TargetFramework>net6.0</TargetFramework>` |
| 分支 | `master`，无远端，纯本地版本库 |
| 发布包 | `<输出目录>\<pkg>_v<ver>.zip` |

### 1.1 环境核实命令（每轮开始先跑，游戏更新会静默改签名）

```powershell
# 游戏是否更新过：看这两个文件的修改时间，别只看 buildid
Get-Item "E:\SteamLibrary\steamapps\common\Easy Red 2\GameAssembly.dll" | Select LastWriteTime
Get-Item "E:\SteamLibrary\steamapps\common\Easy Red 2\Easy Red 2_Data\il2cpp_data\Metadata\global-metadata.dat" | Select LastWriteTime
```

**已知的历史变更**：
- 游戏 2.1.x 起移除了 `PhaseBarGUI` 类型 → 任何 `typeof(PhaseBarGUI)` 直接 patch 会
  `TypeLoadException` 导致**整个插件加载失败**。改用 `FindGameType("PhaseBarGUI")` 运行时探测 + 条件 patch。
- 2026-09-12 静默更新改了原生签名（`SettingsGUI_V2.UpdateOpenedMenu` 1 参→2 参、
  `FillSettingPage` 1 参→4 参）→ 旧编译产物运行时 `MissingMethodException`。
  **对策**：核心交互的原生调用走反射 / `AccessTools` 按名取值 + 参数个数自适应。

## 2. 目录结构（一 Mod 一顶级目录，不要建 src/assets/config/build）

```
ER2_Mods/
├── <ModName>/                 每个 mod 一个顶级目录
│   ├── <modname>.csproj       net6.0 类库，引用 interop 程序集（Private=false）
│   ├── Plugin.cs              [BepInPlugin] 入口类，改功能主要改这里
│   ├── *.cs                   可选：拆分出的补丁/UI/逻辑文件
│   ├── Assets/                可选：音频/纹理，需 assetsDir 配置才部署
│   ├── README.txt             发布必需（缺失时 build.ps1 静默跳过 → zip 里只有 DLL）
│   ├── Nexus_description.md   发布必需
│   ├── README_CN.txt          可选：中文包用
│   └── Nexus_description_CN.md 可选：中文包用
├── Shared/NoHintsHudLink.cs   跨 mod UI 隐藏联动（反射，无编译期依赖）
├── scripts/build.ps1          一体化构建：编译+部署+清cfg+打包
├── research_out/              反编译/解包研究产物（用完即清或并入根目录）
└── *.md                       知识文档（见 §7）
```

### 2.1 新增 mod 时的完整清单

1. 建目录 `<ModName>/`，写 `<modname>.csproj`（照抄 §2.2 模板）。
2. 写 `Plugin.cs`，`[BepInPlugin("<guid>", "<显示名>", "x.y.z")]`。
3. 写 `README.txt` + `Nexus_description.md`（**发布必需，否则打包静默丢文档**）。
4. 在 `scripts/build.ps1` 的 `ValidateSet` 和 `switch ($Mod)` 里**加一行**。
5. 编译部署：`scripts\build.ps1 -Mod <ModName>`。
6. 更新 `ER2_projects_status.md` 台账。

### 2.2 csproj 模板（照抄，只改 AssemblyName / RootNamespace）

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net6.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <AssemblyName>ER2_<ModName></AssemblyName>
    <RootNamespace>ER2<ModName></RootNamespace>
    <GenerateAssemblyInfo>true</GenerateAssemblyInfo>
    <NoWarn>CS0618</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="Assembly-CSharp">
      <HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\interop\Assembly-CSharp.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx.Core">
      <HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\core\BepInEx.Core.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="BepInEx.Unity.IL2CPP">
      <HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\core\BepInEx.Unity.IL2CPP.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="HarmonyLib">
      <HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\core\0Harmony.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2CppInterop.Runtime">
      <HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\core\Il2CppInterop.Runtime.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="Il2Cppmscorlib">
      <HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\interop\Il2Cppmscorlib.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine">
      <HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\interop\UnityEngine.dll</HintPath>
      <Private>false</Private>
    </Reference>
    <Reference Include="UnityEngine.CoreModule">
      <HintPath>E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\interop\UnityEngine.CoreModule.dll</HintPath>
      <Private>false</Private>
    </Reference>
  </ItemGroup>
</Project>
```

**全部引用必须 `Private=false`**（避免把游戏程序集复制进产物）。

## 3. Plugin.cs 骨架（含调试日志开关强制约定）

```csharp
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace ER2MyMod
{
    [BepInPlugin("er2.mymod", "ER2 My Mod", "1.0.0")]   // 版本必须纯 x.y.z，禁字母后缀
    public class Plugin : BasePlugin
    {
        internal static ManualLogSource ModLog;
        internal static ConfigEntry<bool> enabled;
        internal static ConfigEntry<bool> debugLog;      // §3.1 强制约定

        public override void Load()
        {
            ModLog = Log;
            enabled  = Config.Bind("General", "enabled", true, "Master switch.");
            debugLog = Config.Bind("Debug", "debugLog", false, "true = 输出诊断日志。");
            new Harmony("er2.mymod").PatchAll();
            ModLog.LogInfo("ER2 My Mod 1.0.0 loaded.");  // ← 版本串也要同步改
        }

        internal static bool DebugOn => debugLog != null && debugLog.Value;
    }
}
```

### 3.1 调试日志开关（2026-09-18 起为强制约定）

- **统一写法**：`Config.Bind("Debug", "debugLog", false, ...)` —— 节名 `Debug`、键名 `debugLog`、默认 `false`。
- **门控**：诊断日志一律 `if (Plugin.DebugOn) Plugin.ModLog.LogInfo(...)`。
- **`LogError` / `LogWarning` 不门控**——错误必须无条件可见。
- **保留低频功能日志**（启动横幅、页面注入、自动保存）——这些是排查锚点。
- **发布版禁止**：开发/测试页面、硬编码生 key 的临时行、只写日志不干事的空回调。

**判定标准（照这个判，别照"是不是开发开关"判）**：
- 「生 key / 空回调 / 点了没反应」= **必须从发布版删掉**。
- 「文案正常、功能可用、只是另一种实现」= 可保留，但必须默认关。
- 「开关会影响你用来操作它的那个界面」= **无条件删掉**（自锁陷阱：打开后无法从界面关掉）。

## 4. 构建 / 部署 / 打包

```powershell
# 一体化：构建+部署+清cfg+打包（推荐所有构建都走这里）
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字>
# 只打包不部署
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字> -SkipDeploy
# 只部署不打包
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字> -SkipPackage
# 中文版（出 _CN_ 包）
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字> -Cn
# 仅编译（workdir 为 mod 目录）
dotnet build -c Release <proj>.csproj
dotnet build -c Release <proj>.csproj 2>&1 | Select-String "error"
```

- **部署时游戏可能在运行**（DLL 被锁）→ build.ps1 已内置 10 分钟轮询；仍失败就**告知用户退出游戏**。
- `-Cn` 会带 `CN_BUILD` 编译定义 → **EN/CN 两个包的 DLL 内容不同**。
  惯例最终部署 **EN 构建**，故**先跑 `-Cn`、最后跑默认包**。

### 4.1 版本号与发布

- 版本号 `x.y.z` **纯数字**，每次改动 z+1。**三处必须同步**：`BepInPlugin` / 启动日志字符串 / zip 名。
- **发布前清理调试/诊断日志**（高频日志、限频诊断全清），保留低频功能日志。
- 发布包 = DLL + `README.txt` + `Nexus_description.md`。
- 双语发布取 `README_CN.txt` / `Nexus_description_CN.md`（build.ps1 自动）。
- **打包后必须拆 zip 核对内容**，别只看"打包成功"（build.ps1 对缺失文档静默跳过）。

## 5. 验收三件套（漏一 = 没部署完）

```
[ ] 源码与部署 DLL sha256 一致
[ ] 日志有 "Loading [<插件名> x.y.z]" 且与 BepInPlugin 一致
[ ] 源码内版本字符串 grep 计数 ≥2（BepInPlugin + 启动日志）
[ ] 无 "Skipping type" / "Ambiguous" / "Error loading" / "TypeLoadException"
[ ] 有本 mod 的功能触发日志
```

## 6. 常见错误处理（症状 → 根因 → 对策）

### 6.1 插件完全不加载

| 症状 | 根因 | 对策 |
|---|---|---|
| BepInEx 直接跳过插件 | `[BepInPlugin]` 版本号带字母后缀 | 改纯 `x.y.z` |
| `TypeLoadException`，整个插件失败 | patch 了已不存在的类型（如 `PhaseBarGUI`） | 改 `FindGameType()` 运行时探测 + 条件 patch |
| `Undefined target method` | patch 了 interop 里不存在的方法 | 先 `ilspycmd -t <Type>` 确认签名 |
| `Ambiguous` 异常中断整个 PatchAll | 重载方法未显式指定参数类型 | `[HarmonyPatch(typeof(X), "M", new Type[]{...})]` |

### 6.2 patch 不触发

| 症状 | 根因 | 对策 |
|---|---|---|
| 静默注入 null、拦截失效 | Prefix/Postfix 参数名与 interop 参数名不一致 | 名字必须**完全一致**（`SetBleeding(bool bleeding)` 不是 `value`） |
| 入口 patch 失效 | 目标是协程方法（编译成状态机，方法体从未被调用） | 改定时循环 |
| 运行时 `MissingMethodException`，且逃逸出 Prefix 的 try/catch | 游戏静默改了原生方法签名 | 走反射 / `AccessTools` 按名取值 + 参数个数自适应 |

### 6.3 功能执行了但无效

| 症状 | 根因 | 对策 |
|---|---|---|
| "日志全绿但什么都看不见/听不见" | 运行时创建的 Texture2D/AudioClip 被 Unity 卸载 | `hideFlags=(HideFlags)61`，**必设** |
| 功能时灵时不灵 | 被游戏覆盖/重置（战役改天气等） | 加诊断日志让用户测一轮，用日志定位 |
| 局内暂停后 mod 永久停摆 | 用了 `Time.time`（timeScale=0 时冻结） | **所有节流/冷却/看门狗必须用 `Time.unscaledTime`** |

### 6.4 写游戏状态的硬约束

1. **写血量必须整体赋值**：`soldier.life_total = new ProtectedInt(hp);`
   （getter 返回类型副本，`.Value = x` 报 CS1612）
2. **血量只能渐进小步写**：单帧大幅跳变 → 游戏判死/覆盖；每帧 ±1 累积器安全。
3. **活体断肢不要调 `DetachLimb`**：原生自带 20-30/s 失血 DoT（必死）——用视觉隐藏 + 自己控血。
4. **类型转换必须 `TryCast<T>()`**：interop 返回基类包装，C# `as` 按 CLR 类型检查恒失败。
5. **`ref` 参数在 IL2CPP 下生效**：改的是托管 interop 桩（实测 before=6→after=60）。
6. **`Creature.SetIncapacitated` 主动调用（血量>0）会被游戏判死**，不是"只是趴下"。
7. **投降单位血量被游戏接管**：外部扣血无效 → 用计时器 `Kill`。
8. **UI**：`new GUIStyle()` 默认 `normal.textColor` 是黑，`GUI.color` 是乘法 tint →
   必须显式 `normal.textColor = Color.white`。

> 完整 62 条陷阱见 `ER2_mod_dev_guide.md` §3；逐会话复盘见 `ER2_mod_经验.md`。

## 7. 资源文件引用方式

**知识文档（改代码前必读对应章节）**：
- `AGENTS.md` —— **每轮必读入口**：环境事实、陷阱、工作流契约。
- `ER2_projects_status.md` —— 各 mod 版本台账（**别凭记忆猜版本号**）。
- `ER2_mod_dev_guide.md` —— 完整机制/API/陷阱 1-62。
- `ER2_mod_经验.md` —— 致命陷阱全集 + 逐会话复盘。
- `ER2_mod_技能.md` / `ER2_mod_工具.md` —— 方法论 / 命令速查。

**跨 mod UI 联动（改任何 mod 的 UI 前必读）**：
原生 UI 隐藏由 Hide Anything（`com.ryan.er2.nointeractionhints`）统一管理，14 类原生 UI。
接入方式 A（推荐，无编译期依赖）：
```xml
<Compile Include="..\Shared\NoHintsHudLink.cs" Link="NoHintsHudLink.cs" />
```
```csharp
if (ER2Shared.NoHintsHudLink.IsHidden("er2.你的modid", "显示名")) return;  // 跳过绘制
```
- **新接入的 mod 必须在 `HudCompat.knownNames` 预注册表加一行**，否则开关要等首次查询才出现。
- 持久型 UI（每帧 OnGUI）每帧开头查一次；提示型显示前查一次。
- **术语坑**：v4 起**已无 F5 热键**，改为「勾选 = 始终隐藏」。旧注释里的 "F5" 已失效。

**资源解包**：
- 先读 `<Game>\Easy Red 2_Data\StreamingAssets\CorvoBundles\*.manifest`（22 个，0.1MB）
  列出全部资源路径——**别做 3.7GB 全量解包**。
- 第三方游戏《Gates of Hell》`resource\*.pak` **就是标准 ZIP**，用 .NET `ZipFile` 读，**别用 WinRAR**（会卡死）。

## 8. 命令白名单

| 命令 | 用途 |
|---|---|
| `powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字>` | 构建+部署+清cfg+打包 |
| `dotnet build -c Release <proj>` | 仅编译（workdir 为 mod 目录） |
| `Select-String <log> -Pattern "..."` | 查日志（**每次实测后必查，不要猜**） |
| `ilspycmd -t <Type> <dll> -o <dir>` | 反编译查 API |
| `ilspycmd -l c <dll>` | 列出 dll 全部类型 |
| `Get-Item <game>\BepInEx\plugins\<name>.dll` | 确认部署（比对时间戳/sha256） |

**反编译要点**：
- interop 里方法体全是 IL2CPP 原生转发壳——**只有签名/字段/继承关系可信，行为必须实测**。
- 可从**已部署的 plugins DLL** 反编译恢复源码。
- 验证构建语言用 `ilspycmd -t ER2ModManager.Plugin` 看 `DefaultChinese` 常量
  （**别用字符串搜索**，元数据字符串堆编码反直觉）。
- 反编译产物里 `val..ctor(...)` 是非法语法，改写为 `new Vector2(...)`。
- **查继承成员**：`ilspycmd -t Soldier` 查不到继承成员（如 `inventory` 来自 `Creature`），
  别误判"不存在"——先确认继承链。

## 9. 工作流契约（每次改动必须遵守）

1. 读 `AGENTS.md` → 读 `ER2_projects_status.md` 查目标 mod 现状 → **通读 `Plugin.cs`**。
2. 改源码（用 write/edit 工具，**不用 PowerShell 写中文源码**）。
3. 跑 `scripts\build.ps1 -Mod <名字>`。
4. **请用户实测** → 回读 `LogOutput.log` 验证，**不要猜**。
5. 按 §5 验收三件套自检。
6. 同步更新 `ER2_projects_status.md` 台账 + `README.txt` + `Nexus_description.md`。
7. `research_out/` 的反编译产物用完即清或并入根目录。

**需求澄清纪律（血泪教训）**：
- 指挥/控制类需求**先问清是「接管单个单位」还是「RTS 框选指挥」**——两者技术跨度天差地别
  （`DirectControl` 在错误理解上做了 3 个版本才对齐）。
- **提新 mod 方向前先确认生态里没有现成方案**（先搜 Nexus / 问用户）。
