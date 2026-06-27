using System;
using System.Collections.Generic;
using DedicatedServerMod.Organisations.Domain;

namespace DedicatedServerMod.Organisations.Persistence;

[Serializable]
internal sealed class OrganisationSaveData
{
    public const int CurrentVersion = 3;

    public int Version { get; set; } = CurrentVersion;
    public Dictionary<string, OrganisationRecord> Organisations { get; set; } = new Dictionary<string, OrganisationRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> PlayerToOrganisation { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, OrganisationInvite> PendingInvites { get; set; } = new Dictionary<string, OrganisationInvite>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ScopedWalletRecord> ScopedWallets { get; set; } = new Dictionary<string, ScopedWalletRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PropertyReservationRecord> PropertyReservations { get; set; } = new Dictionary<string, PropertyReservationRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ScopedCustomerRecord> ScopedCustomers { get; set; } = new Dictionary<string, ScopedCustomerRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ScopedDealerRecord> ScopedDealers { get; set; } = new Dictionary<string, ScopedDealerRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ScopedSupplierRecord> ScopedSuppliers { get; set; } = new Dictionary<string, ScopedSupplierRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ActiveCustomerContractReservationRecord> ActiveCustomerContracts { get; set; } = new Dictionary<string, ActiveCustomerContractReservationRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, ScopedContractRecord> ScopedContracts { get; set; } = new Dictionary<string, ScopedContractRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, QuestScopeRecord> QuestScopes { get; set; } = new Dictionary<string, QuestScopeRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PropertyScopeRecord> PropertyScopes { get; set; } = new Dictionary<string, PropertyScopeRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, VehicleOwnershipRecord> VehicleOwnerships { get; set; } = new Dictionary<string, VehicleOwnershipRecord>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, PhysicalWorldReservationRecord> PhysicalWorldReservations { get; set; } = new Dictionary<string, PhysicalWorldReservationRecord>(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> PlayersShownOnboardingPrompt { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
}
