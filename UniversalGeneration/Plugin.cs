using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ER2UniversalGeneration;

[BepInPlugin("er2.universalgeneration", "ER2 Universal Generation", "2.5.15")]
public class Plugin : BasePlugin
{
	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> enabled;
	internal static ConfigEntry<KeyCode> panelKey;
	internal static ConfigEntry<bool> debugLog;
	internal static ConfigEntry<bool> noAttackNeutral;
	internal static ConfigEntry<bool> enemyNativeAI; // 1.1.1：敌方生成物走原生 AI（推进/进攻）
	internal static ConfigEntry<string> favorites; // 收藏的条目 id（"v:Panther,i:usa_infantry"）
	internal static ConfigEntry<bool> uiMono;      // 2.5.0：灰黑单色 UI（与 SquadCommand 同名同义）
	internal static ConfigEntry<float> uiPanelAlpha; // 1.4.24：面板不透明度（与 SquadCommand 同名同义）

	public override void Load()
	{
		ModLog = Log;

		enabled = Config.Bind("General", "enabled", true, Ui.Tr("主开关。关闭后 mod 完全休眠。"));
		panelKey = Config.Bind("General", "panelKey", KeyCode.G, Ui.Tr("生成面板开关（仅战场指挥官 RTS 上帝视角内有效）。落点模式/携带物品中再按一次取消。"));
		noAttackNeutral = Config.Bind("General", "noAttackNeutral", true, Ui.Tr("生成的我方/敌方单位不主动攻击中立（Civilian）阵营（被打仍会还手；玩家手动标记的目标照打）。"));
		enemyNativeAI = Config.Bind("General", "enemyNativeAI", true, Ui.Tr("生成的敌方单位走原生 AI（主动推进、随战役任务进攻）。关闭后敌方与我方单位一样原地驻守、只接战不移动。"));
		debugLog = Config.Bind("Debug", "debugLog", false, new ConfigDescription(Ui.Tr("调试日志开关（发布版保持关闭）。输出枚举/反射/生成诊断。"), new AcceptableValueList<bool>(true, false)));
		favorites = Config.Bind("General", "favorites", "", Ui.Tr("收藏的生成条目（自动维护，勿手改）。"));
		// 2.5.0：与 SquadCommand 的 UI/uiMono 同名同义——两个 mod 的面板要长得一样，
		// 玩家改一个就该两边都变（共享 Er2Ui 令牌，只有这一处开关各读各的 cfg）。
		uiMono = Config.Bind("UI", "uiMono", true, Ui.Tr("半透明黑 UI（推荐，默认）。面板/列表/按钮走中性黑+半透明，靠明度与描边区分层次；关掉则回退旧版军绿配色。"));
		uiMono.SettingChanged += (s, e) => ER2Shared.Er2Ui.SetMono(uiMono.Value);
		ER2Shared.Er2Ui.SetMono(uiMono.Value);
		// 1.4.24：与 SquadCommand 同名同义（两个 mod 的面板要长得一样）
		uiPanelAlpha = Config.Bind("UI", "uiPanelAlpha", 0.72f, new ConfigDescription(Ui.Tr("面板不透明度（0.55~1.0）。越低越能透出战场，但面板越容易被地形颜色带偏；1.0 = 完全不透明。"), new AcceptableValueRange<float>(0.55f, 1f)));
		uiPanelAlpha.SettingChanged += (s, e) => ER2Shared.Er2Ui.SetPanelAlpha(uiPanelAlpha.Value);
		// 1.4.33：**cfg 默认值迁移**——BepInEx 的 cfg 一旦生成就不随代码默认值更新，
		// 老用户本地仍是旧默认 0.85；命中旧默认值（＝从未自定义）时更新为新默认 0.72。
		if (Mathf.Abs(uiPanelAlpha.Value - 0.85f) < 0.001f) uiPanelAlpha.Value = 0.72f;
		ER2Shared.Er2Ui.SetPanelAlpha(uiPanelAlpha.Value);

		HostLink.Init();
		GenCatalog.LoadFavorites();
		ItemCatalog.LoadFavs(); // 2.1.0：物品收藏（"t:<id>"，与单位收藏共存于同一配置项）
		GenRunner.Ensure();             // 协程宿主（生成协程驱动）
		GenCatalog.BeginStartupProbe(); // 1.3.2：目录在游戏启动时后台分帧探测（面板打开零卡顿）
		// 2.0.2：物品目录改为**运行时 ItemsDatabase 枚举**（替换 2.0.0 的磁盘 manifest 解析）
		// 2.0.3：修正就绪闸门（2.0.2 的 `l != null` 被空数组骗过 → 物品页签建不起来）。
		// 现在等 `ItemsDatabase.Loaded` + 实枚举非空，且 0 条不再永久放弃。
		// 根因：manifest 文件名 ≠ 运行时数据库键，导致"只有带图标的条目能生成"。
		// 现在直接向游戏要条目 → 列表里只会出现数据库里真实存在的物品。
		// 2.0.4：修正轮询节奏（2.0.3 的 WaitForSeconds(2f) 太慢，实测输给场景切换被杀）。
		// 改为**每帧轮询 + 心跳判活看门狗**；枚举预算只在数据库就绪后才起算。
		// 2.0.5：**彻底移除"自动放弃"**（2.0.4 的看门狗在场景加载期间误判"心跳停跳"并重启，
		// 6 次触顶后 probeState=3 永久放弃 → 物品页签永远建不起来 = 用户看到的症状）。
		// 2.0.6：但 2.0.5 "不判死只续跑"又矫枉过正 —— 协程被场景切换杀死时
		// probeState 永远卡在 1，看门狗认为"它在跑"，一次都不重启（实测日志无任何诊断行）。
		// 现改为 **tick 计数判活**（协程每帧 probeTicks++，看门狗比对是否增长）——
		// 与时间无关，场景加载/墙上时钟都骗不过它。
		// 2.0.7：tick 判活生效后，**真正的病根终于被 2.0.6 的诊断日志照出来**：
		// 前四轮修的全是"协程活不活"，而就绪闸门本身条件就是错的 ——
		// `ItemsDatabase.Loaded` 恒为 true，但 `GetAllItemsOfType<PropData>(PropType.items)`(=6)
		// **永远返回空数组**（连续 154s、跨 Menu→Aberdeen 三场景实测）。
		// 即 items(6) 是杂项总类、常态为空；可生成物实际在 weapons(7)/ammo(8)/attachment(9)。
		// 现 `ProbeDatabaseReady` 改为**四类任意非空即就绪**，并打印四类真实条数。
		ItemCatalog.onReady = () => GenPanel.RebuildItemTabsPublic();
		ItemCatalog.Ensure();
		new Harmony("er2.universalgeneration").PatchAll(typeof(Plugin).Assembly); // Tick/Draw 驱动补丁

		ModLog.LogInfo("ER2 Universal Generation 2.5.15 loaded. panelKey=" + panelKey.Value);
	}
}
