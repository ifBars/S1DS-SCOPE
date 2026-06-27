using System;
using System.Collections.Generic;
using DedicatedServerMod.Organisations.Domain;

namespace DedicatedServerMod.Organisations.Contracts;

[Serializable]
internal sealed class CreateOrganisationRequestDto
{
    public string Name { get; set; } = string.Empty;
}

[Serializable]
internal sealed class InvitePlayerRequestDto
{
    public string TargetPlayer { get; set; } = string.Empty;
}

[Serializable]
internal sealed class InviteActionRequestDto
{
    public string InviteId { get; set; } = string.Empty;
}

[Serializable]
internal sealed class QuestTrackingRequestDto
{
    public string QuestGuid { get; set; } = string.Empty;
    public bool IsTracked { get; set; }
}

[Serializable]
internal sealed class CustomerOfferRejectRequestDto
{
    public string NpcGuid { get; set; } = string.Empty;
}

[Serializable]
internal sealed class OrganisationAtmTransactionRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public float Amount { get; set; }
    public bool IsDeposit { get; set; }
}

[Serializable]
internal sealed class OrganisationAtmTransactionResultDto
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public float Amount { get; set; }
    public bool IsDeposit { get; set; }
    public string Message { get; set; } = string.Empty;
}

[Serializable]
internal sealed class OrganisationShopCheckoutLineDto
{
    public string ItemId { get; set; } = string.Empty;
    public int Quantity { get; set; }
}

[Serializable]
internal sealed class OrganisationShopCheckoutRequestDto
{
    public string RequestId { get; set; } = string.Empty;
    public string ShopCode { get; set; } = string.Empty;
    public string ShopName { get; set; } = string.Empty;
    public float Total { get; set; }
    public List<OrganisationShopCheckoutLineDto> Lines { get; set; } = new List<OrganisationShopCheckoutLineDto>();
}

[Serializable]
internal sealed class OrganisationShopCheckoutResultDto
{
    public string RequestId { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
}

[Serializable]
internal sealed class LeaveOrganisationRequestDto
{
}

[Serializable]
internal sealed class KickOrganisationMemberRequestDto
{
    public string MemberSteamId { get; set; } = string.Empty;
}

[Serializable]
internal sealed class TransferOrganisationOwnershipRequestDto
{
    public string NewOwnerSteamId { get; set; } = string.Empty;
}

[Serializable]
internal sealed class OrganisationInviteDto
{
    public string InviteId { get; set; } = string.Empty;
    public string OrganisationId { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public string InviterSteamId { get; set; } = string.Empty;
    public string InviterName { get; set; } = string.Empty;
    public long ExpiresAtUnixTimeSeconds { get; set; }
}

[Serializable]
internal sealed class OrganisationMemberDto
{
    public string SteamId { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public bool IsOwner { get; set; }
    public bool IsOnline { get; set; }
}

[Serializable]
internal sealed class OrganisationSnapshotDto
{
    public bool HasOrganisation { get; set; }
    public bool UsesSharedScope { get; set; }
    public bool ShouldShowOnboarding { get; set; }
    public bool ShouldApplyTeamColorToOutfit { get; set; }
    public string OrganisationId { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public string TeamColorHex { get; set; } = string.Empty;
    public string ScopeKind { get; set; } = string.Empty;
    public string ScopeName { get; set; } = string.Empty;
    public string ScopeOwnerKey { get; set; } = string.Empty;
    public string OwnerSteamId { get; set; } = string.Empty;
    public string PlayerSteamId { get; set; } = string.Empty;
    public string PlayerRole { get; set; } = string.Empty;
    public float OnlineBalance { get; set; }
    public float WeeklyDepositSum { get; set; }
    public float VictoryOnlineBalanceTarget { get; set; }
    public bool HasReachedVictoryTarget { get; set; }
    public long VictoryAchievedAtUnixTimeSeconds { get; set; }
    public List<string> MemberSteamIds { get; set; } = new List<string>();
    public List<string> OwnedPropertyCodes { get; set; } = new List<string>();
    public List<string> AccessiblePropertyCodes { get; set; } = new List<string>();
    public List<string> ReservedPropertyCodes { get; set; } = new List<string>();
    public List<string> OwnedVehicleGuids { get; set; } = new List<string>();
    public List<string> AccessibleVehicleGuids { get; set; } = new List<string>();
    public List<OrganisationMemberDto> Members { get; set; } = new List<OrganisationMemberDto>();
    public List<OrganisationInviteDto> PendingInvites { get; set; } = new List<OrganisationInviteDto>();
}

[Serializable]
internal sealed class OrganisationErrorDto
{
    public string Message { get; set; } = string.Empty;
}

[Serializable]
internal sealed class OrganisationNotificationDto
{
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
}

[Serializable]
internal sealed class OrganisationVictoryDto
{
    public string OrganisationId { get; set; } = string.Empty;
    public string OrganisationName { get; set; } = string.Empty;
    public float OnlineBalance { get; set; }
    public float TargetOnlineBalance { get; set; }
    public List<string> MemberSteamIds { get; set; } = new List<string>();
}

[Serializable]
internal sealed class QuestScopeSyncDto
{
    public string OwnerKey { get; set; } = string.Empty;
    public string CartelStatus { get; set; } = string.Empty;
    public string CartelDealDataJson { get; set; } = string.Empty;
    public Dictionary<string, float> CartelInfluenceByRegion { get; set; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    public CartelActivityScopeRecord CartelActivityState { get; set; } = new CartelActivityScopeRecord();
    public Dictionary<string, bool> MapRegionUnlockedByRegion { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    public bool DarkMarketUnlocked { get; set; }
    public bool SewerUnlocked { get; set; }
    public bool LoanSharksArrived { get; set; }
    public float HoursSinceLoanSharksArrived { get; set; }
    public ProductMarketScopeRecord ProductMarketState { get; set; } = new ProductMarketScopeRecord();
    public List<ScopedQuestSyncDto> Quests { get; set; } = new List<ScopedQuestSyncDto>();
    public List<ScopedContractSyncDto> Contracts { get; set; } = new List<ScopedContractSyncDto>();
    public List<ScopedDeaddropQuestSyncDto> Deaddrops { get; set; } = new List<ScopedDeaddropQuestSyncDto>();
    public List<string> InaccessibleDeaddropGuids { get; set; } = new List<string>();
}

[Serializable]
internal sealed class QuestScopeSyncChunkDto
{
    public string OwnerKey { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public int Total { get; set; }
    public QuestScopeSyncDto Scope { get; set; } = new QuestScopeSyncDto();
}

[Serializable]
internal sealed class ScopedQuestEntrySyncDto
{
    public string Name { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
}

[Serializable]
internal class ScopedQuestSyncDto
{
    public string Guid { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public bool IsTracked { get; set; }
    public bool Expires { get; set; }
    public int ExpiryElapsedDays { get; set; }
    public int ExpiryTime { get; set; }
    public List<ScopedQuestEntrySyncDto> Entries { get; set; } = new List<ScopedQuestEntrySyncDto>();
}

[Serializable]
internal sealed class ScopedContractSyncDto : ScopedQuestSyncDto
{
    public string CustomerGuid { get; set; } = string.Empty;
    public float Payment { get; set; }
    public string ProductListJson { get; set; } = string.Empty;
    public string DeliveryLocationGuid { get; set; } = string.Empty;
    public bool DeliveryWindowEnabled { get; set; }
    public int DeliveryWindowStartTime { get; set; }
    public int DeliveryWindowEndTime { get; set; }
    public int PickupScheduleIndex { get; set; }
    public int AcceptElapsedDays { get; set; }
    public int AcceptTime { get; set; }
}

[Serializable]
internal sealed class ScopedDeaddropQuestSyncDto : ScopedQuestSyncDto
{
    public string DeaddropGuid { get; set; } = string.Empty;
}

[Serializable]
internal sealed class CustomerScopeEntryDto
{
    public string NpcGuid { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; }
    public float RelationshipDelta { get; set; } = 2f;
    public bool HasBeenRecommended { get; set; }
    public int OfferedDeals { get; set; }
    public int CompletedDeliveries { get; set; }
    public bool HasActiveContract { get; set; }
    public bool BusyWithOtherScope { get; set; }
    public string CustomerDataJson { get; set; } = string.Empty;
}

[Serializable]
internal sealed class DealerScopeEntryDto
{
    public string NpcId { get; set; } = string.Empty;
    public bool HasBeenRecommended { get; set; }
    public bool IsRecruited { get; set; }
    public bool BusyWithOtherScope { get; set; }
    public float Cash { get; set; }
    public int CompletedDeals { get; set; }
    public List<string> AssignedCustomerNpcIds { get; set; } = new List<string>();
}

[Serializable]
internal sealed class SupplierScopeEntryDto
{
    public string NpcId { get; set; } = string.Empty;
    public bool IsUnlocked { get; set; }
    public float RelationshipDelta { get; set; } = 2f;
    public bool DeliveriesEnabled { get; set; }
    public float Debt { get; set; }
    public bool DeadDropPreparing { get; set; }
    public int MinsUntilDeadDropReady { get; set; } = -1;
}

[Serializable]
internal sealed class CustomerScopeSyncDto
{
    public string OwnerKey { get; set; } = string.Empty;
    public List<CustomerScopeEntryDto> Customers { get; set; } = new List<CustomerScopeEntryDto>();
    public List<DealerScopeEntryDto> Dealers { get; set; } = new List<DealerScopeEntryDto>();
    public List<SupplierScopeEntryDto> Suppliers { get; set; } = new List<SupplierScopeEntryDto>();
}

[Serializable]
internal sealed class CustomerScopeSyncChunkDto
{
    public string OwnerKey { get; set; } = string.Empty;
    public int Sequence { get; set; }
    public int Total { get; set; }
    public List<CustomerScopeEntryDto> Customers { get; set; } = new List<CustomerScopeEntryDto>();
    public List<DealerScopeEntryDto> Dealers { get; set; } = new List<DealerScopeEntryDto>();
    public List<SupplierScopeEntryDto> Suppliers { get; set; } = new List<SupplierScopeEntryDto>();
}
