namespace ER2Shared.NativeUi
{
	/// <summary>
	/// 原生 UI 适配层的宿主配置。
	///
	/// 宿主插件在 Load() 时设置一次即可：
	/// <code>
	/// NativeUiConfig.Chinese = Plugin.DefaultChinese;   // CN_BUILD 构建为 true
	/// NativeUiLog.Tag        = "[MyMod] ";
	/// </code>
	///
	/// 本层刻意不引用任何具体插件的类型（如 Plugin.DefaultChinese），
	/// 这样同一份源码可以被任意 mod 以 &lt;Compile Include&gt; 链接复用。
	/// </summary>
	public static class NativeUiConfig
	{
		/// <summary>
		/// 界面语言是否用中文。影响日期月份等原生排版文案的选择。
		/// 默认 false（英文）。
		/// </summary>
		public static bool Chinese = false;
	}
}
