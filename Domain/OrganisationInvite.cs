using System;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class OrganisationInvite
{
    public string InviteId { get; set; } = string.Empty;
    public string OrganisationId { get; set; } = string.Empty;
    public string InviterSteamId { get; set; } = string.Empty;
    public string InviterName { get; set; } = string.Empty;
    public string InviteeSteamId { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }

    public bool IsExpired(DateTime utcNow)
    {
        return ExpiresAtUtc <= utcNow;
    }
}
