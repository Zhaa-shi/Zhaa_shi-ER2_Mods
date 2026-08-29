using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Photon.Pun;
using UnityEngine;

namespace ER2InventoryPause;

// ─────────────────────────────────────────────────────────────
//  ER2 Inventory Pause（背包暂停）
//  打开背包（自己的背包或尸体背包）时游戏世界暂停，关闭背包恢复。
//  原理：轮询 InventoryPanel.isOpen（覆盖自己背包与尸体"周围"背包），
//  isOpen 期间持续把 Time.timeScale 置 0（世界冻结、UI 正常、不弹原生菜单），
//  关闭背包恢复进入前的 timeScale；原生 Esc 暂停菜单打开时不干预。
// ─────────────────────────────────────────────────────────────

[BepInPlugin("er2.inventorypause", "ER2 Inventory Pause", "1.0.5")]
[BepInProcess("Easy Red 2.exe")]
public class Plugin : BasePlugin
{
#if CN_BUILD
	internal const bool DefaultChinese = true;
#else
	internal const bool DefaultChinese = false;
#endif

	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> Enabled;
	internal static ConfigEntry<bool> ApplyInMultiplayer;
	internal static ConfigEntry<int> PauseMode;
	internal static ConfigEntry<float> PauseDelay;

	internal static string T(string cn, string en) => DefaultChinese ? cn : en;

	public override void Load()
	{
		ModLog = Log;

		Enabled = Config.Bind("General", "Enabled", true, T(
			"总开关：关闭后打开背包不再暂停游戏。",
			"Master switch. When off, opening the inventory no longer pauses the game."));
		ApplyInMultiplayer = Config.Bind("General", "ApplyInMultiplayer", false, T(
			"联机对局是否也暂停（默认仅单机；联机暂停不同步，可能造成不公平）。",
			"Whether pausing also applies in multiplayer matches (default: singleplayer only; pausing is local-only and may be unfair)."));
		PauseMode = Config.Bind("General", "PauseMode", 4, new ConfigDescription(T(
			"暂停方式：4 = 延迟 timeScale 冻结（推荐：等背包打开动画完成后再冻结世界；真暂停——子弹/手雷/爆炸全停，无需无敌，丢弃的道具自动落地）；5 = AI 冻结+无敌（实测 AI 冻结无效且无敌可被滥用，弃用）；0-3 = 早期方案（实测有缺陷，弃用）。",
			"Pause method: 4 = delayed timeScale freeze (recommended: waits for the inventory open animation, then freezes the world; a real pause - bullets/grenades/explosions all stop, no invincibility needed, dropped items auto-land); 5 = AI freeze + invincible (tested: AI freeze ineffective and invincibility abusable, deprecated); 0-3 = early methods (tested flawed, deprecated)."),
			new AcceptableValueRange<int>(0, 5)));
		PauseDelay = Config.Bind("General", "PauseDelay", 0.5f, new ConfigDescription(T(
			"模式 4 的延迟秒数：等待背包打开动画完成后再冻结世界（0.5s 足够；越小暴露窗口越短，但太短会卡动画）。",
			"Delay in seconds for mode 4: waits for the inventory open animation before freezing (0.5s is enough; smaller = shorter exposure, too small stalls the animation)."),
			new AcceptableValueRange<float>(0.1f, 2f)));

		new Harmony("er2.inventorypause").PatchAll(GetType().Assembly);
		ModLog.LogInfo("ER2 Inventory Pause 1.0.5 loaded.");
	}

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
}

/// <summary>轮询背包状态（10Hz）：打开 → 暂停世界；关闭 → 恢复。
/// 不用入口 patch（协程/多路径打开不可靠——实测经验）。
/// 机制（实测定案）：原生暂停 = SetPause（timeScale=0 + 菜单 + disableOnPause → 死锁）；
/// isPaused 标志不冻结世界且禁用输入；timeScale=0 会卡背包打开动画 + 丢弃的武器浮空（物理冻结）。
/// → 模式 5（默认）：AI 冻结（enableAiBehaviour(false)，士兵全停）+ 玩家无敌（伤害短路），
///   timeScale 保持 1：玩家操作/物理正常（丢枪落地），战斗完全冻结，打开瞬间立即生效。</summary>
[HarmonyPatch(typeof(PlayerController), "Update")]
internal static class InventoryPausePatch
{
	private static float _savedScale = 1f;
	private static float _lastCheck;
	private static bool _lastOpen;
	private static bool _pausedByUs;
	private static float _openSince;

	/// <summary>本 mod 冻结了 AI 的士兵 InstanceID（关闭背包时恢复）。</summary>
	private static readonly HashSet<int> _frozenAi = new HashSet<int>();

	/// <summary>玩家无敌标志（背包打开期间，伤害路径短路用）。</summary>
	internal static bool PlayerInvincible;

	/// <summary>获取士兵 AI 行为开关（官方 Lua 通道，HVT 实证可逆）。</summary>
	internal static AiParams GetAiParams(Soldier s)
	{
		try
		{
			return new Lua_Soldier(s).getAiParams();
		}
		catch
		{
			return null;
		}
	}

	internal static bool IsPlayerUnit(Soldier s)
	{
		try
		{
			if (s == null)
			{
				return false;
			}
			PlayerController pc = PlayerController.currentController;
			if (pc == null || pc.ControlledCharacter == null)
			{
				return false;
			}
			Soldier me = pc.ControlledCharacter.TryCast<Soldier>();
			return me != null && me == s;
		}
		catch
		{
			return false;
		}
	}

	private static void Postfix()
	{
		try
		{
			if (!Plugin.IsActive())
			{
				return;
			}
			float now = Time.unscaledTime;
			if (now < _lastCheck + 0.1f)
			{
				return; // 10Hz
			}
			_lastCheck = now;

			bool open = false;
			try
			{
				open = InventoryPanel.isOpen;
			}
			catch
			{
				return;
			}
			// 防抖：不与原生暂停命令同帧打架
			float sinceChange = 99f;
			try
			{
				sinceChange = now - Pause.LastPauseStatusChange();
			}
			catch
			{
			}
			bool edgeOpen = open && !_lastOpen;
			bool edgeClose = !open && _lastOpen;
			_lastOpen = open;
			if (edgeOpen)
			{
				_openSince = now;
			}

			int mode = Plugin.PauseMode != null ? Plugin.PauseMode.Value : 4;

			if (open)
			{
				if (_pausedByUs)
				{
					// 已暂停：mode 4 期间把丢弃的道具落到地面（timeScale=0 物理冻结会浮空——修复）
					if (mode == 4)
					{
						SettleDroppedItems();
					}
					return;
				}
				bool nativePaused = false;
				try
				{
					nativePaused = Pause.isPaused;
				}
				catch
				{
				}
				if (nativePaused)
				{
					return; // 原生菜单打开中，不干预
				}
				if (mode == 5)
				{
					if (sinceChange > 0.2f)
					{
						FreezeAllAi();
						PlayerInvincible = true;
						_pausedByUs = true;
						Plugin.ModLog.LogInfo($"[InvPause] paused (AI freeze + invincible, frozen={_frozenAi.Count})");
					}
					return;
				}
				if (mode == 4)
				{
					// 延迟冻结：等打开动画完成（协程 scaled 时间，立即冻结会卡动画）
					float delay = Plugin.PauseDelay != null ? Plugin.PauseDelay.Value : 0.5f;
					if (now - _openSince >= delay && sinceChange > 0.2f)
					{
						_savedScale = Time.timeScale;
						Time.timeScale = 0f;
						_pausedByUs = true;
						Plugin.ModLog.LogInfo($"[InvPause] paused (delayed ts freeze, saved={_savedScale:F2})");
					}
					return;
				}
				if (sinceChange > 0.2f)
				{
					ApplyPause(mode);
					_pausedByUs = true;
					Plugin.ModLog.LogInfo($"[InvPause] paused (mode={mode})");
				}
			}
			else if (edgeClose && _pausedByUs && sinceChange > 0.2f)
			{
				if (mode == 5)
				{
					RestoreAllAi();
					PlayerInvincible = false;
					_pausedByUs = false;
					Plugin.ModLog.LogInfo("[InvPause] resumed");
				}
				else if (mode == 4)
				{
					Time.timeScale = _savedScale;
					_savedScale = 1f;
					_pausedByUs = false;
					Plugin.ModLog.LogInfo("[InvPause] resumed");
				}
				else
				{
					ApplyResume(mode);
					_pausedByUs = false;
					Plugin.ModLog.LogInfo("[InvPause] resumed");
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>让暂停期间丢弃的道具落地：timeScale=0 时物理（FixedUpdate）冻结，丢弃的武器/物品
	/// 会悬在半空——轮询扫描 ItemObject.spawnedItems（游戏自维护列表），明显离地（>0.8m）的
	/// 射线向下找地面拉下。恢复 timeScale 后道具已在地面，正常静止。</summary>
	private static void SettleDroppedItems()
	{
		try
		{
			Il2CppSystem.Collections.Generic.List<ItemObject> items = ItemObject.spawnedItems;
			if (items == null)
			{
				return;
			}
			for (int i = 0; i < items.Count; i++)
			{
				try
				{
					ItemObject io = items[i];
					if (io == null || io.transform == null)
					{
						continue;
					}
					Transform t = io.transform;
					if (Physics.Raycast(t.position, Vector3.down, out RaycastHit hit, 10f))
					{
						float dy = t.position.y - hit.point.y;
						if (dy > 0.8f)
						{
							Vector3 p = t.position;
							p.y = hit.point.y + 0.05f;
							t.position = p;
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

	/// <summary>冻结所有非玩家士兵的 AI（官方行为开关）。</summary>
	private static void FreezeAllAi()
	{
		try
		{
			_frozenAi.Clear();
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
					if (s == null || IsPlayerUnit(s))
					{
						continue;
					}
					AiParams ap = GetAiParams(s);
					if (ap != null)
					{
						ap.enableAiBehaviour(false);
						_frozenAi.Add(s.GetInstanceID());
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

	/// <summary>恢复所有被冻结士兵的 AI（已死的跳过）。</summary>
	private static void RestoreAllAi()
	{
		try
		{
			Il2CppSystem.Collections.Generic.List<Creature> all = Creature.aliveCreatures;
			if (all != null)
			{
				foreach (Creature c in all)
				{
					try
					{
						if (c == null)
						{
							continue;
						}
						Soldier s = c.TryCast<Soldier>();
						if (s == null)
						{
							continue;
						}
						if (_frozenAi.Contains(s.GetInstanceID()))
						{
							AiParams ap = GetAiParams(s);
							if (ap != null)
							{
								ap.enableAiBehaviour(true);
							}
						}
					}
					catch
					{
					}
				}
			}
			_frozenAi.Clear();
		}
		catch
		{
		}
	}

	private static void ApplyPause(int mode)
	{
		try
		{
			switch (mode)
			{
				case 0:
					_savedScale = Time.timeScale;
					Time.timeScale = 0f;
					break;
				case 1:
				case 3:
					{
						float tsBefore = Time.timeScale;
						Pause.SetPause(true);
						// 隐藏原生暂停菜单（避免挡住背包）；isPaused 保持 true → 世界冻结
						try
						{
							Pause p = Pause.instance;
							if (p != null && p.pauseMenu != null && p.pauseMenu.activeSelf)
							{
								p.pauseMenu.SetActive(false);
							}
						}
						catch
						{
						}
						float tsAfter = Time.timeScale;
						if (mode == 3 && tsAfter == 0f)
						{
							Time.timeScale = tsBefore > 0f ? tsBefore : 1f; // 恢复时间（若 SetPause 冻结了）
						}
					}
					break;
				default:
					// 手动设标志（弃用：输入禁用但世界不冻结——用户实测）
					Pause.isPaused = true;
					break;
			}
		}
		catch
		{
		}
	}

	private static void ApplyResume(int mode)
	{
		try
		{
			switch (mode)
			{
				case 0:
					Time.timeScale = _savedScale;
					_savedScale = 1f;
					break;
				case 1:
				case 3:
					Pause.SetPause(false);
					break;
				default:
					Pause.isPaused = false;
					break;
			}
		}
		catch
		{
		}
	}
}

// ═════════════════════════════════════════════════════════════
//  玩家无敌（背包打开期间）：同 CombatTweaks FF 结构，受害者为玩家且无敌标志开 → 短路
// ═════════════════════════════════════════════════════════════

[HarmonyPatch(typeof(BodyPart), "HitPart")]
[HarmonyPriority(Priority.First)]
internal static class InvincibleHitPatch
{
	private static bool Prefix(BodyPart __instance, ref bool __result)
	{
		try
		{
			if (!InventoryPausePatch.PlayerInvincible || __instance == null)
			{
				return true;
			}
			Creature unit = __instance.GetUnit();
			Soldier s = unit != null ? unit.TryCast<Soldier>() : null;
			if (s != null && InventoryPausePatch.IsPlayerUnit(s))
			{
				__result = false;
				return false; // 玩家无敌：子弹/近战/碰撞伤害短路
			}
		}
		catch
		{
		}
		return true;
	}
}

[HarmonyPatch(typeof(BodyPart), "TryDamageWithExplosion")]
[HarmonyPriority(Priority.First)]
internal static class InvincibleExplosionPatch
{
	private static bool Prefix(BodyPart __instance)
	{
		try
		{
			if (!InventoryPausePatch.PlayerInvincible || __instance == null)
			{
				return true;
			}
			Creature unit = __instance.GetUnit();
			Soldier s = unit != null ? unit.TryCast<Soldier>() : null;
			if (s != null && InventoryPausePatch.IsPlayerUnit(s))
			{
				return false; // 爆炸伤害短路
			}
		}
		catch
		{
		}
		return true;
	}
}

[HarmonyPatch(typeof(BodyPart), "AllowDamage")]
[HarmonyPriority(Priority.First)]
internal static class InvincibleAllowPatch
{
	private static bool Prefix(ref bool __result, string toFaction)
	{
		try
		{
			if (!InventoryPausePatch.PlayerInvincible || string.IsNullOrEmpty(toFaction))
			{
				return true;
			}
			// toFaction 只有字符串，无法确认受害者是玩家；交给 HitPart/爆炸短路兜底，
			// 这里保守不拦截（避免误伤敌人伤害判定）。
			return true;
		}
		catch
		{
		}
		return true;
	}
}
