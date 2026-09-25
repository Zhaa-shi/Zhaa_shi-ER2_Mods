using System;
using HarmonyLib;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 1.4.43（用户定案，**推翻 1.4.40-1.4.42 的拦截方案**）：
/// 单位**必须能**"拾取武器并放置于右手"——1.4.40 起的"武器只进背包"拦截把原生右手拾取也堵死了
/// （用户实测：地面菜单「Take Into Right Hand」点了进背包；背包里武器的操作是"穿上"这类穿戴件文案）。
/// 本类现在**只做观测（探针），不做拦截**：记录每一次拿枪/换枪调用（谁、什么枪、哪条路径），
/// 行为完全交还原生。日志强制令见 AGENTS.md §3.5——没有日志的功能代码不予合入。
/// 保留的探针：PickUpCR（地面拾取穿戴协程）/ PickUpItemFromInventory（背包→手持）/
/// AddItemInHand（手部挂物低层）/ LoadAndSetWeapon（Lua 设枪）。全部 LogInfo、低频、无副作用。
/// </summary>
[HarmonyPatch]
internal static class LootPolicy
{
	private static Soldier _player;
	private static float _playerNext;

	/// <summary>玩家当前控制的士兵（0.5s 缓存）。</summary>
	private static bool IsPlayer(Soldier s)
	{
		if (s == null) return false;
		float now = Time.unscaledTime;
		if (now >= _playerNext)
		{
			_playerNext = now + 0.5f;
			try { _player = PlayerController.currentController.ControlledCharacter; }
			catch { _player = null; }
		}
		bool eq = false;
		try { eq = ReferenceEquals(_player, s); } catch { }
		return eq;
	}

	private static string SafeName(Soldier s)
	{
		try { return s.name; } catch { return "?"; }
	}

	// ══════════ 探针（只记录，不拦截）══════════

	/// <summary>① 地面拾取穿戴协程——"拾起置于右手"类交互的执行体。</summary>
	[HarmonyPatch(typeof(Soldier), "PickUpCR", new Type[] { typeof(GameObject), typeof(int) })]
	[HarmonyPrefix]
	private static void PickUpCR_Probe(Soldier __instance, GameObject itemInstance, int wearedItemIndex)
	{
		try
		{
			Plugin.ModLog?.LogInfo("[LootPolicy] PickUpCR: who=" + SafeName(__instance)
				+ " item=" + (itemInstance != null ? itemInstance.name : "null")
				+ " slot=" + wearedItemIndex + " isWeapon="
				+ (itemInstance != null && itemInstance.GetComponent<HandheldItem>() != null)
				+ " isPlayer=" + IsPlayer(__instance));
		}
		catch { }
	}

	/// <summary>② 背包→手持的换枪入口。</summary>
	[HarmonyPatch(typeof(Soldier), "PickUpItemFromInventory",
		new Type[] { typeof(VirtualItem), typeof(InventoryManager), typeof(int) })]
	[HarmonyPrefix]
	private static void PickUpItemFromInventory_Probe(Soldier __instance, VirtualItem virtualItem, int wearedItemIndex)
	{
		try
		{
			Plugin.ModLog?.LogInfo("[LootPolicy] PickUpItemFromInventory: who=" + SafeName(__instance)
				+ " item=" + (virtualItem != null ? virtualItem.item_id : "null")
				+ " slot=" + wearedItemIndex + " isPlayer=" + IsPlayer(__instance));
		}
		catch { }
	}

	/// <summary>③ 手部挂物低层入口——出生配枪也会走这里。</summary>
	[HarmonyPatch(typeof(Soldier), "AddItemInHand", new Type[]
		{ typeof(GameObject), typeof(Transform), typeof(Vector3), typeof(Vector3) })]
	[HarmonyPrefix]
	private static void AddItemInHand_Probe(Soldier __instance, GameObject objectInstance)
	{
		try
		{
			Plugin.ModLog?.LogInfo("[LootPolicy] AddItemInHand: who=" + SafeName(__instance)
				+ " obj=" + (objectInstance != null ? objectInstance.name : "null")
				+ " isWeapon=" + (objectInstance != null && objectInstance.GetComponent<HandheldItem>() != null)
				+ " isPlayer=" + IsPlayer(__instance));
		}
		catch { }
	}

	/// <summary>④ Lua 层设枪入口（任务脚本/小队逻辑/背包面板合成项用）。</summary>
	[HarmonyPatch(typeof(Lua_Soldier), "LoadAndSetWeapon", new Type[] { typeof(string), typeof(int) })]
	[HarmonyPrefix]
	private static void LoadAndSetWeapon_Probe(Lua_Soldier __instance, string itmid, int slot)
	{
		try
		{
			Soldier s = null; try { s = __instance.connectedSoldier; } catch { }
			Plugin.ModLog?.LogInfo("[LootPolicy] LoadAndSetWeapon: who=" + SafeName(s)
				+ " item=" + itmid + " slot=" + slot);
		}
		catch { }
	}
}
