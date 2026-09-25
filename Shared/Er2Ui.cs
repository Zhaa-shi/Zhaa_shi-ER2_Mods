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
	// 1.4.24 **中性半透明黑**（用户："把面板UI改成半透明黑色，不要棕色了"）：
	//   ① **去掉色相**——R=G=B（纯中性灰黑），不再带暖棕。棕色是上一轮为了"协调泥土背景"
	//      刻意加的，用户明确否掉。
	//   ② **不透明度改由 cfg 驱动**：`PanelAlpha`（cfg UI/uiPanelAlpha，默认 0.85）。
	//      半透明与"够黑"是一对矛盾轴——α 越低越透但越容易被地形染色（陷阱 96），
	//      与其替玩家猜，不如把这一轴暴露成配置项，默认取折中的 0.85。
	//   ③ 皮革纹理保留（用户上轮要的质感），但**去掉暖调**改中性灰——
	//      在中性黑底上留暖色会重新泛黄。
	//   元素区分靠三重：中性黑底 + 中性描边/分隔线 + 选中亮灰填充。
	private static readonly Color MonoPanelBg = new Color(0x08 / 255f, 0x08 / 255f, 0x0A / 255f);
	private static readonly Color MonoPanelBorder = new Color(1f, 1f, 1f, 0.85f);
	private static readonly Color MonoTitleBar = new Color(0x10 / 255f, 0x10 / 255f, 0x13 / 255f);
	private static readonly Color MonoSurface = new Color(0x18 / 255f, 0x18 / 255f, 0x1C / 255f);
	private static readonly Color MonoSurfaceHover = new Color(0x32 / 255f, 0x32 / 255f, 0x3A / 255f);
	private static readonly Color MonoSurfaceActive = new Color(0x52 / 255f, 0x52 / 255f, 0x5E / 255f);
	private static readonly Color MonoRowBg = new Color(0x0E / 255f, 0x0E / 255f, 0x11 / 255f);
	private static readonly Color MonoFavRow = new Color(1f, 1f, 1f, 0.10f);
	private static readonly Color MonoText = new Color(1f, 1f, 1f, 1f);
	private static readonly Color MonoTextDim = new Color(0xE4 / 255f, 0xE4 / 255f, 0xE8 / 255f, 1f);
	private static readonly Color MonoTextDisabled = new Color(0xA8 / 255f, 0xA8 / 255f, 0xB0 / 255f, 0.90f);
	private static readonly Color MonoTextOnActive = new Color(1f, 1f, 1f, 1f);
	private static readonly Color MonoTextOnPlate = new Color(0xF5 / 255f, 0xF5 / 255f, 0xF7 / 255f, 1f);
	private static readonly Color MonoStarHot = new Color(0.90f, 0.90f, 0.92f, 0.95f);
	private static readonly Color MonoSurfaceDisabled = new Color(0x10 / 255f, 0x10 / 255f, 0x12 / 255f, 0.62f);
	private static readonly Color MonoRowHover = new Color(1f, 1f, 1f, 0.10f);
	private static readonly Color MonoListBg = new Color(0f, 0f, 0f, 0.50f);
	// 1.4.24：结构线改中性灰——中性黑底上深棕会显脏
	private static readonly Color MonoEdge = new Color(1f, 1f, 1f, 0.65f);   // 分隔线（白，不透明域）
	private static readonly Color MonoEdgeSoft = new Color(1f, 1f, 1f, 0.30f);                    // 行间分隔（白 30%）

	/// <summary>
	/// cfg UI/uiPanelAlpha → 面板**主不透明度**（0.55~1.0）。默认 0.85。
	/// 这是"透 ↔ 黑"那条矛盾轴：α 越低越能透出战场，但越容易被地形颜色染色（陷阱 96）。
	/// 各结构色的相对层次（标题条更实、行底更透）由下面属性按比例推出，玩家只调一个值。
	/// </summary>
	public static float PanelAlpha = 0.85f;

	public static void SetPanelAlpha(float v)
	{
		if (float.IsNaN(v)) return;
		PanelAlpha = Mathf.Clamp(v, 0.55f, 1f);
	}

	private static Color WithA(Color c, float a) => new Color(c.r, c.g, c.b, Mathf.Clamp01(a));

	// —— A 面板结构（1.4.24：Mono 分支的 α 由 `PanelAlpha` 推出，玩家一个 cfg 控全局透明度）——
	public static Color PanelBg => Mono ? WithA(MonoPanelBg, PanelAlpha) : LegacyPanelBg;                        // 面板底
	// 1.4.27：**线条/文字是前景，不参与 PanelAlpha**（用户："我说透明只是指背景透明"）。
	// 上一版把描边 α 也绑到 PanelAlpha 上，等于连边框一起做成了半透明——对比度就是这样丢的。
	public static Color PanelBorder => Mono ? MonoPanelBorder : new Color(0.35f, 0.5f, 0.35f, 0.55f); // 面板描边
	public static Color TitleBar => Mono ? WithA(MonoTitleBar, PanelAlpha + 0.05f) : new Color(0.06f, 0.11f, 0.06f, 0.95f);     // 标题条
	public static Color ListBg => Mono ? MonoListBg : new Color(0f, 0f, 0f, 0.35f);            // 列表区底（内凹，不随 PanelAlpha）
	public static Color Surface => Mono ? WithA(MonoSurface, PanelAlpha) : LegacySurface;                        // 控件底
	public static Color SurfaceHover => Mono ? WithA(MonoSurfaceHover, PanelAlpha + 0.07f) : new Color(0.2f, 0.32f, 0.2f, 0.95f); // 悬停底
	public static Color SurfaceActive => Mono ? WithA(MonoSurfaceActive, PanelAlpha + 0.13f) : LegacySurfaceActive;      // 选中底
	public static Color SurfaceDisabled => Mono ? MonoSurfaceDisabled : new Color(0f, 0f, 0f, 0.55f); // 禁用底
	public static Color RowBg => Mono ? WithA(MonoRowBg, PanelAlpha - 0.05f) : LegacyRowBg;                              // 普通行底
	public static Color RowHover => Mono ? MonoRowHover : new Color(0.5f, 0.7f, 0.5f, 0.12f); // 行悬停覆盖
	public static Color SurfaceRow => Surface;                                                // 兼容旧名（列表行底）
	public static Color FavRow => Mono ? MonoFavRow : LegacyFavRow;                           // 收藏行底

	// —— A2 结构线（2.5.2 新增：设计感靠"面 + 线 + 条"三维，不再只靠明度差）——
	/// <summary>分隔线（标题条下、列表上下、面板内分区）。</summary>
	public static Color Edge => Mono ? MonoEdge : new Color(0.35f, 0.5f, 0.35f, 0.55f);
	/// <summary>极淡分隔（列表行之间）——只在需要"分而不抢"时用。</summary>
	public static Color EdgeSoft => Mono ? MonoEdgeSoft : new Color(0.5f, 0.7f, 0.5f, 0.10f);

	// —— B 文字 ——
	public static Color Text => Mono ? MonoText : LegacyText;                                 // 正文
	public static Color TextOnActive => Mono ? MonoTextOnActive : new Color(0.04f, 0.09f, 0.04f, 1f); // 选中态文字
	public static Color TextDim => Mono ? MonoTextDim : LegacyTextDim;                        // 次要文字
	public static Color TextDisabled => Mono ? MonoTextDisabled : new Color(0.55f, 0.65f, 0.55f, 0.65f); // 禁用文字
	public static Color TextOnPlate => Mono ? MonoTextOnPlate : new Color(0.9f, 0.96f, 0.9f, 1f);      // 底板上的字

	// —— C 指示 ——
	public static Color Accent => new Color(0xF5 / 255f, 0xF5 / 255f, 0xF7 / 255f, 1f);        // 强调（1.4.24 中性主题里=近纯白）
	public static Color Warn => new Color(0xE0 / 255f, 0xA3 / 255f, 0x3A / 255f, 0.98f);      // 警告（暂停等）
	public static Color Danger => new Color(0xC4 / 255f, 0x45 / 255f, 0x3C / 255f, 0.95f);    // 危险（集火）
	public static Color Success => new Color(0x6F / 255f, 0xA8 / 255f, 0x60 / 255f, 0.95f);   // 成功
	public static Color Scrim => new Color(0.02f, 0.02f, 0.025f, 0.72f);                       // 遮罩（1.4.24 中性黑半透，提示条/徽标底）
	public static Color StarOn => new Color(1f, 0xD8 / 255f, 0x00 / 255f, 1f);                // ★（1.4.27 提亮：纯正金黄）
	public static Color StarHot => Mono ? MonoStarHot : LegacyStarHot;                        // ☆ 悬停

	// ══════════ ① 令牌：世界空间标记（3D 层）══════════
	// 全部白/半透灰，靠**不透明度档位**区分语义（用户："标记点等都用白色或半透明的灰色"）：
	//   友军脚环最淡(.28/.34) → 路线(.26) → 登车线(.48) → 阵型/载具(.60/.80)
	//   → 选中(.92) → 移动目标(.85) —— 越"当前正在操作"的越亮。
	// 集火(Focus)保留红，仅在 markerColorMode=Semantic 时生效；Mono 模式下也走白灰。
	// 2.5.2：**半透明灰换成实色灰阶**（用户："半透明灰色质感不好"）。
	// 原因：α 0.26~0.48 的线叠在草地/雪地/沙地上会被背景"吃掉"，颜色随地面漂移，
	// 看起来发灰发脏、边界发虚——这是半透明在 3D 场景里的通病，不是配色问题。
	// 改法：**把层次从 α 移到灰度值**（灰阶实色，α ≥ 0.80），线是实打实的，质感立刻变硬：
	//   中灰 #8E/9A（背景信息：友军/路线）→ 亮灰 #C6（登车/载具）→ 近白 #E2/#F2（阵型/移动）
	//   → 纯白 #FFF（选中）。仍是"白/灰"体系，不回彩色。
	// 唯一保留半透明的是**幽灵预览**（那是模型本体，需要透出地面）。
	public static Color WFriendly => new Color(0x8E / 255f, 0x95 / 255f, 0x9C / 255f, 0.85f);  // 友军脚环（步兵）
	public static Color WFriendlyVeh => new Color(0xA2 / 255f, 0xA9 / 255f, 0xB0 / 255f, 0.88f); // 友军脚环（载具）
	public static Color WSelected => new Color(1f, 1f, 1f, 0.95f);                              // 选中角标
	public static Color WSelectedDot => new Color(1f, 1f, 1f, 1f);                              // 选中中心点
	public static Color WFocus => new Color(0xC4 / 255f, 0x45 / 255f, 0x3C / 255f, 0.95f);      // 集火（红）
	public static Color WFocusDown => new Color(0xE0 / 255f, 0xA3 / 255f, 0x3A / 255f, 0.90f);  // 集火·降级（橙）
	public static Color WMove => new Color(0xF2 / 255f, 0xF5 / 255f, 0xF7 / 255f, 0.92f);       // 移动目标点
	public static Color WPath => new Color(0x9A / 255f, 0xA1 / 255f, 0xA8 / 255f, 0.80f);       // 行进路线（长虚线，暗）
	public static Color WBoard => new Color(0xC6 / 255f, 0xCB / 255f, 0xD0 / 255f, 0.88f);      // 登车线（短虚线，亮）
	public static Color WFormation => new Color(0xE2 / 255f, 0xE6 / 255f, 0xEA / 255f, 0.90f);  // 阵型拖动标记
	public static Color WVehicle => new Color(0xCB / 255f, 0xCF / 255f, 0xD4 / 255f, 0.85f);    // 载具专用标记
	public static Color WGhost => new Color(0xB8 / 255f, 0xBC / 255f, 0xC0 / 255f, 0.32f);      // 幽灵预览（保留半透）
	public static Color WLabelPlate => new Color(0x0A / 255f, 0x0C / 255f, 0x0E / 255f, 0.92f); // 名签底板

	/// <summary>
	/// 2.5.0：世界空间标记的颜色来源。默认 = 上面的灰黑令牌；
	/// 若 cfg <c>markerColorMode=Semantic</c>，把集火/降级恢复为红/橙语义色（其它仍是白灰）。
	/// </summary>
	public static bool MarkerSemantic = false;

	public static Color MarkerFocus => MarkerSemantic ? WFocus : WSelected;
	public static Color MarkerFocusDown => MarkerSemantic ? WFocusDown : new Color(0xB4 / 255f, 0xB9 / 255f, 0xBE / 255f, 0.92f);

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

	// ══════════ 线宽：固定世界单位（1.4.26）══════════
	// 沿革（三段，都是被实测推着走的，别再回头）：
	//   1.4.21 用"经验倍率"补偿 → 与分辨率强耦合（1440p 粗 1.33×、4K 粗 2×）；
	//   1.4.22 改"距离取错"（position.magnitude 是到世界原点的距离）→ 线宽爆表；
	//   1.4.24 改"1080p 目标像素宽"（width_m ∝ dist）→ 屏幕像素恒定，跨分辨率一致。
	//   **1.4.26 定案：回到固定世界单位**——因为像素恒定与标记本身的尺度规则冲突：
	//   标记半径 `UnitRingRadius` 是**固定世界值**（从 Collider 量出，clamp 0.35~1.1m），
	//   圆环天然"近大远小"；而像素恒定让**线宽的世界值 ∝ 距离**，远处线宽被放大到逼近环半径
	//   （60m 处 1.5px ≈ 0.10m，200m 处 ≈ 0.34m vs 环半径 0.6m）→ **环被糊成实心大圆盘**。
	//   用户原话："这个标记近小圆大"——精确描述了这个现象。
	//   定案：线宽也用固定世界米，与环一起近大远小，比例恒定、透视自然。
	//   代价：极远处线会细到亚像素（那就是真实透视的表现），不再做人为补偿。

	/// <summary>
	/// 线宽总入口（1.4.26）：<paramref name="baseMeters"/> = **世界空间线宽（米）**，
	/// 与标记半径同一尺度规则 → 近大远小，比例与视距无关。
	/// 层次建议（米）：脚环 0.032 / 虚线 0.030~0.038 / 角标 0.042~0.060 / 强调环 0.065。
	/// <paramref name="camDist"/> 保留在签名里（13 个调用点不必逐处改），当前**不参与换算**——
	/// 这是有意的：线宽回到世界空间后，距离不再应该是它的输入。
	/// </summary>
	public static float LineWidth(float baseMeters, float camDist)
	{
		return Mathf.Clamp(baseMeters, 0.003f, 3f);
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

	/// <summary>
	/// 2.5.2：**矩形描边**（四边）。IMGUI 没有边框 API，只能四条 Fill 拼。
	/// 这是"设计感"的主要来源——此前所有面都是纯色块、没有任何边框，
	/// 在亮背景战场上一压就糊成一片（用户："只有单一的色块没有设计感"）。
	/// </summary>
	public static void Frame(Rect r, Color c, float t)
	{
		Fill(new Rect(r.x, r.y, r.width, t), c);
		Fill(new Rect(r.x, r.yMax - t, r.width, t), c);
		Fill(new Rect(r.x, r.y, t, r.height), c);
		Fill(new Rect(r.xMax - t, r.y, t, r.height), c);
	}

	/// <summary>水平分隔线（标题条下、分区之间）。</summary>
	public static void HLine(Rect r, Color c) => Fill(r, c);

	/// <summary>
	/// 1.4.23：**皮革纹理**（64×64 程序化噪声，确定性）。
	/// 为什么需要：IMGUI 只有纯色填充，面板再怎么调色也只是"一块色板"。
	/// 皮革的观感来自**细微的斑驳明暗**，一层低透明度的噪声叠加即可获得。
	/// 用固定种子的 value noise（两层：0.09 的皱褶 + 0.37 的细粒），
	/// 刻意做成偏暗（0.34~0.66）——叠上去只压暗不泛灰。
	/// hideFlags=61 是陷阱 12 的定案值。
	/// </summary>
	private static Texture2D leatherTex;

	public static Texture2D LeatherTex()
	{
		if (leatherTex != null) return leatherTex;
		const int S = 64;
		leatherTex = new Texture2D(S, S, TextureFormat.ARGB32, false);
		leatherTex.hideFlags = (HideFlags)61;
		leatherTex.wrapMode = TextureWrapMode.Repeat;   // 平铺
		leatherTex.filterMode = FilterMode.Bilinear;
		var px = new Color[S * S];
		for (int y = 0; y < S; y++)
		{
			for (int x = 0; x < S; x++)
			{
				float n1 = Mathf.PerlinNoise(x * 0.09f, y * 0.09f);   // 皮面大皱褶
				float n2 = Mathf.PerlinNoise(x * 0.37f + 31.7f, y * 0.37f + 11.3f); // 细粒
				float v = Mathf.Clamp01(0.5f + (n1 - 0.5f) * 0.62f + (n2 - 0.5f) * 0.28f);
				float g = Mathf.Lerp(0.34f, 0.66f, v);
				px[y * S + x] = new Color(g, g, g, 1f);   // 1.4.24：中性灰（去暖调，配中性黑底）
			}
		}
		leatherTex.SetPixels(px);
		leatherTex.Apply(false, true);
		return leatherTex;
	}

	/// <summary>把皮革纹理**平铺**铺满一个矩形（低透明度叠加，只做质感不做主角）。</summary>
	public static void Leather(Rect r, float alpha = 0.10f)
	{
		if (r.width <= 0f || r.height <= 0f) return;
		try
		{
			Texture2D t = LeatherTex();
			if (t == null) return;
			Color keep = GUI.color;
			GUI.color = new Color(1f, 1f, 1f, alpha);
			GUI.DrawTextureWithTexCoords(r, t, new Rect(0f, 0f, r.width / t.width, r.height / t.height));
			GUI.color = keep;
		}
		catch { }
	}

	/// <summary>
	/// 1.4.23：**标准面板底** = 近黑底 + 皮革纹理 + 顶部内高光。
	/// 顶端那条 1px 亮度是皮革的"受光边缘"——它让面板看起来是**一块有厚度的皮**，
	/// 而不是一个纯色矩形（这是"皮革感"的第二个来源，第一个是噪声纹理）。
	/// </summary>
	public static void PanelBase(Rect r, float leatherAlpha = 0.10f)
	{
		Fill(r, PanelBg);
		Leather(r, leatherAlpha);
		HLine(new Rect(r.x, r.y, r.width, Mathf.Max(1f, Scale)), EdgeSoft);   // 受光边缘
	}

	/// <summary>
	/// 左侧（或指定边）强调竖条——选中行的"激活"指示。
	/// 比"整行换底色"更像设计：底色只轻微提亮，靠这条竖线点名当前项。
	/// </summary>
	public static void AccentBar(Rect r, Color c, float w) => Fill(new Rect(r.x, r.y, w, r.height), c);

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
			// 1.4.22：内缩随 Scale（19px = 左右各留 ~6.5px 呼吸位 + 描边位），长标签不再贴边
			st.fontSize = FitSize(st, labels[i], cw - 19f * Scale, FontTabMax, FontTabMin);
			if (GUI.Button(r, labels[i], st)) clicked = i;
			// 1.4.22：**每个页签都描边**——未选中用暖棕 Edge、选中用暖白 Accent。
			// 用户反馈"UI 各元素区分不明显"：此前页签只有填充色差，在亮背景上读不出边界。
			Frame(r, on ? Accent : Edge, Mathf.Max(1f, Scale));
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
		// 1.4.22：全部 × Scale（原 30/38/76/20 硬编码 → UI 缩放后箭头与文字错位）
		float pw = 30f * Scale, pgap = 8f * Scale;
		if (GUI.Button(new Rect(x, y, pw, BtnH), "◀", btn)) return -step;
		GUI.Label(new Rect(x + pw + pgap, y + 2f * Scale, w - (pw + pgap) * 2f, 20f * Scale), mid, label);
		if (GUI.Button(new Rect(x + w - pw, y, pw, BtnH), "▶", btn)) return step;
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
