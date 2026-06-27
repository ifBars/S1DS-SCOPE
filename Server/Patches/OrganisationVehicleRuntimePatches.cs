#if SERVER
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Vehicles;
#else
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Vehicles;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationVehicleRuntimePatches
{
    [HarmonyPatch(typeof(Player), "RpcReader___Server_set_CurrentVehicle_3323014238")]
    [HarmonyPrefix]
    private static bool PlayerSetCurrentVehiclePrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        int originalPosition = PooledReader0.Position;
        NetworkObject vehicleObject = PooledReader0.ReadNetworkObject();
        LandVehicle? vehicle = vehicleObject == null ? null : vehicleObject.GetComponent<LandVehicle>();
        bool canAccess = CanConnectionAccessVehicle(conn, vehicle);
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(VehicleManager), "RpcReader___Server_SpawnVehicle_3323115898")]
    [HarmonyPrefix]
    private static bool VehicleSpawnPrefix(VehicleManager __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient)
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        string vehicleCode = PooledReader0.ReadString();
        Vector3 position = PooledReader0.ReadVector3();
        Quaternion rotation = PooledReader0.ReadQuaternion(AutoPackType.PackedLess);
        bool playerOwned = PooledReader0.ReadBoolean();
        if (!playerOwned)
        {
            PooledReader0.Position = originalPosition;
            return true;
        }

        serverMod.HandlePurchasedVehicleSpawn(__instance, conn, vehicleCode, position, rotation, playerOwned);
        return false;
    }

    [HarmonyPatch(typeof(LandVehicle), "RpcReader___Server_SetSeatOccupant_Server_3266232555")]
    [HarmonyPrefix]
    private static bool VehicleSeatOccupantPrefix(LandVehicle __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadInt32(AutoPackType.PackedLess);
        _ = PooledReader0.ReadNetworkConnection();
        PooledReader0.Position = originalPosition;
        return CanConnectionAccessVehicle(conn, __instance);
    }

    [HarmonyPatch(typeof(LandVehicle), "RpcReader___Server_SetOwner_328543758")]
    [HarmonyPrefix]
    private static bool VehicleSetOwnerPrefix(LandVehicle __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadNetworkConnection();
        PooledReader0.Position = originalPosition;
        return CanConnectionAccessVehicle(conn, __instance);
    }

    [HarmonyPatch(typeof(LandVehicle), "RpcReader___Server_SendOwnedColor_911055161")]
    [HarmonyPrefix]
    private static bool VehicleSendOwnedColorPrefix(LandVehicle __instance, NetworkConnection conn)
    {
        return CanConnectionAccessVehicle(conn, __instance);
    }

    [HarmonyPatch(typeof(LandVehicle), "RpcReader___Server_SetTransform_Server_3848837105")]
    [HarmonyPrefix]
    private static bool VehicleSetTransformPrefix(LandVehicle __instance, NetworkConnection conn)
    {
        return CanConnectionAccessVehicle(conn, __instance);
    }

    [HarmonyPatch(typeof(LandVehicle), "RpcReader___Server_SetSteeringAngle_431000436")]
    [HarmonyPrefix]
    private static bool VehicleSetSteeringAnglePrefix(LandVehicle __instance, NetworkConnection conn)
    {
        return CanConnectionAccessVehicle(conn, __instance);
    }

    [HarmonyPatch(typeof(LandVehicle), "RpcReader___Server_SetIsBreaking_Server_1140765316")]
    [HarmonyPrefix]
    private static bool VehicleSetIsBreakingPrefix(LandVehicle __instance, NetworkConnection conn)
    {
        return CanConnectionAccessVehicle(conn, __instance);
    }

    [HarmonyPatch(typeof(LandVehicle), "RpcReader___Server_SetIsReversing_Server_1140765316")]
    [HarmonyPrefix]
    private static bool VehicleSetIsReversingPrefix(LandVehicle __instance, NetworkConnection conn)
    {
        return CanConnectionAccessVehicle(conn, __instance);
    }

    [HarmonyPatch(typeof(VehicleLights), "RpcReader___Server_set_HeadlightsOn_1140765316")]
    [HarmonyPrefix]
    private static bool VehicleLightsSetHeadlightsOnPrefix(VehicleLights __instance, NetworkConnection conn)
    {
        return CanConnectionAccessVehicle(conn, __instance == null ? null : __instance.GetComponent<LandVehicle>());
    }

    private static bool CanConnectionAccessVehicle(NetworkConnection conn, LandVehicle? vehicle)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient || vehicle == null)
        {
            return true;
        }

        return serverMod.CanPlayerAccessVehicle(conn, vehicle);
    }
}
#endif
