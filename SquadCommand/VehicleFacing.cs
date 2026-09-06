using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 1.0.2：载具朝向控制（地狱之门式：选中载具长按右键拖动，松开转向）。
/// 1.0.3/1.0.4 实测定案：faceDirWhenStopped 通道无效（原生 IsRotatedToward 恒真、RotateVehicleTowardEnemy
/// 外部直调不转车，且 StopAndClearPath 会停掉原生转向）——改为直驱车体 yaw：
/// C# 计算车头与目标方向的有向夹角（Vector3.SignedAngle），每帧按 FaceSpeedDegPerSec 角速度
/// 绕世界 Y 轴旋转车体（AngleAxis*rotation，保留地形俯仰/侧倾），夹角 &lt;4° 判完成并清 faceDir 字段。
/// faceDirWhenStopped 仍在下达时写入（无害提示，若某状态原生会消费则方向一致），完成/超时清空防回转。
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
        public float angle0;       // 下达时夹角（日志判据）
        public int fails;          // interop 异常连续计数（铁律 30：连续 3 次才判死）
    }

    private static readonly List<FacingTask> tasks = new List<FacingTask>();

    internal const float DragThresholdPx = 14f;  // 长按 0.35s 到点时的拖动判定阈值（像素）
    private const float DirPointDist = 30f;      // 朝向兼容点距离
    private const float TimeoutSeconds = 15f;    // 15s 超时兜底
    private const float DoneAngleDeg = 4f;       // 夹角判定阈值（度）
    private const float FaceSpeedDegPerSec = 60f;// 直驱原地转向角速度

    /// <summary>车上的 AIVehicle 组件（本体找不到再找子级，与 DriveVehicleTo 同款）。</summary>
    private static AIVehicle GetAi(Vehicle v)
    {
        try { AIVehicle ai = v.GetComponent<AIVehicle>(); if (ai != null) return ai; } catch { }
        try { return v.GetComponentInChildren<AIVehicle>(); } catch { return null; }
    }

    /// <summary>车头与"车→dirPoint"水平方向的有向夹角（度），纯托管计算。</summary>
    private static float ManagedSignedAngle(Vehicle v, Vector3 dirPoint)
    {
        try
        {
            Vector3 to = dirPoint - v.transform.position; to.y = 0f;
            Vector3 fwd = v.transform.forward; fwd.y = 0f;
            if (to.sqrMagnitude < 0.01f || fwd.sqrMagnitude < 0.0001f) return 0f;
            return Vector3.SignedAngle(fwd.normalized, to.normalized, Vector3.up);
        }
        catch { return 0f; }
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
                GodViewController.CancelVehicleMoveObservation(v); // 摘除该车移动观察/待发重试，防与朝向任务互相打架
                try { ai.faceDirWhenStopped = new Il2CppSystem.Nullable<Vector3>(point); } catch { }
                float a0 = ManagedSignedAngle(v, point);
                tasks.Add(new FacingTask { veh = v, ai = ai, dirPoint = point, issuedAt = Time.unscaledTime, deadline = Time.unscaledTime + TimeoutSeconds, angle0 = a0 });
                issued++;
            }
            catch (Exception ex) { SquadCmdLogic.Log("[Facing] 下达失败 vehicle=" + GodViewController.SafeName(v) + ": " + ex.Message); }
        }
        if (issued > 0)
            SquadCmdLogic.LogAlways("[Facing] issue vehicles=" + issued + " point=" + point.ToString("0.0") + " (直驱车体 yaw)");
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

    /// <summary>持久任务段：驱动转向 + 到位/超时收尾。RTS 退出后继续生效（车在 FPS 视角下也能转完）。</summary>
    internal static void Tick()
    {
        if (tasks.Count == 0) return;
        float now = Time.unscaledTime;
        bool paused = GodViewController.Paused;
        for (int i = tasks.Count - 1; i >= 0; i--)
        {
            FacingTask t = tasks[i];
            bool remove = false;
            try
            {
                if (t.veh == null || t.veh.transform == null) { tasks.RemoveAt(i); continue; }
                float ang = ManagedSignedAngle(t.veh, t.dirPoint);
                if (Mathf.Abs(ang) <= DoneAngleDeg)
                {
                    ClearField(t);
                    SquadCmdLogic.LogAlways("[Facing] done vehicle=" + GodViewController.SafeName(t.veh)
                        + " elapsed=" + (now - t.issuedAt).ToString("0.0") + "s angle0=" + t.angle0.ToString("0"));
                    remove = true;
                }
                else if (now > t.deadline)
                {
                    ClearField(t);
                    SquadCmdLogic.LogAlways("[Facing] timeout vehicle=" + GodViewController.SafeName(t.veh)
                        + " angle=" + ang.ToString("0") + " angle0=" + t.angle0.ToString("0") + "（" + TimeoutSeconds + "s 未到位，已清字段）");
                    remove = true;
                }
                else if (!paused)
                {
                    // 直驱车体 yaw：绕世界 Y 轴旋转，保留地形俯仰/侧倾；暂停（timeScale=0）时不驱动
                    float dt = Time.deltaTime;
                    if (dt > 0f)
                    {
                        float angDelta = Mathf.Clamp(ang, -FaceSpeedDegPerSec * dt, FaceSpeedDegPerSec * dt);
                        t.veh.transform.rotation = Quaternion.AngleAxis(angDelta, Vector3.up) * t.veh.transform.rotation;
                    }
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
        Color c = new Color(1f, 1f, 1f, 0.6f); // 白色半透明（与移动路线同系）
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
