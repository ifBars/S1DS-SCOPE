using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class VehicleOwnershipRecord
{
    public string VehicleGuid { get; set; } = string.Empty;
    public string VehicleCode { get; set; } = string.Empty;
    public string OwnerSteamId { get; set; } = string.Empty;
    public string OwnerOrganisationId { get; set; } = string.Empty;
    public DateTime PurchasedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
