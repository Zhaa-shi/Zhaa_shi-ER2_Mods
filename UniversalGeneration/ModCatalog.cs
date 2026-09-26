using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Xml.Linq;
using Il2CppInterop.Runtime;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 2.2.0：第三方内容目录（工坊 / 本地 mod 的载具与物品）——按用户要求重启 1.3.2 移除的能力。
///
/// **发现方式 = 磁盘解析每个已装 mod 的 index.xml**（游戏自己的注册清单：
/// `ModData/Metadata/{ModName,BundleName}` + `Prefabs/RegisteredPrefab/{PrefabName,DisplayName,Type}`）。
/// 为什么不走运行时数据库：2.0.7 实测定案 `GetAllItemsOfType&lt;PropData&gt;` 恒为空
/// （库按泛型 T 过滤、存的是 ItemObject），而 `ItemObject` 上没有 mod_id ——
/// **运行时无法区分"官方条目"与"mod 条目"**；index.xml 是游戏自己启动时消费的同一份清单
/// （`ModsLoader.ParseXmlString` → `RegisterModObject`），磁盘解析 + 运行时校验是唯一可靠路线
/// （与 GenCatalog 的 manifest 解析同一模式）。
///
/// **2.4.1（陷阱 77）：目录定位从 `ModsLoader.mods_installed` 改为磁盘直扫。**
/// 实测日志：主菜单阶段 `ModsLoader.mods_installed` 恒为空 → 探测等 30s 后拿到 mod=0，
/// 然后 `ready=true` 一锤定音（`ProbeWatchdog` 见 ready 直接 return，永不重试）
/// → 用户进战斗后物品库从 949 涨到 2107（mod 内容这时才可用），但「Mod载具」页签
/// **整个会话都空着**。这是"终态 + 一次性探测"的经典组合事故。
/// 现在：
/// - 目录从**磁盘**定位（`&lt;SteamLib&gt;/steamapps/workshop/content/&lt;appid&gt;/*/index.xml`
///   + `&lt;Game&gt;/Mods` + 运行时 `mods_installed` 兜底），与 mod 何时被加载无关；
/// - 解析出的候选**缓存复用**，只有**运行时校验**需要等 `ItemCatalog.Ready`，
///   没就绪就本轮跳过、15s 后再来（**不写终态**）；
/// - 空闲后每 60s 重扫一次，新订阅的 mod 会自动出现；载具校验失败计数上限 5 次后放弃该
///   候选（避免 8s 超时 × 几十条 = 后台空转），放弃时**必打日志**。
///
/// **条目 id 用游戏公开 API 计算**：`ModsLoader.GenerateModItemId(bundleName, prefabName)`，
/// 校验全走运行时：
/// - 物品（items=3/weapons=4/ammo=5/attachment=6/uniforms=7）：`ItemsDatabase.GetItemObject(id) != null`
///   → 经 `ItemCatalog.AddModItem` 入"Mod物品"桶（id 已验证，"点了才发现无效"结构上不可能）；
/// - 载具（vehicles=2）：`VehicleSpawner.GetVehiclePrefabAsync` 异步验证 prefab 可加载
///   （每帧最多一个，防卡顿），验证通过的 id 直接可喂 `VehicleSpawner.vehiclePrefabID`。
///
/// **小队/单位**：mod 注册的小队与玩家内容进 `SquadsArchive.squads` 后，GenCatalog 步兵页
/// 本来就按"全量 key"列出（1.2.1 起）——本类输出覆盖情况诊断日志证实，不重复建桶。
///
/// 可观测性（工作区铁律）：索引到的 mod 数、每个 mod 的候选数/验证通过数全部进日志；
/// 枚举协程配 tick 判活看门狗（GenDriver.Tick 每秒调 `ProbeWatchdog`，判据与时间无关）。
/// </summary>
internal static class ModCatalog
{
	/// <summary>验证通过的 mod 载具（GenPanel 页签 "modveh"）。</summary>
	internal static List<GenEntry> vehicles = new();

	/// <summary>已完成过至少一轮扫描（**不代表有内容**——2.4.1 陷阱 77）。</summary>
	internal static bool ready;

	private const float RetryWaitDb = 15f;   // 物品库未就绪/本轮还有活儿：15s 后再来
	private const float RetryIdle = 60f;     // 全部处理完：60s 后再扫一次（抓新订阅的 mod）
	private const string FallbackAppId = "1324780";
	private const int VehPerPass = 10;       // 每轮最多校验 10 个载具（每个最坏 8s，防后台长跑）
	private const int VehMaxFail = 5;        // 单个候选最多失败 5 次（超时/bundle 未加载）

	// ── 探测状态（0=待启动 1=进行中 2=空闲；**无"放弃"终态**）──
	private static int probeState;
	private static int probeTicks;
	private static int watchdogLastTicks = -1;
	private static int probeRestarts;
	private static int passes;
	private static float nextPassAt;

	private static readonly HashSet<string> seenIndex = new(StringComparer.OrdinalIgnoreCase);
	private static readonly List<ModSrc> srcs = new();
	private static readonly HashSet<string> gotItems = new(StringComparer.OrdinalIgnoreCase);
	private static readonly HashSet<string> gotVeh = new(StringComparer.OrdinalIgnoreCase);
	private static readonly Dictionary<string, int> vehFails = new(StringComparer.OrdinalIgnoreCase);
	private static int candVeh, candItems;

	/// <summary>一个已解析的 mod 清单（只缓存候选，校验结果另记）。</summary>
	private sealed class ModSrc
	{
		public string Index;
		public string Bundle;
		public string Name;
		public readonly List<string[]> Items = new();   // [生成id, 裸prefab名, 显示名]
		public readonly List<string[]> Vehs = new();
	}

	/// <summary>index.xml 里的 Type → 类别。返回 null = 本 mod 不接管的类型（地图/音效/贴图等）。</summary>
	private static string KindOf(string typeStr)
	{
		switch ((typeStr ?? "").Trim())
		{
			case "2": return "vehicle";                       // ModPropType.vehicles
			case "3": case "4": case "5": case "6": case "7": // items/weapons/ammo/attachment/uniforms
				return "item";
			default: return null;
		}
	}

	// ================= 看门狗（GenDriver.Tick 每秒一次）=================

	internal static void ProbeWatchdog()
	{
		if (probeState == 1)
		{
			// tick 计数判活（陷阱 67 的定案判据：与时间无关，场景加载骗不过）
			if (watchdogLastTicks < 0 || probeTicks != watchdogLastTicks)
			{
				watchdogLastTicks = probeTicks;
				return;
			}
			probeRestarts++;
			Plugin.ModLog.LogWarning("[UniGen] 第三方内容目录探测已停止（tick 停在 " + probeTicks + "），自动重启（第 "
				+ probeRestarts + " 次，已有 载具=" + vehicles.Count + " 物品=" + ItemCatalog.Bucket("mod").Count + "）。");
			probeState = 0;
			nextPassAt = 0f;
			Begin();
			return;
		}

		// 空闲：**周期性重扫**（2.4.1——不再有"ready 就永远不问"的终态）
		if (probeState == 0 || Time.unscaledTime >= nextPassAt) Begin();
	}

	private static void Begin()
	{
		if (probeState == 1) return;
		probeState = 1;
		probeTicks = 0;
		watchdogLastTicks = -1;
		passes++;
		if (passes == 1)
			Plugin.ModLog.LogInfo("[UniGen] 第三方内容目录探测启动（磁盘直扫 index.xml）");
		try { GenRunner.StartCoroutine(ProbeCR()); }
		catch (Exception ex)
		{
			probeState = 2;
			nextPassAt = Time.unscaledTime + RetryWaitDb;
			Plugin.ModLog.LogWarning("[UniGen] 第三方内容目录协程启动失败: " + ex.Message);
		}
	}

	// ================= 枚举协程 =================

	private static IEnumerator ProbeCR()
	{
		// ① 磁盘发现（不依赖 ModsLoader 的加载时机——陷阱 77）
		int fresh = Discover();
		if (fresh > 0 || passes == 1)
			Plugin.ModLog.LogInfo("[UniGen] 第三方内容: 已索引 mod=" + srcs.Count + "（本轮新增 " + fresh
				+ "）累计候选 载具=" + candVeh + " 物品=" + candItems);

		// ② 运行时校验需要物品库就绪；没就绪就本轮只建索引，下一轮再来（不写终态）
		bool dbReady = false;
		try { dbReady = ItemCatalog.Ready; } catch { }
		if (!dbReady)
		{
			probeState = 2;
			nextPassAt = Time.unscaledTime + RetryWaitDb;
			yield break;
		}

		// ③ 待校验队列（已通过的不再校验；载具失败次数用尽的不重试）
		var itemQ = new List<string[]>();
		var vehQ = new List<string[]>();
		foreach (ModSrc s in srcs)
		{
			foreach (string[] c in s.Items)
				if (!gotItems.Contains(c[0]) && !gotItems.Contains(c[1])) itemQ.Add(c);
			foreach (string[] c in s.Vehs)
				if (!gotVeh.Contains(c[0]) && !gotVeh.Contains(c[1]) && FailLeft(c)) vehQ.Add(c);
		}

		// ④ 物品：运行时校验（GetItemObject 非空才入桶；时间片 3ms/帧）
		var sw = System.Diagnostics.Stopwatch.StartNew();
		long budgetUntil = 0;
		int itemOk = 0;
		foreach (string[] cand in itemQ)
		{
			string valid = null;
			try { if (ItemsDatabase.GetItemObject(cand[0]) != null) valid = cand[0]; } catch { }
			if (valid == null && cand[1] != cand[0])
			{
				try { if (ItemsDatabase.GetItemObject(cand[1]) != null) valid = cand[1]; } catch { }
			}
			if (valid != null)
			{
				ItemCatalog.AddModItem(valid, cand[2]);
				gotItems.Add(cand[0]); gotItems.Add(cand[1]);
				itemOk++;
			}
			probeTicks++;

			if (sw.ElapsedMilliseconds >= budgetUntil)
			{
				budgetUntil = sw.ElapsedMilliseconds + 3;
				yield return null;
			}
		}

		// ⑤ 载具：异步验证 prefab 可加载（GetVehiclePrefabAsync 完成回调；失败 8s 超时跳过）。
		// 委托用 DelegateSupport 托管转换；转换不可用（理论上不会）则退化为"不验证直接列出"。
		int vehTodo = Mathf.Min(vehQ.Count, VehPerPass);
		int vehOk = 0;
		bool delegateBroken = false;
		for (int qi = 0; qi < vehTodo; qi++)
		{
			string[] cand = vehQ[qi];
			string validId = null;
			string[] idCands = cand[0] == cand[1] ? new[] { cand[0] } : new[] { cand[0], cand[1] };
			foreach (string idCand in idCands)
			{
				if (delegateBroken) { validId = cand[0]; break; }

				GameObject prefab = null;
				bool done = false;
				GameObject spGo = null;
				try
				{
					spGo = new GameObject("UniGen_VehValidate");
					VehicleSpawner sp = spGo.AddComponent<VehicleSpawner>();
					sp.vehiclePrefabID = idCand;
					var cb = Il2CppInterop.Runtime.DelegateSupport.ConvertDelegate<
						VehicleSpawner.GetVehiclePrefabAsyncResult>(
						new Action<GameObject>(p => { prefab = p; done = true; }));
					sp.GetVehiclePrefabAsync(cb);
				}
				catch (Exception ex)
				{
					delegateBroken = true;
					Plugin.ModLog.LogWarning("[UniGen] 载具验证委托不可用（改为不验证直接列出）: " + ex.Message);
				}

				float dl = Time.unscaledTime + 8f;
				while (!done && !delegateBroken && Time.unscaledTime < dl)
				{
					probeTicks++;
					yield return null;
				}
				// 校验完销毁临时 spawner（回调已触发/超时；不能提前销毁——会杀死内部协程导致回调不来）
				try { if (spGo != null) UnityEngine.Object.Destroy(spGo); } catch { }

				if (prefab != null) { validId = idCand; break; }
				probeTicks++;
				yield return null;
			}

			if (validId != null)
			{
				vehicles.Add(new GenEntry
				{
					Id = validId,
					Title = GenCatalog.PrettyNamePublic(cand[2]),
					IsInfantry = false,
					Category = "modveh"
				});
				gotVeh.Add(cand[0]); gotVeh.Add(cand[1]);
				vehOk++;
			}
			else
			{
				NoteVehFail(cand);
			}
			probeTicks++;
			yield return null;
		}

		// ⑥ 小队覆盖诊断（mod/自定义小队本就由 GenCatalog 步兵页按全量 key 列出；只在首轮打一次）
		if (passes == 1)
		{
			try
			{
				int total = 0, nonEnum = 0;
				var enumNames = new HashSet<string>(Enum.GetNames(typeof(SquadType)));
				foreach (string k in SquadsArchive.squads.Keys)
				{
					total++;
					if (!enumNames.Contains(k)) nonEnum++;
					probeTicks++;
				}
				// 2.5.33（§7 发布前清理）：统计性诊断收进 debugLog 门控。
				//   probeTicks 计数照旧累积——它是看门狗的存活信号，绝不能因门控而漏算。
				if (Plugin.debugLog.Value)
					Plugin.ModLog.LogInfo("[UniGen] 小队覆盖诊断: SquadsArchive 总数=" + total
						+ " 非官方枚举键=" + nonEnum + "（这些已在「步兵」页签直接可生成）");
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning("[UniGen] 小队覆盖诊断失败: " + ex.Message);
			}
		}

		bool more = vehQ.Count > vehTodo || !dbReady;
		ready = true;
		probeState = 2;
		nextPassAt = Time.unscaledTime + (more ? RetryWaitDb : RetryIdle);
		// 2.5.33（§7 发布前清理）：这是一条**周期性**日志（15s / 60s 循环重扫），发布版只保留首轮，
		//   之后静默；需要看每一轮扫描的累计通过数时把 Debug/debugLog 打开。
		if (Plugin.debugLog.Value || passes == 1)
			Plugin.ModLog.LogInfo("[UniGen] 第三方内容目录 第 " + passes + " 轮: 索引 mod=" + srcs.Count
				+ " 载具候选=" + candVeh + " 累计通过=" + vehicles.Count
				+ " 物品候选=" + candItems + " 累计通过=" + ItemCatalog.Bucket("mod").Count
				+ "（本轮新通过 载具=" + vehOk + " 物品=" + itemOk + "，待校验 载具=" + (vehQ.Count - vehTodo) + "）");

		if (vehOk > 0 || itemOk > 0)
		{
			try { GenPanel.RebuildItemTabsPublic(); } catch { }
			try { GenPanel.RebuildFavTabsPublic(); } catch { }
		}
	}

	// ================= 磁盘发现 =================

	private static int Discover()
	{
		int fresh = 0;

		foreach (string dir in ModRoots())
		{
			string[] subs;
			try { subs = Directory.GetDirectories(dir); } catch { continue; }
			foreach (string sd in subs)
			{
				string f = Path.Combine(sd, "index.xml");
				if (!File.Exists(f)) continue;
				probeTicks++;
				if (!seenIndex.Add(f)) continue;
				ModSrc s = Parse(f);
				if (s != null) { srcs.Add(s); fresh++; }
			}
			probeTicks++;
		}

		// 运行时已注册的 mod 兜底（覆盖磁盘扫描找不到的位置；主菜单时这里通常为空——陷阱 77）
		try
		{
			var dict = ModsLoader.mods_installed;
			if (dict != null)
				foreach (var kv in dict)
				{
					string folder = null;
					try { folder = kv.Value?.assetBundleFolder; } catch { }
					string f = FindIndexXml(folder);
					if (f != null && seenIndex.Add(f))
					{
						ModSrc s = Parse(f);
						if (s != null) { srcs.Add(s); fresh++; }
					}
					probeTicks++;
				}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] 读取已装 mod 列表失败: " + ex.Message);
		}

		return fresh;
	}

	/// <summary>要扫的 mod 根目录（工坊 + 本地；不存在的不进列表）。</summary>
	private static List<string> ModRoots()
	{
		var list = new List<string>();
		string root = null;
		try { root = Path.GetDirectoryName(Application.dataPath); } catch { }   // <Game>
		if (string.IsNullOrEmpty(root)) return list;

		string appId = FallbackAppId;
		try
		{
			string sid = Path.Combine(root, "steam_appid.txt");
			if (File.Exists(sid))
			{
				string t = File.ReadAllText(sid).Trim();
				if (t.Length > 0) appId = t;
			}
		}
		catch { }

		// <SteamLib>/steamapps/common/<Game> → <SteamLib>/steamapps
		DirectoryInfo sa = null;
		try { sa = Directory.GetParent(root)?.Parent; } catch { }
		if (sa != null)
		{
			AddIfDir(list, Path.Combine(sa.FullName, "workshop", "content", appId));
			foreach (string lib in OtherLibraries(sa.FullName))
				AddIfDir(list, Path.Combine(lib, "steamapps", "workshop", "content", appId));
		}

		foreach (string sub in new[] { "Mods", "mods", "LocalMods" })
			AddIfDir(list, Path.Combine(root, sub));

		return list;
	}

	private static void AddIfDir(List<string> list, string p)
	{
		if (!string.IsNullOrEmpty(p) && Directory.Exists(p)) list.Add(p);
	}

	/// <summary>解析 libraryfolders.vdf 里的其他 Steam 库（解析失败就只用游戏所在库——不阻塞）。</summary>
	private static List<string> OtherLibraries(string steamapps)
	{
		var res = new List<string>();
		try
		{
			string vdf = Path.Combine(steamapps, "libraryfolders.vdf");
			if (!File.Exists(vdf)) return res;
			foreach (string raw in File.ReadAllLines(vdf))
			{
				string t = raw.Trim();
				if (!t.StartsWith("\"path\"", StringComparison.OrdinalIgnoreCase)) continue;
				int a = t.IndexOf('"', 6);
				if (a < 0) continue;
				int b = t.IndexOf('"', a + 1);
				if (b < 0) continue;
				string p = t.Substring(a + 1, b - a - 1).Replace("\\\\", "\\");
				if (Directory.Exists(p)) res.Add(p);
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] libraryfolders.vdf 解析失败（只用游戏所在库）: " + ex.Message);
		}
		return res;
	}

	/// <summary>解析一份 index.xml → 候选清单；没有可接管类型（纯地图/贴图）返回 null。</summary>
	private static ModSrc Parse(string indexXml)
	{
		try
		{
			XDocument doc = XDocument.Load(indexXml);
			XElement md = doc.Root?.Element("Metadata");
			string bundle = md?.Element("BundleName")?.Value ?? "";
			string name = md?.Element("ModName")?.Value;
			if (string.IsNullOrEmpty(name)) name = Path.GetFileName(Path.GetDirectoryName(indexXml));

			var s = new ModSrc { Index = indexXml, Bundle = bundle, Name = name };
			foreach (XElement p in doc.Descendants("RegisteredPrefab"))
			{
				string prefab = p.Element("PrefabName")?.Value;
				string display = p.Element("DisplayName")?.Value;
				string type = p.Element("Type")?.Value;
				if (string.IsNullOrEmpty(prefab)) continue;

				string kind = KindOf(type);
				if (kind == null) continue;

				// id 候选：① 游戏自己的生成器 ② 裸 prefab 名（旧版 mod / 官方风格 id）
				string id1 = prefab;
				try { id1 = ModsLoader.GenerateModItemId(bundle, prefab); } catch { }
				if (string.IsNullOrEmpty(id1)) id1 = prefab;

				var c = new[] { id1, prefab, string.IsNullOrEmpty(display) ? prefab : display };
				if (kind == "vehicle") s.Vehs.Add(c); else s.Items.Add(c);
				probeTicks++;
			}

			if (s.Items.Count == 0 && s.Vehs.Count == 0) return null;
			candItems += s.Items.Count;
			candVeh += s.Vehs.Count;
			if (s.Vehs.Count > 0 || Plugin.debugLog.Value)
				Plugin.ModLog.LogInfo("[UniGen] 第三方内容: " + name + " 候选 载具=" + s.Vehs.Count
					+ " 物品=" + s.Items.Count + "（bundle=" + bundle + "）");
			return s;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] 解析 index.xml 失败（" + indexXml + "）: " + ex.Message);
			return null;
		}
	}

	// ================= 载具失败计数 =================

	private static bool FailLeft(string[] cand)
	{
		int a, b;
		vehFails.TryGetValue(cand[0], out a);
		vehFails.TryGetValue(cand[1], out b);
		return Mathf.Min(a, b) < VehMaxFail;
	}

	private static void NoteVehFail(string[] cand)
	{
		foreach (string id in new[] { cand[0], cand[1] })
		{
			vehFails.TryGetValue(id, out int n);
			n++;
			vehFails[id] = n;
			if (n == VehMaxFail)
				Plugin.ModLog.LogWarning("[UniGen] 载具候选校验连续失败 " + VehMaxFail + " 次，放弃该条: " + id
					+ "（mod 的 bundle 未加载或 prefab 名与清单不符）");
		}
	}

	/// <summary>从 mod 的 bundle 目录向上找 index.xml（最多 4 级）。</summary>
	private static string FindIndexXml(string folder)
	{
		if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
		string dir = folder;
		for (int i = 0; i < 4 && !string.IsNullOrEmpty(dir); i++)
		{
			string candidate = Path.Combine(dir, "index.xml");
			if (File.Exists(candidate)) return candidate;
			DirectoryInfo parent = Directory.GetParent(dir);
			if (parent == null) break;
			dir = parent.FullName;
		}
		return null;
	}
}
