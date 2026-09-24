using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 落点模式：面板点[条目]后进入，战场鼠标移动画预览圈，左键确认生成。
/// Shift+左键连续放置；右键/G/ESC 取消。见 docs/DESIGN.md §2.4。
/// </summary>
internal static class Placer
{
	private static GenEntry entry;
	private static string faction;
	private static string side;        // 1.1.1：面板所选阵营意图（mine/enemy/neutral）→ 决定 AI 模式
	private static SquadType? crewType; // 乘员班型（null=空车）
	private static bool placing;
	private static bool exitOnRelease; // 生成后等松开左键再退出放置（整段按压保持手势互斥，防宿主框选残影）
	private static Vector3 previewPos;
	private static bool previewValid;
	private static bool ghostPending;  // 1.0.4：幽灵预览实例正在生成
	private static bool ghostReady;    // 1.0.4：幽灵已就位（用它替代自绘圈）
	private static float previewYaw;   // 1.0.5：预览朝向（左键长按拖动旋转）
	private static bool rotating;      // 1.0.5：正在旋转朝向（左键长按拖动）

	public static bool Placing => placing;

	public static GenEntry PendingEntry() => entry;

	/// <summary>进入落点模式（面板收起，G 键被本状态占用）。</summary>
	public static void Begin(GenEntry e, string fac, string sideIntent, SquadType? ct)
	{
		entry = e; faction = fac; side = sideIntent; crewType = ct;
		placing = true;
		previewValid = false;
		ghostPending = false;
		ghostReady = false;
		previewYaw = 0f;
		rotating = false;
		leftHoldValid = false;
		leftHoldTime = -10f;
		// 1.0.6：面板里点条目那一下左键的"松开"会落进本状态机，被当成一次放置点击
		//（用户反馈"点列表条目直接就放置了，拖不出来"）。忽略到左键真正松开为止。
		ignoreUntilRelease = true;
		GenRunner.DestroyPreview();
		GenPanel.Flash(Ui.Tr("选择放置位置…"));
	}

	/// <summary>1.0.6：面板点选那次左键尚未松开 → 本帧不做放置/旋转判定。</summary>
	private static bool ignoreUntilRelease;
	private static bool warnedNoGhost; // 1.0.11：预览不可用只提示一次
	private static float ghostRetryAt; // 1.2.2：幽灵创建失败后的退避时刻（防每帧重试刷屏+拖垮帧率）

	public static void Cancel(string reason)
	{
		// 1.0.10：即便 placing 已是 false 也要清一次预览（幂等）——覆盖"标志被别处置 false、
		// 但幽灵还在场"的状态错乱（用户反馈预览可能残留）。
		bool wasPlacing = placing;
		placing = false;
		entry = null;
		ghostPending = false;
		ghostReady = false;
		rotating = false;
		ignoreUntilRelease = false;
		GenRunner.DestroyPreview();
		if (!wasPlacing) return;
		if (reason == "RTS 退出") return; // 退出 RTS 不回面板
		GenPanel.Flash(Ui.Tr("已取消放置"));
		GenPanel.SetOpen(true, false); // 回到面板（保持原类别）
	}

	/// <summary>每帧（Tick Postfix 里）更新预览与放置点击。悬停徽标上的点击忽略。</summary>
	public static void TickPlacing()
	{
		if (!placing) return;

		// 生成后：保持互斥到左键松开，再退出放置回面板（防止宿主同帧读到点击产生框选残影）
		if (exitOnRelease)
		{
			if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
			{
				exitOnRelease = false;
				placing = false;
				entry = null;
				ghostPending = false;
				ghostReady = false;
				GenRunner.DestroyPreview(); // 1.0.10：退出放置必须清预览（原来只置标志 → 幽灵残留）
				GenPanel.SetOpen(true, false);
			}
			return;
		}

		// 1.0.10 看门狗：放置模式中若宿主 RTS 已退出（面板/放置状态可能已失配）→ 强制取消并清预览。
		// 这条兜底覆盖所有"没走到 Cancel"的异常路径（宿主异常、场景切换、面板状态错乱等）。
		if (!HostLink.GodViewActive) { Cancel("RTS 退出"); return; }

		UpdatePreview();

		// 1.0.4：首个有效落点出现时生成"幽灵预览实例"（复用宿主视觉），此后跟随光标移动
		// 1.2.2：失败后 1.5s 退避——此前每帧重试（636 条警告刷屏 + 拖垮帧率，用户实测卡顿）
		if (previewValid && !ghostPending && !ghostReady && HostLink.GhostAvailable && entry != null
			&& Time.unscaledTime >= ghostRetryAt)
		{
			ghostPending = true;
			GenEntry eRef = entry;
			string fRef = faction;
			Vector3 p0 = previewPos;
			GenRunner.SpawnPreviewGhost(eRef, p0, fRef, ok =>
			{
				ghostPending = false;
				ghostReady = ok;
				if (!ok) ghostRetryAt = Time.unscaledTime + 1.5f;
				// 1.0.11：失败必须**无条件**可见——此前只有 debugLog 才打印，导致"预览不显示"无从定位
				if (ok) { if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 幽灵预览就绪"); }
				else Plugin.ModLog.LogWarning("[UniGen] 幽灵预览创建失败（宿主 GhostPreview 返回 false）——1.5s 后重试");
			});
		}
		// 1.0.7：旋转中只改朝向、不动位置（MovePreviewTo 会把整队按鼠标新落点平移）
		// 1.0.11：放置模式中宿主预览不可用时给出一次性提示（避免"点了没反应"的困惑）
		if (!HostLink.GhostAvailable && !warnedNoGhost && entry != null)
		{
			warnedNoGhost = true;
			Plugin.ModLog.LogWarning("[UniGen] 宿主幽灵预览不可用（GhostPreview.Ghostify 缺失或失败）——放置将无模型预览");
		}
		if (ghostReady)
		{
			if (!rotating) GenRunner.MovePreviewTo(previewPos);
			GenRunner.SetPreviewYaw(previewYaw);
		}

		bool guiNow = GenPanel.IsMouseOverBadge();
		bool leftDown = Input.GetMouseButtonDown(0);
		bool leftHeld = Input.GetMouseButton(0);
		bool leftUp = Input.GetMouseButtonUp(0);
		bool rightDown = Input.GetMouseButtonDown(1);
		bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

		// 1.0.6：吞掉"从面板点进放置模式"的那一次按压（否则它会被当成放置点击，拖不出朝向）
		if (ignoreUntilRelease)
		{
			if (Input.GetKeyDown(KeyCode.Escape)) { Cancel("ESC"); return; }
			if (rightDown) { Cancel("右键"); return; }
			if (leftUp || !leftHeld) ignoreUntilRelease = false; // 松开后正常进入手势判定
			else return;                                          // 仍按住：只刷新预览，不判定手势
		}

		// 1.0.5（用户要求）：左键**长按拖动 = 旋转生成物朝向**（松手不放置）；
		// 短按（未进入旋转）= 确认放置。旋转以横向拖动像素为准（每 3px 转 1°）。
		if (leftDown && !guiNow)
		{
			leftHoldTime = Time.unscaledTime;
			leftHoldPos = Input.mousePosition;
			leftHoldValid = previewValid;
		}
		if (leftHeld && !guiNow && leftHoldValid && !rotating
			&& Time.unscaledTime - leftHoldTime >= RotateHoldSeconds)
		{
			rotating = true;
			GenPanel.Flash(Ui.Tr("旋转朝向：左右拖动（松手完成）"));
		}
		if (rotating && leftHeld)
		{
			float dx = Input.mousePosition.x - leftHoldPos.x;
			if (Mathf.Abs(dx) > 0.5f)
			{
				previewYaw = Mathf.Repeat(previewYaw + dx * 0.33f, 360f);
				leftHoldPos = Input.mousePosition;
			}
		}

		if (Input.GetKeyDown(KeyCode.Escape))
		{
			Cancel("ESC");
			return;
		}
		if (rightDown)
		{
			Cancel("右键");
			return;
		}
		if (leftUp)
		{
			bool wasRotating = rotating;
			rotating = false;
			if (wasRotating || !leftHoldValid || guiNow) return; // 旋转结束/无效按下：不放置
			Plugin.ModLog.LogInfo("[UniGen] 放置点击 valid=" + previewValid + " pos=" + previewPos + " ghost=" + ghostReady + " yaw=" + previewYaw.ToString("0"));
			if (!previewValid)
			{
				GenPanel.Flash(Ui.Tr("此处无法放置（未命中地面）"), true);
				return;
			}
			// 1.0.4：幽灵只是预览——确认时销毁它，再走正式生成（避免把预览实例当成品）
			GenRunner.DestroyPreview();
			ghostReady = false;
			ExecuteSpawn(previewPos);
			if (!shift)
			{
				exitOnRelease = true; // 本帧保持 placing=true（互斥生效），松开后退出
			}
			else
			{
				GenPanel.Flash(Ui.Tr("已放置，可继续（右键结束）"));
			}
		}
	}

	private static float leftHoldTime = -10f;
	private static Vector3 leftHoldPos;
	private static bool leftHoldValid;
	private const float RotateHoldSeconds = 0.28f; // 超过此时长按判定为"旋转朝向"而非"放置"

	/// <summary>1.0.7：旋转朝向期间**冻结落点**——鼠标横向移动只改朝向，预览不再跟着鼠标平移
	///（用户反馈"改变方向时幽灵单位也跟着动，不应该在原地只转向"）。</summary>
	private static void UpdatePreview()
	{
		if (rotating) return; // 旋转中：保持 previewPos/previewValid 不变
		previewValid = false;
		// 上帝视角下 Camera.main 可能拿不到（宿主同款：ResourcesManager.mainCamera 优先）
		Camera cam = null;
		try { cam = ResourcesManager.mainCamera; } catch { }
		if (cam == null) { try { cam = Camera.main; } catch { } }
		if (cam == null) return;
		try
		{
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hit, 6000f))
			{
				// 1.0.8：放宽法线阈值 0.4 → 0.15（用户反馈"地图稍稍不平就无法放置"）。
				// 只要不是近乎垂直的陡壁都接受；幽灵预览本身会让玩家看清落点是否合适。
				Vector3 n = hit.normal;
				if (n.y > 0.15f)
				{
					previewPos = hit.point;
					previewValid = true;
				}
			}
		}
		catch { }
	}

	private static void ExecuteSpawn(Vector3 pos)
	{
		GenEntry e = entry;
		string fac = faction;
		string sd = side;
		SquadType? crewT = crewType;
		if (e == null) return;

		if (e.IsInfantry)
		{
			GenRunner.SpawnInfantrySquad(e, pos, fac, sd, sq =>
			{
				if (sq != null) GenPanel.Flash(Ui.Tr("已生成 ") + e.Title + Ui.Tr("（") + FacName(fac) + "）");
				else GenPanel.Flash(Ui.Tr("生成失败：") + e.Title, true);
			});
		}
		else
		{
			GenRunner.SpawnVehicle(e, pos, fac, crewT, veh =>
			{
				if (veh != null) GenPanel.Flash(Ui.Tr("已生成 ") + e.Title + Ui.Tr("（") + FacName(fac) + "）");
				else GenPanel.Flash(Ui.Tr("生成失败：") + e.Title, true);
			});
		}
	}

	private static string FacName(string fac)
	{
		return FactionData.IsFriendly(fac, GenRunner.MyFaction()) ? Ui.Tr("我方") : Ui.Tr("敌方");
	}

	/// <summary>OnGUI 末段画预览标识。1.0.5（用户要求）：**只保留幽灵模型预览，删除绿色圈**。
	/// 幽灵不可用（宿主缺失/克隆失败）时也不画圈——改为底部徽标文字提示，避免误导。</summary>
	public static void DrawPreview()
	{
		// 1.0.5：绿色圈已按用户要求删除；预览一律由幽灵模型承担（GenRunner.SpawnPreviewGhost）。
	}
}
