#if SERVER
using System;
using System.Collections.Generic;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Utils;

namespace DedicatedServerMod.Organisations.Persistence;

internal sealed class OrganisationSaveDataMigrator
{
    private readonly OrganisationLogger _logger;

    public OrganisationSaveDataMigrator(OrganisationLogger logger)
    {
        _logger = logger;
    }

    public OrganisationSaveData Migrate(OrganisationSaveData? data, out bool changed)
    {
        changed = false;
        data ??= new OrganisationSaveData();

        if (data.Version != OrganisationSaveData.CurrentVersion)
        {
            _logger.Info($"Migrating organisation save data from version {data.Version} to {OrganisationSaveData.CurrentVersion}.");
            data.Version = OrganisationSaveData.CurrentVersion;
            changed = true;
        }

        NormalizeCollections(data, ref changed);
        changed |= NormalizeOrganisationRecords(data);
        changed |= NormalizeScopedRecords(data);
        changed |= ReconcileMembershipIndex(data);
        changed |= ReconcileLegacyPropertyMirrors(data);

        return data;
    }

    private static void NormalizeCollections(OrganisationSaveData data, ref bool changed)
    {
        data.Organisations = NormalizeDictionary(data.Organisations, record => record.OrgId, ref changed);
        data.PlayerToOrganisation = NormalizeStringDictionary(data.PlayerToOrganisation, ref changed);
        data.PendingInvites = NormalizeDictionary(data.PendingInvites, record => record.InviteId, ref changed);
        data.ScopedWallets = NormalizeDictionary(data.ScopedWallets, record => record.OwnerKey, ref changed);
        data.PropertyReservations = NormalizeDictionary(data.PropertyReservations, record => record.PropertyCode, ref changed);
        data.ScopedCustomers = NormalizeDictionary(data.ScopedCustomers, record => BuildScopedKey(record.OwnerKey, record.NpcGuid), ref changed);
        data.ScopedDealers = NormalizeDictionary(data.ScopedDealers, record => BuildScopedKey(record.OwnerKey, record.NpcId), ref changed);
        data.ScopedSuppliers = NormalizeDictionary(data.ScopedSuppliers, record => BuildScopedKey(record.OwnerKey, record.NpcId), ref changed);
        data.ActiveCustomerContracts = NormalizeDictionary(data.ActiveCustomerContracts, record => record.CustomerNpcGuid, ref changed);
        data.ScopedContracts = NormalizeDictionary(data.ScopedContracts, record => record.ContractGuid, ref changed);
        data.QuestScopes = NormalizeDictionary(data.QuestScopes, record => record.OwnerKey, ref changed);
        data.PropertyScopes = NormalizeDictionary(data.PropertyScopes, record => record.OwnerKey, ref changed);
        data.VehicleOwnerships = NormalizeDictionary(data.VehicleOwnerships, record => record.VehicleGuid, ref changed);
        data.PhysicalWorldReservations = NormalizeDictionary(data.PhysicalWorldReservations, record => record.ReservationId, ref changed);
        data.PlayersShownOnboardingPrompt = NormalizeHashSet(data.PlayersShownOnboardingPrompt, ref changed);
    }

    private static bool NormalizeOrganisationRecords(OrganisationSaveData data)
    {
        bool changed = false;
        foreach (KeyValuePair<string, OrganisationRecord> pair in data.Organisations)
        {
            OrganisationRecord record = pair.Value;
            if (string.IsNullOrWhiteSpace(record.OrgId))
            {
                record.OrgId = pair.Key;
                changed = true;
            }

            if (record.TeamColorHex == null)
            {
                record.TeamColorHex = string.Empty;
                changed = true;
            }

            if (record.MemberRoles == null)
            {
                record.MemberRoles = new Dictionary<string, OrganisationRole>(StringComparer.OrdinalIgnoreCase);
                changed = true;
            }
            else
            {
                Dictionary<string, OrganisationRole> normalizedRoles = new Dictionary<string, OrganisationRole>(StringComparer.OrdinalIgnoreCase);
                foreach (KeyValuePair<string, OrganisationRole> role in record.MemberRoles)
                {
                    if (!string.IsNullOrWhiteSpace(role.Key))
                    {
                        normalizedRoles[role.Key] = role.Value;
                    }
                }

                if (normalizedRoles.Count != record.MemberRoles.Count || !ReferenceEquals(normalizedRoles.Comparer, record.MemberRoles.Comparer))
                {
                    record.MemberRoles = normalizedRoles;
                    changed = true;
                }
            }

            if (!string.IsNullOrWhiteSpace(record.OwnerSteamId)
                && (!record.MemberRoles.TryGetValue(record.OwnerSteamId, out OrganisationRole ownerRole) || ownerRole != OrganisationRole.Owner))
            {
                record.MemberRoles[record.OwnerSteamId] = OrganisationRole.Owner;
                changed = true;
            }

            if (record.OwnedPropertyCodes == null)
            {
                record.OwnedPropertyCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                changed = true;
            }
            else
            {
                HashSet<string> normalizedProperties = NormalizeHashSet(record.OwnedPropertyCodes, ref changed);
                if (!ReferenceEquals(normalizedProperties, record.OwnedPropertyCodes))
                {
                    record.OwnedPropertyCodes = normalizedProperties;
                }
            }

            if (record.CreatedAtUtc == default)
            {
                record.CreatedAtUtc = DateTime.UtcNow;
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeScopedRecords(OrganisationSaveData data)
    {
        bool changed = false;

        foreach (KeyValuePair<string, ScopedWalletRecord> pair in data.ScopedWallets)
        {
            changed |= EnsureOwnerKey(pair.Value, pair.Key);
        }

        foreach (KeyValuePair<string, QuestScopeRecord> pair in data.QuestScopes)
        {
            QuestScopeRecord record = pair.Value;
            changed |= EnsureOwnerKey(record, pair.Key);
            record.VariableValuesByName = NormalizeStringDictionary(record.VariableValuesByName, ref changed);
            record.DeaddropStorageDataByDropGuid = NormalizeStringDictionary(record.DeaddropStorageDataByDropGuid, ref changed);
            record.CustomerStatesByNpcGuid = NormalizeStringDictionary(record.CustomerStatesByNpcGuid, ref changed);
            record.CartelInfluenceByRegion = NormalizeFloatDictionary(record.CartelInfluenceByRegion, ref changed);
            record.MapRegionUnlockedByRegion = NormalizeBoolDictionary(record.MapRegionUnlockedByRegion, ref changed);
            record.CartelGraffitiDataBySurfaceGuid = NormalizeStringDictionary(record.CartelGraffitiDataBySurfaceGuid, ref changed);
            record.CartelActivityState = NormalizeCartelActivityState(record.CartelActivityState, ref changed);
            record.ProductMarketState = NormalizeProductMarketState(record.ProductMarketState, ref changed);
            record.QuestManagerDataJson = NormalizeStringValue(record.QuestManagerDataJson, ref changed);
            record.DeaddropQuestDataJson = NormalizeStringValue(record.DeaddropQuestDataJson, ref changed);
            record.CartelStatus = NormalizeStringValue(record.CartelStatus, ref changed);
            if (record.CartelHoursSinceStatusChange < 0)
            {
                record.CartelHoursSinceStatusChange = 9999;
                changed = true;
            }

            record.CartelDealDataJson = NormalizeStringValue(record.CartelDealDataJson, ref changed);
            record.CartelDealStorageDataJson = NormalizeStringValue(record.CartelDealStorageDataJson, ref changed);
            if (record.UpdatedAtUtc == default)
            {
                record.UpdatedAtUtc = DateTime.UtcNow;
                changed = true;
            }
        }

        foreach (KeyValuePair<string, PropertyScopeRecord> pair in data.PropertyScopes)
        {
            PropertyScopeRecord record = pair.Value;
            changed |= EnsureOwnerKey(record, pair.Key);
            record.PropertyStatesByCode = NormalizeStringDictionary(record.PropertyStatesByCode, ref changed);
        }

        foreach (KeyValuePair<string, ScopedDealerRecord> pair in data.ScopedDealers)
        {
            ScopedDealerRecord record = pair.Value;
            if (record.AssignedCustomerNpcIds == null)
            {
                record.AssignedCustomerNpcIds = new List<string>();
                changed = true;
            }
            else
            {
                List<string> normalizedAssignments = new List<string>();
                HashSet<string> seenAssignments = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (string assignedCustomer in record.AssignedCustomerNpcIds)
                {
                    if (!string.IsNullOrWhiteSpace(assignedCustomer) && seenAssignments.Add(assignedCustomer))
                    {
                        normalizedAssignments.Add(assignedCustomer);
                    }
                }

                if (normalizedAssignments.Count != record.AssignedCustomerNpcIds.Count)
                {
                    record.AssignedCustomerNpcIds = normalizedAssignments;
                    changed = true;
                }
            }
        }

        foreach (KeyValuePair<string, PropertyReservationRecord> pair in data.PropertyReservations)
        {
            PropertyReservationRecord record = pair.Value;
            if (string.IsNullOrWhiteSpace(record.PropertyCode))
            {
                record.PropertyCode = pair.Key;
                changed = true;
            }

            changed |= NormalizeReservationOwnerFields(data, record);
        }

        foreach (KeyValuePair<string, VehicleOwnershipRecord> pair in data.VehicleOwnerships)
        {
            VehicleOwnershipRecord record = pair.Value;
            if (string.IsNullOrWhiteSpace(record.VehicleGuid))
            {
                record.VehicleGuid = pair.Key;
                changed = true;
            }
        }

        foreach (KeyValuePair<string, PhysicalWorldReservationRecord> pair in data.PhysicalWorldReservations)
        {
            PhysicalWorldReservationRecord record = pair.Value;
            if (string.IsNullOrWhiteSpace(record.ReservationId))
            {
                record.ReservationId = pair.Key;
                changed = true;
            }

            if (record.ReservedAtUtc == default)
            {
                record.ReservedAtUtc = DateTime.UtcNow;
                changed = true;
            }
        }

        return changed;
    }

    private static bool ReconcileMembershipIndex(OrganisationSaveData data)
    {
        bool changed = false;
        Dictionary<string, string> rebuilt = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (OrganisationRecord organisation in data.Organisations.Values)
        {
            foreach (string memberSteamId in organisation.MemberRoles.Keys)
            {
                if (!string.IsNullOrWhiteSpace(memberSteamId) && !string.IsNullOrWhiteSpace(organisation.OrgId))
                {
                    rebuilt[memberSteamId] = organisation.OrgId;
                }
            }
        }

        foreach (KeyValuePair<string, string> existing in data.PlayerToOrganisation)
        {
            if (!rebuilt.TryGetValue(existing.Key, out string? organisationId)
                || !string.Equals(organisationId, existing.Value, StringComparison.OrdinalIgnoreCase))
            {
                changed = true;
                break;
            }
        }

        if (!changed && rebuilt.Count != data.PlayerToOrganisation.Count)
        {
            changed = true;
        }

        if (changed)
        {
            data.PlayerToOrganisation = rebuilt;
        }

        return changed;
    }

    private static bool ReconcileLegacyPropertyMirrors(OrganisationSaveData data)
    {
        bool changed = false;
        foreach (OrganisationRecord organisation in data.Organisations.Values)
        {
            HashSet<string> rebuiltProperties = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string ownerKey = BuildOrganisationOwnerKey(organisation.OrgId);
            foreach (PropertyReservationRecord reservation in data.PropertyReservations.Values)
            {
                if (string.Equals(reservation.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)
                    && !string.IsNullOrWhiteSpace(reservation.PropertyCode))
                {
                    rebuiltProperties.Add(reservation.PropertyCode);
                }
            }

            if (!organisation.OwnedPropertyCodes.SetEquals(rebuiltProperties))
            {
                organisation.OwnedPropertyCodes = rebuiltProperties;
                changed = true;
            }
        }

        return changed;
    }

    private static bool NormalizeReservationOwnerFields(OrganisationSaveData data, PropertyReservationRecord record)
    {
        bool changed = false;
        if (!string.IsNullOrWhiteSpace(record.OwnerOrganisationId))
        {
            string ownerKey = BuildOrganisationOwnerKey(record.OwnerOrganisationId);
            if (!string.Equals(record.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
            {
                record.OwnerKey = ownerKey;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(record.OwnerSteamId)
                && data.Organisations.TryGetValue(record.OwnerOrganisationId, out OrganisationRecord? organisation))
            {
                record.OwnerSteamId = organisation.OwnerSteamId;
                changed = true;
            }
        }
        else if (string.IsNullOrWhiteSpace(record.OwnerKey) && !string.IsNullOrWhiteSpace(record.OwnerSteamId))
        {
            record.OwnerKey = BuildPersonalOwnerKey(record.OwnerSteamId);
            changed = true;
        }

        if (record.ReservedAtUtc == default)
        {
            record.ReservedAtUtc = DateTime.UtcNow;
            changed = true;
        }

        if (record.UpdatedAtUtc == default)
        {
            record.UpdatedAtUtc = record.ReservedAtUtc;
            changed = true;
        }

        return changed;
    }

    private static Dictionary<string, T> NormalizeDictionary<T>(Dictionary<string, T>? source, Func<T, string> fallbackKeySelector, ref bool changed)
        where T : class
    {
        if (source == null)
        {
            changed = true;
            return new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, T> normalized = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, T> pair in source)
        {
            if (pair.Value == null)
            {
                changed = true;
                continue;
            }

            string key = !string.IsNullOrWhiteSpace(pair.Key) ? pair.Key : fallbackKeySelector(pair.Value);
            if (string.IsNullOrWhiteSpace(key))
            {
                changed = true;
                continue;
            }

            normalized[key] = pair.Value;
        }

        if (normalized.Count != source.Count)
        {
            changed = true;
        }

        return normalized;
    }

    private static Dictionary<string, string> NormalizeStringDictionary(Dictionary<string, string>? source, ref bool changed)
    {
        if (source == null)
        {
            changed = true;
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, string> normalized = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                changed = true;
                continue;
            }

            normalized[pair.Key] = pair.Value ?? string.Empty;
        }

        if (normalized.Count != source.Count)
        {
            changed = true;
        }

        return normalized;
    }

    private static Dictionary<string, float> NormalizeFloatDictionary(Dictionary<string, float>? source, ref bool changed)
    {
        if (source == null)
        {
            changed = true;
            return new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, float> normalized = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, float> pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || float.IsNaN(pair.Value) || float.IsInfinity(pair.Value))
            {
                changed = true;
                continue;
            }

            normalized[pair.Key] = pair.Value;
        }

        if (normalized.Count != source.Count)
        {
            changed = true;
        }

        return normalized;
    }

    private static Dictionary<string, bool> NormalizeBoolDictionary(Dictionary<string, bool>? source, ref bool changed)
    {
        if (source == null)
        {
            changed = true;
            return new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        Dictionary<string, bool> normalized = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, bool> pair in source)
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                changed = true;
                continue;
            }

            normalized[pair.Key] = pair.Value;
        }

        if (normalized.Count != source.Count)
        {
            changed = true;
        }

        return normalized;
    }

    private static HashSet<string> NormalizeHashSet(HashSet<string>? source, ref bool changed)
    {
        if (source == null)
        {
            changed = true;
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        HashSet<string> normalized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string value in source)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                normalized.Add(value);
            }
        }

        if (normalized.Count != source.Count)
        {
            changed = true;
        }

        return normalized;
    }

    private static List<string> NormalizeStringList(List<string>? source, ref bool changed)
    {
        if (source == null)
        {
            changed = true;
            return new List<string>();
        }

        List<string> normalized = new List<string>();
        foreach (string value in source)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                changed = true;
                continue;
            }

            normalized.Add(value);
        }

        if (normalized.Count != source.Count)
        {
            changed = true;
        }

        return normalized;
    }

    private static CartelActivityScopeRecord NormalizeCartelActivityState(CartelActivityScopeRecord? state, ref bool changed)
    {
        if (state == null)
        {
            changed = true;
            return new CartelActivityScopeRecord();
        }

        state.GlobalActivityRegion = NormalizeStringValue(state.GlobalActivityRegion, ref changed);
        if (state.RegionalActivitiesByRegion == null)
        {
            state.RegionalActivitiesByRegion = new Dictionary<string, RegionalCartelActivityScopeRecord>(StringComparer.OrdinalIgnoreCase);
            changed = true;
            return state;
        }

        Dictionary<string, RegionalCartelActivityScopeRecord> normalized = new Dictionary<string, RegionalCartelActivityScopeRecord>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, RegionalCartelActivityScopeRecord> pair in state.RegionalActivitiesByRegion)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || pair.Value == null)
            {
                changed = true;
                continue;
            }

            normalized[pair.Key] = pair.Value;
        }

        if (normalized.Count != state.RegionalActivitiesByRegion.Count)
        {
            changed = true;
        }

        state.RegionalActivitiesByRegion = normalized;
        return state;
    }

    private static ProductMarketScopeRecord NormalizeProductMarketState(ProductMarketScopeRecord? state, ref bool changed)
    {
        if (state == null)
        {
            changed = true;
            return new ProductMarketScopeRecord();
        }

        state.DiscoveredProductIds = NormalizeHashSet(state.DiscoveredProductIds, ref changed);
        state.ListedProductIds = NormalizeHashSet(state.ListedProductIds, ref changed);
        state.FavouritedProductIds = NormalizeHashSet(state.FavouritedProductIds, ref changed);
        state.CreatedProductIds = NormalizeHashSet(state.CreatedProductIds, ref changed);
        state.PricesByProductId = NormalizeFloatDictionary(state.PricesByProductId, ref changed);
        state.MixRecipes = NormalizeProductMixRecipes(state.MixRecipes, ref changed);
        state.ContractReceiptJson = NormalizeStringList(state.ContractReceiptJson, ref changed);
        state.CurrentMixOperationJson = NormalizeStringValue(state.CurrentMixOperationJson, ref changed);
        return state;
    }

    private static List<ProductMixRecipeScopeRecord> NormalizeProductMixRecipes(List<ProductMixRecipeScopeRecord>? source, ref bool changed)
    {
        if (source == null)
        {
            changed = true;
            return new List<ProductMixRecipeScopeRecord>();
        }

        List<ProductMixRecipeScopeRecord> normalized = new List<ProductMixRecipeScopeRecord>();
        foreach (ProductMixRecipeScopeRecord recipe in source)
        {
            if (recipe == null
                || string.IsNullOrWhiteSpace(recipe.ProductId)
                || string.IsNullOrWhiteSpace(recipe.MixerId)
                || string.IsNullOrWhiteSpace(recipe.OutputId))
            {
                changed = true;
                continue;
            }

            recipe.ProductId ??= string.Empty;
            recipe.MixerId ??= string.Empty;
            recipe.OutputId ??= string.Empty;
            normalized.Add(recipe);
        }

        if (normalized.Count != source.Count)
        {
            changed = true;
        }

        return normalized;
    }

    private static string NormalizeStringValue(string? value, ref bool changed)
    {
        if (value != null)
        {
            return value;
        }

        changed = true;
        return string.Empty;
    }

    private static bool EnsureOwnerKey(ScopedWalletRecord record, string key)
    {
        if (!string.IsNullOrWhiteSpace(record.OwnerKey))
        {
            return false;
        }

        record.OwnerKey = key;
        return true;
    }

    private static bool EnsureOwnerKey(QuestScopeRecord record, string key)
    {
        if (!string.IsNullOrWhiteSpace(record.OwnerKey))
        {
            return false;
        }

        record.OwnerKey = key;
        return true;
    }

    private static bool EnsureOwnerKey(PropertyScopeRecord record, string key)
    {
        if (!string.IsNullOrWhiteSpace(record.OwnerKey))
        {
            return false;
        }

        record.OwnerKey = key;
        return true;
    }

    private static string BuildScopedKey(string ownerKey, string scopedId)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(scopedId))
        {
            return string.Empty;
        }

        return string.Concat(ownerKey, "|", scopedId);
    }

    private static string BuildOrganisationOwnerKey(string organisationId)
    {
        return string.IsNullOrWhiteSpace(organisationId) ? string.Empty : $"org:{organisationId}";
    }

    private static string BuildPersonalOwnerKey(string steamId)
    {
        return string.IsNullOrWhiteSpace(steamId) ? string.Empty : $"player:{steamId}";
    }
}
#endif
