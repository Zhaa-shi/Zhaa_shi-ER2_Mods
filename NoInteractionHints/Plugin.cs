using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace ER2NoInteractionHints;

[BepInPlugin("com.ryan.er2.nointeractionhints", "ER2 Hide Anything", "4.5.3")]
public class Plugin : BasePlugin
{
	[HarmonyPatch(typeof(InteractionGUI2), "SetInteraction")]
	private static class SkipSingleInteractionPatch
	{
		private static bool Prefix()
		{
			return !UiGroups.IsHidden("hints");
		}
	}

	[HarmonyPatch(typeof(InteractionGUI), "SetInteractions")]
	private static class SkipMultiInteractionPatch
	{
		private static bool Prefix()
		{
			return !UiGroups.IsHidden("hints");
		}
	}

	[HarmonyPatch(typeof(InputDisplayer), "OnEnable")]
	private static class HideInputDisplayerPatch
	{
		private static void Postfix(InputDisplayer __instance)
		{
			if (UiGroups.IsHidden("hints") && __instance != null)
			{
				GameObject go = __instance.gameObject;
				if (go != null && go.activeSelf)
				{
					go.SetActive(false);
				}
			}
		}
	}

	[HarmonyPatch(typeof(Soldier), "Update")]
	private static class BattleTickPatch
	{
		private static float tickTimer;

		private static void Postfix()
		{
			tickTimer += Time.deltaTime;
			if (tickTimer < 0.1f)
			{
				return;
			}
			tickTimer = 0f;
			// 战斗帧：补扫契约注册 + 巡检隐藏状态（每项独立锁定=严格，未锁定=跟随现实）
			HudCompat.PreregisterKnownMods();
			HudCompat.DiscoverFields();
			UiGroups.Enforce();
			HudCompat.Enforce();
		}
	}

	/// <summary>
	/// 设置界面帧钩子：打开设置（ModManager 页面）时补扫注册 + 巡检隐藏状态，
	/// 保证主菜单打开 ModManager 就看到全部开关。低优先级 → 先于 ModManager 的填充。
	/// </summary>
	[HarmonyPatch(typeof(SettingsGUI_V2), "Update")]
	[HarmonyPriority(100)]
	private static class HudInitSettingsPatch
	{
		private static void Postfix()
		{
			HudCompat.PreregisterKnownMods();
			HudCompat.DiscoverFields();
			UiGroups.Enforce();
			HudCompat.Enforce();
		}
	}

	internal static ManualLogSource ModLog;

	/// <summary>旧式联动兼容字段（v1 时代读它的 mod 用；v4 无主开关，恒为 true = 不隐藏）。</summary>
	internal static bool HudEnabled = true;

	internal static ConfigEntry<bool> enabled;

	/// <summary>0.9.15 兼容：游戏更新移除了 PhaseBarGUI 类型——不存在时跳过全部 PhaseBar 相关 patch。</summary>
	internal static readonly bool PhaseBarPresent = AccessTools.TypeByName("PhaseBarGUI") != null;

	public override void Load()
	{
		ModLog = this.Log;
		enabled = Config.Bind("General", "enabled", true, "Master switch for UI hiding.");
		if (!PhaseBarPresent) ModLog.LogWarning("PhaseBarGUI not found in this game version — phase bar hiding disabled (game removed the class).");
		HudCompat.Cfg = Config;
		UiGroups.Cfg = Config;
		// UI 分类注册（每类：勾选 = 始终隐藏且严格生效；默认全部不勾选）
		UiGroups.Register("hints", "interaction hints", UiHiders.SetHints);
		UiGroups.Register("notifications", "notification popups", UiHiders.SetNotifications);
		UiGroups.Register("objectiveBanner", "objective banners & short messages", UiHiders.SetBanners);
		UiGroups.Register("hud", "player HUD (ammo, weapon, squad)", UiHiders.SetPlayerHud);
		UiGroups.Register("phaseBar", "phase bar", UiHiders.SetPhaseBar);
		UiGroups.Register("objectives", "objective indicator & mission status", UiHiders.SetObjectives);
		UiGroups.Register("map", "map & minimap", UiHiders.SetMap);
		UiGroups.Register("vehicle", "vehicle HUD", UiHiders.SetVehicle);
		UiGroups.Register("misc", "misc UI (version, tutorial, prompts)", UiHiders.SetMisc);
		UiGroups.Register("hitmarker", "hitmarkers & crosshair", UiHiders.SetHitmarker);
		UiGroups.Register("bloodSplash", "blood splash effect", UiHiders.SetBloodSplash);
		UiGroups.Register("chat", "chat messages", UiHiders.SetChat);
		UiGroups.Register("scope", "scope overlay", UiHiders.SetScope);
		UiGroups.Register("worldMarkers", "world markers (following markers)", UiHiders.SetWorldMarkers);
		new Harmony("com.ryan.er2.nointeractionhints").PatchAll();
		// 0.9.15：PhaseBar patch 条件注册（类型被游戏移除时不挂）
		UiPatches.PatchPhaseBar(new Harmony("com.ryan.er2.nointeractionhints"));
		RehidePatches.PatchPhaseBarRehide(new Harmony("com.ryan.er2.nointeractionhints"));
		// 预注册已安装的生态 mod + 惯例字段扫描（Load 先跑一遍，设置界面/战斗帧补扫）
		HudCompat.PreregisterKnownMods();
		HudCompat.DiscoverFields();
		// 应用勾选的隐藏状态（元素不存在的场景由巡检补藏）
		UiGroups.ApplyAll();
		ModLog.LogInfo((object)"ER2 Hide Anything 4.5.3 loaded. Check boxes in Mod Manager to hide UIs (checked = hidden & locked).");
	}
}
