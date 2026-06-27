#if SERVER
using System;
using System.Collections.Generic;
using DedicatedServerMod.Organisations.Utils;
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Messaging;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.NPCs.Responses;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Product;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.UI.Handover;
using Il2CppScheduleOne.UI.Phone.Messages;
using Il2CppScheduleOne.VoiceOver;
using ScopedDialogueDatabase = Il2CppScheduleOne.Dialogue.DialogueDatabase;
using Guid = Il2CppSystem.Guid;
#else
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.Cartel;
using ScheduleOne.Dialogue;
using ScheduleOne.DevUtilities;
using ScheduleOne.Economy;
using ScheduleOne.ItemFramework;
using ScheduleOne.Map;
using ScheduleOne.Messaging;
using ScheduleOne.NPCs;
using ScheduleOne.NPCs.Behaviour;
using ScheduleOne.NPCs.CharacterClasses;
using ScheduleOne.NPCs.Responses;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Product;
using ScheduleOne.Quests;
using ScheduleOne.UI.Handover;
using ScheduleOne.UI.Phone.Messages;
using ScheduleOne.VoiceOver;
using ScopedDialogueDatabase = ScheduleOne.Dialogue.DialogueDatabase;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationCustomerContractPatches
{
    private static readonly System.Reflection.FieldInfo? CustomerDialogueDatabaseField = AccessTools.Field(typeof(Customer), "dialogueDatabase");
    private static readonly System.Reflection.FieldInfo? CustomerMinsSinceUnlockedField = AccessTools.Field(typeof(Customer), "minsSinceUnlocked");
    private static readonly System.Reflection.FieldInfo? CustomerTimeSinceLastDealCompletedField = AccessTools.Field(typeof(Customer), "<TimeSinceLastDealCompleted>k__BackingField");
    private static readonly System.Reflection.MethodInfo? CustomerGetOrderableProductsMethod = AccessTools.Method(typeof(Customer), "GetOrderableProducts", new[] { typeof(Dealer) });
    private static readonly Dictionary<string, int> DealerInventoryMutationDepthByNpcId = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> PreparedCustomerStateMutationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    private static bool IsEmptyGuid(Guid guid)
    {
        return guid.Equals(Guid.Empty);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_SendContractAccepted_507093020")]
    [HarmonyPrefix]
    private static bool CustomerContractAcceptedReaderPrefix(Customer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        return serverMod.TryHandleCustomerContractAcceptance(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.ContractAccepted))]
    [HarmonyPostfix]
    private static void CustomerContractAcceptedPostfix(Customer __instance, Contract __result)
    {
        OrganisationsServerMod.ActiveInstance?.FinalizeCustomerContractAcceptance(__instance, __result);
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.OfferContract))]
    [HarmonyPrefix]
    private static bool CustomerOfferContractPrefix()
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.HasHydratedQuestOwner();
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.OfferContract))]
    [HarmonyPostfix]
    private static void CustomerOfferContractPostfix(Customer __instance)
    {
        OrganisationsServerMod.ActiveInstance?.NotifyCustomerOfferGenerated(__instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_ProcessCounterOfferServerSide_900355577")]
    [HarmonyPrefix]
    private static bool CustomerCounterOfferReaderPrefix(Customer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient)
        {
            return true;
        }

        return serverMod.TryHandleCustomerOfferMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_ProcessCounterOfferServerSide_900355577")]
    [HarmonyPostfix]
    private static void CustomerCounterOfferReaderPostfix(Customer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        _ = conn;

        OrganisationsServerMod.ActiveInstance?.FinalizeCustomerOfferMutation(__instance);
    }

    [HarmonyPatch(typeof(Customer), "ContractRejected")]
    [HarmonyPostfix]
    private static void CustomerContractRejectedPostfix(Customer __instance)
    {
        OrganisationsServerMod.ActiveInstance?.NotifyCustomerOfferCleared(__instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcLogic___ExpireOffer_2166136261")]
    [HarmonyPostfix]
    private static void CustomerOfferExpiredPostfix(Customer __instance)
    {
        OrganisationsServerMod.ActiveInstance?.NotifyCustomerOfferCleared(__instance);
    }

    [HarmonyPatch(typeof(Customer), "OnMinPass")]
    [HarmonyPostfix]
    private static void CustomerOnMinPassPostfix(Customer __instance)
    {
        OrganisationsServerMod.ActiveInstance?.RecordHydratedCustomerState(__instance);
    }

    [HarmonyPatch(typeof(Customer), "OnSleepStart")]
    [HarmonyPostfix]
    private static void CustomerOnSleepStartPostfix(Customer __instance)
    {
        OrganisationsServerMod.ActiveInstance?.RecordHydratedCustomerState(__instance);
    }

    [HarmonyPatch(typeof(Customer), "ShouldTryApproachPlayer")]
    [HarmonyPrefix]
    private static bool CustomerShouldTryApproachPlayerPrefix(Customer __instance, ref bool __result)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.HasHydratedQuestOwner())
        {
            __result = false;
            return false;
        }

        __result = ShouldHydratedCustomerTryApproachPlayer(__instance, serverMod);
        return false;
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.RequestProduct), new[] { typeof(Player) })]
    [HarmonyPrefix]
    private static bool CustomerRequestProductPrefix(Customer __instance, ref Player target)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TrySelectHydratedCustomerRequestTarget(__instance, target, out Player? scopedTarget))
        {
            return false;
        }

        if (scopedTarget != null)
        {
            target = scopedTarget;
        }

        return true;
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.PlayerRejectedProductRequest))]
    [HarmonyPrefix]
    private static bool CustomerPlayerRejectedProductRequestPrefix(Customer __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.HasHydratedQuestOwner())
        {
            return false;
        }

        if (__instance?.NPC == null)
        {
            return false;
        }

        __instance.NPC.PlayVO(EVOLineType.Annoyed);
        __instance.NPC.Avatar?.EmotionManager?.AddEmotionOverride("Annoyed", "product_rejected", 30f, 1);
        if (TryGetCustomerDialogueLine(__instance, "request_product_rejected", out string dialogueLine))
        {
            __instance.NPC.DialogueHandler?.ShowWorldspaceDialogue(dialogueLine, 5f);
        }

        if (__instance.NPC.Responses is NPCResponses_Civilian && __instance.NPC.Aggression > 0.1f)
        {
            float chance = Mathf.Clamp(__instance.NPC.Aggression, 0f, 0.7f);
            chance -= __instance.NPC.RelationData.NormalizedRelationDelta * 0.3f;
            chance += __instance.CurrentAddiction * 0.2f;
            if (UnityEngine.Random.Range(0f, 1f) < chance
                && serverMod.TryFindHydratedCustomerRequestRetaliationTarget(__instance.transform.position, out Player? target)
                && target?.NetworkObject != null)
            {
                __instance.NPC.Behaviour.CombatBehaviour.SetTargetAndEnable_Server(target.NetworkObject);
            }
        }

        return false;
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.CustomerRejectedDeal))]
    [HarmonyPrefix]
    private static bool CustomerRejectedDealPrefix(Customer __instance, bool offeredByPlayer)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.HasHydratedQuestOwner())
        {
            return false;
        }

        if (__instance?.NPC == null || __instance.CurrentContract == null)
        {
            return true;
        }

        if (offeredByPlayer)
        {
            Singleton<HandoverScreen>.Instance.ClearCustomerSlots(returnToOriginals: true);
        }

        __instance.CurrentContract.Fail();
        __instance.NPC.RelationData.ChangeRelationship(-0.5f);
        __instance.NPC.PlayVO(EVOLineType.Annoyed);
        __instance.NPC.Avatar?.EmotionManager?.AddEmotionOverride("Annoyed", "deal_rejected", 30f);
        if (TryGetCustomerDialogueLine(__instance, "customer_rejected_deal", out string dialogueLine))
        {
            __instance.NPC.DialogueHandler?.ShowWorldspaceDialogue(dialogueLine, 5f);
        }

        SetTimeSinceLastDealCompleted(__instance, 0);
        if (__instance.NPC.RelationData.RelationDelta < 2.5f
            && offeredByPlayer
            && __instance.NPC.Responses is NPCResponses_Civilian
            && __instance.NPC.Aggression > 0.5f
            && UnityEngine.Random.Range(0f, __instance.NPC.RelationData.NormalizedRelationDelta) < __instance.NPC.Aggression * 0.5f
            && serverMod.TryFindHydratedCustomerRequestRetaliationTarget(__instance.transform.position, out Player? target)
            && target?.NetworkObject != null)
        {
            __instance.NPC.Behaviour.CombatBehaviour.SetTargetAndEnable_Server(target.NetworkObject);
        }

        serverMod.RecordHydratedCustomerState(__instance);
        __instance.Invoke("EndWait", 1f);
        return false;
    }

    private static bool TryGetCustomerDialogueLine(Customer customer, string lineKey, out string line)
    {
        line = string.Empty;
        if (CustomerDialogueDatabaseField?.GetValue(customer) is not ScopedDialogueDatabase dialogueDatabase)
        {
            return false;
        }

        line = dialogueDatabase.GetLine(EDialogueModule.Customer, lineKey);
        return !string.IsNullOrWhiteSpace(line);
    }

    private static bool ShouldHydratedCustomerTryApproachPlayer(Customer customer, OrganisationsServerMod serverMod)
    {
        if (customer?.NPC == null)
        {
            return false;
        }

        if (!customer.NPC.RelationData.Unlocked
            || customer.CurrentContract != null
            || customer.OfferedContractInfo != null
            || customer.TimeSinceLastDealCompleted < 1440
            || GetMinsSinceUnlocked(customer) < 30
            || !customer.NPC.IsConscious
            || customer.AssignedDealer != null
            || customer.NPC.Behaviour.RequestProductBehaviour.Active
            || customer.NPC.DialogueHandler.IsDialogueInProgress
            || customer.CurrentAddiction < 0.33f
            || (float)customer.TimeSincePlayerApproached < Mathf.Lerp(4320f, 2160f, customer.CurrentAddiction)
            || GetOrderableProductCount(customer) == 0)
        {
            return false;
        }

        if (!serverMod.TryGetHydratedOwnerClosestPlayerDistance(customer.transform.position, out float distance))
        {
            return false;
        }

        if (distance < 20f)
        {
            return false;
        }

        for (int i = 0; i < Customer.UnlockedCustomers.Count; i++)
        {
            Customer unlockedCustomer = Customer.UnlockedCustomers[i];
            if (unlockedCustomer?.NPC?.Behaviour?.RequestProductBehaviour == null
                || !serverMod.TryIsHydratedOwnerNpcUnlocked(unlockedCustomer.NPC, out bool isUnlocked)
                || !isUnlocked)
            {
                continue;
            }

            if (unlockedCustomer.NPC.Behaviour.RequestProductBehaviour.Active)
            {
                return false;
            }
        }

        return true;
    }

    private static int GetMinsSinceUnlocked(Customer customer)
    {
        return CustomerMinsSinceUnlockedField?.GetValue(customer) is int minsSinceUnlocked
            ? minsSinceUnlocked
            : int.MaxValue;
    }

    private static void SetTimeSinceLastDealCompleted(Customer customer, int value)
    {
        CustomerTimeSinceLastDealCompletedField?.SetValue(customer, value);
    }

    private static int GetOrderableProductCount(Customer customer)
    {
        object? orderableProducts = CustomerGetOrderableProductsMethod?.Invoke(customer, new object?[] { null });
        if (orderableProducts is System.Collections.ICollection collection)
        {
            return collection.Count;
        }

        return 0;
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_ExpireOffer_2166136261")]
    [HarmonyPrefix]
    private static bool CustomerExpireOfferReaderPrefix(Customer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient)
        {
            return true;
        }

        return serverMod.TryHandleCustomerOfferMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_ExpireOffer_2166136261")]
    [HarmonyPostfix]
    private static void CustomerExpireOfferReaderPostfix(Customer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        _ = conn;

        OrganisationsServerMod.ActiveInstance?.FinalizeCustomerOfferMutation(__instance);
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.CurrentContractEnded))]
    [HarmonyPostfix]
    private static void CustomerCurrentContractEndedPostfix(Customer __instance, EQuestState outcome)
    {
        OrganisationsServerMod.ActiveInstance?.ReleaseCustomerContract(__instance, outcome.ToString());
    }

    [HarmonyPatch(typeof(Customer), "ProcessHandoverClient")]
    [HarmonyPostfix]
    private static void CustomerProcessHandoverClientPostfix(Customer __instance, float satisfaction, bool handoverByPlayer, string npcToRecommend, HandoverScreen.EHandoverOutcome outcome)
    {
        _ = satisfaction;
        _ = handoverByPlayer;
        _ = outcome;

        OrganisationsServerMod.ActiveInstance?.RecordCustomerRecommendationFromHandover(__instance, npcToRecommend);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_ProcessHandoverServerSide_3760244802")]
    [HarmonyPrefix]
    private static void CustomerProcessHandoverServerSideReaderPrefix(NetworkConnection conn)
    {
        OrganisationsServerMod.ActiveInstance?.PrepareCustomerHandoverScope(conn);
    }

    [HarmonyPatch(typeof(Customer), "RpcLogic___ProcessHandoverServerSide_3760244802")]
    [HarmonyPrefix]
    private static void CustomerProcessHandoverServerSidePrefix(Customer __instance, bool handoverByPlayer, NetworkObject dealerObject)
    {
        OrganisationsServerMod.ActiveInstance?.PrepareCartelContractReceiptOwner(__instance, handoverByPlayer, dealerObject);
    }

    [HarmonyPatch(typeof(Customer), "RpcLogic___ProcessHandoverServerSide_3760244802")]
    [HarmonyPostfix]
    private static void CustomerProcessHandoverServerSidePostfix()
    {
        OrganisationsServerMod.ActiveInstance?.ClearActiveDealerSaleOwner();
    }

    [HarmonyPatch(typeof(ProductManager), nameof(ProductManager.RecordContractReceipt))]
    [HarmonyPostfix]
    private static void ProductManagerRecordContractReceiptPostfix(NetworkConnection conn, ContractReceipt receipt)
    {
        _ = conn;
        OrganisationsServerMod.ActiveInstance?.ClearCartelContractReceiptOwner(receipt);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_ProcessSampleServerSide_3704012609")]
    [HarmonyPrefix]
    private static bool CustomerSampleServerReaderPrefix(Customer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient)
        {
            return true;
        }

        return serverMod.TryPrepareCustomerDiscoveryMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_ChangeAddiction_431000436")]
    [HarmonyPrefix]
    private static bool CustomerChangeAddictionReaderPrefix(Customer __instance, NetworkConnection conn)
    {
        return PrepareCustomerStateMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_ChangeAddiction_431000436")]
    [HarmonyPostfix]
    private static void CustomerChangeAddictionReaderPostfix(Customer __instance, NetworkConnection conn)
    {
        RecordCustomerStateMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_AdjustAffinity_3036964899")]
    [HarmonyPrefix]
    private static bool CustomerAdjustAffinityReaderPrefix(Customer __instance, NetworkConnection conn)
    {
        return PrepareCustomerStateMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_AdjustAffinity_3036964899")]
    [HarmonyPostfix]
    private static void CustomerAdjustAffinityReaderPostfix(Customer __instance, NetworkConnection conn)
    {
        RecordCustomerStateMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_RejectProductRequestOffer_2166136261")]
    [HarmonyPrefix]
    private static bool CustomerRejectProductRequestOfferReaderPrefix(Customer __instance, NetworkConnection conn)
    {
        return PrepareCustomerStateMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Customer), "RpcReader___Server_RejectProductRequestOffer_2166136261")]
    [HarmonyPostfix]
    private static void CustomerRejectProductRequestOfferReaderPostfix(Customer __instance, NetworkConnection conn)
    {
        RecordCustomerStateMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(NPCBehaviour), "RpcReader___Server_ConsumeProduct_3964170259")]
    [HarmonyPrefix]
    private static bool NpcConsumeProductReaderPrefix(NPCBehaviour __instance, NetworkConnection conn)
    {
        if (!TryFindCustomer(__instance?.Npc, out Customer? customer) || customer == null)
        {
            return true;
        }

        return PrepareCustomerStateMutation(conn, customer);
    }

    [HarmonyPatch(typeof(NPCBehaviour), "RpcReader___Server_ConsumeProduct_3964170259")]
    [HarmonyPostfix]
    private static void NpcConsumeProductReaderPostfix(NPCBehaviour __instance, NetworkConnection conn)
    {
        if (TryFindCustomer(__instance?.Npc, out Customer? customer) && customer != null)
        {
            RecordCustomerStateMutation(conn, customer);
        }
    }

    [HarmonyPatch(typeof(ConsumeProductBehaviour), "RpcReader___Server_SendProduct_3964170259")]
    [HarmonyPrefix]
    private static bool NpcSendProductReaderPrefix(ConsumeProductBehaviour __instance, NetworkConnection conn)
    {
        if (!TryFindCustomer(__instance?.Npc, out Customer? customer) || customer == null)
        {
            return true;
        }

        return PrepareCustomerStateMutation(conn, customer);
    }

    [HarmonyPatch(typeof(ConsumeProductBehaviour), "RpcReader___Server_SendProduct_3964170259")]
    [HarmonyPostfix]
    private static void NpcSendProductReaderPostfix(ConsumeProductBehaviour __instance, NetworkConnection conn)
    {
        if (TryFindCustomer(__instance?.Npc, out Customer? customer) && customer != null)
        {
            RecordCustomerStateMutation(conn, customer);
        }
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_MarkAsRecommended_2166136261")]
    [HarmonyPrefix]
    private static bool DealerRecommendedServerReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        _ = conn;
        return false;
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.ContractedOffered))]
    [HarmonyPrefix]
    private static bool DealerContractedOfferedPrefix(Dealer __instance, ContractInfo contractInfo, Customer customer)
    {
        _ = contractInfo;
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryPrepareDealerContractAcceptance(__instance, customer);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.RemoveContractItems))]
    [HarmonyPrefix]
    private static bool DealerRemoveContractItemsPrefix(Dealer __instance)
    {
        return PrepareDealerInventoryMutation(__instance);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.RemoveContractItems))]
    [HarmonyPostfix]
    private static void DealerRemoveContractItemsPostfix(Dealer __instance)
    {
        RecordDealerInventory(__instance);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.AddItemToInventory))]
    [HarmonyPrefix]
    private static bool DealerAddItemToInventoryPrefix(Dealer __instance)
    {
        return PrepareDealerInventoryMutation(__instance);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.AddItemToInventory))]
    [HarmonyPostfix]
    private static void DealerAddItemToInventoryPostfix(Dealer __instance)
    {
        RecordDealerInventory(__instance);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.TryMoveOverflowItems))]
    [HarmonyPrefix]
    private static bool DealerTryMoveOverflowItemsPrefix(Dealer __instance)
    {
        return PrepareDealerInventoryMutation(__instance);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.TryMoveOverflowItems))]
    [HarmonyPostfix]
    private static void DealerTryMoveOverflowItemsPostfix(Dealer __instance)
    {
        RecordDealerInventory(__instance);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.TryRobDealer))]
    [HarmonyPrefix]
    private static bool DealerTryRobDealerPrefix(Dealer __instance)
    {
        return PrepareDealerInventoryMutation(__instance, includeCash: true);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.TryRobDealer))]
    [HarmonyPostfix]
    private static void DealerTryRobDealerPostfix(Dealer __instance)
    {
        RecordDealerInventory(__instance, includeCash: true);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.CompletedDeal))]
    [HarmonyPrefix]
    private static bool DealerCompletedDealPrefix(Dealer __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        bool continueOriginal = serverMod.TryHandleScopedDealerCompletedDeal(__instance);
        if (!continueOriginal)
        {
            __instance.onCompleteDeal?.Invoke();
        }

        return continueOriginal;
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.SubmitPayment))]
    [HarmonyPrefix]
    private static bool DealerSubmitPaymentPrefix(Dealer __instance, float payment)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryHandleScopedDealerPayment(__instance, payment);
    }

    [HarmonyPatch(typeof(Dealer), nameof(Dealer.CheckNotifyPlayerOfDeal))]
    [HarmonyPrefix]
    private static bool DealerCheckNotifyPlayerOfDealPrefix(Dealer __instance, Dealer cartelDealer, Contract contract)
    {
        _ = __instance;
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryHandleScopedCartelDealNotification(cartelDealer, contract);
    }

    [HarmonyPatch(typeof(RobDealer), "GetDealerToRob")]
    [HarmonyPrefix]
    private static bool CartelRobDealerGetDealerToRobPrefix(EMapRegion region, ref Dealer? __result)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (serverMod.TryGetDealerForCartelRobbery(region, out Dealer? dealer))
        {
            __result = dealer;
            return false;
        }

        __result = null;
        return false;
    }

    [HarmonyPatch(typeof(StealDeadDrop), "GetRandomDropToStealFrom")]
    [HarmonyPrefix]
    private static bool CartelStealDeadDropGetRandomDropPrefix(EMapRegion region, ref DeadDrop? __result)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (serverMod.TryPrepareCartelDeadDropTheft(region, out DeadDrop? drop))
        {
            __result = drop;
            return false;
        }

        __result = null;
        return false;
    }

    [HarmonyPatch(typeof(StealDeadDrop), nameof(StealDeadDrop.Activate))]
    [HarmonyPostfix]
    private static void CartelStealDeadDropActivatePostfix(EMapRegion region)
    {
        OrganisationsServerMod.ActiveInstance?.FinalizeCartelDeadDropTheft(region);
    }

    [HarmonyPatch(typeof(Ambush), "ContractReceiptRecorded")]
    [HarmonyPrefix]
    private static bool CartelAmbushContractReceiptRecordedPrefix(Ambush __instance, ContractReceipt receipt)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryHandleCartelContractReceiptAmbush(__instance, receipt);
    }

    [HarmonyPatch(typeof(DeadDrop), nameof(DeadDrop.GetRandomEmptyDrop))]
    [HarmonyPrefix]
    private static bool DeadDropGetRandomEmptyDropPrefix(Vector3 origin, ref DeadDrop? __result)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (serverMod.TryGetRandomAvailableDeaddrop(origin, out DeadDrop? drop))
        {
            __result = drop;
            return false;
        }

        __result = null;
        return false;
    }

    [HarmonyPatch(typeof(DeadDrop), "UpdateDeadDrop")]
    [HarmonyPrefix]
    private static bool DeadDropUpdateDeadDropPrefix(DeadDrop __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleDeadDropUpdate(__instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_InitialRecruitment_2166136261")]
    [HarmonyPrefix]
    private static bool DealerInitialRecruitmentServerReaderPrefix(Dealer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryHandleDealerRecruitment(conn, __instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_AddCustomer_Server_3615296227")]
    [HarmonyPrefix]
    private static bool DealerAddCustomerServerReaderPrefix(Dealer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        string npcId = PooledReader0.ReadString();
        if (conn == null || conn.IsLocalClient)
        {
            __instance.RpcLogic___AddCustomer_Server_3615296227(npcId);
            return false;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod != null && serverMod.TryHandleDealerCustomerAssignment(conn, __instance, npcId, addAssignment: true))
        {
            __instance.RpcLogic___AddCustomer_Server_3615296227(npcId);
        }

        return false;
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SendRemoveCustomer_3615296227")]
    [HarmonyPrefix]
    private static bool DealerRemoveCustomerServerReaderPrefix(Dealer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        string npcId = PooledReader0.ReadString();
        if (conn == null || conn.IsLocalClient)
        {
            __instance.RpcLogic___SendRemoveCustomer_3615296227(npcId);
            return false;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod != null && serverMod.TryHandleDealerCustomerAssignment(conn, __instance, npcId, addAssignment: false))
        {
            __instance.RpcLogic___SendRemoveCustomer_3615296227(npcId);
        }

        return false;
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetCash_431000436")]
    [HarmonyPrefix]
    private static bool DealerSetCashServerReaderPrefix(Dealer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        float cash = PooledReader0.ReadSingle();
        if (conn == null || conn.IsLocalClient)
        {
            __instance.RpcLogic___SetCash_431000436(cash);
            return false;
        }

        OrganisationsServerMod.ActiveInstance?.TryHandleDealerCashSet(conn, __instance, cash);
        return false;
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SubmitPayment_431000436")]
    [HarmonyPrefix]
    private static bool DealerSubmitPaymentServerReaderPrefix(Dealer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        float payment = PooledReader0.ReadSingle();
        if (conn == null || conn.IsLocalClient)
        {
            __instance.RpcLogic___SubmitPayment_431000436(payment);
            return false;
        }

        OrganisationsServerMod.ActiveInstance?.TryHandleDealerPayment(conn, __instance, payment);
        return false;
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_CompletedDeal_2166136261")]
    [HarmonyPrefix]
    private static bool DealerCompletedDealServerReaderPrefix(Dealer __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            __instance.RpcLogic___CompletedDeal_2166136261();
            return false;
        }

        if (OrganisationsServerMod.ActiveInstance != null && OrganisationsServerMod.ActiveInstance.TryHandleDealerCompletedDeal(conn, __instance))
        {
            __instance.onCompleteDeal?.Invoke();
        }

        return false;
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetStoredInstance_2652194801")]
    [HarmonyPrefix]
    private static bool DealerSetStoredInstanceReaderPrefix(Dealer __instance, NetworkConnection conn)
    {
        return CanAccessDealerInventory(conn, __instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetStoredInstance_2652194801")]
    [HarmonyPostfix]
    private static void DealerSetStoredInstanceReaderPostfix(Dealer __instance, NetworkConnection conn)
    {
        RecordDealerInventory(conn, __instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetItemSlotQuantity_1692629761")]
    [HarmonyPrefix]
    private static bool DealerSetItemSlotQuantityReaderPrefix(Dealer __instance, NetworkConnection conn)
    {
        return CanAccessDealerInventory(conn, __instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetItemSlotQuantity_1692629761")]
    [HarmonyPostfix]
    private static void DealerSetItemSlotQuantityReaderPostfix(Dealer __instance, NetworkConnection conn)
    {
        RecordDealerInventory(conn, __instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetSlotLocked_3170825843")]
    [HarmonyPrefix]
    private static bool DealerSetSlotLockedReaderPrefix(Dealer __instance, NetworkConnection conn)
    {
        return CanAccessDealerInventory(conn, __instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetSlotLocked_3170825843")]
    [HarmonyPostfix]
    private static void DealerSetSlotLockedReaderPostfix(Dealer __instance, NetworkConnection conn)
    {
        RecordDealerInventory(conn, __instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetSlotFilter_527532783")]
    [HarmonyPrefix]
    private static bool DealerSetSlotFilterReaderPrefix(Dealer __instance, NetworkConnection conn)
    {
        return CanAccessDealerInventory(conn, __instance);
    }

    [HarmonyPatch(typeof(Dealer), "RpcReader___Server_SetSlotFilter_527532783")]
    [HarmonyPostfix]
    private static void DealerSetSlotFilterReaderPostfix(Dealer __instance, NetworkConnection conn)
    {
        RecordDealerInventory(conn, __instance);
    }

    [HarmonyPatch(typeof(NPCInventory), "SetStoredInstance_Internal")]
    [HarmonyPrefix]
    private static bool NpcInventorySetStoredInstanceInternalPrefix(NPCInventory __instance, NetworkConnection conn, int itemSlotIndex, ItemInstance? instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null
            || (!serverMod.TryHandleCartelDealerInventoryStoredInstanceReplay(__instance, conn, itemSlotIndex, instance)
                && serverMod.ShouldReplayCartelDealerInventoryToConnection(__instance, conn));
    }

    [HarmonyPatch(typeof(NPCInventory), "SetItemSlotQuantity_Internal")]
    [HarmonyPrefix]
    private static bool NpcInventorySetItemSlotQuantityInternalPrefix(NPCInventory __instance, int itemSlotIndex, int quantity)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleCartelDealerInventoryQuantityReplay(__instance, itemSlotIndex, quantity);
    }

    [HarmonyPatch(typeof(NPCInventory), "SetSlotLocked_Internal")]
    [HarmonyPrefix]
    private static bool NpcInventorySetSlotLockedInternalPrefix(NPCInventory __instance, NetworkConnection conn, int itemSlotIndex, bool locked, NetworkObject lockOwner, string lockReason)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null
            || (!serverMod.TryHandleCartelDealerInventorySlotLockedReplay(__instance, conn, itemSlotIndex, locked, lockOwner, lockReason)
                && serverMod.ShouldReplayCartelDealerInventoryToConnection(__instance, conn));
    }

    [HarmonyPatch(typeof(NPCInventory), "SetSlotFilter_Internal")]
    [HarmonyPrefix]
    private static bool NpcInventorySetSlotFilterInternalPrefix(NPCInventory __instance, NetworkConnection conn, int itemSlotIndex, SlotFilter filter)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null
            || (!serverMod.TryHandleCartelDealerInventorySlotFilterReplay(__instance, conn, itemSlotIndex, filter)
                && serverMod.ShouldReplayCartelDealerInventoryToConnection(__instance, conn));
    }

    [HarmonyPatch(typeof(Supplier), "RpcReader___Server_SendUnlocked_2166136261")]
    [HarmonyPrefix]
    private static bool SupplierUnlockedServerReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        _ = conn;
        return false;
    }

    [HarmonyPatch(typeof(Supplier), "RpcReader___Server_SetDeaddrop_3971994486")]
    [HarmonyPrefix]
    private static bool SupplierSetDeaddropReaderPrefix(Supplier __instance, NetworkConnection conn)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryPrepareSupplierMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Supplier), "RpcReader___Server_SetDeaddrop_3971994486")]
    [HarmonyPostfix]
    private static void SupplierSetDeaddropReaderPostfix(Supplier __instance, NetworkConnection conn)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        serverMod?.RecordScopedSupplierState(conn, __instance);
        serverMod?.RecordPendingSupplierDeaddropOwner(conn, __instance);
    }

    [HarmonyPatch(typeof(Supplier), "RpcReader___Server_ChangeDebt_431000436")]
    [HarmonyPrefix]
    private static bool SupplierChangeDebtReaderPrefix(Supplier __instance, NetworkConnection conn)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryPrepareSupplierMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(Supplier), "RpcReader___Server_ChangeDebt_431000436")]
    [HarmonyPostfix]
    private static void SupplierChangeDebtReaderPostfix(Supplier __instance, NetworkConnection conn)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.RecordScopedSupplierState(conn, __instance);
    }

    [HarmonyPatch(typeof(Supplier), "TryRecoverDebt")]
    [HarmonyPrefix]
    private static bool SupplierTryRecoverDebtPrefix(Supplier __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryPrepareSupplierDebtRecovery(__instance);
    }

    [HarmonyPatch(typeof(Supplier), "TryRecoverDebt")]
    [HarmonyPostfix]
    private static void SupplierTryRecoverDebtPostfix(Supplier __instance)
    {
        OrganisationsServerMod.ActiveInstance?.CompleteSupplierDebtRecovery(__instance);
    }

    [HarmonyPatch(typeof(Supplier), "CompleteDeaddrop")]
    [HarmonyPrefix]
    private static void SupplierCompleteDeaddropPrefix(Supplier __instance)
    {
        OrganisationsServerMod.ActiveInstance?.BeginSupplierDeaddropCompletion(__instance);
    }

    [HarmonyPatch(typeof(Supplier), "CompleteDeaddrop")]
    [HarmonyFinalizer]
    private static void SupplierCompleteDeaddropFinalizer(Supplier __instance)
    {
        OrganisationsServerMod.ActiveInstance?.EndSupplierDeaddropCompletion(__instance);
    }

    [HarmonyPatch(typeof(MSGConversation), nameof(MSGConversation.SendMessageChain))]
    [HarmonyPrefix]
    private static bool SupplierDeaddropReadyMessagePrefix(MSGConversation __instance, MessageChain messages)
    {
        if (__instance?.sender is not Supplier supplier
            || messages?.Messages == null
            || messages.Messages.Count == 0)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedSupplierDeaddropReadyMessage(supplier, messages.Messages[0]);
    }

    [HarmonyPatch(typeof(Supplier), nameof(Supplier.MeetAtLocation))]
    [HarmonyPrefix]
    private static bool SupplierMeetAtLocationPrefix(Supplier __instance, NetworkConnection conn)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.CanReplaySupplierMeeting(conn, __instance);
    }

    [HarmonyPatch(typeof(Supplier), nameof(Supplier.EndMeeting))]
    [HarmonyPostfix]
    private static void SupplierEndMeetingPostfix(Supplier __instance)
    {
        OrganisationsServerMod.ActiveInstance?.NotifySupplierMeetingEnded(__instance);
    }

    [HarmonyPatch(typeof(Supplier), "MinPass")]
    [HarmonyPostfix]
    private static void SupplierMinPassPostfix(Supplier __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        serverMod?.EnforceScopedSupplierMeetingExpiry(__instance);
        serverMod?.RefreshScopedSupplierMeeting(__instance);
    }

    [HarmonyPatch(typeof(Supplier), "OnTimeSkip")]
    [HarmonyPostfix]
    private static void SupplierTimeSkipPostfix(Supplier __instance, int minsSlept)
    {
        _ = minsSlept;
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        serverMod?.RefreshScopedSupplierMeeting(__instance);
        serverMod?.RefreshScopedSupplierDeaddropTimer(__instance);
    }

    [HarmonyPatch(typeof(Supplier), nameof(Supplier.OnSpawnServer))]
    [HarmonyPostfix]
    private static void SupplierOnSpawnServerPostfix(Supplier __instance, NetworkConnection connection)
    {
        OrganisationsServerMod.ActiveInstance?.ReplayScopedSupplierMeetingOnSpawn(connection, __instance);
    }

    [HarmonyPatch(typeof(MessagingManager), "RpcReader___Server_SendPlayerMessage_1952281135")]
    [HarmonyPrefix]
    private static bool SupplierMeetupMessageReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        int sendableIndex = PooledReader0.ReadInt32();
        _ = PooledReader0.ReadInt32();
        string npcId = PooledReader0.ReadString();
        PooledReader0.Position = originalPosition;

        if (NPCManager.GetNPC(npcId) is not Supplier supplier)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (supplier is Phil phil && serverMod.TryHandleScopedPhilInstructionsRequest(conn, phil, sendableIndex))
        {
            return false;
        }

        if (sendableIndex == 1)
        {
            serverMod.TryHandleSupplierMeetupRequest(conn, supplier);
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(Customer), "OnCustomerUnlocked")]
    [HarmonyPrefix]
    private static void CustomerUnlockedPrefix(Customer __instance)
    {
        OrganisationsServerMod.ActiveInstance?.PrepareCustomerUnlockSideEffects(__instance);
    }

    [HarmonyPatch(typeof(Customer), "OnCustomerUnlocked")]
    [HarmonyPostfix]
    private static void CustomerUnlockedPostfix(Customer __instance)
    {
        OrganisationsServerMod.ActiveInstance?.FinalizeCustomerUnlock(__instance);
    }

    [HarmonyPatch(typeof(NPC), "RpcReader___Server_SendRelationship_431000436")]
    [HarmonyPrefix]
    private static bool NpcRelationshipServerReaderPrefix(NPC __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        Customer? customer = __instance.GetComponent<Customer>();
        if (customer != null)
        {
            OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
            return serverMod == null || serverMod.TryPrepareCustomerRelationshipMutation(conn, customer);
        }

        if (__instance is Supplier supplier)
        {
            OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
            return serverMod == null || serverMod.TryPrepareSupplierMutation(conn, supplier);
        }

        return true;
    }

    [HarmonyPatch(typeof(NPC), "RpcReader___Server_SendRelationship_431000436")]
    [HarmonyPostfix]
    private static void NpcRelationshipServerReaderPostfix(NPC __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;
        if (conn == null || conn.IsLocalClient)
        {
            return;
        }

        Customer? customer = __instance.GetComponent<Customer>();
        if (customer != null)
        {
            OrganisationsServerMod.ActiveInstance?.RecordScopedCustomerRelationship(conn, customer, __instance.RelationData.RelationDelta);
            return;
        }

        if (__instance is Supplier supplier)
        {
            OrganisationsServerMod.ActiveInstance?.RecordScopedSupplierState(conn, supplier);
        }
    }

    private static bool CanAccessDealerInventory(NetworkConnection conn, Dealer dealer)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.CanAccessDealerInventory(conn, dealer);
    }

    private static void RecordDealerInventory(NetworkConnection conn, Dealer dealer)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.RecordScopedDealerInventory(conn, dealer);
    }

    private static bool PrepareDealerInventoryMutation(Dealer dealer, bool includeCash = false)
    {
        if (dealer == null || string.IsNullOrWhiteSpace(dealer.ID))
        {
            return true;
        }

        if (DealerInventoryMutationDepthByNpcId.TryGetValue(dealer.ID, out int depth) && depth > 0)
        {
            DealerInventoryMutationDepthByNpcId[dealer.ID] = depth + 1;
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod != null)
        {
            bool prepared = includeCash
                ? serverMod.TryPrepareDealerStateMutation(dealer)
                : serverMod.TryPrepareDealerInventoryMutation(dealer);
            if (!prepared)
            {
                return false;
            }
        }

        DealerInventoryMutationDepthByNpcId[dealer.ID] = 1;
        return true;
    }

    private static void RecordDealerInventory(Dealer dealer, bool includeCash = false)
    {
        if (dealer == null || string.IsNullOrWhiteSpace(dealer.ID))
        {
            return;
        }

        if (!DealerInventoryMutationDepthByNpcId.TryGetValue(dealer.ID, out int depth))
        {
            return;
        }

        if (depth <= 1)
        {
            DealerInventoryMutationDepthByNpcId.Remove(dealer.ID);
            if (includeCash)
            {
                OrganisationsServerMod.ActiveInstance?.RecordScopedDealerState(dealer);
            }
            else
            {
                OrganisationsServerMod.ActiveInstance?.RecordScopedDealerInventory(dealer);
            }
            return;
        }

        DealerInventoryMutationDepthByNpcId[dealer.ID] = depth - 1;
    }

    private static bool PrepareCustomerStateMutation(NetworkConnection conn, Customer customer)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryPrepareCustomerStateMutation(conn, customer))
        {
            return false;
        }

        string mutationKey = BuildCustomerStateMutationKey(conn, customer);
        if (!string.IsNullOrWhiteSpace(mutationKey))
        {
            PreparedCustomerStateMutationKeys.Add(mutationKey);
        }

        return true;
    }

    private static void RecordCustomerStateMutation(NetworkConnection conn, Customer customer)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return;
        }

        string mutationKey = BuildCustomerStateMutationKey(conn, customer);
        if (string.IsNullOrWhiteSpace(mutationKey) || !PreparedCustomerStateMutationKeys.Remove(mutationKey))
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.RecordScopedCustomerState(conn, customer);
    }

    private static string BuildCustomerStateMutationKey(NetworkConnection conn, Customer customer)
    {
        if (conn == null || customer?.NPC == null || IsEmptyGuid(customer.NPC.GUID))
        {
            return string.Empty;
        }

        return conn.ClientId.ToString() + "|" + customer.NPC.GUID;
    }

    private static bool TryFindCustomer(NPC? npc, out Customer? customer)
    {
        customer = null;
        if (npc == null)
        {
            return false;
        }

        if (TryFindCustomerInList(Customer.UnlockedCustomers.AsManagedEnumerable(), npc, out customer))
        {
            return true;
        }

        return TryFindCustomerInList(Customer.LockedCustomers.AsManagedEnumerable(), npc, out customer);
    }

    private static bool TryFindCustomerInList(IEnumerable<Customer> customers, NPC npc, out Customer? customer)
    {
        customer = null;
        if (customers == null)
        {
            return false;
        }

        foreach (Customer candidate in customers)
        {
            if (candidate?.NPC == npc)
            {
                customer = candidate;
                return true;
            }
        }

        return false;
    }

}
#endif
