#if CLIENT
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI;
#else
using FishNet.Connection;
using FishNet.Object;
using ScheduleOne.Cartel;
using ScheduleOne.DevUtilities;
using ScheduleOne.Economy;
using ScheduleOne.GameTime;
using ScheduleOne.NPCs.Behaviour;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Quests;
using ScheduleOne.UI;
#endif
using DedicatedServerMod.Organisations.Utils;

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationQuestClientPatches
{
    private const string LoanSharkKidnapMessage = "In the middle of the night, the door is kicked in and you are dragged into a vehicle trunk...";

    private static bool _loanSharkKidnapQueued;

    internal static void ClearLoanSharkKidnapQueue()
    {
        _loanSharkKidnapQueued = false;
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.SetIsTracked))]
    [HarmonyPrefix]
    private static void CapturePreviousTrackedState(Quest __instance, ref bool __state)
    {
        __state = __instance.IsTracked;
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.SetIsTracked))]
    [HarmonyPostfix]
    private static void ForwardScopedQuestTracking(Quest __instance, bool __state)
    {
        if (!QuestScopeRules.ShouldVirtualizeQuest(__instance))
        {
            return;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null
            || !clientMod.HasSnapshot
            || clientMod.IsApplyingQuestScopeSync
            || __state == __instance.IsTracked)
        {
            return;
        }

        clientMod.SubmitQuestTracking(__instance.GUID.ToString(), __instance.IsTracked);
    }

    [HarmonyPatch(typeof(QuestManager), "RpcLogic___CreateContract_Networked_2526053753")]
    [HarmonyPrefix]
    private static bool SuppressCreateContractLogic(NetworkConnection conn, string title, string description, string guid, bool tracked, NetworkObject customer, ContractInfo contractData, GameDateTime expiry, GameDateTime acceptTime, NetworkObject dealerObj)
    {
        _ = conn;
        _ = title;
        _ = description;
        _ = guid;
        _ = tracked;
        _ = customer;
        _ = contractData;
        _ = expiry;
        _ = acceptTime;
        _ = dealerObj;
        return false;
    }

    [HarmonyPatch(typeof(QuestManager), "RpcLogic___ReceiveQuestAction_920727549")]
    [HarmonyPrefix]
    private static bool SuppressQuestActionLogic(NetworkConnection conn, string guid, QuestManager.EQuestAction action)
    {
        _ = conn;
        _ = action;
        return QuestScopeRules.ShouldVirtualizeQuest(guid) ? false : true;
    }

    [HarmonyPatch(typeof(QuestManager), "RpcLogic___ReceiveQuestState_3887376304")]
    [HarmonyPrefix]
    private static bool SuppressQuestStateLogic(NetworkConnection conn, string guid, EQuestState state)
    {
        _ = conn;
        _ = state;
        return QuestScopeRules.ShouldVirtualizeQuest(guid) ? false : true;
    }

    [HarmonyPatch(typeof(QuestManager), "RpcLogic___SetQuestTracked_619441887")]
    [HarmonyPrefix]
    private static bool SuppressQuestTrackedLogic(NetworkConnection conn, string guid, bool tracked)
    {
        _ = conn;
        _ = tracked;
        return QuestScopeRules.ShouldVirtualizeQuest(guid) ? false : true;
    }

    [HarmonyPatch(typeof(QuestManager), "RpcLogic___ReceiveQuestEntryState_311789429")]
    [HarmonyPrefix]
    private static bool SuppressQuestEntryStateLogic(NetworkConnection conn, string guid, int entryIndex, EQuestState state)
    {
        _ = conn;
        _ = entryIndex;
        _ = state;
        return QuestScopeRules.ShouldVirtualizeQuest(guid) ? false : true;
    }

    [HarmonyPatch(typeof(QuestManager), "RpcLogic___CreateDeaddropCollectionQuest_3895153758")]
    [HarmonyPrefix]
    private static bool SuppressDeaddropCreateLogic(NetworkConnection conn, string dropGUID, string guidString)
    {
        _ = conn;
        _ = dropGUID;
        _ = guidString;
        return false;
    }

    [HarmonyPatch(typeof(CartelDealManager), "RpcLogic___InitializeDealQuest_2137933519")]
    [HarmonyPrefix]
    private static bool SuppressCartelDealQuestForNonTrucedScope()
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.CanReceiveCartelDealQuest;
    }

    [HarmonyPatch(typeof(Cartel), "RpcLogic___SetStatus_3666943613")]
    [HarmonyPrefix]
    private static bool SuppressCartelStatusOutsideScopedApply()
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.CanApplyCartelStatusRpc;
    }

    [HarmonyPatch(typeof(CartelInfluence), "RpcLogic___SetInfluence_2071772313")]
    [HarmonyPrefix]
    private static bool SuppressCartelInfluenceSetOutsideScopedApply()
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.CanApplyCartelInfluenceRpc;
    }

    [HarmonyPatch(typeof(CartelInfluence), "RpcLogic___ChangeInfluence_1267088319")]
    [HarmonyPrefix]
    private static bool SuppressCartelInfluenceChangeOutsideScopedApply()
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.CanApplyCartelInfluenceRpc;
    }

    [HarmonyPatch(typeof(CartelActivities), "RpcLogic___StartGlobalActivity_1796582335")]
    [HarmonyPrefix]
    private static bool SuppressGlobalCartelActivityOutsideScopedApply()
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.CanApplyCartelActivityRpc;
    }

    [HarmonyPatch(typeof(CartelRegionActivities), "RpcLogic___StartActivity_2681120339")]
    [HarmonyPrefix]
    private static bool SuppressRegionalCartelActivityOutsideScopedApply()
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.CanApplyCartelActivityRpc;
    }

    [HarmonyPatch(typeof(GraffitiBehaviour), "RpcLogic___SetSpraySurface_Client_1824087381")]
    [HarmonyPrefix]
    private static bool SuppressGraffitiSurfaceOutsideScopedActivity(NetworkConnection conn, NetworkObject surface)
    {
        _ = conn;
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.CanApplyCartelGraffitiSurfaceRpc(surface);
    }

    [HarmonyPatch(typeof(Quest_TheDeepEnd), "BeforeSleep")]
    [HarmonyPrefix]
    private static bool QueueScopedLoanSharkKidnapMessage(Quest_TheDeepEnd __instance)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || !clientMod.ShouldRunLoanSharkKidnap(__instance))
        {
            return true;
        }

        if (Singleton<SleepCanvas>.InstanceExists)
        {
            Singleton<SleepCanvas>.Instance.QueueSleepMessage(LoanSharkKidnapMessage);
            _loanSharkKidnapQueued = true;
        }

        return false;
    }

    [HarmonyPatch(typeof(Quest_TheDeepEnd), "SleepFadeOut")]
    [HarmonyPrefix]
    private static bool RunScopedLoanSharkKidnapTeleport(Quest_TheDeepEnd __instance)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || !_loanSharkKidnapQueued)
        {
            return true;
        }

        if (!clientMod.ShouldRunLoanSharkKidnap(__instance))
        {
            _loanSharkKidnapQueued = false;
            return true;
        }

        _loanSharkKidnapQueued = false;
        if (__instance.MeetingTeleportPoint == null || !PlayerSingleton<PlayerMovement>.InstanceExists || Player.Local == null)
        {
            return false;
        }

        PlayerSingleton<PlayerMovement>.Instance.Teleport(__instance.MeetingTeleportPoint.position);
        Player.Local.transform.forward = __instance.MeetingTeleportPoint.forward;
        return false;
    }
}
#endif
