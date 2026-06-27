using System;
using System.Collections.Generic;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class OrganisationRecord
{
    public string OrgId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string TeamColorHex { get; set; } = string.Empty;
    public string OwnerSteamId { get; set; } = string.Empty;
    public Dictionary<string, OrganisationRole> MemberRoles { get; set; } = new Dictionary<string, OrganisationRole>(StringComparer.OrdinalIgnoreCase);
    public float OnlineBalance { get; set; }
    public float WeeklyDepositSum { get; set; }
    public float LastVictoryOnlineBalanceTarget { get; set; }
    public DateTime? VictoryAchievedAtUtc { get; set; }
    public HashSet<string> OwnedPropertyCodes { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public DateTime CreatedAtUtc { get; set; }

    public bool HasMember(string steamId)
    {
        return !string.IsNullOrWhiteSpace(steamId) && MemberRoles.ContainsKey(steamId);
    }
}
