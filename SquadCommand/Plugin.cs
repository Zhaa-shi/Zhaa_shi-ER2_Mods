using System;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace ER2SquadCommand;

[BepInPlugin("er2.squadcommand", "ER2 Battlefield Commander", "1.4.18")]
public class Plugin : BasePlugin
{
	internal static ManualLogSource ModLog;

	internal static ConfigEntry<bool> enabled;
	internal static ConfigEntry<float> radius;
	internal static ConfigEntry<bool> debugLog;
	internal static ConfigEntry<KeyCode> godKey;
	// 1.2.0：命令快捷键（原命令环 8 项键盘化，全部可自定义）
	internal static ConfigEntry<KeyCode> keyStand;
	internal static ConfigEntry<KeyCode> keyCrouch;
	internal static ConfigEntry<KeyCode> keyProne;
	internal static ConfigEntry<KeyCode> keyStop;
	internal static ConfigEntry<KeyCode> keyHoldFire;
	internal static ConfigEntry<KeyCode> keyCover;
	internal static ConfigEntry<KeyCode> keyRally;
	internal static ConfigEntry<KeyCode> keyScatter;
	internal static ConfigEntry<KeyCode> keyPack;   // 1.3.0：背包窗口
	internal static ConfigEntry<float> packRange;   // 1.3.0：背包联动半径
	internal static ConfigEntry<bool> ghostPreview;
	internal static ConfigEntry<bool> customCursor;
	internal static ConfigEntry<string> cursorStyle; // 1.2.13：Arrow（默认）/ Cross
	internal static ConfigEntry<string> uiColorBase;
	internal static ConfigEntry<string> uiColorHover;
	internal static ConfigEntry<string> uiColorText;

	public override void Load()
	{
		ModLog = Log;

		enabled = Config.Bind("General", "enabled", true, Ui.Tr("主开关。"));
		radius = Config.Bind("Control", "moveRadius", 8f, new ConfigDescription(Ui.Tr("移动到达判定半径（米）；双击右键「前往并防守」的防守半径同用此值。"), new AcceptableValueRange<float>(1f, 60f)));
		debugLog = Config.Bind("Debug", "debugLog", false, new ConfigDescription(Ui.Tr("调试日志开关（发布版保持关闭）。开启后输出全部指挥/登车/标记/阵型/背包/穿戴诊断日志，用于问题排查。")));
		godKey = Config.Bind("General", "godKey", KeyCode.F9, Ui.Tr("上帝视角开关（仅进入）。退出＝选中小队后点顶部[控制该小队]随机接管一人；全军覆没时按键紧急退出。空格＝暂停/继续世界。"));

		keyStand = Config.Bind("Hotkeys", "keyStand", KeyCode.Z, Ui.Tr("站起（恢复 AI 姿态）。"));
		keyCrouch = Config.Bind("Hotkeys", "keyCrouch", KeyCode.X, Ui.Tr("蹲下。"));
		keyProne = Config.Bind("Hotkeys", "keyProne", KeyCode.C, Ui.Tr("趴下。"));
		keyStop = Config.Bind("Hotkeys", "keyStop", KeyCode.V, Ui.Tr("停止（取消移动/标记，单位原地）。"));
		keyHoldFire = Config.Bind("Hotkeys", "keyHoldFire", KeyCode.B, Ui.Tr("停火/开火切换。"));
		keyCover = Config.Bind("Hotkeys", "keyCover", KeyCode.N, Ui.Tr("就近掩体（以选中中心为准找掩护）。"));
		keyRally = Config.Bind("Hotkeys", "keyRally", KeyCode.M, Ui.Tr("集合（各队向班长集结）。"));
		keyScatter = Config.Bind("Hotkeys", "keyScatter", KeyCode.F, Ui.Tr("分散（各队就地散开找掩护）。"));
		keyPack = Config.Bind("Hotkeys", "keyPack", KeyCode.G, Ui.Tr("打开/关闭焦点单位（步兵背包/载具货舱/尸体）的格子背包窗口。可开多个窗口，拖拽交换物品。"));
		// 1.4.8：半径回归 3m（用户规则）；兵停驻在圈外时由 LootTick 逐步重派移动带进圈（不再放大半径）
		packRange = Config.Bind("Control", "packRange", 3f, new ConfigDescription(Ui.Tr("背包联动半径（米）：第一个打开的背包为锚点，其余背包距锚点超过此值将无法打开/自动关闭。"), new AcceptableValueRange<float>(1f, 100f)));
		ghostPreview = Config.Bind("Control", "ghostPreview", true, Ui.Tr("阵型拖动中的白色半透明单位预览（克隆失败会自动降级为标记）。"));
		customCursor = Config.Bind("Control", "customCursor", true, Ui.Tr("自定义光标（RTS 内按指向对象变色：友军绿/敌军红/载具青/建筑黄/火力点橙/可交互浅蓝）。"));
		cursorStyle = Config.Bind("Control", "cursorStyle", "Circle", new ConfigDescription(Ui.Tr("光标样式：Circle=空心半透明圆（默认）/ Arrow=箭头 / Cross=细线十字。"), new AcceptableValueList<string>("Circle", "Arrow", "Cross")));
		cursorStyle.SettingChanged += (s2, e2) => MouseCursor.InvalidateCache();

		uiColorBase = Config.Bind("UI", "colorBase", "#0E1C0EB4", new ConfigDescription(Ui.Tr("UI 主色（#RRGGBB 或 #RRGGBBAA）：按钮底板、小队列表行。默认深绿半透明（与底部提示条一致）。")));
		uiColorHover = Config.Bind("UI", "colorHover", "#3E703EE0", Ui.Tr("UI 悬停/选中指示颜色（中绿）。"));
		uiColorText = Config.Bind("UI", "colorText", "#DFF0DF", Ui.Tr("UI 文字/描边颜色。"));
		uiColorBase.SettingChanged += (s, e) => GodViewController.ApplyUiTheme();
		uiColorHover.SettingChanged += (s, e) => GodViewController.ApplyUiTheme();
		uiColorText.SettingChanged += (s, e) => GodViewController.ApplyUiTheme();
		GodViewController.ApplyUiTheme();

		new Harmony("er2.squadcommand").PatchAll(typeof(Plugin).Assembly);
		FrameEndRunner.Ensure();
		ModLog.LogInfo("ER2 Battlefield Commander 1.4.18 loaded. godKey=" + godKey.Value);
		ThirdPartyCompat.LogCoexistenceHint(ModLog); // 1.4.15：第三方 mod 共存提示
	}
}

/// <summary>1.4.15：第三方 mod 共存。
/// 只做**探测 + 提示**，不做任何编译期依赖（反射扫已加载程序集，找不到就是没装）。
/// Advanced Combat Movement（Responsive Orders，GUID "AdvancedCombatMovement"）与
/// 本 mod 同时装时的已知分工：
/// · 停火还原被它吞掉 → 已在 GodViewController.ResumeFire 里改成「校验 + 直写字段」兜底；
/// · 它的 F 键（标记/跟随）走 PlayerController.Update，上帝视角内该 Update 被本 mod 跳过
///   → RTS 内由本 mod 独占 F（= 分散），FPS 内归它，**两者不打架**；
/// · 它自带的 AI 防守驻留（Defensive Hold / Danger Memory）会给小队下自己的移动令，
///   与我们的命令抢控制权——这类冲突只能由玩家在它的设置页里关掉对应开关。</summary>
internal static class ThirdPartyCompat
{
	private static bool probed;
	private static bool acmPresent;

	/// <summary>是否检测到 Advanced Combat Movement（结果缓存，只扫一次）。</summary>
	internal static bool AcmPresent
	{
		get { if (!probed) { probed = true; acmPresent = ScanFor("ResponsiveOrdersPlugin"); } return acmPresent; }
	}

	private static bool ScanFor(string typeName)
	{
		try
		{
			foreach (System.Reflection.Assembly asm in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type t = asm.GetType(typeName, false, false);
				if (t != null) return true;
			}
		}
		catch { }
		return false;
	}

	internal static void LogCoexistenceHint(ManualLogSource log)
	{
		try
		{
			if (!AcmPresent) return;
			log.LogInfo("Third-party coex: Advanced Combat Movement detected — hold-fire restore is guarded; if your orders get overridden, disable its 'Squad Defensive Hold' / 'Danger Memory' in its settings page.");
		}
		catch { }
	}
}

/// <summary>
/// 帧末兜底宿主：每帧 WaitForEndOfFrame（LateUpdate 之后、渲染之前）调用 GodViewController.FrameEndGuard，
/// 覆盖"锁源在 LateUpdate 之后"的鼠标抢锁，压住闪烁；同时限频记录诊断日志。
/// </summary>
internal class FrameEndRunner : MonoBehaviour
{
	private static FrameEndRunner instance;

	public FrameEndRunner(IntPtr ptr) : base(ptr) { }

	internal static void Ensure()
	{
		try
		{
			if (instance != null) return;
			ClassInjector.RegisterTypeInIl2Cpp<FrameEndRunner>();
			GameObject go = new GameObject("ER2SquadCommandRunner");
			UnityEngine.Object.DontDestroyOnLoad(go);
			instance = go.AddComponent<FrameEndRunner>();
		}
		catch (Exception ex) { Plugin.ModLog.LogError("FrameEndRunner 启动失败: " + ex.Message); }
	}

	private void Start()
	{
		StartCoroutine(BepInEx.Unity.IL2CPP.Utils.Collections.CollectionExtensions.WrapToIl2Cpp(Loop()));
	}

	private static System.Collections.IEnumerator Loop()
	{
		while (true)
		{
			yield return new WaitForEndOfFrame();
			try { GodViewController.FrameEndGuard(); } catch { }
		}
	}

	/// <summary>1.4.2：启动原生（IL2CPP 侧）协程——Lua_Soldier.LoadAndSetWeapon 等官方异步通道。
	/// interop 返回的只是协程对象，必须显式 StartCoroutine 才会运行（陷阱 25 同源；模式同 UniGen.CoroutineHost）。</summary>
	internal static void RunNativeCoroutine(Il2CppSystem.Collections.IEnumerator routine)
	{
		try
		{
			if (routine == null || instance == null) return;
			instance.StartCoroutine(routine);
		}
		catch (Exception ex) { Plugin.ModLog.LogError("RunNativeCoroutine 失败: " + ex.Message); }
	}
}

/// <summary>0.7.86 B：受控单位死亡瞬间（原生 Update 前）修复 Squad 归属，防空候选。</summary>
[HarmonyPatch(typeof(PlayerController), "Update")]
public static class DeathGuardPatch
{
	private static void Prefix()
	{
		try { if (Plugin.enabled.Value) GodViewController.DeathGuardCheck(); } catch { }
	}
}

/// <summary>输入与追踪轮询（PlayerController.Update Postfix）。</summary>
[HarmonyPatch(typeof(PlayerController), "Update")]
public static class InputPatch
{
	private static void Postfix()
	{
		try
		{
			if (!Plugin.enabled.Value)
			{
				return;
			}
			GodViewController.TickFly();
			GodViewController.Tick();
			GodViewController.PostTakeoverGuard(); // 接管抑制期每帧兜底关闭"选择队友"面板
			if (Plugin.godKey.Value != KeyCode.None && Input.GetKeyDown(Plugin.godKey.Value))
			{
				GodViewController.Toggle();
				return;
			}
			GodViewController.EnsureTakeoverProtection();
			// GodViewController.EnsurePlayerSquadHasCandidates(); // 0.7.86 停用：全灭时劫持原生"选择新小队"流程（0.7.34 遗留，真因已由 ClearSquadList 修复取代）
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("SquadCommand update error: " + ex.Message);
		}
	}
}

/// <summary>上帝视角激活时吞掉玩家输入与游戏相机控制（Prefix 返回 false 跳过原方法）。</summary>
[HarmonyPatch(typeof(PlayerController), "Update")]
public static class GodViewSkipUpdatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>上帝视角激活时接管相机：直接跳过原 LateUpdate（含鼠标锁定/相机回中逻辑，否则点击时光标被锁回中央并闪烁）。</summary>
[HarmonyPatch(typeof(PlayerController), "LateUpdate")]
public static class GodViewCameraPatch
{
	private static bool Prefix()
	{
		if (!GodViewController.Active) return true; // 正常时原逻辑照跑
		GodViewController.LateApply(); // 上帝视角：接管相机并跳过原方法
		return false;
	}
}

/// <summary>上帝视角激活/接管后抑制期内拦截重生面板显示（SetPlayer(null) 会触发，导致"选择队友"提示挡住左下角）。</summary>
[HarmonyPatch(typeof(RespawnPanel), "EnableRespawnPanel")]
public static class GodViewBlockRespawnEnablePatch
{
	private static bool Prefix()
	{
		return !GodViewController.SuppressingUi;
	}
}

/// <summary>同上：拦截 SetRespawningView（重生视图切换）。</summary>
[HarmonyPatch(typeof(RespawnPanel), "SetRespawningView")]
public static class GodViewBlockRespawnViewPatch
{
	private static bool Prefix()
	{
		return !GodViewController.SuppressingUi;
	}
}

/// <summary>拦截 PlayerGUI.ShowSquadList：上帝视角/接管抑制期全拦；平时拦"空选项"提示（除自己外无存活队友，弹了也没得选）。</summary>
[HarmonyPatch(typeof(PlayerGUI), "ShowSquadList")]
public static class GodViewBlockSquadListPatch
{
	private static bool Prefix(Squad squad)
	{
		if (GodViewController.SuppressingUi) return false;
		// 0.7.86：不再拦"空候选"——原生 ShowSquadList 内含空候选兜底（转增援/重生），
		// 拦掉它 = 兜底永远不跑 = 卡死在选择提示。只在接管抑制窗内拦。
		return true;
	}
}

/// <summary>拦截 PlayerGUI.UpdateSquadGUI（面板每帧更新入口，真实战斗中被反复调用）。</summary>
[HarmonyPatch(typeof(PlayerGUI), "UpdateSquadGUI")]
public static class GodViewBlockSquadUpdatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.SuppressingUi; // 0.7.86：撤销——重生推进也在这些回调里，屏蔽会冻结流程
	}
}

/// <summary>拦截 PlayerGUI.StartSquadSelection：上帝视角/抑制期全拦；平时无其他存活队友时拦（避免空提示）。</summary>
[HarmonyPatch(typeof(PlayerGUI), "StartSquadSelection")]
public static class GodViewBlockSquadStartPatch
{
	private static bool Prefix()
	{
		if (GodViewController.SuppressingUi) return false;
		// 0.7.86：同 ShowSquadList——空候选交给原生兜底
		return true;
	}
}

/// <summary>上帝视角期间/接管后短暂禁用游戏原生"切换小队存活成员"圆形菜单（与 mod 的接管控制重复/冲突）。</summary>
[HarmonyPatch(typeof(PlayerController), "ShowSwitchMemberSelection")]
public static class GodViewBlockSwitchMemberPatch
{
	private static bool Prefix()
	{
		return !GodViewController.SuppressingUi; // 0.7.86：撤销——重生推进也在这些回调里，屏蔽会冻结流程
	}
}

/// <summary>上帝视角/接管抑制期跳过 PlayerGUI.LateUpdate：它每帧刷新，会把"选择队友"面板重新打开（前缀屏蔽仍会残留状态）。</summary>
[HarmonyPatch(typeof(PlayerGUI), "LateUpdate")]
public static class GodViewSkipPlayerGuiLatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.SuppressingUi; // 0.7.86：撤销——重生推进也在这些回调里，屏蔽会冻结流程 // 0.7.86：拔掉空候选面板的重开驱动
	}
}

/// <summary>上帝视角激活时跳过 FPSGunManager 的相机/手部更新（防视角抖动 + 防鼠标锁定被重置）。</summary>
[HarmonyPatch(typeof(FPSGunManager), "Update")]
public static class GodViewSkipFpsGunUpdatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>同上：跳过 FPSGunManager.LateUpdate（相机抖动主源之一）。</summary>
[HarmonyPatch(typeof(FPSGunManager), "LateUpdate")]
public static class GodViewSkipFpsGunLatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>上帝视角时跳过 PlayerController.FixedUpdate（Move/物理/相机跟随，避免与 mod 相机打架）。</summary>
[HarmonyPatch(typeof(PlayerController), "FixedUpdate")]
public static class GodViewSkipPlayerFixedPatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

// ---- 禁用以下的原生相机/输入控制器，否则每帧仍会改写主相机并把光标锁回中央（单位/画面卡顿 + 光标闪烁的共同根因）----

/// <summary>跳过 CameraDirector.Update（相机导演每帧改写主相机）。</summary>
[HarmonyPatch(typeof(CameraDirector), "Update")]
public static class GodViewSkipCameraDirectorUpdatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>跳过 CameraDirector.FixedUpdate。</summary>
[HarmonyPatch(typeof(CameraDirector), "FixedUpdate")]
public static class GodViewSkipCameraDirectorFixedPatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>跳过 CameraDirector.LateUpdate。</summary>
[HarmonyPatch(typeof(CameraDirector), "LateUpdate")]
public static class GodViewSkipCameraDirectorLatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>跳过 CameraDirector.UpdateDOF（景深/成像更新，防止相机被重置）。</summary>
[HarmonyPatch(typeof(CameraDirector), "UpdateDOF")]
public static class GodViewSkipCameraDirectorDofPatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>
/// 1.2.10：**跳过 TerrainCamera.Update**——这是"长按右键拖动视角被带动"的真凶。
/// TerrainCamera 是游戏内置的自由/地形相机（字段：orbit / mouseRotate / distanceToTarget /
/// mainSpeed / GetBaseInput），god view 把玩家设为"无单位"（SetPlayer(null)）后游戏启用了它，
/// 按住鼠标即旋转视角，与我们的 camPos/camRot 互相打架。
/// 之前只 patch 了 CameraDirector/SimpleCameraController，漏了这一个。
/// </summary>
[HarmonyPatch(typeof(TerrainCamera), "Update")]
public static class GodViewSkipTerrainCameraPatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>跳过 SimpleCameraController.Update。</summary>
[HarmonyPatch(typeof(SimpleCameraController), "Update")]
public static class GodViewSkipSimpleCamUpdatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>跳过 CinematicCameraController.Update（电影相机/锁定相机）。</summary>
[HarmonyPatch(typeof(Corvostudio.CinematicCamera.CinematicCameraController), "Update")]
public static class GodViewSkipCineCamUpdatePatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>跳过 CinematicCameraController.FixedUpdate。</summary>
[HarmonyPatch(typeof(Corvostudio.CinematicCamera.CinematicCameraController), "FixedUpdate")]
public static class GodViewSkipCineCamFixedPatch
{
	private static bool Prefix()
	{
		return !GodViewController.Active;
	}
}

/// <summary>0.7.86：上帝视角 SetPlayer(null) 使原生 Vehicle.PlayerIsInside 空引用（每帧 NRE 且中断 AIVehicle.Update）——
/// 玩家不受控时直接视为"不在车内"，其余情况放行原生。仅此一个保护点，不动驾驶链。</summary>
[HarmonyPatch(typeof(Vehicle), "PlayerIsInside")]
public static class VehiclePlayerIsInsideGuardPatch
{
	private static bool Prefix(ref bool __result)
	{
		try
		{
			// 0.7.86：保护窗=上帝视角 或 接管后抑制窗（这两种状态玩家可能不受控）
			bool guard = GodViewController.Active || Time.unscaledTime < GodViewController.SuppressSwitchMemberUntil;
			if (!guard) return true; // 正常游戏：放行原生
			PlayerController pc = PlayerController.currentController;
			if (pc != null && pc.ControlledCharacter != null) return true; // 有受控角色：原生自判
			__result = false; // 玩家不受控（上帝视角/重生间隙）：视为不在任何车内
			return false;
		}
		catch { return true; }
	}
}

/// <summary>
/// 1.2.7：幽灵预览**伤害免疫**。预览幽灵是真实 Soldier/Vehicle 生成物，
/// allowBeingTargeted(false) 挡不住"已经锁定它的敌人"（用户两次反馈预览被打掉）——
/// 这里直接在伤害入口拒绝：幽灵受到的伤害一律丢弃。
/// </summary>
[HarmonyPatch(typeof(Creature), "Damage")]
public static class GhostDamageImmunityPatch
{
	private static bool Prefix(Creature __instance)
	{
		try { if (GhostPreview.IsGhost(__instance)) return false; } catch { }
		return true;
	}
}

/// <summary>调试 HUD（PlayerController.OnGUI Postfix）。</summary>
[HarmonyPatch(typeof(PlayerController), "OnGUI")]
[HarmonyPriority(800)] // 1.2.18：最后执行 → 自绘光标压过其它 IMGUI（如通用生成面板）
public static class DrawPatch
{
	private static void Postfix()
	{
		try
		{
			GodViewController.DrawHud();
		}
		catch (Exception ex)
		{
			Plugin.ModLog.LogError("SquadCommand draw error: " + ex.Message);
		}
	}
}

/// <summary>上帝视角激活时，原生 Cursor.set_lockState 强制为 None（根治右键闪烁+重置中央）。</summary>
[HarmonyPatch(typeof(UnityEngine.Cursor), "set_lockState")]
public static class CursorLockPatch
{
	private static void Prefix(ref CursorLockMode value)
	{
		if (GodViewController.Active) value = CursorLockMode.None;
	}
}

/// <summary>上帝视角激活时，原生 Cursor.set_visible 强制为 false（1.2.7：改 IMGUI 自绘光标后
/// 由我们负责绘制，系统光标必须藏起来；关闭 customCursor 或 ESC 菜单打开时放行原生）。</summary>
[HarmonyPatch(typeof(UnityEngine.Cursor), "set_visible")]
public static class CursorVisiblePatch
{
	private static void Prefix(ref bool value)
	{
		if (GodViewController.Active && MouseCursor.OwnsCursor) value = true; // 1.2.19：SetCursor 贴图只在 visible 时显示
	}
}

/// <summary>
/// 标记集火：覆盖 Soldier.GetBestVisibleEnemy（Postfix）。
/// 只对【下达标记时的单位快照】生效（步兵选择 + 选中载具车组），走原生目标选择让 AI 自然集火，
/// 零强制态 —— HVT 老兵团已实证此路径有效。攻击/标记指令原用 forceTarget（从未生效），
/// 改为记录 Spottable 让 AI 自行选择。
/// 0.9.16：行军停火改走原生 SetHoldFireOrder 通道，本方法不再做全盲切断
///（实测会引发任务系统异常：单位罚站/大面积冻结，且拦不住任务级打断）。
/// </summary>
[HarmonyPatch(typeof(Soldier), "GetBestVisibleEnemy")]
internal static class MarkedTargetSelectionPatch
{
	private static void Postfix(Soldier __instance, ref Spottable __result, ref float dist)
	{
		try
		{
			var mark = GodViewController.CurrentMark;
			if (mark == null || mark.Spottable == null || __instance == null || __instance.transform == null)
			{
				return;
			}
			// 只引导下达标记时的单位（切回 FPS 后仍生效；后来接管的单位不受影响）
			if (!GodViewController.IsMarkUnit(__instance)) return;
			// 玩家自己瞄准/控制的单位不受强制
			// 1.4.14 性能：本 Postfix 每兵每帧都跑，玩家控制单位查询走宿主缓存（0.5s，见 CachedPlayerSoldier）
			Soldier ctrl = GodViewController.CachedPlayerSoldier();
			if (ctrl != null && __instance.Pointer == ctrl.Pointer) return;
			string viewerFac = "";
			try { viewerFac = __instance.faction ?? ""; } catch { }
			string myFac = GodViewController.MySideFaction();
			if (string.IsNullOrEmpty(viewerFac) || string.IsNullOrEmpty(myFac)) return;
			// 仅本阵营（友军）AI 受标记引导
			if (!SquadCmdLogic.Friendly(viewerFac, myFac)) return;
			// 标记目标必须不是本阵营的（友军不能打友军）
			if (SquadCmdLogic.Friendly(viewerFac, mark.Faction)) return;
			// 半径
			float radius = GodViewController.MarkRadius;
			float d2 = (mark.Position - __instance.transform.position).sqrMagnitude;
			if (d2 > radius * radius) return;
			// 视线（防穿墙锁）——用 PruneMark 的 0.5s 缓存，不再每兵每帧射线
			if (!GodViewController.MarkLosCached) return;
			__result = mark.Spottable;
			dist = Mathf.Sqrt(d2);
		}
		catch
		{
		}
	}
}

/// <summary>
/// 标记集火（载具版）：覆盖 Vehicle.CurrentVisibleTarget（getter）。
/// 选中载具车组时，让载具自身的炮塔/武器把被标记目标当作当前可见目标 ——
/// 解决"在载具里的单位攻击目标但单位不会行动"（步兵的 GetBestVisibleEnemy 覆盖不驱动载具炮塔）。
/// </summary>
[HarmonyPatch(typeof(Vehicle), "CurrentVisibleTarget", MethodType.Getter)]
internal static class MarkedVehicleTargetPatch
{
	private static void Postfix(Vehicle __instance, ref Spottable __result)
	{
		try
		{
			var mark = GodViewController.CurrentMark;
			if (mark == null || mark.Spottable == null || __instance == null) return;
			if (!GodViewController.IsMarkVehicle(__instance)) return;
			// 当前玩家若已接管该载具，交还给 FPS 原生目标链。
			try
			{
				// 1.4.14 性能：走宿主缓存（同 GetBestVisibleEnemy Postfix）
				Soldier ctrl = GodViewController.CachedPlayerSoldier();
				Vehicle playerVehicle = ctrl != null ? ctrl.GetComponentInParent<Vehicle>() : null;
				if (playerVehicle != null && playerVehicle.Pointer == __instance.Pointer) return;
			}
			catch { }
			__result = mark.Spottable;
		}
		catch
		{
		}
	}
}
