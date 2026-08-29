using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using UnityEngine;

namespace ER2ZoomAnywhere
{
	/// <summary>
	/// ER2 Zoom Anywhere（任意情况放大）
	///
	/// 原版行为：静止 + 瞄准（ADS）时按住 Shift 屏息 → 镜头小幅放大 + 准星稳定。
	/// 本 mod 解除"静止"限制：移动 / 奔跑 / 任何姿态中按住 Shift 都能屏息放大；
	/// 可选：不瞄准（腰射）时按住也能放大；可调额外放大倍率。
	///
	/// 原理（基于游戏内部机制）：
	///  - Soldier.CanHoldBreath() 是"能否屏息"的门槛（原版要求静止瞄准）；
	///  - PlayerController.holdingBreath / IsHoldingBreath() 是屏息状态；
	///  - FPSGunManager 用 holdBreathContribution 驱动镜头放大与稳定；
	///  - PlayerController.GetAimingFOV() 输出当前瞄准 FOV（额外倍率挂在这里）。
	/// </summary>
	[BepInPlugin("er2.zoomanywhere", "ER2 Zoom Anywhere", "1.0.1")]
	public class Plugin : BasePlugin
	{
		internal static ManualLogSource ModLog;

		internal static ConfigEntry<bool> enabled;
		internal static ConfigEntry<bool> anyMovement;
		internal static ConfigEntry<bool> withoutAiming;
		internal static ConfigEntry<float> extraZoom;
		internal static ConfigEntry<KeyCode> holdKey;

		public override void Load()
		{
			ModLog = Log;
			enabled = Config.Bind("Zoom", "enabled", true, "Master switch for Zoom Anywhere (restart required).");
			anyMovement = Config.Bind("Zoom", "anyMovement", true, "Allow hold-breath zoom while moving / in any stance (vanilla only allows it while standing still).");
			withoutAiming = Config.Bind("Zoom", "withoutAiming", false, "Also apply the extra zoom while not aiming (hip fire). Needs a zoom multiplier above 1.0.");
			extraZoom = Config.Bind("Zoom", "extraZoom", 1.0f, "Extra zoom multiplier applied while holding the key (1.0 = vanilla zoom only, 1.5 = 1.5x closer).");
			holdKey = Config.Bind("Zoom", "holdKey", KeyCode.LeftShift, "Key that activates the zoom (default Left Shift, same as vanilla hold breath).");
			new Harmony("er2.zoomanywhere").PatchAll();
			ModLog.LogInfo((object)"ER2 Zoom Anywhere 1.0.1 loaded.");
		}

		/// <summary>总开关 + 按键是否按住。</summary>
		internal static bool KeyHeld()
		{
			if (enabled == null || !enabled.Value)
			{
				return false;
			}
			try
			{
				KeyCode k = (holdKey != null) ? holdKey.Value : KeyCode.LeftShift;
				return Input.GetKey(k);
			}
			catch
			{
				return false;
			}
		}

		/// <summary>是否强制屏息状态（解除静止限制）。</summary>
		internal static bool BreathForced()
		{
			return KeyHeld() && anyMovement != null && anyMovement.Value;
		}
	}

	// ===== 1. 解除"静止"门槛：玩家士兵 CanHoldBreath 强制 true =====
	[HarmonyPatch(typeof(Soldier), "CanHoldBreath")]
	internal static class CanHoldBreathPatch
	{
		private static void Postfix(Soldier __instance, ref bool __result)
		{
			if (!Plugin.BreathForced())
			{
				return;
			}
			try
			{
				PlayerController pc = PlayerController.currentController;
				if (pc == null || pc.ControlledCharacter == null)
				{
					return;
				}
				if (pc.ControlledCharacter != __instance)
				{
					return; // 只影响玩家控制的士兵
				}
				__result = true;
			}
			catch
			{
			}
		}
	}

	// ===== 2. 屏息状态读取点强制 true（FPSGunManager 等消费方直接查它）=====
	[HarmonyPatch(typeof(PlayerController), "IsHoldingBreath")]
	internal static class IsHoldingBreathPatch
	{
		private static void Postfix(ref bool __result)
		{
			if (Plugin.BreathForced())
			{
				__result = true;
			}
		}
	}

	// ===== 3. 防御：Update 之后直接把 holdingBreath 字段写 true =====
	// （万一原版 Update 内部在 CanHoldBreath 之外还有移动门槛）
	[HarmonyPatch(typeof(PlayerController), "Update")]
	internal static class ForceHoldingBreathFieldPatch
	{
		private static void Postfix(PlayerController __instance)
		{
			if (!Plugin.BreathForced() || __instance == null)
			{
				return;
			}
			try
			{
				__instance.holdingBreath = true;
			}
			catch
			{
			}
		}
	}

	// ===== 4. 额外放大倍率（步兵瞄准 FOV）=====
	[HarmonyPatch(typeof(PlayerController), "GetAimingFOV")]
	internal static class ExtraZoomPatch
	{
		private static void Postfix(PlayerController __instance, ref float __result)
		{
			if (!Plugin.KeyHeld())
			{
				return;
			}
			float mult = PatchUtil.ClampMult(Plugin.extraZoom);
			if (mult <= 1.01f)
			{
				return;
			}
			bool aiming = false;
			try
			{
				aiming = __instance != null && __instance.IsAiming;
			}
			catch
			{
			}
			if (!aiming && !Plugin.withoutAiming.Value)
			{
				return;
			}
			// 倍镜/瞄具本身已放大，不叠加，避免过曝
			// （版本兼容：老版本游戏没有 IsAimingThroughMagnifiedScope()，见 PatchUtil）
			if (PatchUtil.IsAimingThroughMagnifiedScope(__instance))
			{
				return;
			}
			__result = Mathf.Lerp(__result, __result / mult, 1f);
		}
	}

	// ===== 5. 额外放大倍率（载具瞄准 FOV）=====
	[HarmonyPatch(typeof(PlayerController), "GetAimingFOVVehicle")]
	internal static class ExtraZoomVehiclePatch
	{
		private static void Postfix(ref float __result)
		{
			if (!Plugin.KeyHeld())
			{
				return;
			}
			float mult = PatchUtil.ClampMult(Plugin.extraZoom);
			if (mult <= 1.01f)
			{
				return;
			}
			try
			{
				PlayerController pc = PlayerController.currentController;
				if (PatchUtil.IsAimingThroughMagnifiedScope(pc))
				{
					return;
				}
			}
			catch
			{
			}
			__result = Mathf.Lerp(__result, __result / mult, 1f);
		}
	}

	internal static class PatchUtil
	{
		internal static float ClampMult(ConfigEntry<float> e)
		{
			if (e == null)
			{
				return 1f;
			}
			return Mathf.Clamp(e.Value, 1f, 3f);
		}

		// 倍镜检测（版本兼容）。
		// PlayerController.IsAimingThroughMagnifiedScope() 是较新游戏版本才加入的方法，
		// 在旧版本上直接编译期调用会在每次 GetAimingFOV/GetAimingFOVVehicle 时抛
		// MissingMethodException（载具/机枪画面乱闪 bug 的根因），且该异常无法被方法内 try/catch 接住。
		// 这里改为反射缓存 MethodInfo：方法存在 → 正常检测；方法缺失 → 返回 false 并只提示一次，
		// 行为退化为"不做倍镜防叠加保护"，但绝不崩溃。
		private static System.Reflection.MethodInfo magnifiedScopeMethod;
		private static bool magnifiedScopeResolved;

		internal static bool IsAimingThroughMagnifiedScope(PlayerController pc)
		{
			if (pc == null)
			{
				return false;
			}
			if (!magnifiedScopeResolved)
			{
				magnifiedScopeResolved = true;
				magnifiedScopeMethod = typeof(PlayerController).GetMethod("IsAimingThroughMagnifiedScope", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
				if (magnifiedScopeMethod == null && Plugin.ModLog != null)
				{
					Plugin.ModLog.LogWarning((object)"IsAimingThroughMagnifiedScope() not found in this game version - magnified-scope over-zoom protection disabled (compat mode).");
				}
			}
			System.Reflection.MethodInfo m = magnifiedScopeMethod;
			if (m == null)
			{
				return false;
			}
			try
			{
				return (bool)m.Invoke(pc, null);
			}
			catch
			{
				return false;
			}
		}
	}
}
