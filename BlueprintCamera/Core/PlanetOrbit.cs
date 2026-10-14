using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueprintCamera.Core;

public static class PlanetOrbit
{
    public static Shot Create(Frame view, int seed, int planetId, float radius, float aspect, float seconds, bool reverse, bool autoFrame)
    {
        Shot.Range(aspect, 0.1f, 10, "画面宽高比");
        var start = view.Copy();
        var radial = Vector3.Normalize(view.Position);
        if (autoFrame)
        {
            float halfFov = view.Fov * Path.Rad / 2;
            float halfAngle = Math.Min(halfFov, (float)Math.Atan(Math.Tan(halfFov) * aspect));
            // Fit only on entry or explicit request; accepting a manual view must retain its distance.
            start.Position = radial * (radius / (float)Math.Sin(halfAngle) * 1.12f);
        }
        start.Rotation = Quaternion.Normalize(Path.FromTo(Vector3.Transform(Vector3.UnitZ, view.Rotation),
            -radial, Vector3.Transform(Vector3.UnitY, view.Rotation)) * view.Rotation);
        start.Name = "起点";
        start.Time = 0;
        var shot = new Shot
        {
            Name = "星球环绕", Seed = seed, PlanetId = planetId, PlanetRadius = radius,
            Route = Route.PlanetOrbit, Aim = Aim.Target, Repeat = Repeat.Seamless,
            Duration = seconds, OrbitDegrees = reverse ? -360 : 360,
            StartHold = 0, EndHold = 0, EaseSeconds = 0,
            Target = radial * radius, Frames = new List<Frame> { start }
        };
        shot.Validate(true);
        return shot;
    }

    public static void Zoom(Shot shot, float steps)
    {
        var start = shot.Frames[0];
        float distance = start.Position.Length();
        // Normalization can round a vector beyond the serialized coordinate limit; leave a float margin.
        float next = Math.Max(shot.PlanetRadius + 0.2f, Math.Min(Shot.PositionLimit - 0.01f, distance * (float)Math.Pow(1.15, steps)));
        start.Position = Vector3.Normalize(start.Position) * next;
    }

    internal static Vector3 Axis(Frame start)
    {
        var radial = Vector3.Normalize(start.Position);
        var up = Vector3.Transform(Vector3.UnitY, start.Rotation);
        var axis = up - radial * Vector3.Dot(up, radial);
        // Advanced editing can aim straight along the radial up vector.
        if (axis.LengthSquared() < 1e-8f)
            axis = Vector3.Cross(radial, Math.Abs(radial.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
        return Vector3.Normalize(axis);
    }
}
