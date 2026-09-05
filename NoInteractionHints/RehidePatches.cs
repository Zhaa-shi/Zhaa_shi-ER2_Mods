using Corvostudio.UI;
using HarmonyLib;

namespace ER2NoInteractionHints
{
	/// <summary>
	/// 每帧补藏 patch（v4.4）：游戏在 LateUpdate/Update 里每帧重新显示部分 UI，
	/// 巡检（Update 阶段）隐藏后同一帧就被恢复。这些 Postfix 挂在游戏自己
	/// 重新显示逻辑之后，把对应类别再藏回去 → 视觉上始终隐藏。
	/// 全部无参 Postfix，开销极小（仅 cfg 勾选时才执行）。
	/// </summary>
	internal static class RehidePatches
	{
		// ===== 玩家 HUD / 目标横幅 / 聊天 / 杂项（同一个管理器）=====
		[HarmonyPatch(typeof(PlayerGUI), "LateUpdate")]
		private static class RehidePlayerGuiPatch
		{
			private static void Postfix()
			{
				if (UiGroups.IsHidden("hud"))
				{
					UiHiders.RehidePlayerHud();
				}
				if (UiGroups.IsHidden("objectiveBanner"))
				{
					UiHiders.RehideBanners();
				}
				if (UiGroups.IsHidden("chat"))
				{
					UiHiders.RehideChat();
				}
				if (UiGroups.IsHidden("misc"))
				{
					UiHiders.RehideMisc();
				}
			}
		}

		// ===== 命中反馈 + 准星 =====
		[HarmonyPatch(typeof(HitmarkerGUI), "LateUpdate")]
		private static class RehideHitmarkerPatch
		{
			private static void Postfix()
			{
				if (UiGroups.IsHidden("hitmarker"))
				{
					UiHiders.RehideHitmarker();
				}
			}
		}

		// ===== 目标指示器 =====
		[HarmonyPatch(typeof(ObjectiveGUI), "LateUpdate")]
		private static class RehideObjectivePatch
		{
			private static void Postfix()
			{
				if (UiGroups.IsHidden("objectives"))
				{
					UiHiders.RehideObjectives();
				}
			}
		}

		// ===== 阶段进度条 =====
		// 0.9.15：PhaseBarGUI 在新版游戏中被移除——改为条件 patch（类型存在才挂）。
		internal static void PatchPhaseBarRehide(Harmony h)
		{
			if (!Plugin.PhaseBarPresent) return;
			try
			{
				var t = AccessTools.TypeByName("PhaseBarGUI");
				var mLate = AccessTools.Method(t, "LateUpdate");
				var post = new HarmonyMethod(typeof(RehidePatches), nameof(RehidePhaseBarPostfix));
				h.Patch(mLate, postfix: post);
			}
			catch (System.Exception ex) { Plugin.ModLog.LogWarning("PhaseBar rehide patch skipped: " + ex.Message); }
		}

		private static void RehidePhaseBarPostfix()
		{
			try
			{
				if (UiGroups.IsHidden("phaseBar"))
				{
					UiHiders.RehidePhaseBar();
				}
			}
			catch { }
		}

		// ===== 载具 HUD =====
		[HarmonyPatch(typeof(VehicleGUI), "Update")]
		private static class RehideVehiclePatch
		{
			private static void Postfix()
			{
				if (UiGroups.IsHidden("vehicle"))
				{
					UiHiders.RehideVehicle();
				}
			}
		}

		// ===== 地图与小地图 =====
		[HarmonyPatch(typeof(MiniMapGUI), "Update")]
		private static class RehideMapPatch
		{
			private static void Postfix()
			{
				if (UiGroups.IsHidden("map"))
				{
					UiHiders.RehideMap();
				}
			}
		}

		// ===== 随动标记（世界头顶标记）=====
		[HarmonyPatch(typeof(Marker3DGUI), "LateUpdate")]
		private static class RehideWorldMarkersPatch
		{
			private static void Postfix()
			{
				if (UiGroups.IsHidden("worldMarkers"))
				{
					UiHiders.RehideWorldMarkers();
				}
			}
		}
	}
}
