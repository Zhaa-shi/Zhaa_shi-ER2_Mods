# Easy Red 2 场景物体分类体系（解包实证）

> 2026-08-18 解包产出。来源：`CorvoBundles\*.manifest`（资源路径全集）+ interop 反编译
> （`PropData` / `MapPropManager.MapProp*` / `ItemsDatabase` / `ItemObject` 体系）。
> 用途：任何需要"识别场景里出现的东西并分类"的 mod（环境破坏、物理、标记等）的权威依据。

## 一、资源包层分类（manifest 目录结构）

游戏资源按 bundle 分装，bundle 内的**文件夹 = 第一层分类**：

| bundle | 顶层文件夹 | 数量 | 内容 |
|---|---|---|---|
| **er2bundle** | `Props and Buildings/` | 1337 | 环境/建筑/家具/道路/工事/设施等全部场景静态物 |
| | `Utility/` | 6 | 门窗等交互件 |
| | `VFXs/` | 3 | 特效 |
| **er2items** | `Uniforms/` | 760 | 军服/头盔/背心/装具（按国家分 26 国子目录） |
| | `Items/` | 338 | 武器/弹药/弹匣/附件/医疗/工具/载具武器 |
| | `grenades/` | 48 | 手雷/反坦克雷/烟雾/炸药/燃烧瓶（ThrowableWheel 全集来源） |
| **er2vehicles** | `Vehicles/` | 380 | 载具 prefab（坦克/卡车/飞机/船只/拖车…） |
| **er2units** | `Factions/` | 19 | 阵营定义（Germany/UnitedStates/…/Civilian） |
| **er2battles** | `Battles/` | 5 | 战役定义 |
| **er2maps_placedprops** | `Scenes/` | 20 | 每张地图的"已放置物"数据（见 §三） |

`Props and Buildings/` 内命名可再细分（经验命名法）：`Road*`道路、`Trench*`战壕、`bunker*`碉堡、
`*Wall*`墙、`Sandbags*`沙袋、`*House*/Barn*`房屋、`GrenadeBox*_Refill`弹药箱、`StreetLight*`路灯、
`Crater*`弹坑、`Rubble*`瓦砾、`sign_*`路牌、`*Spawner`生成点、`*_LOD*`多级细节、`*_DST`破坏态。

## 二、官方分类枚举（PropData）

`PropData`（物品/物件注册表条目）内嵌两个分类枚举——**这是游戏自己的权威分类**：

```csharp
public enum PropType {          // 13 类：物件的大类
    environment=0, terrain=1, streets=2, buildings=3, props=4,
    vehicles=5, items=6, weapons=7, ammo=8, attachment=9,
    roadMaterial=10, vfx=11, deprecated=99
}
public enum Category {          // 12 类：装备/载具用途细分
    Unknown, Headgear, Vest, Uniform, Wheeled, Tank, Plane,
    AutoTransport, StaticMg, StaticGun, Special, Unlisted
}
```

`PropData` 字段（每个注册物件一条）：

| 字段 | 含义 |
|---|---|
| `prefab_name` / `name` / `translation_id` | 预制名/显示名/翻译键 |
| `propType` / `category` | **上述两个枚举**（主分类 + 用途分类） |
| `assetBundleName` / `settedAssetBundleFolder` | 资源定位（mod 可借此拿到加载路径） |
| `spawn_distance` | 生成距离（LOD/剔除用） |
| `dlc` / `deprecated` / `mod_id` / `shiftAdjust` | DLC 归属/废弃标记/作者/落点修正 |

## 三、地图放置物运行时体系（MapPropManager）

地图里的静态物不是裸 prefab，而是三层结构：

```
MapProp（作者数据）            MapPropReference（运行时实例）        PropData（注册表分类）
├─ prop_id                    ├─ connected_map_prop ────────────► MapProp
├─ position/rotation/scale    ├─ connected_prop_data ───────────► PropData（§二）
└─ destructiblePhases(sbyte[])├─ prop_instance (GameObject)
                              ├─ destructionManager (DestructibleManager) ← 原生破坏系统
                              ├─ instance_life (float[])  每可破坏子件血量
                              ├─ override_destruction(sbyte[])
                              ├─ colliders / colliders_enabled / renderMode
                              ├─ doorStatus (bool[])       门状态
                              └─ SetSynchedLife/IsDestroyedForever/RefreshActualLife
```

要点：
- **原生可破坏物 = 有 `DestructibleManager` + `instance_life` + `destructiblePhases` 的 MapPropReference**
  ——这就是"大场景破坏走原生动画"的权威通道（相位推进 + 特效 + 血量），mod 不应绕过。
- 门的开关状态由 `doorStatus` 数组管理（ModManager/HideAnything 类可读写）。
- LOD/剔除：`spawnDistance` + `LOD_DISTANCE_MULT` + `RenderMode`（这也是"家具带 `_LOD0/1` 兄弟"的来源）。

## 四、物品体系（ItemsDatabase + ItemObject 类层级）

`ItemsDatabase` 是物品注册表（`Loaded`/`GetItemObject(id)`/`GetSpecificItemClass<T>`/
`GetAllItemsOfType<T>(PropType)`——**PropType 就是 items/weapons/ammo/attachment 的过滤键**）。

运行时物品 = ItemObject 派生（世界物体）与 VirtualItem 派生（背包/虚拟物品）两套：

```
ItemObject（世界物基类，spawnedItems 静态表）
├─ ItemObjectStackable → ItemObjectRecoverLife（食物/药品）
├─ Weapon → HandheldItem → GenericGun（枪械；含 ammoSpace/magazine/弹匣系统）
│    └─ Panzerfaust（一次性）
├─ Magazine（弹匣）、Attachment（配件）
├─ ItemGrenade（手雷世界物：fuseType/explosionDamage/explosionRadius）
├─ DroppableAmmoBox / AmmoBox / AmmoRefillCrate（补给箱）
├─ ItemClothing / ItemHelmet（服装头盔）
├─ ItemObjectBandages / ItemObjectSyringe / ItemObjectToolbox
├─ ItemObjectDeployableVehicle（可部署载具）
└─ TurretWeaponBullet / VirtualAmmo / VirtualShellAmmo（炮塔弹药数据）

VirtualItem（背包/虚拟）
├─ VirtualWeapon / VirtualAmmo / VirtualShellAmmo / VirtualMagazineItem
└─ VirtualThrowable → VirtualGrenade / VirtualSmokeGrenade / VirtualATGrenade
```

**运行时识别物品类型**：`go.GetComponentInParent<ItemObject>()` 拿到子类即知用途；
`ItemsDatabase.GetPropData(id)` 类接口可查 PropType（items/weapons/ammo/attachment）。

## 五、生物与载具（另一层分类）

- 士兵：`Soldier : Creature`（`Creature.allCreatures/aliveCreatures`），`faction` 字符串
  （`UnitedStates_allies`/`Germany_axis` 等，26 国 × 阵营），`IsPlayer()` 引用级判定；
  装备槽/背包 `inventory`（InventoryManager，`items` 为 `List<VirtualItem>`）。
- 载具：`Vehicle`（`allVehicles`）→ `VehicleWithWheels → VehicleTank` / `MovableVehicle → VehiclePlane`；
  `IsStatic/IsArtillery/IsAA/IsTransportVehicle/IsSPA` 快速判定；炮塔 `Turret → TurretGun → TurretMG`
  （固定火力点是 TurretMG），炮塔武器 `TurretWeapon`（loadedAmmoCount/maxAmmoCount/ammos[].bullet_id）。
- 可破坏建筑：`DestructableBuilding`（life/Damage(float)/DestroyBuilding(bool)/SetDestructionPhase）。

## 六、给 mod 的运行时分类决策树（拿一个 GameObject 怎么归类）

```
1. GetComponentInParent<Creature> 有 → 生物（Soldier → faction / IsPlayer）
2. GetComponentInParent<Vehicle>  有 → 载具（IsTank/IsStatic/… + Turret 体系）
3. GetComponentInParent<ItemObject>有 → 物品（子类=用途；PropData.propType=weapons/ammo/items…）
4. 命中 MapPropReference → connected_prop_data.propType/category（权威！）
   ├─ propType=buildings/props + destructionManager≠null → 原生可破坏（走原生动画）
   ├─ propType=environment/streets/roadMaterial → 地形道路（默认不可动）
   └─ propType=props（家具等）+ 无 destructionManager → "普通场景物"（名字启发式兜底）
5. 兜底：名称启发式（Road*/Trench*/Sandbags*/table/… 前缀）+ Collider/Renderer 存在性
```

## 七、对"环境破坏类"mod 的启示（MorePhysics 19 版迭代的教训对照）

1. **分类权威在 PropData/MapPropReference**：mod 判断"这是不是可破坏物"应查
   `MapPropReference.destructionManager`/`instance_life`，而不是名字猜。
2. **原生可破坏物（destructiblePhases 非空）一律走原生**：`DestructibleManager` 相位推进自带
   动画/特效/联网同步，mod 覆盖 = 与原生打架（虚影/血量覆盖/同步错乱）。
3. **普通 props（家具等）才是 mod 的空间**，且需处理：LOD 兄弟节点（`*_LOD0` 与兄弟同名）、
   静态批处理（isStatic 级联）、物品 kinematic 刚体（EnablePhysic 产物）、门 doorStatus。
4. **威力分级**：`Explosion.CreateExplosion` 的 `explosionMaxDamage` 即爆炸威力值，
   与 `PropData` 分类/`instance_life` 结合可做"手雷不坏沙袋、炸药可"式分级。
