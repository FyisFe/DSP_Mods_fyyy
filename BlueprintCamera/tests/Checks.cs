using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using BlueprintCamera.Core;
using CameraPath = BlueprintCamera.Core.Path;

static class Checks
{
    static int assertions;
    static void Check(bool ok, string message) { assertions++; if (!ok) throw new Exception(message); }
    static void Near(float value, float expected, float tolerance, string message) => Check(Math.Abs(value - expected) <= tolerance, message + $": {value} != {expected}");
    static void Reject(Action action, string message)
    {
        bool rejected = false;
        try { action(); } catch (ArgumentException) { rejected = true; }
        Check(rejected, message);
    }
    static Shot Shot(Route route, params Vector3[] positions)
    {
        var shot = new Shot { Seed = 42, PlanetId = 101, Route = route, Target = new Vector3(0, 200, 0) };
        foreach (var p in positions) shot.Frames.Add(new Frame { Position = p });
        shot.SpaceTimes(); return shot;
    }
    static void Main()
    {
        Routes(); PlanetShots(); PlanetZoom(); Orientations(); Clock(); Files();
        Console.WriteLine($"PASS: {assertions} assertions: planet framing/orbits, distance speed, sphere/poles, rotations, seeking, playback boundaries, JSON and atomic storage.");
    }

    static void PlanetShots()
    {
        foreach (float aspect in new[] { 16f / 9, 9f / 16 })
        foreach (float radius in new[] { 80f, 200f, 500f })
        foreach (var radial in new[] { Vector3.UnitY, -Vector3.UnitY, Vector3.UnitX, Vector3.Normalize(new Vector3(2, 1, 3)) })
        {
            var view = new Frame { Position = radial * (radius + 10), Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.7f), Fov = 60 };
            var shot = PlanetOrbit.Create(view, 42, 101, radius, aspect, 60, false, true);
            var path = new CameraPath(shot);
            var start = path.Evaluate(0);
            float distance = start.Position.Length();
            Check(Vector3.Dot(Vector3.Normalize(start.Position), radial) > 0.99999f, "planet shot starts on current side");
            Near(view.Position.Length(), radius + 10, 0.001f, "auto framing does not mutate source pose");
            float silhouette = (float)Math.Asin(radius / distance);
            Check(silhouette < view.Fov * CameraPath.Rad / 2 && Math.Tan(silhouette) < Math.Tan(view.Fov * CameraPath.Rad / 2) * aspect,
                "entire sphere fits vertically and horizontally");
            Check(!path.BelowSurface, "planet orbit stays outside surface");
            var previous = start;
            float step = Vector3.Distance(start.Position, path.Evaluate(0.01f).Position);
            for (int i = 1; i <= 100; i++)
            {
                var frame = path.Evaluate(i / 100f);
                Near(frame.Position.Length(), distance, 0.002f, "constant planet distance");
                Check(Vector3.Dot(Vector3.Transform(Vector3.UnitZ, frame.Rotation), -Vector3.Normalize(frame.Position)) > 0.99999f, "always faces planet center");
                Near(Vector3.Distance(previous.Position, frame.Position) / step, 1, 0.001f, "uniform orbital speed");
                Check(Math.Abs(Quaternion.Dot(previous.Rotation, frame.Rotation)) > 0.999f, "no pole or seam flip");
                previous = frame;
            }
            Check(Vector3.Distance(start.Position, previous.Position) < 0.001f && Math.Abs(Quaternion.Dot(start.Rotation, previous.Rotation)) > 0.99999f, "seamless full circle");
            Check(Vector3.Dot(path.Evaluate(0.01f).Position - start.Position, Vector3.Transform(Vector3.UnitX, start.Rotation)) < 0, "positive angle moves camera left");
            var reverse = new CameraPath(PlanetOrbit.Create(view, 42, 101, radius, aspect, 60, true, true));
            Check(Vector3.Distance(reverse.Evaluate(0.25f).Position, path.Evaluate(0.75f).Position) < 0.002f, "reverse takes same circle backwards");
            var loaded = Store.Decode<BlueprintCamera.Core.Shot>(Store.Encode(shot));
            Check(loaded.Route == Route.PlanetOrbit && Vector3.Distance(new CameraPath(loaded).Evaluate(0.4f).Position, path.Evaluate(0.4f).Position) < 0.001f, "saved planet route round trip");
        }
        var loop = PlanetOrbit.Create(new Frame { Position = new Vector3(0, 220, 0) }, 42, 101, 200, 16f / 9, 60, false, true);
        var player = new Playback();
        player.Start(loop, 0, false, true);
        Check(player.Phase == Phase.Countdown, "simple capture counts down");
        player.Tick(18); Near(player.Progress, 0.25f, 0.00001f, "simple orbit starts after countdown without hold or ease");
        player.Tick(78); Near(player.Progress, 0.25f, 0.00001f, "simple orbit repeats without pause");
        Check(player.Running, "simple capture runs until stopped");
        var polar = loop.Copy();
        polar.Frames[0].Rotation = Quaternion.Identity;
        Check(!float.IsNaN(new CameraPath(polar).Evaluate(0.25f).X), "advanced radial-up pose has a finite orbit axis");
    }

    static void PlanetZoom()
    {
        var view = new Frame { Position = Vector3.Normalize(new Vector3(2, 1, 3)) * 900, Fov = 70 };
        var manual = PlanetOrbit.Create(view, 42, 101, 200, 16f / 9, 60, false, false);
        Check(manual.Frames[0].Position == view.Position, "accepting manual framing preserves distance and position");
        var fit = PlanetOrbit.Create(view, 42, 101, 200, 16f / 9, 60, false, true);
        Check(fit.Frames[0].Position.Length() < view.Position.Length(), "explicit auto framing can reset the distance");
        var original = new CameraPath(manual);
        PlanetOrbit.Zoom(manual, 3);
        manual.Validate(true);
        var zoomed = new CameraPath(manual);
        Check(zoomed.Evaluate(0).Position.Length() > view.Position.Length(), "zoom out increases orbit radius");
        foreach (float p in new[] { 0f, 0.37f, 0.8f, 1f })
        {
            var before = original.Evaluate(p);
            var after = zoomed.Evaluate(p);
            Check(Vector3.Dot(Vector3.Normalize(before.Position), Vector3.Normalize(after.Position)) > 0.99999f, "zoom preserves current orbital direction and phase");
            Check(Math.Abs(Quaternion.Dot(before.Rotation, after.Rotation)) > 0.99999f && before.Fov == after.Fov, "distance changes without changing aim or FOV");
        }
        var saved = Store.Decode<BlueprintCamera.Core.Shot>(Store.Encode(manual));
        var player = new Playback();
        player.Start(saved, 0, false, true);
        player.Tick(25);
        Near(new CameraPath(saved).Evaluate(player.Progress).Position.Length(), zoomed.Evaluate(0).Position.Length(), 0.001f, "capture and saved shots retain zoomed distance");
        manual.Duration = 120; manual.OrbitDegrees = -360;
        Near(new CameraPath(manual).Evaluate(0.42f).Position.Length(), zoomed.Evaluate(0).Position.Length(), 0.001f, "speed and direction changes retain zoom");
        PlanetOrbit.Zoom(manual, -3);
        Near(manual.Frames[0].Position.Length(), view.Position.Length(), 0.001f, "opposite zoom steps restore distance");
        PlanetOrbit.Zoom(manual, -100);
        manual.Validate(true);
        Check(!new CameraPath(manual).BelowSurface, "zoom in stops above planet surface");
        PlanetOrbit.Zoom(manual, 100);
        manual.Validate(true);
        Check(!new CameraPath(manual).BelowSurface, "zoom out remains a valid camera path at the coordinate limit");
        var random = new Random(42);
        for (int i = 0; i < 32; i++)
        {
            manual.Frames[0].Position = Vector3.Normalize(new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble())) * 900;
            PlanetOrbit.Zoom(manual, 100);
            manual.Validate(true);
        }
    }

    static void Routes()
    {
        var line = Shot(Route.Line, new Vector3(0, 220, 0), new Vector3(1, 220, 0), new Vector3(101, 220, 0));
        var path = new CameraPath(line);
        Near(path.Length, 101, 0.001f, "line length");
        for (int i = 0; i <= 100; i++) Near(path.Evaluate(i / 100f).X, 1.01f * i, 0.001f, "unequal legs use distance speed");
        Near(path.ProgressAtFrame(1), 1f / 101, 0.00001f, "key time uses distance");
        line.ConstantSpeed = false; line.Frames[1].Time = 0.9f;
        path = new CameraPath(line);
        Near(path.Evaluate(0.9f).X, 1, 0.001f, "manual key timing");
        Near(path.Evaluate(0.95f).X, 51, 0.001f, "manual timing final leg");

        var curve = Shot(Route.Curve, new Vector3(-70, 250, 0), new Vector3(-10, 220, 0), new Vector3(0, 225, 5), new Vector3(80, 270, 10));
        path = new CameraPath(curve);
        float step = path.Length / 4000;
        Vector3 prev = path.Evaluate(0).Position;
        float maxRelative = 0;
        for (int i = 1; i <= 4000; i++)
        {
            Vector3 now = path.Evaluate(i / 4000f).Position;
            maxRelative = Math.Max(maxRelative, Math.Abs(Vector3.Distance(prev, now) / step - 1));
            prev = now;
        }
        Check(maxRelative < 0.0025f, "curve local speed error <0.25%, actual " + maxRelative);
        for (int i = 0; i < curve.Frames.Count; i++) Check(Vector3.Distance(path.Evaluate(path.ProgressAtFrame(i)).Position, curve.Frames[i].Position) < 0.005f, "curve passes keys");
        Console.WriteLine("Curve maximum local distance-speed error: " + (maxRelative * 100).ToString("F3") + "%");

        foreach (var ends in new[] {
            new[] { new Vector3(20, 220, 0), new Vector3(-20, 220, 0) },
            new[] { new Vector3(-220, 0, 0.01f), new Vector3(-220, 0, -0.01f) },
            new[] { new Vector3(220, 0, 0), new Vector3(-220, 0, 0) } })
        {
            ends[0] = Vector3.Normalize(ends[0]) * 220; ends[1] = Vector3.Normalize(ends[1]) * 220;
            path = new CameraPath(Shot(Route.Surface, ends));
            for (int i = 0; i <= 200; i++) Near(path.Evaluate(i / 200f).Position.Length(), 220, 0.0002f, "sphere altitude across pole/seam/antipodes");
            Check(!path.BelowSurface, "surface path stays above sphere");
            Check(Vector3.Distance(path.Evaluate(1).Position, ends[1]) < 0.001f, "sphere endpoint");
        }
        Check(new CameraPath(Shot(Route.Line, new Vector3(220, 0, 0), new Vector3(-220, 0, 0))).BelowSurface, "chord crosses planet");
        Check(new CameraPath(Shot(Route.Line, new Vector3(-5000, 200.099f, 0), new Vector3(4990, 200.099f, 0))).BelowSurface, "near tangent crossing between samples");
        var orbit = Shot(Route.Orbit, new Vector3(30, 240, 0));
        orbit.OrbitDegrees = 360; orbit.Aim = Aim.Target; orbit.Repeat = Repeat.Seamless; orbit.StartHold = orbit.EndHold = 0;
        path = new CameraPath(orbit);
        Near(path.Length, 2 * (float)Math.PI * 30, 0.01f, "orbit circumference");
        Check(Vector3.Distance(path.Evaluate(0).Position, path.Evaluate(1).Position) < 0.001f, "closed orbit endpoint");
        orbit.OrbitDegrees = 180; Reject(() => new CameraPath(orbit), "nonclosed seamless refused");
        orbit.OrbitDegrees = 360; orbit.EaseSeconds = 1; Reject(() => new CameraPath(orbit), "ease breaks seamless constant speed");
    }

    static void Orientations()
    {
        var shot = Shot(Route.Line, new Vector3(-20, 240, 0), new Vector3(0, 240, 0), new Vector3(20, 240, 0));
        shot.Aim = Aim.Target;
        var path = new CameraPath(shot);
        var last = path.Evaluate(0).Rotation;
        for (int i = 0; i <= 400; i++)
        {
            float t = i / 400f;
            var frame = path.Evaluate(t);
            var dir = Vector3.Normalize(shot.Target - frame.Position);
            Check(Vector3.Dot(dir, Vector3.Transform(Vector3.UnitZ, frame.Rotation)) > 0.99999f, "aim exact through overhead");
            Check(Math.Abs(Quaternion.Dot(last, frame.Rotation)) > 0.999f, "no overhead flip");
            last = frame.Rotation;
            path.Evaluate(1 - t);
            Check(frame.Rotation == path.Evaluate(t).Rotation, "seek independent of history");
        }
        shot.Aim = Aim.Fixed;
        path = new CameraPath(shot);
        Check(path.Evaluate(0.73f).Rotation == shot.Frames[0].Rotation, "fixed means fixed orientation");
        shot.Route = Route.Surface; shot.Aim = Aim.Ground;
        shot.Frames[0].Rotation = CameraPath.FromTo(Vector3.UnitY, Vector3.Normalize(shot.Frames[0].Position), Vector3.UnitX);
        path = new CameraPath(shot);
        for (int i = 0; i <= 100; i++)
        {
            var f = path.Evaluate(i / 100f);
            Check(Vector3.Dot(Vector3.Transform(Vector3.UnitY, f.Rotation), Vector3.Normalize(f.Position)) > 0.99999f, "ground-relative up");
        }
        shot = Shot(Route.Still, new Vector3(0, 220, 0));
        path = new CameraPath(shot);
        Check(path.Evaluate(0).Position == path.Evaluate(1).Position, "stationary zero length");
        shot = Shot(Route.Line, new Vector3(0, 220, 0), new Vector3(10, 220, 0));
        shot.Frames[1].Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 90 * CameraPath.Rad);
        shot.Frames[1].Fov = 90; shot.LockFov = false;
        path = new CameraPath(shot);
        Near(path.Evaluate(0.5f).Fov, 75, 0.00001f, "FOV interpolation");
        Check(Math.Abs(Quaternion.Dot(path.Evaluate(0.5f).Rotation, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 45 * CameraPath.Rad))) > 0.99999f, "quaternion shortest arc");
        shot.Frames[1].Rotation = -shot.Frames[0].Rotation;
        path = new CameraPath(shot);
        Check(Math.Abs(Quaternion.Dot(path.Evaluate(0.5f).Rotation, shot.Frames[0].Rotation)) > 0.99999f, "equivalent quaternion sign");
        shot = Shot(Route.Line, new Vector3(-3, 220, 0), new Vector3(7, 220, 0));
        shot.Target = new Vector3(0, 220, 0); shot.Aim = Aim.Target;
        Reject(() => new CameraPath(shot), "reject a target crossing between samples");
        shot.Aim = Aim.Keyframes; shot.Frames[1].Position = shot.Frames[0].Position;
        shot.Frames[1].Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, 1);
        Reject(() => new CameraPath(shot), "in-place rotation requires timed keys");
        shot.ConstantSpeed = false;
        path = new CameraPath(shot);
        Check(Math.Abs(Quaternion.Dot(path.Evaluate(0.5f).Rotation, Quaternion.CreateFromAxisAngle(Vector3.UnitY, 0.5f))) > 0.99999f, "timed in-place turn");
    }

    static void Clock()
    {
        var s = Shot(Route.Line, new Vector3(0, 220, 0), new Vector3(100, 220, 0));
        var p = new Playback();
        p.Start(s, 100, false, true);
        Check(p.Phase == Phase.Countdown && p.CountdownLeft == 3 && p.Progress == 0, "countdown start");
        p.Tick(103); Check(p.Phase == Phase.StartHold, "start hold boundary");
        p.Tick(105); Check(p.Phase == Phase.Moving, "move boundary");
        p.Tick(115); Near(p.Progress, 0.5f, 0.00001f, "real time progress");
        p.Tick(125); Check(p.Phase == Phase.EndHold && p.Progress == 1, "end hold boundary");
        p.Tick(127); Check(p.Phase == Phase.Finished && !p.Running, "finish boundary");
        p.Start(s, 200, true, true); p.Tick(215); Near(p.Progress, 0.5f, 0.00001f, "reverse");
        p.Tick(227); Near(p.Progress, 0, 0, "reverse end");
        for (int fps = 24; fps <= 144; fps += 30)
        {
            p.Start(s, 0, false, true);
            for (int i = 1; i <= fps * 15; i++) p.Tick((double)i / fps);
            Near(p.Progress, 0.5f, 0.00001f, "FPS independent");
        }
        s.EaseSeconds = 2;
        for (int i = 0; i <= 100; i++)
        {
            float v = i / 100f;
            p.Start(s, 10, false, false, v);
            Near(p.Progress, v, 0.00001f, "ease seek inverse");
            p.Pause(10); float frozen = p.Progress; p.Tick(99); Near(p.Progress, frozen, 0, "paused frozen");
        }
        s.EaseSeconds = 0; s.Countdown = 0; s.Repeat = Repeat.PingPong;
        p.Start(s, 0, false, true); p.Tick(24 + 2 + 5); Near(p.Progress, 0.75f, 0.00001f, "pingpong second leg");
        s.Repeat = Repeat.Restart;
        p.Start(s, 0, false, true); p.Tick(24000 + 2 + 5); Near(p.Progress, 0.25f, 0.00001f, "large clock step looping");
        p.Seek(0.4f); Check(!p.Running && p.Progress == 0.4f, "scrub stops playback");
    }

    static void Files()
    {
        var s = Shot(Route.Curve, new Vector3(0, 220, 0), new Vector3(20, 220, 0));
        s.Name = "生产线 \"A\"\n测试";
        s.Frames[0].Name = "中文 & <>";
        s.Frames[0].Rotation = Quaternion.CreateFromAxisAngle(Vector3.Normalize(new Vector3(1, 2, 3)), 1.3f);
        var loaded = Store.Decode<Shot>(Store.Encode(s)); loaded.Validate(true);
        Check(Store.Encode(loaded) == Store.Encode(s), "shot roundtrip");
        var lib = new Library(); lib.Shots.Add(s);
        string folder = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "BlueprintCamera-checks-" + Guid.NewGuid().ToString("N"));
        string file = System.IO.Path.Combine(folder, "library.json");
        try
        {
            Store.Save(file, lib); var first = Store.Load(file);
            Check(first.Shots[0].Name == s.Name, "unicode persistent library");
            s.Name = "更新"; Store.Save(file, lib);
            Check(Store.Load(file).Shots[0].Name == "更新" && !File.Exists(file + ".tmp"), "atomic replacement");
            string before = File.ReadAllText(file);
            s.Duration = float.NaN; Reject(() => Store.Save(file, lib), "reject nonfinite");
            Check(File.ReadAllText(file) == before, "invalid save preserves original bytes");
        }
        finally { if (Directory.Exists(folder)) Directory.Delete(folder, true); }
        Reject(() => Store.Decode<Shot>(new string(' ', Store.MaxBytes + 1)), "oversize import");
        s.Duration = 20; s.Version = 2; Reject(() => s.Validate(false), "future version");
        s.Version = 1; s.Frames[0].Qw = 0; Reject(() => s.Validate(false), "zero quaternion");
        s.Frames[0].Qw = 1; s.Frames[1].Time = 0; Reject(() => s.Validate(false), "duplicate times");
        s.Frames[1].Time = 1; s.Frames[1].X = float.PositiveInfinity; Reject(() => s.Validate(false), "nonfinite coordinate");
    }
}
