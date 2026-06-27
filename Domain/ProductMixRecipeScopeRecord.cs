using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class ProductMixRecipeScopeRecord
{
    public string ProductId { get; set; } = string.Empty;
    public string MixerId { get; set; } = string.Empty;
    public string OutputId { get; set; } = string.Empty;

    public ProductMixRecipeScopeRecord Clone()
    {
        return new ProductMixRecipeScopeRecord
        {
            ProductId = ProductId ?? string.Empty,
            MixerId = MixerId ?? string.Empty,
            OutputId = OutputId ?? string.Empty,
        };
    }
}
