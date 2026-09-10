using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace InterstellarLogisticsOpt;

[HarmonyPatch(typeof(GalacticTransport), nameof(GalacticTransport.GameTick))]
internal static class DispatchScheduler
{
    internal static volatile int Factor = 1;
    internal static volatile bool AgeLocks = true;
    internal static string Failure;
    private static GalacticTransport _transport;
    private static long _lastTime;
    private static int _clock, _delay;

    internal static void Reset()
    {
        _transport = null;
        _clock = _delay = 0;
        AgeLocks = true;
    }

    [HarmonyPrefix, HarmonyPatch(typeof(GalacticTransport), nameof(GalacticTransport.Free))]
    private static void Free(GalacticTransport __instance)
    {
        if (_transport == __instance) Reset();
    }

    [HarmonyPrefix]
    private static bool Prefix(GalacticTransport __instance, ref long time)
    {
        int factor = Factor;
        if (!DispatchOptimization.Enabled || factor <= 1 || Failure != null || DispatchOptimization.Failure != null)
        {
            Reset();
            return true;
        }
        if (_transport != __instance || time != _lastTime + 1)
        {
            Reset();
            _transport = __instance;
        }
        _lastTime = time;
        if (_delay > 0)
        {
            --_delay;
            AgeLocks = false;
            return false;
        }
        // Native GameTick owns the complete ordered sweep. Only its dispatch clock changes;
        // factory transport workers age locks after the main-thread dispatch barrier.
        time = _clock;
        _clock = (_clock + 1) % 60;
        _delay = factor - 1;
        AgeLocks = true;
        return true;
    }
}

[HarmonyPatch(typeof(StationComponent), nameof(StationComponent.InternalTickRemote))]
internal static class PriorityClock
{
    [HarmonyTranspiler]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = instructions.ToList();
        var renderer = AccessTools.Method(typeof(StationComponent), nameof(StationComponent.ShipRenderersOnTick));
        int tail = code.FindIndex(c => c.Calls(renderer)) + 1;
        if (!DispatchOptimization.SupportedGame || tail <= 0 || tail >= code.Count || !code[tail].LoadsConstant(0) ||
            code[code.Count - 1].opcode != OpCodes.Ret || code[tail].blocks.Count != 0)
        {
            DispatchScheduler.Failure = "Unsupported InternalTickRemote body; amortized scheduling was not applied.";
            return code;
        }
        DispatchScheduler.Failure = null;
        var done = generator.DefineLabel();
        code[code.Count - 1].labels.Add(done);
        var gate = new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(DispatchScheduler), nameof(DispatchScheduler.AgeLocks)));
        gate.labels.AddRange(code[tail].labels);
        code[tail].labels.Clear();
        code.InsertRange(tail, new[] { gate, new CodeInstruction(OpCodes.Brfalse, done) });
        return code;
    }
}
