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
	// ══════════ ① 令牌：尺寸 ══════════
	// （数值沿用现行外观，重绘时才改这里——一处改，两个 mod 同时生效）
	public const float Pad = 10f;      // 面板内边距
	public const float Gap = 4f;       // 控件间距（页签/行）
	public const float RowH = 28f;     // 列表行高
	public const float TabH = 24f;     // 页签高
	public const float BtnH = 22f;     // 小按钮（分页箭头等）
	public const float PanelW = 320f;  // 生成面板宽（宿主侧面板宽度自定，不用此值）

	// ══════════ ① 令牌：字号 ══════════
	public const int FontTitle = 15;
	public const int FontBody = 12;
	public const int FontSmall = 10;
	public const int FontTabMax = 12;  // 页签自适应上限
	public const int FontTabMin = 8;   // 页签自适应下限（再小就不可读）

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
	private static readonly Color MonoPanelBg = new Color(0x0C / 255f, 0x0C / 255f, 0x0C / 255f, 0.94f);
	private static readonly Color MonoPanelBorder = new Color(1f, 1f, 1f, 0.18f);
	private static readonly Color MonoTitleBar = new Color(0x14 / 255f, 0x14 / 255f, 0x14 / 255f, 0.96f);
	private static readonly Color MonoSurface = new Color(0x1E / 255f, 0x1E / 255f, 0x1E / 255f, 0.90f);
	private static readonly Color MonoSurfaceHover = new Color(0x2A / 255f, 0x2A / 255f, 0x2A / 255f, 0.95f);
	private static readonly Color MonoSurfaceActive = new Color(0x3A / 255f, 0x3A / 255f, 0x3A / 255f, 1f);
	private static readonly Color MonoRowBg = new Color(0x12 / 255f, 0x12 / 255f, 0x12 / 255f, 0.86f);
	private static readonly Color MonoFavRow = new Color(1f, 1f, 1f, 0.10f);
	private static readonly Color MonoText = new Color(0xE8 / 255f, 0xE8 / 255f, 0xE8 / 255f, 1f);
	private static readonly Color MonoTextDim = new Color(0xA0 / 255f, 0xA0 / 255f, 0xA0 / 255f, 0.90f);
	private static readonly Color MonoTextDisabled = new Color(0x5A / 255f, 0x5A / 255f, 0x5A / 255f, 0.70f);
	private static readonly Color MonoTextOnActive = new Color(1f, 1f, 1f, 1f);
	private static readonly Color MonoTextOnPlate = new Color(0xF2 / 255f, 0xF2 / 255f, 0xF2 / 255f, 1f);
	private static readonly Color MonoStarHot = new Color(0.78f, 0.78f, 0.78f, 0.95f);
	private static readonly Color MonoSurfaceDisabled = new Color(0f, 0f, 0f, 0.55f);
	private static readonly Color MonoRowHover = new Color(1f, 1f, 1f, 0.08f);
	private static readonly Color MonoListBg = new Color(0f, 0f, 0f, 0.40f);

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
