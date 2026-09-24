using System;
using System.Collections.Generic;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 1.0.2：载具朝向控制。1.2.0 起：入口改为阵型下发（Formation 线槽到位补发）与移动覆盖，
/// 独立的"朝向拖动"手势已被阵型箭头上位替代。
/// 1.0.3/1.0.4 实测定案：faceDirWhenStopped 通道无效（原生 IsRotatedToward 恒真、RotateVehicleTowardEnemy
/// 外部直调不转车，且 StopAndClearPath 会停掉原生转向）——最终机制 = 直驱车体 yaw：
/// C# Vector3.SignedAngle 算车头与目标方向夹角，每帧按该车转速（ResolveTurnSpeed，坦克慢/轮式快）
/// 绕世界 Y 轴逼近（AngleAxis*rotation，保留地形俯仰/侧倾），夹角 &lt;4° 判完成并清 faceDir 字段。
/// faceDirWhenStopped 仍在下达时写入（无害提示），完成/超时清空防回转。
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
        public float speedDeg;     // 该车原地转向角速度（度/秒，下达时解析缓存）
        public int fails;          // interop 异常连续计数（铁律 30：连续 3 次才判死）
    }

    private static readonly List<FacingTask> tasks = new List<FacingTask>();

    private const float TimeoutSeconds = 15f;    // 15s 超时兜底
    private const float DoneAngleDeg = 4f;       // 夹角判定阈值（度）
    private const float FaceSpeedFallback = 60f; // 轮式车兜底角速度（读不到任何转速源时）
    private const float TankSpeedFallback = 28f; // 坦克兜底角速度（明显慢于轮式）

    /// <summary>解析该车原地转向角速度（度/秒）：rotationSpeed → 坦克 curRotationSpeed → 按坦克/轮式兜底。
    /// 1.0.5 实测各车读数无差异（rotationSpeed 疑似未接运行时数据），分类兜底保证坦克明显慢于轮式。</summary>
    private static float ResolveTurnSpeed(Vehicle v)
    {
        try { float rs = v.rotationSpeed; if (rs > 0.5f) return Mathf.Clamp(rs, 4f, 240f); } catch { }
        try
        {
            VehicleTank tank = v.TryCast<VehicleTank>(); // 铁律 4：IL2CPP 必须 TryCast
            if (tank != null)
            {
                try { float crs = tank.curRotationSpeed; if (crs > 0.5f) return Mathf.Clamp(crs, 4f, 240f); } catch { }
                return TankSpeedFallback;
            }
        }
        catch { }
        return FaceSpeedFallback;
    }

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

    /// <summary>载具是否可转向：非飞机、玩家未接管，且（有 AIVehicle + 驾驶员存活）或（1.2.3：无 AIVehicle 的
    /// 固定火力点/火炮——只要车上有存活乘员即可原地转向，因为转向只直驱 transform yaw，不依赖原生驾驶链）。
    /// 1.2.7：炮位也带 AIVehicle 但没有驾驶员概念 → 判定改为"**可移动载具才要求驾驶员**，
    /// 火力点/火炮只要有人操作就能转"（否则用户报的"火炮还是不能操控转向"）。</summary>
    internal static bool IsEligible(Vehicle v)
    {
        try
        {
            if (v == null || v.transform == null) return false;
            try { if (v.IsAirVehicle()) return false; } catch { return false; }
            bool mobile = Formation.IsMobileVehicle(v); // 履带/轮式/飞机 = 可移动
            if (mobile)
            {
                if (GetAi(v) == null) return false;
                try { if (!v.HasDriverAlive) return false; } catch { }
            }
            else
            {
                // 火力点/火炮/拖车：有人操作即可原地转向
                if (!HasAliveCrew(v)) return false;
            }
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

    /// <summary>1.2.3：车上是否有存活乘员（无 AIVehicle 的火力点/火炮的可用性判据）。</summary>
    private static bool HasAliveCrew(Vehicle v)
    {
        try
        {
            Soldier[] crew = v.GetComponentsInChildren<Soldier>();
            if (crew != null)
                for (int i = 0; i < crew.Length; i++)
                    if (crew[i] != null && crew[i].IsAlive) return true;
        }
        catch { }
        return false;
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
        float logSpd = 0f;
        bool logTank = false;
        if (cands == null || cands.Count == 0) return 0;
        foreach (Vehicle v in new List<Vehicle>(cands))
        {
            try
            {
                if (!IsEligible(v)) continue;
                AIVehicle ai = GetAi(v);
                long p = (long)v.Pointer;
                tasks.RemoveAll(t => { try { return t.veh == null || (long)t.veh.Pointer == p; } catch { return true; } });
                try { if (ai != null) ai.StopAndClearPath(); } catch { } // 1.2.3：火力点无 AIVehicle
                GodViewController.CancelVehicleMoveObservation(v); // 摘除该车移动观察/待发重试，防与朝向任务互相打架
                try { if (ai != null) ai.faceDirWhenStopped = new Il2CppSystem.Nullable<Vector3>(point); } catch { }
                float spd = ResolveTurnSpeed(v);
                if (issued == 0)
                {
                    logSpd = spd;
                    try { logTank = v.TryCast<VehicleTank>() != null; } catch { }
                }
                float a0 = ManagedSignedAngle(v, point);
                tasks.Add(new FacingTask { veh = v, ai = ai, dirPoint = point, issuedAt = Time.unscaledTime, deadline = Time.unscaledTime + TimeoutSeconds, angle0 = a0, speedDeg = spd });
                issued++;
            }
            catch (Exception ex) { SquadCmdLogic.Log("[Facing] 下达失败 vehicle=" + GodViewController.SafeName(v) + ": " + ex.Message); }
        }
        if (issued > 0)
            SquadCmdLogic.LogAlways("[Facing] 载具转向 vehicles=" + issued + " spd=" + logSpd.ToString("0") + " tank=" + (logTank ? "Y" : "N"));
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
                    // 直驱车体 yaw：绕世界 Y 轴旋转，保留地形俯仰/侧倾；角速度按下达时解析的
                    // 该车转速（speedDeg），暂停（timeScale=0）时不驱动
                    float dt = Time.deltaTime;
                    if (dt > 0f)
                    {
                        float spd = t.speedDeg > 0.5f ? t.speedDeg : FaceSpeedFallback;
                        float angDelta = Mathf.Clamp(ang, -spd * dt, spd * dt);
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
        try { if (t.ai != null) t.ai.faceDirWhenStopped = new Il2CppSystem.Nullable<Vector3>(); } catch { }
    }
}
