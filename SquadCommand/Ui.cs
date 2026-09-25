using System.Collections.Generic;

namespace ER2SquadCommand;

/// <summary>
/// 0.9.13：双语 UI 文案层。调用点一律写中文原串（可含 {0} 占位，用 string.Format 组装），
/// Ui.Tr 负责翻译：默认构建（英文发布版）查表转英文，CN_BUILD 构建原样返回中文。
/// 表缺失的键回退中文——补表即可，不影响运行。
/// </summary>
internal static class Ui
{
#if CN_BUILD
	public static string Tr(string cn) { return cn; }
#else
	private static readonly Dictionary<string, string> En = new Dictionary<string, string>
	{
		// ===== 底栏提示 / 状态 =====
		["WASD 移动    滚轮 缩放    中键 旋转    Q/E 升降    │    左键 选择/框选    右键 指令    长按拖动 阵型    │    Z/X/C 站/蹲/趴    V 停止    B 停火    N 掩体    M 集合    F 分散    │    空格 暂停    ESC 设置"]
			= "WASD Move    Wheel Zoom    MMB Rotate    Q/E Height    │    LMB Select/Box    RMB Orders    Hold-RMB+Drag Formation    │    Z/X/C Stand/Crouch/Prone    V Halt    B Hold Fire    N Cover    M Rally    F Scatter    │    Space Pause    ESC Menu",
		["  高度 "] = "  Alt ",
		["⏸ 已暂停（空格继续）"] = "⏸ Paused (Space to resume)",
		["已选 "] = "Selected: ",
		["步兵 "] = "Infantry ",
		[" + 载具 "] = " + Vehicles ",
		["移动 → "] = "Moving → ",
		[" 已到位"] = " in position",

		// ===== 顶栏按钮 =====
		["控制该小队"] = "Take Command",
		["分队"] = "Split",
		["合并"] = "Merge",

		// ===== 命令环 =====
		["站起"] = "Stand",
		["蹲下"] = "Crouch",
		["趴下"] = "Prone",
		["停止"] = "Halt",
		["掩体"] = "Cover",
		["集合"] = "Rally",
		["停火"] = "Hold Fire",
		["分散"] = "Scatter",

		// ===== 交互环 =====
		["上车"] = "Board",
		["下车"] = "Dismount",
		["修理"] = "Repair",

		// ===== 移动 / 通用反馈 =====
		["上帝视角 ON（框选临时选择，右键指挥，空格暂停）"] = "God View ON (box-select, right-click orders, Space pause)",
		["移动 → 步兵 {0} + 载具 {1}"] = "Move → {0} infantry + {1} vehicles",
		["停止 → {0} 单位"] = "Halt → {0} units",
		["到达 → {0} 单位"] = "Arrived → {0} units",
		["分散 → {0} 队就地找掩护"] = "Scatter → {0} squads seeking cover nearby",
		["已清空选择"] = "Selection cleared",
		["已继续（时间恢复）"] = "Resumed (time running)",
		["已暂停（空格继续）"] = "Paused (Space to resume)",
		["双击需命中友军单位"] = "Double-click must hit a friendly unit",
		["未命中地面"] = "No ground hit",
		["先框选/选中单位"] = "Box-select/choose units first",
		["先框选/选中要指挥的单位"] = "Box-select/choose units first",
		["前往并防守（同一目标）"] = "Move & Defend (same target)",
		["车辆未分队：先点【分队】再移动"] = "Vehicle not split: click [Split] first",

		// ===== 选择 / 编组 =====
		["已选中整车组"] = "Vehicle crew selected",
		["已选中整队 {0} 名步兵"] = "Selected whole squad, {0} infantry",
		["临时指挥：步兵 {0} + 载具 {1}"] = "Temp command: infantry {0} + vehicles {1}",
		["（追加）"] = " (appended)",
		["未框到可选单位（全空或载具无车组）"] = "No selectable units in box (empty or crewless vehicles)",
		["已选中小队 {0} 个单位"] = "Selected squad, {0} units",
		["编组 {0} 已保存（步兵 {1} + 车组 {2}）"] = "Group {0} saved ({1} infantry + {2} crews)",
		["编组 {0}（步兵 {1} + 车组 {2}）"] = "Group {0} ({1} infantry + {2} crews)",
		["编组 {0} 已无存活单位"] = "Group {0} has no living units",
		["不能按键退出：框选/选中单位 → 点顶部 [控制该小队] 接管"] = "Can't exit by key: box-select units → click [Take Command]",
		["请先框选/选中要接管的单位"] = "Box-select/choose units to take over first",

		// ===== 分队 / 合并 =====
		["先选中要分队的步兵"] = "Select infantry to split first",
		["新建小队失败"] = "Failed to create squad",
		["分队失败：无可拆单位"] = "Split failed: nothing to split",
		["分队 → "] = "Split → ",
		[" 人入新队（原队 "] = " into new squad (from ",
		[" 个，其余未动）"] = " squads, rest untouched)",
		["分队 → 车组 "] = "Split → crew of ",
		[" 人入新队（徒步步兵留原队）"] = " to new squad (foot infantry kept)",
		["车组已是独立小队，无可拆分"] = "Crew squad is standalone, nothing to split",
		["合并 → "] = "Merged → ",
		[" 人入"] = " into ",
		["车组"] = "crew squad",
		["步兵队"] = "infantry squad",
		["，已在队 "] = ", already in squad ",
		["，超员留下 "] = ", overflow left ",
		["，{0} 辆车同队"] = ", {0} vehicles in one squad",
		["无可合并（选中单位已在同一小队）"] = "Nothing to merge (already one squad)",
		["选中单位没有所属小队"] = "Selected units have no squad",
		["先选中要合并的步兵"] = "Select infantry to merge first",
		["没有可并入车组的步兵"] = "No infantry to merge into the crew",

		// ===== 集火 / 姿态 =====
		["集火标记 → "] = "Focus-fire mark → ",
		["（持续到死亡/失控）"] = " (until death/lost)",
		["标记目标物 → "] = "Marked prop → ",
		["（持续到失效）"] = " (until destroyed)",
		["标记失败：目标无 Spottable"] = "Mark failed: target has no Spottable",
		["标记失败：载具无目标"] = "Mark failed: vehicle has no target",
		["站起（恢复 AI 姿态） → {0} 单位"] = "Stand up (AI pose restored) → {0} units",
		[" → {0} 单位"] = " → {0} units",

		// ===== 登车 / 载具 =====
		["上车 → {0} 人（原生登车中…）"] = "Board → {0} soldiers (native boarding...)",
		["，余 {0} 人未下令"] = ", {0} left out",
		["登车完成 → 可直接驾驶"] = "Boarded → you can drive now",
		["登车完成"] = "Boarded",
		["载具已满"] = "Vehicle full",
		["已下车 {0} 辆载具"] = "Dismounted {0} vehicles",

		// ===== 进阶命令环 =====
		["无可用步兵小队"] = "No available infantry squads",
		["无可用小队"] = "No available squads",
		["无可用小队（找不到班长）"] = "No available squads (leader not found)",
		["停火 → "] = "Cease fire → ",
		["开火 → "] = "Open fire → ",
		[" 队"] = " squads",

		// ===== 配置描述 =====
		["主开关。"] = "Master switch.",
		["移动到达判定半径（米）；双击右键「前往并防守」的防守半径同用此值。"] = "Move arrival radius (m); also the defend radius of double-right-click Move & Defend.",
		["调试日志开关（发布版保持关闭）。开启后输出全部指挥/登车/标记/阵型诊断日志，用于问题排查。"] = "Debug logging (keep off for release). Enables command/boarding/mark/formation diagnostic logs.",
		["上帝视角开关（仅进入）。退出＝选中小队后点顶部[控制该小队]随机接管一人；全军覆没时按键紧急退出。空格＝暂停/继续世界。"] = "God view toggle key (enter only). Exit = select units → click [Take Command]; emergency exit by key when all are dead. Space = pause/resume.",
		["站起（恢复 AI 姿态）。"] = "Stand up (AI pose restored).",
		["蹲下。"] = "Crouch.",
		["趴下。"] = "Prone.",
		["停止（取消移动/标记，单位原地）。"] = "Halt (cancel move/mark, units stay).",
		["停火/开火切换。"] = "Toggle hold fire / open fire.",
		["就近掩体（以选中中心为准找掩护）。"] = "Seek cover near selection center.",
		["集合（各队向班长集结）。"] = "Rally (each squad regroups on its leader).",
		["分散（各队就地散开找掩护）。"] = "Scatter (squads spread out and seek cover).",
		["阵型拖动中的白色半透明单位预览（克隆失败会自动降级为标记）。"] = "White translucent unit preview while dragging formations (auto-degrades to markers if cloning fails).",

		// ===== 1.4.21 补：Markers 段 cfg 描述（此前表中缺失，EN 构建会回退中文）=====
		["友军脚环（步兵/载具地面圆环）。仅 RTS 视角显示。"] = "Friendly foot rings (ground circles for infantry/vehicles). RTS view only.",
		["选中角标（四角直角括号）。"] = "Selection brackets (four right-angle corners).",
		["集火目标环与名签（任务跨视角持续显示）。"] = "Focus-fire ring and name plate (persists across views).",
		["移动目标点（小圈 + 中心点）。"] = "Move target marker (small ring + center dot).",
		["行进路线与登车连线（虚线）。"] = "Route and boarding lines (dashed).",
		["阵型拖动标记（箭头 + 阵型线 + 落点）。"] = "Formation drag markers (arrow + line + slots).",
		["世界空间名签（集火目标名称，带深色底板）。"] = "World-space name plates (focus target name, dark backing).",
		["选中/目标指示的呼吸脉动效果。关掉为静态（性能略好，画面更稳）。"] = "Breathing pulse on selection/target markers. Off = static (slightly faster, steadier image).",
		["标记整体尺寸倍率（角标/环/目标点半径同乘）。"] = "Global marker size multiplier (applies to bracket/ring/target radius).",
		["标记线宽倍率（在世界空间基准线宽之上再乘）。1 = 默认；嫌细调大、嫌粗调小。"] = "Marker line width multiplier (on top of the world-space base width). 1 = default; raise if too thin, lower if too thick.",
		["标记穿墙显示（不做深度测试）。开启后单位进建筑也能看到标记，但会糊在墙面上。"] = "Draw markers through walls (no depth test). Units inside buildings stay visible, but markers smear on wall surfaces.",
		["标记配色：Mono=灰阶单色（默认，配灰黑 UI；层次靠灰度值而非透明度）/ Semantic=集火红、降级橙（保留语义色）。"] = "Marker palette: Mono = greyscale (default, matches the dark UI; hierarchy by lightness, not alpha) / Semantic = red focus, orange downgraded (keeps semantic colors).",
		["半透明黑 UI（推荐，默认）。面板/列表/按钮走中性黑+半透明，靠明度与描边区分层次；关掉则回退旧版军绿配色。"] = "Translucent black UI (recommended, default). Panels/lists/buttons use neutral black with translucency; hierarchy comes from lightness and outlines. Turn off to fall back to the old military-green palette.",
		["面板不透明度（0.40~1.0）。越低越能透出战场，但面板越容易被地形颜色带偏；1.0 = 完全不透明。"] = "Panel opacity (0.40-1.0). Lower shows more battlefield through the panel, but the panel then picks up the terrain colour; 1.0 = fully opaque.",
		["HUD 按钮底板 / 小队列表行颜色（#RRGGBB 或 #RRGGBBAA）。默认纯黑 72%（与底部提示条一致）。"] = "HUD button fill / squad list row color (#RRGGBB or #RRGGBBAA). Default: solid black at 72% (same as the bottom hint bar).",
		["HUD 按钮悬停/选中颜色。默认中性深灰。"] = "HUD button hover/selection color. Default: neutral dark grey.",
		["HUD 文字/描边颜色。默认近纯白。"] = "HUD text/border color. Default: near-white.",
		["3D 场景标记总开关。关掉后所有世界空间标记（脚环/角标/集火环/目标点/路线/阵型）都不再绘制。"] = "Master switch for 3D scene markers. Off = no world-space markers at all (rings / brackets / focus ring / move target / routes / formation).",
		["自定义光标（RTS 内按指向对象换形状与明度：敌军红、火力点橙，其余灰阶）。"] = "Custom cursor (RTS: shape and lightness follow the pointed object — enemy red, emplacement orange, everything else greyscale).",
		["调试日志开关（发布版保持关闭）。开启后输出全部指挥/登车/标记/阵型/背包/穿戴诊断日志，用于问题排查。"] = "Debug logging (keep off for release). Enables full command/boarding/mark/formation/backpack/equip diagnostic logs.",
		["选中的单位都在载具里，附近也没有可派的徒步友军"] = "All selected units are inside vehicles and no nearby foot allies can be sent",
		["[降级] "] = "[Supp] ",
		["{0} → {1} 单位"] = "{0} → {1} units",
		["修理 → "] = "Repair → ",
		["修理失败: "] = "Repair failed: ",
		["分队 → {0} 人入新队（原队 {1} 个，其余未动）"] = "Split → {0} into new squad (from {1} squads, rest untouched)",
		["分队 → 车组 {0} 人入新队（徒步步兵留原队）"] = "Split → crew of {0} to new squad (foot infantry kept)",
		["合并 → {0} 人入{1}"] = "Merged → {0} into {1}",
		["标记目标物 → {0}（持续到失效）"] = "Marked prop → {0} (until destroyed)",
		["集火标记 → {0}（持续到死亡/失控）"] = "Focus-fire mark → {0} (until death/lost)",

		// ===== 1.2.0+ 阵型 / 信息面板 =====
		["载具转向 → {0}"] = "Face → {0} vehicles",
		["阵型 → 掩体 {0} + 排开 {1} + 载具 {2} + 转向 {3}"] = "Formation → {0} in cover + {1} spread + {2} vehicles + {3} facing",
		["阵型：无可用单位"] = "Formation: no usable units",
		["就近掩体 → {0} 队"] = "Cover nearby → {0} squads",
		["进入建筑 → {0} 人进掩体防守"] = "Enter building → {0} taking cover",
		["下车"] = "Dismount",
		["修理"] = "Repair",
		["无需修理"] = "No repair needed",
		["先选中要修理的载具"] = "Select a vehicle to repair first",
		["修理需选中一支完整步兵小队"] = "Repair needs a complete infantry squad selected",
		["该载具无需修理"] = "This vehicle needs no repair",

		// ===== 1.2.0 左下角信息面板 =====
		["已选 步兵 "] = "Selected: ",
		["未选中单位（左键选择/框选）"] = "No unit selected (LMB click/box)",
		["载具 "] = "Vehicle ",
		[" · 乘员 "] = " · crew ",
		["姿态 "] = "Stance ",
		["压制 "] = "Suppression ",
		["受压制"] = "suppressed",
		["受扰"] = "pinned",
		["无"] = "none",
		["驻守 → "] = "Holding → ",
		[" 已进入掩体"] = " in cover",
		["自定义光标（RTS 内按指向对象变色：友军绿/敌军红/载具青/建筑黄/火力点橙/可交互浅蓝）。"] = "Custom cursor in RTS, tinted by what it points at (friendly green / enemy red / vehicle cyan / building yellow / emplacement orange / interactable light blue).",
		["光标样式：Circle=空心半透明圆（默认）/ Arrow=箭头 / Cross=细线十字。"] = "Cursor style: Circle = hollow translucent ring (default) / Arrow / Cross.",
		["背包"] = "Backpack",
		["武器"] = "Weapons",
		["弹药"] = "Ammo",
		["爆炸物"] = "Explosives",
		["医疗"] = "Medical",
		["装备工具"] = "Gear/Tools",
		["其他"] = "Other",

		["关闭背包"] = "Close bag",
		["背包 · "] = "Backpack · ",
		["可交互载具："] = "Interactable vehicle: ",
		["（空位 "] = " (seats ",
		["）"] = ")",
		["可交互物品："] = "Interactable item: ",
		["已选中可交互载具："] = "Interactable vehicle selected: ",
		["已选中物品："] = "Item selected: ",
		["先选中要指挥的单位（当前选中的是可交互物）"] = "Select units to command first (an interactable is selected)",
		["火力点转向 → {0} 座"] = "Fire position facing → {0}",
		["无可转向的火力点（需车上有乘员）"] = "No turnable fire positions (needs crew)",
		["拖动太短（拉出箭头指定朝向）"] = "Drag too short (pull an arrow to set facing)",
		["（无数据）"] = "(no data)",

		// ===== 1.3.0 格子背包（1.3.2 修订） =====
		["货舱"] = "Cargo",
		["货舱 · "] = "Cargo · ",
		["（阵亡）"] = " (KIA)",
		["装备中"] = "Equipped",
		["装备中的物品只能丢弃到地上"] = "Equipped items can only be dropped to the ground",
		["距离太远（需距锚点 {0}m 内）"] = "Too far (must be within {0}m of the anchor pack)",
		["超出联动半径，已关闭："] = "Out of link radius, closed: ",
		["背包窗口已达上限（{0}）"] = "Max pack windows reached ({0})",
		["找不到该单位的背包数据"] = "No inventory data for this unit",
		["物品已不在原背包"] = "Item no longer in the source pack",
		["超过负重上限，无法放入"] = "Over weight limit, can't place",
		["交换超重，已取消"] = "Swap exceeds weight, canceled",
		["移动失败"] = "Move failed",
		["已丢弃 "] = "Dropped ",
		["已拾取 "] = "Picked up ",
		["拾取失败（超重？）"] = "Pickup failed (overweight?)",
		["已派 {0} 过去（到达后打开背包）"] = "Sent {0} over (backpack opens on arrival)",
		["已派 {0} 前去拾取（到达后捡起）"] = "Sent {0} to fetch it (picks up on arrival)",
		["没有走到目标旁（停在第 {0}m），已取消"] = "Didn't reach the target (stopped at {0}m), canceled",
		["该物品没有可用交互"] = "No interactions available for this item",
		["交互失败"] = "Interaction failed",
		["穿上"] = "Wear",
		["拿起至右手"] = "Take into right hand",
		["地上"] = "Ground",
		[" · 压制 "] = " · Suppression ",
		["丢弃"] = "Discard",
		["脱下"] = "Take off",
		["摘下"] = "Take off",
		["使用"] = "Use",
		["吃"] = "Eat",
		["喝"] = "Drink",
		["补充弹药"] = "Refill ammo",
		["装填"] = "Load",
		["安装配件"] = "Attach accessory",
		["拾起置于右手"] = "Pick up (right hand)",
		["物品已失效"] = "That item is no longer there",
		["打开/关闭焦点单位（步兵背包/载具货舱/尸体）的格子背包窗口。可开多个窗口，拖拽交换物品。"] = "Toggle the grid backpack window of the focused unit (infantry pack / vehicle cargo / corpse). Open several windows and drag items between them.",
		["背包联动半径（米）：第一个打开的背包为锚点，其余背包距锚点超过此值将无法打开/自动关闭。"] = "Pack link radius (m): the first opened pack is the anchor; packs beyond this radius from it can't be opened / are closed automatically.",
	};

	public static string Tr(string cn)
	{
		return En.TryGetValue(cn, out string v) ? v : cn;
	}
#endif
}
