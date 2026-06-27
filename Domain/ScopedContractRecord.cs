using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class ScopedContractRecord
{
    public string ContractGuid { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public string CustomerNpcGuid { get; set; } = string.Empty;
    public string DeliveryLocationGuid { get; set; } = string.Empty;
    public string ContractDataJson { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
