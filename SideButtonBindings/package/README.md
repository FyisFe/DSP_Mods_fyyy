# SideButtonBindings

允许在游戏的“设置 → 按键设置”中，将可改键的功能绑定到鼠标侧键。适用于原生条目，以及 CE 等 Mod 添加到同一界面的条目。

- 支持通常的前进、后退侧键，以及 Unity 识别的其他附加鼠标按钮（`Mouse3`–`Mouse6`，即第 4–7 键）。
- 支持侧键与 Ctrl、Alt、Shift 的组合。
- 保留按键冲突检查；同一冲突组内重复占用的按键仍会被拒绝。
- 左键、右键、中键、滚轮的限制保持原样；本 Mod 不放开 Alt+W / Alt+S 与移动键的冲突。

需要 BepInEx 5。将压缩包内的 `SideButtonBindings.dll` 放入所使用游戏或 r2modman 配置的 `BepInEx/plugins/SideButtonBindings/`，启动游戏后在原生按键设置中录入侧键并点击“应用”。鼠标驱动需将按钮设置为标准鼠标侧键。

## 构建与验证

```powershell
dotnet build SideButtonBindings/SideButtonBindings.csproj -c Release
dotnet run --project SideButtonBindings/tests/Checks.csproj -c Debug -- "C:/Program Files (x86)/Applications/Steam/steamapps/common/Dyson Sphere Program/DSPGAME_Data/Managed"
```

编译引用已安装游戏的原版程序集，可通过 `GameManagedDir` 指定路径。Release 构建生成 `package/SideButtonBindings-1.0.0.zip`。

补丁仅在录入侧键时，临时放开当前设置条目的输入设备限制，结束或抛出异常时恢复。输入采集、按键冲突判断和保存加载仍由游戏处理。

离线检查针对 DSP 0.10.35.29057，覆盖侧键范围、设备分类、原生冲突比较和 XML 往返。实际鼠标输入、应用设置、重启保留和功能触发需要在游戏内验证。
