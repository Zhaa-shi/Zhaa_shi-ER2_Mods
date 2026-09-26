# ER2 Mod 启停切换器

一键切换「纯原版 / 带 mod」状态，不用手动搬 DLL。

## 为什么需要它

BepInEx 启动时会扫描 `BepInEx\plugins\` 下的**所有** `*.dll` 并加载，没有内置开关。
装的 mod 一多，想回到原版就得逐个把 DLL 挪出去 —— 麻烦且容易漏。

本工具的做法：**把插件在 `plugins\` 和 `plugins_disabled\` 之间移动**。
移出去 = 不加载，移回来 = 加载。只改位置，不改内容，随时可手工还原。

## 快速使用

| 操作 | 命令 |
|---|---|
| 打开交互菜单 | 双击 `launch-er2.bat`，或 `powershell -ExecutionPolicy Bypass -File scripts\er2-mods.ps1` |
| 真·原版启动 | 双击 `launch-er2.bat` 前先选 `n`，或 `launch-er2.bat vanilla` |
| 真原版（无提示） | `launch-er2.bat pure` 或 `-Pure` |
| 恢复 BepInEx | `launch-er2.bat restore` 或 `-RestoreBepInEx` |
| 只看状态 | `powershell -ExecutionPolicy Bypass -File scripts\er2-mods.ps1 -List` |

### 两种「回到原版」的区别

| 档位 | 做什么 | 游戏内效果 | 「已修改」提示 |
|---|---|---|---|
| **vanilla** | 只禁用全部插件 | 没有任何 mod | **仍然会弹** |
| **pure（真·原版）** | 禁用全部插件 **+ 关闭 BepInEx 注入器** | 完全纯净 | **不再弹** |

**为什么 vanilla 还会弹提示**：游戏检测的是游戏目录下有没有 `winhttp.dll`
（BepInEx 的注入器），**与加载了哪些插件无关**。只要它还在，游戏就认为被改过。
证据：游戏程序集 `global-metadata.dat` 里有 `winhttp.dll in game dir` 这条检测文案。

**pure 模式移动的 3 个文件**（都在游戏根目录）：

| 文件 | 作用 |
|---|---|
| `winhttp.dll` | 注入器本体（游戏检测的就是它） |
| `doorstop_config.ini` | Doorstop 配置 |
| `.doorstop_version` | 版本标记 |

它们会被移到游戏根目录下的 `bepinex_off\`。恢复用 `-RestoreBepInEx` 或菜单 `r` 键。

> ⚠️ **pure 期间任何 mod 都不工作**（BepInEx 根本没启动）。
> 文件不会丢失 —— 脚本每次移动后都校验，任一步失败立即回滚。

### 交互菜单


```
   1. commandmarker                        三方 [资源包]
   2. ER2_AIFood.dll                       我的
   ...
  28. ER2_ZoomAnywhere.dll                 我的
```

| 输入 | 作用 |
|---|---|
| `1,3,5` | 切换这几项（已启用→禁用，已禁用→启用） |
| `a` | 全部启用 |
| `n` | 全部禁用（= 纯原版） |
| `m` | 只启用「我的」，禁用第三方 |
| `t` | 只启用「第三方」，禁用「我的」 |
| `s` 或回车 | 只刷新状态 |
| `q` | 退出 |

### 命令行参数

```powershell
-File scripts\er2-mods.ps1 -Vanilla              # 全部禁用
-File scripts\er2-mods.ps1 -All                  # 全部启用
-File scripts\er2-mods.ps1 -OnlyMine             # 只留自己的 mod
-File scripts\er2-mods.ps1 -List                 # 只看状态
-File scripts\er2-mods.ps1 -Disable 'A.dll','B.dll'
-File scripts\er2-mods.ps1 -Enable 'A.dll'
-File scripts\er2-mods.ps1 -GameDir 'D:\Steam\...\Easy Red 2'
```

## 切换后要重启游戏

BepInEx 在**启动时**扫描插件目录，所以切换对**当前已运行的游戏无效**。
`launch-er2.bat` 已把「切换 → 启动」串成一步。

## 安全设计

- **只移动登记在册的条目** —— `plugins\` 里的未知文件不会被碰
- **先移动后校验** —— 任一步失败立即回滚，不留半截状态
- **禁用 ModManager 前会警告** —— 因为它一被禁用，游戏内 MODS 页面就没了
- **不改 DLL 内容** —— 纯位置移动，任何时候都能手工改回来

## 归属清单

`scripts/er2-mods.ps1` 顶部有两份清单，**新增插件时按需维护**：

```powershell
# 你自己开发的 mod（-OnlyMine / m 键用）
$Mine = @(
    'ER2_AIFood.dll'
    ...
)

# 配套资源目录：禁用插件时一起移动
$Companions = @{
    'ER2_VeteranHVT.dll' = @('ER2_VeteranHVT')   # 插件 + 音频资源
}

# 资源包（BepInEx 不加载，但为"彻底还原"一并管理）
$Bundles = @(
    'commandmarker'
)
```

> **`$Companions` 很重要**：像 `ER2_VeteranHVT.dll` 这类插件带了同名资源目录，
> 只移 DLL 不移目录，轻则功能异常重则报错。发现新插件带资源目录时加进来。

## 目录结构

```
<Game>\BepInEx\
├── plugins\              启用的插件（BepInEx 扫描这里）
├── plugins_disabled\     禁用的插件（BepInEx 不扫描）
└── config\               配置（不受影响，切换不会丢设置）
```

**配置不会丢** —— 切换只动插件目录，`config\*.cfg` 原封不动。所以你来回切换后，
每个 mod 的设置还是原来的。

## 维护须知：bat 文件必须保持纯 ASCII

`launch-er2.bat` **不能加中文注释**。`cmd.exe` 按 ANSI/GBK 读取 .bat，
而文件是 UTF-8 保存的，中文会变成乱码并被当作命令执行（报一堆
`'xxx' is not recognized as an internal or external command`）。

所以该文件里所有说明都用英文写，文件名也用 ASCII（`launch-er2.bat`
而非 `启动ER2.bat`）。要改它的话请保持这个约定。

> 另：PowerShell 脚本（`er2-mods.ps1`）相反 —— 它**含中文，必须以
> UTF-8 BOM 保存**，否则 PS 5.1 会按 GBK 读取导致中文乱码、语法报错。
> 两者要求正好相反，别搞混。

## 与 ModManager 的分工

| | 管什么 | 何时生效 |
|---|---|---|
| **本工具** | 插件**是否加载** | 游戏启动前 |
| **ER2 ModManager** | 已加载插件的**配置项** | 即时 |

两者互补：ModManager 无法让插件不加载（它自己也得先被加载才能工作），
本工具无法改配置值。**建议保留 ModManager 为启用状态**，否则游戏里就调不了配置了。

## 常见问题

**切换后游戏里 mod 还在？**
游戏没重启。BepInEx 只在启动时扫描目录。

**想让某个 mod 彻底不加载，但它不在列表里？**
把它加进 `$Mine` 或 `$Bundles` 清单；未登记的未知文件脚本会刻意跳过。

**移错了/想手工恢复？**
直接把文件从 `plugins_disabled\` 拖回 `plugins\` 即可，两者结构完全一样。

**`plugins\` 里有个 `commandmarker` 是什么？**
UnityFS AssetBundle（Unity 资源包），BepInEx 不加载它。已纳入管理清单，
「纯原版」时会被一起移走。
