#if CLIENT
using System;
using System.Collections.Generic;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Utils;
using HarmonyLib;
using Newtonsoft.Json;
#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Relation;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Guid = Il2CppSystem.Guid;
using PersistenceCustomerData = Il2CppScheduleOne.Persistence.Datas.CustomerData;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.Economy;
using ScheduleOne.NPCs;
using ScheduleOne.NPCs.Relation;
using ScheduleOne.UI.Phone.Delivery;
using PersistenceCustomerData = ScheduleOne.Persistence.Datas.CustomerData;
#endif

namespace DedicatedServerMod.Organisations.Client.Services;

internal sealed class OrganisationCustomerScopeClientService
{
    private static readonly System.Reflection.FieldInfo? UnlockedBackingField = AccessTools.Field(typeof(NPCRelationData), "<Unlocked>k__BackingField");
    private static readonly System.Reflection.FieldInfo? RelationDeltaBackingField = AccessTools.Field(typeof(NPCRelationData), "<RelationDelta>k__BackingField");
    private static readonly System.Reflection.FieldInfo? DealerRecommendedBackingField = AccessTools.Field(typeof(Dealer), "<HasBeenRecommended>k__BackingField");
    private static readonly System.Reflection.FieldInfo? DealerRecruitedBackingField = AccessTools.Field(typeof(Dealer), "<IsRecruited>k__BackingField");
    private static readonly System.Reflection.FieldInfo? DealerCashBackingField = AccessTools.Field(typeof(Dealer), "<Cash>k__BackingField");
    private static readonly System.Reflection.FieldInfo? SupplierDeliveriesEnabledBackingField = AccessTools.Field(typeof(Supplier), "<DeliveriesEnabled>k__BackingField");
    private static readonly System.Reflection.FieldInfo? SupplierMinsUntilDeaddropReadyBackingField = AccessTools.Field(typeof(Supplier), "<minsUntilDeaddropReady>k__BackingField");
    private static readonly System.Reflection.FieldInfo? DeliveryShopAvailableBackingField = AccessTools.Field(typeof(DeliveryShop), "<IsAvailable>k__BackingField");
    private static readonly System.Reflection.MethodInfo? DeliveryShopSetIsAvailableMethod = AccessTools.Method(typeof(DeliveryShop), "SetIsAvailable");

    private readonly Dictionary<string, CustomerScopeEntryDto> _customersByNpcGuid = new Dictionary<string, CustomerScopeEntryDto>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DealerScopeEntryDto> _dealersByNpcId = new Dictionary<string, DealerScopeEntryDto>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SupplierScopeEntryDto> _suppliersByNpcId = new Dictionary<string, SupplierScopeEntryDto>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, CustomerScopeSyncChunkDto> _pendingChunksBySequence = new Dictionary<int, CustomerScopeSyncChunkDto>();
    private string _pendingChunkOwnerKey = string.Empty;
    private int _pendingChunkTotal;
    private bool _pendingApply;

    private static bool IsEmptyGuid(Guid guid)
    {
        return guid.Equals(Guid.Empty);
    }

    public void Replace(CustomerScopeSyncDto sync)
    {
        _customersByNpcGuid.Clear();
        if (sync?.Customers != null)
        {
            foreach (CustomerScopeEntryDto entry in sync.Customers)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.NpcGuid))
                {
                    continue;
                }

                _customersByNpcGuid[entry.NpcGuid] = entry;
            }
        }

        _dealersByNpcId.Clear();
        if (sync?.Dealers != null)
        {
            foreach (DealerScopeEntryDto entry in sync.Dealers)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.NpcId))
                {
                    continue;
                }

                _dealersByNpcId[entry.NpcId] = entry;
            }
        }

        _suppliersByNpcId.Clear();
        if (sync?.Suppliers != null)
        {
            foreach (SupplierScopeEntryDto entry in sync.Suppliers)
            {
                if (entry == null || string.IsNullOrWhiteSpace(entry.NpcId))
                {
                    continue;
                }

                _suppliersByNpcId[entry.NpcId] = entry;
            }
        }

        _pendingApply = true;
    }

    public void AddChunk(CustomerScopeSyncChunkDto chunk)
    {
        if (chunk == null || chunk.Total <= 0 || chunk.Sequence < 0 || chunk.Sequence >= chunk.Total)
        {
            return;
        }

        if (_pendingChunkTotal != chunk.Total
            || !string.Equals(_pendingChunkOwnerKey, chunk.OwnerKey ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || chunk.Sequence == 0)
        {
            _pendingChunksBySequence.Clear();
            _pendingChunkOwnerKey = chunk.OwnerKey ?? string.Empty;
            _pendingChunkTotal = chunk.Total;
        }

        _pendingChunksBySequence[chunk.Sequence] = chunk;
        if (_pendingChunksBySequence.Count < _pendingChunkTotal)
        {
            return;
        }

        CustomerScopeSyncDto sync = new CustomerScopeSyncDto
        {
            OwnerKey = _pendingChunkOwnerKey,
        };

        for (int sequence = 0; sequence < _pendingChunkTotal; sequence++)
        {
            if (!_pendingChunksBySequence.TryGetValue(sequence, out CustomerScopeSyncChunkDto? pendingChunk))
            {
                return;
            }

            sync.Customers.AddRange(pendingChunk.Customers);
            sync.Dealers.AddRange(pendingChunk.Dealers);
            sync.Suppliers.AddRange(pendingChunk.Suppliers);
        }

        _pendingChunksBySequence.Clear();
        _pendingChunkOwnerKey = string.Empty;
        _pendingChunkTotal = 0;
        Replace(sync);
    }

    public void Tick()
    {
        if (!_pendingApply || NPCManager.NPCRegistry.Count == 0)
        {
            return;
        }

        foreach (NPC npc in NPCManager.NPCRegistry)
        {
            if (npc is Dealer dealer && !string.IsNullOrWhiteSpace(dealer.ID) && _dealersByNpcId.TryGetValue(dealer.ID, out DealerScopeEntryDto? dealerEntry))
            {
                ApplyDealerScope(dealer, dealerEntry);
            }

            if (npc is Supplier supplier && !string.IsNullOrWhiteSpace(supplier.ID) && _suppliersByNpcId.TryGetValue(supplier.ID, out SupplierScopeEntryDto? supplierEntry))
            {
                ApplySupplierScope(supplier, supplierEntry);
            }

            Customer? customer = npc?.GetComponent<Customer>();
            if (customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
            {
                continue;
            }

            if (!_customersByNpcGuid.TryGetValue(customer.NPC.GUID.ToString(), out CustomerScopeEntryDto? entry))
            {
                continue;
            }

            ApplyCustomerScope(customer, entry);
        }

        _pendingApply = false;
    }

    public void Clear()
    {
        _customersByNpcGuid.Clear();
        _dealersByNpcId.Clear();
        _suppliersByNpcId.Clear();
        _pendingChunksBySequence.Clear();
        _pendingChunkOwnerKey = string.Empty;
        _pendingChunkTotal = 0;
        _pendingApply = false;
    }

    public bool TryGet(string npcGuid, out CustomerScopeEntryDto entry)
    {
        if (string.IsNullOrWhiteSpace(npcGuid))
        {
            entry = null!;
            return false;
        }

        return _customersByNpcGuid.TryGetValue(npcGuid, out entry!);
    }

    public bool TryGetDealer(string npcId, out DealerScopeEntryDto entry)
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            entry = null!;
            return false;
        }

        return _dealersByNpcId.TryGetValue(npcId, out entry!);
    }

    public bool TryGetSupplier(string npcId, out SupplierScopeEntryDto entry)
    {
        if (string.IsNullOrWhiteSpace(npcId))
        {
            entry = null!;
            return false;
        }

        return _suppliersByNpcId.TryGetValue(npcId, out entry!);
    }

    private static void ApplyCustomerScope(Customer customer, CustomerScopeEntryDto entry)
    {
        if (!string.IsNullOrWhiteSpace(entry.CustomerDataJson))
        {
            try
            {
                PersistenceCustomerData? data = JsonConvert.DeserializeObject<PersistenceCustomerData>(entry.CustomerDataJson);
                if (data != null)
                {
                    customer.Load(data);
                }
            }
            catch
            {
            }
        }

        customer.NPC.RelationData.SetRelationship(entry.RelationshipDelta, network: false);

        if (entry.IsUnlocked)
        {
            customer.NPC.RelationData.Unlock(NPCRelationData.EUnlockType.DirectApproach, notify: false);
            if (!Customer.UnlockedCustomers.Contains(customer))
            {
                Customer.UnlockedCustomers.Add(customer);
            }

            Customer.LockedCustomers.Remove(customer);
        }
        else
        {
            UnlockedBackingField?.SetValue(customer.NPC.RelationData, false);
            Customer.UnlockedCustomers.Remove(customer);
            if (!Customer.LockedCustomers.Contains(customer))
            {
                Customer.LockedCustomers.Add(customer);
            }
        }

        customer.SetPotentialCustomerPoIEnabled(!entry.IsUnlocked && customer.IsUnlockable());
    }

    private static void ApplyDealerScope(Dealer dealer, DealerScopeEntryDto entry)
    {
        DealerRecommendedBackingField?.SetValue(dealer, entry.HasBeenRecommended);
        DealerRecruitedBackingField?.SetValue(dealer, entry.IsRecruited);
        DealerCashBackingField?.SetValue(dealer, entry.Cash);

        foreach (Customer assignedCustomer in dealer.AssignedCustomers.AsManagedEnumerable().ToList())
        {
            assignedCustomer.AssignDealer(null);
        }

        dealer.AssignedCustomers.Clear();
        foreach (string npcId in entry.AssignedCustomerNpcIds)
        {
            if (string.IsNullOrWhiteSpace(npcId))
            {
                continue;
            }

            NPC npc = NPCManager.GetNPC(npcId);
            Customer? customer = npc?.GetComponent<Customer>();
            if (customer == null)
            {
                continue;
            }

            dealer.AssignedCustomers.Add(customer);
            customer.AssignDealer(dealer);
        }
    }

    private static void ApplySupplierScope(Supplier supplier, SupplierScopeEntryDto entry)
    {
        RelationDeltaBackingField?.SetValue(supplier.RelationData, entry.RelationshipDelta);
        if (entry.IsUnlocked)
        {
            supplier.RelationData.Unlock(NPCRelationData.EUnlockType.Recommendation, notify: false);
        }
        else
        {
            UnlockedBackingField?.SetValue(supplier.RelationData, false);
        }

        SupplierDeliveriesEnabledBackingField?.SetValue(supplier, entry.DeliveriesEnabled);
        SupplierMinsUntilDeaddropReadyBackingField?.SetValue(supplier, entry.MinsUntilDeadDropReady);
        supplier.sync___set_value_debt(entry.Debt, asServer: false);
        supplier.sync___set_value_deadDropPreparing(entry.DeadDropPreparing, asServer: false);
        ApplySupplierStashAvailability(supplier, entry.IsUnlocked);
        ApplySupplierDeliveryShopAvailability(supplier, entry.DeliveriesEnabled);
    }

    private static void ApplySupplierStashAvailability(Supplier supplier, bool isAvailable)
    {
        SupplierStash? stash = supplier.Stash;
        if (stash == null)
        {
            return;
        }

        if (stash.IntObj != null)
        {
            stash.IntObj.enabled = isAvailable;
        }

        if (stash.StashPoI != null)
        {
            stash.StashPoI.enabled = isAvailable;
        }
    }

    private static void ApplySupplierDeliveryShopAvailability(Supplier supplier, bool isAvailable)
    {
        if (supplier.Shop == null || !PlayerSingleton<DeliveryApp>.InstanceExists)
        {
            return;
        }

        DeliveryShop deliveryShop = PlayerSingleton<DeliveryApp>.Instance.GetShop(supplier.Shop.ShopName);
        if (deliveryShop == null)
        {
            return;
        }

        if (isAvailable)
        {
            DeliveryShopSetIsAvailableMethod?.Invoke(deliveryShop, Array.Empty<object>());
            return;
        }

        DeliveryShopAvailableBackingField?.SetValue(deliveryShop, false);
        deliveryShop.gameObject.SetActive(false);
    }
}
#endif
