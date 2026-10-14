using System;
using System.Collections.Generic;
using System.Numerics;

namespace BlueprintCamera.Core;

// Immutable playback snapshot. Geometry and orientation tables are built only after edits.
public sealed class Path
{
    private readonly Shot shot;
    private readonly float[] parameters, distances;
    private readonly Quaternion[] rotations;
    private readonly Vector3 orbitCenter, orbitAxis;
    private Vector3 AimTarget => shot.Route == Route.PlanetOrbit ? Vector3.Zero : shot.Target;
    public float Length { get; }
    public bool BelowSurface { get; }
    public const float Rad = (float)(Math.PI / 180);

    public Path(Shot source)
    {
        shot = source.Copy();
        shot.Validate(true);
        foreach (var frame in shot.Frames) frame.Rotation = Quaternion.Normalize(frame.Rotation);
        if (shot.IsOrbit)
        {
            orbitCenter = AimTarget;
            orbitAxis = shot.Route == Route.PlanetOrbit ? PlanetOrbit.Axis(shot.Frames[0]) : Vector3.Normalize(shot.Target);
        }
        var points = new List<float> { 0 };
        // Include every knot; subdivide until both arc length and midpoint position converge.
        int segments = shot.IsOrbit ? Math.Max(8, (int)Math.Ceiling(Math.Abs(shot.OrbitDegrees) / 15)) : Math.Max(1, shot.Frames.Count - 1);
        for (int i = 0; i < segments; i++)
        {
            bool keyed = !shot.ConstantSpeed && !shot.IsOrbit && shot.Frames.Count > 1;
            Subdivide(points, keyed ? shot.Frames[i].Time : (float)i / segments,
                keyed ? shot.Frames[i + 1].Time : (float)(i + 1) / segments, 0);
        }
        parameters = points.ToArray();
        distances = new float[parameters.Length];
        rotations = new Quaternion[parameters.Length];
        Vector3 last = Position(0);
        Quaternion orientation = shot.Frames[0].Rotation;
        Vector3 lastForward = Vector3.Transform(Vector3.UnitZ, orientation);
        bool below = false;
        for (int i = 0; i < parameters.Length; i++)
        {
            Vector3 p = Position(parameters[i]);
            if (i > 0) distances[i] = distances[i - 1] + Vector3.Distance(last, p);
            if (p.Length() < shot.PlanetRadius + 0.1f) below = true;
            if (i > 0 && Vector3.DistanceSquared(last, p) > 1e-12f)
            {
                Vector3 chord = p - last;
                Vector3 nearest = last + chord * Clamp(-Vector3.Dot(last, chord) / chord.LengthSquared());
                if (nearest.Length() < shot.PlanetRadius + 0.1f) below = true;
            }
            if (i > 0 && shot.Aim == Aim.Target && Vector3.DistanceSquared(last, p) > 1e-12f)
            {
                Vector3 segment = p - last;
                Vector3 nearest = last + segment * Clamp(Vector3.Dot(AimTarget - last, segment) / segment.LengthSquared());
                if (Vector3.DistanceSquared(nearest, AimTarget) < 1e-6f)
                    throw new ArgumentException("路线经过注视目标，请调整机位或目标。");
            }
            if (shot.Aim == Aim.Target || shot.Aim == Aim.Travel)
            {
                Vector3 forward = Direction(parameters[i], p);
                // Parallel transport avoids pole/overhead flips and is independent of seek history.
                orientation = Quaternion.Normalize(FromTo(lastForward, forward, Vector3.Transform(Vector3.UnitY, orientation)) * orientation);
                lastForward = forward;
            }
            rotations[i] = orientation;
            last = p;
        }
        Length = shot.IsOrbit
            ? Math.Abs(shot.OrbitDegrees) * Rad * Vector3.Cross(orbitAxis, shot.Frames[0].Position - orbitCenter).Length()
            : distances[distances.Length - 1];
        BelowSurface = below;
        if (shot.Repeat == Repeat.Seamless)
        {
            Frame first = Evaluate(0), end = Evaluate(1);
            if (Vector3.Distance(first.Position, end.Position) > 0.01f || Math.Abs(Quaternion.Dot(first.Rotation, end.Rotation)) < 0.99999f || Math.Abs(first.Fov - end.Fov) > 0.01f)
                throw new ArgumentException("无缝循环要求首尾位置、朝向和视野一致。");
            Vector3 d0 = Position(0.0001f) - Position(0), d1 = Position(1) - Position(0.9999f);
            if (d0.LengthSquared() > 1e-10f && d1.LengthSquared() > 1e-10f && Vector3.Dot(Vector3.Normalize(d0), Vector3.Normalize(d1)) < 0.999f)
                throw new ArgumentException("无缝循环要求首尾移动方向连续。");
        }
    }

    private void Subdivide(List<float> table, float a, float b, int depth)
    {
        float mid = (a + b) / 2;
        Vector3 p = Position(a), q = Position(b), m = Position(mid);
        float chord = Vector3.Distance(p, q);
        // A minimum sampling density also resolves orientation along stationary/low-curvature paths.
        if (depth < 12 && (b - a > 1f / 256 || Vector3.Distance(m, (p + q) / 2) > Math.Max(0.00002f, chord * 0.0001f) || Vector3.Distance(p, m) + Vector3.Distance(m, q) - chord > 0.0005f))
        {
            Subdivide(table, a, mid, depth + 1);
            Subdivide(table, mid, b, depth + 1);
        }
        else table.Add(b);
    }

    public Frame Evaluate(float progress)
    {
        float t = Clamp(progress);
        // Circular motion is already distance-linear; lookup accumulation would introduce angular drift.
        if (shot.ConstantSpeed && !shot.IsOrbit && Length > 0.0001f)
        {
            float d = t * Length;
            int i = Interval(distances, d);
            float span = distances[i + 1] - distances[i];
            t = Mix(parameters[i], parameters[i + 1], span > 1e-8f ? (d - distances[i]) / span : 0);
        }
        Vector3 p = Position(t);
        Segment(t, out int segment, out float u);
        Frame a = shot.Frames[segment], b = shot.Frames[Math.Min(segment + 1, shot.Frames.Count - 1)];
        Quaternion rotation = Quaternion.Slerp(a.Rotation, b.Rotation, u);
        if (shot.IsOrbit) rotation = OrbitRotation(t) * shot.Frames[0].Rotation;
        if (shot.Aim == Aim.Fixed) rotation = shot.Frames[0].Rotation;
        if (shot.Aim == Aim.Ground)
            rotation = FromTo(Vector3.Normalize(shot.Frames[0].Position), Vector3.Normalize(p), Vector3.Transform(Vector3.UnitX, shot.Frames[0].Rotation)) * shot.Frames[0].Rotation;
        if (shot.Aim == Aim.Target || shot.Aim == Aim.Travel)
        {
            if (shot.IsOrbit)
            {
                Vector3 forward = Direction(0, Position(0));
                Quaternion start = FromTo(Vector3.Transform(Vector3.UnitZ, shot.Frames[0].Rotation), forward, Vector3.Transform(Vector3.UnitY, shot.Frames[0].Rotation)) * shot.Frames[0].Rotation;
                rotation = OrbitRotation(t) * start;
            }
            else
            {
                int i = Interval(parameters, t);
                rotation = Quaternion.Slerp(rotations[i], rotations[i + 1], (t - parameters[i]) / (parameters[i + 1] - parameters[i]));
                rotation = FromTo(Vector3.Transform(Vector3.UnitZ, rotation), Direction(t, p), Vector3.Transform(Vector3.UnitY, rotation)) * rotation;
            }
        }
        return new Frame { Position = p, Rotation = Quaternion.Normalize(rotation), Fov = shot.LockFov ? shot.Frames[0].Fov : Mix(a.Fov, b.Fov, u) };
    }

    public float ProgressAtFrame(int index)
    {
        float t = shot.Frames[index].Time;
        if (!shot.ConstantSpeed || Length < 0.0001f) return t;
        float parameter = (float)index / Math.Max(1, shot.Frames.Count - 1);
        if (shot.IsOrbit) return parameter;
        int i = Interval(parameters, parameter);
        return Mix(distances[i], distances[i + 1], (parameter - parameters[i]) / (parameters[i + 1] - parameters[i])) / Length;
    }

    private Vector3 Position(float t)
    {
        if (shot.Route == Route.Still) return shot.Frames[0].Position;
        if (shot.IsOrbit) return orbitCenter + Vector3.Transform(shot.Frames[0].Position - orbitCenter, OrbitRotation(t));
        Segment(t, out int i, out float u);
        Vector3 a = shot.Frames[i].Position, b = shot.Frames[i + 1].Position;
        if (shot.Route == Route.Surface)
        {
            Vector3 normalA = Vector3.Normalize(a), normalB = Vector3.Normalize(b);
            Quaternion arc = FromTo(normalA, normalB, Vector3.Transform(Vector3.UnitX, shot.Frames[i].Rotation));
            return Vector3.Transform(normalA, Quaternion.Slerp(Quaternion.Identity, arc, u)) * Mix(a.Length(), b.Length(), u);
        }
        if (shot.Route == Route.Curve)
        {
            Vector3 before = i == 0 ? 2 * a - b : shot.Frames[i - 1].Position;
            Vector3 after = i + 2 == shot.Frames.Count ? 2 * b - a : shot.Frames[i + 2].Position;
            Vector3 m0 = (b - before) / 2, m1 = (after - a) / 2;
            float u2 = u * u, u3 = u2 * u;
            return (2 * u3 - 3 * u2 + 1) * a + (u3 - 2 * u2 + u) * m0 + (-2 * u3 + 3 * u2) * b + (u3 - u2) * m1;
        }
        return Vector3.Lerp(a, b, u);
    }

    private Quaternion OrbitRotation(float t) => Quaternion.CreateFromAxisAngle(orbitAxis, shot.OrbitDegrees * Rad * t);

    private Vector3 Direction(float t, Vector3 position)
    {
        Vector3 d = shot.Aim == Aim.Target ? AimTarget - position : Position(Math.Min(1, t + 0.0001f)) - Position(Math.Max(0, t - 0.0001f));
        if (d.LengthSquared() < 1e-10f)
        {
            if (shot.Aim == Aim.Target) throw new ArgumentException("路线经过注视目标，无法确定朝向。");
            return Vector3.Transform(Vector3.UnitZ, shot.Frames[0].Rotation);
        }
        return Vector3.Normalize(d);
    }

    private void Segment(float t, out int index, out float local)
    {
        if (shot.Frames.Count == 1) { index = 0; local = 0; return; }
        if (shot.ConstantSpeed)
        {
            float scaled = t * (shot.Frames.Count - 1);
            index = Math.Min(shot.Frames.Count - 2, (int)scaled);
            local = scaled - index;
        }
        else
        {
            index = 0;
            while (index < shot.Frames.Count - 2 && t > shot.Frames[index + 1].Time) index++;
            local = (t - shot.Frames[index].Time) / (shot.Frames[index + 1].Time - shot.Frames[index].Time);
        }
    }

    private static int Interval(float[] values, float value)
    {
        int i = Array.BinarySearch(values, value);
        if (i < 0) i = ~i - 1;
        return Math.Max(0, Math.Min(values.Length - 2, i));
    }

    public static Quaternion FromTo(Vector3 from, Vector3 to, Vector3 fallback)
    {
        float dot = Vector3.Dot(from, to);
        Vector3 cross = Vector3.Cross(from, to);
        float sine = cross.Length();
        // atan2 preserves tiny arcs even when the float dot rounds to +1 or -1.
        if (sine > 1e-7f) return Quaternion.CreateFromAxisAngle(cross / sine, (float)Math.Atan2(sine, dot));
        if (dot >= 0) return Quaternion.Identity;
        Vector3 axis = fallback - from * Vector3.Dot(fallback, from);
        if (axis.LengthSquared() < 1e-8f) axis = Vector3.Cross(from, Math.Abs(from.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY);
        return Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)Math.PI);
    }

    public static float Clamp(float x) => Math.Max(0, Math.Min(1, x));
    private static float Mix(float a, float b, float t) => a + (b - a) * t;
}
