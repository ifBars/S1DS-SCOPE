#if CLIENT
using System;
using System.Linq;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Utils;

namespace DedicatedServerMod.Organisations.Client.Testing;

internal sealed class OrganisationWorkflowSmokeRunner
{
    private const int MaxInviteAttempts = 6;
    private static readonly TimeSpan InviteRetryInterval = TimeSpan.FromSeconds(15);

    private readonly OrganisationWorkflowSmokeOptions _options;
    private readonly OrganisationLogger _logger;
    private readonly Action<string> _createOrganisation;
    private readonly Action<string> _invitePlayer;
    private readonly Action<string> _acceptInvite;
    private bool _createSubmitted;
    private bool _acceptSubmitted;
    private bool _completionLogged;
    private bool _inviteDelayLogged;
    private bool _inviteRetryExhaustedLogged;
    private int _inviteAttemptCount;
    private DateTime? _organisationReadyAtUtc;
    private DateTime? _lastInviteAttemptAtUtc;

    public OrganisationWorkflowSmokeRunner(
        OrganisationWorkflowSmokeOptions options,
        OrganisationLogger logger,
        Action<string> createOrganisation,
        Action<string> invitePlayer,
        Action<string> acceptInvite)
    {
        _options = options ?? new OrganisationWorkflowSmokeOptions();
        _logger = logger;
        _createOrganisation = createOrganisation;
        _invitePlayer = invitePlayer;
        _acceptInvite = acceptInvite;

        if (_options.Enabled)
        {
            _logger.Info($"[OrgSmoke] Workflow automation enabled. Create={_options.ShouldCreateOrganisation}, Invite={_options.ShouldInvitePlayer}, InviteDelaySeconds={_options.InviteDelaySeconds}, AutoAccept={_options.AutoAcceptInvites}.");
        }
    }

    public void Tick(bool hasSnapshot, OrganisationSnapshotDto snapshot)
    {
        if (!_options.Enabled || !hasSnapshot || snapshot == null)
        {
            return;
        }

        if (_options.AutoAcceptInvites && !_acceptSubmitted && !snapshot.HasOrganisation && snapshot.PendingInvites.Count > 0)
        {
            string inviteId = snapshot.PendingInvites[0].InviteId;
            _acceptSubmitted = true;
            _logger.Info($"[OrgSmoke] Accepting invite {inviteId} from organisation '{snapshot.PendingInvites[0].OrganisationName}'.");
            _acceptInvite(inviteId);
            return;
        }

        if (_options.ShouldCreateOrganisation && !_createSubmitted && !snapshot.HasOrganisation)
        {
            _createSubmitted = true;
            _logger.Info($"[OrgSmoke] Creating organisation '{_options.OrganisationName}'.");
            _createOrganisation(_options.OrganisationName);
            return;
        }

        if (snapshot.HasOrganisation && !_organisationReadyAtUtc.HasValue)
        {
            _organisationReadyAtUtc = DateTime.UtcNow;
        }

        bool isComplete = IsComplete(snapshot);
        if (_options.ShouldInvitePlayer && !isComplete && snapshot.HasOrganisation)
        {
            if (!HasInviteDelayElapsed())
            {
                return;
            }

            if (ShouldAttemptInvite())
            {
                _inviteAttemptCount++;
                _lastInviteAttemptAtUtc = DateTime.UtcNow;
                _logger.Info($"[OrgSmoke] Inviting player '{_options.InviteTarget}' to organisation '{snapshot.OrganisationName}'. Attempt={_inviteAttemptCount}/{MaxInviteAttempts}.");
                _invitePlayer(_options.InviteTarget);
            }

            return;
        }

        if (!_completionLogged && isComplete)
        {
            _completionLogged = true;
            _logger.Info($"[OrgSmoke] Workflow complete. HasOrganisation={snapshot.HasOrganisation}, ScopeOwnerKey={snapshot.ScopeOwnerKey}, Members={string.Join(",", snapshot.MemberSteamIds)}.");
        }
    }

    public void ResetRuntimeState()
    {
        _createSubmitted = false;
        _acceptSubmitted = false;
        _completionLogged = false;
        _inviteDelayLogged = false;
        _inviteRetryExhaustedLogged = false;
        _inviteAttemptCount = 0;
        _organisationReadyAtUtc = null;
        _lastInviteAttemptAtUtc = null;
    }

    private bool HasInviteDelayElapsed()
    {
        if (_options.InviteDelaySeconds <= 0 || !_organisationReadyAtUtc.HasValue)
        {
            return true;
        }

        TimeSpan elapsed = DateTime.UtcNow - _organisationReadyAtUtc.Value;
        if (elapsed.TotalSeconds >= _options.InviteDelaySeconds)
        {
            return true;
        }

        if (!_inviteDelayLogged)
        {
            _inviteDelayLogged = true;
            _logger.Info($"[OrgSmoke] Waiting {_options.InviteDelaySeconds} seconds before inviting player '{_options.InviteTarget}'.");
        }

        return false;
    }

    private bool ShouldAttemptInvite()
    {
        if (_inviteAttemptCount >= MaxInviteAttempts)
        {
            if (!_inviteRetryExhaustedLogged)
            {
                _inviteRetryExhaustedLogged = true;
                _logger.Warning($"[OrgSmoke] Invite retry limit reached for player '{_options.InviteTarget}'.");
            }

            return false;
        }

        if (!_lastInviteAttemptAtUtc.HasValue)
        {
            return true;
        }

        return DateTime.UtcNow - _lastInviteAttemptAtUtc.Value >= InviteRetryInterval;
    }

    private bool IsComplete(OrganisationSnapshotDto snapshot)
    {
        if (!snapshot.HasOrganisation)
        {
            return false;
        }

        if (_options.ShouldInvitePlayer)
        {
            return snapshot.MemberSteamIds.Any(member => string.Equals(member, _options.InviteTarget, StringComparison.OrdinalIgnoreCase));
        }

        return _options.ShouldCreateOrganisation || _options.AutoAcceptInvites;
    }
}
#endif
