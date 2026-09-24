using System.Collections.Generic;
using UnityEngine;

namespace ER2Shared;

/// <summary>
/// 两个 mod 共用的 **IMGUI 设计令牌 + 绘制原语**。
///
/// **为什么是"共享源码"而不是各写一份**：战场指挥官与通用生成要重绘成同一套观感，
/// 配色/间距/字号/控件这些令牌必须只有一个定义处——否则改一处漏一处，
/// 又会变成"两个面板长得不一样"的事故。两个程序集互相不引用（宿主与 addon 只按反射联动），
/// 故两边 csproj 用 `&lt;Compile Include="..\Shared\Er2Ui.cs" Link="Shared\Er2Ui.cs" /&gt;`
/// 编译同一份文件（各自 internal，互不影响）。
///
/// **本工作区的硬约束（血泪版）**：
/// - `new GUIStyle(GUIStyle)` 拷贝构造被 IL2CPP 裁剪 → 只能 `new GUIStyle()` + 逐属性赋值（陷阱 5）；
/// - `new GUIStyle()` 的 `normal.textColor` **默认黑** → 必须显式设，否则白底看不见字；
/// - 自建 `Texture2D` 必须 `hideFlags = (HideFlags)61`（陷阱 12：不设会在场景切换/资源回收后丢背景）；
/// - `GUI.Button`/`GUI.Label` 的文字**不按矩形裁剪** → 定宽控件必须自己适配字号（陷阱 76）；
/// - `GUIStyle.CalcSize` 在 IL2CPP 下**可用**（BackpackPanel 1.4.9 起长期使用验证），
///   但需要"设字号→测量→还原"，且结果要缓存（每次调用有 GUIContent 分配）。
///
/// 结构：① 令牌（尺寸/字号/配色）② 资源（按色缓存的纯色贴图）③ 样式工厂
/// ④ 控件原语（填充/页签网格/分页/星标）⑤ 文本自适应。
/// **重绘时只动 ①③，②④ 的行为不变。**
/// </summary>
internal static class Er2Ui
{
	// ══════════ ① 令牌：尺寸（随 Scale 联动）═════════
	// 2.5.1：**全部改成属性**——以前是 const，"UI 是死的"根源就在这里：
	// 字号/间距/行高写死，玩家没法按自己的屏幕和视力调。现在统一乘 `Scale`。
	public static float Pad => 10f * Scale;      // 面板内边距
	public static float Gap => 4f * Scale;       // 控件间距（页签/行）
	public static float RowH => 28f * Scale;     // 列表行高
	public static float TabH => 24f * Scale;     // 页签高
	public static float BtnH => 22f * Scale;     // 小按钮（分页箭头等）
	public const float PanelWDefault = 320f;     // 生成面板**默认**宽（实际宽度由 PanelFrame 持有）

	// ══════════ ① 令牌：字号（随 Scale 联动）══════════
	// ⚠️ 字号是 int，必须 RoundToInt——直接截断会让 0.8 倍下 12*0.8=9.6 变 9，落差过大。
	public static int FontTitle => Mathf.RoundToInt(15 * Scale);
	public static int FontBody => Mathf.RoundToInt(12 * Scale);
	public static int FontSmall => Mathf.RoundToInt(10 * Scale);
	public static int FontTabMax => Mathf.RoundToInt(12 * Scale);  // 页签自适应上限
	public static int FontTabMin => Mathf.Max(6, Mathf.RoundToInt(8 * Scale)); // 自适应下限（再小不可读）

	/// <summary>
	/// 2.5.1：**自适应 UI 缩放倍率**（由 AutoScale() 按屏幕分辨率算出，不需要玩家设置）。
	/// 影响上面全部尺寸与字号令牌 → 面板高度自动跟着内容变化，不需要单独调高度。
	/// ⚠️ 改这个值**必须**走 SetScale()，它会清 FitSize 缓存——
	/// 否则缩放后字号不更新（测量缓存里存的是旧字号下的结果）。
	/// </summary>
	public static float Scale = 1f;
	public const float ScaleMin = 0.75f;
	public const float ScaleMax = 1.6f;

	/// <summary>
	/// 面板侧**各判各的**重建依据：缓存"我建样式时的 Scale"，本帧比一下就知道要不要重建。
	/// 比全局 dirty 标志可靠——多个面板共用一个 dirty 时，第一个清掉后其余的就不重建了。
	/// 用法：`if (Er2Ui.ScaleChangedSince(myStyleScale)) { RebuildStyles(); myStyleScale = Er2Ui.Scale; }`
	/// </summary>
	public static bool ScaleChangedSince(float lastScale) => Mathf.Abs(Scale - lastScale) > 0.001f;

	/// <summary>
	/// 唯一改 Scale 的入口：夹到 [ScaleMin, ScaleMax] 后写入，**变了就清字号测量缓存**。
	/// 为什么必须清：FitSize 的缓存 key 是 (宽|max|min|文本)，虽然大部分情况下宽度/字号也随
	/// Scale 变、key 会自然不同，但调用方传的是**自己算出来的固定像素宽**（不随 Scale 的那种）
	/// 时 key 就不变 → 会拿到旧字号下测出的结果，表现为"缩放后字还是那么大/那么小"。
	/// 清一次的代价远小于这个 bug 的排查成本。
	/// </summary>
	public static void SetScale(float v)
	{
		if (float.IsNaN(v) || v <= 0f) return;                       // 脏值直接忽略，保持上一次
		float s = Mathf.Clamp(v, ScaleMin, ScaleMax);
		if (Mathf.Abs(s - Scale) <= 0.001f) return;
		Scale = s;
		fitCache.Clear();
	}

	/// <summary>2.5.1：面板**默认**宽度——随 Scale 联动（320 是 1.0 倍下的设计值）。</summary>
	public static float PanelW => PanelWDefault * Scale;

	// ══════════ ① 自适应（2.5.1：用户"UI 不能是死的，要是可以动态调整的"= 自适应）═════════
	// 不是让玩家手动拖/输数值——是 UI 自己跟着**游戏原生**的分辨率倍率走：
	// 玩家在游戏设置里调 UI 大小，两个 mod 的界面同步缩放，观感和原生 HUD 一致。
	//
	// 倍率来源优先级：
	//   ① `ResourcesManager.ResolutionMult`（**游戏原生**，由各 mod 每帧写进 NativeMult）
	//   ② 取不到时兜底：按屏幕分辨率算 min(w/1920, h/1080)，clamp 到 [0.75, 1.6]
	// ——取 min 保证两个方向都不溢出（只按高度算的话，21:9 超宽屏会低估横向空间）。
	private const float DesignW = 1920f, DesignH = 1080f;

	/// <summary>兜底用：按屏幕分辨率算（取宽高比例的较小值，两个方向都不溢出）。</summary>
	private static float ScreenMult()
	{
		float w = DesignW, h = DesignH;
		try { w = Screen.width; h = Screen.height; } catch { }
		if (w <= 0f || h <= 0f) return 1f;
		return Mathf.Min(w / DesignW, h / DesignH);
	}

	/// <summary>
	/// **游戏原生**分辨率倍率 = `ResourcesManager.ResolutionMult`（玩家在游戏设置里调的 UI 大小）。
	/// 两个 mod 共用这一份实现（SquadCommand 原本自己有一份 `ResMult()`，现转发到这里，
	/// 避免两处各缓存各的、算法漂移）。0.5s 粒度：OnGUI 一帧多次事件、十几处取样式，
	/// 每次都读 interop 属性是每秒上千次调用；而运行中倍率几乎不变。
	/// </summary>
	public static float NativeResMult()
	{
		float now = Time.unscaledTime;
		if (resMultCache > 0f && now < resMultNext) return resMultCache;
		resMultNext = now + 0.5f;
		try
		{
			float m = ResourcesManager.ResolutionMult;
			if (m > 0f && !float.IsNaN(m)) { resMultCache = m; return m; }
		}
		catch { }
		resMultCache = ScreenMult();   // 拿不到就按屏幕兜底（仍是自适应，不是钉死 1.0）
		return resMultCache;
	}
	private static float resMultCache = -1f;
	private static float resMultNext = -10f;

	/// <summary>
	/// 刷新 Scale。**每帧调一次即可**（值没变时 SetScale 直接返回，无副作用）。
	/// 返回是否有变化（调用方可据此决定是否重建样式）。
	/// </summary>
	public static bool AutoScale()
	{
		float before = Scale;
		SetScale(NativeResMult());
		return Mathf.Abs(Scale - before) > 0.001f;
	}

	/// <summary>
	/// 把"设计稿上的固定像素宽"收敛进屏幕可用宽度。
	/// 修底部提示条这类硬编码 1400px 的控件——在 1366 宽的屏上会直接溢出到屏幕外。
	/// </summary>
	public static float ScreenFit(float designedW, float margin = 40f)
	{
		float avail = 1920f;
		try { avail = Screen.width - margin; } catch { }
		if (avail <= 0f) avail = designedW;
		return Mathf.Min(designedW * Scale, avail);
	}

	// ══════════ ① 令牌：配色（中性灰黑单色系）══════════
	// 2.5.0 重绘：由"军绿 + 琥珀金"整体换为**中性灰黑**——强调色就是纯白，
	// 靠「明度档 + 虚线节奏 + 形状」做区分，不再用色相区分（用户明确要求"灰黑色的 UI"）。
	// 保留彩色的只有：收藏★(StarOn) / 危险(Danger) / 警告(Warn) / 成功(Success)。
	// 旧军绿预设保留在 Legacy* 一组，供 cfg uiMono=false 回退（过渡期用）。

	/// <summary>
	/// cfg UI/uiMono。true（默认）= 灰黑单色系；false = 回退 2.4.x 的军绿预设。
	/// **只影响下面这一组"结构色"**——世界空间标记(W*)与语义色(集火/警告/收藏)不受影响。
	/// 两个 mod 各自在启动时调一次 SetMono()。
	/// </summary>
	public static bool Mono = true;

	// 结构色的两套预设（Mono / Legacy），下面用属性按 Mono 分派。
	// 2.5.1 提亮：用户反馈"现在的 UI 太黑了"——原 #0C/#1E/#12 在战场亮背景下压成一坨，
	// 且深色面之间几乎没有层次差（#0C→#1E 只差 18 级）。现在整体抬到中深灰，
	// 并把**面与面的明度差拉开**（面板→标题条→控件→行→选中，逐级 +8~14 级），
	// 保持"灰黑"基调不变（仍是无彩色的中性灰，不回到军绿）。
	private static readonly Color MonoPanelBg = new Color(0x1C / 255f, 0x20 / 255f, 0x24 / 255f, 0.93f);
	private static readonly Color MonoPanelBorder = new Color(1f, 1f, 1f, 0.30f);
	private static readonly Color MonoTitleBar = new Color(0x2C / 255f, 0x31 / 255f, 0x37 / 255f, 0.96f);
	private static readonly Color MonoSurface = new Color(0x36 / 255f, 0x3C / 255f, 0x43 / 255f, 0.93f);
	private static readonly Color MonoSurfaceHover = new Color(0x45 / 255f, 0x4C / 255f, 0x55 / 255f, 0.96f);
	private static readonly Color MonoSurfaceActive = new Color(0x5A / 255f, 0x63 / 255f, 0x6E / 255f, 1f);
	private static readonly Color MonoRowBg = new Color(0x2A / 255f, 0x2F / 255f, 0x35 / 255f, 0.90f);
	private static readonly Color MonoFavRow = new Color(1f, 1f, 1f, 0.14f);
	private static readonly Color MonoText = new Color(0xF2 / 255f, 0xF4 / 255f, 0xF6 / 255f, 1f);
	private static readonly Color MonoTextDim = new Color(0xC2 / 255f, 0xC8 / 255f, 0xCE / 255f, 0.92f);
	private static readonly Color MonoTextDisabled = new Color(0x8A / 255f, 0x91 / 255f, 0x99 / 255f, 0.80f);
	private static readonly Color MonoTextOnActive = new Color(1f, 1f, 1f, 1f);
	private static readonly Color MonoTextOnPlate = new Color(0xFA / 255f, 0xFB / 255f, 0xFC / 255f, 1f);
	private static readonly Color MonoStarHot = new Color(0.88f, 0.90f, 0.92f, 0.95f);
	private static readonly Color MonoSurfaceDisabled = new Color(0x10 / 255f, 0x12 / 255f, 0x14 / 255f, 0.62f);
	private static readonly Color MonoRowHover = new Color(1f, 1f, 1f, 0.12f);
	private static readonly Color MonoListBg = new Color(0x08 / 255f, 0x0A / 255f, 0x0C / 255f, 0.32f);

	// —— A 面板结构 ——
	public static Color PanelBg => Mono ? MonoPanelBg : LegacyPanelBg;                        // 面板底
	public static Color PanelBorder => Mono ? MonoPanelBorder : new Color(0.35f, 0.5f, 0.35f, 0.55f); // 面板描边
	public static Color TitleBar => Mono ? MonoTitleBar : new Color(0.06f, 0.11f, 0.06f, 0.95f);     // 标题条
	public static Color ListBg => Mono ? MonoListBg : new Color(0f, 0f, 0f, 0.35f);            // 列表区底
	public static Color Surface => Mono ? MonoSurface : LegacySurface;                        // 控件底
	public static Color SurfaceHover => Mono ? MonoSurfaceHover : new Color(0.2f, 0.32f, 0.2f, 0.95f); // 悬停底
	public static Color SurfaceActive => Mono ? MonoSurfaceActive : LegacySurfaceActive;      // 选中底
	public static Color SurfaceDisabled => Mono ? MonoSurfaceDisabled : new Color(0f, 0f, 0f, 0.55f); // 禁用底
	public static Color RowBg => Mono ? MonoRowBg : LegacyRowBg;                              // 普通行底
	public static Color RowHover => Mono ? MonoRowHover : new Color(0.5f, 0.7f, 0.5f, 0.12f); // 行悬停覆盖
	public static Color SurfaceRow => Surface;                                                // 兼容旧名（列表行底）
	public static Color FavRow => Mono ? MonoFavRow : LegacyFavRow;                           // 收藏行底

	// —— B 文字 ——
	public static Color Text => Mono ? MonoText : LegacyText;                                 // 正文
	public static Color TextOnActive => Mono ? MonoTextOnActive : new Color(0.04f, 0.09f, 0.04f, 1f); // 选中态文字
	public static Color TextDim => Mono ? MonoTextDim : LegacyTextDim;                        // 次要文字
	public static Color TextDisabled => Mono ? MonoTextDisabled : new Color(0.55f, 0.65f, 0.55f, 0.65f); // 禁用文字
	public static Color TextOnPlate => Mono ? MonoTextOnPlate : new Color(0.9f, 0.96f, 0.9f, 1f);      // 底板上的字

	// —— C 指示 ——
	public static Color Accent => new Color(1f, 1f, 1f, 1f);                                  // 强调（灰黑主题里=纯白）
	public static Color Warn => new Color(0xE0 / 255f, 0xA3 / 255f, 0x3A / 255f, 0.98f);      // 警告（暂停等）
	public static Color Danger => new Color(0xC4 / 255f, 0x45 / 255f, 0x3C / 255f, 0.95f);    // 危险（集火）
	public static Color Success => new Color(0x6F / 255f, 0xA8 / 255f, 0x60 / 255f, 0.95f);   // 成功
	public static Color Scrim => new Color(0f, 0f, 0f, 0.50f);                                // 遮罩
	public static Color StarOn => new Color(1f, 0xD4 / 255f, 0x5E / 255f, 1f);                // ★
	public static Color StarHot => Mono ? MonoStarHot : LegacyStarHot;                        // ☆ 悬停

	// ══════════ ① 令牌：世界空间标记（3D 层）══════════
	// 全部白/半透灰，靠**不透明度档位**区分语义（用户："标记点等都用白色或半透明的灰色"）：
	//   友军脚环最淡(.28/.34) → 路线(.26) → 登车线(.48) → 阵型/载具(.60/.80)
	//   → 选中(.92) → 移动目标(.85) —— 越"当前正在操作"的越亮。
	// 集火(Focus)保留红，仅在 markerColorMode=Semantic 时生效；Mono 模式下也走白灰。
	public static Color WFriendly => new Color(1f, 1f, 1f, 0.28f);        // 友军脚环（步兵）
	public static Color WFriendlyVeh => new Color(1f, 1f, 1f, 0.34f);     // 友军脚环（载具）
	public static Color WSelected => new Color(1f, 1f, 1f, 0.92f);        // 选中角标
	public static Color WSelectedDot => new Color(1f, 1f, 1f, 0.95f);     // 选中中心点
	public static Color WFocus => new Color(0xC4 / 255f, 0x45 / 255f, 0x3C / 255f, 0.92f);  // 集火（红）
	public static Color WFocusDown => new Color(0xE0 / 255f, 0xA3 / 255f, 0x3A / 255f, 0.80f); // 集火·降级（橙）
	public static Color WMove => new Color(1f, 1f, 1f, 0.85f);            // 移动目标点
	public static Color WPath => new Color(1f, 1f, 1f, 0.26f);            // 行进路线（长虚线 8-5，暗）
	public static Color WBoard => new Color(1f, 1f, 1f, 0.48f);           // 登车线（短虚线 4-4，亮）
	public static Color WFormation => new Color(1f, 1f, 1f, 0.80f);       // 阵型拖动标记
	public static Color WVehicle => new Color(1f, 1f, 1f, 0.60f);         // 载具专用标记
	public static Color WGhost => new Color(0xC8 / 255f, 0xC8 / 255f, 0xC8 / 255f, 0.35f);  // 幽灵预览
	public static Color WLabelPlate => new Color(0x0C / 255f, 0x0C / 255f, 0x0C / 255f, 0.90f); // 名签底板

	/// <summary>
	/// 2.5.0：世界空间标记的颜色来源。默认 = 上面的灰黑令牌；
	/// 若 cfg <c>markerColorMode=Semantic</c>，把集火/降级恢复为红/橙语义色（其它仍是白灰）。
	/// </summary>
	public static bool MarkerSemantic = false;

	public static Color MarkerFocus => MarkerSemantic ? WFocus : WSelected;
	public static Color MarkerFocusDown => MarkerSemantic ? WFocusDown : new Color(0.82f, 0.82f, 0.82f, 0.85f);

	// ══════════ ① 令牌：旧军绿预设（cfg uiMono=false 回退用）══════════
	public static Color LegacyPanelBg => new Color(0.02f, 0.05f, 0.02f, 0.88f);
	public static Color LegacySurface => new Color(0x0E / 255f, 0x1C / 255f, 0x0E / 255f, 0xB4 / 255f);
	public static Color LegacySurfaceActive => new Color(0x3E / 255f, 0x70 / 255f, 0x3E / 255f, 0xE0 / 255f);
	public static Color LegacyText => new Color(0xDF / 255f, 0xF0 / 255f, 0xDF / 255f, 1f);
	public static Color LegacyTextDim => new Color(0.55f, 0.65f, 0.55f, 0.75f);
	public static Color LegacyRowBg => new Color(0.12f, 0.2f, 0.12f, 0.85f);
	public static Color LegacyFavRow => new Color(0.4f, 0.58f, 0.4f, 0.95f);
	public static Color LegacyStarHot => new Color(0.8f, 0.9f, 0.8f, 0.95f);

	// ══════════ ① 令牌：世界空间绘制参数（2.5.0）══════════

	/// <summary>脉动幅度（cfg markerPulse 关掉时由调用方传 0）。基准 = 1 ± 0.07*sin(t*5)。</summary>
	public const float PulseAmp = 0.07f;
	public const float PulseSpeed = 5f;

	/// <summary>
	/// 按 key 哈希错开脉动相位——修 2.5.0 前"4 个标记共用同一个全局 pulse，
	/// 同频同相一起呼吸"的机械感（用户截图里选中/集火/目标点同步闪烁像在闪灯）。
	/// hash 用 FNV-1a，稳定且无分配。
	/// </summary>
	public static float Pulse(string key, float t, float amp = PulseAmp, float speed = PulseSpeed)
	{
		if (amp <= 0f) return 1f;
		unchecked
		{
			int h = (int)2166136261u;
			if (!string.IsNullOrEmpty(key))
				for (int i = 0; i < key.Length; i++) { h ^= key[i]; h *= 16777619; }
			float phase = (h & 0xFFFF) / 65536f * Mathf.PI * 2f;   // [0, 2π)
			return 1f + amp * Mathf.Sin(t * speed + phase);
		}
	}

	/// <summary>
	/// 线宽的世界单位补偿：LineRenderer.widthMultiplier 是**世界单位**（C11），
	/// 远距离会细成一丝。按相机距离放大，夹在 [0.6, 2.5] 倍（近处不糊、远处可见）。
	/// dist/30 的 30 是经验值：ER2 战场常见接敌距离 ~30m 时为 1.0 倍基准。
	/// </summary>
	public static float WidthScale(float camDist)
	{
		if (camDist <= 0f) return 1f;
		return Mathf.Clamp(camDist / 30f, 0.6f, 2.5f);
	}

	/// <summary>cfg UI/uiMono → Mono。启动与 <c>SettingChanged</c> 时各调一次。</summary>
	public static void SetMono(bool mono) { Mono = mono; }

	/// <summary>
	/// cfg Markers/markerColorMode（"Mono" / "Semantic"）→ MarkerSemantic。
	/// 大小写不敏感；无法识别时保守取 Mono（灰黑是用户明确要的默认观感）。
	/// </summary>
	public static void SetMarkerColorMode(string mode)
	{
		MarkerSemantic = !string.IsNullOrEmpty(mode) &&
			mode.Trim().Equals("Semantic", System.StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>线宽补偿总入口：把调用点的原始线宽 + 相机距离揉成最终 widthMultiplier。</summary>
	public static float LineWidth(float baseWidth, float camDist)
	{
		return baseWidth * WidthScale(camDist);
	}

	// ══════════ ② 资源：按色缓存的纯色贴图 ══════════
	// 单槽缓存会把别的样式一起换掉（17g3），故按颜色建字典；
	// hideFlags=61 是陷阱 12 的定案值（不设 → 场景切换后背景变白/消失）。
	private static readonly Dictionary<int, Texture2D> solidCache = new();

	public static Texture2D Solid(Color c)
	{
		Color32 b = c;
		int key = (b.r << 24) | (b.g << 16) | (b.b << 8) | b.a;
		if (solidCache.TryGetValue(key, out Texture2D t) && t != null) return t;

		Texture2D tex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
		Color[] px = new Color[16];
		for (int i = 0; i < px.Length; i++) px[i] = c;
		tex.SetPixels(px);
		tex.Apply();
		tex.hideFlags = (HideFlags)61;
		solidCache[key] = tex;
		return tex;
	}

	/// <summary>实心矩形（面板底/列表底/分隔）。内部保证 GUI.color 复位。</summary>
	public static void Fill(Rect r, Color c)
	{
		Color keep = GUI.color;
		GUI.color = c;
		GUI.DrawTexture(r, Texture2D.whiteTexture);
		GUI.color = keep;
	}

	// ══════════ ③ 样式工厂 ══════════

	/// <summary>纯文字样式（Label 用）。font 传 null = 用 IMGUI 默认字体。</summary>
	public static GUIStyle MakeLabel(int size, TextAnchor align, Color fg, FontStyle fs = FontStyle.Normal, Font font = null)
	{
		GUIStyle s = new GUIStyle();
		if (font != null) s.font = font;
		s.fontSize = size;
		s.fontStyle = fs;
		s.alignment = align;
		s.normal.textColor = fg;   // 陷阱：默认黑
		return s;
	}

	/// <summary>纯色按钮样式（normal/hover/active/focused 同底色与同字色，避免"悬停变色"的廉价感）。</summary>
	public static GUIStyle MakeButton(int size, Color bg, Color fg, FontStyle fs = FontStyle.Normal,
		TextAnchor align = TextAnchor.MiddleCenter, Font font = null)
	{
		GUIStyle s = MakeLabel(size, align, fg, fs, font);
		Texture2D t = Solid(bg);
		s.normal.background = t;
		s.hover.background = t;
		s.active.background = t;
		s.focused.background = t;
		return s;
	}

	// ══════════ ④ 控件原语 ══════════

	/// <summary>
	/// 定宽页签网格：**字号自适应**（陷阱 76——IMGUI 文字不裁剪，长标签会压住邻居）。
	/// 返回被点击的索引，-1 = 无。
	/// </summary>
	public static int TabGrid(float x, float y, float w, IList<string> labels, int sel, int perRow,
		float tabH, GUIStyle normal, GUIStyle active)
	{
		if (labels == null || labels.Count == 0) return -1;
		float cw = (w - (perRow - 1) * Gap) / perRow;
		int clicked = -1;
		for (int i = 0; i < labels.Count; i++)
		{
			int row = i / perRow, col = i % perRow;
			Rect r = new Rect(x + col * (cw + Gap), y + row * (tabH + Gap), cw, tabH);
			bool on = i == sel;
			GUIStyle st = on ? active : normal;
			// ⚠️ 只改专用样式实例的字号（这两个样式归页签专用，别处不得复用）
			st.fontSize = FitSize(st, labels[i], cw - 6f, FontTabMax, FontTabMin);
			if (GUI.Button(r, labels[i], st)) clicked = i;
		}
		return clicked;
	}

	/// <summary>页签网格占的高度（count=0 → 0）。**必须与 TabGrid 用同一 perRow/tabH**。</summary>
	public static float TabGridH(int count, int perRow, float tabH)
	{
		if (count <= 0) return 0f;
		return Mathf.CeilToInt(count / (float)perRow) * (tabH + Gap);
	}

	/// <summary>
	/// 分页行（◀ 页码 ▶；Shift 点击一次跳 10 页）。返回**页码增量**（调用方负责夹取），0 = 无操作。
	/// </summary>
	public static int Pager(float x, float y, float w, int pages, string mid, GUIStyle btn, GUIStyle label)
	{
		if (pages <= 1) return 0;
		bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		int step = shift ? 10 : 1;
		if (GUI.Button(new Rect(x, y, 30f, BtnH), "◀", btn)) return -step;
		GUI.Label(new Rect(x + 38f, y + 2f, w - 76f, 20f), mid, label);
		if (GUI.Button(new Rect(x + w - 30f, y, 30f, BtnH), "▶", btn)) return step;
		return 0;
	}

	// ══════════ ⑤ 文本自适应 ══════════

	private static readonly Dictionary<string, int> fitCache = new();

	/// <summary>
	/// 让 text 在 maxW 内放得下的最大字号（CalcSize 精确测量）。
	/// 结果按 (宽度档位+字号区间+文本) 缓存——每帧对每个控件调 CalcSize 太贵，且有 GUIContent 分配。
	/// </summary>
	public static int FitSize(GUIStyle st, string text, float maxW, int max, int min)
	{
		bool fits;
		return FitSize(st, text, maxW, max, min, out fits);
	}

	/// <summary>同上，并告诉你"最小号是否也放不下"（调用方据此决定要不要改文案/截断）。</summary>
	public static int FitSize(GUIStyle st, string text, float maxW, int max, int min, out bool fits)
	{
		fits = true;
		if (st == null || string.IsNullOrEmpty(text)) return max;

		int w = Mathf.RoundToInt(maxW);
		string key = w + "|" + max + "|" + min + "|" + text;
		if (fitCache.TryGetValue(key, out int cached))
		{
			fits = cached > 0;                 // 缓存里正数=放得下（值即字号），负数=放不下
			return fits ? cached : -cached;
		}

		int size = max;
		bool ok = false;
		int keep = st.fontSize;
		try
		{
			for (int sz = max; sz >= min; sz--)
			{
				size = sz;
				st.fontSize = sz;
				if (st.CalcSize(new GUIContent(text)).x <= maxW) { ok = true; break; }
			}
		}
		catch { size = max; ok = true; }
		finally { st.fontSize = keep; }   // 还原：样式是共享实例，测量不能留下副作用

		if (fitCache.Count > 4000) fitCache.Clear();   // 兜底防无限增长（物品名上千条）
		fitCache[key] = ok ? size : -size;
		fits = ok;
		return size;
	}
}
