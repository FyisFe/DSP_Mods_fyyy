using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Security.Cryptography;
using System.Text;
using HarmonyLib;

namespace LogisticsProfiler
{
    internal static class DispatchDetails
    {
        internal static bool Enabled;
        [ThreadStatic] internal static long[] Active;

        // Instruction ordinals in DetermineDispatch, game MVID ece4a40e-5e73-43f4-a9f8-4e74970b5942.
        // A zero probe observes the existing stack value; it never re-reads mutable game state.
        // ponytail: this map supports one method body; remap and verify the signature when the game changes.
        private static readonly (int Instruction, string Name, bool Zero)[] Sites = {
            (142, "supply_visit", false), (510, "demand_visit", false),
            (158, "own_priority_lock", false), (526, "own_priority_lock", false),
            (279, "peer_priority_lock", false), (604, "peer_priority_lock", false),
            (463, "own_supply_short", false), (1122, "own_demand_empty", false),
            (457, "peer_demand_empty", false), (1116, "peer_supply_short", false),
            (262, "peer_missing", true), (587, "peer_missing", true),
            (334, "trip_check", false), (694, "trip_check", false),
            (367, "range_block", true), (727, "range_block", true),
            (736, "collector_block", false),
            (386, "warper_block", false), (756, "warper_block", false),
            (396, "ship_or_energy_block", false), (766, "ship_or_energy_block", false),
            (400, "flight_eligible", false), (770, "flight_eligible", false),
            (440, "trip_energy_short", false), (1099, "trip_energy_short", false),
            (429, "supply_attempt", false), (430, "supply_failed", true),
            (1088, "demand_attempt", false), (1089, "demand_failed", true),
            (782, "reverse_search", false), (817, "reverse_visit", false),
            (827, "reverse_match", false),
            (843, "reverse_own_lock", false), (868, "reverse_peer_lock", false),
            (1051, "reverse_supply_short", false), (1045, "reverse_demand_empty", false),
            (1031, "reverse_energy_short", false),
            (1010, "reverse_attempt", false), (1011, "reverse_failed", true)
        };
        internal static readonly string[] Names = Sites.Select(s => s.Name).Concat(new[] { "priority_lock_calls" }).Distinct().ToArray();

        internal static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
        {
            var code = instructions.ToList();
            if (!Enabled) return code;
            string signature = Signature(code);
            if (signature != "1ca813e1dac119109aab8572092832c81050e8484a67099d6738f96b925afe7b")
            {
                // A later mod may rebuild this patch chain. Stop our capture without rejecting its patch.
                LogisticsProfilerPlugin.Failure = new InvalidOperationException("Unsupported DetermineDispatch body for detailed profiling: " + signature);
                return code;
            }

            var trace = generator.DeclareLocal(typeof(long[]));
            var result = new List<CodeInstruction> {
                new CodeInstruction(OpCodes.Ldsfld, AccessTools.Field(typeof(DispatchDetails), nameof(Active))),
                new CodeInstruction(OpCodes.Stloc, trace)
            };
            var sites = Sites.ToDictionary(s => s.Instruction);
            var setLock = AccessTools.Method(typeof(StationComponent), "SetPriorityLock");
            for (int i = 0; i < code.Count; i++)
            {
                var instruction = code[i];
                bool lockCall = instruction.Calls(setLock);
                if (sites.TryGetValue(i, out var site) || lockCall)
                {
                    int metric = Array.IndexOf(Names, lockCall ? "priority_lock_calls" : site.Name);
                    var done = generator.DefineLabel();
                    var probe = new List<CodeInstruction>();
                    if (!lockCall && site.Zero)
                    {
                        probe.Add(new CodeInstruction(OpCodes.Dup));
                        probe.Add(new CodeInstruction(OpCodes.Brtrue, done));
                    }
                    probe.Add(new CodeInstruction(OpCodes.Ldloc, trace));
                    probe.Add(new CodeInstruction(OpCodes.Brfalse, done));
                    probe.Add(new CodeInstruction(OpCodes.Ldloc, trace));
                    probe.Add(new CodeInstruction(OpCodes.Ldc_I4, metric));
                    probe.Add(new CodeInstruction(OpCodes.Ldelema, typeof(long)));
                    probe.Add(new CodeInstruction(OpCodes.Dup));
                    probe.Add(new CodeInstruction(OpCodes.Ldind_I8));
                    probe.Add(new CodeInstruction(OpCodes.Ldc_I4_1));
                    probe.Add(new CodeInstruction(OpCodes.Conv_I8));
                    probe.Add(new CodeInstruction(OpCodes.Add));
                    probe.Add(new CodeInstruction(OpCodes.Stind_I8));
                    probe[0].labels.AddRange(instruction.labels);
                    instruction.labels.Clear();
                    // All mapped sites are outside exception-region boundaries.
                    if (instruction.blocks.Count != 0) throw new InvalidOperationException("Dispatch probe crosses an exception boundary.");
                    instruction.labels.Add(done);
                    result.AddRange(probe);
                }
                result.Add(instruction);
            }
            return result;
        }

        // Include branch destinations, operands and exception regions, not just opcode counts.
        // Label IDs vary with Harmony's wrapper; normalize them to instruction ordinals.
        private static string Signature(IList<CodeInstruction> code)
        {
            var labels = new Dictionary<Label, int>();
            for (int i = 0; i < code.Count; i++)
                foreach (var label in code[i].labels) labels.Add(label, i);
            var text = new StringBuilder();
            foreach (var instruction in code)
            {
                object operand = instruction.operand;
                // FieldInfo.ToString uses Int32 on CLR but System.Int32 on Unity Mono.
                string value = operand is Label label ? "target:" + labels[label] :
                    operand is LocalBuilder local ? "local:" + local.LocalIndex + ":" + local.LocalType :
                    operand is FieldInfo field ? field.DeclaringType + ":" + field.FieldType.FullName + " " + field.Name :
                    operand is MemberInfo member ? member.DeclaringType + ":" + member :
                    Convert.ToString(operand, CultureInfo.InvariantCulture);
                text.Append(instruction.opcode.Value).Append(':').Append(value).Append(';');
                foreach (var block in instruction.blocks) text.Append(block.blockType).Append(':').Append(block.catchType).Append(';');
                text.Append('\n');
            }
            using (var sha = SHA256.Create())
                return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", "").ToLowerInvariant();
        }
    }
}
