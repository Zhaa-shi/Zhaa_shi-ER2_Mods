using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Corvostudio.SettingsData;
using HarmonyLib;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace ER2ModManager;

[BepInPlugin("er2.modmanager", "ER2 Mod Manager", "1.2.3")]
public class Plugin : BasePlugin
{
	/// <summary>构建语言：CN_BUILD 编译符号 = 中文版（标签中文、页标题"模组"）；否则英文版。</summary>
#if CN_BUILD
	internal const bool DefaultChinese = true;
#else
	internal const bool DefaultChinese = false;
#endif

	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> enabled;

	/// <summary>原生 UI 重写 POC 开关（渲染 4 行原生 SettingSelectable 验证）。</summary>
	internal static ConfigEntry<bool> nativePoc;

	/// <summary>原生 UI 完整页面开关（整页用原生 SettingSelectable 渲染）。</summary>
	internal static ConfigEntry<bool> nativeFull;

	internal static Harmony HarmonyInstance;

	public override void Load()
	{
		ModLog = Log;
		enabled = Config.Bind("General", "enabled", true, "Master switch for the Mod Manager page (restart required).");
		nativePoc = Config.Bind("General", "NativePoc", false, "POC: render test rows via native SettingSelectable.Create (dev only).");
		nativeFull = Config.Bind("General", "NativeFull", false, "DEV: render the whole MODS page via native SettingSelectable rows.");
		HarmonyInstance = new Harmony("er2.modmanager");
		HarmonyInstance.PatchAll(GetType().Assembly);
		ModLog.LogInfo((object)"ER2 Mod Manager 1.2.3 loaded.");
	}
}


/// <summary>
/// 把 "Mods" 页注入游戏原生设置界面（SettingsGUI_V2）：
///  - settingsMenus 数组末尾追加一个 SettingsPageData（pageName_id = "MODS"）
///  - 用户翻页到最后一页时由我们接管填充（原生 Setting* 组件动态创建，观感与原生一致）
///  - 页面内容是所有已加载 BepInEx 插件的配置项（Toggle/Slider/Dropdown），修改即时写回 cfg
/// </summary>
public static class ModRegistry
{
	internal static int myIndex = -1;

	private const string MyPageId = "MODS";

	/// <summary>把文本转成可读标题：分隔符/大小写/数字字母边界拆词，每词首字母大写其余小写
	/// （enableAIVaulting → Enable Ai Vaulting；AAMult → Aa Mult；Friendly Fire → Friendly Fire）。
	/// 供配置项标签、分区名、下拉选项值的统一兜底显示。</summary>
	internal static string HumanizeKey(string key)
	{
		if (string.IsNullOrEmpty(key))
		{
			return key ?? "";
		}
		// 1. 拆词：分隔符（含空格）+ 大小写边界 + 数字↔字母边界
		System.Collections.Generic.List<string> words = new System.Collections.Generic.List<string>();
		System.Text.StringBuilder cur = new System.Text.StringBuilder();
		for (int i = 0; i < key.Length; i++)
		{
			char c = key[i];
			if (c == '_' || c == '-' || c == '.' || c == ' ')
			{
				if (cur.Length > 0)
				{
					words.Add(cur.ToString());
					cur.Length = 0;
				}
				continue;
			}
			if (cur.Length > 0)
			{
				char prev = key[i - 1];
				char next = (i + 1 < key.Length) ? key[i + 1] : '\0';
				bool boundary = false;
				if (char.IsLower(prev) && char.IsUpper(c))
				{
					boundary = true; // aV
				}
				else if (char.IsUpper(prev) && char.IsUpper(c) && char.IsLower(next))
				{
					boundary = true; // AIV → AI | V
				}
				else if (char.IsDigit(prev) != char.IsDigit(c))
				{
					boundary = true; // a1 / 1a
				}
				if (boundary)
				{
					words.Add(cur.ToString());
					cur.Length = 0;
				}
			}
			cur.Append(c);
		}
		if (cur.Length > 0)
		{
			words.Add(cur.ToString());
		}
		// 2. 每词 Title Case：首字母大写，其余小写
		System.Text.StringBuilder result = new System.Text.StringBuilder();
		foreach (string w in words)
		{
			if (w.Length == 0)
			{
				continue;
			}
			if (result.Length > 0)
			{
				result.Append(' ');
			}
			result.Append(char.ToUpper(w[0]));
			if (w.Length > 1)
			{
				result.Append(w.Substring(1).ToLowerInvariant());
			}
		}
		return result.ToString();
	}

	/// <summary>检查/执行注入（幂等，场景重载后自动重新注入）。</summary>
	internal static void EnsureInjected(SettingsGUI_V2 s)
	{
		try
		{
			Il2CppReferenceArray<SettingsPageData> arr = s.settingsMenus;
			if (arr == null || arr.Length == 0)
			{
				return;
			}
			SettingsPageData last = arr[arr.Length - 1];
			if (last != null && last.pageName_id == MyPageId)
			{
				myIndex = arr.Length - 1;
				return;
			}
			// 首次注入（或场景重载后重新注入）
			Il2CppReferenceArray<SettingsPageData> na = new Il2CppReferenceArray<SettingsPageData>(arr.Length + 1);
			for (int i = 0; i < arr.Length; i++)
			{
				na[i] = arr[i];
			}
			SettingsPageData pd = new SettingsPageData((SettingsPage)999);
			pd.pageName_id = MyPageId;
			na[arr.Length] = pd;
			s.settingsMenus = na;
			myIndex = arr.Length;
			Plugin.ModLog.LogInfo((object)("ModManager: injected '" + MyPageId + "' page at index " + myIndex + " (total " + na.Length + " pages)."));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager inject error: " + ex.Message));
		}
	}

	/// <summary>v1.1.4：翻页进入 MODS 页的唯一入口（TabRight/TabLeft 调用）。
	/// 补原生点击音效（拦截原生翻页后原生不再播音效）。**不做防抖吞掉**——连点时每次都必须
	/// 执行 OpenMyPage（它开头就设 currentOpenedMenu=mods 页并停掉原生填充协程）；吞掉反而把
	/// cur 留在旧页，后续点击错位、原生协程继续灌行 → "连点两页以上列表不显示"。
	/// v1.1.5：进入时滚动回顶；页内展开/收起重建（ToggleMod 等直调 OpenMyPage）不回顶。</summary>
	internal static void EnterMyPage(SettingsGUI_V2 s)
	{
		try
		{
			SoundManager.ClickSound();
		}
		catch
		{
		}
		OpenMyPage(s);
		ResetScrollTop(s.contentPage);
	}

	/// <summary>接管并填充我们的页面（用户翻到最后一页时由 TabRightPatch 调用）。</summary>
	internal static void OpenMyPage(SettingsGUI_V2 s)
	{
		// 重开设置/重建页面时兜底落盘上次的暂存改动（等效"退出即保存"）
		FlushAllStaged();
		SettingsGUI_V2.currentOpenedMenu = myIndex;
		ClearWatchesAndFlush();
		// 停掉原生填充协程：新设置页的 FillSettingPage 是异步协程，晚到时会把原生控件
		// 灌进我们的页面 → 与我们的每帧清理形成拉锯（页面反复重建/闪烁）
		try
		{
			if (s.fillingRoutine != null)
			{
				s.StopCoroutine(s.fillingRoutine);
				s.fillingRoutine = null;
			}
		}
		catch
		{
		}
		ClearContent(s.contentPage);
		// v1.2.3：重建时重置行内缩锁存（按当前几何重新测，见 ApplyScrollbarInset）
		ResetInsetLatch();
		// v1.1.9：先确保 Content 链可见再填充——快速右翻连点会把链停用，建在停用父级下的
		// 容器永不渲染/布局（STUCK-EVIDENCE 实锤：childCount=42 size=442x100 active=False）
		EnsureContentVisible(s);
		float y = FillContent(s.contentPage);
		// 立即强制修复一次滚动高度，避免长列表打开瞬间只有第一行、要等 0.5s 巡检才恢复。
		SelfHealScroll(s.contentPage, true);
		try
		{
			if (s.title != null)
			{
				s.title.text = Plugin.DefaultChinese ? "模组设置" : "Mod Settings";
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.2.1：MODS 页（末页）右翻绕回第一页。**原生调用必须走反射**——游戏
	/// 2026-09-12 更新把 `SettingsGUI_V2.UpdateOpenedMenu(bool)` 改成
	/// `UpdateOpenedMenu(bool setFirstButtonSeected, bool preservePosition)`：直接写调用，
	/// 编译期令牌在运行时找不到方法 → MissingMethodException 从 Prefix 逃逸（连 try/catch
	/// 都拦不住，异常发生在 JIT 解析调用点时）→ 整个 Prefix 失败 → 原生 SettingsTabRight
	/// 也一起中断 = 用户报的"按右翻页键不能翻页"。反射按名 + 参数个数自适应，将来再加
	/// 参数也不会复发。</summary>
	internal static void WrapToFirstPage(SettingsGUI_V2 s)
	{
		try
		{
			SoundManager.ClickSound();
		}
		catch
		{
		}
		SettingsGUI_V2.currentOpenedMenu = 0;
		CallUpdateOpenedMenu(s, true, false);
	}

	/// <summary>版本容错调用原生 UpdateOpenedMenu：参数个数自适应（1 参=旧版 / 2 参=2026-09-12 起）。</summary>
	internal static bool CallUpdateOpenedMenu(SettingsGUI_V2 s, bool setFirstButtonSelected, bool preservePosition)
	{
		try
		{
			if (s == null || s.Equals(null))
			{
				return false;
			}
			System.Reflection.MethodInfo mi = FindUpdateOpenedMenu();
			if (mi == null)
			{
				Plugin.ModLog.LogWarning((object)"ModManager: native SettingsGUI_V2.UpdateOpenedMenu not found in this game build.");
				return false;
			}
			System.Reflection.ParameterInfo[] ps = mi.GetParameters();
			object[] args = (ps != null && ps.Length >= 2)
				? new object[2] { setFirstButtonSelected, preservePosition }
				: new object[1] { setFirstButtonSelected };
			mi.Invoke(s, args);
			return true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager: UpdateOpenedMenu invoke failed: " + ex.Message));
			return false;
		}
	}

	private static System.Reflection.MethodInfo cachedUpdateOpenedMenu;

	private static bool searchedUpdateOpenedMenu;

	private static System.Reflection.MethodInfo FindUpdateOpenedMenu()
	{
		if (searchedUpdateOpenedMenu)
		{
			return cachedUpdateOpenedMenu;
		}
		searchedUpdateOpenedMenu = true;
		try
		{
			System.Reflection.MethodInfo[] all = typeof(SettingsGUI_V2).GetMethods(
				System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
			for (int i = 0; i < all.Length; i++)
			{
				System.Reflection.MethodInfo m = all[i];
				if (m == null || m.Name != "UpdateOpenedMenu")
				{
					continue;
				}
				System.Reflection.ParameterInfo[] ps = m.GetParameters();
				int n = (ps != null) ? ps.Length : 0;
				if (n < 1)
				{
					continue;
				}
				if (cachedUpdateOpenedMenu == null || n > cachedUpdateOpenedMenu.GetParameters().Length)
				{
					cachedUpdateOpenedMenu = m;
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager: UpdateOpenedMenu lookup failed: " + ex.Message));
		}
		return cachedUpdateOpenedMenu;
	}

	/// <summary>v1.1.3：上一帧轮询时间（设置界面重开检测——Update 停跑期间时间跳变 &gt; 1.5s）。</summary>
	internal static float lastPollTime;

	/// <summary>重建页面前清空监视。落盘由各分区【保存】按钮显式触发（用户要求"按下后才保存"）。</summary>
	private static void ClearWatchesAndFlush()
	{
		try
		{
			watches.Clear();
		}
		catch
		{
		}
	}

	/// <summary>模板兜底缓存入口。</summary>
	internal static void CacheTemplatesFromNative(Transform contentPage)
	{
		Templates.CacheFromNative(contentPage);
	}

	private static void ClearContent(Transform contentPage)
	{
		if (contentPage == null)
		{
			return;
		}
		try
		{
			// 注意：IL2CPP 下 foreach (Transform child in transform) 枚举元素是 Il2CppSystem.Object，
			// 不能隐式转 Transform —— 用 childCount + GetChild
			for (int i = contentPage.childCount - 1; i >= 0; i--)
			{
				Transform child = contentPage.GetChild(i);
				if (child != null && child.gameObject != null)
				{
					UnityEngine.Object.Destroy(child.gameObject);
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager clear error: " + ex.Message));
		}
	}

	/// <summary>展开的 mod（默认全折叠，点击标题切换）。</summary>
	internal static readonly HashSet<string> expandedMods = new HashSet<string>();

	/// <summary>展开的配置分区（默认全收起；多分区 mod 的展开页内显示分区小标题，点击折叠/展开）。</summary>
	internal static readonly HashSet<string> expandedSections = new HashSet<string>();

	/// <summary>滚动高度自愈节流（原生协程/关闭转场可能写入错误高度 → 周期性重测容器实际高度）。</summary>
	internal static float nextScrollFixTime;

	/// <summary>v1.2.3：行内缩的单调锁存值（本地像素）。同一页面会话内只增不减——
	/// 原生在"预留/不预留滚动条"两种状态间切换时不会来回跳，消灭闪烁；建页时重置。</summary>
	private static float insetLeftLatched;

	private static float insetRightLatched;

	/// <summary>建页时重置内缩锁存（每次重建都按当时几何重新测一遍）。</summary>
	internal static void ResetInsetLatch()
	{
		insetLeftLatched = 0f;
		insetRightLatched = 0f;
	}

	/// <summary>我们是否改过 ScrollRect.content 的锚点（离开 MODS 页时还原，防影响原生页面）。</summary>
	internal static RectTransform anchorChangedTarget;

	internal static Vector2 anchorOrigMin;

	internal static Vector2 anchorOrigMax;

	internal static Vector2 anchorOrigPivot;

	internal static Vector2 anchorOrigPos;

	/// <summary>v1.1.8：接管前原生 content 的原始高度（SelfHealScroll 会写 sizeDelta.y，
	/// 还原时必须一并恢复——漏还原会留下坏高度：原生页滑条与按键重合、重进 MODS 页布局错乱）。</summary>
	internal static float anchorOrigHeight = float.NaN;

	/// <summary>还原被我们改过的 content 锚点与高度（v1.1.8：补还原 sizeDelta.y）。</summary>
	internal static void RestoreScrollAnchors()
	{
		try
		{
			if (anchorChangedTarget != null && !anchorChangedTarget.Equals(null))
			{
				anchorChangedTarget.anchorMin = anchorOrigMin;
				anchorChangedTarget.anchorMax = anchorOrigMax;
				anchorChangedTarget.pivot = anchorOrigPivot;
				anchorChangedTarget.anchoredPosition = anchorOrigPos;
				// 高度也还原：原生 content 是拉伸锚点时高度由布局驱动，sizeDelta.y 归 0；
				// 非拉伸时还原接管前记录的原始值
				if (!float.IsNaN(anchorOrigHeight))
				{
					anchorChangedTarget.sizeDelta = new Vector2(anchorChangedTarget.sizeDelta.x, anchorOrigHeight);
				}
				try
				{
					UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(anchorChangedTarget);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
		anchorChangedTarget = null;
		anchorOrigHeight = float.NaN;
	}

	/// <summary>活跃控件监视（轮询值变化——绕开 UnityAction 委托桥接的 marshaling bug）。</summary>
	internal sealed class ControlWatch
	{
		internal UnityEngine.UI.Slider slider;

		internal UnityEngine.UI.InputField input;

		internal bool inputIsFloat;

		/// <summary>v1.1.6：自由文本输入（string 配置无选项列表）——文本变化直接 StageValue 原文。</summary>
		internal bool isFreeText;

		internal bool hasRange;

		internal float rangeMin;

		internal float rangeMax;

		internal string lastInputText;

		internal UnityEngine.UI.Dropdown dropdown;

		internal UnityEngine.UI.Toggle toggle;

		internal ConfigFile cfg;

		internal ConfigEntryBase entry;

		internal bool wholeNumbers;

		internal float lastFloat = float.NaN;

		internal int lastInt = -1;

		internal bool lastBool;

		internal bool hasBool;

		internal Type enumType;

		internal string[] enumNames;

		internal List<UnityEngine.UI.Text> valueTexts;
	}

	internal static readonly List<ControlWatch> watches = new List<ControlWatch>();

	/// <summary>暂存区：cfg → (键 → 新值)。v1.0.32 起无【保存】按钮：改动由看门狗在 0.8s 后自动落盘
	/// （等效"退出设置即保存"），翻页/重开设置时兜底落盘。</summary>
	private static readonly Dictionary<ConfigFile, Dictionary<string, object>> staged = new Dictionary<ConfigFile, Dictionary<string, object>>();

	/// <summary>最后一次暂存改动的时间（看门狗用）。</summary>
	internal static float lastStageTime;

	internal static void StageValue(ConfigFile cfg, ConfigEntryBase entry, object v)
	{
		try
		{
			if (cfg == null || entry == null)
			{
				return;
			}
			if (!staged.TryGetValue(cfg, out Dictionary<string, object> map))
			{
				map = new Dictionary<string, object>();
				staged[cfg] = map;
			}
			map[entry.Definition.Key] = v;
			lastStageTime = Time.unscaledTime;
			// 只记录非数值类型（按键/开关/下拉），滑条拖动不刷屏
			if (entry.SettingType != typeof(float) && entry.SettingType != typeof(int))
			{
				Plugin.ModLog.LogInfo((object)("ModManager staged: " + entry.Definition.Key + " = " + v));
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager stage value error: " + ex.Message));
		}
	}

	/// <summary>控件初始值：暂存区有值用暂存值，否则用配置当前值（未保存的改动在重开页面后仍然可见）。</summary>
	internal static object GetStaged(ConfigFile cfg, ConfigEntryBase entry, object current)
	{
		try
		{
			if (cfg != null && staged.TryGetValue(cfg, out Dictionary<string, object> map) &&
				map.TryGetValue(entry.Definition.Key, out object v))
			{
				return v;
			}
		}
		catch
		{
		}
		return current;
	}

	/// <summary>把某 cfg 的暂存值全部写入配置对象并落盘（退出设置时自动调用）。</summary>
	private static bool ApplyAndSave(ConfigFile cfg)
	{
		if (cfg == null)
		{
			return false;
		}
		if (staged.TryGetValue(cfg, out Dictionary<string, object> map))
		{
			foreach (ConfigEntryBase e in cfg.Values)
			{
				if (e == null || !map.TryGetValue(e.Definition.Key, out object v))
				{
					continue;
				}
				try
				{
					e.BoxedValue = v;
				}
				catch (Exception ex)
				{
					Plugin.ModLog.LogError((object)("ModManager apply value error: " + ex.Message));
				}
			}
			staged.Remove(cfg);
		}
		try
		{
			cfg.Save();
			return true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager cfg.Save error: " + ex.Message));
			return false;
		}
	}

	/// <summary>退出设置界面时调用：把所有暂存改动写入配置并落盘（Esc/继续 = 保存动作）。</summary>
	internal static void FlushAllStaged()
	{
		try
		{
			if (staged.Count == 0)
			{
				return;
			}
			int n = staged.Count;
			foreach (ConfigFile cfg in new List<ConfigFile>(staged.Keys))
			{
				ApplyAndSave(cfg);
			}
			Plugin.ModLog.LogInfo((object)("ModManager: auto-saved staged changes for " + n + " config(s)."));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager flush error: " + ex.Message));
		}
	}

	/// <summary>看门狗（设置界面每帧调用）：暂存改动停止 0.8s 后自动落盘；离开 MODS 页也兜底落盘。</summary>
	internal static void WatchdogFlush()
	{
		try
		{
			if (staged.Count > 0)
			{
				if (SettingsGUI_V2.currentOpenedMenu != myIndex || Time.unscaledTime - lastStageTime > 0.8f)
				{
					FlushAllStaged();
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>按钮文字瞬时反馈（复制/重置后闪烁提示，无需等设置关闭）。</summary>
	private sealed class FlashItem
	{
		internal UnityEngine.UI.Text text;

		internal string original;

		internal float until;
	}

	private static readonly List<FlashItem> flashes = new List<FlashItem>();

	private static void FlashText(UnityEngine.UI.Text text, string temp, float seconds)
	{
		try
		{
			if (text == null || text.Equals(null))
			{
				return;
			}
			flashes.RemoveAll(f => f.text == null || f.text.Equals(null) || f.text == text);
			flashes.Add(new FlashItem { text = text, original = text.text, until = Time.unscaledTime + seconds });
			text.text = temp;
		}
		catch
		{
		}
	}

	private static void PollFlashes()
	{
		float now = Time.unscaledTime;
		for (int i = flashes.Count - 1; i >= 0; i--)
		{
			try
			{
				FlashItem f = flashes[i];
				if (f == null || f.text == null || f.text.Equals(null) || now >= f.until)
				{
					if (f != null && f.text != null && !f.text.Equals(null))
					{
						f.text.text = f.original;
					}
					flashes.RemoveAt(i);
				}
			}
			catch
			{
				flashes.RemoveAt(i);
			}
		}
	}

	/// <summary>每帧轮询活跃控件（由 InjectPollPatch 的 Update Postfix 调用）。</summary>
	internal static void PollControls()
	{
		PollFlashes();
		PollKeyCapture();
		if (watches.Count == 0)
		{
			return;
		}
		foreach (ControlWatch w in watches)
		{
			try
			{
				if (w.input != null && !w.input.Equals(null))
				{
					// 数值输入框：直接键入数字（替代滑条）。轮询 text，绕开 InputField 事件桥接。
					string text = w.input.text;
					if (text != w.lastInputText)
					{
						w.lastInputText = text;
						// v1.1.6：自由文本（string 配置）→ 原文直接暂存（空串也写，允许清空）
						if (w.isFreeText)
						{
							StageValue(w.cfg, w.entry, text);
						}
						else if (string.IsNullOrEmpty(text))
						{
							// 输入中的中间态（空/小数点）：保持上一次有效暂存
						}
						else if (w.inputIsFloat && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float fv) && !float.IsNaN(fv) && !float.IsInfinity(fv))
						{
							if (w.hasRange)
							{
								fv = Mathf.Clamp(fv, w.rangeMin, w.rangeMax);
							}
							StageValue(w.cfg, w.entry, fv);
						}
						else if (!w.inputIsFloat && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int iv))
						{
							if (w.hasRange)
							{
								iv = Math.Max((int)w.rangeMin, Math.Min((int)w.rangeMax, iv));
							}
							StageValue(w.cfg, w.entry, iv);
						}
					}
				}
				else if (w.slider != null && !w.slider.Equals(null))
				{
					float v = w.slider.value;
					if (!float.IsNaN(w.lastFloat) && Mathf.Abs(v - w.lastFloat) > 0.001f)
					{
						w.lastFloat = v;
						// 整数步进只影响显示与滑条吸附；写回时 float 配置必须传 float（否则 ConfigEntry<float> 读值抛 InvalidCastException）
						object val = w.wholeNumbers ? ((w.entry.SettingType == typeof(float)) ? (object)(float)v : (object)(int)v) : (object)v;
						StageValue(w.cfg, w.entry, val);
						UpdateValueTexts(w.valueTexts, w.wholeNumbers, v);
					}
					else if (float.IsNaN(w.lastFloat))
					{
						w.lastFloat = v;
					}
				}
				else if (w.toggle != null && !w.toggle.Equals(null))
				{
					bool v = w.toggle.isOn;
					if (w.hasBool && v != w.lastBool)
					{
						w.lastBool = v;
						StageValue(w.cfg, w.entry, v);
					}
					else if (!w.hasBool)
					{
						w.lastBool = v;
						w.hasBool = true;
					}
				}
				else if (w.dropdown != null && !w.dropdown.Equals(null))
				{
					int v = w.dropdown.value;
					if (w.lastInt >= 0 && v != w.lastInt)
					{
						w.lastInt = v;
						if (w.entry.SettingType == typeof(string))
						{
							AcceptableValueBase av = (w.entry.Description != null) ? w.entry.Description.AcceptableValues : null;
							if (av is AcceptableValueList<string> lst && lst.AcceptableValues != null && lst.AcceptableValues.Length > 0)
							{
								string[] vals = lst.AcceptableValues;
								StageValue(w.cfg, w.entry, vals[Math.Max(0, Math.Min(vals.Length - 1, v))]);
							}
						}
						else if (w.entry.SettingType == typeof(bool))
						{
							// bool 配置的退化 Dropdown（Off=0 / On=1）
							StageValue(w.cfg, w.entry, v == 1);
						}
						else if (w.enumType != null && w.enumNames != null && v >= 0 && v < w.enumNames.Length)
						{
							// 枚举下拉：写枚举值
							try
							{
								StageValue(w.cfg, w.entry, Enum.Parse(w.enumType, w.enumNames[v]));
							}
							catch
							{
							}
						}
						else
						{
							StageValue(w.cfg, w.entry, v);
						}
					}
					else if (w.lastInt < 0)
					{
						w.lastInt = v;
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager poll control error: " + ex.Message));
			}
		}
	}

	/// <summary>
	/// 填充页面：在 Content 下建一个自动布局容器（VerticalLayoutGroup + ContentSizeFitter），
	/// 所有行挂进容器，位置/高度/滚动范围全部交给原生 UI 系统 —— 与原生设置页同一套布局机制。
	/// </summary>
	internal static float FillContent(Transform contentPage)
	{
		// 原生 UI 重写 POC：NativePoc=true 时用原生 SettingSelectable.Create 渲染测试行
		if (Plugin.nativePoc != null && Plugin.nativePoc.Value)
		{
			return NativePage.RenderPoc(contentPage);
		}
		// 原生 UI 完整页面：NativeFull=true 时整页用原生行渲染
		if (Plugin.nativeFull != null && Plugin.nativeFull.Value)
		{
			return NativePage.BuildAndRender(contentPage);
		}
		float y = 0f;
		List<PluginConfig> plugins = CollectPlugins();
		if (contentPage == null)
		{
			return y;
		}
		Templates.Ensure(contentPage);
		if (plugins.Count == 0)
		{
			return y;
		}
		// 自动布局容器（锚定 Content 顶部，LayoutGroup 向下排列，Fitter 自动扩展高度）
		// v1.1.8：间隙再收（右移 4px + 右边距 8px → 行与滑条间隙 ~8px，贴近原版）。
		GameObject cont = new GameObject("MM_Container");
		cont.transform.SetParent(contentPage, false);
		RectTransform crt = cont.AddComponent<RectTransform>();
		crt.anchorMin = new Vector2(0f, 1f);
		crt.anchorMax = new Vector2(1f, 1f);
		crt.pivot = new Vector2(0.5f, 1f);
		crt.anchoredPosition = new Vector2(-4f, 0f);
		crt.sizeDelta = new Vector2(-8f, 100f);
		UnityEngine.UI.VerticalLayoutGroup lg = cont.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
		lg.spacing = 6f;
		lg.childAlignment = TextAnchor.UpperCenter;
		lg.childControlWidth = true;
		lg.childControlHeight = false;
		lg.childForceExpandWidth = true;
		lg.childForceExpandHeight = false;
		UnityEngine.UI.ContentSizeFitter fit = cont.AddComponent<UnityEngine.UI.ContentSizeFitter>();
		fit.horizontalFit = UnityEngine.UI.ContentSizeFitter.FitMode.Unconstrained;
		fit.verticalFit = UnityEngine.UI.ContentSizeFitter.FitMode.PreferredSize;
		Transform container = cont.transform;
		// 热键冲突提示（顶部）
		foreach (string warn in DetectHotkeyConflicts(plugins))
		{
			AddWarningRow(container, warn);
		}
		// v1.1.4：按剥离前缀后的短名排序 + 字母分组（A/B/…/0-9/#），每组前插一个字母小标题行。
		List<PluginConfig> visible = new List<PluginConfig>(plugins);
		foreach (PluginConfig p in visible)
		{
			p.shortName = StripNamePrefix(p.name);
		}
		visible.Sort((a, b) => string.Compare(a?.shortName, b?.shortName, StringComparison.OrdinalIgnoreCase));
		string lastLetter = null;
		foreach (PluginConfig p in visible)
		{
			try
			{
				// 字母分组小标题：短名首字符 A-Z 归字母组；数字归 0-9；其余归 #
				string letter = GroupLetter(p.shortName);
				if (letter != null && letter != lastLetter)
				{
					lastLetter = letter;
					AddCategoryRow(container, letter);
				}
				// 可点击标题（剥离前缀的短名；展开行小字显示完整原名）
				string name = p.name;
				bool expanded = expandedMods.Contains(name);
				AddSectionButton(SettingsGUI_V2.instance, container, p.shortName, expanded, name);
				if (expanded)
				{
					FillModEntries(p, container);
					// 展开内容末尾：重置（恢复默认）+ 复制全部文本
					AddFooterRow(container, p.name, p.cfg);
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager fill plugin error: " + ex.Message));
			}
		}
		// 列表底部感谢行（双语）
		AddThanksRow(container);
		// 强制布局刷新，读取容器实际高度并扩展 Content（滚动范围）
		try
		{
			UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
			Canvas.ForceUpdateCanvases();
			float pref = 0f;
			try
			{
				pref = UnityEngine.UI.LayoutUtility.GetPreferredHeight(crt);
			}
			catch
			{
				pref = crt.rect.height;
			}
			float h = Math.Max(200f, pref + 8f);
			RectTransform rt = contentPage.GetComponent<RectTransform>();
			if (rt != null)
			{
				rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
			}
			y = h;
		}
		catch
		{
		}
		// v1.2.1 诊断（发布前删）：每次建页 dump 一次行几何/重叠，便于与"重进后"对照
		LogLayoutSnapshot("build", contentPage);
		LogScrollChain("build", contentPage);
		return y;
	}

	/// <summary>v1.1.7：重建风暴熔断状态（0.5s 内第 4 次 !hasOurs 触发证据 dump + 停止重建）。</summary>
	internal static float lastRebuildTime;

	internal static int rebuildBurst;

	/// <summary>v1.1.7：只做布局强推，不再整页重建（熔断期的保守自愈——若只是 LayoutGroup 未算，
	/// ForceRebuild + Canvas 刷新就能把零行容器的尺寸撑起来）。</summary>
	internal static void ForceLayoutOnly(SettingsGUI_V2 s)
	{
		try
		{
			Transform c = s.contentPage;
			if (c == null)
			{
				return;
			}
			for (int i = c.childCount - 1; i >= 0; i--)
			{
				Transform ch = c.GetChild(i);
				if (ch != null && ch.name == "MM_Container")
				{
					RectTransform crt = ch.GetComponent<RectTransform>();
					if (crt != null)
					{
						UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
					}
					break;
				}
			}
			Canvas.ForceUpdateCanvases();
		}
		catch
		{
		}
	}

	/// <summary>v1.1.7：卡死现场一次性证据 dump（重建风暴熔断时触发）——不猜原因，把全部相关
	/// 状态写日志：容器子行数、contentPage 子物体清单、插件缓存数、页码、原生实例/协程状态。</summary>
	internal static void DumpStuckEvidence(SettingsGUI_V2 s)
	{
		try
		{
			string nl = Environment.NewLine;
			System.Text.StringBuilder sb = new System.Text.StringBuilder("ModManager STUCK-EVIDENCE dump:");
			sb.Append(nl).Append("  currentOpenedMenu=").Append(SettingsGUI_V2.currentOpenedMenu)
			   .Append(" myIndex=").Append(myIndex);
			try
			{
				sb.Append(nl).Append("  pluginsCache=").Append(pluginsCache != null ? pluginsCache.Count.ToString() : "null")
				   .Append(" chainloaderPlugins=").Append(IL2CPPChainloader.Instance?.Plugins != null ? IL2CPPChainloader.Instance.Plugins.Count.ToString() : "null");
			}
			catch (Exception ex2)
			{
				sb.Append(nl).Append("  chainloader read error: ").Append(ex2.Message);
			}
			try
			{
				if (s != null)
				{
					sb.Append(nl).Append("  fillingRoutine=").Append(s.fillingRoutine != null ? "live" : "null")
					   .Append(" instance=").Append(s.gameObject != null ? s.gameObject.activeInHierarchy.ToString() : "?");
					Transform c = s.contentPage;
					sb.Append(nl).Append("  contentPage=").Append(c != null ? c.name : "null")
					   .Append(" childCount=").Append(c != null ? c.childCount : -1);
					if (c != null)
					{
						for (int i = 0; i < c.childCount; i++)
						{
							Transform ch = c.GetChild(i);
							if (ch != null)
							{
								RectTransform rt = ch.GetComponent<RectTransform>();
								sb.Append(nl).Append("    [").Append(i).Append("] ").Append(ch.name)
								   .Append(" childCount=").Append(ch.childCount)
								   .Append(" size=").Append(rt != null ? rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0") : "?")
								   .Append(" active=").Append(ch.gameObject != null ? ch.gameObject.activeInHierarchy.ToString() : "?");
							}
						}
					}
				}
			}
			catch (Exception ex3)
			{
				sb.Append(nl).Append("  contentPage dump error: ").Append(ex3.Message);
			}
			Plugin.ModLog.LogWarning((object)sb.ToString());
		}
		catch
		{
		}
	}

	/// <summary>v1.1.9：空列表真凶修复（STUCK-EVIDENCE 实锤：MM_Container childCount=42
	/// size=442x100 active=False——42 行全在、宽度正常、整个容器 inactive）。快速右翻连点
	/// 打断原生页填充流程，原生把共享滚动 Content 所在链停用（正是用户看到的"没有滑条的
	/// 设置界面"）；我们的容器建在停用父级下 → activeInHierarchy=false → 永不渲染/布局。
	/// MODS 页激活期间 Content 链的可见性归我们管：发现链上被停用就逐级重新激活。</summary>
	internal static void EnsureContentVisible(SettingsGUI_V2 s)
	{
		try
		{
			if (s == null || s.contentPage == null)
			{
				return;
			}
			Transform cur = s.contentPage;
			int guard = 0;
			while (cur != null && cur.gameObject != null && guard++ < 8)
			{
				if (!cur.gameObject.activeSelf)
				{
					cur.gameObject.SetActive(true);
					Plugin.ModLog.LogInfo((object)("ModManager: re-activated '" + cur.name + "' on content chain (was disabled by native paging)."));
				}
				cur = cur.parent;
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.1.3：滚动复位到顶部 + 清理 NaN（翻页/重进后列表不显示的常见根因）。</summary>
	internal static void ResetScrollTop(Transform contentPage)
	{
		try
		{
			Transform cur = contentPage;
			while (cur != null)
			{
				UnityEngine.UI.ScrollRect sr = cur.GetComponent<UnityEngine.UI.ScrollRect>();
				if (sr != null)
				{
					float p = sr.verticalNormalizedPosition;
					if (float.IsNaN(p) || p < 0.99f)
					{
						sr.verticalNormalizedPosition = 1f;
					}
					// handle 残缺（size<=0 或 NaN）时触发一次重算
					UnityEngine.UI.Scrollbar sb = sr.verticalScrollbar;
					if (sb != null && (float.IsNaN(sb.size) || sb.size <= 0.001f))
					{
						Canvas.ForceUpdateCanvases();
						sr.verticalNormalizedPosition = 1f;
					}
					return;
				}
				cur = cur.parent;
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.2.3：让行容器避开竖直滚动条所占的竖条（"灰色名称栏被滑条压住"的根治，且不闪）。
	/// 实测证据（2026-09-12 日志）：原生只有在部分路径里把 Viewport 收窄 17px 预留滚动条
	/// （`Viewport sd=-17` → 容器右缘 1875 vs 滚动条 1882，间隙 7px = 正常，图一）；
	/// 但"退出设置再进去"那条路径不收窄（`Viewport 450`）而滚动条已 active → 容器右缘 1900
	/// 与滚动条区间 1882..1912 重叠 18px = 用户看到的"滑条和他的背景压住名称栏背景"（图二）。
	/// 所以宽度不能只靠原生预留：按滚动条世界区间算我们容器的内缩。
	/// **量法必须是固定参考**（contentPage 右缘 - 基础内缩），不能用容器当前 rect —— 用后者会
	/// 缩进去之后就算不出重叠、下一帧又退回 → 每帧横跳（v1.2.2 实测闪烁根因）。</summary>
	internal static void ApplyScrollbarInset(Transform contentPage, Transform cont, RectTransform crt, RectTransform rt)
	{
		try
		{
			if (cont == null || crt == null || rt == null || cont.name != "MM_Container")
			{
				return;
			}
			RectTransform sbrt = null;
			try
			{
				UnityEngine.UI.ScrollRect sr = null;
				Transform cur = contentPage;
				while (cur != null && sr == null)
				{
					sr = cur.GetComponent<UnityEngine.UI.ScrollRect>();
					cur = cur.parent;
				}
				if (sr != null && sr.verticalScrollbar != null && sr.verticalScrollbar.gameObject != null)
				{
					// 不看 activeInHierarchy：滚动条隐藏时原生不收窄 Viewport，但随时可能显示出来
					// （实测同一位置），所以按它的矩形位置一律预留，避免"它一显示就压住名称栏"。
					sbrt = sr.verticalScrollbar.GetComponent<RectTransform>();
				}
				if (sbrt == null)
				{
					SettingsGUI_V2 s = SettingsGUI_V2.instance;
					if (s != null && s.leftScrollbar != null && s.leftScrollbar.gameObject != null)
					{
						sbrt = s.leftScrollbar.GetComponent<RectTransform>();
					}
				}
			}
			catch
			{
			}
			float leftInset = 0f;
			float rightInset = 8f;
			if (sbrt != null)
			{
				// 关键：重叠量必须相对**固定参考 contentPage** 量，不能用容器当前 rect。
				// v1.2.2 用容器当前 rect 量 → 缩进去之后就算不出重叠 → 下一帧退回基础值 →
				// 24/8 每帧反复横跳（用户实测的闪烁）。这里按 "contentPage 右缘 - 基础内缩"
				// 这个不随我们改动变化的基准量。
				float rx0, rx1, ry0, ry1, sx0, sx1, sy0, sy1;
				WorldRect(rt, out rx0, out rx1, out ry0, out ry1);
				WorldRect(sbrt, out sx0, out sx1, out sy0, out sy1);
				float ovY = Math.Min(ry1, sy1) - Math.Max(ry0, sy0);
				float scale = Math.Abs(rt.lossyScale.x) > 0.0001f ? Math.Abs(rt.lossyScale.x) : 1f;
				if (ovY > 1f)
				{
					float mid = (rx0 + rx1) * 0.5f;
					if (sx0 >= mid)
					{
						// 滚动条在右侧：把"基础内缩后仍被压住的量 + 6 世界像素间隙"补上
						float over = (rx1 - rightInset * scale) - sx0 + 6f;
						if (over > 0f)
						{
							// 上限 26：滚动条竖条约 20 本地像素，超过必然是过场瞬态，别锁死
							rightInset += Math.Min(26f, over / scale);
						}
					}
					else if (sx1 <= mid)
					{
						float over = sx1 - (rx0 + leftInset * scale) + 6f;
						if (over > 0f)
						{
							leftInset += Math.Min(26f, over / scale);
						}
					}
				}
			}
			// 单调锁存（同一页面会话内只增不减）：原生在"预留/不预留滚动条"两种状态间切换时
			// 内缩值不会来回跳，彻底消灭闪烁；代价是偶尔多留几像素空隙（肉眼无感）。
			if (leftInset > insetLeftLatched)
			{
				insetLeftLatched = leftInset;
			}
			if (rightInset > insetRightLatched)
			{
				insetRightLatched = rightInset;
			}
			leftInset = insetLeftLatched;
			rightInset = insetRightLatched;
			// 安全阀：别把列表压成一条
			if (rt.rect.width - leftInset - rightInset < 200f)
			{
				return;
			}
			float w = leftInset + rightInset;
			float p = (leftInset - rightInset) * 0.5f;
			if (Math.Abs(crt.sizeDelta.x + w) > 0.5f || Math.Abs(crt.anchoredPosition.x - p) > 0.5f)
			{
				crt.sizeDelta = new Vector2(-w, crt.sizeDelta.y);
				crt.anchoredPosition = new Vector2(p, crt.anchoredPosition.y);
				try
				{
					UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
				}
				catch
				{
				}
				Plugin.ModLog.LogInfo((object)("ModManager: row inset applied -> left=" + leftInset.ToString("F1")
					+ " right=" + rightInset.ToString("F1") + " (row width " + (rt.rect.width - w).ToString("F0") + ")"));
			}
		}
		catch
		{
		}
	}

	/// <summary>周期性重测容器高度并断言到滚动内容（防原生协程/转场把滚动范围写坏）。</summary>
	internal static void SelfHealScroll(Transform contentPage, bool force = false)
	{
		if (contentPage == null || (!force && Time.unscaledTime < nextScrollFixTime))
		{
			return;
		}
		nextScrollFixTime = Time.unscaledTime + 0.5f;
		try
		{
			Transform cont = null;
			for (int i = contentPage.childCount - 1; i >= 0; i--)
			{
				Transform child = contentPage.GetChild(i);
				if (child != null && child.name == "MM_Container")
				{
					cont = child;
					break;
				}
			}
			if (cont == null)
			{
				// 原生行模式：没有 MM_Container，直接用 contentPage 自身作为高度基准
				cont = contentPage;
				if (cont == null)
				{
					return;
				}
			}
			RectTransform crt = cont.GetComponent<RectTransform>();
			RectTransform rt = contentPage.GetComponent<RectTransform>();
			if (crt == null || rt == null)
			{
				return;
			}
			Canvas.ForceUpdateCanvases();
			// v1.2.2：先按滚动条实际位置把行宽内缩（见 ApplyScrollbarInset 注释）
			ApplyScrollbarInset(contentPage, cont, crt, rt);
			float h = Math.Max(200f, crt.rect.height + 8f);
			// 关键：滚动范围由 ScrollRect.content（不一定是 contentPage 本身，局内可能是父级 Content）决定
			RectTransform target = rt;
			UnityEngine.UI.ScrollRect sr = null;
			Transform cur2 = contentPage;
			while (cur2 != null && sr == null)
			{
				sr = cur2.GetComponent<UnityEngine.UI.ScrollRect>();
				cur2 = cur2.parent;
			}
			if (sr != null && sr.content != null && sr.content != rt)
			{
				target = sr.content;
			}
			// 彻底修复：若目标 content 是"拉伸锚点"（rect 高度不受 sizeDelta 控制），改为顶部拉伸锚点
			// 并直接写 rect 高度；同时强制重建布局让 ScrollRect 立即重算滚动范围。
			if (target != null)
			{
				try
				{
					Vector2 am = target.anchorMin;
					Vector2 ax = target.anchorMax;
					if (Math.Abs(ax.y - am.y) > 0.01f)
					{
						// 垂直方向是拉伸锚点 → 改为顶部锚定（高度由 sizeDelta 决定）；记录原值便于还原
						if (anchorChangedTarget == null)
						{
							anchorChangedTarget = target;
							anchorOrigMin = am;
							anchorOrigMax = ax;
							anchorOrigPivot = target.pivot;
							anchorOrigPos = target.anchoredPosition;
							// v1.1.8：记录原始 sizeDelta.y（还原时恢复，防坏高度泄漏到原生页）
							anchorOrigHeight = target.sizeDelta.y;
						}
						target.anchorMin = new Vector2(am.x, 1f);
						target.anchorMax = new Vector2(ax.x, 1f);
						target.pivot = new Vector2(target.pivot.x, 1f);
						target.anchoredPosition = new Vector2(target.anchoredPosition.x, 0f);
					}
				}
				catch
				{
				}
				target.sizeDelta = new Vector2(target.sizeDelta.x, h);
				try
				{
					UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(target);
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.2.1（临时诊断，定位"重进模组页后控件重叠"；发布前删）：dump 行几何 + 重叠检测。
	/// 行高口径 = sizeDelta.y（VerticalLayoutGroup 在 childControlHeight=false 时用的就是它）。</summary>
	internal static void LogLayoutSnapshot(string tag, Transform contentPage)
	{
		try
		{
			if (contentPage == null)
			{
				return;
			}
			Transform cont = null;
			for (int i = contentPage.childCount - 1; i >= 0; i--)
			{
				Transform ch = contentPage.GetChild(i);
				if (ch != null && ch.name == "MM_Container")
				{
					cont = ch;
					break;
				}
			}
			System.Text.StringBuilder sb = new System.Text.StringBuilder("ModManager LAYOUT[" + tag + "]");
			RectTransform prt = contentPage.GetComponent<RectTransform>();
			if (prt != null)
			{
				sb.Append(" content=").Append(prt.rect.width.ToString("0")).Append("x").Append(prt.rect.height.ToString("0"))
				   .Append(" contentSd=").Append(prt.sizeDelta.x.ToString("0")).Append(",").Append(prt.sizeDelta.y.ToString("0"));
			}
			if (cont == null)
			{
				sb.Append(" container=MISSING");
				Plugin.ModLog.LogInfo((object)sb.ToString());
				return;
			}
			RectTransform crt = cont.GetComponent<RectTransform>();
			if (crt != null)
			{
				sb.Append(" container=").Append(crt.rect.width.ToString("0")).Append("x").Append(crt.rect.height.ToString("0"));
			}
			sb.Append(" rows=").Append(cont.childCount).Append(" frame=").Append(Time.frameCount);
			Plugin.ModLog.LogInfo((object)sb.ToString());
			int collapsed = 0;
			int stretched = 0;
			int overlaps = 0;
			float prevBot = 0f;
			string prevName = null;
			for (int i = 0; i < cont.childCount; i++)
			{
				Transform c = cont.GetChild(i);
				if (c == null)
				{
					continue;
				}
				RectTransform rt = c.GetComponent<RectTransform>();
				if (rt == null)
				{
					continue;
				}
				float h = rt.sizeDelta.y;
				float pv = rt.pivot.y;
				float top = rt.anchoredPosition.y + (1f - pv) * h;
				float bot = top - h;
				bool isStretched = Math.Abs(rt.anchorMax.y - rt.anchorMin.y) > 0.01f;
				if (h <= 1f)
				{
					collapsed++;
				}
				if (isStretched)
				{
					stretched++;
				}
				if (prevName != null && top > prevBot + 0.5f)
				{
					overlaps++;
					if (overlaps <= 6)
					{
						Plugin.ModLog.LogInfo((object)("ModManager LAYOUT[" + tag + "] OVERLAP #" + overlaps + ": '" + prevName
							+ "' bottom=" + prevBot.ToString("F1") + " vs '" + c.name + "' top=" + top.ToString("F1")
							+ " (overlap " + (top - prevBot).ToString("F1") + "px)"));
					}
				}
				if (i < 10 || i >= cont.childCount - 2)
				{
					Plugin.ModLog.LogInfo((object)("ModManager LAYOUT[" + tag + "] row[" + i + "] " + c.name
						+ " h=" + h.ToString("F1") + " y=" + rt.anchoredPosition.y.ToString("F1")
						+ " pivotY=" + pv.ToString("F2") + " anchorY=" + rt.anchorMin.y.ToString("F2") + ".." + rt.anchorMax.y.ToString("F2")
						+ (isStretched ? " STRETCHED" : "") + (h <= 1f ? " COLLAPSED" : "")));
				}
				prevBot = bot;
				prevName = c.name;
			}
			Plugin.ModLog.LogInfo((object)("ModManager LAYOUT[" + tag + "] summary: rows=" + cont.childCount + " collapsed=" + collapsed
				+ " stretched=" + stretched + " overlappingPairs=" + overlaps));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager layout snapshot error: " + ex.Message));
		}
	}

	/// <summary>v1.2.1（临时诊断，发布前删）：dump Content 链 + ScrollRect + 左侧滚动条几何（世界坐标重叠量）。
	/// 用来定位"重进模组设置后滑条（滚动条）压住 mod 选项"到底是滚动条位移还是行内容变宽。</summary>
	internal static void LogScrollChain(string tag, Transform contentPage)
	{
		try
		{
			if (contentPage == null)
			{
				return;
			}
			Transform cur = contentPage;
			int depth = 0;
			while (cur != null && cur.gameObject != null && depth < 10)
			{
				RectTransform rt = cur.GetComponent<RectTransform>();
				string comps = "";
				try
				{
					if (cur.GetComponent<UnityEngine.UI.ScrollRect>() != null) comps += "ScrollRect ";
					if (cur.GetComponent<UnityEngine.UI.Scrollbar>() != null) comps += "Scrollbar ";
					if (cur.GetComponent<UnityEngine.UI.RectMask2D>() != null) comps += "RectMask2D ";
					if (cur.GetComponent<UnityEngine.UI.VerticalLayoutGroup>() != null) comps += "VLG ";
				}
				catch
				{
				}
				Plugin.ModLog.LogInfo((object)("ModManager SCROLL[" + tag + "] chain+" + depth + " '" + cur.name + "' rect="
					+ (rt != null ? rt.rect.width.ToString("0") + "x" + rt.rect.height.ToString("0") : "?")
					+ " sd=" + (rt != null ? rt.sizeDelta.x.ToString("0") + "," + rt.sizeDelta.y.ToString("0") : "?")
					+ " anchorMin=" + (rt != null ? rt.anchorMin.x.ToString("F2") + "," + rt.anchorMin.y.ToString("F2") : "?")
					+ " anchorMax=" + (rt != null ? rt.anchorMax.x.ToString("F2") + "," + rt.anchorMax.y.ToString("F2") : "?")
					+ " pivot=" + (rt != null ? rt.pivot.x.ToString("F2") + "," + rt.pivot.y.ToString("F2") : "?")
					+ " pos=" + (rt != null ? rt.anchoredPosition.x.ToString("0") + "," + rt.anchoredPosition.y.ToString("0") : "?")
					+ " active=" + (cur.gameObject.activeInHierarchy ? "1" : "0")
					+ (comps.Length > 0 ? " [" + comps.Trim() + "]" : "")));
				cur = cur.parent;
				depth++;
			}
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s == null)
			{
				return;
			}
			UnityEngine.UI.ScrollRect sr = null;
			cur = contentPage;
			while (cur != null && sr == null)
			{
				sr = cur.GetComponent<UnityEngine.UI.ScrollRect>();
				cur = cur.parent;
			}
			RectTransform cont = null;
			for (int i = contentPage.childCount - 1; i >= 0; i--)
			{
				Transform ch = contentPage.GetChild(i);
				if (ch != null && ch.name == "MM_Container")
				{
					cont = ch.GetComponent<RectTransform>();
					break;
				}
			}
			if (sr != null)
			{
				RectTransform vp = sr.viewport;
				RectTransform ct = sr.content;
				Plugin.ModLog.LogInfo((object)("ModManager SCROLL[" + tag + "] ScrollRect content='"
					+ (ct != null ? ct.name : "null") + "' " + (ct != null ? ct.rect.width.ToString("0") + "x" + ct.rect.height.ToString("0") : "?")
					+ " viewport='" + (vp != null ? vp.name : "null") + "' " + (vp != null ? vp.rect.width.ToString("0") + "x" + vp.rect.height.ToString("0") : "?")
					+ " vPos=" + sr.verticalNormalizedPosition.ToString("F3")
					+ " vScrollbar=" + (sr.verticalScrollbar != null ? sr.verticalScrollbar.name : "null")));
			}
			RectTransform brow = null;
			if (s.leftScrollbar != null)
			{
				brow = s.leftScrollbar.GetComponent<RectTransform>();
				Plugin.ModLog.LogInfo((object)("ModManager SCROLL[" + tag + "] leftScrollbar '" + s.leftScrollbar.name + "' rect="
					+ (brow != null ? brow.rect.width.ToString("0") + "x" + brow.rect.height.ToString("0") : "?")
					+ " sd=" + (brow != null ? brow.sizeDelta.x.ToString("0") + "," + brow.sizeDelta.y.ToString("0") : "?")
					+ " anchorMin=" + (brow != null ? brow.anchorMin.x.ToString("F2") + "," + brow.anchorMin.y.ToString("F2") : "?")
					+ " anchorMax=" + (brow != null ? brow.anchorMax.x.ToString("F2") + "," + brow.anchorMax.y.ToString("F2") : "?")
					+ " pos=" + (brow != null ? brow.anchoredPosition.x.ToString("0") + "," + brow.anchoredPosition.y.ToString("0") : "?")
					+ " value=" + s.leftScrollbar.value.ToString("F3")
					+ " size=" + s.leftScrollbar.size.ToString("F3")
					+ " active=" + (s.leftScrollbar.gameObject.activeInHierarchy ? "1" : "0")));
			}
			// 世界坐标下：容器行区域与滚动条的横向重叠量（>0 = 真的压住了）
			if (brow != null && cont != null)
			{
				float c0x, c0y, c1x, c1y, b0x, b0y, b1x, b1y;
				WorldRect(cont, out c0x, out c1x, out c0y, out c1y);
				WorldRect(brow, out b0x, out b1x, out b0y, out b1y);
				float ovX = Math.Min(c1x, b1x) - Math.Max(c0x, b0x);
				float ovY = Math.Min(c1y, b1y) - Math.Max(c0y, b0y);
				Plugin.ModLog.LogInfo((object)("ModManager SCROLL[" + tag + "] overlap: container.world x=" + c0x.ToString("0") + ".." + c1x.ToString("0")
					+ " y=" + c0y.ToString("0") + ".." + c1y.ToString("0")
					+ " | scrollbar.world x=" + b0x.ToString("0") + ".." + b1x.ToString("0")
					+ " y=" + b0y.ToString("0") + ".." + b1y.ToString("0")
					+ " => overlapX=" + ovX.ToString("0") + "px overlapY=" + ovY.ToString("0") + "px"));
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager scroll chain error: " + ex.Message));
		}
	}

	private static void WorldRect(RectTransform rt, out float xmin, out float xmax, out float ymin, out float ymax)
	{
		xmin = 0f;
		xmax = 0f;
		ymin = 0f;
		ymax = 0f;
		if (rt == null)
		{
			return;
		}
		Vector3 a = rt.TransformPoint(new Vector3(rt.rect.xMin, rt.rect.yMin, 0f));
		Vector3 b = rt.TransformPoint(new Vector3(rt.rect.xMax, rt.rect.yMax, 0f));
		xmin = Math.Min(a.x, b.x);
		xmax = Math.Max(a.x, b.x);
		ymin = Math.Min(a.y, b.y);
		ymax = Math.Max(a.y, b.y);
	}

	/// <summary>v1.1.4：字母分组小标题行（"A"…"Z" / "0-9" / "#"）：小字号灰字，样式同旧分类行。</summary>
	private static float AddCategoryRow(Transform container, string text)
	{
		try
		{
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			GameObject row = new GameObject("MM_CatRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 32f);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.offsetMin = new Vector2(4f, 2f);
			trt.offsetMax = new Vector2(-4f, -2f);
			Text txt = tgo.AddComponent<Text>();
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 16;
			txt.fontStyle = FontStyle.Bold;
			txt.color = new Color(0.75f, 0.75f, 0.75f, 1f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.text = text;
			return 32f;
		}
		catch
		{
			return 0f;
		}
	}

	/// <summary>列表底部的感谢行（双语）。</summary>
	private static void AddThanksRow(Transform container)
	{
		try
		{
			GameObject row = new GameObject("MM_ThanksRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 36f);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.sizeDelta = new Vector2(-24f, 0f);
			trt.offsetMin = new Vector2(12f, 0f);
			trt.offsetMax = new Vector2(-12f, 0f);
			Text txt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 18;
			txt.fontStyle = FontStyle.Italic;
			txt.color = new Color(0.75f, 0.75f, 0.78f, 0.9f);
			txt.alignment = TextAnchor.MiddleCenter;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.text = Plugin.DefaultChinese ? "感谢你的使用，爱来自Zhaa_shi" : "Thanks for using, love from Zhaa_shi";
		}
		catch
		{
		}
	}

	/// <summary>
	/// mod 名称标题（可点击展开/折叠）。自建 uGUI 行，样式对齐原生设置项 label（字号/颜色/字体），
	/// 宽度交给 LayoutGroup，文字拉伸到行宽、左对齐、字号自适应。
	/// </summary>
	private static float AddSectionButton(SettingsGUI_V2 s, Transform container, string name, bool expanded, string rawName)
	{
		try
		{
			// 从原生控件模板读 label 样式（更贴近原版设置页）
			Font font = null;
			int fontSize = 20;
			Color labelColor = Color.white;
			try
			{
				GameObject tpl = Templates.slider ?? Templates.toggle ?? Templates.dropdown;
				if (tpl != null && tpl.transform.childCount > 0)
				{
					Text refT = tpl.transform.GetChild(0).GetComponent<Text>();
					if (refT != null)
					{
						font = (refT.font != null) ? refT.font : null;
						fontSize = Math.Max(16, refT.fontSize);
						labelColor = refT.color;
					}
				}
			}
			catch
			{
			}
			if (font == null)
			{
				font = GetNativeFont(s);
			}
			GameObject row = new GameObject("MM_ModTitle");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			// v1.1.4：按实测宽度判断是否截断。可用宽度 = viewport 实宽 - 标签两侧边距(24) - 滚动条余量(30)；
			// 放不下才截断省略号，展开行仍显示完整原始名。
			string prefix = expanded ? "v " : "> ";
			float availWidth = TitleAvailWidth(s);
			bool longName = MeasuresWiderThan(rawName, font, fontSize + 2, prefix, availWidth);
			float rowH = (expanded && longName) ? 64f : 42f;
			rt.sizeDelta = new Vector2(0f, rowH);
			// label（锚点拉伸到行宽，左对齐）
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.sizeDelta = new Vector2(-24f, 38f);
			trt.anchoredPosition = Vector2.zero;
			Text txt = tgo.AddComponent<Text>();
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = fontSize + 2;
			txt.fontStyle = FontStyle.Bold;
			txt.color = labelColor;
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.resizeTextForBestFit = true;
			txt.resizeTextMinSize = 12;
			txt.resizeTextMaxSize = fontSize + 2;
			// 超长 mod 名截断为省略号（仅当实测宽度放不下；二分收敛到可容纳的前缀长度）
			string display = name;
			if (longName)
			{
				display = EllipsizeToFit(name, font, fontSize + 2, prefix, availWidth);
			}
			txt.text = prefix + display;
			// 展开时在标题下补一行完整原始名（小字、降透明度），长名字不再丢失
			if (expanded && longName)
			{
				GameObject sub = new GameObject("MM_ModFullName");
				sub.transform.SetParent(row.transform, false);
				RectTransform srt = sub.AddComponent<RectTransform>();
				srt.anchorMin = new Vector2(0f, 0f);
				srt.anchorMax = new Vector2(1f, 0f);
				srt.pivot = new Vector2(0.5f, 0f);
				srt.sizeDelta = new Vector2(-24f, 20f);
				srt.anchoredPosition = new Vector2(0f, -2f);
				Text stxt = sub.AddComponent<Text>();
				if (font != null)
				{
					stxt.font = font;
				}
				stxt.fontSize = Math.Max(12, fontSize - 5);
				stxt.color = new Color(labelColor.r, labelColor.g, labelColor.b, 0.65f);
				stxt.alignment = TextAnchor.MiddleLeft;
				stxt.horizontalOverflow = HorizontalWrapMode.Overflow;
				stxt.text = rawName;
			}
			// 透明点击区（覆盖整行）
			GameObject hit = new GameObject("HitArea");
			hit.transform.SetParent(row.transform, false);
			RectTransform hrt = hit.AddComponent<RectTransform>();
			hrt.anchorMin = Vector2.zero;
			hrt.anchorMax = Vector2.one;
			hrt.sizeDelta = Vector2.zero;
			hrt.offsetMin = Vector2.zero;
			hrt.offsetMax = Vector2.zero;
			UnityEngine.UI.Image img = hit.AddComponent<UnityEngine.UI.Image>();
			img.color = new Color(1f, 1f, 1f, 0.01f);
			UnityEngine.UI.Button btn = hit.AddComponent<UnityEngine.UI.Button>();
			btn.transition = UnityEngine.UI.Selectable.Transition.None;
			string modName = rawName;
			btn.onClick.RemoveAllListeners();
			System.Action act = delegate
			{
				ToggleMod(modName);
			};
			btn.onClick.AddListener(act);
			return rowH;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager header error: " + ex.Message));
			return 0f;
		}
	}

	/// <summary>v1.1.4：测宽探针设置（scaleFactor=1 时 GetPreferredWidth 返回 UI 单位像素）。</summary>
	private static float MeasureTextWidth(string text, Font font, int fontSize)
	{
		try
		{
			if (string.IsNullOrEmpty(text))
			{
				return 0f;
			}
			TextGenerationSettings settings = new TextGenerationSettings();
			if (font != null)
			{
				settings.font = font;
			}
			settings.fontSize = fontSize;
			settings.fontStyle = FontStyle.Bold;
			settings.scaleFactor = 1f;
			settings.horizontalOverflow = HorizontalWrapMode.Overflow;
			settings.verticalOverflow = VerticalWrapMode.Overflow;
			settings.generateOutOfBounds = true;
			TextGenerator gen = new TextGenerator();
			float w = gen.GetPreferredWidth(text, settings);
			return w;
		}
		catch
		{
			return -1f;
		}
	}

	/// <summary>v1.1.4：标题行内可用像素宽。从 SettingsGUI_V2 所在 Canvas 实测（分辨率无关），
	/// 探测失败退回 1920 宽的经验值。内容宽约为屏幕宽 45%（原生设置页布局），标签左右各留 12px + 滚动条 30px。</summary>
	private static float TitleAvailWidth(SettingsGUI_V2 s)
	{
		try
		{
			Transform cp = (s != null) ? s.contentPage : null;
			if (cp != null)
			{
				RectTransform rt = cp.GetComponent<RectTransform>();
				if (rt != null && rt.rect.width > 10f)
				{
					return rt.rect.width - 24f - 30f;
				}
			}
		}
		catch
		{
		}
		return 1920f * 0.45f - 54f;
	}

	/// <summary>v1.1.4：标题宽度实测。availWidth = 行内真实可用像素（viewport 宽 - 标签边距 - 滚动条余量）。</summary>
	private static bool MeasuresWiderThan(string text, Font font, int fontSize, string prefix, float availWidth)
	{
		if (text == null || font == null)
		{
			return false;
		}
		float w = MeasureTextWidth(prefix + text, font, fontSize);
		if (w < 0f)
		{
			return false;
		}
		return w > availWidth;
	}

	/// <summary>二分收敛：保留能放进可用宽度的最长前缀 + "…"（保底至少留 3 个字符 + 省略号）。</summary>
	private static string EllipsizeToFit(string text, Font font, int fontSize, string prefix, float availWidth)
	{
		try
		{
			if (string.IsNullOrEmpty(text) || font == null)
			{
				return text;
			}
			if (MeasureTextWidth(prefix + text + "…", font, fontSize) <= availWidth)
			{
				return text + "…";
			}
			int lo = 3;
			int hi = text.Length - 1;
			int best = 3;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				if (MeasureTextWidth(prefix + text.Substring(0, mid) + "…", font, fontSize) <= availWidth)
				{
					best = mid;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}
			return text.Substring(0, best) + "…";
		}
		catch
		{
			return text;
		}
	}

	/// <summary>展开内容末尾的按钮行：重置（恢复默认值）+ 复制全部文本。</summary>
	private static void AddFooterRow(Transform container, string modName, ConfigFile cfg)
	{
		try
		{
			if (cfg == null)
			{
				return;
			}
			GameObject row = new GameObject("MM_FooterRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 38f);
			// 注：v1.0.32 起无【保存】按钮——退出设置（Esc/继续）时自动保存所有暂存改动
			// 重置按钮（最右）
			MakeFooterButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(96f, 30f), Plugin.DefaultChinese ? "重置全部" : "Reset all", delegate
			{
				ResetModSettings(cfg, modName);
			});
			// 复制按钮（重置按钮左侧）
			Text copyTxt = null;
			copyTxt = MakeFooterButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-116f, 0f), new Vector2(96f, 30f), Plugin.DefaultChinese ? "复制全部文本" : "Copy all text", delegate
			{
				CopyModText(modName, cfg);
				FlashText(copyTxt, Plugin.DefaultChinese ? "已复制 ✓" : "Copied ✓", 1.5f);
			});
		}
		catch
		{
		}
	}

	private static Text MakeFooterButton(GameObject row, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, string label, System.Action onClick)
	{
		Text result = null;
		try
		{
		GameObject btnGo = new GameObject("FooterBtn");
		btnGo.transform.SetParent(row.transform, false);
		RectTransform brt = btnGo.AddComponent<RectTransform>();
		brt.anchorMin = anchorMin;
		brt.anchorMax = anchorMax;
		brt.pivot = new Vector2(1f, 0.5f);
		brt.anchoredPosition = anchoredPos;
		brt.sizeDelta = size;
		UnityEngine.UI.Image bimg = btnGo.AddComponent<UnityEngine.UI.Image>();
		bimg.color = new Color(0.25f, 0.26f, 0.3f, 0.95f);
		UnityEngine.UI.Button bbtn = btnGo.AddComponent<UnityEngine.UI.Button>();
		bbtn.transition = UnityEngine.UI.Selectable.Transition.None;
		GameObject btgo = new GameObject("Text");
		btgo.transform.SetParent(btnGo.transform, false);
		RectTransform btrt = btgo.AddComponent<RectTransform>();
		btrt.anchorMin = Vector2.zero;
		btrt.anchorMax = Vector2.one;
		btrt.sizeDelta = Vector2.zero;
		btrt.offsetMin = Vector2.zero;
		btrt.offsetMax = Vector2.zero;
		Text btxt = btgo.AddComponent<Text>();
		Font font = GetNativeFont(SettingsGUI_V2.instance);
		if (font != null)
		{
			btxt.font = font;
		}
		btxt.fontSize = 15;
		btxt.color = Color.white;
		btxt.alignment = TextAnchor.MiddleCenter;
		btxt.resizeTextForBestFit = true;
		btxt.resizeTextMinSize = 10;
		btxt.resizeTextMaxSize = 15;
		btxt.text = label;
		bbtn.onClick.RemoveAllListeners();
		bbtn.onClick.AddListener(onClick);
		result = btxt;
		}
		catch
		{
		}
		return result;
	}

	/// <summary>重置本 mod 所有配置为默认值并重建页面（暂存语义：落盘由【保存】按钮触发）。</summary>
	internal static void ResetModSettings(ConfigFile cfg, string modName)
	{
		try
		{
			int n = 0;
			foreach (ConfigEntryBase e in cfg.Values)
			{
				try
				{
					if (e == null)
					{
						continue;
					}
					StageValue(cfg, e, e.DefaultValue);
					n++;
				}
				catch
				{
				}
			}
			Plugin.ModLog.LogInfo((object)("ModManager: reset " + n + " settings of '" + modName + "' to defaults (pending save)."));
			// 重建页面显示新值
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s != null)
			{
				OpenMyPage(s);
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager reset error: " + ex.Message));
		}
	}

	/// <summary>复制本 mod 的全部文本（mod 名 + 每个配置项的键名/描述，不含值）到剪贴板。</summary>
	internal static void CopyModText(string modName, ConfigFile cfg)
	{
		try
		{
			if (cfg == null)
			{
				return;
			}
			string text = modName + "\n";
			foreach (ConfigEntryBase e in cfg.Values)
			{
				if (e == null)
				{
					continue;
				}
				text += "  " + EntryCopyText(e) + "\n";
			}
			GUIUtility.systemCopyBuffer = text;
			try
			{
				Corvostudio.UI.Hint hint = Corvostudio.UI.Hint.instance;
				if (hint != null)
				{
					Corvostudio.UI.Hint.Display(Plugin.DefaultChinese ? "已复制到剪贴板" : "Copied to clipboard", 2f, true, true);
				}
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager copy error: " + ex.Message));
		}
	}

	/// <summary>数字安全字体（v1.0.74 重写）：游戏 2.1.0 更新后设置页改用简体中文字体
	/// （无 ASCII 数字字形）→ 数字不显示。用**运行时渲染探测**找真正能画出数字的字体：
	/// ① FontList.font_default（游戏默认拉丁字体）→ ② 全部已加载 Font → ③ 滑块模板
	/// → ④ 活动设置页含数字的 Text → ⑤ GUI.skin/LegacyRuntime（不依赖已移除的 PhaseBarGUI）。
	/// 探测：隐藏 uGUI Text 设 "0123456789" 量 preferredWidth（与输入框同一渲染路径，
	/// 缺字形宽度≈0）。解析一次缓存；字体来自游戏资源，mod 只引用不创建。</summary>
	private static Font cachedDigitFont;

	private static bool digitFontResolved;

	private static Font GetDigitSafeFont(SettingsGUI_V2 s)
	{
		if (digitFontResolved)
		{
			return cachedDigitFont;
		}
		digitFontResolved = true;
		Font found = null;
		string src = null;
		// ① FontList：游戏默认/粗体/历史字体（Latin 字体必有数字；中文简/繁字体无数字会被探测剔除）
		try
		{
			FontList fl = ResourcesManager.instance != null ? ResourcesManager.instance.fonts : null;
			if (fl != null)
			{
				Font[] cands = new Font[] { fl.font_default, fl.font_default_bold, fl.font_default_historic };
				foreach (Font f in cands)
				{
					if (f != null && FontRendersDigits(f))
					{
						found = f;
						src = "FontList.font_default";
						break;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager digit font FontList error: " + ex.Message));
		}
		// ② 全部已加载字体
		if (found == null)
		{
			try
			{
				Font[] all = Resources.FindObjectsOfTypeAll<Font>();
				if (all != null)
				{
					foreach (Font f in all)
					{
						if (f == null || f == found)
						{
							continue;
						}
						if (FontRendersDigits(f))
						{
							found = f;
							src = "loaded fonts";
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager digit font loaded-fonts error: " + ex.Message));
			}
		}
		// ③ 滑块模板字体（探测通过才用——v1.0.73 直接取模板字体踩中 CJK 无数字的坑）
		if (found == null)
		{
			try
			{
				if (Templates.slider != null)
				{
					UnityEngine.UI.Text[] ts = Templates.slider.GetComponentsInChildren<UnityEngine.UI.Text>(true);
					if (ts != null)
					{
						foreach (UnityEngine.UI.Text t in ts)
						{
							if (t != null && t.font != null && FontRendersDigits(t.font))
							{
								found = t.font;
								src = "slider template";
								break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager digit font slider fallback error: " + ex.Message));
			}
		}
		// ④ 活动设置页里含数字的 Text 的字体（探测通过才用）
		if (found == null)
		{
			try
			{
				if (s != null && s.transform != null)
				{
					UnityEngine.UI.Text[] all = s.transform.GetComponentsInChildren<UnityEngine.UI.Text>(true);
					if (all != null)
					{
						Font titleFont = null;
						try
						{
							titleFont = (s.title != null) ? s.title.font : null;
						}
						catch
						{
						}
						foreach (UnityEngine.UI.Text t in all)
						{
							if (t == null || t.font == null || t.font == titleFont)
							{
								continue;
							}
							if (HasDigit(t.text) && FontRendersDigits(t.font))
							{
								found = t.font;
								src = "live settings digit text";
								break;
							}
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager digit font live scan error: " + ex.Message));
			}
		}
		// ⑤ GUI.skin 字体（游戏更新后 PhaseBarGUI 已移除；不再依赖旧 API）
		if (found == null)
		{
			try
			{
				Font f = GUI.skin != null ? GUI.skin.font : null;
				if (f != null && FontRendersDigits(f))
				{
					found = f;
					src = "GUI.skin";
				}
			}
			catch
			{
			}
		}
		// ⑥ 引擎内置 LegacyRuntime：ASCII 全字形，数字绝对可渲染（v1.0.75 兜底，
		// 游戏 2.1.0 所有已加载字体可能全是无 ASCII 的 CJK 子集字体）
		if (found == null)
		{
			try
			{
				Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
				if (f != null)
				{
					found = f;
					src = "LegacyRuntime builtin";
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager digit font LegacyRuntime error: " + ex.Message));
			}
		}
		cachedDigitFont = found;
		Plugin.ModLog.LogInfo((object)("ModManager digit font: " + (found != null ? ("'" + found.name + "' (source=" + src + ")") : "NONE — digits will not render")));
		return cachedDigitFont;
	}

	/// <summary>运行时探测：该字体能否真正渲染数字。
	/// v1.0.75：preferredWidth 会为缺失字形给占位宽度（CJK 子集字体被误判通过），
	/// 改用**网格顶点数**（PopulateWithErrors 后 vertexCount>0 = 字形真实生成）；
	/// 并按字体名硬排除已知 CJK 子集字体（游戏 2.1.0 的简体/繁体中文/日文字体无 ASCII）。</summary>
	private static bool FontRendersDigits(Font f)
	{
		try
		{
			if (f == null)
			{
				return false;
			}
			string n = f.name;
			if (!string.IsNullOrEmpty(n))
			{
				string l = n.ToLowerInvariant();
				if (l.Contains("chinese") || l.Contains("japanese") || l.Contains("cjk") ||
					l.Contains("simplified") || l.Contains("traditional") || l.Contains("kr"))
				{
					return false; // 已知 CJK 子集字体：无 ASCII 数字
				}
			}
			GameObject go = new GameObject("MMFontProbe");
			UnityEngine.UI.Text t = go.AddComponent<UnityEngine.UI.Text>();
			t.font = f;
			t.fontSize = 20;
			TextGenerator gen = t.cachedTextGenerator;
			TextGenerationSettings settings = t.GetGenerationSettings(Vector2.zero);
			gen.PopulateWithErrors("0123456789", settings, t.gameObject);
			bool ok = gen.vertexCount > 0;
			UnityEngine.Object.Destroy(go);
			return ok;
		}
		catch
		{
			return false;
		}
	}

	private static bool HasDigit(string text)
	{
		if (string.IsNullOrEmpty(text))
		{
			return false;
		}
		for (int i = 0; i < text.Length; i++)
		{
			char c = text[i];
			if (c >= '0' && c <= '9')
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>原生设置字体：SettingsGUI_V2.title 的字体（原生设置页标题字体），失败回退 GUI.skin/引擎。</summary>
	private static Font GetNativeFont(SettingsGUI_V2 s)
	{
		try
		{
			if (s != null && s.title != null && s.title.font != null)
			{
				return s.title.font;
			}
		}
		catch
		{
		}
		try
		{
			return GUI.skin != null ? GUI.skin.font : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>填充一个 mod 的全部配置项：多分区时按分区显示小标题（可折叠），单分区保持平铺。</summary>
	private static void FillModEntries(PluginConfig p, Transform container)
	{
		try
		{
			if (p.cfg == null || p.cfg.Count == 0)
			{
				AddFallbackTextRow(container, Plugin.DefaultChinese ? "该 mod 没有可调设置" : "This mod has no configurable settings");
				return;
			}
			List<ConfigEntryBase> entries = new List<ConfigEntryBase>();
			foreach (ConfigEntryBase entry in p.cfg.Values)
			{
				if (entry != null)
				{
					entries.Add(entry);
				}
			}
			if (entries.Count == 0)
			{
				return;
			}
			// 按分区分组（保持出现顺序）
			List<string> sections = new List<string>();
			Dictionary<string, List<ConfigEntryBase>> bySection = new Dictionary<string, List<ConfigEntryBase>>(StringComparer.OrdinalIgnoreCase);
			foreach (ConfigEntryBase e in entries)
			{
				string sec = e.Definition.Section;
				if (string.IsNullOrEmpty(sec))
				{
					sec = "General";
				}
				if (!bySection.TryGetValue(sec, out List<ConfigEntryBase> list))
				{
					list = new List<ConfigEntryBase>();
					bySection[sec] = list;
					sections.Add(sec);
				}
				list.Add(e);
			}
			if (sections.Count >= 2)
			{
				foreach (string sec in sections)
				{
					string key = p.name + "|" + sec;
					bool expanded = expandedSections.Contains(key);
					AddSectionHeader(container, HumanizeKey(sec), expanded, key);
					if (!expanded)
					{
						continue;
					}
					foreach (ConfigEntryBase e in bySection[sec])
					{
						AddSetting(p.cfg, e, container);
					}
				}
			}
			else
			{
				foreach (ConfigEntryBase e in entries)
				{
					AddSetting(p.cfg, e, container);
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager fill entries error: " + ex.Message));
		}
	}

	/// <summary>分区小标题（可点击折叠/展开）。</summary>
	private static void AddSectionHeader(Transform container, string title, bool expanded, string key)
	{
		try
		{
			GameObject row = new GameObject("MM_SectionTitle");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 32f);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.sizeDelta = new Vector2(-24f, 30f);
			trt.anchoredPosition = Vector2.zero;
			Text txt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 18;
			txt.fontStyle = FontStyle.Bold;
			txt.color = new Color(0.85f, 0.85f, 0.9f, 0.95f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.text = (expanded ? "▾ " : "▸ ") + title;
			// 点击切换折叠并重建整页（System.Action → UnityAction 隐式转换，与 Footer 按钮同一已验证模式）
			UnityEngine.UI.Button btn = row.AddComponent<UnityEngine.UI.Button>();
			btn.transition = UnityEngine.UI.Selectable.Transition.None;
			System.Action toggleAction = delegate
			{
				ToggleSection(key);
			};
			btn.onClick.RemoveAllListeners();
			btn.onClick.AddListener(toggleAction);
		}
		catch
		{
		}
	}

	/// <summary>切换分区展开状态并重建整页。</summary>
	internal static void ToggleSection(string key)
	{
		try
		{
			if (!expandedSections.Add(key))
			{
				expandedSections.Remove(key);
			}
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s != null)
			{
				OpenMyPage(s);
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager section toggle error: " + ex.Message));
		}
	}

	/// <summary>切换 mod 展开状态并重建整页。</summary>
	internal static void ToggleMod(string name)
	{
		try
		{
			if (!expandedMods.Add(name))
			{
				expandedMods.Remove(name);
			}
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s == null)
			{
				return;
			}
			OpenMyPage(s);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager toggle error: " + ex.Message));
		}
	}

	/// <summary>克隆模板、挂到自动布局容器、设置 label 文本。返回是否成功创建。</summary>
	private static bool SetupControl(GameObject go, Transform container, string label, string valueText)
	{
		if (go == null)
		{
			return false;
		}
		go.transform.SetParent(container, false);
		RectTransform rt = go.GetComponent<RectTransform>();
		if (rt != null)
		{
			// LayoutGroup 控制宽度，这里只固定高度
			rt.sizeDelta = new Vector2(rt.sizeDelta.x, rt.sizeDelta.y);
		}
		try
		{
			Text t = go.transform.GetChild(0).GetComponent<Text>();
			if (t != null)
			{
				t.text = label;
			}
		}
		catch
		{
		}
		return true;
	}

	private static bool AddSetting(ConfigFile cfg, ConfigEntryBase entry, Transform container)
	{
		try
		{
			// label 人性化键名（驼峰/下划线拆词）
			string label = HumanizeKey(entry.Definition.Key);
			Type t = entry.SettingType;
			bool ok = false;
			if (t == typeof(bool))
			{
				ok = AddToggle(cfg, entry, container, label);
			}
			else if (t == typeof(float))
			{
				ok = AddNumericInput(cfg, entry, container, label, true);
			}
			else if (t == typeof(int))
			{
				ok = AddNumericInput(cfg, entry, container, label, false);
			}
			else if (t == typeof(string))
			{
				// 键名含 key/toggle 且选项是按键列表 → 用"点击改键"按钮（与 KeyCode 热键统一）；
				// 其余 string → 下拉
				if (IsHotkeyEntry(entry) && LooksLikeKeyList(entry))
				{
					ok = AddHotkeyRebind(cfg, entry, container, label, false);
				}
				else
				{
					ok = AddDropdown(cfg, entry, container, label);
				}
			}
			else if (t == typeof(KeyCode))
			{
				// 原生快捷键（KeyCode 类型）→ 点击改键按钮（按下新键即捕获）
				ok = AddHotkeyRebind(cfg, entry, container, label, true);
			}
			else if (t.IsEnum)
			{
				// 枚举：键名含 key/toggle → 点击改键按钮（如 InputSystem Key 热键）；
				// 其他枚举 → 下拉选择
				if (IsHotkeyEntry(entry))
				{
					ok = AddHotkeyRebind(cfg, entry, container, label, false, t);
				}
				else
				{
					ok = AddDropdownEnum(cfg, entry, container, label);
				}
			}
			if (ok)
			{
				// 控件下方的小字介绍
				string desc = GetDescription(entry);
				if (!string.IsNullOrEmpty(desc))
				{
					AddDescriptionRow(container, desc);
				}
				// 每个子选项独立的复制/重置按钮（放最底部）
				AddEntryActions(container, cfg, entry, label);
			}
			else
			{
				// v1.1.6：全局兜底——任何类型/模板路径失败都渲染只读文本行，配置项永不静默消失
				AddFallbackTextRow(container, label + ": " + FormatEntryValue(entry));
				string desc = GetDescription(entry);
				if (!string.IsNullOrEmpty(desc))
				{
					AddDescriptionRow(container, desc);
				}
				ok = true;
			}
			return ok;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager add setting error: " + ex.Message));
			return false;
		}
	}

	/// <summary>配置项小字介绍：直接用 mod 自带的 BepInEx 描述原文（中文 mod 显示中文简介，英文 mod 显示英文简介）。</summary>
	private static string GetDescription(ConfigEntryBase entry)
	{
		try
		{
			return (entry.Description != null) ? entry.Description.Description : null;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>控件下方的小字介绍行（灰色小字，按文本长度估算行高，稳定不重叠）。</summary>
	private static void AddDescriptionRow(Transform container, string text)
	{
		try
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			// 估算行数（保守：英文 8px/字、中文 16px/字，行宽按 420px，另加 1 行余量——
			// 估算偏小会导致文字纵向溢出压到下一行的按钮）
			float width = 0f;
			foreach (char ch in text)
			{
				width += (ch > 127) ? 16f : 8f;
			}
			int lines = Math.Max(1, (int)Math.Ceiling(width / 420f) + 1);
			float h = lines * 21f + 10f;
			GameObject row = new GameObject("MM_DescRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, h);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.sizeDelta = new Vector2(-24f, 0f);
			trt.offsetMin = new Vector2(12f, 2f);
			trt.offsetMax = new Vector2(-12f, -2f);
			Text txt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 15;
			txt.fontStyle = FontStyle.Normal;
			txt.color = new Color(0.68f, 0.68f, 0.72f, 0.95f);
			txt.alignment = TextAnchor.UpperLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Wrap;
			txt.verticalOverflow = VerticalWrapMode.Overflow;
			txt.text = text;
		}
		catch
		{
		}
	}

	/// <summary>检测已安装 mod 之间的热键冲突（KeyCode 类型或键名含 key/toggle 的配置）。</summary>
	internal static List<string> DetectHotkeyConflicts(List<PluginConfig> plugins)
	{
		List<string> result = new List<string>();
		try
		{
			Dictionary<string, string> map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
			foreach (PluginConfig p in plugins)
			{
				if (p == null || p.cfg == null)
				{
					continue;
				}
				foreach (ConfigEntryBase e in p.cfg.Values)
				{
					if (e == null || !IsHotkeyEntry(e))
					{
						continue;
					}
					string value = HotkeyValue(e);
					if (string.IsNullOrEmpty(value) || value == "None" || value == "0")
					{
						continue;
					}
					string who = p.name + " (" + HumanizeKey(e.Definition.Key) + ")";
					if (map.TryGetValue(value, out string first))
					{
						result.Add(Plugin.DefaultChinese
							? "⚠ 热键冲突：" + first + " 与 " + who + " 都使用「" + value + "」"
							: "⚠ Hotkey conflict: " + first + " and " + who + " both use '" + value + "'");
					}
					else
					{
						map[value] = who;
					}
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager hotkey conflict scan error: " + ex.Message));
		}
		return result;
	}

	internal static bool IsHotkeyEntry(ConfigEntryBase e)
	{
		try
		{
			if (e.SettingType == typeof(KeyCode))
			{
				return true;
			}
			if (e.SettingType == typeof(string))
			{
				string key = e.Definition.Key;
				if (string.IsNullOrEmpty(key))
				{
					return false;
				}
				string low = key.ToLowerInvariant();
				return low.Contains("key") || low.Contains("toggle");
			}
			if (e.SettingType.IsEnum)
			{
				// 枚举热键（如 InputSystem Key）：键名含 key/toggle → 点击改键
				string key = e.Definition.Key;
				if (string.IsNullOrEmpty(key))
				{
					return false;
				}
				string low = key.ToLowerInvariant();
				return low.Contains("key") || low.Contains("toggle");
			}
		}
		catch
		{
		}
		return false;
	}

	internal static string HotkeyValue(ConfigEntryBase e)
	{
		try
		{
			object bv = e.BoxedValue;
			if (bv == null)
			{
				return "";
			}
			if (e.SettingType == typeof(KeyCode))
			{
				return bv.ToString();
			}
			if (e.SettingType == typeof(string))
			{
				return (string)bv;
			}
			if (e.SettingType.IsEnum)
			{
				return bv.ToString();
			}
		}
		catch
		{
		}
		return "";
	}

	/// <summary>热键冲突警告行（顶部黄色提示）。</summary>
	private static void AddWarningRow(Transform container, string text)
	{
		try
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			float width = 0f;
			foreach (char ch in text)
			{
				width += (ch > 127) ? 16f : 9f;
			}
			int lines = Math.Max(1, (int)Math.Ceiling(width / 500f));
			float h = lines * 21f + 10f;
			GameObject row = new GameObject("MM_WarnRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, h);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0f);
			trt.anchorMax = new Vector2(1f, 1f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.sizeDelta = new Vector2(-24f, 0f);
			trt.offsetMin = new Vector2(12f, 2f);
			trt.offsetMax = new Vector2(-12f, -2f);
			Text txt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 15;
			txt.fontStyle = FontStyle.Bold;
			txt.color = new Color(1f, 0.78f, 0.25f, 1f);
			txt.alignment = TextAnchor.UpperLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Wrap;
			txt.verticalOverflow = VerticalWrapMode.Overflow;
			txt.text = text;
		}
		catch
		{
		}
	}

	private static bool AddToggle(ConfigFile cfg, ConfigEntryBase entry, Transform container, string label)
	{
		GameObject tpl = Templates.toggle;
		GameObject go;
		if (tpl != null)
		{
			go = Templates.Instantiate(tpl);
			if (go == null)
			{
				return false;
			}
			SetupControl(go, container, label, null);
			UnityEngine.UI.Toggle tg = go.transform.GetChild(1).GetComponent<UnityEngine.UI.Toggle>();
			if (tg != null)
			{
				bool cur = (bool)GetStaged(cfg, entry, entry.BoxedValue);
				tg.isOn = cur;
				tg.onValueChanged.RemoveAllListeners();
				// 轮询监视（不绑定 onValueChanged —— UnityAction<bool> 桥接会把 false 也传成 true）
				watches.Add(new ControlWatch { toggle = tg, cfg = cfg, entry = entry, lastBool = cur, hasBool = true });
			}
			return true;
		}
		if (Templates.dropdown != null)
		{
			go = Templates.Instantiate(Templates.dropdown);
			if (go == null)
			{
				return false;
			}
			SetupControl(go, container, label, null);
			UnityEngine.UI.Dropdown dd = go.transform.GetChild(1).GetComponent<UnityEngine.UI.Dropdown>();
			if (dd != null)
			{
				Il2CppSystem.Collections.Generic.List<Dropdown.OptionData> opts = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
				opts.Add(new Dropdown.OptionData("Off"));
				opts.Add(new Dropdown.OptionData("On"));
				dd.ClearOptions();
				dd.AddOptions(opts);
				int idx = ((bool)GetStaged(cfg, entry, entry.BoxedValue)) ? 1 : 0;
				dd.value = idx;
				dd.onValueChanged.RemoveAllListeners();
				// 轮询监视
				watches.Add(new ControlWatch { dropdown = dd, cfg = cfg, entry = entry, lastInt = idx, wholeNumbers = false });
			}
			return true;
		}
		// 无模板：文本行兜底（保证不空白）
		AddFallbackTextRow(container, label + ": " + (((bool)GetStaged(cfg, entry, entry.BoxedValue)) ? "On" : "Off"));
		return true;
	}

	private static bool AddNumericInput(ConfigFile cfg, ConfigEntryBase entry, Transform container, string label, bool isFloat)
	{
		try
		{
			float min = 0f;
			float max = 1000f;
			bool hasRange = false;
			AcceptableValueBase av = (entry.Description != null) ? entry.Description.AcceptableValues : null;
			if (isFloat)
			{
				if (av is AcceptableValueRange<float> rng)
				{
					min = rng.MinValue;
					max = rng.MaxValue;
					hasRange = true;
				}
			}
			else if (av is AcceptableValueRange<int> irng)
			{
				min = irng.MinValue;
				max = irng.MaxValue;
				hasRange = true;
			}

			Font labelFont = GetNativeFont(SettingsGUI_V2.instance);
			Font font = GetDigitSafeFont(SettingsGUI_V2.instance);
			// 双行布局：上行标签 + 下行输入框/范围提示，彻底避免文字与输入框重叠。
			GameObject row = new GameObject("MM_NumRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 64f);

			// 上：标签（整行宽度，右端不与下方输入框争空间）
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.anchoredPosition = new Vector2(0f, 16f);
			trt.sizeDelta = new Vector2(-12f, 26f);
			Text txt = tgo.AddComponent<Text>();
			if (labelFont != null)
			{
				txt.font = labelFont;
			}
			txt.fontSize = 20;
			txt.color = new Color(0.85f, 0.85f, 0.85f, 1f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.resizeTextForBestFit = true;
			txt.resizeTextMinSize = 10;
			txt.resizeTextMaxSize = 20;
			txt.text = label;

			// 下左：范围提示（有范围时显示）
			if (hasRange)
			{
				GameObject rgo = new GameObject("RangeHint");
				rgo.transform.SetParent(row.transform, false);
				RectTransform rrt = rgo.AddComponent<RectTransform>();
				rrt.anchorMin = new Vector2(0f, 0f);
				rrt.anchorMax = new Vector2(1f, 0f);
				rrt.pivot = new Vector2(0.5f, 0.5f);
				rrt.anchoredPosition = new Vector2(0f, 7f);
				rrt.sizeDelta = new Vector2(-216f, 22f);
				Text rtxt = rgo.AddComponent<Text>();
				if (font != null)
				{
					rtxt.font = font;
				}
				rtxt.fontSize = 13;
				rtxt.color = new Color(0.5f, 0.5f, 0.5f, 1f);
				rtxt.alignment = TextAnchor.MiddleLeft;
				rtxt.horizontalOverflow = HorizontalWrapMode.Overflow;
				rtxt.text = (isFloat ? min.ToString("0.##") : ((int)min).ToString()) + " – " + (isFloat ? max.ToString("0.##") : ((int)max).ToString());
			}

			// 下右：数字输入框
			GameObject igo = new GameObject("Value");
			igo.transform.SetParent(row.transform, false);
			RectTransform irt = igo.AddComponent<RectTransform>();
			UnityEngine.UI.Image img = igo.AddComponent<UnityEngine.UI.Image>();
			UnityEngine.UI.InputField field = igo.AddComponent<UnityEngine.UI.InputField>();
			irt.anchorMin = new Vector2(1f, 0f);
			irt.anchorMax = new Vector2(1f, 0f);
			irt.pivot = new Vector2(1f, 0.5f);
			irt.anchoredPosition = new Vector2(-8f, 7f);
			irt.sizeDelta = new Vector2(200f, 26f);
			img.color = new Color(0.13f, 0.13f, 0.15f, 0.95f);

			GameObject igoT = new GameObject("Text");
			igoT.transform.SetParent(igo.transform, false);
			RectTransform itrt = igoT.AddComponent<RectTransform>();
			itrt.anchorMin = new Vector2(0f, 0f);
			itrt.anchorMax = new Vector2(1f, 1f);
			itrt.offsetMin = new Vector2(8f, 3f);
			itrt.offsetMax = new Vector2(-8f, -3f);
			Text itxt = igoT.AddComponent<Text>();
			if (font != null)
			{
				itxt.font = font;
			}
			itxt.fontSize = 18;
			itxt.color = Color.white;
			itxt.alignment = TextAnchor.MiddleLeft;
			itxt.horizontalOverflow = HorizontalWrapMode.Overflow;
			field.textComponent = itxt;
			field.contentType = isFloat ? UnityEngine.UI.InputField.ContentType.DecimalNumber : UnityEngine.UI.InputField.ContentType.IntegerNumber;

			object cur = GetStaged(cfg, entry, entry.BoxedValue);
			field.text = isFloat ? ((float)cur).ToString("0.##") : ((int)cur).ToString();

			watches.Add(new ControlWatch
			{
				input = field,
				cfg = cfg,
				entry = entry,
				inputIsFloat = isFloat,
				hasRange = hasRange,
				rangeMin = min,
				rangeMax = max,
				lastInputText = field.text
			});
			return true;
		}
		catch
		{
			// 输入框构建失败退回原生滑条
			return AddSlider(cfg, entry, container, label, !isFloat);
		}
	}

	/// <summary>v1.1.6：自由文本输入（string 配置且无 AcceptableValueList，如颜色 #RRGGBBAA）。
	/// 双行布局同 AddNumericInput：上行标签，下行输入框。文本经轮询直写暂存（原文，含空串）。</summary>
	private static bool AddFreeTextInput(ConfigFile cfg, ConfigEntryBase entry, Transform container, string label)
	{
		try
		{
			Font labelFont = GetNativeFont(SettingsGUI_V2.instance);
			GameObject row = new GameObject("MM_TextRow_Input");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 64f);
			// 上：标签
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.anchoredPosition = new Vector2(0f, 16f);
			trt.sizeDelta = new Vector2(-12f, 26f);
			Text txt = tgo.AddComponent<Text>();
			if (labelFont != null)
			{
				txt.font = labelFont;
			}
			txt.fontSize = 20;
			txt.color = new Color(0.85f, 0.85f, 0.85f, 1f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.resizeTextForBestFit = true;
			txt.resizeTextMinSize = 10;
			txt.resizeTextMaxSize = 20;
			txt.text = label;
			// 下右：输入框
			GameObject igo = new GameObject("Value");
			igo.transform.SetParent(row.transform, false);
			RectTransform irt = igo.AddComponent<RectTransform>();
			UnityEngine.UI.Image img = igo.AddComponent<UnityEngine.UI.Image>();
			UnityEngine.UI.InputField field = igo.AddComponent<UnityEngine.UI.InputField>();
			irt.anchorMin = new Vector2(1f, 0f);
			irt.anchorMax = new Vector2(1f, 0f);
			irt.pivot = new Vector2(1f, 0.5f);
			irt.anchoredPosition = new Vector2(-8f, 7f);
			irt.sizeDelta = new Vector2(280f, 26f);
			img.color = new Color(0.13f, 0.13f, 0.15f, 0.95f);
			GameObject igoT = new GameObject("Text");
			igoT.transform.SetParent(igo.transform, false);
			RectTransform itrt = igoT.AddComponent<RectTransform>();
			itrt.anchorMin = new Vector2(0f, 0f);
			itrt.anchorMax = new Vector2(1f, 1f);
			itrt.offsetMin = new Vector2(8f, 3f);
			itrt.offsetMax = new Vector2(-8f, -3f);
			Text itxt = igoT.AddComponent<Text>();
			Font font = GetDigitSafeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				itxt.font = font;
			}
			itxt.fontSize = 18;
			itxt.color = Color.white;
			itxt.alignment = TextAnchor.MiddleLeft;
			itxt.horizontalOverflow = HorizontalWrapMode.Overflow;
			field.textComponent = itxt;
			field.contentType = UnityEngine.UI.InputField.ContentType.Standard;
			string cur;
			try
			{
				object v = GetStaged(cfg, entry, entry.BoxedValue);
				cur = (v as string) ?? (v != null ? v.ToString() : "");
			}
			catch
			{
				cur = "";
			}
			field.text = cur;
			watches.Add(new ControlWatch
			{
				input = field,
				cfg = cfg,
				entry = entry,
				isFreeText = true,
				lastInputText = field.text
			});
			return true;
		}
		catch
		{
			return false;
		}
	}

	private static bool AddSlider(ConfigFile cfg, ConfigEntryBase entry, Transform container, string label, bool wholeNumbers)
	{
		if (Templates.slider == null)
		{
			// 无模板：文本行兜底
			AddFallbackTextRow(container, label + ": " + FormatEntryValue(entry));
			return true;
		}
		GameObject go = Templates.Instantiate(Templates.slider);
		if (go == null)
		{
			return false;
		}
		SetupControl(go, container, label, null);
		UnityEngine.UI.Slider sl = go.transform.GetChild(1).GetComponent<UnityEngine.UI.Slider>();
		if (sl != null)
		{
			float min = 0f;
			float max = 100f;
			float cur = 0f;
			if (entry.SettingType == typeof(float))
			{
				cur = (float)GetStaged(cfg, entry, entry.BoxedValue);
				AcceptableValueBase av = (entry.Description != null) ? entry.Description.AcceptableValues : null;
				if (av is AcceptableValueRange<float> rng)
				{
					min = rng.MinValue;
					max = rng.MaxValue;
				}
			}
			else
			{
				cur = (int)GetStaged(cfg, entry, entry.BoxedValue);
				AcceptableValueBase av = (entry.Description != null) ? entry.Description.AcceptableValues : null;
				if (av is AcceptableValueRange<int> irng)
				{
					min = irng.MinValue;
					max = irng.MaxValue;
				}
			}
			// 滑块最大值按原 mod 设计保留，但不超过 1000（min 不变，保留原设计步进/小数规则）
			if (min <= 1000f)
			{
				max = Mathf.Min(max, 1000f);
			}
			sl.minValue = min;
			sl.maxValue = max;
			// 整数步进：int 类型或范围 ≥10 的 float 配置都用整数步进（数字只显示整数）
			bool whole = wholeNumbers || (max - min >= 10f);
			sl.wholeNumbers = whole;
			// 值显示文本：克隆模板的其余 Text 是模板占位（如 60.1%），改成真实值并随拖动更新
			List<UnityEngine.UI.Text> valueTexts = new List<UnityEngine.UI.Text>();
			try
			{
				UnityEngine.UI.Text[] texts = go.GetComponentsInChildren<UnityEngine.UI.Text>(true);
				if (texts != null)
				{
					int ti = 0;
					foreach (UnityEngine.UI.Text tx in texts)
					{
						if (tx == null)
						{
							continue;
						}
						if (ti == 0)
						{
							tx.text = label;
						}
						else
						{
							valueTexts.Add(tx);
						}
						ti++;
					}
				}
			}
			catch
			{
			}
			sl.onValueChanged.RemoveAllListeners();
			sl.value = Mathf.Clamp(cur, min, max);
			UpdateValueTexts(valueTexts, whole, sl.value);
			// 注册轮询监视（不绑定 onValueChanged —— UnityAction<float> 桥接会把参数 marshaling 成垃圾值）
			watches.Add(new ControlWatch { slider = sl, cfg = cfg, entry = entry, wholeNumbers = whole, valueTexts = valueTexts });
		}
		return true;
	}

	internal static void UpdateValueTexts(List<UnityEngine.UI.Text> texts, bool wholeNumbers, float v)
	{
		if (texts == null)
		{
			return;
		}
		string s = wholeNumbers ? ((int)v).ToString() : v.ToString("0.##");
		foreach (UnityEngine.UI.Text tx in texts)
		{
			try
			{
				if (tx != null)
				{
					tx.text = s;
				}
			}
			catch
			{
			}
		}
	}

	private static bool AddDropdown(ConfigFile cfg, ConfigEntryBase entry, Transform container, string label)
	{
		AcceptableValueBase av = (entry.Description != null) ? entry.Description.AcceptableValues : null;
		if (!(av is AcceptableValueList<string>) )
		{
			// v1.1.6：无选项列表的自由字符串（如 SquadCommand 的 #RRGGBBAA 颜色配置）→ 自由文本输入框。
			// 旧逻辑静默 return false → 整个配置项消失（SquadCommand 颜色项不显示的根因）。
			return AddFreeTextInput(cfg, entry, container, label);
		}
		if (Templates.dropdown == null)
		{
			// 无模板：文本行兜底
			AddFallbackTextRow(container, label + ": " + FormatEntryValue(entry));
			return true;
		}
		AcceptableValueList<string> lst = (AcceptableValueList<string>)av;
		if (lst.AcceptableValues == null || lst.AcceptableValues.Length == 0)
		{
			return AddFreeTextInput(cfg, entry, container, label);
		}
		string[] vals = lst.AcceptableValues;
		GameObject go = Templates.Instantiate(Templates.dropdown);
		if (go == null)
		{
			return false;
		}
		SetupControl(go, container, label, null);
		UnityEngine.UI.Dropdown dd = go.transform.GetChild(1).GetComponent<UnityEngine.UI.Dropdown>();
		if (dd != null)
		{
			Il2CppSystem.Collections.Generic.List<Dropdown.OptionData> opts = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
			int idx = 0;
			string cur = (string)GetStaged(cfg, entry, entry.BoxedValue);
			for (int i = 0; i < vals.Length; i++)
			{
				opts.Add(new Dropdown.OptionData(HumanizeKey(vals[i])));
				if (vals[i] == cur)
				{
					idx = i;
				}
			}
			dd.ClearOptions();
			dd.AddOptions(opts);
			dd.value = Mathf.Clamp(idx, 0, vals.Length - 1);
			dd.onValueChanged.RemoveAllListeners();
			// 注册轮询监视（不绑定 onValueChanged —— 委托桥接不可靠）
			watches.Add(new ControlWatch { dropdown = dd, cfg = cfg, entry = entry, lastInt = Mathf.Clamp(idx, 0, vals.Length - 1) });
		}
		return true;
	}

	/// <summary>枚举配置（非热键）→ 下拉选择（Enum.GetNames + HumanizeKey）。</summary>
	private static bool AddDropdownEnum(ConfigFile cfg, ConfigEntryBase entry, Transform container, string label)
	{
		try
		{
			Type t = entry.SettingType;
			if (t == null || !t.IsEnum)
			{
				return false;
			}
			string[] names = Enum.GetNames(t);
			if (names == null || names.Length == 0)
			{
				return false;
			}
			if (Templates.dropdown == null)
			{
				AddFallbackTextRow(container, label + ": " + FormatEntryValue(entry));
				return true;
			}
			GameObject go = Templates.Instantiate(Templates.dropdown);
			if (go == null)
			{
				return false;
			}
			SetupControl(go, container, label, null);
			UnityEngine.UI.Dropdown dd = go.transform.GetChild(1).GetComponent<UnityEngine.UI.Dropdown>();
			if (dd != null)
			{
				Il2CppSystem.Collections.Generic.List<Dropdown.OptionData> opts = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
				int idx = 0;
				string cur = "";
				try
				{
					object bv = GetStaged(cfg, entry, entry.BoxedValue);
					cur = bv != null ? bv.ToString() : "";
				}
				catch
				{
				}
				for (int i = 0; i < names.Length; i++)
				{
					opts.Add(new Dropdown.OptionData(HumanizeKey(names[i])));
					if (names[i] == cur)
					{
						idx = i;
					}
				}
				dd.ClearOptions();
				dd.AddOptions(opts);
				dd.value = Mathf.Clamp(idx, 0, names.Length - 1);
				dd.onValueChanged.RemoveAllListeners();
				// 注册轮询监视（不绑定 onValueChanged —— 委托桥接不可靠）
				watches.Add(new ControlWatch { dropdown = dd, cfg = cfg, entry = entry, lastInt = Mathf.Clamp(idx, 0, names.Length - 1), enumType = t, enumNames = names });
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>选项列表是否像"按键枚举"（含 None 或 F1/Mouse0/Alpha1 之类）。</summary>
	private static bool LooksLikeKeyList(ConfigEntryBase e)
	{
		try
		{
			if (e == null || e.Description == null || !(e.Description.AcceptableValues is AcceptableValueList<string> lst) || lst.AcceptableValues == null)
			{
				return false;
			}
			foreach (string v in lst.AcceptableValues)
			{
				if (v == null)
				{
					continue;
				}
				if (v == "None" || System.Text.RegularExpressions.Regex.IsMatch(v, "^(F|Alpha|Mouse)\\d+$"))
				{
					return true;
				}
			}
		}
		catch
		{
		}
		return false;
	}

	/// <summary>热键配置 → 点击改键按钮（捕获模式：按下新键即暂存；Esc 取消）。
	/// isKeyCode=true 写 KeyCode 枚举；false + enumType=null 写字符串；enumType 非空写该枚举（如 InputSystem Key）。</summary>
	private static bool AddHotkeyRebind(ConfigFile cfg, ConfigEntryBase entry, Transform container, string label, bool isKeyCode, Type enumType = null)
	{
		try
		{
			GameObject row = new GameObject("MM_KeyRebind");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 42f);
			// 标签（左）
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.sizeDelta = new Vector2(-214f, 38f);
			trt.anchoredPosition = Vector2.zero;
			Text ltxt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				ltxt.font = font;
			}
			ltxt.fontSize = 18;
			ltxt.color = Color.white;
			ltxt.alignment = TextAnchor.MiddleLeft;
			ltxt.horizontalOverflow = HorizontalWrapMode.Overflow;
			ltxt.text = label;
			// 改键按钮（右）
			string cur = "None";
			try
			{
				object bv = GetStaged(cfg, entry, entry.BoxedValue);
				cur = bv != null ? bv.ToString() : "None";
			}
			catch
			{
			}
			Text btnTxt = null;
			btnTxt = MakeFooterButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(140f, 30f), HumanizeKey(cur), delegate
			{
				StartKeyCapture(cfg, entry, btnTxt, isKeyCode, enumType);
			});
			// None（禁用）小按钮：string/KeyCode 恒有；枚举只有含 None 值时显示
			object noneVal = null;
			if (enumType != null)
			{
				try
				{
					noneVal = Enum.Parse(enumType, "None");
				}
				catch
				{
					noneVal = null;
				}
			}
			else
			{
				noneVal = isKeyCode ? (object)KeyCode.None : (object)"None";
			}
			if (noneVal != null)
			{
				MakeFooterButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-160f, 0f), new Vector2(50f, 30f), "None", delegate
				{
					StageValue(cfg, entry, noneVal);
					SettingsGUI_V2 s = SettingsGUI_V2.instance;
					if (s != null)
					{
						OpenMyPage(s);
					}
				});
			}
			return true;
		}
		catch
		{
			return false;
		}
	}

	/// <summary>按键捕获状态（同一时间最多一个捕获）。</summary>
	private static ConfigFile captureCfg;

	private static ConfigEntryBase captureEntry;

	private static Text captureText;

	private static string captureOriginal;

	private static float captureUntil;

	private static bool captureIsKeyCode;

	private static Type captureEnumType;

	private static string[] captureOptions = BuildKeyOptions();

	/// <summary>进入捕获模式：按钮显示"请按新键"，2 秒内按下任意候选键即暂存。</summary>
	internal static void StartKeyCapture(ConfigFile cfg, ConfigEntryBase entry, Text btnTxt, bool isKeyCode, Type enumType = null)
	{
		try
		{
			captureCfg = cfg;
			captureEntry = entry;
			captureText = btnTxt;
			captureIsKeyCode = isKeyCode;
			captureEnumType = enumType;
			captureOriginal = (btnTxt != null && !btnTxt.Equals(null)) ? btnTxt.text : "";
			captureUntil = Time.unscaledTime + 2f;
			if (btnTxt != null && !btnTxt.Equals(null))
			{
				btnTxt.text = Plugin.DefaultChinese ? "请按新键..." : "Press key...";
			}
		}
		catch
		{
		}
	}

	private static void EndKeyCapture(bool applied, string newKey)
	{
		captureCfg = null;
		captureEntry = null;
		if (captureText != null && !captureText.Equals(null))
		{
			captureText.text = applied ? newKey : captureOriginal;
		}
		captureText = null;
	}

	/// <summary>每帧轮询按键捕获（在 PollControls 里调用）。</summary>
	private static void PollKeyCapture()
	{
		try
		{
			if (captureEntry == null)
			{
				return;
			}
			if (Time.unscaledTime > captureUntil)
			{
				EndKeyCapture(false, null);
				return;
			}
			if (Input.GetKeyDown(KeyCode.Escape))
			{
				EndKeyCapture(false, null);
				return;
			}
			string[] opts = captureOptions;
			if (opts == null)
			{
				EndKeyCapture(false, null);
				return;
			}
			foreach (string k in opts)
			{
				if (string.IsNullOrEmpty(k) || k == "None")
				{
					continue;
				}
				try
				{
					if (Input.GetKeyDown((KeyCode)Enum.Parse(typeof(KeyCode), k)))
					{
						if (captureEnumType != null)
						{
							// 枚举热键（如 InputSystem Key）：KeyCode 名 → 枚举名映射 → 写入枚举值
							string mapped = MapKeyCodeToEnumName(k);
							if (mapped == null)
							{
								continue; // 该键无对应枚举名（如鼠标键），继续轮询
							}
							try
							{
								object ev = Enum.Parse(captureEnumType, mapped);
								StageValue(captureCfg, captureEntry, ev);
								EndKeyCapture(true, HumanizeKey(mapped));
								return;
							}
							catch
							{
								continue;
							}
						}
						StageValue(captureCfg, captureEntry, captureIsKeyCode ? (object)(KeyCode)Enum.Parse(typeof(KeyCode), k) : (object)k);
						EndKeyCapture(true, k);
						return;
					}
				}
				catch
				{
				}
			}
		}
		catch
		{
		}
	}

	private static object ParseKeyCode(string s)
	{
		try
		{
			return Enum.Parse(typeof(KeyCode), s);
		}
		catch
		{
			return KeyCode.None;
		}
	}

	/// <summary>KeyCode 枚举名 → 目标枚举名（InputSystem Key 等）。名字相同直接返回；已知差异做映射；
	/// 无法映射（如鼠标键 Mouse0-6）返回 null。</summary>
	private static string MapKeyCodeToEnumName(string keyCodeName)
	{
		try
		{
			if (string.IsNullOrEmpty(keyCodeName))
			{
				return null;
			}
			if (keyCodeName == "Return")
			{
				return "Enter";
			}
			if (keyCodeName == "LeftControl")
			{
				return "LeftCtrl";
			}
			if (keyCodeName == "RightControl")
			{
				return "RightCtrl";
			}
			if (keyCodeName == "Menu")
			{
				return "ContextMenu";
			}
			if (keyCodeName.Length == 6 && keyCodeName.StartsWith("Alpha", StringComparison.Ordinal))
			{
				// Alpha0-9 → Digit0-9
				return "Digit" + keyCodeName.Substring(5);
			}
			// 鼠标键等其他 InputSystem.Key 不存在的键
			if (keyCodeName.StartsWith("Mouse", StringComparison.Ordinal))
			{
				return null;
			}
			return keyCodeName;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>精选可捕获的按键列表（真实存在的键：None + F1-F12 + 字母 + 数字 + 方向键 + 常用键 + 鼠标）。</summary>
	private static string[] BuildKeyOptions()
	{
		try
		{
			HashSet<string> keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
			{
				"None", "Space", "Return", "Escape", "Tab", "Backspace", "Delete", "Insert",
				"Home", "End", "PageUp", "PageDown", "UpArrow", "DownArrow", "LeftArrow", "RightArrow",
				"LeftShift", "RightShift", "LeftControl", "RightControl", "LeftAlt", "RightAlt",
				"LeftWindows", "RightWindows", "Menu",
				"Mouse0", "Mouse1", "Mouse2", "Mouse3", "Mouse4", "Mouse5", "Mouse6"
			};
			for (int i = 1; i <= 12; i++)
			{
				keep.Add("F" + i);
			}
			for (int i = 0; i <= 9; i++)
			{
				keep.Add("Alpha" + i);
			}
			for (char c = 'A'; c <= 'Z'; c++)
			{
				keep.Add(c.ToString());
			}
			string[] names = Enum.GetNames(typeof(KeyCode));
			List<string> list = new List<string>();
			foreach (string n in names)
			{
				if (keep.Contains(n))
				{
					list.Add(n);
				}
			}
			return list.ToArray();
		}
		catch
		{
			return null;
		}
	}

	/// <summary>小灰字次级按钮（子项操作用；与页脚深色按钮区分）。Text 自身作 targetGraphic 即可点击。</summary>
	private static Text MakeSmallTextButton(GameObject row, Vector2 anchorMin, Vector2 anchorMax, Vector2 anchoredPos, Vector2 size, string label, System.Action onClick)
	{
		Text result = null;
		try
		{
			GameObject btnGo = new GameObject("SmallBtn");
			btnGo.transform.SetParent(row.transform, false);
			RectTransform brt = btnGo.AddComponent<RectTransform>();
			brt.anchorMin = anchorMin;
			brt.anchorMax = anchorMax;
			brt.pivot = new Vector2(1f, 0.5f);
			brt.anchoredPosition = anchoredPos;
			brt.sizeDelta = size;
			Text bt = btnGo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				bt.font = font;
			}
			bt.fontSize = 13;
			bt.color = new Color(0.62f, 0.64f, 0.7f, 0.9f);
			bt.alignment = TextAnchor.MiddleRight;
			bt.text = label;
			UnityEngine.UI.Button bbtn = btnGo.AddComponent<UnityEngine.UI.Button>();
			bbtn.transition = UnityEngine.UI.Selectable.Transition.None;
			bbtn.targetGraphic = bt;
			bbtn.onClick.RemoveAllListeners();
			bbtn.onClick.AddListener(onClick);
			result = bt;
		}
		catch
		{
		}
		return result;
	}

	/// <summary>每个子选项下方的操作行：独立复制（仅名称+简介）与重置按钮（小灰字链接样式，与页脚深色按钮区分）。</summary>
	private static void AddEntryActions(Transform container, ConfigFile cfg, ConfigEntryBase entry, string label)
	{
		try
		{
			GameObject row = new GameObject("MM_EntryActions");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 24f);
			// 布局与分区底部一致：重置在右、复制在左；小灰字 = 次级操作
			Text resetTxt = null;
			resetTxt = MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-12f, 0f), new Vector2(64f, 22f), Plugin.DefaultChinese ? "重置" : "Reset", delegate
			{
				try
				{
					StageValue(cfg, entry, entry.DefaultValue);
					FlashText(resetTxt, Plugin.DefaultChinese ? "已重置 ✓" : "Reset ✓", 1.5f);
					SettingsGUI_V2 s = SettingsGUI_V2.instance;
					if (s != null)
					{
						OpenMyPage(s);
					}
				}
				catch (Exception ex)
				{
					Plugin.ModLog.LogError((object)("ModManager entry reset error: " + ex.Message));
				}
			});
			Text copyTxt = null;
			copyTxt = MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-84f, 0f), new Vector2(64f, 22f), Plugin.DefaultChinese ? "复制" : "Copy", delegate
			{
				try
				{
					GUIUtility.systemCopyBuffer = EntryCopyText(entry);
					FlashText(copyTxt, Plugin.DefaultChinese ? "已复制 ✓" : "Copied ✓", 1.5f);
				}
				catch (Exception ex)
				{
					Plugin.ModLog.LogError((object)("ModManager entry copy error: " + ex.Message));
				}
			});
		}
		catch
		{
		}
	}

	/// <summary>单个配置项的复制文本：人性化名称 + mod 自带简介原文（无简介时只输出名称）。</summary>
	private static string EntryCopyText(ConfigEntryBase entry)
	{
		string key = HumanizeKey(entry.Definition.Key);
		string desc = null;
		try
		{
			desc = (entry.Description != null) ? entry.Description.Description : null;
		}
		catch
		{
		}
		return string.IsNullOrEmpty(desc) ? key : key + ": " + desc;
	}

	/// <summary>原生控件模板（Resources.Load 优先，失败则缓存原生页克隆体的独立副本）。</summary>
	private static class Templates
	{
		internal static GameObject toggle;

		internal static GameObject slider;

		internal static GameObject dropdown;

		internal static GameObject button;

		internal static GameObject panel;

		internal static bool scanned;

		internal static Transform holder;

		internal static void Ensure(Transform contentPage)
		{
			if (scanned)
			{
				return;
			}
			scanned = true;
			try
			{
				// 只在字段为空时尝试 Resources.Load —— 不能覆盖 Update 轮询已缓存的模板
				if (toggle == null)
				{
					toggle = Resources.Load<GameObject>("Settings_Toggle");
				}
				if (slider == null)
				{
					slider = Resources.Load<GameObject>("Settings_Slider");
				}
				if (dropdown == null)
				{
					dropdown = Resources.Load<GameObject>("Settings_Dropdown");
				}
				if (button == null)
				{
					button = Resources.Load<GameObject>("Settings_Button");
				}
				if (panel == null)
				{
					panel = Resources.Load<GameObject>("Settings_Panel");
				}
			}
			catch
			{
			}
			CacheFromNative(contentPage);
		}

		/// <summary>从原生页克隆体缓存模板副本（Instantiate 出的副本独立于源，源被销毁后仍可用）。</summary>
		internal static void CacheFromNative(Transform contentPage)
		{
			if (contentPage == null)
			{
				return;
			}
			try
			{
				if (holder == null)
				{
					GameObject h = new GameObject("__ModManagerTemplates");
					UnityEngine.Object.DontDestroyOnLoad(h);
					h.SetActive(false);
					holder = h.transform;
				}
				for (int i = 0; i < contentPage.childCount; i++)
				{
					Transform c = contentPage.GetChild(i);
					if (c == null)
					{
						continue;
					}
					string nm = c.name;
					if (toggle == null && nm.IndexOf("Toggle", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						GameObject tpl = UnityEngine.Object.Instantiate(c.gameObject);
						if (tpl != null)
						{
							toggle = tpl;
							toggle.transform.SetParent(holder, false);
							RememberTemplateHeight(tpl, c);
						}
					}
					else if (slider == null && nm.IndexOf("Slider", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						GameObject tpl = UnityEngine.Object.Instantiate(c.gameObject);
						if (tpl != null)
						{
							slider = tpl;
							slider.transform.SetParent(holder, false);
							RememberTemplateHeight(tpl, c);
						}
					}
					else if (dropdown == null && nm.IndexOf("Dropdown", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						GameObject tpl = UnityEngine.Object.Instantiate(c.gameObject);
						if (tpl != null)
						{
							dropdown = tpl;
							dropdown.transform.SetParent(holder, false);
							RememberTemplateHeight(tpl, c);
						}
					}
					else if (panel == null && (nm.IndexOf("Panel", StringComparison.OrdinalIgnoreCase) >= 0 || nm.IndexOf("Title", StringComparison.OrdinalIgnoreCase) >= 0))
					{
						GameObject tpl = UnityEngine.Object.Instantiate(c.gameObject);
						if (tpl != null)
						{
							panel = tpl;
							panel.transform.SetParent(holder, false);
							RememberTemplateHeight(tpl, c);
						}
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager template cache error: " + ex.Message));
			}
		}

		internal static GameObject Instantiate(GameObject tpl)
		{
			try
			{
				if (tpl == null)
				{
					return null;
				}
				GameObject go = UnityEngine.Object.Instantiate(tpl);
				NormalizeRowHeight(tpl, go);
				return go;
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager instantiate error: " + ex.Message));
				return null;
			}
		}

		/// <summary>v1.2.1：模板原生行的实测高度（缓存模板时从原生页那一行读）。
		/// 带拉伸锚点的克隆自己推算不出高度（sizeDelta.y 可能就是 0）。</summary>
		private static readonly Dictionary<int, float> templateHeights = new Dictionary<int, float>();

		private static void RememberTemplateHeight(GameObject tpl, Transform nativeRow)
		{
			try
			{
				if (tpl == null)
				{
					return;
				}
				float h = 0f;
				if (nativeRow != null)
				{
					RectTransform nrt = nativeRow.GetComponent<RectTransform>();
					if (nrt != null)
					{
						h = nrt.rect.height;
					}
				}
				if (h <= 1f)
				{
					RectTransform trt = tpl.GetComponent<RectTransform>();
					if (trt != null && trt.sizeDelta.y > 1f)
					{
						h = trt.sizeDelta.y;
					}
				}
				if (h > 1f)
				{
					templateHeights[tpl.GetInstanceID()] = h;
				}
			}
			catch
			{
			}
		}

		/// <summary>v1.2.1：**行高归一化**（重进模组页后"滑条与选项重叠"的根因修复）。
		/// 行容器用的是 VerticalLayoutGroup(childControlHeight=false)——这种情况下 uGUI 取的行高
		/// 是 `sizeDelta[1]`（不是 rect.height）：克隆来的原生行若带垂直拉伸锚点、sizeDelta.y=0，
		/// 行高就算成 0 → 所有行被摆到同一个 y → 视觉上整页控件叠在一起。这里对有病的行
		/// 统一改成非拉伸顶部锚点 + 显式高度（优先用模板实测高度，其次 sizeDelta，最后 48 兜底）。</summary>
		private static void NormalizeRowHeight(GameObject tpl, GameObject go)
		{
			try
			{
				if (go == null)
				{
					return;
				}
				RectTransform rt = go.GetComponent<RectTransform>();
				if (rt == null)
				{
					return;
				}
				bool stretched = Math.Abs(rt.anchorMax.y - rt.anchorMin.y) > 0.01f;
				bool zeroHeight = rt.sizeDelta.y <= 1f;
				if (!stretched && !zeroHeight)
				{
					return;
				}
				float h = 0f;
				try
				{
					if (tpl != null)
					{
						templateHeights.TryGetValue(tpl.GetInstanceID(), out h);
					}
				}
				catch
				{
				}
				if (h <= 1f && rt.sizeDelta.y > 1f)
				{
					h = rt.sizeDelta.y;
				}
				if (h <= 1f)
				{
					h = 48f;
				}
				if (stretched)
				{
					rt.anchorMin = new Vector2(rt.anchorMin.x, 1f);
					rt.anchorMax = new Vector2(rt.anchorMax.x, 1f);
					rt.pivot = new Vector2(rt.pivot.x, 1f);
				}
				rt.sizeDelta = new Vector2(rt.sizeDelta.x, h);
				Plugin.ModLog.LogInfo((object)("ModManager: normalized template row '" + go.name + "' -> h=" + h.ToString("F1")
					+ (stretched ? " (was vertically stretched)" : " (sizeDelta.y was ~0)")));
			}
			catch
			{
			}
		}
	}

	internal sealed class PluginConfig
	{
		internal string name;

		/// <summary>v1.1.4：剥离 "Easy Red 2 "/"ER2 " 前缀后的短名（排序/分组/标题显示用）。</summary>
		internal string shortName;

		internal ConfigFile cfg;
	}

	/// <summary>v1.1.4：剥离 mod 名的 "Easy Red 2" / "ER2" 前缀（含后续分隔符），
	/// 便于按真实功能名排序分组；无前缀时原样返回。</summary>
	internal static string StripNamePrefix(string name)
	{
		try
		{
			if (string.IsNullOrEmpty(name))
			{
				return name ?? "";
			}
			string[] prefixes = { "Easy Red 2", "ER2" };
			foreach (string p in prefixes)
			{
				if (name.Length > p.Length &&
					name.StartsWith(p, StringComparison.OrdinalIgnoreCase) &&
					(name[p.Length] == ' ' || name[p.Length] == '-' || name[p.Length] == '_' || name[p.Length] == ':'))
				{
					string rest = name.Substring(p.Length + 1).TrimStart();
					if (rest.Length > 0)
					{
						return rest;
					}
				}
			}
			return name;
		}
		catch
		{
			return name ?? "";
		}
	}

	/// <summary>v1.1.4：短名 → 分组标题（"A"…"Z" / "0-9" / "#"）。</summary>
	internal static string GroupLetter(string shortName)
	{
		try
		{
			if (string.IsNullOrEmpty(shortName))
			{
				return "#";
			}
			char c = char.ToUpperInvariant(shortName[0]);
			if (c >= '0' && c <= '9')
			{
				return "0-9";
			}
			if (c >= 'A' && c <= 'Z')
			{
				return c.ToString();
			}
			return "#";
		}
		catch
		{
			return "#";
		}
	}

	/// <summary>是否带总开关：配置里存在名为 Enabled/enabled 的 bool 配置项。</summary>
	internal static bool HasMasterSwitch(PluginConfig p)
	{
		if (p == null || p.cfg == null)
		{
			return false;
		}
		foreach (ConfigEntryBase e in p.cfg.Values)
		{
			if (e != null && string.Equals(e.Definition.Key, "Enabled", StringComparison.OrdinalIgnoreCase) && e.SettingType == typeof(bool))
			{
				return true;
			}
		}
		return false;
	}

	/// <summary>枚举所有已加载插件及其配置（跳过无配置项的插件）。</summary>
	private static List<PluginConfig> pluginsCache;

	/// <summary>v1.1.6：插件列表缓存。快速翻页/重开设置期间 IL2CPPChainloader.Plugins 字典可能
	/// 瞬态返回空 → FillContent 空列表 → 轮询每帧判 !hasOurs 反复重建 = 卡死空白页（日志实测
	/// container=100 连刷几十帧）。插件集合运行期不变，成功收集一次后永久缓存。</summary>
	internal static List<PluginConfig> CollectPlugins()
	{
		if (pluginsCache != null)
		{
			return pluginsCache;
		}
		List<PluginConfig> list = new List<PluginConfig>();
		try
		{
			IL2CPPChainloader loader = IL2CPPChainloader.Instance;
			if (loader == null)
			{
				Plugin.ModLog.LogInfo((object)"ModManager: IL2CPPChainloader.Instance is null.");
				return list;
			}
			Dictionary<string, PluginInfo> infos = loader.Plugins;
			if (infos == null)
			{
				Plugin.ModLog.LogInfo((object)"ModManager: PluginInfos is null.");
				return list;
			}
			foreach (KeyValuePair<string, PluginInfo> kv in infos)
			{
				try
				{
					PluginInfo pi = kv.Value;
					if (pi == null)
					{
						continue;
					}
					BasePlugin inst = pi.Instance as BasePlugin;
					if (inst == null)
					{
						continue;
					}
					ConfigFile cfg = inst.Config;
					string name = (pi.Metadata != null) ? pi.Metadata.Name : kv.Key;
					if (cfg == null || cfg.Count == 0)
					{
						// 无配置项的 mod 显示为只读"已安装"条目（cfg=null，展开显示"没有可调设置"）。
						list.Add(new PluginConfig { name = name, cfg = null });
						continue;
					}
					// 显式保存语义：关闭 BepInEx 自动落盘（默认 true，会绕过我们的【保存】按钮）
					try
					{
						cfg.SaveOnConfigSet = false;
					}
					catch
					{
					}
					list.Add(new PluginConfig { name = name, cfg = cfg });
				}
					catch (Exception ex)
					{
						Plugin.ModLog.LogError((object)("ModManager collect item error: " + ex.Message));
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogError((object)("ModManager collect error: " + ex.Message));
			}
			// 只缓存非空结果：空 = 瞬态故障（链加载器字典暂不可读），下次重试
			if (list.Count > 0)
			{
				pluginsCache = list;
			}
			return list;
		}

	private static string FormatEntryValue(ConfigEntryBase entry)
	{
		try
		{
			object v = entry.BoxedValue;
			return (v != null) ? v.ToString() : "";
		}
		catch
		{
			return "?";
		}
	}

	/// <summary>无模板时的文本行兜底（自建 uGUI Text，原生字体，宽度交给 LayoutGroup）。</summary>
	private static float AddFallbackTextRow(Transform container, string text)
	{
		try
		{
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			GameObject row = new GameObject("MM_TextRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 34f);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.anchoredPosition = Vector2.zero;
			trt.sizeDelta = new Vector2(-48f, 30f);
			Text txt = tgo.AddComponent<Text>();
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 20;
			txt.color = new Color(0.85f, 0.85f, 0.85f, 1f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.resizeTextForBestFit = true;
			txt.resizeTextMinSize = 10;
			txt.resizeTextMaxSize = 20;
			txt.text = text;
			return 34f;
		}
		catch
		{
			return 0f;
		}
	}

}

/// <summary>轮询：注入 MODS 页 + 轮询活跃控件值变化 + 抓取原生控件模板。</summary>
[HarmonyPatch(typeof(SettingsGUI_V2), "Update")]
public class InjectPollPatch
{
	private static void Postfix()
	{
		try
		{
			if (Plugin.enabled != null && !Plugin.enabled.Value)
			{
				return;
			}
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s == null)
			{
				return;
			}
			ModRegistry.EnsureInjected(s);
			// 原生可能在最后一页隐藏 R 翻页按钮（它不知道 MODS 页）→ 强制左右按钮常显。
			// v1.1.3：l_button 也常显（首页左翻绕回 MODS 页，见 TabLeftPatch）。
			try
			{
				if (s.r_button != null)
				{
					if (!s.r_button.gameObject.activeSelf)
					{
						s.r_button.gameObject.SetActive(true);
					}
					if (!s.r_button.interactable)
					{
						s.r_button.interactable = true;
					}
				}
				if (s.l_button != null)
				{
					if (!s.l_button.gameObject.activeSelf)
					{
						s.l_button.gameObject.SetActive(true);
					}
					if (!s.l_button.interactable)
					{
						s.l_button.interactable = true;
					}
				}
			}
			catch
			{
			}
			// 会话检测：设置界面关闭期间 Update 不跑，unscaledTime 跳变 = 刚重开。
			// 重开后强制重建一次（问题：重进后 slider 模板布局残留/滚动状态错乱导致滑条与标题行重叠、列表不显示）。
			if (SettingsGUI_V2.currentOpenedMenu == ModRegistry.myIndex && ModRegistry.myIndex > 0)
			{
				if (Time.unscaledTime - ModRegistry.lastPollTime > 1.5f)
				{
					ModRegistry.lastPollTime = Time.unscaledTime;
					// v1.2.1：重开设置界面前先把上次接管时改过的 content 锚点/高度还原再重建。
					// 用户"直接关掉设置界面"时 Update 停跑 → RestoreScrollAnchors 没有机会执行，
					// content 会带着我们的顶部锚点+旧高度进入原生重开流程（坏高度泄漏的老根因）。
					ModRegistry.RestoreScrollAnchors();
					ModRegistry.OpenMyPage(s);
					ModRegistry.ResetScrollTop(s.contentPage);
					// v1.2.1 诊断（发布前删）：重开设置界面后的现场
					ModRegistry.LogLayoutSnapshot("reopen", s.contentPage);
					ModRegistry.LogScrollChain("reopen", s.contentPage);
					return;
				}
				ModRegistry.lastPollTime = Time.unscaledTime;
			}
			else
			{
				// v1.2.1 诊断（发布前删）：重开设置界面后页码没落在 MODS 页时也 dump 一次，
				// 用来看是不是上一轮残留容器/坏高度与原生页争用
				if (Time.unscaledTime - ModRegistry.lastPollTime > 1.5f)
				{
					ModRegistry.LogLayoutSnapshot("reopen-native", s.contentPage);
					ModRegistry.LogScrollChain("reopen-native", s.contentPage);
				}
				ModRegistry.lastPollTime = Time.unscaledTime;
			}
			// 轮询活跃控件（开关/滑条/下拉）的值变化（绕开 UnityAction 委托桥接的 marshaling bug）
			ModRegistry.PollControls();
			// 模板缓存：原生页有内容时抓取控件模板（Resources.Load 失败时的兜底）
			Transform cp = s.contentPage;
			if (cp != null && cp.childCount > 0 && SettingsGUI_V2.currentOpenedMenu != ModRegistry.myIndex)
			{
				ModRegistry.CacheTemplatesFromNative(cp);
			}
			// 重开设置界面时原生可能重置页码/重新填充我们的页 → 检测并重新接管
			if (SettingsGUI_V2.currentOpenedMenu == ModRegistry.myIndex)
			{
				// v1.1.9：MODS 页激活期间 Content 链必须可见（快速右翻连点会把链停用 →
				// 我们的容器 inactive 不渲染 = 空列表；STUCK-EVIDENCE 实锤根因）
				ModRegistry.EnsureContentVisible(s);
				// 原生填充是异步协程：重开设置界面时协程可能晚到，把原生控件填进我们的页 → 每帧清理非我们容器的子物体
				bool hasOurs = false;
				bool nativeMode = (Plugin.nativePoc != null && Plugin.nativePoc.Value) ||
								  (Plugin.nativeFull != null && Plugin.nativeFull.Value);
				if (nativeMode)
				{
					// 原生行模式：整页由我们同步填充，不清除子物体（行就是我们的）；
					// 但剪除原生填充协程可能追加的冗余行（超出我们渲染数量的子物体）
					hasOurs = true;
					NativePage.PruneExtras(s.contentPage);
				}
				else
				{
					try
					{
						Transform c2 = s.contentPage;
						if (c2 != null)
						{
								for (int i = c2.childCount - 1; i >= 0; i--)
								{
									Transform child = c2.GetChild(i);
									if (child != null)
									{
										if (child.name == "MM_Container")
										{
											hasOurs = true;
										}
										else
										{
											UnityEngine.Object.Destroy(child.gameObject);
										}
									}
								}
							}
						}
						catch
						{
						}
					}
					// v1.1.7：hasOurs 加严——容器存在但零子行 = 坏状态（布局从未展开、看不见任何行），
					// 与"容器不在"同视，都需要重建。配熔断器防重建风暴。
					// v1.1.8：布局死状态——容器有行但 rect 高度仍是初始 100（Content 的高也是 200 兜底
					// 值）= LayoutGroup 从未算过/被清，行全部叠在一点 → 视觉上"没有列表"。同样触发重建。
					if (hasOurs)
					{
						Transform ours = null;
						try
						{
							Transform c3 = s.contentPage;
							for (int i = c3.childCount - 1; i >= 0; i--)
							{
								Transform ch = c3.GetChild(i);
								if (ch != null && ch.name == "MM_Container")
								{
									ours = ch;
									break;
								}
							}
						}
						catch
						{
						}
						if (ours != null)
						{
							bool dead = ours.childCount == 0;
							if (!dead)
							{
								try
								{
									RectTransform ort = ours.GetComponent<RectTransform>();
									RectTransform prt = s.contentPage.GetComponent<RectTransform>();
									// 行数>0 但容器高未展开（≈初始值）且 content 高也没跟上 = 布局死
									if (ort != null && prt != null &&
										ort.rect.height <= 101f && prt.rect.height <= 201f)
									{
										dead = true;
									}
								}
								catch
								{
								}
							}
							if (dead)
							{
								hasOurs = false;
							}
						}
					}
					if (!hasOurs)
					{
						float now = Time.unscaledTime;
						// 熔断：1 秒内最多 2 次重建；超限 = 有东西在持续对抗我们（不猜原因），dump 证据 + 停手
						if (now - ModRegistry.lastRebuildTime < 0.5f)
						{
							ModRegistry.rebuildBurst++;
							if (ModRegistry.rebuildBurst == 4)
							{
								ModRegistry.DumpStuckEvidence(s);
							}
							if (ModRegistry.rebuildBurst >= 4)
							{
								// 停止重建，仅做布局强推（若只是布局未算，这一下就能救活）
								ModRegistry.ForceLayoutOnly(s);
							}
						}
						else
						{
							ModRegistry.rebuildBurst = 1;
							ModRegistry.lastRebuildTime = now;
							ModRegistry.OpenMyPage(s);
						}
					}
					// 滚动高度自愈：周期性重测容器实际高度（防原生协程/关闭转场写入错误滚动范围）
					ModRegistry.SelfHealScroll(s.contentPage);
			}
			else
			{
				// 不在 MODS 页：只还原我们改过的 content 锚点。
				// 游戏更新后的 SettingsGUI_V2 会在转场/动画期间暂时改写 currentOpenedMenu；
				// 旧逻辑看到 lastOnMods 就每帧 OpenMyPage，导致按钮刚创建便被 Destroy，
				// 日志表现为 MODS page 反复打开、页面控件全部闪失。重新进入 MODS 页
				// 由 TabRight/TabLeft 的明确翻页入口处理，这里绝不抢回页面。
				ModRegistry.RestoreScrollAnchors();
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager inject poll error: " + ex.Message));
		}
	}
}

/// <summary>设置界面帧看门狗：暂存改动 0.8s 后自动落盘（等效"退出即保存"，无需关闭钩子）。</summary>
[HarmonyPatch(typeof(SettingsGUI_V2), "Update")]
public class WatchdogPatch
{
	private static void Postfix()
	{
		try
		{
			ModRegistry.WatchdogFlush();
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager watchdog error: " + ex.Message));
		}
	}
}

/// <summary>翻到最后一页（我们的 MODS 页）时接管填充；v1.1.4 在 MODS 页再右翻绕回第一页
///（与左翻绕回 MODS 页呼应，两个方向都能循环）。</summary>
[HarmonyPatch(typeof(SettingsGUI_V2), "SettingsTabRight")]
public class TabRightPatch
{	private static bool Prefix()
	{
		try
		{
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s == null || ModRegistry.myIndex <= 0)
			{
				return true;
			}
			int cur = SettingsGUI_V2.currentOpenedMenu;
			if (cur == ModRegistry.myIndex)
			{
				// 已是 MODS 页（末页）：右翻绕回第一页（走原生 UpdateOpenedMenu 恢复原生气/行为）
				ModRegistry.WrapToFirstPage(s);
				return false;
			}
			if (cur + 1 == ModRegistry.myIndex)
			{
				ModRegistry.EnterMyPage(s);
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager tab right error: " + ex.Message));
		}
		return true;
	}
}

/// <summary>左翻：从越界页（myIndex+1）回 MODS 页重新接管；v1.1.3 在第一页左翻绕回 MODS 页
///（原生翻页循环走左边按钮，MODS 页在末尾，绕回 = 最快进入路径）。左翻离开 MODS 页正常放行。</summary>
[HarmonyPatch(typeof(SettingsGUI_V2), "SettingsTabLeft")]
public class TabLeftPatch
{
	private static bool Prefix()
	{
		try
		{
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s == null || ModRegistry.myIndex <= 0)
			{
				return true;
			}
			int cur = SettingsGUI_V2.currentOpenedMenu;
			if (cur == ModRegistry.myIndex + 1)
			{
				ModRegistry.EnterMyPage(s);
				return false;
			}
			if (cur == 0)
			{
				// 第一页左翻 → 绕回最后一页（MODS）
				ModRegistry.EnterMyPage(s);
				return false;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager tab left error: " + ex.Message));
		}
		return true;
	}
}

/// <summary>原生设置页的异步内容操作门户（游戏 2.1.x 新增的清理/重填路径）：
/// MODS 页激活期间，原生 ClearContentPage（异步清空）与 UpdateOpenedMenu（重开填充协程）
/// 会清掉我们的控件 → 与我们的轮询重建形成拉锯循环。统一拦截这两条路径。</summary>
[HarmonyPatch]
public static class NativeContentGatePatch
{
	private static System.Collections.Generic.IEnumerable<MethodBase> TargetMethods()
	{
		// 注意：TargetMethods 里不能 yield 任何 null（Harmony 会判定目标非法并中断整个 PatchAll），
		// 所以先收集再过滤——游戏版本变化导致某个方法消失时，剩下的照常拦截。
		MethodBase[] candidates = new MethodBase[]
		{
			AccessTools.Method(typeof(SettingsGUI_V2), nameof(SettingsGUI_V2.ClearContentPage)),
			AccessTools.Method(typeof(SettingsGUI_V2), nameof(SettingsGUI_V2.UpdateOpenedMenu)),
			// v1.2.1：游戏 2026-09-12 更新后，原生重开设置界面还会直接起 FillSettingPage 协程
			//（新签名带 restorePosition/selectedY/scrollbarValue，用来恢复上次滚动位置），
			// 这条路不经过 UpdateOpenedMenu → 必须一并拦，否则原生填充协程会往我们的页里灌行/改布局。
			AccessTools.Method(typeof(SettingsGUI_V2), "FillSettingPage")
		};
		System.Collections.Generic.List<MethodBase> targets = new System.Collections.Generic.List<MethodBase>();
		foreach (MethodBase mb in candidates)
		{
			if (mb != null)
			{
				targets.Add(mb);
			}
		}
		return targets;
	}

	// 无参 Prefix 对两个目标都兼容；返回 false = 跳过原方法
	private static bool Prefix()
	{
		return !(ModRegistry.myIndex > 0 && SettingsGUI_V2.currentOpenedMenu == ModRegistry.myIndex);
	}
}
