using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 幽灵预览（半透明单位模型，表现阵型/掩体落位）。供本 mod 的阵型拖动与附属 mod 的放置预览复用。
///
/// 生命周期（1.2.15 收口）：
///   创建 → 池化复用（拖动中只建不毁）→ **任何结束路径都必须 ClearAll()**。
///   结束路径包括：正常下发（Formation.IssueFromDrag）、手势复位、松手在 UI 上、退出 RTS、
///   右键已松开的看门狗、拖动异常。此前只清 GodViewController 的手势标志、
///   没清 Formation.dragging/幽灵 → **取消长按后幽灵残留在场上**（用户反馈），现已全部收口。
///
/// 组件处理（1.2.15 性能）：原来 Ghostify/MakeGhost 各做 5~8 次 GetComponentsInChildren 遍历，
/// 现合并为**单次遍历**（`ProcessComponents`）。
/// </summary>
internal static class GhostPreview
{
	private const int MaxGhosts = 16;        // 单次拖动的幽灵总量上限
	private const int MaxNewPerApply = 6;    // 每次刷新最多新建几个（分摊 Instantiate 尖刺）
	private const int GhostPtrCap = 512;     // 幽灵指针表上限（异常路径下防无界增长）

	private static readonly Dictionary<long, GameObject> live = new Dictionary<long, GameObject>();
	private static readonly HashSet<long> wantedPtrs = new HashSet<long>(); // Apply 内复用，避免 O(n×m)
	/// <summary>全部幽灵（含附属 mod 的放置预览）的 Creature/Vehicle 指针——伤害免疫与地面射线豁免用。</summary>
	private static readonly HashSet<long> ghostPtrs = new HashSet<long>();
	private static Material ghostMat;
	private static bool cloneBroken;
	private static int failCount;

	internal static bool Enabled => Plugin.ghostPreview != null && Plugin.ghostPreview.Value && !cloneBroken;

	/// <summary>当前幽灵数量（诊断用）。</summary>
	internal static int LiveCount => live.Count;

	/// <summary>该对象是否幽灵（伤害免疫：预览被敌人打死过，用户反馈两次）。</summary>
	internal static bool IsGhost(Creature c)
	{
		try { return c != null && ghostPtrs.Contains((long)c.Pointer); } catch { return false; }
	}

	/// <summary>该 Transform 是否属于幽灵（相机地面射线必须忽略幽灵，
	/// 否则幽灵跑到相机下方会把地面高度抬高 → 相机被"顶"着持续上升）。</summary>
	internal static bool IsGhostTransform(Transform t)
	{
		try
		{
			if (t == null) return false;
			for (Transform cur = t; cur != null; cur = cur.parent)
			{
				string n = cur.name;
				if (n != null && (n.StartsWith("ER2Ghost_", StringComparison.Ordinal) || n.StartsWith("UniGenPreview_", StringComparison.Ordinal)))
					return true;
			}
		}
		catch { }
		return false;
	}

	// ══════════════════════════════════════════════════════════
	// 组件处理（单次遍历）
	// ══════════════════════════════════════════════════════════

	/// <summary>
	/// 1.2.16：幽灵组件处理。
	/// ① **碰撞体/物理/行为/相机**走一次 `Component[]` 遍历；
	/// ② **渲染器材质**单独用强类型 `GetComponentsInChildren<Renderer>()` —— 1.2.15 把材质也塞进
	///    Component 遍历里，实测"幽灵变回原色"（`c is Renderer` 未命中），故改回已验证的强类型写法。
	/// </summary>
	/// <summary>
	/// 1.2.17：幽灵组件处理——**全部改回强类型遍历**。
	/// 1.2.15 曾合并为一次 Component[] 遍历 + is 判别，实测 IL2CPP 下对
	/// Collider/Rigidbody/Renderer 均**不可靠**（命中不了）→ 幽灵带原材质/碰撞/物理，
	/// 连带"视角乱飞""有碰撞可交互""附属 mod 预览不显示"。
	/// 教训：IL2CPP interop 下对 GetComponentsInChildren&lt;Component&gt;() 的元素做 is 派生类
	/// 判别不可靠，**必须用强类型 GetComponentsInChildren&lt;T&gt;()**。
	/// </summary>
	private static void ProcessComponents(GameObject g)
	{
		// ① 相机/灯：直接销毁（残留会劫持渲染视角）
		Camera[] cams = g.GetComponentsInChildren<Camera>(true);
		if (cams != null) for (int i = 0; i < cams.Length; i++) { try { if (cams[i] != null) UnityEngine.Object.Destroy(cams[i]); } catch { } }
		Light[] ls = g.GetComponentsInChildren<Light>(true);
		if (ls != null) for (int i = 0; i < ls.Length; i++) { try { if (ls[i] != null) UnityEngine.Object.Destroy(ls[i]); } catch { } }

		// ② 物理：Rigidbody/Joint 不是 Behaviour，必须单独处理（否则骨架被物理拉扯）
		Rigidbody[] rbs = g.GetComponentsInChildren<Rigidbody>(true);
		if (rbs != null)
			for (int i = 0; i < rbs.Length; i++)
			{
				Rigidbody rb = rbs[i];
				if (rb == null) continue;
				try
				{
					rb.isKinematic = true;
					rb.useGravity = false;
					rb.constraints = RigidbodyConstraints.FreezeAll;
					rb.velocity = Vector3.zero;
					rb.angularVelocity = Vector3.zero;
					rb.detectCollisions = false;
				}
				catch { }
			}
		Joint[] js = g.GetComponentsInChildren<Joint>(true);
		if (js != null)
			for (int i = 0; i < js.Length; i++)
			{
				Joint j = js[i];
				if (j == null) continue;
				try { j.enablePreprocessing = false; j.breakForce = 0f; j.breakTorque = 0f; } catch { }
			}

		// ③ 碰撞体：全部停用（含 CharacterController——它派生自 Collider）
		Collider[] cols = g.GetComponentsInChildren<Collider>(true);
		if (cols != null)
			for (int i = 0; i < cols.Length; i++) { try { if (cols[i] != null) cols[i].enabled = false; } catch { } }

		// ④ 行为：停用（保留 Renderer/Animator；AudioListener 按类型名销毁）
		Behaviour[] bs = g.GetComponentsInChildren<Behaviour>(true);
		if (bs != null)
			for (int i = 0; i < bs.Length; i++)
			{
				Behaviour b = bs[i];
				if (b == null) continue;
				try
				{
					if (b is Renderer || b is Animator) continue;
					if (b.GetType().Name == "AudioListener") { UnityEngine.Object.Destroy(b); continue; }
					b.enabled = false;
				}
				catch { }
			}

		// ⑤ 渲染：换幽灵材质 + 关阴影
		Renderer[] rs = g.GetComponentsInChildren<Renderer>(true);
		if (rs != null)
			for (int i = 0; i < rs.Length; i++)
			{
				Renderer r = rs[i];
				if (r == null) continue;
				try
				{
					if (ghostMat != null) r.sharedMaterial = ghostMat;
					r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
					r.receiveShadows = false;
				}
				catch { }
			}
	}

	/// <summary>
	/// 把任意 GameObject 就地"幽灵化"（供附属 mod 的放置预览复用同一套视觉）。
	/// 返回 false 表示材质不可用（调用方应回退自己的预览）。**单向操作**，用完直接 Destroy。
	/// </summary>
	internal static bool Ghostify(GameObject g)
	{
		try
		{
			if (g == null) return false;
			EnsureMat();
			if (ghostMat == null) return false;
			ProcessComponents(g);
			return true;
		}
		catch (Exception ex) { SquadCmdLogic.Log("[Ghost] Ghostify 失败: " + ex.Message); return false; }
	}

	private static int collidersOff; // 诊断用：本次处理停用了几个碰撞体
	private static string matShaderName = "";
	private static bool matDiagLogged;

	/// <summary>1.2.16：多 shader 兜底 + **无条件一次性诊断**。
	/// 此前创建失败走 `SquadCmdLogic.Log`（受 debugLog 门控）→ 静默失败，
	/// 表现为"幽灵不是灰色半透明而是原色"（材质没换上）与"附属 mod 预览不显示"（Ghostify 直接返回 false）。</summary>
	private static void EnsureMat()
	{
		if (ghostMat != null) return;
		// 候选按优先级：内置管线精灵/透明系 → URP Unlit → 顶点色
		// 1.4.32：**顺序重排**。`Sprites/Default` 的片元是 `tex × IN.color`——它读**顶点色**，
		// 而幽灵是 Mesh（没有顶点色，等价于白）→ `ghostMat.color/_Color` **完全不起作用**，
		// 渲染出来是"实心亮白"（用户截图实证）。
		// `Particles/Standard Unlit` 是 unlit + **读 `_Color`** + 支持 alpha → 放在首位。
		string[] cands = {
			"Particles/Standard Unlit",
			"Legacy Shaders/Transparent/Diffuse",
			"Unlit/Transparent",
			"Sprites/Default",                       // 兜底：只在有顶点色的物体上表现正确
			"Universal Render Pipeline/Unlit",
			"Hidden/Internal-Colored",               // 最后兜底（不透明，仅保证"看得见轮廓"）
		};
		try
		{
			Shader sh = null;
			foreach (string n in cands)
			{
				try { sh = Shader.Find(n); } catch { sh = null; }
				if (sh != null) { matShaderName = n; break; }
			}
			if (sh == null)
			{
				cloneBroken = true;
				SquadCmdLogic.LogAlways("[Ghost] 幽灵材质创建失败：全部候选 shader 都找不到，已降级为纯标记预览");
				return;
			}
			ghostMat = new Material(sh);
			// 1.4.32：Particles/Standard Unlit 默认 `_Mode = 0`（Opaque）→ 必须显式切到 **Fade**，
			// 否则 alpha 一样不生效。这一组设置对命中的多数 shader 无害（属性名不存在时静默忽略）。
			try
			{
				ghostMat.SetFloat("_Mode", 2f);                     // 2 = Fade
				ghostMat.SetOverrideTag("RenderType", "Transparent");
				ghostMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
				ghostMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
				ghostMat.SetInt("_ZWrite", 0);
				ghostMat.SetFloat("_Surface", 1f);                  // URP 用语：Transparent
				ghostMat.DisableKeyword("_ALPHATEST_ON");
				ghostMat.EnableKeyword("_ALPHABLEND_ON");
				ghostMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
				ghostMat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
				ghostMat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
			}
			catch { }
			ghostMat.color = ER2Shared.Er2Ui.WGhost;
			try { ghostMat.SetColor("_Color", ghostMat.color); } catch { }
			ghostMat.hideFlags = (HideFlags)61; // 陷阱 12：运行时创建的资源防场景卸载
		}
		catch (Exception ex)
		{
			cloneBroken = true;
			SquadCmdLogic.LogAlways("[Ghost] 幽灵材质创建异常，已降级为纯标记预览: " + ex.Message);
		}
	}

	// ══════════════════════════════════════════════════════════
	// 池化与刷新
	// ══════════════════════════════════════════════════════════

	/// <summary>按掩体分配计划刷新幽灵集合（拖动中限频调用）。计划外的幽灵隐藏待复用，不销毁。</summary>
	internal static void Apply(List<Formation.CoverSlot> covers, Vector3 facing)
	{
		try
		{
			if (!Enabled || covers == null || covers.Count == 0) { ClearAll(); return; }
			EnsureMat();
			Quaternion rot = Quaternion.LookRotation(facing.sqrMagnitude > 0.001f ? facing : Vector3.forward);
			int created = 0;
			int activeTotal = 0;
			foreach (Formation.CoverSlot cs in covers)
			{
				if (cs == null || activeTotal >= MaxGhosts) break;
				long k = 0;
				try
				{
					if (cs.unit == null || !cs.unit.IsAlive || cs.unit.transform == null) continue;
					k = (long)cs.unit.Pointer;
				}
				catch { continue; }
				GameObject g;
				if (!live.TryGetValue(k, out g) || g == null)
				{
					if (created >= MaxNewPerApply) continue; // 本轮预算用完：下一轮再建（标记仍在，不缺指示）

					g = MakeGhost(cs.unit);
					if (g == null) continue; // 克隆失败：本轮跳过（连续失败会自动降级）
					live[k] = g;
					TryPose(g, cs.pose);
					created++;
				}
				try { g.SetActive(true); g.transform.position = cs.pos; g.transform.rotation = rot; activeTotal++; } catch { }
			}
			// 计划外的幽灵：隐藏待复用（不 Destroy——销毁/重建是拖动卡顿的根因）
			// 1.2.15 性能：原实现每个幽灵都遍历整张 covers 表（O(n×m)），改为 HashSet 一次比对。
			wantedPtrs.Clear();
			foreach (Formation.CoverSlot cs in covers)
			{
				try { if (cs?.unit != null) wantedPtrs.Add((long)cs.unit.Pointer); } catch { }
			}
			foreach (KeyValuePair<long, GameObject> kv in live)
			{
				try { if (!wantedPtrs.Contains(kv.Key) && kv.Value != null && kv.Value.activeSelf) kv.Value.SetActive(false); } catch { }
			}
		}
		catch (Exception ex) { SquadCmdLogic.Log("[Ghost] Apply 失败: " + ex.Message); }
	}


	/// <summary>拖动结束/退出 RTS/手势取消：**整体销毁**（唯一的清场入口）。</summary>
	internal static void ClearAll()
	{
		foreach (GameObject g in live.Values)
		{
			try { if (g != null) UnregisterGhost(g); } catch { }
			try { if (g != null) UnityEngine.Object.Destroy(g); } catch { }
		}
		live.Clear();
		wantedPtrs.Clear();
	}

	private static GameObject MakeGhost(Soldier s)
	{
		try
		{
			GameObject g = UnityEngine.Object.Instantiate(s.gameObject);
			g.name = "ER2Ghost_" + s.gameObject.name;
			// 单次遍历完成全部组件处理（含剥离相机类组件：残留会劫持渲染视角）
			ProcessComponents(g);
			// 幽灵是真实 Soldier 克隆体——必须脱离小队、不可被瞄准、关 AI
			DetachAndPacify(g);
			failCount = 0;
			return g;
		}
		catch (Exception ex)
		{
			failCount++;
			if (failCount >= 2 && !cloneBroken)
			{
				cloneBroken = true;
				SquadCmdLogic.LogAlways("[Ghost] 幽灵模型克隆连续失败，已降级为纯标记预览：" + ex.Message);
			}
			else SquadCmdLogic.Log("[Ghost] 克隆失败: " + ex.Message);
			return null;
		}
	}

	// ══════════════════════════════════════════════════════════
	// 幽灵登记表（伤害免疫 + 地面射线豁免）
	// ══════════════════════════════════════════════════════════

	/// <summary>让克隆体脱离小队（不进小队列表面板/统计）、关闭"可被瞄准"并登记进幽灵表。
	/// `allowBeingTargeted(false)` 对**已经锁定**的敌人不够，故另加伤害免疫补丁按表拒绝伤害。</summary>
	internal static void DetachAndPacify(GameObject g)
	{
		try
		{
			if (g == null) return;
			// 1.2.17：强类型遍历（Component[] + is 不可靠，见 ProcessComponents 注释）
			Soldier[] ss = g.GetComponentsInChildren<Soldier>(true);
			if (ss != null)
				for (int i = 0; i < ss.Length; i++)
				{
					Soldier c = ss[i];
					if (c == null) continue;
					try { c.joinedSquad = null; } catch { }
					try { new Lua_Soldier(c).getAiParams().allowBeingTargeted(false); } catch { }
					try { new Lua_Soldier(c).getAiParams().enableAiBehaviour(false); } catch { }
					AddGhostPtr((long)c.Pointer);
				}
			Vehicle[] vv = g.GetComponentsInChildren<Vehicle>(true);
			if (vv != null)
				for (int i = 0; i < vv.Length; i++) { try { if (vv[i] != null) AddGhostPtr((long)vv[i].Pointer); } catch { } }
		}
		catch { }
	}

	private static void AddGhostPtr(long p)
	{
		if (ghostPtrs.Count >= GhostPtrCap) ghostPtrs.Clear(); // 异常路径防无界增长
		ghostPtrs.Add(p);
	}

	/// <summary>供附属 mod 登记/注销预览幽灵（伤害免疫 + 地面射线豁免）。</summary>
	internal static void RegisterGhost(GameObject g)
	{
		DetachAndPacify(g);
	}

	/// <summary>注销幽灵：单次遍历移除 Soldier/Creature/Vehicle 指针。
	/// （原实现只删 Creature 指针 → Soldier/Vehicle 指针永不清理，表无界增长。）</summary>
	internal static void UnregisterGhost(GameObject g)
	{
		try
		{
			if (g == null) return;
			Soldier[] ss = g.GetComponentsInChildren<Soldier>(true);
			if (ss != null)
				for (int i = 0; i < ss.Length; i++) { try { if (ss[i] != null) ghostPtrs.Remove((long)ss[i].Pointer); } catch { } }
			Vehicle[] vv = g.GetComponentsInChildren<Vehicle>(true);
			if (vv != null)
				for (int i = 0; i < vv.Length; i++) { try { if (vv[i] != null) ghostPtrs.Remove((long)vv[i].Pointer); } catch { } }
		}
		catch { }
	}

	/// <summary>尝试把克隆体 Animator 停到建议姿态的 clip（名称含 crouch/prone，优先带 idle 的）；找不到不动。</summary>
	private static void TryPose(GameObject g, SoldierPose pose)
	{
		if (pose == SoldierPose.Idle) return;
		try
		{
			Animator an = g.GetComponentInChildren<Animator>();
			if (an == null || an.runtimeAnimatorController == null) return;
			AnimationClip[] clips = an.runtimeAnimatorController.animationClips;
			if (clips == null) return;
			string want = pose == SoldierPose.Prone ? "prone" : "crouch";
			AnimationClip best = null;
			foreach (AnimationClip c in clips)
			{
				if (c == null) continue;
				string n = c.name.ToLowerInvariant();
				if (n.Contains(want)) { best = c; if (n.Contains("idle")) break; }
			}
			if (best != null) an.Play(best.name, 0, 0f);
			if (Plugin.debugLog.Value)
				SquadCmdLogic.Log("[Ghost] pose clip " + (best != null ? best.name : "（未找到，保持默认）") + " want=" + want);
		}
		catch { }
	}
}
