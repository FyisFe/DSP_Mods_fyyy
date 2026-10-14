using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using BepInEx;
using BepInEx.Configuration;
using BlueprintCamera.Core;
using HarmonyLib;
using UnityEngine;
using NVector = System.Numerics.Vector3;
using NQuaternion = System.Numerics.Quaternion;
using CameraPath = BlueprintCamera.Core.Path;
using FilePath = System.IO.Path;

namespace BlueprintCamera;

[BepInPlugin(Guid, "BlueprintCamera", "0.2.1")]
[BepInIncompatibility("starfi5h.plugin.CameraTools")]
public sealed class Plugin : BaseUnityPlugin
{
    public const string Guid = "org.fyyy.blueprintcamera";
    internal static Plugin Instance;
    internal bool Active, Free, ShowWindow, ShowPath, HideHud, HideMecha;
    internal string Message = "Alt+F8 进入摄影；鼠标中键转向，WASD 平移，QE 升降。";
    internal Frame View;
    internal Library Library;
    internal int Selected = -1;
    private Shot quickShot;
    internal Shot Shot
    {
        get => Selected < 0 ? quickShot : Library.Shots[Selected];
        private set { if (Selected < 0) quickShot = value; else Library.Shots[Selected] = value; }
    }
    internal readonly Playback Player = new Playback();
    internal CameraPath Path;
    internal float MoveSpeed = 20;
    internal bool Capturing { get; private set; }
    internal bool CleanDisplay => Capturing || thumbPending;
    internal string Folder { get; private set; }
    internal string LoadError;
    private string libraryFile;
    private Harmony harmony;
    private CameraWindow window;
    private ConfigEntry<KeyboardShortcut> toggleKey, playKey, captureKey, lockKey;
    private ConfigEntry<float> uiScale;
    private CameraPose originalPose;
    private Matrix4x4 originalProjection;
    private GameData session;
    private PlanetData planet;
    private UIGame gameUi;
    private bool originalModel, originalCursor;
    private float originalAlpha;
    private bool originalInteractable, originalRaycasts;
    private int originalHideShader;
    private bool displayHidden, modelHidden, thumbPending;
    private int releasedInputFrame = -1;
    private double saveAt = double.PositiveInfinity;
    private readonly Stack<Shot> undo = new Stack<Shot>(), redo = new Stack<Shot>();
    private LineRenderer line, targetLine;
    private Material lineMaterial;

    private void Awake()
    {
        Instance = this;
        Folder = FilePath.Combine(Paths.ConfigPath, "BlueprintCamera");
        libraryFile = FilePath.Combine(Folder, "library.json");
        try { Library = Store.Load(libraryFile); }
        catch (Exception e) { Library = new Library(); LoadError = e.Message; Logger.LogError(e); }
        toggleKey = Config.Bind("Keys", "Toggle", new KeyboardShortcut(KeyCode.F8, KeyCode.LeftAlt), "Enter/exit photography");
        playKey = Config.Bind("Keys", "PlayPause", new KeyboardShortcut(KeyCode.F9, KeyCode.LeftAlt), "Play/pause current shot");
        captureKey = Config.Bind("Keys", "Capture", new KeyboardShortcut(KeyCode.F10, KeyCode.LeftAlt), "Countdown and clean playback for external recording");
        lockKey = Config.Bind("Keys", "FreeLock", new KeyboardShortcut(KeyCode.F7, KeyCode.LeftAlt), "Free camera / locked composition");
        uiScale = Config.Bind("UI", "Scale", 1f, new ConfigDescription("Photography panel scale", new AcceptableValueRange<float>(0.7f, 2f)));
        window = new CameraWindow(this);
        harmony = new Harmony(Guid);
        try { harmony.PatchAll(typeof(CameraPatches)); }
        catch
        {
            harmony.UnpatchSelf(); Instance = null; enabled = false;
            throw;
        }
        Logger.LogInfo("BlueprintCamera 0.2.1: Alt+F8 opens photography mode.");
    }

    private bool Available => GameMain.isRunning && !GameMain.isLoading && !DSPGame.IsMenuDemo && !GameMain.inOtherScene &&
        GameMain.localPlanet != null && GameMain.localPlanet.factoryLoaded && GameMain.mainPlayer != null && GameMain.mainPlayer.isAlive &&
        GameCamera.main != null && UIRoot.instance != null;

    internal void InputFrame()
    {
        if (Active && (!Available || GameMain.data != session || GameMain.localPlanet != planet)) Exit();
        if (Active && (UIRoot.instance.uiGame.starmap.active || UIRoot.instance.uiGame.screenshotTool.active || GameMain.isPaused || UIMessageBox.activeCount > 0)) Exit();
        bool typing = window.Typing;
        if (toggleKey.Value.IsDown() && !typing && (Active || !VFInput.inputing))
        {
            if (Active) Exit(); else Enter();
            VFInput.UseEscape();
            return;
        }
        if (!Active) return;
        // Consume Escape before UIGame can open its pause menu or clear a clean display.
        if (Input.GetKeyDown(KeyCode.Escape))
        {
            VFInput.UseEscape();
            if (typing) window.Unfocus();
            else if (Capturing || Player.Running) Pause();
            else Exit();
            return;
        }
        if (typing) return;
        if (playKey.Value.IsDown()) { if (Player.Running) Pause(); else Play(false, false); }
        if (captureKey.Value.IsDown()) Play(true, false);
        if (lockKey.Value.IsDown()) { Pause(); Free = !Free; }
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
        {
            if (Input.GetKeyDown(KeyCode.Z)) Undo(false);
            if (Input.GetKeyDown(KeyCode.Y)) Undo(true);
        }
        if (Input.GetKeyDown(KeyCode.Tab)) ShowWindow = !ShowWindow;
    }

    private void Update()
    {
        if (Time.realtimeSinceStartupAsDouble >= saveAt) Save();
        if (Active && !Available) Exit();
    }

    // Native UpdateCommandState assigns Move/Follow/Mine on hover, without a click.
    internal static bool CanEnterFrom(ECommand command, bool picking) => !picking &&
        (command == ECommand.None || command == ECommand.Move || command == ECommand.Follow || command == ECommand.Mine);

    internal void Enter()
    {
        if (!Available) { Message = "请先进入一颗已加载工厂的行星。"; return; }
        var ui = UIRoot.instance.uiGame;
        if (VFInput.inFullscreenGUI || ui.screenshotTool.active || UIMessageBox.activeCount > 0)
        { UIRealtimeTip.Popup("请先关闭全屏窗口、截图工具或对话框。"); return; }
        var controller = GameMain.mainPlayer.controller;
        if (!CanEnterFrom(controller.cmd.type, controller.inPickMode))
        { UIRealtimeTip.Popup("请先退出建造、拆除、拾取或物品操作模式。"); return; }
        session = GameMain.data;
        planet = GameMain.localPlanet;
        gameUi = ui;
        originalPose = new CameraPose(GameCamera.main);
        originalProjection = GameCamera.main.projectionMatrix;
        originalModel = GameMain.mainPlayer.controller.modelVisible;
        originalCursor = Cursor.visible;
        originalAlpha = ui.canvasGroup.alpha;
        originalInteractable = ui.canvasGroup.interactable;
        originalRaycasts = ui.canvasGroup.blocksRaycasts;
        originalHideShader = Shader.GetGlobalInt("_Global_HideAllUI");
        View = ReadCamera();
        Active = Free = ShowWindow = true;
        HideHud = HideMecha = Capturing = false;
        window.Open(true);
        if (LoadError != null) Message = "摄影库读取失败，原文件保留，保存已禁用：" + LoadError;
    }

    internal void Exit()
    {
        if (!Active) return;
        StopAllCoroutines();
        Pause();
        HideHud = HideMecha = thumbPending = false;
        ApplyDisplay();
        Active = Free = ShowWindow = false;
        releasedInputFrame = Time.frameCount;
        if (GameCamera.main != null && GameMain.data == session && GameMain.localPlanet == planet)
        {
            originalPose.ApplyToCamera(GameCamera.main);
            GameCamera.main.projectionMatrix = originalProjection;
        }
        if (line != null) line.enabled = false;
        if (targetLine != null) targetLine.enabled = false;
        window.Unfocus();
        session = null;
        planet = null;
        gameUi = null;
        Save();
    }

    internal void CameraFrame()
    {
        if (!Available || session != GameMain.data || planet != GameMain.localPlanet) { Exit(); return; }
        if (window.SimpleMode && !Free && !CleanDisplay && !window.MouseOver && !window.Typing && Application.isFocused)
        {
            float wheel = Input.mouseScrollDelta.y;
            if (wheel != 0) ZoomPlanetOrbit(-wheel);
        }
        if (Player.Running)
        {
            Player.Tick(Time.realtimeSinceStartupAsDouble);
            View = Path.Evaluate(Player.Progress);
            if (!Player.Running) { Capturing = false; ShowWindow = true; }
        }
        else if (Free && !window.Typing && !window.MouseOver && Application.isFocused)
        {
            float dt = Mathf.Min(Time.unscaledDeltaTime, 0.1f);
            float speed = MoveSpeed * (Input.GetKey(KeyCode.LeftShift) ? 5 : 1) * (Input.GetKey(KeyCode.LeftControl) ? 0.2f : 1);
            var rotation = ToUnity(View.Rotation);
            var position = ToUnity(View.Position);
            if (Input.GetMouseButton(2)) rotation *= Quaternion.Euler(-Input.GetAxisRaw("Mouse Y") * 2, Input.GetAxisRaw("Mouse X") * 2, 0);
            float x = Axis(KeyCode.D, KeyCode.A), z = Axis(KeyCode.W, KeyCode.S), y = Axis(KeyCode.E, KeyCode.Q);
            Vector3 delta = new Vector3(x, y, z);
            if (delta.sqrMagnitude > 1) delta.Normalize();
            position += rotation * delta * (speed * dt);
            if (Input.GetKey(KeyCode.LeftAlt)) View.Fov = Mathf.Clamp(View.Fov - Input.mouseScrollDelta.y * 2, 10, 120);
            else position += rotation * Vector3.forward * (Input.mouseScrollDelta.y * speed * 0.2f);
            // Free positioning stays above the sphere; authored paths report crossings instead of silently changing them.
            if (position.magnitude < planet.realRadius + 0.2f) position = position.normalized * (planet.realRadius + 0.2f);
            View.Position = ToNumerics(position);
            View.Rotation = ToNumerics(rotation);
        }
        GameCamera.main.transform.SetPositionAndRotation(ToUnity(View.Position), ToUnity(View.Rotation));
        GameCamera.main.fieldOfView = View.Fov;
        // Native PostLogic writes a custom matrix; reset it so the photographic FOV is actually rendered.
        GameCamera.main.ResetProjectionMatrix();
        ApplyDisplay();
        if (line != null) line.enabled = ShowPath && !Capturing && !thumbPending && Path != null;
        if (targetLine != null) targetLine.enabled = ShowPath && !Capturing && !thumbPending && Matches(Shot) && Shot.Route != Route.PlanetOrbit && (Shot.Route == Route.Orbit || Shot.Aim == Aim.Target);
    }

    internal void Fail(Exception error)
    {
        Logger.LogError(error);
        Exit();
        Message = "摄影已退出：" + error.Message;
    }

    internal bool BlockInput => Active || releasedInputFrame == Time.frameCount;

    internal void ApplyDisplay()
    {
        bool clean = Capturing || thumbPending;
        Cursor.visible = clean ? false : originalCursor;
        if (clean)
        {
            if (line != null) line.enabled = false;
            if (targetLine != null) targetLine.enabled = false;
        }
        if (gameUi == null) return;
        bool hide = HideHud || clean;
        if (hide)
        {
            gameUi.canvasGroup.alpha = 0;
            gameUi.canvasGroup.interactable = gameUi.canvasGroup.blocksRaycasts = false;
            Shader.SetGlobalInt("_Global_HideAllUI", 1);
        }
        else if (displayHidden)
        {
            gameUi.canvasGroup.alpha = originalAlpha;
            gameUi.canvasGroup.interactable = originalInteractable;
            gameUi.canvasGroup.blocksRaycasts = originalRaycasts;
            Shader.SetGlobalInt("_Global_HideAllUI", originalHideShader);
        }
        displayHidden = hide;
        bool hideModel = HideMecha || clean;
        if (hideModel != modelHidden && GameMain.data == session && session?.mainPlayer?.controller?.model != null)
            session.mainPlayer.controller.modelVisible = hideModel ? false : originalModel;
        modelHidden = hideModel;
    }

    private void OnApplicationFocus(bool focused)
    {
        if (!focused && Active && Player.Running) { Pause(); Message = "窗口失去焦点，镜头已暂停。"; }
    }

    private void OnGUI()
    {
        if (!Active || thumbPending) return;
        if (Capturing)
        {
            if (Player.Phase == Phase.Countdown)
            {
                var style = new GUIStyle(GUI.skin.box) { fontSize = 36, alignment = TextAnchor.MiddleCenter };
                GUI.Box(new Rect(Screen.width / 2f - 100, Screen.height / 2f - 45, 200, 90), Math.Ceiling(Player.CountdownLeft).ToString(), style);
            }
            return;
        }
        if (ShowWindow) window.Draw(uiScale.Value);
    }

    internal void NewPlanetOrbit(float seconds, bool reverse, bool autoFrame)
    {
        var next = PlanetOrbit.Create(View, GameMain.galaxy.seed, planet.id, planet.realRadius,
            GameCamera.main.aspect, seconds, reverse, autoFrame);
        Pause();
        quickShot = next;
        Selected = -1;
        undo.Clear(); redo.Clear();
        ShowPath = HideHud = HideMecha = false;
        Rebuild(); window.ResetFields(); Seek(0);
    }

    internal void ZoomPlanetOrbit(float steps)
    {
        if (Free) NewPlanetOrbit(Shot.Duration, Shot.OrbitDegrees < 0, false);
        Pause();
        float progress = Player.Progress;
        Edit(s => PlanetOrbit.Zoom(s, steps));
        Seek(progress);
    }

    internal void SaveShotCopy()
    {
        if (Library.Shots.Count >= 128) throw new ArgumentException("摄影库已满。");
        var copy = Shot.Copy();
        if (Selected >= 0) copy.Name = "副本 " + copy.Name;
        Library.Shots.Add(copy);
        Select(Library.Shots.Count - 1);
        Changed();
    }

    internal void NewShot()
    {
        if (Library.Shots.Count >= 128) throw new ArgumentException("最多保存 128 条路线，请先导出并删除部分镜头。");
        Pause();
        var shot = new Shot { Name = "镜头 " + (Library.Shots.Count + 1), Seed = GameMain.galaxy.seed, PlanetId = planet.id, PlanetRadius = planet.realRadius, Route = Route.Still };
        shot.Frames.Add(View.Copy());
        shot.Target = ToNumerics(GameMain.mainPlayer.position);
        Library.Shots.Add(shot);
        Selected = Library.Shots.Count - 1;
        undo.Clear(); redo.Clear();
        Changed();
    }

    internal void Select(int index)
    {
        Pause(); Selected = index; undo.Clear(); redo.Clear(); Rebuild(); window.ResetFields();
    }

    internal void Edit(Action<Shot> edit)
    {
        var next = Shot.Copy();
        edit(next);
        next.Validate(false);
        Pause();
        undo.Push(Shot.Copy()); redo.Clear();
        Shot = next;
        Changed();
    }

    internal void Undo(bool forward)
    {
        var from = forward ? redo : undo;
        if (from.Count == 0) return;
        (forward ? undo : redo).Push(Shot.Copy());
        Pause(); Shot = from.Pop(); Changed();
        if (window.SimpleMode) Seek(Player.Progress);
    }

    internal void Changed()
    {
        if (Selected >= 0) saveAt = Time.realtimeSinceStartupAsDouble + 0.7;
        Rebuild(); window.ResetFields();
    }

    internal void Rebuild()
    {
        Path = null;
        if (Shot == null) return;
        try
        {
            if (!Matches(Shot)) { Message = "该镜头属于其他行星或星系；到达原地点后可播放。"; UpdateLines(); return; }
            Path = new CameraPath(Shot);
            Message = Path.BelowSurface ? "路线接近或穿过球面，请调整机位后拍摄。" : "路线就绪。";
        }
        catch (ArgumentException e) { Message = e.Message; }
        UpdateLines();
    }

    internal bool Matches(Shot s) => planet != null && s.Seed == GameMain.galaxy.seed && s.PlanetId == planet.id && Math.Abs(s.PlanetRadius - planet.realRadius) < 0.01f;

    internal void Play(bool capture, bool reverse)
    {
        if (!Active || !window.ApplyParameters()) return;
        if (Path == null) Rebuild();
        if (Path == null) return;
        if (Path.BelowSurface) { Message = "路线穿过球面，请先调整；可拖动进度定位问题。"; return; }
        Free = false;
        Capturing = capture;
        float progress = Player.Progress;
        if (reverse && progress <= 0 || !reverse && progress >= 1) progress = reverse ? 1 : 0;
        Player.Start(Shot, Time.realtimeSinceStartupAsDouble, reverse, capture, progress);
        View = Path.Evaluate(Player.Progress);
        ApplyDisplay();
    }

    internal void Pause()
    {
        bool moving = Player.Running;
        Player.Pause(Time.realtimeSinceStartupAsDouble);
        if (moving && Path != null) View = Path.Evaluate(Player.Progress);
        if (Capturing) ShowWindow = true;
        Capturing = false;
        ApplyDisplay();
    }

    internal void Seek(float p)
    {
        if (Path == null) return;
        Pause(); Free = false; Player.Seek(p); View = Path.Evaluate(p);
    }

    internal void Go(Frame frame) { Pause(); Free = false; View = frame.Copy(); }

    internal void TargetCenter()
    {
        Ray ray = new Ray(ToUnity(View.Position), ToUnity(View.Rotation) * Vector3.forward);
        Edit(next => next.Target = ToNumerics(TargetPicker.Pick(planet, ray)));
    }

    internal void SaveView(string name)
    {
        if (Library.Views.Count >= 256) throw new ArgumentException("最多收藏 256 个机位。");
        var saved = new Shot { Name = name, Seed = GameMain.galaxy.seed, PlanetId = planet.id, PlanetRadius = planet.realRadius, Route = Route.Still };
        saved.Frames.Add(View.Copy()); saved.Validate(false);
        Library.Views.Add(saved); saveAt = 0;
        StartCoroutine(Thumbnail(saved));
    }

    internal string ThumbnailFile(Shot shot)
    {
        using (var hash = System.Security.Cryptography.SHA256.Create())
        {
            string id = BitConverter.ToString(hash.ComputeHash(System.Text.Encoding.UTF8.GetBytes(Store.Encode(shot)))).Replace("-", "");
            return FilePath.Combine(Folder, "thumbnails", id + ".png");
        }
    }

    private IEnumerator Thumbnail(Shot saved)
    {
        Pause(); Free = false; thumbPending = true; ApplyDisplay();
        // Let a complete frame render without the IMGUI commands that triggered the capture.
        yield return null;
        yield return new WaitForEndOfFrame();
        Texture2D full = null, small = null;
        RenderTexture rt = null, old = RenderTexture.active;
        try
        {
            if (!Active || GameMain.data != session) yield break;
            full = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            full.ReadPixels(new Rect(0, 0, Screen.width, Screen.height), 0, 0); full.Apply();
            rt = RenderTexture.GetTemporary(192, 108, 0);
            Graphics.Blit(full, rt); RenderTexture.active = rt;
            small = new Texture2D(192, 108, TextureFormat.RGB24, false);
            small.ReadPixels(new Rect(0, 0, 192, 108), 0, 0); small.Apply();
            string file = ThumbnailFile(saved); Directory.CreateDirectory(FilePath.GetDirectoryName(file));
            File.WriteAllBytes(file, small.EncodeToPNG());
        }
        finally
        {
            RenderTexture.active = old;
            if (rt != null) RenderTexture.ReleaseTemporary(rt);
            if (full != null) Destroy(full);
            if (small != null) Destroy(small);
            thumbPending = false; ApplyDisplay();
        }
    }

    internal void Save()
    {
        saveAt = double.PositiveInfinity;
        if (LoadError != null || Library == null) return;
        try { Store.Save(libraryFile, Library); }
        catch (Exception e) { Message = "保存失败：" + e.Message; Logger.LogError(e); }
    }

    private void UpdateLines()
    {
        if (!Active) return;
        if (line == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader == null) return;
            lineMaterial = new Material(shader);
            line = MakeLine("blueprint-camera-path", new Color(0.1f, 0.9f, 0.8f));
            targetLine = MakeLine("blueprint-camera-target", new Color(1, 0.55f, 0.15f));
        }
        line.positionCount = Path == null ? 0 : 257;
        if (Path != null) for (int i = 0; i < 257; i++) line.SetPosition(i, ToUnity(Path.Evaluate(i / 256f).Position));
        Vector3 target = ToUnity(Shot.Target), up = target.normalized;
        targetLine.positionCount = 2;
        targetLine.SetPosition(0, target); targetLine.SetPosition(1, target + up * 8);
    }

    private LineRenderer MakeLine(string name, Color color)
    {
        var go = new GameObject(name); go.transform.SetParent(transform, false);
        var renderer = go.AddComponent<LineRenderer>();
        renderer.sharedMaterial = lineMaterial; renderer.useWorldSpace = true;
        renderer.startWidth = renderer.endWidth = 0.12f;
        renderer.startColor = renderer.endColor = color;
        return renderer;
    }

    private void OnDestroy()
    {
        if (Active) Exit(); else Save();
        harmony?.UnpatchSelf(); window?.Dispose();
        if (line != null) Destroy(line.gameObject);
        if (targetLine != null) Destroy(targetLine.gameObject);
        if (lineMaterial != null) Destroy(lineMaterial);
        Instance = null;
    }

    private static float Axis(KeyCode positive, KeyCode negative) => (Input.GetKey(positive) ? 1 : 0) - (Input.GetKey(negative) ? 1 : 0);
    internal static Frame ReadCamera() => new Frame { Position = ToNumerics(GameCamera.main.transform.position), Rotation = ToNumerics(GameCamera.main.transform.rotation), Fov = GameCamera.main.fieldOfView };
    internal static NVector ToNumerics(Vector3 v) => new NVector(v.x, v.y, v.z);
    internal static NQuaternion ToNumerics(Quaternion q) => new NQuaternion(q.x, q.y, q.z, q.w);
    internal static Vector3 ToUnity(NVector v) => new Vector3(v.X, v.Y, v.Z);
    internal static Quaternion ToUnity(NQuaternion q) => new Quaternion(q.X, q.Y, q.Z, q.W);
}

internal static class CameraPatches
{
    private static bool Active => Plugin.Instance != null && Plugin.Instance.Active;

    [HarmonyPostfix, HarmonyPatch(typeof(VFInput), nameof(VFInput.OnUpdate))]
    private static void Input()
    {
        try { Plugin.Instance?.InputFrame(); }
        catch (Exception e) { Plugin.Instance?.Fail(e); }
    }

    [HarmonyPostfix, HarmonyPatch(typeof(VFInput), nameof(VFInput.UpdateGameStates))]
    private static void BlockGameInput()
    {
        if (Plugin.Instance == null || !Plugin.Instance.BlockInput) return;
        VFInput.inputing = true;
        VFInput.onGUI = VFInput.onGUIOperate = true;
        VFInput.readyToBuild = VFInput.readyToPlant = VFInput.readyToEnterBlueprintMode = false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(GameCamera), nameof(GameCamera.FrameLogic))]
    private static bool Camera()
    {
        if (!Active) return true;
        try { Plugin.Instance.CameraFrame(); }
        catch (Exception e) { Plugin.Instance.Fail(e); }
        return !Active;
    }

    [HarmonyPostfix, HarmonyPatch(typeof(UIGame), "_OnLateUpdate")]
    private static void Display() { if (Active) Plugin.Instance.ApplyDisplay(); }

    [HarmonyPostfix, HarmonyPatch(typeof(UICursor), nameof(UICursor.EndCursorDetermine))]
    private static void CursorDisplay() { if (Active && Plugin.Instance.CleanDisplay) Cursor.visible = false; }

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerController), "GetInput")]
    private static bool Movement(PlayerController __instance)
    {
        if (!Active) return true;
        __instance.input0 = __instance.input1 = Vector4.zero;
        return false;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerController), "HandleBaseInput")]
    private static bool Interaction() => !Active;

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerAction_Inspect), nameof(PlayerAction_Inspect.GameTick))]
    private static bool Inspect() => !Active;

    [HarmonyPrefix, HarmonyPatch(typeof(PlayerControlGizmo), nameof(PlayerControlGizmo.OnOutlineDraw))]
    private static bool Outline() => !Active;
}
