using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using FullPhotonReceiver;
using HarmonyLib;

internal static class Checks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private delegate long RequestGamma(ref PowerGeneratorComponent receiver,
        float sx, float sy, float sz, float increase, float eta);
    private delegate long SupplyGamma(ref PowerGeneratorComponent receiver, float response);

    private static void Main(string[] args)
    {
        Require(args.Length == 1, "Usage: Checks <game-managed-directory>");
        AppDomain.CurrentDomain.AssemblyResolve += (_, e) =>
        {
            string path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        Run();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Run()
    {
        // Delegates keep the native methods from being inlined before patching.
        var requestGamma = (RequestGamma)AccessTools.Method(typeof(PowerGeneratorComponent),
            nameof(PowerGeneratorComponent.EnergyCap_Gamma_Req)).CreateDelegate(typeof(RequestGamma));
        var supplyGamma = (SupplyGamma)AccessTools.Method(typeof(PowerGeneratorComponent),
            nameof(PowerGeneratorComponent.EnergyCap_Gamma)).CreateDelegate(typeof(SupplyGamma));
        // Synthetic lens IDs exercise different abilities without loading Unity's item database.
        ItemProto.catalystAbilityById = new float[12000];
        ItemProto.catalystAbilityById[1] = 2f;
        ItemProto.catalystAbilityById[2] = 4f;
        var cases = new List<(PowerGeneratorComponent Input, PowerGeneratorComponent Output,
            float Sun, float Response, long Grid)>();
        foreach (int product in new[] { 0, 1208 })
        foreach (float warmup in new[] { 0f, 0.5f, 1f })
        foreach (int lens in new[] { 0, 1, 2 })
        foreach (bool queued in new[] { false, true })
        foreach (byte inc in new byte[] { 0, 4, 10 })
        foreach (float response in new[] { 0f, 0.1f, 1f })
        foreach (float sun in new[] { -1f, 1f })
        {
            if (lens == 0 && (queued || inc != 0)) continue;
            var input = new PowerGeneratorComponent
            {
                gamma = true, productId = product, genEnergyPerTick = 100000, x = 1f,
                warmup = warmup, curCatalystId = lens, catalystIncLevel = inc,
                catalystPoint = lens != 0 && !queued ? 3600 : 0,
                catalystCount = (short)(lens != 0 && queued ? 1 : 0)
            };
            var output = input;
            requestGamma(ref output, 1f, 0f, 0f, 0f, 0.8f);
            long grid = supplyGamma(ref output, 1f);
            cases.Add((input, output, sun, response, grid));
        }

        var harmony = new Harmony("org.fyyy.fullphotonreceiver.checks");
        try
        {
            harmony.PatchAll(typeof(FullPhotonReceiverPlugin).GetNestedType("GammaPatches", BindingFlags.NonPublic));
            foreach (var c in cases)
            {
                var actual = c.Input;
                long request = requestGamma(ref actual, c.Sun, 0f, 0f, 0f, 0.8f);
                long grid = supplyGamma(ref actual, c.Response);
                Require(request == 0L, "sphere demand");
                Require(grid == c.Grid, "grid output");
                Require(actual.capacityCurrentTick == c.Output.capacityCurrentTick,
                    $"native full capacity: product={actual.productId}, lens={actual.curCatalystId}, queued={actual.catalystCount}, inc={actual.catalystIncLevel}, warmup={actual.warmup}, sun={c.Sun}, response={c.Response}; expected={c.Output.capacityCurrentTick}, actual={actual.capacityCurrentTick}");
                Require(actual.currentStrength == c.Output.currentStrength, "signal strength");
                Require(actual.warmupSpeed == c.Output.warmupSpeed, "warmup rate");
                Require(actual.warmup == c.Input.warmup && actual.catalystPoint == c.Input.catalystPoint &&
                    actual.catalystCount == c.Input.catalystCount, "capacity phase must not consume lenses or advance warmup");
            }
            Console.WriteLine($"PASS: {cases.Count} native receiver comparisons; game MVID {typeof(PowerGeneratorComponent).Module.ModuleVersionId}");
        }
        finally
        {
            harmony.UnpatchSelf();
        }
    }
}
