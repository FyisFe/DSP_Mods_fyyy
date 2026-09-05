# Changelog / 更新日志

## 1.2.0

- 新增 `ColdGeometry` 配置项，完整读档时无损压缩传送带路径几何，默认关闭，修改后需重启游戏。 / Add optional lossless belt-path geometry compression during full-save loading. `ColdGeometry` defaults to `false` and requires a game restart after changes.
- 使用 Windows XPRESS_HUFF 压缩并复用线程工作区；显示或编辑时按路径还原，保存时临时解压，保持原版路径存档格式。 / Use Windows XPRESS_HUFF with reusable thread workspaces; restore paths for display or editing and decode temporarily for saving, preserving the vanilla path save format.
- 修复空路径缩容后无法继续扩容的问题，保留正容量以便再次铺带。 / Keep positive capacity when trimming empty paths so vanilla belt construction can grow them again.
