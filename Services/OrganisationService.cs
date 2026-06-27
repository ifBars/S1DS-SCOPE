#if SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using DedicatedServerMod.API;
using DedicatedServerMod.Organisations.Configuration;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Persistence;
using DedicatedServerMod.Organisations.Utils;
using DedicatedServerMod.Server.Player;

namespace DedicatedServerMod.Organisations.Services;

internal sealed class OrganisationService : IOrganisationService
{
    private static readonly TimeSpan InviteLifetime = TimeSpan.FromMinutes(15);

    private readonly IOrganisationRepository _repository;
    private readonly OrganisationLogger _logger;
    private readonly OrganisationServerConfig _config;

    public OrganisationService(IOrganisationRepository repository, OrganisationLogger logger, OrganisationServerConfig config)
    {
        _repository = repository;
        _logger = logger;
        _config = config;
    }

    public bool TryCreateOrganisation(PlayerIdentity creator, string organisationName, out OrganisationSnapshotDto snapshot, out string error)
    {
        snapshot = BuildSnapshot(creator.SteamId);
        error = string.Empty;
        string normalizedName = NormalizeOrganisationName(organisationName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            error = "Organisation name is required.";
            return false;
        }

        if (normalizedName.Length > Constants.MaxOrganisationNameLength)
        {
            error = $"Organisation name must be {Constants.MaxOrganisationNameLength} characters or fewer.";
            return false;
        }

        if (_repository.Current.PlayerToOrganisation.ContainsKey(creator.SteamId))
        {
            error = "You are already in an organisation.";
            return false;
        }

        if (_config.RestrictOrganisationCreationToConfiguredTeams)
        {
            error = "Organisation creation is restricted to configured teams on this server. Configured teams are assigned automatically.";
            return false;
        }

        bool nameExists = _repository.Current.Organisations.Values.Any(o => string.Equals(o.Name, normalizedName, StringComparison.OrdinalIgnoreCase));
        if (nameExists)
        {
            error = "An organisation with that name already exists.";
            return false;
        }

        string organisationId = Guid.NewGuid().ToString("N");
        OrganisationRecord record = new OrganisationRecord
        {
            OrgId = organisationId,
            Name = normalizedName,
            OwnerSteamId = creator.SteamId,
            OnlineBalance = _config.StartingOnlineBalance,
            CreatedAtUtc = DateTime.UtcNow,
        };
        record.MemberRoles[creator.SteamId] = OrganisationRole.Owner;
        _repository.Current.Organisations[organisationId] = record;
        _repository.Current.PlayerToOrganisation[creator.SteamId] = organisationId;
        RemoveInvitesForPlayer(creator.SteamId);
        MarkOnboardingPromptShown(creator.SteamId);
        _repository.MarkDirty();
        _logger.Info($"Created organisation '{normalizedName}' for {creator.PlayerName} ({creator.SteamId}).");
        snapshot = BuildSnapshot(creator.SteamId);
        return true;
    }

    public bool TryInvitePlayer(PlayerIdentity inviter, string targetIdentifier, out OrganisationInviteDto invite, out string error)
    {
        invite = new OrganisationInviteDto();
        error = string.Empty;
        if (!TryGetPlayersOrganisation(inviter.SteamId, out OrganisationRecord organisation))
        {
            error = "You are not in an organisation.";
            return false;
        }

        if (!HasInvitePermission(organisation, inviter.SteamId))
        {
            error = "You do not have permission to invite players.";
            return false;
        }

        ConnectedPlayerInfo? target = FindConnectedPlayer(targetIdentifier);
        if (target == null)
        {
            error = "Player not found online.";
            return false;
        }

        string targetSteamId = !string.IsNullOrWhiteSpace(target.AuthenticatedSteamId) ? target.AuthenticatedSteamId : target.SteamId;
        if (string.IsNullOrWhiteSpace(targetSteamId))
        {
            error = "Target player does not have a valid identity yet.";
            return false;
        }

        if (organisation.HasMember(targetSteamId))
        {
            error = "That player is already in your organisation.";
            return false;
        }

        if (IsConfiguredTeamRosterLocked(organisation) && !IsConfiguredTeamRosterMember(organisation, targetSteamId))
        {
            error = "This configured team is restricted to the server-defined roster.";
            return false;
        }

        bool rosterChanged = ReconcileLockedConfiguredTeamOrganisationRoster(organisation);
        if (rosterChanged)
        {
            _repository.MarkDirty();
        }

        if (HasReachedMemberLimit(organisation))
        {
            error = $"Organisation member limit reached ({_config.MaxOrganisationMembers}).";
            return false;
        }

        if (_repository.Current.PlayerToOrganisation.ContainsKey(targetSteamId))
        {
            error = "That player is already in another organisation.";
            return false;
        }

        int inviteCount = _repository.Current.PendingInvites.Values.Count(x => string.Equals(x.InviteeSteamId, targetSteamId, StringComparison.OrdinalIgnoreCase) && !x.IsExpired(DateTime.UtcNow));
        if (inviteCount >= Constants.MaxInvitesPerPlayer)
        {
            error = "That player already has too many pending invites.";
            return false;
        }

        OrganisationInvite pendingInvite = new OrganisationInvite
        {
            InviteId = Guid.NewGuid().ToString("N"),
            OrganisationId = organisation.OrgId,
            InviterSteamId = inviter.SteamId,
            InviterName = inviter.PlayerName,
            InviteeSteamId = targetSteamId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.Add(InviteLifetime),
        };

        _repository.Current.PendingInvites[pendingInvite.InviteId] = pendingInvite;
        _repository.MarkDirty();
        invite = ToInviteDto(organisation, pendingInvite);
        return true;
    }

    public bool TryAcceptInvite(PlayerIdentity player, string inviteId, out OrganisationSnapshotDto snapshot, out string error)
    {
        snapshot = BuildSnapshot(player.SteamId);
        error = string.Empty;
        if (_repository.Current.PlayerToOrganisation.ContainsKey(player.SteamId))
        {
            error = "You are already in an organisation.";
            return false;
        }

        if (!TryGetPendingInvite(player.SteamId, inviteId, out OrganisationInvite invite, out error))
        {
            return false;
        }

        if (!_repository.Current.Organisations.TryGetValue(invite.OrganisationId, out OrganisationRecord? organisation))
        {
            _repository.Current.PendingInvites.Remove(invite.InviteId);
            _repository.MarkDirty();
            error = "The organisation for this invite no longer exists.";
            return false;
        }

        if (organisation == null)
        {
            error = "The organisation for this invite no longer exists.";
            return false;
        }

        if (IsConfiguredTeamRosterLocked(organisation) && !IsConfiguredTeamRosterMember(organisation, player.SteamId))
        {
            _repository.Current.PendingInvites.Remove(invite.InviteId);
            _repository.MarkDirty();
            error = "This configured team is restricted to the server-defined roster.";
            return false;
        }

        bool rosterChanged = ReconcileLockedConfiguredTeamOrganisationRoster(organisation);
        if (rosterChanged)
        {
            _repository.MarkDirty();
        }

        if (HasReachedMemberLimit(organisation))
        {
            _repository.Current.PendingInvites.Remove(invite.InviteId);
            _repository.MarkDirty();
            error = $"Organisation member limit reached ({_config.MaxOrganisationMembers}).";
            return false;
        }

        organisation.MemberRoles[player.SteamId] = OrganisationRole.Member;
        _repository.Current.PlayerToOrganisation[player.SteamId] = organisation.OrgId;
        RemoveInvitesForPlayer(player.SteamId);
        MarkOnboardingPromptShown(player.SteamId);
        _repository.MarkDirty();
        snapshot = BuildSnapshot(player.SteamId);
        return true;
    }

    public bool TryDeclineInvite(PlayerIdentity player, string inviteId, out OrganisationSnapshotDto snapshot, out string error)
    {
        snapshot = BuildSnapshot(player.SteamId);
        error = string.Empty;
        if (!TryGetPendingInvite(player.SteamId, inviteId, out OrganisationInvite invite, out error))
        {
            return false;
        }

        _repository.Current.PendingInvites.Remove(invite.InviteId);
        _repository.MarkDirty();
        snapshot = BuildSnapshot(player.SteamId);
        return true;
    }

    public bool TryLeaveOrganisation(PlayerIdentity player, out OrganisationSnapshotDto snapshot, out string error)
    {
        snapshot = BuildSnapshot(player.SteamId);
        error = string.Empty;
        if (!TryGetPlayersOrganisation(player.SteamId, out OrganisationRecord organisation))
        {
            error = "You are not in an organisation.";
            return false;
        }

        if (string.Equals(organisation.OwnerSteamId, player.SteamId, StringComparison.OrdinalIgnoreCase))
        {
            error = "Transfer ownership before leaving your organisation.";
            return false;
        }

        if (IsConfiguredTeamRosterMembershipLocked(organisation) && IsConfiguredTeamRosterMember(organisation, player.SteamId))
        {
            error = "This configured team roster is locked by the server.";
            return false;
        }

        organisation.MemberRoles.Remove(player.SteamId);
        _repository.Current.PlayerToOrganisation.Remove(player.SteamId);
        RemoveInvitesForPlayer(player.SteamId);
        _repository.MarkDirty();
        snapshot = BuildSnapshot(player.SteamId);
        return true;
    }

    public bool TryKickMember(PlayerIdentity player, string memberSteamId, out OrganisationSnapshotDto snapshot, out string error)
    {
        snapshot = BuildSnapshot(player.SteamId);
        error = string.Empty;

        if (!TryGetPlayersOrganisation(player.SteamId, out OrganisationRecord organisation))
        {
            error = "You are not in an organisation.";
            return false;
        }

        if (!string.Equals(organisation.OwnerSteamId, player.SteamId, StringComparison.OrdinalIgnoreCase))
        {
            error = "Only the organisation owner can kick members.";
            return false;
        }

        string targetSteamId = (memberSteamId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetSteamId) || !organisation.MemberRoles.ContainsKey(targetSteamId))
        {
            error = "That member was not found in your organisation.";
            return false;
        }

        if (string.Equals(targetSteamId, player.SteamId, StringComparison.OrdinalIgnoreCase))
        {
            error = "Use leave organisation to remove yourself.";
            return false;
        }

        if (string.Equals(targetSteamId, organisation.OwnerSteamId, StringComparison.OrdinalIgnoreCase))
        {
            error = "The organisation owner cannot be kicked.";
            return false;
        }

        if (IsConfiguredTeamRosterMembershipLocked(organisation) && IsConfiguredTeamRosterMember(organisation, targetSteamId))
        {
            error = "This configured team roster is locked by the server.";
            return false;
        }

        organisation.MemberRoles.Remove(targetSteamId);
        _repository.Current.PlayerToOrganisation.Remove(targetSteamId);
        RemoveInvitesForPlayer(targetSteamId);
        _repository.MarkDirty();
        snapshot = BuildSnapshot(player.SteamId);
        return true;
    }

    public bool TryTransferOwnership(PlayerIdentity player, string newOwnerSteamId, out OrganisationSnapshotDto snapshot, out string error)
    {
        snapshot = BuildSnapshot(player.SteamId);
        error = string.Empty;

        if (!TryGetPlayersOrganisation(player.SteamId, out OrganisationRecord organisation))
        {
            error = "You are not in an organisation.";
            return false;
        }

        if (!string.Equals(organisation.OwnerSteamId, player.SteamId, StringComparison.OrdinalIgnoreCase))
        {
            error = "Only the organisation owner can transfer ownership.";
            return false;
        }

        string targetSteamId = (newOwnerSteamId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(targetSteamId) || !organisation.MemberRoles.ContainsKey(targetSteamId))
        {
            error = "Select a current organisation member to transfer ownership to.";
            return false;
        }

        if (string.Equals(targetSteamId, player.SteamId, StringComparison.OrdinalIgnoreCase))
        {
            error = "You already own this organisation.";
            return false;
        }

        if (IsConfiguredTeamRosterMembershipLocked(organisation))
        {
            error = "Configured team ownership is controlled by the server roster.";
            return false;
        }

        organisation.OwnerSteamId = targetSteamId;
        organisation.MemberRoles[player.SteamId] = OrganisationRole.Officer;
        organisation.MemberRoles[targetSteamId] = OrganisationRole.Owner;
        _repository.MarkDirty();
        snapshot = BuildSnapshot(player.SteamId);
        return true;
    }

    public bool TryApplyOnlineTransaction(PlayerIdentity player, string transactionName, float unitAmount, float quantity, string transactionNote, out OrganisationSnapshotDto snapshot, out string error)
    {
        snapshot = BuildSnapshot(player.SteamId);
        error = string.Empty;

        if (!TryResolveWalletScope(player.SteamId, out string ownerKey, out OrganisationRecord? organisation, out string scopeName))
        {
            error = "Player scope is not ready yet.";
            return false;
        }

        if (float.IsNaN(unitAmount) || float.IsInfinity(unitAmount) || float.IsNaN(quantity) || float.IsInfinity(quantity) || quantity < 0f)
        {
            error = "Invalid transaction amount.";
            return false;
        }

        float totalAmount = unitAmount * quantity;
        if (float.IsNaN(totalAmount) || float.IsInfinity(totalAmount))
        {
            error = "Invalid transaction amount.";
            return false;
        }

        ScopedWalletRecord wallet = GetOrCreateWallet(ownerKey, organisation);
        float nextBalance = wallet.OnlineBalance + totalAmount;
        if (nextBalance < 0f)
        {
            error = $"Insufficient {scopeName.ToLowerInvariant()} balance.";
            return false;
        }

        wallet.OnlineBalance = nextBalance;
        wallet.UpdatedAtUtc = DateTime.UtcNow;
        SyncLegacyOrganisationWallet(organisation, wallet);
        _repository.MarkDirty();
        _logger.Info($"Applied scoped transaction '{transactionName}' for {player.PlayerName} ({player.SteamId}) ownerKey={ownerKey} amount {totalAmount:0.##}. Note='{transactionNote}'.");
        snapshot = BuildSnapshot(player.SteamId);
        return true;
    }

    public bool TryApplyOnlineTransactionByOwnerKey(string ownerKey, string transactionName, float unitAmount, float quantity, string transactionNote, out OrganisationSnapshotDto snapshot, out string error)
    {
        string normalizedOwnerKey = ownerKey ?? string.Empty;
        snapshot = BuildSnapshotByOwnerKey(normalizedOwnerKey);
        error = string.Empty;

        if (!TryResolveWalletScopeByOwnerKey(normalizedOwnerKey, out OrganisationRecord? organisation, out string scopeName))
        {
            error = "Owner scope is not ready yet.";
            return false;
        }

        if (float.IsNaN(unitAmount) || float.IsInfinity(unitAmount) || float.IsNaN(quantity) || float.IsInfinity(quantity) || quantity < 0f)
        {
            error = "Invalid transaction amount.";
            return false;
        }

        float totalAmount = unitAmount * quantity;
        if (float.IsNaN(totalAmount) || float.IsInfinity(totalAmount))
        {
            error = "Invalid transaction amount.";
            return false;
        }

        ScopedWalletRecord wallet = GetOrCreateWallet(normalizedOwnerKey, organisation);
        float nextBalance = wallet.OnlineBalance + totalAmount;
        if (nextBalance < 0f)
        {
            error = $"Insufficient {scopeName.ToLowerInvariant()} balance.";
            return false;
        }

        wallet.OnlineBalance = nextBalance;
        wallet.UpdatedAtUtc = DateTime.UtcNow;
        SyncLegacyOrganisationWallet(organisation, wallet);
        _repository.MarkDirty();
        _logger.Info($"Applied scoped transaction '{transactionName}' for ownerKey={normalizedOwnerKey} amount {totalAmount:0.##}. Note='{transactionNote}'.");
        snapshot = BuildSnapshotByOwnerKey(normalizedOwnerKey);
        return true;
    }

    public bool TryProcessAtmTransaction(PlayerIdentity player, float amount, bool isDeposit, out OrganisationSnapshotDto snapshot, out string error)
    {
        snapshot = BuildSnapshot(player.SteamId);
        error = string.Empty;

        if (!TryResolveWalletScope(player.SteamId, out string ownerKey, out OrganisationRecord? organisation, out string scopeName))
        {
            error = "Player scope is not ready yet.";
            return false;
        }

        if (float.IsNaN(amount) || float.IsInfinity(amount) || amount <= 0f)
        {
            error = "Invalid ATM amount.";
            return false;
        }

        ScopedWalletRecord wallet = GetOrCreateWallet(ownerKey, organisation);

        if (isDeposit)
        {
            if (wallet.WeeklyDepositSum + amount > _config.WeeklyAtmDepositLimit)
            {
                error = $"Weekly {scopeName.ToLowerInvariant()} ATM deposit limit reached.";
                return false;
            }

            wallet.WeeklyDepositSum += amount;
            wallet.OnlineBalance += amount;
        }
        else
        {
            if (wallet.OnlineBalance < amount)
            {
                error = $"Insufficient {scopeName.ToLowerInvariant()} balance.";
                return false;
            }

            wallet.OnlineBalance -= amount;
        }

        wallet.UpdatedAtUtc = DateTime.UtcNow;
        SyncLegacyOrganisationWallet(organisation, wallet);
        _repository.MarkDirty();
        _logger.Info($"Processed scoped ATM {(isDeposit ? "deposit" : "withdrawal")} for {player.PlayerName} ({player.SteamId}) ownerKey={ownerKey} amount {amount:0.##}.");
        snapshot = BuildSnapshot(player.SteamId);
        return true;
    }

    public bool EnsureConfiguredTeamMembership(PlayerIdentity player)
    {
        if (!_config.EnableConfiguredTeams || player == null || string.IsNullOrWhiteSpace(player.SteamId))
        {
            return false;
        }

        bool changed = ReconcileLockedConfiguredTeamMembership(player);
        ConfiguredTeamDefinition? team = _config.ConfiguredTeams.FirstOrDefault(candidate => candidate.ContainsMember(player.SteamId));
        if (team == null)
        {
            return changed;
        }

        OrganisationRecord organisation = GetOrCreateConfiguredTeam(team, player);
        changed |= ApplyConfiguredTeamMetadata(organisation, team);
        changed |= ReconcileLockedConfiguredTeamOrganisationRoster(organisation, team);
        if (_repository.Current.PlayerToOrganisation.TryGetValue(player.SteamId, out string? currentOrganisationId))
        {
            if (string.Equals(currentOrganisationId, organisation.OrgId, StringComparison.OrdinalIgnoreCase))
            {
                changed |= ApplyConfiguredTeamRole(organisation, team, player.SteamId);
                if (changed)
                {
                    _repository.MarkDirty();
                }

                return changed;
            }

            if (!_config.AllowConfiguredTeamReassignment && !_config.LockConfiguredTeamRosterMembership)
            {
                if (changed)
                {
                    _repository.MarkDirty();
                }

                _logger.Warning($"Configured team membership skipped for {player.PlayerName} ({player.SteamId}) because they are already in another organisation.");
                return changed;
            }

            RemovePlayerFromCurrentOrganisation(player.SteamId);
            changed = true;
        }

        if (HasReachedMemberLimit(organisation))
        {
            _logger.Warning($"Configured team membership skipped for {player.PlayerName} ({player.SteamId}); '{organisation.Name}' is at the configured member limit.");
            return false;
        }

        changed |= ApplyConfiguredTeamRole(organisation, team, player.SteamId);

        _repository.Current.PlayerToOrganisation[player.SteamId] = organisation.OrgId;
        RemoveInvitesForPlayer(player.SteamId);
        MarkOnboardingPromptShown(player.SteamId);
        _repository.MarkDirty();
        _logger.Info($"Assigned {player.PlayerName} ({player.SteamId}) to configured organisation '{organisation.Name}'.");
        return true;
    }

    public bool TryMarkVictoryReached(OrganisationSnapshotDto snapshot, out OrganisationVictoryDto victory)
    {
        victory = new OrganisationVictoryDto();
        if (_config.VictoryOnlineBalanceTarget <= 0f
            || snapshot == null
            || !snapshot.HasOrganisation
            || string.IsNullOrWhiteSpace(snapshot.OrganisationId)
            || snapshot.OnlineBalance + 0.009f < _config.VictoryOnlineBalanceTarget
            || !_repository.Current.Organisations.TryGetValue(snapshot.OrganisationId, out OrganisationRecord? organisation))
        {
            return false;
        }

        if (organisation.VictoryAchievedAtUtc.HasValue
            && Math.Abs(organisation.LastVictoryOnlineBalanceTarget - _config.VictoryOnlineBalanceTarget) < 0.01f)
        {
            return false;
        }

        DateTime achievedAtUtc = DateTime.UtcNow;
        organisation.LastVictoryOnlineBalanceTarget = _config.VictoryOnlineBalanceTarget;
        organisation.VictoryAchievedAtUtc = achievedAtUtc;
        _repository.MarkDirty();

        victory = new OrganisationVictoryDto
        {
            OrganisationId = organisation.OrgId,
            OrganisationName = organisation.Name,
            OnlineBalance = snapshot.OnlineBalance,
            TargetOnlineBalance = _config.VictoryOnlineBalanceTarget,
            MemberSteamIds = organisation.MemberRoles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList(),
        };
        _logger.Info($"Organisation '{organisation.Name}' reached the configured victory balance target {_config.VictoryOnlineBalanceTarget:0.##} with balance {snapshot.OnlineBalance:0.##}.");
        return true;
    }

    public void ResetWeeklyDepositSums()
    {
        MigrateLegacyOrganisationWallets();
        bool changed = false;

        foreach (ScopedWalletRecord wallet in _repository.Current.ScopedWallets.Values)
        {
            if (wallet.WeeklyDepositSum <= 0f)
            {
                continue;
            }

            wallet.WeeklyDepositSum = 0f;
            wallet.UpdatedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        foreach (OrganisationRecord organisation in _repository.Current.Organisations.Values)
        {
            if (_repository.Current.ScopedWallets.TryGetValue(BuildOrganisationOwnerKey(organisation.OrgId), out ScopedWalletRecord? wallet))
            {
                SyncLegacyOrganisationWallet(organisation, wallet);
            }
        }

        if (!changed)
        {
            return;
        }

        _repository.MarkDirty();
        _logger.Info("Reset weekly ATM deposit totals for all scope wallets.");
    }

    public float GetWeeklyDepositSumByOwnerKey(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return 0f;
        }

        MigrateLegacyOrganisationWallets();

        OrganisationRecord? organisation = null;
        if (ownerKey.StartsWith("org:", StringComparison.OrdinalIgnoreCase))
        {
            string organisationId = ownerKey.Substring("org:".Length);
            _repository.Current.Organisations.TryGetValue(organisationId, out organisation);
        }

        return ReadWallet(ownerKey, organisation).WeeklyDepositSum;
    }

    public string ResolveOwnerKey(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return string.Empty;
        }

        if (TryGetPlayersOrganisation(steamId, out OrganisationRecord organisation))
        {
            return BuildOrganisationOwnerKey(organisation.OrgId);
        }

        return BuildPlayerOwnerKey(steamId);
    }

    public void MarkOnboardingPromptShown(string steamId)
    {
        string normalizedSteamId = (steamId ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedSteamId))
        {
            return;
        }

        if (_repository.Current.PlayersShownOnboardingPrompt.Add(normalizedSteamId))
        {
            _repository.MarkDirty();
        }
    }

    public OrganisationSnapshotDto BuildSnapshot(string steamId)
    {
        PurgeExpiredInvites();

        OrganisationSnapshotDto snapshot = new OrganisationSnapshotDto
        {
            PlayerSteamId = steamId ?? string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(steamId) && TryGetPlayersOrganisation(steamId, out OrganisationRecord organisation))
        {
            ScopedWalletRecord wallet = ReadWallet(BuildOrganisationOwnerKey(organisation.OrgId), organisation);
            snapshot.HasOrganisation = true;
            snapshot.UsesSharedScope = true;
            snapshot.OrganisationId = organisation.OrgId;
            snapshot.OrganisationName = organisation.Name;
            snapshot.TeamColorHex = organisation.TeamColorHex ?? string.Empty;
            snapshot.ShouldApplyTeamColorToOutfit = _config.ApplyConfiguredTeamColorToOutfit
                && !string.IsNullOrWhiteSpace(snapshot.TeamColorHex);
            snapshot.ScopeKind = OwnerScopeKind.Organisation.ToString();
            snapshot.ScopeName = organisation.Name;
            snapshot.ScopeOwnerKey = BuildOrganisationOwnerKey(organisation.OrgId);
            snapshot.OwnerSteamId = organisation.OwnerSteamId;
            snapshot.PlayerRole = organisation.MemberRoles.TryGetValue(steamId, out OrganisationRole role)
                ? role.ToString()
                : OrganisationRole.Member.ToString();
            snapshot.OnlineBalance = wallet.OnlineBalance;
            snapshot.WeeklyDepositSum = wallet.WeeklyDepositSum;
            ApplyVictoryState(snapshot, organisation);
            snapshot.MemberSteamIds = organisation.MemberRoles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
            snapshot.Members = snapshot.MemberSteamIds
                .Select(memberSteamId => BuildMemberDto(organisation, memberSteamId, steamId))
                .ToList();
        }
        else if (!string.IsNullOrWhiteSpace(steamId))
        {
            ScopedWalletRecord wallet = ReadWallet(BuildPlayerOwnerKey(steamId), null);
            snapshot.ScopeKind = OwnerScopeKind.Player.ToString();
            snapshot.ScopeName = "Personal";
            snapshot.ScopeOwnerKey = BuildPlayerOwnerKey(steamId);
            snapshot.PlayerRole = "Solo";
            snapshot.OnlineBalance = wallet.OnlineBalance;
            snapshot.WeeklyDepositSum = wallet.WeeklyDepositSum;
            snapshot.MemberSteamIds = new List<string> { steamId };
        }

        snapshot.ShouldShowOnboarding = !snapshot.HasOrganisation
            && !_repository.Current.PlayersShownOnboardingPrompt.Contains(steamId ?? string.Empty);

        snapshot.PendingInvites = _repository.Current.PendingInvites.Values
            .Where(x => string.Equals(x.InviteeSteamId, steamId, StringComparison.OrdinalIgnoreCase))
            .Where(x => _repository.Current.Organisations.TryGetValue(x.OrganisationId, out _))
            .Select(x => ToInviteDto(_repository.Current.Organisations[x.OrganisationId], x))
            .OrderBy(x => x.OrganisationName, StringComparer.OrdinalIgnoreCase)
            .ToList();

        return snapshot;
    }

    public OrganisationSnapshotDto BuildSnapshotByOwnerKey(string ownerKey)
    {
        string normalizedOwnerKey = ownerKey ?? string.Empty;
        if (normalizedOwnerKey.StartsWith("player:", StringComparison.OrdinalIgnoreCase))
        {
            return BuildSnapshot(normalizedOwnerKey.Substring("player:".Length));
        }

        if (normalizedOwnerKey.StartsWith("org:", StringComparison.OrdinalIgnoreCase))
        {
            string organisationId = normalizedOwnerKey.Substring("org:".Length);
            if (_repository.Current.Organisations.TryGetValue(organisationId, out OrganisationRecord? organisation))
            {
                string memberSteamId = organisation.MemberRoles.Keys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? organisation.OwnerSteamId;
                return BuildSnapshot(memberSteamId);
            }
        }

        return new OrganisationSnapshotDto
        {
            ScopeOwnerKey = normalizedOwnerKey,
        };
    }

    private void ApplyVictoryState(OrganisationSnapshotDto snapshot, OrganisationRecord organisation)
    {
        if (_config.VictoryOnlineBalanceTarget <= 0f)
        {
            return;
        }

        snapshot.VictoryOnlineBalanceTarget = _config.VictoryOnlineBalanceTarget;
        snapshot.HasReachedVictoryTarget = organisation.VictoryAchievedAtUtc.HasValue
            && Math.Abs(organisation.LastVictoryOnlineBalanceTarget - _config.VictoryOnlineBalanceTarget) < 0.01f;
        snapshot.VictoryAchievedAtUnixTimeSeconds = snapshot.HasReachedVictoryTarget && organisation.VictoryAchievedAtUtc.HasValue
            ? new DateTimeOffset(organisation.VictoryAchievedAtUtc.Value).ToUnixTimeSeconds()
            : 0L;
    }

    private bool TryGetPlayersOrganisation(string steamId, out OrganisationRecord organisation)
    {
        organisation = null!;
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return false;
        }

        if (!_repository.Current.PlayerToOrganisation.TryGetValue(steamId, out string? organisationId))
        {
            return false;
        }

        if (!_repository.Current.Organisations.TryGetValue(organisationId, out OrganisationRecord? resolvedOrganisation)
            || resolvedOrganisation == null)
        {
            return false;
        }

        organisation = resolvedOrganisation;
        return true;
    }

    private bool TryResolveWalletScope(string steamId, out string ownerKey, out OrganisationRecord? organisation, out string scopeName)
    {
        organisation = null;
        scopeName = string.Empty;
        ownerKey = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return false;
        }

        if (TryGetPlayersOrganisation(steamId, out OrganisationRecord resolvedOrganisation))
        {
            organisation = resolvedOrganisation;
            ownerKey = BuildOrganisationOwnerKey(resolvedOrganisation.OrgId);
            scopeName = "organisation";
            return true;
        }

        ownerKey = BuildPlayerOwnerKey(steamId);
        scopeName = "personal";
        return true;
    }

    private bool TryResolveWalletScopeByOwnerKey(string ownerKey, out OrganisationRecord? organisation, out string scopeName)
    {
        organisation = null;
        scopeName = "Personal";

        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        if (ownerKey.StartsWith("org:", StringComparison.OrdinalIgnoreCase))
        {
            string organisationId = ownerKey.Substring("org:".Length);
            if (!_repository.Current.Organisations.TryGetValue(organisationId, out organisation))
            {
                return false;
            }

            scopeName = organisation.Name;
            return true;
        }

        if (ownerKey.StartsWith("player:", StringComparison.OrdinalIgnoreCase))
        {
            return !string.IsNullOrWhiteSpace(ownerKey.Substring("player:".Length));
        }

        return false;
    }

    private ScopedWalletRecord ReadWallet(string ownerKey, OrganisationRecord? organisation)
    {
        if (_repository.Current.ScopedWallets.TryGetValue(ownerKey, out ScopedWalletRecord? wallet))
        {
            return wallet;
        }

        if (organisation != null)
        {
            return new ScopedWalletRecord
            {
                OwnerKey = ownerKey,
                OnlineBalance = organisation.OnlineBalance,
                WeeklyDepositSum = organisation.WeeklyDepositSum,
            };
        }

        return new ScopedWalletRecord
        {
            OwnerKey = ownerKey,
            OnlineBalance = _config.StartingOnlineBalance,
        };
    }

    private ScopedWalletRecord GetOrCreateWallet(string ownerKey, OrganisationRecord? organisation)
    {
        if (_repository.Current.ScopedWallets.TryGetValue(ownerKey, out ScopedWalletRecord? wallet))
        {
            return wallet;
        }

        wallet = new ScopedWalletRecord
        {
            OwnerKey = ownerKey,
            OnlineBalance = organisation?.OnlineBalance ?? _config.StartingOnlineBalance,
            WeeklyDepositSum = organisation?.WeeklyDepositSum ?? 0f,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _repository.Current.ScopedWallets[ownerKey] = wallet;
        SyncLegacyOrganisationWallet(organisation, wallet);
        _repository.MarkDirty();
        return wallet;
    }

    private void MigrateLegacyOrganisationWallets()
    {
        bool changed = false;
        foreach (OrganisationRecord organisation in _repository.Current.Organisations.Values)
        {
            string ownerKey = BuildOrganisationOwnerKey(organisation.OrgId);
            if (_repository.Current.ScopedWallets.ContainsKey(ownerKey))
            {
                continue;
            }

            if (Math.Abs(organisation.OnlineBalance) < 0.0001f && Math.Abs(organisation.WeeklyDepositSum) < 0.0001f)
            {
                continue;
            }

            _repository.Current.ScopedWallets[ownerKey] = new ScopedWalletRecord
            {
                OwnerKey = ownerKey,
                OnlineBalance = organisation.OnlineBalance,
                WeeklyDepositSum = organisation.WeeklyDepositSum,
                UpdatedAtUtc = DateTime.UtcNow,
            };
            changed = true;
        }

        if (changed)
        {
            _repository.MarkDirty();
        }
    }

    private static void SyncLegacyOrganisationWallet(OrganisationRecord? organisation, ScopedWalletRecord wallet)
    {
        if (organisation == null)
        {
            return;
        }

        organisation.OnlineBalance = wallet.OnlineBalance;
        organisation.WeeklyDepositSum = wallet.WeeklyDepositSum;
    }

    private static string BuildPlayerOwnerKey(string steamId)
    {
        return $"player:{steamId}";
    }

    private static string BuildOrganisationOwnerKey(string organisationId)
    {
        return $"org:{organisationId}";
    }

    private static ConnectedPlayerInfo? FindConnectedPlayer(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || S1DS.Server.Players == null)
        {
            return null;
        }

        ConnectedPlayerInfo? bySteamId = S1DS.Server.Players.GetPlayerBySteamId(identifier);
        if (bySteamId != null)
        {
            return bySteamId;
        }

        return S1DS.Server.Players.GetPlayerByName(identifier);
    }

    private bool TryGetPendingInvite(string inviteeSteamId, string inviteId, out OrganisationInvite invite, out string error)
    {
        invite = null!;
        error = string.Empty;
        PurgeExpiredInvites();

        if (string.IsNullOrWhiteSpace(inviteId)
            || !_repository.Current.PendingInvites.TryGetValue(inviteId, out OrganisationInvite? resolvedInvite)
            || resolvedInvite == null)
        {
            error = "Invite not found.";
            return false;
        }

        invite = resolvedInvite;
        if (!string.Equals(invite.InviteeSteamId, inviteeSteamId, StringComparison.OrdinalIgnoreCase))
        {
            error = "Invite does not belong to you.";
            return false;
        }

        return true;
    }

    private bool HasInvitePermission(OrganisationRecord organisation, string steamId)
    {
        if (!organisation.MemberRoles.TryGetValue(steamId, out OrganisationRole role))
        {
            return false;
        }

        return role == OrganisationRole.Owner || role == OrganisationRole.Officer;
    }

    private bool HasReachedMemberLimit(OrganisationRecord organisation)
    {
        return _config.MaxOrganisationMembers > 0 && organisation.MemberRoles.Count >= _config.MaxOrganisationMembers;
    }

    private bool IsConfiguredTeamRosterLocked(OrganisationRecord organisation)
    {
        return _config.RestrictConfiguredTeamInvitesToRoster
            && TryGetConfiguredTeamForOrganisation(organisation, out _);
    }

    private bool IsConfiguredTeamRosterMembershipLocked(OrganisationRecord organisation)
    {
        return _config.LockConfiguredTeamRosterMembership
            && TryGetConfiguredTeamForOrganisation(organisation, out _);
    }

    private bool IsConfiguredTeamRosterMember(OrganisationRecord organisation, string steamId)
    {
        return TryGetConfiguredTeamForOrganisation(organisation, out ConfiguredTeamDefinition? team)
            && team != null
            && team.ContainsMember(steamId);
    }

    private bool TryGetConfiguredTeamForOrganisation(OrganisationRecord organisation, out ConfiguredTeamDefinition? team)
    {
        team = null;
        if (!_config.EnableConfiguredTeams || organisation == null || string.IsNullOrWhiteSpace(organisation.Name))
        {
            return false;
        }

        team = _config.ConfiguredTeams.FirstOrDefault(candidate =>
            string.Equals(candidate.Name, organisation.Name, StringComparison.OrdinalIgnoreCase));
        return team != null;
    }

    private bool ReconcileLockedConfiguredTeamMembership(PlayerIdentity player)
    {
        if (!_config.LockConfiguredTeamRosterMembership
            || !_repository.Current.PlayerToOrganisation.TryGetValue(player.SteamId, out string? currentOrganisationId)
            || !_repository.Current.Organisations.TryGetValue(currentOrganisationId, out OrganisationRecord? currentOrganisation)
            || !TryGetConfiguredTeamForOrganisation(currentOrganisation, out ConfiguredTeamDefinition? currentTeam)
            || currentTeam == null
            || currentTeam.ContainsMember(player.SteamId))
        {
            return false;
        }

        RemovePlayerFromCurrentOrganisation(player.SteamId);
        RemoveInvitesForPlayer(player.SteamId);
        _repository.MarkDirty();
        _logger.Info($"Removed {player.PlayerName} ({player.SteamId}) from configured organisation '{currentOrganisation.Name}' because they are no longer on the locked server roster.");
        return true;
    }

    private bool ReconcileLockedConfiguredTeamOrganisationRoster(OrganisationRecord organisation, ConfiguredTeamDefinition team)
    {
        if (!_config.LockConfiguredTeamRosterMembership || organisation == null || team == null)
        {
            return false;
        }

        bool changed = false;
        foreach (string steamId in organisation.MemberRoles.Keys
            .Where(steamId => !team.ContainsMember(steamId))
            .ToList())
        {
            RemovePlayerFromCurrentOrganisation(steamId);
            RemoveInvitesForPlayer(steamId);
            changed = true;
        }

        if (changed)
        {
            _logger.Info($"Removed stale non-roster members from configured organisation '{organisation.Name}' before assignment.");
        }

        return changed;
    }

    private bool ReconcileLockedConfiguredTeamOrganisationRoster(OrganisationRecord organisation)
    {
        if (!TryGetConfiguredTeamForOrganisation(organisation, out ConfiguredTeamDefinition? team) || team == null)
        {
            return false;
        }

        return ReconcileLockedConfiguredTeamOrganisationRoster(organisation, team);
    }

    private OrganisationRecord GetOrCreateConfiguredTeam(ConfiguredTeamDefinition team, PlayerIdentity player)
    {
        OrganisationRecord? existing = _repository.Current.Organisations.Values.FirstOrDefault(organisation =>
            string.Equals(organisation.Name, team.Name, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            return existing;
        }

        string ownerSteamId = ResolveConfiguredTeamOwnerSteamId(team, player.SteamId);
        OrganisationRecord created = new OrganisationRecord
        {
            OrgId = Guid.NewGuid().ToString("N"),
            Name = team.Name,
            TeamColorHex = team.TeamColorHex,
            OwnerSteamId = ownerSteamId,
            OnlineBalance = _config.StartingOnlineBalance,
            CreatedAtUtc = DateTime.UtcNow,
        };
        _repository.Current.Organisations[created.OrgId] = created;
        GetOrCreateWallet(BuildOrganisationOwnerKey(created.OrgId), created);
        _logger.Info($"Created configured organisation '{created.Name}' for team preset.");
        return created;
    }

    private static bool ApplyConfiguredTeamMetadata(OrganisationRecord organisation, ConfiguredTeamDefinition team)
    {
        if (organisation == null || team == null)
        {
            return false;
        }

        string normalizedColor = team.TeamColorHex ?? string.Empty;
        if (string.Equals(organisation.TeamColorHex ?? string.Empty, normalizedColor, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        organisation.TeamColorHex = normalizedColor;
        return true;
    }

    private static string ResolveConfiguredTeamOwnerSteamId(ConfiguredTeamDefinition team, string fallbackSteamId)
    {
        if (!string.IsNullOrWhiteSpace(team.OwnerSteamId))
        {
            return team.OwnerSteamId;
        }

        return team.MemberSteamIds.FirstOrDefault(member => !string.IsNullOrWhiteSpace(member)) ?? fallbackSteamId;
    }

    private bool ApplyConfiguredTeamRole(OrganisationRecord organisation, ConfiguredTeamDefinition team, string steamId)
    {
        string ownerSteamId = ResolveConfiguredTeamOwnerSteamId(team, steamId);
        OrganisationRole role = string.Equals(ownerSteamId, steamId, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(organisation.OwnerSteamId) && organisation.MemberRoles.Count == 0)
                ? OrganisationRole.Owner
                : OrganisationRole.Member;
        bool changed = !organisation.MemberRoles.TryGetValue(steamId, out OrganisationRole existingRole) || existingRole != role;

        if (!string.IsNullOrWhiteSpace(ownerSteamId))
        {
            changed |= TryApplyConfiguredTeamOwner(organisation, team, ownerSteamId);
        }

        organisation.MemberRoles[steamId] = role;
        return changed;
    }

    private bool TryApplyConfiguredTeamOwner(OrganisationRecord organisation, ConfiguredTeamDefinition team, string ownerSteamId)
    {
        if (!CanAssignConfiguredTeamOwner(organisation, team, ownerSteamId))
        {
            return false;
        }

        bool changed = false;
        if (_repository.Current.PlayerToOrganisation.TryGetValue(ownerSteamId, out string? existingOrganisationId)
            && !string.Equals(existingOrganisationId, organisation.OrgId, StringComparison.OrdinalIgnoreCase))
        {
            RemovePlayerFromCurrentOrganisation(ownerSteamId);
            changed = true;
        }

        foreach (string existingOwnerSteamId in organisation.MemberRoles
            .Where(pair => pair.Value == OrganisationRole.Owner && !string.Equals(pair.Key, ownerSteamId, StringComparison.OrdinalIgnoreCase))
            .Select(pair => pair.Key)
            .ToList())
        {
            organisation.MemberRoles[existingOwnerSteamId] = OrganisationRole.Member;
            changed = true;
        }

        if (!organisation.MemberRoles.TryGetValue(ownerSteamId, out OrganisationRole ownerRole) || ownerRole != OrganisationRole.Owner)
        {
            organisation.MemberRoles[ownerSteamId] = OrganisationRole.Owner;
            changed = true;
        }

        if (!_repository.Current.PlayerToOrganisation.TryGetValue(ownerSteamId, out string? indexedOrganisationId)
            || !string.Equals(indexedOrganisationId, organisation.OrgId, StringComparison.OrdinalIgnoreCase))
        {
            _repository.Current.PlayerToOrganisation[ownerSteamId] = organisation.OrgId;
            changed = true;
        }

        if (!string.Equals(organisation.OwnerSteamId, ownerSteamId, StringComparison.OrdinalIgnoreCase))
        {
            organisation.OwnerSteamId = ownerSteamId;
            changed = true;
        }

        return changed;
    }

    private bool CanAssignConfiguredTeamOwner(OrganisationRecord organisation, ConfiguredTeamDefinition team, string ownerSteamId)
    {
        if (!_repository.Current.PlayerToOrganisation.TryGetValue(ownerSteamId, out string? existingOrganisationId)
            || string.Equals(existingOrganisationId, organisation.OrgId, StringComparison.OrdinalIgnoreCase)
            || _config.AllowConfiguredTeamReassignment
            || (_config.LockConfiguredTeamRosterMembership && team.ContainsMember(ownerSteamId)))
        {
            return true;
        }

        _logger.Warning($"Configured team owner sync skipped for '{team.Name}' because owner {ownerSteamId} already belongs to another organisation and configured team reassignment is disabled.");
        return false;
    }

    private static OrganisationRole ResolveConfiguredTeamRole(ConfiguredTeamDefinition team, string steamId, OrganisationRecord organisation)
    {
        string ownerSteamId = ResolveConfiguredTeamOwnerSteamId(team, steamId);
        if (string.Equals(ownerSteamId, steamId, StringComparison.OrdinalIgnoreCase)
            || (string.IsNullOrWhiteSpace(organisation.OwnerSteamId) && organisation.MemberRoles.Count == 0))
        {
            return OrganisationRole.Owner;
        }

        return OrganisationRole.Member;
    }

    private void RemovePlayerFromCurrentOrganisation(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId)
            || !_repository.Current.PlayerToOrganisation.TryGetValue(steamId, out string? organisationId)
            || !_repository.Current.Organisations.TryGetValue(organisationId, out OrganisationRecord? organisation))
        {
            _repository.Current.PlayerToOrganisation.Remove(steamId);
            return;
        }

        organisation.MemberRoles.Remove(steamId);
        _repository.Current.PlayerToOrganisation.Remove(steamId);
        if (string.Equals(organisation.OwnerSteamId, steamId, StringComparison.OrdinalIgnoreCase))
        {
            string? replacementOwner = organisation.MemberRoles.Keys.OrderBy(member => member, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            organisation.OwnerSteamId = replacementOwner ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(replacementOwner))
            {
                organisation.MemberRoles[replacementOwner] = OrganisationRole.Owner;
            }
        }
    }

    private void RemoveInvitesForPlayer(string steamId)
    {
        List<string> inviteIds = _repository.Current.PendingInvites.Values
            .Where(x => string.Equals(x.InviteeSteamId, steamId, StringComparison.OrdinalIgnoreCase))
            .Select(x => x.InviteId)
            .ToList();

        for (int i = 0; i < inviteIds.Count; i++)
        {
            _repository.Current.PendingInvites.Remove(inviteIds[i]);
        }
    }

    private void PurgeExpiredInvites()
    {
        DateTime utcNow = DateTime.UtcNow;
        List<string> expiredInviteIds = _repository.Current.PendingInvites.Values
            .Where(x => x.IsExpired(utcNow))
            .Select(x => x.InviteId)
            .ToList();

        if (expiredInviteIds.Count == 0)
        {
            return;
        }

        for (int i = 0; i < expiredInviteIds.Count; i++)
        {
            _repository.Current.PendingInvites.Remove(expiredInviteIds[i]);
        }

        _repository.MarkDirty();
    }

    private static string NormalizeOrganisationName(string organisationName)
    {
        return (organisationName ?? string.Empty).Trim();
    }

    private static OrganisationInviteDto ToInviteDto(OrganisationRecord organisation, OrganisationInvite invite)
    {
        return new OrganisationInviteDto
        {
            InviteId = invite.InviteId,
            OrganisationId = invite.OrganisationId,
            OrganisationName = organisation.Name,
            InviterSteamId = invite.InviterSteamId,
            InviterName = invite.InviterName,
            ExpiresAtUnixTimeSeconds = new DateTimeOffset(invite.ExpiresAtUtc).ToUnixTimeSeconds(),
        };
    }

    private OrganisationMemberDto BuildMemberDto(OrganisationRecord organisation, string memberSteamId, string viewerSteamId)
    {
        ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(memberSteamId);
        string displayName;
        if (connectedPlayer != null)
        {
            displayName = !string.IsNullOrWhiteSpace(connectedPlayer.PlayerName)
                ? connectedPlayer.PlayerName
                : connectedPlayer.DisplayName;
        }
        else if (string.Equals(memberSteamId, viewerSteamId, StringComparison.OrdinalIgnoreCase))
        {
            displayName = "You";
        }
        else
        {
            displayName = memberSteamId;
        }

        organisation.MemberRoles.TryGetValue(memberSteamId, out OrganisationRole role);

        return new OrganisationMemberDto
        {
            SteamId = memberSteamId,
            DisplayName = displayName,
            Role = role.ToString(),
            IsOwner = string.Equals(organisation.OwnerSteamId, memberSteamId, StringComparison.OrdinalIgnoreCase),
            IsOnline = connectedPlayer?.Connection != null && connectedPlayer.IsConnected,
        };
    }
}
#endif
