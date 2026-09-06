using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 反射联动战场指挥官宿主（陷阱 9 模式：无编译期依赖，宿主缺失自动降级）。
/// 需要：GodViewController.Active（RTS 是否激活）、CurrentMark（标记点）、
/// externalGuiBlock（把本 mod 面板 Rect 注册进宿主手势互斥）。
/// </summary>
internal static class HostLink
{
	private static Assembly hostAsm;
	private static Type godViewType;
	private static PropertyInfo piActive;
	private static FieldInfo fiExternalGuiBlock;
	private static PropertyInfo piCurrentMark;
	private static FieldInfo fiSavedFaction;
	private static FieldInfo fiEscMenuOpen;
	private static FieldInfo fiRtsSquadSet; // HashSet<long>：宿主 RTS 分队集合（车辆驾驶资格门槛）

	private static Func<Rect?> guiBlockDelegate;

	public static bool HostPresent => godViewType != null;

	public static void Init()
	{
		try
		{
			foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				string n = asm.GetName().Name;
				if (n == "ER2_BattlefieldCommander" || n == "SquadCommand" || n == "ER2SquadCommand")
				{
					hostAsm = asm;
					break;
				}
			}
			if (hostAsm == null)
			{
				Plugin.ModLog.LogWarning("未找到战场指挥官宿主程序集——Universal Generation 将保持休眠（仅在宿主 RTS 模式内工作）。");
				return;
			}
			godViewType = hostAsm.GetType("ER2SquadCommand.GodViewController");
			if (godViewType == null)
			{
				Plugin.ModLog.LogError("宿主程序集里找不到 ER2SquadCommand.GodViewController，反射联动失败。");
				return;
			}
			piActive = godViewType.GetProperty("Active", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			fiExternalGuiBlock = godViewType.GetField("externalGuiBlock", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			piCurrentMark = godViewType.GetProperty("CurrentMark", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			fiSavedFaction = godViewType.GetField("SavedFaction", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			fiEscMenuOpen = godViewType.GetField("escMenuOpen", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			fiRtsSquadSet = godViewType.GetField("rtsSquadSet", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

			if (piActive == null || fiExternalGuiBlock == null)
			{
				Plugin.ModLog.LogError("宿主 GodViewController 缺少 Active/externalGuiBlock 成员（宿主版本过旧？），反射联动失败。");
				godViewType = null;
				return;
			}

			// 把本 mod 的面板遮挡 Rect 注入宿主手势互斥
			guiBlockDelegate = new Func<Rect?>(GenPanel.ExternalGuiBlockRect);
			fiExternalGuiBlock.SetValue(null, guiBlockDelegate);

			Plugin.ModLog.LogInfo("已联动战场指挥官宿主（" + hostAsm.GetName().Name + "），externalGuiBlock 已注入。");
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("HostLink.Init 失败: " + ex.Message);
			godViewType = null;
		}
	}

	/// <summary>宿主 RTS 上帝视角是否激活；宿主缺失恒 false（mod 休眠）。</summary>
	public static bool GodViewActive
	{
		get
		{
			if (godViewType == null || piActive == null) return false;
			try { return (bool)piActive.GetValue(null); } catch { return false; }
		}
	}

	/// <summary>
	/// 把小队登记进宿主的 rtsSquadSet（RTS 分队集合）。
	/// 宿主 DriveVehicleTo 要求 ai.squadInside 指向的班必须在此集合内才允许驾驶——
	/// 该集合只在宿主【分队】/【上车】流程登记，外部生成的乘员班必须主动注册。
	/// </summary>
	public static bool RtsRegisterSquad(Squad sq)
	{
		if (sq == null || fiRtsSquadSet == null) return false;
		try
		{
			var set = fiRtsSquadSet.GetValue(null) as HashSet<long>;
			if (set == null) return false;
			bool added = set.Add((long)sq.Pointer);
			if (added && Plugin.debugLog.Value)
				Plugin.ModLog.LogInfo("[UniGen] 已登记 RTS 分队: 0x" + ((long)sq.Pointer).ToString("X"));
			return true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] rtsSquadSet 登记失败: " + ex.Message);
			return false;
		}
	}

	/// <summary>宿主缓存的玩家阵营（上帝视角接管后 ControlledCharacter 为空的回落）。</summary>
	public static string HostSavedFaction()
	{
		if (godViewType == null || fiSavedFaction == null) return "";
		try { return fiSavedFaction.GetValue(null) as string ?? ""; } catch { return ""; }
	}

	/// <summary>宿主 ESC 设置菜单是否打开（打开时本 mod 全部 UI 隐藏，跟随宿主行为）。</summary>
	public static bool EscMenuOpen
	{
		get
		{
			if (godViewType == null || fiEscMenuOpen == null) return false;
			try { return (bool)fiEscMenuOpen.GetValue(null); } catch { return false; }
		}
	}

	/// <summary>宿主当前标记点（RTS 里画的集火标记）；无标记返回 null。</summary>
	public static object CurrentMark
	{
		get
		{
			if (godViewType == null || piCurrentMark == null) return null;
			try { return piCurrentMark.GetValue(null); } catch { return null; }
		}
	}
}
