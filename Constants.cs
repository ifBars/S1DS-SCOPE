namespace DedicatedServerMod.Organisations;

internal static class Constants
{
    public const string ModName = "Organisations";
    public const string ModVersion = "0.1.0";
    public const string ModAuthor = "Bars";
    public const string SaveFileName = "Organisations";
    public const string ConfigFileName = "config.toml";
    public const string LegacyConfigFileName = "OrganisationsConfig.json";
    public const int MaxOrganisationNameLength = 64;
    public const int MaxInvitesPerPlayer = 8;
    public const float WeeklyAtmDepositLimit = 10000f;
    public const float WeeklyDealerRetentionFee = 1000f;

    public static class Messages
    {
        public const string SnapshotRequest = "org_snapshot_request";
        public const string Snapshot = "org_snapshot";
        public const string Error = "org_error";
        public const string Notification = "org_notification";
        public const string QuestTrackingRequest = "org_quest_tracking_request";
        public const string AtmTransactionRequest = "org_atm_transaction_request";
        public const string AtmTransactionResult = "org_atm_transaction_result";
        public const string ShopCheckoutRequest = "org_shop_checkout_request";
        public const string ShopCheckoutResult = "org_shop_checkout_result";
        public const string CreateRequest = "org_create_request";
        public const string InviteRequest = "org_invite_request";
        public const string InviteReceived = "org_invite_received";
        public const string InviteAcceptRequest = "org_invite_accept_request";
        public const string InviteDeclineRequest = "org_invite_decline_request";
        public const string LeaveRequest = "org_leave_request";
        public const string KickRequest = "org_kick_request";
        public const string TransferOwnershipRequest = "org_transfer_owner_request";
        public const string OnboardingPromptSeenRequest = "org_onboarding_prompt_seen_request";
        public const string QuestScopeSync = "org_quest_scope_sync";
        public const string QuestScopeSyncChunk = "org_quest_scope_sync_chunk";
        public const string CustomerScopeSync = "org_customer_scope_sync";
        public const string CustomerScopeSyncChunk = "org_customer_scope_sync_chunk";
        public const string CustomerOfferRejectRequest = "org_customer_offer_reject_request";
    }
}
