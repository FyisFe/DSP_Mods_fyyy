using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization;
using DashboardOverhaul;
using HarmonyLib;
using static Checks;

internal static class ChartChecks
{
    private const BindingFlags HiddenStatic = BindingFlags.Static | BindingFlags.NonPublic;

    public static void Run()
    {
        CheckPersistence();
        CheckObserveGroups();
        CheckObserveTargets();
        Console.WriteLine("Chart checks passed: style cycling/defaults, native save fallback/recovery, failed-save rollback, lock inheritance, batch scopes and targets.");
    }

    private static ChartData Data(int preset = 0, int layout = 0)
    {
        var data = new ChartData { statPlanId = 7, presetIndex = preset, displayTypeParams = new int[16] };
        data.displayTypeParams[0] = layout;
        return data;
    }

    private static void CheckPersistence()
    {
        var stylesField = typeof(ChartPresetsDB).GetField("styles", HiddenStatic);
        var indices = (int[])typeof(ChartStyles).GetField("Indices", HiddenStatic).GetValue(null);
        var originalIndices = (int[])indices.Clone();
        var originalStyles = stylesField.GetValue(null);
        var originalData = GameMain.data;
        int originalBlue = ChartStyles.BlueBorderIndex;
        int originalBlack = ChartStyles.BlackBackgroundIndex;
        var harmony = new Harmony("DashboardOverhaul.Checks.ChartPersistence");
        try
        {
            // Only the native data serializers are patched; no Unity lifecycle methods execute here.
            GameMain.data = new GameData
            {
                patch = GameData.CURRENT_PATCH,
                statistics = new GameStatData
                {
                    charts = new CustomCharts { statPlans = new StatPlanPool { buffer = new StatPlan[8] } }
                }
            };
            GameMain.data.statistics.charts.statPlans.buffer[7] =
                new AstroItemProductionStatPlan { id = 7, type = EStatPlanType.AstroItemProduction };

            ConfigureStyles(0);
            CheckStyleChanges();
            for (int layout = 6; layout <= 8; layout++)
            {
                ConfigureStyles(0);
                PatchSerializers(harmony, import: false);
                var data = Data(1, layout);
                data.backgroundStyleIndex = ChartStyles.GreenBackgroundIndex;
                data.borderStyleIndex = indices[5];
                data.displayTypeParams[3] = 91;
                var liveParameters = data.displayTypeParams;
                byte[] saved = Write(data);
                Require(ReferenceEquals(data.displayTypeParams, liveParameters) && data.displayTypeParams[0] == layout &&
                    data.backgroundStyleIndex == indices[0] && data.borderStyleIndex == indices[5],
                    "successful export must restore the live chart and parameter array");

                harmony.UnpatchSelf();
                var vanilla = Read(saved);
                Require(vanilla.backgroundStyleIndex == 0 && vanilla.borderStyleIndex == 0 &&
                    vanilla.displayTypeParams[0] == 0 && vanilla.displayTypeParams[3] == 91,
                    "native import without the mod must receive valid default styles/layout and preserve unrelated parameters");
                byte[] vanillaResave = Write(vanilla);

                // Adding native styles changes runtime indices; persisted custom identities must still resolve.
                ConfigureStyles(2);
                PatchSerializers(harmony, import: true);
                var restored = Read(vanillaResave);
                Require(restored.backgroundStyleIndex == indices[0] && restored.borderStyleIndex == indices[5] &&
                    restored.displayTypeParams[0] == layout && restored.displayTypeParams[3] == 91,
                    "reinstalling the mod must recover custom styles/layout after a native resave and table growth");
                restored.backgroundStyleIndex = 1;
                restored.borderStyleIndex = 2;
                restored.displayTypeParams[0] = 1;
                var changed = Read(Write(restored));
                Require(changed.backgroundStyleIndex == 1 && changed.borderStyleIndex == 2 && changed.displayTypeParams[0] == 1,
                    "editing restored settings must not resurrect stale custom metadata on the next load");
                harmony.UnpatchSelf();
            }

            PatchSerializers(harmony, import: true);
            var failing = Data(1, 6);
            failing.backgroundStyleIndex = indices[0];
            failing.borderStyleIndex = indices[5];
            failing.displayTypeParams[3] = 37;
            var beforeFailure = failing.displayTypeParams;
            bool threw = false;
            try
            {
                using (var stream = new ThrowingStream())
                using (var writer = new BinaryWriter(stream)) failing.Export(writer);
            }
            catch (IOException) { threw = true; }
            Require(threw && ReferenceEquals(failing.displayTypeParams, beforeFailure) &&
                failing.backgroundStyleIndex == indices[0] && failing.borderStyleIndex == indices[5] &&
                failing.displayTypeParams[0] == 6 && failing.displayTypeParams[3] == 37,
                "a failed stream write must propagate the error and restore every live field and its array reference");

            var locked = Data();
            locked.borderStyleIndex = indices[5];
            ChartState.SetLocked(locked, true);
            Require(locked.locked && locked.borderStyleIndex == ChartStyles.BlueBorderIndex, "locking must apply the blue border");
            var resized = Data(1);
            typeof(ChartState).GetMethod("Inherit", HiddenStatic).Invoke(null,
                new object[] { locked, resized, EStatPlanType.AstroItemProduction });
            Require(resized.locked && !ReferenceEquals(resized.displayTypeParams, locked.displayTypeParams),
                "size replacement must inherit the native lock without sharing mutable display parameters");
            ChartState.SetLocked(resized, false);
            Require(!resized.locked && resized.borderStyleIndex == indices[5] && locked.locked,
                "unlocking after a size replacement must restore the original custom border");

            var legacy = Data();
            legacy.displayTypeParams[15] = 1;
            legacy = Read(Write(legacy));
            Require(legacy.locked, "legacy lock metadata must migrate to the native lock");
            ChartState.SetLocked(legacy, false);
            Require(!Read(Write(legacy)).locked, "a migrated legacy lock must not undo a subsequent native unlock");
        }
        finally
        {
            harmony.UnpatchSelf();
            GameMain.data = originalData;
            stylesField.SetValue(null, originalStyles);
            Array.Copy(originalIndices, indices, indices.Length);
            SetStyleIndex(nameof(ChartStyles.BlueBorderIndex), originalBlue);
            SetStyleIndex(nameof(ChartStyles.BlackBackgroundIndex), originalBlack);
        }
    }

    private static void CheckStyleChanges()
    {
        foreach (var type in new[] { EChartStyleType.Background, EChartStyleType.Border })
        {
            int current = 0;
            var styles = ChartPresetsDB.GetChartStyles();
            for (int i = 1; i < styles.Length; i++)
                if (styles[i].type == type)
                {
                    current = ChartStyles.NextIndex(current, type);
                    Require(current == i, "style cycling must follow menu order and skip other kinds");
                }
            Require(ChartStyles.NextIndex(current, type) == 0, "style cycling must wrap to default");
        }

        var data = Data();
        ChartStyles.Apply(data, EChartStyleType.Background, ChartStyles.GreenBackgroundIndex);
        ChartStyles.Apply(data, EChartStyleType.Border, ChartStyles.BlueBorderIndex);
        ChartStyles.Apply(data, EChartStyleType.Border, 0);
        Require(data.borderStyleIndex == 0 && data.backgroundStyleIndex == ChartStyles.GreenBackgroundIndex,
            "an unlocked default border must clear without changing the background");
        ChartState.SetLocked(data, true);
        ChartStyles.Apply(data, EChartStyleType.Border, 0);
        Require(data.borderStyleIndex == ChartStyles.BlueBorderIndex && data.locked,
            "a locked default border must stay blue without changing the lock");
    }

    private static void ConfigureStyles(int extraNativeStyles)
    {
        // A data-only registry fixture avoids loading Unity assets or executing their lifecycle.
        var indices = (int[])typeof(ChartStyles).GetField("Indices", HiddenStatic).GetValue(null);
        int nativeCount = 3 + extraNativeStyles;
        var styles = new ChartStyle[nativeCount + indices.Length];
        for (int i = 0; i < styles.Length; i++) styles[i] = new ChartStyle { type = EChartStyleType.Generic };
        styles[1].type = EChartStyleType.Background;
        styles[2].type = EChartStyleType.Border;
        for (int i = 0; i < indices.Length; i++)
        {
            indices[i] = nativeCount + i;
            styles[indices[i]].type = i < 3 ? EChartStyleType.Background : EChartStyleType.Border;
        }
        typeof(ChartPresetsDB).GetField("styles", HiddenStatic).SetValue(null, styles);
        SetStyleIndex(nameof(ChartStyles.BlueBorderIndex), 2);
        SetStyleIndex(nameof(ChartStyles.BlackBackgroundIndex), 1);
    }

    private static void SetStyleIndex(string name, int index) =>
        typeof(ChartStyles).GetProperty(name).GetSetMethod(true).Invoke(null, new object[] { index });

    private static void PatchSerializers(Harmony harmony, bool import)
    {
        HarmonyMethod Patch(string name) => new HarmonyMethod(typeof(ChartState).GetMethod(name, HiddenStatic));
        harmony.Patch(AccessTools.Method(typeof(ChartData), nameof(ChartData.Export)),
            prefix: Patch("Export_Prefix"), finalizer: Patch("Export_Finalizer"));
        if (import) harmony.Patch(AccessTools.Method(typeof(ChartData), nameof(ChartData.Import)), postfix: Patch("Import_Postfix"));
    }

    [MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static byte[] Write(ChartData data)
    {
        using (var stream = new MemoryStream())
        {
            using (var writer = new BinaryWriter(stream, System.Text.Encoding.UTF8, leaveOpen: true)) data.Export(writer);
            return stream.ToArray();
        }
    }

    [MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static ChartData Read(byte[] saved)
    {
        var data = new ChartData();
        using (var stream = new MemoryStream(saved))
        using (var reader = new BinaryReader(stream)) data.Import(reader);
        return data;
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public override void Write(byte[] buffer, int offset, int count) => throw new IOException("Simulated write failure");
    }

    private static void CheckObserveGroups()
    {
        var groupKey = typeof(PageObserveMenu).GetMethod("GroupKey", HiddenStatic);
        object Key(StatPlan plan, ChartData data) => groupKey.Invoke(null, new object[] { plan, data });
        var data = Data();
        var production = new AstroItemProductionStatPlan { type = EStatPlanType.AstroItemProduction };
        object rate = Key(production, data);
        for (int level = 1; level <= 4; level++)
        {
            production.timeLevel = level;
            Require(rate.Equals(Key(production, data)), "ordinary production periods must share the same batch options");
        }
        production.timeLevel = 5;
        Require(!rate.Equals(Key(production, data)), "total production must not receive rate-only batch layouts");
        production.timeLevel = 0;
        Require(!rate.Equals(Key(production, Data(1))), "different presets must not share a batch option domain");

        var dyson = new DysonSphereStatPlan { type = EStatPlanType.DysonSphere };
        object sails = Key(dyson, data);
        data.displayTypeParams[0] = 1;
        object structure = Key(dyson, data);
        Require(!sails.Equals(structure), "sails and structure power use different layout integers");
        data.displayTypeParams[0] = 2;
        Require(structure.Equals(Key(dyson, data)), "structure and cell charts share the same layout meanings");
        dyson.timeLevel = 5;
        Require(!structure.Equals(Key(dyson, data)), "Dyson rate and total menus must retain their distinct meanings");

        var transport = new AstroTransportStatPlan { type = EStatPlanType.AstroTransport };
        object local = Key(transport, data);
        transport.isInterstellar = true;
        Require(!local.Equals(Key(transport, data)), "interstellar charts must not receive local-carrier monitor choices");
        var storage = new SingleStorageStatPlan { type = EStatPlanType.SingleStorage };
        foreach (var supported in new[] { SingleStorageStatPlan.EStatPlanStorageType.Grid, SingleStorageStatPlan.EStatPlanStorageType.Tank })
        {
            storage.storageType = supported;
            object stacked = Key(storage, data);
            foreach (var other in new[] { SingleStorageStatPlan.EStatPlanStorageType.Path, SingleStorageStatPlan.EStatPlanStorageType.Station })
            {
                storage.storageType = other;
                Require(!stacked.Equals(Key(storage, data)), "non-stacked storage must not hide or receive stacked-storage menu choices");
            }
        }
    }

    private static void CheckObserveTargets()
    {
        var source = (UIChartAstroItemProduction)FormatterServices.GetUninitializedObject(typeof(UIChartAstroItemProduction));
        var target = (UIChartAstroItemProduction)FormatterServices.GetUninitializedObject(typeof(UIChartAstroItemProduction));
        var sourceData = source.chartData = Data();
        var targetData = target.chartData = Data();
        Action<int> handler = source.OnLayoutMenuButtonClick;
        var targets = new List<UIChart> { source, target };
        var bind = typeof(PageObserveMenu).GetMethod("BindTargets", HiddenStatic);
        var actions = (Action<int>[])bind.Invoke(null, new object[] { handler, source, targets });
        actions[0](1);
        Require(sourceData.displayTypeParams[0] == 1 && targetData.displayTypeParams[0] == 0,
            "the first bound callback must update only its own chart");
        actions[1](2);
        Require(sourceData.displayTypeParams[0] == 1 && targetData.displayTypeParams[0] == 2 &&
            ReferenceEquals(source.chartData, sourceData) && ReferenceEquals(target.chartData, targetData),
            "each callback must update its real target without swapping chart data references");
        Action<int> multicast = handler + handler;
        Require(bind.Invoke(null, new object[] { multicast, source, targets }) == null,
            "multicast extensions need explicit scope and must not be silently replayed");
        Action<int> closure = value => sourceData.displayTypeParams[0] = value;
        Require(bind.Invoke(null, new object[] { closure, source, targets }) == null,
            "foreign closures must not be presented as supported batch actions");
    }
}
