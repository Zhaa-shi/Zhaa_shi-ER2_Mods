using System;

namespace ER2ThrowableWheel;

internal static class WheelLogic
{
	/// <summary>
	/// Creates the proper VirtualItem subclass for a throwable id.
	/// </summary>
	public static VirtualItem MakeVirtualItem(string id)
	{
		string text = id.ToLowerInvariant();
		if (!text.StartsWith("smokegrenade_", StringComparison.Ordinal) &&
			!text.Contains("mle1916_smoke") && !text.Contains("nhg42"))
		{
			if (!text.StartsWith("at_", StringComparison.Ordinal) &&
				!text.Contains("_at", StringComparison.Ordinal) &&
				!text.StartsWith("tankmine", StringComparison.Ordinal) &&
				!text.Contains("satchel") && !text.Contains("geballte") &&
				!text.Contains("_hhl") && !text.Contains("39a_flamegrenade") &&
				!text.Contains("_rpg"))
			{
				return new VirtualGrenade(id);
			}
			// VirtualATGrenade 的第二个参数 isSapperGrenade：爆破/工兵炸药（satchel/geballte）为 true，其余反坦克榴弹/地雷为 false。
			bool isSapper = text.Contains("satchel") || text.Contains("geballte");
			return new VirtualATGrenade(id, isSapper);
		}
		return new VirtualSmokeGrenade(id);
	}

	/// <summary>
	/// Makes sure every configured throwable exists in the controlled player's inventory.
	///
	/// Items are injected directly into the inventory item list as proper VirtualThrowable
	/// subclasses. The game's own add paths (AddVirtualItem / AddItemToInventory) normalize
	/// the item back to a plain base VirtualItem, which the native grenade wheel
	/// (type-filtered by VirtualThrowable) then ignores. Direct injection keeps the proper
	/// subclass, so the wheel shows the item and the native selection callback can throw it
	/// (native Throw works with inventory instances, as vanilla items prove).
	///
	/// With ReplaceWheelContent=true, vanilla throwables are removed first.
	/// </summary>
	public static void RefillPlayerInventory()
	{
		var instance = ThrowableWheelPlugin.Instance;
		if (instance == null || instance.ItemIds.Length == 0)
			return;

		var pc = PlayerController.currentController;
		if (pc == null)
			return;
		var soldier = pc.ControlledCharacter;
		if (soldier == null || soldier.inventory == null)
			return;
		var inv = soldier.inventory.inventory;
		if (inv == null)
			return;

		if (instance.ReplaceWheelContent.Value)
		{
			try
			{
				// Remove vanilla throwables before adding the configured list.
				var items = inv.items;
				if (items != null)
				{
					for (int i = items.Count - 1; i >= 0; i--)
					{
						var it = items[i];
						if (it == null)
							continue;
						bool isVanilla = !IsConfigured(it.item_id);
						if (isVanilla && it.TryCast<VirtualThrowable>() != null)
							items.RemoveAt(i);
					}
					ThrowableWheelPlugin.Logger.LogInfo("Removed vanilla throwables from player inventory (ReplaceWheelContent).");
				}
			}
			catch (Exception ex)
			{
				ThrowableWheelPlugin.Logger.LogWarning($"Remove vanilla throwables failed: {ex.Message}");
			}
		}

		foreach (string id in instance.ItemIds)
		{
			try
			{
				if (CountInInventory(inv, id) > 0)
					continue;
				var vi = MakeVirtualItem(id);
				if (vi == null)
					continue;
				inv.items.Add(vi);
				ThrowableWheelPlugin.Logger.LogInfo($"Refilled player inventory: {id} (injected as {vi.GetType().FullName})");
			}
			catch (Exception ex)
			{
				ThrowableWheelPlugin.Logger.LogWarning($"Refill failed for id=\"{id}\": {ex.Message}");
			}
		}
	}

	private static bool IsConfigured(string id)
	{
		var instance = ThrowableWheelPlugin.Instance;
		if (instance == null || id == null)
			return false;
		foreach (string configured in instance.ItemIds)
		{
			if (string.Equals(configured, id, StringComparison.OrdinalIgnoreCase))
				return true;
		}
		return false;
	}

	private static int CountInInventory(Inventory inv, string id)
	{
		var items = inv.items;
		if (items == null)
			return 0;
		int count = 0;
		for (int i = 0; i < items.Count; i++)
		{
			var it = items[i];
			if (it != null && string.Equals(it.item_id, id, StringComparison.OrdinalIgnoreCase))
				count++;
		}
		return count;
	}
}
