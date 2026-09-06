using System;
using System.Collections.Generic;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace ER2SquadCommand;

/// <summary>
/// 0.9.0：3D 场景标记——单位跟随类指示（友军脚环/选中环/集火环/移动目标环/标记名签）
/// 全部用世界空间对象在场景中显示，不再用 OnGUI 屏幕投影绘制。
/// 无 MonoBehaviour：环体由 GodViewController.Tick 每帧驱动；本轮未命中的对象自动隐藏。
/// 圆环 = LineRenderer 闭合圆（单位半径 1m 建模，缩放 GO 改大小）；名签 = TextMesh 朝向相机。
/// </summary>
internal static class SceneMarkers
{
	private static readonly Dictionary<string, GameObject> pool = new Dictionary<string, GameObject>();
	private static readonly HashSet<string> used = new HashSet<string>();
	private static Material lineMat;
	private static Font labelFont;
	private const int Segments = 48;
	private static bool shaderLogged;

	private static Material LineMat()
	{
		if (lineMat != null) return lineMat;
		// 0.9.4：按用户要求回归深度测试（环不再盖在单位/一切物体上面），
		// 环体抬高到 0.15m 缓解缓坡地形裁切
		foreach (string sn in new[] { "Sprites/Default", "Universal Render Pipeline/Unlit", "Particles/Standard Unlit" })
		{
			Shader sh = null;
			try { sh = Shader.Find(sn); } catch { }
			if (sh != null)
			{
				lineMat = new Material(sh);
				if (!shaderLogged) { SquadCmdLogic.Log("[SceneMarkers] 线材质 shader=" + sn); shaderLogged = true; }
				return lineMat;
			}
		}
		return null;
	}

	private static Font LabelFont()
	{
		if (labelFont != null) return labelFont;
		try { if (GUI.skin != null) labelFont = GUI.skin.font; } catch { }
		if (labelFont == null)
		{
			try { labelFont = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"); } catch { }
		}
		return labelFont;
	}

	private static GameObject GetPooled(string key)
	{
		if (pool.TryGetValue(key, out GameObject go) && go != null) return go;
		go = new GameObject("SCM_" + key);
		UnityEngine.Object.DontDestroyOnLoad(go);
		LineRenderer lr = go.AddComponent<LineRenderer>();
		lr.useWorldSpace = false;
		lr.loop = true;
		lr.positionCount = Segments;
		lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		lr.receiveShadows = false;
		lr.material = LineMat();
		// 单位半径 1m 的圆（XZ 平面），缩放 GO 控制大小
		Il2CppStructArray<Vector3> pts = new Il2CppStructArray<Vector3>(Segments);
		for (int i = 0; i < Segments; i++)
		{
			float a = (float)i / Segments * Mathf.PI * 2f;
			pts[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
		}
		lr.SetPositions(pts);
		pool[key] = go;
		return go;
	}

	/// <summary>画/刷新一个世界空间圆环（radius<=0 时隐藏）。</summary>
	public static void Ring(string key, Vector3 groundPos, float radius, Color c, float width, bool visible)
	{
		if (!visible || radius <= 0f) return;
		used.Add(key);
		GameObject go = GetPooled(key);
		go.SetActive(true);
		go.transform.localScale = new Vector3(radius, 1f, radius);
		go.transform.position = groundPos;
		LineRenderer lr = go.GetComponent<LineRenderer>();
		lr.startColor = c;
		lr.endColor = c;
		lr.widthMultiplier = width;
	}

	/// <summary>世界空间文字名签（自动朝向相机）。</summary>
	public static void Label(string key, Vector3 pos, string text, Color c, bool visible)
	{
		if (!visible || string.IsNullOrEmpty(text)) return;
		used.Add(key);
		if (!pool.TryGetValue(key, out GameObject go) || go == null)
		{
			go = new GameObject("SCMT_" + key);
			UnityEngine.Object.DontDestroyOnLoad(go);
			TextMesh tm = go.AddComponent<TextMesh>();
			tm.fontSize = 48;
			tm.characterSize = 0.085f;
			tm.anchor = TextAnchor.LowerCenter;
			tm.alignment = TextAlignment.Center;
			Font f = LabelFont();
			if (f != null) { tm.font = f; tm.GetComponent<MeshRenderer>().sharedMaterial = f.material; }
			pool[key] = go;
		}
		go.SetActive(true);
		TextMesh t = go.GetComponent<TextMesh>();
		if (t.text != text) t.text = text;
		t.color = c;
		go.transform.position = pos;
		Camera cam = Camera.main;
		if (cam != null)
			go.transform.rotation = Quaternion.LookRotation(go.transform.position - cam.transform.position);
	}

	/// <summary>
	/// 0.9.2：地面实心圆点（程序化圆盘 Mesh，顶色烘焙，GUI/Text Shader 无视深度）。
	/// </summary>
	public static void Dot(string key, Vector3 groundPos, float radius, Color c, bool visible)
	{
		if (!visible || radius <= 0f) return;
		used.Add(key);
		if (!pool.TryGetValue(key, out GameObject go) || go == null)
		{
			go = new GameObject("SCMD_" + key);
			UnityEngine.Object.DontDestroyOnLoad(go);
			MeshFilter mf = go.AddComponent<MeshFilter>();
			MeshRenderer mr = go.AddComponent<MeshRenderer>();
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mr.receiveShadows = false;
			Material m = LineMat() != null ? new Material(LineMat().shader) : null;
			if (m != null) { m.SetColor("_Color", c); mr.material = m; }
			// 圆盘网格（单位半径 1m），顶点色烘焙成目标色，避免依赖 shader _Color 通道
			Mesh mesh = new Mesh();
			int seg = 28;
			Il2CppStructArray<Vector3> verts = new Il2CppStructArray<Vector3>(seg + 1);
			Il2CppStructArray<int> tris = new Il2CppStructArray<int>(seg * 3);
			Il2CppStructArray<Color> cols = new Il2CppStructArray<Color>(seg + 1);
			verts[0] = Vector3.zero;
			cols[0] = c;
			for (int i = 0; i < seg; i++)
			{
				float a = (float)i / seg * Mathf.PI * 2f;
				verts[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				cols[i + 1] = c;
				tris[i * 3] = 0;
				tris[i * 3 + 1] = i + 1;
				tris[i * 3 + 2] = (i + 1) % seg + 1;
			}
			mesh.vertices = verts;
			mesh.triangles = tris;
			mesh.colors = cols;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			mf.sharedMesh = mesh;
			pool[key] = go;
		}
		go.SetActive(true);
		go.transform.localScale = new Vector3(radius, 1f, radius);
		go.transform.position = groundPos;
	}

	// 1.0.2：箭头两翼/主线坐标暂存（SetPositions 立即拷贝，多支箭头可复用）
	private static Il2CppStructArray<Vector3> arrowMain = new Il2CppStructArray<Vector3>(2);
	private static Il2CppStructArray<Vector3> arrowHead = new Il2CppStructArray<Vector3>(3);

	/// <summary>
	/// 1.0.2：世界空间箭头（主线 + 两翼），用于载具朝向拖动指示。每帧调用刷新位置。
	/// </summary>
	public static void Arrow(string key, Vector3 from, Vector3 to, Color c, float width, bool visible)
	{
		if (!visible) return;
		used.Add(key);
		if (!pool.TryGetValue(key, out GameObject parent) || parent == null)
		{
			parent = new GameObject("SCMA_" + key);
			UnityEngine.Object.DontDestroyOnLoad(parent);
			for (int i = 0; i < 2; i++)
			{
				GameObject seg = new GameObject(i == 0 ? "line" : "head");
				seg.transform.SetParent(parent.transform, false);
				LineRenderer lr = seg.AddComponent<LineRenderer>();
				lr.useWorldSpace = true;
				lr.loop = false;
				lr.positionCount = i == 0 ? 2 : 3;
				lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				lr.receiveShadows = false;
				lr.material = LineMat();
			}
			pool[key] = parent;
		}
		parent.SetActive(true);
		Vector3 dir = to - from;
		if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward; else dir.Normalize();
		Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
		Vector3 right = Vector3.Cross(up, dir).normalized;
		float wing = Mathf.Max(width * 4f, 0.9f);
		LineRenderer[] lrs = parent.GetComponentsInChildren<LineRenderer>();
		if (lrs == null || lrs.Length < 2) return;
		LineRenderer main = lrs[0], head = lrs[1];
		arrowMain[0] = from; arrowMain[1] = to;
		main.SetPositions(arrowMain);
		arrowHead[0] = to - dir * wing + right * wing * 0.6f;
		arrowHead[1] = to;
		arrowHead[2] = to - dir * wing - right * wing * 0.6f;
		head.SetPositions(arrowHead);
		main.startColor = c; main.endColor = c; main.widthMultiplier = width;
		head.startColor = c; head.endColor = c; head.widthMultiplier = width * 0.8f;
	}

	/// <summary>帧末：本轮未被刷新的标记全部隐藏（由 GodViewController.Tick 调用）。</summary>
	public static void EndFrame()
	{
		foreach (var kv in pool)
		{
			if (kv.Value == null) continue;
			if (!used.Contains(kv.Key) && kv.Value.activeSelf) kv.Value.SetActive(false);
		}
		used.Clear();
	}

	/// <summary>
	/// 0.9.1：RTS 角括号选中指示——四角 45° 圆弧，角间留缺口（父对象 + 4 段弧线，缩放父对象）。
	/// 比整圈圆环更有 RTS 辨识度，选中变化一目了然。
	/// </summary>
	public static void Bracket(string key, Vector3 groundPos, float radius, Color c, float width, bool visible)
	{
		if (!visible || radius <= 0f) return;
		used.Add(key);
		if (!pool.TryGetValue(key, out GameObject parent) || parent == null)
		{
			parent = new GameObject("SCMB_" + key);
			UnityEngine.Object.DontDestroyOnLoad(parent);
			for (int q = 0; q < 4; q++)
			{
				GameObject seg = new GameObject("c" + q);
				seg.transform.SetParent(parent.transform, false);
				LineRenderer lr = seg.AddComponent<LineRenderer>();
				lr.useWorldSpace = false;
				lr.loop = false;
				lr.positionCount = 9;
				lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
				lr.receiveShadows = false;
				lr.material = LineMat();
				Il2CppStructArray<Vector3> pts = new Il2CppStructArray<Vector3>(9);
				float start = q * 90f + 22.5f; // 每角 45° 弧，四角间各留 45° 缺口
				for (int i = 0; i < 9; i++)
				{
					float a = (start + 45f * i / 8f) * Mathf.Deg2Rad;
					pts[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				}
				lr.SetPositions(pts);
			}
			pool[key] = parent;
		}
		parent.SetActive(true);
		parent.transform.localScale = new Vector3(radius, 1f, radius);
		parent.transform.position = groundPos;
		foreach (Transform child in parent.transform)
		{
			LineRenderer lr = child.GetComponent<LineRenderer>();
			if (lr == null) continue;
			lr.startColor = c;
			lr.endColor = c;
			lr.widthMultiplier = width;
		}
	}
}
