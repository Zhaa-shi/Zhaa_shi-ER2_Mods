# ER2 Mod 开发 —— 工具篇

> 用途：Easy Red 2（IL2CPP）BepInEx 插件开发的**命令与脚本速查**。本篇只收录可执行命令、路径、
> 脚本用法、产物命名与部署确认；配套的环境机制/命中细节详见《ER2_mod_经验.md》，方法论详见《ER2_mod_技能.md》。

## 一套路径（全部原样）

| 用途 | 路径 |
|---|---|
| 游戏根目录 | `E:\SteamLibrary\steamapps\common\Easy Red 2`（下称 `<Game>`） |
| 插件目录 | `<Game>\BepInEx\plugins\` |
| 配置目录 | `<Game>\BepInEx\config\` |
| 日志 | `<Game>\BepInEx\LogOutput.log` / `ErrorLog.log` |
| interop | `<Game>\BepInEx\interop\`（IL2CPP 反编译包装，启动时重新生成） |
| 市场资源清单（manifest，可替代全量解包） | `<Game>\Easy Red 2_Data\StreamingAssets\CorvoBundles\er2items.manifest`（及同目录 `*.manifest`，共 22 个，约 0.1MB） |
| 源码工作区 | `<工作区根>` |
| 发布包 | `<输出目录>\<ModName>_v<版本>.zip` |
| WinRAR（系统装在非标准路径） | `<WinRAR安装路径>\WinRAR.exe` |

## 构建命令

| 命令 | 用途 | 说明 |
|---|---|---|
| `powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod <名字>` | 一体化：构建+部署+清cfg+打包（一次完成） | 推荐所有构建走这里 |
| `... -Mod <名字> -SkipDeploy` | 只打包不部署 | |
| `... -Mod <名字> -SkipPackage` | 只部署 | |
| `... -Mod <名字> -Cn` | 编译**中文版**并打包 `_CN_` 命名 zip | 一条命令完成，无需手动复制轮询 |
| `dotnet build -c Release <proj>.csproj` | 仅编译 | workdir 为 mod 目录；CN 版加 `-p:DefineConstants=CN_BUILD` |
| `dotnet build -c Release <proj>.csproj 2>&1 \| Select-String "error"` | 仅编译并只看错误 | 编译错误过滤查看 |

**支持的 mod 参数名（-Mod 取值）**：`LimbTweaks` / `WeatherControl` / `AIFood` / `NoInteractionHints`
（Hide Anything）/ `ModManager` / `ThrowableWheel`。脚本内还曾有 `CombatTweaks`/`MorePhysics`/
`BattlefieldUI` 等条目（相关 mod 已删除并清理，条目可作历史保留）。

**产物命名规则**：
- 发布包 zip 名必须与版本号一致：`<ModName>_v<版本>.zip`（如 `ER2_ModManager_v1.0.21.zip`）。
- 中文版：`<pkg>_CN_v<ver>.zip`（如 `ER2_ModManager_CN_v1.0.21.zip`）——由 `-Cn` 自动处理。
- 双语 mod 出 4 个包：EN/CN × 2。
- zip 内 = DLL + `README.txt` + `Nexus_description.md`。

## 日志与诊断命令

| 命令 | 用途 |
|---|---|
| `Select-String <log> -Pattern "..."` | 查日志/过滤本 mod 日志（**每次测试后必查**） |
| `Get-Item <game>\BepInEx\plugins\<name>.dll` | 确认部署 |

日志读取纪律：用户测试后**读 LogOutput.log 验证（不要猜）**。

## 反编译命令

| 命令 | 用途 |
|---|---|
| `ilspycmd -t <Type> <dll> -o <dir>` | 反编译指定类型查 API |
| `ilspycmd -l c <dll>` | 列出 dll 全部类型 |
| `ilspycmd -t <Type> <dll>` 反编译**已部署的 plugins DLL** | 从已部署插件恢复源码 |

验证构建语言用 `ilspycmd -t ER2ModManager.Plugin` 看 `DefaultChinese = true/false`
（详见《ER2_mod_经验.md》陷阱 39）。详见《ER2_mod_技能.md》反编译分析流程。

## 资源解包与发布包解压

| 命令/工具 | 用途 |
|---|---|
| UnityPy（Python） | 读 AssetBundle（er2items / er2bundle / er2vehicles 等）typetree；读 `globalgamemanagers` 的 PhysicsManager/TimeManager/TagManager |
| `"<WinRAR安装路径>\WinRAR.exe" x -o+ -y <rar> "<out>\"` | 解压 rar 发布包（用户 mod 有时只有 rar） |
| `scripts/validate_assets.ps1` / `probe_png.ps1` | PNG chunk CRC 校验 + 像素采样通用工具（素材工作流，可复用） |
| `Check_mm_dup.ps1`（即 `check_mm_dup.ps1`） | ModManager 中文词典（ChineseLabels）重复键校验脚本——在 `ChineseLabels` 词典加键后**必须跑**（详见《ER2_mod_经验.md》陷阱 18） |

> 注：er2items 3.7GB / er2bundle 4.5GB 全量 UnityPy 解包太重——先读 `CorvoBundles\*.manifest`（0.1MB）
> 列出全部资源路径（投掷物 ID 全集、资源清单都从 manifest 拿），需要 typetree 细节再按需精确解包单个资源。

## 部署/发布流程命令顺序

1. 改 `Plugin.cs` → 跑 `scripts/build.ps1 -Mod <名字>`（或 `dotnet build` + 部署轮询脚本）。
2. **部署时游戏可能运行** → build.ps1 已内置 10 分钟轮询（每 10 秒重试一次）；若失败告知用户退出游戏。
3. 用户测试后 → `Select-String LogOutput.log -Pattern "..."` 验证（不要猜）。
4. 发布包 `<输出目录>\<ModName>_v<版本>.zip`。

## 命令白名单与禁止事项

**允许**：上表全部命令；`dev_*` 开发侧操作（scaffold/build/inject/self-test 等）。PowerShell 只用于
复制/编译/日志——**不要用 PowerShell 重写含中文的源码文件**（详见《ER2_mod_经验.md》陷阱 27）。

**禁止**：
- 修改游戏原文件；
- 删除 plugins 里其他 mod 的 DLL；
- 用 `FindObjectsOfType` 类代码做每帧扫描（应改用 `Creature.allCreatures`/`aliveCreatures`、
  `Vehicle.allVehicles`、`ItemObject.spawnedItems` 等静态表，或按需 `Physics.OverlapSphere`）。

## 快速验证清单（发布前逐项勾）

```
[ ] 日志有 "Loading [ER2 Xxx x.y.z]"
[ ] 无 "Skipping type" / "Ambiguous" / "Error loading"
[ ] 功能触发日志（如 "Limb detached" / "Weather set to Rain" / "AI ate food"）
[ ] 发布包 zip 已更新（版本号一致）
[ ] 启动日志版本字符串与 BepInPlugin 一致
```
