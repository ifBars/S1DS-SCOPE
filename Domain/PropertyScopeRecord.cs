using System;
using System.Collections.Generic;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class PropertyScopeRecord
{
    public string OwnerKey { get; set; } = string.Empty;
    public Dictionary<string, string> PropertyStatesByCode { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public PropertyScopeRecord Clone()
    {
        return new PropertyScopeRecord
        {
            OwnerKey = OwnerKey,
            UpdatedAtUtc = UpdatedAtUtc,
            PropertyStatesByCode = new Dictionary<string, string>(PropertyStatesByCode, StringComparer.OrdinalIgnoreCase),
        };
    }
}
