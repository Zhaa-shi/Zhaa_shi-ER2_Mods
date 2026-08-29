using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace ER2LimbTweaks;

[BepInPlugin("er2.limbtweaks", "ER2 Limb Tweaks", "2.13.100")]
public class Plugin : BasePlugin
{
	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> enabled;
	internal static ConfigEntry<bool> corpseEnabled;
	internal static ConfigEntry<bool> aliveEnabled;
	internal static ConfigEntry<bool> bleedEnabled;
	internal static ConfigEntry<bool> affectsPlayer;
	internal static ConfigEntry<float> damageNeeded;
	internal static ConfigEntry<float> corpseDamageThreshold;

	internal static string notifyMessage = "";

	internal static float notifyUntil;

	private static string lastNotifyText = "";

	private static float lastNotifyTime = -10f;

	internal static void Notify(string message)
	{
		// 同消息节流：2.5 秒内重复的同一条提示不再次入队（防狂按键刷爆原生 Hint 队列）
		if (message == lastNotifyText && Time.time - lastNotifyTime < 2.5f)
		{
			return;
		}
		lastNotifyText = message;
		lastNotifyTime = Time.time;
		notifyMessage = message;
		notifyUntil = Time.time + 3.5f;
	}

	// 联动：ER2 No Interaction Hints v2（F5 隐藏 HUD）时隐藏本 mod 的屏幕提示
	// （每 mod 独立开关：ModManager → No Interaction Hints → Hide Other Mods UI）
	internal static bool IsInteractionHudHidden()
	{
		return ER2Shared.NoHintsHudLink.IsHidden("er2.limbtweaks", "ER2 Limb Tweaks");
	}

	public override void Load()
	{
		ModLog = this.Log;
		enabled = Config.Bind("General", "enabled", true, "Master switch for the limb tweaks.");
		corpseEnabled = Config.Bind("Corpse Shooting", "corpseEnabled", true, "Shooting a dead body detaches the limb near the bullet hit.");
		aliveEnabled = Config.Bind("Living Soldiers", "aliveEnabled", true, "High damage on a limb can sever it while the soldier is still alive.");
		bleedEnabled = Config.Bind("Bleeding", "bleedEnabled", true, "Severed limbs cause continuous blood loss until death. Disable to keep severing without the bleed-out.");
		affectsPlayer = Config.Bind("Bleeding", "affectsPlayer", true, "Apply severing and bleeding to the player-controlled soldier. Disable to make the player immune (AI still affected).");
		damageNeeded = Config.Bind("Limbs", "damageNeeded", 60f, "Accumulated damage on the same limb required to sever it (alive).");
		corpseDamageThreshold = Config.Bind("Limbs", "corpseDamageThreshold", 80f, "Accumulated sever score (damage + muzzle velocity x0.08) on the same corpse limb required to sever it.");
		new Harmony("er2.limbtweaks").PatchAll(Assembly.GetExecutingAssembly());
		ModLog.LogInfo((object)"ER2 Limb Tweaks 2.13.100 loaded.");
	}
}

[HarmonyPatch(typeof(BodyPart), "HitPart")]
public class HitPartPatch{
	private static bool Prefix(BodyPart __instance, float damage)
	{
		try
		{
			if (!Plugin.enabled.Value || __instance == null)
			{
				return true;
			}
			Soldier soldier = __instance.unit as Soldier;
			if (soldier == null)
			{
				return true;
			}
			if (!soldier.NotDeadAndSurrendered())
			{
				return true;
			}
			Dismember.RecordHit(soldier, __instance);
			if (Plugin.aliveEnabled.Value && damage > 0f)
			{
				if (Dismember.TryAliveHit(soldier, __instance, damage))
				{
					Plugin.ModLog.LogInfo((object)"Limb severed, damage intercepted.");
					return false;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("HitPart record error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Corvostudio.Weapons.BulletInstance), "OnHit")]
public class BulletOnHitPatch
{
	/// <summary>命中尸体时先静音尸体的音频源（原生音效在其后播放，此时已禁用 → 打尸体无死亡音效）。</summary>
	private static float lastDiagTime;

	private static void Prefix(RaycastHit hit)
	{
		try
		{
			if (hit.collider == null)
			{
				return;
			}
			BodyPart bp = hit.collider.GetComponentInParent<BodyPart>(true);
			// 诊断：尸体命中（限频 2s）——定位"第二条腿断不了"（collider/bp 状态）
			if (bp != null && bp.unit != null && bp.unit.IsDead && Time.time - lastDiagTime > 2f)
			{
				lastDiagTime = Time.time;
				Plugin.ModLog.LogInfo((object)("Corpse hit diag: collider='" + hit.collider.name + "', bodyPart=" + ((bp != null) ? bp.bodyPart.ToString() : "null")));
			}
			if (bp == null || bp.unit == null || !bp.unit.IsDead)
			{
				return;
			}
			AudioSource[] srcs = bp.unit.GetComponentsInChildren<AudioSource>(true);
			if (srcs != null)
			{
				foreach (AudioSource s in srcs)
				{
					try
					{
						if (s != null && s.enabled)
						{
							s.enabled = false;
						}
					}
					catch
					{
					}
				}
			}
		}
		catch
		{
		}
	}

	private static void Postfix(Corvostudio.Weapons.BulletInstance __instance, RaycastHit hit)
	{
		try
		{
			if (!Plugin.corpseEnabled.Value || hit.collider == null)
			{
				return;
			}
			BodyPart bp = hit.collider.GetComponentInParent<BodyPart>(true);
			if (bp == null)
			{
				return;
			}
			Soldier soldier = bp.unit as Soldier;
			if (soldier == null)
			{
				return;
			}
			if (soldier.NotDeadAndSurrendered())
			{
				return;
			}
			float dmg = 0f;
			try
			{
				if (__instance != null && __instance.bulletData != null)
				{
					dmg = __instance.bulletData.penetrationDamage + __instance.bulletData.explosionDamage + __instance.bulletData.ActualSpeed * 0.08f;
				}
			}
			catch
			{
			}
			Dismember.TryCorpseHit(soldier, bp, dmg);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Corpse OnHit error: " + ex.Message));
		}
	}
}

[HarmonyPatch(typeof(MatchData), "StartBattleLoading")]
public class BattleStartResetPatch
{
	private static void Postfix()
	{
		Dismember.ResetAll();
	}
}

[HarmonyPatch(typeof(RagdollManager), "Ragdolize", new Type[] { typeof(Vector3) })]
public class RagdolizeWithDirectionPatch
{
	private static void Postfix(RagdollManager __instance)
	{
		Dismember.TryDismember(__instance);
	}
}

[HarmonyPatch(typeof(RagdollManager), "Ragdolize", new Type[0])]
public class RagdolizePlainPatch
{
	private static void Postfix(RagdollManager __instance)
	{
		Dismember.TryDismember(__instance);
	}
}

[HarmonyPatch(typeof(Soldier), "SetBleeding")]
public class BandageBlockPatch
{
	private static bool Prefix(Soldier __instance, bool bleeding)
	{
		try
		{
			if (!bleeding)
			{
				if (Dismember.IsBleedingUnit(__instance))
				{
					// 断肢流血单位禁止止血：保持流血状态，阻止游戏原生回血启动
					return false;
				}
				if (Dismember.IsPlayerControlled(__instance) && Dismember.IsHandBroken(__instance))
				{
					Plugin.Notify("Cannot use bandage - arm severed.");
					Plugin.ModLog.LogInfo((object)"Player-controlled soldier with severed arm cannot use bandage.");
					return false;
				}
				if (!Dismember.IsPlayerControlled(__instance) && Dismember.IsRightArmBroken(__instance))
				{
					Plugin.ModLog.LogInfo((object)"AI with severed right arm cannot use bandage.");
					return false;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Bandage patch error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Soldier), "UseBandages")]
public class UseBandagesBlockPatch
{
	private static void Postfix(Soldier __instance)
	{
		try
		{
			if (Dismember.IsPlayerControlled(__instance) && Dismember.IsHandBroken(__instance))
			{
				return;
			}
			if (!Dismember.IsPlayerControlled(__instance) && Dismember.IsRightArmBroken(__instance))
			{
				return;
			}
			Dismember.AddBleedTime(__instance);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("UseBandages Postfix error: " + ex.Message));
		}
	}

	private static bool Prefix(Soldier __instance)
	{
		try
		{
			if (Dismember.IsPlayerControlled(__instance) && Dismember.IsHandBroken(__instance))
			{
				Plugin.Notify("Cannot use bandage - arm severed.");
				Plugin.ModLog.LogInfo((object)"Player-controlled soldier with severed arm cannot use bandage.");
				return false;
			}
			if (!Dismember.IsPlayerControlled(__instance) && Dismember.IsRightArmBroken(__instance))
			{
				Plugin.ModLog.LogInfo((object)"AI with severed right arm cannot use bandage.");
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("UseBandages patch error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Soldier), "PickUpItem")]
public class PickUpBlockPatch
{
	private static bool Prefix(Soldier __instance, GameObject itemInstance)
	{
		try
		{
			bool isWeapon = itemInstance != null && itemInstance.GetComponent<GenericGun>() != null;
			if (Dismember.IsBothArmsBroken(__instance))
			{
				if (Dismember.IsPlayerControlled(__instance))
				{
					Plugin.Notify("Cannot pick up - both arms severed.");
				}
				Plugin.ModLog.LogInfo((object)"Both-arms soldier cannot pick up items.");
				return false;
			}
			if (isWeapon && Dismember.IsRightArmBroken(__instance))
			{
				if (Dismember.IsPlayerControlled(__instance))
				{
					Plugin.Notify("Cannot pick up weapons - right arm severed.");
				}
				Plugin.ModLog.LogInfo((object)"Right-arm soldier cannot pick up weapons.");
				return false;
			}
			if (isWeapon && Dismember.IsLeftArmBroken(__instance) && !Dismember.IsRightArmBroken(__instance))
			{
				string wid = null;
				if (itemInstance != null)
				{
					ItemObject io = itemInstance.GetComponent<ItemObject>();
					if (io != null)
					{
						wid = io.item_id;
					}
				}
				if (!Dismember.IsPistol(wid))
				{
					if (Dismember.IsPlayerControlled(__instance))
					{
						Plugin.Notify("Cannot pick up long weapons - left arm severed.");
					}
					Plugin.ModLog.LogInfo((object)"Left-arm soldier cannot pick up long weapons.");
					return false;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("PickUp patch error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Soldier), "SwitchTo")]
public class SwitchWeaponBlockPatch
{
	private static bool Prefix(Soldier __instance, int wearedItemIndex)
	{
		try
		{
			if (Dismember.IsRightArmBroken(__instance))
			{
				if (Dismember.IsPlayerControlled(__instance))
				{
					Plugin.Notify("Cannot equip weapons - right arm severed.");
				}
				Plugin.ModLog.LogInfo((object)"Right-arm soldier cannot equip/switch weapons.");
				return false;
			}
			if (Dismember.IsLeftArmBroken(__instance) && Dismember.IsPlayerControlled(__instance))
			{
				string wid = Dismember.GetWearedItemId(__instance, wearedItemIndex);
				if (!Dismember.IsPistol(wid))
				{
					if (Dismember.IsPlayerControlled(__instance))
					{
						Plugin.Notify("Cannot equip long weapons - left arm severed.");
					}
					Plugin.ModLog.LogInfo((object)"Left-arm soldier cannot equip long weapons.");
					return false;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("SwitchTo patch error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Soldier), "Reload")]
public class ReloadBlockPatch
{
	private static bool Prefix(Soldier __instance)
	{
		try
		{
			if (__instance == null)
			{
				return true;
			}
			if (Dismember.IsRightArmBroken(__instance))
			{
				Plugin.ModLog.LogInfo((object)"Right-arm soldier cannot reload.");
				return false;
			}
			if (Dismember.IsLeftArmBroken(__instance) && Dismember.IsPlayerControlled(__instance))
			{
				Plugin.Notify("Cannot reload - left arm severed.");
				Plugin.ModLog.LogInfo((object)"Left-arm soldier cannot reload.");
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Reload patch error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(GenericGun), "Reload")]
public class GenericGunReloadBlockPatch
{
	private static bool Prefix(Creature user)
	{
		try
		{
			Soldier s = user as Soldier;
			if (s == null)
			{
				return true;
			}
			if (Dismember.IsRightArmBroken(s))
			{
				Plugin.ModLog.LogInfo((object)"Right-arm soldier cannot reload (gun).");
				return false;
			}
			if (Dismember.IsLeftArmBroken(s) && Dismember.IsPlayerControlled(s))
			{
				Plugin.Notify("Cannot reload - left arm severed.");
				Plugin.ModLog.LogInfo((object)"Left-arm soldier cannot reload (gun).");
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("GenericGun Reload patch error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Soldier), "Throw")]
public class ThrowBlockPatch
{
	private static bool Prefix(Soldier __instance)
	{
		try
		{
			if (Dismember.IsRightArmBroken(__instance))
			{
				if (Dismember.IsPlayerControlled(__instance))
				{
					Plugin.Notify("Cannot throw - right arm severed.");
				}
				Plugin.ModLog.LogInfo((object)"Right-arm soldier cannot throw.");
				return false;
			}
			if (Dismember.IsLeftArmBroken(__instance) && Dismember.IsPlayerControlled(__instance))
			{
				Plugin.Notify("Cannot throw - arm severed.");
				Plugin.ModLog.LogInfo((object)"Left-arm soldier cannot throw.");
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Throw patch error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(Soldier), "UnSurrender")]
public class UnSurrenderBlockPatch
{
	private static void Postfix(Soldier __instance)
	{
		try
		{
			if (Dismember.IsRightArmBroken(__instance))
			{
				__instance.DropItemNow(0);
				Plugin.ModLog.LogInfo((object)"Right-arm soldier auto-equipped weapon dropped after surrender end.");
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("UnSurrender patch error: " + ex.Message));
		}
	}
}

[HarmonyPatch(typeof(Soldier), "UnSurrenderSynched")]
public class UnSurrenderSynchedBlockPatch
{
	private static void Postfix(Soldier __instance)
	{
		try
		{
			if (Dismember.IsRightArmBroken(__instance))
			{
				__instance.DropItemNow(0);
				Plugin.ModLog.LogInfo((object)"Right-arm soldier auto-equipped weapon dropped after surrender end (synched).");
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("UnSurrenderSynched patch error: " + ex.Message));
		}
	}
}

[HarmonyPatch(typeof(Soldier), "SetPose")]
public class ProneLockPatch
{
	private static bool Prefix(Soldier __instance, SoldierPose pose)
	{
		try
		{
			if (!Dismember.IsLegBroken(__instance) || pose == SoldierPose.Prone)
			{
				return true;
			}
			if (Dismember.IsPlayerControlled(__instance))
			{
				Plugin.Notify("You cannot stand or crouch - leg severed.");
			}
			Plugin.ModLog.LogInfo((object)"Leg-broken soldier cannot change stance (prone locked).");
			return false;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("SetPose patch error: " + ex.Message));
		}
		return true;
	}
}

[HarmonyPatch(typeof(PlayerController), "SetPlayer")]
public class PlayerControlRestorePatch
{
	private static Soldier prevUnit;

	private static void Prefix(Soldier unit)
	{
		try
		{
			PlayerController pc = PlayerController.currentController;
			prevUnit = (pc != null) ? pc.ControlledCharacter : null;
		}
		catch
		{
			prevUnit = null;
		}
	}

	private static void Postfix(Soldier unit)
	{
		try
		{
			if (unit != null && Dismember.IsHandBroken(unit))
			{
				Dismember.RestoreHide(unit);
				if (Dismember.IsLeftArmBroken(unit) && !Dismember.IsRightArmBroken(unit))
				{
					Dismember.QueuePistolSwitch(unit);
				}
			}
			if (prevUnit != null && Dismember.IsHandBroken(prevUnit))
			{
				Dismember.RestoreHide(prevUnit);
				Plugin.ModLog.LogInfo((object)"Re-hid severed arm on previous unit after control switch.");
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("SetPlayer restore error: " + ex.Message));
		}
	}
}

[HarmonyPatch(typeof(BattleManager), "Update")]
public class BleedTickPatch
{
	private static void Postfix()
	{
		Dismember.BleedTickAll();
	}
}

[HarmonyPatch(typeof(BattleManager), "Update")]
public class ReapplyPatch
{
	private static float timer;

	private static void Postfix()
	{
		try
		{
			timer += Time.deltaTime;
			if (timer < 0.08f)
			{
				return;
			}
			timer = 0f;
			PlayerController pc = PlayerController.currentController;
			if (pc == null)
			{
				return;
			}
			Soldier s = pc.ControlledCharacter;
			if (s != null && (Dismember.IsHandBroken(s) || Dismember.IsLegBroken(s)))
			{
				Dismember.RestoreHide(s);
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Reapply error: " + ex.Message));
		}
	}
}

[HarmonyPatch(typeof(Creature), "Damage")]
public class NativeDamageControlPatch
{
	private static bool Prefix(Creature __instance, float dam)
	{
		try
		{
			if (__instance == null)
			{
				return true;
			}
			bool bleeding = false;
			try
			{
				bleeding = __instance.isBleeding;
			}
			catch
			{
			}
			if (!bleeding || dam > 5f)
			{
				return true;
			}
			Dismember.RegisterNativeBleed(__instance);
			return false;
		}
		catch
		{
			return true;
		}
	}
}

[HarmonyPatch(typeof(Soldier), "SetBleeding")]
public class SetBleedingTrackPatch
{
	private static void Postfix(Soldier __instance, bool bleeding)
	{
		try
		{
			if (!bleeding)
			{
				Dismember.UnregisterNativeBleed(__instance);
			}
		}
		catch
		{
		}
	}
}

[HarmonyPatch(typeof(BodyPart), "GetBodyPartMultiplier", new Type[] { typeof(BodyPartType), typeof(HitType) })]
public class LimbMultiplierPatch
{
	private static void Postfix(BodyPartType bodyPart, ref float __result)
	{
		try
		{
			if (bodyPart == BodyPartType.arm_l || bodyPart == BodyPartType.arm_r || bodyPart == BodyPartType.leg_l || bodyPart == BodyPartType.leg_r)
			{
				__result *= 0.5f;
			}
		}
		catch
		{
		}
	}
}

[HarmonyPatch(typeof(BattleManager), "OnGUI")]
public class NotifyGuiPatch
{
	private static GUIStyle notifyStyle;

	private static bool notifyStyleFailed;

	private static void Postfix()
	{
		try
		{
			if (Plugin.notifyUntil <= 0f || Time.time > Plugin.notifyUntil)
			{
				return;
			}
			if (Plugin.IsInteractionHudHidden())
			{
				return;
			}
			string msg = Plugin.notifyMessage;
			if (string.IsNullOrEmpty(msg))
			{
				return;
			}
			// 优先走游戏原生提示弹窗（Corvostudio.UI.Hint）——观感与原生教程提示一致
			if (NativeUi.ShowNativeHint(msg, 3.5f))
			{
				Plugin.notifyUntil = 0f; // 已交给原生队列，清除本 mod 重绘标记，避免每帧重复入队
				return;
			}
			// 回退：原生字体 + 游戏自己的描边文字（GuiExtension.OutlinedLabel）
			if (notifyStyle == null && !notifyStyleFailed)
			{
				try
				{
					notifyStyle = NativeUi.MakeStyle(36, FontStyle.Bold, NativeUi.NativeTextColor(), TextAnchor.MiddleCenter, wordWrap: true);
				}
				catch
				{
					notifyStyleFailed = true;
				}
			}
			float w = Screen.width * 0.8f;
			float h = 80f;
			float x = (Screen.width - w) / 2f;
			float y = Screen.height * 0.28f;
			Color prev = GUI.color;
			if (notifyStyle == null)
			{
				GUI.color = Color.red;
				GUI.Label(new Rect(x, y, w, h), msg);
				GUI.color = prev;
				return;
			}
			NativeUi.OutlinedLabel(new Rect(x, y, w, h), msg, notifyStyle, 1);
			GUI.color = prev;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Notify GUI error: " + ex.Message));
		}
	}
}

public static class Dismember
{
	private const int StNormal = 0;

	private const int StIncapacitated = 1;

	private const int StSurrendered = 2;

	private static readonly Dictionary<IntPtr, HashSet<BodyPartType>> detached = new Dictionary<IntPtr, HashSet<BodyPartType>>();

	private static readonly Dictionary<IntPtr, int> states = new Dictionary<IntPtr, int>();

	private static readonly Dictionary<IntPtr, bool> droppedWeapon = new Dictionary<IntPtr, bool>();

	private static readonly Dictionary<IntPtr, BodyPart> lastHitBody = new Dictionary<IntPtr, BodyPart>();

	private static readonly Dictionary<IntPtr, Dictionary<BodyPartType, float>> corpseDamage = new Dictionary<IntPtr, Dictionary<BodyPartType, float>>();

	private static readonly Dictionary<IntPtr, Dictionary<BodyPartType, float>> aliveDamage = new Dictionary<IntPtr, Dictionary<BodyPartType, float>>();

	private static readonly HashSet<string> pistolIds = new HashSet<string>
	{
		"beretta", "colt1911", "luger", "nambu", "tt33", "walter_p38",
		"c96", "c96stock", "c96_712", "c96stock_712", "ruby",
		"smithandwesson", "smithandwesson_short",
		"enfield_no2_mk1", "enfield_no2_mk1s"
	};

	internal static void ResetAll()
	{
		detached.Clear();
		hiddenLegBones.Clear();
		states.Clear();
		droppedWeapon.Clear();
		lastHitBody.Clear();
		corpseDamage.Clear();
		aliveDamage.Clear();
		bleeders.Clear();
		lastBleedAdd.Clear();
		bleedRate.Clear();
		bleedDmgAccum.Clear();
		surrenderTimers.Clear();
		nativeBleeders.Clear();
		Plugin.ModLog.LogInfo((object)"Limb Tweaks state reset (new battle).");
	}

	internal static bool IsPistol(string itemId)
	{
		return itemId != null && pistolIds.Contains(itemId);
	}

	internal static bool IsDownedLimbBroken(Soldier soldier)
	{
		if (soldier == null)
		{
			return false;
		}
		IntPtr key = soldier.Pointer;
		return states.TryGetValue(key, out int st) && st == StIncapacitated && IsLegBroken(soldier);
	}

	private static readonly Dictionary<IntPtr, Soldier> bleeders = new Dictionary<IntPtr, Soldier>();

	internal static void StartLimbBleed(Soldier soldier, IntPtr key, BodyPartType part)
	{
		// 切换机制：流血关闭（断肢仍生效但不再失血）或玩家免疫时跳过流血
		if (!Plugin.bleedEnabled.Value)
		{
			Plugin.ModLog.LogInfo((object)"Limb bleed skipped - bleed system disabled.");
			return;
		}
		if (!Plugin.affectsPlayer.Value && IsPlayerControlled(soldier))
		{
			Plugin.ModLog.LogInfo((object)"Limb bleed skipped - player is immune (affectsPlayer=false).");
			return;
		}
		bleeders[key] = soldier;
		bleedRate[key] = 1f;
		bleedDmgAccum.Remove(key);
		try
		{
			soldier.isBleeding = true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("StartLimbBleed set isBleeding error: " + ex.Message));
		}
		string limbName = (part == BodyPartType.arm_l || part == BodyPartType.arm_r) ? "arm" : "leg";
		if (IsPlayerControlled(soldier))
		{
			Plugin.Notify("You are bleeding out from a severed " + limbName + "!");
		}
		Plugin.ModLog.LogInfo((object)("Limb bleed started - hp=" + Math.Max(0, soldier.life_total.Value) + "."));
	}

	internal static void AddBleedTime(Soldier soldier)
	{
		if (soldier == null)
		{
			return;
		}
		IntPtr key = soldier.Pointer;
		if (!bleeders.ContainsKey(key))
		{
			return;
		}
		if (lastBleedAdd.TryGetValue(key, out float last) && Time.time - last < 2f)
		{
			return;
		}
		lastBleedAdd[key] = Time.time;
		float rate = bleedRate.TryGetValue(key, out float rv) ? rv : 1f;
		rate *= 0.5f;
		if (rate < 0.25f)
		{
			// 第 3+ 个绷带：完全无效
			if (IsPlayerControlled(soldier))
			{
				Plugin.Notify("Bandage has no more effect.");
			}
			Plugin.ModLog.LogInfo((object)"Bandage ignored - bleed already at minimum (1/s).");
			return;
		}
		if (rate <= 0.25f)
		{
			// 第 2 个绷带：流血降到最低 1/s，此后绷带无效
			// 保持 isBleeding=true 阻止游戏自然回血
			bleedRate[key] = 0.25f;
			try
			{
				soldier.isBleeding = true;
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("FloorBleed set isBleeding error: " + ex.Message));
			}
			if (IsPlayerControlled(soldier))
			{
				Plugin.Notify("Bleeding slowed to a trickle - bandages no longer help.");
			}
			Plugin.ModLog.LogInfo((object)"Bleed rate floored at 1/s (2nd bandage).");
			return;
		}
		bleedRate[key] = rate;
		try
		{
			soldier.isBleeding = true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("AddBleedTime restart bleeding error: " + ex.Message));
		}
		if (IsPlayerControlled(soldier))
		{
			Plugin.Notify("Bandage applied - bleeding slowed to 4/s.");
		}
		Plugin.ModLog.LogInfo((object)"Bandage used - bleed rate 4/s.");
	}

	private static readonly Dictionary<IntPtr, float> lastBleedAdd = new Dictionary<IntPtr, float>();

	private static readonly Dictionary<IntPtr, float> bleedRate = new Dictionary<IntPtr, float>();

	private static readonly Dictionary<IntPtr, float> bleedDmgAccum = new Dictionary<IntPtr, float>();

	private static readonly Dictionary<IntPtr, float> surrenderTimers = new Dictionary<IntPtr, float>();

	private static readonly Dictionary<IntPtr, Creature> nativeBleeders = new Dictionary<IntPtr, Creature>();

	internal static void BleedTickAll()
	{
		try
		{
			foreach (KeyValuePair<IntPtr, Soldier> kv in new List<KeyValuePair<IntPtr, Soldier>>(bleeders))
			{
				try
				{
					Soldier s = kv.Value;
					if (s == null || s.IsDead)
					{
						CleanupBleeder(kv.Key);
						continue;
					}
					// 切换机制即时生效：流血被关闭或玩家免疫时，停止已有流血
					if (!Plugin.bleedEnabled.Value || (!Plugin.affectsPlayer.Value && IsPlayerControlled(s)))
					{
						CleanupBleeder(kv.Key);
						continue;
					}
					HashSet<BodyPartType> done;
					if (!detached.TryGetValue(kv.Key, out done))
					{
						CleanupBleeder(kv.Key);
						continue;
					}
					// 投降单位：血量被游戏接管，改用计时致死（投降后 10 秒流血流死）
					if (states.TryGetValue(kv.Key, out int st) && st == StSurrendered)
					{
						float stLeft = surrenderTimers.TryGetValue(kv.Key, out float stv) ? stv : 10f;
						stLeft -= Time.deltaTime;
						if (stLeft <= 0f)
						{
							if (IsPlayerControlled(s))
							{
								Plugin.Notify("You bled out from a severed limb.");
							}
							s.Kill();
							CleanupBleeder(kv.Key);
							surrenderTimers.Remove(kv.Key);
							Plugin.ModLog.LogInfo((object)"Surrendered severed-limb soldier bled out.");
						}
						else
						{
							surrenderTimers[kv.Key] = stLeft;
						}
						continue;
					}
					float rate = bleedRate.TryGetValue(kv.Key, out float rv) ? rv : 1f;
					float dps = (rate >= 1f) ? 7f : ((rate >= 0.5f) ? 4f : 1f);
					if (IsPlayerControlled(s))
					{
						// 玩家控制单位流血减半，延长断肢限制体验窗口
						dps *= 0.5f;
					}
					float acc = bleedDmgAccum.TryGetValue(kv.Key, out float a) ? a : 0f;
					acc += dps * Time.deltaTime;
					int hp = 0;
					try
					{
						hp = Math.Max(0, s.life_total.Value);
					}
					catch
					{
					}
					if (acc >= 1f)
					{
						int whole = (int)acc;
						acc -= whole;
						hp = Math.Max(0, hp - whole);
						try
						{
							s.life_total = new ProtectedInt(hp);
						}
						catch
						{
						}
					}
					bleedDmgAccum[kv.Key] = acc;
					if (hp <= 0)
					{
						if (IsPlayerControlled(s))
						{
							Plugin.Notify("You bled out from a severed limb.");
						}
						s.Kill();
						CleanupBleeder(kv.Key);
						Plugin.ModLog.LogInfo((object)"Severed-limb soldier bled out.");
					}
				}
				catch (Exception ex)
				{
					CleanupBleeder(kv.Key);
					Plugin.ModLog.LogError((object)("Bleed tick item error: " + ex.Message));
				}
			}
			foreach (KeyValuePair<IntPtr, Creature> kv in new List<KeyValuePair<IntPtr, Creature>>(nativeBleeders))
			{
				try
				{
					Creature c = kv.Value;
					if (c == null || c.IsDead)
					{
						nativeBleeders.Remove(kv.Key);
						bleedDmgAccum.Remove(kv.Key);
						continue;
					}
					bool bleeding = false;
					try
					{
						bleeding = c.isBleeding;
					}
					catch
					{
					}
					if (!bleeding)
					{
						nativeBleeders.Remove(kv.Key);
						bleedDmgAccum.Remove(kv.Key);
						continue;
					}
					float acc = bleedDmgAccum.TryGetValue(kv.Key, out float a) ? a : 0f;
					acc += 4f * Time.deltaTime;
					int hp = 0;
					try
					{
						hp = Math.Max(0, c.life_total.Value);
					}
					catch
					{
					}
					if (acc >= 1f)
					{
						int whole = (int)acc;
						acc -= whole;
						hp = Math.Max(0, hp - whole);
						try
						{
							c.life_total = new ProtectedInt(hp);
						}
						catch
						{
						}
					}
					bleedDmgAccum[kv.Key] = acc;
					if (hp <= 0)
					{
						c.Kill();
						nativeBleeders.Remove(kv.Key);
						bleedDmgAccum.Remove(kv.Key);
						Plugin.ModLog.LogInfo((object)"Bleeding soldier bled out (native).");
					}
				}
				catch (Exception ex)
				{
					nativeBleeders.Remove(kv.Key);
					bleedDmgAccum.Remove(kv.Key);
					Plugin.ModLog.LogError((object)("Native bleed tick error: " + ex.Message));
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("BleedTick error: " + ex.Message));
		}
	}

	private static void CleanupBleeder(IntPtr key)
	{
		bleeders.Remove(key);
		lastBleedAdd.Remove(key);
		bleedRate.Remove(key);
		bleedDmgAccum.Remove(key);
		surrenderTimers.Remove(key);
	}

	private static void ApplySeverDamage(Soldier soldier, float damage)
	{
		try
		{
			soldier.Damage((int)damage);
			Plugin.ModLog.LogInfo((object)("Sever hit instant damage: -" + (int)damage + " hp (no clamp)."));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ApplySeverDamage error: " + ex.Message));
		}
	}

	internal static void RegisterNativeBleed(Creature c)
	{
		try
		{
			if (c == null)
			{
				return;
			}
			IntPtr key = c.Pointer;
			if (bleeders.ContainsKey(key))
			{
				return;
			}
			nativeBleeders[key] = c;
		}
		catch
		{
		}
	}

	internal static bool IsBleedingUnit(Creature c)
	{
		return c != null && bleeders.ContainsKey(c.Pointer);
	}

	internal static void UnregisterNativeBleed(Creature c)
	{
		try
		{
			if (c == null)
			{
				return;
			}
			nativeBleeders.Remove(c.Pointer);
			bleedDmgAccum.Remove(c.Pointer);
		}
		catch
		{
		}
	}

	internal static bool IsPlayerControlled(Soldier soldier)
	{
		if (soldier == null)
		{
			return false;
		}
		try
		{
			if (soldier.IsPlayer())
			{
				return true;
			}
			PlayerController pc = PlayerController.currentController;
			return pc != null && pc.ControlledCharacter != null && pc.ControlledCharacter.Pointer == soldier.Pointer;
		}
		catch
		{
			return false;
		}
	}

	internal static bool IsLegBroken(Soldier soldier)
	{
		HashSet<BodyPartType> done;
		if (soldier == null || !detached.TryGetValue(soldier.Pointer, out done))
		{
			return false;
		}
		return done.Contains(BodyPartType.leg_l) || done.Contains(BodyPartType.leg_r);
	}

	internal static bool IsRightArmBroken(Soldier soldier)
	{
		HashSet<BodyPartType> done;
		if (soldier == null || !detached.TryGetValue(soldier.Pointer, out done))
		{
			return false;
		}
		return done.Contains(BodyPartType.arm_r);
	}

	internal static bool IsLeftArmBroken(Soldier soldier)
	{
		HashSet<BodyPartType> done;
		if (soldier == null || !detached.TryGetValue(soldier.Pointer, out done))
		{
			return false;
		}
		return done.Contains(BodyPartType.arm_l);
	}

	internal static bool IsHandBroken(Soldier soldier)
	{
		HashSet<BodyPartType> done;
		if (soldier == null || !detached.TryGetValue(soldier.Pointer, out done))
		{
			return false;
		}
		return done.Contains(BodyPartType.arm_l) || done.Contains(BodyPartType.arm_r);
	}

	internal static bool IsBothArmsBroken(Soldier soldier)
	{
		HashSet<BodyPartType> done;
		if (soldier == null || !detached.TryGetValue(soldier.Pointer, out done))
		{
			return false;
		}
		return done.Contains(BodyPartType.arm_l) && done.Contains(BodyPartType.arm_r);
	}

	internal static void RecordHit(Soldier soldier, BodyPart bp)
	{
		if (bp == null || !IsSeverableLimb(bp.bodyPart))
		{
			return;
		}
		lastHitBody[soldier.Pointer] = bp;
	}

	internal static bool TryAliveHit(Soldier soldier, BodyPart bp, float damage)
	{
		try
		{
			if (bp == null || !IsSeverableLimb(bp.bodyPart))
			{
				return false;
			}
			// 切换机制：玩家免疫断肢（AI 仍受影响）
			if (!Plugin.affectsPlayer.Value && IsPlayerControlled(soldier))
			{
				return false;
			}
			IntPtr key = soldier.Pointer;
			HashSet<BodyPartType> done;
			if (detached.TryGetValue(key, out done) && done.Count > 0)
			{
				// 已断肢单位：不再累积/锁定，后续伤害（含手雷）走游戏原生全额结算
				return false;
			}
			Dictionary<BodyPartType, float> parts;
			if (!aliveDamage.TryGetValue(key, out parts))
			{
				parts = new Dictionary<BodyPartType, float>();
				aliveDamage[key] = parts;
			}
			BodyPartType p = bp.bodyPart;
			float cur = parts.TryGetValue(p, out float v) ? v : 0f;
			cur += damage;
			parts[p] = cur;
			if (cur < Plugin.damageNeeded.Value)
			{
				return false;
			}
			ApplySeverDamage(soldier, damage);
			if (soldier.IsDead)
			{
				Plugin.ModLog.LogInfo((object)"Severing hit killed the soldier outright.");
				return true;
			}
			DetachOnce(soldier, bp);
			return true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Alive hit error: " + ex.Message));
			return false;
		}
	}

	internal static void TryDismember(RagdollManager ragdoll)
	{
		try
		{
			if (!Plugin.enabled.Value || ragdoll == null)
			{
				return;
			}
			Soldier soldier = ragdoll.GetComponent<Soldier>();
			if (soldier == null)
			{
				soldier = ragdoll.GetComponentInParent<Soldier>(true);
			}
			if (soldier == null)
			{
				return;
			}
			BodyPart bp;
			if (!lastHitBody.TryGetValue(soldier.Pointer, out bp) || bp == null || !IsSeverableLimb(bp.bodyPart))
			{
				return;
			}
			DetachOnce(soldier, bp);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Dismember error: " + ex.Message));
		}
	}

	internal static void TryCorpseHit(Soldier soldier, BodyPart bp, float damage)
	{
		try
		{
			if (bp == null || !IsSeverableLimb(bp.bodyPart))
			{
				return;
			}
			if (damage <= 0f)
			{
				return;
			}
			IntPtr key = soldier.Pointer;
			Dictionary<BodyPartType, float> parts;
			if (!corpseDamage.TryGetValue(key, out parts))
			{
				parts = new Dictionary<BodyPartType, float>();
				corpseDamage[key] = parts;
			}
			BodyPartType p = bp.bodyPart;
			float cur = parts.TryGetValue(p, out float v) ? v : 0f;
			cur += damage;
			parts[p] = cur;
			if (cur < Plugin.corpseDamageThreshold.Value)
			{
				return;
			}
			DetachOnce(soldier, bp);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("Corpse hit error: " + ex.Message));
		}
	}

	private static bool IsSeverableLimb(BodyPartType part)
	{
		return part == BodyPartType.arm_l || part == BodyPartType.arm_r || part == BodyPartType.leg_l || part == BodyPartType.leg_r;
	}

	private static void DetachOnce(Soldier soldier, BodyPart bp)
	{
		BodyPartType part = bp.bodyPart;
		IntPtr key = soldier.Pointer;
		HashSet<BodyPartType> done;
		if (!detached.TryGetValue(key, out done))
		{
			done = new HashSet<BodyPartType>();
			detached[key] = done;
		}
		if (!done.Add(part))
		{
			return;
		}
		bool alive = soldier.NotDeadAndSurrendered();
		if (alive)
		{
			StartLimbBleed(soldier, key, part);
			ApplyState(soldier, done, key);
		}
		if (part == BodyPartType.arm_l || part == BodyPartType.arm_r)
		{
			DropWeaponOnce(soldier, key);
		}
		if (alive)
		{
			// 活体断肢不调用原生 DetachLimb（其内置失血 DoT 会导致快速死亡），只做视觉隐藏
			if (part == BodyPartType.arm_l)
			{
				QueuePistolSwitch(soldier);
				HideArm(soldier, left: true);
			}
			else if (part == BodyPartType.arm_r)
			{
				HideArm(soldier, left: false);
			}
			else if (part == BodyPartType.leg_l || part == BodyPartType.leg_r)
			{
				// 活体断腿：用 BodyPart 的骨骼做视觉隐藏（soldier 只有单个 legTransform，不区分左右）
				CacheLegBone(key, part, bp != null ? bp.transform : null);
				HideLimbBone(bp != null ? bp.transform : null, "leg " + part);
			}
		}
		else
		{
			soldier.DetachLimb(part);
		}
		Plugin.ModLog.LogInfo((object)("Limb detached: " + part + (alive ? " (mangled)" : "") + "."));
	}

	internal static void QueuePistolSwitch(Soldier soldier)
	{
		try
		{
			Il2CppReferenceArray<WearedItem> items = soldier.wearedItems;
			if (items == null)
			{
				return;
			}
			for (int i = 0; i < items.Length; i++)
			{
				if (items[i] == null)
				{
					continue;
				}
				if (IsPistol(GetWearedItemId(soldier, i)))
				{
					soldier.SwitchTo(i);
					Plugin.ModLog.LogInfo((object)("Left arm severed - equipped pistol (slot " + i + ")."));
					return;
				}
			}
			Plugin.ModLog.LogInfo((object)"Left arm severed - no pistol found, staying unarmed.");
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("QueuePistolSwitch error: " + ex.Message));
		}
	}

	private static void DropWeaponOnce(Soldier soldier, IntPtr key)
	{
		if (droppedWeapon.TryGetValue(key, out bool dropped) && dropped)
		{
			return;
		}
		droppedWeapon[key] = true;
		try
		{
			string wid = GetWearedItemId(soldier, 0);
			if (IsPistol(wid))
			{
				Plugin.ModLog.LogInfo((object)"Arm severed - kept pistol.");
				return;
			}
			soldier.DropItemNow(0);
			Plugin.ModLog.LogInfo((object)"Arm severed - weapon dropped.");
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("DropItemNow error: " + ex.Message));
		}
	}

	internal static string GetWearedItemId(Soldier soldier, int index)
	{
		try
		{
			Il2CppReferenceArray<WearedItem> items = soldier.wearedItems;
			if (items != null && index >= 0 && index < items.Length && items[index] != null)
			{
				WearedItem wi = items[index];
				if (wi.itemInstance != null)
				{
					return wi.itemInstance.item_id;
				}
				if (wi.inventoryReference != null)
				{
					return wi.inventoryReference.item_id;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	internal static void RestoreHide(Soldier soldier)
	{
		try
		{
			if (soldier == null)
			{
				return;
			}
			bool left = IsArmBroken(soldier, BodyPartType.arm_l);
			bool right = IsArmBroken(soldier, BodyPartType.arm_r);
			if (left)
			{
				HideArm(soldier, left: true);
			}
			if (right)
			{
				HideArm(soldier, left: false);
			}
			RehideLegBones(soldier);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("RestoreHide error: " + ex.Message));
		}
	}

	private static bool IsArmBroken(Soldier soldier, BodyPartType part)
	{
		HashSet<BodyPartType> done;
		if (soldier == null || !detached.TryGetValue(soldier.Pointer, out done))
		{
			return false;
		}
		return done.Contains(part);
	}

	/// <summary>隐藏断腿骨骼（bp.transform 即该腿的骨骼；缓存引用供控制权切换后重应用）。</summary>
	private static readonly Dictionary<IntPtr, Dictionary<BodyPartType, Transform>> hiddenLegBones = new Dictionary<IntPtr, Dictionary<BodyPartType, Transform>>();

	private static void HideLimbBone(Transform bone, string what)
	{
		try
		{
			if (bone == null)
			{
				Plugin.ModLog.LogInfo((object)("HideLimbBone: bone is null for " + what + "."));
				return;
			}
			bone.localScale = Vector3.zero;
			Renderer[] renderers = bone.GetComponentsInChildren<Renderer>(true);
			if (renderers != null)
			{
				foreach (Renderer r in renderers)
				{
					if (r != null)
					{
						r.enabled = false;
					}
				}
			}
			Plugin.ModLog.LogInfo((object)("HideLimbBone: hid bone '" + bone.name + "' (" + what + ")."));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("HideLimbBone error: " + ex.Message));
		}
	}

	/// <summary>缓存断腿骨骼引用（DetachOnce 调用）。</summary>
	internal static void CacheLegBone(IntPtr key, BodyPartType part, Transform bone)
	{
		try
		{
			Dictionary<BodyPartType, Transform> m;
			if (!hiddenLegBones.TryGetValue(key, out m))
			{
				m = new Dictionary<BodyPartType, Transform>();
				hiddenLegBones[key] = m;
			}
			m[part] = bone;
		}
		catch
		{
		}
	}

	/// <summary>重隐藏断腿骨骼（控制权切换后）。</summary>
	internal static void RehideLegBones(Soldier soldier)
	{
		try
		{
			if (soldier == null)
			{
				return;
			}
			Dictionary<BodyPartType, Transform> m;
			if (!hiddenLegBones.TryGetValue(soldier.Pointer, out m))
			{
				return;
			}
			foreach (KeyValuePair<BodyPartType, Transform> kv in m)
			{
				if (kv.Value != null)
				{
					HideLimbBone(kv.Value, "rehide leg " + kv.Key);
				}
			}
		}
		catch
		{
		}
	}

	private static void HideArm(Soldier soldier, bool left)
	{		try
		{
			try
			{
				Transform t = left ? soldier.leftArm : soldier.rightArm;
				if (t != null)
				{
					t.localScale = Vector3.zero;
					Renderer[] renderers = t.GetComponentsInChildren<Renderer>(true);
					if (renderers != null)
					{
						foreach (Renderer r in renderers)
						{
							if (r != null)
							{
								r.enabled = false;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("HideArm arm bone error: " + ex.Message));
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("HideArm error: " + ex.Message));
		}
	}

	private static void ApplyState(Soldier soldier, HashSet<BodyPartType> done, IntPtr key)
	{
		int st = states.TryGetValue(key, out int s) ? s : StNormal;
		if (st == StSurrendered)
		{
			soldier.Kill();
			Plugin.ModLog.LogInfo((object)"Surrendered soldier died on further limb loss.");
			return;
		}
		bool legBroken = done.Contains(BodyPartType.leg_l) || done.Contains(BodyPartType.leg_r);
		bool rightArmBroken = done.Contains(BodyPartType.arm_r);
		try
		{
			if (legBroken)
			{
				if (st != StIncapacitated)
				{
					soldier.SetPose(SoldierPose.Prone);
					states[key] = StIncapacitated;
					Plugin.ModLog.LogInfo((object)"Mangled soldier -> forced prone (leg broken).");
				}
			}
			else if (rightArmBroken)
			{
				if (st != StSurrendered)
				{
					soldier.Surrender();
					states[key] = StSurrendered;
					surrenderTimers[key] = 10f;
					Plugin.ModLog.LogInfo((object)"Mangled soldier -> surrender (right arm severed).");
				}
			}
			else
			{
				states[key] = StNormal;
				Plugin.ModLog.LogInfo((object)"Mangled soldier -> normal.");
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ApplyState error: " + ex.Message));
		}
	}
}