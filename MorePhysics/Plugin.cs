using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Versioning;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using Corvostudio.Weapons;
using HarmonyLib;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using Il2CppInterop.Runtime.InteropTypes;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using Photon.Pun;
using UnityEngine;
using Object = UnityEngine.Object;

namespace ER2MorePhysics
{
	[HarmonyPatch(typeof(BulletInstance), "OnHit")]
	internal static class BulletImpactPatch
	{
		private static void Postfix(BulletInstance __instance, Vector3 hitDirection, float impactSpeed, RaycastHit hit)
		{
			try
			{
				if (Plugin.GateBlocked() || __instance == null)
				{
					return;
				}
				Collider collider = hit.collider;
				if ((Object)(object)collider == (Object)null)
				{
					return;
				}
				float num = ((Plugin.BulletKnockback != null) ? Plugin.BulletKnockback.Value : 2f);
				Rigidbody componentInParent = ((Component)collider).gameObject.GetComponentInParent<Rigidbody>();
				if ((Object)(object)componentInParent != (Object)null && !componentInParent.isKinematic)
				{
					if (!Plugin.IsSceneObject(((Component)collider).transform))
					{
						Plugin.LogInfo("MP: bullet push blocked on '" + ((Object)((Component)collider).gameObject).name + "' (unit).");
						return;
					}
					Plugin.LogInfo("MP: bullet push '" + ((Object)((Component)collider).gameObject).name + "'.");
					componentInParent.AddForce(hitDirection.normalized * num * 0.6f, (ForceMode)1);
					return;
				}
				float num2 = 10f;
				try
				{
					num2 = __instance.CalculateDamage();
				}
				catch
				{
				}
				num2 *= ((Plugin.BulletDamageMultiplier != null) ? Plugin.BulletDamageMultiplier.Value : 1f);
				if (!Plugin.DamageSceneObject(((Component)collider).gameObject, num2, DamageSource.Bullet))
				{
					Plugin.DiagnoseBlocked(((Component)collider).gameObject, DamageSource.Bullet);
					bool smallBlocker = false;
					try
					{
						Bounds bounds = collider.bounds;
						Vector3 extents = bounds.extents;
						smallBlocker = Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)) <= 1.5f;
					}
					catch
					{
					}
					GhostProp.CheckBlockedHit(__instance, hit.point, hitDirection, smallBlocker, collider);
				}
				else
				{
					Rigidbody orAddRigidbody = Plugin.GetOrAddRigidbody(((Component)collider).gameObject);
					if (!((Object)(object)orAddRigidbody == (Object)null))
					{
						orAddRigidbody.AddForce(hitDirection.normalized * num, (ForceMode)1);
						Plugin.DropItemsOnTop(((Component)orAddRigidbody).transform);
						Plugin.LogQuiet("MorePhysics: bullet destroyed '" + ((Object)((Component)collider).gameObject).name + "'.");
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics bullet impact failed: " + ex.Message));
			}
		}
	}
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
				if (hit == null || (Object)(object)hit.rigidbody == (Object)null || Plugin.GateBlocked())
				{
					return;
				}
				Rigidbody rigidbody = hit.rigidbody;
				bool flag = (Object)(object)((Component)rigidbody).GetComponentInParent<RagdollManager>(true) != (Object)null;
				bool flag2 = flag && Plugin.PushCorpses != null && Plugin.PushCorpses.Value;
				bool flag3 = !flag && Plugin.PushPhysItems != null && Plugin.PushPhysItems.Value && Plugin.IsSceneObject(((Component)rigidbody).transform);
				if (!flag2 && !flag3)
				{
					return;
				}
				Vector3 val = (((Object)(object)hit.controller != (Object)null) ? hit.controller.velocity : Vector3.zero);
				val.y = 0f;
				float magnitude = val.magnitude;
				if (magnitude < 0.5f)
				{
					return;
				}
				Vector3 normal = hit.normal;
				if ((flag3 && normal.y > 0.6f) || Time.time - _lastPushTime < 0.3f)
				{
					return;
				}
				_lastPushTime = Time.time;
				if (rigidbody.isKinematic)
				{
					rigidbody.isKinematic = false;
					rigidbody.useGravity = true;
				}
				rigidbody.WakeUp();
				float num = ((Plugin.CorpsePushForce != null) ? Plugin.CorpsePushForce.Value : 10f);
				if (num <= 0f)
				{
					return;
				}
				float num2 = 1f;
				try
				{
					num2 = Mathf.Clamp(rigidbody.mass, 0.5f, 20f) / 5f;
					if (!flag)
					{
						num2 = Mathf.Clamp(num2, 0.3f, 1.2f);
					}
				}
				catch
				{
				}
				Vector3 val2 = val;
				if (val2.sqrMagnitude < 0.01f && hit.moveDirection != Vector3.zero)
				{
					val2 = hit.moveDirection;
					val2.y = 0f;
				}
				if (!(val2.sqrMagnitude < 0.01f))
				{
					float num3 = Mathf.Clamp(magnitude, 0.5f, 2f);
					rigidbody.AddForceAtPosition(val2.normalized * num * num3 * num2, hit.point, (ForceMode)1);
				}
			}
			catch
			{
			}
		}
	}
	public enum DamageSource
	{
		Bullet,
		Melee,
		Explosion
	}
	[HarmonyPatch(typeof(Explosion), "CreateExplosion")]
	internal static class ExplosionImpactPatch
	{
		private static void Postfix(Vector3 position, float explosionRadius, float explosionMaxDamage)
		{
			try
			{
				if (Plugin.GateBlocked() || explosionRadius <= 0f)
				{
					return;
				}
				float num = ((Plugin.ExplosionDamageMultiplier != null) ? Plugin.ExplosionDamageMultiplier.Value : 1f);
				float num2 = ((Plugin.ExplosionKnockback != null) ? Plugin.ExplosionKnockback.Value : 4f);
				Collider[] array = ((Collider[])(Il2CppArrayBase<Collider>)(object)Physics.OverlapSphere(position, explosionRadius));
				foreach (Collider val in array)
				{
					try
					{
						if ((Object)(object)val == (Object)null || (Object)(object)((Component)val).gameObject == (Object)null)
						{
							continue;
						}
						Vector3 val2 = ((Component)val).transform.position - position;
						float magnitude = val2.magnitude;
						if (magnitude < 0.01f)
						{
							continue;
						}
						float num3 = 1f - Mathf.Clamp01(magnitude / explosionRadius);
						Vector3 val3 = val2.normalized + Vector3.up * 0.6f;
						Vector3 normalized = val3.normalized;
						Rigidbody componentInParent = ((Component)val).gameObject.GetComponentInParent<Rigidbody>();
						if ((Object)(object)componentInParent != (Object)null && !componentInParent.isKinematic)
						{
							if (!Plugin.IsSceneObject(((Component)val).transform))
							{
								Plugin.LogInfo("MP: explosion push blocked on '" + ((Object)((Component)val).gameObject).name + "' (unit).");
								continue;
							}
							Plugin.LogInfo("MP: explosion push '" + ((Object)((Component)val).gameObject).name + "'.");
							componentInParent.AddForce(normalized * (num3 * num2), (ForceMode)1);
							continue;
						}
						if (!Plugin.DamageSceneObject(((Component)val).gameObject, explosionMaxDamage * num3 * num, DamageSource.Explosion))
						{
							Plugin.DiagnoseBlocked(((Component)val).gameObject, DamageSource.Explosion);
							continue;
						}
						Rigidbody orAddRigidbody = Plugin.GetOrAddRigidbody(((Component)val).gameObject);
						if (!((Object)(object)orAddRigidbody == (Object)null))
						{
							orAddRigidbody.AddForce(normalized * (num3 * num2), (ForceMode)1);
							Plugin.DropItemsOnTop(((Component)orAddRigidbody).transform);
							Plugin.LogQuiet("MorePhysics: explosion destroyed '" + ((Object)((Component)val).gameObject).name + "'.");
						}
					}
					catch (Exception ex)
					{
						Plugin.ModLog.LogWarning((object)("MorePhysics explosion object failed: " + ex.Message));
					}
				}
			}
			catch (Exception ex2)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics explosion impact failed: " + ex2.Message));
			}
		}
	}
	[HarmonyPatch(typeof(BulletInstance), "RaycastAll")]
	internal static class GhostProp
	{
		private static float _nextScan;

		private static string effectMatchDebug;

		private static void Postfix(BulletInstance __instance, Vector3 startPos, Vector3 dir, float dist, bool __result)
		{
			try
			{
				if (!__result && !Plugin.GateBlocked() && __instance != null && IsPlayerShot(__instance) && !(Time.time < _nextScan))
				{
					_nextScan = Time.time + 2f;
					float magnitude = dir.magnitude;
					if (!(magnitude < 0.01f) && !(dist <= 0f))
					{
						Vector3 val = dir / magnitude;
						CheckSegment(__instance, startPos, startPos + val * dist, val, 0f);
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics ghost-prop failed: " + ex.Message));
			}
		}

		internal static void CheckBlockedHit(BulletInstance bullet, Vector3 hitPoint, Vector3 hitDir, bool smallBlocker, Collider blocked)
		{
			try
			{
				if (Plugin.GateBlocked() || bullet == null || !IsPlayerShot(bullet) || Time.time < _nextScan)
				{
					return;
				}
				_nextScan = Time.time + 2f;
				if (!CheckBlockedSubtree(bullet, blocked, hitPoint, hitDir))
				{
					float magnitude = hitDir.magnitude;
					if (!(magnitude < 0.01f))
					{
						Vector3 ndir = hitDir / magnitude;
						Vector3 startPosition = bullet.startPosition;
						CheckSegment(bullet, startPosition, hitPoint, ndir, smallBlocker ? 4f : 0f);
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics ghost-prop blocked-hit failed: " + ex.Message));
			}
		}

		private static bool CheckBlockedSubtree(BulletInstance bullet, Collider blocked, Vector3 hitPoint, Vector3 hitDir)
		{
			try
			{
				if ((Object)(object)blocked == (Object)null || (Object)(object)((Component)blocked).transform == (Object)null)
				{
					return false;
				}
				Transform transform = ((Component)blocked).transform;
				Renderer[] array = ((Renderer[])((Component)transform).GetComponentsInChildren<Renderer>(true));
				if (array == null || array.Length == 0)
				{
					return false;
				}
				Transform val = null;
				float num = float.MaxValue;
				foreach (Renderer val2 in array)
				{
					if ((Object)(object)val2 == (Object)null || (Object)(object)((Component)val2).transform == (Object)null || val2 is SkinnedMeshRenderer)
					{
						continue;
					}
					Transform transform2 = ((Component)val2).transform;
					Bounds bounds = val2.bounds;
					if (!(Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) > 1.5f) && !IsEffectLike(transform2) && !((Object)(object)((Component)transform2).GetComponentInParent<Rigidbody>(true) != (Object)null) && !((Object)(object)((Component)transform2).GetComponentInChildren<Collider>(true) != (Object)null) && Plugin.IsSceneObject(transform2) && Plugin.IsPhysicalizable(transform2))
					{
						float num2 = Vector3.Distance(bounds.center, hitPoint);
						if (num2 < num)
						{
							num = num2;
							val = transform2;
						}
					}
				}
				if ((Object)(object)val == (Object)null)
				{
					return false;
				}
				Rigidbody orAddRigidbody = Plugin.GetOrAddRigidbody(((Component)val).gameObject);
				if ((Object)(object)orAddRigidbody == (Object)null)
				{
					return false;
				}
				float num3 = ((Plugin.BulletKnockback != null) ? Plugin.BulletKnockback.Value : 2f);
				float magnitude = hitDir.magnitude;
				Vector3 val3 = ((magnitude > 0.01f) ? (hitDir / magnitude) : Vector3.forward);
				orAddRigidbody.AddForce(val3 * num3 * 1.5f, (ForceMode)1);
				Plugin.ModLog.LogInfo((object)("MP: ghost-prop hit '" + ((Object)val).name + "' (under '" + ((Object)transform).name + "')."));
				return true;
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics ghost subtree failed: " + ex.Message));
				return false;
			}
		}

		private static bool IsEffectLike(Transform t)
		{
			bool num = Plugin.IsNoiseLike(t);
			if (num)
			{
				effectMatchDebug = ((Object)t).name;
			}
			return num;
		}

		private static Renderer[] GetAllRenderers()
		{
			try
			{
				Il2CppReferenceArray<Object> val = Object.FindObjectsOfType(Il2CppType.Of<Renderer>(), true);
				if (val == null || ((Il2CppArrayBase<Object>)(object)val).Count == 0)
				{
					return null;
				}
				Renderer[] array = (Renderer[])(object)new Renderer[((Il2CppArrayBase<Object>)(object)val).Count];
				for (int i = 0; i < ((Il2CppArrayBase<Object>)(object)val).Count; i++)
				{
					array[i] = ((Il2CppObjectBase)((Il2CppArrayBase<Object>)(object)val)[i]).TryCast<Renderer>();
				}
				return array;
			}
			catch
			{
				return ((Renderer[])Object.FindObjectsOfType<Renderer>());
			}
		}

		private static void CheckSegment(BulletInstance bullet, Vector3 from, Vector3 to, Vector3 ndir, float extend)
		{
			try
			{
				Vector3 val = to - from;
				float magnitude = val.magnitude;
				if (magnitude < 0.3f)
				{
					return;
				}
				float num = magnitude + extend;
				Transform val2 = null;
				float num2 = float.MaxValue;
				float num3 = float.MaxValue;
				Renderer[] allRenderers = GetAllRenderers();
				foreach (Renderer val3 in allRenderers)
				{
					if ((Object)(object)val3 == (Object)null || (Object)(object)((Component)val3).transform == (Object)null || val3 is SkinnedMeshRenderer)
					{
						continue;
					}
					Transform transform = ((Component)val3).transform;
					Bounds bounds = val3.bounds;
					float num4 = Vector3.Dot(bounds.center - from, ndir);
					if (!(num4 < -0.5f) && !(num4 > num + 1f))
					{
						Vector3 val4 = from + ndir * Mathf.Clamp(num4, 0f, num);
						float num5 = Vector3.Distance(bounds.center, val4);
						if (!(num5 > 2f) && !IsEffectLike(transform) && !(Mathf.Max(bounds.extents.x, Mathf.Max(bounds.extents.y, bounds.extents.z)) > 1.5f) && !((Object)(object)((Component)transform).GetComponentInParent<Rigidbody>(true) != (Object)null) && !((Object)(object)((Component)transform).GetComponentInChildren<Collider>(true) != (Object)null) && Plugin.IsSceneObject(transform) && Plugin.IsPhysicalizable(transform) && (num5 < num2 - 0.05f || (Mathf.Abs(num5 - num2) <= 0.05f && num4 < num3)))
						{
							num2 = num5;
							num3 = num4;
							val2 = transform;
						}
					}
				}
				if (!((Object)(object)val2 == (Object)null))
				{
					Rigidbody orAddRigidbody = Plugin.GetOrAddRigidbody(((Component)val2).gameObject);
					if (!((Object)(object)orAddRigidbody == (Object)null))
					{
						float num6 = ((Plugin.BulletKnockback != null) ? Plugin.BulletKnockback.Value : 2f);
						orAddRigidbody.AddForce(ndir * num6 * 1.5f, (ForceMode)1);
						Plugin.ModLog.LogInfo((object)("MP: ghost-prop hit '" + ((Object)val2).name + "'."));
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics ghost segment failed: " + ex.Message));
			}
		}

		private static bool IsPlayerShot(BulletInstance bullet)
		{
			try
			{
				Soldier shooter = bullet.shooter;
				return (Object)(object)shooter != (Object)null && ((Creature)shooter).IsPlayer();
			}
			catch
			{
				return false;
			}
		}
	}
	[HarmonyPatch(typeof(Soldier), "Melee")]
	internal static class MeleeImpactPatch
	{
		private static float _next;

		private static void Postfix(Soldier __instance)
		{
			try
			{
				if (Plugin.GateBlocked() || (Object)(object)__instance == (Object)null || (Object)(object)((Component)__instance).transform == (Object)null || Time.time < _next)
				{
					return;
				}
				_next = Time.time + 0.3f;
				Vector3 position = ((Component)__instance).transform.position;
				Vector3 forward = ((Component)__instance).transform.forward;
				float num = ((Plugin.MeleeKnockback != null) ? Plugin.MeleeKnockback.Value : 6f);
				float damage = ((Plugin.MeleeDamage != null) ? Plugin.MeleeDamage.Value : 25f);
				Collider[] array = ((Collider[])(Il2CppArrayBase<Collider>)(object)Physics.OverlapSphere(position, 2.2f));
				foreach (Collider val in array)
				{
					try
					{
						if ((Object)(object)val == (Object)null || (Object)(object)((Component)val).gameObject == (Object)null)
						{
							continue;
						}
						Vector3 val2 = ((Component)val).transform.position - position;
						if (val2.magnitude < 0.01f || Vector3.Angle(forward, val2.normalized) > 60f)
						{
							continue;
						}
						Rigidbody componentInParent = ((Component)val).gameObject.GetComponentInParent<Rigidbody>();
						Vector3 val3;
						if ((Object)(object)componentInParent != (Object)null && !componentInParent.isKinematic)
						{
							if (!Plugin.IsSceneObject(((Component)val).transform))
							{
								Plugin.LogInfo("MP: melee push blocked on '" + ((Object)((Component)val).gameObject).name + "' (unit).");
								continue;
							}
							Plugin.LogInfo("MP: melee push '" + ((Object)((Component)val).gameObject).name + "'.");
							val3 = val2.normalized + Vector3.up * 0.5f;
							Vector3 normalized = val3.normalized;
							componentInParent.AddForce(normalized * num, (ForceMode)1);
							continue;
						}
						if (!Plugin.DamageSceneObject(((Component)val).gameObject, damage, DamageSource.Melee))
						{
							Plugin.DiagnoseBlocked(((Component)val).gameObject, DamageSource.Melee);
							continue;
						}
						Rigidbody orAddRigidbody = Plugin.GetOrAddRigidbody(((Component)val).gameObject);
						if (!((Object)(object)orAddRigidbody == (Object)null))
						{
							val3 = val2.normalized + Vector3.up * 0.5f;
							Vector3 normalized2 = val3.normalized;
							orAddRigidbody.AddForce(normalized2 * num, (ForceMode)1);
							Plugin.DropItemsOnTop(((Component)orAddRigidbody).transform);
							Plugin.LogQuiet("MorePhysics: melee destroyed '" + ((Object)((Component)val).gameObject).name + "'.");
						}
					}
					catch (Exception ex)
					{
						Plugin.ModLog.LogWarning((object)("MorePhysics melee object failed: " + ex.Message));
					}
				}
			}
			catch (Exception ex2)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics melee impact failed: " + ex2.Message));
			}
		}
	}
	public class PhysicsDebris : MonoBehaviour
	{
		public float DespawnAfter = 30f;

		public float DamageOnHit = 10f;

		private float _alive;

		private void Update()
		{
			if (!(DespawnAfter <= 0f))
			{
				_alive += Time.deltaTime;
				if (_alive >= DespawnAfter)
				{
					Object.Destroy((Object)(object)((Component)this).gameObject);
				}
			}
		}

		private void OnCollisionEnter(Collision collision)
		{
			try
			{
				if (collision == null || (Object)(object)collision.gameObject == (Object)null || DamageOnHit <= 0f)
				{
					return;
				}
				Collider collider = collision.collider;
				if ((Object)(object)collider == (Object)null)
				{
					return;
				}
				Soldier val = FindSoldier(((Component)collider).transform);
				if ((Object)(object)val == (Object)null)
				{
					return;
				}
				float num = 0f;
				try
				{
					Vector3 relativeVelocity = collision.relativeVelocity;
					num = relativeVelocity.magnitude;
				}
				catch
				{
				}
				if (num < 2.5f)
				{
					return;
				}
				float damage = DamageOnHit * Mathf.Clamp(0.4f + num * 0.1f, 0.4f, 2f);
				Vector3 point = ((Component)val).transform.position;
				try
				{
					if (collision.contacts != null && ((Il2CppArrayBase<ContactPoint>)(object)collision.contacts).Length > 0)
					{
						ContactPoint val2 = ((Il2CppArrayBase<ContactPoint>)(object)collision.contacts)[0];
						point = val2.point;
					}
				}
				catch
				{
				}
				if (TryDamageSoldier(val, damage, point))
				{
					Plugin.ModLog.LogInfo((object)("MP: debris hit '" + ((Object)val).name + "' for " + damage.ToString("0.0") + " dmg speed=" + num.ToString("0.0")));
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics debris collision failed: " + ex.Message));
			}
		}

		private static Soldier FindSoldier(Transform t)
		{
			Transform val = t;
			while ((Object)(object)val != (Object)null)
			{
				Soldier component = ((Component)val).GetComponent<Soldier>();
				if ((Object)(object)component != (Object)null)
				{
					return component;
				}
				val = val.parent;
			}
			return null;
		}

		private static bool TryDamageSoldier(Soldier soldier, float damage, Vector3 point)
		{
			try
			{
				if ((Object)(object)soldier == (Object)null || (Object)(object)((Component)soldier).transform == (Object)null)
				{
					return false;
				}
				BodyPart[] array = ((BodyPart[])((Component)soldier).GetComponentsInChildren<BodyPart>(true));
				if (array == null || array.Length == 0)
				{
					((Creature)soldier).Damage(damage);
					return true;
				}
				bool flag = false;
				foreach (BodyPart val in array)
				{
					if ((Object)(object)val == (Object)null)
					{
						continue;
					}
					try
					{
						if (val.HitPart(damage, 0f, point, "", (HitType)0))
						{
							flag = true;
						}
					}
					catch
					{
					}
				}
				if (!flag)
				{
					((Creature)soldier).Damage(damage);
				}
				return true;
			}
			catch
			{
				return false;
			}
		}
	}
	[HarmonyPatch(typeof(PlayerController), "Update")]
	internal static class PhysicsTickPatch
	{
		private static float _next;

		private static void Postfix()
		{
			try
			{
				UnitCollision.Apply();
				if (Plugin.GateBlocked())
				{
					return;
				}
				if (!(Time.time < _next))
				{
					_next = Time.time + 2f;
					Plugin.ReenableDamagedColliders();
					if (Plugin.EnableItemPhysics != null && Plugin.EnableItemPhysics.Value)
					{
						EnableItemPhysics();
					}
				}
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("MorePhysics tick failed: " + ex.Message));
			}
		}

		private static void EnableItemPhysics()
		{
			Il2CppSystem.Collections.Generic.List<ItemObject> spawnedItems = ItemObject.spawnedItems;
			if (spawnedItems == null)
			{
				return;
			}
			Il2CppSystem.Collections.Generic.List<ItemObject>.Enumerator enumerator = spawnedItems.GetEnumerator();
			while (enumerator.MoveNext())
			{
				ItemObject current = enumerator.Current;
				try
				{
					if (!((Object)(object)current == (Object)null))
					{
						Transform transform = ((Component)current).transform;
						if (!((Object)(object)transform == (Object)null) && !Plugin.IsAttachedToUnit(transform) && !Plugin.IsExcludedName(current.name) && !current.IsPhysicEnabled())
						{
							current.EnablePhysic();
							Plugin.LogQuiet("MorePhysics: enabled physics on '" + current.name + "'.");
						}
					}
				}
				catch (Exception ex)
				{
					Plugin.LogQuiet("MorePhysics item skipped: " + ex.Message);
				}
			}
		}
	}
	[BepInPlugin("er2.morephysics", "ER2 More Physics", "0.1.49")]
	[BepInProcess("Easy Red 2.exe")]
	public class Plugin : BasePlugin
	{
#if CN_BUILD
		internal const bool DefaultChinese = true;
#else
		internal const bool DefaultChinese = false;
#endif

		internal static ManualLogSource ModLog;

#if CN_BUILD
		private static bool _chinese = true;
#else
		private static bool _chinese;

		private static bool _langChecked;
#endif

		internal static ConfigEntry<bool> Enabled;

		internal static ConfigEntry<bool> SingleplayerOnly;

		internal static ConfigEntry<float> DefaultHealth;

		internal static ConfigEntry<float> MeleeDamage;

		internal static ConfigEntry<float> BulletDamageMultiplier;

		internal static ConfigEntry<float> ExplosionDamageMultiplier;

		internal static ConfigEntry<float> BulletKnockback;

		internal static ConfigEntry<float> MeleeKnockback;

		internal static ConfigEntry<float> ExplosionKnockback;

		internal static ConfigEntry<float> PhysicsDamage;

		internal static ConfigEntry<bool> UnitCollision;

		internal static ConfigEntry<int> UnitCollisionLayer;

		internal static ConfigEntry<bool> PushCorpses;

		internal static ConfigEntry<float> CorpsePushForce;

		internal static ConfigEntry<bool> PushPhysItems;

		internal static ConfigEntry<bool> EnableItemPhysics;

		internal static ConfigEntry<bool> EnableScenePhysics;

		internal static ConfigEntry<bool> ExplosionOnlyHardObjects;

		internal static ConfigEntry<float> DespawnTime;

		internal static ConfigEntry<bool> EnableFurniture;

		internal static ConfigEntry<bool> EnableContainers;

		internal static ConfigEntry<bool> EnableDebris;

		internal static ConfigEntry<bool> EnableBuildingParts;

		internal static ConfigEntry<bool> EnableMiscProps;

		internal static ConfigEntry<string> ExcludedNameKeywords;

		internal static ConfigEntry<bool> TargetPracticeOverride;

		private static readonly Dictionary<int, float> sceneHp = new Dictionary<int, float>();

		internal static readonly List<Transform> PhysProps = new List<Transform>();

		private const int PhysPropsCap = 512;

		internal static readonly string[] NoiseKeywords = new string[28]
		{
			"impactfx", "dust", "decal", "visual_clone", "smoke", "spark", "blood", "bolt", "tracer", "muzzle",
			"mag", "magazine", "garand", "thompson", "carbine", "springfield", "weapon", "rifle", "stg", "mp40",
			"luger", "colt", "beretta", "tt33", "nagan", "shotgun", "bar_", "scoped"
		};

		private static readonly HashSet<int> oversized = new HashSet<int>();

		private const int SceneHpCap = 2048;

		private const int OversizedCap = 512;

		private const int DropMax = 8;

		private static float _nextDropScan;

		private static readonly HashSet<int> seenDropSkips = new HashSet<int>();

		private static readonly Dictionary<int, Collider> damagedColliders = new Dictionary<int, Collider>();

		private const int DamagedColliderCap = 256;

		private static float _nextLog;

		private static readonly Dictionary<int, float> _diagTimes = new Dictionary<int, float>();

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
			if (!_chinese)
			{
				return en;
			}
			return cn;
#endif
		}

		internal static void RegisterPhysicalized(Transform anchor)
		{
			try
			{
				if ((Object)(object)anchor == (Object)null || (Object)(object)((Component)anchor).gameObject == (Object)null || IsNoiseName(((Object)anchor).name))
				{
					return;
				}
				for (int i = 0; i < PhysProps.Count; i++)
				{
					if ((Object)(object)PhysProps[i] == (Object)(object)anchor)
					{
						return;
					}
				}
				if (PhysProps.Count >= 512)
				{
					PhysProps.RemoveAt(0);
				}
				PhysProps.Add(anchor);
			}
			catch
			{
			}
		}

		internal static bool IsNoiseName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			string text = name.ToLowerInvariant();
			for (int i = 0; i < NoiseKeywords.Length; i++)
			{
				if (text.Contains(NoiseKeywords[i]))
				{
					return true;
				}
			}
			return false;
		}

		internal static bool IsNoiseLike(Transform t)
		{
			try
			{
				Transform val = t;
				int num = 0;
				while ((Object)(object)val != (Object)null && num < 8)
				{
					if (IsNoiseName(((Object)val).name))
					{
						return true;
					}
					val = val.parent;
					num++;
				}
			}
			catch
			{
			}
			return false;
		}

		internal static bool IsOversizedStructure(Transform anchor)
		{
			if ((Object)(object)anchor == (Object)null)
			{
				return false;
			}
			int instanceID = ((Object)((Component)anchor).gameObject).GetInstanceID();
			if (oversized.Contains(instanceID))
			{
				return true;
			}
			if (((Renderer[])((Component)anchor).GetComponentsInChildren<Renderer>(true)).Length > 30)
			{
				oversized.Add(instanceID);
				if (oversized.Count > 512)
				{
					oversized.Clear();
				}
				return true;
			}
			return false;
		}

		internal static bool IsOversizedCollider(GameObject go)
		{
			if ((Object)(object)go == (Object)null)
			{
				return false;
			}
			Collider[] array = ((Collider[])go.GetComponents<Collider>());
			foreach (Collider val in array)
			{
				if (!((Object)(object)val == (Object)null) && !val.isTrigger)
				{
					Bounds bounds = val.bounds;
					Vector3 extents = bounds.extents;
					if (Mathf.Max(extents.x, Mathf.Max(extents.y, extents.z)) > 2.5f)
					{
						return true;
					}
				}
			}
			return false;
		}

		public override void Load()
		{
			ModLog = ((BasePlugin)this).Log;
			Enabled = ((BasePlugin)this).Config.Bind<bool>("General", "Enabled", true, T("总开关：关闭后额外的物理效果全部失效。", "Master switch. When false, the extra physics effects are disabled."));
			SingleplayerOnly = ((BasePlugin)this).Config.Bind<bool>("General", "SingleplayerOnly", true, T("默认仅单机（离线）战斗生效（联机物理不同步且影响公平性）；关闭后联机同样生效。", "By default the extra physics only apply in singleplayer (offline) sessions. Disable to also apply in multiplayer."));
			DefaultHealth = ((BasePlugin)this).Config.Bind<float>("Physics", "DefaultHealth", 40f, new ConfigDescription(T("场景物件在被击飞前的基础生命值。", "Default health of scene objects before they can be knocked flying."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(1f, 500f), Array.Empty<object>()));
			MeleeDamage = ((BasePlugin)this).Config.Bind<float>("Physics", "MeleeDamage", 25f, new ConfigDescription(T("近战对场景物件造成的伤害。", "Damage dealt to scene objects by melee attacks."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 200f), Array.Empty<object>()));
			BulletDamageMultiplier = ((BasePlugin)this).Config.Bind<float>("Physics", "BulletDamageMultiplier", 1f, new ConfigDescription(T("子弹对场景物件的伤害倍率。", "Multiplier for bullet damage against scene objects."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.1f, 5f), Array.Empty<object>()));
			ExplosionDamageMultiplier = ((BasePlugin)this).Config.Bind<float>("Physics", "ExplosionDamageMultiplier", 1f, new ConfigDescription(T("爆炸对场景物件的伤害倍率。", "Multiplier for explosion damage against scene objects."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0.1f, 5f), Array.Empty<object>()));
			BulletKnockback = ((BasePlugin)this).Config.Bind<float>("Physics", "BulletKnockback", 3f, new ConfigDescription(T("子弹摧毁场景物件时的击飞冲量。", "Knockback impulse applied when a scene object is destroyed by a bullet."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 10f), Array.Empty<object>()));
			MeleeKnockback = ((BasePlugin)this).Config.Bind<float>("Physics", "MeleeKnockback", 4f, new ConfigDescription(T("近战摧毁场景物件时的击飞冲量。", "Knockback impulse applied when a scene object is destroyed by melee."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 20f), Array.Empty<object>()));
			ExplosionKnockback = ((BasePlugin)this).Config.Bind<float>("Physics", "ExplosionKnockback", 5f, new ConfigDescription(T("爆炸摧毁场景物件时的击飞冲量。", "Knockback impulse applied when a scene object is destroyed by an explosion."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 30f), Array.Empty<object>()));
			PhysicsDamage = ((BasePlugin)this).Config.Bind<float>("Physics", "PhysicsDamage", 50f, new ConfigDescription(T("飞行中的物理化物品撞到士兵/玩家造成的伤害基础值（速度越快越疼，低速蹭到不扣血；0 = 关闭碰撞伤害）。友军判定归战场调整 mod 负责。", "Base damage dealt when a flying physicalized object hits a soldier/player (faster = more painful, slow grazing deals none; 0 = off). Friendly-fire decisions are handled by the Combat Tweaks mod."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 100f), Array.Empty<object>()));
			UnitCollision = ((BasePlugin)this).Config.Bind<bool>("Physics", "UnitCollision", true, T("士兵互相阻挡（不重叠）：复用原版贴合人体的受击碰撞体阻挡玩家/AI 相互穿过。默认开；尸体不参与阻挡（只被推开，不挡活人）。", "Units physically block each other (no overlap): reuses the vanilla body colliders so player/AI can't pass through each other. On by default; corpses don't block (pushed away, don't stop living units)."));
			UnitCollisionLayer = ((BasePlugin)this).Config.Bind<int>("Physics", "UnitCollisionLayer", -1, new ConfigDescription(T("士兵互碰的调试项：-1 = 正常；-2 = 诊断模式（只打印碰撞矩阵与受击碰撞体信息，不改任何物理）。一般无需修改。", "Unit-collision debug: -1 = normal; -2 = diagnostics (only logs the collision matrices and hit-collider details, changes nothing). Usually leave at -1."), (AcceptableValueBase)(object)new AcceptableValueRange<int>(-2, 31), Array.Empty<object>()));
			PushCorpses = ((BasePlugin)this).Config.Bind<bool>("Physics", "PushCorpses", true, T("单位推开尸体：士兵/玩家撞到尸体时把它推开，不再从尸体上走过。独立于士兵互碰开关（v0.1.32 起默认开）。", "Units push corpses aside: soldiers/player shove corpses out of the way instead of walking over them. Independent of the unit-block toggle (on by default since v0.1.32)."));
			CorpsePushForce = ((BasePlugin)this).Config.Bind<float>("Physics", "CorpsePushForce", 10f, new ConfigDescription(T("推开尸体的冲量力度（越大推得越远；0 = 尸体只阻挡不被推开）。", "Impulse strength used to shove corpses (higher = pushed further; 0 = corpses block but are not shoved)."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 40f), Array.Empty<object>()));
			PushPhysItems = ((BasePlugin)this).Config.Bind<bool>("Physics", "PushPhysItems", true, T("推动物理化道具：士兵/玩家撞到已被物理化的物品（箱子/收音机/桌子等）时会把它推开，而不是卡住。独立于推开尸体开关。", "Push physicalized props: soldiers/player shove physicalized items (crates, radios, tables) out of the way instead of getting stuck. Independent of the corpse-push toggle."));
			EnableItemPhysics = ((BasePlugin)this).Config.Bind<bool>("Physics", "EnableItemPhysics", true, T("周期性给场景中的游戏物品（ItemObject）开启原生物理（尊重下方排除关键词）。", "Periodically enable physics on ItemObject items in the scene (respects the exclusion keywords below)."));
			EnableScenePhysics = ((BasePlugin)this).Config.Bind<bool>("Physics", "EnableScenePhysics", true, T("允许非物品场景物件在生命耗尽后物理化。", "Allow non-item scene objects to become physical when destroyed."));
			ExplosionOnlyHardObjects = ((BasePlugin)this).Config.Bind<bool>("Physics", "ExplosionOnlyHardObjects", true, T("硬质物件（墙/混凝土/柱子等）只受爆炸伤害，子弹和近战打不动。", "Hard objects (walls, concrete, pillars, etc.) only take damage from explosions, not bullets or melee."));
			DespawnTime = ((BasePlugin)this).Config.Bind<float>("Physics", "DespawnTime", 0f, new ConfigDescription(T("物理化场景物件在多少秒后自动消失；0 = 永不消失。", "Seconds before a physicalized scene object is removed. 0 = never despawn."), (AcceptableValueBase)(object)new AcceptableValueRange<float>(0f, 300f), Array.Empty<object>()));
			EnableFurniture = ((BasePlugin)this).Config.Bind<bool>("Physics", "EnableFurniture", true, T("允许家具（桌子/椅子/床/柜子等）物理化。", "Allow furniture (tables, chairs, beds, desks) to become physical."));
			EnableContainers = ((BasePlugin)this).Config.Bind<bool>("Physics", "EnableContainers", true, T("允许容器（木箱/弹药箱/油桶/袋子等）物理化。", "Allow containers (crates, boxes, barrels, bags) to become physical."));
			EnableDebris = ((BasePlugin)this).Config.Bind<bool>("Physics", "EnableDebris", false, T("允许地面小杂物（碎石/草/弹壳等）物理化（默认关，防满地乱滚）。", "Allow small ground debris/rocks/grass to become physical (off by default to avoid clutter)."));
			EnableBuildingParts = ((BasePlugin)this).Config.Bind<bool>("Physics", "EnableBuildingParts", false, T("允许建筑部件（墙/混凝土/砖/柱等）物理化（默认关——它们属于原生可破坏体系）。", "Allow building parts (walls, concrete, bricks, pillars) to become physical (off by default)."));
			EnableMiscProps = ((BasePlugin)this).Config.Bind<bool>("Physics", "EnableMiscProps", true, T("允许其他未分类道具物理化。", "Allow other unclassified props to become physical."));
			ExcludedNameKeywords = ((BasePlugin)this).Config.Bind<string>("Physics", "ExcludedNameKeywords", "grass,debris,rock,stone,pebble,bush,foliage,clutter,ground,dirt,shell,casing,leaf,branch", T("永不物理化的名称关键词（逗号分隔；同时约束物品物理与场景物件物理化）。", "Comma-separated name keywords that are never physicalized (applies to both item physics and scene object physicalization)."));
			TargetPracticeOverride = ((BasePlugin)this).Config.Bind<bool>("Physics", "TargetPracticeOverride", false, T("接管射击场靶子：被击中时保持可射击（跳过游戏的翻倒/冷却反应），连打数枪后可被本 mod 物理化击飞；关闭则保留游戏原生靶子行为（打靶课程计分不受影响）。", "Override shooting-range targets: they stay shootable when hit (the game's flip/cooldown reaction is skipped) so they can be destroyed and physicalized. Turn off to keep the vanilla target behavior (practice course scoring)."));
			try
			{
				ClassInjector.RegisterTypeInIl2Cpp<PhysicsDebris>();
				ClassInjector.RegisterTypeInIl2Cpp<CorpsePusher>();
			}
			catch (Exception ex)
			{
				ModLog.LogWarning((object)("MorePhysics: type registration failed: " + ex.Message));
			}
			new Harmony("er2.morephysics").PatchAll(typeof(Plugin).Assembly);
			ModLog.LogInfo((object)"ER2 More Physics 0.1.49 loaded.");
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

		internal static bool IsAttachedToUnit(Transform t)
		{
			while ((Object)(object)t != (Object)null)
			{
				if ((Object)(object)((Component)t).GetComponent<Creature>() != (Object)null)
				{
					return true;
				}
				t = t.parent;
			}
			return false;
		}

		internal static bool IsSceneObject(Transform t)
		{
			if ((Object)(object)t == (Object)null)
			{
				return false;
			}
			if (IsAttachedToUnit(t))
			{
				return false;
			}
			if ((Object)(object)((Component)t).GetComponentInParent<Vehicle>() != (Object)null)
			{
				return false;
			}
			if ((Object)(object)((Component)t).GetComponentInParent<RagdollManager>() != (Object)null)
			{
				return false;
			}
			if ((Object)(object)((Component)t).GetComponentInParent<DestructableBuilding>() != (Object)null)
			{
				return false;
			}
			int layer = ((Component)t).gameObject.layer;
			if (layer == 4 || layer == 14)
			{
				return false;
			}
			string name = ((Object)t).name;
			if (name != null && (name.Contains("Terrain") || name.Contains("Water") || name.Contains("Ground")))
			{
				return false;
			}
			if (name != null && ContainsAny(name.ToLowerInvariant(), "collider", "hitbox", "capsule", "ragdoll"))
			{
				return false;
			}
			return true;
		}

		internal static bool IsExcludedName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			if (ExcludedNameKeywords != null && !string.IsNullOrEmpty(ExcludedNameKeywords.Value))
			{
				string text = name.ToLowerInvariant();
				string[] array = ExcludedNameKeywords.Value.Split(new char[2] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (string text2 in array)
				{
					if (!string.IsNullOrEmpty(text2) && text.Contains(text2.Trim().ToLowerInvariant()))
					{
						return true;
					}
				}
			}
			return false;
		}

		internal static bool IsPhysicalizable(Transform t)
		{
			if ((Object)(object)t == (Object)null || !IsSceneObject(t))
			{
				return false;
			}
			string name = ((Object)t).name;
			if (IsExcludedName(name))
			{
				return false;
			}
			if (string.IsNullOrEmpty(name))
			{
				if (EnableMiscProps != null)
				{
					return EnableMiscProps.Value;
				}
				return false;
			}
			string text = name.ToLowerInvariant();
			if (ContainsAny(text, "door", "gate", "doorway", "crater", "decal", "collider", "hitbox", "capsule"))
			{
				return false;
			}
			if (ContainsAny(text, "spawner"))
			{
				return false;
			}
			if (ContainsAny(text, "table", "chair", "bed", "couch", "desk", "shelf", "wardrobe", "cabinet", "bench"))
			{
				if (EnableFurniture != null)
				{
					return EnableFurniture.Value;
				}
				return false;
			}
			if (ContainsAny(text, "crate", "box", "barrel", "bag", "chest", "suitcase", "ammo", "supply"))
			{
				if (EnableContainers != null)
				{
					return EnableContainers.Value;
				}
				return false;
			}
			if (ContainsAny(text, "grass", "debris", "rock", "stone", "pebble", "bush", "foliage", "dirt", "shell", "casing", "leaf", "branch"))
			{
				if (EnableDebris != null)
				{
					return EnableDebris.Value;
				}
				return false;
			}
			if (ContainsAny(text, "wall", "concrete", "brick", "pillar", "column", "bunker", "building", "structure", "foundation", "roof", "trench", "embrasure", "ladder", "sandbag", "house", "home", "barn", "shed", "garage", "church", "shop", "warehouse", "factory", "mill", "station", "hut", "cabin"))
			{
				if (EnableBuildingParts != null)
				{
					return EnableBuildingParts.Value;
				}
				return false;
			}
			if (EnableMiscProps != null)
			{
				return EnableMiscProps.Value;
			}
			return false;
		}

		private static bool ContainsAny(string text, params string[] keys)
		{
			foreach (string value in keys)
			{
				if (text.Contains(value))
				{
					return true;
				}
			}
			return false;
		}

		internal static string LodGroupBase(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return name ?? "";
			}
			string text = name.ToLowerInvariant();
			for (int num = text.IndexOf("lod", StringComparison.Ordinal); num >= 0; num = text.IndexOf("lod", num + 3, StringComparison.Ordinal))
			{
				bool num2 = num == 0 || text[num - 1] == '_' || text[num - 1] == '.';
				bool flag = num + 3 >= text.Length || char.IsDigit(text[num + 3]);
				if (num2 && flag)
				{
					return name.Substring(0, num).TrimEnd('_', '.', ' ');
				}
			}
			return name;
		}

		internal static bool IsLodNode(string name)
		{
			return LodGroupBase(name) != name;
		}

		internal static Rigidbody GetOrAddRigidbody(GameObject go, bool attachDebris = true)
		{
			if ((Object)(object)go == (Object)null)
			{
				return null;
			}
			ModLog.LogInfo((object)("MP: GAR enter '" + ((Object)go).name + "'"));
			if (!IsPhysicalizable(go.transform))
			{
				ModLog.LogInfo((object)("MP: GAR '" + ((Object)go).name + "' -> not physicalizable"));
				return null;
			}
			if (IsOversizedCollider(go))
			{
				ModLog.LogInfo((object)("MP: GAR '" + ((Object)go).name + "' -> oversized collider"));
				return null;
			}
			Rigidbody componentInParent = go.GetComponentInParent<Rigidbody>();
			if ((Object)(object)componentInParent != (Object)null)
			{
				if (!IsSceneObject(((Component)componentInParent).transform))
				{
					ModLog.LogInfo((object)("MP: GAR '" + ((Object)go).name + "' -> BLOCKED unit rb '" + ((Object)((Component)componentInParent).transform).name + "'"));
					return null;
				}
				if (!IsOversizedStructure(((Component)componentInParent).transform))
				{
					componentInParent.isKinematic = false;
					componentInParent.useGravity = true;
					UnstaticRecursive(((Component)componentInParent).transform);
					EnsureSolidCollider(((Component)componentInParent).transform);
					if (attachDebris)
					{
						AttachPhysicsDebris(((Component)componentInParent).gameObject);
					}
					RegisterPhysicalized(((Component)componentInParent).transform);
					ModLog.LogInfo((object)("MP: GAR '" + ((Object)go).name + "' -> UNLOCKED rb on '" + ((Object)((Component)componentInParent).transform).name + "'"));
					return componentInParent;
				}
				LogQuiet("MorePhysics: skipped oversized structure '" + ((Object)((Component)componentInParent).transform).name + "'.");
				return null;
			}
			Transform val = go.transform;
			Transform val2 = go.transform;
			while ((Object)(object)val2.parent != (Object)null)
			{
				Transform parent = val2.parent;
				if (IsSpawnerName(((Object)parent).name) || (!IsLodNode(((Object)val2).name) && (IsContainerName(((Object)parent).name) || ((Object)(object)((Component)parent).GetComponent<Collider>() == (Object)null && (Object)(object)((Component)parent).GetComponent<Renderer>() == (Object)null) || (Object)(object)((Component)parent).GetComponent<Rigidbody>() != (Object)null)))
				{
					break;
				}
				val = parent;
				val2 = parent;
			}
			if (IsSpawnerName(((Object)val).name))
			{
				val = go.transform;
			}
			if (IsOversizedStructure(val))
			{
				LogQuiet("MorePhysics: skipped oversized structure '" + ((Object)val).name + "'.");
				return null;
			}
			if (!IsSceneObject(val))
			{
				ModLog.LogInfo((object)("MP: GAR '" + ((Object)go).name + "' -> BLOCKED unit anchor '" + ((Object)val).name + "'"));
				return null;
			}
			Rigidbody val3 = ((Component)val).GetComponent<Rigidbody>();
			if ((Object)(object)val3 == (Object)null)
			{
				val3 = ((Component)val).gameObject.AddComponent<Rigidbody>();
			}
			val3.isKinematic = false;
			val3.useGravity = true;
			UnstaticRecursive(val);
			EnsureSolidCollider(val);
			if (attachDebris)
			{
				AttachPhysicsDebris(((Component)val).gameObject);
			}
			RegisterPhysicalized(val);
			ModLog.LogInfo((object)("MP: GAR '" + ((Object)go).name + "' -> ADDED rb on '" + ((Object)val).name + "'"));
			return val3;
		}

		internal unsafe static void EnsureSolidCollider(Transform anchor)
		{
			try
			{
				if ((Object)(object)anchor == (Object)null)
				{
					return;
				}
				Collider[] array = ((Collider[])((Component)anchor).GetComponentsInChildren<Collider>(true));
				bool flag = false;
				for (int i = 0; i < array.Length; i++)
				{
					if ((Object)(object)array[i] != (Object)null && !array[i].isTrigger && array[i].enabled)
					{
						flag = true;
						break;
					}
				}
				if (flag)
				{
					for (int j = 0; j < array.Length; j++)
					{
						if ((Object)(object)array[j] != (Object)null && array[j].isTrigger)
						{
							array[j].enabled = false;
						}
					}
					return;
				}
				for (int k = 0; k < array.Length; k++)
				{
					if ((Object)(object)array[k] != (Object)null)
					{
						array[k].enabled = false;
					}
				}
				if (!RefitWithMeshBoxes(anchor))
				{
					Bounds lb = ComputeAnchorLocalBounds(anchor);
					Vector3 size = lb.size;
					if (!(size.sqrMagnitude <= 0.0001f))
					{
						AddLocalBox(anchor, lb);
						ManualLogSource modLog = ModLog;
						string name = ((Object)anchor).name;
						size = lb.size;
						modLog.LogInfo((object)("MP: no native solid collider, added fitted box on '" + name + "' size=" + ((object)(*(Vector3*)(&size))/*cast due to constrained. prefix*/).ToString()));
					}
				}
			}
			catch (Exception ex)
			{
				ModLog.LogWarning((object)("MorePhysics ensure-solid failed: " + ex.Message));
			}
		}

		private static bool RefitWithMeshBoxes(Transform anchor)
		{
			try
			{
				MeshFilter[] array = ((MeshFilter[])((Component)anchor).GetComponentsInChildren<MeshFilter>(true));
				if (array == null || array.Length == 0)
				{
					return false;
				}
				Collider[] array2 = ((Collider[])((Component)anchor).GetComponentsInChildren<Collider>(true));
				int num = 0;
				Vector3[] array3 = (Vector3[])(object)new Vector3[8];
				Bounds val2 = default(Bounds);
				for (int i = 0; i < 2; i++)
				{
					if (num >= 12)
					{
						break;
					}
					for (int j = 0; j < array.Length; j++)
					{
						if (num >= 12)
						{
							break;
						}
						MeshFilter val = array[j];
						if ((Object)(object)val == (Object)null || (Object)(object)((Component)val).transform == (Object)null || (i == 0 && IsCoarseLod(((Component)val).transform)))
						{
							continue;
						}
						Mesh sharedMesh = val.sharedMesh;
						if ((Object)(object)sharedMesh == (Object)null)
						{
							continue;
						}
						Renderer component = ((Component)val).GetComponent<Renderer>();
						if ((Object)(object)component == (Object)null || !component.enabled || !((Component)component).gameObject.activeInHierarchy)
						{
							continue;
						}
						Bounds bounds = sharedMesh.bounds;
						Vector3 size = bounds.size;
						if (!(size.sqrMagnitude <= 0.0001f) && !(Mathf.Max(bounds.size.x, Mathf.Max(bounds.size.y, bounds.size.z)) < 0.1f))
						{
							Bounds bounds2 = component.bounds;
							Vector3 center = bounds2.center;
							Vector3 extents = bounds2.extents;
							array3[0] = anchor.InverseTransformPoint(center + new Vector3(0f - extents.x, 0f - extents.y, 0f - extents.z));
							array3[1] = anchor.InverseTransformPoint(center + new Vector3(extents.x, 0f - extents.y, 0f - extents.z));
							array3[2] = anchor.InverseTransformPoint(center + new Vector3(0f - extents.x, extents.y, 0f - extents.z));
							array3[3] = anchor.InverseTransformPoint(center + new Vector3(extents.x, extents.y, 0f - extents.z));
							array3[4] = anchor.InverseTransformPoint(center + new Vector3(0f - extents.x, 0f - extents.y, extents.z));
							array3[5] = anchor.InverseTransformPoint(center + new Vector3(extents.x, 0f - extents.y, extents.z));
							array3[6] = anchor.InverseTransformPoint(center + new Vector3(0f - extents.x, extents.y, extents.z));
							array3[7] = anchor.InverseTransformPoint(center + new Vector3(extents.x, extents.y, extents.z));
							val2 = new Bounds(array3[0], Vector3.zero);
							for (int k = 1; k < 8; k++)
							{
								val2.Encapsulate(array3[k]);
							}
							BoxCollider obj = ((Component)anchor).gameObject.AddComponent<BoxCollider>();
							((Collider)obj).isTrigger = false;
							Vector3 size2 = val2.size;
							size2.x = Mathf.Clamp(size2.x, 0.05f, 6f);
							size2.y = Mathf.Clamp(size2.y, 0.05f, 6f);
							size2.z = Mathf.Clamp(size2.z, 0.05f, 6f);
							obj.size = size2;
							obj.center = val2.center;
							num++;
						}
					}
				}
				if (num == 0)
				{
					return false;
				}
				for (int l = 0; l < array2.Length; l++)
				{
					if ((Object)(object)array2[l] != (Object)null)
					{
						array2[l].enabled = false;
					}
				}
				ModLog.LogInfo((object)("MP: refit '" + ((Object)anchor).name + "' with " + num + " per-mesh boxes (old " + array2.Length + " disabled)."));
				return true;
			}
			catch (Exception ex)
			{
				ModLog.LogWarning((object)("MorePhysics mesh-box refit failed: " + ex.Message));
				return false;
			}
		}

		private static void AddLocalBox(Transform anchor, Bounds lb)
		{
			BoxCollider obj = ((Component)anchor).gameObject.AddComponent<BoxCollider>();
			((Collider)obj).isTrigger = false;
			Vector3 size = lb.size;
			size.x = Mathf.Clamp(size.x, 0.2f, 8f);
			size.y = Mathf.Clamp(size.y, 0.2f, 8f);
			size.z = Mathf.Clamp(size.z, 0.2f, 8f);
			obj.size = size;
			obj.center = lb.center;
		}

		private static bool IsCoarseLod(Transform t)
		{
			try
			{
				if ((Object)(object)t == (Object)null)
				{
					return false;
				}
				string text = ((Object)t).name.ToLowerInvariant();
				if (string.IsNullOrEmpty(text))
				{
					return false;
				}
				if (text.Contains("far_") || text.Contains("impostor"))
				{
					return true;
				}
				for (int num = text.IndexOf("lod", StringComparison.Ordinal); num >= 0; num = text.IndexOf("lod", num + 3, StringComparison.Ordinal))
				{
					bool num2 = num == 0 || text[num - 1] == '_' || text[num - 1] == '.';
					bool flag = num + 3 < text.Length && char.IsDigit(text[num + 3]);
					if (num2 && flag)
					{
						return text[num + 3] - 48 >= 1;
					}
				}
				return false;
			}
			catch
			{
				return false;
			}
		}

		private static Renderer[] CollectFitRenderers(Transform anchor, bool includeInactive)
		{
			Renderer[] array = ((Renderer[])((Component)anchor).GetComponentsInChildren<Renderer>(includeInactive));
			Renderer[] array2 = (Renderer[])(object)new Renderer[array.Length];
			int num = 0;
			Renderer[] array3 = (Renderer[])(object)new Renderer[array.Length];
			int num2 = 0;
			foreach (Renderer val in array)
			{
				if (!((Object)(object)val == (Object)null) && !((Object)(object)((Component)val).transform == (Object)null))
				{
					array3[num2++] = val;
					if (!IsCoarseLod(((Component)val).transform))
					{
						array2[num++] = val;
					}
				}
			}
			if (num > 0)
			{
				Renderer[] array4 = (Renderer[])(object)new Renderer[num];
				Array.Copy(array2, array4, num);
				return array4;
			}
			Renderer[] array5 = (Renderer[])(object)new Renderer[num2];
			Array.Copy(array3, array5, num2);
			return array5;
		}

		internal static Bounds ComputeAnchorLocalBounds(Transform anchor)
		{
			Bounds result = default(Bounds);
			result = new Bounds(anchor.position, Vector3.zero);
			bool flag = false;
			Renderer[] array = CollectFitRenderers(anchor, includeInactive: true);
			int num = Mathf.Min(array.Length, 32);
			Vector3[] array2 = (Vector3[])(object)new Vector3[8];
			for (int i = 0; i < num; i++)
			{
				Renderer val = array[i];
				if (!((Object)(object)val == (Object)null) && val.enabled)
				{
					Bounds bounds = val.bounds;
					Vector3 center = bounds.center;
					Vector3 extents = bounds.extents;
					array2[0] = anchor.InverseTransformPoint(center + new Vector3(0f - extents.x, 0f - extents.y, 0f - extents.z));
					array2[1] = anchor.InverseTransformPoint(center + new Vector3(extents.x, 0f - extents.y, 0f - extents.z));
					array2[2] = anchor.InverseTransformPoint(center + new Vector3(0f - extents.x, extents.y, 0f - extents.z));
					array2[3] = anchor.InverseTransformPoint(center + new Vector3(extents.x, extents.y, 0f - extents.z));
					array2[4] = anchor.InverseTransformPoint(center + new Vector3(0f - extents.x, 0f - extents.y, extents.z));
					array2[5] = anchor.InverseTransformPoint(center + new Vector3(extents.x, 0f - extents.y, extents.z));
					array2[6] = anchor.InverseTransformPoint(center + new Vector3(0f - extents.x, extents.y, extents.z));
					array2[7] = anchor.InverseTransformPoint(center + new Vector3(extents.x, extents.y, extents.z));
					if (!flag)
					{
						result = new Bounds(array2[0], Vector3.zero);
						flag = true;
					}
					for (int j = 0; j < 8; j++)
					{
						result.Encapsulate(array2[j]);
					}
				}
			}
			if (!flag)
			{
				result = new Bounds(anchor.position, new Vector3(0.4f, 0.4f, 0.4f));
			}
			return result;
		}

		internal static Bounds ComputeAnchorBounds(Transform anchor)
		{
			Bounds bounds = default(Bounds);
			bounds = new Bounds(anchor.position, Vector3.zero);
			bool flag = false;
			Renderer[] array = ((Renderer[])((Component)anchor).GetComponentsInChildren<Renderer>(true));
			int num = Mathf.Min(array.Length, 32);
			for (int i = 0; i < num; i++)
			{
				if (!((Object)(object)array[i] == (Object)null))
				{
					if (!flag)
					{
						bounds = array[i].bounds;
						flag = true;
					}
					else
					{
						bounds.Encapsulate(array[i].bounds);
					}
				}
			}
			if (!flag)
			{
				Collider[] array2 = ((Collider[])((Component)anchor).GetComponentsInChildren<Collider>(true));
				int num2 = Mathf.Min(array2.Length, 32);
				for (int j = 0; j < num2; j++)
				{
					if (!((Object)(object)array2[j] == (Object)null))
					{
						if (!flag)
						{
							bounds = array2[j].bounds;
							flag = true;
						}
						else
						{
							bounds.Encapsulate(array2[j].bounds);
						}
					}
				}
			}
			return bounds;
		}

		internal unsafe static void DropItemsOnTop(Transform anchor)
		{
			try
			{
				if ((Object)(object)anchor == (Object)null)
				{
					return;
				}
				Bounds val = ComputeAnchorBounds(anchor);
				Vector3 val2 = val.size;
				if (val2.sqrMagnitude <= 0.0001f)
				{
					return;
				}
				ManualLogSource modLog = ModLog;
				string name = ((Object)anchor).name;
				val2 = val.size;
				modLog.LogInfo((object)("MP: drop-scan anchor='" + name + "' size=" + ((object)(*(Vector3*)(&val2))/*cast due to constrained. prefix*/).ToString()));
				int num = 0;
				HashSet<int> done = new HashSet<int>();
				HashSet<string> droppedBases = new HashSet<string>();
				string text = LodGroupBase(((Object)anchor).name);
				Bounds val3 = val;
				val3.Expand(0.2f);
				Vector3 center = val.center;
				val2 = val.extents;
				float num2 = Mathf.Clamp(val2.magnitude + 0.4f, 0.6f, 6f);
				Collider[] array = ((Collider[])(Il2CppArrayBase<Collider>)(object)Physics.OverlapSphere(center, num2));
				if (array != null)
				{
					for (int i = 0; i < array.Length; i++)
					{
						if (num >= 8)
						{
							break;
						}
						Collider val4 = array[i];
						if ((Object)(object)val4 == (Object)null || (Object)(object)((Component)val4).transform == (Object)null)
						{
							continue;
						}
						Transform transform = ((Component)val4).transform;
						if (!transform.IsChildOf(anchor) && !anchor.IsChildOf(transform) && !(LodGroupBase(((Object)transform).name) == text))
						{
							Bounds bounds = val4.bounds;
							if (bounds.Intersects(val3) && DropOne(transform, done, droppedBases))
							{
								num++;
							}
						}
					}
				}
				if (num >= 8 || !IsFurnitureName(((Object)anchor).name) || !(Time.time >= _nextDropScan))
				{
					return;
				}
				_nextDropScan = Time.time + 2f;
				Renderer[] array2 = ((Renderer[])Object.FindObjectsOfType<Renderer>());
				if (array2 == null)
				{
					return;
				}
				for (int j = 0; j < array2.Length; j++)
				{
					if (num >= 8)
					{
						break;
					}
					Renderer val5 = array2[j];
					if ((Object)(object)val5 == (Object)null || (Object)(object)((Component)val5).transform == (Object)null)
					{
						continue;
					}
					Transform transform2 = ((Component)val5).transform;
					if (!transform2.IsChildOf(anchor) && !anchor.IsChildOf(transform2) && !(LodGroupBase(((Object)transform2).name) == text))
					{
						Bounds bounds2 = val5.bounds;
						if (bounds2.Intersects(val3) && !(Mathf.Max(bounds2.extents.x, Mathf.Max(bounds2.extents.y, bounds2.extents.z)) > 1.5f) && DropOne(transform2, done, droppedBases))
						{
							num++;
						}
					}
				}
			}
			catch (Exception ex)
			{
				ModLog.LogWarning((object)("MorePhysics drop-on-top failed: " + ex.Message));
			}
		}

		private static bool IsFurnitureName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			return ContainsAny(name.ToLowerInvariant(), "table", "desk", "shelf", "counter", "bench", "wardrobe", "cabinet", "dresser", "nightstand");
		}

		private static bool DropOne(Transform t, HashSet<int> done, HashSet<string> droppedBases)
		{
			try
			{
				if ((Object)(object)t == (Object)null || (Object)(object)((Component)t).gameObject == (Object)null)
				{
					return false;
				}
				int instanceID = ((Object)((Component)t).gameObject).GetInstanceID();
				if (done.Contains(instanceID))
				{
					return false;
				}
				done.Add(instanceID);
				string text = LodGroupBase(((Object)t).name);
				if (droppedBases.Contains(text))
				{
					LogInfo("MP: drop-candidate '" + ((Object)t).name + "' -> LOD sibling of already-dropped base '" + text + "', skipped.");
					return false;
				}
				if (!IsSceneObject(t))
				{
					LogInfo("MP: drop-candidate '" + ((Object)t).name + "' -> not scene object, skipped.");
					return false;
				}
				if ((Object)(object)((Component)t).GetComponentInChildren<Renderer>(true) == (Object)null)
				{
					LogInfo("MP: drop-candidate '" + ((Object)t).name + "' -> no renderer, skipped.");
					return false;
				}
				if (!IsPhysicalizable(t))
				{
					LogInfo("MP: drop-candidate '" + ((Object)t).name + "' -> not physicalizable, skipped.");
					return false;
				}
				Bounds val = ComputeAnchorBounds(t);
				if (Mathf.Max(val.extents.x, Mathf.Max(val.extents.y, val.extents.z)) > 1.5f)
				{
					LogInfo("MP: drop-candidate '" + ((Object)t).name + "' -> too big (" + ((object)val.extents/*cast due to constrained. prefix*/).ToString() + "), skipped.");
					return false;
				}
				if (IsOversizedCollider(((Component)t).gameObject))
				{
					LogInfo("MP: drop-candidate '" + ((Object)t).name + "' -> oversized collider, skipped.");
					return false;
				}
				if ((Object)(object)((Component)t).GetComponentInParent<Rigidbody>() != (Object)null)
				{
					LogInfo("MP: drop-candidate '" + ((Object)t).name + "' has rb, skipped.");
					return false;
				}
				ItemObject componentInParent = ((Component)t).GetComponentInParent<ItemObject>();
				if ((Object)(object)componentInParent != (Object)null)
				{
					if (!componentInParent.IsPhysicEnabled())
					{
						componentInParent.EnablePhysic();
						LogQuiet("MorePhysics: dropped item '" + componentInParent.name + "'.");
						return true;
					}
					return false;
				}
				if ((Object)(object)GetOrAddRigidbody(((Component)t).gameObject, attachDebris: false) != (Object)null)
				{
					droppedBases.Add(text);
					LogInfo("MP: dropped prop '" + ((Object)t).name + "'.");
					return true;
				}
				LogInfo("MP: drop-candidate '" + ((Object)t).name + "' -> GetOrAddRigidbody null.");
				return false;
			}
			catch (Exception ex)
			{
				ModLog.LogWarning((object)("MorePhysics drop item failed: " + ex.Message));
				return false;
			}
		}

		private static void LogSeenOnce(int id, string msg)
		{
			if (seenDropSkips.Count > 256)
			{
				seenDropSkips.Clear();
			}
			if (seenDropSkips.Add(id))
			{
				LogQuiet(msg);
			}
		}

		internal static void NoteDamagedColliders(GameObject go)
		{
			try
			{
				if ((Object)(object)go == (Object)null)
				{
					return;
				}
				Collider[] array = ((Collider[])go.GetComponentsInChildren<Collider>(true));
				for (int i = 0; i < array.Length && i < 4; i++)
				{
					if (!((Object)(object)array[i] == (Object)null) && !array[i].isTrigger)
					{
						int instanceID = ((Object)((Component)array[i]).gameObject).GetInstanceID();
						if (!damagedColliders.ContainsKey(instanceID))
						{
							damagedColliders[instanceID] = array[i];
						}
					}
				}
				if (damagedColliders.Count > 256)
				{
					damagedColliders.Clear();
				}
			}
			catch
			{
			}
		}

		internal static void ReenableDamagedColliders()
		{
			if (damagedColliders.Count == 0)
			{
				return;
			}
			foreach (KeyValuePair<int, Collider> damagedCollider in damagedColliders)
			{
				Collider value = damagedCollider.Value;
				try
				{
					if (!((Object)(object)value == (Object)null) && !((Object)(object)((Component)value).gameObject == (Object)null) && !value.enabled)
					{
						value.enabled = true;
						LogQuiet("MorePhysics: re-enabled collider '" + ((Object)((Component)value).gameObject).name + "'.");
					}
				}
				catch
				{
				}
			}
		}

		private static void AttachPhysicsDebris(GameObject go)
		{
			try
			{
				if (!((Object)(object)go.GetComponentInParent<ItemObject>() != (Object)null))
				{
					PhysicsDebris physicsDebris = go.GetComponent<PhysicsDebris>();
					if ((Object)(object)physicsDebris == (Object)null)
					{
						physicsDebris = go.AddComponent<PhysicsDebris>();
					}
					physicsDebris.DespawnAfter = ((DespawnTime != null) ? DespawnTime.Value : 30f);
					physicsDebris.DamageOnHit = ((PhysicsDamage != null) ? PhysicsDamage.Value : 0f);
				}
			}
			catch (Exception ex)
			{
				ModLog.LogWarning((object)("MorePhysics attach debris failed: " + ex.Message));
			}
		}

		internal static bool IsExplosionOnly(Transform t)
		{
			if ((Object)(object)t == (Object)null)
			{
				return false;
			}
			string name = ((Object)t).name;
			if (string.IsNullOrEmpty(name))
			{
				return false;
			}
			return ContainsAny(name.ToLowerInvariant(), "wall", "concrete", "bunker", "building", "structure", "stone", "brick", "pillar", "column", "trench", "embrasure", "sandbag");
		}

		internal static float GetSceneHp(GameObject go)
		{
			int instanceID = ((Object)go).GetInstanceID();
			if (sceneHp.TryGetValue(instanceID, out var value))
			{
				return value;
			}
			value = ((DefaultHealth != null) ? DefaultHealth.Value : 40f);
			sceneHp[instanceID] = value;
			return value;
		}

		internal static bool DamageSceneObject(GameObject go, float damage, DamageSource source)
		{
			if ((Object)(object)go == (Object)null || !IsSceneObject(go.transform) || !IsPhysicalizable(go.transform))
			{
				return false;
			}
			if (IsOversizedCollider(go))
			{
				return false;
			}
			if ((Object)(object)go.GetComponent<ItemObject>() == (Object)null && EnableScenePhysics != null && !EnableScenePhysics.Value)
			{
				return false;
			}
			if (IsExplosionOnly(go.transform) && source != DamageSource.Explosion && ExplosionOnlyHardObjects != null && ExplosionOnlyHardObjects.Value)
			{
				return false;
			}
			NoteDamagedColliders(go);
			int instanceID = ((Object)go).GetInstanceID();
			float num = GetSceneHp(go);
			num -= damage;
			sceneHp[instanceID] = num;
			ModLog.LogInfo((object)("MP: damaged '" + ((Object)go).name + "' hp=" + num + " dmg=" + damage + " src=" + source));
			if (sceneHp.Count > 2048)
			{
				sceneHp.Clear();
			}
			return num <= 0f;
		}

		internal static void UnstaticRecursive(Transform t)
		{
			if ((Object)(object)t == (Object)null)
			{
				return;
			}
			try
			{
				((Component)t).gameObject.isStatic = false;
			}
			catch
			{
			}
			for (int i = 0; i < t.childCount; i++)
			{
				Transform child = t.GetChild(i);
				if ((Object)(object)child != (Object)null)
				{
					UnstaticRecursive(child);
				}
			}
		}

		private static bool IsContainerName(string name)
		{
			if (string.IsNullOrEmpty(name))
			{
				return true;
			}
			string text = name.ToLowerInvariant();
			switch (text)
			{
			case "objects":
			case "environment":
			case "map":
			case "level":
			case "scene":
			case "world":
				return true;
			default:
				if (!text.Contains("container") && !text.Contains("root"))
				{
					return text.Contains("parent");
				}
				return true;
			}
		}

		internal static bool IsSpawnerName(string name)
		{
			if (!string.IsNullOrEmpty(name))
			{
				return name.ToLowerInvariant().Contains("spawner");
			}
			return false;
		}

		internal static void LogQuiet(string msg)
		{
			if (!(Time.time < _nextLog))
			{
				_nextLog = Time.time + 2f;
				ModLog.LogInfo((object)msg);
			}
		}

		internal static void LogInfo(string msg)
		{
			ModLog.LogInfo((object)msg);
		}

		internal static void DiagnoseBlocked(GameObject go, DamageSource source)
		{
			if ((Object)(object)go == (Object)null || (Object)(object)go.transform == (Object)null)
			{
				return;
			}
			string text = null;
			Transform transform = go.transform;
			try
			{
				if (!IsSceneObject(transform))
				{
					text = "not-scene-object";
				}
				else if (!IsPhysicalizable(transform))
				{
					text = "not-physicalizable";
				}
				else if (IsOversizedCollider(go))
				{
					text = "oversized-collider";
				}
				else if ((Object)(object)go.GetComponent<ItemObject>() == (Object)null && EnableScenePhysics != null && !EnableScenePhysics.Value)
				{
					text = "scene-physics-off";
				}
				else if (IsExplosionOnly(transform) && source != DamageSource.Explosion && ExplosionOnlyHardObjects != null && ExplosionOnlyHardObjects.Value)
				{
					text = "explosion-only";
				}
				if (text == null)
				{
					return;
				}
				int instanceID = ((Object)go).GetInstanceID();
				if (!_diagTimes.TryGetValue(instanceID, out var value) || !(Time.time - value < 5f))
				{
					if (_diagTimes.Count > 512)
					{
						_diagTimes.Clear();
					}
					_diagTimes[instanceID] = Time.time;
					Collider component = go.GetComponent<Collider>();
					object obj2;
					if (!((Object)(object)component == (Object)null))
					{
						string[] obj = new string[5]
						{
							((object)component).GetType().Name,
							" trigger=",
							component.isTrigger.ToString(),
							" ext=",
							null
						};
						Bounds bounds = component.bounds;
						obj[4] = ((object)bounds.extents/*cast due to constrained. prefix*/).ToString();
						obj2 = string.Concat(obj);
					}
					else
					{
						obj2 = "none";
					}
					string text2 = (string)obj2;
					ModLog.LogInfo((object)("MorePhysics diag: '" + ((Object)go).name + "' blocked(" + text + ") collider=" + text2 + " layer=" + ((Component)transform).gameObject.layer + " parent=" + (((Object)(object)transform.parent != (Object)null) ? ((Object)transform.parent).name : "-")));
				}
			}
			catch
			{
			}
		}
	}
	[HarmonyPatch(typeof(TargetPractice), "OnHitted")]
	internal static class TargetPracticePatch
	{
		private static bool Prefix()
		{
			try
			{
				if (Plugin.GateBlocked())
				{
					return true;
				}
				if (Plugin.TargetPracticeOverride != null && !Plugin.TargetPracticeOverride.Value)
				{
					return true;
				}
				return false;
			}
			catch
			{
				return true;
			}
		}
	}
	internal static class UnitCollision
	{
		private const int BodyPartLayer = 9; // 受击碰撞体层（贴合人体）

		private const int CorpseLayerFallback = 10; // 尸体层兜底（拿不到 RagdollManager.ragdollizedLayer 时）

		private static int _ccLayer = -1;

		// 兜底层锁定标记：战斗开局单位未生成时探不到 CC，会锁进固定层 1；
		// 锁定后必须保持重探，拿到真实 CC 层要能改判（层矩阵开错对 = 玩家照样穿人）
		private static bool _fallbackLocked;

		// 本 mod 打开过的矩阵对标记：开关关掉时据此恢复成游戏默认的 ignore 状态。
		// 物理矩阵是进程级状态，只开不关 = "关闭"永远不生效（热开关必修）
		private static bool _unitMatrixOpen;

		private static bool _corpseMatrixOpen;

		private static int _warned;

		private static float _nextApply;

		private static float _lastMatrixLog;

		private static readonly HashSet<int> _pusherGos = new HashSet<int>();

		private static float _lastProbeLog;

		private static float _lastCorpseLog;

		private static float _lastLivingDump;

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
				// GateBlocked 折叠进来而不是在 tick 先拦：总开关关掉后 Apply 仍要进来，
				// "开→关"的矩阵恢复才有机会执行
				bool blocked = Plugin.GateBlocked();
				bool unitColl = !blocked && Plugin.UnitCollision != null && Plugin.UnitCollision.Value;
				bool pushCorpses = !blocked && Plugin.PushCorpses != null && Plugin.PushCorpses.Value;
				bool diag = !blocked && Plugin.UnitCollisionLayer != null && Plugin.UnitCollisionLayer.Value == -2;
				// 矩阵恢复：本 mod 打开过的对，开关一关立即还原成游戏默认 ignore 状态
				// （游戏自身从不改矩阵，恢复是安全的）
				if (_ccLayer >= 0 && (_unitMatrixOpen || _corpseMatrixOpen))
				{
					if (_unitMatrixOpen && !unitColl)
					{
						Physics.IgnoreLayerCollision(_ccLayer, 9, true);
						_unitMatrixOpen = false;
						Plugin.ModLog.LogInfo((object)("MP: unit matrix restored (ccLayer=" + _ccLayer + " bodyPartLayer=9 ignore=True)."));
					}
					if (_corpseMatrixOpen && !pushCorpses)
					{
						int corpseLayerR = GetCorpseLayer();
						if (corpseLayerR >= 0 && corpseLayerR != _ccLayer)
						{
							Physics.IgnoreLayerCollision(_ccLayer, corpseLayerR, true);
						}
						_corpseMatrixOpen = false;
						Plugin.ModLog.LogInfo((object)("MP: corpse matrix restored (ccLayer=" + _ccLayer + " corpseLayer=" + corpseLayerR + " ignore=True)."));
					}
				}
				// 位置级防重叠/推尸体：AI 走 NavMeshAgent（transform 直写）不走物理，
				// 碰撞矩阵只对玩家 CC 生效——AI 与 AI/玩家/尸体的交互必须每帧手动处理。
				// 活体-活体只挂 UnitCollision、AI-尸体只挂 PushCorpses（方法内细分）。
				if (unitColl || pushCorpses)
				{
					ResolveOverlaps(unitColl, pushCorpses);
				}
				if ((!unitColl && !diag && !pushCorpses) || Time.time < _nextApply)
				{
					return;
				}
				_nextApply = Time.time + 1f;
				if ((_ccLayer < 0 || _fallbackLocked) && !ProbeCcLayer())
				{
					return;
				}
				if (diag)
				{
					ProbeCcLayer();
					ProbeBodies();
					ProbeLivingColliders();
					return;
				}
				bool changed = false;
				if (unitColl && Physics.GetIgnoreLayerCollision(_ccLayer, 9))
				{
					Physics.IgnoreLayerCollision(_ccLayer, 9, false);
					_unitMatrixOpen = true;
					changed = true;
				}
				int corpseLayer = GetCorpseLayer();
				if (pushCorpses && corpseLayer >= 0 && corpseLayer != _ccLayer && Physics.GetIgnoreLayerCollision(_ccLayer, corpseLayer))
				{
					Physics.IgnoreLayerCollision(_ccLayer, corpseLayer, false);
					_corpseMatrixOpen = true;
					changed = true;
				}
				if (changed && Time.time - _lastMatrixLog >= 10f)
				{
					_lastMatrixLog = Time.time;
					Plugin.ModLog.LogInfo((object)("MP: unit-collision ON ccLayer=" + _ccLayer + " bodyPartLayer=" + 9 + " corpseLayer=" + corpseLayer + " changed=" + changed));
				}
				if (pushCorpses)
				{
					EnsureCorpsePhysics();
					EnsurePushers();
				}
				PushItemsNearAI();
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
					_fallbackLocked = false;
				}
				else if (anyAiHasCc)
				{
					_ccLayer = aiLayer;
					_fallbackLocked = false;
				}
				else if (_ccLayer < 0)
				{
					_ccLayer = 1;
					_fallbackLocked = true;
				}
				if (log)
				{
					Plugin.ModLog.LogInfo((object)("MP: unit-matrix playerCcLayer=" + playerLayer + " aiCcLayer=" + aiLayer + " ccLayer=" + _ccLayer + " bodyPartLayer=" + 9 + " ignore=" + ((_ccLayer >= 0) ? Physics.GetIgnoreLayerCollision(_ccLayer, 9).ToString() : "n/a")));
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
					Plugin.ModLog.LogInfo((object)("MP: corpse-diagnostics no ragdoll manager; layer=" + corpseLayer));
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
					Plugin.ModLog.LogInfo((object)("MP: corpse-rb '" + rb.gameObject.name + "' kin=" + rb.isKinematic + " mass=" + rb.mass + " layer=" + rb.gameObject.layer + " colliders=" + enabled + "/" + cols.Length));
					shown++;
				}
				Plugin.ModLog.LogInfo((object)("MP: corpse-diagnostics ragdollizedLayer=" + corpseLayer + " ccLayer=" + _ccLayer + " ignoreCcVsCorpse=" + ((_ccLayer >= 0 && corpseLayer >= 0) ? Physics.GetIgnoreLayerCollision(_ccLayer, corpseLayer).ToString() : "n/a") + " pushers=" + _pusherGos.Count));
			}
			catch (Exception ex)
			{
				Plugin.ModLog.LogWarning((object)("UnitCollision corpse diagnostics failed: " + ex.Message));
			}
		}

		// 诊断（2.1 穿人排查）：dump 活体士兵的 CC 与受击碰撞体实际分层/启用/触发状态。
		// 层矩阵开在 (ccLayer,9) 只在「AI 受击碰撞体确实在 9 层、非 trigger、已启用」时挡人；
		// 任何一条不满足玩家就照样穿过——这里把三条事实直接打出来，不靠猜。
		private static void ProbeLivingColliders()
		{
			try
			{
				if (Time.time - _lastLivingDump < 10f)
				{
					return;
				}
				_lastLivingDump = Time.time;
				Il2CppSystem.Collections.Generic.List<Creature> alive = Creature.aliveCreatures;
				if (alive == null)
				{
					return;
				}
				int dumped = 0;
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
						bool ragdolled = false;
						try
						{
							ragdolled = c.ragdoll_manager != null && c.ragdoll_manager.ragdollized;
						}
						catch
						{
						}
						if (ragdolled)
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
						if (!isPlayer && dumped >= 3)
						{
							continue;
						}
						string ccInfo;
						try
						{
							CharacterController cc = s.m_controller;
							ccInfo = (cc != null)
								? ("cc(goLayer=" + cc.gameObject.layer + " enabled=" + cc.enabled + " r=" + cc.radius.ToString("F2") + ")")
								: "cc=null";
						}
						catch (Exception ex)
						{
							ccInfo = "cc-err:" + ex.Message;
						}
						Dictionary<string, int> agg = new Dictionary<string, int>();
						List<string> solid = new List<string>();
						Collider[] cols = s.GetComponentsInChildren<Collider>(true);
						int solidCount = 0;
						foreach (Collider col in cols)
						{
							if (col == null)
							{
								continue;
							}
							string key = "L" + col.gameObject.layer + "/trig=" + (col.isTrigger ? 1 : 0) + "/en=" + (col.enabled ? 1 : 0);
							if (agg.ContainsKey(key))
							{
								agg[key]++;
							}
							else
							{
								agg[key] = 1;
							}
							if (col.enabled && !col.isTrigger)
							{
								solidCount++;
								if (solid.Count < 4)
								{
									solid.Add(col.gameObject.name + "@L" + col.gameObject.layer);
								}
							}
						}
						List<string> parts = new List<string>();
						foreach (KeyValuePair<string, int> kv in agg)
						{
							parts.Add(kv.Key + "x" + kv.Value);
						}
						Plugin.ModLog.LogInfo((object)("MP: living-dump " + (isPlayer ? "PLAYER" : "AI") + " '" + s.gameObject.name + "' goLayer=" + s.gameObject.layer + " " + ccInfo + " colliders[" + cols.Length + "] " + string.Join("; ", parts) + " solid=" + solidCount + ((solid.Count > 0) ? (" e.g." + string.Join(",", solid)) : "")));
						dumped++;
						if (dumped >= 4)
						{
							break;
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
					Plugin.ModLog.LogInfo((object)("MP: corpse physics active (dynamic " + madeDynamic + ", colliders " + collidersEnabled + ")."));
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
		private static void ResolveOverlaps(bool unitColl, bool pushCorpses)
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
				// 尸体数据源 2/3 + 合并：只在 PushCorpses 开启时收集（活体-活体不需要尸体表）
				if (pushCorpses)
				{
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
							Plugin.ModLog.LogInfo((object)("MP: corpses via scene scan (" + _scannedCorpses.Count + "), aliveCreatures=" + _corpses.Count + "."));
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
				} // end if (pushCorpses) 尸体收集
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
				// 活体-活体防重叠（只挂 UnitCollision：PushCorpses 不应让士兵互推生效）
				if (unitColl)
				{
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
				}
				// AI-尸体：推尸体或把 AI 推出（只挂 PushCorpses）
				if (pushCorpses && _corpses.Count > 0)
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
					Plugin.ModLog.LogInfo((object)("MP: overlaps (living " + _living.Count + ", corpses " + _corpses.Count + " src " + _corpseScanSrc + ", pushed " + _overlapPushed + ", corpsePushes " + _corpsePushes + ", aiPairs " + _aiCorpsePairs + ")."));
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
		private static void PushItemsNearAI()
		{
			try
			{
				if (Plugin.PushPhysItems == null || !Plugin.PushPhysItems.Value)
				{
					return;
				}
				Il2CppSystem.Collections.Generic.List<Creature> aliveCreatures = Creature.aliveCreatures;
				if (aliveCreatures == null)
				{
					return;
				}
				List<Vector3> list = null;
				Il2CppSystem.Collections.Generic.List<Creature>.Enumerator enumerator = aliveCreatures.GetEnumerator();
				while (enumerator.MoveNext())
				{
					Creature current = enumerator.Current;
					try
					{
						if ((Object)(object)current == (Object)null || (Object)(object)((Component)current).transform == (Object)null)
						{
							continue;
						}
						Soldier val = ((Il2CppObjectBase)current).TryCast<Soldier>();
						if (!((Object)(object)val == (Object)null) && !((Creature)val).IsPlayer())
						{
							if (list == null)
							{
								list = new List<Vector3>();
							}
							list.Add(((Component)val).transform.position);
						}
					}
					catch
					{
					}
				}
				if (list == null || list.Count == 0)
				{
					return;
				}
				List<Transform> physProps = Plugin.PhysProps;
				Vector3 val4 = default(Vector3);
				for (int i = 0; i < physProps.Count; i++)
				{
					Transform val2 = physProps[i];
					if ((Object)(object)val2 == (Object)null || (Object)(object)((Component)val2).gameObject == (Object)null)
					{
						physProps.RemoveAt(i);
						i--;
						continue;
					}
					try
					{
						Rigidbody component = ((Component)val2).GetComponent<Rigidbody>();
						if ((Object)(object)component == (Object)null || component.isKinematic || (Object)(object)((Component)component).GetComponentInParent<RagdollManager>(true) != (Object)null || !Plugin.IsSceneObject(val2) || Plugin.IsNoiseLike(val2))
						{
							continue;
						}
						float num = Mathf.Clamp(component.mass, 1f, 20f);
						if (num > 40f)
						{
							continue;
						}
						Vector3 position = val2.position;
						for (int j = 0; j < list.Count; j++)
						{
							Vector3 val3 = position - list[j];
							float num2 = val3.x * val3.x + val3.z * val3.z;
							if (!(num2 > 1.69f))
							{
								float num3 = Mathf.Sqrt(num2);
								if (!(num3 < 0.01f))
								{
									val4 = new Vector3(val3.x / num3, 0f, val3.z / num3);
									float num4 = (1.3f - num3) * 6f * (0.4f + num * 0.03f);
									component.AddForce(val4 * num4, (ForceMode)1);
									component.WakeUp();
								}
								break;
							}
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
	}
}
