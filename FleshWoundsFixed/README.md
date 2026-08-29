# FleshWoundsFixed（ER2 Flesh Wounds 1.0.1 重建修复版）

> 2026-08-20 由部署版反编译重建。**这是第三方 mod（Nexus [Easy Red 2 mods/59](https://www.nexusmods.com/easyred2/mods/59) Flesh Wounds - BloodWorks）的修复副本**，不是工作区原创 mod。

## 为什么存在

游戏 2.1.0 更新后，战斗中会随机出现"单位贴图消失变紫"（洋红）并持续到本局结束。定位结论：

- 原版 Flesh Wounds 命中士兵时**克隆士兵材质 + 新建伤口贴图**替换全身 SMR；
- 玩家身体**不受 LRU 保护**（`_soldierHasPlayer` 只在"玩家开枪命中别人"时置位；玩家挨打走 AI 路径不置位）→ 战斗中贴图预算（用户配 64 张）满 → 玩家身体/尸体被 `EvictSoldier` 淘汰 → **贴图被 `Destroy` 而渲染器（FPS 手/TPS 模型/尸体）仍引用 → 洋红**；
- 原版 `EvictSoldier` 只按缓存 SMR 列表还原（缓存过期漏掉后创建的渲染器），`ClearAllWounds`（切场景）甚至**不还原直接销毁**。

## 修复内容（v1.0.0 → v1.0.1）

1. `RestoreWoundMaterials()`：销毁前对目标士兵**缓存 + 现场重扫**全部 SMR，把引用克隆材质的槽还原为原始材质（FPS 手/制服 LOD 重建后也不会漏）。
2. `EvictSoldier`（LRU 淘汰/士兵销毁）：先 `RestoreWoundMaterials` 再入 `_pendingDestroy` 延迟销毁。
3. `ClearAllWounds`（切场景）：先按根节点全部还原，再销毁。
4. `RootHasPlayer()`：命中绘制时若目标根含玩家控制的士兵 → 纳入 `_soldierHasPlayer` LRU 保护（玩家身体不再被淘汰）。
5. 其余逻辑与原版 1:1（方法集 55→57，仅新增上述 2 个助手；复杂方法体归一化对比 SplatBrush/DrainAiQueue/GetTopo 逐行一致，RaycastBest 仅 ILSpy 局部变量渲染噪声）。

## 构建与部署

- `dotnet build -c Release FleshWoundsFixed.csproj`（AssemblyName = ER2_FleshWoundsBW）
- 部署到 `游戏\BepInEx\plugins\ER2_FleshWoundsBW\ER2_FleshWoundsBW.dll`（保留血刷 png）
- GUID `ER2_FleshWounds` 不变 → 配置 `BepInEx\config\ER2_FleshWounds.cfg` 与 TRCompat 桥接自动兼容

## 注意事项

- 反编译重建存在 ILSpy 伪代码人工修正点（`(ref x)` cast 残留、`..ctor` 伪代码、`op_Implicit` 显式调用、`ref flag`→`out flag` 等），修完后与原件反编译做过逐方法核对。
- 若游戏后续大版本更新，先重新核对 `VerifiedGameVersion`（csproj 无此项，在 Plugin.cs 的 `[assembly: AssemblyMetadata("VerifiedGameVersion", "921")]`）与 SoldierLodManager 钩子（启动日志 `[BLOOD] SoldierLodManager: 4/4 material hooks active`）。
