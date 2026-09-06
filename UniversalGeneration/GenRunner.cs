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

		try
		{
			veh.SetFaction(faction);
			Plugin.ModLog.LogInfo("[UniGen] 载具已生成: " + entry.Id + " @ " + pos + " faction=" + faction);
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("[UniGen] SetFaction 失败(" + entry.Id + "): " + ex.Message);
		}

		// 临时 spawner 清理（生成物是独立实例，不受影响）
		try { if (spawnerGo != null) UnityEngine.Object.Destroy(spawnerGo); } catch { }

		RegisterSpawnedVehicle(veh);

		// 解锁：locked 载具会导致上车静默失败（宿主 BoardVehicle 同款前置）
		try { if (veh.IsLocked()) veh.SetLocked(false); } catch { }

		int seats = 0;
		try { seats = veh.seats.Length; } catch { }
		if (crewType.HasValue && seats > 0)
		{
			// 乘员流程（宿主 BoardVehicle 同款三步）：
			// ①车旁落地（超员先裁）→ ②逐兵 Lua_Soldier.boardVehicle（可靠登车）
			// → ③上车后 AIVehicle.squadInside=乘员班（驾驶资格——没有它车辆收不到指挥）
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
				StartCoroutineNative(SpawnManager.SpawnAISquadGlobal(facRef, null, sd, pos, 4f, null,
					ToIl2Cpp((Action<Squad>)(sq =>
					{
						if (sq == null) return;
						RegisterSpawnedSquad(sq);
						// 超载裁剪（登车前处理，站在车边直接移除）
						try
						{
							int cm = sq.CountMembers;
							if (cm > seats)
							{
								for (int i = cm - 1; i >= seats; i--)
								{
									try
									{
										Soldier m = sq.GetMemberClamped(i);
										if (m != null) UnityEngine.Object.Destroy(m.gameObject);
									}
									catch { }
								}
								Plugin.ModLog.LogInfo("[UniGen] 超载裁剪: " + cm + " → " + seats);
							}
						}
						catch { }
						// ②逐兵登车 + ③登车完成后给驾驶资格
						StartCoroutine(BoardCR(sq, vehRef, seats));
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
	/// 乘员登车（完整复刻宿主手动上车流程，实测该流程后车辆可指挥）：
	/// 等待落地 → 新班（FindOrNew+SetFullySpawned+isOpenForJoiners）→ 逐兵转班 → 逐兵 boardVehicle
	/// → 轮询全员上车 → AIVehicle.squadInside=新班（驾驶资格）。
	/// </summary>
	private static IEnumerator BoardCR(Squad spawnSq, Vehicle veh, int seats)
	{
		if (spawnSq == null || veh == null) yield break;

		// 等待生成完成（fullySpawned）——对未完成生成的兵下登车令会导致登车状态损坏
		// （表现为有人不上车/车辆不可指挥；手动上下车之所以有效，是因为那时早已 fullySpawned）
		float waitSpawn = Time.unscaledTime + 12f;
		while (Time.unscaledTime < waitSpawn)
		{
			bool ready = false;
			try { ready = spawnSq.fullySpawned; } catch { ready = true; }
			if (ready) break;
			yield return new WaitForSeconds(0.5f);
		}
		// 额外缓冲一拍，让落地动画收尾
		yield return new WaitForSeconds(0.5f);

		// ① 新班：与宿主 CreateNewSquad 同款三件套
		Squad ns = null;
		try
		{
			ns = Squad.FindOrNew(Guid.NewGuid().ToString());
			ns.SetFullySpawned();
			ns.isOpenForJoiners = true;
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] 新建乘员班失败（回退原班）: " + ex.Message);
		}
		Squad target = ns ?? spawnSq;

		// ② 逐兵转班（Leave+Join，宿主 AddInfantryToSquadTo 同款）
		int members = 0;
		try { members = spawnSq.CountMembers; } catch { }
		int moved = 0;
		for (int i = 0; i < members; i++)
		{
			Soldier m = null;
			try
			{
				m = spawnSq.GetMemberClamped(i);
				if (m == null || !m.IsAlive) continue;
				Squad old = m.joinedSquad;
				if (old != null && old.Pointer != target.Pointer) old.Leave(m, false);
				target.Join(m);
				moved++;
			}
			catch { }
			yield return new WaitForSeconds(0.05f);
		}
		if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 乘员转班: " + moved + "/" + members + " → " + (target.squadCode ?? "?"));
		if (ns != null)
		{
			RegisterSpawnedSquad(ns);
			spawnedSquads.Remove(spawnSq); // 原班已空，移出清除表
		}

		// ③ 逐兵登车
		Lua_Vehicle lv = null;
		try { lv = new Lua_Vehicle(veh); } catch { }
		int cnt = 0;
		try { cnt = target.CountMembers; } catch { }
		int ordered = 0;
		for (int i = 0; i < cnt; i++)
		{
			Soldier m = null;
			try
			{
				m = target.GetMemberClamped(i);
				if (m != null && m.IsAlive && lv != null) new Lua_Soldier(m).boardVehicle(lv);
			}
			catch { }
			if (m == null || !m.IsAlive) continue;
			ordered++;
			yield return new WaitForSeconds(0.15f);
		}
		if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 登车令已发: " + ordered + "/" + cnt);

		// ④ 轮询全员上车
		float deadline = Time.unscaledTime + 30f;
		int inside = 0;
		while (Time.unscaledTime < deadline)
		{
			try { inside = veh.peopleInside; } catch { inside = 0; }
			if (inside >= seats) break;
			yield return new WaitForSeconds(0.5f);
		}
		Plugin.ModLog.LogInfo("[UniGen] 乘员登车" + (inside >= seats ? "完成: " : "超时: ") + inside + "/" + seats);

		// ⑤ 驾驶资格：AIVehicle.squadInside 指向乘员班
		try
		{
			AIVehicle ai = veh.GetComponent<AIVehicle>();
			if (ai == null) ai = veh.GetComponentInChildren<AIVehicle>();
			if (ai != null)
			{
				ai.squadInside = target;
				if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] 驾驶资格已授予: squadInside=乘员班");
			}
		}
		catch (Exception ex) { Plugin.ModLog.LogWarning("[UniGen] squadInside 设置失败: " + ex.Message); }
		// ⑥ 关键：乘员班必须登记进宿主 rtsSquadSet（RTS 分队集合）——
		// 宿主 DriveVehicleTo 拒绝指挥任何 squadInside 不在该集合内的载具（"syncWindow 未就绪"）。
		// 手动下车→上车之所以有效，正是因为宿主 BoardVehicle 把新班登记了进去。
		bool reg = HostLink.RtsRegisterSquad(target);
		if (Plugin.debugLog.Value || !reg) Plugin.ModLog.LogInfo("[UniGen] RTS 分队登记: " + (reg ? "成功" : "失败"));
	}

	// ================= 步兵小队 =================

	/// <summary>生成步兵小队（SpawnAISquadGlobal，受控参数=不抢任务、听指挥）。</summary>
	public static void SpawnInfantrySquad(GenEntry entry, Vector3 pos, string faction, Action<Squad> onDone)
	{
		StartCoroutine(SpawnInfantryCR(entry, pos, faction, onDone));
	}

	private static IEnumerator SpawnInfantryCR(GenEntry entry, Vector3 pos, string faction, Action<Squad> onDone)
	{
		SquadData sd = null;
		try
		{
			if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] SpawnSquad begin id=" + entry.Id + " faction=" + faction + " pos=" + pos);
			// 枚举重载：官方 SquadType（字符串 label 已废弃——无效 label 游戏回落默认空小队）
			sd = ItemsDatabase.GetSquadLoadouts(entry.SType, 0);
			if (sd == null || sd.CountLoadouts() <= 0)
			{
				Plugin.ModLog.LogError("[UniGen] GetSquadLoadouts(" + entry.SType + ") 返回空小队——类型无效，条目已隐藏。");
				GenCatalog.RemoveInfantry(entry.SType);
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
		// 受控参数：不抢任务（原地待命）、自动接战、听 RTS 指令——"打人就行，别乱跑"
		ApplyControlledToSquad(result);
		Plugin.ModLog.LogInfo("[UniGen] 步兵小队已生成: " + entry.Id + " @ " + pos + " faction=" + faction);
		onDone?.Invoke(result);
	}

	// ================= 生成物注册表（一键清除）=================

	private static readonly List<Vehicle> spawnedVehicles = new();
	private static readonly List<Squad> spawnedSquads = new();

	private static void RegisterSpawnedVehicle(Vehicle v)
	{
		if (v != null) spawnedVehicles.Add(v);
	}

	private static void RegisterSpawnedSquad(Squad s)
	{
		if (s != null) spawnedSquads.Add(s);
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
	public static void Tick()
	{
		try
		{
			if (!Plugin.enabled.Value) return;
			bool active = HostLink.GodViewActive;
			GenPanel.RtsActive = active;

			if (!active)
			{
				Placer.Cancel("RTS 退出");
				GenPanel.SetOpen(false);
				return;
			}
			if (HostLink.EscMenuOpen) return; // 设置菜单打开：冻结本 mod 输入与放置（G 键不响应）

			// 注意：目录枚举/小队捕获只在面板打开时做（SetOpen），Tick 里零重活——避免进 RTS 卡顿

			if (Plugin.panelKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.panelKey.Value))
			{
				if (Plugin.debugLog.Value) Plugin.ModLog.LogInfo("[UniGen] G 按下（godView=true, placing=" + Placer.Placing + "）");
				if (Placer.Placing) Placer.Cancel("G 键");
				else GenPanel.Toggle();
			}

			Placer.TickPlacing();
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
			// 放置模式画底部徽标（与面板 open 状态解耦）；否则画面板 + 左下角开关
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
