using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2UniversalGeneration;

/// <summary>
/// 落点模式：面板点[条目]后进入，战场鼠标移动画预览圈，左键确认生成。
/// Shift+左键连续放置；右键/G/ESC 取消。见 docs/DESIGN.md §2.4。
/// </summary>
internal static class Placer
{
	private static GenEntry entry;
	private static string faction;
	private static SquadType? crewType; // 乘员班型（null=空车）
	private static bool placing;
	private static bool exitOnRelease; // 生成后等松开左键再退出放置（整段按压保持手势互斥，防宿主框选残影）
	private static Vector3 previewPos;
	private static bool previewValid;

	public static bool Placing => placing;

	public static GenEntry PendingEntry() => entry;

	/// <summary>进入落点模式（面板收起，G 键被本状态占用）。</summary>
	public static void Begin(GenEntry e, string fac, SquadType? ct)
	{
		entry = e; faction = fac; crewType = ct;
		placing = true;
		previewValid = false;
		GenPanel.Flash(Ui.Tr("选择放置位置…"));
	}

	public static void Cancel(string reason)
	{
		if (!placing) return;
		placing = false;
		entry = null;
		if (reason == "RTS 退出") return; // 退出 RTS 不回面板
		GenPanel.Flash(Ui.Tr("已取消放置"));
		GenPanel.SetOpen(true, false); // 回到面板（保持原类别）
	}

	/// <summary>每帧（Tick Postfix 里）更新预览与放置点击。悬停徽标上的点击忽略。</summary>
	public static void TickPlacing()
	{
		if (!placing) return;

		// 生成后：保持互斥到左键松开，再退出放置回面板（防止宿主同帧读到点击产生框选残影）
		if (exitOnRelease)
		{
			if (Input.GetMouseButtonUp(0) || !Input.GetMouseButton(0))
			{
				exitOnRelease = false;
				placing = false;
				entry = null;
				GenPanel.SetOpen(true, false);
			}
			return;
		}

		UpdatePreview();

		bool guiNow = GenPanel.IsMouseOverBadge();
		bool leftDown = Input.GetMouseButtonDown(0);
		bool rightDown = Input.GetMouseButtonDown(1);
		bool shift = Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift);

		if (Input.GetKeyDown(KeyCode.Escape))
		{
			Cancel("ESC");
			return;
		}
		if (rightDown)
		{
			Cancel("右键");
			return;
		}
		if (leftDown && !guiNow)
		{
			Plugin.ModLog.LogInfo("[UniGen] 放置点击 valid=" + previewValid + " pos=" + previewPos);
			if (!previewValid)
			{
				GenPanel.Flash(Ui.Tr("此处无法放置（未命中地面）"), true);
				return;
			}
			ExecuteSpawn(previewPos);
			if (!shift)
			{
				exitOnRelease = true; // 本帧保持 placing=true（互斥生效），松开后退出
			}
			else
			{
				GenPanel.Flash(Ui.Tr("已放置，可继续（右键结束）"));
			}
		}
	}

	private static void UpdatePreview()
	{
		previewValid = false;
		// 上帝视角下 Camera.main 可能拿不到（宿主同款：ResourcesManager.mainCamera 优先）
		Camera cam = null;
		try { cam = ResourcesManager.mainCamera; } catch { }
		if (cam == null) { try { cam = Camera.main; } catch { } }
		if (cam == null) return;
		try
		{
			Ray ray = cam.ScreenPointToRay(Input.mousePosition);
			if (Physics.Raycast(ray, out RaycastHit hit, 6000f))
			{
				// 命中面必须大体朝上（打在单位/山坡侧面上不算）
				Vector3 n = hit.normal;
				if (n.y > 0.4f)
				{
					previewPos = hit.point;
					previewValid = true;
				}
			}
		}
		catch { }
	}

	private static void ExecuteSpawn(Vector3 pos)
	{
		GenEntry e = entry;
		string fac = faction;
		SquadType? crewT = crewType;
		if (e == null) return;

		if (e.IsInfantry)
		{
			GenRunner.SpawnInfantrySquad(e, pos, fac, sq =>
			{
				if (sq != null) GenPanel.Flash(Ui.Tr("已生成 ") + e.Title + Ui.Tr("（") + FacName(fac) + "）");
				else GenPanel.Flash(Ui.Tr("生成失败：") + e.Title, true);
			});
		}
		else
		{
			GenRunner.SpawnVehicle(e, pos, fac, crewT, veh =>
			{
				if (veh != null) GenPanel.Flash(Ui.Tr("已生成 ") + e.Title + Ui.Tr("（") + FacName(fac) + "）");
				else GenPanel.Flash(Ui.Tr("生成失败：") + e.Title, true);
			});
		}
	}

	private static string FacName(string fac)
	{
		return FactionData.IsFriendly(fac, GenRunner.MyFaction()) ? Ui.Tr("我方") : Ui.Tr("敌方");
	}

	/// <summary>OnGUI 末段画预览标识：内环 + 旋转刻度 + 中心十字（紧凑不挡视野；我方绿/敌方红）。</summary>
	public static void DrawPreview()
	{
		if (!placing || !previewValid) return;
		Color c = PreviewColor();
		float r = entry != null && entry.IsInfantry ? 2.5f : 3.5f;
		Vector3 center = previewPos + Vector3.up * 0.25f;
		float t = Time.unscaledTime;

		// 内环（亮）：落点圈，缓慢旋转
		Ring(center, r, c, 32, t * 40f);
		// 旋转刻度（4 根）
		for (int k = 0; k < 4; k++)
		{
			float a = (90f * k + t * 40f) * Mathf.Deg2Rad;
			Vector3 d = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
			DrawLineSeg(center + d * (r * 0.8f), center + d * (r * 1.2f), c, 2f);
		}
		// 中心点（十字）
		float dot = r * 0.14f;
		DrawLineSeg(center + Vector3.left * dot, center + Vector3.right * dot, c, 2.5f);
		DrawLineSeg(center + Vector3.forward * dot, center + Vector3.back * dot, c, 2.5f);
		// 短高度柱
		DrawLineSeg(center, center + Vector3.up * (r * 0.7f), new Color(c.r, c.g, c.b, 0.45f), 1.2f);
	}

	private static void Ring(Vector3 center, float r, Color c, int seg, float angleOffsetDeg = 0f)
	{
		Vector3 prev = center + Quaternion.Euler(0f, angleOffsetDeg, 0f) * new Vector3(r, 0, 0);
		for (int i = 1; i <= seg; i++)
		{
			float ang = angleOffsetDeg + (360f / seg) * i;
			Vector3 next = center + Quaternion.Euler(0f, ang, 0f) * new Vector3(r, 0, 0);
			DrawLineSeg(prev, next, c, 2f);
			prev = next;
		}
	}

	/// <summary>预览圈颜色：我方绿 / 非我方红（按 _allies/_axis 后缀判）。</summary>
	private static Color PreviewColor()
	{
		string my = GenRunner.MyFaction();
		return FactionData.IsFriendly(faction, my)
			? new Color(0.4f, 1f, 0.4f, 0.85f)
			: new Color(1f, 0.35f, 0.3f, 0.85f);
	}

	private static UnityEngine.Material lineMat;
	private static void DrawLineSeg(Vector3 a, Vector3 b, Color c, float width)
	{
		try
		{
			if (lineMat == null)
				lineMat = new UnityEngine.Material(UnityEngine.Shader.Find("Sprites/Default"));
			lineMat.SetPass(0);
			GL.Begin(GL.LINES);
			GL.Color(c);
			GL.Vertex(a);
			GL.Vertex(b);
			GL.End();
		}
		catch { }
	}
}
