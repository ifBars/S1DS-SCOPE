#if CLIENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Services;
using DedicatedServerMod.Organisations.Utils;
using HarmonyLib;
using Newtonsoft.Json;
#if IL2CPP
using Il2CppFishNet;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Il2CppScheduleOne.Variables;
using ECartelStatus = Il2Cpp.ECartelStatus;
using GUIDManager = Il2Cpp.GUIDManager;
using Guid = Il2CppSystem.Guid;
using QuestEntryData = Il2CppScheduleOne.Persistence.Datas.QuestEntryData;
#else
using FishNet;
using FishNet.Object;
using ScheduleOne.Cartel;
using ScheduleOne.DevUtilities;
using ScheduleOne.Doors;
using ScheduleOne.Economy;
using ScheduleOne.GameTime;
using ScheduleOne.Graffiti;
using ScheduleOne.Map;
using ScheduleOne.NPCs;
using ScheduleOne.NPCs.CharacterClasses;
using ScheduleOne.Product;
using ScheduleOne.Quests;
using ScheduleOne.UI.Phone.Delivery;
using ScheduleOne.Variables;
using QuestEntryData = ScheduleOne.Persistence.Datas.QuestEntryData;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Client.Services;

internal sealed class OrganisationQuestScopeClientService
{
    private static readonly System.Reflection.MethodInfo? CartelInitializeDealQuestMethod =
        AccessTools.Method(typeof(CartelDealManager), "RpcLogic___InitializeDealQuest_2137933519");
    private static readonly MethodInfo? CartelStartGlobalActivityLogicMethod =
        AccessTools.Method(typeof(CartelActivities), "RpcLogic___StartGlobalActivity_1796582335");
    private static readonly MethodInfo? CartelStartRegionalActivityLogicMethod =
        AccessTools.Method(typeof(CartelRegionActivities), "RpcLogic___StartActivity_2681120339");
    private static readonly MethodInfo? CartelActivityDeactivateMethod =
        AccessTools.Method(typeof(CartelActivity), "Deactivate");
    private static readonly FieldInfo? MapRegionIsUnlockedField =
        AccessTools.Field(typeof(MapRegionData), "<IsUnlocked>k__BackingField");
    private static readonly FieldInfo? CartelActivitiesCurrentGlobalActivityField =
        AccessTools.Field(typeof(CartelActivities), "<CurrentGlobalActivity>k__BackingField");
    private static readonly FieldInfo? CartelActivitiesHoursUntilNextGlobalActivityField =
        AccessTools.Field(typeof(CartelActivities), "<HoursUntilNextGlobalActivity>k__BackingField");
    private static readonly FieldInfo? CartelRegionActivitiesCurrentActivityField =
        AccessTools.Field(typeof(CartelRegionActivities), "<CurrentActivity>k__BackingField");
    private static readonly FieldInfo? CartelRegionActivitiesHoursUntilNextActivityField =
        AccessTools.Field(typeof(CartelRegionActivities), "<HoursUntilNextActivity>k__BackingField");
    private static readonly FieldInfo? DarkMarketUnlockedField =
        AccessTools.Field(typeof(DarkMarket), "<Unlocked>k__BackingField");
    private static readonly MethodInfo? SewerManagerSetSewerUnlockedClientLogicMethod =
        AccessTools.Method(typeof(SewerManager), "RpcLogic___SetSewerUnlocked_Client_328543758");
    private static readonly FieldInfo? SewerManagerIsSewerUnlockedField =
        AccessTools.Field(typeof(SewerManager), "<IsSewerUnlocked>k__BackingField");

    private readonly OrganisationLogger _logger;
    private readonly bool _skipDefaultQuestApply;
    private readonly bool _skipContractApply;
    private readonly bool _skipDeaddropApply;
    private readonly bool _skipCartelApply;
    private readonly bool _skipMapAndUnlockApply;
    private readonly bool _skipProductMarketApply;
    private readonly bool _skipDeaddropAffordanceApply;
    private readonly bool _skipChunkReassembly;
    private readonly bool _skipQueueAfterChunkReassembly;
    private readonly bool _skipApplyMutations;
    private readonly string? _onlyApplyStage;
    private QuestScopeSyncDto? _pendingScope;
    private readonly Dictionary<int, QuestScopeSyncChunkDto> _pendingChunksBySequence = new Dictionary<int, QuestScopeSyncChunkDto>();
    private readonly HashSet<string> _activeDeaddropGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _inaccessibleDeaddropGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private string? _pendingScopeSignature;
    private string _pendingChunkOwnerKey = string.Empty;
    private int _pendingChunkTotal;
    private string? _activeOwnerKey;
    private string? _activeCartelStatus;
    private string? _lastAppliedScopeSignature;
    private bool _loanSharksArrived;
    private float _hoursSinceLoanSharksArrived;

    public OrganisationQuestScopeClientService(OrganisationLogger logger)
    {
        _logger = logger;
        _skipDefaultQuestApply = HasCommandLineArg("--org-diag-skip-default-quests");
        _skipContractApply = HasCommandLineArg("--org-diag-skip-contracts");
        _skipDeaddropApply = HasCommandLineArg("--org-diag-skip-deaddrops");
        _skipCartelApply = HasCommandLineArg("--org-diag-skip-cartel");
        _skipMapAndUnlockApply = HasCommandLineArg("--org-diag-skip-map-unlocks");
        _skipProductMarketApply = HasCommandLineArg("--org-diag-skip-product-market");
        _skipDeaddropAffordanceApply = HasCommandLineArg("--org-diag-skip-deaddrop-affordances");
        _skipChunkReassembly = HasCommandLineArg("--org-diag-skip-quest-chunk-reassembly");
        _skipQueueAfterChunkReassembly = HasCommandLineArg("--org-diag-skip-quest-queue-after-reassembly");
        _skipApplyMutations = HasCommandLineArg("--org-diag-skip-quest-apply-mutations");
        _onlyApplyStage = GetCommandLineArgValue("--org-diag-only-quest-apply-stage");

        if (_skipDefaultQuestApply || _skipContractApply || _skipDeaddropApply || _skipCartelApply || _skipMapAndUnlockApply || _skipProductMarketApply || _skipDeaddropAffordanceApply || _skipChunkReassembly || _skipQueueAfterChunkReassembly || _skipApplyMutations || !string.IsNullOrWhiteSpace(_onlyApplyStage))
        {
            _logger.Warning($"Quest scope diagnostics active. skipDefaultQuests={_skipDefaultQuestApply}, skipContracts={_skipContractApply}, skipDeaddrops={_skipDeaddropApply}, skipCartel={_skipCartelApply}, skipMapUnlocks={_skipMapAndUnlockApply}, skipProductMarket={_skipProductMarketApply}, skipDeaddropAffordances={_skipDeaddropAffordanceApply}, skipChunkReassembly={_skipChunkReassembly}, skipQueueAfterChunkReassembly={_skipQueueAfterChunkReassembly}, skipApplyMutations={_skipApplyMutations}, onlyApplyStage={_onlyApplyStage ?? "none"}.");
        }
    }

    public bool IsApplyingScope { get; private set; }

    public bool CanReceiveCartelDealQuest => string.Equals(_activeCartelStatus, ECartelStatus.Truced.ToString(), StringComparison.OrdinalIgnoreCase);

    public bool CanApplyCartelGraffitiSurfaceRpc(NetworkObject surfaceObject)
    {
        if (IsApplyingScope || surfaceObject == null)
        {
            return true;
        }

        WorldSpraySurface? surface = surfaceObject.GetComponent<WorldSpraySurface>();
        return surface == null || HasActiveSprayGraffitiActivity(surface.Region);
    }

    public bool ShouldRunLoanSharkKidnap(Quest_TheDeepEnd quest)
    {
        return _loanSharksArrived
            && _hoursSinceLoanSharksArrived >= Quest_TheDeepEnd.KIDNAP_TIME
            && quest != null
            && quest.Entries.Count > 0
            && quest.Entries[0].State == EQuestState.Active;
    }

    public bool CanAccessDeaddrop(string deaddropGuid)
    {
        if (string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return true;
        }

        if (_inaccessibleDeaddropGuids.Contains(deaddropGuid))
        {
            return false;
        }

        return _activeDeaddropGuids.Count == 0 || _activeDeaddropGuids.Contains(deaddropGuid);
    }

    public void RefreshDeaddropAffordance(DeadDrop drop)
    {
        if (drop == null)
        {
            return;
        }

        if (drop.PoI != null)
        {
            drop.PoI.enabled = false;
        }

        if (drop.Light == null)
        {
            return;
        }

        string dropGuid = drop.GUID.ToString();
        bool hasVisibleContents = drop.Storage != null && drop.Storage.ItemCount > 0;
        drop.Light.Enabled = hasVisibleContents && CanAccessDeaddrop(dropGuid);
    }

    public void QueueScopeSync(QuestScopeSyncDto scope)
    {
        string signature = JsonConvert.SerializeObject(scope);
        if (string.Equals(signature, _pendingScopeSignature, StringComparison.Ordinal)
            || string.Equals(signature, _lastAppliedScopeSignature, StringComparison.Ordinal))
        {
            _logger.Info($"[QuestScopeDiag] Ignored duplicate quest scope sync. OwnerKey={scope.OwnerKey}, SignatureChars={signature.Length}, Quests={scope.Quests.Count}, Contracts={scope.Contracts.Count}, Deaddrops={scope.Deaddrops.Count}.");
            return;
        }

        _pendingScope = scope;
        _pendingScopeSignature = signature;
        _logger.Info($"[QuestScopeDiag] Queued quest scope sync for apply. OwnerKey={scope.OwnerKey}, SignatureChars={signature.Length}, Quests={scope.Quests.Count}, Contracts={scope.Contracts.Count}, Deaddrops={scope.Deaddrops.Count}.");
    }

    public void AddChunk(QuestScopeSyncChunkDto chunk)
    {
        if (chunk == null || chunk.Total <= 0 || chunk.Sequence < 0 || chunk.Sequence >= chunk.Total || chunk.Scope == null)
        {
            _logger.Warning("[QuestScopeDiag] Ignored invalid quest scope chunk.");
            return;
        }

        if (_pendingChunkTotal != chunk.Total
            || !string.Equals(_pendingChunkOwnerKey, chunk.OwnerKey ?? string.Empty, StringComparison.OrdinalIgnoreCase)
            || chunk.Sequence == 0)
        {
            _pendingChunksBySequence.Clear();
            _pendingChunkOwnerKey = chunk.OwnerKey ?? string.Empty;
            _pendingChunkTotal = chunk.Total;
            _logger.Info($"[QuestScopeDiag] Started quest scope chunk set. OwnerKey={_pendingChunkOwnerKey}, Total={_pendingChunkTotal}.");
        }

        _pendingChunksBySequence[chunk.Sequence] = chunk;
        _logger.Info($"[QuestScopeDiag] Stored quest scope chunk {chunk.Sequence + 1}/{chunk.Total}. OwnerKey={chunk.OwnerKey}, Received={_pendingChunksBySequence.Count}/{_pendingChunkTotal}, Quests={chunk.Scope.Quests.Count}, Contracts={chunk.Scope.Contracts.Count}, Deaddrops={chunk.Scope.Deaddrops.Count}.");
        if (_pendingChunksBySequence.Count < _pendingChunkTotal)
        {
            return;
        }

        if (_skipChunkReassembly)
        {
            _logger.Warning($"[QuestScopeDiag] Quest scope chunk reassembly diagnostics active. Dropping complete chunk set before reassembly. OwnerKey={_pendingChunkOwnerKey}, Received={_pendingChunksBySequence.Count}/{_pendingChunkTotal}.");
            _pendingChunksBySequence.Clear();
            _pendingChunkOwnerKey = string.Empty;
            _pendingChunkTotal = 0;
            return;
        }

        QuestScopeSyncDto? sync = null;
        for (int sequence = 0; sequence < _pendingChunkTotal; sequence++)
        {
            if (!_pendingChunksBySequence.TryGetValue(sequence, out QuestScopeSyncChunkDto? pendingChunk))
            {
                _logger.Warning($"[QuestScopeDiag] Quest scope chunk set is missing sequence {sequence + 1}/{_pendingChunkTotal}; waiting for more chunks.");
                return;
            }

            if (sync == null)
            {
                sync = pendingChunk.Scope;
                continue;
            }

            sync.Quests.AddRange(pendingChunk.Scope.Quests);
            sync.Contracts.AddRange(pendingChunk.Scope.Contracts);
            sync.Deaddrops.AddRange(pendingChunk.Scope.Deaddrops);
        }

        _pendingChunksBySequence.Clear();
        _pendingChunkOwnerKey = string.Empty;
        _pendingChunkTotal = 0;
        if (sync != null)
        {
            _logger.Info($"[QuestScopeDiag] Reassembled quest scope chunks. OwnerKey={sync.OwnerKey}, Quests={sync.Quests.Count}, Contracts={sync.Contracts.Count}, Deaddrops={sync.Deaddrops.Count}.");
            if (_skipQueueAfterChunkReassembly)
            {
                _logger.Warning($"[QuestScopeDiag] Quest scope queue diagnostics active. Dropping reassembled scope before queue/apply. OwnerKey={sync.OwnerKey}, Quests={sync.Quests.Count}, Contracts={sync.Contracts.Count}, Deaddrops={sync.Deaddrops.Count}.");
                return;
            }

            QueueScopeSync(sync);
        }
    }

    public void Tick()
    {
        if (_pendingScope == null)
        {
            return;
        }

        QuestManager? questManager = NetworkSingleton<QuestManager>.Instance;
        if (questManager == null)
        {
            return;
        }

        ApplyScope(questManager, _pendingScope);
        _lastAppliedScopeSignature = _pendingScopeSignature;
        _pendingScope = null;
        _pendingScopeSignature = null;
    }

    public void ClearPending()
    {
        _pendingScope = null;
        _pendingScopeSignature = null;
        _pendingChunksBySequence.Clear();
        _pendingChunkOwnerKey = string.Empty;
        _pendingChunkTotal = 0;
        _activeOwnerKey = null;
        _activeCartelStatus = null;
        _lastAppliedScopeSignature = null;
        _loanSharksArrived = false;
        _hoursSinceLoanSharksArrived = 0f;
        _activeDeaddropGuids.Clear();
        _inaccessibleDeaddropGuids.Clear();
    }

    private void ApplyScope(QuestManager questManager, QuestScopeSyncDto scope)
    {
        bool isScopeSwitch = !string.Equals(_activeOwnerKey, scope.OwnerKey, StringComparison.OrdinalIgnoreCase);

        IsApplyingScope = true;
        try
        {
            _logger.Info($"[QuestScopeDiag] Begin applying quest scope. OwnerKey={scope.OwnerKey}, IsScopeSwitch={isScopeSwitch}, Quests={scope.Quests.Count}, Contracts={scope.Contracts.Count}, Deaddrops={scope.Deaddrops.Count}.");
            if (ShouldApplyStage("ResetContracts", _skipContractApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: ResetContracts.");
                ResetContracts();
            }

            if (ShouldApplyStage("ResetDeaddrops", _skipDeaddropApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: ResetDeaddrops.");
                ResetDeaddrops();
            }

            if (ShouldApplyStage("DefaultQuests", _skipDefaultQuestApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: DefaultQuests.");
                ApplyDefaultQuests(questManager, scope.Quests, isScopeSwitch);
            }

            if (ShouldApplyStage("Contracts", _skipContractApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: Contracts.");
                ApplyContracts(questManager, scope.Contracts);
            }

            if (ShouldApplyStage("Deaddrops", _skipDeaddropApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: Deaddrops.");
                ApplyDeaddrops(questManager, scope.Deaddrops);
            }

            if (ShouldApplyStage("Cartel", _skipCartelApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: Cartel.");
                _activeCartelStatus = ApplyCartelStatus(scope.CartelStatus);
                ApplyCartelInfluence(scope.CartelInfluenceByRegion);
                ApplyCartelDeal(scope.CartelDealDataJson);
                ApplyCartelActivityState(scope.CartelActivityState);
            }

            if (ShouldApplyStage("MapAndUnlocks", _skipMapAndUnlockApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: MapAndUnlocks.");
                _logger.Info("[QuestScopeDiag] Applying stage: MapRegions.");
                ApplyMapRegionState(scope.MapRegionUnlockedByRegion);
                _logger.Info("[QuestScopeDiag] Applying stage: DarkMarket.");
                ApplyDarkMarketState(scope.DarkMarketUnlocked);
                _logger.Info("[QuestScopeDiag] Applying stage: Sewer.");
                ApplySewerState(scope.SewerUnlocked);
            }
            else
            {
                if (ShouldApplyStage("MapRegions", _skipMapAndUnlockApply))
                {
                    _logger.Info("[QuestScopeDiag] Applying stage: MapRegions.");
                    ApplyMapRegionState(scope.MapRegionUnlockedByRegion);
                }

                if (ShouldApplyStage("DarkMarket", _skipMapAndUnlockApply))
                {
                    _logger.Info("[QuestScopeDiag] Applying stage: DarkMarket.");
                    ApplyDarkMarketState(scope.DarkMarketUnlocked);
                }

                if (ShouldApplyStage("Sewer", _skipMapAndUnlockApply))
                {
                    _logger.Info("[QuestScopeDiag] Applying stage: Sewer.");
                    ApplySewerState(scope.SewerUnlocked);
                }
            }

            if (ShouldApplyStage("ProductMarket", _skipProductMarketApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: ProductMarket.");
                ProductMarketScopeApplier.Apply(scope.ProductMarketState);
            }

            if (ShouldApplyStage("Bookkeeping", skipFlag: false))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: Bookkeeping.");
                _loanSharksArrived = scope.LoanSharksArrived;
                _hoursSinceLoanSharksArrived = scope.HoursSinceLoanSharksArrived;
            }

            if (ShouldApplyStage("DeaddropAffordances", _skipDeaddropAffordanceApply))
            {
                _logger.Info("[QuestScopeDiag] Applying stage: DeaddropAffordances.");
                ReplaceActiveDeaddrops(scope.Deaddrops);
                ReplaceInaccessibleDeaddrops(scope.InaccessibleDeaddropGuids);
                RefreshDeaddropAffordances();
            }

            if (ShouldApplyStage("Bookkeeping", skipFlag: false))
            {
                _activeOwnerKey = scope.OwnerKey;
            }

            _logger.Info($"Applied scoped quest sync for {scope.OwnerKey}. Quests={scope.Quests.Count}, Contracts={scope.Contracts.Count}, Deaddrops={scope.Deaddrops.Count}.");
            _logger.Info($"[QuestScopeDiag] Finished applying quest scope. OwnerKey={scope.OwnerKey}.");
        }
        finally
        {
            IsApplyingScope = false;
        }
    }

    private static bool HasCommandLineArg(string arg)
    {
        foreach (string candidate in Environment.GetCommandLineArgs())
        {
            if (string.Equals(candidate, arg, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private bool ShouldApplyStage(string stageName, bool skipFlag)
    {
        if (_skipApplyMutations)
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(_onlyApplyStage))
        {
            return string.Equals(_onlyApplyStage, stageName, StringComparison.OrdinalIgnoreCase);
        }

        return !skipFlag;
    }

    private static string? GetCommandLineArgValue(string arg)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length; index++)
        {
            string candidate = args[index];
            if (candidate.StartsWith(arg + "=", StringComparison.OrdinalIgnoreCase))
            {
                return candidate.Substring(arg.Length + 1);
            }

            if (string.Equals(candidate, arg, StringComparison.OrdinalIgnoreCase) && index + 1 < args.Length)
            {
                return args[index + 1];
            }
        }

        return null;
    }

    private static void ResetDefaultQuests(QuestManager questManager)
    {
        foreach (Quest quest in questManager.DefaultQuests)
        {
            if (!QuestScopeRules.ShouldVirtualizeQuest(quest))
            {
                continue;
            }

            quest.SetIsTracked(false);
            quest.SetQuestState(EQuestState.Inactive, network: false);
            for (int i = 0; i < quest.Entries.Count; i++)
            {
                quest.SetQuestEntryState(i, EQuestState.Inactive, network: false);
            }
        }
    }

    private static void ApplyDefaultQuests(QuestManager questManager, System.Collections.Generic.List<ScopedQuestSyncDto> quests, bool forceReset)
    {
        if (forceReset)
        {
            ResetDefaultQuests(questManager);
        }

        foreach (ScopedQuestSyncDto questData in quests)
        {
            if (questData == null || string.IsNullOrWhiteSpace(questData.Guid))
            {
                continue;
            }

            Quest? quest = GUIDManager.GetObject<Quest>(new Guid(questData.Guid));
            if (!QuestScopeRules.ShouldVirtualizeQuest(quest))
            {
                continue;
            }

            ApplyQuestState(quest, questData, forceReset);
        }
    }

    private static void ApplyQuestState(Quest quest, ScopedQuestSyncDto questData, bool forceReset)
    {
        EQuestState targetQuestState = ParseQuestState(questData.State);
        if (forceReset || quest.State != targetQuestState)
        {
            quest.SetQuestState(targetQuestState, network: false);
        }

        for (int i = 0; i < questData.Entries.Count && i < quest.Entries.Count; i++)
        {
            ScopedQuestEntrySyncDto entryData = questData.Entries[i];
            if (!string.Equals(quest.Entries[i].Title, entryData.Name, StringComparison.Ordinal))
            {
                quest.Entries[i].SetEntryTitle(entryData.Name);
            }

            EQuestState targetEntryState = ParseQuestState(entryData.State);
            if (forceReset || quest.Entries[i].State != targetEntryState)
            {
                quest.SetQuestEntryState(i, targetEntryState, network: false);
            }
        }

        if (forceReset || quest.IsTracked != questData.IsTracked)
        {
            quest.SetIsTracked(questData.IsTracked);
        }

        if (forceReset
            || quest.Expires != questData.Expires
            || quest.Expiry.elapsedDays != questData.ExpiryElapsedDays
            || Math.Abs(quest.Expiry.time - questData.ExpiryTime) > 0.001f)
        {
            quest.ConfigureExpiry(
                questData.Expires,
                new GameDateTime
                {
                    elapsedDays = questData.ExpiryElapsedDays,
                    time = questData.ExpiryTime,
                });
        }
    }

    private static void ApplyContracts(QuestManager questManager, System.Collections.Generic.List<ScopedContractSyncDto> contracts)
    {
        foreach (ScopedContractSyncDto contractData in contracts)
        {
            if (contractData == null || string.IsNullOrWhiteSpace(contractData.CustomerGuid))
            {
                continue;
            }

            Customer? customer = GUIDManager.GetObject<NPC>(new Guid(contractData.CustomerGuid))?.GetComponent<Customer>();
            if (customer == null)
            {
                continue;
            }

            ProductList? productList = string.IsNullOrWhiteSpace(contractData.ProductListJson)
                ? null
                : JsonConvert.DeserializeObject<ProductList>(contractData.ProductListJson);
            if (productList == null)
            {
                continue;
            }

            QuestWindowConfig deliveryWindow = new QuestWindowConfig
            {
                IsEnabled = contractData.DeliveryWindowEnabled,
                WindowStartTime = contractData.DeliveryWindowStartTime,
                WindowEndTime = contractData.DeliveryWindowEndTime,
            };

            Contract contract = questManager.CreateContract_Local(
                contractData.Title,
                contractData.Description,
                contractData.Entries.Select(entry => new QuestEntryData(entry.Name, ParseQuestState(entry.State))).ToArray(),
                contractData.Guid,
                contractData.IsTracked,
                customer,
                contractData.Payment,
                productList,
                contractData.DeliveryLocationGuid,
                deliveryWindow,
                contractData.Expires,
                new GameDateTime { elapsedDays = contractData.ExpiryElapsedDays, time = contractData.ExpiryTime },
                contractData.PickupScheduleIndex,
                new GameDateTime { elapsedDays = contractData.AcceptElapsedDays, time = contractData.AcceptTime });

            contract.SetQuestState(ParseQuestState(contractData.State), network: false);
            for (int i = 0; i < contractData.Entries.Count && i < contract.Entries.Count; i++)
            {
                contract.Entries[i].SetEntryTitle(contractData.Entries[i].Name);
                contract.SetQuestEntryState(i, ParseQuestState(contractData.Entries[i].State), network: false);
            }

            contract.SetIsTracked(contractData.IsTracked);
        }
    }

    private static void ApplyDeaddrops(QuestManager questManager, System.Collections.Generic.List<ScopedDeaddropQuestSyncDto> deaddrops)
    {
        foreach (ScopedDeaddropQuestSyncDto questData in deaddrops)
        {
            if (questData == null || string.IsNullOrWhiteSpace(questData.DeaddropGuid) || GUIDManager.IsGUIDAlreadyRegistered(new Guid(questData.Guid)))
            {
                continue;
            }

            DeadDrop? drop = GUIDManager.GetObject<DeadDrop>(new Guid(questData.DeaddropGuid));
            if (drop == null)
            {
                continue;
            }

            DeaddropQuest quest = UnityEngine.Object.Instantiate(questManager.DeaddropCollectionPrefab.gameObject, questManager.QuestContainer).GetComponent<DeaddropQuest>();
            quest.SetDrop(drop);
            quest.Description = questData.Description;
            quest.SetGUID(new Guid(questData.Guid));
            if (questData.Entries.Count > 0)
            {
                quest.Entries[0].SetEntryTitle(questData.Entries[0].Name);
            }

            quest.Begin(network: false);
            quest.SetQuestState(ParseQuestState(questData.State), network: false);
            for (int i = 0; i < questData.Entries.Count && i < quest.Entries.Count; i++)
            {
                quest.SetQuestEntryState(i, ParseQuestState(questData.Entries[i].State), network: false);
            }

            quest.SetIsTracked(questData.IsTracked);
            quest.ConfigureExpiry(
                questData.Expires,
                new GameDateTime
                {
                    elapsedDays = questData.ExpiryElapsedDays,
                    time = questData.ExpiryTime,
                });
        }
    }

    private void ReplaceActiveDeaddrops(System.Collections.Generic.List<ScopedDeaddropQuestSyncDto> deaddrops)
    {
        _activeDeaddropGuids.Clear();
        foreach (ScopedDeaddropQuestSyncDto deaddrop in deaddrops)
        {
            if (deaddrop != null
                && !string.IsNullOrWhiteSpace(deaddrop.DeaddropGuid)
                && string.Equals(deaddrop.State, EQuestState.Active.ToString(), StringComparison.OrdinalIgnoreCase))
            {
                _activeDeaddropGuids.Add(deaddrop.DeaddropGuid);
            }
        }
    }

    private void ReplaceInaccessibleDeaddrops(System.Collections.Generic.List<string> deaddropGuids)
    {
        _inaccessibleDeaddropGuids.Clear();
        if (deaddropGuids == null)
        {
            return;
        }

        foreach (string deaddropGuid in deaddropGuids)
        {
            if (!string.IsNullOrWhiteSpace(deaddropGuid))
            {
                _inaccessibleDeaddropGuids.Add(deaddropGuid);
            }
        }
    }

    private void RefreshDeaddropAffordances()
    {
        foreach (DeadDrop drop in DeadDrop.DeadDrops)
        {
            RefreshDeaddropAffordance(drop);
        }
    }

    private static void ResetContracts()
    {
        foreach (Contract contract in Contract.Contracts.AsManagedEnumerable().ToList())
        {
            if (contract == null)
            {
                continue;
            }

            contract.End();
            UnityEngine.Object.Destroy(contract.gameObject);
        }
    }

    private static void ResetDeaddrops()
    {
        foreach (DeaddropQuest quest in DeaddropQuest.DeaddropQuests.AsManagedEnumerable().ToList())
        {
            if (quest == null)
            {
                continue;
            }

            quest.End();
            UnityEngine.Object.Destroy(quest.gameObject);
        }
    }

    private static string? ApplyCartelStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status) || !Enum.TryParse(status, out ECartelStatus cartelStatus))
        {
            return null;
        }

        Cartel? cartel = NetworkSingleton<Cartel>.Instance;
        if (cartel == null || cartel.Status == cartelStatus)
        {
            return cartelStatus.ToString();
        }

        cartel.RpcLogic___SetStatus_3666943613(null, cartelStatus, resetStatusChangeTimer: false);
        return cartelStatus.ToString();
    }

    private static void ApplyCartelInfluence(Dictionary<string, float> influenceByRegion)
    {
        if (influenceByRegion == null || influenceByRegion.Count == 0)
        {
            return;
        }

        CartelInfluence? influence = NetworkSingleton<Cartel>.Instance?.Influence;
        if (influence == null)
        {
            return;
        }

        foreach (KeyValuePair<string, float> pair in influenceByRegion)
        {
            if (!Enum.TryParse(pair.Key, ignoreCase: true, out EMapRegion region))
            {
                continue;
            }

            influence.RpcLogic___SetInfluence_2071772313(null, region, Mathf.Clamp01(pair.Value));
        }
    }

    private void ApplyCartelDeal(string dealDataJson)
    {
        if (!CanReceiveCartelDealQuest || string.IsNullOrWhiteSpace(dealDataJson))
        {
            return;
        }

        CartelDealInfo? dealInfo = JsonConvert.DeserializeObject<CartelDealInfo>(dealDataJson);
        CartelDealManager? dealManager = NetworkSingleton<Cartel>.Instance?.DealManager;
        if (dealInfo?.IsValid() != true || dealManager == null || CartelInitializeDealQuestMethod == null)
        {
            return;
        }

        CartelInitializeDealQuestMethod.Invoke(dealManager, new object?[] { null, dealInfo });
    }

    private static void ApplyMapRegionState(Dictionary<string, bool> unlockedByRegion)
    {
        if (unlockedByRegion == null || unlockedByRegion.Count == 0)
        {
            return;
        }

        Map? map = Singleton<Map>.Instance;
        if (map?.Regions == null)
        {
            return;
        }

        foreach (MapRegionData regionData in map.Regions)
        {
            if (regionData == null)
            {
                continue;
            }

            bool unlocked = regionData.UnlockedByDefault
                || (unlockedByRegion.TryGetValue(regionData.Region.ToString(), out bool scopedUnlocked) && scopedUnlocked);
            MapRegionIsUnlockedField?.SetValue(regionData, unlocked);
        }
    }

    private void ApplyCartelActivityState(CartelActivityScopeRecord activityState)
    {
        CartelActivities? activities = NetworkSingleton<Cartel>.Instance?.Activities;
        if (activities == null)
        {
            return;
        }

        if (!string.Equals(_activeCartelStatus, ECartelStatus.Hostile.ToString(), StringComparison.OrdinalIgnoreCase))
        {
            ClearCartelActivityState(activities);
            return;
        }

        ApplyGlobalCartelActivityState(activities, activityState);
        ApplyRegionalCartelActivityState(activities, activityState);
    }

    private static void ApplyGlobalCartelActivityState(CartelActivities activities, CartelActivityScopeRecord activityState)
    {
        int scopedCooldown = Math.Max(0, activityState.GlobalHoursUntilNextActivity);
        CartelActivitiesHoursUntilNextGlobalActivityField?.SetValue(activities, scopedCooldown);
        DeactivateGlobalCartelActivity(activities);

        if (activityState.GlobalActivityIndex < 0
            || activityState.GlobalActivityIndex >= activities.GlobalActivities.Count
            || !Enum.TryParse(activityState.GlobalActivityRegion, ignoreCase: true, out EMapRegion region)
            || CartelStartGlobalActivityLogicMethod == null)
        {
            return;
        }

        CartelStartGlobalActivityLogicMethod.Invoke(activities, new object?[] { null, region, activityState.GlobalActivityIndex });
        CartelActivitiesHoursUntilNextGlobalActivityField?.SetValue(activities, scopedCooldown);
    }

    private static void ApplyRegionalCartelActivityState(CartelActivities activities, CartelActivityScopeRecord activityState)
    {
        if (activities.RegionalActivities == null)
        {
            return;
        }

        foreach (CartelRegionActivities regionActivities in activities.RegionalActivities)
        {
            if (regionActivities == null)
            {
                continue;
            }

            ApplyRegionalCartelActivityState(regionActivities, activityState);
        }
    }

    private static void ApplyRegionalCartelActivityState(CartelRegionActivities regionActivities, CartelActivityScopeRecord activityState)
    {
        string regionKey = regionActivities.Region.ToString();
        if (activityState.RegionalActivitiesByRegion == null
            || !activityState.RegionalActivitiesByRegion.TryGetValue(regionKey, out RegionalCartelActivityScopeRecord? regionalState))
        {
            DeactivateRegionalCartelActivity(regionActivities);
            return;
        }

        CartelRegionActivitiesHoursUntilNextActivityField?.SetValue(regionActivities, Math.Max(0, regionalState.HoursUntilNextActivity));
        DeactivateRegionalCartelActivity(regionActivities);

        if (regionalState.ActivityIndex < 0
            || regionalState.ActivityIndex >= regionActivities.Activities.Count
            || CartelStartRegionalActivityLogicMethod == null)
        {
            return;
        }

        CartelStartRegionalActivityLogicMethod.Invoke(regionActivities, new object?[] { null, regionalState.ActivityIndex });
    }

    private static void ClearCartelActivityState(CartelActivities activities)
    {
        DeactivateGlobalCartelActivity(activities);

        if (activities.RegionalActivities == null)
        {
            return;
        }

        foreach (CartelRegionActivities regionActivities in activities.RegionalActivities)
        {
            if (regionActivities != null)
            {
                DeactivateRegionalCartelActivity(regionActivities);
            }
        }
    }

    private static void DeactivateGlobalCartelActivity(CartelActivities activities)
    {
        CartelActivity? activity = activities.CurrentGlobalActivity;
        if (activity != null)
        {
            CartelActivityDeactivateMethod?.Invoke(activity, Array.Empty<object>());
        }

        CartelActivitiesCurrentGlobalActivityField?.SetValue(activities, null);
    }

    private static void DeactivateRegionalCartelActivity(CartelRegionActivities regionActivities)
    {
        CartelActivity? activity = regionActivities.CurrentActivity;
        if (activity != null)
        {
            CartelActivityDeactivateMethod?.Invoke(activity, Array.Empty<object>());
        }

        CartelRegionActivitiesCurrentActivityField?.SetValue(regionActivities, null);
    }

    private static bool HasActiveSprayGraffitiActivity(EMapRegion region)
    {
        CartelActivities? activities = NetworkSingleton<Cartel>.Instance?.Activities;
        if (activities == null)
        {
            return true;
        }

        if (activities.CurrentGlobalActivity is SprayGraffiti globalActivity && globalActivity.Region == region)
        {
            return true;
        }

        CartelRegionActivities? regionActivities = activities.GetRegionalActivities(region);
        return regionActivities == null || regionActivities.CurrentActivity is SprayGraffiti;
    }

    private static void ApplyDarkMarketState(bool unlocked)
    {
        DarkMarket? darkMarket = NetworkSingleton<DarkMarket>.Instance;
        if (darkMarket == null)
        {
            return;
        }

        VariableDatabase? database = NetworkSingleton<VariableDatabase>.Instance;
        database?.SetVariableValue("WarehouseUnlocked", unlocked.ToString(), network: false);

        DarkMarketUnlockedField?.SetValue(darkMarket, unlocked);
        darkMarket.MainDoor?.SetKnockingEnabled(!unlocked);
        if (darkMarket.MainDoor?.Igor != null)
        {
            darkMarket.MainDoor.Igor.gameObject.SetActive(false);
        }

        if (darkMarket.AccessZone?.Doors != null)
        {
            foreach (DoorController door in darkMarket.AccessZone.Doors)
            {
                if (door != null)
                {
                    door.noAccessErrorMessage = unlocked ? "Only open after 6PM" : string.Empty;
                }
            }
        }

        if (unlocked)
        {
            darkMarket.Oscar?.EnableDeliveries();
        }
        else
        {
            SetOscarDeliveryAvailable(darkMarket.Oscar, false);
        }
    }

    private static void SetOscarDeliveryAvailable(Oscar oscar, bool available)
    {
        if (oscar?.ShopInterface == null || !PlayerSingleton<DeliveryApp>.InstanceExists)
        {
            return;
        }

        PlayerSingleton<DeliveryApp>.Instance.SetIsAvailable(oscar.ShopInterface, available);
        PlayerSingleton<DeliveryApp>.Instance.RefreshContent();
    }

    private static void ApplySewerState(bool unlocked)
    {
        SewerManager? sewerManager = NetworkSingleton<SewerManager>.Instance;
        if (sewerManager == null)
        {
            return;
        }

        if (unlocked)
        {
            SewerManagerSetSewerUnlockedClientLogicMethod?.Invoke(sewerManager, new object?[] { null });
            return;
        }

        SewerManagerIsSewerUnlockedField?.SetValue(sewerManager, false);
    }

    private static EQuestState ParseQuestState(string state)
    {
        return Enum.TryParse(state, out EQuestState parsed) ? parsed : EQuestState.Inactive;
    }
}
#endif
