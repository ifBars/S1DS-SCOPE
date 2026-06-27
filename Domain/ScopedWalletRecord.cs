using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class ScopedWalletRecord
{
    public string OwnerKey { get; set; } = string.Empty;
    public float OnlineBalance { get; set; }
    public float WeeklyDepositSum { get; set; }
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
