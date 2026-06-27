using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class ScopedSupplierRecord
{
    public string OwnerKey { get; set; } = string.Empty;
    public string NpcId { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; }
    public float RelationshipDelta { get; set; } = 2f;
    public bool DeliveriesEnabled { get; set; }
    public float Debt { get; set; }
    public bool DeadDropPreparing { get; set; }
    public int MinsUntilDeadDropReady { get; set; } = -1;
    public string DeaddropItemsJson { get; set; } = string.Empty;
    public bool DebtReminderSent { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
