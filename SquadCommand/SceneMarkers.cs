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
		public MeshRenderer plate;   // 2.5.0：名签底板（Label 专用）
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

	/// <summary>
	/// 2.5.0：选中角标由**四段 45° 圆弧**改为**四个直角折角**（L 形括号）。
	///
	/// 为什么改：用户截图实证原形状"跟台风一样"——4 段同心弧 + 中心点，读起来像气象符号而非
	/// RTS 选中框。根因是斜视角下圆弧会被投影成椭圆，四段弧各自的"朝向"因此丢失，
	/// 只剩"围绕中心的四个弧块"这一模糊意象。**直角折角在任意视角下都保持 L 形可辨识**，
	/// 是 RTS（星际/红警/全面战争）通用的选中语言。
	///
	/// 几何：每个角一段折线，positionCount 9→3，坐标固定为
	///   [角内-edge 端点, 角顶点, 角内-另一edge 端点]
	/// loop=false（三段是一条开口折线，不能闭合）。父对象缩放 = 半径，4 段随父缩放。
	/// **4 个调用点（GVC 3633/3643、Formation 851/858）签名不变**——只换几何，不动接口。
	/// </summary>
	private static Mark GetBracket(string key)
	{
		if (TryGet(key, out Mark m)) return m;
		GameObject parent = NewRoot(key, "SCMB_");
		LineRenderer[] lrs = new LineRenderer[4];
		// 折角臂长（单位半径上的占比）：0.34 ≈ 视觉上"框角"而不连成整圈；
		// 留出的 0.66 缺口让四角明确分离，不像台风弧。
		const float arm = 0.34f;
		// 四角：(signX, signZ) = (-1,-1) (-1,+1) (+1,+1) (+1,-1)，逆时针对应 GVC 的四个象限
		float[] sx = { -1f, -1f, 1f, 1f };
		float[] sz = { -1f, 1f, 1f, -1f };
		for (int q = 0; q < 4; q++)
		{
			GameObject seg = new GameObject("c" + q);
			seg.transform.SetParent(parent.transform, false);
			LineRenderer lr = seg.AddComponent<LineRenderer>();
			lr.useWorldSpace = false;
			lr.loop = false;
			lr.positionCount = 3;
			lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			lr.receiveShadows = false;
			lr.sharedMaterial = LineMat();
			Il2CppStructArray<Vector3> pts = new Il2CppStructArray<Vector3>(3);
			float px = sx[q], pz = sz[q];
			// 沿 -Z 方向伸出的臂（在角顶的"横边"上）与沿 -X 方向伸出的臂（"竖边"上）
			pts[0] = new Vector3(px * 1f, 0f, pz * (1f - arm));
			pts[1] = new Vector3(px * 1f, 0f, pz * 1f);
			pts[2] = new Vector3(px * (1f - arm), 0f, pz * 1f);
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

	private static Texture2D dashTexLong, dashTexShort;

	/// <summary>
	/// 2.5.0：两种虚线节奏，用**节奏**而非颜色区分语义（灰黑主题下的主要手段）：
	///   long（默认）= 14/32 实 + 18/32 空 → 长划疏点，用于**行进路线**（背景信息，不抢眼）
	///   short       = 22/32 实 + 10/32 空 → 短划密点，用于**登车线**（当前操作，需要更实）
	/// 两者都由 32px 宽纹理平铺。原来只有一种 14/18，路线与登车线在视觉上完全无法区分。
	/// </summary>
	private static Texture2D DashTex(bool shortDash)
	{
		if (shortDash && dashTexShort != null) return dashTexShort;
		if (!shortDash && dashTexLong != null) return dashTexLong;
		int fill = shortDash ? 22 : 14;
		Texture2D t = new Texture2D(32, 1, TextureFormat.RGBA32, false);
		t.wrapMode = TextureWrapMode.Repeat;
		Color[] cols = new Color[32];
		for (int i = 0; i < 32; i++) cols[i] = i < fill ? Color.white : new Color(1f, 1f, 1f, 0f);
		t.SetPixels(cols);
		t.Apply();
		if (shortDash) dashTexShort = t; else dashTexLong = t;
		return t;
	}

	private static Mark GetLine(string key, bool shortDash)
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
			mat.mainTexture = DashTex(shortDash);
			lr.sharedMaterial = mat;
		}
		m = new Mark { go = go, lrs = new[] { lr }, mat = mat };
		pool[key] = m;
		return m;
	}

	/// <summary>
	/// 2.5.0：名签加**深色底板**。原来只有 TextMesh 白字，压在浅色地形/雪地/天空上读不清
	/// （TextMesh 无底板 API，见硬约束 C12 → 只能自己拼一块面片）。
	/// 实现：同一 GO 下挂一个子对象，MeshFilter 用一个 2 三角形面片，顶点色烘焙成
	/// WLabelPlate(#0C0C0C@0.90)；材质取 LineMat 的 shader（支持顶点色，无需 _Color 通道）。
	/// 面片尺寸跟随 TextMesh 的字符宽度（粗略估算即可，名签本身短）。
	/// </summary>
	private static void EnsurePlate(Mark m)
	{
		if (m.plate != null) return;
		try
		{
			GameObject go = new GameObject("plate");
			go.transform.SetParent(m.go.transform, false);
			MeshFilter mf = go.AddComponent<MeshFilter>();
			MeshRenderer mr = go.AddComponent<MeshRenderer>();
			mr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
			mr.receiveShadows = false;
			Material mat = LineMat() != null ? new Material(LineMat().shader) : null;
			Mesh mesh = new Mesh();
			// 单位面片（XZ 平面不行——名签朝相机，所以建在 XY 平面，随 transform.rotation 一起转）
			Il2CppStructArray<Vector3> verts = new Il2CppStructArray<Vector3>(4);
			verts[0] = new Vector3(-0.5f, 0f, 0f);
			verts[1] = new Vector3(0.5f, 0f, 0f);
			verts[2] = new Vector3(0.5f, 1f, 0f);
			verts[3] = new Vector3(-0.5f, 1f, 0f);
			Il2CppStructArray<int> tris = new Il2CppStructArray<int>(6);
			tris[0] = 0; tris[1] = 2; tris[2] = 1;
			tris[3] = 0; tris[4] = 3; tris[5] = 2;
			Color plate = ER2Shared.Er2Ui.WLabelPlate;
			Il2CppStructArray<Color> cols = new Il2CppStructArray<Color>(4);
			for (int i = 0; i < 4; i++) cols[i] = plate;
			mesh.vertices = verts;
			mesh.triangles = tris;
			mesh.colors = cols;
			mesh.RecalculateNormals();
			mesh.RecalculateBounds();
			mf.sharedMesh = mesh;
			if (mat != null) mr.sharedMaterial = mat;
			m.plate = mr;
		}
		catch (Exception ex)
		{
			SquadCmdLogic.LogWarning("[SceneMarkers] 名签底板创建失败: " + ex.Message);
		}
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
		EnsurePlate(m);
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

	/// <summary>世界空间文字名签（自动朝向相机）。2.5.0：带深色底板提升可读性。</summary>
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
		// 2.5.0：底板按文字宽度估算（CJK/西文混排，取每字符 ~0.9 * charSize 的保守值 + 内边距）
		if (m.plate != null && m.plate.transform != null)
		{
			float w = Mathf.Max(0.6f, text.Length * tm_charW + 0.3f);
			float h = 0.5f;
			// 文字 anchor=LowerCenter，底板放到文字基线稍下方，不遮字
			m.plate.transform.localPosition = new Vector3(0f, -h * 0.55f, 0.01f);
			m.plate.transform.localScale = new Vector3(w, h, 1f);
		}
	}

	/// <summary>名签每字符世界宽度估算值（charSize 0.085 下 CJK 约 0.085，西文约 0.05；取宽值保守）。</summary>
	private const float tm_charW = 0.085f;

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

	/// <summary>
	/// 1.0.3：两点直线（1.0.4 起为灰色虚线），用于单位行进路线/登车线标识。每帧调用刷新位置。
	/// 2.5.0：新增 <paramref name="shortDash"/> —— false=长划（行进路线），true=短划（登车线）。
	/// 纹理由 GetLine 首次创建时按 key 定形（PL*/FML* 用长划、PB* 用短划），后续调用保持。
	/// </summary>
	public static void Line(string key, Vector3 from, Vector3 to, Color c, float width, bool visible, bool shortDash = false)
	{
		if (!visible) return;
		used.Add(key);
		Mark m = GetLine(key, shortDash);
		m.go.SetActive(true);
		LineRenderer l = m.lrs[0];
		arrowMain[0] = from; arrowMain[1] = to;
		l.SetPositions(arrowMain);
		l.startColor = c; l.endColor = c; l.widthMultiplier = width;
		// 平铺密度：长划约 2m 一周期，短划约 1.2m（更密，读起来"更实"）
		if (m.mat != null)
		{
			float len = Vector3.Distance(from, to);
			float period = shortDash ? 1.2f : 2f;
			m.mat.mainTextureScale = new Vector2(Mathf.Max(0.5f, len / period), 1f);
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
	/// 0.9.1：RTS 角括号选中指示。2.5.0：几何由四段圆弧改为**四个直角折角**
	/// （见 GetBracket 注释——原弧形状被用户判为"跟台风一样"）。
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
