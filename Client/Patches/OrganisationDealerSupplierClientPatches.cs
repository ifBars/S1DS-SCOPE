#if CLIENT
using DedicatedServerMod.Organisations.Contracts;
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet;
using Il2CppFishNet.Connection;
using Il2CppScheduleOne.Economy;
#else
using FishNet;
using FishNet.Connection;
using ScheduleOne.Economy;
#endif

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationDealerSupplierClientPatches
{
    [HarmonyPatch(typeof(Dealer), "CanOfferRecruitment")]
    [HarmonyPrefix]
    private static bool DealerCanOfferRecruitmentPrefix(Dealer __instance, ref bool __result, ref string reason)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance == null || string.IsNullOrWhiteSpace(__instance.ID))
        {
            return true;
        }

        if (!clientMod.TryGetDealerScope(__instance.ID, out DealerScopeEntryDto? state))
        {
            return true;
        }

        reason = string.Empty;
        if (state.BusyWithOtherScope)
        {
            reason = __instance.FirstName + " already works for another crew";
            __result = false;
            return false;
        }

        if (state.IsRecruited)
        {
            __result = false;
            return false;
        }

        if (!__instance.RelationData.IsMutuallyKnown())
        {
            reason = "Unlock one of " + __instance.FirstName + "'s connections";
            __result = false;
            return false;
        }

        if (!state.HasBeenRecommended)
        {
            reason = "Must be recommended by one of " + __instance.FirstName + "'s connections";
            __result = false;
            return false;
        }

        __result = true;
        return false;
    }

    [HarmonyPatch(typeof(Dealer), "RpcLogic___SetRecommended_2166136261")]
    [HarmonyPrefix]
    private static bool SuppressVanillaDealerRecommendedLogic()
    {
        return false;
    }

    [HarmonyPatch(typeof(Dealer), "TradeItems")]
    [HarmonyPrefix]
    private static bool DealerTradeItemsPrefix(Dealer __instance)
    {
        return CanUseScopedDealer(__instance);
    }

    [HarmonyPatch(typeof(Dealer), "CanCollectCash")]
    [HarmonyPrefix]
    private static bool DealerCanCollectCashPrefix(Dealer __instance, ref bool __result, ref string reason)
    {
        if (CanUseScopedDealer(__instance))
        {
            return true;
        }

        reason = __instance != null ? __instance.FirstName + " does not work for your scope" : "Dealer is unavailable";
        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(Dealer), "RpcLogic___AddCustomer_Client_2971853958")]
    [HarmonyPrefix]
    private static bool SuppressVanillaDealerAddCustomerLogic()
    {
        return false;
    }

    [HarmonyPatch(typeof(Dealer), "RpcLogic___RemoveCustomer_3615296227")]
    [HarmonyPrefix]
    private static bool SuppressVanillaDealerRemoveCustomerLogic()
    {
        return false;
    }

    [HarmonyPatch(typeof(Supplier), "RpcLogic___SetUnlocked_2166136261")]
    [HarmonyPrefix]
    private static bool SuppressVanillaSupplierUnlockedLogic()
    {
        return false;
    }

    [HarmonyPatch(typeof(Supplier), "RpcLogic___EnableDeliveries_328543758")]
    [HarmonyPrefix]
    private static bool SuppressVanillaSupplierDeliveryUnlockLogic()
    {
        return false;
    }

    [HarmonyPatch(typeof(Supplier), "RpcLogic___MeetAtLocation_3470796954")]
    [HarmonyPrefix]
    private static bool SupplierMeetAtLocationPrefix(Supplier __instance, NetworkConnection conn)
    {
        if (conn != null && InstanceFinder.ClientManager?.Connection != null && conn.ClientId != InstanceFinder.ClientManager.Connection.ClientId)
        {
            return false;
        }

        return CanUseScopedSupplier(__instance);
    }

    private static bool CanUseScopedDealer(Dealer dealer)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || dealer == null || string.IsNullOrWhiteSpace(dealer.ID))
        {
            return true;
        }

        return clientMod.TryGetDealerScope(dealer.ID, out DealerScopeEntryDto? state)
            && state.IsRecruited
            && !state.BusyWithOtherScope;
    }

    private static bool CanUseScopedSupplier(Supplier supplier)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return true;
        }

        return clientMod.TryGetSupplierScope(supplier.ID, out SupplierScopeEntryDto? state)
            && state.IsUnlocked;
    }
}
#endif
