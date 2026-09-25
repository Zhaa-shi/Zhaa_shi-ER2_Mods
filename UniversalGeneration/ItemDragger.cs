using System;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 2.0.0：物品拖放 —— 鼠标"拿起一件物品"（面板条目 / 已持有物），
/// 松手落在**士兵身上** = 放进他的背包；落在**地面** = 丢到地上生成实体。
///
/// 手势契约（与 Placer 落点模式同一套，避免两套状态机打架）：
/// - 拿起时 `GenPanel.IsOpen` 保持 true 但面板缩成徽标（DrawCarryBadge），
///   `ExternalGuiBlockRect()` 返回全屏 → 宿主完全让位，拖放点击不会触发原生框选/指令。
/// - `ignoreUntilRelease`：面板里点条目那一下左键的"松开"会落进本状态机（Placer 1.0.6 同款教训），
///   必须吞掉到真正松开为止，否则"点一下就直接丢地上"。
/// - 右键 / ESC / G 键 = 放弃携带（Placer.Cancel 同款）。
/// - 看门狗：宿主 RTS 退出 → 强制取消。
/// </summary>
internal static class ItemDragger
{
	private static bool carrying;
	private static string itemId;
	private static string itemTitle;
	private static bool ignoreUntilRelease;
	private static Vector3 ghostPos;
	private static bool ghostValid;

	// 2.2.0：3D 幽灵模型预览（替代"地面光圈 + 光标图标"——用户要求只要模型）
	private static bool ghostModelReady;
	private static bool ghostModelPending;
	private static float ghostRetryAt; // 失败 1.5s 退避（Placer 1.2.2 同款，防刷屏+拖垮帧率）
	private static bool warnedNoGhost; // 2.3.0：宿主幽灵不可用只提示一次（Placer 1.0.11 同款）

	// 目标高亮（拖放过程中实时判定，给玩家"要放进谁"的即时反馈）
	private static Soldier hoverSoldier;
	private static float hoverNameAt;
	private static string hoverName = "";

	internal static bool Carrying => carrying;
	internal static string CarriedId => itemId;
	internal static string CarriedTitle => itemTitle;

	/// <summary>开始携带一件物品（面板条目点击 / 拖拽起步）。</summary>
	internal static void Begin(ItemEntry e)
	{
		if (e == null) return;
		carrying = true;
		itemId = e.Id;
		itemTitle = e.Title;
		ignoreUntilRelease = true;   // 面板点选那一下的松开不算放置（Placer 1.0.6 教训）
		ghostValid = false;
		hoverSoldier = null;
		// 关闭面板 → 全屏让位给拖放（关掉才能看到落点，也避免面板吃掉松手事件）
		GenPanel.SetOpen(false, false);
		// 2.5.30：**拆成两个 Tr 调用**——上一版把" · Shift 连续"并进同一字面量，字典键对不上
		// → 英文版整句回退中文（用户实测）。旧键 EN 已存在，新键 " · Shift 连续" 也已存在。
		GenPanel.Flash(Ui.Tr("携带 ") + e.Title + Ui.Tr("：拖到单位身上放背包，拖到地上丢弃") + Ui.Tr(" · Shift 连续"));
	}

	/// <summary>取消携带（右键/ESC/G/宿主退出）。幂等。</summary>
	internal static void Cancel(string reason, bool reopenPanel = true)
	{
		bool was = carrying;
		carrying = false;
		itemId = null;
		itemTitle = null;
		ignoreUntilRelease = false;
		ghostValid = false;
		hoverSoldier = null;
		DiscardGhostModel(); // 2.2.0：预览模型一并收口（幂等）
		if (!was) return;
		if (reason == "RTS 退出") return;
		if (reopenPanel) GenPanel.SetOpen(true, false);
	}

	/// <summary>每帧驱动（GenDriver.Tick 内、Placer 之后调用）。</summary>
	internal static void Tick()
	{
		if (!carrying) return;

		// 看门狗：宿主 RTS 退出 → 强制收口（与 Placer 1.0.10 同款）
		if (!HostLink.GodViewActive) { Cancel("RTS 退出", false); return; }

		bool leftHeld = Input.GetMouseButton(0);
		bool leftUp = Input.GetMouseButtonUp(0);
		bool rightDown = Input.GetMouseButtonDown(1);

		// 吞掉"从面板点进携带模式"的那次按压（否则松手即被当成一次投放）
		// 2.0.1 修复（用户实测"按下左键选取后直接就放置了"）：面板点击的 OnGUI MouseUp 与
		// Update 的 GetMouseButtonUp **在同一帧都成立**——清掉标志后**必须立即 return**，
		// 否则同一帧会继续往下走到 Drop()，"点一下就丢出去"。原实现清了标志却继续执行 = 形同虚设。
		if (ignoreUntilRelease)
		{
			if (Input.GetKeyDown(KeyCode.Escape)) { Cancel("ESC"); return; }
			if (rightDown) { Cancel("右键"); return; }
			if (leftUp || !leftHeld)
			{
				ignoreUntilRelease = false; // 这次松手已被消化，本帧到此为止
				return;
			}
			UpdateHover(); // 仍按住：只刷新预览，不判定手势
			return;
		}

		if (Input.GetKeyDown(KeyCode.Escape)) { Cancel("ESC"); return; }
		if (rightDown) { Cancel("右键"); return; }

		UpdateHover();

		// 2.0.1：放置判定改为**按住状态下松手**——leftUp 那一帧 GetMouseButton(0) 已为 false，
		// 所以判据必须 leftUp 优先、leftHeld 兜后（否则吞掉唯一一次投放）。
		if (leftUp) { Drop(); return; }
		if (!leftHeld) return; // 键盘态：只更新目标高亮
	}

	/// <summary>实时判定鼠标下的投放目标（士兵优先，其次地面）。</summary>
	private static void UpdateHover()
	{
		hoverSoldier = null;
		Soldier s = ItemSpawner.SoldierUnderMouse();
		if (s != null) hoverSoldier = s;

		ghostValid = ItemSpawner.GroundUnderMouse(out Vector3 g);
		ghostPos = g;

		// 2.2.0：幽灵模型跟随（悬停单位时收起——要进背包，不是落地）
		UpdateGhostModel();

		// 目标名字（0.1s 节流；.name 是 interop 字符串读，别每帧取）
		if (hoverSoldier != null)
		{
			if (Time.unscaledTime - hoverNameAt > 0.2f)
			{
				hoverNameAt = Time.unscaledTime;
				hoverName = "";
				try { hoverName = hoverSoldier.name; } catch { }
				if (string.IsNullOrEmpty(hoverName)) hoverName = Ui.Tr("单位");
			}
		}
		else hoverName = "";
	}

	/// <summary>
	/// 2.2.0：幽灵模型生命周期。地面落点有效时显示（异步创建 + 失败退避）；
	/// 悬停到单位或落点无效时收起。复用 GenRunner 的预览代数 guard（确认/取消时统一 DestroyPreview）。
	/// </summary>
	private static void UpdateGhostModel()
	{
		bool wantModel = hoverSoldier == null && ghostValid && !string.IsNullOrEmpty(itemId);
		if (!wantModel)
		{
			DiscardGhostModel();
			return;
		}
		if (ghostModelReady && GenRunner.HasPreview)
		{
			GenRunner.MovePreviewTo(ghostPos);
			return;
		}
		if (ghostModelPending || Time.unscaledTime < ghostRetryAt) return;
		if (!HostLink.GhostAvailable)
		{
			// 2.3.0：一次性警告（Placer 1.0.11 同款）——静默降级曾让"预览不显示"无从排查
			if (!warnedNoGhost)
			{
				warnedNoGhost = true;
				Plugin.ModLog.LogWarning("[UniGen] 宿主幽灵预览不可用（GhostPreview.Ghostify 缺失）——物品携带将无 3D 模型预览");
			}
			return;
		}

		ghostModelPending = true;
		string idRef = itemId;
		GenRunner.SpawnItemGhost(idRef, ghostPos, ok =>
		{
			ghostModelPending = false;
			ghostModelReady = ok;
			if (!ok) ghostRetryAt = Time.unscaledTime + 1.5f;
		});
	}

	/// <summary>2.2.0：收起幽灵模型（幂等）。</summary>
	private static void DiscardGhostModel()
	{
		if (GenRunner.HasPreview) GenRunner.DestroyPreview();
		ghostModelReady = false;
		ghostModelPending = false;
	}

	private static void Drop()
	{
		Soldier target = hoverSoldier;
		bool onGround = ghostValid;
		Vector3 pos = ghostPos;
		string id = itemId;
		string title = itemTitle;

		if (target != null)
		{
			// ① 落到单位身上 → 进背包
			if (ItemSpawner.GiveToSoldier(target, id, out string msg))
			{
				GenPanel.Flash(Ui.Tr("已放入 ") + title + " → " + hoverName);
				Plugin.ModLog.LogInfo("[UniGen] 拖放入包: " + id + " → " + hoverName);
			}
			else GenPanel.Flash(msg, true);
			Cancel("投放完成");
			return;
		}

		if (onGround)
		{
			// ② 落到地面 → 生成世界实体
			GameObject go = ItemSpawner.DropAt(id, pos, out string msg);
			if (go != null)
			{
				// 2.5.29（用户："让通用生成mod也支持长按shift连续放置物品"）：
				// **Shift 按住时丢地上后继续保持携带**——连点连放，右键/ESC/G 结束
				//（与 Placer 单位放置的 Shift 连续同款体验）。放进背包不适用（物品已易主）。
				bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
				if (shift)
				{
					GenPanel.Flash(Ui.Tr("已丢下 ") + title + Ui.Tr("（Shift 连续放置中，右键结束）"));
					Plugin.ModLog.LogInfo("[UniGen] Shift 连续放置: " + id + " @ " + pos);
					return;   // 不 Cancel：携带状态与幽灵模型原样保留，下一击继续放置
				}
				GenPanel.Flash(Ui.Tr("已丢下 ") + title);
			}
			else GenPanel.Flash(msg, true);
			Cancel("投放完成");
			return;
		}

		// ③ 两者都不是：给明确原因，不收手（允许玩家继续找落点）
		// 2.0.1：区分"鼠标没对准任何东西"与"对准了但两个通道都不可用"，方便排查
		GenPanel.Flash(Ui.Tr("此处无法放置（对准单位或地面）"), true);
		if (Plugin.debugLog.Value)
			Plugin.ModLog.LogInfo("[UniGen] 投放未生效: id=" + id + " target=null ground=false mouse=" + Input.mousePosition);
	}

	/// <summary>
	/// 拖放状态下的全屏手势互斥（与 Placer.Placing 同款）：
	/// 携带中宿主完全让位，避免投放的那次点击同时触发原生框选/移动指令。
	/// </summary>
	internal static Rect? BlockRect()
	{
		if (!carrying) return null;
		return new Rect(0f, 0f, Screen.width, Screen.height);
	}

	/// <summary>携带中：目标单位高亮 + 3D 幽灵模型 + 底部提示条。
	/// 2.2.0（用户要求"只要模型"）：地面落点不再画光圈、光标不再画图标——
	/// 落点预览由幽灵模型承担；悬停单位时的绿色目标环保留（那是"放进谁的背包"的指向反馈，不是落点预览）。</summary>
	internal static void Draw()
	{
		if (!carrying) return;

		// 目标单位高亮：脚下光环（世界坐标 → 屏幕，4 个角取中心 + 半径）
		if (hoverSoldier != null) DrawTargetRing(hoverSoldier);

		DrawCarryBadge();
	}
	/// <summary>目标单位脚下的高亮环（世界空间 4 角投影，绕开 GetWorldCorners 全零陷阱 14）。</summary>
	private static void DrawTargetRing(Soldier s)
	{
		try
		{
			Camera cam = ItemSpawner.CurrentCamera();
			if (cam == null || s.transform == null) return;
			Vector3 c = s.transform.position;
			// 2.5.1：亮绿 → 白（Er2Ui.WSelected）。用户要求"标记点等都用白色或半透明的灰色"。
			Color col = ER2Shared.Er2Ui.WSelected;
			DrawWorldRing(cam, c + Vector3.up * 0.05f, 0.85f, col);
		}
		catch { }
	}

	/// <summary>世界空间圆环 → 屏幕线段（用 TransformPoint 绕开 GetWorldCorners 陷阱）。</summary>
	private static void DrawWorldRing(Camera cam, Vector3 center, float radius, Color col)
	{
		const int seg = 28;
		Vector2 prev = Vector2.zero;
		bool hasPrev = false;
		Color keep = GUI.color;
		GUI.color = col;
		for (int i = 0; i <= seg; i++)
		{
			float a = i / (float)seg * Mathf.PI * 2f;
			Vector3 world = center + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
			Vector3 sp = cam.WorldToScreenPoint(world);
			if (sp.z <= 0f) { hasPrev = false; continue; }
			Vector2 p = new Vector2(sp.x, Screen.height - sp.y);
			if (hasPrev) DrawLine(prev, p);
			prev = p;
			hasPrev = true;
		}
		GUI.color = keep;
	}

	/// <summary>IMGUI 画线（GUI.DrawTexture 旋转矩阵；本环境验证过的路径）。</summary>
	private static void DrawLine(Vector2 a, Vector2 b)
	{
		float ang = Mathf.Atan2(b.y - a.y, b.x - a.x) * Mathf.Rad2Deg;
		float len = Vector2.Distance(a, b);
		Matrix4x4 keep = GUI.matrix;
		GUIUtility.RotateAroundPivot(ang, a);
		GUI.DrawTexture(new Rect(a.x, a.y, len, 1.6f * ER2Shared.Er2Ui.Scale), Texture2D.whiteTexture);
		GUI.matrix = keep;
	}

	/// <summary>底部提示条（携带模式的说明 + 当前目标）。</summary>
	private static void DrawCarryBadge()
	{
		GenPanel.EnsureStylesPublic();
		Rect r = BadgeRect();
		GUI.color = ER2Shared.Er2Ui.Scrim;   // 2.5.1：军绿底 → 中性遮罩
		GUI.DrawTexture(r, Texture2D.whiteTexture);
		GUI.color = Color.white;

		string target;
		if (hoverSoldier != null) target = Ui.Tr("松手 → 放入 ") + hoverName + Ui.Tr(" 的背包");
		else if (ghostValid) target = Ui.Tr("松手 → 丢到地上");
		else target = Ui.Tr("对准单位或地面");
		string txt = Ui.Tr("携带: ") + itemTitle + "    " + target + Ui.Tr("    右键 取消");
		float m = 8f * ER2Shared.Er2Ui.Scale;   // 2.5.1：内缩随倍率
		GUI.Label(new Rect(r.x + m, r.y + m * 0.5f, r.width - m * 2f, 22f * ER2Shared.Er2Ui.Scale), txt, GenPanel.FlashStylePublic());
	}

	public static Rect BadgeRect()
	{
		// 2.5.1：宽度自适应 + 收敛进屏幕（原来硬编码 560px，小屏会溢出）
		float w = ER2Shared.Er2Ui.ScreenFit(560f);
		float h = 28f * ER2Shared.Er2Ui.Scale;
		return new Rect((Screen.width - w) * 0.5f, Screen.height - 62f * ER2Shared.Er2Ui.Scale, w, h);
	}

	internal static bool IsMouseOverBadge()
	{
		if (!carrying) return false;
		return BadgeRect().Contains(ItemSpawner.MouseScreenPos());
	}
}
