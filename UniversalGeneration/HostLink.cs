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
	private static FieldInfo fiCameraPass;  // 2.3.0：宿主 externalCameraPass（放置/携带中放行相机，见 CameraPassThrough）
	private static MethodInfo miGhostify;    // 1.0.4：宿主 GhostPreview.Ghostify（放置预览复用同一套幽灵视觉）
	private static Type ghostPreviewType;

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
			// 2.3.0：注册 externalCameraPass（宿主 1.4.16+）——放置/携带的全屏互斥只吞点击手势，
			// 不冻结相机（滚轮/中键）。用户反馈"预放置时不能滚动滚轮改变视角"即旧版被冻结。
			// 宿主旧版无此字段：null 跳过，行为回退旧版（滚轮仍被冻结，但不报错）。
			try
			{
				fiCameraPass = godViewType.GetField("externalCameraPass", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
				if (fiCameraPass != null)
					fiCameraPass.SetValue(null, new System.Func<bool>(CameraPassThrough));
			}
			catch { }
			// 1.0.4：宿主幽灵预览（放置预览复用；宿主缺失时静默降级为自绘标记）
			try
			{
				ghostPreviewType = hostAsm.GetType("ER2SquadCommand.GhostPreview");
				if (ghostPreviewType != null)
					miGhostify = ghostPreviewType.GetMethod("Ghostify", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			}
			catch { }

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

	/// <summary>1.0.4：把 GameObject 幽灵化（复用宿主 GhostPreview 的同一套半透明视觉）。
	/// 宿主缺失或调用失败返回 false——调用方回退自绘标记。</summary>
	public static bool Ghostify(UnityEngine.GameObject go)
	{
		if (miGhostify == null || go == null) return false;
		try { return (bool)miGhostify.Invoke(null, new object[] { go }); } catch { return false; }
	}

	/// <summary>1.0.4：宿主幽灵预览是否可用（不可用则放置预览退回自绘圈）。</summary>
	public static bool GhostAvailable => miGhostify != null;

	/// <summary>
	/// 2.3.0：放置/携带中告知宿主"全屏互斥是拖放手势，不是 UI 面板"——
	/// 相机输入（滚轮升降/中键旋转）照常，点击手势仍被 externalGuiBlock 吞掉。
	/// 修复用户反馈"预放置时不能滚动滚轮改变视角"（宿主 1.4.16 的 externalCameraPass 契约）。
	/// </summary>
	private static bool CameraPassThrough()
	{
		return Placer.Placing || ItemDragger.Carrying;
	}

	private static MethodInfo miRegisterGhost, miUnregisterGhost;

	/// <summary>1.0.8：把预览对象登记进宿主幽灵表（伤害免疫 + 相机地面射线豁免）。</summary>
	public static void RegisterGhost(UnityEngine.GameObject go)
	{
		try
		{
			if (go == null || ghostPreviewType == null) return;
			if (miRegisterGhost == null)
				miRegisterGhost = ghostPreviewType.GetMethod("RegisterGhost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			if (miRegisterGhost != null) miRegisterGhost.Invoke(null, new object[] { go });
		}
		catch { }
	}

	public static void UnregisterGhost(UnityEngine.GameObject go)
	{
		try
		{
			if (go == null || ghostPreviewType == null) return;
			if (miUnregisterGhost == null)
				miUnregisterGhost = ghostPreviewType.GetMethod("UnregisterGhost", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);
			if (miUnregisterGhost != null) miUnregisterGhost.Invoke(null, new object[] { go });
		}
		catch { }
	}
}
