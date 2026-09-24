using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace ER2UniversalGeneration;

/// <summary>
/// 可放置物品条目。
/// Id = **游戏运行时物品数据库的键**（`PropData.prefab_name`），例如 "MP44"、"Carcano_M91"。
/// 该 Id 可直接喂给 `ItemsDatabase.GetItemObject(id)` —— 这是 2.0.2 的核心修正。
/// </summary>
internal class ItemEntry
{
	public string Id;      // item_id（= PropData.prefab_name，可直接查数据库）
	public string Title;   // UI 显示名
	public string Bucket;  // weapons / ammo / throwables / gear / food / misc / uniforms
	/// <summary>
	/// 2.1.0：子分类键（"" = 不细分）。取自**游戏自己的判定**，不靠名字猜：
	/// `rifle` / `pistol` 来自 `Weapon.weaponPose`（`Weapon : HandheldItem : ItemObject`，
	/// 故需 `TryCast&lt;Weapon&gt;()`——陷阱 5：IL2CPP 下 C# `as` 恒失败）；
	/// `wearable` 来自 `Interagible.IsWerable()`。
	/// </summary>
	public string Sub = "";

	public override string ToString() => Title ?? Id;
}

/// <summary>
/// 物品目录（2.0.2 重写）。
///
/// **为什么重写（用户实测：无图标条目全部无法生成）**：
/// 2.0.0/2.0.1 走**磁盘 manifest 解析**（`er2items.manifest` 里的 prefab 文件路径 → 文件名当 item_id），
/// 并用 `GetItemObject` 抽样剔除。实测证明这条路线**根本错**：
/// manifest 文件名与**运行时物品数据库的键不是同一套字符串**——
/// 例：`ArisakaT38.prefab` / `Carcano.prefab` / `Thompson_M1928.prefab` / `Syringe.prefab`
/// 确实是磁盘上的真实预制品（正则解析无误），但 `GetItemObject("ArisakaT38")` 返回 null。
/// 而带图标的条目（`bar_1918` / `bandages` / `37mm_ns37_ammo` / `ToolBox`）恰好两套键一致 → 能生成。
/// 于是呈现用户看到的"只有前面有图标的物品可以生成"——
/// **图标 = `ItemObject.icon` 非空 = 条目真的存在于运行时数据库**，图标成了"有效"的天然标志物。
///
/// 另有 757 条 `Uniforms/` 条目（`commonwealth/aus_infantry_uniform_1` 等）全部无效：
/// 服装在游戏里是 `Loadout` / `CustomSquadMember.uniform` 字段，**根本不是 ItemObject**。
///
/// **修法：不再猜 id，直接向游戏要** —— 运行时枚举 `ItemsDatabase.GetAllItemsOfType<PropData>(PropType.xxx)`，
/// 把每个 `PropData.prefab_name` 作为 Id、`name` 作为标题，**分类直接用游戏自己的 `PropType`**
/// （items=6 / weapons=7 / ammo=8 / attachment=9），不再靠字符串启发式猜桶。
/// 这样列表里**只可能出现数据库里真实存在的条目**，从源头消灭"点了才发现无效"。
///
/// 性能（1.2.0 教训：数百次"构建型"原生调用会冻结主线程）：
/// 枚举是**分帧协程**（每帧 3ms 时间片），且只读 PropData 元数据（不实例化、不建班）。
/// 数据库未就绪时**逐帧轮询**等待（见 `ProbeDatabaseReady` 与 `ProbeCR`）。
///
/// **2.0.5（2.0.4 实测仍失败，用户第 11 轮）**：彻底移除"自动放弃"。
/// 2.0.2 一次跑死 → 2.0.3 修复判据但轮询太慢 → 2.0.4 改逐帧轮询却被看门狗"误判死亡 + 触顶放弃"。
/// 三次都栽在同一个模式上：**探测链路里存在一个"永久终态"**。
/// 现在 `probeState` 只有 0/1/2 三态，**没有任何放弃路径**：
/// 协程断了就看门狗重启，枚举 0 条就复位重试，重启幂等（`Add` 按 Id 去重）。
/// 另加 `LogProbeDiag`：闸门为何不放行（`Loaded=false` / 枚举 n=0 / 抛异常）每 5s 记一次，
/// 让日志自己说话而不是靠猜。
///
/// **2.0.7（2.0.6 日志终于给出真因，用户第 11 轮续三）**：
/// 前四轮全在修"协程到底活不活"，**真正的病根是就绪判据本身挂在了一个空的类别上**。
/// 2.0.6 新增的诊断日志连续 154 秒、跨 Menu→LoadingScene→Aberdeen 三场景打印同一句：
/// `Loaded=true 但 items 枚举 n=0（null=-1）` —— `ItemsDatabase.Loaded` **恒为 true**，
/// 而 `GetAllItemsOfType<PropData>(PropType.items)`(=6) **永远返回空数组**。
/// 即：**`items`(6) 在游戏里是杂项总类、常态为空**，可生成物品实际落在
/// `weapons`(7) / `ammo`(8) / `attachment`(9)。
/// 修法：`ProbeDatabaseReady` **不再依赖任何单一 PropType** —— 四类里任意一类非空即判就绪，
/// 并把四类的真实条数一并记入日志（`物品库可枚举: items=N weapons=N ammo=N attachment=N`）。
/// **教训（已写进 guide 陷阱 67）：排查"闸门永远不放行"时，先问"闸门的条件本身是不是错的"，
/// 再问"等条件的循环还活着吗"。前四轮只做了后者。**
/// </summary>
internal static class ItemCatalog
{
	/// <summary>页签顺序（面板里按此顺序渲染 Buckets 中的桶）。2.2.0：尾部追加 mod（第三方物品，校验通过的才入桶）。</summary>
	internal static readonly string[] BucketOrder = { "weapons", "ammo", "throwables", "gear", "food", "misc", "mod" };

	internal static readonly Dictionary<string, List<ItemEntry>> Buckets = new Dictionary<string, List<ItemEntry>>();

	internal static bool Ready;
	internal static bool Failed;

	// ── 枚举进度（供看门狗与日志） ──
	internal static int probeState; // 0=未开始 1=进行中 2=完成
	internal static float probeStartedAt;
	internal static int probeRestarts;

	/// <summary>
	/// **2.0.6 核心**：协程"心跳计数"——协程每跑一帧就 +1。
	/// 看门狗读它两次，**计数没涨 = 协程真的没在跑**。
	/// 与时间无关 → 不会被"场景加载期间墙上时钟照走"骗到（2.0.4 就栽在这）。
	/// </summary>
	internal static int probeTicks;

	/// <summary>看门狗上次观察到的 tick 计数（与上一次 Tick 调用比较）。</summary>
	private static int watchdogLastTicks = -1;

	/// <summary>2.0.5：诊断节流（见 LogProbeDiag）。</summary>
	private static float lastDiagAt;

	/// <summary>幂等入口：面板打开 / 启动时调用。已完成或进行中直接返回。</summary>
	internal static void Ensure()
	{
		if (Ready || Failed || probeState == 1) return;
		if (probeState == 3) probeState = 0; // 旧版"已放弃"不再视为终态
		Begin();
	}

	internal static void Begin()
	{
		if (Ready || Failed || probeState == 1) return;
		probeState = 1;
		probeStartedAt = UnityEngine.Time.unscaledTime;
		probeTicks = 0;              // 2.0.6：新协程从 0 开始计数
		watchdogLastTicks = -1;      // 2.0.6：让看门狗下次调用时"重新建立基线"，不误判
		try
		{
			GenRunner.StartCoroutine(ProbeCR());
		}
		catch (Exception ex)
		{
			probeState = 0;
			Plugin.ModLog.LogWarning("[UniGen] 物品目录协程启动失败: " + ex.Message);
		}
	}

	/// <summary>
	/// 看门狗（由 GenDriver.Tick 每秒调用）。
	///
	/// **判活判据的演进（三次踩坑记录，务必先读懂再改）**：
	/// - 1.3.x/2.0.3：`Time.unscaledTime - probeStartedAt > 阈值` 判死 →
	///   **场景加载期间协程停摆但墙上时钟照走** → 合法等待被误杀；
	/// - 2.0.4：改"心跳时间戳停跳 > 20s"→ **同一错误**（仍是时间判据），
	///   且每次误杀都 `probeRestarts++`，6 次触顶后**永久放弃** → 页签永不出现；
	/// - 2.0.5：干脆"不判死、只续跑"（`probeState==1` 直接 return）→ **更糟**：
	///   协程被场景切换杀死时**没人把 probeState 复位**（该复位的是已死的协程自己），
	///   `probeState` 永远卡在 1，看门狗**永远认为它在跑**，一次都不重启。
	///
	/// **2.0.6 正解：用协程自己的计数器判活，彻底不看时间。**
	/// 协程每跑一帧 `probeTicks++`；看门狗每次被调用（也在主线程）比对计数是否增长。
	/// **计数不涨 = 协程真的没在跑** —— 与时间无关，`Time.timeScale`、场景加载、
	/// 墙上时钟全都骗不过它。这同时满足两个看似矛盾的需求：
	/// ① 场景加载导致的"合法停摆"不会误杀（因为看门狗自己那时也没被调用，不会去比对）；
	/// ② 真死的协程一定被发现（因为看门狗在被调用时它必须也在涨）。
	/// 另：**重启无上限**（幂等，`Add` 按 Id 去重）。
	/// </summary>
	internal static void ProbeWatchdog()
	{
		// 2.0.7：Ready 之后的一次性补漏（首轮可能只拿到部分条目）
		if (Ready && !enrichDone && nextEnrichAt > 0f && UnityEngine.Time.unscaledTime >= nextEnrichAt)
		{
			enrichDone = true;
			try { GenRunner.StartCoroutine(EnrichCR()); } catch { }
			return;
		}

		if (Ready || Failed) return;

		// 未开始 → 启动
		if (probeState == 0)
		{
			probeRestarts++;
			if (probeRestarts == 1)
				Plugin.ModLog.LogInfo("[UniGen] 物品目录探测启动（看门狗）");
			Begin();
			return;
		}

		if (probeState != 1) return;

		// 2.0.6：tick 计数判活。首次观察只建立基线，不判定。
		if (watchdogLastTicks < 0 || probeTicks != watchdogLastTicks)
		{
			watchdogLastTicks = probeTicks;   // 计数变了（或首次）→ 协程活着，更新基线
			return;
		}

		// 计数与上次完全相同 → 协程在此期间一帧都没跑 → 真的死了，重启。
		probeRestarts++;
		Plugin.ModLog.LogWarning("[UniGen] 物品目录探测已停止（tick 停在 " + probeTicks
			+ "），自动重启补齐（第 " + probeRestarts + " 次，已有 " + TotalCount() + " 条）。");
		probeState = 0;
		Begin();
	}

	/// <summary>该桶是否有内容。</summary>
	internal static bool HasBucket(string name) => Bucket(name).Count > 0;

	internal static List<ItemEntry> Bucket(string name)
	{
		if (Buckets.TryGetValue(name, out List<ItemEntry> l)) return l;
		return empty;
	}

	private static readonly List<ItemEntry> empty = new List<ItemEntry>();

	internal static int TotalCount()
	{
		int n = 0;
		foreach (List<ItemEntry> l in Buckets.Values) n += l.Count;
		return n;
	}

	/// <summary>2.1.0：子分类显示顺序（只列实际存在的）。</summary>
	internal static readonly string[] SubOrder = { "rifle", "pistol", "wearable" };

	/// <summary>2.1.0：物品收藏 id 集合（纯 id，持久化时加 "t:" 前缀写进 `Plugin.favorites`）。</summary>
	private static readonly HashSet<string> favIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

	internal static bool IsFav(string id) => !string.IsNullOrEmpty(id) && favIds.Contains(id);

	/// <summary>2.1.0：持久化为 "t:&lt;id&gt;" 形式（供 GenCatalog.SaveFavs 统一写回）。</summary>
	internal static IEnumerable<string> FavIdsPrefixed()
	{
		foreach (string id in favIds) yield return "t:" + id;
	}

	internal static void LoadFavs()
	{
		favIds.Clear();
		try
		{
			string raw = Plugin.favorites.Value ?? "";
			foreach (string part in raw.Split(','))
			{
				string s = part.Trim();
				if (s.Length > 2 && s.StartsWith("t:", StringComparison.Ordinal))
					favIds.Add(s.Substring(2));
			}
		}
		catch { }
	}

	/// <summary>2.1.0：切换物品收藏（持久化走 GenCatalog.SaveFavs，避免两处各写覆盖对方）。</summary>
	internal static bool ToggleFav(ItemEntry e)
	{
		if (e == null || string.IsNullOrEmpty(e.Id)) return false;
		if (favIds.Contains(e.Id)) favIds.Remove(e.Id);
		else favIds.Add(e.Id);
		GenCatalog.SaveFavs();
		return favIds.Contains(e.Id);
	}

	/// <summary>2.1.0：某桶内实际出现过的子分类（按 SubOrder 排序；无子分类则空表）。</summary>
	internal static List<string> SubsOf(string bucket, bool favOnly = false)
	{
		var res = new List<string>();
		if (!Buckets.TryGetValue(bucket, out List<ItemEntry> list)) return res;
		foreach (string s in SubOrder)
		{
			for (int i = 0; i < list.Count; i++)
			{
				ItemEntry e = list[i];
				if (e.Sub != s) continue;
				if (favOnly && !favIds.Contains(e.Id)) continue;
				res.Add(s);
				break;
			}
		}
		return res;
	}

	/// <summary>
	/// 2.1.1：取桶内条目（可按子分类/首字母过滤，可只要收藏项）。
	/// **没有关键字输入框**——`GUI.TextField` 在本游戏被 Unity 裁剪
	/// （实测 `OnGUI 异常: Method unstripping failed`，一抛整帧 OnGUI 中断、列表全空），
	/// 改为纯点击的**首字母索引**，零键盘依赖。
	/// </summary>
	internal static List<ItemEntry> Query(string bucket, string sub, string letter, bool favOnly)
	{
		var res = new List<ItemEntry>();
		if (!Buckets.TryGetValue(bucket, out List<ItemEntry> list)) return res;
		bool hasSub = !string.IsNullOrEmpty(sub);
		bool hasLetter = !string.IsNullOrEmpty(letter);
		for (int i = 0; i < list.Count; i++)
		{
			ItemEntry e = list[i];
			if (hasSub && e.Sub != sub) continue;
			if (favOnly && !favIds.Contains(e.Id)) continue;
			if (hasLetter && !HasLetter(e, letter)) continue;
			res.Add(e);
		}
		if (favOnly) res.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
		return res;
	}

	/// <summary>2.1.1：桶内实际出现过的首字母（按字母序，供索引按钮行）。</summary>
	internal static List<string> LettersOf(string bucket, string sub, bool favOnly)
	{
		var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		if (!Buckets.TryGetValue(bucket, out List<ItemEntry> list)) return new List<string>();
		bool hasSub = !string.IsNullOrEmpty(sub);
		for (int i = 0; i < list.Count; i++)
		{
			ItemEntry e = list[i];
			if (hasSub && e.Sub != sub) continue;
			if (favOnly && !favIds.Contains(e.Id)) continue;
			string c = LetterOf(e);
			if (c != null) set.Add(c);
		}
		var res = new List<string>(set);
		res.Sort(StringComparer.OrdinalIgnoreCase);
		return res;
	}

	/// <summary>标题首字母（大写化；非字母取原字符）。</summary>
	private static string LetterOf(ItemEntry e)
	{
		string t = e.Title;
		if (string.IsNullOrEmpty(t)) return null;
		return char.ToUpperInvariant(t[0]).ToString();
	}

	private static bool HasLetter(ItemEntry e, string letter)
	{
		string c = LetterOf(e);
		return c != null && string.Equals(c, letter, StringComparison.OrdinalIgnoreCase);
	}

	/// <summary>2.1.0：收藏清单（按桶过滤，空桶名=全部）。</summary>
	internal static List<ItemEntry> Favs(string bucket)
	{
		var res = new List<ItemEntry>();
		foreach (List<ItemEntry> list in Buckets.Values)
			for (int i = 0; i < list.Count; i++)
			{
				ItemEntry e = list[i];
				if (!favIds.Contains(e.Id)) continue;
				if (!string.IsNullOrEmpty(bucket) && e.Bucket != bucket) continue;
				res.Add(e);
			}
		res.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
		return res;
	}

	/// <summary>2.1.1：首字母匹配（替代被裁剪的关键字输入）。</summary>
	// Matches 已随 GUI.TextField 一并移除——无输入框即无字符串匹配。

	/// <summary>2.1.0：子分类判定——**只用游戏自己的判定，不猜名字**。</summary>
	private static string SubFor(ItemObject io)
	{
		// ① 武器：TryCast<Weapon> 成功即拿到官方 weaponPose（rifle=1 / pistol=2）
		try
		{
			Weapon w = io.TryCast<Weapon>();
			if (w != null)
				return w.weaponPose == WeaponPose.pistol ? "pistol" : "rifle";
		}
		catch { }
		// ② 可穿戴（制服/头盔/背包等）：官方 IsWerable()
		try { if (io.IsWerable()) return "wearable"; } catch { }
		return "";
	}

	// ── 枚举协程 ──

	/// <summary>
	/// 枚举 `PropType.items` / `weapons` / `ammo` / `attachment` 四类（= 全部可生成物品类别）。
	/// 每帧 3ms 时间片；数据库未就绪则等候重试。
	///
	/// **2.0.3 结构修正**：改为 attempt 循环（对齐 `GenCatalog.StartupProbeCR`）。
	/// 2.0.2 是"一锤子"结构：就绪判据一旦被空数组骗过，枚举 0 条后直接 `yield break` +
	/// `Failed=true`，此后**没有任何重试机会** → 物品功能整局不可用。
	///
	/// **2.0.4 竞速修正（2.0.3 实测仍失败）**：2.0.3 用 `WaitForSeconds(2f)` 轮询就绪 ——
	/// **太慢，且会输给场景切换**。实测日志：主菜单打印"未就绪，2s 后重试"后，游戏切到
	/// 战斗场景（物品数据库那时才真正加载完），而协程正卡在 `WaitForSeconds` 里 →
	/// **随场景切换被杀死**（宿主 DontDestroyOnLoad 活着，但运行中的协程死）。
	/// 看门狗 150s 后才重启，此时 60 次 × 2s 的预算已在主菜单空耗殆尽。
	///
	/// 现在改为**每帧轮询就绪**（成本极低：一次 bool 读取 + 一次数组长度检查），
	/// 一旦数据库就绪**当帧立刻开始枚举**，不再有任何等待窗口可被场景切换截断。
	/// 另：预算只在"数据库就绪之后"起算，主菜单的等待不再消耗枚举预算。
	///
	/// **2.0.5（2.0.4 实测仍失败）**：枚举 0 条时**不再 `Failed=true` 永久放弃**，
	/// 而是复位 `probeState=0` 交给看门狗下一轮重启（`Add` 去重保证幂等）。
	/// 就绪探测为何失败由 `LogProbeDiag` 每 5s 记一次原始值。
	/// </summary>
	private static IEnumerator ProbeCR()
	{
		System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
		long budgetUntil = 0;

		// 逐类目枚举（enum 值：items=6, weapons=7, ammo=8, attachment=9）
		int[] types = { 6, 7, 8, 9 };

		// 2.0.7：**废弃"就绪闸门"，直接尝试枚举**。
		// 前四轮全在修"等闸门的循环活不活"，而 2.0.6 日志证明**闸门条件本身就是错的**
		// （`Loaded` 恒 true、`items`(6) 恒空）→ 闸门永远不开，后面再怎么重试都没用。
		// 现在不再有任何"必须满足才开工"的前置条件：
		// 拿到条目就用，拿不到就下一轮重试（`Add` 按 Id 去重，重复枚举幂等）。
		// 这样"闸门条件猜错"这一类故障被**整体消除**，而不是再猜一次。
		for (int attempt = 0; attempt < 5; attempt++)
		{
			SampleCounts();    // 记录四类真实条数（节流打印）——只观测，不阻塞
			probeTicks++;      // 列表为空时也要涨，否则看门狗会误判"协程已死"
			yield return EnumerateAll(types, sw, budgetUntil);
			probeTicks++;

			if (TotalCount() > 0) break;

			yield return null;
		}

		SortAll();

		if (TotalCount() == 0)
		{
			// 2.0.5：**不再永久放弃**——数据库可能只是这一帧还没填表。
			// 复位为"未开始"，由看门狗在下一秒重启；Add 去重保证幂等。
			Plugin.ModLog.LogWarning("[UniGen] 物品枚举本次仍为 0 条，交由看门狗稍后重试（不放弃）。");
			probeState = 0;
			yield break;
		}

		Ready = true;
		probeState = 2;
		nextEnrichAt = UnityEngine.Time.unscaledTime + 20f;  // 2.0.7：20s 后补漏重扫一次
		var sb = new StringBuilder();
		foreach (string k in BucketOrder)
			if (Buckets.TryGetValue(k, out List<ItemEntry> l2)) sb.Append(" ").Append(k).Append("=").Append(l2.Count);
		Plugin.ModLog.LogInfo("[UniGen] 物品目录就绪(来源=运行时 ItemsDatabase 枚举):"
			+ sb + " 合计=" + TotalCount() + "；耗时 " + sw.ElapsedMilliseconds + "ms");

		try { onReady?.Invoke(); } catch { }
		onReady = null;
	}

	/// <summary>
	/// 逐类目枚举四个 PropType（时间片 3ms/帧，可跨帧 yield）。
	///
	/// **2.0.7：两类数据源都枚举** —— ① `ItemObject`（主）② `PropData`（兜底）。
	/// 理由：`GetAllItemsOfType&lt;T&gt;` 极可能是**按泛型 T 过滤**的，而数据库里存的是
	/// `ItemObject`（有 `item_id`/`icon`；`GetItemObject(id)` 返回的正是它）。
	/// 2.0.2~2.0.6 只问了 `PropData`，于是恒为 0 —— 这就是"物品分类一个都不出现"的根因。
	/// `Add` 按 Id 去重，两条路重复也只会保留一条。
	/// </summary>
	private static IEnumerator EnumerateAll(int[] types, System.Diagnostics.Stopwatch sw, long budgetUntil)
	{
		foreach (int t in types)
		{
			// ① ItemObject —— 真正的物品（`item_id` 即 `GetItemObject` / 生成所用的键）
			Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<ItemObject> objs = null;
			try { objs = ItemsDatabase.GetAllItemsOfType<ItemObject>((PropData.PropType)t); }
			catch (Exception ex)
			{
				if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] 枚举 ItemObject PropType " + t + " 失败: " + ex.Message);
			}
			if (objs != null)
			{
				for (int i = 0; i < objs.Count; i++)
				{
					try
					{
						ItemObject io = objs[i];
						if (io == null) continue;

						string id = null;
						try { id = io.item_id; } catch { }
						if (string.IsNullOrEmpty(id)) continue;
						if (id.StartsWith("test_", StringComparison.OrdinalIgnoreCase)) continue;

						// 2.4.0：显示名**优先走游戏自己的映射接口** `GetMappedResourcesName()`
						//（MappedResources.prefs → PropData.name，游戏 UI 的 4 处原生调用方都用它），
						// 退回 `io.name`（GameObject 名，常是 "arisaka t38carbine" 这类内部小写名），
						// 再退回 id。mod 物品的注册名是什么就显示什么（第三方数据，本 mod 不改写）。
						string display = null;
						try { display = io.GetMappedResourcesName(); } catch { }
						if (string.IsNullOrEmpty(display)) { try { display = io.name; } catch { } }

						Add(id, display, BucketFor((PropData.PropType)t, id), SubFor(io));
					}
					catch { }

					if (sw.ElapsedMilliseconds >= budgetUntil)
					{
						budgetUntil = sw.ElapsedMilliseconds + 3;
						probeTicks++;
						yield return null;
					}
				}
			}

			// ② PropData —— 兜底（旧路径；两边都非空时由 Add 去重）
			Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<PropData> list = null;
			try { list = ItemsDatabase.GetAllItemsOfType<PropData>((PropData.PropType)t); }
			catch (Exception ex)
			{
				if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] 枚举 PropType " + t + " 失败: " + ex.Message);
			}
			if (list == null) continue;

			for (int i = 0; i < list.Count; i++)
			{
				try
				{
					PropData pd = list[i];
					if (pd == null) continue;
					if (pd.deprecated) continue;          // 已废弃资源不列
					if (pd.mod_id != 0u) continue;        // 第三方内容不列（1.3.2 决定：只生成官方内容）

					string id = null;
					try { id = pd.prefab_name; } catch { }
					if (string.IsNullOrEmpty(id)) continue;
					if (id.StartsWith("test_", StringComparison.OrdinalIgnoreCase)) continue;

					string display = null;
					try { display = pd.name; } catch { }

					string bucket = BucketFor((PropData.PropType)t, id);
					Add(id, display, bucket);
				}
				catch { }

				// 时间片：每帧最多 3ms（1.2.0 教训：同步跑数百次原生调用会冻结主线程）
				if (sw.ElapsedMilliseconds >= budgetUntil)
				{
					budgetUntil = sw.ElapsedMilliseconds + 3;
					probeTicks++;   // 2.0.6：心跳计数
					yield return null;
				}
			}
		}
	}

	/// <summary>全部桶按标题排序。</summary>
	private static void SortAll()
	{
		foreach (List<ItemEntry> l in Buckets.Values)
			l.Sort((a, b) => string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase));
	}

	/// <summary>注册条目（同 Id 去重——看门狗重启续跑时可能重复枚举）。</summary>
	private static void Add(string id, string display, string bucket, string sub = "")
	{
		if (!Buckets.TryGetValue(bucket, out List<ItemEntry> list))
		{
			list = new List<ItemEntry>();
			Buckets[bucket] = list;
		}
		for (int i = 0; i < list.Count; i++)
			if (string.Equals(list[i].Id, id, StringComparison.OrdinalIgnoreCase)) return;

		list.Add(new ItemEntry
		{
			Id = id,
			Title = Pretty(string.IsNullOrEmpty(display) ? id : display),
			Bucket = bucket,
			Sub = sub ?? ""
		});
	}

	/// <summary>
	/// 2.2.0：注册一个**校验通过**的第三方物品（ModCatalog 调用）。
	/// id 已经 `GetItemObject(id)` 验证非 null —— 只可能出现"列表里有就能生成"。
	/// 与官方条目分开一个桶（"Mod物品"页签），避免混入官方目录造成"哪个是 mod 的"不可辨。
	/// </summary>
	internal static void AddModItem(string id, string display)
	{
		if (string.IsNullOrEmpty(id)) return;
		int before = TotalCount();
		Add(id, display, "mod");
		if (TotalCount() > before) modItemsAdded++;
	}

	/// <summary>2.2.0：本次会话新增的 mod 物品数（供 ModCatalog 判断是否需要重建页签）。</summary>
	internal static int modItemsAdded;

	/// <summary>目录就绪后的回调（面板重建页签用）。</summary>
	internal static Action onReady;

	/// <summary>
	/// 2.0.7：**闸门不再依赖某一个具体 PropType**。
	///
	/// **实测定案（2.0.6 日志，第 11 轮续三）**：`ItemsDatabase.Loaded` **恒为 true**，
	/// 但 `GetAllItemsOfType<PropData>(PropType.items)`（=6）**在 Aberdeen 战斗场景里
	/// 等了 154 秒仍然返回空数组** → 前几轮修的全部是"协程活不活"，
	/// 而真正的病灶是**我把就绪判据挂在了 `items` 这一个类别上**。
	/// 游戏里可生成的物品很可能主要落在 `weapons`/`ammo`/`attachment`，
	/// 而 `items`(6) 只是杂项总类、平时为空。
	///
	/// 现在改为：**只要四个类别里任意一个非空，就认为数据库可枚举**。
	/// 并把四个类别的真实数量记录下来（`LastCounts`）供日志与后续排查。
	/// </summary>
	/// <summary>
	/// 2.0.7：**只观测、不阻塞**。采样四个 PropType 的真实条数，节流打印。
	///
	/// 这是 2.0.3~2.0.6 那个"就绪闸门"的替代品。闸门版本（`ProbeDatabaseReady`）
	/// 会 `return false` 挡住整个枚举流程，而它的条件（items 类别非空）实测恒不成立
	/// → **流程永远卡在闸门前**，后面设计得再健壮的重试也一次都跑不到。
	/// 观测版本不返回"是否就绪"，因此**不存在被错误条件挡住的可能**；
	/// 它唯一的作用是把真实条数写进日志（供人判断，而非供程序决策）。
	/// </summary>
	/// <summary>
	/// 2.0.7：**同时探测两种泛型实参**并都记进日志。
	/// `IO` = `GetAllItemsOfType&lt;ItemObject&gt;`、`PD` = `GetAllItemsOfType&lt;PropData&gt;`。
	/// 2.0.3~2.0.6 一直只问 PD，而它恒为 0；本轮起两条路都走，谁有内容用谁。
	/// </summary>
	private static void SampleCounts()
	{
		bool any = false;
		string detail = "";
		foreach (int t in ProbeTypes)
		{
			int nIO = CountItemObjects(t);
			int nPD = CountPropDatas(t);
			LastCounts[t] = nIO;
			detail += " " + ((PropData.PropType)t) + "=IO:" + nIO + "/PD:" + nPD;
			if (nIO > 0 || nPD > 0) any = true;
		}

		if (!any)
		{
			LogProbeDiag("四类全空:" + detail);
			return;
		}
		if (!loggedReadyDetail)
		{
			loggedReadyDetail = true;
			Plugin.ModLog.LogInfo("[UniGen] 物品库可枚举(IO=ItemObject, PD=PropData):" + detail);
		}
	}

	/// <summary>2.0.7：探针类别（items=6 杂项 / weapons=7 / ammo=8 / attachment=9）。</summary>
	private static readonly int[] ProbeTypes = { 6, 7, 8, 9 };

	/// <summary>2.0.7：上一次各类别的条目数（供日志排查）。</summary>
	internal static readonly Dictionary<int, int> LastCounts = new Dictionary<int, int>();

	private static bool loggedReadyDetail;

	/// <summary>2.0.7：Ready 之后一次性"补漏"重扫的时间点（数据库可能首轮只加载了一部分）。</summary>
	private static float nextEnrichAt;
	private static bool enrichDone;

	/// <summary>
	/// 2.0.7：补漏重扫——首轮拿到条目后 20s 再扫一遍。
	/// `Add` 按 Id 去重，所以只会"补进新条目"，不会重复；条目数有增长才重建页签。
	/// 目的：数据库若是**分批加载**，首轮可能只拿到一部分，补漏保证目录最终完整。
	/// </summary>
	private static IEnumerator EnrichCR()
	{
		System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
		long budgetUntil = 0;
		int[] types2 = { 6, 7, 8, 9 };

		int before = TotalCount();
		yield return EnumerateAll(types2, sw, budgetUntil);
		SortAll();
		int after = TotalCount();

		if (after > before)
		{
			Plugin.ModLog.LogInfo("[UniGen] 物品目录补漏: " + before + " → " + after + " 条，重建物品页签。");
			try { GenPanel.RebuildItemTabsPublic(); } catch { }
		}
	}

	/// <summary>
	/// 2.0.7：安全取某类别的条目数。
	/// `ItemObject` 是**真正的物品类型**（有 `item_id` / `icon`，`GetItemObject(id)` 返回的就是它），
	/// `PropData` 是道具元数据（2.0.2~2.0.6 一直用它，实测恒 0）。
	/// null → -1，异常 → -2。
	/// </summary>
	private static int CountItemObjects(int type)
	{
		try
		{
			Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<ItemObject> l =
				ItemsDatabase.GetAllItemsOfType<ItemObject>((PropData.PropType)type);
			return (l == null) ? -1 : l.Count;
		}
		catch { return -2; }
	}

	private static int CountPropDatas(int type)
	{
		try
		{
			Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<PropData> l =
				ItemsDatabase.GetAllItemsOfType<PropData>((PropData.PropType)type);
			return (l == null) ? -1 : l.Count;
		}
		catch { return -2; }
	}

	/// <summary>
	/// 2.0.5 诊断：把"闸门为何不放行"的原始值打到日志（每 5 秒最多一条，避免刷屏）。
	/// 2.0.3/2.0.4 两轮都卡在"闸门永远 false 但不知为何"——必须让日志自己说话。
	/// </summary>
	private static void LogProbeDiag(string why)
	{
		float now = UnityEngine.Time.unscaledTime;
		if (now - lastDiagAt < 5f) return;
		lastDiagAt = now;
		Plugin.ModLog.LogInfo("[UniGen] 物品库就绪探测: " + why + "（已等 " + (now - probeStartedAt).ToString("F1") + "s, scene=" + UnityEngine.SceneManagement.SceneManager.GetActiveScene().name + "）");
	}

	/// <summary>
	/// 桶归类（2.0.2）：**优先用游戏自己的 PropType**，只有 `items`（= 杂项总类）才落到
	/// 命名启发式细分（弹药/投掷物/医疗食物/装备）。
	/// 这样"是不是武器""是不是弹药"由游戏判定，不再靠猜。
	/// </summary>
	private static string BucketFor(PropData.PropType type, string id)
	{
		switch ((int)type)
		{
			case 7: return "weapons";     // PropType.weapons
			case 8: return "ammo";        // PropType.ammo
			case 9: return "gear";        // PropType.attachment
		}

		// PropType.items（= 6，杂项总类）：命名启发式细分
		string s = id.ToLowerInvariant();
		if (s.StartsWith("grenade", StringComparison.Ordinal) || s.StartsWith("smokegrenade", StringComparison.Ordinal)
			|| s.Contains("satchel") || s.Contains("geballte") || s.Contains("molotov")
			|| s.Contains("dynamite") || s.Contains("tnt") || s.Contains("tankmine")
			|| s.Contains("kaenbin") || s.StartsWith("at_", StringComparison.Ordinal)) return "throwables";

		if (s.Contains("bandage") || s.Contains("medkit") || s.Contains("morphine") || s.Contains("syringe")
			|| s.Contains("aspirin") || s.Contains("aspirine") || s.Contains("sulfa")
			|| s.Contains("ration") || s.Contains("can") || s.Contains("food")
			|| s.Contains("canteen") || s.Contains("chocolate")) return "food";

		if (s.Contains("ammo") || s.Contains("_mag") || s.Contains("magazine")
			|| s.Contains("_belt") || s.Contains("_clip") || s.Contains("round") || s.Contains("shell")) return "ammo";

		string[] gearKeys =
		{
			"scope", "bipod", "bayonet", "knife", "shovel", "toolbox", "backpack",
			"radio", "binocular", "helmet", "vest", "gear", "harness", "pouch", "holster",
			"belt", "strap", "wire", "flag", "tripod"
		};
		foreach (string k in gearKeys)
			if (s.Contains(k)) return "gear";

		if (s.Contains("crate") || s.Contains("key") || s.Contains("note") || s.Contains("map_"))
			return "misc";

		return "weapons"; // 兜底：items 类目里绝大多数是枪械（2.0.1 同款结论）
	}

	/// <summary>id/显示名 → 列表行标题：下划线转空格（显示名一般已是可读文本，此处仅稳妥处理）。</summary>
	private static string Pretty(string s)
	{
		if (string.IsNullOrEmpty(s)) return s;
		string t = s.Replace('_', ' ').Trim();
		return t.Length > 34 ? t.Substring(0, 33) + "…" : t;
	}
}
