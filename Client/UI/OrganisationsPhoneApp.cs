#if CLIENT
using System;
using DedicatedServerMod.Organisations.Client;
using DedicatedServerMod.Organisations.Contracts;
using S1API.Internal.Abstraction;
using S1API.Input;
using S1API.PhoneApp;
using S1API.UI;
using S1API.Utils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace DedicatedServerMod.Organisations.Client.UI;

/// <summary>
/// In-game phone app for onboarding players into organisations and managing the full organisation lifecycle.
/// </summary>
public sealed class OrganisationsPhoneApp : PhoneApp
{
    private enum Page
    {
        Home,
        Invites,
        Members,
    }

    private static readonly Color BackgroundColor = new Color(0.05f, 0.06f, 0.08f, 1f);
    private static readonly Color PanelColor = new Color(0.11f, 0.12f, 0.15f, 1f);
    private static readonly Color SurfaceColor = new Color(0.15f, 0.17f, 0.2f, 1f);
    private static readonly Color RaisedSurfaceColor = new Color(0.18f, 0.2f, 0.24f, 1f);
    private static readonly Color AccentColor = new Color(0.21f, 0.57f, 0.94f, 1f);
    private static readonly Color SecondaryAccentColor = new Color(0.21f, 0.57f, 0.94f, 1f);
    private static readonly Color WarningColor = new Color(0.68f, 0.3f, 0.2f, 1f);
    private static readonly Color NeutralButtonColor = new Color(0.27f, 0.31f, 0.33f, 1f);
    private static readonly Color InputColor = new Color(0.15f, 0.2f, 0.21f, 1f);
    private static readonly Color DisabledInputColor = new Color(0.11f, 0.13f, 0.14f, 1f);
    private static readonly Color OverlayColor = new Color(0f, 0f, 0f, 0.62f);
    private static readonly Color BodyTextColor = new Color(0.88f, 0.89f, 0.92f, 1f);
    private static readonly Color MutedTextColor = new Color(0.67f, 0.69f, 0.74f, 1f);
    private static readonly Color LineColor = new Color(1f, 1f, 1f, 0.05f);

    private Text? _summaryText;
    private Text? _statusText;
    private Text? _stateMetricText;
    private Text? _rosterMetricText;
    private Text? _balanceMetricText;
    private Text? _teamMetricText;
    private Image? _teamMetricSwatch;
    private Text? _onboardingHintText;
    private Text? _onboardingAsideText;
    private Text? _managementHintText;
    private Text? _managementAsideText;
    private RectTransform? _invitesContent;
    private RectTransform? _membersContent;
    private GameObject? _tabBar;
    private GameObject? _homePagePanel;
    private GameObject? _invitesPanel;
    private GameObject? _membersPanel;
    private GameObject? _homeCreatePanel;
    private GameObject? _homeManagePanel;
    private InputField? _createInput;
    private Image? _createInputBackground;
    private InputField? _inviteInput;
    private Image? _inviteInputBackground;
    private Button? _createButton;
    private Button? _laterButton;
    private Button? _inviteButton;
    private Button? _leaveButton;
    private Text? _createButtonLabel;
    private Text? _laterButtonLabel;
    private Text? _inviteButtonLabel;
    private Text? _leaveButtonLabel;
    private Button? _homeTabButton;
    private Button? _invitesTabButton;
    private Button? _membersTabButton;
    private Text? _homeTabLabel;
    private Text? _invitesTabLabel;
    private Text? _membersTabLabel;
    private GameObject? _confirmationOverlay;
    private Text? _confirmationTitle;
    private Text? _confirmationMessage;
    private Button? _confirmationConfirmButton;
    private Button? _confirmationCancelButton;
    private Text? _confirmationConfirmLabel;
    private Action? _pendingConfirmationAction;
    private bool _focusCreateInputOnNextRefresh;
    private Page _currentPage = Page.Home;

    public static OrganisationsPhoneApp? Instance { get; private set; }

    protected override string AppName => "OrganisationsApp";

    protected override string AppTitle => "Organisations";

    protected override string IconLabel => "Orgs";

    protected override string IconFileName => string.Empty;

    protected override void OnCreated()
    {
        base.OnCreated();
        Instance = this;
    }

    protected override void OnDestroyed()
    {
        if (ReferenceEquals(Instance, this))
        {
            Instance = null;
        }

        base.OnDestroyed();
    }

    protected override void OnPhoneClosed()
    {
        base.OnPhoneClosed();
        Controls.IsTyping = false;

        if (EventSystem.current != null)
        {
            EventSystem.current.SetSelectedGameObject(null);
        }

        _createInput?.DeactivateInputField();
        _inviteInput?.DeactivateInputField();
        HideConfirmation();
    }

    protected override void OnCreatedUI(GameObject container)
    {
        GameObject background = UIFactory.Panel("MainBackground", container.transform, BackgroundColor, fullAnchor: true);

        GameObject topBar = UIFactory.TopBar("TopBar", background.transform, "Organisations", 0.84f, 70, 70, 0, 35);
        topBar.GetComponent<Image>().color = PanelColor;
        var (_, refreshButton, _) = UIFactory.RoundedButtonWithLabel("RefreshButton", "Refresh", topBar.transform, AccentColor, 120, 40, 16, Color.white);
        ButtonUtils.AddListener(refreshButton, () => OrganisationsClientMod.ActiveInstance?.RefreshSnapshotFromUi());

        GameObject summaryPanel = UIFactory.Panel("SummaryPanel", background.transform, PanelColor, new Vector2(0.03f, 0.05f), new Vector2(0.39f, 0.82f));
        GameObject summaryBody = CreatePanelBody(summaryPanel, 20f, 20f, 18f, 18f);
        UIFactory.VerticalLayoutOnGO(summaryBody, 12, new RectOffset(0, 0, 0, 0));
        ConfigureVerticalLayout(summaryBody, forceExpandHeight: false);

        _summaryText = UIFactory.Text("SummaryText", string.Empty, summaryBody.transform, 20, TextAnchor.UpperLeft, FontStyle.Bold);
        _statusText = UIFactory.Text("StatusText", string.Empty, summaryBody.transform, 14, TextAnchor.UpperLeft);
        if (_statusText != null)
        {
            _statusText.color = BodyTextColor;
        }

        CreateMetricCard(summaryBody.transform, "State", out _stateMetricText);
        CreateMetricCard(summaryBody.transform, "Roster", out _rosterMetricText);
        CreateMetricCard(summaryBody.transform, "Balance", out _balanceMetricText);
        CreateTeamMetricCard(summaryBody.transform, out _teamMetricText, out _teamMetricSwatch);

        _tabBar = CreateTabBar(summaryBody.transform);

        GameObject contentPanel = UIFactory.Panel("ContentPanel", background.transform, PanelColor, new Vector2(0.41f, 0f), new Vector2(0.97f, 0.82f));
        _homePagePanel = UIFactory.Panel("HomePage", contentPanel.transform, Color.clear, fullAnchor: true);
        CreateHomePage(_homePagePanel.transform);
        _invitesPanel = CreateSectionPanel(contentPanel.transform, "Invites", out _invitesContent);
        _membersPanel = CreateSectionPanel(contentPanel.transform, "Members", out _membersContent);

        _confirmationOverlay = CreateConfirmationOverlay(background.transform);
        HideConfirmation();
        RefreshUI();
    }

    internal void PrepareForOnboarding()
    {
        _focusCreateInputOnNextRefresh = true;
    }

    internal void FocusCreateInput()
    {
        _focusCreateInputOnNextRefresh = true;
        TryFocusInput(_createInput);
    }

    public void RefreshUI()
    {
        if (_summaryText == null || _statusText == null || _invitesContent == null || _membersContent == null || _homePagePanel == null)
        {
            return;
        }

        OrganisationsClientMod? client = OrganisationsClientMod.ActiveInstance;
        if (client == null || !client.HasSnapshot)
        {
            _summaryText.text = "Connecting to organisation service...";
            _statusText.text = "Waiting for the latest organisation snapshot from the server.";
            UpdateSummaryMetrics(new OrganisationSnapshotDto());
            SetCreateState(canCreate: false);
            SetInviteState(canInvite: false, canLeave: false);
            SetInputState(_createInput, _createInputBackground, false, "");
            SetInputState(_inviteInput, _inviteInputBackground, false, "");
            SyncVisiblePage(new OrganisationSnapshotDto());
            UpdateOnboardingCopy(new OrganisationSnapshotDto());
            UpdateManagementCopy(new OrganisationSnapshotDto());
            RebuildEmptyState(_invitesContent, "No invite data yet.");
            RebuildEmptyState(_membersContent, "No organisation members to display.");
            return;
        }

        OrganisationSnapshotDto snapshot = client.Snapshot;
        _summaryText.text = BuildSummary(snapshot);
        _statusText.text = BuildStatus(snapshot);
        UpdateSummaryMetrics(snapshot);
        UpdateOnboardingCopy(snapshot);
        UpdateManagementCopy(snapshot);
        RebuildInvites(snapshot);
        RebuildMembers(snapshot);

        bool canCreate = !snapshot.HasOrganisation;
        bool canInvite = snapshot.HasOrganisation && HasInvitePermission(snapshot);
        bool canLeave = snapshot.HasOrganisation;

        SetCreateState(canCreate);
        SetInviteState(canInvite, canLeave);
        SetInputState(_createInput, _createInputBackground, canCreate, "Enter organisation name...");
        SetInputState(_inviteInput, _inviteInputBackground, canInvite, canInvite ? "Enter player name or Steam ID..." : "Owner or officers can invite players.");
        SyncVisiblePage(snapshot);

        if (_focusCreateInputOnNextRefresh && !snapshot.HasOrganisation)
        {
            TryFocusInput(_createInput);
            _focusCreateInputOnNextRefresh = false;
        }
        else if (snapshot.HasOrganisation)
        {
            _focusCreateInputOnNextRefresh = false;
        }
    }

    private GameObject CreateTabBar(Transform parent)
    {
        GameObject panel = UIFactory.Panel("TabBar", parent, SurfaceColor);
        LayoutElement panelLayout = panel.AddComponent<LayoutElement>();
        panelLayout.minHeight = 174f;
        panelLayout.preferredHeight = 174f;
        panelLayout.flexibleWidth = 1f;

        GameObject body = CreatePanelBody(panel, 20f, 26f, 14f, 14f);
        UIFactory.VerticalLayoutOnGO(body, 8, new RectOffset(0, 0, 0, 0));
        ConfigureVerticalLayout(body, forceExpandHeight: false);

        Text pagesLabel = UIFactory.Text("PagesLabel", "Pages", body.transform, 13, TextAnchor.MiddleLeft, FontStyle.Bold);
        pagesLabel.color = MutedTextColor;

        (_, _homeTabButton, _homeTabLabel) = UIFactory.RoundedButtonWithLabel("HomeTab", "Home", body.transform, NeutralButtonColor, 156, 34, 14, Color.white);
        (_, _invitesTabButton, _invitesTabLabel) = UIFactory.RoundedButtonWithLabel("InvitesTab", "Invites", body.transform, NeutralButtonColor, 156, 34, 14, Color.white);
        (_, _membersTabButton, _membersTabLabel) = UIFactory.RoundedButtonWithLabel("MembersTab", "Members", body.transform, NeutralButtonColor, 156, 34, 14, Color.white);

        ButtonUtils.AddListener(_homeTabButton, () => SetPage(Page.Home));
        ButtonUtils.AddListener(_invitesTabButton, () => SetPage(Page.Invites));
        ButtonUtils.AddListener(_membersTabButton, () => SetPage(Page.Members));

        return panel;
    }

    private void CreateHomePage(Transform parent)
    {
        GameObject root = UIFactory.Panel("HomeRoot", parent, Color.clear, fullAnchor: true);
        GameObject body = CreatePanelBody(root, 34f, 40f, 16f, 68f);
        UIFactory.VerticalLayoutOnGO(body, 14, new RectOffset(0, 0, 0, 0));
        ConfigureVerticalLayout(body, forceExpandHeight: false);

        _homeCreatePanel = CreateContentCard("HomeCreatePanel", body.transform, 236f);
        GameObject createBody = CreatePanelBody(_homeCreatePanel, 28f, 22f, 20f, 16f);
        UIFactory.VerticalLayoutOnGO(createBody, 10, new RectOffset(0, 0, 0, 0));
        ConfigureVerticalLayout(createBody, forceExpandHeight: false);

        UIFactory.Text("OnboardingTitle", "Create your organisation", createBody.transform, 20, TextAnchor.MiddleLeft, FontStyle.Bold);
        _onboardingHintText = UIFactory.Text("OnboardingHint", string.Empty, createBody.transform, 14, TextAnchor.UpperLeft);
        if (_onboardingHintText != null)
        {
            _onboardingHintText.color = BodyTextColor;
        }

        (_createInput, _createInputBackground) = CreateSingleLineInput(createBody.transform, 42f, "Enter organisation name...");

        GameObject buttonRow = UIFactory.ButtonRow("OnboardingButtons", createBody.transform, 12f, TextAnchor.MiddleLeft);
        (_, _createButton, _createButtonLabel) = UIFactory.RoundedButtonWithLabel("CreateButton", "Create Organisation", buttonRow.transform, AccentColor, 210, 38, 15, Color.white);
        (_, _laterButton, _laterButtonLabel) = UIFactory.RoundedButtonWithLabel("LaterButton", "Later", buttonRow.transform, NeutralButtonColor, 110, 38, 15, Color.white);

        _onboardingAsideText = UIFactory.Text("OnboardingAsideText", string.Empty, createBody.transform, 13, TextAnchor.UpperLeft, FontStyle.Italic);
        if (_onboardingAsideText != null)
        {
            _onboardingAsideText.color = MutedTextColor;
        }

        if (_createButton != null)
        {
            ButtonUtils.AddListener(_createButton, HandleCreateButtonClicked);
        }

        if (_laterButton != null)
        {
            ButtonUtils.AddListener(_laterButton, CloseApp);
        }

        _homeManagePanel = CreateContentCard("HomeManagePanel", body.transform, 220f);
        GameObject manageBody = CreatePanelBody(_homeManagePanel, 28f, 22f, 18f, 18f);
        UIFactory.VerticalLayoutOnGO(manageBody, 10, new RectOffset(0, 0, 0, 0));
        ConfigureVerticalLayout(manageBody, forceExpandHeight: false);

        UIFactory.Text("ManagementTitle", "Organisation hub", manageBody.transform, 20, TextAnchor.MiddleLeft, FontStyle.Bold);
        _managementHintText = UIFactory.Text("ManagementHint", string.Empty, manageBody.transform, 14, TextAnchor.UpperLeft);
        if (_managementHintText != null)
        {
            _managementHintText.color = BodyTextColor;
        }

        (_inviteInput, _inviteInputBackground) = CreateSingleLineInput(manageBody.transform, 42f, "Enter player name or Steam ID...");

        GameObject buttonRowManage = UIFactory.ButtonRow("ManagementButtons", manageBody.transform, 16f, TextAnchor.MiddleLeft);
        (_, _inviteButton, _inviteButtonLabel) = UIFactory.RoundedButtonWithLabel("InviteButton", "Invite Player", buttonRowManage.transform, AccentColor, 160, 38, 15, Color.white);
        (_, _leaveButton, _leaveButtonLabel) = UIFactory.RoundedButtonWithLabel("LeaveButton", "Leave Organisation", buttonRowManage.transform, WarningColor, 190, 38, 15, Color.white);

        _managementAsideText = UIFactory.Text("ManagementAsideText", string.Empty, manageBody.transform, 13, TextAnchor.UpperLeft, FontStyle.Italic);
        if (_managementAsideText != null)
        {
            _managementAsideText.color = MutedTextColor;
        }

        if (_inviteButton != null)
        {
            ButtonUtils.AddListener(_inviteButton, HandleInviteButtonClicked);
        }

        if (_leaveButton != null)
        {
            ButtonUtils.AddListener(_leaveButton, ConfirmLeaveOrganisation);
        }
    }

    private GameObject CreateConfirmationOverlay(Transform parent)
    {
        GameObject overlay = UIFactory.Panel("ConfirmationOverlay", parent, OverlayColor, fullAnchor: true);
        GameObject card = UIFactory.Panel("ConfirmationCard", overlay.transform, PanelColor, new Vector2(0.13f, 0.22f), new Vector2(0.87f, 0.64f));
        UIFactory.VerticalLayoutOnGO(card, 12, new RectOffset(20, 20, 20, 20));

        _confirmationTitle = UIFactory.Text("ConfirmationTitle", string.Empty, card.transform, 19, TextAnchor.MiddleLeft, FontStyle.Bold);
        _confirmationMessage = UIFactory.Text("ConfirmationMessage", string.Empty, card.transform, 14, TextAnchor.UpperLeft);

        GameObject buttonRow = UIFactory.ButtonRow("ConfirmationButtons", card.transform, 12f, TextAnchor.MiddleRight);
        (_, _confirmationConfirmButton, _confirmationConfirmLabel) = UIFactory.RoundedButtonWithLabel("ConfirmButton", "Confirm", buttonRow.transform, WarningColor, 140, 38, 15, Color.white);
        (_, _confirmationCancelButton, _) = UIFactory.RoundedButtonWithLabel("CancelButton", "Cancel", buttonRow.transform, NeutralButtonColor, 120, 38, 15, Color.white);

        if (_confirmationConfirmButton != null)
        {
            ButtonUtils.AddListener(_confirmationConfirmButton, ConfirmPendingAction);
        }

        if (_confirmationCancelButton != null)
        {
            ButtonUtils.AddListener(_confirmationCancelButton, HideConfirmation);
        }

        return overlay;
    }

    private void HandleCreateButtonClicked()
    {
        OrganisationsClientMod.ActiveInstance?.CreateOrganisation(_createInput?.text ?? string.Empty);
    }

    private void HandleInviteButtonClicked()
    {
        OrganisationsClientMod.ActiveInstance?.InvitePlayer(_inviteInput?.text ?? string.Empty);
    }

    private void ConfirmLeaveOrganisation()
    {
        ShowConfirmation(
            "Leave organisation?",
            "If you are the owner, transfer ownership before leaving. Otherwise you will lose access to the organisation roster and balance immediately.",
            "Leave",
            WarningColor,
            () => OrganisationsClientMod.ActiveInstance?.LeaveOrganisation());
    }

    private void ConfirmKickMember(OrganisationMemberDto member)
    {
        ShowConfirmation(
            "Kick member?",
            $"Remove {member.DisplayName} from the organisation? They will lose access immediately and any pending invite will need to be re-sent later.",
            "Kick",
            WarningColor,
            () => OrganisationsClientMod.ActiveInstance?.KickMember(member.SteamId));
    }

    private void ConfirmTransferOwnership(OrganisationMemberDto member)
    {
        ShowConfirmation(
            "Transfer ownership?",
            $"Transfer ownership to {member.DisplayName}? You will be downgraded to Officer and only the new owner will be able to kick members or transfer ownership again.",
            "Transfer",
            NeutralButtonColor,
            () => OrganisationsClientMod.ActiveInstance?.TransferOwnership(member.SteamId));
    }

    private void ShowConfirmation(string title, string message, string confirmText, Color confirmColor, Action confirmAction)
    {
        if (_confirmationOverlay == null || _confirmationTitle == null || _confirmationMessage == null || _confirmationConfirmButton == null || _confirmationConfirmLabel == null)
        {
            confirmAction();
            return;
        }

        _pendingConfirmationAction = confirmAction;
        _confirmationTitle.text = title;
        _confirmationMessage.text = message;
        _confirmationConfirmLabel.text = confirmText;
        Image? confirmImage = _confirmationConfirmButton.GetComponent<Image>();
        if (confirmImage != null)
        {
            confirmImage.color = confirmColor;
        }

        _confirmationOverlay.SetActive(true);
    }

    private void ConfirmPendingAction()
    {
        Action? pendingAction = _pendingConfirmationAction;
        HideConfirmation();
        pendingAction?.Invoke();
    }

    private void HideConfirmation()
    {
        _pendingConfirmationAction = null;
        if (_confirmationOverlay != null)
        {
            _confirmationOverlay.SetActive(false);
        }
    }

    private void RebuildInvites(OrganisationSnapshotDto snapshot)
    {
        if (_invitesContent == null)
        {
            return;
        }

        UIFactory.ClearChildren(_invitesContent);
        if (snapshot.PendingInvites.Count == 0)
        {
            RebuildEmptyState(_invitesContent, snapshot.HasOrganisation
                ? "No pending invites for you right now."
                : "You do not have any pending invites yet.");
            return;
        }

        for (int i = 0; i < snapshot.PendingInvites.Count; i++)
        {
            OrganisationInviteDto invite = snapshot.PendingInvites[i];
            GameObject row = CreateCard($"Invite_{invite.InviteId}", _invitesContent, 120f);
            AddCardContent(row, invite.OrganisationName, $"Invited by {invite.InviterName}  ·  Expires {FormatExpiry(invite.ExpiresAtUnixTimeSeconds)}");

            GameObject buttonRow = UIFactory.ButtonRow("InviteActions", row.transform, 14f, TextAnchor.MiddleLeft);
            var (_, acceptButton, _) = UIFactory.RoundedButtonWithLabel("AcceptButton", "Accept", buttonRow.transform, AccentColor, 96, 34, 14, Color.white);
            var (_, declineButton, _) = UIFactory.RoundedButtonWithLabel("DeclineButton", "Decline", buttonRow.transform, WarningColor, 96, 34, 14, Color.white);
            ButtonUtils.AddListener(acceptButton, () => OrganisationsClientMod.ActiveInstance?.AcceptInvite(invite.InviteId));
            ButtonUtils.AddListener(declineButton, () => OrganisationsClientMod.ActiveInstance?.DeclineInvite(invite.InviteId));
        }
    }

    private void RebuildMembers(OrganisationSnapshotDto snapshot)
    {
        if (_membersContent == null)
        {
            return;
        }

        UIFactory.ClearChildren(_membersContent);
        if (!snapshot.HasOrganisation || snapshot.Members.Count == 0)
        {
            RebuildEmptyState(_membersContent, "Create or join an organisation to manage its members.");
            return;
        }

        bool canManageMembers = IsOwner(snapshot);
        for (int i = 0; i < snapshot.Members.Count; i++)
        {
            OrganisationMemberDto member = snapshot.Members[i];
            bool canManageThisMember = canManageMembers && !IsSelf(snapshot, member);
            GameObject row = CreateCard($"Member_{member.SteamId}", _membersContent, canManageThisMember ? 126f : 98f);
            string subtitle = $"{member.Role}  ·  {(member.IsOnline ? "Online" : "Offline")}";
            AddCardContent(row, member.DisplayName, subtitle, centered: true);

            if (canManageThisMember)
            {
                GameObject buttonRow = UIFactory.ButtonRow("MemberActions", row.transform, 14f, TextAnchor.MiddleLeft);
                var (_, kickButton, _) = UIFactory.RoundedButtonWithLabel("KickButton", "Kick", buttonRow.transform, WarningColor, 80, 32, 13, Color.white);
                var (_, transferButton, _) = UIFactory.RoundedButtonWithLabel("TransferButton", "Transfer Owner", buttonRow.transform, NeutralButtonColor, 130, 32, 13, Color.white);
                ButtonUtils.AddListener(kickButton, () => ConfirmKickMember(member));
                ButtonUtils.AddListener(transferButton, () => ConfirmTransferOwnership(member));
            }
        }
    }

    private void UpdateSummaryMetrics(OrganisationSnapshotDto snapshot)
    {
        if (_stateMetricText == null || _rosterMetricText == null || _balanceMetricText == null || _teamMetricText == null)
        {
            return;
        }

        if (!snapshot.HasOrganisation)
        {
            _stateMetricText.text = snapshot.ShouldShowOnboarding ? "Ready to create" : "Unaffiliated";
            _rosterMetricText.text = snapshot.PendingInvites.Count == 1 ? "1 pending invite" : $"{snapshot.PendingInvites.Count} pending invites";
            _balanceMetricText.text = $"${snapshot.OnlineBalance:0.##} (Personal)";
            _teamMetricText.text = "None";
            SetTeamSwatchColor(string.Empty);
            return;
        }

        _stateMetricText.text = snapshot.PlayerRole;
        _rosterMetricText.text = snapshot.Members.Count == 1 ? "1 member" : $"{snapshot.Members.Count} members";
        _balanceMetricText.text = snapshot.VictoryOnlineBalanceTarget > 0f
            ? $"${snapshot.OnlineBalance:0.##} / ${snapshot.VictoryOnlineBalanceTarget:0.##}"
            : $"${snapshot.OnlineBalance:0.##}";
        _teamMetricText.text = string.IsNullOrWhiteSpace(snapshot.TeamColorHex) ? "Unassigned" : snapshot.TeamColorHex.ToUpperInvariant();
        SetTeamSwatchColor(snapshot.TeamColorHex);
    }

    private void SetCreateState(bool canCreate)
    {
        SetButtonState(_createButton, _createButtonLabel, canCreate, "Create Organisation");
        SetButtonState(_laterButton, _laterButtonLabel, canCreate, "Later");
    }

    private void SetInviteState(bool canInvite, bool canLeave)
    {
        SetButtonState(_inviteButton, _inviteButtonLabel, canInvite, "Invite Player");
        SetButtonState(_leaveButton, _leaveButtonLabel, canLeave, "Leave Organisation");
    }

    private void SyncVisiblePage(OrganisationSnapshotDto snapshot)
    {
        bool hasOrganisation = snapshot.HasOrganisation;
        if (_currentPage == Page.Members && !hasOrganisation)
        {
            _currentPage = Page.Home;
        }

        if (_homePagePanel != null)
        {
            _homePagePanel.SetActive(_currentPage == Page.Home);
        }

        if (_invitesPanel != null)
        {
            _invitesPanel.SetActive(_currentPage == Page.Invites);
        }

        if (_membersPanel != null)
        {
            _membersPanel.SetActive(_currentPage == Page.Members && hasOrganisation);
        }

        if (_homeCreatePanel != null)
        {
            _homeCreatePanel.SetActive(!hasOrganisation);
        }

        if (_homeManagePanel != null)
        {
            _homeManagePanel.SetActive(hasOrganisation);
        }

        UpdateTabVisuals(hasOrganisation);
    }

    private void SetPage(Page page)
    {
        _currentPage = page;
        OrganisationSnapshotDto snapshot = OrganisationsClientMod.ActiveInstance?.Snapshot ?? new OrganisationSnapshotDto();
        SyncVisiblePage(snapshot);
    }

    private void UpdateTabVisuals(bool hasOrganisation)
    {
        UpdateTabButton(_homeTabButton, _homeTabLabel, _currentPage == Page.Home, "Home", true);
        UpdateTabButton(_invitesTabButton, _invitesTabLabel, _currentPage == Page.Invites, "Invites", true);
        UpdateTabButton(_membersTabButton, _membersTabLabel, _currentPage == Page.Members, "Members", hasOrganisation);
    }

    private static void UpdateTabButton(Button? button, Text? label, bool isActive, string text, bool enabled)
    {
        if (button == null || label == null)
        {
            return;
        }

        button.interactable = enabled;
        label.text = text;
        if (button.image != null)
        {
            button.image.color = !enabled
                ? new Color(0.16f, 0.16f, 0.18f, 1f)
                : isActive ? AccentColor : NeutralButtonColor;
        }

        label.color = enabled ? Color.white : MutedTextColor;
    }

    private void UpdateOnboardingCopy(OrganisationSnapshotDto snapshot)
    {
        if (_onboardingHintText == null)
        {
            return;
        }

        if (snapshot.PendingInvites.Count > 0)
        {
            _onboardingHintText.text = "You can create your own organisation here or review your pending invites below before deciding.";
        }
        else if (snapshot.ShouldShowOnboarding)
        {
            _onboardingHintText.text = "Welcome. Create an organisation now to get your group set up, or close the app and come back later from your phone home screen.";
        }
        else
        {
            _onboardingHintText.text = "Create an organisation here whenever you are ready.";
        }

        if (_onboardingAsideText == null)
        {
            return;
        }

        _onboardingAsideText.text = snapshot.PendingInvites.Count > 0
            ? $"Pending invites: {snapshot.PendingInvites.Count}\n\nReview the invite list below before creating your own organisation if you want to join an existing crew instead."
            : "Creating an organisation makes you the owner. You will be able to invite members, manage the roster, and transfer ownership later from this same app.";
    }

    private void UpdateManagementCopy(OrganisationSnapshotDto snapshot)
    {
        if (_managementHintText == null)
        {
            return;
        }

        if (IsOwner(snapshot))
        {
            _managementHintText.text = "Invite players, manage the roster, and transfer ownership from this hub.";
        }
        else if (HasInvitePermission(snapshot))
        {
            _managementHintText.text = "You can invite players and review the roster here. Owner-only actions stay on the member cards.";
        }
        else
        {
            _managementHintText.text = "Review your organisation here. Only the owner or officers can send new invites.";
        }

        if (_managementAsideText == null)
        {
            return;
        }

        if (IsOwner(snapshot))
        {
            _managementAsideText.text = "Owners can invite players, kick members, and transfer ownership. Use the member cards below for the owner-only actions.";
            return;
        }

        if (HasInvitePermission(snapshot))
        {
            _managementAsideText.text = "Officers can send invites and review the full roster, but owner-only actions remain protected.";
            return;
        }

        _managementAsideText.text = "Members can review invites, the roster, and organisation status here. Ask the owner or an officer when new invites need to be sent.";
    }

    private static void SetButtonState(Button? button, Text? label, bool enabled, string text)
    {
        if (button == null || label == null)
        {
            return;
        }

        if (enabled)
        {
            ButtonUtils.Enable(button, label, text);
        }
        else
        {
            ButtonUtils.Disable(button, label, text);
        }
    }

    private static void SetInputState(InputField? input, Image? background, bool enabled, string placeholderText)
    {
        if (input == null)
        {
            return;
        }

        input.interactable = enabled;
        input.readOnly = !enabled;
        if (background != null)
        {
            background.color = enabled ? InputColor : DisabledInputColor;
        }

        Text? placeholder = input.placeholder as Text;
        if (placeholder != null)
        {
            placeholder.text = placeholderText;
        }
    }

    private static GameObject CreateSectionPanel(Transform parent, string title, out RectTransform content)
    {
        GameObject panel = UIFactory.Panel($"{title}Panel", parent, Color.clear, fullAnchor: true);
        GameObject body = CreatePanelBody(panel, 38f, 40f, 16f, 68f);
        UIFactory.VerticalLayoutOnGO(body, 12, new RectOffset(0, 0, 0, 0));
        ConfigureVerticalLayout(body, forceExpandHeight: false);

        Text titleText = UIFactory.Text("SectionTitle", title, body.transform, 20, TextAnchor.MiddleLeft, FontStyle.Bold);
        titleText.color = Color.white;

        GameObject divider = UIFactory.Panel("SectionDivider", body.transform, LineColor);
        LayoutElement dividerLayout = divider.AddComponent<LayoutElement>();
        dividerLayout.minHeight = 1f;
        dividerLayout.preferredHeight = 1f;

        GameObject scrollHost = UIFactory.Panel("ScrollHost", body.transform, Color.clear);
        LayoutElement scrollLayout = scrollHost.AddComponent<LayoutElement>();
        scrollLayout.flexibleWidth = 1f;
        scrollLayout.flexibleHeight = 1f;
        scrollLayout.minHeight = 0f;

        content = UIFactory.ScrollableVerticalList("ContentScroll", scrollHost.transform, out _);
        ConfigureScrollContent(content, 12, new RectOffset(16, 12, 0, 0));
        UIFactory.FitContentHeight(content);
        return panel;
    }

    private static GameObject CreateCard(string name, Transform parent, float minHeight)
    {
        GameObject card = UIFactory.Panel(name, parent, SurfaceColor);
        LayoutElement layoutElement = card.AddComponent<LayoutElement>();
        layoutElement.minHeight = minHeight;
        layoutElement.flexibleWidth = 1f;
        UIFactory.VerticalLayoutOnGO(card, 6, new RectOffset(16, 16, 14, 14));
        return card;
    }

    private static void AddCardContent(GameObject parent, string title, string subtitle, bool centered = false)
    {
        TextAnchor titleAnchor = centered ? TextAnchor.MiddleCenter : TextAnchor.MiddleLeft;
        TextAnchor subtitleAnchor = centered ? TextAnchor.UpperCenter : TextAnchor.UpperLeft;

        Text titleText = UIFactory.Text("Title", title, parent.transform, 16, titleAnchor, FontStyle.Bold);
        titleText.color = Color.white;
        Text subtitleText = UIFactory.Text("Subtitle", subtitle, parent.transform, 13, subtitleAnchor);
        subtitleText.color = new Color(0.79f, 0.83f, 0.84f, 1f);
    }

    private static void RebuildEmptyState(RectTransform content, string message)
    {
        UIFactory.ClearChildren(content);
        GameObject card = CreateCard("EmptyState", content, 110f);
        Text title = UIFactory.Text("EmptyTitle", "Nothing here yet", card.transform, 16, TextAnchor.MiddleCenter, FontStyle.Bold);
        title.color = Color.white;
        Text text = UIFactory.Text("EmptyText", message, card.transform, 14, TextAnchor.MiddleCenter, FontStyle.Italic);
        text.color = MutedTextColor;
    }

    private static string BuildSummary(OrganisationSnapshotDto snapshot)
    {
        if (!snapshot.HasOrganisation)
        {
            return snapshot.PendingInvites.Count > 0
                ? $"You are not in an organisation yet. Personal balance: ${snapshot.OnlineBalance:0.##}. Pending invites: {snapshot.PendingInvites.Count}."
                : $"You are not in an organisation yet. Personal balance: ${snapshot.OnlineBalance:0.##}.";
        }

        string teamLine = string.IsNullOrWhiteSpace(snapshot.TeamColorHex)
            ? string.Empty
            : $"\nTeam color: {snapshot.TeamColorHex.ToUpperInvariant()}";
        string victoryLine = snapshot.VictoryOnlineBalanceTarget > 0f
            ? $"\nTarget: ${snapshot.OnlineBalance:0.##} / ${snapshot.VictoryOnlineBalanceTarget:0.##}{(snapshot.HasReachedVictoryTarget ? " complete" : string.Empty)}"
            : string.Empty;

        return $"{snapshot.OrganisationName}\nRole: {snapshot.PlayerRole}\nMembers: {snapshot.Members.Count}\nBalance: ${snapshot.OnlineBalance:0.##}{victoryLine}{teamLine}";
    }

    private static string BuildStatus(OrganisationSnapshotDto snapshot)
    {
        if (!snapshot.HasOrganisation)
        {
            return snapshot.PendingInvites.Count > 0
                ? "Use this hub to create your own organisation or accept an invite from the list below."
                : "Use this hub to create and manage your organisation from your phone.";
        }

        if (IsOwner(snapshot))
        {
            return snapshot.HasReachedVictoryTarget
                ? "Your organisation has reached the configured victory target."
                : "You can invite players, manage members,\nand transfer ownership from this app.";
        }

        if (snapshot.HasReachedVictoryTarget)
        {
            return "Your organisation has reached the configured victory target.";
        }

        return HasInvitePermission(snapshot)
            ? "You can invite players and review your organisation roster here."
            : "Review your organisation roster here. Owner-only actions remain restricted.";
    }

    private static string FormatExpiry(long expiresAtUnixTimeSeconds)
    {
        DateTimeOffset expiry = DateTimeOffset.FromUnixTimeSeconds(expiresAtUnixTimeSeconds);
        TimeSpan remaining = expiry - DateTimeOffset.UtcNow;
        if (remaining <= TimeSpan.Zero)
        {
            return "soon";
        }

        if (remaining.TotalHours >= 1)
        {
            return $"in {(int)Math.Ceiling(remaining.TotalHours)}h";
        }

        return $"in {Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes))}m";
    }

    private static bool HasInvitePermission(OrganisationSnapshotDto snapshot)
    {
        return string.Equals(snapshot.PlayerRole, "Owner", StringComparison.OrdinalIgnoreCase)
            || string.Equals(snapshot.PlayerRole, "Officer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsOwner(OrganisationSnapshotDto snapshot)
    {
        return string.Equals(snapshot.PlayerRole, "Owner", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSelf(OrganisationSnapshotDto snapshot, OrganisationMemberDto member)
    {
        return string.Equals(snapshot.PlayerSteamId, member.SteamId, StringComparison.OrdinalIgnoreCase);
    }

    private static (InputField? input, Image? background) CreateSingleLineInput(Transform parent, float height, string placeholderText)
    {
        GameObject inputRoot = new GameObject("Input");
        inputRoot.transform.SetParent(parent, false);

        RectTransform rectTransform = inputRoot.AddComponent<RectTransform>();
        rectTransform.sizeDelta = new Vector2(0f, height);

        LayoutElement layoutElement = inputRoot.AddComponent<LayoutElement>();
        layoutElement.minHeight = height;
        layoutElement.preferredHeight = height;

        Image background = inputRoot.AddComponent<Image>();
        background.color = InputColor;
        background.sprite = Resources.GetBuiltinResource<Sprite>("UI/Skin/UISprite.psd");
        background.type = Image.Type.Sliced;

        InputField inputField = inputRoot.AddComponent<InputField>();
        inputField.lineType = InputField.LineType.SingleLine;

        Text text = UIFactory.Text("Text", string.Empty, inputRoot.transform, 14, TextAnchor.MiddleLeft);
        text.color = BodyTextColor;
        RectTransform textRect = text.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = new Vector2(14f, 6f);
        textRect.offsetMax = new Vector2(-14f, -6f);

        Text placeholder = UIFactory.Text("Placeholder", placeholderText, inputRoot.transform, 14, TextAnchor.MiddleLeft, FontStyle.Italic);
        RectTransform placeholderRect = placeholder.GetComponent<RectTransform>();
        placeholderRect.anchorMin = Vector2.zero;
        placeholderRect.anchorMax = Vector2.one;
        placeholderRect.offsetMin = new Vector2(14f, 6f);
        placeholderRect.offsetMax = new Vector2(-14f, -6f);
        placeholder.color = new Color(MutedTextColor.r, MutedTextColor.g, MutedTextColor.b, 0.78f);

        inputField.textComponent = text;
        inputField.placeholder = placeholder;

        EventTrigger trigger = inputRoot.AddComponent<EventTrigger>();
        EventHelper.AddEventTrigger(trigger, EventTriggerType.Select, () => Controls.IsTyping = true);
        EventHelper.AddEventTrigger(trigger, EventTriggerType.Deselect, () => Controls.IsTyping = false);
        EventHelper.AddListener<string>(_ => Controls.IsTyping = false, inputField.onEndEdit);
        EventHelper.AddListener<string>(_ =>
        {
            if (inputField.isFocused)
            {
                Controls.IsTyping = true;
            }
        }, inputField.onValueChanged);

        return (inputField, background);
    }

    private static void TryFocusInput(InputField? input)
    {
        if (input == null || !input.gameObject.activeInHierarchy)
        {
            return;
        }

        input.Select();
        input.ActivateInputField();
        Controls.IsTyping = true;
    }

    private static GameObject CreatePanelBody(GameObject panel, float left, float right, float top, float bottom)
    {
        GameObject body = new GameObject("Body");
        body.transform.SetParent(panel.transform, false);
        RectTransform rectTransform = body.AddComponent<RectTransform>();
        rectTransform.anchorMin = Vector2.zero;
        rectTransform.anchorMax = Vector2.one;
        rectTransform.offsetMin = new Vector2(left, bottom);
        rectTransform.offsetMax = new Vector2(-right, -top);
        return body;
    }

    private static GameObject CreateStretchRow(string name, Transform parent, float spacing, int padLeft = 0, int padRight = 0, int padTop = 0, int padBottom = 0)
    {
        GameObject row = new GameObject(name);
        row.transform.SetParent(parent, false);
        row.AddComponent<RectTransform>();
        HorizontalLayoutGroup layout = row.AddComponent<HorizontalLayoutGroup>();
        layout.spacing = spacing;
        layout.padding = new RectOffset(padLeft, padRight, padTop, padBottom);
        layout.childAlignment = TextAnchor.UpperLeft;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        row.AddComponent<LayoutElement>().flexibleWidth = 1f;
        return row;
    }

    private static void CreateMetricCard(Transform parent, string label, out Text valueText)
    {
        GameObject card = UIFactory.Panel(label + "Metric", parent, SurfaceColor);
        LayoutElement layoutElement = card.AddComponent<LayoutElement>();
        layoutElement.minHeight = 76f;
        layoutElement.flexibleWidth = 1f;
        UIFactory.VerticalLayoutOnGO(card, 6, new RectOffset(16, 16, 14, 14));

        Text labelText = UIFactory.Text("Label", label, card.transform, 12, TextAnchor.MiddleLeft, FontStyle.Bold);
        labelText.color = MutedTextColor;
        valueText = UIFactory.Text("Value", string.Empty, card.transform, 15, TextAnchor.UpperLeft, FontStyle.Bold);
        valueText.color = Color.white;
    }

    private static void CreateTeamMetricCard(Transform parent, out Text valueText, out Image swatch)
    {
        GameObject card = UIFactory.Panel("TeamMetric", parent, SurfaceColor);
        LayoutElement layoutElement = card.AddComponent<LayoutElement>();
        layoutElement.minHeight = 76f;
        layoutElement.flexibleWidth = 1f;
        UIFactory.VerticalLayoutOnGO(card, 6, new RectOffset(16, 16, 14, 14));

        Text labelText = UIFactory.Text("Label", "Team", card.transform, 12, TextAnchor.MiddleLeft, FontStyle.Bold);
        labelText.color = MutedTextColor;

        GameObject row = CreateStretchRow("TeamMetricRow", card.transform, 8f);
        GameObject swatchObject = UIFactory.Panel("TeamColor", row.transform, NeutralButtonColor);
        LayoutElement swatchLayout = swatchObject.AddComponent<LayoutElement>();
        swatchLayout.minWidth = 22f;
        swatchLayout.preferredWidth = 22f;
        swatchLayout.minHeight = 22f;
        swatchLayout.preferredHeight = 22f;
        swatch = swatchObject.GetComponent<Image>();

        valueText = UIFactory.Text("Value", string.Empty, row.transform, 15, TextAnchor.MiddleLeft, FontStyle.Bold);
        valueText.color = Color.white;
    }

    private void SetTeamSwatchColor(string hexColor)
    {
        if (_teamMetricSwatch == null)
        {
            return;
        }

        _teamMetricSwatch.color = ColorUtility.TryParseHtmlString(hexColor, out Color parsed)
            ? parsed
            : NeutralButtonColor;
    }

    private static GameObject CreateContentCard(string name, Transform parent, float minHeight)
    {
        GameObject card = UIFactory.Panel(name, parent, SurfaceColor);
        LayoutElement layoutElement = card.AddComponent<LayoutElement>();
        layoutElement.minHeight = minHeight;
        layoutElement.flexibleWidth = 1f;
        return card;
    }

    private static void ConfigureScrollContent(RectTransform content, int spacing, RectOffset padding)
    {
        VerticalLayoutGroup? layout = content.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            return;
        }

        layout.spacing = spacing;
        layout.padding = padding;
        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
    }

    private static void ConfigureVerticalLayout(GameObject gameObject, bool forceExpandHeight)
    {
        VerticalLayoutGroup? layout = gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null)
        {
            return;
        }

        layout.childControlWidth = true;
        layout.childControlHeight = true;
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = forceExpandHeight;
    }

}
#endif
