using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 上帝视角 v0.7.42（RTS 指挥，0.7.41 稳定基线清理版）。
///   左键：单击选友军（临时指挥，不建队）/拖框/双击选队/空白清选/Shift 追加
///   右键短按：空白=移动(M7)｜敌军=叛徒标记+推进｜友军载具/车内兵=交互环｜徒步友军=合并环
///   右键长按 0.35s：单位环（站起[resetPose]/蹲下/趴下[setPose+还原名单]/停止）
///   交互环：上车（成功后转选车组）/下车/修理（原生 OrderRepairVehicle）/合并（12 上限，坦克可并）
///   顶栏按钮：控制该小队 / 分队（新建组）/ 合并（并入当前激活 RTS 组）
///   空格暂停；F9 进出；顶部按钮接管（保护窗+独苗转移）；退出还原原小队/姿态/标记/状态
///   标记=持久集火（GetBestVisibleEnemy/CurrentVisibleTarget Postfix + LOS 缓存）+ 推进状态机
///   死亡/换队链已冻结（0.7.40/41：停用 ClearSquadList 与 EnsurePlayerSquadHasCandidates）
///   光标防闪烁：Cursor set_lockState/visible patch + FrameEndRunner 兜底
/// </summary>

internal static class GodViewController
{
	internal static bool Active { get; private set; }
	internal static string SavedFaction = "";

	private static Soldier savedSoldier;
	internal static Soldier SavedSoldier => savedSoldier;

	// 相机
	private static Vector3 camPos;
	private static Quaternion camRot;
	private static float yaw, pitch;
	private static bool midDrag;
	private static float lastDragX, lastDragY;

	// ===== 选择状态（重写核心） =====
	// mainSquad = 当前指挥的步兵小队（框选时真实新建）；selVehicles = 选中的载具/火力点车组
	private static Squad mainSquad;
	private static readonly List<Squad> selVehicles = new List<Squad>();
	// 0.7.43：被点选 Vehicle 的真实引用（A/B 隔离核心）——光标/下车/标签以它为准
	private static readonly List<Vehicle> selVehicleRefs = new List<Vehicle>();
	// 0.7.46：分队后创建的当前 RTS 分队（0.7.56 起降级为"激活组"标记，不参与驾驶判定）
	private static Squad rtsSquad;
	// 0.7.56 P0：全部 RTS 分队指针集合——车辆驾驶判定改用集合成员关系（多组共存不互踩）
	private static readonly HashSet<long> rtsSquadSet = new HashSet<long>();

	// 0.7.44：Vehicle 选择 → 乘员 Soldier 展平（控制权只到 Soldier 级，绝不经共享 Squad）
	private static List<Soldier> SelectedVehicleOccupants()
	{
		List<Soldier> occ = new List<Soldier>();
		foreach (Vehicle rv in selVehicleRefs)
		{
			if (rv == null || rv.transform == null) continue;
			try
			{
				Soldier[] os = rv.GetComponentsInChildren<Soldier>();
				if (os == null) continue;
				for (int i = 0; i < os.Length; i++)
					if (os[i] != null && os[i].IsAlive) occ.Add(os[i]);
			}
			catch { }
		}
		return occ;
	}

	private static bool AnySelRefOccupied()
	{
		foreach (Vehicle v in selVehicleRefs)
		{
			try { if (v != null && v.transform != null && FirstCrew(v) != null) return true; } catch { }
		}
		return false;
	}

	private static void AddVehicleRef(Vehicle v)
	{
		if (v == null) return;
		try
		{
			long ptr = (long)v.Pointer;
			foreach (Vehicle x in selVehicleRefs) { try { if (x != null && (long)x.Pointer == ptr) return; } catch { } }
			selVehicleRefs.Add(v);
		}
		catch { }
	}

	private static void PruneVehicleRefs()
	{
		for (int i = selVehicleRefs.Count - 1; i >= 0; i--)
		{
			Vehicle v = selVehicleRefs[i];
			try { if (v == null || v.transform == null) selVehicleRefs.RemoveAt(i); } catch { selVehicleRefs.RemoveAt(i); }
		}
	}
	// 单击虚拟选择（不建队）：virtualUnits 非空时优先
	private static readonly List<Soldier> virtualUnits = new List<Soldier>();
	private static bool addingToSelection;   // 本次操作是否 Shift 追加
	private static float selFlash = -10f;
	internal const int MaxSelection = 30;

	// 小队列表（右下角，编号+符号）
	private static List<Squad> cachedFriendlySquads = new List<Squad>();
	private static float squadPanelRefresh = -10f;
	private static float lastPanelClickTime = -10f;
	private static IntPtr lastPanelClickSquad;
	private static Rect squadPanelHit;

	// 命令目标（可视化）
	private static Vector3 cmdTarget;
	private static bool hasCmdTarget;
	private static float cmdTargetUntil = -10f;
	private static Vector3 squadCenterDraw;

	// 双击/框选
	private static float lastClickTime = -10f;
	private static Vector2 lastClickPos;

	// 反馈
	private static string cmdFlash = "";
	private static float cmdFlashUntil = -10f;
	private static bool flyingToSquad;
	private static Vector3 flyTarget;
	private static float flyStartTime;
	private static Vector3 flyStartPos;

	// ===== 交互命令环（仅上车/下车/修理，不含 Move/Attack）=====
	private static bool showInteractionWheel = false;
	private static Vector2 wheelScreenPos;
	private static Vehicle wheelTargetVehicle;      // 目标载具（上车/开车）
	private static Squad wheelTargetVehicleCrew;    // 目标载具车组（下车）
	private static Soldier wheelTargetSoldier;      // 目标友军士兵（上车/物品）
	// 布局常量：三项 120° 均布在半径 WheelItemDist 圆周上（正上/右下/左下）。
	// 相邻按钮中心距 = 2*72*sin60° ≈ 125px > 按钮宽 84px，保证互不重叠。
	private const float WheelBtnW = 84f;
	private const float WheelBtnH = 32f;
	private const float WheelItemDist = 72f;      // 按钮中心到环心距离
	private const float WheelEdgeMarginX = 110f;  // 环心离屏幕左右边最小值（按钮横向最远伸出约 105px，防出屏）
	private const float WheelEdgeMarginY = 96f;   // 环心离屏幕上下边最小值（上方按钮纵向最远伸出约 88px）
	// 动态条目：kind0=交互环(上车/下车/修理/合并)，kind2=单位环(站/蹲/趴/停止)；槽位按数量均布
	private const int WheelSlotMax = 4;
	private static readonly string[] WheelItemLabels = new string[WheelSlotMax];
	private static readonly bool[] WheelItemEnabled = new bool[WheelSlotMax];
	private static int wheelItemCount;
	/// <summary>轮盘类型：0=交互环（右键载具/车内士兵），2=常驻单位环（选中后长按右键）。友军环已移除（合并上移顶栏）。</summary>
	private static int wheelKind;

	// 开环右键豁免：Update 阶段 GetMouseButtonDown(1) 开环后，同一次按压的 MouseDown(1)
	// 仍会进入本帧 IMGUI 队列——若不豁免，环会在打开的同一帧被自己的"右键关闭"吃掉。
	// 窗口内且未移出豁免半径的右键 Down 不视为"第二次右键"；两条阈值常量均可调。
	private const float WheelOpenGuardSeconds = 0.25f;   // 开环后的豁免时长
	private const float WheelOpenGuardPixels = 30f;     // 豁免最大位移（像素）
	// 常驻单位环的世界锚点（开环时鼠标射线落点）：环随镜头/世界一致移动
	private static Vector3 wheelAnchorWorld;
	private static bool hasWheelAnchor;
	private static float wheelOpenedAtTime = -10f;
	private static Vector2 wheelOpenedAtGuiPos = Vector2.zero; // 开环时鼠标位置（IMGUI 左上原点）
	// 关环后吞掉同一次左键手势的余下部分：松开事件发生在环已关闭的下一帧，
	// 会落进普通 ClickActOrCancel 把刚执行的命令结果（如转选车组）静默清掉
	private static bool swallowLeftGesture;
	// ◆ 标记绘制缓存（OnGUI 每帧多 pass）
	private static List<Soldier> markerCache = new List<Soldier>();
	private static float markerCacheUntil = -10f;

	// ===== M7：移动 Command State =====
	private static bool mvActive;
	private static Vector3 mvTarget;
	private static readonly List<Soldier> mvUnits = new List<Soldier>();
	private static readonly Dictionary<long, float> mvLastDist = new Dictionary<long, float>();
	// 0.7.62 P2：移动路径质量统计（直线距离 vs 实际路程 vs 修正次数）
	private static readonly Dictionary<long, Vector3> mvLastPos = new Dictionary<long, Vector3>();
	private static readonly Dictionary<long, double> mvPathAcc = new Dictionary<long, double>();
	private static float mvQualityStart = -10f;
	private static int mvFixCount;
	private static float mvArriveDist = 8f; // 0.7.75：本次移动的到达判定半径（fast=30m，避免与原生散开拉扯）
	private static float mvLastCheck = -10f;
	private static readonly List<Soldier> rushNoEngage = new List<Soldier>(); // 冲锋期禁索敌名单

	/// <summary>M7：每 2s 检查移动状态——只对"未在接近目标"的单位做必要修正，不整队轰炸。</summary>
	/// <summary>0.7.62 P2：每帧累积选中单位实际路程（绕路量化的采样源）。</summary>
	private static void MoveQualityTick()
	{
		if (!mvActive || mvQualityStart < 0f) return;
		foreach (Soldier u in mvUnits)
		{
			try
			{
				if (u == null || u.transform == null) continue;
				long k = (long)u.Pointer;
				if (mvLastPos.TryGetValue(k, out Vector3 prev))
				{
					mvPathAcc.TryGetValue(k, out double acc);
					mvPathAcc[k] = acc + (u.transform.position - prev).magnitude; // 累积实际路程
				}
				mvLastPos[k] = u.transform.position;
			}
			catch { }
		}
	}

	/// <summary>0.7.62 P2：移动结束输出路径质量（直线/路程/绕路系数/修正次数/用时）。</summary>
	private static void EmitMoveQuality()
	{
		try
		{
			double path = 0; double line = 0; int n = 0;
			foreach (Soldier u in mvUnits)
			{
				if (u == null || u.transform == null) continue;
				long k = (long)u.Pointer;
				line += (u.transform.position - mvTarget).magnitude;
				if (mvPathAcc.TryGetValue(k, out double acc)) path += acc;
				n++;
			}
			double ratio = path > 0.01 && line > 0.01 ? path / line : 1.0;
			SquadCmdLogic.Log("[MoveQuality] 单位=" + n + " 直线剩余=" + line.ToString("0.0") + "m"
				+ " 尾段路程=" + path.ToString("0.0") + "m"
				+ " 修正次数=" + mvFixCount
				+ " 用时=" + (Time.unscaledTime - mvQualityStart).ToString("0.0") + "s"
				+ "（对照：直线剩余/路程 比值越小越直接）");
		}
		catch { }
		mvLastPos.Clear(); mvPathAcc.Clear(); mvFixCount = 0; mvQualityStart = -10f;
	}

	private static void TickMove()
	{
		if (!mvActive) return;
		if (mvFromMark)
		{
			try { var m = CurrentMark; if (m == null) { mvActive = false; return; } mvTarget = m.Position; }
			catch { mvActive = false; return; }
		}
		float now = Time.unscaledTime;
		if (now - mvLastCheck < Plugin.m7Interval.Value) return;
		mvLastCheck = now;
		try
		{
			for (int i = mvUnits.Count - 1; i >= 0; i--)
				if (mvUnits[i] == null || !mvUnits[i].IsAlive || mvUnits[i].transform == null) mvUnits.RemoveAt(i);
			if (mvUnits.Count == 0) { mvActive = false; return; }
			if (Time.unscaledTime - mvQualityStart > Plugin.trackSeconds.Value)
			{
				mvActive = false; mvLastDist.Clear();
				SquadCmdLogic.Log("[SquadCmd] MOVE 超时 " + Plugin.trackSeconds.Value + "s");
				EmitMoveQuality();
				return;
			}
			int arrived = 0, stalled = 0;
			List<Soldier> fix = new List<Soldier>();
			foreach (Soldier u in mvUnits)
			{
				float d = (u.transform.position - mvTarget).magnitude;
				long key = (long)u.Pointer;
				bool prev = mvLastDist.TryGetValue(key, out float pd);
				if (d <= mvArriveDist) { arrived++; mvLastDist[key] = d; continue; }
				if (!prev || d < pd - 0.5f) mvLastDist[key] = d;      // 在接近：不打扰
				else { stalled++; fix.Add(u); }                        // 停滞/被抢任务：待修正
			}
			if (arrived >= mvUnits.Count)
			{
				mvActive = false; mvLastDist.Clear();
				ResetEngagement(); // 0.7.66：到达恢复交战
				SquadCmdLogic.Log("[SquadCmd] MOVE 完成 " + arrived + " 单位");
				EmitMoveQuality();
				return;
			}
			if (stalled > 0)
			{
				mvFixCount += stalled;
				foreach (Soldier u in fix)
				{
					try
					{
						new Lua_Soldier(u).stop();
						AiParams ap = new Lua_Soldier(u).getAiParams();
						try { ap.followCustomSquadOrders(); } catch { }
						try { ap.followCustomDirectCommands(); } catch { }
						try { ap.allowMovements(true); } catch { }
						// 0.7.66 任务优先：被修正的单位途中禁索敌（战斗不再拉停任务），到达后恢复
						try { ap.allowCheckForEnemies(false); if (!rushNoEngage.Contains(u)) rushNoEngage.Add(u); } catch { }
						new Lua_Soldier(u).moveTo(mvTarget);
						mvLastDist[(long)u.Pointer] = (u.transform.position - mvTarget).magnitude;
					}
					catch { }
				}
				SquadCmdLogic.Log("[SquadCmd] MOVE 修正 单位=" + stalled + "/" + mvUnits.Count + " 距目标=" + ((fix.Count > 0 ? (fix[0].transform.position - mvTarget).magnitude : 0f)).ToString("0.0") + "m");
			}
		}
		catch { }
	}

	/// <summary>恢复交战能力（普通移动/停止时解除冲锋禁索敌）。</summary>
	private static void ResetEngagement()
	{
		foreach (Soldier s in rushNoEngage)
		{
			try { new Lua_Soldier(s).getAiParams().allowCheckForEnemies(true); } catch { }
		}
		rushNoEngage.Clear();
	}

	// 右键长按手势状态（长按=常驻命令环，短按=直接指令）
	private const float RightLongPressSeconds = 0.35f;
	private static bool rightHoldActive;
	private static bool rightLongPressOpened;
	private static bool rightGestureWheelOpen;
	private static float rightDownTime = -10f;
	private static Vector2 rightDownScreenPos;
	private static float lastRightBlankClickTime = -10f;
	// ===== 标记敌军（集火）——0.7.63 最终形态重建（0.7.68 误删恢复） =====
	internal sealed class MarkedTarget
	{
		public Spottable Spottable;
		public Soldier Soldier;
		public Vehicle Vehicle;
		public Vector3 Position;
		public string Faction;
		public string Name;
		public float Until;
		/// <summary>M6：超范围降级（恢复范围内+视线后自动恢复）</summary>
		public bool Downgraded;
		public bool Active => Until == float.MaxValue || Until > Time.unscaledTime;
	}
	private static MarkedTarget mark;

	/// <summary>当前标记（GetBestVisibleEnemy 覆盖用；null/过期 = 无标记）。</summary>
	internal static MarkedTarget CurrentMark => (mark != null && mark.Active && !mark.Downgraded) ? mark : null;
	/// <summary>标记生效半径（米，集火距离）。</summary>
	internal static float MarkRadius = 500f;
	private static bool mvFromMark;
	private static float markLostSightSince = -1f;
	private const float MarkLoseSightGraceSeconds = 6f;
	private static float pruneMarkNext;
	private static int markInteropFailures;
	private const int MarkInteropFailureLimit = 3;
	/// <summary>LOS/范围缓存（0.5s 刷新）：GetBestVisibleEnemy Postfix 用它替代每兵每帧射线。</summary>
	internal static bool MarkLosCached = true;

	private static void PruneMark()
	{
		if (mark == null) return;
		if (Time.unscaledTime < pruneMarkNext) return;
		pruneMarkNext = Time.unscaledTime + 0.5f;
		if (!mark.Active) { mark = null; SquadCmdLogic.Log("[SquadCmd] 标记清除：超时"); return; }
		try
		{
			if (mark.Soldier != null && !mark.Soldier.IsAlive) { mark = null; SquadCmdLogic.Log("[SquadCmd] 标记清除：目标死亡"); return; }
			if (mark.Vehicle != null && mark.Vehicle.transform == null) { mark = null; SquadCmdLogic.Log("[SquadCmd] 标记清除：载具失效"); return; }
			try
			{
				if (mark.Soldier != null && mark.Soldier.transform != null) mark.Position = mark.Soldier.transform.position;
				else if (mark.Vehicle != null && mark.Vehicle.transform != null) mark.Position = mark.Vehicle.transform.position;
			}
			catch { }
			markInteropFailures = 0;
			List<Soldier> uu = GetCommandUnits();
			if (uu.Count == 0) { MarkLosCached = true; return; } // 无选中：只保活不判距
			Vector3 tp = mark.Position;
			float best = float.MaxValue;
			Soldier observer = null;
			foreach (Soldier u in uu)
			{
				if (u == null || u.transform == null) continue;
				float d = (u.transform.position - tp).sqrMagnitude;
				if (d < best) { best = d; observer = u; }
			}
			if (observer == null) return;
			bool inRange = best <= MarkRadius * MarkRadius;
			bool los = HasLineOfSightToMark(observer, tp);
			MarkLosCached = inRange && los;
			if (!inRange)
			{
				if (!mark.Downgraded) { mark.Downgraded = true; SquadCmdLogic.Log("[SquadCmd] 标记降级：超出范围"); }
				markLostSightSince = -1f;
				return;
			}
			if (!los)
			{
				if (markLostSightSince < 0f) markLostSightSince = Time.unscaledTime;
				else if (Time.unscaledTime - markLostSightSince > MarkLoseSightGraceSeconds)
				{
					mark = null; markLostSightSince = -1f;
					SquadCmdLogic.Log("[SquadCmd] 标记清除：失去视线超时");
				}
			}
			else
			{
				markLostSightSince = -1f;
				if (mark.Downgraded) { mark.Downgraded = false; SquadCmdLogic.Log("[SquadCmd] 标记恢复：回到范围+视线"); }
			}
		}
		catch
		{
			if (++markInteropFailures >= MarkInteropFailureLimit)
			{
				mark = null; markInteropFailures = 0;
				SquadCmdLogic.Log("[SquadCmd] 标记清除：目标对象失效");
			}
		}
	}

	/// <summary>视线检查：viewer 眼睛到标记点无遮挡（覆盖 GetBestVisibleEnemy 防穿墙锁）。</summary>
	internal static bool HasLineOfSightToMark(Soldier viewer, Vector3 targetPos)
	{
		try
		{
			if (viewer == null || viewer.transform == null) return false;
			Vector3 from = viewer.transform.position + Vector3.up * 1.5f;
			Vector3 to = targetPos + Vector3.up * 1.2f;
			Vector3 dir = to - from;
			float d = dir.magnitude;
			if (d < 0.1f) return true;
			if (Physics.Raycast(from, dir / d, out RaycastHit hit, d))
			{
				Creature c = hit.collider.GetComponentInParent<Creature>();
				if (c != null && mark != null && mark.Soldier != null && c == mark.Soldier) return true;
				Vehicle v = hit.collider.transform.GetComponentInParent<Vehicle>();
				if (v != null && mark != null && mark.Vehicle != null && v == mark.Vehicle) return true;
				return false;
			}
			return true;
		}
		catch { return true; }
	}

	// 接管/退出后的原生 UI 抑制
	private const float SwitchMemberSuppressSeconds = 8f;
	private static float suppressSwitchMemberUntil = -10f;
	internal static float SuppressSwitchMemberUntil => suppressSwitchMemberUntil;

	// ===== 0.7.68 重建段：0.7.68 误删段恢复（选择/命令/标记方法与字段，源=各历史版本最终形态）=====
	private static Vector2 lastRightBlankClickPos;
	private static Vector3? lastMovePoint; // 0.7.77：最近一次移动目标（双击第二击复用，避免重新 raycast 使标记乱飞）
	private static bool isDragging;
	private static Vector2 pressStart;
	// 手感
	private const float BaseSpeed = 32f;
	private const float BoostMult = 3f;
	private const float RotSpeed = 0.22f;
	private const float MinPitch = 3f, MaxPitch = 88f, MinHeight = 1.2f, MaxHeight = 400f, HeightStep = 8f;
	private const float DoubleClickTime = 0.3f;
	private const float DragThreshold = 10f;
	private const float FlyDuration = 1.2f;
	private static float panelHideTimer = -10f;
	// 定期清理
	private static float cleanupTimer = -10f;
	// 移动 pending（同步窗口重试）
	private static Vehicle pendVeh;
	private static Vector3 pendPoint;
	private static float pendUntil;
	private static float pendNextRetry;
	// M5 原队映射
	private static readonly Dictionary<long, Squad> originSquadMap = new Dictionary<long, Squad>();

	private static bool HasSelection => SelInfantryCount() > 0 || selVehicles.Count > 0;

	private static int SelInfantryCount()
	{
		if (mainSquad != null) { try { return mainSquad.CountMembers; } catch { return 0; } }
		return virtualUnits.Count;
	}

	private static int SelTotal => SelInfantryCount() + selVehicles.Count;

	private static void ClearSelection()
	{
		// 0.7.83 任务生命周期定案：指令下达后持续执行直到 完成/单位死亡/新指令覆盖。
		// 清空/更换选择【不】影响进行中的任务（M7 修正与登车流程照常）；显式停止只用【停止】按钮。
		mainSquad = null;
		selVehicles.Clear();
		selVehicleRefs.Clear();
		virtualUnits.Clear();
		hasCmdTarget = false;
	}

	/// <summary>当前选中的步兵（身上 ◆ 光标；只移动它们 = 未选不动）。</summary>
	internal static List<Soldier> GetSelectedInfantry()
	{
		List<Soldier> list = new List<Soldier>();
		if (mainSquad != null)
		{
			try
			{
				int n = mainSquad.CountMembers;
				for (int i = 0; i < n; i++)
				{
					Soldier m = mainSquad.GetMemberClamped(i);
					if (m != null && m.IsAlive && m.transform != null) list.Add(m);
				}
			}
			catch { }
		}
		else
		{
			for (int i = 0; i < virtualUnits.Count; i++)
			{
				Soldier s = virtualUnits[i];
				if (s != null && s.IsAlive && s.transform != null) list.Add(s);
			}
		}
		return list;
	}

	/// <summary>本次指令的全部指挥对象（步兵 + 选中载具的实际在车乘员）。
	/// ER2 登车不拆小队——必须按车辆子物体过滤，否则会连未上车的人一起选中。</summary>
	private static List<Soldier> GetCommandUnits()
	{
		List<Soldier> list = GetSelectedInfantry();
		if (selVehicleRefs.Count > 0)
		{
			foreach (Vehicle rv in selVehicleRefs)
			{
				if (rv == null || rv.transform == null) continue;
				try
				{
					Soldier[] occ = rv.GetComponentsInChildren<Soldier>();
					if (occ == null) continue;
					for (int i = 0; i < occ.Length; i++)
						if (occ[i] != null && occ[i].IsAlive) list.Add(occ[i]);
				}
				catch { }
			}
			return list;
		}
		foreach (Squad sq in selVehicles)
		{
			if (sq == null) continue;
			Vehicle v = VehicleOfCrew(sq);
			if (v != null && v.transform != null)
			{
				Dictionary<long, Vehicle> seen = new Dictionary<long, Vehicle>();
				try
				{
					int mc = sq.CountMembers;
					for (int i = 0; i < mc; i++)
					{
						Soldier mm = sq.GetMemberClamped(i);
						if (mm == null || mm.transform == null) continue;
						Vehicle vm = null;
						try { vm = mm.GetComponentInParent<Vehicle>(); } catch { }
						if (vm != null && vm.transform != null && !seen.ContainsKey((long)vm.Pointer)) seen[(long)vm.Pointer] = vm;
					}
				}
				catch { }
				if (seen.Count > 0)
				{
					try
					{
						foreach (Vehicle vv in seen.Values)
						{
							Soldier[] occ = vv.GetComponentsInChildren<Soldier>();
							if (occ == null) continue;
							for (int i = 0; i < occ.Length; i++)
								if (occ[i] != null && occ[i].IsAlive) list.Add(occ[i]);
						}
						continue;
					}
					catch { }
				}
			}
			try
			{
				int n = sq.CountMembers;
				for (int i = 0; i < n; i++)
				{
					Soldier m = sq.GetMemberClamped(i);
					if (m != null && m.IsAlive) list.Add(m);
				}
			}
			catch { }
		}
		return list;
	}

	internal static List<Squad> GetSelectedVehicleCrews()
	{
		return new List<Squad>(selVehicles);
	}

	private static bool IsInfantry(Soldier s)
	{
		try { if (s.GetComponentInParent<Vehicle>() != null) return false; } catch { }
		try { if (new Lua_Soldier(s).isInsideVehicle()) return false; } catch { }
		return true;
	}

	private static Squad CrewOf(Soldier s)
	{
		if (s == null) return null;
		Squad sq = null;
		try { sq = s.joinedSquad; } catch { }
		return sq;
	}

	/// <summary>点击选中（替换）：步兵=虚拟单选，载具=整车组。</summary>
	private static void SelectSingle(Soldier s)
	{
		ClearSelection();
		if (s == null) return;
		if (IsInfantry(s)) { virtualUnits.Add(s); }
		else { Squad crew = CrewOf(s); if (crew != null) selVehicles.Add(crew); }
		selFlash = Time.unscaledTime + 3f;
	}

	/// <summary>点击选中载具（整车组）。</summary>
	private static void SelectVehicleCrew(Squad crewSq)
	{
		ClearSelection();
		if (crewSq != null) selVehicles.Add(crewSq);
		selFlash = Time.unscaledTime + 3f;
	}

	/// <summary>追加：把步兵加入当前选择。</summary>
	private static void AddInfantry(Soldier s)
	{
		if (s == null) return;
		if (mainSquad != null) { AddInfantryToSquadTo(s, mainSquad); }
		else if (!virtualUnits.Contains(s)) virtualUnits.Add(s);
		selFlash = Time.unscaledTime + 3f;
	}

	private static void AddVehicle(Squad crewSq)
	{
		if (crewSq != null && !selVehicles.Contains(crewSq)) selVehicles.Add(crewSq);
		selFlash = Time.unscaledTime + 3f;
	}

	/// <summary>清理死亡/空选择。</summary>
	private static void PruneSelection()
	{
		if (mainSquad != null) { try { mainSquad.RemoveNullUnits(); } catch { } }
		for (int i = virtualUnits.Count - 1; i >= 0; i--)
			if (virtualUnits[i] == null || !virtualUnits[i].IsAlive) virtualUnits.RemoveAt(i);
		for (int i = selVehicles.Count - 1; i >= 0; i--)
		{
			Squad sq = selVehicles[i];
			if (sq == null) { selVehicles.RemoveAt(i); continue; }
			bool dead = true;
			try { int n = sq.CountMembers; for (int j = 0; j < n; j++) { Soldier m = sq.GetMemberClamped(j); if (m != null && m.IsAlive) { dead = false; break; } } } catch { dead = true; }
			if (dead) selVehicles.RemoveAt(i);
		}
		if (mainSquad == null && virtualUnits.Count == 0 && selVehicles.Count == 0) hasCmdTarget = false;
	}

	/// <summary>创建并注册一个新的真实小队。</summary>
	private static Squad CreateNewSquad()
	{
		try
		{
			string gid = Guid.NewGuid().ToString();
			Squad sq = Squad.FindOrNew(gid);
			if (sq == null) sq = new Squad(gid, true);
			try { sq.SetFullySpawned(); } catch { }
			try { sq.isOpenForJoiners = true; } catch { }
			SquadCmdLogic.Log("[SquadCmd] 新建小队 ptr=0x" + ((long)sq.Pointer).ToString("X"));
			return sq;
		}
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] 新建小队失败: " + ex.Message); return null; }
	}

	/// <summary>定期清理：移除小队内的死/空成员（0.7.34 收窄：不再全局 ClearEmptySquads）。</summary>
	private static void PeriodicCleanup()
	{
		if (Time.unscaledTime < cleanupTimer) return;
		cleanupTimer = Time.unscaledTime + 5f;
		try { if (mainSquad != null) mainSquad.RemoveNullUnits(); } catch { }
	}

	/// <summary>移动：只对选中步兵逐单位 moveTo（AI 命令通道）。</summary>
	internal static int MoveUnits(List<Soldier> units, Vector3 point)
	{
		int n = 0;
		if (units == null) return 0;
		foreach (Soldier s in units)
		{
			if (s == null || !s.IsAlive) continue;
			try
			{
				SquadCmdLogic.RegisterControlledUnit(s);
				AiParams ap = new Lua_Soldier(s).getAiParams();
				try { ap.followCustomSquadOrders(); } catch { }
				try { ap.followCustomDirectCommands(); } catch { }
				try { ap.allowMovements(true); } catch { }
				new Lua_Soldier(s).moveTo(point);
				n++;
			}
			catch { }
		}
		return n;
	}

	/// <summary>让载具/火力点车组开到目标点（Squad 原生订单链）。</summary>
	private static int DriveVehicleTo(Vehicle vehRef, Vector3 point)
	{
		RTSTrace("DriveEntry", "vehicle=" + (vehRef != null ? vehRef.name : "null"));
		if (vehRef == null || vehRef.transform == null) { RTSTrace("DriveDecision", "canDrive=false reason=nullRef"); return 0; }
		try
		{
			AIVehicle ai = null;
			try { ai = vehRef.GetComponent<AIVehicle>(); } catch { }
			if (ai == null) { try { ai = vehRef.GetComponentInChildren<AIVehicle>(); } catch { } }
			if (ai == null)
			{
				SquadCmdLogic.Log("[VehicleMove] vehicle=" + vehRef.name + " 无 AIVehicle 组件");
				return 0;
			}
			if (rtsSquadSet.Count == 0)
			{
				RTSTrace("DriveDecision", "vehicle=" + vehRef.name + " canDrive=false reason=noRtsSquad(未分队)");
				SquadCmdLogic.Log("[VehicleMove] vehicle=" + vehRef.name + " 未分队：请先点【分队】再驾驶");
				cmdFlash = "车辆未分队：先点【分队】再移动"; cmdFlashUntil = Time.unscaledTime + 2.5f;
				return 0;
			}
			Squad tgt = null;
			try { tgt = ai.squadInside; } catch { }
			if (tgt == null || !rtsSquadSet.Contains((long)tgt.Pointer))
			{
				RTSTrace("DriveDecision", "vehicle=" + vehRef.name + " canDrive=false reason=syncWindow squadInside=" + (tgt != null ? "0x" + ((long)tgt.Pointer).ToString("X") : "null"));
				SquadCmdLogic.Log("[VehicleMove] vehicle=" + vehRef.name + " 同步未就绪（等待原生同步，稍后自动重试）");
				pendVeh = vehRef; pendPoint = point; pendUntil = Time.unscaledTime + 3f; pendNextRetry = Time.unscaledTime + 0.25f;
				return 0;
			}
			RTSTrace("DriveDecision", "vehicle=" + vehRef.name + " canDrive=true squadInside=rtsSquad");
			SquadCmdLogic.StopTracking();
			new Lua_Squad(tgt).moveTo(point, Plugin.radius.Value);
			SquadCmdLogic.Log("[VehicleMove] vehicle=" + vehRef.name + " squadInside=0x" + ((long)tgt.Pointer).ToString("X")
				+ " target=" + point.ToString("0.0") + " via=Lua_Squad");
			return 1;
		}
		catch (Exception ex) { SquadCmdLogic.Log("[VehicleMove] 载具移动失败: " + ex.Message); return 0; }
	}

	internal static void ClearMark() { mark = null; }

	private static void MarkEnemySoldier(Soldier target)
	{
		if (target == null) return;
		Spottable spot = GetSpottable(target);
		if (spot == null)
		{
			cmdFlash = "标记失败：目标无 Spottable"; cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}
		string fac = SafeFaction(target.faction);
		markInteropFailures = 0; markLostSightSince = -1f;
		mark = new MarkedTarget
		{
			Spottable = spot,
			Soldier = target,
			Position = target.transform.position,
			Faction = fac,
			Name = SafeName(target),
			Until = float.MaxValue
		};
		RecordCmdTarget(target.transform.position);
		BeginMarkAdvance();
		cmdFlash = "叛徒标记 → " + mark.Name + "（持续到死亡/失控）"; cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.Log("[SquadCmd] 叛徒标记(持久) " + mark.Name + " 阵营=" + fac);
	}

	private static void MarkEnemyVehicle(Vehicle veh)
	{
		if (veh == null) return;
		Spottable spot = null;
		try { spot = Creature.GetConnectedSpottable(veh.transform); } catch { }
		if (spot == null)
		{
			Soldier crew = FirstCrew(veh);
			if (crew != null && crew.IsAlive) { MarkEnemySoldier(crew); return; }
			cmdFlash = "标记失败：载具无目标"; cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}
		string fac = "";
		try { fac = veh.GetVehicleFaction() ?? ""; } catch { }
		markInteropFailures = 0; markLostSightSince = -1f;
		mark = new MarkedTarget
		{
			Spottable = spot,
			Vehicle = veh,
			Position = veh.transform.position,
			Faction = fac,
			Name = "载具 " + SafeName(veh),
			Until = float.MaxValue
		};
		RecordCmdTarget(veh.transform.position);
		BeginMarkAdvance();
		cmdFlash = "叛徒标记 → " + mark.Name + "（持续到死亡/失控）"; cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.Log("[SquadCmd] 叛徒标记(持久) 载具 " + veh.name);
	}

	/// <summary>标记任意 Spottable（中立物品/设施）：标谁打谁。</summary>
	private static void MarkSpottable(Spottable spot, string objName, string fac, Vector3 pos)
	{
		if (spot == null) return;
		markInteropFailures = 0; markLostSightSince = -1f;
		mark = new MarkedTarget
		{
			Spottable = spot,
			Position = pos,
			Faction = fac,
			Name = string.IsNullOrEmpty(objName) ? "目标物" : objName,
			Until = float.MaxValue
		};
		RecordCmdTarget(pos);
		BeginMarkAdvance();
		cmdFlash = "标记目标物 → " + mark.Name + "（持续到失效）"; cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.Log("[SquadCmd] 标记目标物 " + mark.Name);
	}

	private static string SafeName(Soldier s)
	{
		try { string n = s.name_surname; return string.IsNullOrEmpty(n) ? ("单位#" + s.GetInstanceID()) : n; } catch { return "单位"; }
	}

	private static string SafeName(Vehicle v)
	{
		try { return string.IsNullOrEmpty(v.name) ? "未知载具" : v.name; } catch { return "未知载具"; }
	}

	private static string SafeFaction(string fac)
	{
		try { return string.IsNullOrEmpty(fac) ? "" : fac; } catch { return ""; }
	}

	private static void ClearFollow(string reason, bool logIt) { }

	/// <summary>M5：框选建队时记录"单位→原小队"，退出 RTS 时还原。</summary>
	private static void RestoreOriginalSquads()
	{
		if (originSquadMap.Count == 0) return;
		int restored = 0;
		foreach (var kv in originSquadMap)
		{
			Soldier s = null;
			try
			{
				Il2CppSystem.Collections.Generic.List<Creature> all = Creature.allCreatures;
				if (all == null) continue;
				for (int i = 0; i < all.Count; i++)
				{
					Creature c = all[i]; if (c == null) continue;
					Soldier cs = c.TryCast<Soldier>();
					if (cs == null || cs.Pointer != (IntPtr)kv.Key) continue;
					s = cs; break;
				}
			}
			catch { continue; }
			Squad orig = kv.Value;
			try
			{
				if (s == null || !s.IsAlive || orig == null) continue;
				Squad cur = s.joinedSquad;
				if (cur != null && orig != null && cur.Pointer == orig.Pointer) continue;
				if (cur != null) cur.Leave(s, false);
				orig.Join(s);
				restored++;
			}
			catch { }
		}
		originSquadMap.Clear();
		mainSquad = null;
		virtualUnits.Clear();
		SquadCmdLogic.Log("[SquadCmd] M5 退队还原 成员=" + restored);
	}

	private static Spottable GetSpottable(Soldier s)
	{
		try { if (s == null || s.transform == null) return null; return Creature.GetConnectedSpottable(s.transform); } catch { return null; }
	}

	// 登车 pending 字段
	private static Vehicle pendingBoardVeh;
	private static Squad pendingBoardSq;
	private static List<Soldier> pendingBoardUnits;
	private static float pendingBoardUntil;
	private static float pendingBoardNext;
	private static HashSet<long> pendingBoardIssued = new HashSet<long>();
	private static readonly Dictionary<long, float> issuedAt = new Dictionary<long, float>();
	private static readonly Dictionary<long, float> guideAt = new Dictionary<long, float>();
	private static Vector3? lastGuideVehPos;

	/// <summary>标记后：选中单位向目标推进（借 M7 修正循环），到达交战距离即停由原生接战。</summary>
	private static void BeginMarkAdvance()
	{
		mvTarget = mark != null ? mark.Position : Vector3.zero;
		mvUnits.Clear();
		foreach (Soldier s in GetSelectedInfantry()) if (s != null && s.IsAlive) mvUnits.Add(s);
		mvLastDist.Clear(); mvActive = mvUnits.Count > 0; mvFromMark = mvActive; mvLastCheck = Time.unscaledTime;
		mvArriveDist = Plugin.radius.Value; // 标记推进精确到位
	}

	internal static bool SuppressingUi
	{
		get
		{
			if (Active) return true;
			if (Time.unscaledTime >= suppressSwitchMemberUntil) return false;
			// 0.7.33：接管后抑制窗内玩家死亡 → 立即放行，RespawnPanel/重生流程必须能走
			try
			{
				PlayerController pc = PlayerController.currentController;
				Soldier c = pc != null ? pc.ControlledCharacter : null;
				if (c == null || !c.IsAlive) return false;
			}
			catch { }
			return true;
		}
	}

	private static void RTSTrace(string stage, string detail)
	{
		try
		{
			string rs = rtsSquad != null ? "0x" + ((long)rtsSquad.Pointer).ToString("X") : "null";
			string rv = selVehicleRefs.Count.ToString();
			string sv = selVehicles.Count.ToString();
			SquadCmdLogic.Log("[RTSSquadTrace] stage=" + stage + " rtsSquad=" + rs + " selVehicleRefs=" + rv + " selVehicles=" + sv + (detail.Length > 0 ? " | " + detail : ""));
		}
		catch { }
	}

	// ===== 0.7.46：分队（把当前 RTS 选中 Soldier[] 拆进真正的新 Squad）=====
	private static void SplitSelected()
	{
		RTSTrace("SplitBefore", "selectedUnits=" + GetCommandUnits().Count);
		List<Soldier> units = GetCommandUnits();
		if (units.Count == 0) { cmdFlash = "先选中要分队的单位"; cmdFlashUntil = Time.unscaledTime + 2f; return; }
		Squad ns = CreateNewSquad();
		if (ns == null) { cmdFlash = "新建小队失败"; cmdFlashUntil = Time.unscaledTime + 2f; return; }
		int moved = 0;
		HashSet<IntPtr> oldSet = new HashSet<IntPtr>();
		foreach (Soldier u in units)
		{
			if (u == null || !u.IsAlive) continue;
			try { Squad old = u.joinedSquad; if (old != null) oldSet.Add(old.Pointer); } catch { }
			if (AddInfantryToSquadTo(u, ns)) moved++;
		}
		if (moved == 0) { cmdFlash = "分队失败：无可拆单位"; cmdFlashUntil = Time.unscaledTime + 2f; return; }
		rtsSquad = ns;
		rtsSquadSet.Add((long)ns.Pointer);
		RTSTrace("SplitAfter", "oldRtsSquad→newRtsSquad=0x" + ((long)ns.Pointer).ToString("X") + " selectedUnits=" + GetCommandUnits().Count);
		mainSquad = null; virtualUnits.Clear(); // 分队后控制组=NewSquad（选择显示仍按 selVehicleRefs 车辆乘员）
		cmdFlash = "分队 → " + moved + " 人入新队（原队 " + oldSet.Count + " 个，其余未动）";
		cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.Log("[Split] 分队 入队=" + moved + " 原队数=" + oldSet.Count + " newSquad=0x" + ((long)ns.Pointer).ToString("X"));
		SplitCheck(ns);
	}

	/// <summary>0.7.57：把当前选中 Soldier[] 并入当前激活 RTS 组（rtsSquad）。</summary>
	private static void MergeSelectedToRts()
	{
		// 0.7.61：合并目标不再用隐藏的 rtsSquad 指针（0.7.57 事故：已"取消选择"的队被 rtsSquad 拖入合并）。
		// 新语义：目标 = 选中单位中人数最多的原生小队——合并永远只影响被选中的单位。
		List<Soldier> units = GetSelectedInfantry();
		foreach (Soldier occ in SelectedVehicleOccupants())
			if (occ != null && occ.IsAlive && !units.Contains(occ)) units.Add(occ);
		if (units.Count == 0) { cmdFlash = "无选中单位"; cmdFlashUntil = Time.unscaledTime + 2f; return; }
		Dictionary<long, Squad> tally = new Dictionary<long, Squad>();
		Dictionary<long, int> cnt = new Dictionary<long, int>();
		foreach (Soldier u in units)
		{
			try
			{
				Squad q = u.joinedSquad;
				if (q == null) continue;
				long k = (long)q.Pointer;
				if (!tally.ContainsKey(k)) { tally[k] = q; cnt[k] = 0; }
				cnt[k]++;
			}
			catch { }
		}
		Squad target = null; int best = 0;
		foreach (var kv in cnt)
		{
			if (kv.Value > best) { best = kv.Value; target = tally[kv.Key]; }
		}
		if (target == null) { cmdFlash = "选中单位没有所属小队"; cmdFlashUntil = Time.unscaledTime + 2f; return; }
		int moved = 0, skip = 0;
		foreach (Soldier u in units)
		{
			if (u == null || !u.IsAlive) continue;
			try
			{
				Squad cur = u.joinedSquad;
				if (cur != null && (long)cur.Pointer == (long)target.Pointer) { skip++; continue; }
				if (AddInfantryToSquadTo(u, target)) moved++;
			}
			catch { }
		}
		if (rtsSquad != null && (long)target.Pointer == (long)rtsSquad.Pointer) { /* 目标=激活组，无需变更 */ }
		cmdFlash = "合并 → " + moved + " 人入目标队（" + best + " 人队）" + (skip > 0 ? "（已在队 " + skip + "）" : "");
		cmdFlashUntil = Time.unscaledTime + 2.5f;
		SquadCmdLogic.Log("[Merge] 收编 入队=" + moved + " 已在队=" + skip + " 目标=0x" + ((long)target.Pointer).ToString("X") + " 目标人数=" + best);
	}

	/// <summary>0.7.46：分队后立即读取载具 AIVehicle.squadInside，验证原生是否跟随新 Squad。</summary>
	private static void SplitCheck(Squad ns)
	{
		foreach (Vehicle rv in selVehicleRefs)
		{
			try
			{
				if (rv == null || rv.transform == null) continue;
				AIVehicle ai = rv.GetComponent<AIVehicle>();
				if (ai == null) ai = rv.GetComponentInChildren<AIVehicle>();
				string si = "null";
				bool same = false;
				string dj = "null";
				bool jEqNew = false;
				try
				{
					if (ai != null && ai.squadInside != null)
					{
						si = "0x" + ((long)ai.squadInside.Pointer).ToString("X");
						same = (long)ai.squadInside.Pointer == (long)ns.Pointer;
					}
					Soldier drv = FirstCrew(rv); // 0.7.47：乘员 joinedSquad 对照
					if (drv != null)
					{
						Squad jq = drv.joinedSquad;
						if (jq != null) { dj = "0x" + ((long)jq.Pointer).ToString("X"); jEqNew = (long)jq.Pointer == (long)ns.Pointer; }
					}
				}
				catch { }
				SquadCmdLogic.Log("[SplitCheck] vehicle=" + rv.name
					+ " driver=" + SafeName(FirstCrew(rv))
					+ " driverJoinedSquad=" + dj + " joinedEqualsNew=" + (jEqNew ? "true" : "false")
					+ " squadInside=" + si + " squadInsideEqualsNew=" + (same ? "true" : "false")
					+ " newSquad=0x" + ((long)ns.Pointer).ToString("X"));
			}
			catch (Exception ex) { SquadCmdLogic.Log("[SplitCheck] 异常 " + ex.Message); }
		}
		// 无载具选中时也留一条（纯步兵分队场景）
		if (selVehicleRefs.Count == 0) SquadCmdLogic.Log("[SplitCheck] 无选中载具（纯步兵分队）");
	}

	private static void BoardPendingTick()
	{
		if (pendingBoardVeh == null || pendingBoardSq == null || pendingBoardUnits == null) return;
		Vehicle veh = pendingBoardVeh;
		Squad sq = pendingBoardSq;
		try
		{
			if (veh.transform == null) { CancelBoardPending("载具销毁"); return; }
			if (Time.unscaledTime > pendingBoardUntil)
			{
				foreach (Soldier bs in pendingBoardUnits)
				{
					try
					{
						if (bs == null || !bs.IsAlive || pendingBoardIssued.Contains((long)bs.Pointer)) continue;
						new Lua_Soldier(bs).boardVehicle(new Lua_Vehicle(veh));
						pendingBoardIssued.Add((long)bs.Pointer);
					}
					catch { }
				}
				FinishBoardPending("超时强制登车");
				return;
			}
			if (Time.unscaledTime < pendingBoardNext) return;
			pendingBoardNext = Time.unscaledTime + 0.5f;
			// 0.7.69：引导点只在车辆位移>2m 时刷新（固定目标不重发=不卡顿）
			bool refresh = !lastGuideVehPos.HasValue
				|| (veh.transform.position - lastGuideVehPos.Value).sqrMagnitude > 4f;
			if (refresh) lastGuideVehPos = veh.transform.position;
			// 0.7.74：车内成员实时集合（完成判据=全员真实在车，而非"已发命令"）
			HashSet<long> inCar = new HashSet<long>();
			try
			{
				Soldier[] occ = veh.GetComponentsInChildren<Soldier>();
				if (occ != null) for (int i = 0; i < occ.Length; i++) if (occ[i] != null && occ[i].IsAlive) inCar.Add((long)occ[i].Pointer);
			}
			catch { }
			foreach (Soldier bs in pendingBoardUnits)
			{
				try
				{
					if (bs == null || !bs.IsAlive) continue;
					long k = (long)bs.Pointer;
					if (inCar.Contains(k)) continue; // 已真实在车
					// 0.7.82：0.7.80 的选择门已删——登车途中乘员必然"不在选择中"（换选/清选是常态），
					// 该门会停摆引导与重发，导致剩人卡路+25s 超时强制。玩家改令由 MoveCommandTo 摘除机制处理。
					float d = (bs.transform.position - veh.transform.position).magnitude;
					bool issued = pendingBoardIssued.Contains(k);
					if (d < 10f)
					{
						// 阶段2：近距离登车。已发但 8s 未进车（原生登车被打断/失败）→ 自动重发
						if (issued && issuedAt.TryGetValue(k, out float t0) && Time.unscaledTime - t0 < 8f) continue;
						issuedAt[k] = Time.unscaledTime;
						pendingBoardIssued.Add(k);
						new Lua_Soldier(bs).boardVehicle(new Lua_Vehicle(veh));
						SquadCmdLogic.Log("[BoardPending] boardVehicle " + (issued ? "重发 " : "") + SafeName(bs));
					}
					else
					{
						// 0.7.76：接近命令只在 车移动>2m 或 距上次引导>3s 时才发——
						// moveTo 是持久命令，0.5s 重发会不断打断原生移动（登车卡顿根因）
						guideAt.TryGetValue(k, out float g0);
						if (refresh || Time.unscaledTime - g0 > 3f)
						{
							guideAt[k] = Time.unscaledTime;
							Vector3 p = NearSideApproachPoint(bs, veh);
							new Lua_Soldier(bs).moveTo(p);
						}
					}
				}
				catch { } // 单兵异常跳过，不取消整批登车
			}
			// 完成判据：全部存活乘员真实在车
			bool allIn = true;
			foreach (Soldier bs in pendingBoardUnits)
			{
				try { if (bs != null && bs.IsAlive && !inCar.Contains((long)bs.Pointer)) { allIn = false; break; } } catch { }
			}
			if (allIn) { FinishBoardPending("全员在车"); return; }
		}
		catch { } // 0.7.69：单轮异常跳过本轮，不取消整批登车
	}

	private static void FinishBoardPending(string reason)
	{
		try
		{
			Vehicle veh = pendingBoardVeh;
			Squad sq = pendingBoardSq;
			// 0.7.85：转队已在发起时同帧完成——这里只补登记兜底 + 收尾
			rtsSquad = sq;
			rtsSquadSet.Add((long)sq.Pointer);
			if (veh != null)
			{
				selVehicleRefs.Clear();
				selVehicles.Add(sq);
				AddVehicleRef(veh);
			}
			SquadCmdLogic.Log("[BoardPending] 完成（" + reason + "） vehicle=" + (veh != null ? veh.name : "?"));
			cmdFlash = "登车完成 → 可直接驾驶"; cmdFlashUntil = Time.unscaledTime + 2.5f;
			selFlash = Time.unscaledTime + 3f;
		}
		catch { }
		pendingBoardVeh = null; pendingBoardSq = null; pendingBoardUnits = null;
		try { pendingBoardIssued.Clear(); } catch { }
	}

	/// <summary>0.7.72：登车接近点=士兵当前方位一侧距车 4m 处（就近接近；太近会落在车体碰撞内引发寻路抖动）。</summary>
	private static Vector3 NearSideApproachPoint(Soldier s, Vehicle veh)
	{
		Vector3 vp = veh.transform.position;
		Vector3 sp = s.transform.position;
		Vector3 dir = sp - vp; dir.y = 0f;
		if (dir.sqrMagnitude < 0.01f) dir = veh.transform.forward; // 士兵恰在车上：用车头方向
		dir.Normalize();
		return vp + dir * 4f;
	}

	private static void CancelBoardPending(string reason)
	{
		try
		{
			SquadCmdLogic.Log("[BoardPending] 取消：" + reason + " vehicle=" + (pendingBoardVeh != null ? pendingBoardVeh.name : "?"));
		}
		catch { }
		pendingBoardVeh = null; pendingBoardSq = null; pendingBoardUnits = null;
		try { pendingBoardIssued.Clear(); } catch { }
	}

	/// <summary>0.7.48：分队同步窗口的自动重试	/// <summary>0.7.48：分队同步窗口的自动重试（最多 3s；收敛后把同一目标补发给新 Squad 原生命令）。</summary>
	private static void VehiclePendingTick()
	{
		if (pendVeh == null) return;
		if (Time.unscaledTime > pendUntil) { pendVeh = null; return; }
		if (Time.unscaledTime < pendNextRetry) return;
		pendNextRetry = Time.unscaledTime + 0.25f;
		try
		{
			if (pendVeh.transform == null || rtsSquad == null) { pendVeh = null; return; }
			AIVehicle ai = pendVeh.GetComponent<AIVehicle>();
			if (ai == null) ai = pendVeh.GetComponentInChildren<AIVehicle>();
			if (ai == null) { pendVeh = null; return; }
			Squad tgt = null;
			try { tgt = ai.squadInside; } catch { }
			if (tgt == null || !rtsSquadSet.Contains((long)tgt.Pointer)) return; // 仍未收敛（0.7.56 集合语义）
			SquadCmdLogic.StopTracking();
			new Lua_Squad(tgt).moveTo(pendPoint, Plugin.radius.Value);
			SquadCmdLogic.Log("[VehicleMove] vehicle=" + pendVeh.name + " squadInside=0x" + ((long)tgt.Pointer).ToString("X")
				+ " target=" + pendPoint.ToString("0.0") + " via=Lua_Squad（同步窗口重试）");
			pendVeh = null;
		}
		catch { pendVeh = null; }
	}

	/// <summary>我方阵营（上帝视角用缓存，平时读受控角色）。</summary>
	internal static string MySideFaction()
	{
		try
		{
			if (Active && !string.IsNullOrEmpty(SavedFaction)) return SavedFaction;
			PlayerController pc = PlayerController.currentController;
			if (pc != null && pc.ControlledCharacter != null && !string.IsNullOrEmpty(pc.ControlledCharacter.faction)) return pc.ControlledCharacter.faction;
		}
		catch { }
		return "";
	}

	/// <summary>该士兵是否在当前选择中（步兵选择 / 选中载具车组）。标记集火只引导选中单位。</summary>
	internal static bool IsSelectedUnit(Soldier s)
	{
		if (s == null) return false;
		try
		{
			if (mainSquad != null && mainSquad.GetMemberIndex(s) >= 0) return true;
		}
		catch { }
		for (int i = 0; i < virtualUnits.Count; i++)
			if (virtualUnits[i] != null && virtualUnits[i].Pointer == s.Pointer) return true;
		foreach (Squad sq in selVehicles)
		{
			try { if (sq.GetMemberIndex(s) >= 0) return true; } catch { }
		}
		return false;
	}

	/// <summary>该载具是否在当前选择中（载具车组成员之一在 selVehicles）。</summary>
	internal static bool IsSelectedVehicle(Vehicle v)
	{
		if (v == null) return false;
		try
		{
			long vPtr = (long)v.Pointer;
			foreach (Vehicle rv in selVehicleRefs) { try { if (rv != null && (long)rv.Pointer == vPtr) return true; } catch { } }
			int vId = v.GetInstanceID();
			foreach (Squad sq in selVehicles)
			{
				if (sq == null) continue;
				int n = sq.CountMembers;
				for (int i = 0; i < n; i++)
				{
					Soldier m = sq.GetMemberClamped(i);
					if (m == null || m.transform == null) continue;
					Vehicle mv = m.GetComponentInParent<Vehicle>();
					if (mv != null && mv.GetInstanceID() == vId) return true;
				}
			}
		}
		catch { }
		return false;
	}

	/// <summary>上车：优先小队级 boardVehicle，失败逐员兜底。返回成功数。</summary>
	/// <summary>
	/// 上车：逐员 boardVehicle，按空位数限流（解决"选择人数超过载具上限无法上车"）。
	/// 先解锁载具；小队级上车在自建小队上不稳定（用户实测进不去），改为逐员为主。
	/// </summary>
	private static int BoardVehicle(Vehicle veh)
	{
		if (veh == null) return 0;
		List<Soldier> units = GetSelectedInfantry();
		if (units.Count == 0)
		{
			cmdFlash = "先框选/选中要指挥的单位"; cmdFlashUntil = Time.unscaledTime + 2f;
			return 0;
		}
		Lua_Vehicle lv = new Lua_Vehicle(veh);
		// 解锁（防止 locked 导致上车静默失败）
		try { if (veh.IsLocked()) veh.SetLocked(false); } catch { }
		// 空位数
		int seats = 99;
		try { seats = lv.countEmptySeats(); } catch { }
		if (seats <= 0)
		{
			cmdFlash = "载具已满"; cmdFlashUntil = Time.unscaledTime + 2f;
			return 0;
		}
		// 0.7.70 关键修复：此处【不发】boardVehicle——0.7.67 重构遗漏了删除本调用，
		// 导致原生登车路线（分散→走登车点）与我们的环形接近引导双源竞争（先分散/乱走/到车旁不上车）。
		// boardVehicle 统一在 BoardPendingTick 阶段2（距车 8m 内）才发出。
		string extra = "";
		int n = 0;
		List<Soldier> overflow = new List<Soldier>(); // 0.7.72：超员落选者——原地停止待命，消除乱走
		foreach (Soldier s in units)
		{
			if (n >= seats) { overflow.Add(s); continue; }
			n++;
		}
		if (overflow.Count > 0)
		{
			// 0.7.73：落选者自动编入"待命分队"——脱离原队 S 的跟随/任务链（乱走根因），
			// 原地待命；玩家之后可正常选中他们（已在 rtsSquadSet，走原生链移动/合并）
			Squad waitSq = CreateNewSquad();
			if (waitSq != null)
			{
				int moved = 0;
				foreach (Soldier s in overflow)
				{
					try { if (AddInfantryToSquadTo(s, waitSq)) moved++; } catch { }
				}
				rtsSquadSet.Add((long)waitSq.Pointer);
				try { new Lua_Soldier(overflow[0]).stop(); } catch { }
				SquadCmdLogic.Log("[BoardPending] 待命分队建立 人数=" + moved + " 队=0x" + ((long)waitSq.Pointer).ToString("X") + "（脱离原队跟随链，原地待命）");
				extra = "，余 " + moved + " 人入待命组";
			}
			foreach (Soldier s in overflow)
			{
				try { new Lua_Soldier(s).stop(); } catch { }
			}
		}
		// 0.7.67：两段式登车——阶段1 纯步兵直线接近（不与原生登车移动源打架，杜绝"先分散"）；
		// 阶段2 距车 6m 内才发 boardVehicle（原生立即塞入）。转队/登记在完成后执行。
		if (n > 0)
		{
			List<Soldier> wait = new List<Soldier>();
			int taken2 = 0;
			foreach (Soldier bs in units)
			{
				if (taken2 >= n) break;
				try { if (bs != null && bs.IsAlive) { wait.Add(bs); taken2++; } } catch { }
			}
			pendingBoardVeh = veh;
			pendingBoardSq = CreateNewSquad();
			pendingBoardUnits = wait;
			pendingBoardUntil = Time.unscaledTime + 25f;
			pendingBoardNext = Time.unscaledTime; // 立即引导一轮
			// 0.7.85 核心修正：转队/登记与下令【同帧】完成（成员关系与位置无关）——
			// 乘员此刻已是新队成员、组已获驾驶资格。此前"登车完成后才转队"导致：
			// 剩人→资格永不生效→驾驶锁死；超员者留在原队→跟随链持续驱动乱走。
			{
				int moved = 0;
				foreach (Soldier bs in wait)
				{
					try { if (AddInfantryToSquadTo(bs, pendingBoardSq)) moved++; } catch { }
				}
				rtsSquad = pendingBoardSq;
				rtsSquadSet.Add((long)pendingBoardSq.Pointer);
				selVehicleRefs.Clear();
				selVehicles.Add(pendingBoardSq);
				AddVehicleRef(veh);
				SquadCmdLogic.Log("[BoardPending] 转队同帧完成 moved=" + moved + " rtsSquad=0x" + ((long)pendingBoardSq.Pointer).ToString("X"));
			}
			foreach (Soldier s in wait)
			{
				// 0.7.71：接近点=士兵当前方位一侧（就近接近）——不再环形分布，
				// 远距离不再"向四周扩散"，也不会有人绕到坦克对侧（乱走根因）
				Vector3 p = NearSideApproachPoint(s, veh);
				try
				{
					SquadCmdLogic.RegisterControlledUnit(s);
					AiParams ap = new Lua_Soldier(s).getAiParams();
					try { ap.followCustomSquadOrders(); } catch { }
					try { ap.followCustomDirectCommands(); } catch { }
					try { ap.allowMovements(true); } catch { }
					new Lua_Soldier(s).moveTo(p);
				}
				catch { }
			}
			extra = "（登车接近中…）";
			selFlash = Time.unscaledTime + 3f;
			SquadCmdLogic.Log("[BoardPending] two-stage queued vehicle=" + veh.name + " units=" + wait.Count);
		}
		RecordCmdTarget(veh.transform.position);
		// 保持选择连续性：上车的士兵会被原生移出原小队编入车组，步兵选择会凭空消失——
		// 这里把选择转换为该车组的载具选择（座位不够时只保留实际上车者）
		cmdFlash = "上车 → " + n + " 人（登车中…）" + (overflow.Count > 0 ? "，余 " + overflow.Count + " 人原地待命" : "") + extra; cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.Log("[SquadCmd] 上车发起 " + veh.name + " 人数=" + n + " 空位=" + seats + "（boardVehicle 延迟至阶段2）");
		RTSTrace("BoardVehicleExit", "vehicle=" + veh.name + " boarded=" + n);
		return n;
	}

	/// <summary>
	/// 下车：已选载具/火力点车组全部下车，并让车组步行离开载具附近
	/// （解决"下车后立刻又上车"：先停追踪，再给车组新的步行目标，AI 不会为了旧命令重新上车）。
	/// </summary>
	private static int DismountAllVehicles()
	{
		int n = 0;
		SquadCmdLogic.StopTracking(); // 先停追踪重发
		ClearFollow("下车", false);
		PruneVehicleRefs();
		foreach (Vehicle v in new List<Vehicle>(selVehicleRefs))
		{
			try
			{
				if (v == null || v.transform == null) continue;
				// 0.7.44：先快照该车乘员（下车后不可再反推），下车后逐员用单兵移动路径
				List<Soldier> occ = SelectedVehicleOccupants();
				Soldier[] inV = new Soldier[0];
				try { inV = v.GetComponentsInChildren<Soldier>(); } catch { }
				List<Soldier> dismounted = new List<Soldier>();
				if (inV != null)
					for (int i = 0; i < inV.Length; i++)
						if (inV[i] != null && inV[i].IsAlive) dismounted.Add(inV[i]);
				v.ForceExitAllVehicle();
				Vector3 exitPos = v.transform.position;
				Vector3 dir = v.transform.forward; dir.y = 0f; dir.Normalize();
				if (dir.sqrMagnitude < 0.01f) dir = new Vector3(1f, 0f, 0f);
				Vector3 target = exitPos - dir * 10f + Vector3.Cross(Vector3.up, dir) * 4f;
				if (dismounted.Count > 0)
				{
					List<Soldier> walk = new List<Soldier>(dismounted);
					MoveUnits(walk, target); // 单兵级：绝不经过共享 Squad
				}
				n++;
			}
			catch { }
		}
		cmdFlash = "已下车 " + n + " 辆载具"; cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.Log("[SquadCmd] 按钮下车: " + n);
		return n;
	}

	/// <summary>车组 Squad → Vehicle。</summary>
	private static Vehicle VehicleOfCrew(Squad crewSq)
	{
		if (crewSq == null) return null;
		try
		{
			int n = crewSq.CountMembers;
			for (int i = 0; i < n; i++)
			{
				Soldier m = crewSq.GetMemberClamped(i);
				if (m == null) continue;
				Vehicle v = null;
				try { v = m.GetComponentInParent<Vehicle>(); } catch { }
				if (v != null) return v;
			}
		}
		catch { }
		return null;
	}

	/// <summary>
	/// 从屏幕点解析可上车的友军目标（载具/火力点/火炮）。
	/// 命中敌军载具 → isEnemyVeh=true 返回 null；命中友军/中立 → 返回 Vehicle；未命中 → 返回 null。
	/// 修复"轮盘上车总是未命中友军载具"：①轮盘命令改用按下点（wheelPressPos）而非松开点；
	/// ②命中士兵/载具部件/无载具父级的火力点时，OverlapSphere 扫描附近友军载具兜底。
	/// </summary>
	private static Vehicle ResolveBoardTarget(Vector2 screenPos, out bool isEnemyVeh, out bool hitGround)
	{
		isEnemyVeh = false;
		hitGround = false;
		Camera cam = MainCam(); if (cam == null) return null;
		try
		{
			Ray ray = cam.ScreenPointToRay(screenPos);
			if (!Physics.Raycast(ray, out RaycastHit hit, 1500f)) return null;
			hitGround = true;

			// 直接命中载具/火炮（友军或中立都可上车；仅明确敌军不可进）
			Vehicle veh = hit.collider.transform.GetComponentInParent<Vehicle>();
			if (veh == null) veh = hit.collider.transform.GetComponent<Vehicle>();
			if (veh != null)
			{
				if (VehicleFriendly(veh) || !VehicleHostile(veh)) return veh;
				isEnemyVeh = true;
				return null;
			}
			// 命中士兵（可能在载具/火力点里）
			Soldier sol = hit.collider.transform.GetComponentInParent<Soldier>();
			if (sol == null) sol = hit.collider.transform.GetComponent<Soldier>();
			if (sol != null && sol.IsAlive)
			{
				Vehicle vIn = null;
				try { vIn = sol.GetComponentInParent<Vehicle>(); } catch { }
				if (vIn == null)
				{
					try { if (new Lua_Soldier(sol).isInsideVehicle()) vIn = NearbyFriendlyVehicle(hit.point); } catch { }
				}
				if (vIn != null)
				{
					if (VehicleFriendly(vIn)) return vIn;
					isEnemyVeh = true;
					return null;
				}
			}
			// 命中火力点（TurretGun 无载具父级）/载具部件/乘员旁 → 扫描附近友军载具兜底
			Vehicle near = NearbyFriendlyVehicle(hit.point);
			if (near != null) return near;
			return null;
		}
		catch { return null; }
	}

	/// <summary>在命中点附近 5m 内找最近的友军载具（兜底：命中载具部件/火力点/乘员）。</summary>
	private static Vehicle NearbyFriendlyVehicle(Vector3 center)
	{
		Vehicle best = null;
		float bestD = float.MaxValue;
		try
		{
			Collider[] cols = Physics.OverlapSphere(center, 5f);
			for (int i = 0; i < cols.Length; i++)
			{
				Collider col = cols[i];
				if (col == null || col.transform == null) continue;
				Vehicle v = col.transform.GetComponentInParent<Vehicle>();
				if (v == null) v = col.transform.GetComponent<Vehicle>();
				if (v == null || !VehicleFriendly(v)) continue;
				float d = (v.transform.position - center).sqrMagnitude;
				if (d < bestD) { bestD = d; best = v; }
			}
		}
		catch { }
		return best;
	}

	// ===== 进入/退出/接管 =====

	internal static bool CanEnter()
	{
		try
		{
			if (MatchData.data == null || !MatchData.data.HasMatchSettedUp) return false;
			PlayerController pc = PlayerController.currentController;
			return pc != null && pc.ControlledCharacter != null && pc.ControlledCharacter.IsAlive;
		}
		catch { return false; }
	}

	internal static void Toggle()
	{
		if (Active)
		{
			int friendlies = 0;
			try { friendlies = SquadCmdLogic.GetAllFriendlySquads().Count; } catch { }
			if (friendlies > 0)
			{
				cmdFlash = "不能按键退出：框选/选中单位 → 点顶部 [控制该小队] 接管"; cmdFlashUntil = Time.unscaledTime + 4f;
				return;
			}
			SquadCmdLogic.Log("[SquadCmd] 无存活友军小队，允许紧急退出。");
			Exit();
			return;
		}
		if (!CanEnter()) { SquadCmdLogic.Log("[SquadCmd] 上帝视角：不在战斗中或玩家不存在。"); return; }
		Enter();
	}

	internal static void Enter()
	{
		if (Active) return;
		try
		{
			PlayerController pc = PlayerController.currentController;
			savedSoldier = pc?.ControlledCharacter;
			SavedFaction = "";
			if (savedSoldier != null) try { SavedFaction = savedSoldier.faction ?? ""; } catch { }

			if (savedSoldier != null)
			{
				try { new Lua_Soldier(savedSoldier).getAiParams().enableAiBehaviour(true); } catch { }
				try { new Lua_Soldier(savedSoldier).getAiParams().followCustomSquadOrders(); } catch { }
				try { PlayerController.currentController?.SetPlayer(null, 0f); } catch { }
				SquadCmdLogic.Log("[SquadCmd] 上帝视角：玩家单位 AI 接管，已脱离控制。");
				HideSquadPanel();
			}
			SquadCmdLogic.ClearControlledSelection();
			ClearSelection();
			isDragging = false;
			flyingToSquad = false;

			Vector3 start = Vector3.zero;
			if (savedSoldier != null) start = savedSoldier.transform.position;
			else { Camera c = MainCam(); if (c != null) start = c.transform.position; }
			camPos = start + Vector3.up * 70f;
			yaw = 0f; pitch = 70f;
			RecomputeRot();
			Active = true;
			SetCursor(true);
			ApplyCam(MainCam());
			cmdFlash = "上帝视角 ON（框选组建小队，右键指挥，空格暂停）"; cmdFlashUntil = Time.unscaledTime + 4f;
			SquadCmdLogic.Log("[SquadCmd] 上帝视角 ON  pos=" + camPos.ToString("0.0") + " RTS 控制权仅绑定当前选择");
		}
		catch (Exception ex) { Active = false; SquadCmdLogic.Log("[SquadCmd] 上帝视角进入失败: " + ex.Message); }
	}

	internal static void Exit()
	{
		if (!Active) return;
		Active = false;
		EnsureTimeResumed();
		SquadCmdLogic.StopTracking();
		ClearFollow("退出上帝视角", false);
		ClearMark();
		RestoreAllPoses();
		rtsSquadSet.Clear(); // 0.7.56：战斗边界清空 RTS 分队登记
		RestoreOriginalSquads(); // M5：RTS 建队解散，成员回原小队（退出后恢复原生 AI 结构）
		SquadCmdLogic.ClearControlledSelection();
		isDragging = false;
		flyingToSquad = false;
		ClearSelection();
		SavedFaction = "";
		if (savedSoldier != null)
		{
			try { new Lua_Soldier(savedSoldier).getAiParams().enableAiBehaviour(false); } catch { }
			if (savedSoldier.IsAlive && PlayerController.currentController != null)
			{
				try { PlayerController.currentController.SetPlayer(savedSoldier, 0f); } catch { }
			}
			else
			{
				SquadCmdLogic.Log("[SquadCmd] 退出：玩家单位已死亡，跳过 SetPlayer（进入重生流程）。");
			}
		}
		savedSoldier = null;
		RestoreSquadPanel();
		SetCursor(false);
		SquadCmdLogic.Log("[SquadCmd] 上帝视角 OFF。");
	}

	// ===== 0.7.35：接管后小队保护窗（A）+ 死亡前置修复（B）=====
	private static float takeoverProtectUntil = -10f;
	private static float lastProtectCheck = -10f;
	private static bool lastCtrlAlive = true;
	private static bool deathGuardDone;
	private static bool deathRespawnScheduled;

	/// <summary>A：接管后 10s 保护窗，每 0.5s 校验受控单位 joinedSquad；断链立即重挂稳定友军队。</summary>
	internal static void EnsureTakeoverProtection()
	{
		if (Active || Time.unscaledTime >= takeoverProtectUntil) return;
		if (Time.unscaledTime - lastProtectCheck < 0.5f) return;
		lastProtectCheck = Time.unscaledTime;
		try
		{
			PlayerController pc = PlayerController.currentController;
			Soldier c = pc != null ? pc.ControlledCharacter : null;
			if (c == null || !c.IsAlive) return;
			Squad jsq = null; try { jsq = c.joinedSquad; } catch { }
			if (jsq != null && AliveOthers(jsq) >= 1) return;
			TransferControlledToStableSquad("保护窗");
		}
		catch { }
	}

	internal static Soldier LastKnownSoldier => lastKnownSoldier;
	private static Soldier lastKnownSoldier; // 0.7.37：最后一个有效受控单位缓存

	/// <summary>B(0.7.37 观测版)：以 lastKnownSoldier 缓存检测死亡跳变沿（不依赖 ControlledCharacter 尸体态）。</summary>
	internal static void DeathGuardCheck()
	{
		if (Active) return;
		try
		{
			PlayerController pc = PlayerController.currentController;
			Soldier c = pc != null ? pc.ControlledCharacter : null;
			if (c != null && c.IsAlive)
			{
				lastKnownSoldier = c;
				return;
			}
			// ControlledCharacter 为空或失活：检查缓存引用的死亡跳变沿
			Soldier lk = lastKnownSoldier;
			if (lk == null) return;
			bool dead = false;
			try { dead = !lk.IsAlive; } catch { dead = true; }
			if (!dead) return;
			if (deathGuardDone) return;
			deathGuardDone = true;
			string sq = "?"; int others = -1;
			try { Squad jsq = lk.joinedSquad; sq = jsq != null ? "0x" + ((long)jsq.Pointer).ToString("X") : "null"; if (jsq != null) others = AliveOthers(jsq); } catch { }
			int quota = -1; try { quota = BattleManager.instance.playerRespawns; } catch { }
			SquadCmdLogic.Log("[DeathGuard] detected death 单位=" + SafeName(lk) + " joinedSquad=" + sq + " AliveOthers=" + others + " 配额=" + quota + "（观测版：不接重生）");
		}
		catch { }
	}

	/// <summary>把受控（含尸体）单位重挂进最大稳定友军原生小队；同步 PlayerGUI 字段。日志 [PlayerSquadRestore]。</summary>
	private static void TransferControlledToStableSquad(string tag)
	{
		try
		{
			PlayerController pc = PlayerController.currentController;
			Soldier c = pc != null ? pc.ControlledCharacter : null;
			if (c == null) return;
			Squad cur = null; try { cur = c.joinedSquad; } catch { }
			Squad best = null; int bestN = 0;
			foreach (Squad fs in SquadCmdLogic.GetAllFriendlySquads())
			{
				if (fs == null) continue;
				if (cur != null && fs.Pointer == cur.Pointer) continue;
				int n = AliveCount(fs);
				if (n > bestN) { bestN = n; best = fs; }
			}
			if (best == null) { SquadCmdLogic.Log("[" + tag + "] 无可用友军小队（全灭？）交原生流程"); return; }
			if (cur != null)
			{
				originSquadMap[(long)c.Pointer] = cur;
				cur.Leave(c, false);
			}
			best.Join(c);
			Squad after = null; try { after = c.joinedSquad; } catch { }
			bool ok = after != null && after.Pointer == best.Pointer;
			try { PlayerGUI.squad = c.joinedSquad; PlayerGUI.GUISquad = c.joinedSquad; } catch { }
			SquadCmdLogic.Log("[PlayerSquadRestore] [" + tag + "] 重挂" + (ok ? "成功" : "失败") + " → " + bestN + " 人队 ptr=0x" + ((long)best.Pointer).ToString("X"));
		}
		catch (Exception ex)
		{
			SquadCmdLogic.Log("[PlayerSquadRestore] [" + tag + "] 异常: " + ex.Message);
		}
	}

	private static float reSquadGuard = -10f;
	/// <summary>0.7.34：玩家死亡且所在队空候选 → 立即把原生 UI 小队挂到最大存活友军队（尽快加载小队信息，给选择队友真实候选）。</summary>
	internal static void EnsurePlayerSquadHasCandidates()
	{
		if (Active || Time.unscaledTime < reSquadGuard) return;
		try
		{
			PlayerController pc = PlayerController.currentController;
			Soldier c = pc != null ? pc.ControlledCharacter : null;
			if (c == null || c.IsAlive) return;
			Squad cur = null; try { cur = c.joinedSquad; } catch { }
			if (cur != null && AliveOthers(cur) >= 1) return;
			Squad best = null; int bestN = 0;
			foreach (Squad fs in SquadCmdLogic.GetAllFriendlySquads())
			{
				if (fs == null) continue;
				int n = AliveCount(fs);
				if (n > bestN) { bestN = n; best = fs; }
			}
			if (best == null) return; // 全灭：交给原生增援流程
			PlayerGUI.squad = best; PlayerGUI.GUISquad = best;
			reSquadGuard = Time.unscaledTime + 2f;
			SquadCmdLogic.Log("[SquadCmd] 死亡空候选：UI 小队重指 " + bestN + " 人队（恢复选择队友候选）");
		}
		catch { }
	}

	/// <summary>0.7.30：屏蔽≠关闭——空候选队友选择必须走原生 Close，否则"选择队友"空面板卡死。</summary>
	internal static void EnsureSquadSelectionClosed()
	{
		try
		{
			if (PlayerGUI.IsSelectingSquad())
			{
				PlayerGUI.CloseSquadSelection();
				// PlayerGUI.ClearSquadList(); // 0.7.40 停用（嫌疑：清空标签池导致死亡后"选择队友"空列表）
				SquadCmdLogic.Log("[SquadCmd] 空候选队友选择已强制关闭（防卡死）");
			}
		}
		catch { }
	}

	/// <summary>除当前控制单位外，该小队还有几名存活成员（决定选人提示是否值得弹）。</summary>
	internal static int AliveOthers(Squad sq)
	{
		try
		{
			Soldier ctrl = null;
			try { ctrl = PlayerController.currentController != null ? PlayerController.currentController.ControlledCharacter : null; } catch { }
			int alive = 0;
			int n = sq.CountMembers;
			for (int i = 0; i < n; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m == null || !m.IsAlive) continue;
				alive++;
			}
			if (ctrl != null)
			{
				try { if (sq.GetMemberIndex(ctrl) >= 0) alive--; } catch { }
			}
			return alive;
		}
		catch { return 0; }
	}

	/// <summary>选中小队 → 随机接管一名存活成员并退出上帝视角（唯一正常退出方式）。</summary>
	internal static void TakeControlSelected()
	{
		if (!Active) return;
		try
		{
			PruneSelection();
			List<Soldier> pickPool = GetCommandUnits();
			if (pickPool.Count == 0)
			{
				cmdFlash = "请先框选/选中要接管的单位"; cmdFlashUntil = Time.unscaledTime + 2f;
				return;
			}
			Soldier pick = pickPool[UnityEngine.Random.Range(0, pickPool.Count)];
			Squad sq = null;
			try { sq = pick.joinedSquad; } catch { }
			// 0.7.30：若 pick 所在小队没有其他存活队友，转移进最大的有人友军小队，
			// 防止接管后立即死亡 → 原生"选择队友"空候选卡死
			try
			{
				if (sq != null && AliveOthers(sq) < 1)
				{
					Squad best = null; int bestN = 0;
					foreach (Squad fs in SquadCmdLogic.GetAllFriendlySquads())
					{
						if (fs == null || fs.Pointer == sq.Pointer) continue;
						int n = AliveCount(fs);
						if (n > bestN) { bestN = n; best = fs; }
					}
					if (best != null)
					{
						originSquadMap[(long)pick.Pointer] = sq;
						sq.Leave(pick, false);
						best.Join(pick);
						sq = pick.joinedSquad;
						SquadCmdLogic.Log("[SquadCmd] 接管前转移独苗进 " + bestN + " 人小队（防空候选卡死）");
					}
				}
			}
			catch { }
			int rest = pickPool.Count - 1;
			try { new Lua_Soldier(pick).getAiParams().enableAiBehaviour(false); } catch { }
			Active = false;
			EnsureTimeResumed();
			suppressSwitchMemberUntil = Time.unscaledTime + SwitchMemberSuppressSeconds;
			SquadCmdLogic.StopTracking();
			ClearFollow("接管单位", false);
			ClearMark();
			RestoreAllPoses(); // 接管的单位若被蹲/趴锁定，先还原否则玩家自己也无法站起
			SquadCmdLogic.ClearControlledSelection();
			isDragging = false; flyingToSquad = false;
			ClearSelection();
			SavedFaction = "";
			savedSoldier = null;
			RestoreSquadPanel();
			SetCursor(false);
			try { PlayerController.currentController?.SetPlayer(pick, 0f); }
			catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] SetPlayer 失败: " + ex.Message); }
			lastKnownSoldier = pick; // 0.7.37：观测死亡沿的缓存基准
			takeoverProtectUntil = Time.unscaledTime + 10f; // A：接管保护窗启动
			lastProtectCheck = -10f; lastCtrlAlive = true; deathGuardDone = false; deathRespawnScheduled = false;
			try
			{
				if (sq != null && pick.joinedSquad == null) { sq.Join(pick); SquadCmdLogic.Log("[SquadCmd] 接管单位丢失小队归属，已重新 Join。"); }
			}
			catch { }
			try { RespawnPanel.DisableRespawningView(true); } catch { }
			try { RespawnPanel.DisableRespawnPanel(0.2f); } catch { }
			try
			{
				if (PlayerGUI.IsSelectingSquad())
				{
					PlayerGUI.CloseSquadSelection();
					// PlayerGUI.ClearSquadList(); // 0.7.40 停用（嫌疑：清空标签池导致死亡后"选择队友"空列表）
					SquadCmdLogic.Log("[SquadCmd] 接管时检测到原生小队选择状态，已主动 CloseSquadSelection + ClearSquadList。");
				}
			}
			catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] 关闭原生小队选择失败: " + ex.Message); }
			try { PlayerGUI.squad = pick.joinedSquad; PlayerGUI.GUISquad = pick.joinedSquad; } catch { }
			SquadCmdLogic.Log("[SquadCmd] 接管选中单位（余 " + rest + " 人留 AI）。");
		}
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] 接管失败: " + ex.Message); }
	}

	// ===== 帧循环 =====

	internal static void Tick()
	{
		if (!Active || flyingToSquad) return;
		try
		{
			if (Time.unscaledTime > panelHideTimer)
			{
				panelHideTimer = Time.unscaledTime + 1f;
				HideSquadPanel();
			}
			PeriodicCleanup();
			PruneSelection();
			// 0.7.44：注册链不得带共享 Squad——Vehicle 选择只注册其乘员 Soldier
			{
				List<Soldier> occ = SelectedVehicleOccupants();
				List<Soldier> withOcc = GetSelectedInfantry();
				withOcc.AddRange(occ);
				SquadCmdLogic.SyncControlledSelection(withOcc, new List<Squad>());
			}
			PruneMark();
			MoveQualityTick();
			TickMove();
			BoardPendingTick();
			VehiclePendingTick();

			// 空格暂停
			if (Input.GetKeyDown(KeyCode.Space))
			{
				TogglePause();
			}
			// 用 unscaledDeltaTime：空格暂停（timeScale=0）时镜头仍可移动
			float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
			HandleMove(dt);
			HandleHeight(dt);
			HandleDrag();
			HandleClick();
			AssertFreeCursor();
			ApplyCam(MainCam());
		}
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] Tick 错误: " + ex.Message); }
	}

	internal static void LateApply() { if (!Active) return; try { AssertFreeCursor(); ApplyCam(MainCam()); } catch { } }

	internal static void PostTakeoverGuard()
	{
		if (Time.unscaledTime >= suppressSwitchMemberUntil) return;
		try
		{
			if (PlayerGUI.IsSelectingSquad())
			{
				PlayerGUI.CloseSquadSelection();
				// PlayerGUI.ClearSquadList(); // 0.7.40 停用（嫌疑：清空标签池导致死亡后"选择队友"空列表）
			}
			HideSquadPanel();
		}
		catch { }
	}

	/// <summary>上帝视角期间强制光标自由（Cursor patch 已兜底，这里双保险；右键按住跳过防闪烁）。</summary>
	private static void AssertFreeCursor()
	{
		try
		{
			if (Input.GetMouseButton(1)) return;
			if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
			if (!Cursor.visible) Cursor.visible = true;
		}
		catch { }
	}

	internal static void FrameEndGuard()
	{
		if (!Active) return;
		try
		{
			if (Input.GetMouseButton(1)) return;
			if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
			if (!Cursor.visible) Cursor.visible = true;
		}
		catch { }
	}

	// ===== 暂停（空格） =====

	private static float lastPauseToggle = -10f;
	internal static bool Paused => Time.timeScale < 0.01f;

	private static void TogglePause()
	{
		if (Time.unscaledTime < lastPauseToggle) return;
		lastPauseToggle = Time.unscaledTime + 0.4f;
		if (Paused)
		{
			Time.timeScale = 1f;
			cmdFlash = "已继续（时间恢复）"; cmdFlashUntil = Time.unscaledTime + 2f;
			SquadCmdLogic.Log("[SquadCmd] 上帝视角：时间恢复。");
		}
		else
		{
			Time.timeScale = 0f;
			cmdFlash = "已暂停（空格继续）"; cmdFlashUntil = Time.unscaledTime + 2f;
			SquadCmdLogic.Log("[SquadCmd] 上帝视角：已暂停。");
		}
	}

	private static void EnsureTimeResumed()
	{
		try { if (Time.timeScale < 0.01f) Time.timeScale = 1f; } catch { }
	}

	// ===== 原生"选择存活队友"面板隐藏/恢复 =====

	private static void HideSquadPanel()
	{
		try
		{
			PlayerGUI pg = PlayerGUI.instance;
			if (pg == null) return;
			if (pg.squadGUI != null) pg.squadGUI.gameObject.SetActive(false);
			if (pg.squadGUI_Panel != null) pg.squadGUI_Panel.gameObject.SetActive(false);
			if (pg.squadGUIMask != null) pg.squadGUIMask.gameObject.SetActive(false);
			if (pg.squadData_Panel != null) pg.squadData_Panel.SetActive(false);
		}
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] HideSquadPanel 失败: " + ex.Message); }
	}

	private static void RestoreSquadPanel()
	{
		try
		{
			PlayerGUI pg = PlayerGUI.instance;
			if (pg == null) return;
			if (pg.squadGUI != null) pg.squadGUI.gameObject.SetActive(true);
			if (pg.squadGUI_Panel != null) pg.squadGUI_Panel.gameObject.SetActive(true);
			if (pg.squadGUIMask != null) pg.squadGUIMask.gameObject.SetActive(true);
			if (pg.squadData_Panel != null) pg.squadData_Panel.SetActive(true);
		}
		catch { }
	}

	// ===== 控制 =====

	private static void HandleMove(float dt)
	{
		Camera cam = MainCam(); if (cam == null) return;
		Vector3 fwd = cam.transform.forward; fwd.y = 0f; fwd.Normalize();
		Vector3 right = cam.transform.right; right.y = 0f; right.Normalize();
		if (fwd.sqrMagnitude < 0.001f) fwd = new Vector3(1f, 0f, 0f);
		if (right.sqrMagnitude < 0.001f) right = new Vector3(0f, 0f, -1f);
		Vector3 dir = Vector3.zero;
		if (Input.GetKey(KeyCode.W)) dir += fwd;
		if (Input.GetKey(KeyCode.S)) dir -= fwd;
		if (Input.GetKey(KeyCode.D)) dir += right;
		if (Input.GetKey(KeyCode.A)) dir -= right;
		if (dir.sqrMagnitude > 0f)
		{
			dir.Normalize();
			float speed = BaseSpeed * (1f + camPos.y / 100f * 1.6f);
			if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift)) speed *= BoostMult;
			camPos += dir * speed * dt;
		}
	}

	private static void HandleHeight(float dt)
	{
		float wheel = Input.mouseScrollDelta.y;
		if (Mathf.Abs(wheel) > 0.001f) camPos.y += -wheel * HeightStep * (1f + camPos.y / 150f);
		if (Input.GetKey(KeyCode.E)) camPos.y += BaseSpeed * 0.7f * dt;
		if (Input.GetKey(KeyCode.Q)) camPos.y -= BaseSpeed * 0.7f * dt;
		float ground = GroundHeightAt(camPos);
		camPos.y = Mathf.Clamp(camPos.y, Mathf.Max(MinHeight, ground + 3f), MaxHeight);
	}

	private static float GroundHeightAt(Vector3 p)
	{
		try { Vector3 origin = new Vector3(p.x, p.y + 200f, p.z); if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 500f)) return hit.point.y; } catch { }
		return 0f;
	}

	private static void HandleDrag()
	{
		bool down = Input.GetMouseButton(2);
		if (down && !midDrag) { midDrag = true; lastDragX = Input.mousePosition.x; lastDragY = Input.mousePosition.y; }
		else if (!down && midDrag) { midDrag = false; }
		if (midDrag)
		{
			float dx = Input.mousePosition.x - lastDragX, dy = Input.mousePosition.y - lastDragY;
			lastDragX = Input.mousePosition.x; lastDragY = Input.mousePosition.y;
			yaw += dx * RotSpeed; pitch -= dy * RotSpeed; pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch); RecomputeRot();
		}
	}

	private static void HandleClick()
	{
		// M2：先做 HUD 遮挡判定——鼠标悬停在本 mod UI（顶部控制按钮/小队列表）上时，
		// 左右键都不作为战场指令处理（否则右键穿透 UI 会误下发移动/开环）
		if (IsMouseOverGui()) return;

		// 轮盘打开期间接管左右键：
		//  左键点按钮 = 执行（OnGUI 处理；此处不动选择，修复"点选项把选择清掉"）
		//  左键点空白 = 关闭轮盘（并重置框选起点，防止松开后拖出错误选框）
		//  右键       = 关闭（豁免逻辑在 DrawInteractionWheel 内；这里不下新指令）
		if (showInteractionWheel)
		{
			if (Input.GetMouseButtonDown(0))
			{
				Vector2 mg = MouseGui();
				if (!MouseOverWheelButton(mg))
				{
					CloseInteractionWheel("左键空白");
					isDragging = false;
					pressStart = mg;
				}
			}
			return;
		}

		// 吞掉关环手势的剩余部分：环关闭后同一次按住的左键在松开前不进入任何战场点击逻辑
		if (swallowLeftGesture)
		{
			if (!Input.GetMouseButton(0)) swallowLeftGesture = false; // 手势结束，恢复正常
			return;
		}

		// 右键手势状态机：按下只计时；≤RightLongPressSeconds 松开＝在按下坐标下达指令；
		// 有选中且按满时长＝打开常驻命令环（开环按压由豁免逻辑保护，松开不另发指令）
		if (Input.GetMouseButtonDown(1))
		{
			SquadCmdLogic.Log("[SquadCmd] RMB↓ gui=" + IsMouseOverGui() + " wheel=" + showInteractionWheel + " swallow=" + swallowLeftGesture + " sel=" + SelTotal);
			rightHoldActive = true;
			rightLongPressOpened = false;
			rightGestureWheelOpen = showInteractionWheel;
			rightDownTime = Time.unscaledTime;
			rightDownScreenPos = Input.mousePosition;
		}
		if (rightHoldActive && Input.GetMouseButton(1) && !showInteractionWheel && !rightGestureWheelOpen
			&& !rightLongPressOpened && !IsMouseOverGui()
			&& SelTotal > 0 && Time.unscaledTime - rightDownTime >= RightLongPressSeconds)
		{
			rightLongPressOpened = true;
			OpenCommandRing();
		}
		if (Input.GetMouseButtonUp(1) && rightHoldActive)
		{
			// 0.7.33 修复：0.7.27 删双击冲刺时误删了本调用——右键松开自此完全失灵
			if (!rightLongPressOpened && !rightGestureWheelOpen && !IsMouseOverGui())
			{
				float nowR = Time.unscaledTime;
				bool dbl = nowR - lastRightBlankClickTime < 0.6f
					&& Vector2.Distance(MouseGui(), lastRightBlankClickPos) < 40f; // 0.7.64：双击右键=快速模式
				lastRightBlankClickTime = nowR;
				lastRightBlankClickPos = MouseGui();
				if (dbl && lastMovePoint.HasValue)
				{
					// 0.7.77：双击第二击=对第一击的同一目标切快速模式（复用目标点，不重新 raycast——
					// 远距离时 40px 屏幕偏移对应地面几十米，重新取点会让目标标记乱飞）
					MoveCommandTo(lastMovePoint.Value, true);
					cmdFlash = "快速移动（同一目标）"; cmdFlashUntil = Time.unscaledTime + 1.5f;
				}
				else
				{
					IssueDirectCommand(rightDownScreenPos);
				}
			}
			rightHoldActive = false;
		}

		addingToSelection = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		// 左键按下：记录按下位置
		if (Input.GetMouseButtonDown(0))
		{
			pressStart = MouseGui();
			isDragging = false;
		}
		// 按住拖出框选
		if (Input.GetMouseButton(0) && !isDragging && (MouseGui() - pressStart).sqrMagnitude > DragThreshold * DragThreshold)
		{
			isDragging = true;
		}
		// 松开左键
		if (Input.GetMouseButtonUp(0))
		{
			if (isDragging) { FinishBoxSelect(); isDragging = false; return; }
			float now = Time.unscaledTime;
			float dt = now - lastClickTime;
			Vector2 pos = MouseGui();
			bool near = Vector2.Distance(pos, lastClickPos) < 30f;
			lastClickTime = now; lastClickPos = pos;
			if (dt < DoubleClickTime && near)
			{
				// 双击 → 选中该单位所在小队（若在 mainSquad 则全选该队）
				DoubleClickSelect();
			}
			else
			{
				ClickActOrCancel();
			}
			return;
		}
	}

	private static Vector2 MouseGui()
	{
		return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
	}

	private static bool IsMouseOverGui()
	{
		try
		{
			Vector2 m = MouseGui();
			if (HasSelection)
			{
				Rect btn = ControlButtonRect();
				if (btn.Contains(m)) return true;
				if (SplitButtonRect().Contains(m)) return true;
				if (MergeButtonRect().Contains(m)) return true;
			}
			if (squadPanelHit.height > 0f && squadPanelHit.Contains(m)) return true;
		}
		catch { }
		return false;
	}

	private static Rect ControlButtonRect()
	{
		return new Rect((Screen.width - 300f) * 0.5f, 12f, 300f, 36f);
	}

	/// <summary>0.7.46：【分队】按钮（控制按钮右侧）。</summary>
	private static Rect SplitButtonRect()
	{
		Rect b = ControlButtonRect();
		return new Rect(b.xMax + 8f, 12f, 120f, 36f);
	}

	/// <summary>0.7.58：【合并】按钮（控制按钮左侧）——把选中单位并入当前激活 RTS 组。</summary>
	private static Rect MergeButtonRect()
	{
		Rect b = ControlButtonRect();
		return new Rect(b.x - 8f - 120f, 12f, 120f, 36f);
	}

	/// <summary>左键单击：只负责选择/取消选择（友军=选中，其余=清空选择）。</summary>
	private static void ClickActOrCancel()
	{
		Camera cam = MainCam(); if (cam == null) return;
		try
		{
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hit, 1500f))
			{
				Vehicle veh = hit.collider.transform.GetComponentInParent<Vehicle>();
				if (veh == null) veh = hit.collider.transform.GetComponent<Vehicle>();
				if (veh != null && VehicleFriendly(veh))
				{
					// 友军载具：选中整车组（车组 Squad 从任一乘员取）
					Squad crew = CrewOf(FirstCrew(veh));
					if (addingToSelection) { AddVehicle(crew); AddVehicleRef(veh); }
					else { SelectVehicleCrew(crew); selVehicleRefs.Clear(); AddVehicleRef(veh); }
					return;
				}
				Soldier sol = hit.collider.transform.GetComponentInParent<Soldier>();
				if (sol == null) sol = hit.collider.transform.GetComponent<Soldier>();
				if (sol != null && sol.IsAlive && FriendlyUnit(sol))
				{
					// 友军步兵：选中（Shift=追加）
					if (addingToSelection) AddInfantry(sol);
					else SelectSingle(sol);
					return;
				}
			}
			// 敌军/中立/空白 → 取消选择（不影响进行中的任务；停止用【停止】按钮）
			ClearSelection();
			cmdFlash = "已清空选择"; cmdFlashUntil = Time.unscaledTime + 1.5f;
		}
		catch { }
	}

	private static Soldier FirstCrew(Vehicle veh)
	{
		try
		{
			Soldier[] crew = veh.GetComponentsInChildren<Soldier>();
			if (crew != null && crew.Length > 0) return crew[0];
		}
		catch { }
		return null;
	}

	/// <summary>双击：选中该单位所在小队全部存活步兵（虚拟或并入当前组）。</summary>
	private static void DoubleClickSelect()
	{
		Camera cam = MainCam(); if (cam == null) return;
		try
		{
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hit, 1500f))
			{
				Soldier sol = hit.collider.transform.GetComponentInParent<Soldier>();
				if (sol == null) sol = hit.collider.transform.GetComponent<Soldier>();
				if (sol == null || !sol.IsAlive || !FriendlyUnit(sol))
				{
					cmdFlash = "双击需命中友军单位"; cmdFlashUntil = Time.unscaledTime + 2f;
					return;
				}
				Squad sq = null;
				try { sq = sol.joinedSquad; } catch { }
				if (sq == null)
				{
					SelectSingle(sol);
				}
				else
				{
					// 双击 = 选中整队步兵（若目标在载具内则选中整车组）
					if (IsInfantry(sol))
					{
						ClearSelection();
						virtualUnits.Clear();
						try
						{
							int n = sq.CountMembers;
							for (int i = 0; i < n; i++)
							{
								Soldier m = sq.GetMemberClamped(i);
								if (m != null && m.IsAlive && IsInfantry(m)) virtualUnits.Add(m);
							}
						}
						catch { }
						selFlash = Time.unscaledTime + 3f;
						cmdFlash = "已选中整队 " + virtualUnits.Count + " 名步兵"; cmdFlashUntil = Time.unscaledTime + 2f;
					}
					else
					{
						SelectVehicleCrew(sq);
						cmdFlash = "已选中整车组"; cmdFlashUntil = Time.unscaledTime + 2f;
					}
				}
			}
		}
		catch { }
	}

	/// <summary>框选结束：框内友军步兵抽离入新小队（或追加），载具车组整体纳入选择。</summary>
	private static void FinishBoxSelect()
	{
		Camera cam = MainCam(); if (cam == null) return;
		try
		{
			Rect sel = GetScreenRect(pressStart, MouseGui());
			if (sel.width < 20f || sel.height < 20f) return;
			string fac = MyFac();
			List<Soldier> infantry = new List<Soldier>();
			Dictionary<IntPtr, Squad> vehSquads = new Dictionary<IntPtr, Squad>();
			Dictionary<long, Vehicle> vehRefMap = new Dictionary<long, Vehicle>();
			Il2CppSystem.Collections.Generic.List<Creature> list = Creature.allCreatures;
			if (list == null) return;
			for (int i = 0; i < list.Count; i++)
			{
				Creature c = list[i]; if (c == null) continue;
				Soldier s = c.TryCast<Soldier>(); if (s == null || !s.IsAlive) continue;
				if (!SquadCmdLogic.Friendly(s.faction ?? "", fac)) continue;
				Vector3 sp = cam.WorldToScreenPoint(s.transform.position);
				if (sp.z < 0f) continue;
				sp.y = Screen.height - sp.y;
				if (!sel.Contains(sp)) continue;
				if (IsInfantry(s))
				{
					infantry.Add(s);
				}
				else
				{
					Squad crew = CrewOf(s);
					if (crew != null) vehSquads[crew.Pointer] = crew;
					try { Vehicle vv = s.GetComponentInParent<Vehicle>(); if (vv != null && vv.transform != null) vehRefMap[(long)vv.Pointer] = vv; } catch { }
				}
			}

			if (!addingToSelection)
			{
				ClearSelection();
			}
			// 组建新小队：替换模式必建新队；追加且已有队则沿用
			if (infantry.Count > 0)
			{
				// v0.7.24：框选一律"临时指挥"（虚拟选择），不再拆散原生小队建新队——结构破坏交给显式“合并”
				foreach (Soldier s in infantry)
				{
					if (!virtualUnits.Contains(s)) virtualUnits.Add(s);
				}
			}
			// 载具车组整体纳入（不拆散、不改车组小队）+ 记录真实 Vehicle 引用
			foreach (Squad crew in vehSquads.Values)
			{
				if (!selVehicles.Contains(crew)) selVehicles.Add(crew);
			}
			foreach (var kv in vehRefMap) AddVehicleRef(kv.Value);

			int total = SelTotal;
			if (total < 1)
			{
				cmdFlash = "未框到可选单位（全空或载具无车组）"; cmdFlashUntil = Time.unscaledTime + 2f;
				return;
			}
			selFlash = Time.unscaledTime + 3f;
			cmdFlash = "临时指挥：步兵 " + SelInfantryCount() + " + 载具 " + selVehicles.Count + (addingToSelection ? "（追加）" : ""); cmdFlashUntil = Time.unscaledTime + 3f;
			SquadCmdLogic.Log("[SquadCmd] 框选(临时): 步兵=" + infantry.Count + " 载具=" + vehSquads.Count + " 追加=" + addingToSelection);
		}
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] 框选失败: " + ex.Message); }
	}

	// ===== 命令目标 =====

	private static void RecordCmdTarget(Vector3 p)
	{
		hasCmdTarget = true; cmdTarget = p; cmdTargetUntil = Time.unscaledTime + 12f;
		squadCenterDraw = SelCenter();
	}

	private static Vector3 SelCenter()
	{
		List<Soldier> units = GetSelectedInfantry();
		Vector3 sum = Vector3.zero; int n = 0;
		foreach (Soldier s in units) { if (s != null && s.transform != null) { sum += s.transform.position; n++; } }
		foreach (Squad sq in selVehicles)
		{
			try
			{
				int c = sq.CountMembers;
				for (int i = 0; i < c; i++)
				{
					Soldier m = sq.GetMemberClamped(i);
					if (m != null && m.transform != null) { sum += m.transform.position; n++; }
				}
			}
			catch { }
		}
		return n > 0 ? sum / n : Vector3.zero;
	}

	// ===== 相机 =====

	private static void RecomputeRot() { camRot = Quaternion.Euler(pitch, yaw, 0f); }
	private static void ApplyCam(Camera cam) { if (cam == null) return; cam.transform.position = camPos; cam.transform.rotation = camRot; }

	internal static Camera MainCam()
	{
		try { Camera c = ResourcesManager.mainCamera; if (c != null) return c; } catch { }
		try { Camera m = Camera.main; if (m != null) return m; } catch { }
		return null;
	}

	private static void SetCursor(bool god)
	{
		try { if (god) { Cursor.lockState = CursorLockMode.None; Cursor.visible = true; } else { Cursor.lockState = CursorLockMode.Locked; Cursor.visible = false; } } catch { }
	}

	private static string MyFac() => string.IsNullOrEmpty(SavedFaction) ? GetMyFactionStatic() : SavedFaction;
	private static bool FriendlyUnit(Soldier s) => SquadCmdLogic.Friendly(s.faction ?? "", MyFac());

	/// <summary>载具友军判定：GetVehicleFaction 为空（火炮等静态火力点常返回空）时回退用乘员阵营。</summary>
	private static bool VehicleFriendly(Vehicle v)
	{
		string vf = "";
		try { vf = v.GetVehicleFaction() ?? ""; } catch { }
		if (!string.IsNullOrEmpty(vf))
		{
			return SquadCmdLogic.Friendly(vf, MyFac());
		}
		// 兜底：用乘员/车组阵营判定
		try
		{
			Squad crew = CrewOf(FirstCrew(v));
			if (crew != null)
			{
				int n = crew.CountMembers;
				for (int i = 0; i < n; i++)
				{
					Soldier m = crew.GetMemberClamped(i);
					if (m != null && !string.IsNullOrEmpty(m.faction))
					{
						return SquadCmdLogic.Friendly(m.faction, MyFac());
					}
				}
			}
		}
		catch { }
		return false;
	}

	/// <summary>
	/// 载具是否明确为敌军（对立阵营）。中立/空阵营（GetVehicleFaction 为空、无乘员）不算敌军
	/// —— 中立载具/火力点应可上车，不应被当作攻击目标。
	/// </summary>
	private static bool VehicleHostile(Vehicle v)
	{
		string vf = "";
		try { vf = v.GetVehicleFaction() ?? ""; } catch { }
		string myFac = MyFac();
		if (string.IsNullOrEmpty(vf) || string.IsNullOrEmpty(myFac))
		{
			// 兜底：用乘员阵营判定
			try
			{
				Squad crew = CrewOf(FirstCrew(v));
				if (crew != null)
				{
					int n = crew.CountMembers;
					for (int i = 0; i < n; i++)
					{
						Soldier m = crew.GetMemberClamped(i);
						if (m != null && !string.IsNullOrEmpty(m.faction)) { vf = m.faction; break; }
					}
				}
			}
			catch { }
		}
		if (string.IsNullOrEmpty(vf) || string.IsNullOrEmpty(myFac)) return false;
		if (string.Equals(vf, myFac, StringComparison.Ordinal)) return false;
		bool mineA = myFac.EndsWith("_allies", StringComparison.Ordinal);
		bool mineX = myFac.EndsWith("_axis", StringComparison.Ordinal);
		bool hisA = vf.EndsWith("_allies", StringComparison.Ordinal);
		bool hisX = vf.EndsWith("_axis", StringComparison.Ordinal);
		if ((mineA && hisX) || (mineX && hisA)) return true;  // 明确对立
		return false;                                          // 同方或无法判定 → 非敌军
	}

	private static string GetMyFactionStatic()
	{
		try { PlayerController pc = PlayerController.currentController; if (pc != null) { Soldier s = pc.ControlledCharacter; if (s != null && !string.IsNullOrEmpty(s.faction)) return s.faction; } } catch { }
		return "";
	}

	private static Rect GetScreenRect(Vector2 a, Vector2 b)
	{
		float x = Mathf.Min(a.x, b.x), y = Mathf.Min(a.y, b.y);
		float w = Mathf.Abs(a.x - b.x), h = Mathf.Abs(a.y - b.y);
		return new Rect(x, y, w, h);
	}

	// ===== 镜头飞向小队（保留，未绑定 UI） =====

	private static void FlyTo(Vector3 center)
	{
		if (center == Vector3.zero) return;
		flyingToSquad = true;
		flyStartPos = camPos;
		flyTarget = center + Vector3.up * 30f;
		flyStartTime = Time.unscaledTime;
	}

	internal static void TickFly()
	{
		if (!flyingToSquad) return;
		try
		{
			float t = (Time.unscaledTime - flyStartTime) / FlyDuration;
			if (t >= 1f) { camPos = flyTarget; flyingToSquad = false; ApplyCam(MainCam()); return; }
			t = t * t * (3f - 2f * t);
			camPos = Vector3.Lerp(flyStartPos, flyTarget, t);
			yaw = 0f; pitch = 70f; RecomputeRot();
			ApplyCam(MainCam());
		}
		catch { flyingToSquad = false; }
	}

	// ===== HUD =====

	internal static void DrawHud()
	{
		if (!Active) return;
		try
		{
			GUIStyle st = SquadCmdLogic.HudStyle();
			if (st == null) return;
			Camera cam = MainCam();
			string info = cam != null ? "  高度 " + cam.transform.position.y.ToString("0") + "m" : "";

			// 底部指令提示
			string hint = "WASD移动 滚轮缩放 中键旋转 左键=选/框选(建队) 右键=指令 / 选中后长按右键=单位环(站蹲趴停止) 空格=暂停" + info;
			GUIStyle hs = SquadCmdLogic.HudStyleSmall();
			GUI.color = new Color(0.05f, 0.05f, 0.05f, 0.55f);
			GUI.DrawTexture(new Rect((Screen.width - 900f) * 0.5f, Screen.height - 30f, 900f, 22f), Texture2D.whiteTexture);
			GUI.color = Color.white;
			GUI.Label(new Rect((Screen.width - 900f) * 0.5f, Screen.height - 31f, 900f, 22f), hint, hs);

			// 左上角：暂停 + 选择信息
			PruneSelection();
			string status = "";
			if (Paused) status = "⏸ 已暂停（空格继续）";
			if (HasSelection)
			{
				string sel = "步兵 " + SelInfantryCount() + " + 载具 " + selVehicles.Count;
				status = status == "" ? ("已选 " + sel) : (status + "  |  已选 " + sel);
			}
			if (status != "")
			{
				GUI.color = new Color(0.05f, 0.05f, 0.05f, 0.55f);
				GUI.DrawTexture(new Rect(8f, 10f, 400f, 22f), Texture2D.whiteTexture);
				GUI.color = Paused ? new Color(1f, 0.85f, 0.2f, 0.95f) : selColor;
				GUI.Label(new Rect(14f, 9f, 400f, 22f), status, st);
				GUI.color = Color.white;
			}

			// 命令反馈
			if (cmdFlash != "" && Time.unscaledTime < cmdFlashUntil)
			{
				GUI.color = new Color(0.05f, 0.05f, 0.05f, 0.55f);
				GUI.DrawTexture(new Rect(8f, 38f, 700f, 22f), Texture2D.whiteTexture);
				GUI.color = selColor;
				GUI.Label(new Rect(14f, 37f, 700f, 22f), cmdFlash, st);
				GUI.color = Color.white;
			}

			// 顶部控制按钮
			if (HasSelection)
			{
				Rect btn = ControlButtonRect();
				bool hover = btn.Contains(Event.current.mousePosition);
				GUI.color = hover ? new Color(0.10f, 0.30f, 0.12f, 0.9f) : new Color(0.08f, 0.15f, 0.09f, 0.82f);
				GUI.DrawTexture(btn, Texture2D.whiteTexture);
				GUI.color = new Color(0.35f, 1f, 0.5f, 0.85f);
				GUI.DrawTexture(new Rect(btn.x, btn.y, btn.width, 1.5f), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(btn.x, btn.yMax - 1.5f, btn.width, 1.5f), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(btn.x, btn.y, 1.5f, btn.height), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(btn.xMax - 1.5f, btn.y, 1.5f, btn.height), Texture2D.whiteTexture);
				GUI.color = Color.white;
				GUI.Label(btn, "控制该小队（随机接管一名存活成员）", st);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
				{
					Event.current.Use();
					TakeControlSelected();
					return;
				}
				// 0.7.46：【分队】按钮——把当前选中 Soldier[] 拆进真正的新 Squad
				Rect sbtn = SplitButtonRect();
				bool shover = sbtn.Contains(Event.current.mousePosition);
				GUI.color = shover ? new Color(0.18f, 0.28f, 0.12f, 0.9f) : new Color(0.13f, 0.20f, 0.09f, 0.82f);
				GUI.DrawTexture(sbtn, Texture2D.whiteTexture);
				GUI.color = new Color(0.7f, 1f, 0.5f, 0.85f);
				GUI.DrawTexture(new Rect(sbtn.x, sbtn.y, sbtn.width, 1.5f), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sbtn.x, sbtn.yMax - 1.5f, sbtn.width, 1.5f), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sbtn.x, sbtn.y, 1.5f, sbtn.height), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sbtn.xMax - 1.5f, sbtn.y, 1.5f, sbtn.height), Texture2D.whiteTexture);
				GUI.color = Color.white;
				GUI.Label(sbtn, "分队", st);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && shover)
				{
					Event.current.Use();
					SplitSelected();
					return;
				}
				// 0.7.57：【合并】按钮——选中单位并入当前激活 RTS 组
				Rect mbtn = MergeButtonRect();
				bool mhover = mbtn.Contains(Event.current.mousePosition);
				GUI.color = mhover ? new Color(0.15f, 0.25f, 0.20f, 0.9f) : new Color(0.10f, 0.17f, 0.13f, 0.82f);
				GUI.DrawTexture(mbtn, Texture2D.whiteTexture);
				GUI.color = new Color(0.5f, 1f, 0.8f, 0.85f);
				GUI.DrawTexture(new Rect(mbtn.x, mbtn.y, mbtn.width, 1.5f), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(mbtn.x, mbtn.yMax - 1.5f, mbtn.width, 1.5f), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(mbtn.x, mbtn.y, 1.5f, mbtn.height), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(mbtn.xMax - 1.5f, mbtn.y, 1.5f, mbtn.height), Texture2D.whiteTexture);
				GUI.color = Color.white;
				GUI.Label(mbtn, "合并", st);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && mhover)
				{
					Event.current.Use();
					MergeSelectedToRts();
					return;
				}
			}

			// 目标标记
			if (hasCmdTarget && Time.unscaledTime < cmdTargetUntil && cam != null)
			{
				Vector3 sp = cam.WorldToScreenPoint(cmdTarget);
				if (sp.z > 0f)
				{
					float x = sp.x, y = Screen.height - sp.y;
					GUI.color = new Color(1f, 0.5f, 0.2f, 0.95f);
					GUI.Label(new Rect(x - 7f, y - 7f, 14f, 14f), "◎", st);
					if (squadCenterDraw != Vector3.zero)
					{
						Vector3 cs = cam.WorldToScreenPoint(squadCenterDraw);
						if (cs.z > 0f) DrawLine(new Vector2(cs.x, Screen.height - cs.y), new Vector2(x, y), st);
					}
				}
			}

			// 标记敌军集火标记（红色菱形 + 目标名，存活期间常显）
			PruneMark();
			var markDraw = mark;
			if (markDraw != null && markDraw.Active && cam != null)
			{
				Vector3 msp = cam.WorldToScreenPoint(markDraw.Position + Vector3.up * 2.2f);
				if (msp.z > 0f)
				{
					float mx = msp.x, my = Screen.height - msp.y;
					GUI.color = new Color(1f, 0.25f, 0.2f, 0.98f);
					GUI.Label(new Rect(mx - 12f, my - 12f, 24f, 24f), "◆", st);
					// 目标名标签（更亮红）
					GUI.color = new Color(1f, 0.45f, 0.4f, 0.95f);
					GUI.Label(new Rect(mx + 10f, my - 10f, 220f, 20f), "⚔ " + (markDraw.Downgraded ? "[降级] " : "") + markDraw.Name, st);
					GUI.color = Color.white;
				}
			}

			// 框选矩形
			if (isDragging)
			{
				Rect sel = GetScreenRect(pressStart, MouseGui());
				GUI.color = new Color(0.2f, 1f, 0.35f, 0.2f);
				GUI.DrawTexture(sel, Texture2D.whiteTexture);
				GUI.color = new Color(0.2f, 1f, 0.35f, 0.8f);
				GUI.DrawTexture(new Rect(sel.x, sel.y, sel.width, 1.5f), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sel.x, sel.yMax, sel.width, 1.5f), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sel.x, sel.y, 1.5f, sel.height), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sel.xMax, sel.y, 1.5f, sel.height), Texture2D.whiteTexture);
				GUI.color = Color.white;
			}

			// 右下角小队列表（编号 + 装甲□/步兵○ 符号）
			DrawSquadPanel(st);

			// 选中单位标记（◆，身上光标）——0.2s 缓存，OnGUI 多 pass 复用
			PruneSelection();
			if (Time.unscaledTime > markerCacheUntil) { markerCacheUntil = Time.unscaledTime + 0.2f; markerCache = GetCommandUnits(); }
			List<Soldier> markers = markerCache;
			for (int i = 0; i < markers.Count; i++)
			{
				Soldier m = markers[i];
				if (m == null || m.transform == null) continue;
				Vector3 sp = cam.WorldToScreenPoint(m.transform.position);
				if (sp.z < 0f) continue;
				GUI.color = selColor;
				GUI.Label(new Rect(sp.x - 5f, Screen.height - sp.y - 5f, 10f, 10f), "◆", st);
				GUI.color = Color.white;
			}

			// 交互命令环（仅上车/下车/物品）
			DrawInteractionWheel(st);
		}
		catch { }
	}

	private static void DrawLine(Vector2 a, Vector2 b, GUIStyle st)
	{
		float dx = b.x - a.x, dy = b.y - a.y, len = Mathf.Sqrt(dx * dx + dy * dy);
		if (len < 1f) return;
		GUI.color = new Color(0.2f, 1f, 0.35f, 0.4f);
		GUI.Label(new Rect(a.x, a.y - 1f, len, 2f), "", st);
		GUI.color = Color.white;
	}

	// ===== 小队列表（右下角，编号 + 符号） =====

	private const float PanelW = 190f, PanelH = 22f, PanelGap = 2f;

	/// <summary>小队符号串：装甲单位 □ 在前，步兵单位 ○ 在后（用户要求：正方形总是在圆形前面）。</summary>
	private static string SquadSymbols(Squad sq)
	{
		if (sq == null) return "";
		int armor = 0, inf = 0;
		try
		{
			int n = sq.CountMembers;
			for (int i = 0; i < n; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m == null || !m.IsAlive) continue;
				if (IsInfantry(m)) inf++; else armor++;
			}
		}
		catch { }
		System.Text.StringBuilder sb = new System.Text.StringBuilder(armor + inf + 2);
		for (int i = 0; i < armor; i++) sb.Append('□');
		for (int i = 0; i < inf; i++) sb.Append('○');
		return sb.ToString();
	}

	private static void RefreshSquadList()
	{
		cachedFriendlySquads.Clear();
		try
		{
			string fac = string.IsNullOrEmpty(SavedFaction) ? GetMyFactionStatic() : SavedFaction;
			Il2CppSystem.Collections.Generic.List<Creature> list = Creature.allCreatures;
			if (list == null || list.Count == 0) return;
			Dictionary<IntPtr, Squad> dict = new Dictionary<IntPtr, Squad>();
			for (int i = 0; i < list.Count; i++)
			{
				Creature c = list[i]; if (c == null) continue;
				Soldier s = c.TryCast<Soldier>(); if (s == null || !s.IsAlive) continue;
				if (!SquadCmdLogic.Friendly(s.faction ?? "", fac)) continue;
				Squad sq = s.joinedSquad; if (sq == null || dict.ContainsKey(sq.Pointer)) continue;
				dict[sq.Pointer] = sq;
			}
			cachedFriendlySquads = new List<Squad>(dict.Values);
		}
		catch { }
		// 排序：成员多的在前（稳定显示）
		cachedFriendlySquads.Sort((a, b) => SafeCount(b).CompareTo(SafeCount(a)));
	}

	private static int SafeCount(Squad sq)
	{
		try { return sq.CountMembers; } catch { return 0; }
	}

	private static void DrawSquadPanel(GUIStyle st)
	{
		if (st == null) return;
		float now = Time.unscaledTime;
		if (now > squadPanelRefresh)
		{
			squadPanelRefresh = now + 2f;
			RefreshSquadList();
		}
		// 行数（含标题行）
		int rows = cachedFriendlySquads.Count + 1;
		float totalH = rows * (PanelH + PanelGap);
		float startX = Screen.width - PanelW - 10f;
		float y = Mathf.Max(8f, Screen.height - 14f - totalH);
		squadPanelHit = new Rect(startX, y, PanelW, totalH);

		// 标题行
		GUI.color = new Color(0.05f, 0.10f, 0.06f, 0.7f);
		GUI.DrawTexture(new Rect(startX, y, PanelW, PanelH), Texture2D.whiteTexture);
		GUI.color = new Color(0.6f, 0.95f, 0.7f, 0.95f);
		GUI.Label(new Rect(startX + 6f, y, PanelW - 6f, PanelH), "— 小队列表 —（□装甲 ○步兵）", st);
		GUI.color = Color.white;
		y += PanelH + PanelGap;

		int idx = 1;
		foreach (Squad sq in cachedFriendlySquads)
		{
			if (sq == null) continue;
			Rect r = new Rect(startX, y, PanelW, PanelH);
			bool isSel = mainSquad != null && sq.Pointer == mainSquad.Pointer;
			GUI.color = isSel ? new Color(0.14f, 0.45f, 0.20f, 0.85f) : new Color(0.10f, 0.10f, 0.10f, 0.72f);
			GUI.Box(r, "");
			string sym = SquadSymbols(sq);
			GUI.color = isSel ? Color.white : new Color(0.85f, 0.85f, 0.85f, 1f);
			GUI.Label(new Rect(r.x + 6f, r.y, r.width - 12f, r.height), idx + "  " + sym, st);
			GUI.color = Color.white;
			if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && r.Contains(Event.current.mousePosition))
			{
				Event.current.Use();
				float tNow = Time.unscaledTime;
				if (sq.Pointer == lastPanelClickSquad && tNow - lastPanelClickTime < DoubleClickTime)
				{
					// 双击：选中该队 + 镜头飞过去
					lastPanelClickTime = -10f; lastPanelClickSquad = IntPtr.Zero;
					SelectSquadFromList(sq);
					FlyTo(SquadCenterOf(sq));
				}
				else
				{
					// 单击：选中该队
					lastPanelClickTime = tNow; lastPanelClickSquad = sq.Pointer;
					SelectSquadFromList(sq);
				}
			}
			y += PanelH + PanelGap;
			idx++;
		}
	}

	/// <summary>从列表选中一个小队：步兵进虚拟选择，载具车组进 selVehicles（不改动其小队归属）。</summary>
	private static void SelectSquadFromList(Squad sq)
	{
		ClearSelection();
		if (sq == null) return;
		try
		{
			int n = sq.CountMembers;
			for (int i = 0; i < n; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m == null || !m.IsAlive) continue;
				if (IsInfantry(m))
				{
					if (!virtualUnits.Contains(m)) virtualUnits.Add(m);
				}
				else
				{
					Squad crew = CrewOf(m);
					if (crew != null && !selVehicles.Contains(crew)) selVehicles.Add(crew);
					try { Vehicle vv = m.GetComponentInParent<Vehicle>(); if (vv != null && vv.transform != null) AddVehicleRef(vv); } catch { }
				}
			}
		}
		catch { }
		selFlash = Time.unscaledTime + 3f;
		cmdFlash = "已选中小队 " + SelTotal + " 个单位"; cmdFlashUntil = Time.unscaledTime + 2f;
	}

	private static Vector3 SquadCenterOf(Squad sq)
	{
		Vector3 sum = Vector3.zero; int n = 0;
		try
		{
			int c = sq.CountMembers;
			for (int i = 0; i < c; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m != null && m.transform != null) { sum += m.transform.position; n++; }
			}
		}
		catch { }
		return n > 0 ? sum / n : Vector3.zero;
	}

	// ===== 右键直接指令（原版功能，无菜单无轮盘，标点就在点击处） =====

	/// <summary>
	/// 右键直接下达指令（标点就在点击处）：
	///  空白地面 → 移动选中单位到点击处；
	///  敌军士兵/载具 → 标记（集火，替代失效的攻击）；
	///  友军/中立载具/士兵 → 打开交互轮盘（上车/下车/物品）。
	/// </summary>
	private static void IssueDirectCommand(Vector2 screenPos, bool fast = false)
	{
		Camera cam = MainCam(); if (cam == null) return;
		try
		{
			Ray ray = cam.ScreenPointToRay(screenPos);
			if (!Physics.Raycast(ray, out RaycastHit hit, 1500f))
			{
				cmdFlash = "未命中地面"; cmdFlashUntil = Time.unscaledTime + 2f;
				return;
			}
			Vehicle veh = hit.collider.transform.GetComponentInParent<Vehicle>();
			if (veh == null) veh = hit.collider.transform.GetComponent<Vehicle>();
			Soldier sol = hit.collider.transform.GetComponentInParent<Soldier>();
			if (sol == null) sol = hit.collider.transform.GetComponent<Soldier>();

			// ---- 载具 ----
			if (veh != null)
			{
				if (VehicleFriendly(veh) || !VehicleHostile(veh))
				{
					// 友军/中立载具 → 打开交互轮盘（上车/下车/物品）
					Squad hitCrew = CrewOf(FirstCrew(veh));
					bool isSelected = hitCrew != null && selVehicles.Contains(hitCrew);
					OpenInteractionWheel(screenPos, veh, isSelected ? hitCrew : null);
				}
				else
				{
					// 敌军载具 → 标记（集火）
					MarkEnemyVehicle(veh);
				}
				return;
			}

			// ---- 士兵 ----
			if (sol != null && sol.IsAlive)
			{
				if (FriendlyUnit(sol))
				{
					// 友军士兵：坐在载具/火力点里 → 交互环（上车）；徒步友军 → 视为地面移动目标（0.7.57：合并已上移顶栏）
					Vehicle vIn = null;
					try { vIn = sol.GetComponentInParent<Vehicle>(); } catch { }
					if (vIn != null && (VehicleFriendly(vIn) || !VehicleHostile(vIn)))
					{
						OpenInteractionWheel(screenPos, veh: vIn, sol: sol);
						return;
					}
					MoveCommandTo(hit.point, fast);
					return;
				}
				else
				{
					// 敌军士兵 → 标记（集火）
					MarkEnemySoldier(sol);
				}
				return;
			}

			// ---- 火力点（TurretGun 无载具父级）→ 交互轮盘 ----
			Vehicle near = NearbyFriendlyVehicle(hit.point);
			if (near != null)
			{
				OpenInteractionWheel(screenPos, veh: near);
				return;
			}

			// ---- 可标记对象（0.7.30 修复：仅限真实单位类对象——Creature/Vehicle 父级。
			// 不得对任意命中物取 Spottable：地形附件/静态物也带 Spottable，会把右键移动劫持成标记）----
			bool unitish = false;
			try { if (hit.collider.transform.GetComponentInParent<Creature>() != null) unitish = true; } catch { }
			try { if (!unitish && hit.collider.transform.GetComponentInParent<Vehicle>() != null) unitish = true; } catch { }
			if (unitish)
			{
				Spottable spotObj = null;
				try { spotObj = Creature.GetConnectedSpottable(hit.collider.transform); } catch { }
				if (spotObj != null)
				{
					MarkSpottable(spotObj, hit.collider.name, "", hit.point);
					return;
				}
			}

			// ---- 空白地面 → 移动 ----
			if (SelTotal == 0)
			{
				cmdFlash = "先框选/选中要指挥的单位"; cmdFlashUntil = Time.unscaledTime + 2f;
				return;
			}
			MoveCommandTo(hit.point, fast);
		}
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] 右键指令失败: " + ex.Message); }
	}

	/// <summary>普通移动前恢复开火（解除不交战状态的 holdFire）。</summary>
	private static void RestoreFireWill()
	{
		foreach (Soldier s in GetSelectedInfantry())
		{
			try { Squad g = s.joinedSquad; if (g != null) new Lua_Squad(g).fireAtWill(true); } catch { }
		}
	}

	/// <summary>整队释放+移动 到目标点。</summary>
	private static void RegisterAndMove(Squad sq, Vector3 p)
	{
		SquadCmdLogic.RegisterControlledSquad(sq);
		SquadCmdLogic.DisableNativeOrders(sq);
		new Lua_Squad(sq).moveTo(p, Plugin.radius.Value);
	}

	/// <summary>移动指令：选中步兵走 + 选中载具开过去（到点击点，标点就在点击处）。移动会解除跟随。</summary>
	private static void MoveCommandTo(Vector3 point, bool fast = false)
	{
		// 0.7.47 诊断：移动前选择状态核对
		{
			List<Soldier> cu = GetCommandUnits();
			SquadCmdLogic.Log("[SelectionCheck] infantry=" + GetSelectedInfantry().Count + " vehicles=" + selVehicles.Count + " commandUnits=" + cu.Count);
			foreach (Vehicle rv in selVehicleRefs)
			{
				int occ = 0; try { Soldier[] os = rv != null && rv.transform != null ? rv.GetComponentsInChildren<Soldier>() : null; if (os != null) for (int i = 0; i < os.Length; i++) if (os[i] != null && os[i].IsAlive) occ++; } catch { }
				SquadCmdLogic.Log("[SelectionCheck] selectedVehicleRef=" + (rv != null ? rv.name : "?") + " occupants=" + occ);
			}
		}
		ClearFollow("下达移动", false);
		// 0.7.78：玩家新命令优先——把选中的、尚未发出 boardVehicle 的乘员从登车 pending 摘除，
		// 否则登车引导（阶段1）会持续拉他们回车边，与本次移动命令竞争（原地踏步根因）。
		if (pendingBoardVeh != null && pendingBoardUnits != null)
		{
			List<Soldier> sel = GetSelectedInfantry();
			int removed = pendingBoardUnits.RemoveAll(s =>
			{
				try
				{
					if (s == null) return true;
					long k = (long)s.Pointer;
					foreach (Soldier x in sel) { try { if (x != null && (long)x.Pointer == k) return true; } catch { } }
					return false;
				}
				catch { return false; }
			});
			if (pendingBoardUnits.Count == 0) CancelBoardPending("玩家下达了新命令");
			else if (removed > 0) SquadCmdLogic.Log("[BoardPending] 摘除 " + removed + " 名改令乘员（余 " + pendingBoardUnits.Count + " 人继续登车）");
		}
		ResetEngagement();
		RestoreFireWill();
		if (SelTotal == 0)
		{
			cmdFlash = "先框选/选中要指挥的单位"; cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}
		List<Soldier> infantry = GetSelectedInfantry();
		// 0.7.74：步兵全部逐兵直奔（Squad 链的"整队等队友"太慢，实测放弃）
		int movedInf = MoveUnits(infantry, point);
		int driven = 0;
		foreach (Vehicle vv in new List<Vehicle>(selVehicleRefs))
		{
			driven += DriveVehicleTo(vv, point);
		}
		if (movedInf > 0 || driven > 0)
		{
			SquadCmdLogic.StartTrackingUnits(infantry.Count > 0 ? infantry : null, point);
			// M7：登记全部步兵
			mvTarget = point; mvUnits.Clear();
			foreach (Soldier s3 in infantry) if (s3 != null && s3.IsAlive) mvUnits.Add(s3);
			mvLastDist.Clear(); mvActive = mvUnits.Count > 0; mvLastCheck = fast ? -10f : Time.unscaledTime; // fast：立即修正一轮
			// 0.7.62：路径质量统计复位
			mvLastPos.Clear();
			foreach (Soldier s3 in mvUnits) try { mvLastPos[(long)s3.Pointer] = s3.transform.position; } catch { }
			mvQualityStart = Time.unscaledTime; mvFixCount = 0;
			mvArriveDist = fast ? 30f : Plugin.radius.Value; // 0.7.75：快速模式到达半径拉大（30m 内即算到位，不与原生散开拉扯）
		}
		mvFromMark = false;
		lastMovePoint = point;
		RecordCmdTarget(point);
		cmdFlash = "移动 → 步兵 " + movedInf + " + 载具 " + driven + (fast ? "（快速）" : ""); cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.Log("[SquadCmd] 移动 point=" + point.ToString("0.0") + " 步兵=" + movedInf + " 载具=" + driven + (fast ? " 快速" : ""));
	}

	/// <summary>停止（选中单位停下，清移动命令）。</summary>
	private static void StopSelected()
	{
		int n = 0;
		SquadCmdLogic.StopTracking();
		// 0.7.45 分层停止：徒步步兵 Soldier.stop；选中载具走 AIVehicle.StopAndClearPath
		foreach (Soldier s in GetSelectedInfantry())
		{
			if (s == null || !s.IsAlive) continue;
			try { new Lua_Soldier(s).stop(); n++; } catch { }
		}
		foreach (Vehicle vv in new List<Vehicle>(selVehicleRefs))
		{
			try
			{
				AIVehicle ai = vv.GetComponent<AIVehicle>();
				if (ai == null) ai = vv.GetComponentInChildren<AIVehicle>();
				if (ai != null) { ai.StopAndClearPath(); n++; }
			}
			catch { }
		}
		cmdFlash = "停止 → " + n + " 单位"; cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.Log("[SquadCmd] 轮盘停止: " + n);
	}

	// ===== 交互命令环（上车/下车/修理/合并）=====
	/// <summary>轮盘当前指向目标的可读描述（诊断日志用）。</summary>
	private static string WheelTargetDesc()
	{
		try
		{
			if (wheelTargetVehicle != null) return "载具[" + SafeName(wheelTargetVehicle) + "]";
			if (wheelTargetSoldier != null) return "士兵[" + SafeName(wheelTargetSoldier) + "]";
			if (wheelTargetVehicleCrew != null) return "已选车组×" + selVehicles.Count;
		}
		catch { }
		return "无";
	}

	private static string YN(bool v) { return v ? "Y" : "N"; }

	/// <summary>开环公共收尾：记录开环输入（供豁免同一次按压）+ 打开 + 一条 OPEN 日志。</summary>
	private static void FinishWheelOpen(string kindTag, string detail)
	{
		wheelOpenedAtTime = Time.unscaledTime;
		wheelOpenedAtGuiPos = new Vector2(wheelScreenPos.x, Screen.height - wheelScreenPos.y);
		showInteractionWheel = true;
		SquadCmdLogic.Log("[SquadCmd] 轮盘 OPEN [" + kindTag + "] 目标=" + WheelTargetDesc() + " " + detail);
	}

	/// <summary>解析轮盘的修理目标：指向的载具，或已选车组对应的载具。</summary>
	private static Vehicle WheelRepairTarget()
	{
		if (wheelTargetVehicle != null) return wheelTargetVehicle;
		return VehicleOfCrew(wheelTargetVehicleCrew);
	}

	/// <summary>原生判定：载具是否有可修的损坏部件（CanBeRepaired；调用异常视为不可修）。</summary>
	private static bool VehicleCanBeRepaired(Vehicle v)
	{
		try { return v != null && v.CanBeRepaired(); } catch { return false; }
	}

	/// <summary>打开常驻命令环（选中单位后长按右键）：站起/蹲下/趴下/停止，作用于当前选择；环心=选中单位的屏幕质心。</summary>
	private static void OpenCommandRing()
	{
		if (SelTotal == 0) { cmdFlash = "先框选/选中单位"; cmdFlashUntil = Time.unscaledTime + 2f; return; }
		Camera cam = MainCam();
		Vector3 c = SelCenter() + Vector3.up * 1.2f;
		if (cam != null)
		{
			Vector3 sp = cam.WorldToScreenPoint(c);
			wheelScreenPos = new Vector2(sp.x, sp.y);
		}
		wheelTargetVehicle = null;
		wheelTargetVehicleCrew = null;
		wheelTargetSoldier = null;
		Camera cc = MainCam();
		hasWheelAnchor = false;
		if (cc != null)
		{
			try { Ray rr = cc.ScreenPointToRay(Input.mousePosition); if (Physics.Raycast(rr, out RaycastHit hh, 3000f)) { wheelAnchorWorld = hh.point + Vector3.up * 1.0f; hasWheelAnchor = true; } } catch { }
		}
		wheelKind = 2;
		wheelItemCount = 4;
		WheelItemLabels[0] = "站起"; WheelItemLabels[1] = "蹲下"; WheelItemLabels[2] = "趴下"; WheelItemLabels[3] = "停止";
		bool hasInf = GetSelectedInfantry().Count > 0;
		WheelItemEnabled[0] = hasInf; WheelItemEnabled[1] = hasInf; WheelItemEnabled[2] = hasInf;
		WheelItemEnabled[3] = true; // 停止对步兵+载具都有效
		FinishWheelOpen("单位", "步兵=" + GetSelectedInfantry().Count + " 载具=" + selVehicles.Count
			+ " 可用项=[站起" + YN(hasInf) + " 蹲下" + YN(hasInf) + " 趴下" + YN(hasInf) + " 停止Y]");
	}

	/// <summary>对选中步兵应用姿态：站起=resetPose 归还 AI；蹲/趴=setPose 持久设置并记入还原名单。</summary>
	private static void ApplyPoseToSelection(SoldierPose pose)
	{
		int n = 0;
		foreach (Soldier s in GetSelectedInfantry())
		{
			if (s == null || !s.IsAlive) continue;
			try
			{
				SquadCmdLogic.RegisterControlledUnit(s);
				new Lua_Soldier(s).setPose((int)pose);
				if (pose != SoldierPose.Idle && !poseLockedUnits.Contains(s)) poseLockedUnits.Add(s);
				n++;
			}
			catch { }
		}
		cmdFlash = PoseName(pose) + " → " + n + " 单位"; cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.Log("[SquadCmd] 姿态 " + PoseName(pose) + " 单位=" + n);
	}

	// setPose 是持久设置（实测：趴下后退出 RTS、玩家接管都无法自行站起）——
	// 蹲/趴过的单位记入名单，退出上帝视角/接管前逐个 resetPose() 归还 AI 姿态控制
	private static readonly List<Soldier> poseLockedUnits = new List<Soldier>();

	private static void ResetPoseToSelection()
	{
		int n = 0;
		foreach (Soldier s in GetSelectedInfantry())
		{
			if (s == null || !s.IsAlive) continue;
			try { SquadCmdLogic.RegisterControlledUnit(s); new Lua_Soldier(s).resetPose(); n++; } catch { }
		}
		cmdFlash = "站起（恢复 AI 姿态） → " + n + " 单位"; cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.Log("[SquadCmd] 姿态 站起(resetPose) 单位=" + n);
	}

	/// <summary>把蹲/趴姿态全部还原为 AI 自动姿态（退出 RTS / 接管单位前调用）。</summary>
	private static void RestoreAllPoses()
	{
		for (int i = poseLockedUnits.Count - 1; i >= 0; i--)
		{
			Soldier s = poseLockedUnits[i];
			try { if (s != null && s.IsAlive) new Lua_Soldier(s).resetPose(); } catch { }
		}
		bool had = poseLockedUnits.Count > 0;
		poseLockedUnits.Clear();
		if (had) SquadCmdLogic.Log("[SquadCmd] 姿态已还原（退出/接管）");
	}

	/// <summary>把士兵并入指定小队（记录原队映射），不动 mainSquad 字段。</summary>
	private static bool AddInfantryToSquadTo(Soldier s, Squad target)
	{
		if (s == null || target == null) return false;
		try
		{
			Squad old = s.joinedSquad;
			if (old != null && old.Pointer != target.Pointer)
			{
				originSquadMap[(long)s.Pointer] = old;
				old.Leave(s, false);
			}
			target.Join(s);
			return true;
		}
		catch { return false; }
	}

	private static int AliveCount(Squad sq)
	{
		int n = 0;
		try
		{
			int c = sq.CountMembers;
			for (int i = 0; i < c; i++) { Soldier m = sq.GetMemberClamped(i); if (m != null && m.IsAlive) n++; }
		}
		catch { }
		return n;
	}

	/// <summary>M6前置：修理订单所用的小队——选中步兵共同所属、且不包含未选中成员的原生小队。</summary>
	private static Squad NativeRepairSquad()
	{
		List<Soldier> inf = GetSelectedInfantry();
		if (inf.Count == 0) return null;
		Squad sq = null;
		try { sq = inf[0].joinedSquad; } catch { return null; }
		if (sq == null || AliveCount(sq) != inf.Count) return null;
		return sq;
	}

	private static string PoseName(SoldierPose p)
	{
		return p == SoldierPose.Idle ? "站起" : p == SoldierPose.Crouch ? "蹲下" : "趴下";
	}

	/// <summary>打开交互轮盘：右键友军/中立载具、车内士兵时调用。记录开环输入，供 DrawInteractionWheel 豁免同一次按压。</summary>
	private static void OpenInteractionWheel(Vector2 screenPos, Vehicle veh = null, Squad crew = null, Soldier sol = null)
	{
		wheelScreenPos = screenPos;
		wheelTargetVehicle = veh;
		wheelTargetVehicleCrew = crew;
		wheelTargetSoldier = sol;
		hasWheelAnchor = false; // 0.7.80：清除单位环遗留锚定，否则交互环被钉死在旧鼠标落点（不跟随载具）
		wheelKind = 0;
		wheelItemCount = 4;
		WheelItemLabels[0] = "上车"; WheelItemLabels[1] = "下车"; WheelItemLabels[2] = "修理"; WheelItemLabels[3] = "合并";
		bool hasSelInf = GetSelectedInfantry().Count > 0;
		bool hasSelVeh = selVehicles.Count > 0;
		PruneVehicleRefs();
		bool occupiedSel = AnySelRefOccupied(); // 0.7.43：实时占用判定（无缓存无滞后）
		WheelItemEnabled[0] = ((veh != null || sol != null) && hasSelInf) || (veh != null && !occupiedSel); // 上车：有步兵可选，或选中车已全空
		WheelItemEnabled[1] = occupiedSel; // 下车：选中的车里确有乘员
		// 修理：目标载具可修（原生 CanBeRepaired=部件损坏）且有 RTS 建队步兵接单
		// （单击虚拟选择没有受控小队——原生 Squad 修理订单会牵动未选中队友，故不启用）
		Vehicle repairTarget = veh != null ? veh : VehicleOfCrew(crew);
		WheelItemEnabled[2] = repairTarget != null && NativeRepairSquad() != null && VehicleCanBeRepaired(repairTarget);
		// 跟随：目标是友军/中立载具（非本方已选车组），且选中了任意步兵或车组
		Vehicle mTgt = veh != null ? veh : VehicleOfCrew(crew);
		WheelItemEnabled[3] = mTgt != null && CrewOf(FirstCrew(mTgt)) != null && (hasSelInf || hasSelVeh);
		FinishWheelOpen("交互", "选择=步兵" + YN(hasSelInf) + "/载具" + YN(hasSelVeh)
			+ " 可用项=[上车" + YN(WheelItemEnabled[0]) + " 下车" + YN(WheelItemEnabled[1]) + " 修理" + YN(WheelItemEnabled[2]) + " 合并" + YN(WheelItemEnabled[3]) + "]");
	}

	/// <summary>打开友军上下文环（右键徒步友军士兵；合并进目标所在小队）。</summary>
	/// <summary>关闭交互轮盘（reason 仅用于诊断日志；目标描述在清理前记录）。同时吞掉当前左键手势的剩余部分。</summary>
	private static void CloseInteractionWheel(string reason = "未知")
	{
		SquadCmdLogic.Log("[SquadCmd] 轮盘 CLOSE 原因=" + reason + " 目标=" + WheelTargetDesc());
		showInteractionWheel = false;
		wheelTargetVehicle = null;
		wheelTargetVehicleCrew = null;
		wheelTargetSoldier = null;
		swallowLeftGesture = true;
	}

	/// <summary>执行轮盘选中的动作（按 wheelKind 映射：交互环 0/1/2=上车/下车/修理，槽3=合并；友军环槽0=合并；单位环 4..7=站/蹲/趴/停止）。</summary>
	private static void ExecuteWheelAction(int index)
	{
		string actionName = index >= 0 && index < wheelItemCount ? WheelItemLabels[index] : "?";
		int actionId = index;                    // 默认=交互环槽位（上车/下车/修理/合并）
		if (wheelKind == 2) actionId = 4 + index; // 单位环槽0..3 = 站起/蹲下/趴下/停止
		SquadCmdLogic.Log("[SquadCmd] 轮盘 EXECUTE 动作=" + actionName + " 目标=" + WheelTargetDesc());
		switch (actionId)
		{
			case 0: // 上车
				if (wheelTargetVehicle != null) BoardVehicle(wheelTargetVehicle);
				else if (wheelTargetSoldier != null)
				{
					Vehicle vIn = null;
					try { vIn = wheelTargetSoldier.GetComponentInParent<Vehicle>(); } catch { }
					if (vIn != null) BoardVehicle(vIn);
					else
					{
						Vehicle near = NearbyFriendlyVehicle(wheelTargetSoldier.transform.position);
						if (near != null) BoardVehicle(near);
					}
				}
				break;
			case 1: // 下车
				DismountAllVehicles();
				break;
			case 2: // 修理：RTS 小队对目标载具下原生修理订单（士兵走去执行 VehicleRepairTask）
				{
					Vehicle rv = WheelRepairTarget();
					Squad rsq = NativeRepairSquad();
					if (rv != null && rsq != null)
					{
						try
						{
							SquadCmdLogic.StopTracking();
							SquadCmdLogic.RegisterControlledSquad(rsq);
							rsq.OrderRepairVehicle(rv);
							cmdFlash = "修理 → " + SafeName(rv); cmdFlashUntil = Time.unscaledTime + 2f;
							SquadCmdLogic.Log("[SquadCmd] 修理订单 " + rv.name + " 队 ptr=0x" + ((long)mainSquad.Pointer).ToString("X"));
						}
						catch (Exception ex)
						{
							cmdFlash = "修理失败: " + ex.Message; cmdFlashUntil = Time.unscaledTime + 2f;
							SquadCmdLogic.Log("[SquadCmd] OrderRepairVehicle 失败: " + ex.Message);
						}
					}
				}
				break;
			case 3: // 合并：选中步兵/其他载具乘员并入目标小队；上限12人，多余保持原队
				{
					Squad tsq = null;
					try { tsq = wheelTargetSoldier != null ? wheelTargetSoldier.joinedSquad : null; } catch { }
					if (tsq == null)
					{
						Vehicle tv = wheelTargetVehicle != null ? wheelTargetVehicle : VehicleOfCrew(wheelTargetVehicleCrew);
						try { Squad cs = CrewOf(FirstCrew(tv)); if (cs != null) tsq = cs; } catch { }
					}
					if (tsq != null)
					{
						int cap = 12 - AliveCount(tsq);
						int mgd = 0, skip = 0;
						foreach (Soldier s2 in GetSelectedInfantry())
							if (s2 != null && s2.IsAlive)
							{
								if (cap <= 0) { skip++; continue; }
								try { if (AddInfantryToSquadTo(s2, tsq)) { mgd++; cap--; } } catch { }
							}
						bool vehMerged = false;
						foreach (Squad csq in new List<Squad>(GetSelectedVehicleCrews()))
						{
							if (csq == null || (tsq != null && csq.Pointer == tsq.Pointer)) continue;
							Vehicle mv = VehicleOfCrew(csq);
							Vehicle tvv = wheelTargetVehicle != null ? wheelTargetVehicle : VehicleOfCrew(wheelTargetVehicleCrew);
							if (mv == null || mv.transform == null || tvv == null) { skip++; continue; }
							try
							{
								Soldier[] occ = mv.GetComponentsInChildren<Soldier>();
								for (int i = 0; i < occ.Length; i++)
								{
									if (occ[i] == null || !occ[i].IsAlive) continue;
									if (cap <= 0) { skip++; continue; }
									if (AddInfantryToSquadTo(occ[i], tsq)) { mgd++; cap--; }
								}
								selVehicles.Remove(csq);
								if (!selVehicles.Contains(tsq)) selVehicles.Add(tsq);
								if (tvv != null) AddVehicleRef(tvv);
								vehMerged = true;
							}
							catch { skip++; }
						}
						if (vehMerged)
						{
							mainSquad = null; virtualUnits.Clear();
						}
						else { mainSquad = null; virtualUnits.Clear(); }
						ClearFollow("合并", false);
						cmdFlash = "已合并 " + mgd + " 人入目标小队" + (skip > 0 ? "（超出上限留下 " + skip + "）" : "");
						cmdFlashUntil = Time.unscaledTime + 2.5f;
						SquadCmdLogic.Log("[SquadCmd] 合并 入队=" + mgd + " 溢出=" + skip + " 队ptr=0x" + ((long)tsq.Pointer).ToString("X"));
					}
					else { cmdFlash = "目标没有所属小队"; cmdFlashUntil = Time.unscaledTime + 2f; }
				}
				break;
			case 4: ResetPoseToSelection(); break;
			case 5: ApplyPoseToSelection(SoldierPose.Crouch); break;
			case 6: ApplyPoseToSelection(SoldierPose.Prone); break;
			case 7: // 停止：取消当前 RTS 行为（移动/标记），作用于全部选中单位
				ClearFollow("停止", false);
				ClearMark();
				ResetEngagement();
				mvActive = false;
				StopSelected();
				break;
		}
		CloseInteractionWheel("执行动作:" + actionName);
	}

	/// <summary>绘制交互轮盘（IMGUI，鼠标位置为中心；每项独立底板+描边，不再画大面积背景方块）。</summary>
	/// <summary>当前环心（IMGUI 坐标）：有载具目标时锚定其世界位置（随镜头/目标实时更新），否则用开环屏幕点；均做防出屏钳制。</summary>
	private static Vector2 WheelCenterGui(Camera cam)
	{
		Vector2 c = wheelScreenPos;
		c.y = Screen.height - c.y;
		try
		{
			if (cam != null)
			{
				Vector3 wp = hasWheelAnchor ? wheelAnchorWorld
					: (wheelTargetVehicle != null && wheelTargetVehicle.transform != null ? wheelTargetVehicle.transform.position : Vector3.zero);
				bool useW = hasWheelAnchor || (wheelTargetVehicle != null && wheelTargetVehicle.transform != null);
				if (useW)
				{
					Vector3 sp = cam.WorldToScreenPoint(wp);
					if (sp.z > 0f) c = new Vector2(sp.x, Screen.height - sp.y);
				}
			}
		}
		catch { }
		c.x = Mathf.Clamp(c.x, WheelEdgeMarginX, Screen.width - WheelEdgeMarginX);
		c.y = Mathf.Clamp(c.y, WheelEdgeMarginY, Screen.height - WheelEdgeMarginY);
		return c;
	}

	private static Rect WheelButtonRect(int index, Vector2 center)
	{
		float angle = -90f + index * (360f / Mathf.Max(1, wheelItemCount));
		float rad = angle * Mathf.Deg2Rad;
		Vector2 pos = center + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * WheelItemDist;
		return new Rect(pos.x - WheelBtnW * 0.5f, pos.y - WheelBtnH * 0.5f, WheelBtnW, WheelBtnH);
	}

	/// <summary>鼠标是否悬停在任一轮盘按钮上（HandleClick 左键路由用，与绘制共用同一几何源）。</summary>
	private static bool MouseOverWheelButton(Vector2 guiPos)
	{
		if (!showInteractionWheel || wheelItemCount <= 0) return false;
		Vector2 c = WheelCenterGui(MainCam());
		for (int i = 0; i < wheelItemCount; i++)
			if (WheelButtonRect(i, c).Contains(guiPos)) return true;
		return false;
	}

	/// <summary>绘制轮盘（IMGUI）：环心锚定目标载具世界位置；每项独立底板+描边。</summary>
	private static void DrawInteractionWheel(GUIStyle st)
	{
		if (!showInteractionWheel || st == null || wheelItemCount <= 0) return;
		Event e = Event.current;
		Vector2 center = WheelCenterGui(MainCam());
		Vector2 mouse = e.mousePosition;
		for (int i = 0; i < wheelItemCount; i++)
		{
			Rect btn = WheelButtonRect(i, center);
			bool hover = btn.Contains(mouse) && WheelItemEnabled[i];
			// 按钮底板
			GUI.color = WheelItemEnabled[i] ? new Color(0.06f, 0.13f, 0.08f, 0.92f) : new Color(0.10f, 0.10f, 0.10f, 0.75f);
			GUI.DrawTexture(btn, Texture2D.whiteTexture);
			// 描边（悬停亮绿），与顶部控制按钮同风格
			GUI.color = hover ? new Color(0.45f, 1f, 0.55f, 0.95f) : new Color(0.25f, 0.55f, 0.3f, 0.9f);
			GUI.DrawTexture(new Rect(btn.x, btn.y, btn.width, 1.5f), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(btn.x, btn.yMax - 1.5f, btn.width, 1.5f), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(btn.x, btn.y, 1.5f, btn.height), Texture2D.whiteTexture);
			GUI.DrawTexture(new Rect(btn.xMax - 1.5f, btn.y, 1.5f, btn.height), Texture2D.whiteTexture);
			// 文字
			GUI.color = WheelItemEnabled[i] ? (hover ? Color.white : new Color(0.85f, 0.95f, 0.87f, 0.95f)) : new Color(0.55f, 0.55f, 0.55f, 0.8f);
			GUI.Label(btn, WheelItemLabels[i], st);
			if (hover && e.type == EventType.MouseDown && e.button == 0)
			{
				e.Use();
				ExecuteWheelAction(i);
				return;
			}
		}
		// 环心小标记
		GUI.color = new Color(0.5f, 1f, 0.6f, 0.8f);
		GUI.Label(new Rect(center.x - 6f, center.y - 10f, 12f, 12f), "◎", st);
		// 右键关闭（豁免开环按压：开环的那次 MouseDown(1) 迟到进入 IMGUI 时，
		// 处于豁免窗口内且未移出豁免半径 → 不视为"第二次右键"，只吞掉事件不关闭）
		if (e.type == EventType.MouseDown && e.button == 1)
		{
			float sinceOpen = Time.unscaledTime - wheelOpenedAtTime;
			bool withinGuard = sinceOpen < WheelOpenGuardSeconds
				&& ((Vector2)e.mousePosition - wheelOpenedAtGuiPos).sqrMagnitude < WheelOpenGuardPixels * WheelOpenGuardPixels;
			e.Use();
			if (!withinGuard) CloseInteractionWheel("二次右键");
		}
		GUI.color = Color.white;
	}

	private static readonly Color selColor = new Color(0.2f, 1f, 0.35f, 0.95f);
}
