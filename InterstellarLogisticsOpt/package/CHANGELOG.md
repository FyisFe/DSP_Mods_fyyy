# Changelog / 更新日志

## 1.2.0

### 中文

- 修复没有闲船或电量不足时跳过整次调度的问题，保留必要的优先级锁更新和配对游标推进。
- 调整全航线降频方式，按原版优先级和塔的顺序完整扫描。优先级锁跟随派船检查一起放慢，避免下次扫描前锁已失效。系数仍为 1 至 30，存档格式不变。
- 减少供需不足时无效的对端库存读取和重复锁刷新，系数 1 时也生效。
- 关闭优化或切回系数 1 时恢复原版调度和锁倒计时。退出存档时清理调度状态，卸载插件时取消界面及配置事件订阅。

### English

- Fixed stations skipping the entire dispatch call when they had no idle ships or insufficient energy. Required priority-lock updates and pair-cursor advancement now run as usual.
- Changed amortization to run complete sweeps in native priority and station order. Priority locks count down with dispatch checks so they do not expire before the next sweep. The factor remains 1 to 30, with no save-format change.
- Removed ineffective counterpart inventory reads and repeated lock refreshes when supply or demand is insufficient. This also works at factor 1.
- Restore native dispatch and lock aging when disabled or set to factor 1. Clear dispatch state when unloading a save, and unsubscribe UI and configuration events when unloading the plugin.
