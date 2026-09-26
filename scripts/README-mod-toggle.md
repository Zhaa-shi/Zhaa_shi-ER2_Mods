# ER2 Mod 启动器

一个文件，双击就能切换「带 mod 玩 / 无 mod 玩」。

## 用法

双击 **`launch-er2.bat`**，出现三个选项：

```
    1.  带 mod 玩
        启用全部插件，正常启动游戏

    2.  无 mod 玩（纯净）
        禁用全部插件并关闭 BepInEx，不弹「已修改」提示

    3.  恢复
        重新开启 BepInEx（不启动游戏）

    d.  高级选项
    q.  退出
```

也可以带参数直接执行：

| 命令 | 作用 |
|---|---|
| `launch-er2.bat mods` | 带 mod 玩 |
| `launch-er2.bat pure` | 无 mod 玩（纯净） |
| `launch-er2.bat restore` | 只恢复 BepInEx，不启动游戏 |

## 两个档位的区别

| 档位 | 做什么 | 游戏内效果 | 「已修改」提示 |
|---|---|---|---|
| **带 mod 玩** | 启用全部插件 | 所有 mod 生效 | 会弹 |
| **无 mod 玩（纯净）** | 禁用全部插件 **+ 关闭 BepInEx 注入器** | 完全纯净，像没装过 mod | **不弹** |

### 为什么「纯净」要关 BepInEx

游戏内置一个 `IntegrityGuard` 检查，按**特征名**逐个探测 BepInEx/Doorstop 的安装痕迹，
**跟你加载了几个插件无关**。只要任一特征还在，游戏就弹：

> An unofficially modified version of the game has been detected.

取证（两处）：

- 游戏程序集 `global-metadata.dat` 里有 `winhttp.dll in game dir`、`BepInEx/core`、`BepInEx/plugins` 等特征字符串
- 实测 `Player.log` 里 IntegrityGuard 逐条打出命中路径：
  `[IntegrityGuard] BepInEx/Doorstop rilevato (early): .../BepInEx/core`，
  移走后再启动又报 `.../dotnet` —— 说明它是在**逐个探测**这些特征

> 只移 `winhttp.dll` 不够（提示依旧）；必须把 `BepInEx\` 和 `dotnet\` 也移走。

所以「纯净」档位会把这三个文件移到游戏根目录下的 `bepinex_off\`：

| 移走的东西 | 作用 |
|---|---|
| `winhttp.dll` | 注入器本体 |
| `doorstop_config.ini` | Doorstop 配置 |
| `.doorstop_version` | 版本标记 |
| `BepInEx\` | BepInEx 本体（约 83 MB） |
| `dotnet\` | BepInEx 6 的 CoreCLR 运行时 |

恢复用「3. 恢复」。

> ⚠️ **纯净期间任何 mod 都不工作** —— BepInEx 根本没启动。
> 文件不会丢：每次移动后都校验，任一步失败立即回滚。

## 高级选项

按 `d` 进入：

| 选项 | 作用 |
|---|---|
| 1 | 只启用「我的 mod」，禁用第三方 |
| 2 | 只启用「第三方」，禁用我的 |
| 3 | 查看完整插件清单（`[我]` / `[三]` 标记归属） |
| 4 | 不改动，直接启动游戏 |

## 原理

BepInEx 启动时扫描 `BepInEx\plugins\` 下**所有** `*.dll` 并加载，没有内置开关。
本工具的做法是**移动文件**：

```
<Game>\BepInEx\
├── plugins\              启用的插件（BepInEx 扫描这里）
├── plugins_disabled\     禁用的插件（BepInEx 不扫描）
└── config\               配置（不受影响）

<Game>\
└── bepinex_off\          关闭 BepInEx 时，注入器文件暂存这里
```

**配置不会丢** —— 切换只动插件目录，`config\*.cfg` 原封不动，
来回切换后每个 mod 的设置还是原来的。

**需要重启游戏** —— BepInEx 只在启动时扫描目录，切换对已运行的游戏无效。

## 维护须知

### 两个编码约束（很重要，别搞混）

| 部分 | 编码要求 | 原因 |
|---|---|---|
| **bat 头部**（`###PS1###` 之前） | **必须纯 ASCII，且不能有 BOM** | `cmd.exe` 按 ANSI/GBK 读取，中文会乱码并被当命令执行；BOM 会破坏 `@echo off` |
| **PowerShell 部分**（`###PS1###` 之后） | **UTF-8**，运行时自动加 BOM | PS 5.1 需要 BOM 才能正确读中文 |

脚本运行时会把 `###PS1###` 之后的部分提取到临时 `.ps1`（带 BOM），
所以中文提示能正常显示，而 cmd 不会碰到它们。

**改这个文件时**：bat 头部加注释只能用英文；中文注释请加到 `###PS1###` 之后。

### 新增插件时

在 PowerShell 部分的 `$Companions` 里登记配套资源目录（如果该插件带同名资源文件夹）：

```powershell
$Companions = @{
    'ER2_VeteranHVT.dll' = @('ER2_VeteranHVT')   # 插件 + 音频资源，必须一起移动
}
```

只移 DLL 不移资源目录，轻则功能异常重则报错。

如果是你自己开发的新 mod，还要加进 `$mineList`（高级选项靠它区分归属）：

```powershell
$mineList = @(
    'ER2_AIFood.dll'
    ...
)
```

## 与 ModManager 的分工

| | 管什么 | 何时生效 |
|---|---|---|
| **本启动器** | 插件**是否加载** | 游戏启动前 |
| **ER2 ModManager** | 已加载插件的**配置项** | 即时 |

两者互补：ModManager 无法让插件不加载（它自己也得先被加载才能工作），
本启动器无法改配置值。

**注意**：「纯净」档位会连 ModManager 一起关掉，那时游戏内没有 MODS 页面。
如果只是不想要某些第三方 mod、但仍想调设置，用「高级选项 → 只启用我的」，
而不是「纯净」。

## 常见问题

**切换后游戏里 mod 还在？**
游戏没重启。BepInEx 只在启动时扫描目录。

**移错了 / 想手工恢复？**
直接把文件从 `plugins_disabled\` 拖回 `plugins\`，
或从 `bepinex_off\` 拖回游戏根目录即可，两边结构完全一样。

**`plugins\` 里有个 `commandmarker` 是什么？**
UnityFS AssetBundle（Unity 资源包），BepInEx 不加载它。
它属于某个 mod 的资产，会被一并移动以保持"纯净"彻底。
