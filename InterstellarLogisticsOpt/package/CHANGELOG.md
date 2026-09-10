# Changelog / 更新日志

## 1.2.0

### 中文

- 修复没有闲船或电量不足时跳过整次调度的问题，保留必要的优先级锁更新和配对游标推进。
- 保留 1.1.0 按塔编号错开调度的方式，系数仍为 1 至 30。优先级锁倒计时和返程装货沿用原版；系数大于 1 时仍会牺牲部分优先级准确性和响应速度。
- 减少供需不足时无效的对端库存读取和重复锁刷新，系数 1 时也生效。
- 设置修改后从下一次模拟更新生效，关闭优化或切回系数 1 时恢复原版调度。卸载插件时取消界面及配置事件订阅。

### English

- Fixed stations skipping the entire dispatch call when they had no idle ships or insufficient energy. Required priority-lock updates and pair-cursor advancement now run as usual.
- Kept the station-ID phase dispersion from 1.1.0 and the factor range of 1 to 30. Priority-lock countdowns and return loading remain native; factors above 1 still trade some priority fidelity and responsiveness for performance.
- Removed ineffective counterpart inventory reads and repeated lock refreshes when supply or demand is insufficient. This also works at factor 1.
- Apply settings on the next simulation tick and restore native scheduling when disabled or set to factor 1. Unsubscribe UI and configuration events when unloading the plugin.
