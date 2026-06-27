#if SERVER
using System;
using System.Collections.Generic;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Persistence;
using DedicatedServerMod.Organisations.Utils;
using HarmonyLib;
using Newtonsoft.Json;
#if IL2CPP
using PersistenceCustomerData = Il2CppScheduleOne.Persistence.Datas.CustomerData;
using PersistenceSupplierData = Il2CppScheduleOne.Persistence.Datas.SupplierData;
using ScopedItemSet = Il2CppScheduleOne.Persistence.Datas.ItemSet;
using ScopedStringIntPair = Il2CppScheduleOne.DevUtilities.StringIntPair;
#else
using PersistenceCustomerData = ScheduleOne.Persistence.Datas.CustomerData;
using PersistenceSupplierData = ScheduleOne.Persistence.Datas.SupplierData;
using ScopedItemSet = ScheduleOne.Persistence.Datas.ItemSet;
using ScopedStringIntPair = ScheduleOne.DevUtilities.StringIntPair;
#endif
#if IL2CPP
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using Il2CppScheduleOne.Quests;
using Guid = Il2CppSystem.Guid;
#else
using ScheduleOne.Economy;
using ScheduleOne.ItemFramework;
using ScheduleOne.Map;
using ScheduleOne.NPCs;
using ScheduleOne.NPCs.Relation;
using ScheduleOne.Quests;
#endif

namespace DedicatedServerMod.Organisations.Services;

internal sealed class OrganisationCustomerContractService
{
    private static readonly System.Reflection.FieldInfo? RelationUnlockedBackingField = AccessTools.Field(typeof(NPCRelationData), "<Unlocked>k__BackingField");
    private static readonly System.Reflection.FieldInfo? RelationUnlockTypeBackingField = AccessTools.Field(typeof(NPCRelationData), "<UnlockType>k__BackingField");
    private static readonly System.Reflection.FieldInfo? RelationDeltaBackingField = AccessTools.Field(typeof(NPCRelationData), "<RelationDelta>k__BackingField");
    private static readonly System.Reflection.FieldInfo? OfferedContractInfoField = AccessTools.Field(typeof(Customer), "offeredContractInfo");
    private static readonly System.Reflection.FieldInfo? SupplierDeliveriesEnabledBackingField = AccessTools.Field(typeof(Supplier), "<DeliveriesEnabled>k__BackingField");
    private static readonly System.Reflection.FieldInfo? SupplierMinsUntilDeaddropReadyBackingField = AccessTools.Field(typeof(Supplier), "<minsUntilDeaddropReady>k__BackingField");
    private static readonly System.Reflection.FieldInfo? SupplierDeaddropItemsField = AccessTools.Field(typeof(Supplier), "deaddropItems");
    private static readonly System.Reflection.FieldInfo? SupplierDebtReminderSentField = AccessTools.Field(typeof(Supplier), "repaymentReminderSent");

    private readonly IOrganisationRepository _repository;
    private readonly IOrganisationService _organisationService;
    private readonly OrganisationLogger _logger;
    private readonly Dictionary<string, ScopedCustomerRecord> _initialCustomerTemplatesByGuid = new Dictionary<string, ScopedCustomerRecord>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingAcceptOwnerKeysByCustomerGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingOfferMutationOwnerKeysByCustomerGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingUnlockOwnerKeysByCustomerGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingRecommendationOwnerKeysByCustomerGuid = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _recentCompletedContractOwnerKeysByCustomerId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    public OrganisationCustomerContractService(IOrganisationRepository repository, IOrganisationService organisationService, OrganisationLogger logger)
    {
        _repository = repository;
        _organisationService = organisationService;
        _logger = logger;
    }

    private static bool IsEmptyGuid(Guid guid)
    {
        return guid.Equals(Guid.Empty);
    }

    public bool TryBeginContractAcceptance(string steamId, Customer customer, out string ownerKey, out string denialMessage)
    {
        ownerKey = string.Empty;
        denialMessage = string.Empty;
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            denialMessage = "Customer is unavailable right now.";
            return false;
        }

        ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        if (!OrganisationScopeRules.CanReserveCustomerContract(
                _repository.Current,
                ownerKey,
                customerGuid,
                "Sorry, found another dealer.",
                out denialMessage))
        {
            return false;
        }

        _pendingAcceptOwnerKeysByCustomerGuid[customerGuid] = ownerKey;
        UpsertScopedCustomer(ownerKey, customer);
        return true;
    }

    internal bool TryBeginCartelDealerContractAcceptance(string ownerKey, Customer customer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Cartel activity owner scope is not ready.";
            return false;
        }

        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            denialMessage = "Customer is unavailable right now.";
            return false;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        if (!OrganisationScopeRules.CanReserveCustomerContract(
                _repository.Current,
                ownerKey,
                customerGuid,
                customer.NPC.FirstName + " already found another dealer.",
                out denialMessage))
        {
            return false;
        }

        _pendingAcceptOwnerKeysByCustomerGuid[customerGuid] = ownerKey;
        UpsertScopedCustomer(ownerKey, customer);
        return true;
    }

    public void RegisterPendingUnlock(string steamId, Customer customer)
    {
        if (string.IsNullOrWhiteSpace(steamId) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        _pendingUnlockOwnerKeysByCustomerGuid[customerGuid] = ownerKey;
        UpsertScopedCustomer(ownerKey, customer);
    }

    public bool TryBeginCustomerDiscoveryMutation(string steamId, Customer customer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            denialMessage = "Customer is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        ScopedCustomerRecord record = UpsertScopedCustomer(ownerKey, customer);
        ApplyScopedCustomerState(customer, record);
        _pendingUnlockOwnerKeysByCustomerGuid[customerGuid] = ownerKey;
        return true;
    }

    public bool TryGetPendingUnlockOwnerKey(Customer customer, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return false;
        }

        if (!_pendingUnlockOwnerKeysByCustomerGuid.TryGetValue(customer.NPC.GUID.ToString(), out string? pendingOwnerKey)
            || string.IsNullOrWhiteSpace(pendingOwnerKey))
        {
            return false;
        }

        ownerKey = pendingOwnerKey;
        return true;
    }

    public bool TryPrepareRelationshipMutation(string steamId, Customer customer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            denialMessage = "Customer is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        ScopedCustomerRecord record = UpsertScopedCustomer(ownerKey, customer);
        ApplyScopedCustomerState(customer, record);
        return true;
    }

    public bool TryPrepareCustomerStateMutation(string steamId, Customer customer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            denialMessage = "Customer is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        string key = BuildScopedCustomerKey(ownerKey, customer.NPC.GUID.ToString());
        if (!_repository.Current.ScopedCustomers.TryGetValue(key, out ScopedCustomerRecord? record) || !record.IsUnlocked)
        {
            denialMessage = "Customer is not unlocked for your scope.";
            return false;
        }

        ApplyScopedCustomerState(customer, record);
        return true;
    }

    public void FinalizeUnlock(Customer customer)
    {
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        if (!_pendingUnlockOwnerKeysByCustomerGuid.TryGetValue(customerGuid, out string? ownerKey))
        {
            return;
        }

        _pendingUnlockOwnerKeysByCustomerGuid.Remove(customerGuid);
        ScopedCustomerRecord record = UpsertScopedCustomer(ownerKey, customer);
        record.IsUnlocked = true;
        record.RelationshipDelta = customer.NPC.RelationData.RelationDelta;
        record.HasBeenRecommended = customer.HasBeenRecommended;
        record.CustomerDataJson = BuildCustomerDataJson(customer, includeOffer: false);
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Recorded scoped customer unlock for ownerKey={ownerKey} customer={customerGuid}.");
    }

    public bool RecordOwnerCustomerUnlocked(string ownerKey, Customer customer)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return false;
        }

        ScopedCustomerRecord record = UpsertScopedCustomer(ownerKey, customer);
        bool changed = !record.IsUnlocked;
        record.IsUnlocked = true;
        record.RelationshipDelta = customer.NPC.RelationData.RelationDelta;
        record.HasBeenRecommended = customer.HasBeenRecommended;
        record.CustomerDataJson = BuildCustomerDataJson(customer, includeOffer: false);
        record.UpdatedAtUtc = DateTime.UtcNow;
        ApplyScopedCustomerState(customer, record);
        _repository.MarkDirty();
        return changed;
    }

    public bool RecordOwnerSupplierUnlocked(string ownerKey, Supplier supplier)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return false;
        }

        ScopedSupplierRecord record = UpsertScopedSupplier(ownerKey, supplier);
        bool changed = !record.IsUnlocked;
        CaptureSupplierState(supplier, record);
        record.IsUnlocked = true;
        record.UpdatedAtUtc = DateTime.UtcNow;
        HydrateSupplierFromRecord(supplier, record);
        _repository.MarkDirty();
        return changed;
    }

    public void RecordRelationship(string steamId, Customer customer, float relationship)
    {
        if (string.IsNullOrWhiteSpace(steamId) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        ScopedCustomerRecord record = UpsertScopedCustomer(ownerKey, customer);
        record.RelationshipDelta = relationship;
        record.IsUnlocked = customer.NPC.RelationData.Unlocked;
        record.HasBeenRecommended = customer.HasBeenRecommended;
        record.CustomerDataJson = BuildCustomerDataJson(customer, includeOffer: false);
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    public void RecordCustomerState(string steamId, Customer customer)
    {
        if (string.IsNullOrWhiteSpace(steamId) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        RecordCustomerStateForOwner(ownerKey, customer);
    }

    public bool RecordCustomerStateForOwner(string ownerKey, Customer customer)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return false;
        }

        ScopedCustomerRecord record = UpsertScopedCustomer(ownerKey, customer);
        bool changed = false;
        if (record.IsUnlocked != customer.NPC.RelationData.Unlocked)
        {
            record.IsUnlocked = customer.NPC.RelationData.Unlocked;
            changed = true;
        }

        if (!EqualityComparer<float>.Default.Equals(record.RelationshipDelta, customer.NPC.RelationData.RelationDelta))
        {
            record.RelationshipDelta = customer.NPC.RelationData.RelationDelta;
            changed = true;
        }

        if (record.OfferedDeals != customer.OfferedDeals)
        {
            record.OfferedDeals = customer.OfferedDeals;
            changed = true;
        }

        if (record.CompletedDeliveries != customer.CompletedDeliveries)
        {
            record.CompletedDeliveries = customer.CompletedDeliveries;
            changed = true;
        }

        if (record.HasBeenRecommended != customer.HasBeenRecommended)
        {
            record.HasBeenRecommended = customer.HasBeenRecommended;
            changed = true;
        }

        string customerDataJson = BuildCustomerDataJson(customer, includeOffer: true);
        if (!string.Equals(record.CustomerDataJson, customerDataJson, StringComparison.Ordinal))
        {
            record.CustomerDataJson = customerDataJson;
            changed = true;
        }

        if (!changed)
        {
            return false;
        }

        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public void RecordOfferedContract(string ownerKey, Customer customer)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        ScopedCustomerRecord record = UpsertScopedCustomer(ownerKey, customer);
        if (!record.IsUnlocked)
        {
            return;
        }

        record.CustomerDataJson = BuildCustomerDataJson(customer, includeOffer: true);
        record.OfferedDeals = Math.Max(record.OfferedDeals, customer.OfferedDeals);
        record.HasBeenRecommended = customer.HasBeenRecommended;
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    public void ClearOfferedContract(string ownerKey, Customer customer)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        string key = BuildScopedCustomerKey(ownerKey, customer.NPC.GUID.ToString());
        if (!_repository.Current.ScopedCustomers.TryGetValue(key, out ScopedCustomerRecord? record))
        {
            record = UpsertScopedCustomer(ownerKey, customer);
        }

        record.CustomerDataJson = BuildCustomerDataJson(customer, includeOffer: false);
        record.HasBeenRecommended = customer.HasBeenRecommended;
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    public bool TryBeginOfferMutation(string steamId, Customer customer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            denialMessage = "Customer is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        string key = BuildScopedCustomerKey(ownerKey, customerGuid);
        if (!_repository.Current.ScopedCustomers.TryGetValue(key, out ScopedCustomerRecord? record)
            || !record.IsUnlocked
            || string.IsNullOrWhiteSpace(record.CustomerDataJson))
        {
            denialMessage = "This offer is no longer available.";
            return false;
        }

        if (!TryReadCustomerData(record.CustomerDataJson, out PersistenceCustomerData? data)
            || data == null
            || !data.IsContractOffered
            || data.OfferedContract == null)
        {
            denialMessage = "This offer is no longer available.";
            return false;
        }

        customer.Load(data);
        _pendingOfferMutationOwnerKeysByCustomerGuid[customerGuid] = ownerKey;
        return true;
    }

    public bool CompleteOfferMutation(Customer customer)
    {
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return false;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        if (!_pendingOfferMutationOwnerKeysByCustomerGuid.TryGetValue(customerGuid, out string? ownerKey))
        {
            return false;
        }

        _pendingOfferMutationOwnerKeysByCustomerGuid.Remove(customerGuid);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        if (customer.OfferedContractInfo != null)
        {
            RecordOfferedContract(ownerKey, customer);
        }
        else
        {
            ClearOfferedContract(ownerKey, customer);
        }

        return true;
    }

    public bool RejectOfferedContract(string steamId, string customerGuid, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(customerGuid))
        {
            denialMessage = "Customer is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        string key = BuildScopedCustomerKey(ownerKey, customerGuid);
        if (!_repository.Current.ScopedCustomers.TryGetValue(key, out ScopedCustomerRecord? record)
            || string.IsNullOrWhiteSpace(record.CustomerDataJson))
        {
            denialMessage = "This offer is no longer available.";
            return false;
        }

        if (!TryReadCustomerData(record.CustomerDataJson, out PersistenceCustomerData? data)
            || data == null
            || !data.IsContractOffered
            || data.OfferedContract == null)
        {
            denialMessage = "This offer is no longer available.";
            return false;
        }

        data.IsContractOffered = false;
        data.OfferedContract = null;
        record.CustomerDataJson = data.GetJson(prettyPrint: false);
        record.UpdatedAtUtc = DateTime.UtcNow;
        _pendingOfferMutationOwnerKeysByCustomerGuid.Remove(customerGuid);
        _repository.MarkDirty();
        _logger.Info($"Rejected scoped customer offer for ownerKey={ownerKey} customer={customerGuid}.");
        return true;
    }

    public CustomerScopeSyncDto BuildScopeSyncForPlayer(string steamId)
    {
        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        CustomerScopeSyncDto sync = new CustomerScopeSyncDto
        {
            OwnerKey = ownerKey,
        };

        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return sync;
        }

        foreach (NPC npc in NPCManager.NPCRegistry)
        {
            Customer? customer = npc?.GetComponent<Customer>();
            if (npc is Dealer dealer && !string.IsNullOrWhiteSpace(dealer.ID))
            {
                string dealerKey = BuildScopedNpcKey(ownerKey, dealer.ID);
                _repository.Current.ScopedDealers.TryGetValue(dealerKey, out ScopedDealerRecord? dealerRecord);
                sync.Dealers.Add(new DealerScopeEntryDto
                {
                    NpcId = dealer.ID,
                    HasBeenRecommended = dealerRecord?.HasBeenRecommended ?? false,
                    IsRecruited = dealerRecord != null && string.Equals(dealerRecord.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase) && dealerRecord.IsRecruited,
                    BusyWithOtherScope = dealerRecord != null && !string.Equals(dealerRecord.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase) && dealerRecord.IsRecruited,
                    Cash = dealerRecord?.Cash ?? 0f,
                    CompletedDeals = dealerRecord != null && string.Equals(dealerRecord.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)
                        ? dealerRecord.CompletedDeals
                        : 0,
                    AssignedCustomerNpcIds = dealerRecord != null && string.Equals(dealerRecord.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)
                        ? new List<string>(dealerRecord.AssignedCustomerNpcIds)
                        : new List<string>(),
                });
            }

            if (npc is Supplier supplier && !string.IsNullOrWhiteSpace(supplier.ID))
            {
                string supplierKey = BuildScopedNpcKey(ownerKey, supplier.ID);
                _repository.Current.ScopedSuppliers.TryGetValue(supplierKey, out ScopedSupplierRecord? supplierRecord);
                sync.Suppliers.Add(new SupplierScopeEntryDto
                {
                    NpcId = supplier.ID,
                    IsUnlocked = supplierRecord?.IsUnlocked ?? false,
                    RelationshipDelta = supplierRecord?.RelationshipDelta ?? 2f,
                    DeliveriesEnabled = supplierRecord?.DeliveriesEnabled ?? false,
                    Debt = supplierRecord?.Debt ?? 0f,
                    DeadDropPreparing = supplierRecord?.DeadDropPreparing ?? false,
                    MinsUntilDeadDropReady = supplierRecord?.MinsUntilDeadDropReady ?? -1,
                });
            }

            if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
            {
                continue;
            }

            string npcGuid = customer.NPC.GUID.ToString();
            EnsureInitialCustomerTemplate(customer);
            string key = BuildScopedCustomerKey(ownerKey, npcGuid);
            _repository.Current.ScopedCustomers.TryGetValue(key, out ScopedCustomerRecord? record);
            _repository.Current.ActiveCustomerContracts.TryGetValue(npcGuid, out ActiveCustomerContractReservationRecord? reservation);

            if (!ShouldIncludeCustomerScopeEntry(record, reservation, ownerKey))
            {
                continue;
            }

            sync.Customers.Add(new CustomerScopeEntryDto
            {
                NpcGuid = npcGuid,
                IsUnlocked = record?.IsUnlocked ?? false,
                RelationshipDelta = record?.RelationshipDelta ?? 2f,
                HasBeenRecommended = record?.HasBeenRecommended ?? false,
                OfferedDeals = record?.OfferedDeals ?? 0,
                CompletedDeliveries = record?.CompletedDeliveries ?? 0,
                HasActiveContract = reservation != null && string.Equals(reservation.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase),
                BusyWithOtherScope = reservation != null && !string.Equals(reservation.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase),
                CustomerDataJson = BuildCustomerScopeDataJson(record),
            });
        }

        return sync;
    }

    public int GetUnlockedCustomerCountForOwner(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return 0;
        }

        int count = 0;
        foreach (NPC npc in NPCManager.NPCRegistry)
        {
            Customer? customer = npc?.GetComponent<Customer>();
            if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
            {
                continue;
            }

            EnsureInitialCustomerTemplate(customer);
            string key = BuildScopedCustomerKey(ownerKey, customer.NPC.GUID.ToString());
            if (_repository.Current.ScopedCustomers.TryGetValue(key, out ScopedCustomerRecord? record) && record.IsUnlocked)
            {
                count++;
            }
        }

        return count;
    }

    public bool TryGetCustomerCountsForOwnerByRegion(string ownerKey, EMapRegion region, out int unlockedCount, out int totalCount)
    {
        unlockedCount = 0;
        totalCount = 0;

        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        foreach (NPC npc in NPCManager.NPCRegistry)
        {
            Customer? customer = npc?.GetComponent<Customer>();
            if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID) || customer.NPC.Region != region)
            {
                continue;
            }

            totalCount++;
            EnsureInitialCustomerTemplate(customer);
            string key = BuildScopedCustomerKey(ownerKey, customer.NPC.GUID.ToString());
            if (_repository.Current.ScopedCustomers.TryGetValue(key, out ScopedCustomerRecord? record) && record.IsUnlocked)
            {
                unlockedCount++;
            }
        }

        return true;
    }

    public bool TryGetSupplierStateForOwner(string ownerKey, Supplier supplier, out bool isUnlocked, out bool deadDropPreparing, out int minsUntilDeadDropReady)
    {
        isUnlocked = false;
        deadDropPreparing = false;
        minsUntilDeadDropReady = -1;

        if (string.IsNullOrWhiteSpace(ownerKey) || supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return false;
        }

        string supplierKey = BuildScopedNpcKey(ownerKey, supplier.ID);
        if (!_repository.Current.ScopedSuppliers.TryGetValue(supplierKey, out ScopedSupplierRecord? record))
        {
            return false;
        }

        isUnlocked = record.IsUnlocked;
        deadDropPreparing = record.DeadDropPreparing;
        minsUntilDeadDropReady = record.MinsUntilDeadDropReady;
        return true;
    }

    public bool TryIsNpcUnlockedForOwner(string ownerKey, NPC npc, out bool isUnlocked)
    {
        isUnlocked = false;
        if (string.IsNullOrWhiteSpace(ownerKey) || npc == null)
        {
            return false;
        }

        Customer? customer = npc.GetComponent<Customer>();
        if (customer?.NPC != null && !IsEmptyGuid(customer.NPC.GUID))
        {
            EnsureInitialCustomerTemplate(customer);
            string customerKey = BuildScopedCustomerKey(ownerKey, customer.NPC.GUID.ToString());
            if (_repository.Current.ScopedCustomers.TryGetValue(customerKey, out ScopedCustomerRecord? customerRecord))
            {
                isUnlocked = customerRecord.IsUnlocked;
                return true;
            }

            return false;
        }

        if (npc is Supplier supplier && !string.IsNullOrWhiteSpace(supplier.ID))
        {
            string supplierKey = BuildScopedNpcKey(ownerKey, supplier.ID);
            if (_repository.Current.ScopedSuppliers.TryGetValue(supplierKey, out ScopedSupplierRecord? supplierRecord))
            {
                isUnlocked = supplierRecord.IsUnlocked;
                return true;
            }

            return false;
        }

        if (npc is Dealer dealer && !string.IsNullOrWhiteSpace(dealer.ID))
        {
            string dealerKey = BuildScopedNpcKey(ownerKey, dealer.ID);
            if (_repository.Current.ScopedDealers.TryGetValue(dealerKey, out ScopedDealerRecord? dealerRecord))
            {
                isUnlocked = dealerRecord.HasBeenRecommended || dealerRecord.IsRecruited;
                return true;
            }

            return false;
        }

        return false;
    }

    public void CompleteContractAcceptance(Customer customer, Contract? contract)
    {
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        if (!_pendingAcceptOwnerKeysByCustomerGuid.TryGetValue(customerGuid, out string? ownerKey))
        {
            return;
        }

        _pendingAcceptOwnerKeysByCustomerGuid.Remove(customerGuid);
        if (contract == null || string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        string deliveryLocationGuid = contract.DeliveryLocation != null && !IsEmptyGuid(contract.DeliveryLocation.GUID)
            ? contract.DeliveryLocation.GUID.ToString()
            : string.Empty;

        _repository.Current.ActiveCustomerContracts[customerGuid] = new ActiveCustomerContractReservationRecord
        {
            CustomerNpcGuid = customerGuid,
            OwnerKey = ownerKey,
            ContractGuid = contract.GUID.ToString(),
            DeliveryLocationGuid = deliveryLocationGuid,
            AcceptedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        ScopedCustomerRecord customerRecord = UpsertScopedCustomer(ownerKey, customer);
        customerRecord.IsUnlocked = customer.NPC.RelationData.Unlocked;
        customerRecord.RelationshipDelta = customer.NPC.RelationData.RelationDelta;
        customerRecord.OfferedDeals = Math.Max(customerRecord.OfferedDeals, customer.OfferedDeals);
        customerRecord.HasBeenRecommended = customer.HasBeenRecommended;
        customerRecord.CustomerDataJson = BuildCustomerDataJson(customer, includeOffer: false);
        customerRecord.UpdatedAtUtc = DateTime.UtcNow;

        _repository.Current.ScopedContracts[contract.GUID.ToString()] = new ScopedContractRecord
        {
            ContractGuid = contract.GUID.ToString(),
            OwnerKey = ownerKey,
            CustomerNpcGuid = customerGuid,
            DeliveryLocationGuid = deliveryLocationGuid,
            ContractDataJson = contract.GetSaveString(),
            Status = "Active",
            AcceptedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _repository.MarkDirty();
        _logger.Info($"Reserved active contract slot for customer {customerGuid} ownerKey={ownerKey} contractGuid={contract.GUID}.");
    }

    public void ReleaseActiveContract(Customer customer, string outcome)
    {
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        _pendingAcceptOwnerKeysByCustomerGuid.Remove(customerGuid);
        _pendingUnlockOwnerKeysByCustomerGuid.Remove(customerGuid);
        if (!_repository.Current.ActiveCustomerContracts.TryGetValue(customerGuid, out ActiveCustomerContractReservationRecord? reservation))
        {
            return;
        }

        _repository.Current.ActiveCustomerContracts.Remove(customerGuid);
        _pendingOfferMutationOwnerKeysByCustomerGuid.Remove(customerGuid);
        if (string.Equals(outcome, EQuestState.Completed.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            _pendingRecommendationOwnerKeysByCustomerGuid[customerGuid] = reservation.OwnerKey;
        }

        if (!string.IsNullOrWhiteSpace(reservation.ContractGuid)
            && _repository.Current.ScopedContracts.TryGetValue(reservation.ContractGuid, out ScopedContractRecord? contractRecord))
        {
            contractRecord.Status = string.IsNullOrWhiteSpace(outcome) ? "Ended" : outcome;
            contractRecord.UpdatedAtUtc = DateTime.UtcNow;
        }

        ScopedCustomerRecord customerRecord = UpsertScopedCustomer(reservation.OwnerKey, customer);
        customerRecord.CompletedDeliveries = Math.Max(customerRecord.CompletedDeliveries, customer.CompletedDeliveries);
        customerRecord.HasBeenRecommended = customer.HasBeenRecommended;
        customerRecord.CustomerDataJson = BuildCustomerDataJson(customer, includeOffer: false);
        customerRecord.UpdatedAtUtc = DateTime.UtcNow;

        _repository.MarkDirty();
        _logger.Info($"Released active contract slot for customer {customerGuid} outcome={outcome}.");
    }

    public bool PrepareCompletedContractReceiptOwner(Customer customer)
    {
        return TryPrepareCompletedContractReceiptOwner(customer, out _);
    }

    public bool TryPrepareCompletedContractReceiptOwner(Customer customer, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID) || string.IsNullOrWhiteSpace(customer.NPC.ID))
        {
            return false;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        if (!_repository.Current.ActiveCustomerContracts.TryGetValue(customerGuid, out ActiveCustomerContractReservationRecord? reservation)
            || string.IsNullOrWhiteSpace(reservation.OwnerKey))
        {
            return false;
        }

        ownerKey = reservation.OwnerKey;
        _recentCompletedContractOwnerKeysByCustomerId[customer.NPC.ID] = ownerKey;
        return true;
    }

    public bool TryConsumeRecentCompletedContractOwnerForCustomer(string customerId, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (string.IsNullOrWhiteSpace(customerId))
        {
            return false;
        }

        if (!_recentCompletedContractOwnerKeysByCustomerId.TryGetValue(customerId, out string? recentOwnerKey)
            || string.IsNullOrWhiteSpace(recentOwnerKey))
        {
            return false;
        }

        ownerKey = recentOwnerKey;
        _recentCompletedContractOwnerKeysByCustomerId.Remove(customerId);
        return true;
    }

    public bool TryGetRecentCompletedContractOwnerForCustomer(string customerId, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (string.IsNullOrWhiteSpace(customerId)
            || !_recentCompletedContractOwnerKeysByCustomerId.TryGetValue(customerId, out string? recentOwnerKey)
            || string.IsNullOrWhiteSpace(recentOwnerKey))
        {
            return false;
        }

        ownerKey = recentOwnerKey;
        return true;
    }

    public void ClearRecentCompletedContractOwnerForCustomer(string customerId)
    {
        if (!string.IsNullOrWhiteSpace(customerId))
        {
            _recentCompletedContractOwnerKeysByCustomerId.Remove(customerId);
        }
    }

    public bool RecordRecommendedNpcForCompletedContract(Customer sourceCustomer, string recommendedNpcId)
    {
        if (sourceCustomer?.NPC == null || IsEmptyGuid(sourceCustomer.NPC.GUID))
        {
            return false;
        }

        string sourceCustomerGuid = sourceCustomer.NPC.GUID.ToString();
        if (!_pendingRecommendationOwnerKeysByCustomerGuid.TryGetValue(sourceCustomerGuid, out string? ownerKey))
        {
            return false;
        }

        _pendingRecommendationOwnerKeysByCustomerGuid.Remove(sourceCustomerGuid);
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(recommendedNpcId))
        {
            return false;
        }

        NPC recommendedNpc = NPCManager.GetNPC(recommendedNpcId);
        if (recommendedNpc is Dealer dealer)
        {
            ScopedDealerRecord dealerRecord = UpsertScopedDealer(ownerKey, dealer);
            dealerRecord.HasBeenRecommended = true;
            dealerRecord.UpdatedAtUtc = DateTime.UtcNow;
            _repository.MarkDirty();
            _logger.Info($"Recorded scoped dealer recommendation for ownerKey={ownerKey} source={sourceCustomerGuid} recommended={dealer.ID}.");
            return true;
        }

        if (recommendedNpc is Supplier supplier)
        {
            ScopedSupplierRecord supplierRecord = UpsertScopedSupplier(ownerKey, supplier);
            supplierRecord.IsUnlocked = true;
            supplierRecord.RelationshipDelta = Math.Max(supplierRecord.RelationshipDelta, supplier.RelationData.RelationDelta);
            supplierRecord.UpdatedAtUtc = DateTime.UtcNow;
            _repository.MarkDirty();
            _logger.Info($"Recorded scoped supplier unlock for ownerKey={ownerKey} source={sourceCustomerGuid} supplier={supplier.ID}.");
            return true;
        }

        Customer? recommendedCustomer = recommendedNpc?.GetComponent<Customer>();
        if (recommendedCustomer?.NPC == null || IsEmptyGuid(recommendedCustomer.NPC.GUID))
        {
            return false;
        }

        ScopedCustomerRecord record = UpsertScopedCustomer(ownerKey, recommendedCustomer);
        record.HasBeenRecommended = true;
        if (!TryReadCustomerData(record.CustomerDataJson, out PersistenceCustomerData? data) || data == null)
        {
            data = recommendedCustomer.GetCustomerData();
            data.IsContractOffered = false;
            data.OfferedContract = null;
        }

        data.HasBeenRecommended = true;
        record.CustomerDataJson = data.GetJson(prettyPrint: false);
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Recorded scoped customer recommendation for ownerKey={ownerKey} source={sourceCustomerGuid} recommended={record.NpcGuid}.");
        return true;
    }

    public bool TryRecruitDealer(string steamId, Dealer dealer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || dealer == null || string.IsNullOrWhiteSpace(dealer.ID))
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        if (!OrganisationScopeRules.CanRecruitDealer(
                _repository.Current,
                ownerKey,
                dealer.ID,
                dealer.FirstName,
                out ScopedDealerRecord? record,
                out denialMessage))
        {
            return false;
        }

        if (record == null)
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        record.IsRecruited = true;
        record.Cash = 0f;
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Recorded scoped dealer recruitment for ownerKey={ownerKey} dealer={dealer.ID}.");
        return true;
    }

    public bool TryAssignDealerCustomer(string steamId, Dealer dealer, string customerNpcId, out string denialMessage)
    {
        return TryMutateDealerAssignment(steamId, dealer, customerNpcId, addAssignment: true, out denialMessage);
    }

    public bool TryRemoveDealerCustomer(string steamId, Dealer dealer, string customerNpcId, out string denialMessage)
    {
        return TryMutateDealerAssignment(steamId, dealer, customerNpcId, addAssignment: false, out denialMessage);
    }

    public bool TrySetDealerCash(string steamId, Dealer dealer, float cash, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (!TryGetOwnedDealerRecord(steamId, dealer, out ScopedDealerRecord? record, out denialMessage))
        {
            return false;
        }

        if (record == null)
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        record.Cash = Math.Max(0f, cash);
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Set scoped dealer cash ownerKey={record.OwnerKey} dealer={dealer.ID} cash={record.Cash}.");
        return true;
    }

    public bool TrySubmitDealerPayment(string steamId, Dealer dealer, float payment, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (payment <= 0f)
        {
            return true;
        }

        if (!TryGetOwnedDealerRecord(steamId, dealer, out ScopedDealerRecord? record, out denialMessage))
        {
            return false;
        }

        if (record == null)
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        record.Cash = Math.Max(0f, record.Cash + payment * (1f - dealer.Cut));
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Submitted scoped dealer payment ownerKey={record.OwnerKey} dealer={dealer.ID} payment={payment} cash={record.Cash}.");
        return true;
    }

    public bool TryRecordDealerCompletedDeal(string steamId, Dealer dealer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (!TryGetOwnedDealerRecord(steamId, dealer, out ScopedDealerRecord? record, out denialMessage))
        {
            return false;
        }

        if (record == null)
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        record.CompletedDeals++;
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Recorded scoped dealer completed deal ownerKey={record.OwnerKey} dealer={dealer.ID} completedDeals={record.CompletedDeals}.");
        return true;
    }

    public bool TryRecordDealerCompletedDealForOwner(string ownerKey, Dealer dealer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (!TryGetOwnedDealerRecordForOwner(ownerKey, dealer, out ScopedDealerRecord? record, out denialMessage))
        {
            return false;
        }

        if (record == null)
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        record.CompletedDeals++;
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Recorded scoped dealer completed deal ownerKey={record.OwnerKey} dealer={dealer.ID} completedDeals={record.CompletedDeals}.");
        return true;
    }

    public DealerRetentionProcessingResult ProcessWeeklyDealerRetention(bool enabled, float weeklyFee)
    {
        DealerRetentionProcessingResult result = OrganisationScopeRules.ProcessDealerRetentionFees(
            _repository.Current,
            enabled,
            weeklyFee,
            DateTime.UtcNow);
        if (!result.HasChanges)
        {
            return result;
        }

        _repository.MarkDirty();
        _logger.Info($"Processed scoped dealer retention fees. Processed={result.ProcessedCount}, Paid={result.PaidCount}, Lost={result.LostCount}, WeeklyFee={weeklyFee:0.##}.");
        foreach (string dealerKey in result.LostDealerKeys)
        {
            _logger.Info($"Scoped dealer retention failed; dealer stopped working for owner. DealerKey={dealerKey}.");
        }

        return result;
    }

    public List<DealerRetentionWarningRecord> RecordWeeklyDealerRetentionWarnings(bool enabled, float weeklyFee, int elapsedDay)
    {
        List<DealerRetentionWarningRecord> warnings = OrganisationScopeRules.RecordDealerRetentionWarnings(
            _repository.Current,
            enabled,
            weeklyFee,
            elapsedDay,
            DateTime.UtcNow);
        if (warnings.Count == 0)
        {
            return warnings;
        }

        _repository.MarkDirty();
        _logger.Info($"Recorded scoped dealer retention warnings. Count={warnings.Count}, WeeklyFee={weeklyFee:0.##}, ElapsedDay={elapsedDay}.");
        return warnings;
    }

    public bool TrySubmitDealerPaymentForOwner(string ownerKey, Dealer dealer, float payment, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (payment <= 0f)
        {
            return true;
        }

        if (!TryGetOwnedDealerRecordForOwner(ownerKey, dealer, out ScopedDealerRecord? record, out denialMessage))
        {
            return false;
        }

        if (record == null)
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        record.Cash = Math.Max(0f, record.Cash + payment * (1f - dealer.Cut));
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Submitted scoped dealer payment ownerKey={record.OwnerKey} dealer={dealer.ID} payment={payment} cash={record.Cash}.");
        return true;
    }

    public bool CanAccessDealerInventory(string steamId, Dealer dealer, out string denialMessage)
    {
        if (!TryGetOwnedDealerRecord(steamId, dealer, out ScopedDealerRecord? record, out denialMessage) || record == null)
        {
            return false;
        }

        HydrateDealerInventoryFromRecord(dealer, record);
        return true;
    }

    public void RecordScopedDealerInventory(string steamId, Dealer dealer)
    {
        if (!TryGetOwnedDealerRecord(steamId, dealer, out ScopedDealerRecord? record, out _) || record == null)
        {
            return;
        }

        CaptureDealerInventory(dealer, record);
        _repository.MarkDirty();
    }

    public bool TryPrepareDealerInventoryMutation(Dealer dealer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (!TryGetRecruitedDealerRecord(dealer, out ScopedDealerRecord? record, out denialMessage) || record == null)
        {
            return false;
        }

        HydrateDealerInventoryFromRecord(dealer, record);
        return true;
    }

    public bool TryPrepareDealerStateMutation(Dealer dealer, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (!TryGetRecruitedDealerRecord(dealer, out ScopedDealerRecord? record, out denialMessage) || record == null)
        {
            return false;
        }

        HydrateDealerStateFromRecord(dealer, record);
        return true;
    }

    internal bool TryGetDealerForCartelRobbery(string ownerKey, EMapRegion region, out Dealer? dealer)
    {
        dealer = null;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        foreach (ScopedDealerRecord record in _repository.Current.ScopedDealers.Values)
        {
            if (record == null
                || !record.IsRecruited
                || !string.Equals(record.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)
                || record.Cash < 100f
                || record.AssignedCustomerNpcIds.Count == 0)
            {
                continue;
            }

            Dealer? candidate = Dealer.AllPlayerDealers.AsManagedEnumerable().FirstOrDefault(item =>
                item != null
                && string.Equals(item.ID, record.NpcId, StringComparison.OrdinalIgnoreCase)
                && item.Region == region
                && item.IsConscious);
            if (candidate == null)
            {
                continue;
            }

            HydrateDealerStateFromRecord(candidate, record);
            if (GetDealerInventoryQuantity(candidate) < 4)
            {
                continue;
            }

            dealer = candidate;
            return true;
        }

        return false;
    }

    public bool TryGetRecruitedDealerForOwnerInRegion(string ownerKey, EMapRegion region, out Dealer? dealer)
    {
        dealer = null;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        foreach (ScopedDealerRecord record in _repository.Current.ScopedDealers.Values)
        {
            if (record == null
                || !record.IsRecruited
                || !string.Equals(record.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(record.NpcId))
            {
                continue;
            }

            foreach (Dealer candidate in Dealer.AllPlayerDealers)
            {
                if (candidate != null
                    && candidate.Region == region
                    && string.Equals(candidate.ID, record.NpcId, StringComparison.OrdinalIgnoreCase))
                {
                    dealer = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public void RecordScopedDealerInventory(Dealer dealer)
    {
        if (!TryGetRecruitedDealerRecord(dealer, out ScopedDealerRecord? record, out _) || record == null)
        {
            return;
        }

        CaptureDealerInventory(dealer, record);
        _repository.MarkDirty();
    }

    public void RecordScopedDealerState(Dealer dealer)
    {
        if (!TryGetRecruitedDealerRecord(dealer, out ScopedDealerRecord? record, out _) || record == null)
        {
            return;
        }

        CaptureDealerState(dealer, record);
        _repository.MarkDirty();
    }

    public bool TryPrepareSupplierMutation(string steamId, Supplier supplier, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (!TryGetOwnedSupplierRecord(steamId, supplier, out ScopedSupplierRecord? record, out denialMessage) || record == null)
        {
            return false;
        }

        HydrateSupplierFromRecord(supplier, record);
        return true;
    }

    public void RecordScopedSupplierState(string steamId, Supplier supplier)
    {
        if (!TryGetOwnedSupplierRecord(steamId, supplier, out ScopedSupplierRecord? record, out _) || record == null)
        {
            return;
        }

        CaptureSupplierState(supplier, record);
        _repository.MarkDirty();
    }

    public bool TryPrepareSupplierMutationForOwner(string ownerKey, Supplier supplier, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (!TryGetSupplierRecordForOwner(ownerKey, supplier, out ScopedSupplierRecord? record, out denialMessage) || record == null)
        {
            return false;
        }

        HydrateSupplierFromRecord(supplier, record);
        return true;
    }

    public void RecordScopedSupplierStateForOwner(string ownerKey, Supplier supplier)
    {
        if (!TryGetSupplierRecordForOwner(ownerKey, supplier, out ScopedSupplierRecord? record, out _) || record == null)
        {
            return;
        }

        CaptureSupplierState(supplier, record);
        _repository.MarkDirty();
    }

    public bool TryGetSingleUnlockedSupplierOwnerKey(Supplier supplier, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return false;
        }

        foreach (ScopedSupplierRecord candidate in _repository.Current.ScopedSuppliers.Values)
        {
            if (candidate == null
                || !candidate.IsUnlocked
                || !string.Equals(candidate.NpcId, supplier.ID, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ownerKey))
            {
                ownerKey = string.Empty;
                return false;
            }

            ownerKey = candidate.OwnerKey;
        }

        return !string.IsNullOrWhiteSpace(ownerKey);
    }

    public bool TryGetSinglePreparingSupplierDeaddropOwnerKey(Supplier supplier, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return false;
        }

        foreach (ScopedSupplierRecord candidate in _repository.Current.ScopedSuppliers.Values)
        {
            if (candidate == null
                || !candidate.IsUnlocked
                || !candidate.DeadDropPreparing
                || !string.Equals(candidate.NpcId, supplier.ID, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ownerKey))
            {
                ownerKey = string.Empty;
                return false;
            }

            ownerKey = candidate.OwnerKey;
        }

        return !string.IsNullOrWhiteSpace(ownerKey);
    }

    public bool CanUseSupplier(string steamId, Supplier supplier)
    {
        return TryGetOwnedSupplierRecord(steamId, supplier, out _, out _);
    }

    public bool TryBeginDealerContractAcceptance(Dealer dealer, Customer customer, out string ownerKey, out string denialMessage)
    {
        ownerKey = string.Empty;
        denialMessage = string.Empty;
        if (dealer == null || string.IsNullOrWhiteSpace(dealer.ID) || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            denialMessage = "Dealer contract is unavailable right now.";
            return false;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        if (OrganisationScopeRules.TryFindDealerContractOwner(
                _repository.Current,
                dealer.ID,
                dealer.FirstName,
                customer.NPC.ID,
                customerGuid,
                customer.NPC.FirstName,
                out ownerKey,
                out denialMessage))
        {
            _pendingAcceptOwnerKeysByCustomerGuid[customerGuid] = ownerKey;
            UpsertScopedCustomer(ownerKey, customer);
            return true;
        }

        return false;
    }

    private ScopedDealerRecord UpsertScopedDealer(string ownerKey, Dealer dealer)
    {
        string key = BuildScopedNpcKey(ownerKey, dealer.ID);
        if (_repository.Current.ScopedDealers.TryGetValue(key, out ScopedDealerRecord? record))
        {
            return record;
        }

        record = new ScopedDealerRecord
        {
            OwnerKey = ownerKey,
            NpcId = dealer.ID,
            HasBeenRecommended = false,
            IsRecruited = false,
            Cash = 0f,
            CompletedDeals = 0,
            AssignedCustomerNpcIds = new List<string>(),
            UpdatedAtUtc = DateTime.UtcNow,
        };
        CaptureDealerInventory(dealer, record);
        _repository.Current.ScopedDealers[key] = record;
        return record;
    }

    private static void HydrateDealerInventoryFromRecord(Dealer dealer, ScopedDealerRecord record)
    {
        var slots = dealer.GetAllSlots();
        ScopedItemSet itemSet = new ScopedItemSet(slots);
        itemSet.Items = DeserializeStringArray(record.InventoryItemsJson);
        itemSet.SlotFilters = DeserializeSlotFilterArray(record.InventorySlotFiltersJson);
        itemSet.LoadTo(slots);
    }

    private static void HydrateDealerStateFromRecord(Dealer dealer, ScopedDealerRecord record)
    {
        HydrateDealerInventoryFromRecord(dealer, record);
        SetDealerCash(dealer, Math.Max(0f, record.Cash));
    }

    private static int GetDealerInventoryQuantity(Dealer dealer)
    {
#if IL2CPP
        return dealer.Inventory?.ItemSlots?.AsManagedEnumerable().Sum(slot => slot?.Quantity ?? 0) ?? 0;
#else
        return ((IItemSlotOwner)dealer.Inventory).GetQuantitySum();
#endif
    }

    private static void CaptureDealerInventory(Dealer dealer, ScopedDealerRecord record)
    {
        ScopedItemSet itemSet = new ScopedItemSet(dealer.GetAllSlots());
        record.InventoryItemsJson = JsonConvert.SerializeObject(itemSet.Items ?? Array.Empty<string>());
        record.InventorySlotFiltersJson = JsonConvert.SerializeObject(itemSet.SlotFilters ?? Array.Empty<SlotFilter>());
        record.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static void CaptureDealerState(Dealer dealer, ScopedDealerRecord record)
    {
        CaptureDealerInventory(dealer, record);
        record.Cash = Math.Max(0f, dealer.Cash);
    }

    private static void SetDealerCash(Dealer dealer, float cash)
    {
        AccessTools.PropertySetter(typeof(Dealer), nameof(Dealer.Cash))?.Invoke(dealer, new object[] { cash });
    }

    private static string[]? DeserializeStringArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<string[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static SlotFilter[]? DeserializeSlotFilterArray(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<SlotFilter[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private bool TryMutateDealerAssignment(string steamId, Dealer dealer, string customerNpcId, bool addAssignment, out string denialMessage)
    {
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || dealer == null || string.IsNullOrWhiteSpace(dealer.ID) || string.IsNullOrWhiteSpace(customerNpcId))
        {
            denialMessage = "Dealer assignment is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        string dealerKey = BuildScopedNpcKey(ownerKey, dealer.ID);
        if (!_repository.Current.ScopedDealers.TryGetValue(dealerKey, out ScopedDealerRecord? dealerRecord) || !dealerRecord.IsRecruited)
        {
            denialMessage = dealer.FirstName + " does not work for your scope.";
            return false;
        }

        NPC customerNpc = NPCManager.GetNPC(customerNpcId);
        Customer? customer = customerNpc?.GetComponent<Customer>();
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            denialMessage = "Customer is unavailable right now.";
            return false;
        }

        string customerKey = BuildScopedCustomerKey(ownerKey, customer.NPC.GUID.ToString());
        if (!_repository.Current.ScopedCustomers.TryGetValue(customerKey, out ScopedCustomerRecord? customerRecord) || !customerRecord.IsUnlocked)
        {
            denialMessage = "That customer is not unlocked for your scope.";
            return false;
        }

        if (addAssignment)
        {
            if (!dealerRecord.AssignedCustomerNpcIds.Exists(id => string.Equals(id, customerNpcId, StringComparison.OrdinalIgnoreCase)))
            {
                dealerRecord.AssignedCustomerNpcIds.Add(customerNpcId);
            }
        }
        else
        {
            dealerRecord.AssignedCustomerNpcIds.RemoveAll(id => string.Equals(id, customerNpcId, StringComparison.OrdinalIgnoreCase));
        }

        dealerRecord.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"{(addAssignment ? "Assigned" : "Removed")} scoped dealer customer ownerKey={ownerKey} dealer={dealer.ID} customer={customerNpcId}.");
        return true;
    }

    private bool TryGetOwnedDealerRecord(string steamId, Dealer dealer, out ScopedDealerRecord? record, out string denialMessage)
    {
        record = null;
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || dealer == null || string.IsNullOrWhiteSpace(dealer.ID))
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        return TryGetOwnedDealerRecordForOwner(ownerKey, dealer, out record, out denialMessage);
    }

    private bool TryGetOwnedDealerRecordForOwner(string ownerKey, Dealer dealer, out ScopedDealerRecord? record, out string denialMessage)
    {
        record = null;
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(ownerKey) || dealer == null || string.IsNullOrWhiteSpace(dealer.ID))
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        string dealerKey = BuildScopedNpcKey(ownerKey, dealer.ID);
        if (!_repository.Current.ScopedDealers.TryGetValue(dealerKey, out record) || !record.IsRecruited)
        {
            denialMessage = dealer.FirstName + " does not work for this scope.";
            return false;
        }

        return true;
    }

    private bool TryGetRecruitedDealerRecord(Dealer dealer, out ScopedDealerRecord? record, out string denialMessage)
    {
        record = null;
        denialMessage = string.Empty;
        if (dealer == null || string.IsNullOrWhiteSpace(dealer.ID))
        {
            denialMessage = "Dealer is unavailable right now.";
            return false;
        }

        foreach (ScopedDealerRecord candidate in _repository.Current.ScopedDealers.Values)
        {
            if (candidate != null
                && candidate.IsRecruited
                && string.Equals(candidate.NpcId, dealer.ID, StringComparison.OrdinalIgnoreCase))
            {
                record = candidate;
                return true;
            }
        }

        denialMessage = dealer.FirstName + " does not work for a scoped owner.";
        return false;
    }

    private bool TryGetOwnedSupplierRecord(string steamId, Supplier supplier, out ScopedSupplierRecord? record, out string denialMessage)
    {
        record = null;
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(steamId) || supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            denialMessage = "Supplier is unavailable right now.";
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            denialMessage = "Player scope is not ready yet.";
            return false;
        }

        string supplierKey = BuildScopedNpcKey(ownerKey, supplier.ID);
        if (!_repository.Current.ScopedSuppliers.TryGetValue(supplierKey, out record) || !record.IsUnlocked)
        {
            denialMessage = supplier.FirstName + " is not unlocked for your scope.";
            return false;
        }

        return true;
    }

    private bool TryGetSupplierRecordForOwner(string ownerKey, Supplier supplier, out ScopedSupplierRecord? record, out string denialMessage)
    {
        record = null;
        denialMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(ownerKey) || supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            denialMessage = "Supplier is unavailable right now.";
            return false;
        }

        string supplierKey = BuildScopedNpcKey(ownerKey, supplier.ID);
        if (!_repository.Current.ScopedSuppliers.TryGetValue(supplierKey, out record) || !record.IsUnlocked)
        {
            denialMessage = supplier.FirstName + " is not unlocked for this scope.";
            return false;
        }

        return true;
    }

    private ScopedSupplierRecord UpsertScopedSupplier(string ownerKey, Supplier supplier)
    {
        string key = BuildScopedNpcKey(ownerKey, supplier.ID);
        if (_repository.Current.ScopedSuppliers.TryGetValue(key, out ScopedSupplierRecord? record))
        {
            return record;
        }

        record = new ScopedSupplierRecord
        {
            OwnerKey = ownerKey,
            NpcId = supplier.ID,
            IsUnlocked = false,
            RelationshipDelta = supplier.RelationData.RelationDelta,
            DeliveriesEnabled = supplier.DeliveriesEnabled,
            Debt = supplier.sync___get_value_debt(),
            DeadDropPreparing = supplier.sync___get_value_deadDropPreparing(),
            MinsUntilDeadDropReady = supplier.minsUntilDeaddropReady,
            UpdatedAtUtc = DateTime.UtcNow,
        };
        _repository.Current.ScopedSuppliers[key] = record;
        return record;
    }

    private static void HydrateSupplierFromRecord(Supplier supplier, ScopedSupplierRecord record)
    {
        RelationDeltaBackingField?.SetValue(supplier.RelationData, record.RelationshipDelta);
        if (record.IsUnlocked)
        {
            RelationUnlockedBackingField?.SetValue(supplier.RelationData, true);
            RelationUnlockTypeBackingField?.SetValue(supplier.RelationData, NPCRelationData.EUnlockType.Recommendation);
        }
        else
        {
            RelationUnlockedBackingField?.SetValue(supplier.RelationData, false);
        }

        SupplierDeliveriesEnabledBackingField?.SetValue(supplier, record.DeliveriesEnabled);
        SupplierMinsUntilDeaddropReadyBackingField?.SetValue(supplier, record.MinsUntilDeadDropReady);
        SupplierDebtReminderSentField?.SetValue(supplier, record.DebtReminderSent);
        supplier.sync___set_value_debt(record.Debt, asServer: true);
        supplier.sync___set_value_deadDropPreparing(record.DeadDropPreparing, asServer: true);
        SupplierDeaddropItemsField?.SetValue(supplier, DeserializeDeaddropItems(record.DeaddropItemsJson));
    }

    private static void CaptureSupplierState(Supplier supplier, ScopedSupplierRecord record)
    {
        record.IsUnlocked = supplier.RelationData.Unlocked;
        record.RelationshipDelta = supplier.RelationData.RelationDelta;
        record.DeliveriesEnabled = supplier.DeliveriesEnabled;
        record.Debt = supplier.sync___get_value_debt();
        record.DeadDropPreparing = supplier.sync___get_value_deadDropPreparing();
        record.MinsUntilDeadDropReady = supplier.minsUntilDeaddropReady;
        record.DebtReminderSent = SupplierDebtReminderSentField?.GetValue(supplier) is bool reminderSent && reminderSent;
        record.DeaddropItemsJson = SerializeDeaddropItems(supplier.GetNPCData() as PersistenceSupplierData);
        record.UpdatedAtUtc = DateTime.UtcNow;
    }

    private static string SerializeDeaddropItems(PersistenceSupplierData? supplierData)
    {
        return supplierData?.deaddropItems == null
            ? string.Empty
            : JsonConvert.SerializeObject(supplierData.deaddropItems);
    }

    private static ScopedStringIntPair[]? DeserializeDeaddropItems(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<ScopedStringIntPair[]>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private ScopedCustomerRecord UpsertScopedCustomer(string ownerKey, Customer customer)
    {
        string customerGuid = customer.NPC.GUID.ToString();
        EnsureInitialCustomerTemplate(customer);

        string key = BuildScopedCustomerKey(ownerKey, customerGuid);
        if (_repository.Current.ScopedCustomers.TryGetValue(key, out ScopedCustomerRecord? record))
        {
            return record;
        }

        if (_initialCustomerTemplatesByGuid.TryGetValue(customerGuid, out ScopedCustomerRecord? template))
        {
            record = CloneCustomerTemplate(template, ownerKey);
        }
        else
        {
            record = BuildCustomerTemplate(customer, ownerKey);
        }

        record.OwnerKey = ownerKey;
        record.NpcGuid = customerGuid;
        record.UpdatedAtUtc = DateTime.UtcNow;
        _repository.Current.ScopedCustomers[key] = record;
        return record;
    }

    private void EnsureInitialCustomerTemplate(Customer customer)
    {
        if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return;
        }

        string customerGuid = customer.NPC.GUID.ToString();
        if (_initialCustomerTemplatesByGuid.ContainsKey(customerGuid))
        {
            return;
        }

        _initialCustomerTemplatesByGuid[customerGuid] = BuildCustomerTemplate(customer, string.Empty);
    }

    private static ScopedCustomerRecord BuildCustomerTemplate(Customer customer, string ownerKey)
    {
        return new ScopedCustomerRecord
        {
            OwnerKey = ownerKey,
            NpcGuid = customer.NPC.GUID.ToString(),
            IsUnlocked = customer.NPC.RelationData.Unlocked,
            RelationshipDelta = customer.NPC.RelationData.RelationDelta,
            OfferedDeals = customer.OfferedDeals,
            CompletedDeliveries = customer.CompletedDeliveries,
            HasBeenRecommended = customer.HasBeenRecommended,
            CustomerDataJson = BuildCustomerDataJson(customer, includeOffer: false),
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    private static ScopedCustomerRecord CloneCustomerTemplate(ScopedCustomerRecord template, string ownerKey)
    {
        return new ScopedCustomerRecord
        {
            OwnerKey = ownerKey,
            NpcGuid = template.NpcGuid,
            IsUnlocked = template.IsUnlocked,
            RelationshipDelta = template.RelationshipDelta,
            OfferedDeals = template.OfferedDeals,
            CompletedDeliveries = template.CompletedDeliveries,
            HasBeenRecommended = template.HasBeenRecommended,
            CustomerDataJson = template.CustomerDataJson,
            UpdatedAtUtc = DateTime.UtcNow,
        };
    }

    private bool ShouldIncludeCustomerScopeEntry(ScopedCustomerRecord? record, ActiveCustomerContractReservationRecord? reservation, string ownerKey)
    {
        _ = ownerKey;
        if (reservation != null)
        {
            return true;
        }

        if (record == null)
        {
            return false;
        }

        return record.IsUnlocked
            || !EqualityComparer<float>.Default.Equals(record.RelationshipDelta, 2f)
            || record.HasBeenRecommended
            || record.OfferedDeals != 0
            || record.CompletedDeliveries != 0
            || !string.IsNullOrWhiteSpace(BuildCustomerScopeDataJson(record));
    }

    private string BuildCustomerScopeDataJson(ScopedCustomerRecord? record)
    {
        if (record == null || string.IsNullOrWhiteSpace(record.CustomerDataJson))
        {
            return string.Empty;
        }

        if (!TryReadCustomerData(record.CustomerDataJson, out PersistenceCustomerData? data)
            || data == null
            || !data.IsContractOffered
            || data.OfferedContract == null)
        {
            return string.Empty;
        }

        return record.CustomerDataJson;
    }

    private void ApplyScopedCustomerState(Customer customer, ScopedCustomerRecord record)
    {
        if (!string.IsNullOrWhiteSpace(record.CustomerDataJson)
            && TryReadCustomerData(record.CustomerDataJson, out PersistenceCustomerData? data)
            && data != null)
        {
            customer.Load(data);
            if (!data.IsContractOffered || data.OfferedContract == null)
            {
                OfferedContractInfoField?.SetValue(customer, null);
            }
        }

        customer.NPC.RelationData.SetRelationship(record.RelationshipDelta, network: false);
        ApplyRelationshipUnlockState(customer, record.IsUnlocked);
        customer.SetPotentialCustomerPoIEnabled(!record.IsUnlocked && customer.IsUnlockable());
    }

    private static void ApplyRelationshipUnlockState(Customer customer, bool isUnlocked)
    {
        if (isUnlocked)
        {
            customer.NPC.RelationData.Unlock(NPCRelationData.EUnlockType.DirectApproach, notify: false);
            RelationUnlockTypeBackingField?.SetValue(customer.NPC.RelationData, NPCRelationData.EUnlockType.DirectApproach);
            if (!Customer.UnlockedCustomers.Contains(customer))
            {
                Customer.UnlockedCustomers.Add(customer);
            }

            Customer.LockedCustomers.Remove(customer);
            return;
        }

        RelationUnlockedBackingField?.SetValue(customer.NPC.RelationData, false);
        Customer.UnlockedCustomers.Remove(customer);
        if (!Customer.LockedCustomers.Contains(customer))
        {
            Customer.LockedCustomers.Add(customer);
        }
    }

    private static string BuildCustomerDataJson(Customer customer, bool includeOffer)
    {
        PersistenceCustomerData data = customer.GetCustomerData();
        if (!includeOffer)
        {
            data.IsContractOffered = false;
            data.OfferedContract = null;
        }

        return data.GetJson(prettyPrint: false);
    }

    private bool TryReadCustomerData(string customerDataJson, out PersistenceCustomerData? data)
    {
        try
        {
            data = JsonConvert.DeserializeObject<PersistenceCustomerData>(customerDataJson);
            return data != null;
        }
        catch (JsonException ex)
        {
            data = null;
            _logger.Warning($"Failed to read scoped customer data: {ex.Message}");
            return false;
        }
    }

    private static string BuildScopedCustomerKey(string ownerKey, string customerGuid)
    {
        return OrganisationScopeRules.BuildScopedCustomerKey(ownerKey, customerGuid);
    }

    private static string BuildScopedNpcKey(string ownerKey, string npcId)
    {
        return OrganisationScopeRules.BuildScopedNpcKey(ownerKey, npcId);
    }
}
#endif
