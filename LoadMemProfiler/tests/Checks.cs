using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using LoadMemProfiler;

internal static class Checks
{
    private static void Require(bool condition, string name)
    {
        if (!condition) throw new Exception(name);
    }

    private static void Main(string[] args)
    {
        var paths = new PathTotals();
        paths.Add(true, 100, 30, 100, 100, 72);
        paths.Add(false, 700, 0, 700, 700, 128);
        Require(paths.Active == 1 && paths.Inactive == 1 && paths.Used == 30, "live paths exclude recycled objects");
        Require(paths.ActiveSlackBytes == 70 * 29 && paths.InactiveBytes == 700 * 29 + 128, "slack and retained bytes stay separate");
        Require(paths.BufferBytes + paths.GeometryBytes + paths.AuxiliaryBytes == 800 * 29 + 200, "parallel array accounting");
        paths.Add(true, 2000000000, 1, 2000000000, 2000000000, 0);
        Require(paths.MaxSlackBytes == 1999999999L * 29, "byte counts do not overflow 32 bits");
        var cold = new PathTotals();
        cold.Add(true, 100, 100, 0, 0, 0);
        Require(cold.GeometryBytes == 0 && cold.ActiveSlackBytes == 0, "missing geometry is not counted as allocated");

        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        var frames = new FrameWindow();
        Require(frames.Columns(0).Split('\t')[3] == "-1.000", "empty percentile is unavailable");
        for (int i = 1; i <= 5000; i++) frames.Add(i);
        var values = frames.Columns(60).Split('\t');
        Require(values[0] == "5000" && values[1] == "4096", "bounded percentile window");
        Require(values[2] == "2500.500" && values[3] == "4796.000" && values[4] == "4960.000", "mean and nearest-rank percentiles");
        Require(values[5] == "5000.000" && values[6] == "60.000", "full-window max and invariant numbers");
        frames.Reset();
        frames.Add(3.5);
        Require(frames.Columns(0).Split('\t')[4] == "3.500", "reset discards the previous interval");
        Require(Tsv.Cell("a\tb\r\nc") == "a b  c", "labels cannot create TSV columns or rows");
        int visited = 0;
        var scan = SlowScan(() => visited++);
        Require(ScanSlice.Step(scan) && visited == 1, "an over-budget batch yields before touching the next batch");
        while (ScanSlice.Step(scan)) { }
        Require(visited == 3, "resumed scans visit every batch exactly once");
        Console.WriteLine("PASS: path accounting, 64-bit sizes, bounded frame percentiles, TSV formatting, scan time budget.");
        if (args.Length != 0) Bindings(args);
    }

    private static IEnumerator SlowScan(Action visit)
    {
        for (int i = 0; i < 3; i++)
        {
            Thread.Sleep(5); // Model a cold-page stall that exceeds the entire slice budget.
            visit();
            yield return null;
        }
    }

    private static void Bindings(string[] args)
    {
        Require(args.Length == 3, "binding check arguments: plugin.dll game-managed-dir bepinex-core-dir");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in args.Skip(1))
            {
                string file = Path.Combine(dir, name);
                if (File.Exists(file)) return Assembly.LoadFrom(file);
            }
            return null;
        };
        var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
        var snapshot = assembly.GetType("LoadMemProfiler.CapacitySnapshot", true);
        RuntimeHelpers.RunClassConstructor(snapshot.TypeHandle);
        int patches = 0;
        foreach (var method in assembly.GetType("LoadMemProfiler.Patches", true).GetMethods(BindingFlags.Static | BindingFlags.NonPublic))
        {
            var patch = method.GetCustomAttributesData().FirstOrDefault(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch");
            if (patch == null) continue;
            var type = (Type)patch.ConstructorArguments[0].Value;
            var name = (string)patch.ConstructorArguments[1].Value;
            var originals = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance).Where(m => m.Name == name).ToArray();
            Require(originals.Length == 1, "unique patch target: " + type.Name + "." + name);
            foreach (var arg in method.GetParameters())
            {
                if (arg.Name == "__result") Require(arg.ParameterType == originals[0].ReturnType, "patch result: " + method.Name);
                if (!arg.Name.StartsWith("__")) Require(originals[0].GetParameters().Any(p => p.Name == arg.Name && p.ParameterType == arg.ParameterType), "patch argument: " + method.Name);
            }
            patches++;
        }
        Console.WriteLine("PASS: capacity field accessors and " + patches + " patch signatures; game MVID " +
            Assembly.LoadFrom(Path.Combine(args[1], "Assembly-CSharp.dll")).ManifestModule.ModuleVersionId);
    }
}
