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

	/// <summary>v2.5.37：磁盘缓存是否已就绪（读盘命中或战斗场景中写盘成功）。
	/// false 且已 Ready 时，看门狗会在进入战斗后复位重扫（修复 v2.5.36 固化不完整缓存的回归）。</summary>
	internal static bool cacheWritten;

	/// <summary>v2.5.38：Ready 的时间戳（items 类延迟补漏的 30s 计时起点）。</summary>
	private static float readyAt;

	/// <summary>v2.5.38：items 类补漏是否已执行（每会话一次）。</summary>
	private static bool lateDone;

	/// <summary>v2.5.39：读盘缓存是否已验证（战斗场景中与实时枚举比对过）。
	/// v2.5.37 写的 560 条缓存带着 full=1 却是残表——"读盘命中 = 零验证"是设计缺陷：
	/// 残表会被永久使用。现在命中后战斗场景中后台跑一次验证枚举（等 bundle 加载完），
	/// 与缓存比对，有差异则更新内存/磁盘/页签。每会话一次。</summary>
	private static bool cacheVerified;

	/// <summary>验证枚举与缓存的条数差超过该值即判定"缓存过期"（更新之）。</summary>
	private const int VerifyDiffThreshold = 0;

	/// <summary>v2.5.43：一次性诊断——看门狗首次被调用 / Ready 后台轮首次判定，各记一条。</summary>
	private static bool wdFirstLogged;
	private static bool readyDiagLogged;

	/// <summary>v2.5.45：已验证轮数 / 下一轮触发时刻。官方物品库随进程逐步长齐（实测开局 1164 →
	/// 久玩 1451），单轮快照必然偏少 → 多轮累积，直到某一轮不再新增。</summary>
	private static int verifyPasses;
	private static float nextVerifyAt;

	/// <summary>v2.5.46：正在跑的验证轮数（&gt;0 = 有轮在跑，看门狗不再触发下一轮 → 杜绝重叠执行）。</summary>
	private static int verifyActive;

	/// <summary>v2.5.45：单会话最多跑几轮验证（间隔逐轮放宽，避免无休止的原子冻结）。</summary>
	private const int MaxVerifyPasses = 3;

	/// <summary>v2.5.47：**官方条目**新增 ≤ 该值即判定"目录已稳定"。刻意不是"必须恰好 0"——
	/// 游戏内部还会零星登记几条，若要求 0 则判据永远不成立，验证轮永不收敛 = 每次启动白付数秒冻结
	/// （玩家："进游戏后总是卡一会"）。漏掉的那几条下次验证会自然并进来，不影响可用性。</summary>
	private const int MaxStableGrowth = 5;

	/// <summary>v2.5.47：官方物品条目数（**排除 mod 桶**）。稳定性判定必须用它——mod 物品由
	/// ModCatalog 异步分轮补入，混入判定会把"mod 在长"当成"官方目录在长"。</summary>
	private static int OfficialCount()
	{
		int n = 0;
		try
		{
			foreach (KeyValuePair<string, List<ItemEntry>> kv in Buckets)
			{
				if (kv.Key == "mod" || kv.Value == null)
				{
					continue;
				}
				n += kv.Value.Count;
			}
		}
		catch
		{
		}
		return n;
	}

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
		if (Ready || probeState == 1) return;
		if (probeState == 3) probeState = 0; // 旧版"已放弃"不再视为终态
		Begin();
	}

	internal static void Begin()
	{
		if (Ready || probeState == 1) return;
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
	///
	/// **2.0.7 的"就绪 20s 后自动补漏"已废弃（v2.5.36）**：实测单次 GetAllItemsOfType 最高
	/// 5.5 秒且为原子调用（时间片切不开），自动补漏 = 进游戏后固定再来一轮数秒冻结；
	/// 其"数据库分批加载补条目"的使命由**磁盘缓存**取代（首次枚举写盘，之后零调用）。
	///
	/// **v2.5.37/40 的场景判定已全部删除（v2.5.41）**：连续两个"看起来合理"的判据
	///（`MainMenu.instance == null`、`BattleManager.IsBattleActive()`）都被实测证伪
	///（前者恒非 null、后者在 RTS 上帝视角返回 false）→ 探测/写盘/验证轮被整体跳过，
	/// 物品页签直接消失（玩家："大选项 4 行变 3 行"）。
	/// **最终定案：场景/战斗判定彻底移除，枚举完整性由 `WaitForAssetBundleLoaded("er2bundle")`
	/// 挂起等待保证**——bundle 没加载完就挂着等（零成本），加载完自动继续（条目完整）。
	/// 任何场景（含主菜单）都可以安全探测/写盘/验证。
	///
	/// **v2.5.42：Ready 后台轮必须放在 early-return 之前** —— v2.5.40/41 把验证/补漏触发块
	/// 写在 `if (Ready) return;` **之后**，而读盘命中使 Ready 在启动瞬间就为 true →
	/// 本函数每秒都在第一行返回，那段代码是**死代码**：验证轮从未启动过，
	/// 磁盘缓存停在 560 条残表永不更新（玩家："列表在进战斗后一直没有变化"）。
	/// 教训：**给"提前退出"的函数加分支时，新逻辑若服务于"已就绪"状态，
	/// 必须放在退出之前**——就绪态恰恰是 early-return 最先吞掉的状态。
	/// </summary>
	internal static void ProbeWatchdog()
	{
		// v2.5.43：一次性探针——记录"看门狗是否真的被调用、当时状态如何"。
		// 前几轮反复出现"日志里什么都没有"，无法区分"没被调用"与"条件不满足"；
		// 这一条把两者分开（下轮日志只要有它，就能确定调用链是通的）。
		if (!wdFirstLogged)
		{
			wdFirstLogged = true;
			Plugin.ModLog.LogInfo("[UniGen] 看门狗首次调用: Ready=" + Ready + " probeState=" + probeState
				+ " cacheWritten=" + cacheWritten + " lateDone=" + lateDone
				+ " readyAt=" + readyAt.ToString("F2") + " now=" + UnityEngine.Time.unscaledTime.ToString("F2"));
		}

		if (Ready)
		{
			if (probeState != 2) return; // 兜底（理论不可达：Ready 必然 probeState==2）

			// v2.5.43：**计时基线自愈**。读盘路径的 `readyAt` 取自"插件 Awake 那一刻"的
			// `Time.unscaledTime`——那时游戏**一帧都没跑过，值为 0** → 下面 `readyAt > 0f`
			// 的守卫把两条后台轮**永久拒之门外**（玩家连续两轮反馈"列表/加载没有变化"，
			// 日志里连"开始验证"都不出现；而枚举路径的 readyAt 在游戏中设定，必然 >0，
			// 所以历史上只有读盘路径失效）。任何"进入 Ready 却没人建立基线"的情况，这里补上。
			if (readyAt <= 0f)
			{
				readyAt = UnityEngine.Time.unscaledTime;
				nextVerifyAt = readyAt + 10f; // v2.5.45：首轮验证的触发时刻
				Plugin.ModLog.LogInfo("[UniGen] 后台轮计时基线缺失（readyAt=0，读盘在 Awake 建立）→ 现补建 "
					+ readyAt.ToString("F1") + "s，10s 后触发验证轮。");
			}
			if (!readyDiagLogged)
			{
				readyDiagLogged = true;
				Plugin.ModLog.LogInfo("[UniGen] Ready 后台轮判定: probeState=" + probeState
					+ " cacheWritten=" + cacheWritten + " lateDone=" + lateDone
					+ " cacheVerified=" + cacheVerified + " readyAt=" + readyAt.ToString("F1")
					+ " now=" + UnityEngine.Time.unscaledTime.ToString("F1"));
			}

			// 两条后台轮**互斥**，绝不同时会跑（否则同一批原子调用做两遍 = 双倍冻结）：
			// - 读盘命中（cacheWritten=true）→ 只跑 VerifyCacheCR（多轮累积，见下）；
			// - 运行时首次枚举（无缓存，cacheWritten=false）→ 只跑 LateEnrichCR（items 类 2 次）。
			if (!cacheWritten && !lateDone && readyAt > 0f && UnityEngine.Time.unscaledTime >= readyAt + 30f)
			{
				lateDone = true;
				try { GenRunner.StartCoroutine(LateEnrichCR()); } catch (Exception ex) { Plugin.ModLog.LogWarning("[UniGen] items 补漏启动失败: " + ex.Message); }
			}
			// v2.5.45：**多轮累积验证**。官方物品库是随游戏进程逐步长出来的——同一份代码实测：
			// 开局 ~23s 枚举得 1164 条（weapons 549 / gear 407），而长时间游玩后可到 1451 条
			//（weapons 701 / gear 542）。单轮快照必然偏少，所以：每轮只增不减地并入（Add 按 Id
			// 去重），直到某一轮**不再新增**才判定稳定；间隔逐轮放宽（10s → +30s → +120s），
			// 单会话最多 MaxVerifyPasses 轮。稳定那轮才把缓存标 verified=1（见 VerifyCacheCR）。
			else if (cacheWritten && !cacheVerified && verifyActive == 0 && verifyPasses < MaxVerifyPasses)
			{
				if (nextVerifyAt <= 0f) nextVerifyAt = UnityEngine.Time.unscaledTime;
				if (UnityEngine.Time.unscaledTime >= nextVerifyAt)
				{
					verifyPasses++;
					// v2.5.46：**间隔由上一轮结束时设定**（VerifyCacheCR 的 finally：完成时刻 + 30/120s）。
					// 上一版在这里预设间隔，而单轮耗时 30~60s → 间隔早已过期 → 两轮重叠执行，
					// 同一批原子调用做两遍（实测日志两行同号"第 2 轮"，冻结翻倍）。
					try { GenRunner.StartCoroutine(VerifyCacheCR()); }
					catch (Exception ex)
					{
						nextVerifyAt = UnityEngine.Time.unscaledTime + 30f; // 启动失败不空转
						Plugin.ModLog.LogWarning("[UniGen] 缓存验证启动失败: " + ex.Message);
					}
				}
			}
			return; // Ready 路径处理完毕（轮已触发或未到触发时间）
		}

		// 未开始 → 启动
		if (probeState == 0)
		{
			probeRestarts++;
			if (probeRestarts == 1)
				Plugin.ModLog.LogInfo("[UniGen] 物品目录探测启动（看门狗）");
			Begin();
			return;
		}

		// 进行中 → tick 判活（2.0.6 正解）
		if (probeState == 1)
		{
			if (probeTicks == watchdogLastTicks)
			{
				Plugin.ModLog.LogWarning("[UniGen] 物品目录探测已停止（tick 停在 " + probeTicks + "），自动重启补齐（第 "
					+ probeRestarts + " 次，已有 " + TotalCount() + " 条）。");
				probeState = 0;
				return;
			}
			watchdogLastTicks = probeTicks;
		}
	}

	/// <summary>
	/// v2.5.39：读盘缓存的验证枚举——等 bundle 加载完 → 真实枚举四类 → 与"验证前条数"比对。
	/// 有差异（游戏更新/残表）→ 重写磁盘缓存 + 重建页签；一致 → 静默。
	/// 注意：本协程期间 GenPanel 仍用旧数据照常工作，比对完成后才切换。
	/// </summary>
	private static IEnumerator VerifyCacheCR()
	{
		// v2.5.46：捕获**本轮自己的编号**。上一版日志打的是全局 `verifyPasses`，而调度只按时间
		// 间隔触发、不等上一轮结束 → 两轮重叠时两行都印成"第 2 轮"（实测日志：1669→1921 与
		// 1164→1921 两行同号），既看不懂又白付一倍的原子冻结。
		int myPass = verifyPasses;
		verifyActive++; // 重叠守卫：本轮未结束前，看门狗不再触发下一轮
		try
		{
			System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
			long budgetUntil = 0;
			int[] allTypes = { 6, 7, 8, 9 };

			InvalidateTypeCache(); // 验证 = 必须真实枚举（缓存结果无意义）
			// v2.5.47：**稳定性判定只看官方条目**。mod 物品由 ModCatalog 异步分轮补入，
			// 用 TotalCount() 会把"mod 那侧还在长"误判成"官方目录还在长" → 永远判不出稳定 →
			// **每次启动都重跑验证轮**（每轮数次原子调用、实测单次最高 8.6 秒冻结；
			// 玩家："不知道为什么进游戏后总是要卡一会"）。这是本节的第二类"判据选错对象"错误。
			int before = OfficialCount();
			int modBefore = Bucket("mod").Count;
			Plugin.ModLog.LogInfo("[UniGen] 开始验证物品目录（第 " + myPass + " 轮，官方 " + before
				+ " 条 + mod " + modBefore + " 条，等 er2bundle 加载后实时枚举并并入）…");
			yield return ItemsDatabase.WaitForAssetBundleLoaded("er2bundle");
			yield return EnumerateAll(allTypes, sw, budgetUntil);
			SortAll();
			int after = OfficialCount();
			int modAfter = Bucket("mod").Count;

			int grew = after - before;
			Plugin.ModLog.LogInfo("[UniGen] 验证第 " + myPass + " 轮: 官方 " + before + " → " + after
				+ "（新增 " + grew + "；判定依据）· mod " + modBefore + " → " + modAfter
				+ "（不计入判定）" + BucketsBrief() + " 耗时 " + sw.ElapsedMilliseconds + "ms");

			if (grew > MaxStableGrowth)
			{
				// 目录仍在长（官方库随进程逐步加载）→ 写盘但**不标 verified**
				//（未稳定前不标，免得把"还没长齐"的快照永久固化），下一轮继续补。
				WriteCacheToDisk(false);
				try { GenPanel.RebuildItemTabsPublic(); } catch { }
				Plugin.ModLog.LogInfo("[UniGen] 官方目录仍在增长（新增 " + grew + " > " + MaxStableGrowth
					+ "），已并入并写盘；"
					+ (myPass < MaxVerifyPasses
						? "下一轮继续验证（本会话共 " + MaxVerifyPasses + " 轮）。"
						: "验证轮已跑满 " + MaxVerifyPasses + " 轮，本会话到此为止——下次启动会继续补齐。"));
			}
			else
			{
				// 官方条目几乎不再增长 = 目录已稳定 → 现在才允许标 verified=1（后续启动跳过验证枚举）。
				cacheVerified = true;
				WriteCacheToDisk(true);
				Plugin.ModLog.LogInfo("[UniGen] 官方目录已稳定（本轮新增 " + grew + " ≤ " + MaxStableGrowth
					+ "）→ 标记 verified=1：后续启动不再做验证枚举，零原子调用、零冻结。");
			}
		}
		finally
		{
			// 正常结束或协程被场景切换杀死都会归零（yield 型方法里的 finally 合法）。
			verifyActive--;
			// 下一轮从**本轮结束**起算间隔——这样"单轮耗时 30~60s"不会再和固定间隔打架造成重叠。
			nextVerifyAt = UnityEngine.Time.unscaledTime + (verifyPasses <= 1 ? 30f : 120f);
		}
	}

	/// <summary>v2.5.37：清空内存目录与 API 缓存，复位为"未开始"（完整重扫用）。
	/// `Add` 按 Id 去重 + mod 物品由 ModCatalog 独立扫描，复位不会造成重复或丢失。</summary>
	private static void ResetForFullRescan()
	{
		Buckets.Clear();
		typeCache.Clear();
		Ready = false;
		probeState = 0;
		probeRestarts = 0;
		watchdogLastTicks = -1;
		modItemsAdded = 0;
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

		// v2.5.36：**磁盘缓存优先**。实测单次 GetAllItemsOfType 最高 5.5 秒且为原子调用（无法分帧），
		// 是"进游戏固定时间后卡几秒"的元凶；而官方物品目录基本静态——首次枚举写盘，之后每次启动
		// 直接读盘，**零枚举调用**。游戏更新新增物品后勾 cfg `refreshItemCache` 一次即可刷新。
		if (TryLoadCacheFromDisk())
		{
			try { onReady?.Invoke(); } catch { }
			onReady = null;
			yield break;
		}

		// v2.5.38：**等官方物品 bundle 完全加载再枚举**——这是"枚举完整性"的硬保证。
		// 之前的两轮实测（475/560 条 vs 完整 1509 条）证明：ItemsDatabase 的内容随
		// asset bundle 渐进加载（物品定义都在 er2bundle 里），任何时候"顺手枚举"都可能
		// 拿到半库并把半库写进磁盘缓存（v2.5.36/2.5.37 两次回归同根）。
		// WaitForAssetBundleLoaded：已加载 → 立即返回；未加载 → 挂起等待，加载完自动继续。
		yield return ItemsDatabase.WaitForAssetBundleLoaded("er2bundle");
		Plugin.ModLog.LogInfo("[UniGen] er2bundle 已就绪，开始枚举物品目录（bundle 加载完成 = 条目完整的前提）。");

		// v2.5.38：**首轮只枚举核心三类**（ItemObject × weapons/ammo/attachment——实测贡献
		// 1451/1509 条）；items(6) 类（投掷物/食物/杂项 58 条）的两次调用最慢（PropData(items)
		// 实测 4.5~8 秒），挪到 Ready 后 30s 的补漏轮（见 LateEnrichCR），面板先可用。
		// PropData(weapons/ammo/attachment) 兜底路径**删除**：2.0.7 实测恒为 0，纯浪费 3 次慢调用。
		int[] coreTypes = { 7, 8, 9 };

		// 2.0.7：**废弃"就绪闸门"，直接尝试枚举**。
		// 前四轮全在修"等闸门的循环活不活"，而 2.0.6 日志证明**闸门条件本身就是错的**
		// （`Loaded` 恒 true、`items`(6) 恒空）→ 闸门永远不开，后面再怎么重试都没用。
		// 现在不再有任何"必须满足才开工"的前置条件：
		// 拿到条目就用，拿不到就下一轮重试（`Add` 按 Id 去重，重复枚举幂等）。
		// 这样"闸门条件猜错"这一类故障被**整体消除**，而不是再猜一次。
		for (int attempt = 0; attempt < 5; attempt++)
		{
			probeTicks++;      // 列表为空时也要涨，否则看门狗会误判"协程已死"
			yield return EnumerateAll(coreTypes, sw, budgetUntil);
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
		readyAt = UnityEngine.Time.unscaledTime; // v2.5.38：items 补漏的计时起点
		// v2.5.41：无条件写盘——枚举完整性已由 WaitForAssetBundleLoaded 挂起等待保证
		//（bundle 加载完 = 条目全），场景判定被实测证伪后彻底移除。
		WriteCacheToDisk();
		cacheWritten = true;
		var sb = new StringBuilder();
		foreach (string k in BucketOrder)
			if (Buckets.TryGetValue(k, out List<ItemEntry> l2)) sb.Append(" ").Append(k).Append("=").Append(l2.Count);
		Plugin.ModLog.LogInfo("[UniGen] 物品目录就绪(来源=运行时 ItemsDatabase 枚举):"
			+ sb + " 合计=" + TotalCount() + "；耗时 " + sw.ElapsedMilliseconds + "ms");

		try { onReady?.Invoke(); } catch { }
		onReady = null;
	}

	/// <summary>
	/// v2.5.38：**延迟补漏轮**——Ready 后 30s 由看门狗触发一次，只补 items(6) 类
	///（投掷物/食物/杂项，约 58 条）：ItemObject(items) + PropData(items) 两次调用，
	/// 实测是全部调用里最慢的两次（4.5~8 秒原子冻结），所以单独拆出来错峰，
	/// 不与核心三类（面板立即可用的 96%）抢同一时间段。补到条目就更新页签并重写磁盘缓存。
	/// </summary>
	private static IEnumerator LateEnrichCR()
	{
		System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
		long budgetUntil = 0;
		int[] lateTypes = { 6 };

		InvalidateTypeCache(); // 重新真实枚举 items 类（缓存里可能没有或已过期）
		int before = TotalCount();
		yield return EnumerateAll(lateTypes, sw, budgetUntil);
		SortAll();
		int after = TotalCount();

		Plugin.ModLog.LogInfo("[UniGen] items 类补漏完成: " + before + " → " + after + " 条，耗时 "
			+ sw.ElapsedMilliseconds + "ms（含 2 次原子调用，无法分帧）");
		cacheVerified = true; // v2.5.40：刚完成 items 补漏 = 目录最新，无需再跑验证轮
		if (after > before)
		{
			WriteCacheToDisk();
			try { GenPanel.RebuildItemTabsPublic(); } catch { }
		}
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
			try { objs = GetAllCached<ItemObject>(t); }
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
			try { list = GetAllCached<PropData>(t); }
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

	// ── v2.5.36：物品目录磁盘缓存 ─────────────────────────────
	// 实测单次 GetAllItemsOfType 最高 5.5 秒且为原子调用（无法分帧）= "进游戏固定时间卡几秒"的元凶；
	// 而官方物品目录基本静态 → 首次枚举写盘，之后每次启动直接读盘（零枚举调用）。
	// 游戏更新新增物品后勾 cfg Catalog.refreshItemCache 一次即可刷新。

	private static string CacheFilePath()
	{
		return System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "er2.universalgeneration.items.cache.tsv");
	}

	/// <summary>单文件签名（大小:修改时间秒）。不存在 → "0"。</summary>
	private static string SigOf(string path)
	{
		try
		{
			System.IO.FileInfo fi = new System.IO.FileInfo(path);
			if (!fi.Exists) return "0";
			return fi.Length.ToString() + ":" + new DateTimeOffset(fi.LastWriteTimeUtc).ToUnixTimeSeconds().ToString();
		}
		catch { return "0"; }
	}

	/// <summary>
	/// v2.5.44：**游戏构建签名**——用于判断"官方物品目录是否有变化的可能"。
	/// 三者合计：GameAssembly.dll（程序集）+ global-metadata.dat（IL2CPP 元数据）+
	/// StreamingAssets/CorvoBundles/er2bundle（物品定义所在资源包）。
	/// 签名一致 = 游戏没更新过 → 磁盘缓存可直接采信，**跳过验证枚举**。
	/// 为什么必须跳过：验证枚举实测 4 次原子调用、单次最高 8.1s、**合计 54.6 秒冻结**，
	/// 每次启动都付一遍是不可接受的（玩家早先就抱怨过"固定时间卡顿"）。
	/// 取不到关键文件 → 返回 "unknown"（调用方视为"不可信"，照常验证）。
	/// 手动刷新入口始终保留：cfg `Catalog / refreshItemCache`。
	/// </summary>
	private static string CacheSig()
	{
		try
		{
			string root = System.IO.Path.GetFullPath(System.IO.Path.Combine(BepInEx.Paths.ConfigPath, "..", ".."));
			string a = SigOf(System.IO.Path.Combine(root, "GameAssembly.dll"));
			string b = SigOf(System.IO.Path.Combine(root, "Easy Red 2_Data", "il2cpp_data", "Metadata", "global-metadata.dat"));
			string c = SigOf(System.IO.Path.Combine(root, "Easy Red 2_Data", "StreamingAssets", "CorvoBundles", "er2bundle"));
			if (a == "0" || b == "0") return "unknown"; // 关键文件取不到 → 不可信
			return a + "|" + b + "|" + c;
		}
		catch { return "unknown"; }
	}

	/// <summary>各桶条目数的紧凑串（日志/排查用——"凭空多出的分类"这类问题靠它定位）。</summary>
	private static string BucketsBrief()
	{
		var sb = new StringBuilder();
		foreach (string k in BucketOrder)
			if (Buckets.TryGetValue(k, out List<ItemEntry> l) && l.Count > 0)
				sb.Append(" ").Append(k).Append("=").Append(l.Count);
		return sb.ToString();
	}

	/// <summary>从缓存头（制表符分隔）取字段值。</summary>
	private static string ExtractHeaderField(string header, string key)
	{
		try
		{
			foreach (string part in header.Split('\t'))
				if (part.StartsWith(key + "=")) return part.Substring(key.Length + 1).Trim();
		}
		catch { }
		return "";
	}

	private static string EscapeField(string s)
	{
		return (s ?? "").Replace("\\", "\\\\").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "");
	}

	private static string UnescapeField(string s)
	{
		if (string.IsNullOrEmpty(s)) return "";
		return s.Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\\\", "\\");
	}

	/// <summary>尝试从磁盘缓存恢复目录（成功 = Ready 且跳过全部枚举调用）。</summary>
	private static bool TryLoadCacheFromDisk()
	{
		try
		{
			if (Plugin.refreshItemCache != null && Plugin.refreshItemCache.Value)
			{
				Plugin.ModLog.LogInfo("[UniGen] refreshItemCache=true，忽略磁盘物品目录缓存，本次将重新枚举。");
				return false;
			}
			string path = CacheFilePath();
			if (!System.IO.File.Exists(path)) return false;
			string[] lines = System.IO.File.ReadAllLines(path, System.Text.Encoding.UTF8);
			// v2.5.37：header 必须带 full=1（只在战斗场景写盘时标记）。
			// 没有标记 = 老版本/主菜单阶段写的不完整缓存 → 不采信，cacheWritten 保持 false，
			// 进战斗后由看门狗重扫重建（否则旧残表会被"已就绪"标记永久固化）。
			if (lines.Length < 2 || !lines[0].StartsWith("#unigen-items-cache") || !lines[0].Contains("full=1"))
			{
				Plugin.ModLog.LogInfo("[UniGen] 磁盘物品目录缓存缺少完整标记（旧版或主菜单期写入）——忽略之，进战斗后重建。");
				return false;
			}

			// v2.5.44：解析"已验证 + 游戏构建签名"。
			// verified=1 且签名一致 → 本会话**跳过验证枚举**（省下 54.6 秒原子冻结）。
			// 任一不满足 → 照常验证一次（残表风险只允许存在一个会话）。
			bool verifiedOnDisk = lines[0].Contains("verified=1");
			string cachedSig = ExtractHeaderField(lines[0], "sig");
			string nowSig = CacheSig();
			bool sigSame = !string.IsNullOrEmpty(cachedSig) && cachedSig != "unknown" && cachedSig == nowSig;
			cacheVerified = verifiedOnDisk && sigSame;

			for (int i = 1; i < lines.Length; i++)
			{
				string line = lines[i];
				if (string.IsNullOrEmpty(line)) continue;
				string[] parts = line.Split('\t');
				if (parts.Length < 4) continue;
				Add(UnescapeField(parts[2]), UnescapeField(parts[3]), parts[0], UnescapeField(parts[1]));
			}
			if (TotalCount() == 0) return false;
			SortAll();
			Ready = true;
			probeState = 2;
			cacheWritten = true; // v2.5.37：磁盘缓存已就绪 → 场景完整性检查不再触发重扫
			lateDone = true;     // v2.5.40：缓存里已含 items 类 → items 补漏轮跳过（由验证轮全量覆盖）
			readyAt = UnityEngine.Time.unscaledTime;
			nextVerifyAt = readyAt + 10f; // v2.5.45：首轮验证在就绪 10s 后（多轮累积的起点）
			Plugin.ModLog.LogInfo("[UniGen] 物品目录自磁盘缓存加载: " + TotalCount() + " 条" + BucketsBrief() + "（"
				+ System.IO.Path.GetFileName(path) + "）——本次启动零枚举调用，无原子冻结。");
			Plugin.ModLog.LogInfo("[UniGen] 缓存校验状态: verified=" + (verifiedOnDisk ? 1 : 0)
				+ " 签名一致=" + sigSame + "（磁盘 " + cachedSig + " / 当前 " + nowSig + "）→ "
				+ (cacheVerified ? "本会话跳过验证枚举（游戏未更新，目录可信）"
					: "本会话将做一次验证枚举（缓存未经比对，或游戏已更新）"));
			return true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] 物品目录磁盘缓存读取失败（回退到实时枚举）: " + ex.Message);
			return false;
		}
	}

	/// <summary>把当前目录写盘（"mod" 桶除外——mod 物品每次启动由 ModCatalog 重新扫描，写进去会陈旧）。</summary>
	private static void WriteCacheToDisk(bool verified = false)
	{
		try
		{
			var sb = new StringBuilder();
			// v2.5.44：头部记录"是否经过完整比对验证" + 游戏构建签名。
			// verified=1 且 sig 未变 → 下次启动跳过验证枚举（省下 54.6 秒原子冻结）。
			// **任何未经比对的写入（运行时枚举 567 / 补漏 602 / 旧重扫 972）一律 verified=0**，
			// 下次启动照常验证一次并升级为 verified=1（残表风险只允许存在一个会话）。
			sb.Append("#unigen-items-cache v1\tfull=1\tverified=").Append(verified ? "1" : "0")
				.Append("\tsig=").Append(CacheSig()).Append("\tcount=").Append(TotalCount()).AppendLine();
			foreach (string k in BucketOrder)
			{
				if (k == "mod") continue;
				if (!Buckets.TryGetValue(k, out List<ItemEntry> l)) continue;
				foreach (ItemEntry e in l)
				{
					sb.Append(k).Append('\t').Append(EscapeField(e.Sub)).Append('\t')
					  .Append(EscapeField(e.Id)).Append('\t').Append(EscapeField(e.Title)).AppendLine();
				}
			}
			string path = CacheFilePath();
			System.IO.File.WriteAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
			Plugin.ModLog.LogInfo("[UniGen] 物品目录已写磁盘缓存: " + TotalCount() + " 条（verified="
				+ (verified ? "1" : "0") + "，签名=" + CacheSig() + "）" + BucketsBrief() + " → " + path);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] 物品目录磁盘缓存写入失败（不影响功能，下次启动会重新枚举）: " + ex.Message);
		}
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
			// 2.5.33（§7 发布前清理）：限频诊断统一收进 debugLog 门控——
			//   "物品库为何不就绪"仍可诊断，但发布版默认不再每 5s 打一条（顺带省掉字符串拼接开销）。
			if (Plugin.debugLog.Value) LogProbeDiag("四类全空:" + detail);
			return;
		}
		if (!loggedReadyDetail)
		{
			loggedReadyDetail = true;
			Plugin.ModLog.LogInfo("[UniGen] 物品库可枚举(IO=ItemObject, PD=PropData):" + detail);
		}
	}

	/// <summary>
	/// 2.0.7：探针类别（items=6 杂项 / weapons=7 / ammo=8 / attachment=9）。
	/// </summary>
	private static readonly int[] ProbeTypes = { 6, 7, 8, 9 };

	/// <summary>
	/// v2.5.36：`GetAllItemsOfType` 的**会话内结果缓存**（key = 类型名|propType）。
	/// 该 API 每次调用都全量过滤数据库，**单次实测最高 5.5 秒且为原子调用（无法分帧）**——
	/// 2.5.35 的耗时探针实锤（PropData(items)=4533ms、ItemObject(weapons)=5463ms）。
	/// 因此同一 (T, type) 一次会话只允许调一次真实 API，其余全部走缓存；
	/// 磁盘缓存命中时连这一次都不发生。EnrichCR 重扫前需显式清空本字典。
	/// </summary>
	private static readonly Dictionary<string, object> typeCache = new Dictionary<string, object>();

	private static Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> GetAllCached<T>(int propType) where T : Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase
	{
		string key = typeof(T).Name + "|" + propType;
		if (typeCache.TryGetValue(key, out object v) && v is Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<T> hit)
		{
			return hit;
		}
		var sw = System.Diagnostics.Stopwatch.StartNew();
		var result = ItemsDatabase.GetAllItemsOfType<T>((PropData.PropType)propType);
		sw.Stop();
		typeCache[key] = result;
		if (sw.ElapsedMilliseconds >= 150)
		{
			// 性能证据：不受 debugLog 门控（这正是"进游戏固定时间卡几秒"的元凶现场）
			Plugin.ModLog.LogWarning("[UniGen] GetAllItemsOfType<" + typeof(T).Name + ">(" + (PropData.PropType)propType
				+ ") 单次耗时 " + sw.ElapsedMilliseconds + "ms（原子调用，结果已缓存——本会话同参数不再重调）");
		}
		return result;
	}

	/// <summary>v2.5.36：清空 API 结果缓存（补漏重扫前调用，强制真实重枚举）。</summary>
	private static void InvalidateTypeCache()
	{
		typeCache.Clear();
	}

	/// <summary>2.0.7：上一次各类别的条目数（供日志排查）。</summary>
	internal static readonly Dictionary<int, int> LastCounts = new Dictionary<int, int>();

	private static bool loggedReadyDetail;

	/// <summary>2.0.7：Ready 之后一次性"补漏"重扫的时间点。
	/// **v2.5.36 起废弃**：自动补漏 = 进游戏后固定再来一轮数秒冻结（单次 GetAllItemsOfType
	/// 实测最高 5.5 秒且为原子调用），其"数据库分批加载补条目"的使命由磁盘缓存取代。
	/// EnrichCR 保留（InvalidateTypeCache 后可手动重扫），但不再有任何自动触发。</summary>

	/// <summary>
	/// 2.0.7：补漏重扫——首轮拿到条目后再扫一遍（**v2.5.36 起无自动触发**，见上）。
	/// `Add` 按 Id 去重，所以只会"补进新条目"，不会重复；条目数有增长才重建页签。
	/// </summary>
	private static IEnumerator EnrichCR()
	{
		System.Diagnostics.Stopwatch sw = System.Diagnostics.Stopwatch.StartNew();
		long budgetUntil = 0;
		int[] types2 = { 6, 7, 8, 9 };

		// v2.5.36：重扫前清 API 结果缓存（否则拿到的是首轮的旧数组，补漏永远补不进新条目）
		InvalidateTypeCache();

		int before = TotalCount();
		yield return EnumerateAll(types2, sw, budgetUntil);
		SortAll();
		int after = TotalCount();

		// v2.5.36：重扫完成的总耗时（含数次原子大调用，无法分帧）；并把补漏后的目录写一次
		// 磁盘缓存，让"下次启动"直接用完整目录（零调用）。
		Plugin.ModLog.LogInfo("[UniGen] 补漏重扫完成: " + before + " → " + after + " 条，耗时 "
			+ sw.ElapsedMilliseconds + "ms（含数次原子调用，无法分帧）");
		WriteCacheToDisk();

		if (after > before)
		{
			Plugin.ModLog.LogInfo("[UniGen] 物品目录补漏: " + before + " → " + after + " 条，重建物品页签。");
			try { GenPanel.RebuildItemTabsPublic(); } catch { }
		}
	}

	/// <summary>
	/// 2.0.7：安全取某类别的条目数。
	/// v2.5.36：**只读缓存，未命中不触发真实调用**——单次 GetAllItemsOfType 最高 5.5 秒且为
	/// 原子调用，绝不能在"只观测"的路径里随手触发。未缓存返回 -3。
	/// </summary>
	private static int CountItemObjects(int type)
	{
		try
		{
			if (!typeCache.TryGetValue("ItemObject|" + type, out object v)) return -3;
			return (v is Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<ItemObject> l) ? l.Count : -2;
		}
		catch { return -2; }
	}

	private static int CountPropDatas(int type)
	{
		try
		{
			if (!typeCache.TryGetValue("PropData|" + type, out object v)) return -3;
			return (v is Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<PropData> l) ? l.Count : -2;
		}
		catch { return -2; }
	}

	/// <summary>
	/// 2.0.5 诊断：把"闸门为何不放行"的原始值打到日志（每 5 秒最多一条，避免刷屏）。
	/// 2.0.3/2.0.4 两轮都卡在"闸门永远 false 但不知为何"——必须让日志自己说话。
	/// 2.5.33（§7 发布前清理）：改为 **debugLog 门控**——限频诊断不得在发布版常开输出；
	///   调用点也做了同样判定，目的是连字符串拼接开销一起省掉。
	/// </summary>
	private static void LogProbeDiag(string why)
	{
		if (!Plugin.debugLog.Value) return;
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
