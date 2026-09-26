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

[BepInPlugin("er2.modmanager", "ER2 Mod Manager", "1.7.9")]
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

	/// <summary>调试日志开关（发布版保持关闭）。开启后输出建页/模板/取值等诊断日志，用于问题排查。</summary>
	internal static ConfigEntry<bool> debugLog;

	/// <summary>v1.5.17：是否记住**分区**展开状态（默认开）。
	/// 沿革：v1.5.20 曾因"重启游戏后这点记忆没必要"改为关闭，但那是个误判——它同时是分区状态的
	/// 唯一持久化来源，关掉后每次启动都从零开始（展开 mod 只看到几行分区标题 = 玩家说的"没内容"），
	/// v1.5.23 恢复为开。
	/// **v1.7.1：作用范围收窄到分区**——mod 自身的展开态不再跨启动恢复（玩家反馈"进游戏后某个 mod
	/// 是展开的"）。分区记忆仍必要：否则每个 mod 里的分组都要重新点开。</summary>
	internal static ConfigEntry<bool> rememberExpanded;

	/// <summary>v1.5.19：延迟构建（折叠的 mod 只建标题行，正文首次展开时才建）。
	/// 收益：进 MODS 页的建页成本从"全部条目"降到"标题 + 已展开的 mod"
	///（本机实测 942 行 / 0.3~0.4 秒 → 71 行 / 35~98ms）。
	/// v1.7.9 起**默认开启**：历史上的三次"点开没内容"（v1.5.9/1.5.19/1.5.23）真因已定案并修复
	///（插入点被自己的兜底钳掉，v1.7.4）、结构/布局自检兜底在位（v1.7.3，连续失败自动回退常规路径）、
	/// 展开窗口探针四采样点全干净（v1.7.5），玩家多轮实测确认。</summary>
	internal static ConfigEntry<bool> lazyBuild;

	/// <summary>v1.7.5：展开时给"刚建好的那批行"做淡入（默认开）。
	/// 起因：延迟建页下首次展开要现建整段行，而行的外观/几何要到该帧末才落定——只要有任何一拍
	/// 慢一帧，玩家就看到"展开时选项闪一下"。淡入把那 1~2 帧盖掉：CanvasGroup **不参与布局**，
	/// 行照常占位，所以周围行不会因为"先隐藏"再多跳一次。收起时立即收尾（见 FinishRevealFor）。</summary>
	internal static ConfigEntry<bool> revealAnim;

	/// <summary>v1.7.5：淡入时长（毫秒，默认 200；0 = 只静默一帧不渐变）。</summary>
	internal static ConfigEntry<int> revealMs;

	/// <summary>v1.7.8：是否在 MODS 页顶部显示热键冲突提示的明细行（页顶小块头上的"隐藏/显示"
	/// 按钮写这个值并立即落盘）。块头本身始终显示（隐藏了也能看到"共 N 组"）。</summary>
	internal static ConfigEntry<bool> hotkeyWarnings;

	/// <summary>v1.7.8：ModManager 自己的 cfg 文件引用——页顶按钮切换后立即落盘用
	///（BepInEx 不保证在条目变更时即时写盘，显式 Save 一次最稳）。</summary>
	internal static ConfigFile OwnConfig;

	/// <summary>调试日志是否开启。debugLog 未就绪时一律视为关闭 → 发布版默认静默。</summary>
	internal static bool DebugOn
	{
		get
		{
			try
			{
				return debugLog != null && debugLog.Value;
			}
			catch
			{
				return false;
			}
		}
	}

	internal static Harmony HarmonyInstance;

	public override void Load()
	{
		ModLog = Log;
		enabled = Config.Bind("General", "enabled", true, "Master switch for the Mod Manager page (restart required).");
		debugLog = Config.Bind("Debug", "debugLog", false, "Debug logging (keep OFF in release). Prints page-build / template / value diagnostics.");
		// v1.5.20：展开状态记忆——**默认关闭**（理由见字段注释：会话内本就不丢，且与延迟建页互相抵消）
		// v1.5.22：**恢复默认开启**。v1.5.20 依据"重启游戏后这点展开记忆没必要"的质疑把它改成了
		// false，但那是个误判——它同时是"哪些 mod 展开"和"哪些分区被手动收起"的**唯一持久化来源**：
		// 关掉之后每次启动两者都从零开始，于是展开任何 mod 都退回初始状态（当时分区还是**默认收起**，
		// 玩家只看到 4~7 行分区标题、看不到配置项，他描述为"mod 展开后不出内容"）。
		// 它和延迟建页的进页成本也没有冲突（未展开的 mod 本来就不建行），恢复开启没有代价。
		rememberExpanded = Config.Bind("Ui", "rememberExpanded", true, "Remember which setting groups were opened inside each mod, across game restarts. Mods themselves always start folded.");
		// v1.5.19 引入；v1.5.24 起默认关闭（"展开时插入行"三次复现"点开没内容"）。
		// **v1.7.9 默认开启**：真因已定案并修复（v1.7.4：插入点被自己的兜底钳掉），
		// 自检 + 有界回退在位（v1.7.3：连续两次失败自动弃用并整页重建，最坏=一次常规建页），
		// 展开窗口探针（v1.7.5）四采样点全干净，玩家多轮实测确认。
		lazyBuild = Config.Bind("Ui", "lazyBuild", true, "Build a folded mod's settings only when you first expand it (much faster page open). Every deferred build is self-checked; if anything looks wrong it automatically falls back to a full page build.");
		// v1.7.5：展开淡入——见 revealAnim 字段注释。目的是把"延迟建页/延迟重排那两拍"盖住，
		// 顺带给展开一个可见的过渡（玩家提议"做个动画掩饰加载"，这里做成淡入，成本可控、可关掉）。
		revealAnim = Config.Bind("Ui", "revealAnim", true, "Fade newly built rows in when a mod is expanded. Masks the one-frame settle of a lazily built body. Set false to show them instantly.");
		revealMs = Config.Bind("Ui", "revealMs", 200, "Fade-in duration in milliseconds for the expand reveal (0 = no fade, hold one frame only).");
		// v1.7.8：热键冲突明细的显示开关（页顶块头按钮切换；ModManager 自己的设置页里也会出现同一项）。
		hotkeyWarnings = Config.Bind("Ui", "hotkeyWarnings", true, "Show the individual hotkey conflict warnings at the top of the MODS page (toggle with the small button next to the warnings header).");
		OwnConfig = Config;
		ModRegistry.LoadExpandedState();
		ModRegistry.LoadFavorites();
		HarmonyInstance = new Harmony("er2.modmanager");
		HarmonyInstance.PatchAll(GetType().Assembly);
		ModLog.LogInfo((object)"ER2 Mod Manager 1.7.9 loaded.");
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
		// 2. 每词 Title Case：首字母大写，其余小写 —— 但**常见缩写整词还原**（v1.5.17）。
		// 原实现把 AI→Ai、HUD→Hud、UI→Ui、FOV→Fov、MG→Mg，标签明显不对（注释里早已承认）。
		// 判据用**白名单**而不是"整词全大写就保留"：配置文件里 SCREAMING_SNAKE_CASE 很常见，
		// 那个判据会把 MAX_COUNT 显示成 "MAX COUNT"（期望 "Max Count"），只有确知的缩写才保留。
		System.Text.StringBuilder result = new System.Text.StringBuilder();
		for (int wi = 0; wi < words.Count; wi++)
		{
			string w = words[wi];
			if (w.Length == 0)
			{
				continue;
			}
			bool isAcronym = IsKnownAcronym(w);
			// 缩写 + 紧随数字 → 合并（MG + 42 → MG42，WW + 1 → WW1，M + 1 → M1）
			string digits = null;
			if (isAcronym && wi + 1 < words.Count && IsAllDigits(words[wi + 1]))
			{
				digits = words[wi + 1];
				wi++;
			}
			if (result.Length > 0)
			{
				result.Append(' ');
			}
			if (isAcronym)
			{
				result.Append(w.ToUpperInvariant());
				if (digits != null)
				{
					result.Append(digits);
				}
			}
			else
			{
				result.Append(char.ToUpper(w[0]));
				if (w.Length > 1)
				{
					result.Append(w.Substring(1).ToLowerInvariant());
				}
				if (digits != null)
				{
					result.Append(' ').Append(digits); // 非缩写不合并（Panzer 4 保持原样）
				}
			}
		}
		return result.ToString();
	}

	/// <summary>v1.5.17：常见缩写白名单（整词大写还原）。**判据必须是白名单而不是"整词全大写"**——
	/// 后者会把 MAX_COUNT / DEBUG_MODE 这类 SCREAMING_SNAKE_CASE 键显示成全大写，反而不像标签。
	/// 单字母（M / T / S 等）也算：它们几乎总是武器/车型编号的前缀（M + 1 → M1）。</summary>
	private static readonly System.Collections.Generic.HashSet<string> KnownAcronyms =
		new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase)
	{
		"AI", "UI", "UX", "HUD", "FOV", "FPS", "TPS", "RTS", "POV", "ID", "HP", "MP", "XP",
		"AP", "HE", "AT", "AA", "AAA", "MG", "SMG", "LMG", "HMG", "IFF", "NVG", "IR", "KIA", "WIA",
		"DLC", "WW", "WWI", "WWII", "TNT", "RPG", "APC", "IFV", "ATGM", "SPG", "LZ", "NCO", "FPS",
		"M", "T", "S", "P", "K", "G"
	};

	private static bool IsKnownAcronym(string w)
	{
		return !string.IsNullOrEmpty(w) && KnownAcronyms.Contains(w);
	}

	private static bool IsAllDigits(string s)
	{
		if (string.IsNullOrEmpty(s))
		{
			return false;
		}
		for (int i = 0; i < s.Length; i++)
		{
			if (!char.IsDigit(s[i]))
			{
				return false;
			}
		}
		return true;
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
	/// v1.1.5：进入时滚动回顶；页内展开/收起重建（ToggleMod 等直调 OpenMyPage）不回顶。
	/// v1.5.15：**ClickSound 挪到 OpenMyPage 之后**——玩家对照实测"原生页间翻页有声、进 MODS
	/// 无声"，而 probe 证明 ClickSound 调用成功无异常 → 唯一说得通的是：声音在 OpenMyPage
	/// 销毁/重建整页 UI 的同一帧被连带扼杀（原生翻页"先播再切页"但页面对象不销毁所以有声）。
	/// 播音挪到页面重建完成之后，不再有可被销毁的窗口。</summary>
	internal static void EnterMyPage(SettingsGUI_V2 s)
	{
		OpenMyPage(s);
		ResetScrollTop(s.contentPage);
		ScheduleClick();
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
		// v1.5.6：建页后把布局彻底落定再交还给每帧巡检。否则当帧容器的 rect 仍停在初始 100，
		// 巡检的"布局死"判定会立刻触发一次重建 —— 这正是玩家看到的白闪。
		try
		{
			Canvas.ForceUpdateCanvases();
			Transform ccont = (s.contentPage != null) ? s.contentPage : null;
			if (ccont != null)
			{
				for (int i = ccont.childCount - 1; i >= 0; i--)
				{
					Transform child = ccont.GetChild(i);
					if (child != null && child.name == "MM_Container")
					{
						RectTransform crt2 = child.GetComponent<RectTransform>();
						if (crt2 != null)
						{
							UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(crt2);
						}
						break;
					}
				}
			}
		}
		catch
		{
		}
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
		// v1.7.6：**整页淡入 + 翻页窗口探针**——"翻页闪烁"就发生在这条路径上（它没有展开路径那套保护）。
		// 放在最后：此时布局与高度都已落定，淡入盖住的是"交还给每帧巡检"之后的那一两拍。
		try
		{
			Transform mmc = null;
			if (s.contentPage != null)
			{
				for (int i = s.contentPage.childCount - 1; i >= 0; i--)
				{
					Transform child = s.contentPage.GetChild(i);
					if (child != null && child.name == "MM_Container")
					{
						mmc = child;
						break;
					}
				}
			}
			if (mmc != null)
			{
				// v1.7.6：翻页路径同样确认命中区颜色——白块类故障在"停用→再启用"的路径上会复发，
				// 而进页正好是一次大批行的启用。只读；异常才写回并打故障级告警。
				try
				{
					List<GameObject> pageRows = new List<GameObject>();
					for (int i = 0; i < mmc.childCount; i++)
					{
						Transform ch = mmc.GetChild(i);
						if (ch != null && ch.gameObject != null)
						{
							pageRows.Add(ch.gameObject);
						}
					}
					ReassertRowHitColors(pageRows);
				}
				catch
				{
				}
				RevealWholePage(mmc);
				BeginPageFlashProbe(mmc);
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
	/// 参数也不会复发。
	/// v1.5.15：ClickSound 挪到切换之后（与 EnterMyPage 同理）。</summary>
	internal static void WrapToFirstPage(SettingsGUI_V2 s)
	{
		SettingsGUI_V2.currentOpenedMenu = 0;
		CallUpdateOpenedMenu(s, true, false);
		ScheduleClick();
	}

	/// <summary>v1.5.8：从 MODS 页左翻回到原生最后一页（myIndex-1）。
	/// 与 WrapToFirstPage 同一套机制：**用容错的反射调用原生 UpdateOpenedMenu**（游戏 2026-09-12
	/// 更新改过签名，直接写调用会 MissingMethodException）。离开前先还原我们接管期间改过的
	/// content 锚点与高度（v1.2.1 教训：不还原会把坏高度泄漏进原生页）。
	/// v1.5.15：ClickSound 挪到切换之后（与 EnterMyPage 同理——播音不能早于会重建 UI 的动作）。</summary>
	internal static void LeaveModsBackward(SettingsGUI_V2 s)
	{
		if (s == null || myIndex <= 0)
		{
			return;
		}
		RestoreScrollAnchors();
		SettingsGUI_V2.currentOpenedMenu = myIndex - 1;
		CallUpdateOpenedMenu(s, true, false);
		ScheduleClick();
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
					child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); // v1.7.6：Destroy 帧末才生效，先停渲染（否则污染行会被画一帧 = 翻页闪一下）
				}
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager clear error: " + ex.Message));
		}
	}

	/// <summary>展开的 mod（默认全折叠，点击标题切换）。
	/// **v1.7.1：只在本次运行内有效，不跨启动恢复**——玩家反馈"进游戏后某个 mod 是展开的"
	/// （那是上一会话展开过被记住的结果）。进游戏时列表应统一折叠；分区状态另存（见下方）。</summary>
	internal static readonly HashSet<string> expandedMods = new HashSet<string>();

	/// <summary>展开的配置分区（**默认收起**：多分区 mod 展开后先看到分区标题列表，逐个点开；
	/// 标题带 ▸/▾ 箭头显示状态）。v1.5.22~v1.5.24 曾短暂反转为"记被收起的分区、其余默认展开"，
	/// 用来绕开"点开 mod 只看到几行分区标题"的现象——但那个现象的真因是**展开记忆被关掉**
	/// （v1.5.20），记忆恢复之后默认收起不再有问题（展开过的分区会被记住），故 v1.5.25 依玩家
	/// 选择还原默认收起。</summary>
	internal static readonly HashSet<string> expandedSections = new HashSet<string>();

	/// <summary>v1.7.0：收藏的 mod（每项 = mod 全名）。页首收藏区按此填充；点条目即展开该 mod 并滚到它。</summary>
	private static readonly List<string> favorites = new List<string>();

	/// <summary>收藏区槽位数。**固定槽、一次建好、只改文本与显隐**——本项目不做事后插行
	///（lazyBuild 三次翻车都源于运行时插行）。</summary>
	private const int FavSlots = 24;

	private static readonly GameObject[] favRows = new GameObject[FavSlots];

	private static readonly Text[] favLabels = new Text[FavSlots];

	private static GameObject favHeaderRow;

	/// <summary>v1.5.17：展开状态的持久化文件。**刻意不放 cfg**——状态是"一长串 mod 全名/分区键"，
	/// 出现在设置页的文本框里既难看又可能被误编辑；单独一个小文件干净且不污染 UI。</summary>
	private static string ExpandedStatePath()
	{
		return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "er2.modmanager.expanded.txt");
	}

	/// <summary>v1.5.17：启动时恢复展开状态（`Ui / rememberExpanded` 关闭时不读）。
	/// 每行一条：`m\t&lt;mod 全名&gt;` 或 `s\t&lt;mod 全名|分区&gt;`。读取失败只影响"记不住"，不阻断任何流程。</summary>
	internal static void LoadExpandedState()
	{
		try
		{
			if (Plugin.rememberExpanded == null || !Plugin.rememberExpanded.Value)
			{
				return;
			}
			string path = ExpandedStatePath();
			if (!System.IO.File.Exists(path))
			{
				return;
			}
			int mods = 0;
			int secs = 0;
			foreach (string raw in System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8))
			{
				string line = raw;
				if (string.IsNullOrEmpty(line) || line.Length < 3)
				{
					continue;
				}
				if (line.StartsWith("m\t"))
				{
					// v1.7.1：**不再恢复 mod 的展开态**（只恢复分区）。
					// 玩家反馈"进游戏后某个 mod 是展开的"——那是上一个会话里展开过、被记下来的结果。
					// 进游戏时列表应统一是折叠的（一个展开的 mod 会把下面所有 mod 推下去），而分区的
					// 状态仍然值得记住（否则每个 mod 都要重新点开分组）。旧文件里的 `m\t` 行直接忽略，
					// 下次分区变动写盘时会被整文件重写清掉。
					mods++;
				}
				else if (line.StartsWith("s\t"))
				{
					if (expandedSections.Add(line.Substring(2)))
					{
						secs++;
					}
				}
				// v1.5.25：v1.5.22~24 写下的 `c\t`（"手动收起的分区"）在新语义下等于默认行为，忽略即可
				//（玩家的那几条记录不会被沿用，但结果与他的意图一致——那些分区就是收起的）。
			}
			Plugin.ModLog.LogInfo((object)("ModManager: 分区展开状态已恢复（分区=" + secs + "；mod 一律折叠"
				+ (mods > 0 ? "，忽略旧文件里 " + mods + " 条 mod 展开记录" : "") + "）。"));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("ModManager: 展开状态读取失败（不影响功能，本次不记忆）: " + ex.Message);
		}
	}

	/// <summary>v1.5.17：每次展开/折叠后写回（文件很小，点击频率低，直接同步写盘）。</summary>
	internal static void SaveExpandedState()
	{
		try
		{
			if (Plugin.rememberExpanded == null || !Plugin.rememberExpanded.Value)
			{
				return;
			}
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			// v1.7.1：只写分区（`s\t`）。mod 的展开态不跨启动（见 LoadExpandedState 注释），
			// 所以这里不再写 `m\t`——旧文件里的 `m\t` 会在本次写盘时被整文件覆盖清掉。
			foreach (string s in expandedSections)
			{
				if (!string.IsNullOrEmpty(s))
				{
					sb.Append("s\t").Append(s.Replace('\n', ' ')).Append('\n');
				}
			}
			System.IO.File.WriteAllText(ExpandedStatePath(), sb.ToString(), new System.Text.UTF8Encoding(false));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("ModManager: 展开状态保存失败: " + ex.Message);
		}
	}

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

	/// <summary>v1.3.1：点击音效（原生设置项点击都有 ClickSound；我们自建的控件以前没声）。
	/// v1.5.14：catch 里加日志——玩家报"翻页到 MODS 页没音效"，而所有翻页分支都调了 ClickSound
	/// 且 trace 带 [sound] 标记；若 SoundManager.ClickSound() 本身在抛异常，空 catch 会把它静默吞掉，
	/// 永远查不到线索。现在失败必留痕。
	/// v1.5.15/16：**播音时序**——v1.5.15 把播音挪到 OpenMyPage 之后仍无声（探针零异常），且玩家
	/// 报"翻到 MODS 页会**卡一下**"（FillContent 上千对象的整页重建）。结论：重建那一帧音频系统
	/// 正被 UI 大改冲击，紧跟着的 ClickSound 无效。v1.5.16 改为**延迟播音**（0.25s，经 PollControls
	/// 每帧轮询触发）——页面重建尘埃落定后再播。原生页间翻页有声（对照组）证明 ClickSound 本身
	/// 在设置菜单里是有效的，问题只在时机。</summary>
	internal static void PlayClick()
	{
		try
		{
			SoundManager.ClickSound();
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("ModManager: ClickSound failed: " + ex.GetType().Name + " " + ex.Message);
		}
	}

	/// <summary>v1.5.16：延迟播音队列（翻页类动作用——页面重建会冲击音频，立即播无效）。</summary>
	private static float scheduledClickAt = -1f;

	/// <summary>翻页类延迟播音：0.25s 后由 PollControls 补播（UI 已重建完成）。</summary>
	internal static void ScheduleClick()
	{
		scheduledClickAt = Time.unscaledTime + 0.25f;
	}

	private static void PollScheduledClick()
	{
		if (scheduledClickAt < 0f)
		{
			return;
		}
		if (Time.unscaledTime < scheduledClickAt)
		{
			return;
		}
		scheduledClickAt = -1f;
		PlayClick();
	}

	/// <summary>v1.5.4：翻页音效兜底 + 分支诊断。
	/// 原生 SettingsTabRight/TabLeft 自身会播点击音效；但第三方 mod 的 Prefix 返回 false 会把
	/// 原生一起吞掉，而它自己又不播（ACM 的假页代码全文无 SoundManager 调用）→ 整条翻页链静音。
	/// 规则：**凡是"这次原生不会执行"的分支，都由我们补一声**。会在原生执行的分支绝不能补，
	/// 否则和原生音效叠成双击声。</summary>
	internal static void TabSound(string where)
	{
		PlayClick();
		TabTrace(where + " [sound]");
	}

	/// <summary>v1.5.4：翻页分支追踪（把走的哪条分支打进日志，实测时不用猜）。</summary>
	internal static void TabTrace(string where)
	{
		try
		{
			string tp = "none";
			if (ThirdPartyPage.Present)
			{
				tp = ThirdPartyPage.IsOpen ? ("open/" + ThirdPartyPage.FakePage) : "closed";
			}
			Plugin.ModLog.LogInfo((object)("ModManager: tab " + where + " cur=" + SettingsGUI_V2.currentOpenedMenu
				+ " myIndex=" + myIndex + " thirdParty=" + tp + " f=" + Time.frameCount));
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
			// v1.5.13：**ColorBlock 只是配置，不会立即应用到渲染**。AddComponent<Button>() 那一刻
			// OnEnable 用的是【默认】ColorBlock（normal = 不透明白）把 CanvasRenderer 染成白色，
			// 而我们的透明 normal 要等第一次状态变化（鼠标划过）才被应用 → 进 MODS 页/整页重建的
			// 头两帧，每个 HitArea（全行宽白色 Image）同时显形 = 玩家一直报的"点击后闪白"
			//（录屏逐帧实测：平均亮度 125 → 344，持续 2 帧 ≈ 67ms，白色块正好是行形状）。
			// 这里配置完立即把 normal 色画上，创建那一刻就是透明的。
			UnityEngine.UI.Image im = btn.targetGraphic as UnityEngine.UI.Image;
			if (im != null && im.canvasRenderer != null)
			{
				im.canvasRenderer.SetColor(cb.normalColor);
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.7.2：小按钮的悬停 / 按下反馈。这类按钮（★ 收藏、还原默认、重置全部、复制全部…）
	/// 此前一律 `Transition.None`——v1.3.1 为避开"创建瞬间被默认配色染成白块"而干脆关掉了过渡，
	/// 代价是鼠标划过毫无反应（玩家："鼠标放上去没有任何互动效果"）。现在改用 ColorTint，但
	/// **normal 保持纯白**（乘 1 = 文本/图片自身颜色不变，创建当刻也不会变白），只有悬停/按下才变亮：
	/// 文字自身颜色偏暗，乘一个 >1 的值即明显提亮；按钮底色是暗灰，乘 1.6 也会明显变浅。</summary>
	private static void ApplyButtonHoverTint(UnityEngine.UI.Button btn)
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
			cb.normalColor = Color.white;
			cb.highlightedColor = new Color(1.6f, 1.5f, 1.2f, 1f);
			cb.pressedColor = new Color(0.85f, 0.78f, 0.6f, 1f);
			cb.selectedColor = Color.white;
			cb.disabledColor = new Color(0.5f, 0.5f, 0.5f, 0.6f);
			btn.colors = cb;
			// 与 ApplyRowHoverTint 同理：配置完立即把 normal 画上，创建那一刻就是原本的颜色
			UnityEngine.UI.Graphic g = btn.targetGraphic;
			if (g != null && g.canvasRenderer != null)
			{
				g.canvasRenderer.SetColor(cb.normalColor);
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.7.2：★ 收藏按钮的字形与配色——收藏 = 实心金星，未收藏 = 空心暗星。
	/// 原先两者同色，收藏状态在列表里几乎看不出来。</summary>
	internal static void SetFavStarVisual(Text btn, bool on)
	{
		try
		{
			if (btn == null || btn.Equals(null))
			{
				return;
			}
			btn.text = on ? "★" : "☆";
			btn.color = on ? new Color(0.95f, 0.78f, 0.30f, 1f) : new Color(0.62f, 0.64f, 0.7f, 0.9f);
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
			// v1.6.0：记下行本体——搜索过滤要按条目精确控制行的显隐（此前只记了行容器）。
			if (extras != null)
			{
				extras.row = row;
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
			if (extras != null)
			{
				// v1.5.5：热键行的值显示在"改键按钮"上，而热键控件没有 watch（值靠 StartKeyCapture
				// 写）→ 记住这个 Text，单项还原时才有地方同步显示。
				try
				{
					Transform vb = row.transform.Find("ValueBtn");
					if (vb != null)
					{
						extras.valueButton = vb.GetComponent<Text>();
					}
				}
				catch
				{
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

		/// <summary>v1.6.0：本项对应的主行。</summary>
		internal GameObject row;

		internal Text arrow;
		internal bool expanded;

		/// <summary>v1.5.5：所属配置（用于判定该项是否已被改动、以及还原默认值）。</summary>
		internal ConfigFile cfg;

		internal ConfigEntryBase entry;

		/// <summary>v1.5.5：不含改动标记的标签原文（含 ▸/▾ 前缀）。标记是拼上去的，
		/// 刷新时要用它重拼，不能从当前 text 推回来。</summary>
		internal string baseLabel;

		/// <summary>上次计算出的"已改动"状态（只有翻转时才改 UI，避免数值拖动刷屏）。</summary>
		internal bool dirty;

		/// <summary>v1.5.5：单项还原行（展开且该项已改动时才显示）。行本体**始终建好**，
		/// 只切 SetActive —— 与本项目"一次建好、点击只显隐"的约定一致，动态插行会漏登记</summary>
		internal GameObject restoreRow;

		/// <summary>改键按钮上的值文字（热键类控件没有 watch，还原后要手动同步显示）。</summary>
		internal UnityEngine.UI.Text valueButton;

		/// <summary>v1.5.0：逐级可见性所需的归属（mod → 分区 → 单项）。</summary>
		internal ModBody ownerMod;

		internal SectionBody ownerSection;

		/// <summary>该项当前是否在三层（mod / 分区 / 单项）都应当可见。</summary>
		internal bool VisibleNow()
		{
			return expanded
				&& (ownerSection == null || ownerSection.expanded)
				&& (ownerMod == null || ownerMod.expanded);
		}
	}

	private static readonly List<EntryExtras> entryExtras = new List<EntryExtras>();

	/// <summary>v1.5.5：该项现值是否不等于默认值（**含未保存的暂存值**——用 GetStaged 取"用户看到的值"，
	/// 因为 ApplyAndSave 之前 entry.BoxedValue 还没变）。</summary>
	private static bool IsDirty(ConfigFile cfg, ConfigEntryBase entry)
	{
		try
		{
			object cur = GetStaged(cfg, entry, entry.BoxedValue);
			object def = entry.DefaultValue;
			return !object.Equals(cur, def);
		}
		catch
		{
			return false;
		}
	}

	/// <summary>v1.5.5：按 [cfg, 键] 找到对应的行 extras（同一键在一页里只出现一次）。</summary>
	private static EntryExtras FindExtras(ConfigFile cfg, ConfigEntryBase entry)
	{
		string key = EntryKey(cfg, entry);
		for (int i = 0; i < entryExtras.Count; i++)
		{
			EntryExtras ex = entryExtras[i];
			if (ex != null && ex.key == key)
			{
				return ex;
			}
		}
		return null;
	}

	/// <summary>v1.5.5：重算并刷新某一项的"已改动"外观（标签尾部标记 + 还原行显隐）。
	/// 调用频次刻意压到最低：**只在 dirty 真正翻转时**才动 UI（数值拖动会每帧 StageValue，
	/// 但状态一旦为真就一直是真，不会重复布局）。</summary>
	private static void RefreshEntryDirtyUi(ConfigFile cfg, ConfigEntryBase entry)
	{
		try
		{
			EntryExtras ex = FindExtras(cfg, entry);
			if (ex == null)
			{
				return;
			}
			bool now = IsDirty(ex.cfg, ex.entry);
			if (now == ex.dirty)
			{
				return;
			}
			ex.dirty = now;
			ApplyEntryDirtyUi(ex);
			if (Plugin.DebugOn)
			{
				Plugin.ModLog.LogInfo((object)("ModManager: dirty " + (now ? "set" : "cleared") + " for " + ex.key));
			}
		}
		catch (Exception ex2)
		{
			Plugin.ModLog.LogError((object)("ModManager dirty refresh error: " + ex2.Message));
		}
	}

	/// <summary>把 ex.dirty 的当前值画到界面上（不改 dirty 本身）。</summary>
	private static void ApplyEntryDirtyUi(EntryExtras ex)
	{
		try
		{
			if (ex == null)
			{
				return;
			}
			if (ex.arrow != null && !ex.arrow.Equals(null) && !string.IsNullOrEmpty(ex.baseLabel))
			{
				FitRowLabel(ex.arrow, ex.baseLabel, ex.dirty ? DirtySuffix : null);
			}
			if (ex.restoreRow != null)
			{
				bool visible = ex.dirty && ex.VisibleNow();
				if (ex.restoreRow.activeSelf != visible)
				{
					ex.restoreRow.SetActive(visible);
					RelayoutRowsOf(ex);
				}
			}
		}
		catch (Exception e)
		{
			Plugin.ModLog.LogError((object)("ModManager dirty apply error: " + e.Message));
		}
	}

	/// <summary>某项的二级行发生增减时重排容器并重算滚动高度（不重建页面 → 不闪）。</summary>
	private static void RelayoutRowsOf(EntryExtras ex)
	{
		try
		{
			Transform c = (ex != null) ? ex.container : null;
			if (c == null)
			{
				return;
			}
			UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(c as RectTransform);
			SelfHealScroll(c, true);
		}
		catch
		{
		}
	}

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
				// v1.7.6：子选项展开/收起也给同一套淡入（玩家："给子选项也加上"）。
				// 简介行 + 还原行的显隐就是一次 SetActive + 重排；漏一拍同样是"闪一下"。
				List<GameObject> exRows = new List<GameObject>();
				if (ex.descRow != null)
				{
					exRows.Add(ex.descRow);
				}
				if (ex.restoreRow != null)
				{
					exRows.Add(ex.restoreRow);
				}
				if (now)
				{
					BeginRevealRows("entry", exRows);
				}
				else
				{
					FinishRevealForRows(exRows);
				}
				LogToggle("entry", key, now, (ex.descRow != null ? 1 : 0));
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
			if (entry.SettingType != typeof(float) && entry.SettingType != typeof(int) && Plugin.DebugOn)
			{
				Plugin.ModLog.LogInfo((object)("ModManager staged: " + entry.Definition.Key + " = " + v));
			}
			// v1.5.5：StageValue 是所有控件写值的唯一入口（含按键捕获/下拉/开关/滑条/输入框/
			// 单项还原），所以"是否改动过"的标记在这里刷一次即可覆盖全部路径。
			// 内部会在状态没翻转时直接返回，滑条连续拖动不会重复刷新布局。
			RefreshEntryDirtyUi(cfg, entry);
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
			// v1.5.5：落盘**之前**统计这批改动里"要重启才生效"的项（ApplyAndSave 会清空 staged，
			// 顺序反了就查不到了）。玩家最常见的困惑是"我改了但没反应"——那些 mod 只在 Load 读一次 cfg。
			int restartCount = 0;
			List<string> restartNames = new List<string>();
			foreach (KeyValuePair<ConfigFile, Dictionary<string, object>> kv in staged)
			{
				ConfigFile cfg = kv.Key;
				if (cfg == null || kv.Value == null)
				{
					continue;
				}
				foreach (ConfigEntryBase e in cfg.Values)
				{
					if (e == null || !kv.Value.ContainsKey(e.Definition.Key))
					{
						continue;
					}
					if (RequiresRestart(e))
					{
						restartCount++;
						if (restartNames.Count < 4)
						{
							restartNames.Add(HumanizeKey(e.Definition.Key));
						}
					}
				}
			}
			foreach (ConfigFile cfg in new List<ConfigFile>(staged.Keys))
			{
				ApplyAndSave(cfg);
			}
			Plugin.ModLog.LogInfo((object)("ModManager: auto-saved staged changes for " + n + " config(s)."));
			if (restartCount > 0)
			{
				NotifyRestartNeeded(restartCount, restartNames);
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager flush error: " + ex.Message));
		}
	}

	/// <summary>v1.5.5：改动已保存、但其中一部分要重开游戏才生效 → 用原生 Hint 说一次，
	/// 并把具体是哪几项写进日志（事后排查"改了没反应"时能一眼定位）。</summary>
	private static void NotifyRestartNeeded(int count, List<string> names)
	{
		try
		{
			string tail = "";
			for (int i = 0; i < names.Count; i++)
			{
				tail += ((i == 0) ? "" : ", ") + names[i];
			}
			if (count > names.Count)
			{
				tail += ", …";
			}
			Plugin.ModLog.LogInfo((object)("ModManager: saved, " + count + " change(s) require a game restart to take effect: " + tail));
			try
			{
				Corvostudio.UI.Hint hint = Corvostudio.UI.Hint.instance;
				if (hint != null)
				{
					Corvostudio.UI.Hint.Display(
						Plugin.DefaultChinese
							? ("已保存 · " + count + " 项改动需重启游戏后生效")
							: ("Saved · " + count + " change(s) need a restart"),
						3.5f, true, true);
				}
			}
			catch
			{
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager restart-notify error: " + ex.Message));
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
		PollScheduledClick();
		PollArmedResets();
		PollPendingRelayout();
		PollLazyVerify();
		PollReveal();      // v1.7.5：推进"展开淡入"
		PollFlashProbe();  // v1.7.5：展开窗口闪烁探针采样
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
		float y = 0f;
		float t0 = Time.realtimeSinceStartup; // v1.5.9：建页耗时（见 LogBuildCost）
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
		// v1.7.3：延迟建页的待自检队列同样作废——整页重建后旧 body 已随页面销毁，
		// 若留着它，自检会拿到"container 已销毁"从而**误判失败**（进而误弃用延迟建页）。
		lazyPendingVerify.Clear();
		lazyVerifyAt = 0f;
		// v1.7.5：展开淡入/闪烁探针的进行态同样作废（旧行已随整页销毁）
		ClearReveals();
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
		// v1.5.10：新容器 = 行内缩还没应用过，把"上次写入目标"清掉，让 ApplyScrollbarInset 首次必写
		lastInsetW = float.NaN;
		lastInsetP = float.NaN;
		// v1.6.0：搜索框放最顶部（先于热键提示）——mod 装多了以后，按名字或设置项找东西靠它
		// v1.7.0：收藏区（固定槽位、一次建好）——放在页面最顶部
		float tPhase = Time.realtimeSinceStartup;
		AddFavArea(container);
		lastFavAreaMs = (Time.realtimeSinceStartup - tPhase) * 1000f;
		// 热键冲突提示（顶部）——v1.7.8：块头（计数 + 隐藏/显示按钮）+ 明细行（可收起）
		tPhase = Time.realtimeSinceStartup;
		AddHotkeyWarningBlock(container, DetectHotkeyConflicts(plugins));
		lastWarnMs = (Time.realtimeSinceStartup - tPhase) * 1000f;
		lastBodiesMs = 0f;
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
				Text titleFav = null;
				Text titleTxt = AddSectionButton(SettingsGUI_V2.instance, container, p.shortName, expanded, name, out titleFav);
				// v1.5.12：**回退 v1.5.9 的延迟构建**，恢复"建页时一次建好、点击只原地显隐"。
				// 回退原因（玩家实测）：延迟构建引入"点开没反应"——日志证明回调到达、rows 非空
				// （`toggle mod 'X' -> open rows=118`）、无异常，但内容就是看不见；这条路径的风险
				// 明显高于它带来的翻页性能收益。性能优化等"闪"的问题定案后另做，先保证功能正确。
				ModBody body = new ModBody();
				body.name = name;
				body.title = titleTxt;
				body.expanded = expanded;
				body.plugin = p;
				body.container = container;
				// v1.5.24：**用标题文本的父级拿标题行**，不要用"容器的最后一个子物体"来猜。
				// 后者依赖"这一刻刚建的标题行就是容器末位"这个隐含假设，一旦不成立（或引用在
				// 整页重建/原生填充清理后失效），`titleRow` 就会指向别处甚至变成已销毁对象，
				// 于是 BuildModBody 里的兜底逻辑把插入点落到**容器末尾** → 新行被追加到列表最后、
				// 而不是标题下方（实测 `insertAt` 恒等于"展开前的 childCount"、`activeSpan` 落在列表
				// 底部 1955~3054 而可见区只有 0~642 = 玩家报的"点开没反应"）。
				Transform titleRowT = (titleTxt != null) ? titleTxt.transform.parent : null;
				body.titleRow = (titleRowT != null) ? titleRowT.gameObject : null;
				body.favBtn = titleFav;
				body.built = false;
				// v1.5.19：**延迟构建**（`Ui / lazyBuild`，默认关闭）。关闭时行为与 v1.5.18 完全一致
				//（建页一次建好、点击只原地显隐）；打开时折叠的 mod 只留标题行，正文首次展开时才建，
				// 进页成本从"全部条目"降到"标题 + 已展开的 mod"（本机实测 942 行 0.3~0.4 秒）。
				// 默认关闭的原因见 `Plugin.lazyBuild` 字段注释（v1.5.9 的"点开没反应"未定案）。
				// v1.7.3：`LazyBuildOn()` 还包含"本会话自检未判死"这一条。
				if (!LazyBuildOn() || expanded)
				{
					float tBody = Time.realtimeSinceStartup;
					BuildModBody(body);
					lastBodiesMs += (Time.realtimeSinceStartup - tBody) * 1000f;
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
		// v1.6.1：所有条目已建好，可以按收藏列表填充收藏区了
		RefreshFavArea();
		// v1.5.0：一次性 LAYOUT/SCROLL 诊断（v1.2.1 引入）已在发布前移除
		LogBuildCost(t0, container);
		return y;
	}

	/// <summary>v1.5.9：建页成本。**不受 debugLog 限制**（玩家反馈"翻页按了没反应"，这是性能证据，
	/// 必须可见），5 秒节流一条。</summary>
	private static float lastBuildCostLog;

	private static void LogBuildCost(float t0, Transform container)
	{
		try
		{
			if (Time.unscaledTime - lastBuildCostLog < 5f)
			{
				return;
			}
			lastBuildCostLog = Time.unscaledTime;
			int rows = (container != null) ? container.childCount : -1;
			int built = 0;
			for (int i = 0; i < modBodies.Count; i++)
			{
				if (modBodies[i] != null && modBodies[i].built)
				{
					built++;
				}
			}
			float ms = (Time.realtimeSinceStartup - t0) * 1000f;
			// v1.7.3：分阶段耗时——"进设置页顿一下"要能一眼看出时间花在哪，而不是只看一个总数。
			// bodies = 各 mod 正文（含控件克隆）+ 页脚；fav = 收藏区固定槽位；warn = 热键冲突提示。
			float other = ms - lastBodiesMs - lastFavAreaMs - lastWarnMs;
			Plugin.ModLog.LogInfo((object)("ModManager: page built in " + ms.ToString("F1")
				+ "ms - rows=" + rows + " mods=" + modBodies.Count
				+ " bodiesBuilt=" + built + "/" + modBodies.Count
				+ " lazy=" + (LazyBuildOn() ? "on" : "off")
				+ " | bodies=" + lastBodiesMs.ToString("F0") + "ms fav=" + lastFavAreaMs.ToString("F0")
				+ "ms warn=" + lastWarnMs.ToString("F0") + "ms other=" + other.ToString("F0")
				+ "ms | watches=" + watches.Count + " extras=" + entryExtras.Count
				+ " f=" + Time.frameCount));
		}
		catch
		{
		}
	}

	/// <summary>v1.5.6：整页重建会让整页新行在同一帧重排（= 玩家看到的白闪），所以每一次重建
	/// 都当作故障级事件记下来，并带上触发原因与几何证据。**不受 debugLog 限制**（这是必须被
	/// 看见的故障），同一原因 5 秒节流一条，避免刷屏。</summary>
	private static readonly Dictionary<string, float> lastRebuildLog = new Dictionary<string, float>();

	internal static void LogRebuild(string reason)
	{
		try
		{
			string why = string.IsNullOrEmpty(reason) ? "unknown" : reason;
			float last;
			if (lastRebuildLog.TryGetValue(why, out last) && Time.unscaledTime - last < 5f)
			{
				return;
			}
			lastRebuildLog[why] = Time.unscaledTime;
			Plugin.ModLog.LogWarning((object)("ModManager: rebuilding MODS page (visible flash) reason=" + why
				+ " burst=" + rebuildBurst + " cur=" + SettingsGUI_V2.currentOpenedMenu + " myIndex=" + myIndex));
		}
		catch
		{
		}
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
			// v1.5.10：判据改为与**我们上次写入的目标值**比较，不再与当前实际值比较。
			// 旧判据 `|crt.sizeDelta.x + w| > 0.5` 有个致命问题：容器的尺寸由父级布局（contentPage
			// 的 LayoutGroup）控制，我们写进去的值下一帧会被它算回去 → 每帧都判定"有差异" →
			// 每帧重设并 `ForceRebuildLayoutImmediate(crt)` → **整列行每帧重排 = 选项持续闪动**。
			// 与"意图"比较就不会被别人的覆盖带着重写：目标没变就一个字都不动。
			bool changed = float.IsNaN(lastInsetW)
				|| Math.Abs(lastInsetW - w) > 0.5f
				|| Math.Abs(lastInsetP - p) > 0.5f;
			if (changed)
			{
				// v1.5.21：**只有首次（建页后几何尚未定型）才当帧强制重排**。
				// 玩家实测"选项闪烁又出现了，但概率不再是 100%"——正是这里：原生在"预留 17px /
				// 不预留"两种 Viewport 状态间来回切换（实测 right 在 8 ↔ 24 之间跳），每次切换都让
				// 目标值变化 → 写入 + `ForceRebuildLayoutImmediate` → **整列行在同一帧重排 = 闪一下**。
				// 行宽其实几乎不变（426 ↔ 425），真正需要的只是"让 LayoutGroup 知道宽度变了"，
				// 那件事 Unity 在正常的布局阶段会自己做（sizeDelta 变更会标脏父级）。
				// 所以建页时保留强制重排（否则要等一帧才对齐滚动条），之后的切换只标记脏。
				bool firstApply = float.IsNaN(lastInsetW);
				lastInsetW = w;
				lastInsetP = p;
				insetWrites++;
				crt.sizeDelta = new Vector2(-w, crt.sizeDelta.y);
				crt.anchoredPosition = new Vector2(p, crt.anchoredPosition.y);
				try
				{
					if (firstApply)
					{
						UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(crt);
					}
					else
					{
						UnityEngine.UI.LayoutRebuilder.MarkLayoutForRebuild(crt);
					}
				}
				catch
				{
				}
			}
			LogInset(rt, w, leftInset, rightInset);
		}
		catch
		{
		}
	}

	/// <summary>上次写入的行内缩目标值（见上方注释：判据要与"我们的意图"比，不与当前实际值比）。</summary>
	private static float lastInsetW = float.NaN;

	private static float lastInsetP = float.NaN;

	/// <summary>写入次数统计窗口（v1.5.10：节流日志会**丢掉频率信息**，改用计数聚合输出）。</summary>
	private static int insetWrites;

	private static float insetWindowStart;

	/// <summary>v1.5.10：行内缩写入的**频次**诊断（debugLog 门控，每 5 秒汇总一条）。
	/// 上一版是"每帧一条 + 5 秒节流"，节流恰恰把"到底每帧写了几次"这个关键证据遮住了；
	/// 现在输出窗口内的写入次数：接近帧数（≈300/5s）说明仍在抖动，1~2 次才是正常的。</summary>
	private static void LogInset(RectTransform rt, float w, float leftInset, float rightInset)
	{
		try
		{
			if (!Plugin.DebugOn)
			{
				return;
			}
			float now = Time.unscaledTime;
			if (insetWindowStart <= 0f)
			{
				insetWindowStart = now;
				return;
			}
			if (now - insetWindowStart < 5f)
			{
				return;
			}
			Plugin.ModLog.LogInfo((object)("ModManager: row inset writes=" + insetWrites
				+ " in last 5s (target left=" + leftInset.ToString("F1") + " right=" + rightInset.ToString("F1")
				+ ", content " + rt.rect.width.ToString("F0")
				+ " -> row width " + (rt.rect.width - w).ToString("F0") + ")"));
			insetWrites = 0;
			insetWindowStart = now;
		}
		catch
		{
		}
	}

	/// <summary>v1.5.7：白闪取证探针——检测"多行被摆在同一 y 上"。
	/// 白闪的物理成因只有一个：某一帧行的位置还没定型（堆在容器原点），浅色值框互相叠成一大块白。
	/// 在每次强制重排之后立刻检查这一反常状态，就能区分"重排正常"与"位置真的没算出来"。
	/// debugLog 门控 + 5 秒节流。</summary>
	private static float lastStackProbeLog;

	internal static void ProbeStackedRows(Transform cont, string where)
	{
		try
		{
			if (!Plugin.DebugOn || cont == null || Time.unscaledTime - lastStackProbeLog < 5f)
			{
				return;
			}
			Dictionary<float, int> ys = new Dictionary<float, int>();
			int visible = 0;
			for (int i = 0; i < cont.childCount; i++)
			{
				Transform ch = cont.GetChild(i);
				if (ch == null || ch.gameObject == null || !ch.gameObject.activeSelf)
				{
					continue;
				}
				RectTransform rt = ch.GetComponent<RectTransform>();
				if (rt == null)
				{
					continue;
				}
				visible++;
				float y = rt.anchoredPosition.y;
				int n = 0;
				ys.TryGetValue(y, out n);
				ys[y] = n + 1;
			}
			int maxSame = 0;
			foreach (KeyValuePair<float, int> kv in ys)
			{
				if (kv.Value > maxSame)
				{
					maxSame = kv.Value;
				}
			}
			if (maxSame >= 3 && maxSame >= visible - 2)
			{
				lastStackProbeLog = Time.unscaledTime;
				Plugin.ModLog.LogWarning((object)("ModManager: rows stacked at one y after '" + where
					+ "' - visible=" + visible + " sameY=" + maxSame
					+ " containerH=" + (cont is RectTransform ? ((RectTransform)cont).rect.height.ToString("0") : "?")
					+ " (this is what draws as a white flash)"));
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
	/// <summary>v1.5.6：内容高度剧烈跳变的诊断（debugLog 门控，5 秒节流）。</summary>
	private static float lastHealJumpLog;

	private static void LogHealJump(float prevH, float newH, bool force)
	{
		try
		{
			if (!Plugin.DebugOn || Time.unscaledTime - lastHealJumpLog < 5f)
			{
				return;
			}
			lastHealJumpLog = Time.unscaledTime;
			Plugin.ModLog.LogInfo((object)("ModManager: content height jump " + prevH.ToString("0")
				+ " -> " + newH.ToString("0") + " (force=" + force + ")"));
		}
		catch
		{
		}
	}

	/// <summary>v1.5.11：向上找到真正的 ScrollRect（content 上方未必直接挂它）。</summary>
	private static UnityEngine.UI.ScrollRect FindScrollRect(Transform from)
	{
		try
		{
			Transform cur = from;
			int guard = 0;
			while (cur != null && guard++ < 8)
			{
				UnityEngine.UI.ScrollRect sr = cur.GetComponent<UnityEngine.UI.ScrollRect>();
				if (sr != null)
				{
					return sr;
				}
				cur = cur.parent;
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>v1.5.11：记录"视口顶部距内容顶部的像素偏移"。
	/// 为什么不能直接用 verticalNormalizedPosition：它是**归一化**值，content 高度一变，同一个
	/// 归一化值对应的绝对位置随之改变 → 展开/折叠引起高度突变时，整屏内容会瞬间位移（日志里
	/// 每次点击都跟着一条 `content height jump`）。玩家说的"点击选项后闪一下"极可能就是这一次位移。
	/// 正确做法是记录**绝对偏移**并在事后按新高度换算回去。返回 -1 = 没有可锚定的滚动容器。</summary>
	private static float CaptureScrollOffset(UnityEngine.UI.ScrollRect sr)
	{
		try
		{
			if (sr == null || sr.content == null)
			{
				return -1f;
			}
			float ch = sr.content.rect.height;
			RectTransform vp = (sr.viewport != null) ? sr.viewport : sr.GetComponent<RectTransform>();
			float vh = (vp != null) ? vp.rect.height : ch;
			float max = Math.Max(0f, ch - vh);
			if (max <= 0f)
			{
				return 0f;
			}
			return (1f - sr.verticalNormalizedPosition) * max;
		}
		catch
		{
			return -1f;
		}
	}

	/// <summary>v1.5.11：按绝对偏移还原滚动位置（此时 content 高度已变，换算回新的归一化值）。</summary>
	private static void ApplyScrollOffset(UnityEngine.UI.ScrollRect sr, float offset)
	{
		try
		{
			if (sr == null || sr.content == null || offset < 0f)
			{
				return;
			}
			float ch = sr.content.rect.height;
			RectTransform vp = (sr.viewport != null) ? sr.viewport : sr.GetComponent<RectTransform>();
			float vh = (vp != null) ? vp.rect.height : ch;
			float max = Math.Max(0f, ch - vh);
			if (max <= 0f)
			{
				sr.verticalNormalizedPosition = 1f;
				return;
			}
			sr.verticalNormalizedPosition = Mathf.Clamp01(1f - Math.Min(offset, max) / max);
		}
		catch
		{
		}
	}

	/// <summary>v1.5.11：展开/折叠事件诊断（debugLog 门控，每次操作一条）。
	/// 用来区分"点击没到达回调"与"回调到了但行集合为空"——这是"点了不展开"的两个可能原因。</summary>
	private static void LogToggle(string what, string name, bool now, int rows)
	{
		try
		{
			if (!Plugin.DebugOn)
			{
				return;
			}
			Plugin.ModLog.LogInfo((object)("ModManager: toggle " + what + " '" + name + "' -> " + (now ? "open" : "closed") + " rows=" + rows));
		}
		catch
		{
		}
	}

	/// <summary>v1.5.12：展开后统计"这些行到底怎么样了"——它是区分下面三种失败的唯一手段：
	/// ① active=0            → 行确实没被激活（状态机问题）
	/// ② active&gt;0 onScreen=0 → 行被激活但在视口之外（位置/滚动问题）
	/// ③ active&gt;0 onScreen&gt;0 → 行在视口里、却看不见（渲染/遮挡/尺寸问题）
	/// 上一轮只有"rows=N"这一条日志，三种情况无法分辨，导致又白测一轮。
	/// debugLog 门控，每次展开一条。</summary>
	private static void LogToggleState(string what, string name, List<GameObject> rows)
	{
		try
		{
			if (!Plugin.DebugOn || rows == null)
			{
				return;
			}
			int active = 0;
			int onScreen = 0;
			// v1.5.22：另外把**激活行在容器内的偏移区间**也统计出来——"展开后只看到分区标题"
			// 这类问题的判据是"激活行有几行、落在哪一段"，单看行数看不出来。
			float actTop = float.MaxValue;
			float actBot = float.MinValue;
			Transform anyRow = null;
			for (int i = 0; i < rows.Count; i++)
			{
				GameObject go = rows[i];
				if (go == null)
				{
					continue;
				}
				RectTransform rt = go.GetComponent<RectTransform>();
				if (rt == null)
				{
					continue;
				}
				if (anyRow == null)
				{
					anyRow = rt;
				}
				if (go.activeSelf)
				{
					active++;
					float t = -rt.anchoredPosition.y;
					float b = t + rt.rect.height;
					if (t < actTop)
					{
						actTop = t;
					}
					if (b > actBot)
					{
						actBot = b;
					}
				}
			}
			// v1.5.22：**不再依赖 GetWorldCorners** —— 本机实测它在 IL2CPP 下 Vector3[] 出参回不来
			// （几何诊断里所有对象的 worldY 都打印成 0..0），据此算出的 onScreen 完全不可信。
			// 改在 content 的局部坐标系里算：可见区间 = [(1-vnp)*(contentH-viewportH), + viewportH]，
			// 行相对容器顶的偏移 = -anchoredPosition.y（LayoutGroup 把行锚在容器顶端，容器又顶对齐 content）。
			UnityEngine.UI.ScrollRect sr = FindScrollRect(anyRow);
			if (sr != null)
			{
				RectTransform vp = (sr.viewport != null) ? sr.viewport : sr.GetComponent<RectTransform>();
				float vpH = (vp != null) ? vp.rect.height : 0f;
				float cH = (sr.content != null) ? sr.content.rect.height : 0f;
				float off = (1f - sr.verticalNormalizedPosition) * Math.Max(0f, cH - vpH);
				float visTop = off;
				float visBot = off + vpH;
				for (int i = 0; i < rows.Count; i++)
				{
					GameObject go = rows[i];
					if (go == null || !go.activeSelf)
					{
						continue;
					}
					RectTransform rt = go.GetComponent<RectTransform>();
					if (rt == null)
					{
						continue;
					}
					float rowTop = -rt.anchoredPosition.y;
					float rowBot = rowTop + rt.rect.height;
					if (rowBot >= visTop && rowTop <= visBot)
					{
						onScreen++;
					}
				}
				Plugin.ModLog.LogInfo((object)("ModManager: state " + what + " '" + name + "' rows=" + rows.Count
					+ " active=" + active + " onScreen=" + onScreen
					+ " activeSpan=" + ((actTop == float.MaxValue) ? "?" : (actTop.ToString("0") + ".." + actBot.ToString("0")))
					+ " visible=" + visTop.ToString("0") + ".." + visBot.ToString("0")
					+ " (contentH=" + cH.ToString("0") + " vpH=" + vpH.ToString("0")
					+ " vnp=" + sr.verticalNormalizedPosition.ToString("F2") + ")"));
			}
			else
			{
				Plugin.ModLog.LogInfo((object)("ModManager: state " + what + " '" + name + "' rows=" + rows.Count
					+ " active=" + active + " (no ScrollRect found)"));
			}
		}
		catch
		{
		}
	}

	internal static void SelfHealScroll(Transform contentPage, bool force = false)
	{
		if (contentPage == null || (!force && Time.unscaledTime < nextScrollFixTime))
		{
			return;
		}
		// v1.5.9：调用点历史上一直传的是 **MM_Container 而不是 contentPage**（ToggleEntry /
		// ToggleMod / RelayoutContainer / RelayoutRowsOf 都是）。传错对象时本函数会把高度
		// `target.sizeDelta` 写到容器自己身上，而容器挂着 ContentSizeFitter → 写进去的值下一帧
		// 被 Fitter 算回真实值覆盖 → 高度来回跳（日志实测 `content height jump 1644 -> 1784`
		// 再 `1784 -> 1644`），每次跳变都让整列行重排 = 玩家看到的"选项在闪动"。
		// 在这里统一纠正（比逐个改调用点更不容易漏）：传进来是容器就自动改用它的父级 contentPage。
		if (contentPage.name == "MM_Container" && contentPage.parent != null)
		{
			contentPage = contentPage.parent;
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
				// v1.5.6 探针：整页高度的剧烈跳变会让所有行在同一帧重排（值框叠一起 = 白闪），
				// 这条日志用来判断"闪"是重建造成的还是高度/重排造成的（诊断级，受 debugLog 门控）。
				try
				{
					float prevH = target.rect.height;
					if (Math.Abs(prevH - h) >= 40f)
					{
						LogHealJump(prevH, h, force);
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
				// v1.5.7 取证：这是所有"点击后重排"路径的公共出口（ToggleMod/Section/Entry 最终都经过这里）
				ProbeStackedRows(cont, "SelfHealScroll");
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
	private static Text AddSectionButton(SettingsGUI_V2 s, Transform container, string name, bool expanded, string rawName, out Text favBtn)
	{
		favBtn = null;
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
			// v1.7.0：★ 收藏按钮（mod 标题行右侧）。收藏的是**整个 mod**——页首收藏区点一下即展开并滚到它。
			try
			{
				string favName = rawName;
				favBtn = MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
					new Vector2(-ControlRight, 0f), new Vector2(26f, 22f),
					IsModFavorite(favName) ? "★" : "☆",
					delegate { ToggleModFavorite(favName, null); });
				// v1.7.2：收藏态用**金星**——原先 ★ 与 ☆ 同色，列表里根本看不出哪些收藏了
				SetFavStarVisual(favBtn, IsModFavorite(favName));
			}
			catch
			{
			}
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

	/// <summary>v1.5.5：改动标记（该值已不等于默认值）。**必须是纯文本**——v1.5.0 已实证：
	/// 这些 Text 上富文本 &lt;color&gt;/&lt;size&gt; 不生效，会原样打印出一串标签文本。</summary>
	private const string DirtySuffix = " •";

	/// <summary>行标签按可用宽度**显式**截断（字号恒为 RowLabelFontSize）。
	/// 说明：不能只靠 `resizeTextForBestFit` —— Unity 的 Text 在 `horizontalOverflow = Overflow`
	/// 下 bestFit 不会缩（没有可缩的边界），长标签会直接压到右边值框下面（玩家截图实测）。
	/// v1.5.5：`suffix` 是加在标签末尾的标记（如改动标记），**不参与截断**——否则 Ellipsize
	/// 会把标记割掉，且标记必须始终可见。</summary>
	private static void FitRowLabel(Text txt, string text, string suffix = null)
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
			string mark = string.IsNullOrEmpty(suffix) ? "" : suffix;
			if (font == null || avail <= 40f || string.IsNullOrEmpty(text))
			{
				txt.text = text + mark;
				return;
			}
			float availBase = avail - MeasureTextWidth(mark, font, RowLabelFontSize);
			if (availBase < 40f)
			{
				availBase = 40f;
			}
			if (MeasureTextWidth(text, font, RowLabelFontSize) <= availBase)
			{
				txt.text = text + mark;
				return;
			}
			int lo = 2;
			int hi = Math.Max(2, text.Length - 1);
			int best = 2;
			while (lo <= hi)
			{
				int mid = (lo + hi) / 2;
				if (MeasureTextWidth(text.Substring(0, mid) + "…", font, RowLabelFontSize) <= availBase)
				{
					best = mid;
					lo = mid + 1;
				}
				else
				{
					hi = mid - 1;
				}
			}
			txt.text = ((best < text.Length) ? (text.Substring(0, best) + "…") : text) + mark;
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
	/// <summary>v1.5.5："重置全部"两段式的待确认状态。旧版一点就把整个 mod 的几十项打回默认并
	/// 在 0.8s 后落盘，**无法撤销**（装箱 = 重置 + 遇只有当即那一瞬间能拦）。现在点第一下只是
	/// 进入待确认，3 秒内不点第二下自动撤销，和常规"危险操作二次确认"一致。</summary>
	private sealed class ArmItem
	{
		internal UnityEngine.UI.Text text;

		internal string original;

		internal float until;
	}

	private static readonly List<ArmItem> armedResets = new List<ArmItem>();

	/// <summary>按钮当前是否处于"点第二下就执行"的待确认态。</summary>
	private static bool IsResetArmed(UnityEngine.UI.Text t)
	{
		try
		{
			if (t == null || t.Equals(null))
			{
				return false;
			}
			for (int i = 0; i < armedResets.Count; i++)
			{
				ArmItem a = armedResets[i];
				if (a != null && a.text == t && Time.unscaledTime <= a.until)
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

	private static void ArmReset(UnityEngine.UI.Text t)
	{
		try
		{
			if (t == null || t.Equals(null))
			{
				return;
			}
			DisarmReset(t);
			armedResets.Add(new ArmItem { text = t, original = t.text, until = Time.unscaledTime + 3f });
			t.text = Plugin.DefaultChinese ? "确认重置?" : "Confirm?";
			if (Plugin.DebugOn)
			{
				Plugin.ModLog.LogInfo((object)"ModManager: reset-all armed (awaiting second click, 3s).");
			}
		}
		catch
		{
		}
	}

	private static void DisarmReset(UnityEngine.UI.Text t)
	{
		try
		{
			for (int i = armedResets.Count - 1; i >= 0; i--)
			{
				ArmItem a = armedResets[i];
				if (a == null || a.text == null || a.text.Equals(null) || a.text == t)
				{
					if (a != null && a.text != null && !a.text.Equals(null))
					{
						a.text.text = a.original;
					}
					armedResets.RemoveAt(i);
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>每帧回收：待确认超时或按钮已随页面重建销毁 → 复位并出队。</summary>
	private static void PollArmedResets()
	{
		float now = Time.unscaledTime;
		for (int i = armedResets.Count - 1; i >= 0; i--)
		{
			try
			{
				ArmItem a = armedResets[i];
				if (a == null || a.text == null || a.text.Equals(null) || now >= a.until)
				{
					if (a != null && a.text != null && !a.text.Equals(null))
					{
						a.text.text = a.original;
					}
					armedResets.RemoveAt(i);
				}
			}
			catch
			{
				armedResets.RemoveAt(i);
			}
		}
	}

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
				// v1.5.5：两段式确认——第一下只进入待确认（3 秒自动撤销），第二下才真重置
				if (!IsResetArmed(resetTxt))
				{
					ArmReset(resetTxt);
					return;
				}
				DisarmReset(resetTxt);
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
		// v1.7.2：显式指定 targetGraphic（原先靠 Selectable 自动吸附），并给悬停/按下反馈——
		// 原先是 Transition.None，鼠标划过完全没有反应（玩家反馈"没有任何互动效果"）。
		bbtn.targetGraphic = bimg;
		ApplyButtonHoverTint(bbtn);
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
					object def = e.DefaultValue;
					if (def == null)
					{
						continue;
					}
					StageValue(cfg, e, def);
					ApplyValueToControls(cfg, e, def);
					n++;
				}
				catch
				{
				}
			}
			Plugin.ModLog.LogInfo((object)("ModManager: reset " + n + " settings of '" + modName + "' to defaults (pending save)."));
			// v1.5.5：不再整页重建（旧实现直接 OpenMyPage）。重建会让整页新行在同一帧重排，
			// 正是 v1.5.0 修掉的"点击闪白"；现在改为把默认值写回控件 + 重算改动标记 + 重排容器。
			Transform cont = null;
			for (int i = 0; i < entryExtras.Count; i++)
			{
				EntryExtras ex = entryExtras[i];
				if (ex == null || ex.cfg != cfg)
				{
					continue;
				}
				cont = ex.container;
				ex.dirty = IsDirty(ex.cfg, ex.entry);
				ApplyEntryDirtyUi(ex);
			}
			if (cont != null)
			{
				ReapplyInnerVisibility();
				try
				{
					UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(cont as RectTransform);
				}
				catch
				{
				}
				SelfHealScroll(cont, true);
			}
			else
			{
				// 兜底：页面上找不到这个 mod 的行（异常状态）时才回到重建路径
				SettingsGUI_V2 s = SettingsGUI_V2.instance;
				if (s != null)
				{
					OpenMyPage(s);
				}
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
			// v1.5.17：头部带上元信息（全名 / GUID / 版本 / 条目数）——报 bug 时一行即可定位版本
			string meta = BuildModMetaText(FindPluginByName(modName));
			if (!string.IsNullOrEmpty(meta))
			{
				text += "  " + meta + "\n";
			}
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

	/// <summary>v1.5.18：UI 用的**短**元信息（GUID · 版本 · 条目数）。刻意不含全名——标题行已经显示
	/// mod 名，再重复一遍只会把这行撑长（玩家："元信息行排版不好看"）。完整信息仍进"复制全部"。</summary>
	private static string BuildModMetaShort(PluginConfig p)
	{
		try
		{
			if (p == null)
			{
				return "";
			}
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			if (!string.IsNullOrEmpty(p.guid))
			{
				sb.Append(p.guid);
			}
			if (!string.IsNullOrEmpty(p.version))
			{
				if (sb.Length > 0)
				{
					sb.Append(" · ");
				}
				sb.Append("v").Append(p.version);
			}
			if (p.cfg != null)
			{
				if (sb.Length > 0)
				{
					sb.Append(" · ");
				}
				sb.Append(p.cfg.Count).Append(Plugin.DefaultChinese ? " 项设置" : " settings");
			}
			return sb.ToString();
		}
		catch
		{
			return "";
		}
	}

	/// <summary>v1.5.18：mod 元信息行——**单行、定高、不换行、超长省略号**。
	/// v1.5.17 复用了给"配置项说明"写的 `AddDescriptionRow`，而那个函数按字符数估高（最少两行、
	/// 且以 400px 估宽），于是这一行又高又宽、把下面的内容整体推下去（玩家截图反馈"排版不好看"）。
	/// 说明行可以换行，元信息行只该是一行小字。</summary>
	private static void AddMetaRow(Transform container, string text)
	{
		try
		{
			if (string.IsNullOrEmpty(text))
			{
				return;
			}
			GameObject row = new GameObject("MM_MetaRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 18f);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			float li = LabelLeft + RowIndent;
			float ri = ControlRight;
			trt.sizeDelta = new Vector2(-(li + ri), 16f);
			trt.anchoredPosition = new Vector2((li - ri) * 0.5f, 0f);
			Text txt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 12;
			txt.fontStyle = FontStyle.Normal;
			txt.color = new Color(0.55f, 0.55f, 0.61f, 0.9f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.resizeTextForBestFit = false;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.verticalOverflow = VerticalWrapMode.Truncate;
			txt.raycastTarget = false;
			// 单行截断：用**实测宽度**而不是字符数（同一套测量已在行标签截断里验证过）。
			// 元信息行没有值控件，可用宽度 = 标签列宽 + 值控件列 + 间隙。
			float avail = RowLabelAvailWidth() + ControlWidth + LabelGap;
			if (font != null && avail > 40f && MeasureTextWidth(text, font, 12) > avail)
			{
				int lo = 2;
				int hi = Math.Max(2, text.Length - 1);
				int best = 2;
				while (lo <= hi)
				{
					int mid = (lo + hi) / 2;
					if (MeasureTextWidth(text.Substring(0, mid) + "…", font, 12) <= avail)
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
			else
			{
				txt.text = text;
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.5.17：mod 元信息文本（全名 · GUID · 版本 · 条目数）。用于 mod 内容顶部的信息行与
	/// "复制全部"的头部——玩家截图求助 / 报 bug 时不必再去翻 BepInEx 日志确认是哪个 mod 的哪个版本。</summary>
	private static string BuildModMetaText(PluginConfig p)
	{
		try
		{
			if (p == null)
			{
				return "";
			}
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			if (!string.IsNullOrEmpty(p.name) && !string.Equals(p.name, p.shortName, StringComparison.Ordinal))
			{
				sb.Append(p.name);
			}
			if (!string.IsNullOrEmpty(p.guid))
			{
				if (sb.Length > 0)
				{
					sb.Append(" · ");
				}
				sb.Append(p.guid);
			}
			if (!string.IsNullOrEmpty(p.version))
			{
				if (sb.Length > 0)
				{
					sb.Append(" · ");
				}
				sb.Append("v").Append(p.version);
			}
			if (p.cfg != null)
			{
				if (sb.Length > 0)
				{
					sb.Append(" · ");
				}
				sb.Append(p.cfg.Count).Append(Plugin.DefaultChinese ? " 项设置" : " settings");
			}
			return sb.ToString();
		}
		catch
		{
			return "";
		}
	}

	/// <summary>v1.5.17：按 mod 全名找回它的 PluginConfig（复制导出要带元信息）。列表本身有缓存，开销可忽略。</summary>
	private static PluginConfig FindPluginByName(string name)
	{
		try
		{
			if (string.IsNullOrEmpty(name))
			{
				return null;
			}
			foreach (PluginConfig p in CollectPlugins())
			{
				if (p != null && string.Equals(p.name, name, StringComparison.Ordinal))
				{
					return p;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>v1.5.17：读取第三方 mod 挂的 BepInEx ConfigurationManager 属性对象
	/// （`ConfigurationManagerAttributes`，第三方 mod 通过 ConfigDescription 的 Tags 传入）。
	/// **反射读取，不引入编译期依赖**——该类型只在装了 ConfigurationManager 的 mod 里存在。
	/// 取不到 → null（一律按"没有属性"处理）。</summary>
	private static object CmAttributes(ConfigEntryBase e)
	{
		try
		{
			object[] tags = e?.Description?.Tags;
			if (tags == null)
			{
				return null;
			}
			for (int i = 0; i < tags.Length; i++)
			{
				object t = tags[i];
				if (t != null && t.GetType().Name == "ConfigurationManagerAttributes")
				{
					return t;
				}
			}
		}
		catch
		{
		}
		return null;
	}

	/// <summary>v1.5.17：该配置项是否该显示。BepInEx ConfigurationManager 的约定：
	/// `Browsable = false` 表示"内部项，别显示给玩家"（第三方 mod 常用它藏调试项）。
	/// 属性为 null / 类型里没这个字段 → 照常显示。</summary>
	private static bool EntryBrowsable(ConfigEntryBase e)
	{
		try
		{
			object a = CmAttributes(e);
			System.Reflection.PropertyInfo pi = (a != null) ? a.GetType().GetProperty("Browsable") : null;
			object v = (pi != null) ? pi.GetValue(a) : null;
			if (v is bool b)
			{
				return b;
			}
		}
		catch
		{
		}
		return true;
	}

	/// <summary>v1.5.17：配置项的显示顺序（BepInEx ConfigurationManager 的 `Order`）。
	/// 未声明 → int.MaxValue（排在显式声明者之后）。</summary>
	private static int EntryOrder(ConfigEntryBase e)
	{
		try
		{
			object a = CmAttributes(e);
			System.Reflection.PropertyInfo pi = (a != null) ? a.GetType().GetProperty("Order") : null;
			object v = (pi != null) ? pi.GetValue(a) : null;
			if (v is int i)
			{
				return i;
			}
			if (v is System.IConvertible c)
			{
				return c.ToInt32(null);
			}
		}
		catch
		{
		}
		return int.MaxValue;
	}

	/// <summary>v1.5.19：构建一个 mod 的正文（配置项行 + 页脚行），登记进 `body.rows` 并按展开态显隐。
	/// 两条路径共用：建页时的常规构建（v1.5.18 行为）与延迟构建的"首次展开时补建"。
	/// **插入点必须现查标题行的当前下标**（v1.5.10 教训：建页时记下的静态下标会被别的 mod 展开挤位）；
	/// 先收集对象引用再搬位置（`rows` 登记对象而非下标，两件事互不干扰）。</summary>
	private static void BuildModBody(ModBody body, bool deferred = false)
	{
		try
		{
			if (body == null || body.built || body.container == null || body.plugin == null)
			{
				return;
			}
			ModBody savedMod = currentModBody;
			currentModBody = body;
			float savedIndent = RowIndent;
			int before = body.container.childCount;
			RowIndent = 10f;
			FillModEntries(body.plugin, body.container);
			AddFooterRow(body.container, body.plugin.name, body.plugin.cfg);
			RowIndent = savedIndent;
			currentModBody = savedMod;

			List<GameObject> created = new List<GameObject>();
			for (int ci = before; ci < body.container.childCount; ci++)
			{
				Transform ch = body.container.GetChild(ci);
				if (ch != null && ch.gameObject != null)
				{
					created.Add(ch.gameObject);
				}
			}
			int insertAt = body.container.childCount;
			if (body.titleRow != null)
			{
				insertAt = body.titleRow.transform.GetSiblingIndex() + 1;
				// v1.7.4：**删掉 v1.5.10~v1.5.24 的那句兜底**（`insertAt < before` → 取 `before`）。
				// 它的意图是"下标异常时别往前插"，但后果恰恰相反：新建的行本来就**追加在容器末尾**，
				// 而标题行的下标天然小于 `before` → 兜底**每次必然命中** → 行永远被留在列表末尾而不是
				// 标题下方。1.7.3 的自检当场抓到：`已构建 insertAt=71 before=71 titleRow=ok`，而自检报
				// `row[0] 位置不连续(idx=71 ≠ 28)`（28 = 标题行下标 + 1）。
				// 标题行现在用 `titleTxt.transform.parent` 取（不可漂移），不需要启发式兜底；
				// 真正的保护交给自检（PollLazyVerify：结构不连续即判失败 → 有界回退）。
				if (insertAt < 1)
				{
					insertAt = body.container.childCount; // 唯一保留的硬性防御：下标非法则退化为追加
				}
			}
			for (int k = 0; k < created.Count; k++)
			{
				created[k].transform.SetSiblingIndex(insertAt + k);
				body.rows.Add(created[k]);
				created[k].SetActive(body.expanded);
			}
			body.built = true;
			// 诊断只在实验开关打开时产出（发布默认零噪声）
			if (LazyBuildOn())
			{
				Plugin.ModLog.LogInfo("[MM-lazy] 已构建 '" + body.name + "' 正文: rows=" + created.Count
					+ " expanded=" + body.expanded + " insertAt=" + insertAt
					+ " before=" + before + " titleRow=" + ((body.titleRow == null) ? "NULL" : "ok")
					+ " containerChild=" + body.container.childCount);
				LogDeferredState(body);
			}
			// v1.7.3：**延迟构建后排队自检**（隔一帧，等两层重排都落定）。整页建页（deferred=false）
			// 不排队——那时布局本来就在后续流程里统一刷新。见 PollLazyVerify / LazyBuildProblem。
			if (deferred)
			{
				lazyPendingVerify.Add(body);
				lazyVerifyAt = Time.unscaledTime + 0.35f;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager build body error: " + ex.Message));
		}
	}

	/// <summary>v1.5.19：延迟构建的状态量快照——**把"行到底能不能看见"所需的每个量都打出来**：
	/// 容器与行的 activeInHierarchy（父级停用也算）、容器 rect 高 vs **偏好高**（差很大 = 布局没算过）、
	/// 前 3 行的 anchoredPosition/rect（都堆在同一点 = 浅色值框叠成白块的成因）、以及父链上第一个
	/// 停用节点名（v1.1.9 的 STUCK-EVIDENCE 就是"建在停用父级下"）。
	/// 只在 `Ui / lazyBuild` 打开时产出——实验路径自证。</summary>
	private static void LogDeferredState(ModBody body)
	{
		try
		{
			if (body == null || body.container == null)
			{
				return;
			}
			Transform cont = body.container;
			RectTransform crt = cont.GetComponent<RectTransform>();
			float ch2 = (crt != null) ? crt.rect.height : -1f;
			float pref = -1f;
			try
			{
				if (crt != null)
				{
					pref = UnityEngine.UI.LayoutUtility.GetPreferredHeight(crt);
				}
			}
			catch
			{
			}
			string chain = "ok";
			Transform cur = cont;
			while (cur != null)
			{
				if (!cur.gameObject.activeSelf)
				{
					chain = cur.name;
					break;
				}
				cur = cur.parent;
			}
			int inactive = 0;
			string d = "";
			for (int i = 0; i < 3 && i < body.rows.Count; i++)
			{
				GameObject r = body.rows[i];
				if (r == null)
				{
					continue;
				}
				if (!r.activeInHierarchy)
				{
					inactive++;
				}
				RectTransform rr = r.GetComponent<RectTransform>();
				if (rr != null)
				{
					d += " [" + i + "]y=" + rr.anchoredPosition.y.ToString("F0")
						+ ",h=" + rr.rect.height.ToString("F0")
						+ ",act=" + (r.activeInHierarchy ? 1 : 0);
				}
			}
			Plugin.ModLog.LogInfo("[MM-lazy] 状态 '" + body.name + "': containerActive="
				+ cont.gameObject.activeInHierarchy + " containerH=" + ch2.ToString("F0")
				+ " preferred=" + pref.ToString("F0") + " rows=" + body.rows.Count
				+ " inactiveInHierarchy=" + inactive + " firstInactiveParent=" + chain + d);
		}
		catch
		{
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
			bool anyOrder = false;
			foreach (ConfigEntryBase entry in p.cfg.Values)
			{
				if (entry == null)
				{
					continue;
				}
				// v1.5.17：尊重第三方 ConfigurationManager 的 `Browsable = false`（内部项不显示）
				if (!EntryBrowsable(entry))
				{
					continue;
				}
				if (EntryOrder(entry) != int.MaxValue)
				{
					anyOrder = true;
				}
				entries.Add(entry);
			}
			if (entries.Count == 0)
			{
				// 全部项都被作者标记为不可见 → 与"没有可调设置"同视（否则 mod 展开后空无一物）
				AddFallbackTextRow(container, Plugin.DefaultChinese ? "该 mod 没有可调设置" : "This mod has no configurable settings");
				return;
			}
			// v1.5.17：**只有至少一项声明了 Order 时才重排**；否则保持作者在 cfg 里的绑定顺序——
			// 我们自己的 mod 都是刻意排过序的，无差别按字母重排反而是倒退。
			if (anyOrder)
			{
				entries.Sort(delegate (ConfigEntryBase a, ConfigEntryBase b)
				{
					int oa = EntryOrder(a);
					int ob = EntryOrder(b);
					if (oa != ob)
					{
						return oa.CompareTo(ob);
					}
					return string.Compare(a.Definition.Key, b.Definition.Key, StringComparison.OrdinalIgnoreCase);
				});
			}
			// v1.5.17：mod 元信息行——展开 mod 后的第一行（v1.5.18：改用紧凑单行版，见 AddMetaRow）
			string metaLine = BuildModMetaShort(p);
			if (!string.IsNullOrEmpty(metaLine))
			{
				AddMetaRow(container, metaLine);
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
					// v1.5.25：分区**默认收起**（玩家选择）；展开过的记在 expandedSections 里，重开游戏会恢复。
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

		/// <summary>v1.5.19：延迟构建（`Ui / lazyBuild`）用——正文首次展开时要知道"建哪个 mod、
		/// 建在哪个容器、插在谁的后面"。v1.5.12 曾把这三个字段当脚手架删掉，如今是真实功能。</summary>
		internal PluginConfig plugin;
		internal Transform container;
		internal GameObject titleRow;
		internal bool built;

		/// <summary>v1.7.0：标题行右侧的 ★ 收藏按钮（切换后要改字形，故留引用）。</summary>
		internal Text favBtn;
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
				if (ex == null)
				{
					continue;
				}
				bool on = ex.VisibleNow();
				if (ex.descRow != null)
				{
					ex.descRow.SetActive(on);
				}
				// v1.5.5：还原行多一层条件——只有"改过了"才有意义（没改过就不占版面）
				if (ex.restoreRow != null)
				{
					ex.restoreRow.SetActive(on && ex.dirty);
				}
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
			// v1.7.1：**不落盘**——mod 的展开态只在本次运行内有效（见 LoadExpandedState 注释）。
			// 分区状态由 ToggleSection 负责保存。
			PlayClick();
			bool now = !expandedMods.Contains(name);
			for (int i = 0; i < modBodies.Count; i++)
			{
				if (modBodies[i] == null || modBodies[i].name != name)
				{
					continue;
				}
				SetModExpanded(modBodies[i], now);
				break;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager toggle error: " + ex.Message));
		}
	}

	/// <summary>v1.7.2：**唯一**的"设置某个 mod 展开态"入口（点击标题与收藏跳转共用）。
	/// 抽出它的原因：`JumpToFav` 原先只改了状态标志（`expandedMods` / `ModBody.expanded`），
	/// 没做真正的展开动作（行 SetActive、标题箭头、内部层级重算、重排）→ 出现"状态是已展开、
	/// 界面却是折叠"的不一致：玩家点标题的第一下只是把状态取消（界面毫无变化），第二下才真正展开，
	/// 被他描述为"被收藏的 mod 要点击两下才能展开"。
	/// **教训：凡"设置某个东西的状态"的操作，只允许有一个入口**——两处各写一半，必然出现半状态。</summary>
	private static void SetModExpanded(ModBody mb, bool now)
	{
		try
		{
			if (mb == null)
			{
				return;
			}
			if (now)
			{
				expandedMods.Add(mb.name);
			}
			else
			{
				expandedMods.Remove(mb.name);
			}
			mb.expanded = now;
			// v1.7.5：记下展开前的行数——延迟建页路径下"多出来的行"就是**本次新建成的那批**，
			// 它们才是需要淡入/探针的对象（已建好的行不重放动画）。
			int rowsBefore = mb.rows.Count;
			// v1.5.19：延迟构建（`Ui / lazyBuild`）——折叠的 mod 没有正文，首次展开时在这里补建，
			// 插在它自己的标题行之后（插入点现查，见 BuildModBody）。常规路径 body.built 恒为 true，无副作用。
			// v1.7.3：`deferred=true` → 建完排队自检（PollLazyVerify），失败会自动退回常规路径。
			if (now && !mb.built)
			{
				BuildModBody(mb, true);
			}
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
			// v1.7.5：展开的收尾——顺序有讲究：
			// ① 先确认命中区颜色（`CanvasRenderer` 的颜色在行被停用后会丢，见 ReassertRowHitColors）；
			// ② 再开淡入：本次新建的行从 alpha 0 起，把"建行当帧 + 延迟重排那两拍"盖住。
			// 收起则相反：**必须先立即收尾淡入**，绝不能让行带着 alpha<1 被停用
			//（否则下次展开就是"行在、看不见"——本项目最忌讳的故障形态）。
			if (now)
			{
				ReassertRowHitColors(mb.rows);
				if (mb.rows.Count > rowsBefore)
				{
					BeginReveal(mb.name, mb.rows, rowsBefore);
					BeginFlashProbe(mb.name, mb.rows, rowsBefore);
				}
			}
			else
			{
				// v1.7.6：收起 → 收尾它涉及的淡入（`FinishRevealForRows`：按行集合判定，不按名字）
				FinishRevealForRows(mb.rows);
			}
			LogToggle("mod", mb.name, now, mb.rows.Count);
			// v1.5.12：把"行到底有没有出现在视口里"记下来 —— 上一轮的教训是只有行数还不够，
			// 需要区分"激活了但不在视口"与"在视口里却看不见"。
			LogToggleState("mod", mb.name, mb.rows);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager set expanded error: " + ex.Message));
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
			// v1.5.21：**完整重建链**（原先只重建 container 一层，那是不够的）。
			// container 自己带 ContentSizeFitter（高度按 preferred 设定），同时它又是 contentPage 的
			// 子物体、位置与高度受 contentPage 那一层的布局支配。玩家实测"延迟建页下点开 mod 看不到
			// 内容"——诊断已证明行都建了、都激活了、父链干净、无异常，那问题只能在几何上：只让
			// container 内部重算，宿主那层仍按旧几何摆放，新行就落在可见范围之外。
			UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(container as RectTransform);
			Transform outer = container.parent;
			if (outer != null)
			{
				Canvas.ForceUpdateCanvases();
				UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(outer as RectTransform);
			}
			Canvas.ForceUpdateCanvases();
			SelfHealScroll(container, true);
			// ContentSizeFitter + 嵌套 LayoutGroup 常常要到下一帧才完全稳定 → 记一笔，稍后补排一次。
			LogLazyGeometry("relayout");
			MarkPendingRelayout();
		}
		catch
		{
		}
	}

	/// <summary>v1.5.21：延迟重排的时间点（见 PollPendingRelayout）。0 = 无待办。</summary>
	private static float pendingRelayoutAt;

	/// <summary>v1.7.3：**延迟建页的自检状态**。延迟建页（`Ui / lazyBuild`）历史上三次"点开没内容"
	/// 都是"行建出来了、界面看不见"，而且当时没有任何东西会**自动发现**它。现在每次延迟构建后
	/// 隔一帧自检结构（行是否紧跟标题、是否连续）与布局（宿主高度是否跟上需求高度）：
	/// 失败 → 先补排一次；**连续两次失败 → 本会话弃用延迟建页并整页重建**，回到已验证的老路径。</summary>
	private static readonly List<ModBody> lazyPendingVerify = new List<ModBody>();
	private static float lazyVerifyAt;
	private static int lazyFailStreak;

	/// <summary>v1.7.3：本会话是否已弃用延迟建页（自检连续失败后置位）。</summary>
	private static bool lazyDisabled;

	/// <summary>v1.7.3：延迟建页当前是否生效（开关打开 **且** 本会话未被自检判死）。</summary>
	private static bool LazyBuildOn()
	{
		return Plugin.lazyBuild != null && Plugin.lazyBuild.Value && !lazyDisabled;
	}

	/// <summary>v1.7.3：建页耗时的分阶段统计（供 LogBuildCost 一条日志看清"时间花在哪"）。
	/// 玩家报的是"进设置页顿一下"，所以这条**不受 debugLog 限制**、只在既有 5s 节流下输出。</summary>
	private static float lastFavAreaMs;
	private static float lastWarnMs;
	private static float lastBodiesMs;

	/// <summary>v1.5.21：安排一次"稍后的"整体重排。插入行触发的重排在**同一帧内**往往算不出最终
	/// 几何（ContentSizeFitter 要等 LayoutGroup 的 preferred 落定、宿主那层要等它自己的 driver 跑）；
	/// 隔一帧再排一次是稳定且几乎无成本的兜底。</summary>
	internal static void MarkPendingRelayout()
	{
		pendingRelayoutAt = Time.unscaledTime + 0.12f;
	}

	/// <summary>v1.5.21：每帧轮询里的延迟重排（挂在 PollControls 的既有链上）。</summary>
	private static void PollPendingRelayout()
	{
		try
		{
			if (pendingRelayoutAt <= 0f || Time.unscaledTime < pendingRelayoutAt)
			{
				return;
			}
			pendingRelayoutAt = 0f;
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s == null || s.contentPage == null)
			{
				return;
			}
			Canvas.ForceUpdateCanvases();
			UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(s.contentPage as RectTransform);
			SelfHealScroll(s.contentPage, true);
			LogLazyGeometry("deferred");
		}
		catch
		{
		}
	}

	// ========================= v1.7.5：展开淡入 + 展开窗口闪烁探针 =========================

	/// <summary>v1.7.5：一次"淡入揭示"的进行态（v1.7.6 起通用：展开 / 翻页 / 子选项 / 分区）。
	/// 两种粒度：**逐行**（`groups`；用于页面里的一个子集，例如某个 mod 的正文）与
	/// **整块**（`containerGroup`；用于整页重建——只有一个 CanvasGroup 要写，比逐行写 N 个便宜）。</summary>
	private sealed class RevealItem
	{
		internal string tag;
		internal readonly List<GameObject> rows = new List<GameObject>();
		internal readonly List<CanvasGroup> groups = new List<CanvasGroup>();

		/// <summary>整块淡入用的容器 CanvasGroup（非 null 时只写它，忽略 groups）。</summary>
		internal CanvasGroup containerGroup;

		/// <summary>存活检测基准（行/容器被销毁 → 本项作废）。</summary>
		internal GameObject probe;

		/// <summary>动画起点（= 那一帧的时间）。</summary>
		internal float startAt;

		/// <summary>完全不显示的静默时长——盖住"建行当帧"。</summary>
		internal float hold;

		/// <summary>0→1 的渐变时长。</summary>
		internal float dur;

		/// <summary>兜底：到点无论进度如何都直接显示（防任何漏拍让行永久半透明）。</summary>
		internal float deadline;

		internal float lastAlpha = -1f;
	}

	private static readonly List<RevealItem> reveals = new List<RevealItem>();

	/// <summary>v1.7.6：单次淡入的行数上限——超过它退化为"只静默一帧"（gate）。
	/// 理由：淡入每步都要动 alpha（标脏 Canvas），行数太多时"掩饰"本身就可能变成卡顿源，
	/// 那比"闪一下"更糟（本项目已多次为性能付代价）。</summary>
	private const int RevealMaxFadeRows = 300;

	private static bool RevealOn()
	{
		try
		{
			return Plugin.revealAnim == null || Plugin.revealAnim.Value;
		}
		catch
		{
			return false;
		}
	}

	private static float RevealDur()
	{
		try
		{
			if (Plugin.revealMs == null)
			{
				return 0.2f;
			}
			return Mathf.Clamp((float)Plugin.revealMs.Value, 0f, 800f) / 1000f;
		}
		catch
		{
			return 0.2f;
		}
	}

	/// <summary>v1.7.5 / v1.7.6：**通用的"淡入揭示"**——给一批行挂淡入（`hold` 秒完全不显示，
	/// 随后 `dur` 秒内 alpha 0→1）。
	/// 目的：新建/新露出的行在"建/排的当帧"就参与布局，而外观与数值要到该帧末才落定；只要有一拍
	/// 慢一帧，就会渲染出"半成品"（玩家说的"闪一下"）。让这批行从 alpha 0 淡入，把那 1~2 帧盖掉。
	/// **CanvasGroup 不参与布局** → 行照常占位，所以"先隐藏"不会让周围的行多跳一次。
	/// `blockGroup` 非空 = 整块模式（整页淡入用一个容器 CanvasGroup；比逐行写 N 个便宜）。
	/// 调用方**必须**在对应内容被收起/隐藏时收尾（`FinishRevealForRows`），否则会留下
	/// "行在、看不见"——本项目最忌讳的故障形态。</summary>
	private static void BeginRevealRows(string tag, List<GameObject> targets, CanvasGroup blockGroup = null)
	{
		try
		{
			if (!RevealOn() || targets == null)
			{
				return;
			}
			// 只对"当前真的会显示"的行动手：隐藏的行挂上也看不见，还白多一次写入。
			List<GameObject> live = new List<GameObject>();
			for (int i = 0; i < targets.Count; i++)
			{
				GameObject g = targets[i];
				if (g == null || g.Equals(null) || !g.activeInHierarchy)
				{
					continue;
				}
				live.Add(g);
			}
			if (live.Count == 0)
			{
				return;
			}
			bool gateOnly = live.Count > RevealMaxFadeRows;
			RevealItem it = new RevealItem();
			it.tag = tag;
			it.startAt = Time.unscaledTime;
			it.hold = gateOnly ? 0.02f : 0.07f;
			it.dur = gateOnly ? 0f : RevealDur();
			it.deadline = it.startAt + it.hold + it.dur + 0.8f;
			if (blockGroup != null && !blockGroup.Equals(null))
			{
				blockGroup.alpha = 0f;
				it.containerGroup = blockGroup;
				it.probe = blockGroup.gameObject;
			}
			else
			{
				for (int i = 0; i < live.Count; i++)
				{
					CanvasGroup cg = live[i].GetComponent<CanvasGroup>();
					if (cg == null || cg.Equals(null))
					{
						cg = live[i].AddComponent<CanvasGroup>();
					}
					if (cg == null || cg.Equals(null))
					{
						continue;
					}
					cg.alpha = 0f;
					it.rows.Add(live[i]);
					it.groups.Add(cg);
				}
				if (it.rows.Count == 0)
				{
					return;
				}
				it.probe = it.rows[0];
			}
			it.lastAlpha = 0f;
			reveals.Add(it);
			if (Plugin.DebugOn)
			{
				Plugin.ModLog.LogInfo((object)("[MM-reveal] " + tag + " 淡入 "
					+ (gateOnly ? ("（" + live.Count + " 行 → gate 模式，只静默一帧）") : (live.Count + " 行"))
					+ "（hold=" + it.hold.ToString("F2") + "s dur=" + it.dur.ToString("F2") + "s）"));
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.7.5：展开一个 mod 时，给"本次新建的行"（`rows[from..]`）挂淡入。</summary>
	private static void BeginReveal(string name, List<GameObject> rows, int from)
	{
		try
		{
			if (rows == null || rows.Count <= from)
			{
				return;
			}
			List<GameObject> sub = new List<GameObject>();
			for (int i = from; i < rows.Count; i++)
			{
				sub.Add(rows[i]);
			}
			BeginRevealRows(name, sub);
		}
		catch
		{
		}
	}

	/// <summary>v1.7.6：**整页淡入**——建页之后（翻页进入 / 重进设置 / 自愈重建）调用。
	/// 用容器上的**一个** CanvasGroup 做整块淡入；行数超限时自动退化成"静默一帧"。</summary>
	private static float lastPageRevealAt;

	private static void RevealWholePage(Transform container)
	{
		try
		{
			if (container == null || container.Equals(null))
			{
				return;
			}
			CanvasGroup cg = container.GetComponent<CanvasGroup>();
			if (cg == null || cg.Equals(null))
			{
				cg = container.gameObject.AddComponent<CanvasGroup>();
			}
			if (!RevealOn())
			{
				return; // 开关关闭：不建 reveal（上面加的 CanvasGroup alpha=1，无视觉影响）
			}
			// 防重复触发：`OpenMyPage` 在故障路径上可能连着被调用（自愈重建有 burst 熔断，最坏 2~3 次）——
			// 每次都重放一次整页淡入会变成"页面反复淡出"（比闪烁更糟）。已有整页淡入在进行、
			// 或刚淡入完不久，就直接跳过。
			for (int i = 0; i < reveals.Count; i++)
			{
				if (reveals[i] != null && reveals[i].tag == "page")
				{
					return;
				}
			}
			float now = Time.unscaledTime;
			if (now - lastPageRevealAt < 0.5f)
			{
				return;
			}
			lastPageRevealAt = now;
			List<GameObject> rows = new List<GameObject>();
			for (int i = 0; i < container.childCount; i++)
			{
				Transform ch = container.GetChild(i);
				if (ch != null && ch.gameObject != null)
				{
					rows.Add(ch.gameObject);
				}
			}
			BeginRevealRows("page", rows, cg);
		}
		catch
		{
		}
	}

	/// <summary>v1.7.5：把一次淡入立即收尾（alpha 归 1）。</summary>
	private static void FinishReveal(RevealItem it)
	{
		try
		{
			if (it == null)
			{
				return;
			}
			if (it.containerGroup != null && !it.containerGroup.Equals(null))
			{
				it.containerGroup.alpha = 1f;
			}
			for (int k = 0; k < it.groups.Count; k++)
			{
				CanvasGroup cg = it.groups[k];
				if (cg != null && !cg.Equals(null))
				{
					cg.alpha = 1f;
				}
			}
			it.lastAlpha = 1f;
		}
		catch
		{
		}
	}

	/// <summary>v1.7.5 / v1.7.6：这批行**要被收起/隐藏了** → 立即收尾它们涉及的淡入。
	/// 行若带着 alpha&lt;1 被 SetActive(false)，下次显示就是"行在、看不见"（本项目最忌讳的故障形态）
	/// ——宁可不要动画，也不能留这个隐患。
	/// 整块淡入（整页，`containerGroup`）不在此列：它的行就是整页，收尾交给页面重建。</summary>
	private static void FinishRevealForRows(List<GameObject> rows)
	{
		try
		{
			if (rows == null)
			{
				return;
			}
			for (int i = reveals.Count - 1; i >= 0; i--)
			{
				RevealItem it = reveals[i];
				if (it == null)
				{
					reveals.RemoveAt(i);
					continue;
				}
				if (it.containerGroup != null)
				{
					continue;
				}
				bool hit = false;
				for (int k = 0; k < it.rows.Count && !hit; k++)
				{
					for (int j = 0; j < rows.Count; j++)
					{
						if (rows[j] != null && it.rows[k] == rows[j])
						{
							hit = true;
							break;
						}
					}
				}
				if (hit)
				{
					FinishReveal(it);
					reveals.RemoveAt(i);
				}
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.7.5：整页重建前调用——旧行已随页面销毁，进行态的引用必须作废。
	/// **先收尾再清空**：万一某个容器还活着（例如重建的是同一批对象），不能留下半透明状态。</summary>
	internal static void ClearReveals()
	{
		try
		{
			for (int i = 0; i < reveals.Count; i++)
			{
				FinishReveal(reveals[i]);
			}
			reveals.Clear();
			flashProbes.Clear();
		}
		catch
		{
		}
	}

	/// <summary>v1.7.5：每帧推进淡入（挂在 PollControls 的既有链上）。
	/// alpha 量化到 1/8 步：只有真的变了才写，避免每帧无意义的 Canvas 重建。</summary>
	private static void PollReveal()
	{
		if (reveals.Count == 0)
		{
			return;
		}
		float now = Time.unscaledTime;
		for (int i = reveals.Count - 1; i >= 0; i--)
		{
			RevealItem it = reveals[i];
			if (it == null)
			{
				reveals.RemoveAt(i);
				continue;
			}
			try
			{
				// 注意：Unity 的伪 null —— 对象被销毁后 `probe == null` 为 true（`probe != null` 反而为 false），
				// 所以这里必须用 `== null` 判"已销毁"。
				GameObject probe = it.probe;
				if (probe == null || probe.Equals(null))
				{
					reveals.RemoveAt(i); // 行/容器已随页面销毁
					continue;
				}
				float t = now - it.startAt;
				float a = (it.dur <= 0.01f) ? 1f : Mathf.Clamp01((t - it.hold) / it.dur);
				a = Mathf.Min(1f, Mathf.Ceil(a * 8f) / 8f);
				if (now >= it.deadline)
				{
					a = 1f;
				}
				if (a != it.lastAlpha)
				{
					it.lastAlpha = a;
					if (it.containerGroup != null && !it.containerGroup.Equals(null))
					{
						it.containerGroup.alpha = a; // 整块模式：只写一个
					}
					else
					{
						for (int k = 0; k < it.groups.Count; k++)
						{
							CanvasGroup cg = it.groups[k];
							if (cg != null && !cg.Equals(null))
							{
								cg.alpha = a;
							}
						}
					}
				}
				if (a >= 1f)
				{
					reveals.RemoveAt(i);
				}
			}
			catch
			{
				reveals.RemoveAt(i);
			}
		}
	}

	/// <summary>v1.7.5：**展开时重新确认命中区（HitArea）颜色**——修 + 诊断二合一。
	/// 机理：`Graphic.OnDisable → canvasRenderer.Clear()` 会把 `CanvasRenderer` 的颜色丢掉，再启用时要靠
	/// `Selectable.OnEnable → DoStateTransition` 重新写上。那条链只要有一拍没接上（典型：上一次过渡的
	/// 协程被停用打断，`TweenRunner.m_Running` 卡在 true → 新的即时过渡不再同步应用），命中区就恢复成
	/// **不透明白**——与 v1.5.13 定案的白块同源。这里只读 alpha，异常才写回：健康路径零写入、零重建；
	/// 异常则顺带修好并打一级告警（**不受 debugLog 门控**，属于故障而不是诊断）。</summary>
	private static void ReassertRowHitColors(List<GameObject> rows)
	{
		try
		{
			if (rows == null)
			{
				return;
			}
			int fixedRows = 0;
			for (int i = 0; i < rows.Count; i++)
			{
				GameObject row = rows[i];
				if (row == null || row.Equals(null) || !row.activeInHierarchy)
				{
					continue;
				}
				try
				{
					Transform h = row.transform.Find("HitArea");
					if (h == null)
					{
						continue;
					}
					UnityEngine.UI.Image im = h.GetComponent<UnityEngine.UI.Image>();
					if (im == null || im.canvasRenderer == null)
					{
						continue;
					}
					if (im.canvasRenderer.GetColor().a <= 0.01f)
					{
						continue;
					}
					UnityEngine.UI.Button hb = h.GetComponent<UnityEngine.UI.Button>();
					Color want = (hb != null) ? hb.colors.normalColor : new Color(1f, 1f, 1f, 0f);
					im.canvasRenderer.SetColor(want);
					fixedRows++;
				}
				catch
				{
				}
			}
			if (fixedRows > 0)
			{
				Plugin.ModLog.LogWarning((object)("ModManager: 展开/建页时发现 " + fixedRows
					+ " 行命中区颜色不透明（白块类闪烁），已按各自配色纠正。"));
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.7.5：**展开窗口闪烁探针**。玩家说的"展开时选项闪一下"有三类物理成因，判据各不相同：
	/// ① **未落定**：行的 y 还没被布局写下来（多行 y 相同 = 叠在一起）→ 那一帧渲染出来就是一块亮斑；
	/// ② **白块**：命中区本该透明却是不透明（见 ReassertRowHitColors）；
	/// ③ **页面位移**：`contentH` 在展开之后又变了一次 → 整列行跟着动，观感同样是"闪一下"。
	/// 一次展开采 4 个点：同帧 / +0.03s / +0.13s（延迟重排那一拍）/ +0.36s（延迟自检那一拍），
	/// 每个点一条 `[MM-flash]`，debugLog 门控。有它就无需再靠猜。</summary>
	private sealed class FlashProbe
	{
		internal string name;
		internal List<GameObject> rows;
		internal Transform container;

		/// <summary>v1.7.6：内容页（容器之父）——用来数"非我们的子物体"（原生填充污染）。</summary>
		internal Transform page;

		internal float t0;
		internal int stage;
	}

	private static readonly List<FlashProbe> flashProbes = new List<FlashProbe>();

	private static readonly float[] FlashStageAt = new float[] { 0f, 0.03f, 0.13f, 0.36f };

	private static void BeginFlashProbe(string name, List<GameObject> rows, int from)
	{
		try
		{
			if (!Plugin.DebugOn || rows == null || rows.Count <= from)
			{
				return;
			}
			List<GameObject> mine = new List<GameObject>();
			for (int i = from; i < rows.Count; i++)
			{
				GameObject r = rows[i];
				if (r != null && !r.Equals(null))
				{
					mine.Add(r);
				}
			}
			if (mine.Count == 0)
			{
				return;
			}
			FlashProbe p = new FlashProbe();
			p.name = name;
			p.rows = mine;
			p.container = mine[0].transform.parent;
			p.t0 = Time.unscaledTime;
			p.stage = 0;
			flashProbes.Add(p);
			SampleFlashProbe(p); // stage 0：同帧（重排之后）
			p.stage = 1;
		}
		catch
		{
		}
	}

	/// <summary>v1.7.6：**翻页窗口探针**——建页（进 MODS 页 / 重进设置 / 自愈重建）后采样同一套判据，
	/// 另外多数一项 `foreign`（内容页里"非我们的子物体"个数 = 原生填充协程把原生行灌进我们页面的污染）。
	/// 这是"翻页闪烁"的一个已知成因：`Object.Destroy` 要到**帧末**才生效 → 污染行会被渲染一帧。
	/// 采样点与展开探针一致（同帧 / +0.03s / +0.13s / +0.36s），每条带 `f=`（帧号）便于和翻页日志对齐。</summary>
	private static void BeginPageFlashProbe(Transform container)
	{
		try
		{
			if (!Plugin.DebugOn || container == null || container.Equals(null))
			{
				return;
			}
			List<GameObject> mine = new List<GameObject>();
			for (int i = 0; i < container.childCount; i++)
			{
				Transform ch = container.GetChild(i);
				if (ch != null && ch.gameObject != null)
				{
					mine.Add(ch.gameObject);
				}
			}
			if (mine.Count == 0)
			{
				return;
			}
			FlashProbe p = new FlashProbe();
			p.name = "page";
			p.rows = mine;
			p.container = container;
			p.page = container.parent;
			p.t0 = Time.unscaledTime;
			p.stage = 0;
			flashProbes.Add(p);
			SampleFlashProbe(p); // stage 0：同帧（建页与重排之后）
			p.stage = 1;
		}
		catch
		{
		}
	}

	private static void PollFlashProbe()
	{
		if (flashProbes.Count == 0)
		{
			return;
		}
		float now = Time.unscaledTime;
		for (int i = flashProbes.Count - 1; i >= 0; i--)
		{
			FlashProbe p = flashProbes[i];
			if (p == null)
			{
				flashProbes.RemoveAt(i);
				continue;
			}
			try
			{
				if (now - p.t0 > 1.5f)
				{
					flashProbes.RemoveAt(i);
					continue;
				}
				if (p.stage < FlashStageAt.Length && now - p.t0 >= FlashStageAt[p.stage])
				{
					SampleFlashProbe(p);
					p.stage++;
				}
			}
			catch
			{
				flashProbes.RemoveAt(i);
			}
		}
	}

	private static void SampleFlashProbe(FlashProbe p)
	{
		try
		{
			if (p == null || p.rows == null)
			{
				return;
			}
			int active = 0;
			int whiteRows = 0;
			float hitMax = 0f;
			Dictionary<float, int> ys = new Dictionary<float, int>();
			for (int i = 0; i < p.rows.Count; i++)
			{
				GameObject row = p.rows[i];
				if (row == null || row.Equals(null) || !row.activeInHierarchy)
				{
					continue;
				}
				active++;
				RectTransform rt = row.GetComponent<RectTransform>();
				if (rt != null)
				{
					int cnt;
					ys.TryGetValue(rt.anchoredPosition.y, out cnt);
					ys[rt.anchoredPosition.y] = cnt + 1;
				}
				Transform h = row.transform.Find("HitArea");
				if (h != null)
				{
					UnityEngine.UI.Image im = h.GetComponent<UnityEngine.UI.Image>();
					if (im != null && im.canvasRenderer != null)
					{
						float a = im.canvasRenderer.GetColor().a;
						if (a > hitMax)
						{
							hitMax = a;
						}
						if (a > 0.5f)
						{
							whiteRows++;
						}
					}
				}
			}
			float contH = -1f;
			float contentH = -1f;
			float vnp = -1f;
			if (p.container != null && !p.container.Equals(null))
			{
				RectTransform crt = p.container as RectTransform;
				if (crt != null)
				{
					contH = crt.rect.height;
				}
				Transform host = p.container.parent;
				if (host != null)
				{
					RectTransform hrt = host as RectTransform;
					if (hrt != null)
					{
						contentH = hrt.rect.height;
					}
				}
				UnityEngine.UI.ScrollRect sr = FindScrollRect(p.container);
				if (sr != null)
				{
					vnp = sr.verticalNormalizedPosition;
				}
			}
			// v1.7.6：内容页里"非我们的子物体"个数——原生填充协程把原生行灌进我们页面的污染。
			// 这条是"翻页闪烁"的已知成因：`Object.Destroy` 帧末才生效 → 污染行会被渲染一帧。
			int foreign = -1;
			if (p.page != null && !p.page.Equals(null))
			{
				foreign = 0;
				for (int i = 0; i < p.page.childCount; i++)
				{
					Transform ch = p.page.GetChild(i);
					if (ch != null && ch.name != "MM_Container")
					{
						foreign++;
					}
				}
			}
			Plugin.ModLog.LogInfo((object)("[MM-flash] '" + p.name + "' stage=" + p.stage
				+ " dt=" + ((Time.unscaledTime - p.t0) * 1000f).ToString("0") + "ms"
				+ " f=" + Time.frameCount
				+ " rows=" + p.rows.Count + " active=" + active + " yUniq=" + ys.Count
				+ ((active > 1 && ys.Count < active) ? " <<未落定" : "")
				+ " hitAlphaMax=" + hitMax.ToString("F2")
				+ ((whiteRows > 0) ? (" <<命中区不透明行数=" + whiteRows) : "")
				+ " foreign=" + foreign
				+ ((foreign > 0) ? " <<原生污染" : "")
				+ " contH=" + contH.ToString("0") + " contentH=" + contentH.ToString("0")
				+ " vnp=" + vnp.ToString("F2")));
		}
		catch
		{
		}
	}

	/// <summary>v1.7.3：延迟建页自检的判定。返回 "" = 正常，否则返回人可读的原因。
	/// 判据来自历史三次故障的**物理成因**，不是猜：
	/// ① **结构**——`v1.5.24`：插入锚点取错 → 新行被追加到容器末尾（而不是标题下方），
	///    于是内容"存在但在屏幕外"。判据 = 行必须都在容器内、且紧跟在标题行之后连续排列。
	/// ② **布局**——`v1.5.21`：只重建了容器一层 → 子行排好了，容器自身的**外表几何**停在旧值。
	///    判据 = 宿主（contentPage）实际高度必须跟上它的需求高度（与自愈巡检同一套"需求 vs 实际"思路）。
	/// </summary>
	private static string LazyBuildProblem(ModBody body)
	{
		try
		{
			if (body == null)
			{
				return "body=null";
			}
			if (body.container == null || body.container.Equals(null))
			{
				return "container 已销毁";
			}
			if (body.rows.Count == 0)
			{
				return "rows=0（该 mod 应当有行却一行都没建）";
			}
			int titleIdx = -1;
			if (body.titleRow != null && !body.titleRow.Equals(null))
			{
				titleIdx = body.titleRow.transform.GetSiblingIndex();
			}
			for (int i = 0; i < body.rows.Count; i++)
			{
				GameObject go = body.rows[i];
				if (go == null || go.Equals(null))
				{
					return "row[" + i + "] 已销毁";
				}
				if (go.transform.parent != body.container)
				{
					return "row[" + i + "] 不在容器内";
				}
				if (titleIdx >= 0)
				{
					int idx = go.transform.GetSiblingIndex();
					if (idx < titleIdx + 1)
					{
						return "row[" + i + "] 插到标题之前(idx=" + idx + " < " + (titleIdx + 1) + ")";
					}
					if (idx != titleIdx + 1 + i)
					{
						return "row[" + i + "] 位置不连续(idx=" + idx + " ≠ " + (titleIdx + 1 + i) + ")";
					}
				}
			}
			// 布局：宿主高度是否跟上需求（宿主 = 容器的父级 contentPage）
			try
			{
				RectTransform host = body.container.parent as RectTransform;
				if (host != null)
				{
					float pref = UnityEngine.UI.LayoutUtility.GetPreferredHeight(host);
					float real = host.rect.height;
					if (pref > real + 8f)
					{
						return "宿主高度未跟上(pref=" + pref.ToString("0") + " > rect=" + real.ToString("0") + ")";
					}
				}
			}
			catch
			{
			}
			return "";
		}
		catch (Exception ex)
		{
			return "自检本身异常: " + ex.Message;
		}
	}

	/// <summary>v1.7.3：每帧轮询里的延迟建页自检（挂在 PollControls 的既有链上）。
	/// 通过 → 清空失败计数；失败 → 先补排一次布局；**连续两次失败 → 本会话弃用延迟建页 + 整页重建**，
	/// 让玩家立刻回到已验证的常规路径（代价 = 一次常规建页，即原本每次进页都要付的那 0.5 秒）。
	/// 这样"实验性优化"最坏也只是把老路径的代价付一次，而不再是把功能弄坏。</summary>
	private static void PollLazyVerify()
	{
		try
		{
			if (lazyPendingVerify.Count == 0 || lazyVerifyAt <= 0f || Time.unscaledTime < lazyVerifyAt)
			{
				return;
			}
			lazyVerifyAt = 0f;
			string firstProblem = null;
			string firstName = null;
			for (int i = 0; i < lazyPendingVerify.Count; i++)
			{
				ModBody b = lazyPendingVerify[i];
				string why = LazyBuildProblem(b);
				if (!string.IsNullOrEmpty(why))
				{
					if (firstProblem == null)
					{
						firstProblem = why;
						firstName = (b != null) ? b.name : "?";
					}
				}
			}
			lazyPendingVerify.Clear();
			if (firstProblem == null)
			{
				lazyFailStreak = 0;
				if (Plugin.DebugOn)
				{
					Plugin.ModLog.LogInfo("[MM-lazy] 自检通过：延迟构建的行结构与宿主布局均正常。");
				}
				return;
			}
			lazyFailStreak++;
			Plugin.ModLog.LogWarning("[MM-lazy] 自检失败（第 " + lazyFailStreak + " 次）mod='"
				+ firstName + "'：" + firstProblem);
			if (lazyFailStreak < 2)
			{
				MarkPendingRelayout(); // 先给一次补救：再整体重排一次
				return;
			}
			lazyDisabled = true;
			Plugin.ModLog.LogWarning("[MM-lazy] 连续两次自检失败 → 本会话弃用延迟建页，整页重建回常规路径"
				+ "（一次性代价 = 一次常规建页；请把这条日志反馈给作者）。");
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s != null)
			{
				OpenMyPage(s);
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.5.21：一次性几何诊断（debugLog 门控）。上一轮的诊断只有"行数 / 激活数 / 父链"
	/// ——那些全部正常、内容却仍看不见，说明问题在**几何**上。这里把 Canvas / 内容页 / 视口 / 容器
	/// 的矩形与世界坐标一次性打全，用来直接判定"新行到底在不在可见范围内"，不再靠推断。</summary>
	private static void LogLazyGeometry(string where)
	{
		try
		{
			if (!Plugin.DebugOn)
			{
				return;
			}
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s == null || s.contentPage == null)
			{
				return;
			}
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			sb.Append("[MM-lazy] 几何 ").Append(where);
			try
			{
				Canvas cv = s.contentPage.GetComponentInParent<Canvas>();
				if (cv != null)
				{
					RectTransform cvrt = cv.GetComponent<RectTransform>();
					sb.Append(" | canvas scale=").Append(cv.scaleFactor.ToString("F2"));
					if (cvrt != null)
					{
						sb.Append(" size=").Append(cvrt.rect.width.ToString("0")).Append('x').Append(cvrt.rect.height.ToString("0"));
					}
				}
			}
			catch
			{
			}
			try
			{
				RectTransform prt = s.contentPage.GetComponent<RectTransform>();
				if (prt != null)
				{
					Vector3[] c = new Vector3[4];
					prt.GetWorldCorners(c);
					sb.Append(" | content rect=").Append(prt.rect.width.ToString("0")).Append('x').Append(prt.rect.height.ToString("0"))
					  .Append(" worldY=").Append(c[0].y.ToString("0")).Append("..").Append(c[1].y.ToString("0"))
					  .Append(" anchoredY=").Append(prt.anchoredPosition.y.ToString("0"));
				}
			}
			catch
			{
			}
			try
			{
				UnityEngine.UI.ScrollRect sr = FindScrollRect(s.contentPage);
				if (sr != null)
				{
					RectTransform vp = (sr.viewport != null) ? sr.viewport : sr.GetComponent<RectTransform>();
					if (vp != null)
					{
						Vector3[] c = new Vector3[4];
						vp.GetWorldCorners(c);
						sb.Append(" | viewport rect=").Append(vp.rect.width.ToString("0")).Append('x').Append(vp.rect.height.ToString("0"))
						  .Append(" worldY=").Append(c[0].y.ToString("0")).Append("..").Append(c[1].y.ToString("0"))
						  .Append(" vnp=").Append(sr.verticalNormalizedPosition.ToString("F2"));
					}
				}
			}
			catch
			{
			}
			try
			{
				Transform cont = null;
				for (int i = s.contentPage.childCount - 1; i >= 0; i--)
				{
					Transform ch = s.contentPage.GetChild(i);
					if (ch != null && ch.name == "MM_Container")
					{
						cont = ch;
						break;
					}
				}
				if (cont != null)
				{
					RectTransform crt = cont.GetComponent<RectTransform>();
					if (crt != null)
					{
						Vector3[] c = new Vector3[4];
						crt.GetWorldCorners(c);
						sb.Append(" | container rect=").Append(crt.rect.width.ToString("0")).Append('x').Append(crt.rect.height.ToString("0"))
						  .Append(" worldY=").Append(c[0].y.ToString("0")).Append("..").Append(c[1].y.ToString("0"))
						  .Append(" anchoredY=").Append(crt.anchoredPosition.y.ToString("0"))
						  .Append(" children=").Append(cont.childCount);
					}
				}
			}
			catch
			{
			}
			Plugin.ModLog.LogInfo(sb.ToString());
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
			SaveExpandedState(); // v1.5.17：展开状态立即落盘（重开游戏仍保持）
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
				// v1.7.6：分区展开/收起同样打淡入（它一次露出的是多行，更需要遮住落定那一两帧）
				if (now)
				{
					BeginRevealRows("section", sb.rows);
				}
				else
				{
					FinishRevealForRows(sb.rows);
				}
				LogToggle("section", sb.key, now, sb.rows.Count);
				LogToggleState("section", sb.key, sb.rows);
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
			// v1.5.5 修复：这里原本无条件 `t.text = label`，把上面 FitRowLabel 的**截断结果又覆盖回
			// 未截断的原文** → 长标签照旧压到值控件上（v1.5.0 声称的"省略号截断"在原生模板路径上
			// 从未真正生效）。现在只在 FitRowLabel 没能写入时兜底赋值。
			Text t = go.transform.GetChild(0).GetComponent<Text>();
			if (t != null && string.IsNullOrEmpty(t.text))
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
				ex.cfg = cfg;
				ex.entry = entry;
				// v1.5.5：改动标记的原文 = 含 ▸/▾ 前缀的整行标签；标记由 FitRowLabel 的 suffix 拼上
				ex.baseLabel = rowLabel;
				ex.dirty = IsDirty(cfg, entry);
				FinishEntryRow(row, cfg, entry, ex);
				if (ex.dirty && ex.arrow != null && !ex.arrow.Equals(null))
				{
					FitRowLabel(ex.arrow, rowLabel, DirtySuffix);
				}
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
				// v1.5.5：mod 自己在简介里写明的"需重启生效"前置到这里（原先要展开读原文才知道）
				if (RequiresRestart(entry))
				{
					string note = Plugin.DefaultChinese ? "改动需重启游戏生效" : "Takes effect after game restart";
					info = string.IsNullOrEmpty(info) ? note : (info + "   ·   " + note);
				}
				if (!string.IsNullOrEmpty(info))
				{
					AddDescriptionRow(container, info);
					ex.descRow = container.GetChild(container.childCount - 1).gameObject;
					ex.descRow.SetActive(entryExpanded);
				}
				// v1.5.5：单项还原行（行体与简介行一样"一次建好"，只按 dirty + 展开态显隐）
				ex.restoreRow = AddRestoreRow(container, cfg, entry);
				if (ex.restoreRow != null)
				{
					ex.restoreRow.SetActive(ex.dirty && ex.VisibleNow());
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

	/// <summary>v1.5.5：mod 自带的简介里写明"需要重启"的判定词（中英双语，大小写不敏感）。
	/// 这类改动写入 cfg 后当前这一局仍然用旧值——不提示就是"改了没反应"投诉的主要来源。</summary>
	private static readonly string[] RestartHints =
	{
		"restart",
		"reboot",
		"requires rest",
		"after restart",
		"重启",
		"重开游戏",
		"重新进入游戏",
		"下次启动"
	};

	/// <summary>v1.5.5：该项是否属于"改完要重启游戏才生效"（读 cfg 简介判定；误判代价很低，
	/// 只是多一句提示；漏判才是问题，所以词表宁宽不紧）。</summary>
	private static bool RequiresRestart(ConfigEntryBase entry)
	{
		try
		{
			string desc = (entry != null && entry.Description != null) ? entry.Description.Description : null;
			if (string.IsNullOrEmpty(desc))
			{
				return false;
			}
			string low = desc.ToLowerInvariant();
			for (int i = 0; i < RestartHints.Length; i++)
			{
				if (low.Contains(RestartHints[i]))
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

	/// <summary>v1.5.5：单项还原行——左侧显示默认值（先知道会变成什么再决定要不要还原），
	/// 右侧是"还原默认"文字按钮。**只在改动过且该项展开时出现**（无改动就不占版面）。</summary>
	private static GameObject AddRestoreRow(Transform container, ConfigFile cfg, ConfigEntryBase entry)
	{
		try
		{
			GameObject row = new GameObject("MM_RestoreRow");
			row.transform.SetParent(container, false);
			RectTransform rt = row.AddComponent<RectTransform>();
			rt.sizeDelta = new Vector2(0f, 26f);
			GameObject tgo = new GameObject("Label");
			tgo.transform.SetParent(row.transform, false);
			RectTransform trt = tgo.AddComponent<RectTransform>();
			trt.anchorMin = new Vector2(0f, 0.5f);
			trt.anchorMax = new Vector2(1f, 0.5f);
			trt.pivot = new Vector2(0.5f, 0.5f);
			// v1.7.1：**必须显式给高度**。原先用 offsetMin/offsetMax 且 y 两端都设 0，而
			// anchorMin.y == anchorMax.y == 0.5 时这两个偏移直接决定矩形上下边 → 高度恒为 0
			// → Text（默认 Truncate）把整行裁掉：文字从来不显示，行却照占布局高度。
			// 玩家截图实证：收藏区占了 95px 却全黑。改用与元信息行同一套写法（sizeDelta 给高 +
			// anchoredPosition 定位），那套已被玩家确认可见。
			float rli = LabelLeft + RowIndent + 18f;
			float rri = ControlRight + 96f;
			trt.sizeDelta = new Vector2(-(rli + rri), 20f);
			trt.anchoredPosition = new Vector2((rli - rri) * 0.5f, 0f);
			Text txt = tgo.AddComponent<Text>();
			Font font = GetNativeFont(SettingsGUI_V2.instance);
			if (font != null)
			{
				txt.font = font;
			}
			txt.fontSize = 14;
			txt.color = new Color(0.62f, 0.62f, 0.68f, 0.95f);
			txt.alignment = TextAnchor.MiddleLeft;
			txt.horizontalOverflow = HorizontalWrapMode.Overflow;
			txt.raycastTarget = false;
			txt.text = (Plugin.DefaultChinese ? "默认值 " : "Default ") + FormatDefaultValue(entry);
			Text resetTxt = null;
			resetTxt = MakeSmallTextButton(row, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f), new Vector2(-ControlRight, 0f), new Vector2(84f, 22f), Plugin.DefaultChinese ? "还原默认" : "Restore", delegate
			{
				RestoreEntryDefault(cfg, entry, resetTxt);
			});
			return row;
		}
		catch (Exception err)
		{
			Plugin.ModLog.LogError((object)("ModManager restore row error: " + err.Message));
			return null;
		}
	}

	/// <summary>默认值文本（与值框同一套格式化：float 去尾 0，其余 ToString）。</summary>
	private static string FormatDefaultValue(ConfigEntryBase entry)
	{
		try
		{
			object v = entry.DefaultValue;
			if (v == null)
			{
				return "";
			}
			if (v is float fv)
			{
				return fv.ToString("0.##");
			}
			return v.ToString();
		}
		catch
		{
			return "?";
		}
	}

	/// <summary>v1.5.5：把**单个**配置项还原为默认值。
	/// 与旧版 AddEntryActions 的关键差别：**不再整页重建**（OpenMyPage）——v1.5.0 就是为了消除
	/// 这个白闪才改成"一次建好 + 原地显隐"，重建页面会把老问题带回来。这里改为把默认值写回控件。</summary>
	private static void RestoreEntryDefault(ConfigFile cfg, ConfigEntryBase entry, Text flashTarget)
	{
		try
		{
			if (cfg == null || entry == null)
			{
				return;
			}
			object def = entry.DefaultValue;
			if (def == null)
			{
				Plugin.ModLog.LogWarning((object)("ModManager: cannot restore " + entry.Definition.Key + " - no default value."));
				return;
			}
			string k = entry.Definition.Key;
			StageValue(cfg, entry, def);
			ApplyValueToControls(cfg, entry, def);
			RefreshEntryDirtyUi(cfg, entry);
			if (flashTarget != null && !flashTarget.Equals(null))
			{
				FlashText(flashTarget, Plugin.DefaultChinese ? "已还原 ✓" : "Restored ✓", 1.5f);
			}
			Plugin.ModLog.LogInfo((object)("ModManager: restored '" + k + "' to default '" + def + "' (pending save)."));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager entry restore error: " + ex.Message));
		}
	}

	/// <summary>v1.5.5：把指定值写回该项已建好的控件显示（Toggle / Slider / InputField / Dropdown /
	/// 改键按钮）。用途：单项还原时在**不重建页面**的前提下让控件立刻反映新值。
	/// 一条都没命中时打 Warning —— 界面会显示旧值，属于必须知道的情况。</summary>
	private static void ApplyValueToControls(ConfigFile cfg, ConfigEntryBase entry, object val)
	{
		int hits = 0;
		try
		{
			for (int i = 0; i < watches.Count; i++)
			{
				ControlWatch w = watches[i];
				if (w == null || w.cfg != cfg || w.entry != entry)
				{
					continue;
				}
				try
				{
					if (w.toggle != null && !w.toggle.Equals(null))
					{
						bool b = Convert.ToBoolean(val);
						w.toggle.isOn = b;
						w.lastBool = b;
						w.hasBool = true;
						hits++;
					}
					else if (w.slider != null && !w.slider.Equals(null))
					{
						float f = Convert.ToSingle(val);
						if (w.hasRange)
						{
							f = Mathf.Clamp(f, w.rangeMin, w.rangeMax);
						}
						w.slider.value = f;
						w.lastFloat = f;
						UpdateValueTexts(w.valueTexts, w.wholeNumbers, f);
						hits++;
					}
					else if (w.input != null && !w.input.Equals(null))
					{
						string s = w.isFreeText
							? ((val as string) ?? "")
							: (w.inputIsFloat ? Convert.ToSingle(val).ToString("0.##") : Convert.ToInt32(val).ToString());
						w.input.text = s;
						w.lastInputText = s;
						hits++;
					}
					else if (w.dropdown != null && !w.dropdown.Equals(null))
					{
						int idx = DropdownIndexFor(w, val);
						if (idx >= 0)
						{
							w.dropdown.value = idx;
							w.lastInt = idx;
							hits++;
						}
					}
				}
				catch (Exception ex)
				{
					Plugin.ModLog.LogError((object)("ModManager control sync error: " + ex.Message));
				}
			}
			// 改键按钮没有 watch（值就是按钮上的文字），单独同步
			EntryExtras exx = FindExtras(cfg, entry);
			if (exx != null && exx.valueButton != null && !exx.valueButton.Equals(null) && val != null)
			{
				exx.valueButton.text = HumanizeKey(val.ToString());
				hits++;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager value sync error: " + ex.Message));
		}
		if (hits == 0)
		{
			Plugin.ModLog.LogWarning((object)("ModManager: no control found to display new value of '" + entry.Definition.Key + "' (value staged, UI may lag until page rebuild)."));
		}
	}

	/// <summary>把配置值换算成下拉框的选项下标（与 PollControls 的写回逻辑互为逆运算）。</summary>
	private static int DropdownIndexFor(ControlWatch w, object val)
	{
		try
		{
			if (w.entry == null || val == null)
			{
				return -1;
			}
			if (w.entry.SettingType == typeof(string))
			{
				AcceptableValueBase av = (w.entry.Description != null) ? w.entry.Description.AcceptableValues : null;
				if (av is AcceptableValueList<string> lst && lst.AcceptableValues != null)
				{
					string[] vals = lst.AcceptableValues;
					for (int i = 0; i < vals.Length; i++)
					{
						if (vals[i] == (string)val)
						{
							return i;
						}
					}
				}
				return -1;
			}
			if (w.entry.SettingType == typeof(bool))
			{
				return Convert.ToBoolean(val) ? 1 : 0;
			}
			if (w.enumType != null && w.enumNames != null)
			{
				string name = val.ToString();
				for (int i = 0; i < w.enumNames.Length; i++)
				{
					if (w.enumNames[i] == name)
					{
						return i;
					}
				}
				return -1;
			}
			return Convert.ToInt32(val);
		}
		catch
		{
			return -1;
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
			// v1.7.7：**按"键值"聚合，一键一行**（旧实现两两配对：A/B/C 三个绑定同用一键会报
			// "A 与 B""A 与 C"两行，B 与 C 的关系反而看不到；聚合后一行列全，且不会重复报）。
			// 键用 OrdinalIgnoreCase：大小写不同的同一按键（"F5"/"f5"）也算冲突，显示取先见到的写法。
			Dictionary<string, List<string>> byValue = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
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
					// v1.7.7：**String 条目要求"值像按键"**——与渲染端对齐（渲染端还有 LooksLikeKeyList 守卫，
					// 扫描端此前没有 → 比渲染端宽松）。实锤反例：er2.morephysics 的 ExcludedNameKeywords
					//（键名含 "key"，值是逗号分隔的排除名单）此前会被当热键参与冲突比对 = 误报隐患；
					// 两个 mod 恰好有相同的名单默认值时会报一条假"热键冲突"。
					if (e.SettingType == typeof(string) && !LooksLikeKeyValue(value))
					{
						continue;
					}
					string who = p.name + " (" + HumanizeKey(e.Definition.Key) + ")";
					List<string> users;
					if (!byValue.TryGetValue(value, out users))
					{
						users = new List<string>();
						byValue[value] = users;
					}
					users.Add(who);
				}
			}
			foreach (KeyValuePair<string, List<string>> kv in byValue)
			{
				if (kv.Value == null || kv.Value.Count < 2)
				{
					continue;
				}
				// 同一 mod 内两个条目用同一键也会列出来（自冲突同样值得提示）
				string names = string.Join(Plugin.DefaultChinese ? "、" : ", ", kv.Value.ToArray());
				result.Add(Plugin.DefaultChinese
					? "⚠ 热键冲突：「" + kv.Key + "」被 " + kv.Value.Count + " 个绑定使用：" + names
					: "⚠ Hotkey conflict: '" + kv.Key + "' is used by " + kv.Value.Count + " bindings: " + names);
			}
			if (Plugin.DebugOn)
			{
				Plugin.ModLog.LogInfo((object)("ModManager: hotkey conflict scan: " + result.Count
					+ " group(s) (KeyCode/String/Enum entries whose name contains key/toggle)"));
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager hotkey conflict scan error: " + ex.Message));
		}
		return result;
	}

	/// <summary>v1.7.7：字符串值是否"像一个按键"——**冲突扫描专用**的形状校验（渲染端另有
	/// `LooksLikeKeyList`：要求条目自带 AcceptableValueList 且候选项像键，两者口径不同、各管各的）。
	/// 允许：单词（F5 / Space / Alpha1 / Mouse0 / "joystick button 3"）与 A+B 组合、空值（未设置）。
	/// 拒绝：逗号/分号分隔的名单、路径、句子、超长值——键名碰巧含 "key" 的普通字符串配置不该被当成热键。</summary>
	internal static bool LooksLikeKeyValue(string value)
	{
		try
		{
			if (string.IsNullOrEmpty(value))
			{
				return true; // 未设置：仍按热键对待（允许首次绑定），冲突比对处会跳过空值
			}
			string v = value.Trim();
			if (v.Length == 0 || v.Length > 24)
			{
				return false;
			}
			if (v.IndexOfAny(new char[] { ',', ';', '\\', '/', ':', '=', '\t' }) >= 0)
			{
				return false;
			}
			return System.Text.RegularExpressions.Regex.IsMatch(v, "^[A-Za-z0-9+ ]{1,24}$");
		}
		catch
		{
			return true;
		}
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

	/// <summary>v1.6.1：收藏的持久化文件（与展开状态一样刻意不放 cfg）。每行一项 = "mod 名|条目键"。</summary>
	private static string FavoritesPath()
	{
		return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "er2.modmanager.favorites.txt");
	}

	internal static void LoadFavorites()
	{
		try
		{
			favorites.Clear();
			string path = FavoritesPath();
			if (!System.IO.File.Exists(path))
			{
				return;
			}
			foreach (string raw in System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8))
			{
				if (string.IsNullOrEmpty(raw))
				{
					continue;
				}
				string line = raw.Trim();
				if (line.Length > 0 && !favorites.Contains(line))
				{
					favorites.Add(line);
				}
			}
			Plugin.ModLog.LogInfo((object)("ModManager: 收藏已恢复（" + favorites.Count + " 项）。"));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("ModManager: 收藏读取失败（不影响其它功能）: " + ex.Message);
		}
	}

	internal static void SaveFavorites()
	{
		try
		{
			System.Text.StringBuilder sb = new System.Text.StringBuilder();
			for (int i = 0; i < favorites.Count; i++)
			{
				string f = favorites[i];
				if (string.IsNullOrEmpty(f))
				{
					continue;
				}
				sb.Append(f.Replace('\n', ' ').Replace('\r', ' ')).Append('\n');
			}
			System.IO.File.WriteAllText(FavoritesPath(), sb.ToString(), new System.Text.UTF8Encoding(false));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("ModManager: 收藏保存失败: " + ex.Message);
		}
	}

	/// <summary>v1.7.0：该 mod 是否已收藏。</summary>
	private static bool IsModFavorite(string modName)
	{
		return !string.IsNullOrEmpty(modName) && favorites.Contains(modName);
	}

	/// <summary>v1.7.0：切换某个 mod 的收藏（mod 标题行右侧那颗 ★ 的回调）。
	/// 收藏的对象是**整个 mod**（不是单项设置）——点收藏区条目即展开该 mod 并滚到它。</summary>
	private static void ToggleModFavorite(string modName, Text btn)
	{
		try
		{
			if (string.IsNullOrEmpty(modName))
			{
				return;
			}
			bool now;
			if (favorites.Contains(modName))
			{
				favorites.Remove(modName);
				now = false;
			}
			else
			{
				if (favorites.Count >= FavSlots)
				{
					Plugin.ModLog.LogWarning("ModManager: 收藏已达上限 " + FavSlots + " 个 mod，本次未添加。");
					return;
				}
				favorites.Add(modName);
				now = true;
			}
			SaveFavorites();
			PlayClick();
			SetFavStarVisual(btn, now);
			// 同步同一 mod 的其它 ★ 实例（页面重建后可能残留旧引用），避免显示不一致
			for (int i = 0; i < modBodies.Count; i++)
			{
				ModBody mb = modBodies[i];
				if (mb != null && mb.name == modName && mb.favBtn != null && !mb.favBtn.Equals(null))
				{
					SetFavStarVisual(mb.favBtn, now);
				}
			}
			RefreshFavArea();
			Plugin.ModLog.LogInfo((object)("ModManager: 收藏" + (now ? "添加" : "移除") + " mod '"
				+ modName + "'（共 " + favorites.Count + " 个）"));
		}
		catch
		{
		}
	}

	/// <summary>v1.7.0：页首收藏区。**固定槽位一次建好**，之后只改文本与显隐（本项目不做事后插行）。</summary>
	private static void AddFavArea(Transform container)
	{
		try
		{
			GameObject head = new GameObject("MM_FavHeader");
			head.transform.SetParent(container, false);
			RectTransform hrt = head.AddComponent<RectTransform>();
			hrt.sizeDelta = new Vector2(0f, 24f);
			GameObject hgo = new GameObject("Label");
			hgo.transform.SetParent(head.transform, false);
			RectTransform htrt = hgo.AddComponent<RectTransform>();
			htrt.anchorMin = new Vector2(0f, 0.5f);
			htrt.anchorMax = new Vector2(1f, 0.5f);
			htrt.pivot = new Vector2(0.5f, 0.5f);
			// v1.7.1：高度必须显式给（见 AddRestoreRow 同处注释）——否则高度 0、标题文字永远看不见，
			// 而这一行仍占 24px 版面（玩家截图里那块空白就是它）。
			float hli = LabelLeft + RowIndent;
			float hri = ControlRight;
			htrt.sizeDelta = new Vector2(-(hli + hri), 20f);
			htrt.anchoredPosition = new Vector2((hli - hri) * 0.5f, 0f);
			Text htxt = hgo.AddComponent<Text>();
			Font hfont = GetNativeFont(SettingsGUI_V2.instance);
			if (hfont != null)
			{
				htxt.font = hfont;
			}
			htxt.fontSize = 13;
			htxt.fontStyle = FontStyle.Bold;
			htxt.color = new Color(0.85f, 0.72f, 0.30f, 0.95f);
			htxt.alignment = TextAnchor.MiddleLeft;
			htxt.horizontalOverflow = HorizontalWrapMode.Overflow;
			htxt.raycastTarget = false;
			htxt.text = Plugin.DefaultChinese ? "★ 收藏的 mod（点击跳转）" : "★ Favourite mods (click to jump)";
			favHeaderRow = head;
			head.SetActive(false);
			float li = LabelLeft + RowIndent;
			float ri = ControlRight;
			for (int i = 0; i < FavSlots; i++)
			{
				GameObject row = new GameObject("MM_FavRow" + i);
				row.transform.SetParent(container, false);
				RectTransform rt = row.AddComponent<RectTransform>();
				rt.sizeDelta = new Vector2(0f, 26f);
				GameObject hit = new GameObject("HitArea");
				hit.transform.SetParent(row.transform, false);
				hit.transform.SetAsFirstSibling();
				RectTransform hrt2 = hit.AddComponent<RectTransform>();
				hrt2.anchorMin = Vector2.zero;
				hrt2.anchorMax = Vector2.one;
				hrt2.offsetMin = Vector2.zero;
				hrt2.offsetMax = Vector2.zero;
				Image himg = hit.AddComponent<Image>();
				// v1.7.2：这个 Image **只负责接收指针**，所以彻底透明；悬停反馈改由**文字**承担——
				// 行底色那层高亮（0.035 白）在深色底上等于看不见，玩家反馈"鼠标放上去没有任何互动效果"。
				himg.color = new Color(1f, 1f, 1f, 0f);
				himg.raycastTarget = true;
				UnityEngine.UI.Button hbtn = hit.AddComponent<UnityEngine.UI.Button>();
				// targetGraphic 与悬停配色在标签建好之后再指定（见下方 ApplyButtonHoverTint）
				GameObject tgo = new GameObject("Label");
				tgo.transform.SetParent(row.transform, false);
				RectTransform trt = tgo.AddComponent<RectTransform>();
				trt.anchorMin = new Vector2(0f, 0.5f);
				trt.anchorMax = new Vector2(1f, 0.5f);
				trt.pivot = new Vector2(0.5f, 0.5f);
				// v1.7.1：高度必须显式给（同 AddRestoreRow / 收藏区标题处注释）
				float sli = li + 18f;
				float sri = ri;
				trt.sizeDelta = new Vector2(-(sli + sri), 20f);
				trt.anchoredPosition = new Vector2((sli - sri) * 0.5f, 0f);
				Text txt = tgo.AddComponent<Text>();
				Font font = GetNativeFont(SettingsGUI_V2.instance);
				if (font != null)
				{
					txt.font = font;
				}
				txt.fontSize = 14;
				txt.color = new Color(0.80f, 0.82f, 0.88f, 0.98f);
				txt.alignment = TextAnchor.MiddleLeft;
				txt.horizontalOverflow = HorizontalWrapMode.Overflow;
				txt.raycastTarget = false;
				// v1.7.2：悬停反馈 = 文字变亮（与 ★ 小按钮同一套机制：ColorTint 乘一个 >1 的值，
				// 文字自身偏暗所以会明显提亮）。本项目不能用 EventTrigger 绑 PointerEnter——
				// `UnityAction<T>` 的委托桥接在本项目有已知 marshaling 缺陷（见滑条/下拉的注释）。
				hbtn.targetGraphic = txt;
				ApplyButtonHoverTint(hbtn);
				favRows[i] = row;
				favLabels[i] = txt;
				int idx = i;
				System.Action jumpAction = delegate
				{
					JumpToFav(idx);
				};
				hbtn.onClick.RemoveAllListeners();
				hbtn.onClick.AddListener(jumpAction);
				row.SetActive(false);
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager favorite area error: " + ex.Message));
		}
	}

	/// <summary>v1.7.0：填充收藏区（固定槽位，只改文本与显隐——本项目不做事后插行）。</summary>
	internal static void RefreshFavArea()
	{
		try
		{
			int shown = 0;
			for (int i = 0; i < FavSlots; i++)
			{
				GameObject row = favRows[i];
				if (row == null || row.Equals(null))
				{
					continue;
				}
				if (i >= favorites.Count)
				{
					row.SetActive(false);
					continue;
				}
				string name = favorites[i];
				string label = name;
				// 有短名就用短名显示，与列表里的 mod 标题保持一致
				for (int k = 0; k < modBodies.Count; k++)
				{
					ModBody mb = modBodies[k];
					if (mb != null && mb.name == name && mb.plugin != null
						&& !string.IsNullOrEmpty(mb.plugin.shortName))
					{
						label = mb.plugin.shortName;
						break;
					}
				}
				if (favLabels[i] != null && !favLabels[i].Equals(null))
				{
					favLabels[i].text = "★  " + label;
				}
				row.SetActive(true);
				shown++;
			}
			if (favHeaderRow != null && !favHeaderRow.Equals(null))
			{
				favHeaderRow.SetActive(shown > 0);
			}
			// v1.7.1：**收尾自检**——收藏区曾经"占了版面却一个字都看不见"（标签矩形高 0），
			// 而当时的日志只记了收藏列表本身，看不出渲染问题。这里把矩形实测宽高与激活数打出来：
			// 标签高 0 / 宽 <= 0 就是那类缺陷的直接判据，不必再靠截图量像素。
			if (Plugin.DebugOn)
			{
				RectTransform lrt = (shown > 0 && favLabels[0] != null && !favLabels[0].Equals(null))
					? favLabels[0].GetComponent<RectTransform>() : null;
				RectTransform hrt2 = (favHeaderRow != null && !favHeaderRow.Equals(null))
					? favHeaderRow.GetComponent<RectTransform>() : null;
				Plugin.ModLog.LogInfo((object)("ModManager: 收藏区刷新 → 显示 " + shown + " 项；"
					+ "标题行 h=" + ((hrt2 != null) ? hrt2.rect.height.ToString("0") : "?")
					+ " active=" + ((favHeaderRow != null && !favHeaderRow.Equals(null)) ? favHeaderRow.activeSelf.ToString() : "?")
					+ "；首项标签 w=" + ((lrt != null) ? lrt.rect.width.ToString("0") : "-")
					+ " h=" + ((lrt != null) ? lrt.rect.height.ToString("0") : "-")));
			}
		}
		catch
		{
		}
	}

	/// <summary>v1.7.0：跳到第 idx 个收藏的 mod——展开它并把列表滚到它的标题行。</summary>
	private static void JumpToFav(int idx)
	{
		try
		{
			if (idx < 0 || idx >= favorites.Count)
			{
				return;
			}
			string name = favorites[idx];
			ModBody target = null;
			for (int i = 0; i < modBodies.Count; i++)
			{
				if (modBodies[i] != null && modBodies[i].name == name)
				{
					target = modBodies[i];
					break;
				}
			}
			if (target == null)
			{
				return;
			}
			// v1.7.2：走**与点击标题完全相同**的展开路径（原先只改标志，界面不变）
			SetModExpanded(target, true);
			// 先重排再定位（展开会改变高度）
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			if (s == null || s.contentPage == null)
			{
				return;
			}
			Canvas.ForceUpdateCanvases();
			UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(s.contentPage as RectTransform);
			SelfHealScroll(s.contentPage, true);
			if (target.titleRow != null && !target.titleRow.Equals(null))
			{
				RectTransform rt = target.titleRow.GetComponent<RectTransform>();
				if (rt != null)
				{
					ScrollToOffset(-rt.anchoredPosition.y, s.contentPage);
				}
			}
			PlayClick();
			// 动作日志（每次点击一条，低频）：用来判定"收藏条目的点击到底有没有到达"
			Plugin.ModLog.LogInfo((object)("ModManager: 收藏跳转 → '" + name + "'（已展开并滚到它，rows="
				+ target.rows.Count + "）"));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("ModManager: 收藏跳转失败: " + ex.Message);
		}
	}

	/// <summary>把列表滚到"容器内偏移 offset 处"（换算成 ScrollRect 的归一化位置）。</summary>
	private static void ScrollToOffset(float offset, Transform contentPage)
	{
		try
		{
			UnityEngine.UI.ScrollRect sr = FindScrollRect(contentPage);
			if (sr == null || sr.content == null)
			{
				return;
			}
			RectTransform vp = (sr.viewport != null) ? sr.viewport : sr.GetComponent<RectTransform>();
			float vpH = (vp != null) ? vp.rect.height : 0f;
			float cH = sr.content.rect.height;
			float span = cH - vpH;
			if (vpH <= 0f || span <= 0f)
			{
				return;
			}
			float want = offset - 60f; // 顶部留一点余量，别让目标正好贴边
			if (want < 0f)
			{
				want = 0f;
			}
			if (want > span)
			{
				want = span;
			}
			float vnp = 1f - (want / span);
			if (vnp < 0f)
			{
				vnp = 0f;
			}
			if (vnp > 1f)
			{
				vnp = 1f;
			}
			sr.verticalNormalizedPosition = vnp;
		}
		catch
		{
		}
	}


	/// <summary>热键冲突警告行（顶部黄色提示）。</summary>
	/// <summary>v1.7.8：热键冲突提示块 = **块头（计数 + 隐藏/显示按钮）+ 每组冲突一行（可收起）**。
	/// 玩家反馈的两点都在这：
	/// ① "即便有多个按键冲突也只会显示一个"——真因是**行高估算按 500px 行宽算**，而标签实际可用宽度
	///    只有 ~400px（行宽 425 − 标签左右各 12），文本溢出行底、多条警告叠成一坨（看着像一条）。
	///    现在行宽从 contentPage 实测（内容页宽 − 容器缩进 8 − 标签边距 24），取不到时退 380（宁可
	///    多留一行空白也不能让文本互相叠）。1.7.7 的"一键一行聚合"也已消除两两配对的重复行。
	/// ② "无法隐藏"——块头右侧的小按钮切换 `Ui / hotkeyWarnings`（写 cfg 并立即 Save），明细行
	///    原地 SetActive + 重排（不整页重建）。块头**始终显示**：隐藏了也能看到"共 N 组"。</summary>
	private static void AddHotkeyWarningBlock(Transform container, List<string> warns)
	{
		try
		{
			if (warns == null || warns.Count == 0)
			{
				return;
			}
			bool show = Plugin.hotkeyWarnings == null || Plugin.hotkeyWarnings.Value;
			GameObject head = new GameObject("MM_WarnHead");
			head.transform.SetParent(container, false);
			RectTransform hrt = head.AddComponent<RectTransform>();
			hrt.sizeDelta = new Vector2(0f, 24f);
			GameObject hgo = new GameObject("Label");
			hgo.transform.SetParent(head.transform, false);
			RectTransform htrt = hgo.AddComponent<RectTransform>();
			// 中线锚点 + 显式尺寸（硬机制 0 的 B 写法）：左 12px 起，右侧给按钮让位
			htrt.anchorMin = new Vector2(0f, 0.5f);
			htrt.anchorMax = new Vector2(1f, 0.5f);
			htrt.pivot = new Vector2(0.5f, 0.5f);
			float hl = 12f;
			float hr = 72f;
			htrt.sizeDelta = new Vector2(-(hl + hr), 18f);
			htrt.anchoredPosition = new Vector2((hl - hr) * 0.5f, 0f);
			Text htxt = hgo.AddComponent<Text>();
			Font hfont = GetNativeFont(SettingsGUI_V2.instance);
			if (hfont != null)
			{
				htxt.font = hfont;
			}
			htxt.fontSize = 14;
			htxt.fontStyle = FontStyle.Bold;
			htxt.color = new Color(1f, 0.78f, 0.25f, 1f);
			htxt.alignment = TextAnchor.MiddleLeft;
			htxt.horizontalOverflow = HorizontalWrapMode.Overflow;
			htxt.raycastTarget = false;
			htxt.text = Plugin.DefaultChinese
				? "⚠ 热键冲突 " + warns.Count + " 组"
				: "⚠ Hotkey conflicts: " + warns.Count;
			List<GameObject> blockRows = new List<GameObject>();
			foreach (string w in warns)
			{
				GameObject row = AddWarningRow(container, w);
				if (row != null)
				{
					row.SetActive(show);
					blockRows.Add(row);
				}
			}
			Text btnTxt = null;
			btnTxt = MakeSmallTextButton(head, new Vector2(1f, 0.5f), new Vector2(1f, 0.5f),
				new Vector2(-ControlRight, 0f), new Vector2(56f, 20f),
				show ? (Plugin.DefaultChinese ? "隐藏" : "Hide") : (Plugin.DefaultChinese ? "显示" : "Show"),
				delegate
				{
					try
					{
						bool now = !(Plugin.hotkeyWarnings != null && Plugin.hotkeyWarnings.Value);
						if (Plugin.hotkeyWarnings != null)
						{
							Plugin.hotkeyWarnings.Value = now;
							// BepInEx 不保证条目变更即时写盘 → 显式落盘（重启后保持玩家的选择）
							try
							{
								if (Plugin.OwnConfig != null)
								{
									Plugin.OwnConfig.Save();
								}
							}
							catch
							{
							}
						}
						foreach (GameObject r in blockRows)
						{
							if (r != null && !r.Equals(null))
							{
								r.SetActive(now);
							}
						}
						if (btnTxt != null && !btnTxt.Equals(null))
						{
							btnTxt.text = now ? (Plugin.DefaultChinese ? "隐藏" : "Hide")
								: (Plugin.DefaultChinese ? "显示" : "Show");
						}
						// 明细行显/隐改变了块高 → 与分区开合同一条重排链（不整页重建）
						RelayoutContainer(blockRows);
						PlayClick();
					}
					catch (Exception ex)
					{
						Plugin.ModLog.LogError((object)("ModManager hotkey warning toggle error: " + ex.Message));
					}
				});
		}
		catch
		{
		}
	}

	/// <summary>v1.7.8：一条冲突明细行（黄色粗体，可自动换行）。**返回行对象**（块头要登记它以便
	/// 隐藏/显示）。行高按"实际可用宽度"估：此前写死 500px，而标签实际只有 ~400px 可用
	/// → 行数估少 → 文本溢出行底、多条警告叠成一坨（玩家看到"只有一个冲突"）。</summary>
	private static GameObject AddWarningRow(Transform container, string text)
	{
		try
		{
			if (string.IsNullOrEmpty(text))
			{
				return null;
			}
			float width = 0f;
			foreach (char ch in text)
			{
				width += (ch > 127) ? 16f : 9f;
			}
			// v1.7.8：**按实际可用宽度估行数**。旧值 500px 是拍脑袋的——标签实际可用只有
			// "内容页宽 − 容器缩进 8 − 标签左右边距 24" ≈ 400px，行数被低估 → 文本溢出行底，
			// 多条警告叠成一坨（玩家："有多个冲突也只会显示一个"）。取不到实测值时退 380：
			// 比真实可用更窄 = 宁可多留一行空白，也不能让文本互相叠。
			float avail = 380f;
			try
			{
				RectTransform page = container.parent as RectTransform;
				if (page != null && page.rect.width > 60f)
				{
					avail = page.rect.width - 8f - 24f;
				}
			}
			catch
			{
			}
			int lines = Math.Max(1, (int)Math.Ceiling(width / Math.Max(200f, avail)));
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
			return row;
		}
		catch
		{
			return null;
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
					// v1.5.5：旧实现在这里 OpenMyPage 整页重建（= 白闪）。改为就地更新键位按钮文字；
					// 改动标记由 StageValue 内部的 RefreshEntryDirtyUi 统一刷新。
					if (btnTxt != null && !btnTxt.Equals(null))
					{
						btnTxt.text = HumanizeKey(noneVal.ToString());
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
			bbtn.targetGraphic = bt;
			// v1.7.2：悬停/按下要有反应（原先 Transition.None，划过毫无变化）
			ApplyButtonHoverTint(bbtn);
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
				if (Plugin.DebugOn)
				{
					Plugin.ModLog.LogInfo((object)("ModManager: normalized template row '" + go.name + "' -> h=" + h.ToString("F1")
						+ (stretched ? " (was vertically stretched)" : " (sizeDelta.y was ~0)")));
				}
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

		/// <summary>v1.5.17：插件 GUID（元信息行 / 复制导出用）。</summary>
		internal string guid;

		/// <summary>v1.5.17：插件版本字符串（元信息行 / 复制导出用）。</summary>
		internal string version;

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
				if (Plugin.DebugOn)
				{
					Plugin.ModLog.LogInfo((object)"ModManager: IL2CPPChainloader.Instance is null.");
				}
				return list;
			}
			Dictionary<string, PluginInfo> infos = loader.Plugins;
			if (infos == null)
			{
				if (Plugin.DebugOn)
				{
					Plugin.ModLog.LogInfo((object)"ModManager: PluginInfos is null.");
				}
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
					// v1.5.17：元信息（GUID / 版本）——mod 内容顶部显示，并带进"复制全部"的头部，
					// 玩家报 bug 时不必再去翻 BepInEx 日志确认是哪个 mod 的哪个版本。
					string guid = (pi.Metadata != null) ? pi.Metadata.GUID : kv.Key;
					string ver = "";
					try
					{
						// v1.5.17：**必须反射取版本**——`PluginInfo.Metadata.Version` 的类型是
						// BepInEx 的 `SemanticVersioning.Version`，其程序集本工程未引用（CS0012）；
						// 直接写 `pi.Metadata.Version.ToString()` 编译不过。GetValue 返回 object，
						// 不把该类型嵌进元数据，也就不需要引用。
						object md = pi.Metadata;
						if (md != null)
						{
							System.Reflection.PropertyInfo vp = md.GetType().GetProperty("Version");
							object vv = (vp != null) ? vp.GetValue(md) : null;
							if (vv != null)
							{
								ver = vv.ToString();
							}
						}
					}
					catch
					{
					}
					if (cfg == null || cfg.Count == 0)
					{
						// 无配置项的 mod 显示为只读"已安装"条目（cfg=null，展开显示"没有可调设置"）。
						list.Add(new PluginConfig { name = name, guid = guid, version = ver, cfg = null });
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
					list.Add(new PluginConfig { name = name, guid = guid, version = ver, cfg = cfg });
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
					// v1.5.8：这条分支也是整页重建，之前**没挂探针** → v1.5.7 的"没有重建"结论其实
					// 只覆盖了巡检那条路，证据不完整。补上，与巡检重建共用同一条日志与节流。
					ModRegistry.LogRebuild("session-reopen");
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
				// 只保留我们的容器，其余子物体每帧清掉（原生填充协程晚到时会把原生控件灌进我们的页）
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
											child.gameObject.SetActive(false); UnityEngine.Object.Destroy(child.gameObject); // v1.7.6：Destroy 帧末才生效，先停渲染（否则污染行会被画一帧 = 翻页闪一下）
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
					// v1.5.6 修正：光看绝对高度会**误杀短列表**。FillContent 里 content 高是
					// `Math.Max(200f, pref+8f)`，所以内容短时 prt.rect.height 恒等于 200 →
					// `height<=201` 恒成立；再叠加"容器≈初始高 101"，一个只有几行/几近全折叠的
					// 列表会被判成"布局死" → 每帧 OpenMyPage 重建 = 用户看到的残留白闪。
					// 正确判据是**需求与实际的落差**：只有 preferred 明显高于实际呈现的高度，
					// 才说明布局真没算；preferred 本身就小 = 内容是合法的短列表。
					string deadReason = null;
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
							if (ours.childCount == 0)
							{
								deadReason = "empty-container(rows=0)";
							}
							else
							{
								try
								{
									RectTransform ort = ours.GetComponent<RectTransform>();
									RectTransform prt = s.contentPage.GetComponent<RectTransform>();
									float ch2 = (ort != null) ? ort.rect.height : -1f;
									float ph2 = (prt != null) ? prt.rect.height : -1f;
									float pref = float.NaN;
									try
									{
										pref = UnityEngine.UI.LayoutUtility.GetPreferredHeight(ort);
									}
									catch
									{
									}
									bool shortList = (ch2 <= 101f && ph2 <= 201f);
									// 需求 > 实际 8px 以上才是"没算出来"，而不是"本来就这么短"
									bool lag = !float.IsNaN(pref) && pref > ch2 + 8f;
									if (shortList && lag)
									{
										deadReason = "layout-dead(containerH=" + ch2.ToString("0")
											+ " contentH=" + ph2.ToString("0")
											+ " preferred=" + pref.ToString("0") + ")";
									}
								}
								catch
								{
								}
							}
						}
					}
					else
					{
						deadReason = "container-missing";
					}
					if (deadReason != null)
					{
						hasOurs = false;
					}
					if (!hasOurs)
					{
						float now = Time.unscaledTime;
						// v1.5.6：整页重建 = 玩家看得见的白闪，属于故障级事件 → 日志**不受 debugLog
						// 限制**，但按"事件类日志低频"的要求做 5 秒节流（同一原因不刷屏）。
						ModRegistry.LogRebuild(deadReason);
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

/// <summary>v1.5.3：第三方"假页"式原生设置页共存桥（Advanced Combat Movement / Responsive Orders）。
/// 该 mod 在原生第 3 页劫持 SettingsTabRight 并**恒返回 false**，而它的 DLL 按字母序
/// （A < E）先于我们加载、同优先级下先执行 → 从最后一页往右翻永远停在它的页面上，
/// 我们的 MODS 页（追加在最末）翻不到（只剩"第一页往左翻绕回"这一条旁路）。
/// 桥接方式：我们的 TabRight 提到 Priority.First 先判定它的页面状态——
/// · 它的末页再右翻 → 接我们的 MODS 页；
/// · 它还没到末页 → 放行走它自己的翻页；
/// · 停在它的入口页（第 3 页）→ 让位，否则我们抢先进入 MODS 页、它的页永远打不开。
/// 全部反射（无编译期依赖），未安装时 Present=false，原有行为一字不变。</summary>
internal static class ThirdPartyPage
{
	private const string TypeName = "ResponsiveOrdersNativeSettingsPage";

	/// <summary>该 mod 在 SettingsTabRight 里写死的锚定原生页。</summary>
	internal const int AnchorPage = 3;
	/// <summary>它的假页序号：1=主设置页，2=续页（续页右翻它自己什么都不做）。</summary>
	internal const int LastFakePage = 2;

	private static bool probed;
	private static bool present;
	private static FieldInfo fiIsOpen;
	private static FieldInfo fiFakePage;

	private static void Probe()
	{
		probed = true;
		present = false;
		try
		{
			foreach (Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type t = asm.GetType(TypeName, false, false);
				if (t == null) continue;
				// 注意：这两个都是 **public static 字段**（不是属性），必须用 GetField。
				fiIsOpen = t.GetField("IsOpen", BindingFlags.Public | BindingFlags.Static);
				fiFakePage = t.GetField("CurrentFakePage", BindingFlags.Public | BindingFlags.Static);
				present = fiIsOpen != null && fiFakePage != null;
				if (present) Plugin.ModLog.LogInfo((object)("ModManager: third-party native settings page detected (" + TypeName + ") — tab chain shared."));
				break;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("ModManager third-party page probe error: " + ex.Message));
		}
	}

	internal static bool Present { get { if (!probed) Probe(); return present; } }

	internal static bool IsOpen
	{
		get
		{
			try { return Present && fiIsOpen != null && (bool)fiIsOpen.GetValue(null); }
			catch { return false; }
		}
	}

	/// <summary>它的假页序号（0 = 未知/未开）。</summary>
	internal static int FakePage
	{
		get
		{
			try { return (Present && fiFakePage != null) ? Convert.ToInt32(fiFakePage.GetValue(null)) : 0; }
			catch { return 0; }
		}
	}

	/// <summary>是否停在它的最后一个假页（再右翻应交给我们的 MODS 页）。</summary>
	internal static bool OnLastPage => IsOpen && FakePage >= LastFakePage;

	/// <summary>翻页权交还原生/交给我们时，清掉它的"我的假页还开着"状态。
	/// 不清会有两个后果：① 它的 TabLeft 会抢走我们从 MODS 页的左翻；
	/// ② 下次翻到它的入口页时我们误判"还在它的末页"，直接跳过它的页面。</summary>
	internal static void Detach()
	{
		try
		{
			if (!Present) return;
			fiIsOpen.SetValue(null, false);
			fiFakePage.SetValue(null, 0);
		}
		catch { }
	}
}

/// <summary>翻到最后一页（我们的 MODS 页）时接管填充；v1.1.4 在 MODS 页再右翻绕回第一页
///（与左翻绕回 MODS 页呼应，两个方向都能循环）。v1.5.3：与第三方假页共享翻页链。</summary>
[HarmonyPatch(typeof(SettingsGUI_V2), "SettingsTabRight")]
public class TabRightPatch
{
	[HarmonyPriority(Priority.First)]
	private static bool Prefix()
	{
		try
		{
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			int cur = SettingsGUI_V2.currentOpenedMenu;
			if (s != null && ModRegistry.myIndex > 0 && cur == ModRegistry.myIndex)
			{
				// 已是 MODS 页（末页）：右翻绕回第一页（走原生 UpdateOpenedMenu 恢复原生气/行为）
				ModRegistry.TabTrace("right:wrap-first [sound]"); // WrapToFirstPage 内含 ClickSound
				ModRegistry.WrapToFirstPage(s);
				ThirdPartyPage.Detach(); // 翻页链已交还原生，顺手清掉第三方残留的"我的页还开着"
				return false;
			}
			// v1.5.3：第三方假页链（见 ThirdPartyPage 注释）
			// v1.5.4：让位给它的两个分支里，它的 Prefix 会 return false 吞掉原生（原生音效随之
			// 消失），而它自己不播 → 必须在这里补一声，否则第 3 页→它的假页整段翻页无音效。
			if (ThirdPartyPage.Present)
			{
				if (ThirdPartyPage.IsOpen)
				{
					if (ThirdPartyPage.OnLastPage && s != null && ModRegistry.myIndex > 0)
					{
						ThirdPartyPage.Detach(); // 它的页面不再显示 → 交出翻页权
						ModRegistry.EnterMyPage(s); // 内含 ClickSound，这里不重复补
						return false;
					}
					ModRegistry.TabSound("right:yield-thirdparty-next"); // 它的假页 1→2，原生不执行
					return true; // 还没到它的末页 → 放行走它自己的翻页
				}
				// 停在它的入口页：让位（否则我们抢先接管，它的页面永远打不开）
				if (cur == ThirdPartyPage.AnchorPage)
				{
					ModRegistry.TabSound("right:yield-thirdparty-open"); // 打开它的首页，原生不执行
					return true;
				}
			}
			if (s == null || ModRegistry.myIndex <= 0)
			{
				return true;
			}
			if (cur + 1 == ModRegistry.myIndex)
			{
				ModRegistry.TabTrace("right:enter-mods [sound]"); // EnterMyPage 内含 ClickSound
				ModRegistry.EnterMyPage(s);
				return false;
			}
			ModRegistry.TabTrace("right:pass-native"); // 交给原生，音效由原生播
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
	// v1.5.3：必须排在第三方假页补丁之前——否则从 MODS 页左翻会被它的 TabLeft 抢走
	//（它还以为自己的页面开着，会关掉页面并把我们丢回原生第 3 页）。
	[HarmonyPriority(Priority.First)]
	private static bool Prefix()
	{
		try
		{
			SettingsGUI_V2 s = SettingsGUI_V2.instance;
			int cur = SettingsGUI_V2.currentOpenedMenu;
			if (ThirdPartyPage.Present && ThirdPartyPage.IsOpen && ModRegistry.myIndex > 0 && cur == ModRegistry.myIndex)
			{
				ThirdPartyPage.Detach(); // 从 MODS 页离开 → 摘掉它的残留状态，原生翻页照常
			}
			if (s == null || ModRegistry.myIndex <= 0)
			{
				return true;
			}
			// v1.5.8：**站在 MODS 页按左箭头必须自己接管**。
			// 旧逻辑在这里没有任何分支 → 掉到下面的 `left:pass-native` return true → 原生
			// `SettingsTabLeft` 拿着越界的 cur（原生只认识 0..myIndex-1，MODS 是我们追加的第 5 页）
			// 去执行左翻，于是原生按未知状态**重填整个设置面板** = 玩家看到的那一记白闪。
			// 日志实证：`ModManager: tab left:pass-native cur=4 myIndex=4`（每次离开 MODS 页都出现）。
			// 正确行为：左翻回到原生最后一页（myIndex-1），由我们用容错的 UpdateOpenedMenu 切换，
			// 并拦住原生（return false），与"最后一页右翻进入 MODS"完全对称。
			if (cur == ModRegistry.myIndex)
			{
				ModRegistry.TabTrace("left:leave-mods [sound]"); // LeaveModsBackward 内含 ClickSound
				ModRegistry.LeaveModsBackward(s);
				ThirdPartyPage.Detach(); // 翻页链已交还原生，清掉第三方残留的"我的页还开着"
				return false;
			}
			if (cur == ModRegistry.myIndex + 1)
			{
				ModRegistry.TabTrace("left:enter-mods [sound]"); // EnterMyPage 内含 ClickSound
				ModRegistry.EnterMyPage(s);
				return false;
			}
			if (cur == 0)
			{
				// 第一页左翻 → 绕回最后一页（MODS）
				ModRegistry.TabTrace("left:wrap-mods [sound]"); // EnterMyPage 内含 ClickSound
				ModRegistry.EnterMyPage(s);
				return false;
			}
			// v1.5.4：第三方假页还开着时，它的 TabLeft 会 return false 吞掉原生（含原生音效），
			// 而它自己不播 → 这里补一声；否则从它的假页往左翻全程静音。
			if (ThirdPartyPage.Present && ThirdPartyPage.IsOpen)
			{
				ModRegistry.TabSound("left:yield-thirdparty");
			}
			else
			{
				ModRegistry.TabTrace("left:pass-native"); // 交给原生，音效由原生播
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