using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class ScopedCustomerRecord
{
    public string OwnerKey { get; set; } = string.Empty;
    public string NpcGuid { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; }
    public float RelationshipDelta { get; set; } = 2f;
    public bool HasBeenRecommended { get; set; }
    public int OfferedDeals { get; set; }
    public int CompletedDeliveries { get; set; }
    public string CustomerDataJson { get; set; } = string.Empty;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
