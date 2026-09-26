# Shared/NativeUi —— 原生 UI 适配层

让 mod 的自建页面「看起来和 Easy Red 2 原生界面一模一样」的复用组件。
回收自 Endless mod 的 `UI/` 目录（该 mod 已废弃移除），保留下来供后续 mod 使用。

## 为什么需要它

设计约束（`ER2_无尽模式_设计方案.md` §3）：「**除了文本内容，UI 要与战役模式一模一样**」——
IMGUI 仿制观感不被接受。要做到这一点，必须直接取原生资产，而不是自己画：

| 需求 | 原生来源 |
|---|---|
| 字体 | `LocalizationManager.GetFont` → 回退活体 uGUI `Text` → 回退 `GUI.skin` |
| 日期排版 | `NativeAssets.DateText`（形如 `12 五月 1945`） |
| 阵营旗帜 | `ResourcesManager.GetFactionData().flag` |
| 本地化文案 | `Language.GetText` |

## 文件

| 文件 | 作用 |
|---|---|
| `NativeUiLog.cs` | 日志出口（注入式，未注入时静默） |
| `NativeUiConfig.cs` | 宿主配置（语言开关） |
| `NativeAssets.cs` | 取原生字体 / 旗帜 / 日期文案 / 本地化文本 |
| `UiTheme.cs` | 原生观感 IMGUI 主题（字体 + 描边那套）+ 阵营配色 |
| `NativeTabHost.cs` | 借原生战役页签作画（切入 / 藏内容 / 还原） |
| `InputBlocker.cs` | 独立 Canvas 挡 uGUI 射线，防点击穿透 |

## 接入方式（无编译期依赖）

在宿主 mod 的 `.csproj` 里链接源码：

```xml
<ItemGroup>
  <Compile Include="..\Shared\NativeUi\*.cs" LinkBase="NativeUi" />
</ItemGroup>
```

在 `Plugin.Load()` 里初始化一次：

```csharp
NativeUiConfig.Chinese = DefaultChinese;        // CN_BUILD 构建为 true
NativeUiLog.Tag        = "[MyMod] ";
NativeUiLog.Info       = s => { ModLog.LogInfo(s);  FileLog(s); };
NativeUiLog.Error      = s => { ModLog.LogError(s); FileLog("[E] " + s); };
```

> **为什么日志要注入**：本层不引用 BepInEx，因此拿不到 `Plugin.Log`。
> 这个间接层还有个好处 —— 适配层可以脱离游戏编译，方便验证。

## 编译所需引用

```xml
<Reference Include="Assembly-CSharp">          <!-- <Game>\BepInEx\interop\ -->
<Reference Include="Il2Cppmscorlib">           <!-- <Game>\BepInEx\interop\ -->
<Reference Include="UnityEngine">              <!-- <Game>\BepInEx\interop\ -->
<Reference Include="UnityEngine.CoreModule">   <!-- <Game>\BepInEx\interop\ -->
<Reference Include="UnityEngine.UI">           <!-- <Game>\BepInEx\interop\ -->
<Reference Include="UnityEngine.UIModule">     <!-- <Game>\BepInEx\interop\ -->
<Reference Include="UnityEngine.IMGUIModule">  <!-- <Game>\BepInEx\interop\ -->
<Reference Include="UnityEngine.TextRenderingModule">  <!-- <Game>\BepInEx\interop\ -->
<Reference Include="Il2CppInterop.Runtime">    <!-- <Game>\BepInEx\core\ -->
```

全部 `Private=false`。已实测：net6.0 下 0 error。

## 已知坑（来自 Endless 的实测记录）

- **`new GameObject(name, typeof(...))` 在 interop 下不可用** —— 组件参数要
  `Il2CppReferenceArray<Il2CppSystem.Type>`。一律用 `AddComponent`。
- **`Button.transition = Selectable.Transition.ColorTint`** 才能得到原生悬停态。
- **手工 `new` 原生数据对象是半成品** —— 形能画出来但内部状态（注册表 / 完成度扫描）缺失，
  点击链会静默失败。原生**控件**可以克隆，原生**数据对象**不能手工造。
- **`LocalizationManager.GetFont` 可能返回 null**，必须准备活体 `Text` → `GUI.skin` 的回退链。
- **`BeginScrollView` 被 IL2CPP 裁剪** —— 列表用分页或只绘可见行。
