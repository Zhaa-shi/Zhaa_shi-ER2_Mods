using System;
using System.Collections;
using System.IO;
using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HVT = ER2VeteranHVT;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace HvtTestDriver;

// ─────────────────────────────────────────────────────────────
//  HVT Test Driver（内部自测工具，不发布）
//  进入战斗后自动：
//  1. 把离玩家最近的敌方 AI 直接设为 Lv.V 老兵（25 杀）
//  2. 触发红色闪烁 + 金色闪烁
//  3. 弹两条底部 toast（升级/击杀反馈样式）
//  4. 自动截屏 6 张到 research_out\hvt_shots\
//  用途：配合外部截图 + 视觉模型验证 HVT mod 的渲染输出。
// ─────────────────────────────────────────────────────────────

[BepInPlugin("er2.hvt.testdriver", "HVT Test Driver", "1.0.0")]
[BepInProcess("Easy Red 2.exe")]
public class Plugin : BasePlugin
{
	internal static ManualLogSource ModLog;

	public override void Load()
	{
		ModLog = Log;
		try
		{
			ClassInjector.RegisterTypeInIl2Cpp<DriverBehaviour>();
			GameObject go = new GameObject("HvtTestDriver");
			UnityEngine.Object.DontDestroyOnLoad(go);
			go.hideFlags = (HideFlags)61;
			go.AddComponent<DriverBehaviour>();
			ModLog.LogInfo("HVT Test Driver loaded.");
		}
		catch (Exception ex)
		{
			ModLog.LogWarning($"HVT Test Driver init failed: {ex.Message}");
		}
	}
}

internal class DriverBehaviour : MonoBehaviour
{
	private float _nextCheck;
	private bool _ran;
	private float _nextShotAt;
	private int _shotsLeft;
	private float _nextMenuShotAt;
	private int _menuShotsLeft = 300; // 菜单阶段最多 300 张（15 分钟），进战斗后停止

	private void Update()
	{
		try
		{
			if (_ran)
			{
				// 截屏状态机（IL2CPP 下 StartCoroutine 不兼容 C# IEnumerator，用 Update 驱动）
				if (_shotsLeft > 0 && Time.unscaledTime >= _nextShotAt)
				{
					_shotsLeft--;
					_nextShotAt = Time.unscaledTime + 1.2f;
					TakeShot("shot_");
				}
				return;
			}
			float now = Time.unscaledTime;
			// 菜单阶段截屏：进战斗前每 3s 一张（用游戏自身帧缓冲看菜单，窗口位置无所谓）
			if (_menuShotsLeft > 0 && now >= _nextMenuShotAt)
			{
				_menuShotsLeft--;
				_nextMenuShotAt = now + 3f;
				TakeShot("menu_");
			}
			if (now < _nextCheck)
			{
				return;
			}
			_nextCheck = now + 1f;

			var all = Creature.aliveCreatures;
			if (all == null || all.Count == 0)
			{
				return;
			}
			Soldier me = HVT.Plugin.ControlledSoldier();
			if (me == null || me.transform == null)
			{
				return;
			}
			string mySide = HVT.Plugin.SideOf(me.faction);

			// 找最近的敌方 AI（跨方即可，不限国家）
			Soldier target = null;
			float best = float.MaxValue;
			foreach (var c in all)
			{
				try
				{
					if (c == null || c.transform == null)
					{
						continue;
					}
					Soldier s = c.TryCast<Soldier>();
					if (s == null || s == me)
					{
						continue;
					}
					if (HVT.Plugin.IsPlayerUnit(s))
					{
						continue;
					}
					if (string.IsNullOrEmpty(mySide) || HVT.Plugin.SideOf(s.faction) == mySide)
					{
						continue;
					}
					float d = (s.transform.position - me.transform.position).sqrMagnitude;
					if (d < best)
					{
						best = d;
						target = s;
					}
				}
				catch
				{
				}
			}
			if (target == null)
			{
				HVT.Plugin.ModLog.LogInfo("[HVT-TEST] no enemy AI found nearby yet, waiting...");
				return;
			}

			_ran = true;
			try
			{
				// 1) 目标设为 Lv.V 老兵（25 杀，KillsPerLevel=5）
				HVT.Plugin.GetState(target).EnemyKills = 25;
				HVT.Plugin.ModLog.LogInfo($"[HVT-TEST] marked enemy {target.faction} as Lv.V veteran");

				// 2) 闪烁（金色击杀反馈，随后红色标记警告）
				HVT.HvtBehaviour.FlashPlayer(new Color(1f, 0.8f, 0.15f, 0.22f), 1.2f);
				HVT.Plugin.ModLog.LogInfo("[HVT-TEST] gold flash fired");

				// 3) 底部 toast（击杀反馈 + 升级反馈样式）
				HVT.HvtBehaviour.ShowToast("🎯 TEST 高危目标已消灭！(Lv.V 老兵)", 8f);
				HVT.HvtBehaviour.ShowToast("⭐ TEST 你晋升为 Lv.II 老兵！", 8f);
				HVT.Plugin.ModLog.LogInfo("[HVT-TEST] toasts fired");

				// 4) 截屏（6 张，间隔 1.2s，由 Update 状态机驱动）
				_nextShotAt = Time.unscaledTime + 1.2f;
				_shotsLeft = 6;
			}
			catch (Exception ex)
			{
				HVT.Plugin.ModLog.LogWarning($"[HVT-TEST] setup failed: {ex.Message}");
			}
		}
		catch
		{
		}
	}

	private void TakeShot(string prefix)
	{
		try
		{
			string dir = "D:/Users/71011/Documents/ER2_Mods/research_out/hvt_shots";
			Directory.CreateDirectory(dir);
			string path = $"{dir}/{prefix}{Time.unscaledTime.ToString("F0")}_{_shotsLeft}_{_menuShotsLeft}.png";
			ScreenCapture.CaptureScreenshot(path);
			HVT.Plugin.ModLog.LogInfo($"[HVT-TEST] screenshot: {path}");
		}
		catch (Exception ex)
		{
			HVT.Plugin.ModLog.LogWarning($"[HVT-TEST] shot failed: {ex.Message}");
		}
	}
}
