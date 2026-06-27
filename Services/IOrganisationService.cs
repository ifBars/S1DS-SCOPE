#if SERVER
using DedicatedServerMod.Organisations.Contracts;

namespace DedicatedServerMod.Organisations.Services;

internal interface IOrganisationService
{
    bool TryCreateOrganisation(PlayerIdentity creator, string organisationName, out OrganisationSnapshotDto snapshot, out string error);

    bool TryInvitePlayer(PlayerIdentity inviter, string targetIdentifier, out OrganisationInviteDto invite, out string error);

    bool TryAcceptInvite(PlayerIdentity player, string inviteId, out OrganisationSnapshotDto snapshot, out string error);

    bool TryDeclineInvite(PlayerIdentity player, string inviteId, out OrganisationSnapshotDto snapshot, out string error);

    bool TryLeaveOrganisation(PlayerIdentity player, out OrganisationSnapshotDto snapshot, out string error);

    bool TryKickMember(PlayerIdentity player, string memberSteamId, out OrganisationSnapshotDto snapshot, out string error);

    bool TryTransferOwnership(PlayerIdentity player, string newOwnerSteamId, out OrganisationSnapshotDto snapshot, out string error);

    bool TryApplyOnlineTransaction(PlayerIdentity player, string transactionName, float unitAmount, float quantity, string transactionNote, out OrganisationSnapshotDto snapshot, out string error);

    bool TryApplyOnlineTransactionByOwnerKey(string ownerKey, string transactionName, float unitAmount, float quantity, string transactionNote, out OrganisationSnapshotDto snapshot, out string error);

    bool TryProcessAtmTransaction(PlayerIdentity player, float amount, bool isDeposit, out OrganisationSnapshotDto snapshot, out string error);

    bool EnsureConfiguredTeamMembership(PlayerIdentity player);

    bool TryMarkVictoryReached(OrganisationSnapshotDto snapshot, out OrganisationVictoryDto victory);

    void MarkOnboardingPromptShown(string steamId);

    void ResetWeeklyDepositSums();

    float GetWeeklyDepositSumByOwnerKey(string ownerKey);

    string ResolveOwnerKey(string steamId);

    OrganisationSnapshotDto BuildSnapshot(string steamId);

    OrganisationSnapshotDto BuildSnapshotByOwnerKey(string ownerKey);
}
#endif
