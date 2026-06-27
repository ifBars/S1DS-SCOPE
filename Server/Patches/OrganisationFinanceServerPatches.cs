#if SERVER
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.Persistence.Datas;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.DevUtilities;
using ScheduleOne.GameTime;
using ScheduleOne.Money;
using ScheduleOne.Persistence.Datas;
#endif

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationFinanceServerPatches
{
    [HarmonyPatch(typeof(MoneyManager), "RpcLogic___CreateOnlineTransaction_1419830531")]
    [HarmonyPrefix]
    private static bool SuppressServerLocalOnlineTransactionLogic()
    {
        return false;
    }

    [HarmonyPatch(typeof(MoneyManager), "RpcReader___Server_CreateOnlineTransaction_1419830531")]
    [HarmonyPrefix]
    private static bool MoneyManagerServerTransactionPrefix(MoneyManager __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        string transactionName = PooledReader0.ReadString();
        float unitAmount = PooledReader0.ReadSingle();
        float quantity = PooledReader0.ReadSingle();
        string transactionNote = PooledReader0.ReadString();

        if (!__instance.IsServerInitialized || conn.IsLocalClient)
        {
            return false;
        }

        serverMod.TryHandleVanillaOnlineTransaction(conn, transactionName, unitAmount, quantity, transactionNote);
        return false;
    }

    [HarmonyPatch(typeof(MoneyManager), nameof(MoneyManager.Load))]
    [HarmonyPrefix]
    private static bool MoneyManagerLoadPrefix(MoneyManager __instance, MoneyData data)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        serverMod.OnMoneyManagerLoaded(__instance, data);
        return false;
    }

    [HarmonyPatch(typeof(MoneyManager), nameof(MoneyManager.GetSaveString))]
    [HarmonyPrefix]
    private static bool MoneyManagerGetSaveStringPrefix(MoneyManager __instance, ref string __result)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        __result = serverMod.BuildNeutralMoneySaveString(__instance);
        return false;
    }

    [HarmonyPatch(typeof(MoneyManager), "RpcLogic___ReceiveOnlineTransaction_1419830531")]
    [HarmonyPrefix]
    private static bool SuppressVanillaOnlineTransactionReceive()
    {
        return false;
    }

    [HarmonyPatch(typeof(ATM), nameof(ATM.WeekPass))]
    [HarmonyPostfix]
    private static void ATMWeekPassPostfix()
    {
        OrganisationsServerMod.ActiveInstance?.ResetWeeklyDepositSums();
    }

    [HarmonyPatch(typeof(ATM), nameof(ATM.DayPass))]
    [HarmonyPostfix]
    private static void ATMDayPassPostfix(ATM __instance)
    {
        if (!__instance.IsServer)
        {
            return;
        }

        TimeManager? timeManager = NetworkSingleton<TimeManager>.Instance;
        if (timeManager == null || timeManager.CurrentDay != EDay.Sunday)
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.SendDealerRetentionWarningsForCurrentDay();
    }
}
#endif
