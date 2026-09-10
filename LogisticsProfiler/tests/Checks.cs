using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using LogisticsProfiler;
using InterstellarLogisticsOpt;

internal static class Checks
{
    private static void Require(bool condition, string message)
    {
        if (!condition) throw new Exception(message);
    }

    private static void Main(string[] args)
    {
        Require(args.Length == 2 || (args.Length == 3 && args[2] == "--dispatch-only"), "Usage: Checks <game-managed-dir> <bepinex-core-dir> [--dispatch-only]");
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach (string dir in args.Take(2))
            {
                string path = Path.Combine(dir, name);
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        Measurements();
        if (args.Length == 2) GameChecks();
        else DispatchDetails.Enabled = true;
        DispatchChecks();
        PriorityClockChecks(args.Length == 2);
        SchedulerChecks();
    }

    private static void Measurements()
    {
        var capture = new Capture(1);
        Parallel.For(0, 4, worker =>
        {
            for (int i = 0; i < 1000; i++)
            {
                var sample = new Sample { Kind = 2, Gid = worker, Planet = 101, Pairs = 3000000, NoIdle = true, WorkingShips = 10 };
                sample.Details = new long[DispatchDetails.Names.Length];
                sample.Details[0] = 3000000;
                Require(capture.Begin(ref sample), "open capture accepts samples");
                capture.End(sample, 100, false, false, 0);
            }
        });
        var last = new Sample { Kind = 7, Pairs = -1 };
        Require(capture.Begin(ref last) && !capture.Stop(), "stop waits for in-flight callbacks");
        var late = new Sample();
        Require(!capture.Begin(ref late), "stopped capture rejects new callbacks");
        capture.End(last, 200, true, true, 0);
        Require(capture.Stop(), "in-flight callback drains after stop");
        Require(capture.Methods[2].Samples == 4000 && capture.Stations.Count == 4, "concurrent totals and station grouping");
        Require(capture.Methods[2].Pairs == 12000000000L && capture.Methods[2].Skipped == 4000, "64-bit pair counts and skipped originals");
        Require(capture.Methods[2].Details[0] == 12000000000L, "64-bit concurrent branch aggregation");
        Require(capture.Methods[2].NoIdleTicks == 400000 && capture.Methods[2].NoLaunchTicks == 400000, "blocked-call time buckets");
        Require(capture.Methods[7].Errors == 1, "failed method included");
        Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
        Require(Totals.Number(1.25) == "1.250000" && Totals.Cell("a\tb\nc") == "a b c", "invariant TSV formatting");
        Require(new Totals().Columns(false, false).Split('\t')[2] == "-1.000000", "empty timing unavailable");
        Require(capture.Methods[7].Columns(true, false).Split('\t')[8] == "-1", "flight calls do not report pair counts");
        Console.WriteLine("PASS: concurrent accounting, stop/drain, 64-bit sums, TSV and unavailable metrics.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void GameChecks()
    {
        // Real game method: a shipless demand station still locks an empty priority supplier.
        // Two pairs make the round-robin cursor advance observable as well.
        var station = new StationComponent
        {
            id = 1, gid = 1, planetId = 101, idleShipCount = 0, energy = 10000000,
            deliveryShips = 100, storage = new[] { new StationStore { max = 1000, itemId = 1001 } },
            priorityLocks = new StationPriorityLock[1], remotePairProcesses = new int[6],
            remotePairOffsets = new[] { 0, 0, 2, 2, 2, 2, 2 },
            remotePairs = new[] { new SupplyDemandPair(2, 0, 1, 0), new SupplyDemandPair(3, 0, 1, 0) }
        };
        var supplier = new StationComponent
        {
            gid = 2, storage = new[] { new StationStore { max = 1000, itemId = 1001 } },
            priorityLocks = new StationPriorityLock[1]
        };
        var supplier2 = new StationComponent
        {
            gid = 3, storage = new[] { new StationStore { max = 1000, itemId = 1001 } },
            priorityLocks = new StationPriorityLock[1]
        };
        var planet = new PlanetData { id = 101, factoryIndex = 0 };
        var galaxy = new GalaxyData { stars = new[] { new StarData { planets = new[] { planet } } } };
        var power = new PowerSystem(planet, true)
        {
            networkServes = new[] { 1f }, consumerPool = new PowerConsumerComponent[1]
        };
        var factories = new[] { new PlanetFactory { powerSystem = power } };
        var stats = new[] { new FactoryProductionStat { consumeRegister = new int[1] } };
        var pool = new[] { (StationComponent)null, station, supplier, supplier2 };
        station.DetermineDispatch(100, 1000, 100, 1, pool, stats, factories, galaxy, null);
        Require(station.idleShipCount == 0 && supplier.priorityLocks[0].lockTick == 10 && supplier2.priorityLocks[0].lockTick == 10,
            "vanilla writes other stations' priority locks even with no idle ships");
        Require(station.remotePairProcesses[1] == 1, "shipless scan advances pair cursor");
        supplier.priorityLocks[0] = default;
        station.idleShipCount = 1;
        station.energy = 6000000;
        station.DetermineDispatch(100, 1000, 100, 1, pool, stats, factories, galaxy, null);
        Require(supplier.priorityLocks[0].lockTick == 10, "low-energy scan also has observable side effects");
        Console.WriteLine("PASS: vanilla shipless/low-energy dispatch writes priority locks; cursor advancement reproduced.");

        var profiler = new Harmony("logisticsprofiler.checks");
        var skipper = new Harmony(LogisticsProfilerPlugin.OptGuid);
        DispatchDetails.Enabled = true;
        try
        {
            foreach (var type in new[] { typeof(SchedulerPatch), typeof(DispatchPatch), typeof(RemotePatch) })
                profiler.CreateClassProcessor(type).Patch();
            var method = AccessTools.Method(typeof(StationComponent), "DetermineDispatch");
            skipper.Patch(method, prefix: new HarmonyMethod(typeof(Checks), nameof(Skip)));
            var capture = new Capture(1);
            LogisticsProfilerPlugin.Current = capture;
            station.idleShipCount = 0;
            station.DetermineDispatch(100, 1000, 100, 1, null, null, null, null, null);
            Require(capture.Methods[2].Samples == 1 && capture.Methods[2].Skipped == 1, "profiler observes a later false prefix");
            Require(capture.Methods[2].NoIdle == 1 && capture.Methods[2].Pairs == 2, "entry-state sample survives skipped body");
            Require(capture.Methods[2].Details.All(n => n == 0), "skipped original visits no dispatch branches");
            skipper.UnpatchSelf();
            station.remotePairOffsets = null;
            station.DetermineDispatch(100, 1000, 100, 1, null, null, null, null, null);
            Require(capture.Methods[2].Samples == 2 && capture.Methods[2].Skipped == 1 && capture.Methods[2].EmptyPairs == 1,
                "a normally completed original is counted separately from a skipped one");
            station.remotePairOffsets = new int[0];
            bool threw = false;
            try { station.DetermineDispatch(100, 1000, 100, 1, null, null, null, null, null); }
            catch (IndexOutOfRangeException) { threw = true; }
            Require(threw && capture.Methods[2].Errors == 1, "finalizer counts but does not suppress a game exception");
            Require(DispatchDetails.Active == null, "exception clears thread-local dispatch trace");
            LogisticsProfilerPlugin.Current = null;
            Require(capture.Stop(), "exception releases pending sample");
            Require(LogisticsProfilerPlugin.Failure == null, "no diagnostic failure");
            Console.WriteLine("PASS: all three real Harmony bindings, prefix ordering, skipped originals and exception propagation.");
        }
        finally
        {
            LogisticsProfilerPlugin.Current = null;
            skipper.UnpatchSelf();
            LogisticsProfilerPlugin.Unpatch(profiler);
        }
        Console.WriteLine("Game MVID: " + typeof(StationComponent).Module.ModuleVersionId);
    }

    private static bool Skip() => false;

    private static Func<StationComponent, int> _nativeAge, _patchedAge, _returnBound;

    private static Func<StationComponent, int> CompileBlock(List<CodeInstruction> code)
    {
        var method = new DynamicMethod("priority_block", typeof(int), new[] { typeof(StationComponent) }, typeof(Checks).Module, true);
        var il = method.GetILGenerator();
        var labels = code.SelectMany(c => c.labels).Distinct().ToDictionary(l => l, _ => il.DefineLabel());
        var locals = new Dictionary<int, LocalBuilder>();
        foreach (var instruction in code)
        {
            foreach (var label in instruction.labels) il.MarkLabel(labels[label]);
            object operand = instruction.operand;
            if (operand is LocalBuilder local)
            {
                if (!locals.TryGetValue(local.LocalIndex, out var mapped)) locals.Add(local.LocalIndex, mapped = il.DeclareLocal(local.LocalType));
                il.Emit(instruction.opcode, mapped);
            }
            else if (operand is Label label) il.Emit(instruction.opcode, labels[label]);
            else if (operand is FieldInfo field) il.Emit(instruction.opcode, field);
            else if (operand is MethodInfo called) il.Emit(instruction.opcode, called);
            else if (operand is Type type) il.Emit(instruction.opcode, type);
            else if (operand is byte number) il.Emit(instruction.opcode, number);
            else if (operand is int value) il.Emit(instruction.opcode, value);
            else if (operand == null) il.Emit(instruction.opcode);
            else throw new Exception("Unsupported priority-block operand: " + operand);
        }
        return (Func<StationComponent, int>)method.CreateDelegate(typeof(Func<StationComponent, int>));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void PriorityClockChecks(bool fullBindings)
    {
        var remote = AccessTools.Method(typeof(StationComponent), "InternalTickRemote");
        var renderer = AccessTools.Method(typeof(StationComponent), "ShipRenderersOnTick");
        foreach (bool patched in new[] { false, true })
        {
            var code = PatchProcessor.GetOriginalInstructions(remote);
            if (patched)
                code = PriorityClock.Transpiler(code, new DynamicMethod("clock", typeof(void), Type.EmptyTypes).GetILGenerator()).ToList();
            Require(DispatchScheduler.Failure == null, "supported flight method accepts priority clock");
            int tailStart = code.FindIndex(c => c.Calls(renderer)) + 1;
            var tail = code.Skip(tailStart).Select(c => new CodeInstruction(c)).ToList();
            var zero = new CodeInstruction(OpCodes.Ldc_I4_0);
            zero.labels.AddRange(tail[tail.Count - 1].labels);
            tail[tail.Count - 1].labels.Clear();
            tail.Insert(tail.Count - 1, zero);
            if (patched) _patchedAge = CompileBlock(tail); else _nativeAge = CompileBlock(tail);
            if (patched)
            {
                int start = code.FindIndex(c => c.LoadsField(AccessTools.Field(typeof(StationComponent), "routePriority"))) - 1;
                int firstStore = code.FindIndex(start, c => c.IsStloc());
                object bound = code.Skip(firstStore + 1).First(c => c.IsStloc()).operand;
                int end = code.FindLastIndex(c => c.IsStloc() && Equals(c.operand, bound)) + 1;
                var selection = code.Skip(start).Take(end - start).Select(c => new CodeInstruction(c)).ToList();
                var result = new CodeInstruction(OpCodes.Ldloc, bound);
                result.labels.AddRange(code[end].labels);
                selection.Add(result);
                selection.Add(new CodeInstruction(OpCodes.Ret));
                _returnBound = CompileBlock(selection);
            }
        }
        foreach (bool age in new[] { false, true })
        foreach (ERemoteRoutePriority route in Enum.GetValues(typeof(ERemoteRoutePriority)))
        {
            DispatchScheduler.AgeLocks = age;
            int maximum = route == ERemoteRoutePriority.Ignore ? 0 : route == ERemoteRoutePriority.Prioritize ? 5 : 4;
            Require(_returnBound(new StationComponent { routePriority = route }) == maximum,
                "return cargo retains native priority selection on both dispatch and waiting ticks");
        }
        var native = new StationComponent { priorityLocks = new[] { new StationPriorityLock { priorityIndex = 4, lockTick = 60 } } };
        var observed = new StationComponent { priorityLocks = (StationPriorityLock[])native.priorityLocks.Clone() };
        for (int tick = 0; tick < 1831; tick++)
        {
            DispatchScheduler.AgeLocks = tick % 30 == 0;
            if (DispatchScheduler.AgeLocks) _nativeAge(native);
            _patchedAge(observed);
            Require(native.priorityLocks[0].priorityIndex == observed.priorityLocks[0].priorityIndex && native.priorityLocks[0].lockTick == observed.priorityLocks[0].lockTick,
                "native lock aging at factor 30 preserves byte countdown and zero/clear boundary");
        }
        var changed = PatchProcessor.GetOriginalInstructions(remote);
        int agingIndex = changed.FindIndex(c => c.Calls(renderer)) + 1;
        changed[agingIndex] = new CodeInstruction(OpCodes.Ldc_I4_1);
        Require(PriorityClock.Transpiler(changed, null).SequenceEqual(changed) && DispatchScheduler.Failure != null, "unsupported aging block leaves the native body intact");
        DispatchScheduler.Failure = null;
        DispatchScheduler.Reset();
        if (fullBindings)
        {
            var opt = new Harmony(LogisticsProfilerPlugin.OptGuid);
            var profiler = new Harmony("org.fyyy.logisticsprofiler");
            try
            {
                opt.CreateClassProcessor(typeof(PriorityClock)).Patch();
                profiler.CreateClassProcessor(typeof(RemotePatch)).Patch();
                Require(DispatchScheduler.Failure == null, "full flight-method patch binding with profiler");
            }
            finally { opt.UnpatchSelf(); LogisticsProfilerPlugin.Unpatch(profiler); }
        }
        Console.WriteLine("PASS: native return selection, scaled lock-aging IL, factor-30 byte limits, suspended aging, compatibility rejection and available full-method bindings.");
    }

    private static long _schedulerTime;
    private static bool _throwDispatch;
    private static readonly List<(long Tick, int Priority, int Gid, float Sail, float Warp, int Carries)> Schedule = new List<(long, int, int, float, float, int)>();

    private static bool RecordDispatch(StationComponent __instance, int priorityIndex, float shipSailSpeed, float shipWarpSpeed, int shipCarries)
    {
        Schedule.Add((_schedulerTime, priorityIndex, __instance.gid, shipSailSpeed, shipWarpSpeed, shipCarries));
        if (_throwDispatch) { _throwDispatch = false; throw new InvalidOperationException("dispatch witness"); }
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void SchedulerChecks()
    {
        var data = new GameData {
            history = new GameHistoryData { logisticShipSailSpeed = 100, logisticShipWarpSpeed = 1000, logisticShipSpeedScale = 2, logisticShipCarries = 200, logisticShipWarpDrive = true },
            statistics = new GameStatData { production = new ProductionStatistics() }
        };
        var transport = new GalacticTransport { gameData = data, stationPool = new StationComponent[75], stationCursor = 74 };
        for (int gid = 1; gid < transport.stationPool.Length; gid++)
            transport.stationPool[gid] = new StationComponent { id = gid, gid = gid, routePriority = (ERemoteRoutePriority)(1 + gid % 4) };
        transport.stationPool[12] = null;
        transport.stationPool[17].id = 0;
        transport.stationPool[23].gid = 24;
        var recorder = new Harmony("logisticsprofiler.schedule-checks");
        var opt = new Harmony(LogisticsProfilerPlugin.OptGuid);
        var profiler = new Harmony("org.fyyy.logisticsprofiler");
        try
        {
            recorder.Patch(AccessTools.Method(typeof(StationComponent), "DetermineDispatch"), prefix: new HarmonyMethod(typeof(Checks), nameof(RecordDispatch)));
            Action<int> run = scale => {
                Schedule.Clear();
                DispatchScheduler.Reset();
                var native = new StationComponent { priorityLocks = new[] { new StationPriorityLock { priorityIndex = 4, lockTick = 60 } } };
                var observed = new StationComponent { priorityLocks = (StationPriorityLock[])native.priorityLocks.Clone() };
                for (_schedulerTime = 0; _schedulerTime < 1800; _schedulerTime++)
                {
                    transport.GameTick(_schedulerTime);
                    Require(DispatchScheduler.AgeLocks == (_schedulerTime % scale == 0), "lock aging and complete native dispatch share a clock");
                    if (_schedulerTime % scale == 0) _nativeAge(native);
                    _patchedAge(observed);
                    Require(native.priorityLocks[0].Equals(observed.priorityLocks[0]), "scheduled native lock countdown matches scaled reference");
                }
            };
            run(1);
            var vanilla = Schedule.ToArray();
            opt.CreateClassProcessor(typeof(DispatchScheduler)).Patch();
            profiler.CreateClassProcessor(typeof(SchedulerPatch)).Patch();
            foreach (bool enabled in new[] { false, true })
            foreach (int factor in new[] { 1, 2, 5, 30 })
            {
                DispatchOptimization.Enabled = enabled;
                DispatchScheduler.Factor = factor;
                var capture = new Capture(1);
                LogisticsProfilerPlugin.Current = capture;
                int scale = enabled ? factor : 1;
                run(scale);
                LogisticsProfilerPlugin.Current = null;
                Require(capture.Stop() && capture.Methods[0].Samples == 1800 && capture.Methods[0].Errors == 0, "scheduler instrumentation drains");
                Require(capture.Methods[0].Skipped == 1800 - 1800 / scale, "profiler observes skipped waiting ticks and executed native sweeps");
                Require(capture.Phases.All(p => p.Samples == 30), "profiler phases use simulation time rather than the dispatch clock");
                var expected = vanilla.Where(v => v.Tick * scale < 1800).Select(v => (v.Tick * scale, v.Priority, v.Gid, v.Sail, v.Warp, v.Carries));
                Require(expected.SequenceEqual(Schedule), "all routes keep scaled native cadence, global order and dispatch arguments");
            }
            DispatchScheduler.Reset();
            DispatchScheduler.Factor = 5;
            Schedule.Clear();
            _throwDispatch = true;
            bool threw = false;
            var failed = new Capture(1);
            LogisticsProfilerPlugin.Current = failed;
            try { transport.GameTick(1600); } catch (InvalidOperationException) { threw = true; }
            LogisticsProfilerPlugin.Current = null;
            Require(threw && failed.Stop() && failed.Methods[0].Errors == 1, "native dispatch exception propagates and releases profiler sample");
            DispatchScheduler.Reset();
            transport.GameTick(1700);
            transport.GameTick(1701);
            Require(!DispatchScheduler.AgeLocks, "factor change starts between logical ticks");
            DispatchScheduler.Factor = 1;
            Schedule.Clear();
            transport.GameTick(1800);
            Require(Schedule.Count == vanilla.Count(v => v.Tick == 0) && DispatchScheduler.AgeLocks, "factor one restores native scheduling and aging");
            DispatchScheduler.Factor = 30;
            Schedule.Clear();
            data.history.logisticShipWarpDrive = false;
            transport.GameTick(10);
            Require(Schedule.Count == vanilla.Count(v => v.Tick == 0) && Schedule.All(v => v.Warp == v.Sail), "time reset starts a fresh ordered sweep and preserves non-warp speed");
            transport.GameTick(11);
            DispatchOptimization.Enabled = false;
            Schedule.Clear();
            transport.GameTick(60);
            Require(Schedule.Count == vanilla.Count(v => v.Tick == 0) && DispatchScheduler.AgeLocks, "disabled mode restores native scheduling and aging");
            DispatchOptimization.Enabled = true;
            DispatchScheduler.Failure = "unsupported aging witness";
            Schedule.Clear();
            transport.GameTick(120);
            Require(Schedule.Count == vanilla.Count(v => v.Tick == 0) && DispatchScheduler.AgeLocks, "unsupported clock falls back to native scheduling and aging");
            DispatchScheduler.Failure = null;
            DispatchScheduler.Reset();
            transport.GameTick(20);
            transport.GameTick(21);
            Require(!DispatchScheduler.AgeLocks, "unload starts between logical ticks");
            transport.Free();
            Require(transport.stationPool == null && DispatchScheduler.AgeLocks, "game unload releases scheduler owner and aging gate");
        }
        finally
        {
            LogisticsProfilerPlugin.Current = null;
            opt.UnpatchSelf();
            LogisticsProfilerPlugin.Unpatch(profiler);
            recorder.UnpatchSelf();
            DispatchOptimization.Enabled = false;
            DispatchScheduler.Factor = 1;
            DispatchScheduler.Reset();
        }
        Console.WriteLine("PASS: native all-route factors 1/2/5/30, shared lock clock, exceptions, configuration/time reset, compatibility fallback, unload and profiler integration.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void DispatchChecks()
    {
        const string supplyTrip = "supply_visit=2 trip_check=2 ";
        const string demandTrip = "demand_visit=2 trip_check=2 ";
        const string supplyReady = "supply_visit=1 trip_check=1 flight_eligible=1 ";
        const string demandReady = "demand_visit=1 trip_check=1 flight_eligible=1 reverse_search=1 reverse_visit=1 ";
        var cases = new (string Name, bool Supply, Action<StationComponent, StationComponent> Setup, string Counts)[] {
            ("own supply", true, (s, p) => s.storage[0].count = 0, "supply_visit=2 own_supply_short=2"),
            ("peer supply", false, (s, p) => p.storage[0].count = 0, "demand_visit=2 peer_supply_short=2 priority_lock_calls=2"),
            ("peer demand", true, (s, p) => p.storage[0].count = 1000, "supply_visit=2 peer_demand_empty=2 priority_lock_calls=2"),
            ("own demand", false, (s, p) => s.storage[0].count = 1000, "demand_visit=2 own_demand_empty=2"),
            ("supply lock", true, (s, p) => s.priorityLocks[0] = new StationPriorityLock { priorityIndex = 1, lockTick = 10 }, "supply_visit=2 own_priority_lock=2"),
            ("demand lock", false, (s, p) => s.priorityLocks[0] = new StationPriorityLock { priorityIndex = 1, lockTick = 10 }, "demand_visit=2 own_priority_lock=2"),
            ("peer lock", false, (s, p) => p.priorityLocks[0] = new StationPriorityLock { priorityIndex = 1, lockTick = 10 }, "demand_visit=2 peer_priority_lock=2"),
            ("missing peer", false, (s, p) => s.remotePairs = new[] { new SupplyDemandPair(3, 0, 1, 0), new SupplyDemandPair(3, 0, 1, 0) }, "demand_visit=2 peer_missing=2"),
            ("supply range", true, (s, p) => s.tripRangeShips = 1, supplyTrip + "range_block=2"),
            ("demand range", false, (s, p) => s.tripRangeShips = 1, demandTrip + "range_block=2"),
            ("collector", false, (s, p) => p.isCollector = true, demandTrip + "collector_block=2"),
            ("warper", true, (s, p) => { s.warperNecessary = true; s.warpEnableDist = 1; }, supplyTrip + "warper_block=2"),
            ("no ship", true, (s, p) => s.idleShipCount = 0, supplyTrip + "ship_or_energy_block=2"),
            ("low energy", false, (s, p) => s.energy = 6000000, demandTrip + "ship_or_energy_block=2"),
            ("trip energy", true, (s, p) => s.energy = 10000000, supplyReady + "trip_energy_short=1 priority_lock_calls=2"),
            ("supply launch", true, (s, p) => { }, supplyReady + "supply_attempt=1 priority_lock_calls=2"),
            ("supply failure", true, (s, p) => s.idleShipIndices = 0, supplyReady + "supply_attempt=1 supply_failed=1 priority_lock_calls=2"),
            ("demand launch", false, (s, p) => { }, demandReady + "demand_attempt=1 priority_lock_calls=2"),
            ("demand failure", false, (s, p) => s.idleShipIndices = 0, demandReady + "demand_attempt=1 demand_failed=1 priority_lock_calls=2"),
            ("reverse launch", false, (s, p) => s.remotePairs[1] = new SupplyDemandPair(1, 1, 2, 1), demandReady + "reverse_match=1 reverse_attempt=1 priority_lock_calls=2"),
            ("reverse supply", false, (s, p) => { s.remotePairs[1] = new SupplyDemandPair(1, 1, 2, 1); s.storage[1].count = 0; }, demandReady + "reverse_match=1 reverse_supply_short=1 demand_attempt=1 priority_lock_calls=2"),
            ("reverse demand", false, (s, p) => { s.remotePairs[1] = new SupplyDemandPair(1, 1, 2, 1); p.storage[1].count = 1000; }, demandReady + "reverse_match=1 reverse_demand_empty=1 demand_attempt=1 priority_lock_calls=3"),
            ("reverse energy", false, (s, p) => { s.remotePairs[1] = new SupplyDemandPair(1, 1, 2, 1); s.energy = 10000000; }, demandReady + "reverse_match=1 reverse_energy_short=1 trip_energy_short=1 priority_lock_calls=4"),
            ("reverse failure", false, (s, p) => { s.remotePairs[1] = new SupplyDemandPair(1, 1, 2, 1); s.idleShipIndices = 0; }, demandReady + "reverse_match=1 reverse_attempt=1 reverse_failed=1 demand_attempt=1 demand_failed=1 priority_lock_calls=2")
        };
        var vanilla = cases.Select(c => DispatchScenario(c.Supply, c.Setup)).ToArray();
        var harmony = new Harmony("logisticsprofiler.branch-checks");
        try
        {
            harmony.CreateClassProcessor(typeof(DispatchPatch)).Patch();
            Require(LogisticsProfilerPlugin.Failure == null, "dispatch instrumentation accepted: " + LogisticsProfilerPlugin.Failure);
            for (int i = 0; i < cases.Length; i++)
            {
                var capture = new Capture(1);
                LogisticsProfilerPlugin.Current = capture;
                var observed = DispatchScenario(cases[i].Supply, cases[i].Setup);
                LogisticsProfilerPlugin.Current = null;
                Require(capture.Stop() && DispatchDetails.Active == null, "trace drains after " + cases[i].Name);
                Require(vanilla[i].SequenceEqual(observed), "instrumentation preserves game state: " + cases[i].Name);
                var expected = cases[i].Counts.Split(' ').ToDictionary(s => s.Split('=')[0], s => long.Parse(s.Split('=')[1]));
                for (int n = 0; n < DispatchDetails.Names.Length; n++)
                {
                    expected.TryGetValue(DispatchDetails.Names[n], out long count);
                    Require(capture.Methods[4].Details[n] == count, cases[i].Name + ": " + DispatchDetails.Names[n] + " expected " + count + " got " + capture.Methods[4].Details[n]);
                }
                Require(vanilla[i].SequenceEqual(DispatchScenario(cases[i].Supply, cases[i].Setup)), "inactive probes preserve state");
            }
        }
        finally
        {
            LogisticsProfilerPlugin.Current = null;
            LogisticsProfilerPlugin.Unpatch(harmony);
        }

        OptimizationChecks(cases.Select(c => (c.Name, c.Supply, c.Setup)).ToList());

        var method = AccessTools.Method(typeof(StationComponent), "DetermineDispatch");
        var generator = new DynamicMethod("probe", typeof(void), Type.EmptyTypes).GetILGenerator();
        var code = PatchProcessor.GetOriginalInstructions(method);
        Require(DispatchDetails.Transpiler(code, generator).Count() > code.Count && LogisticsProfilerPlugin.Failure == null, "supported body accepts probes");
        code = PatchProcessor.GetOriginalInstructions(method);
        code[393].operand = 6000001;
        Require(DispatchDetails.Transpiler(code, generator).SequenceEqual(code) && LogisticsProfilerPlugin.Failure is InvalidOperationException,
            "changed operand stops diagnostics and preserves another mod's method body");
        LogisticsProfilerPlugin.Failure = null;
        DispatchDetails.Enabled = false;
        Require(DispatchDetails.Transpiler(code, null).Count() == code.Count, "timing-only mode accepts an uninstrumented method");
        Console.WriteLine("PASS: " + cases.Length + " real dispatch scenarios, exact branch counts, identical logistics state and signature rejection.");
    }

    private static void OptimizationChecks(List<(string Name, bool Supply, Action<StationComponent, StationComponent> Setup)> cases)
    {
        var random = new Random(7421);
        for (int seed = 0; seed < 40; seed++)
        {
            int[] counts = Enumerable.Range(0, 4).Select(_ => random.Next(0, 5) * 100).ToArray();
            int[] orders = Enumerable.Range(0, 4).Select(_ => random.Next(-1, 2) * 300).ToArray();
            int max = new[] { 0, 99, 100, 1000 }[random.Next(4)];
            bool noShip = random.Next(2) == 0;
            long energy = new[] { 6000000L, 10000000L, 1000000000L }[random.Next(3)];
            cases.Add(($"mixed ring {seed}", true, (s, p) => {
                for (int i = 0; i < 2; i++)
                {
                    s.storage[i].count = counts[i]; p.storage[i].count = counts[i + 2];
                    s.storage[i].remoteOrder = orders[i]; p.storage[i].remoteOrder = orders[i + 2];
                    s.storage[i].max = p.storage[i].max = max;
                }
                s.idleShipCount = noShip ? 0 : 1;
                s.energy = energy;
                s.remotePairs[1] = new SupplyDemandPair(2, 1, 1, 1);
            }));
        }
        foreach (bool supply in new[] { false, true })
        foreach (byte priority in new byte[] { 0, 1, 2, 3, 4, 5 })
        foreach (byte ticks in new byte[] { 0, 1, 10, 30, 60 })
            cases.Add(($"lock-only {supply}/{priority}/{ticks}", supply, (s, p) => {
                s.storage[0].count = supply ? 0 : 1000;
                p.storage[0].count = supply ? 1000 : 0;
                p.priorityLocks[0] = new StationPriorityLock { priorityIndex = priority, lockTick = ticks };
                s.idleShipCount = 0;
                s.energy = 6000000;
                s.remotePairProcesses = Enumerable.Repeat(7, 6).ToArray();
            }));
        var vanilla = Enumerable.Range(0, 6).Select(priority =>
            cases.Select(c => DispatchScenario(c.Supply, c.Setup, priority)).ToArray()).ToArray();
        var opt = new Harmony(LogisticsProfilerPlugin.OptGuid);
        var profiler = new Harmony("org.fyyy.logisticsprofiler");
        for (int mode = 0; mode < 4; mode++)
        {
            DispatchDetails.Enabled = mode == 1 || mode == 2;
            try
            {
                if (mode == 1) profiler.CreateClassProcessor(typeof(DispatchPatch)).Patch();
                opt.CreateClassProcessor(typeof(DispatchOptimization)).Patch();
                if (mode >= 2) profiler.CreateClassProcessor(typeof(DispatchPatch)).Patch();
                Require(DispatchOptimization.Failure == null && LogisticsProfilerPlugin.Failure == null,
                    "optimization/profiler patch ordering " + mode);
                foreach (bool enabled in new[] { false, true })
                {
                    DispatchOptimization.Enabled = enabled;
                    for (int priority = 0; priority < 6; priority++)
                    for (int i = 0; i < cases.Count; i++)
                    {
                        var capture = new Capture(1);
                        LogisticsProfilerPlugin.Current = capture;
                        var observed = DispatchScenario(cases[i].Supply, cases[i].Setup, priority);
                        LogisticsProfilerPlugin.Current = null;
                        Require(capture.Stop(), "optimization capture drains");
                        Require(vanilla[priority][i].SequenceEqual(observed),
                            $"optimization state mode={mode} enabled={enabled} priority={priority} {cases[i].Name}");
                    }
                }
                if (DispatchDetails.Enabled)
                {
                    foreach (int priority in new[] { 3, 5 })
                    {
                        var capture = new Capture(1);
                        LogisticsProfilerPlugin.Current = capture;
                        DispatchScenario(true, (s, p) => { s.storage[0].count = 0; p.storage[0].count = 1000; }, priority);
                        LogisticsProfilerPlugin.Current = null;
                        Require(capture.Stop(), "lock elision sample drains");
                        var counters = capture.Methods[priority + 1].Details;
                        Require(counters[Array.IndexOf(DispatchDetails.Names, "own_supply_short")] == 2 &&
                            counters[Array.IndexOf(DispatchDetails.Names, "priority_lock_calls")] == (priority == 3 ? 1 : 0),
                            "probes observe visits and fewer redundant lock calls");
                    }
                }
            }
            finally
            {
                LogisticsProfilerPlugin.Current = null;
                opt.UnpatchSelf();
                LogisticsProfilerPlugin.Unpatch(profiler);
            }
        }
        DispatchDetails.Enabled = true;
        var generator = new DynamicMethod("unsupported", typeof(void), Type.EmptyTypes).GetILGenerator();
        var code = PatchProcessor.GetOriginalInstructions(AccessTools.Method(typeof(StationComponent), "DetermineDispatch"));
        code[463].opcode = OpCodes.Nop;
        Require(DispatchOptimization.Transpiler(code, generator).SequenceEqual(code) && DispatchOptimization.Failure != null,
            "partial pattern match leaves the entire method unchanged");
        DispatchOptimization.Failure = null;
        Console.WriteLine($"PASS: {cases.Count * 6 * 8} original/optimized state comparisons across all priorities, toggles and profiler load orders.");
        DispatchBenchmark(opt);
    }

    private static double _dispatchMilliseconds;

    private static void DispatchBenchmark(Harmony opt)
    {
        Action<StationComponent, StationComponent> supply = (s, p) => { s.storage[0].count = 0; p.storage[0].count = 1000; };
        Action<StationComponent, StationComponent> demand = (s, p) => { s.storage[0].count = 1000; p.storage[0].count = 0; };
        var baseline = new double[3];
        // Synthetic hot loops isolate the measured failure paths; these are not save-level UPS results.
        for (int mode = 0; mode < 3; mode++)
        {
            if (mode == 1) opt.CreateClassProcessor(typeof(DispatchOptimization)).Patch();
            DispatchOptimization.Enabled = mode == 2;
            for (int path = 0; path < 3; path++)
            {
                bool isSupply = path != 2;
                int priority = path == 0 ? 3 : 5;
                var setup = isSupply ? supply : demand;
                DispatchScenario(isSupply, setup, priority, 100, 1024);
                var times = new double[5];
                for (int trial = 0; trial < times.Length; trial++)
                {
                    DispatchScenario(isSupply, setup, priority, 1000, 1024);
                    times[trial] = _dispatchMilliseconds;
                }
                Array.Sort(times);
                if (mode == 0) baseline[path] = times[2];
                Console.WriteLine(FormattableString.Invariant($"BENCH: mode={mode} P{priority} {(isSupply ? "supply" : "demand")} 1,024,000 visits median={times[2]:F3} ms vanilla={baseline[path]:F3} ms"));
            }
        }
        opt.UnpatchSelf();
        DispatchOptimization.Enabled = false;
    }

    private static byte[] DispatchScenario(bool supply, Action<StationComponent, StationComponent> setup, int priorityIndex = 3, int repetitions = 1, int pairCount = 2)
    {
        var station = new StationComponent {
            id = 1, gid = 1, planetId = 101, idleShipCount = 1, idleShipIndices = 1, energy = 1000000000,
            deliveryShips = 100, tripRangeShips = 1e9, warpEnableDist = 1e9,
            storage = new[] { new StationStore { max = 1000, itemId = 1001, count = supply ? 500 : 0 }, new StationStore { max = 1000, itemId = 1002, count = 500 } },
            priorityLocks = new StationPriorityLock[2], remotePairProcesses = new int[6],
            remotePairOffsets = Enumerable.Range(0, 7).Select(p => p > priorityIndex ? pairCount : 0).ToArray(),
            workShipDatas = new ShipData[1], workShipOrders = new RemoteLogisticOrder[1]
        };
        var peer = new StationComponent {
            gid = 2, planetId = 102,
            storage = new[] { new StationStore { max = 1000, itemId = 1001, count = supply ? 0 : 500 }, new StationStore { max = 1000, itemId = 1002 } },
            priorityLocks = new StationPriorityLock[2]
        };
        var pair = supply ? new SupplyDemandPair(1, 0, 2, 0) : new SupplyDemandPair(2, 0, 1, 0);
        station.remotePairs = Enumerable.Repeat(pair, pairCount).ToArray();
        setup(station, peer);
        var planet = new PlanetData { id = 101, factoryIndex = 0 };
        var galaxy = new GalaxyData { stars = new[] { new StarData { planets = new[] { planet } } }, astrosData = new AstroData[103] };
        galaxy.astrosData[102].uPos.x = 10000;
        var power = new PowerSystem(planet, true) { networkServes = new[] { 1f }, consumerPool = new PowerConsumerComponent[1] };
        var factory = new PlanetFactory { powerSystem = power };
        planet.factory = factory;
        var stats = new[] { new FactoryProductionStat { consumeRegister = new int[1211] } };
        var traffic = new TrafficStatistics { gameData = new GameData { galaxy = galaxy }, starTrafficPool = new AstroTrafficStat[2], factoryTrafficPool = new AstroTrafficStat[1] };
        var pool = new[] { null, station, peer, null };
        var factories = new[] { factory };
        var timer = System.Diagnostics.Stopwatch.StartNew();
        for (int repetition = 0; repetition < repetitions; repetition++)
            station.DetermineDispatch(100, 1000, 100, priorityIndex, pool, stats, factories, galaxy, traffic);
        _dispatchMilliseconds = timer.Elapsed.TotalMilliseconds;
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(station.energy);
            writer.Write(station.idleShipCount); writer.Write(station.workShipCount);
            writer.Write(station.idleShipIndices); writer.Write(station.workShipIndices);
            writer.Write(station.nextShipIndex); writer.Write(station.warperCount);
            writer.Write((int)AccessTools.Field(typeof(StationComponent), "_tmp_iter_remote").GetValue(station));
            foreach (int cursor in station.remotePairProcesses) writer.Write(cursor);
            foreach (var tower in new[] { station, peer })
            {
                foreach (var store in tower.storage) store.Export(writer);
                foreach (var priority in tower.priorityLocks) priority.Export(writer);
            }
            foreach (var ship in station.workShipDatas) ship.Export(writer);
            foreach (var order in station.workShipOrders) order.Export(writer);
            writer.Write(stats[0].consumeRegister[1210]);
            foreach (int item in new[] { 1001, 1002 })
            {
                writer.Write(traffic.starTrafficPool[1]?.internalRegister[item] ?? 0);
                writer.Write(traffic.factoryTrafficPool[0]?.outputRegister[item] ?? 0);
            }
            return stream.ToArray();
        }
    }
}
