# Universal Generation（通用生成）

在战场指挥官的 RTS 上帝视角内，按你的意愿生成任何单位与载具。基于游戏原生生成管线的沙盒/作弊工具——无平衡惩罚、无魔改单位，所有生成物与游戏自己生成的一模一样。

## Description（简介）

Universal Generation 在战场指挥官的 RTS 上帝视角内加入一个生成面板。选阵营、选条目、点战场——单位即刻落地，功能完整、可直接指挥。

- **步兵**：约 50 种官方小队类型（步枪班、陆战队、游骑兵、空降兵、党卫军、突击班……），每场战斗从游戏自带小队数据库自动探测，全员满配装备。
- **载具**：游戏本体 + DLC + 内容 mod 的全部坦克、卡车、飞机与火炮——运行时自动枚举（390+ 条目），走游戏原生载具生成器。
- **阵营**：我方 / 敌方 / 中立（平民）。敌方会打你，中立按平民行为。
- **载具自带正式乘员**：按国别的坦克组员、精确按座位数、完整原生登车流程——生成的坦克即刻可被战场指挥官的 RTS 指令指挥。
- **不乱跑**：生成单位原地驻守、见敌接战，永远服从你的 RTS 指令。
- **收藏**：常用条目一键加星，跨会话保存。
- **一键清除**：随时移除本 mod 生成的所有单位。

独立 BepInEx 插件，仅在战场指挥官的 RTS 上帝视角内激活，其余场合完全休眠。

## Installation instructions（安装说明）

1. 安装 [BepInEx](https://bepinex.dev)（IL2CPP）与 **ER2 Battlefield Commander v1.0.1+**
2. 解压 `ER2_UniversalGeneration.dll` 到 `<游戏>/BepInEx/plugins/`
3. 进入战斗 → F9 进上帝视角 → 按 G

## Main features（主要特性）

- 三击出车：`G` → 点条目 → 点战场
- 全量载具目录（本体+DLC+mod），分类页签 + 收藏
- 约 50 种官方小队类型，每场战斗自动校验
- 我方 / 敌方 / 中立阵营生成
- 带乘员载具：国别坦克组员、精确座位数、原生可指挥
- 乘员自定义：专用坦克组员 / 任意步兵班型 / 空车
- 原地驻守行为：见敌就打、不乱跑、听指挥
- Shift+左键 连续放置
- 一键清除全部生成物
- 中英双语界面

## Requirements（需求）

- Easy Red 2（BepInEx IL2CPP）
- [ER2 Battlefield Commander](https://www.nexusmods.com/easyred2/mods/…) v1.0.1 或更新（必需——本 mod 仅在其 RTS 视角内工作）

## Shout outs（致谢）

- **Corvostudio** —— Easy Red 2 与欢迎 modding 的引擎
- **BepInEx / Il2CppInterop / Harmony** 团队
- 战场指挥官 mod——本工具栖息于其上帝视角
