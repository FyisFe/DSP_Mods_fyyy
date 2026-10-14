using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.Serialization;

namespace BlueprintCamera.Core;

public enum Route { Still, Line, Orbit, Surface, Curve, PlanetOrbit }
public enum Aim { Keyframes, Fixed, Ground, Target, Travel }
public enum Repeat { Once, Restart, PingPong, Seamless }

[DataContract]
public sealed class Frame
{
    [DataMember] public string Name = "机位";
    [DataMember] public float X, Y, Z, Qx, Qy, Qz;
    [DataMember] public float Qw = 1;
    [DataMember] public float Fov = 60;
    [DataMember] public float Time;
    public Vector3 Position { get => new Vector3(X, Y, Z); set { X = value.X; Y = value.Y; Z = value.Z; } }
    public Quaternion Rotation { get => new Quaternion(Qx, Qy, Qz, Qw); set { Qx = value.X; Qy = value.Y; Qz = value.Z; Qw = value.W; } }
    public Frame Copy() => (Frame)MemberwiseClone();
}

[DataContract]
public sealed class Shot
{
    internal const float PositionLimit = 10000;
    [DataMember] public int Version = 1;
    [DataMember] public string Name = "新镜头";
    [DataMember] public int Seed, PlanetId;
    [DataMember] public float PlanetRadius = 200;
    [DataMember] public Route Route = Route.Line;
    [DataMember] public Aim Aim = Aim.Keyframes;
    [DataMember] public Repeat Repeat;
    [DataMember] public bool ConstantSpeed = true;
    [DataMember] public bool LockFov = true;
    [DataMember] public float Duration = 20;
    [DataMember] public float EaseSeconds;
    [DataMember] public float Countdown = 3;
    [DataMember] public float StartHold = 2, EndHold = 2;
    [DataMember] public float TargetX, TargetY = 200, TargetZ;
    [DataMember] public float OrbitDegrees = 180;
    [DataMember] public List<Frame> Frames = new List<Frame>();
    public Vector3 Target { get => new Vector3(TargetX, TargetY, TargetZ); set { TargetX = value.X; TargetY = value.Y; TargetZ = value.Z; } }
    public bool IsOrbit => Route == Route.Orbit || Route == Route.PlanetOrbit;

    public Shot Copy()
    {
        var shot = (Shot)MemberwiseClone();
        shot.Frames = Frames.ConvertAll(f => f.Copy());
        return shot;
    }

    public void Validate(bool playable)
    {
        if (Version != 1) throw new ArgumentException("不支持的镜头文件版本。");
        if (string.IsNullOrWhiteSpace(Name) || Name.Length > 100) throw new ArgumentException("镜头名称需要 1–100 个字符。");
        if (Seed < 0 || PlanetId <= 0) throw new ArgumentException("镜头缺少有效的星系和行星信息。");
        Range(PlanetRadius, 1, 10000, "行星半径");
        if (!Enum.IsDefined(typeof(Route), Route) || !Enum.IsDefined(typeof(Aim), Aim) || !Enum.IsDefined(typeof(Repeat), Repeat))
            throw new ArgumentException("镜头模式无效。");
        Range(Duration, 0.1f, 3600, "时长");
        Range(EaseSeconds, 0, Duration / 2, "缓入缓出");
        Range(Countdown, 0, 60, "倒计时");
        Range(StartHold, 0, 600, "起点停留");
        Range(EndHold, 0, 600, "终点停留");
        Range(OrbitDegrees, -3600, 3600, "环绕角度");
        CheckPosition(Target);
        if (Frames == null || Frames.Count > 128) throw new ArgumentException("每个镜头最多 128 个机位。");
        float previous = -1;
        foreach (Frame frame in Frames)
        {
            if (frame == null || frame.Name == null || frame.Name.Length > 100) throw new ArgumentException("机位名称无效。");
            CheckPosition(frame.Position);
            Range(frame.Rotation.LengthSquared(), 0.99f, 1.01f, "机位旋转");
            Range(frame.Fov, 10, 120, "视野");
            Range(frame.Time, 0, 1, "机位时间比例");
            if (frame.Time <= previous) throw new ArgumentException("机位时间必须按顺序递增。");
            previous = frame.Time;
        }
        if (!playable) return;
        int minimum = Route == Route.Still || IsOrbit ? 1 : 2;
        if (Frames.Count < minimum) throw new ArgumentException($"这条路线至少需要 {minimum} 个机位。");
        if (Frames.Count > 1 && (Frames[0].Time != 0 || Frames[Frames.Count - 1].Time != 1))
            throw new ArgumentException("首尾机位时间必须为 0% 和 100%。");
        if (ConstantSpeed && Route != Route.Still && !IsOrbit)
            for (int i = 1; i < Frames.Count; i++)
                if (Vector3.DistanceSquared(Frames[i - 1].Position, Frames[i].Position) < 1e-8f &&
                    (Math.Abs(Quaternion.Dot(Quaternion.Normalize(Frames[i - 1].Rotation), Quaternion.Normalize(Frames[i].Rotation))) < 0.99999f || Math.Abs(Frames[i - 1].Fov - Frames[i].Fov) > 0.001f))
                    throw new ArgumentException("同位置的转向或变焦请关闭匀速，使用手动时间。");
        if (IsOrbit && Math.Abs(OrbitDegrees) < 0.01f) throw new ArgumentException("环绕角度不能为零。");
        if (Route == Route.Orbit)
        {
            var offset = Frames[0].Position - Target;
            var up = Vector3.Normalize(Target);
            if (Math.Abs(OrbitDegrees) < 0.01f || (offset - up * Vector3.Dot(offset, up)).Length() < 0.1f)
                throw new ArgumentException("环绕需要非零角度，起点不能位于目标轴线上。");
        }
        if (Repeat == Repeat.Seamless && (StartHold != 0 || EndHold != 0 || EaseSeconds != 0))
            throw new ArgumentException("无缝循环需要关闭首尾停留和缓入缓出。");
    }

    public void SpaceTimes()
    {
        for (int i = 0; i < Frames.Count; i++) Frames[i].Time = Frames.Count == 1 ? 0 : (float)i / (Frames.Count - 1);
    }

    private static void CheckPosition(Vector3 p)
    {
        Range(p.LengthSquared(), 0.01f, PositionLimit * PositionLimit, "坐标");
    }

    public static void Range(float value, float min, float max, string name)
    {
        if (float.IsNaN(value) || float.IsInfinity(value) || value < min || value > max)
            throw new ArgumentException($"{name}必须在 {min:0.###}–{max:0.###} 之间。");
    }
}
