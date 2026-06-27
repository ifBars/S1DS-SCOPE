#if CLIENT
using System;

namespace DedicatedServerMod.Organisations.Client.Testing;

internal sealed class OrganisationWorkflowSmokeOptions
{
    public bool Enabled => ShouldCreateOrganisation || ShouldInvitePlayer || AutoAcceptInvites;

    public bool ShouldCreateOrganisation => !string.IsNullOrWhiteSpace(OrganisationName);

    public bool ShouldInvitePlayer => !string.IsNullOrWhiteSpace(InviteTarget);

    public string OrganisationName { get; private set; } = string.Empty;

    public string InviteTarget { get; private set; } = string.Empty;

    public int InviteDelaySeconds { get; private set; }

    public bool AutoAcceptInvites { get; private set; }

    public static OrganisationWorkflowSmokeOptions Parse(string[] args)
    {
        string organisationName = string.Empty;
        string inviteTarget = string.Empty;
        int inviteDelaySeconds = 0;
        bool autoAcceptInvites = false;

        args ??= Array.Empty<string>();
        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i] ?? string.Empty;
            if (string.Equals(arg, "--org-smoke-create-name", StringComparison.Ordinal)
                && TryReadNext(args, i, out string value))
            {
                organisationName = value.Trim();
                i++;
                continue;
            }

            if (string.Equals(arg, "--org-smoke-invite-target", StringComparison.Ordinal)
                && TryReadNext(args, i, out value))
            {
                inviteTarget = value.Trim();
                i++;
                continue;
            }

            if (string.Equals(arg, "--org-smoke-invite-delay-seconds", StringComparison.Ordinal)
                && TryReadNext(args, i, out value))
            {
                if (int.TryParse(value.Trim(), out int parsedDelaySeconds) && parsedDelaySeconds > 0)
                {
                    inviteDelaySeconds = parsedDelaySeconds;
                }

                i++;
                continue;
            }

            if (string.Equals(arg, "--org-smoke-auto-accept-invites", StringComparison.Ordinal))
            {
                autoAcceptInvites = true;
            }
        }

        return new OrganisationWorkflowSmokeOptions
        {
            OrganisationName = organisationName,
            InviteTarget = inviteTarget,
            InviteDelaySeconds = inviteDelaySeconds,
            AutoAcceptInvites = autoAcceptInvites,
        };
    }

    private static bool TryReadNext(string[] args, int index, out string value)
    {
        value = string.Empty;
        int nextIndex = index + 1;
        if (nextIndex >= args.Length)
        {
            return false;
        }

        string candidate = args[nextIndex] ?? string.Empty;
        if (candidate.StartsWith("--", StringComparison.Ordinal))
        {
            return false;
        }

        value = candidate;
        return !string.IsNullOrWhiteSpace(value);
    }
}
#endif
