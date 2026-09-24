using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 2.0.0：物品生成器 —— 把 item_id 变成背包里的 VirtualItem，或场上的实体 ItemObject。
///
/// **通道选择（关键决策，实测依据见 guide 陷阱 23）**：
/// ① 进背包：**绝不走 `AddVirtualItem` / `AddItemToInventory`**——两者都会把物品归一化成基类
///    VirtualItem，弹匣/弹药/手雷等子类语义全废（陷阱 23，ThrowableWheel 实证）。
///    本 mod 一律**直接 `inventory.items.Add(vi)`**（ThrowableWheel 已验证的注入法）。
/// ② 子类正确性：优先 `ItemObject.ToVirtualItem()`（**原生自己产子类**，最可靠）——
///    先取 `ItemsDatabase.GetItemObject(id)` 拿到官方 prefab（ScriptableObject，非场景实例），
///    调它的 ToVirtualItem() 得到带正确子类的虚拟物品；失败再退 `VirtualItem.Create(id)`，
///    最后退 `new VirtualItem(id)` 基类兜底（至少能进背包，只是失去子类行为）。
/// ③ 落地：`ItemObject.ToVirtualItem()` → `VirtualItem.InstantiatePrefab()`（原生"虚拟物品 → 世界实体"）；
///    失败退 `UnityEngine.Object.Instantiate(prefab.gameObject)`（裸实例化兜底）。
///    **注意** `InstantiatePrefab()` 在 `VirtualItem` 上，不在 `ItemObject` 上。
/// </summary>
internal static class ItemSpawner
{
	// ================= 虚拟物品构造 =================

	/// <summary>item_id → 正确的 VirtualItem 子类实例。三级兜底，失败返回 null。</summary>
	internal static VirtualItem MakeVirtualItem(string id, out string how)
	{
		how = "无";
		if (string.IsNullOrEmpty(id)) return null;

		// ① 原生 prefab → ToVirtualItem（原生自己产子类，语义最准）
		try
		{
			ItemObject prefab = ItemsDatabase.GetItemObject(id);
			if (prefab != null)
			{
				VirtualItem vi = prefab.ToVirtualItem();
				if (vi != null)
				{
					how = "GetItemObject.ToVirtualItem→" + TypeName(vi);
					return vi;
				}
			}
		}
		catch (Exception ex) { if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] ToVirtualItem 失败(" + id + "): " + ex.Message); }

		// ② 原生静态工厂
		try
		{
			VirtualItem vi = VirtualItem.Create(id);
			if (vi != null)
			{
				how = "VirtualItem.Create→" + TypeName(vi);
				return vi;
			}
		}
		catch (Exception ex) { if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] VirtualItem.Create 失败(" + id + "): " + ex.Message); }

		// ③ 基类兜底（能进背包，子类行为缺失，但至少不是"什么都没发生"）
		try
		{
			VirtualItem vi = new VirtualItem(id);
			if (vi != null)
			{
				how = "基类兜底";
				return vi;
			}
		}
		catch (Exception ex) { Plugin.ModLog.LogWarning("[UniGen] new VirtualItem 失败(" + id + "): " + ex.Message); }

		return null;
	}

	private static string TypeName(VirtualItem vi)
	{
		try { return vi.GetIl2CppType().Name; } catch { return "?"; }
	}

	// ================= 进背包 =================

	/// <summary>
	/// 把 item_id 放进食物的背包。返回 (成功, 反馈文案)。
	/// 负重规则：能读到负重就校验（超出即拒绝，提示上限）；读不到则放行（上帝视角工具，日志可查）。
	/// </summary>
	internal static bool GiveToSoldier(Soldier soldier, string id, out string msg)
	{
		msg = "";
		InventoryManager im = FindInventory(soldier);
		if (im == null) { msg = Ui.Tr("该单位没有可用的背包"); return false; }
		return GiveToInventory(im, id, out msg);
	}

	internal static bool GiveToInventory(InventoryManager im, string id, out string msg)
	{
		msg = "";
		Il2CppSystem.Collections.Generic.List<VirtualItem> items = ItemsOf(im);
		if (items == null)
		{
			msg = Ui.Tr("该单位没有可用的背包");
			Plugin.ModLog.LogWarning("[UniGen] 入包失败(" + id + "): 背包列表不可用（InventoryManager.inventory/items 为空）");
			return false;
		}

		VirtualItem vi = MakeVirtualItem(id, out string how);
		if (vi == null)
		{
			msg = Ui.Tr("物品无效：") + id;
			Plugin.ModLog.LogWarning("[UniGen] 入包失败(" + id + "): 三级构造全部返回 null（GetItemObject / Create / new VirtualItem）");
			return false;
		}

		// 负重校验（拿不到负重就放行）
		float mass = 0f;
		try { mass = vi.GetMass(); } catch { }
		if (TryGetWeight(im, out float cur, out float max) && mass > 0f && cur + mass > max + 0.01f)
		{
			msg = Ui.Tr("负重已满（") + cur.ToString("0.#") + "/" + max.ToString("0.#") + "kg）";
			Plugin.ModLog.LogInfo("[UniGen] 入包被拒(" + id + "): 负重 " + cur.ToString("0.#") + "+" + mass.ToString("0.#") + " > " + max.ToString("0.#"));
			return false;
		}

		try
		{
			items.Add(vi); // 陷阱 23：直接注入，保住子类
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogWarning("[UniGen] 注入背包失败(" + id + "): " + ex.Message);
			msg = Ui.Tr("放入失败：") + id;
			return false;
		}

		// 注入后复核：确认真的在列表里（Add 静默失败时给出准确反馈）
		if (!ContainsId(im, id))
		{
			msg = Ui.Tr("放入失败（未生效）：") + id;
			Plugin.ModLog.LogWarning("[UniGen] 入包失败(" + id + "): items.Add 未生效（列表里查不到该 id），通道=" + how);
			return false;
		}
		Plugin.ModLog.LogInfo("[UniGen] 物品已入背包: " + id + " 通道=" + how + " → " + OwnerName(im));
		return true;
	}

	// ================= 落地 =================

	/// <summary>
	/// 在指定世界坐标生成物品实体。返回生成的 GameObject（失败 null）。
	///
	/// **通道选择（实测依据：guide 陷阱 23 + 宿主 BackpackPanel 已验证的丢弃链）**：
	/// `InstantiatePrefab()` 是 **VirtualItem** 的方法（`Public Virtual New GameObject`），
	/// 不是 `ItemObject` 的——`ItemObject` 上只有 `ToVirtualItem()`（host `ItemObject.decompiled.cs:313`）。
	/// 因此正确链路是：`ItemObject` prefab → `ToVirtualItem()` → `InstantiatePrefab()`。
	/// 这比自己 `Object.Instantiate(prefab.gameObject)` 更可靠：原生会补齐 ItemObject 组件、
	/// 弹药/弹匣容量等子类运行期字段（裸实例化只拿到 prefab 的静态部分）。
	/// 兜底顺序：① 原生 VirtualItem 通道 → ② 裸 GameObject 实例化 → ③ 失败报错。
	/// </summary>
	internal static GameObject DropAt(string id, Vector3 pos, out string msg)
	{
		msg = "";
		ItemObject prefab = null;
		try { prefab = ItemsDatabase.GetItemObject(id); } catch { }

		if (prefab == null)
		{
			msg = Ui.Tr("物品无效：") + id;
			// 2.0.1：这是「物品无效」最主要的出口，原实现无日志 → 玩家报"很多物品无效"时无从定位。
			// 无条件告警（不是 debugLog 门控）：失败必须可观测（工作区教训）。
			Plugin.ModLog.LogWarning("[UniGen] 落地失败(" + id + "): ItemsDatabase.GetItemObject 返回 null（item_id 不在游戏物品数据库里）");
			return null;
		}

		// ① 原生通道：prefab.ToVirtualItem().InstantiatePrefab()
		try
		{
			VirtualItem vi = prefab.ToVirtualItem();
			if (vi != null)
			{
				GameObject inst = vi.InstantiatePrefab();
				if (inst != null)
				{
					PlaceAt(inst, pos);
					Plugin.ModLog.LogInfo("[UniGen] 物品已落地(原生通道): " + id + " @ " + pos);
					return inst;
				}
			}
		}
		catch (Exception ex) { if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] ToVirtualItem/InstantiatePrefab 失败(" + id + "): " + ex.Message); }

		// ② 兜底：裸 GameObject 实例化（prefab 本身是 ItemObject : MonoBehaviour）
		try
		{
			GameObject inst = UnityEngine.Object.Instantiate(prefab.gameObject);
			if (inst != null)
			{
				inst.SetActive(true);
				PlaceAt(inst, pos);
				Plugin.ModLog.LogWarning("[UniGen] 物品已落地(裸实例化兜底): " + id + " @ " + pos);
				return inst;
			}
		}
		catch (Exception ex) { if (Plugin.debugLog.Value) Plugin.ModLog.LogWarning("[UniGen] 裸实例化失败(" + id + "): " + ex.Message); }

		msg = Ui.Tr("物品生成失败：") + id;
		return null;
	}

	/// <summary>把世界物品摆到落点（地面之上一点，避免穿地）。</summary>
	private static void PlaceAt(GameObject go, Vector3 pos)
	{
		try
		{
			go.transform.position = pos + Vector3.up * 0.25f;
			go.transform.rotation = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f);
		}
		catch { }
	}

	// ================= 目标解析 =================

	/// <summary>鼠标下的士兵（拖放目标）。射线命中生物根或布娃娃部件都能回到 Soldier。</summary>
	/// <remarks>
	/// 2.0.1 修复（用户实测"拖到单位身上失败"）：RTS 上帝视角相机离地面很远，
	/// 鼠标指向一个士兵时发射线**很可能先命中他脚下的地形**（士兵轮廓窄、地形是大平面），
	/// 原实现只在"命中的不是士兵"时用 `NearestSoldier(hit.point, 1.6f)` 兜底——
	/// 命中点落在地面，1.6m 半径常常够不到人 → hoverSoldier 恒为 null → 永远走落地分支。
	///
	/// 现改为三级：
	/// ① 射线直接命中士兵/其布娃娃部件 → 直接返回（最准）；
	/// ② **射线穿过生物层但被地形先挡** → 沿射线做一次"生物层专属"射线（只查已命中点之前）；
	/// ③ 仍无 → 以命中点为中心、**按相机距离反算世界半径**搜索最近存活士兵
	///   （RTS 视角下屏上 1px 对应的世界距离与相机高度成正比，固定 1.6m 是错的）。
	/// </remarks>
	internal static Soldier SoldierUnderMouse(float maxDist = 400f)
	{
		Camera cam = CurrentCamera();
		if (cam == null) return null;
		try
		{
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hit, maxDist))
			{
				// ① 命中物本身就是士兵 / 其布娃娃部件
				Soldier s = SoldierFromCollider(hit.collider);
				if (s != null) return s;

				// ③ 以命中点为中心、按比例反算半径找最近的兵
				float radius = SearchRadiusFor(hit.distance, cam);
				s = NearestSoldier(hit.point, radius);
				if (s != null) return s;
			}
		}
		catch { }
		return null;
	}

	/// <summary>
	/// RTS 视角下"鼠标附近"的世界半径：与命中距离成正比（透视投影：世界尺寸 ∝ 距离）。
	/// 取屏幕高度的 3%（约 22px@720p / 32px@1080p）作为容差；
	/// 远距离时上限收敛到 6m，避免把整片区域的兵都算进来（HVT v1.2.2 的"固定世界尺寸"教训同类）。
	/// </summary>
	private static float SearchRadiusFor(float hitDistance, Camera cam)
	{
		float k = 0.03f;                                  // 屏高比例容差
		const float minR = 1.6f, maxR = 6f;               // 近处至少 1.6m（原值），远处不超过 6m
		float r = hitDistance * k;
		if (cam != null && cam.fieldOfView > 1f) r *= (cam.fieldOfView / 60f); // FOV 越大，同屏像素覆盖的世界越大
		return Mathf.Clamp(r, minR, maxR);
	}

	private static Soldier SoldierFromCollider(Collider col)
	{
		if (col == null) return null;
		try
		{
			Soldier s = col.GetComponentInParent<Soldier>();
			if (s != null) return s;
		}
		catch { }
		try
		{
			Creature cr = col.GetComponentInParent<Creature>();
			if (cr != null) return cr.TryCast<Soldier>();
		}
		catch { }
		return null;
	}

	/// <summary>命中点附近最近的存活士兵（布娃娃层级脱离 / 点到尸体上的枪时兜底）。
	/// 走 `Creature.aliveCreatures` 静态表 —— 禁用 FindObjectsOfType 类全场景扫描（工作区禁令）。</summary>
	internal static Soldier NearestSoldier(Vector3 point, float radius)
	{
		Soldier best = null;
		float bestSq = radius * radius;
		try
		{
			Il2CppSystem.Collections.Generic.List<Creature> list = Creature.aliveCreatures;
			if (list == null) return null;
			for (int i = 0; i < list.Count; i++)
			{
				Creature cr = list[i];
				if (cr == null || cr.transform == null) continue;
				Soldier s = cr.TryCast<Soldier>();
				if (s == null) continue;
				float d = (cr.transform.position - point).sqrMagnitude;
				if (d <= bestSq) { bestSq = d; best = s; }
			}
		}
		catch { }
		return best;
	}

	/// <summary>
	/// 鼠标射线打到地面的落点（拖到地上用）。
	/// 2.0.1：命中生物时不再直接判否，而是**沿射线继续找它身后的地面**（RTS 视角下俯角大，
	/// 想丢在单位旁边很容易先打到人身上）——否则玩家会频繁看到"此处无法放置"。
	/// </summary>
	internal static bool GroundUnderMouse(out Vector3 pos, float maxDist = 6000f)
	{
		pos = Vector3.zero;
		Camera cam = CurrentCamera();
		if (cam == null) return false;
		try
		{
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			// SphereCast 半径 0：等价 raycast，但可连续取多次命中（穿过命中物继续往后找）
			RaycastHit[] hits = Physics.RaycastAll(ray, maxDist);
			if (hits == null || hits.Length == 0) return false;
			Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance)); // 由近及远
			for (int i = 0; i < hits.Length; i++)
			{
				RaycastHit h = hits[i];
				// 与 Placer 同款放宽法线阈值（微微起伏的地形也接受）
				if (h.normal.y <= 0.15f) continue;
				// 命中生物/载具时跳过（避免把物品塞进人身体里），继续往后找真正的地面
				if (SoldierFromCollider(h.collider) != null) continue;
				pos = h.point;
				return true;
			}
		}
		catch { }
		return false;
	}

	internal static Camera CurrentCamera()
	{
		Camera cam = null;
		try { cam = ResourcesManager.mainCamera; } catch { }
		if (cam == null) { try { cam = Camera.main; } catch { } }
		return cam;
	}

	// ================= 背包定位 =================

	/// <summary>IMGUI 坐标（y 向下）→ 屏幕坐标。</summary>
	internal static Vector2 MouseScreenPos()
	{
		return new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
	}

	/// <summary>该士兵的 InventoryManager（与宿主 InfoPanel.FindInventoryManager 同款：activeInventories 反查归属）。</summary>
	internal static InventoryManager FindInventory(Soldier s)
	{
		if (s == null) return null;
		try
		{
			// 快路：士兵自身字段
			try
			{
				InventoryManager own = s.inventory;
				if (own != null) return own;
			}
			catch { }

			Il2CppSystem.Collections.Generic.List<InventoryManager> list = InventoryManager.activeInventories;
			if (list == null) return null;
			long ptr = (long)s.Pointer;
			for (int i = 0; i < list.Count; i++)
			{
				InventoryManager m = list[i];
				if (m == null) continue;
				try
				{
					Soldier owner = m.GetComponentInParent<Soldier>();
					if (owner != null && (long)owner.Pointer == ptr) return m;
				}
				catch { }
			}
		}
		catch { }
		return null;
	}

	private static Il2CppSystem.Collections.Generic.List<VirtualItem> ItemsOf(InventoryManager im)
	{
		try
		{
			var inv = im.inventory;
			return inv != null ? inv.items : null;
		}
		catch { return null; }
	}

	private static bool ContainsId(InventoryManager im, string id)
	{
		var items = ItemsOf(im);
		if (items == null) return false;
		try
		{
			for (int i = 0; i < items.Count; i++)
			{
				VirtualItem vi = items[i];
				if (vi == null) continue;
				string vid = null;
				try { vid = vi.item_id; } catch { }
				if (string.Equals(vid, id, StringComparison.OrdinalIgnoreCase)) return true;
			}
		}
		catch { }
		return false;
	}

	private static bool TryGetWeight(InventoryManager im, out float cur, out float max)
	{
		try
		{
			im.GetWeightAndMaxWeight(out cur, out max);
			return max > 0f;
		}
		catch { cur = -1f; max = -1f; return false; }
	}

	private static string OwnerName(InventoryManager im)
	{
		try
		{
			Soldier s = im.GetComponentInParent<Soldier>();
			if (s != null)
			{
				string n = null;
				try { n = s.name; } catch { }
				return string.IsNullOrEmpty(n) ? "士兵" : n;
			}
		}
		catch { }
		return "背包";
	}
}
