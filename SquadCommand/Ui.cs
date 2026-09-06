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
		["WASD 移动    滚轮 缩放    中键 旋转    Q/E 升降    │    左键 选择/框选    右键 指令    长按空地 命令环    载具长按拖动 朝向    │    空格 暂停    ESC 设置"]
			= "WASD Move    Wheel Zoom    MMB Rotate    Q/E Height    │    LMB Select/Box    RMB Orders    Hold-RMB Ring    Vehicle Hold-Drag Facing    │    Space Pause    ESC Menu",
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
		["车辆未分队：先点【分队】再移动"] = "Vehicle not split: click [Split] first",

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
		["调试日志开关（发布版保持关闭）。开启后输出全部指挥/登车/标记诊断日志，用于问题排查。"] = "Debug logging (keep off for release). Enables command/boarding/mark diagnostic logs.",
		["上帝视角开关（仅进入）。退出＝选中小队后点顶部[控制该小队]随机接管一人；全军覆没时按键紧急退出。空格＝暂停/继续世界。"] = "God view toggle key (enter only). Exit = select units → click [Take Command]; emergency exit by key when all are dead. Space = pause/resume.",
		["UI 主色（#RRGGBB 或 #RRGGBBAA）：按钮底板、小队列表行。默认深绿半透明（与底部提示条一致）。"] = "UI base color (#RRGGBB(AA)): button fill, squad list rows. Default translucent dark green (matches bottom bar).",
		["UI 悬停/选中指示颜色（中绿）。"] = "UI hover/selection color.",
		["UI 文字/描边颜色。"] = "UI text/border color.",
		["[降级] "] = "[Supp] ",
		["{0} → {1} 单位"] = "{0} → {1} units",
		["修理 → "] = "Repair → ",
		["修理失败: "] = "Repair failed: ",
		["分队 → {0} 人入新队（原队 {1} 个，其余未动）"] = "Split → {0} into new squad (from {1} squads, rest untouched)",
		["分队 → 车组 {0} 人入新队（徒步步兵留原队）"] = "Split → crew of {0} to new squad (foot infantry kept)",
		["合并 → {0} 人入{1}"] = "Merged → {0} into {1}",
		["标记目标物 → {0}（持续到失效）"] = "Marked prop → {0} (until destroyed)",
		["集火标记 → {0}（持续到死亡/失控）"] = "Focus-fire mark → {0} (until death/lost)",

		// ===== 1.0.2+ 载具朝向拖动 =====
		["载具转向 → {0}"] = "Face → {0} vehicles",
		["无可转向载具（需有驾驶员的非飞机载具）"] = "No steerable vehicles (crewed, non-air)",
		["载具朝向拖动（地狱之门式）：选中载具后长按右键并拖动出箭头，松开车体原地转向。关闭后长按右键仅开命令环。"]
			= "Vehicle facing drag (Gates of Hell style): select vehicles, hold RMB and drag an arrow, release to pivot in place. Off = hold RMB opens the command ring only.",
	};

	public static string Tr(string cn)
	{
		return En.TryGetValue(cn, out string v) ? v : cn;
	}
#endif
}
