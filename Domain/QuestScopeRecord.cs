using System;
using System.Collections.Generic;

namespace DedicatedServerMod.Organisations.Domain;

[Serializable]
internal sealed class QuestScopeRecord
{
    public string OwnerKey { get; set; } = string.Empty;
    public string QuestManagerDataJson { get; set; } = string.Empty;
    public string DeaddropQuestDataJson { get; set; } = string.Empty;
    public string CartelStatus { get; set; } = string.Empty;
    public int CartelHoursSinceStatusChange { get; set; } = 9999;
    public bool SewerUnlocked { get; set; }
    public string CartelDealDataJson { get; set; } = string.Empty;
    public string CartelDealStorageDataJson { get; set; } = string.Empty;
    public int CartelDealHoursUntilNextRequest { get; set; } = -1;
    public Dictionary<string, float> CartelInfluenceByRegion { get; set; } = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
    public CartelActivityScopeRecord CartelActivityState { get; set; } = new CartelActivityScopeRecord();
    public Dictionary<string, string> CartelGraffitiDataBySurfaceGuid { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> MapRegionUnlockedByRegion { get; set; } = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
    public ProductMarketScopeRecord ProductMarketState { get; set; } = new ProductMarketScopeRecord();
    public Dictionary<string, string> VariableValuesByName { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> DeaddropStorageDataByDropGuid { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> CustomerStatesByNpcGuid { get; set; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public QuestScopeRecord Clone()
    {
        return new QuestScopeRecord
        {
            OwnerKey = OwnerKey,
            QuestManagerDataJson = QuestManagerDataJson,
            DeaddropQuestDataJson = DeaddropQuestDataJson,
            CartelStatus = CartelStatus,
            CartelHoursSinceStatusChange = CartelHoursSinceStatusChange,
            SewerUnlocked = SewerUnlocked,
            CartelDealDataJson = CartelDealDataJson,
            CartelDealStorageDataJson = CartelDealStorageDataJson,
            CartelDealHoursUntilNextRequest = CartelDealHoursUntilNextRequest,
            CartelInfluenceByRegion = new Dictionary<string, float>(CartelInfluenceByRegion ?? new Dictionary<string, float>(), StringComparer.OrdinalIgnoreCase),
            CartelActivityState = CartelActivityState?.Clone() ?? new CartelActivityScopeRecord(),
            CartelGraffitiDataBySurfaceGuid = new Dictionary<string, string>(CartelGraffitiDataBySurfaceGuid ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            MapRegionUnlockedByRegion = new Dictionary<string, bool>(MapRegionUnlockedByRegion ?? new Dictionary<string, bool>(), StringComparer.OrdinalIgnoreCase),
            ProductMarketState = ProductMarketState?.Clone() ?? new ProductMarketScopeRecord(),
            VariableValuesByName = new Dictionary<string, string>(VariableValuesByName ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            DeaddropStorageDataByDropGuid = new Dictionary<string, string>(DeaddropStorageDataByDropGuid ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
            UpdatedAtUtc = UpdatedAtUtc,
            CustomerStatesByNpcGuid = new Dictionary<string, string>(CustomerStatesByNpcGuid ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
        };
    }
}
