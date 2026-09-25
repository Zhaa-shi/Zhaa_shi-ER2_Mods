using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 上帝视角 v0.7.93（RTS 指挥，RTS/FPS 共存版）。
///   左键：单击选友军（临时指挥，不建队）/拖框/双击选队/空白清选/Shift 追加
///   右键短按：空白=移动(M7)｜敌军=集火标记（只改目标，不移动）｜友军载具/车内兵=交互环｜徒步友军=视为地面移动（合并已上移顶栏）；双击=原生前往并防守(HoldArea)
///   右键长按 0.35s + 拖动 = 阵型箭头（Formation.cs，地狱之门式：垂直排开/掩体散开/载具到位转向；1.2.0 上位替代命令环与朝向拖动）
///   命令快捷键（cfg Hotkeys 可自定义）：Z/X/C 站/蹲/趴  V 停止  B 停火  N 就近掩体  M 集合  F 分散
///   左下角信息面板 + 只读装备/背包（InfoPanel.cs）
///   交互环：上车（成功后转选车组）/下车/修理（原生 OrderRepairVehicle）；合并只保留顶栏入口（0.7.96 移出轮盘）
///   顶栏按钮：控制该小队 / 分队（显式新建组）/ 合并（并入当前激活 RTS 组）
///   空格暂停；F9 进入/紧急退出；顶部按钮接管（保护窗+独苗转移）
///   退出 RTS 不清除已下达的移动、登车、车辆同步和集火任务；FPS 原生输入照常运行
///   标记=持久集火（GetBestVisibleEnemy/CurrentVisibleTarget Postfix + LOS 缓存）；0.9.17 起不再自动冲锋推进——想去哪用移动/前往并防守下令
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
	// mainSquad = 当前指挥的显式 RTS 分队；框选本身只是临时选择
	private static Squad mainSquad;
	private static readonly List<Squad> selVehicles = new List<Squad>();
	// 0.7.43：被点选 Vehicle 的真实引用（A/B 隔离核心）——光标/下车/标签以它为准
	private static readonly List<Vehicle> selVehicleRefs = new List<Vehicle>();
	// 0.7.46：分队后创建的当前 RTS 分队（0.7.56 起降级为"激活组"标记，不参与驾驶判定）
	private static Squad rtsSquad;
	// 0.7.56 P0：全部 RTS 分队指针集合——车辆驾驶判定改用集合成员关系（多组共存不互踩）
	private static readonly HashSet<long> rtsSquadSet = new HashSet<long>();
	// RTS 视角退出后仍然有效的集火指挥对象快照。当前选择是 UI 状态，
	// 不能作为持续任务的依据；否则切回 FPS 后集火会立即失效。
	private static readonly List<Soldier> persistentMarkUnits = new List<Soldier>();
	private static readonly List<Vehicle> persistentMarkVehicles = new List<Vehicle>();

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
	private static string cmdFlash = Ui.Tr("");
	private static float cmdFlashUntil = -10f;
	private static bool flyingToSquad;
	private static Vector3 flyTarget;
	private static float flyStartTime;
	private static Vector3 flyStartPos;

	// ===== 选择状态（重写核心） =====

	// 右键长按手势状态（长按=常驻命令环，短按=直接指令）
	private const float RightLongPressSeconds = 0.35f;
	private static bool rightHoldActive;
	private static bool rightLongPressOpened;
	private static bool rightGestureWheelOpen;
	private static float rightDownTime = -10f;
	private static Vector2 rightDownScreenPos;
	private static bool rightDownOnUnit; // 按下点命中单位时不开姿态环（松开一律走短按指令）
	private static float lastRightBlankClickTime = -10f;
	private static bool formationDragActive; // 1.2.0：阵型箭头拖动进行中（长按右键拖出，松开下发阵型/掩体/载具朝向）
	// 关环后吞掉同一次左键手势的余下部分（1.2.3：指令环已删，仅保留状态字段供手势链复用）
	private static bool swallowLeftGesture;
	// ◆ 标记绘制缓存（OnGUI 每帧多 pass）
	private static List<Soldier> markerCache = new List<Soldier>();
	private static float markerCacheUntil = -10f;
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
	private static float markLostSightSince = -1f;
	private const float MarkLoseSightGraceSeconds = 6f;
	private static float pruneMarkNext;
	private static int markInteropFailures;
	private const int MarkInteropFailureLimit = 3;
	/// <summary>LOS/范围缓存（0.5s 刷新）：GetBestVisibleEnemy Postfix 用它替代每兵每帧射线。</summary>
	internal static bool MarkLosCached = true;

	/// <summary>1.4.14 性能：PruneMark 的存活单位暂存（复用，避免每 0.5s 一次 List 分配）。</summary>
	private static readonly List<Soldier> pruneMarkBuf = new List<Soldier>();

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
			// 1.4.14 性能：复用缓冲区（原每次 new List<Soldier>()，0.5s 一次也算白扔的 GC）
			List<Soldier> uu = pruneMarkBuf;
			uu.Clear();
			for (int i = persistentMarkUnits.Count - 1; i >= 0; i--)
			{
				Soldier u = persistentMarkUnits[i];
				try
				{
					if (u == null || !u.IsAlive || u.transform == null) { persistentMarkUnits.RemoveAt(i); continue; }
					uu.Add(u);
				}
				catch { persistentMarkUnits.RemoveAt(i); }
			}
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
	private static string SquadCmdLogVia; // 移动令下达通道（LuaSquad/Fallback），仅用于最终移动日志合并为一条
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
	// 1.2.10：控制权登记节流（原每帧调 GetComponentsInChildren，见维护段注释）
	private static float ctrlSyncNext = -10f;
	private static readonly List<Soldier> ctrlSyncBuf = new List<Soldier>();
	private static readonly List<Squad> ctrlSyncSquads = new List<Squad>();
	// 定期清理
	private static float cleanupTimer = -10f;
	// 移动 pending（同步窗口重试）
	private static Vehicle pendVeh;
	private static Vector3 pendPoint;
	private static float pendUntil;
	private static float pendNextRetry;

	// 1.2.4：HasSelection 仍只算"单位"——顶栏按钮（控制/分队/合并）只对单位有意义；
	// 可交互物选择（propVehicle/propItem）走 InfoPanel 单独显示。
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
		ClearPropSelection(); // 1.2.4：可交互物选择与单位选择互斥
		BackpackPanel.CloseAll(); // 1.4.1（用户定案）：取消/更换选择 → 背包窗口随选关闭（CloseAll 幂等）
		// 1.4.9：**不杀在途的派兵会合任务**——兵还在走过去，选择变了不该让任务消失（1.4.8「走到贴身也不开窗」的嫌疑链）
	}

	/// <summary>当前选中的步兵（身上 ◆ 光标；只移动它们 = 未选不动）。</summary>
	internal static List<Soldier> GetSelectedInfantry()
	{
		List<Soldier> list = new List<Soldier>();
		GetSelectedInfantryInto(list);
		return list;
	}

	/// <summary>1.2.1：填充到调用方提供的列表（零分配版本，供每帧调用的 Formation/InfoPanel 使用）。</summary>
	internal static void GetSelectedInfantryInto(List<Soldier> list)
	{
		if (list == null) return;
		list.Clear();
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

	/// <summary>1.4.9：徒步判定（公开版）。**车内乘员不是徒步单位**——对他们下发 moveTo / allowMovements(true)
	/// 不会"走过去"，原生 AI 的反应是**下车步行**（用户实测「让单位上坦克后又会立刻下车」的真凶）。
	/// 所有"走过去/发移动/派兵拾取"的链路都必须先过这一关。</summary>
	internal static bool IsOnFoot(Soldier s)
	{
		if (s == null) return false;
		try { if (s.GetComponentInParent<Vehicle>() != null) return false; } catch { }
		try { if (new Lua_Soldier(s).isInsideVehicle()) return false; } catch { }
		return true;
	}

	private static bool IsInfantry(Soldier s)
	{
		return IsOnFoot(s);
	}

	/// <summary>1.4.9：把选中集拆成「可步行的徒步单位」。返回被排除的车内乘员数。</summary>
	internal static List<Soldier> FilterOnFoot(List<Soldier> src, out int embarked)
	{
		List<Soldier> res = new List<Soldier>();
		embarked = 0;
		if (src == null) return res;
		for (int i = 0; i < src.Count; i++)
		{
			Soldier s = src[i];
			if (s == null) continue;
			bool onFoot = IsOnFoot(s);
			if (onFoot) res.Add(s); else embarked++;
		}
		return res;
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

	/// <summary>移动兜底：只对选中步兵逐单位 moveTo。普通 RTS 指令先尝试原生 Squad 订单。
	/// 1.4.9：**跳过车内乘员**——对车里的兵 allowMovements(true)+moveTo 等于命令他下车步行
	/// （用户实测「让单位上坦克后又会立刻下车」）。车辆移动走 selVehicleRefs → DriveVehicleTo。</summary>
	internal static int MoveUnits(List<Soldier> units, Vector3 point)
	{
		int n = 0;
		if (!Active || units == null) return 0;
		foreach (Soldier s in units)
		{
			if (s == null || !s.IsAlive) continue;
			if (!IsOnFoot(s)) continue; // 1.4.9：车内乘员不接步行指令
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

	/// <summary>让载具/火力点车组开到目标点（Squad 原生订单链）。1.2.0：改 internal 供 Formation 阵型下发。</summary>
	internal static int DriveVehicleTo(Vehicle vehRef, Vector3 point)
	{
		if (!Active) return 0;
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
				cmdFlash = Ui.Tr("车辆未分队：先点【分队】再移动"); cmdFlashUntil = Time.unscaledTime + 2.5f;
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
			if (SquadCmdLogic.TryIssueNativeMove(tgt, point, Plugin.radius.Value))
			{
				SquadCmdLogic.Log("[VehicleMove] vehicle=" + vehRef.name + " squadInside=0x" + ((long)tgt.Pointer).ToString("X")
					+ " target=" + point.ToString("0.0") + " via=LuaSquad");
				RegisterVehicleObservation(vehRef, point);
				return 1;
			}
			// 仅限 RTS 且原生调用失败时保留旧 Lua_Squad 兜底，避免车辆完全失去指令能力。
			new Lua_Squad(tgt).moveTo(point, Plugin.radius.Value);
			SquadCmdLogic.Log("[VehicleMove] vehicle=" + vehRef.name + " squadInside=0x" + ((long)tgt.Pointer).ToString("X")
				+ " target=" + point.ToString("0.0") + " via=Fallback");
			RegisterVehicleObservation(vehRef, point);
			return 1;
		}
		catch (Exception ex) { SquadCmdLogic.Log("[VehicleMove] 载具移动失败: " + ex.Message); return 0; }
	}

	internal static void ClearMark()
	{
		mark = null;
		persistentMarkUnits.Clear();
		persistentMarkVehicles.Clear();
		MarkLosCached = true;
		markLostSightSince = -1f;
	}

	private static void MarkEnemySoldier(Soldier target)
	{
		if (target == null) return;
		Spottable spot = GetSpottable(target);
		if (spot == null)
		{
			cmdFlash = Ui.Tr("标记失败：目标无 Spottable"); cmdFlashUntil = Time.unscaledTime + 2f;
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
		BeginMarkFocus();
		cmdFlash = string.Format(Ui.Tr("集火标记 → {0}（持续到死亡/失控）"), mark.Name); cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.LogAlways("[SquadCmd] 集火标记(持久) " + mark.Name + " 阵营=" + fac);
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
			cmdFlash = Ui.Tr("标记失败：载具无目标"); cmdFlashUntil = Time.unscaledTime + 2f;
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
		BeginMarkFocus();
		cmdFlash = string.Format(Ui.Tr("集火标记 → {0}（持续到死亡/失控）"), mark.Name); cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.LogAlways("[SquadCmd] 集火标记(持久) 载具 " + veh.name);
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
		BeginMarkFocus();
		cmdFlash = string.Format(Ui.Tr("标记目标物 → {0}（持续到失效）"), mark.Name); cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.Log("[SquadCmd] 标记目标物 " + mark.Name);
	}

	internal static string SafeName(Soldier s)
	{
		try { string n = s.name_surname; return string.IsNullOrEmpty(n) ? ("单位#" + s.GetInstanceID()) : n; } catch { return "单位"; }
	}

	internal static string SafeName(Vehicle v)
	{
		try { return string.IsNullOrEmpty(v.name) ? "未知载具" : v.name; } catch { return "未知载具"; }
	}

	private static string SafeFaction(string fac)
	{
		try { return string.IsNullOrEmpty(fac) ? "" : fac; } catch { return ""; }
	}

	private static void ClearFollow(string reason, bool logIt)
	{
		// 这里只清理本 Mod 的 RTS 状态；不向原生 Squad 发送取消/替换订单，
		// 也不触碰 FPS 模式下的原生 AI。新命令经此处覆盖旧的完成度观测。
		ClearMoveObservation();
		pendVeh = null;
		pendUntil = -10f;
		pendNextRetry = -10f;
		if (logIt) SquadCmdLogic.Log("[SquadCmd] 清理 RTS 状态：" + reason);
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

	// ===== 0.7.99：移动完成度观测（纯观察——只统计到位数，不发任何修正命令） =====
	// 0.8.00：补齐载具——DriveVehicleTo 成功发单后登记，到位半径放宽为 2×moveRadius
	private static Vector3 obsTarget;
	private static readonly List<Soldier> obsUnits = new List<Soldier>();
	private static readonly List<Vehicle> obsVehicles = new List<Vehicle>();
	// 0.9.11：行军禁索敌名单——移动命令优先于自动交火（到位/超时/改令即恢复）
	// 0.9.15：改用原生停火通道（Squad.SetHoldFireOrder + AiParams），并加 20s 有效期——
	// 0.9.12 的"GetBestVisibleEnemy 全盲"会引发任务系统异常（单位罚站/冻结）且拦不住任务级打断
	private static readonly List<Soldier> obsNoEngage = new List<Soldier>();
	private static readonly HashSet<long> obsNoEngageSquads = new HashSet<long>();
	private const float NoEngageMaxSeconds = 20f;
	private static float obsUntil = -10f;
	private static float obsNext = -10f;
	internal static int ObsArrived; // HUD 进度行用
	internal static int ObsTotal;

	// ===== 0.8.00：编组热键——Ctrl+1~9 保存当前选择，1~9 召回（RTS 内） =====
	private static readonly Dictionary<int, List<Soldier>> groupUnits = new Dictionary<int, List<Soldier>>();
	private static readonly Dictionary<int, List<Squad>> groupCrews = new Dictionary<int, List<Squad>>();
	private static readonly Dictionary<int, List<Vehicle>> groupVehRefs = new Dictionary<int, List<Vehicle>>();

	/// <summary>
	/// 0.9.17：标记=纯集火——只登记单位快照供 GetBestVisibleEnemy/CurrentVisibleTarget 覆盖引导射击，
	/// 不再下发 Squad.Charge 推进（想去哪用移动/前往并防守下令，标记只管"打谁"）。
	/// 保留 ClearMoveObservation：上一个移动令的行军停火必须立刻解除，否则被标记单位被停火锁住不打。
	/// </summary>
	private static void BeginMarkFocus()
	{
		ClearMoveObservation(); // 标记解除行军停火（若上个移动令还在），标记单位恢复开火
		persistentMarkUnits.Clear();
		persistentMarkVehicles.Clear();
		foreach (Soldier s in GetSelectedInfantry())
		{
			if (s == null || !s.IsAlive) continue;
			bool exists = false;
			foreach (Soldier old in persistentMarkUnits) { try { if (old != null && old.Pointer == s.Pointer) { exists = true; break; } } catch { } }
			if (!exists) persistentMarkUnits.Add(s);
		}
		foreach (Soldier s in SelectedVehicleOccupants())
		{
			if (s == null || !s.IsAlive) continue;
			bool exists = false;
			foreach (Soldier old in persistentMarkUnits) { try { if (old != null && old.Pointer == s.Pointer) { exists = true; break; } } catch { } }
			if (!exists) persistentMarkUnits.Add(s);
		}
		foreach (Vehicle v in selVehicleRefs)
		{
			if (v == null || v.transform == null) continue;
			bool exists = false;
			foreach (Vehicle old in persistentMarkVehicles) { try { if (old != null && old.Pointer == v.Pointer) { exists = true; break; } } catch { } }
			if (!exists) persistentMarkVehicles.Add(v);
		}
		foreach (Squad sq in selVehicles)
		{
			Vehicle v = VehicleOfCrew(sq);
			if (v == null || v.transform == null) continue;
			bool exists = false;
			foreach (Vehicle old in persistentMarkVehicles) { try { if (old != null && old.Pointer == v.Pointer) { exists = true; break; } } catch { } }
			if (!exists) persistentMarkVehicles.Add(v);
		}
		// 0.9.17：不再自动冲锋推进——标记只改目标选择（打谁），不动单位位置。
	}

	private static void RemovePersistentUnit(Soldier unit)
	{
		if (unit == null) return;
		try { persistentMarkUnits.RemoveAll(s => s == null || s.Pointer == unit.Pointer); } catch { }
		try { obsUnits.RemoveAll(s => s == null || s.Pointer == unit.Pointer); } catch { } // 接管单位不再计入移动完成度
		try
		{
			// 0.9.15：被接管单位若在行军停火名单中，恢复感知再移除
			if (obsNoEngage.RemoveAll(s => s == null || s.Pointer == unit.Pointer) > 0)
			{
				try
				{
					AiParams ap = new Lua_Soldier(unit).getAiParams();
					ap.allowCheckForEnemies(true);
					ap.allowFindCoverWhenSuppressed(true);
				}
				catch { }
			}
		}
		catch { }
		// 接管车内单位时，连同该单位所在的持久载具标记一起摘除，
		// 避免 FPS 接管后仍由本 Mod 改写玩家载具的目标。
		try
		{
			Vehicle parentVehicle = unit.GetComponentInParent<Vehicle>();
			if (parentVehicle != null)
			{
				long vp = (long)parentVehicle.Pointer;
				persistentMarkVehicles.RemoveAll(v => v == null || (long)v.Pointer == vp);
				obsVehicles.RemoveAll(v => v == null || (long)v.Pointer == vp); // 不再计入移动完成度
			}
		}
		catch { }
		try
		{
			if (persistentMarkUnits.Count == 0 && persistentMarkVehicles.Count == 0)
			{
				mark = null;
				MarkLosCached = true;
				markLostSightSince = -1f;
			}
		}
		catch { }
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
		// 载具车组是不可拆分的选择单元；显式分队只处理徒步步兵。
		// GetCommandUnits() 会展开选中载具的乘员，拿它分队会破坏车组归属。
		List<Soldier> units = GetSelectedInfantry();
		List<Squad> crews = GetSelectedVehicleCrews();
		// 0.8.2：混编队（步兵+坦克合成一队后）只选车 → 把车内乘员分进新小队，徒步步兵留原队
		if (units.Count == 0 && crews.Count > 0)
		{
			SplitCrewFromSquad(crews[0]);
			return;
		}
		if (units.Count == 0) { cmdFlash = Ui.Tr("先选中要分队的步兵"); cmdFlashUntil = Time.unscaledTime + 2f; return; }
		Squad ns = CreateNewSquad();
		if (ns == null) { cmdFlash = Ui.Tr("新建小队失败"); cmdFlashUntil = Time.unscaledTime + 2f; return; }
		int moved = 0;
		HashSet<IntPtr> oldSet = new HashSet<IntPtr>();
		foreach (Soldier u in units)
		{
			// 防御性检查：登车异步完成后，旧选择可能仍含车内乘员；绝不拆分车组。
			if (u == null || !u.IsAlive || !IsInfantry(u)) continue;
			try { Squad old = u.joinedSquad; if (old != null) oldSet.Add(old.Pointer); } catch { }
			if (AddInfantryToSquadTo(u, ns)) moved++;
		}
		if (moved == 0) { cmdFlash = Ui.Tr("分队失败：无可拆单位"); cmdFlashUntil = Time.unscaledTime + 2f; return; }
		rtsSquad = ns;
		rtsSquadSet.Add((long)ns.Pointer);
		RTSTrace("SplitAfter", "oldRtsSquad→newRtsSquad=0x" + ((long)ns.Pointer).ToString("X") + " selectedUnits=" + GetCommandUnits().Count);
		mainSquad = ns; virtualUnits.Clear(); // 分队后控制组=NewSquad（选择显示仍按 selVehicleRefs 车辆乘员）
		cmdFlash = string.Format(Ui.Tr("分队 → {0} 人入新队（原队 {1} 个，其余未动）"), moved, oldSet.Count);
		cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.LogAlways("[Split] 分队 入队=" + moved + " 原队数=" + oldSet.Count + " newSquad=0x" + ((long)ns.Pointer).ToString("X"));
		SplitCheck(ns);
	}

	/// <summary>
	/// 0.8.2：混编队拆分——把车内乘员分进新小队（squadInside 跟随改写），徒步步兵留在原队。
	/// 用于撤销 0.8.1 的"步兵并入车组"：选车点 [分队] 即可拆回两队。
	/// </summary>
	private static void SplitCrewFromSquad(Squad crewSquad)
	{
		Squad ns = CreateNewSquad();
		if (ns == null) { cmdFlash = Ui.Tr("新建小队失败"); cmdFlashUntil = Time.unscaledTime + 2f; return; }
		bool hasFoot = false;
		List<Vehicle> vehicles = new List<Vehicle>();
		int moved = 0;
		int count = 0;
		try { count = crewSquad.CountMembers; } catch { }
		for (int i = 0; i < count; i++)
		{
			Soldier m = null;
			try { m = crewSquad.GetMemberClamped(i); } catch { }
			if (m == null || !m.IsAlive) continue;
			Vehicle vIn = null;
			try { vIn = m.GetComponentInParent<Vehicle>(); } catch { }
			if (vIn == null) { hasFoot = true; continue; } // 徒步步兵留原队
			try { crewSquad.Leave(m, false); } catch { }
			try { ns.Join(m); moved++; } catch { }
			try { if (vIn.transform != null && !vehicles.Contains(vIn)) vehicles.Add(vIn); } catch { }
		}
		if (moved == 0 || !hasFoot)
		{
			cmdFlash = Ui.Tr("车组已是独立小队，无可拆分"); cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}
		// 车辆归属跟随：squadInside 改指新队，驾驶资格同步
		foreach (Vehicle v in vehicles)
		{
			try
			{
				AIVehicle ai = v.GetComponent<AIVehicle>();
				if (ai == null) ai = v.GetComponentInChildren<AIVehicle>();
				if (ai != null) ai.squadInside = ns;
			}
			catch { }
		}
		rtsSquad = ns;
		rtsSquadSet.Add((long)ns.Pointer);
		try { selVehicles.Remove(crewSquad); } catch { }
		try { if (!selVehicles.Contains(ns)) selVehicles.Add(ns); } catch { }
		foreach (Vehicle v in vehicles) AddVehicleRef(v);
		cmdFlash = string.Format(Ui.Tr("分队 → 车组 {0} 人入新队（徒步步兵留原队）"), moved);
		cmdFlashUntil = Time.unscaledTime + 3f;
		selFlash = Time.unscaledTime + 3f;
		SquadCmdLogic.LogAlways("[Split] 车组拆分 moved=" + moved + " vehicles=" + vehicles.Count + " newSquad=0x" + ((long)ns.Pointer).ToString("X"));
		SplitCheck(ns);
	}

	/// <summary>0.7.57：把当前选中 Soldier[] 并入按选中成员多数决定的目标小队。</summary>
	private static void MergeSelectedToRts()
	{
		// 0.9.14 合并语义：
		//  - 只选步兵：单队 → 并入当前激活 RTS 组（须为步兵队）；跨队 → 多数队吸收少数队
		//  - 选了载具：第一辆车的车组为合并目标——选中步兵与其余车组成员全部并入目标车组
		//    （上限 12，超员留下）；被吸收车组的车辆 squadInside 同步改写，合并后照常驾驶。
		List<Soldier> units = GetSelectedInfantry();
		List<Squad> crews = GetSelectedVehicleCrews();
		if (units.Count == 0 && crews.Count == 0)
		{
			cmdFlash = Ui.Tr("先选中要合并的步兵"); cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}
		int moved = 0, skip = 0, overflow = 0;
		Squad target = null; int best = 0;
		List<Vehicle> repointVehicles = new List<Vehicle>();
		if (crews.Count > 0)
		{
			// 车组合并：目标=第一辆车的车组；其余车组成员全部并入（坦克+坦克场景）
			target = crews[0];
			best = AliveCount(target);
			int cap = 12 - best;
			for (int ci = 1; ci < crews.Count; ci++)
			{
				Squad csq = crews[ci];
				if (csq == null) continue;
				int c = 0; try { c = csq.CountMembers; } catch { }
				for (int i = 0; i < c; i++)
				{
					Soldier m = null;
					try { m = csq.GetMemberClamped(i); } catch { }
					if (m == null || !m.IsAlive) continue;
					if (cap <= 0) { overflow++; continue; }
					Vehicle vIn = null;
					try { vIn = m.GetComponentInParent<Vehicle>(); } catch { }
					if (AddInfantryToSquadTo(m, target))
					{
						moved++; cap--;
						try { if (vIn != null && vIn.transform != null && !repointVehicles.Contains(vIn)) repointVehicles.Add(vIn); } catch { }
					}
				}
			}
		}
		else
		{
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
			foreach (var kv in cnt)
			{
				if (kv.Value > best) { best = kv.Value; target = tally[kv.Key]; }
			}
			if (cnt.Count == 1 && rtsSquad != null && target != null
				&& (long)rtsSquad.Pointer != (long)target.Pointer && AliveCount(rtsSquad) > 0
				&& VehicleOfCrew(rtsSquad) == null)
			{
				// 0.7.99：单队选择并入当前激活 RTS 组（多数语义下目标=自己=0 人合并）
				target = rtsSquad;
				best = AliveCount(target);
			}
		}
		if (target == null) { cmdFlash = Ui.Tr("选中单位没有所属小队"); cmdFlashUntil = Time.unscaledTime + 2f; return; }
		int cap2 = 12 - AliveCount(target);
		foreach (Soldier u in units)
		{
			if (u == null || !u.IsAlive || !IsInfantry(u)) continue;
			try
			{
				Squad cur = u.joinedSquad;
				if (cur != null && (long)cur.Pointer == (long)target.Pointer) { skip++; continue; }
				if (cap2 <= 0) { overflow++; continue; }
				if (AddInfantryToSquadTo(u, target)) { moved++; cap2--; }
			}
			catch { }
		}
		if (moved > 0)
		{
			rtsSquad = target;
			rtsSquadSet.Add((long)target.Pointer);
			// 被吸收车组的车辆 squadInside 同步改写（合并后照常驾驶）
			foreach (Vehicle v in repointVehicles)
			{
				try
				{
					AIVehicle ai = v.GetComponent<AIVehicle>();
					if (ai == null) ai = v.GetComponentInChildren<AIVehicle>();
					if (ai != null) ai.squadInside = target;
				}
				catch { }
			}
		}
		string tgtDesc = VehicleOfCrew(target) != null ? Ui.Tr("车组") : Ui.Tr("步兵队");
		if (moved == 0)
		{
			cmdFlash = Ui.Tr("无可合并（选中单位已在同一小队）"); cmdFlashUntil = Time.unscaledTime + 2f;
			SquadCmdLogic.Log("[Merge] 收编 入队=0 已在队=" + skip + " 目标=0x" + ((long)target.Pointer).ToString("X"));
			return;
		}
		try { if (!selVehicles.Contains(target)) selVehicles.Add(target); } catch { }
		cmdFlash = string.Format(Ui.Tr("合并 → {0} 人入{1}"), moved, tgtDesc)
			+ (repointVehicles.Count > 0 ? string.Format(Ui.Tr("，{0} 辆车同队"), repointVehicles.Count) : "")
			+ (skip > 0 ? Ui.Tr("，已在队 ") + skip : "")
			+ (overflow > 0 ? Ui.Tr("，超员留下 ") + overflow : "");
		cmdFlashUntil = Time.unscaledTime + 2.5f;
		SquadCmdLogic.LogAlways("[Merge] 收编 入队=" + moved + " 已在队=" + skip + " 超员=" + overflow + " 目标=" + tgtDesc + "=0x" + ((long)target.Pointer).ToString("X") + " 同队车辆=" + repointVehicles.Count);
	}

	/// <summary>
	/// 0.8.2：【分散】——选中步兵按所属原生小队编组，以各队自身中心为圆心就地散开找掩护
	/// （原生 SendUnitsToCovers，与命令环"掩体"的区别：掩体以你指定的落点为中心，分散以队为中心）。
	/// </summary>
	private static void ScatterSelected()
	{
		HashSet<long> done = new HashSet<long>();
		int n = 0;
		foreach (Soldier s in GetSelectedInfantry())
		{
			try
			{
				if (s == null || !s.IsAlive) continue;
				Squad sq = s.joinedSquad;
				if (sq == null || !done.Add((long)sq.Pointer)) continue;
				Vector3 c = SquadCenterLocal(sq);
				if (c == Vector3.zero) continue;
				SquadCmdLogic.RegisterControlledSquad(sq);
				sq.SendUnitsToCovers(c, 30f);
				n++;
			}
			catch { }
		}
		if (n > 0)
		{
			ClearMoveObservation();
			cmdFlash = string.Format(Ui.Tr("分散 → {0} 队就地找掩护"), n); cmdFlashUntil = Time.unscaledTime + 2.5f;
			SquadCmdLogic.LogAlways("[SquadCmd] 分散 squads=" + n);
		}
		else { cmdFlash = Ui.Tr("无可用步兵小队"); cmdFlashUntil = Time.unscaledTime + 2f; }
	}

	private static Vector3 SquadCenterLocal(Squad sq)
	{
		Vector3 sum = Vector3.zero;
		int n = 0;
		try
		{
			int count = sq.CountMembers;
			for (int i = 0; i < count; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m == null || m.transform == null) continue;
				sum += m.transform.position;
				n++;
			}
		}
		catch { }
		return n > 0 ? sum / n : Vector3.zero;
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

	/// <summary>0.7.99：下达移动后登记完成度观测。0.9.15：改用原生停火通道 + 压制有效期。
	/// 1.0.3：routeOnly=true 仅登记路线显示/到位统计，不加行军停火（双击「前往并防守」用）。
	/// 1.2.0：改 internal——Formation 阵型下发复用（routeOnly，进掩体需要自由行为）。</summary>
	internal static void RegisterMoveObservation(Vector3 point, List<Soldier> units, bool routeOnly = false)
	{
		obsTarget = point;
		obsUnits.Clear();
		HashSet<long> squads = new HashSet<long>();
		foreach (Soldier s in units)
		{
			if (s == null || !s.IsAlive) continue;
			obsUnits.Add(s);
			try
			{
				SquadCmdLogic.RegisterControlledUnit(s);
				if (routeOnly) continue; // 只画路线：不停火、不禁找掩护
				if (!obsNoEngage.Contains(s)) obsNoEngage.Add(s);
				Squad sq = s.joinedSquad;
				if (sq != null) squads.Add((long)sq.Pointer);
			}
			catch { }
		}
		// 0.9.15：原生停火（小队级 SetHoldFireOrder）+ 士兵级禁被压制找掩护。
		// 不再切断 GetBestVisibleEnemy——全盲会引发任务系统异常（罚站/冻结）且拦不住任务级打断。
		if (routeOnly) squads.Clear();
		foreach (long k in squads)
		{
			try
			{
				Squad sq = ResolveSquadByPointer(k);
				if (sq == null) continue;
				SquadCmdLogic.RegisterControlledSquad(sq);
				sq.SetHoldFireOrder(true, false, false, false);
				obsNoEngageSquads.Add(k);
			}
			catch { }
		}
		foreach (Soldier s in obsNoEngage)
		{
			try
			{
				AiParams ap = new Lua_Soldier(s).getAiParams();
				ap.allowFindCoverWhenSuppressed(false);
			}
			catch { }
		}
		obsUntil = Time.unscaledTime + 45f;
		obsNext = Time.unscaledTime + 1f;
		if (!routeOnly) obsNoEngageExpire = Time.unscaledTime + NoEngageMaxSeconds; // 保险丝：20s 后自动恢复开火（routeOnly 未停火，不动保险丝）
		ObsTotal = obsUnits.Count + obsVehicles.Count;
		ObsArrived = 0;
	}

	/// <summary>0.9.15：行军停火到期时间（保险丝，防长期罚站）。</summary>
	private static float obsNoEngageExpire = -10f;

	/// <summary>0.9.15：按指针在存活单位里找回 Squad（interop 对象可能已被 GC 重建）。</summary>
	private static Squad ResolveSquadByPointer(long ptr)
	{
		try
		{
			Il2CppSystem.Collections.Generic.List<Creature> list = Creature.allCreatures;
			if (list == null) return null;
			for (int i = 0; i < list.Count; i++)
			{
				Creature c = list[i];
				if (c == null) continue;
				Soldier s = c.TryCast<Soldier>();
				if (s == null || !s.IsAlive) continue;
				Squad sq = s.joinedSquad;
				if (sq != null && (long)sq.Pointer == ptr) return sq;
			}
		}
		catch { }
		return null;
	}

	/// <summary>1.4.15 兼容：恢复开火（解除行军停火）。
	/// 直接用 `SetHoldFireOrder(false, false, false, false)` 会被第三方 mod 吞掉——
	/// Advanced Combat Movement（Responsive Orders）对该签名组合加了 Prefix：
	/// 「小队长 == 当前操控兵」时它只记一次危险记忆就 return false，
	/// 小队因此**永久停火**（表现：下了移动令后部队再也不还击）。
	/// 这里改成「调用 → 回读校验 → 不生效就直写原生 holdFire 字段」：
	/// 字段写入不经过方法，Harmony 前缀拦不住。无第三方 mod 时校验必然通过，行为不变。</summary>
	private static void ResumeFire(Squad sq)
	{
		if (sq == null) return;
		try { sq.SetHoldFireOrder(false, false, false, false); } catch { }
		try
		{
			bool stillHolding = false;
			try { stillHolding = sq.HoldFire; } catch { }
			if (!stillHolding) { try { stillHolding = sq.holdFire; } catch { } }
			if (stillHolding)
			{
				sq.holdFire = false;
				SquadCmdLogic.LogAlways("[SquadCmd] 恢复开火被第三方 mod 拦截，已直写 holdFire 兜底");
			}
		}
		catch { }
	}

	private static void ClearMoveObservation()
	{
		// 0.9.15：观测结束（到位/超时/改令/到期）恢复开火与被压制找掩护
		foreach (Soldier s in obsNoEngage)
		{
			try
			{
				if (s != null && s.IsAlive)
				{
					AiParams ap = new Lua_Soldier(s).getAiParams();
					ap.allowCheckForEnemies(true);
					ap.allowFindCoverWhenSuppressed(true);
				}
			}
			catch { }
		}
		obsNoEngage.Clear();
		foreach (long k in obsNoEngageSquads)
		{
			try
			{
				Squad sq = ResolveSquadByPointer(k);
				ResumeFire(sq); // 1.4.15：兼容第三方吞调用（见方法注释）
			}
			catch { }
		}
		obsNoEngageSquads.Clear();
		obsUnits.Clear();
		obsVehicles.Clear();
		obsUntil = -10f;
		obsNoEngageExpire = -10f;
		ObsTotal = 0;
		ObsArrived = 0;
		hasCmdTarget = false; // 1.2.1：观察结束 → 目标点标记与行进连线一起消失（用户要求二者同步）
	}

	/// <summary>0.8.00：载具移动成功发单后登记（去重；到位半径=2×moveRadius，车体大停得远）。</summary>
	private static void RegisterVehicleObservation(Vehicle v, Vector3 point)
	{
		if (v == null || v.transform == null) return;
		try
		{
			long p = (long)v.Pointer;
			foreach (Vehicle o in obsVehicles)
				if (o != null && (long)o.Pointer == p) return;
		}
		catch { return; }
		obsVehicles.Add(v);
		obsTarget = point;
		obsUntil = Time.unscaledTime + 45f;
		if (obsNext < Time.unscaledTime) obsNext = Time.unscaledTime + 1f;
		ObsTotal = obsUnits.Count + obsVehicles.Count;
	}

	/// <summary>每 1s 统计到位数；全员到位输出完成日志并清空，45s 超时静默放弃。不发任何修正命令。</summary>
	private static void ObsMoveTick()
	{
		if (obsUnits.Count == 0 && obsVehicles.Count == 0) return;
		if (Time.unscaledTime > obsUntil) { ClearMoveObservation(); return; }
		if (Time.unscaledTime < obsNext) return;
		obsNext = Time.unscaledTime + 1f;
		// 0.9.15：行军停火保险丝到期 → 恢复全部开火/感知（防罚站冻结）
		if (obsNoEngageExpire > 0f && Time.unscaledTime > obsNoEngageExpire)
		{
			obsNoEngageExpire = -10f;
			foreach (Soldier s in obsNoEngage)
			{
				try
				{
					if (s == null || !s.IsAlive) continue;
					AiParams ap = new Lua_Soldier(s).getAiParams();
					ap.allowCheckForEnemies(true);
					ap.allowFindCoverWhenSuppressed(true);
				}
				catch { }
			}
			foreach (long k in obsNoEngageSquads)
			{
				try { Squad sq = ResolveSquadByPointer(k); ResumeFire(sq); } catch { }
			}
			obsNoEngageSquads.Clear();
			SquadCmdLogic.LogAlways("[SquadCmd] 行军停火到期，已恢复交战（单位仍未到位可再下移动令）");
		}
		int alive = 0, arrived = 0;
		float r2 = Plugin.radius.Value * Plugin.radius.Value;
		float vR = Plugin.radius.Value * 2f;
		float vr2 = vR * vR;
		for (int i = obsUnits.Count - 1; i >= 0; i--)
		{
			Soldier s = obsUnits[i];
			try
			{
				if (s == null || !s.IsAlive || s.transform == null) { obsUnits.RemoveAt(i); continue; }
				alive++;
				if ((s.transform.position - obsTarget).sqrMagnitude <= r2) arrived++;
			}
			catch { obsUnits.RemoveAt(i); }
		}
		for (int i = obsVehicles.Count - 1; i >= 0; i--)
		{
			Vehicle v = obsVehicles[i];
			try
			{
				if (v == null || v.transform == null) { obsVehicles.RemoveAt(i); continue; }
				alive++;
				if ((v.transform.position - obsTarget).sqrMagnitude <= vr2) arrived++;
			}
			catch { obsVehicles.RemoveAt(i); }
		}
		ObsArrived = arrived;
		ObsTotal = alive;
		if (alive == 0) { ClearMoveObservation(); return; }
		if (arrived >= alive)
		{
			SquadCmdLogic.LogAlways("[SquadCmd] MOVE 完成 " + arrived + " 单位");
			cmdFlash = string.Format(Ui.Tr("到达 → {0} 单位"), arrived); cmdFlashUntil = Time.unscaledTime + 2.5f;
			ClearMoveObservation();
		}
	}

	private static void BoardPendingTick()
	{
		if (pendingBoardVeh == null || pendingBoardSq == null || pendingBoardUnits == null) return;
		Vehicle veh = pendingBoardVeh;
		Squad sq = pendingBoardSq;
		try
		{
			if (veh.transform == null) { CancelBoardPending("载具销毁"); return; }
			if (Time.unscaledTime < pendingBoardNext) return;
			pendingBoardNext = Time.unscaledTime + 0.5f;
			// 0.7.97：纯观察。登车令已在下令瞬间全量发出（全原生），这里只做完成判定与超时补发，
			// 不再发任何接近引导/分段命令（Mod 自建移动编排已整体移除）。
			HashSet<long> inCar = new HashSet<long>();
			try
			{
				Soldier[] occ = veh.GetComponentsInChildren<Soldier>();
				if (occ != null) for (int i = 0; i < occ.Length; i++) if (occ[i] != null && occ[i].IsAlive) inCar.Add((long)occ[i].Pointer);
			}
			catch { }
			// 完成判据：全部存活乘员真实在车
			bool allIn = true;
			foreach (Soldier bs in pendingBoardUnits)
			{
				try { if (bs != null && bs.IsAlive && !inCar.Contains((long)bs.Pointer)) { allIn = false; break; } } catch { }
			}
			if (allIn) { FinishBoardPending("全员在车"); return; }
			// 1.2.1：登车期间目标点标记跟随载具（标记与登车连线一起显示、一起消失）
			try { RecordCmdTarget(veh.transform.position); } catch { }
			if (Time.unscaledTime > pendingBoardUntil)
			{
				foreach (Soldier bs in pendingBoardUnits)
				{
					try
					{
						if (bs == null || !bs.IsAlive || inCar.Contains((long)bs.Pointer)) continue;
						new Lua_Soldier(bs).boardVehicle(new Lua_Vehicle(veh));
					}
					catch { }
				}
				FinishBoardPending("超时补发登车令");
			}
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
			// 0.8.00：仅当登车上下文仍是当前选择（用户中途未取消/换选）时才转选车组。
			// 判据：发起时 selVehicles 加入了 sq，用户 ClearSelection 会把它带走。
			bool stillSelected = false;
			try { stillSelected = sq != null && selVehicles.Contains(sq); } catch { }
			if (veh != null && stillSelected)
			{
				selVehicleRefs.Clear();
				selVehicles.Add(sq);
				AddVehicleRef(veh);
			}
			SquadCmdLogic.LogAlways("[BoardPending] 完成（" + reason + "） vehicle=" + (veh != null ? veh.name : "?") + " 转选=" + (stillSelected ? "Y" : "N"));
			cmdFlash = stillSelected ? Ui.Tr("登车完成 → 可直接驾驶") : Ui.Tr("登车完成"); cmdFlashUntil = Time.unscaledTime + 2.5f;
			if (stillSelected) selFlash = Time.unscaledTime + 3f;
		}
		catch { }
		pendingBoardVeh = null; pendingBoardSq = null; pendingBoardUnits = null;
	}

	private static void CancelBoardPending(string reason)
	{
		try
		{
			SquadCmdLogic.Log("[BoardPending] 取消：" + reason + " vehicle=" + (pendingBoardVeh != null ? pendingBoardVeh.name : "?"));
		}
		catch { }
		pendingBoardVeh = null; pendingBoardSq = null; pendingBoardUnits = null;
	}

	/// <summary>0.7.48：分队同步窗口的自动重试（最多 3s；收敛后把同一目标补发给新 Squad 原生命令）。</summary>
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
			// 玩家接管车组成员后 Mod 不再代驾：取消同步窗口重试，把车交还玩家。
			try
			{
				PlayerController pc = PlayerController.currentController;
				Soldier ctrl = pc != null ? pc.ControlledCharacter : null;
				Vehicle playerVehicle = ctrl != null ? ctrl.GetComponentInParent<Vehicle>() : null;
				if (playerVehicle != null && playerVehicle.Pointer == pendVeh.Pointer) { pendVeh = null; return; }
			}
			catch { }
			Vehicle retryVeh = pendVeh;
			VehicleFacing.CancelFor(retryVeh); // 1.0.2：重试移动同样覆盖朝向
			if (SquadCmdLogic.TryIssueNativeMove(tgt, pendPoint, Plugin.radius.Value))
			{
				SquadCmdLogic.Log("[VehicleMove] vehicle=" + retryVeh.name + " squadInside=0x" + ((long)tgt.Pointer).ToString("X")
					+ " target=" + pendPoint.ToString("0.0") + " via=LuaSquad（同步窗口重试）");
			}
			else
			{
				new Lua_Squad(tgt).moveTo(pendPoint, Plugin.radius.Value);
				SquadCmdLogic.Log("[VehicleMove] vehicle=" + retryVeh.name + " squadInside=0x" + ((long)tgt.Pointer).ToString("X")
					+ " target=" + pendPoint.ToString("0.0") + " via=Fallback（同步窗口重试）");
			}
			pendVeh = null;
		}
		catch { pendVeh = null; }
	}

	/// <summary>我方阵营（上帝视角用缓存，平时读受控角色）。</summary>
	/// <summary>1.4.14 性能：阵营缓存。MySideFaction 在 `GetBestVisibleEnemy` Postfix 里被
	/// **每兵每帧**调用一次（集火标记生效期间战场上数百兵 × 60fps → 每秒上万次 interop 读 +
	/// 字符串封送），而阵营在整场战斗里根本不变。缓存 2s；SavedFaction 变化时立即失效。</summary>
	private static string mySideFacCache = null;
	private static float mySideFacNext = -10f;
	private static string mySideFacSaved; // 缓存时生效的 SavedFaction（用于失效判定）

	/// <summary>1.4.14 性能：玩家当前操控士兵的缓存（0.5s）。
	/// `PlayerController.currentController` + `ControlledCharacter` 是两次 interop 读，
	/// 而它在 `GetBestVisibleEnemy` / `Vehicle.CurrentVisibleTarget` 两个 Postfix 里
	/// **每兵每帧**被查询（战场上数百单位 → 每秒上万次）。切换操控单位最多晚 0.5s 生效，
	/// 对"标记不强制玩家自己"这一用途完全够用。</summary>
	private static Soldier playerSoldierCache;
	private static float playerSoldierNext = -10f;

	internal static Soldier CachedPlayerSoldier()
	{
		float now = Time.unscaledTime;
		if (now < playerSoldierNext) return playerSoldierCache;
		playerSoldierNext = now + 0.5f;
		try
		{
			PlayerController pc = PlayerController.currentController;
			Soldier c = pc != null ? pc.ControlledCharacter : null;
			playerSoldierCache = (c != null && c.transform != null) ? c : null;
		}
		catch { playerSoldierCache = null; }
		return playerSoldierCache;
	}

	/// <summary>1.4.14：切换操控/接管/退出时立刻失效玩家缓存。</summary>
	internal static void InvalidatePlayerSoldier() { playerSoldierCache = null; playerSoldierNext = -10f; }

	internal static string MySideFaction()
	{
		float now = Time.unscaledTime;
		if (mySideFacCache != null && now < mySideFacNext && string.Equals(mySideFacSaved, SavedFaction, StringComparison.Ordinal)) return mySideFacCache;
		mySideFacNext = now + 2f;
		mySideFacSaved = SavedFaction;
		if (Active && !string.IsNullOrEmpty(SavedFaction)) { mySideFacCache = SavedFaction; return mySideFacCache; }
		try
		{
			PlayerController pc = PlayerController.currentController;
			if (pc != null && pc.ControlledCharacter != null && !string.IsNullOrEmpty(pc.ControlledCharacter.faction))
			{
				mySideFacCache = pc.ControlledCharacter.faction;
				return mySideFacCache;
			}
		}
		catch { }
		// 拿不到就返回空串（调用方 Friendly() 对空串返回 false = 不改写 AI 目标），
		// 但**不缓存空串**，下帧继续重试。
		return "";
	}

	/// <summary>该士兵是否在当前选择中（步兵选择 / 选中载具车组）。标记集火只引导选中单位。
	/// 1.4.14 性能：先查 smSelected（0.2s 刷新的选中指针集，与 GetCommandUnits 同源）；
	/// 命中即真，未命中再走原 interop 兜底——**保持语义不变**（缓存只是加速，不是唯一判据）。</summary>
	internal static bool IsSelectedUnit(Soldier s)
	{
		if (s == null) return false;
		try { if (smSelected.Count > 0 && smSelected.Contains((long)s.Pointer)) return true; } catch { }
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

	/// <summary>该士兵是否仍属于持久集火快照，而不是当前 RTS 选择。</summary>
	internal static bool IsMarkUnit(Soldier s)
	{
		if (s == null) return false;
		try
		{
			long ptr = (long)s.Pointer;
			for (int i = 0; i < persistentMarkUnits.Count; i++)
			{
				Soldier u = persistentMarkUnits[i];
				if (u != null && (long)u.Pointer == ptr) return true;
			}
		}
		catch { }
		return false;
	}

	/// <summary>该载具是否仍属于持久集火快照，而不是当前 RTS 选择。</summary>
	internal static bool IsMarkVehicle(Vehicle v)
	{
		if (v == null) return false;
		try
		{
			long ptr = (long)v.Pointer;
			for (int i = 0; i < persistentMarkVehicles.Count; i++)
			{
				Vehicle rv = persistentMarkVehicles[i];
				if (rv != null && (long)rv.Pointer == ptr) return true;
			}
		}
		catch { }
		return false;
	}

	/// <summary>
	/// 上车（0.7.97 全原生）：下令瞬间对空位内的步兵逐员发出 boardVehicle，
	/// 走位/绕车门/入座全部由原生登车任务完成——Mod 不再发任何接近引导或分段命令
	/// （自建两段式与原生登车双源竞争，是"走到车边不上车、要再框选一次"的根因）。
	/// 超员者不下令，保持原生行为。同帧转队/登记（0.7.85 语义）只负责归属与驾驶资格。
	/// </summary>
	private static int BoardVehicle(Vehicle veh)
	{
		if (veh == null) return 0;
		ClearMoveObservation(); // 0.9.14：上车是新命令——清掉行军压制/任务置空的残留（否则登车后无法驾驶）
		List<Soldier> units = GetSelectedInfantry();
		if (units.Count == 0)
		{
			cmdFlash = Ui.Tr("先框选/选中要指挥的单位"); cmdFlashUntil = Time.unscaledTime + 2f;
			return 0;
		}
		Lua_Vehicle lv = new Lua_Vehicle(veh);
		// 解锁（防止 locked 导致上车静默失败）
		try { if (veh.IsLocked()) veh.SetLocked(false); } catch { }
		int seats = 99;
		try { seats = lv.countEmptySeats(); } catch { }
		if (seats <= 0)
		{
			cmdFlash = Ui.Tr("载具已满"); cmdFlashUntil = Time.unscaledTime + 2f;
			return 0;
		}
		List<Soldier> wait = new List<Soldier>();
		foreach (Soldier s in units)
		{
			if (wait.Count >= seats) break;
			if (s != null && s.IsAlive) wait.Add(s);
		}
		if (wait.Count == 0) return 0;
		pendingBoardVeh = veh;
		pendingBoardSq = CreateNewSquad();
		pendingBoardUnits = wait;
		pendingBoardUntil = Time.unscaledTime + 60f; // 全原生走位，距离可能很远
		// 同帧转队/登记（0.7.85）：乘员此刻已是新队成员、组已获驾驶资格
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
		// 0.7.97：登车令在下令瞬间全量发出，此后移动指挥权完全归原生
		foreach (Soldier bs in wait)
		{
			try { new Lua_Soldier(bs).boardVehicle(lv); } catch { }
		}
		SquadCmdLogic.LogAlways("[BoardPending] 全原生登车令 vehicle=" + veh.name + " 人数=" + wait.Count + " 转队=" + moved);
		RecordCmdTarget(veh.transform.position);
		// 保持选择连续性：上车的士兵会被原生编入车组，步兵选择会凭空消失——
		// 这里把选择转换为该车组的载具选择
		cmdFlash = string.Format(Ui.Tr("上车 → {0} 人（原生登车中…）"), wait.Count) + (units.Count > wait.Count ? string.Format(Ui.Tr("，余 {0} 人未下令"), units.Count - wait.Count) : ""); cmdFlashUntil = Time.unscaledTime + 3f;
		return wait.Count;
	}

	/// <summary>
	/// 下车：已选载具/火力点车组全部下车，并让车组步行离开载具附近
	/// （解决"下车后立刻又上车"：先停追踪，再给车组新的步行目标，AI 不会为了旧命令重新上车）。
	/// </summary>
	private static int DismountAllVehicles()
	{
		int n = 0;
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
		cmdFlash = string.Format(Ui.Tr("已下车 {0} 辆载具"), n); cmdFlashUntil = Time.unscaledTime + 2f;
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
				cmdFlash = Ui.Tr("不能按键退出：框选/选中单位 → 点顶部 [控制该小队] 接管"); cmdFlashUntil = Time.unscaledTime + 4f;
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
				InvalidatePlayerSoldier(); // 1.4.14：操控单位变了 → 立刻失效缓存
				SquadCmdLogic.Log("[SquadCmd] 上帝视角：玩家单位 AI 接管，已脱离控制。");
				HideSquadPanel();
			}
			SquadCmdLogic.ClearControlledSelection();
			ClearSelection();
			ResetInputState();
			BackpackPanel.CloseAll(); // 1.3.0：进入上帝视角先收干净（幂等）
			BackpackPanel.CancelLoot("进入 RTS"); // 1.4.9：进 RTS = 用户主动中断在途的派兵会合任务
			flyingToSquad = false;

			Vector3 start = Vector3.zero;
			if (savedSoldier != null) start = savedSoldier.transform.position;
			else { Camera c = MainCam(); if (c != null) start = c.transform.position; }
			camPos = start + Vector3.up * 70f;
			yaw = 0f; pitch = 70f;
			RecomputeRot();
			Active = true;
			SetCursor(true);
			EnsureCameraAuthority(); // 1.2.11：装上 onPreRender 相机接管
			ApplyCam(MainCam());
			cmdFlash = Ui.Tr("上帝视角 ON（框选临时选择，右键指挥，空格暂停）"); cmdFlashUntil = Time.unscaledTime + 4f;
			SquadCmdLogic.Log("[SquadCmd] 上帝视角 ON  pos=" + camPos.ToString("0.0") + " RTS 控制权仅绑定当前选择");
		}
		catch (Exception ex) { Active = false; SquadCmdLogic.Log("[SquadCmd] 上帝视角进入失败: " + ex.Message); }
	}

	internal static void Exit()
	{
		if (!Active) return;
		Active = false;
		ResetInputState();
		EnsureTimeResumed();
		// 退出只关闭 RTS 界面和相机接管。已经下达的移动、登车、车辆同步
		// 与集火任务必须留在世界中，由 Tick 的持久任务段继续维护。
		RestoreAllPoses();
		Formation.OnRtsExit(); // 1.2.0：清阵型拖动态/幽灵预览/待转向任务
		MouseCursor.Restore(); // 1.2.5：恢复系统光标
		BackpackPanel.CloseAll(); // 1.3.0：关背包窗口 + 清拖拽态
		BackpackPanel.CancelLoot("退出 RTS"); // 1.4.9：退出 = 中断在途会合任务
		SquadCmdLogic.ClearControlledSelection();
		flyingToSquad = false;
		ClearSelection();
		SavedFaction = "";
		if (savedSoldier != null)
		{
			try { new Lua_Soldier(savedSoldier).getAiParams().enableAiBehaviour(false); } catch { }
			if (savedSoldier.IsAlive && PlayerController.currentController != null)
			{
				try { PlayerController.currentController.SetPlayer(savedSoldier, 0f); } catch { }
				InvalidatePlayerSoldier(); // 1.4.14：操控单位变了 → 立刻失效缓存
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
	private static bool deathGuardDone;

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
				cmdFlash = Ui.Tr("请先框选/选中要接管的单位"); cmdFlashUntil = Time.unscaledTime + 2f;
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
			// 只摘除玩家实际接管的单位；其余单位的移动/登车/集火任务继续执行。
			RemovePersistentUnit(pick);
			if (pendingBoardUnits != null)
			{
				int removed = pendingBoardUnits.RemoveAll(s =>
				{
					try { return s == null || s.Pointer == pick.Pointer; }
					catch { return false; }
				});
				if (removed > 0 && pendingBoardUnits.Count == 0) CancelBoardPending("接管单位");
			}
			Active = false;
			EnsureTimeResumed();
			suppressSwitchMemberUntil = Time.unscaledTime + SwitchMemberSuppressSeconds;
			RestoreAllPoses(); // 接管的单位若被蹲/趴锁定，先还原否则玩家自己也无法站起
			SquadCmdLogic.ClearControlledSelection();
			ResetInputState();
			BackpackPanel.CloseAll(); // 1.3.0：关背包窗口 + 清拖拽态
			BackpackPanel.CancelLoot("接管单位"); // 1.4.9：接管 = 中断在途会合任务
			flyingToSquad = false;
			ClearSelection();
			SavedFaction = "";
			savedSoldier = null;
			RestoreSquadPanel();
			SetCursor(false);
			try { PlayerController.currentController?.SetPlayer(pick, 0f); }
			catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] SetPlayer 失败: " + ex.Message); }
			InvalidatePlayerSoldier(); // 1.4.14：操控单位变了 → 立刻失效缓存
			lastKnownSoldier = pick; // 0.7.37：观测死亡沿的缓存基准
			takeoverProtectUntil = Time.unscaledTime + 10f; // A：接管保护窗启动
			lastProtectCheck = -10f; deathGuardDone = false;
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

	/// <summary>0.8.00：编组热键。Ctrl+数字=保存当前选择快照；数字=召回（死亡单位自动剔除，替换当前选择）。</summary>
	private static void HandleGroupHotkeys()
	{
		bool ctrl = Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl);
		for (int k = 1; k <= 9; k++)
		{
			if (!Input.GetKeyDown(KeyCode.Alpha1 + (k - 1))) continue;
			if (ctrl)
			{
				groupUnits[k] = new List<Soldier>(GetSelectedInfantry());
				groupCrews[k] = new List<Squad>(selVehicles);
				groupVehRefs[k] = new List<Vehicle>(selVehicleRefs);
				cmdFlash = string.Format(Ui.Tr("编组 {0} 已保存（步兵 {1} + 车组 {2}）"), k, groupUnits[k].Count, groupCrews[k].Count);
				cmdFlashUntil = Time.unscaledTime + 2f;
				SquadCmdLogic.LogAlways("[SquadCmd] 编组 " + k + " 保存 步兵=" + groupUnits[k].Count + " 车组=" + groupCrews[k].Count);
			}
			else if (groupUnits.ContainsKey(k) || groupCrews.ContainsKey(k))
			{
				List<Soldier> units = new List<Soldier>();
				if (groupUnits.TryGetValue(k, out List<Soldier> gu) && gu != null)
					foreach (Soldier s in gu) { try { if (s != null && s.IsAlive) units.Add(s); } catch { } }
				List<Squad> crews = new List<Squad>();
				if (groupCrews.TryGetValue(k, out List<Squad> gc) && gc != null)
					foreach (Squad c in gc) { try { if (c != null && AliveCount(c) > 0) crews.Add(c); } catch { } }
				if (units.Count == 0 && crews.Count == 0)
				{
					cmdFlash = string.Format(Ui.Tr("编组 {0} 已无存活单位"), k); cmdFlashUntil = Time.unscaledTime + 2f;
					return;
				}
				ClearSelection();
				foreach (Soldier s in units) { try { if (!virtualUnits.Contains(s)) virtualUnits.Add(s); } catch { } }
				foreach (Squad c in crews) { try { if (!selVehicles.Contains(c)) selVehicles.Add(c); } catch { } }
				if (groupVehRefs.TryGetValue(k, out List<Vehicle> gv) && gv != null)
					foreach (Vehicle v in gv) { try { if (v != null && v.transform != null) AddVehicleRef(v); } catch { } }
				cmdFlash = string.Format(Ui.Tr("编组 {0}（步兵 {1} + 车组 {2}）"), k, units.Count, crews.Count);
				cmdFlashUntil = Time.unscaledTime + 2f;
				selFlash = Time.unscaledTime + 2.5f;
			}
			return; // 每帧至多处理一个数字键
		}
	}

	// ===== 1.2.0：命令快捷键（旧命令环 8 项的键盘化，cfg Hotkeys 节可自定义键位） =====

	private static void HandleCommandHotkeys()
	{
		// 1.3.0：背包窗口热键（焦点单位/尸体/载具；尸体选择不走 HasSelection，须在最前）
		if (Plugin.keyPack.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyPack.Value)) { BackpackPanel.ToggleFocused(); return; }
		if (formationDragActive) return;
		if (!HasSelection) return;
		if (Plugin.keyStand.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyStand.Value)) { ResetPoseToSelection(); return; }
		if (Plugin.keyCrouch.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyCrouch.Value)) { ApplyPoseToSelection(SoldierPose.Crouch); return; }
		if (Plugin.keyProne.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyProne.Value)) { ApplyPoseToSelection(SoldierPose.Prone); return; }
		if (Plugin.keyStop.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyStop.Value)) { ClearFollow("停止", false); ClearMark(); StopSelected(); return; }
		if (Plugin.keyHoldFire.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyHoldFire.Value)) { ToggleHoldFireSelected(); return; }
		if (Plugin.keyCover.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyCover.Value)) { CoverAtSelectionCenter(); return; }
		if (Plugin.keyRally.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyRally.Value)) { RallyToLeaders(); return; }
		if (Plugin.keyScatter.Value != KeyCode.None && Input.GetKeyDown(Plugin.keyScatter.Value)) { ScatterSelected(); return; }
	}

	/// <summary>停火/开火切换：有停火的小队→全部恢复开火；全部开火中→全部停火（原命令环【停火】）。</summary>
	private static void ToggleHoldFireSelected()
	{
		HashSet<long> doneH = new HashSet<long>();
		List<Squad> squads = new List<Squad>();
		foreach (Soldier s in GetSelectedInfantry())
		{
			try
			{
				if (s == null || !s.IsAlive) continue;
				Squad sq = s.joinedSquad;
				if (sq == null || !doneH.Add((long)sq.Pointer)) continue;
				squads.Add(sq);
			}
			catch { }
		}
		foreach (Squad csq in GetSelectedVehicleCrews())
		{
			try { if (csq != null && doneH.Add((long)csq.Pointer)) squads.Add(csq); } catch { }
		}
		if (squads.Count == 0) { cmdFlash = Ui.Tr("无可用小队"); cmdFlashUntil = Time.unscaledTime + 2f; return; }
		bool anyHolding = false;
		foreach (Squad sq in squads) { try { if (sq.holdFire) { anyHolding = true; break; } } catch { } }
		bool hold = !anyHolding;
		int nH = 0;
		foreach (Squad sq in squads) { try { sq.holdFire = hold; nH++; } catch { } }
		cmdFlash = (hold ? Ui.Tr("停火 → ") : Ui.Tr("开火 → ")) + nH + Ui.Tr(" 队"); cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.LogAlways("[SquadCmd] " + (hold ? "停火" : "开火") + " squads=" + nH);
	}

	/// <summary>就近掩体：以选中单位中心为圆心 SendUnitsToCovers（原命令环【掩体】快捷键版）。</summary>
	private static void CoverAtSelectionCenter()
	{
		HashSet<long> doneC = new HashSet<long>();
		int nC = 0;
		Vector3 p = SelCenter();
		foreach (Soldier s in GetSelectedInfantry())
		{
			try
			{
				if (s == null || !s.IsAlive) continue;
				Squad sq = s.joinedSquad;
				if (sq == null || !doneC.Add((long)sq.Pointer)) continue;
				SquadCmdLogic.RegisterControlledSquad(sq);
				sq.SendUnitsToCovers(p, Plugin.radius.Value * 2f);
				nC++;
			}
			catch { }
		}
		if (nC > 0)
		{
			ClearFollow("就近掩体", false);
			cmdFlash = string.Format(Ui.Tr("就近掩体 → {0} 队"), nC); cmdFlashUntil = Time.unscaledTime + 2f;
			SquadCmdLogic.LogAlways("[SquadCmd] 就近掩体 squads=" + nC + " center=" + p.ToString("0.0"));
		}
		else { cmdFlash = Ui.Tr("无可用步兵小队"); cmdFlashUntil = Time.unscaledTime + 2f; }
	}

	/// <summary>集合：各步兵小队向自己的班长（原生 getLeader）位置集结（原命令环【集合】快捷键版）。</summary>
	private static void RallyToLeaders()
	{
		HashSet<long> doneF = new HashSet<long>();
		int nF = 0;
		foreach (Soldier s in GetSelectedInfantry())
		{
			try
			{
				if (s == null || !s.IsAlive) continue;
				Squad sq = s.joinedSquad;
				if (sq == null || !doneF.Add((long)sq.Pointer)) continue;
				Soldier leader = null;
				try { leader = new Lua_Squad(sq).getLeader()?.connectedSoldier; } catch { }
				if (leader == null || !leader.IsAlive || leader.transform == null) continue;
				SquadCmdLogic.RegisterControlledSquad(sq);
				new Lua_Squad(sq).moveTo(leader.transform.position, Plugin.radius.Value);
				nF++;
			}
			catch { }
		}
		if (nF > 0) SquadCmdLogic.LogAlways("[SquadCmd] 集合（向班长集结） squads=" + nF);
		else { cmdFlash = Ui.Tr("无可用小队（找不到班长）"); cmdFlashUntil = Time.unscaledTime + 2f; }
	}

	internal static void Tick()
	{
		// 持久任务段：无论当前是 RTS 还是 FPS，都继续维护已经下达的任务。
		try
		{
			// 1.2.8：**每步独立 try/catch**。此前整块共用一个 try——任一步抛异常就跳过
			// SceneMarkersFrame()，而 SceneMarkers.EndFrame()（隐藏本轮未刷新标记）在它内部，
			// 于是已画出的阵型箭头/路线虚线/掩体圈**永久卡在画面上**（用户截图里的残留标记）。
			SafeStep("PruneMark", PruneMark);
			SafeStep("FormationDrag", Formation.DragTick);
			bool markersDrawn = false;
			try { SceneMarkersFrame(); markersDrawn = true; }
			catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] 场景标记错误: " + ex.Message); }
			// 1.2.9：SceneMarkersFrame **内部自己会**调 EndFrame()（并清空 used 集合）——
			// 1.2.8 在这里无条件又调一次，第二次 used 已空 → 本帧所有标记被立刻隐藏
			//（用户反馈"所有3D随动标记都没了"）。只在绘制中途失败（没走到它自己的 EndFrame）时补清理。
			if (!markersDrawn) { try { SceneMarkers.EndFrame(); } catch { } }
			SafeStep("VehicleFacing", VehicleFacing.Tick);
			SafeStep("FormationFacing", Formation.TickPendingFacings);
			SafeStep("CoverArrivals", Formation.TickCoverArrivals);
			SafeStep("Cursor", MouseCursor.Tick);
			SafeStep("ObsMove", ObsMoveTick);
			SafeStep("BoardPending", BoardPendingTick);
			SafeStep("VehiclePending", VehiclePendingTick);
		}
		catch (Exception ex)
		{
			SquadCmdLogic.Log("[SquadCmd] 持久任务维护错误: " + ex.Message);
		}

		// 以下全部属于 RTS 界面/镜头/鼠标链。FPS 时直接放行，不触碰原生输入。
		if (!Active || flyingToSquad) return;

		// 维护链与输入链隔离。单位销毁、原生 Squad 重建等 interop 异常，
		// 不应跳过本帧的鼠标松开事件，否则会把框选/右键手势留在半截状态。
		try
		{
			if (Time.unscaledTime > panelHideTimer)
			{
				panelHideTimer = Time.unscaledTime + 1f;
				HideSquadPanel();
			}
			PeriodicCleanup();
			PruneSelection();
			// 0.7.44：注册链不得带共享 Squad——Vehicle 选择只注册其乘员 Soldier。
			// 1.2.10 性能：原实现**每帧**调 SelectedVehicleOccupants()（内部 GetComponentsInChildren
			// 会分配数组）+ 3 个 List 分配。改为 0.25s 节流，且无载具选择时直接跳过乘员展开。
			if (Time.unscaledTime >= ctrlSyncNext)
			{
				ctrlSyncNext = Time.unscaledTime + 0.25f;
				ctrlSyncBuf.Clear();
				GetSelectedInfantryInto(ctrlSyncBuf);
				PruneVehicleRefs();
				if (selVehicleRefs.Count > 0) ctrlSyncBuf.AddRange(SelectedVehicleOccupants());
				SquadCmdLogic.SyncControlledSelection(ctrlSyncBuf, ctrlSyncSquads);
			}
		}
		catch (Exception ex)
		{
			SquadCmdLogic.Log("[SquadCmd] Tick 维护错误: " + ex.Message);
		}

		// 输入先处理，避免相机/原生对象访问异常阻断鼠标手势收尾。
		try { if (!escMenuOpen) HandleGroupHotkeys(); } catch { }
		try { if (!escMenuOpen) HandleCommandHotkeys(); } catch { } // 1.2.0：命令快捷键（cfg Hotkeys 可自定义）
		try { BackpackPanel.LootTick(); } catch { } // 1.3.3：派兵翻尸体——到达后自动开背包窗口
		// 0.9.11：原版 M 地图在 god 视角强制显示也为空（显隐机制未明），已按设计在 RTS 内禁用，
		// 相关强制显示/还原代码一并移除；FPS 模式下 M 键走原生流程不受影响。
		try { HandleClick(); }
		catch (Exception ex)
		{
			ResetInputState();
			SquadCmdLogic.Log("[SquadCmd] 输入处理失败，已复位 RTS 手势: " + ex.Message);
		}

		// 0.9.6：RTS 内 ESC 开关原生暂停/设置菜单（自持状态；菜单内"继续"按钮触发的原生
		// Resume 会经 IsTimePaused 检测自动解除让位）
		try
		{
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				if (!escMenuOpen)
				{
					Pause.SetPause(true);
					escMenuOpen = true;
					BackpackPanel.ResetDrag("ESC 菜单"); // 1.3.0：菜单打开期间 OnGUI 停跑，拖拽态先收干净
					BackpackPanel.CloseMenu(); // 1.4.0：交互菜单同上
					SquadCmdLogic.Log("[SquadCmd] ESC 打开原生菜单");
				}
				else
				{
					// 0.9.7：对称关闭——SetPause(false)（Resume 实例方法不确定是否收起菜单）
					Pause.SetPause(false);
					escMenuOpen = false;
					SquadCmdLogic.Log("[SquadCmd] ESC 关闭原生菜单");
				}
			}
			else if (escMenuOpen && !Pause.IsTimePaused() && !Pause.IsPaused())
			{
				escMenuOpen = false; // 用户在菜单里点了"继续"（原生 Resume），自动解除让位
			}
		}
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] ESC 菜单切换失败: " + ex.Message); }

		try
		{
			// 1.4.15：先算本帧的"让位"状态，相机输入全部按它分流（见 UiPointerCapture 注释）。
			bool menuCapture = escMenuOpen;                          // 原生 ESC 设置/暂停菜单
			bool uiCapture = menuCapture || UiPointerCapture();      // 指针停在 UI（我方面板/外部 mod 面板）上
			if (!menuCapture && Input.GetKeyDown(KeyCode.Space)) TogglePause();
			// 用 unscaledDeltaTime：空格暂停（timeScale=0）时镜头仍可移动
			float dt = Mathf.Min(Time.unscaledDeltaTime, 0.05f);
			HandleMove(dt, menuCapture);
			HandleHeight(dt, menuCapture, uiCapture);
			HandleDrag(menuCapture, uiCapture);
			AssertFreeCursor();
			ApplyCam(MainCam());
		}
		catch (Exception ex)
		{
			// 相机链单独记录，不影响下一帧继续接收 RTS 输入。
			SquadCmdLogic.Log("[SquadCmd] Tick 相机错误: " + ex.Message);
		}
	}

	/// <summary>1.2.8：单步隔离执行（一步失败不影响后续步骤，尤其是标记清理）。</summary>
	private static void SafeStep(string tag, Action step)
	{
		try { step(); }
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] " + tag + " 错误: " + ex.Message); }
	}

	// ══════════════════════════════════════════════════════════
	// 1.2.11：**相机最终接管**。此前用 WaitForEndOfFrame（1.2.9）没能解决——
	// 说明原生相机写入发生在那之后（同相位的其他协程/渲染前回调）。
	// Camera.onPreRender 是**每个相机渲染前的最后一刻**（晚于所有 LateUpdate 与
	// WaitForEndOfFrame 协程），在这里把主相机压回我们的姿态 = 最终画面一定由我们决定。
	// 只在上帝视角内生效；只接管主相机（其他相机放行）。
	// ══════════════════════════════════════════════════════════
	private static bool camAuthorityInstalled;
	private static float camDiagNext = -10f;

	internal static void EnsureCameraAuthority()
	{
		if (camAuthorityInstalled) return;
		camAuthorityInstalled = true;
		try
		{
			Camera.CameraCallback cb = (System.Action<Camera>)OnPreRenderCamera;
			Camera.onPreRender = Camera.onPreRender + cb;
			SquadCmdLogic.Log("[CamAuthority] onPreRender 钩子已安装");
		}
		catch (Exception ex) { SquadCmdLogic.Log("[CamAuthority] 安装失败: " + ex.Message); }
	}

	private static void OnPreRenderCamera(Camera cam)
	{
		try
		{
			if (!Active || flyingToSquad || cam == null) return;
			Camera main = MainCam();
			if (main == null || cam.Pointer != main.Pointer) return;
			// 诊断：渲染前相机若已偏离我们的姿态，说明本帧有人更晚写入了它。
			// 1.2.12：改用 LogAlways 且**仅在偏离时**记录（原用 Log 被 debugLog 门控 → 一行都没记下来；
			// 无条件记录又会刷屏，故只在异常时输出）。
			if (Time.unscaledTime > camDiagNext)
			{
				Vector3 dp = cam.transform.position - camPos;
				Vector3 dr = cam.transform.rotation.eulerAngles - camRot.eulerAngles;
				if (dp.sqrMagnitude > 0.25f || Mathf.Abs(dr.y) > 3f)
				{
					camDiagNext = Time.unscaledTime + 1f;
					SquadCmdLogic.LogAlways("[CamAuthority] 渲染前相机偏离 dPos=" + dp.ToString("0.00")
						+ " dRotY=" + dr.y.ToString("0.0")
						+ " now=" + cam.transform.position.ToString("0.0")
						+ " want=" + camPos.ToString("0.0")
						+ " rmb=" + Input.GetMouseButton(1));
				}
			}
			cam.transform.position = camPos;
			cam.transform.rotation = camRot;
		}
		catch { }
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

	/// <summary>上帝视角期间强制光标自由（Cursor patch 已兜底，这里双保险）。
	/// 1.2.7：**右键按住时不再跳过**——跳过会导致拖动期间无人维持 lockState=None，
	/// 游戏一旦把光标锁回中央，鼠标位置突变 → 箭头端点暴走（用户报的"视角乱飞"）。</summary>
	private static void AssertFreeCursor()
	{
		try
		{
			if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
			if (MouseCursor.OwnsCursor) { if (!Cursor.visible) Cursor.visible = true; }
			else if (!Cursor.visible) Cursor.visible = true;
		}
		catch { }
	}

	internal static void FrameEndGuard()
	{
		// 1.2.10：此处的"帧末强制复位相机"已移除——1.2.9 的兜底既没解决问题（与原生相机逐帧互斗），
		// 真凶是 **TerrainCamera**（原生自由视角，见 Plugin.cs 的 GodViewSkipTerrainCameraPatch），
		// 现已从源头跳过其 Update，不再需要兜底互斗。
		if (Active)
		{
			// 0.9.11：返回主菜单/战斗结束检测——BattleManager 跨场景常驻（0.9.10 判据无效），
			// 改用"场景内无任何存活生物"（F9 紧急退出同款信号，Exit 已被实测能完整还原相机）。
			// 连续 2s 无生物才触发，避免战斗加载间隙误判；战斗中全灭同样生效（等同紧急退出）。
			try
			{
				int alive = 0;
				Il2CppSystem.Collections.Generic.List<Creature> list = Creature.allCreatures;
				if (list != null)
				{
					for (int i = 0; i < list.Count; i++)
					{
						Creature c = list[i];
						if (c == null || !c.IsAlive) continue;
						alive++;
						break;
					}
				}
				if (alive == 0)
				{
					if (smEmptyWorldSince < 0f) smEmptyWorldSince = Time.unscaledTime;
					else if (Time.unscaledTime - smEmptyWorldSince > 2f)
					{
						SquadCmdLogic.LogAlways("[SquadCmd] 检测到返回主菜单/无存活单位，自动退出上帝视角并还原相机");
						smEmptyWorldSince = -10f;
						Exit();
					}
				}
				else smEmptyWorldSince = -10f;
			}
			catch { }
		}
		if (!Active) return;
		try
		{
			if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
			if (MouseCursor.OwnsCursor) { if (!Cursor.visible) Cursor.visible = true; }
			else if (!Cursor.visible) Cursor.visible = true;
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
			cmdFlash = Ui.Tr("已继续（时间恢复）"); cmdFlashUntil = Time.unscaledTime + 2f;
			SquadCmdLogic.Log("[SquadCmd] 上帝视角：时间恢复。");
		}
		else
		{
			Time.timeScale = 0f;
			cmdFlash = Ui.Tr("已暂停（空格继续）"); cmdFlashUntil = Time.unscaledTime + 2f;
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

	/// <summary>1.4.15：相机输入让位判定。
	/// 背景：此前 HandleMove/HandleHeight/HandleDrag 完全不受 escMenuOpen 约束——
	/// 上帝视角下打开原生设置菜单后，WASD 仍在平移、滚轮仍在改高度、中键仍能旋转
	/// （玩家报"打开设置后转动滚轮视角仍然会跟着动"）。
	/// 规则（照搬 RTS 惯例，也要兼顾"菜单里还要能干活"）：
	/// · menuCapture（ESC 原生菜单）：**全部**相机输入冻结，菜单完全独占鼠标与键盘。
	/// · uiCapture（指针停在我们的面板/背包窗口/信息面板/外部 mod 面板上）：只冻结
	///   **鼠标驱动**的操作（滚轮缩放、中键旋转），键盘 WASD/QE 照常——否则光标恰好
	///   压在小队列表上会让玩家以为"相机坏了"。</summary>
	private static bool UiPointerCapture()
	{
		try
		{
			if (!IsMouseOverGui()) return false;
			// 1.4.16：附属 mod 的"手势互斥全屏"不是面板——相机（滚轮/中键）放行，点击手势照吞
			//（externalGuiBlock 仍 gate 战场手势，互斥语义不变，见 IsMouseOverGui 注释）。
			if (externalCameraPass != null && externalCameraPass()) return false;
			return true;
		}
		catch { return false; }
	}

	private static void HandleMove(float dt, bool menuCapture)
	{
		if (menuCapture) return; // 1.4.15：菜单打开期间冻结键盘平移/升降
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

	private static void HandleHeight(float dt, bool menuCapture, bool uiCapture)
	{
		if (menuCapture) return; // 1.4.15：菜单打开期间完全不让相机响应滚轮/QE
		if (!uiCapture)
		{
			float wheel = Input.mouseScrollDelta.y;
			if (Mathf.Abs(wheel) > 0.001f) camPos.y += -wheel * HeightStep * (1f + camPos.y / 150f);
		}
		if (Input.GetKey(KeyCode.E)) camPos.y += BaseSpeed * 0.7f * dt;
		if (Input.GetKey(KeyCode.Q)) camPos.y -= BaseSpeed * 0.7f * dt;
		float ground = GroundHeightAt(camPos);
		camPos.y = Mathf.Clamp(camPos.y, Mathf.Max(MinHeight, ground + 3f), MaxHeight);
	}

	/// <summary>取该点下方地面高度。1.2.7：**忽略幽灵预览**——幽灵在相机下方会把"地面"抬高，
	/// 相机高度被反复往上推 = 用户报的"摄像机在其上方会一直上升"。
	/// 1.2.10 性能：原实现用 `Physics.RaycastAll`（**每帧分配数组** + 逐命中走父链判幽灵），
	/// 是卡顿主因之一。改为单次 `Physics.Raycast`；仅当命中物是幽灵时，从该点下方再打一次
	/// （最多 2 次射线，**零分配**）。</summary>
	private static float GroundHeightAt(Vector3 p)
	{
		try
		{
			Vector3 origin = new Vector3(p.x, p.y + 200f, p.z);
			if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 500f))
			{
				Transform t = hit.collider != null ? hit.collider.transform : null;
				if (!GhostPreview.IsGhostTransform(t)) return hit.point.y;
				// 命中幽灵：从它下方再打一次（幽灵通常悬在地面上方，跳过它即可拿到真实地面）
				Vector3 o2 = new Vector3(p.x, hit.point.y - 0.3f, p.z);
				if (Physics.Raycast(o2, Vector3.down, out RaycastHit hit2, 500f)) return hit2.point.y;
			}
		}
		catch { }
		return 0f;
	}

	/// <summary>1.4.15：menuCapture（ESC 菜单）= 手势立刻作废，鼠标完全让给原生菜单；
	/// blockStart（指针在 UI 上）= 只不让**新手势**起手，已经按住的中键拖动保持到松手
	///（指针捕获惯例），避免划过面板就把视角转飞。</summary>
	private static void HandleDrag(bool menuCapture, bool blockStart)
	{
		if (menuCapture) { midDrag = false; return; } // 菜单打开：正在进行的旋转也一并结束
		bool down = Input.GetMouseButton(2);
		if (blockStart && !midDrag)
		{
			// 冻结基准点，解除遮挡后再起手不会瞬移一大段。
			lastDragX = Input.mousePosition.x; lastDragY = Input.mousePosition.y;
			return;
		}
		if (down && !midDrag) { midDrag = true; lastDragX = Input.mousePosition.x; lastDragY = Input.mousePosition.y; }
		else if (!down && midDrag) { midDrag = false; }
		if (midDrag)
		{
			float dx = Input.mousePosition.x - lastDragX, dy = Input.mousePosition.y - lastDragY;
			lastDragX = Input.mousePosition.x; lastDragY = Input.mousePosition.y;
			yaw += dx * RotSpeed; pitch -= dy * RotSpeed; pitch = Mathf.Clamp(pitch, MinPitch, MaxPitch); RecomputeRot();
		}
	}

	/// <summary>清理一次右键手势；不关闭已经显示的命令环。</summary>
	private static void ResetRightGesture(bool cancelFormationDrag = true)
	{
		// 1.2.15：拖动中遇到手势复位（异常复位/新按压/UI 命中/ESC 等）→ 必须真正结束拖动并清幽灵，
		// 否则 Formation.dragging 残留、幽灵留在场上。
		// **1.2.18 修复（阵型失效真因）**：右键松开的收尾路径**不能**走这里取消——
		// 松开时 HandleClick 先调本方法、再调 IssueFromDrag 下发阵型；此前在这里抢先
		// CancelDrag（dragging=false + 清计划）→ IssueFromDrag 直接 return → 阵型永远不下发。
		// 所以松手路径传 cancelFormationDrag=false，由 IssueFromDrag / CancelDrag 自己收尾。
		if (cancelFormationDrag && formationDragActive) { try { Formation.CancelDrag("手势复位"); } catch { } }
		rightHoldActive = false;
		rightLongPressOpened = false;
		rightGestureWheelOpen = false;
		formationDragActive = false;
		rightDownTime = -10f;
		rightDownScreenPos = Vector2.zero;
		rightDownOnUnit = false;
	}

	/// <summary>切换 RTS/FPS 或输入异常后的统一手势复位。</summary>
	private static void ResetInputState()
	{
		isDragging = false;
		midDrag = false;
		addingToSelection = false;
		ResetRightGesture();
		swallowLeftGesture = false;
	}

	private static void HandleClick()
	{
		try { HandleClickCore(); }
		catch (Exception ex)
		{
			ResetInputState();
			SquadCmdLogic.Log("[SquadCmd] 输入处理失败，已复位 RTS 手势: " + ex.Message);
		}
	}

	// 0.9.6：ESC 菜单自持状态。0.9.5 的 Pause.HavePanel() 在 RTS 下恒为 true（常驻面板被计入），
	// 把全部让位守卫锁死（鼠标失灵/设置地图打不开的根因）——改为只信自己开的菜单。
	// 0.9.11：原版 M 地图在 god 视角强制显示也为空（机制未明），已按设计在 RTS 内禁用。
	private static bool escMenuOpen;
	private static float smEmptyWorldSince = -10f; // 0.9.11：无存活生物连续计时（返回主菜单检测）


	/// <summary>1.4.11：UI（背包窗口/交互菜单/窗口按钮）**吃掉**了这次左键 → 让战场手势也吞掉这一按的剩余部分。
	/// 否则「点 ✕ 关窗」的松手落在窗口已消失的下一帧，`guiNow` 变 false → 被当成一次空地点击 → **取消选中单位**
	/// （用户实测「关闭背包后也会取消选中单位」）。</summary>
	internal static void SwallowLeftGesture() { swallowLeftGesture = true; }

	private static void HandleClickCore()
	{
		bool leftDown = Input.GetMouseButtonDown(0);
		bool leftHeld = Input.GetMouseButton(0);
		bool leftUp = Input.GetMouseButtonUp(0);
		bool rightDown = Input.GetMouseButtonDown(1);
		bool rightHeld = Input.GetMouseButton(1);
		bool rightUp = Input.GetMouseButtonUp(1);
		bool guiNow = IsMouseOverGui() || escMenuOpen; // 0.9.11：ESC 菜单打开时点击让位（地图已在 RTS 禁用）

		// 1.2.3：指令环/交互环已全部删除（右键直接下指令，其余操作在左下角信息面板），
		// 这里不再有"轮盘打开期间"的收尾分支。

		// 关环后只吞掉同一次左键的剩余部分；右键仍要正常进入状态机。
		bool suppressLeft = swallowLeftGesture;
		if (suppressLeft)
		{
			if (leftUp)
			{
				swallowLeftGesture = false;
			}
			else if (!leftHeld && !leftDown)
			{
				swallowLeftGesture = false;
				suppressLeft = false;
			}
		}

		// 右键：按下只在战场接收；松开先清状态，再发命令，避免命令异常留下锁死标志。
		if (rightDown)
		{
			ResetRightGesture();
			if (!guiNow)
			{
				SquadCmdLogic.Log("[SquadCmd] RMB↓ gui=" + guiNow + " swallow=" + swallowLeftGesture + " sel=" + SelTotal);
				rightHoldActive = true;
				rightDownTime = Time.unscaledTime;
				rightDownScreenPos = Input.mousePosition;
			rightDownOnUnit = RightPressHitsUnit(rightDownScreenPos);
			}
		}
		// 长按=阵型箭头（Formation.cs）。按在单位上默认不开（瞄准按压误触），
		// 但 1.2.5：选中只含不可移动火力点/火炮时**允许按在单位上**——用户就是从炮位上长按拉箭头的。
		if (rightHoldActive && rightHeld && !rightGestureWheelOpen
			&& !rightLongPressOpened && !guiNow
			&& SelTotal > 0 && (!rightDownOnUnit || Formation.SelectionIsFacingOnlyNow())
			&& Time.unscaledTime - rightDownTime >= RightLongPressSeconds)
		{
			rightLongPressOpened = true; // 本次右键不再等待 MouseUp 发短按命令
			if (Formation.TryBeginDrag(rightDownScreenPos, rightDownOnUnit))
			{
				formationDragActive = true; // 保持手势态直到松开；ResetRightGesture 不调用
			}
		}
		if (rightUp)
		{
			bool formation = formationDragActive;
			bool issue = rightHoldActive && !rightLongPressOpened && !rightGestureWheelOpen && !guiNow;
			Vector2 downPos = rightDownScreenPos;
			ResetRightGesture(false); // 1.2.18：松手路径不复位拖动（下一行自己下发/取消）
			if (formation)
			{
				if (!guiNow) Formation.IssueFromDrag();
				else Formation.CancelDrag("松手在 UI 上"); // 1.2.15：UI 上松手=取消（原来什么都不做 → 幽灵残留）
			}
			else if (issue)
			{
				float nowR = Time.unscaledTime;
				bool dbl = nowR - lastRightBlankClickTime < 0.6f
					&& Vector2.Distance(MouseGui(), lastRightBlankClickPos) < 40f; // 0.7.64：双击右键=快速模式
				lastRightBlankClickTime = nowR;
				lastRightBlankClickPos = MouseGui();
				if (dbl && lastMovePoint.HasValue)
				{
					// 0.7.97：双击右键=原生「前往并防守」（Squad.HoldArea，同一点第二击升级）——
					// 每个涉入原生小队一条原生命令，Mod 不再做快速移动/停滞修正编排。
					IssueNativeHoldArea(lastMovePoint.Value);
					cmdFlash = Ui.Tr("前往并防守（同一目标）"); cmdFlashUntil = Time.unscaledTime + 1.5f;
				}
				else
				{
					IssueDirectCommand(downPos);
				}
			}
		}

		if (suppressLeft) return;
		addingToSelection = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);
		// 左键按下：仅战场区域建立新选择手势。
		if (leftDown && !guiNow)
		{
			pressStart = MouseGui();
			isDragging = false;
		}
		// 按住拖出框选
		if (leftHeld && !guiNow && !isDragging && (MouseGui() - pressStart).sqrMagnitude > DragThreshold * DragThreshold)
		{
			isDragging = true;
		}
		// 松开左键：无论当前是否在 UI 上，都必须结束状态；在 UI 上松开则取消本次战场选择。
		if (leftUp)
		{
			bool wasDragging = isDragging;
			isDragging = false;
			if (guiNow) return;
			if (wasDragging) { FinishBoxSelect(); return; }
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
		}
	}

	private static Vector2 MouseGui()
	{
		return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
	}

	// 附属 mod（UniversalGeneration 等）注册的额外 GUI 遮挡区：返回 null=无遮挡。
	// IsMouseOverGui 命中该 Rect 时吞掉战场选择/指令手势，避免附属面板点击误框选。
	// 由附属 mod 反射赋值（本程序集内无赋值点，CS0649 预期内）。
#pragma warning disable CS0649
	internal static Func<Rect?> externalGuiBlock;
#pragma warning restore CS0649

	// 1.4.16：附属 mod 声明"当前的全屏互斥是拖放/放置手势，不是 UI 面板"——返回 true 时
	// 相机输入（滚轮升降/中键旋转）**不冻结**，只吞点击手势。
	// 背景：UniversalGeneration 放置/携带物品期间把 externalGuiBlock 报成全屏 Rect（防点击
	// 误触框选/指令），但同一个布尔也 gate 了 HandleHeight 滚轮与 HandleDrag 中键
	// → 用户实测"预放置时不能滚动滚轮改变视角"。全屏手势互斥不该连相机一起接管。
	// 由附属 mod 反射赋值（本程序集内无赋值点，CS0649 预期内；未赋值=行为与旧版一致）。
#pragma warning disable CS0649
	internal static Func<bool> externalCameraPass;
#pragma warning restore CS0649

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
			if (InfoPanel.WantsMouse()) return true; // 1.2.0：左下角信息面板
			if (BackpackPanel.WantsMouse()) return true; // 1.3.0：格子背包窗口（含拖拽中全屏吞手势）
			if (externalGuiBlock != null && externalGuiBlock() is Rect ex && ex.Contains(m)) return true;
		}
		catch { }
		return false;
	}

	private static Rect ControlButtonRect()
	{
		// 2.5.1：宽高/间距随 Er2Ui.Scale 自适应（原 300/36/12 写死，倍率一变就不跟手）
		float s = ER2Shared.Er2Ui.Scale;
		return new Rect((Screen.width - 300f * s) * 0.5f, 12f * s, 300f * s, 36f * s);
	}

	/// <summary>0.7.46：【分队】按钮（控制按钮右侧）。</summary>
	private static Rect SplitButtonRect()
	{
		Rect b = ControlButtonRect();
		float s = ER2Shared.Er2Ui.Scale;   // 2.5.1：自适应
		return new Rect(b.xMax + 8f * s, 12f * s, 120f * s, 36f * s);
	}

	/// <summary>0.7.58：【合并】按钮（控制按钮左侧）——把选中单位并入当前激活 RTS 组。</summary>
	private static Rect MergeButtonRect()
	{
		Rect b = ControlButtonRect();
		float s = ER2Shared.Er2Ui.Scale;   // 2.5.1：自适应
		return new Rect(b.x - (8f + 120f) * s, 12f * s, 120f * s, 36f * s);
	}
	// 0.9.0：【分散】已移入命令环（槽 7），顶栏不再单独设按钮

	/// <summary>左键单击：只负责选择/取消选择。
	/// 1.2.4（用户要求）：**非单位的可交互物也可选中**——空载具（无车组）、
	/// 可交互物品（ItemObject.CanInteract）都进"物品选择"（selectedProps），
	/// 面板显示其信息，右键可对其下"上车/进入"指令。</summary>
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
				if (veh != null)
				{
					// 友军载具：选中整车组
					if (VehicleFriendly(veh))
					{
						Squad crew = CrewOf(FirstCrew(veh));
						if (addingToSelection) { AddVehicle(crew); AddVehicleRef(veh); }
						else { SelectVehicleCrew(crew); selVehicleRefs.Clear(); AddVehicleRef(veh); }
						return;
					}
					// 1.2.5 修复：**非敌对载具一律进载具选择**（含空载具、无车组/阵营判定不出的火力点/火炮）。
					// 1.2.4 曾把它们路由到"可交互物"槽 → selVehicleRefs 为空 → 火力点既不能阵型也不能转向（回归 bug）。
					// 载具选择允许 selVehicles 为空（只有 selVehicleRefs），驾驶/转向/朝向链路都按 refs 工作。
					if (!VehicleHostile(veh))
					{
						Squad crew = CrewOf(FirstCrew(veh));
						if (addingToSelection) { AddVehicle(crew); AddVehicleRef(veh); }
						else { SelectVehicleCrew(crew); selVehicleRefs.Clear(); AddVehicleRef(veh); }
						return;
					}
				}
				Soldier sol = hit.collider.transform.GetComponentInParent<Soldier>();
				if (sol == null) sol = hit.collider.transform.GetComponent<Soldier>();
				// 1.3.1：IsAlive 访问加保护（尸体上的 interop 属性异常会吞掉整个点击）
				bool solValid = sol != null;
				bool solAlive = false;
				if (solValid) { try { solAlive = sol.IsAlive; } catch { solAlive = false; } }
				if (solValid && solAlive && FriendlyUnit(sol))
				{
					// 友军步兵：选中（Shift=追加）
					if (addingToSelection) AddInfantry(sol);
					else SelectSingle(sol);
					return;
				}
				// 1.3.2：尸体不再左键选中（用户定案：尸体=右键开背包）；左键点尸体等同空白（清选）
				// 1.2.4：可交互物品（ItemObject.CanInteract，如掉落的武器/弹药箱）→ 物品选择
				ItemObject item = null;
				try { item = hit.collider.transform.GetComponentInParent<ItemObject>(); } catch { }
				if (item == null) { try { item = hit.collider.transform.GetComponent<ItemObject>(); } catch { } }
				if (item != null)
				{
					bool canInteract = false;
					try { canInteract = item.CanInteract(); } catch { canInteract = true; }
					if (canInteract) { SelectProp(item); return; }
				}
				SquadCmdLogic.Log("[SquadCmd] LMB 未命中可选物 hit='" + hit.collider.name + "' sol=" + solValid + " item=" + (item != null));
			}
			// 敌军/空白 → 取消选择（不影响进行中的任务；停止用快捷键 V）
			// 1.4.1（用户定案）：背包窗口开着时左键空地**不清选**——保护拖拽/交互工作流；
			// 点其他单位仍会切换选择并随选关窗。
			if (BackpackPanel.HasOpenWindows) return;
			ClearSelection();
			cmdFlash = Ui.Tr("已清空选择"); cmdFlashUntil = Time.unscaledTime + 1.5f;
		}
		catch { }
	}

	// ===== 1.2.4：非单位可交互物选择（可交互物品；载具走正常载具选择）=====
	internal static ItemObject SelectedPropItem => propItem;
	private static ItemObject propItem;

	private static void SelectProp(ItemObject it)
	{
		ClearSelection();
		propItem = it;
		selFlash = Time.unscaledTime + 3f;
		string nm = ""; try { nm = it.item_id; } catch { }
		cmdFlash = Ui.Tr("已选中物品：") + (string.IsNullOrEmpty(nm) ? "?" : nm); cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.Log("[SquadCmd] 选中可交互物品 " + nm);
	}

	/// <summary>清掉可交互物选择（单位选择接管时调用）。</summary>
	internal static void ClearPropSelection()
	{
		propItem = null;
	}

	internal static bool HasPropSelection => propItem != null;

	// ===== 1.3.2：尸体交互改右键（用户定案：尸体不可左键选中；选中单位后右键尸体=开背包，
	//       右键地上物品=拾取）。尸体选择槽已删除，仅保留 NearestDeadSoldier 供右键判定。=====

	/// <summary>1.4.5：按命中点就近找**徒步友军**（右键点偏容错——远景下"右键他"经常点空）。
	/// 只查 Creature.allCreatures 静态表，禁 FindObjectsOfType。</summary>
	private static Soldier NearestFriendlySoldier(Vector3 pos, float radius)
	{
		Soldier best = null;
		float bestD = radius * radius;
		try
		{
			var all = Creature.allCreatures;
			if (all == null) return null;
			for (int i = 0; i < all.Count; i++)
			{
				Creature c = all[i];
				if (c == null) continue;
				Soldier s = null; try { s = c.TryCast<Soldier>(); } catch { }
				if (s == null) continue;
				bool alive = true; try { alive = s.IsAlive; } catch { alive = true; }
				if (!alive) continue;
				try { if (s.GetComponentInParent<Vehicle>() != null) continue; } catch { } // 车内兵不走会合（那是补员上车）
				if (!FriendlyUnit(s)) continue;
				Vector3 p;
				try { if (s.transform == null) continue; p = s.transform.position; } catch { continue; }
				float d = (p - pos).sqrMagnitude;
				if (d < bestD) { bestD = d; best = s; }
			}
		}
		catch { }
		return best;
	}

	/// <summary>1.4.10：最近的**徒步**友军（`NearestFriendlySoldier` 已排除车内乘员、只认同阵营存活兵）。
	/// 背包派兵用：选中的单位全在载具里时改派他，否则那个背包永远打不开。</summary>
	internal static Soldier NearestOnFootFriendly(Vector3 pos, float radius) => NearestFriendlySoldier(pos, radius);

	/// <summary>1.3.1：按命中点就近找阵亡士兵（右键尸体判定兜底）。只查 Creature.allCreatures 静态表，禁 FindObjectsOfType。</summary>
	private static Soldier NearestDeadSoldier(Vector3 pos, float radius)
	{
		Soldier best = null;
		float bestD = radius * radius;
		try
		{
			var all = Creature.allCreatures;
			if (all == null) return null;
			for (int i = 0; i < all.Count; i++)
			{
				Creature c = all[i];
				if (c == null) continue;
				Soldier s = null; try { s = c.TryCast<Soldier>(); } catch { }
				if (s == null) continue;
				bool alive = true; try { alive = s.IsAlive; } catch { alive = true; }
				if (alive) continue;
				Vector3 p;
				try { if (s.transform == null) continue; p = s.transform.position; } catch { continue; }
				float d = (p - pos).sqrMagnitude;
				if (d < bestD) { bestD = d; best = s; }
			}
		}
		catch { }
		return best;
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
					cmdFlash = Ui.Tr("双击需命中友军单位"); cmdFlashUntil = Time.unscaledTime + 2f;
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
						cmdFlash = string.Format(Ui.Tr("已选中整队 {0} 名步兵"), virtualUnits.Count); cmdFlashUntil = Time.unscaledTime + 2f;
					}
					else
					{
						SelectVehicleCrew(sq);
						cmdFlash = Ui.Tr("已选中整车组"); cmdFlashUntil = Time.unscaledTime + 2f;
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
				cmdFlash = Ui.Tr("未框到可选单位（全空或载具无车组）"); cmdFlashUntil = Time.unscaledTime + 2f;
				return;
			}
			selFlash = Time.unscaledTime + 3f;
			cmdFlash = string.Format(Ui.Tr("临时指挥：步兵 {0} + 载具 {1}"), SelInfantryCount(), selVehicles.Count) + (addingToSelection ? Ui.Tr("（追加）") : ""); cmdFlashUntil = Time.unscaledTime + 3f;
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

	/// <summary>1.4.14 性能：主相机缓存。MainCam 每帧被调用约 5 次（HandleMove/HandleHeight/ApplyCam/
	/// LateApply/OnPreRenderCamera），每次都走一次 interop 属性读 + 兜底 `Camera.main`
	///（后者内部是 FindGameObjectWithTag，很贵）。缓存 0.5s，且只在拿到有效值时缓存
	///（相机尚未就绪时保持重试，不把 null 缓存进去）。</summary>
	private static Camera mainCamCache;
	private static float mainCamNext = -10f;

	internal static Camera MainCam()
	{
		float now = Time.unscaledTime;
		if (mainCamCache != null && now < mainCamNext && mainCamCache.Pointer != IntPtr.Zero) return mainCamCache;
		mainCamNext = now + 0.5f;
		try { Camera c = ResourcesManager.mainCamera; if (c != null) { mainCamCache = c; return c; } } catch { }
		try { Camera m = Camera.main; if (m != null) { mainCamCache = m; return m; } } catch { }
		mainCamCache = null;
		return null;
	}

	/// <summary>1.4.14：相机重建（切场景等）时立刻失效缓存，避免多等半秒。</summary>
	internal static void InvalidateMainCam() { mainCamCache = null; mainCamNext = -10f; }

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

	// ===== 0.9.0：统一 UI 主题（cfg "UI" 节可自定义）2.5.0 默认改中性灰 =====
	private static Color uiBase = new Color(0.12f, 0.12f, 0.12f, 0.90f);      // HUD 按钮底板
	private static Color uiHover = new Color(0.23f, 0.23f, 0.23f, 0.95f);     // 悬停/选中
	private static Color uiText = new Color(0.91f, 0.91f, 0.91f, 1f);         // 文字/描边

	/// <summary>
	/// 1.4.33：**cfg 默认值迁移**。
	/// BepInEx 的 cfg 文件一旦生成，**不会**因为代码里默认值改变而更新——
	/// 所以老用户本地的 `colorBase` / `colorHover` 仍是很早以前的旧值（如军绿 `#0E1C0EB4`），
	/// 表现为"其他 UI 都换成新主题了，只有 HUD 按钮/小队列表还是军绿"（用户截图实证："为什么UI没改完"）。
	/// 做法：**仅当值命中历史默认值集合**（说明玩家从未自定义过）时才写入新默认值；
	/// 玩家手动改过的值一律不动（不能覆盖用户意图）。
	/// </summary>
	private static void MigrateLegacyUiCfg()
	{
		try
		{
			string[] legacyBase = { "#0E1C0EB4", "#1E1E1EE6", "#262A30F0", "#201812D9", "#0F0B08EA", "#101010EE", "#0A0A0DEE", "#000000B8" };
			string[] legacyHover = { "#2E4A2EE0", "#3A3A3AF2", "#454E59F5", "#44352AE6", "#2A2017F0", "#2A2A31F5", "#3A3A3AEE" };
			foreach (string s in legacyBase)
				if (string.Equals(Plugin.uiColorBase.Value, s, System.StringComparison.OrdinalIgnoreCase))
				{
					Plugin.uiColorBase.Value = "#00000080";
					SquadCmdLogic.LogAlways("[SquadCmd] cfg 颜色迁移 colorBase " + s + " → #00000080");
					break;
				}
			foreach (string s in legacyHover)
				if (string.Equals(Plugin.uiColorHover.Value, s, System.StringComparison.OrdinalIgnoreCase))
				{
					Plugin.uiColorHover.Value = "#3A3A3AEE";
			string[] legacyText = { "#E8E8E8", "#F1EBE2", "#F0F0F2" };
			foreach (string t in legacyText)
				if (string.Equals(Plugin.uiColorText.Value, t, System.StringComparison.OrdinalIgnoreCase))
				{
					Plugin.uiColorText.Value = "#FFFFFF";
					SquadCmdLogic.LogAlways("[SquadCmd] cfg migration colorText " + t + " -> #FFFFFF");
					break;
				}
					SquadCmdLogic.LogAlways("[SquadCmd] cfg 颜色迁移 colorHover " + s + " → #3A3A3AEE");
					break;
				}
			// 面板不透明度：历史默认 0.85 → 0.72 → 0.50（每次改默认都要把旧值加进迁移链）
			float[] legacyAlpha = { 0.85f, 0.72f };
			foreach (float la in legacyAlpha)
				if (Plugin.uiPanelAlpha != null && Mathf.Abs(Plugin.uiPanelAlpha.Value - la) < 0.001f
					&& Mathf.Abs(ER2Shared.Er2Ui.PanelAlpha - 0.50f) > 0.001f)
				{
					Plugin.uiPanelAlpha.Value = 0.50f;
					SquadCmdLogic.LogAlways("[SquadCmd] cfg 迁移 uiPanelAlpha " + la + " → 0.50");
					break;
				}
		}
		catch (System.Exception ex) { SquadCmdLogic.LogWarning("[SquadCmd] cfg 迁移异常: " + ex.Message); }
	}

	internal static void ApplyUiTheme()
	{
		MigrateLegacyUiCfg();   // 1.4.33：先迁移旧默认值，再读 cfg
		uiBase = ParseThemeColor(Plugin.uiColorBase.Value, uiBase);
		uiHover = ParseThemeColor(Plugin.uiColorHover.Value, uiHover);
		// 1.4.36：**文字/底/悬停色源统一到 Er2Ui 令牌**——不再走 cfg。
		// 原因：cfg 文件不随代码默认值更新（陷阱 109），用户本地的 colorText 还是旧灰值，
		// 表现为"文字还是灰色的"；且两个 mod 的文字色也因此不一致（用户："包括附属mod"）。
		// `uiColorText`/`uiColorBase`/`uiColorHover` 三个 cfg 保留但不再被读取（见 Plugin.cs 注释）。
		uiText = ER2Shared.Er2Ui.Text;
	}

	private static Color ParseThemeColor(string hex, Color fallback)
	{
		try
		{
			string s = (hex ?? "").Trim();
			if (s.Length == 0) return fallback;
			if (!s.StartsWith("#")) s = "#" + s;
			if (ColorUtility.TryParseHtmlString(s, out Color c)) return c;
		}
		catch { }
		return fallback;
	}

	/// <summary>0.9.0：统一按钮绘制——主题色底板+描边+居中文字。0.9.4：禁用态高对比（近黑底+暗淡文字）。</summary>
	/// <summary>
	/// 1.4.32：**HUD 统一底板**——就是玩家说"喜欢"的那条底部提示条的背景
	///（`Er2Ui.Scrim` = 纯黑 72%）+ 皮革纹理 + 一圈 `PanelBorder` 描边。
	/// HUD 各块（左下单位信息 / 右下小队列表 / 底部提示条）全部共用，保证整个 HUD 是一套视觉。
	/// </summary>
	internal static void DrawHudPlate(Rect r)
	{
		if (r.width <= 0f || r.height <= 0f) return;
		try
		{
			ER2Shared.Er2Ui.Fill(r, ER2Shared.Er2Ui.Scrim);
			ER2Shared.Er2Ui.Leather(r, 0.08f);
			ER2Shared.Er2Ui.Frame(r, ER2Shared.Er2Ui.PanelBorder, Mathf.Max(1f, ER2Shared.Er2Ui.Scale));
		}
		catch { }
	}

	private static void DrawUiButton(Rect r, string label, bool enabled, bool hover)
	{
		Color fill = enabled ? (hover ? uiHover : uiBase) : ER2Shared.Er2Ui.SurfaceDisabled;
		Color txt = enabled ? uiText : ER2Shared.Er2Ui.TextDisabled;
		GUI.color = fill;
		GUI.DrawTexture(r, Texture2D.whiteTexture);
		// 1.4.31：描边改用共享令牌 PanelBorder——原来用文字色，与 UniGen 面板的描边不是同一套，
		// 两个 mod 摆在一起会有"两种边框"。
		GUI.color = ER2Shared.Er2Ui.PanelBorder;
		// 1.4.22：描边宽度随倍率（原 1.5f 硬编码——缩放后描边过细，按钮边界读不出）
		float bw = Mathf.Max(1f, 1.5f * ER2Shared.Er2Ui.Scale);
		GUI.DrawTexture(new Rect(r.x, r.y, r.width, bw), Texture2D.whiteTexture);
		GUI.DrawTexture(new Rect(r.x, r.yMax - bw, r.width, bw), Texture2D.whiteTexture);
		GUI.DrawTexture(new Rect(r.x, r.y, bw, r.height), Texture2D.whiteTexture);
		GUI.DrawTexture(new Rect(r.xMax - bw, r.y, bw, r.height), Texture2D.whiteTexture);
		GUI.Label(r, label, SquadCmdLogic.ButtonStyle());
		GUI.color = Color.white;
	}

	// ===== 0.9.0：3D 场景标记驱动（友军脚环/选中环/集火环/移动目标环/名签） =====
	private static readonly List<Soldier> smFriendly = new List<Soldier>();
	private static readonly List<Vehicle> smFriendlyVeh = new List<Vehicle>();
	private static float smFriendlyNext = -10f;
	private static readonly HashSet<long> smSelected = new HashSet<long>();
	/// <summary>1.4.14 性能：选中集里**在载具内**的成员指针（与 markerCache 同批 0.2s 刷新）。
	/// 只给"是否为车内乘员"这一类每帧判定用，避免每帧逐单位 GetComponentInParent。</summary>
	private static readonly HashSet<long> smSelectedInVehicle = new HashSet<long>();
	private const int SceneMarkerCap = 200;
	private const int RouteLineCap = 30; // 1.0.3：路线线数量上限（步兵），载具另加 10

	private static void SceneMarkersFrame()
	{
		float t = Time.unscaledTime;
		// 2.5.0：原来这里只有一个全局 pulse 供所有标记共用 → 同频同相一起呼吸（机械感）。
		// 现在改为按 key 哈希错相，见 PulseOf(key, t)。t 仍传入，避免每个调用点重复取时间。
		// 2.5.0：本帧相机距离——供所有标记做线宽补偿（每帧只算一次）。
		float camDist = MarkerCamDist();
		// 选中集（0.2s 缓存，含车内乘员——车组成员由载具环覆盖，不单独画）
		// 1.4.14 性能：smSelected 与 markerCache 同步刷新（原每帧 Clear + 逐单位 Add，
		// 且 GetComponentInParent 判定也每帧做）——两者生命周期完全一致，没必要分开算。
		if (t > markerCacheUntil)
		{
			markerCacheUntil = t + 0.2f;
			markerCache = GetCommandUnits();
			smSelected.Clear();
			smSelectedInVehicle.Clear();
			for (int i = 0; i < markerCache.Count; i++)
			{
				Soldier s = markerCache[i];
				if (s == null) continue;
				try
				{
					long k = (long)s.Pointer;
					smSelected.Add(k);
					if (!IsOnFoot(s)) smSelectedInVehicle.Add(k);
				}
				catch { }
			}
		}

		// 友军脚环 + 选中角括号：仅 RTS 显示（FPS 第一人称满屏脚环会干扰视野）
		if (Active && MarkersAllEnabled)
		{
			if (t > smFriendlyNext)
			{
				smFriendlyNext = t + 1f;
				RebuildFriendlyCache();
			}
			Color dim = ER2Shared.Er2Ui.WFriendly;      // 2.5.0：白 @0.28（原 0.62 灰 @0.55）
			Color dimVeh = ER2Shared.Er2Ui.WFriendlyVeh; // 2.5.0：白 @0.34（载具比步兵略亮）
			Color selWhite = ER2Shared.Er2Ui.WSelected;  // 2.5.0：白 @0.92
			int drawn = 0;
			// cfg showFriendlyRing：关掉则整块友军脚环不画（选中角标仍可单独开）
			if (ShowFriendlyRing)
			{
				foreach (Soldier s in smFriendly)
				{
					if (drawn >= SceneMarkerCap) break;
					try
					{
						if (s == null || !s.IsAlive || s.transform == null) continue;
						long k = (long)s.Pointer;
						if (smSelected.Contains(k)) continue; // 选中角括号更醒目，不叠画
						SceneMarkers.Ring("F" + k, s.transform.position + Vector3.up * 0.15f, UnitRingRadius(s) * MarkerScaleMul, dim,
							ER2Shared.Er2Ui.LineWidth(0.032f, camDist) * MarkerWidthMul, true);
						drawn++;
					}
					catch { }
				}
				foreach (Vehicle v in smFriendlyVeh)
				{
					if (drawn >= SceneMarkerCap) break;
					try
					{
						if (v == null || v.transform == null) continue;
						long k = (long)v.Pointer;
						if (smSelected.Contains(k)) continue;
						SceneMarkers.Ring("FV" + k, v.transform.position + Vector3.up * 0.15f, VehicleRingRadius(v) * MarkerScaleMul, dimVeh,
							ER2Shared.Er2Ui.LineWidth(0.050f, camDist) * MarkerWidthMul, true);
						drawn++;
					}
					catch { }
				}
			}
			// 选中步兵：灰白角括号 + 呼吸脉动（载具上面已画）
			// 1.4.14 性能：车内乘员判定用 smSelected 同批算好的 smSelectedFoot 集合
			//（原每帧对每个选中兵做一次 GetComponentInParent<Vehicle> interop 调用）。
			if (ShowSelectedBracket)
			{
				foreach (Soldier s in markerCache)
				{
					try
					{
						if (s == null || !s.IsAlive || s.transform == null) continue;
						if (smSelectedInVehicle.Contains((long)s.Pointer)) continue;
						string key = "S" + (long)s.Pointer;
						// 2.5.0：脉动相位按 key 错开（原来所有标记共用同一全局 pulse，同频同相）
						float pl = PulseOf(key, t);
						SceneMarkers.Bracket(key, s.transform.position + Vector3.up * 0.15f, UnitRingRadius(s) * 1.3f * pl, selWhite,
							ER2Shared.Er2Ui.LineWidth(0.042f, camDist) * MarkerWidthMul, true);
					}
					catch { }
				}
				for (int vi = 0; vi < selVehicleRefs.Count; vi++)
				{
					Vehicle v = selVehicleRefs[vi]; // 1.4.14 性能：原 new List<Vehicle>(selVehicleRefs) 每帧分配
					try
					{
						if (v == null || v.transform == null) continue;
						string vkey = "SV" + (long)v.Pointer;
						SceneMarkers.Bracket(vkey, v.transform.position + Vector3.up * 0.15f, VehicleRingRadius(v) * PulseOf(vkey, t), selWhite,
							ER2Shared.Er2Ui.LineWidth(0.060f, camDist) * MarkerWidthMul, true);
					}
					catch { }
				}
			}
		}
		else if (MarkersAllEnabled)
		{
			RebuildFriendlyCacheIfStale(t); // 非 RTS 也要维持缓存新鲜，切回 RTS 时不闪
		}

		// 集火环 + 名签：RTS/FPS 都显示（任务跨视角持续）
		try
		{
			var m = mark;
			if (m != null && m.Active && MarkersAllEnabled && ShowFocusRing)
			{
				// 2.5.0：默认灰黑（白 @0.92）；cfg markerColorMode=Semantic 时回红/橙
				Color mc = m.Downgraded ? ER2Shared.Er2Ui.MarkerFocusDown : ER2Shared.Er2Ui.MarkerFocus;
				string mkey = "MK";
				SceneMarkers.Ring(mkey, m.Position + Vector3.up * 0.12f, 1.5f * PulseOf(mkey, t) * MarkerScaleMul, mc,
					ER2Shared.Er2Ui.LineWidth(0.065f, camDist) * MarkerWidthMul, true, MarkerThroughWall);
				if (MarkerNamePlates)
					SceneMarkers.Label("MKN", m.Position + Vector3.up * 2.6f, "⚔ " + (m.Downgraded ? Ui.Tr("[降级] ") : "") + m.Name, mc, true);
			}
		}
		catch { }


		// 1.2.1：目标点标记与行进连线**同源**（同时出现、同时隐藏）——用户反馈二者错开很乱。
		// 判据 = 标点未过期 或 移动观察任务进行中 或 登车任务进行中；ClearMoveObservation 会同时清 hasCmdTarget。
		bool moveViz = hasCmdTarget && (t < cmdTargetUntil || obsUnits.Count > 0 || obsVehicles.Count > 0 || pendingBoardVeh != null);

		// 移动目标点：RTS/FPS 都显示——0.9.2 黄色小圈 + 中心圆点（不再是孤零零的大圈）
		try
		{
			if (moveViz && ShowMoveTarget && MarkersAllEnabled)
			{
				// 2.5.0：白 @0.85（原琥珀黄）；形状靠"小圈 + 中心点"保持辨识度，不靠色相
				Color moveC = ER2Shared.Er2Ui.WMove;
				string mtKey = "MT";
				SceneMarkers.Ring(mtKey, cmdTarget + Vector3.up * 0.1f, 0.45f * PulseOf(mtKey, t) * MarkerScaleMul, moveC,
					ER2Shared.Er2Ui.LineWidth(0.042f, camDist) * MarkerWidthMul, true);
				SceneMarkers.Dot("MTD", cmdTarget + Vector3.up * 0.1f, 0.11f * PulseOf(mtKey, t) * MarkerScaleMul, moveC, true);
			}
		}
		catch { }

		// 1.0.3：移动路线标识——观察任务存续期间，每个行进单位/载具 → 目标点灰色半透明虚线
		//（含双击「前往并防守」的 routeOnly 登记；45s 观察窗到期或全员到位自动消失）1.0.5：减细
		// 1.2.1：改为只对【当前选中】的单位显示——用户反馈：选择已清空后连线仍挂着太杂乱
		try
		{
			if (moveViz && ShowPathLines && MarkersAllEnabled && (obsUnits.Count > 0 || obsVehicles.Count > 0))
			{
				// 2.5.0：行进路线 = 白 @0.26 **长虚线**（DashLong）——与登车线（短虚线 @0.48）区分开。
				// 原 pathC 与 boardC 字面量完全相同（都是 0.7,0.7,0.7,0.4），两种语义撞色，已修。
				Color pathC = ER2Shared.Er2Ui.WPath;
				int n = 0;
				for (int i = 0; i < obsUnits.Count && n < RouteLineCap; i++, n++)
				{
					Soldier s = obsUnits[i];
					try { if (s != null && s.transform != null && s.IsAlive && IsSelectedUnit(s)) SceneMarkers.Line("PL" + n, s.transform.position + Vector3.up * 0.9f, obsTarget + Vector3.up * 0.3f, pathC, ER2Shared.Er2Ui.LineWidth(0.030f, camDist) * MarkerWidthMul, true, false); } catch { }
				}
				for (int i = 0; i < obsVehicles.Count && n < RouteLineCap + 10; i++, n++)
				{
					Vehicle v = obsVehicles[i];
					try { if (v != null && v.transform != null && IsSelectedVehicle(v)) SceneMarkers.Line("PL" + n, v.transform.position + Vector3.up * 1.2f, obsTarget + Vector3.up * 0.3f, pathC, ER2Shared.Er2Ui.LineWidth(0.038f, camDist) * MarkerWidthMul, true, false); } catch { }
				}
			}
		}
		catch { }

		// 1.0.5：登车路线——登车 pending 期间，每个步行登车单位 → 目标载具实时位置（1.2.1：同样只在选中时显示）
		try
		{
			if (moveViz && ShowPathLines && MarkersAllEnabled && pendingBoardVeh != null && pendingBoardUnits != null && pendingBoardVeh.transform != null)
			{
				// 2.5.0：登车线 = 白 @0.48 **短虚线**（DashShort）——与行进路线（长虚线 @0.26）区分
				Color boardC = ER2Shared.Er2Ui.WBoard;
				int n = 0;
				for (int i = 0; i < pendingBoardUnits.Count && n < RouteLineCap; i++, n++)
				{
					Soldier s = pendingBoardUnits[i];
					try { if (s != null && s.transform != null && s.IsAlive && IsSelectedUnit(s)) SceneMarkers.Line("PB" + n, s.transform.position + Vector3.up * 0.9f, pendingBoardVeh.transform.position + Vector3.up * 1.0f, boardC, ER2Shared.Er2Ui.LineWidth(0.038f, camDist) * MarkerWidthMul, true, true); } catch { }
				}
			}
		}
		catch { }

		// 1.2.0：阵型拖动标记（箭头+阵型线+落点标记；仅 RTS 拖动中显示；EndFrame 前刷新，未刷新自动隐藏）
		if (Active && formationDragActive && ShowFormationMarkers && MarkersAllEnabled)
		{
			try { Formation.DrawDragMarkers(); } catch { }
		}

		SceneMarkers.EndFrame();
	}

	// ===== 2.5.0：视觉 cfg 的运行时快照 + 每帧小工具 =====
	// Plugin 里是 ConfigEntry（热重载），这里缓存成普通静态字段避免每帧 .Value 读字典。
	// 由 Plugin 的 SettingChanged → ApplyMarkerConfig() 统一刷新。
	internal static bool MarkersAllEnabled = true;   // markersEnabled 总开关
	internal static bool ShowFriendlyRing = true;
	internal static bool ShowSelectedBracket = true;
	internal static bool ShowFocusRing = true;
	internal static bool ShowMoveTarget = true;
	internal static bool ShowPathLines = true;
	internal static bool ShowFormationMarkers = true;
	internal static bool MarkerPulseOn = true;
	internal static float MarkerScaleMul = 1f;       // markerScale（半径总倍率）
	internal static float MarkerWidthMul = 1f;       // markerLineWidth（线宽总倍率）
	internal static bool MarkerThroughWall = false;  // markerThroughWall（穿墙，二期）
	internal static bool MarkerNamePlates = true;    // showNamePlates

	/// <summary>2.5.0：非 RTS 视角下也保持友军缓存新鲜（1s 节流），切回 RTS 时不闪一帧空标记。</summary>
	private static void RebuildFriendlyCacheIfStale(float t)
	{
		if (t > smFriendlyNext)
		{
			smFriendlyNext = t + 1f;
			RebuildFriendlyCache();
		}
	}

	/// <summary>
	/// 2.5.0：把 Plugin 的视觉 cfg 刷新到本类的静态快照。Plugin 启动与每个 SettingChanged 各调一次。
	/// **必须对所有 cfg 都赋值**——漏一个就会出现"改了没反应"，且这种 bug 只在运行时显现。
	/// </summary>
	internal static void ApplyMarkerConfig()
	{
		try
		{
			MarkersAllEnabled = Plugin.markersEnabled.Value;
			ShowFriendlyRing = Plugin.showFriendlyRing.Value;
			ShowSelectedBracket = Plugin.showSelectedBracket.Value;
			ShowFocusRing = Plugin.showFocusRing.Value;
			ShowMoveTarget = Plugin.showMoveTarget.Value;
			ShowPathLines = Plugin.showPathLines.Value;
			ShowFormationMarkers = Plugin.showFormationMarkers.Value;
			MarkerPulseOn = Plugin.markerPulse.Value;
			MarkerScaleMul = Plugin.markerScale.Value;
			MarkerWidthMul = Plugin.markerLineWidth.Value;
			MarkerThroughWall = Plugin.markerThroughWall.Value;
			MarkerNamePlates = Plugin.showNamePlates.Value;

			ER2Shared.Er2Ui.SetMono(Plugin.uiMono.Value);
			ER2Shared.Er2Ui.SetMarkerColorMode(Plugin.markerColorMode.Value);
		}
		catch (Exception ex)
		{
			// 失败必须可观测（工作区准则）：不加门控
			SquadCmdLogic.LogWarning("[Markers] ApplyMarkerConfig 失败: " + ex.Message);
		}
	}

	/// <summary>相机到世界原点的粗略距离——用于线宽距离补偿。取相机自身高度 ∝ 观察距离，够用且零分配。</summary>
	private static float markerGroundDistCache = -1f;
	private static float markerGroundDistNext = -10f;

	/// <summary>
	/// 1.4.22：相机到**视线-地面交点**的距离（每帧一次，0.1s 缓存足够）。
	/// 供 MarkerCamDist 与阵型标记共用——两处必须同一份语义（陷阱 78：单一数据源）。
	/// </summary>
	internal static float CameraGroundDist()
	{
		float now = Time.unscaledTime;
		if (markerGroundDistCache > 0f && now < markerGroundDistNext) return markerGroundDistCache;
		float d = 30f;
		try
		{
			Camera cam = MainCam();
			if (cam != null)
			{
				bool got = false;
				try
				{
					Ray ray = new Ray(cam.transform.position, cam.transform.forward);
					if (Physics.Raycast(ray, out RaycastHit hit, 3000f))
					{
						d = Vector3.Distance(cam.transform.position, hit.point);
						got = true;
					}
				}
				catch { }
				if (!got)
					d = Mathf.Max(cam.transform.position.y, 2f); // 看天兜底：用高度近似
			}
		}
		catch { }
		d = Mathf.Clamp(d, 1f, 500f);
		markerGroundDistCache = d;
		markerGroundDistNext = now + 0.1f;
		return d;
	}

	private static float MarkerCamDist()
	{
		// 1.4.22 **关键修复**：原实现 `cam.transform.position.magnitude` 是相机到**世界原点**的距离！
		// ER2 地图原点离战区可达数百米，GodView 每帧拿到的 dist 全是错的大数。
		// 旧版有 Clamp(0.6, 2.5) 掩盖；1.4.21 改纯线性像素换算后直接爆表——
		// 实测环被 ~0.4m 粗线填成实心圆盘、选中角标糊成粗 X（用户截图实证）。
		// 现在取"相机到视线-地面交点"的真实距离（CameraGroundDist）。
		return CameraGroundDist();
	}

	/// <summary>按 key 错相的脉动值。markerPulse 关掉时恒为 1（不呼吸）。</summary>
	private static float PulseOf(string key, float t)
	{
		if (!MarkerPulseOn) return 1f;
		return ER2Shared.Er2Ui.Pulse(key, t) * MarkerScaleMul;
	}

	private static float VehicleRingRadius(Vehicle v)
	{
		try
		{
			Collider col = v.GetComponent<Collider>();
			if (col == null) col = v.GetComponentInChildren<Collider>();
			if (col != null)
			{
				Vector3 e = col.bounds.extents;
				return Mathf.Clamp(Mathf.Max(e.x, e.z) + 0.3f, 1.6f, 4.2f);
			}
		}
		catch { }
		return 3f;
	}

	/// <summary>0.9.4：按单位碰撞体尺寸取环半径（步兵间也有大小区分）。</summary>
	private static float UnitRingRadius(Soldier s)
	{
		try
		{
			Collider col = s.GetComponent<Collider>();
			if (col == null) col = s.GetComponentInChildren<Collider>();
			if (col != null)
			{
				Vector3 e = col.bounds.extents;
				return Mathf.Clamp(Mathf.Max(e.x, e.z) + 0.25f, 0.35f, 1.1f);
			}
		}
		catch { }
		return 0.5f;
	}

	private static void RebuildFriendlyCache()
	{
		smFriendly.Clear();
		smFriendlyVeh.Clear();
		try
		{
			foreach (Squad sq in SquadCmdLogic.GetAllFriendlySquads())
			{
				if (sq == null) continue;
				Vehicle vc = null;
				try { vc = VehicleOfCrew(sq); } catch { }
				if (vc != null && vc.transform != null) { smFriendlyVeh.Add(vc); continue; }
				int c = 0;
				try { c = sq.CountMembers; } catch { }
				for (int i = 0; i < c && smFriendly.Count < SceneMarkerCap; i++)
				{
					Soldier m = null;
					try { m = sq.GetMemberClamped(i); } catch { }
					if (m != null && m.IsAlive) smFriendly.Add(m);
				}
			}
		}
		catch { }
	}

	/// <summary>0.9.1：无背板文字——双描影保证地形上的可读性。</summary>
	private static void DrawShadowLabel(Rect r, string text, GUIStyle st, Color c)
	{
		Color sh = new Color(0f, 0f, 0f, 0.85f);
		GUI.color = sh;
		GUI.Label(new Rect(r.x + 1.5f, r.y + 1.5f, r.width, r.height), text, st);
		GUI.color = c;
		GUI.Label(r, text, st);
		GUI.color = Color.white;
	}

	internal static void DrawHud()
	{
		if (!Active) return;
		if (escMenuOpen) return; // 0.9.7：ESC 菜单打开期间隐藏全部我方 IMGUI，不遮挡原生设置界面
		try
		{
			// 2.5.1：**每帧唯一的自适应入口**——跟游戏原生 UI 倍率（ResourcesManager.ResolutionMult）。
			// 必须放在最前面：下面所有尺寸都读 Er2Ui.Scale，先刷新再画，否则会慢一帧且首帧用 1.0。
			ER2Shared.Er2Ui.AutoScale();
			float s = ER2Shared.Er2Ui.Scale;

			GUIStyle st = SquadCmdLogic.HudStyle();
			if (st == null) return;
			Camera cam = MainCam();
			string info = cam != null ? Ui.Tr("  高度 ") + cam.transform.position.y.ToString("0") + "m" : "";

			// 底部指令提示（1.2.0：快捷键化+阵型箭头）
			string hint = Ui.Tr("WASD 移动    滚轮 缩放    中键 旋转    Q/E 升降    │    左键 选择/框选    右键 指令    长按拖动 阵型    │    Z/X/C 站/蹲/趴    V 停止    B 停火    N 掩体    M 集合    F 分散    │    空格 暂停    ESC 设置") + info;
			GUIStyle hs = SquadCmdLogic.HudStyleSmall();
			// 2.5.0：底部提示条底色去掉绿色调（原来 0.03,0.06,0.03 是军绿 UI 的一部分）
			// 2.5.1：宽度走 ScreenFit（原来硬编码 1400px，在 1366 宽的屏上直接溢出到屏幕外）
			float hintW = ER2Shared.Er2Ui.ScreenFit(1400f);
			float hintH = 22f * s;
			float hintX = (Screen.width - hintW) * 0.5f;
			float hintY = Screen.height - 30f * s;
			// 1.4.32：提示条 = HUD 统一底板（它本来就是玩家指定的"基准背景"）
			// 1.4.36：与左下信息面板互斥（用户："不希望信息栏与下面的提示在竖轴上同时存在"）——
			// 有选中时信息面板占这块竖向空间，提示条整条让位（底板 + 文字都不画）。
			if (!InfoPanel.Visible)
			{
				DrawHudPlate(new Rect(hintX, hintY, hintW, hintH));
				GUI.Label(new Rect(hintX, hintY - 1f * s, hintW, hintH), hint, hs);
			}

			// 左上角：暂停 + 选择信息
			// 1.2.10 性能：此处原每帧调 PruneSelection()，但 OnGUI 每帧有多次事件（Layout/Repaint），
			// 每次都遍历小队成员；而 Tick（Update，先于 OnGUI）已经清理过一次 → 这里去掉。
			string status = "";
			if (Paused) status = Ui.Tr("⏸ 已暂停（空格继续）");
			if (HasSelection)
			{
				string sel = Ui.Tr("步兵 ") + SelInfantryCount() + Ui.Tr(" + 载具 ") + selVehicles.Count;
				status = status == "" ? Ui.Tr("已选 ") + sel : (status + "  |  " + Ui.Tr("已选 ") + sel);
			}
			if (status != "")
			{
				DrawShadowLabel(new Rect(14f * s, 9f * s, 400f * s, 22f * s), status, st, Paused ? ER2Shared.Er2Ui.Warn : uiText);
			}

			// 命令反馈
			if (cmdFlash != "" && Time.unscaledTime < cmdFlashUntil)
			{
				DrawShadowLabel(new Rect(14f * s, 37f * s, 700f * s, 22f * s), cmdFlash, st, uiText);
			}

			// 0.7.99：移动完成度进度行（纯观察统计，ObsMoveTick 维护）
			float hudY = 65f * s;
			if (ObsTotal > 0)
			{
				DrawShadowLabel(new Rect(14f * s, hudY, 320f * s, 22f * s), Ui.Tr("移动 → ") + ObsArrived + "/" + ObsTotal + Ui.Tr(" 已到位"), st, uiHover);
				hudY += 24f * s;
			}


			// 顶部控制按钮（0.9.0：统一主题色，文字居中；分散已移入命令环）
			if (HasSelection)
			{
				Rect btn = ControlButtonRect();
				bool hover = btn.Contains(Event.current.mousePosition);
				DrawUiButton(btn, Ui.Tr("控制该小队"), true, hover);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && hover)
				{
					Event.current.Use();
					TakeControlSelected();
					return;
				}
				Rect sbtn = SplitButtonRect();
				bool shover = sbtn.Contains(Event.current.mousePosition);
				DrawUiButton(sbtn, Ui.Tr("分队"), true, shover);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && shover)
				{
					Event.current.Use();
					SplitSelected();
					return;
				}
				Rect mbtn = MergeButtonRect();
				bool mhover = mbtn.Contains(Event.current.mousePosition);
				DrawUiButton(mbtn, Ui.Tr("合并"), true, mhover);
				if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && mhover)
				{
					Event.current.Use();
					MergeSelectedToRts();
					return;
				}
			}

			// 0.9.0：集火标记/移动目标/选中标记/友军脚环已全部改为 3D 场景标记（SceneMarkers，
			// 由 Tick 持久段每帧驱动，RTS/FPS 均显示），这里不再做屏幕投影绘制。

			// 框选矩形（0.9.0：主题色）
			if (isDragging)
			{
				Rect sel = GetScreenRect(pressStart, MouseGui());
				GUI.color = new Color(uiBase.r, uiBase.g, uiBase.b, 0.22f);
				GUI.DrawTexture(sel, Texture2D.whiteTexture);
				GUI.color = new Color(uiHover.r, uiHover.g, uiHover.b, 0.9f);
				float e = 1.5f * s;   // 2.5.1：描边随倍率（高倍率下 1px 边会细得看不见）
				GUI.DrawTexture(new Rect(sel.x, sel.y, sel.width, e), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sel.x, sel.yMax, sel.width, e), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sel.x, sel.y, e, sel.height), Texture2D.whiteTexture);
				GUI.DrawTexture(new Rect(sel.xMax, sel.y, e, sel.height), Texture2D.whiteTexture);
				GUI.color = Color.white;
			}

			// 右下角小队列表（编号 + 装甲□/步兵○ 符号）
			DrawSquadPanel(st);

			// 1.2.0：左下角选中单位信息面板 + 只读装备/背包
			InfoPanel.Draw();

			// 1.3.0：格子背包窗口（多开 + 拖拽交换，BackpackPanel.cs）
			BackpackPanel.Draw();
		}
		catch { }
	}

	// ===== 小队列表（右下角，编号 + 符号） =====

	// 2.5.1：改为属性，随 Er2Ui.Scale 自适应（原本 const → 分辨率变化时 UI 尺寸钉死）
	private static float PanelW => 190f * ER2Shared.Er2Ui.Scale;
	private static float PanelH => 22f * ER2Shared.Er2Ui.Scale;
	private static float PanelGap => 2f * ER2Shared.Er2Ui.Scale;

	/// <summary>小队符号串：装甲单位 □ 在前，步兵单位 ○ 在后（用户要求：正方形总是在圆形前面）。</summary>
	// 1.2.10 性能：符号串缓存。原实现每次 OnGUI 事件、每行都重算 SquadSymbols，
	// 而它内部逐成员调 IsInfantry()（GetComponentInParent + Lua_Soldier.isInsideVehicle 两次 interop），
	// 10 行小队 × 8 人 × 每帧多次 OnGUI 事件 ≈ 数百次 interop/帧 —— 卡顿主因之一。
	private static readonly Dictionary<long, string> squadSymbolCache = new Dictionary<long, string>();
	private static float squadSymbolNext = -10f;

	private static string SquadSymbolsCached(Squad sq)
	{
		if (sq == null) return "";
		long k = 0;
		try { k = (long)sq.Pointer; } catch { return SquadSymbols(sq); }
		if (Time.unscaledTime >= squadSymbolNext)
		{
			squadSymbolCache.Clear();
			squadSymbolNext = Time.unscaledTime + 2f; // 与小队列表刷新同节拍
		}
		if (squadSymbolCache.TryGetValue(k, out string cached)) return cached;
		string v = SquadSymbols(sq);
		squadSymbolCache[k] = v;
		return v;
	}

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
		// 行数（0.9.1：去掉标题行，纯小队行）
		int rows = cachedFriendlySquads.Count;
		float totalH = rows * (PanelH + PanelGap);
		float startX = Screen.width - PanelW - 10f * ER2Shared.Er2Ui.Scale;
		// 1.4.35：底距 14 → 36（与左下信息面板同）——14 会压到底部提示条上（提示条顶在离底 30f*Scale）
		float y = Mathf.Max(8f * ER2Shared.Er2Ui.Scale, Screen.height - 36f * ER2Shared.Er2Ui.Scale - totalH);
		squadPanelHit = new Rect(startX, y, PanelW, totalH);

		int idx = 1;
		foreach (Squad sq in cachedFriendlySquads)
		{
			if (sq == null) continue;
			Rect r = new Rect(startX, y, PanelW, PanelH);
			bool isSel = mainSquad != null && sq.Pointer == mainSquad.Pointer;
			// 1.4.32：底板改为 HUD 统一底板（与左下信息面板、底部提示条同一套）
			// 原来这里是 uiBase@0.82 的单层平涂，与别的 HUD 块不一致。
			DrawHudPlate(r);
			if (isSel)
			{
				GUI.color = new Color(uiHover.r, uiHover.g, uiHover.b, 0.55f);
				GUI.DrawTexture(r, Texture2D.whiteTexture);
				GUI.color = Color.white;
			}
			Rect numR = new Rect(r.x, r.y, 26f * ER2Shared.Er2Ui.Scale, r.height);
			GUI.color = isSel ? new Color(1f, 1f, 1f, 0.55f) : new Color(uiHover.r, uiHover.g, uiHover.b, 0.4f);
			GUI.DrawTexture(numR, Texture2D.whiteTexture);
			GUI.color = uiText;
			GUI.DrawTexture(new Rect(numR.xMax - 1f, r.y, 1f, r.height), Texture2D.whiteTexture);
			GUI.color = isSel ? new Color(0f, 0f, 0f, 0.9f) : uiText;
			GUI.Label(numR, idx.ToString(), SquadCmdLogic.ButtonStyle());
			GUI.color = uiText;
			GUI.Label(new Rect(numR.xMax, r.y, r.width - numR.width, r.height), SquadSymbolsCached(sq), SquadCmdLogic.ButtonStyle());
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
		cmdFlash = string.Format(Ui.Tr("已选中小队 {0} 个单位"), SelTotal); cmdFlashUntil = Time.unscaledTime + 2f;
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
	/// <summary>右键按下点是否命中单位（载具或存活士兵）。命中时不弹姿态环，松开一律走短按指令。</summary>
	private static bool RightPressHitsUnit(Vector2 screenPos)
	{
		Camera cam = MainCam(); if (cam == null) return false;
		try
		{
			Ray ray = cam.ScreenPointToRay(screenPos);
			if (!Physics.Raycast(ray, out RaycastHit hit, 1500f)) return false;
			Vehicle veh = hit.collider.transform.GetComponentInParent<Vehicle>();
			if (veh == null) veh = hit.collider.transform.GetComponent<Vehicle>();
			if (veh != null) return true;
			Soldier sol = hit.collider.transform.GetComponentInParent<Soldier>();
			if (sol == null) sol = hit.collider.transform.GetComponent<Soldier>();
			// 1.3.2：尸体也算"按在对象上"——右键尸体=开背包，不该起阵型长按
			if (sol != null) return true;
			// 1.3.2：地上物品同理（右键=拾取）；布娃娃脱离层级时按命中点就近找尸体兜底
			ItemObject it = null;
			try { it = hit.collider.transform.GetComponentInParent<ItemObject>(); } catch { }
			if (it == null) { try { it = hit.collider.transform.GetComponent<ItemObject>(); } catch { } }
			if (it != null) return true;
			return NearestDeadSoldier(hit.point, 1.2f) != null;
		}
		catch { return false; }
	}

	/// <summary>1.2.4：把点投影到该处地面（沿 XZ 向下射线）。建筑命中点在屋顶时用于取"房内/房前地面"。</summary>
	private static Vector3 GroundPointUnder(Vector3 p)
	{
		try
		{
			Vector3 origin = new Vector3(p.x, p.y + 60f, p.z);
			if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, 200f)) return hit.point;
		}
		catch { }
		return new Vector3(p.x, 0f, p.z);
	}

	/// <summary>1.2.3：命中物是否属于可进入的建筑/房屋（含可交互门）。用于右键"进入并防守"。</summary>
	private static bool IsBuildingHit(RaycastHit hit)
	{		try
		{
			Transform t = hit.collider.transform;
			if (t == null) return false;
			if (t.GetComponentInParent<DestructableBuilding>() != null) return true;
			if (t.GetComponentInParent<BuildingFurnitureSpawner>() != null) return true;
			if (t.GetComponentInParent<InteragibleDoor>() != null) return true;
		}
		catch { }
		return false;
	}

	/// <summary>1.4.1：右键地上物品——有多个原生交互（弹药箱"补充弹药"等）→ 地面交互菜单；
	/// 单一拾取交互 → 快速拾取（联动半径内即时，超半径派最近士兵走过去捡）。</summary>
	private static void HandleGroundItemClick(ItemObject item, Vector3 pos)
	{
		if (SelTotal == 0)
		{
			cmdFlash = Ui.Tr("先框选/选中单位"); cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}
		Soldier best = NearestSelectedSoldier(pos);
		InventoryManager inter = null;
		if (best != null)
		{
			try { inter = InfoPanel.FindInventoryManager(best); } catch { }
			if (inter == null) { try { inter = best.inventory; } catch { } }
		}
		int count = 0;
		try { var l = item.GetInteractions(inter); count = l != null ? l.Count : 0; } catch { }
		if (count > 1)
		{
			BackpackPanel.OpenGroundMenu(item, inter, MouseGui());
			return;
		}
		BackpackPanel.RequestItemPickup(item, pos);
	}

	private static Soldier NearestSelectedSoldier(Vector3 pos)
	{
		Soldier best = null;
		float bd = float.MaxValue;
		foreach (Soldier s in GetSelectedInfantry())
		{
			try
			{
				if (s == null || !s.IsAlive || s.transform == null) continue;
				float d = (s.transform.position - pos).sqrMagnitude;
				if (d < bd) { bd = d; best = s; }
			}
			catch { }
		}
		return best;
	}

	/// <summary>1.4.1：执行地面拾取（即时/走过去到达后共用）。原生 AddItemToInventoryAndDestroyInstance。</summary>
	internal static void ExecuteGroundPickup(Soldier s, ItemObject item)
	{
		if (s == null || item == null) return;
		InventoryManager im = null;
		try { im = InfoPanel.FindInventoryManager(s); } catch { }
		if (im == null) { try { im = s.inventory; } catch { } }
		if (im == null)
		{
			cmdFlash = Ui.Tr("找不到该单位的背包数据"); cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}
		string nm = ""; try { nm = item.item_id; } catch { }
		string taker = ""; try { taker = SafeName(s); } catch { }
		bool ok = false;
		try { ok = im.AddItemToInventoryAndDestroyInstance(item); }
		catch (Exception ex) { SquadCmdLogic.LogAlways("[SquadCmd] 拾取异常: " + ex.Message); }
		cmdFlash = (ok ? Ui.Tr("已拾取 ") + nm + " → " + taker : Ui.Tr("拾取失败（超重？）"));
		cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.LogAlways("[SquadCmd] 拾取 '" + nm + "' → " + taker + " ok=" + ok);
	}

	private static void IssueDirectCommand(Vector2 screenPos)
	{
		Camera cam = MainCam(); if (cam == null) return;
		try
		{
			Ray ray = cam.ScreenPointToRay(screenPos);
			if (!Physics.Raycast(ray, out RaycastHit hit, 1500f))
			{
				if (SelTotal > 0 || HasPropSelection)
				{
					cmdFlash = Ui.Tr("未命中地面"); cmdFlashUntil = Time.unscaledTime + 2f;
				}
				return;
			}
			Vehicle veh = hit.collider.transform.GetComponentInParent<Vehicle>();
			if (veh == null) veh = hit.collider.transform.GetComponent<Vehicle>();
			Soldier sol = hit.collider.transform.GetComponentInParent<Soldier>();
			if (sol == null) sol = hit.collider.transform.GetComponent<Soldier>();
			bool solAlive = false;
			if (sol != null) { try { solAlive = sol.IsAlive; } catch { solAlive = false; } }

			// ---- 载具 ----（最先：命中载具=整车组交互）
			if (veh != null)
			{
				if (VehicleFriendly(veh) || !VehicleHostile(veh))
				{
					// 1.2.3：友军/中立载具 → 直接上车/进入（原交互环已删；下车/修理移到左下角信息面板）
					BoardVehicle(veh);
				}
				else
				{
					// 敌军载具 → 标记（集火）
					MarkEnemyVehicle(veh);
				}
				return;
			}

			// ---- 士兵（1.4.4：**先于物品**——穿戴物本身就是 ItemObject，点中别人头上的头盔/身上装备
			//      也是点了那个兵；旧顺序会把他头盔当掉落物捡走=用户实测"头盔消失"）----
			if (sol != null && solAlive)
			{
				if (FriendlyUnit(sol))
				{
					// 友军士兵：坐在载具/火力点里 → 直接上车（补员）
					Vehicle vIn = null;
					try { vIn = sol.GetComponentInParent<Vehicle>(); } catch { }
					if (vIn != null && (VehicleFriendly(vIn) || !VehicleHostile(vIn)))
					{
						BoardVehicle(vIn);
						return;
					}
					// 1.3.4（用户定案）：徒步友军 → 最近的选中士兵走过去，到达后开**双方**背包
					//（原"视为地面移动"移除；移动请点地面）
					Vector3 spos = hit.point;
					try { if (sol.transform != null) spos = sol.transform.position; } catch { }
					BackpackPanel.RequestLoot(sol, spos, true);
					return;
				}
				else
				{
					// 敌军士兵 → 标记（集火）
					MarkEnemySoldier(sol);
				}
				return;
			}

			// ---- 尸体 → 右键开尸包（点尸体身上的装备也=开尸包，而不是把装备捡走；
			//      布娃娃层级脱离时按命中点就近兜底）----
			Soldier corpse = null;
			if (sol != null && !solAlive) corpse = sol;
			if (corpse != null)
			{
				if (SelTotal == 0)
				{
					cmdFlash = Ui.Tr("先框选/选中单位"); cmdFlashUntil = Time.unscaledTime + 2f;
					return;
				}
				Vector3 cpos = hit.point;
				try { if (corpse.transform != null) cpos = corpse.transform.position; } catch { }
				BackpackPanel.RequestLoot(corpse, cpos, true); // 1.4.10（用户定案）：尸包也同时开"选中单位"的背包
				return;
			}

			// ---- 地上物品（走到这里 sol 必为 null：真正的掉落物）→ 多交互=菜单 / 单交互=拾取 ----
			ItemObject item = null;
			try { item = hit.collider.transform.GetComponentInParent<ItemObject>(); } catch { }
			if (item == null) { try { item = hit.collider.transform.GetComponent<ItemObject>(); } catch { } }
			if (item != null) { HandleGroundItemClick(item, hit.point); return; }

			// ---- 尸体就近兜底（1.3.1；1.4.5 容错半径对齐 2.5m——远景点偏同样点不到尸体）----
			Soldier corpseNear = null;
			try { corpseNear = NearestDeadSoldier(hit.point, 2.5f); } catch { }
			if (corpseNear != null)
			{
				if (SelTotal == 0)
				{
					cmdFlash = Ui.Tr("先框选/选中单位"); cmdFlashUntil = Time.unscaledTime + 2f;
					return;
				}
				Vector3 cpos2 = hit.point;
				try { if (corpseNear.transform != null) cpos2 = corpseNear.transform.position; } catch { }
				BackpackPanel.RequestLoot(corpseNear, cpos2, true); // 1.4.10：尸包同时开选中单位的背包
				return;
			}

			if (SelTotal == 0 && !HasPropSelection) return; // 0.7.86：无选中单位不响应右键指令（标记/驾驶都会穿帮）

			// 1.2.5：可交互物（物品）选中时，右键给提示（派兵过去需先选单位）
		if (HasPropSelection && SelTotal == 0)
		{
			cmdFlash = Ui.Tr("先选中要指挥的单位（当前选中的是可交互物）"); cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}

			// ---- 火力点（TurretGun 无载具父级）→ 直接进入/上车 ----
			Vehicle near = NearbyFriendlyVehicle(hit.point);
			if (near != null)
			{
				BoardVehicle(near);
				return;
			}

			// ---- 1.2.4：建筑/房屋 → 选中步兵进入并找掩体防守（用户要求）----
			// ⑨ 修复：目标点用**地面投影**——原 hit.point 命中屋顶，标记会飘在房顶
			if (IsBuildingHit(hit) && GetSelectedInfantry().Count > 0)
			{
				Vector3 groundPt = GroundPointUnder(hit.point);
				if (Formation.AssaultCovers(groundPt, 18f) > 0) return;
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

			// ---- 1.4.5：右键点在友军身边 = 会合，不是移动。**用户实际操作模式**：高视角远景下兵很小，
			//      "右键他"经常点偏命中地面 → 旧流程变成普通移动，兵走过去了但谁也不知道要开包
			//      =「走过去了还不自动打开，还要再按一遍」。2.5m 内有徒步友军就按会合处理。----
			Soldier nearFriend = null;
			try { nearFriend = NearestFriendlySoldier(hit.point, 2.5f); } catch { }
			if (nearFriend != null)
			{
				Vector3 spos = hit.point;
				try { if (nearFriend.transform != null) spos = nearFriend.transform.position; } catch { }
				BackpackPanel.RequestLoot(nearFriend, spos, true);
				return;
			}

			// ---- 空白地面 → 移动 ----
			if (SelTotal == 0)
			{
				cmdFlash = Ui.Tr("先框选/选中要指挥的单位"); cmdFlashUntil = Time.unscaledTime + 2f;
				return;
			}
			MoveCommandTo(hit.point);
		}
		catch (Exception ex) { SquadCmdLogic.Log("[SquadCmd] 右键指令失败: " + ex.Message); }
	}

	/// <summary>1.0.2：摘除某车的移动观察任务与待发移动重试（朝向命令接管时调用，防两组任务互相打架）。</summary>
	internal static void CancelVehicleMoveObservation(Vehicle v)
	{
		if (v == null) return;
		try
		{
			long p = (long)v.Pointer;
			for (int i = obsVehicles.Count - 1; i >= 0; i--)
			{
				Vehicle o = obsVehicles[i];
				try
				{
					if (o != null && (long)o.Pointer == p)
					{
						obsVehicles.RemoveAt(i);
						ObsTotal = obsUnits.Count + obsVehicles.Count;
					}
				}
				catch { obsVehicles.RemoveAt(i); }
			}
			// 待发的"同步窗口重试"移动也是该车 → 撤销（朝向是更新的命令）
			if (pendVeh != null)
			{
				try { if ((long)pendVeh.Pointer == p) pendVeh = null; } catch { pendVeh = null; }
			}
		}
		catch { }
	}

	/// <summary>
	/// 0.7.97：双击右键=原生「前往并防守」（Squad.HoldArea）。按选中成员所属原生小队分组，
	/// 每个小队单条原生命令；不建 Mod 侧跟踪/修正状态。载具仍走原生车组订单链。
	/// </summary>
	private static void IssueNativeHoldArea(Vector3 point)
	{
		if (!Active) return;
		ClearFollow("前往并防守", false);
		ClearMark(); // 新命令覆盖旧集火
		int squads = 0;
		HashSet<long> done = new HashSet<long>();
		foreach (Soldier s in GetSelectedInfantry())
		{
			try
			{
				if (s == null || !s.IsAlive) continue;
				Squad sq = s.joinedSquad;
				if (sq == null || !done.Add((long)sq.Pointer)) continue;
				SquadCmdLogic.RegisterControlledSquad(sq);
				sq.HoldArea(point, Plugin.radius.Value);
				squads++;
			}
			catch { }
		}
		int driven = 0;
		foreach (Vehicle vv in new List<Vehicle>(selVehicleRefs)) { VehicleFacing.CancelFor(vv); driven += DriveVehicleTo(vv, point); }
		if (squads > 0 || driven > 0)
		{
			RegisterMoveObservation(point, GetSelectedInfantry(), routeOnly: true); // 1.0.3：行进路线显示（不停火）
			RecordCmdTarget(point); // 1.2.1：目标点标记与连线同源显示
			SquadCmdLogic.LogAlways("[SquadCmd] 前往并防守 squads=" + squads + " vehicles=" + driven + " target=" + point.ToString("0.0"));
		}
	}

	/// <summary>移动指令：选中步兵走 + 选中载具开过去（到点击点，标点就在点击处）。移动会解除跟随。</summary>
	private static void MoveCommandTo(Vector3 point)
	{
		if (!Active) return;
		ClearFollow("下达移动", false);
		// 0.7.78：玩家新命令优先——把选中的乘员从登车 pending 摘除，否则完成判定会一直等待
		// 他们（本次移动命令已覆盖原生 boardVehicle，他们不会再上车）。
		if (pendingBoardVeh != null && pendingBoardUnits != null)
		{
			// 1.4.9：只摘除**徒步**单位——车内乘员这次不会被移动命令覆盖（见下），登车判定该继续等他们
			List<Soldier> sel = FilterOnFoot(GetSelectedInfantry(), out _);
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
		if (SelTotal == 0)
		{
			cmdFlash = Ui.Tr("先框选/选中要指挥的单位"); cmdFlashUntil = Time.unscaledTime + 2f;
			return;
		}
		// 新的移动命令覆盖旧的集火目标；没有有效选中时不误清除已有任务。
		ClearMark();
		// 1.4.9：只把**徒步**单位送进步行链路。车内乘员走的是载具（selVehicleRefs → DriveVehicleTo），
		// 对他们下 moveTo 会被原生 AI 理解成「下车步行」= 用户实测「让单位上坦克后又会立刻下车」。
		int embarked = 0;
		List<Soldier> infantry = FilterOnFoot(GetSelectedInfantry(), out embarked);
		if (embarked > 0)
			SquadCmdLogic.Log("[SquadCmd] 移动：跳过车内乘员 " + embarked + " 名（车辆移动请点载具或用阵型转向）");
		int movedInf;
		SquadCmdLogVia = "";
		if (SquadCmdLogic.TryIssueNativeMove(infantry, point, Plugin.radius.Value))
		{
			movedInf = infantry.Count;
			SquadCmdLogVia = "LuaSquad";
		}
		else
		{
			// 部分原生 Squad、跨 Squad 选择或无归属时，保留逐兵兜底。
			movedInf = MoveUnits(infantry, point);
			SquadCmdLogVia = "Fallback";
		}
		int driven = 0;
		foreach (Vehicle vv in new List<Vehicle>(selVehicleRefs))
		{
			VehicleFacing.CancelFor(vv); // 1.0.2：移动命令覆盖朝向，防车到达后自行回转
			driven += DriveVehicleTo(vv, point);
		}
		if (movedInf > 0) RegisterMoveObservation(point, infantry);
		lastMovePoint = point;
		RecordCmdTarget(point);
		cmdFlash = string.Format(Ui.Tr("移动 → 步兵 {0} + 载具 {1}"), movedInf, driven); cmdFlashUntil = Time.unscaledTime + 3f;
		SquadCmdLogic.LogAlways("[SquadCmd] 移动 point=" + point.ToString("0.0") + " 步兵=" + movedInf + " 载具=" + driven + (SquadCmdLogVia.Length > 0 ? " via=" + SquadCmdLogVia : ""));
	}

	/// <summary>停止（选中单位停下，清移动命令）。</summary>
	private static void StopSelected()
	{
		int n = 0;
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
		cmdFlash = string.Format(Ui.Tr("停止 → {0} 单位"), n); cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.Log("[SquadCmd] 轮盘停止: " + n);
	}

	// ===== 交互（1.2.3：右键直接上车/进入；下车/修理走左下角信息面板）=====

	/// <summary>原生判定：载具是否有可修的损坏部件（CanBeRepaired；调用异常视为不可修）。</summary>
	internal static bool VehicleCanBeRepaired(Vehicle v)
	{
		try { return v != null && v.CanBeRepaired(); } catch { return false; }
	}

	/// <summary>1.2.3：面板【修理】按钮 —— 对当前选中载具下原生修理订单（需选中步兵构成完整原生小队接单）。</summary>
	internal static void RepairSelectedVehicle()
	{
		Vehicle rv = null;
		try { List<Vehicle> vs = GetVehicleRefsSnapshot(); if (vs.Count > 0) rv = vs[0]; } catch { }
		if (rv == null) { cmdFlash = Ui.Tr("先选中要修理的载具"); cmdFlashUntil = Time.unscaledTime + 2f; return; }
		Squad rsq = NativeRepairSquad();
		if (rsq == null) { cmdFlash = Ui.Tr("修理需选中一支完整步兵小队"); cmdFlashUntil = Time.unscaledTime + 2.5f; return; }
		if (!VehicleCanBeRepaired(rv)) { cmdFlash = Ui.Tr("该载具无需修理"); cmdFlashUntil = Time.unscaledTime + 2f; return; }
		try
		{
			SquadCmdLogic.RegisterControlledSquad(rsq);
			rsq.OrderRepairVehicle(rv);
			cmdFlash = Ui.Tr("修理 → ") + SafeName(rv); cmdFlashUntil = Time.unscaledTime + 2f;
			SquadCmdLogic.LogAlways("[SquadCmd] 面板修理订单 " + rv.name + " 队 ptr=0x" + ((long)rsq.Pointer).ToString("X"));
		}
		catch (Exception ex)
		{
			cmdFlash = Ui.Tr("修理失败: ") + ex.Message; cmdFlashUntil = Time.unscaledTime + 2f;
			SquadCmdLogic.Log("[SquadCmd] OrderRepairVehicle 失败: " + ex.Message);
		}
	}

	/// <summary>1.2.3：面板【下车】按钮 —— 已选载具车组全部下车。</summary>
	internal static void DismountSelectedVehicles()
	{
		DismountAllVehicles();
	}

	/// <summary>对选中步兵应用姿态：站起=resetPose 归还 AI；蹲/趴=setPose 持久设置并记入还原名单。
	/// 1.2.0：入口从命令环改为快捷键（cfg Hotkeys）。</summary>
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
		cmdFlash = string.Format(Ui.Tr("{0} → {1} 单位"), PoseName(pose), n); cmdFlashUntil = Time.unscaledTime + 2f;
		SquadCmdLogic.Log("[SquadCmd] 姿态 " + PoseName(pose) + " 单位=" + n);
	}

	// setPose 是持久设置（实测：趴下后退出 RTS、玩家接管都无法自行站起）——
	// 蹲/趴过的单位记入名单，退出上帝视角/接管前逐个 resetPose() 归还 AI 姿态控制
	private static readonly List<Soldier> poseLockedUnits = new List<Soldier>();

	/// <summary>1.2.1：Formation 掩体单位到位 setPose 后登记（退出 RTS/接管时随 RestoreAllPoses 还原）。</summary>
	internal static void AddPoseLocked(Soldier s)
	{
		try { if (s != null && s.IsAlive && !poseLockedUnits.Contains(s)) poseLockedUnits.Add(s); } catch { }
	}

	private static void ResetPoseToSelection()
	{
		int n = 0;
		foreach (Soldier s in GetSelectedInfantry())
		{
			if (s == null || !s.IsAlive) continue;
			try { SquadCmdLogic.RegisterControlledUnit(s); new Lua_Soldier(s).resetPose(); n++; } catch { }
		}
		cmdFlash = string.Format(Ui.Tr("站起（恢复 AI 姿态） → {0} 单位"), n); cmdFlashUntil = Time.unscaledTime + 2f;
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

	internal static string PoseName(SoldierPose p)
	{
		return p == SoldierPose.Idle ? Ui.Tr("站起") : p == SoldierPose.Crouch ? Ui.Tr("蹲下") : Ui.Tr("趴下");
	}

	// ===== 1.2.0：供 Formation / GhostPreview / InfoPanel 使用的内部访问器 =====

	internal static bool EscMenuOpen => escMenuOpen;
	internal static Color UiBase => uiBase;
	internal static Color UiHover => uiHover;
	internal static Color UiText => uiText;

	/// <summary>1.3.0：外部面板（BackpackPanel）写左上反馈行。</summary>
	internal static void Flash(string msg, float seconds = 2f)
	{
		cmdFlash = msg;
		cmdFlashUntil = Time.unscaledTime + seconds;
	}

	// 1.2.5：MouseCursor 探测用（避免把 private 方法改成 internal 扩散面）
	internal static Vector3 CamPosDiag => camPos; // 1.2.11：诊断用（拖动时比对相机实际位置）

	internal static bool IsMouseOverGuiPublic() => IsMouseOverGui();
	internal static bool VehicleHostilePublic(Vehicle v) => VehicleHostile(v);
	internal static bool FriendlyUnitPublic(Soldier s) => FriendlyUnit(s);
	internal static bool IsBuildingHitPublic(RaycastHit hit) => IsBuildingHit(hit);

	internal static int SelInfantryCountPublic() => SelInfantryCount();

	internal static int VehicleRefCountPublic() { PruneVehicleRefs(); return selVehicleRefs.Count; }

	/// <summary>当前选中的载具真实引用快照（已剔除销毁项）。</summary>
	internal static List<Vehicle> GetVehicleRefsSnapshot()
	{
		PruneVehicleRefs();
		return new List<Vehicle>(selVehicleRefs);
	}

	/// <summary>1.2.1：填充到调用方提供的列表（零分配版本）。</summary>
	internal static void GetVehicleRefsInto(List<Vehicle> list)
	{
		if (list == null) return;
		list.Clear();
		PruneVehicleRefs();
		list.AddRange(selVehicleRefs);
	}

	/// <summary>新命令通用前置（Formation 下发用）：清移动观测/行军停火 + 摘除改令登车乘员 + 清集火标记。
	/// 1.2.1：同步清 Formation 的待收尾队列（旧的到位姿态/到位转向随新命令作废）。</summary>
	internal static void PrepareNewOrder(string reason)
	{
		Formation.OnNewOrder();
		ClearFollow(reason, false);
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
		ClearMark();
	}

	/// <summary>阵型下发后的目标登记（双击前往并防守的复用点 + 3D 标点）。</summary>
	internal static void NoteFormationTarget(Vector3 p)
	{
		lastMovePoint = p;
		RecordCmdTarget(p);
	}

	/// <summary>命令反馈（Formation 等外部类用，与 cmdFlash 同源）。</summary>
	internal static void Flash(string msg)
	{
		cmdFlash = msg;
		cmdFlashUntil = Time.unscaledTime + 3f;
	}

}
