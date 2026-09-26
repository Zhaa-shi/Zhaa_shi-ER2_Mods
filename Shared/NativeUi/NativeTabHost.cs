using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2Shared.NativeUi
{
	/// <summary>
	/// 原生页签宿主：**借用游戏自己的页签切换**来实现征服页的进出。
	///
	/// 为什么必须这么做（2026-09-13 实测两轮）：
	///   · 第一轮：我逐帧写相机位置去"运镜" → **和原生主菜单的相机逻辑打架**，
	///     用户实测"相机转了一圈又回到原地"。
	///   · 同时：主菜单按钮没有隐藏 → 我的页面和原生按钮**重叠**（截图实证）。
	///
	/// 第二轮改用原生机制：调 `MainMenu.OpenTab(camaigns_tab, true)` ——
	/// 这正是玩家点「战役」时游戏自己走的路径，它会：
	///   ① 让**原生代码**把相机移到 `CAM_POS_campaigns`（不再打架）
	///   ② 原生地隐藏主菜单页签（不再重叠）
	///   ③ 播放原生过场（fadeAnim）
	///
	/// 然后把战役页的原生内容藏起来（它的子物体 + `CampaignsTree` 组件），
	/// 让位置让给我们的 IMGUI 页面（IMGUI 与页签的 active 状态无关，照常绘制）。
	/// 退出时还原，并 `OpenTab(mainMenu_tab)` 原生返回主菜单。
	///
	/// S6 侦察实证（`recon_latest.txt`）：
	///   cameraPositions 与页签按名字一一对应：
	///   CAM_POS_menu↔MainMenu / CAM_POS_settings↔SettingsMenu / **CAM_POS_campaigns↔CampaignMenu**
	///   / CAM_POS_credits / CAM_POS_statistics / CAM_POS_editor / CAM_POS_multiplayer
	/// </summary>
	public static class NativeTabHost
	{
		private static MainMenu _mm;
		private static GameObject _tab;                 // 借用的原生页签（camaigns_tab）
		private static bool _entered;

		private static readonly List<GameObject> _hiddenChildren = new List<GameObject>();
		private static Behaviour _treeComponent;        // CampaignsTree（藏内容时一并停用）
		private static bool _treeWasEnabled;

		private static float _nextWatchdog;
		private static string _lastError;

		/// <summary>是否已进入原生页签（诊断用）。</summary>
		public static bool Entered => _entered;

		/// <summary>进入：切到原生战役页 → 藏掉它的内容 → 让位给征服页。</summary>
		public static void Enter()
		{
			if (_entered) return;
			try
			{
				_mm = MainMenu.instance;
				if (_mm == null) { Warn("MainMenu.instance 为空，无法切页签"); return; }

				_tab = _mm.camaigns_tab;
				if (_tab == null) { Warn("camaigns_tab 为空"); return; }

				// ① 先藏原生内容（放在 OpenTab 之前：OpenTab 只激活页签根，
				//    不会把我们设成隐藏的子物体重新打开）
				HideNativeContent();

				// ② 原生切页签：相机移动 + 隐藏主菜单 + 过场，全部由原生代码完成
				_mm.OpenTab(_tab, true);

				_entered = true;
				NativeUiLog.Inf("已切入原生战役页签（相机由原生接管，运镜不再打架）");
			}
			catch (Exception ex)
			{
				Warn("切入原生页签失败: " + ex.Message);
				// 失败也要保证可用：至少把主菜单藏掉，避免重叠
				try { if (_mm != null && _mm.mainMenu_tab != null) _mm.mainMenu_tab.SetActive(false); } catch { }
			}
		}

		/// <summary>退出：还原原生内容 → 原生返回主菜单页签。</summary>
		public static void Exit()
		{
			if (!_entered) return;
			try
			{
				RestoreNativeContent();

				if (_mm != null && _mm.mainMenu_tab != null)
					_mm.OpenTab(_mm.mainMenu_tab, true);   // 原生返回：相机移回 CAM_POS_menu
				else if (_mm != null)
					_mm.CloseAllTabsNow();

				NativeUiLog.Inf("已从原生战役页签返回主菜单");
			}
			catch (Exception ex)
			{
				Warn("返回主菜单失败: " + ex.Message);
				try { if (_mm != null && _mm.mainMenu_tab != null) _mm.mainMenu_tab.SetActive(true); } catch { }
			}
			finally
			{
				_entered = false;
				_tab = null;
				_mm = null;
			}
		}

		/// <summary>
		/// 看门狗（由 Update 节流调用）：原生 `CampaignsTree` 会自己刷新并重新显示内容，
		/// 所以只要页面开着，就持续把原生内容压住（同 Hide Anything 的 Enforce 思路）。
		/// </summary>
		public static void Enforce()
		{
			if (!_entered) return;
			try
			{
				if (Time.unscaledTime < _nextWatchdog) return;
				_nextWatchdog = Time.unscaledTime + 0.5f;

				// 主菜单页签若被原生重新打开，压回去（否则又重叠）
				if (_mm != null && _mm.mainMenu_tab != null && _mm.mainMenu_tab.activeSelf)
					_mm.mainMenu_tab.SetActive(false);

				// 原生战役内容若被重新显示，再藏一次
				for (int i = 0; i < _hiddenChildren.Count; i++)
				{
					GameObject go = _hiddenChildren[i];
					if (go != null && go.activeSelf) go.SetActive(false);
				}
			}
			catch { }
		}

		// ================= 内容隐藏/还原 =================

		private static void HideNativeContent()
		{
			_hiddenChildren.Clear();
			if (_tab == null) return;

			try
			{
				// 停用 CampaignsTree：否则它会异步重建战役列表（白耗性能，且可能把内容重新显示）
				_treeComponent = null;
				_treeWasEnabled = false;
				try
				{
					CampaignsTree tree = _tab.GetComponent<CampaignsTree>();
					if (tree != null)
					{
						_treeComponent = tree;
						_treeWasEnabled = tree.enabled;
						tree.enabled = false;
					}
				}
				catch (Exception ex) { Warn("停用 CampaignsTree 失败: " + ex.Message); }

				// 藏掉页签下的所有子物体（只记录进入前是激活的，退出时只还原那些）
				Transform t = _tab.transform;
				int n = t.childCount;
				for (int i = 0; i < n; i++)
				{
					Transform ch = null;
					try { ch = t.GetChild(i); } catch { }
					if (ch == null) continue;
					GameObject go = ch.gameObject;
					if (go == null) continue;
					if (go.activeSelf)
					{
						_hiddenChildren.Add(go);
						go.SetActive(false);
					}
				}

				NativeUiLog.Inf("已隐藏原生战役页内容（子物体 " + _hiddenChildren.Count + " 个 + CampaignsTree 组件）");
			}
			catch (Exception ex) { Warn("隐藏原生内容失败: " + ex.Message); }
		}

		private static void RestoreNativeContent()
		{
			try
			{
				for (int i = 0; i < _hiddenChildren.Count; i++)
				{
					GameObject go = _hiddenChildren[i];
					if (go != null) go.SetActive(true);
				}
			}
			catch { }
			_hiddenChildren.Clear();

			try
			{
				if (_treeComponent != null) _treeComponent.enabled = _treeWasEnabled;
			}
			catch { }
			_treeComponent = null;
		}

		private static void Warn(string msg)
		{
			if (_lastError == msg) return;
			_lastError = msg;
			NativeUiLog.Err("" + msg);
		}

		/// <summary>诊断串。</summary>
		public static string Describe()
		{
			if (!_entered) return "未进入原生页签";
			return "原生页签生效（借 camaigns_tab，隐藏子物体 " + _hiddenChildren.Count + " 个）";
		}
	}
}
