using System;
using System.Collections.Generic;
using System.Linq;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Photon.Pun;

namespace ER2ThrowableWheel;

[BepInPlugin("er2.throwablewheel", "ER2 Throwable Wheel", "1.3.6")]
[BepInProcess("Easy Red 2.exe")]
public class ThrowableWheelPlugin : BasePlugin
{
	// ------------------------------------------------------------------
	// 语言构建：CN_BUILD 编译符号 = 中文版；否则英文版（与 ModManager 一致）
	// ------------------------------------------------------------------
#if CN_BUILD
	public const string PresetCustom = "自定义";
	public const string PresetClassic = "经典14";
	public const string PresetExpanded = "扩充34";
	public const string PresetAll = "全部48";
	public const string PresetAT = "反坦克";
	public const string PresetIncendiary = "燃烧";
	public const string PresetSmoke = "烟雾";

	private const string T_Enabled = "总开关。关闭后本 mod 完全不生效。";
	private const string T_Replace = "开启后先清空背包里的原生投掷物，再补入配置清单——转盘只显示配置项。";
	private const string T_Refill = "每 2 秒检查玩家背包，缺少的配置投掷物自动补入（扔完自动补充，等于无限弹药）。";
	private const string T_Mp = "默认联机时本 mod 不生效（避免公平性问题）。开启后联机同样生效。";
	private const string T_Preset = "快速选择一组投掷物清单：自定义 = 使用下方「自定义投掷物清单」手动配置。注意：条目过多（约 16 种以上）原生转盘可能无法正常显示，大预设请谨慎使用。";
	private const string T_ItemIds = "逗号分隔的投掷物 ID 列表，仅当「投掷物预设」为「自定义」时生效。可用 ID 见模组文档。";
#else
	public const string PresetCustom = "Custom";
	public const string PresetClassic = "Classic";
	public const string PresetExpanded = "Expanded";
	public const string PresetAll = "All";
	public const string PresetAT = "Anti-Tank";
	public const string PresetIncendiary = "Incendiary";
	public const string PresetSmoke = "Smoke";

	private const string T_Enabled = "Master switch. When false the mod is fully idle.";
	private const string T_Replace = "If true, vanilla throwables are removed from the player inventory and the wheel shows ONLY the configured items.";
	private const string T_Refill = "Re-checks and refills the player inventory every 2 seconds (items are re-added after being thrown).";
	private const string T_Mp = "By default the mod does nothing in online matches to avoid balance/integrity issues. Set true to also apply it in multiplayer sessions.";
	private const string T_Preset = "Quick loadout preset: Custom uses the ItemIds list below. Note: with too many items (roughly 16+) the native wheel may fail to display properly - use large presets with care.";
	private const string T_ItemIds = "Comma-separated throwable ID list, only used when the preset is Custom. See the mod documentation for available IDs.";
#endif

	// 经典 14 种（默认；转盘可正常显示的数量级）
	public const string DefaultIds =
		"grenade_ger,grenade_usa,grenade_eng,grenade_rus,grenade_jap,grenade_ita,molotov,at_hhl_3," +
		"grenade_rus_at,smokegrenade_ger,smokegrenade_usa,smokegrenade_eng_77,smokegrenade_rus,dynamite";

	private const string ExpandedIds =
		"grenade_ger,grenade_usa,grenade_eng,grenade_rus,grenade_jap,grenade_ita," +
		"grenade_f1,grenade_rg42,grenade_type91," +
		"molotov,kaenbin,dynamite,tnt,m37_satchel_charge,geballte_ladung_3kg," +
		"grenade_no74_sticky,grenade_gammon_n82,coconutgrenade_1," +
		"at_hhl_3,at_hhl_3_5,grenade_rus_at,grenade_ger_at,grenade_rpg40,grenade_rpg43," +
		"tankmine,tankmine_t99," +
		"smokegrenade_ger,smokegrenade_usa,smokegrenade_eng_77,smokegrenade_rus," +
		"smokegrenade_jap,smokegrenade_ita,smokegrenade_nhg42,smokegrenade_eng_79";

	private const string AllIds =
		"grenade_ger,grenade_usa,grenade_eng,grenade_rus,grenade_jap,grenade_ita," +
		"grenade_ger_m43,grenade_ger_m43_frag,grenade_ehg_mod_39,grenade_ehg_mod_39y," +
		"grenade_f1,grenade_f1_fr,grenade_rg42,grenade_type91,grenade_wz24,grenade_wz33," +
		"grenade_of_mle_1915,grenade_ita2,grenade_usa_y,grenade_no74_sticky," +
		"grenade_gammon_n82,coconutgrenade_1,coconutgrenade_2," +
		"molotov,kaenbin,dynamite,tnt,m37_satchel_charge,geballte_ladung_3kg," +
		"at_hhl_3,at_hhl_3_5,grenade_rus_at,grenade_ger_at,grenade_rpg40,grenade_rpg43," +
		"tankmine,tankmine_t99," +
		"smokegrenade_ger,smokegrenade_usa,smokegrenade_eng_77,smokegrenade_eng_79," +
		"smokegrenade_rus,smokegrenade_jap,smokegrenade_ita,smokegrenade_ita2," +
		"smokegrenade_nhg42,smokegrenade_bk2h,grenade_mle1916_smoke_fr";

	private const string AtIds =
		"at_hhl_3,at_hhl_3_5,grenade_rus_at,grenade_ger_at,grenade_rpg40,grenade_rpg43,tankmine,tankmine_t99";

	private const string IncendiaryIds = "molotov,kaenbin";

	private const string SmokeIds =
		"smokegrenade_ger,smokegrenade_usa,smokegrenade_eng_77,smokegrenade_eng_79," +
		"smokegrenade_rus,smokegrenade_jap,smokegrenade_ita,smokegrenade_nhg42,grenade_mle1916_smoke_fr";

	private static readonly Dictionary<string, string> Presets = new Dictionary<string, string>(StringComparer.Ordinal)
	{
		{ PresetClassic, DefaultIds },
		{ PresetExpanded, ExpandedIds },
		{ PresetAll, AllIds },
		{ PresetAT, AtIds },
		{ PresetIncendiary, IncendiaryIds },
		{ PresetSmoke, SmokeIds },
	};

	internal static ThrowableWheelPlugin Instance { get; private set; }
	internal static ManualLogSource Logger => Instance.Log;

	internal ConfigEntry<bool> Enabled;
	internal ConfigEntry<bool> ReplaceWheelContent;
	internal ConfigEntry<bool> RefillBeforeWheelOpens;
	internal ConfigEntry<bool> AllowInMultiplayer;
	internal ConfigEntry<string> LoadoutPreset;
	internal ConfigEntry<string> WheelItemIds;

	internal string[] ItemIds = Array.Empty<string>();

	public override void Load()
	{
		Instance = this;
		Enabled = Config.Bind("General", "Enabled", true, T_Enabled);
		ReplaceWheelContent = Config.Bind("General", "ReplaceWheelContent", false, T_Replace);
		RefillBeforeWheelOpens = Config.Bind("General", "RefillBeforeWheelOpens", true, T_Refill);
		AllowInMultiplayer = Config.Bind("General", "AllowInMultiplayer", false, T_Mp);
		LoadoutPreset = Config.Bind("Wheel", "LoadoutPreset", PresetClassic,
			new ConfigDescription(T_Preset,
				new AcceptableValueList<string>(new[] { PresetCustom, PresetClassic, PresetExpanded, PresetAll, PresetAT, PresetIncendiary, PresetSmoke })));
		WheelItemIds = Config.Bind("Wheel", "ItemIds", DefaultIds, T_ItemIds);

		// 即使初始为关闭也安装 patch：ModManager 里热开启时无需重启即可生效。
		// 各 patch 运行时会检查 Enabled.Value，关闭时保持完全空闲。
		if (!Enabled.Value)
		{
			Logger.LogInfo("ER2 Throwable Wheel is disabled by config. Patches stay installed for hot-enable via ModManager.");
		}

		RebuildItemIds();
		Logger.LogInfo($"ER2 Throwable Wheel loaded. preset={LoadoutPreset.Value} replace={ReplaceWheelContent.Value} refillOnOpen={RefillBeforeWheelOpens.Value} allowMp={AllowInMultiplayer.Value} ids={string.Join(",", ItemIds)}");

		new Harmony("er2.throwablewheel").PatchAll(typeof(ThrowableWheelPlugin).Assembly);
		Logger.LogInfo("ER2 Throwable Wheel patches installed.");

		var showCircle = AccessTools.Method(typeof(CircularMenuSelection.CircularMenu2), "ShowCircle");
		Logger.LogInfo($"ShowCircle target found: {showCircle != null}  ({showCircle})");
	}

	/// <summary>Rebuilds the effective item id list from the current config (preset or custom list). Called each tick so ModManager edits apply immediately.</summary>
	internal void RebuildItemIds()
	{
		try
		{
			string raw;
			string preset = LoadoutPreset != null ? LoadoutPreset.Value : PresetClassic;
			if (IsCustomPreset(preset) || !TryResolvePreset(preset, out raw))
				raw = WheelItemIds != null ? WheelItemIds.Value : DefaultIds;
			ItemIds = raw
				.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
				.Select(s => s.Trim())
				.Where(s => s.Length > 0)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToArray();
		}
		catch (Exception ex)
		{
			Logger.LogWarning($"RebuildItemIds failed: {ex.Message}");
		}
	}

	/// <summary>是否“自定义”预设（兼容中英文配置值，避免换语言构建后失效）。</summary>
	private static bool IsCustomPreset(string preset)
	{
		return preset == PresetCustom || preset == "Custom" || preset == "自定义";
	}

	/// <summary>
	/// 解析预设名到投掷物 ID 列表。先查当前构建语言的 Presets，再兼容另一种语言的预设名，
	/// 解决 EN/CN 构建切换后旧 cfg 里的预设名失效（除自定义/经典外全部回退成自定义清单）的问题。
	/// </summary>
	private static bool TryResolvePreset(string preset, out string ids)
	{
		if (Presets.TryGetValue(preset, out ids))
			return true;

		string canonical = null;
		if (preset == "Classic" || preset == "经典14")
			canonical = PresetClassic;
		else if (preset == "Expanded" || preset == "扩充34")
			canonical = PresetExpanded;
		else if (preset == "All" || preset == "全部48")
			canonical = PresetAll;
		else if (preset == "Anti-Tank" || preset == "反坦克")
			canonical = PresetAT;
		else if (preset == "Incendiary" || preset == "燃烧")
			canonical = PresetIncendiary;
		else if (preset == "Smoke" || preset == "烟雾")
			canonical = PresetSmoke;

		if (canonical != null)
			return Presets.TryGetValue(canonical, out ids);
		ids = null;
		return false;
	}

	internal static bool IsOnlineMultiplayer()
	{
		try
		{
			return PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.OfflineMode;
		}
		catch
		{
			return false;
		}
	}
}
