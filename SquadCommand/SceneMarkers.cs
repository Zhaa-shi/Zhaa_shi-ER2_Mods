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
		foreach (string sn in new[] { "Sprites/Default", "Universal Render Pipeline/Unlit", "Particles/Standard Unlit", "Legacy Shaders/Particles/Alpha Blended" })
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
		try { labelFont = PhaseBarGUI.GetDefaultFont(); } catch { }
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
}
