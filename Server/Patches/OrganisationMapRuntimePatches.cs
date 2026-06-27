#if SERVER
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.Map;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.Map;
#endif

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationMapRuntimePatches
{
    [HarmonyPatch(typeof(MapRegionData), nameof(MapRegionData.SetUnlocked))]
    [HarmonyPrefix]
    private static bool MapRegionSetUnlockedPrefix(MapRegionData __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedMapRegionUnlock(__instance);
    }

    [HarmonyPatch(typeof(DarkMarket), "RpcReader___Server_SendUnlocked_2166136261")]
    [HarmonyPrefix]
    private static bool DarkMarketSendUnlockedPrefix(DarkMarket __instance, NetworkConnection conn)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleDarkMarketUnlock(conn, __instance);
    }

    [HarmonyPatch(typeof(SewerManager), "RpcReader___Server_SetSewerUnlocked_Server_2166136261")]
    [HarmonyPrefix]
    private static bool SewerManagerSetSewerUnlockedPrefix(SewerManager __instance, NetworkConnection conn)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleSewerUnlock(conn, __instance);
    }

    [HarmonyPatch(typeof(SewerManager), "RpcReader___Server_SetRandomKeyCollected_Server_2166136261")]
    [HarmonyPrefix]
    private static bool SewerManagerSetRandomKeyCollectedPrefix(NetworkConnection conn)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryReserveRandomWorldSewerKey(conn);
    }

    [HarmonyPatch(typeof(SewerMushrooms), "RpcReader___Server_SetMushroomSpawnLocationAvailable_3316948804")]
    [HarmonyPrefix]
    private static bool SewerMushroomsSetSpawnLocationAvailablePrefix(SewerMushrooms __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        int originalPosition = PooledReader0.Position;
        int locationIndex = PooledReader0.ReadInt32();
        bool canMutatePhysicalSpawn = locationIndex >= 0
            && __instance != null
            && __instance.MushroomLocations != null
            && locationIndex < __instance.MushroomLocations.Count
            && __instance.GetActiveMushroomLocationIndices().Contains(locationIndex)
            && (OrganisationsServerMod.ActiveInstance?.CanPlayerAccessSewer(conn) ?? true);
        if (canMutatePhysicalSpawn)
        {
            PooledReader0.Position = originalPosition;
        }

        return canMutatePhysicalSpawn;
    }
}
#endif
