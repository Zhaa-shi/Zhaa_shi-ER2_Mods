using System;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Photon.Pun;
using UnityEngine;

namespace ER2UnitCollision
{
	/// <summary>
	/// ER2 More Physics - Unit Collision（MorePhysics 轻量附属模组）。
	///
	/// 只保留 ER2 More Physics v0.1.47 的"单位 + 尸体碰撞"部分：
	///  - 活体士兵互相阻挡（复用原版贴合人体的受击碰撞体，零新增体积，玩家/AI 不再互相穿过）；
	///  - 尸体不挡活人：尸体骨骼刚体强制动态 + 近处碰撞体启用，被士兵/玩家撞到会被推开；
	///  - UnitCollisionLayer = -2 诊断模式（只打印碰撞矩阵/碰撞体信息，不改物理）。
	///
	/// 场景物件物理化 / 物品物理 / 击飞与碰撞伤害等其余功能全部移除。
	/// </summary>
	[BepInPlugin("er2.morephysics.unitcollision", "ER2 More Physics - Unit Collision", "1.0.6")]
	[BepInProcess("Easy Red 2.exe")]
	public class Plugin : BasePlugin
	{
		internal static ManualLogSource ModLog;

		internal static ConfigEntry<bool> Enabled;

		internal static ConfigEntry<bool> SingleplayerOnly;

		internal static ConfigEntry<bool> UnitCollision;

		internal static ConfigEntry<int> UnitCollisionLayer;

		internal static ConfigEntry<bool> PushCorpses;

		internal static ConfigEntry<float> CorpsePushForce;

#if CN_BUILD
		private static bool _chinese = true;
#else
		private static bool _chinese;

		private static bool _langChecked;
#endif

		public override void Load()
		{
			ModLog = Log;
			Enabled = Config.Bind("General", "Enabled", true, T("总开关：关闭后单位/尸体碰撞效果全部失效。", "Master switch. When false, unit/corpse collision effects are disabled."));
			SingleplayerOnly = Config.Bind("General", "SingleplayerOnly", true, T("默认仅单机（离线）战斗生效（联机碰撞不同步）；关闭后联机同样生效。", "By default only applies in singleplayer (offline) sessions (collision is not synced online). Disable to also apply in multiplayer."));
			UnitCollision = Config.Bind("Collision", "UnitCollision", true, T("士兵互相阻挡（不重叠）：复用原版贴合人体的受击碰撞体阻挡玩家/AI 相互穿过。默认开；尸体不参与阻挡（只被推开，不挡活人）。", "Units physically block each other (no overlap): reuses the vanilla body colliders so player/AI can't pass through each other. On by default; corpses don't block (pushed away, don't stop living units)."));
			UnitCollisionLayer = Config.Bind("Collision", "UnitCollisionLayer", -1, new ConfigDescription(T("士兵互碰的调试项：-1 = 正常；-2 = 诊断模式（只打印碰撞矩阵与受击碰撞体信息，不改任何物理）。一般无需修改。", "Unit-collision debug: -1 = normal; -2 = diagnostics (only logs the collision matrices and hit-collider details, changes nothing). Usually leave at -1."), new AcceptableValueRange<int>(-2, 31)));
			PushCorpses = Config.Bind("Collision", "PushCorpses", true, T("单位推开尸体：士兵/玩家撞到尸体时把它推开，不再从尸体上走过。独立于士兵互碰开关。", "Units push corpses aside: soldiers/player shove corpses out of the way instead of walking over them. Independent of the unit-block toggle."));
			CorpsePushForce = Config.Bind("Collision", "CorpsePushForce", 10f, new ConfigDescription(T("推开尸体的冲量力度（越大推得越远；0 = 尸体只阻挡不被推开）。", "Impulse strength used to shove corpses (higher = pushed further; 0 = corpses block but are not shoved)."), new AcceptableValueRange<float>(0f, 40f)));
			try
			{
				ClassInjector.RegisterTypeInIl2Cpp<CorpsePusher>();
			}
			catch (Exception ex)
			{
				ModLog.LogWarning((object)("UnitCollision: type registration failed: " + ex.Message));
			}
			new Harmony("er2.morephysics.unitcollision").PatchAll(typeof(Plugin).Assembly);
			ModLog.LogInfo((object)"ER2 More Physics - Unit Collision 1.0.6 loaded.");
		}

		internal static string T(string cn, string en)
		{
#if CN_BUILD
			return cn;
#else
			if (!_langChecked)
			{
				_langChecked = true;
				try
				{
					_chinese = (int)Language.currentlanguage == 6 || (int)Language.currentlanguage == 12;
				}
				catch
				{
					_chinese = false;
				}
			}
			return _chinese ? cn : en;
#endif
		}

		internal static bool IsOnline()
		{
			try
			{
				return PhotonNetwork.IsConnectedAndReady && !PhotonNetwork.OfflineMode;
			}
			catch
			{
				return false;
			}
		}

		internal static bool GateBlocked()
		{
			if (Enabled == null || Enabled.Value)
			{
				if (SingleplayerOnly != null && SingleplayerOnly.Value)
				{
					return IsOnline();
				}
				return false;
			}
			return true;
		}
	}

	/// <summary>挂在玩家/AI 的 CharacterController 上：撞到尸体时把尸体推开（0.3 秒节流）。</summary>
	public class CorpsePusher : MonoBehaviour
	{
		private float _lastPushTime;

		public CorpsePusher(IntPtr ptr)
			: base(ptr)
		{
		}

		private void OnControllerColliderHit(ControllerColliderHit hit)
		{
			try
			{
				if (hit == null || hit.rigidbody == null || Plugin.GateBlocked())
				{
					return;
				}
				Rigidbody rb = hit.rigidbody;
				// 只处理尸体（RagdollManager 下的骨骼刚体）；场景道具不属于本轻量版范围
				bool isCorpse = rb.GetComponentInParent<RagdollManager>(true) != null;
				if (!isCorpse || (Plugin.PushCorpses != null && !Plugin.PushCorpses.Value))
				{
					return;
				}
				Vector3 vel = (hit.controller != null) ? hit.controller.velocity : Vector3.zero;
				vel.y = 0f;
				float speed = vel.magnitude;
				if (speed < 0.5f)
				{
					return;
				}
				if (Time.time - _lastPushTime < 0.3f)
				{
					return;
				}
				_lastPushTime = Time.time;
				if (rb.isKinematic)
				{
					rb.isKinematic = false;
					rb.useGravity = true;
				}
				rb.WakeUp();
				float force = (Plugin.CorpsePushForce != null) ? Plugin.CorpsePushForce.Value : 10f;
				if (force <= 0f)
				{
					return;
				}
				float massFactor = 1f;
				try
				{
					massFactor = Mathf.Clamp(rb.mass, 0.5f, 20f) / 5f;
				}
				catch
				{
				}
				Vector3 dir = vel;
				if (dir.sqrMagnitude < 0.01f && hit.moveDirection != Vector3.zero)
				{
					dir = hit.moveDirection;
					dir.y = 0f;
				}
				if (dir.sqrMagnitude < 0.01f)
				{
					return;
				}
				float speedFactor = Mathf.Clamp(speed, 0.5f, 2f);
				rb.AddForceAtPosition(dir.normalized * force * speedFactor * massFactor, hit.point, ForceMode.Impulse);
			}
			catch
			{
			}
		}
	}

	// 每帧驱动 UnitCollision.Apply()（内部 1 秒节流）
	[HarmonyPatch(typeof(PlayerController), "Update")]
	internal static class PhysicsTickPatch
	{
		private static void Postfix()
		{
			try
			{
				if (Plugin.GateBlocked())
				{
					return;
				}
				UnitCollision.Apply();
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("UnitCollision tick failed: " + ex.Message));
			}
		}
	}

	/// <summary>单位互碰 + 尸体物理（源自 MorePhysics v0.1.47 的 UnitCollision 模块，去掉道具推动部分）。</summary>
	internal static class UnitCollision
	{
		private const int BodyPartLayer = 9; // 受击碰撞体层（贴合人体）

		private const int CorpseLayerFallback = 10; // 尸体层兜底（拿不到 RagdollManager.ragdollizedLayer 时）

		private static int _ccLayer = -1;

		private static int _warned;

		private static float _nextApply;

		private static float _lastMatrixLog;

		private static readonly HashSet<int> _pusherGos = new HashSet<int>();

		private static float _lastProbeLog;

		private static float _lastCorpseLog;

		private static readonly Dictionary<int, float> _corpsePushTimes = new Dictionary<int, float>();

		private static readonly List<Soldier> _living = new List<Soldier>();

		private static readonly List<RagdollManager> _corpses = new List<RagdollManager>();

		private static readonly List<RagdollManager> _scannedCorpses = new List<RagdollManager>();

		// 每帧预计算缓冲（避免活体两两检测时 O(n²) 次 IL2CPP 调用）
		private static readonly Vector3[] _livingPos = new Vector3[512];

		private static readonly bool[] _livingIsPlayer = new bool[512];

		private static readonly float[] _livingRadius = new float[512];

		private static float _lastCorpseScan;

		private static int _corpseScanSrc;

		private static float _lastOverlapLog;

		private static int _overlapPushed;

		private static int _corpsePushes;

		private static int _aiCorpsePairs;

		internal static void Apply()
		{
			try
			{
				bool unitColl = Plugin.UnitCollision != null && Plugin.UnitCollision.Value;
				bool pushCorpses = Plugin.PushCorpses != null && Plugin.PushCorpses.Value;
				bool diag = Plugin.UnitCollisionLayer != null && Plugin.UnitCollisionLayer.Value == -2;
				// 位置级防重叠/推尸体：AI 走 NavMeshAgent（transform 直写）不走物理，
				// 碰撞矩阵只对玩家 CC 生效——AI 与 AI/玩家/尸体的交互必须每帧手动处理。
				if (unitColl || pushCorpses)
				{
					ResolveOverlaps();
				}
				if ((!unitColl && !diag && !pushCorpses) || Time.time < _nextApply)
				{
					return;
				}
				_nextApply = Time.time + 1f;
				if (_ccLayer < 0 && !ProbeCcLayer())
				{
					return;
				}
				if (diag)
				{
					ProbeCcLayer();
					ProbeBodies();
					return;
				}
				bool changed = false;
				if (unitColl && Physics.GetIgnoreLayerCollision(_ccLayer, 9))
				{
					Physics.IgnoreLayerCollision(_ccLayer, 9, false);
					changed = true;
				}
				int corpseLayer = GetCorpseLayer();
				if (pushCorpses && corpseLayer >= 0 && corpseLayer != _ccLayer && Physics.GetIgnoreLayerCollision(_ccLayer, corpseLayer))
				{
					Physics.IgnoreLayerCollision(_ccLayer, corpseLayer, false);
					changed = true;
				}
				if (changed && Time.time - _lastMatrixLog >= 10f)
				{
					_lastMatrixLog = Time.time;
					Plugin.ModLog.LogInfo((object)("UC: unit-collision ON ccLayer=" + _ccLayer + " bodyPartLayer=" + 9 + " corpseLayer=" + corpseLayer + " changed=" + changed));
				}
				EnsureCorpsePhysics();
				EnsurePushers();
			}
			catch (Exception ex)
			{
				if (_warned < 3)
				{
					_warned++;
					Plugin.ModLog.LogWarning((object)("UnitCollision failed: " + ex.Message));
				}
			}
		}

		private static int GetCorpseLayer()
		{
			try
			{
				int ragdollizedLayer = RagdollManager.ragdollizedLayer;
				if (ragdollizedLayer >= 0)
				{
					return ragdollizedLayer;
				}
			}
			catch
			{
			}
			return 10;
		}

		private static bool ProbeCcLayer()
		{
			bool log = Time.time - _lastProbeLog >= 10f;
			if (log)
			{
				_lastProbeLog = Time.time;
			}
			try
			{
				Il2CppSystem.Collections.Generic.List<Creature> alive = Creature.aliveCreatures;
				if (alive == null)
				{
					return false;
				}
				int playerLayer = -1;
				int aiLayer = -1;
				bool anyAiHasCc = false;
				foreach (Creature c in alive)
				{
					try
					{
						if (c == null || c.transform == null)
						{
							continue;
						}
						Soldier s = c.TryCast<Soldier>();
						if (s == null)
						{
							continue;
						}
						int layer = (s.m_controller != null) ? s.m_controller.gameObject.layer : -1;
						if (layer < 0)
						{
							continue;
						}
						bool isPlayer = false;
						try
						{
							isPlayer = c.IsPlayer();
						}
						catch
						{
						}
						if (isPlayer)
						{
							playerLayer = layer;
						}
						else
						{
							aiLayer = layer;
							anyAiHasCc = true;
						}
					}
					catch
					{
					}
				}
				// 玩家 CC 层优先；拿不到就用任意士兵的；都没有则用文档实证的固定层 1
				if (playerLayer >= 0)
				{
					_ccLayer = playerLayer;
				}
				else if (anyAiHasCc)
				{
					_ccLayer = aiLayer;
				}
				else if (_ccLayer < 0)
				{
					_ccLayer = 1;
				}
				if (log)
				{
					Plugin.ModLog.LogInfo((object)("UC: unit-matrix playerCcLayer=" + playerLayer + " aiCcLayer=" + aiLayer + " ccLayer=" + _ccLayer + " bodyPartLayer=" + 9 + " ignore=" + ((_ccLayer >= 0) ? Physics.GetIgnoreLayerCollision(_ccLayer, 9).ToString() : "n/a")));
				}
				return _ccLayer >= 0;
			}
			catch
			{
				return false;
			}
		}

		private static void ProbeBodies()
		{
			try
			{
				int corpseLayer = GetCorpseLayer();
				RagdollManager rm = FindAnyCorpse();
				if (rm == null || rm.transform == null)
				{
					Plugin.ModLog.LogInfo((object)("UC: corpse-diagnostics no ragdoll manager; layer=" + corpseLayer));
					return;
				}
				Rigidbody[] rbs = rm.GetComponentsInChildren<Rigidbody>(true);
				int shown = 0;
				for (int i = 0; i < rbs.Length && shown < 5; i++)
				{
					Rigidbody rb = rbs[i];
					if (rb == null)
					{
						continue;
					}
					int enabled = 0;
					Collider[] cols = rb.GetComponentsInChildren<Collider>(true);
					for (int j = 0; j < cols.Length; j++)
					{
						if (cols[j] != null && cols[j].enabled)
						{
							enabled++;
						}
					}
					Plugin.ModLog.LogInfo((object)("UC: corpse-rb '" + rb.gameObject.name + "' kin=" + rb.isKinematic + " mass=" + rb.mass + " layer=" + rb.gameObject.layer + " colliders=" + enabled + "/" + cols.Length));
					shown++;
				}
				Plugin.ModLog.LogInfo((object)("UC: corpse-diagnostics ragdollizedLayer=" + corpseLayer + " ccLayer=" + _ccLayer + " ignoreCcVsCorpse=" + ((_ccLayer >= 0 && corpseLayer >= 0) ? Physics.GetIgnoreLayerCollision(_ccLayer, corpseLayer).ToString() : "n/a") + " pushers=" + _pusherGos.Count));
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("UnitCollision corpse diagnostics failed: " + ex.Message));
			}
		}

		// 尸体骨骼刚体动态化（可被推开）：只在活人（玩家/AI）5 米内才强制 dynamic +
		// 启用被游戏禁用的碰撞体；远处尸体不碰，交还游戏原生管理（ProcessPausePhysic 等），
		// 避免全战场尸体进入活跃物理导致抽搐/性能问题。
		private static void EnsureCorpsePhysics()
		{
			try
			{
				Il2CppSystem.Collections.Generic.List<Creature> alive = Creature.aliveCreatures;
				if (alive == null)
				{
					return;
				}
				List<RagdollManager> corpses = null;
				List<Vector3> corpsePos = null;
				List<Vector3> livingPos = null;
				foreach (Creature c in alive)
				{
					try
					{
						if (c == null || c.transform == null)
						{
							continue;
						}
						Soldier s = c.TryCast<Soldier>();
						if (s == null)
						{
							continue;
						}
						if (c.ragdoll_manager != null && c.ragdoll_manager.ragdollized)
						{
							if (corpses == null)
							{
								corpses = new List<RagdollManager>();
								corpsePos = new List<Vector3>();
							}
							corpses.Add(c.ragdoll_manager);
							corpsePos.Add(c.transform.position);
						}
						else
						{
							// 活人（玩家或 AI，不依赖 m_controller——AI 用 NavMeshAgent 移动可能没有 CC）
							if (livingPos == null)
							{
								livingPos = new List<Vector3>();
							}
							livingPos.Add(c.transform.position);
						}
					}
					catch
					{
					}
				}
				if (corpses == null)
				{
					return;
				}
				int madeDynamic = 0;
				int collidersEnabled = 0;
				for (int i = 0; i < corpses.Count; i++)
				{
					RagdollManager rm = corpses[i];
					if (rm == null || rm.transform == null)
					{
						continue;
					}
					bool nearLiving = false;
					if (livingPos != null)
					{
						Vector3 p = corpsePos[i];
						foreach (Vector3 lp in livingPos)
						{
							Vector3 d = lp - p;
							if (d.x * d.x + d.y * d.y + d.z * d.z < 25f)
							{
								nearLiving = true;
								break;
							}
						}
					}
					if (!nearLiving)
					{
						continue; // 远处尸体：交还游戏原生管理
					}
					Rigidbody[] rbs = rm.GetComponentsInChildren<Rigidbody>(true);
					for (int k = 0; k < rbs.Length; k++)
					{
						Rigidbody rb = rbs[k];
						if (rb == null)
						{
							continue;
						}
						try
						{
							if (rb.isKinematic)
							{
								rb.isKinematic = false;
								rb.useGravity = true;
								madeDynamic++;
							}
							Collider[] cols = rb.GetComponentsInChildren<Collider>(true);
							for (int m = 0; m < cols.Length; m++)
							{
								if (cols[m] != null && !cols[m].enabled && !cols[m].isTrigger)
								{
									cols[m].enabled = true;
									collidersEnabled++;
								}
							}
						}
						catch
						{
						}
					}
				}
				if ((madeDynamic > 0 || collidersEnabled > 0) && Time.time - _lastCorpseLog > 10f)
				{
					_lastCorpseLog = Time.time;
					Plugin.ModLog.LogInfo((object)("UC: corpse physics active (dynamic " + madeDynamic + ", colliders " + collidersEnabled + ")."));
				}
			}
			catch
			{
			}
		}

		// 给所有活体士兵的 CharacterController 挂 CorpsePusher（幂等）
		private static void EnsurePushers()
		{
			try
			{
				Il2CppSystem.Collections.Generic.List<Creature> alive = Creature.aliveCreatures;
				if (alive == null)
				{
					return;
				}
				if (_pusherGos.Count > 4096)
				{
					_pusherGos.Clear();
				}
				foreach (Creature c in alive)
				{
					try
					{
						if (c == null || c.transform == null)
						{
							continue;
						}
						Soldier s = c.TryCast<Soldier>();
						if (s == null || s.m_controller == null || s.m_controller.gameObject == null)
						{
							continue;
						}
						GameObject go = s.m_controller.gameObject;
						int id = go.GetInstanceID();
						if (_pusherGos.Add(id) && go.GetComponent<CorpsePusher>() == null)
						{
							go.AddComponent<CorpsePusher>();
						}
					}
					catch
					{
					}
				}
			}
			catch
			{
			}
		}

		// 找任意一具尸体（诊断用）：优先从单位表找，找不到再兜底全场景搜索（仅诊断模式触发）
		private static RagdollManager FindAnyCorpse()
		{
			try
			{
				Il2CppSystem.Collections.Generic.List<Creature> alive = Creature.aliveCreatures;
				if (alive != null)
				{
					foreach (Creature c in alive)
					{
						try
						{
							if (c == null || c.transform == null)
							{
								continue;
							}
							Soldier s = c.TryCast<Soldier>();
							if (s != null && c.ragdoll_manager != null && c.ragdoll_manager.ragdollized)
							{
								return c.ragdoll_manager;
							}
						}
						catch
						{
						}
					}
				}
				return UnityEngine.Object.FindObjectOfType<RagdollManager>();
			}
			catch
			{
				return null;
			}
		}

		// ===== 位置级交互（每帧）=====
		// AI 士兵用 NavMeshAgent 移动（transform 直写，不走物理碰撞），碰撞矩阵与
		// OnControllerColliderHit 只对玩家生效。为了让 AI 也参与碰撞，这里每帧手动：
		//  - 活体-活体：水平距离小于阈值即分离（只推 AI，玩家由物理矩阵管，绝不推玩家）；
		//  - AI-尸体：PushCorpses 开启且力度>0 时给尸体骨骼刚体施加冲量（推尸体），
		//    否则把 AI 推出尸体（尸体只阻挡）。
		private static void ResolveOverlaps()
		{
			try
			{
				Il2CppSystem.Collections.Generic.List<Creature> alive = Creature.aliveCreatures;
				if (alive == null)
				{
					return;
				}
				_living.Clear();
				_corpses.Clear();
				foreach (Creature c in alive)
				{
					try
					{
						if (c == null || c.transform == null)
						{
							continue;
						}
						Soldier s = c.TryCast<Soldier>();
						if (s == null)
						{
							continue;
						}
						// 尸体判定：IsDead 优先（比 ragdollized 可靠），ragdollized 兜底
						bool isCorpse = false;
						try
						{
							isCorpse = c.IsDead;
						}
						catch
						{
							isCorpse = false;
						}
						if (!isCorpse)
						{
							isCorpse = c.ragdoll_manager != null && c.ragdoll_manager.ragdollized;
						}
						if (isCorpse && c.ragdoll_manager != null)
						{
							_corpses.Add(c.ragdoll_manager);
						}
						else
						{
							_living.Add(s);
						}
					}
					catch
					{
					}
				}
				// 尸体数据源 2：allCreatures（全单位表，含尸体——ragdollized 单位会被移出
				// aliveCreatures，这是 AI 推不动尸体的根因）。每帧遍历，开销与 alive 同级。
				try
				{
					Il2CppSystem.Collections.Generic.List<Creature> all = Creature.allCreatures;
					if (all != null)
					{
						foreach (Creature c in all)
						{
							try
							{
								if (c == null || c.ragdoll_manager == null || c.ragdoll_manager.transform == null)
								{
									continue;
								}
								bool dead = false;
								try
								{
									dead = c.IsDead;
								}
								catch
								{
								}
								if (!dead)
								{
									continue;
								}
								RagdollManager rm = c.ragdoll_manager;
								if (!_corpses.Contains(rm))
								{
									_corpses.Add(rm);
								}
							}
							catch
							{
							}
						}
					}
				}
				catch
				{
				}
				// 尸体数据源 3：全场景 RagdollManager 扫描（1s 刷新 + 缓存复用，
				// 结果不随帧丢弃——扫描帧之外也用缓存，保证尸体列表每帧可用）
				if (Time.time - _lastCorpseScan > 1f)
				{
					_lastCorpseScan = Time.time;
					_scannedCorpses.Clear();
					try
					{
						RagdollManager[] all = UnityEngine.Object.FindObjectsOfType<RagdollManager>(true);
						for (int i = 0; i < all.Length; i++)
						{
							RagdollManager rm = all[i];
							if (rm == null || rm.transform == null)
							{
								continue;
							}
							bool rd = false;
							try
							{
								rd = rm.ragdollized;
							}
							catch
							{
							}
							if (rd)
							{
								_scannedCorpses.Add(rm);
							}
						}
						if (_scannedCorpses.Count > 0 && Time.time - _lastCorpseLog > 10f)
						{
							_lastCorpseLog = Time.time;
							Plugin.ModLog.LogInfo((object)("UC: corpses via scene scan (" + _scannedCorpses.Count + "), aliveCreatures=" + _corpses.Count + "."));
						}
					}
					catch
					{
					}
				}
				// 合并扫描缓存（去重）——非扫描帧也复用缓存
				for (int i = 0; i < _scannedCorpses.Count; i++)
				{
					RagdollManager rm = _scannedCorpses[i];
					if (rm != null && !_corpses.Contains(rm))
					{
						_corpses.Add(rm);
					}
				}
				// 来源标记：0 = 单位表（alive/allCreatures），1 = 场景扫描兜底
				_corpseScanSrc = (_corpses.Count > 0 && _scannedCorpses.Count == 0) ? 0 : 1;
				if (_living.Count == 0)
				{
					return;
				}
				// 预计算活体位置/玩家标记/半径（每帧一次，避免两两检测时重复 IL2CPP 调用）
				int livingCount = _living.Count;
				if (livingCount > 512)
				{
					livingCount = 512; // 极端大场面截断保护（同屏 512 活体不现实）
				}
				for (int i = 0; i < livingCount; i++)
				{
					Soldier s = _living[i];
					_livingPos[i] = (s != null && s.transform != null) ? s.transform.position : Vector3.zero;
					_livingIsPlayer[i] = s != null && IsPlayerUnit(s);
					_livingRadius[i] = (s != null) ? GetUnitRadius(s) : 0.4f;
				}
				// 活体-活体防重叠
				for (int i = 0; i < livingCount; i++)
				{
					Soldier a = _living[i];
					if (a == null || a.transform == null)
					{
						continue;
					}
					Vector3 pa = _livingPos[i];
					for (int j = i + 1; j < livingCount; j++)
					{
						Soldier b = _living[j];
						if (b == null || b.transform == null)
						{
							continue;
						}
						Vector3 d = _livingPos[j] - pa;
						d.y = 0f;
						// 粗筛：远超两人半径和的直接跳过（避免全量平方根）
						if (d.x > 1.6f || d.x < -1.6f || d.z > 1.6f || d.z < -1.6f)
						{
							continue;
						}
						float dist = d.magnitude;
						if (dist < 0.0001f)
						{
							continue;
						}
						float threshold = Mathf.Max((_livingRadius[i] + _livingRadius[j]) * 0.9f, 0.65f);
						if (dist >= threshold)
						{
							continue;
						}
						Vector3 dir = d / dist;
						float overlap = threshold - dist;
						bool aPlayer = _livingIsPlayer[i];
						bool bPlayer = _livingIsPlayer[j];
						if (aPlayer && !bPlayer)
						{
							PushSoldier(b, dir, overlap); // AI 撞玩家 → 推 AI，玩家不动
						}
						else if (bPlayer && !aPlayer)
						{
							PushSoldier(a, -dir, overlap);
						}
						else if (!aPlayer && !bPlayer)
						{
							PushSoldier(a, -dir, overlap * 0.5f);
							PushSoldier(b, dir, overlap * 0.5f);
						}
						// 玩家-玩家：不推（单机不存在；联机不同步，由 SingleplayerOnly 默认关）
					}
				}
				// AI-尸体：推尸体或把 AI 推出
				if (_corpses.Count > 0)
				{
					bool pushOn = Plugin.PushCorpses != null && Plugin.PushCorpses.Value;
					float force = (Plugin.CorpsePushForce != null) ? Plugin.CorpsePushForce.Value : 10f;
					for (int i = 0; i < livingCount; i++)
					{
						Soldier l = _living[i];
						if (l == null || l.transform == null || _livingIsPlayer[i])
						{
							continue; // 玩家推尸体走原生 CC 碰撞回调，这里只补 AI
						}
						Vector3 lp = _livingPos[i];
						for (int k = 0; k < _corpses.Count; k++)
						{
							RagdollManager rm = _corpses[k];
							if (rm == null || rm.transform == null)
							{
								continue;
							}
							Vector3 d = rm.transform.position - lp;
							d.y = 0f;
							// 粗筛
							if (d.x > 1.5f || d.x < -1.5f || d.z > 1.5f || d.z < -1.5f)
							{
								continue;
							}
							float dist = d.magnitude;
							if (dist >= 1.2f || dist < 0.0001f)
							{
								continue;
							}
							Vector3 dir = d / dist;
							_aiCorpsePairs++;
							if (pushOn && force > 0f)
							{
								if (IsAiMoving(l))
								{
									// 移动中的 AI：把尸体整体推开（kinematic 平移 / dynamic 速度直写）
									PushCorpseWhole(rm, dir, force);
								}
								else if (dist < 0.85f)
								{
									// 静止 AI 与尸体重叠：把 AI 推出尸体（不站在尸体上穿模）
									PushSoldier(l, -dir, 0.85f - dist);
								}
							}
							else
							{
								PushSoldier(l, -dir, 1.2f - dist); // 尸体只阻挡：把 AI 挡在外面
							}
						}
					}
				}
				if ((_overlapPushed > 0 || _corpsePushes > 0 || _aiCorpsePairs > 0) && Time.time - _lastOverlapLog >= 10f)
				{
					_lastOverlapLog = Time.time;
					Plugin.ModLog.LogInfo((object)("UC: overlaps (living " + _living.Count + ", corpses " + _corpses.Count + " src " + _corpseScanSrc + ", pushed " + _overlapPushed + ", corpsePushes " + _corpsePushes + ", aiPairs " + _aiCorpsePairs + ")."));
					_overlapPushed = 0;
					_corpsePushes = 0;
					_aiCorpsePairs = 0;
				}
			}
			catch
			{
			}
		}

		private static bool IsPlayerUnit(Soldier s)
		{
			try
			{
				return ((Creature)s).IsPlayer();
			}
			catch
			{
				return false;
			}
		}

		private static float GetUnitRadius(Soldier s)
		{
			try
			{
				if (s.m_controller != null && s.m_controller.radius > 0.01f)
				{
					return s.m_controller.radius;
				}
			}
			catch
			{
			}
			return 0.4f;
		}

		// 位置修正（只推 AI）：限速防抖，多帧收敛；y 不动
		private static void PushSoldier(Soldier s, Vector3 dir, float amount)
		{
			try
			{
				if (s == null || s.transform == null || amount <= 0f)
				{
					return;
				}
				float step = Mathf.Min(amount, 0.08f);
				Vector3 p = s.transform.position;
				s.transform.position = new Vector3(p.x + dir.x * step, p.y, p.z + dir.z * step);
				_overlapPushed++;
			}
			catch
			{
			}
		}

		// AI 推尸体（混合策略，0.15 秒/具节流）：
		//  - kinematic 刚体（游戏冻结物理的尸体）：直接平移 transform（保持相对位置 = 整体搬运），
		//    不依赖物理引擎——游戏怎么冻结都推得动；
		//  - dynamic 刚体：velocity 直写同一水平速度（整体位移，关节无相对应力）。
		private static void PushCorpseWhole(RagdollManager rm, Vector3 dir, float force)
		{
			try
			{
				int id = rm.gameObject.GetInstanceID();
				if (_corpsePushTimes.TryGetValue(id, out float last) && Time.time - last < 0.15f)
				{
					return;
				}
				_corpsePushTimes[id] = Time.time;
				if (_corpsePushTimes.Count > 256)
				{
					_corpsePushTimes.Clear();
				}
				Rigidbody[] rbs = rm.GetComponentsInChildren<Rigidbody>(true);
				if (rbs.Length == 0)
				{
					return;
				}
				// 目标水平速度：force 10 → 1.5 m/s；force 40 → 3.6 m/s
				float speed = 0.8f + force * 0.07f;
				// kinematic 刚体每次平移量（≈ speed × 0.15s）
				Vector3 delta = new Vector3(dir.x * speed * 0.15f, 0f, dir.z * speed * 0.15f);
				for (int i = 0; i < rbs.Length; i++)
				{
					Rigidbody rb = rbs[i];
					if (rb == null)
					{
						continue;
					}
					try
					{
						if (rb.isKinematic)
						{
							Transform t = rb.transform;
							Vector3 p = t.position;
							t.position = new Vector3(p.x + delta.x, p.y, p.z + delta.z);
						}
						else
						{
							rb.WakeUp();
							Vector3 v = rb.velocity;
							rb.velocity = new Vector3(dir.x * speed, v.y, dir.z * speed);
						}
					}
					catch
					{
					}
				}
				_overlapPushed++;
				_corpsePushes++;
			}
			catch
			{
			}
		}

		// AI 是否正在水平移动（位置差分测速，不依赖 NavMeshAgent 组件）
		private static readonly Dictionary<int, Vector3> _aiLastPos = new Dictionary<int, Vector3>();

		private static readonly Dictionary<int, float> _aiLastTime = new Dictionary<int, float>();

		private static bool IsAiMoving(Soldier s)
		{
			try
			{
				int id = s.gameObject.GetInstanceID();
				Vector3 p = s.transform.position;
				if (_aiLastPos.TryGetValue(id, out Vector3 last) && _aiLastTime.TryGetValue(id, out float t))
				{
					float dt = Time.time - t;
					if (dt > 0.01f)
					{
						Vector3 d = p - last;
						d.y = 0f;
						float spd = d.magnitude / dt;
						_aiLastPos[id] = p;
						_aiLastTime[id] = Time.time;
						return spd > 0.5f;
					}
				}
				_aiLastPos[id] = p;
				_aiLastTime[id] = Time.time;
				if (_aiLastPos.Count > 2048)
				{
					_aiLastPos.Clear();
					_aiLastTime.Clear();
				}
				return false;
			}
			catch
			{
				return false;
			}
		}
	}
}
