using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ER2UniversalGeneration;

[BepInPlugin("er2.universalgeneration", "ER2 Universal Generation", "1.0.0")]
public class Plugin : BasePlugin
{
	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> enabled;
	internal static ConfigEntry<KeyCode> panelKey;
	internal static ConfigEntry<bool> debugLog;
	internal static ConfigEntry<string> favorites; // 收藏的条目 id（"v:Panther,i:usa_infantry"）

	public override void Load()
	{
		ModLog = Log;

		enabled = Config.Bind("General", "enabled", true, Ui.Tr("主开关。关闭后 mod 完全休眠。"));
		panelKey = Config.Bind("General", "panelKey", KeyCode.G, Ui.Tr("生成面板开关（仅战场指挥官 RTS 上帝视角内有效）。落点模式中再按一次取消放置。"));
		debugLog = Config.Bind("Debug", "debugLog", false, new ConfigDescription(Ui.Tr("调试日志开关（发布版保持关闭）。输出枚举/反射/生成诊断。"), new AcceptableValueList<bool>(true, false)));
		favorites = Config.Bind("General", "favorites", "", Ui.Tr("收藏的生成条目（自动维护，勿手改）。"));

		HostLink.Init();
		GenCatalog.LoadFavorites();
		GenRunner.Ensure();   // 协程宿主（生成协程驱动）
		new Harmony("er2.universalgeneration").PatchAll(typeof(Plugin).Assembly); // Tick/Draw 驱动补丁

		ModLog.LogInfo("ER2 Universal Generation 1.0.0 loaded. panelKey=" + panelKey.Value);
	}
}
