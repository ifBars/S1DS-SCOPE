#if CLIENT
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.Product;
#else
using ScheduleOne.Product;
#endif

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationProductClientPatches
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        string[] methodNames =
        {
            "RpcLogic___RecordContractReceipt_691682765",
            "RpcLogic___SetProductListed_310431262",
            "RpcLogic___SetProductListed_619441887",
            "RpcLogic___SetProductFavourited_310431262",
            "RpcLogic___SetProductFavourited_619441887",
            "RpcLogic___SetProductDiscovered_619441887",
            "RpcLogic___SetMethDiscovered_2166136261",
            "RpcLogic___SetCocaineDiscovered_2166136261",
            "RpcLogic___SetShroomsDiscovered_2166136261",
            "RpcLogic___CreateWeed_1777266891",
            "RpcLogic___CreateCocaine_1327282946",
            "RpcLogic___CreateMeth_1869045686",
            "RpcLogic___CreateShroom_Client_812995776",
            "RpcLogic___CreateMixRecipe_1410895574",
            "RpcLogic___SetPrice_4077118173",
            "RpcLogic___SetMixOperation_3670976965",
            "RpcLogic___FinishAndNameMix_4237212381",
        };

        foreach (string methodName in methodNames)
        {
            MethodInfo? method = AccessTools.Method(typeof(ProductManager), methodName);
            if (method != null)
            {
                yield return method;
            }
        }
    }

    [HarmonyPrefix]
    private static bool SuppressProductEconomyLogic()
    {
        return ShouldAllowProductMarketLogic();
    }

    private static bool ShouldAllowProductMarketLogic()
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null || !clientMod.HasSnapshot || clientMod.IsApplyingQuestScopeSync;
    }
}
#endif
