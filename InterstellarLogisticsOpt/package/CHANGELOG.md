# Changelog / 更新日志

## 1.2.0

### 中文

- 修复没有闲船或电量不足时遗漏优先级锁更新和配对游标推进的问题。
- 减少供需不足时无效的对端库存读取和重复锁刷新，系数 1 时也生效。
- 卸载插件时取消界面及配置事件订阅。

### English

- Fixed missing priority-lock updates and pair-cursor advancement at stations with no idle ships or insufficient energy.
- Removed ineffective counterpart inventory reads and repeated lock refreshes when supply or demand is insufficient. This also works at factor 1.
- Unsubscribe UI and configuration events when unloading the plugin.
