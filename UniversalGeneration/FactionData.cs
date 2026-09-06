using System;
using System.Collections.Generic;

namespace ER2UniversalGeneration;

/// <summary>
/// 阵营表。阵营字符串 = Faction 枚举名（宿主 Friendly() 按 _allies/_axis 后缀判敌我，实证）。
/// 训练场等特殊战局 BattleData 双方可能同阵营/缺失 → 用后缀推导 + 本表兜底。
/// </summary>
internal static class FactionData
{
	internal static readonly string[] Allies =
	{
		"UnitedStates_allies", "England_allies", "Ussr_allies", "Poland_allies",
		"Italy_allies", "Germany_allies", "Japan_allies", "Australia_allies",
		"Canada_allies", "UnitedStates_Afrikan_allies"
	};

	internal static readonly string[] Axis =
	{
		"Germany_axis", "Japan_axis", "Italy_axis", "UnitedStates_axis",
		"England_axis", "Poland_axis", "Ussr_axis", "Australia_axis",
		"Canada_axis", "UnitedStates_Afrikan_axis"
	};

	/// <summary>给定阵营的对立侧代表（同国另一阵营）；未知阵营回落盟军↔轴心。</summary>
	public static string OppositeOf(string fac)
	{
		if (string.IsNullOrEmpty(fac)) return "Germany_axis";
		if (fac.EndsWith("_allies", StringComparison.Ordinal))
		{
			string nation = fac.Substring(0, fac.Length - "_allies".Length);
			string cand = nation + "_axis";
			foreach (string a in Axis) if (a == cand) return cand;
			return "Germany_axis";
		}
		if (fac.EndsWith("_axis", StringComparison.Ordinal))
		{
			string nation = fac.Substring(0, fac.Length - "_axis".Length);
			string cand = nation + "_allies";
			foreach (string a in Allies) if (a == cand) return cand;
			return "UnitedStates_allies";
		}
		return "Germany_axis";
	}

	/// <summary>是否友方（宿主 Friendly 同款后缀逻辑）。</summary>
	public static bool IsFriendly(string fac, string myFac)
	{
		if (string.IsNullOrEmpty(myFac) || string.IsNullOrEmpty(fac)) return false;
		if (string.Equals(myFac, fac, StringComparison.Ordinal)) return true;
		bool mA = myFac.EndsWith("_allies", StringComparison.Ordinal);
		bool mX = myFac.EndsWith("_axis", StringComparison.Ordinal);
		bool fA = fac.EndsWith("_allies", StringComparison.Ordinal);
		bool fX = fac.EndsWith("_axis", StringComparison.Ordinal);
		return (mA && fA) || (mX && fX);
	}

	/// <summary>显示名：去后缀（UnitedStates_allies → United States）。</summary>
	public static string DisplayName(string fac)
	{
		if (string.IsNullOrEmpty(fac)) return "?";
		string n = fac.Replace("_allies", "").Replace("_axis", "").Replace("_", " ");
		return n;
	}
}
