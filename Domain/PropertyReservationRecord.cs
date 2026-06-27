using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class PropertyReservationRecord
{
    public string PropertyCode { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public string OwnerSteamId { get; set; } = string.Empty;
    public string OwnerOrganisationId { get; set; } = string.Empty;
    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}
