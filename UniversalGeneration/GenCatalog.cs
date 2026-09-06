using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 可生成条目（载具 prefab 或步兵小队类型）。
/// vehicle: prefab 名直接给 VehicleSpawner.vehiclePrefabID；squad: SquadType 枚举给 GetSquadLoadouts 枚举重载。
/// </summary>
internal class GenEntry
{
	public string Id;        // prefab 名或 SquadType 枚举名
	public string Title;     // UI 显示名
	public bool IsInfantry;  // true=步兵小队，false=载具
	public string Category;  // tanks / wheeled / planes / artillery / infantry
	public SquadType SType;  // 步兵条目专用

	public override string ToString() => Title ?? Id;
}

/// <summary>
/// 条目目录。载具清单来自游戏自带 StreamingAssets/CorvoBundles/*.manifest（磁盘解析，毫秒级）；
/// 步兵目录来自 SquadType 枚举逐成员运行时探测（GetSquadLoadouts 枚举重载 + CountLoadouts 校验，
/// 无效/无数据类型不列出——字符串 label 路线已废弃：游戏对无效 label 回落默认空小队）。
/// 都只在面板打开时加载一次，不放每帧 Tick。
/// </summary>
internal static class GenCatalog
{
	internal static List<GenEntry> tanks = new();
	internal static List<GenEntry> wheeled = new();
	internal static List<GenEntry> planes = new();
	internal static List<GenEntry> artillery = new();
	internal static List<GenEntry> infantry = new();
	internal static List<GenEntry> favorites = new();

	/// <summary>收藏 id（"v:Panther" / "i:usa_infantry"），持久化到 cfg。</summary>
	private static readonly List<string> favIds = new();

	internal static bool vehicleCatalogReady;
	internal static bool vehicleCatalogFailed;
	internal static bool infantryProbed;

	private static readonly Regex VehRegex = new Regex(
		@"-\s+Assets/.+Vehicles/(Tanks|Wheeled|Planes|Artillery)/(.+?)\.prefab",
		RegexOptions.IgnoreCase | RegexOptions.Compiled);

	// ================= 收藏 =================

	internal static void LoadFavorites()
	{
		try
		{
			favIds.Clear();
			string raw = Plugin.favorites.Value ?? "";
			foreach (string part in raw.Split(','))
			{
				string id = part.Trim();
				if (!string.IsNullOrEmpty(id)) favIds.Add(id);
			}
		}
		catch { }
	}

	internal static bool IsFav(string id) => favIds.Contains("v:" + id) || favIds.Contains("i:" + id);

	/// <summary>切换收藏并持久化。返回收藏后的状态。</summary>
	internal static bool ToggleFav(GenEntry e)
	{
		string fid = (e.IsInfantry ? "i:" : "v:") + e.Id;
		if (favIds.Contains(fid)) { favIds.Remove(fid); favorites.RemoveAll(x => x.Id == e.Id && x.IsInfantry == e.IsInfantry); }
		else
		{
			favIds.Add(fid);
			GenEntry copy = new GenEntry { Id = e.Id, Title = e.Title, IsInfantry = e.IsInfantry, Category = e.Category, SType = e.SType };
			favorites.RemoveAll(x => x.Id == e.Id && x.IsInfantry == e.IsInfantry);
			favorites.Add(copy);
		}
		SaveFavs();
		return favIds.Contains(fid);
	}

	/// <summary>把收藏 id 解析成条目（面板打开、目录就绪后调用）。</summary>
	internal static void RebuildFavorites()
	{
		favorites.Clear();
		foreach (string fid in favIds)
		{
			GenEntry e = ResolveFav(fid);
			if (e != null) favorites.Add(e);
		}
	}

	private static GenEntry ResolveFav(string fid)
	{
		int us = fid.IndexOf(':');
		if (us != 1) return null;
		string id = fid.Substring(2);
		if (fid[0] == 'i')
		{
			foreach (GenEntry e in infantry) if (e.Id == id) return e;
		}
		else
		{
			foreach (GenEntry e in tanks) if (e.Id == id) return e;
			foreach (GenEntry e in wheeled) if (e.Id == id) return e;
			foreach (GenEntry e in planes) if (e.Id == id) return e;
			foreach (GenEntry e in artillery) if (e.Id == id) return e;
		}
		return null;
	}

	private static void SaveFavs()
	{
		try { Plugin.favorites.Value = string.Join(",", favIds); } catch { }
	}

	// ================= 载具目录 =================

	/// <summary>确保载具清单已加载（读磁盘 manifest；失败不重试，日志一次）。</summary>
	public static void EnsureVehicleCatalog()
	{
		if (vehicleCatalogReady || vehicleCatalogFailed) return;
		try
		{
			string bundlesDir = Path.Combine(Application.dataPath, "StreamingAssets", "CorvoBundles");
			if (!Directory.Exists(bundlesDir))
			{
				vehicleCatalogFailed = true;
				Plugin.ModLog.LogError("[UniGen] 找不到 CorvoBundles 目录: " + bundlesDir);
				return;
			}

			HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
			foreach (string mf in Directory.GetFiles(bundlesDir, "*.manifest"))
			{
				try
				{
					foreach (string line in File.ReadLines(mf))
					{
						Match m = VehRegex.Match(line);
						if (!m.Success) continue;
						string folder = m.Groups[1].Value.ToLowerInvariant();
						string name = m.Groups[2].Value.Trim();
						if (string.IsNullOrEmpty(name) || !seen.Add(name)) continue;

						GenEntry e = new GenEntry
						{
							Id = name,
							Title = PrettyName(name),
							IsInfantry = false,
							Category = folder switch
							{
								"tanks" => "tanks",
								"wheeled" => "wheeled",
								"planes" => "planes",
								"artillery" => "artillery",
								_ => null
							}
						};
						if (e.Category == null) continue;
						switch (e.Category)
						{
							case "tanks": tanks.Add(e); break;
							case "wheeled": wheeled.Add(e); break;
							case "planes": planes.Add(e); break;
							case "artillery": artillery.Add(e); break;
						}
					}
				}
				catch (Exception exInner)
				{
					if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] manifest 跳过(" + Path.GetFileName(mf) + "): " + exInner.Message);
				}
			}

			SortBuckets();
			int total = tanks.Count + wheeled.Count + planes.Count + artillery.Count;
			if (total == 0)
			{
				vehicleCatalogFailed = true;
				Plugin.ModLog.LogError("[UniGen] manifest 解析到 0 个载具——正则或目录结构变化，需更新 VehRegex。");
				return;
			}
			vehicleCatalogReady = true;
			Plugin.ModLog.LogInfo("[UniGen] 载具目录就绪(来源=manifest): tanks=" + tanks.Count + " wheeled=" + wheeled.Count
				+ " planes=" + planes.Count + " artillery=" + artillery.Count);
		}
		catch (Exception ex)
		{
			vehicleCatalogFailed = true;
			Plugin.ModLog.LogError("[UniGen] 载具清单加载失败: " + ex);
		}
	}

	/// <summary>
	/// 步兵目录：SquadType 枚举逐成员探测（GetSquadLoadouts 枚举重载，CountLoadouts&gt;0 才有效）。
	/// 每战斗探测一次（面板打开时），约 60 次原生调用，毫秒级。
	/// </summary>
	public static void ProbeInfantryTypes()
	{
		if (infantryProbed) return;
		infantryProbed = true;
		infantry.Clear();
		int skipped = 0;
		try
		{
			foreach (SquadType t in Enum.GetValues(typeof(SquadType)))
			{
				if (t == SquadType.unarmed_tutorial) continue; // 教学空手类型，排除
				try
				{
					SquadData sd = ItemsDatabase.GetSquadLoadouts(t, 0);
					if (sd == null || sd.CountLoadouts() <= 0) { skipped++; continue; }
					infantry.Add(new GenEntry
					{
						Id = t.ToString(),
						Title = PrettyType(t.ToString()),
						IsInfantry = true,
						Category = "infantry",
						SType = t
					});
				}
				catch { skipped++; }
			}
			infantry.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
			Plugin.ModLog.LogInfo("[UniGen] 步兵类型探测完成: 有效 " + infantry.Count + " 个，跳过 " + skipped + " 个");
			if (Plugin.debugLog.Value)
				foreach (GenEntry e in infantry) Plugin.ModLog.LogInfo("[UniGen]   · " + e.Id);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("[UniGen] 步兵类型探测失败: " + ex);
		}
	}

	/// <summary>生成时二次校验：类型无效（返回空小队）则从目录移除。</summary>
	public static void RemoveInfantry(SquadType t)
	{
		infantry.RemoveAll(e => e.IsInfantry && e.SType == t);
	}

	/// <summary>按阵营（Faction 枚举名字符串）映射专用坦克乘员 SquadType。</summary>
	public static SquadType? TankCrewTypeFor(string faction)
	{
		if (string.IsNullOrEmpty(faction)) return null;
		string nation = faction;
		int us = faction.IndexOf('_');
		if (us > 0) nation = faction.Substring(0, us);
		switch (nation)
		{
			case "Germany": return SquadType.ger_tankCrew;
			case "UnitedStates": return SquadType.usa_tankCrew;
			case "England": return SquadType.eng_tankCrew;
			case "Ussr": return SquadType.rus_tankCrew;
			case "Japan": return SquadType.jap_tankCrew;
			case "Italy": return SquadType.ita_tankCrew;
			case "Poland": return SquadType.eng_pol_tankCrew;
			case "Australia": return SquadType.aus_tankCrew;
			case "Canada": return SquadType.can_tankCrew;
			default: return null;
		}
	}

	private static void SortBuckets()
	{
		tanks.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
		wheeled.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
		planes.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
		artillery.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>把超长 id 截断（列表行高有限，陷阱 19）。</summary>
	private static string PrettyName(string id)
	{
		if (string.IsNullOrEmpty(id)) return id;
		return id.Length > 34 ? id.Substring(0, 33) + "…" : id;
	}

	/// <summary>SquadType 枚举名 → 可读名（"usa_marine_AT" → "USA Marine AT"）。</summary>
	private static string PrettyType(string enumName)
	{
		if (string.IsNullOrEmpty(enumName)) return enumName;
		string[] parts = enumName.Split('_');
		for (int i = 0; i < parts.Length; i++)
		{
			string p = parts[i];
			if (p.Length == 0) continue;
			if (i == 0) parts[i] = p.ToUpperInvariant(); // 国别码大写：usa → USA
			else parts[i] = char.ToUpperInvariant(p[0]) + p.Substring(1);
		}
		string s = string.Join(" ", parts);
		return s.Length > 34 ? s.Substring(0, 33) + "…" : s;
	}

	/// <summary>当前类别对应的条目列表。</summary>
	public static List<GenEntry> GetBucket(string cat)
	{
		switch (cat)
		{
			case "tanks": return tanks;
			case "wheeled": return wheeled;
			case "planes": return planes;
			case "artillery": return artillery;
			case "infantry": return infantry;
			case "favorites": return favorites;
			default: return tanks;
		}
	}
}
