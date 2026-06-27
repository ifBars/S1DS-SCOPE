using DedicatedServerMod.Organisations;
using DedicatedServerMod.Organisations.Client.Testing;
using DedicatedServerMod.Organisations.Contracts;
using DedicatedServerMod.Organisations.Domain;
using DedicatedServerMod.Organisations.Persistence;

Run("organisation message constants are unique and mirrored by contract aliases", () =>
{
    string[] messages =
    {
        OrganisationMessages.SnapshotRequest,
        OrganisationMessages.Snapshot,
        OrganisationMessages.Error,
        OrganisationMessages.Notification,
        OrganisationMessages.QuestTrackingRequest,
        OrganisationMessages.AtmTransactionRequest,
        OrganisationMessages.AtmTransactionResult,
        OrganisationMessages.ShopCheckoutRequest,
        OrganisationMessages.ShopCheckoutResult,
        OrganisationMessages.CreateRequest,
        OrganisationMessages.InviteRequest,
        OrganisationMessages.InviteReceived,
        OrganisationMessages.InviteAcceptRequest,
        OrganisationMessages.InviteDeclineRequest,
        OrganisationMessages.LeaveRequest,
        OrganisationMessages.KickRequest,
        OrganisationMessages.TransferOwnershipRequest,
        OrganisationMessages.OnboardingPromptSeenRequest,
        OrganisationMessages.QuestScopeSync,
        OrganisationMessages.QuestScopeSyncChunk,
        OrganisationMessages.CustomerScopeSync,
        OrganisationMessages.CustomerScopeSyncChunk,
        OrganisationMessages.CustomerOfferRejectRequest,
    };

    EqualInt(messages.Length, messages.Distinct(StringComparer.Ordinal).Count(), "unique message count");
    Equal(Constants.Messages.CreateRequest, OrganisationMessages.CreateRequest, "create request mirror");
    Equal(Constants.Messages.CustomerScopeSyncChunk, OrganisationMessages.CustomerScopeSyncChunk, "customer scope chunk mirror");
});

Run("parses create and invite workflow flags", () =>
{
    OrganisationWorkflowSmokeOptions options = OrganisationWorkflowSmokeOptions.Parse(new[]
    {
        "Schedule I.exe",
        "--org-smoke-create-name",
        "Vespucci Imports",
        "--org-smoke-invite-target",
        "76561198000000019",
    });

    Assert(options.Enabled, "options should be enabled");
    Assert(options.ShouldCreateOrganisation, "create should be enabled");
    Assert(options.ShouldInvitePlayer, "invite should be enabled");
    Equal("Vespucci Imports", options.OrganisationName, "organisation name");
    Equal("76561198000000019", options.InviteTarget, "invite target");
});

Run("parses auto accept invite workflow flag", () =>
{
    OrganisationWorkflowSmokeOptions options = OrganisationWorkflowSmokeOptions.Parse(new[]
    {
        "Schedule I.exe",
        "--org-smoke-auto-accept-invites",
    });

    Assert(options.Enabled, "options should be enabled");
    Assert(options.AutoAcceptInvites, "auto accept should be enabled");
    Assert(!options.ShouldCreateOrganisation, "create should not be enabled");
    Assert(!options.ShouldInvitePlayer, "invite should not be enabled");
});

Run("parses invite delay workflow flag", () =>
{
    OrganisationWorkflowSmokeOptions options = OrganisationWorkflowSmokeOptions.Parse(new[]
    {
        "Schedule I.exe",
        "--org-smoke-invite-delay-seconds",
        "75",
    });

    EqualInt(75, options.InviteDelaySeconds, "invite delay seconds");
});

Run("ignores invalid invite delay workflow values", () =>
{
    OrganisationWorkflowSmokeOptions options = OrganisationWorkflowSmokeOptions.Parse(new[]
    {
        "Schedule I.exe",
        "--org-smoke-invite-delay-seconds",
        "-5",
        "--org-smoke-create-name",
        "RPSmokeCrew",
    });

    Assert(options.Enabled, "create option should still enable workflow");
    EqualInt(0, options.InviteDelaySeconds, "invalid invite delay seconds");
});

Run("ignores incomplete workflow flags", () =>
{
    OrganisationWorkflowSmokeOptions options = OrganisationWorkflowSmokeOptions.Parse(new[]
    {
        "Schedule I.exe",
        "--org-smoke-create-name",
        "   ",
        "--org-smoke-invite-target",
    });

    Assert(!options.Enabled, "options should not be enabled");
    Assert(!options.ShouldCreateOrganisation, "create should not be enabled");
    Assert(!options.ShouldInvitePlayer, "invite should not be enabled");
});

Run("organisation membership checks are case insensitive and reject blank ids", () =>
{
    OrganisationRecord organisation = new OrganisationRecord
    {
        OrgId = "crew",
        OwnerSteamId = "76561198000000009",
    };
    organisation.MemberRoles["76561198000000009"] = OrganisationRole.Owner;

    Assert(organisation.HasMember("76561198000000009"), "exact member id should be present");
    Assert(organisation.HasMember("76561198000000009".ToUpperInvariant()), "member lookup should use case-insensitive comparer");
    Assert(!organisation.HasMember(" "), "blank member id should be rejected");
});

Run("organisation invite expires at boundary time", () =>
{
    DateTime now = new DateTime(2026, 6, 21, 12, 0, 0, DateTimeKind.Utc);
    OrganisationInvite invite = new OrganisationInvite
    {
        InviteId = "invite-a",
        ExpiresAtUtc = now.AddMinutes(5),
    };

    Assert(!invite.IsExpired(now.AddMinutes(4).AddSeconds(59)), "invite should be valid before expiry");
    Assert(invite.IsExpired(now.AddMinutes(5)), "invite should expire exactly at expiry time");
    Assert(invite.IsExpired(now.AddMinutes(6)), "invite should remain expired after expiry time");
});

Run("customer contract can be completed solo when reservation owner matches", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string ownerKey = "player:solo-a";
    const string customerGuid = "customer-guid-a";
    saveData.ActiveCustomerContracts[customerGuid] = new ActiveCustomerContractReservationRecord
    {
        CustomerNpcGuid = customerGuid,
        OwnerKey = ownerKey,
        ContractGuid = "contract-a",
    };

    bool allowed = OrganisationScopeRules.CanReserveCustomerContract(
        saveData,
        ownerKey,
        customerGuid,
        "conflict",
        out string denialMessage);

    Assert(allowed, "matching solo owner should keep the active deal slot");
    Equal(string.Empty, denialMessage, "denial message");
});

Run("customer contract is shared by two players in the same organisation owner key", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string orgOwnerKey = "org:imports";
    const string customerGuid = "customer-guid-b";
    saveData.ActiveCustomerContracts[customerGuid] = new ActiveCustomerContractReservationRecord
    {
        CustomerNpcGuid = customerGuid,
        OwnerKey = orgOwnerKey,
        ContractGuid = "contract-b",
    };

    bool allowed = OrganisationScopeRules.CanReserveCustomerContract(
        saveData,
        orgOwnerKey,
        customerGuid,
        "conflict",
        out string denialMessage);

    Assert(allowed, "same org owner key should allow the second member to continue the active deal");
    Equal(string.Empty, denialMessage, "denial message");
});

Run("customer contract rejects a separate solo or organisation owner", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string customerGuid = "customer-guid-c";
    saveData.ActiveCustomerContracts[customerGuid] = new ActiveCustomerContractReservationRecord
    {
        CustomerNpcGuid = customerGuid,
        OwnerKey = "org:first",
        ContractGuid = "contract-c",
    };

    bool allowed = OrganisationScopeRules.CanReserveCustomerContract(
        saveData,
        "player:separate",
        customerGuid,
        "Sorry, found another dealer.",
        out string denialMessage);

    Assert(!allowed, "different owner key should not take over the active deal slot");
    Equal("Sorry, found another dealer.", denialMessage, "denial message");
});

Run("org members can share accepted customer deal while outsiders are blocked", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string orgOwnerKey = "org:rp-crew";
    const string customerGuid = "customer-guid-shared";
    saveData.ActiveCustomerContracts[customerGuid] = new ActiveCustomerContractReservationRecord
    {
        CustomerNpcGuid = customerGuid,
        OwnerKey = orgOwnerKey,
        ContractGuid = "contract-shared",
    };

    bool sameOrgAllowed = OrganisationScopeRules.CanReserveCustomerContract(
        saveData,
        orgOwnerKey,
        customerGuid,
        "conflict",
        out string sameOrgDenial);
    bool outsiderAllowed = OrganisationScopeRules.CanReserveCustomerContract(
        saveData,
        "org:rival-crew",
        customerGuid,
        "This customer is already dealing with another crew.",
        out string outsiderDenial);

    Assert(sameOrgAllowed, "same organisation members should share the accepted deal");
    Equal(string.Empty, sameOrgDenial, "same org denial");
    Assert(!outsiderAllowed, "outside owner should not take over the accepted deal");
    Equal("This customer is already dealing with another crew.", outsiderDenial, "outsider denial");
});

Run("dealer recruitment is exclusive across separate owner keys", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string dealerId = "dealer-benji";
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey("org:first", dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = "org:first",
        NpcId = dealerId,
        HasBeenRecommended = true,
        IsRecruited = true,
    };
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey("player:second", dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = "player:second",
        NpcId = dealerId,
        HasBeenRecommended = true,
    };

    bool allowed = OrganisationScopeRules.CanRecruitDealer(
        saveData,
        "player:second",
        dealerId,
        "Benji",
        out ScopedDealerRecord? record,
        out string denialMessage);

    Assert(!allowed, "second owner should not recruit an already recruited dealer");
    Assert(record != null && record.HasBeenRecommended, "candidate record should be returned for diagnostics");
    Equal("Benji already works for another crew.", denialMessage, "denial message");
});

Run("recommended dealer can be hired by the owner that earned the recommendation", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string ownerKey = "player:solo-a";
    const string dealerId = "dealer-benji";
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey(ownerKey, dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = ownerKey,
        NpcId = dealerId,
        HasBeenRecommended = true,
    };

    bool allowed = OrganisationScopeRules.CanRecruitDealer(
        saveData,
        ownerKey,
        dealerId,
        "Benji",
        out ScopedDealerRecord? record,
        out string denialMessage);

    Assert(allowed, "recommended owner should be allowed to hire the dealer");
    Assert(record != null, "recommended dealer record should be returned");
    Equal(string.Empty, denialMessage, "denial message");
});

Run("dealer hiring stays locked until that owner earns the recommendation", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string dealerId = "dealer-benji";
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey("org:first", dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = "org:first",
        NpcId = dealerId,
        HasBeenRecommended = true,
    };

    bool allowed = OrganisationScopeRules.CanRecruitDealer(
        saveData,
        "org:second",
        dealerId,
        "Benji",
        out ScopedDealerRecord? record,
        out string denialMessage);

    Assert(!allowed, "second owner should not hire from another owner's recommendation");
    Assert(record == null, "missing scoped record should be returned as null");
    Equal("Must be recommended by one of Benji's connections.", denialMessage, "denial message");
});

Run("dealer contract owner resolves only for assigned unlocked customers", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string ownerKey = "org:crew";
    const string dealerId = "dealer-maya";
    const string customerNpcId = "cust-npc-1";
    const string customerGuid = "cust-guid-1";
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey(ownerKey, dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = ownerKey,
        NpcId = dealerId,
        IsRecruited = true,
        AssignedCustomerNpcIds = new List<string> { customerNpcId },
    };
    saveData.ScopedCustomers[OrganisationScopeRules.BuildScopedCustomerKey(ownerKey, customerGuid)] = new ScopedCustomerRecord
    {
        OwnerKey = ownerKey,
        NpcGuid = customerGuid,
        IsUnlocked = true,
    };

    bool allowed = OrganisationScopeRules.TryFindDealerContractOwner(
        saveData,
        dealerId,
        "Maya",
        customerNpcId,
        customerGuid,
        "Albert",
        out string resolvedOwnerKey,
        out string denialMessage);

    Assert(allowed, "assigned unlocked customer should resolve to dealer owner");
    Equal(ownerKey, resolvedOwnerKey, "owner key");
    Equal(string.Empty, denialMessage, "denial message");
});

Run("dealer contract rejects customers assigned to another active owner", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string ownerKey = "org:crew";
    const string dealerId = "dealer-maya";
    const string customerNpcId = "cust-npc-2";
    const string customerGuid = "cust-guid-2";
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey(ownerKey, dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = ownerKey,
        NpcId = dealerId,
        IsRecruited = true,
        AssignedCustomerNpcIds = new List<string> { customerNpcId },
    };
    saveData.ScopedCustomers[OrganisationScopeRules.BuildScopedCustomerKey(ownerKey, customerGuid)] = new ScopedCustomerRecord
    {
        OwnerKey = ownerKey,
        NpcGuid = customerGuid,
        IsUnlocked = true,
    };
    saveData.ActiveCustomerContracts[customerGuid] = new ActiveCustomerContractReservationRecord
    {
        CustomerNpcGuid = customerGuid,
        OwnerKey = "player:rival",
        ContractGuid = "contract-rival",
    };

    bool allowed = OrganisationScopeRules.TryFindDealerContractOwner(
        saveData,
        dealerId,
        "Maya",
        customerNpcId,
        customerGuid,
        "Albert",
        out string resolvedOwnerKey,
        out string denialMessage);

    Assert(!allowed, "dealer should not take a customer reserved by another owner");
    Equal(string.Empty, resolvedOwnerKey, "owner key");
    Equal("Albert already found another dealer.", denialMessage, "denial message");
});

Run("dealer sale accepts assigned customer for hired org and reserves active deal", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string ownerKey = "org:dealers";
    const string dealerId = "dealer-maya";
    const string customerNpcId = "cust-npc-sale";
    const string customerGuid = "cust-guid-sale";
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey(ownerKey, dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = ownerKey,
        NpcId = dealerId,
        IsRecruited = true,
        AssignedCustomerNpcIds = new List<string> { customerNpcId },
    };
    saveData.ScopedCustomers[OrganisationScopeRules.BuildScopedCustomerKey(ownerKey, customerGuid)] = new ScopedCustomerRecord
    {
        OwnerKey = ownerKey,
        NpcGuid = customerGuid,
        IsUnlocked = true,
    };

    bool ownerResolved = OrganisationScopeRules.TryFindDealerContractOwner(
        saveData,
        dealerId,
        "Maya",
        customerNpcId,
        customerGuid,
        "Albert",
        out string resolvedOwnerKey,
        out string denialMessage);

    Assert(ownerResolved, "hired dealer should accept a sale for an assigned unlocked customer");
    Equal(ownerKey, resolvedOwnerKey, "resolved owner key");
    Equal(string.Empty, denialMessage, "denial message");

    saveData.ActiveCustomerContracts[customerGuid] = new ActiveCustomerContractReservationRecord
    {
        CustomerNpcGuid = customerGuid,
        OwnerKey = resolvedOwnerKey,
        ContractGuid = "contract-sale",
    };

    bool rivalAllowed = OrganisationScopeRules.CanReserveCustomerContract(
        saveData,
        "player:rival",
        customerGuid,
        "Albert already found another dealer.",
        out string rivalDenial);

    Assert(!rivalAllowed, "active dealer sale should block rival owners");
    Equal("Albert already found another dealer.", rivalDenial, "rival denial");
});

Run("dealer sale rejects unlocked customer assigned to another owner's recruited dealer", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string firstOwnerKey = "org:first";
    const string secondOwnerKey = "org:second";
    const string dealerId = "dealer-maya";
    const string customerNpcId = "cust-npc-sale";
    const string customerGuid = "cust-guid-sale";
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey(firstOwnerKey, dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = firstOwnerKey,
        NpcId = dealerId,
        IsRecruited = true,
        AssignedCustomerNpcIds = new List<string> { "other-customer" },
    };
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey(secondOwnerKey, dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = secondOwnerKey,
        NpcId = dealerId,
        IsRecruited = true,
        AssignedCustomerNpcIds = new List<string> { customerNpcId },
    };
    saveData.ScopedCustomers[OrganisationScopeRules.BuildScopedCustomerKey(secondOwnerKey, customerGuid)] = new ScopedCustomerRecord
    {
        OwnerKey = secondOwnerKey,
        NpcGuid = customerGuid,
        IsUnlocked = true,
    };

    bool allowed = OrganisationScopeRules.TryFindDealerContractOwner(
        saveData,
        dealerId,
        "Maya",
        customerNpcId,
        customerGuid,
        "Albert",
        out string resolvedOwnerKey,
        out string denialMessage);

    Assert(!allowed, "ambiguous duplicate recruited dealer state should fail closed at the first conflicting recruited record");
    Equal(string.Empty, resolvedOwnerKey, "resolved owner key");
    Equal("Albert is not assigned to this dealer's scope.", denialMessage, "denial message");
});

Run("dealer sale rejects unassigned customer even if the customer is unlocked", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string ownerKey = "org:dealers";
    const string dealerId = "dealer-maya";
    const string customerGuid = "cust-guid-sale";
    saveData.ScopedDealers[OrganisationScopeRules.BuildScopedNpcKey(ownerKey, dealerId)] = new ScopedDealerRecord
    {
        OwnerKey = ownerKey,
        NpcId = dealerId,
        IsRecruited = true,
        AssignedCustomerNpcIds = new List<string> { "different-customer" },
    };
    saveData.ScopedCustomers[OrganisationScopeRules.BuildScopedCustomerKey(ownerKey, customerGuid)] = new ScopedCustomerRecord
    {
        OwnerKey = ownerKey,
        NpcGuid = customerGuid,
        IsUnlocked = true,
    };

    bool allowed = OrganisationScopeRules.TryFindDealerContractOwner(
        saveData,
        dealerId,
        "Maya",
        "cust-npc-sale",
        customerGuid,
        "Albert",
        out string resolvedOwnerKey,
        out string denialMessage);

    Assert(!allowed, "dealer sale should reject customers outside the dealer assignment list");
    Equal(string.Empty, resolvedOwnerKey, "resolved owner key");
    Equal("Albert is not assigned to this dealer's scope.", denialMessage, "denial message");
});

Run("product sale listing stays scoped and clone isolated per owner", () =>
{
    ProductMarketScopeRecord crewMarket = new ProductMarketScopeRecord();
    crewMarket.DiscoveredProductIds.Add("og-kush");
    crewMarket.ListedProductIds.Add("og-kush");
    crewMarket.PricesByProductId["og-kush"] = 42f;
    crewMarket.ContractReceiptJson.Add("{\"receipt\":\"crew\"}");

    ProductMarketScopeRecord playerMarket = crewMarket.Clone();
    playerMarket.ListedProductIds.Remove("og-kush");
    playerMarket.ListedProductIds.Add("sour-diesel");
    playerMarket.PricesByProductId["sour-diesel"] = 65f;
    playerMarket.ContractReceiptJson[0] = "{\"receipt\":\"player\"}";

    Assert(crewMarket.ListedProductIds.Contains("og-kush"), "original owner should keep listed product");
    Assert(!crewMarket.ListedProductIds.Contains("sour-diesel"), "original owner should not inherit cloned listing");
    EqualFloat(42f, crewMarket.PricesByProductId["og-kush"], "original product price");
    Equal("{\"receipt\":\"crew\"}", crewMarket.ContractReceiptJson[0], "original contract receipt");
    Assert(!playerMarket.ListedProductIds.Contains("og-kush"), "clone should allow independent product removal");
    Assert(playerMarket.ListedProductIds.Contains("sour-diesel"), "clone should allow independent product listing");
    EqualFloat(65f, playerMarket.PricesByProductId["sour-diesel"], "clone product price");
});

Run("quest scope clone deep-copies product and cartel state", () =>
{
    QuestScopeRecord original = new QuestScopeRecord
    {
        OwnerKey = "org:crew",
        CartelActivityState = new CartelActivityScopeRecord
        {
            GlobalActivityIndex = 1,
            GlobalActivityRegion = "north",
            RegionalActivitiesByRegion = new Dictionary<string, RegionalCartelActivityScopeRecord>(StringComparer.OrdinalIgnoreCase)
            {
                ["north"] = new RegionalCartelActivityScopeRecord
                {
                    ActivityIndex = 2,
                    HoursUntilNextActivity = 3,
                },
            },
        },
        ProductMarketState = new ProductMarketScopeRecord
        {
            ListedProductIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "og-kush" },
            MixRecipes = new List<ProductMixRecipeScopeRecord>
            {
                new ProductMixRecipeScopeRecord
                {
                    ProductId = "og-kush",
                    MixerId = "gasoline",
                    OutputId = "diesel-kush",
                },
            },
        },
    };

    QuestScopeRecord clone = original.Clone();
    clone.ProductMarketState.ListedProductIds.Add("sour-diesel");
    clone.ProductMarketState.MixRecipes[0].OutputId = "mutated-output";
    clone.CartelActivityState.RegionalActivitiesByRegion["north"].ActivityIndex = 99;

    Assert(original.ProductMarketState.ListedProductIds.Contains("og-kush"), "original product listing should remain");
    Assert(!original.ProductMarketState.ListedProductIds.Contains("sour-diesel"), "original product listing should not receive clone addition");
    Equal("diesel-kush", original.ProductMarketState.MixRecipes[0].OutputId, "original mix recipe output");
    EqualInt(2, original.CartelActivityState.RegionalActivitiesByRegion["north"].ActivityIndex, "original regional activity index");
});

Run("dealer retention fee keeps paid dealer recruited", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string dealerKey = "org:crew|dealer-maya";
    saveData.ScopedDealers[dealerKey] = new ScopedDealerRecord
    {
        OwnerKey = "org:crew",
        NpcId = "dealer-maya",
        IsRecruited = true,
        Cash = 1250f,
        AssignedCustomerNpcIds = new List<string> { "cust-a" },
    };

    DealerRetentionProcessingResult result = OrganisationScopeRules.ProcessDealerRetentionFees(
        saveData,
        enabled: true,
        weeklyFee: 1000f,
        utcNow: DateTime.UtcNow);

    ScopedDealerRecord record = saveData.ScopedDealers[dealerKey];
    Assert(record.IsRecruited, "paid dealer should stay recruited");
    EqualFloat(250f, record.Cash, "dealer cash");
    EqualInt(1, record.AssignedCustomerNpcIds.Count, "assigned customer count");
    EqualInt(1, result.ProcessedCount, "processed count");
    EqualInt(1, result.PaidCount, "paid count");
    EqualInt(0, result.LostCount, "lost count");
});

Run("dealer retention fee removes unpaid dealer and clears assignments", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string dealerKey = "player:solo|dealer-benji";
    saveData.ScopedDealers[dealerKey] = new ScopedDealerRecord
    {
        OwnerKey = "player:solo",
        NpcId = "dealer-benji",
        HasBeenRecommended = true,
        IsRecruited = true,
        Cash = 300f,
        AssignedCustomerNpcIds = new List<string> { "cust-a", "cust-b" },
    };

    DealerRetentionProcessingResult result = OrganisationScopeRules.ProcessDealerRetentionFees(
        saveData,
        enabled: true,
        weeklyFee: 1000f,
        utcNow: DateTime.UtcNow);

    ScopedDealerRecord record = saveData.ScopedDealers[dealerKey];
    Assert(!record.IsRecruited, "unpaid dealer should stop working");
    Assert(record.HasBeenRecommended, "lost dealer should remain recommended so the owner can re-recruit later");
    EqualFloat(0f, record.Cash, "dealer cash");
    EqualInt(0, record.AssignedCustomerNpcIds.Count, "assigned customer count");
    EqualInt(1, result.ProcessedCount, "processed count");
    EqualInt(0, result.PaidCount, "paid count");
    EqualInt(1, result.LostCount, "lost count");
    EqualInt(1, result.LostDealerKeys.Count, "lost dealer key count");
});

Run("dealer retention fee toggle leaves recruited dealers untouched when disabled", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string dealerKey = "org:crew|dealer-maya";
    saveData.ScopedDealers[dealerKey] = new ScopedDealerRecord
    {
        OwnerKey = "org:crew",
        NpcId = "dealer-maya",
        IsRecruited = true,
        Cash = 0f,
        AssignedCustomerNpcIds = new List<string> { "cust-a" },
    };

    DealerRetentionProcessingResult result = OrganisationScopeRules.ProcessDealerRetentionFees(
        saveData,
        enabled: false,
        weeklyFee: 1000f,
        utcNow: DateTime.UtcNow);

    ScopedDealerRecord record = saveData.ScopedDealers[dealerKey];
    Assert(record.IsRecruited, "disabled retention should not remove recruited dealers");
    EqualFloat(0f, record.Cash, "dealer cash");
    EqualInt(1, record.AssignedCustomerNpcIds.Count, "assigned customer count");
    EqualInt(0, result.ProcessedCount, "processed count");
    Assert(!result.HasChanges, "disabled retention should report no changes");
});

Run("dealer retention warning records low-cash recruited dealer once per elapsed day", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string dealerKey = "org:crew|dealer-maya";
    saveData.ScopedDealers[dealerKey] = new ScopedDealerRecord
    {
        OwnerKey = "org:crew",
        NpcId = "dealer-maya",
        IsRecruited = true,
        Cash = 250f,
    };

    List<DealerRetentionWarningRecord> firstWarnings = OrganisationScopeRules.RecordDealerRetentionWarnings(
        saveData,
        enabled: true,
        weeklyFee: 1000f,
        elapsedDay: 13,
        utcNow: DateTime.UtcNow);
    List<DealerRetentionWarningRecord> secondWarnings = OrganisationScopeRules.RecordDealerRetentionWarnings(
        saveData,
        enabled: true,
        weeklyFee: 1000f,
        elapsedDay: 13,
        utcNow: DateTime.UtcNow);

    EqualInt(1, firstWarnings.Count, "first warning count");
    Equal("org:crew", firstWarnings[0].OwnerKey, "warning owner key");
    Equal("dealer-maya", firstWarnings[0].DealerNpcId, "warning dealer id");
    EqualFloat(250f, firstWarnings[0].Cash, "warning cash");
    EqualFloat(1000f, firstWarnings[0].WeeklyFee, "warning weekly fee");
    EqualInt(0, secondWarnings.Count, "second warning count");
    EqualInt(13, saveData.ScopedDealers[dealerKey].LastRetentionWarningElapsedDay, "last warning day");
});

Run("dealer retention warning can repeat on a later elapsed day if still underfunded", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    const string dealerKey = "org:crew|dealer-maya";
    saveData.ScopedDealers[dealerKey] = new ScopedDealerRecord
    {
        OwnerKey = "org:crew",
        NpcId = "dealer-maya",
        IsRecruited = true,
        Cash = 250f,
    };

    List<DealerRetentionWarningRecord> firstWarnings = OrganisationScopeRules.RecordDealerRetentionWarnings(
        saveData,
        enabled: true,
        weeklyFee: 1000f,
        elapsedDay: 13,
        utcNow: DateTime.UtcNow);
    List<DealerRetentionWarningRecord> nextDayWarnings = OrganisationScopeRules.RecordDealerRetentionWarnings(
        saveData,
        enabled: true,
        weeklyFee: 1000f,
        elapsedDay: 14,
        utcNow: DateTime.UtcNow);

    EqualInt(1, firstWarnings.Count, "first warning count");
    EqualInt(1, nextDayWarnings.Count, "next day warning count");
    EqualInt(14, saveData.ScopedDealers[dealerKey].LastRetentionWarningElapsedDay, "last warning day");
});

Run("dealer retention warning ignores disabled or funded dealers", () =>
{
    OrganisationSaveData saveData = new OrganisationSaveData();
    saveData.ScopedDealers["org:crew|dealer-maya"] = new ScopedDealerRecord
    {
        OwnerKey = "org:crew",
        NpcId = "dealer-maya",
        IsRecruited = true,
        Cash = 1250f,
    };
    saveData.ScopedDealers["org:crew|dealer-benji"] = new ScopedDealerRecord
    {
        OwnerKey = "org:crew",
        NpcId = "dealer-benji",
        IsRecruited = true,
        Cash = 100f,
    };

    List<DealerRetentionWarningRecord> disabledWarnings = OrganisationScopeRules.RecordDealerRetentionWarnings(
        saveData,
        enabled: false,
        weeklyFee: 1000f,
        elapsedDay: 14,
        utcNow: DateTime.UtcNow);
    List<DealerRetentionWarningRecord> enabledWarnings = OrganisationScopeRules.RecordDealerRetentionWarnings(
        saveData,
        enabled: true,
        weeklyFee: 1000f,
        elapsedDay: 14,
        utcNow: DateTime.UtcNow);

    EqualInt(0, disabledWarnings.Count, "disabled warning count");
    EqualInt(1, enabledWarnings.Count, "enabled warning count");
    Equal("dealer-benji", enabledWarnings[0].DealerNpcId, "low-cash dealer id");
});

static void Run(string name, Action test)
{
    try
    {
        test();
        Console.WriteLine($"PASS {name}");
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"FAIL {name}: {ex.Message}");
        Environment.ExitCode = 1;
    }
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

static void Equal(string expected, string actual, string label)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'");
    }
}

static void EqualInt(int expected, int actual, string label)
{
    if (expected != actual)
    {
        throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'");
    }
}

static void EqualFloat(float expected, float actual, string label)
{
    if (Math.Abs(expected - actual) > 0.001f)
    {
        throw new InvalidOperationException($"{label}: expected '{expected}', got '{actual}'");
    }
}
