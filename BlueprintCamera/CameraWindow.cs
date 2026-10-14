using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BlueprintCamera.Core;
using UnityEngine;
using CameraPath = BlueprintCamera.Core.Path;
using FilePath = System.IO.Path;

namespace BlueprintCamera;

internal sealed class CameraWindow : IDisposable
{
    private readonly Plugin plugin;
    private Rect rect = new Rect(24, 64, 380, 500);
    private Vector2 scroll, frameScroll;
    private int tab, selectedFrame;
    private float scale = 1;
    private GUISkin skin;
    private readonly Dictionary<string, string> fields = new Dictionary<string, string>();
    private readonly Dictionary<Shot, Texture2D> thumbnails = new Dictionary<Shot, Texture2D>();
    private string bookmark = "收藏机位", exportName = "shot.json";
    private bool clearFocus;
    private bool advanced, reverseOrbit;
    private float orbitSeconds = 60;
    public bool Typing { get; private set; }
    public bool SimpleMode => !advanced;
    public bool MouseOver => plugin.ShowWindow && rect.Contains(new Vector2(Input.mousePosition.x / scale, (Screen.height - Input.mousePosition.y) / scale));
    private static readonly string[] Routes = { "固定", "直线", "局部环绕", "球面", "曲线", "整颗星球" };
    private static readonly string[] Aims = { "关键帧朝向", "固定方向", "相对地面", "注视目标", "沿路前看" };
    private static readonly string[] Repeats = { "一次", "跳回重播", "往返", "无缝循环" };

    public CameraWindow(Plugin plugin) { this.plugin = plugin; }

    public void Open(bool autoFrame)
    {
        advanced = false;
        scroll = Vector2.zero;
        Unfocus();
        plugin.NewPlanetOrbit(orbitSeconds, reverseOrbit, autoFrame);
    }

    public void ResetFields()
    {
        fields.Clear();
        selectedFrame = Math.Min(selectedFrame, plugin.Shot != null ? Math.Max(0, plugin.Shot.Frames.Count - 1) : 0);
        if (!advanced && plugin.Shot?.Route == Route.PlanetOrbit)
        {
            orbitSeconds = plugin.Shot.Duration;
            reverseOrbit = plugin.Shot.OrbitDegrees < 0;
        }
    }
    public void Unfocus() { Typing = false; clearFocus = true; }

    public void Draw(float requestedScale)
    {
        scale = Math.Min(requestedScale, Math.Min(Screen.width / (advanced ? 560f : 400f), Screen.height / 500f));
        if (skin == null)
        {
            skin = UnityEngine.Object.Instantiate(GUI.skin);
            var text = UIRoot.instance.GetComponentInChildren<UnityEngine.UI.Text>(true);
            if (text != null) skin.font = text.font;
            skin.label.fontSize = skin.button.fontSize = skin.toggle.fontSize = skin.textField.fontSize = 14;
            skin.label.wordWrap = true;
            skin.button.padding = new RectOffset(7, 7, 6, 6);
            skin.textField.padding = new RectOffset(6, 6, 5, 5);
            skin.window.padding = new RectOffset(14, 14, 30, 14);
        }
        var previousMatrix = GUI.matrix;
        var previousSkin = GUI.skin;
        var previousColor = GUI.backgroundColor;
        GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
        GUI.skin = skin;
        skin.label.fontSize = skin.button.fontSize = skin.toggle.fontSize = skin.textField.fontSize = advanced ? 14 : 16;
        GUI.backgroundColor = new Color(0.12f, 0.23f, 0.29f, 0.98f);
        rect.width = advanced ? 540 : 380;
        rect.height = Math.Min(advanced ? 780 : plugin.Free ? 630 : 560, Screen.height / scale - 24);
        rect.x = Mathf.Clamp(rect.x, 0, Screen.width / scale - rect.width);
        rect.y = Mathf.Clamp(rect.y, 0, Screen.height / scale - rect.height);
        rect = GUILayout.Window(175948021, rect, Contents, "蓝图摄影", GUILayout.Width(rect.width));
        if (clearFocus) { GUI.FocusControl(null); clearFocus = false; }
        Typing = (GUI.GetNameOfFocusedControl() ?? "").StartsWith("text:", StringComparison.Ordinal);
        GUI.matrix = previousMatrix;
        GUI.skin = previousSkin;
        GUI.backgroundColor = previousColor;
    }

    private void Contents(int id)
    {
        if (!advanced)
        {
            Simple();
            GUI.DragWindow(new Rect(0, 0, rect.width, 27));
            return;
        }
        GUILayout.BeginHorizontal();
        Button("← 返回简单拍摄", () =>
        {
            if (!ApplyParameters()) return;
            Open(false);
        });
        GUILayout.Label("高级编辑", GUILayout.Width(90));
        GUILayout.EndHorizontal();
        if (plugin.Selected < 0)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label("临时镜头 · 保存后可下次继续编辑");
            Button("保存此镜头", () => { if (ApplyParameters()) plugin.SaveShotCopy(); });
            GUILayout.EndHorizontal();
        }
        GUILayout.BeginHorizontal();
        GUILayout.Label(plugin.Free ? "自由构图" : plugin.Player.Running ? "播放中" : "构图已锁定", GUILayout.Width(130));
        Button(plugin.Free ? "锁定 Alt+F7" : "自由 Alt+F7", () => { plugin.Pause(); plugin.Free = !plugin.Free; });
        Button("收起 Tab", () => { plugin.ShowWindow = false; Unfocus(); });
        Button("退出", plugin.Exit);
        GUILayout.EndHorizontal();
        GUILayout.Label("中键转向 · WASD 移动 · QE 升降 · 滚轮推拉 · Alt+滚轮视野 · Shift 快 / Ctrl 慢");
        GUILayout.BeginHorizontal();
        GUILayout.Label("移动速度", GUILayout.Width(75));
        plugin.MoveSpeed = GUILayout.HorizontalSlider(plugin.MoveSpeed, 1, 100);
        GUILayout.Label(plugin.MoveSpeed.ToString("0") + " / 秒", GUILayout.Width(70));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        plugin.ShowPath = GUILayout.Toggle(plugin.ShowPath, "显示轨迹");
        plugin.HideHud = GUILayout.Toggle(plugin.HideHud, "隐藏 HUD");
        plugin.HideMecha = GUILayout.Toggle(plugin.HideMecha, "隐藏机甲");
        GUILayout.EndHorizontal();
        plugin.ApplyDisplay();

        int chosen = GUILayout.Toolbar(tab, new[] { "镜头", "路径机位", "收藏机位", "摄影库 / 文件" });
        if (chosen != tab) { tab = chosen; scroll = Vector2.zero; Unfocus(); }
        scroll = GUILayout.BeginScrollView(scroll);
        if (plugin.Shot != null)
        {
            switch (tab)
            {
                case 0: Settings(); break;
                case 1: Frames(); break;
                case 2: Bookmarks(); break;
                case 3: Files(); break;
            }
        }
        GUILayout.EndScrollView();
        GUILayout.Space(6);
        Playback();
        GUILayout.Label(plugin.Message, GUILayout.MinHeight(40));
        GUI.DragWindow(new Rect(0, 0, rect.width, 27));
    }

    private void Simple()
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label("整颗星球 · 匀速环绕");
        Button("高级…", () => { plugin.Pause(); advanced = true; scroll = Vector2.zero; Unfocus(); });
        Button("退出", plugin.Exit);
        GUILayout.EndHorizontal();
        scroll = GUILayout.BeginScrollView(scroll);
        GUILayout.Space(8);
        GUILayout.Label("对准星球中心，远近可以自由调整。");
        GUILayout.Space(12);
        GUILayout.Label("一圈用时");
        int duration = orbitSeconds == 30 ? 0 : orbitSeconds == 60 ? 1 : 2;
        int chosen = GUILayout.Toolbar(duration, new[] { "30 秒", "60 秒", "120 秒" }, GUILayout.Height(38));
        if (chosen != duration)
        {
            orbitSeconds = chosen == 0 ? 30 : chosen == 1 ? 60 : 120;
            UpdateOrbit();
        }
        GUILayout.Space(8);
        int direction = GUILayout.Toolbar(reverseOrbit ? 1 : 0, new[] { "向左环绕", "向右环绕" }, GUILayout.Height(34));
        if ((direction == 1) != reverseOrbit) { reverseOrbit = direction == 1; UpdateOrbit(); }
        GUILayout.Label("全程匀速，转完一圈后继续环绕。");
        GUILayout.Space(10);
        GUILayout.BeginHorizontal();
        Button("拉近", () => plugin.ZoomPlanetOrbit(-1));
        Button("拉远", () => plugin.ZoomPlanetOrbit(1));
        Button("自动构图", () => plugin.NewPlanetOrbit(orbitSeconds, reverseOrbit, true));
        GUILayout.EndHorizontal();
        GUILayout.Label("画面上滚轮调远近；调节时暂停预览。");
        if (plugin.Free)
        {
            GUILayout.Label("WASD / QE 移动，中键转向，滚轮推拉。确认后对准星心，保留当前距离。");
            GUILayout.BeginHorizontal();
            Button("使用当前位置", () => plugin.NewPlanetOrbit(orbitSeconds, reverseOrbit, false));
            Button("取消调整", () => plugin.Seek(0));
            GUILayout.EndHorizontal();
        }
        else Button("调整环绕起点…", () => { plugin.Pause(); plugin.Free = true; });
        GUILayout.EndScrollView();
        GUILayout.Space(8);
        Button(plugin.Player.Running ? "停止预览" : "①  预览环绕", () =>
        {
            if (plugin.Player.Running) plugin.Seek(0);
            else plugin.Play(false, false);
        }, 40);
        var color = GUI.backgroundColor;
        GUI.backgroundColor = new Color(0.2f, 0.7f, 0.68f, 1);
        Button("②  开始拍摄", () => plugin.Play(true, false), 46);
        GUI.backgroundColor = color;
        GUILayout.Label("先启动外部录屏。拍摄会倒数 3 秒，并隐藏界面、机甲和鼠标。Esc 找回面板。");
        if (plugin.Message != "路线就绪。") GUILayout.Label(plugin.Message, GUILayout.MinHeight(36));
        plugin.ApplyDisplay();
    }

    private void UpdateOrbit()
    {
        Run(() =>
        {
            if (plugin.Free) { plugin.NewPlanetOrbit(orbitSeconds, reverseOrbit, false); return; }
            plugin.Edit(s => { s.Duration = orbitSeconds; s.OrbitDegrees = reverseOrbit ? -360 : 360; });
            plugin.Seek(0);
        });
    }

    private void Settings()
    {
        Shot shot = plugin.Shot;
        Field("镜头名称", "name", shot.Name);
        GUILayout.Label("路线");
        int route = GUILayout.SelectionGrid((int)shot.Route, Routes, 3);
        if (route != (int)shot.Route) Run(() => plugin.Edit(s => s.Route = (Route)route));
        shot = plugin.Shot;
        GUILayout.Label("朝向");
        int aim = GUILayout.SelectionGrid((int)shot.Aim, Aims, 3);
        if (aim != (int)shot.Aim) Run(() => plugin.Edit(s => s.Aim = (Aim)aim));
        shot = plugin.Shot;
        GUILayout.BeginHorizontal();
        bool uniform = GUILayout.Toggle(shot.ConstantSpeed, "按路径距离匀速");
        bool fov = GUILayout.Toggle(shot.LockFov, "固定视野（首机位）");
        GUILayout.EndHorizontal();
        if (uniform != shot.ConstantSpeed || fov != shot.LockFov) Run(() => plugin.Edit(s => { s.ConstantSpeed = uniform; s.LockFov = fov; }));
        shot = plugin.Shot;
        Number("移动时长 / 秒", "duration", shot.Duration);
        Number("首尾缓动各 / 秒", "ease", shot.EaseSeconds);
        Number("拍摄倒计时 / 秒", "countdown", shot.Countdown);
        Number("起点停留 / 秒", "start", shot.StartHold);
        Number("终点停留 / 秒", "end", shot.EndHold);
        GUILayout.Label("播放方式");
        int repeat = GUILayout.SelectionGrid((int)shot.Repeat, Repeats, 2);
        if (repeat != (int)shot.Repeat) Run(() => plugin.Edit(s => s.Repeat = (Repeat)repeat));
        if (shot.IsOrbit)
        {
            Number("环绕角度 / °", "angle", shot.OrbitDegrees);
        }
        if (shot.Route == Route.PlanetOrbit)
            GUILayout.Label("围绕星球中心沿大圆运动；环绕平面由首机位决定。“注视目标”会对准星心。");
        if (shot.Route == Route.Orbit)
        {
            GUILayout.Label("以首机位为起点，绕目标所在地的竖直轴旋转；负角度反向。");
            GUILayout.BeginHorizontal();
            Number("半径", "radius", OrbitRadius(shot), 55);
            Number("高差", "height", OrbitHeight(shot), 55);
            GUILayout.EndHorizontal();
        }
        if (shot.Route != Route.PlanetOrbit && (shot.Route == Route.Orbit || shot.Aim == Aim.Target))
        {
            GUILayout.Space(6);
            GUILayout.BeginHorizontal();
            Button("目标取画面中心", plugin.TargetCenter);
            Button("目标取机甲位置", () => plugin.Edit(s => s.Target = Plugin.ToNumerics(GameMain.mainPlayer.position)));
            GUILayout.EndHorizontal();
            shot = plugin.Shot;
            Number("目标 X", "tx", shot.TargetX);
            Number("目标 Y", "ty", shot.TargetY);
            Number("目标 Z", "tz", shot.TargetZ);
            GUILayout.BeginHorizontal();
            Button("目标沿地面升高 1", () => plugin.Edit(s => s.Target += System.Numerics.Vector3.Normalize(s.Target)));
            Button("目标沿地面降低 1", () => plugin.Edit(s => s.Target -= System.Numerics.Vector3.Normalize(s.Target)));
            GUILayout.EndHorizontal();
        }
        Button("应用参数", () => ApplyParameters());
        GUILayout.Label(shot.ConstantSpeed ? "机位时间按路程自动分配；固定位置的转向请使用手动时间。" : "手动时间：在“路径机位”中设置每个机位到达的时间。缓动关闭时按该时间经过。");
        GUILayout.Label("直线与曲线使用行星局部空间；球面路线沿两机位间短弧行进，高度在两端之间插值。");
    }

    public bool ApplyParameters()
    {
        if (!advanced)
        {
            if (plugin.Free) plugin.NewPlanetOrbit(orbitSeconds, reverseOrbit, false);
            return true;
        }
        if (fields.Count == 0) return true;
        try
        {
            var next = plugin.Shot.Copy();
            if (fields.TryGetValue("name", out var name)) next.Name = name;
            next.Duration = Value("duration", next.Duration);
            next.EaseSeconds = Value("ease", next.EaseSeconds);
            next.Countdown = Value("countdown", next.Countdown);
            next.StartHold = Value("start", next.StartHold);
            next.EndHold = Value("end", next.EndHold);
            next.OrbitDegrees = Value("angle", next.OrbitDegrees);
            next.TargetX = Value("tx", next.TargetX); next.TargetY = Value("ty", next.TargetY); next.TargetZ = Value("tz", next.TargetZ);
            if (next.Route == Route.Orbit && next.Frames.Count > 0)
            {
                float radius = Value("radius", OrbitRadius(next)), height = Value("height", OrbitHeight(next));
                Shot.Range(radius, 0.1f, 5000, "环绕半径"); Shot.Range(height, -5000, 5000, "环绕高差");
                var up = System.Numerics.Vector3.Normalize(next.Target);
                var offset = next.Frames[0].Position - next.Target;
                var radial = offset - up * System.Numerics.Vector3.Dot(offset, up);
                if (radial.LengthSquared() < 1e-8f) radial = System.Numerics.Vector3.Cross(up, Math.Abs(up.X) < 0.9f ? System.Numerics.Vector3.UnitX : System.Numerics.Vector3.UnitY);
                next.Frames[0].Position = next.Target + up * height + System.Numerics.Vector3.Normalize(radial) * radius;
            }
            next.Validate(false);
            if (Store.Encode(next) != Store.Encode(plugin.Shot)) plugin.Edit(s => CopySettings(next, s));
            return true;
        }
        catch (Exception e) { plugin.Message = "参数未应用：" + e.Message; return false; }
    }

    private static void CopySettings(Shot from, Shot to)
    {
        to.Name = from.Name; to.Duration = from.Duration; to.EaseSeconds = from.EaseSeconds;
        to.Countdown = from.Countdown; to.StartHold = from.StartHold; to.EndHold = from.EndHold;
        to.OrbitDegrees = from.OrbitDegrees; to.Target = from.Target; to.Frames = from.Frames;
    }

    private void Frames()
    {
        var shot = plugin.Shot;
        GUILayout.BeginHorizontal();
        Button("追加当前构图", () => plugin.Edit(s =>
        {
            var f = plugin.View.Copy(); f.Name = "机位 " + (s.Frames.Count + 1); s.Frames.Add(f); s.SpaceTimes();
            if (s.Route == Route.Still && s.Frames.Count == 2) s.Route = Route.Line;
        }));
        Button("撤销 Ctrl+Z", () => plugin.Undo(false));
        Button("重做 Ctrl+Y", () => plugin.Undo(true));
        GUILayout.EndHorizontal();
        shot = plugin.Shot;
        frameScroll = GUILayout.BeginScrollView(frameScroll, GUILayout.Height(160));
        for (int i = 0; i < shot.Frames.Count; i++)
        {
            int index = i;
            var frame = shot.Frames[i];
            float progress = plugin.Path == null ? frame.Time : plugin.Path.ProgressAtFrame(i);
            if (GUILayout.Button($"{(selectedFrame == i ? "●" : "○")}  {i + 1}. {frame.Name}     {progress * shot.Duration:0.00} 秒"))
            { selectedFrame = index; fields.Clear(); plugin.Go(frame); }
        }
        GUILayout.EndScrollView();
        if (shot.Frames.Count == 0) return;
        selectedFrame = Math.Min(selectedFrame, shot.Frames.Count - 1);
        Frame selected = shot.Frames[selectedFrame];
        Field("机位名称", "frameName", selected.Name);
        Number("到达时间 / 秒", "frameTime", selected.Time * shot.Duration);
        Number("视野 FOV / °", "frameFov", selected.Fov);
        Number("位置 X", "frameX", selected.X);
        Number("位置 Y", "frameY", selected.Y);
        Number("位置 Z", "frameZ", selected.Z);
        GUILayout.Label("朝向由当前构图保存；开启自由相机后可中键微调，再更新此机位。");
        GUILayout.BeginHorizontal();
        Button("应用机位参数", () => plugin.Edit(s =>
        {
            var f = s.Frames[selectedFrame];
            f.Name = fields.TryGetValue("frameName", out var name) ? name : f.Name;
            f.Time = Value("frameTime", f.Time * s.Duration) / s.Duration;
            f.Fov = Value("frameFov", f.Fov);
            f.X = Value("frameX", f.X); f.Y = Value("frameY", f.Y); f.Z = Value("frameZ", f.Z);
        }));
        Button("用当前构图更新", () => plugin.Edit(s =>
        {
            var old = s.Frames[selectedFrame]; var f = plugin.View.Copy(); f.Name = old.Name; f.Time = old.Time; s.Frames[selectedFrame] = f;
        }));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Button("上移", () => MoveFrame(-1));
        Button("下移", () => MoveFrame(1));
        Button("删除（可撤销）", () => plugin.Edit(s => { s.Frames.RemoveAt(selectedFrame); s.SpaceTimes(); }));
        Button("平均分配时间", () => plugin.Edit(s => s.SpaceTimes()));
        GUILayout.EndHorizontal();
        GUILayout.Label("追加、删除或重排会平均分配机位时间；匀速模式仍按实际路程分配。首尾时间必须为 0 和总时长。");
        GUILayout.Space(8);
        GUILayout.Label("推拉预设：从当前构图沿视线移动，自动生成两个机位。");
        Number("移动距离（负数拉远）", "dolly", 20);
        Button("生成推近 / 拉远路线", () => plugin.Edit(s =>
        {
            float distance = Value("dolly", 20); Shot.Range(distance, -5000, 5000, "推拉距离");
            var first = plugin.View.Copy(); first.Name = "起点"; first.Time = 0;
            var last = first.Copy(); last.Name = "终点"; last.Time = 1;
            last.Position += System.Numerics.Vector3.Transform(System.Numerics.Vector3.UnitZ, first.Rotation) * distance;
            s.Frames = new List<Frame> { first, last }; s.Route = Route.Line; s.Aim = Aim.Fixed;
        }));
    }

    private void MoveFrame(int delta)
    {
        int next = selectedFrame + delta;
        if (next < 0 || next >= plugin.Shot.Frames.Count) return;
        plugin.Edit(s => { var frame = s.Frames[selectedFrame]; s.Frames.RemoveAt(selectedFrame); s.Frames.Insert(next, frame); s.SpaceTimes(); });
        selectedFrame = next;
    }

    private void Bookmarks()
    {
        GUILayout.BeginHorizontal();
        GUI.SetNextControlName("text:bookmark"); bookmark = GUILayout.TextField(bookmark, 100);
        Button("收藏当前画面", () => plugin.SaveView(bookmark));
        GUILayout.EndHorizontal();
        for (int i = 0; i < plugin.Library.Views.Count; i++)
        {
            int index = i; Shot view = plugin.Library.Views[i];
            GUILayout.BeginHorizontal(GUI.skin.box);
            if (!thumbnails.TryGetValue(view, out var thumb))
            {
                string file = plugin.ThumbnailFile(view);
                try
                {
                    if (File.Exists(file))
                    {
                        thumb = new Texture2D(2, 2);
                        if (!thumb.LoadImage(File.ReadAllBytes(file))) { UnityEngine.Object.Destroy(thumb); thumb = null; }
                    }
                }
                catch (IOException e) { plugin.Message = "缩略图读取失败：" + e.Message; }
                thumbnails[view] = thumb;
            }
            if (thumb != null) GUILayout.Label(thumb, GUILayout.Width(128), GUILayout.Height(72));
            GUILayout.BeginVertical();
            GUILayout.Label(view.Name + (plugin.Matches(view) ? "" : "（其他行星）"));
            GUILayout.BeginHorizontal();
            bool enabled = GUI.enabled; GUI.enabled = plugin.Matches(view);
            Button("恢复", () => plugin.Go(view.Frames[0]));
            Button("加入路线", () => plugin.Edit(s => { s.Frames.Add(view.Frames[0].Copy()); s.SpaceTimes(); }));
            GUI.enabled = enabled;
            Button("删除", () => Confirm("删除收藏机位", "删除“" + view.Name + "”？", () =>
            {
                plugin.Library.Views.RemoveAt(index);
                if (thumbnails.TryGetValue(view, out var image)) UnityEngine.Object.Destroy(image);
                thumbnails.Remove(view); plugin.Save();
            }));
            GUILayout.EndHorizontal(); GUILayout.EndVertical(); GUILayout.EndHorizontal();
        }
    }

    private void Files()
    {
        GUILayout.BeginHorizontal();
        Button("新建镜头", plugin.NewShot);
        Button(plugin.Selected < 0 ? "保存到摄影库" : "复制当前镜头", () => { if (ApplyParameters()) plugin.SaveShotCopy(); });
        Button("立即保存", () => { if (ApplyParameters()) plugin.Save(); });
        GUILayout.EndHorizontal();
        if (plugin.Selected < 0) GUILayout.Label("当前是临时镜头。保存到摄影库后，重启游戏也能继续编辑。");
        for (int i = 0; i < plugin.Library.Shots.Count; i++)
        {
            int index = i; Shot s = plugin.Library.Shots[i];
            if (GUILayout.Button((i == plugin.Selected ? "● " : "○ ") + s.Name + (plugin.Matches(s) ? "" : "（其他行星）"))) plugin.Select(index);
        }
        bool canDelete = GUI.enabled;
        GUI.enabled = canDelete && plugin.Selected >= 0;
        Button("删除当前镜头…", () => Confirm("删除镜头", "删除“" + plugin.Shot.Name + "”？", () =>
        {
            plugin.Library.Shots.RemoveAt(plugin.Selected);
            plugin.Selected = Math.Min(plugin.Selected, plugin.Library.Shots.Count - 1);
            plugin.Save();
        }));
        GUI.enabled = canDelete;
        GUILayout.Space(10);
        GUILayout.Label("导入 / 导出 JSON（镜头数据，视频由外部软件录制）");
        GUI.SetNextControlName("text:exportName"); exportName = GUILayout.TextField(exportName, 100);
        GUILayout.BeginHorizontal();
        Button("导出为新文件", () =>
        {
            if (!ApplyParameters()) return;
            string file = ExportFile(); Directory.CreateDirectory(FilePath.GetDirectoryName(file));
            using (var stream = new FileStream(file, FileMode.CreateNew, FileAccess.Write))
            using (var writer = new StreamWriter(stream)) writer.Write(Store.Encode(plugin.Shot));
            plugin.Message = "已导出：" + file;
        });
        Button("读取同名文件", () =>
        {
            string file = ExportFile();
            if (new FileInfo(file).Length > Store.MaxBytes) throw new ArgumentException("文件不得超过 2 MiB。");
            Import(File.ReadAllText(file));
        });
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Button("复制镜头 JSON", () => { if (ApplyParameters()) GUIUtility.systemCopyBuffer = Store.Encode(plugin.Shot); });
        Button("从剪贴板导入", () => Import(GUIUtility.systemCopyBuffer));
        Button("打开文件夹", () => { Directory.CreateDirectory(plugin.Folder); Application.OpenURL(new Uri(plugin.Folder + FilePath.DirectorySeparatorChar).AbsoluteUri); });
        GUILayout.EndHorizontal();
        GUILayout.Label("库自动保存到 BepInEx/config/BlueprintCamera/library.json；文件导出到 exports。镜头绑定星系种子和行星。导入会新增镜头。");
        if (plugin.LoadError != null) GUILayout.Label("原摄影库读取失败，为保护文件，本次会话禁用保存。请先修复或移走 library.json 后重启游戏；仍可导出新镜头。");
    }

    private void Import(string json)
    {
        if (plugin.Library.Shots.Count >= 128) throw new ArgumentException("摄影库已满。");
        var shot = Store.Decode<Shot>(json);
        if (shot == null) throw new ArgumentException("镜头为空。");
        shot.Validate(false);
        plugin.Library.Shots.Add(shot); plugin.Select(plugin.Library.Shots.Count - 1); plugin.Changed();
    }

    private string ExportFile()
    {
        if (string.IsNullOrWhiteSpace(exportName) || FilePath.GetFileName(exportName) != exportName || exportName.IndexOfAny(FilePath.GetInvalidFileNameChars()) >= 0 || !exportName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("请输入不含路径的 .json 文件名。");
        return FilePath.Combine(plugin.Folder, "exports", exportName);
    }

    private void Playback()
    {
        GUILayout.BeginHorizontal();
        bool old = GUI.enabled; GUI.enabled = plugin.Path != null;
        float p = GUILayout.HorizontalSlider(plugin.Player.Progress, 0, 1);
        if (Math.Abs(p - plugin.Player.Progress) > 0.00001f) plugin.Seek(p);
        GUI.enabled = old;
        GUILayout.Label($"{plugin.Player.Progress * plugin.Shot.Duration:0.00} / {plugin.Shot.Duration:0.00} 秒", GUILayout.Width(130));
        GUILayout.EndHorizontal();
        GUILayout.BeginHorizontal();
        Button("回起点", () => plugin.Seek(0));
        Button(plugin.Player.Running ? "暂停" : "播放 Alt+F9", () => { if (plugin.Player.Running) plugin.Pause(); else plugin.Play(false, false); });
        Button("反向", () => plugin.Play(false, true));
        Button("拍摄 Alt+F10", () => plugin.Play(true, false));
        GUILayout.EndHorizontal();
        GUILayout.Label("拍摄自动隐藏 HUD、机甲、鼠标和轨迹；Esc 暂停并显示面板，再按 Esc 退出。");
    }

    private string Field(string label, string id, string value, float width = 145)
    {
        if (!fields.TryGetValue(id, out string text)) text = value;
        GUILayout.BeginHorizontal(); GUILayout.Label(label, GUILayout.Width(width));
        GUI.SetNextControlName("text:" + id); text = GUILayout.TextField(text, 100); fields[id] = text;
        GUILayout.EndHorizontal(); return text;
    }
    private void Number(string label, string id, float value, float width = 145) => Field(label, id, value.ToString("R", CultureInfo.InvariantCulture), width);
    private float Value(string id, float fallback)
    {
        if (!fields.TryGetValue(id, out var text)) return fallback;
        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) || float.IsNaN(value) || float.IsInfinity(value))
            throw new ArgumentException("请输入有效数字：" + text);
        return value;
    }
    private static float OrbitHeight(Shot s) => s.Frames.Count == 0 ? 0 : System.Numerics.Vector3.Dot(s.Frames[0].Position - s.Target, System.Numerics.Vector3.Normalize(s.Target));
    private static float OrbitRadius(Shot s)
    {
        if (s.Frames.Count == 0) return 20;
        var up = System.Numerics.Vector3.Normalize(s.Target);
        return (s.Frames[0].Position - s.Target - up * OrbitHeight(s)).Length();
    }
    private void Button(string label, Action action, float height = 0) { if (GUILayout.Button(label, GUILayout.MinHeight(height))) Run(action); }
    private void Run(Action action) { try { action(); } catch (Exception e) { plugin.Message = e.Message; } }
    private void Confirm(string title, string text, Action action)
    {
        plugin.Exit();
        UIMessageBox.Show(title, text, "取消", "删除", UIMessageBox.WARNING, null, () => Run(action));
    }
    public void Dispose()
    {
        if (skin != null) UnityEngine.Object.Destroy(skin);
        foreach (var texture in thumbnails.Values) UnityEngine.Object.Destroy(texture);
    }
}
