using System;
using Corvostudio.UI;
using UnityEngine;

namespace ER2LimbTweaks;

/// <summary>
/// 原生 UI 复用助手：让 mod 的屏幕元素与游戏原生 UI 观感一致。
/// 原理（反编译确认）：
///  - PhaseBarGUI.GetDefaultFont()  -> 游戏原生默认字体（可能退回引擎内置 LegacyRuntime）
///  - InteractionGUI2 / Hint / PlayerGUI 的 uGUI Text -> 战斗 HUD 实际使用的字体（首选来源）
///  - Corvostudio.UI.Hint.Display() -> 游戏原生提示弹窗（淡入淡出+排队）
///  - GuiExtension.OutlinedLabel()  -> 游戏自己的 IMGUI 描边文字
///  - Hint.fullColor / clearColor   -> 原生提示文字颜色
/// 回退链保证任何场景下都不崩：原生通道不可用 -> 原生字体+描边 -> 最简 GUI.Label。
/// 字体解析每个来源都打一次性日志，便于用 LogOutput.log 定位。
/// </summary>
internal static class NativeUi
{
	private static Font cachedFont;

	private static bool fontResolved;

	/// <summary>
	/// 解析游戏原生默认字体（只做一次，失败后每帧轻量重试）。
	/// 来源优先级：战斗 HUD 活体 uGUI Text（互动提示/提示弹窗/玩家 HUD）-> PhaseBarGUI.GetDefaultFont() -> GUI.skin。
	/// </summary>
	internal static Font GetNativeFont()
	{
		if (fontResolved)
		{
			return cachedFont;
		}
		fontResolved = true;
		// 1) 互动提示文字字体（战斗场景必然存在的 uGUI HUD 字体）
		try
		{
			InteractionGUI2 ig = InteractionGUI2.instance;
			if (ig != null && ig.inputDisplayer != null && ig.inputDisplayer.text != null && ig.inputDisplayer.text.font != null)
			{
				cachedFont = ig.inputDisplayer.text.font;
				Plugin.ModLog.LogInfo((object)("NativeUi: font source=InteractionGUI2.inputDisplayer.text -> '" + cachedFont.name + "'."));
			}
		}
		catch
		{
		}
		// 2) 原生提示弹窗文字字体
		if (cachedFont == null)
		{
			try
			{
				Hint h = Hint.instance;
				if (h != null && h.fadeText != null && h.fadeText.font != null)
				{
					cachedFont = h.fadeText.font;
					Plugin.ModLog.LogInfo((object)("NativeUi: font source=Hint.fadeText -> '" + cachedFont.name + "'."));
				}
			}
			catch
			{
			}
		}
		// 3) 玩家 HUD 武器弹药文字字体
		if (cachedFont == null)
		{
			try
			{
				PlayerGUI pg = PlayerGUI.instance;
				if (pg != null && pg.weapon_ammos != null && pg.weapon_ammos.font != null)
				{
					cachedFont = pg.weapon_ammos.font;
					Plugin.ModLog.LogInfo((object)("NativeUi: font source=PlayerGUI.weapon_ammos -> '" + cachedFont.name + "'."));
				}
			}
			catch
			{
			}
		}
		// 4) 引擎默认（兜底；当前游戏已移除 PhaseBarGUI）
		if (cachedFont == null)
		{
			try
			{
				cachedFont = GUI.skin.font;
			}
			catch
			{
			}
		}
		Plugin.ModLog.LogInfo((object)((cachedFont != null) ? ("NativeUi: using native font '" + cachedFont.name + "'.") : "NativeUi: no font found, using engine default."));
		return cachedFont;
	}

	/// <summary>原生提示文字颜色（Hint 弹窗的 fullColor），拿不到就白色。</summary>
	internal static Color NativeTextColor()
	{
		try
		{
			return Hint.fullColor;
		}
		catch
		{
			return Color.white;
		}
	}

	/// <summary>
	/// 建一个带原生字体的 GUIStyle。
	/// 注意：new GUIStyle() 的 normal.textColor 默认黑色，必须显式设颜色（踩坑记录）。
	/// </summary>
	internal static GUIStyle MakeStyle(int fontSize, FontStyle fontStyle, Color color, TextAnchor anchor, bool wordWrap = false)
	{
		GUIStyle style = new GUIStyle();
		Font f = GetNativeFont();
		if (f != null)
		{
			style.font = f;
		}
		style.fontSize = fontSize;
		style.fontStyle = fontStyle;
		style.alignment = anchor;
		style.wordWrap = wordWrap;
		style.normal.textColor = color;
		return style;
	}

	/// <summary>游戏原生描边文字；原生调用失败时退回普通 Label（不打断绘制）。</summary>
	internal static void OutlinedLabel(Rect rect, string text, GUIStyle style, int outlineSize = 1)
	{
		try
		{
			GuiExtension.OutlinedLabel(rect, text, style, outlineSize);
		}
		catch
		{
			try
			{
				GUI.Label(rect, text, style);
			}
			catch
			{
			}
		}
	}

	/// <summary>
	/// 走游戏原生提示弹窗通道（Corvostudio.UI.Hint.Display）。
	/// 返回 true 表示已交给原生 UI；false 表示原生通道不可用，调用方应使用 IMGUI 回退。
	/// 调用方必须在显示成功后清掉自己的重绘标记，避免每帧重复入队。
	/// </summary>
	internal static bool ShowNativeHint(string text, float duration)
	{
		try
		{
			Hint h = Hint.instance;
			if (h == null)
			{
				Plugin.ModLog.LogInfo((object)"NativeUi: Hint channel unavailable (Hint.instance == null), falling back to IMGUI.");
				return false;
			}
			Hint.Display(text, duration, true, true);
			Plugin.ModLog.LogInfo((object)("NativeUi: hint displayed via native Hint channel: " + text));
			return true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError((object)("NativeUi: native hint failed, falling back: " + ex.Message));
			return false;
		}
	}
}
