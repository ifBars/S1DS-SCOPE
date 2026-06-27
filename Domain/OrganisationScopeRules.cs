using System;
using System.Collections.Generic;
using DedicatedServerMod.Organisations.Persistence;

namespace DedicatedServerMod.Organisations.Domain;

internal static class OrganisationScopeRules
{
    public static string BuildScopedCustomerKey(string ownerKey, string customerGuid)
    {
        return ownerKey + "|" + customerGuid;
    }

    public static string BuildScopedNpcKey(string ownerKey, string npcId)
    {
        return ownerKey + "|" + npcId;
    }

    public static bool CanReserveCustomerContract(
        OrganisationSaveData saveData,
        string ownerKey,
        string customerGuid,
        string denialMessageForConflictingOwner,
        out string denialMessage)
    {
        denialMessage = string.Empty;
        if (saveData.ActiveCustomerContracts.TryGetValue(customerGuid, out ActiveCustomerContractReservationRecord? activeReservation)
            && !string.Equals(activeReservation.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            denialMessage = denialMessageForConflictingOwner;
            return false;
        }

        return true;
    }

    public static bool CanRecruitDealer(
        OrganisationSaveData saveData,
        string ownerKey,
        string dealerNpcId,
        string dealerFirstName,
        out ScopedDealerRecord? record,
        out string denialMessage)
    {
        denialMessage = string.Empty;
        string key = BuildScopedNpcKey(ownerKey, dealerNpcId);
        saveData.ScopedDealers.TryGetValue(key, out record);
        if (record == null || !record.HasBeenRecommended)
        {
            denialMessage = "Must be recommended by one of " + dealerFirstName + "'s connections.";
            return false;
        }

        foreach (ScopedDealerRecord existing in saveData.ScopedDealers.Values)
        {
            if (existing == null
                || !existing.IsRecruited
                || !string.Equals(existing.NpcId, dealerNpcId, StringComparison.OrdinalIgnoreCase)
                || string.Equals(existing.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            denialMessage = dealerFirstName + " already works for another crew.";
            return false;
        }

        return true;
    }

    public static bool TryFindDealerContractOwner(
        OrganisationSaveData saveData,
        string dealerNpcId,
        string dealerFirstName,
        string customerNpcId,
        string customerGuid,
        string customerFirstName,
        out string ownerKey,
        out string denialMessage)
    {
        ownerKey = string.Empty;
        denialMessage = string.Empty;

        foreach (ScopedDealerRecord dealerRecord in saveData.ScopedDealers.Values)
        {
            if (dealerRecord == null
                || !dealerRecord.IsRecruited
                || !string.Equals(dealerRecord.NpcId, dealerNpcId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!ContainsIgnoreCase(dealerRecord.AssignedCustomerNpcIds, customerNpcId))
            {
                denialMessage = customerFirstName + " is not assigned to this dealer's scope.";
                return false;
            }

            string customerKey = BuildScopedCustomerKey(dealerRecord.OwnerKey, customerGuid);
            if (!saveData.ScopedCustomers.TryGetValue(customerKey, out ScopedCustomerRecord? customerRecord) || !customerRecord.IsUnlocked)
            {
                denialMessage = customerFirstName + " is not unlocked for this dealer's scope.";
                return false;
            }

            if (!CanReserveCustomerContract(
                    saveData,
                    dealerRecord.OwnerKey,
                    customerGuid,
                    customerFirstName + " already found another dealer.",
                    out denialMessage))
            {
                return false;
            }

            ownerKey = dealerRecord.OwnerKey;
            return true;
        }

        denialMessage = dealerFirstName + " does not work for a scoped owner.";
        return false;
    }

    public static DealerRetentionProcessingResult ProcessDealerRetentionFees(
        OrganisationSaveData saveData,
        bool enabled,
        float weeklyFee,
        DateTime utcNow)
    {
        DealerRetentionProcessingResult result = new DealerRetentionProcessingResult();
        if (!enabled || weeklyFee <= 0f)
        {
            return result;
        }

        foreach (KeyValuePair<string, ScopedDealerRecord> pair in saveData.ScopedDealers)
        {
            ScopedDealerRecord record = pair.Value;
            if (record == null || !record.IsRecruited)
            {
                continue;
            }

            result.ProcessedCount++;
            if (record.Cash + 0.009f >= weeklyFee)
            {
                record.Cash = Math.Max(0f, record.Cash - weeklyFee);
                record.UpdatedAtUtc = utcNow;
                result.PaidCount++;
                result.ChangedOwnerKeys.Add(record.OwnerKey);
                continue;
            }

            record.IsRecruited = false;
            record.Cash = 0f;
            record.AssignedCustomerNpcIds.Clear();
            record.UpdatedAtUtc = utcNow;
            result.LostCount++;
            result.LostDealerKeys.Add(pair.Key);
            result.ChangedOwnerKeys.Add(record.OwnerKey);
        }

        return result;
    }

    public static List<DealerRetentionWarningRecord> RecordDealerRetentionWarnings(
        OrganisationSaveData saveData,
        bool enabled,
        float weeklyFee,
        int elapsedDay,
        DateTime utcNow)
    {
        List<DealerRetentionWarningRecord> warnings = new List<DealerRetentionWarningRecord>();
        if (!enabled || weeklyFee <= 0f)
        {
            return warnings;
        }

        foreach (ScopedDealerRecord record in saveData.ScopedDealers.Values)
        {
            if (record == null
                || !record.IsRecruited
                || string.IsNullOrWhiteSpace(record.OwnerKey)
                || string.IsNullOrWhiteSpace(record.NpcId)
                || record.Cash + 0.009f >= weeklyFee
                || record.LastRetentionWarningElapsedDay == elapsedDay)
            {
                continue;
            }

            record.LastRetentionWarningElapsedDay = elapsedDay;
            record.UpdatedAtUtc = utcNow;
            warnings.Add(new DealerRetentionWarningRecord(record.OwnerKey, record.NpcId, record.Cash, weeklyFee));
        }

        return warnings;
    }

    private static bool ContainsIgnoreCase(IEnumerable<string> values, string candidate)
    {
        foreach (string value in values)
        {
            if (string.Equals(value, candidate, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}

internal sealed class DealerRetentionWarningRecord
{
    public DealerRetentionWarningRecord(string ownerKey, string dealerNpcId, float cash, float weeklyFee)
    {
        OwnerKey = ownerKey ?? string.Empty;
        DealerNpcId = dealerNpcId ?? string.Empty;
        Cash = cash;
        WeeklyFee = weeklyFee;
    }

    public string OwnerKey { get; }
    public string DealerNpcId { get; }
    public float Cash { get; }
    public float WeeklyFee { get; }
}

internal sealed class DealerRetentionProcessingResult
{
    public int ProcessedCount { get; set; }
    public int PaidCount { get; set; }
    public int LostCount { get; set; }
    public List<string> LostDealerKeys { get; } = new List<string>();
    public HashSet<string> ChangedOwnerKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    public bool HasChanges => PaidCount > 0 || LostCount > 0;
}
