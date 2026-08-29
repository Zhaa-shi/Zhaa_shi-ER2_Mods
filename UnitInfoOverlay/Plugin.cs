using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace ER2UnitInfoOverlay;

[BepInPlugin("er2.unitinfooverlay", "ER2 Unit Inspector", "1.0.4")]
public class Plugin : BasePlugin
{
#if CN_BUILD
	internal const bool DefaultChinese = true;
#else
	internal const bool DefaultChinese = false;
#endif

	internal static ManualLogSource ModLog;

	// General
	internal static ConfigEntry<bool> enabled;
	internal static ConfigEntry<KeyCode> toggleKey;
	internal static ConfigEntry<string> displayMode;
	internal static ConfigEntry<bool> showAlive;
	internal static ConfigEntry<bool> showCorpses;
	internal static ConfigEntry<bool> showPlayer;
	internal static ConfigEntry<float> maxDistance;
	internal static ConfigEntry<int> maxUnits;
	internal static ConfigEntry<int> fontSize;
	internal static ConfigEntry<float> bgOpacity;
	internal static ConfigEntry<float> aimRadius;
	internal static ConfigEntry<bool> showSummary;
	internal static ConfigEntry<float> hideAfterLoad;

	// Info Lines
	internal static ConfigEntry<bool> showHp;
	internal static ConfigEntry<bool> showState;
	internal static ConfigEntry<bool> showPose;
	internal static ConfigEntry<bool> showFaction;
	internal static ConfigEntry<bool> showRole;
	internal static ConfigEntry<bool> showPos;
	internal static ConfigEntry<bool> showVelocity;
	internal static ConfigEntry<bool> showId;

	internal static string T(string cn, string en) => DefaultChinese ? cn : en;

	public override void Load()
	{
		ModLog = Log;

		enabled = Config.Bind("General", "enabled", true, T("总开关（关闭后悬浮信息与热键全部停用）。", "Master switch (disables the overlay and the hotkey entirely)."));
		toggleKey = Config.Bind("General", "toggleKey", KeyCode.F10, T("显示/隐藏悬浮信息的快捷键（可在 ModManager 里改键）。", "Key that toggles the overlay on/off (rebindable in ModManager)."));
		displayMode = Config.Bind("General", "displayMode", "Targeted", new ConfigDescription(T("显示模式：All=范围内全部单位；Targeted=只显示准星附近最近的一个单位（带高亮框）。", "Display mode: All = every unit in range; Targeted = only the nearest unit to the crosshair (highlighted)."), new AcceptableValueList<string>("All", "Targeted")));
		showAlive = Config.Bind("General", "showAlive", true, T("显示存活单位。", "Show alive units."));
		showCorpses = Config.Bind("General", "showCorpses", true, T("显示尸体（已死亡单位，灰色）。", "Show corpses (dead units, gray)."));
		showPlayer = Config.Bind("General", "showPlayer", false, T("也显示玩家自己控制的单位（视角贴脸会挡视线，默认关）。", "Also show the unit you control (right at the camera, off by default)."));
		maxDistance = Config.Bind("General", "maxDistance", 60f, new ConfigDescription(T("最大显示距离（米）。", "Maximum display distance in meters."), new AcceptableValueRange<float>(5f, 500f)));
		maxUnits = Config.Bind("General", "maxUnits", 1, new ConfigDescription(T("单帧最多绘制单位数（性能保护）。", "Max units drawn per frame (performance guard)."), new AcceptableValueRange<int>(1, 200)));
		fontSize = Config.Bind("General", "fontSize", 12, new ConfigDescription(T("悬浮文字字号（按分辨率缩放）。", "Overlay font size (scaled by resolution)."), new AcceptableValueRange<int>(9, 24)));
		bgOpacity = Config.Bind("General", "bgOpacity", 0.55f, new ConfigDescription(T("标签背景不透明度（0=纯文字无背景）。", "Label background opacity (0 = text only, no background)."), new AcceptableValueRange<float>(0f, 0.9f)));
		aimRadius = Config.Bind("General", "aimRadius", 160f, new ConfigDescription(T("Targeted 模式下准星吸附半径（像素）。", "Crosshair snap radius in pixels for Targeted mode."), new AcceptableValueRange<float>(10f, 200f)));
		showSummary = Config.Bind("General", "showSummary", true, T("屏幕左上角显示摘要行（开关/单位计数/范围）。", "Show the summary line at the top-left (toggle state / unit counts / range)."));
		hideAfterLoad = Config.Bind("General", "hideAfterLoad", 6f, new ConfigDescription(T("进战斗/换场后前 N 秒不显示（覆盖加载黑屏尾巴；0=关闭）。", "Hide for the first N seconds after a scene loads (covers the loading-screen tail; 0 = off)."), new AcceptableValueRange<float>(0f, 30f)));

		showHp = Config.Bind("Info Lines", "showHp", true, T("血量行：当前HP/观察上限/失能阈值（thr）。", "HP line: current / observed max / incapacitation threshold (thr)."));
		showState = Config.Bind("Info Lines", "showState", true, T("状态标签行：BLEED流血 / DOWN倒下(HP低于失能阈值) / SURR投降 / SPRINT / RUN / MOVE / CRAWL / AIM / RELOAD / THROW / FIRE着火 / VEH载具 / CARRY搬运 / CARRIED被搬运 / TALK / STAM!体力耗尽 / WATER。", "State tag line: BLEED / DOWN (HP below incapacitation threshold) / SURR surrender / SPRINT / RUN / MOVE / CRAWL / AIM / RELOAD / THROW / FIRE / VEH vehicle / CARRY / CARRIED / TALK / STAM! / WATER."));
		showPose = Config.Bind("Info Lines", "showPose", true, T("姿态行：Idle / Crouch / Prone。", "Pose line: Idle / Crouch / Prone."));
		showFaction = Config.Bind("Info Lines", "showFaction", true, T("阵营行（faction 字符串）。", "Faction line (raw faction string)."));
		showRole = Config.Bind("Info Lines", "showRole", true, T("兵种标签行：MEDIC / GUNNER / AT / SAPPER / MARKSMAN / RADIO / LEADER。", "Role tag line: MEDIC / GUNNER / AT / SAPPER / MARKSMAN / RADIO / LEADER."));
		showPos = Config.Bind("Info Lines", "showPos", true, T("坐标与距离行（pos x,y,z | 距离m）。", "Position and distance line (pos x,y,z | dist m)."));
		showVelocity = Config.Bind("Info Lines", "showVelocity", true, T("速度行（m/s，平滑估算）。", "Velocity line (m/s, smoothed estimate)."));
		showId = Config.Bind("Info Lines", "showId", true, T("实例 ID 与指针行（开发者调试用）。", "Instance ID and pointer line (developer debugging)."));

		new Harmony("er2.unitinfooverlay").PatchAll(typeof(Plugin).Assembly);
		ModLog.LogInfo("ER2 Unit Inspector 1.0.4 loaded. Toggle: " + toggleKey.Value + " | mode: " + displayMode.Value + " | range: " + maxDistance.Value + "m");
	}
}

/// <summary>数据刷新 + 热键轮询（PlayerController.Update Postfix，每帧执行，内部节流）。</summary>
[HarmonyPatch(typeof(PlayerController), "Update")]
public static class OverlayUpdatePatch
{
	private static void Postfix()
	{
		try
		{
			if (!Plugin.enabled.Value)
			{
				OverlayLogic.ClearCache();
				return;
			}
			if (Plugin.toggleKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.toggleKey.Value))
			{
				OverlayLogic.active = !OverlayLogic.active;
				Plugin.ModLog.LogInfo("Unit Inspector overlay toggled: " + (OverlayLogic.active ? "ON" : "OFF"));
			}
			OverlayLogic.Refresh();
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("Unit Inspector update error: " + ex.Message);
		}
	}
}

/// <summary>IMGUI 绘制（PlayerController.OnGUI Postfix，与原生 UI 同一绘制上下文）。</summary>
[HarmonyPatch(typeof(PlayerController), "OnGUI")]
public static class OverlayDrawPatch
{
	private static void Postfix()
	{
		try
		{
			OverlayLogic.Draw();
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("Unit Inspector draw error: " + ex.Message);
		}
	}
}
