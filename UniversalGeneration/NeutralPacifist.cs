using System;
using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 1.0.2：本 mod 生成的单位（我方/敌方）不主动攻击中立（Civilian）阵营——用户要求。
/// 机制：Harmony Postfix 覆盖 Soldier.GetBestVisibleEnemy 与 Vehicle.CurrentVisibleTarget（getter）：
/// 射手是本 mod 登记的生成物、且目标解析为中立阵营时，把索敌结果置 null。
/// 只拦"主动索敌"——被打仍会通过原生受击路径还手，RTS 指令不受影响。
/// Spottable 是纯接口包装（Il2CppObjectBase，无阵营/transform 成员），目标阵营用
/// GetPosition() 附近 3.5m 内最近存活生物的 faction 判定，结果按 Spottable 指针缓存
/// （位置漂移 &gt;5m 或超 8s 重判；缓存量 &gt;256 清空，防战斗切换后指针复用误判）。
///
/// 1.0.3 修复（用户反馈）：**玩家手动标记的中立目标必须照打**——只拦 AI 自主索敌，不拦玩家指令。
/// 判定：宿主战场指挥官的集火标记（反射读 `GodViewController.CurrentMark` 的 `Spottable` 字段，
/// 无编译期依赖）指向该目标时一律放行，"标谁打谁"照常生效。
/// </summary>
internal static class NeutralPacifist
{
	/// <summary>1.0.8：中立阵营判定（**宽松匹配**）——实际 faction 字符串可能是 "Civilian"/"Civilian_id"/
	/// "civilians" 等变体，1.0.2 的精确等值比较会全部漏判（用户反馈"敌我仍主动攻击中立"）。
	/// 现在：非空且含 "civil"（不区分大小写）即视为中立。</summary>
	private static bool IsCivilianFaction(string f)
	{
		if (string.IsNullOrEmpty(f)) return false;
		return f.IndexOf("civil", StringComparison.OrdinalIgnoreCase) >= 0;
	}
	private const float ProbeRadius = 3.5f;
	private const float CacheSeconds = 8f;
	private const float CacheRecheckMove = 5f;

	private static readonly HashSet<long> spawnedShooters = new HashSet<long>();

	private class CivCache { public bool isCiv; public Vector3 pos; public float at; }
	private static readonly Dictionary<long, CivCache> cache = new Dictionary<long, CivCache>();

	/// <summary>GenRunner 登记生成物时同步登记"射手"（我方/敌方生成的兵与载具）。</summary>
	internal static void RegisterShooter(Soldier s) { try { if (s != null) spawnedShooters.Add((long)s.Pointer); } catch { } }
	internal static void RegisterShooter(Vehicle v) { try { if (v != null) spawnedShooters.Add((long)v.Pointer); } catch { } }

	internal static void ClearRegistry()
	{
		spawnedShooters.Clear();
		cache.Clear();
	}

	/// <summary>1.0.9：该射手自己是不是中立（平民）。用户反馈"中立单位为什么会主动攻击"——
	/// 平民阵营 AI 会自行索敌。开关打开时中立射手一律不获取目标（不主动开战）。
	/// 玩家手动标记的目标仍然放行（标谁打谁优先于本规则）。</summary>
	internal static bool IsCivilianShooter(Soldier s)
	{
		try
		{
			if (s == null) return false;
			return IsCivilianFaction(s.faction);
		}
		catch { return false; }
	}

	private static System.Reflection.FieldInfo markSpotField;
	private static bool markFieldProbed;

	/// <summary>1.0.3：该 Spottable 是否为玩家手动下达的集火标记目标（是 → 放行，不拦）。</summary>
	internal static bool IsPlayerMarkedTarget(Spottable spot)
	{
		if (spot == null) return false;
		try
		{
			object mark = HostLink.CurrentMark;
			if (mark == null) return false;
			if (!markFieldProbed)
			{
				markFieldProbed = true;
				markSpotField = mark.GetType().GetField("Spottable",
					System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			}
			if (markSpotField == null) return false;
			object markedSpot = markSpotField.GetValue(mark);
			if (markedSpot == null) return false;
			Spottable ms = null;
			try { ms = ((Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase)markedSpot).TryCast<Spottable>(); } catch { }
			if (ms == null) return false;
			return (long)ms.Pointer == (long)spot.Pointer;
		}
		catch { return false; }
	}

	internal static bool IsNeutralTarget(Spottable spot)
	{
		try
		{
			if (spot == null) return false;
			long k = (long)spot.Pointer;
			Vector3 pos = spot.GetPosition();
			float now = Time.unscaledTime;
			if (cache.TryGetValue(k, out CivCache c)
				&& now - c.at < CacheSeconds
				&& (pos - c.pos).sqrMagnitude < CacheRecheckMove * CacheRecheckMove)
			{
				return c.isCiv;
			}
			bool isCiv = false;
			float best = ProbeRadius * ProbeRadius;
			Il2CppSystem.Collections.Generic.List<Creature> list = Creature.allCreatures;
			if (list != null)
			{
				for (int i = 0; i < list.Count; i++)
				{
					Creature cr = list[i];
					if (cr == null || !cr.IsAlive || cr.transform == null) continue;
					float d = (cr.transform.position - pos).sqrMagnitude;
					if (d > best) continue;
					best = d;
					string f = null;
					try { Soldier cs = cr.TryCast<Soldier>(); if (cs != null) f = cs.faction; } catch { }
					try { if (string.IsNullOrEmpty(f)) { Vehicle vv = cr.TryCast<Vehicle>(); if (vv != null) f = vv.GetVehicleFaction(); } } catch { }
					isCiv = IsCivilianFaction(f);
				}
			}
			cache[k] = new CivCache { isCiv = isCiv, pos = pos, at = now };
			if (cache.Count > 256) cache.Clear();
			if (Plugin.debugLog.Value && isCiv)
				Plugin.ModLog.LogInfo("[UniGen] 中立保护命中：目标 " + pos.ToString("0.0") + " 判为中立");
			return isCiv;
		}
		catch { return false; }
	}

	/// <summary>1.0.8：是否拦截该射手的自主索敌。**不再只限本 mod 生成物**——
	/// 用户要求"无论友军还是敌人都不主动攻击中立"，所以开关打开时对所有射手生效
	/// （打不打中立是全局规则，与谁生成的无关）。</summary>
	internal static bool ShouldBlock(Soldier shooter)
	{
		if (shooter == null) return false;
		try { return Plugin.noAttackNeutral.Value; } catch { return false; }
	}

	internal static bool ShouldBlock(Vehicle shooter)
	{
		if (shooter == null) return false;
		try { return Plugin.noAttackNeutral.Value; } catch { return false; }
	}
}

/// <summary>生成步兵主动索敌时跳过中立目标。</summary>
[HarmonyPatch(typeof(Soldier), "GetBestVisibleEnemy")]
internal static class NeutralPacifistSoldierPatch
{
	private static void Postfix(Soldier __instance, ref Spottable __result)
	{
		try
		{
			if (__result == null) return;
			if (!NeutralPacifist.ShouldBlock(__instance)) return;
			// 1.0.9：中立单位（平民）不主动攻击任何人
			if (NeutralPacifist.IsCivilianShooter(__instance)) { __result = null; return; }
			if (NeutralPacifist.IsPlayerMarkedTarget(__result)) return; // 1.0.3：玩家标记的目标照打
			if (NeutralPacifist.IsNeutralTarget(__result)) __result = null;
		}
		catch { }
	}
}

/// <summary>生成载具炮塔/武器主动索敌时跳过中立目标。</summary>
[HarmonyPatch(typeof(Vehicle), "CurrentVisibleTarget", MethodType.Getter)]
internal static class NeutralPacifistVehiclePatch
{
	private static void Postfix(Vehicle __instance, ref Spottable __result)
	{
		try
		{
			if (__result == null) return;
			if (!NeutralPacifist.ShouldBlock(__instance)) return;
			if (NeutralPacifist.IsPlayerMarkedTarget(__result)) return; // 1.0.3：玩家标记的目标照打
			if (NeutralPacifist.IsNeutralTarget(__result)) __result = null;
		}
		catch { }
	}
}
