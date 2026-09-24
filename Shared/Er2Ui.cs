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

	// ══════════ ① 令牌：配色（军事深色 HUD）══════════
	public static Color PanelBg => new Color(0.02f, 0.05f, 0.02f, 0.88f);        // 面板底
	public static Color ListBg => new Color(0f, 0f, 0f, 0.35f);                   // 列表区底
	public static Color Surface => new Color(0x0E / 255f, 0x1C / 255f, 0x0E / 255f, 0xB4 / 255f);        // 控件底
	public static Color SurfaceRow => new Color(0x0E / 255f, 0x1C / 255f, 0x0E / 255f, 0.82f);           // 列表行底（比控件底略透）
	public static Color SurfaceActive => new Color(0x3E / 255f, 0x70 / 255f, 0x3E / 255f, 0xE0 / 255f);  // 选中底
	public static Color Text => new Color(0xDF / 255f, 0xF0 / 255f, 0xDF / 255f, 1f);                    // 正文
	public static Color TextOnActive => new Color(0.04f, 0.09f, 0.04f, 1f);                              // 选中态文字（深）
	public static Color TextDim => new Color(0.55f, 0.65f, 0.55f, 0.75f);         // 次要文字（星标未选中）
	public static Color FavRow => new Color(0.4f, 0.58f, 0.4f, 0.95f);            // 收藏行底
	public static Color RowBg => new Color(0.12f, 0.2f, 0.12f, 0.85f);            // 普通行底
	public static Color StarOn => new Color(1f, 0.85f, 0.3f, 1f);                 // ★
	public static Color StarHot => new Color(0.8f, 0.9f, 0.8f, 0.95f);            // ☆ 悬停

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
