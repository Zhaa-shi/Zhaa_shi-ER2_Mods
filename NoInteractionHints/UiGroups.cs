using System;
using System.Collections.Generic;
using BepInEx.Configuration;

namespace ER2NoInteractionHints
{
	/// <summary>
	/// UI 分类注册表（v4.3，ER2 Hide Anything）：每类一个勾选开关，勾选 = 始终隐藏且严格生效
	/// （游戏/其他机制重新显示会被立即再次隐藏）——勾选即锁定，无需单独的锁定按钮。
	/// 默认全部不勾选。
	/// </summary>
	public static class UiGroups
	{
		internal const string SectionName = "Hide UI";

		private sealed class Group
		{
			internal string Id;
			internal string DisplayName;
			internal ConfigEntry<bool> Cfg; // 勾选 = 隐藏（严格）
			internal Action<bool> SetVisible; // true=显示，false=隐藏（立即）
		}

		private static readonly Dictionary<string, Group> groups = new Dictionary<string, Group>(StringComparer.OrdinalIgnoreCase);

		/// <summary>由 Plugin.Load 注入。null = 未就绪。</summary>
		internal static ConfigFile Cfg;

		/// <summary>注册一个 UI 类别。setVisible(true)=显示，setVisible(false)=立即隐藏。幂等。</summary>
		public static void Register(string id, string displayName, Action<bool> setVisible)
		{
			if (string.IsNullOrEmpty(id) || setVisible == null || Cfg == null || groups.ContainsKey(id))
			{
				return;
			}
			ConfigEntry<bool> entry = Cfg.Bind(SectionName, id, false, "Checked = this UI is always hidden (strictly enforced).");
			groups[id] = new Group { Id = id, DisplayName = displayName, Cfg = entry, SetVisible = setVisible };
		}

		/// <summary>类别当前是否应隐藏（该类别被勾选）。供一次性显示的 patch 查询。</summary>
		public static bool IsHidden(string id)
		{
			if (Plugin.enabled == null || !Plugin.enabled.Value)
			{
				return false;
			}
			return groups.TryGetValue(id, out Group g) && g.Cfg != null && g.Cfg.Value;
		}

		/// <summary>加载时应用一次：把所有被勾选的类别立即隐藏（元素不存在的场景由巡检补藏）。</summary>
		internal static void ApplyAll()
		{
			foreach (Group g in groups.Values)
			{
				if (g.Cfg == null || !g.Cfg.Value)
				{
					continue;
				}
				try
				{
					g.SetVisible(false);
				}
				catch
				{
				}
			}
		}

		/// <summary>轮询巡检（节流由调用方控制）：被勾选的类别强制保持隐藏（勾选即锁定）。</summary>
		internal static void Enforce()
		{
			if (Plugin.enabled == null || !Plugin.enabled.Value)
			{
				return;
			}
			foreach (Group g in groups.Values)
			{
				if (g.Cfg == null || !g.Cfg.Value)
				{
					continue;
				}
				try
				{
					g.SetVisible(false);
				}
				catch
				{
				}
			}
		}
	}
}
