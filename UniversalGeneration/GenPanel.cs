using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 生成面板（IMGUI，屏幕左侧抽屉）。设计见 docs/DESIGN.md §2.3。
/// 陷阱 5：GUIStyle 的 normal.textColor 必须显式设置；拷贝构造被裁剪。
///
/// 2.0.0：新增**物品页签族**（武器/弹药/投掷物/装备/医疗食物/服装/其他）——
/// 条目点击拿起后拖放投放（拖到单位身上进背包 / 拖到地上生成实体），见 ItemDragger。
/// </summary>
internal static class GenPanel
{
	// ── 状态 ──
	private static bool open;
	public static bool RtsActive;

	private static string faction = "mine";          // mine / enemy / neutral
	private static string category = "favorites";    // favorites / infantry / ... / item:weapons ...
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
	private const float RowH = 28f;      // 1.2.0：26→28（用户反馈"太密"）
	private const int VisibleRows = 10;
	private const int TabsPerRow = 4;    // 1.2.0：8 个页签两行排（一行塞 7 个太挤）
	private const int ItemTabsPerRow = 4; // 2.0.0：物品页签族同样两行排
	private const int FavTabsPerRow = 4;  // 2.1.0：收藏分类子页签（最多 6 单位 + 6 物品 = 3 行）
	private const float TabH = 24f;

	private static GUIStyle titleStyle, textStyle, buttonStyle, activeButtonStyle, rowStyle, flashStyle, starStyle;

	// 2.4.1：页签专用样式（**只给 DrawTabButton 用**，字号会被逐次改写以适配定宽按钮；
	// 不能复用 buttonStyle/activeButtonStyle——它们被别处以固定 12 号使用）
	private static GUIStyle tabStyle, tabActiveStyle;

	// 2.0.0：物品页签族缓存（"item:weapons" → 显示名）；库存/收藏页签数变化时重建
	private static readonly List<string> itemCats = new();
	private static readonly List<string> itemCatNames = new();

	// 2.1.0：收藏分类子页签（单位分类 "infantry"… 与物品分类 "item:weapons"… 混排，只列有收藏的）
	private static readonly List<string> favCats = new();
	private static readonly List<string> favCatNames = new();
	private static string favCat = "";

	// 2.1.0：物品子分类（""=全部 / rifle / pistol / wearable）
	private static string itemSub = "";

	// 2.1.1：物品首字母索引（不用 GUI.TextField——IL2CPP 下被裁剪，见陷阱）
	private static string letterFilter = "";

	public static bool IsOpen => open;

	/// <summary>宿主 IsMouseOverGui 用的遮挡区：
	/// 放置/携带模式=全屏（宿主完全让位，避免投放点击同时触发原生框选/指令）；
	/// 面板打开=面板 Rect；面板关闭=左缘开关按钮 Rect；其余 null。</summary>
	public static Rect? ExternalGuiBlockRect()
	{
		if (!RtsActive || HostLink.EscMenuOpen) return null;
		if (Placer.Placing) return new Rect(0f, 0f, Screen.width, Screen.height);
		if (ItemDragger.Carrying) return ItemDragger.BlockRect(); // 2.0.0：物品携带全屏互斥
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
			GenCatalog.RebuildFavorites(); // 收藏条目解析（目录就绪后）
			ItemCatalog.Ensure();          // 2.0.2：物品目录（运行时 ItemsDatabase 枚举，分帧）
			RebuildItemTabs();
			RebuildFavTabs();              // 2.1.0：收藏分类子页签
			// 探测在游戏启动时后台进行（Tick 看门狗续跑）——面板打开零探测负担（1.3.1）
		}
	}

	/// <summary>2.0.0：物品页签族（只列有内容的桶）。</summary>
	private static void RebuildItemTabs()
	{
		itemCats.Clear();
		itemCatNames.Clear();
		foreach (string b in ItemCatalog.BucketOrder)
		{
			if (!ItemCatalog.HasBucket(b)) continue;
			itemCats.Add("item:" + b);
			itemCatNames.Add(Ui.Tr(BucketLabel(b)));
		}
		// 当前选中的物品页签若已被清空（校验剔除了全部条目），退回第一个可用页签
		if (IsItemCategory && !itemCats.Contains(category))
			category = itemCats.Count > 0 ? itemCats[0] : null;
	}

	/// <summary>
	/// 2.1.0：收藏**按类别分组**的子页签（单位分类 + 物品分类，只列有收藏的）。
	/// 选中后分别走各自的渲染路径（单位列表 / 物品列表），**不做混合列表**——
	/// 单位点击=放置、物品点击=拿起，两种行为混在一屏容易误操作。
	/// </summary>
	private static void RebuildFavTabs()
	{
		favCats.Clear();
		favCatNames.Clear();

		string[] uc = { "infantry", "mgs", "tanks", "wheeled", "planes", "artillery", "modveh" };
		string[] un = { "步兵", "机枪", "坦克", "轮式", "飞机", "火炮", "Mod载具" };
		for (int i = 0; i < uc.Length; i++)
		{
			for (int k = 0; k < GenCatalog.favorites.Count; k++)
				if (GenCatalog.favorites[k].Category == uc[i]) { favCats.Add(uc[i]); favCatNames.Add(Ui.Tr(un[i])); break; }
		}
		foreach (string b in ItemCatalog.BucketOrder)
		{
			if (ItemCatalog.Favs(b).Count > 0) { favCats.Add("item:" + b); favCatNames.Add(Ui.Tr(BucketLabel(b))); }
		}

		if (favCats.Count == 0) { favCat = ""; return; }
		if (string.IsNullOrEmpty(favCat) || !favCats.Contains(favCat)) favCat = favCats[0];
	}

	/// <summary>2.0.1：供物品校验完成后（协程回调）重建页签 —— 剔除无效条目后页签可能消失。</summary>
	internal static void RebuildItemTabsPublic() => RebuildItemTabs();

	/// <summary>2.2.0：供 ModCatalog 目录就绪后重建收藏分类子页签。</summary>
	internal static void RebuildFavTabsPublic() => RebuildFavTabs();

	/// <summary>物品桶的 UI 名（中文原串，交给 Ui.Tr 翻译）。</summary>
	internal static string BucketLabel(string bucket)
	{
		switch (bucket)
		{
			case "weapons": return "武器";
			case "ammo": return "弹药";
			case "throwables": return "投掷物";
			case "gear": return "装备";
			case "food": return "医疗食物";
			case "uniforms": return "服装";
			case "mod": return "Mod物品";
			default: return "其他";
		}
	}

	/// <summary>当前是否物品类别（"item:xxx"）。</summary>
	private static bool IsItemCategory => category != null && category.StartsWith("item:", StringComparison.Ordinal);

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

		// 类别页签（两行排布：收藏 + 步兵/机枪/坦克/轮式/飞机/火炮 + 物品页签族；
		// 1.2.0 用户反馈单行太挤；2.0.0 新增物品族，故改为"单位页签 + 物品页签"两段）
		string[] cats = { "favorites", "infantry", "mgs", "tanks", "wheeled", "planes", "artillery", "modveh" };
		string[] catNames = { Ui.Tr("收藏"), Ui.Tr("步兵"), Ui.Tr("机枪"), Ui.Tr("坦克"), Ui.Tr("轮式"), Ui.Tr("飞机"), Ui.Tr("火炮"), Ui.Tr("Mod载具") };
		float cw = (w - (TabsPerRow - 1f) * 4f) / TabsPerRow;
		for (int i = 0; i < cats.Length; i++)
		{
			int row = i / TabsPerRow, col = i % TabsPerRow;
			bool sel = category == cats[i];
			// 2.4.1：走自适应字号（"Mod Vehicles" 之类的长标签不再溢出压住邻居）
			if (DrawTabButton(new Rect(x + col * (cw + 4f), y + row * (TabH + 4f), cw, TabH), catNames[i], sel))
			{
				category = cats[i];
				page = 0;
				itemSub = ""; letterFilter = "";   // 2.1.0：换类即复位子分类与过滤，避免"切过去是空列表"
				if (category == "favorites") RebuildFavTabs();
			}
		}
		y += 2f * (TabH + 4f);

		// 2.0.0：物品页签族（只列有内容的桶；桶为空则此段不出现）
		if (itemCats.Count > 0)
		{
			float iw = (w - (ItemTabsPerRow - 1f) * 4f) / ItemTabsPerRow;
			int irows = (itemCats.Count + ItemTabsPerRow - 1) / ItemTabsPerRow;
			for (int i = 0; i < itemCats.Count; i++)
			{
				int row = i / ItemTabsPerRow, col = i % ItemTabsPerRow;
				bool sel = category == itemCats[i];
				if (DrawTabButton(new Rect(x + col * (iw + 4f), y + row * (TabH + 4f), iw, TabH), itemCatNames[i], sel))
				{
					category = itemCats[i];
					page = 0;
					itemSub = ""; letterFilter = "";   // 2.1.0：同上
				}
			}
			y += irows * (TabH + 4f);
		}

		// 2.1.0：选中「收藏」时，再列一行**收藏分类**子页签（单位类 + 物品类）
		if (category == "favorites" && favCats.Count > 0)
		{
			float fw = (w - (FavTabsPerRow - 1f) * 4f) / FavTabsPerRow;
			int frows = (favCats.Count + FavTabsPerRow - 1) / FavTabsPerRow;
			for (int i = 0; i < favCats.Count; i++)
			{
				int row = i / FavTabsPerRow, col = i % FavTabsPerRow;
				bool sel = favCat == favCats[i];
				if (DrawTabButton(new Rect(x + col * (fw + 4f), y + row * (TabH + 4f), fw, TabH), favCatNames[i], sel))
				{
					favCat = favCats[i];
					page = 0;
					itemSub = "";
				}
			}
			y += frows * (TabH + 4f);
		}

		// 条目列表（分页制：GUI.BeginScrollView 被游戏裁剪，禁用滚动区；只用 Label/Button/DrawTexture）
		// 2.1.0：收藏分类选中物品类 → 走物品列表（桶名来自 favCat）
		if (category == "favorites" && favCat.StartsWith("item:", StringComparison.Ordinal))
		{
			DrawItemList(x, y, w, favCat.Substring(5), true);   // 收藏分类 → 只要收藏项
			return;
		}
		if (IsItemCategory) { DrawItemList(x, y, w, category.Substring(5), false); return; }

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
				if (GUI.Button(starBtn, star, starStyle)) { GenCatalog.ToggleFav(e); RebuildFavTabs(); }
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

		// 分页行（1.2.1：步兵页可达 50+ 页，Shift+点击一次跳 10 页）
		if (pages > 1)
		{
			bool shiftPg = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
			int step = shiftPg ? 10 : 1;
			if (DrawButton(new Rect(x, y, 30f, 22f), "◀", buttonStyle)) page = Mathf.Max(0, page - step);
			GUI.Label(new Rect(x + 38f, y + 2f, 60f, 20f), (page + 1) + "/" + pages, textStyle);
			if (DrawButton(new Rect(x + w - 30f, y, 30f, 22f), "▶", buttonStyle)) page = Mathf.Min(pages - 1, page + step);
		}
		y += 26f;

		// 乘员行（步兵无乘员选项）。开关都用按钮（GUI.Toggle 有裁剪风险）
		if (category != "infantry")
		{
			string crewLabel = crewMode == 0 ? Ui.Tr("乘员:专用") : crewMode == 1 ? Ui.Tr("乘员:兵班") : Ui.Tr("乘员:无");
			if (DrawButton(new Rect(x, y, 90f, 24f), crewLabel, crewMode != 2 ? activeButtonStyle : buttonStyle))
				crewMode = (crewMode + 1) % 3;
			if (crewMode == 1 && GenCatalog.crewPool.Count > 0)
			{
				// 兵班选择器：‹ 类型 ›（在官方步兵类型池里循环；自定义班不作乘员来源）
				if (infantrySel == null || infantrySel.SpawnByKey) infantrySel = GenCatalog.crewPool[0];
				int idx = GenCatalog.crewPool.FindIndex(e => e.Id == infantrySel.Id);
				if (idx < 0) idx = 0;
				if (DrawButton(new Rect(x + 94f, y, 22f, 24f), "‹", buttonStyle))
					infantrySel = GenCatalog.crewPool[(idx - 1 + GenCatalog.crewPool.Count) % GenCatalog.crewPool.Count];
				GUI.Label(new Rect(x + 118f, y + 3f, 110f, 20f), infantrySel.Title, textStyle);
				if (DrawButton(new Rect(x + 230f, y, 22f, 24f), "›", buttonStyle))
					infantrySel = GenCatalog.crewPool[(idx + 1) % GenCatalog.crewPool.Count];
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

	// ══════════════════ 2.0.0：物品页签 ══════════════════

	/// <summary>
	/// 物品列表：与单位列表同布局，但条目**点击即拿起**（进入携带模式），
	/// 之后拖到单位身上进背包、拖到地上生成实体（ItemDragger）。
	/// 行左侧画小图标（有则画，取不到不阻塞——图标是异步链，先出名字）。
	/// </summary>
	private static void DrawItemList(float x, float y, float w, string bucket, bool favOnly)
	{
		// 2.1.0：子分类页签（只列该桶实际存在的子分类；判定来自游戏官方字段，不猜名字）
		List<string> subs = ItemCatalog.SubsOf(bucket, favOnly);
		if (subs.Count > 0)
		{
			int n = subs.Count + 1; // +「全部」
			float sw = (w - (n - 1f) * 4f) / n;
			if (DrawTabButton(new Rect(x, y, sw, TabH), Ui.Tr("全部"), itemSub == ""))
			{ itemSub = ""; page = 0; }
			for (int i = 0; i < subs.Count; i++)
			{
				bool sel = itemSub == subs[i];
				// 2.3.0：SubLabel 返回中文原串，**必须过 Ui.Tr**——2.2.0 加「可穿戴」子分类时
				// 这里漏了包装 → 英文版按钮直接显示中文（用户实测截图实锤）。BucketLabel 的教训同款。
				if (DrawTabButton(new Rect(x + (i + 1) * (sw + 4f), y, sw, TabH), Ui.Tr(SubLabel(subs[i])), sel))
				{ itemSub = subs[i]; page = 0; }
			}
			y += TabH + 4f;
		}

		// 2.1.1：首字母索引行（**替代 GUI.TextField**——它在 IL2CPP 下"Method unstripping failed"，
		// 一抛异常整帧 OnGUI 中断 = 用户看到的"列表全空"。字母行纯点击，零键盘依赖）
		List<string> letters = ItemCatalog.LettersOf(bucket, itemSub, favOnly);
		if (letters.Count > 2)
		{
			// 固定按钮宽 + 自动换行：字母最多 30+ 个，挤单行会窄到不可点。
			// 2.2.0：**「全部」占两格**（2.1.1 让它宽 34px 越过 27px 网格间距 → 压住相邻按钮，
			// 用户实测"菜单按钮有重叠"）；字母从**第 3 格（i>=2）**起排——
			// ⚠️ 2.2.0 首发把 else 写成从 i==1 就取 letters[i-2] → i=1 时 letters[-1] 抛
			// ArgumentOutOfRangeException → 整帧 OnGUI 中断 → 字母与物品列表全消失
			//（用户实测截图：子分类页签 + 孤零零一个「全部」，其余全空）。2.2.1 修复。
			const float LBW = 24f, LGap = 3f;
			int per = Mathf.Max(3, Mathf.FloorToInt((w + LGap) / (LBW + LGap)));
			int cells = letters.Count + 2; // +「全部」占两格
			for (int i = 0; i < cells; i++)
			{
				int row = i / per, col = i % per;
				float bx = x + col * (LBW + LGap);
				float by = y + row * 22f;
				if (i == 0)
				{
					bool allSel = string.IsNullOrEmpty(letterFilter);
					if (DrawButton(new Rect(bx, by, LBW * 2f + LGap, 20f), Ui.Tr("全部"), allSel ? activeButtonStyle : buttonStyle))
					{ letterFilter = ""; page = 0; }
				}
				else if (i >= 2)
				{
					string lt = letters[i - 2];
					bool sel = string.Equals(letterFilter, lt, StringComparison.OrdinalIgnoreCase);
					if (DrawButton(new Rect(bx, by, LBW, 20f), lt, sel ? activeButtonStyle : buttonStyle))
					{ letterFilter = lt; page = 0; }
				}
			}
			int rows = (cells + per - 1) / per;
			y += rows * 22f + 2f;
		}
		else if (!string.IsNullOrEmpty(letterFilter))
		{
			letterFilter = ""; // 字母行消失时（如子分类变了）自动复位，避免空列表
		}

		List<ItemEntry> all = ItemCatalog.Query(bucket, itemSub, letterFilter, favOnly);

		ItemEntry[] snap;
		try { snap = all.ToArray(); }
		catch { snap = Array.Empty<ItemEntry>(); }

		int pages = Mathf.Max(1, Mathf.CeilToInt(snap.Length / (float)VisibleRows));
		if (page >= pages) page = pages - 1;
		if (page < 0) page = 0;
		float listH = VisibleRows * RowH + 8f;

		GUI.color = new Color(0f, 0f, 0f, 0.35f);
		GUI.DrawTexture(new Rect(x, y, w, listH), Texture2D.whiteTexture);
		GUI.color = Color.white;

		if (snap.Length == 0)
		{
			GUI.Label(new Rect(x + 8f, y + 6f, w - 16f, 22f), Ui.Tr("（无匹配条目）"), textStyle);
		}
		else
		{
			int from = page * VisibleRows;
			int to = Mathf.Min(from + VisibleRows, snap.Length);
			ItemEntry clicked = null;
			for (int i = from; i < to; i++)
			{
				ItemEntry e = snap[i];
				float rowY = y + 4f + (i - from) * RowH;
				// 图标（36px 见方，行左侧；图标缺失时该位置空着，不影响点击）
				Texture2D tex = IconTexFor(e.Id);
				if (tex != null)
				{
					DrawTexFit(tex, new Rect(x + 6f, rowY + 1f, RowH - 4f, RowH - 4f), 0.95f);
				}
				float tx = tex != null ? x + 6f + RowH : x + 6f;
				float tw = w - 12f - (tx - x) - 30f;   // 2.1.0：右侧让出星标位
				Color keep = GUI.backgroundColor;
				bool fav = ItemCatalog.IsFav(e.Id);
				GUI.backgroundColor = fav ? new Color(0.4f, 0.58f, 0.4f, 0.95f) : new Color(0.12f, 0.2f, 0.12f, 0.85f);
				if (GUI.Button(new Rect(tx, rowY, tw, RowH - 2f), e.Title, rowStyle)) clicked = e;
				GUI.backgroundColor = keep;

				// 2.1.0：收藏星标（与单位行一致：透明底，仅星形变色）
				Rect starBtn = new Rect(x + w - 28f, rowY + 1f, 26f, RowH - 4f);
				bool starHover = starBtn.Contains(Event.current.mousePosition);
				Color keepTxt = GUI.contentColor;
				GUI.contentColor = fav ? new Color(1f, 0.85f, 0.3f, 1f)
					: (starHover ? new Color(0.8f, 0.9f, 0.8f, 0.95f) : new Color(0.55f, 0.65f, 0.55f, 0.75f));
				if (GUI.Button(starBtn, fav ? "★" : "☆", starStyle))
				{
					ItemCatalog.ToggleFav(e);
					RebuildFavTabs();   // 收藏分类页签随之增减
				}
				GUI.contentColor = keepTxt;
			}
			if (clicked != null)
			{
				Event.current.Use();
				BeginCarry(clicked);
				return;
			}
		}
		y += listH + 4f;

		if (pages > 1)
		{
			bool shiftPg = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
			int step = shiftPg ? 10 : 1;
			if (DrawButton(new Rect(x, y, 30f, 22f), "◀", buttonStyle)) page = Mathf.Max(0, page - step);
			GUI.Label(new Rect(x + 38f, y + 2f, 90f, 20f), (page + 1) + "/" + pages + "  (" + snap.Length + ")", textStyle);
			if (DrawButton(new Rect(x + w - 30f, y, 30f, 22f), "▶", buttonStyle)) page = Mathf.Min(pages - 1, page + step);
		}
		y += 26f;

		GUI.Label(new Rect(x, y, w, 34f),
			Ui.Tr("点击条目拿起 → 拖到单位身上放入背包，拖到地上则生成实体"), textStyle);
	}

	/// <summary>2.1.0：子分类的 UI 名（中文原串，交给 Ui.Tr 翻译）。</summary>
	private static string SubLabel(string sub)
	{
		switch (sub)
		{
			case "rifle": return "步枪";
			case "pistol": return "手枪";
			case "wearable": return "可穿戴";
			default: return sub;
		}
	}

	/// <summary>拿起物品 → 进入携带模式（收起面板 + 全屏手势互斥，均由 ItemDragger 统一负责）。</summary>
	private static void BeginCarry(ItemEntry e)
	{
		ItemDragger.Begin(e); // Begin 内部已 SetOpen(false)
	}

	// ── 物品图标链（2.0.0）──
	// 终案路径与宿主 BackpackPanel 1.3.4 一致：解析 Sprite → **立刻光栅化成自建 Texture2D**
	// （游戏图标是图集子区域，直接持有 Sprite 会 "garbage collected in IL2CPP domain"；
	//  自建贴图 + hideFlags=61 是本工作区全程验证过的绘制路径）。
	// 但本 mod **不在 OnGUI 里做 GPU 光栅化**——面板每帧重绘，避免打断 IMGUI 绘制状态；
	// 改为**惰性后台协程**逐个解析，就绪后直接画。

	private static readonly Dictionary<string, Texture2D> iconTex = new Dictionary<string, Texture2D>();
	private static readonly HashSet<string> iconTried = new HashSet<string>();
	private static readonly List<Texture2D> iconKeepAlive = new List<Texture2D>();

	/// <summary>取图标（已光栅化的自建贴图）。未就绪返回 null 并排队异步解析。</summary>
	internal static Texture2D IconTexFor(string id)
	{
		if (string.IsNullOrEmpty(id)) return null;
		if (iconTex.TryGetValue(id, out Texture2D t) && t != null) return t;
		QueueIcon(id);
		return null;
	}

	/// <summary>排队解析（每 id 一次；后台协程分帧做，主线程零阻塞）。</summary>
	private static void QueueIcon(string id)
	{
		if (iconTried.Contains(id)) return;
		iconTried.Add(id);
		GenRunner.StartCoroutine(IconCR(id));
	}

	private static System.Collections.IEnumerator IconCR(string id)
	{
		yield return null; // 让出本帧（本协程由点击触发，避免与当前 IMGUI 事件同帧竞争）
		Sprite sp = ResolveIcon(id);
		if (sp != null)
		{
			Texture2D tex = Rasterize(sp);
			if (tex != null)
			{
				tex.hideFlags = (HideFlags)61; // 陷阱 12：运行时贴图防场景卸载
				iconTex[id] = tex;
				iconKeepAlive.Add(tex);
			}
			else if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 图标光栅化失败: " + id);
		}
		else if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 图标未命中: " + id);
	}

	/// <summary>三级解析链：prefab.icon → 全局 sprite 缓存 → er2gui bundle 按名。</summary>
	private static Sprite ResolveIcon(string id)
	{
		// ① 官方 prefab 的 icon 字段
		try
		{
			ItemObject prefab = ItemsDatabase.GetItemObject(id);
			if (prefab != null)
			{
				Sprite ic = null;
				try { ic = prefab.icon; } catch { }
				if (ic != null) return ic;
			}
		}
		catch { }

		// ② 全局 sprite 缓存（游戏加载过的图标都在这里）
		try
		{
			var cached = ItemsDatabase.cachedLoadedSprites;
			if (cached != null)
			{
				if (cached.TryGetValue(id, out Sprite s1) && s1 != null) return s1;
				foreach (Sprite v in cached.Values)
				{
					try { if (v != null && v.name == id) return v; } catch { }
				}
			}
		}
		catch { }

		// ③ er2gui bundle 按名直取（试 id 与常见后缀）
		string[] tries = { id, id + "_icon", "icon_" + id };
		for (int i = 0; i < tries.Length; i++)
		{
			try
			{
				Sprite s = ItemsDatabase.LoadAndCacheSprite(tries[i], "er2gui");
				if (s != null) return s;
			}
			catch { }
		}
		return null;
	}

	/// <summary>Sprite 图集子区域 → 自建 Texture2D（GPU blit + ReadPixels，源图不必可读）。</summary>
	private static Texture2D Rasterize(Sprite sp)
	{
		Texture2D outTex = null;
		RenderTexture rt = null;
		try
		{
			Texture src = sp.texture;
			Rect tr = sp.textureRect;
			if (src == null || tr.width < 2f || tr.height < 2f) return null;
			int w = Mathf.Clamp(Mathf.CeilToInt(tr.width), 2, 128);
			int h = Mathf.Clamp(Mathf.CeilToInt(tr.height), 2, 128);
			outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
			rt = RenderTexture.GetTemporary((int)src.width, (int)src.height, 0);
			Graphics.Blit(src, rt);
			RenderTexture.active = rt;
			outTex.ReadPixels(new Rect(tr.x, tr.y, tr.width, tr.height), 0, 0);
			outTex.Apply(false, false);
			RenderTexture.active = null;
			RenderTexture.ReleaseTemporary(rt);
			return outTex;
		}
		catch (Exception ex)
		{
			if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] Rasterize 异常: " + ex.Message);
			if (rt != null) { try { RenderTexture.ReleaseTemporary(rt); } catch { } }
			try { if (outTex != null) UnityEngine.Object.Destroy(outTex); } catch { }
			return null;
		}
	}

	/// <summary>保纵横比居中绘制自建贴图（只用 GUI.DrawTexture，本工作区验证过的路径）。</summary>
	private static void DrawTexFit(Texture2D tex, Rect into, float alpha)
	{
		try
		{
			float asp = tex.width / (float)tex.height;
			float w = into.width, h = into.height;
			if (w / h > asp) w = h * asp; else h = w / asp;
			Rect fit = new Rect(into.x + (into.width - w) * 0.5f, into.y + (into.height - h) * 0.5f, w, h);
			Color keep = GUI.color;
			GUI.color = new Color(1f, 1f, 1f, alpha);
			GUI.DrawTexture(fit, tex, ScaleMode.ScaleToFit, true);
			GUI.color = keep;
		}
		catch { }
	}

	// ── 落点模式启动 ──
	private static void BeginPlacement(GenEntry entry)
	{		if (recentIds.Contains(entry.Id)) recentIds.Remove(entry.Id);
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
		else if (infantrySel == null && GenCatalog.crewPool.Count > 0) infantrySel = GenCatalog.crewPool[0];

		SetOpen(false); // 面板完全收起：放置期间 IsOpen=false，点击不再被 guiNow 吞掉
		Placer.Begin(entry, fac, faction, ResolveCrewType(entry, fac));
	}

	/// <summary>载具乘员班型：0=国别专用坦克组员 1=所选步兵类型 2=无。步兵条目/自定义班恒 null（SType 无意义）。</summary>
	private static SquadType? ResolveCrewType(GenEntry entry, string fac)
	{
		if (entry.IsInfantry || crewMode == 2) return null;
		if (crewMode == 0) return GenCatalog.TankCrewTypeFor(fac);
		return infantrySel != null && infantrySel.IsInfantry && !infantrySel.SpawnByKey ? infantrySel.SType : (SquadType?)null;
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
		// 2.4.0：高度**按内容实算**——旧版固定公式只算了"单位类别"的布局，
		// 物品页签族 / 收藏子页签 / 物品子分类行 / 字母索引行 / 物品帮助行全都没计入 →
		// 物品页的列表下半段、分页行与帮助行画到面板背景外（用户实测截图实锤）。
		// ⚠️ 本函数 + ItemListHeight 必须与 OnGUI / DrawItemList 的 y 累加**逐项对齐**：
		// 任何一处加行，另一处必须同步（与 GUI 布局镜像修改是同一纪律）。
		float h = 8f                              // 顶部留白（OnGUI: y = r.y + 8f）
				+ 26f                             // 标题行
				+ 36f                             // 阵营行
				+ 2f * (TabH + 4f);               // 单位页签固定两行
		if (itemCats.Count > 0)
			h += Mathf.CeilToInt(itemCats.Count / (float)ItemTabsPerRow) * (TabH + 4f);
		if (category == "favorites" && favCats.Count > 0)
			h += Mathf.CeilToInt(favCats.Count / (float)FavTabsPerRow) * (TabH + 4f);

		if (category == "favorites" && favCat.StartsWith("item:", StringComparison.Ordinal))
			h += ItemListHeight(favCat.Substring(5), true);
		else if (IsItemCategory)
			h += ItemListHeight(category.Substring(5), false);
		else
			h += (VisibleRows * RowH + 8f)        // 列表区
			   + 4f + 26f                         // 列表下间隙 + 分页行（OnGUI 恒累计，与是否绘制无关）
			   + 32f                              // 乘员行（步兵不绘制但 y 照样累计）
			   + 24f                              // 阵营预览行
			   + 8f;                              // 底部留白
		return new Rect(panelPos.x, panelPos.y, PanelW, h);
	}

	/// <summary>
	/// 2.4.0：DrawItemList 的高度镜像。w 取 PanelW - 20f（OnGUI 里 x = r.x + 10f）。
	/// 子分类行/字母索引行的有无由 SubsOf / LettersOf 的真实结果决定——与绘制逻辑同一判据，
	/// 不重复猜。每帧两次线性扫描的成本可忽略（DrawItemList 本身每帧还在做 Query）。
	/// </summary>
	private static float ItemListHeight(string bucket, bool favOnly)
	{
		float h = 0f;

		List<string> subs = ItemCatalog.SubsOf(bucket, favOnly);
		if (subs.Count > 0) h += TabH + 4f;

		List<string> letters = ItemCatalog.LettersOf(bucket, itemSub, favOnly);
		if (letters.Count > 2)
		{
			const float LBW = 24f, LGap = 3f;   // 与 DrawItemList 字母行同一常量
			int per = Mathf.Max(3, Mathf.FloorToInt(((PanelW - 20f) + LGap) / (LBW + LGap)));
			int cells = letters.Count + 2;      // +「全部」占两格
			h += Mathf.CeilToInt(cells / (float)per) * 22f + 2f;
		}

		h += VisibleRows * RowH + 8f            // 列表区
		   + 4f + 26f                           // 列表下间隙 + 分页行（恒累计）
		   + 34f                                // 帮助行
		   + 8f;                                // 底部留白
		return h;
	}

	private static List<GenEntry> FilteredBucket()
	{
		// 2.1.0：收藏 + 选中单位分类 → 只列该分类的收藏
		if (category == "favorites" && !string.IsNullOrEmpty(favCat)
			&& !favCat.StartsWith("item:", StringComparison.Ordinal))
		{
			var res = new List<GenEntry>();
			foreach (GenEntry e in GenCatalog.favorites)
				if (e.Category == favCat) res.Add(e);
			return res;
		}
		return GenCatalog.GetBucket(category);
	}

	private static bool DrawButton(Rect r, string label, GUIStyle style)
	{
		bool clicked = GUI.Button(r, label, style);
		return clicked;
	}

	// ══════════ 2.4.1：定宽页签的自适应字号（陷阱 76）══════════
	//
	// 病灶：页签是**定宽网格**（PanelW=320 → 每格 ≈72px），而字号固定 12。
	// 英文 "Mod Vehicles"（12 个拉丁字符）在 12 号下约 78~84px > 72px →
	// IMGUI 的文字按居中绘制、不会被按钮矩形裁掉 → 直接压到相邻页签上，
	// 用户看到的就是"页签有重叠、显示不完整"。中文 "Mod载具" 反而放得下，
	// 所以这个问题只在英文版暴露（与 2.3.0 的"英文版有中文"同一类：只测了一种语言）。
	//
	// 定案：**逐格算字号**——按字符宽度系数估宽（CJK≈1.0em，拉丁≈0.56em），
	// 取能塞进按钮的最大字号（下限 8）。
	// ⚠️ 不能用 `GUIStyle.CalcSize` 之外的方案都行，但**绝不能 `new GUIStyle(style)`**
	// （陷阱 5：拷贝构造被 IL2CPP 裁剪）→ 只能用专用样式实例改写 fontSize。
	private static int TabFontSize(string text, float w)
	{
		if (string.IsNullOrEmpty(text)) return 12;
		float unit = 0f;
		for (int i = 0; i < text.Length; i++)
			unit += text[i] > (char)0x2E80 ? 1f : 0.56f;   // CJK/全角 vs 拉丁
		if (unit <= 0f) return 12;
		return Mathf.Clamp(Mathf.FloorToInt((w - 6f) / unit), 8, 12);   // -6 = 左右内边距
	}

	private static bool DrawTabButton(Rect r, string text, bool active)
	{
		GUIStyle st = active ? tabActiveStyle : tabStyle;
		if (st == null) return DrawButton(r, text, active ? activeButtonStyle : buttonStyle);
		st.fontSize = TabFontSize(text, r.width);
		return DrawButton(r, text, st);
	}

	public static void DrawPlacingBadge()
	{
		// 底部居中窄条（宿主提示条上方），不遮战场
		Rect r = BadgeRect();
		GUI.color = new Color(0.02f, 0.05f, 0.02f, 0.78f);
		GUI.DrawTexture(r, Texture2D.whiteTexture);
		GUI.color = Color.white;
		string txt = Ui.Tr("放置: ") + (Placer.PendingEntry()?.Title ?? "?")
			+ Ui.Tr("    左键 放置 · 左键长按拖动 旋转朝向 · Shift 连续 · 右键 取消");
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

	/// <summary>RTS 的"生成"开关按钮（仅面板关闭时显示；点击=G）。
	/// 1.1.1：从左下角移到左缘中段（用户要求：左下角让位给战场指挥官的选中单位信息面板）。</summary>
	private static Rect ToggleButtonRect()
	{
		return new Rect(12f, Screen.height * 0.42f, 120f, 26f);
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

	/// <summary>2.0.0：供 ItemDragger 复用的样式访问（携带徽标/光标名签）。</summary>
	internal static void EnsureStylesPublic() => EnsureStyles();

	/// <summary>2.0.0：供 ItemDragger 复用的提示样式（携带徽标、光标名签）。</summary>
	internal static GUIStyle FlashStylePublic() { EnsureStyles(); return flashStyle; }

	private static void EnsureStyles()
	{
		if (titleStyle != null) return;		// 陷阱 5：GUIStyle 拷贝构造被 IL2CPP 裁剪——全部 new GUIStyle() + 显式字段；normal.textColor 必须显式
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

		// 2.4.1：页签样式（字号由 DrawTabButton 逐次改写，故必须单独一份）
		tabStyle = MakeSolidButton(12, baseBg, text, FontStyle.Normal);
		tabActiveStyle = MakeSolidButton(12, hoverBg, new Color(0.04f, 0.09f, 0.04f, 1f), FontStyle.Bold);

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
