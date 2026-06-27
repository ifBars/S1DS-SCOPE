#if CLIENT
using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.UI.ATM;
using Il2CppScheduleOne.UI.Shop;
#else
using ScheduleOne.Interaction;
using ScheduleOne.Money;
using ScheduleOne.UI.ATM;
using ScheduleOne.UI.Shop;
#endif

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationFinanceClientPatches
{
    [HarmonyPatch(typeof(MoneyManager), "RpcLogic___CreateOnlineTransaction_1419830531")]
    [HarmonyPrefix]
    private static bool SuppressClientLocalOnlineTransactionLogic()
    {
        return false;
    }

    [HarmonyPatch(typeof(MoneyManager), "RpcLogic___ReceiveOnlineTransaction_1419830531")]
    [HarmonyPrefix]
    private static bool SuppressVanillaOnlineTransactionReceive()
    {
        return false;
    }

    [HarmonyPatch(typeof(ATM), nameof(ATM.Hovered))]
    [HarmonyPrefix]
    private static bool ATMHoveredPrefix(InteractableObject ___intObj)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || clientMod.CanUseScopedFinance)
        {
            return true;
        }

        ___intObj.SetMessage(clientMod.GetAtmUnavailableReason());
        ___intObj.SetInteractableState(InteractableObject.EInteractableState.Disabled);
        return false;
    }

    [HarmonyPatch(typeof(ATM), nameof(ATM.Enter))]
    [HarmonyPrefix]
    private static bool ATMEnterPrefix()
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.CanUseScopedFinance;
    }

    [HarmonyPatch(typeof(Cart), nameof(Cart.Buy))]
    [HarmonyPrefix]
    private static bool CartBuyPrefix(Cart __instance)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || clientMod.TryHandleShopBuy(__instance);
    }

    [HarmonyPatch(typeof(ATMInterface), "ProcessTransaction")]
    [HarmonyPrefix]
    private static bool ATMProcessTransactionPrefix(ATMInterface __instance, float amount, bool depositing, ref System.Collections.IEnumerator __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null)
        {
            return true;
        }

        __result = clientMod.ProcessAtmTransaction(__instance, amount, depositing);
        return false;
    }
}
#endif
