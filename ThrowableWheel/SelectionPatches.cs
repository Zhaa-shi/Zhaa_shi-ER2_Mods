using System;
using CircularMenuSelection;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ER2ThrowableWheel;

/// <summary>
/// Periodic refill driver (2s throttle). The native grenade-wheel coroutine snapshots the
/// player inventory when it starts and that coroutine start is not reliably hookable.
/// </summary>
[HarmonyPatch(typeof(PlayerController), "Update")]
internal static class RefillTickPatch
{
	private static float _next;

	private static void Postfix()
	{
		try
		{
			if (Time.time < _next)
				return;
			_next = Time.time + 2f;

			var instance = ThrowableWheelPlugin.Instance;
			if (instance == null || !instance.Enabled.Value || !instance.RefillBeforeWheelOpens.Value)
				return;
			if (ThrowableWheelPlugin.IsOnlineMultiplayer() && !instance.AllowInMultiplayer.Value)
				return;

			instance.RebuildItemIds(); // hot-reload config changes (preset / custom list)
			WheelLogic.RefillPlayerInventory();
		}
		catch (Exception ex)
		{
			ThrowableWheelPlugin.Logger.LogWarning($"Refill tick failed: {ex.Message}");
		}
	}
}

/// <summary>
/// Refills the inventory when the grenade wheel opens. The refill is too late for the
/// CURRENT wheel data (the coroutine already snapshotted the inventory), but the NEXT
/// wheel open picks up the items. Combined with the periodic tick this guarantees the
/// items are present.
/// </summary>
[HarmonyPatch(typeof(CircularMenu2), "ShowCircle")]
internal static class WheelOpenRefillPatch
{
	private static void Postfix(Il2CppReferenceArray<CircularMenuData> data, GameInput keyCodeToRelease)
	{
		try
		{
			string inputName = keyCodeToRelease.ToString();
			if (!inputName.Contains("Grenade", StringComparison.OrdinalIgnoreCase) &&
				!inputName.Contains("Granade", StringComparison.OrdinalIgnoreCase))
				return;

			var instance = ThrowableWheelPlugin.Instance;
			if (instance == null || !instance.Enabled.Value || !instance.RefillBeforeWheelOpens.Value)
				return;
			if (ThrowableWheelPlugin.IsOnlineMultiplayer() && !instance.AllowInMultiplayer.Value)
				return;

			instance.RebuildItemIds(); // hot-reload config changes (preset / custom list)
			WheelLogic.RefillPlayerInventory();
		}
		catch (Exception ex)
		{
			ThrowableWheelPlugin.Logger.LogWarning($"WheelOpen handler failed: {ex.Message}");
		}
	}
}
