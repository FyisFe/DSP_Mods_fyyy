# Changelog / 更新日志

## 2.0.0

- 发电模式现在也无视太阳朝向和戴森球供能限制，按当前预热、透镜和增产剂提供满功率容量。/ Extend full-capacity operation to power mode, independent of sun direction and Dyson Sphere supply.
- 修复 DSP 0.10.35.29057 移除 `catalystIncLevel` getter 导致的 `MissingMethodException`；容量计算改用游戏原生方法，支持当前透镜类型、待使用透镜和增产剂加成。/ Fix the removed `catalystIncLevel` getter on DSP 0.10.35.29057; use the native capacity calculation for current lens types, queued lenses and proliferator bonuses.
