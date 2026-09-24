using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;

namespace ER2NoInteractionHints
{
	/// <summary>
	/// 跨 mod UI 联动契约（v4，公开 API，供其他 mod 通过反射调用，无编译期依赖）。
	///
	/// 用法（其他 mod）：在"要不要显示自己的 UI"时查询
	///   HudCompat.IsHudHidden(id, displayName)
	/// 返回 true  = 玩家在该 mod 的开关上打了勾（始终隐藏）→ 隐藏你的 UI；
	/// 返回 false = 正常显示。
	/// 首次查询会自动为该 id 建立配置项（默认不勾选），ModManager 里可直接勾选。
	/// 每帧查询开销可接受（缓存 ConfigEntry，热读取）。
	///
	/// 兼容惯例：插件程序集中名为 HudEnabled / HudVisible / ShowHud 的 static bool 字段
	/// 会被自动发现（默认不勾选），勾选后写 false 隐藏；未锁定时若被外部改回 true
	/// （重新显示）则自动取消勾选，锁定时强制再写 false。
	/// </summary>
	public static class HudCompat
	{
		private sealed class Entry
		{
			internal string Id;
			internal string DisplayName;
			internal ConfigEntry<bool> Cfg; // 勾选 = 隐藏（严格）
			internal FieldInfo Field; // 自动发现的字段（写入口）；null = 纯查询型条目
		}

		internal const string SectionName = "Hide Other Mods UI";

		/// <summary>由 Plugin.Load 注入的配置。null = 未就绪。</summary>
		internal static ConfigFile Cfg;

		private static readonly Dictionary<string, Entry> entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

		// 本工作区生态 mod 的显示名（对方只传 id 时也能显示正确名字）。
		// 预注册表：装了就立即在 Mod Manager 显示开关（无需等对方首次查询）。
		// 注意：新接入 NoHintsHudLink 的 mod 必须加在这里，否则开关要等进战斗后懒注册才出现。
		private static readonly Dictionary<string, string> knownNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
		{
			{ "er2.limbtweaks", "ER2 Limb Tweaks" },
			{ "er2.weathercontrol", "ER2 Weather Control" },
			{ "er2.healthbars", "ER2 Health Bars" },
			{ "er2.highvaluetarget", "ER2 Veteran HVT" },
			{ "er2.unitinfooverlay", "ER2 Unit Inspector" },
			{ "er2.conquest", "ER2 Conquest" }
		};

		// 自动发现的惯例字段名（精确匹配）
		private static readonly string[] discoveredFieldNames = { "HudEnabled", "HudVisible", "ShowHud" };

		private static int discoveryPasses;

		private static int preregisterPasses;

		/// <summary>预注册与发现扫描是否都已跑满 2 遍（Load + 首帧）。供初始化 ticker 判断何时自毁。</summary>
		internal static bool InitComplete => Cfg != null && discoveryPasses >= 2 && preregisterPasses >= 2;

		/// <summary>查询 id 对应 UI 当前是否应隐藏（勾选 = 始终隐藏）。首次查询自动注册（默认不勾选）。未就绪/未知时安全返回 false。</summary>
		public static bool IsHudHidden(string id, string displayName = null)
		{
			if (string.IsNullOrEmpty(id) || Cfg == null || Plugin.enabled == null)
			{
				return false;
			}
			Entry e = EnsureEntry(id, displayName, false);
			if (e == null || e.Cfg == null)
			{
				return false;
			}
			return Plugin.enabled.Value && e.Cfg.Value;
		}

		/// <summary>
		/// 轮询巡检（节流由调用方控制）：惯例字段条目——被勾选时强制保持隐藏（勾选即锁定，
		/// 字段被外部改回 true 立即再写 false）。
		/// </summary>
		internal static void Enforce()
		{
			foreach (Entry e in entries.Values)
			{
				if (e.Field == null || e.Cfg == null || !e.Cfg.Value)
				{
					continue;
				}
				try
				{
					if (!(bool)e.Field.GetValue(null))
					{
						continue; // 仍是隐藏态
					}
					e.Field.SetValue(null, false);
				}
				catch
				{
				}
			}
		}

		/// <summary>
		/// 扫描插件程序集中的惯例字段（HudEnabled/HudVisible/ShowHud）。最多跑 2 次：
		/// 第 1 次在 Load（此时部分插件可能尚未加载），第 2 次在战斗第一帧（全部加载完毕）。
		/// </summary>
		internal static void DiscoverFields()
		{
			if (discoveryPasses >= 2 || Cfg == null)
			{
				return;
			}
			discoveryPasses++;
			Assembly self = typeof(HudCompat).Assembly;
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (a == self)
				{
					continue;
				}
				string an = a.GetName().Name;
				if (an == null || IsFrameworkAssembly(an))
				{
					continue;
				}
				Type[] types = SafeGetTypes(a);
				if (types == null || !AssemblyHasPlugin(types))
				{
					continue;
				}
				foreach (Type t in types)
				{
					if (t == null || (!t.IsClass && !t.IsValueType))
					{
						continue;
					}
					FieldInfo[] fields;
					try
					{
						fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
					}
					catch
					{
						continue;
					}
					if (fields == null)
					{
						continue;
					}
					foreach (FieldInfo f in fields)
					{
						if (f.FieldType != typeof(bool) || !IsDiscoveredName(f.Name))
						{
							continue;
						}
						string id = "ui." + an + "." + t.Name + "." + f.Name;
						if (entries.ContainsKey(id))
						{
							continue;
						}
						Entry e = EnsureEntry(id, an + " (" + f.Name + ")", false);
						if (e != null)
						{
							e.Field = f;
						}
					}
				}
			}
		}

		/// <summary>
		/// 预注册：为"已安装"的本生态 mod（按 knownNames 的 GUID 扫描插件程序集）立即建立配置项，
		/// 让 ModManager 页面无需等首次查询就显示全部开关。未知/未来 mod 仍走懒注册（首次查询）。
		/// 幂等，最多跑 2 遍（Load + 首帧）：插件加载顺序不保证，首帧时全部插件已就位。
		/// </summary>
		internal static void PreregisterKnownMods()
		{
			if (Cfg == null || preregisterPasses >= 2)
			{
				return;
			}
			preregisterPasses++;
			HashSet<string> installed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			Assembly self = typeof(HudCompat).Assembly;
			foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
			{
				if (a == self)
				{
					continue;
				}
				string an = a.GetName().Name;
				if (an == null || IsFrameworkAssembly(an))
				{
					continue;
				}
				Type[] types = SafeGetTypes(a);
				if (types == null)
				{
					continue;
				}
				foreach (Type t in types)
				{
					if (t == null)
					{
						continue;
					}
					try
					{
						BepInPlugin attr = t.GetCustomAttribute<BepInPlugin>(false);
						if (attr != null && attr.GUID != null && knownNames.ContainsKey(attr.GUID))
						{
							installed.Add(attr.GUID);
						}
					}
					catch
					{
					}
				}
			}
			foreach (KeyValuePair<string, string> kv in knownNames)
			{
				if (installed.Contains(kv.Key))
				{
					EnsureEntry(kv.Key, kv.Value, false);
				}
			}
			if (Plugin.ModLog != null)
			{
				Plugin.ModLog.LogInfo((object)("NIH: preregister pass " + preregisterPasses + " -> " + installed.Count + " known mod(s) found."));
			}
		}

		private static Entry EnsureEntry(string id, string displayName, bool defaultOn)
		{
			if (entries.TryGetValue(id, out Entry e))
			{
				return e;
			}
			string name = displayName;
			if (string.IsNullOrEmpty(name))
			{
				knownNames.TryGetValue(id, out name);
			}
			if (string.IsNullOrEmpty(name))
			{
				name = id;
			}
			ConfigEntry<bool> cfg = Cfg.Bind(SectionName, id, defaultOn, "Checked = this mod's UI hides when you press the hide-HUD key (unchecked = its UI stays visible).");
			e = new Entry { Id = id, DisplayName = name, Cfg = cfg };
			entries[id] = e;
			return e;
		}

		private static bool IsDiscoveredName(string name)
		{
			foreach (string fn in discoveredFieldNames)
			{
				if (name == fn)
				{
					return true;
				}
			}
			return false;
		}

		private static bool IsFrameworkAssembly(string name)
		{
			foreach (string prefix in new string[] { "System.", "Microsoft.", "Unity", "Il2Cpp", "Mono.", "mscorlib", "netstandard", "BepInEx", "HarmonyLib", "0Harmony", "Newtonsoft", "MonoMod", "Il2CppInterop", "Assembly-CSharp" })
			{
				if (name.StartsWith(prefix, StringComparison.Ordinal))
				{
					return true;
				}
			}
			return false;
		}

		private static Type[] SafeGetTypes(Assembly a)
		{
			try
			{
				return a.GetTypes();
			}
			catch (ReflectionTypeLoadException ex)
			{
				return ex.Types;
			}
			catch
			{
				return null;
			}
		}

		private static bool AssemblyHasPlugin(Type[] types)
		{
			foreach (Type t in types)
			{
				if (t == null)
				{
					continue;
				}
				try
				{
					if (t.GetCustomAttribute<BepInPlugin>(false) != null)
					{
						return true;
					}
				}
				catch
				{
				}
			}
			return false;
		}
	}
}
