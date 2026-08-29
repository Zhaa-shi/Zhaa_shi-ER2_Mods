# Easy Red 2 — 载具 / 武器 / 音频 / 杂项系统细节报告（利于 mod 开发）

> 反编译源：`Assembly-CSharp.dll`（interop），结果见 `research_out\code\` 与 `code2\`。
> 说明：interop 层把每个成员（含 private）都暴露为 `public unsafe` 包装；下文"公开 API"指具备 mod 可调用价值的成员。

## 一、载具系统

### 1. Vehicle（基类，MonoBehaviour）
```csharp
public class Vehicle : MonoBehaviour
```
- 静态注册表：`public static List<Vehicle> allVehicles`（可遍历，等价于 `Creature.allCreatures` 的载具版）。
- **血量**：`public ProtectedShort life_total` + `public short Maxlife` => `_Maxlife_k__BackingField`。注意和 Soldier 一样是 `ProtectedShort`，必须当作只读/整体读，掉血走游戏原生路径避免被同步覆盖。
- **座位**：`public Il2CppReferenceArray<Seats> seats`（每元素一个座位），`public int peopleInside`。
- **伤害部件**：`public List<VehicleDamagablePart> damageableParts` 与 `public Il2CppReferenceArray<VehicleDamagableDetachablePart> detachableParts`。
- **库存**：`public InventoryManager inventory`（载具自带弹药/物资库）。
- 状态：`locked`、`physicsDisabled`、`vehicleDisabled`、`maxLod/lodDistance`、`rotationSpeed`、`tpsCamDist`、`param_version`、`name_id`。
- 常用 API：
  - 乘员/座位：`GetOnVehicle(int pos, Soldier)`、`GetOnVehicleBestPos`、`GetOffVehicle`、`EjectAll`、`GetDriverSeat`、`HasDriver`、`GetDriver`、`SittedSoldiers()`、`GetCommanderSeatID`、`SeatIsTurret(int)`。
  - 炮塔：`GetTurret(int)`、`GetMainTurret(...)`、`GetMainTurretSeat(...)`、`AllTurrets()`、`AllTurretGuns()`、`DisableAllTurrets()`、`RefillAllTurrets()`、`EmptyAllTurrets()`。
  - 损伤：`Damage(float dam)`、`DamageHull(short)`、`SetDamage(short, bool)`、`GetDamage()`、`Repair()`、`BurnDown(bool)`、`IsDisabled()`、`CanBeRepaired()`、`IsOperative()`、`PlayImpactSound()`。
  - 阵营：`SetFaction(string)` / `GetVehicleFaction()`；移动：`Move(Vector2)`、`Brake()`、`SetMoveDir`、`GetCurrentSpeed`、`GetCurrentRotY`。
  - 判定：`IsVehicle()`、`IsAirVehicle()`、`IsArtillery()`、`IsAA()`、`IsStatic()`、`IsLoud()`、`IsTransportVehicle()`、`IsSPA()`、`CalculatePeopleInside()`、`PlayerInside()`。
- 继承链：`Vehicle > VehicleWithWheels > VehicleTank`，`Vehicle > MovableVehicle > VehiclePlane`。（`MovableVehicle` 为绝大多数车/坦克/卡车的通用基类。）

### 2. VehicleTank（继承 VehicleWithWheels）
- 仅薄封装：新增一个 `float curRotationSpeed`；重写转向/轮速/引擎音相关虚方法（`EffectiveSteer`、`RefreshWheelsRPM`、`UpdateMotorSound`、`GetEngineSoundMult`）。**炮塔/装甲并不在 VehicleTank 上**，而是挂在 `Turret` / `VehicleDamagablePart` 上 —— 装甲细节见 §1.6。

### 3. VehiclePlane（继承 MovableVehicle）
- 大量飞行物理字段（`thrustForce`、`liftMult`、`aerodynamicity`、`throttle`、`r_yaw/pitch/roll`、`isGrounded`、`centerOfMass` 等）。
- 炸弹舱：`CountBombs()`、`DropBombs()`、`RefillBombBay()`、`EmptyBombBay()`、`BombBayEmpty()`、`CanRefillBombBay()`、`PredictBombImpactPosition()`。
- 飞行解体：`DamageAllCrew(float)`、`Explode()`、`GetExplosionOnImpactSpeedKmh(part)`、`PartIsPropeller/Tail`、`HasAtLeastAPropeller()`、`HasAllTails()`。
- 起落架：`OpenGear/CloseGear/SetGearClosed`。

### 4. VehicleSeat 与 Seats（座位模型）
- `VehicleSeat`（MonoBehaviour，物理座位/动画挂点）：`string onSeatAnimator_id`、`bool canCrouch`。
- `Seats`（`[Serializable]`，纯数据，逻辑座位）字段包含：
  ```csharp
  Soldier unitSet; bool showUnitSet; Vehicle seatVehicle;
  float reloadEndTime; int seatID; bool isDriver; bool forceHideWeaponWhenHere;
  Turret connectedTurret; // 座位→炮塔 单向关联
  float maxLookY; Vector3 seatHandsPositions; Vector3 visionPort;
  ```
- 方法：`hasTurret`、`hasUnitSet`、`OnPlayerEnterSeat()`、`OnPlayerLeaveSeat()`、`GetSeatIndexInVehicle()`、`ShowUnit(Soldier)`、`HasPeriscope()`、`GetFPSCameraRotationLimit()` 等。
- **关联**：一个 Vehicle 持 `seats[]`；个把座位经 `Seats.connectedTurret` 指向 `Turret`（即某座位是炮手位）。`Turret.turretSeatIdOnvehicle` 反向标识炮塔座位号。改座位→乘员/炮塔绑定，改这两个字段即可，但要遵循游戏进出车流程以免同步冲突。

### 5. Turret / TurretGun / TurretWeapon（炮塔火力系统）
- `Turret : MonoBehaviour` 是炮塔基座（旋转/瞄准/镜）：
  - 轴限位：`xAxisBond`、`xAxisBondDown`、`yAxisBond`；`CanShoot360`、`YandXaxisInverted`、`isAA`、`minimumNightVision`；
  - 瞄准：`ManualRotate(lr, ud)`、`LookDir(Vector3)`、`CanShootInDirection(dir, rangeMult)`、`GetTurretRotationSpeed()`、`zeroingAngle`、`scopeFOV`、`scopeSprite_id`；
  - 座员：`GetSittedUnit()`、`GetSittedUnit(Vehicle)`、`GetSeat()`、`turretSeatIdOnvehicle`。
- `TurretGun : Turret` 是具体机枪/炮（**炮手控制与装填的核心**）：
  - 弹药座位：`short selectedWeapon_id`、`short selectedAmmo_id`、`LoadedAmmo_id`、`SelectedAmmoIdAsFlatArray`、`bool combinedFireActive`、`bool triggerPressed / pressTriggerRequest`、`lastNetUser`；
  - 持续/循环射击：`IsFiringCycled`、`_IsFiringCycled_k__BackingField`、`ExtractOneBullet(TurretWeapon)`、`GetCurrentAmmoCount()`、`HasAmmo()`、`AnyCombinedWeaponReady()`；
  - 自动/低射速：`IsAutomaticFire(TurretWeapon)`、`IsLowFireRate()`、`GetBarrelsFireDelay(weapon)`；
  - 弹药抽取/装填：`ScanUnitInventoryForAmmos(Soldier)`、`SelectAmmo(int wid)`；
  - **玩家与 AI 共用同一套 API**，无需区分：玩家触发 `triggerPressed`，AI 走 `TurretAI` 调 `GetCenterFirePosition()`/射击。因此 mod 可"代开炮"：找到 `TurretGun` 设 `triggerPressed=true` 或以 `ExtractOneBullet`+射击方法模拟。
  - 装填监听挂 `next_ammo_check_time`/`reloadEndTime`/`selectedWeapon_id`。
- `TurretWeapon : Il2CppSystem.Object` 是炮塔武器槽（配置 + 弹药计数）：
  - `short loadedAmmo_id / loadedAmmoCount / maxAmmoCount / tracerCount`；`float nextReloadEnd / nextAvailableShot`；
  - `int roundPerMinute`、`float reloadTime / delayBetweenShots / dispersionAngle / recoilIntensity`、`bool isRecoilless / reloadWhenFiring / preventAimWhenReloading`；
  - 特效：`string fireFx_id / fireFxContinuous_id / fireFx_bundle`、`float fireSoundVolume`；
  - 方法：`SetLoadedAmmo`、`SetAmmoCount`、`ReloadIfNecessary`、`Reload(...)`、`Unload(Inventory)`、`IsReloaded/IsReloading`、`GetReloadValue()`、`ExtractBullet`、`PlayFireFx/StopFireFx`。

### 6. VehicleDamagablePart / VehicleDamageDetails（装甲与伤害）
- `VehicleDamagablePart : Interagible` 是载具可损部件（车体/装甲/部件）：
  - 装甲字段：`float armorThickness`、`bool considerAngledArmor`、`bool canRicochet`、`bool damagePlayerOnCollision`、`bool damageHullOnGettingRammed`、`ImpactType impactType`、`Vehicle rootVeh`。
  - 命中/穿透 API：`bool HitPart(damage, penetration, point, fromFaction, HitType)`、`bool TryPenetrateArmor(BulletData, firePos, impactPos, impactDir, angle, shooter, overridePenetrationDamage=-1)`、`int GetPenetrationAtAngle(float)`、`void TryDamageWithExplosion(maxPen, explDamage, pos, responsible, HitType)`、`int ExplosionDamageCloseLayer()`、`bool ProjectileCanRicochet()`、`Vehicle GetRootVehicle()`、`string GetFaction()`。
  - **HitPart 是被弹判定主入口**（对应步兵的 `BodyPart.HitPart`）：改命中反馈/装甲手感，patch 这里的 `HitPart`/`TryPenetrateArmor` 最合适。
- `VehicleDamageDetails : MonoBehaviour` 是屏幕伤害显示：`DisplayDamageData(string text, Color)`、`SetDirectionArrow(Vector2)`、`AnimateTexts()`（无害的 UI，mod 可随制造伤害调用）。

### 7. ArtilleryStrike / ArtilleryAimMover（火炮支援）
- `ArtilleryStrike : MonoBehaviour`：
  ```csharp
  int shells; float strikeRadius; float strikeFrequency; string shellType;
  Vector3 strikeStartPos; float nextStrikeHit; bool strikeStarted;
  void Start(); void Update(); public void StartStrike(); public void ShootProjectile();
  ```
- **可 mod 触发**：`StartStrike()` 是公开方法；且字段全公开（`shells / strikeRadius / strikeFrequency / shellType`）可在触发前覆盖 —— 非常适合"呼叫炮击"mod：`AddComponent<ArtilleryStrike>()` → 设参数 → `StartStrike()`；或对已有 `ArtilleryStrike` 实例调用。注意 `ShootProjectile()` 是 private 包装，但 `StartStrike` 已够。
- `ArtilleryAimMover : MonoBehaviour`：把炮管（Turret 的某轴）对到目标方向的辅助旋转脚本，字段 `Turret connectedTurret`、`byte axis`、`float rotationMultiplier`、`bool alightWithForwardDir`。

### 8. VehicleSpawner（生成 API）
- `VehicleSpawner : MonoBehaviour`：
  - 字段：`string vehiclePrefabID`、`int camoId`、`Vehicle spawnedVehicle`；
  - 方法：`GetVehicleId()`、`bool IsAvailable()`、`SpawnVehicle()`（返回 `Vehicle`）、`GetSpawnedVehicle()`、`bool CanTakeControl()`、`bool HasAliveWheeledVehicleOrPlane()`、`void GetVehiclePrefabAsync(callback)`、`bool SpawnedVehicleIsEmptyStaticVehicle()`。
- mod 可复用：找到/新建 `VehicleSpawner` 后 `SpawnVehicle()` 即生成车辆；生成异步用 `GetVehiclePrefabAsync`。生成后 `spawnedVehicle` 可直接驱动/装填/改名。

## 二、武器系统

### 1. GenericGun （步兵枪械核心）
- `public class GenericGun : Weapon`（`Weapon : HandheldItem`）。几乎全部枪械逻辑在此。
- 配置项（决定武器行为）：`string compatibleAmmo`、`int ammoSpace`、`string magazineSocket`、`bool automaticFire`、`int roundPerMinute`、`float bulletDispersion / recoilIntensity / delayBetweenShots`、`int muzzleVelocity`、`bool isManualBoltOperated / boltActionBeforeFiring / boltOpenAfterLastShot`、`bool isFlamethrower`、`float overheat_time`、`bool isModded`、`string modBundleName`、`string animBundleName / animBundleFolder`、`string fireFx_id / fireFx_bundle`、`string sound_equip_gun / sound_holster_gun`、`float firing_sound_variance`。
- 弹药/弹匣：`Magazine currentMagazine`、`AmmoItem usedAmmoItem`、`GetCurrentAmmoCount()`、`GetCurrentChamberedAmmoCount()`、`SetAmmoCount(int)`、`ExtractOneBullet()`、`HasMagazineInstalled()`、`UseMagazines()`、`GetChamberFreeSpace(VirtualItem, out canTopLoad, out mag)`。
- 开火：`Shoot(Creature user, Vector3 fireDir, bool isFakeShot=false)`、`FakeFire(...)`、`EmptyChamberClick()`、`ActualMuzzleVelocity`。**外部扣弹/开火可行**；semi/auto 由 `automaticFire`+`nextAvailableShot` 控制。
- 换弹/供弹：`CanReloadStripperClip()`、`MoveAmmosFromMagazineToWeapon()`、`IsReloadingOrUsingBolt()`、`TryInterruptReload()`、`Refill(int)`（补给）、`GetReloadAnimationLenght(bool)/GetBoltActionAnimationLenght(bool)`。
- 配件：`SupportAttachment(id/type)`、`HasAttachmentInstalled(AttachmentType)`、`TryGetInstalledAttachmentForSlot(SupportedAttachment, out Attachment)`、`RemoveAttachment(string)`、`RemoveAllAttachments()`、`SetAttachmentsAndMagazineFromIdList(Il2CppStringArray)`。
- 瞄准/零位：`IsUsingScope()`、`GetAimPos`、`ZeroingUp/Down()`、`GetCurrentZeroingInMeters(out min)`、`HasScopeWithMagnification()`、`IsSniperRifle()/IsPistol()`。
- 声音：`SingleFireSound(Soldier)`、`PlayFireSound(Soldier)`、`PlayMechanicalSound(AudioClip, Soldier, startTime)`、`ForceStopLoopedSound`、`SetReverb(bool)`；`UseCloseSound/DistantSound/LoopedSound`。
- 射击模式：`GetCurrentSelectorStateIndex()`、`SwitchFireMode()`、`IsFullAuto()`、`CouldFireFulAuto()`、`GetFireRate()`、`UpdateFireSelectorMeshes()`。

### 2. Magazine（弹匣，继承 ItemObject —— 真实弹匣）
- `AmmoItem usedAmmoItem`、`bool use_tracer`、`string compatibleAmmo`、`string socket`（挂点 id）、`int capacity`、`float swap_mag_time_perc_half/full`、`string override_anim_reload_half/full`。
- `GetCurrentAmmoCount()`、`Refill(int)`、`isInstalled()`。

### 3. Attachment（配件，继承 ItemObject）
- 方法多来自基类：`isInstalled()`、`GetAttachmentType()`（返回 `AttachmentType`）、`ToVirtualItem()`。
- 配件挂点信息不在 Attachment 而在 `GenericGun.magazineSocket` + `SupportedAttachment`（见 `WeaponSocketCompatibilityChecker`）。**每个弹匣/枪有 socket id 字符串，Inventory/轮盘以 type/虚拟物品匹配**。

### 4. FireSelectorHandler（射速选择器，挂枪上的 MonoBehaviour）
- `int CurrentState`、`int stateCount`、`Il2CppReferenceArray<SelectorLever> levers`、`float transitionDuration`；`SetState(int)`、`CycleNext()`、`CyclePrevious()`。GenericGun 通过 `SwitchFireMode`/`UpdateFireSelectorMeshes` 与它联动。改射击模式优先走 `GenericGun.SwitchFireMode()`（含 Selector 同步），少直接动 `FireSelectorHandler`。

### 5. Panzerfaust（一次性武器）
- `public class Panzerfaust : GenericGun`，仅新增 `string throw_away_anim`。**一次性丢弃逻辑由 GenericGun/相关流程实现**（无弹匣、无装填），mod 做一次性武器只需派生 `GenericGun` 并靠 `ammoSpace`/`automaticFire=false` + 丢弃动画即可。

### 6. DroppableAmmoBox（可放置弹箱）
- 继承 `ItemObjectStackable`：`string[] overrideContent`、`float delay`、`float nextAvailableUse`、`GameObject UICanvas`；`RefillAmmoForCurrentWeapons(Soldier)` 公开。放地上/自带补货可在 mod 里调用它给玩家补弹。

### 7. 武器数据存哪（重点）
- `ItemsDatabase`（MonoBehaviour，静态注册表 / bundle 管理器）是唯一入口：
  - `static ItemsDatabase instance`；`static bool Loaded`；`static string AssetBundlesPath / StreamingAssetsPath`；
  - bundle：`GetAssetBundle(name, folder)`、`LoadAssetBundleAsync`、`IsAssetBundleLoaded`、`WaitForAssetBundleLoaded`、`UnloadAssetBundle/UnloadAllAssetBundles`、`TotalMemoryCleanup`；
  - 取资源：`static UnityEngine.Object Load(name, bundle="er2bundle", folder=null)`、`LoadAsync(name,bundle,..,cb)`、`GetPropPrefabCached(prop_id)`、`LoadAndCacheSprite(name, bundle="er2gui")`；
  - **物品对象化**：`GetItemObject(string item_id)`、`T GetSpecificItemClass<T>(string item_id)`（IL2CPP 泛型）、`GetAllItemsOfType<T>(PropType)`、`GetLoadout/LoadoutRandom`、`GetSquadLoadouts`、`GetAmbientSoundIndex`。
- **武器定义在 er2items bundle**（大型资源包，用 manifest 而非全量读）；item_id → 物品定义（含 ammo/mag/socket/fx/sound id）。`GenericGun.isModded` + `modBundleName` 表明**游戏原生支持 mod 武器/音频/动画 bundle 覆盖**（把自定义 bundle 放对应目录即可被识别）。
- 增/改武器：以 `GetSpecificItemClass<GenericGun>` 取已有定义克隆改字段，或走 `GetItemObject` + `ToVirtualItem` 注入轮盘/背包（注意 AGENTS 陷阱 11：需正确子类应 `inv.items.Add(...)`）。

## 三、音频系统

### 1. RadioManager — 无线电（语音的"通讯范围"判定）
- 静态：`static List<RadioManager> radios`；`static bool IsNearRadio(Vector3)`、`static RadioManager GetNearRadio(Vector3)`；实例：`float radioRadius`。
- **作用**：不是播放器，而是"是否在无线电旁"的判定点（步兵/通讯兵触发语音的前提）。mod 可读它来判断玩家能否收到"呼支援/炮击"类语音。

> 真正放无线电/3D 语音在 `ResourcesManager`：`_PlayClipOnRadioDelayed` / `_PlayClipOn3DRadioDelayed` / `_GetRandomVoiceForCR`（见类型清单）。想播呼支援语音可从 ResourcesManager 找这些方法。

### 2. VoiceManager — 无线电接线员语音
- 实例 MonoBehaviour，内含大量 `Il2CppReferenceArray<AudioClip>`（`iVeBeenHit`、`enemyTankSpotted`、`artillerySupportAt`、`tankSupportRequest`、`artilleryStrikeIncomingAt`、`getOutTankOnFire`、`numbers`、`scream_long` 等）+ `float volumeMultiplier`。
- `AudioClip GetVoice(VoiceClip clip, int index = -1)`：按 `VoiceClip` 枚举取随机/指定语音。
- 说明：语音**数据从 er2voices 打包进各 VoiceManager 实例**；mod 想播自定义语音，可自行加载 AudioClip 走 SoundManager，或给 VoiceManager 换数组。它非静态，需找到场景/族实例。

### 3. SoundManager — 通用 3D 音效（mod 首选）
- 全部静态，签名非常干净：
  ```csharp
  static void SpawnAndPlayAsync(Vector3 pos, string sound_name_id, float maxHearDistance=30f, float volume=1f, float maxDuration=0f, bool loop=false, string bundle="er2bundle", Transform follow_go=null, Action<AudioSource> audioSource=null);
  static IEnumerator SpawnAndPlayAsyncCR(...);
  static void SpawnAndPlay(Vector3 pos, AudioClip clip, ...);            // 直接给 AudioClip
  static IEnumerator LoadAndPlaySound/CR(AudioSource, string sound_id, ..., string bundle="er2bundle", ...);
  static IEnumerator DestroyIn(AudioSource, float baseDuration, bool destroy_go, ...);
  static void ClickSound();
  ```
- **结论**：mod 既能按 `sound_id`+bundle 播放游戏音，也能 `SpawnAndPlay` 直接喂自定义 `AudioClip`（bundle=自定义）。这是"自定义音效/语音"最干净的入口。

### 4. EnvironmentSoundManager — 环境/氛围音
- 静态单例 `static EnvironmentSoundManager instance`；静态音量 `environmentVolume`、`ambientVolume`；`static void PlayAmbientSound(AudioClip)`。
- 实例：`TrySwitch(string targetClip)`、`IEnumerator CrossfadeEnvironmentSound(string sound_id)`、`IsWaterClip`、`SetLPFilterEnabled(bool, duration)`。
- 与 WeatherControl 可联动：mod 换天气时可 `CrossfadeEnvironmentSound` 切换环境底噪。

## 四、杂项系统

### 1. TemperatureEstimator — 温度估算（气候驱动）
- `static float EstimateTemperature(latitude, longitude, minute, hour, dayOfMonth, month, cloud_on_map, snowOnMap, WeatherType)`；`static float InterpolateValue(lat, dayOfYear, List<ClimateData>, count=4)`；`static List<ClimateData> points`；一堆纬度带/季节静态常量（`NORTHP_WINTER_TEMP`、`RAIN_EXCURSION`、`GROUND_SNOW_EXCURSION` 等）。
- **作用**：由地图经纬度/日期/天气插值出温度。当前**没有**可观测的"体温影响游戏性"的公开结果 → 温度目前较偏氛围/设定数据。WeatherControl 可复用它输出"当前温度℃"展示给玩家（如 HUD），或用于衍生天气强度。

### 2. CensorshipManager — 审查/血腥开关
- `List<CensorshipCriteria> rules` + `public bool IsCensorshipNeeded()`（`[Serializable]`，非常轻）。
- **与 LimbTweaks 联动**：`IsCensorshipNeeded()` 就是这个"是否该屏蔽血腥"的判定点 —— LimbTweaks 可在断肢/流血前调用它，为真则收敛特效/喷血量，或反之强制关闭。直接读实例判定即可，无需了解 criteria 细节。

### 3. AchievementManager — 成就
- 静态：`static HashSet<string> alreadyUnlocked`；`static void Unlock(string achievement_id, bool update_now=false)`、`UnlockDelayed(string, float, bool)`、`IEnumerator UnlockCR(...)`。
- mod 解锁/查状态都走静态方法，非常简单。

### 4. SaveDataManager — 通用序列化 / 存档（mod 强推）
- 静态 IO 工具集，最适合 mod 存自定义配置：
  - 路径：`static string persistentSavePath / persistentMissionEditorPath / persistentMapEditorPath / persistentModsPath / loadedMapPath`；`static string GetPersistentPathTo(string folderName)`。
  - 存取：`static bool SaveToFile<T>(data, fileName, path, method=BinaryFormatter)`、`static T LoadFromFile<T>(fileName, out data, path, method)`、`static bool FileExists<T>(file, path)`、`DeleteFile/DeleteFolder`、`ReadStream/CloseStream`；
  - JSON：`static string ObjectToJson<T>(obj, prettyPrint)`、`static T JsonToObject<T>(string)`、`static T DeserializeFromString<T>`、`static string SerializeToString<T>`；
  - 其它：`OpenExplorer/FixStringAsFilename/ResolvePathCaseInsensitive`。
- **存档结构**：存档根目录在 `persistentPath`，各子目录用 `GetPersistentPathTo` 取；可用 `SerializationMethod.BinaryFormatter`（整包二进制）或 JSON（可读）。mod 数据放 `persistentModsPath/<ModName>/` 最合适。

### 5. SaveDataMount — 存档挂载
- 静态：`GetMountPath(subPath)`、`MountSaveData(mount_dir, loadData, createSize)`、`UnmountSaveData()`、`ForceUnmountSaveData()`。
- 说明：存档以"挂载目录"方式管理（类似挂盘）。mod 一般无需直接调用，除非要包自己的存档文件进游戏存档区。

### 6. GameSettingsSystem — 游戏设置（热加载）
- 实例经静态引用存在；方法全是"设置+生效"：
  - 音量：`SetMasterVolume`、`SetMusicVolume`、`SetVoiceVolume`、`SetFXsVolume`、`GetMusicVolumeMultiplier()`；
  - UI：`SetGUIEnabled`、`SetOnScreenControlsEnabled`、`SetPlayerStatsUiEnabled`；
  - 语言/TPS：`SetLanguage(Languages)`、`SetTPS(bool)`、`Apply()`、`RefreshTPS(bool)`。
- **热加载友好**：改音量/UI 随时可调并在帧内生效，无需重启。WeatherControl / AIFood 若要做"只在某 UI 开时显示"可复用这些开关。

## 五、对 mod 的机会清单

1. **呼叫炮击 mod**：`ArtilleryStrike.StartStrike()` 公开 + 字段全可覆盖（半径/弹数/弹种/频率），`AddComponent<ArtilleryStrike>` 即可触发落点炮击；配合 `RadioManager.IsNearRadio(pos)` 只让通讯兵能呼。
2. **读/改载具状态 mod**：`Vehicle.allVehicles` + `GetDamage/SetDamage/DamageHull/Damage`/`Repair`/`BurnDown`/`IsDisabled` 全套公开，可做载具 HUD、一键维修、毁伤事件监听（patch `OnDisableVehicle`）。
3. **装甲/穿透手感 mod**：patch `VehicleDamagablePart.HitPart` / `TryPenetrateArmor`（弹道主入口），可调 `armorThickness/considerAngledArmor/canRicochet`，实现"更真实穿深/跳弹"。
4. **AI/玩家炮手加速、自动装填、弹药大改**：`TurretGun`（triggerPressed/SelectedAmmo/ExtractOneBullet）+ `TurretWeapon`（loadedAmmoCount/maxAmmoCount/roundPerMinute/delayBetweenShots）字段公开，可做"无限炮弹""射速调节""自动开火"。
5. **自定义音效/语音 mod**：`SoundManager.SpawnAndPlay(pos, AudioClip,…)` 直接播自定义 clip；`EnvironmentSoundManager.CrossfadeEnvironmentSound` 切环境音；`VoiceManager.GetVoice(VoiceClip)` 复用原生语音。
6. **LimbTweaks 血腥联动**：`CensorshipManager.IsCensorshipNeeded()` 就是血腥开关判定点，为真时收敛断肢喷血/特效。
7. **环境/温度/HUD mod**：`TemperatureEstimator.EstimateTemperature(...)` 输出温度值给 HUD；`GameSettingsSystem.SetXXXVolume`/`SetGUIEnabled` 热切换 UI/音频。
8. **自定义武器/弹匣 mod**：`ItemsDatabase.GetSpecificItemClass<GenericGun>`/`GetAllItemsOfType` + `GenericGun.isModded`/`modBundleName`，可无侵入注册/改武器，配合 `GenericGun.SwitchFireMode/Refill/SetAttachmentsAndMagazineFromIdList` 控制。
9. **mod 存档**：`SaveDataManager` 全套静态 JSON/Binary 序列化 + `persistentModsPath`，比 BepInEx cfg 更适合存复杂/共享进度。
10. **载具生成/调兵 mod**：`VehicleSpawner.SpawnVehicle()` + `VehicleSpawner.spawnedVehicle`，可在地图上按需生成己方/敌方载具（配合 `Vehicle.SetFaction/GetOnVehicle/AddAI`）。

---

### 附：反编译落盘清单（本次产出）
- `code\`：Vehicle、VehicleTank、VehiclePlane、MovableVehicle、Seats、VehicleSeat、Turret、VehicleDamagablePart、ArtilleryAimMover、ArtilleryStrike、VehicleSpawner、Weapon、Attachment、DroppableAmmoBox、RadioManager、SoundManager、EnvironmentSoundManager、TemperatureEstimator、CensorshipManager、AchievementManager、SaveDataManager、SaveDataMount、GameSettingsSystem、ItemsDatabase 等。
- `code2\`：TurretGun、TurretWeapon、Magazine、FireSelectorHandler、GenericGun、Panzerfaust、VoiceManager、VehicleDamageDetails、DroppableAmmoBox 等。
