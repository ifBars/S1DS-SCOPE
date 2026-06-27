#if SERVER
using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using DedicatedServerMod.Organisations.Utils;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.Graffiti;
using Il2CppScheduleOne.Map;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.NPCs.CharacterClasses;
using Il2CppScheduleOne.NPCs.Behaviour;
using Il2CppScheduleOne.NPCs.Relation;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Quests;
using Il2CppScheduleOne.Variables;
using CharacterRay = Il2CppScheduleOne.NPCs.CharacterClasses.Ray;
using ECartelStatus = Il2Cpp.ECartelStatus;
#else
using FishNet.Connection;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.DevUtilities;
using ScheduleOne.Cartel;
using ScheduleOne.Economy;
using ScheduleOne.Employees;
using ScheduleOne.Graffiti;
using ScheduleOne.Map;
using ScheduleOne.Money;
using ScheduleOne.NPCs.CharacterClasses;
using ScheduleOne.NPCs.Behaviour;
using ScheduleOne.NPCs.Relation;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Property;
using ScheduleOne.Quests;
using ScheduleOne.Variables;
using CharacterRay = ScheduleOne.NPCs.CharacterClasses.Ray;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationQuestRuntimePatches
{
    private static readonly FieldInfo? GearingUpSetCollectionPositionField = AccessTools.Field(typeof(Quest_GearingUp), "setCollectionPosition");
    private static readonly FieldInfo? GraffitiBehaviourSpraySurfaceField = AccessTools.Field(typeof(GraffitiBehaviour), "_spraySurface");
    private static readonly MethodInfo? SinkOrSwimSpawnLoanSharkVehicleMethod = AccessTools.Method(typeof(Quest_SinkOrSwim), "SpawnLoanSharkVehicle");
    private static EMapRegion _pendingGlobalCartelActivityEndRegion;
    private static bool _pendingGlobalCartelActivityEnd;

    [HarmonyPatch(typeof(Quest), nameof(Quest.SetQuestState))]
    [HarmonyPostfix]
    private static void QuestStatePostfix(Quest __instance, EQuestState state)
    {
        if (!QuestScopeRules.ShouldVirtualizeQuest(__instance))
        {
            return;
        }

        if (__instance is DeaddropQuest deaddropQuest)
        {
            OrganisationsServerMod.ActiveInstance?.NotifyDeaddropStateMutation(deaddropQuest, state);
            return;
        }

        OrganisationsServerMod.ActiveInstance?.NotifyQuestWorldMutation($"Quest state changed: {__instance.GUID}");
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.SetQuestEntryState))]
    [HarmonyPostfix]
    private static void QuestEntryStatePostfix(Quest __instance)
    {
        if (!QuestScopeRules.ShouldVirtualizeQuest(__instance))
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.NotifyQuestWorldMutation($"Quest entry changed: {__instance.GUID}");
    }

    [HarmonyPatch(typeof(Quest), nameof(Quest.SetIsTracked))]
    [HarmonyPostfix]
    private static void QuestTrackedPostfix(Quest __instance)
    {
        if (!QuestScopeRules.ShouldVirtualizeQuest(__instance))
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.NotifyQuestWorldMutation($"Quest tracked changed: {__instance.GUID}");
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.ContractAccepted))]
    [HarmonyPostfix]
    private static void ContractAcceptedPostfix()
    {
        OrganisationsServerMod.ActiveInstance?.NotifyQuestWorldMutation("Contract accepted");
    }

    [HarmonyPatch(typeof(QuestManager), nameof(QuestManager.CreateDeaddropCollectionQuest), new[] { typeof(string), typeof(string) })]
    [HarmonyPostfix]
    private static void DeaddropCreatedPostfix(DeaddropQuest __result)
    {
        if (__result != null)
        {
            OrganisationsServerMod.ActiveInstance?.NotifyDeaddropCreated(__result);
            return;
        }

        OrganisationsServerMod.ActiveInstance?.NotifyQuestWorldMutation("Deaddrop quest created");
    }

    [HarmonyPatch(typeof(DeaddropQuest), "OnUncappedMinPass")]
    [HarmonyPrefix]
    private static bool DeaddropQuestOnUncappedMinPassPrefix(DeaddropQuest __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedDeaddropMinuteCompletion(__instance);
    }

    [HarmonyPatch(typeof(Quest_MovingUp), "OnUncappedMinPass")]
    [HarmonyPrefix]
    private static bool MovingUpOnUncappedMinPassPrefix(Quest_MovingUp __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryGetHydratedOwnerUnlockedCustomerCount(out int customerCount))
        {
            return false;
        }

        if (__instance.ReachCustomersEntry.State == EQuestState.Active)
        {
            ApplyReachCustomerEntryProgress(__instance.ReachCustomersEntry, "Unlock 10 customers", customerCount);
        }

        return false;
    }

    [HarmonyPatch(typeof(Quest_ExpandingOperations), "OnUncappedMinPass")]
    [HarmonyPrefix]
    private static bool ExpandingOperationsOnUncappedMinPassPrefix(Quest_ExpandingOperations __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryGetHydratedOwnerUnlockedCustomerCount(out int customerCount))
        {
            return false;
        }

        if (__instance.State == EQuestState.Active)
        {
            int growTentCount = serverMod.TryGetHydratedOwnerSweatshopPotCount(out int scopedSweatshopPots)
                ? Mathf.Clamp(scopedSweatshopPots - 2, 0, 2)
                : Mathf.Clamp(Mathf.RoundToInt(NetworkSingleton<VariableDatabase>.Instance.GetValue<float>("Sweatshop_Pots")) - 2, 0, 2);
            __instance.SetUpGrowTentsEntry.SetEntryTitle("Set up 2 more grow tents (" + growTentCount + "/2)");
            if (growTentCount >= 2 && __instance.SetUpGrowTentsEntry.State != EQuestState.Completed)
            {
                __instance.SetUpGrowTentsEntry.Complete();
            }

            ApplyReachCustomerEntryProgress(__instance.ReachCustomersEntry, "Reach 10 customers", customerCount);
        }

        return false;
    }

    private static void ApplyReachCustomerEntryProgress(QuestEntry entry, string title, int customerCount)
    {
        entry.SetEntryTitle(title + " (" + customerCount + "/10)");
        if (customerCount >= 10 && entry.State != EQuestState.Completed)
        {
            entry.Complete();
        }
    }

    [HarmonyPatch(typeof(Quest_Connections), nameof(Quest_Connections.Begin))]
    [HarmonyPrefix]
    private static bool ConnectionsBeginPrefix(Quest_Connections __instance, bool network)
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

        if (__instance.State == EQuestState.Active)
        {
            return false;
        }

        __instance.SetQuestState(EQuestState.Active, network: false);
        foreach (QuestEntry entry in __instance.Entries)
        {
            NPCUnlockTracker tracker = entry.GetComponent<NPCUnlockTracker>();
            if (tracker?.Npc == null || !serverMod.TryIsHydratedOwnerNpcUnlocked(tracker.Npc, out bool isUnlocked))
            {
                entry.SetState(EQuestState.Active);
                continue;
            }

            entry.SetState(isUnlocked ? EQuestState.Completed : EQuestState.Active);
        }

        if (__instance.TrackOnBegin)
        {
            __instance.SetIsTracked(tracked: true);
        }

        __instance.UpdateHUDUI();
        if (network)
        {
            NetworkSingleton<QuestManager>.Instance.SendQuestAction(__instance.GUID.ToString(), QuestManager.EQuestAction.Begin);
        }

        __instance.onQuestBegin?.Invoke();
        return false;
    }

    [HarmonyPatch(typeof(Quest_OnTheGrind), "OnUncappedMinPass")]
    [HarmonyPrefix]
    private static bool OnTheGrindOnUncappedMinPassPrefix(Quest_OnTheGrind __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryGetHydratedOwnerCompletedContractCount(out int completedContractCount))
        {
            return false;
        }

        if (__instance.CompleteDealsEntry.State == EQuestState.Active)
        {
            __instance.CompleteDealsEntry.SetEntryTitle("Complete 3 deals (" + completedContractCount + "/3)");
        }

        return false;
    }

    [HarmonyPatch(typeof(Quest_GearingUp), "OnUncappedMinPass")]
    [HarmonyPrefix]
    private static bool GearingUpOnUncappedMinPassPrefix(Quest_GearingUp __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryGetHydratedOwnerSupplierDeadDropState(__instance.Supplier, out bool deadDropPreparing, out int minsUntilDeadDropReady))
        {
            return false;
        }

        if (__instance.CollectDropEntry.State == EQuestState.Active
            && !GetGearingUpCollectionPositionSet(__instance)
            && serverMod.TryGetHydratedOwnerActiveDeaddropPosition(out Vector3 dropPosition))
        {
            SetGearingUpCollectionPosition(__instance, true);
            __instance.CollectDropEntry.SetPoILocation(dropPosition);
        }

        if (__instance.WaitForDropEntry.State == EQuestState.Active)
        {
            int remainingMins = deadDropPreparing ? minsUntilDeadDropReady : -1;
            if (remainingMins > 0)
            {
                __instance.WaitForDropEntry.SetEntryTitle("Wait for the dead drop (" + remainingMins + " mins)");
            }
            else
            {
                __instance.WaitForDropEntry.SetEntryTitle("Wait for the dead drop");
            }
        }

        return false;
    }

    [HarmonyPatch(typeof(Quest_GearingUp), "DropReady")]
    [HarmonyPrefix]
    private static bool GearingUpDropReadyPrefix(Quest_GearingUp __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryGetHydratedOwnerSupplierDeadDropState(__instance.Supplier, out bool deadDropPreparing, out int minsUntilDeadDropReady))
        {
            return false;
        }

        return deadDropPreparing && minsUntilDeadDropReady <= 0;
    }

    private static bool GetGearingUpCollectionPositionSet(Quest_GearingUp quest)
    {
        return GearingUpSetCollectionPositionField?.GetValue(quest) is bool value && value;
    }

    private static void SetGearingUpCollectionPosition(Quest_GearingUp quest, bool value)
    {
        GearingUpSetCollectionPositionField?.SetValue(quest, value);
    }

    [HarmonyPatch(typeof(Quest_WeNeedToCook), "OnUncappedMinPass")]
    [HarmonyPrefix]
    private static bool WeNeedToCookOnUncappedMinPassPrefix(Quest_WeNeedToCook __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryIsHydratedOwnerSupplierUnlocked(__instance.MethSupplier, out bool isUnlocked))
        {
            return false;
        }

        if (__instance.State != EQuestState.Inactive || !isUnlocked)
        {
            return false;
        }

        foreach (Quest prerequisite in __instance.PrerequisiteQuests)
        {
            if (prerequisite.State != EQuestState.Completed)
            {
                return false;
            }
        }

        __instance.Begin();
        return false;
    }

    [HarmonyPatch(typeof(Quest_GrowShrooms), "SupplierUnlocked")]
    [HarmonyPrefix]
    private static bool GrowShroomsSupplierUnlockedPrefix(Quest_GrowShrooms __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryIsHydratedOwnerSupplierUnlocked(__instance.ShroomSupplier, out bool isUnlocked))
        {
            return false;
        }

        return isUnlocked;
    }

    [HarmonyPatch(typeof(Quest_CleanCash), "OnUncappedMinPass")]
    [HarmonyPrefix]
    private static bool CleanCashOnUncappedMinPassPrefix(Quest_CleanCash __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryGetHydratedOwnerWeeklyDepositProgress(out float weeklyDepositSum, out float weeklyDepositLimit))
        {
            return false;
        }

        if (__instance.State == EQuestState.Inactive && weeklyDepositSum >= weeklyDepositLimit)
        {
            __instance.Begin();
        }

        if (__instance.State == EQuestState.Completed)
        {
            return false;
        }

        bool hasScopedBusiness = serverMod.TryGetHydratedOwnerFirstBusiness(out Business? business);
        if (__instance.BuyBusinessEntry.State == EQuestState.Active && hasScopedBusiness)
        {
            __instance.BuyBusinessEntry.Complete();
        }

        if (__instance.GoToBusinessEntry.State == EQuestState.Active)
        {
            if (business?.PoI != null)
            {
                __instance.GoToBusinessEntry.transform.position = business.PoI.transform.position;
            }

            if (Player.Local?.CurrentBusiness != null && serverMod.CanHydratedOwnerAccessProperty(Player.Local.CurrentBusiness))
            {
                __instance.GoToBusinessEntry.Complete();
            }
        }

        return false;
    }

    [HarmonyPatch(typeof(Quest_DownToBusiness), "DayPass")]
    [HarmonyPrefix]
    private static bool DownToBusinessDayPassPrefix(Quest_DownToBusiness __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        serverMod.AdvanceScopedTutorialDayForCompletedQuest(__instance);
        return false;
    }

    [HarmonyPatch(typeof(Quest_SinkOrSwim), "CheckArrival")]
    [HarmonyPrefix]
    private static bool SinkOrSwimCheckArrivalPrefix(Quest_SinkOrSwim __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (serverMod.TryHandleScopedLoanSharkArrival(__instance))
        {
            SinkOrSwimSpawnLoanSharkVehicleMethod?.Invoke(__instance, Array.Empty<object>());
            __instance.LoanSharkGraves?.gameObject.SetActive(true);
        }

        return false;
    }

    [HarmonyPatch(typeof(Quest_TheDeepEnd), "HourPass")]
    [HarmonyPrefix]
    private static bool TheDeepEndHourPassPrefix(Quest_TheDeepEnd __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        serverMod.AdvanceScopedLoanSharkHours(__instance);
        return false;
    }

    [HarmonyPatch(typeof(Quest_UnfavourableAgreements), "CheckQuestStart")]
    [HarmonyPrefix]
    private static bool UnfavourableAgreementsCheckQuestStartPrefix(Quest_UnfavourableAgreements __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (!serverMod.TryGetHydratedOwnerCustomerCountsByRegion(EMapRegion.Westville, out int unlockedWestvilleCustomers, out int totalWestvilleCustomers))
        {
            return false;
        }

        if (__instance.State != EQuestState.Inactive || NetworkSingleton<Cartel>.Instance.Status != ECartelStatus.Unknown)
        {
            return false;
        }

        if ((unlockedWestvilleCustomers >= 5 && __instance.PrereqQuest.State == EQuestState.Completed)
            || (totalWestvilleCustomers > 0 && unlockedWestvilleCustomers >= totalWestvilleCustomers))
        {
            __instance.Begin();
        }

        return false;
    }

    [HarmonyPatch(typeof(Quest_UnfavourableAgreements), nameof(Quest_UnfavourableAgreements.Begin))]
    [HarmonyPostfix]
    private static void UnfavourableAgreementsBeginPostfix(Quest_UnfavourableAgreements __instance)
    {
        OrganisationsServerMod.ActiveInstance?.CompleteScopedUnfavourableAgreementsIntro(__instance);
    }

    [HarmonyPatch(typeof(Thomas), nameof(Thomas.SendIntroMessage))]
    [HarmonyPrefix]
    private static bool ThomasSendIntroMessagePrefix(Thomas __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedThomasIntroMessage(__instance);
    }

    [HarmonyPatch(typeof(Quest_DefeatCartel), nameof(Quest_DefeatCartel.SetQuestState))]
    [HarmonyPrefix]
    private static void DefeatCartelSetQuestStatePrefix(EQuestState state)
    {
        if (state == EQuestState.Completed)
        {
            OrganisationsServerMod.ActiveInstance?.BeginScopedCartelDefeatStatusMutation();
        }
    }

    [HarmonyPatch(typeof(Quest_DefeatCartel), nameof(Quest_DefeatCartel.SetQuestState))]
    [HarmonyFinalizer]
    private static void DefeatCartelSetQuestStateFinalizer(EQuestState state)
    {
        if (state == EQuestState.Completed)
        {
            OrganisationsServerMod.ActiveInstance?.EndScopedCartelDefeatStatusMutation();
        }
    }

    [HarmonyPatch(typeof(Cartel), nameof(Cartel.SetStatus))]
    [HarmonyPrefix]
    private static bool CartelSetStatusPrefix(ECartelStatus newStatus)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.ShouldAllowCartelStatusMutation(newStatus);
    }

    [HarmonyPatch(typeof(Cartel), "HourPass")]
    [HarmonyPostfix]
    private static void CartelHourPassPostfix()
    {
        OrganisationsServerMod.ActiveInstance?.AdvanceCartelStatusHours();
    }

    [HarmonyPatch(typeof(CartelInfluence), "RpcLogic___SetInfluence_2071772313")]
    [HarmonyPostfix]
    private static void CartelSetInfluencePostfix(EMapRegion region, float influence)
    {
        OrganisationsServerMod.ActiveInstance?.RecordHydratedCartelInfluence(region, influence);
    }

    [HarmonyPatch(typeof(CartelInfluence), "RpcReader___Server_ChangeInfluence_2792544924")]
    [HarmonyPrefix]
    private static void CartelChangeInfluenceReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;

        OrganisationsServerMod.ActiveInstance?.PrepareCallerCartelInfluenceMutation(conn);
    }

    [HarmonyPatch(typeof(CartelInfluence), "RpcLogic___ChangeInfluence_2792544924")]
    [HarmonyPrefix]
    private static bool CartelChangeInfluenceLogicPrefix(EMapRegion region, float amount)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryPrepareCartelInfluenceMutation(region, amount);
    }

    [HarmonyPatch(typeof(CartelInfluence), "RpcLogic___ChangeInfluence_1267088319")]
    [HarmonyPostfix]
    private static void CartelChangeInfluencePostfix(EMapRegion region, float newInfluence)
    {
        OrganisationsServerMod.ActiveInstance?.RecordHydratedCartelInfluence(region, newInfluence);
    }

    [HarmonyPatch(typeof(CartelActivities), "HourPass")]
    [HarmonyPostfix]
    private static void CartelActivitiesHourPassPostfix(CartelActivities __instance)
    {
        CartelActivity? activity = __instance.CurrentGlobalActivity;
        if (activity != null)
        {
            OrganisationsServerMod.ActiveInstance?.RecordStrictCartelActivityState(activity.Region);
            return;
        }

        OrganisationsServerMod.ActiveInstance?.AdvanceCartelGlobalActivityCooldowns();
    }

    [HarmonyPatch(typeof(CartelActivities), "GetValidRegionsForActivity")]
    [HarmonyPrefix]
    private static bool CartelActivitiesGetValidRegionsPrefix(ref List<EMapRegion> __result)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return true;
        }

        if (serverMod.TryGetScopedGlobalCartelActivityRegions(out List<EMapRegion> regions))
        {
            __result = regions;
            return false;
        }

        return true;
    }

    [HarmonyPatch(typeof(CartelActivities), "TryStartActivity")]
    [HarmonyPrefix]
    private static bool CartelActivitiesTryStartActivityPrefix(CartelActivities __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryStartScopedGlobalCartelActivity(__instance);
    }

    [HarmonyPatch(typeof(CartelActivities), "RpcLogic___StartGlobalActivity_1796582335")]
    [HarmonyPrefix]
    private static bool CartelStartGlobalActivityPrefix(EMapRegion region)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.ShouldStartCartelGlobalActivity(region);
    }

    [HarmonyPatch(typeof(CartelActivities), "RpcLogic___StartGlobalActivity_1796582335")]
    [HarmonyPostfix]
    private static void CartelStartGlobalActivityPostfix(CartelActivities __instance, EMapRegion region)
    {
        if (__instance.CurrentGlobalActivity != null)
        {
            OrganisationsServerMod.ActiveInstance?.RecordHydratedCartelGlobalActivityStarted(region, __instance.CurrentGlobalActivity);
        }
    }

    [HarmonyPatch(typeof(CartelActivities), "ActivityEnded")]
    [HarmonyPrefix]
    private static void CartelGlobalActivityEndedPrefix(CartelActivities __instance)
    {
        CartelActivity? activity = __instance.CurrentGlobalActivity;
        if (activity != null)
        {
            _pendingGlobalCartelActivityEndRegion = activity.Region;
            _pendingGlobalCartelActivityEnd = true;
        }
    }

    [HarmonyPatch(typeof(CartelActivities), "ActivityEnded")]
    [HarmonyPostfix]
    private static void CartelGlobalActivityEndedPostfix()
    {
        if (!_pendingGlobalCartelActivityEnd)
        {
            return;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        serverMod?.RecordStrictCartelActivityState(_pendingGlobalCartelActivityEndRegion);
        serverMod?.ClearCartelGlobalActivityOwner(_pendingGlobalCartelActivityEndRegion);
        _pendingGlobalCartelActivityEnd = false;
    }

    [HarmonyPatch(typeof(CartelRegionActivities), "HourPass")]
    [HarmonyPostfix]
    private static void CartelRegionActivitiesHourPassPostfix(CartelRegionActivities __instance)
    {
        if (__instance.CurrentActivity != null)
        {
            OrganisationsServerMod.ActiveInstance?.RecordStrictCartelActivityState(__instance.Region);
            return;
        }

        OrganisationsServerMod.ActiveInstance?.AdvanceCartelRegionalActivityCooldowns(__instance.Region);
    }

    [HarmonyPatch(typeof(CartelRegionActivities), "RpcLogic___StartActivity_2681120339")]
    [HarmonyPrefix]
    private static bool CartelStartRegionActivityPrefix(CartelRegionActivities __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.ShouldStartCartelRegionalActivity(__instance.Region);
    }

    [HarmonyPatch(typeof(CartelRegionActivities), "TryStartActivity")]
    [HarmonyPrefix]
    private static bool CartelRegionActivitiesTryStartActivityPrefix(CartelRegionActivities __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryStartScopedRegionalCartelActivity(__instance);
    }

    [HarmonyPatch(typeof(CartelRegionActivities), "RpcLogic___StartActivity_2681120339")]
    [HarmonyPostfix]
    private static void CartelStartRegionActivityPostfix(CartelRegionActivities __instance)
    {
        if (__instance.CurrentActivity != null)
        {
            OrganisationsServerMod.ActiveInstance?.RecordHydratedCartelRegionalActivityStarted(__instance.Region, __instance.CurrentActivity);
        }
    }

    [HarmonyPatch(typeof(CartelRegionActivities), "ActivityEnded")]
    [HarmonyPostfix]
    private static void CartelRegionActivityEndedPostfix(CartelRegionActivities __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        serverMod?.RecordStrictCartelActivityState(__instance.Region);
        serverMod?.ClearCartelRegionalActivityOwner(__instance.Region);
    }

    [HarmonyPatch(typeof(CartelDealer), "DiedOrKnockedOut")]
    [HarmonyPrefix]
    private static void CartelDealerDiedOrKnockedOutPrefix(CartelDealer __instance)
    {
        OrganisationsServerMod.ActiveInstance?.PrepareCartelDealerDefeatSideEffects(__instance);
    }

    [HarmonyPatch(typeof(CartelDealer), "ConfigureGoonSettings")]
    [HarmonyPrefix]
    private static bool CartelDealerConfigureSettingsPrefix(CartelDealer __instance, NetworkConnection conn, CartelGoonAppearance appearance, float moveSpeed)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null
            || (!serverMod.TryHandleCartelDealerConfigureReplay(__instance, conn, appearance, moveSpeed)
                && serverMod.ShouldReplayCartelDealerToConnection(__instance, conn));
    }

    [HarmonyPatch(typeof(Ambush), "MinPassed")]
    [HarmonyPrefix]
    private static bool CartelAmbushMinPassedPrefix(Ambush __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.TryHandleScopedTimedCartelAmbush(__instance);
    }

    [HarmonyPatch(typeof(CartelGoon), nameof(CartelGoon.Spawn))]
    [HarmonyPrefix]
    private static void CartelGoonSpawnPrefix(CartelGoon __instance)
    {
        OrganisationsServerMod.ActiveInstance?.PrepareCartelGoonSpawn(__instance);
    }

    [HarmonyPatch(typeof(CartelGoon), "Spawn_Client")]
    [HarmonyPrefix]
    private static bool CartelGoonSpawnClientPrefix(CartelGoon __instance, NetworkConnection conn)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null
            || (!serverMod.TryHandleCartelGoonSpawnReplay(__instance, conn)
                && serverMod.ShouldReplayCartelGoonToConnection(__instance, conn));
    }

    [HarmonyPatch(typeof(CartelGoon), "ConfigureGoonSettings")]
    [HarmonyPrefix]
    private static bool CartelGoonConfigureSettingsPrefix(CartelGoon __instance, NetworkConnection conn, CartelGoonAppearance appearance, float moveSpeed)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null
            || (!serverMod.TryHandleCartelGoonConfigureReplay(__instance, conn, appearance, moveSpeed)
                && serverMod.ShouldReplayCartelGoonToConnection(__instance, conn));
    }

    [HarmonyPatch(typeof(SprayGraffiti), nameof(SprayGraffiti.SetSpraySurface))]
    [HarmonyPrefix]
    private static bool SprayGraffitiSetSpraySurfacePrefix(SprayGraffiti __instance, EMapRegion region, bool overrideExisting)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedSprayGraffitiSurfaceSelection(__instance, region, overrideExisting);
    }

    [HarmonyPatch(typeof(SprayGraffiti), nameof(SprayGraffiti.Activate))]
    [HarmonyPrefix]
    private static void SprayGraffitiActivatePrefix(EMapRegion region)
    {
        OrganisationsServerMod.ActiveInstance?.BeginScopedCartelGoonSpawnForActivity(region);
    }

    [HarmonyPatch(typeof(SprayGraffiti), nameof(SprayGraffiti.Activate))]
    [HarmonyFinalizer]
    private static void SprayGraffitiActivateFinalizer()
    {
        OrganisationsServerMod.ActiveInstance?.EndScopedCartelGoonSpawnForActivity();
    }

    [HarmonyPatch(typeof(SpraySurface), "RpcReader___Server_AddStrokes_Server_1511871282")]
    [HarmonyPrefix]
    private static void SpraySurfaceAddStrokesReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;

        OrganisationsServerMod.ActiveInstance?.PrepareCartelGraffitiMutation(conn);
    }

    [HarmonyPatch(typeof(SpraySurface), "RpcLogic___AddStrokes_Server_1511871282")]
    [HarmonyPostfix]
    private static void SpraySurfaceAddStrokesPostfix(SpraySurface __instance)
    {
        RecordCartelGraffitiSurface(__instance);
    }

    [HarmonyPatch(typeof(SpraySurface), "RpcReader___Server_Undo_Server_3316948804")]
    [HarmonyPrefix]
    private static void SpraySurfaceUndoReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;

        OrganisationsServerMod.ActiveInstance?.PrepareCartelGraffitiMutation(conn);
    }

    [HarmonyPatch(typeof(SpraySurface), "RpcLogic___Undo_Server_3316948804")]
    [HarmonyPostfix]
    private static void SpraySurfaceUndoPostfix(SpraySurface __instance)
    {
        RecordCartelGraffitiSurface(__instance);
    }

    [HarmonyPatch(typeof(SpraySurface), "RpcReader___Server_ClearDrawing_2166136261")]
    [HarmonyPrefix]
    private static void SpraySurfaceClearDrawingReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;

        OrganisationsServerMod.ActiveInstance?.PrepareCartelGraffitiMutation(conn);
    }

    [HarmonyPatch(typeof(SpraySurface), "RpcLogic___ClearDrawing_2166136261")]
    [HarmonyPostfix]
    private static void SpraySurfaceClearDrawingPostfix(SpraySurface __instance)
    {
        RecordCartelGraffitiSurface(__instance);
    }

    [HarmonyPatch(typeof(WorldSpraySurface), "RpcReader___Server_MarkDrawingFinalized_2166136261")]
    [HarmonyPrefix]
    private static void WorldSpraySurfaceFinalizedReaderPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = PooledReader0;
        _ = channel;

        OrganisationsServerMod.ActiveInstance?.PrepareCartelGraffitiMutation(conn);
    }

    [HarmonyPatch(typeof(GraffitiBehaviour), "RpcLogic___Complete_Server_2166136261")]
    [HarmonyPrefix]
    private static void GraffitiBehaviourCompletePrefix(GraffitiBehaviour __instance)
    {
        if (TryGetGraffitiBehaviourSurface(__instance, out WorldSpraySurface surface))
        {
            OrganisationsServerMod.ActiveInstance?.PrepareCartelGraffitiMutation(surface);
        }
    }

    [HarmonyPatch(typeof(GraffitiBehaviour), nameof(GraffitiBehaviour.Disable))]
    [HarmonyPrefix]
    private static void GraffitiBehaviourDisablePrefix(GraffitiBehaviour __instance)
    {
        if (TryGetGraffitiBehaviourSurface(__instance, out WorldSpraySurface surface))
        {
            OrganisationsServerMod.ActiveInstance?.PrepareCartelGraffitiMutation(surface);
        }
    }

    [HarmonyPatch(typeof(WorldSpraySurface), "RpcLogic___Set_3759704962")]
    [HarmonyPostfix]
    private static void WorldSpraySurfaceSetPostfix(WorldSpraySurface __instance)
    {
        OrganisationsServerMod.ActiveInstance?.RecordHydratedCartelGraffitiSurface(__instance);
    }

    [HarmonyPatch(typeof(WorldSpraySurface), "RpcLogic___SetFinalized_2166136261")]
    [HarmonyPostfix]
    private static void WorldSpraySurfaceFinalizedPostfix(WorldSpraySurface __instance)
    {
        OrganisationsServerMod.ActiveInstance?.RecordHydratedCartelGraffitiSurface(__instance);
    }

    [HarmonyPatch(typeof(WorldSpraySurface), nameof(WorldSpraySurface.CleanGraffiti))]
    [HarmonyPostfix]
    private static void WorldSpraySurfaceCleanGraffitiPostfix(WorldSpraySurface __instance)
    {
        OrganisationsServerMod.ActiveInstance?.RecordHydratedCartelGraffitiSurface(__instance);
    }

    private static void RecordCartelGraffitiSurface(SpraySurface surface)
    {
        if (surface is WorldSpraySurface worldSpraySurface)
        {
            OrganisationsServerMod.ActiveInstance?.RecordHydratedCartelGraffitiSurface(worldSpraySurface);
        }
    }

    private static bool TryGetGraffitiBehaviourSurface(GraffitiBehaviour behaviour, out WorldSpraySurface surface)
    {
        surface = GraffitiBehaviourSpraySurfaceField?.GetValue(behaviour) as WorldSpraySurface ?? null!;
        return surface != null;
    }

    [HarmonyPatch(typeof(Quest_DefeatCartel), "OnSleepEnd")]
    [HarmonyPrefix]
    private static bool DefeatCartelOnSleepEndPrefix(Quest_DefeatCartel __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null
            || __instance.State != EQuestState.Inactive)
        {
            return true;
        }

        if (!serverMod.TryGetHydratedOwnerCartelStatus(out string cartelStatus))
        {
            return false;
        }

        return !string.Equals(cartelStatus, ECartelStatus.Defeated.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [HarmonyPatch(typeof(Sam), nameof(Sam.SendTunnelDugMessage))]
    [HarmonyPrefix]
    private static bool SamSendTunnelDugMessagePrefix(Sam __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedSamTunnelDugMessage(__instance);
    }

    [HarmonyPatch(typeof(CharacterRay), "NotifyPlayerOfManorRebuild")]
    [HarmonyPrefix]
    private static bool RayNotifyPlayerOfManorRebuildPrefix(CharacterRay __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedRayManorRebuildMessage(__instance);
    }

    [HarmonyPatch(typeof(CartelDealManager), "StartDeal")]
    [HarmonyPrefix]
    private static bool CartelStartDealPrefix()
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.ShouldStartCartelDealRequest();
    }

    [HarmonyPatch(typeof(CartelDealManager), "StartDeal")]
    [HarmonyPostfix]
    private static void CartelStartDealPostfix(CartelDealManager __instance)
    {
        OrganisationsServerMod.ActiveInstance?.CaptureGeneratedCartelDeal(__instance);
    }

    [HarmonyPatch(typeof(CartelDealManager), "SendRequestMessage")]
    [HarmonyPrefix]
    private static bool CartelDealRequestMessagePrefix(CartelDealManager __instance, CartelDealInfo dealInfo)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedCartelDealRequestMessage(__instance, dealInfo);
    }

    [HarmonyPatch(typeof(CartelDealManager), "SendOverdueMessage")]
    [HarmonyPrefix]
    private static bool CartelDealOverdueMessagePrefix(CartelDealManager __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedCartelDealOverdueMessage(__instance);
    }

    [HarmonyPatch(typeof(CartelDealManager), "SendExpiryMessage")]
    [HarmonyPrefix]
    private static bool CartelDealExpiryMessagePrefix(CartelDealManager __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleScopedCartelDealExpiryMessage(__instance);
    }

    [HarmonyPatch(typeof(CartelDealManager), "HourPass")]
    [HarmonyPostfix]
    private static void CartelDealHourPassPostfix(CartelDealManager __instance)
    {
        if (__instance.ActiveDeal == null)
        {
            OrganisationsServerMod.ActiveInstance?.AdvanceCartelDealCooldowns();
        }
    }

    [HarmonyPatch(typeof(CartelDealManager), "OnTimeSkip")]
    [HarmonyPostfix]
    private static void CartelDealTimeSkipPostfix(CartelDealManager __instance, int mins)
    {
        if (__instance.ActiveDeal != null || mins <= 0)
        {
            return;
        }

        int skippedHours = Mathf.CeilToInt((float)mins / 60f);
        OrganisationsServerMod.ActiveInstance?.AdvanceCartelDealCooldowns(skippedHours);
    }

    [HarmonyPatch(typeof(CartelDealManager), "MarkDealOverdue")]
    [HarmonyPostfix]
    private static void CartelMarkDealOverduePostfix(CartelDealManager __instance)
    {
        OrganisationsServerMod.ActiveInstance?.CaptureCartelDealOverdue(__instance);
    }

    [HarmonyPatch(typeof(CartelDealManager), "ExpireDeal")]
    [HarmonyPrefix]
    private static bool CartelExpireDealPrefix(CartelDealManager __instance)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.ShouldRunVanillaCartelDealExpiry(__instance);
    }

    [HarmonyPatch(typeof(CartelDealManager), "CompleteDeal")]
    [HarmonyPostfix]
    private static void CartelCompleteDealPostfix(CartelDealManager __instance)
    {
        OrganisationsServerMod.ActiveInstance?.CaptureCompletedCartelDeal(__instance);
    }

    [HarmonyPatch(typeof(CartelDealManager), "DepositCash")]
    [HarmonyPrefix]
    private static bool CartelDepositCashPrefix(CartelDealManager __instance, float amount)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || !serverMod.TryHandleCartelDealCashPayout(__instance, amount);
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.OfferContract))]
    [HarmonyPostfix]
    private static void CustomerOfferContractPostfix()
    {
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.AssignContract))]
    [HarmonyPostfix]
    private static void CustomerAssignContractPostfix()
    {
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.SetIsAwaitingDelivery))]
    [HarmonyPostfix]
    private static void CustomerAwaitingDeliveryPostfix()
    {
    }

    [HarmonyPatch(typeof(Customer), nameof(Customer.CurrentContractEnded))]
    [HarmonyPostfix]
    private static void CustomerCurrentContractEndedPostfix()
    {
    }
}

[HarmonyPatch]
internal static class OrganisationEmployeeQuestGetEmployeesPatch
{
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Quest_Botanists), nameof(Quest_Botanists.GetEmployees));
        yield return AccessTools.Method(typeof(Quest_Chemists), nameof(Quest_Chemists.GetEmployees));
        yield return AccessTools.Method(typeof(Quest_Cleaners), nameof(Quest_Cleaners.GetEmployees));
        yield return AccessTools.Method(typeof(Quest_Packagers), nameof(Quest_Packagers.GetEmployees));
    }

    [HarmonyPostfix]
    private static void Postfix(ref List<Employee> __result)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || __result == null)
        {
            return;
        }

        for (int i = __result.Count - 1; i >= 0; i--)
        {
            if (!serverMod.CanHydratedOwnerAccessEmployee(__result[i]))
            {
                __result.RemoveAt(i);
            }
        }
    }
}
#endif
