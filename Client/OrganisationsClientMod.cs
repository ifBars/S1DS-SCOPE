#if CLIENT
using System;
using System.Collections;
using System.Linq;
using System.Runtime.ExceptionServices;
using DedicatedServerMod.API;
using DedicatedServerMod.Organisations.Client.Patches;
using DedicatedServerMod.Organisations.Client.Services;
using DedicatedServerMod.Organisations.Client.Testing;
using DedicatedServerMod.Organisations.Client.UI;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Utils;
using DedicatedServerMod.Shared.Networking;
using HarmonyLib;
using MelonLoader;
using Newtonsoft.Json;
#if IL2CPP
using Il2CppFishNet.Object;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.ATM;
using Il2CppScheduleOne.UI.Phone;
using Il2CppScheduleOne.UI.Shop;
#else
using FishNet.Object;
using ScheduleOne.DevUtilities;
using ScheduleOne.Economy;
using ScheduleOne.Money;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Property;
using ScheduleOne.Quests;
using ScheduleOne.UI;
using ScheduleOne.UI.ATM;
using ScheduleOne.UI.Phone;
using ScheduleOne.UI.Shop;
#endif
using UnityEngine;

[assembly: MelonInfo(typeof(DedicatedServerMod.Organisations.Client.OrganisationsClientMod), DedicatedServerMod.Organisations.Constants.ModName, DedicatedServerMod.Organisations.Constants.ModVersion, DedicatedServerMod.Organisations.Constants.ModAuthor)]
[assembly: MelonGame("TVGS", "Schedule I")]

namespace DedicatedServerMod.Organisations.Client;

public sealed class OrganisationsClientMod : ClientMelonModBase
{
    internal static OrganisationsClientMod? ActiveInstance { get; private set; }

    private static readonly TimeSpan InitialSnapshotRequestDelay = TimeSpan.FromSeconds(2);

    private OrganisationLogger _logger = null!;
    private readonly OrganisationClientState _state = new OrganisationClientState();
    private OrganisationQuestScopeClientService _questScopeClientService = null!;
    private OrganisationCustomerScopeClientService _customerScopeClientService = null!;
    private OrganisationTeamAppearanceService _teamAppearanceService = null!;
    private OrganisationWorkflowSmokeRunner? _workflowSmokeRunner;
    private bool _snapshotRequested;
    private bool _snapshotRequestQueued;
    private DateTime _snapshotRequestDueAtUtc;
    private bool _hasSnapshot;
    private bool _isInitialized;
    private bool _pendingFirstJoinOnboarding;
    private bool _hasAcknowledgedOnboardingThisSession;
    private string? _pendingAtmRequestId;
    private OrganisationAtmTransactionResultDto? _pendingAtmResult;
    private string? _pendingShopCheckoutRequestId;
    private OrganisationShopCheckoutResultDto? _pendingShopCheckoutResult;
    private bool _pendingPropertyScopeApply;
    private string? _pendingUnavailableEstatePropertyCode;
    private string? _lastExceptionSignature;
    private DateTime _lastExceptionAtUtc;
    private bool _runtimeHooksInstalled;

    public override void OnInitializeMelon()
    {
        EnsureRuntimeDependencies();
        _logger.Info("Melon initialized.");
    }

    internal bool CanUseScopedFinance => _hasSnapshot;

    internal bool HasSnapshot => _hasSnapshot;

    internal bool IsApplyingQuestScopeSync => _questScopeClientService.IsApplyingScope;

    internal bool CanReceiveCartelDealQuest => _questScopeClientService.CanReceiveCartelDealQuest;

    internal bool CanApplyCartelActivityRpc => _questScopeClientService.IsApplyingScope;

    internal bool CanApplyCartelStatusRpc => !_hasSnapshot || _questScopeClientService.IsApplyingScope;

    internal bool CanApplyCartelInfluenceRpc => !_hasSnapshot || _questScopeClientService.IsApplyingScope;

    internal bool CanApplyCartelGraffitiSurfaceRpc(NetworkObject surfaceObject) => !_hasSnapshot || _questScopeClientService.CanApplyCartelGraffitiSurfaceRpc(surfaceObject);

    internal bool ShouldRunLoanSharkKidnap(Quest_TheDeepEnd quest) => _hasSnapshot && _questScopeClientService.ShouldRunLoanSharkKidnap(quest);

    internal OrganisationSnapshotDto Snapshot => _state.Snapshot;

    public override void OnClientInitialize()
    {
        EnsureRuntimeDependencies();

        if (_isInitialized)
        {
            _logger.Info("Client mod already initialized; skipping duplicate initialization.");
            return;
        }

        ResetRuntimeState();
        InstallRuntimeHooks();
        OrganisationIntroPatches.Initialize(HandleCharacterCreatorCompleted, message => _logger.Info($"[IntroPatch] {message}"));
        _isInitialized = true;
        _logger.Info("Client mod initialized and intro patch applied.");
    }

    public override void OnClientShutdown()
    {
        EnsureRuntimeDependencies();

        if (!_isInitialized)
        {
            ResetRuntimeState();
            return;
        }

        _logger.Info("Client mod shutting down.");
        RemoveRuntimeHooks();
        ResetRuntimeState();
        _isInitialized = false;

        if (ReferenceEquals(ActiveInstance, this))
        {
            ActiveInstance = null;
        }
    }

    public override void OnConnectedToServer()
    {
        EnsureRuntimeDependencies();
        _logger.Info("Connected to dedicated server; waiting for client player readiness before requesting organisation snapshot.");
    }

    public override void OnDisconnectedFromServer()
    {
        EnsureRuntimeDependencies();
        _logger.Info("Disconnected from dedicated server; clearing organisation client state.");
        ResetRuntimeState();
    }

    public override void OnClientPlayerReady()
    {
        EnsureRuntimeDependencies();
        _snapshotRequestQueued = true;
        _snapshotRequestDueAtUtc = DateTime.UtcNow + InitialSnapshotRequestDelay;
        _logger.Info("Client player ready hook observed; queued organisation snapshot refresh after client quest readiness delay.");
    }

    internal void Tick()
    {
        FlushPendingSnapshotRequest();
        TryAutoOpenOnboardingHub();
        TryApplyPropertyScopeSnapshot();
        _questScopeClientService.Tick();
        _customerScopeClientService.Tick();
        _workflowSmokeRunner?.Tick(_hasSnapshot, _state.Snapshot);
        if (_hasSnapshot)
        {
            _teamAppearanceService.Tick(_state.Snapshot);
        }
    }

    public override void OnUpdate()
    {
        EnsureRuntimeDependencies();
        Tick();
    }

    public override bool OnCustomMessage(string messageType, byte[] data)
    {
        EnsureRuntimeDependencies();
        string payload = data != null ? System.Text.Encoding.UTF8.GetString(data) : string.Empty;
        _logger.Info($"Received organisation custom message '{messageType}'. PayloadBytes={data?.Length ?? 0}, PayloadChars={payload.Length}.");
        switch (messageType)
        {
            case OrganisationMessages.Snapshot:
                HandleSnapshot(payload);
                return true;

            case OrganisationMessages.InviteReceived:
                ShowNotification("Organisation", "You received a new organisation invite.");
                RequestSnapshot();
                return true;

            case OrganisationMessages.Error:
                OrganisationErrorDto? error = Deserialize<OrganisationErrorDto>(payload);
                if (error != null)
                {
                    _logger.Warning(error.Message);
                    ShowNotification("Organisation", error.Message);
                }
                return true;

            case OrganisationMessages.Notification:
                OrganisationNotificationDto? notification = Deserialize<OrganisationNotificationDto>(payload);
                if (notification != null)
                {
                    ShowNotification(notification.Title, notification.Message);
                }
                return true;

            case OrganisationMessages.AtmTransactionResult:
                HandleAtmTransactionResult(payload);
                return true;

            case OrganisationMessages.ShopCheckoutResult:
                HandleShopCheckoutResult(payload);
                return true;

            case OrganisationMessages.QuestScopeSync:
                QuestScopeSyncDto? questScope = Deserialize<QuestScopeSyncDto>(payload);
                if (questScope != null)
                {
                    _logger.Info($"[QuestScopeDiag] Received single quest scope sync. OwnerKey={questScope.OwnerKey}, PayloadChars={payload.Length}, Quests={questScope.Quests.Count}, Contracts={questScope.Contracts.Count}, Deaddrops={questScope.Deaddrops.Count}.");
                    _questScopeClientService.QueueScopeSync(questScope);
                }
                return true;

            case OrganisationMessages.QuestScopeSyncChunk:
                QuestScopeSyncChunkDto? questScopeChunk = Deserialize<QuestScopeSyncChunkDto>(payload);
                if (questScopeChunk != null)
                {
                    _logger.Info($"[QuestScopeDiag] Received quest scope chunk {questScopeChunk.Sequence + 1}/{questScopeChunk.Total}. OwnerKey={questScopeChunk.OwnerKey}, PayloadChars={payload.Length}, Quests={questScopeChunk.Scope?.Quests.Count ?? 0}, Contracts={questScopeChunk.Scope?.Contracts.Count ?? 0}, Deaddrops={questScopeChunk.Scope?.Deaddrops.Count ?? 0}.");
                    _questScopeClientService.AddChunk(questScopeChunk);
                }
                return true;

            case OrganisationMessages.CustomerScopeSync:
                CustomerScopeSyncDto? customerScope = Deserialize<CustomerScopeSyncDto>(payload);
                if (customerScope != null)
                {
                    _customerScopeClientService.Replace(customerScope);
                }
                return true;

            case OrganisationMessages.CustomerScopeSyncChunk:
                CustomerScopeSyncChunkDto? customerScopeChunk = Deserialize<CustomerScopeSyncChunkDto>(payload);
                if (customerScopeChunk != null)
                {
                    _customerScopeClientService.AddChunk(customerScopeChunk);
                }
                return true;

            default:
                return false;
        }
    }

    private void RequestSnapshot()
    {
        if (_snapshotRequested)
        {
            _logger.Info("Skipped snapshot request because one is already pending.");
            return;
        }

        if (!CustomMessaging.IsEndpointReady)
        {
            _logger.Info("Skipped snapshot request because the custom messaging endpoint is not ready yet.");
            return;
        }

        if (!CustomMessaging.TrySendToServer(OrganisationMessages.SnapshotRequest, "{}"))
        {
            _logger.Warning("Failed to queue organisation snapshot request because the messaging backend rejected it.");
            return;
        }

        _snapshotRequested = true;
        _logger.Info("Sending organisation snapshot request to server.");
    }

    private void FlushPendingSnapshotRequest()
    {
        if (!_snapshotRequestQueued || _snapshotRequestDueAtUtc > DateTime.UtcNow)
        {
            return;
        }

        _snapshotRequestQueued = false;
        RequestSnapshot();
    }

    private void HandleSnapshot(string payload)
    {
        _snapshotRequested = false;
        _snapshotRequestQueued = false;
        OrganisationSnapshotDto? snapshot = Deserialize<OrganisationSnapshotDto>(payload);
        if (snapshot == null)
        {
            _logger.Warning("Failed to deserialize organisation snapshot payload.");
            return;
        }

        bool hadSnapshot = _hasSnapshot;
        float previousBalance = _state.Snapshot.OnlineBalance;
        _hasSnapshot = true;
        _state.Replace(snapshot);
        ApplyFinanceSnapshot(snapshot, hadSnapshot, previousBalance);
        _pendingPropertyScopeApply = true;
        TryApplyPropertyScopeSnapshot();
        UpdateOnboardingState(snapshot);
        _logger.Info($"Snapshot updated. HasOrganisation={snapshot.HasOrganisation}, PendingInvites={snapshot.PendingInvites.Count}, ShouldShowOnboarding={snapshot.ShouldShowOnboarding}, PlayerSteamId={snapshot.PlayerSteamId}.");
        NotifyPhoneAppStateChanged();
        TryAutoOpenOnboardingHub();
        _workflowSmokeRunner?.Tick(_hasSnapshot, _state.Snapshot);
    }

    private void TryApplyPropertyScopeSnapshot()
    {
        if (!_hasSnapshot || !_pendingPropertyScopeApply || Property.Properties.Count == 0)
        {
            return;
        }

        ApplyOwnedProperties(_state.Snapshot.OwnedPropertyCodes);
        _pendingPropertyScopeApply = false;
    }

    private void ApplyOwnedProperties(System.Collections.Generic.IEnumerable<string> ownedPropertyCodes)
    {
        System.Collections.Generic.HashSet<string> ownedCodes = new System.Collections.Generic.HashSet<string>(
            ownedPropertyCodes ?? Array.Empty<string>(),
            StringComparer.OrdinalIgnoreCase);

        foreach (Property property in Property.Properties)
        {
            if (property == null)
            {
                continue;
            }

            SetPropertyOwnedStateLocally(property, ownedCodes.Contains(property.PropertyCode));
        }
    }

    private void SetPropertyOwnedStateLocally(Property property, bool isOwned)
    {
        if (isOwned)
        {
            AccessTools.Method(property.GetType(), "RecieveOwned")?.Invoke(property, null);
            return;
        }

        AccessTools.Field(typeof(Property), "<IsOwned>k__BackingField")?.SetValue(property, false);
        Property.OwnedProperties.Remove(property);
        if (!Property.UnownedProperties.Contains(property))
        {
            Property.UnownedProperties.Add(property);
        }

        if (property.ForSaleSign != null)
        {
            property.ForSaleSign.gameObject.SetActive(true);
        }

        if (property.ListingPoster != null)
        {
            property.ListingPoster.gameObject.SetActive(true);
        }

        if (property.PoI != null)
        {
            property.PoI.gameObject.SetActive(false);
            property.PoI.SetMainText(property.PropertyName + " (Unowned)");
            SetPoiIconStateSafely(property, owned: false);
        }
    }

    private void SetPoiIconStateSafely(Property property, bool owned)
    {
        try
        {
            if (property?.PoI == null || property.PoI.IconContainer == null)
            {
                return;
            }

            Transform? ownedIcon = property.PoI.IconContainer.Find("Owned");
            Transform? unownedIcon = property.PoI.IconContainer.Find("Unowned");
            if (ownedIcon != null)
            {
                ownedIcon.gameObject.SetActive(owned);
            }

            if (unownedIcon != null)
            {
                unownedIcon.gameObject.SetActive(!owned);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning($"Failed to update POI icon state for property '{property?.PropertyCode}': {ex.Message}");
        }
    }

    private void HandleCharacterCreatorCompleted()
    {
        _logger.Info("Character creator completion observed; refreshing organisation state for phone app UX.");
        if (_hasSnapshot)
        {
            _teamAppearanceService.Tick(_state.Snapshot);
        }

        RequestSnapshot();
    }

    internal void CreateOrganisation(string organisationName)
    {
        if (string.IsNullOrWhiteSpace(organisationName))
        {
            ShowNotification("Organisation", "Enter an organisation name first.");
            _logger.Warning("Organisation creation submit ignored because the name was empty.");
            return;
        }

        CreateOrganisationRequestDto request = new CreateOrganisationRequestDto
        {
            Name = organisationName.Trim(),
        };

        _logger.Info($"Submitting organisation create request for '{request.Name}'.");
        CustomMessaging.SendToServer(OrganisationMessages.CreateRequest, JsonConvert.SerializeObject(request));
    }

    internal void InvitePlayer(string targetPlayer)
    {
        if (string.IsNullOrWhiteSpace(targetPlayer))
        {
            ShowNotification("Organisation", "Enter a player name or Steam ID first.");
            _logger.Warning("Organisation invite submit ignored because the target was empty.");
            return;
        }

        InvitePlayerRequestDto request = new InvitePlayerRequestDto
        {
            TargetPlayer = targetPlayer.Trim(),
        };

        _logger.Info($"Submitting organisation invite request for '{request.TargetPlayer}'.");
        CustomMessaging.SendToServer(OrganisationMessages.InviteRequest, JsonConvert.SerializeObject(request));
    }

    internal void RefreshSnapshotFromUi()
    {
        RequestSnapshot();
    }

    internal void SubmitQuestTracking(string questGuid, bool isTracked)
    {
        if (!_hasSnapshot || string.IsNullOrWhiteSpace(questGuid))
        {
            return;
        }

        QuestTrackingRequestDto request = new QuestTrackingRequestDto
        {
            QuestGuid = questGuid,
            IsTracked = isTracked,
        };

        _logger.Info($"Submitting scoped quest tracking update for {questGuid}: tracked={isTracked}.");
        CustomMessaging.SendToServer(OrganisationMessages.QuestTrackingRequest, JsonConvert.SerializeObject(request));
    }

    internal void SubmitCustomerOfferRejection(string npcGuid)
    {
        if (string.IsNullOrWhiteSpace(npcGuid))
        {
            return;
        }

        CustomerOfferRejectRequestDto request = new CustomerOfferRejectRequestDto
        {
            NpcGuid = npcGuid,
        };

        _logger.Info($"Submitting scoped customer offer rejection for {npcGuid}.");
        CustomMessaging.SendToServer(OrganisationMessages.CustomerOfferRejectRequest, JsonConvert.SerializeObject(request));
    }

    internal void OpenOrganisationHub(bool preferOnboarding)
    {
        OrganisationsPhoneApp? app = OrganisationsPhoneApp.Instance;
        if (app == null || !Singleton<GameplayMenu>.InstanceExists)
        {
            return;
        }

        GameplayMenu menu = Singleton<GameplayMenu>.Instance;
        if (!menu.IsOpen)
        {
            menu.SetIsOpen(true);
        }

        if (menu.CurrentScreen != GameplayMenu.EGameplayScreen.Phone)
        {
            menu.SetScreen(GameplayMenu.EGameplayScreen.Phone);
        }

        if (Phone.ActiveApp != null && !app.IsOpen())
        {
            PlayerSingleton<Phone>.Instance.RequestCloseApp();
        }

        if (preferOnboarding)
        {
            app.PrepareForOnboarding();
        }

        app.OpenApp();
        if (preferOnboarding)
        {
            app.FocusCreateInput();
        }
    }

    internal void NotifyOnboardingHubShown()
    {
        if (_hasAcknowledgedOnboardingThisSession)
        {
            return;
        }

        _hasAcknowledgedOnboardingThisSession = true;
        _pendingFirstJoinOnboarding = false;
        CustomMessaging.SendToServer(OrganisationMessages.OnboardingPromptSeenRequest, "{}");
    }

    internal void PromptCreateOrganisation()
    {
        if (!_hasSnapshot)
        {
            ShowNotification("Organisation", "Still loading organisation data.");
            return;
        }

        if (_state.Snapshot.HasOrganisation)
        {
            ShowNotification("Organisation", "You are already in an organisation.");
            return;
        }

        OpenOrganisationHub(preferOnboarding: true);
    }

    internal void PromptInvitePlayer()
    {
        if (!_hasSnapshot || !_state.Snapshot.HasOrganisation)
        {
            ShowNotification("Organisation", "Join or create an organisation first.");
            return;
        }

        OpenOrganisationHub(preferOnboarding: false);
    }

    internal void AcceptInvite(string inviteId)
    {
        if (string.IsNullOrWhiteSpace(inviteId))
        {
            return;
        }

        CustomMessaging.SendToServer(OrganisationMessages.InviteAcceptRequest, JsonConvert.SerializeObject(new InviteActionRequestDto
        {
            InviteId = inviteId,
        }));
    }

    internal void DeclineInvite(string inviteId)
    {
        if (string.IsNullOrWhiteSpace(inviteId))
        {
            return;
        }

        CustomMessaging.SendToServer(OrganisationMessages.InviteDeclineRequest, JsonConvert.SerializeObject(new InviteActionRequestDto
        {
            InviteId = inviteId,
        }));
    }

    internal void LeaveOrganisation()
    {
        CustomMessaging.SendToServer(OrganisationMessages.LeaveRequest, JsonConvert.SerializeObject(new LeaveOrganisationRequestDto()));
    }

    internal void KickMember(string memberSteamId)
    {
        if (string.IsNullOrWhiteSpace(memberSteamId))
        {
            return;
        }

        CustomMessaging.SendToServer(OrganisationMessages.KickRequest, JsonConvert.SerializeObject(new KickOrganisationMemberRequestDto
        {
            MemberSteamId = memberSteamId,
        }));
    }

    internal void TransferOwnership(string newOwnerSteamId)
    {
        if (string.IsNullOrWhiteSpace(newOwnerSteamId))
        {
            return;
        }

        CustomMessaging.SendToServer(OrganisationMessages.TransferOwnershipRequest, JsonConvert.SerializeObject(new TransferOrganisationOwnershipRequestDto
        {
            NewOwnerSteamId = newOwnerSteamId,
        }));
    }

    private void HandleAtmTransactionResult(string payload)
    {
        OrganisationAtmTransactionResultDto? result = Deserialize<OrganisationAtmTransactionResultDto>(payload);
        if (result == null)
        {
            _logger.Warning("Failed to deserialize ATM transaction result payload.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingAtmRequestId) && string.Equals(_pendingAtmRequestId, result.RequestId, StringComparison.OrdinalIgnoreCase))
        {
            _pendingAtmResult = result;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            if (result.Success)
            {
                _logger.Info(result.Message);
            }
            else
            {
                _logger.Warning(result.Message);
            }
        }
    }

    private void HandleShopCheckoutResult(string payload)
    {
        OrganisationShopCheckoutResultDto? result = Deserialize<OrganisationShopCheckoutResultDto>(payload);
        if (result == null)
        {
            _logger.Warning("Failed to deserialize shop checkout result payload.");
            return;
        }

        if (!string.IsNullOrWhiteSpace(_pendingShopCheckoutRequestId)
            && string.Equals(_pendingShopCheckoutRequestId, result.RequestId, StringComparison.OrdinalIgnoreCase))
        {
            _pendingShopCheckoutResult = result;
        }

        if (!string.IsNullOrWhiteSpace(result.Message))
        {
            if (result.Success)
            {
                _logger.Info(result.Message);
            }
            else
            {
                _logger.Warning(result.Message);
            }
        }
    }

    internal string GetAtmUnavailableReason()
    {
        if (!_hasSnapshot)
        {
            return "Loading account";
        }

        return string.Empty;
    }

    internal bool IsPropertyReservedByOtherScope(string propertyCode)
    {
        if (!_hasSnapshot || string.IsNullOrWhiteSpace(propertyCode))
        {
            return false;
        }

        bool isReserved = _state.Snapshot.ReservedPropertyCodes.Contains(propertyCode, StringComparer.OrdinalIgnoreCase);
        if (!isReserved)
        {
            return false;
        }

        return !_state.Snapshot.OwnedPropertyCodes.Contains(propertyCode, StringComparer.OrdinalIgnoreCase);
    }

    internal bool DoesScopeOwnProperty(string propertyCode)
    {
        return _hasSnapshot
            && !string.IsNullOrWhiteSpace(propertyCode)
            && _state.Snapshot.OwnedPropertyCodes.Contains(propertyCode, StringComparer.OrdinalIgnoreCase);
    }

    internal bool CanAccessProperty(string propertyCode)
    {
        return _hasSnapshot
            && !string.IsNullOrWhiteSpace(propertyCode)
            && _state.Snapshot.AccessiblePropertyCodes.Contains(propertyCode, StringComparer.OrdinalIgnoreCase);
    }

    internal bool ShouldSuppressVanillaPropertyOwnedBroadcast(Property? property)
    {
        return property != null && !string.IsNullOrWhiteSpace(property.PropertyCode);
    }

    internal bool CanAccessVehicle(string vehicleGuid)
    {
        return _hasSnapshot
            && !string.IsNullOrWhiteSpace(vehicleGuid)
            && _state.Snapshot.AccessibleVehicleGuids.Contains(vehicleGuid, StringComparer.OrdinalIgnoreCase);
    }

    internal bool CanAccessDeaddrop(string deaddropGuid)
    {
        return _questScopeClientService.CanAccessDeaddrop(deaddropGuid);
    }

    internal void RefreshScopedDeaddropAffordance(DeadDrop drop)
    {
        _questScopeClientService.RefreshDeaddropAffordance(drop);
    }

    internal bool ShouldForceShowEstateAgentChoice(string choiceLabel)
    {
        return string.Equals(choiceLabel, "motelroom", StringComparison.OrdinalIgnoreCase)
            && IsPropertyReservedByOtherScope("motelroom");
    }

    internal void SetPendingUnavailableEstateProperty(string propertyCode)
    {
        _pendingUnavailableEstatePropertyCode = propertyCode;
    }

    internal void ClearPendingUnavailableEstateProperty()
    {
        _pendingUnavailableEstatePropertyCode = null;
    }

    internal string ModifyEstateAgentDialogueText(string dialogueLabel, string dialogueText)
    {
        if (!string.Equals(_pendingUnavailableEstatePropertyCode, "motelroom", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(dialogueLabel, "CONFIRM", StringComparison.Ordinal))
        {
            return dialogueText;
        }

        return "Sorry, the motel room has already been bought by another player. You will need to make your start somewhere else.";
    }

    internal string ModifyEstateAgentChoiceText(string choiceLabel, string choiceText)
    {
        if (string.Equals(choiceLabel, "motelroom", StringComparison.OrdinalIgnoreCase)
            && IsPropertyReservedByOtherScope("motelroom"))
        {
            return "Motel room <color=#D96C6C>(already taken)</color>";
        }

        if (!string.Equals(_pendingUnavailableEstatePropertyCode, "motelroom", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(choiceLabel, "CONFIRM_CHOICE", StringComparison.Ordinal))
        {
            return choiceText;
        }

        return "I need another option";
    }

    internal bool ShouldHideEstateAgentChoice(string choiceLabel)
    {
        Property? property = FindProperty(choiceLabel);
        if (property == null)
        {
            return false;
        }

        return DoesScopeOwnProperty(property.PropertyCode);
    }

    internal bool TryValidateEstateAgentChoice(string choiceLabel, out string invalidReason)
    {
        Property? property = FindProperty(choiceLabel);
        if (property == null)
        {
            invalidReason = string.Empty;
            return true;
        }

        return TryValidateScopedPropertyChoice(property, out invalidReason);
    }

    internal bool TryValidateEstateAgentConfirmation(Property property, out string invalidReason)
    {
        return TryValidateScopedPropertyChoice(property, out invalidReason);
    }

    internal bool TryValidateCashPropertyPurchase(Property property, float price, out string invalidReason)
    {
        invalidReason = string.Empty;
        if (property == null || string.IsNullOrWhiteSpace(property.PropertyCode))
        {
            return true;
        }

        if (!_hasSnapshot)
        {
            invalidReason = "Loading account";
            return false;
        }

        if (DoesScopeOwnProperty(property.PropertyCode))
        {
            invalidReason = _state.Snapshot.HasOrganisation
                ? "Already owned by your organisation"
                : "Already owned";
            return false;
        }

        if (IsPropertyReservedByOtherScope(property.PropertyCode))
        {
            invalidReason = string.Equals(property.PropertyCode, "motelroom", StringComparison.OrdinalIgnoreCase)
                ? "Donna has already sold the motel room to another player."
                : (_state.Snapshot.HasOrganisation ? "Owned by another crew" : "Already owned by another player");
            return false;
        }

        if (property.IsOwned)
        {
            invalidReason = "Already owned";
            return false;
        }

        MoneyManager? moneyManager = NetworkSingleton<MoneyManager>.Instance;
        if (moneyManager != null && moneyManager.cashBalance + 0.009f < price)
        {
            invalidReason = "Insufficient cash";
            return false;
        }

        return true;
    }

    internal bool TryValidateBusinessOperation(Business business, out string invalidReason)
    {
        invalidReason = string.Empty;
        if (business == null || string.IsNullOrWhiteSpace(business.PropertyCode))
        {
            return true;
        }

        if (!_hasSnapshot)
        {
            invalidReason = "Loading account";
            return false;
        }

        if (!DoesScopeOwnProperty(business.PropertyCode) || !CanAccessProperty(business.PropertyCode))
        {
            invalidReason = IsPropertyReservedByOtherScope(business.PropertyCode)
                ? (_state.Snapshot.HasOrganisation ? "Owned by another crew" : "Already owned by another player")
                : "Business is not owned by your scope";
            return false;
        }

        if (!business.IsOwned)
        {
            invalidReason = "Business is not owned";
            return false;
        }

        return true;
    }

    internal bool TryValidateLaunderingStart(Business business, float amount, out string invalidReason)
    {
        if (!TryValidateBusinessOperation(business, out invalidReason))
        {
            return false;
        }

        MoneyManager? moneyManager = NetworkSingleton<MoneyManager>.Instance;
        if (moneyManager != null && moneyManager.cashBalance + 0.009f < amount)
        {
            invalidReason = "Insufficient cash";
            return false;
        }

        return true;
    }

    internal bool TryValidateShopCheckout(Cart cart, out string invalidReason)
    {
        invalidReason = string.Empty;
        if (cart == null || cart.Shop == null)
        {
            return true;
        }

        float total = GetCartPriceSum(cart);
        if (total <= 0f)
        {
            return true;
        }

        MoneyManager? moneyManager = NetworkSingleton<MoneyManager>.Instance;
        float cashBalance = moneyManager?.cashBalance ?? 0f;
        switch (cart.Shop.PaymentType)
        {
            case ShopInterface.EPaymentType.Cash:
                return true;
            case ShopInterface.EPaymentType.Online:
                return TryValidateScopedOnlinePayment(total, out invalidReason);
            case ShopInterface.EPaymentType.PreferCash:
                return cashBalance + 0.009f >= total || TryValidateScopedOnlinePayment(total, out invalidReason);
            case ShopInterface.EPaymentType.PreferOnline:
                if (CanUseScopedFinance && _state.Snapshot.OnlineBalance + 0.009f >= total)
                {
                    return true;
                }

                if (cashBalance + 0.009f >= total)
                {
                    return true;
                }

                return TryValidateScopedOnlinePayment(total, out invalidReason);
            default:
                return true;
        }
    }

    internal bool TryHandleShopBuy(Cart cart)
    {
        if (cart == null || cart.Shop == null)
        {
            return true;
        }

        float total = GetCartPriceSum(cart);
        if (!ShouldUseServerShopCheckout(cart, total, out string invalidReason))
        {
            if (!TryValidateShopCheckout(cart, out invalidReason))
            {
                ShowOrganisationNotification(invalidReason);
                return false;
            }

            return true;
        }

        if (!string.IsNullOrWhiteSpace(invalidReason))
        {
            ShowOrganisationNotification(invalidReason);
            return false;
        }

        if (_pendingShopCheckoutRequestId != null)
        {
            ShowOrganisationNotification("Checkout is already processing");
            return false;
        }

        if (!cart.Shop.WillCartFit())
        {
            ShowOrganisationNotification("Order will not fit");
            return false;
        }

        MelonCoroutines.Start(ProcessShopCheckout(cart, total));
        return false;
    }

    internal void ShowOrganisationNotification(string subtitle)
    {
        ShowNotification("Organisation", subtitle);
    }

    internal bool TryGetCustomerScope(string npcGuid, out CustomerScopeEntryDto entry)
    {
        return _customerScopeClientService.TryGet(npcGuid, out entry);
    }

    internal bool TryGetDealerScope(string npcId, out DealerScopeEntryDto entry)
    {
        return _customerScopeClientService.TryGetDealer(npcId, out entry);
    }

    internal bool TryGetSupplierScope(string npcId, out SupplierScopeEntryDto entry)
    {
        return _customerScopeClientService.TryGetSupplier(npcId, out entry);
    }

    internal IEnumerator ProcessAtmTransaction(ATMInterface atmInterface, float amount, bool isDeposit)
    {
        atmInterface.SetActiveScreen(AtmInterfaceAccess.GetProcessingScreen(atmInterface));
        yield return new WaitForSeconds(1f);

        if (!CanUseScopedFinance)
        {
            _logger.Warning("ATM transaction blocked because the player scope is not ready yet.");
            atmInterface.SetActiveScreen(AtmInterfaceAccess.GetMenuScreen(atmInterface));
            yield break;
        }

        string requestId = Guid.NewGuid().ToString("N");
        _pendingAtmRequestId = requestId;
        _pendingAtmResult = null;

        OrganisationAtmTransactionRequestDto request = new OrganisationAtmTransactionRequestDto
        {
            RequestId = requestId,
            Amount = amount,
            IsDeposit = isDeposit,
        };

        _logger.Info($"Sending ATM {(isDeposit ? "deposit" : "withdrawal")} request for {amount:0.##}.");
        CustomMessaging.SendToServer(OrganisationMessages.AtmTransactionRequest, JsonConvert.SerializeObject(request));

        float elapsed = 0f;
        const float timeoutSeconds = 5f;
        while ((_pendingAtmResult == null || !string.Equals(_pendingAtmResult.RequestId, requestId, StringComparison.OrdinalIgnoreCase)) && elapsed < timeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        OrganisationAtmTransactionResultDto? result = _pendingAtmResult;
        _pendingAtmRequestId = null;
        _pendingAtmResult = null;

        if (result == null || !string.Equals(result.RequestId, requestId, StringComparison.OrdinalIgnoreCase))
        {
            _logger.Warning("ATM transaction timed out waiting for server response.");
            atmInterface.SetActiveScreen(AtmInterfaceAccess.GetMenuScreen(atmInterface));
            yield break;
        }

        AtmInterfaceAccess.PlayCompleteSound(atmInterface);

        if (!result.Success)
        {
            atmInterface.SetActiveScreen(AtmInterfaceAccess.GetMenuScreen(atmInterface));
            yield break;
        }

        MoneyManager? moneyManager = NetworkSingleton<MoneyManager>.Instance;
        if (moneyManager != null)
        {
            moneyManager.ChangeCashBalance(result.IsDeposit ? -result.Amount : result.Amount);
        }

        AtmInterfaceAccess.SetSuccessSubtitle(atmInterface, result.Message);
        atmInterface.SetActiveScreen(AtmInterfaceAccess.GetSuccessScreen(atmInterface));
    }

    private IEnumerator ProcessShopCheckout(Cart cart, float total)
    {
        if (cart == null || cart.Shop == null)
        {
            yield break;
        }

        string requestId = Guid.NewGuid().ToString("N");
        _pendingShopCheckoutRequestId = requestId;
        _pendingShopCheckoutResult = null;

        OrganisationShopCheckoutRequestDto request = new OrganisationShopCheckoutRequestDto
        {
            RequestId = requestId,
            ShopCode = cart.Shop.ShopCode,
            ShopName = cart.Shop.ShopName,
            Total = total,
        };

        foreach (var item in cart.cartDictionary)
        {
            string? itemId = item.Key == null ? null : GetListingItemId(item.Key);
            if (string.IsNullOrWhiteSpace(itemId) || item.Value <= 0)
            {
                continue;
            }

            request.Lines.Add(new OrganisationShopCheckoutLineDto
            {
                ItemId = itemId,
                Quantity = item.Value,
            });
        }

        if (request.Lines.Count == 0)
        {
            _pendingShopCheckoutRequestId = null;
            yield break;
        }

        CustomMessaging.SendToServer(OrganisationMessages.ShopCheckoutRequest, JsonConvert.SerializeObject(request));

        float elapsed = 0f;
        const float timeoutSeconds = 5f;
        while ((_pendingShopCheckoutResult == null || !string.Equals(_pendingShopCheckoutResult.RequestId, requestId, StringComparison.OrdinalIgnoreCase)) && elapsed < timeoutSeconds)
        {
            elapsed += Time.unscaledDeltaTime;
            yield return null;
        }

        OrganisationShopCheckoutResultDto? result = _pendingShopCheckoutResult;
        _pendingShopCheckoutRequestId = null;
        _pendingShopCheckoutResult = null;

        if (result == null || !string.Equals(result.RequestId, requestId, StringComparison.OrdinalIgnoreCase))
        {
            ShowOrganisationNotification("Checkout timed out");
            yield break;
        }

        if (!result.Success)
        {
            ShowOrganisationNotification(string.IsNullOrWhiteSpace(result.Message) ? "Checkout failed" : result.Message);
            yield break;
        }

        cart.Shop.HandoverItems();
        cart.ClearCart();
        cart.Shop.CheckoutSound.Play();
        cart.Shop.SetIsOpen(isOpen: false);
        cart.Shop.onOrderCompleted?.Invoke();
    }

    private void ApplyFinanceSnapshot(OrganisationSnapshotDto snapshot, bool hadSnapshot, float previousBalance)
    {
        float onlineBalance = snapshot.OnlineBalance;
        float weeklyDepositSum = snapshot.WeeklyDepositSum;

        MoneyManager? moneyManager = NetworkSingleton<MoneyManager>.Instance;
        if (moneyManager != null)
        {
            moneyManager.onlineBalance = onlineBalance;
        }

        ATM.WeeklyDepositSum = weeklyDepositSum;

        if (Singleton<HUD>.InstanceExists)
        {
            Singleton<HUD>.Instance.OnlineBalanceDisplay.SetBalance(onlineBalance);
            if (hadSnapshot && Math.Abs(previousBalance - onlineBalance) > 0.009f)
            {
                // Removed in game update
                // Singleton<HUD>.Instance.OnlineBalanceDisplay.Show();
            }
        }
    }

    private bool TryValidateScopedOnlinePayment(float amount, out string invalidReason)
    {
        invalidReason = string.Empty;
        if (!CanUseScopedFinance)
        {
            invalidReason = "Loading account";
            return false;
        }

        if (_state.Snapshot.OnlineBalance + 0.009f < amount)
        {
            invalidReason = "Insufficient organisation funds";
            return false;
        }

        return true;
    }

    private static float GetCartPriceSum(Cart cart)
    {
        float total = 0f;
        foreach (var item in cart.cartDictionary)
        {
            if (item.Key != null && item.Value > 0)
            {
                total += item.Value * item.Key.Price;
            }
        }

        return total;
    }

    private static string? GetListingItemId(ShopListing listing)
    {
        object? item = AccessTools.Property(listing.GetType(), "Item")?.GetValue(listing);
        return item == null ? null : AccessTools.Property(item.GetType(), "ID")?.GetValue(item) as string;
    }

    private bool ShouldUseServerShopCheckout(Cart cart, float total, out string invalidReason)
    {
        invalidReason = string.Empty;
        if (cart == null || cart.Shop == null || total <= 0f)
        {
            return false;
        }

        MoneyManager? moneyManager = NetworkSingleton<MoneyManager>.Instance;
        float cashBalance = moneyManager?.cashBalance ?? 0f;
        switch (cart.Shop.PaymentType)
        {
            case ShopInterface.EPaymentType.Online:
                _ = TryValidateScopedOnlinePayment(total, out invalidReason);
                return true;
            case ShopInterface.EPaymentType.PreferCash:
                if (cashBalance + 0.009f >= total)
                {
                    return false;
                }

                _ = TryValidateScopedOnlinePayment(total, out invalidReason);
                return true;
            case ShopInterface.EPaymentType.PreferOnline:
                if (CanUseScopedFinance && _state.Snapshot.OnlineBalance + 0.009f >= total)
                {
                    return true;
                }

                if (cashBalance + 0.009f >= total)
                {
                    return false;
                }

                _ = TryValidateScopedOnlinePayment(total, out invalidReason);
                return true;
            default:
                return false;
        }
    }

    private static T? Deserialize<T>(string payload) where T : class
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            return null;
        }

        try
        {
            return JsonConvert.DeserializeObject<T>(payload);
        }
        catch
        {
            return null;
        }
    }

    private void ResetRuntimeState()
    {
        _snapshotRequested = false;
        _snapshotRequestQueued = false;
        _snapshotRequestDueAtUtc = DateTime.MinValue;
        _hasSnapshot = false;
        _pendingPropertyScopeApply = false;
        _pendingUnavailableEstatePropertyCode = null;
        _pendingFirstJoinOnboarding = false;
        _hasAcknowledgedOnboardingThisSession = false;
        _pendingAtmRequestId = null;
        _pendingAtmResult = null;
        _pendingShopCheckoutRequestId = null;
        _pendingShopCheckoutResult = null;
        OrganisationQuestClientPatches.ClearLoanSharkKidnapQueue();
        _questScopeClientService.ClearPending();
        _customerScopeClientService.Clear();
        _teamAppearanceService.Clear();
        _workflowSmokeRunner?.ResetRuntimeState();
        _state.Replace(new OrganisationSnapshotDto());
        ApplyFinanceSnapshot(new OrganisationSnapshotDto(), false, 0f);
        NotifyPhoneAppStateChanged();
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
        if (type != LogType.Exception || !LooksLikeRelevantException(condition, stackTrace))
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
        if (!LooksLikeRelevantException(relevantException.Message, stackTrace))
        {
            return;
        }

        LogDetailedException("First-chance exception", relevantException.Message, stackTrace, relevantException);
    }

    private bool LooksLikeRelevantException(string? message, string? stackTrace)
    {
        string haystack = string.Concat(message ?? string.Empty, "\n", stackTrace ?? string.Empty);
        if (!haystack.Contains("NullReferenceException", StringComparison.Ordinal)
            && !haystack.Contains("Coroutine continue failure", StringComparison.Ordinal))
        {
            return false;
        }

        return haystack.Contains("DedicatedServerMod.Organisations", StringComparison.Ordinal)
            || haystack.Contains("ScheduleOne.Quests", StringComparison.Ordinal)
            || haystack.Contains("ScheduleOne.Economy.Customer", StringComparison.Ordinal)
            || haystack.Contains("ScheduleOne.Property", StringComparison.Ordinal);
    }

    private void LogDetailedException(string source, string message, string stackTrace, Exception? exception)
    {
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

    private void UpdateOnboardingState(OrganisationSnapshotDto snapshot)
    {
        if (snapshot.HasOrganisation || !snapshot.ShouldShowOnboarding)
        {
            _pendingFirstJoinOnboarding = false;
            return;
        }

        if (_hasAcknowledgedOnboardingThisSession)
        {
            return;
        }

        _pendingFirstJoinOnboarding = true;
    }

    private void TryAutoOpenOnboardingHub()
    {
        if (!_pendingFirstJoinOnboarding || _hasAcknowledgedOnboardingThisSession)
        {
            return;
        }

        if (!_hasSnapshot || _state.Snapshot.HasOrganisation || !_state.Snapshot.ShouldShowOnboarding)
        {
            _pendingFirstJoinOnboarding = false;
            return;
        }

        if (Player.Local == null || !Player.Local.HasCompletedIntro || OrganisationsPhoneApp.Instance == null || !Singleton<GameplayMenu>.InstanceExists)
        {
            return;
        }

        OpenOrganisationHub(preferOnboarding: true);
        NotifyOnboardingHubShown();
    }

    private void NotifyPhoneAppStateChanged()
    {
        if (OrganisationsPhoneApp.Instance != null)
        {
            OrganisationsPhoneApp.Instance.RefreshUI();
        }
    }

    private bool TryValidateScopedPropertyChoice(Property property, out string invalidReason)
    {
        invalidReason = string.Empty;
        if (property == null || string.IsNullOrWhiteSpace(property.PropertyCode))
        {
            return true;
        }

        if (!_hasSnapshot)
        {
            invalidReason = "Loading account";
            return false;
        }

        if (DoesScopeOwnProperty(property.PropertyCode))
        {
            invalidReason = _state.Snapshot.HasOrganisation
                ? "Already owned by your organisation"
                : "Already owned";
            return false;
        }

        if (IsPropertyReservedByOtherScope(property.PropertyCode))
        {
            if (string.Equals(property.PropertyCode, "motelroom", StringComparison.OrdinalIgnoreCase))
            {
                invalidReason = "Donna has already sold the motel room to another player.";
                return false;
            }

            invalidReason = _state.Snapshot.HasOrganisation
                ? "Owned by another crew"
                : "Already owned by another player";
            return false;
        }

        if (property.IsOwned)
        {
            invalidReason = "Already owned";
            return false;
        }

        if (_state.Snapshot.OnlineBalance + 0.009f < property.Price)
        {
            invalidReason = "Insufficient balance";
            return false;
        }

        return true;
    }

    private static Property? FindProperty(string propertyCode)
    {
        if (string.IsNullOrWhiteSpace(propertyCode))
        {
            return null;
        }

        return Property.Properties.AsManagedEnumerable().FirstOrDefault(property =>
            property != null
            && string.Equals(property.PropertyCode, propertyCode, StringComparison.OrdinalIgnoreCase));
    }

    private void ShowNotification(string title, string subtitle)
    {
        if (!Singleton<NotificationsManager>.InstanceExists)
        {
            return;
        }

        Sprite? icon = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        Singleton<NotificationsManager>.Instance.SendNotification(title, subtitle, icon, 4f, true);
    }

    private void EnsureRuntimeDependencies()
    {
        if (_logger != null)
        {
            return;
        }

        _logger = new OrganisationLogger(LoggerInstance);
        _questScopeClientService = new OrganisationQuestScopeClientService(_logger);
        _customerScopeClientService = new OrganisationCustomerScopeClientService();
        _teamAppearanceService = new OrganisationTeamAppearanceService(_logger);
        _workflowSmokeRunner = new OrganisationWorkflowSmokeRunner(
            OrganisationWorkflowSmokeOptions.Parse(Environment.GetCommandLineArgs()),
            _logger,
            CreateOrganisation,
            InvitePlayer,
            AcceptInvite);
        ActiveInstance = this;
    }
}
#endif
