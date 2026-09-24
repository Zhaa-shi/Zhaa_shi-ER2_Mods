using System;
using System.Collections;
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
	public string Id;        // prefab 名或 SquadType 枚举名（小队库 key）
	public string Title;     // UI 显示名
	public bool IsInfantry;  // true=步兵小队，false=载具
	public bool SpawnByKey;  // 1.2.1：步兵条目按字符串 key 生成（小队库 key），否则按 SType 枚举
	public string Category;  // tanks / wheeled / planes / artillery / mgs / infantry
	public SquadType SType;  // 步兵条目专用（按 key 生成的条目无意义）

	public override string ToString() => Title ?? Id;
}

/// <summary>
/// 条目目录。载具清单来自游戏自带 StreamingAssets/CorvoBundles/*.manifest（磁盘解析，毫秒级）；
/// 步兵目录 = ① SquadType 枚举官方班型 + ② SquadsArchive.squads 全量 key（游戏完整班型库，
/// 含 466 个季节/战场变体）。
/// （第三方类目已按用户决定整体移除：1.3.1 砍场上自定义班——运行时空壳；1.3.2 砍 mod 载具枚举。）
/// ①②④ 在**游戏启动时**后台分帧完成，每帧时间片 3ms；场景切换杀死协程后由
/// ProbeWatchdog 自动重启续跑（去重 guard 幂等）。
/// </summary>
internal static class GenCatalog
{
	internal static List<GenEntry> tanks = new();
	internal static List<GenEntry> wheeled = new();
	internal static List<GenEntry> planes = new();
	internal static List<GenEntry> artillery = new();
	internal static List<GenEntry> mgs = new();       // 1.1.0：火力点（Vehicles/MGs/ 固定机枪，游戏全部 32 个 prefab）
	internal static List<GenEntry> infantry = new();
	internal static List<GenEntry> favorites = new();

	/// <summary>乘员班型选择池（仅官方枚举班型；扩展/自定义班不作乘员来源）。</summary>
	internal static readonly List<GenEntry> crewPool = new();

	/// <summary>收藏 id（"v:Panther" / "i:usa_infantry"），持久化到 cfg。</summary>
	private static readonly List<string> favIds = new();

	internal static bool vehicleCatalogReady;
	internal static bool vehicleCatalogFailed;

	/// <summary>启动探测状态：0=未开始 1=协程进行中 2=完成。</summary>
	private static int probeState;
	private static float probeStartedAt;
	private static bool loggedDbWait;
	private static int probeRestarts;
	private static bool probeGiveUp;

	private static readonly Regex VehRegex = new Regex(
		@"-\s+Assets/.+Vehicles/(Tanks|Wheeled|Planes|Artillery|MGs)/(.+?)\.prefab",
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
			foreach (GenEntry e in mgs) if (e.Id == id) return e;
			foreach (GenEntry e in ModCatalog.vehicles) if (e.Id == id) return e; // 2.2.0：mod 载具收藏
		}
		return null;
	}

	/// <summary>
	/// 2.1.0：**唯一写入点**（单位项 + 物品项一起写）。
	/// 物品收藏由 `ItemCatalog` 持有，但其持久化也走这里——两个模块各写一次会互相覆盖。
	/// 注意 `favIds` 里混有物品项（"t:xxx"，LoadFavorites 时整串收进来的），
	/// 写回时用 `ItemCatalog.FavIdsPrefixed()` 覆盖，避免残留已取消收藏的旧项。
	/// </summary>
	internal static void SaveFavs()
	{
		try
		{
			var all = new List<string>();
			foreach (string f in favIds)
				if (!f.StartsWith("t:", StringComparison.Ordinal)) all.Add(f);
			all.AddRange(ItemCatalog.FavIdsPrefixed());
			Plugin.favorites.Value = string.Join(",", all);
		}
		catch { }
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
								"mgs" => "mgs",
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
							case "mgs": mgs.Add(e); break;
						}
					}
				}
				catch (Exception exInner)
				{
					if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] manifest 跳过(" + Path.GetFileName(mf) + "): " + exInner.Message);
				}
			}

			SortBuckets();
			int total = tanks.Count + wheeled.Count + planes.Count + artillery.Count + mgs.Count;
			if (total == 0)
			{
				vehicleCatalogFailed = true;
				Plugin.ModLog.LogError("[UniGen] manifest 解析到 0 个载具——正则或目录结构变化，需更新 VehRegex。");
				return;
			}
			vehicleCatalogReady = true;
			Plugin.ModLog.LogInfo("[UniGen] 载具目录就绪(来源=manifest): tanks=" + tanks.Count + " wheeled=" + wheeled.Count
				+ " planes=" + planes.Count + " artillery=" + artillery.Count + " mgs(火力点)=" + mgs.Count);
		}
		catch (Exception ex)
		{
			vehicleCatalogFailed = true;
			Plugin.ModLog.LogError("[UniGen] 载具清单加载失败: " + ex);
		}
	}

	// ================= 探测（1.3.0：游戏启动时后台分帧完成，面板打开零负担）=================
	// 1.3.1：看门狗自动重启（场景切换杀死协程）；场上自定义班捕获已砍（运行时实例是空壳）。

	/// <summary>Plugin.Load（主菜单）调用：官方班型 + 小队库 + mod 载具在后台分帧探测。
	/// 主菜单时物品数据库可能未就绪 → 每 2s 自动重试。面板打开时零工作量。
	/// 重启安全：不清列表（条目去重 guard 保证幂等），被场景切换杀死后由看门狗续跑。</summary>
	public static void BeginStartupProbe()
	{
		if (probeState != 0) return;
		probeState = 1;
		probeStartedAt = Time.unscaledTime;
		GenRunner.StartCoroutine(StartupProbeCR());
	}

	/// <summary>
	/// 探测看门狗（GenDriver.Tick 每秒调一次）。**实测定案（1.3.0 日志）**：
	/// ① 场上自定义班的运行时实例是**空壳**（4 个出生点全带 custom_squad 但 members 合计 0，
	/// 成员数据不挂在 SpawnManager 上）→ 自定义班生成**不可实现，功能已砍**（1.3.1）；
	/// ② 注入的协程宿主在场景切换时会被 Unity 连 DontDestroyOnLoad 一起清掉，探测协程随之死亡
	/// → probeState 复位由本看门狗自动重启（重启利用条目去重幂等，战斗场景内一次即可跑完）。
	/// </summary>
	public static void ProbeWatchdog()
	{
		if (probeGiveUp) return;
		if (probeState == 1 && Time.unscaledTime - probeStartedAt > 20f)
		{
			// 协程被场景切换杀死（20s 无进展即判死；正常全程 <5s）→ 复位重启
			probeRestarts++;
			if (probeRestarts > 6)
			{
				probeGiveUp = true;
				Plugin.ModLog.LogError("[UniGen] 启动探测反复中断（" + probeRestarts + " 次），已放弃自动重启（步兵 " + infantry.Count + " 条）");
				return;
			}
			Plugin.ModLog.LogWarning("[UniGen] 探测协程被场景切换中断（第 " + probeRestarts + " 次），自动重启补齐（已有步兵 " + infantry.Count + " 条）");
			probeState = 0;
		}
		if (probeState == 0) BeginStartupProbe();
	}

	private static IEnumerator StartupProbeCR()
	{
		System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
		long budgetUntil = 0; // 本帧允许的处理截止时刻（ms）——时间片切片，任何帧最多干 3ms
		int skipped = 0;
		int enumCount = 0;
		int libCount = 0;
		bool failed = false;

		// ① 官方 SquadType 枚举（55 个"建班型"原生调用——面板卡顿主源之一，逐项切片）。
		// 主菜单时物品数据库可能未就绪（0 个有效）→ 每 2s 重试，最多 ~2 分钟。
		// （收集与切片分离：yield 不能落在带 catch 的 try 里）
		Array squadTypes = null;
		try { squadTypes = Enum.GetValues(typeof(SquadType)); }
		catch (Exception exGet) { Plugin.ModLog.LogError("[UniGen] SquadType 枚举不可用: " + exGet); failed = true; }
		if (squadTypes != null && !failed)
		{
			for (int attempt = 0; attempt < 60; attempt++)
			{
				skipped = 0;
				foreach (SquadType t in squadTypes)
				{
					if (t == SquadType.unarmed_tutorial) continue; // 教学空手类型，排除
					try
					{
						SquadData sd = ItemsDatabase.GetSquadLoadouts(t, 0);
						if (sd == null || sd.CountLoadouts() <= 0) { skipped++; continue; }
						if (infantry.Exists(x => x.Id == t.ToString())) continue; // 重试不重复
						GenEntry e = new GenEntry
						{
							Id = t.ToString(),
							Title = PrettyType(t.ToString()),
							IsInfantry = true,
							Category = "infantry",
							SType = t
						};
						infantry.Add(e);
						crewPool.Add(e); // 乘员池只收官方枚举班型
					}
					catch { skipped++; }
					if (sw.ElapsedMilliseconds >= budgetUntil) { yield return null; budgetUntil = sw.ElapsedMilliseconds + 3; }
				}
				enumCount = infantry.Count;
				if (enumCount > 0) break; // 数据库就绪
				if (!loggedDbWait)
				{
					loggedDbWait = true;
					Plugin.ModLog.LogInfo("[UniGen] 物品数据库未就绪，2s 后重试（主菜单后台探测）…");
				}
				yield return new WaitForSeconds(2f);
			}
		}
		long tEnum = sw.ElapsedMilliseconds;
		if (enumCount == 0 && !failed)
		{
			Plugin.ModLog.LogError("[UniGen] 后台探测放弃：物品数据库 2 分钟内未就绪");
			probeState = 0;
			probeGiveUp = true; // 不再自动重启（面板打开也没有可补的数据）
			yield break;
		}
		yield return null;

		// ② 小队库（SquadsArchive.squads）全量 key = 游戏完整班型数据库（466 个非枚举 key：
		// 季节/战场变体如 win/dday/early）。只读表元数据（label + DLC 解锁），**绝不逐 key
		// 建班**——1.2.0 的 466 次 GetSquadLoadouts = 主线程卡死数秒（用户实测）。
		// 生成有效性校验在生成时做（失败自动移除）。
		List<string> libKeys = null;
		try
		{
			var dict = SquadsArchive.squads;
			if (dict != null)
			{
				libKeys = new List<string>();
				foreach (string k in dict.Keys) if (!string.IsNullOrEmpty(k)) libKeys.Add(k);
			}
		}
		catch (Exception exArc)
		{
			if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] 小队库枚举失败: " + exArc.Message);
		}
		if (libKeys != null)
		{
			foreach (string key in libKeys)
			{
				try
				{
					if (infantry.Exists(x => x.Id == key)) continue; // 枚举名已覆盖
					var dict2 = SquadsArchive.squads;
					SquadDataTable tbl = dict2 != null ? dict2[key] : null;
					if (tbl == null) continue;
					bool unlocked = true;
					try { unlocked = tbl.IsUnlockedOnThisBranch(); } catch { }
					if (!unlocked) continue; // 未拥有 DLC 的班型不列（生成时也会失败）
					string label = null;
					try { label = tbl.squadTypeLabel; } catch { }
					infantry.Add(new GenEntry
					{
						Id = key,
						Title = string.IsNullOrEmpty(label) ? PrettyType(key) : label,
						IsInfantry = true,
						Category = "infantry",
						SpawnByKey = true
					});
					libCount++;
				}
				catch { }
				if (sw.ElapsedMilliseconds >= budgetUntil) { yield return null; budgetUntil = sw.ElapsedMilliseconds + 3; }
			}
		}
		long tLib = sw.ElapsedMilliseconds;
		yield return null;

		// ③④ 已按用户决定移除：1.3.1 砍场上自定义班（运行时 CustomSquad 是空壳，无法生成）；
		// 1.3.2 砍第三方/mod 载具枚举——本 mod 只生成官方内容。
		long tMods = sw.ElapsedMilliseconds;

		infantry.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
		probeState = 2;
		if (failed)
		{
			Plugin.ModLog.LogError("[UniGen] 后台探测部分失败（官方枚举异常），耗时 " + tMods + "ms");
			yield break;
		}
		Plugin.ModLog.LogInfo("[UniGen] 启动探测完成: 官方枚举 " + enumCount + " + 小队库扩展 " + libCount + "（跳过 " + skipped
			+ "）；耗时 枚举 " + tEnum + "ms / 小队库 " + (tLib - tEnum) + "ms / 收尾 " + (tMods - tLib) + "ms");
		if (Plugin.debugLog.Value)
		{
			foreach (GenEntry e in infantry) Plugin.ModLog.LogInfo("[UniGen]   · " + e.Id + (e.SpawnByKey ? " [lib]" : ""));
		}
	}

	/// <summary>生成时二次校验：条目无效（返回空小队）则从目录移除。按 Id 移除，兼容库 key 与自定义班。</summary>
	public static void RemoveInfantryEntry(GenEntry e)
	{
		if (e == null) return;
		infantry.RemoveAll(x => x.IsInfantry && x.Id == e.Id);
		crewPool.RemoveAll(x => x.Id == e.Id);
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
		mgs.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>把超长 id 截断（列表行高有限，陷阱 19）。</summary>
	private static string PrettyName(string id)
	{
		if (string.IsNullOrEmpty(id)) return id;
		return id.Length > 34 ? id.Substring(0, 33) + "…" : id;
	}

	/// <summary>2.2.0：ModCatalog 用的公开包装。</summary>
	internal static string PrettyNamePublic(string id) => PrettyName(id);

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
			case "mgs": return mgs;
			case "infantry": return infantry;
			case "favorites": return favorites;
			case "modveh": return ModCatalog.vehicles; // 2.2.0：mod 载具
			default: return tanks;
		}
	}
}
