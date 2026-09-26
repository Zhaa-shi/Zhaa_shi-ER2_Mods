using System;
using System.Collections.Generic;
using ER2Shared;
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
	// 2.5.1：**改成属性**——原本是 `const float PanelW = Er2Ui.PanelW`，而 Er2Ui 的令牌
	// 已随 Scale 联动（自适应屏幕分辨率），const 在编译期就把值钉死，自适应会失效。
	// （const 也不能引用属性，所以这里必须是 static 属性。）
	private static float PanelW => Er2Ui.PanelW;  // 随分辨率自适应（1.0 倍下 = 320）
	private static float RowH => Er2Ui.RowH;       // 28（1.2.0：26→28，用户反馈"太密"）
	private static float TabH => Er2Ui.TabH;       // 24
	private const int VisibleRows = 10;
	private const int TabsPerRow = 4;    // 1.2.0：8 个页签两行排（一行塞 7 个太挤）
	private const int ItemTabsPerRow = 4; // 2.0.0：物品页签族同样两行排

	// 2.5.1：字母索引行的尺寸原本在**两处各写一遍**（BuildRows 算高度 / DrawLetters 去画），
	// 是陷阱 78 典型的"两处必须一致"——现在收成一份，自适应时也只改这里。
	private static float LetterBtnW => 24f * Er2Ui.Scale;
	private static float LetterGap => 3f * Er2Ui.Scale;
	private static float LetterRowH => 22f * Er2Ui.Scale;
	private static float LetterBtnH => 20f * Er2Ui.Scale;

	// 单位类别页签：显示名在 EnsureStyles 里过一次 Ui.Tr（避免每帧翻译 + 每帧分配数组）
	private static readonly string[] UnitCats = { "favorites", "infantry", "mgs", "tanks", "wheeled", "planes", "artillery", "modveh" };
	private static readonly string[] UnitCatRaw = { "收藏", "步兵", "机枪", "坦克", "轮式", "飞机", "火炮", "Mod载具" };
	private static readonly string[] UnitCatNames = new string[UnitCats.Length];

	private static GUIStyle titleStyle, textStyle, buttonStyle, activeButtonStyle, rowStyle, flashStyle, starStyle;
	// 1.4.30：列表行**文字**专用样式（行底由 rowStyle 的空按钮画，文字单独描边双绘）
	private static GUIStyle rowTextStyle;

	// 2.4.2：页签专用样式（**只给页签用**，字号会被逐次改写以适配定宽按钮；
	// 不能复用 buttonStyle/activeButtonStyle——它们被别处以固定 12 号使用）
	private static GUIStyle tabStyle, tabActiveStyle;
	// 1.4.27：帮助行**专用**样式——它需要按可用宽度单独缩字号（陷阱 76 同源）
	private static GUIStyle helpStyle;

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

	// 2.4.2：行计划（布局单一数据源）+ 分页上下文（List 行写、Pager 行读——两者必定相邻）
	private static readonly List<Row> rows = new();
	private static int curPages = 1, curTotal;

	// ══════════ 2.4.2：布局单一数据源（行计划）══════════
	//
	// 陷阱 75 的**结构性**修法：高度不再另算一遍。
	// 2.4.0 曾把"高度公式"与"绘制的 y 累加"逐项对齐——但那是靠人记住两处同步，
	// 加一行 UI 就再踩一次。现在：**先排这一帧要画哪些行（BuildRows），高度 = 各行高之和**，
	// 绘制只按计划逐行取 Rect —— 二者消费同一份列表，漂移在结构上不可能发生。
	private enum RowKind { Title, Faction, UnitTabs, ItemTabs, SubTabs, FavCrumb, Letters, List, Pager, Crew, Preview, ItemHelp }

	private struct Row
	{
		public RowKind Kind;
		public float H;
	}

	/// <summary>当前是否物品分支；是则给出桶名与"只要收藏"。</summary>
	private static bool ItemBranch(out string bucket, out bool favOnly)
	{
		bucket = null; favOnly = false;
		if (category == "favorites" && !string.IsNullOrEmpty(favCat) && favCat.StartsWith("item:", StringComparison.Ordinal))
		{ bucket = favCat.Substring(5); favOnly = true; return true; }
		if (IsItemCategory) { bucket = category.Substring(5); favOnly = false; return true; }
		return false;
	}

	private static void BuildRows()
	{
		rows.Clear();
		// 2.5.2：固定行高也要乘 Scale（此前漏了 → 自适应下标题/阵营/分页/预览行不跟着缩放）
		// 1.4.23：26 → 34 = 8(上留白) + 22(标题行按钮) + 4(下留白)，
		// 原 26 装不下"从 +8 开始、高 22"的按钮 → 按钮底部越过分隔线，压出"最上面的按钮重叠"。
		rows.Add(new Row { Kind = RowKind.Title, H = 34f * Er2Ui.Scale });
		rows.Add(new Row { Kind = RowKind.Faction, H = 36f * Er2Ui.Scale });
		rows.Add(new Row { Kind = RowKind.UnitTabs, H = Er2Ui.TabGridH(UnitCats.Length, TabsPerRow, TabH) });
		if (itemCats.Count > 0)
			rows.Add(new Row { Kind = RowKind.ItemTabs, H = Er2Ui.TabGridH(itemCats.Count, ItemTabsPerRow, TabH) });

		// 1.4.28：收藏**条目**视图（已进子文件夹）时，插一行面包屑「◀ 收藏 / 分类名」
		if (category == "favorites" && !string.IsNullOrEmpty(favCat))
			rows.Add(new Row { Kind = RowKind.FavCrumb, H = Er2Ui.BtnH + Er2Ui.Gap });

		if (ItemBranch(out string bucket, out bool favOnly))
		{
			if (ItemCatalog.SubsOf(bucket, favOnly).Count > 0)
				rows.Add(new Row { Kind = RowKind.SubTabs, H = TabH });

			List<string> letters = ItemCatalog.LettersOf(bucket, itemSub, favOnly);
			if (letters.Count > 2)
			{
				int per = Mathf.Max(3, Mathf.FloorToInt(((PanelW - 20f * Er2Ui.Scale) + LetterGap) / (LetterBtnW + LetterGap)));
				int lr = (letters.Count + 2 + per - 1) / per;   // +2：「全部」占两格
				rows.Add(new Row { Kind = RowKind.Letters, H = lr * LetterRowH + 2f * Er2Ui.Scale });
			}
			else if (!string.IsNullOrEmpty(letterFilter))
			{
				letterFilter = "";   // 字母行消失时（如子分类变了）自动复位，避免空列表
			}

			rows.Add(new Row { Kind = RowKind.List, H = VisibleRows * RowH + 8f * Er2Ui.Scale });
			rows.Add(new Row { Kind = RowKind.Pager, H = 26f * Er2Ui.Scale });
			// 1.4.28：**两行高**（38 → 56）+ 样式 wordWrap——文案再长也只换行，绝不横向溢出。
		// 前两轮都在"缩字号/缩短文案"上打转，换行才是根治（窄面板 + 长英文的组合无法靠缩字号解决）。
		rows.Add(new Row { Kind = RowKind.ItemHelp, H = 56f * Er2Ui.Scale });
		}
		else
		{
			rows.Add(new Row { Kind = RowKind.List, H = VisibleRows * RowH + 8f * Er2Ui.Scale });
			rows.Add(new Row { Kind = RowKind.Pager, H = 26f * Er2Ui.Scale });
			rows.Add(new Row { Kind = RowKind.Crew, H = 32f * Er2Ui.Scale });
			rows.Add(new Row { Kind = RowKind.Preview, H = 24f * Er2Ui.Scale });
		}
	}

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

		// 1.4.28：**不再自动跳进第一个分类**——favCat=="" 表示"停在主文件夹"，
		// 用户要求"打开主文件夹后显示（各分类）子文件夹"。
		if (favCats.Count == 0) { favCat = ""; return; }
		if (!string.IsNullOrEmpty(favCat) && !favCats.Contains(favCat)) favCat = "";
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
		// 2.5.1：先按屏幕分辨率刷新自适应倍率，再建样式（顺序反了会用旧字号建样式）
		Er2Ui.AutoScale();
		EnsureStyles();

		// 落点模式时面板缩成提示徽标
		if (Placer.Placing) { DrawPlacingBadge(); return; }

		Rect r = PanelRect();

		// 标题栏拖动（IMGUI 标准模式；拖动区不含关闭按钮）
		Rect titleHit = new Rect(r.x, r.y, r.width - 100f * Er2Ui.Scale, 26f * Er2Ui.Scale);
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
			// 2.5.4：**拖拽后取整**——panelPos 带小数时整个面板的文字都画在亚像素上被模糊成灰字
			panelPos.x = Mathf.Round(panelPos.x);
			panelPos.y = Mathf.Round(panelPos.y);
			Event.current.Use();
		}
		else if (dragging && (Event.current.type == EventType.MouseUp || Event.current.rawType == EventType.MouseUp))
		{
			dragging = false;
			Event.current.Use();
		}

		// 1.4.23：**近黑皮革底**（纯色 Fill → PanelBase：底色 + 皮革噪声 + 顶部受光边）
		Er2Ui.PanelBase(r);

		// 2.5.2 **设计感三件套**（用户："只有单一的色块没有设计感"）：
		// ① 标题条独立底色，把"标题"从内容里分出来；② 标题条下分隔线；③ 面板外框。
		// 此前整块面板只有一种底色，全靠明度差撑层次 → 在亮背景战场上一压就糊。
		float s = Er2Ui.Scale;
		BuildRows();
		float titleH = rows.Count > 0 ? rows[0].H : 26f * s;
		Er2Ui.Fill(new Rect(r.x, r.y, r.width, titleH), Er2Ui.TitleBar);
		Er2Ui.Leather(new Rect(r.x, r.y, r.width, titleH), 0.14f);   // 1.4.23：标题条皮革
		Er2Ui.HLine(new Rect(r.x, r.y + titleH, r.width, Mathf.Max(1f, s)), Er2Ui.Edge);

		// 2.4.2：绘制**只消费行计划**——高度与绘制同源（见 BuildRows），加行只需改一处
		DrawRows(r);

		// 1.4.25：外框加粗到 ~2px——半透明面板在亮背景（石头/水泥地）上边界会被吃掉，
		// 用户反馈"对比不明显"，一圈更亮的粗边框是最直接的"面板到此为止"信号
		Er2Ui.Frame(r, Er2Ui.PanelBorder, Mathf.Max(1.5f, 2f * s));   // 最后画，不被内容覆盖
	}

	/// <summary>按行计划逐行取 Rect 绘制（每行只拿到自己的 Rect，不再自行累加 y）。</summary>
	private static void DrawRows(Rect r)
	{
		BuildRows();
		float x = r.x + Er2Ui.Pad, w = r.width - Er2Ui.Pad * 2f;
		float y = r.y + 8f * Er2Ui.Scale;
		bool item = ItemBranch(out string bucket, out bool favOnly);

		for (int i = 0; i < rows.Count; i++)
		{
			Row row = rows[i];
			Rect rect = new Rect(x, y, w, row.H);
			bool stop = false;
			switch (row.Kind)
			{
				case RowKind.Title: stop = DrawTitleRow(rect, r); break;
				case RowKind.Faction: DrawFactionRow(rect); break;
				case RowKind.UnitTabs: DrawUnitTabs(rect); break;
				case RowKind.ItemTabs: DrawItemTabs(rect); break;
				case RowKind.SubTabs: DrawSubTabs(rect, bucket, favOnly); break;
				case RowKind.Letters: DrawLetters(rect, bucket, favOnly); break;
				case RowKind.FavCrumb: DrawFavCrumb(rect); break;
				case RowKind.List:
					// 1.4.28：**收藏主文件夹视图**——favCat 为空时列出各分类子文件夹，
					// 而不是直接铺条目（用户："收藏改为每一个生成文件夹的子文件夹，在打开主文件夹后显示"）
					if (category == "favorites" && string.IsNullOrEmpty(favCat)) stop = DrawFavFolderList(rect);
					else stop = item ? DrawItemListBody(rect, bucket, favOnly) : DrawUnitListBody(rect);
					break;
				case RowKind.Pager: DrawPager(rect, item); break;
				case RowKind.Crew: DrawCrewRow(rect); break;
				case RowKind.Preview: DrawPreviewRow(rect); break;
				case RowKind.ItemHelp:
					{
						// 1.4.28：换行 + 中左对齐（原来是 MiddleLeft，两行时会整体偏下）
						string helpTxt = Ui.Tr("点击条目拿起 → 拖到单位身上放入背包，拖到地上则生成实体");
						helpStyle.alignment = TextAnchor.UpperLeft;
						Rect hr = new Rect(rect.x, rect.y, rect.width, rect.height);
						hr.x = Mathf.Round(hr.x); hr.y = Mathf.Round(hr.y);   // 2.5.27：整数对齐
						Er2Ui.LabelShadowed(hr, helpTxt, helpStyle, Er2Ui.Text);   // 2.5.31：阴影收编
					}
					break;
			}
			if (stop) return;      // 点条目进入放置/携带 → 本帧到此为止（原 return 语义）
			// 1.4.22：**行间统一间距**——此前只有部分行内嵌 Gap，Crew/Preview 贴在一起
			//（用户反馈"背景与文字的重叠"）。间距只在行循环里加一处，行高定义保持纯粹。
			y += row.H + (i + 1 < rows.Count ? Er2Ui.Gap : 0f);
		}
	}

	// ── 逐行绘制 ──

	private static bool DrawTitleRow(Rect rect, Rect panel)
	{
		// 1.4.22：全部 × Scale（原为硬编码 100/24/62/22——UI 缩放后标题与按钮错位）
		// 2.5.27：三个矩形全部**整数对齐**（2.5.4 亚像素模糊根因——裸 GUI.Label/Button 不经过
		// LabelShadowed 的对齐，Scale≈0.998 之类非整数倍率下坐标带小数 = 文字发灰）
		// 2.5.31：**全部文字收编 LabelShadowed**（2.5.28 探针证实状态纯白仍灰 → 灰 = 无阴影衬底的
		// 对比度问题，与页签同配方后全面板观感一致）
		float ts = Er2Ui.Scale;
		GUI.color = Color.white; GUI.contentColor = Color.white; GUI.backgroundColor = Color.white;
		Rect tr = new Rect(rect.x, rect.y, rect.width - 100f * ts, 24f * ts);
		tr.x = Mathf.Round(tr.x); tr.y = Mathf.Round(tr.y);
		Er2Ui.LabelShadowed(tr, Ui.Tr("通用生成"), titleStyle, Er2Ui.Text);
		Rect clearR = new Rect(panel.xMax - (34f + 62f) * ts, rect.y, 62f * ts, 22f * ts);
		clearR.x = Mathf.Round(clearR.x); clearR.y = Mathf.Round(clearR.y);
		if (GUI.Button(clearR, GUIContent.none, buttonStyle))
		{
			int n = GenRunner.ClearAllSpawned();
			Flash(Ui.Tr("已清除 ") + n + Ui.Tr(" 个生成物"));
		}
		Er2Ui.LabelShadowed(clearR, Ui.Tr("清除"), buttonStyle, Er2Ui.Text);
		Rect xR = new Rect(panel.xMax - 8f * ts - 22f * ts, rect.y, 22f * ts, 22f * ts);
		xR.x = Mathf.Round(xR.x); xR.y = Mathf.Round(xR.y);
		if (GUI.Button(xR, GUIContent.none, buttonStyle))
		{
			SetOpen(false);
			return true;
		}
		Er2Ui.LabelShadowed(xR, "×", buttonStyle, Er2Ui.Text);
		return false;
	}

	private static void DrawFactionRow(Rect rect)
	{
		// 1.4.22：× Scale + 未选中项加暖棕描边（用户："UI 各元素区分不明显"）
		// 2.5.27：按钮矩形**整数对齐**——third = 宽度/3 除不尽 → 阵营行文字亚像素模糊发灰
		//（用户实测：Allies 整数位 255 纯白、Neutral 分数位 229 灰），文字描边由按钮样式自带白色
		// 2.5.28：**连续探针 + 强制复位**——灰字若在此处仍出现，探针会把当时的 IMGUI 状态打进日志
		// 2.5.33（§7 发布前清理）：探针是**限频诊断**，已收进 debugLog 门控，发布版不再常开输出；
		//   灰字若在将来回归，把 Debug/debugLog 打开即可复现同样的观测（诊断能力保留，噪音清零）
		// 2.5.31：探针证实状态纯白仍灰 → **文字收编 LabelShadowed**（GUI.Button 只当点击区，
		// 文案走阴影原语，与页签同配方；无阴影衬底的白字在半透明浅底上对比度天然低一档）
		float fs = Er2Ui.Scale;
		GUI.color = Color.white; GUI.contentColor = Color.white; GUI.backgroundColor = Color.white;
		if (Plugin.debugLog.Value && Time.unscaledTime >= factionProbeNext)
		{
			factionProbeNext = Time.unscaledTime + 2f;
			Plugin.ModLog?.LogInfo("[UniGen] 阵营行状态探针: GUI.color=" + GUI.color
				+ " contentColor=" + GUI.contentColor + " enabled=" + GUI.enabled);
		}
		float third = (rect.width - 16f * fs) / 3f;
		float fh = 30f * fs;
		string[] labs = { Ui.Tr("我方"), Ui.Tr("敌方"), Ui.Tr("中立") };
		string[] keys = { "mine", "enemy", "neutral" };
		for (int i = 0; i < 3; i++)
		{
			bool on = faction == keys[i];
			Rect br = new Rect(rect.x + (third + 8f * fs) * i, rect.y, third, fh);
			br.x = Mathf.Round(br.x); br.y = Mathf.Round(br.y);
			if (GUI.Button(br, GUIContent.none, on ? activeButtonStyle : buttonStyle)) faction = keys[i];
			Er2Ui.LabelShadowed(br, labs[i], on ? activeButtonStyle : buttonStyle, Er2Ui.Text);
			Er2Ui.Frame(br, on ? Er2Ui.Accent : Er2Ui.Edge, Mathf.Max(1f, fs));
		}
	}

	private static void DrawUnitTabs(Rect rect)
	{
		int hit = Er2Ui.TabGrid(rect.x, rect.y, rect.width, UnitCatNames, IndexOfUnitCat(category),
			TabsPerRow, TabH, tabStyle, tabActiveStyle);
		if (hit < 0 || hit >= UnitCats.Length) return;
		category = UnitCats[hit];
		page = 0;
		itemSub = ""; letterFilter = "";   // 2.1.0：换类即复位子分类与过滤，避免"切过去是空列表"
		if (category == "favorites") RebuildFavTabs();
	}

	private static void DrawItemTabs(Rect rect)
	{
		int hit = Er2Ui.TabGrid(rect.x, rect.y, rect.width, itemCatNames, itemCats.IndexOf(category),
			ItemTabsPerRow, TabH, tabStyle, tabActiveStyle);
		if (hit < 0 || hit >= itemCats.Count) return;
		category = itemCats[hit];
		page = 0;
		itemSub = ""; letterFilter = "";   // 2.1.0：同上
	}

	/// <summary>
	/// 1.4.28：收藏**主文件夹**视图——列出所有含收藏的分类（＝子文件夹）。
	/// 用户要求："收藏改为每一个生成文件夹的子文件夹，在打开主文件夹后显示。"
	/// 条目 `▸ 名称 (数量)`；点进去把 favCat 设为该分类，回到条目视图。
	/// </summary>
	private static bool DrawFavFolderList(Rect rect)
	{
		float s = Er2Ui.Scale;
		curPages = 1;
		curTotal = favCats.Count;

		float listH = rect.height - Er2Ui.Gap;
		Er2Ui.Fill(new Rect(rect.x, rect.y, rect.width, listH), Er2Ui.ListBg);
		Er2Ui.Frame(new Rect(rect.x, rect.y, rect.width, listH), Er2Ui.Edge, Mathf.Max(1f, s));

		if (favCats.Count == 0)
		{
			GUI.Label(new Rect(rect.x + 8f * s, rect.y + 6f * s, rect.width - 16f * s, 22f * s),
				Ui.Tr("（还没有收藏：点条目右侧的 ☆ 添加）"), helpStyle);
			return false;
		}

		int n = Mathf.Min(favCats.Count, VisibleRows);
		for (int i = 0; i < n; i++)
		{
			float rowY = rect.y + 4f * s + i * RowH;
			Color keep = GUI.backgroundColor;
			GUI.backgroundColor = Er2Ui.Col((i & 1) == 0 ? Er2Ui.RowBg : Er2Ui.RowBgAlt);
			Rect rr = new Rect(rect.x + 6f * s, rowY, rect.width - 12f * s, RowH - 2f * s);
			string label = "▸ " + favCatNames[i] + "   (" + FavCountOf(i) + ")";
			bool hit = GUI.Button(rr, GUIContent.none, rowStyle);
			GUI.backgroundColor = keep;
			bool fhov = rr.Contains(Event.current.mousePosition);
			Er2Ui.LabelShadowed(new Rect(rr.x + 6f * s, rr.y, rr.width - 12f * s, rr.height),
				label, rowTextStyle, fhov ? Er2Ui.TextHover : Er2Ui.Text);
			// 1.4.29：文件夹列表也要有行分隔线（原来这里 0 处，与其他列表观感不一致）
			Er2Ui.HLine(new Rect(rr.x, rowY + RowH - 2f * s, rr.width, Mathf.Max(1f, s)), Er2Ui.EdgeSoft);
			if (hit)
			{
				Event.current.Use();
				favCat = favCats[i];
				page = 0; itemSub = ""; letterFilter = "";
				return true;   // 本帧到此（视图已切）
			}
		}
		return false;
	}

	/// <summary>某个收藏分类下的条目数（单位走 favorites 表，物品走 ItemCatalog.Favs）。</summary>
	private static int FavCountOf(int i)
	{
		if (i < 0 || i >= favCats.Count) return 0;
		string c = favCats[i];
		if (c.StartsWith("item:", StringComparison.Ordinal))
		{
			try { return ItemCatalog.Favs(c.Substring(5)).Count; } catch { return 0; }
		}
		int n = 0;
		for (int k = 0; k < GenCatalog.favorites.Count; k++)
			if (GenCatalog.favorites[k].Category == c) n++;
		return n;
	}

	/// <summary>收藏条目视图的面包屑：「◀ 收藏」按钮 + 当前分类名。</summary>
	private static void DrawFavCrumb(Rect rect)
	{
		float s = Er2Ui.Scale;
		Rect back = new Rect(rect.x, rect.y, 92f * s, Er2Ui.BtnH);
		if (GUI.Button(back, Ui.Tr("◀ 收藏"), buttonStyle))
		{
			Event.current.Use();
			favCat = "";
			page = 0; itemSub = ""; letterFilter = "";
			RebuildFavTabs();
			return;
		}
		int idx = favCats.IndexOf(favCat);
		string name = idx >= 0 && idx < favCatNames.Count ? favCatNames[idx] : favCat;
		GUI.Label(new Rect(back.xMax + 8f * s, rect.y, rect.width - back.width - 8f * s, Er2Ui.BtnH),
			"▸ " + name, textStyle);
	}

	private static int IndexOfUnitCat(string cat)
	{
		for (int i = 0; i < UnitCats.Length; i++) if (UnitCats[i] == cat) return i;
		return -1;
	}

	/// <summary>物品子分类行：「全部」+ 该桶实际存在的子分类（判定来自游戏官方字段，不猜名字）。</summary>
	private static void DrawSubTabs(Rect rect, string bucket, bool favOnly)
	{
		List<string> subs = ItemCatalog.SubsOf(bucket, favOnly);
		int n = subs.Count + 1;                       // +「全部」
		var labels = new List<string>(n);
		labels.Add(Ui.Tr("全部"));
		foreach (string s in subs) labels.Add(Ui.Tr(SubLabel(s)));   // 2.3.0：SubLabel 返回中文原串，**必须过 Ui.Tr**

		int sel = string.IsNullOrEmpty(itemSub) ? 0 : subs.IndexOf(itemSub) + 1;
		int hit = Er2Ui.TabGrid(rect.x, rect.y, rect.width, labels, sel, n, TabH, tabStyle, tabActiveStyle);
		if (hit < 0) return;
		itemSub = hit == 0 ? "" : subs[hit - 1];
		page = 0;
	}

	/// <summary>
	/// 首字母索引行（**替代 GUI.TextField**——它在 IL2CPP 下"Method unstripping failed"，
	/// 一抛异常整帧 OnGUI 中断 = 用户看到的"列表全空"。字母行纯点击，零键盘依赖）。
	/// ⚠️ 2.2.0 把「全部」做成占两格、字母从**第 3 格（i>=2）**起排——
	/// 2.2.0 首发写成 i==1 就取 letters[i-2] → letters[-1] 抛异常 → 整帧中断（2.2.1 修复）。
	/// </summary>
	private static void DrawLetters(Rect rect, string bucket, bool favOnly)
	{
		List<string> letters = ItemCatalog.LettersOf(bucket, itemSub, favOnly);
		if (letters.Count <= 2) return;

		// 2.5.1：尺寸取自上面的单一定义（原来这里 const 又写了一遍，与 BuildRows 的算式重复）
		float LBW = LetterBtnW, LGap = LetterGap;
		int per = Mathf.Max(3, Mathf.FloorToInt((rect.width + LGap) / (LBW + LGap)));
		int cells = letters.Count + 2;   // +「全部」占两格
		for (int i = 0; i < cells; i++)
		{
			int row = i / per, col = i % per;
			float bx = rect.x + col * (LBW + LGap);
			float by = rect.y + row * LetterRowH;
			if (i == 0)
			{
				bool allSel = string.IsNullOrEmpty(letterFilter);
				if (GUI.Button(new Rect(bx, by, LBW * 2f + LGap, LetterBtnH), Ui.Tr("全部"), allSel ? activeButtonStyle : buttonStyle))
				{ letterFilter = ""; page = 0; }
			}
			else if (i >= 2)
			{
				string lt = letters[i - 2];
				bool sel = string.Equals(letterFilter, lt, StringComparison.OrdinalIgnoreCase);
				if (GUI.Button(new Rect(bx, by, LBW, LetterBtnH), lt, sel ? activeButtonStyle : buttonStyle))
				{ letterFilter = lt; page = 0; }
			}
		}
	}

	/// <summary>单位列表（点击=放置）。返回 true = 已开始放置，本帧停止继续绘制。</summary>
	private static bool DrawUnitListBody(Rect rect)
	{
		List<GenEntry> bucket = FilteredBucket();
		curPages = Mathf.Max(1, Mathf.CeilToInt(bucket.Count / (float)VisibleRows));
		curTotal = bucket.Count;
		page = Mathf.Clamp(page, 0, curPages - 1);

		float listH = rect.height - Er2Ui.Gap;
		Er2Ui.Fill(new Rect(rect.x, rect.y, rect.width, listH), Er2Ui.ListBg);
		Er2Ui.Frame(new Rect(rect.x, rect.y, rect.width, listH), Er2Ui.Edge, Mathf.Max(1f, Er2Ui.Scale)); // 2.5.2：列表内凹边框

		if (bucket.Count == 0)
		{
			Er2Ui.LabelShadowed(new Rect(rect.x + 8f * Er2Ui.Scale, rect.y + 6f * Er2Ui.Scale, rect.width - 16f * Er2Ui.Scale, 22f * Er2Ui.Scale), Ui.Tr("（无匹配条目）"), textStyle, Er2Ui.Text);
			return false;
		}

		int from = page * VisibleRows;
		int to = Mathf.Min(from + VisibleRows, bucket.Count);
		GenEntry clicked = null;
		for (int i = from; i < to; i++)
		{
			GenEntry e = bucket[i];
			float ls = Er2Ui.Scale;
			float rowY = rect.y + 4f * ls + (i - from) * RowH;
			bool fav = GenCatalog.IsFav(e.Id);
			Color keep = GUI.backgroundColor;
			// 1.4.28：**斑马纹**——偶数行换一档底色（用户"还是很暗"＝层次看不出）
			GUI.backgroundColor = Er2Ui.Col(fav ? Er2Ui.FavRow : (((i - from) & 1) == 0 ? Er2Ui.RowBg : Er2Ui.RowBgAlt));
			Rect rowRect = new Rect(rect.x + 6f * ls, rowY, rect.width - 38f * ls, RowH - 2f * ls);
			// 1.4.30：**空按钮画底 + 描边文字单独画**——Button 的文字没法做描边，
			// 而 Bold 在游戏字体上不生效（见 Er2Ui.LabelOutlined 注释）。
			if (GUI.Button(rowRect, GUIContent.none, rowStyle)) clicked = e;
			GUI.backgroundColor = keep;
			// 1.4.34：悬停反馈 = 文字变暗（用户指明；原 tooltip 方案已删）
			bool hov = rowRect.Contains(Event.current.mousePosition);
			Er2Ui.LabelShadowed(new Rect(rowRect.x + 6f * ls, rowRect.y, rowRect.width - 12f * ls, rowRect.height),
				e.Title, rowTextStyle, hov ? Er2Ui.TextHover : Er2Ui.Text);
			// 2.5.2：收藏行左侧强调竖条——比"整行换底色"更像设计（底色只轻微提亮，靠竖条点名）
			if (fav) Er2Ui.AccentBar(rowRect, Er2Ui.Accent, Mathf.Max(2f, 3f * ls));
			// 1.4.22：行分隔线——行底与行底之间加一条极淡暖棕，元素边界一眼可辨
			// 1.4.29：**每行都画底线**（原来只在"非最后一行"画 → 单行列表看起来完全没有横线，
			// 用户："每个选项下不都有一个小横线吗，收藏里怎么没了"）
			Er2Ui.HLine(new Rect(rowRect.x, rowY + RowH - 2f * ls, rowRect.width, Mathf.Max(1f, ls)), Er2Ui.EdgeSoft);
			if (StarButton(new Rect(rect.x + rect.width - 30f * ls, rowY + 1f * ls, 26f * ls, RowH - 4f * ls), fav))
			{
				GenCatalog.ToggleFav(e);
				RebuildFavTabs();   // 收藏分类页签随之增减
			}
		}
		if (clicked != null)
		{
			Event.current.Use();
			BeginPlacement(clicked);
			return true;
		}
		return false;
	}

	/// <summary>物品列表（点击=拿起 → 携带模式）。行左侧画小图标（有则画，取不到不阻塞）。</summary>
	private static bool DrawItemListBody(Rect rect, string bucket, bool favOnly)
	{
		List<ItemEntry> all = ItemCatalog.Query(bucket, itemSub, letterFilter, favOnly);
		ItemEntry[] snap;
		try { snap = all.ToArray(); }
		catch { snap = Array.Empty<ItemEntry>(); }

		curPages = Mathf.Max(1, Mathf.CeilToInt(snap.Length / (float)VisibleRows));
		curTotal = snap.Length;
		page = Mathf.Clamp(page, 0, curPages - 1);

		float listH = rect.height - Er2Ui.Gap;
		Er2Ui.Fill(new Rect(rect.x, rect.y, rect.width, listH), Er2Ui.ListBg);
		Er2Ui.Frame(new Rect(rect.x, rect.y, rect.width, listH), Er2Ui.Edge, Mathf.Max(1f, Er2Ui.Scale)); // 2.5.2：列表内凹边框

		if (snap.Length == 0)
		{
			Er2Ui.LabelShadowed(new Rect(rect.x + 8f * Er2Ui.Scale, rect.y + 6f * Er2Ui.Scale, rect.width - 16f * Er2Ui.Scale, 22f * Er2Ui.Scale), Ui.Tr("（无匹配条目）"), textStyle, Er2Ui.Text);
			return false;
		}

		int from = page * VisibleRows;
		int to = Mathf.Min(from + VisibleRows, snap.Length);
		ItemEntry clicked = null;
		for (int i = from; i < to; i++)
		{
			ItemEntry e = snap[i];
			float isc = Er2Ui.Scale;
			float rowY = rect.y + 4f * isc + (i - from) * RowH;
			Texture2D tex = IconTexFor(e.Id);
			if (tex != null) DrawTexFit(tex, new Rect(rect.x + 6f * isc, rowY + 1f * isc, RowH - 4f * isc, RowH - 4f * isc), 0.95f);
			float tx = rect.x + 6f * isc + (tex != null ? RowH : 0f);
			float tw = rect.width - 12f * isc - (tx - rect.x) - 30f * isc;   // 2.1.0：右侧让出星标位

			bool fav = ItemCatalog.IsFav(e.Id);
			Color keep = GUI.backgroundColor;
			GUI.backgroundColor = Er2Ui.Col(fav ? Er2Ui.FavRow : (((i - from) & 1) == 0 ? Er2Ui.RowBg : Er2Ui.RowBgAlt));
			Rect itemRowRect = new Rect(tx, rowY, tw, RowH - 2f * isc);
			if (GUI.Button(itemRowRect, GUIContent.none, rowStyle)) clicked = e;
			GUI.backgroundColor = keep;
			bool ihov = itemRowRect.Contains(Event.current.mousePosition);
			Er2Ui.LabelShadowed(new Rect(itemRowRect.x + 6f * isc, itemRowRect.y, itemRowRect.width - 12f * isc, itemRowRect.height),
				e.Title, rowTextStyle, ihov ? Er2Ui.TextHover : Er2Ui.Text);
			// 2.5.2：收藏行左侧强调竖条（与单位列表同款）
			if (fav) Er2Ui.AccentBar(itemRowRect, Er2Ui.Accent, Mathf.Max(2f, 3f * isc));
			Er2Ui.HLine(new Rect(itemRowRect.x, rowY + RowH - 2f * isc, itemRowRect.width, Mathf.Max(1f, isc)), Er2Ui.EdgeSoft);   // 1.4.29：每行都画
			if (StarButton(new Rect(rect.x + rect.width - 28f * isc, rowY + 1f * isc, 26f * isc, RowH - 4f * isc), fav))
			{
				ItemCatalog.ToggleFav(e);
				RebuildFavTabs();
			}
		}
		if (clicked != null)
		{
			Event.current.Use();
			BeginCarry(clicked);
			return true;
		}
		return false;
	}

	/// <summary>收藏星标（透明底，仅星形变色）。</summary>
	private static bool StarButton(Rect r, bool fav)
	{
		bool hover = r.Contains(Event.current.mousePosition);
		Color keepTxt = GUI.contentColor;
		GUI.contentColor = Er2Ui.Col(fav ? Er2Ui.StarOn : (hover ? Er2Ui.StarHot : Er2Ui.TextDim));
		bool hit = GUI.Button(r, fav ? "★" : "☆", starStyle);
		GUI.contentColor = keepTxt;
		return hit;
	}

	private static void DrawPager(Rect rect, bool item)
	{
		string mid = item ? (page + 1) + "/" + curPages + "  (" + curTotal + ")" : (page + 1) + "/" + curPages;
		int delta = Er2Ui.Pager(rect.x, rect.y, rect.width, curPages, mid, buttonStyle, textStyle);
		if (delta != 0) page = Mathf.Clamp(page + delta, 0, curPages - 1);
	}

	/// <summary>乘员行（步兵无乘员选项）。开关都用按钮（GUI.Toggle 有裁剪风险）。</summary>
	private static void DrawCrewRow(Rect rect)
	{
		if (category == "infantry") return;
		string crewLabel = crewMode == 0 ? Ui.Tr("乘员:专用") : crewMode == 1 ? Ui.Tr("乘员:兵班") : Ui.Tr("乘员:无");
		// 1.4.22：× Scale（原 90/24 硬编码——缩放后按钮与行高不匹配，文字压到相邻行上）
		// 2.5.31：按钮只当点击区，文案走阴影原语（与页签/阵营同配方）
		float cs = Er2Ui.Scale;
		Rect crewBtn = new Rect(rect.x, rect.y, 90f * cs, 24f * cs);
		crewBtn.x = Mathf.Round(crewBtn.x); crewBtn.y = Mathf.Round(crewBtn.y);   // 2.5.27：整数对齐
		if (GUI.Button(crewBtn, GUIContent.none, crewMode != 2 ? activeButtonStyle : buttonStyle))
			crewMode = (crewMode + 1) % 3;
		Er2Ui.LabelShadowed(crewBtn, crewLabel, crewMode != 2 ? activeButtonStyle : buttonStyle, Er2Ui.Text);
		Er2Ui.Frame(crewBtn, Er2Ui.Edge, Mathf.Max(1f, cs));   // 1.4.22：描边，与页签/阵营按钮同款
		if (crewMode == 1 && GenCatalog.crewPool.Count > 0)
		{
			// 兵班选择器：‹ 类型 ›（在官方步兵类型池里循环；自定义班不作乘员来源）
			if (infantrySel == null || infantrySel.SpawnByKey) infantrySel = GenCatalog.crewPool[0];
			int idx = GenCatalog.crewPool.FindIndex(e => e.Id == infantrySel.Id);
			if (idx < 0) idx = 0;
			if (GUI.Button(new Rect(rect.x + 94f * cs, rect.y, 22f * cs, 24f * cs), "‹", buttonStyle))
				infantrySel = GenCatalog.crewPool[(idx - 1 + GenCatalog.crewPool.Count) % GenCatalog.crewPool.Count];
			GUI.Label(new Rect(rect.x + 118f * cs, rect.y + 3f * cs, 110f * cs, 20f * cs), infantrySel.Title, textStyle);
			if (GUI.Button(new Rect(rect.x + 230f * cs, rect.y, 22f * cs, 24f * cs), "›", buttonStyle))
				infantrySel = GenCatalog.crewPool[(idx + 1) % GenCatalog.crewPool.Count];
		}
	}

	/// <summary>提示行（显示将生成的真实阵营 id）。</summary>
	private static void DrawPreviewRow(Rect rect)
	{
		string myFac = GenRunner.MyFaction();
		string previewFac, sideTag;
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
		GUI.Label(new Rect(rect.x, rect.y, rect.width, 20f * Er2Ui.Scale), Ui.Tr("→ 生成 ") + previewFac + "（" + sideTag + "）", textStyle);
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
		// 2.5.1：宽高随分辨率自适应；宽度再收敛进屏幕（小屏不会溢出）
		float w = Er2Ui.ScreenFit(430f);
		float h = 28f * Er2Ui.Scale;
		return new Rect((Screen.width - w) * 0.5f, Screen.height - 62f * Er2Ui.Scale, w, h);
	}

	/// <summary>鼠标是否悬停在放置徽标上（GUI 坐标系：y 向下）。</summary>
	public static bool IsMouseOverBadge()
	{
		Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
		return Placer.Placing && BadgeRect().Contains(m);
	}

	// ── 私有 ──
	/// <summary>
	/// 2.4.2：**高度 = 行计划之和**（此前是另一套固定公式，靠人工与绘制对齐——陷阱 75 的病根）。
	/// 本函数可以每帧被多次调用（ExternalGuiBlockRect / Draw 各调一次），BuildRows 是幂等的。
	/// </summary>
	private static Rect PanelRect()
	{
		BuildRows();
		// 1.4.25：底部留白 8 → 18（原 16 均分时最后一行 ItemHelp 紧贴下边框，
		// 用户反馈"最下面的字都超出菜单了"——贴边在视觉上就读作溢出）
		float h = 30f * Er2Ui.Scale;       // 顶部 8 + 底部 22
		for (int i = 0; i < rows.Count; i++)
			h += rows[i].H + (i + 1 < rows.Count ? Er2Ui.Gap : 0f);   // 与 DrawRows 同一份间距规则
		return new Rect(panelPos.x, panelPos.y, PanelW, h);
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

	// 2.4.2：页签自适应字号（陷阱 76）已上移到 `Er2Ui.TabGrid` + `Er2Ui.FitSize`
	//（用 CalcSize 精确测量代替 2.4.1 的字符系数估算，并带结果缓存；两个 mod 共用）。

	public static void DrawPlacingBadge()
	{
		// 底部居中窄条（宿主提示条上方），不遮战场
		Rect r = BadgeRect();
		// 2.5.1：军绿底 → 中性遮罩（Er2Ui.Scrim），与宿主提示条同款
		GUI.color = Er2Ui.Scrim;
		GUI.DrawTexture(r, Texture2D.whiteTexture);
		GUI.color = Color.white;
		string txt = Ui.Tr("放置: ") + (Placer.PendingEntry()?.Title ?? "?")
			+ Ui.Tr("    左键 放置 · 左键长按拖动 旋转朝向 · Shift 连续 · 右键 取消");
		float m = 8f * Er2Ui.Scale;   // 2.5.1：内缩随倍率
		GUI.Label(new Rect(r.x + m, r.y + m * 0.5f, r.width - m * 2f, 22f * Er2Ui.Scale), txt, flashStyle);
	}

	/// <summary>生成反馈（跟随面板，面板关闭时跟随左下角按钮位置）。</summary>
	public static void DrawFlash()
	{
		if (flash == "" || Time.unscaledTime >= flashUntil) return;
		if (!RtsActive) return;
		EnsureStyles();
		GUIStyle st = flashStyle;
		// 2.5.1：淡绿正常色 → Er2Ui.Text；错误色 → Er2Ui.Danger（与宿主同一套语义色）
		st.normal.textColor = Er2Ui.Col(flashIsError ? Er2Ui.Danger : Er2Ui.Text);
		GUI.Label(new Rect(panelPos.x, panelPos.y - 24f * Er2Ui.Scale, Er2Ui.ScreenFit(700f), 22f * Er2Ui.Scale), flash, st);
	}

	/// <summary>RTS 的"生成"开关按钮（仅面板关闭时显示；点击=G）。
	/// 1.1.1：从左下角移到左缘中段（用户要求：左下角让位给战场指挥官的选中单位信息面板）。
	/// 2.5.29：**整数对齐**——Screen.height*0.42 是分数坐标，按钮文字亚像素模糊发灰（灰字同款根因）。</summary>
	private static Rect ToggleButtonRect()
	{
		Rect r = new Rect(12f * Er2Ui.Scale, Screen.height * 0.42f,
			120f * Er2Ui.Scale, 26f * Er2Ui.Scale);
		r.x = Mathf.Round(r.x); r.y = Mathf.Round(r.y);
		return r;
	}

	public static void DrawToggleButton()
	{
		if (!RtsActive || open || Placer.Placing) return;
		EnsureStyles();
		Rect r = ToggleButtonRect();
		// 2.5.32（用户："现在有白色描边了，但是背景还是灰色的"）：按钮直接贴在明亮泥地上，
		// buttonStyle 的 0.55 半透明底会被洗成灰蒙蒙——加**纯黑 72% 底板**（与指挥官底部提示条
		// 同款配方，用户点名的观感锚点），文字走阴影原语。按钮本体只当点击区。
		GUI.color = Color.white; GUI.contentColor = Color.white; GUI.backgroundColor = Color.white;
		Er2Ui.Fill(r, new Color(0f, 0f, 0f, 0.72f));
		bool clicked = GUI.Button(r, GUIContent.none, buttonStyle);
		Er2Ui.LabelShadowed(r, Ui.Tr("生成 [G]"), buttonStyle, Er2Ui.Text);
		Er2Ui.Frame(r, Er2Ui.Edge, Mathf.Max(1f, Er2Ui.Scale));   // 2.5.29：描边与页签/阵营按钮同款
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

	// 2.4.2：样式工厂与纯色贴图缓存已上移到 ER2Shared.Er2Ui（两个 mod 共用一份令牌，
	// 且 Er2Ui.Solid 是按颜色建缓存 + 带 hideFlags=61，修掉了这里"单槽缓存 + 无 hideFlags"的隐患）。

	/// <summary>2.0.0：供 ItemDragger 复用的样式访问（携带徽标/光标名签）。</summary>
	internal static void EnsureStylesPublic() => EnsureStyles();

	/// <summary>2.0.0：供 ItemDragger 复用的提示样式（携带徽标、光标名签）。</summary>
	internal static GUIStyle FlashStylePublic() { EnsureStyles(); return flashStyle; }

	private static void EnsureStyles()
	{
		// 陷阱 5：GUIStyle 拷贝构造被 IL2CPP 裁剪——全部 new GUIStyle() + 显式字段；normal.textColor 必须显式
		// 2.5.1：scale 变化（分辨率切换 / 自适应重算）后字号已变，缓存的样式必须重建。
		// 各判各的（Er2Ui.ScaleChangedSince）——不用全局 dirty 标志，避免多面板互相抢清。
		if (titleStyle != null && !Er2Ui.ScaleChangedSince(styleScale)) return;
		styleScale = Er2Ui.Scale;
		// 2.5.3（日志强制令）：面板首次构建即记录 UI 诊断快照——用户报"文字发灰"时，
		// 日志里的 Scale/字号/透明度就是判定依据，不再靠猜
		if (!uiDiagLogged)
		{
			uiDiagLogged = true;
			try
			{
				Plugin.ModLog?.LogInfo("[UniGen] UI 诊断: Scale=" + Er2Ui.Scale.ToString("0.00")
					+ " FontTitle=" + Er2Ui.FontTitle + " FontBody=" + Er2Ui.FontBody
					+ " FontTab=" + Er2Ui.FontTabMax + "/" + Er2Ui.FontTabMin
					+ " Mono=" + Er2Ui.Mono + " PanelAlpha=" + Er2Ui.PanelAlpha.ToString("0.00")
					+ " 文字=白+右下阴影（LabelShadowed）");
			}
			catch { }
		}
		// 2.4.2：全部走 ER2Shared.Er2Ui 的令牌与工厂（配色/字号在两个 mod 里只有一个定义处）
		for (int i = 0; i < UnitCats.Length; i++) UnitCatNames[i] = Ui.Tr(UnitCatRaw[i]);
		titleStyle = Er2Ui.MakeLabel(Er2Ui.FontTitle, TextAnchor.MiddleLeft, Er2Ui.Text, FontStyle.Bold);
		textStyle = Er2Ui.MakeLabel(Er2Ui.FontBody, TextAnchor.MiddleLeft, Er2Ui.Text);
		// 1.4.30：行文字再放大一号（FontBody+3 = 15px）——配合描边，深底上足够醒
		rowTextStyle = Er2Ui.MakeLabel(Er2Ui.FontBody + 4, TextAnchor.MiddleLeft, Er2Ui.Text);   // 1.4.33：15 → 16px
		helpStyle = Er2Ui.MakeLabel(Er2Ui.FontBody, TextAnchor.MiddleLeft, Er2Ui.Text);
		helpStyle.wordWrap = true;   // 1.4.28：允许换行（否则长文案只能溢出被裁）

		// 宿主同款主题：#0E1C0EB4 底 / #3E703EE0 选中 / #DFF0DF 文字
		buttonStyle = Er2Ui.MakeButton(Er2Ui.FontBody, Er2Ui.Surface, Er2Ui.Text);
		activeButtonStyle = Er2Ui.MakeButton(Er2Ui.FontBody, Er2Ui.SurfaceActive, Er2Ui.TextOnActive, FontStyle.Bold);
		// 1.4.29：**Bold + 字号 +1**——12px Normal 在深底上「亮度感」不足，用户连续两轮反馈"文本太暗"。
		// 加粗是提升深底白字可读性最直接的手段（对比度其实早就够了，缺的是笔画厚度）。
		// 2.5.2：底改**白贴图**——行底色由 backgroundColor 提供（经 Col() 转线性，见 Er2Ui ⓪），
		// 贴图本身若也是深色会与 backgroundColor 相乘双重变暗、斑马纹消失。
		rowStyle = Er2Ui.MakeButton(Er2Ui.FontBody + 1, Color.white, Er2Ui.Text, FontStyle.Bold, TextAnchor.MiddleLeft);

		// 页签样式（字号由页签原语逐次改写，故必须单独一份——不能与 buttonStyle 共用）
		tabStyle = Er2Ui.MakeButton(Er2Ui.FontTabMax, Er2Ui.Surface, Er2Ui.Text);
		tabActiveStyle = Er2Ui.MakeButton(Er2Ui.FontTabMax, Er2Ui.SurfaceActive, Er2Ui.TextOnActive, FontStyle.Bold);

		// 2.5.1：这两个字号原本写死 13/14，自适应下不跟随 → 改乘 Scale
		int flashSize = Mathf.Max(8, Mathf.RoundToInt(13 * Er2Ui.Scale));
		flashStyle = Er2Ui.MakeLabel(flashSize, TextAnchor.MiddleLeft, Er2Ui.Text, FontStyle.Bold);

		// 收藏星标：透明底（无底色贴图），仅文字颜色随状态
		int starSize = Mathf.Max(10, Mathf.RoundToInt(17 * Er2Ui.Scale));   // 1.4.29：14 → 17（小号星形笔画太细显得暗）
		// 1.4.27：**textColor 必须纯白**——GUI.contentColor 与 style.normal.textColor 是**相乘**关系，
		// 原来用 TextDim(#CACAD0) → ★ 的金色被乘暗成 #CAA84D（用户："收藏的黄色星星也太暗了"）。
		starStyle = Er2Ui.MakeLabel(starSize, TextAnchor.MiddleCenter, Color.white, FontStyle.Bold);
		starStyle.hover.textColor = Color.white;
		starStyle.active.textColor = Color.white;
		starStyle.focused.textColor = Color.white;
	}

	// 建样式时的 Scale（变了就重建，见 EnsureStyles）
	private static float styleScale = 1f;
	// 2.5.3：UI 诊断快照只打一次（日志强制令）
	private static bool uiDiagLogged;
	// 2.5.28：阵营行状态探针节流（2s 一条）
	private static float factionProbeNext;
}
