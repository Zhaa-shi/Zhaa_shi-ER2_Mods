using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace ER2UnitInfoOverlay;

/// <summary>一个单位的绘制条目（刷新阶段构建，绘制阶段只读）。</summary>
internal sealed class OverlayEntry
{
	public IntPtr Ptr;
	public int InstanceId;
	public Vector2 Screen;
	public float Width;
	public float Height;
	public Color Accent;
	public readonly List<string> Lines = new List<string>();
	public readonly List<Color> Colors = new List<Color>();
}

/// <summary>
/// 单位状态悬浮显示核心：
///  - Refresh()  在 PlayerController.Update Postfix 里调用（内部 0.1s 节流，Time.unscaledTime），
///    枚举 Creature.allCreatures → 距离/朝向/屏幕裁剪 → 构建信息行与布局；
///  - Draw()     在 PlayerController.OnGUI Postfix 里调用，从缓存绘制（IMGUI 多事件帧不重复计算）。
/// 所有 interop 访问都 try/catch（单位可能在遍历中被销毁），绘制不依赖任何每帧 FindObjectsOfType。
/// </summary>
internal static class OverlayLogic
{
	internal static bool active = true;

	private static readonly List<OverlayEntry> cache = new List<OverlayEntry>();
	private static readonly Dictionary<IntPtr, Vector3> lastPos = new Dictionary<IntPtr, Vector3>();
	private static readonly Dictionary<IntPtr, float> lastTime = new Dictionary<IntPtr, float>();
	private static readonly Dictionary<IntPtr, Vector3> vel = new Dictionary<IntPtr, Vector3>();
	private static readonly Dictionary<IntPtr, int> observedMax = new Dictionary<IntPtr, int>();

	private static Texture2D whiteTex;
	private static GUIStyle labelStyle;
	private static GUIStyle summaryStyle;
	private static int styleFontSize = -1;
	private static float lastRefresh = -10f;
	private static float lastErrLog = -10f;
	/// <summary>场景就绪时刻（unscaledTime）；-1 = 场景未就绪（无单位/无受控角色）。</summary>
	private static float firstVisibleTime = -1f;
	private static readonly StringBuilder sb = new StringBuilder(96);

	/// <summary>双语文本（DefaultChinese 是编译期常量，每次调用在编译期折叠为单一字符串，无运行时开销）。</summary>
	private static string T(string cn, string en) => Plugin.DefaultChinese ? cn : en;

	internal static int aliveCount;
	internal static int corpseCount;
	internal static int drawnCount;

	internal static void ClearCache()
	{
		cache.Clear();
		aliveCount = corpseCount = drawnCount = 0;
	}

	internal static void Refresh()
	{
		if (!active)
		{
			ClearCache();
			return;
		}
		if (Time.unscaledTime - lastRefresh < 0.1f)
		{
			return;
		}
		lastRefresh = Time.unscaledTime;

		try
		{
			PlayerController pc = PlayerController.currentController;
			if (pc == null)
			{
				firstVisibleTime = -1f;
				ClearCache();
				return;
			}
			Soldier controlled = null;
			try
			{
				controlled = pc.ControlledCharacter;
			}
			catch
			{
			}

			Camera cam = null;
			try
			{
				cam = ResourcesManager.mainCamera;
			}
			catch
			{
			}
			if (cam == null)
			{
				try
				{
					cam = Camera.main;
				}
				catch
				{
				}
			}
			if (cam == null)
			{
				return;
			}

			Transform camT = cam.transform;
			Vector3 camPos = camT.position;
			Vector3 camFwd = camT.forward;
			float mult = 1f;
			try
			{
				mult = ResourcesManager.ResolutionMult;
			}
			catch
			{
			}
			if (mult <= 0f || float.IsNaN(mult))
			{
				mult = 1f;
			}

			int fs = Plugin.fontSize.Value;
			EnsureStyle(fs, mult);

			bool showAlive = Plugin.showAlive.Value;
			bool showCorpses = Plugin.showCorpses.Value;
			bool showPlayer = Plugin.showPlayer.Value;
			float maxDist = Plugin.maxDistance.Value;
			float maxDistSq = maxDist * maxDist;
			int maxUnits = Plugin.maxUnits.Value;
			bool targetOnly = Plugin.displayMode.Value == "Targeted";
			float aimR = Plugin.aimRadius.Value;

			Il2CppSystem.Collections.Generic.List<Creature> list = Creature.allCreatures;
			cache.Clear();
			aliveCount = corpseCount = drawnCount = 0;
			if (list == null || list.Count == 0)
			{
				// 场景未就绪（加载中/空场景）：重置场景计时器，等下次出现单位时重新计时
				firstVisibleTime = -1f;
				return;
			}
			if (firstVisibleTime < 0f)
			{
				firstVisibleTime = Time.unscaledTime;
			}

			Vector2 center = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
			float bestCenter = float.MaxValue;
			OverlayEntry best = null;
			HashSet<IntPtr> allPtrs = new HashSet<IntPtr>();

			for (int i = 0; i < list.Count; i++)
			{
				Creature c = list[i];
				if (c == null)
				{
					continue;
				}
				try
				{
					allPtrs.Add(c.Pointer);

					bool alive = c.IsAlive;
					bool isPlayerUnit = controlled != null && c.Pointer == controlled.Pointer;
					if (!showPlayer && isPlayerUnit)
					{
						continue;
					}
					if (alive)
					{
						if (!showAlive && !isPlayerUnit)
						{
							continue;
						}
						aliveCount++;
					}
					else
					{
						if (!showCorpses)
						{
							continue;
						}
						corpseCount++;
					}

					Vector3 pos = c.transform.position;
					float dx = pos.x - camPos.x;
					float dy = pos.y - camPos.y;
					float dz = pos.z - camPos.z;
					float dSq = dx * dx + dy * dy + dz * dz;
					if (dSq > maxDistSq || dSq <= 0.001f)
					{
						continue;
					}
					// 必须在相机前方（点积 > 0），且不显示身后单位
					if (camFwd.x * dx + camFwd.y * dy + camFwd.z * dz <= 0f)
					{
						continue;
					}

					Vector3 head;
					try
					{
						head = c.UINamePos();
					}
					catch
					{
						head = pos + Vector3.up * 1.7f;
					}
					Vector3 sp = cam.WorldToScreenPoint(head);
					if (sp.z <= 0.01f)
					{
						continue;
					}
					float sx = sp.x;
					float sy = Screen.height - sp.y; // IMGUI 左上原点
					if (sx < -300f || sx > Screen.width + 300f || sy < -100f || sy > Screen.height + 300f)
					{
						continue;
					}

					if (!targetOnly && drawnCount >= maxUnits)
					{
						continue;
					}

					OverlayEntry e = new OverlayEntry
					{
						Ptr = c.Pointer,
						InstanceId = c.GetInstanceID(),
						Screen = new Vector2(sx, sy)
					};
					BuildEntry(c, alive, pos, Mathf.Sqrt(dSq), e, controlled);
					if (e.Lines.Count == 0)
					{
						continue;
					}

					// 布局测量（10Hz × ≤40 单位，开销可忽略）
					float maxW = 0f;
					for (int li = 0; li < e.Lines.Count; li++)
					{
						Vector2 sz = labelStyle.CalcSize(new GUIContent(e.Lines[li]));
						if (sz.x > maxW)
						{
							maxW = sz.x;
						}
					}
					float pad = 5f * mult;
					e.Width = maxW + pad * 2f;
					e.Height = e.Lines.Count * (fs + 3f) + pad * 2f;

					if (targetOnly)
					{
						float cd = Vector2.Distance(e.Screen, center);
						if (cd <= aimR && cd < bestCenter)
						{
							bestCenter = cd;
							best = e;
						}
					}
					else
					{
						cache.Add(e);
						drawnCount++;
					}
				}
				catch
				{
				}
			}

			if (targetOnly && best != null)
			{
				cache.Add(best);
				drawnCount = 1;
			}

			// 清理已消失单位的跟踪状态（速度/血量上限）
			if (allPtrs.Count > 0)
			{
				List<IntPtr> stale = null;
				foreach (IntPtr k in lastPos.Keys)
				{
					if (!allPtrs.Contains(k))
					{
						if (stale == null)
						{
							stale = new List<IntPtr>();
						}
						stale.Add(k);
					}
				}
				if (stale != null)
				{
					for (int si = 0; si < stale.Count; si++)
					{
						lastPos.Remove(stale[si]);
						lastTime.Remove(stale[si]);
						vel.Remove(stale[si]);
						observedMax.Remove(stale[si]);
					}
				}
			}
		}
		catch (Exception ex)
		{
			LogThrottled("Unit Inspector refresh error: " + ex.Message);
		}
	}

	/// <summary>构建一个单位的信息行（名称 + 可选行），并填好逐行颜色与边框强调色。</summary>
	private static void BuildEntry(Creature c, bool alive, Vector3 pos, float dist, OverlayEntry e, Soldier controlled)
	{
		Soldier s = null;
		try
		{
			s = c.TryCast<Soldier>();
		}
		catch
		{
		}

		bool isPlayerUnit = controlled != null && c.Pointer == controlled.Pointer;
		int hp = -1;
		int thr = Creature.INCAPACITATED_THREESHOLD;
		try
		{
			hp = c.life_total.Value;
		}
		catch
		{
		}
		bool isBleeding = false;
		try
		{
			isBleeding = c.isBleeding;
		}
		catch
		{
		}

		// ---- 名称行（恒显示）----
		string name = null;
		try
		{
			name = c.name_surname;
		}
		catch
		{
		}
		if (string.IsNullOrEmpty(name))
		{
			name = T("单位#", "Unit#") + e.InstanceId;
		}
		sb.Clear();
		sb.Append(name);
		if (isPlayerUnit)
		{
			sb.Append(' ').Append(T("[玩家]", "[YOU]"));
		}
		else if (s != null)
		{
			try
			{
				if (s.IsAI())
				{
					sb.Append(" [AI]");
				}
			}
			catch
			{
			}
		}
		if (!alive)
		{
			sb.Append(' ').Append(T("[阵亡]", "[DEAD]"));
		}
		e.Lines.Add(sb.ToString());
		e.Colors.Add(alive ? new Color(0.95f, 0.95f, 0.95f, 1f) : new Color(0.55f, 0.55f, 0.55f, 1f));

		// ---- 边框强调色 ----
		if (!alive)
		{
			e.Accent = new Color(0.55f, 0.55f, 0.55f, 1f);
		}
		else if (isPlayerUnit)
		{
			e.Accent = new Color(0.45f, 0.8f, 1f, 1f);
		}
		else if (s != null)
		{
			e.Accent = FactionColor(s, controlled);
		}
		else
		{
			e.Accent = new Color(0.9f, 0.9f, 0.9f, 1f);
		}

		// ---- 血量行 ----
		if (Plugin.showHp.Value && hp >= 0)
		{
			int om = hp;
			if (observedMax.TryGetValue(e.Ptr, out int prev) && prev > om)
			{
				om = prev;
			}
			observedMax[e.Ptr] = om;
			sb.Clear();
			sb.Append(T("血量 ", "HP ")).Append(hp).Append('/').Append(om);
			if (thr > 0)
			{
				sb.Append(" (").Append(T("阈值 ", "thr ")).Append(thr).Append(')');
			}
			if (isBleeding)
			{
				sb.Append(' ').Append(T("流血", "BLEED"));
			}
			e.Lines.Add(sb.ToString());
			e.Colors.Add(HpColor(hp, om));
		}

		// ---- 状态标签行 ----
		if (Plugin.showState.Value && s != null)
		{
			sb.Clear();
			bool badState = false;
			if (isBleeding)
			{
				sb.Append(T("流血", "BLEED")).Append(' ');
				badState = true;
			}
			if (alive && hp >= 0 && thr > 0 && hp < thr)
			{
				sb.Append(T("倒下", "DOWN")).Append(' ');
				badState = true;
			}
			try
			{
				if (s.HasSurrended())
				{
					sb.Append(T("投降", "SURR")).Append(' ');
					badState = true;
				}
			}
			catch
			{
			}
			try
			{
				if (s.isSprinting)
				{
					sb.Append(T("冲刺", "SPRINT")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsRunning())
				{
					sb.Append(T("奔跑", "RUN")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsMoving())
				{
					sb.Append(T("移动", "MOVE")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsCrawling)
				{
					sb.Append(T("匍匐", "CRAWL")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsAiming)
				{
					sb.Append(T("瞄准", "AIM")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsReloading)
				{
					sb.Append(T("换弹", "RELOAD")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsThrowing)
				{
					sb.Append(T("投掷", "THROW")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsOnFire)
				{
					sb.Append(T("着火", "FIRE")).Append(' ');
					badState = true;
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsOnVehicle())
				{
					sb.Append(T("载具", "VEH")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsCarryingBody())
				{
					sb.Append(T("搬运", "CARRY")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsCarried())
				{
					sb.Append(T("被搬", "CARRIED")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsTalking())
				{
					sb.Append(T("说话", "TALK")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsOutOfStamina())
				{
					sb.Append(T("力竭!", "STAM!")).Append(' ');
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsOnWater())
				{
					sb.Append(T("涉水", "WATER")).Append(' ');
				}
			}
			catch
			{
			}
			if (sb.Length > 0)
			{
				sb.Length--;
				e.Lines.Add(sb.ToString());
			}
			else
			{
				e.Lines.Add("-");
			}
			e.Colors.Add(badState ? new Color(1f, 0.45f, 0.4f, 1f) : new Color(0.9f, 0.9f, 0.9f, 1f));
		}

		// ---- 姿态行 ----
		if (Plugin.showPose.Value && s != null)
		{
			SoldierPose pose = SoldierPose.Idle;
			try
			{
				pose = s.m_pose;
			}
			catch
			{
			}
			sb.Clear();
			sb.Append(T("姿态: ", "Pose: ")).Append(PoseName(pose));
			e.Lines.Add(sb.ToString());
			e.Colors.Add(new Color(0.85f, 0.95f, 1f, 1f));
		}

		// ---- 阵营行 ----
		if (Plugin.showFaction.Value && s != null)
		{
			string fac = null;
			try
			{
				fac = s.faction;
			}
			catch
			{
			}
			sb.Clear();
			sb.Append(T("阵营: ", "Faction: ")).Append(string.IsNullOrEmpty(fac) ? "?" : fac);
			e.Lines.Add(sb.ToString());
			e.Colors.Add(new Color(0.8f, 0.8f, 0.75f, 1f));
		}

		// ---- 兵种行 ----
		if (Plugin.showRole.Value && s != null)
		{
			sb.Clear();
			try
			{
				if (s.IsMedic())
				{
					sb.Append(T("医疗 ", "MEDIC "));
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsGunner())
				{
					sb.Append(T("机枪 ", "GUNNER "));
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsATUnit())
				{
					sb.Append(T("反坦克 ", "AT "));
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsSapper())
				{
					sb.Append(T("工兵 ", "SAPPER "));
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsMarksman())
				{
					sb.Append(T("狙击 ", "MARKSMAN "));
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsRadioman())
				{
					sb.Append(T("电台 ", "RADIO "));
				}
			}
			catch
			{
			}
			try
			{
				if (s.IsSquadLeader())
				{
					sb.Append(T("队长 ", "LEADER "));
				}
			}
			catch
			{
			}
			e.Lines.Add(sb.Length > 0 ? sb.ToString().TrimEnd() : "-");
			e.Colors.Add(new Color(0.75f, 1f, 0.8f, 1f));
		}

		// ---- 坐标与距离行 ----
		if (Plugin.showPos.Value)
		{
			sb.Clear();
			sb.Append(T("坐标 ", "pos ")).Append(pos.x.ToString("0.0")).Append(',').Append(pos.y.ToString("0.0")).Append(',').Append(pos.z.ToString("0.0"));
			sb.Append(" | ").Append(dist.ToString("0.0")).Append(T("米", "m"));
			e.Lines.Add(sb.ToString());
			e.Colors.Add(new Color(0.85f, 0.85f, 0.85f, 1f));
		}

		// ---- 速度行 ----
		if (Plugin.showVelocity.Value)
		{
			Vector3 v = Vector3.zero;
			float now = Time.unscaledTime;
			if (lastPos.TryGetValue(e.Ptr, out Vector3 lp) && lastTime.TryGetValue(e.Ptr, out float lt))
			{
				float dt = now - lt;
				if (dt > 0.02f && dt < 1f)
				{
					Vector3 inst = (pos - lp) / dt;
					if (inst.magnitude < 60f)
					{
						v = vel.TryGetValue(e.Ptr, out Vector3 pv) ? Vector3.Lerp(pv, inst, 0.3f) : inst;
						vel[e.Ptr] = v;
					}
				}
			}
			lastPos[e.Ptr] = pos;
			lastTime[e.Ptr] = now;
			sb.Clear();
			sb.Append(T("速度 ", "vel ")).Append(v.magnitude.ToString("0.00")).Append(' ').Append(T("米/秒", "m/s"));
			e.Lines.Add(sb.ToString());
			e.Colors.Add(new Color(0.7f, 0.9f, 1f, 1f));
		}

		// ---- 实例 ID 行 ----
		if (Plugin.showId.Value)
		{
			sb.Clear();
			sb.Append(T("ID ", "id ")).Append(e.InstanceId).Append(' ').Append(T("指针 0x", "ptr 0x")).Append(((long)e.Ptr).ToString("X"));
			e.Lines.Add(sb.ToString());
			e.Colors.Add(new Color(0.6f, 0.6f, 0.65f, 1f));
		}
	}

	/// <summary>姿态名（中文版：站立/蹲姿/卧倒；英文版：Idle/Crouch/Prone）。</summary>
	private static string PoseName(SoldierPose pose)
	{
		switch (pose)
		{
			case SoldierPose.Crouch:
				return T("蹲姿", "Crouch");
			case SoldierPose.Prone:
				return T("卧倒", "Prone");
			default:
				return T("站立", "Idle");
		}
	}

	/// <summary>同阵营绿色 / 敌阵营红色 / 未知白色（按 faction 字符串与 "_allies"/"_axis" 后缀分组）。</summary>
	private static Color FactionColor(Soldier s, Soldier controlled)
	{
		try
		{
			string myFac = null;
			if (controlled != null)
			{
				try
				{
					myFac = controlled.faction;
				}
				catch
				{
				}
			}
			string fac = s.faction;
			if (string.IsNullOrEmpty(myFac) || string.IsNullOrEmpty(fac))
			{
				return new Color(0.9f, 0.9f, 0.9f, 1f);
			}
			if (string.Equals(myFac, fac, StringComparison.Ordinal))
			{
				return new Color(0.45f, 1f, 0.5f, 1f);
			}
			bool mineA = myFac.EndsWith("_allies", StringComparison.Ordinal);
			bool mineX = myFac.EndsWith("_axis", StringComparison.Ordinal);
			bool hisA = fac.EndsWith("_allies", StringComparison.Ordinal);
			bool hisX = fac.EndsWith("_axis", StringComparison.Ordinal);
			if ((mineA && hisA) || (mineX && hisX))
			{
				return new Color(0.45f, 1f, 0.5f, 1f);
			}
			if ((mineA && hisX) || (mineX && hisA))
			{
				return new Color(1f, 0.35f, 0.3f, 1f);
			}
			return new Color(0.9f, 0.9f, 0.9f, 1f);
		}
		catch
		{
			return new Color(0.9f, 0.9f, 0.9f, 1f);
		}
	}

	private static Color HpColor(int hp, int max)
	{
		float pct = max > 0 ? hp / (float)max : 1f;
		if (pct > 0.6f)
		{
			return new Color(0.45f, 1f, 0.5f, 1f);
		}
		if (pct > 0.3f)
		{
			return new Color(1f, 0.85f, 0.3f, 1f);
		}
		return new Color(1f, 0.35f, 0.3f, 1f);
	}

	internal static void Draw()
	{
		if (!active || cache.Count == 0)
		{
			return;
		}
		// 暂停（设置/暂停菜单）或死亡期间隐藏（Pause/timeScale 已在 v1.0.1 实证战斗中为 false；
		// IsDead 语义确定：活着的受控角色必为 false）
		if (IsHiddenState())
		{
			return;
		}
		// 加载尾巴隐藏（确定性实现）：场景从"无单位"变为"有单位"后前 hideAfterLoad 秒不绘制，
		// 覆盖进战斗/换场黑屏加载尾巴；不依赖原生加载信号（LoadingCircle.IsLoading 语义未验证，v1.0.2 曾致悬浮窗消失）
		if (Plugin.hideAfterLoad.Value > 0f && firstVisibleTime >= 0f && Time.unscaledTime - firstVisibleTime < Plugin.hideAfterLoad.Value)
		{
			return;
		}
		try
		{
			// F5 隐藏 HUD 联动（Hide Anything 契约，逐 mod 开关）
			if (ER2Shared.NoHintsHudLink.IsHidden("er2.unitinfooverlay", "Unit Info Overlay"))
			{
				return;
			}
		}
		catch
		{
		}

		try
		{
			float mult = 1f;
			try
			{
				mult = ResourcesManager.ResolutionMult;
			}
			catch
			{
			}
			if (mult <= 0f || float.IsNaN(mult))
			{
				mult = 1f;
			}
			int fs = Plugin.fontSize.Value;
			EnsureStyle(fs, mult);
			if (labelStyle == null)
			{
				return;
			}
			// 场景切换可能销毁字体/贴图（陷阱：hideFlags 资源保护），销毁后自动重建
			if (labelStyle.font == null)
			{
				styleFontSize = -1;
				EnsureStyle(fs, mult);
			}
			Texture2D tex = GetWhiteTex();
			float bgA = Mathf.Clamp01(Plugin.bgOpacity.Value);
			bool hasBg = bgA > 0.001f && tex != null;
			float pad = 5f * mult;

			// 摘要行（左上角）
			if (Plugin.showSummary.Value)
			{
				sb.Clear();
				sb.Append(T("[单位观察] ", "[Unit Inspector] ")).Append(active ? T("开", "ON") : T("关", "OFF"));
				sb.Append("  |  ").Append(T("键 ", "key ")).Append(Plugin.toggleKey.Value);
				sb.Append("  |  ").Append(T("存活 ", "alive ")).Append(aliveCount).Append("  ").Append(T("尸体 ", "corpses ")).Append(corpseCount);
				sb.Append("  |  ").Append(T("绘制 ", "drawn ")).Append(drawnCount);
				sb.Append("  |  ").Append(T("范围 ", "range ")).Append(Plugin.maxDistance.Value.ToString("0")).Append(T("米", "m"));
				GUI.color = Color.white;
				GuiExtension.OutlinedLabel(new Rect(10f, 8f, 900f, 30f), sb.ToString(), summaryStyle, 1);
			}

			for (int i = 0; i < cache.Count; i++)
			{
				OverlayEntry e = cache[i];
				float x = Mathf.Clamp(e.Screen.x - e.Width * 0.5f, 2f, Mathf.Max(2f, Screen.width - e.Width - 2f));
				float y = Mathf.Clamp(e.Screen.y - e.Height, 2f, Mathf.Max(2f, Screen.height - e.Height - 2f));
				Rect r = new Rect(x, y, e.Width, e.Height);

				if (hasBg)
				{
					GUI.color = new Color(0f, 0f, 0f, bgA);
					GUI.DrawTexture(r, tex);
				}
				// 强调色边框
				Color border = e.Accent;
				border.a = 0.9f;
				GUI.color = border;
				GUI.DrawTexture(new Rect(r.x, r.y, r.width, 1f), tex);
				GUI.DrawTexture(new Rect(r.x, r.y + r.height - 1f, r.width, 1f), tex);
				GUI.DrawTexture(new Rect(r.x, r.y, 1f, r.height), tex);
				GUI.DrawTexture(new Rect(r.x + r.width - 1f, r.y, 1f, r.height), tex);
				// Targeted 模式高亮：外圈再描一层
				if (Plugin.displayMode.Value == "Targeted" && cache.Count == 1)
				{
					border.a = 1f;
					GUI.color = border;
					GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, r.width + 4f, 1f), tex);
					GUI.DrawTexture(new Rect(r.x - 2f, r.y + r.height + 1f, r.width + 4f, 1f), tex);
					GUI.DrawTexture(new Rect(r.x - 2f, r.y - 2f, 1f, r.height + 4f), tex);
					GUI.DrawTexture(new Rect(r.x + r.width + 1f, r.y - 2f, 1f, r.height + 4f), tex);
				}

				// 信息行
				GUI.color = Color.white;
				float lineH = fs + 3f;
				float ly = r.y + pad;
				for (int li = 0; li < e.Lines.Count; li++)
				{
					GUI.color = e.Colors[li];
					GuiExtension.OutlinedLabel(new Rect(r.x + pad, ly, e.Width - pad * 2f, lineH), e.Lines[li], labelStyle, 1);
					ly += lineH;
				}
			}
			GUI.color = Color.white;
		}
		catch (Exception ex)
		{
			LogThrottled("Unit Inspector draw error: " + ex.Message);
		}
		finally
		{
			GUI.color = Color.white;
		}
	}

	private static void EnsureStyle(int fs, float mult)
	{
		int target = Mathf.RoundToInt(fs * mult);
		if (labelStyle != null && styleFontSize == target)
		{
			return;
		}
		styleFontSize = target;
		Font f = GetFont();
		labelStyle = new GUIStyle();
		if (f != null)
		{
			labelStyle.font = f;
		}
		labelStyle.fontSize = target;
		labelStyle.fontStyle = FontStyle.Normal;
		labelStyle.alignment = TextAnchor.UpperLeft;
		labelStyle.wordWrap = false;
		labelStyle.normal.textColor = Color.white; // 陷阱：new GUIStyle() 默认黑色

		summaryStyle = new GUIStyle();
		if (f != null)
		{
			summaryStyle.font = f;
		}
		summaryStyle.fontSize = Mathf.Max(9, Mathf.RoundToInt(13f * mult));
		summaryStyle.fontStyle = FontStyle.Bold;
		summaryStyle.alignment = TextAnchor.UpperLeft;
		summaryStyle.normal.textColor = new Color(1f, 1f, 1f, 0.85f);
	}

	private static Font GetFont()
	{
		try
		{
			if (GUI.skin != null && GUI.skin.font != null)
			{
				return GUI.skin.font;
			}
		}
		catch
		{
		}
		try
		{
			return Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
		}
		catch
		{
			return null;
		}
	}

	private static Texture2D GetWhiteTex()
	{
		if (whiteTex == null)
		{
			try
			{
				Texture2D t = new Texture2D(1, 1);
				t.name = "ER2_UnitInspector_WhiteTex";
				t.hideFlags = (HideFlags)61;
				t.SetPixel(0, 0, Color.white);
				t.Apply();
				whiteTex = t;
			}
			catch
			{
				whiteTex = null;
			}
		}
		return whiteTex;
	}

	/// <summary>
	/// 隐藏状态检测。只保留**实证/语义确定**的信号（v1.0.2 曾因加入未验证语义的门控导致悬浮窗消失）：
	/// 1) 暂停：设置/暂停菜单打开（Pause.isPaused —— v1.0.1 实证战斗中为 false；兜底 timeScale==0，陷阱 36 同源）；
	/// 2) 死亡/重生：受控角色 IsDead（活着的受控角色必为 false）。
	/// 诊断实证（v1.0.3）：LoadingCircle.IsLoading 战斗中恒 false（可用但加载中语义未验证，加载隐藏走确定性方案）；
	/// DeathPanel.instance.gameObject.activeInHierarchy 战斗中恒 true（v1.0.2 悬浮窗消失根因，不可用）。
	/// </summary>
	private static bool IsHiddenState()
	{
		try
		{
			if (Pause.isPaused)
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			if (Time.timeScale <= 0.001f)
			{
				return true;
			}
		}
		catch
		{
		}
		try
		{
			PlayerController pc = PlayerController.currentController;
			if (pc != null)
			{
				Soldier controlled = pc.ControlledCharacter;
				if (controlled != null && controlled.IsDead)
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

	private static void LogThrottled(string msg)
	{
		float now = Time.unscaledTime;
		if (now - lastErrLog < 5f)
		{
			return;
		}
		lastErrLog = now;
		try
		{
			Plugin.ModLog.LogError(msg);
		}
		catch
		{
		}
	}
}
