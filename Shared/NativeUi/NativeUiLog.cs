using System;

namespace ER2Shared.NativeUi
{
	/// <summary>
	/// 原生 UI 适配层的日志出口。
	///
	/// 本层不引用 BepInEx，因此不能用 <c>Plugin.Log</c>；由宿主插件在 Load() 时注入：
	/// <code>
	/// NativeUiLog.Info  = s => { ModLog.LogInfo(s);  FileLog(s); };
	/// NativeUiLog.Error = s => { ModLog.LogError(s); FileLog("[E] " + s); };
	/// </code>
	///
	/// 未注入时静默——适配层自身绝不能因为日志而炸功能。
	/// <c>Tag</c> 用于给日志加宿主前缀（如 "[Endless]"），便于多 mod 共存时区分来源。
	/// </summary>
	public static class NativeUiLog
	{
		/// <summary>日志前缀，形如 "[MyMod]"。由宿主设置。</summary>
		public static string Tag = "";

		/// <summary>错误输出（未注入时静默）。</summary>
		public static Action<string> Error;

		/// <summary>信息输出（未注入时静默）。</summary>
		public static Action<string> Info;

		public static void Err(string msg)
		{
			try { Error?.Invoke(Tag + msg); } catch { }
		}

		public static void Inf(string msg)
		{
			try { Info?.Invoke(Tag + msg); } catch { }
		}
	}
}
