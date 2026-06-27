using System;
using System.Collections.Generic;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class CartelActivityScopeRecord
{
    public int GlobalActivityIndex { get; set; } = -1;
    public string GlobalActivityRegion { get; set; } = string.Empty;
    public int GlobalHoursUntilNextActivity { get; set; } = -1;
    public Dictionary<string, RegionalCartelActivityScopeRecord> RegionalActivitiesByRegion { get; set; } = new Dictionary<string, RegionalCartelActivityScopeRecord>(StringComparer.OrdinalIgnoreCase);

    public CartelActivityScopeRecord Clone()
    {
        Dictionary<string, RegionalCartelActivityScopeRecord> regionalActivities = new Dictionary<string, RegionalCartelActivityScopeRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, RegionalCartelActivityScopeRecord> pair in RegionalActivitiesByRegion)
        {
            regionalActivities[pair.Key] = pair.Value.Clone();
        }

        return new CartelActivityScopeRecord
        {
            GlobalActivityIndex = GlobalActivityIndex,
            GlobalActivityRegion = GlobalActivityRegion,
            GlobalHoursUntilNextActivity = GlobalHoursUntilNextActivity,
            RegionalActivitiesByRegion = regionalActivities,
        };
    }
}

[Serializable]
internal sealed class RegionalCartelActivityScopeRecord
{
    public int ActivityIndex { get; set; } = -1;
    public int HoursUntilNextActivity { get; set; } = -1;

    public RegionalCartelActivityScopeRecord Clone()
    {
        return new RegionalCartelActivityScopeRecord
        {
            ActivityIndex = ActivityIndex,
            HoursUntilNextActivity = HoursUntilNextActivity,
        };
    }
}
