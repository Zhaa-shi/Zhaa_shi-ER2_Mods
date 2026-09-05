using System;
using HarmonyLib;
using System.Collections.Generic;
using Corvostudio.UI;
using UnityEngine;
using UnityEngine.UI;

namespace ER2NoInteractionHints
{
	/// <summary>
	/// 各 UI 类别的隐藏动作（v4.4）。
	///
	/// 失效根因（v4.3 实测）：游戏在 LateUpdate/Update 里每帧重新显示部分 UI
	/// （PlayerGUI.LateUpdate / HitmarkerGUI.LateUpdate / PhaseBarGUI.LateUpdate /
	/// ObjectiveGUI.LateUpdate / VehicleGUI.Update / MiniMapGUI.Update），
	/// 而巡检 tick 挂在 Soldier.Update Postfix（Update 阶段），隐藏后同一帧的
	/// LateUpdate 又把它显示回来 → 看起来"完全没用"。
	///
	/// v4.4 三管齐下：
	///  1. 源拦截（UiPatches）：一次性显示类直接阻断入口（Display/ForceDisplayNow/
	///     HitMarkerFX/PlaceCrosshair/ShowObjectiveToPlayer/OpenMap…）；
	///  2. 每帧补藏（RehidePatches）：在对应类的 LateUpdate/Update Postfix 里
	///     重新隐藏（运行在游戏自己重新显示之后）；
	///  3. 巡检兜底（UiGroups.Enforce）：常驻元素 + 找不到目标时记录诊断日志。
	/// </summary>
	internal static class UiHiders
	{
		/// <summary>记忆式元素隐藏器：Hide 时只藏 activeSelf 的元素并记录；ShowAll 只还原记录的。</summary>
		internal sealed class ElementHider
		{
			private readonly HashSet<GameObject> hidden = new HashSet<GameObject>();

			/// <summary>隐藏（返回是否真的执行了隐藏，供诊断日志）。</summary>
			internal bool Hide(Component c)
			{
				return HideGo(c != null ? c.gameObject : null);
			}

			internal bool HideGo(GameObject go)
			{
				if (go != null && go.activeSelf)
				{
					go.SetActive(false);
					hidden.Add(go);
					return true;
				}
				return false;
			}

			internal void ShowAll()
			{
				foreach (GameObject go in hidden)
				{
					if (go != null)
					{
						go.SetActive(true);
					}
				}
				hidden.Clear();
			}

			/// <summary>是否有我们藏过的元素被游戏重新显示（activeSelf 恢复）。</summary>
			internal bool AnyReShown()
			{
				foreach (GameObject go in hidden)
				{
					if (go != null && go.activeSelf)
					{
						return true;
					}
				}
				return false;
			}
		}

		/// <summary>无静态 instance 的类：缓存查找，场景重载后自动重查；找不到时冷却防热路径扫描。</summary>
		internal sealed class RefCache<T> where T : Component
		{
			private T cached;
			private float nextTry;

			internal T Get()
			{
				if (cached != null)
				{
					return cached;
				}
				if (Time.unscaledTime < nextTry)
				{
					return null;
				}
				nextTry = Time.unscaledTime + 5f;
				try
				{
					cached = UnityEngine.Object.FindObjectOfType<T>();
				}
				catch
				{
					cached = null;
				}
				return cached;
			}
		}

		/// <summary>低频诊断日志（每类独立节流，10 秒一条，避免刷屏）。</summary>
		internal sealed class MissLog
		{
			private float nextLog;
			private readonly string what;

			internal MissLog(string what)
			{
				this.what = what;
			}

			internal void Miss()
			{
				if (Time.unscaledTime < nextLog)
				{
					return;
				}
				nextLog = Time.unscaledTime + 10f;
				Plugin.ModLog?.LogInfo((object)("NIH: " + what + " targets not found."));
			}

			internal void Done()
			{
				if (Time.unscaledTime >= nextLog)
				{
					nextLog = Time.unscaledTime + 10f;
					Plugin.ModLog?.LogInfo((object)("NIH: " + what + " hidden."));
				}
			}
		}

		// ===== 1. 互动提示（InteractionGUI2/InteractionGUI/InputDisplayer）=====
		internal static readonly ElementHider Hints = new ElementHider();

		internal static void SetHints(bool visible)
		{
			if (visible)
			{
				Hints.ShowAll();
				return;
			}
			try
			{
				InteractionGUI2 ig2 = InteractionGUI2.instance;
				if (ig2 != null)
				{
					if (ig2.interactionWindow != null)
					{
						Hints.HideGo(ig2.interactionWindow.gameObject);
					}
					Hints.Hide(ig2.inputDisplayer);
				}
				Hints.Hide(InteractionGUI.instance);
			}
			catch
			{
			}
		}

		// ===== 2. 通知弹窗（原生 Hint 通道：游戏教程提示 + 所有走该通道的 mod 通知）=====
		// 源拦截在 UiPatches（Display/ForceDisplayNow/ForceAndPlay/HintToPlayer），
		// 这里负责把正在显示的立即清掉。
		internal static void SetNotifications(bool visible)
		{
			if (visible)
			{
				return; // 恢复：下一次 Display 正常播放
			}
			try
			{
				Hint h = Hint.instance;
				if (h == null)
				{
					return;
				}
				if (h.playCoroutine != null)
				{
					h.StopCoroutine(h.playCoroutine);
					h.playCoroutine = null;
				}
				if (h.forceCoroutine != null)
				{
					h.StopCoroutine(h.forceCoroutine);
					h.forceCoroutine = null;
				}
				if (h.fadeText != null && h.fadeText.gameObject != null)
				{
					CanvasGroup cg = h.fadeText.gameObject.GetComponent<CanvasGroup>();
					if (cg != null)
					{
						cg.alpha = 0f;
					}
					else
					{
						Color c = h.fadeText.color;
						h.fadeText.color = new Color(c.r, c.g, c.b, 0f);
					}
				}
				Il2CppSystem.Collections.Generic.List<Hint.HintData> q = Hint.hintQueue;
				if (q != null)
				{
					try
					{
						q.Clear();
					}
					catch
					{
						Hint.hintQueue = new Il2CppSystem.Collections.Generic.List<Hint.HintData>();
					}
				}
			}
			catch
			{
			}
		}

		// ===== 3. 目标横幅/短消息（PlayerGUI.objectiveGUI 容器）=====
		internal static readonly ElementHider Banners = new ElementHider();
		private static readonly MissLog BannersLog = new MissLog("objective banners");

		internal static void SetBanners(bool visible)
		{
			if (visible)
			{
				Banners.ShowAll();
				return;
			}
			try
			{
				PlayerGUI p = PlayerGUI.instance;
				if (p == null || p.objectiveGUI == null)
				{
					BannersLog.Miss();
					return;
				}
				if (Banners.HideGo(p.objectiveGUI.gameObject))
				{
					BannersLog.Done();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（PlayerGUI.LateUpdate Postfix 调用）：游戏可能在 LateUpdate 里重新显示横幅容器。</summary>
		internal static void RehideBanners()
		{
			PlayerGUI p = PlayerGUI.instance;
			if (p != null && p.objectiveGUI != null && p.objectiveGUI.gameObject != null && p.objectiveGUI.gameObject.activeSelf)
			{
				p.objectiveGUI.gameObject.SetActive(false);
			}
		}

		// ===== 4. 玩家 HUD（弹药/武器名/归零/倒地/翻越/语音/小队面板）=====
		internal static readonly ElementHider PlayerHud = new ElementHider();
		private static readonly MissLog HudLog = new MissLog("player HUD");

		internal static void SetPlayerHud(bool visible)
		{
			if (visible)
			{
				PlayerHud.ShowAll();
				return;
			}
			try
			{
				PlayerGUI p = PlayerGUI.instance;
				if (p == null)
				{
					HudLog.Miss();
					return;
				}
				bool did = false;
				// 整个玩家状态 HUD 容器（武器名/弹药/归零/姿态的父级）
				did = PlayerHud.HideGo(p.gui_obj) || did;
				did = PlayerHud.Hide(p.weapon_name) || did;
				did = PlayerHud.Hide(p.weapon_ammos) || did;
				did = PlayerHud.Hide(p.zeroingText) || did;
				did = PlayerHud.HideGo(p.bleedingOutUi) || did;
				did = PlayerHud.HideGo(p.vault_icon) || did;
				did = PlayerHud.Hide(p.doRoleNearbyicon) || did;
				did = PlayerHud.HideGo(p.speaker_icon) || did;
				// 死亡界面（选其他小队成员）期间不隐藏小队面板，保证死亡后能切换队员
				if (!IsDeathScreenActive())
				{
					did = PlayerHud.Hide(p.squadGUI_Panel) || did;
					did = PlayerHud.HideGo(p.squadData_Panel) || did;
				}
				if (did)
				{
					HudLog.Done();
				}
				else
				{
					HudLog.Miss();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（PlayerGUI.LateUpdate Postfix 调用）。</summary>
		internal static void RehidePlayerHud()
		{
			PlayerGUI p = PlayerGUI.instance;
			if (p == null)
			{
				return;
			}
			HideActive(p.gui_obj);
			HideActiveGo(p.weapon_name);
			HideActiveGo(p.weapon_ammos);
			HideActiveGo(p.zeroingText);
			HideActiveGo(p.bleedingOutUi);
			HideActiveGo(p.vault_icon);
			HideActiveGo(p.doRoleNearbyicon);
			HideActiveGo(p.speaker_icon);
			// 死亡界面期间不补藏小队面板（否则死亡后无法选择其他小队成员）
			if (!IsDeathScreenActive())
			{
				HideActiveGo(p.squadGUI_Panel);
				HideActiveGo(p.squadData_Panel);
			}
		}

		/// <summary>死亡界面是否在显示中（死亡面板激活或受控角色已死）。</summary>
		internal static bool IsDeathScreenActive()
		{
			try
			{
				if (DeathPanel.instance != null && DeathPanel.instance.gameObject != null && DeathPanel.instance.gameObject.activeInHierarchy)
				{
					return true;
				}
				PlayerController pc = PlayerController.currentController;
				if (pc != null && pc.ControlledCharacter != null && pc.ControlledCharacter.IsDead)
				{
					return true;
				}
			}
			catch
			{
			}
			return false;
		}

		// ===== 5. 阶段条 =====
		// 优先走原生 PhaseBarGUI.SetVisible(false)（游戏自己维护 visibleFlag，LateUpdate 尊重它）；
		// 同时把组件 GameObject 藏掉作为兜底。
		// 0.9.15：PhaseBarGUI 在新版游戏中被移除——类型缺失时直接跳过（该类别无可隐藏对象）。
		internal static readonly ElementHider PhaseBar = new ElementHider();
		private static readonly MissLog PhaseBarLog = new MissLog("phase bar");

		internal static void SetPhaseBar(bool visible)
		{
			if (!Plugin.PhaseBarPresent) return; // 新版游戏已无阶段条
			if (visible)
			{
				PhaseBar.ShowAll();
				RestorePhaseBarNative();
				return;
			}
			try
			{
				var t = Plugin.PhaseBarType;
				var pbObj = t.GetField("instance").GetValue(null);
				if (pbObj != null)
				{
					AccessTools.Method(t, "SetVisible").Invoke(null, new object[] { false });
					// 反射实例无法满足 ElementHider 的 Component 泛型——转 Component 兜底
					PhaseBar.Hide(pbObj as UnityEngine.Component);
					PhaseBarLog.Done();
				}
				else
				{
					PhaseBarLog.Miss();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（PhaseBarGUI.LateUpdate Postfix 调用）。</summary>
		internal static void RehidePhaseBar()
		{
			try
			{
				var t = Plugin.PhaseBarType;
				if (t.GetField("instance").GetValue(null) != null)
				{
					AccessTools.Method(t, "SetVisible").Invoke(null, new object[] { false });
				}
			}
			catch
			{
			}
		}

		private static void RestorePhaseBarNative()
		{
			try
			{
				var t = Plugin.PhaseBarType;
				if (t.GetField("instance").GetValue(null) != null)
				{
					AccessTools.Method(t, "SetVisible").Invoke(null, new object[] { true });
				}
			}
			catch
			{
			}
		}

		// ===== 6. 目标指示（ObjectiveGUI）+ 任务状态面板（MissionStatusGUI）=====
		internal static readonly RefCache<ObjectiveGUI> objectiveCache = new RefCache<ObjectiveGUI>();
		internal static readonly ElementHider Objectives = new ElementHider();
		private static readonly MissLog ObjectivesLog = new MissLog("objective indicators");

		internal static void SetObjectives(bool visible)
		{
			if (visible)
			{
				Objectives.ShowAll();
				return;
			}
			try
			{
				bool did = false;
				ObjectiveGUI og = objectiveCache.Get();
				if (og != null && og.guiPanel != null)
				{
					did = Objectives.HideGo(og.guiPanel) || did;
				}
				MissionStatusGUI ms = MissionStatusGUI.instance;
				if (ms != null && ms.panel != null)
				{
					did = Objectives.HideGo(ms.panel) || did;
				}
				if (did)
				{
					ObjectivesLog.Done();
				}
				else
				{
					ObjectivesLog.Miss();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（ObjectiveGUI.LateUpdate Postfix 调用）。</summary>
		internal static void RehideObjectives()
		{
			ObjectiveGUI og = objectiveCache.Get();
			if (og != null && og.guiPanel != null && og.guiPanel.activeSelf)
			{
				og.guiPanel.SetActive(false);
			}
			MissionStatusGUI ms = MissionStatusGUI.instance;
			if (ms != null && ms.panel != null && ms.panel.activeSelf)
			{
				ms.panel.SetActive(false);
			}
		}

		// ===== 7. 地图与小地图（合并：小地图常驻角落 + M 键全屏地图）=====
		// 入口阻断在 UiPatches（MapGUI.OpenMap / MiniMapGUI.OpenMiniMap / OpenCloseMiniMap），
		// 这里负责把已显示/常驻的藏掉。
		internal static readonly RefCache<MapGUI> mapCache = new RefCache<MapGUI>();
		internal static readonly ElementHider Map = new ElementHider();
		private static readonly MissLog MapLog = new MissLog("map/minimap");

		internal static void SetMap(bool visible)
		{
			if (visible)
			{
				Map.ShowAll();
				return;
			}
			try
			{
				bool did = false;
				// 小地图（MiniMapGUI）
				MiniMapGUI mm = MiniMapGUI.Instance;
				if (mm != null)
				{
					did = Map.Hide(mm) || did;
					if (mm.miniMap != null)
					{
						did = Map.HideGo(mm.miniMap.gameObject) || did;
					}
					if (mm.detailsContainer != null)
					{
						did = Map.HideGo(mm.detailsContainer.gameObject) || did;
					}
					if (mm.mapName != null)
					{
						did = Map.Hide(mm.mapName) || did;
					}
					if (mm.playerArrow != null)
					{
						did = Map.HideGo(mm.playerArrow.gameObject) || did;
					}
				}
				// 全屏地图（MapGUI）
				MapGUI mg = mapCache.Get();
				if (mg != null)
				{
					if (mg.renderersParent != null)
					{
						did = Map.HideGo(mg.renderersParent.gameObject) || did;
					}
					did = Map.Hide(mg) || did;
				}
				if (did)
				{
					MapLog.Done();
				}
				else
				{
					MapLog.Miss();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（MiniMapGUI.Update Postfix 调用）。</summary>
		internal static void RehideMap()
		{
			MiniMapGUI mm = MiniMapGUI.Instance;
			if (mm != null)
			{
				HideActive(mm);
				HideActiveGo(mm.miniMap);
				HideActiveGo(mm.detailsContainer);
				HideActive(mm.mapName);
				HideActiveGo(mm.playerArrow);
			}
			MapGUI mg = mapCache.Get();
			if (mg != null)
			{
				HideActiveGo(mg.renderersParent);
				HideActive(mg);
			}
		}

		// ===== 8. 载具 HUD =====
		internal static readonly ElementHider Vehicle = new ElementHider();
		private static readonly MissLog VehicleLog = new MissLog("vehicle HUD");

		internal static void SetVehicle(bool visible)
		{
			if (visible)
			{
				Vehicle.ShowAll();
				return;
			}
			try
			{
				VehicleGUI vg = VehicleGUI.instance;
				if (vg == null)
				{
					VehicleLog.Miss();
					return;
				}
				bool did = false;
				if (vg.vehicleGUI != null)
				{
					did = Vehicle.HideGo(vg.vehicleGUI.gameObject) || did;
				}
				did = Vehicle.HideGo(vg.status_Panel) || did;
				did = Vehicle.HideGo(vg.ammos_Panel) || did;
				if (did)
				{
					VehicleLog.Done();
				}
				else
				{
					VehicleLog.Miss();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（VehicleGUI.Update Postfix 调用）。</summary>
		internal static void RehideVehicle()
		{
			VehicleGUI vg = VehicleGUI.instance;
			if (vg == null)
			{
				return;
			}
			HideActiveGo(vg.vehicleGUI);
			HideActiveGo(vg.status_Panel);
			HideActiveGo(vg.ammos_Panel);
		}

		// ===== 9. 命中反馈 + 准星（默认不随 F5 隐藏，用户可开）=====
		internal static readonly ElementHider Hitmarker = new ElementHider();
		private static readonly MissLog HitmarkerLog = new MissLog("hitmarker/crosshair");

		internal static void SetHitmarker(bool visible)
		{
			if (visible)
			{
				Hitmarker.ShowAll();
				return;
			}
			try
			{
				HitmarkerGUI hg = HitmarkerGUI.instance;
				if (hg == null)
				{
					HitmarkerLog.Miss();
					return;
				}
				bool did = Hitmarker.Hide(hg);
				did = Hitmarker.Hide(hg.hitmarker) || did;
				did = Hitmarker.Hide(hg.crosshair) || did;
				did = Hitmarker.Hide(hg.crosshair_dynamic) || did;
				if (did)
				{
					HitmarkerLog.Done();
				}
				else
				{
					HitmarkerLog.Miss();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（HitmarkerGUI.LateUpdate Postfix 调用）。</summary>
		internal static void RehideHitmarker()
		{
			HitmarkerGUI hg = HitmarkerGUI.instance;
			if (hg == null)
			{
				return;
			}
			HideActive(hg);
			HideActive(hg.hitmarker);
			HideActive(hg.crosshair);
			HideActive(hg.crosshair_dynamic);
		}

		// ===== 10. 受伤血屏（默认不随 F5 隐藏）=====
		internal static readonly ElementHider BloodSplash = new ElementHider();

		internal static void SetBloodSplash(bool visible)
		{
			if (visible)
			{
				BloodSplash.ShowAll();
				return;
			}
			try
			{
				BloodSplashGUI bg = BloodSplashGUI.instance;
				if (bg != null)
				{
					BloodSplash.Hide(bg);
				}
			}
			catch
			{
			}
		}

		// ===== 11. 聊天（默认不随 F5 隐藏）=====
		internal static readonly ElementHider Chat = new ElementHider();
		private static readonly MissLog ChatLog = new MissLog("chat");

		internal static void SetChat(bool visible)
		{
			if (visible)
			{
				Chat.ShowAll();
				return;
			}
			try
			{
				PlayerGUI p = PlayerGUI.instance;
				if (p == null || p.chatContent == null)
				{
					ChatLog.Miss();
					return;
				}
				if (Chat.HideGo(p.chatContent.gameObject))
				{
					ChatLog.Done();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（PlayerGUI.LateUpdate Postfix 调用）。</summary>
		internal static void RehideChat()
		{
			PlayerGUI p = PlayerGUI.instance;
			if (p != null && p.chatContent != null && p.chatContent.gameObject != null && p.chatContent.gameObject.activeSelf)
			{
				p.chatContent.gameObject.SetActive(false);
			}
		}

		// ===== 12. 瞄具界面（默认不随 F5 隐藏）=====
		internal static readonly ElementHider Scope = new ElementHider();
		private static readonly MissLog ScopeLog = new MissLog("scope overlay");

		internal static void SetScope(bool visible)
		{
			if (visible)
			{
				Scope.ShowAll();
				return;
			}
			try
			{
				ScopeGUI sg = ScopeGUI.instance;
				if (sg == null || sg.scopeRoot == null)
				{
					ScopeLog.Miss();
					return;
				}
				if (Scope.HideGo(sg.scopeRoot))
				{
					ScopeLog.Done();
				}
			}
			catch
			{
			}
		}

		// ===== 13. 随动标记（世界头顶标记 Marker3DGUI）=====
		internal static void SetWorldMarkers(bool visible)
		{
			try
			{
				Marker3DGUI m = Marker3DGUI.instance;
				if (m == null || m.container == null)
				{
					return;
				}
				m.container.gameObject.SetActive(visible);
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（Marker3DGUI.LateUpdate Postfix 调用）。</summary>
		internal static void RehideWorldMarkers()
		{
			try
			{
				Marker3DGUI m = Marker3DGUI.instance;
				if (m != null && m.container != null && m.container.gameObject != null && m.container.gameObject.activeSelf)
				{
					m.container.gameObject.SetActive(false);
				}
			}
			catch
			{
			}
		}

		// ===== 14. 杂项（版本水印/教程字幕任务/菜单通知/跳过提示）=====
		internal static readonly RefCache<VersionText> versionCache = new RefCache<VersionText>();
		internal static readonly ElementHider Misc = new ElementHider();
		private static readonly MissLog MiscLog = new MissLog("misc UI");

		internal static void SetMisc(bool visible)
		{
			if (visible)
			{
				Misc.ShowAll();
				return;
			}
			try
			{
				bool did = false;
				did = Misc.Hide(versionCache.Get()) || did;
				// 用公开的 GetInstance()（FindObjectOfType 找不到未激活对象）
				TutorialGUI tg = null;
				try
				{
					tg = TutorialGUI.GetInstance();
				}
				catch
				{
					tg = tutorialCache.Get();
				}
				if (tg != null)
				{
					did = Misc.HideGo(tg.captions_panel) || did;
					did = Misc.HideGo(tg.tasks_panel) || did;
				}
				if (did)
				{
					MiscLog.Done();
				}
				else
				{
					MiscLog.Miss();
				}
			}
			catch
			{
			}
		}

		/// <summary>每帧补藏（无独立 Update 的类，由巡检兜底；TutorialGUI.ShowTasks 已在 UiPatches 阻断）。</summary>
		internal static void RehideMisc()
		{
			HideActive(versionCache.Get());
			try
			{
				TutorialGUI tg = TutorialGUI.GetInstance();
				if (tg != null)
				{
					HideActiveGo(tg.captions_panel);
					HideActiveGo(tg.tasks_panel);
				}
			}
			catch
			{
			}
		}

		internal static readonly RefCache<TutorialGUI> tutorialCache = new RefCache<TutorialGUI>();

		// ===== 通用辅助 =====

		/// <summary>GameObject 若正显示则立即隐藏（无记录，供每帧补藏路径使用）。</summary>
		internal static void HideActiveGo(GameObject go)
		{
			if (go != null && go.activeSelf)
			{
				go.SetActive(false);
			}
		}

		/// <summary>组件所在 GameObject 若正显示则立即隐藏（无记录，供每帧补藏路径使用）。</summary>
		internal static void HideActiveGo(Component c)
		{
			if (c != null && c.gameObject != null && c.gameObject.activeSelf)
			{
				c.gameObject.SetActive(false);
			}
		}

		/// <summary>组件所在 GameObject 若正显示则立即隐藏（无记录）。</summary>
		internal static void HideActive(Component c)
		{
			HideActiveGo(c);
		}

		/// <summary>GameObject 若正显示则立即隐藏（无记录）。</summary>
		internal static void HideActive(GameObject go)
		{
			HideActiveGo(go);
		}
	}
}
