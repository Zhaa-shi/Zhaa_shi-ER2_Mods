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

[BepInPlugin("er2.modmanager", "ER2 Mod Manager", "1.5.0")]
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
		ModLog.LogInfo((object)"ER2 Mod Manager 1.5.0 loaded.");
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
			// v1.3.1：先脱离父级再 Destroy。Destroy 是帧末才生效，旧行会在本帧继续与新行**叠着画**
			// （重建时整页闪一下的根因）；SetParent(null) 让它立刻退出布局与渲染。
			for (int i = contentPage.childCount - 1; i >= 0; i--)
			{
				Transform child = contentPage.GetChild(i);
				if (child != null && child.gameObject != null)
				{
					try
					{
						child.SetParent(null, false);
					}
					catch
					{
					}
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

	/// <summary>展开的配置分区（v1.5.0 定为**默认收起**：多分区 mod 展开后先看到分区标题列表，
	/// 逐个点开——玩家明确要求；标题带 ▸/▾ 箭头显示状态，其下内容再缩进一级以防混淆）。</summary>
	internal static readonly HashSet<string> expandedSections = new HashSet<string>();

	/// <summary>滚动高度自愈节流（原生协程/关闭转场可能写入错误高度 → 周期性重测容器实际高度）。</summary>
	internal static float nextScrollFixTime;

	/// <summary>v1.2.4：行右缘与滚动条左缘之间保留的世界像素间隙（实测原生"正常态"就是这个观感）。</summary>
	private const float GapWorld = 6f;

	// ── v1.3.0 排版套件（A/B 档视觉改造）─────────────────────────────
	// 参考目标（玩家截的另一个 mod 设置页）：分区标题带全宽分隔线、每行单行、值控件右对齐成
	// 同一列、深底值框顶部 1px 高光、无多余边框、留白撑起层次。
	/// <summary>值控件列：统一宽度，所有值控件右对齐成一列。
	/// v1.5.0：200→**130**（玩家反馈"输入框太大了"），同时给左边标签留出宽度。</summary>
	private const float ControlWidth = 130f;

	private const float ControlHeight = 24f;

	private const float ControlRight = 8f;

	private const float LabelLeft = 12f;

	private const float LabelGap = 10f;

	/// <summary>单行行高（标签与值框同行）。</summary>
	private const float EntryRowHeight = 32f;

	/// <summary>v1.4.0：统一配色——只保留"主文字/次文字"两级 + 浅色值框，不再有强调色、不再有常驻底色。
	/// 设计原则（玩家要求）：每个元素必须有用，纯装饰一律不要。</summary>
	private static readonly Color TextPrimary = new Color(0.86f, 0.86f, 0.88f, 1f);

	private static readonly Color TextDim = new Color(0.52f, 0.52f, 0.56f, 1f);

	private static readonly Color ValueFill = new Color(0.74f, 0.74f, 0.76f, 1f);

	private static readonly Color ValueText = new Color(0.10f, 0.10f, 0.12f, 1f);

	/// <summary>展开的配置项（键 = cfg|section.key）：默认收起 → 小字简介与【重置】【复制】只在展开时出现，
	/// 把每项 4 行的噪音压到 1 行。</summary>
	internal static readonly HashSet<string> expandedEntries = new HashSet<string>();

	internal static string EntryKey(ConfigFile cfg, ConfigEntryBase entry)
	{
		try
		{
			// v1.5.0 修复（玩家："展开一个 mod 的子选项描述，另一个 mod 的描述也显示了"）：
			// ConfigFile 没重写 ToString() → 所有 cfg 都得到同一个类型名字符串 → 不同 mod 里
			// 同名 section.key（如 General.Enabled）**键冲突**，一处展开处处展开。
			// 改用配置文件的真实路径做唯一标识（同 mod 的 cfg 路径唯一）。
			string id = null;
			try
			{
				id = (cfg != null) ? cfg.ConfigFilePath : null;
			}
			catch
			{
				id = null;
			}
			if (string.IsNullOrEmpty(id))
			{
				id = (cfg != null) ? ("cfg#" + cfg.GetHashCode().ToString()) : "?";
			}
			return id + "|" +
				((entry != null) ? entry.Definition.Section + "." + entry.Definition.Key : "?");
		}
		catch
		{
			return "?";
		}
	}

	/// <summary>行缩进：展开的 mod 内容整体右移，形成父子层次。</summary>
	internal static float RowIndent;

	/// <summary>值控件列统一定位：右对齐 + 固定宽高。</summary>
	private static void PlaceControlColumn(RectTransform crt)
	{
		if (crt == null)
		{
			return;
		}
		crt.anchorMin = new Vector2(1f, 0.5f);
		crt.anchorMax = new Vector2(1f, 0.5f);
		crt.pivot = new Vector2(1f, 0.5f);
		crt.anchoredPosition = new Vector2(-ControlRight, 0f);
		crt.sizeDelta = new Vector2(ControlWidth, ControlHeight);
	}

	/// <summary>标签占位：左起 LabelLeft+缩进，右侧让出控件列，垂直居中。
	/// 注意：水平轴用"拉伸锚点 + sizeDelta/anchoredPosition"表达（不能只写 offsetMin/Max —— 垂直轴
	/// 是固定锚点，offset 全 0 会把行高压成 0，标签直接看不见）。</summary>
	private static void PlaceLabel(RectTransform trt)
	{
		if (trt == null)
		{
			return;
		}
		float leftInset = LabelLeft + RowIndent;
		float rightInset = ControlRight + ControlWidth + LabelGap;
		trt.anchorMin = new Vector2(0f, 0.5f);
		trt.anchorMax = new Vector2(1f, 0.5f);
		trt.pivot = new Vector2(0.5f, 0.5f);
		trt.sizeDelta = new Vector2(-(leftInset + rightInset), ControlHeight);
		trt.anchoredPosition = new Vector2((leftInset - rightInset) * 0.5f, 0f);
	}

	/// <summary>全宽 1px 分隔线（无 sprite 的 Image 就是纯色块；raycastTarget 关掉免得挡点击）。</summary>
	private static void AddHLine(Transform parent, float alpha, float insetLeft = -1f, float insetRight = -1f)
	{
		try
		{
			GameObject go = new GameObject("MM_Line");
			go.transform.SetParent(parent, false);
			RectTransform rt = go.AddComponent<RectTransform>();
			rt.anchorMin = new Vector2(0f, 0f);
			rt.anchorMax = new Vector2(1f, 0f);
			rt.pivot = new Vector2(0.5f, 0f);
			rt.offsetMin = new Vector2((insetLeft >= 0f) ? insetLeft : (LabelLeft + RowIndent), 0f);
			rt.offsetMax = new Vector2(-((insetRight >= 0f) ? insetRight : ControlRight), 0f);
			rt.sizeDelta = new Vector2(rt.sizeDelta.x, 1f);
			Image img = go.AddComponent<Image>();
			img.raycastTarget = false;
			img.color = new Color(1f, 1f, 1f, alpha);
		}
		catch
		{
		}
	}

	/// <summary>v1.3.0：按名字 hash 取柔和强调色（卡片头色条用）。</summary>
	private static Color AccentColor(string name)
	{
		try
		{
			int h = 0;
			if (!string.IsNullOrEmpty(name))
			{
				for (int i = 0; i < name.Length; i++)
				{
					h = (h * 31 + name[i]) & 0x7fffffff;
				}
			}
			Color[] palette = new Color[]
			{
				new Color(0.36f, 0.62f, 0.92f, 0.95f),
				new Color(0.42f, 0.78f, 0.55f, 0.95f),
				new Color(0.92f, 0.72f, 0.35f, 0.95f),
				new Color(0.85f, 0.50f, 0.42f, 0.95f),
				new Color(0.68f, 0.55f, 0.90f, 0.95f),
				new Color(0.45f, 0.78f, 0.85f, 0.95f),
				new Color(0.80f, 0.60f, 0.78f, 0.95f),
				new Color(0.60f, 0.72f, 0.45f, 0.95f)
			};
			return palette[h % palette.Length];
		}
		catch
		{
			return new Color(0.5f, 0.6f, 0.8f, 0.9f);
		}
	}

	/// <summary>v1.3.1：点击音效（原生设置项点击都有 ClickSound；我们自建的控件以前没声）。</summary>
	internal static void PlayClick()
	{
		try
		{
			SoundManager.ClickSound();
		}
		catch
		{
		}
	}

	/// <summary>v1.3.1：行的悬停/点击反馈色——**底噪必须为 0**（v1.3.0 给每行铺了 1.2%~2% 白底，
	/// 实测截图里整页都是灰条 = 用户说的"光污染"），只在悬停/按下时短暂提亮。</summary>
	private static void ApplyRowHoverTint(Button btn)
	{
		try
		{
			if (btn == null)
			{
				return;
			}
			btn.transition = UnityEngine.UI.Selectable.Transition.ColorTint;
			UnityEngine.UI.ColorBlock cb = btn.colors;
			cb.colorMultiplier = 1f;
			cb.normalColor = new Color(1f, 1f, 1f, 0f);
			cb.highlightedColor = new Color(1f, 1f, 1f, 0.035f);
			cb.pressedColor = new Color(1f, 1f, 1f, 0.06f);
			cb.selectedColor = new Color(1f, 1f, 1f, 0f);
			cb.disabledColor = new Color(1f, 1f, 1f, 0f);
			btn.colors = cb;
		}
		catch
		{
		}
	}

	/// <summary>行级交互装饰：整行透明点击区（悬停高亮 + 点击标签区展开/收起该项）。
	/// 放在最底层（SetAsFirstSibling），控件在它上面会先吃掉点击，所以点值框/开关不会误触展开。
	/// v1.3.1：底色 alpha=0（不再铺灰底），点击带原生音效，并把"二级内容"登记到 extras 以便原地展开。</summary>
	private static void FinishEntryRow(GameObject row, ConfigFile cfg, ConfigEntryBase entry, EntryExtras extras)
	{
		try
		{
			if (row == null)
			{
				return;
			}
			Text lbl = null;
			try
			{
				if (row.transform.childCount > 0)
				{
					lbl = row.transform.GetChild(0).GetComponent<Text>();
				}
			}
			catch
			{
			}
			if (lbl == null)
			{
				try
				{
					lbl = row.GetComponentInChildren<Text>(true);
				}
				catch
				{
				}
			}
			if (lbl != null)
			{
				lbl.raycastTarget = false;
				if (extras != null)
				{
					extras.arrow = lbl;
				}
			}
			GameObject hit = new GameObject("HitArea");
			hit.transform.SetParent(row.transform, false);
			hit.transform.SetAsFirstSibling();
			RectTransform hrt = hit.AddComponent<RectTransform>();
			hrt.anchorMin = Vector2.zero;
			hrt.anchorMax = Vector2.one;
			hrt.offsetMin = Vector2.zero;
			hrt.offsetMax = Vector2.zero;
			Image img = hit.AddComponent<Image>();
			img.color = Color.white;
			Button btn = hit.AddComponent<Button>();
			btn.targetGraphic = img;
			ApplyRowHoverTint(btn);
			string key = EntryKey(cfg, entry);
			btn.onClick.RemoveAllListeners();
			System.Action act = delegate
			{
				ToggleEntry(key);
			};
			btn.onClick.AddListener(act);
			if (extras != null)
			{
				entryExtras.Add(extras);
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.3.1：一个配置项的"二级内容"（简介行 + 重置/复制行）——建页时一次性建好，
	/// 点行只做 SetActive 原地切换：**不再整页重建**（v1.3.0 每次点击都重建整页 = 用户看到的闪烁）。</summary>
	private sealed class EntryExtras
	{
		internal string key;
		internal Transform container;
		internal GameObject descRow;
		internal GameObject actionsRow;
		internal Text arrow;
		internal bool expanded;

		/// <summary>v1.5.0：逐级可见性所需的归属（mod → 分区 → 单项）。</summary>
		internal ModBody ownerMod;

		internal SectionBody ownerSection;
	}

	private static readonly List<EntryExtras> entryExtras = new List<EntryExtras>();

	/// <summary>切换单个配置项的展开态（原地激活/隐藏简介与操作行，不重建页面）。</summary>
	internal static void ToggleEntry(string key)
	{
		try
		{
			bool now;
			if (!expandedEntries.Add(key))
			{
				expandedEntries.Remove(key);
				now = false;
			}
			else
			{
				now = true;
			}
			PlayClick();
			for (int i = 0; i < entryExtras.Count; i++)
			{
				EntryExtras ex = entryExtras[i];
				if (ex == null || ex.key != key)
				{
					continue;
				}
				ex.expanded = now;
				if (ex.arrow != null)
				{
					// v1.5.0 修复：原来直接把标签 text 覆盖成箭头 → 展开后**选项名消失**。
					// 正确做法是剥掉旧前缀再拼新前缀（保留名称与范围富文本）。
					ex.arrow.text = (now ? "▾  " : "▸  ") + StripArrowPrefix(ex.arrow.text);
				}
				// 简介行可见性由三层状态统一重算（mod → 分区 → 单项）
				ReapplyInnerVisibility();
				try
				{
					RectTransform crt = ex.container as RectTransform;
					if (crt != null)
					{
						UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
					}
				}
				catch
				{
				}
				SelfHealScroll(ex.container, true);
				break;
			}
		}
		catch (Exception ex2)
		{
			Plugin.ModLog.LogError((object)("ModManager entry toggle error: " + ex2.Message));
		}
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
		// v1.3.1：新一页 → 旧的"二级内容"登记表作废（对象已随整页销毁）
		entryExtras.Clear();
		// v1.5.0：正文/分区的行集合登记表同样作废
		modBodies.Clear();
		sectionBodies.Clear();
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
		// v1.3.0：行距收到 4（行内单行、行高压到 38），分区之间靠"48px 标题行 + 分隔线"拉开层次
		lg.spacing = 4f;
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
					RowIndent = 0f;
					AddCategoryRow(container, letter);
				}
				// 可点击标题（剥离前缀的短名；展开行小字显示完整原名）
				string name = p.name;
				bool expanded = expandedMods.Contains(name);
				RowIndent = 0f;
				Text titleTxt = AddSectionButton(SettingsGUI_V2.instance, container, p.shortName, expanded, name);
				// v1.5.0：正文**始终建好**（收起的 SetActive(false)），点击只原地显隐 —— 不再重建整页
				// （重建时整页新行在同一帧重排，浅色值框会瞬间叠在一起 = 用户看到的"全部选项闪白"）
				ModBody body = new ModBody();
				body.name = name;
				body.title = titleTxt;
				body.expanded = expanded;
				ModBody savedMod = currentModBody;
				currentModBody = body;
				int bodyStart = container.childCount;
				RowIndent = 10f;
				FillModEntries(p, container);
				AddFooterRow(container, p.name, p.cfg);
				RowIndent = 0f;
				currentModBody = savedMod;
				for (int ci = bodyStart; ci < container.childCount; ci++)
				{
					Transform ch = container.GetChild(ci);
					if (ch != null && ch.gameObject != null)
					{
						body.rows.Add(ch.gameObject);
						ch.gameObject.SetActive(expanded);
					}
				}
				modBodies.Add(body);
				// v1.5.0 修复：上面按 mod 状态批量激活会**连带打开**该 mod 里本应收起的分区与简介行
				//（玩家："分区还是默认开启的"、"展开一个 mod 的选项描述，另一个 mod 的描述也显示了"）。
				// 层级由外到内重新压一遍：mod → 分区 → 单项简介，后写的状态覆盖前面的。
				ReapplyInnerVisibility();
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
		// v1.5.0：一次性 LAYOUT/SCROLL 诊断（v1.2.1 引入）已在发布前移除
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

	/// <summary>v1.2.4：把**行右缘钉在滚动条左缘左侧 6 世界像素处**（"灰色名称栏被滑条压住"根治，
	/// 且不闪、不短）。
	/// 实测（2026-09-12 截图逐像素 + 日志）：原生只有在部分路径把 Viewport 收窄 17px 预留滚动条，
	/// 且两条路径会**在两次建页之间来回切换**：正常态 `Viewport 433`（行右缘世界 x≈1875 = 图一）、
	/// 异常态 `Viewport 450`（行右缘 1900 压住滚动条 = 图二）。
	/// 历史坑：v1.2.2 拿容器自己的 rect 当基准 → 缩进去就量不到重叠 → 每帧 24/8 横跳（闪烁）；
	/// v1.2.3 改成单调锁存 → 原生切回收窄态时行宽短掉 25px（截图实测右缘 1849，露出深色缝）。
	/// 现在用**无反馈的绝对目标**：desiredLocal = (contentPage 右缘 − (滚动条左缘 − 6)) / scale，
	/// 基准是 contentPage，不随我们的写入变化 → 原生两态之间切换时本地内缩自动变（8 ↔ 24），
	/// 而行右缘的世界坐标恒定 ≈1875 —— 既压不住滚动条，也不会短一截。</summary>
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
				// 目标：把**行右缘钉在滚动条左缘左侧 GAP 世界像素处**，再换算成"相对当前 contentPage"
				// 的内缩。基准是 contentPage（不随我们的写入变化）→ 计算无反馈、不振荡；
				// 原生在"预留 17px / 不预留"两种 Viewport 状态间切换时，本式自动给出不同的本地内缩
				// （实测 8 ↔ 24），而行右缘的世界坐标保持不变（≈1875）→ 既不会压住滚动条，
				// 也不会像 v1.2.3 的单调锁存那样在切回收窄状态时短掉一截（露出深色缝）。
				float rx0, rx1, ry0, ry1, sx0, sx1, sy0, sy1;
				WorldRect(rt, out rx0, out rx1, out ry0, out ry1);
				WorldRect(sbrt, out sx0, out sx1, out sy0, out sy1);
				float ovY = Math.Min(ry1, sy1) - Math.Max(ry0, sy0);
				float scale = Math.Abs(rt.lossyScale.x) > 0.0001f ? Math.Abs(rt.lossyScale.x) : 1f;
				if (ovY > 1f && sx1 > sx0)
				{
					float mid = (rx0 + rx1) * 0.5f;
					if (sx0 >= mid)
					{
						rightInset = Mathf.Clamp((rx1 - sx0 + GapWorld) / scale, 8f, 60f);
					}
					else if (sx1 <= mid)
					{
						leftInset = Mathf.Clamp((sx1 - rx0 + GapWorld) / scale, 0f, 60f);
					}
				}
			}
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
					+ " right=" + rightInset.ToString("F1") + " (content " + rt.rect.width.ToString("F0")
					+ " -> row width " + (rt.rect.width - w).ToString("F0") + ")"));
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.2.5：每帧重算行内缩。建页那一瞬原生 Viewport 宽度/滚动条位置可能还没定型
	/// （实测：进页后"宽深色缝"约 1 秒才恢复 = 只能等 SelfHealScroll 那一拍 0.5s 自愈），
	/// 而本公式基准是 contentPage（无反馈）→ 每帧重算是安全的，能把纠正提前到当帧。</summary>
	internal static void RefreshRowInset(Transform contentPage)
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
			if (cont == null)
			{
				return;
			}
			RectTransform crt = cont.GetComponent<RectTransform>();
			RectTransform rt = contentPage.GetComponent<RectTransform>();
			if (crt == null || rt == null)
			{
				return;
			}
			ApplyScrollbarInset(contentPage, cont, crt, rt);
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
			// v1.2.4：先按滚动条实际位置把行宽内缩（见 ApplyScrollbarInset 注释）
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

	/// <summary>v1.4.0：字母分组行——只剩一个暗色字母（分组导航用），**不再加分隔线**（纯装饰）。</summary>
	private static float AddCategoryRow(Transform container, string text)
	{
		try
		{
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			GameObject row = new GameObject("MM_CatRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 26f);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(0f, 0.5f);
			trt.pivot = new Vector2(0f, 0.5f);
			trt.anchoredPosition = new Vector2(LabelLeft, -2f);
			trt.sizeDelta = new Vector2(60f, 20f);
			Text txt = tgo.AddComponent<Text>();
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 12;
			txt.fontStyle = FontStyle.Bold;
			txt.color = new Color(0.42f, 0.42f, 0.46f, 1f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.raycastTarget = false;
			txt.text = text;
			return 26f;
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
	private static Text AddSectionButton(SettingsGUI_V2 s, Transform container, string name, bool expanded, string rawName)
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
			string prefix = expanded ? "▾  " : "▸  ";
			float availWidth = TitleAvailWidth(s);
			bool longName = MeasuresWiderThan(rawName, font, fontSize + 2, prefix, availWidth);
			float rowH = (expanded && longName) ? 52f : 34f;
			rt.sizeDelta = new Vector2(0f, rowH);
			// label（锚点拉伸到行宽，左对齐）
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			trt.sizeDelta = new Vector2(-24f, 30f);
			trt.anchoredPosition = new Vector2(0f, expanded && longName ? 8f : 0f);
			Text txt = tgo.AddComponent<Text>();
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = fontSize + 1;
			txt.fontStyle = FontStyle.Bold;
			txt.color = TextPrimary;
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.resizeTextForBestFit = true;
			txt.resizeTextMinSize = 12;
			txt.resizeTextMaxSize = fontSize + 1;
			txt.raycastTarget = false;
			// 超长 mod 名截断为省略号（仅当实测宽度放不下；二分收敛到可容纳的前缀长度）
			string display = name;
			if (longName)
			{
				display = EllipsizeToFit(name, font, fontSize + 1, prefix, availWidth);
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
				srt.sizeDelta = new Vector2(-24f, 18f);
				srt.anchoredPosition = new Vector2(0f, 1f);
				Text stxt = sub.AddComponent<Text>();
				if (font != null)
				{
					stxt.font = font;
				}
				stxt.fontSize = Math.Max(12, fontSize - 5);
				stxt.color = TextDim;
				stxt.alignment = TextAnchor.MiddleLeft;
				stxt.horizontalOverflow = HorizontalWrapMode.Overflow;
				stxt.raycastTarget = false;
				stxt.text = rawName;
			}
			// v1.5.0：mod 名下方加一条横线（玩家要求）——把"mod 标题"与"首字母分组"明确分开
			AddHLine(row.transform, 0.14f);
			// 透明点击区（覆盖整行）+ 悬停高亮
			GameObject hit = new GameObject("HitArea");
			hit.transform.SetParent(row.transform, false);
			hit.transform.SetAsFirstSibling();
			RectTransform hrt = hit.AddComponent<RectTransform>();
			hrt.anchorMin = Vector2.zero;
			hrt.anchorMax = Vector2.one;
			hrt.sizeDelta = Vector2.zero;
			hrt.offsetMin = Vector2.zero;
			hrt.offsetMax = Vector2.zero;
			UnityEngine.UI.Image img = hit.AddComponent<UnityEngine.UI.Image>();
			img.color = Color.white;
			UnityEngine.UI.Button btn = hit.AddComponent<UnityEngine.UI.Button>();
			btn.targetGraphic = img;
			ApplyRowHoverTint(btn);
			string modName = rawName;
			btn.onClick.RemoveAllListeners();
			System.Action act = delegate
			{
				ToggleMod(modName);
			};
			btn.onClick.AddListener(act);
			return txt;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager header error: " + ex.Message));
			return null;
		}
	}

	/// <summary>配置项的取值范围文本（有 AcceptableValueRange 时）。供"展开后的信息行"使用。</summary>
	private static string RangeText(ConfigEntryBase entry)
	{
		try
		{
			AcceptableValueBase av = (entry != null && entry.Description != null) ? entry.Description.AcceptableValues : null;
			if (av is AcceptableValueRange<float> rf)
			{
				return rf.MinValue.ToString("0.##") + " – " + rf.MaxValue.ToString("0.##");
			}
			if (av is AcceptableValueRange<int> ri)
			{
				return ri.MinValue + " – " + ri.MaxValue;
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>行标签统一字号（玩家反馈"文字改变大小后不好看"）：**所有配置项标签同一号**，
	/// 放不下只做省略号截断，绝不逐行改字号。</summary>
	private const int RowLabelFontSize = 16;

	/// <summary>行标签按可用宽度**显式**截断（字号恒为 RowLabelFontSize）。
	/// 说明：不能只靠 `resizeTextForBestFit` —— Unity 的 Text 在 `horizontalOverflow = Overflow`
	/// 下 bestFit 不会缩（没有可缩的边界），长标签会直接压到右边值框下面（玩家截图实测）。</summary>
	private static void FitRowLabel(Text txt, string text)
	{
		try
		{
			if (txt == null)
			{
				return;
			}
			Font font = txt.font;
			float avail = RowLabelAvailWidth();
			txt.resizeTextForBestFit = false;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.fontSize = RowLabelFontSize;
			if (font == null || avail <= 40f || string.IsNullOrEmpty(text))
			{
				txt.text = text;
				return;
			}
			if (MeasureTextWidth(text, font, RowLabelFontSize) <= avail)
			{
				txt.text = text;
				return;
			}
			int lo = 2;
			int hi = Math.Max(2, text.Length - 1);
			int best = 2;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				if (MeasureTextWidth(text.Substring(0, mid) + "…", font, RowLabelFontSize) <= avail)
				{
					best = mid;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}
			txt.text = (best < text.Length) ? (text.Substring(0, best) + "…") : text;
		}
		catch
		{
		}
	}

	/// <summary>行标签可用像素宽：contentPage 实宽 − 左边距/缩进 − 值控件列 − 间隙。</summary>
	private static float RowLabelAvailWidth()
	{
		try
		{
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			Transform cp = (s != null) ? s.contentPage : null;
			if (cp != null)
			{
				RectTransform rt = cp.GetComponent<RectTransform>();
				if (rt != null && rt.rect.width > 60f)
				{
					return rt.rect.width - (LabelLeft + RowIndent) - (ControlRight + ControlWidth + LabelGap);
				}
			}
		}
		catch
		{
		}
		return 1920f * 0.45f - 8f - 218f;
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
			rt.sizeDelta = new Vector2(0f, 28f);
			// 注：v1.0.32 起无【保存】按钮——退出设置（Esc/继续）时自动保存所有暂存改动
			// v1.4.0：页脚两个动作改成**纯文字按钮**（原来填灰底 = 满屏灰块的主要来源）
			Text resetTxt = null;
			resetTxt = MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-ControlRight, 0f), new Vector2(72f, 22f), Plugin.DefaultChinese ? "重置全部" : "Reset all", delegate
			{
				ResetModSettings(cfg, modName);
			});
			Text copyTxt = null;
			copyTxt = MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(ControlRight + 78f), 0f), new Vector2(72f, 22f), Plugin.DefaultChinese ? "复制全部" : "Copy all", delegate
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
		System.Action wrapped = delegate
		{
			PlayClick();
			try
			{
				onClick();
			}
			catch
			{
			}
		};
		bbtn.onClick.AddListener(wrapped);
		result = btxt;
		}
		catch
		{
		}
		return result;
	}

	/// <summary>v1.4.0：值框样式的按钮（浅底 + 深色居中文字，与数值输入框同款）——改键按钮用它，
	/// 免得用灰底按钮又多出一块与整体不一致的色块。</summary>
	private static Text MakeValueButton(GameObject row, Vector2 anchoredPos, Vector2 size, string label, System.Action onClick)
	{
		Text result = null;
		try
		{
			GameObject btnGo = new GameObject("ValueBtn");
			btnGo.transform.SetParent(row.transform, false);
			RectTransform brt = btnGo.AddComponent<RectTransform>();
			brt.anchorMin = new Vector2(1f, 0.5f);
			brt.anchorMax = new Vector2(1f, 0.5f);
			brt.pivot = new Vector2(1f, 0.5f);
			brt.anchoredPosition = anchoredPos;
			brt.sizeDelta = size;
			UnityEngine.UI.Image bimg = btnGo.AddComponent<UnityEngine.UI.Image>();
			bimg.color = ValueFill;
			UnityEngine.UI.Button bbtn = btnGo.AddComponent<UnityEngine.UI.Button>();
			bbtn.targetGraphic = bimg;
			UnityEngine.UI.ColorBlock cb = bbtn.colors;
			cb.colorMultiplier = 1f;
			cb.normalColor = Color.white;
			cb.highlightedColor = new Color(1.12f, 1.12f, 1.12f, 1f);
			cb.pressedColor = new Color(0.86f, 0.86f, 0.86f, 1f);
			cb.selectedColor = Color.white;
			cb.disabledColor = new Color(0.6f, 0.6f, 0.6f, 1f);
			bbtn.colors = cb;
			GameObject btgo = new GameObject("Text");
			btgo.transform.SetParent(btnGo.transform, false);
			RectTransform btrt = btgo.AddComponent<RectTransform>();
			btrt.anchorMin = Vector2.zero;
			btrt.anchorMax = Vector2.one;
			btrt.offsetMin = new Vector2(6f, 2f);
			btrt.offsetMax = new Vector2(-6f, -2f);
			Text btxt = btgo.AddComponent<Text>();
			Font font = GetDigitSafeFont(SettingsGUI_V2.instance);
			if (font == null)
			{
				font = GetNativeFont(SettingsGUI_V2.instance);
			}
			if (font != null)
			{
				btxt.font = font;
			}
			btxt.fontSize = 15;
			btxt.color = ValueText;
			btxt.alignment = TextAnchor.MiddleCenter;
			btxt.horizontalOverflow = HorizontalWrapMode.Overflow;
			btxt.resizeTextForBestFit = true;
			btxt.resizeTextMinSize = 10;
			btxt.resizeTextMaxSize = 16;
			btxt.raycastTarget = false;
			btxt.text = label;
			bbtn.onClick.RemoveAllListeners();
			System.Action wrapped = delegate
			{
				PlayClick();
				try
				{
					onClick();
				}
				catch
				{
				}
			};
			bbtn.onClick.AddListener(wrapped);
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
					Text secTitle = AddSectionHeader(container, HumanizeKey(sec), expanded, key, bySection[sec].Count);
					// v1.5.0：分区正文也**始终建好**，按展开态显隐（点击分区标题原地切换，不重建页面）
					SectionBody sb = new SectionBody();
					sb.key = key;
					sb.expanded = expanded;
					sb.title = secTitle;
					sb.owner = currentModBody;
					SectionBody savedSec = currentSectionBody;
					currentSectionBody = sb;
					int secStart = container.childCount;
					// 分区内的配置项再缩进一级，形成"分区标题 ↔ 内容"的层级（玩家反馈两者只有颜色差异）
					float savedIndent = RowIndent;
					RowIndent = savedIndent + 12f;
					foreach (ConfigEntryBase e in bySection[sec])
					{
						AddSetting(p.cfg, e, container);
					}
					RowIndent = savedIndent;
					currentSectionBody = savedSec;
					for (int ci = secStart; ci < container.childCount; ci++)
					{
						Transform ch = container.GetChild(ci);
						if (ch != null && ch.gameObject != null)
						{
							sb.rows.Add(ch.gameObject);
							ch.gameObject.SetActive(expanded);
						}
					}
					sectionBodies.Add(sb);
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

	/// <summary>v1.5.0：分区小标题——目的有二：① 分组（下方一条极淡分隔线）② 与配置项明确区分
	/// （玩家反馈"子文件夹和选项只有颜色差异，不容易区分"）：带 ▸/▾ 折叠箭头（**默认收起**，
	/// 箭头随展开状态切换）、字号更小、且其下的配置项会**再缩进一级**。点标题折叠/展开。
	/// 返回标题 Text，供 ToggleSection 原地切换箭头。</summary>
	private static Text AddSectionHeader(Transform container, string title, bool expanded, string key, int count)
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
			float li = LabelLeft + RowIndent;
			float ri = ControlRight;
			trt.sizeDelta = new Vector2(-(li + ri), 20f);
			trt.anchoredPosition = new Vector2((li - ri) * 0.5f, 4f);
			Text txt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 13;
			txt.fontStyle = FontStyle.Bold;
			txt.color = TextDim;
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.raycastTarget = false;
			txt.text = (expanded ? "▾  " : "▸  ") + (Plugin.DefaultChinese ? title : title.ToUpperInvariant());
			AddHLine(row.transform, 0.16f);
			GameObject hit = new GameObject("HitArea");
			hit.transform.SetParent(row.transform, false);
			hit.transform.SetAsFirstSibling();
			RectTransform hrt = hit.AddComponent<RectTransform>();
			hrt.anchorMin = Vector2.zero;
			hrt.anchorMax = Vector2.one;
			hrt.offsetMin = Vector2.zero;
			hrt.offsetMax = Vector2.zero;
			Image img = hit.AddComponent<Image>();
			img.color = Color.white;
			UnityEngine.UI.Button btn = hit.AddComponent<UnityEngine.UI.Button>();
			btn.targetGraphic = img;
			ApplyRowHoverTint(btn);
			System.Action toggleAction = delegate
			{
				ToggleSection(key);
			};
			btn.onClick.RemoveAllListeners();
			btn.onClick.AddListener(toggleAction);
			return txt;
		}
		catch
		{
			return null;
		}
	}

	/// <summary>v1.5.0：一个 mod 的正文行集合（收起时整体 SetActive(false)，点击标题原地显隐）。</summary>
	private sealed class ModBody
	{
		internal string name;
		internal Text title;
		internal bool expanded;
		internal readonly List<GameObject> rows = new List<GameObject>();
	}

	/// <summary>v1.5.0：一个分区的正文行集合（同上）。title = 标题 Text，用于切换 ▸/▾ 状态显示；
	/// owner = 所属 mod（可见性要逐级与：mod 收起时分区内容也不能露出来）。</summary>
	private sealed class SectionBody
	{
		internal string key;
		internal Text title;
		internal bool expanded;
		internal ModBody owner;
		internal readonly List<GameObject> rows = new List<GameObject>();
	}

	private static ModBody currentModBody;

	private static SectionBody currentSectionBody;

	private static readonly List<ModBody> modBodies = new List<ModBody>();

	private static readonly List<SectionBody> sectionBodies = new List<SectionBody>();

	/// <summary>v1.5.0：按"mod → 分区 → 单项简介"三层状态重算每一行的可见性（由内到外都要为真）。
	/// 建页与任何一次展开/收起之后都要调用——否则会出现"分区默认是开的""另一个 mod 的简介
	/// 也显示了"这类串状态问题。</summary>
	internal static void ReapplyInnerVisibility()
	{
		try
		{
			for (int i = 0; i < sectionBodies.Count; i++)
			{
				SectionBody sb = sectionBodies[i];
				if (sb == null)
				{
					continue;
				}
				bool on = sb.expanded && (sb.owner == null || sb.owner.expanded);
				for (int j = 0; j < sb.rows.Count; j++)
				{
					if (sb.rows[j] != null)
					{
						sb.rows[j].SetActive(on);
					}
				}
			}
			for (int i = 0; i < entryExtras.Count; i++)
			{
				EntryExtras ex = entryExtras[i];
				if (ex == null || ex.descRow == null)
				{
					continue;
				}
				bool on = ex.expanded
					&& (ex.ownerSection == null || ex.ownerSection.expanded)
					&& (ex.ownerMod == null || ex.ownerMod.expanded);
				ex.descRow.SetActive(on);
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.5.0：原地切换一个 mod 的展开态（不重建页面 → 不闪）。</summary>
	internal static void ToggleMod(string name)
	{
		try
		{
			bool now;
			if (!expandedMods.Add(name))
			{
				expandedMods.Remove(name);
				now = false;
			}
			else
			{
				now = true;
			}
			PlayClick();
			for (int i = 0; i < modBodies.Count; i++)
			{
				if (modBodies[i] == null || modBodies[i].name != name)
				{
					continue;
				}
				ModBody mb = modBodies[i];
				mb.expanded = now;
				for (int j = 0; j < mb.rows.Count; j++)
				{
					if (mb.rows[j] != null)
					{
						mb.rows[j].SetActive(now);
					}
				}
				if (mb.title != null)
				{
					mb.title.text = (now ? "▾  " : "▸  ") + StripArrowPrefix(mb.title.text);
				}
				ReapplyInnerVisibility();
				RelayoutContainer(mb.rows);
				break;
			}		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager toggle error: " + ex.Message));
		}
	}

	/// <summary>剥掉行首的展开箭头（▸/▾）与后续空格，保留名称与富文本后缀。</summary>
	private static string StripArrowPrefix(string s)
	{
		try
		{
			if (string.IsNullOrEmpty(s))
			{
				return s ?? "";
			}
			if (s[0] == '▾' || s[0] == '▸')
			{
				return s.Substring(1).TrimStart();
			}
			return s;
		}
		catch
		{
			return s ?? "";
		}
	}

	/// <summary>行集合变更后重排 + 重算滚动高度（不重建页面）。</summary>
	private static void RelayoutContainer(List<GameObject> rows)
	{
		try
		{
			Transform container = null;
			if (rows != null)
			{
				for (int i = 0; i < rows.Count; i++)
				{
					if (rows[i] != null)
					{
						container = rows[i].transform.parent;
						break;
					}
				}
			}
			if (container == null)
			{
				return;
			}
			UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(container as RectTransform);
			SelfHealScroll(container, true);
		}
		catch
		{
		}
	}

	/// <summary>切换分区展开状态（v1.5.0：原地显隐，不重建页面）。</summary>
	internal static void ToggleSection(string key)
	{
		try
		{
			bool now;
			if (!expandedSections.Add(key))
			{
				expandedSections.Remove(key);
				now = false;
			}
			else
			{
				now = true;
			}
			PlayClick();
			for (int i = 0; i < sectionBodies.Count; i++)
			{
				SectionBody sb = sectionBodies[i];
				if (sb == null || sb.key != key)
				{
					continue;
				}
				sb.expanded = now;
				for (int j = 0; j < sb.rows.Count; j++)
				{
					if (sb.rows[j] != null)
					{
						sb.rows[j].SetActive(now);
					}
				}
				// v1.5.0 修复：分区箭头以前不更新（玩家："箭头不会变化，无法显示展开状态"）
				if (sb.title != null)
				{
					sb.title.text = (now ? "▾  " : "▸  ") + StripArrowPrefix(sb.title.text);
				}
				ReapplyInnerVisibility();
				RelayoutContainer(sb.rows);
				break;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager section toggle error: " + ex.Message));
		}
	}

	/// <summary>克隆模板、挂到自动布局容器、设置 label 文本。
	/// v1.3.0：统一行高，并把行内标签与值控件分别对齐到"左 + 缩进"和"统一右列"——
	/// 原生模板各有各的宽度，不对齐就会像以前那样参差。</summary>
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
			rt.sizeDelta = new Vector2(rt.sizeDelta.x, EntryRowHeight);
		}
		try
		{
			if (go.transform.childCount > 0)
			{
				RectTransform lrt = go.transform.GetChild(0).GetComponent<RectTransform>();
				if (lrt != null)
				{
					PlaceLabel(lrt);
				}
				Text t0 = go.transform.GetChild(0).GetComponent<Text>();
				if (t0 != null)
				{
					t0.raycastTarget = false;
					t0.color = new Color(0.88f, 0.88f, 0.9f, 1f);
					t0.alignment = TextAnchor.MiddleLeft;
					// v1.5.0：原生模板行的标签同样统一字号 + 超宽截断（否则长名会压到值控件上）
					FitRowLabel(t0, label);
				}
				if (go.transform.childCount > 1)
				{
					RectTransform crt2 = go.transform.GetChild(1).GetComponent<RectTransform>();
					if (crt2 != null)
					{
						// 开关：复选框本身贴在容器左端，所以容器只给窄宽并右对齐 → 复选框落在右列边缘；
						// 其余控件（下拉/滑条/输入框）占满统一右列。
						bool isToggle = false;
						try
						{
							isToggle = go.transform.GetChild(1).GetComponent<UnityEngine.UI.Toggle>() != null;
						}
						catch
						{
						}
						float h = crt2.sizeDelta.y;
						if (h < 20f || h > 40f)
						{
							h = ControlHeight;
						}
						crt2.anchorMin = new Vector2(1f, 0.5f);
						crt2.anchorMax = new Vector2(1f, 0.5f);
						crt2.pivot = new Vector2(1f, 0.5f);
						crt2.anchoredPosition = new Vector2(-ControlRight, 0f);
						crt2.sizeDelta = new Vector2(isToggle ? 34f : ControlWidth, h);
					}
				}
			}
		}
		catch
		{
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
			string ekey = EntryKey(cfg, entry);
			bool entryExpanded = expandedEntries.Contains(ekey);
			// v1.5.0：箭头**一直显示**（玩家要求）：收起 ▸ / 展开 ▾ —— 一眼能看出这行可展开
			string rowLabel = (entryExpanded ? "▾  " : "▸  ") + label;
			Type t = entry.SettingType;
			int before = container.childCount;
			bool ok = false;
			if (t == typeof(bool))
			{
				ok = AddToggle(cfg, entry, container, rowLabel);
			}
			else if (t == typeof(float))
			{
				ok = AddNumericInput(cfg, entry, container, rowLabel, true);
			}
			else if (t == typeof(int))
			{
				ok = AddNumericInput(cfg, entry, container, rowLabel, false);
			}
			else if (t == typeof(string))
			{
				// 键名含 key/toggle 且选项是按键列表 → 用"点击改键"按钮（与 KeyCode 热键统一）；
				// 其余 string → 下拉
				if (IsHotkeyEntry(entry) && LooksLikeKeyList(entry))
				{
					ok = AddHotkeyRebind(cfg, entry, container, rowLabel, false);
				}
				else
				{
					ok = AddDropdown(cfg, entry, container, rowLabel);
				}
			}
			else if (t == typeof(KeyCode))
			{
				// 原生快捷键（KeyCode 类型）→ 点击改键按钮（按下新键即捕获）
				ok = AddHotkeyRebind(cfg, entry, container, rowLabel, true);
			}
			else if (t.IsEnum)
			{
				// 枚举：键名含 key/toggle → 点击改键按钮（如 InputSystem Key 热键）；
				// 其他枚举 → 下拉选择
				if (IsHotkeyEntry(entry))
				{
					ok = AddHotkeyRebind(cfg, entry, container, rowLabel, false, t);
				}
				else
				{
					ok = AddDropdownEnum(cfg, entry, container, rowLabel);
				}
			}
			bool createdRow = container.childCount > before;
			if (!ok)
			{
				// v1.1.6：全局兜底——任何类型/模板路径失败都渲染只读文本行，配置项永不静默消失
				AddFallbackTextRow(container, rowLabel + ": " + FormatEntryValue(entry));
				createdRow = true;
				ok = true;
			}
			if (createdRow)
			{
				// 行级交互：整行悬停高亮 + 点击标签区展开该项（控件会先吃掉自己的点击）
				GameObject row = container.GetChild(container.childCount - 1).gameObject;
				EntryExtras ex = new EntryExtras();
				ex.key = ekey;
				ex.container = container;
				ex.expanded = entryExpanded;
				ex.ownerMod = currentModBody;
				ex.ownerSection = currentSectionBody;
				FinishEntryRow(row, cfg, entry, ex);
				// v1.5.0：二级内容 = 取值范围 + 简介（都是"需要时再看"的信息，纯文本，无富文本标签）
				string desc = GetDescription(entry);
				string range = RangeText(entry);
				string info = "";
				if (!string.IsNullOrEmpty(range))
				{
					info = (Plugin.DefaultChinese ? "取值范围 " : "Range ") + range;
				}
				if (!string.IsNullOrEmpty(desc))
				{
					info = string.IsNullOrEmpty(info) ? desc : (info + "   ·   " + desc);
				}
				if (!string.IsNullOrEmpty(info))
				{
					AddDescriptionRow(container, info);
					ex.descRow = container.GetChild(container.childCount - 1).gameObject;
					ex.descRow.SetActive(entryExpanded);
				}
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

	/// <summary>v1.3.0：配置项展开后的小字介绍行（灰色小字，按文本长度估算行高，稳定不重叠）。
	/// 默认收起 → 只在点开该项时出现，列表本体保持单行清爽。</summary>
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
			int lines = Math.Max(1, (int)Math.Ceiling(width / 400f) + 1);
			float h = lines * 20f + 6f;
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
			trt.sizeDelta = new Vector2(0f, 0f);
			trt.offsetMin = new Vector2(LabelLeft + RowIndent + 18f, 0f);
			trt.offsetMax = new Vector2(-ControlRight, 0f);
			Text txt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 14;
			txt.fontStyle = FontStyle.Normal;
			txt.color = new Color(0.62f, 0.62f, 0.68f, 0.95f);
			txt.alignment = TextAnchor.UpperLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Wrap;
			txt.verticalOverflow = VerticalWrapMode.Overflow;
			txt.raycastTarget = false;
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
			// v1.3.0 单行布局：标签左、值框在统一右列（参考图观感）；范围收进标签尾部的小灰字，
			// 不再单独占一行（旧版三件套 = 上行标签 + 下行输入框 + 左下范围提示，行高 64 且显挤）。
			GameObject row = new GameObject("MM_NumRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, EntryRowHeight);

			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			PlaceLabel(trt);
			Text txt = tgo.AddComponent<Text>();
			if (labelFont != null)
			{
				txt.font = labelFont;
			}
			txt.color = new Color(0.88f, 0.88f, 0.9f, 1f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.raycastTarget = false;
			// v1.5.0：标签**只用纯文本**（富文本 <color>/<size> 在这个 Text 上不生效，标签会
			// 原样显示出一串 <COLOR=#6E6E78> 又变超长 → 继续压框）。范围提示改到"展开后的简介行"。
			FitRowLabel(txt, label);

			// 值框：统一右列 + 深底 + 顶部 1px 高光 + 数字居中（照参考图）
			GameObject igo = new GameObject("Value");
			igo.transform.SetParent(row.transform, false);
			RectTransform irt = igo.AddComponent<RectTransform>();
			PlaceControlColumn(irt);
			UnityEngine.UI.Image img = igo.AddComponent<UnityEngine.UI.Image>();
			UnityEngine.UI.InputField field = igo.AddComponent<UnityEngine.UI.InputField>();
			// v1.3.1：改成原生/参考图那种**浅色值框 + 深色数字**（以前是深底白字，跟原生设置页不搭）
			img.color = new Color(0.78f, 0.78f, 0.80f, 1f);
			GameObject hl = new GameObject("TopHighlight");
			hl.transform.SetParent(igo.transform, false);
			RectTransform hlrt = hl.AddComponent<RectTransform>();
			hlrt.anchorMin = new Vector2(0f, 1f);
			hlrt.anchorMax = new Vector2(1f, 1f);
			hlrt.pivot = new Vector2(0.5f, 1f);
			hlrt.offsetMin = new Vector2(0f, 0f);
			hlrt.offsetMax = new Vector2(0f, 0f);
			hlrt.sizeDelta = new Vector2(0f, 1f);
			Image hlimg = hl.AddComponent<Image>();
			hlimg.raycastTarget = false;
			hlimg.color = new Color(1f, 1f, 1f, 0.55f);
			GameObject sh = new GameObject("BottomShade");
			sh.transform.SetParent(igo.transform, false);
			RectTransform shrt = sh.AddComponent<RectTransform>();
			shrt.anchorMin = new Vector2(0f, 0f);
			shrt.anchorMax = new Vector2(1f, 0f);
			shrt.pivot = new Vector2(0.5f, 0f);
			shrt.offsetMin = Vector2.zero;
			shrt.offsetMax = Vector2.zero;
			shrt.sizeDelta = new Vector2(0f, 1f);
			Image shimg = sh.AddComponent<Image>();
			shimg.raycastTarget = false;
			shimg.color = new Color(0f, 0f, 0f, 0.25f);

			GameObject igoT = new GameObject("Text");
			igoT.transform.SetParent(igo.transform, false);
			RectTransform itrt = igoT.AddComponent<RectTransform>();
			itrt.anchorMin = new Vector2(0f, 0f);
			itrt.anchorMax = new Vector2(1f, 1f);
			itrt.offsetMin = new Vector2(10f, 3f);
			itrt.offsetMax = new Vector2(-10f, -3f);
			Text itxt = igoT.AddComponent<Text>();
			if (font != null)
			{
				itxt.font = font;
			}
			itxt.fontSize = 15;
			itxt.color = new Color(0.10f, 0.10f, 0.12f, 1f);
			itxt.alignment = TextAnchor.MiddleCenter;
			itxt.horizontalOverflow = HorizontalWrapMode.Overflow;
			itxt.raycastTarget = false;
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

	/// <summary>v1.3.0：自由文本输入（string 配置且无 AcceptableValueList，如颜色 #RRGGBBAA）。
	/// 单行布局同数值行：标签左 + 值框在统一右列。文本经轮询直写暂存（原文，含空串）。</summary>
	private static bool AddFreeTextInput(ConfigFile cfg, ConfigEntryBase entry, Transform container, string label)
	{
		try
		{
			Font labelFont = GetNativeFont(SettingsGUI_V2.instance);
			GameObject row = new GameObject("MM_TextRow_Input");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, EntryRowHeight);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			PlaceLabel(trt);
			Text txt = tgo.AddComponent<Text>();
			if (labelFont != null)
			{
				txt.font = labelFont;
			}
			txt.color = new Color(0.88f, 0.88f, 0.9f, 1f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.raycastTarget = false;
			FitRowLabel(txt, label);
			// 值框（统一右列 + 顶部高光）
			GameObject igo = new GameObject("Value");
			igo.transform.SetParent(row.transform, false);
			RectTransform irt = igo.AddComponent<RectTransform>();
			PlaceControlColumn(irt);
			UnityEngine.UI.Image img = igo.AddComponent<UnityEngine.UI.Image>();
			UnityEngine.UI.InputField field = igo.AddComponent<UnityEngine.UI.InputField>();
			img.color = new Color(0.78f, 0.78f, 0.80f, 1f);
			GameObject hl = new GameObject("TopHighlight");
			hl.transform.SetParent(igo.transform, false);
			RectTransform hlrt = hl.AddComponent<RectTransform>();
			hlrt.anchorMin = new Vector2(0f, 1f);
			hlrt.anchorMax = new Vector2(1f, 1f);
			hlrt.pivot = new Vector2(0.5f, 1f);
			hlrt.offsetMin = Vector2.zero;
			hlrt.offsetMax = Vector2.zero;
			hlrt.sizeDelta = new Vector2(0f, 1f);
			Image hlimg = hl.AddComponent<Image>();
			hlimg.raycastTarget = false;
			hlimg.color = new Color(1f, 1f, 1f, 0.55f);
			GameObject igoT = new GameObject("Text");
			igoT.transform.SetParent(igo.transform, false);
			RectTransform itrt = igoT.AddComponent<RectTransform>();
			itrt.anchorMin = new Vector2(0f, 0f);
			itrt.anchorMax = new Vector2(1f, 1f);
			itrt.offsetMin = new Vector2(10f, 3f);
			itrt.offsetMax = new Vector2(-10f, -3f);
			Text itxt = igoT.AddComponent<Text>();
			Font font = GetDigitSafeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				itxt.font = font;
			}
			itxt.fontSize = 15;
			itxt.color = new Color(0.10f, 0.10f, 0.12f, 1f);
			itxt.alignment = TextAnchor.MiddleCenter;
			itxt.horizontalOverflow = HorizontalWrapMode.Overflow;
			itxt.raycastTarget = false;
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
			rt.sizeDelta = new Vector2(0f, EntryRowHeight);
			// 标签（左）
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			PlaceLabel(trt);
			Text ltxt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				ltxt.font = font;
			}
			ltxt.color = new Color(0.88f, 0.88f, 0.9f, 1f);
			ltxt.alignment = TextAnchor.MiddleLeft;
			ltxt.raycastTarget = false;
			FitRowLabel(ltxt, label);
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
			// v1.4.0：改键按钮用"值框样式"（浅底深字），None 改成右侧一个暗色小字按钮
			// （220 宽的右列：键位框 148 + 间隔 6 + None 46）
			btnTxt = MakeValueButton(row, new Vector2(-(ControlRight + 52f), 0f), new Vector2(ControlWidth - 52f, ControlHeight), HumanizeKey(cur), delegate
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
				MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-ControlRight, 0f), new Vector2(46f, 20f), "None", delegate
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
			System.Action wrapped = delegate
			{
				PlayClick();
				try
				{
					onClick();
				}
				catch
				{
				}
			};
			bbtn.onClick.AddListener(wrapped);
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
			rt.sizeDelta = new Vector2(0f, 26f);
			// 布局与分区底部一致：重置在右、复制在左；v1.3.0 起只在展开该项时出现
			Text resetTxt = null;
			resetTxt = MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-ControlRight, 0f), new Vector2(64f, 22f), Plugin.DefaultChinese ? "重置" : "Reset", delegate
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
			copyTxt = MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-(ControlRight + 70f), 0f), new Vector2(64f, 22f), Plugin.DefaultChinese ? "复制" : "Copy", delegate
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
			rt.sizeDelta = new Vector2(0f, EntryRowHeight);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			PlaceLabel(trt);
			Text txt = tgo.AddComponent<Text>();
			if (font != null)
			{
				txt.font = font;
			}
			txt.color = new Color(0.82f, 0.82f, 0.86f, 1f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.raycastTarget = false;
			FitRowLabel(txt, text);
			return EntryRowHeight;
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
					return;
				}
				ModRegistry.lastPollTime = Time.unscaledTime;
			}
			else
			{
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
				// v1.2.5：每帧重算行内缩（无反馈公式，重算安全）——建页瞬间原生几何未定型时
				// 当帧就能纠正，不用等 0.5s 的自愈节拍（用户实测"宽深色缝约 1 秒后恢复"）。
				ModRegistry.RefreshRowInset(s.contentPage);
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