using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class PhysicalWorldReservationRecord
{
    public string ReservationId { get; set; } = string.Empty;
    public string OwnerKey { get; set; } = string.Empty;
    public DateTime ReservedAtUtc { get; set; } = DateTime.UtcNow;
}
