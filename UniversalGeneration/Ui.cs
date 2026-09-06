using System.Collections.Generic;

namespace ER2UniversalGeneration;

/// <summary>
/// 双语 UI 文案层（与宿主 SquadCommand 同款）。调用点一律写中文原串，
/// Ui.Tr 负责翻译：默认构建（英文发布版）查表转英文，CN_BUILD 构建原样返回中文。
/// 表缺失的键回退中文。
/// </summary>
internal static class Ui
{
#if CN_BUILD
	public static string Tr(string cn) { return cn; }
#else
	private static readonly Dictionary<string, string> En = new Dictionary<string, string>
	{
		// ===== 面板 =====
		["通用生成"] = "Universal Generation",
		["我方"] = "Allies",
		["敌方"] = "Enemy",
		["中立"] = "Neutral",
		["收藏"] = "Favs",
		["步兵"] = "Infantry",
		["坦克"] = "Tanks",
		["轮式"] = "Wheeled",
		["飞机"] = "Planes",
		["火炮"] = "Artillery",
		["（无匹配条目）"] = "(no entries)",
		["乘员:专用"] = "Crew: Tanker",
		["乘员:兵班"] = "Crew: Squad",
		["乘员:无"] = "Crew: None",
		["清除"] = "Clear",
		["生成 [G]"] = "Spawn [G]",
		["→ 生成 "] = "→ Spawn ",
		["（"] = " (",
		["）"] = ")",
		["悬停条目查看模型预览"] = "Hover an entry for a model preview",

		// ===== 放置模式 =====
		["选择放置位置…"] = "Pick a placement spot…",
		["放置: "] = "Placing: ",
		["    左键 确认 · Shift 连续 · 右键 取消"] = "    LMB place · Shift repeat · RMB cancel",
		["已取消放置"] = "Placement cancelled",
		["已放置，可继续（右键结束）"] = "Placed — continue (RMB to finish)",
		["此处无法放置（未命中地面）"] = "Cannot place here (no ground hit)",

		// ===== 反馈 =====
		["已生成 "] = "Spawned ",
		["生成失败："] = "Spawn failed: ",
		["已清除 "] = "Removed ",
		[" 个生成物"] = " spawned object(s)",
		["阵营未知——请先进入战斗"] = "Faction unknown — enter a battle first",

		// ===== 配置 =====
		["主开关。关闭后 mod 完全休眠。"] = "Master switch. When off, the mod is fully dormant.",
		["生成面板开关（仅战场指挥官 RTS 上帝视角内有效）。落点模式中再按一次取消放置。"]
			= "Toggle the spawn panel (only inside Battlefield Commander's RTS god view). Press again while placing to cancel.",
		["调试日志开关（发布版保持关闭）。输出枚举/反射/生成诊断。"]
			= "Debug logging (keep off in releases). Enumeration/reflection/spawn diagnostics.",
		["收藏的生成条目（自动维护，勿手改）。"] = "Favorited spawn entries (maintained automatically).",
	};

	public static string Tr(string cn)
	{
		return En.TryGetValue(cn, out string v) ? v : cn;
	}
#endif
}
