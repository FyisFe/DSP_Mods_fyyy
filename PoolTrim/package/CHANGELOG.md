# Changelog / 更新日志

## 1.2.0

- 新增 `ColdGeometry` 配置项，在完整读档时无损压缩传送带的坐标与朝向。默认关闭，修改后需重启游戏。 / Added `ColdGeometry` to losslessly compress belt positions and rotations when loading a full save. It defaults to `false`; restart the game after changing it.
- 使用 Windows XPRESS_HUFF 压缩，复用每个线程的工作区。显示或编辑时按路径还原，保存时临时解压，存档中的路径数据仍使用原版格式。 / Compression uses Windows XPRESS_HUFF and reuses each thread's workspace. Paths are restored for rendering or editing and temporarily decompressed for saving. Saved path data keeps the vanilla format.
- 修复空路径裁剪后无法继续扩容的问题。空路径保留正容量，让后续铺带能正常扩容。 / Fixed empty paths failing to grow after trimming. They now keep a positive capacity so building more belts can expand them.
