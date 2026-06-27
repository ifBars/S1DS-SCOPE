#if SERVER
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.UI.Shop;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.UI.Shop;
#endif

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationShopRuntimePatches
{
    [HarmonyPatch(typeof(ShopManager), "RpcReader___Server_SendStock_15643032")]
    [HarmonyPrefix]
    private static bool ShopManagerSendStockPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        string shopCode = PooledReader0.ReadString();
        string itemId = PooledReader0.ReadString();
        int requestedStock = PooledReader0.ReadInt32();

        ShopListing? listing = FindListing(shopCode, itemId);
        if (listing == null || listing.IsUnlimitedStock)
        {
            PooledReader0.Position = originalPosition;
            return true;
        }

        bool canApplyStock = requestedStock >= 0 && requestedStock <= listing.CurrentStock;
        if (canApplyStock)
        {
            PooledReader0.Position = originalPosition;
        }

        return canApplyStock;
    }

    private static ShopListing? FindListing(string shopCode, string itemId)
    {
        for (int shopIndex = 0; shopIndex < ShopInterface.AllShops.Count; shopIndex++)
        {
            ShopInterface shop = ShopInterface.AllShops[shopIndex];
            if (shop == null || shop.ShopCode != shopCode)
            {
                continue;
            }

            for (int listingIndex = 0; listingIndex < shop.Listings.Count; listingIndex++)
            {
                ShopListing listing = shop.Listings[listingIndex];
                if (listing != null && GetListingItemId(listing) == itemId)
                {
                    return listing;
                }
            }
        }

        return null;
    }

    private static string? GetListingItemId(ShopListing listing)
    {
        object? item = AccessTools.Property(listing.GetType(), "Item")?.GetValue(listing);
        return item == null ? null : AccessTools.Property(item.GetType(), "ID")?.GetValue(item) as string;
    }
}
#endif
