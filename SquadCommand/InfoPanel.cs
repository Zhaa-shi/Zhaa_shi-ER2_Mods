using System;
using System.Collections.Generic;
using ER2Shared;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 1.2.0：左下角选中单位信息面板（地狱之门式）。
/// 1.2.1：去底板、文字贴左缘（x=12）双描影。
/// 1.2.4（用户要求）：背包按钮（原"只读列表"只能看见前几项）。
/// 1.2.5/1.2.6：分类列表版背包面板。
/// 1.3.0：分类列表**由格子背包窗口取代**（BackpackPanel.cs：多窗口 + 拖拽交换）；
///   本面板只保留 [背包]/[货舱] 按钮与尸体行（左键点选尸体 → 也可开背包）。
///   信息区：姓名/职类、血量条、姿态、压制；载具另有 [下车]/[修理]。
/// </summary>
internal static class InfoPanel
{
	// 2.5.1：改为属性，随 Er2Ui.Scale 自适应（原本 const → 分辨率变化时 UI 尺寸钉死）
	private static float W => 352f * ER2Shared.Er2Ui.Scale;
	private static float H => 190f * ER2Shared.Er2Ui.Scale;
	// 1.4.34：34 → 14，与右下小队列表同——左右两块贴底位置一致（用户："让他们对称啊"）
	private static float BottomGap => 14f * ER2Shared.Er2Ui.Scale;
	private static float LeftX => 12f * ER2Shared.Er2Ui.Scale;
	// 1.4.32：面板左边缘。原来 x=0（贴屏幕边），而右侧小队列表是"离右边缘 10*Scale"——
	// 用户要求"与右边的小队列表对齐"，这里取同样的 10*Scale 做**左右对称**。
	private static float PanelPad => 10f * ER2Shared.Er2Ui.Scale;

	// 背包列表（1.2.5：分类列表，非网格）
	private static int unitCycle;
	private static readonly Dictionary<long, float> maxHpSeen = new Dictionary<long, float>();
	private static GUIStyle smallStyle;
	private static float styleScale = 1f;   // 2.5.1：建样式时的 Scale（变了就重建）


	// 1.4.34：**高度按内容动态**——原来固定 190*Scale，内容少时下方大片空白
	//（用户："信息显示也不会动态调整，下面空这么多"）。Draw() 每帧实测内容底部回写 contentH。
	private static float contentH = 120f;
	internal static Rect PanelRect() => new Rect(PanelPad, Screen.height - contentH - BottomGap, W, contentH);

	/// <summary>宿主 IsMouseOverGui 用：鼠标在面板上时吞掉战场手势。
	/// 1.3.0：背包格子窗口的命中判定移到 BackpackPanel.WantsMouse()（含拖拽中全屏吞手势）。</summary>
	internal static bool WantsMouse()
	{
		if (!GodViewController.Active) return false;
		try
		{
			Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
			if (PanelRect().Contains(m)) return true;
		}
		catch { }
		return false;
	}

	internal static void Draw()
	{
		if (!GodViewController.Active) return;
		if (GodViewController.EscMenuOpen) return;
		try
		{
			// 2.5.1：先按屏幕分辨率刷新自适应倍率；scale 变了就重建样式（字号随分辨率走）
			ER2Shared.Er2Ui.AutoScale();
			if (smallStyle != null && ER2Shared.Er2Ui.ScaleChangedSince(styleScale))
				smallStyle = null;
			Color textC = GodViewController.UiText;
			Color hoverC = GodViewController.UiHover;
			smallStyle = smallStyle ?? MakeSmall();
			if (smallStyle != null) styleScale = ER2Shared.Er2Ui.Scale;
			// 1.4.32：**整块背景**（原来只有零散控件底，文字直接叠在战场上，与右侧小队列表不一致）。
			// 底色用 `Er2Ui.Scrim` —— 就是玩家说"喜欢"的那条底部提示条的背景（纯黑 72%）。
			Rect pr = PanelRect();
			GodViewController.DrawHudPlate(pr);

			float x = LeftX;
			float y = pr.y + 4f * ER2Shared.Er2Ui.Scale;
			float w = W - 8f * ER2Shared.Er2Ui.Scale;

			// ── 行1：选择统计 + 焦点切换 ──
			string title = Ui.Tr("已选 步兵 ") + GodViewController.SelInfantryCountPublic()
				+ Ui.Tr(" + 载具 ") + GodViewController.VehicleRefCountPublic();
			ShadowLabel(new Rect(x, y, w - 64f, 20f), title, textC);
			int total = FocusCount();
			if (total > 0)
			{
				if (unitCycle >= total) unitCycle = 0;
				Rect prev = new Rect(x + w - 64f, y, 20f, 20f);
				Rect next = new Rect(x + w - 20f, y, 20f, 20f);
				if (GhostButton(prev, "◀", textC, hoverC)) unitCycle = (unitCycle - 1 + total) % total;
				if (GhostButton(next, "▶", textC, hoverC)) unitCycle = (unitCycle + 1) % total;
				ShadowLabel(new Rect(x + w - 44f, y, 24f, 20f), (unitCycle + 1) + "/" + total, textC);
			}
			y += 22f;

			// ── 行2-3：焦点单位 ──
			y = DrawFocused(x, y, w, textC, hoverC);

			// ── 载具交互（下车/修理）──
			y = DrawVehicleActions(x, y, w, textC, hoverC);

			// ── 可交互物（1.2.4：空载具/物品选中时显示）──
			y = DrawPropSelection(x, y, w, textC);

			// ── 背包/货舱按钮（1.3.0：改开 BackpackPanel 格子窗口；1.3.2 起尸体走右键开窗，不再有尸体行）──
			y = DrawPackButton(x, y, textC, hoverC);

			// 1.4.34：**实测内容底部回写面板高度**——内容多则面板长、少则短，不再留大片空白。
			// 上下限：最少装下标题+一行，最多到屏幕中部（防异常情况无限长）。
			float measured = (y + 10f * ER2Shared.Er2Ui.Scale) - pr.y;
			contentH = Mathf.Clamp(measured, 96f * ER2Shared.Er2Ui.Scale, 340f * ER2Shared.Er2Ui.Scale);
		}
		catch { }
	}

	// ===== 1.3.0：背包/货舱按钮（格子窗口在 BackpackPanel）=====

	private static float DrawPackButton(float x, float y, Color textC, Color hoverC)
	{
		object f = FocusAt(unitCycle);
		Soldier s = f as Soldier;
		Vehicle v = f as Vehicle;
		if (s != null)
		{
			Rect b = new Rect(x, y, 88f, 20f);
			if (ActionButton(b, Ui.Tr("背包"), textC, hoverC)) BackpackPanel.ToggleForSoldier(s);
			return y + 22f;
		}
		if (v != null)
		{
			Rect b = new Rect(x, y, 88f, 20f);
			if (ActionButton(b, Ui.Tr("货舱"), textC, hoverC)) BackpackPanel.ToggleForVehicle(v);
			return y + 22f;
		}
		return y;
	}

	/// <summary>找该士兵的 InventoryManager（activeInventories 里按归属匹配；找不到返回 null）。
	/// 1.3.0 提为 internal，供 BackpackPanel 复用。</summary>
	internal static InventoryManager FindInventoryManager(Soldier s)
	{
		try
		{
			var list = InventoryManager.activeInventories;
			if (list == null) return null;
			long sPtr = (long)s.Pointer;
			for (int i = 0; i < list.Count; i++)
			{
				InventoryManager m = list[i];
				if (m == null) continue;
				// 归属判定：InventoryManager 挂在士兵身上（GetComponentInParent 反查）
				try
				{
					Soldier owner = m.GetComponentInParent<Soldier>();
					if (owner != null && (long)owner.Pointer == sPtr) return m;
				}
				catch { }
				try
				{
					if (m.IsPlayersInventory()) { /* 玩家背包：仅当玩家就是该兵时用 */ }
				}
				catch { }
			}
		}
		catch { }
		return null;
	}

	// ===== 焦点单位信息 =====

	private static float DrawFocused(float x, float y, float w, Color textC, Color hoverC)
	{
		int total = FocusCount();
		if (total == 0)
		{
			ShadowLabel(new Rect(x, y, w, 20f), Ui.Tr("未选中单位（左键选择/框选）"), new Color(textC.r, textC.g, textC.b, 0.6f));
			return y + 22f;
		}
		object focus = FocusAt(unitCycle);
		Soldier s = focus as Soldier;
		Vehicle v = focus as Vehicle;
		string line1;
		float hp = -1f, hpMax = 100f;
		if (s != null)
		{
			line1 = GodViewController.SafeName(s);
			try { string cls = s.GetClassName(); if (!string.IsNullOrEmpty(cls)) line1 += " · " + cls; } catch { }
			try { hp = s.life_total.Value; } catch { }
			long k = 0; try { k = (long)s.Pointer; } catch { }
			if (hp >= 0f)
			{
				float seen;
				if (!maxHpSeen.TryGetValue(k, out seen) || hp > seen) maxHpSeen[k] = hp;
				hpMax = Mathf.Max(1f, maxHpSeen.TryGetValue(k, out seen) ? seen : 100f);
			}
		}
		else if (v != null)
		{
			line1 = Ui.Tr("载具 ") + GodViewController.SafeName(v);
			try
			{
				int occ = 0;
				Soldier[] os = v.GetComponentsInChildren<Soldier>();
				if (os != null) foreach (Soldier o in os) { try { if (o != null && o.IsAlive) occ++; } catch { } }
				line1 += Ui.Tr(" · 乘员 ") + occ;
			}
			catch { }
		}
		else return y;

			ShadowLabel(new Rect(x, y, w - 130f, 20f), line1, textC);
			if (s != null && hp >= 0f)
			{
				// 1.4.2 UI 打磨：条与数字分离（数字右对齐独立框，不再叠在条上）
				Rect bar = new Rect(x + w - 124f, y + 8f, 84f, 8f);
				Color keep = GUI.color;
				GUI.color = new Color(0f, 0f, 0f, 0.55f);
				GUI.DrawTexture(bar, Texture2D.whiteTexture);
				float frac = Mathf.Clamp01(hp / hpMax);
				GUI.color = frac > 0.55f ? new Color(0.4f, 0.85f, 0.4f, 0.95f)
					: frac > 0.25f ? new Color(0.95f, 0.75f, 0.25f, 0.95f)
					: new Color(0.95f, 0.3f, 0.25f, 0.95f);
				GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * frac, bar.height), Texture2D.whiteTexture);
				GUI.color = keep;
				TextAnchor keepAlign = smallStyle.alignment;
				smallStyle.alignment = TextAnchor.MiddleRight;
				ShadowLabel(new Rect(x + w - 36f, y, 36f, 20f), hp.ToString("0"), textC);
				smallStyle.alignment = keepAlign;
			}
			y += 22f;

		if (s != null)
		{
			string pose = "";
			try { pose = GodViewController.PoseName(s.Pose); } catch { }
			string sup = "";
			try
			{
				float sv = new Lua_Soldier(s).getSuppressionValue();
				sup = sv >= 0.5f ? Ui.Tr("受压制") : sv > 0.1f ? Ui.Tr("受扰") : Ui.Tr("无");
			}
			catch { }
			ShadowLabel(new Rect(x, y, w, 20f), Ui.Tr("姿态 ") + pose + Ui.Tr(" · 压制 ") + sup, textC);
			y += 22f;
		}
		return y;
	}

	private static readonly List<Vehicle> actionVehBuf = new List<Vehicle>();

	/// <summary>1.2.3：选中含载具时的交互行（下车 / 修理）。</summary>
	private static float DrawVehicleActions(float x, float y, float w, Color textC, Color hoverC)
	{
		int vehCount = 0;
		bool canRepair = false;
		try
		{
			// 1.2.10：复用零分配版本（原 GetVehicleRefsSnapshot 每次 new List）
			GodViewController.GetVehicleRefsInto(actionVehBuf);
			vehCount = actionVehBuf.Count;
			foreach (Vehicle v in actionVehBuf) { if (GodViewController.VehicleCanBeRepaired(v)) { canRepair = true; break; } }
		}
		catch { }
		if (vehCount == 0) return y;

		Rect b1 = new Rect(x, y, 72f, 20f);
		Rect b2 = new Rect(x + 78f, y, 72f, 20f);
		if (ActionButton(b1, Ui.Tr("下车"), textC, hoverC)) GodViewController.DismountSelectedVehicles();
		if (ActionButton(b2, canRepair ? Ui.Tr("修理") : Ui.Tr("无需修理"), textC, hoverC) && canRepair)
			GodViewController.RepairSelectedVehicle();
		return y + 22f;
	}

	/// <summary>1.2.4：选中的可交互物（空载具 / 物品）信息。</summary>
	private static float DrawPropSelection(float x, float y, float w, Color textC)
	{
		ItemObject pi = GodViewController.SelectedPropItem;
		if (pi == null) return y;
		string id = "?"; try { id = pi.item_id; } catch { }
		ShadowLabel(new Rect(x, y, w, 20f), Ui.Tr("可交互物品：") + id, textC);
		return y + 22f;
	}

	// ===== 绘制工具 =====

	private static void ShadowLabel(Rect r, string text, Color c)
	{
		Color sh = new Color(0f, 0f, 0f, 0.85f);
		GUI.color = sh;
		GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, smallStyle);
		GUI.color = c;
		GUI.Label(r, text, smallStyle);
		GUI.color = Color.white;
	}

	private static bool GhostButton(Rect r, string label, Color textC, Color hoverC)
	{
		Event e = Event.current;
		bool hover = e != null && r.Contains(e.mousePosition);
		GUI.color = hover ? hoverC : new Color(textC.r, textC.g, textC.b, 0.55f);
		GUI.Label(r, label, smallStyle);
		GUI.color = Color.white;
		if (hover && e != null && e.type == EventType.MouseDown && e.button == 0)
		{
			e.Use();
			return true;
		}
		return false;
	}

	private static bool ActionButton(Rect r, string label, Color textC, Color hoverC)
	{
		Event e = Event.current;
		bool hover = e != null && r.Contains(e.mousePosition);
		GUI.color = hover ? new Color(hoverC.r, hoverC.g, hoverC.b, 0.35f) : new Color(0f, 0f, 0f, 0.35f);
		GUI.DrawTexture(r, Texture2D.whiteTexture);
		GUI.color = Color.white;
		// 1.4.31：与 UniGen 面板同款——皮革质感 + 顶部受光边（风格统一）
		Er2Ui.Leather(r, 0.10f);
		Er2Ui.HLine(new Rect(r.x, r.y, r.width, Mathf.Max(1f, Er2Ui.Scale)), Er2Ui.EdgeSoft);
		ShadowLabel(r, label, textC);
		if (hover && e != null && e.type == EventType.MouseDown && e.button == 0)
		{
			e.Use();
			return true;
		}
		return false;
	}

	// ===== 焦点单位集合 =====

	// 1.2.10 性能：焦点列表改为**缓存**（0.15s）。原 FocusCount/FocusAt 被 OnGUI 的每个事件
	//（多次 Layout/Repaint）各调一次，每次都 GetSelectedInfantry()+GetVehicleRefsSnapshot()
	// → 每帧十几次 List 分配 + 逐单位 GetComponentInParent。
	private static readonly List<Soldier> focusInf = new List<Soldier>();
	private static readonly List<Vehicle> focusVeh = new List<Vehicle>();
	private static float focusCacheNext = -10f;

	private static void RefreshFocusCache()
	{
		if (Time.unscaledTime < focusCacheNext) return;
		focusCacheNext = Time.unscaledTime + 0.15f;
		GodViewController.GetSelectedInfantryInto(focusInf);
		for (int i = focusInf.Count - 1; i >= 0; i--)
		{
			Soldier s = focusInf[i];
			try
			{
				if (s == null || !s.IsAlive || s.transform == null || s.GetComponentInParent<Vehicle>() != null)
					focusInf.RemoveAt(i);
			}
			catch { focusInf.RemoveAt(i); }
		}
		GodViewController.GetVehicleRefsInto(focusVeh);
	}

	private static int FocusCount()
	{
		RefreshFocusCache();
		return focusInf.Count + focusVeh.Count;
	}

	private static object FocusAt(int idx)
	{
		RefreshFocusCache();
		if (idx >= 0 && idx < focusInf.Count) return focusInf[idx];
		int vi = idx - focusInf.Count;
		if (vi >= 0 && vi < focusVeh.Count) return focusVeh[vi];
		return null;
	}

	/// <summary>1.3.0：当前焦点单位（BackpackPanel 背包热键入口用）。</summary>
	internal static object CurrentFocusUnit() => FocusAt(unitCycle);

	private static GUIStyle MakeSmall()
	{
		Font f = null;
		try { f = SquadCmdLogic.HudStyleSmall().font; } catch { }
		// 2.4.2：走 Er2Ui 工厂（normal.textColor 默认黑的陷阱由工厂统一兜住）
		// 2.5.1：字号由写死 12 改为 Er2Ui 令牌（随分辨率自适应）
		return Er2Ui.MakeLabel(Er2Ui.FontBody, TextAnchor.MiddleLeft, Color.white, FontStyle.Normal, f);
	}

	/// <summary>
	/// 2.5.1：样式缓存失效入口——Scale 变化（分辨率切换/自适应重算）后字号已变，
	/// 缓存的 smallStyle 必须重建，否则界面还是旧字号。
	/// </summary>
	internal static void InvalidateStyles()
	{
		smallStyle = null;
	}
}
