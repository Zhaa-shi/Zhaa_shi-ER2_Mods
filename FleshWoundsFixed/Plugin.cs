using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using System.Security;
using System.Security.Permissions;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Core.Logging.Interpolation;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using BepInEx.Unity.IL2CPP.Utils.Collections;
using Corvostudio.Weapons;
using ER2.ModKit;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Microsoft.CodeAnalysis;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;
using Random = UnityEngine.Random;

[assembly: CompilationRelaxations(8)]
[assembly: RuntimeCompatibility(WrapNonExceptionThrows = true)]
[assembly: Debuggable(DebuggableAttribute.DebuggingModes.IgnoreSymbolStoreSequencePoints)]
[assembly: AssemblyMetadata("VerifiedGameVersion", "921")]
[assembly: AssemblyCompany("ER2_FleshWoundsBW")]
[assembly: AssemblyConfiguration("Release")]
[assembly: AssemblyFileVersion("1.0.1.0")]
[assembly: AssemblyInformationalVersion("1.0.1")]
[assembly: AssemblyProduct("ER2 Flesh Wounds")]
[assembly: AssemblyTitle("ER2_FleshWoundsBW")]
[assembly: SecurityPermission(SecurityAction.RequestMinimum, SkipVerification = true)]
[assembly: AssemblyVersion("1.0.1.0")]
[module: UnverifiableCode]
namespace Microsoft.CodeAnalysis
{
	[CompilerGenerated]
	[Microsoft.CodeAnalysis.Embedded]
	internal sealed class EmbeddedAttribute : Attribute
	{
	}
}
namespace System.Runtime.CompilerServices
{
	[CompilerGenerated]
	[Microsoft.CodeAnalysis.Embedded]
	[AttributeUsage(AttributeTargets.Module, AllowMultiple = false, Inherited = false)]
	internal sealed class RefSafetyRulesAttribute : Attribute
	{
		public readonly int Version;

		public RefSafetyRulesAttribute(int P_0)
		{
			Version = P_0;
		}
	}
}
namespace ER2.ModKit
{
	internal static class GameVersionCheck
	{
		internal static void Run(ManualLogSource log, string logPrefix)
		{
			//IL_0078: Unknown result type (might be due to invalid IL or missing references)
			//IL_007e: Expected O, but got Unknown
			//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ea: Expected O, but got Unknown
			int gameVersion;
			try
			{
				gameVersion = ResourcesManager.gameVersion;
			}
			catch (Exception ex)
			{
				log.LogInfo((object)(logPrefix + " Could not read the game build number (" + ex.GetType().Name + "). Version check skipped."));
				return;
			}
			string text = Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>().FirstOrDefault((AssemblyMetadataAttribute a) => a.Key == "VerifiedGameVersion")?.Value;
			bool flag = default(bool);
			if (string.IsNullOrWhiteSpace(text))
			{
				BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(125, 3, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(logPrefix);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" Game build ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(gameVersion);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(". This mod is not stamped with a verified ");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("build — set <VerifiedGameVersion>");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(gameVersion);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("</VerifiedGameVersion> in its .csproj.");
				}
				log.LogInfo(val);
			}
			else if (text.Trim() == gameVersion.ToString())
			{
				BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(24, 2, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(logPrefix);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" Game build ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(gameVersion);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" (verified).");
				}
				log.LogInfo(val);
			}
			else
			{
				log.LogWarning((object)($"{logPrefix} Game build {gameVersion}, but this mod was only verified against build {text}. Easy Red 2 has updated since. If anything behaves oddly, " + "re-vendor libs/ and re-run the hook check before reporting a bug."));
			}
		}
	}
}
namespace ER2_FleshWounds
{
	public static class BloodPainter
	{
		private readonly struct TargetKey(int rootId, string baseName) : IEquatable<TargetKey>
		{
			public readonly int rootId = rootId;

			public readonly string baseName = baseName;

			public bool Equals(TargetKey other)
			{
				if (rootId == other.rootId)
				{
					return baseName == other.baseName;
				}
				return false;
			}

			public override bool Equals(object obj)
			{
				if (obj is TargetKey other)
				{
					return Equals(other);
				}
				return false;
			}

			public override int GetHashCode()
			{
				return (rootId * 397) ^ ((baseName != null) ? baseName.GetHashCode() : 0);
			}
		}

		private class WoundTarget
		{
			public TargetKey key;

			public Texture2D tex;

			public Color32[] pixels;

			public int w;

			public int h;

			public Material original;

			public Material clone;

			public Transform root;

			public int rootId;

			public bool dirty;

			public int dirtyX0;

			public int dirtyY0;

			public int dirtyX1;

			public int dirtyY1;
		}

		public struct HitData
		{
			public BodyPart bodyPart;

			public BulletData bulletData;

			public RaycastHit gameplayHit;

			public Vector3 hitDirection;

			public bool isPlayer;
		}

		private class MeshTopo
		{
			public int[] tris;

			public Vector2[] uvs;

			public int[] subStart;

			public int[] subLen;
		}

		private struct RayHitInfo
		{
			public SkinnedMeshRenderer smr;

			public int firstIndex;

			public float u;

			public float v;

			public Vector3 worldPoint;
		}

		private struct RayCand
		{
			public SkinnedMeshRenderer smr;

			public float enter;

			public bool flesh;
		}

		private struct SmrFlags
		{
			public bool flesh;

			public bool isLod;

			public bool impostor;
		}

		private class BakeEntry
		{
			public Vector3[] verts;

			public float time;

			public int meshId;
		}

		public struct Brush
		{
			public Color32[] px;

			public int w;

			public int h;
		}

		private class AlbedoSeed
		{
			public Texture2D gpuTex;

			public Color32 avgColor;

			public int w;

			public int h;

			public Color32[] pixels;

			public TextureWrapMode wrapMode;

			public Texture2D halfGpuTex;

			public Color32[] halfPixels;

			public int halfW;

			public int halfH;

			public string srcTexKey;

			public int srcTexId;
		}

		private static readonly Dictionary<TargetKey, WoundTarget> _targets = new Dictionary<TargetKey, WoundTarget>();

		private static readonly Dictionary<int, List<WoundTarget>> _soldierTargets = new Dictionary<int, List<WoundTarget>>();

		private static readonly HashSet<WoundTarget> _dirtyTargets = new HashSet<WoundTarget>();

		private static readonly List<Object> _pendingDestroy = new List<Object>();

		private static readonly Dictionary<int, int> _decalCount = new Dictionary<int, int>();

		private static readonly Dictionary<int, int> _aiExitCount = new Dictionary<int, int>();

		private static readonly Dictionary<int, long> _soldierLastUsed = new Dictionary<int, long>();

		private static readonly Dictionary<int, bool> _soldierHasPlayer = new Dictionary<int, bool>();

		private static long _useTick;

		private static readonly Dictionary<int, SkinnedMeshRenderer[]> _soldierSmrCache = new Dictionary<int, SkinnedMeshRenderer[]>();

		private static readonly HashSet<int> _warnedNoSmr = new HashSet<int>();

		private static readonly Queue<HitData> _hitQueue = new Queue<HitData>();

		private static readonly Queue<HitData> _playerQueue = new Queue<HitData>();

		internal static Transform _playerRef;

		private static readonly Stopwatch _playerFrameSw = new Stopwatch();

		private static int _playerBudgetFrame = -1;

		private static readonly Stopwatch _aiFrameSw = new Stopwatch();

		private static int _aiBudgetFrame = -1;

		private static Mesh _probeMesh;

		private static bool _inventoryLogged;

		private static readonly Dictionary<int, MeshTopo> _topoCache = new Dictionary<int, MeshTopo>();

		private static readonly List<RayCand> _cands = new List<RayCand>(8);

		private static readonly Comparison<RayCand> _candCmp = delegate(RayCand x, RayCand y)
		{
			if (x.flesh == y.flesh)
			{
				return x.enter.CompareTo(y.enter);
			}
			return x.flesh ? 1 : (-1);
		};

		private const float EarlyOutDistSqr = 0.09f;

		private const float TriPrefilterSqr = 0.36f;

		private static readonly Dictionary<int, SmrFlags> _smrFlags = new Dictionary<int, SmrFlags>();

		private static readonly Dictionary<int, BakeEntry> _bakeCache = new Dictionary<int, BakeEntry>();

		private static int _bakeSweepFrame = -1;

		private static readonly List<int> _bakeSweepScratch = new List<int>();

		private const float BakeIdleRetentionSeconds = 5f;

		private const float ExitProbeEpsilon = 0.05f;

		private const float ExitTriPrefilterSqr = 1f;

		private const float ExitEarlyOutDistSqr = 0.25f;

		public static Brush[] _brushes;

		private static readonly int[] _exitBrushIndices = new int[4] { 0, 1, 2, 4 };

		private const int SCRATCH = 512;

		private static readonly int[] _scratchBuckets = new int[4] { 64, 128, 256, 512 };

		private static Texture2D[] _scratchTexBuckets;

		private static Color32[][] _scratchPxBuckets;

		private static Texture2D _scratchTex;

		private static readonly Dictionary<string, AlbedoSeed> _albedoCache = new Dictionary<string, AlbedoSeed>();

		private static readonly Dictionary<string, List<string>> _seedsByTexName = new Dictionary<string, List<string>>(StringComparer.Ordinal);

		private static readonly HashSet<string> _pendingSeedInvalidations = new HashSet<string>(StringComparer.Ordinal);

		private static readonly List<string> _invalMatScratch = new List<string>();

		private static readonly HashSet<int> _invalRootScratch = new HashSet<int>();

		private static bool _warnedNoMaterial;

		public static event Action<Vector3, Vector3, Transform, bool> OnExitWound;

		private static Vector3 GetPlayerRefPos()
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_0027: Unknown result type (might be due to invalid IL or missing references)
			if ((Object)(object)_playerRef != (Object)null)
			{
				return _playerRef.position;
			}
			Camera main = Camera.main;
			if (!((Object)(object)main != (Object)null))
			{
				return Vector3.zero;
			}
			return ((Component)main).transform.position;
		}

		public static void HandlePlayerHit(BodyPart bodyPart, BulletData bulletData, RaycastHit hit, Vector3 dir)
		{
			//IL_0042: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Unknown result type (might be due to invalid IL or missing references)
			//IL_0087: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			if (!Plugin.Enabled)
			{
				return;
			}
			int frameCount = Time.frameCount;
			if (frameCount != _playerBudgetFrame)
			{
				_playerBudgetFrame = frameCount;
				_playerFrameSw.Restart();
			}
			if (_playerFrameSw.Elapsed.TotalMilliseconds < (double)Plugin.PlayerMsPerFrame)
			{
				PaintWound(bodyPart, bulletData, hit, dir, playerShot: true);
				return;
			}
			if (_playerQueue.Count >= 256)
			{
				_playerQueue.Dequeue();
			}
			_playerQueue.Enqueue(new HitData
			{
				bodyPart = bodyPart,
				bulletData = bulletData,
				gameplayHit = hit,
				hitDirection = dir,
				isPlayer = true
			});
		}

		public static void EnqueueHit(BodyPart bodyPart, BulletData bulletData, RaycastHit hit, Vector3 dir)
		{
			//IL_000e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0068: Unknown result type (might be due to invalid IL or missing references)
			//IL_0069: Unknown result type (might be due to invalid IL or missing references)
			//IL_0070: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			if (!Plugin.Enabled)
			{
				return;
			}
			float aIMaxDistance = Plugin.AIMaxDistance;
			Vector3 val = GetPlayerRefPos() - ((RaycastHit)(hit)).point;
			if (!(((Vector3)(val)).sqrMagnitude > aIMaxDistance * aIMaxDistance))
			{
				if (_hitQueue.Count >= 128)
				{
					_hitQueue.Dequeue();
				}
				_hitQueue.Enqueue(new HitData
				{
					bodyPart = bodyPart,
					bulletData = bulletData,
					gameplayHit = hit,
					hitDirection = dir
				});
			}
		}

		public static void DrainPlayerQueue()
		{
			int frameCount = Time.frameCount;
			if (frameCount != _playerBudgetFrame)
			{
				_playerBudgetFrame = frameCount;
				_playerFrameSw.Restart();
			}
			while (_playerFrameSw.Elapsed.TotalMilliseconds < (double)Plugin.PlayerMsPerFrame && _playerQueue.Count > 0)
			{
				ProcessHit(_playerQueue.Dequeue());
			}
		}

		public static bool TryDequeue(out HitData data)
		{
			if (_hitQueue.Count == 0)
			{
				data = default(HitData);
				return false;
			}
			data = _hitQueue.Dequeue();
			return true;
		}

		public static void DrainAiQueue()
		{
			int frameCount = Time.frameCount;
			if (frameCount != _aiBudgetFrame)
			{
				_aiBudgetFrame = frameCount;
				_aiFrameSw.Restart();
			}
			HitData data;
			while (_aiFrameSw.Elapsed.TotalMilliseconds < (double)Plugin.AIMsPerFrame && TryDequeue(out data))
			{
				ProcessHit(data);
			}
		}

		public static void ProcessHit(HitData d)
		{
			//IL_001c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			if (!((Object)(object)d.bodyPart == (Object)null))
			{
				PaintWound(d.bodyPart, d.bulletData, d.gameplayHit, d.hitDirection, d.isPlayer);
			}
		}

		public static void PaintWound(BodyPart bodyPart, BulletData bulletData, RaycastHit gameplayHit, Vector3 hitDirection, bool playerShot)
		{
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_0017: Unknown result type (might be due to invalid IL or missing references)
			if (Plugin.DebugLog)
			{
				DumpBulletData(bulletData, playerShot);
			}
			PaintWoundCore(bodyPart, ((RaycastHit)(gameplayHit)).point, hitDirection, playerShot, allowExitWound: true);
		}

		public static void PaintWoundMelee(BodyPart bodyPart, Vector3 hitPoint, Vector3 hitDirection)
		{
			//IL_0001: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			PaintWoundCore(bodyPart, hitPoint, hitDirection, playerShot: true, allowExitWound: false);
		}

		private static void PaintWoundCore(BodyPart bodyPart, Vector3 hitPoint, Vector3 hitDirection, bool playerShot, bool allowExitWound)
		{
			//IL_0060: Unknown result type (might be due to invalid IL or missing references)
			//IL_0066: Unknown result type (might be due to invalid IL or missing references)
			//IL_006b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0070: Unknown result type (might be due to invalid IL or missing references)
			//IL_0127: Unknown result type (might be due to invalid IL or missing references)
			//IL_010e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0110: Unknown result type (might be due to invalid IL or missing references)
			//IL_0115: Unknown result type (might be due to invalid IL or missing references)
			//IL_011a: Unknown result type (might be due to invalid IL or missing references)
			//IL_011e: Unknown result type (might be due to invalid IL or missing references)
			//IL_012c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0130: Unknown result type (might be due to invalid IL or missing references)
			//IL_0131: Unknown result type (might be due to invalid IL or missing references)
			//IL_0138: Unknown result type (might be due to invalid IL or missing references)
			//IL_013d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0142: Unknown result type (might be due to invalid IL or missing references)
			//IL_0172: Unknown result type (might be due to invalid IL or missing references)
			//IL_0179: Expected O, but got Unknown
			//IL_03b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_03b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_03d0: Unknown result type (might be due to invalid IL or missing references)
			//IL_03d2: Unknown result type (might be due to invalid IL or missing references)
			//IL_0496: Unknown result type (might be due to invalid IL or missing references)
			//IL_049d: Unknown result type (might be due to invalid IL or missing references)
			//IL_04ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_04b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_04bc: Unknown result type (might be due to invalid IL or missing references)
			//IL_04ca: Unknown result type (might be due to invalid IL or missing references)
			//IL_04d6: Unknown result type (might be due to invalid IL or missing references)
			//IL_04db: Unknown result type (might be due to invalid IL or missing references)
			//IL_04e0: Unknown result type (might be due to invalid IL or missing references)
			//IL_059c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0541: Unknown result type (might be due to invalid IL or missing references)
			//IL_0546: Unknown result type (might be due to invalid IL or missing references)
			//IL_0547: Unknown result type (might be due to invalid IL or missing references)
			//IL_054c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0294: Unknown result type (might be due to invalid IL or missing references)
			//IL_029b: Expected O, but got Unknown
			//IL_061b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0622: Expected O, but got Unknown
			//IL_064b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0718: Unknown result type (might be due to invalid IL or missing references)
			//IL_0719: Unknown result type (might be due to invalid IL or missing references)
			//IL_0834: Unknown result type (might be due to invalid IL or missing references)
			//IL_083b: Expected O, but got Unknown
			//IL_078d: Unknown result type (might be due to invalid IL or missing references)
			//IL_078e: Unknown result type (might be due to invalid IL or missing references)
			//IL_073e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0745: Expected O, but got Unknown
			//IL_07c6: Unknown result type (might be due to invalid IL or missing references)
			//IL_07cd: Expected O, but got Unknown
			if (!Plugin.Enabled)
			{
				return;
			}
			Transform root = ((Component)bodyPart).transform.root;
			if ((Object)(object)root == (Object)null)
			{
				return;
			}
			int instanceID = ((Object)root).GetInstanceID();
			int frameCount = Time.frameCount;
			float unscaledTime = Time.unscaledTime;
			if (frameCount != _bakeSweepFrame)
			{
				_bakeSweepFrame = frameCount;
				PurgeStaleBakes(unscaledTime);
			}
			Vector3 val;
			int num;
			if (playerShot)
			{
				Camera main = Camera.main;
				if ((Object)(object)main != (Object)null)
				{
					val = ((Component)main).transform.position - root.position;
					if (((Vector3)(val)).sqrMagnitude > Plugin.MaxDistance * Plugin.MaxDistance)
					{
						return;
					}
				}
				num = Plugin.PlayerCap;
			}
			else
			{
				num = Plugin.AICap;
			}
			_decalCount.TryGetValue(instanceID, out var value);
			if (value >= num)
			{
				return;
			}
			if (!_soldierSmrCache.TryGetValue(instanceID, out var value2) || value2 == null)
			{
				value2 = ((Component)root).GetComponentsInChildren<SkinnedMeshRenderer>(true);
				_soldierSmrCache[instanceID] = value2;
			}
			if (value2 == null || value2.Length == 0)
			{
				if (_warnedNoSmr.Add(instanceID))
				{
					Plugin.Log.LogWarning((object)"[BLOOD] No SkinnedMeshRenderer under soldier root");
				}
				return;
			}
			Vector3 normalized;
			if (!(((Vector3)(hitDirection)).sqrMagnitude > 0.0001f))
			{
				val = hitPoint - root.position;
				normalized = ((Vector3)(val)).normalized;
			}
			else
			{
				normalized = ((Vector3)(hitDirection)).normalized;
			}
			Vector3 val2 = normalized;
			Ray probeRay = default(Ray);
			probeRay = new Ray(hitPoint - val2 * 0.5f, val2);
			EnsureProbe();
			bool flag = default(bool);
			if (Plugin.DebugLog && !_inventoryLogged)
			{
				_inventoryLogged = true;
				ManualLogSource log = Plugin.Log;
				BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(45, 2, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD] === SMR inventory for '");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(((Object)root).name);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("' (count=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(value2.Length);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(") ===");
				}
				log.LogInfo(val3);
				SkinnedMeshRenderer[] array = value2;
				foreach (SkinnedMeshRenderer val4 in array)
				{
					if (!((Object)(object)val4 == (Object)null))
					{
						Il2CppReferenceArray<Material> sharedMaterials = ((Renderer)val4).sharedMaterials;
						string text = "";
						for (int j = 0; j < ((Il2CppArrayBase<Material>)(object)sharedMaterials).Length; j++)
						{
							text = text + ((j > 0) ? "," : "") + (((Object)(object)((Il2CppArrayBase<Material>)(object)sharedMaterials)[j] != (Object)null) ? ((Object)((Il2CppArrayBase<Material>)(object)sharedMaterials)[j]).name : "null");
						}
						string text2 = (((Object)(object)val4.sharedMesh != (Object)null) ? ((Object)val4.sharedMesh).name : "null");
						int num2 = (((Object)(object)val4.sharedMesh != (Object)null) ? val4.sharedMesh.subMeshCount : 0);
						ManualLogSource log2 = Plugin.Log;
						val3 = new BepInExInfoLogInterpolatedStringHandler(70, 8, out flag);
						if (flag)
						{
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD]   '");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(((Object)val4).name);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("' enabled=");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<bool>(((Renderer)val4).enabled);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" active=");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<bool>(((Component)val4).gameObject.activeInHierarchy);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" ");
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("forceOff=");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<bool>(((Renderer)val4).forceRenderingOff);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" visible=");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<bool>(((Renderer)val4).isVisible);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" mesh='");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(text2);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("' subs=");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(num2);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" mats=[");
							((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(text);
							((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("]");
						}
						log2.LogInfo(val3);
					}
				}
			}
			Stopwatch stopwatch = (Plugin.DebugLog ? Stopwatch.StartNew() : null);
			RayHitInfo best = default(RayHitInfo);
			if (!RaycastBest(value2, useLod1: true, skipFlesh: false, probeRay, hitPoint, 0.36f, 0.09f, unscaledTime, ref best) && !RaycastBest(value2, useLod1: false, skipFlesh: false, probeRay, hitPoint, 0.36f, 0.09f, unscaledTime, ref best))
			{
				if (Plugin.DebugLog)
				{
					Plugin.Log.LogWarning((object)"[BLOOD] Software raycast found no hit");
				}
				return;
			}
			double num3 = (Plugin.DebugLog ? stopwatch.Elapsed.TotalMilliseconds : 0.0);
			SkinnedMeshRenderer smr = best.smr;
			MeshTopo topo = GetTopo(((Object)smr.sharedMesh).GetInstanceID(), null);
			int num4 = topo.tris[best.firstIndex];
			int num5 = topo.tris[best.firstIndex + 1];
			int num6 = topo.tris[best.firstIndex + 2];
			float num7 = 1f - best.u - best.v;
			Vector2 val5 = topo.uvs[num4] * num7 + topo.uvs[num5] * best.u + topo.uvs[num6] * best.v;
			int num8 = SubmeshForFirstIndex(topo, best.firstIndex);
			string text3 = null;
			float num9 = 0f;
			if (Plugin.DebugLog)
			{
				Il2CppReferenceArray<Material> sharedMaterials2 = ((Renderer)smr).sharedMaterials;
				text3 = ((num8 < ((Il2CppArrayBase<Material>)(object)sharedMaterials2).Length && (Object)(object)((Il2CppArrayBase<Material>)(object)sharedMaterials2)[num8] != (Object)null) ? ((Object)((Il2CppArrayBase<Material>)(object)sharedMaterials2)[num8]).name : "?");
				val = best.worldPoint - hitPoint;
				num9 = ((Vector3)(val)).magnitude;
			}
			if (Plugin.DebugLog)
			{
				stopwatch.Restart();
			}
			WoundTarget woundTarget = EnsureWoundTarget(root, instanceID, smr, num8, value2, playerShot);
			if (woundTarget == null)
			{
				return;
			}
			int brushIndex = Random.Range(0, _brushes.Length);
			float num10 = Random.Range(0.9f, 1.3f);
			SplatBrush(woundTarget, val5, Plugin.WoundSize, brushIndex, num10);
			_decalCount[instanceID] = value + 1;
			_soldierLastUsed[instanceID] = ++_useTick;
			// v1.0.1 修复：玩家自己的身体也必须受 LRU 保护——玩家**挨打**走的是
			// AI 命中路径（playerShot=false），原版只在玩家开枪命中别人时置位，
			// 导致战斗中贴图预算满时玩家身体（含 FPS 手）被淘汰销毁 → 贴图被
			// Destroy 而渲染器仍引用 → 单位变紫。
			if (playerShot || RootHasPlayer(root))
			{
				_soldierHasPlayer[instanceID] = true;
			}
			double num11 = (Plugin.DebugLog ? stopwatch.Elapsed.TotalMilliseconds : 0.0);
			if (Plugin.DebugLog)
			{
				ManualLogSource log3 = Plugin.Log;
				BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(76, 8, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD] HIT plr=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<bool>(playerShot);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" uv=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<Vector2>(val5);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" submesh=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(num8);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" alignErr=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<float>(num9, "F3");
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("m ");
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("smr='");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(((Object)smr).name);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("' mat='");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(text3);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("' | raycastMs=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<double>(num3, "F1");
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" paintMs=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<double>(num11, "F1");
				}
				log3.LogInfo(val3);
			}
			if (!allowExitWound || !Plugin.ExitDecalsEnabled)
			{
				return;
			}
			if (playerShot)
			{
				if (Random.value < Plugin.PlayerExitChance)
				{
					TryPaintExitWound(bodyPart, hitPoint, val2, root, instanceID, value2, unscaledTime, playerShot: true, num10);
				}
				else if (Plugin.DebugLog)
				{
					ManualLogSource log4 = Plugin.Log;
					BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(62, 1, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD] Exit pass skipped: chance roll failed plr=true chance=");
						((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<float>(Plugin.PlayerExitChance);
					}
					log4.LogInfo(val3);
				}
			}
			else if (Random.value < Plugin.AIExitChance)
			{
				_aiExitCount.TryGetValue(instanceID, out var value3);
				if (value3 < 10)
				{
					if (TryPaintExitWound(bodyPart, hitPoint, val2, root, instanceID, value2, unscaledTime, playerShot: false, num10))
					{
						_aiExitCount[instanceID] = value3 + 1;
					}
				}
				else if (Plugin.DebugLog)
				{
					ManualLogSource log5 = Plugin.Log;
					BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(49, 3, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD] AI exit pass skipped: soldier ");
						((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(instanceID);
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" at cap (");
						((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(value3);
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("/");
						((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(10);
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(")");
					}
					log5.LogInfo(val3);
				}
			}
			else if (Plugin.DebugLog)
			{
				ManualLogSource log6 = Plugin.Log;
				BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(56, 1, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD] AI exit pass skipped: chance roll failed chance=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<float>(Plugin.AIExitChance);
				}
				log6.LogInfo(val3);
			}
		}

		private static bool RaycastBest(SkinnedMeshRenderer[] smrs, bool useLod1, bool skipFlesh, Ray probeRay, Vector3 refPoint, float triPrefilterSq, float earlyOutSq, float now, ref RayHitInfo best)
		{
			//IL_0002: Unknown result type (might be due to invalid IL or missing references)
			//IL_0007: Unknown result type (might be due to invalid IL or missing references)
			//IL_0068: Unknown result type (might be due to invalid IL or missing references)
			//IL_006d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_028a: Unknown result type (might be due to invalid IL or missing references)
			//IL_028f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0294: Unknown result type (might be due to invalid IL or missing references)
			//IL_029a: Unknown result type (might be due to invalid IL or missing references)
			//IL_029f: Unknown result type (might be due to invalid IL or missing references)
			//IL_02a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_02a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_02aa: Unknown result type (might be due to invalid IL or missing references)
			//IL_02af: Unknown result type (might be due to invalid IL or missing references)
			//IL_0228: Unknown result type (might be due to invalid IL or missing references)
			//IL_022d: Unknown result type (might be due to invalid IL or missing references)
			//IL_03c9: Unknown result type (might be due to invalid IL or missing references)
			//IL_03ce: Unknown result type (might be due to invalid IL or missing references)
			//IL_03d0: Unknown result type (might be due to invalid IL or missing references)
			//IL_03d5: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_01f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_02f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_02f6: Unknown result type (might be due to invalid IL or missing references)
			//IL_02f8: Unknown result type (might be due to invalid IL or missing references)
			//IL_02fd: Unknown result type (might be due to invalid IL or missing references)
			//IL_030d: Unknown result type (might be due to invalid IL or missing references)
			//IL_030f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0315: Unknown result type (might be due to invalid IL or missing references)
			//IL_031e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0327: Unknown result type (might be due to invalid IL or missing references)
			//IL_033b: Unknown result type (might be due to invalid IL or missing references)
			//IL_033d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0341: Unknown result type (might be due to invalid IL or missing references)
			//IL_0346: Unknown result type (might be due to invalid IL or missing references)
			//IL_034b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0350: Unknown result type (might be due to invalid IL or missing references)
			//IL_0352: Unknown result type (might be due to invalid IL or missing references)
			//IL_0356: Unknown result type (might be due to invalid IL or missing references)
			//IL_035b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0360: Unknown result type (might be due to invalid IL or missing references)
			//IL_03a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_03aa: Unknown result type (might be due to invalid IL or missing references)
			Vector3 direction = ((Ray)(probeRay)).direction;
			_cands.Clear();
			float enter = default(float);
			foreach (SkinnedMeshRenderer val in smrs)
			{
				if ((Object)(object)val == (Object)null || (Object)(object)val.sharedMesh == (Object)null)
				{
					continue;
				}
				SmrFlags smrFlags = GetSmrFlags(val);
				if (!smrFlags.impostor && useLod1 == smrFlags.isLod && (!skipFlesh || !smrFlags.flesh))
				{
					Bounds bounds = ((Renderer)val).bounds;
					if (((Bounds)(bounds)).IntersectRay(probeRay, out enter))
					{
						_cands.Add(new RayCand
						{
							smr = val,
							enter = enter,
							flesh = smrFlags.flesh
						});
					}
				}
			}
			_cands.Sort(_candCmp);
			bool flag = false;
			float num = float.MaxValue;
			foreach (RayCand cand in _cands)
			{
				if ((flag && cand.flesh) || (flag && cand.enter >= num))
				{
					break;
				}
				SkinnedMeshRenderer smr = cand.smr;
				int instanceID = ((Object)smr).GetInstanceID();
				int instanceID2 = ((Object)smr.sharedMesh).GetInstanceID();
				MeshTopo value;
				bool flag2 = _topoCache.TryGetValue(instanceID2, out value);
				BakeEntry value2 = null;
				bool flag3 = flag2 && _bakeCache.TryGetValue(instanceID, out value2) && value2.meshId == instanceID2;
				Vector3[] verts;
				int num2;
				if (flag3 && now - value2.time <= 0.1f)
				{
					verts = value2.verts;
					num2 = verts.Length;
				}
				else
				{
					smr.BakeMesh(_probeMesh);
					if (!flag2)
					{
						value = GetTopo(instanceID2, _probeMesh);
					}
					Il2CppStructArray<Vector3> vertices = _probeMesh.vertices;
					num2 = ((Il2CppArrayBase<Vector3>)(object)vertices).Length;
					if (flag3 && value2.verts.Length == num2)
					{
						for (int j = 0; j < num2; j++)
						{
							value2.verts[j] = ((Il2CppArrayBase<Vector3>)(object)vertices)[j];
						}
						value2.time = now;
					}
					else
					{
						Vector3[] array = (Vector3[])(object)new Vector3[num2];
						for (int k = 0; k < num2; k++)
						{
							array[k] = ((Il2CppArrayBase<Vector3>)(object)vertices)[k];
						}
						value2 = new BakeEntry
						{
							verts = array,
							time = now,
							meshId = instanceID2
						};
						_bakeCache[instanceID] = value2;
					}
					verts = value2.verts;
				}
				int[] tris = value.tris;
				Transform transform = ((Component)smr).transform;
				Vector3 val2 = transform.InverseTransformPoint(((Ray)(probeRay)).origin);
				Vector3 val3 = transform.InverseTransformVector(((Ray)(probeRay)).direction);
				Vector3 val4 = transform.InverseTransformPoint(refPoint);
				Vector3 val5;
				for (int l = 0; l + 2 < tris.Length; l += 3)
				{
					int num3 = tris[l];
					int num4 = tris[l + 1];
					int num5 = tris[l + 2];
					if (num3 >= num2 || num4 >= num2 || num5 >= num2)
					{
						continue;
					}
					val5 = verts[num3] - val4;
					if (!(((Vector3)(val5)).sqrMagnitude > triPrefilterSq) && RayTriangle(val2, val3, verts[num3], verts[num4], verts[num5], out var t, out var u, out var v))
					{
						Vector3 val6 = transform.TransformPoint(val2 + val3 * t);
						float num6 = Vector3.Dot(val6 - ((Ray)(probeRay)).origin, direction);
						if (num6 > 0.002f && num6 < 1f && num6 < num)
						{
							num = num6;
							best.smr = smr;
							best.firstIndex = l;
							best.u = u;
							best.v = v;
							best.worldPoint = val6;
							flag = true;
						}
					}
				}
				if (flag)
				{
					val5 = best.worldPoint - refPoint;
					if (((Vector3)(val5)).sqrMagnitude < earlyOutSq)
					{
						break;
					}
				}
			}
			return flag;
		}

		private static bool TryPaintExitWound(BodyPart bodyPart, Vector3 hitPoint, Vector3 dir, Transform root, int rootId, SkinnedMeshRenderer[] smrs, float now, bool playerShot, float entryScale)
		{
			//IL_0331: Unknown result type (might be due to invalid IL or missing references)
			//IL_0338: Expected O, but got Unknown
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_002e: Unknown result type (might be due to invalid IL or missing references)
			//IL_002f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0030: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_003f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0044: Unknown result type (might be due to invalid IL or missing references)
			//IL_0045: Unknown result type (might be due to invalid IL or missing references)
			//IL_0046: Unknown result type (might be due to invalid IL or missing references)
			//IL_004c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Unknown result type (might be due to invalid IL or missing references)
			//IL_0059: Unknown result type (might be due to invalid IL or missing references)
			//IL_005a: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_0073: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_008f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0134: Unknown result type (might be due to invalid IL or missing references)
			//IL_013b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0149: Unknown result type (might be due to invalid IL or missing references)
			//IL_0155: Unknown result type (might be due to invalid IL or missing references)
			//IL_015a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0168: Unknown result type (might be due to invalid IL or missing references)
			//IL_0174: Unknown result type (might be due to invalid IL or missing references)
			//IL_0179: Unknown result type (might be due to invalid IL or missing references)
			//IL_017e: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fc: Unknown result type (might be due to invalid IL or missing references)
			//IL_0259: Unknown result type (might be due to invalid IL or missing references)
			//IL_0260: Expected O, but got Unknown
			//IL_0315: Unknown result type (might be due to invalid IL or missing references)
			//IL_031a: Unknown result type (might be due to invalid IL or missing references)
			//IL_028a: Unknown result type (might be due to invalid IL or missing references)
			Collider component = ((Component)bodyPart).GetComponent<Collider>();
			if ((Object)(object)component == (Object)null)
			{
				if (Plugin.DebugLog)
				{
					Plugin.Log.LogInfo((object)"[BLOOD] Exit skipped: bodyPart has no Collider");
				}
				return false;
			}
			Bounds bounds = component.bounds;
			Vector3 val = RayAabbExit(hitPoint, dir, ((Bounds)(bounds)).min, ((Bounds)(bounds)).max);
			Vector3 val2 = val + dir * 0.05f;
			Ray probeRay = default(Ray);
			probeRay = new Ray(val2, -dir);
			RayHitInfo best = default(RayHitInfo);
			if (!RaycastBest(smrs, useLod1: true, skipFlesh: false, probeRay, val, 1f, 0.25f, now, ref best) && !RaycastBest(smrs, useLod1: false, skipFlesh: true, probeRay, val, 1f, 0.25f, now, ref best))
			{
				if (Plugin.DebugLog)
				{
					Plugin.Log.LogInfo((object)"[BLOOD] Exit raycast found no hit - skipping exit decal");
				}
				return false;
			}
			SkinnedMeshRenderer smr = best.smr;
			MeshTopo topo = GetTopo(((Object)smr.sharedMesh).GetInstanceID(), null);
			int num = topo.tris[best.firstIndex];
			int num2 = topo.tris[best.firstIndex + 1];
			int num3 = topo.tris[best.firstIndex + 2];
			float num4 = 1f - best.u - best.v;
			Vector2 val3 = topo.uvs[num] * num4 + topo.uvs[num2] * best.u + topo.uvs[num3] * best.v;
			int num5 = SubmeshForFirstIndex(topo, best.firstIndex);
			WoundTarget woundTarget = EnsureWoundTarget(root, rootId, smr, num5, smrs, playerShot);
			if (woundTarget == null)
			{
				if (Plugin.DebugLog)
				{
					Plugin.Log.LogInfo((object)"[BLOOD] Exit skipped: EnsureWoundTarget returned null");
				}
				return false;
			}
			int brushIndex = _exitBrushIndices[Random.Range(0, _exitBrushIndices.Length)];
			float num6 = Mathf.Max(1.4f, entryScale);
			float num7 = Mathf.Max(1.8f, num6);
			float num8 = Random.Range(num6, num7);
			SplatBrush(woundTarget, val3, Plugin.WoundSize, brushIndex, num8);
			_decalCount.TryGetValue(rootId, out var value);
			_decalCount[rootId] = value + 1;
			_soldierLastUsed[rootId] = ++_useTick;
			bool flag = default(bool);
			if (Plugin.DebugLog)
			{
				ManualLogSource log = Plugin.Log;
				BepInExInfoLogInterpolatedStringHandler val4 = new BepInExInfoLogInterpolatedStringHandler(60, 6, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("[BLOOD] EXIT plr=");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<bool>(playerShot);
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" uv=");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<Vector2>(val3);
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" submesh=");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<int>(num5);
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" smr='");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<string>(((Object)smr).name);
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("' ");
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("entryScale=");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<float>(entryScale, "F2");
					((BepInExLogInterpolatedStringHandler)val4).AppendLiteral(" exitScale=");
					((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<float>(num8, "F2");
				}
				log.LogInfo(val4);
			}
			if (BloodPainter.OnExitWound != null)
			{
				try
				{
					BloodPainter.OnExitWound(best.worldPoint, dir, root, playerShot);
				}
				catch (Exception ex)
				{
					ManualLogSource log2 = Plugin.Log;
					BepInExErrorLogInterpolatedStringHandler val5 = new BepInExErrorLogInterpolatedStringHandler(38, 1, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("[BLOOD] OnExitWound subscriber threw: ");
						((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<Exception>(ex);
					}
					log2.LogError(val5);
				}
			}
			return true;
		}

		private static Vector3 RayAabbExit(Vector3 origin, Vector3 dir, Vector3 min, Vector3 max)
		{
			//IL_0006: Unknown result type (might be due to invalid IL or missing references)
			//IL_0052: Unknown result type (might be due to invalid IL or missing references)
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_009e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_002d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b0: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			//IL_0071: Unknown result type (might be due to invalid IL or missing references)
			//IL_0033: Unknown result type (might be due to invalid IL or missing references)
			//IL_003a: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fb: Unknown result type (might be due to invalid IL or missing references)
			//IL_0100: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00bd: Unknown result type (might be due to invalid IL or missing references)
			//IL_007f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0086: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cb: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d2: Unknown result type (might be due to invalid IL or missing references)
			float num = float.MaxValue;
			if (Mathf.Abs(dir.x) > 1E-08f)
			{
				float num2 = (((dir.x > 0f) ? max.x : min.x) - origin.x) / dir.x;
				if (num2 > 0f)
				{
					num = Mathf.Min(num, num2);
				}
			}
			if (Mathf.Abs(dir.y) > 1E-08f)
			{
				float num3 = (((dir.y > 0f) ? max.y : min.y) - origin.y) / dir.y;
				if (num3 > 0f)
				{
					num = Mathf.Min(num, num3);
				}
			}
			if (Mathf.Abs(dir.z) > 1E-08f)
			{
				float num4 = (((dir.z > 0f) ? max.z : min.z) - origin.z) / dir.z;
				if (num4 > 0f)
				{
					num = Mathf.Min(num, num4);
				}
			}
			if (num == float.MaxValue)
			{
				num = 0.3f;
			}
			return origin + dir * num;
		}

		private static void EnsureProbe()
		{
			//IL_000d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0012: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Expected O, but got Unknown
			//IL_0064: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Expected O, but got Unknown
			if ((Object)(object)_probeMesh == (Object)null)
			{
				_probeMesh = new Mesh
				{
					hideFlags = (HideFlags)61
				};
				Object.DontDestroyOnLoad((Object)(object)_probeMesh);
			}
			if ((Object)(object)_scratchTex == (Object)null)
			{
				int num = _scratchBuckets.Length;
				_scratchTexBuckets = (Texture2D[])(object)new Texture2D[num];
				_scratchPxBuckets = new Color32[num][];
				for (int i = 0; i < num; i++)
				{
					int num2 = _scratchBuckets[i];
					Texture2D val = new Texture2D(num2, num2, (TextureFormat)4, false);
					((Object)val).hideFlags = (HideFlags)61;
					Object.DontDestroyOnLoad((Object)(object)val);
					_scratchTexBuckets[i] = val;
					_scratchPxBuckets[i] = (Color32[])(object)new Color32[num2 * num2];
				}
				_scratchTex = _scratchTexBuckets[num - 1];
			}
		}

		private static void PurgeStaleBakes(float now)
		{
			_bakeSweepScratch.Clear();
			foreach (KeyValuePair<int, BakeEntry> item in _bakeCache)
			{
				if (now - item.Value.time > 5f)
				{
					_bakeSweepScratch.Add(item.Key);
				}
			}
			for (int i = 0; i < _bakeSweepScratch.Count; i++)
			{
				_bakeCache.Remove(_bakeSweepScratch[i]);
			}
		}

		private static bool IsFleshMesh(SkinnedMeshRenderer s)
		{
			string text = ((Object)s).name.ToLower();
			if (text.Contains("hand") || text.Contains("face") || text.Contains("head"))
			{
				return true;
			}
			Il2CppReferenceArray<Material> sharedMaterials = ((Renderer)s).sharedMaterials;
			for (int i = 0; i < ((Il2CppArrayBase<Material>)(object)sharedMaterials).Length; i++)
			{
				Material val = ((Il2CppArrayBase<Material>)(object)sharedMaterials)[i];
				if ((Object)(object)val != (Object)null && (((Object)val).name.IndexOf("metahuman", StringComparison.OrdinalIgnoreCase) >= 0 || ((Object)val).name.IndexOf("skin", StringComparison.OrdinalIgnoreCase) >= 0))
				{
					return true;
				}
			}
			Mesh sharedMesh = s.sharedMesh;
			if ((Object)(object)sharedMesh != (Object)null)
			{
				string text2 = ((Object)sharedMesh).name.ToLower();
				if (text2.Contains("caucasic") || text2.Contains("fullbody") || text2.Contains("metahuman"))
				{
					return true;
				}
			}
			return false;
		}

		private static SmrFlags GetSmrFlags(SkinnedMeshRenderer s)
		{
			int instanceID = ((Object)s).GetInstanceID();
			if (_smrFlags.TryGetValue(instanceID, out var value))
			{
				return value;
			}
			string text = ((Object)s).name.ToLower();
			value.isLod = text.Contains("_lod");
			value.impostor = text.Contains("_lod2") || ((Object)(object)s.sharedMesh != (Object)null && ((Object)s.sharedMesh).name.ToLower().Contains("far_soldier"));
			value.flesh = IsFleshMesh(s);
			_smrFlags[instanceID] = value;
			return value;
		}

		private static MeshTopo GetTopo(int sharedId, Mesh baked)
		{
			//IL_0062: Unknown result type (might be due to invalid IL or missing references)
			//IL_0067: Unknown result type (might be due to invalid IL or missing references)
			//IL_009c: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a1: Unknown result type (might be due to invalid IL or missing references)
			if (_topoCache.TryGetValue(sharedId, out var value))
			{
				return value;
			}
			Il2CppStructArray<int> triangles = baked.triangles;
			int[] array = new int[((Il2CppArrayBase<int>)(object)triangles).Length];
			for (int i = 0; i < array.Length; i++)
			{
				array[i] = ((Il2CppArrayBase<int>)(object)triangles)[i];
			}
			Il2CppStructArray<Vector2> uv = baked.uv;
			Vector2[] array2 = (Vector2[])(object)new Vector2[((Il2CppArrayBase<Vector2>)(object)uv).Length];
			for (int j = 0; j < array2.Length; j++)
			{
				array2[j] = ((Il2CppArrayBase<Vector2>)(object)uv)[j];
			}
			int subMeshCount = baked.subMeshCount;
			int[] array3 = new int[subMeshCount];
			int[] array4 = new int[subMeshCount];
			for (int k = 0; k < subMeshCount; k++)
			{
				SubMeshDescriptor subMesh = baked.GetSubMesh(k);
				array3[k] = ((SubMeshDescriptor)(subMesh)).indexStart;
				array4[k] = ((SubMeshDescriptor)(subMesh)).indexCount;
			}
			value = new MeshTopo
			{
				tris = array,
				uvs = array2,
				subStart = array3,
				subLen = array4
			};
			_topoCache[sharedId] = value;
			return value;
		}

		private static int SubmeshForFirstIndex(MeshTopo topo, int firstIndex)
		{
			for (int i = 0; i < topo.subStart.Length; i++)
			{
				if (firstIndex >= topo.subStart[i] && firstIndex < topo.subStart[i] + topo.subLen[i])
				{
					return i;
				}
			}
			return 0;
		}

		private static bool RayTriangle(Vector3 o, Vector3 d, Vector3 v0, Vector3 v1, Vector3 v2, out float t, out float u, out float v)
		{
			//IL_0018: Unknown result type (might be due to invalid IL or missing references)
			//IL_0019: Unknown result type (might be due to invalid IL or missing references)
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Unknown result type (might be due to invalid IL or missing references)
			//IL_0022: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Unknown result type (might be due to invalid IL or missing references)
			//IL_0028: Unknown result type (might be due to invalid IL or missing references)
			//IL_0029: Unknown result type (might be due to invalid IL or missing references)
			//IL_002a: Unknown result type (might be due to invalid IL or missing references)
			//IL_002b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0030: Unknown result type (might be due to invalid IL or missing references)
			//IL_0031: Unknown result type (might be due to invalid IL or missing references)
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_0054: Unknown result type (might be due to invalid IL or missing references)
			//IL_0055: Unknown result type (might be due to invalid IL or missing references)
			//IL_0056: Unknown result type (might be due to invalid IL or missing references)
			//IL_005b: Unknown result type (might be due to invalid IL or missing references)
			//IL_005f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0061: Unknown result type (might be due to invalid IL or missing references)
			//IL_0081: Unknown result type (might be due to invalid IL or missing references)
			//IL_0083: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Unknown result type (might be due to invalid IL or missing references)
			//IL_0089: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_008e: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b5: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
			t = (u = (v = 0f));
			Vector3 val = v1 - v0;
			Vector3 val2 = v2 - v0;
			Vector3 val3 = Vector3.Cross(d, val2);
			float num = Vector3.Dot(val, val3);
			if (num > -1E-08f && num < 1E-08f)
			{
				return false;
			}
			float num2 = 1f / num;
			Vector3 val4 = o - v0;
			u = Vector3.Dot(val4, val3) * num2;
			if (u < -0.0001f || u > 1.0001f)
			{
				return false;
			}
			Vector3 val5 = Vector3.Cross(val4, val);
			v = Vector3.Dot(d, val5) * num2;
			if (v < -0.0001f || u + v > 1.0001f)
			{
				return false;
			}
			t = Vector3.Dot(val2, val5) * num2;
			return t > 1E-05f;
		}

		private static Color32[] DownscaleHalf(Color32[] src, int srcW, int srcH, int dstW, int dstH)
		{
			//IL_0059: Unknown result type (might be due to invalid IL or missing references)
			//IL_005e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0065: Unknown result type (might be due to invalid IL or missing references)
			//IL_006a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0072: Unknown result type (might be due to invalid IL or missing references)
			//IL_0077: Unknown result type (might be due to invalid IL or missing references)
			//IL_007f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0084: Unknown result type (might be due to invalid IL or missing references)
			//IL_008d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0094: Unknown result type (might be due to invalid IL or missing references)
			//IL_009c: Unknown result type (might be due to invalid IL or missing references)
			//IL_00a4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00af: Unknown result type (might be due to invalid IL or missing references)
			//IL_00b6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00be: Unknown result type (might be due to invalid IL or missing references)
			//IL_00c6: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d1: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e0: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e8: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f3: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_0102: Unknown result type (might be due to invalid IL or missing references)
			//IL_010a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0115: Unknown result type (might be due to invalid IL or missing references)
			//IL_011a: Unknown result type (might be due to invalid IL or missing references)
			Color32[] array = (Color32[])(object)new Color32[dstW * dstH];
			for (int i = 0; i < dstH; i++)
			{
				int num = Math.Min(i * 2, srcH - 1);
				int num2 = Math.Min(num + 1, srcH - 1);
				int num3 = num * srcW;
				int num4 = num2 * srcW;
				for (int j = 0; j < dstW; j++)
				{
					int num5 = Math.Min(j * 2, srcW - 1);
					int num6 = Math.Min(num5 + 1, srcW - 1);
					Color32 val = src[num3 + num5];
					Color32 val2 = src[num3 + num6];
					Color32 val3 = src[num4 + num5];
					Color32 val4 = src[num4 + num6];
					array[i * dstW + j] = new Color32((byte)(val.r + val2.r + val3.r + val4.r >> 2), (byte)(val.g + val2.g + val3.g + val4.g >> 2), (byte)(val.b + val2.b + val3.b + val4.b >> 2), (byte)(val.a + val2.a + val3.a + val4.a >> 2));
				}
			}
			return array;
		}

		private static void DumpBulletData(BulletData bd, bool playerShot)
		{
			//IL_0032: Unknown result type (might be due to invalid IL or missing references)
			//IL_0038: Expected O, but got Unknown
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_0159: Unknown result type (might be due to invalid IL or missing references)
			if (bd == null)
			{
				Plugin.Log.LogInfo((object)("[BLOOD] BulletData plr=" + playerShot + " <null>"));
				return;
			}
			ManualLogSource log = Plugin.Log;
			bool flag = default(bool);
			BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(239, 22, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] BulletData plr=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(playerShot);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" caliber=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(bd.caliber);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" shellType=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<ShellType>(bd.shellType);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" mass_grains=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(bd.mass_grains);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("speed=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(bd.speed);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" actualSpeed=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(bd.ActualSpeed);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" mmPen=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(bd.mmPenetration);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" penDmg=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(bd.penetrationDamage);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("expDmg=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(bd.explosionDamage);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" expRadius=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(bd.explosion_radius);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" firedShells=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(bd.firedShells);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" behaviour=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<BulletBehaviour>(bd.behaviour);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" | ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("isHighCal=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(bd.IsHighCaliber());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" isVeryHighCal=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(bd.IsVeryHighCaliber());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" isShotgun=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(bd.IsShotgun());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("isAp=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(bd.IsAp());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" isHe=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(bd.IsHe());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" isFire=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(bd.IsFire());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" isBullet=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(bd.IsAPullet());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" | ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("massDesc='");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(bd.GetMassDesc());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("' penDesc='");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(bd.GetPenetrationDesc());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("' behDesc='");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(bd.BehaviourDesc());
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("'");
			}
			log.LogInfo(val);
		}

		private static void SplatBrush(WoundTarget t, Vector2 uv, float brushFrac, int brushIndex, float sizeScale = 1f)
		{
			//IL_0048: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b2: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b7: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b9: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d2: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d7: Unknown result type (might be due to invalid IL or missing references)
			//IL_01d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_0202: Unknown result type (might be due to invalid IL or missing references)
			//IL_0210: Unknown result type (might be due to invalid IL or missing references)
			//IL_022a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0238: Unknown result type (might be due to invalid IL or missing references)
			//IL_0252: Unknown result type (might be due to invalid IL or missing references)
			//IL_0260: Unknown result type (might be due to invalid IL or missing references)
			//IL_0288: Unknown result type (might be due to invalid IL or missing references)
			//IL_028a: Unknown result type (might be due to invalid IL or missing references)
			if (_brushes == null || _brushes[brushIndex].px == null)
			{
				return;
			}
			Brush brush = _brushes[brushIndex];
			Color32[] px = brush.px;
			int w = brush.w;
			int h = brush.h;
			int w2 = t.w;
			int h2 = t.h;
			float num = uv.x * (float)w2;
			float num2 = uv.y * (float)h2;
			int num3 = Mathf.Max(4, Mathf.RoundToInt(brushFrac * sizeScale * (float)w2)) / 2;
			float num4 = ((brushIndex == 4) ? 0f : Random.Range(0f, (float)Math.PI * 2f));
			float num5 = Mathf.Cos(num4);
			float num6 = Mathf.Sin(num4);
			bool flag = Random.value > 0.5f;
			float num7 = Random.Range(0.72f, 1f);
			Color32[] pixels = t.pixels;
			int num8 = Mathf.RoundToInt(num);
			int num9 = Mathf.RoundToInt(num2);
			for (int i = -num3; i <= num3; i++)
			{
				int num10 = num9 + i;
				if (num10 < 0 || num10 >= h2)
				{
					continue;
				}
				for (int j = -num3; j <= num3; j++)
				{
					int num11 = num8 + j;
					if (num11 < 0 || num11 >= w2)
					{
						continue;
					}
					float num12 = (float)j * num5 - (float)i * num6;
					float num13 = (float)j * num6 + (float)i * num5;
					float num14 = num12 / (float)num3 * 0.5f + 0.5f;
					float num15 = num13 / (float)num3 * 0.5f + 0.5f;
					if (num14 < 0f || num14 > 1f || num15 < 0f || num15 > 1f)
					{
						continue;
					}
					if (flag)
					{
						num14 = 1f - num14;
					}
					int num16 = (int)(num14 * (float)(w - 1));
					int num17 = (int)(num15 * (float)(h - 1));
					Color32 val = px[num17 * w + num16];
					if (val.a != 0)
					{
						int num18 = num10 * w2 + num11;
						Color32 val2 = pixels[num18];
						float num19 = Mathf.Clamp01((float)(int)val.a / 255f * 1.15f);
						if (!(num19 <= 0.003f))
						{
							val2.r = (byte)((float)(int)val.r * num7 * num19 + (float)(int)val2.r * (1f - num19));
							val2.g = (byte)((float)(int)val.g * num7 * num19 + (float)(int)val2.g * (1f - num19));
							val2.b = (byte)((float)(int)val.b * num7 * num19 + (float)(int)val2.b * (1f - num19));
							val2.a = byte.MaxValue;
							pixels[num18] = val2;
						}
					}
				}
			}
			int num20 = Mathf.Max(0, num8 - num3);
			int num21 = Mathf.Max(0, num9 - num3);
			int num22 = Mathf.Min(w2, num8 + num3 + 1);
			int num23 = Mathf.Min(h2, num9 + num3 + 1);
			if (!t.dirty)
			{
				t.dirty = true;
				t.dirtyX0 = num20;
				t.dirtyY0 = num21;
				t.dirtyX1 = num22;
				t.dirtyY1 = num23;
				_dirtyTargets.Add(t);
				return;
			}
			if (num20 < t.dirtyX0)
			{
				t.dirtyX0 = num20;
			}
			if (num21 < t.dirtyY0)
			{
				t.dirtyY0 = num21;
			}
			if (num22 > t.dirtyX1)
			{
				t.dirtyX1 = num22;
			}
			if (num23 > t.dirtyY1)
			{
				t.dirtyY1 = num23;
			}
		}

		private static int PickScratchBucket(int sw, int sh)
		{
			int num = ((sw > sh) ? sw : sh);
			for (int i = 0; i < _scratchBuckets.Length; i++)
			{
				if (num <= _scratchBuckets[i])
				{
					return i;
				}
			}
			return -1;
		}

		public static void FlushDirtyTargets()
		{
			//IL_00cf: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d4: Unknown result type (might be due to invalid IL or missing references)
			if (_dirtyTargets.Count == 0)
			{
				return;
			}
			EnsureProbe();
			foreach (WoundTarget dirtyTarget in _dirtyTargets)
			{
				dirtyTarget.dirty = false;
				if ((Object)(object)dirtyTarget.tex == (Object)null)
				{
					continue;
				}
				int num = dirtyTarget.dirtyX1 - dirtyTarget.dirtyX0;
				int num2 = dirtyTarget.dirtyY1 - dirtyTarget.dirtyY0;
				if (num <= 0 || num2 <= 0)
				{
					continue;
				}
				int num3 = PickScratchBucket(num, num2);
				if (num3 >= 0)
				{
					int num4 = _scratchBuckets[num3];
					Texture2D val = _scratchTexBuckets[num3];
					Color32[] array = _scratchPxBuckets[num3];
					for (int i = 0; i < num2; i++)
					{
						for (int j = 0; j < num; j++)
						{
							array[i * num4 + j] = dirtyTarget.pixels[(dirtyTarget.dirtyY0 + i) * dirtyTarget.w + (dirtyTarget.dirtyX0 + j)];
						}
					}
					val.SetPixels32(array);
					val.Apply(false, false);
					Graphics.CopyTexture((Texture)(object)val, 0, 0, 0, 0, num, num2, (Texture)(object)dirtyTarget.tex, 0, 0, dirtyTarget.dirtyX0, dirtyTarget.dirtyY0);
				}
				else
				{
					dirtyTarget.tex.SetPixels32(dirtyTarget.pixels);
					dirtyTarget.tex.Apply(false, false);
				}
			}
			_dirtyTargets.Clear();
		}

		public static void FlushPendingDestroys()
		{
			if (_pendingDestroy.Count == 0)
			{
				return;
			}
			for (int i = 0; i < _pendingDestroy.Count; i++)
			{
				if (_pendingDestroy[i] != (Object)null)
				{
					Object.Destroy(_pendingDestroy[i]);
				}
			}
			_pendingDestroy.Clear();
		}

		private static Texture GetAlbedoTex(Material mat)
		{
			if (mat.HasProperty("_BaseMap"))
			{
				Texture texture = mat.GetTexture("_BaseMap");
				if ((Object)(object)texture != (Object)null)
				{
					return texture;
				}
			}
			if (mat.HasProperty("_MainTex"))
			{
				Texture texture2 = mat.GetTexture("_MainTex");
				if ((Object)(object)texture2 != (Object)null)
				{
					return texture2;
				}
			}
			return mat.mainTexture;
		}

		public static void CacheMaterial(Material mat, string normName)
		{
			//IL_003e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d4: Expected O, but got Unknown
			//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ab: Unknown result type (might be due to invalid IL or missing references)
			//IL_01db: Unknown result type (might be due to invalid IL or missing references)
			//IL_01dd: Unknown result type (might be due to invalid IL or missing references)
			//IL_01fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ff: Unknown result type (might be due to invalid IL or missing references)
			//IL_0244: Unknown result type (might be due to invalid IL or missing references)
			//IL_024b: Expected O, but got Unknown
			//IL_025d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0282: Unknown result type (might be due to invalid IL or missing references)
			//IL_0137: Unknown result type (might be due to invalid IL or missing references)
			//IL_013c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0140: Unknown result type (might be due to invalid IL or missing references)
			//IL_014c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0158: Unknown result type (might be due to invalid IL or missing references)
			//IL_0361: Unknown result type (might be due to invalid IL or missing references)
			//IL_0368: Expected O, but got Unknown
			//IL_03d7: Unknown result type (might be due to invalid IL or missing references)
			//IL_03f1: Unknown result type (might be due to invalid IL or missing references)
			//IL_040b: Unknown result type (might be due to invalid IL or missing references)
			if ((Object)(object)mat == (Object)null || _albedoCache.ContainsKey(normName))
			{
				return;
			}
			Texture albedoTex = GetAlbedoTex(mat);
			if ((Object)(object)albedoTex == (Object)null)
			{
				_albedoCache[normName] = new AlbedoSeed
				{
					avgColor = new Color32((byte)80, (byte)75, (byte)60, byte.MaxValue)
				};
				return;
			}
			int width = albedoTex.width;
			int height = albedoTex.height;
			int num = width;
			int num2 = height;
			int woundMaxRes = Plugin.WoundMaxRes;
			if (woundMaxRes > 0 && (num > woundMaxRes || num2 > woundMaxRes))
			{
				float num3 = Mathf.Min((float)woundMaxRes / (float)num, (float)woundMaxRes / (float)num2);
				num = Mathf.Max(1, Mathf.RoundToInt((float)num * num3));
				num2 = Mathf.Max(1, Mathf.RoundToInt((float)num2 * num3));
			}
			RenderTexture temporary = RenderTexture.GetTemporary(num, num2, 0, (RenderTextureFormat)0, (RenderTextureReadWrite)2);
			Graphics.Blit(albedoTex, temporary);
			RenderTexture active = RenderTexture.active;
			RenderTexture.active = temporary;
			Texture2D val = new Texture2D(num, num2, (TextureFormat)4, false);
			val.ReadPixels(new Rect(0f, 0f, (float)num, (float)num2), 0, 0, false);
			val.Apply(false, false);
			RenderTexture.active = active;
			RenderTexture.ReleaseTemporary(temporary);
			Color32[] array = val.GetPixels32();
			int num4 = 0;
			int num5 = 0;
			int num6 = 0;
			int num7 = 0;
			for (int i = num2 / 4; i <= 3 * num2 / 4; i += num2 / 4)
			{
				for (int j = num / 4; j <= 3 * num / 4; j += num / 4)
				{
					Color32 val2 = array[i * num + j];
					num4 += val2.r;
					num5 += val2.g;
					num6 += val2.b;
					num7++;
				}
			}
			Color32 val3 = new Color32((byte)(num4 / num7), (byte)(num5 / num7), (byte)(num6 / num7), byte.MaxValue);
			((Texture)val).wrapMode = albedoTex.wrapMode;
			((Texture)val).filterMode = (FilterMode)1;
			((Object)val).hideFlags = (HideFlags)61;
			Object.DontDestroyOnLoad((Object)(object)val);
			AlbedoSeed albedoSeed = new AlbedoSeed
			{
				gpuTex = val,
				avgColor = val3,
				w = num,
				h = num2,
				pixels = array,
				wrapMode = albedoTex.wrapMode
			};
			if (num >= 1024)
			{
				int num8 = num / 2;
				int num9 = num2 / 2;
				RenderTexture temporary2 = RenderTexture.GetTemporary(num8, num9, 0, (RenderTextureFormat)0, (RenderTextureReadWrite)2);
				Graphics.Blit((Texture)(object)val, temporary2);
				RenderTexture active2 = RenderTexture.active;
				RenderTexture.active = temporary2;
				Texture2D val4 = new Texture2D(num8, num9, (TextureFormat)4, false);
				val4.ReadPixels(new Rect(0f, 0f, (float)num8, (float)num9), 0, 0, false);
				val4.Apply(false, false);
				RenderTexture.active = active2;
				RenderTexture.ReleaseTemporary(temporary2);
				((Texture)val4).wrapMode = albedoTex.wrapMode;
				((Texture)val4).filterMode = (FilterMode)1;
				((Object)val4).hideFlags = (HideFlags)61;
				Object.DontDestroyOnLoad((Object)(object)val4);
				albedoSeed.halfGpuTex = val4;
				albedoSeed.halfW = num8;
				albedoSeed.halfH = num9;
				albedoSeed.halfPixels = DownscaleHalf(array, num, num2, num8, num9);
			}
			string name = ((Object)albedoTex).name;
			name = (albedoSeed.srcTexKey = (string.IsNullOrEmpty(name) ? null : name.ToLowerInvariant()));
			albedoSeed.srcTexId = ((Object)albedoTex).GetInstanceID();
			if (name != null)
			{
				if (!_seedsByTexName.TryGetValue(name, out var value))
				{
					value = (_seedsByTexName[name] = new List<string>());
				}
				if (!value.Contains(normName))
				{
					value.Add(normName);
				}
			}
			_albedoCache[normName] = albedoSeed;
			if (Plugin.DebugLog)
			{
				ManualLogSource log = Plugin.Log;
				bool flag = default(bool);
				BepInExInfoLogInterpolatedStringHandler val5 = new BepInExInfoLogInterpolatedStringHandler(37, 7, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("[BLOOD] Cached '");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<string>(normName);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("' (");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<int>(num);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("x");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<int>(num2);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral(") tex='");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<string>(name ?? "<none>");
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("' avg=(");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<byte>(val3.r);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral(",");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<byte>(val3.g);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral(",");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<byte>(val3.b);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral(")");
				}
				log.LogInfo(val5);
			}
		}

		public static int InvalidateAlbedoSeedsForTexture(string textureName)
		{
			//IL_0051: Unknown result type (might be due to invalid IL or missing references)
			//IL_0058: Expected O, but got Unknown
			try
			{
				if (string.IsNullOrEmpty(textureName))
				{
					return 0;
				}
				string text = textureName.ToLowerInvariant();
				if (!_seedsByTexName.TryGetValue(text, out var value) || value.Count == 0)
				{
					return 0;
				}
				_pendingSeedInvalidations.Add(text);
				return value.Count;
			}
			catch (Exception ex)
			{
				ManualLogSource log = Plugin.Log;
				bool flag = default(bool);
				BepInExWarningLogInterpolatedStringHandler val = new BepInExWarningLogInterpolatedStringHandler(52, 2, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] InvalidateAlbedoSeedsForTexture('");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(textureName);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("') failed: ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(ex.Message);
				}
				log.LogWarning(val);
				return 0;
			}
		}

		internal static void FlushSeedInvalidations()
		{
			//IL_0212: Unknown result type (might be due to invalid IL or missing references)
			//IL_0219: Expected O, but got Unknown
			if (_pendingSeedInvalidations.Count == 0)
			{
				return;
			}
			_invalMatScratch.Clear();
			foreach (string pendingSeedInvalidation in _pendingSeedInvalidations)
			{
				if (!_seedsByTexName.TryGetValue(pendingSeedInvalidation, out var value))
				{
					continue;
				}
				foreach (string item in value)
				{
					if (_albedoCache.TryGetValue(item, out var value2) && value2.srcTexKey == pendingSeedInvalidation)
					{
						_invalMatScratch.Add(item);
					}
				}
				_seedsByTexName.Remove(pendingSeedInvalidation);
			}
			_pendingSeedInvalidations.Clear();
			if (_invalMatScratch.Count == 0)
			{
				return;
			}
			_invalRootScratch.Clear();
			foreach (KeyValuePair<TargetKey, WoundTarget> target in _targets)
			{
				if (_invalMatScratch.Contains(target.Key.baseName))
				{
					_invalRootScratch.Add(target.Value.rootId);
				}
			}
			foreach (int item2 in _invalRootScratch)
			{
				EvictSoldier(item2);
			}
			foreach (string item3 in _invalMatScratch)
			{
				if (_albedoCache.TryGetValue(item3, out var value3))
				{
					if ((Object)(object)value3.gpuTex != (Object)null)
					{
						_pendingDestroy.Add((Object)(object)value3.gpuTex);
					}
					if ((Object)(object)value3.halfGpuTex != (Object)null)
					{
						_pendingDestroy.Add((Object)(object)value3.halfGpuTex);
					}
					value3.pixels = null;
					value3.halfPixels = null;
					_albedoCache.Remove(item3);
				}
			}
			ManualLogSource log = Plugin.Log;
			bool flag = default(bool);
			BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(94, 2, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] Invalidated ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(_invalMatScratch.Count);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" albedo seed(s) after an external ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("texture replacement; evicted ");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(_invalRootScratch.Count);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" soldier(s)");
			}
			log.LogInfo(val);
			_invalMatScratch.Clear();
			_invalRootScratch.Clear();
		}

		public static void CacheSoldier(Transform root)
		{
			if ((Object)(object)root == (Object)null)
			{
				return;
			}
			EnsureProbe();
			foreach (SkinnedMeshRenderer componentsInChild in ((Component)root).GetComponentsInChildren<SkinnedMeshRenderer>(true))
			{
				if ((Object)(object)componentsInChild == (Object)null)
				{
					continue;
				}
				foreach (Material item in (Il2CppArrayBase<Material>)(object)((Renderer)componentsInChild).sharedMaterials)
				{
					if (!((Object)(object)item == (Object)null))
					{
						string text = NormName(((Object)item).name);
						if (!_albedoCache.ContainsKey(text))
						{
							CacheMaterial(item, text);
						}
					}
				}
				if (!((Object)(object)componentsInChild.sharedMesh == (Object)null))
				{
					GetSmrFlags(componentsInChild);
					int instanceID = ((Object)componentsInChild.sharedMesh).GetInstanceID();
					if (!_topoCache.ContainsKey(instanceID))
					{
						componentsInChild.BakeMesh(_probeMesh);
						GetTopo(instanceID, _probeMesh);
					}
				}
			}
		}

		private static string NormName(string n)
		{
			if (string.IsNullOrEmpty(n))
			{
				return n;
			}
			if (n.EndsWith(" (Instance)"))
			{
				n = n.Substring(0, n.Length - 11);
			}
			if (n.EndsWith("_BLOOD"))
			{
				n = n.Substring(0, n.Length - 6);
			}
			return n;
		}

		private static WoundTarget EnsureWoundTarget(Transform root, int rootId, SkinnedMeshRenderer hitSmr, int submesh, SkinnedMeshRenderer[] smrs, bool playerShot)
		{
			//IL_013b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0142: Expected O, but got Unknown
			//IL_01ee: Unknown result type (might be due to invalid IL or missing references)
			//IL_0253: Unknown result type (might be due to invalid IL or missing references)
			//IL_0266: Unknown result type (might be due to invalid IL or missing references)
			//IL_026b: Unknown result type (might be due to invalid IL or missing references)
			//IL_027c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0286: Expected O, but got Unknown
			//IL_024a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0266: Unknown result type (might be due to invalid IL or missing references)
			//IL_026b: Unknown result type (might be due to invalid IL or missing references)
			//IL_027c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0286: Expected O, but got Unknown
			//IL_04c7: Unknown result type (might be due to invalid IL or missing references)
			//IL_04ce: Expected O, but got Unknown
			//IL_0426: Unknown result type (might be due to invalid IL or missing references)
			//IL_041d: Unknown result type (might be due to invalid IL or missing references)
			Il2CppReferenceArray<Material> sharedMaterials = ((Renderer)hitSmr).sharedMaterials;
			if (submesh >= ((Il2CppArrayBase<Material>)(object)sharedMaterials).Length)
			{
				submesh = 0;
			}
			Material val = ((Il2CppArrayBase<Material>)(object)sharedMaterials)[submesh];
			if ((Object)(object)val == (Object)null)
			{
				if (!_warnedNoMaterial)
				{
					_warnedNoMaterial = true;
					Plugin.Log.LogWarning((object)"[BLOOD] Hit submesh has no material; suppressing further warnings");
				}
				return null;
			}
			string text = NormName(((Object)val).name);
			TargetKey key = new TargetKey(rootId, text);
			if (_targets.TryGetValue(key, out var value) && (Object)(object)value.tex != (Object)null)
			{
				return value;
			}
			while (_targets.Count >= Plugin.GlobalMaxWoundTextures && EvictLruSoldier(rootId))
			{
			}
			if (_targets.Count >= Plugin.GlobalMaxWoundTextures)
			{
				return null;
			}
			if (!_albedoCache.TryGetValue(text, out var value2))
			{
				CacheMaterial(val, text);
				_albedoCache.TryGetValue(text, out value2);
			}
			int num = ((value2 != null && value2.w > 0) ? value2.w : 1024);
			int num2 = ((value2 != null && value2.h > 0) ? value2.h : 1024);
			bool flag = !playerShot && num >= 1024;
			int num3 = (flag ? (num / 2) : num);
			int num4 = (flag ? (num2 / 2) : num2);
			Texture2D val2 = new Texture2D(num3, num4, (TextureFormat)4, false);
			((Object)val2).hideFlags = (HideFlags)61;
			if (flag && (Object)(object)value2?.halfGpuTex != (Object)null && value2.halfW == num3 && value2.halfH == num4)
			{
				Graphics.CopyTexture((Texture)(object)value2.halfGpuTex, 0, 0, 0, 0, num3, num4, (Texture)(object)val2, 0, 0, 0, 0);
			}
			else if ((Object)(object)value2?.gpuTex != (Object)null)
			{
				if (flag)
				{
					RenderTexture temporary = RenderTexture.GetTemporary(num3, num4, 0, (RenderTextureFormat)0, (RenderTextureReadWrite)2);
					Graphics.Blit((Texture)(object)value2.gpuTex, temporary);
					RenderTexture active = RenderTexture.active;
					RenderTexture.active = temporary;
					val2.ReadPixels(new Rect(0f, 0f, (float)num3, (float)num4), 0, 0, false);
					val2.Apply(false, false);
					RenderTexture.active = active;
					RenderTexture.ReleaseTemporary(temporary);
				}
				else
				{
					Graphics.CopyTexture((Texture)(object)value2.gpuTex, 0, 0, 0, 0, num3, num4, (Texture)(object)val2, 0, 0, 0, 0);
				}
			}
			else
			{
				val2.Apply(false, false);
			}
			int num5;
			if (value2 != null)
			{
				num5 = (int)value2.wrapMode;
			}
			else
			{
				Texture albedoTex = GetAlbedoTex(val);
				num5 = ((albedoTex != null) ? ((int)albedoTex.wrapMode) : 0);
			}
			((Texture)val2).wrapMode = (TextureWrapMode)num5;
			((Texture)val2).filterMode = (FilterMode)1;
			Material val3 = new Material(val)
			{
				name = text + "_BLOOD",
				hideFlags = (HideFlags)61
			};
			if (val3.HasProperty("_BaseMap"))
			{
				val3.SetTexture("_BaseMap", (Texture)(object)val2);
			}
			if (val3.HasProperty("_MainTex"))
			{
				val3.SetTexture("_MainTex", (Texture)(object)val2);
			}
			val3.mainTexture = (Texture)(object)val2;
			int num6 = 0;
			foreach (SkinnedMeshRenderer val4 in smrs)
			{
				if ((Object)(object)val4 == (Object)null)
				{
					continue;
				}
				Il2CppReferenceArray<Material> sharedMaterials2 = ((Renderer)val4).sharedMaterials;
				bool flag2 = false;
				for (int j = 0; j < ((Il2CppArrayBase<Material>)(object)sharedMaterials2).Length; j++)
				{
					if ((Object)(object)((Il2CppArrayBase<Material>)(object)sharedMaterials2)[j] != (Object)null && NormName(((Object)((Il2CppArrayBase<Material>)(object)sharedMaterials2)[j]).name) == text)
					{
						((Il2CppArrayBase<Material>)(object)sharedMaterials2)[j] = val3;
						flag2 = true;
						num6++;
					}
				}
				if (flag2)
				{
					((Renderer)val4).sharedMaterials = sharedMaterials2;
				}
			}
			Color32[] array;
			if (value2?.pixels != null && !flag)
			{
				array = (Color32[])value2.pixels.Clone();
			}
			else if (flag && value2?.halfPixels != null && value2.halfW == num3 && value2.halfH == num4)
			{
				array = (Color32[])value2.halfPixels.Clone();
			}
			else if (value2?.pixels != null && flag)
			{
				array = DownscaleHalf(value2.pixels, num, num2, num3, num4);
			}
			else
			{
				array = (Color32[])(object)new Color32[num3 * num4];
				Array.Fill(array, (value2 != null) ? value2.avgColor : new Color32((byte)80, (byte)75, (byte)60, byte.MaxValue));
			}
			WoundTarget woundTarget = new WoundTarget
			{
				key = key,
				tex = val2,
				pixels = array,
				w = num3,
				h = num4,
				original = val,
				clone = val3,
				root = root,
				rootId = rootId
			};
			_targets[key] = woundTarget;
			if (!_soldierTargets.TryGetValue(rootId, out var value3))
			{
				value3 = (_soldierTargets[rootId] = new List<WoundTarget>());
			}
			value3.Add(woundTarget);
			if (Plugin.DebugLog)
			{
				ManualLogSource log = Plugin.Log;
				bool flag3 = default(bool);
				BepInExInfoLogInterpolatedStringHandler val5 = new BepInExInfoLogInterpolatedStringHandler(68, 5, out flag3);
				if (flag3)
				{
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("[BLOOD] Created wound target ");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<int>(num3);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("x");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<int>(num4);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral(" from '");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<string>(text);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral("' (propagated to ");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<int>(num6);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral(" slots, live=");
					((BepInExLogInterpolatedStringHandler)val5).AppendFormatted<int>(_targets.Count);
					((BepInExLogInterpolatedStringHandler)val5).AppendLiteral(")");
				}
				log.LogInfo(val5);
			}
			return woundTarget;
		}

		/// <summary>v1.0.1：目标根节点是否包含玩家控制的士兵（玩家身体纳入 LRU 保护）。</summary>
		private static bool RootHasPlayer(Transform root)
		{
			try
			{
				if (root == null)
				{
					return false;
				}
				Soldier[] sols = root.GetComponentsInChildren<Soldier>(true);
				for (int i = 0; i < sols.Length; i++)
				{
					if (sols[i] != null && sols[i].IsPlayer())
					{
						return true;
					}
				}
			}
			catch
			{
			}
			return false;
		}

		/// <summary>v1.0.1：把目标士兵所有 SMR（缓存 + 现场重扫，防缓存过期漏掉
		/// 后创建的渲染器，如 FPS 手/制服 LOD 重建）上引用克隆材质的槽还原为
		/// 原始材质——必须在销毁克隆材质/贴图前调用，否则渲染器引用已销毁对象
		/// → 单位贴图消失变紫。</summary>
		private static void RestoreWoundMaterials(List<WoundTarget> targets)
		{
			try
			{
				if (targets == null || targets.Count == 0)
				{
					return;
				}
				Dictionary<Material, Material> dictionary = new Dictionary<Material, Material>();
				foreach (WoundTarget item in targets)
				{
					if ((Object)(object)item.clone != (Object)null && (Object)(object)item.original != (Object)null)
					{
						dictionary[item.clone] = item.original;
					}
				}
				if (dictionary.Count == 0)
				{
					return;
				}
				Transform root = targets[0].root;
				int rootId = targets[0].rootId;
				List<SkinnedMeshRenderer> list = new List<SkinnedMeshRenderer>();
				if (_soldierSmrCache.TryGetValue(rootId, out var cached) && cached != null)
				{
					foreach (SkinnedMeshRenderer r in cached)
					{
						if (r != null)
						{
							list.Add(r);
						}
					}
				}
				if ((Object)(object)root != (Object)null)
				{
					SkinnedMeshRenderer[] fresh = root.GetComponentsInChildren<SkinnedMeshRenderer>(true);
					foreach (SkinnedMeshRenderer r in fresh)
					{
						if (r == null)
						{
							continue;
						}
						bool dup = false;
						foreach (SkinnedMeshRenderer e in list)
						{
							if (e == r)
							{
								dup = true;
								break;
							}
						}
						if (!dup)
						{
							list.Add(r);
						}
					}
				}
				foreach (SkinnedMeshRenderer val in list)
				{
					if ((Object)(object)val == (Object)null)
					{
						continue;
					}
					Il2CppReferenceArray<Material> sharedMaterials = ((Renderer)val).sharedMaterials;
					bool changed = false;
					for (int j = 0; j < ((Il2CppArrayBase<Material>)(object)sharedMaterials).Length; j++)
					{
						if ((Object)(object)((Il2CppArrayBase<Material>)(object)sharedMaterials)[j] != (Object)null && dictionary.TryGetValue(((Il2CppArrayBase<Material>)(object)sharedMaterials)[j], out var original))
						{
							((Il2CppArrayBase<Material>)(object)sharedMaterials)[j] = original;
							changed = true;
						}
					}
					if (changed)
					{
						((Renderer)val).sharedMaterials = sharedMaterials;
					}
				}
			}
			catch
			{
			}
		}

		private static bool EvictLruSoldier(int exceptRootId)
		{
			int num = 0;
			long num2 = long.MaxValue;
			bool flag = false;
			foreach (KeyValuePair<int, long> item in _soldierLastUsed)
			{
				if (item.Key != exceptRootId)
				{
					bool value;
					bool flag2 = _soldierHasPlayer.TryGetValue(item.Key, out value) && value;
					if (!flag && !flag2)
					{
						flag = true;
						num2 = item.Value;
						num = item.Key;
					}
					else if (flag == !flag2 && item.Value < num2)
					{
						num2 = item.Value;
						num = item.Key;
					}
				}
			}
			if (num == 0)
			{
				return false;
			}
			EvictSoldier(num);
			return true;
		}

		private static void EvictSoldier(int rootId)
		{
			//IL_029e: Unknown result type (might be due to invalid IL or missing references)
			//IL_02a5: Expected O, but got Unknown
			_soldierTargets.TryGetValue(rootId, out var value);
			SkinnedMeshRenderer[] value2;
			SkinnedMeshRenderer[] array = (_soldierSmrCache.TryGetValue(rootId, out value2) ? value2 : null);
			int num = 0;
			if (value != null && value.Count > 0)
			{
				// v1.0.1：先全面还原（缓存 + 现场重扫）再销毁——否则渲染器引用
				// 已销毁的克隆材质/贴图 → 单位贴图消失变紫
				RestoreWoundMaterials(value);
				foreach (WoundTarget item2 in value)
				{
					if ((Object)(object)item2.clone != (Object)null)
					{
						_pendingDestroy.Add((Object)(object)item2.clone);
					}
					if ((Object)(object)item2.tex != (Object)null)
					{
						_pendingDestroy.Add((Object)(object)item2.tex);
					}
					item2.pixels = null;
					_targets.Remove(item2.key);
					_dirtyTargets.Remove(item2);
					num++;
				}
				value.Clear();
			}
			_soldierTargets.Remove(rootId);
			if (array != null)
			{
				SkinnedMeshRenderer[] array3 = array;
				foreach (SkinnedMeshRenderer val2 in array3)
				{
					if ((Object)(object)val2 != (Object)null)
					{
						_bakeCache.Remove(((Object)val2).GetInstanceID());
					}
				}
			}
			_decalCount.Remove(rootId);
			_aiExitCount.Remove(rootId);
			_soldierLastUsed.Remove(rootId);
			_soldierHasPlayer.Remove(rootId);
			_soldierSmrCache.Remove(rootId);
			if (Plugin.DebugLog)
			{
				ManualLogSource log = Plugin.Log;
				bool flag2 = default(bool);
				BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(49, 3, out flag2);
				if (flag2)
				{
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD] Evicted soldier ");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(rootId);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" (");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(num);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" textures freed, live=");
					((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(_targets.Count);
					((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(")");
				}
				log.LogInfo(val3);
			}
		}

		public static void OnSoldierDestroyed(int rootId)
		{
			if (_soldierLastUsed.ContainsKey(rootId))
			{
				EvictSoldier(rootId);
			}
		}

		public static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
		{
			//IL_0000: Unknown result type (might be due to invalid IL or missing references)
			//IL_0002: Invalid comparison between Unknown and I4
			//IL_007d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0083: Expected O, but got Unknown
			//IL_0015: Unknown result type (might be due to invalid IL or missing references)
			//IL_001b: Expected O, but got Unknown
			bool flag = default(bool);
			if ((int)mode == 1)
			{
				if (Plugin.DebugLog)
				{
					ManualLogSource log = Plugin.Log;
					BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(56, 2, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] Additive scene load (");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(((Scene)(scene)).name);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral(") - keeping ");
						((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(_targets.Count);
						((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" wound textures");
					}
					log.LogInfo(val);
				}
			}
			else
			{
				int count = _targets.Count;
				ClearAllWounds();
				ManualLogSource log2 = Plugin.Log;
				BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(49, 2, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] Scene loaded (");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(((Scene)(scene)).name);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(") - cleared ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(count);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" wound textures");
				}
				log2.LogInfo(val);
			}
		}

		private static void ClearAllWounds()
		{
			FlushPendingDestroys();
			// v1.0.1：销毁前先按根节点全面还原（现场重扫），防止切场景时仍有
			// 存活渲染器引用克隆材质/贴图 → 单位贴图消失变紫
			foreach (KeyValuePair<int, List<WoundTarget>> kv in _soldierTargets)
			{
				RestoreWoundMaterials(kv.Value);
			}
			foreach (WoundTarget value in _targets.Values)
			{
				if ((Object)(object)value.clone != (Object)null)
				{
					Object.Destroy((Object)(object)value.clone);
				}
				if ((Object)(object)value.tex != (Object)null)
				{
					Object.Destroy((Object)(object)value.tex);
				}
				value.pixels = null;
			}
			_targets.Clear();
			_dirtyTargets.Clear();
			_soldierTargets.Clear();
			_decalCount.Clear();
			_aiExitCount.Clear();
			_soldierLastUsed.Clear();
			_soldierHasPlayer.Clear();
			_soldierSmrCache.Clear();
			_smrFlags.Clear();
			_hitQueue.Clear();
			_playerQueue.Clear();
			_bakeCache.Clear();
			_bakeSweepFrame = -1;
		}
	}
	[HarmonyPatch(typeof(BulletInstance), "OnHit")]
	public static class BulletOnHitPatch
	{
		private static bool _loggedPostfixError;

		private static void Postfix(BulletInstance __instance, Vector3 hitDirection, float impactSpeed, BulletData bulletData, RaycastHit hit)
		{
			//IL_00d7: Unknown result type (might be due to invalid IL or missing references)
			//IL_00d9: Unknown result type (might be due to invalid IL or missing references)
			//IL_00fa: Unknown result type (might be due to invalid IL or missing references)
			//IL_0101: Expected O, but got Unknown
			//IL_0077: Unknown result type (might be due to invalid IL or missing references)
			//IL_0079: Unknown result type (might be due to invalid IL or missing references)
			try
			{
				Collider collider = ((RaycastHit)(hit)).collider;
				if ((Object)(object)collider == (Object)null)
				{
					return;
				}
				BodyPart val = ((Component)collider).GetComponent<BodyPart>();
				if ((Object)(object)val == (Object)null)
				{
					val = ((Component)collider).GetComponentInParent<BodyPart>();
				}
				if ((Object)(object)val == (Object)null)
				{
					return;
				}
				Soldier shooter = __instance.shooter;
				bool flag = false;
				try
				{
					if ((Object)(object)shooter != (Object)null)
					{
						flag = ResourcesManager.ShooterIsPlayer(shooter);
					}
				}
				catch
				{
				}
				if (flag)
				{
					if ((Object)(object)shooter != (Object)null)
					{
						BloodPainter._playerRef = ((Component)shooter).transform.root;
					}
					BloodPainter.HandlePlayerHit(val, bulletData, hit, hitDirection);
					return;
				}
				if (Plugin.BlockFriendlyAIWounds && (Object)(object)shooter != (Object)null)
				{
					try
					{
						Soldier component = ((Component)((Component)val).transform.root).GetComponent<Soldier>();
						if ((Object)(object)component != (Object)null && !string.IsNullOrEmpty(shooter.faction) && shooter.faction == component.faction)
						{
							return;
						}
					}
					catch
					{
					}
				}
				BloodPainter.EnqueueHit(val, bulletData, hit, hitDirection);
			}
			catch (Exception ex)
			{
				if (!_loggedPostfixError)
				{
					_loggedPostfixError = true;
					ManualLogSource log = Plugin.Log;
					bool flag2 = default(bool);
					BepInExErrorLogInterpolatedStringHandler val2 = new BepInExErrorLogInterpolatedStringHandler(49, 1, out flag2);
					if (flag2)
					{
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("OnHit postfix error; suppressing further errors. ");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<Exception>(ex);
					}
					log.LogError(val2);
				}
			}
		}
	}
	[HarmonyPatch(typeof(Soldier), "Start")]
	public static class SoldierStartPatch
	{
		private static void Postfix(Soldier __instance)
		{
			//IL_001f: Unknown result type (might be due to invalid IL or missing references)
			//IL_0025: Expected O, but got Unknown
			Transform root = ((Component)__instance).transform.root;
			try
			{
				BloodPainter.CacheSoldier(root);
			}
			catch (Exception ex)
			{
				ManualLogSource log = Plugin.Log;
				bool flag = default(bool);
				BepInExWarningLogInterpolatedStringHandler val = new BepInExWarningLogInterpolatedStringHandler(33, 1, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] Soldier.Start cache err: ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(ex.Message);
				}
				log.LogWarning(val);
			}
		}
	}
	public static class SoldierLodManagerPatch
	{
		public static void Postfix(SoldierLodManager __instance)
		{
			//IL_001d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0023: Expected O, but got Unknown
			try
			{
				BloodPainter.CacheSoldier(((Component)__instance).transform.root);
			}
			catch (Exception ex)
			{
				ManualLogSource log = Plugin.Log;
				bool flag = default(bool);
				BepInExWarningLogInterpolatedStringHandler val = new BepInExWarningLogInterpolatedStringHandler(37, 1, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] SoldierLodManager patch err: ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(ex.Message);
				}
				log.LogWarning(val);
			}
		}
	}
	[HarmonyPatch(typeof(Soldier), "OnDestroy")]
	public static class SoldierDestroyPatch
	{
		public static void Postfix(Soldier __instance)
		{
			try
			{
				Transform root = ((Component)__instance).transform.root;
				if ((Object)(object)root != (Object)null)
				{
					BloodPainter.OnSoldierDestroyed(((Object)root).GetInstanceID());
				}
			}
			catch (Exception)
			{
			}
		}
	}
	public class BloodDecalHost : MonoBehaviour
	{
		public BloodDecalHost(IntPtr ptr)
			: base(ptr)
		{
		}

		private void Start()
		{
			((MonoBehaviour)this).StartCoroutine(BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(DrainQueue()));
			SceneManager.sceneLoaded += (UnityAction<Scene, LoadSceneMode>)BloodPainter.OnSceneLoaded;
			Plugin.Log.LogInfo((object)"[BLOOD] BloodDecalHost started");
		}

		private static IEnumerator DrainQueue()
		{
			while (true)
			{
				BloodPainter.FlushSeedInvalidations();
				BloodPainter.DrainPlayerQueue();
				BloodPainter.DrainAiQueue();
				BloodPainter.FlushDirtyTargets();
				BloodPainter.FlushPendingDestroys();
				yield return null;
			}
		}
	}
	[BepInPlugin("ER2_FleshWounds", "ER2 Flesh Wounds", "1.0.1")]
	public class Plugin : BasePlugin
	{
		internal static ManualLogSource Log;

		internal static bool Enabled = true;

		internal static bool BlockFriendlyAIWounds = true;

		internal static float WoundSize = 0.12f;

		internal static bool ExitDecalsEnabled = true;

		internal static float PlayerExitChance = 1f;

		internal static float AIExitChance = 1f;

		internal static float MaxDistance = 500f;

		internal static float AIMaxDistance = 350f;

		internal static int AICap = 96;

		internal static int PlayerCap = 96;

		internal static int GlobalMaxWoundTextures = 1024;

		internal static float PlayerMsPerFrame = 10f;

		internal static float AIMsPerFrame = 4f;

		internal static int WoundMaxRes = 1024;

		internal static bool DebugLog = false;

		internal const int AIQueueSize = 128;

		internal const int PlayerQueueSize = 256;

		internal const float BakeCacheSeconds = 0.1f;

		internal const float EntryScaleMin = 0.9f;

		internal const float EntryScaleMax = 1.3f;

		internal const float ExitScaleMin = 1.4f;

		internal const float ExitScaleMax = 1.8f;

		internal const int AIExitWoundsPerSoldier = 10;

		public override void Load()
		{
			//IL_0030: Unknown result type (might be due to invalid IL or missing references)
			//IL_0035: Unknown result type (might be due to invalid IL or missing references)
			//IL_003d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0043: Expected O, but got Unknown
			//IL_004e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0053: Unknown result type (might be due to invalid IL or missing references)
			//IL_0059: Expected O, but got Unknown
			//IL_005e: Expected O, but got Unknown
			//IL_0068: Unknown result type (might be due to invalid IL or missing references)
			//IL_006e: Expected O, but got Unknown
			Log = ((BasePlugin)this).Log;
			GameVersionCheck.Run(Log, "[BLOOD]");
			LoadConfig();
			LoadBloodTexture();
			ClassInjector.RegisterTypeInIl2Cpp<BloodDecalHost>();
			GameObject val = new GameObject("BloodDecalHost")
			{
				hideFlags = (HideFlags)61
			};
			Object.DontDestroyOnLoad((Object)val);
			val.AddComponent<BloodDecalHost>();
			Harmony val2 = new Harmony("com.easyred2.ER2_FleshWounds");
			ApplyPatches(val2);
			TryPatchSoldierLodManager(val2);
			ManualLogSource log = Log;
			bool flag = default(bool);
			BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(15, 1, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("Plugin ");
				((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>("ER2_FleshWounds");
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(" 1.0.1 loaded!");
			}
			log.LogInfo(val3);
		}

		private static void ApplyPatches(Harmony harmony)
		{
			Patch(harmony, typeof(BulletOnHitPatch));
			Patch(harmony, typeof(SoldierStartPatch));
			Patch(harmony, typeof(SoldierDestroyPatch));
		}

		private static void Patch(Harmony harmony, Type type)
		{
			//IL_001a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0020: Expected O, but got Unknown
			try
			{
				harmony.CreateClassProcessor(type).Patch();
			}
			catch (Exception ex)
			{
				ManualLogSource log = Log;
				bool flag = default(bool);
				BepInExErrorLogInterpolatedStringHandler val = new BepInExErrorLogInterpolatedStringHandler(107, 2, out flag);
				if (flag)
				{
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<string>(type.Name);
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" failed to apply - that feature is disabled ");
					((BepInExLogInterpolatedStringHandler)val).AppendLiteral("this session. Likely a game update renamed its target. ");
					((BepInExLogInterpolatedStringHandler)val).AppendFormatted<Exception>(ex);
				}
				log.LogError(val);
			}
		}

		private static void TryPatchSoldierLodManager(Harmony harmony)
		{
			//IL_0010: Unknown result type (might be due to invalid IL or missing references)
			//IL_0016: Expected O, but got Unknown
			//IL_00fd: Unknown result type (might be due to invalid IL or missing references)
			//IL_0104: Expected O, but got Unknown
			//IL_016a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0171: Expected O, but got Unknown
			//IL_00c4: Unknown result type (might be due to invalid IL or missing references)
			//IL_00cb: Expected O, but got Unknown
			//IL_0074: Unknown result type (might be due to invalid IL or missing references)
			//IL_007b: Expected O, but got Unknown
			HarmonyMethod val = new HarmonyMethod(typeof(SoldierLodManagerPatch), "Postfix", (Type[])null);
			string[] obj = new string[4] { "InitializeBaseBodyMaterials", "ApplyBaseBodyMaterials", "RefreshUniformLod", "RefreshVestLod" };
			int num = 0;
			string[] array = obj;
			bool flag = default(bool);
			BepInExInfoLogInterpolatedStringHandler val3;
			foreach (string text in array)
			{
				try
				{
					MethodInfo methodInfo = AccessTools.Method(typeof(SoldierLodManager), text, (Type[])null, (Type[])null);
					if (methodInfo == null)
					{
						ManualLogSource log = Log;
						BepInExWarningLogInterpolatedStringHandler val2 = new BepInExWarningLogInterpolatedStringHandler(44, 1, out flag);
						if (flag)
						{
							((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("[BLOOD] SoldierLodManager.");
							((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(text);
							((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(" not found in stub");
						}
						log.LogWarning(val2);
						continue;
					}
					harmony.Patch((MethodBase)methodInfo, (HarmonyMethod)null, val, (HarmonyMethod)null, (HarmonyMethod)null, (HarmonyMethod)null);
					ManualLogSource log2 = Log;
					val3 = new BepInExInfoLogInterpolatedStringHandler(34, 1, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD] Patched SoldierLodManager.");
						((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(text);
					}
					log2.LogInfo(val3);
					num++;
				}
				catch (Exception ex)
				{
					ManualLogSource log3 = Log;
					BepInExWarningLogInterpolatedStringHandler val2 = new BepInExWarningLogInterpolatedStringHandler(44, 2, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral("[BLOOD] Could not patch SoldierLodManager.");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(text);
						((BepInExLogInterpolatedStringHandler)val2).AppendLiteral(": ");
						((BepInExLogInterpolatedStringHandler)val2).AppendFormatted<string>(ex.Message);
					}
					log3.LogWarning(val2);
				}
			}
			if (num == 0)
			{
				Log.LogWarning((object)"[BLOOD] No SoldierLodManager methods patched - first-hit caching fallback active");
				return;
			}
			ManualLogSource log4 = Log;
			val3 = new BepInExInfoLogInterpolatedStringHandler(51, 1, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("[BLOOD] SoldierLodManager: ");
				((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(num);
				((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("/4 material hooks active");
			}
			log4.LogInfo(val3);
		}

		private void LoadConfig()
		{
			//IL_007b: Unknown result type (might be due to invalid IL or missing references)
			//IL_0085: Expected O, but got Unknown
			//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00ec: Expected O, but got Unknown
			//IL_0124: Unknown result type (might be due to invalid IL or missing references)
			//IL_012e: Expected O, but got Unknown
			//IL_0166: Unknown result type (might be due to invalid IL or missing references)
			//IL_0170: Expected O, but got Unknown
			//IL_01a8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01b2: Expected O, but got Unknown
			//IL_01e3: Unknown result type (might be due to invalid IL or missing references)
			//IL_01ed: Expected O, but got Unknown
			//IL_021e: Unknown result type (might be due to invalid IL or missing references)
			//IL_0228: Expected O, but got Unknown
			//IL_025d: Unknown result type (might be due to invalid IL or missing references)
			//IL_0267: Expected O, but got Unknown
			//IL_029f: Unknown result type (might be due to invalid IL or missing references)
			//IL_02a9: Expected O, but got Unknown
			//IL_02e1: Unknown result type (might be due to invalid IL or missing references)
			//IL_02eb: Expected O, but got Unknown
			//IL_032a: Unknown result type (might be due to invalid IL or missing references)
			//IL_0334: Expected O, but got Unknown
			//IL_0371: Unknown result type (might be due to invalid IL or missing references)
			//IL_0377: Expected O, but got Unknown
			Enabled = ((BasePlugin)this).Config.Bind<bool>("General", "Enabled", true, "Master switch. Turns off all blood decals.").Value;
			BlockFriendlyAIWounds = !((BasePlugin)this).Config.Bind<bool>("General", "Friendly Fire Blood", false, "Let AI bleed when shot by their own side. Your own shots always draw blood, regardless.").Value;
			WoundSize = ((BasePlugin)this).Config.Bind<float>("Blood", "Wound Size", 0.12f, new ConfigDescription("Size of a wound decal, as a fraction of the body texture's width. Bigger number = bigger blood.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.02f, 0.5f), Array.Empty<object>())).Value;
			ExitDecalsEnabled = ((BasePlugin)this).Config.Bind<bool>("Blood", "Exit Wounds", true, "Paint a second, larger decal where the bullet exits the body, in addition to the entry wound.").Value;
			PlayerExitChance = ((BasePlugin)this).Config.Bind<float>("Blood", "Exit Wound Chance (Your Shots)", 1f, new ConfigDescription("Odds that one of your shots leaves an exit wound. 1.0 = always, 0.0 = never. Requires Exit Wounds.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), Array.Empty<object>())).Value;
			AIExitChance = ((BasePlugin)this).Config.Bind<float>("Blood", "Exit Wound Chance (AI Shots)", 1f, new ConfigDescription("Odds that an AI shot leaves an exit wound. 1.0 = always, 0.0 = never. Requires Exit Wounds.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 1f), Array.Empty<object>())).Value;
			MaxDistance = ((BasePlugin)this).Config.Bind<float>("Limits", "Blood Range (Your Shots)", 500f, new ConfigDescription("How far away (metres from the camera) your own shots still draw blood.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(50f, 1500f), Array.Empty<object>())).Value;
			AIMaxDistance = ((BasePlugin)this).Config.Bind<float>("Limits", "Blood Range (AI Shots)", 350f, new ConfigDescription("How far away (metres from you) AI-on-AI shots still draw blood. Raise for bloodier distant firefights; costs more CPU in large battles.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(50f, 1500f), Array.Empty<object>())).Value;
			PlayerCap = ((BasePlugin)this).Config.Bind<int>("Limits", "Wounds Per Body (Your Shots)", 96, new ConfigDescription("Max decals on one body from your own shots before older ones stop adding up. A through-shot counts as two.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(1, 256), Array.Empty<object>())).Value;
			AICap = ((BasePlugin)this).Config.Bind<int>("Limits", "Wounds Per Body (AI Shots)", 96, new ConfigDescription("Max decals on one body from AI-inflicted shots before older ones stop adding up.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(1, 256), Array.Empty<object>())).Value;
			GlobalMaxWoundTextures = ((BasePlugin)this).Config.Bind<int>("Limits", "Max Blood Textures", 1024, new ConfigDescription("How many bloodied soldiers can exist at once, battlefield-wide. When full, the soldier bled on longest ago loses their blood to make room (whoever you're currently fighting is kept last). Raise for large, long battles; costs more memory.", (AcceptableValueBase)(object)new AcceptableValueRange<int>(64, 4096), Array.Empty<object>())).Value;
			PlayerMsPerFrame = ((BasePlugin)this).Config.Bind<float>("Performance", "Frame Budget (Your Shots)", 10f, new ConfigDescription("Max milliseconds per frame spent painting your own hits. Higher keeps full-auto decals flowing without dropping any; costs more CPU on a bad frame.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(1f, 50f), Array.Empty<object>())).Value;
			AIMsPerFrame = ((BasePlugin)this).Config.Bind<float>("Performance", "Frame Budget (AI Shots)", 4f, new ConfigDescription("Max milliseconds per frame spent painting AI-inflicted hits. Same idea as the shots budget above, tuned lower since AI hits are far more numerous.", (AcceptableValueBase)(object)new AcceptableValueRange<float>(1f, 50f), Array.Empty<object>())).Value;
			WoundMaxRes = ((BasePlugin)this).Config.Bind<int>("Performance", "Wound Texture Resolution", 1024, new ConfigDescription("Pixel resolution of each wound texture. Higher looks crisper up close; costs more VRAM and bigger first-hit stutters.", (AcceptableValueBase)(object)new AcceptableValueList<int>(new int[3] { 512, 1024, 2048 }), Array.Empty<object>())).Value;
			DebugLog = ((BasePlugin)this).Config.Bind<bool>("Debug", "Debug Logging", false, "Log every bullet hit to the BepInEx console. Leave off unless troubleshooting.").Value;
			ManualLogSource log = Log;
			bool flag = default(bool);
			BepInExInfoLogInterpolatedStringHandler val = new BepInExInfoLogInterpolatedStringHandler(170, 15, out flag);
			if (flag)
			{
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("[BLOOD] Config: enabled=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(Enabled);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" blockFriendlyAI=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(BlockFriendlyAIWounds);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("woundSize=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(WoundSize);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" exitEnabled=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(ExitDecalsEnabled);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" playerExitChance=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(PlayerExitChance);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" aiExitChance=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(AIExitChance);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("maxDist=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(MaxDistance);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" aiDist=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(AIMaxDistance);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" player=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(PlayerCap);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ai=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(AICap);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" globalTex=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(GlobalMaxWoundTextures);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" ");
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral("plrMs=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(PlayerMsPerFrame);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" aiMs=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<float>(AIMsPerFrame);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" woundRes=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<int>(WoundMaxRes);
				((BepInExLogInterpolatedStringHandler)val).AppendLiteral(" debugLog=");
				((BepInExLogInterpolatedStringHandler)val).AppendFormatted<bool>(DebugLog);
			}
			log.LogInfo(val);
		}

		private void LoadBloodTexture()
		{
			//IL_01c8: Unknown result type (might be due to invalid IL or missing references)
			//IL_01cf: Expected O, but got Unknown
			//IL_0094: Unknown result type (might be due to invalid IL or missing references)
			//IL_009b: Expected O, but got Unknown
			//IL_00dd: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00e4: Unknown result type (might be due to invalid IL or missing references)
			//IL_015c: Unknown result type (might be due to invalid IL or missing references)
			//IL_0163: Expected O, but got Unknown
			//IL_0103: Unknown result type (might be due to invalid IL or missing references)
			//IL_0108: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f2: Unknown result type (might be due to invalid IL or missing references)
			//IL_00f4: Unknown result type (might be due to invalid IL or missing references)
			string[] array = new string[5] { "blood1", "blood2", "blood3", "blood4", "blood5" };
			BloodPainter._brushes = new BloodPainter.Brush[array.Length];
			bool flag = default(bool);
			for (int i = 0; i < array.Length; i++)
			{
				try
				{
					string path = Path.Combine(Paths.PluginPath, "ER2_FleshWoundsBW", array[i] + ".png");
					if (!File.Exists(path))
					{
						path = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), array[i] + ".png");
					}
					byte[] array2 = File.ReadAllBytes(path);
					Texture2D val = new Texture2D(2, 2);
					ImageConversion.LoadImage(val, array2);
					int width = ((Texture)val).width;
					int height = ((Texture)val).height;
					Color32[] array3 = val.GetPixels32();
					Color32[] array4 = (Color32[])(object)new Color32[array3.Length];
					for (int j = 0; j < array3.Length; j++)
					{
						Color32 val2 = array3[j];
						if (val2.a > 8)
						{
							array4[j] = val2;
						}
						else
						{
							array4[j] = new Color32((byte)0, (byte)0, (byte)0, (byte)0);
						}
					}
					BloodPainter._brushes[i] = new BloodPainter.Brush
					{
						px = array4,
						w = width,
						h = height
					};
					Object.Destroy((Object)(object)val);
					ManualLogSource log = Log;
					BepInExInfoLogInterpolatedStringHandler val3 = new BepInExInfoLogInterpolatedStringHandler(28, 3, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("Blood brush '");
						((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<string>(array[i]);
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("' processed (");
						((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(width);
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral("x");
						((BepInExLogInterpolatedStringHandler)val3).AppendFormatted<int>(height);
						((BepInExLogInterpolatedStringHandler)val3).AppendLiteral(")");
					}
					log.LogInfo(val3);
				}
				catch (Exception ex)
				{
					ManualLogSource log2 = Log;
					BepInExErrorLogInterpolatedStringHandler val4 = new BepInExErrorLogInterpolatedStringHandler(28, 2, out flag);
					if (flag)
					{
						((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("Blood brush '");
						((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<string>(array[i]);
						((BepInExLogInterpolatedStringHandler)val4).AppendLiteral("' load failed: ");
						((BepInExLogInterpolatedStringHandler)val4).AppendFormatted<Exception>(ex);
					}
					log2.LogError(val4);
				}
			}
		}
	}
	public static class MyPluginInfo
	{
		public const string PLUGIN_GUID = "ER2_FleshWounds";

		public const string PLUGIN_NAME = "ER2 Flesh Wounds";

		public const string PLUGIN_VERSION = "1.0.1";
	}
}
