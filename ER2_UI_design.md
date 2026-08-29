# Easy Red 2 模组 UI 设计指南（原生观感）

> 目标：让 mod 的屏幕元素（通知、血条、数字、提示）看起来"本来就是游戏原生 UI"。
> 方法：优先**复用游戏原生 UI 通道**（反编译 `Assembly-CSharp` 确认的 public static API），
> 不可用时回退到"原生字体 + 游戏自己的描边助手"，最后才用裸 IMGUI。
> 本文件记录机制、API 清单、改造记录与踩坑。反编译原始文件在 `ui_research/` 目录。

## 1. 游戏 UI 架构（反编译确认）

Easy Red 2 的 UI 有三套并行体系，mod 必须分清自己在哪套里：

| 体系 | 载体 | 代表类 | 特点 |
|---|---|---|---|
| **uGUI（Canvas）** | GameObject + Text/Image | `PlayerGUI`、`InteractionGUI2`、`Hint`、`InputDisplayer`、`PhaseBarGUI`、`ObjectiveGUI`、`HitmarkerGUI` | 战斗 HUD 主体；字体/颜色由场景预制体决定；随 Canvas 缩放 |
| **IMGUI（OnGUI）** | `GUI.*` / `GUILayout.*` | `BattleManager.OnGUI`、`GuiExtension`、`MenuGUI` 等 | 仅少量屏幕元素；字体默认引擎内置（LegacyRuntime），**mod 血条/通知目前在这套里** |
| **世界标记** | Texture 池 + 遮挡测试 | `Marker3DGUI` | 头顶/3D 位置的图标渲染，自带遮挡淡出、按 slot 排序、屏幕尺寸缩放 |

关键结论：
- 游戏 HUD（弹药、互动提示、目标、小地图）全部是 **uGUI**，不是 OnGUI。
- `BattleManager.OnGUI` 每帧被调用（mod 的 `[HarmonyPatch(typeof(BattleManager), "OnGUI")]` Postfix 是标准挂点），
  但**游戏自己在 OnGUI 里画的东西极少**（任务预览等），HUD 不在这里。
- 字体：uGUI 用 `FontList`（font_default / font_japanese / font_chinese_traditional），
  `PhaseBarGUI.GetDefaultFont()` 可拿到游戏原生默认字体（interop 里是 public static）。

## 2. 原生 UI 复用清单（全部反编译验证）

以下 API 都是 `Assembly-CSharp`（interop）里的 **public static**，mod 可直接调用：

| API | 签名 | 用途 | 备注 |
|---|---|---|---|
| **原生提示弹窗** | `Corvostudio.UI.Hint.Display(string text, float duration = 4.5f, bool showOnGUIdisabled = false, bool force_this = false)` | mod 通知首选通道（淡入淡出 + 队列 + 原生字体/颜色） | 调用前判 `Hint.instance != null`；`force_this=true` 立即打断当前显示；游戏教程提示也走这里 |
| 提示弹窗单例 | `Hint.instance`（static 属性） | 通道可用性检查；`fadeText.font` 可作字体兜底 | 主菜单/无 Hint 场景返回 null |
| 原生提示颜色 | `Hint.fullColor` / `Hint.clearColor`（static Color） | 回退绘制时用原生文字颜色 | 拿不到时用白色 |
| **原生描边文字** | `GuiExtension.OutlinedLabel(Rect rect, string text, GUIStyle style, int outlineSize = 1)`（另有 float alpha 和无 style 重载） | IMGUI 描边文字（游戏自己的实现，替代手写 ±2px 黑字） | 内部保存/恢复 GUI.color；比手写描边更接近原生轮廓 |
| **原生目标横幅** | `PlayerGUI.ShowObjectiveToPlayer(string objText, string objName, bool force)` | 目标式大横幅（两行：标题+正文） | 会进目标队列（`_QueueRunner`），慎用 |
| **原生短消息** | `PlayerGUI.ShowShortText(string text, bool force)` | 短文本消息 | 用途与 Hint 有重叠，按需选择 |
| **原生世界标记** | `Marker3DGUI.Draw(object key, int slot, Texture tex, Vector3 worldPos, float size, float alpha, Color tint, bool occlusionTest = false)` | 头顶血条/图标（自带遮挡+缩放） | 画整张 Texture，按比例填充需要两张图或动态生成 |
| 标记屏幕尺寸 | `Marker3DGUI.MarkerScreenSize(float a, float b)` | 计算标记像素尺寸 | |
| **原生受伤血屏** | `BloodSplashGUI.PlayEffect()` | 玩家受伤/流血反馈 | 简单粗暴好用 |
| **原生默认字体** | `PhaseBarGUI.GetDefaultFont()` | IMGUI 样式字体 = 游戏字体 | interop 里是 public static（即使原生是 private） |
| 阶段条配色 | `PhaseBarGUI.SetColorsHex(string done, string current, string todo)` + `doneColor/currentColor/todoColor/outlineColor/lineBackColor/lineFillColor` | 配色参考（任务阶段条） | 阶段条本身是 uGUI，不直接复用 |
| 原生命中反馈 | `HitmarkerGUI.HitMarkerFX(...)` / `hitmarker_color` 等 static | 命中反馈参考 | 别抢原生频道，仅参考颜色 |
| 互动提示隐藏 | `InteractionGUI2.SetInteraction(Interaction, int)` / `InteractionGUI.SetInteractions(...)` | NoInteractionHints 拦截点 | 两者是 uGUI 窗口，SetActive(false) 可整体隐藏 |
| 按键提示 | `InputDisplayer.OnEnable` / `RefreshTexts()` | 互动窗口里的按键图标 | 与 InteractionGUI2.inputDisplayer 同体 |

## 3. 原生观感设计准则（本次实践总结）

1. **字体第一**：IMGUI 默认字体（LegacyRuntime/Arial）一眼就是 mod。用 `PhaseBarGUI.GetDefaultFont()`
   赋给 GUIStyle.font，观感立刻向 uGUI 靠拢。字体解析一次并缓存（启动日志记录字体名便于验证）。
2. **描边用原生实现**：手写"黑字±2px 叠加"只有 4 个方向、粗细不匀；`GuiExtension.OutlinedLabel`
   是游戏自己的轮廓实现，轮廓形状/宽度与原生一致。
3. **通知走 Hint 通道**：`Hint.Display` 的淡入淡出、停留时长、排队行为全是原生体验，
   比任何 OnGUI 自绘都像原生。**强制参数全传**（`duration/showOnGUIdisabled/force_this`），
   不要依赖可选参数的默认值（IL2CPP interop 对默认值处理不可靠）。
4. **显示一次就清标记**：Hint.Display 是"入队"，如果放在 OnGUI Postfix 里每帧调用会刷爆队列。
   必须在成功显示后把自己的 `notifyUntil` 清零（`Plugin.notifyUntil = 0f`），只入队一次。
5. **保持回退链**：`Hint.instance == null`（如主菜单）或原生调用抛异常时，
   回退到"原生字体 + OutlinedLabel"，再退到裸 `GUI.Label`。每层 try/catch，绝不打断原生绘制。
6. **颜色克制**：通知文字用 `Hint.fullColor`（原生提示色），不要大红大绿；
   血条用语义色（绿/黄/红）是通用健康语言，游戏无血条可抄，但数字用白色+描边（原生风格）。
7. **透明度**：原生 HUD 背景色多为半透明黑（`Color(0,0,0,0.7)` 量级），纯黑纯白都突兀。
8. **尊重 F5 隐藏 HUD 的联动**：NoInteractionHints 隐藏互动提示时，mod 自有通知也应当隐藏
   （现有 mod 通过反射读 `HudEnabled`，保持该约定）。
9. **世界锚定元素**优先考虑 `Marker3DGUI.Draw`（遮挡/缩放/排序都原生），自绘 WorldToScreenPoint
   没有遮挡测试，会穿墙显示——这是"像 mod"的最大破绽。

## 4. 本次改造记录（2026 轮次）

### ER2_LimbTweaks 2.13.93 → 2.13.95
- 新增 `NativeUi.cs`（LimbTweaks 内独立副本，保持 mod 单文件部署无外部依赖）。
- `NotifyGuiPatch`：通知改走 `Hint.Display(msg, 3.5f, true, true)`（force 打断队列，适合"断肢出血"
  这类紧急反馈）；成功后清 `notifyUntil` 防重复入队。
- `Notify()` 加同消息节流：2.5s 内相同消息不重复入队（防狂按键刷爆原生 Hint 队列——实测连按装备键
  同一条提示 8 连发排队轰炸）。
- 回退绘制：36pt 粗体 + 原生字体 + `Hint.fullColor` 文字色 + `GuiExtension.OutlinedLabel` 描边
  （替换原手写 ±2px 黑描边 + 大红字）。
- 日志：`NativeUi: hint displayed via native Hint channel: <text>` / `NativeUi: using native font '<name>'`
  低频，可保留作功能日志。

### ER2_WeatherControl 1.6.1 → 1.6.3
- 同上：热键切换天气通知走 `Hint.Display`（3s），回退样式 26pt 原生字体 + OutlinedLabel；Notify 加同消息节流。
- 顺手修复启动日志版本号与 BepInPlugin 不一致的历史问题（原日志写 1.3.0，实际 1.6.1）。
- csproj 补 `UnityEngine.TextRenderingModule` / `UnityEngine.UI` 引用（Font/Text 类型需要）。

### ER2_HealthBars 1.0.0 → 1.1.1
- 数字：`new GUIStyle()` + 默认字体 → `NativeUi.MakeStyle(13, Bold, White, MiddleCenter)`（原生字体）。
- 数字绘制：`GUI.Label` → `GuiExtension.OutlinedLabel(..., 1)`（原生描边，白字+黑边可读性大增）。
- 血条本体仍是 OnGUI 自绘（黑底半透明 + 绿/黄/红填充）——后续可选改造：
  `Marker3DGUI.Draw` 两张纹理（背景条+按比例宽的填充条）实现遮挡测试，代价是失去数字自由布局。

### 实测结果（2026-08-14 会话）
- ✅ 原生 Hint 通道生效：WeatherControl F7 连切组合、LimbTweaks 断臂限制提示全部走
  `Hint.Display`（日志 `hint displayed via native Hint channel`），观感与教程提示一致。
- ✅ F5 HUD 联动正常（HUD off/on 交替日志，隐藏时通知不显示）。
- ✅ 字体：`PhaseBarGUI.GetDefaultFont()` 只返回引擎内置 LegacyRuntime；改为抓战斗 HUD 活体
  uGUI Text 后拿到 **'Special UI Font'**（`InteractionGUI2.inputDisplayer.text.font`，游戏 HUD 字体），
  静态缓存只解析一次。
- ✅ 通知节流：`cannot reload` 尝试 4 次只入队 2 次、`cannot pick up` 尝试 2 次只入队 1 次。
- ⚠️ 日志中出现 `Creature.OnDestroy` NullReferenceException：游戏原生场景卸载 bug，
  mod 未 patch OnDestroy，与本 mod 无关，可忽略。

### ER2_NoInteractionHints / ER2_AIFood
- 本次未改。NoInteractionHints 本身就是"隐藏原生 UI"的 mod，无自有绘制；
  AIFood 无屏幕元素。若未来要加提示，直接走 Hint.Display 即可（记得先查 `HudEnabled`）。

## 5. 踩坑记录（UI 专项）

1. **`Corvostudio.UI.Hint` 需要 `using Corvostudio.UI;`**——不在全局命名空间，漏 using 编译报
   CS0246（三个 mod 都踩了一遍）。
2. **`Hint.fadeText` 是 `UnityEngine.UI.Text`**——csproj 必须引用 `UnityEngine.UI.dll`，
   否则 CS0012「类型 Text 在未引用的程序集中定义」。
3. **interop 里的"私有"方法也是 public wrapper**：`PhaseBarGUI.GetDefaultFont()` 原生声明为 private，
   但 il2cpp interop 生成的包装类是 public static，可以直接调（这是 IL2CPP interop 的特性，不是漏洞利用）。
4. **可选参数别省**：`Hint.Display(text)` 依赖默认 duration=4.5 —— IL2CPP interop 的默认值元数据
   存在但不可靠，**所有参数显式传**（`Hint.Display(text, 3.5f, true, true)`）。
5. **每帧入队陷阱**：OnGUI Postfix 里调 Hint.Display 等于每帧往 hintQueue 塞一条。
   成功显示后必须清自己的重绘标记（`notifyUntil = 0f`）。
6. **Hint 通道不一定存在**：主菜单/无 Hint 场景 `Hint.instance` 为 null；原生调用也可能抛异常。
   回退链（原生 → OutlinedLabel → GUI.Label）每层 try/catch 是底线。
7. **GUIStyle 默认黑色文字**（老坑）：`new GUIStyle()` 必须显式 `normal.textColor`，
   且 `new GUIStyle(其他style)` 拷贝构造被 IL2CPP 裁剪——只能无参构造后逐属性赋值。
8. **字体对象生命周期**：Font 来自游戏资源，mod 只引用不创建，不需要释放；
   缓存引用跨场景有效（资源常驻）。
9. **描边标签的 GUI.color 语义**：`GuiExtension.OutlinedLabel` 内部保存/恢复 GUI.color，
   但样式文字颜色由 style.normal.textColor 决定——想变色就改 style 或靠 GUI.color 乘法 tint，
   别假设它一定读 GUI.color。

## 6. 快速实现模板

```csharp
// 1) 通知（推荐，原生弹窗）
if (NativeUi.ShowNativeHint(msg, 3.5f)) { notifyUntil = 0f; return; } // 在 OnGUI Postfix 里

// 2) 回退：原生字体 + 原生描边
notifyStyle = NativeUi.MakeStyle(26, FontStyle.Bold, NativeUi.NativeTextColor(), TextAnchor.MiddleCenter, true);
NativeUi.OutlinedLabel(new Rect(x, y, w, h), msg, notifyStyle, 1);

// 3) 头顶元素（原生标记通道）
// Marker3DGUI.Draw(key, 0, myTex, worldPos, size, alpha, tint, occlusionTest: true);
```

## 8. 原生设置界面复用（SettingsGUI_V2 / Corvostudio.SettingsData）

> ModManager 用这套体系把"Mods"页直接嵌进游戏原生设置界面。反编译原件在 `ui_research/settings/`。

### 架构
- 设置界面 = `SettingsGUI_V2`（MonoBehaviour，`instance` 静态单例，主菜单场景）。
  - `settingsMenus`（`Il2CppReferenceArray<SettingsPageData>`，可写）：设置页列表，翻页就是数组下标。
  - `currentOpenedMenu`（static int）：当前页索引。
  - `contentPage`（Transform）：当前页内容的挂载容器。
  - `SettingsTabLeft/Right()`：翻页（Public，可 patch）；`FillSettingPage(bool)` 协程负责填充原生页。
  - `title`（Text）：页标题。
- 设置项 = `Corvostudio.SettingsData.Setting*` 组件（MonoBehaviour），**代码驱动、运行时动态创建**：
  - `SettingToggle(string text_id, bool start_val, UnityAction<bool> cb, bool refreshGuiOnChange)`
  - `SettingSlider(string text_id, float start_val, UnityAction<float> cb, float min, float max, string override_value_text = null, float visualizeMultiplier = 1f, bool wholeNumbers = false)`
  - `SettingDropdown(string text_id, Il2CppSystem.Collections.Generic.List<Dropdown.OptionData> opts, int start_idx, UnityAction<int> cb)`
  - `SettingPanel(string panel_id)`（分区标题）/ `SettingSpace(float)`（间距）
  - 全部有 `float Create(Transform contentPage, float yShift)`（实例化到容器，返回新 y）和 `float GetHeight()`。
- BepInEx 侧：`IL2CPPChainloader.Instance.Plugins`（`Dictionary<string, PluginInfo>`）→
  `PluginInfo.Instance as BasePlugin` → `.Config`（`ConfigFile`：`Values` 枚举、`this[ConfigDefinition]`、
  `ConfigEntryBase.SettingType` / `BoxedValue` / `Description.AcceptableValues`；`SaveOnConfigSet` 默认自动存盘）。

### 注入方法（ModManager 已验证的套路）
1. patch `SettingsGUI_V2.Update` Postfix 轮询 `EnsureInjected`：
   `settingsMenus` 扩容 +1，末尾塞 `new SettingsPageData((SettingsPage)999)`（枚举可强转任意值），
   `pageName_id = "MODS"`。幂等判断：检查末项 `pageName_id == "MODS"`（场景重载自动重注入）。
2. patch `SettingsTabRight` Prefix：当 `currentOpenedMenu + 1 == myIndex` 时接管——
   设 `currentOpenedMenu = myIndex`、清空 `contentPage` 子物体、用 `Setting*` 组件填充、设 `title.text`，
   `return false` 跳过原生。往回翻（TabLeft）放行原生。
3. 配置项类型映射：bool→Toggle、float→Slider（`AcceptableValueRange<float>` 给 min/max）、
   int→Slider（wholeNumbers）、string+`AcceptableValueList<string>`→Dropdown。
4. 修改回调：`entry.BoxedValue = v`（自动写盘）+ `cfg.Save()` 保险。

### 踩坑（ModManager 开发实测）
- `UnityAction<T>` 是 Il2Cpp 委托类，**构造函数只接受 IntPtr**；必须两步创建：
  `System.Action<T> a = ...; UnityAction<T> ua = a;`（interop 提供 `implicit operator UnityAction<T>(Action<T>)`）。
- **UnityAction 委托桥接的 marshaling 在 IL2CPP 下不可靠（重大坑）**：
  - `UnityAction<bool>`：切 Off 时回调收到的永远是 `true`（False 被吞）。
  - `UnityAction<float>`：回调参数是**垃圾 float**（如 3.575383E+35，把配置写坏）。
  - 现象不一致（bool 看着正常、float 明显乱码），极易误判。**最终方案：所有交互控件
    （Toggle/Slider/Dropdown）一律不绑事件，改为每帧轮询 `value/isOn` 对比 lastValue，
    变化时写配置 + 0.5s 写盘节流 + 页面重建时强制落盘。**
- BepInEx 6 的 chainloader 属性叫 **`Plugins`**（不是 5.x 的 `PluginInfos`），在泛型基类
  `BaseChainloader<T>` 上，编译期可访问。
- `Setting*` 组件的 `Create()` 需要 prefab，但 prefab 在 AssetBundle 里（`Resources.Load` 拿不到，
  `Create()` 报 "The Object you want to instantiate is null"）。**最终方案：从原生设置页已实例化
  的克隆体 `Settings_XXX(Clone)` 上 Instantiate 独立副本作为模板**（副本独立于源，源被 Destroy
  后仍可用）；Toggle 模板只在 graphic 页有（page 0/1 只有 Dropdown/Button/Slider），模板缺失时
  用文本行兜底。
- 翻页越界：注入的 MODS 页是最后一页，再按右会让 `currentOpenedMenu` 越界产生空白"第二页"，
  左翻回来时原生会重填我们的页（空白）。**修复：TabRight 在 cur==myIndex 时 return false；
  TabLeft 在 cur==myIndex+1 时接管重填。**
- `foreach (Transform child in transform)` 枚举元素是 Il2CppSystem.Object 不能隐式转 Transform，
  必须 `childCount + GetChild(i)`。
- 自建 uGUI 行的布局：**手动 anchoredPosition 与原生 Content 锚点体系对不上会跑出视口；
  最终用 VerticalLayoutGroup + ContentSizeFitter 自动布局**（容器锚定 Content 顶部），行内 Text
  用锚点拉伸到行宽 + `resizeTextForBestFit` 防长名截断，再 `Canvas.ForceUpdateCanvases()` 后
  读容器高度设置 Content.sizeDelta（滚动范围）。
- 原生设置字体直接取 `SettingsGUI_V2.instance.title.font`（设置页标题字体），零依赖。
- `new GameObject` 后 `AddComponent<RectTransform>()` 可行（替换默认 Transform）。

### 实测结果（2026-08-14 会话，ModManager 1.0.2）
- ✅ 注入成功：原生 4 页（settings/gameplay/graphic/controls）+ MODS = 5 页，翻页/回翻/越界拦截全部正常。
- ✅ 自动布局方案下标题完整可读（长名自适应字号）、折叠/展开、滚动全部正常。
- ✅ 枚举 13 个带配置的插件，配置项按类型映射（bool→Toggle、float/int→Slider、string→Dropdown）。
- ✅ 开关/滑条/下拉修改全部正确写回 cfg（轮询方案验证：`enabled = false`、滑条拖动连续值全部正确落盘）。
- ⚠️ 早期版本曾把垃圾 float（E+35）写入 er2.aifood.cfg —— 已手动修复并随轮询方案根治。
- ✅ 双语构建：`-p:DefineConstants=CN_BUILD` 编译中文版（中文 mod 名/键名/单位/页标题"模组"），
  默认构建英文版。中文标签映射只覆盖自制 5 mod（其余显示原文）；滑条整数化规则
  （范围 ≥10 整数步进、小范围如 0-1 保持小数）。
- ⚠️ 中文词典用 `StringComparer.OrdinalIgnoreCase` 时 `"enabled"` 与 `"Enabled"` 是同一键，
  字典初始化器抛 ArgumentException → 整个静态类构造失败 → 页面空白（日志：
  "The type initializer for 'X' threw an exception"）。

## 7. 研究材料

- `ui_research/*.decompiled.cs`：`ilspycmd -t <Type> Assembly-CSharp.dll` 反编译产物
  （Hint / PlayerGUI / InteractionGUI(2) / InputDisplayer / PhaseBarGUI / ObjectiveGUI /
  HitmarkerGUI / BloodSplashGUI / Marker3DGUI / GuiExtension / FontList / MenuNotification /
  VersionText / BattleManager / MiniMapGUI / MapGUI / MissionStatusGUI / VehicleGUI /
  ScopeGUI / ChatMessage / SkipPromptGUI / TutorialGUI / VehGuiArmamentDisplay /
  VehicleSeatGUI）。
- 反编译命令：`ilspycmd -t Corvostudio.UI.Hint "E:\SteamLibrary\steamapps\common\Easy Red 2\BepInEx\interop\Assembly-CSharp.dll" -o <outdir>`

## 9. HideAnything 研究补充（v4 会话，反编译确认）

### 各 UI 类的可见根（隐藏目标）
| UI | 类 | 可见根/隐藏目标 | 备注 |
|---|---|---|---|
| 小地图 | `MiniMapGUI` | `MiniMapGUI.Instance.miniMap`（RectTransform，贴图）+ **组件本体 GameObject**（面板/弹窗根）+ `detailsContainer` + `mapName` | 只藏 miniMap 会剩下面板框（"弹窗还在"）；必须连组件本体一起藏 |
| 全屏地图 | `MapGUI` | `renderersParent`（Transform）+ 组件本体 | 无静态 instance，用缓存查找；地图关闭时组件 inactive → FindObjectOfType 找不到（只在打开时能藏） |
| 任务状态 | `MissionStatusGUI` | `Instance.panel` | 静态 instance |
| 阶段条 | `PhaseBarGUI` | `Instance.gameObject` | 静态 instance |
| 命中/准星 | `HitmarkerGUI` | `Instance.gameObject`（hitmarker+crosshair 一体） | `HitMarkerFX(bool, Nullable<Vector3>, bool, float)` 可 Prefix 拦截 |
| 瞄具 | `ScopeGUI` | `Instance.scopeRoot` | 静态 instance |
| 教程 | `TutorialGUI` | `Instance.captions_panel` / `tasks_panel` | 静态 instance |
| 聊天 | `ChatMessage` | 静态 `NewChatMessage/NewJoinMessage/NewLeftMessage/NewKilledMessage/NewDisabledPlayerVehicleMessage` 全 Prefix 拦截 + `PlayerGUI.chatRoot` 隐藏 | 聊天经 PlayerGUI 渲染 |
| 玩家 HUD | `PlayerGUI` | 静态 instance；子元素：weapon_name/weapon_ammos/zeroingText/bleedingOutUi/vault_icon/doRoleNearbyicon/speaker_icon/squadGUI_Panel/squadData_Panel；objectiveGUI（目标横幅容器） | 逐元素藏 |
| 跳过提示 | `SkipPromptGUI` | 静态 `Draw()` Prefix 拦截 | |

### 设置界面滚动结构（局内 vs 主菜单）
- 层级：`Content(我们的页) → Content(父级) → Viewport(Mask) → Scroll View(ScrollRect)`
  —— **滚动范围由 ScrollRect.content（父级 Content）决定**，局内与主菜单一致，但主菜单恰好
  contentPage 即 content，局内必须向上找 ScrollRect 取 content
- `SettingsGUI_V2` interop 只有 Start/Update/FillSettingPage/SettingsTabLeft/Right/Refresh 等，
  **没有 OnDisable/OnDestroy**（陷阱 38）
- 局内暂停（timeScale=0）时 `Time.time` 冻结，一切节流必须用 `Time.unscaledTime`（陷阱 36）
- 修复套路：垂直拉伸锚点 content → 改顶部锚定 + sizeDelta + ForceRebuildLayoutImmediate；
  离开页面时还原锚点
