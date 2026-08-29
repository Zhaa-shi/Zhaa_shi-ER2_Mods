using System;
using System.Reflection;

namespace ER2Shared
{
	/// <summary>
	/// ER2 No Interaction Hints v2 联动辅助（反射调用 HudCompat，无编译期依赖；
	/// 对方缺失/未就绪时静默返回"不隐藏"，本 mod 独立运行不受影响）。
	///
	/// 用法：在"要不要显示自己的 UI"时查询
	///   NoHintsHudLink.IsHidden("er2.你的modid", "显示名")
	/// 返回 true = 用户按了 F5 隐藏 HUD 且该 mod 的独立开关开着 → 隐藏你的 UI。
	/// 首次查询会自动在 NoInteractionHints 的配置里建立该 mod 的独立开关（默认开），
	/// 玩家可在 ModManager → No Interaction Hints → Hide Other Mods UI 里逐 mod 关闭。
	/// 每帧查询开销可接受（缓存 MethodInfo + 热读取）。
	/// </summary>
	public static class NoHintsHudLink
	{
		private static Assembly cachedAsm;

		private static MethodInfo cachedIsHidden;

		private static bool lookupDone;

		private static void EnsureLookup()
		{
			if (lookupDone)
			{
				return;
			}
			lookupDone = true;
			try
			{
				foreach (Assembly a in AppDomain.CurrentDomain.GetAssemblies())
				{
					string n = a.GetName().Name;
					if (n != null && n.IndexOf("NoInteractionHints", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						cachedAsm = a;
						break;
					}
				}
				if (cachedAsm == null)
				{
					return;
				}
				Type t = cachedAsm.GetType("ER2NoInteractionHints.HudCompat");
				if (t == null)
				{
					return;
				}
				cachedIsHidden = t.GetMethod("IsHudHidden", new Type[] { typeof(string), typeof(string) });
			}
			catch
			{
				cachedIsHidden = null;
			}
		}

		/// <summary>id 对应 UI 当前是否应隐藏（F5 关闭且该 mod 的开关打开）。NoInteractionHints 缺失时返回 false。</summary>
		public static bool IsHidden(string id, string displayName = null)
		{
			try
			{
				EnsureLookup();
				if (cachedIsHidden == null)
				{
					return false;
				}
				object r = cachedIsHidden.Invoke(null, new object[] { id, displayName ?? id });
				return r is bool b && b;
			}
			catch
			{
				return false;
			}
		}
	}
}
