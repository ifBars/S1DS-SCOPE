#if SERVER
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.ExceptionServices;
using DedicatedServerMod.API;
using DedicatedServerMod.Organisations.Configuration;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Persistence;
using DedicatedServerMod.Organisations.Services;
using DedicatedServerMod.Organisations.Utils;
using DedicatedServerMod.Server.Player;
using DedicatedServerMod.Shared.Networking;
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.GameTime;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Police;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Phone.Messages;
using Il2CppScheduleOne.UI.Shop;
using Il2CppScheduleOne.Variables;
using Il2CppScheduleOne.Vehicles;
using CharacterRay = Il2CppScheduleOne.NPCs.CharacterClasses.Ray;
using ECartelStatus = Il2Cpp.ECartelStatus;
#else
using FishNet.Connection;
using FishNet.Object;
using ScheduleOne.Cartel;
using ScheduleOne.Delivery;
using ScheduleOne.Dialogue;
using ScheduleOne.Economy;
using ScheduleOne.DevUtilities;
using ScheduleOne.Employees;
using ScheduleOne.Graffiti;
using ScheduleOne.GameTime;
using ScheduleOne.ItemFramework;
using ScheduleOne.Map;
using ScheduleOne.Messaging;
using ScheduleOne.Money;
using ScheduleOne.NPCs;
using ScheduleOne.NPCs.CharacterClasses;
using ScheduleOne.ObjectScripts;
using ScheduleOne.Persistence;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Police;
using ScheduleOne.Property;
using ScheduleOne.Quests;
using ScheduleOne.UI.Phone.Messages;
using ScheduleOne.UI.Shop;
using ScheduleOne.Variables;
using ScheduleOne.Vehicles;
using CharacterRay = ScheduleOne.NPCs.CharacterClasses.Ray;
#endif
using MelonLoader;
using Newtonsoft.Json;
using UnityEngine;

[assembly: MelonInfo(typeof(DedicatedServerMod.Organisations.Server.OrganisationsServerMod), DedicatedServerMod.Organisations.Constants.ModName, DedicatedServerMod.Organisations.Constants.ModVersion, DedicatedServerMod.Organisations.Constants.ModAuthor)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace DedicatedServerMod.Organisations.Server;

public sealed class OrganisationsServerMod : ServerMelonModBase
{
    internal static OrganisationsServerMod? ActiveInstance { get; private set; }

    private const int MaxQuestScopeSyncPayloadChars = 4000;
    private const int MaxCustomerScopeSyncPayloadChars = 4000;
    private const int MaxCustomerScopeSyncMessagesPerFlush = 1;
    private static readonly TimeSpan QuestScopeSyncDebounce = TimeSpan.FromMilliseconds(100);
    private static readonly TimeSpan InitialQuestScopeSyncDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan InitialCustomerScopeSyncDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan QuestScopeSyncSendInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CustomerScopeSyncSendInterval = TimeSpan.FromMilliseconds(250);
    private static readonly TimeSpan CartelAmbushInfluenceOwnerTtl = TimeSpan.FromMinutes(Ambush.CANCEL_AMBUSH_AFTER_MINS);
    private const string RandomWorldSewerKeyReservationId = "sewer:random_world_key";
    private static readonly MethodInfo? AmbushSpawnAmbushMethod = typeof(Ambush).GetMethod("SpawnAmbush", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? CartelActivityDeactivateMethod = typeof(CartelActivity).GetMethod("Deactivate", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CartelActivityMinsSinceActivationField = typeof(CartelActivity).GetField("<MinsSinceActivation>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? AmbushRegionActivitiesField = typeof(Ambush).GetField("_regionActivities", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? SprayGraffitiValidSpraySurfaceField = typeof(SprayGraffiti).GetField("_validSpraySurface", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? SprayGraffitiMinimumDistanceFromPlayersField = typeof(SprayGraffiti).GetField("_minimumDistanceFromPlayers", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? CartelDealActiveDealField = typeof(CartelDealManager).GetField("<ActiveDeal>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? CartelDealSendExpiryMessageMethod = typeof(CartelDealManager).GetMethod("SendExpiryMessage", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? CartelActivitiesStartGlobalActivityMethod = typeof(CartelActivities).GetMethod("StartGlobalActivity", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? CartelRegionActivitiesStartActivityMethod = typeof(CartelRegionActivities).GetMethod("StartActivity", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NetworkConnection), typeof(int) }, null);
    private static readonly MethodInfo? CartelGoonSpawnClientMethod = typeof(CartelGoon).GetMethod("Spawn_Client", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NetworkConnection) }, null);
    private static readonly MethodInfo? CartelGoonSpawnClientLogicMethod = typeof(CartelGoon).GetMethod("RpcLogic___Spawn_Client_328543758", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? CartelGoonConfigureSettingsMethod = typeof(CartelGoon).GetMethod("ConfigureGoonSettings", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NetworkConnection), typeof(CartelGoonAppearance), typeof(float) }, null);
    private static readonly MethodInfo? CartelGoonConfigureSettingsLogicMethod = typeof(CartelGoon).GetMethod("RpcLogic___ConfigureGoonSettings_3427656873", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? CartelDealerConfigureSettingsMethod = typeof(CartelDealer).GetMethod("ConfigureGoonSettings", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NetworkConnection), typeof(CartelGoonAppearance), typeof(float) }, null);
    private static readonly MethodInfo? CartelDealerConfigureSettingsLogicMethod = typeof(CartelDealer).GetMethod("RpcLogic___ConfigureGoonSettings_3427656873", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? NpcInventorySetStoredInstanceMethod = typeof(NPCInventory).GetMethod("SetStoredInstance_Internal", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NetworkConnection), typeof(int), typeof(ItemInstance) }, null);
    private static readonly MethodInfo? NpcInventorySetStoredInstanceLogicMethod = typeof(NPCInventory).GetMethod("RpcLogic___SetStoredInstance_Internal_2652194801", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? NpcInventorySetItemSlotQuantityLogicMethod = typeof(NPCInventory).GetMethod("RpcLogic___SetItemSlotQuantity_Internal_1692629761", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? NpcInventorySetSlotLockedMethod = typeof(NPCInventory).GetMethod("SetSlotLocked_Internal", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NetworkConnection), typeof(int), typeof(bool), typeof(NetworkObject), typeof(string) }, null);
    private static readonly MethodInfo? NpcInventorySetSlotLockedLogicMethod = typeof(NPCInventory).GetMethod("RpcLogic___SetSlotLocked_Internal_3170825843", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? NpcInventorySetSlotFilterMethod = typeof(NPCInventory).GetMethod("SetSlotFilter_Internal", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(NetworkConnection), typeof(int), typeof(SlotFilter) }, null);
    private static readonly MethodInfo? NpcInventorySetSlotFilterLogicMethod = typeof(NPCInventory).GetMethod("RpcLogic___SetSlotFilter_Internal_527532783", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? MessagingManagerReceiveConversationDataMethod = typeof(MessagingManager).GetMethod("ReceiveMSGConversationData", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo? SupplierMinsSinceMeetingStartField = typeof(Supplier).GetField("minsSinceMeetingStart", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? DarkMarketSetUnlockedMethod = typeof(DarkMarket).GetMethod("SetUnlocked", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly MethodInfo? SewerManagerSetSewerUnlockedClientMethod = typeof(SewerManager).GetMethod("SetSewerUnlocked_Client", BindingFlags.Instance | BindingFlags.NonPublic);

    private readonly EventSubscriptionHub _subscriptions = new EventSubscriptionHub();
    private readonly Dictionary<string, DateTime> _pendingQuestScopeSyncs = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, DateTime> _pendingCustomerScopeSyncs = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastSnapshotPayloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastQuestScopeSyncPayloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastCustomerScopeSyncPayloads = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly List<PendingScopeSyncMessage> _pendingQuestScopeSyncMessages = new List<PendingScopeSyncMessage>();
    private readonly List<PendingCustomerScopeSyncMessage> _pendingCustomerScopeSyncMessages = new List<PendingCustomerScopeSyncMessage>();
    private DateTime _pendingQuestScopeSyncMessagesDueAtUtc;
    private DateTime _pendingCustomerScopeSyncsDueAtUtc;
    private readonly HashSet<string> _notifiedCompletedDeaddropQuestGuids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pendingSupplierDeaddropOwnersBySupplierId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _activeSupplierMeetingOwnersBySupplierId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, SupplierMeetingScopeRecord> _activeSupplierMeetingsBySupplierAndOwner = new Dictionary<string, SupplierMeetingScopeRecord>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastSupplierStashMutationOwnersBySupplierId = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<EMapRegion, string> _activeCartelGlobalActivityOwnersByRegion = new Dictionary<EMapRegion, string>();
    private readonly Dictionary<EMapRegion, string> _activeCartelRegionalActivityOwnersByRegion = new Dictionary<EMapRegion, string>();
    private readonly Dictionary<EMapRegion, PendingCartelAmbushInfluenceOwner> _pendingCartelAmbushInfluenceOwnersByRegion = new Dictionary<EMapRegion, PendingCartelAmbushInfluenceOwner>();
    private readonly Dictionary<EMapRegion, string> _pendingCartelDeadDropTheftOwnersByRegion = new Dictionary<EMapRegion, string>();
    private readonly Dictionary<EMapRegion, string> _pendingCartelDeadDropTheftDropsByRegion = new Dictionary<EMapRegion, string>();
    private readonly Dictionary<CartelGoon, string> _activeCartelGoonOwners = new Dictionary<CartelGoon, string>();
    private string? _activeSupplierDeaddropCompletionOwnerKey;
    private string? _activeDeaddropCollectionOwnerKey;
    private string? _activeSupplierDebtRecoveryOwnerKey;
    private string? _pendingCartelDealOwnerKey;
    private string? _activeCartelDealOwnerKey;
    private string? _activeCallerCartelStatusOwnerKey;
    private string? _activeDealerSaleOwnerKey;
    private string? _activeDeaddropItemCountVariableOwnerKey;
    private string? _activeDeaddropItemCountVariableName;
    private string? _activeDealerSaleDealerId;
    private string? _activeCartelGoonSpawnOwnerKey;
    private DateTime _pendingQuestWorldMutationDueAtUtc = DateTime.MinValue;
    private bool _questWorldMutationPending;
    private OrganisationLogger _logger = null!;
    private OrganisationServerConfig _config = null!;
    private FileOrganisationRepository _repository = null!;
    private IOrganisationService _organisationService = null!;
    private OrganisationQuestScopeService _questScopeService = null!;
    private OrganisationPropertyScopeService _propertyScopeService = null!;
    private OrganisationVehicleAccessService _vehicleAccessService = null!;
    private OrganisationCustomerContractService _customerContractService = null!;
    private string? _lastExceptionSignature;
    private DateTime _lastExceptionAtUtc = DateTime.MinValue;
    private int _supplierMeetingReplayDepth;
    private int _cartelDefeatStatusSuppressionDepth;
    private bool _callerCartelInfluenceMutationPrepared;
    private bool _suppressNextCartelInfluenceCapture;
    private bool _suppressQuestVariableMutationNotification;
    private bool _runtimeHooksInstalled;
    private bool _skipQuestScopeSyncSend;
    private bool _logQuestVariableDiagnostics;
    private bool _logDeaddropDiagnostics;
    private int _maxQuestScopeChunksToSend = int.MaxValue;
    private bool _isInitialized;

    public override void OnInitializeMelon()
    {
        EnsureRuntimeDependencies();
        _logger.Info("Melon initialized.");
    }

    public override void OnServerInitialize()
    {
        EnsureRuntimeDependencies();

        if (_isInitialized)
        {
            _logger.Info("Server mod already initialized; skipping duplicate initialization.");
            return;
        }

        ActiveInstance = this;
        InstallRuntimeHooks();
        _subscriptions.Add(
            () => CustomMessaging.ServerMessageReceived += OnServerMessageReceived,
            () => CustomMessaging.ServerMessageReceived -= OnServerMessageReceived);
        _isInitialized = true;
        _logger.Info("Server mod initialized.");
    }

    public override void OnServerShutdown()
    {
        EnsureRuntimeDependencies();

        if (!_isInitialized)
        {
            return;
        }

        RemoveRuntimeHooks();
        _subscriptions.Clear();
        _pendingQuestScopeSyncs.Clear();
        _pendingCustomerScopeSyncs.Clear();
        _lastSnapshotPayloads.Clear();
        _lastQuestScopeSyncPayloads.Clear();
        _lastCustomerScopeSyncPayloads.Clear();
        _pendingQuestScopeSyncMessages.Clear();
        _pendingCustomerScopeSyncMessages.Clear();
        _pendingQuestScopeSyncMessagesDueAtUtc = DateTime.MinValue;
        _pendingCustomerScopeSyncsDueAtUtc = DateTime.MinValue;
        _notifiedCompletedDeaddropQuestGuids.Clear();
        ClearTransientRuntimeScopeState();
        _isInitialized = false;

        if (ReferenceEquals(ActiveInstance, this))
        {
            ActiveInstance = null;
        }
    }

    public override void OnBeforeSave()
    {
        EnsureRuntimeDependencies();
        EnsureRepositoryRegistered("before save");
        _questScopeService.CaptureCurrentWorldStateForDeterministicScope();
        _propertyScopeService.CaptureCurrentWorldStateForDeterministicScope();
        _logger.Info("Marking organisation repository dirty before save.");
        _repository.MarkDirty();
    }

    public override void OnBeforeLoad()
    {
        EnsureRuntimeDependencies();
        EnsureRepositoryRegistered("before load");
        ClearTransientRuntimeScopeState();
        _logger.Info("Organisation repository prepared for load cycle.");
    }

    public override void OnAfterLoad()
    {
        EnsureRuntimeDependencies();
        EnsureRepositoryRegistered("after load");
#if IL2CPP
        LoadRepositoryFromActiveSave("after load");
#endif
        _logger.Info($"Organisation repository ready after load. Organisations={_repository.Current.Organisations.Count}, PendingInvites={_repository.Current.PendingInvites.Count}.");
    }

    public override void OnUpdate()
    {
        EnsureRuntimeDependencies();
        FlushPendingQuestWorldMutation();
        FlushPendingQuestScopeSyncs();
        FlushPendingQuestScopeSyncMessages();
        FlushPendingCustomerScopeSyncs();
        FlushPendingCustomerScopeSyncMessages();
    }

    private void OnServerMessageReceived(NetworkConnection connection, string command, string data)
    {
        try
        {
            if (!IsOrganisationCommand(command))
            {
                return;
            }

            if (!TryResolveCaller(connection, out PlayerIdentity identity))
            {
                _logger.Warning($"Failed to resolve caller for organisation command '{command}'.");
                SendError(connection, "Player identity is not ready yet.");
                return;
            }

            _logger.Info($"Received organisation command '{command}' from {identity.PlayerName} ({identity.SteamId}).");
            _organisationService.EnsureConfiguredTeamMembership(identity);

            switch (command)
            {
                case OrganisationMessages.SnapshotRequest:
                    _questScopeService.TryHydrateWorldForPlayer(identity.SteamId);
                    _propertyScopeService.TryHydrateWorldForPlayer(identity.SteamId);
                    SendSnapshot(connection, identity.SteamId, sendScopeSyncs: false);
                    QueueInitialScopeSyncs(identity.SteamId);
                    break;

                case OrganisationMessages.CreateRequest:
                    HandleCreateRequest(connection, identity, data);
                    break;

                case OrganisationMessages.InviteRequest:
                    HandleInviteRequest(connection, identity, data);
                    break;

                case OrganisationMessages.InviteAcceptRequest:
                    HandleInviteAcceptRequest(connection, identity, data);
                    break;

                case OrganisationMessages.InviteDeclineRequest:
                    HandleInviteDeclineRequest(connection, identity, data);
                    break;

                case OrganisationMessages.LeaveRequest:
                    HandleLeaveRequest(connection, identity);
                    break;

                case OrganisationMessages.KickRequest:
                    HandleKickRequest(connection, identity, data);
                    break;

                case OrganisationMessages.TransferOwnershipRequest:
                    HandleTransferOwnershipRequest(connection, identity, data);
                    break;

                case OrganisationMessages.OnboardingPromptSeenRequest:
                    HandleOnboardingPromptSeenRequest(identity);
                    break;

                case OrganisationMessages.QuestTrackingRequest:
                    HandleQuestTrackingRequest(connection, identity, data);
                    break;

                case OrganisationMessages.CustomerOfferRejectRequest:
                    HandleCustomerOfferRejectRequest(connection, identity, data);
                    break;

                case OrganisationMessages.AtmTransactionRequest:
                    HandleAtmTransactionRequest(connection, identity, data);
                    break;

                case OrganisationMessages.ShopCheckoutRequest:
                    HandleShopCheckoutRequest(connection, identity, data);
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Unhandled server message error", ex);
            SendError(connection, "The organisation server hit an unexpected error.");
        }
    }

    private bool TryResolveCaller(NetworkConnection connection, out PlayerIdentity identity)
    {
        identity = new PlayerIdentity();
        ConnectedPlayerInfo? player = S1DS.Server.Players?.GetPlayer(connection);
        if (player == null)
        {
            return false;
        }

        string steamId = ResolveSteamId(player);
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return false;
        }

        identity = new PlayerIdentity
        {
            SteamId = steamId,
            PlayerName = player.PlayerName ?? player.DisplayName,
        };

        return true;
    }

    private void HandleCreateRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        CreateOrganisationRequestDto? request = Deserialize<CreateOrganisationRequestDto>(data);
        if (request == null)
        {
            _logger.Warning($"Received invalid create organisation payload from {identity.SteamId}.");
            SendError(connection, "Invalid create organisation request.");
            return;
        }

        _questScopeService.EnsurePersonalScopeCaptured(identity.SteamId);
        _propertyScopeService.EnsurePersonalScopeCaptured(identity.SteamId);

        if (!_organisationService.TryCreateOrganisation(identity, request.Name, out OrganisationSnapshotDto snapshot, out string error))
        {
            _logger.Warning($"Organisation create request failed for {identity.SteamId}: {error}");
            SendError(connection, error);
            return;
        }

        _questScopeService.ClonePersonalScopeToOrganisation(identity.SteamId, snapshot.OrganisationId);
        _propertyScopeService.ClonePersonalScopeToOrganisation(identity.SteamId, snapshot.OrganisationId);
        _vehicleAccessService.AttachOwnedVehiclesToOrganisation(identity.SteamId, snapshot.OrganisationId);

        _logger.Info($"Organisation create request succeeded for {identity.SteamId} with name '{request.Name}'.");
        SendSnapshot(connection, snapshot);
    }

    private void HandleInviteRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        InvitePlayerRequestDto? request = Deserialize<InvitePlayerRequestDto>(data);
        if (request == null)
        {
            SendError(connection, "Invalid invite request.");
            return;
        }

        if (!_organisationService.TryInvitePlayer(identity, request.TargetPlayer, out OrganisationInviteDto invite, out string error))
        {
            SendError(connection, error);
            return;
        }

        SendSnapshot(connection, identity.SteamId);

        ConnectedPlayerInfo? invitee = FindConnectedPlayer(request.TargetPlayer);
        if (invitee?.Connection != null)
        {
            Send(invitee.Connection, OrganisationMessages.InviteReceived, invite);
            SendSnapshot(invitee.Connection, ResolveSteamId(invitee));
        }
    }

    private void HandleInviteAcceptRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        InviteActionRequestDto? request = Deserialize<InviteActionRequestDto>(data);
        if (request == null)
        {
            SendError(connection, "Invalid accept invite request.");
            return;
        }

        _questScopeService.EnsurePersonalScopeCaptured(identity.SteamId);
        _propertyScopeService.EnsurePersonalScopeCaptured(identity.SteamId);

        if (!_organisationService.TryAcceptInvite(identity, request.InviteId, out OrganisationSnapshotDto snapshot, out string error))
        {
            SendError(connection, error);
            return;
        }

        _questScopeService.TryHydrateWorldForPlayer(identity.SteamId);
        _propertyScopeService.TryHydrateWorldForPlayer(identity.SteamId);
        _propertyScopeService.ClonePersonalScopeToOrganisation(identity.SteamId, snapshot.OrganisationId);
        _vehicleAccessService.AttachOwnedVehiclesToOrganisation(identity.SteamId, snapshot.OrganisationId);
        SendQuestScopeSync(connection, identity.SteamId);
        BroadcastQuestScopeSync(snapshot.MemberSteamIds);
        BroadcastSnapshotsAndVictory(snapshot);
    }

    private void HandleInviteDeclineRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        InviteActionRequestDto? request = Deserialize<InviteActionRequestDto>(data);
        if (request == null)
        {
            SendError(connection, "Invalid decline invite request.");
            return;
        }

        if (!_organisationService.TryDeclineInvite(identity, request.InviteId, out OrganisationSnapshotDto snapshot, out string error))
        {
            SendError(connection, error);
            return;
        }

        SendSnapshot(connection, snapshot);
    }

    private void HandleLeaveRequest(NetworkConnection connection, PlayerIdentity identity)
    {
        OrganisationSnapshotDto previousSnapshot = _organisationService.BuildSnapshot(identity.SteamId);
        if (!_organisationService.TryLeaveOrganisation(identity, out OrganisationSnapshotDto snapshot, out string error))
        {
            SendError(connection, error);
            return;
        }

        _questScopeService.TryHydrateWorldForPlayer(identity.SteamId);
        _propertyScopeService.TryHydrateWorldForPlayer(identity.SteamId);
        SendSnapshot(connection, snapshot);
        BroadcastSnapshots(previousSnapshot.MemberSteamIds);
    }

    private void HandleKickRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        KickOrganisationMemberRequestDto? request = Deserialize<KickOrganisationMemberRequestDto>(data);
        if (request == null)
        {
            SendError(connection, "Invalid kick member request.");
            return;
        }

        OrganisationSnapshotDto previousSnapshot = _organisationService.BuildSnapshot(identity.SteamId);
        if (!_organisationService.TryKickMember(identity, request.MemberSteamId, out OrganisationSnapshotDto snapshot, out string error))
        {
            SendError(connection, error);
            return;
        }

        SendSnapshot(connection, snapshot);
        BroadcastSnapshots(previousSnapshot.MemberSteamIds);
    }

    private void HandleTransferOwnershipRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        TransferOrganisationOwnershipRequestDto? request = Deserialize<TransferOrganisationOwnershipRequestDto>(data);
        if (request == null)
        {
            SendError(connection, "Invalid transfer ownership request.");
            return;
        }

        if (!_organisationService.TryTransferOwnership(identity, request.NewOwnerSteamId, out OrganisationSnapshotDto snapshot, out string error))
        {
            SendError(connection, error);
            return;
        }

        BroadcastSnapshotsAndVictory(snapshot);
    }

    private void HandleAtmTransactionRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        OrganisationAtmTransactionRequestDto? request = Deserialize<OrganisationAtmTransactionRequestDto>(data);
        if (request == null)
        {
            SendAtmTransactionResult(connection, new OrganisationAtmTransactionResultDto
            {
                Success = false,
                Message = "Invalid ATM transaction request.",
            });
            return;
        }

        if (!_organisationService.TryProcessAtmTransaction(identity, request.Amount, request.IsDeposit, out OrganisationSnapshotDto snapshot, out string error))
        {
            _logger.Warning($"Organisation ATM transaction failed for {identity.SteamId}: {error}");
            SendAtmTransactionResult(connection, new OrganisationAtmTransactionResultDto
            {
                RequestId = request.RequestId,
                Amount = request.Amount,
                IsDeposit = request.IsDeposit,
                Success = false,
                Message = error,
            });
            return;
        }

        BroadcastSnapshotsAndVictory(snapshot);
        SendAtmTransactionResult(connection, new OrganisationAtmTransactionResultDto
        {
            RequestId = request.RequestId,
            Amount = request.Amount,
            IsDeposit = request.IsDeposit,
            Success = true,
            Message = request.IsDeposit
                ? $"You have deposited {MoneyManager.FormatAmount(request.Amount)}"
                : $"You have withdrawn {MoneyManager.FormatAmount(request.Amount)}",
        });
    }

    private void HandleShopCheckoutRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        OrganisationShopCheckoutRequestDto? request = Deserialize<OrganisationShopCheckoutRequestDto>(data);
        if (request == null || string.IsNullOrWhiteSpace(request.RequestId))
        {
            SendShopCheckoutResult(connection, new OrganisationShopCheckoutResultDto
            {
                Success = false,
                Message = "Invalid checkout request.",
            });
            return;
        }

        if (!TryValidateShopCheckoutRequest(request, out float authoritativeTotal, out string error))
        {
            SendShopCheckoutResult(connection, new OrganisationShopCheckoutResultDto
            {
                RequestId = request.RequestId,
                Success = false,
                Message = error,
            });
            return;
        }

        if (!CanUseCheckoutShop(connection, request.ShopCode, request.ShopName))
        {
            SendShopCheckoutResult(connection, new OrganisationShopCheckoutResultDto
            {
                RequestId = request.RequestId,
                Success = false,
                Message = "Shop is unavailable for your scope.",
            });
            return;
        }

        if (!_organisationService.TryApplyOnlineTransaction(
            identity,
            "Purchase from " + (string.IsNullOrWhiteSpace(request.ShopName) ? request.ShopCode : request.ShopName),
            -authoritativeTotal,
            1f,
            string.Empty,
            out OrganisationSnapshotDto snapshot,
            out error))
        {
            SendShopCheckoutResult(connection, new OrganisationShopCheckoutResultDto
            {
                RequestId = request.RequestId,
                Success = false,
                Message = error,
            });
            return;
        }

        ApplyShopCheckoutStock(request);
        BroadcastSnapshotsAndVictory(snapshot);
        SendShopCheckoutResult(connection, new OrganisationShopCheckoutResultDto
        {
            RequestId = request.RequestId,
            Success = true,
            Message = "Checkout complete",
        });
    }

    private void HandleQuestTrackingRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        QuestTrackingRequestDto? request = Deserialize<QuestTrackingRequestDto>(data);
        if (request == null || string.IsNullOrWhiteSpace(request.QuestGuid))
        {
            SendError(connection, "Invalid quest tracking request.");
            return;
        }

        _logger.Info($"Received quest tracking update for {request.QuestGuid} from {identity.SteamId}: tracked={request.IsTracked}.");
        RecordScopedQuestTracking(connection, request.QuestGuid, request.IsTracked);
    }

    private void HandleCustomerOfferRejectRequest(NetworkConnection connection, PlayerIdentity identity, string data)
    {
        CustomerOfferRejectRequestDto? request = Deserialize<CustomerOfferRejectRequestDto>(data);
        if (request == null || string.IsNullOrWhiteSpace(request.NpcGuid))
        {
            SendError(connection, "Invalid customer offer rejection request.");
            return;
        }

        if (!_customerContractService.RejectOfferedContract(identity.SteamId, request.NpcGuid, out string error))
        {
            SendError(connection, error);
            return;
        }

        BroadcastSnapshots(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
    }

    private void HandleOnboardingPromptSeenRequest(PlayerIdentity identity)
    {
        _organisationService.MarkOnboardingPromptShown(identity.SteamId);
        _logger.Info($"Marked organisation onboarding prompt as shown for {identity.PlayerName} ({identity.SteamId}).");
    }

    private void SendSnapshot(NetworkConnection connection, string steamId)
    {
        SendSnapshot(connection, steamId, sendScopeSyncs: false);
    }

    private void SendSnapshot(NetworkConnection connection, string steamId, bool sendScopeSyncs)
    {
        SendSnapshot(connection, _organisationService.BuildSnapshot(steamId), sendScopeSyncs);
    }

    private void SendSnapshot(NetworkConnection connection, OrganisationSnapshotDto snapshot)
    {
        SendSnapshot(connection, snapshot, sendScopeSyncs: false);
    }

    private void SendSnapshot(NetworkConnection connection, OrganisationSnapshotDto snapshot, bool sendScopeSyncs)
    {
        if (!IsSendConnectionUsable(connection))
        {
            return;
        }

        snapshot.OwnedPropertyCodes = _propertyScopeService.GetOwnedPropertyCodesForPlayer(snapshot.PlayerSteamId);
        snapshot.AccessiblePropertyCodes = _propertyScopeService.GetAccessiblePropertyCodesForPlayer(snapshot.PlayerSteamId);
        snapshot.ReservedPropertyCodes = _propertyScopeService.GetReservedPropertyCodes();
        snapshot.OwnedVehicleGuids = _vehicleAccessService.GetOwnedVehicleGuidsForPlayer(snapshot.PlayerSteamId);
        snapshot.AccessibleVehicleGuids = _vehicleAccessService.GetAccessibleVehicleGuidsForPlayer(snapshot.PlayerSteamId);
        if (sendScopeSyncs)
        {
            SendQuestScopeSync(connection, snapshot.PlayerSteamId);
            SendCustomerScopeSync(connection, snapshot.PlayerSteamId);
        }

        string json = JsonConvert.SerializeObject(snapshot);
        string cacheKey = BuildSyncCacheKey(connection, snapshot.PlayerSteamId);
        if (_lastSnapshotPayloads.TryGetValue(cacheKey, out string? previousJson)
            && string.Equals(previousJson, json, StringComparison.Ordinal))
        {
            return;
        }

        _lastSnapshotPayloads[cacheKey] = json;
        _logger.Info($"Sending organisation snapshot. HasOrganisation={snapshot.HasOrganisation}, PendingInvites={snapshot.PendingInvites.Count}, PlayerSteamId={snapshot.PlayerSteamId}, ScopeOwnerKey={snapshot.ScopeOwnerKey}, Members={snapshot.MemberSteamIds.Count}.");
        CustomMessaging.SendToClient(connection, OrganisationMessages.Snapshot, json);
    }

    private void BroadcastSnapshots(IEnumerable<string> steamIds)
    {
        HashSet<string> uniqueSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string steamId in steamIds)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !uniqueSteamIds.Add(steamId))
            {
                continue;
            }

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            SendSnapshot(connectedPlayer.Connection, _organisationService.BuildSnapshot(steamId));
        }
    }

    private void BroadcastSnapshotsAndVictory(OrganisationSnapshotDto snapshot)
    {
        if (_organisationService.TryMarkVictoryReached(snapshot, out OrganisationVictoryDto victory))
        {
            SendVictoryNotification(victory);
            snapshot = _organisationService.BuildSnapshotByOwnerKey("org:" + victory.OrganisationId);
        }

        BroadcastSnapshots(snapshot.MemberSteamIds);
    }

    private void SendVictoryNotification(OrganisationVictoryDto victory)
    {
        string message = $"{victory.OrganisationName} reached {MoneyManager.FormatAmount(victory.TargetOnlineBalance)} with {MoneyManager.FormatAmount(victory.OnlineBalance)}.";
        IEnumerable<string> audience = _config.AnnounceVictoryToAllPlayers
            ? GetConnectedSteamIds()
            : victory.MemberSteamIds;
        SendNotification(audience, "Team victory", message);
    }

    private static IEnumerable<string> GetConnectedSteamIds()
    {
        if (S1DS.Server.Players == null)
        {
            return Array.Empty<string>();
        }

        return S1DS.Server.Players.GetConnectedPlayers()
            .Select(ResolveSteamId)
            .Where(steamId => !string.IsNullOrWhiteSpace(steamId))
            .ToList();
    }

    private void SendError(NetworkConnection connection, string message)
    {
        _logger.Warning($"Sending organisation error to client: {message}");
        Send(connection, OrganisationMessages.Error, new OrganisationErrorDto { Message = message ?? "Unknown error." });
    }

    private void SendNotification(NetworkConnection connection, string title, string message)
    {
        Send(connection, OrganisationMessages.Notification, new OrganisationNotificationDto
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Organisation" : title,
            Message = message ?? string.Empty,
        });
    }

    private void SendNotification(IEnumerable<string> steamIds, string title, string message)
    {
        HashSet<string> uniqueSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string steamId in steamIds)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !uniqueSteamIds.Add(steamId))
            {
                continue;
            }

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            SendNotification(connectedPlayer.Connection, title, message);
        }
    }

    private static MSGConversationData BuildConversationSnapshot(MSGConversation conversation, MessageChain messageChain, bool read, bool isHidden)
    {
        MSGConversationData data = conversation.GetSaveData();
        TextMessageData[] messages = new TextMessageData[messageChain.Messages.Count];
        for (int i = 0; i < messageChain.Messages.Count; i++)
        {
            messages[i] = new TextMessageData(
                (int)Message.ESenderType.Other,
                UnityEngine.Random.Range(int.MinValue, int.MaxValue),
                messageChain.Messages[i],
                i == messageChain.Messages.Count - 1);
        }

        data.Read = read;
        data.IsHidden = isHidden;
        data.MessageHistory = messages;
        data.ActiveResponses = Array.Empty<TextResponseData>();
        return data;
    }

    private static MessageChain CreateSingleMessageChain(string message)
    {
#if IL2CPP
        Il2CppSystem.Collections.Generic.List<string> messages = new Il2CppSystem.Collections.Generic.List<string>();
        messages.Add(message);
        return new MessageChain { Messages = messages };
#else
        return new MessageChain { Messages = new List<string> { message } };
#endif
    }

    private static MSGConversationData BuildConversationSnapshot(MSGConversation conversation, string playerMessage, MessageChain responseChain, bool read, bool isHidden)
    {
        MSGConversationData data = conversation.GetSaveData();
        List<TextMessageData> messages = new List<TextMessageData>
        {
            new TextMessageData(
                (int)Message.ESenderType.Player,
                UnityEngine.Random.Range(int.MinValue, int.MaxValue),
                playerMessage,
                true),
        };

        for (int i = 0; i < responseChain.Messages.Count; i++)
        {
            messages.Add(new TextMessageData(
                (int)Message.ESenderType.Other,
                UnityEngine.Random.Range(int.MinValue, int.MaxValue),
                responseChain.Messages[i],
                i == responseChain.Messages.Count - 1));
        }

        data.Read = read;
        data.IsHidden = isHidden;
        data.MessageHistory = messages.ToArray();
        data.ActiveResponses = Array.Empty<TextResponseData>();
        return data;
    }

    private void SendScopedConversationSnapshot(NPC sender, MessageChain messageChain, IEnumerable<string> steamIds, bool read, bool isHidden)
    {
        if (sender?.MSGConversation == null || MessagingManagerReceiveConversationDataMethod == null)
        {
            _logger.Warning("Skipped scoped message conversation snapshot because the messaging reflection target was unavailable.");
            return;
        }

        MessagingManager? messagingManager = NetworkSingleton<MessagingManager>.Instance;
        if (messagingManager == null)
        {
            return;
        }

        MSGConversationData data = BuildConversationSnapshot(sender.MSGConversation, messageChain, read, isHidden);
        SendScopedConversationData(messagingManager, sender.ID, data, steamIds);
    }

    private void SendScopedConversationSnapshot(NPC sender, string playerMessage, MessageChain responseChain, IEnumerable<string> steamIds, bool read, bool isHidden)
    {
        if (sender?.MSGConversation == null || MessagingManagerReceiveConversationDataMethod == null)
        {
            _logger.Warning("Skipped scoped message conversation snapshot because the messaging reflection target was unavailable.");
            return;
        }

        MessagingManager? messagingManager = NetworkSingleton<MessagingManager>.Instance;
        if (messagingManager == null)
        {
            return;
        }

        MSGConversationData data = BuildConversationSnapshot(sender.MSGConversation, playerMessage, responseChain, read, isHidden);
        SendScopedConversationData(messagingManager, sender.ID, data, steamIds);
    }

    private void SendScopedConversationData(MessagingManager messagingManager, string npcId, MSGConversationData data, IEnumerable<string> steamIds)
    {
        if (MessagingManagerReceiveConversationDataMethod == null)
        {
            return;
        }

        HashSet<string> uniqueSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string steamId in steamIds)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !uniqueSteamIds.Add(steamId))
            {
                continue;
            }

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            MessagingManagerReceiveConversationDataMethod.Invoke(
                messagingManager,
                new object[] { connectedPlayer.Connection, npcId, data });
        }
    }

    private void SendAtmTransactionResult(NetworkConnection connection, OrganisationAtmTransactionResultDto result)
    {
        Send(connection, OrganisationMessages.AtmTransactionResult, result);
    }

    private void SendShopCheckoutResult(NetworkConnection connection, OrganisationShopCheckoutResultDto result)
    {
        Send(connection, OrganisationMessages.ShopCheckoutResult, result);
    }

    private void Send(NetworkConnection connection, string messageType, object payload)
    {
        string json = JsonConvert.SerializeObject(payload);
        CustomMessaging.SendToClient(connection, messageType, json);
    }

    private void SendIfChanged(NetworkConnection connection, string steamId, string messageType, object payload, Dictionary<string, string> cache)
    {
        string json = JsonConvert.SerializeObject(payload);
        string cacheKey = BuildSyncCacheKey(connection, steamId);
        if (cache.TryGetValue(cacheKey, out string? previousJson)
            && string.Equals(previousJson, json, StringComparison.Ordinal))
        {
            return;
        }

        cache[cacheKey] = json;
        CustomMessaging.SendToClient(connection, messageType, json);
    }

    private static T? Deserialize<T>(string data) where T : class
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(data);
        }
        catch
        {
            return null;
        }
    }

    internal bool TryHandleVanillaOnlineTransaction(NetworkConnection connection, string transactionName, float unitAmount, float quantity, string transactionNote)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (!_organisationService.TryApplyOnlineTransaction(identity, transactionName, unitAmount, quantity, transactionNote, out OrganisationSnapshotDto snapshot, out string error))
        {
            _logger.Warning($"Organisation online transaction failed for {identity.SteamId}: {error}");
            SendError(connection, error);
            return false;
        }

        BroadcastSnapshotsAndVictory(snapshot);
        return true;
    }

    internal void OnMoneyManagerLoaded(MoneyManager moneyManager, MoneyData data)
    {
        moneyManager.sync___set_value_onlineBalance(0f, true);
        moneyManager.sync___set_value_lifetimeEarnings(Mathf.Clamp(data?.LifetimeEarnings ?? 0f, 0f, float.MaxValue), true);
        ATM.WeeklyDepositSum = 0f;
        _logger.Info("Neutralized vanilla MoneyManager online balance state on load.");
    }

    internal string BuildNeutralMoneySaveString(MoneyManager moneyManager)
    {
        return new MoneyData(0f, moneyManager.GetNetWorth(), moneyManager.LifetimeEarnings, 0f).GetJson();
    }

    internal void ResetWeeklyDepositSums()
    {
        _organisationService.ResetWeeklyDepositSums();
        DealerRetentionProcessingResult dealerRetention = _customerContractService.ProcessWeeklyDealerRetention(
            _config.EnableDealerRetentionFees,
            _config.WeeklyDealerRetentionFee);
        ATM.WeeklyDepositSum = 0f;
        if (dealerRetention.HasChanges)
        {
            RefreshConnectedPlayerSnapshots();
            BroadcastCustomerScopeSync(GetConnectedSteamIds());
        }
    }

    internal void SendDealerRetentionWarningsForCurrentDay()
    {
        TimeManager? timeManager = NetworkSingleton<TimeManager>.Instance;
        if (timeManager == null || timeManager.CurrentDay != EDay.Sunday)
        {
            return;
        }

        List<DealerRetentionWarningRecord> warnings = _customerContractService.RecordWeeklyDealerRetentionWarnings(
            _config.EnableDealerRetentionFees,
            _config.WeeklyDealerRetentionFee,
            timeManager.ElapsedDays);
        foreach (DealerRetentionWarningRecord warning in warnings)
        {
            SendDealerRetentionWarning(warning);
        }
    }

    private void SendDealerRetentionWarning(DealerRetentionWarningRecord warning)
    {
        if (warning == null || string.IsNullOrWhiteSpace(warning.OwnerKey) || string.IsNullOrWhiteSpace(warning.DealerNpcId))
        {
            return;
        }

        Dealer? dealer = FindDealerByNpcId(warning.DealerNpcId);
        if (dealer == null)
        {
            _logger.Warning($"Skipped dealer retention warning because dealer was not available. OwnerKey={warning.OwnerKey}, Dealer={warning.DealerNpcId}.");
            return;
        }

        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(warning.OwnerKey);
        if (snapshot.MemberSteamIds.Count == 0)
        {
            return;
        }

        string message = $"Hey boss, my cash is getting low. I need next week's paycheck by tomorrow or I'm out. I have {MoneyManager.FormatAmount(warning.Cash)} and need {MoneyManager.FormatAmount(warning.WeeklyFee)}.";
        SendScopedConversationSnapshot(
            dealer,
            CreateSingleMessageChain(message),
            snapshot.MemberSteamIds,
            read: false,
            isHidden: false);
        SendNotification(snapshot.MemberSteamIds, dealer.FirstName, message);
    }

    private static Dealer? FindDealerByNpcId(string dealerNpcId)
    {
        if (string.IsNullOrWhiteSpace(dealerNpcId))
        {
            return null;
        }

        foreach (Dealer dealer in Dealer.AllPlayerDealers)
        {
            if (dealer != null && string.Equals(dealer.ID, dealerNpcId, StringComparison.OrdinalIgnoreCase))
            {
                return dealer;
            }
        }

        return null;
    }

    internal void NotifyQuestWorldMutation(string reason)
    {
        _ = reason;
        if (!_questScopeService.HasHydratedOwner)
        {
            return;
        }

        _questWorldMutationPending = true;
        _pendingQuestWorldMutationDueAtUtc = DateTime.UtcNow + QuestScopeSyncDebounce;
    }

    internal void NotifyQuestVariableMutation(string variableName)
    {
        if (_suppressQuestVariableMutationNotification)
        {
            LogQuestVariableDiagnostic($"Suppressed local quest variable mutation notification. Variable={variableName}.");
            return;
        }

        if (TryRecordActiveDeaddropItemCountVariable(variableName))
        {
            return;
        }

        if (!_questScopeService.RecordHydratedVariableValue(variableName))
        {
            return;
        }

        BroadcastQuestScopeSyncThrottled(_questScopeService.GetAudienceSteamIdsForHydratedOwner());
    }

    internal bool TryHandleDeadDropUpdate(DeadDrop deadDrop)
    {
        if (deadDrop == null)
        {
            return false;
        }

        if (deadDrop.PoI != null)
        {
            deadDrop.PoI.enabled = false;
        }

        int itemCount = deadDrop.Storage?.ItemCount ?? 0;
        if (deadDrop.Light != null)
        {
            deadDrop.Light.Enabled = itemCount > 0;
        }

        string deaddropGuid = deadDrop.GUID.ToString();
        string variableName = deadDrop.ItemCountVariable;
        if (!TryGetUniqueActiveDeaddropOwner(deaddropGuid, out string ownerKey))
        {
            if (!string.IsNullOrWhiteSpace(variableName))
            {
                SetQuestVariableLocally(variableName, itemCount.ToString(), suppressMutationNotification: true);
                LogDeaddropDiagnostic($"Handled DeadDrop.UpdateDeadDrop locally without scoped owner. Drop={deaddropGuid}, Variable={variableName}, ItemCount={itemCount}.");
            }
            else
            {
                LogDeaddropDiagnostic($"Handled visual-only DeadDrop.UpdateDeadDrop for {deaddropGuid}; no ItemCountVariable or unique scoped owner is available.");
            }

            return true;
        }

        bool capturedStorage = TryRecordActiveDeaddropStorage(ownerKey, deaddropGuid);
        if (string.IsNullOrWhiteSpace(variableName))
        {
            LogDeaddropDiagnostic($"Handled scoped visual-only DeadDrop.UpdateDeadDrop. Drop={deaddropGuid}, Owner={ownerKey}, ItemCount={itemCount}, StorageCaptured={capturedStorage}.");
            return true;
        }

        LogDeaddropDiagnostic($"Handled scoped DeadDrop.UpdateDeadDrop without vanilla network broadcast. Drop={deaddropGuid}, Owner={ownerKey}, Variable={variableName}, ItemCount={itemCount}.");
        _activeDeaddropItemCountVariableOwnerKey = ownerKey;
        _activeDeaddropItemCountVariableName = variableName;
        try
        {
            SetQuestVariableLocally(variableName, itemCount.ToString(), suppressMutationNotification: false);
        }
        finally
        {
            ClearActiveDeaddropItemCountVariableUpdate();
        }

        return true;
    }

    private bool TryRecordActiveDeaddropStorage(string ownerKey, string deaddropGuid)
    {
        if (_questScopeService.IsRuntimeCaptureSuppressed)
        {
            return false;
        }

        if (!_questScopeService.RecordOwnerDeaddropStorage(ownerKey, deaddropGuid))
        {
            return false;
        }

        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        return true;
    }

    private void ClearActiveDeaddropItemCountVariableUpdate()
    {
        _activeDeaddropItemCountVariableOwnerKey = null;
        _activeDeaddropItemCountVariableName = null;
    }

    private bool TryRecordActiveDeaddropItemCountVariable(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName)
            || string.IsNullOrWhiteSpace(_activeDeaddropItemCountVariableOwnerKey)
            || !string.Equals(variableName, _activeDeaddropItemCountVariableName, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (_questScopeService.RecordOwnerLiveVariableValue(_activeDeaddropItemCountVariableOwnerKey, variableName))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(_activeDeaddropItemCountVariableOwnerKey).MemberSteamIds);
        }

        return true;
    }

    internal bool TryHandleRemoteQuestVariableMutation(NetworkConnection connection, string variableName, string value)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            LogQuestVariableDiagnostic($"Remote variable mutation ignored because caller could not be resolved. ConnectionId={connection?.ClientId.ToString() ?? "null"}, Variable={variableName}, Value={value}.");
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (IsDeadDropItemCountVariable(variableName))
        {
            LogQuestVariableDiagnostic($"Remote dead-drop item-count variable mutation suppressed. Owner={ownerKey}, SteamId={identity.SteamId}, ConnectionId={connection.ClientId}, Variable={variableName}, Value={value}.");
            return true;
        }

        if (!_questScopeService.IsScopedPersistentGlobalVariable(variableName))
        {
            LogQuestVariableDiagnostic($"Remote variable mutation allowed to vanilla. Owner={ownerKey}, SteamId={identity.SteamId}, Variable={variableName}, Value={value}, Reason=NotScopedPersistentGlobal.");
            return false;
        }

        LogQuestVariableDiagnostic($"Remote scoped variable mutation captured. Owner={ownerKey}, SteamId={identity.SteamId}, ConnectionId={connection.ClientId}, Variable={variableName}, Value={value}.");
        if (_questScopeService.SetOwnerVariableValue(ownerKey, variableName, value))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }

        return true;
    }

    private void SetQuestVariableLocally(string variableName, string value, bool suppressMutationNotification)
    {
        bool previousSuppression = _suppressQuestVariableMutationNotification;
        _suppressQuestVariableMutationNotification = suppressMutationNotification;
        try
        {
            NetworkSingleton<VariableDatabase>.Instance.SetVariableValue(variableName, value, network: false);
        }
        catch (Exception exception)
        {
            _logger.Error($"Failed to apply local quest variable value. Variable={variableName}, Value={value}", exception);
        }
        finally
        {
            _suppressQuestVariableMutationNotification = previousSuppression;
        }
    }

    private static bool IsDeadDropItemCountVariable(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName) || DeadDrop.DeadDrops == null)
        {
            return false;
        }

        for (int index = 0; index < DeadDrop.DeadDrops.Count; index++)
        {
            DeadDrop deadDrop = DeadDrop.DeadDrops[index];
            if (deadDrop != null
                && !string.IsNullOrWhiteSpace(deadDrop.ItemCountVariable)
                && string.Equals(deadDrop.ItemCountVariable, variableName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private void LogQuestVariableDiagnostic(string message)
    {
        if (_logQuestVariableDiagnostics)
        {
            _logger.Info("[QuestVariableDiag] " + message);
        }
    }

    private void LogDeaddropDiagnostic(string message)
    {
        if (_logDeaddropDiagnostics)
        {
            _logger.Info("[DeaddropDiag] " + message);
        }
    }

    internal void NotifyDeaddropStateMutation(DeaddropQuest quest, EQuestState state)
    {
        if (!string.IsNullOrWhiteSpace(_activeDeaddropCollectionOwnerKey))
        {
            if (_questScopeService.RecordOwnerDeaddropState(_activeDeaddropCollectionOwnerKey, quest))
            {
                List<string> memberSteamIds = _organisationService.BuildSnapshotByOwnerKey(_activeDeaddropCollectionOwnerKey).MemberSteamIds;
                BroadcastQuestScopeSyncThrottled(memberSteamIds);
                NotifyScopedDeaddropCompletion(memberSteamIds, quest, state);
            }

            return;
        }

        if (!string.IsNullOrWhiteSpace(_activeSupplierDeaddropCompletionOwnerKey))
        {
            if (_questScopeService.RecordOwnerDeaddropState(_activeSupplierDeaddropCompletionOwnerKey, quest))
            {
                List<string> memberSteamIds = _organisationService.BuildSnapshotByOwnerKey(_activeSupplierDeaddropCompletionOwnerKey).MemberSteamIds;
                BroadcastQuestScopeSyncThrottled(memberSteamIds);
                NotifyScopedDeaddropCompletion(memberSteamIds, quest, state);
            }

            return;
        }

        if (!_questScopeService.RecordHydratedDeaddropState(quest))
        {
            return;
        }

        List<string> audienceSteamIds = _questScopeService.GetAudienceSteamIdsForHydratedOwner().ToList();
        BroadcastQuestScopeSyncThrottled(audienceSteamIds);
        NotifyScopedDeaddropCompletion(audienceSteamIds, quest, state);
    }

    internal void NotifyDeaddropCreated(DeaddropQuest quest)
    {
        if (quest?.Drop == null)
        {
            return;
        }

        string? ownerKey = _activeSupplierDeaddropCompletionOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            NotifyQuestWorldMutation("Deaddrop quest created");
            return;
        }

        if (!_questScopeService.RecordOwnerDeaddropState(ownerKey, quest))
        {
            return;
        }

        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
    }

    private void NotifyScopedDeaddropCompletion(IEnumerable<string> steamIds, DeaddropQuest quest, EQuestState state)
    {
        if (quest?.Drop == null || state != EQuestState.Completed)
        {
            return;
        }

        string questGuid = quest.GUID.ToString();
        if (string.IsNullOrWhiteSpace(questGuid) || !_notifiedCompletedDeaddropQuestGuids.Add(questGuid))
        {
            return;
        }

        string dropName = !string.IsNullOrWhiteSpace(quest.Drop.DeadDropName)
            ? quest.Drop.DeadDropName
            : "Dead drop";
        SendNotification(steamIds, "Dead drop collected", $"{dropName} has been collected.");
    }

    internal bool TryHandleScopedDeaddropMinuteCompletion(DeaddropQuest quest)
    {
        if (quest?.Drop == null || quest.State != EQuestState.Active || quest.Drop.Storage?.ItemCount != 0)
        {
            return false;
        }

        string dropGuid = quest.Drop.GUID.ToString();
        if (!TryGetUniqueActiveDeaddropOwner(dropGuid, out string ownerKey))
        {
            _logger.Info($"Suppressed dead-drop minute completion for {dropGuid}; no unique active owner scope was available.");
            return true;
        }

        _activeDeaddropCollectionOwnerKey = ownerKey;
        try
        {
            if (quest.Entries.Count > 0 && quest.Entries[0].State != EQuestState.Completed)
            {
                quest.Entries[0].Complete();
            }

            quest.Complete(network: false);
        }
        finally
        {
            _activeDeaddropCollectionOwnerKey = null;
        }

        return true;
    }

    internal void RecordPendingSupplierDeaddropOwner(NetworkConnection connection, Supplier supplier)
    {
        if (supplier == null
            || string.IsNullOrWhiteSpace(supplier.ID)
            || !supplier.sync___get_value_deadDropPreparing()
            || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        _pendingSupplierDeaddropOwnersBySupplierId[supplier.ID] = ownerKey;
    }

    internal void BeginSupplierDeaddropCompletion(Supplier supplier)
    {
        _activeSupplierDeaddropCompletionOwnerKey = null;
        if (supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return;
        }

        if (_pendingSupplierDeaddropOwnersBySupplierId.TryGetValue(supplier.ID, out string? ownerKey))
        {
            _activeSupplierDeaddropCompletionOwnerKey = ownerKey;
            return;
        }

        if (_customerContractService.TryGetSinglePreparingSupplierDeaddropOwnerKey(supplier, out ownerKey))
        {
            _activeSupplierDeaddropCompletionOwnerKey = ownerKey;
        }
    }

    internal void EndSupplierDeaddropCompletion(Supplier supplier)
    {
        if (supplier != null && !string.IsNullOrWhiteSpace(supplier.ID) && !string.IsNullOrWhiteSpace(_activeSupplierDeaddropCompletionOwnerKey))
        {
            _customerContractService.RecordScopedSupplierStateForOwner(_activeSupplierDeaddropCompletionOwnerKey, supplier);
            RefreshConnectedPlayerSnapshots();
            _pendingSupplierDeaddropOwnersBySupplierId.Remove(supplier.ID);
        }

        _activeSupplierDeaddropCompletionOwnerKey = null;
    }

    internal bool TryHandleScopedSupplierDeaddropReadyMessage(Supplier supplier, string message)
    {
        if (supplier == null
            || string.IsNullOrWhiteSpace(supplier.ID)
            || string.IsNullOrWhiteSpace(message)
            || string.IsNullOrWhiteSpace(_activeSupplierDeaddropCompletionOwnerKey))
        {
            return false;
        }

        SendNotification(
            _organisationService.BuildSnapshotByOwnerKey(_activeSupplierDeaddropCompletionOwnerKey).MemberSteamIds,
            supplier.FirstName,
            message);
        return true;
    }

    internal void PrepareQuestScopeForConnection(NetworkConnection connection)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.PrepareWorldForPlayer(identity.SteamId);
    }

    internal void NotifyPropertyWorldMutation(string reason)
    {
        _propertyScopeService.NotifyWorldMutation(reason);
        RefreshConnectedPlayerSnapshots();
    }

    internal bool TryHandlePropertyReservation(NetworkConnection connection, Property property)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_propertyScopeService.TryReservePropertyForPlayer(identity.SteamId, property, out string error))
        {
            RefreshConnectedPlayerSnapshots();
            return true;
        }

        if (property != null && property.Price > 0f)
        {
            if (_organisationService.TryApplyOnlineTransaction(identity, property.PropertyName + " reservation refund", property.Price, 1f, "Property reservation denied", out OrganisationSnapshotDto snapshot, out _))
            {
                BroadcastSnapshotsAndVictory(snapshot);
            }
        }

        SendError(connection, error);
        RefreshConnectedPlayerSnapshots();
        return false;
    }

    internal bool CanPlayerAccessProperty(NetworkConnection connection, Property property)
    {
        if (property == null || string.IsNullOrWhiteSpace(property.PropertyCode))
        {
            return true;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        return _propertyScopeService.CanPlayerAccessProperty(identity.SteamId, property.PropertyCode);
    }

    internal bool CanPlayerAccessProperty(NetworkConnection connection, string propertyCode)
    {
        if (string.IsNullOrWhiteSpace(propertyCode))
        {
            return false;
        }

        Property? property = Singleton<PropertyManager>.Instance.GetProperty(propertyCode);
        if (property == null)
        {
            return false;
        }

        return CanPlayerAccessProperty(connection, property);
    }

    internal bool TryAuthorizeBusinessLaunderingStart(NetworkConnection connection, Business business, float amount, int minutesSinceStarted)
    {
        if (business == null || string.IsNullOrWhiteSpace(business.PropertyCode))
        {
            SendError(connection, "Business laundering is unavailable right now.");
            return false;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (!_propertyScopeService.CanOwnerAccessProperty(ownerKey, business.PropertyCode))
        {
            SendError(connection, "This business belongs to another organisation.");
            return false;
        }

        if (float.IsNaN(amount)
            || float.IsInfinity(amount)
            || amount <= 0f
            || amount > business.appliedLaunderLimit
            || minutesSinceStarted != 0)
        {
            SendError(connection, "Invalid laundering operation.");
            return false;
        }

        if (_questScopeService.AddOwnerNumericVariableValue(
            ownerKey,
            "LaunderingOperationsStarted",
            1f,
            replicateLiveValue: string.Equals(_questScopeService.HydratedOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }

        return true;
    }

    internal bool CanHydratedOwnerAccessEmployee(Employee employee)
    {
        if (employee?.AssignedProperty == null)
        {
            return true;
        }

        return CanHydratedOwnerAccessProperty(employee.AssignedProperty);
    }

    internal bool CanHydratedOwnerAccessProperty(Property property)
    {
        if (property == null || string.IsNullOrWhiteSpace(property.PropertyCode))
        {
            return true;
        }

        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        return _propertyScopeService.CanOwnerAccessProperty(ownerKey, property.PropertyCode);
    }

    internal bool TryHandleBusinessLaunderingCompletion(Business business, LaunderingOperation operation)
    {
        if (business == null
            || operation == null
            || string.IsNullOrWhiteSpace(business.PropertyCode)
            || !_propertyScopeService.TryGetPropertyOwnerKey(business.PropertyCode, out string ownerKey))
        {
            return false;
        }

        if (!_organisationService.TryApplyOnlineTransactionByOwnerKey(
            ownerKey,
            "Money laundering (" + business.PropertyName + ")",
            operation.amount,
            1f,
            string.Empty,
            out OrganisationSnapshotDto snapshot,
            out string error))
        {
            _logger.Warning($"Business laundering payout failed for property={business.PropertyCode} ownerKey={ownerKey}: {error}");
            return false;
        }

        _questScopeService.AddOwnerNumericVariableValue(
            ownerKey,
            "LaunderingOperationsCompleted",
            1f,
            replicateLiveValue: string.Equals(_questScopeService.HydratedOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase));
        BroadcastSnapshotsAndVictory(snapshot);
        BroadcastQuestScopeSyncThrottled(snapshot.MemberSteamIds);
        return true;
    }

    internal bool HasHydratedQuestOwner()
    {
        return _questScopeService.HasHydratedOwner;
    }

    internal void AdvanceScopedTutorialDayForCompletedQuest(Quest quest)
    {
        foreach (string ownerKey in _questScopeService.AdvanceTutorialDaysForCompletedQuest(quest))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    internal bool TryHandleScopedLoanSharkArrival(Quest_SinkOrSwim quest)
    {
        List<string> changedOwnerKeys = _questScopeService.MarkLoanSharkArrivalForReadyScopes(quest);
        foreach (string ownerKey in changedOwnerKeys)
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }

        if (changedOwnerKeys.Count == 0 || _questScopeService.IsLiveGlobalVariableTrue("Loan_Sharks_Arrived"))
        {
            return false;
        }

        _questScopeService.SetLiveGlobalVariableWithoutCapture("Loan_Sharks_Arrived", true.ToString(), network: true);
        return true;
    }

    internal void AdvanceScopedLoanSharkHours(Quest_TheDeepEnd quest)
    {
        foreach (string ownerKey in _questScopeService.AdvanceLoanSharkHoursForCompletedScopes(quest))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    internal bool TryGetHydratedOwnerWeeklyDepositProgress(out float weeklyDepositSum, out float weeklyDepositLimit)
    {
        weeklyDepositSum = 0f;
        weeklyDepositLimit = _config.WeeklyAtmDepositLimit;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        weeklyDepositSum = _organisationService.GetWeeklyDepositSumByOwnerKey(ownerKey);
        return true;
    }

    internal bool TryGetHydratedOwnerFirstBusiness(out Business? business)
    {
        business = null;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        for (int i = 0; i < Business.Businesses.Count; i++)
        {
            Business candidate = Business.Businesses[i];
            if (candidate != null
                && !string.IsNullOrWhiteSpace(candidate.PropertyCode)
                && _propertyScopeService.CanOwnerAccessProperty(ownerKey, candidate.PropertyCode))
            {
                business = candidate;
                return true;
            }
        }

        return false;
    }

    internal bool TryGetHydratedOwnerSweatshopPotCount(out int potCount)
    {
        potCount = 0;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        bool foundSweatshop = false;
        for (int i = 0; i < Property.Properties.Count; i++)
        {
            Property property = Property.Properties[i];
            if (property is not Sweatshop sweatshop
                || string.IsNullOrWhiteSpace(sweatshop.PropertyCode)
                || !_propertyScopeService.CanOwnerAccessProperty(ownerKey, sweatshop.PropertyCode))
            {
                continue;
            }

            foundSweatshop = true;
            potCount += sweatshop.GetBuildablesOfType<Pot>().Count;
        }

        return foundSweatshop;
    }

    internal bool CanPlayerAccessDeaddrop(NetworkConnection connection, string deaddropGuid)
    {
        if (string.IsNullOrWhiteSpace(deaddropGuid))
        {
            return true;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        return _questScopeService.CanOwnerAccessDeaddrop(ownerKey, deaddropGuid);
    }

    internal void RecordDeaddropStorageMutation(NetworkConnection connection, string deaddropGuid)
    {
        if (string.IsNullOrWhiteSpace(deaddropGuid) || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (!TryGetUniqueActiveDeaddropOwner(deaddropGuid, out string activeOwnerKey)
            || !string.Equals(activeOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!_questScopeService.RecordOwnerDeaddropStorage(ownerKey, deaddropGuid))
        {
            return;
        }

        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
    }

    private bool TryGetUniqueActiveDeaddropOwner(string deaddropGuid, out string ownerKey)
    {
        if (_questScopeService.TryGetActiveDeaddropOwner(deaddropGuid, out ownerKey, out bool isAmbiguous))
        {
            return true;
        }

        if (isAmbiguous)
        {
            _logger.Info($"Suppressed active dead-drop owner resolution for {deaddropGuid}; multiple scoped active owners are recorded.");
        }

        return false;
    }

    internal bool TryGetRandomAvailableDeaddrop(Vector3 origin, out DeadDrop? drop)
    {
        if (string.IsNullOrWhiteSpace(_activeSupplierDeaddropCompletionOwnerKey))
        {
            drop = null;
            _logger.Warning("Suppressed supplier dead-drop location selection because no scoped owner could be resolved.");
            return false;
        }

        return _questScopeService.TryGetRandomAvailableDeaddrop(origin, _activeSupplierDeaddropCompletionOwnerKey, out drop);
    }

    internal bool TryPrepareCartelDeadDropTheft(EMapRegion region, out DeadDrop? drop)
    {
        _pendingCartelDeadDropTheftOwnersByRegion.Remove(region);
        _pendingCartelDeadDropTheftDropsByRegion.Remove(region);
        if (!TryGetStrictCartelActivityOwner(region, out string ownerKey))
        {
            drop = null;
            _logger.Info($"Suppressed cartel dead-drop theft in {region}; no strict active cartel activity owner was available.");
            return false;
        }

        if (!_questScopeService.TryPrepareCartelDeadDropTheft(region, ownerKey, out drop)
            || drop == null
            || string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        _pendingCartelDeadDropTheftOwnersByRegion[region] = ownerKey;
        _pendingCartelDeadDropTheftDropsByRegion[region] = drop.GUID.ToString();
        return true;
    }

    internal void FinalizeCartelDeadDropTheft(EMapRegion region)
    {
        if (!_pendingCartelDeadDropTheftOwnersByRegion.TryGetValue(region, out string? ownerKey)
            || !_pendingCartelDeadDropTheftDropsByRegion.TryGetValue(region, out string? dropGuid))
        {
            return;
        }

        _pendingCartelDeadDropTheftOwnersByRegion.Remove(region);
        _pendingCartelDeadDropTheftDropsByRegion.Remove(region);
        if (!_questScopeService.RecordCartelDeadDropTheft(ownerKey, dropGuid))
        {
            return;
        }

        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
    }

    internal bool IsTrackedProperty(Property property)
    {
        return property != null
            && !string.IsNullOrWhiteSpace(property.PropertyCode)
            && _propertyScopeService.IsPropertyReserved(property.PropertyCode);
    }

    internal void HandlePurchasedVehicleSpawn(VehicleManager vehicleManager, NetworkConnection connection, string vehicleCode, Vector3 position, Quaternion rotation, bool playerOwned)
    {
        LandVehicle? vehicle = vehicleManager?.SpawnAndReturnVehicle(vehicleCode, position, rotation, playerOwned);
        if (vehicle == null || !playerOwned || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _vehicleAccessService.RegisterPurchasedVehicle(identity.SteamId, vehicle);
        BroadcastSnapshots(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
    }

    internal bool CanPlayerAccessVehicle(NetworkConnection connection, LandVehicle vehicle)
    {
        if (vehicle == null)
        {
            return true;
        }

        DeliveryVehicle? deliveryVehicle = vehicle.GetComponent<DeliveryVehicle>();
        if (deliveryVehicle?.ActiveDelivery != null)
        {
            return CanPlayerAccessDeliveryDestination(connection, deliveryVehicle.ActiveDelivery);
        }

        if (!vehicle.IsPlayerOwned)
        {
            return true;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        return _vehicleAccessService.CanPlayerAccessVehicle(identity.SteamId, vehicle.GUID.ToString());
    }

    internal bool CanPlayerAccessDeliveryDestination(NetworkConnection connection, DeliveryInstance delivery)
    {
        if (delivery == null || string.IsNullOrWhiteSpace(delivery.DestinationCode))
        {
            return true;
        }

        return CanPlayerAccessProperty(connection, delivery.DestinationCode);
    }

    internal bool TryHandleCustomerContractAcceptance(NetworkConnection connection, Customer customer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        _questScopeService.PrepareWorldForPlayer(identity.SteamId);
        _questScopeService.ApplyHydratedAcceptedContractCountVariable();

        if (_customerContractService.TryBeginContractAcceptance(identity.SteamId, customer, out _, out string denialMessage))
        {
            return true;
        }

        if (customer?.NPC != null && !string.IsNullOrWhiteSpace(denialMessage))
        {
            customer.NPC.ShowWorldSpaceDialogue(denialMessage, 4f);
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Customer is unavailable right now." : denialMessage);
        return false;
    }

    internal bool TryHandleCustomerOfferMutation(NetworkConnection connection, Customer customer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TryBeginOfferMutation(identity.SteamId, customer, out string denialMessage))
        {
            return true;
        }

        if (customer?.NPC != null && !string.IsNullOrWhiteSpace(denialMessage))
        {
            customer.NPC.ShowWorldSpaceDialogue(denialMessage, 4f);
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "This offer is no longer available." : denialMessage);
        return false;
    }

    internal void FinalizeCustomerContractAcceptance(Customer customer, Contract? contract)
    {
        _customerContractService.CompleteContractAcceptance(customer, contract);
        _questScopeService.ApplyHydratedActiveContractCountVariable();
        RefreshConnectedPlayerSnapshots();
    }

    internal void FinalizeCustomerOfferMutation(Customer customer)
    {
        if (_customerContractService.CompleteOfferMutation(customer))
        {
            RefreshConnectedPlayerSnapshots();
        }
    }

    internal void ReleaseCustomerContract(Customer customer, string outcome)
    {
        _customerContractService.ReleaseActiveContract(customer, outcome);
        RefreshConnectedPlayerSnapshots();
    }

    internal void RegisterPendingCustomerUnlock(NetworkConnection connection, Customer customer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _customerContractService.RegisterPendingUnlock(identity.SteamId, customer);
    }

    internal bool TryPrepareCustomerDiscoveryMutation(NetworkConnection connection, Customer customer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TryBeginCustomerDiscoveryMutation(identity.SteamId, customer, out string denialMessage))
        {
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Customer is unavailable right now." : denialMessage);
        return false;
    }

    internal void PrepareCustomerUnlockSideEffects(Customer customer)
    {
        if (_customerContractService.TryGetPendingUnlockOwnerKey(customer, out string ownerKey))
        {
            _questScopeService.PrepareWorldForOwnerKey(ownerKey);
        }
    }

    internal bool TryPrepareCustomerRelationshipMutation(NetworkConnection connection, Customer customer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TryPrepareRelationshipMutation(identity.SteamId, customer, out string denialMessage))
        {
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Customer is unavailable right now." : denialMessage);
        return false;
    }

    internal bool TryPrepareCustomerStateMutation(NetworkConnection connection, Customer customer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TryPrepareCustomerStateMutation(identity.SteamId, customer, out string denialMessage))
        {
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Customer is unavailable right now." : denialMessage);
        return false;
    }

    internal void FinalizeCustomerUnlock(Customer customer)
    {
        _customerContractService.FinalizeUnlock(customer);
        RefreshConnectedPlayerSnapshots();
    }

    internal void RecordScopedCustomerRelationship(NetworkConnection connection, Customer customer, float relationship)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _customerContractService.RecordRelationship(identity.SteamId, customer, relationship);
        RefreshConnectedPlayerSnapshots();
    }

    internal void RecordScopedCustomerState(NetworkConnection connection, Customer customer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _customerContractService.RecordCustomerState(identity.SteamId, customer);
        RefreshConnectedPlayerSnapshots();
    }

    internal void RecordHydratedCustomerState(Customer customer)
    {
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        if (_customerContractService.RecordCustomerStateForOwner(ownerKey, customer))
        {
            RefreshConnectedPlayerSnapshots();
        }
    }

    internal bool TrySelectHydratedCustomerRequestTarget(Customer customer, Player? requestedTarget, out Player? scopedTarget)
    {
        scopedTarget = requestedTarget;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        string customerName = customer?.NPC?.fullName ?? customer?.NPC?.ID ?? "Customer";
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            _logger.Info($"Suppressed product request from {customerName}; no hydrated owner scope was available.");
            return false;
        }

        if (requestedTarget != null && IsPlayerInOwnerScope(requestedTarget, ownerKey) && CanReceiveCustomerProductRequest(requestedTarget))
        {
            return true;
        }

        if (TryFindRandomPlayerInOwnerScope(ownerKey, out scopedTarget))
        {
            return true;
        }

        _logger.Info($"Suppressed product request from {customerName}; no valid player in hydrated owner scope {ownerKey}.");
        return false;
    }

    internal bool TryFindHydratedCustomerRequestRetaliationTarget(Vector3 origin, out Player? target)
    {
        target = null;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        return TryFindClosestPlayerInOwnerScope(ownerKey, origin, out target, out _);
    }

    internal bool TryGetHydratedOwnerClosestPlayerDistance(Vector3 origin, out float distance)
    {
        distance = 0f;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        return TryFindClosestPlayerInOwnerScope(ownerKey, origin, out _, out distance);
    }

    internal void NotifyCustomerOfferGenerated(Customer customer)
    {
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        _customerContractService.RecordOfferedContract(ownerKey, customer);
        RefreshConnectedPlayerSnapshots();
    }

    internal void NotifyCustomerOfferCleared(Customer customer)
    {
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        _customerContractService.ClearOfferedContract(ownerKey, customer);
        RefreshConnectedPlayerSnapshots();
    }

    internal bool TryGetHydratedOwnerUnlockedCustomerCount(out int count)
    {
        count = 0;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        count = _customerContractService.GetUnlockedCustomerCountForOwner(ownerKey);
        return true;
    }

    internal bool TryGetHydratedOwnerCustomerCountsByRegion(EMapRegion region, out int unlockedCount, out int totalCount)
    {
        unlockedCount = 0;
        totalCount = 0;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        return _customerContractService.TryGetCustomerCountsForOwnerByRegion(ownerKey, region, out unlockedCount, out totalCount);
    }

    internal bool TryGetHydratedOwnerSupplierDeadDropState(Supplier supplier, out bool deadDropPreparing, out int minsUntilDeadDropReady)
    {
        deadDropPreparing = false;
        minsUntilDeadDropReady = -1;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        return _customerContractService.TryGetSupplierStateForOwner(ownerKey, supplier, out bool isUnlocked, out deadDropPreparing, out minsUntilDeadDropReady)
            && isUnlocked;
    }

    internal bool TryIsHydratedOwnerSupplierUnlocked(Supplier supplier, out bool isUnlocked)
    {
        isUnlocked = false;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        return _customerContractService.TryGetSupplierStateForOwner(ownerKey, supplier, out isUnlocked, out _, out _);
    }

    internal bool TryIsHydratedOwnerNpcUnlocked(NPC npc, out bool isUnlocked)
    {
        isUnlocked = false;
        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        return _customerContractService.TryIsNpcUnlockedForOwner(ownerKey, npc, out isUnlocked);
    }

    internal bool TryGetHydratedOwnerActiveDeaddropPosition(out Vector3 position)
    {
        return _questScopeService.TryGetHydratedActiveDeaddropPosition(out position);
    }

    internal bool TryGetHydratedOwnerCompletedContractCount(out int count)
    {
        return _questScopeService.TryGetHydratedCompletedContractCount(out count);
    }

    internal bool TryGetHydratedOwnerCartelStatus(out string status)
    {
        return _questScopeService.TryGetHydratedCartelStatus(out status);
    }

    internal void BeginScopedCartelDefeatStatusMutation()
    {
        _cartelDefeatStatusSuppressionDepth++;
    }

    internal void EndScopedCartelDefeatStatusMutation()
    {
        if (_cartelDefeatStatusSuppressionDepth > 0)
        {
            _cartelDefeatStatusSuppressionDepth--;
        }
    }

    internal bool ShouldAllowCartelStatusMutation(ECartelStatus status)
    {
        if (_cartelDefeatStatusSuppressionDepth <= 0 || status != ECartelStatus.Defeated || !_questScopeService.HasHydratedOwner)
        {
            return true;
        }

        if (_questScopeService.RecordHydratedCartelStatus(status.ToString()))
        {
            BroadcastQuestScopeSyncThrottled(_questScopeService.GetAudienceSteamIdsForHydratedOwner());
        }

        return false;
    }

    internal void RecordHydratedCartelInfluence(EMapRegion region, float influence)
    {
        if (_suppressNextCartelInfluenceCapture)
        {
            _suppressNextCartelInfluenceCapture = false;
            return;
        }

        if (_questScopeService.RecordHydratedCartelInfluence(region, influence))
        {
            BroadcastQuestScopeSyncThrottled(_questScopeService.GetAudienceSteamIdsForHydratedOwner());
        }
    }

    internal bool TryHandleScopedMapRegionUnlock(MapRegionData regionData)
    {
        if (regionData == null || regionData.UnlockedByDefault || !_questScopeService.HasHydratedOwner)
        {
            return false;
        }

        string? ownerKey = _questScopeService.HydratedOwnerKey;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        bool changed = _questScopeService.RecordOwnerMapRegionUnlocked(ownerKey, regionData.Region);
        _questScopeService.ApplyMapRegionUnlockToLiveWorld(regionData.Region);
        changed |= RecordStartingNpcUnlocksForOwner(ownerKey, regionData);

        if (changed)
        {
            List<string> memberSteamIds = _organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds;
            BroadcastQuestScopeSyncThrottled(memberSteamIds);
            BroadcastCustomerScopeSync(memberSteamIds);
        }

        return true;
    }

    internal void PrepareCallerCartelInfluenceMutation(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.PrepareWorldForPlayer(identity.SteamId);
        _callerCartelInfluenceMutationPrepared = true;
    }

    internal bool TryPrepareCartelInfluenceMutation(EMapRegion region, float amount)
    {
        if (_callerCartelInfluenceMutationPrepared)
        {
            _callerCartelInfluenceMutationPrepared = false;
            return true;
        }

        if (TryGetStrictCartelActivityOwner(region, out string ownerKey))
        {
            _questScopeService.PrepareWorldForOwnerKey(ownerKey);
            return true;
        }

        if (IsCartelAmbushDefeatInfluenceDelta(amount)
            && _pendingCartelAmbushInfluenceOwnersByRegion.TryGetValue(region, out PendingCartelAmbushInfluenceOwner? pendingAmbushOwner))
        {
            _pendingCartelAmbushInfluenceOwnersByRegion.Remove(region);
            if (IsPendingCartelAmbushInfluenceOwnerValid(pendingAmbushOwner))
            {
                _questScopeService.PrepareWorldForOwnerKey(pendingAmbushOwner.OwnerKey);
                return true;
            }
        }

        _suppressNextCartelInfluenceCapture = true;
        _logger.Info($"Suppressed cartel influence mutation in {region}; no caller or strict activity owner scope was available.");
        return false;
    }

    private static bool IsCartelAmbushDefeatInfluenceDelta(float amount)
    {
        return Math.Abs(amount - Ambush.AMBUSH_DEFEATED_INFLUENCE_CHANGE) <= 0.001f;
    }

    private bool IsPendingCartelAmbushInfluenceOwnerValid(PendingCartelAmbushInfluenceOwner pendingOwner)
    {
        if (pendingOwner == null
            || string.IsNullOrWhiteSpace(pendingOwner.OwnerKey)
            || DateTime.UtcNow - pendingOwner.CreatedAtUtc > CartelAmbushInfluenceOwnerTtl
            || !CanPlayerBeCartelAmbushed(pendingOwner.Target)
            || !IsPlayerInOwnerScope(pendingOwner.Target, pendingOwner.OwnerKey))
        {
            return false;
        }

        return true;
    }

    private bool RecordStartingNpcUnlocksForOwner(string ownerKey, MapRegionData regionData)
    {
        if (string.IsNullOrWhiteSpace(ownerKey) || regionData?.StartingNPCs == null)
        {
            return false;
        }

        bool changed = false;
        foreach (NPC npc in regionData.StartingNPCs)
        {
            if (npc == null)
            {
                continue;
            }

            Customer? customer = npc.GetComponent<Customer>();
            if (customer != null)
            {
                changed |= _customerContractService.RecordOwnerCustomerUnlocked(ownerKey, customer);
                continue;
            }

            Supplier? supplier = npc as Supplier ?? npc.GetComponent<Supplier>();
            if (supplier != null)
            {
                changed |= _customerContractService.RecordOwnerSupplierUnlocked(ownerKey, supplier);
            }
        }

        return changed;
    }

    private bool TryResolveScopedGlobalCartelActivityOwner(EMapRegion region, out string ownerKey)
    {
        return _questScopeService.TryResolveStartedGlobalCartelActivityOwner(
            region,
            candidateOwnerKey => HasOwnerPlayerInOrAdjacentRegion(candidateOwnerKey, region),
            out ownerKey);
    }

    private List<ScopedGlobalCartelActivityCandidate> BuildScopedGlobalCartelActivityCandidates()
    {
        List<ScopedGlobalCartelActivityCandidate> candidates = new List<ScopedGlobalCartelActivityCandidate>();
        foreach (EMapRegion region in (EMapRegion[])Enum.GetValues(typeof(EMapRegion)))
        {
            if (!TryResolveScopedGlobalCartelActivityOwner(region, out string ownerKey)
                || !_questScopeService.TryGetOwnerCartelInfluence(ownerKey, region, out float influence)
                || influence <= 0.001f)
            {
                continue;
            }

            candidates.Add(new ScopedGlobalCartelActivityCandidate(ownerKey, region, influence));
        }

        return candidates;
    }

    private static List<int> BuildShuffledActivityIndices(int count)
    {
        List<int> indices = new List<int>();
        for (int i = 0; i < count; i++)
        {
            indices.Add(i);
        }

        for (int i = indices.Count - 1; i > 0; i--)
        {
            int swapIndex = UnityEngine.Random.Range(0, i + 1);
            (indices[i], indices[swapIndex]) = (indices[swapIndex], indices[i]);
        }

        return indices;
    }

    private bool HasOwnerPlayerInOrAdjacentRegion(string ownerKey, EMapRegion region)
    {
        MapRegionData? regionData = Singleton<Map>.Instance?.GetRegionData(region);
        List<EMapRegion> eligibleRegions =
#if IL2CPP
            regionData?.GetAdjacentRegions().ToManagedList() ?? new List<EMapRegion>();
#else
            regionData?.GetAdjacentRegions() ?? new List<EMapRegion>();
#endif
        if (!eligibleRegions.Contains(region))
        {
            eligibleRegions.Add(region);
        }

        foreach (Player player in Player.PlayerList)
        {
            if (player != null
                && eligibleRegions.Contains(player.CurrentRegion)
                && IsPlayerInOwnerScope(player, ownerKey))
            {
                return true;
            }
        }

        return false;
    }

    internal void RecordCartelActivityStateForOwner(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        if (_questScopeService.RecordOwnerCartelActivityState(ownerKey))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    internal void RecordStrictCartelActivityState(EMapRegion region)
    {
        if (!TryGetStrictCartelActivityOwner(region, out string ownerKey))
        {
            return;
        }

        RecordCartelActivityStateForOwner(ownerKey);
    }

    internal void AdvanceCartelGlobalActivityCooldowns()
    {
        BroadcastQuestScopeSyncForOwners(_questScopeService.AdvanceCartelGlobalActivityCooldowns());
    }

    internal void AdvanceCartelRegionalActivityCooldowns(EMapRegion region)
    {
        BroadcastQuestScopeSyncForOwners(_questScopeService.AdvanceCartelRegionalActivityCooldowns(region));
    }

    internal void AdvanceCartelDealCooldowns(int hoursElapsed = 1)
    {
        BroadcastQuestScopeSyncForOwners(_questScopeService.AdvanceCartelDealCooldowns(hoursElapsed));
    }

    internal void RecordHydratedCartelGlobalActivityStarted(EMapRegion region, CartelActivity activity)
    {
        if (activity == null
            || activity.Region != region)
        {
            return;
        }

        if (!TryResolveScopedGlobalCartelActivityOwner(region, out string ownerKey))
        {
            _logger.Info($"Suppressed cartel global activity owner capture in {region}; no ready owner scope was available.");
            return;
        }

        _activeCartelGlobalActivityOwnersByRegion[region] = ownerKey;
        RecordCartelActivityStateForOwner(ownerKey);
    }

    internal bool ShouldStartCartelGlobalActivity(EMapRegion region)
    {
        if (TryResolveScopedGlobalCartelActivityOwner(region, out _))
        {
            return true;
        }

        _logger.Info($"Suppressed cartel global activity start in {region}; no ready owner scope was available.");
        return false;
    }

    internal bool TryStartScopedGlobalCartelActivity(CartelActivities activities)
    {
        if (activities == null)
        {
            return false;
        }

        if (activities.CurrentGlobalActivity != null)
        {
            return true;
        }

        List<ScopedGlobalCartelActivityCandidate> candidates = BuildScopedGlobalCartelActivityCandidates();
        if (candidates.Count == 0)
        {
            _logger.Info("Suppressed cartel global activity start; no scoped owner had a ready region with same-owner player proximity.");
            return true;
        }

        candidates.Sort((left, right) => right.Influence.CompareTo(left.Influence));
        ScopedGlobalCartelActivityCandidate? selected = null;
        foreach (ScopedGlobalCartelActivityCandidate candidate in candidates)
        {
            if (UnityEngine.Random.Range(0f, 1f) < candidate.Influence * 0.8f)
            {
                selected = candidate;
                break;
            }
        }

        if (selected == null)
        {
            _logger.Info("Suppressed cartel global activity start; no scoped region passed the influence roll.");
            return true;
        }

        if (CartelActivitiesStartGlobalActivityMethod == null)
        {
            _logger.Warning("Suppressed cartel global activity start; StartGlobalActivity reflection target was unavailable.");
            return true;
        }

        _questScopeService.PrepareWorldForOwnerKey(selected.OwnerKey);
        _activeCartelGlobalActivityOwnersByRegion[selected.Region] = selected.OwnerKey;
        foreach (int activityIndex in BuildShuffledActivityIndices(activities.GlobalActivities.Count))
        {
            CartelActivity activity = activities.GlobalActivities[activityIndex];
            if (activity == null || !activity.IsRegionValidForActivity(selected.Region))
            {
                continue;
            }

            CartelActivitiesStartGlobalActivityMethod.Invoke(activities, new object?[] { null, selected.Region, activityIndex });
            return true;
        }

        _activeCartelGlobalActivityOwnersByRegion.Remove(selected.Region);
        _logger.Info($"Suppressed cartel global activity start in {selected.Region}; no activity was valid for scoped owner {selected.OwnerKey}.");
        return true;
    }

    internal bool TryGetScopedGlobalCartelActivityRegions(out List<EMapRegion> regions)
    {
        regions = BuildScopedGlobalCartelActivityCandidates()
            .Select(candidate => candidate.Region)
            .Distinct()
            .ToList();

        return true;
    }

    internal void RecordHydratedCartelRegionalActivityStarted(EMapRegion region, CartelActivity activity)
    {
        if (activity == null
            || activity.Region != region)
        {
            return;
        }

        if (!_questScopeService.TryResolveStartedRegionalCartelActivityOwner(region, _questScopeService.HydratedOwnerKey, out string ownerKey))
        {
            _logger.Info($"Suppressed cartel regional activity owner capture in {region}; no ready owner scope was available.");
            return;
        }

        _activeCartelRegionalActivityOwnersByRegion[region] = ownerKey;
        RecordCartelActivityStateForOwner(ownerKey);
    }

    internal bool ShouldStartCartelRegionalActivity(EMapRegion region)
    {
        if (_questScopeService.TryResolveStartedRegionalCartelActivityOwner(region, _questScopeService.HydratedOwnerKey, out _))
        {
            return true;
        }

        _logger.Info($"Suppressed cartel regional activity start in {region}; no ready owner scope was available.");
        return false;
    }

    internal bool TryStartScopedRegionalCartelActivity(CartelRegionActivities regionActivities)
    {
        if (regionActivities == null)
        {
            return false;
        }

        if (regionActivities.CurrentActivity != null)
        {
            return true;
        }

        if (!_questScopeService.TryResolveStartedRegionalCartelActivityOwner(regionActivities.Region, _questScopeService.HydratedOwnerKey, out string ownerKey))
        {
            _logger.Info($"Suppressed cartel regional activity start in {regionActivities.Region}; no ready owner scope was available before validity checks.");
            return true;
        }

        if (CartelRegionActivitiesStartActivityMethod == null)
        {
            _logger.Warning("Suppressed cartel regional activity start; StartActivity reflection target was unavailable.");
            return true;
        }

        _questScopeService.PrepareWorldForOwnerKey(ownerKey);
        _activeCartelRegionalActivityOwnersByRegion[regionActivities.Region] = ownerKey;
        foreach (int activityIndex in BuildShuffledActivityIndices(regionActivities.Activities.Count))
        {
            CartelActivity activity = regionActivities.Activities[activityIndex];
            if (activity == null || !activity.IsRegionValidForActivity(regionActivities.Region))
            {
                continue;
            }

            CartelRegionActivitiesStartActivityMethod.Invoke(regionActivities, new object?[] { null, activityIndex });
            return true;
        }

        _activeCartelRegionalActivityOwnersByRegion.Remove(regionActivities.Region);
        _logger.Info($"Suppressed cartel regional activity start in {regionActivities.Region}; no activity was valid for scoped owner {ownerKey}.");
        return true;
    }

    internal void ClearCartelGlobalActivityOwner(EMapRegion region)
    {
        _activeCartelGlobalActivityOwnersByRegion.Remove(region);
    }

    internal void ClearCartelRegionalActivityOwner(EMapRegion region)
    {
        _activeCartelRegionalActivityOwnersByRegion.Remove(region);
    }

    internal void RecordHydratedCartelGraffitiSurface(WorldSpraySurface surface)
    {
        if (_questScopeService.RecordHydratedCartelGraffitiSurface(surface))
        {
            BroadcastQuestScopeSyncThrottled(_questScopeService.GetAudienceSteamIdsForHydratedOwner());
        }
    }

    internal void PrepareCartelGraffitiMutation(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.PrepareWorldForPlayer(identity.SteamId);
    }

    internal void PrepareCartelGraffitiMutation(WorldSpraySurface surface)
    {
        if (surface == null || !TryGetStrictCartelActivityOwner(surface.Region, out string ownerKey))
        {
            return;
        }

        _questScopeService.PrepareWorldForOwnerKey(ownerKey);
    }

    internal void PrepareProductMarketMutation(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.PrepareProductMarketForPlayer(identity.SteamId);
    }

    internal void PrepareProductMarketReplication(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.PrepareProductMarketForPlayer(identity.SteamId);
    }

    internal void PrepareProductMarketForProperty(Property? property)
    {
        if (property == null
            || string.IsNullOrWhiteSpace(property.PropertyCode)
            || !_propertyScopeService.TryGetPropertyOwnerKey(property.PropertyCode, out string ownerKey))
        {
            return;
        }

        _questScopeService.PrepareProductMarketForOwnerKey(ownerKey);
    }

    internal void RecordProductMarketForProperty(Property? property)
    {
        if (property == null
            || string.IsNullOrWhiteSpace(property.PropertyCode)
            || !_propertyScopeService.TryGetPropertyOwnerKey(property.PropertyCode, out string ownerKey))
        {
            return;
        }

        RecordProductMarketForOwner(ownerKey);
    }

    internal void RecordProductMarketMutation(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        if (_questScopeService.RecordProductMarketForPlayer(identity.SteamId))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
        }
    }

    internal void CompleteScopedProductMixesOnNewDay()
    {
        List<string> changedOwnerKeys = _questScopeService.CompletePendingProductMixesOnNewDay();
        foreach (string ownerKey in changedOwnerKeys)
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    internal void RecordCallerCartelStatus(NetworkConnection connection, ECartelStatus status, bool resetStatusChangeTimer)
    {
        _activeCallerCartelStatusOwnerKey = null;
        if (status != ECartelStatus.Truced && status != ECartelStatus.Hostile)
        {
            return;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        _questScopeService.PrepareWorldForPlayer(identity.SteamId);
        _activeCallerCartelStatusOwnerKey = ownerKey;

        if (_questScopeService.RecordOwnerCartelStatus(ownerKey, status.ToString(), resetStatusChangeTimer))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }

        if (status == ECartelStatus.Truced)
        {
            _pendingCartelDealOwnerKey = ownerKey;
        }
        else if (status == ECartelStatus.Hostile && string.Equals(_pendingCartelDealOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            _pendingCartelDealOwnerKey = null;
        }
    }

    internal void CompleteCallerCartelStatusMutation()
    {
        if (string.IsNullOrWhiteSpace(_activeCallerCartelStatusOwnerKey))
        {
            return;
        }

        try
        {
            RecordCartelActivityStateForOwner(_activeCallerCartelStatusOwnerKey);
        }
        finally
        {
            _activeCallerCartelStatusOwnerKey = null;
        }
    }

    internal void AdvanceCartelStatusHours()
    {
        _ = _questScopeService.AdvanceCartelStatusHours();
    }

    internal bool TryHandleCallerCartelAgreementCancellation(NetworkConnection connection, Thomas thomas)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        bool changed = _questScopeService.RecordOwnerCartelStatus(ownerKey, ECartelStatus.Hostile.ToString());
        changed |= _questScopeService.RecordOwnerCartelDeal(ownerKey, null);
        changed |= _questScopeService.ClearOwnerCartelDealStorage(ownerKey);
        ClearActiveCartelDealOwner(ownerKey);

        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        if (changed)
        {
            BroadcastQuestScopeSyncThrottled(snapshot.MemberSteamIds);
        }

        SendScopedThomasAgreementCancelledSnapshot(thomas, snapshot.MemberSteamIds);
        SendNotification(snapshot.MemberSteamIds, "Cartel agreement cancelled", "The Benzies are hostile to your organisation.");
        return true;
    }

    private void SendScopedThomasAgreementCancelledSnapshot(Thomas thomas, IEnumerable<string> steamIds)
    {
        if (thomas?.MSGConversation == null)
        {
            return;
        }

        MessageChain? messageChain = thomas.DialogueHandler?.Database
            ?.GetChain(EDialogueModule.Generic, "thomas_acknowledge_cancel_agreement")
            ?.GetMessageChain();
        if (messageChain?.Messages == null || messageChain.Messages.Count == 0)
        {
            _logger.Warning("Skipped Thomas cancel-agreement acknowledgement because the dialogue chain was unavailable.");
            return;
        }

        SendScopedConversationSnapshot(
            thomas,
            "We're not working together any more - our agreement is off.",
            messageChain,
            steamIds,
            read: false,
            isHidden: false);
    }

    internal bool TryHandleScopedThomasIntroMessage(Thomas thomas)
    {
        if (thomas?.MSGConversation == null)
        {
            return false;
        }

        string ownerKey = _questScopeService.HydratedOwnerKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            _logger.Info("Suppressed Thomas intro message; no hydrated owner scope was available.");
            return true;
        }

        MessageChain? messageChain = thomas.DialogueHandler?.Database
            ?.GetChain(EDialogueModule.Generic, "thomas_intro")
            ?.GetMessageChain();
        if (messageChain?.Messages == null || messageChain.Messages.Count == 0)
        {
            _logger.Warning("Allowed Thomas intro message because the dialogue chain was unavailable.");
            return false;
        }

        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        thomas.MSGConversation.SetIsKnown(false);
        SendScopedConversationSnapshot(thomas, messageChain, snapshot.MemberSteamIds, read: false, isHidden: false);
        SendNotification(snapshot.MemberSteamIds, "Unknown", messageChain.Messages[0]);
        return true;
    }

    internal void CompleteScopedUnfavourableAgreementsIntro(Quest_UnfavourableAgreements quest)
    {
        if (quest?.ReadMessageQuestEntry == null || string.IsNullOrWhiteSpace(_questScopeService.HydratedOwnerKey))
        {
            return;
        }

        if (quest.ReadMessageQuestEntry.State == EQuestState.Active)
        {
            quest.ReadMessageQuestEntry.Complete();
        }
    }

    internal bool TryHandleScopedThomasMeetingEnded(Thomas thomas, NetworkConnection connection)
    {
        if (thomas == null || connection == null || connection.IsLocalClient)
        {
            return false;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            _logger.Warning("Suppressed Thomas meeting completion because the caller could not be resolved.");
            return true;
        }

        _questScopeService.PrepareWorldForPlayer(identity.SteamId);
        thomas.onMeetingEnded?.Invoke();
        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
        return true;
    }

    internal bool TryHandleScopedSamTunnelDugMessage(Sam sam)
    {
        if (sam?.MSGConversation == null)
        {
            return false;
        }

        string ownerKey = _questScopeService.HydratedOwnerKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            _logger.Info("Suppressed Sam tunnel-dug message; no hydrated owner scope was available.");
            return true;
        }

        MessageChain? messageChain = sam.DialogueHandler?.Database
            ?.GetChain(EDialogueModule.Generic, "tunnel_dug")
            ?.GetMessageChain();
        if (messageChain == null)
        {
            _logger.Warning("Suppressed Sam tunnel-dug message because the dialogue chain was unavailable.");
            return true;
        }

        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedConversationSnapshot(sam, messageChain, snapshot.MemberSteamIds, read: false, isHidden: false);
        BroadcastQuestScopeSyncThrottled(snapshot.MemberSteamIds);
        return true;
    }

    internal bool TryHandleScopedRayManorRebuildMessage(CharacterRay ray)
    {
        if (ray?.MSGConversation == null)
        {
            return false;
        }

        Manor? manor = Property.Properties.AsManagedEnumerable().FirstOrDefault(property => property is Manor) as Manor;
        if (manor == null
            || string.IsNullOrWhiteSpace(manor.PropertyCode)
            || !_propertyScopeService.TryGetPropertyOwnerKey(manor.PropertyCode, out string ownerKey))
        {
            _logger.Info("Suppressed Ray manor-rebuild message; no scoped Hyland Manor owner was available.");
            return true;
        }

        MessageChain? messageChain = ray.DialogueHandler?.Database
            ?.GetChain(EDialogueModule.Generic, "manor_rebuilt")
            ?.GetMessageChain();
        if (messageChain == null)
        {
            _logger.Warning("Suppressed Ray manor-rebuild message because the dialogue chain was unavailable.");
            return true;
        }

        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedConversationSnapshot(ray, messageChain, snapshot.MemberSteamIds, read: false, isHidden: false);
        return true;
    }

    internal bool TryHandleScopedPhilInstructionsRequest(NetworkConnection connection, Phil phil, int sendableIndex)
    {
        if (phil?.MSGConversation == null
            || sendableIndex < 0
            || sendableIndex >= phil.MSGConversation.Sendables.Count)
        {
            return false;
        }

        SendableMessage sendable = phil.MSGConversation.Sendables[sendableIndex];
        if (!string.Equals(sendable.Text, "How do I grow shrooms?", StringComparison.Ordinal))
        {
            return false;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return true;
        }

        if (!_customerContractService.TryPrepareSupplierMutation(identity.SteamId, phil, out string denialMessage))
        {
            SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Supplier is unavailable right now." : denialMessage);
            return true;
        }

        MessageChain? messageChain = phil.DialogueHandler?.Database
            ?.GetChain(EDialogueModule.Generic, "grow_shrooms_instructions")
            ?.GetMessageChain();
        if (messageChain == null)
        {
            _logger.Warning("Suppressed Phil shroom-growing instructions because the dialogue chain was unavailable.");
            return true;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedConversationSnapshot(phil, sendable.Text, messageChain, snapshot.MemberSteamIds, read: false, isHidden: false);
        return true;
    }

    internal bool TryHandleDarkMarketUnlock(NetworkConnection connection, DarkMarket darkMarket)
    {
        if (darkMarket == null || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        bool changed = _questScopeService.SetOwnerVariableValue(ownerKey, "WarehouseUnlocked", true.ToString());
        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedDarkMarketUnlock(darkMarket, snapshot.MemberSteamIds);
        BroadcastQuestScopeSyncThrottled(snapshot.MemberSteamIds);
        if (changed)
        {
            SendNotification(snapshot.MemberSteamIds, "Dark Market unlocked", "The Dark Market is now available to your organisation.");
        }

        return true;
    }

    internal bool TryHandleSewerUnlock(NetworkConnection connection, SewerManager sewerManager)
    {
        if (sewerManager == null || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        bool changed = _questScopeService.RecordOwnerSewerUnlocked(ownerKey);
        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedSewerUnlock(sewerManager, snapshot.MemberSteamIds);
        BroadcastQuestScopeSyncThrottled(snapshot.MemberSteamIds);
        if (changed)
        {
            SendNotification(snapshot.MemberSteamIds, "Sewer unlocked", "Sewer access is now available to your organisation.");
        }

        return true;
    }

    internal bool CanPlayerAccessSewer(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient)
        {
            return true;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        return _questScopeService.IsOwnerSewerUnlocked(ownerKey);
    }

    internal bool TryReserveRandomWorldSewerKey(NetworkConnection connection)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        if (_repository.Current.PhysicalWorldReservations.TryGetValue(RandomWorldSewerKeyReservationId, out PhysicalWorldReservationRecord? existing))
        {
            return string.Equals(existing.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
        }

        _repository.Current.PhysicalWorldReservations[RandomWorldSewerKeyReservationId] = new PhysicalWorldReservationRecord
        {
            ReservationId = RandomWorldSewerKeyReservationId,
            OwnerKey = ownerKey,
            ReservedAtUtc = DateTime.UtcNow,
        };
        _repository.MarkDirty();
        SendNotification(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds, "Sewer key collected", "The random world sewer key is reserved to your organisation.");
        return true;
    }

    internal void CaptureGeneratedCartelDeal(CartelDealManager dealManager)
    {
        if (dealManager == null || dealManager.ActiveDeal?.IsValid() != true)
        {
            return;
        }

        string? ownerKey = ResolveGeneratedCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            _pendingCartelDealOwnerKey = null;
            _logger.Info("Suppressed generated cartel deal capture; no scoped owner was ready for the active deal.");
            return;
        }

        _pendingCartelDealOwnerKey = null;
        _activeCartelDealOwnerKey = ownerKey;
        if (_questScopeService.RecordOwnerCartelDeal(ownerKey, dealManager.ActiveDeal))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    internal bool TryHandleScopedCartelDealRequestMessage(CartelDealManager dealManager, CartelDealInfo dealInfo)
    {
        _ = dealManager;
        string ownerKey = _pendingCartelDealOwnerKey ?? string.Empty;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            ownerKey = ResolveGeneratedCartelDealOwnerKey() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            _logger.Info("Suppressed cartel deal request message; no scoped owner was ready for the generated deal.");
            return true;
        }

        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedCartelDealMessage(dealManager.RequestingNPC, "cartel_deal_request", snapshot.MemberSteamIds, dealInfo);
        SendNotification(snapshot.MemberSteamIds, "Cartel deal requested", BuildCartelDealRequestNotification(dealInfo));
        return true;
    }

    internal bool TryHandleScopedCartelDealOverdueMessage(CartelDealManager dealManager)
    {
        string? ownerKey = ResolveActiveCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            if (HasActiveCartelDeal(dealManager))
            {
                _logger.Info("Suppressed cartel deal overdue message; no unique scoped active deal owner could be resolved.");
                return true;
            }

            return false;
        }

        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedCartelDealMessage(dealManager.RequestingNPC, "cartel_deal_overdue", snapshot.MemberSteamIds);
        SendNotification(snapshot.MemberSteamIds, "Cartel deal overdue", "The Benzies deal is overdue.");
        return true;
    }

    internal bool TryHandleScopedCartelDealExpiryMessage(CartelDealManager dealManager)
    {
        string? ownerKey = ResolveActiveCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            if (HasActiveCartelDeal(dealManager))
            {
                _logger.Info("Suppressed cartel deal expiry message; no unique scoped active deal owner could be resolved.");
                return true;
            }

            return false;
        }

        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedCartelDealMessage(dealManager.RequestingNPC, "cartel_deal_expired", snapshot.MemberSteamIds);
        SendNotification(snapshot.MemberSteamIds, "Cartel deal expired", "The Benzies deal expired. They are hostile to your organisation.");
        return true;
    }

    private void SendScopedCartelDealMessage(NPC requestingNpc, string dialogueKey, IEnumerable<string> steamIds, CartelDealInfo? dealInfo = null)
    {
        if (requestingNpc?.MSGConversation == null)
        {
            _logger.Warning($"Skipped cartel deal message '{dialogueKey}' because the requesting NPC conversation was unavailable.");
            return;
        }

        MessageChain? messageChain = requestingNpc.DialogueHandler?.Database
            ?.GetChain(EDialogueModule.Generic, dialogueKey)
            ?.GetMessageChain();
        if (messageChain?.Messages == null || messageChain.Messages.Count == 0)
        {
            _logger.Warning($"Skipped cartel deal message '{dialogueKey}' because the dialogue chain was unavailable.");
            return;
        }

        if (dealInfo?.IsValid() == true)
        {
            for (int i = 0; i < messageChain.Messages.Count; i++)
            {
                string text = messageChain.Messages[i];
                text = text.Replace("<PRODUCT>", BuildCartelDealProductText(dealInfo), StringComparison.Ordinal);
                text = text.Replace("<PAYMENT>", MoneyManager.FormatAmount(dealInfo.PaymentAmount), StringComparison.Ordinal);
                text = text.Replace("<DUE_DAYS>", "3", StringComparison.Ordinal);
                messageChain.Messages[i] = text;
            }
        }

        SendScopedConversationSnapshot(requestingNpc, messageChain, steamIds, read: false, isHidden: false);
    }

    private static string BuildCartelDealProductText(CartelDealInfo dealInfo)
    {
        return $"{dealInfo.RequestedProductQuantity}x {dealInfo.RequestedProductID}";
    }

    internal bool ShouldStartCartelDealRequest()
    {
        string? ownerKey = ResolveGeneratedCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            _pendingCartelDealOwnerKey = null;
            _logger.Info("Suppressed cartel deal request start; no scoped owner was ready for a new deal.");
            return false;
        }

        _pendingCartelDealOwnerKey = ownerKey;
        return true;
    }

    private string? ResolveGeneratedCartelDealOwnerKey()
    {
        return _questScopeService.TryResolveGeneratedCartelDealOwner(
            _pendingCartelDealOwnerKey,
            _questScopeService.HydratedOwnerKey,
            out string ownerKey)
            ? ownerKey
            : null;
    }

    private static string BuildCartelDealRequestNotification(CartelDealInfo dealInfo)
    {
        if (dealInfo?.IsValid() != true)
        {
            return "The Benzies requested a new deal from your organisation.";
        }

        return $"{dealInfo.RequestedProductQuantity}x {dealInfo.RequestedProductID} requested for {MoneyManager.FormatAmount(dealInfo.PaymentAmount)}. Due in 3 days.";
    }

    internal void CaptureCartelDealOverdue(CartelDealManager dealManager)
    {
        if (dealManager == null || dealManager.ActiveDeal?.IsValid() != true)
        {
            return;
        }

        string? ownerKey = ResolveActiveCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        if (_questScopeService.RecordOwnerCartelDeal(ownerKey, dealManager.ActiveDeal))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    internal void CaptureCompletedCartelDeal(CartelDealManager dealManager)
    {
        if (dealManager == null)
        {
            return;
        }

        string? ownerKey = ResolveActiveCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        bool changed = _questScopeService.RecordOwnerCartelDeal(ownerKey, dealManager.ActiveDeal);
        changed |= _questScopeService.ClearOwnerCartelDealStorage(ownerKey);
        changed |= _questScopeService.RecordOwnerCartelDealCooldown(ownerKey, dealManager.HoursUntilNextDealRequest);
        dealManager.DeliveryEntity?.ClearContents();
        ClearActiveCartelDealOwner(ownerKey);

        if (changed)
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    internal bool TryHandleCartelDealCashPayout(CartelDealManager dealManager, float amount)
    {
        if (dealManager == null || amount <= 0f || float.IsNaN(amount) || float.IsInfinity(amount))
        {
            return false;
        }

        string? ownerKey = ResolveActiveCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            if (HasActiveCartelDeal(dealManager))
            {
                _logger.Info("Suppressed cartel deal payout; no unique scoped active deal owner could be resolved.");
                return true;
            }

            return false;
        }

        if (!_organisationService.TryApplyOnlineTransactionByOwnerKey(
            ownerKey,
            "Cartel deal payout",
            amount,
            1f,
            "Scoped cartel deal completion",
            out OrganisationSnapshotDto snapshot,
            out string error))
        {
            _logger.Warning($"Cartel deal scoped payout failed for ownerKey={ownerKey}: {error}");
            return false;
        }

        BroadcastSnapshotsAndVictory(snapshot);
        SendNotification(snapshot.MemberSteamIds, "Cartel deal completed", MoneyManager.FormatAmount(amount) + " deposited to your scoped account.");
        return true;
    }

    internal bool ShouldRunVanillaCartelDealExpiry(CartelDealManager dealManager)
    {
        if (dealManager == null)
        {
            return true;
        }

        string? ownerKey = ResolveActiveCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            if (HasActiveCartelDeal(dealManager))
            {
                _logger.Info("Suppressed cartel deal expiry; no unique scoped active deal owner could be resolved.");
                return false;
            }

            return true;
        }

        dealManager.DealQuest?.Expire(true);
        CartelDealSendExpiryMessageMethod?.Invoke(dealManager, Array.Empty<object>());
        SetActiveCartelDeal(dealManager, null);
        dealManager.DeliveryEntity?.ClearContents();

        bool changed = _questScopeService.RecordOwnerCartelStatus(ownerKey, ECartelStatus.Hostile.ToString());
        changed |= _questScopeService.RecordOwnerCartelDeal(ownerKey, null);
        changed |= _questScopeService.ClearOwnerCartelDealStorage(ownerKey);
        ClearActiveCartelDealOwner(ownerKey);

        if (changed)
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }

        return false;
    }

    internal bool CanAccessCartelDealStorage(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient)
        {
            return true;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        string? activeOwnerKey = ResolveActiveCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(activeOwnerKey)
            && HasActiveCartelDeal(NetworkSingleton<Cartel>.Instance?.DealManager))
        {
            return false;
        }

        return string.IsNullOrWhiteSpace(activeOwnerKey)
            || string.Equals(ownerKey, activeOwnerKey, StringComparison.OrdinalIgnoreCase);
    }

    internal void RecordCartelDealStorageMutation(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient || !CanAccessCartelDealStorage(connection))
        {
            return;
        }

        string? ownerKey = ResolveActiveCartelDealOwnerKey();
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        if (_questScopeService.RecordOwnerCartelDealStorage(ownerKey))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    private string? ResolveActiveCartelDealOwnerKey()
    {
        if (!string.IsNullOrWhiteSpace(_activeCartelDealOwnerKey))
        {
            return _activeCartelDealOwnerKey;
        }

        if (_questScopeService.TryGetActiveCartelDealOwner(out string ownerKey, out bool isAmbiguous))
        {
            _activeCartelDealOwnerKey = ownerKey;
            return ownerKey;
        }

        if (isAmbiguous)
        {
            _logger.Info("Suppressed active cartel deal owner resolution; multiple scoped active deal owners are recorded.");
        }

        return null;
    }

    private static bool HasActiveCartelDeal(CartelDealManager? dealManager)
    {
        return dealManager?.ActiveDeal?.IsValid() == true;
    }

    private void ClearActiveCartelDealOwner(string ownerKey)
    {
        if (string.Equals(_activeCartelDealOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            _activeCartelDealOwnerKey = null;
        }

        if (string.Equals(_pendingCartelDealOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase))
        {
            _pendingCartelDealOwnerKey = null;
        }
    }

    private static void SetActiveCartelDeal(CartelDealManager dealManager, CartelDealInfo? dealInfo)
    {
        CartelDealActiveDealField?.SetValue(dealManager, dealInfo);
    }

    internal void PrepareCustomerHandoverScope(NetworkConnection connection)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.PrepareWorldForPlayer(identity.SteamId);
        _questScopeService.ApplyHydratedCompletedContractCountVariable();
    }

    internal void RecordCustomerRecommendationFromHandover(Customer sourceCustomer, string recommendedNpcId)
    {
        if (_customerContractService.RecordRecommendedNpcForCompletedContract(sourceCustomer, recommendedNpcId))
        {
            RefreshConnectedPlayerSnapshots();
        }
    }

    internal bool TryHandleDealerRecruitment(NetworkConnection connection, Dealer dealer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TryRecruitDealer(identity.SteamId, dealer, out string denialMessage))
        {
            RefreshConnectedPlayerSnapshots();
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Dealer is unavailable right now." : denialMessage);
        return false;
    }

    internal bool TryHandleDealerCustomerAssignment(NetworkConnection connection, Dealer dealer, string customerNpcId, bool addAssignment)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        string denialMessage;
        bool success = addAssignment
            ? _customerContractService.TryAssignDealerCustomer(identity.SteamId, dealer, customerNpcId, out denialMessage)
            : _customerContractService.TryRemoveDealerCustomer(identity.SteamId, dealer, customerNpcId, out denialMessage);

        if (success)
        {
            RefreshConnectedPlayerSnapshots();
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Dealer assignment is unavailable right now." : denialMessage);
        return false;
    }

    internal bool TryHandleDealerCashSet(NetworkConnection connection, Dealer dealer, float cash)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TrySetDealerCash(identity.SteamId, dealer, cash, out string denialMessage))
        {
            RefreshConnectedPlayerSnapshots();
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Dealer cash is unavailable right now." : denialMessage);
        return false;
    }

    internal bool TryHandleDealerPayment(NetworkConnection connection, Dealer dealer, float payment)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TrySubmitDealerPayment(identity.SteamId, dealer, payment, out string denialMessage))
        {
            RefreshConnectedPlayerSnapshots();
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Dealer payment is unavailable right now." : denialMessage);
        return false;
    }

    internal bool TryHandleDealerCompletedDeal(NetworkConnection connection, Dealer dealer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TryRecordDealerCompletedDeal(identity.SteamId, dealer, out string denialMessage))
        {
            RefreshConnectedPlayerSnapshots();
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Dealer completed-deal progress is unavailable right now." : denialMessage);
        return false;
    }

    internal bool CanAccessDealerInventory(NetworkConnection connection, Dealer dealer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.CanAccessDealerInventory(identity.SteamId, dealer, out string denialMessage))
        {
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Dealer inventory is unavailable right now." : denialMessage);
        return false;
    }

    internal void RecordScopedDealerInventory(NetworkConnection connection, Dealer dealer)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _customerContractService.RecordScopedDealerInventory(identity.SteamId, dealer);
        RefreshConnectedPlayerSnapshots();
    }

    internal bool TryPrepareDealerInventoryMutation(Dealer dealer)
    {
        if (_customerContractService.TryPrepareDealerInventoryMutation(dealer, out string denialMessage))
        {
            return true;
        }

        _logger.Warning(string.IsNullOrWhiteSpace(denialMessage) ? "Dealer inventory mutation was rejected." : denialMessage);
        return false;
    }

    internal bool TryPrepareDealerStateMutation(Dealer dealer)
    {
        if (_customerContractService.TryPrepareDealerStateMutation(dealer, out string denialMessage))
        {
            return true;
        }

        _logger.Warning(string.IsNullOrWhiteSpace(denialMessage) ? "Dealer state mutation was rejected." : denialMessage);
        return false;
    }

    internal bool TryGetDealerForCartelRobbery(EMapRegion region, out Dealer? dealer)
    {
        dealer = null;
        if (!TryGetStrictCartelActivityOwner(region, out string ownerKey))
        {
            return false;
        }

        return _customerContractService.TryGetDealerForCartelRobbery(ownerKey, region, out dealer);
    }

    internal bool TryHandleScopedCartelDealNotification(Dealer cartelDealer, Contract contract)
    {
        if (cartelDealer == null || contract == null)
        {
            return true;
        }

        if (!TryGetStrictCartelActivityOwner(cartelDealer.Region, out string ownerKey))
        {
            return false;
        }

        if (UnityEngine.Random.value > 0.3f)
        {
            return false;
        }

        EMapRegion notificationRegion = ResolveUnlockedCartelNotificationRegion(ownerKey, cartelDealer.Region);
        if (!_customerContractService.TryGetRecruitedDealerForOwnerInRegion(ownerKey, notificationRegion, out Dealer? scopedDealer) || scopedDealer == null)
        {
            _logger.Info($"Suppressed cartel deal notification for owner scope {ownerKey}; no recruited scoped dealer in {notificationRegion}.");
            return false;
        }

        string location = contract.DeliveryLocation != null
            ? contract.DeliveryLocation.LocationDescription
            : "an unknown location";
        string message = $"Hey boss, I've heard there's a Benzies deal happening in {notificationRegion}, {location}. Might be worth checking out.";
        OrganisationSnapshotDto snapshot = _organisationService.BuildSnapshotByOwnerKey(ownerKey);
        SendScopedConversationSnapshot(
            scopedDealer,
            CreateSingleMessageChain(message),
            snapshot.MemberSteamIds,
            read: false,
            isHidden: false);
        SendNotification(
            snapshot.MemberSteamIds,
            scopedDealer.FirstName,
            message);
        return false;
    }

    internal bool TryHandleCartelContractReceiptAmbush(Ambush ambush, ContractReceipt receipt)
    {
        if (ambush == null || receipt == null || receipt.CompletedBy != EContractParty.PlayerDealer)
        {
            return true;
        }

        if (!_customerContractService.TryConsumeRecentCompletedContractOwnerForCustomer(receipt.CustomerId, out string ownerKey))
        {
            return true;
        }

        RecordProductMarketForOwner(ownerKey);

        NPC customerNpc = NPCManager.GetNPC(receipt.CustomerId);
        if (customerNpc == null || customerNpc.Region != ambush.Region)
        {
            return false;
        }

        if (!TryFindCartelAmbushTarget(ownerKey, customerNpc.transform.position, out Player? target))
        {
            _logger.Info($"Suppressed cartel post-deal ambush for receipt {receipt.ReceiptId}; no eligible player in owner scope {ownerKey} was near customer {receipt.CustomerId}.");
            return false;
        }

        Player ambushTarget = target!;
        if (ambushTarget.Avatar != null
            && PoliceOfficer.GetNearestOfficer(ambushTarget.Avatar.CenterPoint, out float distanceToOfficer) != null
            && distanceToOfficer < 15f)
        {
            _logger.Info($"Suppressed cartel post-deal ambush for receipt {receipt.ReceiptId}; nearby officer is protecting owner scope {ownerKey} target.");
            return false;
        }

        Vector3 spawnOrigin = ambushTarget.transform.position - ambushTarget.transform.forward * 8f;
        Vector3[] spawnPoints = new Vector3[4];
        for (int i = 0; i < spawnPoints.Length; i++)
        {
            spawnPoints[i] = spawnOrigin + (((i % 2 == 0) ? Vector3.left : Vector3.right) + ((i % 2 == 1) ? Vector3.forward : Vector3.back)) * 2f;
        }

        RecordPendingCartelAmbushInfluenceOwner(ambush.Region, ownerKey, ambushTarget);
        BeginCartelGoonSpawnScope(ownerKey);
        try
        {
            AmbushSpawnAmbushMethod?.Invoke(ambush, new object[] { ambushTarget, spawnPoints });
        }
        finally
        {
            EndCartelGoonSpawnScope();
        }

        _logger.Info($"Retargeted cartel post-deal ambush for receipt {receipt.ReceiptId} to owner scope {ownerKey} player {ambushTarget.PlayerName}.");
        return false;
    }

    internal bool TryHandleScopedTimedCartelAmbush(Ambush ambush)
    {
        if (ambush == null)
        {
            return true;
        }

        if (!TryGetStrictCartelActivityOwner(ambush.Region, out string ownerKey))
        {
            return false;
        }

        if (!ambush.IsActive)
        {
            return false;
        }

        SetCartelActivityMinsSinceActivation(ambush, ambush.MinsSinceActivation + 1);
        if (ambush.MinsSinceActivation >= 360)
        {
            _logger.Info($"Scoped cartel ambush in {ambush.Region} timed out for owner scope {ownerKey}.");
            CartelActivityDeactivateMethod?.Invoke(ambush, Array.Empty<object>());
            RecordCartelActivityStateForOwner(ownerKey);
            return false;
        }

        CartelRegionActivities? regionActivities = GetAmbushRegionActivities(ambush);
        if (regionActivities?.AmbushLocations == null)
        {
            return false;
        }

        foreach (CartelAmbushLocation location in regionActivities.AmbushLocations)
        {
            if (location == null || location.AmbushPoints == null)
            {
                continue;
            }

            foreach (Player candidate in Player.PlayerList)
            {
                if (!CanPlayerBeCartelAmbushed(candidate)
                    || !IsPlayerInOwnerScope(candidate, ownerKey)
                    || candidate.Avatar == null
                    || Vector3.Distance(candidate.Avatar.CenterPoint, location.transform.position) > location.DetectionRadius)
                {
                    continue;
                }

                if (PoliceOfficer.GetNearestOfficer(candidate.Avatar.CenterPoint, out float distanceToOfficer) != null
                    && distanceToOfficer < 15f)
                {
                    break;
                }

                Vector3[] spawnPoints = new Vector3[location.AmbushPoints.Length];
                for (int i = 0; i < location.AmbushPoints.Length; i++)
                {
                    spawnPoints[i] = location.AmbushPoints[i].position;
                }

                RecordPendingCartelAmbushInfluenceOwner(ambush.Region, ownerKey, candidate);
                BeginCartelGoonSpawnScope(ownerKey);
                try
                {
                    AmbushSpawnAmbushMethod?.Invoke(ambush, new object[] { candidate, spawnPoints });
                }
                finally
                {
                    EndCartelGoonSpawnScope();
                }

                _logger.Info($"Retargeted timed cartel ambush in {ambush.Region} to owner scope {ownerKey} player {candidate.PlayerName}.");
                return false;
            }
        }

        return false;
    }

    internal bool TryHandleScopedSprayGraffitiSurfaceSelection(SprayGraffiti activity, EMapRegion region, bool overrideExisting)
    {
        if (activity == null)
        {
            return false;
        }

        if (!overrideExisting && GetSprayGraffitiSurface(activity) != null)
        {
            return true;
        }

        if (!TryResolveScopedCartelRegionalActivityOwner(region, out string ownerKey))
        {
            SetSprayGraffitiSurface(activity, null);
            _logger.Info($"Suppressed cartel graffiti surface selection in {region}; no ready owner scope was available.");
            return true;
        }

        WorldSpraySurface? surface = SelectSprayGraffitiSurfaceForOwner(activity, region, ownerKey);
        SetSprayGraffitiSurface(activity, surface);
        if (surface == null)
        {
            _logger.Info($"Suppressed cartel graffiti surface selection in {region}; no spray surface was available away from owner scope {ownerKey} players.");
        }

        return true;
    }

    internal void BeginScopedCartelGoonSpawnForActivity(EMapRegion region)
    {
        if (!TryGetStrictCartelActivityOwner(region, out string ownerKey))
        {
            _activeCartelGoonSpawnOwnerKey = null;
            return;
        }

        BeginCartelGoonSpawnScope(ownerKey);
    }

    internal void EndScopedCartelGoonSpawnForActivity()
    {
        EndCartelGoonSpawnScope();
    }

    internal void PrepareCartelGoonSpawn(CartelGoon goon)
    {
        if (goon == null || string.IsNullOrWhiteSpace(_activeCartelGoonSpawnOwnerKey))
        {
            return;
        }

        _activeCartelGoonOwners[goon] = _activeCartelGoonSpawnOwnerKey;
    }

    internal bool TryHandleCartelGoonSpawnReplay(CartelGoon goon, NetworkConnection connection)
    {
        if (goon == null || connection != null || !TryGetCartelGoonOwner(goon, out string ownerKey))
        {
            return false;
        }

        if (CartelGoonSpawnClientMethod == null || CartelGoonSpawnClientLogicMethod == null)
        {
            _logger.Warning("Allowed cartel goon observer spawn replay because the private Spawn_Client reflection targets were unavailable.");
            return false;
        }

        CartelGoonSpawnClientLogicMethod.Invoke(goon, new object?[] { null });
        SendCartelGoonSpawnReplayToOwner(goon, ownerKey);
        return true;
    }

    internal bool TryHandleCartelGoonConfigureReplay(CartelGoon goon, NetworkConnection connection, CartelGoonAppearance appearance, float moveSpeed)
    {
        if (goon == null || connection != null || !TryGetCartelGoonOwner(goon, out string ownerKey))
        {
            return false;
        }

        if (CartelGoonConfigureSettingsMethod == null || CartelGoonConfigureSettingsLogicMethod == null)
        {
            _logger.Warning("Allowed cartel goon observer appearance replay because the private ConfigureGoonSettings reflection targets were unavailable.");
            return false;
        }

        CartelGoonConfigureSettingsLogicMethod.Invoke(goon, new object?[] { null, appearance, moveSpeed });
        SendCartelGoonConfigureReplayToOwner(goon, ownerKey, appearance, moveSpeed);
        return true;
    }

    internal bool ShouldReplayCartelGoonToConnection(CartelGoon goon, NetworkConnection connection)
    {
        if (goon == null || connection == null || connection.IsLocalClient || !TryGetCartelGoonOwner(goon, out string ownerKey))
        {
            return true;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string callerOwnerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        return string.Equals(callerOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
    }

    internal bool TryHandleCartelDealerConfigureReplay(CartelDealer dealer, NetworkConnection connection, CartelGoonAppearance appearance, float moveSpeed)
    {
        if (dealer == null || connection != null || !TryGetStrictCartelActivityOwner(dealer.Region, out string ownerKey))
        {
            return false;
        }

        if (CartelDealerConfigureSettingsMethod == null || CartelDealerConfigureSettingsLogicMethod == null)
        {
            _logger.Warning("Allowed cartel dealer observer appearance replay because the private ConfigureGoonSettings reflection targets were unavailable.");
            return false;
        }

        CartelDealerConfigureSettingsLogicMethod.Invoke(dealer, new object?[] { null, appearance, moveSpeed });
        SendCartelDealerConfigureReplayToOwner(dealer, ownerKey, appearance, moveSpeed);
        return true;
    }

    internal bool ShouldReplayCartelDealerToConnection(CartelDealer dealer, NetworkConnection connection)
    {
        if (dealer == null || connection == null || connection.IsLocalClient || !TryGetStrictCartelActivityOwner(dealer.Region, out string ownerKey))
        {
            return true;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string callerOwnerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        return string.Equals(callerOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
    }

    internal bool TryHandleCartelDealerInventoryStoredInstanceReplay(NPCInventory inventory, NetworkConnection connection, int itemSlotIndex, ItemInstance? instance)
    {
        if (connection != null || !TryGetCartelDealerInventoryOwner(inventory, out CartelDealer dealer, out string ownerKey))
        {
            return false;
        }

        if (NpcInventorySetStoredInstanceMethod == null || NpcInventorySetStoredInstanceLogicMethod == null)
        {
            _logger.Warning("Allowed cartel dealer inventory observer item replay because the private NPCInventory SetStoredInstance reflection targets were unavailable.");
            return false;
        }

        NpcInventorySetStoredInstanceLogicMethod.Invoke(inventory, new object?[] { null, itemSlotIndex, instance });
        SendCartelDealerInventoryStoredInstanceReplayToOwner(inventory, ownerKey, itemSlotIndex, instance);
        _customerContractService.RecordScopedDealerInventory(dealer);
        return true;
    }

    internal bool TryHandleCartelDealerInventoryQuantityReplay(NPCInventory inventory, int itemSlotIndex, int quantity)
    {
        if (!TryGetCartelDealerInventoryOwner(inventory, out CartelDealer dealer, out string ownerKey))
        {
            return false;
        }

        if (NpcInventorySetStoredInstanceMethod == null || NpcInventorySetItemSlotQuantityLogicMethod == null)
        {
            _logger.Warning("Allowed cartel dealer inventory observer quantity replay because the private NPCInventory quantity reflection targets were unavailable.");
            return false;
        }

        NpcInventorySetItemSlotQuantityLogicMethod.Invoke(inventory, new object[] { itemSlotIndex, quantity });
        SendCartelDealerInventoryCurrentSlotReplayToOwner(inventory, ownerKey, itemSlotIndex);
        _customerContractService.RecordScopedDealerInventory(dealer);
        return true;
    }

    internal bool TryHandleCartelDealerInventorySlotLockedReplay(NPCInventory inventory, NetworkConnection connection, int itemSlotIndex, bool locked, NetworkObject lockOwner, string lockReason)
    {
        if (connection != null || !TryGetCartelDealerInventoryOwner(inventory, out _, out string ownerKey))
        {
            return false;
        }

        if (NpcInventorySetSlotLockedMethod == null || NpcInventorySetSlotLockedLogicMethod == null)
        {
            _logger.Warning("Allowed cartel dealer inventory observer lock replay because the private NPCInventory lock reflection targets were unavailable.");
            return false;
        }

        NpcInventorySetSlotLockedLogicMethod.Invoke(inventory, new object?[] { null, itemSlotIndex, locked, lockOwner, lockReason });
        SendCartelDealerInventorySlotLockedReplayToOwner(inventory, ownerKey, itemSlotIndex, locked, lockOwner, lockReason);
        return true;
    }

    internal bool TryHandleCartelDealerInventorySlotFilterReplay(NPCInventory inventory, NetworkConnection connection, int itemSlotIndex, SlotFilter filter)
    {
        if (connection != null || !TryGetCartelDealerInventoryOwner(inventory, out _, out string ownerKey))
        {
            return false;
        }

        if (NpcInventorySetSlotFilterMethod == null || NpcInventorySetSlotFilterLogicMethod == null)
        {
            _logger.Warning("Allowed cartel dealer inventory observer filter replay because the private NPCInventory filter reflection targets were unavailable.");
            return false;
        }

        NpcInventorySetSlotFilterLogicMethod.Invoke(inventory, new object?[] { null, itemSlotIndex, filter });
        SendCartelDealerInventorySlotFilterReplayToOwner(inventory, ownerKey, itemSlotIndex, filter);
        return true;
    }

    internal bool ShouldReplayCartelDealerInventoryToConnection(NPCInventory inventory, NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient || !TryGetCartelDealerInventoryOwner(inventory, out _, out string ownerKey))
        {
            return true;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string callerOwnerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        return string.Equals(callerOwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase);
    }

    private bool TryGetCartelGoonOwner(CartelGoon goon, out string ownerKey)
    {
        if (goon != null
            && _activeCartelGoonOwners.TryGetValue(goon, out string? resolvedOwnerKey)
            && !string.IsNullOrWhiteSpace(resolvedOwnerKey))
        {
            ownerKey = resolvedOwnerKey;
            return true;
        }

        ownerKey = string.Empty;
        return false;
    }

    private bool TryGetCartelDealerInventoryOwner(NPCInventory inventory, out CartelDealer dealer, out string ownerKey)
    {
        dealer = null!;
        ownerKey = string.Empty;
        if (inventory == null)
        {
            return false;
        }

        dealer = inventory.GetComponent<CartelDealer>();
        return dealer != null && TryGetStrictCartelActivityOwner(dealer.Region, out ownerKey);
    }

    private void SendCartelGoonSpawnReplayToOwner(CartelGoon goon, string ownerKey)
    {
        foreach (NetworkConnection connection in GetOwnerConnections(ownerKey))
        {
            CartelGoonSpawnClientMethod?.Invoke(goon, new object?[] { connection });
        }
    }

    private void SendCartelGoonConfigureReplayToOwner(CartelGoon goon, string ownerKey, CartelGoonAppearance appearance, float moveSpeed)
    {
        foreach (NetworkConnection connection in GetOwnerConnections(ownerKey))
        {
            CartelGoonConfigureSettingsMethod?.Invoke(goon, new object?[] { connection, appearance, moveSpeed });
        }
    }

    private void SendCartelDealerConfigureReplayToOwner(CartelDealer dealer, string ownerKey, CartelGoonAppearance appearance, float moveSpeed)
    {
        foreach (NetworkConnection connection in GetOwnerConnections(ownerKey))
        {
            CartelDealerConfigureSettingsMethod?.Invoke(dealer, new object?[] { connection, appearance, moveSpeed });
        }
    }

    private void SendCartelDealerInventoryStoredInstanceReplayToOwner(NPCInventory inventory, string ownerKey, int itemSlotIndex, ItemInstance? instance)
    {
        foreach (NetworkConnection connection in GetOwnerConnections(ownerKey))
        {
            NpcInventorySetStoredInstanceMethod?.Invoke(inventory, new object?[] { connection, itemSlotIndex, instance });
        }
    }

    private void SendCartelDealerInventoryCurrentSlotReplayToOwner(NPCInventory inventory, string ownerKey, int itemSlotIndex)
    {
        ItemInstance? instance = itemSlotIndex >= 0 && itemSlotIndex < inventory.ItemSlots.Count
            ? inventory.ItemSlots[itemSlotIndex].ItemInstance
            : null;
        SendCartelDealerInventoryStoredInstanceReplayToOwner(inventory, ownerKey, itemSlotIndex, instance);
    }

    private void SendCartelDealerInventorySlotLockedReplayToOwner(NPCInventory inventory, string ownerKey, int itemSlotIndex, bool locked, NetworkObject lockOwner, string lockReason)
    {
        foreach (NetworkConnection connection in GetOwnerConnections(ownerKey))
        {
            NpcInventorySetSlotLockedMethod?.Invoke(inventory, new object?[] { connection, itemSlotIndex, locked, lockOwner, lockReason });
        }
    }

    private void SendCartelDealerInventorySlotFilterReplayToOwner(NPCInventory inventory, string ownerKey, int itemSlotIndex, SlotFilter filter)
    {
        foreach (NetworkConnection connection in GetOwnerConnections(ownerKey))
        {
            NpcInventorySetSlotFilterMethod?.Invoke(inventory, new object?[] { connection, itemSlotIndex, filter });
        }
    }

    private IEnumerable<NetworkConnection> GetOwnerConnections(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            yield break;
        }

        foreach (string steamId in _organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds)
        {
            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection != null)
            {
                yield return connectedPlayer.Connection;
            }
        }
    }

    private void BeginCartelGoonSpawnScope(string ownerKey)
    {
        _activeCartelGoonSpawnOwnerKey = string.IsNullOrWhiteSpace(ownerKey) ? null : ownerKey;
    }

    private void EndCartelGoonSpawnScope()
    {
        _activeCartelGoonSpawnOwnerKey = null;
    }

    private void RecordPendingCartelAmbushInfluenceOwner(EMapRegion region, string ownerKey, Player target)
    {
        if (string.IsNullOrWhiteSpace(ownerKey)
            || target == null
            || !CanPlayerBeCartelAmbushed(target)
            || !IsPlayerInOwnerScope(target, ownerKey)
            || NetworkSingleton<Cartel>.Instance?.GoonPool?.UnspawnedGoonCount <= 0)
        {
            _pendingCartelAmbushInfluenceOwnersByRegion.Remove(region);
            return;
        }

        _pendingCartelAmbushInfluenceOwnersByRegion[region] = new PendingCartelAmbushInfluenceOwner(ownerKey, target, DateTime.UtcNow);
    }

    private bool TryResolveScopedCartelRegionalActivityOwner(EMapRegion region, out string ownerKey)
    {
        if (TryGetStrictCartelActivityOwner(region, out ownerKey))
        {
            return true;
        }

        return _questScopeService.TryResolveStartedRegionalCartelActivityOwner(region, _questScopeService.HydratedOwnerKey, out ownerKey);
    }

    private WorldSpraySurface? SelectSprayGraffitiSurfaceForOwner(SprayGraffiti activity, EMapRegion region, string ownerKey)
    {
        if (NetworkSingleton<GraffitiManager>.Instance?.WorldSpraySurfaces == null)
        {
            return null;
        }

        float minimumDistance = GetSprayGraffitiMinimumDistanceFromPlayers(activity);
        List<WorldSpraySurface> surfaces = NetworkSingleton<GraffitiManager>.Instance.WorldSpraySurfaces.AsManagedEnumerable()
            .Where(surface => surface != null
                && surface.Region == region
                && surface.CanBeSprayedByNPCs
                && surface.CanBeEdited(checkEditor: true))
            .OrderBy(_ => UnityEngine.Random.value)
            .ToList();

        foreach (WorldSpraySurface surface in surfaces)
        {
            if (!TryFindClosestPlayerInOwnerScope(ownerKey, surface.CenterPoint, out _, out float distance)
                || distance > minimumDistance)
            {
                return surface;
            }
        }

        return null;
    }

    private static WorldSpraySurface? GetSprayGraffitiSurface(SprayGraffiti activity)
    {
        return SprayGraffitiValidSpraySurfaceField?.GetValue(activity) as WorldSpraySurface;
    }

    private static void SetSprayGraffitiSurface(SprayGraffiti activity, WorldSpraySurface? surface)
    {
        SprayGraffitiValidSpraySurfaceField?.SetValue(activity, surface);
    }

    private static float GetSprayGraffitiMinimumDistanceFromPlayers(SprayGraffiti activity)
    {
        object? value = SprayGraffitiMinimumDistanceFromPlayersField?.GetValue(activity);
        return value is float minimumDistance ? minimumDistance : 20f;
    }

    internal void PrepareCartelDealerDefeatSideEffects(CartelDealer dealer)
    {
        if (dealer != null && TryGetStrictCartelActivityOwner(dealer.Region, out string ownerKey))
        {
            _questScopeService.PrepareWorldForOwnerKey(ownerKey);
        }
    }

    internal void PrepareCartelContractReceiptOwner(Customer customer, bool handoverByPlayer, NetworkObject dealerObject)
    {
        _activeDealerSaleOwnerKey = null;
        _activeDealerSaleDealerId = null;
        if (handoverByPlayer || customer == null || dealerObject == null)
        {
            return;
        }

        Dealer dealer = dealerObject.GetComponent<Dealer>();
        if (dealer == null || dealer.DealerType != EDealerType.PlayerDealer)
        {
            return;
        }

        if (_customerContractService.TryPrepareCompletedContractReceiptOwner(customer, out string ownerKey))
        {
            _activeDealerSaleOwnerKey = ownerKey;
            _activeDealerSaleDealerId = dealer.ID;
        }
    }

    internal void ClearActiveDealerSaleOwner()
    {
        _activeDealerSaleOwnerKey = null;
        _activeDealerSaleDealerId = null;
    }

    internal bool TryHandleScopedDealerCompletedDeal(Dealer dealer)
    {
        if (!TryGetActiveDealerSaleOwner(dealer, out string ownerKey))
        {
            return true;
        }

        if (_customerContractService.TryRecordDealerCompletedDealForOwner(ownerKey, dealer, out string denialMessage))
        {
            RefreshConnectedPlayerSnapshots();
            return false;
        }

        _logger.Warning(string.IsNullOrWhiteSpace(denialMessage) ? "Scoped dealer completed-deal side effect was rejected." : denialMessage);
        return false;
    }

    internal bool TryHandleScopedDealerPayment(Dealer dealer, float payment)
    {
        if (!TryGetActiveDealerSaleOwner(dealer, out string ownerKey))
        {
            return true;
        }

        if (_customerContractService.TrySubmitDealerPaymentForOwner(ownerKey, dealer, payment, out string denialMessage))
        {
            RefreshConnectedPlayerSnapshots();
            return false;
        }

        _logger.Warning(string.IsNullOrWhiteSpace(denialMessage) ? "Scoped dealer payment side effect was rejected." : denialMessage);
        return false;
    }

    internal void ClearCartelContractReceiptOwner(ContractReceipt receipt)
    {
        if (receipt != null)
        {
            if (_customerContractService.TryGetRecentCompletedContractOwnerForCustomer(receipt.CustomerId, out string ownerKey))
            {
                RecordProductMarketForOwner(ownerKey);
            }

            _customerContractService.ClearRecentCompletedContractOwnerForCustomer(receipt.CustomerId);
        }
    }

    private void RecordProductMarketForOwner(string ownerKey)
    {
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        if (_questScopeService.RecordProductMarketForOwnerKey(ownerKey))
        {
            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    internal void RecordScopedDealerInventory(Dealer dealer)
    {
        _customerContractService.RecordScopedDealerInventory(dealer);
        RefreshConnectedPlayerSnapshots();
    }

    internal void RecordScopedDealerState(Dealer dealer)
    {
        _customerContractService.RecordScopedDealerState(dealer);
        RefreshConnectedPlayerSnapshots();
    }

    internal bool TryPrepareDealerContractAcceptance(Dealer dealer, Customer customer)
    {
        if (dealer is CartelDealer cartelDealer)
        {
            if (!TryGetStrictCartelActivityOwner(cartelDealer.Region, out string ownerKey))
            {
                _logger.Warning("Cartel dealer contract acceptance was rejected because no strict active cartel activity owner was available.");
                return false;
            }

            if (_customerContractService.TryBeginCartelDealerContractAcceptance(ownerKey, customer, out string cartelDenialMessage))
            {
                return true;
            }

            _logger.Warning(string.IsNullOrWhiteSpace(cartelDenialMessage) ? "Cartel dealer contract acceptance was rejected." : cartelDenialMessage);
            return false;
        }

        if (_customerContractService.TryBeginDealerContractAcceptance(dealer, customer, out _, out string denialMessage))
        {
            return true;
        }

        _logger.Warning(string.IsNullOrWhiteSpace(denialMessage) ? "Dealer contract acceptance was rejected." : denialMessage);
        return false;
    }

    internal bool TryPrepareSupplierMutation(NetworkConnection connection, Supplier supplier)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.TryPrepareSupplierMutation(identity.SteamId, supplier, out string denialMessage))
        {
            return true;
        }

        SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Supplier is unavailable right now." : denialMessage);
        return false;
    }

    internal void RecordScopedSupplierState(NetworkConnection connection, Supplier supplier)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _customerContractService.RecordScopedSupplierState(identity.SteamId, supplier);
        RefreshConnectedPlayerSnapshots();
    }

    internal void NoteSupplierStashMutation(NetworkConnection connection, SupplierStash supplierStash)
    {
        if (supplierStash?.Supplier == null
            || string.IsNullOrWhiteSpace(supplierStash.Supplier.ID)
            || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return;
        }

        _lastSupplierStashMutationOwnersBySupplierId[supplierStash.Supplier.ID] = ownerKey;
    }

    internal bool TryPrepareSupplierDebtRecovery(Supplier supplier)
    {
        _activeSupplierDebtRecoveryOwnerKey = null;
        if (supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return true;
        }

        if (!_lastSupplierStashMutationOwnersBySupplierId.TryGetValue(supplier.ID, out string? ownerKey)
            || string.IsNullOrWhiteSpace(ownerKey))
        {
            if (!_customerContractService.TryGetSingleUnlockedSupplierOwnerKey(supplier, out ownerKey))
            {
                return false;
            }
        }

        if (!_customerContractService.TryPrepareSupplierMutationForOwner(ownerKey, supplier, out _))
        {
            return false;
        }

        _activeSupplierDebtRecoveryOwnerKey = ownerKey;
        return true;
    }

    internal void CompleteSupplierDebtRecovery(Supplier supplier)
    {
        if (supplier == null || string.IsNullOrWhiteSpace(_activeSupplierDebtRecoveryOwnerKey))
        {
            _activeSupplierDebtRecoveryOwnerKey = null;
            return;
        }

        _customerContractService.RecordScopedSupplierStateForOwner(_activeSupplierDebtRecoveryOwnerKey, supplier);
        RefreshConnectedPlayerSnapshots();
        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(_activeSupplierDebtRecoveryOwnerKey).MemberSteamIds);
        _activeSupplierDebtRecoveryOwnerKey = null;
    }

    internal void RefreshScopedSupplierDeaddropTimer(Supplier supplier)
    {
        if (supplier == null || string.IsNullOrWhiteSpace(supplier.ID) || !supplier.sync___get_value_deadDropPreparing())
        {
            return;
        }

        string? ownerKey = null;
        if (!_pendingSupplierDeaddropOwnersBySupplierId.TryGetValue(supplier.ID, out ownerKey)
            && !_customerContractService.TryGetSinglePreparingSupplierDeaddropOwnerKey(supplier, out ownerKey))
        {
            return;
        }

        _customerContractService.RecordScopedSupplierStateForOwner(ownerKey, supplier);
        RefreshConnectedPlayerSnapshots();
        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
    }

    internal bool CanUseSupplier(NetworkConnection connection, Supplier supplier)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (_customerContractService.CanUseSupplier(identity.SteamId, supplier))
        {
            return true;
        }

        SendError(connection, "Supplier is not unlocked for your scope.");
        return false;
    }

    internal bool CanOrderDeliveryShop(NetworkConnection connection, string shopName)
    {
        if (string.IsNullOrWhiteSpace(shopName))
        {
            return false;
        }

        Supplier? supplier = null;
        foreach (NPC npc in NPCManager.NPCRegistry)
        {
            if (npc is Supplier candidate
                && candidate.Shop != null
                && string.Equals(candidate.Shop.ShopName, shopName, StringComparison.OrdinalIgnoreCase))
            {
                supplier = candidate;
                break;
            }
        }

        return supplier == null || CanUseSupplier(connection, supplier);
    }

    private bool CanUseCheckoutShop(NetworkConnection connection, string shopCode, string shopName)
    {
        Supplier? supplier = FindSupplierForShop(shopCode, shopName);
        return supplier == null || CanUseSupplier(connection, supplier);
    }

    private static Supplier? FindSupplierForShop(string shopCode, string shopName)
    {
        foreach (NPC npc in NPCManager.NPCRegistry)
        {
            if (npc is not Supplier supplier || supplier.Shop == null)
            {
                continue;
            }

            bool matchesCode = !string.IsNullOrWhiteSpace(shopCode)
                && string.Equals(supplier.Shop.ShopCode, shopCode, StringComparison.OrdinalIgnoreCase);
            bool matchesName = !string.IsNullOrWhiteSpace(shopName)
                && string.Equals(supplier.Shop.ShopName, shopName, StringComparison.OrdinalIgnoreCase);
            if (matchesCode || matchesName)
            {
                return supplier;
            }
        }

        return null;
    }

    private static bool TryValidateShopCheckoutRequest(OrganisationShopCheckoutRequestDto request, out float authoritativeTotal, out string error)
    {
        authoritativeTotal = 0f;
        error = string.Empty;

        if (request == null
            || string.IsNullOrWhiteSpace(request.ShopCode)
            || request.Lines == null
            || request.Lines.Count == 0)
        {
            error = "Invalid checkout request.";
            return false;
        }

        ShopInterface? shop = FindShop(request.ShopCode);
        if (shop == null)
        {
            error = "Shop is unavailable.";
            return false;
        }

        Dictionary<string, int> quantitiesByItemId = BuildCheckoutQuantities(request);
        foreach (KeyValuePair<string, int> item in quantitiesByItemId)
        {
            ShopListing? listing = FindListing(shop, item.Key);
            if (listing == null || item.Value <= 0)
            {
                error = "Invalid checkout item.";
                return false;
            }

            if (!listing.IsUnlimitedStock && item.Value > listing.CurrentStock)
            {
                error = "Shop stock changed.";
                return false;
            }

            authoritativeTotal += item.Value * listing.Price;
        }

        if (authoritativeTotal <= 0f || Math.Abs(authoritativeTotal - request.Total) > 0.05f)
        {
            error = "Checkout total changed.";
            return false;
        }

        return true;
    }

    private static void ApplyShopCheckoutStock(OrganisationShopCheckoutRequestDto request)
    {
        ShopInterface? shop = FindShop(request.ShopCode);
        if (shop == null)
        {
            return;
        }

        Dictionary<string, int> quantitiesByItemId = BuildCheckoutQuantities(request);
        foreach (KeyValuePair<string, int> item in quantitiesByItemId)
        {
            ShopListing? listing = FindListing(shop, item.Key);
            if (listing == null || listing.IsUnlimitedStock)
            {
                continue;
            }

            int nextStock = Math.Max(0, listing.CurrentStock - item.Value);
            listing.SetStock(nextStock, network: false);
            if (NetworkSingleton<ShopManager>.InstanceExists)
            {
                NetworkSingleton<ShopManager>.Instance.SetStock(null, shop.ShopCode, item.Key, nextStock);
            }
        }
    }

    private static Dictionary<string, int> BuildCheckoutQuantities(OrganisationShopCheckoutRequestDto request)
    {
        Dictionary<string, int> quantitiesByItemId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (OrganisationShopCheckoutLineDto line in request.Lines)
        {
            if (line == null || string.IsNullOrWhiteSpace(line.ItemId) || line.Quantity <= 0)
            {
                continue;
            }

            quantitiesByItemId.TryGetValue(line.ItemId, out int existing);
            quantitiesByItemId[line.ItemId] = existing + line.Quantity;
        }

        return quantitiesByItemId;
    }

    private static ShopInterface? FindShop(string shopCode)
    {
        if (string.IsNullOrWhiteSpace(shopCode))
        {
            return null;
        }

        for (int i = 0; i < ShopInterface.AllShops.Count; i++)
        {
            ShopInterface shop = ShopInterface.AllShops[i];
            if (shop != null && string.Equals(shop.ShopCode, shopCode, StringComparison.OrdinalIgnoreCase))
            {
                return shop;
            }
        }

        return null;
    }

    private static ShopListing? FindListing(ShopInterface shop, string itemId)
    {
        if (shop == null || string.IsNullOrWhiteSpace(itemId))
        {
            return null;
        }

        for (int i = 0; i < shop.Listings.Count; i++)
        {
            ShopListing listing = shop.Listings[i];
            if (listing != null && string.Equals(GetListingItemId(listing), itemId, StringComparison.OrdinalIgnoreCase))
            {
                return listing;
            }
        }

        return null;
    }

    private static string? GetListingItemId(ShopListing listing)
    {
        object? item = AccessTools.Property(listing.GetType(), "Item")?.GetValue(listing);
        return item == null ? null : AccessTools.Property(item.GetType(), "ID")?.GetValue(item) as string;
    }

    internal bool TryHandleSupplierMeetupRequest(NetworkConnection connection, Supplier supplier)
    {
        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            SendError(connection, "Player identity is not ready yet.");
            return false;
        }

        if (!_customerContractService.TryPrepareSupplierMutation(identity.SteamId, supplier, out string denialMessage))
        {
            SendError(connection, string.IsNullOrWhiteSpace(denialMessage) ? "Supplier is unavailable right now." : denialMessage);
            return false;
        }

        if (supplier.RelationData.RelationDelta < 4f)
        {
            SendError(connection, "Insufficient supplier trust.");
            return false;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (!string.IsNullOrWhiteSpace(supplier.ID)
            && TryGetActiveSupplierMeeting(supplier.ID, out SupplierMeetingScopeRecord? activeMeeting))
        {
            string message = string.Equals(activeMeeting!.OwnerKey, ownerKey, StringComparison.OrdinalIgnoreCase)
                ? "Supplier is already meeting your scope."
                : "Supplier is already meeting another crew.";
            SendError(connection, message);
            return false;
        }

        if (!TryChooseSupplierMeetingLocation(ownerKey, out int locationIndex))
        {
            SendError(connection, "No supplier meeting location is available right now.");
            return false;
        }

        if (!string.IsNullOrWhiteSpace(supplier.ID))
        {
            _activeSupplierMeetingOwnersBySupplierId[supplier.ID] = ownerKey;
            _activeSupplierMeetingsBySupplierAndOwner[BuildSupplierMeetingKey(supplier.ID, ownerKey)] = new SupplierMeetingScopeRecord(ownerKey, supplier.ID, locationIndex, 360);
        }

        SendSupplierMeetingConfirmation(connection, supplier, locationIndex);
        InvokeScopedSupplierMeeting(supplier, connection, locationIndex, 360);
        _customerContractService.RecordScopedSupplierState(identity.SteamId, supplier);
        RefreshConnectedPlayerSnapshots();
        return true;
    }

    internal bool CanReplaySupplierMeeting(NetworkConnection connection, Supplier supplier)
    {
        if (supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return true;
        }

        if (_supplierMeetingReplayDepth > 0)
        {
            return true;
        }

        bool hasScopedMeeting = _activeSupplierMeetingOwnersBySupplierId.TryGetValue(supplier.ID, out string? ownerKey)
            && !string.IsNullOrWhiteSpace(ownerKey);
        if (connection == null || connection.IsLocalClient)
        {
            return !hasScopedMeeting;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return false;
        }

        string callerOwnerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (_activeSupplierMeetingsBySupplierAndOwner.ContainsKey(BuildSupplierMeetingKey(supplier.ID, callerOwnerKey)))
        {
            return false;
        }

        return !hasScopedMeeting
            || string.Equals(ownerKey, callerOwnerKey, StringComparison.OrdinalIgnoreCase);
    }

    internal void ReplayScopedSupplierMeetingOnSpawn(NetworkConnection connection, Supplier supplier)
    {
        if (connection == null || connection.IsLocalClient || supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return;
        }

        if (!TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        string ownerKey = _organisationService.ResolveOwnerKey(identity.SteamId);
        if (!_activeSupplierMeetingsBySupplierAndOwner.TryGetValue(BuildSupplierMeetingKey(supplier.ID, ownerKey), out SupplierMeetingScopeRecord? meeting))
        {
            return;
        }

        InvokeScopedSupplierMeeting(supplier, connection, meeting.LocationIndex, meeting.GetRemainingMinutes());
    }

    internal void NotifySupplierMeetingEnded(Supplier supplier)
    {
        if (supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return;
        }

        _activeSupplierMeetingOwnersBySupplierId.Remove(supplier.ID);
        List<string> meetingKeysToRemove = new List<string>();
        foreach (KeyValuePair<string, SupplierMeetingScopeRecord> meeting in _activeSupplierMeetingsBySupplierAndOwner)
        {
            if (string.Equals(meeting.Value.SupplierId, supplier.ID, StringComparison.OrdinalIgnoreCase))
            {
                meetingKeysToRemove.Add(meeting.Key);
            }
        }

        foreach (string meetingKey in meetingKeysToRemove)
        {
            _activeSupplierMeetingsBySupplierAndOwner.Remove(meetingKey);
        }
    }

    internal void RefreshScopedSupplierMeeting(Supplier supplier)
    {
        if (supplier == null || string.IsNullOrWhiteSpace(supplier.ID))
        {
            return;
        }

        if (!TryGetActiveSupplierMeeting(supplier.ID, out SupplierMeetingScopeRecord? meeting))
        {
            return;
        }

        if (supplier.Status != Supplier.ESupplierStatus.Meeting)
        {
            NotifySupplierMeetingEnded(supplier);
            return;
        }

        int minsSinceMeetingStart = GetSupplierMinsSinceMeetingStart(supplier);
        if (minsSinceMeetingStart >= 0)
        {
            meeting!.SetElapsedMinutes(minsSinceMeetingStart);
        }
    }

    internal void EnforceScopedSupplierMeetingExpiry(Supplier supplier)
    {
        if (supplier == null
            || string.IsNullOrWhiteSpace(supplier.ID)
            || supplier.Status != Supplier.ESupplierStatus.Meeting
            || !TryGetActiveSupplierMeeting(supplier.ID, out SupplierMeetingScopeRecord? meeting))
        {
            return;
        }

        int minsSinceMeetingStart = GetSupplierMinsSinceMeetingStart(supplier);
        if (minsSinceMeetingStart < Supplier.MEETUP_DURATION_MINS)
        {
            meeting!.SetElapsedMinutes(minsSinceMeetingStart);
            return;
        }

        if (TryFindClosestPlayerInOwnerScope(meeting!.OwnerKey, supplier.transform.position, out _, out float distance)
            && distance <= Supplier.MeetingEndDistance)
        {
            meeting.SetElapsedMinutes(minsSinceMeetingStart);
            return;
        }

        supplier.EndMeeting();
    }

    internal void RecordScopedQuestAction(NetworkConnection connection, string guid, QuestManager.EQuestAction action)
    {
        if (!ShouldProcessQuestMutation(connection) || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.RecordQuestAction(identity.SteamId, guid, action);
        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
    }

    internal void RecordScopedQuestState(NetworkConnection connection, string guid, EQuestState state)
    {
        if (!ShouldProcessQuestMutation(connection) || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.RecordQuestState(identity.SteamId, guid, state);
        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
    }

    internal void RecordScopedQuestEntryState(NetworkConnection connection, string guid, int entryIndex, EQuestState state)
    {
        if (!ShouldProcessQuestMutation(connection) || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.RecordQuestEntryState(identity.SteamId, guid, entryIndex, state);
        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
    }

    internal void RecordScopedQuestTracking(NetworkConnection connection, string guid, bool tracked)
    {
        if (!ShouldProcessQuestMutation(connection) || !TryResolveCaller(connection, out PlayerIdentity identity))
        {
            return;
        }

        _questScopeService.RecordQuestTracking(identity.SteamId, guid, tracked);
        BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshot(identity.SteamId).MemberSteamIds);
    }

    internal bool ShouldProcessQuestMutation(NetworkConnection connection)
    {
        if (connection == null || connection.IsLocalClient)
        {
            return false;
        }

        return !Singleton<LoadManager>.InstanceExists || !Singleton<LoadManager>.Instance.IsLoading;
    }

    internal void EnsureRepositoryRegisteredFromPersistenceHook(string reason)
    {
        EnsureRepositoryRegistered(reason);
    }

#if IL2CPP
    internal void SaveRepositoryFromPersistenceHook(string saveFolderPath, string reason)
    {
        EnsureRuntimeDependencies();
        _repository.SaveToSaveFolderPath(saveFolderPath);
        _logger.Info($"Saved organisation repository during {reason}.");
    }

    private void LoadRepositoryFromActiveSave(string reason)
    {
        LoadManager? loadManager = Singleton<LoadManager>.Instance;
        string? saveFolderPath = loadManager?.LoadedGameFolderPath;
        if (string.IsNullOrWhiteSpace(saveFolderPath))
        {
            _logger.Warning($"Skipped organisation repository load during {reason} because LoadedGameFolderPath is unavailable.");
            return;
        }

        _repository.LoadFromSaveFolderPath(saveFolderPath);
        _logger.Info($"Loaded organisation repository during {reason}.");
    }
#endif

    private void EnsureRepositoryRegistered(string reason)
    {
#if IL2CPP
        _ = reason;
#else
        SaveManager? saveManager = Singleton<SaveManager>.Instance;
        if (saveManager == null)
        {
            _logger.Warning($"Skipped organisation repository registration during {reason} because SaveManager is unavailable.");
            return;
        }

        if (saveManager.Saveables.Contains(_repository))
        {
            return;
        }

        _repository.InitializeSaveable();
        _logger.Info($"Ensured organisation repository is registered with SaveManager during {reason}.");
#endif
    }

    private void ClearTransientRuntimeScopeState()
    {
        _pendingSupplierDeaddropOwnersBySupplierId.Clear();
        _activeSupplierMeetingOwnersBySupplierId.Clear();
        _activeSupplierMeetingsBySupplierAndOwner.Clear();
        _lastSupplierStashMutationOwnersBySupplierId.Clear();
        _activeCartelGlobalActivityOwnersByRegion.Clear();
        _activeCartelRegionalActivityOwnersByRegion.Clear();
        _pendingCartelAmbushInfluenceOwnersByRegion.Clear();
        _pendingCartelDeadDropTheftOwnersByRegion.Clear();
        _pendingCartelDeadDropTheftDropsByRegion.Clear();
        _activeCartelGoonOwners.Clear();
        _activeSupplierDeaddropCompletionOwnerKey = null;
        _activeDeaddropCollectionOwnerKey = null;
        _activeSupplierDebtRecoveryOwnerKey = null;
        _pendingCartelDealOwnerKey = null;
        _activeCartelDealOwnerKey = null;
        _activeCallerCartelStatusOwnerKey = null;
        _activeDealerSaleOwnerKey = null;
        _activeDeaddropItemCountVariableOwnerKey = null;
        _activeDeaddropItemCountVariableName = null;
        _activeDealerSaleDealerId = null;
        _activeCartelGoonSpawnOwnerKey = null;
        _supplierMeetingReplayDepth = 0;
        _cartelDefeatStatusSuppressionDepth = 0;
        _callerCartelInfluenceMutationPrepared = false;
        _suppressNextCartelInfluenceCapture = false;
    }

    private static bool IsOrganisationCommand(string command)
    {
        return !string.IsNullOrWhiteSpace(command)
            && command.StartsWith("org_", StringComparison.OrdinalIgnoreCase);
    }

    private void SendQuestScopeSync(NetworkConnection connection, string steamId)
    {
        if (!IsSendConnectionUsable(connection))
        {
            _logger.Warning($"[QuestScopeDiag] Skipping quest scope sync because connection is unusable. SteamId={steamId}, ConnectionId={connection?.ClientId.ToString() ?? "null"}.");
            return;
        }

        if (_skipQuestScopeSyncSend)
        {
            _logger.Warning($"[QuestScopeDiag] Skipping quest scope sync send because --org-diag-skip-quest-scope-sync-send is active. SteamId={steamId}, ConnectionId={connection?.ClientId.ToString() ?? "null"}.");
            return;
        }

        QuestScopeSyncDto? scope = _questScopeService.BuildScopeSyncForPlayer(steamId);
        if (scope == null)
        {
            _logger.Warning($"[QuestScopeDiag] No quest scope built for SteamId={steamId}; nothing to send.");
            return;
        }

        string json = JsonConvert.SerializeObject(scope);
        string cacheKey = BuildSyncCacheKey(connection, steamId);
        if (_lastQuestScopeSyncPayloads.TryGetValue(cacheKey, out string? previousJson)
            && string.Equals(previousJson, json, StringComparison.Ordinal))
        {
            _logger.Info($"[QuestScopeDiag] Suppressed duplicate quest scope sync. SteamId={steamId}, ConnectionId={connection?.ClientId.ToString() ?? "null"}, PayloadChars={json.Length}.");
            return;
        }

        _lastQuestScopeSyncPayloads[cacheKey] = json;
        _logger.Info($"[QuestScopeDiag] Built quest scope sync. SteamId={steamId}, OwnerKey={scope.OwnerKey}, ConnectionId={connection?.ClientId.ToString() ?? "null"}, PayloadChars={json.Length}, Quests={scope.Quests.Count}, Contracts={scope.Contracts.Count}, Deaddrops={scope.Deaddrops.Count}.");
        SendQuestScopePayload(connection!, scope, json);
    }

    private void SendQuestScopePayload(NetworkConnection connection, QuestScopeSyncDto scope, string json)
    {
        if (json.Length <= MaxQuestScopeSyncPayloadChars)
        {
            _logger.Info($"[QuestScopeDiag] Queueing single quest scope sync. OwnerKey={scope.OwnerKey}, ConnectionId={connection?.ClientId.ToString() ?? "null"}, PayloadChars={json.Length}.");
            QueueQuestScopeMessage(connection!, OrganisationMessages.QuestScopeSync, json);
            return;
        }

        List<QuestScopeSyncChunkDto> chunks = BuildQuestScopeChunks(scope);
        int chunksToSend = Math.Min(chunks.Count, _maxQuestScopeChunksToSend);
        _logger.Info($"[QuestScopeDiag] Queueing chunked quest scope sync. OwnerKey={scope.OwnerKey}, ConnectionId={connection?.ClientId.ToString() ?? "null"}, OriginalPayloadChars={json.Length}, Chunks={chunks.Count}, ChunksToSend={chunksToSend}.");

        if (chunksToSend < chunks.Count)
        {
            _logger.Warning($"[QuestScopeDiag] Quest scope chunk diagnostics active. Sending only {chunksToSend}/{chunks.Count} chunks; client should not reassemble this partial scope.");
        }

        for (int index = 0; index < chunksToSend; index++)
        {
            chunks[index].Sequence = index;
            chunks[index].Total = chunks.Count;
            string chunkPayload = JsonConvert.SerializeObject(chunks[index]);
            _logger.Info($"[QuestScopeDiag] Queueing quest scope chunk {index + 1}/{chunks.Count}. OwnerKey={scope.OwnerKey}, ConnectionId={connection?.ClientId.ToString() ?? "null"}, PayloadChars={chunkPayload.Length}, Quests={chunks[index].Scope.Quests.Count}, Contracts={chunks[index].Scope.Contracts.Count}, Deaddrops={chunks[index].Scope.Deaddrops.Count}.");
            QueueQuestScopeMessage(connection!, OrganisationMessages.QuestScopeSyncChunk, chunkPayload);
        }
    }

    private void QueueQuestScopeMessage(NetworkConnection connection, string messageType, string payload)
    {
        if (!IsSendConnectionUsable(connection))
        {
            _logger.Warning($"[QuestScopeDiag] Dropped queued quest scope message because connection is unusable. MessageType={messageType}, ConnectionId={connection?.ClientId.ToString() ?? "null"}, PayloadChars={payload?.Length ?? 0}.");
            return;
        }

        _pendingQuestScopeSyncMessages.Add(new PendingScopeSyncMessage(connection, messageType, payload));
        _logger.Info($"[QuestScopeDiag] Quest scope message queued. MessageType={messageType}, ConnectionId={connection.ClientId}, PayloadChars={payload?.Length ?? 0}, PendingMessages={_pendingQuestScopeSyncMessages.Count}.");
    }

    private static List<QuestScopeSyncChunkDto> BuildQuestScopeChunks(QuestScopeSyncDto scope)
    {
        List<QuestScopeSyncChunkDto> chunks = new List<QuestScopeSyncChunkDto>();
        QuestScopeSyncDto currentScope = CreateQuestScopeChunkScope(scope, includeSharedState: true);

        foreach (ScopedQuestSyncDto quest in scope.Quests)
        {
            currentScope.Quests.Add(quest);
            QuestScopeSyncChunkDto testChunk = new QuestScopeSyncChunkDto { OwnerKey = scope.OwnerKey, Scope = currentScope };
            if (currentScope.Quests.Count <= 1 || JsonConvert.SerializeObject(testChunk).Length <= MaxQuestScopeSyncPayloadChars)
            {
                continue;
            }

            currentScope.Quests.RemoveAt(currentScope.Quests.Count - 1);
            chunks.Add(new QuestScopeSyncChunkDto { OwnerKey = scope.OwnerKey, Scope = currentScope });

            currentScope = CreateQuestScopeChunkScope(scope, includeSharedState: false);
            currentScope.Quests.Add(quest);
        }

        currentScope.Contracts.AddRange(scope.Contracts);
        currentScope.Deaddrops.AddRange(scope.Deaddrops);
        if (currentScope.Quests.Count > 0 || currentScope.Contracts.Count > 0 || currentScope.Deaddrops.Count > 0 || chunks.Count == 0)
        {
            chunks.Add(new QuestScopeSyncChunkDto { OwnerKey = scope.OwnerKey, Scope = currentScope });
        }

        return chunks;
    }

    private static QuestScopeSyncDto CreateQuestScopeChunkScope(QuestScopeSyncDto scope, bool includeSharedState)
    {
        if (!includeSharedState)
        {
            return new QuestScopeSyncDto { OwnerKey = scope.OwnerKey };
        }

        return new QuestScopeSyncDto
        {
            OwnerKey = scope.OwnerKey,
            CartelStatus = scope.CartelStatus,
            CartelDealDataJson = scope.CartelDealDataJson,
            CartelInfluenceByRegion = new Dictionary<string, float>(scope.CartelInfluenceByRegion, StringComparer.OrdinalIgnoreCase),
            CartelActivityState = scope.CartelActivityState,
            MapRegionUnlockedByRegion = new Dictionary<string, bool>(scope.MapRegionUnlockedByRegion, StringComparer.OrdinalIgnoreCase),
            DarkMarketUnlocked = scope.DarkMarketUnlocked,
            SewerUnlocked = scope.SewerUnlocked,
            LoanSharksArrived = scope.LoanSharksArrived,
            HoursSinceLoanSharksArrived = scope.HoursSinceLoanSharksArrived,
            ProductMarketState = scope.ProductMarketState,
            InaccessibleDeaddropGuids = new List<string>(scope.InaccessibleDeaddropGuids),
        };
    }

    private void QueueInitialScopeSyncs(string steamId)
    {
        if (string.IsNullOrWhiteSpace(steamId))
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        _pendingQuestScopeSyncs[steamId] = nowUtc + InitialQuestScopeSyncDelay;
        _pendingCustomerScopeSyncs[steamId] = nowUtc + InitialCustomerScopeSyncDelay;
    }

    private void FlushPendingQuestWorldMutation()
    {
        if (!_questWorldMutationPending || _pendingQuestWorldMutationDueAtUtc > DateTime.UtcNow)
        {
            return;
        }

        _questWorldMutationPending = false;
        _pendingQuestWorldMutationDueAtUtc = DateTime.MinValue;
        if (!_questScopeService.NotifyWorldMutation("debounced"))
        {
            return;
        }

        BroadcastQuestScopeSyncThrottled(_questScopeService.GetAudienceSteamIdsForHydratedOwner());
    }

    private void SendCustomerScopeSync(NetworkConnection connection, string steamId)
    {
        if (!IsSendConnectionUsable(connection))
        {
            return;
        }

        CustomerScopeSyncDto scope = _customerContractService.BuildScopeSyncForPlayer(steamId);
        string json = JsonConvert.SerializeObject(scope);
        string cacheKey = BuildSyncCacheKey(connection, steamId);
        if (_lastCustomerScopeSyncPayloads.TryGetValue(cacheKey, out string? previousJson)
            && string.Equals(previousJson, json, StringComparison.Ordinal))
        {
            return;
        }

        _lastCustomerScopeSyncPayloads[cacheKey] = json;
        QueueCustomerScopePayload(connection, cacheKey, scope, json);
    }

    private void QueueCustomerScopePayload(NetworkConnection connection, string cacheKey, CustomerScopeSyncDto scope, string json)
    {
        _pendingCustomerScopeSyncMessages.RemoveAll(message => string.Equals(message.CacheKey, cacheKey, StringComparison.OrdinalIgnoreCase));

        if (json.Length <= MaxCustomerScopeSyncPayloadChars)
        {
            QueueCustomerScopeMessage(connection, cacheKey, OrganisationMessages.CustomerScopeSync, json);
            return;
        }

        _logger.Warning($"Skipped oversized customer scope sync for {cacheKey}; payload length {json.Length} exceeds {MaxCustomerScopeSyncPayloadChars}. The native network state will remain authoritative until a smaller scoped delta is available.");
    }

    private void QueueCustomerScopeMessage(NetworkConnection connection, string cacheKey, string messageType, string payload)
    {
        if (!IsSendConnectionUsable(connection))
        {
            return;
        }

        _pendingCustomerScopeSyncMessages.Add(new PendingCustomerScopeSyncMessage(connection, cacheKey, messageType, payload));
    }

    private static bool IsSendConnectionUsable(NetworkConnection connection)
    {
        return connection != null && connection.IsValid && connection.ClientId >= 0;
    }

    private static List<CustomerScopeSyncChunkDto> BuildCustomerScopeChunks(CustomerScopeSyncDto scope)
    {
        List<CustomerScopeSyncChunkDto> chunks = new List<CustomerScopeSyncChunkDto>();
        CustomerScopeSyncChunkDto current = CreateCustomerScopeChunk(scope, includeSharedLists: true);

        foreach (CustomerScopeEntryDto customer in scope.Customers)
        {
            current.Customers.Add(customer);
            if (current.Customers.Count <= 1 || JsonConvert.SerializeObject(current).Length <= MaxCustomerScopeSyncPayloadChars)
            {
                continue;
            }

            current.Customers.RemoveAt(current.Customers.Count - 1);
            chunks.Add(current);

            current = CreateCustomerScopeChunk(scope, includeSharedLists: false);
            current.Customers.Add(customer);
        }

        if (current.Customers.Count > 0 || chunks.Count == 0)
        {
            chunks.Add(current);
        }

        return chunks;
    }

    private static CustomerScopeSyncChunkDto CreateCustomerScopeChunk(CustomerScopeSyncDto scope, bool includeSharedLists)
    {
        return new CustomerScopeSyncChunkDto
        {
            OwnerKey = scope.OwnerKey,
            Dealers = includeSharedLists ? new List<DealerScopeEntryDto>(scope.Dealers) : new List<DealerScopeEntryDto>(),
            Suppliers = includeSharedLists ? new List<SupplierScopeEntryDto>(scope.Suppliers) : new List<SupplierScopeEntryDto>(),
        };
    }

    private void FlushPendingCustomerScopeSyncMessages()
    {
        if (_pendingCustomerScopeSyncMessages.Count == 0 || _pendingCustomerScopeSyncsDueAtUtc > DateTime.UtcNow)
        {
            return;
        }

        int sent = 0;
        DateTime nextDueAtUtc = DateTime.UtcNow + CustomerScopeSyncSendInterval;
        while (_pendingCustomerScopeSyncMessages.Count > 0 && sent < MaxCustomerScopeSyncMessagesPerFlush)
        {
            PendingCustomerScopeSyncMessage message = _pendingCustomerScopeSyncMessages[0];
            _pendingCustomerScopeSyncMessages.RemoveAt(0);

            if (!IsSendConnectionUsable(message.Connection))
            {
                continue;
            }

            CustomMessaging.SendToClient(message.Connection, message.MessageType, message.Payload);
            sent++;
        }

        if (_pendingCustomerScopeSyncMessages.Count > 0)
        {
            _pendingCustomerScopeSyncsDueAtUtc = nextDueAtUtc;
        }
        else
        {
            _pendingCustomerScopeSyncsDueAtUtc = DateTime.MinValue;
        }
    }

    private void BroadcastCustomerScopeSync(IEnumerable<string> steamIds)
    {
        HashSet<string> uniqueSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string steamId in steamIds)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !uniqueSteamIds.Add(steamId))
            {
                continue;
            }

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            SendCustomerScopeSync(connectedPlayer.Connection, steamId);
        }
    }

    private void BroadcastQuestScopeSync(IEnumerable<string> steamIds)
    {
        HashSet<string> uniqueSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string steamId in steamIds)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !uniqueSteamIds.Add(steamId))
            {
                continue;
            }

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            SendQuestScopeSync(connectedPlayer.Connection, steamId);
        }
    }

    private void BroadcastQuestScopeSyncThrottled(IEnumerable<string> steamIds)
    {
        DateTime dueAtUtc = DateTime.UtcNow + QuestScopeSyncDebounce;
        foreach (string steamId in steamIds)
        {
            if (string.IsNullOrWhiteSpace(steamId))
            {
                continue;
            }

            _pendingQuestScopeSyncs[steamId] = dueAtUtc;
        }
    }

    private void BroadcastQuestScopeSyncForOwners(IEnumerable<string> ownerKeys)
    {
        foreach (string ownerKey in ownerKeys)
        {
            if (string.IsNullOrWhiteSpace(ownerKey))
            {
                continue;
            }

            BroadcastQuestScopeSyncThrottled(_organisationService.BuildSnapshotByOwnerKey(ownerKey).MemberSteamIds);
        }
    }

    private void FlushPendingQuestScopeSyncs()
    {
        if (_pendingQuestScopeSyncs.Count == 0)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        List<string> dueSteamIds = new List<string>();
        foreach (KeyValuePair<string, DateTime> entry in _pendingQuestScopeSyncs)
        {
            if (entry.Value <= nowUtc)
            {
                dueSteamIds.Add(entry.Key);
            }
        }

        if (dueSteamIds.Count == 0)
        {
            return;
        }

        foreach (string steamId in dueSteamIds)
        {
            _pendingQuestScopeSyncs.Remove(steamId);

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            SendQuestScopeSync(connectedPlayer.Connection, steamId);
        }
    }

    private void FlushPendingQuestScopeSyncMessages()
    {
        if (_pendingQuestScopeSyncMessages.Count == 0 || _pendingQuestScopeSyncMessagesDueAtUtc > DateTime.UtcNow)
        {
            return;
        }

        PendingScopeSyncMessage message = _pendingQuestScopeSyncMessages[0];
        _pendingQuestScopeSyncMessages.RemoveAt(0);

        if (IsSendConnectionUsable(message.Connection))
        {
            _logger.Info($"[QuestScopeDiag] Sending quest scope message. MessageType={message.MessageType}, ConnectionId={message.Connection.ClientId}, PayloadChars={message.Payload?.Length ?? 0}, RemainingAfterSend={_pendingQuestScopeSyncMessages.Count}.");
            CustomMessaging.SendToClient(message.Connection, message.MessageType, message.Payload);
        }
        else
        {
            _logger.Warning($"[QuestScopeDiag] Skipped quest scope message flush because connection became unusable. MessageType={message.MessageType}, ConnectionId={message.Connection?.ClientId.ToString() ?? "null"}, PayloadChars={message.Payload?.Length ?? 0}, RemainingAfterDrop={_pendingQuestScopeSyncMessages.Count}.");
        }

        _pendingQuestScopeSyncMessagesDueAtUtc = _pendingQuestScopeSyncMessages.Count > 0
            ? DateTime.UtcNow + QuestScopeSyncSendInterval
            : DateTime.MinValue;
    }

    private void FlushPendingCustomerScopeSyncs()
    {
        if (_pendingCustomerScopeSyncs.Count == 0)
        {
            return;
        }

        DateTime nowUtc = DateTime.UtcNow;
        List<string> dueSteamIds = new List<string>();
        foreach (KeyValuePair<string, DateTime> entry in _pendingCustomerScopeSyncs)
        {
            if (entry.Value <= nowUtc)
            {
                dueSteamIds.Add(entry.Key);
            }
        }

        foreach (string steamId in dueSteamIds)
        {
            _pendingCustomerScopeSyncs.Remove(steamId);

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            SendCustomerScopeSync(connectedPlayer.Connection, steamId);
        }
    }

    private void InstallRuntimeHooks()
    {
        if (_runtimeHooksInstalled)
        {
            return;
        }

#if !IL2CPP
        Application.logMessageReceivedThreaded += OnUnityLogMessageReceived;
#endif
        AppDomain.CurrentDomain.FirstChanceException += OnFirstChanceException;
        _runtimeHooksInstalled = true;
    }

    private void RemoveRuntimeHooks()
    {
        if (!_runtimeHooksInstalled)
        {
            return;
        }

#if !IL2CPP
        Application.logMessageReceivedThreaded -= OnUnityLogMessageReceived;
#endif
        AppDomain.CurrentDomain.FirstChanceException -= OnFirstChanceException;
        _runtimeHooksInstalled = false;
    }

    private void OnUnityLogMessageReceived(string condition, string stackTrace, LogType type)
    {
        if (type != LogType.Exception)
        {
            return;
        }

        LogDetailedException("Unity log callback", condition, stackTrace, null);
    }

    private void OnFirstChanceException(object? sender, FirstChanceExceptionEventArgs args)
    {
        Exception exception = args.Exception;
        Exception relevantException = exception is System.Reflection.TargetInvocationException tie && tie.InnerException != null
            ? tie.InnerException
            : exception;

        if (relevantException is not NullReferenceException)
        {
            return;
        }

        string stackTrace = relevantException.StackTrace ?? exception.StackTrace ?? string.Empty;
        LogDetailedException("First-chance exception", relevantException.Message, stackTrace, relevantException);
    }

    private void LogDetailedException(string source, string message, string stackTrace, Exception? exception)
    {
        bool looksLikeNullReference = (message?.Contains("NullReferenceException", StringComparison.Ordinal) ?? false)
            || (stackTrace?.Contains("NullReferenceException", StringComparison.Ordinal) ?? false)
            || exception is NullReferenceException;
        bool looksLikeCoroutineFailure = (message?.Contains("Coroutine continue failure", StringComparison.Ordinal) ?? false)
            || (stackTrace?.Contains("Coroutine continue failure", StringComparison.Ordinal) ?? false);
        if (!looksLikeNullReference && !looksLikeCoroutineFailure)
        {
            return;
        }

        string signature = string.Concat(source, "|", message, "|", stackTrace);
        if (signature == _lastExceptionSignature && (DateTime.UtcNow - _lastExceptionAtUtc).TotalSeconds < 1)
        {
            return;
        }

        _lastExceptionSignature = signature;
        _lastExceptionAtUtc = DateTime.UtcNow;

        _logger.Error($"[{source}] {message}");
        if (!string.IsNullOrWhiteSpace(stackTrace))
        {
            _logger.Error($"[{source}] stack trace:\n{stackTrace}");
        }

        if (exception?.TargetSite != null)
        {
            _logger.Error($"[{source}] target site: {exception.TargetSite.DeclaringType?.FullName}.{exception.TargetSite.Name}");
        }
    }

    internal void LogRuntimeWarning(string message, Exception? exception = null)
    {
        _logger.Warning(message);
        if (!string.IsNullOrWhiteSpace(exception?.StackTrace))
        {
            _logger.Warning(exception.StackTrace!);
        }
    }

    private void RefreshConnectedPlayerSnapshots()
    {
        if (S1DS.Server.Players == null)
        {
            return;
        }

        HashSet<string> seenSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var connectedPlayer in S1DS.Server.Players.GetConnectedPlayers())
        {
            if (connectedPlayer == null || connectedPlayer.IsLoopbackConnection || connectedPlayer.Connection == null)
            {
                continue;
            }

            string steamId = ResolveSteamId(connectedPlayer);
            if (string.IsNullOrWhiteSpace(steamId) || !seenSteamIds.Add(steamId))
            {
                continue;
            }

            _organisationService.EnsureConfiguredTeamMembership(new PlayerIdentity
            {
                SteamId = steamId,
                PlayerName = connectedPlayer.PlayerName ?? connectedPlayer.DisplayName,
            });
            SendSnapshot(connectedPlayer.Connection, steamId);
        }
    }

    private bool TryChooseSupplierMeetingLocation(string ownerKey, out int locationIndex)
    {
        locationIndex = -1;
        List<int> candidateIndexes = new List<int>();
        for (int i = 0; i < SupplierLocation.AllLocations.Count; i++)
        {
            SupplierLocation? location = SupplierLocation.AllLocations[i];
            if (location == null || location.IsOccupied || IsSupplierMeetingLocationReserved(i))
            {
                continue;
            }

            candidateIndexes.Add(i);
        }

        if (candidateIndexes.Count == 0)
        {
            return false;
        }

        locationIndex = candidateIndexes[UnityEngine.Random.Range(0, candidateIndexes.Count)];
        return locationIndex >= 0;
    }

    private bool IsSupplierMeetingLocationReserved(int locationIndex)
    {
        if (locationIndex < 0)
        {
            return false;
        }

        foreach (SupplierMeetingScopeRecord meeting in _activeSupplierMeetingsBySupplierAndOwner.Values)
        {
            if (meeting.LocationIndex == locationIndex)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryResolvePlayerSteamId(Player player, out string steamId)
    {
        steamId = string.Empty;
        if (player == null)
        {
            return false;
        }

        ConnectedPlayerInfo? connectedPlayer = player.Connection != null && S1DS.Server.Players != null
            ? S1DS.Server.Players.GetPlayer(player.Connection)
            : null;
        if (connectedPlayer != null)
        {
            steamId = ResolveSteamId(connectedPlayer);
        }

        if (string.IsNullOrWhiteSpace(steamId))
        {
            steamId = player.PlayerCode ?? string.Empty;
        }

        return !string.IsNullOrWhiteSpace(steamId);
    }

    private bool TryFindCartelAmbushTarget(string ownerKey, Vector3 origin, out Player? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        float bestDistance = float.MaxValue;
        foreach (Player candidate in Player.PlayerList)
        {
            if (!CanPlayerBeCartelAmbushed(candidate)
                || !TryResolvePlayerSteamId(candidate, out string steamId)
                || !string.Equals(_organisationService.ResolveOwnerKey(steamId), ownerKey, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            float distance = Vector3.Distance(candidate.Avatar.CenterPoint, origin);
            if (distance > 10f || distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            target = candidate;
        }

        return target != null;
    }

    private bool TryFindRandomPlayerInOwnerScope(string ownerKey, out Player? target)
    {
        target = null;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        List<Player> candidates = new List<Player>();
        foreach (Player candidate in Player.PlayerList)
        {
            if (IsPlayerInOwnerScope(candidate, ownerKey) && CanReceiveCustomerProductRequest(candidate))
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            return false;
        }

        target = candidates[UnityEngine.Random.Range(0, candidates.Count)];
        return true;
    }

    private bool TryFindClosestPlayerInOwnerScope(string ownerKey, Vector3 origin, out Player? target, out float distance)
    {
        target = null;
        distance = 0f;
        if (string.IsNullOrWhiteSpace(ownerKey))
        {
            return false;
        }

        float bestDistance = float.MaxValue;
        foreach (Player candidate in Player.PlayerList)
        {
            if (!IsPlayerInOwnerScope(candidate, ownerKey) || !CanReceiveCustomerProductRequest(candidate))
            {
                continue;
            }

            float candidateDistance = Vector3.Distance(candidate.transform.position, origin);
            if (candidateDistance >= bestDistance)
            {
                continue;
            }

            bestDistance = candidateDistance;
            target = candidate;
        }

        distance = bestDistance;
        return target != null;
    }

    private bool IsPlayerInOwnerScope(Player player, string ownerKey)
    {
        return player != null
            && TryResolvePlayerSteamId(player, out string steamId)
            && string.Equals(_organisationService.ResolveOwnerKey(steamId), ownerKey, StringComparison.OrdinalIgnoreCase);
    }

    private static bool CanReceiveCustomerProductRequest(Player player)
    {
        return player != null
            && !player.IsArrested
            && player.Health != null
            && player.Health.IsAlive
            && !player.IsSleeping;
    }

    private static bool CanPlayerBeCartelAmbushed(Player player)
    {
        if (player == null || player.Health == null || !player.Health.IsAlive)
        {
            return false;
        }

        return player.CrimeData != null
            && !player.CrimeData.BodySearchPending
            && player.CrimeData.CurrentPursuitLevel == PlayerCrimeData.EPursuitLevel.None;
    }

    private static void SetCartelActivityMinsSinceActivation(CartelActivity activity, int value)
    {
        CartelActivityMinsSinceActivationField?.SetValue(activity, Math.Max(0, value));
    }

    private bool TryGetStrictCartelActivityOwner(EMapRegion region, out string ownerKey)
    {
        if (_activeCartelRegionalActivityOwnersByRegion.TryGetValue(region, out string? regionalOwnerKey)
            && !string.IsNullOrWhiteSpace(regionalOwnerKey))
        {
            ownerKey = regionalOwnerKey;
            return true;
        }

        if (_activeCartelGlobalActivityOwnersByRegion.TryGetValue(region, out string? globalOwnerKey)
            && !string.IsNullOrWhiteSpace(globalOwnerKey))
        {
            ownerKey = globalOwnerKey;
            return true;
        }

        ownerKey = string.Empty;
        return false;
    }

    private bool TryGetActiveDealerSaleOwner(Dealer dealer, out string ownerKey)
    {
        ownerKey = _activeDealerSaleOwnerKey ?? string.Empty;
        return dealer != null
            && !string.IsNullOrWhiteSpace(ownerKey)
            && !string.IsNullOrWhiteSpace(_activeDealerSaleDealerId)
            && string.Equals(dealer.ID, _activeDealerSaleDealerId, StringComparison.OrdinalIgnoreCase);
    }

    private EMapRegion ResolveUnlockedCartelNotificationRegion(string ownerKey, EMapRegion region)
    {
        if (_questScopeService.IsOwnerMapRegionUnlocked(ownerKey, region))
        {
            return region;
        }

        EMapRegion[] regions = (EMapRegion[])Enum.GetValues(typeof(EMapRegion));
        foreach (EMapRegion candidate in regions)
        {
            if (_questScopeService.IsOwnerMapRegionUnlocked(ownerKey, candidate))
            {
                return candidate;
            }
        }

        return region;
    }

    private static CartelRegionActivities? GetAmbushRegionActivities(Ambush ambush)
    {
        if (AmbushRegionActivitiesField?.GetValue(ambush) is CartelRegionActivities regionActivities)
        {
            return regionActivities;
        }

        return NetworkSingleton<Cartel>.Instance?.Activities?.GetRegionalActivities(ambush.Region);
    }

    private void SendSupplierMeetingConfirmation(NetworkConnection connection, Supplier supplier, int locationIndex)
    {
        SupplierLocation? location = GetSupplierLocation(locationIndex);
        string locationDescription = !string.IsNullOrWhiteSpace(location?.LocationDescription)
            ? location.LocationDescription
            : "the selected meeting location";
        string supplierName = !string.IsNullOrWhiteSpace(supplier?.FirstName)
            ? supplier.FirstName
            : "Supplier";
        SendNotification(connection, supplierName, $"Meet at {locationDescription}.");
    }

    private static void SendScopedDarkMarketUnlock(DarkMarket darkMarket, IEnumerable<string> steamIds)
    {
        if (DarkMarketSetUnlockedMethod == null)
        {
            return;
        }

        HashSet<string> uniqueSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string steamId in steamIds)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !uniqueSteamIds.Add(steamId))
            {
                continue;
            }

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            DarkMarketSetUnlockedMethod.Invoke(darkMarket, new object[] { connectedPlayer.Connection });
        }
    }

    private static void SendScopedSewerUnlock(SewerManager sewerManager, IEnumerable<string> steamIds)
    {
        if (SewerManagerSetSewerUnlockedClientMethod == null)
        {
            return;
        }

        HashSet<string> uniqueSteamIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string steamId in steamIds)
        {
            if (string.IsNullOrWhiteSpace(steamId) || !uniqueSteamIds.Add(steamId))
            {
                continue;
            }

            ConnectedPlayerInfo? connectedPlayer = FindConnectedPlayer(steamId);
            if (connectedPlayer?.Connection == null)
            {
                continue;
            }

            SewerManagerSetSewerUnlockedClientMethod.Invoke(sewerManager, new object[] { connectedPlayer.Connection });
        }
    }

    private static SupplierLocation? GetSupplierLocation(int locationIndex)
    {
        if (locationIndex < 0 || locationIndex >= SupplierLocation.AllLocations.Count)
        {
            return null;
        }

        return SupplierLocation.AllLocations[locationIndex];
    }

    private void InvokeScopedSupplierMeeting(Supplier supplier, NetworkConnection connection, int locationIndex, int expireInMinutes)
    {
        _supplierMeetingReplayDepth++;
        try
        {
            supplier.MeetAtLocation(connection, locationIndex, Math.Max(0, expireInMinutes));
            RefreshScopedSupplierMeeting(supplier);
        }
        finally
        {
            _supplierMeetingReplayDepth--;
        }
    }

    private static int GetSupplierMinsSinceMeetingStart(Supplier supplier)
    {
        if (SupplierMinsSinceMeetingStartField == null)
        {
            return -1;
        }

        object? value = SupplierMinsSinceMeetingStartField.GetValue(supplier);
        return value is int minsSinceMeetingStart ? minsSinceMeetingStart : -1;
    }

    private bool TryGetActiveSupplierMeeting(string supplierId, out SupplierMeetingScopeRecord? activeMeeting)
    {
        activeMeeting = null;
        if (string.IsNullOrWhiteSpace(supplierId))
        {
            return false;
        }

        foreach (SupplierMeetingScopeRecord meeting in _activeSupplierMeetingsBySupplierAndOwner.Values)
        {
            if (string.Equals(meeting.SupplierId, supplierId, StringComparison.OrdinalIgnoreCase))
            {
                activeMeeting = meeting;
                return true;
            }
        }

        return false;
    }

    private static string BuildSyncCacheKey(NetworkConnection connection, string steamId)
    {
        if (connection == null)
        {
            return steamId ?? string.Empty;
        }

        return string.Concat(steamId ?? string.Empty, "|", connection.ClientId.ToString());
    }

    private static string BuildSupplierMeetingKey(string supplierId, string ownerKey)
    {
        return string.Concat(supplierId ?? string.Empty, "|", ownerKey ?? string.Empty);
    }

    private static ConnectedPlayerInfo? FindConnectedPlayer(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier) || S1DS.Server.Players == null)
        {
            return null;
        }

        ConnectedPlayerInfo? bySteamId = S1DS.Server.Players.GetPlayerBySteamId(identifier);
        if (bySteamId != null)
        {
            return bySteamId;
        }

        return S1DS.Server.Players.GetPlayerByName(identifier);
    }

    private static string ResolveSteamId(ConnectedPlayerInfo player)
    {
        if (player == null)
        {
            return string.Empty;
        }

        return !string.IsNullOrWhiteSpace(player.AuthenticatedSteamId)
            ? player.AuthenticatedSteamId
            : player.SteamId ?? string.Empty;
    }

    private void EnsureRuntimeDependencies()
    {
        if (_logger != null)
        {
            return;
        }

        _logger = new OrganisationLogger(LoggerInstance);
        _skipQuestScopeSyncSend = HasCommandLineArg("--org-diag-skip-quest-scope-sync-send");
        if (_skipQuestScopeSyncSend)
        {
            _logger.Warning("Quest scope send diagnostics active. Server will skip quest scope sync messages.");
        }

        _maxQuestScopeChunksToSend = GetCommandLineIntArg("--org-diag-max-quest-scope-chunks", int.MaxValue);
        if (_maxQuestScopeChunksToSend != int.MaxValue)
        {
            _maxQuestScopeChunksToSend = Math.Max(0, _maxQuestScopeChunksToSend);
            _logger.Warning($"Quest scope chunk diagnostics active. Server will send at most {_maxQuestScopeChunksToSend} quest scope chunks per sync.");
        }

        _logQuestVariableDiagnostics = HasCommandLineArg("--org-diag-log-quest-variables");
        if (_logQuestVariableDiagnostics)
        {
            _logger.Warning("Quest variable diagnostics active. Server will log scoped variable mutation routing decisions.");
        }

        _logDeaddropDiagnostics = HasCommandLineArg("--org-diag-log-deaddrops");
        if (_logDeaddropDiagnostics)
        {
            _logger.Warning("Dead-drop diagnostics active. Server will log scoped DeadDrop.UpdateDeadDrop handling decisions.");
        }

        _config = OrganisationServerConfig.Load(_logger);
        _repository = new FileOrganisationRepository(_logger);
        _organisationService = new OrganisationService(_repository, _logger, _config);
        _questScopeService = new OrganisationQuestScopeService(_repository, _organisationService, _logger);
        _propertyScopeService = new OrganisationPropertyScopeService(_repository, _organisationService, _logger);
        _vehicleAccessService = new OrganisationVehicleAccessService(_repository, _logger);
        _customerContractService = new OrganisationCustomerContractService(_repository, _organisationService, _logger);
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

    private static int GetCommandLineIntArg(string arg, int fallback)
    {
        string[] args = Environment.GetCommandLineArgs();
        for (int index = 0; index < args.Length; index++)
        {
            string candidate = args[index];
            if (candidate.StartsWith(arg + "=", StringComparison.OrdinalIgnoreCase))
            {
                return int.TryParse(candidate.Substring(arg.Length + 1), out int parsed)
                    ? parsed
                    : fallback;
            }

            if (string.Equals(candidate, arg, StringComparison.OrdinalIgnoreCase)
                && index + 1 < args.Length
                && int.TryParse(args[index + 1], out int nextParsed))
            {
                return nextParsed;
            }
        }

        return fallback;
    }

    private sealed class SupplierMeetingScopeRecord
    {
        public SupplierMeetingScopeRecord(string ownerKey, string supplierId, int locationIndex, int expireInMinutes)
        {
            OwnerKey = ownerKey;
            SupplierId = supplierId;
            LocationIndex = locationIndex;
            ExpireInMinutes = expireInMinutes;
        }

        public string OwnerKey { get; }
        public string SupplierId { get; }
        public int LocationIndex { get; }
        public int ExpireInMinutes { get; }
        public int ElapsedMinutes { get; private set; }

        public int GetRemainingMinutes()
        {
            return Math.Max(0, ExpireInMinutes - ElapsedMinutes);
        }

        public void SetElapsedMinutes(int elapsedMinutes)
        {
            ElapsedMinutes = Math.Max(0, elapsedMinutes);
        }
    }

    private sealed class PendingCustomerScopeSyncMessage
    {
        public PendingCustomerScopeSyncMessage(NetworkConnection connection, string cacheKey, string messageType, string payload)
        {
            Connection = connection;
            CacheKey = cacheKey;
            MessageType = messageType;
            Payload = payload;
        }

        public NetworkConnection Connection { get; }
        public string CacheKey { get; }
        public string MessageType { get; }
        public string Payload { get; }
    }

    private sealed class PendingScopeSyncMessage
    {
        public PendingScopeSyncMessage(NetworkConnection connection, string messageType, string payload)
        {
            Connection = connection;
            MessageType = messageType;
            Payload = payload;
        }

        public NetworkConnection Connection { get; }
        public string MessageType { get; }
        public string Payload { get; }
    }

    private sealed class ScopedGlobalCartelActivityCandidate
    {
        public ScopedGlobalCartelActivityCandidate(string ownerKey, EMapRegion region, float influence)
        {
            OwnerKey = ownerKey;
            Region = region;
            Influence = Mathf.Clamp01(influence);
        }

        public string OwnerKey { get; }
        public EMapRegion Region { get; }
        public float Influence { get; }
    }

    private sealed class PendingCartelAmbushInfluenceOwner
    {
        public PendingCartelAmbushInfluenceOwner(string ownerKey, Player target, DateTime createdAtUtc)
        {
            OwnerKey = ownerKey;
            Target = target;
            CreatedAtUtc = createdAtUtc;
        }

        public string OwnerKey { get; }
        public Player Target { get; }
        public DateTime CreatedAtUtc { get; }
    }
}
#endif
