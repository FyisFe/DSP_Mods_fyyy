# FullPhotonReceiver

射线接收站在光子和发电两种模式下，始终提供当前预热程度、透镜和增产剂对应的满功率，无视太阳朝向与戴森球供能，并且不向戴森球申请能量。

- 保留原版预热过程、透镜消耗和产物缓存上限。
- 光子模式生产临界光子，不向电网供电；发电模式提供满功率容量，实际发电量由电网需求决定。
- 无配置项，加载即生效。将 `FullPhotonReceiver.dll` 放入 `BepInEx/plugins`，重启游戏。

Ray receivers run at full capacity in both photon and power modes for their current warmup, lens and proliferator level, regardless of sun direction or Dyson Sphere supply. They request no sphere energy. Photon mode produces photons without supplying the grid; power mode provides full generating capacity, with actual generation determined by grid demand. Native warmup, lens consumption and output buffers are preserved. No configuration is required.

## 2.0.0

发电模式也无视太阳朝向和戴森球供能限制，按当前预热、透镜和增产剂提供满功率容量。

Extend full-capacity operation to power mode, independent of sun direction and Dyson Sphere supply.

## 1.0.1

修复 DSP 0.10.35.29057 将 `catalystIncLevel` 属性改为字段后导致的 `MissingMethodException`。满功率计算改为调用游戏原生方法，以使用当前透镜类型和增产剂加成。

Fix the removed `catalystIncLevel` getter on DSP 0.10.35.29057. Use the game's native maximum-output calculation for lens types and proliferator bonuses.
