#if SERVER
using DedicatedServerMod.Organisations.Server;
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.Delivery;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.Delivery;
#endif

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationDeliveryRuntimePatches
{
    [HarmonyPatch(typeof(DeliveryManager), "RpcReader___Server_SendDelivery_2813439055")]
    [HarmonyPrefix]
    private static bool SendDeliveryPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        string storeName = PooledReader0.ReadString();
        _ = PooledReader0.ReadString();
        string destinationCode = PooledReader0.ReadString();
        PooledReader0.Position = originalPosition;

        return serverMod.CanOrderDeliveryShop(conn, storeName) && serverMod.CanPlayerAccessProperty(conn, destinationCode);
    }

    [HarmonyPatch(typeof(DeliveryManager), "RpcReader___Server_RecordDeliveryReceipt_Server_2582461062")]
    [HarmonyPrefix]
    private static bool RecordDeliveryReceiptPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        string storeName = PooledReader0.ReadString();
        string destinationCode = PooledReader0.ReadString();
        PooledReader0.Position = originalPosition;

        return serverMod.CanOrderDeliveryShop(conn, storeName) && serverMod.CanPlayerAccessProperty(conn, destinationCode);
    }
}
#endif
