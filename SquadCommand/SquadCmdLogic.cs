using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// RTS 上帝视角指挥——最小可行性诊断。
/// 目的：验证（1）能否枚举并识别友军小队；（2）new Lua_Squad(squad).moveTo(pos,radius) 是否真的让小队移动；
///       （3）原生任务拉取已在上帝视角进入时对本阵营全部小队解除（followCustomSquadOrders，每 6s 刷新）。
/// 所有 interop 访问一律 try/catch（单位/小队可能在遍历中被销毁），并记录日志供用户测试后回读。
/// </summary>
internal static class SquadCmdLogic
{
	private static readonly StringBuilder sb = new StringBuilder(160);

	// 追踪状态
	private static Squad trackedSquad;
	private static List<Soldier> trackedUnits;
	private static Vector3 trackAim;
	private static string trackName;
	private static float trackStartTime;
	private static float trackEndTime;
	private static float lastTrackLog = -10f;

	// RTS 控制权登记：只有当前选择中的对象才允许 Mod 介入原生 AI。
	private static readonly HashSet<IntPtr> controlledSquads = new HashSet<IntPtr>();
	private static readonly HashSet<IntPtr> controlledUnits = new HashSet<IntPtr>();

	// HUD 状态
	private static Vec lastStatus;
	private static float hudTick = -10f;

	internal static void ListSquads()
	{
		try
		{
			string myFac = GetMyFaction();
			List<Squad> squads = CollectSquads();
			sb.Clear();
			sb.Append("[SquadCmd] ==== 小队清单 (").Append(squads.Count).Append(") ====");
			Log(sb.ToString());
			for (int i = 0; i < squads.Count; i++)
			{
				Squad sq = squads[i];
				if (sq == null)
				{
					continue;
				}
				try
				{
					Vector3 center = SquadCenter(sq);
					string fac = GetSquadFaction(sq);
					bool friendly = Friendly(fac, myFac);
					bool playerIn = sq.IsPlayerInSquad();
					int order = -1;
					string obj = "";
					try
					{
						order = (int)sq.order;
					}
					catch
					{
					}
					try
					{
						Lua_Squad ls = new Lua_Squad(sq);
						if (ls.hasObjective())
						{
							Vector3 op = ls.getObjectivePosition();
							obj = " | obj=" + op;
						}
					}
					catch
					{
					}
					sb.Clear();
					sb.Append("  [").Append(i).Append("] ptr=0x").Append(((long)sq.Pointer).ToString("X"))
					  .Append(" 成员=").Append(SafeCount(sq))
					  .Append(" 阵营=").Append(string.IsNullOrEmpty(fac) ? "?" : fac)
					  .Append(" 友军=").Append(friendly ? "Y" : "N")
					  .Append(" 玩家在队=").Append(playerIn ? "Y" : "N")
					  .Append(" order=").Append(order)
					  .Append(" 中心=").Append(center.ToString("0.0"))
					  .Append(obj);
					Log(sb.ToString());
				}
				catch (Exception ex)
				{
					Log("  副队 " + i + " 读取失败: " + ex.Message);
				}
			}
			Log("[SquadCmd] ==== 我阵营=" + (string.IsNullOrEmpty(myFac) ? "?" : myFac) + " 友军判定前缀: _allies/_axis 后缀 ====");
		}
		catch (Exception ex)
		{
			Log("[SquadCmd] ListSquads error: " + ex.Message);
		}
	}

	internal static void IssueMove()
	{
		try
		{
			PlayerController pc = PlayerController.currentController;
			if (pc == null)
			{
				Log("[SquadCmd] PlayerController.currentController == null，忽略。");
				return;
			}
			Vector3 aim = AimPoint(pc);
			IssueMoveTo(aim);
		}
		catch (Exception ex)
		{
			Log("[SquadCmd] IssueMove error: " + ex.Message);
		}
	}

	/// <summary>对全部友军小队向 worldPoint 下达 moveTo（保留给调试/未来 GUI；无快捷键绑定）。返回下发的小队数。</summary>
	internal static int IssueMoveTo(Vector3 aim)
	{
		try
		{
			if (!GodViewController.Active)
			{
				return 0;
			}
			List<Soldier> units = GodViewController.GetSelectedInfantry();
			if (TryIssueNativeMove(units, aim, Plugin.radius.Value))
			{
				StartTrackingUnits(units, aim);
				LogAlways("[SquadCmd] 本次移动 via=LuaSquad 选中=" + units.Count);
				return units.Count;
			}
			int moved = 0;
			for (int i = 0; i < units.Count; i++)
			{
				Soldier s = units[i];
				if (s == null || !s.IsAlive) continue;
				try
				{
					RegisterControlledUnit(s);
					DisableNativeOrders(s);
					new Lua_Soldier(s).moveTo(aim);
					moved++;
				}
				catch (Exception ex) { Log("  [SquadCmd] moveTo 单位失败: " + ex.Message); }
			}
			StartTrackingUnits(units, aim);
			Log("[SquadCmd] 本次仅向已选 " + moved + " 个单位下发移动。");
			return moved;
		}
		catch (Exception ex)
		{
			Log("[SquadCmd] IssueMoveTo error: " + ex.Message);
			return 0;
		}
	}

	/// <summary>
	/// RTS-only 原生移动桥接：只有当选择恰好覆盖一个完整原生 Squad 的全部存活成员时才接管。
	/// 部分选中、跨 Squad 选择或缺少 joinedSquad 时返回 false，由调用方走逐兵兜底。
	/// </summary>
	internal static bool TryIssueNativeMove(List<Soldier> units, Vector3 aim, float radius)
	{
		if (!GodViewController.Active || units == null || units.Count == 0)
		{
			return false;
		}
		try
		{
			HashSet<IntPtr> selected = new HashSet<IntPtr>();
			Squad common = null;
			for (int i = 0; i < units.Count; i++)
			{
				Soldier s = units[i];
				if (s == null || !s.IsAlive || !selected.Add(s.Pointer))
				{
					return false;
				}
				Squad joined = s.joinedSquad;
				if (joined == null)
				{
					return false;
				}
				if (common == null)
				{
					common = joined;
				}
				else if (common.Pointer != joined.Pointer)
				{
					return false;
				}
			}

			if (!IsCompleteAliveSquadSelection(common, selected))
			{
				return false;
			}
			return TryIssueNativeMove(common, aim, radius);
		}
		catch (Exception ex)
		{
			Log("[SquadCmd] 原生 Squad 移动映射失败: " + ex.Message);
			return false;
		}
	}

	/// <summary>对已经确认属于 RTS 控制范围的完整 Squad 下发原生 follow/move 订单。</summary>
	internal static bool TryIssueNativeMove(Squad sq, Vector3 aim, float radius)
	{
		// 该重载也供 RTS 退出后的车辆同步窗口重试使用；是否允许新指令
		// 由调用方保证，这里只负责把订单送进游戏真正的 Squad AI 链。
		if (sq == null)
		{
			return false;
		}
		try
		{
			if (!HasAliveMembers(sq))
			{
				return false;
			}
			RegisterControlledSquad(sq);
			DisableNativeOrders(sq);
			// SynchOrder 只同步订单字段，不能可靠启动完整移动 AI。
			// Lua_Squad.moveTo 才是游戏原生指挥链实际使用的入口。
			new Lua_Squad(sq).moveTo(aim, radius);
			return true;
		}
		catch (Exception ex)
		{
			Log("[SquadCmd] 原生 Lua_Squad.moveTo 失败: " + ex.Message);
			return false;
		}
	}

	private static bool IsCompleteAliveSquadSelection(Squad sq, HashSet<IntPtr> selected)
	{
		if (sq == null || selected == null || selected.Count == 0)
		{
			return false;
		}
		HashSet<IntPtr> aliveMembers = new HashSet<IntPtr>();
		int alive = 0;
		int count = sq.CountMembers;
		for (int i = 0; i < count; i++)
		{
			Soldier member = sq.GetMemberClamped(i);
			if (member == null || !member.IsAlive)
			{
				continue;
			}
			alive++;
			aliveMembers.Add(member.Pointer);
		}
		if (alive != selected.Count || aliveMembers.Count != selected.Count)
		{
			return false;
		}
		foreach (IntPtr ptr in selected)
		{
			if (!aliveMembers.Contains(ptr))
			{
				return false;
			}
		}
		return true;
	}

	private static bool HasAliveMembers(Squad sq)
	{
		try
		{
			int count = sq.CountMembers;
			for (int i = 0; i < count; i++)
			{
				Soldier member = sq.GetMemberClamped(i);
				if (member != null && member.IsAlive)
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	internal static void TickTracker()
	{
		if (trackedUnits != null) { TickTrackerUnits(); return; }
		if (trackedSquad == null)
		{
			return;
		}
		try
		{
			float now = Time.unscaledTime;
			if (now > trackEndTime)
			{
				Vector3 c = SquadCenter(trackedSquad);
				sb.Clear();
				sb.Append("[SquadCmd] TRACK END 小队=").Append(trackName)
				  .Append(" 最终中心=").Append(c.ToString("0.0"))
				  .Append(" 目标=").Append(trackAim.ToString("0.0"))
				  .Append(" 距目标=").Append((c - trackAim).magnitude.ToString("0.0"))
				  .Append(" order=").Append(SafeOrder(trackedSquad))
				  .Append(" 超时");
				Log(sb.ToString());
				trackedSquad = null;
				return;
			}
			// 提前到达：中心距目标 ≤ radius 时结束追踪
			Vector3 curC = SquadCenter(trackedSquad);
			if ((curC - trackAim).sqrMagnitude <= Plugin.radius.Value * Plugin.radius.Value)
			{
				sb.Clear();
				sb.Append("[SquadCmd] TRACK END 小队=").Append(trackName)
				  .Append(" 已到达，中心=").Append(curC.ToString("0.0"))
				  .Append(" 目标=").Append(trackAim.ToString("0.0"))
				  .Append(" 距目标=").Append((curC - trackAim).magnitude.ToString("0.0"));
				Log(sb.ToString());
				trackedSquad = null;
				return;
			}
			// 被动追踪：不再周期性重发 moveTo。
			// 只有当原生状态真正跑偏时，后续命令状态机才应介入；本版本先避免无脑重置 AI。
			if (now - lastTrackLog >= 2f)
			{
				lastTrackLog = now;
				float el = now - trackStartTime;
				Vector3 c = SquadCenter(trackedSquad);
				sb.Clear();
				sb.Append("[SquadCmd] T+").Append(el.ToString("0.0")).Append("s ")
				  .Append(trackName)
				  .Append(" 中心=").Append(c.ToString("0.0"))
				  .Append(" 距目标=").Append((c - trackAim).magnitude.ToString("0.0"))
				  .Append(" order=").Append(SafeOrder(trackedSquad));
				Log(sb.ToString());
			}
		}
		catch
		{
		}
	}

	/// <summary>追踪单位组（虚拟选择）。本版本只监控到达状态，不周期性重发命令。</summary>
	private static void TickTrackerUnits()
	{
		if (trackedUnits == null) return;
		try
		{
			// 清理死亡
			for (int i = trackedUnits.Count - 1; i >= 0; i--)
				if (trackedUnits[i] == null || !trackedUnits[i].IsAlive) trackedUnits.RemoveAt(i);
			float now = Time.unscaledTime;
			if (now > trackEndTime || trackedUnits.Count == 0)
			{
				Vector3 c = UnitsCenter(trackedUnits);
				sb.Clear();
				sb.Append("[SquadCmd] TRACK END 单位组=").Append(trackName)
				  .Append(" 剩余=").Append(trackedUnits.Count)
				  .Append(" 中心=").Append(c.ToString("0.0"))
				  .Append(" 目标=").Append(trackAim.ToString("0.0"))
				  .Append(" 距目标=").Append((c - trackAim).magnitude.ToString("0.0"))
				  .Append(" 超时");
				Log(sb.ToString());
				trackedUnits = null;
				return;
			}
			// 提前到达：中心距目标 ≤ radius 时结束追踪
			Vector3 curC = UnitsCenter(trackedUnits);
			if ((curC - trackAim).sqrMagnitude <= Plugin.radius.Value * Plugin.radius.Value)
			{
				sb.Clear();
				sb.Append("[SquadCmd] TRACK END 单位组=").Append(trackName)
				  .Append(" 已到达，中心=").Append(curC.ToString("0.0"))
				  .Append(" 目标=").Append(trackAim.ToString("0.0"))
				  .Append(" 距目标=").Append((curC - trackAim).magnitude.ToString("0.0"));
				Log(sb.ToString());
				trackedUnits = null;
				return;
			}
			if (now - lastTrackLog >= 2f)
			{
				lastTrackLog = now;
				float el = now - trackStartTime;
				Vector3 c = UnitsCenter(trackedUnits);
				sb.Clear();
				sb.Append("[SquadCmd] T+").Append(el.ToString("0.0")).Append("s ")
				  .Append(trackName)
				  .Append(" 剩余=").Append(trackedUnits.Count)
				  .Append(" 中心=").Append(c.ToString("0.0"))
				  .Append(" 距目标=").Append((c - trackAim).magnitude.ToString("0.0"));
				Log(sb.ToString());
			}
		}
		catch { }
	}

	internal static void DrawHud()
	{
		if (!Plugin.enabled.Value)
		{
			return;
		}
		// 上帝视角下不画常驻 HUD（GodViewController 有自己的 HUD，避免重叠）
		if (GodViewController.Active)
		{
			return;
		}
		try
		{
			if (Time.unscaledTime - hudTick < 0.5f && lastStatus != null)
			{
				DrawStatus();
				return;
			}
			hudTick = Time.unscaledTime;
			PlayerController pc = PlayerController.currentController;
			string myFac = GetMyFaction();
			int friendCount = 0;
			int total = 0;
			try
			{
				List<Squad> squads = CollectSquads();
				total = squads.Count;
				for (int i = 0; i < squads.Count; i++)
				{
					if (squads[i] != null && Friendly(GetSquadFaction(squads[i]), myFac))
					{
						friendCount++;
					}
				}
			}
			catch
			{
			}
			lastStatus = new Vec(friendCount, total);
			DrawStatus();
		}
		catch
		{
		}
	}

	private static void DrawStatus()
	{
		try
		{
			if (lastStatus == null)
			{
				return;
			}
			GUIStyle st = HudStyle();
			if (st == null)
			{
				return;
			}
			sb.Clear();
			sb.Append("[小队指挥] 友军小队 ").Append(lastStatus.A).Append('/').Append(lastStatus.B);
			sb.Append("   F9 上帝视角");
			GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.55f);
			GUI.Label(new Rect(8f, Screen.height - 46f, 720f, 30f), sb.ToString(), st);
			GUI.color = Color.white;
		}
		catch
		{
		}
	}

	#region 基础查询

	private static List<Squad> CollectSquads()
	{
		Dictionary<IntPtr, Squad> dict = new Dictionary<IntPtr, Squad>();
		try
		{
			Il2CppSystem.Collections.Generic.List<Creature> list = Creature.allCreatures;
			if (list == null)
			{
				return new List<Squad>();
			}
			for (int i = 0; i < list.Count; i++)
			{
				Creature c = list[i];
				if (c == null)
				{
					continue;
				}
				try
				{
					Soldier s = c.TryCast<Soldier>();
					if (s == null)
					{
						continue;
					}
					if (!s.IsAlive)
					{
						continue;
					}
					Squad sq = s.joinedSquad;
					if (sq == null)
					{
						continue;
					}
					if (!dict.ContainsKey(sq.Pointer))
					{
						dict[sq.Pointer] = sq;
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return new List<Squad>(dict.Values);
	}

	private static string GetMyFaction()
	{
		// 上帝视角下玩家已 SetPlayer(null)，ControlledCharacter 拿不到 → 用缓存的阵营
		if (GodViewController.Active && !string.IsNullOrEmpty(GodViewController.SavedFaction))
		{
			return GodViewController.SavedFaction;
		}
		try
		{
			PlayerController pc = PlayerController.currentController;
			if (pc != null)
			{
				Soldier s = pc.ControlledCharacter;
				if (s != null && !string.IsNullOrEmpty(s.faction))
				{
					return s.faction;
				}
			}
		}
		catch
		{
		}
		return "";
	}

	private static string GetSquadFaction(Squad sq)
	{
		try
		{
			int count = sq.CountMembers;
			for (int i = 0; i < count; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m != null && !string.IsNullOrEmpty(m.faction))
				{
					return m.faction;
				}
			}
		}
		catch
		{
		}
		return "";
	}

	internal static bool Friendly(string fac, string myFac)
	{
		if (string.IsNullOrEmpty(myFac) || string.IsNullOrEmpty(fac))
		{
			return false;
		}
		if (string.Equals(myFac, fac, StringComparison.Ordinal))
		{
			return true;
		}
		bool mineA = myFac.EndsWith("_allies", StringComparison.Ordinal);
		bool mineX = myFac.EndsWith("_axis", StringComparison.Ordinal);
		bool hisA = fac.EndsWith("_allies", StringComparison.Ordinal);
		bool hisX = fac.EndsWith("_axis", StringComparison.Ordinal);
		return (mineA && hisA) || (mineX && hisX);
	}

	private static Vector3 SquadCenter(Squad sq)
	{
		Vector3 sum = Vector3.zero;
		int n = 0;
		try
		{
			int count = sq.CountMembers;
			for (int i = 0; i < count; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m == null)
				{
					continue;
				}
				Transform t = m.transform;
				if (t == null)
				{
					continue;
				}
				sum += t.position;
				n++;
			}
		}
		catch
		{
		}
		return n > 0 ? sum / n : Vector3.zero;
	}

	private static int SafeCount(Squad sq)
	{
		try
		{
			return sq.CountMembers;
		}
		catch
		{
			return -1;
		}
	}

	private static int SafeOrder(Squad sq)
	{
		try
		{
			return (int)sq.order;
		}
		catch
		{
			return -2;
		}
	}

	private static Vector3 AimPoint(PlayerController pc)
	{
		try
		{
			Vector3 v = pc.CalculateOrderPosition();
			if (v != Vector3.zero)
			{
				return v;
			}
		}
		catch
		{
		}
		try
		{
			Camera cam = null;
			try
			{
				cam = ResourcesManager.mainCamera;
			}
			catch
			{
			}
			if (cam == null)
			{
				cam = Camera.main;
			}
			if (cam != null)
			{
				Ray ray = cam.ScreenPointToRay(new Vector3(Screen.width * 0.5f, Screen.height * 0.5f, 0f));
				if (Physics.Raycast(ray, out RaycastHit hit, 600f))
				{
					return hit.point;
				}
				return cam.transform.position + cam.transform.forward * 40f;
			}
		}
		catch
		{
		}
		return Vector3.zero;
	}

	internal static bool IsControlledSquad(Squad sq)
	{
		try { return sq != null && controlledSquads.Contains(sq.Pointer); } catch { return false; }
	}

	internal static bool IsControlledUnit(Soldier s)
	{
		try { return s != null && controlledUnits.Contains(s.Pointer); } catch { return false; }
	}

	internal static void RegisterControlledUnit(Soldier s)
	{
		if (s == null) return;
		try { if (s.IsAlive) controlledUnits.Add(s.Pointer); } catch { }
	}

	internal static void RegisterControlledSquad(Squad sq)
	{
		if (sq == null) return;
		try { controlledSquads.Add(sq.Pointer); } catch { }
	}

	internal static void SyncControlledSelection(List<Soldier> units, List<Squad> squads)
	{
		try
		{
			controlledUnits.Clear();
			controlledSquads.Clear();
			if (units != null)
			{
				for (int i = 0; i < units.Count; i++) RegisterControlledUnit(units[i]);
			}
			if (squads != null)
			{
				for (int i = 0; i < squads.Count; i++) RegisterControlledSquad(squads[i]);
			}
		}
		catch { }
	}

	internal static void ClearControlledSelection()
	{
		controlledUnits.Clear();
		controlledSquads.Clear();
	}

	internal static void DisableNativeOrders(Squad sq)
	{
		if (!IsControlledSquad(sq)) return;
		try
		{
			int count = sq.CountMembers;
			for (int i = 0; i < count; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m == null || !IsControlledUnit(m) && controlledSquads.Contains(sq.Pointer) == false) continue;
				try
				{
					AiParams ap = new Lua_Soldier(m).getAiParams();
					try { ap.followCustomSquadOrders(); } catch { }
					try { ap.followCustomDirectCommands(); } catch { }
					try { ap.allowMovements(true); } catch { }
				}
				catch { }
			}
		}
		catch { }
	}

	internal static void DisableNativeOrders(Soldier s)
	{
		if (!IsControlledUnit(s)) return;
		try
		{
			AiParams ap = new Lua_Soldier(s).getAiParams();
			try { ap.followCustomSquadOrders(); } catch { }
			try { ap.followCustomDirectCommands(); } catch { }
			try { ap.allowMovements(true); } catch { }
		}
		catch { }
	}

	internal static void StartTracking(Squad sq, Vector3 aim)
	{
		RegisterControlledSquad(sq);
		trackedUnits = null;
		trackedSquad = sq;
		trackAim = aim;
		trackName = "ptr=0x" + ((long)sq.Pointer).ToString("X") + " 成员=" + SafeCount(sq);
		trackStartTime = Time.unscaledTime;
		trackEndTime = Time.unscaledTime + Plugin.trackSeconds.Value;
		lastTrackLog = -10f;
	}

	/// <summary>追踪虚拟选择单位组；不再周期性覆盖原生 AI。</summary>
	internal static void StartTrackingUnits(List<Soldier> units, Vector3 aim)
	{
		trackedSquad = null;
		trackedUnits = units != null ? new List<Soldier>(units) : null;
		trackAim = aim;
		trackName = (trackedUnits != null ? trackedUnits.Count + "人" : "空");
		trackStartTime = Time.unscaledTime;
		trackEndTime = Time.unscaledTime + Plugin.trackSeconds.Value;
		lastTrackLog = -10f;
	}

	internal static void StopTracking()
	{
		trackedSquad = null;
		trackedUnits = null;
	}

	/// <summary>计算单位组中心（仅存活）。</summary>
	internal static Vector3 UnitsCenter(List<Soldier> units)
	{
		if (units == null || units.Count == 0) return Vector3.zero;
		Vector3 sum = Vector3.zero; int n = 0;
		for (int i = 0; i < units.Count; i++)
		{
			Soldier s = units[i];
			if (s == null || !s.IsAlive || s.transform == null) continue;
			sum += s.transform.position; n++;
		}
		return n > 0 ? sum / n : Vector3.zero;
	}

	/// <summary>玩家阵营的全部存活小队（用于批量解除原生任务拉取等）。</summary>
	internal static List<Squad> GetAllFriendlySquads()
	{
		List<Squad> res = new List<Squad>();
		string myFac = GetMyFaction();
		List<Squad> all = CollectSquads();
		for (int i = 0; i < all.Count; i++)
		{
			Squad sq = all[i];
			if (sq == null) continue;
			if (Friendly(GetSquadFaction(sq), myFac)) res.Add(sq);
		}
		return res;
	}

	internal static void LogAlways(string msg)
	{
		try { Plugin.ModLog.LogInfo(msg); } catch { }
	}

	internal static void Log(string msg)
	{
		try
		{
			if (Plugin.debugLog == null || !Plugin.debugLog.Value) return; // 0.7.84：发布默认静默，cfg 开 debugLog 查看诊断
			Plugin.ModLog.LogInfo(msg);
		}
		catch
		{
		}
	}

	#endregion

	#region HUD 样式

	private static GUIStyle hudStyle;
	private static int hudFontSize;
	private static GUIStyle smallStyle;
	private static int smallFontSize;

	internal static GUIStyle HudStyle()
	{
		int fs = Mathf.Max(11, Mathf.RoundToInt(13f * ResMult()));
		if (hudStyle != null && hudFontSize == fs)
		{
			return hudStyle;
		}
		hudFontSize = fs;
		Font f = GetFont();
		hudStyle = new GUIStyle();
		if (f != null)
		{
			hudStyle.font = f;
		}
		hudStyle.fontSize = fs;
		hudStyle.fontStyle = FontStyle.Bold;
		hudStyle.alignment = TextAnchor.MiddleLeft;
		hudStyle.normal.textColor = Color.white; // 陷阱：new GUIStyle() 默认黑色
		return hudStyle;
	}

	/// <summary>小字号样式（底部提示行等次要信息）。</summary>
	internal static GUIStyle HudStyleSmall()
	{
		int fs = Mathf.Max(10, Mathf.RoundToInt(11f * ResMult()));
		if (smallStyle != null && smallFontSize == fs)
		{
			return smallStyle;
		}
		smallFontSize = fs;
		Font f = GetFont();
		smallStyle = new GUIStyle();
		if (f != null)
		{
			smallStyle.font = f;
		}
		smallStyle.fontSize = fs;
		smallStyle.alignment = TextAnchor.MiddleCenter;
		smallStyle.normal.textColor = new Color(0.85f, 0.9f, 0.85f, 0.95f);
		return smallStyle;
	}

	private static float ResMult()
	{
		try
		{
			float m = ResourcesManager.ResolutionMult;
			return (m > 0f && !float.IsNaN(m)) ? m : 1f;
		}
		catch
		{
			return 1f;
		}
	}

	private static Font GetFont()
	{
		try
		{
			Font f = PhaseBarGUI.GetDefaultFont();
			if (f != null)
			{
				return f;
			}
		}
		catch
		{
		}
		try
		{
			if (GUI.skin != null && GUI.skin.font != null)
			{
				return GUI.skin.font;
			}
		}
		catch
		{
		}
		return null;
	}

	#endregion

	/// <summary>极简结构体（HUD 缓存，避免额外分配）。</summary>
	private sealed class Vec
	{
		public readonly int A;
		public readonly int B;
		public Vec(int a, int b)
		{
			A = a;
			B = b;
		}
	}
}
