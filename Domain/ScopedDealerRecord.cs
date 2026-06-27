using System;
using System.Collections.Generic;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class ScopedDealerRecord
{
    public string OwnerKey { get; set; } = string.Empty;
    public string NpcId { get; set; } = string.Empty;
    public bool HasBeenRecommended { get; set; }
    public bool IsRecruited { get; set; }
    public float Cash { get; set; }
    public int CompletedDeals { get; set; }
    public List<string> AssignedCustomerNpcIds { get; set; } = new List<string>();
    public string InventoryItemsJson { get; set; } = string.Empty;
    public string InventorySlotFiltersJson { get; set; } = string.Empty;
    public int LastRetentionWarningElapsedDay { get; set; } = -1;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
