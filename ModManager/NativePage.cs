using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Corvostudio.SettingsData;
using BepInEx.Configuration;

namespace ER2ModManager;

// ─────────────────────────────────────────────────────────────
//  NativePage —— 原生 UI 重写（v2 完整版）
//  页面所有行均由原生 SettingSelectable 子类（Panel/Button/Toggle/
//  Slider/Dropdown）的 Create(contentPage, yShift) 渲染：
//  · 布局（yShift 累加）、控件实例化、样式 = 游戏原生代码
//  · 标签/内容文本 = Create 后覆写（text_id 变体 + 手动设 Text）
//  · 值变更 = 轮询（沿用 ControlWatch，绕开 UnityAction<bool> 桥接
//    false→true 翻转 bug）
//  · 按钮点击 = System.Action → UnityAction 隐式转换（已验证可用）
// ─────────────────────────────────────────────────────────────
internal static class NativePage
{
	private sealed class NativeRow
	{
		internal Transform root;
		internal Text label;
		internal Toggle toggle;
		internal Slider slider;
		internal Dropdown dropdown;
		internal Button button;
		internal Text contentText;
	}

	private static readonly List<NativeRow> rows = new List<NativeRow>();

	/// <summary>最近一次渲染产生的子物体数量（供剪除原生协程追加的冗余行）。</summary>
	internal static int renderedCount;

	/// <summary>剪除 contentPage 尾部超出我们渲染数量的子物体（原生填充协程可能追加行）。</summary>
	internal static void PruneExtras(Transform contentPage)
	{
		try
		{
			if (contentPage == null || renderedCount <= 0)
			{
				return;
			}
			for (int i = contentPage.childCount - 1; i >= renderedCount; i--)
			{
				Transform child = contentPage.GetChild(i);
				if (child != null)
				{
					UnityEngine.Object.Destroy(child.gameObject);
				}
			}
		}
		catch
		{
		}
	}

	// ── 完整原生页面（NativeFull=true）─────────────────────────
	internal static float BuildAndRender(Transform contentPage)
	{
		rows.Clear();
		ModRegistry.watches.Clear();
		float y = 0f;
		try
		{
			List<ModRegistry.PluginConfig> plugins = ModRegistry.CollectPlugins();
			if (contentPage == null || plugins.Count == 0)
			{
				SetContentHeight(contentPage, y);
				return y;
			}
			foreach (string warn in ModRegistry.DetectHotkeyConflicts(plugins))
			{
				AddHeaderRow(contentPage, ref y, warn);
			}
			List<ModRegistry.PluginConfig> hasSwitch = new List<ModRegistry.PluginConfig>();
			List<ModRegistry.PluginConfig> noSwitch = new List<ModRegistry.PluginConfig>();
			foreach (ModRegistry.PluginConfig p in plugins)
			{
				if (ModRegistry.HasMasterSwitch(p))
				{
					hasSwitch.Add(p);
				}
				else
				{
					noSwitch.Add(p);
				}
			}
			if (hasSwitch.Count > 0)
			{
				AddHeaderRow(contentPage, ref y, Plugin.DefaultChinese ? "── 有开关 mod ──" : "── With master switch ──");
				RenderMods(contentPage, ref y, hasSwitch);
			}
			if (noSwitch.Count > 0)
			{
				AddHeaderRow(contentPage, ref y, Plugin.DefaultChinese ? "── 无开关 mod ──" : "── Without master switch ──");
				RenderMods(contentPage, ref y, noSwitch);
			}
			AddHeaderRow(contentPage, ref y, Plugin.DefaultChinese ? "感谢你的使用，爱来自Zhaa_shi" : "Thanks for using, love from Zhaa_shi");
		}
		catch (Exception e)
		{
			Plugin.ModLog.LogError("[MM] native build error: " + e);
		}
		SetContentHeight(contentPage, y);
		renderedCount = contentPage != null ? contentPage.childCount : 0;
		return y;
	}

	private static void RenderMods(Transform cp, ref float y, List<ModRegistry.PluginConfig> list)
	{
		foreach (ModRegistry.PluginConfig p in list)
		{
			try
			{
				string name = p.name;
				bool expanded = ModRegistry.expandedMods.Contains(name);
				string disp = name;
				string mark = expanded ? "▾" : "▸";
				System.Action toggleAction = delegate { ModRegistry.ToggleMod(name); };
				AddButtonRow(cp, ref y, disp, mark, toggleAction);
				if (!expanded)
				{
					continue;
				}
				RenderEntries(cp, ref y, p);
				System.Action resetAction = delegate { ModRegistry.ResetModSettings(p.cfg, name); };
				System.Action copyAction = delegate { ModRegistry.CopyModText(name, p.cfg); };
				AddButtonRow(cp, ref y, "", Plugin.DefaultChinese ? "重置默认" : "Reset", resetAction);
				AddButtonRow(cp, ref y, "", Plugin.DefaultChinese ? "复制配置文本" : "Copy config", copyAction);
			}
			catch (Exception e)
			{
				Plugin.ModLog.LogError("[MM] native mod row error: " + e.Message);
			}
		}
	}

	private static void RenderEntries(Transform cp, ref float y, ModRegistry.PluginConfig p)
	{
		if (p.cfg == null || p.cfg.Count == 0)
		{
			AddHeaderRow(cp, ref y, Plugin.DefaultChinese ? "该 mod 没有可调设置" : "This mod has no configurable settings");
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
				bool expanded = ModRegistry.expandedSections.Contains(key);
				string mark = expanded ? "▾" : "▸";
				System.Action toggleAction = delegate { ModRegistry.ToggleSection(key); };
				AddButtonRow(cp, ref y, ModRegistry.HumanizeKey(sec), mark, toggleAction);
				if (!expanded)
				{
					continue;
				}
				foreach (ConfigEntryBase e in bySection[sec])
				{
					AddEntry(cp, ref y, p.cfg, e);
				}
			}
		}
		else
		{
			foreach (ConfigEntryBase e in entries)
			{
				AddEntry(cp, ref y, p.cfg, e);
			}
		}
	}

	// ── 单条配置 → 原生行 ──────────────────────────────────────
	private static void AddEntry(Transform cp, ref float y, ConfigFile cfg, ConfigEntryBase entry)
	{
		try
		{
			string label = ModRegistry.HumanizeKey(entry.Definition.Key);
			if (entry.SettingType == typeof(bool))
			{
				bool cur = (bool)ModRegistry.GetStaged(cfg, entry, entry.BoxedValue);
				System.Action<bool> noop = delegate { };
				AddToggleRow(cp, ref y, label, cur, noop, cfg, entry);
			}
			else if (entry.SettingType == typeof(float))
			{
				float min = 0f, max = 1000f;
				if (entry.Description != null && entry.Description.AcceptableValues is AcceptableValueRange<float> rng)
				{
					min = rng.MinValue;
					max = rng.MaxValue;
				}
				float cur = (float)ModRegistry.GetStaged(cfg, entry, entry.BoxedValue);
				System.Action<float> noop = delegate { };
				AddSliderRow(cp, ref y, label, cur, noop, min, max, false, cfg, entry);
			}
			else if (entry.SettingType == typeof(int))
			{
				float min = 0f, max = 1000f;
				if (entry.Description != null && entry.Description.AcceptableValues is AcceptableValueRange<int> irng)
				{
					min = irng.MinValue;
					max = irng.MaxValue;
				}
				float cur = (int)ModRegistry.GetStaged(cfg, entry, entry.BoxedValue);
				System.Action<float> noop = delegate { };
				AddSliderRow(cp, ref y, label, cur, noop, min, max, true, cfg, entry);
			}
			else if (entry.SettingType == typeof(KeyCode) || ModRegistry.IsHotkeyEntry(entry))
			{
				bool isKeyCode = entry.SettingType == typeof(KeyCode);
				string value = ModRegistry.HotkeyValue(entry);
				if (string.IsNullOrEmpty(value))
				{
					value = "None";
				}
				// 枚举热键（如 InputSystem Key）：捕获时传枚举类型，写入枚举值
				Type enumType = entry.SettingType.IsEnum ? entry.SettingType : null;
				NativeRow holder = null;
				System.Action click = delegate
				{
					if (holder != null)
					{
						ModRegistry.StartKeyCapture(cfg, entry, holder.contentText, isKeyCode, enumType);
					}
				};
				AddButtonRow(cp, ref y, label, value, click, delegate (NativeRow nr) { holder = nr; });
			}
			else
			{
				// string 枚举 / 其他：下拉（AcceptableValueList → 选项；否则当前值单选项）
				List<string> options = null;
				if (entry.Description != null && entry.Description.AcceptableValues is AcceptableValueList<string> lst &&
					lst.AcceptableValues != null && lst.AcceptableValues.Length > 0)
				{
					options = new List<string>(lst.AcceptableValues);
				}
				else if (entry.SettingType.IsEnum)
				{
					options = new List<string>(Enum.GetNames(entry.SettingType));
				}
				if (options == null)
				{
					options = new List<string> { entry.BoxedValue != null ? entry.BoxedValue.ToString() : "" };
				}
				string curStr = entry.BoxedValue != null ? entry.BoxedValue.ToString() : "";
				int startIdx = Math.Max(0, options.IndexOf(curStr));
				System.Action<int> noop = delegate { };
				AddDropdownRow(cp, ref y, label, options, startIdx, noop, cfg, entry);
			}
		}
		catch (Exception e)
		{
			Plugin.ModLog.LogError("[MM] native entry error: " + e.Message);
		}
	}

	// ── 行创建原语 ─────────────────────────────────────────────
	private static float CreateRow(Transform cp, float y, SettingSelectable row, Action<NativeRow> bind)
	{
		float h = row.Create(cp, y);
		NativeRow nr = new NativeRow();
		nr.root = cp.GetChild(cp.childCount - 1);
		BindControls(nr);
		if (bind != null)
		{
			bind(nr);
		}
		rows.Add(nr);
		if (Plugin.DebugOn)
		{
			Plugin.ModLog.LogInfo("[MM] native row " + row.GetType().Name + " h=" + h.ToString("F1"));
		}
		return y + h;
	}

	private static void BindControls(NativeRow nr)
	{
		if (nr.root == null)
		{
			return;
		}
		// 模板结构（与现有 MM 实证一致）：GetChild(0) = 标签 Text，GetChild(1) = 控件
		if (nr.root.childCount > 0)
		{
			nr.label = nr.root.GetChild(0).GetComponent<Text>();
			if (nr.root.childCount > 1)
			{
				Transform c1 = nr.root.GetChild(1);
				nr.toggle = c1.GetComponent<Toggle>();
				nr.slider = c1.GetComponent<Slider>();
				nr.dropdown = c1.GetComponent<Dropdown>();
				nr.button = c1.GetComponent<Button>();
			}
		}
		// 兜底：子树搜索
		if (nr.toggle == null)
		{
			nr.toggle = nr.root.GetComponentInChildren<Toggle>(true);
		}
		if (nr.slider == null)
		{
			nr.slider = nr.root.GetComponentInChildren<Slider>(true);
		}
		if (nr.dropdown == null)
		{
			nr.dropdown = nr.root.GetComponentInChildren<Dropdown>(true);
		}
		if (nr.button == null)
		{
			nr.button = nr.root.GetComponentInChildren<Button>(true);
		}
		if (nr.label == null)
		{
			Text[] texts = nr.root.GetComponentsInChildren<Text>(true);
			for (int i = 0; i < texts.Length; i++)
			{
				if (texts[i] != null)
				{
					nr.label = texts[i];
					break;
				}
			}
		}
		// 按钮内容文本：按钮子节点里的非标签 Text
		if (nr.button != null && nr.label != null)
		{
			Text[] texts = nr.button.GetComponentsInChildren<Text>(true);
			for (int i = 0; i < texts.Length; i++)
			{
				if (texts[i] != null && texts[i] != nr.label)
				{
					nr.contentText = texts[i];
					break;
				}
			}
			if (nr.contentText == null)
			{
				nr.contentText = nr.label;
			}
		}
	}

	// ── 标题/分隔行（原生 SettingPanel 的 prefab 加载失败 → 用已验证的 SettingButton）──
	private static void AddHeaderRow(Transform cp, ref float y, string text)
	{
		try
		{
			System.Action noop = delegate { };
			AddButtonRow(cp, ref y, text, "", noop);
		}
		catch (Exception e)
		{
			Plugin.ModLog.LogError("[MM] native header error: " + e.Message);
		}
	}

	private static void AddButtonRow(Transform cp, ref float y, string label, string content, System.Action click, Action<NativeRow> lateBind = null)
	{
		try
		{
			y = CreateRow(cp, y, new SettingButton(label, content, click), delegate (NativeRow nr)
			{
				if (nr.label != null)
				{
					nr.label.text = label;
				}
				if (nr.contentText != null)
				{
					nr.contentText.text = content;
				}
				if (lateBind != null)
				{
					lateBind(nr);
				}
			});
		}
		catch (Exception e)
		{
			Plugin.ModLog.LogError("[MM] native button error: " + e.Message);
		}
	}

	private static void AddToggleRow(Transform cp, ref float y, string label, bool start, System.Action<bool> cb,
		ConfigFile cfg, ConfigEntryBase entry)
	{
		try
		{
			y = CreateRow(cp, y, new SettingToggle(label, start, cb, false), delegate (NativeRow nr)
			{
				if (nr.label != null)
				{
					nr.label.text = label;
				}
				if (nr.toggle != null && cfg != null)
				{
					nr.toggle.isOn = start;
					ModRegistry.watches.Add(new ModRegistry.ControlWatch
					{
						toggle = nr.toggle,
						cfg = cfg,
						entry = entry,
						lastBool = start,
						hasBool = true
					});
				}
			});
		}
		catch (Exception e)
		{
			Plugin.ModLog.LogError("[MM] native toggle error: " + e.Message);
		}
	}

	private static void AddSliderRow(Transform cp, ref float y, string label, float start, System.Action<float> cb,
		float min, float max, bool wholeNumbers, ConfigFile cfg, ConfigEntryBase entry)
	{
		try
		{
			y = CreateRow(cp, y, new SettingSlider(label, start, cb, min, max, null, 1f, wholeNumbers), delegate (NativeRow nr)
			{
				if (nr.label != null)
				{
					nr.label.text = label;
				}
				if (nr.slider != null && cfg != null)
				{
					nr.slider.minValue = min;
					nr.slider.maxValue = max;
					nr.slider.wholeNumbers = wholeNumbers;
					nr.slider.value = start;
					ModRegistry.watches.Add(new ModRegistry.ControlWatch
					{
						slider = nr.slider,
						cfg = cfg,
						entry = entry,
						lastFloat = start,
						wholeNumbers = wholeNumbers,
						hasRange = true,
						rangeMin = min,
						rangeMax = max
					});
				}
			});
		}
		catch (Exception e)
		{
			Plugin.ModLog.LogError("[MM] native slider error: " + e.Message);
		}
	}

	private static void AddDropdownRow(Transform cp, ref float y, string label, List<string> options, int startIdx,
		System.Action<int> cb, ConfigFile cfg, ConfigEntryBase entry)
	{
		try
		{
			Il2CppSystem.Collections.Generic.List<Dropdown.OptionData> opts = new Il2CppSystem.Collections.Generic.List<Dropdown.OptionData>();
			foreach (string o in options)
			{
				opts.Add(new Dropdown.OptionData(ModRegistry.HumanizeKey(o)));
			}
			y = CreateRow(cp, y, new SettingDropdown(label, opts, startIdx, cb), delegate (NativeRow nr)
			{
				if (nr.label != null)
				{
					nr.label.text = label;
				}
				if (nr.dropdown != null && cfg != null)
				{
					nr.dropdown.ClearOptions();
					nr.dropdown.AddOptions(opts);
					nr.dropdown.value = Math.Max(0, Math.Min(opts.Count - 1, startIdx));
					ModRegistry.watches.Add(new ModRegistry.ControlWatch
					{
						dropdown = nr.dropdown,
						cfg = cfg,
						entry = entry,
						lastInt = startIdx
					});
				}
			});
		}
		catch (Exception e)
		{
			Plugin.ModLog.LogError("[MM] native dropdown error: " + e.Message);
		}
	}

	// ── 页面高度 / 诊断 ────────────────────────────────────────
	private static void SetContentHeight(Transform contentPage, float y)
	{
		try
		{
			if (contentPage == null)
			{
				return;
			}
			RectTransform rt = contentPage.GetComponent<RectTransform>();
			if (rt != null)
			{
				rt.sizeDelta = new Vector2(rt.sizeDelta.x, Math.Max(200f, y + 8f));
			}
		}
		catch
		{
		}
	}
}
