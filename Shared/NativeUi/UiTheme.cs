using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2Shared.NativeUi
{
	/// <summary>
	/// 征服模式 UI 主题：原生字体 + 原生描边 + 克制配色。
	///
	/// 目标（照 `ER2_UI_design.md` 的准则）：看起来"本来就是游戏的一部分"，而不是 mod 叠层。
	///   · 字体取游戏 HUD 的活体 uGUI Text 字体（拿不到再退 GUI.skin → 内置字体）
	///   · 描边走游戏自己的 `GuiExtension.OutlinedLabel`
	///   · 底色用半透明黑，不用纯黑纯白
	///   · 字体/纹理**只解析一次并缓存**；运行时纹理必须 `hideFlags=(HideFlags)61`（陷阱 12）
	/// </summary>
	public static class UiTheme
	{
		// ---- 配色（克制：两级文字色 + 语义色） ----
		public static readonly Color PanelBg = new Color(0.03f, 0.035f, 0.045f, 0.94f);
		public static readonly Color PanelBgSoft = new Color(0.05f, 0.06f, 0.075f, 0.88f);
		public static readonly Color CardBg = new Color(0.09f, 0.10f, 0.12f, 0.95f);
		public static readonly Color RowHover = new Color(1f, 1f, 1f, 0.07f);
		public static readonly Color Line = new Color(1f, 1f, 1f, 0.12f);

		public static readonly Color TextPrimary = new Color(0.90f, 0.91f, 0.93f);
		public static readonly Color TextDim = new Color(0.58f, 0.60f, 0.64f);
		public static readonly Color TextFaint = new Color(0.40f, 0.42f, 0.46f);

		public static readonly Color Accent = new Color(0.98f, 0.76f, 0.30f);      // 高亮/可行动
		public static readonly Color Danger = new Color(0.90f, 0.32f, 0.28f);      // 敌方/危险
		public static readonly Color Success = new Color(0.42f, 0.78f, 0.44f);     // 我方/成功
		public static readonly Color Warn = new Color(0.95f, 0.68f, 0.25f);

		// ---- 势力配色（按阵营名散列取色，零维护——不硬编码国家表） ----
		private static readonly Color[] FactionPalette =
		{
			new Color(0.36f, 0.62f, 0.92f),  // 蓝
			new Color(0.85f, 0.34f, 0.32f),  // 红
			new Color(0.45f, 0.78f, 0.50f),  // 绿
			new Color(0.88f, 0.70f, 0.32f),  // 金
			new Color(0.68f, 0.50f, 0.86f),  // 紫
			new Color(0.40f, 0.78f, 0.80f),  // 青
			new Color(0.86f, 0.55f, 0.30f),  // 橙
			new Color(0.75f, 0.45f, 0.62f)   // 玫红
		};

		private static readonly Dictionary<string, Color> _factionColors =
			new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);

		/// <summary>阵营 → 稳定颜色（同名恒同色）。</summary>
		public static Color FactionColor(string faction)
		{
			if (string.IsNullOrEmpty(faction)) return TextFaint;
			Color c;
			if (_factionColors.TryGetValue(faction, out c)) return c;

			int h = 0;
			for (int i = 0; i < faction.Length; i++) h = unchecked(h * 31 + faction[i]);
			c = FactionPalette[Math.Abs(h) % FactionPalette.Length];
			_factionColors[faction] = c;
			return c;
		}

		// ---- 字体 ----

		private static Font _font;
		private static bool _fontResolved;

		/// <summary>
		/// 原生字体。回退链：活体 uGUI Text（游戏 HUD 字体）→ GUI.skin.font → 内置 LegacyRuntime。
		/// 只解析一次。
		/// </summary>
		public static Font Font
		{
			get
			{
				if (_fontResolved) return _font;
				_fontResolved = true;

				// 1) 活体 uGUI Text —— 最贴近原生观感（游戏 HUD 用的就是它）
				try
				{
					UnityEngine.UI.Text[] texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
					if (texts != null)
					{
						for (int i = 0; i < texts.Length; i++)
						{
							try
							{
								if (texts[i] != null && texts[i].font != null)
								{
									_font = texts[i].font;
									NativeUiLog.Inf("UI 字体取自活体 uGUI Text: '" + _font.name + "'");
									return _font;
								}
							}
							catch { }
						}
					}
				}
				catch { }

				// 2) GUI.skin
				try
				{
					if (GUI.skin != null && GUI.skin.font != null)
					{
						_font = GUI.skin.font;
						NativeUiLog.Inf("UI 字体取自 GUI.skin: '" + _font.name + "'");
						return _font;
					}
				}
				catch { }

				// 3) 内置
				try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
				return _font;
			}
		}

		// ---- GUIStyle 缓存（按 (字号, 颜色, 对齐) 键复用） ----

		private static readonly Dictionary<string, GUIStyle> _styles = new Dictionary<string, GUIStyle>();

		public static GUIStyle Style(int size, Color color, TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle fs = FontStyle.Normal)
		{
			string key = size + "|" + ColorKey(color) + "|" + (int)anchor + "|" + (int)fs;
			GUIStyle s;
			if (_styles.TryGetValue(key, out s) && s != null) return s;

			s = new GUIStyle();
			// 陷阱 11：new GUIStyle() 默认 normal.textColor 是黑，必须显式设
			s.normal.textColor = color;
			s.fontSize = size;
			s.fontStyle = fs;
			s.alignment = anchor;
			s.wordWrap = false;
			s.richText = true;
			Font f = Font;
			if (f != null) s.font = f;

			_styles[key] = s;
			return s;
		}

		private static string ColorKey(Color c) =>
			((int)(c.r * 255)) + "_" + ((int)(c.g * 255)) + "_" + ((int)(c.b * 255)) + "_" + ((int)(c.a * 255));

		// ---- 纹理（纯色/圆点） ----

		private static Texture2D _white;
		private static readonly Dictionary<string, Texture2D> _solid = new Dictionary<string, Texture2D>();
		private static readonly Dictionary<int, Texture2D> _disc = new Dictionary<int, Texture2D>();

		/// <summary>1x1 白纹理（用 GUI.color 上色）。</summary>
		public static Texture2D White
		{
			get
			{
				if (_white != null) return _white;
				_white = new Texture2D(1, 1, TextureFormat.RGBA32, false);
				_white.SetPixel(0, 0, Color.white);
				_white.Apply();
				_white.hideFlags = (HideFlags)61;  // 陷阱 12：防场景卸载
				return _white;
			}
		}

		/// <summary>实心圆点纹理（省份节点）。按直径缓存。</summary>
		public static Texture2D Disc(int diameter)
		{
			if (diameter < 4) diameter = 4;
			Texture2D t;
			if (_disc.TryGetValue(diameter, out t) && t != null) return t;

			t = new Texture2D(diameter, diameter, TextureFormat.RGBA32, false);
			float r = diameter * 0.5f;
			Color[] px = new Color[diameter * diameter];
			for (int y = 0; y < diameter; y++)
			{
				for (int x = 0; x < diameter; x++)
				{
					float dx = x + 0.5f - r, dy = y + 0.5f - r;
					float d = Mathf.Sqrt(dx * dx + dy * dy);
					// 边缘 1px 抗锯齿
					float a = Mathf.Clamp01(r - d);
					px[y * diameter + x] = new Color(1f, 1f, 1f, a);
				}
			}
			t.SetPixels(px);
			t.Apply();
			t.hideFlags = (HideFlags)61;
			_disc[diameter] = t;
			return t;
		}

		// ---- 绘制助手 ----

		/// <summary>填充矩形（半透明底色用）。</summary>
		public static void Fill(Rect r, Color c)
		{
			Color prev = GUI.color;
			GUI.color = c;
			GUI.DrawTexture(r, White);
			GUI.color = prev;
		}

		/// <summary>1px 横线。</summary>
		public static void HLine(float x, float y, float w, Color c)
		{
			Fill(new Rect(x, y, w, 1f), c);
		}

		/// <summary>带描边的文字（走游戏原生描边实现；失败则退普通 Label）。</summary>
		public static void Text(Rect r, string s, GUIStyle style, int outline = 1)
		{
			if (string.IsNullOrEmpty(s)) return;
			try
			{
				GuiExtension.OutlinedLabel(r, s, style, outline);
				return;
			}
			catch { }
			try { GUI.Label(r, s, style); } catch { }
		}

		/// <summary>无描边的普通文字（小字/次要信息用，避免整屏描边太吵）。</summary>
		public static void TextPlain(Rect r, string s, GUIStyle style)
		{
			if (string.IsNullOrEmpty(s)) return;
			try { GUI.Label(r, s, style); } catch { }
		}

		/// <summary>
		/// 全局交互开关。弹窗打开时置 false，让底下的按钮全部失效。
		///
		/// ⚠️ 为什么需要它（2026-09-13 实测"弹窗关不掉"的根因）：
		///   IMGUI 里**先画的控件先拿到鼠标**（不是后画的在上层！）——
		///   弹窗虽然最后绘制，但它底下的省份卡片按钮是**先**画的，
		///   于是点击被卡片吞掉，弹窗的【关闭】永远收不到事件。
		///   正确做法：弹窗期间把下层控件的交互整体关掉。
		/// </summary>
		public static bool InputEnabled = true;

		/// <summary>自绘按钮。返回是否被点击。样式沿用工作区惯例（值框式：浅底深字/悬停提亮）。</summary>
		public static bool Button(Rect r, string label, bool enabled = true, bool primary = false)
		{
			bool interactive = enabled && InputEnabled;
			bool hover = interactive && r.Contains(Event.current.mousePosition);
			Color bg = primary ? new Color(0.24f, 0.34f, 0.22f, 0.95f) : new Color(0.14f, 0.15f, 0.18f, 0.95f);
			if (!enabled) bg = new Color(0.10f, 0.10f, 0.11f, 0.70f);
			else if (hover) bg = primary ? new Color(0.30f, 0.42f, 0.27f, 0.98f) : new Color(0.20f, 0.22f, 0.26f, 0.98f);

			Fill(r, bg);
			Fill(new Rect(r.x, r.y, r.width, 1f), Line);

			GUIStyle st = Style(14, enabled ? TextPrimary : TextFaint, TextAnchor.MiddleCenter, FontStyle.Bold);
			TextPlain(new Rect(r.x, r.y + 1f, r.width, r.height), label, st);

			if (!interactive) return false;
			return GUI.Button(r, GUIContent.none, GUIStyle.none);
		}

		/// <summary>交互式点击区（列表行 / 选项卡用）。弹窗打开时一律失效。</summary>
		public static bool ClickArea(Rect r)
		{
			if (!InputEnabled) return false;
			return GUI.Button(r, GUIContent.none, GUIStyle.none);
		}

		/// <summary>势力色圆点 + 描边。</summary>
		public static void NodeDisc(float cx, float cy, int diameter, Color color, bool selected, bool highlight)
		{
			float r = diameter * 0.5f;
			if (selected || highlight)
			{
				int ring = diameter + (selected ? 10 : 6);
				Color rc = selected ? Accent : new Color(1f, 1f, 1f, 0.55f);
				rc.a = selected ? 0.85f : 0.35f;
				Fill(new Rect(cx - ring * 0.5f, cy - ring * 0.5f, ring, ring), new Color(0f, 0f, 0f, 0f));
				Color prev = GUI.color;
				GUI.color = rc;
				GUI.DrawTexture(new Rect(cx - ring * 0.5f, cy - ring * 0.5f, ring, ring), Disc(ring));
				GUI.color = prev;
			}
			Color p = GUI.color;
			GUI.color = color;
			GUI.DrawTexture(new Rect(cx - r, cy - r, diameter, diameter), Disc(diameter));
			GUI.color = p;
		}

		// ================= 自绘滚动列表 =================
		//
		// ⚠️ 为什么不用 GUI.BeginScrollView（2026-09-13 实测踩坑，294 次/帧的崩溃）：
		//    `GUI.BeginScrollView(Rect, Vector2, Rect)` 的 3 参重载在本游戏的 IL2CPP 构建里
		//    **被代码裁剪掉了** → 抛 `System.NotSupportedException: Method unstripping failed`
		//    （栈：BeginScrollView(8参) ← BeginScrollView(3参) ← DrawArmyTab）。
		//    这是 IL2CPP 裁剪的典型症状：托管侧存在、原生侧没有对应实现。
		//    游戏自己用不到的 UnityEngine 方法都可能被裁掉，**mod 不能想当然地用**。
		//
		// 替代方案：自己算偏移 + 只绘制可见行（不依赖任何裁剪/滚动 API），
		// 滚轮走 EventType.ScrollWheel，滚动条自绘。

		/// <summary>把滚动偏移夹到合法范围。</summary>
		public static float ClampScroll(float scroll, Rect view, float contentH)
		{
			float max = Mathf.Max(0f, contentH - view.height);
			if (scroll < 0f) return 0f;
			if (scroll > max) return max;
			return scroll;
		}

		/// <summary>处理滚轮（鼠标在视图内才响应）。</summary>
		public static bool HandleWheel(Rect view, ref float scroll, float contentH)
		{
			Event e = Event.current;
			if (e == null) return false;
			if (e.type != EventType.ScrollWheel) return false;
			if (!view.Contains(e.mousePosition)) return false;

			scroll = ClampScroll(scroll + e.delta.y * 30f, view, contentH);
			e.Use();
			return true;
		}

		/// <summary>自绘细长滚动条（贴视图右缘）。内容不溢出时不画。</summary>
		public static void DrawScrollbar(Rect view, float scroll, float contentH)
		{
			if (contentH <= view.height + 1f) return;

			const float w = 6f;
			float trackX = view.xMax - w - 2f;
			Fill(new Rect(trackX, view.y, w, view.height), new Color(1f, 1f, 1f, 0.06f));

			float ratio = view.height / contentH;
			float thumbH = Mathf.Max(28f, view.height * ratio);
			float maxScroll = Mathf.Max(0.0001f, contentH - view.height);
			float t = Mathf.Clamp01(scroll / maxScroll);
			float thumbY = view.y + t * (view.height - thumbH);

			Fill(new Rect(trackX, thumbY, w, thumbH), new Color(1f, 1f, 1f, 0.26f));
		}

		/// <summary>行是否落在可视区内（跳过多余绘制——列表可能有上千行）。</summary>
		public static bool RowVisible(Rect view, float rowY, float rowH)
		{
			return rowY + rowH >= view.y && rowY <= view.yMax;
		}
	}
}
