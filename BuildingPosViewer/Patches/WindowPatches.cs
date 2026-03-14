using HarmonyLib;
using UnityEngine;

namespace BuildingPosViewer.Patches;

public static class WindowPatches
{
    private static void ShowPos(UnityEngine.UI.Text titleText, PlanetFactory factory, int entityId)
    {
        if (titleText == null || factory == null || entityId <= 0)
            return;
        PosDisplayHelper.Show(titleText, factory, entityId);
    }

    // --- Assembler ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIAssemblerWindow), "_OnUpdate")]
    public static void UIAssemblerWindow_OnUpdate(UIAssemblerWindow __instance)
    {
        if (__instance.assemblerId == 0 || __instance.factory == null) return;
        var entityId = __instance.factorySystem.assemblerPool[__instance.assemblerId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Miner ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIMinerWindow), "_OnUpdate")]
    public static void UIMinerWindow_OnUpdate(UIMinerWindow __instance)
    {
        if (__instance.minerId == 0 || __instance.factory == null) return;
        var entityId = __instance.factorySystem.minerPool[__instance.minerId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Lab ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UILabWindow), "_OnUpdate")]
    public static void UILabWindow_OnUpdate(UILabWindow __instance)
    {
        if (__instance.labId == 0 || __instance.factory == null) return;
        var entityId = __instance.factorySystem.labPool[__instance.labId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Storage ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIStorageWindow), "_OnUpdate")]
    public static void UIStorageWindow_OnUpdate(UIStorageWindow __instance)
    {
        if (__instance.storageId == 0 || __instance.factory == null) return;
        var entityId = __instance.factoryStorage.storagePool[__instance.storageId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Tank ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UITankWindow), "_OnUpdate")]
    public static void UITankWindow_OnUpdate(UITankWindow __instance)
    {
        if (__instance.tankId == 0 || __instance.factory == null) return;
        var entityId = __instance.storage.tankPool[__instance.tankId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Fractionator ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIFractionatorWindow), "_OnUpdate")]
    public static void UIFractionatorWindow_OnUpdate(UIFractionatorWindow __instance)
    {
        if (__instance.fractionatorId == 0 || __instance.factory == null) return;
        var entityId = __instance.factorySystem.fractionatorPool[__instance.fractionatorId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Ejector ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIEjectorWindow), "_OnUpdate")]
    public static void UIEjectorWindow_OnUpdate(UIEjectorWindow __instance)
    {
        if (__instance.ejectorId == 0 || __instance.factory == null) return;
        var entityId = __instance.factorySystem.ejectorPool[__instance.ejectorId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Silo ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UISiloWindow), "_OnUpdate")]
    public static void UISiloWindow_OnUpdate(UISiloWindow __instance)
    {
        if (__instance.siloId == 0 || __instance.factory == null) return;
        var entityId = __instance.factorySystem.siloPool[__instance.siloId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Inserter ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIInserterWindow), "_OnUpdate")]
    public static void UIInserterWindow_OnUpdate(UIInserterWindow __instance)
    {
        if (__instance.inserterId == 0 || __instance.factory == null) return;
        var entityId = __instance.factorySystem.inserterPool[__instance.inserterId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Belt ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBeltWindow), "_OnUpdate")]
    public static void UIBeltWindow_OnUpdate(UIBeltWindow __instance)
    {
        if (__instance.beltId == 0 || __instance.factory == null) return;
        var entityId = __instance.traffic.beltPool[__instance.beltId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Accumulator ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIAccumulatorWindow), "_OnUpdate")]
    public static void UIAccumulatorWindow_OnUpdate(UIAccumulatorWindow __instance)
    {
        if (__instance.accId == 0 || __instance.factory == null) return;
        var entityId = __instance.powerSystem.accPool[__instance.accId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Station ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIStationWindow), "_OnUpdate")]
    public static void UIStationWindow_OnUpdate(UIStationWindow __instance)
    {
        if (__instance.stationId == 0 || __instance.factory == null) return;
        var entityId = __instance.transport.stationPool[__instance.stationId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Dispenser ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIDispenserWindow), "_OnUpdate")]
    public static void UIDispenserWindow_OnUpdate(UIDispenserWindow __instance)
    {
        if (__instance.dispenserId == 0 || __instance.factory == null) return;
        var entityId = __instance.transport.dispenserPool[__instance.dispenserId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Battle Base ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIBattleBaseWindow), "_OnUpdate")]
    public static void UIBattleBaseWindow_OnUpdate(UIBattleBaseWindow __instance)
    {
        if (__instance.battleBaseId == 0 || __instance.factory == null) return;
        var entityId = __instance.defenseSystem.battleBases.buffer[__instance.battleBaseId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Turret ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UITurretWindow), "_OnUpdate")]
    public static void UITurretWindow_OnUpdate(UITurretWindow __instance)
    {
        if (__instance.turretId == 0 || __instance.factory == null) return;
        ref var turret = ref __instance.defenseSystem.turrets.buffer[__instance.turretId];
        ShowPos(__instance.titleText, __instance.factory, turret.entityId);
    }

    // --- Power Generator (solar, wind, thermal, fusion, etc.) ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPowerGeneratorWindow), "_OnUpdate")]
    public static void UIPowerGeneratorWindow_OnUpdate(UIPowerGeneratorWindow __instance)
    {
        if (__instance.generatorId == 0 || __instance.factory == null) return;
        var entityId = __instance.powerSystem.genPool[__instance.generatorId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Power Node (tesla tower, satellite substation, etc.) ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPowerNodeWindow), "_OnUpdate")]
    public static void UIPowerNodeWindow_OnUpdate(UIPowerNodeWindow __instance)
    {
        if (__instance.nodeId == 0 || __instance.factory == null) return;
        var entityId = __instance.powerSystem.nodePool[__instance.nodeId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }

    // --- Power Exchanger (energy exchanger) ---

    [HarmonyPostfix]
    [HarmonyPatch(typeof(UIPowerExchangerWindow), "_OnUpdate")]
    public static void UIPowerExchangerWindow_OnUpdate(UIPowerExchangerWindow __instance)
    {
        if (__instance.exchangerId == 0 || __instance.factory == null) return;
        var entityId = __instance.powerSystem.excPool[__instance.exchangerId].entityId;
        ShowPos(__instance.titleText, __instance.factory, entityId);
    }
}
