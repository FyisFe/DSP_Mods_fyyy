using System;

namespace BlueprintCamera.Core;

public enum Phase { Paused, Countdown, StartHold, Moving, EndHold, Finished }

public sealed class Playback
{
    private Shot shot;
    private double start, offset;
    private bool reverse;
    public bool Running { get; private set; }
    public float Progress { get; private set; }
    public Phase Phase { get; private set; }
    public double CountdownLeft { get; private set; }

    public void Start(Shot settings, double now, bool backwards, bool capture, float progress = 0)
    {
        shot = settings.Copy();
        reverse = backwards;
        start = now;
        offset = capture ? -shot.Countdown : shot.StartHold + InverseEase(backwards ? 1 - progress : progress) * shot.Duration;
        Running = true;
        Tick(now);
    }

    public void Tick(double now)
    {
        if (!Running) return;
        double t = Math.Max(0, now - start) + offset;
        CountdownLeft = Math.Max(0, -t);
        if (t < 0) { Phase = Phase.Countdown; Progress = reverse ? 1 : 0; return; }
        double cycle = shot.StartHold + shot.Duration + shot.EndHold;
        long lap = (long)Math.Floor(t / cycle);
        bool backwards = reverse;
        if (shot.Repeat != Repeat.Once)
        {
            t %= cycle;
            if (shot.Repeat == Repeat.PingPong && (lap & 1) != 0) backwards = !backwards;
        }
        else if (t >= cycle)
        {
            Running = false;
            Phase = Phase.Finished;
            Progress = reverse ? 0 : 1;
            return;
        }
        float u;
        if (t < shot.StartHold) { Phase = Phase.StartHold; u = 0; }
        else if (t >= shot.StartHold + shot.Duration) { Phase = Phase.EndHold; u = 1; }
        else { Phase = Phase.Moving; u = Ease((float)((t - shot.StartHold) / shot.Duration)); }
        Progress = backwards ? 1 - u : u;
    }

    public void Pause(double now) { Tick(now); Running = false; Phase = Phase.Paused; }
    public void Seek(float progress) { Running = false; Progress = Path.Clamp(progress); Phase = Phase.Paused; }

    // Integrate a trapezoidal speed profile. Ease=0 is exactly constant speed.
    private float Ease(float u)
    {
        float e = shot.EaseSeconds / shot.Duration;
        if (e == 0) return u;
        if (u < e) return u * u / (2 * e * (1 - e));
        if (u > 1 - e) return 1 - (1 - u) * (1 - u) / (2 * e * (1 - e));
        return (u - e / 2) / (1 - e);
    }

    private float InverseEase(float p)
    {
        float e = shot.EaseSeconds / shot.Duration;
        if (e == 0) return p;
        float edge = e / (2 * (1 - e));
        if (p < edge) return (float)Math.Sqrt(2 * e * (1 - e) * p);
        if (p > 1 - edge) return 1 - (float)Math.Sqrt(2 * e * (1 - e) * (1 - p));
        return p * (1 - e) + e / 2;
    }
}
