using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 1.2.19：自定义光标改回 **Cursor.SetCursor（系统级光标）**。
///   1.2.12-1.2.18 的 IMGUI 自绘有两个解决不了的问题：
///   ① **被其它 IMGUI 遮挡**（通用生成面板等）——同一 OnGUI 回调内按补丁执行顺序绘制，
///      GUI.depth 在同一回调里不生效；② 随帧率有一帧滞后（"不跟手"）。
///   系统级光标由 OS 合成，**永远在最上层**且零延迟；圆环贴图是纯距离函数生成（可靠，
///   不再出现 1.2.5 时代箭头多边形算错的白块）。
///   形状/颜色随指向对象变化（0.1s 节流探测），形状变化或每 2s 重申一次 SetCursor。
/// </summary>
internal static class MouseCursor
{
	internal enum Shape { Default, Friendly, Enemy, Vehicle, Building, Emplacement, Cross, Interactable }

	// 1.4.31：32 → 64。32×32 是 Windows 硬件光标的上限，一旦系统 DPI 缩放或游戏
	// 全屏缩放把它拉伸，就会明显发糊。给 64 后 Unity 自动走**软件光标**，按屏幕坐标绘制，锐利。
	private const int TexSize = 64;
	private const float RingRadius = 14f;        // 1.4.31：随 TexSize 32→64 等比放大（原 7@32）
	private const float RingHalfWidth = 2.2f;    // 同上（原 1.1@32）
	private const float RingAlpha = 0.9f;        // 系统光标不参与场景混合，可以更实
	private const float RingDarkAlpha = 0.5f;    // 外侧暗描边
	private const float ReprobeInterval = 0.1f;

	private static Shape current = Shape.Default;
	private static Shape applied = (Shape)(-1);
	private static float nextProbe = -10f;
	private static float reapplyNext = -10f;
	private static readonly Dictionary<Shape, Texture2D> texCache = new Dictionary<Shape, Texture2D>();

	internal static bool Enabled => Plugin.customCursor != null && Plugin.customCursor.Value;

	/// <summary>我们是否接管光标（RTS 内、功能开、非 ESC 菜单）。</summary>
	internal static bool OwnsCursor
	{
		get
		{
			try { return Enabled && GodViewController.Active && !GodViewController.EscMenuOpen; }
			catch { return false; }
		}
	}

	/// <summary>每帧（Tick 持久段）：形状变化或周期性重申 SetCursor；维持光标自由。</summary>
	internal static void Tick()
	{
		try
		{
			if (!OwnsCursor) return;
			if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
			if (!Cursor.visible) Cursor.visible = true; // SetCursor 的贴图只在 visible 时显示

			// 拖阵型/放置模式固定十字（精度场景），不探测
			if (Formation.Dragging || UniversalGenProbe.Placing) current = Shape.Cross;
			else if (Time.unscaledTime >= nextProbe)
			{
				nextProbe = Time.unscaledTime + ReprobeInterval;
				current = Probe();
			}

			// 形状变化即切换；每 2s 重申一次（防游戏或其它 mod 覆盖了我们的光标）
			if (current != applied || Time.unscaledTime > reapplyNext)
			{
				Texture2D t = GetTex(current);
				if (t != null)
				{
					Cursor.SetCursor(t, new Vector2(TexSize * 0.5f, TexSize * 0.5f), CursorMode.Auto);
					applied = current;
				}
				reapplyNext = Time.unscaledTime + 2f;
			}
		}
		catch { }
	}

	/// <summary>退出 RTS / 关闭功能：还回系统默认光标。</summary>
	internal static void Restore()
	{
		try
		{
			Cursor.SetCursor(null, Vector2.zero, CursorMode.Auto);
			Cursor.visible = true;
			applied = (Shape)(-1);
		}
		catch { }
	}

	/// <summary>样式切换后重建贴图（cfg SettingChanged 调用）。</summary>
	internal static void InvalidateCache()
	{
		texCache.Clear();
		applied = (Shape)(-1);
	}

	// ══════════════ 贴图 ══════════════

	private static Texture2D GetTex(Shape s)
	{
		if (texCache.TryGetValue(s, out Texture2D t) && t != null) return t;
		try
		{
			t = Build(s);
			if (t != null) { t.hideFlags = (HideFlags)61; texCache[s] = t; } // 陷阱 12
		}
		catch (Exception ex) { SquadCmdLogic.Log("[Cursor] 贴图生成失败 " + s + ": " + ex.Message); }
		return t;
	}

	private static void SetPx(Color[] px, int x, int y, Color c)
	{
		if (x < 0 || y < 0 || x >= TexSize || y >= TexSize) return;
		px[y * TexSize + x] = c;
	}

	/// <summary>
	/// 2.5.0：光标配色改**灰阶**（用户："标记点等都用白色或半透明的灰色"）。
	/// 灰黑主题下不再用色相区分目标类型；类型由**形状**承担（Default/Friendly/Enemy/Vehicle/…）。
	/// 仅两处保留彩色，因为它们承载**危险/权限**语义而非类型：
	///   Enemy = 红（敌对，必须一眼可辨）
	///   Emplacement = 橙（可操作的重武器点，与普通建筑区分）
	/// 其余按"可交互程度"排明度：Friendly 略暗（背景信息）→ Interactable 最亮（当前可点）。
	/// </summary>
	private static Color StateColor(Shape s)
	{
		switch (s)
		{
			// 1.4.31：**恢复逐状态语义色**。1.4.19 为配合灰黑 UI 把光标整体压成灰阶，
			// 结果只剩"敌军红 / 工事橙"两档，用户反馈"不能变色了"。
			// 光标画在 3D 场景上（不是面板里），本来就不必跟着面板去色——恢复彩色反而更好辨认。
			case Shape.Friendly: return new Color(0.35f, 0.95f, 0.55f, 1f);   // 青绿（友军）
			case Shape.Enemy: return new Color(1f, 0.32f, 0.28f, 1f);         // 红（敌军）
			case Shape.Vehicle: return new Color(0.35f, 0.85f, 1f, 1f);       // 亮青（可驾驶载具）
			case Shape.Building: return new Color(0.78f, 0.78f, 0.80f, 1f);   // 灰白（建筑＝环境物）
			case Shape.Emplacement: return new Color(1f, 0.68f, 0.28f, 1f);   // 橙（工事/重武器）
			case Shape.Interactable: return new Color(1f, 0.92f, 0.35f, 1f);  // 黄（可交互物品）
			case Shape.Cross: return new Color(1f, 1f, 1f, 1f);               // 纯白（精度十字）
			default: return new Color(1f, 1f, 1f, 1f);                        // 纯白（默认）
		}
	}

	private static bool UseCrossStyle()
	{
		try { return Plugin.cursorStyle != null && Plugin.cursorStyle.Value == "Cross"; }
		catch { return false; }
	}

	private static Texture2D Build(Shape s)
	{
		Texture2D t = new Texture2D(TexSize, TexSize, TextureFormat.RGBA32, false);
		Color[] px = new Color[TexSize * TexSize];
		for (int i = 0; i < px.Length; i++) px[i] = new Color(0, 0, 0, 0);
		Color c = StateColor(s);
		bool cross = UseCrossStyle() || s == Shape.Cross;
		if (cross) DrawCross(px, c, s);
		else DrawRing(px, c, s);
		t.SetPixels(px);
		t.Apply();
		return t;
	}

	// ── 空心半透明圆环（默认）──
	private static void DrawRing(Color[] px, Color c, Shape s)
	{
		float cx = TexSize * 0.5f, cy = TexSize * 0.5f;
		for (int y = 0; y < TexSize; y++)
			for (int x = 0; x < TexSize; x++)
			{
				float dx = x + 0.5f - cx, dy = y + 0.5f - cy;
				float d = Mathf.Sqrt(dx * dx + dy * dy);
				float band = Mathf.Abs(d - RingRadius);
				float a = Mathf.Clamp01(RingHalfWidth + 0.5f - band);
				if (a > 0f) { px[y * TexSize + x] = new Color(c.r, c.g, c.b, RingAlpha * a); continue; }
				float dband = Mathf.Abs(d - (RingRadius + RingHalfWidth + 0.9f));
				float da = Mathf.Clamp01(0.9f - dband);
				if (da > 0f) px[y * TexSize + x] = new Color(0f, 0f, 0f, RingDarkAlpha * da);
			}
		// 火力点：下方小弧（"可转向"提示）
		if (s == Shape.Emplacement)
			for (int a = 200; a <= 340; a += 4)
			{
				float rad = a * Mathf.Deg2Rad;
				SetPx(px, Mathf.RoundToInt(cx + Mathf.Cos(rad) * 13f), Mathf.RoundToInt(cy + Mathf.Sin(rad) * 13f), c);
			}
	}

	// ── 细线十字（备选）──
	private static void DrawCross(Color[] px, Color c, Shape s)
	{
		Color dark = new Color(0f, 0f, 0f, 0.7f);
		int cx = TexSize / 2, cy = TexSize / 2;
		int gap = 4, arm = 11;
		for (int r = gap; r <= arm; r++)
		{
			SetPx(px, cx + r, cy - 1, dark); SetPx(px, cx - r, cy - 1, dark);
			SetPx(px, cx + r, cy + 1, dark); SetPx(px, cx - r, cy + 1, dark);
			SetPx(px, cx - 1, cy + r, dark); SetPx(px, cx - 1, cy - r, dark);
			SetPx(px, cx + 1, cy + r, dark); SetPx(px, cx + 1, cy - r, dark);
		}
		for (int r = gap; r <= arm; r++)
		{
			SetPx(px, cx + r, cy, c); SetPx(px, cx - r, cy, c);
			SetPx(px, cx, cy + r, c); SetPx(px, cx, cy - r, c);
		}
		SetPx(px, cx, cy, c);
		if (s == Shape.Vehicle)
		{
			int b = 10;
			for (int i = -b; i <= b; i++)
			{
				SetPx(px, cx + i, cy + b, c); SetPx(px, cx + i, cy - b, c);
				SetPx(px, cx + b, cy + i, c); SetPx(px, cx - b, cy + i, c);
			}
		}
	}

	// ══════════════ 语义探测 ══════════════

	private static Shape Probe()
	{
		try
		{
			if (GodViewController.IsMouseOverGuiPublic()) return Shape.Default;
			Camera cam = GodViewController.MainCam();
			if (cam == null) return Shape.Default;
			if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 1500f)) return Shape.Default;
			Vehicle veh = hit.collider.transform.GetComponentInParent<Vehicle>();
			if (veh == null) veh = hit.collider.transform.GetComponent<Vehicle>();
			if (veh != null)
			{
				if (GodViewController.VehicleHostilePublic(veh)) return Shape.Enemy;
				return Formation.CanDrive(veh) ? Shape.Vehicle : Shape.Emplacement;
			}
			Soldier sol = hit.collider.transform.GetComponentInParent<Soldier>();
			if (sol == null) sol = hit.collider.transform.GetComponent<Soldier>();
			if (sol != null && sol.IsAlive)
				return GodViewController.FriendlyUnitPublic(sol) ? Shape.Friendly : Shape.Enemy;
			if (sol != null) return Shape.Interactable; // 1.3.3：尸体（右键可开背包）→ 浅蓝可交互
			if (GodViewController.IsBuildingHitPublic(hit)) return Shape.Building;
			try
			{
				ItemObject it = hit.collider.transform.GetComponentInParent<ItemObject>();
				if (it != null) return Shape.Interactable;
			}
			catch { }
			return Shape.Default;
		}
		catch { return Shape.Default; }
	}
}

/// <summary>反射探测附属 mod（UniversalGeneration）的放置状态。无编译期依赖。</summary>
internal static class UniversalGenProbe
{
	private static System.Reflection.PropertyInfo piPlacing;
	private static bool probed;

	internal static bool Placing
	{
		get
		{
			try
			{
				if (!probed)
				{
					probed = true;
					foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
					{
						string n = asm.GetName().Name;
						if (n != "ER2_UniversalGeneration") continue;
						Type t = asm.GetType("ER2UniversalGeneration.Placer");
						if (t != null) piPlacing = t.GetProperty("Placing", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
						break;
					}
				}
				return piPlacing != null && (bool)piPlacing.GetValue(null);
			}
			catch { return false; }
		}
	}
}
