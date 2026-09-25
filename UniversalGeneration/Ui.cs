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
		["通用生成：拖到单位身上放入背包，拖到地上则生成实体"] = "Universal Generation: drag onto a unit to put it in their backpack, or onto the ground to spawn it",
		["◀ 收藏"] = "< Favourites",
		["（还没有收藏：点条目右侧的 ☆ 添加）"] = "(No favourites yet - click the ☆ on the right of an entry to add)",
		["半透明黑 UI（推荐，默认）。面板/列表/按钮走中性黑+半透明，靠明度与描边区分层次；关掉则回退旧版军绿配色。"] = "Translucent black UI (recommended, default). Panels/lists/buttons use neutral black with translucency; hierarchy comes from lightness and outlines. Turn off to fall back to the old military-green palette.",
		["面板不透明度（0.55~1.0）。越低越能透出战场，但面板越容易被地形颜色带偏；1.0 = 完全不透明。"] = "Panel opacity (0.55-1.0). Lower shows more battlefield through the panel, but the panel then picks up the terrain colour; 1.0 = fully opaque.",
		["点击条目拿起 → 拖到单位身上放入背包，拖到地上则生成实体"] = "Click to pick up - drop on a unit (backpack) or the ground (spawn)",
		["生成的我方/敌方单位不主动攻击中立（Civilian）阵营（被打仍会还手；玩家手动标记的目标照打）。"] = "Spawned friendly/enemy units do not attack the neutral (Civilian) faction on their own (they still fight back if attacked; manually marked targets are engaged as usual).",
		["生成的敌方单位走原生 AI（主动推进、随战役任务进攻）。关闭后敌方与我方单位一样原地驻守、只接战不移动。"] = "Spawned enemy units use native AI (they advance and attack with the battle objective). Off = enemies hold position like your own units and only fight when engaged.",
		["生成面板开关（仅战场指挥官 RTS 上帝视角内有效）。落点模式/携带物品中再按一次取消。"] = "Panel toggle (only works inside the Battlefield Commander RTS god view). Pressing again while placing or carrying cancels.",
		["调试日志开关（发布版保持关闭）。输出枚举/反射/生成诊断。"] = "Debug logging (keep off for release). Emits enumeration/reflection/spawn diagnostics.",
		["我方"] = "Allies",
		["敌方"] = "Enemy",
		["中立"] = "Neutral",
		["收藏"] = "Favs",
		["步兵"] = "Infantry",
		["机枪"] = "MGs",
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

		// ===== 2.1.0：收藏分类 + 子分类 + 过滤 =====
		["全部"] = "All",
		["步枪"] = "Rifles",
		["手枪"] = "Pistols",
		["可穿戴"] = "Wearable",
		["搜索…"] = "Search…",
		["清空"] = "Clear",

		// ===== 2.2.0：第三方（mod）内容 =====
		["Mod载具"] = "Mod Vehicles",
		["Mod物品"] = "Mod Items",

		// ===== 物品页签（2.0.0）=====
		["武器"] = "Weapons",
		["弹药"] = "Ammo",
		["投掷物"] = "Throwables",
		["装备"] = "Gear",
		["医疗食物"] = "Medical/Food",
		["服装"] = "Uniforms",
		["其他"] = "Misc",
		["点击条目拿起 → 拖到单位身上放入背包，拖到地上则生成实体"]
			= "Click to pick up → drop on a unit for their backpack, or on the ground to spawn it",
		["携带 "] = "Carrying ",
		["：拖到单位身上放背包，拖到地上丢弃"] = ": drop on a unit for their backpack, or on the ground",
		["携带: "] = "Carrying: ",
		["松手 → 放入 "] = "Release → put into ",
		[" 的背包"] = "'s backpack",
		["松手 → 丢到地上"] = "Release → drop on the ground",
		["对准单位或地面"] = "aim at a unit or the ground",
		["    右键 取消"] = "    RMB cancel",
		["此处无法放置（对准单位或地面）"] = "Cannot place here (aim at a unit or the ground)",
		["已放入 "] = "Put ",
		[" → "] = " → ",
		["已丢下 "] = "Dropped ",
		["单位"] = "unit",
		["该单位没有可用的背包"] = "That unit has no usable backpack",
		["物品无效："] = "Invalid item: ",
		["放入失败："] = "Failed to place: ",
		["放入失败（未生效）："] = "Failed to place (no effect): ",
		["负重已满（"] = "Overweight (",
		["kg）"] = " kg)",

		// ===== 放置模式 =====
		["选择放置位置…"] = "Pick a placement spot…",
		["放置: "] = "Placing: ",
		["    左键 放置 · 左键长按拖动 旋转朝向 · Shift 连续 · 右键 取消"] = "    LMB place · hold-drag LMB to rotate · Shift repeat · RMB cancel",
		["旋转朝向：左右拖动（松手完成）"] = "Rotate facing: drag left/right (release to finish)",
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
		// 2.2.2：**键必须与代码里的串逐字一致**——此前字典里是旧版措辞，代码已改 → 查表落空 → 回退中文
		//（用户实测"英文版为什么还有中文"就是这条配置说明）。新增 `Tr` 缺失自检，漂移会直接进日志。
		["生成面板开关（仅战场指挥官 RTS 上帝视角内有效）。落点模式/携带物品中再按一次取消。"]
			= "Toggle the spawn panel (only inside Battlefield Commander's RTS god view). Press again while placing or carrying to cancel.",
		["生成的我方/敌方单位不主动攻击中立（Civilian）阵营（被打仍会还手；玩家手动标记的目标照打）。"]
			= "Spawned ally/enemy units do not proactively engage the neutral (Civilian) faction (they still fight back when attacked, and units you manually mark are always attacked).",
		["生成的敌方单位走原生 AI（主动推进、随战役任务进攻）。关闭后敌方与我方单位一样原地驻守、只接战不移动。"]
			= "Spawned ENEMY units use their native AI (they advance and attack on their own, following battle tasks). Turn off to make them hold position like allied spawns (engage only, never move).",
		["调试日志开关（发布版保持关闭）。输出枚举/反射/生成诊断。"]
			= "Debug logging (keep off in releases). Enumeration/reflection/spawn diagnostics.",
		["收藏的生成条目（自动维护，勿手改）。"] = "Favorited spawn entries (maintained automatically).",
		// ===== 物品（2.0.0）=====
		["物品生成失败："] = "Item spawn failed: ",
	};

#if !CN_BUILD
	private static readonly HashSet<string> missedKeys = new HashSet<string>();
#endif

	public static string Tr(string cn)
	{
		if (En.TryGetValue(cn, out string v)) return v;

		// 2.2.2：词条缺失自检。字典键与代码串一旦漂移（改了措辞没改字典），
		// 英文版会静默回退中文——用户正是这么发现的。现在开启调试日志时直接点名是哪一条。
		try
		{
			if (missedKeys.Add(cn) && Plugin.debugLog != null && Plugin.debugLog.Value)
				Plugin.ModLog?.LogWarning("[UniGen] 英文词条缺失（回退中文）: " + cn);
		}
		catch { }
		return cn;
	}
#endif
}
