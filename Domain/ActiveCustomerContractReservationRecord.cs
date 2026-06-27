using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class ActiveCustomerContractReservationRecord
{
    public string CustomerNpcGuid { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public string ContractGuid { get; set; } = string.Empty;
    public string DeliveryLocationGuid { get; set; } = string.Empty;
    public DateTime AcceptedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
