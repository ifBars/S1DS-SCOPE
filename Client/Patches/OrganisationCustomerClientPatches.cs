#if CLIENT
using DedicatedServerMod.Organisations.Contracts;
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppScheduleOne;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Quests;
using Guid = Il2CppSystem.Guid;
using PersistenceCustomerData = Il2CppScheduleOne.Persistence.Datas.CustomerData;
#else
using FishNet.Connection;
using ScheduleOne;
using ScheduleOne.DevUtilities;
using ScheduleOne.Economy;
using ScheduleOne.GameTime;
using ScheduleOne.Map;
using ScheduleOne.NPCs;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Quests;
using PersistenceCustomerData = ScheduleOne.Persistence.Datas.CustomerData;
#endif

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationCustomerClientPatches
{
    private static bool IsEmptyGuid(Guid guid)
    {
        return guid.Equals(Guid.Empty);
    }

    [HarmonyPatch(typeof(Customer), "ShowDirectApproachOption")]
    [HarmonyPrefix]
    private static bool ShowDirectApproachOptionPrefix(Customer __instance, bool enabled, ref bool __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance?.NPC == null || IsEmptyGuid(__instance.NPC.GUID))
        {
            return true;
        }

        if (!clientMod.TryGetCustomerScope(__instance.NPC.GUID.ToString(), out CustomerScopeEntryDto? state))
        {
            return true;
        }

        if (Player.Local.CrimeData.CurrentPursuitLevel != PlayerCrimeData.EPursuitLevel.None && Player.Local.CrimeData.TimeSinceSighted < 5f)
        {
            __result = false;
            return false;
        }

        __result = enabled && __instance.CustomerData.CanBeDirectlyApproached && !__instance.IsAwaitingDelivery && !state.IsUnlocked;
        return false;
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.IsUnlockable))]
    [HarmonyPrefix]
    private static bool IsUnlockablePrefix(Customer __instance, ref bool __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance?.NPC == null || IsEmptyGuid(__instance.NPC.GUID))
        {
            return true;
        }

        if (!clientMod.TryGetCustomerScope(__instance.NPC.GUID.ToString(), out CustomerScopeEntryDto? state))
        {
            return true;
        }

        __result = !state.IsUnlocked && __instance.NPC.RelationData.IsMutuallyKnown();
        return false;
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.KnownAndRecommended))]
    [HarmonyPrefix]
    private static bool KnownAndRecommendedPrefix(Customer __instance, ref bool __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance?.NPC == null || IsEmptyGuid(__instance.NPC.GUID))
        {
            return true;
        }

        if (!clientMod.TryGetCustomerScope(__instance.NPC.GUID.ToString(), out CustomerScopeEntryDto? state))
        {
            return true;
        }

        if (!GameManager.IS_TUTORIAL && !Singleton<Map>.Instance.GetRegionData(__instance.NPC.Region).IsUnlocked)
        {
            __result = false;
            return false;
        }

        __result = state.HasBeenRecommended && __instance.NPC.RelationData.IsMutuallyKnown();
        return false;
    }

    [HarmonyPatch(typeof(Customer), "RpcLogic___ReceiveCustomerData_2280244125")]
    [HarmonyPrefix]
    private static bool SuppressVanillaCustomerDataLogic(Customer __instance, NetworkConnection conn, PersistenceCustomerData data)
    {
        _ = conn;
        _ = data;
        return !HasScopedCustomerEntry(__instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcLogic___SetOfferedContract_4277245194")]
    [HarmonyPrefix]
    private static bool SuppressVanillaOfferLogic(Customer __instance, ContractInfo info, GameDateTime offerTime)
    {
        _ = info;
        _ = offerTime;
        return !HasScopedCustomerEntry(__instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcLogic___SetContractIsCounterOffer_2166136261")]
    [HarmonyPrefix]
    private static bool SuppressVanillaCounterOfferLogic(Customer __instance)
    {
        return !HasScopedCustomerEntry(__instance);
    }

    [HarmonyPatch(typeof(Customer), "ContractRejected")]
    [HarmonyPrefix]
    private static bool ContractRejectedPrefix(Customer __instance)
    {
        if (__instance?.NPC == null || IsEmptyGuid(__instance.NPC.GUID) || __instance.OfferedContractInfo == null)
        {
            return true;
        }

        OrganisationsClientMod.ActiveInstance?.SubmitCustomerOfferRejection(__instance.NPC.GUID.ToString());
        return true;
    }

    [HarmonyPatch(typeof(Customer), "RpcLogic___ReceiveContractAccepted_2166136261")]
    [HarmonyPrefix]
    private static bool SuppressVanillaAcceptLogic(Customer __instance)
    {
        return !HasScopedCustomerEntry(__instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcLogic___ReceiveContractRejected_2166136261")]
    [HarmonyPrefix]
    private static bool SuppressVanillaRejectLogic(Customer __instance)
    {
        return !HasScopedCustomerEntry(__instance);
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.ContractWellReceived))]
    [HarmonyPrefix]
    private static bool ContractWellReceivedPrefix(string npcToRecommend)
    {
        return OrganisationsClientMod.ActiveInstance == null || string.IsNullOrWhiteSpace(npcToRecommend);
    }

    [HarmonyPatch(typeof(NPC), "RpcLogic___ReceiveRelationshipData_4052192084")]
    [HarmonyPrefix]
    private static bool SuppressVanillaRelationshipLogic(NPC __instance, NetworkConnection conn, float relationship, bool unlocked)
    {
        _ = conn;
        _ = relationship;
        _ = unlocked;
        if (__instance == null || IsEmptyGuid(__instance.GUID))
        {
            return true;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || !clientMod.HasSnapshot || !clientMod.TryGetCustomerScope(__instance.GUID.ToString(), out _);
    }

    private static bool HasScopedCustomerEntry(Customer customer)
    {
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return false;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod != null && clientMod.HasSnapshot && clientMod.TryGetCustomerScope(customer.NPC.GUID.ToString(), out _);
    }
}
#endif
