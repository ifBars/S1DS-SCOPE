#if SERVER
using System;
using System.Linq;
using DedicatedServerMod.Organisations.Utils;
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Property;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.Doors;
using ScheduleOne.Property;
#endif

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationPropertyRuntimePatches
{
    private static readonly HashSet<LaunderingOperation> ScopedCompletedLaunderingOperations = new HashSet<LaunderingOperation>();

    [HarmonyPatch(typeof(Property), "RpcReader___Server_SetOwned_Server_2166136261")]
    [HarmonyPrefix]
    private static bool PropertyOwnedPrefix(Property __instance, NetworkConnection conn)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        return serverMod.TryHandlePropertyReservation(conn, __instance);
    }

    [HarmonyPatch(typeof(Business), "CompleteOperation")]
    [HarmonyPrefix]
    private static bool BusinessCompleteOperationPrefix(Business __instance, LaunderingOperation op)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || !serverMod.TryHandleBusinessLaunderingCompletion(__instance, op))
        {
            return true;
        }

        ScopedCompletedLaunderingOperations.Add(op);
        return false;
    }

    [HarmonyPatch(typeof(Business), "CompleteOperation")]
    [HarmonyPostfix]
    private static void BusinessCompleteOperationPostfix(Business __instance, LaunderingOperation op)
    {
        if (__instance == null || op == null)
        {
            return;
        }

        if (!ScopedCompletedLaunderingOperations.Remove(op))
        {
            return;
        }

        __instance.LaunderingOperations.Remove(op);
        __instance.HasChanged = true;
        Business.onOperationFinished?.Invoke(op);
    }

    [HarmonyPatch(typeof(DoorController), "RpcReader___Server_SetIsOpen_Server_1319291243")]
    [HarmonyPrefix]
    private static bool DoorSetIsOpenPrefix(DoorController __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient)
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadBoolean();
        EDoorSide accessSide = (EDoorSide)PooledReader0.ReadInt32(AutoPackType.PackedLess);
        _ = PooledReader0.ReadBoolean();

        Property? property = FindPropertyForDoor(__instance);
        if (property == null || !serverMod.IsTrackedProperty(property))
        {
            PooledReader0.Position = originalPosition;
            return true;
        }

        if (accessSide == EDoorSide.Interior)
        {
            PooledReader0.Position = originalPosition;
            return true;
        }

        bool canAccess = serverMod.CanPlayerAccessProperty(conn, property);
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    private static Property? FindPropertyForDoor(DoorController doorController)
    {
        return Property.Properties.AsManagedEnumerable().FirstOrDefault(property =>
            property != null
            && !string.IsNullOrWhiteSpace(property.PropertyCode)
            && (doorController.transform.IsChildOf(property.transform)
                || property.DoBoundsContainPoint(doorController.transform.position)));
    }
}
#endif
