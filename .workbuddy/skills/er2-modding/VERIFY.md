# 验证清单：确认 Skill 与环境可用

> 用途：验证 `.workbuddy/skills/er2-modding/SKILL.md` 所描述的环境与流程是否真实可用。
> 每一项都有**明确的判断依据**，不要凭感觉勾。

## 前置：环境核实（1 分钟）

```powershell
# 游戏是否更新过（别只看 buildid —— 静默更新不改 buildid）
Get-Item "E:\SteamLibrary\steamapps\common\Easy Red 2\GameAssembly.dll" | Select LastWriteTime
Get-Item "E:\SteamLibrary\steamapps\common\Easy Red 2\Easy Red 2_Data\il2cpp_data\Metadata\global-metadata.dat" | Select LastWriteTime
```

**判断**：修改时间晚于你上次构建时间 → interop 需重新生成，旧编译产物可能运行时
`MissingMethodException`。此时**先启动一次游戏让 BepInEx 重建 interop，再重新编译**。

---

## 验证 1：Skill 能被 WorkBuddy 自动加载

**操作**：另开一个会话，工作目录设为 `<工作区根>`，然后问：
「ER2 的 Mod 是 JSON 配置吗？」

**判断依据**：
- ✅ 正确 → 回答指出 ER2 Mod 是 C# BepInEx 插件、不是 JSON 配置，并引用 `<Game>\BepInEx\plugins\` 等真实路径。
- ❌ 失败 → 回答仍在讲 `mod_manifest.json` / `src/` 目录 → Skill 未加载，检查
  `SKILL.md` 的 YAML frontmatter 是否合法（`name` / `description` 必填）。

---

## 验证 2：构建链路可用（拿现成 mod 验证，不新建）

**操作**（选一个已存在的轻量 mod，如 `ZoomAnywhere`）：
```powershell
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod ZoomAnywhere -SkipDeploy -SkipPackage
```

**判断依据**：
- ✅ `[1/4] Building ZoomAnywhere ...` 后无 error，退出码 0。
- ❌ 报 `dotnet` 未找到 → 装 .NET SDK 6+。
- ❌ 报 interop 引用找不到 → 游戏未启动过或路径不对，先启动游戏一次。

> **注意**：`-SkipDeploy -SkipPackage` 只编译，**不碰游戏目录**，是最安全的验证方式。

---

## 验证 3：部署与日志闭环

**操作**：
```powershell
# 部署（游戏最好先退出，否则 build.ps1 会轮询最多 10 分钟）
powershell -ExecutionPolicy Bypass -File scripts\build.ps1 -Mod ZoomAnywhere -SkipPackage
# 比对哈希
Get-FileHash "ZoomAnywhere\bin\Release\net6.0\ER2_ZoomAnywhere.dll" -Algorithm SHA256
Get-FileHash "E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\plugins\ER2_ZoomAnywhere.dll" -Algorithm SHA256
```
然后启动游戏，**进入一局战斗**，退出后查日志：
```powershell
Select-String "E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\LogOutput.log" -Pattern "Zoom Anywhere"
```

**判断依据**：
- ✅ 两个 SHA256 一致。
- ✅ 日志有 `Loading [ER2 Zoom Anywhere 1.0.1]`，版本号与 `Plugin.cs` 的 `[BepInPlugin]` 一致。
- ✅ 无 `Skipping type` / `Ambiguous` / `Error loading` / `TypeLoadException`。
- ❌ 日志完全没有该插件 → DLL 未部署成功或被 BepInEx 跳过（查版本号是否带字母后缀）。

---

## 验证 4：新建 mod 流程（可选，确认脚手架可用）

**操作**：照 `SKILL.md` §2.1 / §2.2 新建一个最小 mod：
1. 建目录 `MyTestMod/`，照 §2.2 写 `MyTestMod.csproj`（改 `AssemblyName` / `RootNamespace`）。
2. 照 §3 写 `Plugin.cs`，`[BepInPlugin("er2.mytestmod", "ER2 My Test Mod", "1.0.0")]`，
   `Load()` 里只留 `ModLog.LogInfo("ER2 My Test Mod 1.0.0 loaded.");`。
3. 写 `README.txt` + `Nexus_description.md`（**发布必需**）。
4. 在 `scripts/build.ps1` 的 `ValidateSet` 和 `switch ($Mod)` 各加一行。
5. 跑 `scripts\build.ps1 -Mod MyTestMod`。

**判断依据**：
- ✅ 编译通过、部署成功、游戏启动日志出现 `Loading [ER2 My Test Mod 1.0.0]`。
- ✅ 打出的 zip 内含 **DLL + README.txt + Nexus_description.md** 三个文件。
  （**必须拆开 zip 看**——build.ps1 对缺失文档静默跳过）

---

## 验证 5：反编译查 API 链路

**操作**：
```powershell
# 确认 ilspycmd 可用
ilspycmd --version
# 列类型（拿一个真实的类试）
ilspycmd -l c "E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\interop\Assembly-CSharp.dll" | Select-String "Soldier"
```

**判断依据**：
- ✅ `ilspycmd` 有版本输出，且能列出 `Soldier` 相关类型。
- ❌ 命令不存在 → `dotnet tool install -g ilspycmd`。

---

## 总判定

| # | 验证项 | 通过 = 说明 |
|---|---|---|
| 1 | Skill 自动加载 | WorkBuddy 已具备 ER2 领域上下文 |
| 2 | 构建链路 | .NET SDK + interop 引用配置正确 |
| 3 | 部署与日志 | **端到端闭环打通**（最关键） |
| 4 | 新建 mod | 脚手架与 build.ps1 注册可用 |
| 5 | 反编译 | API 查证能力可用 |

**1–3 全绿即认为环境健康**，可以进入正常开发。4–5 按需。
