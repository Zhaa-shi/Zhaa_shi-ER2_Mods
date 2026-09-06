using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 生成面板（IMGUI，屏幕左侧抽屉）。设计见 docs/DESIGN.md §2.3。
/// 陷阱 5：GUIStyle 的 normal.textColor 必须显式设置；拷贝构造被裁剪。
/// </summary>
internal static class GenPanel
{
	// ── 状态 ──
	private static bool open;
	public static bool RtsActive;

	private static string faction = "mine";          // mine / enemy / neutral
	private static string category = "favorites";    // favorites / infantry / tanks / wheeled / planes / artillery
	private static int page;
	private static string flash = "";
	private static float flashUntil;
	private static bool flashIsError;

	// 最近生成置顶（会话内）
	private static readonly List<string> recentIds = new();

	// 面板位置（标题栏可拖动，会话内记忆）；默认避开宿主左上状态行（y=9/37/65 三行）
	private static Vector2 panelPos = new Vector2(12f, 100f);
	private static bool dragging;

	// 乘员模式（载具页）：0=专用坦克组员 1=所选步兵类型 2=无（空车）
	private static int crewMode;
	private static GenEntry infantrySel; // "所选步兵"模式的来源（最近点过的步兵条目）

	// ── 布局常量 ──
	private const float PanelW = 320f;
	private const float RowH = 26f;
	private const int VisibleRows = 10;

	private static GUIStyle titleStyle, textStyle, buttonStyle, activeButtonStyle, rowStyle, flashStyle, starStyle;

	public static bool IsOpen => open;

	/// <summary>宿主 IsMouseOverGui 用的遮挡区：
	/// 放置模式=全屏（宿主完全让位，避免放置点击同时触发原生框选/指令）；
	/// 面板打开=面板 Rect；面板关闭=左下角开关按钮 Rect；其余 null。</summary>
	public static Rect? ExternalGuiBlockRect()
	{
		if (!RtsActive || HostLink.EscMenuOpen) return null;
		if (Placer.Placing) return new Rect(0f, 0f, Screen.width, Screen.height);
		if (open) return PanelRect();
		return ToggleBlockRect();
	}

	public static void Toggle()
	{
		if (!HostLink.HostPresent)
		{
			Plugin.ModLog.LogWarning("[UniGen] 未找到战场指挥官宿主——本 mod 仅在其 RTS 模式内工作。");
			return;
		}
		SetOpen(!open, true); // 用户主动打开 → 回到收藏页
	}

	/// <summary>开关面板。resetCategory：打开时是否回到收藏页（放置后重开=false，保持原类别）。</summary>
	public static void SetOpen(bool v, bool resetCategory = true)
	{
		open = v;
		if (open)
		{
			if (resetCategory)
			{
				category = "favorites";
				page = 0;
			}
			GenCatalog.EnsureVehicleCatalog();
			GenCatalog.ProbeInfantryTypes(); // SquadType 枚举探测（每战斗一次）
			GenCatalog.RebuildFavorites();   // 收藏条目解析（目录就绪后）
		}
	}

	public static void Flash(string msg, bool isError = false)
	{
		flash = msg;
		flashIsError = isError;
		flashUntil = Time.unscaledTime + 2.2f;
	}

	// ── 主绘制 ──
	public static void Draw()
	{
		if (!open || !RtsActive) return;
		EnsureStyles();

		// 落点模式时面板缩成提示徽标
		if (Placer.Placing) { DrawPlacingBadge(); return; }

		Rect r = PanelRect();

		// 标题栏拖动（IMGUI 标准模式；拖动区不含关闭按钮）
		Rect titleHit = new Rect(r.x, r.y, r.width - 100f, 26f);
		if (Event.current.type == EventType.MouseDown && titleHit.Contains(Event.current.mousePosition))
		{
			dragging = true;
			Event.current.Use();
		}
		else if (dragging && Event.current.type == EventType.MouseDrag)
		{
			panelPos += Event.current.delta;
			panelPos.x = Mathf.Clamp(panelPos.x, 0f, Screen.width - 80f);
			panelPos.y = Mathf.Clamp(panelPos.y, 0f, Screen.height - 80f);
			Event.current.Use();
		}
		else if (dragging && (Event.current.type == EventType.MouseUp || Event.current.rawType == EventType.MouseUp))
		{
			dragging = false;
			Event.current.Use();
		}

		GUI.color = new Color(0.02f, 0.05f, 0.02f, 0.88f);
		GUI.DrawTexture(r, Texture2D.whiteTexture);
		GUI.color = Color.white;

		float x = r.x + 10f, w = r.width - 20f;
		float y = r.y + 8f;

		// 标题行（含一键清除）
		GUI.Label(new Rect(x, y, w - 100f, 24f), Ui.Tr("通用生成"), titleStyle);
		Rect clearBtn = new Rect(r.xMax - 96f, y, 62f, 22f);
		if (DrawButton(clearBtn, Ui.Tr("清除"), buttonStyle))
		{
			int n = GenRunner.ClearAllSpawned();
			Flash(Ui.Tr("已清除 ") + n + Ui.Tr(" 个生成物"));
		}
		Rect closeBtn = new Rect(r.xMax - 30f, y, 22f, 22f);
		if (DrawButton(closeBtn, "×", buttonStyle)) { SetOpen(false); return; }
		y += 26f;

		// 阵营切换（我方 / 敌方 / 中立）
		float third = (w - 16f) / 3f;
		if (DrawButton(new Rect(x, y, third, 30f), Ui.Tr("我方"), faction == "mine" ? activeButtonStyle : buttonStyle)) faction = "mine";
		if (DrawButton(new Rect(x + third + 8f, y, third, 30f), Ui.Tr("敌方"), faction == "enemy" ? activeButtonStyle : buttonStyle)) faction = "enemy";
		if (DrawButton(new Rect(x + (third + 8f) * 2f, y, third, 30f), Ui.Tr("中立"), faction == "neutral" ? activeButtonStyle : buttonStyle)) faction = "neutral";
		y += 36f;

		// 类别页签（6 个：收藏 + 五类）
		string[] cats = { "favorites", "infantry", "tanks", "wheeled", "planes", "artillery" };
		string[] catNames = { Ui.Tr("收藏"), Ui.Tr("步兵"), Ui.Tr("坦克"), Ui.Tr("轮式"), Ui.Tr("飞机"), Ui.Tr("火炮") };
		float cw = (w - 5f * 4f) / 6f;
		for (int i = 0; i < cats.Length; i++)
		{
			bool sel = category == cats[i];
			if (DrawButton(new Rect(x + i * (cw + 4f), y, cw, 24f), catNames[i], sel ? activeButtonStyle : buttonStyle))
			{
				category = cats[i];
				page = 0;
			}
		}
		y += 30f;

		// 条目列表（分页制：GUI.BeginScrollView 被游戏裁剪，禁用滚动区；只用 Label/Button/DrawTexture）
		List<GenEntry> bucket = FilteredBucket();
		int pages = Mathf.Max(1, Mathf.CeilToInt(bucket.Count / (float)VisibleRows));
		if (page >= pages) page = pages - 1;
		float listH = VisibleRows * RowH + 8f;
		Rect listOuter = new Rect(x, y, w, listH);
		GUI.color = new Color(0f, 0f, 0f, 0.35f);
		GUI.DrawTexture(listOuter, Texture2D.whiteTexture);
		GUI.color = Color.white;
		if (bucket.Count == 0)
		{
			GUI.Label(new Rect(x + 8f, y + 6f, w - 16f, 22f), Ui.Tr("（无匹配条目）"), textStyle);
		}
		else
		{
			int from = page * VisibleRows;
			int to = Mathf.Min(from + VisibleRows, bucket.Count);
			GenEntry clicked = null;
			for (int i = from; i < to; i++)
			{
				GenEntry e = bucket[i];
				float rowY = y + 4f + (i - from) * RowH;
				Color keep = GUI.backgroundColor;
				string label = e.Title;
				bool fav = GenCatalog.IsFav(e.Id);
				// 名称按钮（点=放置）
				Rect nameBtn = new Rect(x + 6f, rowY, w - 38f, RowH - 2f);
				GUI.backgroundColor = fav ? new Color(0.4f, 0.58f, 0.4f, 0.95f) : new Color(0.12f, 0.2f, 0.12f, 0.85f);
				if (GUI.Button(nameBtn, label, rowStyle)) clicked = e;
				// 收藏星标（点=切换收藏；透明底，仅星形变色）
				GUI.backgroundColor = keep;
				string star = fav ? "★" : "☆";
				Rect starBtn = new Rect(x + w - 30f, rowY + 1f, 26f, RowH - 4f);
				bool starHover = starBtn.Contains(Event.current.mousePosition);
				Color keepTxt = GUI.contentColor;
				GUI.contentColor = fav ? new Color(1f, 0.85f, 0.3f, 1f) : (starHover ? new Color(0.8f, 0.9f, 0.8f, 0.95f) : new Color(0.55f, 0.65f, 0.55f, 0.75f));
				if (GUI.Button(starBtn, star, starStyle)) GenCatalog.ToggleFav(e);
				GUI.contentColor = keepTxt;
			}
			if (clicked != null)
			{
				Event.current.Use();
				BeginPlacement(clicked);
				return;
			}
		}
		y += listH + 4f;

		// 分页行
		if (pages > 1)
		{
			if (DrawButton(new Rect(x, y, 30f, 22f), "◀", buttonStyle)) page = Mathf.Max(0, page - 1);
			GUI.Label(new Rect(x + 38f, y + 2f, 60f, 20f), (page + 1) + "/" + pages, textStyle);
			if (DrawButton(new Rect(x + w - 30f, y, 30f, 22f), "▶", buttonStyle)) page = Mathf.Min(pages - 1, page + 1);
		}
		y += 26f;

		// 乘员行（步兵无乘员选项）。开关都用按钮（GUI.Toggle 有裁剪风险）
		if (category != "infantry")
		{
			string crewLabel = crewMode == 0 ? Ui.Tr("乘员:专用") : crewMode == 1 ? Ui.Tr("乘员:兵班") : Ui.Tr("乘员:无");
			if (DrawButton(new Rect(x, y, 90f, 24f), crewLabel, crewMode != 2 ? activeButtonStyle : buttonStyle))
				crewMode = (crewMode + 1) % 3;
			if (crewMode == 1 && GenCatalog.infantry.Count > 0)
			{
				// 兵班选择器：‹ 类型 ›（在步兵类型表里循环）
				if (infantrySel == null) infantrySel = GenCatalog.infantry[0];
				int idx = GenCatalog.infantry.FindIndex(e => e.Id == infantrySel.Id);
				if (idx < 0) idx = 0;
				if (DrawButton(new Rect(x + 94f, y, 22f, 24f), "‹", buttonStyle))
					infantrySel = GenCatalog.infantry[(idx - 1 + GenCatalog.infantry.Count) % GenCatalog.infantry.Count];
				GUI.Label(new Rect(x + 118f, y + 3f, 110f, 20f), infantrySel.Title, textStyle);
				if (DrawButton(new Rect(x + 230f, y, 22f, 24f), "›", buttonStyle))
					infantrySel = GenCatalog.infantry[(idx + 1) % GenCatalog.infantry.Count];
			}
		}
		y += 32f;

		// 提示行（显示将生成的真实阵营 id）
		string myFac = GenRunner.MyFaction();
		string previewFac;
		string sideTag;
		if (faction == "neutral")
		{
			previewFac = "Civilian";
			sideTag = Ui.Tr("中立");
		}
		else
		{
			previewFac = faction == "mine"
				? (string.IsNullOrEmpty(myFac) ? "UnitedStates_allies" : myFac)
				: (FactionData.IsFriendly(GenRunner.EnemyFaction(), myFac) || string.IsNullOrEmpty(GenRunner.EnemyFaction())
					? FactionData.OppositeOf(myFac) : GenRunner.EnemyFaction());
			sideTag = FactionData.IsFriendly(previewFac, myFac) ? Ui.Tr("我方") : Ui.Tr("敌方");
		}
		GUI.Label(new Rect(x, y, w, 20f), Ui.Tr("→ 生成 ") + previewFac + "（" + sideTag + "）", textStyle);
		y += 24f;
	}

	// ── 落点模式启动 ──
	private static void BeginPlacement(GenEntry entry)
	{
		if (recentIds.Contains(entry.Id)) recentIds.Remove(entry.Id);
		recentIds.Insert(0, entry.Id);
		if (recentIds.Count > 8) recentIds.RemoveAt(recentIds.Count - 1);

		string myFac = GenRunner.MyFaction();
		string fac;
		if (faction == "neutral")
		{
			fac = "Civilian"; // Faction 枚举成员名（中立平民阵营）
		}
		else if (faction == "mine")
		{
			fac = myFac;
			// 我方未知 → 默认盟军代表（训练场等无 PlayerController 的场景）
			if (string.IsNullOrEmpty(fac)) fac = "UnitedStates_allies";
		}
		else
		{
			fac = GenRunner.EnemyFaction();
			// 战局双方同阵营/数据缺失（训练场）→ 按我方后缀推导对立阵营
			if (string.IsNullOrEmpty(fac) || FactionData.IsFriendly(fac, myFac))
				fac = FactionData.OppositeOf(myFac);
		}
		if (string.IsNullOrEmpty(fac))
		{
			Flash(Ui.Tr("阵营未知——请先进入战斗"), true);
			return;
		}

		// 记录步兵选择（"乘员:兵班"模式的来源）
		if (entry.IsInfantry) infantrySel = entry;
		else if (infantrySel == null && GenCatalog.infantry.Count > 0) infantrySel = GenCatalog.infantry[0];

		SetOpen(false); // 面板完全收起：放置期间 IsOpen=false，点击不再被 guiNow 吞掉
		Placer.Begin(entry, fac, ResolveCrewType(entry, fac));
	}

	/// <summary>载具乘员班型：0=国别专用坦克组员 1=所选步兵类型 2=无。步兵条目恒 null。</summary>
	private static SquadType? ResolveCrewType(GenEntry entry, string fac)
	{
		if (entry.IsInfantry || crewMode == 2) return null;
		if (crewMode == 0) return GenCatalog.TankCrewTypeFor(fac);
		return infantrySel != null && infantrySel.IsInfantry ? infantrySel.SType : (SquadType?)null;
	}

	/// <summary>放置徽标（屏幕底部居中，不挡视野）。</summary>
	public static Rect BadgeRect()
	{
		return new Rect((Screen.width - 430f) * 0.5f, Screen.height - 62f, 430f, 28f);
	}

	/// <summary>鼠标是否悬停在放置徽标上（GUI 坐标系：y 向下）。</summary>
	public static bool IsMouseOverBadge()
	{
		Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
		return Placer.Placing && BadgeRect().Contains(m);
	}

	// ── 私有 ──
	private static Rect PanelRect()
	{
		// 标题26 + 阵营36 + 页签30 + 列表(10行+8) + 分页26 + 选项32 + 提示24 + 上下留白16
		float h = 8f + 26f + 36f + 30f + (VisibleRows * RowH + 8f) + 26f + 32f + 24f + 8f;
		return new Rect(panelPos.x, panelPos.y, PanelW, h);
	}

	private static List<GenEntry> FilteredBucket()
	{
		return GenCatalog.GetBucket(category);
	}

	private static bool DrawButton(Rect r, string label, GUIStyle style)
	{
		bool clicked = GUI.Button(r, label, style);
		return clicked;
	}

	public static void DrawPlacingBadge()
	{
		// 底部居中窄条（宿主提示条上方），不遮战场
		Rect r = BadgeRect();
		GUI.color = new Color(0.02f, 0.05f, 0.02f, 0.78f);
		GUI.DrawTexture(r, Texture2D.whiteTexture);
		GUI.color = Color.white;
		string txt = Ui.Tr("放置: ") + (Placer.PendingEntry()?.Title ?? "?")
			+ Ui.Tr("    左键 确认 · Shift 连续 · 右键 取消");
		GUI.Label(new Rect(r.x + 8f, r.y + 4f, r.width - 16f, 22f), txt, flashStyle);
	}

	/// <summary>生成反馈（跟随面板，面板关闭时跟随左下角按钮位置）。</summary>
	public static void DrawFlash()
	{
		if (flash == "" || Time.unscaledTime >= flashUntil) return;
		if (!RtsActive) return;
		EnsureStyles();
		GUIStyle st = flashStyle;
		st.normal.textColor = flashIsError ? new Color(1f, 0.45f, 0.4f, 0.98f) : new Color(0.85f, 1f, 0.85f, 0.98f);
		GUI.Label(new Rect(panelPos.x, panelPos.y - 24f, 700f, 22f), flash, st);
	}

	/// <summary>RTS 左下角的"生成"开关按钮（仅面板关闭时显示；点击=G）。</summary>
	private static Rect ToggleButtonRect()
	{
		return new Rect(12f, Screen.height - 34f, 120f, 26f);
	}

	public static void DrawToggleButton()
	{
		if (!RtsActive || open || Placer.Placing) return;
		EnsureStyles();
		Rect r = ToggleButtonRect();
		bool clicked = GUI.Button(r, Ui.Tr("生成 [G]"), buttonStyle);
		if (clicked)
		{
			Event.current.Use();
			Toggle();
		}
	}

	/// <summary>左下角开关按钮的 Rect（面板关闭时的手势遮挡区）。</summary>
	public static Rect? ToggleBlockRect()
	{
		if (!RtsActive || open || Placer.Placing) return null;
		return ToggleButtonRect();
	}

	/// <summary>自建纯色按钮样式（normal/hover/active 同底色，文字色一致）。</summary>
	private static GUIStyle MakeSolidButton(int fontSize, Color bg, Color fg, FontStyle fs)
	{
		GUIStyle s = new GUIStyle();
		s.fontSize = fontSize;
		s.fontStyle = fs;
		s.alignment = TextAnchor.MiddleCenter;
		s.normal.textColor = fg;
		s.hover.textColor = fg;
		s.active.textColor = fg;
		s.focused.textColor = fg;
		Texture2D tex = SolidTexture(bg);
		s.normal.background = tex;
		s.hover.background = tex;
		s.active.background = tex;
		s.focused.background = tex;
		return s;
	}

	private static Texture2D solidTex;
	private static Color solidTexColor;
	private static Texture2D SolidTexture(Color c)
	{
		if (solidTex != null && solidTexColor == c) return solidTex;
		Texture2D t = new Texture2D(4, 4, TextureFormat.RGBA32, false);
		Color[] px = new Color[16];
		for (int i = 0; i < px.Length; i++) px[i] = c;
		t.SetPixels(px);
		t.Apply();
		solidTex = t;
		solidTexColor = c;
		return t;
	}

	private static void EnsureStyles()
	{
		if (titleStyle != null) return;
		// 陷阱 5：GUIStyle 拷贝构造被 IL2CPP 裁剪——全部 new GUIStyle() + 显式字段；normal.textColor 必须显式
		titleStyle = new GUIStyle();
		titleStyle.fontSize = 15;
		titleStyle.fontStyle = FontStyle.Bold;
		titleStyle.normal.textColor = new Color(0.87f, 0.94f, 0.87f, 1f);
		titleStyle.alignment = TextAnchor.MiddleLeft;

		textStyle = new GUIStyle();
		textStyle.fontSize = 12;
		textStyle.normal.textColor = new Color(0.87f, 0.94f, 0.87f, 1f);
		textStyle.alignment = TextAnchor.MiddleLeft;

		// 按钮样式：宿主同款主题（默认 #0E1C0EB4 底 / #3E703EE0 选中 / #DFF0DF 文字）
		Color baseBg = new Color(0x0E / 255f, 0x1C / 255f, 0x0E / 255f, 0xB4 / 255f);
		Color hoverBg = new Color(0x3E / 255f, 0x70 / 255f, 0x3E / 255f, 0xE0 / 255f);
		Color text = new Color(0xDF / 255f, 0xF0 / 255f, 0xDF / 255f, 1f);
		buttonStyle = MakeSolidButton(12, baseBg, text, FontStyle.Normal);
		activeButtonStyle = MakeSolidButton(12, hoverBg, new Color(0.04f, 0.09f, 0.04f, 1f), FontStyle.Bold);
		rowStyle = MakeSolidButton(12, new Color(baseBg.r, baseBg.g, baseBg.b, 0.82f), text, FontStyle.Normal);
		rowStyle.alignment = TextAnchor.MiddleLeft;

		flashStyle = new GUIStyle();
		flashStyle.fontSize = 13;
		flashStyle.fontStyle = FontStyle.Bold;
		flashStyle.normal.textColor = new Color(0.85f, 1f, 0.85f, 0.98f);

		// 收藏星标：透明底（无底色贴图），仅文字颜色随状态
		starStyle = new GUIStyle();
		starStyle.fontSize = 14;
		starStyle.alignment = TextAnchor.MiddleCenter;
		starStyle.normal.textColor = new Color(0.55f, 0.65f, 0.55f, 0.75f);
		starStyle.hover.textColor = Color.white;
		starStyle.active.textColor = Color.white;
		starStyle.focused.textColor = Color.white;
	}
}
