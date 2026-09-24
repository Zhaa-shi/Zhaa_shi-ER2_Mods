using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 1.2.0：地狱之门式阵型箭头（右键长按 0.35s 起手，拖出箭头，松开下发）。
///   语义（用户定案）：阵型线中心 = 长按点 A；线方向垂直于 AB；线总长 = |AB|；单位面向 B 方向。
///   目标区有掩体（CoverManager.GetCovers 按阵营+受敌方向查询，原生掩体点含沙袋等，装 Combat Cover 类内容包时更密）
///   → 步兵按分配进掩体（1.2.1：moveTo + 到位 setPose，见下）；分不到掩体的步兵与载具沿阵型线垂直排开。
///   拖动中预览：掩体分配 = 白色半透明幽灵模型（GhostPreview，失败自动降级为标记）；
///   无掩体步兵槽 = 黄点；载具槽 = 角括号；阵型线本身画一条黄线。
///
/// 1.2.1 修复（用户实测）：
///   · 掩体单位不动 → 原生 findCover 实测不下移动令，改走 MoveUnits + 到位 TickCoverArrivals 补 setPose。
///   · 拖到屏幕外/指向天空时抽搐 → 射线改为「物理命中（限距+法线过滤）→ 锚点等高平面求交」双通道，
///     并把箭头长度钳到 MaxArrowLen；超远落点曾让阵型线拉出几千米 → 标记被视锥裁切（"标记被截断"）+ 大跳变。
///   · 标记不跟手 → 布局（线槽/阵型线/箭头）改为**每帧**重算（纯数学，无分配）；
///     只有昂贵的掩体查询+幽灵刷新按 0.35s 节流（分配是固定点，不需要每帧重算）。
///   · 每帧零分配：复用缓冲列表 + 插入排序（无 lambda 闭包）。
/// </summary>
internal static class Formation
{
	// ===== 拖动状态 =====
	private static bool dragging;
	internal static bool Dragging => dragging;
	private static Vector3 anchor;
	private static bool hasAnchor;
	private static Vector3 end;
	private static bool hasEnd;
	// 1.2.7：起手时**冻结相机基向量与像素比例**——拖动期间不再读相机，
	// 彻底断开"箭头 → 相机 → 箭头"的反馈回路（此前视角/端点互相激发的根因）。
	private static Vector3 dragRight = Vector3.right;
	private static Vector3 dragFwd = Vector3.forward;
	private static Vector2 dragAnchorGui;
	private static float dragPerPx = 1f;

	// ===== 轴（每帧重算）=====
	private static Vector3 facingDir = Vector3.forward;
	private static Vector3 perpDir = Vector3.right;
	private static float lineLen;

	// ===== 计划 =====
	private static readonly List<CoverSlot> coverSlots = new List<CoverSlot>();   // 掩体分配（0.35s 节流）
	private static readonly List<LineSlot> lineSlots = new List<LineSlot>();     // 无掩体步兵（每帧）
	private static readonly List<LineSlot> vehSlots = new List<LineSlot>();      // 载具（每帧）
	private static readonly List<Vehicle> facingOnly = new List<Vehicle>();      // 1.2.3：不可移动的火力点/火炮 → 只转向
	private static readonly HashSet<long> coveredPtrs = new HashSet<long>();
	private static float coverQueryNext = -10f;

	// ===== 复用缓冲（避免每帧分配）=====
	private static readonly List<Soldier> infBuf = new List<Soldier>();
	private static readonly List<Soldier> lineBuf = new List<Soldier>();
	private static readonly List<Vehicle> vehBuf = new List<Vehicle>();

	internal class CoverSlot
	{
		public Soldier unit;
		public Vector3 pos;
		public SoldierPose pose;
	}

	internal class LineSlot
	{
		public Soldier unit;   // 步兵槽（veh 为 null 时有效）
		public Vehicle veh;    // 载具槽
		public Vector3 pos;
		public int rank;
	}

	class PendingFacing
	{
		public Vehicle veh;
		public Vector3 slot;
		public Vector3 facePoint;
		public Vector3 dir;      // 1.2.4：期望朝向（轮式车用"再开一段"实现，不原地转）
		public float deadline;
	}

	/// <summary>1.2.1：掩体单位到位后补建议姿态（moveTo 方案的收尾）。</summary>
	class CoverArrival
	{
		public Soldier unit;
		public Vector3 pos;
		public SoldierPose pose;
		public float deadline;
	}


	private static readonly List<PendingFacing> pendingFacings = new List<PendingFacing>();
	private static readonly List<CoverArrival> pendingCoverArrivals = new List<CoverArrival>();
	/// <summary>1.2.4：本次拖动是否"仅固定火力点/火炮"模式（锚点=单位自身，箭头从单位伸出指向鼠标）。</summary>
	private static bool facingOnlyMode;
	private static readonly List<Vehicle> facingOnlyScratch = new List<Vehicle>();
	private static float dragDiagNext = -10f; // 1.2.11：拖动诊断限频
	private static float rmbUpSince = -1f;   // 1.2.17：右键松开时刻（看门狗宽限用）

	// ===== 布局/行为常量 =====
	private const float InfSpacing = 1.4f;      // 步兵线间距（米）
	private const float VehSpacing = 7f;        // 载具线间距
	private const float RankGap = 3f;           // 第二排纵深
	private const float MinArrowLen = 1.0f;     // 箭头短于此按"普通点移动"处理
	private const float MaxArrowLen = 120f;     // 1.2.1：箭头长度上限（超远落点 = 标记裁切 + 跳变根因）
	private const float GroundRayDist = 400f;   // 1.2.1：地面射线限距（超出即走平面兜底，避免掠射噪声）
	private const float CoverQueryInterval = 0.35f; // 掩体查询节流（昂贵：八叉树枚举 + 占用判定）
	private const float FacingArriveDist = 7f;  // 载具距槽位多近开始转向
	private const float FacingTimeout = 45f;    // 载具一直到不了槽位就放弃转向
	private const float MaxCoverQueryRadius = 35f; // 1.2.1：掩体查询半径上限
	private const int MaxCoverResults = 24;        // 1.2.1：单次掩体枚举上限
	private const float CoverArriveDist = 1.8f;    // 1.2.1：进掩体到位判定距离
	private const float CoverArriveTimeout = 90f;  // 1.2.1：到位姿态补发超时
	private const float WheeledRollDist = 14f;     // 1.2.4：轮式车到位后"续驶摆正"距离（不原地转）

	/// <summary>1.2.4：是否履带车（履带可原地转向；轮式只能靠行驶摆正）。</summary>
	private static bool IsTracked(Vehicle v)
	{
		try { return v != null && v.TryCast<VehicleTank>() != null; } catch { return false; }
	}

	// ===== 起手：长按到点时调用。锚点射线命中地面 → 进入拖动态 =====
	/// <param name="downScreenPos">按下时的屏幕点（Unity 左下原点）</param>
	/// <param name="pressedOnUnit">按下点是否命中单位（命中时默认不开阵型，除非是选中的固定火力点）</param>
	internal static bool TryBeginDrag(Vector2 downScreenPos, bool pressedOnUnit = false)
	{
		Camera cam = GodViewController.MainCam();
		if (cam == null) return false;
		try
		{
			// 1.2.7：按在**已选中的固定火力点/火炮**上 → 直接以该单位自身为锚点进入朝向模式
			// （用户反馈"火炮等火力点还是不能操控转向"——炮位有 AIVehicle 时会走正常阵型，
			//  现在改用"是否履带/轮式/飞机"判定可移动性，炮位一律进朝向模式）。
			CollectFacingOnly(facingOnlyScratch);
			if (pressedOnUnit && facingOnlyScratch.Count > 0)
			{
				Vehicle hitVeh = RaycastVehicle(cam, downScreenPos);
				Vehicle pick = null;
				if (hitVeh != null)
					foreach (Vehicle v in facingOnlyScratch) { try { if (v != null && v.Pointer == hitVeh.Pointer) { pick = v; break; } } catch { } }
				if (pick != null)
				{
					anchor = pick.transform.position;
					hasAnchor = true; end = anchor; hasEnd = false; dragging = true;
					facingOnlyMode = true;
					coverSlots.Clear(); lineSlots.Clear(); vehSlots.Clear(); coveredPtrs.Clear();
					facingOnly.Clear(); facingOnly.Add(pick);
					CaptureDragBasis(cam);
					RecomputeAxes();
					SquadCmdLogic.Log("[Formation] 火力点朝向拖动 begin（按在炮位上）");
					return true;
				}
			}

			// 只选中固定火力点/火炮时：不需要点空地，锚点取单位自身
			CollectFacingOnly(facingOnlyScratch);
			if (facingOnlyScratch.Count > 0)
			{
				anchor = facingOnlyScratch[0].transform.position;
				hasAnchor = true;
				end = anchor;
				hasEnd = false;
				dragging = true;
				facingOnlyMode = true;
				coverSlots.Clear(); lineSlots.Clear(); vehSlots.Clear(); coveredPtrs.Clear();
				facingOnly.Clear();
				facingOnly.AddRange(facingOnlyScratch);
				coverQueryNext = -10f;
				CaptureDragBasis(cam);
				RecomputeAxes();
				SquadCmdLogic.Log("[Formation] 火力点朝向拖动 begin anchor=单位 " + anchor.ToString("0.0"));
				return true;
			}
			facingOnlyMode = false;
			if (!Physics.Raycast(cam.ScreenPointToRay(downScreenPos), out RaycastHit hit, GroundRayDist)) return false;
			if (hit.normal.y < 0.25f) return false; // 打在山坡侧面/单位上不算（放宽：略陡地形也可作锚点）
			anchor = hit.point;
			hasAnchor = true;
			end = anchor;
			hasEnd = false;
			dragging = true;
			coverSlots.Clear(); lineSlots.Clear(); vehSlots.Clear(); coveredPtrs.Clear();
			coverQueryNext = -10f; // 立刻做第一轮掩体查询
			CaptureDragBasis(cam);
			RecomputeAxes();
			LogDragCamera("开始");
			return true;
		}
		catch (Exception ex) { SquadCmdLogic.Log("[Formation] drag begin 失败: " + ex.Message); return false; }
	}

	private static Vehicle RaycastVehicle(Camera cam, Vector2 screenPos)
	{
		try
		{
			if (!Physics.Raycast(cam.ScreenPointToRay(screenPos), out RaycastHit hit, 1500f)) return null;
			Vehicle v = hit.collider.transform.GetComponentInParent<Vehicle>();
			if (v == null) v = hit.collider.transform.GetComponent<Vehicle>();
			return v;
		}
		catch { return null; }
	}

	/// <summary>1.2.7：收集选中里的**不可移动**载具（非履带/轮式/飞机 = 火力点、火炮、拖车）。</summary>
	private static void CollectFacingOnly(List<Vehicle> dst)
	{
		dst.Clear();
		FillFootInfantry(infBuf);
		if (infBuf.Count > 0) return; // 有步兵 → 走正常阵型
		GodViewController.GetVehicleRefsInto(vehBuf);
		foreach (Vehicle v in vehBuf)
		{
			if (v == null || v.transform == null) continue;
			if (!IsMobileVehicle(v)) dst.Add(v);
		}
	}

	/// <summary>1.2.7：可移动载具判定（履带/轮式/飞机）——其余（炮位/火炮/拖车）一律"只转向"。
	/// 旧判定用 `AIVehicle` 存在与否，而炮位也带 AIVehicle → 被误当可驾驶（用户反馈"还是不能转向"）。</summary>
	internal static bool IsMobileVehicle(Vehicle v)
	{
		try { if (v == null || v.transform == null) return false; } catch { return false; }
		try { if (v.TryCast<VehicleTank>() != null) return true; } catch { }
		try { if (v.TryCast<VehicleWithWheels>() != null) return true; } catch { }
		try { if (v.TryCast<VehiclePlane>() != null) return true; } catch { }
		return false;
	}

	/// <summary>1.2.7：供长按仲裁用——当前选择是否"只含不可移动火力点/火炮"（此时允许按在单位上起手）。</summary>
	internal static bool SelectionIsFacingOnlyNow()
	{
		try { CollectFacingOnly(facingOnlyScratch); return facingOnlyScratch.Count > 0; } catch { return false; }
	}

	/// <summary>拖动中每帧：更新箭头终点 + 轴 + 布局（廉价）；掩体查询/幽灵刷新按 0.35s 节流。</summary>
	internal static void DragTick()
	{
		if (!dragging || !hasAnchor) return;
		// 1.2.15 看门狗：拖动中若已退出 RTS / 右键已松开 → 取消并清幽灵。
		// **1.2.17 修复（阵型完全失效的根因）**：本函数在 Tick 里跑在 HandleClick **之前**，
		// 松开的那一帧 HandleClick 还要用这次拖动下发阵型——原来看门狗同帧抢先
		// CancelDrag（dragging=false）→ IssueFromDrag 直接 return → 阵型永远不下发。
		// 现在给 0.15s 宽限：只有"松开后迟迟没被收尾"（真正的漏网路径）才取消。
		try
		{
			if (!GodViewController.Active) { CancelDrag("已退出 RTS"); return; }
			if (!Input.GetMouseButton(1))
			{
				if (rmbUpSince < 0f) rmbUpSince = Time.unscaledTime;
				else if (Time.unscaledTime - rmbUpSince > 0.15f) { CancelDrag("右键已松开"); return; }
			}
			else rmbUpSince = -1f;
		}
		catch { }
		Camera cam = GodViewController.MainCam();
		if (cam == null) return;
		UpdateEndPoint(cam);
		RecomputeAxes();
		// 1.2.12 诊断：拖动期间**仅在异常时**记录（光标被锁/被显示、相机偏离我们的姿态）。
		// 原实现用 Log（受 debugLog 门控 → 默认关闭时一行都没有），且无条件记录会刷屏；
		// 现在只在真正异常时用 LogAlways 输出，平时完全静默。
		if (Time.unscaledTime > dragDiagNext)
		{
			dragDiagNext = Time.unscaledTime + 1f;
			try
			{
				Camera c2 = GodViewController.MainCam();
				bool cursorBad = Cursor.lockState != CursorLockMode.None || Cursor.visible;
				Vector3 want = GodViewController.CamPosDiag;
				bool camBad = c2 != null && (c2.transform.position - want).sqrMagnitude > 0.25f;
				if (cursorBad || camBad)
				{
					SquadCmdLogic.LogAlways("[FormDiag] 异常 lock=" + Cursor.lockState + " vis=" + Cursor.visible
						+ " cam=" + (c2 != null ? c2.transform.position.ToString("0.0") : "null")
						+ " want=" + want.ToString("0.0")
						+ " rmb=" + Input.GetMouseButton(1)
						+ " ghost=" + GhostPreview.LiveCount);
				}
			}
			catch { }
		}
		if (Time.unscaledTime >= coverQueryNext)
		{
			coverQueryNext = Time.unscaledTime + CoverQueryInterval;
			RebuildCoverAssignment();
			// 1.2.17：撤掉 1.2.16 的"手稳"判据——拖动中鼠标一直在动，判据恒 false →
			// 幽灵永远建不出来（用户反馈"幽灵数量对不上绿圈"）。
			// 幽灵按单位指针池化（每单位只建一次），成本可控；帧尖刺改由
			// "组件处理改回强类型 + 幽灵真正惰性化（无 AI/无物理）"解决，而不是靠不建。
			GhostPreview.Apply(coverSlots, facingDir);
		}
		BuildLayout();
	}

	/// <summary>
	/// 箭头终点（1.2.7：**起手冻结基向量 + 纯屏幕空间**，全程不读相机）。
	/// 历史：① 等高平面求交 → 近水平视角解到相机背后；② 相机前向半空间约束 → 只剩 180°；
	/// ③ 射线命中/未命中逐帧交替 → 端点大跳（"视角乱飞"）；④ 每帧读相机做屏幕映射 → 相机被改时形成反馈。
	/// 现在方向 = 起手时相机右/前基向量 × 鼠标相对起手点的屏幕位移，长度 = 像素 × 起手时算好的比例。
	/// </summary>
	private static void UpdateEndPoint(Camera cam)
	{
		try
		{
			Vector2 mGui = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
			Vector2 d = mGui - dragAnchorGui;
			float px = d.magnitude;
			if (px < 6f) { end = anchor; hasEnd = false; return; }
			// 屏幕上 = 远离相机（+前），屏幕右 = +右；IMGUI 的 d.y 向下为正，故取负
			Vector3 dir = dragRight * d.x + dragFwd * (-d.y);
			if (dir.sqrMagnitude < 0.0001f) return;
			dir.Normalize();
			float len = Mathf.Clamp(px * dragPerPx, 0f, MaxArrowLen);
			end = anchor + dir * len;
			hasEnd = len > 0.05f;
		}
		catch { }
	}

	/// <summary>1.2.7：起手时记录锚点屏幕位置、相机基向量与"像素→米"比例（拖动期间不再读相机）。</summary>
	private static void CaptureDragBasis(Camera cam)
	{
		try
		{
			Vector3 sp = cam.WorldToScreenPoint(anchor);
			dragAnchorGui = new Vector2(sp.x, Screen.height - sp.y);
			Vector3 right = cam.transform.right; right.y = 0f; right.Normalize();
			Vector3 fwd = cam.transform.forward; fwd.y = 0f;
			if (fwd.sqrMagnitude < 0.0001f) fwd = Vector3.forward;
			fwd.Normalize();
			if (right.sqrMagnitude < 0.0001f) right = Vector3.right;
			dragRight = right; dragFwd = fwd;
			float camH = Mathf.Max(4f, cam.transform.position.y - anchor.y);
			dragPerPx = (2f * camH * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) / Mathf.Max(1f, Screen.height);
		}
		catch { }
	}

	private static void RecomputeAxes()
	{
		Vector3 d = hasEnd ? end - anchor : Vector3.zero;
		d.y = 0f;
		lineLen = d.magnitude;
		facingDir = lineLen > 0.05f ? d / lineLen : Vector3.forward;
		perpDir = new Vector3(facingDir.z, 0f, -facingDir.x);
		if (perpDir.sqrMagnitude < 0.0001f) perpDir = Vector3.right;
		perpDir.Normalize();
	}

	// ===== 掩体分配（0.35s 节流）=====

	private static void RebuildCoverAssignment()
	{
		coverSlots.Clear();
		coveredPtrs.Clear();
		FillFootInfantry(infBuf);
		if (infBuf.Count == 0 || lineLen < MinArrowLen) return;
		float radius = Mathf.Clamp(Mathf.Max(10f, lineLen * 0.5f + 12f), 10f, MaxCoverQueryRadius);
		AssignCovers(anchor, radius, facingDir, lineLen * 0.5f + 12f);
		if (Plugin.debugLog.Value)
			SquadCmdLogic.Log("[Formation] covers 步兵=" + infBuf.Count + " 掩体=" + coverSlots.Count);
	}

	/// <summary>掩体分配核心（阵型箭头与"右键建筑进掩体"共用）：按中心查询 → 就近贪心分配 → 记入 coverSlots。</summary>
	/// <param name="center">查询中心</param>
	/// <param name="queryRadius">查询半径</param>
	/// <param name="facing">受敌方向（零向量 = 不限方向）</param>
	/// <param name="maxDistFromCenter">掩体点距中心的最大距离（超出不算）</param>
	private static void AssignCovers(Vector3 center, float queryRadius, Vector3 facing, float maxDistFromCenter)
	{
		FillFootInfantry(infBuf);
		if (infBuf.Count == 0) return;
		List<AiDestination> free = QueryCovers(center, queryRadius, facing);
		if (free.Count == 0) return;
		SortByDistToAnchor(infBuf, center);
		bool[] used = new bool[free.Count];
		foreach (Soldier s in infBuf)
		{
			Vector3 sp;
			try { sp = s.transform.position; } catch { continue; }
			int best = -1; float bestD = float.MaxValue;
			for (int i = 0; i < free.Count; i++)
			{
				if (used[i]) continue;
				Vector3 cp;
				try { cp = free[i].GetCoverPosition(); } catch { continue; }
				if (DistXz(cp, center) > maxDistFromCenter) continue;
				float dd = DistXz(cp, sp);
				if (dd < bestD) { bestD = dd; best = i; }
			}
			if (best < 0) continue;
			used[best] = true;
			Vector3 pos;
			try { pos = free[best].GetCoverPosition(); } catch { continue; }
			SoldierPose pose = SoldierPose.Idle;
			try { pose = free[best].GetCoverPose(); } catch { }
			coverSlots.Add(new CoverSlot { unit = s, pos = pos, pose = pose });
			try { coveredPtrs.Add((long)s.Pointer); } catch { }
		}
	}

	/// <summary>1.2.3：右键点击建筑 → 选中步兵进入并找掩体防守（用户要求）。
	/// 与阵型共用掩体分配管线：原生掩体点查询 + moveTo + 到位 setPose。</summary>
	internal static int AssaultCovers(Vector3 center, float queryRadius)
	{
		coverSlots.Clear();
		coveredPtrs.Clear();
		try
		{
			AssignCovers(center, queryRadius, Vector3.zero, queryRadius);
			if (coverSlots.Count == 0) return 0;
			GodViewController.PrepareNewOrder("进入建筑");
			int n = 0;
			float now = Time.unscaledTime;
			foreach (CoverSlot cs in coverSlots)
			{
				try
				{
					if (cs.unit == null || !cs.unit.IsAlive) continue;
					if (GodViewController.MoveUnits(new List<Soldier> { cs.unit }, cs.pos) > 0)
					{
						n++;
						RemoveCoverArrival(cs.unit);
						pendingCoverArrivals.Add(new CoverArrival { unit = cs.unit, pos = cs.pos, pose = cs.pose, deadline = now + CoverArriveTimeout });
					}
				}
				catch { }
			}
			if (n > 0)
			{
				GodViewController.GetSelectedInfantryInto(infBuf);
				GodViewController.RegisterMoveObservation(center, infBuf, routeOnly: true);
				GodViewController.NoteFormationTarget(center);
				GodViewController.Flash(string.Format(Ui.Tr("进入建筑 → {0} 人进掩体防守"), n));
				SquadCmdLogic.LogAlways("[CoverAssault] 建筑进掩体 center=" + center.ToString("0.0") + " 人数=" + n);
			}
			return n;
		}
		catch (Exception ex) { SquadCmdLogic.Log("[CoverAssault] 失败: " + ex.Message); return 0; }
		finally
		{
			coverSlots.Clear();
			coveredPtrs.Clear();
		}
	}

	/// <summary>查询中心附近可用掩体（原生 CoverManager 八叉树）。facing 为零向量时不限受敌方向。</summary>
	private static List<AiDestination> QueryCovers(Vector3 center, float radius, Vector3 facing)
	{
		List<AiDestination> res = new List<AiDestination>();
		string fac = GodViewController.MySideFaction();
		if (string.IsNullOrEmpty(fac)) return res;
		bool hasDir = facing.sqrMagnitude > 0.01f;
		if (hasDir) QueryCoversOnce(center, fac, facing, radius, res);
		if (res.Count == 0) QueryCoversOnce(center, fac, Vector3.zero, radius, res);
		for (int i = res.Count - 1; i >= 0; i--)
		{
			AiDestination dd = res[i];
			bool keep = true;
			try
			{
				if (dd.IsCoverDestroyed() || dd.IsCoverOccupied(fac) || dd.IsVehicle()) keep = false;
				else if (hasDir && !dd.IsCoverAvailable(facing, fac)) keep = false;
			}
			catch { keep = false; }
			if (!keep) res.RemoveAt(i);
		}
		return res;
	}

	private static void QueryCoversOnce(Vector3 center, string fac, Vector3 dir, float radius, List<AiDestination> sink)
	{
		try
		{
			Il2CppSystem.Collections.IEnumerable en = CoverManager.GetCovers(center, radius, fac, dir, false);
			Il2CppSystem.Collections.IEnumerator e = en.GetEnumerator();
			while (true)
			{
				bool next;
				try { next = e.MoveNext(); } catch { break; }
				if (!next) break;
				if (sink.Count >= MaxCoverResults) break; // 枚举上限，防大拖动卡顿
				AiDestination d = null;
				try { d = (e.Current as Il2CppSystem.Object)?.TryCast<AiDestination>(); } catch { }
				if (d != null) sink.Add(d);
			}
		}
		catch (Exception ex) { SquadCmdLogic.Log("[Formation] GetCovers 失败: " + ex.Message); }
	}

	// ===== 布局（每帧，纯数学无分配）=====

	/// <summary>阵型线排布：中心=锚点、方向=垂直于箭头、单排总宽=max(lineLen,(n-1)×间距)；
	/// 排不下自动第二排（向箭头反方向错 RankGap）。成员按当前横向投影排序，减少交叉走位。</summary>
	private static void BuildLayout()
	{
		lineSlots.Clear();
		vehSlots.Clear();
		facingOnly.Clear();
		FillFootInfantry(infBuf);
		// 无掩体的步兵
		lineBuf.Clear();
		foreach (Soldier s in infBuf)
		{
			long k;
			try { k = (long)s.Pointer; } catch { continue; }
			if (!coveredPtrs.Contains(k)) lineBuf.Add(s);
		}
		GodViewController.GetVehicleRefsInto(vehBuf);
		// 1.2.3：不可移动的火力点/火炮（无 AIVehicle）不进阵型线槽——它们只转向（用户要求）
		// 1.2.7：不可移动的火力点/火炮/拖车不进阵型线槽（只转向）；判定改用是否履带/轮式/飞机
		vehBuf.RemoveAll(v => { bool mobile = IsMobileVehicle(v); if (!mobile && v != null) facingOnly.Add(v); return !mobile; });
		BuildLine(lineBuf, null, InfSpacing);
		BuildLine(null, vehBuf, VehSpacing);
	}

	/// <summary>1.2.3：该载具能否被驾驶（有 AIVehicle 才能走原生订单链）。无 AIVehicle = 火力点/火炮等固定物。</summary>
	internal static bool CanDrive(Vehicle v)
	{
		try
		{
			if (v == null || v.transform == null) return false;
			AIVehicle ai = v.GetComponent<AIVehicle>();
			if (ai == null) ai = v.GetComponentInChildren<AIVehicle>();
			return ai != null;
		}
		catch { return false; }
	}

	private static void BuildLine(List<Soldier> units, List<Vehicle> vehicles, float spacing)
	{
		bool forVeh = vehicles != null;
		int n = forVeh ? vehicles.Count : units.Count;
		if (n == 0) return;

		// 横向投影排序（减少交叉；插入排序，无 lambda 分配）
		if (forVeh) SortVehiclesByProj(vehicles);
		else SortSoldiersByProj(units);

		float usable = Mathf.Max(lineLen, spacing);
		int perRank = Mathf.Clamp(Mathf.FloorToInt(usable / spacing) + 1, 1, n);
		for (int i = 0; i < n; i++)
		{
			int rank = i / perRank;
			int idx = i % perRank;
			int inRank = Mathf.Min(perRank, n - rank * perRank);
			float t = inRank == 1 ? 0.5f : (float)idx / (inRank - 1);
			float off = (t - 0.5f) * usable;
			Vector3 pos = anchor + perpDir * off - facingDir * (rank * RankGap);
			if (forVeh) vehSlots.Add(new LineSlot { veh = vehicles[i], pos = pos, rank = rank });
			else lineSlots.Add(new LineSlot { unit = units[i], pos = pos, rank = rank });
		}
	}

	// ===== 下发（松开右键） =====

	internal static void IssueFromDrag()
	{
		if (!dragging) return;
		dragging = false;
		if (!hasAnchor) return;
		if (facingOnlyMode) { IssueFacingOnly(); return; }
		try
		{
			if (!hasEnd || lineLen < MinArrowLen)
			{
				// 拖动太短 → 等效普通移动到锚点
				end = anchor;
				hasEnd = false;
			}
			RecomputeAxes();
			RebuildCoverAssignment();
			BuildLayout();
			GhostPreview.ClearAll();

			LogDragCamera("下发");
			GodViewController.PrepareNewOrder("阵型");

			int coverN = 0, lineN = 0, vehN = 0;
			float now = Time.unscaledTime;
			// 1.2.1：掩体单位改走 moveTo + 到位后 setPose（原生 findCover 实测不下移动令）
			foreach (CoverSlot cs in coverSlots)
			{
				try
				{
					if (cs.unit == null || !cs.unit.IsAlive) continue;
					if (GodViewController.MoveUnits(new List<Soldier> { cs.unit }, cs.pos) > 0)
					{
						coverN++;
						RemoveCoverArrival(cs.unit);
						pendingCoverArrivals.Add(new CoverArrival { unit = cs.unit, pos = cs.pos, pose = cs.pose, deadline = now + CoverArriveTimeout });
					}
					if (Plugin.debugLog.Value)
						SquadCmdLogic.Log("[CoverOrder] " + GodViewController.SafeName(cs.unit) + " → " + cs.pos.ToString("0.0")
							+ " pose=" + cs.pose + " via=moveTo");
				}
				catch { }
			}
			foreach (LineSlot ls in lineSlots)
			{
				try
				{
					if (ls.unit == null || !ls.unit.IsAlive) continue;
					if (GodViewController.MoveUnits(new List<Soldier> { ls.unit }, ls.pos) > 0) lineN++;
				}
				catch { }
			}
			foreach (LineSlot ls in vehSlots)
			{
				try
				{
					Vehicle v = ls.veh;
					if (v == null || v.transform == null) continue;
					VehicleFacing.CancelFor(v);
					if (GodViewController.DriveVehicleTo(v, ls.pos) > 0)
					{
						vehN++;
						pendingFacings.RemoveAll(pf => { try { return pf.veh != null && (long)pf.veh.Pointer == (long)v.Pointer; } catch { return false; } });
						pendingFacings.Add(new PendingFacing { veh = v, slot = ls.pos, facePoint = anchor + facingDir * 60f, dir = facingDir, deadline = now + FacingTimeout });
					}
				}
				catch { }
			}
			// 1.2.3：不可移动的火力点/火炮 → 原地按箭头方向转向（用户要求：这些单位只转向）
			int faceN = 0;
			if (facingOnly.Count > 0)
			{
				List<Vehicle> fl = new List<Vehicle>(facingOnly);
				faceN = VehicleFacing.IssueFacing(fl, anchor + facingDir * 60f);
			}

			GodViewController.GetSelectedInfantryInto(infBuf);
			if (coverN + lineN + vehN + faceN > 0)
			{
				GodViewController.RegisterMoveObservation(anchor, infBuf, routeOnly: true); // 只画路线+统计，不加行军停火（进掩体需要自由行为）
				GodViewController.NoteFormationTarget(anchor);
				GodViewController.Flash(string.Format(Ui.Tr("阵型 → 掩体 {0} + 排开 {1} + 载具 {2} + 转向 {3}"), coverN, lineN, vehN, faceN));
				SquadCmdLogic.LogAlways("[Formation] 下发 anchor=" + anchor.ToString("0.0") + " lineLen=" + lineLen.ToString("0.0")
					+ " 掩体=" + coverN + " 排开=" + lineN + " 载具=" + vehN + " 转向=" + faceN + " facing=" + facingDir.ToString("0.0"));
			}
			else
			{
				GodViewController.Flash(Ui.Tr("阵型：无可用单位"));
			}
		}
		catch (Exception ex) { SquadCmdLogic.Log("[Formation] 下发失败: " + ex.Message); }
		finally
		{
			hasAnchor = false;
			hasEnd = false;
			facingOnlyMode = false;
			coverSlots.Clear(); lineSlots.Clear(); vehSlots.Clear(); coveredPtrs.Clear(); facingOnly.Clear();
		}
	}

	/// <summary>1.2.4：仅固定火力点/火炮的拖动收尾——按"单位 → 鼠标落点"方向原地转向。</summary>
	private static void IssueFacingOnly()
	{
		try
		{
			if (facingOnly.Count == 0) return;
			RecomputeAxes();
			if (!hasEnd || lineLen < MinArrowLen)
			{
				GodViewController.Flash(Ui.Tr("拖动太短（拉出箭头指定朝向）"));
				return;
			}
			int n = VehicleFacing.IssueFacing(new List<Vehicle>(facingOnly), end);
			if (n > 0)
			{
				GodViewController.NoteFormationTarget(anchor);
				GodViewController.Flash(string.Format(Ui.Tr("火力点转向 → {0} 座"), n));
				SquadCmdLogic.LogAlways("[Formation] 火力点朝向 数量=" + n + " dir=" + facingDir.ToString("0.0"));
			}
			else GodViewController.Flash(Ui.Tr("无可转向的火力点（需车上有乘员）"));
		}
		catch (Exception ex) { SquadCmdLogic.Log("[Formation] 火力点朝向失败: " + ex.Message); }
		finally
		{
			hasAnchor = false;
			hasEnd = false;
			facingOnlyMode = false;
			facingOnly.Clear();
		}
	}

	private static void RemoveCoverArrival(Soldier s)	{
		try
		{
			long k = (long)s.Pointer;
			pendingCoverArrivals.RemoveAll(ca => { try { return ca.unit != null && (long)ca.unit.Pointer == k; } catch { return false; } });
		}
		catch { }
	}

	/// <summary>1.2.1：掩体单位到位（距掩体点 ≤1.8m）后按掩体建议姿态 setPose（蹲/趴计入还原名单）；超时放弃。</summary>
	internal static void TickCoverArrivals()
	{
		if (pendingCoverArrivals.Count == 0) return;
		float now = Time.unscaledTime;
		for (int i = pendingCoverArrivals.Count - 1; i >= 0; i--)
		{
			CoverArrival ca = pendingCoverArrivals[i];
			bool remove = false;
			try
			{
				if (ca.unit == null || !ca.unit.IsAlive || ca.unit.transform == null) remove = true;
				else if (now > ca.deadline) { remove = true; SquadCmdLogic.Log("[CoverOrder] 到位超时，放弃姿态: " + GodViewController.SafeName(ca.unit)); }
				else if (DistXz(ca.unit.transform.position, ca.pos) <= CoverArriveDist)
				{
					if (ca.pose != SoldierPose.Idle)
					{
						try
						{
							new Lua_Soldier(ca.unit).setPose((int)ca.pose);
							GodViewController.AddPoseLocked(ca.unit); // 蹲/趴持久化——退出 RTS/接管时还原
						}
						catch { }
					}
					SquadCmdLogic.LogAlways("[CoverOrder] 到位进掩体 " + GodViewController.SafeName(ca.unit) + " pose=" + ca.pose);
					remove = true;
				}
			}
			catch { remove = true; }
			if (remove) pendingCoverArrivals.RemoveAt(i);
		}
	}

	/// <summary>载具到位转向补发（持久段调用）：距槽位 ≤7m 时按箭头方向直驱转向；45s 未到位放弃。</summary>
	internal static void TickPendingFacings()
	{
		if (pendingFacings.Count == 0) return;
		float now = Time.unscaledTime;
		for (int i = pendingFacings.Count - 1; i >= 0; i--)
		{
			PendingFacing pf = pendingFacings[i];
			bool remove = false;
			try
			{
				if (pf.veh == null || pf.veh.transform == null) { remove = true; }
				else if (now > pf.deadline) { remove = true; SquadCmdLogic.Log("[Formation] 载具转向放弃（超时未到位）: " + GodViewController.SafeName(pf.veh)); }
				else if (DistXz(pf.veh.transform.position, pf.slot) <= FacingArriveDist)
				{
					// 1.2.4（用户要求）：**轮式车不原地旋转**——履带车原地转向，轮式改为
					// "沿期望朝向再开一小段"（靠行驶自然摆正），避免"目标点太近就原地打转"。
					if (IsTracked(pf.veh))
					{
						VehicleFacing.IssueFacing(new List<Vehicle> { pf.veh }, pf.facePoint);
						SquadCmdLogic.LogAlways("[Formation] 履带车到位转向 " + GodViewController.SafeName(pf.veh));
					}
					else
					{
						Vector3 roll = pf.slot + pf.dir * WheeledRollDist;
						GodViewController.DriveVehicleTo(pf.veh, roll);
						SquadCmdLogic.LogAlways("[Formation] 轮式车到位续驶摆正 " + GodViewController.SafeName(pf.veh)
							+ " → " + roll.ToString("0.0"));
					}
					remove = true;
				}
			}
			catch { remove = true; }
			if (remove) pendingFacings.RemoveAt(i);
		}
	}

	/// <summary>
	/// 1.2.15：**中断拖动并清理幽灵**。任何"手势没走到 IssueFromDrag 就结束"的路径都必须调它——
	/// 此前只把 GodViewController 的 formationDragActive 置 false，Formation.dragging 仍为 true，
	/// 幽灵既不销毁也不停止刷新 → **取消长按后幽灵残留在场上**（用户反馈）。
	/// </summary>
	internal static void CancelDrag(string reason)
	{
		LogDragCamera("取消:" + reason);
		if (!dragging && !hasAnchor) { GhostPreview.ClearAll(); return; }
		dragging = false;
		hasAnchor = false;
		hasEnd = false;
		facingOnlyMode = false;
		coverSlots.Clear(); lineSlots.Clear(); vehSlots.Clear(); coveredPtrs.Clear(); facingOnly.Clear();
		GhostPreview.ClearAll();
		SquadCmdLogic.Log("[Formation] 拖动取消（" + reason + "）");
	}

	/// <summary>1.2.16：拖动开始/结束时记录相机状态（无条件、每次拖动仅两行）。
	/// 用于判定"镜头乱飞"到底是**相机被移动**还是**标记/箭头在跳**——前者 now/want 会明显不同。</summary>
	internal static void LogDragCamera(string tag)
	{
		try
		{
			Camera cam = GodViewController.MainCam();
			Vector3 want = GodViewController.CamPosDiag;
			string now = cam != null ? cam.transform.position.ToString("0.0") : "null";
			string rot = cam != null ? cam.transform.eulerAngles.ToString("0") : "null";
			float d = cam != null ? (cam.transform.position - want).magnitude : -1f;
			SquadCmdLogic.LogAlways("[DragCam] " + tag + " cam=" + now + " want=" + want.ToString("0.0")
				+ " 偏差=" + d.ToString("0.00") + " rot=" + rot
				+ " fov=" + (cam != null ? cam.fieldOfView.ToString("0") : "?")
				+ " 选中=" + GodViewController.SelInfantryCountPublic());
		}
		catch { }
	}

	/// <summary>任意新命令（含非阵型）覆盖旧任务时清空待收尾队列（驻守记录保留，除非单位死亡）。</summary>
	internal static void OnNewOrder()
	{
		pendingFacings.Clear();
		pendingCoverArrivals.Clear();
	}




	/// <summary>退出 RTS / 选择失效时清理拖动态、预览与待转向任务。</summary>
	internal static void OnRtsExit()
	{
		dragging = false;
		hasAnchor = false;
		hasEnd = false;
		coverSlots.Clear(); lineSlots.Clear(); vehSlots.Clear(); coveredPtrs.Clear(); facingOnly.Clear();
		OnNewOrder();
		GhostPreview.ClearAll();
	}

	// ===== 拖动中的世界标记（SceneMarkersFrame 在 EndFrame 前调用） =====
	internal static void DrawDragMarkers()
	{
		if (!dragging || !hasAnchor) return;
		Color ac = new Color(1f, 0.9f, 0.4f, 0.9f);
		try
		{
			if (hasEnd && lineLen > 0.3f)
			{
				SceneMarkers.Arrow("FMA", anchor + Vector3.up * 0.3f, end + Vector3.up * 0.3f, ac, 0.12f, true);
				SceneMarkers.Line("FML", anchor + perpDir * (lineLen * 0.5f) + Vector3.up * 0.25f,
					anchor - perpDir * (lineLen * 0.5f) + Vector3.up * 0.25f, new Color(1f, 0.9f, 0.4f, 0.55f), 0.06f, true);
			}
			else
			{
				SceneMarkers.Dot("FMAD", anchor + Vector3.up * 0.15f, 0.45f, ac, true);
			}
		}
		catch { }
		int i = 0;
		foreach (LineSlot ls in lineSlots)
		{
			try { SceneMarkers.Dot("FMS" + i, ls.pos + Vector3.up * 0.1f, 0.2f, ac, true); } catch { }
			i++;
		}
		i = 0;
		foreach (LineSlot ls in vehSlots)
		{
			try { SceneMarkers.Bracket("FMV" + i, ls.pos + Vector3.up * 0.15f, 3.2f, ac, 0.08f, true); } catch { }
			i++;
		}
		// 1.2.3：只转向的火力点/火炮——在自身位置画角括号（箭头已表示方向）
		i = 0;
		foreach (Vehicle v in facingOnly)
		{
			try { if (v != null && v.transform != null) SceneMarkers.Bracket("FMF" + i, v.transform.position + Vector3.up * 0.15f, 2.2f, new Color(0.6f, 0.9f, 1f, 0.85f), 0.08f, true); } catch { }
			i++;
		}
	}

	// ===== 工具 =====

	private static void FillFootInfantry(List<Soldier> dst)
	{
		dst.Clear();
		GodViewController.GetSelectedInfantryInto(dst);
		for (int i = dst.Count - 1; i >= 0; i--)
		{
			Soldier s = dst[i];
			try
			{
				if (s == null || !s.IsAlive || s.transform == null) { dst.RemoveAt(i); continue; }
				if (s.GetComponentInParent<Vehicle>() != null) { dst.RemoveAt(i); continue; }
			}
			catch { dst.RemoveAt(i); }
		}
	}

	private static float DistXz(Vector3 a, Vector3 b)
	{
		float dx = a.x - b.x, dz = a.z - b.z;
		return Mathf.Sqrt(dx * dx + dz * dz);
	}

	private static float DistXz(Soldier s, Vector3 b)
	{
		try { return DistXz(s.transform.position, b); }
		catch { return float.MaxValue; }
	}

	// —— 无分配的插入排序（≤30 元素，O(n²) 可忽略）——

	private static void SortByDistToAnchor(List<Soldier> units, Vector3 center)
	{
		for (int i = 1; i < units.Count; i++)
		{
			Soldier key = units[i];
			float kd = DistXz(key, center);
			int j = i - 1;
			while (j >= 0 && DistXz(units[j], center) > kd) { units[j + 1] = units[j]; j--; }
			units[j + 1] = key;
		}
	}

	private static void SortSoldiersByProj(List<Soldier> units)
	{
		for (int i = 1; i < units.Count; i++)
		{
			Soldier key = units[i];
			float kp = ProjOf(key);
			int j = i - 1;
			while (j >= 0 && ProjOf(units[j]) > kp) { units[j + 1] = units[j]; j--; }
			units[j + 1] = key;
		}
	}

	private static void SortVehiclesByProj(List<Vehicle> vehs)
	{
		for (int i = 1; i < vehs.Count; i++)
		{
			Vehicle key = vehs[i];
			float kp = ProjOf(key);
			int j = i - 1;
			while (j >= 0 && ProjOf(vehs[j]) > kp) { vehs[j + 1] = vehs[j]; j--; }
			vehs[j + 1] = key;
		}
	}

	private static float ProjOf(Soldier s)
	{
		try { return Vector3.Dot(s.transform.position - anchor, perpDir); } catch { return 0f; }
	}

	private static float ProjOf(Vehicle v)
	{
		try { return Vector3.Dot(v.transform.position - anchor, perpDir); } catch { return 0f; }
	}
}
