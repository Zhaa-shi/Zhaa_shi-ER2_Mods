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
///
/// 1.2.1 性能：池条目改为 Mark 结构（**缓存 LineRenderer/Material/TextMesh 引用**）——
/// 此前每帧 GetComponent / GetComponentsInChildren（后者每帧分配数组）在标记多时造成 GC 尖刺，
/// 表现为"标记不够跟手"。现在每帧只剩 transform 赋值 + SetPositions。
/// </summary>
internal static class SceneMarkers
{
	private sealed class Mark
	{
		public GameObject go;
		public LineRenderer[] lrs;   // 线体（Ring/Line=1，Arrow=2，Bracket=4）
		public Material mat;         // Line 专用（虚线纹理实例）
		public TextMesh text;
	}

	private static readonly Dictionary<string, Mark> pool = new Dictionary<string, Mark>();
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

	/// <summary>1.2.7：**穿墙材质**（不做深度测试）——单位进建筑后被屋顶遮住，
	/// 用它在建筑内也能看到掩体/单位标记（用户反馈"进入后看不见，不知道有没有完成防御"）。</summary>
	private static Material lineMatNoDepth;
	internal static Material LineMatNoDepth()
	{
		if (lineMatNoDepth != null) return lineMatNoDepth;
		foreach (string sn in new[] { "GUI/Text Shader", "Hidden/Internal-Colored", "Sprites/Default" })
		{
			Shader sh = null;
			try { sh = Shader.Find(sn); } catch { }
			if (sh == null) continue;
			try
			{
				lineMatNoDepth = new Material(sh);
				try { lineMatNoDepth.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always); } catch { }
				try { lineMatNoDepth.renderQueue = 4000; } catch { } // Overlay：最后绘制
				return lineMatNoDepth;
			}
			catch { }
		}
		return LineMat();
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

	private static bool TryGet(string key, out Mark m)
	{
		return pool.TryGetValue(key, out m) && m != null && m.go != null;
	}

	private static GameObject NewRoot(string key, string prefix)
	{
		GameObject go = new GameObject(prefix + key);
		UnityEngine.Object.DontDestroyOnLoad(go);
		return go;
	}

	// —— 池化创建（各类型只建一次）——

	private static Mark GetRing(string key, bool throughWall = false)
	{
		if (TryGet(key, out Mark m)) return m;
		GameObject go = NewRoot(key, throughWall ? "SCMX_" : "SCM_");
		LineRenderer lr = go.AddComponent<LineRenderer>();
		lr.useWorldSpace = false;
		lr.loop = true;
		lr.positionCount = Segments;
		lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		lr.receiveShadows = false;
		lr.sharedMaterial = throughWall ? LineMatNoDepth() : LineMat();
		// 单位半径 1m 的圆（XZ 平面），缩放 GO 控制大小
		Il2CppStructArray<Vector3> pts = new Il2CppStructArray<Vector3>(Segments);
		for (int i = 0; i < Segments; i++)
		{
			float a = (float)i / Segments * Mathf.PI * 2f;
			pts[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
		}
		lr.SetPositions(pts);
		m = new Mark { go = go, lrs = new[] { lr } };
		pool[key] = m;
		return m;
	}

	private static Mark GetBracket(string key)
	{
		if (TryGet(key, out Mark m)) return m;
		GameObject parent = NewRoot(key, "SCMB_");
		LineRenderer[] lrs = new LineRenderer[4];
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
			lr.sharedMaterial = LineMat();
			Il2CppStructArray<Vector3> pts = new Il2CppStructArray<Vector3>(9);
			float start = q * 90f + 22.5f; // 每角 45° 弧，四角间各留 45° 缺口
			for (int i = 0; i < 9; i++)
			{
				float a = (start + 45f * i / 8f) * Mathf.Deg2Rad;
				pts[i] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			}
			lr.SetPositions(pts);
			lrs[q] = lr;
		}
		m = new Mark { go = parent, lrs = lrs };
		pool[key] = m;
		return m;
	}

	private static Mark GetArrow(string key)
	{
		if (TryGet(key, out Mark m)) return m;
		GameObject parent = NewRoot(key, "SCMA_");
		LineRenderer[] lrs = new LineRenderer[2];
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
			lr.sharedMaterial = LineMat();
			lrs[i] = lr;
		}
		m = new Mark { go = parent, lrs = lrs };
		pool[key] = m;
		return m;
	}

	private static Texture2D dashTex;
	/// <summary>1.0.4：程序化虚线纹理（前段不透明/后段透明，Repeat 平铺）——路线用 LineTextureMode.Tile 拉成虚线。</summary>
	private static Texture2D DashTex()
	{
		if (dashTex != null) return dashTex;
		Texture2D t = new Texture2D(32, 1, TextureFormat.RGBA32, false);
		t.wrapMode = TextureWrapMode.Repeat;
		Color[] cols = new Color[32];
		for (int i = 0; i < 32; i++) cols[i] = i < 14 ? Color.white : new Color(1f, 1f, 1f, 0f);
		t.SetPixels(cols);
		t.Apply();
		dashTex = t;
		return t;
	}

	private static Mark GetLine(string key)
	{
		if (TryGet(key, out Mark m)) return m;
		GameObject go = NewRoot(key, "SCML_");
		LineRenderer lr = go.AddComponent<LineRenderer>();
		lr.useWorldSpace = true;
		lr.loop = false;
		lr.positionCount = 2;
		lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		lr.receiveShadows = false;
		lr.textureMode = LineTextureMode.Tile; // 虚线：按线长平铺 DashTex
		Material mat = LineMat() != null ? new Material(LineMat().shader) : null;
		if (mat != null)
		{
			mat.mainTexture = DashTex();
			lr.sharedMaterial = mat;
		}
		m = new Mark { go = go, lrs = new[] { lr }, mat = mat };
		pool[key] = m;
		return m;
	}

	private static Mark GetLabel(string key)
	{
		if (TryGet(key, out Mark m)) return m;
		GameObject go = NewRoot(key, "SCMT_");
		TextMesh tm = go.AddComponent<TextMesh>();
		tm.fontSize = 48;
		tm.characterSize = 0.085f;
		tm.anchor = TextAnchor.LowerCenter;
		tm.alignment = TextAlignment.Center;
		Font f = LabelFont();
		if (f != null) { tm.font = f; tm.GetComponent<MeshRenderer>().sharedMaterial = f.material; }
		m = new Mark { go = go, text = tm };
		pool[key] = m;
		return m;
	}

	private static Mark GetDot(string key)
	{
		if (TryGet(key, out Mark m)) return m;
		GameObject go = NewRoot(key, "SCMD_");
		MeshFilter mf = go.AddComponent<MeshFilter>();
		MeshRenderer mr = go.AddComponent<MeshRenderer>();
		mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
		mr.receiveShadows = false;
		Material mat = LineMat() != null ? new Material(LineMat().shader) : null;
		// 圆盘网格（单位半径 1m），顶点色烘焙成目标色，避免依赖 shader _Color 通道
		Mesh mesh = new Mesh();
		int seg = 28;
		Il2CppStructArray<Vector3> verts = new Il2CppStructArray<Vector3>(seg + 1);
		Il2CppStructArray<int> tris = new Il2CppStructArray<int>(seg * 3);
		Il2CppStructArray<Color> cols = new Il2CppStructArray<Color>(seg + 1);
		verts[0] = Vector3.zero;
		cols[0] = Color.white;
		for (int i = 0; i < seg; i++)
		{
			float a = (float)i / seg * Mathf.PI * 2f;
			verts[i + 1] = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
			cols[i + 1] = Color.white;
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
		m = new Mark { go = go, mat = mat };
		if (mat != null) mr.sharedMaterial = mat;
		pool[key] = m;
		return m;
	}

	/// <summary>画/刷新一个世界空间圆环（radius&lt;=0 时隐藏）。</summary>
	public static void Ring(string key, Vector3 groundPos, float radius, Color c, float width, bool visible, bool throughWall = false)
	{
		if (!visible || radius <= 0f) return;
		used.Add(key);
		Mark m = GetRing(key, throughWall);
		m.go.SetActive(true);
		m.go.transform.localScale = new Vector3(radius, 1f, radius);
		m.go.transform.position = groundPos;
		LineRenderer lr = m.lrs[0];
		lr.startColor = c;
		lr.endColor = c;
		lr.widthMultiplier = width;
	}

	/// <summary>世界空间文字名签（自动朝向相机）。</summary>
	public static void Label(string key, Vector3 pos, string text, Color c, bool visible)
	{
		if (!visible || string.IsNullOrEmpty(text)) return;
		used.Add(key);
		Mark m = GetLabel(key);
		m.go.SetActive(true);
		TextMesh t = m.text;
		if (t.text != text) t.text = text;
		t.color = c;
		m.go.transform.position = pos;
		Camera cam = GodViewController.MainCam();
		if (cam != null)
			m.go.transform.rotation = Quaternion.LookRotation(m.go.transform.position - cam.transform.position);
	}

	/// <summary>0.9.2：地面实心圆点（程序化圆盘 Mesh，顶点色烘焙）。</summary>
	public static void Dot(string key, Vector3 groundPos, float radius, Color c, bool visible)
	{
		if (!visible || radius <= 0f) return;
		used.Add(key);
		Mark m = GetDot(key);
		if (m.mat != null) m.mat.SetColor("_Color", c); // 实例材质：改色不污染共享材质
		m.go.SetActive(true);
		m.go.transform.localScale = new Vector3(radius, 1f, radius);
		m.go.transform.position = groundPos;
	}

	// 箭头两翼/主线坐标暂存（SetPositions 立即拷贝，多支箭头可复用）
	private static readonly Il2CppStructArray<Vector3> arrowMain = new Il2CppStructArray<Vector3>(2);
	private static readonly Il2CppStructArray<Vector3> arrowHead = new Il2CppStructArray<Vector3>(3);

	/// <summary>1.0.2：世界空间箭头（主线 + 两翼），用于朝向/阵型拖动指示。每帧调用刷新位置。</summary>
	public static void Arrow(string key, Vector3 from, Vector3 to, Color c, float width, bool visible)
	{
		if (!visible) return;
		used.Add(key);
		Mark m = GetArrow(key);
		m.go.SetActive(true);
		Vector3 dir = to - from;
		if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward; else dir.Normalize();
		Vector3 up = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) > 0.95f ? Vector3.right : Vector3.up;
		Vector3 right = Vector3.Cross(up, dir).normalized;
		float wing = Mathf.Max(width * 5f, 0.4f); // 1.0.6：箭头翼随线宽缩放（细线不再配大翼）
		LineRenderer main = m.lrs[0], head = m.lrs[1];
		arrowMain[0] = from; arrowMain[1] = to;
		main.SetPositions(arrowMain);
		arrowHead[0] = to - dir * wing + right * wing * 0.6f;
		arrowHead[1] = to;
		arrowHead[2] = to - dir * wing - right * wing * 0.6f;
		head.SetPositions(arrowHead);
		main.startColor = c; main.endColor = c; main.widthMultiplier = width;
		head.startColor = c; head.endColor = c; head.widthMultiplier = width * 0.8f;
	}

	/// <summary>1.0.3：两点直线（1.0.4 起为灰色虚线），用于单位行进路线标识。每帧调用刷新位置。</summary>
	public static void Line(string key, Vector3 from, Vector3 to, Color c, float width, bool visible)
	{
		if (!visible) return;
		used.Add(key);
		Mark m = GetLine(key);
		m.go.SetActive(true);
		LineRenderer l = m.lrs[0];
		arrowMain[0] = from; arrowMain[1] = to;
		l.SetPositions(arrowMain);
		l.startColor = c; l.endColor = c; l.widthMultiplier = width;
		// 平铺密度：约 2m 一个虚线周期
		if (m.mat != null)
		{
			float len = Vector3.Distance(from, to);
			m.mat.mainTextureScale = new Vector2(Mathf.Max(0.5f, len / 2f), 1f);
		}
	}

	/// <summary>帧末：本轮未被刷新的标记全部隐藏（由 GodViewController.Tick 调用）。</summary>
	public static void EndFrame()
	{
		foreach (var kv in pool)
		{
			Mark m = kv.Value;
			if (m == null || m.go == null) continue;
			if (!used.Contains(kv.Key) && m.go.activeSelf) m.go.SetActive(false);
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
		Mark m = GetBracket(key);
		m.go.SetActive(true);
		m.go.transform.localScale = new Vector3(radius, 1f, radius);
		m.go.transform.position = groundPos;
		for (int i = 0; i < m.lrs.Length; i++)
		{
			LineRenderer lr = m.lrs[i];
			if (lr == null) continue;
			lr.startColor = c;
			lr.endColor = c;
			lr.widthMultiplier = width;
		}
	}
}
