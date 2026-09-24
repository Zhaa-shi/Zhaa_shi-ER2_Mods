using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 生成执行器：VehicleSpawner 封装 / 乘员填充 / 步兵小队（SpawnAISquadGlobal）。
/// 所有异步路径走本 MonoBehaviour 的协程（IL2CPP 需 WrapToIl2Cpp）。
/// </summary>
internal static class GenRunner
{
	private static GenRunnerHost host;

	private static string myFactionCached = "";
	private static float factionCacheTime;

	public static void Ensure() => Ensure(false);

	private static void Ensure(bool force)
	{
		try
		{
			if (!force && host != null) return;
			if (host != null)
			{
				// 旧宿主已失效：清掉残留 GO
				try { UnityEngine.Object.Destroy(host.gameObject); } catch { }
				host = null;
			}
			ClassInjector.RegisterTypeInIl2Cpp<GenRunnerHost>();
			GameObject go = new GameObject("ER2UniversalGenerationRunner");
			UnityEngine.Object.DontDestroyOnLoad(go);
			host = go.AddComponent<GenRunnerHost>();
			Plugin.ModLog.LogInfo("[UniGen] 协程宿主就绪" + (force ? "（重建）" : ""));
		}
		catch (Exception ex) { Plugin.ModLog.LogError("GenRunner 启动失败: " + ex); }
	}

	/// <summary>
	/// 协程启动。宿主可能在场景切换时被销毁（曾导致 native NullReferenceException），
	/// 这里惰性获取：null/调用失败时强制重建再试一次。
	/// </summary>
	public static Coroutine StartCoroutine(IEnumerator routine)
	{
		if (host == null) Ensure();
		try
		{
			return host.StartCoroutineW(routine);
		}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning("[UniGen] 协程宿主失效，重建: " + ex.Message);
				Ensure(true);
				return host.StartCoroutineW(routine);
			}
		}

		/// <summary>
		/// 启动游戏原生协程（SpawnAI/SpawnAISquadGlobal 等 interop 方法返回的
		/// Il2CppSystem.Collections.IEnumerator——只是协程对象，必须显式启动才会运行！
		/// 直接交给 Unity 协程调度器，无需 WrapToIl2Cpp）。
		/// </summary>
		public static Coroutine StartCoroutineNative(Il2CppSystem.Collections.IEnumerator routine)
		{
			if (routine == null) return null;
			if (host == null) Ensure();
			try
			{
				return host.StartCoroutine(routine);
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning("[UniGen] 协程宿主失效，重建: " + ex.Message);
				Ensure(true);
				return host.StartCoroutine(routine);
			}
		}

	/// <summary>当前战局"我方"阵营名（缓存 10s）。战局存的是 Faction 枚举，字符串用 *_id 字段。</summary>
	public static string MyFaction()
	{
		if (Time.unscaledTime - factionCacheTime < 10f && myFactionCached != "") return myFactionCached;
		try
		{
			string fac = null;
			try
			{
				Soldier pc = PlayerController.currentController?.ControlledCharacter;
				if (pc != null && !string.IsNullOrEmpty(pc.faction)) fac = pc.faction;
			}
			catch { }
			if (string.IsNullOrEmpty(fac))
			{
				// 上帝视角玩家已被接管/置空：宿主 SavedFaction（反射缓存）最可靠
				fac = HostLink.HostSavedFaction();
			}
			if (string.IsNullOrEmpty(fac))
			{
				try { fac = MatchData.data?.loadedBattle?.GetBattleData()?.defendersFaction_id; } catch { }
			}
			if (!string.IsNullOrEmpty(fac))
			{
				myFactionCached = fac;
				factionCacheTime = Time.unscaledTime;
			}
		}
		catch { }
		return myFactionCached;
	}

	/// <summary>"敌方"阵营名 = 战局另一方（用 *_id 字符串字段）。</summary>
	public static string EnemyFaction()
	{
		string my = MyFaction();
		try
		{
			BattleData bd = MatchData.data?.loadedBattle?.GetBattleData();
			if (bd != null)
			{
				string inv = bd.invadersFaction_id;
				string def = bd.defendersFaction_id;
				if (!string.IsNullOrEmpty(inv) && !string.IsNullOrEmpty(def))
					return string.Equals(inv, my, System.StringComparison.OrdinalIgnoreCase) ? def : inv;
			}
		}
		catch { }
		return "";
	}

	// ================= 委托桥 =================

	/// <summary>托管 Action&lt;Soldier&gt; → Il2CppSystem.Action&lt;Soldier&gt;（Il2CppInterop 官方模式，Loadout interop 同款）。</summary>
	private static Il2CppSystem.Action<Soldier> ToIl2Cpp(Action<Soldier> a)
		=> DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Soldier>>((System.Delegate)a);

	private static Il2CppSystem.Action<Squad> ToIl2Cpp(Action<Squad> a)
		=> DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Squad>>((System.Delegate)a);

	// ================= 受控参数（生成默认行为）=================

	/// <summary>
	/// 受控参数（宿主 DisableNativeOrders 同款）：单位不抢任务、原地待命、自动接战、听 RTS 指令。
	/// 用于"打人就行，别乱跑"的默认生成行为。
	/// </summary>
	internal static void ApplyControlled(Soldier s)
	{
		if (s == null) return;
		try
		{
			AiParams ap = new Lua_Soldier(s).getAiParams();
			ap.followCustomSquadOrders();
			ap.followCustomDirectCommands();
			ap.allowMovements(true);
			// 1.0.9：生成的中立（平民）单位不主动开战（用户反馈"中立单位为什么会主动攻击"）
			{ string sf = null; try { sf = s.faction; } catch { } if (!string.IsNullOrEmpty(sf) && sf.IndexOf("civil", System.StringComparison.OrdinalIgnoreCase) >= 0) ap.allowCheckForEnemies(false); }
		}
		catch { }
	}

	/// <summary>整队受控参数。</summary>
	internal static void ApplyControlledToSquad(Squad sq)
	{
		if (sq == null) return;
		int n = 0;
		try
		{
			int count = sq.CountMembers;
			for (int i = 0; i < count; i++)
			{
				Soldier m = sq.GetMemberClamped(i);
				if (m == null) continue;
				ApplyControlled(m);
				n++;
			}
		}
		catch { }
		if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 受控参数已应用 ×" + n);
	}

	// ================= 载具 =================

	/// <summary>
	/// 生成载具（官方 VehicleSpawner 管线）。crewType 非 null 时用原生 SpawnAISquadGlobal(spawnOnvehicle)
	/// 生成乘员小队，人数按实际座位数覆盖（不超载不缺员）。
	/// </summary>
	public static void SpawnVehicle(GenEntry entry, Vector3 pos, string faction, SquadType? crewType, Action<Vehicle> onDone)
	{
		StartCoroutine(SpawnVehicleCR(entry, pos, faction, crewType, onDone));
	}

	private static IEnumerator SpawnVehicleCR(GenEntry entry, Vector3 pos, string faction, SquadType? crewType, Action<Vehicle> onDone)
	{
		GameObject spawnerGo = null;
		Vehicle veh = null;
		try
		{
			if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] SpawnVehicle begin id=" + entry.Id + " faction=" + faction + " pos=" + pos + " crew=" + (crewType.HasValue ? crewType.Value.ToString() : "no"));
			spawnerGo = new GameObject("UniGen_VehicleSpawner");
			spawnerGo.transform.position = pos;
			VehicleSpawner sp = spawnerGo.AddComponent<VehicleSpawner>();
			sp.vehiclePrefabID = entry.Id;
			sp.camoId = 0;
			sp.SpawnVehicle();
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("[UniGen] 载具生成调用失败(" + entry.Id + "): " + ex);
			try { if (spawnerGo != null) UnityEngine.Object.Destroy(spawnerGo); } catch { }
			onDone?.Invoke(null);
			yield break;
		}

		// SpawnVehicle 内部异步加载 prefab：轮询等 spawnedVehicle（最多 ~6s）
		VehicleSpawner spRef = spawnerGo != null ? spawnerGo.GetComponent<VehicleSpawner>() : null;
		float deadline = Time.unscaledTime + 6f;
		while (Time.unscaledTime < deadline)
		{
			try { veh = spRef != null ? spRef.GetSpawnedVehicle() : null; } catch { veh = null; }
			if (veh != null) break;
			yield return null;
		}

		if (veh == null)
		{
			Plugin.ModLog.LogError("[UniGen] 载具生成超时/失败: " + entry.Id + "（vehiclePrefabID 不匹配？查日志 GetVehiclePrefab 错误）");
			try { if (spawnerGo != null) UnityEngine.Object.Destroy(spawnerGo); } catch { }
			onDone?.Invoke(null);
			yield break;
		}

		// 1.0.4：生成后收尾（阵营/解锁/乘员/登记）抽为公共方法——
		// 幽灵预览把"预览实例"直接当真实生成物提交时，复用它（不再重新生成）。
		FinalizeVehicle(veh, entry, faction, crewType, pos, onDone);
	}

	/// <summary>载具生成后收尾：SetFaction → 解锁 → 登记 → 乘员（spawnOnvehicle 直接生在车上）。</summary>
	internal static void FinalizeVehicle(Vehicle veh, GenEntry entry, string faction, SquadType? crewType, Vector3 pos, Action<Vehicle> onDone)
	{
		try
		{
			veh.SetFaction(faction);
			ApplyYaw(veh.gameObject); // 1.0.5：应用预览朝向
			Plugin.ModLog.LogInfo("[UniGen] 载具已生成: " + entry.Id + " @ " + pos + " faction=" + faction + " yaw=" + PreviewYaw.ToString("0"));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("[UniGen] SetFaction 失败(" + entry.Id + "): " + ex.Message);
		}

		RegisterSpawnedVehicle(veh);

		// 解锁：locked 载具会导致上车静默失败（宿主 BoardVehicle 同款前置）
		try { if (veh.IsLocked()) veh.SetLocked(false); } catch { }

		int seats = 0;
		try { seats = veh.seats.Length; } catch { }
		if (crewType.HasValue && seats > 0)
		{
			// 1.0.3（用户要求）：乘员**直接生成在车上**——SpawnAISquadGlobal 的 spawnOnvehicle 参数
			// 就是原生"出生即入座"通道（战役增援/载具车组同款），不再走"车旁落地 + 逐员登车"。
			Vehicle vehRef = veh;
			string facRef = faction;
			int standard = 0;
			try { SquadData sd0 = ItemsDatabase.GetSquadLoadouts(crewType.Value, 0); if (sd0 != null) standard = sd0.CountLoadouts(); } catch { }
			int want = seats;
			if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 乘员配置: 座位=" + seats + " 标准班=" + standard + " → 覆盖为 " + want);
			SquadData sd = null;
			try { sd = ItemsDatabase.GetSquadLoadouts(crewType.Value, want); } catch { }
			if (sd != null && sd.CountLoadouts() > 0)
			{
				StartCoroutineNative(SpawnManager.SpawnAISquadGlobal(facRef, null, sd, pos, 4f, vehRef,
					ToIl2Cpp((Action<Squad>)(sq =>
					{
						if (sq == null) return;
						RegisterSpawnedSquad(sq);
						StartCoroutine(FinishCrewCR(sq, vehRef, seats));
					})), -1));
			}
			else
			{
				Plugin.ModLog.LogWarning("[UniGen] 乘员 SquadData 为空（crewType=" + crewType.Value + "），本次不填乘员。");
			}
		}

		onDone?.Invoke(veh);
	}

	/// <summary>
	/// 1.0.3：乘员收尾（已由 spawnOnvehicle 直接生在车上，无需登车流程）：
	/// 等生成完成 → 超员裁剪 → 确认在车 → 授予驾驶资格（AIVehicle.squadInside）+ 登记宿主 rtsSquadSet。
	/// </summary>
	private static IEnumerator FinishCrewCR(Squad crewSq, Vehicle veh, int seats)
	{
		if (crewSq == null || veh == null) yield break;

		float waitSpawn = Time.unscaledTime + 12f;
		while (Time.unscaledTime < waitSpawn)
		{
			bool ready = false;
			try { ready = crewSq.fullySpawned; } catch { ready = true; }
			if (ready) break;
			yield return new WaitForSeconds(0.5f);
		}
		yield return new WaitForSeconds(0.5f); // 落地/入座收尾

		// 超员裁剪（座位数上限；spawnOnvehicle 理论上按座位生成，这里兜底）
		try
		{
			int cm = crewSq.CountMembers;
			if (cm > seats)
			{
				for (int i = cm - 1; i >= seats; i--)
				{
					try
					{
						Soldier m = crewSq.GetMemberClamped(i);
						if (m != null) UnityEngine.Object.Destroy(m.gameObject);
					}
					catch { }
				}
				Plugin.ModLog.LogInfo("[UniGen] 超载裁剪: " + cm + " → " + seats);
			}
		}
		catch { }

		// 观察在车人数（仅日志：spawnOnvehicle 由原生入座，失败不回退登车流程）
		int inside = 0;
		try { inside = veh.peopleInside; } catch { }
		Plugin.ModLog.LogInfo("[UniGen] 乘员已在车（spawnOnvehicle）: " + inside + "/" + seats);

		// 驾驶资格：AIVehicle.squadInside 指向乘员班（没有它车辆收不到指挥）
		try
		{
			AIVehicle ai = veh.GetComponent<AIVehicle>();
			if (ai == null) ai = veh.GetComponentInChildren<AIVehicle>();
			if (ai != null)
			{
				ai.squadInside = crewSq;
				if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 驾驶资格已授予: squadInside=乘员班");
			}
		}
		catch (Exception ex) { Plugin.ModLog.LogWarning("[UniGen] squadInside 设置失败: " + ex.Message); }
		// 乘员班必须登记进宿主 rtsSquadSet（RTS 分队集合），否则宿主 DriveVehicleTo 拒绝指挥
		bool reg = HostLink.RtsRegisterSquad(crewSq);
		if (Plugin.debugLog.Value || !reg) Plugin.ModLog.LogInfo("[UniGen] RTS 分队登记: " + (reg ? "成功" : "失败"));
	}
	// ================= 步兵小队 =================

	/// <summary>生成步兵小队（SpawnAISquadGlobal）。side = 面板所选阵营意图（mine/enemy/neutral），
	/// 1.1.1 起决定 AI 模式：敌方默认原生 AI，我方/中立受控驻守。</summary>
	public static void SpawnInfantrySquad(GenEntry entry, Vector3 pos, string faction, string side, Action<Squad> onDone)
	{
		StartCoroutine(SpawnInfantryCR(entry, pos, faction, side, onDone));
	}

	private static IEnumerator SpawnInfantryCR(GenEntry entry, Vector3 pos, string faction, string side, Action<Squad> onDone)
	{
		SquadData sd = null;
		try
		{
			if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] SpawnSquad begin id=" + entry.Id + " faction=" + faction + " pos=" + pos);
			// 官方班型走 SquadType 枚举重载；小队库扩展班（季节/战场变体）走 string 重载
			sd = entry.SpawnByKey ? ItemsDatabase.GetSquadLoadouts(entry.Id, 0)
				: ItemsDatabase.GetSquadLoadouts(entry.SType, 0);
			if (sd == null || sd.CountLoadouts() <= 0)
			{
				Plugin.ModLog.LogError("[UniGen] GetSquadLoadouts(" + entry.Id + ") 返回空小队——类型无效，条目已隐藏。");
				GenCatalog.RemoveInfantryEntry(entry);
				onDone?.Invoke(null);
				yield break;
			}
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("[UniGen] GetSquadLoadouts(" + entry.SType + ") 抛异常: " + ex);
			onDone?.Invoke(null);
			yield break;
		}

		Squad result = null;
		bool done = false;
		try
		{
			// 注意：SpawnAISquadGlobal 返回的是原生协程对象，必须显式启动才会运行（否则回调永不来）
			StartCoroutineNative(SpawnManager.SpawnAISquadGlobal(faction, null, sd, pos, 10f, null,
				ToIl2Cpp((Action<Squad>)(sq => { result = sq; done = true; })), -1));
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("[UniGen] SpawnAISquadGlobal 调用失败: " + ex);
			onDone?.Invoke(null);
			yield break;
		}

		float deadline = Time.unscaledTime + 10f;
		while (!done && Time.unscaledTime < deadline) yield return null;

		if (result == null)
		{
			Plugin.ModLog.LogError("[UniGen] 小队生成超时/失败: " + entry.Id + " faction=" + faction + "（script_file=null 行为待验证——查游戏日志 AI 初始化错误）");
			onDone?.Invoke(null);
			yield break;
		}
		RegisterSpawnedSquad(result);
		// 1.0.5：应用预览朝向（步兵班：逐个转朝向，保留散开站位）
		try
		{
			int n = result.CountMembers;
			for (int i = 0; i < n; i++)
			{
				Soldier m = result.GetMemberClamped(i);
				if (m != null) ApplyYaw(m.gameObject);
			}
		}
		catch { }
		// 1.1.1：受控参数（原地驻守+听令）只给"有人指挥"的阵营——敌方没人下令会永远
		// 站在出生点（用户实测反馈）。敌方默认原生 AI（随战役任务推进/进攻），cfg 可关回旧行为。
		bool controlled = side != "enemy" || !Plugin.enemyNativeAI.Value;
		if (controlled) ApplyControlledToSquad(result);
		if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] AI 模式: " + (controlled ? "受控(驻守)" : "原生(自由接战/推进)") + " side=" + side);
		Plugin.ModLog.LogInfo("[UniGen] 步兵小队已生成: " + entry.Id + " @ " + pos + " faction=" + faction);
		onDone?.Invoke(result);
	}

	// ================= 放置预览（1.0.4：复用宿主幽灵视觉）=================

	private static readonly List<GameObject> previewGhosts = new List<GameObject>();

	/// <summary>
	/// 1.0.4：生成一个"幽灵预览实例"（真实生成一次 → 立即幽灵化：停用 AI/碰撞、半透明白材质）。
	/// 放置模式中跟随光标移动，确认时由调用方销毁并走正式生成；取消/退出时 DestroyPreview。
	/// 宿主幽灵材质不可用时返回 false（调用方回退自绘圈）。
	/// </summary>
	public static void SpawnPreviewGhost(GenEntry entry, Vector3 pos, string faction, Action<bool> onReady)
	{
		// 1.0.9：记录代数——异步生成完成时若已被取消/已放置，立即自毁
		// （否则成为孤儿预览，表现为"放置预览无法消失"）。
		int gen = previewGeneration;
		StartCoroutine(SpawnPreviewGhostCR(entry, pos, faction, onReady, gen));
	}

	/// <summary>1.0.9：预览代数。DestroyPreview 时自增 → 在途的异步预览作废。</summary>
	private static int previewGeneration;

	private static bool StalePreview(int gen) { return gen != previewGeneration; }

	private static IEnumerator SpawnPreviewGhostCR(GenEntry entry, Vector3 pos, string faction, Action<bool> onReady, int gen)
	{
		if (!HostLink.GhostAvailable) { onReady?.Invoke(false); yield break; }
		// 1.0.13：**这里不能再调 DestroyPreview()**——它会把 previewGeneration 自增，
		// 而本次协程的 gen 是调用时捕获的（更小）→ 本次幽灵刚生成就被判"过期"销毁
		// = 用户反馈"幽灵预览无法渲染"。旧预览的清理由 Begin() 负责（在捕获 gen 之前）。

		if (!entry.IsInfantry)
		{
			// 载具：VehicleSpawner 生成一次 → 幽灵化
			GameObject spawnerGo = null;
			Vehicle veh = null;
			try
			{
				spawnerGo = new GameObject("UniGen_PreviewSpawner");
				spawnerGo.transform.position = pos;
				VehicleSpawner sp = spawnerGo.AddComponent<VehicleSpawner>();
				sp.vehiclePrefabID = entry.Id;
				sp.camoId = 0;
				sp.SpawnVehicle();
			}
			catch { onReady?.Invoke(false); yield break; }

			VehicleSpawner spRef = spawnerGo != null ? spawnerGo.GetComponent<VehicleSpawner>() : null;
			float deadline = Time.unscaledTime + 6f;
			while (Time.unscaledTime < deadline)
			{
				try { veh = spRef != null ? spRef.GetSpawnedVehicle() : null; } catch { veh = null; }
				if (veh != null) break;
				yield return null;
			}
			try { if (spawnerGo != null) UnityEngine.Object.Destroy(spawnerGo); } catch { }
			if (veh == null) { onReady?.Invoke(false); yield break; }

			try
			{
				veh.SetFaction(faction);
				veh.transform.position = pos;
			}
			catch { }
			bool ok = HostLink.Ghostify(veh.gameObject);
			if (!ok) { try { UnityEngine.Object.Destroy(veh.gameObject); } catch { } onReady?.Invoke(false); yield break; }
			if (StalePreview(gen)) { try { UnityEngine.Object.Destroy(veh.gameObject); } catch { } onReady?.Invoke(false); yield break; }
			try { veh.gameObject.name = "UniGenPreview_" + veh.gameObject.name; } catch { }
			HostLink.RegisterGhost(veh.gameObject); // 1.0.8：登记（伤害免疫 + 地面射线豁免）
			TrackGhost(veh.gameObject, pos);
			onReady?.Invoke(true);
			yield break;
		}

		// 步兵：生成一次小队 → 全队幽灵化
		SquadData sd = null;
		try
		{
			sd = entry.SpawnByKey ? ItemsDatabase.GetSquadLoadouts(entry.Id, 0)
				: ItemsDatabase.GetSquadLoadouts(entry.SType, 0);
		}
		catch { }
		if (sd == null || sd.CountLoadouts() <= 0) { onReady?.Invoke(false); yield break; }

		Squad result = null;
		bool done = false;
		try
		{
			StartCoroutineNative(SpawnManager.SpawnAISquadGlobal(faction, null, sd, pos, 6f, null,
				ToIl2Cpp((Action<Squad>)(sq => { result = sq; done = true; })), -1));
		}
		catch { onReady?.Invoke(false); yield break; }

		float dl = Time.unscaledTime + 10f;
		while (!done && Time.unscaledTime < dl) yield return null;
		if (result == null) { onReady?.Invoke(false); yield break; }
		// 1.0.9：生成期间被取消/已放置 → 把刚生成的这一队直接销毁，不留孤儿
		if (StalePreview(gen))
		{
			try
			{
				int n0 = result.CountMembers;
				for (int i = 0; i < n0; i++)
				{
					Soldier m0 = result.GetMemberClamped(i);
					if (m0 != null) UnityEngine.Object.Destroy(m0.gameObject);
				}
			}
			catch { }
			onReady?.Invoke(false);
			yield break;
		}

		// 1.0.14：**边生成边幽灵化**。SpawnAISquadGlobal 的回调触发时，大班型的成员可能还没落齐
		// —— 未幽灵化的成员就是真人士兵：会与场上单位碰撞、会自行走动（用户反馈
		// "超过两个人的小队会直接生成/幽灵会与已有单位碰撞"）。每 0.25s 补一轮，
		// 直到 fullySpawned 且成员数稳定，才交给 onReady。
		HashSet<long> donePtrs = new HashSet<long>();
		int ghosted = 0;
		float waitFull = Time.unscaledTime + 15f;
		int lastCount = -1;
		while (Time.unscaledTime < waitFull)
		{
			if (StalePreview(gen))
			{
				try
				{
					int n0 = result.CountMembers;
					for (int i = 0; i < n0; i++)
					{
						Soldier m0 = result.GetMemberClamped(i);
						if (m0 != null) UnityEngine.Object.Destroy(m0.gameObject);
					}
				}
				catch { }
				DestroyPreview();
				onReady?.Invoke(false);
				yield break;
			}
			int cnt = 0; bool full = false;
			try { cnt = result.CountMembers; full = result.fullySpawned; } catch { }
			try
			{
				for (int i = 0; i < cnt; i++)
				{
					Soldier m = result.GetMemberClamped(i);
					if (m == null) continue;
					long k = (long)m.Pointer;
					if (donePtrs.Contains(k)) continue;
					if (HostLink.Ghostify(m.gameObject))
					{
						try { new Lua_Soldier(m).getAiParams().allowBeingTargeted(false); } catch { }
						try { m.gameObject.name = "UniGenPreview_" + m.gameObject.name; } catch { }
						HostLink.RegisterGhost(m.gameObject); // 伤害免疫 + 地面射线豁免
						TrackGhost(m.gameObject, pos);
						donePtrs.Add(k);
						ghosted++;
					}
				}
			}
			catch { }
			// fullySpawned 且成员数两轮一致且全部幽灵化 → 完成
			if (full && cnt > 0 && cnt == lastCount && ghosted >= cnt) break;
			lastCount = cnt;
			yield return new WaitForSeconds(0.25f);
		}
		if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 步兵预览幽灵化 " + ghosted + " 人");
		// 1.2.2：一个成员都没幽灵化 = 预览彻底失败——把刚生成的真实小队销毁，不留"看不见的真实士兵"
		if (ghosted <= 0)
		{
			try
			{
				int n1 = result.CountMembers;
				for (int i = 0; i < n1; i++)
				{
					Soldier m1 = result.GetMemberClamped(i);
					if (m1 != null) UnityEngine.Object.Destroy(m1.gameObject);
				}
			}
			catch { }
			onReady?.Invoke(false);
			yield break;
		}
		onReady?.Invoke(ghosted > 0);
	}

	// ================= 物品幽灵预览（2.2.0，用户要求"只要模型，不要光圈+图标"）=================

	/// <summary>
	/// 2.2.0：物品携带时的 3D 幽灵预览 —— **裸实例化一次 prefab（纯视觉，不走 ToVirtualItem 生成链）**
	/// → 宿主 Ghostify 幽灵化（半透明 + 免伤 + 射线豁免，与单位/载具预览同一套视觉）。
	/// 预览代数 guard 与单位预览共用（DestroyPreview 时自增 → 在途异步自毁）。
	/// </summary>
	public static void SpawnItemGhost(string itemId, Vector3 pos, Action<bool> onReady)
	{
		int gen = previewGeneration;
		StartCoroutine(SpawnItemGhostCR(itemId, pos, onReady, gen));
	}

	private static IEnumerator SpawnItemGhostCR(string itemId, Vector3 pos, Action<bool> onReady, int gen)
	{
		ItemObject prefab = null;
		try { prefab = ItemsDatabase.GetItemObject(itemId); } catch { }
		if (prefab == null)
		{
			// 2.3.0：失败必须**无条件**可观测——此前静默 onReady(false)，"预览不显示"无从定位
			Plugin.ModLog.LogWarning("[UniGen] 物品幽灵预览失败：GetItemObject(" + itemId + ") 返回 null");
			onReady?.Invoke(false); yield break;
		}

		GameObject inst = null;
		try { inst = UnityEngine.Object.Instantiate(prefab.gameObject); } catch (Exception ex) { Plugin.ModLog.LogWarning("[UniGen] 物品幽灵实例化异常: " + ex.Message); }
		if (inst == null) { onReady?.Invoke(false); yield break; }
		try { inst.SetActive(true); } catch { }

		// 兜底定身：Ghostify 理论上会停物理，但 prefab 各异——显式冻结刚体防"预览掉进地里"
		try
		{
			Rigidbody rb = inst.GetComponent<Rigidbody>();
			if (rb == null) rb = inst.GetComponentInChildren<Rigidbody>();
			if (rb != null) { rb.isKinematic = true; rb.useGravity = false; }
		}
		catch { }

		bool ok = false;
		try { ok = HostLink.Ghostify(inst); } catch { }
		if (!ok)
		{
			try { UnityEngine.Object.Destroy(inst); } catch { }
			Plugin.ModLog.LogWarning("[UniGen] 物品幽灵预览失败：宿主 Ghostify 返回 false（" + itemId + "）");
			onReady?.Invoke(false);
			yield break;
		}
		if (StalePreview(gen)) // 已取消/已投放：正常路径，静默自毁
		{
			try { UnityEngine.Object.Destroy(inst); } catch { }
			onReady?.Invoke(false);
			yield break;
		}
		try { inst.name = "UniGenPreview_ItemGhost_" + inst.name; } catch { }
		// 2.3.0 根因修复：**先落位、再 TrackGhost**。2.2.0 首发顺序写反——TrackGhost 记下的是
		// "prefab 模板原始坐标 − 锚点"（克隆体 Instantiate 时在模板位置，通常是世界原点附近），
		// 下一帧 MovePreviewTo 按这个错误偏移每帧把幽灵挪走 → 永远不在镜头里
		// = 用户实测"物品的 3D 模型不显示"。单位/载具预览没踩中：它们生成即在锚点，偏移天然≈0。
		try { inst.transform.position = pos + Vector3.up * 0.25f; } catch { }
		HostLink.RegisterGhost(inst);   // 免伤 + 本 mod 地面射线豁免
		TrackGhost(inst, pos);
		onReady?.Invoke(true);
	}

	/// <summary>销毁全部幽灵预览实例（取消放置 / 确认生成 / 退出 RTS 时调用）。</summary>
	public static void DestroyPreview()
	{
		previewGeneration++; // 1.0.9：作废在途的异步预览
		if (previewGhosts.Count == 0) { previewOffsets.Clear(); return; } // 1.0.10：幂等快速返回
		foreach (GameObject g in previewGhosts)
		{
			try { if (g != null) HostLink.UnregisterGhost(g); } catch { }
			try { if (g != null) UnityEngine.Object.Destroy(g); } catch { }
		}
		previewGhosts.Clear();
		previewOffsets.Clear();
	}

	public static bool HasPreview => previewGhosts.Count > 0;

	/// <summary>1.0.5：预览/生成的朝向（度，绕世界 Y）。由 Placer 左键长按拖动设置。</summary>
	public static float PreviewYaw { get; set; }

	/// <summary>1.0.8：把幽灵预览**绕锚点**旋转到指定朝向。
	/// 旧实现只写 transform.rotation，而成员位置是"锚点 + 固定偏移"——班成员按弧形散布，
	/// 只转朝向不转偏移，观感就是"在乱转/散开"（用户反馈）。
	/// 现在偏移按 yaw 增量一起旋转，整队像刚体一样原地转向。</summary>
	public static void SetPreviewYaw(float yawDeg)
	{
		float delta = yawDeg - PreviewYaw;
		PreviewYaw = yawDeg;
		Quaternion q = Quaternion.Euler(0f, delta, 0f);
		for (int i = 0; i < previewGhosts.Count; i++)
		{
			GameObject g = previewGhosts[i];
			if (g == null) continue;
			try
			{
				if (i < previewOffsets.Count) previewOffsets[i] = q * previewOffsets[i];
				g.transform.rotation = Quaternion.Euler(0f, yawDeg, 0f);
			}
			catch { }
		}
	}

	/// <summary>把已生成对象旋转到预览朝向（生成时应用；步兵班只转朝向不改站位）。</summary>
	private static void ApplyYaw(GameObject go)
	{
		try
		{
			if (go == null) return;
			Vector3 e = go.transform.eulerAngles;
			go.transform.rotation = Quaternion.Euler(e.x, PreviewYaw, e.z);
		}
		catch { }
	}

	/// <summary>幽灵相对"放置锚点"的偏移（步兵班成员是散开的，整体平移时保留队形）。</summary>
	private static readonly List<Vector3> previewOffsets = new List<Vector3>();

	/// <summary>登记一个幽灵及其相对锚点偏移。</summary>
	private static void TrackGhost(GameObject g, Vector3 anchor)
	{
		previewGhosts.Add(g);
		try { previewOffsets.Add(g.transform.position - anchor); } catch { previewOffsets.Add(Vector3.zero); }
	}

	/// <summary>把幽灵预览整体移到新落点（每帧跟随光标；保留各成员相对队形）。</summary>
	public static void MovePreviewTo(Vector3 pos)
	{
		for (int i = 0; i < previewGhosts.Count; i++)
		{
			GameObject g = previewGhosts[i];
			if (g == null) continue;
			try { g.transform.position = pos + (i < previewOffsets.Count ? previewOffsets[i] : Vector3.zero); } catch { }
		}
	}

	// ================= 生成物注册表（一键清除）=================

	private static readonly List<Vehicle> spawnedVehicles = new();
	private static readonly List<Squad> spawnedSquads = new();

	private static void RegisterSpawnedVehicle(Vehicle v)
	{
		if (v == null) return;
		spawnedVehicles.Add(v);
		NeutralPacifist.RegisterShooter(v); // 1.0.2：中立不主动攻击——登记射手
	}

	private static void RegisterSpawnedSquad(Squad s)
	{
		if (s == null) return;
		spawnedSquads.Add(s);
		try
		{
			int count = s.CountMembers;
			for (int i = 0; i < count; i++)
			{
				Soldier m = s.GetMemberClamped(i);
				if (m != null) NeutralPacifist.RegisterShooter(m); // 1.0.2：中立不主动攻击——登记射手
			}
		}
		catch { }
	}

	/// <summary>一键清除所有本 mod 生成的单位（先兵后车）。返回清除数量。</summary>
	public static int ClearAllSpawned()
	{
		int n = 0;
		foreach (Squad sq in spawnedSquads)
		{
			if (sq == null) continue;
			try
			{
				int count = sq.CountMembers;
				for (int i = 0; i < count; i++)
				{
					try
					{
						Soldier m = sq.GetMemberClamped(i);
						if (m != null && m.IsAlive) { UnityEngine.Object.Destroy(m.gameObject); n++; }
					}
					catch { }
				}
			}
			catch { }
		}
		spawnedSquads.Clear();
		foreach (Vehicle v in spawnedVehicles)
		{
			if (v == null) continue;
			try { UnityEngine.Object.Destroy(v.gameObject); n++; } catch { }
		}
		spawnedVehicles.Clear();
		NeutralPacifist.ClearRegistry(); // 1.0.2：登记表同步清空
		Plugin.ModLog.LogInfo("[UniGen] 一键清除: " + n + " 个生成物");
		return n;
	}
}

/// <summary>
/// 驱动核心。注入 MonoBehaviour 的 Update/OnGUI 在本 IL2CPP 环境不被调度（宿主同款教训），
/// 输入/轮询走 PlayerController.Update Postfix，绘制走 PlayerController.OnGUI Postfix（见 Patches）。
/// GenRunnerHost 只当协程宿主用（StartCoroutine 已验证可用）。
/// </summary>
internal static class GenDriver
{
	private static float nextProbeCheck; // 1.3.1：探测看门狗节流

	public static void Tick()
	{
		try
		{
			if (!Plugin.enabled.Value) return;
			bool active = HostLink.GodViewActive;
			GenPanel.RtsActive = active;

			// 1.3.1：探测看门狗（每秒一次，任何场景）——场景切换杀死探测协程后自动重启续跑
			if (Time.unscaledTime >= nextProbeCheck)
			{
				nextProbeCheck = Time.unscaledTime + 1f;
				GenCatalog.ProbeWatchdog();
				ItemCatalog.ProbeWatchdog(); // 2.0.2：物品目录枚举同款续跑看门狗
				ModCatalog.ProbeWatchdog();  // 2.2.0：第三方内容（mod 载具/物品）目录
			}

			if (!active)
			{
				Placer.Cancel("RTS 退出");
				ItemDragger.Cancel("RTS 退出", false); // 2.0.0：退出 RTS 一并收口物品携带
				GenPanel.SetOpen(false);
				return;
			}
			if (HostLink.EscMenuOpen) return; // 设置菜单打开：冻结本 mod 输入与放置（G 键不响应）

			// 注意：目录枚举/小队捕获只在面板打开时做（SetOpen），Tick 里零重活——避免进 RTS 卡顿

			if (Plugin.panelKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.panelKey.Value))
			{
				if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] G 按下（godView=true, placing=" + Placer.Placing + ", carrying=" + ItemDragger.Carrying + "）");
				if (Placer.Placing) Placer.Cancel("G 键");
				else if (ItemDragger.Carrying) ItemDragger.Cancel("G 键");
				else GenPanel.Toggle();
			}

			Placer.TickPlacing();
			ItemDragger.Tick(); // 2.0.0：物品携带/投放
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] Tick 异常: " + ex);
		}
	}

	public static void Draw()
	{
		try
		{
			if (!Plugin.enabled.Value) return;
			if (HostLink.EscMenuOpen) return; // 宿主设置菜单打开时隐藏全部本 mod UI（跟随宿主行为）
			// 2.5.1：**自适应入口放在总入口**——下面三个分支（携带/放置/面板）不一定都经过
			// GenPanel.Draw()，只在那里刷新的话"携带"和"放置"两条分支会一直用旧倍率。
			ER2Shared.Er2Ui.AutoScale();
			// 携带物品优先：全屏拖放态只画拖放 UI（与放置模式同款，画面不叠）
			if (ItemDragger.Carrying)
			{
				ItemDragger.Draw();
				GenPanel.DrawFlash();
				return;
			}
			// 放置模式画底部徽标（与面板 open 状态解耦）；否则画面板 + 左缘开关
			if (Placer.Placing) GenPanel.DrawPlacingBadge();
			else
			{
				GenPanel.DrawToggleButton();
				GenPanel.Draw();
			}
			GenPanel.DrawFlash();
			Placer.DrawPreview();
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("[UniGen] OnGUI 异常: " + ex.Message);
		}
	}
}

/// <summary>输入/轮询驱动：与宿主 InputPatch 同款（PlayerController.Update Postfix）。</summary>
[HarmonyPatch(typeof(PlayerController), "Update")]
internal static class UniGenTickPatch
{
	private static void Postfix()
	{
		GenDriver.Tick();
	}
}

/// <summary>绘制驱动：与宿主 DrawPatch 同款（PlayerController.OnGUI Postfix）。</summary>
[HarmonyPatch(typeof(PlayerController), "OnGUI")]
internal static class UniGenDrawPatch
{
	private static void Postfix()
	{
		GenDriver.Draw();
	}
}

internal static class GenRunnerHostExtensions
{
	public static Coroutine StartCoroutineW(this GenRunnerHost h, IEnumerator e)
		=> h.StartCoroutine(e.WrapToIl2Cpp());
}

/// <summary>仅当协程宿主：StartCoroutine 在本环境已验证可用；Update/OnGUI 不被调度（故 GenDriver 走 patch）。</summary>
internal class GenRunnerHost : MonoBehaviour
{
	public GenRunnerHost(IntPtr ptr) : base(ptr) { }
}
