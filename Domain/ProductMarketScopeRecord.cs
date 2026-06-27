using System;
using System.Collections.Generic;
using System.Linq;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class ProductMarketScopeRecord
{
    public HashSet<string> DiscoveredProductIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> ListedProductIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> FavouritedProductIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> CreatedProductIds { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, float> PricesByProductId { get; set; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    public List<ProductMixRecipeScopeRecord> MixRecipes { get; set; } = new List<ProductMixRecipeScopeRecord>();
    public List<string> ContractReceiptJson { get; set; } = new List<string>();
    public bool IsAcceptingOrders { get; set; } = true;
    public string CurrentMixOperationJson { get; set; } = string.Empty;
    public bool IsMixComplete { get; set; }

    public ProductMarketScopeRecord Clone()
    {
        return new ProductMarketScopeRecord
        {
            DiscoveredProductIds = new HashSet<string>(DiscoveredProductIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase),
            ListedProductIds = new HashSet<string>(ListedProductIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase),
            FavouritedProductIds = new HashSet<string>(FavouritedProductIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase),
            CreatedProductIds = new HashSet<string>(CreatedProductIds ?? new HashSet<string>(), StringComparer.OrdinalIgnoreCase),
            PricesByProductId = new Dictionary<string, float>(PricesByProductId ?? new Dictionary<string, float>(), StringComparer.OrdinalIgnoreCase),
            MixRecipes = (MixRecipes ?? new List<ProductMixRecipeScopeRecord>()).Select(recipe => recipe?.Clone() ?? new ProductMixRecipeScopeRecord()).ToList(),
            ContractReceiptJson = new List<string>(ContractReceiptJson ?? new List<string>()),
            IsAcceptingOrders = IsAcceptingOrders,
            CurrentMixOperationJson = CurrentMixOperationJson ?? string.Empty,
            IsMixComplete = IsMixComplete,
        };
    }
}
