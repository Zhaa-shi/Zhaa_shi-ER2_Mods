using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ER2AIFood;

[BepInPlugin("er2.aifood", "ER2 AI Food", "1.4.0")]
public class Plugin : BasePlugin
{
	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> enabled;

	internal static ConfigEntry<float> eatBelowHp;

	internal static ConfigEntry<float> checkInterval;

	public override void Load()
	{
		ModLog = this.Log;
		enabled = Config.Bind("General", "enabled", true, "Master switch for AI auto-eating.");
		eatBelowHp = Config.Bind("General", "eatBelowHp", 40f, "AI eats food when life is below this value.");
		checkInterval = Config.Bind("General", "checkInterval", 2f, "Seconds between AI food checks.");
		new Harmony("er2.aifood").PatchAll(typeof(Plugin).Assembly);
		ModLog.LogInfo((object)"ER2 AI Food 1.4.0 loaded.");
	}
}

[HarmonyPatch(typeof(BattleManager), "Update")]
public class AiFoodUpdatePatch
{
	private static float timer;

	private static void Postfix()
	{
		try
		{
			if (!Plugin.enabled.Value)
			{
				return;
			}
			timer += Time.deltaTime;
			if (timer >= Plugin.checkInterval.Value)
			{
				timer = 0f;
				AiFood.TryFeedAll();
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("AI food update error: " + ex.Message));
		}
	}
}

public static class AiFood
{
	private static readonly Dictionary<IntPtr, float> lastEat = new Dictionary<IntPtr, float>();

	internal static void TryFeedAll()
	{
		try
		{
			Il2CppSystem.Collections.Generic.List<Creature> list = Creature.aliveCreatures;
			if (list == null)
			{
				return;
			}
			for (int i = 0; i < list.Count; i++)
			{
				Creature c = list[i];
				if (c == null || c.IsDead)
				{
					continue;
				}
				Soldier s = c.TryCast<Soldier>();
				if (s == null)
				{
					continue;
				}
				bool isPlayer = false;
				try
				{
					isPlayer = s.IsPlayer();
				}
				catch
				{
				}
				if (isPlayer)
				{
					continue;
				}
				int hp = 0;
				try
				{
					hp = Math.Max(0, s.life_total.Value);
				}
				catch
				{
				}
				if (hp >= (int)Plugin.eatBelowHp.Value)
				{
					continue;
				}
				IntPtr key = s.Pointer;
				if (lastEat.TryGetValue(key, out float last) && Time.time - last < 10f)
				{
					continue;
				}
				InventoryManager inv = null;
				try
				{
					inv = s.inventory;
				}
				catch (Exception ex)
				{
					Plugin.ModLog.LogError((object)("AI food inventory access error: " + ex.Message));
				}
				if (inv == null)
				{
					continue;
				}
				VirtualRecoverLife food = FindInInventory(inv);
				if (food == null)
				{
					continue;
				}
				lastEat[key] = Time.time;
				s.RecoverLife(food);
				Plugin.ModLog.LogInfo((object)("AI ate food: hp=" + hp + " ptr=" + key));
			}
			foreach (IntPtr k in new List<IntPtr>(lastEat.Keys))
			{
				if (Time.time - lastEat[k] > 60f)
				{
					lastEat.Remove(k);
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("AI food error: " + ex.Message));
		}
	}

	private static VirtualRecoverLife FindInInventory(InventoryManager inv)
	{
		try
		{
			VirtualItem item = inv.FindItemOfType<VirtualRecoverLife>();
			if (item != null)
			{
				VirtualRecoverLife food = item.TryCast<VirtualRecoverLife>();
				if (food != null)
				{
					return food;
				}
			}
			Il2CppArrayBase<VirtualRecoverLife> arr = inv.GetItemsOfType<VirtualRecoverLife>();
			if (arr != null && arr.Count > 0)
			{
				return arr[0];
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("FindInInventory error: " + ex.Message));
		}
		return null;
	}
}
