using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 1.0.2：载具朝向控制（地狱之门式：选中载具长按右键拖动，松开转向）。
/// 命令通道 = 原生 AIVehicle.faceDirWhenStopped（"停止时朝向"字段，Nullable&lt;Vector3&gt;）：
/// 先 StopAndClearPath 停车，写入朝向点后由原生 StaticVehicleRoutine 每帧原地转车体；
/// IsRotatedToward 到位或 12s 超时后清空字段，防残留让 AI 之后每次停稳都自动回转。
/// 兼容写法：写入"车位置+方向×30m"的世界点而非短向量——native 无论把它当方向（atan2 定航向）
/// 还是当目标点（转向该位置），朝向都收敛为拖动方向；短向量在"目标点"语义下会指向世界原点。
/// 注意：Nullable 字段只能整体赋新包装对象，赋 C# null 会在 il2cpp_object_unbox(0) 处崩游戏。
/// </summary>
internal static class VehicleFacing
{
    internal class FacingTask
    {
        public Vehicle veh;
        public AIVehicle ai;
        public Vector3 dirPoint;   // 朝向点（世界坐标）
        public float issuedAt;
        public float deadline;
        public int readbacks;      // 门控诊断：issue 后 1s/3s 两次读数
        public int fails;          // interop 异常连续计数（铁律 30：连续 3 次才判死）
    }

    private static readonly List<FacingTask> tasks = new List<FacingTask>();

    internal const float DragThresholdPx = 14f;  // 长按 0.35s 到点时的拖动判定阈值（像素）
    private const float DirPointDist = 30f;      // 朝向兼容点距离
    private const float TimeoutSeconds = 12f;
    internal const float ArrowAlpha = 0.95f;

    /// <summary>车上的 AIVehicle 组件（本体找不到再找子级，与 DriveVehicleTo 同款）。</summary>
    private static AIVehicle GetAi(Vehicle v)
    {
        try { AIVehicle ai = v.GetComponent<AIVehicle>(); if (ai != null) return ai; } catch { }
        try { return v.GetComponentInChildren<AIVehicle>(); } catch { return null; }
    }

    /// <summary>载具是否可转向：非飞机、有 AIVehicle、驾驶员存活、玩家未接管。</summary>
    internal static bool IsEligible(Vehicle v)
    {
        try
        {
            if (v == null || v.transform == null) return false;
            try { if (v.IsAirVehicle()) return false; } catch { return false; }
            if (GetAi(v) == null) return false;
            try { if (!v.HasDriverAlive) return false; } catch { }
            try
            {
                PlayerController pc = PlayerController.currentController;
                Soldier ctrl = pc != null ? pc.ControlledCharacter : null;
                Vehicle pv = ctrl != null ? ctrl.GetComponentInParent<Vehicle>() : null;
                if (pv != null && pv.Pointer == v.Pointer) return false;
            }
            catch { }
            return true;
        }
        catch { return false; }
    }

    internal static bool HasEligible(List<Vehicle> cands)
    {
        if (cands == null) return false;
        foreach (Vehicle v in cands) { if (IsEligible(v)) return true; }
        return false;
    }

    internal static int EligibleCount(List<Vehicle> cands)
    {
        int n = 0;
        if (cands == null) return 0;
        foreach (Vehicle v in cands) { if (IsEligible(v)) n++; }
        return n;
    }

    /// <summary>把选中载具转向 point（各自朝鼠标落点方向）。返回成功下达数。</summary>
    internal static int IssueFacing(List<Vehicle> cands, Vector3 point)
    {
        int issued = 0;
        if (cands == null || cands.Count == 0) return 0;
        foreach (Vehicle v in new List<Vehicle>(cands))
        {
            try
            {
                if (!IsEligible(v)) continue;
                AIVehicle ai = GetAi(v);
                long p = (long)v.Pointer;
                tasks.RemoveAll(t => { try { return t.veh == null || (long)t.veh.Pointer == p; } catch { return true; } });
                try { ai.StopAndClearPath(); } catch { }
                GodViewController.CancelVehicleMoveObservation(v); // 摘除该车移动观察，防与朝向任务互相打架
                ai.faceDirWhenStopped = new Il2CppSystem.Nullable<Vector3>(point);
                tasks.Add(new FacingTask { veh = v, ai = ai, dirPoint = point, issuedAt = Time.unscaledTime, deadline = Time.unscaledTime + TimeoutSeconds });
                issued++;
            }
            catch (Exception ex) { SquadCmdLogic.Log("[Facing] 下达失败 vehicle=" + GodViewController.SafeName(v) + ": " + ex.Message); }
        }
        if (issued > 0)
            SquadCmdLogic.LogAlways("[Facing] issue vehicles=" + issued + " point=" + point.ToString("0.0"));
        return issued;
    }

    /// <summary>新移动命令覆盖：清某车的朝向任务与字段（车到达后不得自行回转）。</summary>
    internal static void CancelFor(Vehicle v)
    {
        if (v == null) return;
        long p = 0;
        try { p = (long)v.Pointer; } catch { return; }
        bool had = false;
        for (int i = tasks.Count - 1; i >= 0; i--)
        {
            FacingTask t = tasks[i];
            try
            {
                if (t.veh != null && (long)t.veh.Pointer == p)
                {
                    ClearField(t);
                    tasks.RemoveAt(i);
                    had = true;
                }
            }
            catch { tasks.RemoveAt(i); }
        }
        if (had) SquadCmdLogic.Log("[Facing] 移动命令覆盖，已清该车朝向任务");
    }

    /// <summary>持久任务段：到位/超时/判死收尾。RTS 退出后继续生效（车在 FPS 视角下也能转完）。</summary>
    internal static void Tick()
    {
        if (tasks.Count == 0) return;
        float now = Time.unscaledTime;
        for (int i = tasks.Count - 1; i >= 0; i--)
        {
            FacingTask t = tasks[i];
            bool remove = false;
            try
            {
                if (t.veh == null || t.veh.transform == null) { tasks.RemoveAt(i); continue; }

                // 读数诊断（debugLog 门控）：角度递减=通道生效；faceDir 读回空=被原生清除/拒收
                if (t.readbacks < 2 && now - t.issuedAt > (t.readbacks == 0 ? 1f : 3f))
                {
                    t.readbacks++;
                    float ang = float.NaN;
                    bool hasFd = false;
                    try { ang = t.ai.GetAngleToward(t.dirPoint); } catch { }
                    try
                    {
                        Il2CppSystem.Nullable<Vector3> fd = t.ai.faceDirWhenStopped;
                        if (fd != null && fd.Pointer != IntPtr.Zero && fd.HasValue) hasFd = true;
                    }
                    catch { }
                    if (Plugin.debugLog.Value)
                        SquadCmdLogic.Log("[Facing] readback t+" + (now - t.issuedAt).ToString("0.0") + "s vehicle=" + GodViewController.SafeName(t.veh)
                            + " angle=" + ang.ToString("0.0") + " faceDir=" + (hasFd ? "set" : "null"));
                    if (t.readbacks == 1 && !hasFd && Plugin.debugLog.Value)
                        SquadCmdLogic.Log("[Facing] faceDir 读回 null——字段被原生清除/拒收；若车体未转需降级 RotateVehicleTowardEnemy 驱动");
                }

                bool done = false;
                try { done = t.ai.IsRotatedToward(t.dirPoint); } catch { }
                if (done)
                {
                    ClearField(t);
                    SquadCmdLogic.LogAlways("[Facing] done vehicle=" + GodViewController.SafeName(t.veh) + " elapsed=" + (now - t.issuedAt).ToString("0.0") + "s");
                    remove = true;
                }
                else if (now > t.deadline)
                {
                    ClearField(t);
                    SquadCmdLogic.LogAlways("[Facing] timeout vehicle=" + GodViewController.SafeName(t.veh) + " 已清字段（" + TimeoutSeconds + "s 未到位）");
                    remove = true;
                }
            }
            catch
            {
                t.fails++;
                if (t.fails >= 3) remove = true; // 瞬时 interop 异常不连坐（铁律 30）
            }
            if (remove) tasks.RemoveAt(i);
        }
    }

    private static void ClearField(FacingTask t)
    {
        try { t.ai.faceDirWhenStopped = new Il2CppSystem.Nullable<Vector3>(); } catch { }
    }

    /// <summary>拖动中的箭头绘制：每辆合格载具 → 鼠标落点。由 GodViewController.SceneMarkersFrame 在 EndFrame 前调用。</summary>
    internal static void DrawDrag(Camera cam, List<Vehicle> cands)
    {
        if (cam == null || cands == null || cands.Count == 0) return;
        Vector3 point;
        try
        {
            if (!Physics.Raycast(cam.ScreenPointToRay(Input.mousePosition), out RaycastHit hit, 3000f)) return;
            point = hit.point;
        }
        catch { return; }
        Color c = new Color(1f, 0.85f, 0.35f, ArrowAlpha); // 与移动目标点同系黄色
        int n = 0;
        foreach (Vehicle v in cands)
        {
            try
            {
                if (!IsEligible(v) || v.transform == null) continue;
                SceneMarkers.Arrow("FD" + n, v.transform.position + Vector3.up * 1.2f, point + Vector3.up * 0.4f, c, 0.22f, true);
                n++;
            }
            catch { }
        }
        SceneMarkers.Dot("FDP", point + Vector3.up * 0.1f, 0.35f, c, true);
    }
}
