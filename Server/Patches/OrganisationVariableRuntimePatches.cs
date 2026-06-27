#if SERVER
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.Variables;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.Variables;
#endif

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationVariableRuntimePatches
{
    [HarmonyPatch(typeof(VariableDatabase), nameof(VariableDatabase.SetVariableValue))]
    [HarmonyPostfix]
    private static void SetVariableValuePostfix(string variableName, string value, bool network)
    {
        _ = value;
        _ = network;

        OrganisationsServerMod.ActiveInstance?.NotifyQuestVariableMutation(variableName);
    }

    [HarmonyPatch(typeof(VariableDatabase), "RpcReader___Server_SendValue_3895153758")]
    [HarmonyPrefix]
    private static bool SendValueReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadNetworkConnection();
        string variableName = PooledReader0.ReadString();
        string value = PooledReader0.ReadString();

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod != null && serverMod.TryHandleRemoteQuestVariableMutation(conn, variableName, value))
        {
            return false;
        }

        PooledReader0.Position = originalPosition;
        return true;
    }
}
#endif
