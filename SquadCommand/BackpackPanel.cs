using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime;
using ER2Shared;
using UnityEngine;

namespace ER2SquadCommand;

/// <summary>
/// 1.3.0：格子背包系统（MC 风格）——多窗口 + 鼠标拖拽交换。
/// 1.3.1（用户实测修订）：①同类物品合并成一格（×N=总数，按 武器/弹药/爆炸物/医疗/装备/其他 分类排序，拖动=整组移动/交换）；
///   ②穿戴/手持物品可拿起但只能丢到地上（士兵走 DropItemNow 原生卸下路径），放回格子提示拒绝；
///   ③图标取图链加兜底（模板 icon → ItemsDatabase 缓存按名 → er2gui bundle 按名 → LoadIconAsync），失败无条件记诊断日志；
///   ④联动半径默认 3m。
/// - 容器统一走 InventoryManager（士兵背包 / 尸体背包 / 载具货舱），窗口可同时开多个。
/// - 锚点规则：第一个打开的背包为锚点，其余背包距锚点超过 packRange 拒绝打开；打开后每 0.5s 复检，
///   超出联动半径自动关闭；锚点窗口自身不受范围规则约束；锚点关闭后由最旧的余窗接任。
/// - 搬移通道：不走 TakeIntoInventory/AddVirtualItem（陷阱 16：归一化成基类会废掉弹匣/弹药子类），
///   直接 items.RemoveAt/Add（ThrowableWheel 已验证的注入法）；负重用 GetWeightAndMaxWeight 自算。
/// - 手势隔离：WantsMouse() 挂进 GodViewController.IsMouseOverGui；窗口内事件一律 Event.current + e.Use()；
///   拖拽中吞掉全部手势（防误框选）。
/// - 清理单入口：CloseAll() 幂等（Exit/TakeControlSelected/Enter 调用）；拖拽中间态所有结束路径收口到 ResetDrag。
/// </summary>
internal static class BackpackPanel
{
    // ── 布局常量 ──
    // 2.5.1：**下面这些原是 const → 自适应钉死（"UI 是死的"）。改为随 Er2Ui.Scale 的属性。**
    // 只有"格子数"是纯逻辑量，不参与缩放，保持 const。
    private const int Cols = 6, Rows = 4, PerPage = Cols * Rows;   // 24 格/页（1.2.4 布局惯例）
    private const float CellBase = 46f, GapBase = 3f, MarginBase = 8f, TitleHBase = 26f;
    private static float Cell => CellBase * Er2Ui.Scale;
    private static float Gap => GapBase * Er2Ui.Scale;
    private static float Margin => MarginBase * Er2Ui.Scale;
    private static float TitleH => TitleHBase * Er2Ui.Scale;
    private static float WinW => Margin * 2f + Cols * Cell + (Cols - 1) * Gap;
    private static float WinH => TitleH + 4f * Er2Ui.Scale + Rows * Cell + (Rows - 1) * Gap + Margin;
    private const int MaxWindows = 8;

    /// <summary>2.5.1：建样式/排窗口时记下的 Scale，本帧比一下就知道要不要重来。</summary>
    private static float layoutScale = 1f;

    private sealed class PackWindow
    {
        public InventoryManager invMgr;
        public string title = "";
        public Rect rect;
        public int slot;            // 1.4.3：平铺槽位（开新窗取最低空闲槽，不再互相叠）
        public float openedAt;      // 1.4.3：开窗时间（30s 内豁免范围自动关闭——会合流程 units 还会动）
        public int page;
        public float nextRefresh = -10f;
        public bool titleDrag;
        // 1.4.9：标题按可用宽度适配后的结果（GUI.Label 不裁剪，长名字会直接画到负重数字上）
        public string titleDisplay = "";
        public int titleSize = 12;
        public string titleFitKey = "";
        public float titleFitW = -1f;
        public readonly List<PackCell> cells = new List<PackCell>();
    }

    /// <summary>一格 = 同 item_id 的非穿戴物品组（1.3.1 合并显示）；穿戴项单独成格（锁定）。</summary>
    private sealed class PackCell
    {
        public string id = "";
        public string name = "";
        public int total;      // 总数：弹匣=Σ弹药数；堆叠=ΣstackCount；普通=件数
        public int units;      // VirtualItem 件数
        public float mass;     // 组总重
        public bool worn;      // 穿戴/手持单件 → 锁定，只能丢地上
        public bool isMag;
        public VirtualItem first; // 任一成员（取图标/名称用）
        public int cat;        // 分类序（0武器…5其他，排序用）
    }

    private static readonly List<PackWindow> windows = new List<PackWindow>();
    private static readonly Dictionary<string, Sprite> iconCache = new Dictionary<string, Sprite>();
    // 1.3.4（终案）：**绘制只用自建 Texture2D**。实测因果链（1.3.2/1.3.3 日志）：游戏图标 Sprite/贴图是
    // 256×256 图集子区域 → 画的时候 "Object was garbage collected in IL2CPP domain"（1.3.3 的 IL2CPP 侧
    // keep-alive List 也被 GC，此路不通）→ 解析成功后立刻**光栅化**（GPU blit + ReadPixels，只碰外国对象
    // 一次），之后 GUI.DrawTexture 画自建贴图——与本 mod 每帧在用的 whiteTexture 同一条已验证路径。
    private static readonly Dictionary<string, Texture2D> iconTexCache = new Dictionary<string, Texture2D>();
    private static readonly List<Texture2D> iconTexKeepAlive = new List<Texture2D>();
    private static readonly HashSet<string> iconPending = new HashSet<string>();
    private static readonly HashSet<string> iconLogged = new HashSet<string>();   // 失败诊断只记一次/id
    private static readonly HashSet<string> iconDrawLogged = new HashSet<string>(); // 绘制诊断只记一次/id
    private static readonly Dictionary<string, float> iconNextTry = new Dictionary<string, float>(); // 失败后 2s 内不重试同步链
    private static float nextValidate = -10f;

    // 拖拽中间态（唯一收口：ResetDrag）——1.3.1 起拖动单位 = 整组（按 id）
    private static PackWindow dragWin;
    private static VirtualItem dragItem;   // 组内任一件（穿戴单件的丢弃定位用）
    private static string dragId = "";
    private static string dragName = "";
    private static int dragTotal;
    private static int dragUnits;
    private static bool dragIsMag;
    private static bool dragWorn;
    private static bool dragActive;

    private static GUIStyle textStyle, badgeStyle, tipStyle;

    // ── 原生交互菜单（1.4.0：右键格子 → vi.GetInventoryInteractions 的游戏自带交互列表：穿/吃/卸下…
    //     点条目 = Interaction.Call() 原生执行。能走原生管线就走原生（陷阱 17）
    //     1.4.1：菜单**模态**——点击在进格子前先裁决（实测穿透：菜单底下的格子先吃掉点击=菜单永远点不到
    //     +误触拖拽）；并支持地面物品菜单（弹药箱"补充弹药"等多交互物品）──
    private static PackWindow menuWin;
    private static ItemObject menuGroundItem; // 地面物品菜单（无宿主窗口）
    private static VirtualItem menuItem;
    private static string menuItemId = "";
    private static Rect menuRect;
    private static readonly List<Action> menuExec = new List<Action>();   // 统一执行列表：原生=Interaction.Call；合成=Lua 穿戴
    private static readonly List<string> menuLabels = new List<string>();
    private static readonly List<string> menuRaw = new List<string>();    // 1.4.17：与 menuLabels 平行的**原文**（未翻译），供「拾起」前缀判定；合成条目压入同长占位保持对齐
    private static bool MenuOpen => menuExec.Count > 0;

    // ── 宿主接线 ──

    /// <summary>1.4.1：有打开的背包窗口（宿主用它保护"空地不清选"工作流）。</summary>
    internal static bool HasOpenWindows => windows.Count > 0;

    /// <summary>Update 阶段命中判定：鼠标在任一窗口上、或拖拽中 → 吞战场手势。</summary>
    internal static bool WantsMouse()
    {
        if (!GodViewController.Active) return false;
        try
        {
            if (dragActive) return true;
            Vector2 m = new Vector2(Input.mousePosition.x, Screen.height - Input.mousePosition.y);
            if (MenuOpen && menuRect.Contains(m)) return true; // 1.4.0：交互菜单（含地面物品菜单）
            for (int i = 0; i < windows.Count; i++)
                if (windows[i].rect.Contains(m)) return true;
        }
        catch { }
        return false;
    }

    internal static void Draw()
    {
        if (!GodViewController.Active || GodViewController.EscMenuOpen) return;
        // 2.5.1：自适应入口（背包是独立 OnGUI 入口，不能只靠 DrawHud 里那次）。
        Er2Ui.AutoScale();
        // 倍率变了 → 已开窗口的尺寸得跟着变，否则格子画到窗口外面去。
        // 保留玩家拖过的位置（只改宽高 + clamp 回屏幕内），不强行归位槽位。
        if (Er2Ui.ScaleChangedSince(layoutScale))
        {
            layoutScale = Er2Ui.Scale;
            for (int i = 0; i < windows.Count; i++)
            {
                PackWindow w = windows[i];
                float x = Mathf.Clamp(w.rect.x, 4f, Mathf.Max(4f, Screen.width - WinW - 4f));
                float y = Mathf.Clamp(w.rect.y, 4f, Mathf.Max(4f, Screen.height - WinH - 4f));
                w.rect = new Rect(x, y, WinW, WinH);
                w.titleFitW = -1f;   // 标题适配结果作废（宽度变了）
            }
        }
        LootTick(); // 1.4.9：**双路驱动**——Tick 侧调用实测存在静默失效（到达检测全程无日志），OnGUI 渲染循环
                    // 是本 mod 全程验证存活的主循环（图标在画）；LootTick 自带 0.25s 节流，双调幂等
        Validate();
        EnsureStyles();
        Event e = Event.current;

        // 1.4.1：交互菜单**模态裁决**——必须在格子之前处理，否则菜单底下的格子先吃掉点击
        //（实测：菜单项永远点不到 + 误触拾取拖拽 = "操作丢失"）。
        if (MenuOpen && e != null && e.type == EventType.MouseDown)
        {
            if (menuRect.Contains(e.mousePosition))
            {
                int idx = MenuIndexAt(e.mousePosition);
                e.Use();
                if (idx >= 0) ExecuteInteraction(idx);
                CloseMenu();
            }
            else
            {
                CloseMenu(); // 点菜单外：只关菜单，吞掉本次点击防穿透
                e.Use();
            }
            MarkConsumed(e); // 1.4.11：菜单吃掉了点击（菜单随即关闭）→ 防松手被当成空地点击取消选中
            return;
        }

        bool dropHandled = false;
        for (int i = 0; i < windows.Count; i++) DrawWindow(windows[i], e, ref dropHandled);
        // 格子没接住的 MouseUp：窗口内空白=取消；窗口外=扔地上（MC 惯例）
        if (dragActive && !dropHandled && e != null && e.type == EventType.MouseUp && e.button == 0)
        {
            Vector2 m = e.mousePosition;
            bool inAny = false;
            for (int i = 0; i < windows.Count; i++) if (windows[i].rect.Contains(m)) { inAny = true; break; }
            e.Use();
            if (inAny) ResetDrag("落在窗口空白处");
            else if (dragWorn) DropWornToGround();
            else DropToGround();
        }
        if (dragActive && e != null && e.type == EventType.Repaint) DrawDragGhost(e.mousePosition);
        DrawMenu(e); // 纯绘制（点击已在上面模态裁决）
        MarkConsumed(e);
    }

    /// <summary>1.4.11：本帧有 UI 事件被背包吃掉（`e.Use()` 会把事件置为 Used）→ 通知宿主吞掉这一按的剩余部分。
    /// 不加这一步：「点 ✕ 关窗」的**松手**落在窗口已消失的下一帧 → `guiNow` 为 false → 被当成空地点击
    /// → 取消选中单位（用户实测「关闭背包后也会取消选中单位」）。交互菜单项点击同理。</summary>
    private static void MarkConsumed(Event e)
    {
        if (e == null || e.type != EventType.Used) return;
        try { GodViewController.SwallowLeftGesture(); } catch { }
    }

    // ── 原生交互菜单 ──

    private static void OpenMenu(PackWindow w, PackCell c, Vector2 mousePos)
    {
        CloseMenu();
        FillMenuEntries(() => c.first.GetInventoryInteractions(w.invMgr, w.invMgr)); // interactor=source=所属背包
        AppendEquipActions(w, c); // 1.4.2：原生列表对非玩家 interactor 不给"穿上"→ 补合成官方 Lua 穿戴
        if (menuExec.Count == 0)
        {
            GodViewController.Flash(Ui.Tr("该物品没有可用交互"), 1.5f);
            return;
        }
        menuWin = w; menuItem = c.first; menuItemId = c.id;
        LayoutMenu(mousePos);
        SquadCmdLogic.Log("[Backpack] 交互菜单 " + c.id + "（" + menuExec.Count + " 项）");
    }

    /// <summary>1.4.3：合成"穿上"条目。原生 GetInventoryInteractions 对非玩家 interactor 不生成装备动作 →
    /// 走 Lua_Soldier 官方通道：武器=LoadAndSetWeapon(id,0)（官方协程）；盔=wearHeadgear、衣/甲=wearUniform/wearVest
    /// （**按物品子类判定**——1.4.2 的关键字匹配漏掉了 us_marine_gear_4a 这类不含 vest 字样的胸挂）；
    /// 头盔戴后调 RefreshHeadgearVisibility 刷新头部模型（否则数据穿了、头上没有）。</summary>
    private static void AppendEquipActions(PackWindow w, PackCell c)
    {
        if (c.worn) return; // 穿戴中的只有脱下/丢弃（原生已给）
        // 1.4.12 诊断：把原生菜单原文打出来——判断 "穿上/Wear" 到底来自原生列表还是我们合成的
        //（原生 Interaction.Call() 无参 = interactor 已绑定，若原生真有穿戴项，它才是"点了没用"的元凶）。
        try { SquadCmdLogic.Log("[Backpack] 原生菜单项 " + c.id + "：" + string.Join(" | ", menuLabels.ToArray())); } catch { }
        // 1.4.12（用户实测「只能脱不能穿」）：原生 wear/equip 条目对非玩家 interactor **静默无效** →
        // 剪掉原生条目，统一走我们合成的官方 Lua 通道（带直写兜底），保证菜单里只有一个"穿上"且真的有效。
        try
        {
            int pruned = 0;
            for (int i = menuLabels.Count - 1; i >= 0; i--)
            {
                string l = menuLabels[i] ?? "";
                string ll = l.ToLowerInvariant();
                bool isWear = l == Ui.Tr("穿上")
                    || ((ll.Contains("wear") && !ll.Contains("unwear")) || ll.Contains("equip"));
                if (!isWear) continue;
                menuExec.RemoveAt(i);
                menuLabels.RemoveAt(i);
                menuRaw.RemoveAt(i);
                pruned++;
            }
            if (pruned > 0) SquadCmdLogic.Log("[Backpack] 剪掉原生穿戴条目 " + pruned + " 个（对非玩家静默无效），改用合成通道");
        }
        catch { }
        Soldier owner = null;
        try { owner = w.invMgr.GetComponentInParent<Soldier>(); } catch { }
        if (owner == null) return;
        try
        {
            string id = c.id;
            var helmet = c.first.TryCast<VirtualHelmet>();
            var clothing = helmet == null ? c.first.TryCast<VirtualClothing>() : null;
            var weapon = (helmet == null && clothing == null) ? c.first.TryCast<VirtualWeapon>() : null;
            if (weapon != null)
            {
                menuLabels.Add(Ui.Tr("穿上"));
                menuRaw.Add("穿上"); // 1.4.17：合成条目压占位原文，保持三列表等长（剪枝/索引安全）
                menuExec.Add(() =>
                {
                    string bw = WearSnapshot(owner);
                    string werr = "";
                    try { FrameEndRunner.RunNativeCoroutine(new Lua_Soldier(owner).LoadAndSetWeapon(id, 0)); }
                    catch (Exception ex) { werr = ex.Message; }
                    SquadCmdLogic.Log("[Backpack] 穿戴 武器 id=" + id + " 前[" + bw + "] 后[" + WearSnapshot(owner) + "]"
                        + (werr.Length > 0 ? " 异常=" + werr : ""));
                });
                SquadCmdLogic.Log("[Backpack] 合成穿戴项（武器）" + id);
            }
            else if (helmet != null)
            {
                menuLabels.Add(Ui.Tr("穿上"));
                menuRaw.Add("穿上");
                menuExec.Add(() => WearItem(w, owner, id, 0));
                SquadCmdLogic.Log("[Backpack] 合成穿戴项（盔）" + id);
            }
            else if (clothing != null)
            {
                bool uniform = id.ToLowerInvariant().Contains("uniform");
                menuLabels.Add(Ui.Tr("穿上"));
                menuRaw.Add("穿上");
                menuExec.Add(() => WearItem(w, owner, id, uniform ? 1 : 2));
                SquadCmdLogic.Log("[Backpack] 合成穿戴项（" + (uniform ? "衣" : "甲/挂") + "）" + id);
            }
        }
        catch (Exception ex) { SquadCmdLogic.Log("[Backpack] 合成穿戴项失败 " + c.id + ": " + ex.Message); }
    }

    /// <summary>1.4.12：穿戴状态快照（盔/衣/甲 id；"-"=没穿，"?"=取不到）。诊断用。</summary>
    private static string WearSnapshot(Soldier s)
    {
        string h = "-", u = "-", v = "-";
        try { if (s.hasHeadgear) { VirtualHelmet x = s.headgear_ref; h = (x != null && x.item_id != null) ? x.item_id : "?"; } } catch { h = "?"; }
        try { if (s.hasUniform) { VirtualClothing x = s.uniform_ref; u = (x != null && x.item_id != null) ? x.item_id : "?"; } } catch { u = "?"; }
        try { if (s.hasVest) { VirtualClothing x = s.vest_ref; v = (x != null && x.item_id != null) ? x.item_id : "?"; } } catch { v = "?"; }
        return "盔=" + h + " 衣=" + u + " 甲=" + v;
    }

    /// <summary>1.4.12：背包里第一个该 id 的虚拟物品（不过滤穿戴态）。</summary>
    private static VirtualItem FirstVirtualItem(InventoryManager im, string id)
    {
        try
        {
            var items = ItemsOf(im);
            if (items == null || string.IsNullOrEmpty(id)) return null;
            for (int i = 0; i < items.Count; i++)
            {
                VirtualItem vi = items[i];
                if (vi == null) continue;
                string vid = null; try { vid = vi.item_id; } catch { }
                if (string.Equals(vid, id, StringComparison.Ordinal)) return vi;
            }
        }
        catch { }
        return null;
    }

    /// <summary>1.4.13：穿一件装备（0=盔 1=衣 2=甲）。
    /// **1.4.13 关键发现（反编译 Soldier 确认的原生入口，此前全都没用）**：
    /// `Soldier.SetWerable(VirtualItem, bool)` / `SetWerableCR(...)`（原生"穿上虚拟衣物"，协程版负责异步加载 prefab）、
    /// `Soldier.PickUpItemFromInventory(VirtualItem, InventoryManager, int)`（从背包拿起并穿戴）。
    /// 1.4.12 走的 `Lua_Soldier.wearXxx` 与直写字段**实测都不改变穿戴状态**（日志：前[盔=-] 后[盔=-]），
    /// 所以这里改成**阶梯式尝试 + 每步验证（快照 + `IsWearing`），第一个见效的就停**，日志记下是哪一级生效的。</summary>
    private static void WearItem(PackWindow w, Soldier owner, string id, int kind)
    {
        string before = WearSnapshot(owner);
        VirtualItem vi = FirstVirtualItem(w.invMgr, id);
        bool hadInst = false; try { hadInst = vi != null && vi.IsInstance(); } catch { }
        bool wasWearing = false; try { wasWearing = vi != null && owner.IsWearing(vi); } catch { }
        string err = "";
        string step = "无";

        // ① 原生 SetWerable：语义最贴（"穿上虚拟衣物"），协程在 Soldier 自己身上跑
        if (!WearChanged(owner, before, vi, wasWearing) && vi != null)
        {
            step = "SetWerable";
            try { owner.SetWerable(vi); } catch (Exception ex) { err += " ①" + ex.Message; }
        }
        // ② 老的 Lua 官方脚本通道
        if (!WearChanged(owner, before, vi, wasWearing))
        {
            step = "Lua";
            try
            {
                Lua_Soldier ls = new Lua_Soldier(owner);
                if (kind == 0) ls.wearHeadgear(id);
                else if (kind == 1) ls.wearUniform(id);
                else ls.wearVest(id);
            }
            catch (Exception ex) { err += " ②" + ex.Message; }
            try { owner.TriggerClothingObjRefresh(kind == 0, kind == 2, kind == 1); } catch { }
            if (kind == 0) { try { owner.RefreshHeadgearVisibility(); } catch { } }
        }
        // ③ 从背包"拿起并穿戴"（原生 PickUpItemFromInventory，wearedItemIndex 默认 0）
        if (!WearChanged(owner, before, vi, wasWearing) && vi != null)
        {
            step = "PickUpItemFromInventory";
            try { owner.PickUpItemFromInventory(vi, w.invMgr, 0); } catch (Exception ex) { err += " ③" + ex.Message; }
        }
        // ④ 直写原生装备字段（需要已有活体实例；没有就跳过——拿 prefab 当实例是错的）
        if (!WearChanged(owner, before, vi, wasWearing))
        {
            ItemObject inst = null; try { inst = vi != null && vi.IsInstance() ? vi.GetInstance() : null; } catch { }
            if (inst != null)
            {
                step = "直写字段";
                try
                {
                    if (kind == 0)
                    {
                        ItemHelmet ih = inst.TryCast<ItemHelmet>();
                        VirtualHelmet vh = null; try { vh = vi.TryCast<VirtualHelmet>(); } catch { }
                        if (ih != null)
                        {
                            try { if (vh != null) owner.headgear_ref = vh; } catch { }
                            try { owner.SetHelmetObject(ih); } catch { }
                        }
                    }
                    else
                    {
                        ItemClothing ic = inst.TryCast<ItemClothing>();
                        VirtualClothing vc = null; try { vc = vi.TryCast<VirtualClothing>(); } catch { }
                        if (ic != null)
                        {
                            if (kind == 1)
                            {
                                try { if (vc != null) owner.uniform_ref = vc; } catch { }
                                try { owner.uniform_Obj = ic; } catch { }
                            }
                            else
                            {
                                try { if (vc != null) owner.vest_ref = vc; } catch { }
                                try { owner.vest_Obj = ic; } catch { }
                            }
                        }
                    }
                    try { owner.TriggerClothingObjRefresh(kind == 0, kind == 2, kind == 1); } catch { }
                    if (kind == 0) { try { owner.RefreshHeadgearVisibility(); } catch { } }
                }
                catch (Exception ex) { err += " ④" + ex.Message; }
            }
        }
        // ⑤ 协程版（异步加载 prefab；启动后本帧看不到结果，交给下一帧）
        bool settled = WearChanged(owner, before, vi, wasWearing);
        if (!settled && vi != null)
        {
            step += "+SetWerableCR(异步)";
            try { FrameEndRunner.RunNativeCoroutine(owner.SetWerableCR(vi)); } catch (Exception ex) { err += " ⑤" + ex.Message; }
        }

        bool wearedMark = false;
        try { VirtualItem after = FirstVirtualItem(w.invMgr, id); wearedMark = after != null && after.IsWearedItem(); } catch { }
        string afterSnap = WearSnapshot(owner);
        SquadCmdLogic.Log("[Backpack] 穿戴 id=" + id + " 类型=" + (kind == 0 ? "盔" : kind == 1 ? "衣" : "甲")
            + " 实例=" + hadInst + " 生效级=" + step + " 首步即变=" + (settled ? "是" : "否")
            + " 前[" + before + "] 后[" + afterSnap + "] 穿戴标记=" + wearedMark
            + (err.Length > 0 ? " 异常=" + err : ""));
        if (w != null) RefreshNow(w);
    }

    /// <summary>穿戴是否已生效：状态快照变了，或原生 `IsWearing(该虚拟机物品)` 翻转。</summary>
    private static bool WearChanged(Soldier s, string before, VirtualItem vi, bool wasWearing)
    {
        try { if (WearSnapshot(s) != before) return true; } catch { }
        try { if (vi != null && s.IsWearing(vi) != wasWearing) return true; } catch { }
        return false;
    }

    /// <summary>1.4.1：地面物品交互菜单（弹药箱"补充弹药"等多交互物品；单交互的仍走快速拾取）。</summary>
    internal static void OpenGroundMenu(ItemObject obj, InventoryManager interactor, Vector2 mousePos)
    {
        CloseMenu();
        string oid = "";
        try { oid = obj.item_id; } catch { }
        FillMenuEntries(() => obj.GetInteractions(interactor));
        if (menuExec.Count == 0)
        {
            GodViewController.Flash(Ui.Tr("该物品没有可用交互"), 1.5f);
            return;
        }
        menuGroundItem = obj;
        menuItemId = oid;
        LayoutMenu(mousePos);
        SquadCmdLogic.Log("[Backpack] 地面物品交互菜单 " + oid + "（" + menuExec.Count + " 项）");
    }

    /// <summary>填充交互条目（格子物品/地面物品共用）。</summary>
    private static void FillMenuEntries(Func<Il2CppSystem.Collections.Generic.List<Interaction>> fetch)
    {
        try
        {
            var list = fetch();
            if (list == null) return;
            for (int i = 0; i < list.Count; i++)
            {
                Interaction it = list[i];
                if (it == null) continue;
                string txt = null;
                try { txt = it.GetInteractionText(); } catch { }
                if (string.IsNullOrEmpty(txt)) { try { txt = it.text; } catch { } }
                if (string.IsNullOrEmpty(txt)) txt = "?";
                string raw = txt;         // 1.4.17：留原文——「拾起」前缀判定必须在翻译前做
                txt = TrInteraction(txt); // 1.4.3：原生交互文案是中文源串，走词典英文化
                Interaction it2 = it;
                menuExec.Add(() => it2.Call());
                menuLabels.Add(txt);
                menuRaw.Add(raw);
            }
        }
        catch (Exception ex)
        {
            SquadCmdLogic.LogAlways("[Backpack] 获取交互列表失败: " + ex.Message);
        }
    }

    // 2.5.1：菜单尺寸随倍率。**LayoutMenu 与 MenuIndexAt 必须共用同一份**——
    // 行高写两处的话，倍率一变"命中判定"和"画出来的行"就错位（陷阱 78 同款隐患）。
    private const float MenuWBase = 168f, MenuRowBase = 20f;
    private static float MenuW => MenuWBase * Er2Ui.Scale;
    private static float MenuRow => MenuRowBase * Er2Ui.Scale;

    private static void LayoutMenu(Vector2 mousePos)
    {
        float k = Er2Ui.Scale;
        float mw = MenuW, mh = menuExec.Count * MenuRow + 8f * k;
        float mx = Mathf.Clamp(mousePos.x + 6f * k, 2f, Mathf.Max(2f, Screen.width - mw - 2f));
        float my = Mathf.Clamp(mousePos.y + 6f * k, 2f, Mathf.Max(2f, Screen.height - mh - 2f));
        menuRect = new Rect(mx, my, mw, mh);
    }

    private static int MenuIndexAt(Vector2 p)
    {
        if (!menuRect.Contains(p)) return -1;
        int i = Mathf.FloorToInt((p.y - menuRect.y - 4f * Er2Ui.Scale) / MenuRow);
        if (i < 0 || i >= menuExec.Count) return -1;
        return i;
    }

    private static void DrawMenu(Event e)
    {
        if (!MenuOpen) return;
        if (menuWin != null && !windows.Contains(menuWin)) { CloseMenu(); return; }
        Rect r = menuRect;
        GUI.color = new Color(0f, 0f, 0f, 0.94f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        float k = Er2Ui.Scale;   // 2.5.1：菜单随倍率
        GUI.color = new Color(GodViewController.UiHover.r, GodViewController.UiHover.g, GodViewController.UiHover.b, 0.6f);
        float me = 1f * k;
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, me), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - me, r.width, me), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, me, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - me, r.y, me, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        for (int i = 0; i < menuExec.Count; i++)
        {
            Rect ir = new Rect(r.x + 2f * k, r.y + 4f * k + i * MenuRow, r.width - 4f * k, MenuRow);
            bool hover = ir.Contains(e.mousePosition);
            if (hover)
            {
                GUI.color = new Color(GodViewController.UiHover.r, GodViewController.UiHover.g, GodViewController.UiHover.b, 0.45f);
                GUI.DrawTexture(ir, Texture2D.whiteTexture);
                GUI.color = Color.white;
            }
            Color keep = GUI.color;
            GUI.color = hover ? Color.white : new Color(1f, 1f, 1f, 0.85f);
            GUI.Label(new Rect(ir.x + 8f * k, ir.y, ir.width - 10f * k, ir.height), menuLabels[i], textStyle);
            GUI.color = keep;
        }
        // 点击不在条目循环里处理——菜单是模态的，Draw() 早在进格子前就裁决过（1.4.1）
    }

    private static void ExecuteInteraction(int idx)
    {
        string txt = idx >= 0 && idx < menuLabels.Count ? menuLabels[idx] : "?";
        PackWindow w = menuWin;

        // 1.4.17：地面物品菜单里的「拾起」类条目**不再原样 Call()**。
        // 原生拾起交互没有距离校验，而 `HandheldItem`（枪械）天生多交互（覆写 GetInteractions，
        // 如「拾起置于右手」）→ 枪械永远走菜单路径，结果就是"隔着半张地图把枪吸进背包"
        //（用户实测）。普通物品单交互走 RequestItemPickup（联动半径内即时、超出走过去捡），
        // 菜单拾起改为汇入**同一条链路**，行为完全一致。
        // 其余交互（弹药箱"补充弹药"等）保持原生执行，不受影响。
        if (menuGroundItem != null && idx >= 0 && idx < menuRaw.Count)
        {
            string raw = menuRaw[idx] ?? "";
            if (raw.StartsWith("拾起", StringComparison.Ordinal))
            {
                ItemObject gi = menuGroundItem;
                Vector3 gpos = Vector3.zero;
                bool hasPos = false;
                try { if (gi != null && gi.transform != null) { gpos = gi.transform.position; hasPos = true; } } catch { }
                if (!hasPos) { GodViewController.Flash(Ui.Tr("物品已失效"), 1.5f); return; }
                SquadCmdLogic.LogAlways("[Backpack] 地面菜单「" + raw + "」→ 改走过去拾取链路（不再隔空 Call）");
                RequestItemPickup(gi, gpos); // 联动半径内即时 / 超出派最近士兵走过去，与单交互物品同路
                return;
            }
        }

        try
        {
            if (idx < 0 || idx >= menuExec.Count) return;
            menuExec[idx]();
            GodViewController.Flash(txt, 1.5f);
            SquadCmdLogic.LogAlways("[Backpack] 交互执行 '" + txt + "' id=" + menuItemId + "（" + (w != null ? w.title : Ui.Tr("地上")) + "）");
            if (w != null) RefreshNow(w);
        }
        catch (Exception ex)
        {
            SquadCmdLogic.LogAlways("[Backpack] 交互失败 '" + txt + "': " + ex.Message);
            GodViewController.Flash(Ui.Tr("交互失败"), 1.5f);
        }
    }

    /// <summary>1.4.3：交互文案英文化——GetInteractionText 返回的是游戏中文源串（不经本地化）。
    /// 常见动作走 Ui 词典；前缀类（拆下X/安装X/拾起X/装填X）做规则翻译，剩余保留原文。</summary>
    private static string TrInteraction(string s)
    {
        string t = Ui.Tr(s);
        if (!ReferenceEquals(t, s)) return t;
        if (t.StartsWith("拆下")) return "Detach " + t.Substring(2);
        if (t.StartsWith("安装")) return "Install " + t.Substring(2);
        if (t.StartsWith("拾起")) return "Pick up " + t.Substring(2);
        if (t.StartsWith("装填")) return "Load " + t.Substring(2);
        return t;
    }

    internal static void CloseMenu()
    {
        menuWin = null;
        menuGroundItem = null;
        menuItem = null;
        menuItemId = "";
        menuRect = default;
        menuExec.Clear();
        menuLabels.Clear();
        menuRaw.Clear();
    }

    // ── 打开 / 关闭 ──

    /// <summary>热键入口：焦点单位（步兵/载具）。1.3.2 起尸体不再可选（右键尸体开背包）。</summary>
    internal static void ToggleFocused()
    {
        object f = InfoPanel.CurrentFocusUnit();
        if (f is Soldier s) { ToggleForSoldier(s); return; }
        if (f is Vehicle v) { ToggleForVehicle(v); return; }
        GodViewController.Flash(Ui.Tr("先框选/选中单位"), 1.5f);
    }

    internal static void ToggleForSoldier(Soldier s)
    {
        if (s == null) return;
        ToggleFor(ResolveSoldierInv(s), SoldierTitle(s));
    }

    internal static void ToggleForVehicle(Vehicle v)
    {
        if (v == null) return;
        InventoryManager im = null;
        try { im = v.inventory; } catch { }
        if (im == null) { try { im = v.GetInventory(); } catch { } }
        string nm = ""; try { nm = GodViewController.SafeName(v); } catch { }
        ToggleFor(im, Ui.Tr("货舱 · ") + (string.IsNullOrEmpty(nm) ? "?" : nm));
    }

    internal static string SoldierTitle(Soldier s)
    {
        bool alive = false; try { alive = s.IsAlive; } catch { }
        string nm = ""; try { nm = GodViewController.SafeName(s); } catch { }
        return Ui.Tr("背包 · ") + (string.IsNullOrEmpty(nm) ? "?" : nm) + (alive ? "" : Ui.Tr("（阵亡）"));
    }

    private static InventoryManager ResolveSoldierInv(Soldier s)
    {
        try { InventoryManager m = InfoPanel.FindInventoryManager(s); if (m != null) return m; } catch { }
        try { return s.inventory; } catch { } // Creature 字段兜底（尸体 / activeInventories 漏网）
        return null;
    }

    private static void ToggleFor(InventoryManager im, string title)
    {
        if (im == null)
        {
            GodViewController.Flash(Ui.Tr("找不到该单位的背包数据"), 2f);
            SquadCmdLogic.LogAlways("[Backpack] 打开失败：无 InventoryManager（" + title + "）");
            return;
        }
        long ptr = 0; try { ptr = (long)im.Pointer; } catch { }
        for (int i = 0; i < windows.Count; i++)
        {
            long wp = 0; try { wp = windows[i].invMgr != null ? (long)windows[i].invMgr.Pointer : 0; } catch { }
            if (wp != 0 && wp == ptr) { CloseAt(i); return; }
        }
        OpenWindow(im, title);
    }

    /// <summary>1.4.2：强开窗口（已开则保持不动）。按钮/G 键是开关（ToggleFor）；会合/翻找流程必须用强开，
    /// 否则目标已在选中集里时第二次调用会把刚开的窗又关掉。</summary>
    internal static bool ForceOpenSoldier(Soldier s, bool bypassAnchor = false)
    {
        if (s == null) return false;
        InventoryManager im = ResolveSoldierInv(s);
        if (im == null)
        {
            GodViewController.Flash(Ui.Tr("找不到该单位的背包数据"), 2f);
            SquadCmdLogic.LogAlways("[Backpack] 打开失败：无 InventoryManager（ForceOpen）");
            return false;
        }
        OpenWindow(im, SoldierTitle(s), bypassAnchor);
        // 1.4.3：如实回报（OpenWindow 可能被上限/锚点拦掉）
        long p = 0; try { p = (long)im.Pointer; } catch { }
        for (int i = 0; i < windows.Count; i++)
        {
            long wp = 0; try { wp = windows[i].invMgr != null ? (long)windows[i].invMgr.Pointer : 0; } catch { }
            if (wp != 0 && wp == p) return true;
        }
        return false;
    }

    /// <summary>1.4.3：开窗（平铺槽位布局，不再级联叠窗）。bypassAnchor=会合/翻找到达开窗
    /// （走过去已经证明了距离，不再被远处旧锚点拦掉；30s 后仍受范围自动关闭约束）。</summary>
    private static void OpenWindow(InventoryManager im, string title, bool bypassAnchor = false)
    {
        long ptr = 0; try { ptr = (long)im.Pointer; } catch { }
        for (int i = 0; i < windows.Count; i++)
        {
            long wp = 0; try { wp = windows[i].invMgr != null ? (long)windows[i].invMgr.Pointer : 0; } catch { }
            if (wp != 0 && wp == ptr) return; // 已开：保持
        }
        if (windows.Count >= MaxWindows)
        {
            GodViewController.Flash(string.Format(Ui.Tr("背包窗口已达上限（{0}）"), MaxWindows), 2f);
            SquadCmdLogic.LogAlways("[Backpack] 开窗被拒（达上限）：" + title);
            return;
        }
        // 锚点联动半径：窗口 0 为锚点（第一个打开的背包），其余必须在其 packRange 内
        if (!bypassAnchor && windows.Count > 0)
        {
            Vector3? anchor = PosOf(windows[0]);
            Vector3? pos = PosOf(im);
            float range = Plugin.packRange.Value;
            if (anchor.HasValue && pos.HasValue && (pos.Value - anchor.Value).sqrMagnitude > range * range)
            {
                GodViewController.Flash(string.Format(Ui.Tr("距离太远（需距锚点 {0}m 内）"), range.ToString("0")), 2.5f);
                SquadCmdLogic.LogAlways("[Backpack] 开窗被拒（距锚点超 " + range.ToString("0") + "m）：" + title);
                return;
            }
        }
        PackWindow w = new PackWindow { invMgr = im, title = title, openedAt = Time.unscaledTime };
        w.slot = FindFreeSlot();
        w.rect = SlotRect(w.slot);
        windows.Add(w);
        SquadCmdLogic.Log("[Backpack] 打开背包窗口 " + title + "（" + windows.Count + "/" + MaxWindows + " 槽" + w.slot + "）");
    }

    /// <summary>1.4.3：槽位平铺（列×行，屏幕内换列）——新窗永不压在旧窗上。</summary>
    private static Rect SlotRect(int slot)
    {
        // 2.5.1：槽位间距也随倍率（原来 150/10/14/96 写死）
        float s = Er2Ui.Scale;
        int perCol = Mathf.Max(1, Mathf.FloorToInt((Screen.height - 150f * s) / (WinH + 10f * s)));
        int col = slot / perCol, row = slot % perCol;
        float x = Mathf.Clamp(Screen.width * 0.5f - WinW * 0.5f + col * (WinW + 14f * s), 4f, Mathf.Max(4f, Screen.width - WinW - 4f));
        float y = 96f * s + row * (WinH + 10f * s);
        y = Mathf.Clamp(y, 4f, Mathf.Max(4f, Screen.height - WinH - 4f));
        return new Rect(x, y, WinW, WinH);
    }

    private static int FindFreeSlot()
    {
        var used = new HashSet<int>();
        for (int i = 0; i < windows.Count; i++) used.Add(windows[i].slot);
        for (int s = 0; ; s++) if (!used.Contains(s)) return s;
    }

    private static void CloseAt(int idx)
    {
        if (idx < 0 || idx >= windows.Count) return;
        SquadCmdLogic.Log("[Backpack] 关闭背包窗口 " + windows[idx].title);
        windows.RemoveAt(idx);
        if (dragActive && (dragWin == null || !windows.Contains(dragWin))) ResetDrag("来源窗口关闭");
    }

    /// <summary>唯一清场入口（幂等）：关全部窗口 + 清拖拽态。**不动在途的派兵会合任务**。
    /// 1.4.9 解耦：任务归任务、窗口归窗口——取消/更换选择链（ClearSelection）走的也是这里，
    /// 1.4.8 实测「兵贴身了仍不开窗且全程无到达日志」的头号嫌疑就是这条链顺手把任务清了。</summary>
    internal static void CloseAll()
    {
        CloseMenu();
        if (windows.Count == 0 && !dragActive) return;
        windows.Clear();
        ResetDrag("CloseAll");
    }

    /// <summary>真取消派兵会合/拾取任务。只在「进 RTS / 接管单位 / 退出 RTS」调用（用户主动中断）。</summary>
    internal static void CancelLoot(string reason) => ResetLoot(reason);

    // ── 派兵会合（1.3.3 尸体翻找 → 1.3.4 泛化：右键尸体开尸包；右键友军开双方背包。用户定案：
    //     不能隔空交互——最近的选中士兵走到目标旁（packRange 内）才开窗）──
    // GodViewController 右键 → RequestLoot；Tick 持久段 → LootTick（内部节流）。
    private static Soldier lootSoldier;
    private static InventoryManager lootInv;
    private static Vector3 lootPos;
    private static float lootDeadline = -10f;
    private static float nextLootTick = -10f;
    private static float nextLootTrace = -10f;
    private static bool lootBoth;       // 到达后同时开 walker 自己的背包（右键友军）
    private static bool lootSkipWalker; // walker 就是目标自己（右键的是选中集内成员）→ 只开一个
    private static ItemObject lootItem; // 1.4.1：非空 = 走过去**拾取地面物品**（否则 = 走过去开背包）
    private static Vector3 lootLastPos; // 1.4.8：停驻检测（兵停下但没进圈 → 重新下达移动）
    private static float lootLastMoveT = -10f;
    private static int lootReissues;
    // 1.4.9（用户定案「停止后再触发一次开背包」）：停驻即开窗的独立通道——
    // 只看「兵停了」这一个事实，不再依赖是否落进 packRange 圈（兵被碰撞卡在圈外/挤到贴身都覆盖）。
    private static Vector3 lootStillAnchor = Vector3.zero;
    private static float lootStillSince = -10f;
    private static float lootStartedAt = -10f;
    private static bool lootStopTried;
    private const float StopStillSeconds = 0.8f; // 位移 <0.2m 持续这么久 = 判定「停了」
    private const float StopGraceSeconds = 1.5f; // 任务开始后的宽限：别在兵刚接令还没起步时就判「停驻」
    private const float StopOpenRange = 5f;      // 停驻开窗容差：packRange 之上放宽到这里

    /// <summary>右键目标入口：最近的选中士兵已在联动半径内 → 直接开窗；否则原生 moveTo 过去，到达后自动开。</summary>
    internal static void RequestLoot(Soldier target, Vector3 targetPos, bool openBoth = false)
    {
        ResetLoot("新会合请求");
        if (target == null) return;
        Soldier best = null;
        float bd = float.MaxValue;
        Soldier anyBest = null; // 1.4.9：任意选中单位（含车内乘员）——只在"已在半径内即时开窗"时兜底
        float anyBd = float.MaxValue;
        try
        {
            foreach (Soldier s in GodViewController.GetSelectedInfantry())
            {
                try
                {
                    if (s == null || !s.IsAlive || s.transform == null) continue;
                    float d = (s.transform.position - targetPos).sqrMagnitude;
                    if (d < anyBd) { anyBd = d; anyBest = s; }
                    // 1.4.9：车内乘员不能当"走过去的人"——给他 moveTo 等于命令他下车步行
                    if (!GodViewController.IsOnFoot(s)) continue;
                    if (d < bd) { bd = d; best = s; }
                }
                catch { }
            }
        }
        catch { }
        if (best == null && anyBest != null) { best = anyBest; bd = anyBd; }
        if (best == null) { GodViewController.Flash(Ui.Tr("先框选/选中单位"), 1.5f); return; }
        bool same = false;
        try { same = (long)best.Pointer == (long)target.Pointer; } catch { }
        float range = Plugin.packRange.Value;
        float dist = Mathf.Sqrt(bd);
        string tTitle = SoldierTitle(target);
        if (bd <= range * range)
        {
            SquadCmdLogic.LogAlways("[Backpack] 会合请求 " + tTitle + " 距离=" + dist.ToString("0.0") + "m → 即时开窗");
            OpenMeet(target, same ? null : best, openBoth);
            return;
        }
        // 1.4.9：最近的"选中单位"若是车内乘员，他走不过去（原生 AI 会让他下车步行）
        if (!GodViewController.IsOnFoot(best))
        {
            // 1.4.10：选中的全在载具里（上车后「转选」到载具是常态）→ 改派最近的徒步友军，
            // 否则这个背包永远打不开。仍走同一套"派兵→到达/停驻开窗"流程。
            Soldier helper = GodViewController.NearestOnFootFriendly(targetPos, 30f);
            if (helper == null)
            {
                GodViewController.Flash(Ui.Tr("选中的单位都在载具里，附近也没有可派的徒步友军"), 2f);
                SquadCmdLogic.LogAlways("[Backpack] 会合请求被拒 " + tTitle + " 距离=" + dist.ToString("0.0") + "m：选中的单位都在载具里");
                return;
            }
            string hn = ""; try { hn = GodViewController.SafeName(helper); } catch { }
            try { same = (long)helper.Pointer == (long)target.Pointer; } catch { }
            SquadCmdLogic.LogAlways("[Backpack] 会合请求：选中的单位都在载具里 → 改派最近的徒步友军 " + hn);
            best = helper;
            bd = (helper.transform.position - targetPos).sqrMagnitude;
            dist = Mathf.Sqrt(bd);
        }
        try { new Lua_Soldier(best).moveTo(targetPos); } // 原生 Lua 移动通道（GodViewController 移动兜底同款）
        catch (Exception ex)
        {
            SquadCmdLogic.LogAlways("[Backpack] 下达前往指令失败: " + ex.Message);
            GodViewController.Flash(Ui.Tr("移动失败"), 1.5f);
            return;
        }
        lootSoldier = best;
        lootInv = ResolveSoldierInv(target);
        lootPos = targetPos;
        lootDeadline = Time.unscaledTime + 90f;
        lootBoth = openBoth;
        lootSkipWalker = same;
        lootLastPos = best.transform != null ? best.transform.position : targetPos;
        lootLastMoveT = Time.unscaledTime;
        lootReissues = 0;
        lootStillAnchor = lootLastPos;
        lootStillSince = Time.unscaledTime;
        lootStartedAt = Time.unscaledTime;
        lootStopTried = false;
        string nm = ""; try { nm = GodViewController.SafeName(best); } catch { }
        GodViewController.Flash(string.Format(Ui.Tr("已派 {0} 过去（到达后打开背包）"), nm), 2.5f);
        // 1.4.6：无条件记距离——下一轮日志能直接看出走没走/停在多远
        SquadCmdLogic.LogAlways("[Backpack] 会合请求 " + tTitle + " 距离=" + dist.ToString("0.0") + "m → 派 " + nm + " 前往");
    }

    /// <summary>1.4.1：右键地面物品 → 快速拾取（联动半径内）或派最近士兵走过去捡（超半径）。
    /// 多交互物品（弹药箱等）不走这里——宿主直接开地面交互菜单。</summary>
    internal static void RequestItemPickup(ItemObject item, Vector3 pos)
    {
        ResetLoot("新拾取请求");
        if (item == null) return;
        Soldier best = null;
        float bd = float.MaxValue;
        Soldier anyBest = null; // 1.4.9：车内乘员兜底（仅"半径内即时拾取"）
        float anyBd = float.MaxValue;
        try
        {
            foreach (Soldier s in GodViewController.GetSelectedInfantry())
            {
                try
                {
                    if (s == null || !s.IsAlive || s.transform == null) continue;
                    float d = (s.transform.position - pos).sqrMagnitude;
                    if (d < anyBd) { anyBd = d; anyBest = s; }
                    if (!GodViewController.IsOnFoot(s)) continue; // 1.4.9：车内乘员不接步行指令
                    if (d < bd) { bd = d; best = s; }
                }
                catch { }
            }
        }
        catch { }
        if (best == null && anyBest != null) { best = anyBest; bd = anyBd; }
        if (best == null) { GodViewController.Flash(Ui.Tr("先框选/选中单位"), 1.5f); return; }
        float range = Plugin.packRange.Value;
        if (bd <= range * range) { GodViewController.ExecuteGroundPickup(best, item); return; }
        if (!GodViewController.IsOnFoot(best))
        {
            // 1.4.10：同上——全在车里就改派最近的徒步友军
            Soldier helper = GodViewController.NearestOnFootFriendly(pos, 30f);
            if (helper == null)
            {
                GodViewController.Flash(Ui.Tr("选中的单位都在载具里，附近也没有可派的徒步友军"), 2f);
                SquadCmdLogic.LogAlways("[Backpack] 拾取请求被拒：选中的单位都在载具里");
                return;
            }
            string hn = ""; try { hn = GodViewController.SafeName(helper); } catch { }
            SquadCmdLogic.LogAlways("[Backpack] 拾取请求：选中的单位都在载具里 → 改派最近的徒步友军 " + hn);
            best = helper;
            bd = (helper.transform.position - pos).sqrMagnitude;
        }
        try { new Lua_Soldier(best).moveTo(pos); }
        catch (Exception ex)
        {
            SquadCmdLogic.LogAlways("[Backpack] 下达前往指令失败: " + ex.Message);
            GodViewController.Flash(Ui.Tr("移动失败"), 1.5f);
            return;
        }
        lootSoldier = best;
        lootItem = item;
        lootPos = pos;
        lootDeadline = Time.unscaledTime + 90f;
        lootLastPos = best.transform != null ? best.transform.position : pos;
        lootLastMoveT = Time.unscaledTime;
        lootReissues = 0;
        lootStillAnchor = lootLastPos;
        lootStillSince = Time.unscaledTime;
        lootStartedAt = Time.unscaledTime;
        lootStopTried = false;
        string nm = ""; try { nm = GodViewController.SafeName(best); } catch { }
        GodViewController.Flash(string.Format(Ui.Tr("已派 {0} 前去拾取（到达后捡起）"), nm), 2.5f);
        SquadCmdLogic.Log("[Backpack] 派兵拾取 " + nm + " → " + pos.ToString("0.0"));
    }

    /// <summary>到达后开窗：目标必开；openBoth 时再开 walker 自己的（去重）。
    /// 1.4.3：到达开窗 bypass 锚点（走过去已证明距离；远处旧锚点不再拦截），30s 后回归范围规则。</summary>
    private static void OpenMeet(Soldier target, Soldier walker, bool openBoth)
    {
        bool t = ForceOpenSoldier(target, true);
        bool wk = false;
        if (openBoth && walker != null && !SameSoldier(walker, target)) wk = ForceOpenSoldier(walker, true);
        SquadCmdLogic.LogAlways("[Backpack] 会合到达，开窗 target=" + t + " walker=" + wk);
    }

    private static bool SameSoldier(Soldier a, Soldier b)
    {
        try { return a != null && b != null && (long)a.Pointer == (long)b.Pointer; } catch { return false; }
    }

    /// <summary>GodViewController.Tick 持久段调用（内部 0.25s 节流，unscaled）。</summary>
    internal static void LootTick()
    {
        if (lootSoldier == null) return;
        if (Time.unscaledTime < nextLootTick) return;
        nextLootTick = Time.unscaledTime + 0.25f;
        if (Time.unscaledTime > lootDeadline)
        {
            float lastDist = -1f;
            try { if (lootSoldier != null && lootSoldier.transform != null) lastDist = Vector3.Distance(lootSoldier.transform.position, lootPos); } catch { }
            GodViewController.Flash(string.Format(Ui.Tr("没有走到目标旁（停在第 {0}m），已取消"), lastDist.ToString("0")), 2f);
            SquadCmdLogic.LogAlways("[Backpack] 会合超时取消，最后距离 " + lastDist.ToString("0.0") + "m");
            ResetLoot("超时");
            return;
        }
        bool alive = false;
        try { alive = lootSoldier.IsAlive && lootSoldier.transform != null; } catch { }
        if (!alive) { SquadCmdLogic.Log("[Backpack] 会合单位死亡/失效，取消"); ResetLoot("单位死亡/失效"); return; }
        // 1.4.8：到达判定回归 packRange（3m，用户规则）；配合停驻重派令把兵真正带进圈
        float range = Plugin.packRange.Value;
        Vector3 wpos = lootSoldier.transform.position;
        float dist = Vector3.Distance(wpos, lootPos);

        // 1.4.9：会合进行中低频跟踪（2s 一条，任务丢失/停驻全程可视化）
        if (Time.unscaledTime >= nextLootTrace)
        {
            nextLootTrace = Time.unscaledTime + 2f;
            SquadCmdLogic.Log("[Backpack] 会合进行中 dist=" + dist.ToString("0.0") + "m 重派=" + lootReissues + "/8");
        }

        // 停驻检测（0.25s 采样）：位移 <0.2m 连续 0.8s = 判定「兵停了」
        if (Vector3.Distance(wpos, lootStillAnchor) >= 0.2f)
        {
            lootStillAnchor = wpos;
            lootStillSince = Time.unscaledTime;
            lootStopTried = false; // 又动了 → 下一次停驻可以再开一次
        }
        bool stopped = Time.unscaledTime - lootStillSince >= StopStillSeconds
                    && Time.unscaledTime - lootStartedAt >= StopGraceSeconds;

        // 1.4.9（用户定案）：**停驻即开窗**——兵停下来后就再触发一次开窗，不再等它精确落进 packRange。
        // 实测场景：兵被碰撞挤在目标身上/旁边，距离判定却始终差一点 → 旧逻辑等到超时也不开。
        if (stopped && dist > range)
        {
            float stopRange = Mathf.Max(range, StopOpenRange);
            if (dist <= stopRange)
            {
                SquadCmdLogic.Log("[Backpack] 会合单位停驻开窗 dist=" + dist.ToString("0.0")
                    + "m（容差 " + stopRange.ToString("0") + "m，重派 " + lootReissues + "/8）");
                try { ArriveOpen(lootSoldier, "停驻开窗"); }
                catch (Exception ex) { SquadCmdLogic.LogAlways("[Backpack] 停驻开窗异常: " + ex.Message); ResetLoot("停驻开窗异常"); }
                return;
            }
            if (!lootStopTried)
            {
                lootStopTried = true;
                SquadCmdLogic.Log("[Backpack] 会合单位停驻但过远 dist=" + dist.ToString("0.0")
                    + "m（容差 " + stopRange.ToString("0") + "m），继续重派/等待超时");
            }
        }

        // 停驻检测：1.2s 位移 <0.5m 且仍超圈 → 重新下达 moveTo（最多 8 次，每次记距离）
        if (Time.unscaledTime - lootLastMoveT >= 1.2f)
        {
            float moved = Vector3.Distance(wpos, lootLastPos);
            if (moved < 0.5f && dist > range && lootReissues < 8)
            {
                lootReissues++;
                SquadCmdLogic.Log("[Backpack] 会合单位停驻 " + dist.ToString("0.0") + "m（目标 " + range.ToString("0") + "m），重新下达移动 (" + lootReissues + "/8)");
                try { new Lua_Soldier(lootSoldier).moveTo(lootPos); } catch (Exception ex) { SquadCmdLogic.Log("[Backpack] 重派移动失败: " + ex.Message); }
                lootStillSince = Time.unscaledTime; // 重新下达 = 新的移动段，停驻计时重来
                lootStopTried = false;
            }
            lootLastPos = wpos;
            lootLastMoveT = Time.unscaledTime;
        }

        try
        {
            if (dist <= range) // 1.4.9：线性距离对线性半径（旧写法 range*range 实际是 9m，与规则不符）
                ArriveOpen(lootSoldier, "到达，开窗");
        }
        catch (Exception ex) { SquadCmdLogic.LogAlways("[Backpack] 会合到达判定异常: " + ex.Message); ResetLoot("到达判定异常"); }
    }

    /// <summary>到达/停驻后的开窗唯一收口：拾取任务 → 走过去捡；会合任务 → 开目标背包（openBoth 再开 walker 自己的）。
    /// 先把任务态取成局部量再 ResetLoot，避免收口过程里被重入清空。</summary>
    private static void ArriveOpen(Soldier walker, string why)
    {
        if (walker == null) { ResetLoot(why + "(无 walker)"); return; }
        ItemObject pi = lootItem;
        InventoryManager inv = lootInv;
        bool both = lootBoth;
        bool skip = lootSkipWalker;
        ResetLoot(why);
        if (pi != null) { GodViewController.ExecuteGroundPickup(walker, pi); return; } // 1.4.1：拾取任务
        Soldier target = null;
        try { target = inv != null ? inv.GetComponentInParent<Soldier>() : null; } catch { }
        if (target != null) OpenMeet(target, both && !skip ? walker : null, both);
    }

    /// <summary>会合任务重置：**每次都记原因日志**（1.4.9——此前任务被静默清掉无法定位，到达检测整段失效）。</summary>
    private static void ResetLoot(string reason)
    {
        if (lootSoldier != null)
            SquadCmdLogic.Log("[Backpack] 会合任务重置（" + reason + "）");
        lootSoldier = null;
        lootInv = null;
        lootDeadline = -10f;
        lootBoth = false;
        lootSkipWalker = false;
        lootItem = null;
        lootLastPos = Vector3.zero;
        lootLastMoveT = -10f;
        lootReissues = 0;
        lootStillAnchor = Vector3.zero;
        lootStillSince = -10f;
        lootStartedAt = -10f;
        lootStopTried = false;
    }

    private static void ResetLoot() => ResetLoot("未指明");

    /// <summary>拖拽中间态唯一收口（GhostPreview 模式：任何结束路径都走这里）。</summary>
    internal static void ResetDrag(string reason)
    {
        if (!dragActive) return;
        dragActive = false;
        dragWin = null; dragItem = null; dragId = ""; dragName = ""; dragTotal = 0; dragUnits = 0;
        dragIsMag = false; dragWorn = false;
        SquadCmdLogic.Log("[Backpack] 拖拽结束：" + reason);
    }

    // ── 校验（0.5s，unscaled） ──

    private static void Validate()
    {
        if (Time.unscaledTime < nextValidate) return;
        nextValidate = Time.unscaledTime + 0.5f;
        // 交互菜单的窗口没了 → 收菜单
        if (menuWin != null && !windows.Contains(menuWin)) CloseMenu();
        // 容器失效（销毁/场景清理）
        for (int i = windows.Count - 1; i >= 0; i--)
        {
            PackWindow w = windows[i];
            bool dead = false;
            try { dead = w.invMgr == null || w.invMgr.transform == null || w.invMgr.inventory == null; } catch { dead = true; }
            if (dead)
            {
                windows.RemoveAt(i);
                if (dragActive && (dragWin == null || !windows.Contains(dragWin))) ResetDrag("来源窗口失效");
                SquadCmdLogic.Log("[Backpack] 容器失效自动关闭 " + w.title);
            }
        }
        // 拖拽来源还在吗
        if (dragActive)
        {
            bool ok = dragWin != null && windows.Contains(dragWin);
            if (ok && !dragWorn) { try { ok = FindGroupLive(dragWin.invMgr, dragId).Count > 0; } catch { ok = false; } }
            if (ok && dragWorn) { try { ok = dragItem != null && FindIndex(dragWin.invMgr, dragItem) >= 0; } catch { ok = false; } }
            if (!ok) ResetDrag("拖拽物已失效");
        }
        // 锚点联动半径复检：非锚点窗口超出 → 自动关闭（锚点永不受此规则约束）
        if (windows.Count > 1)
        {
            Vector3? anchor = PosOf(windows[0]);
            if (anchor.HasValue)
            {
                float range = Plugin.packRange.Value;
                for (int i = windows.Count - 1; i >= 1; i--)
                {
                    // 1.4.3：开窗 30s 内豁免（会合流程 units 还在动；宽限期后回归范围规则）
                    if (Time.unscaledTime - windows[i].openedAt < 30f) continue;
                    Vector3? p = PosOf(windows[i]);
                    if (!p.HasValue) continue;
                    if ((p.Value - anchor.Value).sqrMagnitude > range * range)
                    {
                        string t = windows[i].title;
                        windows.RemoveAt(i);
                        if (dragActive && (dragWin == null || !windows.Contains(dragWin))) ResetDrag("来源窗口关闭");
                        GodViewController.Flash(Ui.Tr("超出联动半径，已关闭：") + t, 2.5f);
                        SquadCmdLogic.LogAlways("[Backpack] 超出联动半径（" + range.ToString("0") + "m）关闭窗口 " + t);
                    }
                }
            }
        }
    }

    // ── 单窗口绘制 ──

    private static void DrawWindow(PackWindow w, Event e, ref bool dropHandled)
    {
        if (Time.unscaledTime > w.nextRefresh) { w.nextRefresh = Time.unscaledTime + 0.25f; RefreshCells(w); }

        Rect r = w.rect;
        Color baseC = GodViewController.UiBase;
        Color hoverC = GodViewController.UiHover;
        Color textC = GodViewController.UiText;

        // 2.5.1：标题栏/角标这一层的像素量全部随倍率（原来写死，高倍率下会和格子尺寸脱节）
        float k = Er2Ui.Scale;

        // 底板 + 描边（InfoPanel 同款：不透明底防透字）
        // 2.5.2：底板不再把玩家色"折半"——×0.5 后默认只有 0.06 灰，这是"整个 UI 太黑"的主要来源；
        // 描边改共享令牌 `PanelBorder`，让它成为真正的设计元素而非"底色的半透明版"。
        GUI.color = new Color(baseC.r, baseC.g, baseC.b, 0.95f);
        GUI.DrawTexture(r, Texture2D.whiteTexture);
        // 2.5.2：标题条独立底色——把"标题/关闭/翻页"从格子区里分出来（设计感）
        Er2Ui.Fill(new Rect(r.x, r.y, r.width, TitleH), Er2Ui.TitleBar);
        GUI.color = Er2Ui.PanelBorder;
        float bd = Mathf.Max(1f, 1f * k);
        GUI.DrawTexture(new Rect(r.x, r.y, r.width, bd), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.yMax - bd, r.width, bd), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.x, r.y, bd, r.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(r.xMax - bd, r.y, bd, r.height), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // ── 标题栏：拖动 + 标题 + 负重 + 翻页 + 关闭 ──
        Rect titleBar = new Rect(r.x, r.y, r.width - 24f * k, TitleH);
        if (titleBar.Contains(e.mousePosition) && e.type == EventType.MouseDown && e.button == 0 && !dragActive)
        {
            w.titleDrag = true;
            e.Use();
        }
        if (w.titleDrag)
        {
            if (e.type == EventType.MouseDrag) { w.rect.position += e.delta; e.Use(); }
            else if (e.type == EventType.MouseUp) { w.titleDrag = false; e.Use(); }
        }
        // 1.4.9：标题可用宽度 = 窗口宽 - 左内边距 - 右侧留给「负重(64)+翻页(70)+关闭(20)」
        float titleW = r.width - 182f * k;
        FitTitle(w, titleW - 2f * k);
        int keepSize = textStyle.fontSize;
        textStyle.fontSize = w.titleSize;
        BackpackLabel(new Rect(r.x + 8f * k, r.y + 4f * k, titleW, 20f * k), w.titleDisplay, textC, textStyle);
        textStyle.fontSize = keepSize;

        // 负重（GetWeightAndMaxWeight 取不到就不显示）
        if (TryGetWeight(w.invMgr, out float cw, out float mw) && mw > 0f)
        {
            Color wc = cw >= mw ? new Color(0.95f, 0.3f, 0.25f, 1f)
                : cw >= mw * 0.8f ? new Color(0.95f, 0.75f, 0.25f, 1f)
                : new Color(0.4f, 0.85f, 0.4f, 1f);
            Rect wr = new Rect(r.xMax - 166f * k, r.y + 4f * k, 64f * k, 18f * k);
            TextAnchor keepAlign = textStyle.alignment;
            textStyle.alignment = TextAnchor.MiddleRight;
            BackpackLabel(wr, cw.ToString("0.#") + "/" + mw.ToString("0.#"), wc, textStyle);
            textStyle.alignment = keepAlign;
            // 迷你负重条
            float frac = Mathf.Clamp01(cw / mw);
            Rect bar = new Rect(r.xMax - 166f * k, r.y + TitleH - 4f * k, 64f * k, 2.5f * k);
            GUI.color = new Color(0f, 0f, 0f, 0.6f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = wc;
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * frac, bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;
        }

        int pages = PageCount(w);
        if (pages > 1)
        {
            if (GhostBtn(new Rect(r.xMax - 96f * k, r.y + 3f * k, 20f * k, 20f * k), "◀", textC, hoverC)) w.page = (w.page - 1 + pages) % pages;
            BackpackLabel(new Rect(r.xMax - 74f * k, r.y + 4f * k, 28f * k, 18f * k), (w.page + 1) + "/" + pages, textC, tipStyle);
            if (GhostBtn(new Rect(r.xMax - 46f * k, r.y + 3f * k, 20f * k, 20f * k), "▶", textC, hoverC)) w.page = (w.page + 1) % pages;
        }
        if (GhostBtn(new Rect(r.xMax - 24f * k, r.y + 3f * k, 20f * k, 20f * k), "✕", textC, hoverC)) { CloseAt(windows.IndexOf(w)); return; }
        // 1.4.2：标题栏分隔线（UI 打磨）；2.5.2：改共享令牌 Edge（原 hoverC@0.28 随玩家配色漂移）
        GUI.color = Er2Ui.Edge;
        GUI.DrawTexture(new Rect(r.x + 1f * k, r.y + TitleH, r.width - 2f * k, 1f * k), Texture2D.whiteTexture);
        GUI.color = Color.white;

        // ── 格子 ──
        float gx = r.x + Margin;
        float gy = r.y + TitleH + 4f * Er2Ui.Scale;
        for (int row = 0; row < Rows; row++)
        {
            for (int col = 0; col < Cols; col++)
            {
                int idx = w.page * PerPage + row * Cols + col;
                Rect cr = new Rect(gx + col * (Cell + Gap), gy + row * (Cell + Gap), Cell, Cell);
                DrawCell(w, idx, cr, e, ref dropHandled);
            }
        }
    }

    private static void DrawCell(PackWindow w, int idx, Rect cr, Event e, ref bool dropHandled)
    {
        bool has = idx < w.cells.Count;
        PackCell c = has ? w.cells[idx] : null;
        bool hover = cr.Contains(e.mousePosition);

        // 2.5.1：格子内的像素量随倍率（格底尺寸已随 Cell，但描边/内缩/角标这些仍写死）
        float k = Er2Ui.Scale;

        // 格底 + 细描边
        // 2.5.2：格底由纯黑@0.35 改共享的列表底色（黑格是背包观感"太黑、没层次"的另一半原因）
        GUI.color = hover ? Er2Ui.RowHover : Er2Ui.ListBg;
        GUI.DrawTexture(cr, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, 0.08f);
        float ce = 1f * k;
        GUI.DrawTexture(new Rect(cr.x, cr.y, cr.width, ce), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cr.x, cr.yMax - ce, cr.width, ce), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cr.x, cr.y, ce, cr.height), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(cr.xMax - ce, cr.y, ce, cr.height), Texture2D.whiteTexture);

        bool isDragSrc = dragActive && c != null && string.Equals(c.id, dragId, StringComparison.Ordinal);

        if (has)
        {
            if (c.worn)
            {
                // 穿戴/手持：灰描边（可拿起，但只能丢地上）
                GUI.color = new Color(0.75f, 0.78f, 0.75f, 0.85f);
                float we = 1.5f * k;
                GUI.DrawTexture(new Rect(cr.x, cr.y, cr.width, we), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cr.x, cr.yMax - we, cr.width, we), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cr.x, cr.y, we, cr.height), Texture2D.whiteTexture);
                GUI.DrawTexture(new Rect(cr.xMax - we, cr.y, we, cr.height), Texture2D.whiteTexture);
            }
            Texture2D iconTex = GetIconTex(c.first, c.id);
            if (iconTex != null)
                DrawIconTex(iconTex, new Rect(cr.x + 4f * k, cr.y + 4f * k, cr.width - 8f * k, cr.height - 8f * k), isDragSrc ? 0.25f : 1f);
            if (c.total > 1 || c.isMag || c.units > 1)
            {
                Rect br = new Rect(cr.xMax - 30f * k, cr.yMax - 15f * k, 28f * k, 13f * k);
                GUI.color = new Color(0f, 0f, 0f, 0.78f);
                GUI.DrawTexture(br, Texture2D.whiteTexture);
                GUI.color = Color.white;
                GUI.Label(br, "×" + c.total, badgeStyle);
            }
            if (hover && !dragActive && e.type == EventType.Repaint) DrawTooltip(c, e.mousePosition);
        }

        // ── 交互 ──
        if (hover && e.type == EventType.MouseDown && e.button == 0 && !dragActive && has)
        {
            e.Use();
            CloseMenu();
            dragWin = w; dragId = c.id; dragName = c.name; dragTotal = c.total; dragUnits = c.units;
            dragIsMag = c.isMag; dragWorn = c.worn; dragItem = c.first; dragActive = true;
            SquadCmdLogic.Log("[Backpack] 拾取 " + c.id + " ×" + c.total + (c.worn ? "（穿戴）" : ""));
        }
        else if (hover && e.type == EventType.MouseUp && e.button == 0 && dragActive)
        {
            e.Use();
            dropHandled = true;
            HandleDrop(w, idx);
        }
        else if (hover && dragActive && e.type == EventType.MouseDown && e.button == 1)
        {
            e.Use();
            ResetDrag("右键取消");
        }
        else if (hover && !dragActive && has && e.type == EventType.MouseDown && e.button == 1)
        {
            // 1.4.0：右键物品 → 原生交互菜单（穿/吃/卸下…）
            e.Use();
            OpenMenu(w, c, e.mousePosition);
        }
    }

    // ── 放置判定（1.3.1：整组语义）──

    private static void HandleDrop(PackWindow w, int idx)
    {
        PackWindow src = dragWin;
        string id = dragId;
        if (src == null || string.IsNullOrEmpty(id) || w == null || w.invMgr == null || src.invMgr == null) { ResetDrag("来源/目标失效"); return; }

        if (dragWorn)
        {
            // 穿戴/手持：不能放回格子（会破坏装备引用），只能丢地上
            GodViewController.Flash(Ui.Tr("装备中的物品只能丢弃到地上"), 1.8f);
            RefreshNow(src); RefreshNow(w);
            ResetDrag("穿戴项放下");
            return;
        }

        long srcPtr = 0, dstPtr = 0;
        try { srcPtr = (long)src.invMgr.Pointer; dstPtr = (long)w.invMgr.Pointer; } catch { }
        if (srcPtr != 0 && srcPtr == dstPtr) { ResetDrag("原地放下"); return; } // 格子已按分类排序，同窗重排无意义

        // 活取来源组（按 id 找非穿戴成员——AI 可能在 0.25s 间隙消耗了部分）
        List<VirtualItem> srcLive = FindGroupLive(src.invMgr, id);
        if (srcLive.Count == 0)
        {
            GodViewController.Flash(Ui.Tr("物品已不在原背包"), 1.5f);
            RefreshNow(src); RefreshNow(w);
            ResetDrag("物品消失");
            return;
        }

        // 目标组：格子快照有物 且 id 不同 → 交换；同 id → 并入（=移动）；空格 → 移动
        string dstId = idx >= 0 && idx < w.cells.Count ? w.cells[idx].id : null;
        List<VirtualItem> dstLive = null;
        if (!string.IsNullOrEmpty(dstId) && !string.Equals(dstId, id, StringComparison.Ordinal))
        {
            dstLive = FindGroupLive(w.invMgr, dstId);
            if (dstLive.Count == 0) dstLive = null; // 快照有、活的没了 → 当空格处理
        }

        float massA = SumMass(srcLive);
        float massB = dstLive != null ? SumMass(dstLive) : 0f;
        if (!WeightRoom(w.invMgr, massA, massB) || !WeightRoom(src.invMgr, massB, massA))
        {
            GodViewController.Flash(dstLive != null ? Ui.Tr("交换超重，已取消") : Ui.Tr("超过负重上限，无法放入"), 1.8f);
            RefreshNow(src); RefreshNow(w);
            ResetDrag("超重");
            return;
        }

        try
        {
            if (dstLive != null) MoveAll(dstLive, w.invMgr, src.invMgr); // 交换：先把对方组挪过来
            MoveAll(srcLive, src.invMgr, w.invMgr);
            string tn = "";
            try { tn = srcLive[0].GetIl2CppType().Name; } catch { } // 子类类型名：验证搬移未把物品降级成基类（陷阱 16）
            SquadCmdLogic.Log("[Backpack] " + (dstLive != null ? "交换" : "移动") + " '" + id + "' ×" + srcLive.Count + " 件 (" + tn + ") "
                + src.title + " → " + w.title);
        }
        catch (Exception ex)
        {
            SquadCmdLogic.LogAlways("[Backpack] 移动/交换失败: " + ex.Message);
            GodViewController.Flash(Ui.Tr("移动失败"), 1.5f);
        }
        RefreshNow(src); RefreshNow(w);
        ResetDrag(dstLive != null ? "交换完成" : "移动完成");
    }

    private static void DropToGround()
    {
        PackWindow src = dragWin;
        string id = dragId;
        ResetDrag("丢出");
        if (src == null || src.invMgr == null || string.IsNullOrEmpty(id)) return;
        List<VirtualItem> srcLive = FindGroupLive(src.invMgr, id);
        if (srcLive.Count == 0) { GodViewController.Flash(Ui.Tr("物品已不在原背包"), 1.5f); return; }
        int dropped = 0;
        for (int i = 0; i < srcLive.Count; i++)
        {
            VirtualItem vi = srcLive[i];
            // 首选原生 DropItem（生成世界实物并移除）；未生效/异常走 ExtractAndInstantiate 兜底
            bool removed = false;
            try
            {
                src.invMgr.DropItem(vi);
                removed = FindIndex(src.invMgr, vi) < 0;
            }
            catch (Exception ex) { SquadCmdLogic.Log("[Backpack] DropItem 异常: " + ex.Message); }
            if (!removed)
            {
                try
                {
                    src.invMgr.ExtractAndInstantiate(vi);
                    var items = ItemsOf(src.invMgr);
                    int idx = FindIndex(src.invMgr, vi);
                    if (items != null && idx >= 0) items.RemoveAt(idx);
                    removed = true;
                }
                catch (Exception ex) { SquadCmdLogic.Log("[Backpack] ExtractAndInstantiate 兜底失败: " + ex.Message); }
            }
            if (removed) dropped++;
        }
        if (dropped > 0)
        {
            GodViewController.Flash(Ui.Tr("已丢弃 ") + dragName + " ×" + dropped, 1.5f);
            SquadCmdLogic.LogAlways("[Backpack] 丢弃 '" + id + "' ×" + dropped + "/" + srcLive.Count + "（" + src.title + "）");
        }
        RefreshNow(src);
    }

    /// <summary>穿戴/手持物品丢地上：首选士兵原生卸下路径 DropItemNow（正确处理手持模型）。</summary>
    private static void DropWornToGround()
    {
        PackWindow src = dragWin;
        VirtualItem item = dragItem;
        string id = dragId;
        ResetDrag("丢出（穿戴）");
        if (src == null || src.invMgr == null || item == null) return;
        if (FindIndex(src.invMgr, item) < 0) { GodViewController.Flash(Ui.Tr("物品已不在原背包"), 1.5f); return; }
        bool removed = false;
        // ① 士兵穿戴槽：找到 wearedItems 里 inventoryReference 指向该物品的槽，走原生 DropItemNow
        try
        {
            Soldier owner = src.invMgr.GetComponentInParent<Soldier>();
            if (owner != null)
            {
                var held = owner.GetAllHeldItems();
                if (held != null)
                    for (int i = 0; i < held.Length; i++)
                    {
                        try
                        {
                            WearedItem wi = held[i];
                            if (wi.inventoryReference == null) continue;
                            if ((long)wi.inventoryReference.Pointer != (long)item.Pointer) continue;
                            owner.DropItemNow(i);
                            removed = FindIndex(src.invMgr, item) < 0;
                            if (removed) SquadCmdLogic.Log("[Backpack] DropItemNow 卸下成功 idx=" + i + " " + id);
                            break;
                        }
                        catch { }
                    }
            }
        }
        catch (Exception ex) { SquadCmdLogic.Log("[Backpack] DropItemNow 异常: " + ex.Message); }
        // ② 通用 DropItem
        if (!removed)
        {
            try
            {
                src.invMgr.DropItem(item);
                removed = FindIndex(src.invMgr, item) < 0;
            }
            catch (Exception ex) { SquadCmdLogic.Log("[Backpack] DropItem 异常: " + ex.Message); }
        }
        // ③ ExtractAndInstantiate + 手动移除
        if (!removed)
        {
            try
            {
                src.invMgr.ExtractAndInstantiate(item);
                var items = ItemsOf(src.invMgr);
                int idx = FindIndex(src.invMgr, item);
                if (items != null && idx >= 0) items.RemoveAt(idx);
                removed = true;
            }
            catch (Exception ex) { SquadCmdLogic.Log("[Backpack] ExtractAndInstantiate 兜底失败: " + ex.Message); }
        }
        if (removed)
        {
            GodViewController.Flash(Ui.Tr("已丢弃 ") + dragName, 1.5f);
            SquadCmdLogic.LogAlways("[Backpack] 丢弃（穿戴）'" + id + "'（" + src.title + "）");
        }
        else SquadCmdLogic.LogAlways("[Backpack] 丢弃（穿戴）失败 '" + id + "'");
        RefreshNow(src);
    }

    // ── 负重 ──

    private static bool TryGetWeight(InventoryManager im, out float cur, out float max)
    {
        try
        {
            im.GetWeightAndMaxWeight(out cur, out max);
            return cur >= 0f && max > 0f;
        }
        catch { cur = -1f; max = -1f; return false; }
    }

    /// <summary>im 还能装 addMass（同时换出 removeMass）吗。负重接口不可用时放行（上帝视角工具，日志可查）。</summary>
    private static bool WeightRoom(InventoryManager im, float addMass, float removeMass)
    {
        if (TryGetWeight(im, out float cur, out float max)) return cur + addMass - removeMass <= max + 0.01f;
        return true;
    }

    private static float SumMass(List<VirtualItem> items)
    {
        float m = 0f;
        if (items == null) return 0f;
        for (int i = 0; i < items.Count; i++) { try { m += items[i].GetMass(); } catch { } }
        return m;
    }

    // ── 数据读取 ──

    private static Il2CppSystem.Collections.Generic.List<VirtualItem> ItemsOf(InventoryManager im)
    {
        try { var inv = im.inventory; return inv != null ? inv.items : null; } catch { return null; }
    }

    private static int FindIndex(InventoryManager im, VirtualItem vi)
    {
        var items = ItemsOf(im);
        if (items == null || vi == null) return -1;
        long p = 0; try { p = (long)vi.Pointer; } catch { return -1; }
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                VirtualItem it = items[i];
                if (it == null) continue;
                try { if ((long)it.Pointer == p) return i; } catch { }
            }
        }
        catch { }
        return -1;
    }

    /// <summary>按 id 活取一组非穿戴成员（拖放时以它为准，格子快照只做定位）。</summary>
    private static List<VirtualItem> FindGroupLive(InventoryManager im, string id)
    {
        var res = new List<VirtualItem>();
        var items = ItemsOf(im);
        if (items == null || string.IsNullOrEmpty(id)) return res;
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                VirtualItem vi = items[i];
                if (vi == null) continue;
                string vid = null; try { vid = vi.item_id; } catch { }
                if (!string.Equals(vid, id, StringComparison.Ordinal)) continue;
                bool worn = false; try { worn = vi.IsWearedItem(); } catch { }
                if (worn) continue;
                res.Add(vi);
            }
        }
        catch { }
        return res;
    }

    private static void MoveAll(List<VirtualItem> group, InventoryManager from, InventoryManager to)
    {
        var srcList = ItemsOf(from);
        var dstList = ItemsOf(to);
        if (srcList == null || dstList == null) throw new InvalidOperationException("inventory/items 不可用");
        for (int i = 0; i < group.Count; i++)
        {
            VirtualItem vi = group[i];
            if (vi == null) continue;
            int idx = FindIndex(from, vi);
            if (idx >= 0) srcList.RemoveAt(idx);
            dstList.Add(vi);
        }
    }

    private static void RefreshCells(PackWindow w)
    {
        w.cells.Clear();
        var items = ItemsOf(w.invMgr);
        if (items == null) return;
        var groups = new Dictionary<string, PackCell>();
        var order = new List<string>();
        var wornCells = new List<PackCell>();
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                VirtualItem vi = items[i];
                if (vi == null) continue;
                string id = null; try { id = vi.item_id; } catch { }
                if (string.IsNullOrEmpty(id)) continue;
                int count = 1;
                bool isMag = false;
                try
                {
                    var mag = vi.TryCast<VirtualMagazineItem>();
                    if (mag != null) { isMag = true; count = mag.GetAmmoCount(); }
                    else
                    {
                        var st = vi.TryCast<VirtualItemStackable>();
                        if (st != null) count = st.GetStackCount();
                    }
                }
                catch { }
                bool worn = false; try { worn = vi.IsWearedItem(); } catch { }
                float mass = 0f; try { mass = vi.GetMass(); } catch { }

                if (worn)
                {
                    // 穿戴项不并入组（每件单独锁定格）
                    PackCell wc = new PackCell
                    {
                        id = id,
                        name = DisplayName(vi, id),
                        total = count,
                        units = 1,
                        mass = mass,
                        worn = true,
                        isMag = isMag,
                        first = vi,
                        cat = ItemCategory(id)
                    };
                    wornCells.Add(wc);
                    GetIcon(vi, id);
                    continue;
                }
                if (!groups.TryGetValue(id, out PackCell c))
                {
                    c = new PackCell { id = id, cat = ItemCategory(id) };
                    groups[id] = c;
                    order.Add(id);
                }
                c.total += count;
                c.units++;
                c.mass += mass;
                if (c.first == null)
                {
                    c.first = vi;
                    c.name = DisplayName(vi, id);
                    c.isMag = isMag;
                }
                GetIcon(vi, id); // 预热图标
            }
        }
        catch (Exception ex) { SquadCmdLogic.Log("[Backpack] 背包读取失败: " + ex.Message); }
        foreach (string id in order) w.cells.Add(groups[id]);
        w.cells.AddRange(wornCells);
        // 1.3.1：按分类（武器→弹药→爆炸物→医疗→装备→其他）+ id 排序，同类聚在一起不再杂乱
        w.cells.Sort((a, b) => a.cat != b.cat ? a.cat.CompareTo(b.cat) : string.CompareOrdinal(a.id, b.id));
        if (w.page >= PageCount(w)) w.page = PageCount(w) - 1;
        if (w.page < 0) w.page = 0;
    }

    /// <summary>按 item_id 关键字粗分类（沿用 1.2.5 六类；识别不出归"其他"）。</summary>
    private static int ItemCategory(string id)
    {
        if (string.IsNullOrEmpty(id)) return 5;
        string s = id.ToLowerInvariant();
        bool Has(params string[] ks)
        {
            foreach (string k in ks) if (s.Contains(k)) return true;
            return false;
        }
        if (Has("bandage", "medkit", "med_kit", "syringe", "morphine", "firstaid", "first_aid", "medical", "tourniquet", "splint")) return 3;
        if (Has("grenade", "mine", "satchel", "tnt", "dynamite", "explosive", "smoke_", "flare")) return 2;
        if (Has("ammo", "magazine", "_mag", "clip", "shell", "cartridge", "bullet", "rocket", "warhead")) return 1;
        if (Has("gun", "rifle", "smg", "pistol", "revolver", "carbine", "lmg", "hmg", "mg_", "_mg",
                "kar98", "karabiner", "mosin", "garand", "enfield", "thompson", "ppsh", "ppsh41", "stg",
                "mp40", "mp44", "bren", "bazooka", "panzerfaust", "panzerschreck", "launcher", "mortar",
                "bayonet", "knife", "sword", "katana", "saber")) return 0;
        if (Has("helmet", "vest", "uniform", "binocular", "compass", "shovel", "radio", "backpack", "bag",
                "canteen", "toolbox", "repair", "gas_mask", "gasmask", "helmet", "armor", "bandolier", "pouch")) return 4;
        return 5;
    }

    private static int PageCount(PackWindow w) => Mathf.Max(1, Mathf.CeilToInt(w.cells.Count / (float)PerPage));

    private static void RefreshNow(PackWindow w) { if (w != null) w.nextRefresh = -10f; }

    private static string DisplayName(VirtualItem vi, string id)
    {
        try { string n = vi.GetName(); if (!string.IsNullOrEmpty(n)) return n; } catch { }
        return string.IsNullOrEmpty(id) ? "?" : id;
    }

    private static Vector3? PosOf(PackWindow w) { return w != null ? PosOf(w.invMgr) : null; }

    private static Vector3? PosOf(InventoryManager im)
    {
        try { if (im != null && im.transform != null) return im.transform.position; } catch { }
        return null;
    }

    // ── 图标（1.3.4 终案：解析 → 立刻光栅化成自建 Texture2D，绘制零外国对象）──

    /// <summary>取格子可用的自建贴图。只在 Layout 阶段做 GPU 光栅化（避免打断 IMGUI 绘制状态）。</summary>
    private static Texture2D GetIconTex(VirtualItem vi, string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (iconTexCache.TryGetValue(id, out Texture2D t) && t != null) return t;
        Event e = Event.current;
        if (e != null && e.type != EventType.Layout && e.type != EventType.Ignore) return null;
        Sprite sp = GetIcon(vi, id); // 解析链（有 memo）
        if (sp == null) return null;
        Texture2D tex = RasterizeIcon(sp, id);
        if (tex != null) iconTexCache[id] = tex;
        return tex;
    }

    /// <summary>把 Sprite 的图集子区域 blit 进自建 Texture2D（GPU 侧拷贝，源图不必可读）。</summary>
    private static Texture2D RasterizeIcon(Sprite sp, string id)
    {
        Texture2D outTex = null;
        RenderTexture rt = null;
        try
        {
            Texture src = sp.texture;
            Rect tr = sp.textureRect;
            if (src == null || tr.width < 2f || tr.height < 2f)
            {
                LogIconDiag(id, "光栅化跳过 src=" + (src != null) + " tr=" + tr);
                return null;
            }
            int w = Mathf.Clamp(Mathf.CeilToInt(tr.width), 2, 256);
            int h = Mathf.Clamp(Mathf.CeilToInt(tr.height), 2, 256);
            outTex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            outTex.hideFlags = (HideFlags)61; // 陷阱 12：运行时贴图防场景卸载
            rt = RenderTexture.GetTemporary((int)src.width, (int)src.height, 0);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            outTex.ReadPixels(new Rect(tr.x, tr.y, tr.width, tr.height), 0, 0); // ReadPixels 同为左下原点，1:1 拷贝
            outTex.Apply(false, false);
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            rt = null;
            iconTexKeepAlive.Add(outTex);
            if (!iconDrawLogged.Contains(id))
            {
                iconDrawLogged.Add(id);
                LogIconDiag(id, "光栅化 OK " + w + "x" + h + "（源 " + src.width + "x" + src.height + " tr=" + tr + "）");
            }
            return outTex;
        }
        catch (Exception ex)
        {
            LogIconDiag(id, "光栅化异常 " + ex.Message);
            if (rt != null) { try { RenderTexture.ReleaseTemporary(rt); } catch { } }
            try { if (outTex != null) UnityEngine.Object.Destroy(outTex); } catch { }
            return null;
        }
    }

    private static Sprite CacheIcon(string id, Sprite sp)
    {
        if (sp == null) return null;
        iconCache[id] = sp;
        return sp;
    }

    private static Sprite GetIcon(VirtualItem vi, string id)
    {
        if (string.IsNullOrEmpty(id) || vi == null) return null;
        if (iconCache.TryGetValue(id, out Sprite sp) && sp != null) return sp;
        if (Time.unscaledTime < (iconNextTry.TryGetValue(id, out float t) ? t : 0f)) return null;

        // ① 模板 ItemObject.icon（1.2.4 实测路径；GetItemPrefab 可能触发异步加载而暂时为 null）
        ItemObject prefab = null;
        try { prefab = vi.GetItemPrefab(); } catch (Exception ex) { SquadCmdLogic.Log("[Backpack] GetItemPrefab 异常（" + id + "）: " + ex.Message); }
        if (prefab == null) { try { prefab = ItemsDatabase.GetItemObject(id); } catch { } }
        if (prefab != null)
        {
            Sprite ic = null; try { ic = prefab.icon; } catch { }
            if (ic != null) return CacheIcon(id, ic);
        }

        // ② 全局 sprite 缓存按名找（游戏加载过的图标都在 ItemsDatabase.cachedLoadedSprites）
        int cachedCount = -1;
        try
        {
            var cached = ItemsDatabase.cachedLoadedSprites;
            if (cached != null)
            {
                cachedCount = cached.Count;
                if (cached.TryGetValue(id, out Sprite s2)) return CacheIcon(id, s2);
                foreach (Sprite v in cached.Values)
                {
                    try { if (v != null && v.name == id) return CacheIcon(id, v); } catch { }
                }
            }
        }
        catch { }

        // ③ er2gui bundle 按名直取（试 id 与常见后缀）
        string[] nameTries = { id, id + "_icon", "icon_" + id };
        for (int i = 0; i < nameTries.Length; i++)
        {
            try
            {
                Sprite s3 = ItemsDatabase.LoadAndCacheSprite(nameTries[i], "er2gui");
                if (s3 != null) return CacheIcon(id, s3);
            }
            catch { }
        }

        // ④ 官方异步路径（原生 UI 同款；回调填 CacheIcon）
        if (!iconPending.Contains(id))
        {
            iconPending.Add(id);
            try
            {
                VirtualItem viRef = vi;
                string key = id;
                var cb = DelegateSupport.ConvertDelegate<Il2CppSystem.Action<Sprite>>(
                    (System.Delegate)(System.Action<Sprite>)(s =>
                    {
                        try { iconPending.Remove(key); CacheIcon(key, s); } catch { }
                    }));
                viRef.LoadIconAsync(cb);
            }
            catch (Exception ex)
            {
                iconPending.Remove(id);
                SquadCmdLogic.Log("[Backpack] LoadIconAsync 不可用（" + id + "）: " + ex.Message);
            }
        }

        // 全链未命中：2s 后重试；只记一次无条件诊断（失败日志不被 debugLog 门控）
        iconNextTry[id] = Time.unscaledTime + 2f;
        if (!iconLogged.Contains(id))
        {
            iconLogged.Add(id);
            SquadCmdLogic.LogAlways("[Backpack] 图标未命中 id=" + id
                + " prefab=" + (prefab != null)
                + " cachedSprites=" + cachedCount
                + " loadIconAsyncPending=" + iconPending.Contains(id));
        }
        return null;
    }

    /// <summary>自建贴图 → IMGUI：保纵横比居中。只用 GUI.DrawTexture（本 mod 全程验证过的路径）。</summary>
    private static void DrawIconTex(Texture2D tex, Rect into, float alpha)
    {
        try
        {
            float asp = tex.width / (float)tex.height;
            float w = into.width, h = into.height;
            if (w / h > asp) w = h * asp; else h = w / asp;
            Rect fit = new Rect(into.x + (into.width - w) * 0.5f, into.y + (into.height - h) * 0.5f, w, h);
            Color keep = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, alpha);
            GUI.DrawTexture(fit, tex, ScaleMode.ScaleToFit, true);
            GUI.color = keep;
        }
        catch (Exception ex) { LogIconDiag(null, "绘制异常 " + ex.Message); }
    }

    /// <summary>图标链路诊断：每个 id 只记一次，无条件（失败日志不被 debugLog 门控）。</summary>
    private static void LogIconDiag(string id, string msg)
    {
        try { SquadCmdLogic.Log("[Backpack] 图标诊断 id=" + (id ?? "?") + " " + msg); } catch { }
    }

    // ── 悬浮提示 / 拖拽幽灵 ──

    private static void DrawTooltip(PackCell c, Vector2 m)
    {
        string name = string.IsNullOrEmpty(c.name) ? c.id : c.name;
        string line2 = "";
        if (c.total > 1 || c.units > 1) line2 += "×" + c.total + (c.units > 1 ? " (" + c.units + ")" : "") + "  ";
        try { if (c.mass >= 0f) line2 += c.mass.ToString("0.##") + "kg"; } catch { }
        if (c.worn) line2 += (line2 != "" ? "  " : "") + Ui.Tr("装备中");

        Vector2 s1 = textStyle.CalcSize(new GUIContent(name));
        Vector2 s2 = tipStyle.CalcSize(new GUIContent(line2));
        // 2.5.1：提示框尺寸随倍率（字变大时框也得跟着，否则字溢出黑框）
        float k = Er2Ui.Scale;
        float bw = Mathf.Max(110f * k, Mathf.Max(s1.x, s2.x) + 16f * k);
        float bh = 18f * k + (line2 != "" ? 14f * k : 6f * k);
        float bx = Mathf.Clamp(m.x + 14f * k, 2f, Screen.width - bw - 2f);
        float by = Mathf.Clamp(m.y + 14f * k, 2f, Screen.height - bh - 2f);
        Rect tr = new Rect(bx, by, bw, bh);

        GUI.color = new Color(0f, 0f, 0f, 0.88f);
        GUI.DrawTexture(tr, Texture2D.whiteTexture);
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        GUI.DrawTexture(new Rect(tr.x, tr.y, tr.width, 1f * k), Texture2D.whiteTexture);
        GUI.color = Color.white;
        BackpackLabel(new Rect(tr.x + 8f * k, tr.y + 2f * k, bw - 16f * k, 16f * k), name, Color.white, textStyle);
        if (line2 != "")
            BackpackLabel(new Rect(tr.x + 8f * k, tr.y + 18f * k, bw - 16f * k, 14f * k), line2, new Color(0.8f, 0.85f, 0.8f, 0.95f), tipStyle);
    }

    private static void DrawDragGhost(Vector2 m)
    {
        float k = Er2Ui.Scale;   // 2.5.1：拖拽幽灵随倍率
        Rect gr = new Rect(m.x + 10f * k, m.y + 10f * k, 40f * k, 40f * k);
        GUI.color = new Color(0f, 0f, 0f, 0.55f);
        GUI.DrawTexture(new Rect(gr.x - 2f * k, gr.y - 2f * k, gr.width + 4f * k, gr.height + 4f * k), Texture2D.whiteTexture);
        GUI.color = Color.white;
        if (!string.IsNullOrEmpty(dragId) && iconTexCache.TryGetValue(dragId, out Texture2D sp))
            DrawIconTex(sp, gr, 0.85f);
        if (dragTotal > 1 || dragUnits > 1)
        {
            Rect br = new Rect(gr.xMax - 28f * k, gr.yMax - 15f * k, 30f * k, 13f * k);
            GUI.color = new Color(0f, 0f, 0f, 0.78f);
            GUI.DrawTexture(br, Texture2D.whiteTexture);
            GUI.color = Color.white;
            GUI.Label(br, "×" + dragTotal, badgeStyle);
        }
    }

    // ── 绘制小工具 ──

    private static float styleScale = 1f;

    private static void EnsureStyles()
    {
        // 2.5.1：字号随 Er2Ui.Scale 走 → 倍率一变就得重建（原来是"建过就不再建"，缩放后字不跟着变）
        if (textStyle != null && !Er2Ui.ScaleChangedSince(styleScale)) return;
        styleScale = Er2Ui.Scale;
        Font f = null;
        try { f = SquadCmdLogic.HudStyleSmall().font; } catch { }
        textStyle = MakeStyle(f, Er2Ui.FontBody, TextAnchor.MiddleLeft, Color.white, false);
        tipStyle = MakeStyle(f, Er2Ui.FontSmall, TextAnchor.MiddleCenter, new Color(0.85f, 0.9f, 0.85f, 0.95f), false);
        badgeStyle = MakeStyle(f, Er2Ui.FontSmall, TextAnchor.MiddleCenter, Color.white, true);
    }

	/// <summary>2.4.2：样式工厂已上移到 ER2Shared.Er2Ui（两个 mod 同一份实现）。</summary>
	private static GUIStyle MakeStyle(Font f, int size, TextAnchor align, Color color, bool bold)
	{
		return Er2Ui.MakeLabel(size, align, color, bold ? FontStyle.Bold : FontStyle.Normal, f);
	}

    /// <summary>1.4.9：标题适配可用宽度——GUI.Label **不裁剪**，长名字会直接压在负重数字上
    /// （实测「Backpack · Melvin McCampbell」盖住 44.2/60）。策略：先缩字号（12→9），仍放不下再
    /// 去掉「背包 · 」前缀（保留「（阵亡）」这类后缀），最后才省略号截断。结果按 (文本,宽度) 缓存。</summary>
    private static void FitTitle(PackWindow w, float maxW)
    {
        if (maxW < 8f) maxW = 8f;
        if (w.titleDisplay.Length > 0 && w.titleFitKey == w.title && Mathf.Abs(w.titleFitW - maxW) < 0.5f) return;
        w.titleFitKey = w.title ?? "";
        w.titleFitW = maxW;
        string full = w.title ?? "";
        string prefix = "";
        try { prefix = Ui.Tr("背包 · "); } catch { }
        string noPrefix = (prefix.Length > 0 && full.StartsWith(prefix)) ? full.Substring(prefix.Length) : full;
        int size = Er2Ui.FontBody;   // 2.5.1：基准字号随倍率（原写死 12）
        string disp = null;
        try
        {
            // 第一轮：完整标题缩字号
            for (int sz = Er2Ui.FontBody; sz >= Er2Ui.FontSmall && disp == null; sz--)
            {
                textStyle.fontSize = sz;
                if (textStyle.CalcSize(new GUIContent(full)).x <= maxW) { disp = full; size = sz; }
            }
            // 第二轮：去掉「背包 · 」前缀后再缩字号（名字本身最长，去掉前缀通常就够）
            if (disp == null && noPrefix != full)
            {
                for (int sz = Er2Ui.FontBody; sz >= Er2Ui.FontSmall && disp == null; sz--)
                {
                    textStyle.fontSize = sz;
                    if (textStyle.CalcSize(new GUIContent(noPrefix)).x <= maxW) { disp = noPrefix; size = sz; }
                }
            }
            // 第三轮：最小号省略号截断
            if (disp == null)
            {
                size = Er2Ui.FontSmall;
                textStyle.fontSize = Er2Ui.FontSmall;
                string b = noPrefix;
                for (int n = b.Length - 1; n > 1; n--)
                {
                    string c = b.Substring(0, n) + "…";
                    if (textStyle.CalcSize(new GUIContent(c)).x <= maxW) { b = c; break; }
                }
                disp = b;
            }
        }
        catch { size = Er2Ui.FontBody; disp = full; }
        finally { textStyle.fontSize = Er2Ui.FontBody; }
        w.titleDisplay = string.IsNullOrEmpty(disp) ? full : disp;
        w.titleSize = size;
    }

    private static void BackpackLabel(Rect r, string text, Color c, GUIStyle st)
    {
        Color keep = GUI.color;
        float off = 1.5f * Er2Ui.Scale;   // 2.5.1：阴影偏移随倍率
        GUI.color = new Color(0f, 0f, 0f, 0.85f);
        GUI.Label(new Rect(r.x + off, r.y + off, r.width, r.height), text, st);
        GUI.color = c;
        GUI.Label(r, text, st);
        GUI.color = keep;
    }

    private static bool GhostBtn(Rect r, string label, Color textC, Color hoverC)
    {
        Event e = Event.current;
        bool hover = e != null && r.Contains(e.mousePosition);
        Color keep = GUI.color;
        GUI.color = hover ? hoverC : new Color(textC.r, textC.g, textC.b, 0.55f);
        GUI.Label(r, label, tipStyle); // 居中小字号（EnsureStyles 保证已建）
        GUI.color = keep;
        if (hover && e != null && e.type == EventType.MouseDown && e.button == 0)
        {
            e.Use();
            return true;
        }
        return false;
    }
}
