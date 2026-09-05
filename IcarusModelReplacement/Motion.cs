using System;

namespace IcarusModelReplacement;

internal enum Signal { Constant, Stride, Step, LeftStep, RightStep, Air, Sail }

internal struct Motion
{
    private float stride, air, sail;

    public static Motion Sample(double phase, float run, float air, float sail) => new Motion
    {
        stride = (float)Math.Sin(phase) * run * (1 - air), air = air, sail = sail
    };

    public float Get(Signal signal)
    {
        switch (signal)
        {
            case Signal.Constant: return 1;
            case Signal.Stride: return stride;
            case Signal.Step: return Math.Abs(stride);
            case Signal.LeftStep: return Math.Max(0, stride);
            case Signal.RightStep: return Math.Max(0, -stride);
            case Signal.Air: return air;
            case Signal.Sail: return sail;
            default: throw new ArgumentOutOfRangeException(nameof(signal));
        }
    }
}
