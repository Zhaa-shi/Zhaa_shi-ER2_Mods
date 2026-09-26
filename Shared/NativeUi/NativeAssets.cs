using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2Shared.NativeUi
{
	/// <summary>
	/// 原生资源访问层：把游戏的字体 / 本地化 / 阵营旗帜 / IMGUI 样式集中到一处。
	///
	/// 全部走**原生 API**（M0/M1 侦察已确认存在）：
	///   · <c>Language.GetText(id)</c>          —— 游戏本地化（如 anzio_battle → 安齐奥战役）
	///   · <c>Language.GetBattleName(id)</c>    —— 战斗名本地化
	///   · <c>ResourcesManager.GetFactionData(faction)</c> → <c>FactionDetails.flag</c>（Sprite）
	///   · <c>ResourcesManager.GetGUISTyle(anchor, size, color, style)</c> —— **游戏自己的 IMGUI 样式**
	///   · <c>LocalizationManager.GetFont(TextMode)</c> —— 原生字体
	///
	/// 每一项都有 try/catch + 降级，原生拿不到时退到自绘（颜色点 / 默认字体），
	/// 保证"没装/没就绪"也不会让征服页崩掉。
	/// </summary>
	public static class NativeAssets
	{
		// ================= 字体 =================

		private static Font _font;
		private static bool _fontTried;

		public static Font Font
		{
			get
			{
				if (_fontTried) return _font;
				_fontTried = true;

				// ① 游戏本地化管理器的字体（最原生）
				try
				{
					Font f = LocalizationManager.GetFont(false);
					if (f != null) { _font = f; NativeUiLog.Inf("字体取自 LocalizationManager: '" + f.name + "'"); return _font; }
				}
				catch { }

				// ② 活体 uGUI Text 的字体
				try
				{
					UnityEngine.UI.Text[] texts = UnityEngine.Object.FindObjectsOfType<UnityEngine.UI.Text>();
					if (texts != null)
					{
						for (int i = 0; i < texts.Length; i++)
						{
							try
							{
								if (texts[i] != null && texts[i].font != null)
								{
									_font = texts[i].font;
									NativeUiLog.Inf("字体取自活体 uGUI Text: '" + _font.name + "'");
									return _font;
								}
							}
							catch { }
						}
					}
				}
				catch { }

				// ③ GUI.skin
				try
				{
					if (GUI.skin != null && GUI.skin.font != null) { _font = GUI.skin.font; return _font; }
				}
				catch { }

				// ④ 内置
				try { _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
				return _font;
			}
		}

		// ================= 本地化 =================

		private static readonly Dictionary<string, string> _textCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>取本地化文本；找不到时返回 null（调用方自己决定回退显示什么）。</summary>
		public static string Text(string id)
		{
			if (string.IsNullOrEmpty(id)) return null;
			string cached;
			if (_textCache.TryGetValue(id, out cached)) return cached;

			string result = null;
			try
			{
				result = Language.GetText(id);
				if (!string.IsNullOrEmpty(result) && string.Equals(result, id, StringComparison.OrdinalIgnoreCase))
					result = null;   // 游戏在查不到时会原样返回 id —— 视为"没翻译"
			}
			catch { result = null; }

			_textCache[id] = result;
			return result;
		}

		/// <summary>战区名（原生战役 id → 本地化名，如 anzio_battle → 安齐奥战役）。</summary>
		public static string CampaignName(string campaignId, string fallback)
		{
			string t = Text(campaignId);
			if (!string.IsNullOrEmpty(t)) return t;
			if (!string.IsNullOrEmpty(fallback)) return fallback;
			return campaignId;
		}

		/// <summary>战斗/省份名（优先本地化 id，其次原生 location_name）。</summary>
		public static string BattleName(string nameId, string fallback)
		{
			if (!string.IsNullOrEmpty(nameId))
			{
				try
				{
					string t = Language.GetBattleName(nameId);
					if (!string.IsNullOrEmpty(t) && !string.Equals(t, nameId, StringComparison.OrdinalIgnoreCase)) return t;
				}
				catch { }
				string t2 = Text(nameId);
				if (!string.IsNullOrEmpty(t2)) return t2;
			}
			return string.IsNullOrEmpty(fallback) ? nameId : fallback;
		}

		// ================= 阵营旗帜 =================

		private static readonly Dictionary<string, Sprite> _flags = new Dictionary<string, Sprite>(StringComparer.OrdinalIgnoreCase);
		private static readonly HashSet<string> _flagMissed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>阵营旗帜 Sprite（原生资源）。拿不到返回 null。</summary>
		public static Sprite Flag(string faction)
		{
			if (string.IsNullOrEmpty(faction)) return null;
			Sprite s;
			if (_flags.TryGetValue(faction, out s)) return s;
			if (_flagMissed.Contains(faction)) return null;

			try
			{
				FactionDetails fd = ResourcesManager.GetFactionData(faction);
				if (fd != null && fd.flag != null)
				{
					_flags[faction] = fd.flag;
					return fd.flag;
				}
			}
			catch { }

			_flagMissed.Add(faction);
			return null;
		}

		/// <summary>
		/// 阵营可读名。
		///
		/// ⚠️ 实测踩坑（2026-09-13）：**不要用 `FactionDetails.names`** ——
		/// 那是**士兵名字**数组（用户实测界面显示成了 "George"/"Hans"），不是阵营名。
		/// 本地化文件里也没有阵营名的 key（已核对 Translations/*.xml）。
		/// 所以：先试本地化（万一将来有），再按 id 做**驼峰分词**（UnitedStates_allies → United States）。
		/// </summary>
		public static string FactionDisplayName(string faction)
		{
			if (string.IsNullOrEmpty(faction)) return "中立";

			string t = Text(faction);
			if (!string.IsNullOrEmpty(t)) return t;

			// 去掉 "_allies"/"_axis" 之类的后缀（那是阵营归属，不是国名）
			string nation = faction;
			int us = nation.IndexOf('_');
			if (us > 0) nation = nation.Substring(0, us);

			return SplitCamelCase(nation);
		}

		/// <summary>驼峰分词：UnitedStates → United States；SovietUnion → Soviet Union。</summary>
		private static string SplitCamelCase(string s)
		{
			if (string.IsNullOrEmpty(s)) return s;
			System.Text.StringBuilder sb = new System.Text.StringBuilder(s.Length + 4);
			for (int i = 0; i < s.Length; i++)
			{
				char ch = s[i];
				if (i > 0 && char.IsUpper(ch) && !char.IsUpper(s[i - 1])) sb.Append(' ');
				sb.Append(ch);
			}
			return sb.ToString();
		}

		// ================= 原生 IMGUI 样式 =================

		private static readonly Dictionary<string, GUIStyle> _nativeStyles = new Dictionary<string, GUIStyle>();

		/// <summary>
		/// 游戏自己的 IMGUI 样式（`ResourcesManager.GetGUISTyle`）。
		/// 拿不到时返回 null —— 调用方退回自建样式。
		/// </summary>
		public static GUIStyle NativeStyle(TextAnchor anchor, int size, Color color, FontStyle fs = FontStyle.Normal)
		{
			string key = (int)anchor + "|" + size + "|" + (int)(color.r * 255) + "_" + (int)(color.g * 255) + "_" + (int)(color.b * 255) + "_" + (int)(color.a * 255) + "|" + (int)fs;
			GUIStyle s;
			if (_nativeStyles.TryGetValue(key, out s) && s != null) return s;

			try
			{
				GUIStyle g = ResourcesManager.GetGUISTyle(anchor, size, color, fs);
				if (g != null)
				{
					_nativeStyles[key] = g;
					return g;
				}
			}
			catch { }
			return null;
		}

		// ================= 日期本地化 =================

		private static readonly string[] CnMonths =
		{ "", "一月", "二月", "三月", "四月", "五月", "六月", "七月", "八月", "九月", "十月", "十一月", "十二月" };

		private static readonly string[] EnMonths =
		{ "", "Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec" };

		/// <summary>
		/// 战斗日期串，模仿原生卡片的"12 五月 1945"排版。
		/// 月份名用本地化（中文构建给"五月"，英文构建给"May"）。
		/// </summary>
		public static string DateText(int year, int month, int day)
		{
			if (year <= 0) return "";
			string m;
			if (NativeUiConfig.Chinese)
				m = (month >= 1 && month <= 12) ? CnMonths[month] : month.ToString();
			else
				m = (month >= 1 && month <= 12) ? EnMonths[month] : month.ToString();

			if (day <= 0) return m + " " + year;
			return day + " " + m + " " + year;
		}

		/// <summary>诊断串（写日志/自检报告，确认原生资源到底接上没有）。</summary>
		public static string Describe()
		{
			int flags = _flags.Count;
			bool font = Font != null;
			string sample = Text("anzio_battle");
			return "原生资源：字体=" + (font ? "'" + Font.name + "'" : "无")
				+ "　本地化=" + (sample != null ? "'" + sample + "'" : "不可用")
				+ "　已取旗帜=" + flags + " 个";
		}
	}
}
