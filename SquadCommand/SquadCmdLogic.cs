using System;
using System.Collections.Generic;
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
	// RTS 控制权登记：只有当前选择中的对象才允许 Mod 介入原生 AI。
	private static readonly HashSet<IntPtr> controlledSquads = new HashSet<IntPtr>();
	private static readonly HashSet<IntPtr> controlledUnits = new HashSet<IntPtr>();

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
}
