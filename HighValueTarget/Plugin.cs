using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Corvostudio.UI;
using Corvostudio.Weapons;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Photon.Pun;
using UnityEngine;

namespace ER2VeteranHVT;

// ─────────────────────────────────────────────────────────────
//  ER2 Veteran HVT（老兵高危目标：高危目标标记 + 老兵等级）
//  · 击杀追踪：每名士兵的击杀数 → 每 KillsPerLevel 杀升一级（老兵）
//  · 等级强化：随等级提升，受伤更少、火力更猛、更准、举枪射击更快、移速更快
//  · 仇恨聚焦：被标记单位的仇方 AI 优先攻击它（原生目标选择，无强制态副作用）
//  · 分级标记：不同等级不同标记（尺寸/描边），敌我颜色区分，同时显示
//  · 击杀反馈：玩家消灭高危目标 / 自己晋升时屏幕提示
//  · 3D 标记：世界空间广告牌 quad（近大远小 + 遮挡半透明），替代 IMGUI 屏幕投影
//  · 载具乘员：载具武器击杀计入全体乘员（等级同步提升），标记按载具合并为一枚
// ─────────────────────────────────────────────────────────────

[BepInPlugin("er2.highvaluetarget", "ER2 Veteran HVT", "1.2.2")]
[BepInProcess("Easy Red 2.exe")]
public class Plugin : BasePlugin
{
#if CN_BUILD
	internal const bool DefaultChinese = true;
#else
	internal const bool DefaultChinese = false;
#endif

	public static ManualLogSource ModLog;

	/// <summary>友军保护穿透用的临时阵营标记（必须与任何真实阵营字符串不同）。</summary>
	internal const string FfMarkerFaction = "__hvt_friendly_fire__";

	// ── 以下成员 public：供内部测试驱动插件（HvtTestDriver）直接调用 ──

	/// <summary>阵营 → 阵营方（"_" 后缀：Germany_axis → axis；无下划线返回原串）。</summary>
	public static string SideOf(string faction)
	{
		if (string.IsNullOrEmpty(faction))
		{
			return "";
		}
		int i = faction.LastIndexOf('_');
		return (i >= 0 && i < faction.Length - 1) ? faction.Substring(i + 1) : faction;
	}

	public static Soldier ControlledSoldier()
	{
		try
		{
			PlayerController pc = PlayerController.currentController;
			if (pc == null || pc.ControlledCharacter == null)
			{
				return null;
			}
			return pc.ControlledCharacter.TryCast<Soldier>();
		}
		catch
		{
			return null;
		}
	}

	public static UnitState GetState(Soldier s)
	{
		int id = s.GetInstanceID();
		if (!UnitStates.TryGetValue(id, out UnitState st))
		{
			st = new UnitState();
			UnitStates[id] = st;
		}
		return st;
	}

	// ── Config ──
	internal static ConfigEntry<bool> Enabled;
	internal static ConfigEntry<bool> ApplyInMultiplayer;

	internal static ConfigEntry<int> KillsPerLevel;
	internal static ConfigEntry<int> MaxLevel;
	internal static ConfigEntry<float> PlayerHitWindow;
	internal static ConfigEntry<int> FriendlyKillsToMark;
	internal static ConfigEntry<bool> TraitorFeature;

	internal static ConfigEntry<float> DmgTakenPerLevel;
	internal static ConfigEntry<float> DmgDealtPerLevel;
	internal static ConfigEntry<float> AccPerLevel;
	internal static ConfigEntry<float> FireRatePerLevel;
	internal static ConfigEntry<float> RaisePerLevel;
	internal static ConfigEntry<float> SpeedPerLevel;
	internal static ConfigEntry<bool> SuppressionImmune;
	internal static ConfigEntry<bool> NeverSurrender;

	internal static ConfigEntry<bool> FocusEnabled;
	internal static ConfigEntry<float> FocusRadius;
	internal static ConfigEntry<float> FocusChance; // 兼容保留（聚焦走原生目标选择，不再硬锁）

	internal static ConfigEntry<bool> ShowMarkers;
	internal static ConfigEntry<float> MarkerRange;
	internal static ConfigEntry<bool> MiniMapIcons;
	internal static ConfigEntry<bool> PlayerWarnings;
	internal static ConfigEntry<bool> PlayerIndicator;
	internal static ConfigEntry<bool> KillFeedback;
	internal static ConfigEntry<bool> LevelUpFeedback;

	// ── Debug ──

	/// <summary>调试日志开关（AGENTS.md §7.1 统一约定：节名 Debug / 键名 debugLog / 默认 false）。</summary>
	internal static ConfigEntry<bool> DebugLog;
	/// <summary>门控后的调试输出判定；LogError/LogWarning 不受此门控。</summary>
	internal static bool DebugOn => DebugLog != null && DebugLog.Value;

	// ── Runtime state ──

	/// <summary>每个士兵的击杀/标记状态（InstanceID → state）。</summary>
	public sealed class UnitState
	{
		public int EnemyKills;
		public int FriendlyKills;
		public bool Traitor;
		public readonly HashSet<string> HatedBySides = new HashSet<string>(); // 仇恨来源方（被击杀方）
	}

	internal static readonly Dictionary<int, UnitState> UnitStates = new Dictionary<int, UnitState>();

	/// <summary>最后命中归属表：受害者 InstanceID → (攻击者, 时间)。25s 窗口。</summary>
	internal sealed class HitRecord
	{
		public Soldier Attacker;
		public float Time;
	}

	internal static readonly Dictionary<int, HitRecord> LastHits = new Dictionary<int, HitRecord>();

	/// <summary>玩家命中时间表：受害者 InstanceID → 玩家最后一次命中时间（击杀归属兜底，防被抢）。</summary>
	internal static readonly Dictionary<int, float> PlayerHits = new Dictionary<int, float>();

	/// <summary>已计数的死亡（防 Kill + KillSynched 双计）。</summary>
	internal static readonly HashSet<int> CountedDeaths = new HashSet<int>();

	/// <summary>近战上下文：Soldier.Melee() 后 0.8s 内的攻击者。</summary>
	internal static Soldier MeleeAttacker;
	internal static float MeleeTime;

	/// <summary>子弹上下文：BulletInstance.OnHit 处理期间的射手（HitPart 结算加伤用）。</summary>
	internal static Soldier BulletShooter;
	internal static float BulletTime;

	/// <summary>标记单位的 Spottable/Lua 包装缓存（InstanceID → 包装）。</summary>
	internal static readonly Dictionary<int, Lua_Soldier> LuaCache = new Dictionary<int, Lua_Soldier>();

	/// <summary>标记时的初始移速基准（InstanceID → base），维护循环按倍率回写。</summary>
	internal static readonly Dictionary<int, float> SpeedBases = new Dictionary<int, float>();

	/// <summary>当前活着的被标记单位（tick 重建，供 GetBestVisibleEnemy 覆盖用，避免每帧扫描）。</summary>
	internal static readonly List<Soldier> MarkedAliveSoldiers = new List<Soldier>();

	// ── 击杀音效（BF1 kill confirm） ──

	internal static AudioClip KillClip;
	internal static bool KillClipLoading;

	/// <summary>同步加载击杀音效：WAV 16-bit PCM 自解码 + AudioClip.Create。
	/// （interop 裁剪了 UnityWebRequestMultimedia/WWW 的音频加载构造器——Method not found 实测，
	/// 走 BattlefieldHud 验证过的 WAV 自解码方案。）</summary>
	internal static void StartKillSoundLoad()
	{
		try
		{
			if (KillClip != null || KillClipLoading)
			{
				return;
			}
			KillClipLoading = true;
			string path = Path.Combine(Paths.PluginPath, "ER2_VeteranHVT", "bf1_kill.wav");
			if (!File.Exists(path))
			{
				ModLog.LogWarning($"[HVT] kill sound file missing: {path}");
				KillClipLoading = false;
				return;
			}
			byte[] data = File.ReadAllBytes(path);
			if (data.Length < 44 || data[0] != (byte)'R' || data[1] != (byte)'I' ||
				data[2] != (byte)'F' || data[3] != (byte)'F')
			{
				ModLog.LogWarning("[HVT] kill sound: not a RIFF file");
				KillClipLoading = false;
				return;
			}
			int channels = BitConverter.ToInt16(data, 22);
			int sampleRate = BitConverter.ToInt32(data, 24);
			int bitsPerSample = BitConverter.ToInt16(data, 34);
			int dataOffset = 12; // RIFF 头之后第一个 chunk（标准 PCM WAV 的 data 从 36 开始，不能从 44 找——实测）
			// 定位 data chunk（兼容含额外 chunk 的 WAV）
			while (dataOffset + 8 <= data.Length)
			{
				if (data[dataOffset] == (byte)'d' && data[dataOffset + 1] == (byte)'a' &&
					data[dataOffset + 2] == (byte)'t' && data[dataOffset + 3] == (byte)'a')
				{
					break;
				}
				int chunkSize = BitConverter.ToInt32(data, dataOffset + 4);
				dataOffset += 8 + chunkSize;
			}
			if (dataOffset + 8 > data.Length)
			{
				ModLog.LogWarning("[HVT] kill sound: data chunk not found");
				KillClipLoading = false;
				return;
			}
			int dataSize = BitConverter.ToInt32(data, dataOffset + 4);
			dataOffset += 8;
			if (channels <= 0 || sampleRate <= 0 || bitsPerSample != 16)
			{
				ModLog.LogWarning($"[HVT] kill sound: unsupported format ch={channels} rate={sampleRate} bits={bitsPerSample}");
				KillClipLoading = false;
				return;
			}
			int sampleCount = dataSize / 2;
			float[] samples = new float[sampleCount];
			for (int i = 0; i < sampleCount; i++)
			{
				short s = BitConverter.ToInt16(data, dataOffset + i * 2);
				samples[i] = s / 32768f;
			}
			AudioClip clip = AudioClip.Create("bf1_kill", sampleCount / channels, channels, sampleRate, false);
			clip.SetData(samples, 0);
			clip.hideFlags = (HideFlags)61; // 陷阱 41：运行时资源防场景卸载
			KillClip = clip;
			if (DebugOn)
			{
				ModLog.LogInfo($"[HVT] kill sound loaded (ch={channels} rate={sampleRate} len={sampleCount / (float)sampleRate:F2}s)");
			}
		}
		catch (Exception ex)
		{
			ModLog.LogWarning($"[HVT] kill sound load failed: {ex.Message}");
		}
		KillClipLoading = false;
	}

	/// <summary>在指定位置播放击杀音效（BF1 kill confirm；300m 可听距离覆盖远距离击杀）。</summary>
	internal static void PlayKillSound(Vector3 pos)
	{
		try
		{
			if (KillClip == null)
			{
				return;
			}
			SoundManager.SpawnAndPlay(pos, KillClip, 300f, 1f);
		}
		catch
		{
		}
	}

	internal static string T(string cn, string en) => DefaultChinese ? cn : en;

	public override void Load()
	{
		ModLog = Log;

		Enabled = Config.Bind("General", "Enabled", true, T(
			"总开关：关闭后不再计数/标记/聚焦/强化。",
			"Master switch. When off, no counting/marking/focusing/buffing happens."));
		ApplyInMultiplayer = Config.Bind("General", "ApplyInMultiplayer", false, T(
			"联机对局是否也启用本 mod（默认仅单机）。",
			"Whether the mod also applies in multiplayer matches (default: singleplayer only)."));

		KillsPerLevel = Config.Bind("Veteran", "KillsPerLevel", 5, new ConfigDescription(T(
			"每击杀多少名敌人升一级（老兵等级，罗马数字 I-V 标记）。达到一级即被标记为高危目标。",
			"How many enemy kills advance one veteran level (marked with Roman numerals I-V). Reaching level 1 marks the unit as a High-Value Target."),
			new AcceptableValueRange<int>(1, 50)));
		MaxLevel = Config.Bind("Veteran", "MaxLevel", 5, new ConfigDescription(T(
			"老兵等级上限（最高五级，罗马数字 V）。",
			"Maximum veteran level (max five levels, Roman numeral V)."),
			new AcceptableValueRange<int>(1, 10)));
		PlayerHitWindow = Config.Bind("Veteran", "PlayerHitWindow", 5f, new ConfigDescription(T(
			"玩家命中窗口（秒）：玩家命中过的目标在此时间内死亡，即使最后一发不是玩家打的也算玩家击杀（防混战中归属被抢）。",
			"Player hit window (seconds): if the player hit a unit within this window before its death, the kill counts for the player even if someone else landed the final shot (prevents stolen kills in chaos)."),
			new AcceptableValueRange<float>(0.5f, 30f)));
		FriendlyKillsToMark = Config.Bind("Veteran", "FriendlyKillsToMark", 2, new ConfigDescription(T(
			"击杀多少名友军后玩家被标记为叛徒（友军反过来攻击玩家；叛徒不显示头顶标记）。",
			"How many friendly kills mark the player as a TRAITOR (allies turn on the player; traitors get no head marker)."),
			new AcceptableValueRange<int>(1, 20)));
		TraitorFeature = Config.Bind("Veteran", "TraitorFeature", true, T(
			"叛徒机制：开启后玩家可以击杀友军（友军伤害保护对本 mod 失效），击杀达到阈值后友军反过来攻击玩家。",
			"Traitor mechanic: when on, the player CAN damage allies (friendly-fire protection is bypassed for this mod), and after enough friendly kills the allies turn on the player."));

		DmgTakenPerLevel = Config.Bind("Veteran", "DmgTakenPerLevel", 0.85f, new ConfigDescription(T(
			"每级受击伤害倍率：0.85 = 每级少受 15% 伤害（5 级约 0.44）。",
			"Damage-taken multiplier per level: 0.85 = 15% less damage each level (Lv5 ≈ 0.44)."),
			new AcceptableValueRange<float>(0.5f, 1f)));
		DmgDealtPerLevel = Config.Bind("Veteran", "DmgDealtPerLevel", 1.18f, new ConfigDescription(T(
			"每级造成伤害倍率：1.18 = 每级火力 +18%（子弹/手雷，5 级约 2.3 倍）。",
			"Damage-dealt multiplier per level: 1.18 = +18% firepower per level (bullets/grenades, Lv5 ≈ 2.3x)."),
			new AcceptableValueRange<float>(1f, 2f)));
		AccPerLevel = Config.Bind("Veteran", "AccPerLevel", 1.30f, new ConfigDescription(T(
			"每级命中率倍率：1.30 = 每级命中 +30%（步枪/机枪/火炮通用，5 级约 3.7 倍）。",
			"Accuracy multiplier per level: 1.30 = +30% accuracy per level (rifles, MGs, cannons, Lv5 ≈ 3.7x)."),
			new AcceptableValueRange<float>(1f, 2f)));
		FireRatePerLevel = Config.Bind("Veteran", "FireRatePerLevel", 1.15f, new ConfigDescription(T(
			"每级射速倍率：1.15 = 每级射击间隔缩短约 15%。",
			"Fire-rate multiplier per level: 1.15 = ~15% shorter shot interval per level."),
			new AcceptableValueRange<float>(1f, 2f)));
		RaisePerLevel = Config.Bind("Veteran", "RaisePerLevel", 1.15f, new ConfigDescription(T(
			"每级举枪速度倍率：1.15 = 每级从发现目标到开火更快（游戏无独立举枪 API，与射击间隔合并实现）。",
			"Raise-speed multiplier per level: 1.15 = faster target-to-first-shot per level (the game has no separate raise API, so it scales with the engage delay)."),
			new AcceptableValueRange<float>(1f, 2f)));
		SpeedPerLevel = Config.Bind("Veteran", "SpeedPerLevel", 1.05f, new ConfigDescription(T(
			"每级移动速度倍率：1.05 = 每级移速 +5%（若游戏 AI 每帧覆盖该值则自动失效）。",
			"Movement speed multiplier per level: 1.05 = +5% speed per level (silently no-ops if the game AI overwrites the value every frame)."),
			new AcceptableValueRange<float>(1f, 1.5f)));
		SuppressionImmune = Config.Bind("Veteran", "SuppressionImmune", true, T(
			"老兵（≥1 级）无视压制：被火力压制时继续还击。",
			"Veterans (level 1+) ignore suppression: they keep fighting instead of cowering."));
		NeverSurrender = Config.Bind("Veteran", "NeverSurrender", true, T(
			"老兵（≥1 级）绝不投降。",
			"Veterans (level 1+) never surrender."));

		FocusEnabled = Config.Bind("Focus", "FocusEnabled", true, T(
			"仇恨聚焦：被标记单位的仇方（被它击杀的一方）AI 优先攻击它。",
			"Focus fire: AI of the side the marked unit has been killing prioritizes it."));
		FocusRadius = Config.Bind("Focus", "FocusRadius", 500f, new ConfigDescription(T(
			"仇恨聚焦生效半径（米）：范围内仇方 AI 会把标记单位当作最佳可见敌人。",
			"Focus radius in meters: hostile AI inside this range treat the marked unit as their best visible enemy."),
			new AcceptableValueRange<float>(10f, 2000f)));
		FocusChance = Config.Bind("Focus", "FocusChance", 0.35f, new ConfigDescription(T(
			"兼容保留（旧版硬锁定概率；现聚焦走原生目标选择，此值不再使用）。",
			"Kept for compatibility (legacy hard-lock chance; focus now uses the native target selection)."),
			new AcceptableValueRange<float>(0.05f, 1f)));

		ShowMarkers = Config.Bind("Visual", "ShowMarkers", true, T(
			"在被标记单位/载具上方显示 3D 分级标记（世界空间广告牌，恒定屏占比，被遮挡时自然消失；载具乘员合并为载具单一标记，等级取乘员最高）。",
			"Draw 3D level markers above marked units/vehicles (world-space billboard, constant on-screen size, naturally hidden when occluded; vehicle crew merge into one vehicle marker showing the highest crew level)."));
		MarkerRange = Config.Bind("Visual", "MarkerRange", 500f, new ConfigDescription(T(
			"高危目标标记显示距离（米）：超过该距离不显示标记。",
			"Marker display range in meters: marked units beyond this range show no marker."),
			new AcceptableValueRange<float>(10f, 1000f)));
		MiniMapIcons = Config.Bind("Visual", "MiniMapIcons", true, T(
			"小地图上显示高危目标图标（敌浅红/友天蓝）。",
			"Show High-Value Target icons on the minimap (enemy light-red / friendly sky-blue)."));
		PlayerWarnings = Config.Bind("Visual", "PlayerWarnings", true, T(
			"玩家被标记时屏幕提示与红屏闪烁。",
			"Screen hint and red flash when the player gets marked."));
		PlayerIndicator = Config.Bind("Visual", "PlayerIndicator", true, T(
			"玩家被标记期间顶部常驻指示条（显示老兵等级）。",
			"Persistent top indicator with veteran level while the player is marked."));
		KillFeedback = Config.Bind("Visual", "KillFeedback", true, T(
			"玩家消灭高危目标时屏幕提示 + 金色闪烁。",
			"Screen hint and golden flash when the player eliminates a marked unit."));
		LevelUpFeedback = Config.Bind("Visual", "LevelUpFeedback", true, T(
			"玩家晋升老兵等级时屏幕提示。",
			"Screen hint when the player advances a veteran level."));

		DebugLog = Config.Bind("Debug", "debugLog", false, T(
			"输出调试日志（标记对账、击杀归属、叛徒判定等）。排查问题时临时开启。",
			"Enable debug logging (marker reconciliation, kill attribution, traitor decisions). Turn on temporarily when troubleshooting."));

		new Harmony("er2.highvaluetarget").PatchAll(GetType().Assembly);

		try
		{
			// 陷阱：IL2CPP 下托管 MonoBehaviour 必须先注册进 il2cpp domain 才能 AddComponent
			ClassInjector.RegisterTypeInIl2Cpp<HvtBehaviour>();
			GameObject go = new GameObject("ER2VeteranHVT");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.hideFlags = (HideFlags)61; // HideAndDontSave：跨场景存活
			go.AddComponent<HvtBehaviour>();
		}
		catch (Exception ex)
		{
			ModLog.LogWarning($"ER2 Veteran HVT: behaviour init failed: {ex.Message}");
		}

		ModLog.LogInfo("ER2 Veteran HVT 1.2.2 loaded.");
		StartKillSoundLoad();
	}

	// ── 判定辅助 ──

	internal static bool IsActive()
	{
		if (Enabled == null || !Enabled.Value)
		{
			return false;
		}
		if (ApplyInMultiplayer != null && !ApplyInMultiplayer.Value)
		{
			try
			{
				if (PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.OfflineMode)
				{
					return false;
				}
			}
			catch
			{
			}
		}
		return true;
	}

	public static bool IsPlayerUnit(Soldier s)
	{
		try
		{
			if (s == null)
			{
				return false;
			}
			Soldier controlled = ControlledSoldier();
			return controlled != null && controlled == s;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>士兵当前所在载具（不在载具/异常返回 null）。</summary>
	internal static Vehicle VehicleOf(Soldier s)
	{
		try
		{
			return s != null ? s.GetCurrentVehicle() : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>载具全体乘员（遍历 seats 的 unitSet，过滤空位与已死者）。</summary>
	internal static List<Soldier> VehicleCrew(Vehicle veh)
	{
		List<Soldier> crew = new List<Soldier>();
		try
		{
			if (veh == null)
			{
				return crew;
			}
			var seats = veh.seats;
			if (seats == null)
			{
				return crew;
			}
			for (int i = 0; i < seats.Length; i++)
			{
				try
				{
					Soldier u = seats[i].unitSet;
					if (u != null && !u.IsDead)
					{
						crew.Add(u);
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
		return crew;
	}

	internal static string FactionOfPlayer()
	{
		try
		{
			Soldier p = ControlledSoldier();
			if (p != null)
			{
				return p.faction;
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>玩家阵营方（自由镜头/死亡时用最近一次缓存，避免敌友区分失效）。</summary>
	internal static string LastPlayerSide = "";

	internal static string PlayerSide()
	{
		try
		{
			Soldier p = ControlledSoldier();
			if (p != null && !string.IsNullOrEmpty(p.faction))
			{
				LastPlayerSide = SideOf(p.faction);
			}
		}
		catch
		{
		}
		return LastPlayerSide;
	}

	/// <summary>同阵营伤害是否放行：玩家可伤友军（叛徒机制前提）；任何单位成为叛徒后同方火力对其放行。</summary>
	internal static bool ShouldAllowFriendlyDamage(Soldier attacker, Soldier victim)
	{
		if (attacker == null || victim == null || TraitorFeature == null || !TraitorFeature.Value)
		{
			return false;
		}
		try
		{
			string aSide = SideOf(attacker.faction);
			string vSide = SideOf(victim.faction);
			if (aSide != vSide || string.IsNullOrEmpty(vSide))
			{
				return false;
			}
			if (IsPlayerUnit(attacker))
			{
				return true; // 玩家子弹/近战/爆炸对友军放行
			}
			if (IsTraitor(victim))
			{
				return true; // 叛徒（无论玩家还是 AI）可被同方火力伤害（实测：不放开则友军打叛徒无伤害）
			}
		}
		catch
		{
		}
		return false;
	}

	internal static bool IsTraitor(Soldier s)
	{
		if (s == null)
		{
			return false;
		}
		try
		{
			if (UnitStates.TryGetValue(s.GetInstanceID(), out UnitState st))
			{
				return st.Traitor;
			}
		}
		catch
		{
		}
		return false;
	}

	internal static bool TryGetState(Soldier s, out UnitState st)
	{
		st = null;
		if (s == null)
		{
			return false;
		}
		try
		{
			return UnitStates.TryGetValue(s.GetInstanceID(), out st) && st != null;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>老兵等级 = 击杀数 / KillsPerLevel（截断，封顶 MaxLevel）。</summary>
	internal static int LevelOf(Soldier s)
	{
		if (s == null)
		{
			return 0;
		}
		try
		{
			if (UnitStates.TryGetValue(s.GetInstanceID(), out UnitState st))
			{
				int max = MaxLevel != null ? MaxLevel.Value : 5;
				int step = KillsPerLevel != null ? Mathf.Max(1, KillsPerLevel.Value) : 10;
				return Mathf.Min(max, st.EnemyKills / step);
			}
		}
		catch
		{
		}
		return 0;
	}

	/// <summary>等级 → 罗马数字（I-V，超出按 V 处理）。</summary>
	internal static string RomanNumeral(int level)
	{
		switch (level)
		{			case 1: return "I";
			case 2: return "II";
			case 3: return "III";
			case 4: return "IV";
			default: return "V";
		}
	}

	internal static bool IsMarked(Soldier s) => LevelOf(s) >= 1;

	internal static float Pow(float factor, int level)
	{
		if (level <= 0)
		{
			return 1f;
		}
		try
		{
			return Mathf.Pow(factor, level);
		}
		catch
		{
			return 1f;
		}
	}

	/// <summary>分级标记颜色：等级越高颜色越深（梯度加大，Lv1 最浅、MaxLevel 最深）。
	/// 敌方浅红→深红，友军天蓝→深蓝。（仅用于玩家顶部指示条文字等场合）</summary>
	internal static Color MarkerColor(bool enemy, int level, int maxLevel)
	{
		float t = maxLevel > 1 ? Mathf.Clamp01((level - 1f) / (maxLevel - 1f)) : 1f;
		if (enemy)
		{
			return Color.Lerp(new Color(1f, 0.85f, 0.8f), new Color(0.7f, 0.02f, 0.02f), t);
		}
		return Color.Lerp(new Color(0.75f, 0.93f, 1f), new Color(0f, 0.12f, 0.7f), t);
	}

	/// <summary>头顶标记固定背景色：全部等级统一用最深色（用户确认：固定 V 级深色，
	/// 等级靠罗马数字区分，不靠颜色深浅）。敌方深红 / 友军深蓝。</summary>
	internal static Color FixedMarkerColor(bool enemy)
	{
		return enemy ? new Color(0.7f, 0.02f, 0.02f) : new Color(0f, 0.12f, 0.7f);
	}

	/// <summary>从命中点解析受害者士兵（头盔等独立物件用 ItemHelmet.User 兜底）。</summary>
	internal static Soldier VictimFromHit(RaycastHit hit)
	{
		try
		{
			if (hit.collider == null)
			{
				return null;
			}
			Creature c = hit.collider.GetComponentInParent<Creature>();
			if (c != null)
			{
				return c.TryCast<Soldier>();
			}
			ItemHelmet helm = hit.collider.GetComponentInParent<ItemHelmet>();
			if (helm != null)
			{
				try
				{
					return helm.User;
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return null;
	}

	internal static Soldier PeekMeleeAttacker()
	{
		try
		{
			if (MeleeAttacker != null && Time.unscaledTime - MeleeTime < 0.8f)
			{
				return MeleeAttacker;
			}
		}
		catch
		{
		}
		return null;
	}

	internal static Soldier PeekBulletShooter()
	{
		try
		{
			if (BulletShooter != null && Time.unscaledTime - BulletTime < 0.5f)
			{
				return BulletShooter;
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>视线检查：viewer 眼睛到 target 胸口无遮挡（GetBestVisibleEnemy 覆盖防穿墙锁）。</summary>
	internal static bool HasLineOfSight(Soldier viewer, Soldier target)
	{
		try
		{
			if (viewer == null || target == null || viewer.transform == null || target.transform == null)
			{
				return false;
			}
			Vector3 from = viewer.transform.position + Vector3.up * 1.5f;
			Vector3 to = target.transform.position + Vector3.up * 1.2f;
			Vector3 dir = to - from;
			float d = dir.magnitude;
			if (d < 0.1f)
			{
				return true;
			}
			if (Physics.Raycast(from, dir / d, out RaycastHit hit, d))
			{
				Creature c = hit.collider.GetComponentInParent<Creature>();
				return c != null && c == target;
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>相机视线检查：从相机位置到目标胸口无遮挡（头顶标记遮挡半透明用）。
	/// 命中目标自身 collider 视为可见，命中其他物体视为被遮挡。</summary>
	internal static bool IsVisibleFromCamera(Soldier target, Camera cam)
	{
		try
		{
			if (target == null || cam == null || target.transform == null)
			{
				return true; // 保守：拿不到就按可见处理
			}
			Vector3 from = cam.transform.position;
			Vector3 to = target.transform.position + Vector3.up * 1.2f;
			Vector3 dir = to - from;
			float d = dir.magnitude;
			if (d < 0.1f)
			{
				return true;
			}
			if (Physics.Raycast(from, dir / d, out RaycastHit hit, d))
			{
				Creature c = hit.collider.GetComponentInParent<Creature>();
				return c != null && c == target;
			}
			return true;
		}
		catch
		{
			return true;
		}
	}

	internal static Spottable GetSpottable(Soldier s)
	{
		try
		{
			if (s == null || s.transform == null)
			{
				return null;
			}
			return Creature.GetConnectedSpottable(s.transform);
		}
		catch
		{
			return null;
		}
	}

	internal static Lua_Soldier GetLuaWrapper(Soldier s)
	{
		try
		{
			int id = s.GetInstanceID();
			if (LuaCache.TryGetValue(id, out Lua_Soldier lua) && lua != null)
			{
				return lua;
			}
			lua = new Lua_Soldier(s);
			LuaCache[id] = lua;
			return lua;
		}
		catch
		{
			return null;
		}
	}

	internal static void ShowHint(string text, float duration)
	{
		try
		{
			if (Hint.instance != null)
			{
				Hint.Display(text, duration, true, true);
			}
		}
		catch
		{
		}
	}

	/// <summary>屏幕底部提示（升级/击杀反馈专用，避免与顶部原生提示重叠）。</summary>
	internal static void ShowToast(string text, float duration)
	{
		try
		{
			HvtBehaviour.ShowToast(text, duration);
		}
		catch
		{
		}
	}

	internal static float LastHintTime;

	internal static bool HintThrottle(float interval)
	{
		try
		{
			float now = Time.unscaledTime;
			if (now - LastHintTime < interval)
			{
				return false;
			}
			LastHintTime = now;
			return true;
		}
		catch
		{
			return false;
		}
	}

	// ── 击杀归属 ──

	internal static void RecordHit(Creature victim, Soldier attacker)
	{
		try
		{
			if (victim == null || attacker == null)
			{
				return;
			}
			// 叛徒机制关闭时不记录同方命中：友军伤害本就被友军保护拦截（不掉血），
			// 但归属记录若照写，友军稍后死于其他原因（炮击/AI 互射）会被误归到
			// 玩家头上 → "友军伤害"计数 + 被标叛徒（v1.2.0 实测根因，源头堵住）
			if (TraitorFeature == null || !TraitorFeature.Value)
			{
				Soldier vs = victim.TryCast<Soldier>();
				if (vs != null && !string.IsNullOrEmpty(vs.faction) &&
					SideOf(vs.faction) == SideOf(attacker.faction))
				{
					return;
				}
			}
			LastHits[victim.GetInstanceID()] = new HitRecord { Attacker = attacker, Time = Time.unscaledTime };
			if (IsPlayerUnit(attacker))
			{
				PlayerHits[victim.GetInstanceID()] = Time.unscaledTime;
			}
		}
		catch
		{
		}
	}

	// ── 击杀计数与老兵等级 ──

	internal static void OnUnitKilled(Soldier victim)
	{
		try
		{
			if (victim == null)
			{
				return;
			}
			int vid = victim.GetInstanceID();
			if (!CountedDeaths.Add(vid))
			{
				return; // 已计（Kill/KillSynched 双路径）
			}
			// 击杀反馈用：受害者被标记时的等级（在状态清理前捕获）
			int victimLevel = LevelOf(victim);

			// 1) 命中归属（记录于伤害处理前，瞬杀也覆盖）
			Soldier attacker = null;
			if (LastHits.TryGetValue(vid, out HitRecord rec) && rec != null && rec.Attacker != null &&
				Time.unscaledTime - rec.Time <= 25f)
			{
				attacker = rec.Attacker;
				LastHits.Remove(vid);
			}
			// 2) 瞬杀兜底：死亡发生在子弹原生处理内部，若归属表仍查不到 → 击杀瞬间
			//    正在处理的子弹（OnHit Prefix 已设上下文）就是凶手的子弹
			if (attacker == null || attacker == victim)
			{
				Soldier ctx = PeekBulletShooter();
				if (ctx != null && ctx != victim)
				{
					attacker = ctx;
				}
			}
			if (attacker == null || attacker == victim)
			{
				return; // 环境击杀/无归属
			}

			// 载具乘员共享击杀：射手在载具内（坦克炮/同轴机枪/载具机枪等）时，
			// 击杀计入该载具全体乘员（等级同步提升，标记按载具合并显示）
			List<Soldier> crew = new List<Soldier>();
			HashSet<int> crewIds = new HashSet<int>();
			Vehicle attVeh = VehicleOf(attacker);
			if (attVeh != null)
			{
				foreach (Soldier c in VehicleCrew(attVeh))
				{
					if (c != null && crewIds.Add(c.GetInstanceID()))
					{
						crew.Add(c);
					}
				}
			}
			if (crewIds.Add(attacker.GetInstanceID()))
			{
				crew.Add(attacker);
			}
			if (crew.Count > 1)
			{
				foreach (Soldier c in crew)
				{
					CreditKill(c, victim, victimLevel);
				}
			}
			else
			{
				CreditKill(attacker, victim, victimLevel);
			}

			// 玩家命中窗口兜底：玩家在窗口内命中过该目标，即使最后一发被抢也算玩家击杀
			// 仅敌方击杀生效（防误伤窗口把友军误计为叛徒进度——叛徒只认玩家亲手击杀）；
			// 玩家是该载具乘员时已通过共享获得击杀，不再重复兜底
			Soldier player = ControlledSoldier();
			if (player != null && PlayerHits.TryGetValue(vid, out float phTime) &&
				Time.unscaledTime - phTime <= PlayerHitWindow.Value &&
				SideOf(victim.faction) != SideOf(player.faction) &&
				!crewIds.Contains(player.GetInstanceID()))
			{
				PlayerHits.Remove(vid);
				if (attacker != player)
				{
					CreditKill(player, victim, victimLevel);
				}
			}

			// 玩家死亡 → 清空玩家自身标记（重新做人，死亡即脱靶）
			if (IsPlayerUnit(victim))
			{
				ClearUnitState(vid);
			}
		}
		catch (Exception ex)
		{
			ModLog.LogWarning($"[HVT] OnUnitKilled exception: {ex.Message}");
		}
	}

	/// <summary>给攻击者计一次击杀并结算老兵等级/标记。
	/// 敌我按阵营方判定：跨方击杀 → 老兵经验；同方击杀（跨国家友军）不计数；
	/// 玩家同方击杀 → 叛徒进度（仅玩家能成为叛徒）。</summary>
	internal static void CreditKill(Soldier attacker, Soldier victim, int victimLevel)
	{
		try
		{
			string vSide = SideOf(victim.faction);
			if (string.IsNullOrEmpty(vSide))
			{
				return;
			}
			// 标记阵营污染防护（实测根因，v1.0.7 同款）：FF 放行窗口内 attacker.faction
			// 是标记值（__hvt_friendly_fire__），而放行的前提就是同阵营命中 →
			// 此时击杀必为友军击杀，不能按 SideOf(marker) 判成"敌方击杀"（否则杀友军会升老兵等级）
			bool friendly = attacker.faction == FfMarkerFaction || SideOf(attacker.faction) == vSide;

			// 同方击杀：不升老兵等级；仅玩家累计叛徒进度（AI 击杀同方=误伤，不计）
			if (friendly)
			{
				// 叛徒机制关闭：友军击杀完全不计数/不提示/不标记。伤害放行本就被
				// ShouldAllowFriendlyDamage 拦住，被拦截命中的残留归属记录也不该结算
				//（v1.2.0 实测：关掉机制仍被标叛徒 = 友军被拦截的命中记录 + 友军
				// 后续死于其他原因被误归到玩家头上）
				if (TraitorFeature == null || !TraitorFeature.Value)
				{
					return;
				}
				if (!IsPlayerUnit(attacker))
				{
					return;
				}
				UnitState tst = GetState(attacker);
				if (!tst.Traitor)
				{
					tst.FriendlyKills++;
					int remaining = FriendlyKillsToMark.Value - tst.FriendlyKills;
					if (PlayerWarnings != null && PlayerWarnings.Value && HintThrottle(3f))
					{
						if (remaining > 0)
						{
							// 底部 toast（顶部提示会与原生"占领中"重叠——用户实测）
							ShowToast(T($"⚠ 友军伤害！再击杀 {remaining} 名友军将被标记为叛徒！",
								$"⚠ FRIENDLY FIRE! {remaining} more friendly kill(s) and you will be marked a TRAITOR!"), 4f);
						}
					}
					if (tst.FriendlyKills >= FriendlyKillsToMark.Value)
					{
						tst.Traitor = true;
						tst.HatedBySides.Add(vSide); // 自己的阵营（=受害者阵营）反过来仇恨自己
					if (DebugOn)
				{
					ModLog.LogInfo($"[HVT] Marked TRAITOR: faction={attacker.faction}");
				}
						if (PlayerWarnings != null && PlayerWarnings.Value)
						{
							HvtBehaviour.FlashPlayer(new Color(1f, 0f, 0f, 0.22f), 1.0f);
							ShowToast(T("⚠ 你被标记为叛徒！友军将攻击你！", "⚠ YOU ARE MARKED AS A TRAITOR! YOUR ALLIES WILL ATTACK YOU!"), 6f);
						}
					}
				}
				return;
			}

			UnitState st = GetState(attacker);
			int oldLevel = Mathf.Min(MaxLevel.Value, st.EnemyKills / Mathf.Max(1, KillsPerLevel.Value));
			st.EnemyKills++;
			st.HatedBySides.Add(vSide); // 被击杀方仇恨该单位
			int newLevel = Mathf.Min(MaxLevel.Value, st.EnemyKills / Mathf.Max(1, KillsPerLevel.Value));

			if (newLevel > oldLevel)
			{
				OnMarked(attacker, newLevel);
				// 玩家晋升反馈：合并成一条 toast（升级 + 被标记警告，多行）；
				// 不再分开弹两条（用户实测：两条提示+顶部提示与原生"占领中"重叠）
				if (IsPlayerUnit(attacker))
				{
					bool warn = PlayerWarnings != null && PlayerWarnings.Value;
					bool fb = LevelUpFeedback != null && LevelUpFeedback.Value;
					if (warn)
					{
						HvtBehaviour.FlashPlayer(new Color(1f, 0f, 0f, 0.22f), 1.0f);
					}
					if (warn || fb)
					{
						string txt = "";
						if (fb)
						{
							txt += T($"⭐ 你晋升为 Lv.{RomanNumeral(newLevel)} 老兵！",
								$"⭐ YOU ARE NOW A Lv.{RomanNumeral(newLevel)} VETERAN!");
						}
						if (warn)
						{
							if (txt.Length > 0)
							{
								txt += "\n";
							}
							txt += T("⚠ 你已成为高危目标，敌军将集火你！",
								"⚠ YOU ARE NOW A HIGH-VALUE TARGET! ENEMIES WILL FOCUS YOU!");
						}
						if (txt.Length > 0)
						{
							ShowToast(txt, 4.5f);
						}
					}
				}
			}

			// 玩家消灭高危目标反馈（受害者被标记过，且确实是玩家击杀）
			if (victimLevel >= 1 && IsPlayerUnit(attacker) && KillFeedback != null && KillFeedback.Value && HintThrottle(2f))
			{
				HvtBehaviour.FlashPlayer(new Color(1f, 0.8f, 0.15f, 0.22f), 0.8f);
				ShowToast(T($"🎯 高危目标已消灭！(Lv.{RomanNumeral(victimLevel)} 老兵)", $"🎯 HIGH-VALUE TARGET ELIMINATED! (Lv.{RomanNumeral(victimLevel)} veteran)"), 3.5f);
				// 击杀音效只对敌方高危播放（杀友方高危不响）
				if (SideOf(victim.faction) != PlayerSide())
				{
					PlayKillSound(victim.transform != null ? victim.transform.position : Vector3.zero);
				}
			}
		}
		catch (Exception ex)
		{
			ModLog.LogWarning($"[HVT] CreditKill exception: {ex.Message}");
		}
	}

	internal static void ClearUnitState(int id)
	{
		UnitStates.Remove(id);
		LuaCache.Remove(id);
		SpeedBases.Remove(id);
	}

	private static void OnMarked(Soldier s, int level)
	{
		try
		{
			int id = s.GetInstanceID();
			if (!SpeedBases.ContainsKey(id))
			{
				try
				{
					SoldierAI ai = s.GetComponent<SoldierAI>();
					if (ai != null)
					{
						SpeedBases[id] = ai.speed;
					}
				}
				catch
				{
				}
			}
			string fac = "unknown";
			try
			{
				if (!string.IsNullOrEmpty(s.faction))
				{
					fac = s.faction;
				}
			}
			catch
			{
			}
			int markedCount = 0;
			foreach (var kv in UnitStates)
			{
				if (kv.Value != null && kv.Value.EnemyKills >= Mathf.Max(1, KillsPerLevel.Value))
				{
					markedCount++;
				}
			}
			if (DebugOn)
			{
				ModLog.LogInfo($"[HVT] Marked Lv.{level}: faction={fac} activeMarked={markedCount}");
			}
			// 玩家提示（红闪+toast）已合并进 CreditKill 的晋升反馈，避免同帧弹两条
		}
		catch
		{
		}
	}

	internal static void ResetAll()
	{
		try
		{
			UnitStates.Clear();
			LastHits.Clear();
			PlayerHits.Clear();
			CountedDeaths.Clear();
			LuaCache.Clear();
			SpeedBases.Clear();
			MarkedAliveSoldiers.Clear();
			MeleeAttacker = null;
			BulletShooter = null;
			HvtBehaviour.ClearPlayerFlash();
			HvtBehaviour.ClearToasts();
		}
		catch
		{
		}
	}
}

// ═════════════════════════════════════════════════════════════
//  对外老兵接口（VeteranApi）
// ═════════════════════════════════════════════════════════════

/// <summary>
/// 老兵系统的**公开门面**，供其他 mod 读写老兵等级/击杀（目前消费者：ER2 Conquest 征服模式）。
///
/// 为什么要有这一层：HVT 内部的 <c>LevelOf</c>/<c>TryGetState</c>/<c>UnitStates</c> 都是
/// <c>internal</c>，外部只能反射，而反射内部实现不稳定（HVT 一重构就断）。
/// 把耦合点收敛到这一个 public 类里，内部怎么改都不影响消费者。
///
/// 语义约定：
///   · 等级 0 = 普通单位，1..MaxLevel = 老兵（与 HVT 内部 <see cref="Plugin.LevelOf"/> 同源）
///   · 等级由击杀数推导（<c>EnemyKills / KillsPerLevel</c>，封顶 MaxLevel）
///   · **SetLevel 是"灌等级"**——把外部（如征服模式）持久化的等级写进刚生成的士兵，
///     这是跨战斗养成的关键：战斗是临时的，军队是持久的。
///   · 所有方法都自带 try/catch，**绝不抛异常给调用方**（对方 mod 不应因 HVT 内部问题而崩）
/// </summary>
public static class VeteranApi
{
	/// <summary>API 版本，供消费者做兼容判断。</summary>
	public const int ApiVersion = 1;

	/// <summary>老兵系统的等级上限。</summary>
	public static int MaxLevel
	{
		get
		{
			try { return Plugin.MaxLevel != null ? Plugin.MaxLevel.Value : 5; }
			catch { return 5; }
		}
	}

	/// <summary>升一级所需击杀数。</summary>
	public static int KillsPerLevel
	{
		get
		{
			try { return Plugin.KillsPerLevel != null ? Mathf.Max(1, Plugin.KillsPerLevel.Value) : 5; }
			catch { return 5; }
		}
	}

	/// <summary>老兵机制当前是否生效（受 HVT 总开关影响）。</summary>
	public static bool IsActive
	{
		get
		{
			try { return Plugin.IsActive(); }
			catch { return false; }
		}
	}

	// ---------------- 读 ----------------

	/// <summary>士兵当前老兵等级（0..MaxLevel）。</summary>
	public static int GetLevel(Soldier s)
	{
		try { return s == null ? 0 : Plugin.LevelOf(s); }
		catch { return 0; }
	}

	/// <summary>士兵累计击杀数。</summary>
	public static int GetKills(Soldier s)
	{
		try
		{
			if (s == null) return 0;
			Plugin.UnitState st;
			return Plugin.TryGetState(s, out st) ? st.EnemyKills : 0;
		}
		catch { return 0; }
	}

	/// <summary>士兵是否已被标记为高危目标（等级 ≥ 1）。</summary>
	public static bool IsMarked(Soldier s)
	{
		try { return s != null && Plugin.IsMarked(s); }
		catch { return false; }
	}

	// ---------------- 写 ----------------

	/// <summary>直接设定击杀数（等级随之重新推导）。</summary>
	public static bool SetKills(Soldier s, int kills)
	{
		try
		{
			if (s == null) return false;
			Plugin.UnitState st;
			if (!Plugin.TryGetState(s, out st)) return false;
			st.EnemyKills = Mathf.Max(0, kills);
			return true;
		}
		catch { return false; }
	}

	/// <summary>
	/// 灌入老兵等级（跨战斗养成的关键入口）：等级 → 反推击杀数写入。
	/// 注意这会覆盖该士兵原有的击杀计数——只应在**单位刚生成、尚未参战**时调用。
	/// </summary>
	public static bool SetLevel(Soldier s, int level)
	{
		try
		{
			if (s == null) return false;
			int max = MaxLevel;
			int lv = Mathf.Clamp(level, 0, max);
			return SetKills(s, lv * KillsPerLevel);
		}
		catch { return false; }
	}

	/// <summary>把一个班里的所有士兵都灌成指定老兵等级。返回成功处理的士兵数。</summary>
	public static int ApplySquadLevel(Squad squad, int level)
	{
		int n = 0;
		try
		{
			if (squad == null) return 0;
			Il2CppSystem.Collections.Generic.List<Soldier> members = squad.units;
			if (members == null) return 0;
			for (int i = 0; i < members.Count; i++)
			{
				Soldier s = members[i];
				if (s == null) continue;
				// 先确保状态条目存在（未参战的新兵可能还没有 UnitStates 条目）
				Plugin.GetState(s);
				if (SetLevel(s, level)) n++;
			}
		}
		catch
		{
		}
		return n;
	}

	// ---------------- 班/队级汇总（战斗结束后回读） ----------------

	/// <summary>一个班的击杀合计。</summary>
	public static int GetSquadKills(Squad squad)
	{
		int sum = 0;
		try
		{
			if (squad == null) return 0;
			Il2CppSystem.Collections.Generic.List<Soldier> members = squad.units;
			if (members == null) return 0;
			for (int i = 0; i < members.Count; i++) sum += GetKills(members[i]);
		}
		catch
		{
		}
		return sum;
	}

	/// <summary>一个班的最高老兵等级（用于把"班里打得最好的那个"带回战略层）。</summary>
	public static int GetSquadMaxLevel(Squad squad)
	{
		int best = 0;
		try
		{
			if (squad == null) return 0;
			Il2CppSystem.Collections.Generic.List<Soldier> members = squad.units;
			if (members == null) return 0;
			for (int i = 0; i < members.Count; i++)
			{
				int lv = GetLevel(members[i]);
				if (lv > best) best = lv;
			}
		}
		catch
		{
		}
		return best;
	}

	/// <summary>一个班的存活人数（用于战损回写）。</summary>
	public static int GetSquadAliveCount(Squad squad)
	{
		int alive = 0;
		try
		{
			if (squad == null) return 0;
			Il2CppSystem.Collections.Generic.List<Soldier> members = squad.units;
			if (members == null) return 0;
			for (int i = 0; i < members.Count; i++)
			{
				Soldier s = members[i];
				if (s == null) continue;
				bool dead = false;
				try { dead = s.IsDead; } catch { }
				if (!dead) alive++;
			}
		}
		catch
		{
		}
		return alive;
	}

	/// <summary>一个班的编制人数（存活 + 阵亡，战损比例的分母）。</summary>
	public static int GetSquadSize(Squad squad)
	{
		try
		{
			if (squad == null) return 0;
			Il2CppSystem.Collections.Generic.List<Soldier> members = squad.units;
			return members == null ? 0 : members.Count;
		}
		catch { return 0; }
	}
}

// ═════════════════════════════════════════════════════════════
//  主循环：剪枝 / 标记缓存 / 维护 / 绘制
// ═════════════════════════════════════════════════════════════

/// <summary>主循环与绘制（public：供测试驱动插件直接调用）。</summary>
public class HvtBehaviour : MonoBehaviour
{
	private float _nextTick;
	private int _emptyTicks;
	private readonly HashSet<int> _aliveIds = new HashSet<int>();
	private readonly HashSet<int> _customModeAi = new HashSet<int>();

	// 玩家被标记时的红屏闪烁（静态，patch 线程可写）
	internal static float FlashUntil;
	internal static Color FlashColor = new Color(1f, 0f, 0f, 0.22f);
	internal const float FlashDuration = 1.8f;

	// ── 屏幕底部提示（升级/击杀反馈走这里，避免与顶部原生提示重叠） ──

	internal sealed class Toast
	{
		public string Text;
		public float Until;
	}

	private static readonly List<Toast> Toasts = new List<Toast>();

	/// <summary>屏幕底部提示（测试驱动插件可直接调用）。</summary>
	public static void ShowToast(string text, float duration)
	{
		try
		{
			Toasts.Add(new Toast { Text = text, Until = Time.unscaledTime + duration });
			if (Toasts.Count > 3)
			{
				Toasts.RemoveAt(0);
			}
		}
		catch
		{
		}
	}

	internal static void ClearToasts()
	{
		try
		{
			Toasts.Clear();
		}
		catch
		{
		}
	}

	/// <summary>屏幕闪烁（测试驱动插件可直接调用）。</summary>
	public static void FlashPlayer(Color color, float duration)
	{
		FlashColor = color;
		FlashUntil = Time.unscaledTime + duration;
	}

	internal static void ClearPlayerFlash()
	{
		FlashUntil = 0f;
	}

	private void Update()
	{
		try
		{
			if (!Plugin.IsActive())
			{
				return;
			}
			float now = Time.unscaledTime;
			if (now < _nextTick)
			{
				return;
			}
			_nextTick = now + 0.5f;
			Tick();
		}
		catch
		{
		}
	}

	private void Tick()
	{
		var all = Creature.aliveCreatures;
		if (all == null)
		{
			return;
		}

		// 战斗结束兜底：连续 3 次 tick 无活体 → 重置（菜单/换场）
		_aliveIds.Clear();
		foreach (var c in all)
		{
			try
			{
				if (c != null)
				{
					_aliveIds.Add(c.GetInstanceID());
				}
			}
			catch
			{
			}
		}
		if (_aliveIds.Count == 0)
		{
			if (++_emptyTicks >= 3)
			{
				_emptyTicks = 0;
				if (Plugin.UnitStates.Count > 0 || Plugin.LastHits.Count > 0)
				{
					Plugin.ResetAll();
				}
				// 3D 标记与载具高度缓存随战斗重置
				ClearWorldMarkers();
				_vehicleTopOffset.Clear();
				_displayEntries.Clear();
			}
			return;
		}
		_emptyTicks = 0;

		Prune();
		RebuildMarkedAlive();
		RestoreCustomModeAi();
		PardonTraitorsWhenDisabled();
		RefreshDisplayEntries();
		ReconcileMarkers();
		MaintainMarked();
		PursueTraitors();
	}

	/// <summary>叛徒机制关闭时赦免现有叛徒（关闭开关 = 立即生效，不再被友军集火/追猎；
	/// v1.2.0 追加：同局内被标叛徒后关闭开关的残留状态）。</summary>
	private void PardonTraitorsWhenDisabled()
	{
		try
		{
			if (Plugin.TraitorFeature != null && Plugin.TraitorFeature.Value)
			{
				return;
			}
			foreach (var st in Plugin.UnitStates.Values)
			{
				if (st != null && st.Traitor)
				{
					st.Traitor = false;
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>叛徒死后把切到"自定义指令模式"的友军恢复常态（enableAiBehaviour(true)）。
	/// 实测：有队长/小队编制的友军会无视个人 moveTo——必须先 followCustomDirectCommands() 接管。</summary>
	private void RestoreCustomModeAi()
	{
		try
		{
			if (_customModeAi.Count == 0)
			{
				return;
			}
			bool anyTraitor = false;
			foreach (Soldier m in Plugin.MarkedAliveSoldiers)
			{
				if (m != null && Plugin.IsTraitor(m))
				{
					anyTraitor = true;
					break;
				}
			}
			if (anyTraitor)
			{
				return;
			}
			foreach (int aid in _customModeAi)
			{
				Soldier a = FindAliveSoldierById(aid);
				if (a == null)
				{
					continue;
				}
				try
				{
					Lua_Soldier lua = Plugin.GetLuaWrapper(a);
					AiParams ap = lua != null ? lua.getAiParams() : null;
					if (ap != null)
					{
						ap.enableAiBehaviour(true);
					}
				}
				catch
				{
				}
			}
			if (Plugin.HintThrottle(3f))
			{
			if (Plugin.DebugOn)
			{
				Plugin.ModLog.LogInfo($"[HVT] pursuit: restored {_customModeAi.Count} AI to normal behavior");
			}
			}
			_customModeAi.Clear();
		}
		catch
		{
		}
	}

	/// <summary>叛徒追猎：友军 AI 通过官方移动通道（Lua_Soldier.moveTo，走任务管线不被 AI 控制器覆盖）
	/// 持续推进到叛徒位置——每 tick 下发、无距离上限（贴脸追），随机小偏移防扎堆。</summary>
	private void PursueTraitors()
	{
		try
		{
			if (Plugin.TraitorFeature == null || !Plugin.TraitorFeature.Value)
			{
				return;
			}
			if (Plugin.FocusEnabled == null || !Plugin.FocusEnabled.Value)
			{
				return;
			}
			float radius = Plugin.FocusRadius.Value;
			float radius2 = radius * radius;
			var all = Creature.aliveCreatures;
			if (all == null)
			{
				return;
			}
			int issued = 0;
			foreach (Soldier traitor in Plugin.MarkedAliveSoldiers)
			{
				try
				{
					if (traitor == null || traitor.transform == null || !Plugin.IsTraitor(traitor))
					{
						continue;
					}
					string tSide = Plugin.SideOf(traitor.faction);
					if (string.IsNullOrEmpty(tSide))
					{
						continue;
					}
					Vector3 tPos = traitor.transform.position;
					foreach (var c in all)
					{
						try
						{
							if (c == null || c.transform == null)
							{
								continue;
							}
							Soldier a = c.TryCast<Soldier>();
							if (a == null || a == traitor)
							{
								continue;
							}
							if (Plugin.IsPlayerUnit(a))
							{
								continue;
							}
							if (Plugin.SideOf(a.faction) != tSide)
							{
								continue; // 只有友军追猎叛徒
							}
							if ((tPos - c.transform.position).sqrMagnitude > radius2)
							{
								continue;
							}
							Lua_Soldier lua = Plugin.GetLuaWrapper(a);
							if (lua == null)
							{
								continue;
							}
							try
							{
								if (lua.isInsideVehicle())
								{
									continue; // 载具内不催（可能干扰载具任务）
								}
							}
							catch
							{
							}
							// 首次追猎：切换到官方"自定义指令模式"，让 moveTo 生效
							//（有队长/小队编制的 AI 原生无视个人 moveTo——实测）
							if (_customModeAi.Add(a.GetInstanceID()))
							{
								try
								{
									AiParams ap = lua.getAiParams();
									if (ap != null)
									{
										ap.followCustomDirectCommands();
									}
								}
								catch
								{
								}
							}
							Vector3 dest = tPos + new Vector3(
								UnityEngine.Random.Range(-2f, 2f), 0f, UnityEngine.Random.Range(-2f, 2f));
							lua.moveTo(dest);
							issued++;
						}
						catch
						{
						}
					}
				}
				catch
				{
				}
			}
			if (issued > 0 && Plugin.HintThrottle(3f))
			{
			if (Plugin.DebugOn)
			{
				Plugin.ModLog.LogInfo($"[HVT] pursuit: chasing traitor, moveTo issued to {issued} allies");
			}
			}
		}
		catch
		{
		}
	}

	/// <summary>剪枝：已死/已消失单位的状态、归属、计数、缓存。</summary>
	private void Prune()
	{
		try
		{
			List<int> dead = null;
			foreach (int id in Plugin.UnitStates.Keys)
			{
				if (!_aliveIds.Contains(id))
				{
					if (dead == null)
					{
						dead = new List<int>();
					}
					dead.Add(id);
				}
			}
			if (dead != null)
			{
				foreach (int id in dead)
				{
					Plugin.ClearUnitState(id);
				}
			}
			if (Plugin.LastHits.Count > 0)
			{
				List<int> stale = null;
				float cutoff = Time.unscaledTime - 25f;
				foreach (var kv in Plugin.LastHits)
				{
					if (kv.Value == null || kv.Value.Time < cutoff)
					{
						if (stale == null)
						{
							stale = new List<int>();
						}
						stale.Add(kv.Key);
					}
				}
				if (stale != null)
				{
					foreach (int id in stale)
					{
						Plugin.LastHits.Remove(id);
					}
				}
			}
			if (Plugin.PlayerHits.Count > 0)
			{
				List<int> staleHits = null;
				foreach (int id in Plugin.PlayerHits.Keys)
				{
					if (!_aliveIds.Contains(id))
					{
						if (staleHits == null)
						{
							staleHits = new List<int>();
						}
						staleHits.Add(id);
					}
				}
				if (staleHits != null)
				{
					foreach (int id in staleHits)
					{
						Plugin.PlayerHits.Remove(id);
					}
				}
			}
			if (Plugin.CountedDeaths.Count > 0)
			{
				List<int> deadCounted = null;
				foreach (int id in Plugin.CountedDeaths)
				{
					if (!_aliveIds.Contains(id))
					{
						if (deadCounted == null)
						{
							deadCounted = new List<int>();
						}
						deadCounted.Add(id);
					}
				}
				if (deadCounted != null)
				{
					foreach (int id in deadCounted)
					{
						Plugin.CountedDeaths.Remove(id);
					}
				}
			}
			if (Plugin.LuaCache.Count > 0)
			{
				List<int> deadLua = null;
				foreach (int id in Plugin.LuaCache.Keys)
				{
					if (!_aliveIds.Contains(id))
					{
						if (deadLua == null)
						{
							deadLua = new List<int>();
						}
						deadLua.Add(id);
					}
				}
				if (deadLua != null)
				{
					foreach (int id in deadLua)
					{
						Plugin.LuaCache.Remove(id);
					}
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>重建"活着的被标记单位"缓存（GetBestVisibleEnemy 覆盖用，避免每帧扫全场景）。</summary>
	private void RebuildMarkedAlive()
	{
		try
		{
			Plugin.MarkedAliveSoldiers.Clear();
			if (Plugin.UnitStates.Count == 0)
			{
				return;
			}
			foreach (var c in Creature.aliveCreatures)
			{
				try
				{
					if (c == null)
					{
						continue;
					}
					if (!Plugin.UnitStates.ContainsKey(c.GetInstanceID()))
					{
						continue;
					}
					Soldier s = c.TryCast<Soldier>();
					if (s == null)
					{
						continue;
					}
					// 被标记（等级≥1）或叛徒（含 0 级叛徒）都进入聚焦候选
					bool marked = Plugin.LevelOf(s) >= 1;
					bool traitor = false;
					if (!marked && Plugin.TryGetState(s, out Plugin.UnitState st))
					{
						traitor = st.Traitor;
					}
					if (!marked && !traitor)
					{
						continue;
					}
					Plugin.MarkedAliveSoldiers.Add(s);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>被标记单位持续维护：移速回写（按等级）+ 压制状态清零。</summary>
	private void MaintainMarked()
	{
		try
		{
			if (Plugin.UnitStates.Count == 0)
			{
				return;
			}
			foreach (var c in Creature.aliveCreatures)
			{
				try
				{
					if (c == null)
					{
						continue;
					}
					Soldier s = c.TryCast<Soldier>();
					if (s == null || Plugin.LevelOf(s) < 1)
					{
						continue;
					}
					int id = s.GetInstanceID();
					if (Plugin.SuppressionImmune != null && Plugin.SuppressionImmune.Value)
					{
						try
						{
							s.suppress_time_end = 0f;
						}
						catch
						{
						}
					}
					if (Plugin.SpeedBases.TryGetValue(id, out float baseSpeed))
					{
						float mult = Plugin.Pow(Plugin.SpeedPerLevel.Value, Plugin.LevelOf(s));
						if (mult > 0f && mult != 1f)
						{
							SoldierAI ai = s.GetComponent<SoldierAI>();
							if (ai != null)
							{
								ai.speed = baseSpeed * mult;
							}
						}
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
	}

	private Soldier FindAliveSoldierById(int id)
	{
		var all = Creature.aliveCreatures;
		if (all == null)
		{
			return null;
		}
		foreach (var c in all)
		{
			try
			{
				if (c == null)
				{
					continue;
				}
				if (c.GetInstanceID() == id)
				{
					return c.TryCast<Soldier>();
				}
			}
			catch
			{
			}
		}
		return null;
	}

	// ── 绘制：分级标记 / 玩家红屏闪烁 / 玩家常驻指示条 ──

	/// <summary>分级标记贴图缓存：level → 实底菱形（背景色烘焙 = 敌浅红→深红/友天蓝→深蓝，白色罗马数字）。</summary>
	private static readonly Dictionary<int, Texture2D> LevelTexEnemy = new Dictionary<int, Texture2D>();
	private static readonly Dictionary<int, Texture2D> LevelTexFriendly = new Dictionary<int, Texture2D>();

	/// <summary>标记贴图边长（3D 标记近距离会放大到屏幕 200px+，128px + 3x3 超采样保证清晰）。</summary>
	private const int MarkerTexSize = 128;

	/// <summary>生成等级标记贴图：实底菱形（深红/深蓝底，3x3 超采样抗锯齿边缘）+ 中心白色罗马数字（烘焙进贴图）。</summary>
	private static Texture2D GetLevelTexture(int level, int maxLevel, bool enemy)
	{
		Dictionary<int, Texture2D> cache = enemy ? LevelTexEnemy : LevelTexFriendly;
		if (cache.TryGetValue(level, out Texture2D tex) && tex != null)
		{
			return tex;
		}
		try
		{
			int size = MarkerTexSize;
			tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
			Color bg = Plugin.FixedMarkerColor(enemy); // 固定最深色（用户确认），等级靠罗马数字区分
			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					// 3x3 超采样：边缘像素按覆盖率写 alpha（抗锯齿）
					int inside = 0;
					for (int sy = 0; sy < 3; sy++)
					{
						for (int sx = 0; sx < 3; sx++)
						{
							float nx = (x + (sx + 0.5f) / 3f) / size * 2f - 1f;
							float ny = (y + (sy + 0.5f) / 3f) / size * 2f - 1f;
							if (Mathf.Abs(nx) + Mathf.Abs(ny) <= 0.88f)
							{
								inside++;
							}
						}
					}
					tex.SetPixel(x, y, inside > 0
						? new Color(bg.r, bg.g, bg.b, bg.a * inside / 9f)
						: new Color(0f, 0f, 0f, 0f));
				}
			}
			BakeNumeral(tex, level); // 白色罗马数字
			tex.Apply();
			tex.hideFlags = (HideFlags)61; // 运行时纹理防场景卸载
			cache[level] = tex;
		}
		catch
		{
		}
		return tex;
	}

	/// <summary>像素到线段距离（厚线光栅化用）。</summary>
	private static bool NearLine(float px, float py, float x1, float y1, float x2, float y2, float w)
	{
		float dx = x2 - x1;
		float dy = y2 - y1;
		float len2 = dx * dx + dy * dy;
		float t = len2 > 0f ? Mathf.Clamp01(((px - x1) * dx + (py - y1) * dy) / len2) : 0f;
		float cx = x1 + t * dx;
		float cy = y1 + t * dy;
		float ddx = px - cx;
		float ddy = py - cy;
		return ddx * ddx + ddy * ddy <= w * w;
	}

	/// <summary>把罗马数字（I-V）以粗线段烘焙进贴图中心（白色）。
	/// 注意：Texture2D 的 y=0 在底部（SetPixel 原点左下），V/IV 的顶点必须在 y 小的一侧，
	/// 否则画出来是倒的（实测根因）。线段坐标按 32px 基准定义，运行时缩放到实际贴图尺寸；
	/// 边缘按覆盖率与底层（菱形底色）混合（抗锯齿）。</summary>
	private static void BakeNumeral(Texture2D tex, int level)
	{
		float[][] segs;
		switch (level)
		{
			case 1:
				segs = new float[][] { new float[] { 16f, 9f, 16f, 23f } };
				break;
			case 2:
				segs = new float[][] { new float[] { 12.5f, 9f, 12.5f, 23f }, new float[] { 19.5f, 9f, 19.5f, 23f } };
				break;
			case 3:
				segs = new float[][] { new float[] { 10f, 9f, 10f, 23f }, new float[] { 16f, 9f, 16f, 23f }, new float[] { 22f, 9f, 22f, 23f } };
				break;
			case 4:
				// I（左）+ V：整体跨度以中心 16 对称（I 竖线 8，V 顶点 19，两臂 14/24 → 跨度 8-24 中心 16）
				segs = new float[][] { new float[] { 8f, 9f, 8f, 23f }, new float[] { 14f, 23f, 19f, 9f }, new float[] { 24f, 23f, 19f, 9f } };
				break;
			default:
				// V：顶点在底部 y=9，两臂向上 y=23，顶点 x=16 居中（左右臂 10.5/21.5 对称）
				segs = new float[][] { new float[] { 10.5f, 23f, 16f, 9f }, new float[] { 21.5f, 23f, 16f, 9f } };
				break;
		}
		int size = MarkerTexSize;
		float k = size / 32f;
		float w = 1.1f * k;
		for (int y = 0; y < size; y++)
		{
			for (int x = 0; x < size; x++)
			{
				int hits = 0;
				for (int sy = 0; sy < 3; sy++)
				{
					for (int sx = 0; sx < 3; sx++)
					{
						float px = x + (sx + 0.5f) / 3f;
						float py = y + (sy + 0.5f) / 3f;
						foreach (float[] s in segs)
						{
							if (NearLine(px, py, s[0] * k, s[1] * k, s[2] * k, s[3] * k, w))
							{
								hits++;
								break;
							}
						}
					}
				}
				if (hits > 0)
				{
					// 与底层颜色按覆盖率混合（底层是菱形底色/透明），不是直接覆盖
					Color under = tex.GetPixel(x, y);
					tex.SetPixel(x, y, Color.Lerp(under, Color.white, hits / 9f));
				}
			}
		}
	}

	// ── 3D 世界空间标记（billboard quad，固定世界尺寸；替代 IMGUI 屏幕投影） ──

	/// <summary>标记的世界空间边长（米）：固定尺寸，不做距离补偿 → 真实近大远小。
	/// v1.2.2 改：旧版 scale = dist * MarkerDistScale 是"恒定屏占比"公式，用户实测观感
	/// 反而成"近小远大"（近距离被投影放大不足、远距离世界尺寸膨胀过度），且不符合
	/// 真实世界透视直觉。现改为固定世界尺寸，屏上大小 = 纯透视投影结果。
	/// 0.8m 为用户实测定稿值（初版 1.6m 偏大）。</summary>
	private const float MarkerWorldSize = 0.8f;

	/// <summary>单个 3D 标记对象（锚点 = 步兵或载具）。</summary>
	private sealed class WorldMarker
	{
		public GameObject Go;
		public Material Mat;
		public Soldier Soldier;    // 步兵个人标记（Vehicle == null 时有效）
		public Vehicle Vehicle;    // 载具标记（乘员合并）
		public int Level;
		public bool Enemy;
		public int TexKey;         // 已贴纹理的 (level, enemy) 键
		public float AnchorHeight; // 锚点高度：步兵 3.0m，载具 = 顶面偏移
	}

	/// <summary>显示快照条目：个人标记或载具合并标记（RefreshDisplayEntries 每 tick 重建）。</summary>
	private sealed class DisplayEntry
	{
		public int AnchorId;       // 锚点 InstanceID（士兵或载具）
		public Soldier Soldier;    // 个人条目
		public Vehicle Vehicle;    // 载具条目（乘员合并，等级取最高）
		public int Level;
		public bool Enemy;
	}

	private static Mesh MarkerQuad;
	private static Material MarkerMatTemplate;
	/// <summary>找不到可用 shader 时置位 → OnGUI 回退 IMGUI 屏幕投影绘制。</summary>
	public static bool Marker3dUnavailable;

	private readonly Dictionary<int, WorldMarker> _worldMarkers = new Dictionary<int, WorldMarker>();
	private readonly Dictionary<int, float> _vehicleTopOffset = new Dictionary<int, float>();
	private readonly List<DisplayEntry> _displayEntries = new List<DisplayEntry>();
	private readonly List<WorldMarker> _markerSweep = new List<WorldMarker>();
	private readonly List<int> _markerDead = new List<int>();

	private static Mesh GetMarkerQuad()
	{
		if (MarkerQuad != null)
		{
			return MarkerQuad;
		}
		try
		{
			Mesh mesh = new Mesh();
			mesh.vertices = new Vector3[]
			{
				new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
				new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f)
			};
			mesh.uv = new Vector2[]
			{
				new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f)
			};
			mesh.triangles = new int[] { 0, 2, 1, 2, 3, 1 };
			// 显式白色顶点色：Sprites/Default 的片元色 = 纹理 × 顶点色，缺省属性不可依赖
			mesh.colors = new Color[]
			{
				Color.white, Color.white, Color.white, Color.white
			};
			mesh.hideFlags = (HideFlags)61; // 运行时资源防场景卸载
			MarkerQuad = mesh;
		}
		catch
		{
		}
		return MarkerQuad;
	}

	/// <summary>标记材质模板：Sprites/Default（片元 = 纹理 × 顶点色，rgb*=a 预乘后 One/OneMinusSrcAlpha
	/// 混合，纹理 RGB 正确显示；ZTest LEqual → 遮挡由深度测试自然处理，被完全挡住时消失）。
	/// 注意不能用 GUI/Text Shader——那是字体着色器（RGB 取材质色、纹理只出 alpha），
	/// v1.2.0 首版实测深红/蓝底色全部变白。缺失按序回退；全缺则置 Marker3dUnavailable 走 IMGUI 回退。</summary>
	private static Material GetMarkerMaterialTemplate()
	{
		if (MarkerMatTemplate != null)
		{
			return MarkerMatTemplate;
		}
		string[] candidates = { "Sprites/Default", "Unlit/Transparent" };
		foreach (string n in candidates)
		{
			try
			{
				Shader sh = Shader.Find(n);
				if (sh != null)
				{
					MarkerMatTemplate = new Material(sh);
					MarkerMatTemplate.hideFlags = (HideFlags)61;
					if (Plugin.DebugOn)
					{
						Plugin.ModLog.LogInfo($"[HVT] 3D marker material shader: {n}");
					}
					break;
				}
			}
			catch
			{
			}
		}
		return MarkerMatTemplate;
	}

	/// <summary>载具标记锚点高度：载具全部碰撞体最高点 + 0.9m 余量（下限 2.6m），按载具缓存。</summary>
	private float VehicleTopOffset(Vehicle veh)
	{
		int id = veh.GetInstanceID();
		if (_vehicleTopOffset.TryGetValue(id, out float off))
		{
			return off;
		}
		off = 3.4f;
		try
		{
			float top = float.MinValue;
			var cols = veh.GetComponentsInChildren<Collider>();
			if (cols != null)
			{
				for (int i = 0; i < cols.Length; i++)
				{
					try
					{
						Collider col = cols[i];
						// 排除异常巨大的触发/交互碰撞体（防锚点被拉上天）
						if (col != null && col.enabled && col.bounds.extents.magnitude < 30f)
						{
							top = Mathf.Max(top, col.bounds.max.y);
						}
					}
					catch
					{
					}
				}
			}
			if (top > float.MinValue && veh.transform != null)
			{
				off = Mathf.Max(2.6f, top - veh.transform.position.y + 0.9f);
			}
		}
		catch
		{
		}
		_vehicleTopOffset[id] = off;
		return off;
	}

	/// <summary>显示快照：被标记单位 → 个人条目 / 载具合并条目（一载具一条，等级取乘员最高）。
	/// 供 3D 标记对账与 M 大地图图标共用（大地图载具同步去重）。</summary>
	private void RefreshDisplayEntries()
	{
		try
		{
			_displayEntries.Clear();
			if (Plugin.UnitStates.Count == 0)
			{
				return;
			}
			string playerSide = Plugin.PlayerSide();
			Dictionary<int, DisplayEntry> vehEntries = null;
			foreach (var kv in Plugin.UnitStates)
			{
				try
				{
					Soldier s = FindAliveSoldierById(kv.Key);
					if (s == null || s.transform == null || s.IsDead)
					{
						continue;
					}
					if (Plugin.IsTraitor(s))
					{
						continue; // 叛徒不显示标记
					}
					int lv = Plugin.LevelOf(s);
					if (lv < 1)
					{
						continue;
					}
					if (Plugin.IsPlayerUnit(s))
					{
						continue; // 玩家用顶部指示条
					}
					bool enemy = string.IsNullOrEmpty(playerSide) || Plugin.SideOf(s.faction) != playerSide;
					Vehicle veh = Plugin.VehicleOf(s);
					if (veh != null && veh.transform != null)
					{
						// 载具乘员合并：一载具一条目，等级取乘员最高
						int vid = veh.GetInstanceID();
						if (vehEntries == null)
						{
							vehEntries = new Dictionary<int, DisplayEntry>();
						}
						if (vehEntries.TryGetValue(vid, out DisplayEntry ve) && ve != null)
						{
							if (lv > ve.Level)
							{
								ve.Level = lv;
							}
							continue;
						}
						ve = new DisplayEntry { AnchorId = vid, Vehicle = veh, Level = lv, Enemy = enemy };
						vehEntries[vid] = ve;
						_displayEntries.Add(ve);
						continue;
					}
					_displayEntries.Add(new DisplayEntry { AnchorId = kv.Key, Soldier = s, Level = lv, Enemy = enemy });
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>3D 标记池对账（每 tick）：按显示快照补建/回收 quad 对象并更新纹理/锚高。</summary>
	private void ReconcileMarkers()
	{
		try
		{
			bool want = Plugin.ShowMarkers != null && Plugin.ShowMarkers.Value && _displayEntries.Count > 0 &&
				GetMarkerQuad() != null && GetMarkerMaterialTemplate() != null;
			if (!want)
			{
				if (!Marker3dUnavailable && Plugin.ShowMarkers != null && Plugin.ShowMarkers.Value &&
					_displayEntries.Count > 0)
				{
					Marker3dUnavailable = true;
					Plugin.ModLog.LogWarning("[HVT] no usable shader for 3D markers; falling back to IMGUI overlay");
				}
				ClearWorldMarkers();
				return;
			}
			// 补建/更新
			_markerSweep.Clear();
			foreach (DisplayEntry e in _displayEntries)
			{
				if (e == null)
				{
					continue;
				}
				if (!_worldMarkers.TryGetValue(e.AnchorId, out WorldMarker m) || m == null || m.Go == null)
				{
					m = CreateWorldMarker();
					if (m == null)
					{
						continue;
					}
					_worldMarkers[e.AnchorId] = m;
				}
				m.Soldier = e.Soldier;
				m.Vehicle = e.Vehicle;
				m.Level = e.Level;
				m.Enemy = e.Enemy;
				m.AnchorHeight = e.Vehicle != null ? VehicleTopOffset(e.Vehicle) : 3.0f;
				int texKey = m.Level * 2 + (m.Enemy ? 1 : 0);
				if (m.Mat != null && m.TexKey != texKey)
				{
					m.Mat.mainTexture = GetLevelTexture(m.Level, Plugin.MaxLevel.Value, m.Enemy);
					m.TexKey = texKey;
				}
				_markerSweep.Add(m);
			}
			// 回收不再需要的
			_markerDead.Clear();
			foreach (var kv in _worldMarkers)
			{
				if (!_markerSweep.Contains(kv.Value))
				{
					_markerDead.Add(kv.Key);
				}
			}
			foreach (int id in _markerDead)
			{
				if (_worldMarkers.TryGetValue(id, out WorldMarker dead))
				{
					DestroyWorldMarker(dead);
					_worldMarkers.Remove(id);
				}
			}
		}
		catch
		{
		}
	}

	private WorldMarker CreateWorldMarker()
	{
		try
		{
			GameObject go = new GameObject("HVT_Marker");
			go.transform.SetParent(transform, false); // 挂 behaviour 根（DontDestroyOnLoad，跨场景存活）
			MeshFilter mf = go.AddComponent<MeshFilter>();
			mf.sharedMesh = GetMarkerQuad();
			MeshRenderer rend = go.AddComponent<MeshRenderer>();
			Material mat = new Material(MarkerMatTemplate);
			mat.hideFlags = (HideFlags)61;
			rend.sharedMaterial = mat;
			go.SetActive(false);
			return new WorldMarker { Go = go, Mat = mat, TexKey = -1 };
		}
		catch
		{
			return null;
		}
	}

	private void DestroyWorldMarker(WorldMarker m)
	{
		try
		{
			if (m != null && m.Mat != null)
			{
				UnityEngine.Object.Destroy(m.Mat); // 实例材质单独销毁
			}
		}
		catch
		{
		}
		try
		{
			if (m != null && m.Go != null)
			{
				UnityEngine.Object.Destroy(m.Go);
			}
		}
		catch
		{
		}
	}

	private void ClearWorldMarkers()
	{
		try
		{
			if (_worldMarkers.Count == 0)
			{
				return;
			}
			foreach (WorldMarker m in _worldMarkers.Values)
			{
				DestroyWorldMarker(m);
			}
			_worldMarkers.Clear();
		}
		catch
		{
		}
	}

	/// <summary>3D 标记每帧更新：跟随锚点、billboard 朝向相机、固定世界尺寸（真实近大远小）。
	/// 遮挡由深度测试自然处理（ZTest LEqual，被完全挡住时消失）。
	/// 暂停/隐藏万物/M 大地图/MarkerRange 关闭时全部隐藏（与旧 IMGUI 版门控一致）。</summary>
	private void LateUpdate()
	{
		try
		{
			if (_worldMarkers.Count == 0)
			{
				return;
			}
			bool show = Plugin.IsActive() && Plugin.ShowMarkers != null && Plugin.ShowMarkers.Value && !Marker3dUnavailable;
			Camera cam = null;
			if (show)
			{
				cam = Camera.main;
				show = cam != null;
			}
			if (show)
			{
				try
				{
					if (MiniMapGUI.MiniMapOpened)
					{
						show = false; // M 大地图打开时隐藏（旧版同款规则）
					}
				}
				catch
				{
				}
			}
			if (show)
			{
				bool paused = false;
				try
				{
					paused = Pause.isPaused || Time.timeScale <= 0.001f;
				}
				catch
				{
					paused = Time.timeScale <= 0.001f;
				}
				if (paused)
				{
					show = false;
				}
			}
			if (show)
			{
				try
				{
					if (ER2Shared.NoHintsHudLink.IsHidden("er2.highvaluetarget", "ER2 Veteran HVT"))
					{
						show = false; // 隐藏万物联动
					}
				}
				catch
				{
				}
			}
			float range = Plugin.MarkerRange != null ? Plugin.MarkerRange.Value : 500f;
			float range2 = range * range;
			Vector3 camPos = show ? cam.transform.position : Vector3.zero;
			Quaternion camRot = show ? cam.transform.rotation : Quaternion.identity;
			foreach (WorldMarker m in _worldMarkers.Values)
			{
				try
				{
					if (!show || m == null || m.Go == null)
					{
						if (m != null && m.Go != null && m.Go.activeSelf)
						{
							m.Go.SetActive(false);
						}
						continue;
					}
					Vector3 anchor;
					if (m.Vehicle != null && m.Vehicle.transform != null)
					{
						anchor = m.Vehicle.transform.position + Vector3.up * m.AnchorHeight;
					}
					else if (m.Soldier != null && m.Soldier.transform != null && !m.Soldier.IsDead)
					{
						anchor = m.Soldier.transform.position + Vector3.up * 3.0f;
					}
					else
					{
						if (m.Go.activeSelf)
						{
							m.Go.SetActive(false);
						}
						continue;
					}
					Vector3 toAnchor = anchor - camPos;
					float dist2 = toAnchor.sqrMagnitude;
					if (dist2 > range2)
					{
						if (m.Go.activeSelf)
						{
							m.Go.SetActive(false);
						}
						continue;
					}
					float dist = Mathf.Sqrt(dist2);
					// 固定世界尺寸：真实近大远小（屏上大小 = 纯透视投影，不做距离补偿）
					float scale = MarkerWorldSize;
					m.Go.transform.position = anchor;
					m.Go.transform.rotation = camRot; // billboard 正对相机
					m.Go.transform.localScale = new Vector3(scale, scale, 1f);
					if (!m.Go.activeSelf)
					{
						m.Go.SetActive(true);
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
	}

	/// <summary>IMGUI 屏幕投影标记（回退路径：3D shader 缺失时才启用）。
	/// 原主路径——不随原生标记隐藏（用户实测 Marker3DGUI 会随原生隐藏一起消失）、
	/// 无近距离消失问题；标记在 +3.0m（原生姓名标签之上）；MarkerRange 限制显示距离。</summary>
	private void DrawMarkersScreen()
	{
		try
		{
			if (Plugin.ShowMarkers == null || !Plugin.ShowMarkers.Value || Plugin.UnitStates.Count == 0)
			{
				return;
			}
			Camera cam = Camera.main;
			if (cam == null)
			{
				return;
			}
			// 用户实测：游戏没有常驻小地图，只有按 M 打开的大地图——
			// M 地图打开时整个不画头顶标记（否则盖在地图上）
			try
			{
				if (MiniMapGUI.MiniMapOpened)
				{
					return;
				}
			}
			catch
			{
			}
			string playerSide = Plugin.PlayerSide();
			float range = Plugin.MarkerRange != null ? Plugin.MarkerRange.Value : 500f;
			float range2 = range * range;
			foreach (var kv in Plugin.UnitStates)
			{
				try
				{
					Soldier s = FindAliveSoldierById(kv.Key);
					if (s == null || s.transform == null || s.IsDead)
					{
						continue;
					}
					if (Plugin.IsTraitor(s))
					{
						continue; // 叛徒不显示头顶标记
					}
					int lv = Plugin.LevelOf(s);
					if (lv < 1)
					{
						continue;
					}
					if (Plugin.IsPlayerUnit(s))
					{
						continue; // 玩家用顶部指示条
					}
					if ((cam.transform.position - s.transform.position).sqrMagnitude > range2)
					{
						continue;
					}
					bool enemy = string.IsNullOrEmpty(playerSide) || Plugin.SideOf(s.faction) != playerSide;
					Texture2D tex = GetLevelTexture(lv, Plugin.MaxLevel.Value, enemy);
					if (tex == null)
					{
						continue;
					}
					Vector3 head = s.transform.position + Vector3.up * 3.0f;
					Vector3 vp = cam.WorldToScreenPoint(head);
					if (vp.z <= 0f)
					{
						continue;
					}
					const float size = 30f;
					float x = vp.x - size / 2f;
					float y = Screen.height - vp.y - size / 2f;
					// 被障碍物遮挡时标记整体半透明（用户确认的渲染效果）
					bool occluded = !Plugin.IsVisibleFromCamera(s, cam);
					if (occluded)
					{
						GUI.color = new Color(1f, 1f, 1f, 0.3f);
					}
					// 实底标记（背景色已烘焙，直接绘制，无 tint）
					GUI.DrawTexture(new Rect(x, y, size, size), tex);
					if (occluded)
					{
						GUI.color = Color.white;
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
	}

	/// <summary>游戏单位标记快照（unitsContainer 下，游戏自己每帧放置）。</summary>
	private sealed class MmMarker
	{
		public string Name;
		public Vector2 Local; // 容器 localPosition
		public Vector3 ScreenUp; // 世界位置（Overlay canvas = 屏幕像素，y-up）
	}

	private readonly List<MmMarker> _mmMarkers = new List<MmMarker>();

	/// <summary>M 大地图高危目标图标：仅 M 地图打开时绘制（游戏无常驻小地图——用户实测）。
	/// 直接跟随游戏自己的单位标记：GetPositionInContainer 算容器坐标 → 在 unitsContainer 下
	/// 匹配最近的游戏 marker → 用 marker 的屏幕位置画图标（游戏放哪我们画哪，不猜映射公式）。
	/// 用户实测上一版公式映射乱标，此方案以游戏数据为锚。</summary>
	private void DrawMinimapIcons()
	{
		try
		{
			if (Plugin.MiniMapIcons == null || !Plugin.MiniMapIcons.Value || _displayEntries.Count == 0)
			{
				return;
			}
			MiniMapGUI mm = MiniMapGUI.Instance;
			if (mm == null || mm.miniMap == null)
			{
				return;
			}
			try
			{
				if (!MiniMapGUI.MiniMapOpened)
				{
					return; // 只有 M 地图打开时才画图标
				}
			}
			catch
			{
				return;
			}
			// 1) 收集游戏自己的单位标记
			_mmMarkers.Clear();
			try
			{
				Transform uc = mm.unitsContainer;
				if (uc != null)
				{
					for (int i = 0; i < uc.childCount; i++)
					{
						Transform ch = uc.GetChild(i);
						if (ch == null || !ch.gameObject.activeInHierarchy)
						{
							continue;
						}
						_mmMarkers.Add(new MmMarker
						{
							Name = ch.name,
							Local = new Vector2(ch.localPosition.x, ch.localPosition.y),
							ScreenUp = ch.position
						});
					}
				}
			}
			catch
			{
			}
			if (_mmMarkers.Count == 0)
			{
				return; // 拿不到游戏标记就不画（比乱标好）
			}
			// 遍历显示快照（RefreshDisplayEntries 已把载具乘员合并为单条目 → 大地图一载具一图标）
			foreach (DisplayEntry e in _displayEntries)
			{
				try
				{
					Vector3 worldPos;
					if (e.Vehicle != null && e.Vehicle.transform != null)
					{
						worldPos = e.Vehicle.transform.position;
					}
					else if (e.Soldier != null && e.Soldier.transform != null)
					{
						worldPos = e.Soldier.transform.position;
					}
					else
					{
						continue;
					}
					Vector2 want = mm.GetPositionInContainer(worldPos, false);
					// 2) 匹配 localPosition 最近的游戏 marker
					MmMarker best = null;
					float bestD = float.MaxValue;
					foreach (var m in _mmMarkers)
					{
						float dx = m.Local.x - want.x;
						float dy = m.Local.y - want.y;
						float d = dx * dx + dy * dy;
						if (d < bestD)
						{
							bestD = d;
							best = m;
						}
					}
					if (best == null)
					{
						continue;
					}
					// 3) 在游戏标记旁画 HVT 菱形（右侧偏移避免盖住游戏标记）
					float sx = best.ScreenUp.x + 9f;
					float sy = Screen.height - best.ScreenUp.y; // y-up → IMGUI
					if (sx < -20f || sx > Screen.width + 20f || sy < -20f || sy > Screen.height + 20f)
					{
						continue;
					}
					Texture2D tex = GetLevelTexture(e.Level, Plugin.MaxLevel.Value, e.Enemy);
					if (tex == null)
					{
						continue;
					}
					const float size = 14f;
					GUI.DrawTexture(new Rect(sx - size / 2f, sy - size / 2f, size, size), tex);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	private void OnGUI()
	{
		try
		{
			if (!Plugin.IsActive())
			{
				return;
			}
			// Hide Anything（隐藏万物）联动：F5 隐藏 HUD 时本 mod 全部渲染（标记/图标/指示条/提示）一起隐藏
			try
			{
				if (ER2Shared.NoHintsHudLink.IsHidden("er2.highvaluetarget", "ER2 Veteran HVT"))
				{
					return;
				}
			}
			catch
			{
			}
			// 暂停（设置界面）时隐藏全部标记/指示条（原生单位名同款规则）
			bool paused = false;
			try
			{
				paused = Pause.isPaused || Time.timeScale <= 0.001f;
			}
			catch
			{
				paused = Time.timeScale <= 0.001f;
			}
			float now = Time.unscaledTime;

			// 红屏/金屏闪烁（玩家被标记 / 玩家消灭高危目标）
			if (!paused && now < FlashUntil && FlashColor.a > 0f)
			{
				float a = Mathf.Clamp01((FlashUntil - now) / FlashDuration) * FlashColor.a;
				GUI.color = new Color(FlashColor.r, FlashColor.g, FlashColor.b, a);
				GUI.DrawTexture(new Rect(0f, 0f, Screen.width, Screen.height), Texture2D.whiteTexture);
				GUI.color = Color.white;
			}

			// 玩家常驻指示条（顶部占点 UI 之下、再上移一点；叛徒不显示——只保留提示）
			if (!paused && Plugin.PlayerIndicator != null && Plugin.PlayerIndicator.Value)
			{
				Soldier me = Plugin.ControlledSoldier();
				if (me != null && Plugin.IsMarked(me) && !Plugin.IsTraitor(me))
				{
					int lv = Plugin.LevelOf(me);
					GUIStyle style = new GUIStyle();
					style.fontSize = 22;
					style.fontStyle = FontStyle.Bold;
					style.alignment = TextAnchor.UpperCenter;
					// 等级越高颜色越深（敌方红系）
					style.normal.textColor = Plugin.MarkerColor(true, lv, Plugin.MaxLevel.Value);
					string text = T($"⚠ Lv.{Plugin.RomanNumeral(lv)} 高危目标 — 敌军正在集火你",
						$"⚠ Lv.{Plugin.RomanNumeral(lv)} HIGH-VALUE TARGET — ENEMIES FOCUS YOU");
					GUI.Label(new Rect(0f, 50f, Screen.width, 36f), text, style);
				}
			}

			// 头顶分级标记：3D 世界空间（LateUpdate 驱动）；shader 缺失时回退 IMGUI 投影。
			// M 大地图图标仍走 IMGUI。暂停时都隐藏。
			if (!paused)
			{
				if (Marker3dUnavailable)
				{
					DrawMarkersScreen();
				}
				DrawMinimapIcons();
			}

			// 屏幕底部提示（升级/击杀反馈，避开顶部原生提示；纯文字无背景，多行居中）
			if (!paused && Toasts.Count > 0)
			{
				float now2 = Time.unscaledTime;
				GUIStyle ts = new GUIStyle();
				ts.fontSize = 20;
				ts.fontStyle = FontStyle.Bold;
				ts.alignment = TextAnchor.MiddleCenter;
				ts.wordWrap = true;
				ts.normal.textColor = Color.white;
				for (int i = Toasts.Count - 1; i >= 0; i--)
				{
					if (now2 >= Toasts[i].Until)
					{
						Toasts.RemoveAt(i);
						continue;
					}
					Toast t = Toasts[i];
					int lineCount = Mathf.Max(1, t.Text.Split('\n').Length);
					float th = lineCount * 26f;
					float yy = Screen.height - 150f - (Toasts.Count - 1 - i) * (th + 6f);
					GUI.Label(new Rect(0f, yy, Screen.width, th), t.Text, ts);
				}
			}
		}
		catch
		{
		}
	}

	internal static string T(string cn, string en) => Plugin.DefaultChinese ? cn : en;
}

// ═════════════════════════════════════════════════════════════
//  仇恨聚焦（覆盖原生目标选择）
//  覆盖 Soldier.GetBestVisibleEnemy：让仇方 AI 把被标记单位当作
//  "最佳可见敌人"返回，走原生行为（机动/掩体/换弹正常），零强制态。
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(Soldier), "GetBestVisibleEnemy")]
internal static class MarkedTargetSelectionPatch
{
	private static void Postfix(Soldier __instance, ref Spottable __result, ref float dist)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null || Plugin.MarkedAliveSoldiers.Count == 0)
			{
				return;
			}
			if (Plugin.IsPlayerUnit(__instance))
			{
				return; // 玩家自己瞄准，不受强制
			}
			string viewerSide = Plugin.SideOf(__instance.faction);
			if (string.IsNullOrEmpty(viewerSide))
			{
				return;
			}
			float radius = Plugin.FocusRadius != null ? Plugin.FocusRadius.Value : 500f;
			float radius2 = radius * radius;
			Soldier best = null;
			int bestLevel = -1;
			float bestDist2 = float.MaxValue;
			foreach (Soldier m in Plugin.MarkedAliveSoldiers)
			{
				try
				{
					if (m == null || m == __instance || m.transform == null)
					{
						continue;
					}
					if (!Plugin.TryGetState(m, out Plugin.UnitState st))
					{
						continue;
					}
					if (!st.HatedBySides.Contains(viewerSide))
					{
						continue;
					}
					// 同方永不互打（跨国家友军也算同方）；叛徒例外（友军就是要打叛徒）
					if (Plugin.SideOf(m.faction) == viewerSide && !st.Traitor)
					{
						continue;
					}
					float d2 = (m.transform.position - __instance.transform.position).sqrMagnitude;
					if (d2 > radius2)
					{
						continue;
					}
					// 等级越高优先级越高，同级取近
					int lv = st.Traitor ? 999 : Plugin.LevelOf(m);
					if (lv < bestLevel || (lv == bestLevel && d2 >= bestDist2))
					{
						continue;
					}
					best = m;
					bestLevel = lv;
					bestDist2 = d2;
				}
				catch
				{
				}
			}
			if (best == null)
			{
				return;
			}
			// 视线内才覆盖（防穿墙锁）
			if (!Plugin.HasLineOfSight(__instance, best))
			{
				return;
			}
			Spottable spot = Plugin.GetSpottable(best);
			if (spot == null)
			{
				return;
			}
			__result = spot;
			dist = Mathf.Sqrt(bestDist2);
		}
		catch
		{
		}
	}
}

// ═════════════════════════════════════════════════════════════
//  击杀归属补丁
// ═════════════════════════════════════════════════════════════

/// <summary>子弹上下文 + 命中归属（Prefix：必须在伤害处理前记录——
/// 瞬杀（爆头/一击必杀）的死亡发生在原生 OnHit 内部，Postfix 记录会晚于 Kill 事件，
/// 击杀结算时查不到归属（实测根因）。头盔等独立物件用 VictimFromHit 兜底。</summary>
[HarmonyPatch(typeof(BulletInstance), "OnHit")]
[HarmonyPriority(Priority.High)]
internal static class BulletContextPatch
{
	private static void Prefix(BulletInstance __instance, RaycastHit hit)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null || __instance.shooter == null)
			{
				return;
			}
			Plugin.BulletShooter = __instance.shooter;
			Plugin.BulletTime = Time.unscaledTime;
			Soldier victim = Plugin.VictimFromHit(hit);
			if (victim != null)
			{
				Plugin.RecordHit(victim, __instance.shooter);
			}
		}
		catch
		{
		}
	}
}

/// <summary>爆炸命中归属：记录受害者 → 负责者。</summary>
[HarmonyPatch(typeof(BodyPart), "TryDamageWithExplosion")]
[HarmonyPriority(Priority.Normal)]
internal static class ExplosionAttributionPatch
{
	private static void Prefix(BodyPart __instance, Soldier responsible)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null || responsible == null)
			{
				return;
			}
			Creature unit = __instance.GetUnit();
			if (unit != null)
			{
				Plugin.RecordHit(unit, responsible);
			}
		}
		catch
		{
		}
	}
}

/// <summary>近战归属（Prefix：伤害处理前记录，瞬杀近战同样覆盖）。</summary>
[HarmonyPatch(typeof(BodyPart), "HitPart")]
[HarmonyPriority(Priority.Normal)]
internal static class MeleeAttributionPatch
{
	private static void Prefix(BodyPart __instance, HitType hitType)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null || hitType != HitType.Melee)
			{
				return;
			}
			Soldier attacker = Plugin.PeekMeleeAttacker();
			if (attacker == null)
			{
				return;
			}
			Creature unit = __instance.GetUnit();
			if (unit != null)
			{
				Plugin.RecordHit(unit, attacker);
			}
		}
		catch
		{
		}
	}
}

/// <summary>头盔归属兜底（Prefix：伤害处理前记录，瞬杀覆盖）。
/// 头盔继承 BodyPart 但挂在士兵层级外（GetUnit 为 null），用 ItemHelmet.User 解析受害者。</summary>
[HarmonyPatch(typeof(BodyPart), "HitPart")]
[HarmonyPriority(Priority.Normal)]
internal static class HelmetAttributionPatch
{
	private static void Prefix(BodyPart __instance)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null)
			{
				return;
			}
			ItemHelmet helm = __instance.TryCast<ItemHelmet>();
			if (helm == null)
			{
				return;
			}
			Soldier victim = null;
			try
			{
				victim = helm.User;
			}
			catch
			{
			}
			if (victim == null)
			{
				return;
			}
			Soldier attacker = Plugin.PeekBulletShooter();
			if (attacker == null)
			{
				attacker = Plugin.PeekMeleeAttacker();
			}
			if (attacker == null)
			{
				return;
			}
			Plugin.RecordHit(victim, attacker);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning($"[HVT] helmet attribution exception: {ex.Message}");
		}
	}
}

/// <summary>近战上下文：Melee() 调用时记录攻击者。</summary>
[HarmonyPatch(typeof(Soldier), "Melee")]
internal static class MeleeContextPatch
{
	private static void Postfix(Soldier __instance)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null)
			{
				return;
			}
			Plugin.MeleeAttacker = __instance;
			Plugin.MeleeTime = Time.unscaledTime;
		}
		catch
		{
		}
	}
}

/// <summary>死亡事件：结算击杀归属并计数（Kill 只在真死亡时调用）。</summary>
[HarmonyPatch(typeof(Soldier), "Kill")]
internal static class KillCountPatch
{
	private static void Postfix(Soldier __instance)
	{
		try
		{
			if (Plugin.IsActive())
			{
				Plugin.OnUnitKilled(__instance);
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning($"[HVT] Kill patch exception: {ex.Message}");
		}
	}
}

/// <summary>死亡事件（同步路径，联机同样生效）。</summary>
[HarmonyPatch(typeof(Soldier), "KillSynched")]
internal static class KillSynchedCountPatch
{
	private static void Postfix(Soldier __instance)
	{
		try
		{
			if (Plugin.IsActive())
			{
				Plugin.OnUnitKilled(__instance);
			}
		}
		catch
		{
		}
	}
}

// ═════════════════════════════════════════════════════════════
//  叛徒伤害放行（优先级 900 > CombatTweaks 友军保护 First=800）
//  原理：同阵营命中路径上，把攻击者的阵营字段/参数临时改写为标记值，
//  让友军保护判定"不同阵营"而放行；Postfix 恢复原值。
// ═════════════════════════════════════════════════════════════

/// <summary>子弹链：玩家→友军 / 友军 AI→叛徒玩家 的子弹放行。</summary>
[HarmonyPatch(typeof(BulletInstance), "OnHit")]
[HarmonyPriority(900)]
internal static class TraitorBulletPassPatch
{
	private static bool Prefix(BulletInstance __instance, RaycastHit hit, ref string __state)
	{
		__state = null;
		try
		{
			if (!Plugin.IsActive() || __instance == null || __instance.shooter == null || hit.collider == null)
			{
				return true;
			}
			Soldier shooter = __instance.shooter;
			Soldier victimSoldier = Plugin.VictimFromHit(hit);
			if (victimSoldier == null)
			{
				return true;
			}
			if (!Plugin.ShouldAllowFriendlyDamage(shooter, victimSoldier))
			{
				return true;
			}
			__state = shooter.faction;
			shooter.faction = Plugin.FfMarkerFaction;
		}
		catch
		{
		}
		return true;
	}

	[HarmonyPriority(Priority.Last)]
	private static void Postfix(BulletInstance __instance, string __state)
	{
		if (__state == null || __instance == null || __instance.shooter == null)
		{
			return;
		}
		try
		{
			__instance.shooter.faction = __state;
		}
		catch
		{
		}
	}
}

/// <summary>近战/直伤：同阵营 HitPart 放行（近战上下文判定攻击者）。</summary>
[HarmonyPatch(typeof(BodyPart), "HitPart")]
[HarmonyPriority(900)]
internal static class TraitorMeleePassPatch
{
	private static bool Prefix(BodyPart __instance, ref string fromFaction, HitType hitType)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null)
			{
				return true;
			}
			if (Plugin.TraitorFeature == null || !Plugin.TraitorFeature.Value)
			{
				return true;
			}
			Creature unit = __instance.GetUnit();
			if (unit == null)
			{
				return true;
			}
			Soldier victim = unit.TryCast<Soldier>();
			if (victim == null)
			{
				return true;
			}
			string vSide = Plugin.SideOf(victim.faction);
			if (string.IsNullOrEmpty(vSide))
			{
				return true;
			}
			// 近战有攻击者身份
			Soldier melee = Plugin.PeekMeleeAttacker();
			if (melee != null && Plugin.SideOf(melee.faction) == vSide)
			{
				if (Plugin.IsPlayerUnit(melee) || (Plugin.IsPlayerUnit(victim) && Plugin.IsTraitor(victim)))
				{
					fromFaction = Plugin.FfMarkerFaction;
				}
				return true;
			}
			// 兜底：同阵营直伤命中被标记的叛徒玩家（无身份路径）
			if (Plugin.SideOf(fromFaction) == vSide && Plugin.IsPlayerUnit(victim) && Plugin.IsTraitor(victim))
			{
				fromFaction = Plugin.FfMarkerFaction;
			}
		}
		catch
		{
		}
		return true;
	}
}

/// <summary>爆炸链：玩家手雷→友军 / 友军爆炸→叛徒玩家 放行。</summary>
[HarmonyPatch(typeof(BodyPart), "TryDamageWithExplosion")]
[HarmonyPriority(900)]
internal static class TraitorExplosionPassPatch
{
	private static bool Prefix(BodyPart __instance, Soldier responsible, ref string __state)
	{
		__state = null;
		try
		{
			if (!Plugin.IsActive() || __instance == null || responsible == null)
			{
				return true;
			}
			Creature unit = __instance.GetUnit();
			if (unit == null)
			{
				return true;
			}
			Soldier victim = unit.TryCast<Soldier>();
			if (victim == null)
			{
				return true;
			}
			if (!Plugin.ShouldAllowFriendlyDamage(responsible, victim))
			{
				return true;
			}
			__state = responsible.faction;
			responsible.faction = Plugin.FfMarkerFaction;
		}
		catch
		{
		}
		return true;
	}

	[HarmonyPriority(Priority.Last)]
	private static void Postfix(Soldier responsible, string __state)
	{
		if (__state == null || responsible == null)
		{
			return;
		}
		try
		{
			responsible.faction = __state;
		}
		catch
		{
		}
	}
}

/// <summary>载具装甲链：玩家坦克炮→友军载具 放行。</summary>
[HarmonyPatch(typeof(VehicleDamagablePart), "TryPenetrateArmor")]
[HarmonyPriority(900)]
internal static class TraitorArmorPassPatch
{
	private static bool Prefix(VehicleDamagablePart __instance, Soldier shooter, ref string __state)
	{
		__state = null;
		try
		{
			if (!Plugin.IsActive() || __instance == null || shooter == null)
			{
				return true;
			}
			if (!Plugin.IsPlayerUnit(shooter))
			{
				return true;
			}
			string victimFaction;
			try
			{
				victimFaction = __instance.GetFaction();
			}
			catch
			{
				return true;
			}
			if (string.IsNullOrEmpty(victimFaction))
			{
				return true;
			}
			if (Plugin.SideOf(shooter.faction) != Plugin.SideOf(victimFaction))
			{
				return true;
			}
			__state = shooter.faction;
			shooter.faction = Plugin.FfMarkerFaction;
		}
		catch
		{
		}
		return true;
	}

	[HarmonyPriority(Priority.Last)]
	private static void Postfix(Soldier shooter, string __state)
	{
		if (__state == null || shooter == null)
		{
			return;
		}
		try
		{
			shooter.faction = __state;
		}
		catch
		{
		}
	}
}

// ═════════════════════════════════════════════════════════════
//  老兵强化补丁（按等级缩放）
// ═════════════════════════════════════════════════════════════

/// <summary>命中率总线：老兵命中率随等级提升。</summary>
[HarmonyPatch(typeof(SoldierAI), "ProcessAiAccuracy")]
internal static class MarkedAccuracyPatch
{
	private static void Postfix(Soldier user, ref float __result)
	{
		try
		{
			if (!Plugin.IsActive() || user == null)
			{
				return;
			}
			int lv = Plugin.LevelOf(user);
			if (lv >= 1)
			{
				float mult = Plugin.Pow(Plugin.AccPerLevel.Value, lv);
				if (mult > 0f && mult != 1f)
				{
					__result *= mult;
				}
			}
		}
		catch
		{
		}
	}
}

/// <summary>射击间隔：老兵举枪+射击更快（每级按射速与举枪两个倍率缩短）。</summary>
[HarmonyPatch(typeof(SoldierAI), "CalculateNextShootDelay")]
internal static class MarkedFireRatePatch
{
	private static void Postfix(SoldierAI __instance, ref float __result)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null || __instance.character == null)
			{
				return;
			}
			int lv = Plugin.LevelOf(__instance.character);
			if (lv >= 1 && __result > 0f)
			{
				float fireMult = Plugin.Pow(Plugin.FireRatePerLevel.Value, lv);
				float raiseMult = Plugin.Pow(Plugin.RaisePerLevel.Value, lv);
				__result /= Mathf.Max(1f, fireMult * raiseMult);
			}
		}
		catch
		{
		}
	}
}

/// <summary>压制免疫：老兵（≥1 级）被压制时继续战斗。</summary>
[HarmonyPatch(typeof(SoldierAI), "OnSuppressed")]
internal static class MarkedSuppressionPatch
{
	private static bool Prefix(SoldierAI __instance)
	{
		try
		{
			if (Plugin.IsActive() && Plugin.SuppressionImmune != null && Plugin.SuppressionImmune.Value &&
				__instance != null && __instance.character != null && Plugin.LevelOf(__instance.character) >= 1)
			{
				return false;
			}
		}
		catch
		{
		}
		return true;
	}
}

/// <summary>绝不投降（本地路径）。</summary>
[HarmonyPatch(typeof(Soldier), "Surrender")]
internal static class MarkedSurrenderPatch
{
	private static bool Prefix(Soldier __instance)
	{
		try
		{
			if (Plugin.IsActive() && Plugin.NeverSurrender != null && Plugin.NeverSurrender.Value &&
				Plugin.LevelOf(__instance) >= 1)
			{
				return false;
			}
		}
		catch
		{
		}
		return true;
	}
}

/// <summary>绝不投降（同步路径）。</summary>
[HarmonyPatch(typeof(Soldier), "SurrenderSynched")]
internal static class MarkedSurrenderSynchedPatch
{
	private static bool Prefix(Soldier __instance)
	{
		try
		{
			if (Plugin.IsActive() && Plugin.NeverSurrender != null && Plugin.NeverSurrender.Value &&
				Plugin.LevelOf(__instance) >= 1)
			{
				return false;
			}
		}
		catch
		{
		}
		return true;
	}
}

/// <summary>老兵受击减伤 + 攻击加伤（Priority.Normal：缩放伤害）。
/// 攻击方身份用子弹上下文（OnHit 设置的射手）与近战上下文（Melee 设置的攻击者）判定。</summary>
[HarmonyPatch(typeof(BodyPart), "HitPart")]
[HarmonyPriority(Priority.Normal)]
internal static class MarkedDamagePatch
{
	private static void Prefix(BodyPart __instance, ref float damage)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null || damage <= 0f)
			{
				return;
			}
			Creature unit = __instance.GetUnit();
			if (unit != null)
			{
				Soldier victim = unit.TryCast<Soldier>();
				if (victim != null)
				{
					int lv = Plugin.LevelOf(victim);
					if (lv >= 1)
					{
						damage *= Plugin.Pow(Plugin.DmgTakenPerLevel.Value, lv);
					}
				}
			}
			// 攻击加伤：子弹上下文优先，近战兜底
			Soldier attacker = Plugin.PeekBulletShooter();
			if (attacker == null)
			{
				attacker = Plugin.PeekMeleeAttacker();
			}
			if (attacker != null)
			{
				int lv = Plugin.LevelOf(attacker);
				if (lv >= 1)
				{
					damage *= Plugin.Pow(Plugin.DmgDealtPerLevel.Value, lv);
				}
			}
		}
		catch
		{
		}
	}
}

/// <summary>老兵受爆炸减伤 / 爆炸加伤。</summary>
[HarmonyPatch(typeof(BodyPart), "TryDamageWithExplosion")]
[HarmonyPriority(Priority.Normal)]
internal static class MarkedExplosionDamagePatch
{
	private static void Prefix(BodyPart __instance, Soldier responsible, ref float explosionDamage)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null || explosionDamage <= 0f)
			{
				return;
			}
			Creature unit = __instance.GetUnit();
			if (unit != null)
			{
				Soldier victim = unit.TryCast<Soldier>();
				if (victim != null)
				{
					int lv = Plugin.LevelOf(victim);
					if (lv >= 1)
					{
						explosionDamage *= Plugin.Pow(Plugin.DmgTakenPerLevel.Value, lv);
					}
				}
			}
			if (responsible != null)
			{
				int lv = Plugin.LevelOf(responsible);
				if (lv >= 1)
				{
					explosionDamage *= Plugin.Pow(Plugin.DmgDealtPerLevel.Value, lv);
				}
			}
		}
		catch
		{
		}
	}
}

/// <summary>老兵驾驶增强：载具驾驶员是老兵时 AI 驾驶速度提升（坦克/载具同理可用）。
/// GetDriveData(Single& directionAngle, Int32& turnSide, Single& kmhSpeed, Int32& movingDir)——
/// 注意 interop 参数名是 directionAngle/turnSide/kmhSpeed/movingDir（Harmony 按名注入，
/// 名字错=整 mod 加载失败，实测踩过）。</summary>
[HarmonyPatch(typeof(AIVehicle), "GetDriveData")]
internal static class VeteranDrivePatch
{
	private static void Postfix(AIVehicle __instance, ref float kmhSpeed)
	{
		try
		{
			if (!Plugin.IsActive() || __instance == null || __instance.veh == null)
			{
				return;
			}
			Soldier driver = null;
			try
			{
				driver = __instance.veh.GetDriver();
			}
			catch
			{
			}
			if (driver == null)
			{
				return;
			}
			int lv = Plugin.LevelOf(driver);
			if (lv >= 1)
			{
				kmhSpeed *= Mathf.Min(1.6f, 1f + 0.1f * lv);
			}
		}
		catch
		{
		}
	}
}

// ═════════════════════════════════════════════════════════════
//  战斗结束重置（OnWin = 战役/自由战斗结束信号）
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(BattleManager), "OnWin")]
internal static class BattleEndResetPatch
{
	private static void Postfix()
	{
		try
		{
			if (Plugin.IsActive())
			{
				Plugin.ResetAll();
			}
		}
		catch
		{
		}
	}
}
