using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Corvostudio.Weapons;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Photon.Pun;
using UnityEngine;

namespace ER2CombatTweaks;

// ─────────────────────────────────────────────────────────────
//  ER2 Combat Tweaks
//  · Friendly Fire off in singleplayer (no more friendly kills)
//  · Ammo modifiers for emplacements (mounted MGs) and tanks,
//    split into machine-gun ammo and main-gun shells.
// ─────────────────────────────────────────────────────────────

/// <summary>炮塔弹药槽分类：火力点 / 火炮 / 防空炮 / 自行火炮 / 坦克机枪 / 坦克主炮。
/// （飞机炸弹与步兵武器走独立补丁，不经过此枚举。）</summary>
internal enum AmmoCategory
{
	Emplacement,
	Artillery,
	AA,
	SPA,
	TankMg,
	TankShells
}

[BepInPlugin("er2.combattweaks", "ER2 Combat Tweaks", "1.2.2")]
[BepInProcess("Easy Red 2.exe")]
public class Plugin : BasePlugin
{
#if CN_BUILD
	internal const bool DefaultChinese = true;
#else
	internal const bool DefaultChinese = false;
#endif

	internal static ManualLogSource ModLog;

	/// <summary>友军爆炸上下文（CreateExplosion 执行期间置位）：用于拦截该爆炸对友军士兵的击飞（Ragdolize/AddForce）。
	/// 窗口比调用期略长（friendlyBlastUntil），覆盖被炸飞的物体延迟砸中友军触发的撞击 Ragdolize。</summary>
	internal static bool FriendlyBlastActive;

	internal static float friendlyBlastUntil;

	internal static string FriendlyBlastFaction;

	/// <summary>友军爆炸上下文是否仍有效（含延迟窗口）。供其他 mod 反射调用。</summary>
	internal static bool FriendlyBlastWindow()
	{
		if (!FriendlyBlastActive && Time.unscaledTime >= friendlyBlastUntil)
		{
			FriendlyBlastFaction = null;
			return false;
		}
		return true;
	}

	// ── 出生保护（修复：友军爆炸窗口内 clamp 把刚出生的士兵拽进墙/穿楼板——玩家实测 wallclip） ──

	/// <summary>InstanceID → 首次见到的时刻。clamp 对出生中的士兵不干预。</summary>
	private static readonly Dictionary<int, float> SpawnStamps = new Dictionary<int, float>();

	internal const float SpawnProtectSeconds = 3f;

	/// <summary>士兵是否处于出生保护期（首次见到记录时间；3s 内返回 true）。</summary>
	internal static bool IsRecentlySpawned(Soldier s)
	{
		try
		{
			int id = s.GetInstanceID();
			if (SpawnStamps.TryGetValue(id, out float t))
			{
				return Time.unscaledTime - t < SpawnProtectSeconds;
			}
			SpawnStamps[id] = Time.unscaledTime;
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>修剪已死/消失单位的出生记录（防 InstanceID 复用导致保护失效）。</summary>
	internal static void TrimSpawnStamps(Il2CppSystem.Collections.Generic.List<Creature> all)
	{
		try
		{
			if (SpawnStamps.Count < 1024)
			{
				return;
			}
			var ids = new HashSet<int>();
			if (all != null)
			{
				foreach (var c in all)
				{
					try
					{
						if (c != null)
						{
							ids.Add(c.GetInstanceID());
						}
					}
					catch
					{
					}
				}
			}
			List<int> dead = null;
			foreach (var kv in SpawnStamps)
			{
				if (!ids.Contains(kv.Key))
				{
					if (dead == null)
					{
						dead = new List<int>();
					}
					dead.Add(kv.Key);
				}
			}
			if (dead != null)
			{
				foreach (int id in dead)
				{
					SpawnStamps.Remove(id);
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>地面吸附：中和待触发跳跃状态 + 垂直速度 + 离地位置兜底。
	/// 实测：爆炸把 m_JumpPower/jumpTimeEnd 写成"待触发跳跃"，不动不消耗、一动就飞——
	/// 必须连状态一起清（Prefix 在游戏 Update 读状态前清，Postfix 清速度，位置兜底保底）。
	/// 修复（玩家实测）：出生中的士兵不得干预（spawn 放置瞬态被拽进墙/穿楼板 = wallclip）；
	/// 位置兜底只对"正被炸飞上天"（上升中）的士兵拉回，正常走路/出生瞬态不拉。</summary>
	internal static void ClampFriendlySoldier(Soldier s)
	{
		try
		{
			if (s == null)
			{
				return;
			}
			if (IsRecentlySpawned(s))
			{
				return; // 出生保护：spawn 放置中不碰位置/速度/跳跃状态
			}
			// 上升中（被炸飞）才做位置兜底；先捕获原始垂直速度
			bool airborne = false;
			try
			{
				airborne = s.currentVelocity.y > 1f;
			}
			catch
			{
			}
			try
			{
				s.m_JumpPower = 0f;
			}
			catch
			{
			}
			try
			{
				s.jumpTimeEnd = 0f;
			}
			catch
			{
			}
			Vector3 v = s.currentVelocity;
			v.y = -6f;
			s.currentVelocity = v;
			try
			{
				s.jumpVelocity = Vector3.zero;
			}
			catch
			{
			}
			if (!airborne)
			{
				return; // 未在上升：正常走路/落地中，不做位置拉回
			}
			// 位置兜底：被炸飞上天且明显离地（>1.5m）时射线向下找地面，插值拉回
			Transform t = s.transform;
			if (t == null)
			{
				return;
			}
			if (Physics.Raycast(t.position, Vector3.down, out RaycastHit hit, 30f))
			{
				float groundY = hit.point.y;
				float dy = t.position.y - groundY;
				// 距离异常大（>8m，射线穿过了楼层/物体）说明命中的不是脚下地面——不拉，防穿楼板
				if (dy > 1.5f && dy <= 8f)
				{
					Vector3 p = t.position;
					p.y = Mathf.Lerp(p.y, groundY + 0.05f, 0.35f);
					t.position = p;
				}
			}
		}
		catch
		{
		}
	}

	// ── General ──
	internal static ConfigEntry<bool> Enabled;

	// ── Friendly Fire ──
	internal static ConfigEntry<bool> FfEnabled;
	internal static ConfigEntry<bool> FfSingleplayerOnly;

	// ── Ammo ──
	internal static ConfigEntry<bool> AmmoEnabled;
	internal static ConfigEntry<float> EmplacementMult;
	internal static ConfigEntry<float> ArtilleryMult;
	internal static ConfigEntry<float> AAMult;
	internal static ConfigEntry<float> SPAMult;
	internal static ConfigEntry<float> TankMgMult;
	internal static ConfigEntry<float> TankShellsMult;
	internal static ConfigEntry<float> InfantryMult;
	internal static ConfigEntry<bool> EmplacementInfinite;
	internal static ConfigEntry<bool> ArtilleryInfinite;
	internal static ConfigEntry<bool> AAInfinite;
	internal static ConfigEntry<bool> SPAInfinite;
	internal static ConfigEntry<bool> TankMgInfinite;
	internal static ConfigEntry<bool> TankShellsInfinite;
	internal static ConfigEntry<bool> PlaneBombInfinite;
	internal static ConfigEntry<bool> InfantryInfinite;

	// ── Explosion ──
	internal static ConfigEntry<float> ExplosionRadiusMult;

	// ── Weapon Damage ──
	internal static ConfigEntry<float> WeaponDamageMult;

	internal static string T(string cn, string en) => DefaultChinese ? cn : en;

	public override void Load()
	{
		ModLog = Log;

		Enabled = Config.Bind("General", "Enabled", true, T(
			"总开关：关闭后本 mod 的友军伤害与弹药调整全部失效。",
			"Master switch. When false, both friendly-fire protection and ammo modifiers are disabled."));

		FfEnabled = Config.Bind("Friendly Fire", "FfEnabled", true, T(
			"开启后同阵营（友军）之间不再造成伤害：你的子弹/爆炸/撞击不会伤到队友，AI 友军也伤不到你。",
			"When enabled, units of the same faction can no longer damage each other: your bullets, explosions and collisions won't hurt allies, and friendly AI can't hurt you."));
		FfSingleplayerOnly = Config.Bind("Friendly Fire", "FfSingleplayerOnly", true, T(
			"默认仅单机（离线）战斗生效，联机对局不受影响（避免公平性问题）。关闭后联机同样拦截友军伤害。",
			"By default the friendly-fire protection only applies in singleplayer (offline) sessions. Disable to also protect allies in multiplayer."));

		AmmoEnabled = Config.Bind("Ammo", "AmmoEnabled", true, T(
			"开启后按下方倍率调整火力点、火炮、坦克、飞机与步兵的弹药量。",
			"When enabled, ammo for emplacements, artillery, tanks, planes and infantry is adjusted with the multipliers below."));
		EmplacementMult = Config.Bind("Ammo", "EmplacementMult", 1f, new ConfigDescription(T(
			"火力点（固定机枪位，如沙袋机枪）弹药倍率：1.0 = 原版，2.0 = 每发只消耗半发，弹药耐用一倍。",
			"Ammo multiplier for emplacements (stationary machine-gun positions): 1.0 = vanilla, 2.0 = rounds last twice as long."),
			new AcceptableValueRange<float>(0.2f, 10000f)));
		EmplacementInfinite = Config.Bind("Ammo", "EmplacementInfinite", false, T(
			"火力点无限弹药：弹链永不耗尽，无需换弹（优先于倍率）。",
			"Infinite ammo for emplacements: the belt never runs out and no reload is needed (overrides the multiplier)."));
		ArtilleryMult = Config.Bind("Ammo", "ArtilleryMult", 1f, new ConfigDescription(T(
			"火炮（固定/牵引火炮）弹药倍率：1.0 = 原版。",
			"Ammo multiplier for artillery pieces: 1.0 = vanilla."),
			new AcceptableValueRange<float>(0.2f, 10000f)));
		ArtilleryInfinite = Config.Bind("Ammo", "ArtilleryInfinite", false, T(
			"火炮无限弹药（优先于倍率）。",
			"Infinite ammo for artillery (overrides the multiplier)."));
		AAMult = Config.Bind("Ammo", "AAMult", 1f, new ConfigDescription(T(
			"防空炮弹药倍率：1.0 = 原版。",
			"Ammo multiplier for AA guns: 1.0 = vanilla."),
			new AcceptableValueRange<float>(0.2f, 10000f)));
		AAInfinite = Config.Bind("Ammo", "AAInfinite", false, T(
			"防空炮无限弹药（优先于倍率）。",
			"Infinite ammo for AA guns (overrides the multiplier)."));
		SPAMult = Config.Bind("Ammo", "SPAMult", 1f, new ConfigDescription(T(
			"自行火炮弹药倍率：1.0 = 原版。",
			"Ammo multiplier for self-propelled artillery: 1.0 = vanilla."),
			new AcceptableValueRange<float>(0.2f, 10000f)));
		SPAInfinite = Config.Bind("Ammo", "SPAInfinite", false, T(
			"自行火炮无限弹药（优先于倍率）。",
			"Infinite ammo for SPA (overrides the multiplier)."));
		TankMgMult = Config.Bind("Ammo", "TankMgMult", 1f, new ConfigDescription(T(
			"坦克/载具机枪弹药倍率（并列机枪/车体机枪）：1.0 = 原版。",
			"Ammo multiplier for tank and vehicle machine guns (coaxial / hull MG): 1.0 = vanilla."),
			new AcceptableValueRange<float>(0.2f, 10000f)));
		TankMgInfinite = Config.Bind("Ammo", "TankMgInfinite", false, T(
			"坦克/载具机枪无限弹药（优先于倍率）。",
			"Infinite ammo for tank/vehicle machine guns (overrides the multiplier)."));
		TankShellsMult = Config.Bind("Ammo", "TankShellsMult", 1f, new ConfigDescription(T(
			"坦克主炮炮弹倍率：1.0 = 原版，2.0 = 炮弹耐用一倍。只改炮弹、只改机枪、或两者都改都行。",
			"Ammo multiplier for tank main-gun shells: 1.0 = vanilla. Modify shells, machine guns, or both."),
			new AcceptableValueRange<float>(0.2f, 10000f)));
		TankShellsInfinite = Config.Bind("Ammo", "TankShellsInfinite", false, T(
			"坦克主炮无限炮弹（优先于倍率）。",
			"Infinite shells for tank main guns (overrides the multiplier)."));
		PlaneBombInfinite = Config.Bind("Ammo", "PlaneBombInfinite", false, T(
			"飞机无限炸弹：投弹后炸弹舱自动补满，永不耗尽。",
			"Infinite bombs for aircraft: the bomb bay refills after every drop."));
		InfantryMult = Config.Bind("Ammo", "InfantryMult", 1f, new ConfigDescription(T(
			"步兵武器弹药倍率（步枪/冲锋枪/机枪等）：1.0 = 原版。",
			"Ammo multiplier for infantry weapons (rifles, SMGs, MGs, etc.): 1.0 = vanilla."),
			new AcceptableValueRange<float>(0.2f, 10000f)));
		InfantryInfinite = Config.Bind("Ammo", "InfantryInfinite", false, T(
			"步兵武器无限弹药（优先于倍率）。",
			"Infinite ammo for infantry weapons (overrides the multiplier)."));

		ExplosionRadiusMult = Config.Bind("Explosion", "ExplosionRadiusMult", 1f, new ConfigDescription(T(
			"爆炸范围倍率：1.0 = 原版，2.0 = 爆炸范围翻倍（冲击波/碎片/震屏同步缩放）。",
			"Explosion radius multiplier: 1.0 = vanilla, 2.0 = double the blast radius (shockwave, fragments, and camera shake scale together)."),
			new AcceptableValueRange<float>(0.2f, 10f)));

		WeaponDamageMult = Config.Bind("Weapon", "WeaponDamageMult", 1f, new ConfigDescription(T(
			"步兵受击伤害倍率（全局）：1.0 = 原版，2.0 = 双倍伤害，0.5 = 半伤。覆盖子弹/近战/碰撞/爆炸对士兵造成的伤害。",
			"Global infantry damage multiplier: 1.0 = vanilla, 2.0 = double damage, 0.5 = half damage. Affects bullet, melee, collision and explosion damage dealt to soldiers."),
			new AcceptableValueRange<float>(0.1f, 100f)));

		// 即使初始关闭也装 patch，方便 ModManager 热开启。
		new Harmony("er2.combattweaks").PatchAll(GetType().Assembly);
		ModLog.LogInfo("ER2 Combat Tweaks 1.2.2 loaded.");
	}

	// ── 判定辅助 ──

	/// <summary>联机检测（与 ThrowableWheel 同款）：Photon 已连接且非离线模式。</summary>
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

	/// <summary>阵营 → 阵营方（"_" 后缀：Germany_axis → axis；无下划线返回原串）。
	/// 跨国家同盟（US_allies / UK_allies）视为同一方。</summary>
	internal static string SideOf(string faction)
	{
		if (string.IsNullOrEmpty(faction))
		{
			return "";
		}
		int i = faction.LastIndexOf('_');
		return (i >= 0 && i < faction.Length - 1) ? faction.Substring(i + 1) : faction;
	}

	/// <summary>友军伤害保护是否生效。</summary>
	internal static bool IsFfActive()
	{
		if (Enabled == null || !Enabled.Value || FfEnabled == null || !FfEnabled.Value)
		{
			return false;
		}
		if (FfSingleplayerOnly != null && FfSingleplayerOnly.Value && IsOnlineMultiplayer())
		{
			return false;
		}
		return true;
	}

	/// <summary>弹药调整是否生效。</summary>
	internal static bool IsAmmoActive()
	{
		return Enabled != null && Enabled.Value && AmmoEnabled != null && AmmoEnabled.Value;
	}

	/// <summary>从弹头 id 解析口径（毫米）。解析失败返回 -1。</summary>
	internal static float ParseCaliber(string bulletId)
	{
		if (string.IsNullOrEmpty(bulletId))
		{
			return -1f;
		}
		Match m = Regex.Match(bulletId, @"(\d{1,3})(?:[_\.\-](\d{1,2}))?\s*mm");
		if (!m.Success)
		{
			return -1f;
		}
		float cal = float.Parse(m.Groups[1].Value);
		if (m.Groups[2].Success && !string.IsNullOrEmpty(m.Groups[2].Value))
		{
			cal += float.Parse(m.Groups[2].Value) / (m.Groups[2].Value.Length == 1 ? 10f : 100f);
		}
		return cal;
	}
}

// ═════════════════════════════════════════════════════════════
//  友军伤害保护
// ═════════════════════════════════════════════════════════════

/// <summary>原生静态判定点：几乎全部步兵伤害路径都会经过 AllowDamage(from, to)。
/// Priority.First：先于其他 mod（如 LimbTweaks 的 HitPart 伤害累计）短路，拦截后其累计不会触发（友军不再断肢）。</summary>
[HarmonyPatch(typeof(BodyPart), "AllowDamage")]
[HarmonyPriority(Priority.First)]
internal static class AllowDamagePatch
{
	private static bool Prefix(ref bool __result, string fromFaction, string toFaction)
	{
		if (!Plugin.IsFfActive() || string.IsNullOrEmpty(fromFaction))
		{
			return true;
		}
		if (fromFaction == toFaction)
		{
			__result = false;
			return false;
		}
		return true;
	}
}

/// <summary>步兵受击兜底：同阵营命中直接吞掉（子弹/近战/燃烧等走 HitPart 的路径）。</summary>
[HarmonyPatch(typeof(BodyPart), "HitPart")]
[HarmonyPriority(Priority.First)]
internal static class BodyPartHitPatch
{
	private static bool Prefix(BodyPart __instance, ref bool __result, string fromFaction)
	{
		if (!Plugin.IsFfActive() || string.IsNullOrEmpty(fromFaction) || __instance == null)
		{
			return true;
		}
		string victim;
		try
		{
			victim = __instance.GetFaction();
		}
		catch
		{
			return true;
		}
		if (!string.IsNullOrEmpty(victim) && fromFaction == victim)
		{
			__result = false;
			return false;
		}
		return true;
	}
}

/// <summary>步兵爆炸伤害：由友军士兵负责的爆炸不伤己方士兵。</summary>
[HarmonyPatch(typeof(BodyPart), "TryDamageWithExplosion")]
[HarmonyPriority(Priority.First)]
internal static class BodyPartExplosionPatch
{
	private static bool Prefix(BodyPart __instance, Soldier responsible)
	{
		if (!Plugin.IsFfActive() || responsible == null || __instance == null)
		{
			return true;
		}
		string attacker;
		try
		{
			attacker = responsible.faction;
		}
		catch
		{
			return true;
		}
		if (string.IsNullOrEmpty(attacker))
		{
			return true;
		}
		string victim;
		try
		{
			victim = __instance.GetFaction();
		}
		catch
		{
			return true;
		}
		if (!string.IsNullOrEmpty(victim) && attacker == victim)
		{
			return false; // 跳过原方法 = 不造成伤害
		}
		return true;
	}
}

/// <summary>载具受击兜底：同阵营火力（子弹/炮弹/碰撞）不伤己方载具。</summary>
[HarmonyPatch(typeof(VehicleDamagablePart), "HitPart")]
[HarmonyPriority(Priority.First)]
internal static class VehicleHitPatch
{
	private static bool Prefix(VehicleDamagablePart __instance, ref bool __result, string fromFaction)
	{
		if (!Plugin.IsFfActive() || string.IsNullOrEmpty(fromFaction) || __instance == null)
		{
			return true;
		}
		string victim;
		try
		{
			victim = __instance.GetFaction();
		}
		catch
		{
			return true;
		}
		if (!string.IsNullOrEmpty(victim) && fromFaction == victim)
		{
			__result = false;
			return false;
		}
		return true;
	}
}

/// <summary>载具装甲穿透：炮弹命中己方载具时拦截（shooter 同阵营）。</summary>
[HarmonyPatch(typeof(VehicleDamagablePart), "TryPenetrateArmor")]
[HarmonyPriority(Priority.First)]
internal static class VehicleArmorPatch
{
	private static bool Prefix(VehicleDamagablePart __instance, ref bool __result, Soldier shooter)
	{
		if (!Plugin.IsFfActive() || shooter == null || __instance == null)
		{
			return true;
		}
		string attacker;
		try
		{
			attacker = shooter.faction;
		}
		catch
		{
			return true;
		}
		if (string.IsNullOrEmpty(attacker))
		{
			return true;
		}
		string victim;
		try
		{
			victim = __instance.GetFaction();
		}
		catch
		{
			return true;
		}
		if (!string.IsNullOrEmpty(victim) && attacker == victim)
		{
			__result = false;
			return false;
		}
		return true;
	}
}

// ═════════════════════════════════════════════════════════════
//  友军爆炸击飞拦截
//  友军保护只拦伤害不够：游戏对爆炸范围内士兵的击飞（Ragdolize/AddForce）
//  是独立路径——自己/友军炸药爆开时玩家会被炸飞却无伤害。这里在
//  CreateExplosion 期间记录"友军爆炸"上下文，期间拦截友军士兵的 Ragdolize/AddForce。
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(Explosion), "CreateExplosion")]
internal static class ExplosionFactionContextPatch
{
	private static void Prefix(Vector3 position, Soldier responsible)
	{
		if (!Plugin.IsFfActive())
		{
			return;
		}
		string fac = null;
		if (responsible != null)
		{
			try
			{
				fac = responsible.faction;
			}
			catch
			{
			}
			// 修复（玩家实测）：窗口只能由"友军爆炸"建立——敌人爆炸建立窗口会把
			// 该敌人阵营死亡士兵的 Ragdolize（倒地）拦截 4 秒 → "被打死不倒地、站定冻结"。
			// 敌人爆炸（含敌人手雷/炮弹）不建立窗口，敌人倒地恢复正常。
			if (!string.IsNullOrEmpty(fac))
			{
				try
				{
					PlayerController pc = PlayerController.currentController;
					if (pc != null && pc.ControlledCharacter != null)
					{
						Soldier ps = pc.ControlledCharacter.TryCast<Soldier>();
						if (ps == null || Plugin.SideOf(ps.faction) != Plugin.SideOf(fac))
						{
							return; // 敌人/无玩家上下文 → 不是友军爆炸
						}
					}
					else
					{
						return;
					}
				}
				catch
				{
					return;
				}
			}
		}
		if (string.IsNullOrEmpty(fac))
		{
			// 无归属/归属阵营为空的爆炸（如玩家放置的炸药）：按爆心最近的活体士兵推断归属，
			// 若与玩家同阵营则视为友军爆炸（建立上下文 → 拦截击飞/邻近物体不发射）。
			try
			{
				string playerFac = null;
				try
				{
					PlayerController pc = PlayerController.currentController;
					if (pc != null && pc.ControlledCharacter != null)
					{
						Soldier ps = pc.ControlledCharacter.TryCast<Soldier>();
						if (ps != null)
						{
							playerFac = ps.faction;
						}
					}
				}
				catch
				{
				}
				if (string.IsNullOrEmpty(playerFac))
				{
					return;
				}
				Soldier nearest = null;
				float best = 16f; // 4m
				Il2CppSystem.Collections.Generic.List<Creature> all = Creature.aliveCreatures;
				if (all != null)
				{
					foreach (Creature c in all)
					{
						try
						{
							if (c == null || c.transform == null)
							{
								continue;
							}
							Soldier s = c.TryCast<Soldier>();
							if (s == null)
							{
								continue;
							}
							float d = (c.transform.position - position).sqrMagnitude;
							if (d < best)
							{
								best = d;
								nearest = s;
							}
						}
						catch
						{
						}
					}
				}
				if (nearest != null && nearest.faction == playerFac)
				{
					fac = playerFac;
				}
			}
			catch
			{
			}
		}
		if (string.IsNullOrEmpty(fac))
		{
			return;
		}
		Plugin.FriendlyBlastActive = true;
		Plugin.friendlyBlastUntil = Time.unscaledTime + 4f;
		Plugin.FriendlyBlastFaction = fac;
	}

	// 清场必须最后跑（Last），保证同帧其他 mod 的 Postfix（如 MorePhysics 爆炸物理处理）还能读到上下文。
	[HarmonyPriority(Priority.Last)]
	private static void Postfix()
	{
		Plugin.FriendlyBlastActive = false;
	}
}

[HarmonyPatch(typeof(RagdollManager), "Ragdolize", new Type[] { typeof(Vector3) })]
internal static class FriendlyBlastRagdollPatch
{
	private static bool Prefix(RagdollManager __instance)
	{
		if (!Plugin.FriendlyBlastWindow() || string.IsNullOrEmpty(Plugin.FriendlyBlastFaction) || __instance == null)
		{
			return true;
		}
		Creature owner = null;
		try
		{
			owner = __instance.GetComponentInParent<Creature>();
		}
		catch
		{
		}
		if (owner == null)
		{
			try
			{
				owner = __instance.GetComponent<Creature>();
			}
			catch
			{
			}
		}
		if (owner == null)
		{
			return true;
		}
		string fac;
		try
		{
			// 只有 Soldier 有 faction（Creature 基类没有，需 TryCast）
			Soldier s = owner.TryCast<Soldier>();
			if (s == null)
			{
				return true;
			}
			fac = s.faction;
		}
		catch
		{
			return true;
		}
		if (!string.IsNullOrEmpty(fac) && fac == Plugin.FriendlyBlastFaction)
		{
			return false; // 友军爆炸不再把友军（含玩家）炸飞
		}
		return true;
	}
}

[HarmonyPatch(typeof(RagdollManager), "AddForce", new Type[] { typeof(Vector3) })]
internal static class FriendlyBlastForcePatch
{
	private static bool Prefix(RagdollManager __instance)
	{
		if (!Plugin.FriendlyBlastWindow() || string.IsNullOrEmpty(Plugin.FriendlyBlastFaction) || __instance == null)
		{
			return true;
		}
		Creature owner = null;
		try
		{
			owner = __instance.GetComponentInParent<Creature>();
		}
		catch
		{
		}
		if (owner == null)
		{
			try
			{
				owner = __instance.GetComponent<Creature>();
			}
			catch
			{
			}
		}
		if (owner == null)
		{
			return true;
		}
		string fac;
		try
		{
			Soldier s = owner.TryCast<Soldier>();
			if (s == null)
			{
				return true;
			}
			fac = s.faction;
		}
		catch
		{
			return true;
		}
		if (!string.IsNullOrEmpty(fac) && fac == Plugin.FriendlyBlastFaction)
		{
			return false;
		}
		return true;
	}
}

// ═════════════════════════════════════════════════════════════
//  友军子弹命中整链拦截
//  LimbTweaks 的断肢累计除了 HitPart 前缀，还有 BulletInstance.OnHit 路径
//  （尸体断肢在 OnHit Postfix，与伤害无关）——必须在 First 就把友军命中短路。
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(BulletInstance), "OnHit")]
[HarmonyPriority(Priority.First)]
internal static class FriendlyBulletHitPatch
{
	private static bool Prefix(BulletInstance __instance, RaycastHit hit)
	{
		if (!Plugin.IsFfActive() || __instance == null || hit.collider == null)
		{
			return true;
		}
		string attacker;
		try
		{
			Soldier sh = __instance.shooter;
			if (sh == null)
			{
				return true;
			}
			attacker = sh.faction;
		}
		catch
		{
			return true;
		}
		if (string.IsNullOrEmpty(attacker))
		{
			return true;
		}
		Creature target;
		try
		{
			target = hit.collider.GetComponentInParent<Creature>();
		}
		catch
		{
			return true;
		}
		if (target == null)
		{
			return true;
		}
		string vf;
		try
		{
			Soldier vs = target.TryCast<Soldier>();
			if (vs == null)
			{
				return true;
			}
			vf = vs.faction;
		}
		catch
		{
			return true;
		}
		if (!string.IsNullOrEmpty(vf) && attacker == vf)
		{
			return false; // 友军子弹整条命中处理（伤害/特效/断肢）全部跳过
		}
		return true;
	}
}

// ═════════════════════════════════════════════════════════════
//  爆炸"跳飞"拦截
//  游戏爆炸对士兵的击飞 = 把跳跃系统初速调大（Jump/OnJumpSynch/jumpVelocity），
//  士兵保持可操控地飞上天、滞空后才落下（"全队在天上走路"）。友军爆炸窗口内拦截友军。
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(Soldier), "Jump", new Type[0])]
internal static class FriendlyBlastJumpPatch
{
	private static bool Prefix(Soldier __instance)
	{
		if (!Plugin.FriendlyBlastWindow() || __instance == null || string.IsNullOrEmpty(Plugin.FriendlyBlastFaction))
		{
			return true;
		}
		try
		{
			if (__instance.faction == Plugin.FriendlyBlastFaction)
			{
				return false; // 友军爆炸不再把友军（含玩家）"跳"上天
			}
		}
		catch
		{
		}
		return true;
	}
}

[HarmonyPatch(typeof(Soldier), "OnJumpSynch", new Type[] { typeof(Vector3) })]
internal static class FriendlyBlastJumpSynchPatch
{
	private static bool Prefix(Soldier __instance)
	{
		if (!Plugin.FriendlyBlastWindow() || __instance == null || string.IsNullOrEmpty(Plugin.FriendlyBlastFaction))
		{
			return true;
		}
		try
		{
			if (__instance.faction == Plugin.FriendlyBlastFaction)
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

// ═════════════════════════════════════════════════════════════
//  地面吸附（友军爆炸窗口内）
//  游戏的爆炸"跳飞"由游戏自身战斗管理施加（原生直写 jumpVelocity/currentVelocity，
//  拦截 setter 拦不住发射、还会掐断跳跃弧线）。改为状态中和：
//  ①多阶段夹持（士兵自身 Update + PlayerController.Update 双路，覆盖不同写入时序）
//  ②离地位置兜底（射线找地面拉回）——无论发射走哪条通道/哪个帧，人都站回地上。
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(PlayerController), "Update")]
internal static class FriendlyGroundClampPatch
{
	private static void Postfix()
	{
		try
		{
			if (!Plugin.FriendlyBlastWindow() || string.IsNullOrEmpty(Plugin.FriendlyBlastFaction))
			{
				return;
			}
			Il2CppSystem.Collections.Generic.List<Creature> all = Creature.aliveCreatures;
			if (all == null)
			{
				return;
			}
			foreach (Creature c in all)
			{
				try
				{
					if (c == null)
					{
						continue;
					}
					Soldier s = c.TryCast<Soldier>();
					if (s != null && s.faction == Plugin.FriendlyBlastFaction)
					{
						Plugin.ClampFriendlySoldier(s);
					}
				}
				catch
				{
				}
			}
			Plugin.TrimSpawnStamps(all);
		}
		catch
		{
		}
	}
}

[HarmonyPatch(typeof(Soldier), "Update")]
internal static class SoldierGroundClampPatch
{
	/// <summary>Prefix：在游戏 Update 读取/应用跳跃状态之前，清掉待触发的跳跃（"一动就飞"根因）。
	/// 出生保护：刚出生的士兵（spawn 放置中）不干预——修复 wallclip。</summary>
	private static bool Prefix(Soldier __instance)
	{
		try
		{
			if (__instance == null)
			{
				return true;
			}
			if (!Plugin.FriendlyBlastWindow() || string.IsNullOrEmpty(Plugin.FriendlyBlastFaction))
			{
				return true;
			}
			if (__instance.faction == Plugin.FriendlyBlastFaction)
			{
				if (Plugin.IsRecentlySpawned(__instance))
				{
					return true;
				}
				try
				{
					__instance.m_JumpPower = 0f;
				}
				catch
				{
				}
				try
				{
					__instance.jumpTimeEnd = 0f;
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		return true;
	}

	private static void Postfix(Soldier __instance)
	{
		try
		{
			if (__instance == null)
			{
				return;
			}
			if (!Plugin.FriendlyBlastWindow() || string.IsNullOrEmpty(Plugin.FriendlyBlastFaction))
			{
				return;
			}
			if (__instance.faction == Plugin.FriendlyBlastFaction)
			{
				Plugin.ClampFriendlySoldier(__instance);
			}
		}
		catch
		{
		}
	}
}

// ═════════════════════════════════════════════════════════════
//  火力点 / 坦克弹药调整
// ═════════════════════════════════════════════════════════════

/// <summary>
/// 每发弹药的实际消耗 = 1/倍率：倍率 2.0 时打两发才消耗一发（小数用累积器折算）；
/// 无限弹药 = 打一发补一发，弹链永不耗尽。分类按“火力点 / 坦克机枪 / 坦克炮弹”分别配置。
/// </summary>
[HarmonyPatch(typeof(TurretGun), "ExtractOneBullet")]
internal static class TurretAmmoPatch
{
	private sealed class SlotState
	{
		public AmmoCategory Category;
		public float Spare; // 小数弹药累积器
	}

	// ConditionWeakTable：以引用相等为键，炮塔武器销毁后自动回收。
	private static readonly ConditionalWeakTable<TurretWeapon, SlotState> States = new ConditionalWeakTable<TurretWeapon, SlotState>();

	private static SlotState GetState(TurretGun gun, TurretWeapon weapon)
	{
		if (States.TryGetValue(weapon, out SlotState st))
		{
			return st;
		}
		st = new SlotState { Category = Classify(gun, weapon) };
		States.Add(weapon, st);
		return st;
	}

	/// <summary>弹药槽分类：TurretMG 一定是火力点；否则看载具类型（火炮/防空/自行火炮），再看口径。</summary>
	private static AmmoCategory Classify(TurretGun gun, TurretWeapon weapon)
	{
		try
		{
			if (gun.TryCast<TurretMG>() != null)
			{
				return AmmoCategory.Emplacement;
			}
			Vehicle v = gun.GetComponentInParent<Vehicle>();
			if (v == null)
			{
				return AmmoCategory.Emplacement;
			}
			// 按载具类型细分
			try { if (v.IsArtillery()) return AmmoCategory.Artillery; } catch { }
			try { if (v.IsAA()) return AmmoCategory.AA; } catch { }
			try { if (v.IsSPA()) return AmmoCategory.SPA; } catch { }
			// 坦克/普通载具：按口径分机枪/炮弹
			float cal = CaliberOf(weapon);
			if (cal >= 20f)
			{
				return AmmoCategory.TankShells;
			}
			if (cal > 0f)
			{
				return AmmoCategory.TankMg;
			}
			// 无口径信息：弹链大容量当机枪，小容量当炮弹。
			return weapon.maxAmmoCount >= 50 ? AmmoCategory.TankMg : AmmoCategory.TankShells;
		}
		catch
		{
			return AmmoCategory.TankMg;
		}
	}

	private static float CaliberOf(TurretWeapon weapon)
	{
		try
		{
			var ammos = weapon.ammos;
			if (ammos == null)
			{
				return -1f;
			}
			for (int i = 0; i < ammos.Length; i++)
			{
				TurretWeaponBullet b = ammos[i];
				if (b == null)
				{
					continue;
				}
				float cal = Plugin.ParseCaliber(b.bullet_id);
				if (cal > 0f)
				{
					return cal;
				}
			}
		}
		catch
		{
		}
		return -1f;
	}

	private static bool Prefix(TurretGun __instance, TurretWeapon weapon, ref short __state)
	{
		if (!Plugin.IsAmmoActive() || weapon == null)
		{
			return true;
		}
		__state = weapon.loadedAmmoCount;
		return true;
	}

	private static void Postfix(TurretGun __instance, TurretWeapon weapon, short __state)
	{
		try
		{
			if (!Plugin.IsAmmoActive() || weapon == null || __instance == null)
			{
				return;
			}
			short prev = __state;
			short now = weapon.loadedAmmoCount;
			if (now >= prev || prev <= 0)
			{
				return; // 未消耗（空枪/没扣弹）
			}
			int consumed = prev - now;
			SlotState st = GetState(__instance, weapon);

			bool infinite;
			float mult;
			switch (st.Category)
			{
				case AmmoCategory.Emplacement:
					infinite = Plugin.EmplacementInfinite != null && Plugin.EmplacementInfinite.Value;
					mult = Plugin.EmplacementMult != null ? Plugin.EmplacementMult.Value : 1f;
					break;
				case AmmoCategory.Artillery:
					infinite = Plugin.ArtilleryInfinite != null && Plugin.ArtilleryInfinite.Value;
					mult = Plugin.ArtilleryMult != null ? Plugin.ArtilleryMult.Value : 1f;
					break;
				case AmmoCategory.AA:
					infinite = Plugin.AAInfinite != null && Plugin.AAInfinite.Value;
					mult = Plugin.AAMult != null ? Plugin.AAMult.Value : 1f;
					break;
				case AmmoCategory.SPA:
					infinite = Plugin.SPAInfinite != null && Plugin.SPAInfinite.Value;
					mult = Plugin.SPAMult != null ? Plugin.SPAMult.Value : 1f;
					break;
				case AmmoCategory.TankMg:
					infinite = Plugin.TankMgInfinite != null && Plugin.TankMgInfinite.Value;
					mult = Plugin.TankMgMult != null ? Plugin.TankMgMult.Value : 1f;
					break;
				default:
					infinite = Plugin.TankShellsInfinite != null && Plugin.TankShellsInfinite.Value;
					mult = Plugin.TankShellsMult != null ? Plugin.TankShellsMult.Value : 1f;
					break;
			}

			if (mult == 1f && !infinite)
			{
				return;
			}

			if (infinite)
			{
				// 打一发补一发：弹量不下降，永不换弹。
				weapon.loadedAmmoCount = (short)Math.Min(short.MaxValue, now + consumed);
				return;
			}

			if (mult > 0f)
			{
				// 每发回补 (1 - 1/倍率) 发；倍率越大回补越多，等效消耗 1/倍率。
				st.Spare += consumed * (1f - 1f / mult);
			}

			int add = 0;
			while (st.Spare >= 1f)
			{
				add++;
				st.Spare -= 1f;
			}
			// 倍率小于 1（弹药更少）：负累积到整发时额外扣一发，且不低于 0。
			while (st.Spare <= -1f)
			{
				add--;
				st.Spare += 1f;
			}
			if (add != 0)
			{
				int cap = Math.Max(weapon.maxAmmoCount, (int)prev);
				weapon.loadedAmmoCount = (short)Math.Clamp((int)now + add, 0, Math.Min(cap, short.MaxValue));
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning($"ER2 Combat Tweaks ammo patch failed: {ex.Message}");
		}
	}
}

// ═════════════════════════════════════════════════════════════
//  飞机炸弹弹药调整
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(VehiclePlane), "DropBombs")]
internal static class PlaneBombAmmoPatch
{
	private static void Prefix(VehiclePlane __instance, ref int __state)
	{
		if (!Plugin.IsAmmoActive() || __instance == null)
		{
			__state = -1;
			return;
		}
		try { __state = __instance.CountBombs(); } catch { __state = -1; }
	}

	private static void Postfix(VehiclePlane __instance, int __state)
	{
		if (__state < 0 || !Plugin.IsAmmoActive()) return;
		try
		{
			int now = __instance.CountBombs();
			if (now >= __state) return; // 未消耗
			bool infinite = Plugin.PlaneBombInfinite != null && Plugin.PlaneBombInfinite.Value;
			if (infinite)
			{
				__instance.RefillBombBay();
				return;
			}
		}
		catch { }
	}
}

// ═════════════════════════════════════════════════════════════
//  步兵武器弹药调整
//  步兵开枪耗弹走 Magazine（弹匣）的 ExtractOneBullet / ExtractBullets，
//  而非 GenericGun.ExtractOneBullet（后者 CallerCount≈0，开枪不调用）。
// ═════════════════════════════════════════════════════════════

internal static class InfantryMagAmmoHelper
{
	private sealed class MagState
	{
		public float Spare; // 小数弹药累积器
	}

	private static readonly ConditionalWeakTable<Magazine, MagState> States = new ConditionalWeakTable<Magazine, MagState>();

	private static MagState GetState(Magazine mag)
	{
		if (States.TryGetValue(mag, out MagState st)) return st;
		st = new MagState();
		States.Add(mag, st);
		return st;
	}

	/// <summary>按倍率/无限开关回补 consumed 发弹药。</summary>
	internal static void Restore(Magazine mag, int consumed)
	{
		try
		{
			if (mag == null || consumed <= 0) return;
			bool infinite = Plugin.InfantryInfinite != null && Plugin.InfantryInfinite.Value;
			float mult = Plugin.InfantryMult != null ? Plugin.InfantryMult.Value : 1f;

			if (mult == 1f && !infinite) return;

			if (infinite)
			{
				mag.Refill(consumed); // 打一发补一发
				return;
			}

			if (mult <= 0f) return;
			MagState st = GetState(mag);
			st.Spare += consumed * (1f - 1f / mult);

			int add = 0;
			while (st.Spare >= 1f) { add++; st.Spare -= 1f; }
			while (st.Spare <= -1f) { add--; st.Spare += 1f; }

			if (add > 0)
			{
				mag.Refill(add);
			}
			else if (add < 0)
			{
				// 倍率 < 1：额外扣弹，直接整体赋值 currentAmmo（陷阱 1）。
				int now = mag.GetCurrentAmmoCount();
				mag.currentAmmo = new ProtectedInt(Math.Max(0, now + add));
			}
		}
		catch { }
	}
}

[HarmonyPatch(typeof(Magazine), "ExtractOneBullet")]
internal static class MagExtractOnePatch
{
	private static void Prefix(Magazine __instance, ref int __state)
	{
		if (!Plugin.IsAmmoActive() || __instance == null)
		{
			__state = -1;
			return;
		}
		try { __state = __instance.GetCurrentAmmoCount(); } catch { __state = -1; }
	}

	private static void Postfix(Magazine __instance, int __state)
	{
		if (__state < 0 || !Plugin.IsAmmoActive() || __instance == null) return;
		try
		{
			int now = __instance.GetCurrentAmmoCount();
			if (now >= __state || __state <= 0) return; // 未消耗
			InfantryMagAmmoHelper.Restore(__instance, __state - now);
		}
		catch { }
	}
}

[HarmonyPatch(typeof(Magazine), "ExtractBullets")]
internal static class MagExtractBulletsPatch
{
	private static void Prefix(Magazine __instance, ref int __state)
	{
		if (!Plugin.IsAmmoActive() || __instance == null)
		{
			__state = -1;
			return;
		}
		try { __state = __instance.GetCurrentAmmoCount(); } catch { __state = -1; }
	}

	private static void Postfix(Magazine __instance, int __state)
	{
		if (__state < 0 || !Plugin.IsAmmoActive() || __instance == null) return;
		try
		{
			int now = __instance.GetCurrentAmmoCount();
			if (now >= __state || __state <= 0) return; // 未消耗
			InfantryMagAmmoHelper.Restore(__instance, __state - now);
		}
		catch { }
	}
}

// ═════════════════════════════════════════════════════════════
//  爆炸范围倍率
//  Priority.First 确保在友军爆炸上下文之前修改半径。
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(Explosion), "CreateExplosion")]
[HarmonyPriority(Priority.First)]
internal static class ExplosionRadiusPatch
{
	private static bool _cached;
	private static float _origFull = 1f;
	private static float _origFrag = 1f;
	private static float _origCam = 1f;

	private static void CacheOrig()
	{
		if (_cached) return;
		try
		{
			_origFull = Explosion.FULLDAMAGE_RADIUS_MULT;
			_origFrag = Explosion.FRAGMENTS_RADIUS_MULT;
			_origCam = Explosion.CAM_SHAKE_RADIUS_MULT;
			_cached = true;
		}
		catch { }
	}

	private static void Prefix(ref float explosionRadius)
	{
		try
		{
			if (Plugin.ExplosionRadiusMult == null) return;
			float mult = Plugin.ExplosionRadiusMult.Value;
			if (mult <= 0f) return;
			CacheOrig();
			if (mult != 1f)
			{
				explosionRadius *= mult;
			}
			// 静态字段始终同步：mult=1 时恢复原值，mult≠1 时按倍率缩放
			// （native CreateExplosion 内部用这些乘数计算全伤害/碎片/震屏半径）
			Explosion.FULLDAMAGE_RADIUS_MULT = _origFull * mult;
			Explosion.FRAGMENTS_RADIUS_MULT = _origFrag * mult;
			Explosion.CAM_SHAKE_RADIUS_MULT = _origCam * mult;
		}
		catch { }
	}
}

// ═════════════════════════════════════════════════════════════
//  步兵受击伤害倍率（全局）
//  Priority.Normal：在友军伤害保护（Priority.First）之后运行，
//  友军命中已被短路，只对敌人/敌军命中生效。
//  覆盖两条路径：HitPart（子弹/近战/碰撞）与 TryDamageWithExplosion（爆炸）。
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(BodyPart), "HitPart")]
[HarmonyPriority(Priority.Normal)]
internal static class WeaponDamagePatch
{
	private static void Prefix(ref float damage)
	{
		try
		{
			if (Plugin.WeaponDamageMult != null)
			{
				float mult = Plugin.WeaponDamageMult.Value;
				if (mult != 1f && mult > 0f)
					damage *= mult;
			}
		}
		catch { }
	}
}

[HarmonyPatch(typeof(BodyPart), "TryDamageWithExplosion")]
[HarmonyPriority(Priority.Normal)]
internal static class WeaponExplosionDamagePatch
{
	private static void Prefix(ref float explosionDamage)
	{
		try
		{
			if (Plugin.WeaponDamageMult != null)
			{
				float mult = Plugin.WeaponDamageMult.Value;
				if (mult != 1f && mult > 0f)
					explosionDamage *= mult;
			}
		}
		catch { }
	}
}