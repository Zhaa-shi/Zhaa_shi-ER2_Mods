using System;
using UnityEngine;
using UnityEngine.UI;

namespace ER2Shared.NativeUi
{
	/// <summary>
	/// 输入拦截器：征服页打开时，**挡住原生 uGUI 的点击**。
	///
	/// 为什么需要它（用户实测报告："面板弹出来后点击还会点到原生那些按钮"）：
	///   IMGUI（OnGUI）与 uGUI 是**两套完全独立的输入系统**。
	///   我的征服页画在原生界面之上，但原生按钮的点击由 `EventSystem` + `GraphicRaycaster`
	///   处理——它只认 uGUI 图元，**看不见 IMGUI 画了什么**。于是点击"穿过"我的面板，
	///   落到下面的原生按钮上。
	///
	/// 解法：在**最高 sortingOrder** 的独立 Canvas 上放一张全屏 Image（raycastTarget=true，
	/// alpha≈0 不可见）。它成为射线检测命中的最上层图元 → 原生按钮收不到点击；
	/// 而 IMGUI 走自己的输入通道，我的按钮照常工作。
	///
	/// 只在页面打开时激活，关闭即隐藏——绝不干扰正常游玩。
	/// </summary>
	public static class InputBlocker
	{
		private static GameObject _root;
		private static bool _active;

		/// <summary>开关拦截。pageOpen = true 时挡住原生输入。</summary>
		public static void Set(bool pageOpen)
		{
			try
			{
				if (pageOpen == _active) return;

				if (pageOpen)
				{
					EnsureCreated();
					if (_root != null) { _root.SetActive(true); _active = true; }
				}
				else
				{
					if (_root != null) { _root.SetActive(false); _active = false; }
				}
			}
			catch (Exception ex)
			{
				NativeUiLog.Err("输入拦截器切换失败: " + ex.Message);
			}
		}

		private static void EnsureCreated()
		{
			if (_root != null) return;

			_root = new GameObject("ER2ConquestInputBlocker");
			UnityEngine.Object.DontDestroyOnLoad(_root);
			_root.hideFlags = (HideFlags)61;   // 跨场景存活（陷阱 12）

			Canvas canvas = _root.AddComponent<Canvas>();
			canvas.renderMode = RenderMode.ScreenSpaceOverlay;
			// 拉到最高层：原生菜单 Canvas 的 sortingOrder 通常是 0 或个位数，
			// 这里给一个足够大的值，保证我们永远在它上面（S6 侦察段会 dump 原生 Canvas 的实际值）
			canvas.sortingOrder = 30000;

			// 需要 raycaster 才会参与射线检测
			_root.AddComponent<GraphicRaycaster>();

			GameObject imgGo = new GameObject("Blocker");
			imgGo.transform.SetParent(_root.transform, false);

			RectTransform rt = imgGo.AddComponent<RectTransform>();
			// 全屏拉伸
			rt.anchorMin = Vector2.zero;
			rt.anchorMax = Vector2.one;
			rt.offsetMin = Vector2.zero;
			rt.offsetMax = Vector2.zero;

			Image img = imgGo.AddComponent<Image>();
			// 几乎全透明但**必须 raycastTarget=true**——Unity 的 Graphic 射线检测
			// 只看 raycastTarget 与矩形，默认不看 alpha（alphaHitTestMinimumThreshold 才看，
			// 且那需要贴图可读）。所以 alpha 0 也能挡住点击。
			img.color = new Color(0f, 0f, 0f, 0.003f);
			img.raycastTarget = true;

			NativeUiLog.Inf("输入拦截层已创建（sortingOrder=" + canvas.sortingOrder + "）");
		}

		/// <summary>诊断串。</summary>
		public static string Describe()
		{
			if (_root == null) return "未创建";
			return (_active ? "生效中" : "待命") + " sortingOrder=30000";
		}
	}
}
