#if SERVER
using System.Reflection;
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Product;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.NPCs.Behaviour;
using ScheduleOne.ObjectScripts;
using ScheduleOne.Product;
#endif

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationProductRuntimePatches
{
    private static readonly FieldInfo? HarvestMushroomBedField = AccessTools.Field(typeof(HarvestMushroomBedBehaviour), "_bed");

    [HarmonyPatch(typeof(ProductManager), nameof(ProductManager.OnSpawnServer))]
    [HarmonyPrefix]
    private static void ProductManagerOnSpawnServerPrefix(NetworkConnection connection)
    {
        OrganisationsServerMod.ActiveInstance?.PrepareProductMarketReplication(connection);
    }

    [HarmonyPatch(typeof(ProductManager), "OnNewDay")]
    [HarmonyPrefix]
    private static bool ProductManagerOnNewDayPrefix()
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        serverMod.CompleteScopedProductMixesOnNewDay();
        return false;
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetProductListed_310431262")]
    [HarmonyPrefix]
    private static void ProductListedReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetProductListed_310431262")]
    [HarmonyPostfix]
    private static void ProductListedReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetProductFavourited_310431262")]
    [HarmonyPrefix]
    private static void ProductFavouritedReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetProductFavourited_310431262")]
    [HarmonyPostfix]
    private static void ProductFavouritedReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_DiscoverProduct_3615296227")]
    [HarmonyPrefix]
    private static void ProductDiscoverReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_DiscoverProduct_3615296227")]
    [HarmonyPostfix]
    private static void ProductDiscoverReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SendPrice_606697822")]
    [HarmonyPrefix]
    private static void ProductPriceReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SendPrice_606697822")]
    [HarmonyPostfix]
    private static void ProductPriceReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetMethDiscovered_2166136261")]
    [HarmonyPrefix]
    private static void MethDiscoveredReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetMethDiscovered_2166136261")]
    [HarmonyPostfix]
    private static void MethDiscoveredReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetCocaineDiscovered_2166136261")]
    [HarmonyPrefix]
    private static void CocaineDiscoveredReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetCocaineDiscovered_2166136261")]
    [HarmonyPostfix]
    private static void CocaineDiscoveredReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetShroomsDiscovered_2166136261")]
    [HarmonyPrefix]
    private static void ShroomsDiscoveredReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SetShroomsDiscovered_2166136261")]
    [HarmonyPostfix]
    private static void ShroomsDiscoveredReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SendMixOperation_3670976965")]
    [HarmonyPrefix]
    private static void SendMixOperationReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SendMixOperation_3670976965")]
    [HarmonyPostfix]
    private static void SendMixOperationReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SendFinishAndNameMix_4237212381")]
    [HarmonyPrefix]
    private static void SendFinishAndNameMixReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SendFinishAndNameMix_4237212381")]
    [HarmonyPostfix]
    private static void SendFinishAndNameMixReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SendMixRecipe_852232071")]
    [HarmonyPrefix]
    private static void SendMixRecipeReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_SendMixRecipe_852232071")]
    [HarmonyPostfix]
    private static void SendMixRecipeReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_CreateWeed_Server_2331775230")]
    [HarmonyPrefix]
    private static void CreateWeedReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_CreateWeed_Server_2331775230")]
    [HarmonyPostfix]
    private static void CreateWeedReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_CreateCocaine_Server_891166717")]
    [HarmonyPrefix]
    private static void CreateCocaineReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_CreateCocaine_Server_891166717")]
    [HarmonyPostfix]
    private static void CreateCocaineReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_CreateMeth_Server_4251728555")]
    [HarmonyPrefix]
    private static void CreateMethReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_CreateMeth_Server_4251728555")]
    [HarmonyPostfix]
    private static void CreateMethReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_CreateShroom_Server_2261384965")]
    [HarmonyPrefix]
    private static void CreateShroomReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        PrepareProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(ProductManager), "RpcReader___Server_CreateShroom_Server_2261384965")]
    [HarmonyPostfix]
    private static void CreateShroomReaderPostfix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        RecordProductMarketMutation(PooledReader0, channel, conn);
    }

    [HarmonyPatch(typeof(LabOven), "OutputSlotChanged")]
    [HarmonyPrefix]
    private static void LabOvenOutputSlotChangedPrefix(LabOven __instance)
    {
        OrganisationsServerMod.ActiveInstance?.PrepareProductMarketForProperty(__instance?.ParentProperty);
    }

    [HarmonyPatch(typeof(LabOven), "OutputSlotChanged")]
    [HarmonyPostfix]
    private static void LabOvenOutputSlotChangedPostfix(LabOven __instance)
    {
        OrganisationsServerMod.ActiveInstance?.RecordProductMarketForProperty(__instance?.ParentProperty);
    }

    [HarmonyPatch(typeof(HarvestMushroomBedBehaviour), "OnActionSuccess")]
    [HarmonyPrefix]
    private static void HarvestMushroomBedActionSuccessPrefix(HarvestMushroomBedBehaviour __instance)
    {
        OrganisationsServerMod.ActiveInstance?.PrepareProductMarketForProperty(GetHarvestMushroomBed(__instance)?.ParentProperty);
    }

    [HarmonyPatch(typeof(HarvestMushroomBedBehaviour), "OnActionSuccess")]
    [HarmonyPostfix]
    private static void HarvestMushroomBedActionSuccessPostfix(HarvestMushroomBedBehaviour __instance)
    {
        OrganisationsServerMod.ActiveInstance?.RecordProductMarketForProperty(GetHarvestMushroomBed(__instance)?.ParentProperty);
    }

    private static void PrepareProductMarketMutation(PooledReader reader, Channel channel, NetworkConnection conn)
    {
        _ = reader;
        _ = channel;

        OrganisationsServerMod.ActiveInstance?.PrepareProductMarketMutation(conn);
    }

    private static void RecordProductMarketMutation(PooledReader reader, Channel channel, NetworkConnection conn)
    {
        _ = reader;
        _ = channel;

        OrganisationsServerMod.ActiveInstance?.RecordProductMarketMutation(conn);
    }

    private static MushroomBed? GetHarvestMushroomBed(HarvestMushroomBedBehaviour behaviour)
    {
        return HarvestMushroomBedField?.GetValue(behaviour) as MushroomBed;
    }
}
#endif
