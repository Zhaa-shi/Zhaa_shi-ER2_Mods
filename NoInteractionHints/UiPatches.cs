using Corvostudio.UI;
using HarmonyLib;

namespace ER2NoInteractionHints
{
	/// <summary>
	/// 一次性显示类的拦截 patch（v4.4）：对应 UI 类别隐藏时，Prefix 返回 false 阻止新显示。
	/// 全部用无参 Prefix（只 return false），不依赖参数名（Harmony 按名注入的坑）。
	/// </summary>
	internal static class UiPatches
	{
		// ===== 通知弹窗（原生 Hint 通道：游戏教程提示 + mod 通知）=====
		// Display 是普通路径；ForceDisplayNow / ForceAndPlay 是"强制显示"路径
		// （绕过 Display 直接播放）；PlayerController.HintToPlayer 是游戏侧的便捷入口。
		[HarmonyPatch(typeof(Hint), "Display")]
		private static class BlockHintDisplay
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("notifications");
			}
		}

		[HarmonyPatch(typeof(Hint), "ForceDisplayNow")]
		private static class BlockHintForceDisplayNow
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("notifications");
			}
		}

		[HarmonyPatch(typeof(Hint), "ForceAndPlay")]
		private static class BlockHintForceAndPlay
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("notifications");
			}
		}

		[HarmonyPatch(typeof(PlayerController), "HintToPlayer")]
		private static class BlockHintToPlayer
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("notifications");
			}
		}

		// ===== 目标横幅 / 短消息 =====
		[HarmonyPatch(typeof(PlayerGUI), "ShowObjectiveToPlayer")]
		private static class BlockObjectiveBanner
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("objectiveBanner");
			}
		}

		[HarmonyPatch(typeof(PlayerGUI), "ShowShortText")]
		private static class BlockShortText
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("objectiveBanner");
			}
		}

		// ===== 受伤血屏（默认不随 F5 隐藏，用户可开）=====
		[HarmonyPatch(typeof(BloodSplashGUI), "PlayEffect")]
		private static class BlockBloodSplash
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("bloodSplash");
			}
		}

		// ===== 命中反馈（默认不随 F5 隐藏，用户可开）=====
		[HarmonyPatch(typeof(HitmarkerGUI), "HitMarkerFX")]
		private static class BlockHitmarker
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("hitmarker");
			}
		}

		// ===== 准星（随命中反馈一起隐藏；PlaceCrosshair 每帧/事件都会重新放置准星）=====
		[HarmonyPatch(typeof(HitmarkerGUI), "PlaceCrosshair")]
		private static class BlockPlaceCrosshair
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("hitmarker");
			}
		}

		[HarmonyPatch(typeof(HitmarkerGUI), "UpdateCrosshair")]
		private static class BlockUpdateCrosshair
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("hitmarker");
			}
		}

		// ===== 聊天（默认不随 F5 隐藏）=====
		[HarmonyPatch(typeof(ChatMessage), "NewChatMessage")]
		private static class BlockChatMessage
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("chat");
			}
		}

		[HarmonyPatch(typeof(ChatMessage), "NewJoinMessage")]
		private static class BlockJoinMessage
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("chat");
			}
		}

		[HarmonyPatch(typeof(ChatMessage), "NewLeftMessage")]
		private static class BlockLeftMessage
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("chat");
			}
		}

		[HarmonyPatch(typeof(ChatMessage), "NewKilledMessage")]
		private static class BlockKilledMessage
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("chat");
			}
		}

		[HarmonyPatch(typeof(ChatMessage), "NewDisabledPlayerVehicleMessage")]
		private static class BlockDisabledVehicleMessage
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("chat");
			}
		}

		// ===== 跳过提示 =====
		[HarmonyPatch(typeof(SkipPromptGUI), "Draw")]
		private static class BlockSkipPrompt
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("misc");
			}
		}

		// ===== 菜单通知 =====
		[HarmonyPatch(typeof(MenuNotification), "Setup")]
		private static class BlockMenuNotification
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("misc");
			}
		}

		// ===== 目标指示器（停止更新，视觉由 UiHiders.SetObjectives / RehideObjectives 隐藏）=====
		[HarmonyPatch(typeof(ObjectiveGUI), "LateUpdate")]
		private static class BlockObjectiveUpdate
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("objectives");
			}
		}

		// ===== 任务状态面板（MissionStatusGUI 由 Lua 驱动，补藏即可；阻断不了就靠巡检）=====

		// ===== 阶段进度条 =====
		// 0.9.15：PhaseBarGUI 在新版游戏中被移除——类型缺失时 typeof() 会抛 TypeLoadException，
		// 改为条件 patch（类型存在才挂），不再使用 [HarmonyPatch] 特性。
		internal static void PatchPhaseBar(Harmony h)
		{
			if (!Plugin.PhaseBarPresent) return;
			try
			{
				var t = Plugin.PhaseBarType;
				var mSetVisible = AccessTools.Method(t, "SetVisible");
				var post = new HarmonyMethod(typeof(UiPatches), nameof(BlockPhaseBarShowPostfix));
				h.Patch(mSetVisible, postfix: post);
			}
			catch (System.Exception ex) { Plugin.ModLog.LogWarning("PhaseBar patch skipped: " + ex.Message); }
		}

		private static void BlockPhaseBarShowPostfix(bool visible)
		{
			try
			{
				if (UiGroups.IsHidden("phaseBar") && visible)
				{
					AccessTools.Method(Plugin.PhaseBarType, "SetVisible").Invoke(null, new object[] { false });
				}
			}
			catch { }
		}

		// ===== 地图与小地图（隐藏时禁止打开全屏地图 / 小地图）=====
		[HarmonyPatch(typeof(MapGUI), "OpenMap")]
		private static class BlockOpenMap
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("map");
			}
		}

		[HarmonyPatch(typeof(MiniMapGUI), "OpenMiniMap")]
		private static class BlockOpenMiniMap
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("map");
			}
		}

		[HarmonyPatch(typeof(MiniMapGUI), "OpenCloseMiniMap")]
		private static class BlockOpenCloseMiniMap
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("map");
			}
		}

		// ===== 瞄具界面（隐藏时禁止开启）=====
		[HarmonyPatch(typeof(ScopeGUI), "EnableScope")]
		private static class BlockScopeEnable
		{
			private static bool Prefix()
			{
				return !UiGroups.IsHidden("scope");
			}
		}

		// ===== 教程字幕/任务面板（隐藏时禁止显示）=====
		[HarmonyPatch(typeof(TutorialGUI), "ShowTasks")]
		private static class BlockTutorialShowTasks
		{
			private static void Postfix(TutorialGUI __instance, bool show)
			{
				if (UiGroups.IsHidden("misc") && show && __instance != null)
				{
					if (__instance.captions_panel != null)
					{
						__instance.captions_panel.SetActive(false);
					}
					if (__instance.tasks_panel != null)
					{
						__instance.tasks_panel.SetActive(false);
					}
				}
			}
		}
	}
}
