#if SERVER
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.Quests;
using ECartelStatus = Il2Cpp.ECartelStatus;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.Cartel;
using ScheduleOne.Economy;
using ScheduleOne.NPCs.CharacterClasses;
using ScheduleOne.Quests;
#endif
using DedicatedServerMod.Organisations.Utils;

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationQuestNetworkPatches
{
    private static bool TryBeginScopedQuestRead(PooledReader reader, out string guid)
    {
        int originalPosition = reader.Position;
        guid = reader.ReadString();
        if (!QuestScopeRules.ShouldVirtualizeQuest(guid))
        {
            reader.Position = originalPosition;
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(QuestManager), "RpcWriter___Observers_CreateContract_Networked_2526053753")]
    [HarmonyPrefix]
    private static bool SuppressObserversCreateContract() => true;

    [HarmonyPatch(typeof(QuestManager), "RpcWriter___Observers_ReceiveQuestAction_920727549")]
    [HarmonyPrefix]
    private static bool SuppressObserversQuestAction(NetworkConnection conn, string guid, QuestManager.EQuestAction action)
    {
        _ = conn;
        _ = action;
        return !QuestScopeRules.ShouldVirtualizeQuest(guid);
    }

    [HarmonyPatch(typeof(QuestManager), "RpcWriter___Observers_ReceiveQuestState_3887376304")]
    [HarmonyPrefix]
    private static bool SuppressObserversQuestState(NetworkConnection conn, string guid, EQuestState state)
    {
        _ = conn;
        _ = state;
        return !QuestScopeRules.ShouldVirtualizeQuest(guid);
    }

    [HarmonyPatch(typeof(QuestManager), "RpcWriter___Observers_ReceiveQuestEntryState_311789429")]
    [HarmonyPrefix]
    private static bool SuppressObserversQuestEntryState(NetworkConnection conn, string guid, int entryIndex, EQuestState state)
    {
        _ = conn;
        _ = entryIndex;
        _ = state;
        return !QuestScopeRules.ShouldVirtualizeQuest(guid);
    }

    [HarmonyPatch(typeof(QuestManager), "RpcWriter___Observers_CreateDeaddropCollectionQuest_3895153758")]
    [HarmonyPrefix]
    private static bool SuppressObserversDeaddropCreate() => true;

    [HarmonyPatch(typeof(Cartel), "RpcReader___Server_SetStatus_Server_2366206100")]
    [HarmonyPrefix]
    private static void CaptureCallerCartelStatus(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return;
        }

        int originalPosition = PooledReader0.Position;
        ECartelStatus status = (ECartelStatus)PooledReader0.ReadInt32();
        bool resetStatusChangeTimer = PooledReader0.ReadBoolean();
        PooledReader0.Position = originalPosition;
        OrganisationsServerMod.ActiveInstance?.RecordCallerCartelStatus(conn, status, resetStatusChangeTimer);
    }

    [HarmonyPatch(typeof(Cartel), "RpcReader___Server_SetStatus_Server_2366206100")]
    [HarmonyPostfix]
    private static void CompleteCallerCartelStatusMutation()
    {
        OrganisationsServerMod.ActiveInstance?.CompleteCallerCartelStatusMutation();
    }

    [HarmonyPatch(typeof(Thomas), "RpcReader___Server_CancelAgreement_Server_2166136261")]
    [HarmonyPrefix]
    private static bool CaptureCallerCartelAgreementCancellation(Thomas __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleCallerCartelAgreementCancellation(conn, __instance);
    }

    [HarmonyPatch(typeof(Thomas), "RpcReader___Server_MeetingEnded_Server_2166136261")]
    [HarmonyPrefix]
    private static bool CaptureCallerThomasMeetingEnded(Thomas __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedThomasMeetingEnded(__instance, conn);
    }

    [HarmonyPatch(typeof(QuestManager), "RpcReader___Server_SendQuestAction_2848227116")]
    [HarmonyPrefix]
    private static bool CaptureQuestAction(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        if (!TryBeginScopedQuestRead(PooledReader0, out string guid))
        {
            return true;
        }

        QuestManager.EQuestAction action = (QuestManager.EQuestAction)PooledReader0.ReadInt32();
        OrganisationsServerMod.ActiveInstance?.RecordScopedQuestAction(conn, guid, action);
        return false;
    }

    [HarmonyPatch(typeof(QuestManager), "RpcReader___Server_SendQuestState_4117703421")]
    [HarmonyPrefix]
    private static bool CaptureQuestState(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        if (!TryBeginScopedQuestRead(PooledReader0, out string guid))
        {
            return true;
        }

        EQuestState state = (EQuestState)PooledReader0.ReadInt32();
        OrganisationsServerMod.ActiveInstance?.RecordScopedQuestState(conn, guid, state);
        return false;
    }

    [HarmonyPatch(typeof(QuestManager), "RpcReader___Server_SendQuestEntryState_375159588")]
    [HarmonyPrefix]
    private static bool CaptureQuestEntryState(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        if (!TryBeginScopedQuestRead(PooledReader0, out string guid))
        {
            return true;
        }

        int entryIndex = PooledReader0.ReadInt32();
        EQuestState state = (EQuestState)PooledReader0.ReadInt32();
        OrganisationsServerMod.ActiveInstance?.RecordScopedQuestEntryState(conn, guid, entryIndex, state);
        return false;
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_SendContractAccepted_507093020")]
    [HarmonyPrefix]
    private static void PrepareScopeForContractAccepted(NetworkConnection conn)
    {
        _ = conn;
    }
}
#endif
