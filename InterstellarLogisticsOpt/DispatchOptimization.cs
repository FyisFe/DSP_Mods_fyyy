using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection.Emit;
using HarmonyLib;

namespace InterstellarLogisticsOpt;

[HarmonyPatch(typeof(StationComponent), nameof(StationComponent.DetermineDispatch))]
internal static class DispatchOptimization
{
    internal static readonly bool SupportedGame = typeof(StationComponent).Module.ModuleVersionId == new Guid("ece4a40e-5e73-43f4-a9f8-4e74970b5942");
    internal static volatile bool Enabled;
    internal static string Failure;

    // These branches only read peer storage to decide whether to refresh its priority lock.
    // Dispatch runs on the main thread before the FactoryBeforeTick barrier; transport
    // workers cannot decrement these locks until the later FactoryTransport phase.
    private static bool NeedsLock(StationComponent peer, int index, int priority)
    {
        if (priority == 0 || priority > 4) return false;
        ref var current = ref peer.priorityLocks[index];
        if (current.priorityIndex != 0 && current.priorityIndex < priority) return false;
        int ticks = priority == 1 ? 10 : priority == 2 || priority == 3 ? 30 : 60;
        return current.priorityIndex != priority || current.lockTick != ticks;
    }

    // Run after the diagnostic probes so they still see visits to the original branches.
    [HarmonyTranspiler, HarmonyAfter("org.fyyy.logisticsprofiler")]
    internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        var code = instructions.ToList();
        var storage = AccessTools.Field(typeof(StationComponent), nameof(StationComponent.storage));
        var sites = new List<int>();
        for (int i = 0; i + 8 < code.Count; i++)
        {
            // pool[pair.{supply,demand}Id] -> peer; null check; peer.storage.
            // The launch-capable paths inspect peer.priorityLocks before reading storage.
            if (code[i].IsLdarg(5) && code[i + 1].IsLdloc() &&
                (code[i + 2].LoadsField(AccessTools.Field(typeof(SupplyDemandPair), nameof(SupplyDemandPair.supplyId))) ||
                 code[i + 2].LoadsField(AccessTools.Field(typeof(SupplyDemandPair), nameof(SupplyDemandPair.demandId)))) &&
                code[i + 3].opcode == OpCodes.Ldelem_Ref && code[i + 4].IsStloc() &&
                code[i + 5].IsLdloc() && code[i + 6].opcode == OpCodes.Brfalse &&
                code[i + 7].IsLdloc() && code[i + 8].LoadsField(storage))
                sites.Add(i);
        }
        // ponytail: two verified lock-only branches; recheck game IL when this shape changes.
        if (!SupportedGame ||
            sites.Count != 2 || sites.Any(i => code[i + 7].blocks.Count != 0))
        {
            Failure = "Unsupported DetermineDispatch body; dispatch optimization was not applied.";
            return code;
        }
        Failure = null;
        var enabled = generator.DeclareLocal(typeof(bool));
        foreach (int i in sites.AsEnumerable().Reverse())
        {
            int entry = i + 7;
            var original = generator.DefineLabel();
            bool supply = code[i + 2].LoadsField(AccessTools.Field(typeof(SupplyDemandPair), nameof(SupplyDemandPair.demandId)));
            var guard = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldloc, enabled),
                new CodeInstruction(OpCodes.Brfalse, original),
                new CodeInstruction(code[entry].opcode, code[entry].operand),
                new CodeInstruction(code[i + 1].opcode, code[i + 1].operand),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(SupplyDemandPair), supply ? nameof(SupplyDemandPair.demandIndex) : nameof(SupplyDemandPair.supplyIndex))),
                new CodeInstruction(OpCodes.Ldarg_S, (byte)4),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DispatchOptimization), nameof(NeedsLock))),
                new CodeInstruction(OpCodes.Brfalse, code[i + 6].operand)
            };
            guard[0].labels.AddRange(code[entry].labels);
            code[entry].labels.Clear();
            code[entry].labels.Add(original);
            code.InsertRange(entry, guard);
        }
        code.InsertRange(0, new[] {
            new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(DispatchOptimization), nameof(Enabled))),
            new CodeInstruction(OpCodes.Stloc, enabled)
        });
        return code;
    }
}
