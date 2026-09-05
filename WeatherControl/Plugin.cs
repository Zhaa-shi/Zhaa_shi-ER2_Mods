using System;
using System.Reflection;
using System.Runtime.InteropServices;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Corvostudio.SuperDayNightCycle;
using HarmonyLib;
using UnityEngine;

namespace ER2WeatherControl;

[BepInPlugin("er2.weathercontrol", "ER2 Weather Control", "1.7.2")]
public class Plugin : BasePlugin
{
	internal static ManualLogSource ModLog;

	internal static ConfigEntry<string> weatherMode;

	internal static ConfigEntry<string> atmospherePreset;

	internal static ConfigEntry<bool> enabled;

	internal static string notifyMessage = "";

	internal static float notifyUntil;

	private static string lastNotifyText = "";

	private static float lastNotifyTime = -10f;

	internal static void Notify(string message)
	{
		// 同消息节流：2.5 秒内重复的同一条提示不再次入队（防狂按键刷爆原生 Hint 队列）
		if (message == lastNotifyText && Time.time - lastNotifyTime < 2.5f)
		{
			return;
		}
		lastNotifyText = message;
		lastNotifyTime = Time.time;
		notifyMessage = message;
		notifyUntil = Time.time + 3f;
	}

	// 联动：ER2 No Interaction Hints v2（F5 隐藏 HUD）时隐藏本 mod 的屏幕提示
	// （每 mod 独立开关：ModManager → No Interaction Hints → Hide Other Mods UI）
	internal static bool IsInteractionHudHidden()
	{
		return ER2Shared.NoHintsHudLink.IsHidden("er2.weathercontrol", "ER2 Weather Control");
	}

	public override void Load()
	{
		ModLog = this.Log;
		enabled = Config.Bind("General", "enabled", true, "Master switch for weather control.");
		weatherMode = Config.Bind("Weather", "weatherMode", "Default", new ConfigDescription("Weather applied in battle: Default (map's own weather), Clear, Rain, Snow. Changes apply immediately.", new AcceptableValueList<string>("Default", "Clear", "Rain", "Snow")));
		atmospherePreset = Config.Bind("Atmosphere", "atmospherePreset", "Default", new ConfigDescription("Time of day / atmosphere applied in battle. Changes apply immediately.", new AcceptableValueList<string>("Default", "Midday", "Sunset", "Dawn", "Night", "Cloudy", "Foggy")));
		new Harmony("er2.weathercontrol").PatchAll(Assembly.GetExecutingAssembly());
		ModLog.LogInfo((object)("ER2 Weather Control 1.7.2 loaded. Weather: " + weatherMode.Value + ", Atmosphere: " + atmospherePreset.Value));
	}
}

[HarmonyPatch(typeof(MatchData), "StartBattleLoading")]
public class BattleStartPatch
{
	private static void Postfix()
	{
		WeatherApply.ResetForNewBattle();
	}
}

[HarmonyPatch(typeof(DayNightCycle), "set_WeatherType")]
public class WeatherSetBlockPatch
{
	private static bool Prefix()
	{
		if (WeatherApply.applying)
		{
			return true;
		}
		if (WeatherApply.HasActiveWeather())
		{
			Plugin.ModLog.LogInfo((object)"Blocked mission from changing weather.");
			return false;
		}
		return true;
	}
}

[HarmonyPatch(typeof(DayNightCycle), "SetDayTime", new Type[] { typeof(int), typeof(int) })]
public class SetDayTimeBlockPatch
{
	private static bool Prefix()
	{
		if (WeatherApply.applying)
		{
			return true;
		}
		if (WeatherApply.HasActiveAtmo())
		{
			Plugin.ModLog.LogInfo((object)"Blocked mission from changing time of day.");
			return false;
		}
		return true;
	}
}

[HarmonyPatch(typeof(DayNightCycle), "SetDayTime", new Type[] { typeof(int), typeof(int), typeof(float), typeof(float), typeof(float) })]
public class SetDayTimeFullBlockPatch
{
	private static bool Prefix()
	{
		if (WeatherApply.applying)
		{
			return true;
		}
		if (WeatherApply.HasActiveAtmo())
		{
			Plugin.ModLog.LogInfo((object)"Blocked mission from changing time of day (full).");
			return false;
		}
		return true;
	}
}

[HarmonyPatch(typeof(BattleManager), "Update")]
public class WeatherApplyPatch
{
	private static void Postfix()
	{
		WeatherApply.Tick();
	}
}

[HarmonyPatch(typeof(BattleManager), "OnGUI")]
public class WeatherNotifyGuiPatch
{
	private static GUIStyle notifyStyle;

	private static bool notifyStyleFailed;

	private static void Postfix()
	{
		try
		{
			if (Plugin.notifyUntil <= 0f || Time.time > Plugin.notifyUntil)
			{
				return;
			}
			if (Plugin.IsInteractionHudHidden())
			{
				return;
			}
			string msg = Plugin.notifyMessage;
			if (string.IsNullOrEmpty(msg))
			{
				return;
			}
			// 优先走游戏原生提示弹窗（Corvostudio.UI.Hint）——观感与原生教程提示一致
			if (NativeUi.ShowNativeHint(msg, 3f))
			{
				Plugin.notifyUntil = 0f; // 已交给原生队列，清除本 mod 重绘标记，避免每帧重复入队
				return;
			}
			// 回退：原生字体 + 游戏自己的描边文字（GuiExtension.OutlinedLabel）
			if (notifyStyle == null && !notifyStyleFailed)
			{
				try
				{
					notifyStyle = NativeUi.MakeStyle(26, FontStyle.Bold, NativeUi.NativeTextColor(), TextAnchor.MiddleCenter, wordWrap: true);
				}
				catch
				{
					notifyStyleFailed = true;
				}
			}
			float w = 700f;
			float h = 40f;
			float x = (Screen.width - w) / 2f;
			float y = Screen.height * 0.55f;
			Color prev = GUI.color;
			if (notifyStyle == null)
			{
				GUI.color = new Color(0.7f, 0.85f, 1f);
				GUI.Label(new Rect(x, y, w, h), msg);
				GUI.color = prev;
				return;
			}
			NativeUi.OutlinedLabel(new Rect(x, y, w, h), msg, notifyStyle, 1);
			GUI.color = prev;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Weather notify GUI error: " + ex.Message));
		}
	}
}

public static class WeatherApply
{
	internal static bool applying;

	private static float checkTimer;

	private static float reapplyTimer;

	private static string activeWeather;

	private static string activeAtmo;

	private static bool firstApplied;

	internal static void ResetForNewBattle()
	{
		activeWeather = null;
		activeAtmo = null;
		firstApplied = false;
		checkTimer = 0f;
		reapplyTimer = 0f;
	}

	/// <summary>热生效：配置值变化即重新应用（战斗中和设置界面里改配置都能立即生效）。</summary>
	internal static void Tick()
	{
		try
		{
			if (!Plugin.enabled.Value)
			{
				return;
			}
			checkTimer += Time.deltaTime;
			if (checkTimer < 0.5f)
			{
				return;
			}
			checkTimer = 0f;
			DayNightCycle dnc = DayNightCycle.instance;
			if (dnc == null)
			{
				return;
			}
			string w = Plugin.weatherMode.Value;
			string a = Plugin.atmospherePreset.Value;
			bool changed = false;
			if (w != activeWeather)
			{
				activeWeather = w;
				if (w != "Default")
				{
					ApplyWeather(w);
				}
				changed = true;
			}
			if (a != activeAtmo)
			{
				activeAtmo = a;
				if (a != "Default")
				{
					ApplyAtmosphere(dnc, a);
				}
				changed = true;
			}
			if (changed)
			{
				if (firstApplied)
				{
					Plugin.Notify("Weather: " + w + " / " + a);
				}
				firstApplied = true;
				Plugin.ModLog.LogInfo((object)("Weather applied: " + w + " / " + a + "."));
			}
			TickReapply(dnc);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Weather apply error: " + ex.Message));
		}
	}

	private static void TickReapply(DayNightCycle dnc)
	{
		reapplyTimer += Time.deltaTime;
		if (reapplyTimer < 3f)
		{
			return;
		}
		reapplyTimer = 0f;
		try
		{
			if (dnc == null)
			{
				return;
			}
			if (activeWeather != null && activeWeather != "Default" && WeatherToEnum(activeWeather) != DayNightCycle.lastSetWeather)
			{
				ApplyWeather(activeWeather);
				Plugin.ModLog.LogInfo((object)("Weather was reset by the mission - re-applied " + activeWeather + "."));
			}
			if (activeAtmo != null && activeAtmo != "Default")
			{
				ApplyAtmosphere(dnc, activeAtmo);
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Weather reapply error: " + ex.Message));
		}
	}

	private static WeatherType WeatherToEnum(string mode)
	{
		if (mode == "Rain")
		{
			return WeatherType.Raining;
		}
		if (mode == "Snow")
		{
			return WeatherType.Snowing;
		}
		return WeatherType.None;
	}

	internal static bool HasActiveWeather()
	{
		return activeWeather != null && activeWeather != "Default";
	}

	internal static bool HasActiveAtmo()
	{
		return activeAtmo != null && activeAtmo != "Default";
	}

	private static void ApplyWeather(string mode)
	{
		applying = true;
		try
		{
			if (mode == "Clear")
			{
				DayNightCycle.WeatherType = WeatherType.None;
			}
			else if (mode == "Rain")
			{
				DayNightCycle.WeatherType = WeatherType.Raining;
			}
			else if (mode == "Snow")
			{
				DayNightCycle.WeatherType = WeatherType.Snowing;
			}
		}
		finally
		{
			applying = false;
		}
	}

	private static void ApplyAtmosphere(DayNightCycle dnc, string preset)
	{
		applying = true;
		try
		{
			if (preset == "Midday")
			{
				dnc.SetDayTime(12, 0);
			}
			else if (preset == "Sunset")
			{
				dnc.SetDayTime(18, 30);
			}
			else if (preset == "Dawn")
			{
				dnc.SetDayTime(6, 0);
			}
			else if (preset == "Night")
			{
				dnc.SetDayTime(21, 30);
			}
			else if (preset == "Cloudy")
			{
				dnc.SetDayTime(12, 0, 0.75f, 0f, 0.05f);
			}
			else if (preset == "Foggy")
			{
				dnc.SetDayTime(8, 0, 0.3f, 0f, 0.8f);
			}
		}
		finally
		{
			applying = false;
		}
	}
}