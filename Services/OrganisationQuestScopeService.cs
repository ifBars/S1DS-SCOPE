#if SERVER
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Persistence;
using DedicatedServerMod.Organisations.Utils;
using HarmonyLib;
using Newtonsoft.Json;
#if IL2CPP
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Map;
using QuestGameDateTime = Il2CppScheduleOne.GameTime.GameDateTime;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.Variables;
using ECartelStatus = Il2Cpp.ECartelStatus;
using GUIDManager = Il2Cpp.GUIDManager;
using Guid = Il2CppSystem.Guid;
#else
using ScheduleOne.Cartel;
using ScheduleOne.DevUtilities;
using ScheduleOne.Economy;
using ScheduleOne.GameTime;
using ScheduleOne.Graffiti;
using ScheduleOne.ItemFramework;
using ScheduleOne.Map;
using QuestGameDateTime = ScheduleOne.GameTime.GameDateTime;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.Product;
using ScheduleOne.Quests;
using ScheduleOne.Storage;
using ScheduleOne.Variables;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Services;

internal sealed class OrganisationQuestScopeService
{
    private const string ActiveContractCountVariableName = "Active_Contract_Count";
    private const string AcceptedContractCountVariableName = "Accepted_Contract_Count";
    private const string CompletedContractCountVariableName = "Completed_Contracts_Count";
    private const string DaysSinceTutorialCompletedVariableName = "Days_Since_Tutorial_Completed";
    private const string LoanSharksArrivedVariableName = "Loan_Sharks_Arrived";
    private const string HoursSinceLoanSharksArrivedVariableName = "Hours_Since_LoanSharks_Arrived";
    private const string PendingSupplierDeaddropReservationPrefix = "deaddrop:pending_supplier:";

    private static readonly FieldInfo? CartelActivitiesCurrentGlobalActivityField = typeof(CartelActivities).GetField("<CurrentGlobalActivity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CartelActivitiesHoursUntilNextGlobalActivityField = typeof(CartelActivities).GetField("<HoursUntilNextGlobalActivity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CartelRegionActivitiesCurrentActivityField = typeof(CartelRegionActivities).GetField("<CurrentActivity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CartelRegionActivitiesHoursUntilNextActivityField = typeof(CartelRegionActivities).GetField("<HoursUntilNextActivity>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CartelStatusField = typeof(Cartel).GetField("<Status>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CartelHoursSinceStatusChangeField = typeof(Cartel).GetField("<HoursSinceStatusChange>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? MapRegionIsUnlockedField = typeof(MapRegionData).GetField("<IsUnlocked>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ProductManagerIsAcceptingOrdersField = typeof(ProductManager).GetField("<IsAcceptingOrders>k__BackingField", BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic);
    private static readonly FieldInfo? ProductManagerCurrentMixOperationField = typeof(ProductManager).GetField("<CurrentMixOperation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ProductManagerIsMixCompleteField = typeof(ProductManager).GetField("<IsMixComplete>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? ProductManagerMixRecipesField = typeof(ProductManager).GetField("mixRecipes", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? ProductManagerCreateMixRecipeLogicMethod = typeof(ProductManager).GetMethod("RpcLogic___CreateMixRecipe_1410895574", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

    private readonly IOrganisationRepository _repository;
    private readonly IOrganisationService _organisationService;
    private readonly OrganisationLogger _logger;
    private readonly List<string> _hydratedAudienceSteamIds = new List<string>();

    private string? _hydratedOwnerKey;
    private string? _initialQuestTemplateJson;
    private Dictionary<string, string>? _initialVariableTemplate;
    private int _runtimeCaptureSuppressionDepth;

    public OrganisationQuestScopeService(IOrganisationRepository repository, IOrganisationService organisationService, OrganisationLogger logger)
    {
        _repository = repository;
        _organisationService = organisationService;
        _logger = logger;
    }

    public bool IsRuntimeCaptureSuppressed => _runtimeCaptureSuppressionDepth > 0;
    public bool HasHydratedOwner => !string.IsNullOrWhiteSpace(_hydratedOwnerKey);
    public string? HydratedOwnerKey => _hydratedOwnerKey;

    public void EnsurePersonalScopeCaptured(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return;
        }

        EnsureInitialQuestTemplateCaptured();
        EnsureScope(BuildPlayerOwnerKey(steamId));
    }

    public void ClonePersonalScopeToOrganisation(string steamId, string organisationId)
    {
        if (string.IsNullOrWhiteSpace(steamId) || string.IsNullOrWhiteSpace(organisationId))
        {
            return;
        }

        EnsureInitialQuestTemplateCaptured();

        string personalOwnerKey = BuildPlayerOwnerKey(steamId);
        QuestScopeRecord personalScope = EnsureScope(personalOwnerKey);
        string organisationOwnerKey = BuildOrganisationOwnerKey(organisationId);
        if (_repository.Current.QuestScopes.ContainsKey(organisationOwnerKey))
        {
            return;
        }

        QuestScopeRecord clone = personalScope.Clone();
        clone.OwnerKey = organisationOwnerKey;
        clone.UpdatedAtUtc = DateTime.UtcNow;
        _repository.Current.QuestScopes[organisationOwnerKey] = clone;
        _repository.MarkDirty();
    }

    public void CaptureCurrentWorldStateForDeterministicScope()
    {
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return;
        }

        CaptureLiveWorldState(_hydratedOwnerKey);
    }

    public void TryHydrateWorldForPlayer(string steamId)
    {
        string ownerKey = ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        EnsureInitialQuestTemplateCaptured();

        if (!string.IsNullOrWhiteSpace(_hydratedOwnerKey)
            && !string.Equals(_hydratedOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            CaptureLiveWorldState(_hydratedOwnerKey);
        }

        _hydratedOwnerKey = ownerKey;
        ReplaceHydratedAudience(steamId);
        ApplyOwnerScopeToLiveWorld(ownerKey, forceReset: true);
        CaptureLiveWorldState(ownerKey);
    }

    public bool NotifyWorldMutation(string reason)
    {
        _ = reason;
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        CaptureLiveWorldState(_hydratedOwnerKey);
        return true;
    }

    public bool RecordHydratedVariableValue(string variableName)
    {
        if (IsRuntimeCaptureSuppressed
            || string.IsNullOrWhiteSpace(_hydratedOwnerKey)
            || string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

        if (string.Equals(variableName, ActiveContractCountVariableName, StringComparison.OrdinalIgnoreCase))
        {
            return RecordHydratedActiveContractCountVariable();
        }

        BaseVariable? variable = FindLiveVariable(variableName);
        if (variable == null || !variable.Persistent || variable.VariableMode != EVariableMode.Global)
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(_hydratedOwnerKey);
        string value = variable.GetValue()?.ToString() ?? string.Empty;
        if (scope.VariableValuesByName != null
            && scope.VariableValuesByName.TryGetValue(variable.Name, out string? existing)
            && string.Equals(existing, value, StringComparison.Ordinal))
        {
            return false;
        }

        scope.VariableValuesByName ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        scope.VariableValuesByName[variable.Name] = value;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    private bool RecordHydratedActiveContractCountVariable()
    {
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        int activeCount = GetContractCount(_hydratedOwnerKey, "Active");
        return SetHydratedVariableValue(ActiveContractCountVariableName, activeCount.ToString(), replicateLiveValue: true);
    }

    private int GetCompletedContractCount(string ownerKey)
    {
        return GetContractCount(ownerKey, EQuestState.Completed.ToString());
    }

    private int GetContractCount(string ownerKey, string? status)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return 0;
        }

        return _repository.Current.ScopedContracts.Values.Count(record =>
            string.Equals(record.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)
            && (string.IsNullOrWhiteSpace(status) || string.Equals(record.Status, status, StringComparison.OrdinalIgnoreCase)));
    }

    public bool RecordHydratedDeaddropState(DeaddropQuest quest)
    {
        if (IsRuntimeCaptureSuppressed
            || string.IsNullOrWhiteSpace(_hydratedOwnerKey)
            || quest?.Drop == null)
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(_hydratedOwnerKey);
        List<ScopedDeaddropQuestSyncDto> deaddrops = DeserializeDeaddropList(scope.DeaddropQuestDataJson);
        UpsertDeaddropSync(deaddrops, CreateDeaddropSync(quest));
        scope.DeaddropQuestDataJson = JsonConvert.SerializeObject(deaddrops);
        CaptureDeaddropStorage(scope, deaddrops);
        ReleasePendingSupplierDeaddropReservation(quest.Drop.GUID.ToString());
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool RecordOwnerDeaddropState(string ownerKey, DeaddropQuest quest)
    {
        if (IsRuntimeCaptureSuppressed
            || string.IsNullOrWhiteSpace(ownerKey)
            || quest?.Drop == null)
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        List<ScopedDeaddropQuestSyncDto> deaddrops = DeserializeDeaddropList(scope.DeaddropQuestDataJson);
        UpsertDeaddropSync(deaddrops, CreateDeaddropSync(quest));
        scope.DeaddropQuestDataJson = JsonConvert.SerializeObject(deaddrops);
        CaptureDeaddropStorage(scope, deaddrops);
        ReleasePendingSupplierDeaddropReservation(quest.Drop.GUID.ToString());
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool AddOwnerNumericVariableValue(string ownerKey, string variableName, float delta, bool replicateLiveValue)
    {
        if (IsRuntimeCaptureSuppressed
            || string.IsNullOrWhiteSpace(ownerKey)
            || string.IsNullOrWhiteSpace(variableName)
            || float.IsNaN(delta)
            || float.IsInfinity(delta))
        {
            return false;
        }

        BaseVariable? variable = FindLiveVariable(variableName);
        if (variable == null || !variable.Persistent || variable.VariableMode != EVariableMode.Global)
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        scope.VariableValuesByName ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string currentValue = scope.VariableValuesByName.TryGetValue(variable.Name, out string? storedValue)
            ? storedValue
            : variable.GetValue()?.ToString() ?? "0";
        if (!float.TryParse(currentValue, out float current))
        {
            current = 0f;
        }

        string nextValue = (current + delta).ToString();
        scope.VariableValuesByName[variable.Name] = nextValue;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();

        if (replicateLiveValue && string.Equals(_hydratedOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            variable.SetValue(nextValue, true);
        }

        return true;
    }

    public List<string> AdvanceTutorialDaysForCompletedQuest(Quest quest)
    {
        List<string> changedOwnerKeys = new List<string>();
        if (IsRuntimeCaptureSuppressed || !QuestScopeRules.ShouldVirtualizeQuest(quest))
        {
            return changedOwnerKeys;
        }

        string questGuid = quest.GUID.ToString();
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes.ToList())
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !HasScopedQuestState(pair.Value, questGuid, EQuestState.Completed))
            {
                continue;
            }

            if (AddOwnerNumericVariableValue(
                    pair.Key,
                    DaysSinceTutorialCompletedVariableName,
                    1f,
                    replicateLiveValue: string.Equals(_hydratedOwnerKey, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                changedOwnerKeys.Add(pair.Key);
            }
        }

        return changedOwnerKeys;
    }

    public List<string> MarkLoanSharkArrivalForReadyScopes(Quest_SinkOrSwim quest)
    {
        List<string> changedOwnerKeys = new List<string>();
        if (IsRuntimeCaptureSuppressed || !QuestScopeRules.ShouldVirtualizeQuest(quest))
        {
            return changedOwnerKeys;
        }

        string questGuid = quest.GUID.ToString();
        int finalEntryIndex = quest.Entries.Count - 1;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes.ToList())
        {
            if (string.IsNullOrWhiteSpace(pair.Key)
                || !HasScopedQuestState(pair.Value, questGuid, EQuestState.Active)
                || !TryGetScopedFloat(pair.Value, DaysSinceTutorialCompletedVariableName, out float daysSinceTutorialCompleted)
                || daysSinceTutorialCompleted <= Quest_SinkOrSwim.DAYS_TO_COMPLETE
                || TryGetScopedBool(pair.Value, LoanSharksArrivedVariableName, out bool loanSharksArrived) && loanSharksArrived)
            {
                continue;
            }

            QuestScopeRecord scope = EnsureScope(pair.Key);
            scope.VariableValuesByName ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            scope.VariableValuesByName[LoanSharksArrivedVariableName] = true.ToString();
            UpdateQuestEntryState(scope, questGuid, finalEntryIndex, EQuestState.Completed);
            scope.UpdatedAtUtc = DateTime.UtcNow;
            changedOwnerKeys.Add(pair.Key);
            SynchronizeHydratedWorld(pair.Key);
        }

        if (changedOwnerKeys.Count > 0)
        {
            _repository.MarkDirty();
        }

        return changedOwnerKeys;
    }

    public List<string> AdvanceLoanSharkHoursForCompletedScopes(Quest_TheDeepEnd quest)
    {
        List<string> changedOwnerKeys = new List<string>();
        if (IsRuntimeCaptureSuppressed || !QuestScopeRules.ShouldVirtualizeQuest(quest))
        {
            return changedOwnerKeys;
        }

        Quest? sinkOrSwimQuest = Quest.GetQuest("Sink or Swim");
        if (sinkOrSwimQuest == null)
        {
            return changedOwnerKeys;
        }

        string sinkOrSwimQuestGuid = sinkOrSwimQuest.GUID.ToString();
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes.ToList())
        {
            if (string.IsNullOrWhiteSpace(pair.Key)
                || !HasScopedQuestState(pair.Value, sinkOrSwimQuestGuid, EQuestState.Completed))
            {
                continue;
            }

            if (AddOwnerNumericVariableValue(
                    pair.Key,
                    HoursSinceLoanSharksArrivedVariableName,
                    1f,
                    replicateLiveValue: string.Equals(_hydratedOwnerKey, pair.Key, StringComparison.OrdinalIgnoreCase)))
            {
                changedOwnerKeys.Add(pair.Key);
            }
        }

        return changedOwnerKeys;
    }

    public bool IsLiveGlobalVariableTrue(string variableName)
    {
        BaseVariable? variable = FindLiveVariable(variableName);
        return variable != null
            && bool.TryParse(variable.GetValue()?.ToString() ?? string.Empty, out bool value)
            && value;
    }

    public void SetLiveGlobalVariableWithoutCapture(string variableName, string value, bool network)
    {
        BaseVariable? variable = FindLiveVariable(variableName);
        if (variable == null || !variable.Persistent || variable.VariableMode != EVariableMode.Global)
        {
            return;
        }

        ExecuteWithoutRuntimeCapture(() => variable.SetValue(value, network));
    }

    public bool IsScopedPersistentGlobalVariable(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

        BaseVariable? variable = FindLiveVariable(variableName);
        return variable != null && variable.Persistent && variable.VariableMode == EVariableMode.Global;
    }

    public bool SetOwnerVariableValue(string ownerKey, string variableName, string value)
    {
        if (IsRuntimeCaptureSuppressed
            || string.IsNullOrWhiteSpace(ownerKey)
            || string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

        BaseVariable? variable = FindLiveVariable(variableName);
        if (variable == null || !variable.Persistent || variable.VariableMode != EVariableMode.Global)
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        scope.VariableValuesByName ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string nextValue = value ?? string.Empty;
        if (scope.VariableValuesByName.TryGetValue(variable.Name, out string? existing)
            && string.Equals(existing, nextValue, StringComparison.Ordinal))
        {
            return false;
        }

        scope.VariableValuesByName[variable.Name] = nextValue;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool RecordOwnerLiveVariableValue(string ownerKey, string variableName)
    {
        if (IsRuntimeCaptureSuppressed
            || string.IsNullOrWhiteSpace(ownerKey)
            || string.IsNullOrWhiteSpace(variableName))
        {
            return false;
        }

        BaseVariable? variable = FindLiveVariable(variableName);
        if (variable == null || !variable.Persistent || variable.VariableMode != EVariableMode.Global)
        {
            return false;
        }

        return SetOwnerVariableValue(ownerKey, variable.Name, variable.GetValue()?.ToString() ?? string.Empty);
    }

    private static bool HasScopedQuestState(QuestScopeRecord scope, string questGuid, EQuestState state)
    {
        if (scope == null || string.IsNullOrWhiteSpace(questGuid))
        {
            return false;
        }

        return DeserializeQuestList(scope.QuestManagerDataJson).Any(quest =>
            string.Equals(quest.Guid, questGuid, StringComparison.OrdinalIgnoreCase)
            && string.Equals(quest.State, state.ToString(), StringComparison.OrdinalIgnoreCase));
    }

    private static bool TryGetScopedFloat(QuestScopeRecord scope, string variableName, out float value)
    {
        value = 0f;
        if (scope.VariableValuesByName == null
            || !scope.VariableValuesByName.TryGetValue(variableName, out string? storedValue))
        {
            return false;
        }

        return float.TryParse(storedValue, out value);
    }

    private static bool TryGetScopedBool(QuestScopeRecord scope, string variableName, out bool value)
    {
        value = false;
        return scope.VariableValuesByName != null
            && scope.VariableValuesByName.TryGetValue(variableName, out string? storedValue)
            && bool.TryParse(storedValue, out value);
    }

    private static void UpdateQuestEntryState(QuestScopeRecord scope, string questGuid, int entryIndex, EQuestState state)
    {
        if (entryIndex < 0)
        {
            return;
        }

        List<ScopedQuestSyncDto> quests = DeserializeQuestList(scope.QuestManagerDataJson);
        ScopedQuestSyncDto? quest = FindOrCreateQuestSync(quests, questGuid);
        if (quest == null || entryIndex >= quest.Entries.Count)
        {
            return;
        }

        quest.Entries[entryIndex].State = state.ToString();
        SyncLiveQuestFields(quest, questGuid);
        scope.QuestManagerDataJson = JsonConvert.SerializeObject(quests);
    }

    public QuestScopeSyncDto? BuildScopeSyncForHydratedOwner()
    {
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return null;
        }

        CaptureLiveWorldState(_hydratedOwnerKey);
        return BuildScopeSync(_hydratedOwnerKey);
    }

    public List<string> GetAudienceSteamIdsForHydratedOwner()
    {
        return new List<string>(_hydratedAudienceSteamIds);
    }

    public bool TryGetHydratedActiveDeaddropPosition(out Vector3 position)
    {
        position = default;
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(_hydratedOwnerKey);
        foreach (ScopedDeaddropQuestSyncDto deaddrop in BuildScopedDeaddrops(scope))
        {
            if (deaddrop == null
                || string.IsNullOrWhiteSpace(deaddrop.DeaddropGuid)
                || !scope.DeaddropStorageDataByDropGuid.TryGetValue(deaddrop.DeaddropGuid, out string? storageJson)
                || string.IsNullOrWhiteSpace(storageJson)
                || !TryDeserializeWorldStorage(storageJson, out WorldStorageEntityData? storageData)
                || storageData == null
                || !StorageContainsItems(storageData)
                || !Guid.TryParse(deaddrop.DeaddropGuid, out Guid parsedGuid))
            {
                continue;
            }

            DeadDrop? drop = GUIDManager.GetObject<DeadDrop>(parsedGuid);
            if (drop == null)
            {
                continue;
            }

            position = drop.transform.position;
            return true;
        }

        return false;
    }

    public bool TryGetHydratedCompletedContractCount(out int count)
    {
        count = 0;
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        count = GetCompletedContractCount(_hydratedOwnerKey);
        return true;
    }

    public bool ApplyHydratedCompletedContractCountVariable()
    {
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        int completedCount = GetCompletedContractCount(_hydratedOwnerKey);
        return SetHydratedVariableValue(CompletedContractCountVariableName, completedCount.ToString(), replicateLiveValue: true);
    }

    public bool ApplyHydratedAcceptedContractCountVariable()
    {
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        int acceptedCount = _repository.Current.ScopedContracts.Values.Count(record =>
            string.Equals(record.OwnerKey, _hydratedOwnerKey, StringComparison.OrdinalIgnoreCase)
            && (string.Equals(record.Status, "Active", StringComparison.OrdinalIgnoreCase)
                || string.Equals(record.Status, EQuestState.Completed.ToString(), StringComparison.OrdinalIgnoreCase)));
        return SetHydratedVariableValue(AcceptedContractCountVariableName, acceptedCount.ToString(), replicateLiveValue: true);
    }

    public bool ApplyHydratedActiveContractCountVariable()
    {
        return RecordHydratedActiveContractCountVariable();
    }

    public bool RecordHydratedCartelStatus(string status, bool resetStatusChangeTimer = true)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(_hydratedOwnerKey) || string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(_hydratedOwnerKey);
        if (string.Equals(scope.CartelStatus, status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        scope.CartelStatus = status;
        if (resetStatusChangeTimer)
        {
            scope.CartelHoursSinceStatusChange = 0;
        }

        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool RecordOwnerCartelStatus(string ownerKey, string status, bool resetStatusChangeTimer = true)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(status))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        if (string.Equals(scope.CartelStatus, status, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        scope.CartelStatus = status;
        if (resetStatusChangeTimer)
        {
            scope.CartelHoursSinceStatusChange = 0;
        }

        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public List<string> AdvanceCartelStatusHours()
    {
        List<string> changedOwnerKeys = new List<string>();
        if (IsRuntimeCaptureSuppressed)
        {
            return changedOwnerKeys;
        }

        bool repositoryChanged = false;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes.ToList())
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            QuestScopeRecord scope = EnsureScope(pair.Key);
            if (string.Equals(_hydratedOwnerKey, pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                int liveHours = BuildLiveCartelHoursSinceStatusChange();
                if (scope.CartelHoursSinceStatusChange != liveHours)
                {
                    scope.CartelHoursSinceStatusChange = liveHours;
                    scope.UpdatedAtUtc = DateTime.UtcNow;
                    repositoryChanged = true;
                    changedOwnerKeys.Add(pair.Key);
                }

                continue;
            }

            if (string.IsNullOrWhiteSpace(scope.CartelStatus) || scope.CartelHoursSinceStatusChange >= int.MaxValue)
            {
                continue;
            }

            scope.CartelHoursSinceStatusChange++;
            scope.UpdatedAtUtc = DateTime.UtcNow;
            repositoryChanged = true;
            changedOwnerKeys.Add(pair.Key);
        }

        if (repositoryChanged)
        {
            _repository.MarkDirty();
        }

        return changedOwnerKeys;
    }

    public bool RecordOwnerSewerUnlocked(string ownerKey)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        if (scope.SewerUnlocked)
        {
            return false;
        }

        scope.SewerUnlocked = true;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool IsOwnerSewerUnlocked(string ownerKey)
    {
        return !string.IsNullOrWhiteSpace(ownerKey)
            && _repository.Current.QuestScopes.TryGetValue(ownerKey, out QuestScopeRecord? scope)
            && scope.SewerUnlocked;
    }

    public bool IsOwnerMapRegionUnlocked(string ownerKey, EMapRegion region)
    {
        if (string.IsNullOrWhiteSpace(ownerKey)
            || !_repository.Current.QuestScopes.TryGetValue(ownerKey, out QuestScopeRecord? scope))
        {
            return IsMapRegionUnlockedByDefault(region);
        }

        if (scope.MapRegionUnlockedByRegion == null
            || !scope.MapRegionUnlockedByRegion.TryGetValue(region.ToString(), out bool isUnlocked))
        {
            return IsMapRegionUnlockedByDefault(region);
        }

        return isUnlocked || IsMapRegionUnlockedByDefault(region);
    }

    private static bool IsMapRegionUnlockedByDefault(EMapRegion region)
    {
        MapRegionData? regionData = Singleton<Map>.Instance?.GetRegionData(region);
        return regionData?.UnlockedByDefault == true;
    }

    public bool RecordOwnerCartelDeal(string ownerKey, CartelDealInfo? dealInfo)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        string dealJson = dealInfo?.IsValid() == true ? JsonConvert.SerializeObject(dealInfo) : string.Empty;
        if (string.Equals(scope.CartelDealDataJson, dealJson, StringComparison.Ordinal))
        {
            return false;
        }

        scope.CartelDealDataJson = dealJson;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool CanCaptureGeneratedCartelDealForOwner(string ownerKey)
    {
        return !IsRuntimeCaptureSuppressed
            && !string.IsNullOrWhiteSpace(ownerKey)
            && _repository.Current.QuestScopes.TryGetValue(ownerKey, out QuestScopeRecord? scope)
            && IsCartelTruced(scope)
            && string.IsNullOrWhiteSpace(scope.CartelDealDataJson)
            && scope.CartelDealHoursUntilNextRequest <= 0;
    }

    internal bool TryResolveGeneratedCartelDealOwner(string? pendingOwnerKey, string? preferredOwnerKey, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (CanCaptureGeneratedCartelDealForOwner(pendingOwnerKey ?? string.Empty))
        {
            ownerKey = pendingOwnerKey!;
            return true;
        }

        if (CanCaptureGeneratedCartelDealForOwner(preferredOwnerKey ?? string.Empty))
        {
            ownerKey = preferredOwnerKey!;
            return true;
        }

        DateTime selectedUpdatedAtUtc = DateTime.MaxValue;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes)
        {
            if (!CanCaptureGeneratedCartelDealForOwner(pair.Key))
            {
                continue;
            }

            if (ShouldPreferOwnerCandidate(pair.Key, pair.Value, ownerKey, selectedUpdatedAtUtc, out DateTime updatedAtUtc))
            {
                ownerKey = pair.Key;
                selectedUpdatedAtUtc = updatedAtUtc;
            }
        }

        return !string.IsNullOrWhiteSpace(ownerKey);
    }

    public bool RecordOwnerCartelDealStorage(string ownerKey)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        WorldStorageEntity? deliveryStorage = NetworkSingleton<Cartel>.Instance?.DealManager?.DeliveryEntity;
        if (deliveryStorage == null)
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        string storageJson = deliveryStorage.GetSaveData().GetJson(prettyPrint: false);
        if (string.Equals(scope.CartelDealStorageDataJson, storageJson, StringComparison.Ordinal))
        {
            return false;
        }

        scope.CartelDealStorageDataJson = storageJson;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool ClearOwnerCartelDealStorage(string ownerKey)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        if (string.IsNullOrWhiteSpace(scope.CartelDealStorageDataJson))
        {
            return false;
        }

        scope.CartelDealStorageDataJson = string.Empty;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool RecordOwnerCartelDealCooldown(string ownerKey, int hoursUntilNextDealRequest)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        int clampedHours = Math.Max(0, hoursUntilNextDealRequest);
        QuestScopeRecord scope = EnsureScope(ownerKey);
        if (scope.CartelDealHoursUntilNextRequest == clampedHours)
        {
            return false;
        }

        scope.CartelDealHoursUntilNextRequest = clampedHours;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public List<string> AdvanceCartelDealCooldowns(int hoursElapsed = 1)
    {
        List<string> changedOwnerKeys = new List<string>();
        if (IsRuntimeCaptureSuppressed || hoursElapsed <= 0)
        {
            return changedOwnerKeys;
        }

        bool repositoryChanged = false;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes.ToList())
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (string.Equals(_hydratedOwnerKey, pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                int liveCooldown = BuildLiveCartelDealCooldown();
                if (RecordOwnerCartelDealCooldown(pair.Key, liveCooldown))
                {
                    changedOwnerKeys.Add(pair.Key);
                }

                continue;
            }

            QuestScopeRecord scope = EnsureScope(pair.Key);
            if (!CanAdvanceCartelDealCooldown(scope))
            {
                continue;
            }

            int updatedCooldown = Math.Max(0, scope.CartelDealHoursUntilNextRequest - hoursElapsed);
            if (scope.CartelDealHoursUntilNextRequest == updatedCooldown)
            {
                continue;
            }

            scope.CartelDealHoursUntilNextRequest = updatedCooldown;
            scope.UpdatedAtUtc = DateTime.UtcNow;
            repositoryChanged = true;
            changedOwnerKeys.Add(pair.Key);
        }

        if (repositoryChanged)
        {
            _repository.MarkDirty();
        }

        return changedOwnerKeys;
    }

    public bool RecordHydratedCartelInfluence(EMapRegion region, float influence)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(_hydratedOwnerKey);
        string regionKey = region.ToString();
        float clampedInfluence = Mathf.Clamp01(influence);
        if (scope.CartelInfluenceByRegion.TryGetValue(regionKey, out float existing)
            && Math.Abs(existing - clampedInfluence) < 0.0001f)
        {
            return false;
        }

        scope.CartelInfluenceByRegion[regionKey] = clampedInfluence;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool RecordOwnerMapRegionUnlocked(string ownerKey, EMapRegion region)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        scope.MapRegionUnlockedByRegion ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        string regionKey = region.ToString();
        if (scope.MapRegionUnlockedByRegion.TryGetValue(regionKey, out bool isUnlocked) && isUnlocked)
        {
            return false;
        }

        scope.MapRegionUnlockedByRegion[regionKey] = true;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        _logger.Info($"Recorded scoped map region unlock ownerKey={ownerKey} region={region}.");
        return true;
    }

    public void ApplyMapRegionUnlockToLiveWorld(EMapRegion region)
    {
        Map? map = Singleton<Map>.Instance;
        MapRegionData? regionData = map?.GetRegionData(region);
        if (regionData == null)
        {
            return;
        }

        SetLiveMapRegionUnlocked(regionData, true);
    }

    public bool RecordOwnerCartelActivityState(string ownerKey)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        CartelActivityScopeRecord activityState = BuildLiveCartelActivityState();
        if (AreCartelActivityStatesEqual(scope.CartelActivityState, activityState))
        {
            return false;
        }

        scope.CartelActivityState = activityState;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public List<string> AdvanceCartelGlobalActivityCooldowns()
    {
        List<string> changedOwnerKeys = new List<string>();
        if (IsRuntimeCaptureSuppressed)
        {
            return changedOwnerKeys;
        }

        bool repositoryChanged = false;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes.ToList())
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (string.Equals(_hydratedOwnerKey, pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                if (RecordOwnerCartelActivityState(pair.Key))
                {
                    changedOwnerKeys.Add(pair.Key);
                }

                continue;
            }

            QuestScopeRecord scope = EnsureScope(pair.Key);
            CartelActivityScopeRecord activityState = scope.CartelActivityState ?? new CartelActivityScopeRecord();
            scope.CartelActivityState = activityState;
            if (!CanAdvanceCartelActivityCooldown(scope)
                || activityState.GlobalActivityIndex >= 0
                || activityState.GlobalHoursUntilNextActivity <= 0)
            {
                continue;
            }

            activityState.GlobalHoursUntilNextActivity--;
            scope.UpdatedAtUtc = DateTime.UtcNow;
            repositoryChanged = true;
            changedOwnerKeys.Add(pair.Key);
        }

        if (repositoryChanged)
        {
            _repository.MarkDirty();
        }

        return changedOwnerKeys;
    }

    public List<string> AdvanceCartelRegionalActivityCooldowns(EMapRegion region)
    {
        List<string> changedOwnerKeys = new List<string>();
        if (IsRuntimeCaptureSuppressed)
        {
            return changedOwnerKeys;
        }

        string regionKey = region.ToString();
        bool repositoryChanged = false;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes.ToList())
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
            {
                continue;
            }

            if (string.Equals(_hydratedOwnerKey, pair.Key, StringComparison.OrdinalIgnoreCase))
            {
                if (RecordOwnerCartelActivityState(pair.Key))
                {
                    changedOwnerKeys.Add(pair.Key);
                }

                continue;
            }

            QuestScopeRecord scope = EnsureScope(pair.Key);
            if (!CanAdvanceCartelActivityCooldown(scope, region)
                || scope.CartelActivityState == null
                || scope.CartelActivityState.RegionalActivitiesByRegion == null
                || !scope.CartelActivityState.RegionalActivitiesByRegion.TryGetValue(regionKey, out RegionalCartelActivityScopeRecord? regionalState)
                || regionalState.ActivityIndex >= 0
                || regionalState.HoursUntilNextActivity <= 0)
            {
                continue;
            }

            regionalState.HoursUntilNextActivity--;
            scope.UpdatedAtUtc = DateTime.UtcNow;
            repositoryChanged = true;
            changedOwnerKeys.Add(pair.Key);
        }

        if (repositoryChanged)
        {
            _repository.MarkDirty();
        }

        return changedOwnerKeys;
    }

    internal bool TryResolveStartedGlobalCartelActivityOwner(EMapRegion region, out string ownerKey)
    {
        return TryResolveStartedGlobalCartelActivityOwner(region, _ => true, out ownerKey);
    }

    internal bool TryResolveStartedGlobalCartelActivityOwner(EMapRegion region, Func<string, bool> ownerPredicate, out string ownerKey)
    {
        ownerKey = string.Empty;
        DateTime selectedUpdatedAtUtc = DateTime.MaxValue;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)
                || !IsReadyForGlobalCartelActivityStart(pair.Value, region)
                || !ownerPredicate(pair.Key))
            {
                continue;
            }

            if (ShouldPreferOwnerCandidate(pair.Key, pair.Value, ownerKey, selectedUpdatedAtUtc, out DateTime updatedAtUtc))
            {
                ownerKey = pair.Key;
                selectedUpdatedAtUtc = updatedAtUtc;
            }
        }

        return !string.IsNullOrWhiteSpace(ownerKey);
    }

    internal bool TryResolveStartedGlobalCartelActivityOwner(EMapRegion region, string? preferredOwnerKey, out string ownerKey)
    {
        ownerKey = string.Empty;
        if (!string.IsNullOrWhiteSpace(preferredOwnerKey)
            && _repository.Current.QuestScopes.TryGetValue(preferredOwnerKey, out QuestScopeRecord? preferredScope)
            && preferredScope != null
            && IsReadyForGlobalCartelActivityStart(preferredScope, region))
        {
            ownerKey = preferredOwnerKey;
            return true;
        }

        return TryResolveStartedGlobalCartelActivityOwner(region, out ownerKey);
    }

    internal List<string> GetReadyGlobalCartelActivityOwnerKeys(EMapRegion region)
    {
        List<string> ownerKeys = new List<string>();
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key) || !IsReadyForGlobalCartelActivityStart(pair.Value, region))
            {
                continue;
            }

            ownerKeys.Add(pair.Key);
        }

        return ownerKeys;
    }

    internal bool TryGetOwnerCartelInfluence(string ownerKey, EMapRegion region, out float influence)
    {
        influence = 0f;
        if (string.IsNullOrWhiteSpace(ownerKey)
            || !_repository.Current.QuestScopes.TryGetValue(ownerKey, out QuestScopeRecord? scope)
            || scope.CartelInfluenceByRegion == null
            || !scope.CartelInfluenceByRegion.TryGetValue(region.ToString(), out float scopedInfluence))
        {
            return false;
        }

        influence = Mathf.Clamp01(scopedInfluence);
        return true;
    }

    internal bool TryResolveStartedRegionalCartelActivityOwner(EMapRegion region, out string ownerKey)
    {
        ownerKey = string.Empty;
        string regionKey = region.ToString();
        DateTime selectedUpdatedAtUtc = DateTime.MaxValue;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes)
        {
            if (string.IsNullOrWhiteSpace(pair.Key)
                || !IsReadyForRegionalCartelActivityStart(pair.Value, regionKey, region))
            {
                continue;
            }

            if (ShouldPreferOwnerCandidate(pair.Key, pair.Value, ownerKey, selectedUpdatedAtUtc, out DateTime updatedAtUtc))
            {
                ownerKey = pair.Key;
                selectedUpdatedAtUtc = updatedAtUtc;
            }
        }

        return !string.IsNullOrWhiteSpace(ownerKey);
    }

    internal bool TryResolveStartedRegionalCartelActivityOwner(EMapRegion region, string? preferredOwnerKey, out string ownerKey)
    {
        ownerKey = string.Empty;
        string regionKey = region.ToString();
        if (!string.IsNullOrWhiteSpace(preferredOwnerKey)
            && _repository.Current.QuestScopes.TryGetValue(preferredOwnerKey, out QuestScopeRecord? preferredScope)
            && preferredScope != null
            && IsReadyForRegionalCartelActivityStart(preferredScope, regionKey, region))
        {
            ownerKey = preferredOwnerKey;
            return true;
        }

        return TryResolveStartedRegionalCartelActivityOwner(region, out ownerKey);
    }

    public bool RecordHydratedCartelGraffitiSurface(WorldSpraySurface surface)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(_hydratedOwnerKey) || surface == null)
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(_hydratedOwnerKey);
        scope.CartelGraffitiDataBySurfaceGuid ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string surfaceGuid = surface.GUID.ToString();
        WorldSpraySurfaceData data = surface.GetSaveData();
        bool hasScopedCartelGraffiti = data.ContainsCartelGraffiti || data.Strokes.Count > 0 || data.HasDrawingBeenFinalized;

        if (!hasScopedCartelGraffiti)
        {
            if (!scope.CartelGraffitiDataBySurfaceGuid.Remove(surfaceGuid))
            {
                return false;
            }
        }
        else
        {
            string surfaceJson = JsonConvert.SerializeObject(data);
            if (scope.CartelGraffitiDataBySurfaceGuid.TryGetValue(surfaceGuid, out string? existing)
                && string.Equals(existing, surfaceJson, StringComparison.Ordinal))
            {
                return false;
            }

            scope.CartelGraffitiDataBySurfaceGuid[surfaceGuid] = surfaceJson;
        }

        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public void PrepareProductMarketForPlayer(string steamId)
    {
        string ownerKey = ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        PrepareProductMarketForOwnerKey(ownerKey);
    }

    public void PrepareProductMarketForOwnerKey(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        ExecuteWithoutRuntimeCapture(() => ApplyScopedProductMarket(scope));
    }

    public bool RecordProductMarketForPlayer(string steamId)
    {
        string ownerKey = ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        return RecordProductMarketForOwnerKey(ownerKey);
    }

    public bool RecordProductMarketForOwnerKey(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        ProductMarketScopeRecord state = BuildLiveProductMarketState();
        if (AreProductMarketStatesEqual(scope.ProductMarketState, state))
        {
            return false;
        }

        scope.ProductMarketState = state;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public List<string> CompletePendingProductMixesOnNewDay()
    {
        List<string> changedOwnerKeys = new List<string>();
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes)
        {
            ProductMarketScopeRecord? productMarketState = pair.Value.ProductMarketState;
            if (productMarketState == null
                || productMarketState.IsMixComplete
                || string.IsNullOrWhiteSpace(productMarketState.CurrentMixOperationJson))
            {
                continue;
            }

            productMarketState.IsMixComplete = true;
            pair.Value.UpdatedAtUtc = DateTime.UtcNow;
            changedOwnerKeys.Add(pair.Key);
        }

        if (changedOwnerKeys.Count == 0)
        {
            return changedOwnerKeys;
        }

        _repository.MarkDirty();
        if (!string.IsNullOrWhiteSpace(_hydratedOwnerKey)
            && changedOwnerKeys.Any(ownerKey => string.Equals(ownerKey, _hydratedOwnerKey, StringComparison.OrdinalIgnoreCase)))
        {
            QuestScopeRecord hydratedScope = EnsureScope(_hydratedOwnerKey);
            ExecuteWithoutRuntimeCapture(() => ApplyScopedProductMarket(hydratedScope));
        }

        return changedOwnerKeys;
    }

    public bool TryGetActiveCartelDealOwner(out string ownerKey)
    {
        return TryGetActiveCartelDealOwner(out ownerKey, out _);
    }

    public bool TryGetActiveCartelDealOwner(out string ownerKey, out bool isAmbiguous)
    {
        ownerKey = string.Empty;
        isAmbiguous = false;
        foreach (KeyValuePair<string, QuestScopeRecord> pair in _repository.Current.QuestScopes)
        {
            if (string.IsNullOrWhiteSpace(pair.Value.CartelDealDataJson))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ownerKey))
            {
                ownerKey = string.Empty;
                isAmbiguous = true;
                return false;
            }

            ownerKey = pair.Key;
        }

        return !string.IsNullOrWhiteSpace(ownerKey);
    }

    public bool TryGetHydratedCartelStatus(out string status)
    {
        status = string.Empty;
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(_hydratedOwnerKey);
        status = scope.CartelStatus;
        return !string.IsNullOrWhiteSpace(status);
    }

    public void PrepareWorldForPlayer(string steamId)
    {
        TryHydrateWorldForPlayer(steamId);
    }

    public void PrepareWorldForOwnerKey(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        EnsureInitialQuestTemplateCaptured();
        if (!string.IsNullOrWhiteSpace(_hydratedOwnerKey)
            && !string.Equals(_hydratedOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            CaptureLiveWorldState(_hydratedOwnerKey);
        }

        _hydratedOwnerKey = ownerKey;
        ReplaceHydratedAudienceForOwnerKey(ownerKey);
        ApplyOwnerScopeToLiveWorld(ownerKey, forceReset: true);
        CaptureLiveWorldState(ownerKey);
    }

    public QuestScopeSyncDto? BuildScopeSyncForPlayer(string steamId)
    {
        string ownerKey = ResolveOwnerKey(steamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return null;
        }

        EnsureInitialQuestTemplateCaptured();
        if (string.Equals(ownerKey, _hydratedOwnerKey, StringComparison.OrdinalIgnoreCase))
        {
            CaptureLiveWorldState(ownerKey);
        }

        return BuildScopeSync(ownerKey);
    }

    public void RecordQuestAction(string steamId, string guid, QuestManager.EQuestAction action)
    {
        if (RecordDeaddropAction(steamId, guid, action))
        {
            return;
        }

        if (!QuestScopeRules.ShouldVirtualizeQuest(guid))
        {
            return;
        }

        string ownerKey = ResolveOwnerKey(steamId);
        UpdateScope(ownerKey, quests =>
        {
            ScopedQuestSyncDto? quest = FindOrCreateQuestSync(quests, guid);
            if (quest == null)
            {
                return;
            }

            quest.State = action switch
            {
                QuestManager.EQuestAction.Begin => EQuestState.Active.ToString(),
                QuestManager.EQuestAction.Success => EQuestState.Completed.ToString(),
                QuestManager.EQuestAction.Fail => EQuestState.Failed.ToString(),
                QuestManager.EQuestAction.Expire => EQuestState.Expired.ToString(),
                QuestManager.EQuestAction.Cancel => EQuestState.Cancelled.ToString(),
                _ => quest.State,
            };

            SyncLiveQuestFields(quest, guid);
        });

        SynchronizeHydratedWorld(ownerKey);
    }

    public void RecordQuestState(string steamId, string guid, EQuestState state)
    {
        if (RecordDeaddropState(steamId, guid, state))
        {
            return;
        }

        if (!QuestScopeRules.ShouldVirtualizeQuest(guid))
        {
            return;
        }

        string ownerKey = ResolveOwnerKey(steamId);
        UpdateScope(ownerKey, quests =>
        {
            ScopedQuestSyncDto? quest = FindOrCreateQuestSync(quests, guid);
            if (quest == null)
            {
                return;
            }

            quest.State = state.ToString();
            SyncLiveQuestFields(quest, guid);
        });

        SynchronizeHydratedWorld(ownerKey);
    }

    public void RecordQuestEntryState(string steamId, string guid, int entryIndex, EQuestState state)
    {
        if (RecordDeaddropEntryState(steamId, guid, entryIndex, state))
        {
            return;
        }

        if (!QuestScopeRules.ShouldVirtualizeQuest(guid))
        {
            return;
        }

        string ownerKey = ResolveOwnerKey(steamId);
        UpdateScope(ownerKey, quests =>
        {
            ScopedQuestSyncDto? quest = FindOrCreateQuestSync(quests, guid);
            if (quest == null || entryIndex < 0 || entryIndex >= quest.Entries.Count)
            {
                return;
            }

            quest.Entries[entryIndex].State = state.ToString();
            SyncLiveQuestFields(quest, guid);
        });

        SynchronizeHydratedWorld(ownerKey);
    }

    public void RecordQuestTracking(string steamId, string guid, bool tracked)
    {
        if (RecordDeaddropTracking(steamId, guid, tracked))
        {
            return;
        }

        if (!QuestScopeRules.ShouldVirtualizeQuest(guid))
        {
            return;
        }

        string ownerKey = ResolveOwnerKey(steamId);
        UpdateScope(ownerKey, quests =>
        {
            ScopedQuestSyncDto? quest = FindOrCreateQuestSync(quests, guid);
            if (quest == null)
            {
                return;
            }

            quest.IsTracked = tracked;
            SyncLiveQuestFields(quest, guid);
        });

        SynchronizeHydratedWorld(ownerKey);
    }

    private void SynchronizeHydratedWorld(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey)
            || !string.Equals(ownerKey, _hydratedOwnerKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ApplyOwnerScopeToLiveWorld(ownerKey, forceReset: false);
        CaptureLiveWorldState(ownerKey);
    }

    private void UpdateScope(string ownerKey, Action<List<ScopedQuestSyncDto>> update)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        List<ScopedQuestSyncDto> quests = DeserializeQuestList(scope.QuestManagerDataJson);
        update(quests);
        scope.QuestManagerDataJson = JsonConvert.SerializeObject(quests);
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    private void ApplyOwnerScopeToLiveWorld(string ownerKey, bool forceReset)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        QuestManager? questManager = QuestManager.Instance;
        if (questManager?.DefaultQuests == null)
        {
            return;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        List<ScopedQuestSyncDto> scopedQuests = DeserializeQuestList(scope.QuestManagerDataJson);
        List<ScopedDeaddropQuestSyncDto> scopedDeaddrops = DeserializeDeaddropList(scope.DeaddropQuestDataJson);
        ExecuteWithoutRuntimeCapture(() =>
        {
            ApplyScopedVariables(scope);
            ApplyScopedCartelRuntime(scope);
            ApplyVirtualizedQuests(questManager, scopedQuests, forceReset);
            ApplyVirtualizedDeaddrops(questManager, scope, scopedDeaddrops, forceReset);
            ApplyScopedCartelDealStorage(scope);
            ApplyScopedCartelDealCooldown(scope);
            ApplyScopedCartelInfluence(scope);
            ApplyScopedCartelActivityState(scope);
            ApplyScopedCartelGraffiti(scope);
            ApplyScopedMapRegionUnlocks(scope);
            ApplyScopedProductMarket(scope);
        });
    }

    private static void ApplyScopedCartelRuntime(QuestScopeRecord scope)
    {
        Cartel? cartel = NetworkSingleton<Cartel>.Instance;
        if (cartel == null)
        {
            return;
        }

        ECartelStatus status = ECartelStatus.Unknown;
        if (!string.IsNullOrWhiteSpace(scope.CartelStatus)
            && !Enum.TryParse(scope.CartelStatus, ignoreCase: true, out status))
        {
            status = ECartelStatus.Unknown;
        }

        CartelStatusField?.SetValue(cartel, status);
        CartelHoursSinceStatusChangeField?.SetValue(cartel, Math.Max(0, scope.CartelHoursSinceStatusChange));
    }

    private static void ApplyScopedCartelDealStorage(QuestScopeRecord scope)
    {
        WorldStorageEntity? deliveryStorage = NetworkSingleton<Cartel>.Instance?.DealManager?.DeliveryEntity;
        if (deliveryStorage == null)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(scope.CartelDealStorageDataJson)
            && TryDeserializeWorldStorage(scope.CartelDealStorageDataJson, out WorldStorageEntityData? storageData))
        {
            deliveryStorage.Load(storageData!);
            return;
        }

        if (string.IsNullOrWhiteSpace(scope.CartelDealDataJson))
        {
            deliveryStorage.ClearContents();
        }
    }

    private static void ApplyScopedCartelDealCooldown(QuestScopeRecord scope)
    {
        CartelDealManager? dealManager = NetworkSingleton<Cartel>.Instance?.DealManager;
        if (dealManager == null || scope.CartelDealHoursUntilNextRequest < 0)
        {
            return;
        }

        dealManager.SetHoursUntilDealRequest(scope.CartelDealHoursUntilNextRequest);
    }

    private static void ApplyScopedCartelInfluence(QuestScopeRecord scope)
    {
        if (scope.CartelInfluenceByRegion == null || scope.CartelInfluenceByRegion.Count == 0)
        {
            return;
        }

        CartelInfluence? influence = NetworkSingleton<Cartel>.Instance?.Influence;
        if (influence == null)
        {
            return;
        }

        foreach (KeyValuePair<string, float> pair in scope.CartelInfluenceByRegion)
        {
            if (!Enum.TryParse(pair.Key, ignoreCase: true, out EMapRegion region))
            {
                continue;
            }

            influence.SetInfluence(null, region, Mathf.Clamp01(pair.Value));
        }
    }

    private static void ApplyScopedCartelActivityState(QuestScopeRecord scope)
    {
        CartelActivities? activities = NetworkSingleton<Cartel>.Instance?.Activities;
        if (activities == null || scope.CartelActivityState == null)
        {
            return;
        }

        CartelActivityScopeRecord activityState = scope.CartelActivityState;
        CartelActivitiesHoursUntilNextGlobalActivityField?.SetValue(activities, activityState.GlobalHoursUntilNextActivity);
        CartelActivitiesCurrentGlobalActivityField?.SetValue(activities, GetActivityAtIndex(activities.GlobalActivities.AsManagedEnumerable(), activityState.GlobalActivityIndex));

        if (activities.RegionalActivities == null || activityState.RegionalActivitiesByRegion == null)
        {
            return;
        }

        foreach (CartelRegionActivities regionActivities in activities.RegionalActivities)
        {
            string regionKey = regionActivities.Region.ToString();
            if (!activityState.RegionalActivitiesByRegion.TryGetValue(regionKey, out RegionalCartelActivityScopeRecord? regionalState))
            {
                continue;
            }

            CartelRegionActivitiesHoursUntilNextActivityField?.SetValue(regionActivities, regionalState.HoursUntilNextActivity);
            CartelRegionActivitiesCurrentActivityField?.SetValue(regionActivities, GetActivityAtIndex(regionActivities.Activities.AsManagedEnumerable(), regionalState.ActivityIndex));
        }
    }

    private static CartelActivity? GetActivityAtIndex(IEnumerable<CartelActivity>? activities, int index)
    {
        if (activities == null || index < 0)
        {
            return null;
        }

        int currentIndex = 0;
        foreach (CartelActivity activity in activities)
        {
            if (currentIndex == index)
            {
                return activity;
            }

            currentIndex++;
        }

        return null;
    }

    private static void ApplyScopedMapRegionUnlocks(QuestScopeRecord scope)
    {
        if (scope.MapRegionUnlockedByRegion == null || scope.MapRegionUnlockedByRegion.Count == 0)
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
                || (scope.MapRegionUnlockedByRegion.TryGetValue(regionData.Region.ToString(), out bool scopedUnlocked) && scopedUnlocked);
            SetLiveMapRegionUnlocked(regionData, unlocked);
        }
    }

    private static void SetLiveMapRegionUnlocked(MapRegionData regionData, bool unlocked)
    {
        MapRegionIsUnlockedField?.SetValue(regionData, unlocked);
    }

    private static void ApplyScopedCartelGraffiti(QuestScopeRecord scope)
    {
        GraffitiManager? graffitiManager = NetworkSingleton<GraffitiManager>.Instance;
        if (graffitiManager?.WorldSpraySurfaces == null)
        {
            return;
        }

        scope.CartelGraffitiDataBySurfaceGuid ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (WorldSpraySurface surface in graffitiManager.WorldSpraySurfaces)
        {
            if (surface == null)
            {
                continue;
            }

            string surfaceGuid = surface.GUID.ToString();
            if (scope.CartelGraffitiDataBySurfaceGuid.TryGetValue(surfaceGuid, out string? surfaceJson)
                && TryDeserializeWorldSpraySurface(surfaceJson, out WorldSpraySurfaceData? surfaceData))
            {
#if IL2CPP
                surface.Set(null, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SprayStroke>(surfaceData!.Strokes.ToArray()), surfaceData.HasDrawingBeenFinalized, surfaceData.ContainsCartelGraffiti);
#else
                surface.Set(null, surfaceData!.Strokes.ToArray(), surfaceData.HasDrawingBeenFinalized, surfaceData.ContainsCartelGraffiti);
#endif
                continue;
            }

            if (surface.ContainsCartelGraffiti)
            {
#if IL2CPP
                surface.Set(null, new Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<SprayStroke>(Array.Empty<SprayStroke>()), hasBeenFinalized: false, isCartelGraffiti: false);
#else
                surface.Set(null, Array.Empty<SprayStroke>(), hasBeenFinalized: false, isCartelGraffiti: false);
#endif
            }
        }
    }

    private static void ApplyScopedProductMarket(QuestScopeRecord scope)
    {
        ProductMarketScopeApplier.Apply(scope.ProductMarketState);
    }

    private static void ApplyProductDefinitionList(ProductManager manager, string memberName, IEnumerable<string>? productIds)
    {
        object? value = GetProductManagerMemberValue(manager, memberName);
        if (value is not IList list)
        {
            return;
        }

        list.Clear();
        if (productIds == null)
        {
            return;
        }

        foreach (string productId in productIds.Where(productId => !string.IsNullOrWhiteSpace(productId)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            object? product = FindProductDefinition(manager, productId);
            if (product != null)
            {
                list.Add(product);
            }
        }
    }

    private static void ApplyProductPrices(ProductManager manager, ProductMarketScopeRecord state)
    {
        if (GetProductManagerMemberValue(manager, "ProductPrices") is not IDictionary productPrices)
        {
            return;
        }

        productPrices.Clear();
        if (state.PricesByProductId == null)
        {
            return;
        }

        foreach (KeyValuePair<string, float> pair in state.PricesByProductId)
        {
            object? product = FindProductDefinition(manager, pair.Key);
            if (product != null && !float.IsNaN(pair.Value) && !float.IsInfinity(pair.Value))
            {
                productPrices[product] = pair.Value;
            }
        }
    }

    private static void ApplyProductMixRecipes(ProductManager manager, ProductMarketScopeRecord state)
    {
        if (ProductManagerMixRecipesField?.GetValue(manager) is not IList mixRecipes)
        {
            return;
        }

        mixRecipes.Clear();
        if (state.MixRecipes == null || ProductManagerCreateMixRecipeLogicMethod == null)
        {
            return;
        }

        foreach (ProductMixRecipeScopeRecord recipe in state.MixRecipes)
        {
            if (recipe == null
                || string.IsNullOrWhiteSpace(recipe.ProductId)
                || string.IsNullOrWhiteSpace(recipe.MixerId)
                || string.IsNullOrWhiteSpace(recipe.OutputId))
            {
                continue;
            }

            ProductManagerCreateMixRecipeLogicMethod.Invoke(manager, new object?[] { null, recipe.ProductId, recipe.MixerId, recipe.OutputId });
        }
    }

    private static void ApplyProductContractReceipts(ProductManager manager, ProductMarketScopeRecord state)
    {
        if (manager.ContractReceipts == null)
        {
            return;
        }

        manager.ContractReceipts.Clear();
        if (state.ContractReceiptJson == null)
        {
            return;
        }

        foreach (string receiptJson in state.ContractReceiptJson)
        {
            ContractReceipt? receipt = DeserializeProductContractReceipt(receiptJson);
            if (receipt != null && !manager.ContractReceipts.AsManagedEnumerable().Any(existing => existing.ReceiptId == receipt.ReceiptId))
            {
                manager.ContractReceipts.Add(receipt);
            }
        }
    }

    private static void ApplyVirtualizedQuests(QuestManager questManager, List<ScopedQuestSyncDto> scopedQuests, bool forceReset)
    {
        foreach (Quest quest in questManager.DefaultQuests)
        {
            if (!QuestScopeRules.ShouldVirtualizeQuest(quest))
            {
                continue;
            }

            ScopedQuestSyncDto? scopedQuest = scopedQuests.FirstOrDefault(item => string.Equals(item.Guid, quest.GUID.ToString(), StringComparison.OrdinalIgnoreCase));
            if (scopedQuest == null)
            {
                if (forceReset)
                {
                    ResetQuest(quest);
                }

                continue;
            }

            ApplyQuestState(quest, scopedQuest, forceReset);
        }
    }

    private static void ResetQuest(Quest quest)
    {
        quest.SetIsTracked(false);
        for (int i = 0; i < quest.Entries.Count; i++)
        {
            quest.SetQuestEntryState(i, EQuestState.Inactive, network: false);
        }

        quest.SetQuestState(EQuestState.Inactive, network: false);
        quest.ConfigureExpiry(false, default);
    }

    private static void ApplyQuestState(Quest quest, ScopedQuestSyncDto scopedQuest, bool forceReset)
    {
        EQuestState targetQuestState = ParseQuestState(scopedQuest.State);
        if (forceReset || quest.State != targetQuestState)
        {
            quest.SetQuestState(targetQuestState, network: false);
        }

        for (int i = 0; i < quest.Entries.Count; i++)
        {
            if (i >= scopedQuest.Entries.Count)
            {
                if (forceReset)
                {
                    quest.SetQuestEntryState(i, EQuestState.Inactive, network: false);
                }

                continue;
            }

            ScopedQuestEntrySyncDto scopedEntry = scopedQuest.Entries[i];
            if (!string.Equals(quest.Entries[i].Title, scopedEntry.Name, StringComparison.Ordinal))
            {
                quest.Entries[i].SetEntryTitle(scopedEntry.Name);
            }

            EQuestState targetEntryState = ParseQuestState(scopedEntry.State);
            if (forceReset || quest.Entries[i].State != targetEntryState)
            {
                quest.SetQuestEntryState(i, targetEntryState, network: false);
            }
        }

        if (forceReset || quest.IsTracked != scopedQuest.IsTracked)
        {
            quest.SetIsTracked(scopedQuest.IsTracked);
        }

        if (forceReset
            || quest.Expires != scopedQuest.Expires
            || quest.Expiry.elapsedDays != scopedQuest.ExpiryElapsedDays
            || Math.Abs(quest.Expiry.time - scopedQuest.ExpiryTime) > 0.001f)
        {
            quest.ConfigureExpiry(
                scopedQuest.Expires,
                new QuestGameDateTime
                {
                    elapsedDays = scopedQuest.ExpiryElapsedDays,
                    time = scopedQuest.ExpiryTime,
                });
        }
    }

    private void ExecuteWithoutRuntimeCapture(Action action)
    {
        _runtimeCaptureSuppressionDepth++;
        try
        {
            action();
        }
        finally
        {
            _runtimeCaptureSuppressionDepth--;
        }
    }

    private void ReplaceHydratedAudience(string steamId)
    {
        _hydratedAudienceSteamIds.Clear();
        foreach (string memberSteamId in _organisationService.BuildSnapshot(steamId).MemberSteamIds
                     .Where(memberSteamId => !string.IsNullOrWhiteSpace(memberSteamId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _hydratedAudienceSteamIds.Add(memberSteamId);
        }
    }

    private void ReplaceHydratedAudienceForOwnerKey(string ownerKey)
    {
        _hydratedAudienceSteamIds.Clear();
        foreach (string memberSteamId in _organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds
                     .Where(memberSteamId => !string.IsNullOrWhiteSpace(memberSteamId))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            _hydratedAudienceSteamIds.Add(memberSteamId);
        }
    }

    private QuestScopeSyncDto BuildScopeSync(string ownerKey)
    {
        QuestScopeRecord scope = EnsureScope(ownerKey);
        List<ScopedQuestSyncDto> quests = DeserializeQuestList(scope.QuestManagerDataJson)
            .Where(quest => QuestScopeRules.ShouldVirtualizeQuest(quest.Guid))
            .ToList();

        return new QuestScopeSyncDto
        {
            OwnerKey = ownerKey,
            CartelStatus = ResolveEffectiveCartelStatus(scope),
            CartelDealDataJson = scope.CartelDealDataJson,
            CartelInfluenceByRegion = new Dictionary<string, float>(
                scope.CartelInfluenceByRegion ?? new Dictionary<string, float>(),
                StringComparer.OrdinalIgnoreCase),
            CartelActivityState = scope.CartelActivityState?.Clone() ?? new CartelActivityScopeRecord(),
            MapRegionUnlockedByRegion = new Dictionary<string, bool>(
                scope.MapRegionUnlockedByRegion ?? new Dictionary<string, bool>(),
                StringComparer.OrdinalIgnoreCase),
            DarkMarketUnlocked = IsScopedVariableTrue(scope, "WarehouseUnlocked"),
            SewerUnlocked = scope.SewerUnlocked,
            LoanSharksArrived = IsScopedVariableTrue(scope, LoanSharksArrivedVariableName),
            HoursSinceLoanSharksArrived = GetScopedVariableFloat(scope, HoursSinceLoanSharksArrivedVariableName),
            ProductMarketState = scope.ProductMarketState?.Clone() ?? new ProductMarketScopeRecord(),
            Quests = quests,
            Contracts = BuildScopedContracts(ownerKey),
            Deaddrops = BuildScopedDeaddrops(scope),
            InaccessibleDeaddropGuids = BuildInaccessibleDeaddropGuids(ownerKey),
        };
    }

    private static bool IsScopedVariableTrue(QuestScopeRecord scope, string variableName)
    {
        return scope.VariableValuesByName != null
            && scope.VariableValuesByName.TryGetValue(variableName, out string? value)
            && bool.TryParse(value, out bool parsed)
            && parsed;
    }

    private static float GetScopedVariableFloat(QuestScopeRecord scope, string variableName)
    {
        return TryGetScopedFloat(scope, variableName, out float value) ? value : 0f;
    }

    private static string ResolveEffectiveCartelStatus(QuestScopeRecord scope)
    {
        if (!string.IsNullOrWhiteSpace(scope.CartelStatus))
        {
            return scope.CartelStatus;
        }

        Cartel? cartel = NetworkSingleton<Cartel>.Instance;
        if (cartel == null)
        {
            return string.Empty;
        }

        return cartel.Status == ECartelStatus.Truced ? ECartelStatus.Unknown.ToString() : cartel.Status.ToString();
    }

    private void CaptureLiveWorldState(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        QuestManager? questManager = QuestManager.Instance;
        if (questManager?.DefaultQuests == null)
        {
            return;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        List<ScopedDeaddropQuestSyncDto> deaddrops = MergeLiveDeaddrops(scope);
        scope.QuestManagerDataJson = JsonConvert.SerializeObject(BuildLiveQuestList());
        scope.DeaddropQuestDataJson = JsonConvert.SerializeObject(deaddrops);
        scope.CartelStatus = BuildLiveCartelStatus();
        scope.CartelHoursSinceStatusChange = BuildLiveCartelHoursSinceStatusChange();
        scope.CartelDealDataJson = BuildLiveCartelDealJson();
        scope.CartelDealHoursUntilNextRequest = BuildLiveCartelDealCooldown();
        scope.CartelInfluenceByRegion = BuildLiveCartelInfluenceMap();
        scope.CartelActivityState = BuildLiveCartelActivityState();
        scope.MapRegionUnlockedByRegion = BuildLiveMapRegionUnlockMap();
        scope.ProductMarketState = BuildLiveProductMarketState();
        scope.VariableValuesByName = BuildLiveVariableMap();
        CaptureDeaddropStorage(scope, deaddrops);
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    private static string BuildLiveCartelStatus()
    {
        Cartel? cartel = NetworkSingleton<Cartel>.Instance;
        return cartel == null ? string.Empty : cartel.Status.ToString();
    }

    private static int BuildLiveCartelHoursSinceStatusChange()
    {
        Cartel? cartel = NetworkSingleton<Cartel>.Instance;
        return cartel == null ? 9999 : Math.Max(0, cartel.HoursSinceStatusChange);
    }

    private static string BuildLiveCartelDealJson()
    {
        CartelDealInfo? activeDeal = NetworkSingleton<Cartel>.Instance?.DealManager?.ActiveDeal;
        return activeDeal?.IsValid() == true ? JsonConvert.SerializeObject(activeDeal) : string.Empty;
    }

    private static int BuildLiveCartelDealCooldown()
    {
        CartelDealManager? dealManager = NetworkSingleton<Cartel>.Instance?.DealManager;
        return dealManager == null ? -1 : Math.Max(0, dealManager.HoursUntilNextDealRequest);
    }

    private static Dictionary<string, float> BuildLiveCartelInfluenceMap()
    {
        Dictionary<string, float> influenceByRegion = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        CartelInfluence? influence = NetworkSingleton<Cartel>.Instance?.Influence;
        if (influence == null)
        {
            return influenceByRegion;
        }

        foreach (CartelInfluence.RegionInfluenceData regionInfluence in influence.GetAllRegionInfluence())
        {
            influenceByRegion[regionInfluence.Region.ToString()] = Mathf.Clamp01(regionInfluence.Influence);
        }

        return influenceByRegion;
    }

    private static Dictionary<string, bool> BuildLiveMapRegionUnlockMap()
    {
        Dictionary<string, bool> unlockedByRegion = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        Map? map = Singleton<Map>.Instance;
        if (map?.Regions == null)
        {
            return unlockedByRegion;
        }

        foreach (MapRegionData regionData in map.Regions)
        {
            if (regionData != null)
            {
                unlockedByRegion[regionData.Region.ToString()] = regionData.IsUnlocked || regionData.UnlockedByDefault;
            }
        }

        return unlockedByRegion;
    }

    private static CartelActivityScopeRecord BuildLiveCartelActivityState()
    {
        CartelActivityScopeRecord activityState = new CartelActivityScopeRecord();
        CartelActivities? activities = NetworkSingleton<Cartel>.Instance?.Activities;
        if (activities == null)
        {
            return activityState;
        }

        activityState.GlobalHoursUntilNextActivity = Math.Max(0, activities.HoursUntilNextGlobalActivity);
        activityState.GlobalActivityIndex = GetActivityIndex(activities.GlobalActivities.AsManagedEnumerable(), activities.CurrentGlobalActivity);
        activityState.GlobalActivityRegion = activities.CurrentGlobalActivity != null ? activities.CurrentGlobalActivity.Region.ToString() : string.Empty;

        if (activities.RegionalActivities == null)
        {
            return activityState;
        }

        foreach (CartelRegionActivities regionActivities in activities.RegionalActivities)
        {
            activityState.RegionalActivitiesByRegion[regionActivities.Region.ToString()] = new RegionalCartelActivityScopeRecord
            {
                ActivityIndex = GetActivityIndex(regionActivities.Activities.AsManagedEnumerable(), regionActivities.CurrentActivity),
                HoursUntilNextActivity = Math.Max(0, regionActivities.HoursUntilNextActivity),
            };
        }

        return activityState;
    }

    private static int GetActivityIndex(IEnumerable<CartelActivity>? activities, CartelActivity? activity)
    {
        if (activities == null || activity == null)
        {
            return -1;
        }

        int index = 0;
        foreach (CartelActivity candidate in activities)
        {
            if (ReferenceEquals(candidate, activity))
            {
                return index;
            }

            index++;
        }

        return -1;
    }

    private static bool AreCartelActivityStatesEqual(CartelActivityScopeRecord? left, CartelActivityScopeRecord? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (left.GlobalActivityIndex != right.GlobalActivityIndex
            || left.GlobalHoursUntilNextActivity != right.GlobalHoursUntilNextActivity
            || !string.Equals(left.GlobalActivityRegion, right.GlobalActivityRegion, StringComparison.OrdinalIgnoreCase)
            || left.RegionalActivitiesByRegion.Count != right.RegionalActivitiesByRegion.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, RegionalCartelActivityScopeRecord> pair in left.RegionalActivitiesByRegion)
        {
            if (!right.RegionalActivitiesByRegion.TryGetValue(pair.Key, out RegionalCartelActivityScopeRecord? other)
                || pair.Value.ActivityIndex != other.ActivityIndex
                || pair.Value.HoursUntilNextActivity != other.HoursUntilNextActivity)
            {
                return false;
            }
        }

        return true;
    }

    private static bool CanAdvanceCartelActivityCooldown(QuestScopeRecord scope)
    {
        if (!IsCartelHostile(scope))
        {
            return false;
        }

        if (scope.CartelInfluenceByRegion == null || scope.CartelInfluenceByRegion.Count == 0)
        {
            return true;
        }

        foreach (float influence in scope.CartelInfluenceByRegion.Values)
        {
            if (influence > 0f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsReadyForGlobalCartelActivityStart(QuestScopeRecord scope)
    {
        return scope?.CartelActivityState != null
            && CanAdvanceCartelActivityCooldown(scope)
            && scope.CartelActivityState.GlobalActivityIndex < 0
            && scope.CartelActivityState.GlobalHoursUntilNextActivity == 0;
    }

    private bool IsReadyForGlobalCartelActivityStart(QuestScopeRecord scope, EMapRegion region)
    {
        return IsReadyForGlobalCartelActivityStart(scope)
            && IsOwnerMapRegionUnlocked(scope.OwnerKey, region)
            && scope.CartelInfluenceByRegion != null
            && scope.CartelInfluenceByRegion.TryGetValue(region.ToString(), out float influence)
            && influence > 0.001f;
    }

    private static bool IsReadyForRegionalCartelActivityStart(QuestScopeRecord scope, string regionKey, EMapRegion region)
    {
        return scope?.CartelActivityState?.RegionalActivitiesByRegion != null
            && CanAdvanceCartelActivityCooldown(scope, region)
            && scope.CartelActivityState.RegionalActivitiesByRegion.TryGetValue(regionKey, out RegionalCartelActivityScopeRecord? regionalState)
            && regionalState.ActivityIndex < 0
            && regionalState.HoursUntilNextActivity == 0;
    }

    private static bool CanAdvanceCartelActivityCooldown(QuestScopeRecord scope, EMapRegion region)
    {
        if (!IsCartelHostile(scope))
        {
            return false;
        }

        return scope.CartelInfluenceByRegion != null
            && scope.CartelInfluenceByRegion.TryGetValue(region.ToString(), out float influence)
            && influence > 0f;
    }

    private static bool IsCartelHostile(QuestScopeRecord scope)
    {
        if (Enum.TryParse(scope.CartelStatus, ignoreCase: true, out ECartelStatus status))
        {
            return status == ECartelStatus.Hostile;
        }

        return string.IsNullOrWhiteSpace(scope.CartelStatus)
            && NetworkSingleton<Cartel>.Instance?.Status == ECartelStatus.Hostile;
    }

    private static bool CanAdvanceCartelDealCooldown(QuestScopeRecord scope)
    {
        return IsCartelTruced(scope)
            && string.IsNullOrWhiteSpace(scope.CartelDealDataJson)
            && scope.CartelDealHoursUntilNextRequest > 0;
    }

    private static bool IsCartelTruced(QuestScopeRecord scope)
    {
        if (Enum.TryParse(scope.CartelStatus, ignoreCase: true, out ECartelStatus status))
        {
            return status == ECartelStatus.Truced;
        }

        return string.IsNullOrWhiteSpace(scope.CartelStatus)
            && NetworkSingleton<Cartel>.Instance?.Status == ECartelStatus.Truced;
    }

    private static bool ShouldPreferOwnerCandidate(
        string candidateOwnerKey,
        QuestScopeRecord candidateScope,
        string selectedOwnerKey,
        DateTime selectedUpdatedAtUtc,
        out DateTime candidateUpdatedAtUtc)
    {
        candidateUpdatedAtUtc = candidateScope.UpdatedAtUtc == default ? DateTime.MinValue : candidateScope.UpdatedAtUtc;
        return string.IsNullOrWhiteSpace(selectedOwnerKey)
            || candidateUpdatedAtUtc < selectedUpdatedAtUtc
            || (candidateUpdatedAtUtc == selectedUpdatedAtUtc && string.CompareOrdinal(candidateOwnerKey, selectedOwnerKey) < 0);
    }

    private static ProductMarketScopeRecord BuildLiveProductMarketState()
    {
        ProductMarketScopeRecord state = new ProductMarketScopeRecord();
        ProductManager? manager = NetworkSingleton<ProductManager>.Instance;
        if (manager == null)
        {
            return state;
        }

        state.DiscoveredProductIds = BuildProductIdSet(manager, "DiscoveredProducts");
        state.ListedProductIds = BuildProductIdSet(manager, "ListedProducts");
        state.FavouritedProductIds = BuildProductIdSet(manager, "FavouritedProducts");
        state.CreatedProductIds = BuildProductIdSet(manager, "createdProducts");
        state.PricesByProductId = BuildProductPriceMap(manager);
        state.MixRecipes = BuildProductMixRecipes(manager);
        state.ContractReceiptJson = BuildProductContractReceipts(manager);
        state.CurrentMixOperationJson = manager.CurrentMixOperation == null ? string.Empty : JsonConvert.SerializeObject(manager.CurrentMixOperation);
        state.IsMixComplete = manager.IsMixComplete;
        if (ProductManagerIsAcceptingOrdersField?.GetValue(manager) is bool isAcceptingOrders)
        {
            state.IsAcceptingOrders = isAcceptingOrders;
        }

        return state;
    }

    private static HashSet<string> BuildProductIdSet(ProductManager manager, string memberName)
    {
        HashSet<string> productIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (GetProductManagerMemberValue(manager, memberName) is not IEnumerable products)
        {
            return productIds;
        }

        foreach (object product in products)
        {
            string productId = GetProductId(product);
            if (!string.IsNullOrWhiteSpace(productId))
            {
                productIds.Add(productId);
            }
        }

        return productIds;
    }

    private static Dictionary<string, float> BuildProductPriceMap(ProductManager manager)
    {
        Dictionary<string, float> pricesByProductId = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        if (GetProductManagerMemberValue(manager, "ProductPrices") is not IDictionary productPrices)
        {
            return pricesByProductId;
        }

        foreach (DictionaryEntry entry in productPrices)
        {
            string productId = GetProductId(entry.Key);
            if (!string.IsNullOrWhiteSpace(productId) && entry.Value is float price)
            {
                pricesByProductId[productId] = price;
            }
        }

        return pricesByProductId;
    }

    private static List<ProductMixRecipeScopeRecord> BuildProductMixRecipes(ProductManager manager)
    {
        List<ProductMixRecipeScopeRecord> recipes = new List<ProductMixRecipeScopeRecord>();
        if (ProductManagerMixRecipesField?.GetValue(manager) is not IEnumerable mixRecipes)
        {
            return recipes;
        }

        foreach (object recipe in mixRecipes)
        {
            string productId = GetNestedItemId(recipe, "Ingredients", 0);
            string mixerId = GetNestedItemId(recipe, "Ingredients", 1);
            string outputId = GetRecipeOutputId(recipe);
            if (string.IsNullOrWhiteSpace(productId)
                || string.IsNullOrWhiteSpace(mixerId)
                || string.IsNullOrWhiteSpace(outputId))
            {
                continue;
            }

            recipes.Add(new ProductMixRecipeScopeRecord
            {
                ProductId = productId,
                MixerId = mixerId,
                OutputId = outputId,
            });
        }

        return recipes;
    }

    private static List<string> BuildProductContractReceipts(ProductManager manager)
    {
        List<string> receipts = new List<string>();
        if (manager.ContractReceipts == null)
        {
            return receipts;
        }

        foreach (ContractReceipt receipt in manager.ContractReceipts)
        {
            if (receipt != null)
            {
                receipts.Add(JsonConvert.SerializeObject(receipt));
            }
        }

        return receipts;
    }

    private static bool AreProductMarketStatesEqual(ProductMarketScopeRecord? left, ProductMarketScopeRecord? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return left.IsAcceptingOrders == right.IsAcceptingOrders
            && AreStringSetsEqual(left.DiscoveredProductIds, right.DiscoveredProductIds)
            && AreStringSetsEqual(left.ListedProductIds, right.ListedProductIds)
            && AreStringSetsEqual(left.FavouritedProductIds, right.FavouritedProductIds)
            && AreStringSetsEqual(left.CreatedProductIds, right.CreatedProductIds)
            && AreFloatMapsEqual(left.PricesByProductId, right.PricesByProductId)
            && AreProductMixRecipeListsEqual(left.MixRecipes, right.MixRecipes)
            && AreStringListsEqual(left.ContractReceiptJson, right.ContractReceiptJson)
            && left.IsMixComplete == right.IsMixComplete
            && string.Equals(left.CurrentMixOperationJson ?? string.Empty, right.CurrentMixOperationJson ?? string.Empty, StringComparison.Ordinal);
    }

    private static bool AreStringSetsEqual(ISet<string>? left, ISet<string>? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        return left.Count == right.Count && left.SetEquals(right);
    }

    private static bool AreFloatMapsEqual(IReadOnlyDictionary<string, float>? left, IReadOnlyDictionary<string, float>? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (KeyValuePair<string, float> pair in left)
        {
            if (!right.TryGetValue(pair.Key, out float other) || Math.Abs(pair.Value - other) > 0.0001f)
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreProductMixRecipeListsEqual(IReadOnlyList<ProductMixRecipeScopeRecord>? left, IReadOnlyList<ProductMixRecipeScopeRecord>? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            ProductMixRecipeScopeRecord leftRecipe = left[index];
            ProductMixRecipeScopeRecord rightRecipe = right[index];
            if (!string.Equals(leftRecipe.ProductId, rightRecipe.ProductId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(leftRecipe.MixerId, rightRecipe.MixerId, StringComparison.OrdinalIgnoreCase)
                || !string.Equals(leftRecipe.OutputId, rightRecipe.OutputId, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    private static bool AreStringListsEqual(IReadOnlyList<string>? left, IReadOnlyList<string>? right)
    {
        if (left == null || right == null)
        {
            return left == right;
        }

        if (left.Count != right.Count)
        {
            return false;
        }

        for (int index = 0; index < left.Count; index++)
        {
            if (!string.Equals(left[index] ?? string.Empty, right[index] ?? string.Empty, StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    private static object? FindProductDefinition(ProductManager manager, string productId)
    {
        if (string.IsNullOrWhiteSpace(productId) || GetProductManagerMemberValue(manager, "AllProducts") is not IEnumerable products)
        {
            return null;
        }

        foreach (object product in products)
        {
            if (string.Equals(GetProductId(product), productId, StringComparison.OrdinalIgnoreCase))
            {
                return product;
            }
        }

        return null;
    }

    private static NewMixOperation? DeserializeProductMixOperation(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<NewMixOperation>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ContractReceipt? DeserializeProductContractReceipt(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<ContractReceipt>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string GetProductId(object? product)
    {
        if (product == null)
        {
            return string.Empty;
        }

        Type type = product.GetType();
        PropertyInfo? property = type.GetProperty("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (property?.GetValue(product) is string propertyValue)
        {
            return propertyValue;
        }

        FieldInfo? field = type.GetField("ID", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        return field?.GetValue(product) as string ?? string.Empty;
    }

    private static string GetNestedItemId(object owner, string memberName, int index)
    {
        object? value = GetMemberValue(owner, memberName);
        if (value is not IList list || index < 0 || index >= list.Count)
        {
            return string.Empty;
        }

        object? quantity = list[index];
        object? items = GetMemberValue(quantity, "Items");
        if (items is not IList itemList || itemList.Count == 0)
        {
            return string.Empty;
        }

        return GetProductId(itemList[0]);
    }

    private static string GetRecipeOutputId(object recipe)
    {
        object? product = GetMemberValue(recipe, "Product");
        object? item = GetMemberValue(product, "Item");
        return GetProductId(item);
    }

    private static object? GetMemberValue(object? instance, string memberName)
    {
        if (instance == null || string.IsNullOrWhiteSpace(memberName))
        {
            return null;
        }

        Type type = instance.GetType();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo? field = type.GetField(memberName, flags);
        if (field != null)
        {
            return field.GetValue(instance);
        }

        PropertyInfo? property = type.GetProperty(memberName, flags);
        return property?.GetValue(instance);
    }

    private static object? GetProductManagerMemberValue(ProductManager manager, string memberName)
    {
        Type type = typeof(ProductManager);
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        FieldInfo? field = type.GetField(memberName, flags);
        if (field != null)
        {
            object? target = field.IsStatic ? null : manager;
            return field.GetValue(target);
        }

        PropertyInfo? property = type.GetProperty(memberName, flags);
        if (property != null)
        {
            MethodInfo? getter = property.GetGetMethod(nonPublic: true);
            object? target = getter?.IsStatic == true ? null : manager;
            return property.GetValue(target);
        }

        return null;
    }

    private void EnsureInitialQuestTemplateCaptured()
    {
        if (!string.IsNullOrWhiteSpace(_initialQuestTemplateJson))
        {
            return;
        }

        List<ScopedQuestSyncDto> currentLiveQuests = BuildLiveQuestList();
        _initialQuestTemplateJson = JsonConvert.SerializeObject(currentLiveQuests);
        _initialVariableTemplate = BuildLiveVariableMap();
    }

    private QuestScopeRecord EnsureScope(string ownerKey)
    {
        if (_repository.Current.QuestScopes.TryGetValue(ownerKey, out QuestScopeRecord? existing))
        {
            EnsureProductMarketStateInitialized(existing);
            EnsureCartelInfluenceInitialized(existing);
            EnsureMapRegionUnlocksInitialized(existing);
            if (existing.VariableValuesByName == null || existing.VariableValuesByName.Count == 0)
            {
                existing.VariableValuesByName = CloneVariableMap(_initialVariableTemplate ?? BuildLiveVariableMap());
                existing.UpdatedAtUtc = DateTime.UtcNow;
                _repository.MarkDirty();
            }

            return existing;
        }

        EnsureInitialQuestTemplateCaptured();
        string initialQuestJson = !string.IsNullOrWhiteSpace(_initialQuestTemplateJson)
            ? _initialQuestTemplateJson!
            : JsonConvert.SerializeObject(BuildLiveQuestList());

        QuestScopeRecord created = new QuestScopeRecord
        {
            OwnerKey = ownerKey,
            QuestManagerDataJson = initialQuestJson,
            DeaddropQuestDataJson = "[]",
            CartelInfluenceByRegion = BuildLiveCartelInfluenceMap(),
            MapRegionUnlockedByRegion = BuildLiveMapRegionUnlockMap(),
            ProductMarketState = BuildLiveProductMarketState(),
            VariableValuesByName = CloneVariableMap(_initialVariableTemplate ?? BuildLiveVariableMap()),
            UpdatedAtUtc = DateTime.UtcNow,
        };

        _repository.Current.QuestScopes[ownerKey] = created;
        _repository.MarkDirty();
        _logger.Info($"Created quest scope for {ownerKey}.");
        return created;
    }

    private void EnsureMapRegionUnlocksInitialized(QuestScopeRecord scope)
    {
        if (scope.MapRegionUnlockedByRegion != null && scope.MapRegionUnlockedByRegion.Count > 0)
        {
            return;
        }

        scope.MapRegionUnlockedByRegion = BuildLiveMapRegionUnlockMap();
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    private void EnsureCartelInfluenceInitialized(QuestScopeRecord scope)
    {
        if (scope.CartelInfluenceByRegion != null && scope.CartelInfluenceByRegion.Count > 0)
        {
            return;
        }

        scope.CartelInfluenceByRegion = BuildLiveCartelInfluenceMap();
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    private void EnsureProductMarketStateInitialized(QuestScopeRecord scope)
    {
        if (scope.ProductMarketState == null)
        {
            scope.ProductMarketState = BuildLiveProductMarketState();
            scope.UpdatedAtUtc = DateTime.UtcNow;
            _repository.MarkDirty();
            return;
        }

        if (!IsProductMarketStateEmpty(scope.ProductMarketState))
        {
            return;
        }

        ProductMarketScopeRecord liveState = BuildLiveProductMarketState();
        if (IsProductMarketStateEmpty(liveState))
        {
            return;
        }

        scope.ProductMarketState = liveState;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
    }

    private static bool IsProductMarketStateEmpty(ProductMarketScopeRecord state)
    {
        return (state.DiscoveredProductIds == null || state.DiscoveredProductIds.Count == 0)
            && (state.ListedProductIds == null || state.ListedProductIds.Count == 0)
            && (state.FavouritedProductIds == null || state.FavouritedProductIds.Count == 0)
            && (state.CreatedProductIds == null || state.CreatedProductIds.Count == 0)
            && (state.PricesByProductId == null || state.PricesByProductId.Count == 0)
            && (state.MixRecipes == null || state.MixRecipes.Count == 0)
            && (state.ContractReceiptJson == null || state.ContractReceiptJson.Count == 0)
            && string.IsNullOrWhiteSpace(state.CurrentMixOperationJson)
            && !state.IsMixComplete;
    }

    private static Dictionary<string, string> BuildLiveVariableMap()
    {
        Dictionary<string, string> variables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        VariableDatabase? database = VariableDatabase.Instance;
        if (database?.VariableList == null)
        {
            return variables;
        }

        foreach (BaseVariable variable in database.VariableList)
        {
            if (variable == null || !variable.Persistent || variable.VariableMode != EVariableMode.Global)
            {
                continue;
            }

            variables[variable.Name] = variable.GetValue()?.ToString() ?? string.Empty;
        }

        return variables;
    }

    private static Dictionary<string, string> CloneVariableMap(Dictionary<string, string> source)
    {
        return new Dictionary<string, string>(source, StringComparer.OrdinalIgnoreCase);
    }

    private static BaseVariable? FindLiveVariable(string variableName)
    {
        VariableDatabase? database = VariableDatabase.Instance;
        if (database?.VariableList == null)
        {
            return null;
        }

        foreach (BaseVariable variable in database.VariableList)
        {
            if (variable != null && string.Equals(variable.Name, variableName, StringComparison.OrdinalIgnoreCase))
            {
                return variable;
            }
        }

        return null;
    }

    private bool SetHydratedVariableValue(string variableName, string value, bool replicateLiveValue)
    {
        if (string.IsNullOrWhiteSpace(_hydratedOwnerKey))
        {
            return false;
        }

        BaseVariable? variable = FindLiveVariable(variableName);
        if (variable == null || !variable.Persistent || variable.VariableMode != EVariableMode.Global)
        {
            return false;
        }

        bool liveChanged = !string.Equals(variable.GetValue()?.ToString() ?? string.Empty, value, StringComparison.Ordinal);
        if (liveChanged)
        {
            variable.SetValue(value, replicateLiveValue);
        }

        QuestScopeRecord scope = EnsureScope(_hydratedOwnerKey);
        scope.VariableValuesByName ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (scope.VariableValuesByName.TryGetValue(variable.Name, out string? existing)
            && string.Equals(existing, value, StringComparison.Ordinal))
        {
            return liveChanged;
        }

        scope.VariableValuesByName[variable.Name] = value;
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    private static void ApplyScopedVariables(QuestScopeRecord scope)
    {
        VariableDatabase? database = VariableDatabase.Instance;
        if (database?.VariableList == null || scope.VariableValuesByName == null || scope.VariableValuesByName.Count == 0)
        {
            return;
        }

        foreach (BaseVariable variable in database.VariableList)
        {
            if (variable == null || !variable.Persistent || variable.VariableMode != EVariableMode.Global)
            {
                continue;
            }

            if (scope.VariableValuesByName.TryGetValue(variable.Name, out string? value)
                && value != null)
            {
                variable.SetValue(value, replicate: false);
            }
        }
    }

    private static List<ScopedQuestSyncDto> BuildLiveQuestList()
    {
        QuestManager? questManager = QuestManager.Instance;
        if (questManager?.DefaultQuests == null)
        {
            return new List<ScopedQuestSyncDto>();
        }

        return questManager.DefaultQuests
            .Where(quest => quest != null)
            .Where(QuestScopeRules.ShouldVirtualizeQuest)
            .Select(CreateQuestSync)
            .ToList();
    }

    private static ScopedQuestSyncDto CreateQuestSync(Quest quest)
    {
        return new ScopedQuestSyncDto
        {
            Guid = quest.GUID.ToString(),
            Title = quest.Title,
            Description = quest.Description,
            State = quest.State.ToString(),
            IsTracked = quest.IsTracked,
            Expires = quest.Expires,
            ExpiryElapsedDays = quest.Expiry.elapsedDays,
            ExpiryTime = quest.Expiry.time,
            Entries = quest.Entries.AsManagedEnumerable().Select(entry => new ScopedQuestEntrySyncDto
            {
                Name = entry.Title,
                State = entry.State.ToString(),
            }).ToList(),
        };
    }

    private static List<ScopedDeaddropQuestSyncDto> BuildLiveDeaddropList()
    {
        return DeaddropQuest.DeaddropQuests.AsManagedEnumerable()
            .Where(quest => quest != null && quest.Drop != null)
            .Select(CreateDeaddropSync)
            .ToList();
    }

    private static List<ScopedDeaddropQuestSyncDto> MergeLiveDeaddrops(QuestScopeRecord scope)
    {
        List<ScopedDeaddropQuestSyncDto> merged = DeserializeDeaddropList(scope.DeaddropQuestDataJson);
        foreach (ScopedDeaddropQuestSyncDto liveQuest in BuildLiveDeaddropList())
        {
            UpsertDeaddropSync(merged, liveQuest);
        }

        return merged;
    }

    private static void UpsertDeaddropSync(List<ScopedDeaddropQuestSyncDto> deaddrops, ScopedDeaddropQuestSyncDto quest)
    {
        int existingIndex = deaddrops.FindIndex(item => string.Equals(item.Guid, quest.Guid, StringComparison.OrdinalIgnoreCase));
        if (existingIndex >= 0)
        {
            deaddrops[existingIndex] = quest;
            return;
        }

        deaddrops.Add(quest);
    }

    private static ScopedDeaddropQuestSyncDto CreateDeaddropSync(DeaddropQuest quest)
    {
        return new ScopedDeaddropQuestSyncDto
        {
            Guid = quest.GUID.ToString(),
            Title = quest.Title,
            Description = quest.Description,
            State = quest.State.ToString(),
            IsTracked = quest.IsTracked,
            Expires = quest.Expires,
            ExpiryElapsedDays = quest.Expiry.elapsedDays,
            ExpiryTime = quest.Expiry.time,
            DeaddropGuid = quest.Drop.GUID.ToString(),
            Entries = quest.Entries.AsManagedEnumerable().Select(entry => new ScopedQuestEntrySyncDto
            {
                Name = entry.Title,
                State = entry.State.ToString(),
            }).ToList(),
        };
    }

    private void ApplyVirtualizedDeaddrops(QuestManager questManager, QuestScopeRecord scope, List<ScopedDeaddropQuestSyncDto> scopedDeaddrops, bool forceReset)
    {
        if (forceReset)
        {
            ResetDeaddrops();
        }

        foreach (ScopedDeaddropQuestSyncDto questData in scopedDeaddrops)
        {
            if (questData == null
                || string.IsNullOrWhiteSpace(questData.Guid)
                || string.IsNullOrWhiteSpace(questData.DeaddropGuid)
                || !Guid.TryParse(questData.Guid, out Guid questGuid)
                || !Guid.TryParse(questData.DeaddropGuid, out Guid dropGuid)
                || GUIDManager.IsGUIDAlreadyRegistered(questGuid))
            {
                continue;
            }

            DeadDrop? drop = GUIDManager.GetObject<DeadDrop>(dropGuid);
            if (drop == null)
            {
                continue;
            }

            DeaddropQuest quest = UnityEngine.Object.Instantiate(questManager.DeaddropCollectionPrefab.gameObject, questManager.QuestContainer).GetComponent<DeaddropQuest>();
            quest.SetDrop(drop);
            quest.Description = questData.Description;
            quest.SetGUID(questGuid);
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
                new QuestGameDateTime
                {
                    elapsedDays = questData.ExpiryElapsedDays,
                    time = questData.ExpiryTime,
                });
        }

        ApplyScopedDeaddropStorage(scope, scopedDeaddrops);
    }

    private void ApplyScopedDeaddropStorage(QuestScopeRecord scope, List<ScopedDeaddropQuestSyncDto> scopedDeaddrops)
    {
        HashSet<string> activeDropGuids = new HashSet<string>(
            scopedDeaddrops
                .Where(quest => quest != null && string.Equals(quest.State, EQuestState.Active.ToString(), StringComparison.OrdinalIgnoreCase))
                .Select(quest => quest.DeaddropGuid)
                .Where(guid => !string.IsNullOrWhiteSpace(guid)),
            StringComparer.OrdinalIgnoreCase);

        HashSet<string> globallyScopedDropGuids = GetGloballyActiveDeaddropGuids();
        foreach (DeadDrop drop in DeadDrop.DeadDrops)
        {
            if (drop?.Storage == null)
            {
                continue;
            }

            string dropGuid = drop.GUID.ToString();
            if (activeDropGuids.Contains(dropGuid))
            {
                if (scope.DeaddropStorageDataByDropGuid.TryGetValue(dropGuid, out string? storageJson)
                    && !string.IsNullOrWhiteSpace(storageJson)
                    && TryDeserializeWorldStorage(storageJson, out WorldStorageEntityData? storageData))
                {
                    drop.Storage.Load(storageData!);
                }
                else
                {
                    drop.Storage.ClearContents();
                }

                continue;
            }

            if (globallyScopedDropGuids.Contains(dropGuid))
            {
                drop.Storage.ClearContents();
            }
        }
    }

    private HashSet<string> GetGloballyActiveDeaddropGuids()
    {
        HashSet<string> activeDropGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (QuestScopeRecord scope in _repository.Current.QuestScopes.Values)
        {
            foreach (ScopedDeaddropQuestSyncDto quest in DeserializeDeaddropList(scope.DeaddropQuestDataJson))
            {
                if (!string.IsNullOrWhiteSpace(quest.DeaddropGuid)
                    && string.Equals(quest.State, EQuestState.Active.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    activeDropGuids.Add(quest.DeaddropGuid);
                }
            }
        }

        return activeDropGuids;
    }

    private HashSet<string> GetGloballyUnavailableDeaddropGuids()
    {
        HashSet<string> unavailableDropGuids = GetGloballyActiveDeaddropGuids();
        unavailableDropGuids.UnionWith(GetPendingSupplierDeaddropReservationGuids());
        return unavailableDropGuids;
    }

    private List<string> GetPendingSupplierDeaddropReservationGuids()
    {
        List<string> deaddropGuids = new List<string>();
        foreach (PhysicalWorldReservationRecord reservation in _repository.Current.PhysicalWorldReservations.Values)
        {
            if (reservation == null
                || string.IsNullOrWhiteSpace(reservation.ReservationId)
                || !reservation.ReservationId.StartsWith(PendingSupplierDeaddropReservationPrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            deaddropGuids.Add(reservation.ReservationId.Substring(PendingSupplierDeaddropReservationPrefix.Length));
        }

        return deaddropGuids;
    }

    private bool TryReservePendingSupplierDeaddrop(string ownerKey, string deaddropGuid)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return false;
        }

        string reservationId = BuildPendingSupplierDeaddropReservationId(deaddropGuid);
        if (_repository.Current.PhysicalWorldReservations.TryGetValue(reservationId, out PhysicalWorldReservationRecord? existing))
        {
            return string.Equals(existing.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
        }

        _repository.Current.PhysicalWorldReservations[reservationId] = new PhysicalWorldReservationRecord
        {
            ReservationId = reservationId,
            OwnerKey = ownerKey,
            ReservedAtUtc = DateTime.UtcNow,
        };
        _repository.MarkDirty();
        return true;
    }

    private bool TryGetPendingSupplierDeaddropReservationOwner(string deaddropGuid, out string? ownerKey)
    {
        ownerKey = null;
        if (string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return false;
        }

        string reservationId = BuildPendingSupplierDeaddropReservationId(deaddropGuid);
        if (!_repository.Current.PhysicalWorldReservations.TryGetValue(reservationId, out PhysicalWorldReservationRecord? reservation)
            || string.IsNullOrWhiteSpace(reservation.OwnerKey))
        {
            return false;
        }

        ownerKey = reservation.OwnerKey;
        return true;
    }

    private bool ReleasePendingSupplierDeaddropReservation(string deaddropGuid)
    {
        if (string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return false;
        }

        return _repository.Current.PhysicalWorldReservations.Remove(BuildPendingSupplierDeaddropReservationId(deaddropGuid));
    }

    private static string BuildPendingSupplierDeaddropReservationId(string deaddropGuid)
    {
        return PendingSupplierDeaddropReservationPrefix + deaddropGuid;
    }

    private List<string> BuildInaccessibleDeaddropGuids(string ownerKey)
    {
        HashSet<string> inaccessibleDropGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string deaddropGuid in GetPendingSupplierDeaddropReservationGuids())
        {
            if (TryGetPendingSupplierDeaddropReservationOwner(deaddropGuid, out string? pendingOwnerKey)
                && !string.Equals(pendingOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
            {
                inaccessibleDropGuids.Add(deaddropGuid);
            }
        }

        foreach (QuestScopeRecord scope in _repository.Current.QuestScopes.Values)
        {
            if (scope == null || string.Equals(scope.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (ScopedDeaddropQuestSyncDto quest in DeserializeDeaddropList(scope.DeaddropQuestDataJson))
            {
                if (!string.IsNullOrWhiteSpace(quest.DeaddropGuid)
                    && string.Equals(quest.State, EQuestState.Active.ToString(), StringComparison.OrdinalIgnoreCase))
                {
                    inaccessibleDropGuids.Add(quest.DeaddropGuid);
                }
            }
        }

        return inaccessibleDropGuids.ToList();
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

    private static List<ScopedQuestSyncDto> DeserializeQuestList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ScopedQuestSyncDto>();
        }

        List<ScopedQuestSyncDto>? quests = JsonConvert.DeserializeObject<List<ScopedQuestSyncDto>>(json);
        return quests ?? new List<ScopedQuestSyncDto>();
    }

    private static List<ScopedDeaddropQuestSyncDto> DeserializeDeaddropList(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<ScopedDeaddropQuestSyncDto>();
        }

        List<ScopedDeaddropQuestSyncDto>? quests = JsonConvert.DeserializeObject<List<ScopedDeaddropQuestSyncDto>>(json);
        return quests ?? new List<ScopedDeaddropQuestSyncDto>();
    }

    private bool RecordDeaddropAction(string steamId, string guid, QuestManager.EQuestAction action)
    {
        return action switch
        {
            QuestManager.EQuestAction.Begin => RecordDeaddropState(steamId, guid, EQuestState.Active),
            QuestManager.EQuestAction.Success => RecordDeaddropState(steamId, guid, EQuestState.Completed),
            QuestManager.EQuestAction.Fail => RecordDeaddropState(steamId, guid, EQuestState.Failed),
            QuestManager.EQuestAction.Expire => RecordDeaddropState(steamId, guid, EQuestState.Expired),
            QuestManager.EQuestAction.Cancel => RecordDeaddropState(steamId, guid, EQuestState.Cancelled),
            _ => false,
        };
    }

    private bool RecordDeaddropState(string steamId, string guid, EQuestState state)
    {
        string ownerKey = ResolveOwnerKey(steamId);
        bool recorded = UpdateDeaddrop(ownerKey, guid, quest =>
        {
            quest.State = state.ToString();
        });

        if (recorded)
        {
            SynchronizeHydratedWorld(ownerKey);
        }

        return recorded;
    }

    private bool RecordDeaddropEntryState(string steamId, string guid, int entryIndex, EQuestState state)
    {
        string ownerKey = ResolveOwnerKey(steamId);
        bool recorded = UpdateDeaddrop(ownerKey, guid, quest =>
        {
            if (entryIndex >= 0 && entryIndex < quest.Entries.Count)
            {
                quest.Entries[entryIndex].State = state.ToString();
            }
        });

        if (recorded)
        {
            SynchronizeHydratedWorld(ownerKey);
        }

        return recorded;
    }

    private bool RecordDeaddropTracking(string steamId, string guid, bool tracked)
    {
        string ownerKey = ResolveOwnerKey(steamId);
        bool recorded = UpdateDeaddrop(ownerKey, guid, quest =>
        {
            quest.IsTracked = tracked;
        });

        if (recorded)
        {
            SynchronizeHydratedWorld(ownerKey);
        }

        return recorded;
    }

    private bool UpdateDeaddrop(string ownerKey, string guid, Action<ScopedDeaddropQuestSyncDto> update)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(guid))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        List<ScopedDeaddropQuestSyncDto> deaddrops = DeserializeDeaddropList(scope.DeaddropQuestDataJson);
        ScopedDeaddropQuestSyncDto? quest = deaddrops.FirstOrDefault(item => string.Equals(item.Guid, guid, StringComparison.OrdinalIgnoreCase));
        if (quest == null)
        {
            if (!Guid.TryParse(guid, out Guid parsedGuid))
            {
                return false;
            }

            DeaddropQuest? liveQuest = GUIDManager.GetObject<DeaddropQuest>(parsedGuid);
            if (liveQuest?.Drop == null)
            {
                return false;
            }

            quest = CreateDeaddropSync(liveQuest);
            deaddrops.Add(quest);
        }

        update(quest);
        scope.DeaddropQuestDataJson = JsonConvert.SerializeObject(deaddrops);
        CaptureDeaddropStorage(scope, deaddrops);
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    private static void CaptureDeaddropStorage(QuestScopeRecord scope, List<ScopedDeaddropQuestSyncDto> deaddrops)
    {
        foreach (ScopedDeaddropQuestSyncDto deaddrop in deaddrops)
        {
            if (deaddrop == null
                || string.IsNullOrWhiteSpace(deaddrop.DeaddropGuid)
                || !Guid.TryParse(deaddrop.DeaddropGuid, out Guid parsedGuid))
            {
                continue;
            }

            DeadDrop? drop = GUIDManager.GetObject<DeadDrop>(parsedGuid);
            if (drop?.Storage == null)
            {
                continue;
            }

            WorldStorageEntityData storageData = drop.Storage.GetSaveData();
            scope.DeaddropStorageDataByDropGuid[deaddrop.DeaddropGuid] = storageData.GetJson(prettyPrint: false);
        }
    }

    private static bool TryDeserializeWorldStorage(string json, out WorldStorageEntityData? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            data = JsonConvert.DeserializeObject<WorldStorageEntityData>(json);
            return data?.Contents != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool TryDeserializeWorldSpraySurface(string json, out WorldSpraySurfaceData? data)
    {
        data = null;
        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            data = JsonConvert.DeserializeObject<WorldSpraySurfaceData>(json);
            return data?.Strokes != null;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool StorageContainsItems(WorldStorageEntityData data)
    {
        if (data?.Contents?.Items == null)
        {
            return false;
        }

        foreach (string itemJson in data.Contents.Items)
        {
            if (string.IsNullOrWhiteSpace(itemJson))
            {
                continue;
            }

            try
            {
                ItemData? item = JsonConvert.DeserializeObject<ItemData>(itemJson);
                if (item != null && !string.IsNullOrWhiteSpace(item.ID) && item.Quantity > 0)
                {
                    return true;
                }
            }
            catch (JsonException)
            {
                continue;
            }
        }

        return false;
    }

    private static ScopedQuestSyncDto? FindOrCreateQuestSync(List<ScopedQuestSyncDto> quests, string guid)
    {
        ScopedQuestSyncDto? scopedQuest = quests.FirstOrDefault(item => string.Equals(item.Guid, guid, StringComparison.OrdinalIgnoreCase));
        if (scopedQuest != null)
        {
            return scopedQuest;
        }

        if (!Guid.TryParse(guid, out Guid parsedGuid))
        {
            return null;
        }

        Quest? liveQuest = GUIDManager.GetObject<Quest>(parsedGuid);
        if (!QuestScopeRules.ShouldVirtualizeQuest(liveQuest))
        {
            return null;
        }

        scopedQuest = CreateQuestSync(liveQuest!);
        quests.Add(scopedQuest);
        return scopedQuest;
    }

    private static void SyncLiveQuestFields(ScopedQuestSyncDto scopedQuest, string guid)
    {
        if (!Guid.TryParse(guid, out Guid parsedGuid))
        {
            return;
        }

        Quest? liveQuest = GUIDManager.GetObject<Quest>(parsedGuid);
        if (liveQuest == null)
        {
            return;
        }

        scopedQuest.Title = liveQuest.Title;
        scopedQuest.Description = liveQuest.Description;
        scopedQuest.Expires = liveQuest.Expires;
        scopedQuest.ExpiryElapsedDays = liveQuest.Expiry.elapsedDays;
        scopedQuest.ExpiryTime = liveQuest.Expiry.time;
    }

    private List<ScopedContractSyncDto> BuildScopedContracts(string ownerKey)
    {
        return _repository.Current.ScopedContracts.Values
            .Where(record => string.Equals(record.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
            .Where(record => string.Equals(record.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .Where(record => !string.IsNullOrWhiteSpace(record.ContractDataJson))
            .Select(record => JsonConvert.DeserializeObject<ContractData>(record.ContractDataJson))
            .Where(data => data != null)
            .Select(data => new ScopedContractSyncDto
            {
                Guid = data!.GUID,
                Title = data.Title,
                Description = data.Description,
                State = data.State.ToString(),
                IsTracked = data.IsTracked,
                Expires = data.Expires,
                ExpiryElapsedDays = data.ExpiryDate?.ElapsedDays ?? 0,
                ExpiryTime = data.ExpiryDate?.Time ?? 0,
                Entries = data.Entries.Select(entry => new ScopedQuestEntrySyncDto
                {
                    Name = entry.Name,
                    State = entry.State.ToString(),
                }).ToList(),
                CustomerGuid = data.CustomerGUID,
                Payment = data.Payment,
                ProductListJson = JsonConvert.SerializeObject(data.ProductList),
                DeliveryLocationGuid = data.DeliveryLocationGUID,
                DeliveryWindowEnabled = data.DeliveryWindow?.IsEnabled ?? false,
                DeliveryWindowStartTime = data.DeliveryWindow?.WindowStartTime ?? 0,
                DeliveryWindowEndTime = data.DeliveryWindow?.WindowEndTime ?? 0,
                PickupScheduleIndex = data.PickupScheduleIndex,
                AcceptElapsedDays = data.AcceptTime?.ElapsedDays ?? 0,
                AcceptTime = data.AcceptTime?.Time ?? 0,
            })
            .ToList();
    }

    private static List<ScopedDeaddropQuestSyncDto> BuildScopedDeaddrops(QuestScopeRecord scope)
    {
        return DeserializeDeaddropList(scope.DeaddropQuestDataJson)
            .Where(quest => string.Equals(quest.State, EQuestState.Active.ToString(), StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    public bool CanOwnerAccessDeaddrop(string ownerKey, string deaddropGuid)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return true;
        }

        if (TryGetPendingSupplierDeaddropReservationOwner(deaddropGuid, out string? pendingOwnerKey))
        {
            return string.Equals(pendingOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
        }

        if (TryGetActiveDeaddropOwner(deaddropGuid, out string activeOwnerKey, out bool isAmbiguous))
        {
            return string.Equals(activeOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
        }

        if (isAmbiguous)
        {
            return false;
        }

        return true;
    }

    public bool TryGetActiveDeaddropOwner(string deaddropGuid, out string ownerKey)
    {
        return TryGetActiveDeaddropOwner(deaddropGuid, out ownerKey, out _);
    }

    public bool TryGetActiveDeaddropOwner(string deaddropGuid, out string ownerKey, out bool isAmbiguous)
    {
        ownerKey = string.Empty;
        isAmbiguous = false;
        if (string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return false;
        }

        foreach (QuestScopeRecord scope in _repository.Current.QuestScopes.Values)
        {
            bool ownsActiveDrop = DeserializeDeaddropList(scope.DeaddropQuestDataJson).Any(quest =>
                string.Equals(quest.DeaddropGuid, deaddropGuid, StringComparison.OrdinalIgnoreCase)
                && string.Equals(quest.State, EQuestState.Active.ToString(), StringComparison.OrdinalIgnoreCase));
            if (!ownsActiveDrop)
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(ownerKey))
            {
                ownerKey = string.Empty;
                isAmbiguous = true;
                return false;
            }

            ownerKey = scope.OwnerKey;
        }

        return !string.IsNullOrWhiteSpace(ownerKey);
    }

    public bool RecordOwnerDeaddropStorage(string ownerKey, string deaddropGuid)
    {
        if (IsRuntimeCaptureSuppressed || string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        List<ScopedDeaddropQuestSyncDto> deaddrops = DeserializeDeaddropList(scope.DeaddropQuestDataJson);
        bool ownsActiveDrop = deaddrops.Any(quest =>
            string.Equals(quest.DeaddropGuid, deaddropGuid, StringComparison.OrdinalIgnoreCase)
            && string.Equals(quest.State, EQuestState.Active.ToString(), StringComparison.OrdinalIgnoreCase));
        if (!ownsActiveDrop)
        {
            return false;
        }

        CaptureDeaddropStorage(scope, deaddropGuid);
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    public bool TryGetRandomAvailableDeaddrop(Vector3 origin, out DeadDrop? drop)
    {
        return TryGetRandomAvailableDeaddrop(origin, null, out drop);
    }

    public bool TryGetRandomAvailableDeaddrop(Vector3 origin, string? ownerKey, out DeadDrop? drop)
    {
        drop = null;
        HashSet<string> globallyScopedDropGuids = GetGloballyUnavailableDeaddropGuids();
        List<DeadDrop> candidates = DeadDrop.DeadDrops.AsManagedEnumerable()
            .Where(candidate => candidate?.Storage != null
                && candidate.Storage.ItemCount == 0
                && !globallyScopedDropGuids.Contains(candidate.GUID.ToString()))
            .OrderBy(candidate => Vector3.Distance(candidate.transform.position, origin))
            .ToList();

        if (candidates.Count == 0)
        {
            return false;
        }

        candidates.RemoveAt(0);
        if (candidates.Count == 0)
        {
            return false;
        }

        candidates.RemoveRange(candidates.Count / 2, candidates.Count / 2);
        if (candidates.Count == 0)
        {
            return false;
        }

        drop = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        if (drop != null && !string.IsNullOrWhiteSpace(ownerKey))
        {
            return TryReservePendingSupplierDeaddrop(ownerKey, drop.GUID.ToString());
        }

        return drop != null;
    }

    public bool TryPrepareCartelDeadDropTheft(EMapRegion region, string ownerKey, out DeadDrop? drop)
    {
        drop = null;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        float nowMinutes = NetworkSingleton<TimeManager>.Instance.GetDateTime().GetMinSum();
        QuestScopeRecord scope = EnsureScope(ownerKey);
        foreach (ScopedDeaddropQuestSyncDto quest in DeserializeDeaddropList(scope.DeaddropQuestDataJson))
        {
            if (quest == null
                || !string.Equals(quest.State, EQuestState.Active.ToString(), StringComparison.OrdinalIgnoreCase)
                || string.IsNullOrWhiteSpace(quest.DeaddropGuid)
                || !Guid.TryParse(quest.DeaddropGuid, out Guid parsedGuid))
            {
                continue;
            }

            DeadDrop? candidate = GUIDManager.GetObject<DeadDrop>(parsedGuid);
            if (candidate?.Storage == null || candidate.Region != region)
            {
                continue;
            }

            ExecuteWithoutRuntimeCapture(() =>
            {
                if (scope.DeaddropStorageDataByDropGuid.TryGetValue(quest.DeaddropGuid, out string? storageJson)
                    && !string.IsNullOrWhiteSpace(storageJson)
                    && TryDeserializeWorldStorage(storageJson, out WorldStorageEntityData? storageData))
                {
                    candidate.Storage.Load(storageData!);
                }
                else
                {
                    candidate.Storage.ClearContents();
                }
            });

            if (candidate.Storage.ItemCount < 2
                || nowMinutes - candidate.Storage.LastContentChangeTime.GetMinSum() < 360f
                || candidate.Storage.GetAllItems().AsManagedEnumerable().Sum(item => GetItemMonetaryValue(item)) < 200f)
            {
                continue;
            }

            drop = candidate;
            return true;
        }

        return false;
    }

    public bool RecordCartelDeadDropTheft(string ownerKey, string deaddropGuid)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return false;
        }

        QuestScopeRecord scope = EnsureScope(ownerKey);
        CaptureDeaddropStorage(scope, deaddropGuid);
        scope.UpdatedAtUtc = DateTime.UtcNow;
        _repository.MarkDirty();
        return true;
    }

    private static void CaptureDeaddropStorage(QuestScopeRecord scope, string deaddropGuid)
    {
        if (string.IsNullOrWhiteSpace(deaddropGuid) || !Guid.TryParse(deaddropGuid, out Guid parsedGuid))
        {
            return;
        }

        DeadDrop? drop = GUIDManager.GetObject<DeadDrop>(parsedGuid);
        if (drop?.Storage == null)
        {
            return;
        }

        WorldStorageEntityData storageData = drop.Storage.GetSaveData();
        scope.DeaddropStorageDataByDropGuid[deaddropGuid] = storageData.GetJson(prettyPrint: false);
    }

    private static float GetItemMonetaryValue(object item)
    {
        if (item == null)
        {
            return 0f;
        }

        object? definition = AccessTools.Property(item.GetType(), "Definition")?.GetValue(item, null);
        if (definition == null)
        {
            return 0f;
        }

        float quantity = GetNumericMember(item, "Quantity", 1f);
        if (item.GetType().Name.IndexOf("ProductItemInstance", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return GetNumericMember(definition, "MarketValue", 0f) * quantity * GetNumericMember(item, "Amount", 1f);
        }

        return GetNumericMember(definition, "BasePurchasePrice", 0f) * quantity;
    }

    private static float GetNumericMember(object target, string memberName, float fallback)
    {
        Type type = target.GetType();
        object? value = AccessTools.Property(type, memberName)?.GetValue(target, null)
            ?? AccessTools.Field(type, memberName)?.GetValue(target);
        if (value == null)
        {
            return fallback;
        }

        try
        {
            return Convert.ToSingle(value);
        }
        catch (Exception)
        {
            return fallback;
        }
    }

    private string ResolveOwnerKey(string steamId) => _organisationService.ResolveOwnerKey(steamId);

    private static EQuestState ParseQuestState(string state)
    {
        return Enum.TryParse(state, out EQuestState parsed) ? parsed : EQuestState.Inactive;
    }

    private static string BuildPlayerOwnerKey(string steamId) => $"player:{steamId}";
    private static string BuildOrganisationOwnerKey(string organisationId) => $"org:{organisationId}";
}
#endif
