#if CLIENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DedicatedServerMod.Organisations.Utils;
using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.Building;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Dialogue;
using Il2CppScheduleOne.Doors;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.Money;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.Tiles;
using Il2CppScheduleOne.Trash;
using Il2CppScheduleOne.UI;
using Il2CppScheduleOne.UI.Phone.Delivery;
using Il2CppScheduleOne.UI.Management;
#else
using ScheduleOne.Building;
using ScheduleOne.Delivery;
using ScheduleOne.DevUtilities;
using ScheduleOne.Dialogue;
using ScheduleOne.Doors;
using ScheduleOne.Economy;
using ScheduleOne.EntityFramework;
using ScheduleOne.Interaction;
using ScheduleOne.Management;
using ScheduleOne.Money;
using ScheduleOne.ObjectScripts;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Property;
using ScheduleOne.Storage;
using ScheduleOne.Tiles;
using ScheduleOne.Trash;
using ScheduleOne.UI;
using ScheduleOne.UI.Phone.Delivery;
using ScheduleOne.UI.Management;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationPropertyClientPatches
{
    [HarmonyPatch(typeof(Property), "RpcReader___Observers_ReceiveOwned_Networked_2166136261")]
    [HarmonyPrefix]
    private static bool PropertyReceiveOwnedPrefix(Property __instance)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null)
        {
            return true;
        }

        return !clientMod.ShouldSuppressVanillaPropertyOwnedBroadcast(__instance);
    }

    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), nameof(DialogueHandler_EstateAgent.ShouldChoiceBeShown))]
    [HarmonyPrefix]
    private static bool EstateAgentShouldChoiceBeShownPrefix(string choiceLabel, ref bool __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null)
        {
            return true;
        }

        if (clientMod.ShouldForceShowEstateAgentChoice(choiceLabel))
        {
            __result = true;
            return false;
        }

        if (!clientMod.ShouldHideEstateAgentChoice(choiceLabel))
        {
            return true;
        }

        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ChoiceCallback")]
    [HarmonyPrefix]
    private static void EstateAgentChoiceCallbackPrefix(DialogueHandler_EstateAgent __instance, string choiceLabel)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null)
        {
            return;
        }

        if (!clientMod.ShouldForceShowEstateAgentChoice(choiceLabel))
        {
            clientMod.ClearPendingUnavailableEstateProperty();
            return;
        }

        Property? property = FindProperty(choiceLabel);
        if (property == null)
        {
            clientMod.ClearPendingUnavailableEstateProperty();
            return;
        }

        AccessTools.Field(typeof(DialogueHandler_EstateAgent), "selectedProperty")?.SetValue(__instance, property);
        clientMod.SetPendingUnavailableEstateProperty(property.PropertyCode);
    }

    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), nameof(DialogueHandler_EstateAgent.CheckChoice))]
    [HarmonyPrefix]
    private static bool EstateAgentCheckChoicePrefix(DialogueHandler_EstateAgent __instance, string choiceLabel, ref string invalidReason, ref bool __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null)
        {
            return true;
        }

        if (string.Equals(choiceLabel, "CONFIRM_BUY", StringComparison.Ordinal))
        {
            Property? selectedProperty = GetSelectedProperty(__instance);
            if (selectedProperty == null || clientMod.TryValidateEstateAgentConfirmation(selectedProperty, out invalidReason))
            {
                return true;
            }

            __result = false;
            return false;
        }

        if (string.Equals(choiceLabel, "CONFIRM_BUY_BUSINESS", StringComparison.Ordinal))
        {
            Business? selectedBusiness = GetSelectedBusiness(__instance);
            if (selectedBusiness == null || clientMod.TryValidateEstateAgentConfirmation(selectedBusiness, out invalidReason))
            {
                return true;
            }

            __result = false;
            return false;
        }

        if (clientMod.TryValidateEstateAgentChoice(choiceLabel, out invalidReason))
        {
            return true;
        }

        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "DialogueCallback")]
    [HarmonyPrefix]
    private static bool EstateAgentDialogueCallbackPrefix(DialogueHandler_EstateAgent __instance, string choiceLabel)
    {
        if (!string.Equals(choiceLabel, "CONFIRM_BUY", StringComparison.Ordinal)
            && !string.Equals(choiceLabel, "CONFIRM_BUY_BUSINESS", StringComparison.Ordinal))
        {
            return true;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null)
        {
            return true;
        }

        Property? selectedProperty = string.Equals(choiceLabel, "CONFIRM_BUY_BUSINESS", StringComparison.Ordinal)
            ? GetSelectedBusiness(__instance)
            : GetSelectedProperty(__instance);
        if (selectedProperty == null || clientMod.TryValidateEstateAgentConfirmation(selectedProperty, out string invalidReason))
        {
            return true;
        }

        clientMod.ShowOrganisationNotification(invalidReason);
        return false;
    }

    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ModifyDialogueText")]
    [HarmonyPostfix]
    private static void EstateAgentModifyDialogueTextPostfix(string dialogueLabel, ref string __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null)
        {
            return;
        }

        __result = clientMod.ModifyEstateAgentDialogueText(dialogueLabel, __result);
    }

    [HarmonyPatch(typeof(DialogueHandler_EstateAgent), "ModifyChoiceText")]
    [HarmonyPostfix]
    private static void EstateAgentModifyChoiceTextPostfix(string choiceLabel, ref string __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null)
        {
            return;
        }

        __result = clientMod.ModifyEstateAgentChoiceText(choiceLabel, __result);
    }

    private static Property? GetSelectedProperty(DialogueHandler_EstateAgent handler)
    {
        return AccessTools.Field(typeof(DialogueHandler_EstateAgent), "selectedProperty")?.GetValue(handler) as Property;
    }

    private static Business? GetSelectedBusiness(DialogueHandler_EstateAgent handler)
    {
        return AccessTools.Field(typeof(DialogueHandler_EstateAgent), "selectedBusiness")?.GetValue(handler) as Business;
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
}

[HarmonyPatch]
internal static class OrganisationMingPropertyClientPatches
{
    [HarmonyPatch(typeof(DialogueController_Ming), "CanBuyRoom")]
    [HarmonyPrefix]
    private static bool MingCanBuyRoomPrefix(DialogueController_Ming __instance, ref bool __result)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance?.Property == null)
        {
            return true;
        }

        if (clientMod.TryValidateCashPropertyPurchase(__instance.Property, __instance.Price, out _))
        {
            return true;
        }

        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(DialogueController_Ming), nameof(DialogueController_Ming.CheckChoice))]
    [HarmonyPrefix]
    private static bool MingCheckChoicePrefix(DialogueController_Ming __instance, string choiceLabel, ref string invalidReason, ref bool __result)
    {
        if (!string.Equals(choiceLabel, "CHOICE_CONFIRM", StringComparison.Ordinal))
        {
            return true;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance?.Property == null)
        {
            return true;
        }

        if (clientMod.TryValidateCashPropertyPurchase(__instance.Property, __instance.Price, out invalidReason))
        {
            return true;
        }

        __result = false;
        return false;
    }

    [HarmonyPatch(typeof(DialogueController_Ming), nameof(DialogueController_Ming.ChoiceCallback))]
    [HarmonyPrefix]
    private static bool MingChoiceCallbackPrefix(DialogueController_Ming __instance, string choiceLabel)
    {
        if (!string.Equals(choiceLabel, "CHOICE_CONFIRM", StringComparison.Ordinal))
        {
            return true;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance?.Property == null)
        {
            return true;
        }

        if (clientMod.TryValidateCashPropertyPurchase(__instance.Property, __instance.Price, out string invalidReason))
        {
            return true;
        }

        clientMod.ShowOrganisationNotification(invalidReason);
        return false;
    }
}

[HarmonyPatch]
internal static class OrganisationLaunderingClientPatches
{
    [HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.OpenAmountSelector))]
    [HarmonyPrefix]
    private static bool LaunderingOpenAmountSelectorPrefix(LaunderingInterface __instance)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance?.business == null)
        {
            return true;
        }

        if (clientMod.TryValidateBusinessOperation(__instance.business, out string invalidReason))
        {
            return true;
        }

        clientMod.ShowOrganisationNotification(invalidReason);
        return false;
    }

    [HarmonyPatch(typeof(LaunderingInterface), nameof(LaunderingInterface.ConfirmAmount))]
    [HarmonyPrefix]
    private static bool LaunderingConfirmAmountPrefix(LaunderingInterface __instance)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null || __instance?.business == null)
        {
            return true;
        }

        int amount = GetSelectedAmount(__instance);
        if (clientMod.TryValidateLaunderingStart(__instance.business, amount, out string invalidReason))
        {
            return true;
        }

        clientMod.ShowOrganisationNotification(invalidReason);
        return false;
    }

    private static int GetSelectedAmount(LaunderingInterface launderingInterface)
    {
        object? value = AccessTools.Field(typeof(LaunderingInterface), "selectedAmountToLaunder")?.GetValue(launderingInterface);
        int selectedAmount = value is int amount ? amount : 0;
        MoneyManager? moneyManager = NetworkSingleton<MoneyManager>.Instance;
        int maxAmount = moneyManager == null
            ? selectedAmount
            : (int)Mathf.Min(launderingInterface.business.appliedLaunderLimit, moneyManager.cashBalance);
        return Mathf.Clamp(selectedAmount, 10, maxAmount);
    }
}

[HarmonyPatch]
internal static class OrganisationMotelDoorClientPatches
{
    [HarmonyTargetMethod]
    private static MethodBase? DoorControllerCanPlayerAccessTargetMethod()
    {
        return AccessTools.Method(typeof(DoorController), "CanPlayerAccess", new[] { typeof(EDoorSide), typeof(string).MakeByRefType() });
    }

    [HarmonyPostfix]
    private static void DoorControllerCanPlayerAccessPostfix(DoorController __instance, EDoorSide side, ref bool __result, ref string reason)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        Property? property = FindPropertyForDoor(__instance);
        if (clientMod == null || property == null)
        {
            return;
        }

        if (side == EDoorSide.Exterior && clientMod.IsPropertyReservedByOtherScope(property.PropertyCode))
        {
            __result = false;
            reason = "Owned by another organisation";
            return;
        }

        if (!__result && clientMod.CanAccessProperty(property.PropertyCode))
        {
            __result = true;
            reason = string.Empty;
        }
    }

    private static Property? FindPropertyForDoor(DoorController doorController)
    {
        return Property.Properties.AsManagedEnumerable().FirstOrDefault(property =>
            property != null
            && !string.IsNullOrWhiteSpace(property.PropertyCode)
            && (doorController.transform.IsChildOf(property.transform)
                || property.DoBoundsContainPoint(doorController.transform.position)));
    }
}

[HarmonyPatch]
internal static class OrganisationPropertyRespawnClientPatches
{
    [HarmonyPatch(typeof(Player), "RecalculateCurrentProperty")]
    [HarmonyPostfix]
    private static void RecalculateCurrentPropertyPostfix(Player __instance)
    {
        if (__instance == null || !__instance.IsOwner)
        {
            return;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        Property? lastVisitedProperty = __instance.LastVisitedProperty;
        if (clientMod == null
            || lastVisitedProperty == null
            || string.IsNullOrWhiteSpace(lastVisitedProperty.PropertyCode)
            || clientMod.CanAccessProperty(lastVisitedProperty.PropertyCode))
        {
            return;
        }

        AccessTools.Field(typeof(Player), "<LastVisitedProperty>k__BackingField")?.SetValue(__instance, null);
    }
}

[HarmonyPatch]
internal static class OrganisationPropertyDeliveryClientPatches
{
    [HarmonyPatch(typeof(DeliveryShop), nameof(DeliveryShop.CanOrder))]
    [HarmonyPostfix]
    private static void CanOrderPostfix(DeliveryShop __instance, ref bool __result, ref string reason)
    {
        if (!__result)
        {
            return;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        Property? destination = AccessTools.Field(typeof(DeliveryShop), "destinationProperty")?.GetValue(__instance) as Property;
        if (clientMod == null
            || destination == null
            || string.IsNullOrWhiteSpace(destination.PropertyCode)
            || clientMod.CanAccessProperty(destination.PropertyCode))
        {
            return;
        }

        __result = false;
        reason = "Select a property owned by your organisation";
    }

    [HarmonyPatch(typeof(DeliveryManager), "RpcLogic___ReceiveDelivery_2795369214")]
    [HarmonyPrefix]
    private static bool ReceiveDeliveryPrefix(DeliveryInstance delivery)
    {
        return CanAccessDelivery(delivery);
    }

    [HarmonyPatch(typeof(DeliveryManager), "RpcLogic___SetDeliveryState_316609003")]
    [HarmonyPrefix]
    private static bool SetDeliveryStatePrefix(DeliveryManager __instance, string deliveryID)
    {
        if (__instance?.Deliveries == null)
        {
            return true;
        }

        foreach (DeliveryInstance delivery in __instance.Deliveries)
        {
            if (delivery != null
                && string.Equals(delivery.DeliveryID, deliveryID, StringComparison.OrdinalIgnoreCase))
            {
                return CanAccessDelivery(delivery);
            }
        }

        return true;
    }

    private static bool CanAccessDelivery(DeliveryInstance delivery)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod == null
            || delivery == null
            || string.IsNullOrWhiteSpace(delivery.DestinationCode))
        {
            return true;
        }

        return clientMod.CanAccessProperty(delivery.DestinationCode);
    }
}

[HarmonyPatch]
internal static class OrganisationPropertyAffordanceClientPatches
{
    [HarmonyPatch(typeof(StorageEntity), nameof(StorageEntity.CanBeOpened))]
    [HarmonyPostfix]
    private static void StorageCanBeOpenedPostfix(StorageEntity __instance, ref bool __result)
    {
        if (!__result)
        {
            return;
        }

        DeadDrop? deaddrop = FindDeaddropForStorage(__instance);
        if (deaddrop != null)
        {
            OrganisationsClientMod? activeClientMod = OrganisationsClientMod.ActiveInstance;
            if (activeClientMod != null && !activeClientMod.CanAccessDeaddrop(deaddrop.GUID.ToString()))
            {
                __result = false;
            }

            return;
        }

        Property? property = FindPropertyForStorage(__instance);
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod != null
            && property != null
            && !string.IsNullOrWhiteSpace(property.PropertyCode)
            && !clientMod.CanAccessProperty(property.PropertyCode))
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(DeadDrop), "UpdateDeadDrop")]
    [HarmonyPostfix]
    private static void DeadDropUpdateDeadDropPostfix(DeadDrop __instance)
    {
        OrganisationsClientMod.ActiveInstance?.RefreshScopedDeaddropAffordance(__instance);
    }

    [HarmonyPatch(typeof(BuildableItem), nameof(BuildableItem.CanBeDestroyed))]
    [HarmonyPostfix]
    private static void BuildableCanBeDestroyedPostfix(BuildableItem __instance, ref bool __result, ref string reason)
    {
        if (!__result)
        {
            return;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        Property? property = __instance.ParentProperty;
        if (clientMod != null
            && property != null
            && !string.IsNullOrWhiteSpace(property.PropertyCode)
            && !clientMod.CanAccessProperty(property.PropertyCode))
        {
            __result = false;
            reason = "Owned by another organisation";
        }
    }

    [HarmonyPatch(typeof(ManagementWorldspaceCanvas), "GetHoveredConfigurable")]
    [HarmonyPostfix]
    private static void GetHoveredConfigurablePostfix(ref IConfigurable? __result)
    {
        if (__result != null && !CanAccessConfigurable(__result))
        {
            __result = null;
        }
    }

    [HarmonyPatch(typeof(ManagementWorldspaceCanvas), "GetConfigurablesToShow")]
    [HarmonyPostfix]
    private static void GetConfigurablesToShowPostfix(ref System.Collections.Generic.List<IConfigurable> __result)
    {
        if (__result == null)
        {
            return;
        }

        __result.RemoveAll(configurable => configurable == null || !CanAccessConfigurable(configurable));
    }

    private static bool CanAccessConfigurable(IConfigurable configurable)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        Property? property = configurable?.ParentProperty;
        return clientMod == null
            || property == null
            || string.IsNullOrWhiteSpace(property.PropertyCode)
            || clientMod.CanAccessProperty(property.PropertyCode);
    }

    private static Property? FindPropertyForStorage(StorageEntity storage)
    {
        if (storage == null)
        {
            return null;
        }

        PlaceableStorageEntity? placeableStorage = storage.GetComponentInParent<PlaceableStorageEntity>();
        if (placeableStorage?.ParentProperty != null)
        {
            return placeableStorage.ParentProperty;
        }

        SurfaceStorageEntity? surfaceStorage = storage.GetComponentInParent<SurfaceStorageEntity>();
        if (surfaceStorage?.ParentProperty != null)
        {
            return surfaceStorage.ParentProperty;
        }

        BuildableItem? buildable = storage.GetComponentInParent<BuildableItem>();
        if (buildable?.ParentProperty != null)
        {
            return buildable.ParentProperty;
        }

        PropertyContentsContainer? container = storage.GetComponentInParent<PropertyContentsContainer>();
        return container?.Property ?? storage.GetComponentInParent<Property>();
    }

    private static DeadDrop? FindDeaddropForStorage(StorageEntity storage)
    {
        if (storage == null)
        {
            return null;
        }

        return storage.GetComponentInParent<DeadDrop>();
    }
}

[HarmonyPatch]
internal static class OrganisationPropertyBuildClientPatches
{
    [HarmonyPatch(typeof(BuildUpdate_Grid), "CheckIntersections")]
    [HarmonyPostfix]
    private static void GridCheckIntersectionsPostfix(BuildUpdate_Grid __instance)
    {
        TileIntersection? closestIntersection = AccessTools.Field(typeof(BuildUpdate_Grid), "_closestIntersection")?.GetValue(__instance) as TileIntersection;
        Property? property = closestIntersection?.tile?.OwnerGrid?.ParentProperty;
        if (!CanAccessProperty(property))
        {
            AccessTools.Field(typeof(BuildUpdate_Grid), "_validPosition")?.SetValue(__instance, false);
        }
    }

    [HarmonyPatch(typeof(BuildUpdate_Surface), "IsSurfaceValidForItem")]
    [HarmonyPostfix]
    private static void SurfaceIsValidForItemPostfix(Surface surface, ref bool __result)
    {
        if (__result && !CanAccessProperty(surface?.ParentProperty))
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(BuildUpdate_ProceduralGrid), "CheckGridIntersections")]
    [HarmonyPostfix]
    private static void ProceduralGridCheckIntersectionsPostfix(BuildUpdate_ProceduralGrid __instance)
    {
        BuildUpdate_ProceduralGrid.Intersection? bestIntersection =
            AccessTools.Field(typeof(BuildUpdate_ProceduralGrid), "bestIntersection")?.GetValue(__instance) as BuildUpdate_ProceduralGrid.Intersection;
        Property? property = bestIntersection?.procTile?.ParentBuildableItem?.ParentProperty;
        if (!CanAccessProperty(property))
        {
            AccessTools.Field(typeof(BuildUpdate_ProceduralGrid), "validPosition")?.SetValue(__instance, false);
        }
    }

    private static bool CanAccessProperty(Property? property)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null
            || property == null
            || string.IsNullOrWhiteSpace(property.PropertyCode)
            || clientMod.CanAccessProperty(property.PropertyCode);
    }
}

[HarmonyPatch]
internal static class OrganisationPropertyTransitClientPatches
{
    [HarmonyPatch(typeof(TransitEntitySelector), nameof(TransitEntitySelector.IsObjectTypeValid))]
    [HarmonyPostfix]
    private static void TransitEntitySelectorIsObjectTypeValidPostfix(ITransitEntity obj, ref bool __result, ref string reason)
    {
        if (!__result)
        {
            return;
        }

        Property? property = FindPropertyForTransitEntity(obj);
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        if (clientMod != null
            && property != null
            && !string.IsNullOrWhiteSpace(property.PropertyCode)
            && !clientMod.CanAccessProperty(property.PropertyCode))
        {
            __result = false;
            reason = "Owned by another organisation";
        }
    }

    private static Property? FindPropertyForTransitEntity(ITransitEntity? transitEntity)
    {
        if (transitEntity == null)
        {
            return null;
        }

#if IL2CPP
        Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase transitObject = transitEntity;
        LoadingDock? loadingDock = transitObject.TryCast<LoadingDock>();
        if (loadingDock != null)
        {
            return loadingDock.ParentProperty;
        }

        IConfigurable? configurable = transitObject.TryCast<IConfigurable>();
        if (configurable != null)
        {
            return configurable.ParentProperty;
        }

        BuildableItem? buildableItem = transitObject.TryCast<BuildableItem>();
        if (buildableItem != null)
        {
            return buildableItem.ParentProperty;
        }
#else
        if (transitEntity is LoadingDock loadingDock)
        {
            return loadingDock.ParentProperty;
        }

        if (transitEntity is IConfigurable configurable)
        {
            return configurable.ParentProperty;
        }

        if (transitEntity is BuildableItem buildableItem)
        {
            return buildableItem.ParentProperty;
        }
#endif

        return null;
    }
}

[HarmonyPatch]
internal static class OrganisationPropertyTrashClientPatches
{
    [HarmonyPatch(typeof(TrashContainer), nameof(TrashContainer.CanBeBagged))]
    [HarmonyPostfix]
    private static void TrashContainerCanBeBaggedPostfix(TrashContainer __instance, ref bool __result)
    {
        if (__result && !CanAccessProperty(FindPropertyForTrashContainer(__instance)))
        {
            __result = false;
        }
    }

    [HarmonyPatch(typeof(TrashBag_Equippable), "GetTrashItemsAtPoint")]
    [HarmonyPostfix]
    private static void TrashBagGetTrashItemsAtPointPostfix(ref List<TrashItem> __result)
    {
        if (__result == null)
        {
            return;
        }

        __result.RemoveAll(trash => trash == null || !CanAccessProperty(FindPropertyForTrashItem(trash)));
    }

    [HarmonyPatch(typeof(Toilet), nameof(Toilet.Hovered))]
    [HarmonyPostfix]
    private static void ToiletHoveredPostfix(Toilet __instance)
    {
        if (!CanAccessProperty(__instance.ParentProperty))
        {
            __instance.IntObj.SetInteractableState(InteractableObject.EInteractableState.Invalid);
            __instance.IntObj.SetMessage("Owned by another organisation");
        }
    }

    [HarmonyPatch(typeof(Toilet), nameof(Toilet.Interacted))]
    [HarmonyPrefix]
    private static bool ToiletInteractedPrefix(Toilet __instance)
    {
        return CanAccessProperty(__instance.ParentProperty);
    }

    private static bool CanAccessProperty(Property? property)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null
            || property == null
            || string.IsNullOrWhiteSpace(property.PropertyCode)
            || clientMod.CanAccessProperty(property.PropertyCode);
    }

    private static Property? FindPropertyForTrashContainer(TrashContainer trashContainer)
    {
        if (trashContainer == null)
        {
            return null;
        }

        TrashContainerItem? trashContainerItem = trashContainer.GetComponentInParent<TrashContainerItem>();
        if (trashContainerItem?.ParentProperty != null)
        {
            return trashContainerItem.ParentProperty;
        }

        return FindPropertyForComponent(trashContainer);
    }

    private static Property? FindPropertyForTrashItem(TrashItem trash)
    {
        if (trash == null)
        {
            return null;
        }

        if (trash.CurrentProperty != null)
        {
            return trash.CurrentProperty;
        }

        return FindPropertyAtPoint(trash.transform.position);
    }

    private static Property? FindPropertyForComponent(Component component)
    {
        if (component == null)
        {
            return null;
        }

        BuildableItem? buildable = component.GetComponentInParent<BuildableItem>();
        if (buildable?.ParentProperty != null)
        {
            return buildable.ParentProperty;
        }

        PropertyContentsContainer? container = component.GetComponentInParent<PropertyContentsContainer>();
        if (container?.Property != null)
        {
            return container.Property;
        }

        Property? parentProperty = component.GetComponentInParent<Property>();
        return parentProperty ?? FindPropertyAtPoint(component.transform.position);
    }

    private static Property? FindPropertyAtPoint(Vector3 position)
    {
        for (int i = 0; i < Property.Properties.Count; i++)
        {
            Property property = Property.Properties[i];
            if (property != null
                && !string.IsNullOrWhiteSpace(property.PropertyCode)
                && property.DoBoundsContainPoint(position))
            {
                return property;
            }
        }

        return null;
    }
}

[HarmonyPatch]
internal static class OrganisationPropertyStationClientPatches
{
    [HarmonyPatch(typeof(LabelledSurfaceItem), nameof(LabelledSurfaceItem.Interacted))]
    [HarmonyPrefix]
    private static bool LabelledSurfaceItemInteractedPrefix(LabelledSurfaceItem __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(Sprinkler), nameof(Sprinkler.Hovered))]
    [HarmonyPostfix]
    private static void SprinklerHoveredPostfix(Sprinkler __instance)
    {
        DisableInteractableIfInaccessible(__instance.IntObj, __instance.ParentProperty);
    }

    [HarmonyPatch(typeof(Sprinkler), nameof(Sprinkler.Interacted))]
    [HarmonyPrefix]
    private static bool SprinklerInteractedPrefix(Sprinkler __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(SoilPourer), nameof(SoilPourer.HandleHovered))]
    [HarmonyPostfix]
    private static void SoilPourerHandleHoveredPostfix(SoilPourer __instance)
    {
        DisableInteractableIfInaccessible(__instance.HandleIntObj, __instance.ParentProperty);
    }

    [HarmonyPatch(typeof(SoilPourer), nameof(SoilPourer.FillHovered))]
    [HarmonyPostfix]
    private static void SoilPourerFillHoveredPostfix(SoilPourer __instance)
    {
        DisableInteractableIfInaccessible(__instance.FillIntObj, __instance.ParentProperty);
    }

    [HarmonyPatch(typeof(SoilPourer), nameof(SoilPourer.HandleInteracted))]
    [HarmonyPrefix]
    private static bool SoilPourerHandleInteractedPrefix(SoilPourer __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(SoilPourer), nameof(SoilPourer.FillInteracted))]
    [HarmonyPrefix]
    private static bool SoilPourerFillInteractedPrefix(SoilPourer __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(ChemistryStation), nameof(ChemistryStation.Interacted))]
    [HarmonyPrefix]
    private static bool ChemistryStationInteractedPrefix(ChemistryStation __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(Cauldron), nameof(Cauldron.Interacted))]
    [HarmonyPrefix]
    private static bool CauldronInteractedPrefix(Cauldron __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(LabOven), nameof(LabOven.Interacted))]
    [HarmonyPrefix]
    private static bool LabOvenInteractedPrefix(LabOven __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(MixingStation), nameof(MixingStation.Interacted))]
    [HarmonyPrefix]
    private static bool MixingStationInteractedPrefix(MixingStation __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(DryingRack), nameof(DryingRack.Interacted))]
    [HarmonyPrefix]
    private static bool DryingRackInteractedPrefix(DryingRack __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(OldMixingStation), nameof(OldMixingStation.Interacted))]
    [HarmonyPrefix]
    private static bool OldMixingStationInteractedPrefix(OldMixingStation __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(PackagingStation), nameof(PackagingStation.Interacted))]
    [HarmonyPrefix]
    private static bool PackagingStationInteractedPrefix(PackagingStation __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(BrickPress), nameof(BrickPress.Interacted))]
    [HarmonyPrefix]
    private static bool BrickPressInteractedPrefix(BrickPress __instance)
    {
        return CanAccessBuildable(__instance);
    }

    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.Hovered))]
    [HarmonyPostfix]
    private static void VendingMachineHoveredPostfix(VendingMachine __instance)
    {
        DisableInteractableIfInaccessible(__instance.IntObj, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(VendingMachine), nameof(VendingMachine.Interacted))]
    [HarmonyPrefix]
    private static bool VendingMachineInteractedPrefix(VendingMachine __instance)
    {
        return CanAccessProperty(FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(Recycler), "HandleInteracted")]
    [HarmonyPrefix]
    private static bool RecyclerHandleInteractedPrefix(Recycler __instance)
    {
        return CanAccessProperty(FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(Recycler), "ButtonInteracted")]
    [HarmonyPrefix]
    private static bool RecyclerButtonInteractedPrefix(Recycler __instance)
    {
        return CanAccessProperty(FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(Recycler), "CashInteracted")]
    [HarmonyPrefix]
    private static bool RecyclerCashInteractedPrefix(Recycler __instance)
    {
        return CanAccessProperty(FindPropertyForComponent(__instance));
    }

    private static bool CanAccessBuildable(BuildableItem buildable)
    {
        return CanAccessProperty(buildable?.ParentProperty);
    }

    private static bool CanAccessProperty(Property? property)
    {
        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod == null
            || property == null
            || string.IsNullOrWhiteSpace(property.PropertyCode)
            || clientMod.CanAccessProperty(property.PropertyCode);
    }

    private static void DisableInteractableIfInaccessible(InteractableObject interactable, Property? property)
    {
        if (interactable != null && !CanAccessProperty(property))
        {
            interactable.SetInteractableState(InteractableObject.EInteractableState.Invalid);
            interactable.SetMessage("Owned by another organisation");
        }
    }

    private static Property? FindPropertyForComponent(Component component)
    {
        if (component == null)
        {
            return null;
        }

        BuildableItem? buildable = component.GetComponentInParent<BuildableItem>();
        if (buildable?.ParentProperty != null)
        {
            return buildable.ParentProperty;
        }

        PropertyContentsContainer? container = component.GetComponentInParent<PropertyContentsContainer>();
        if (container?.Property != null)
        {
            return container.Property;
        }

        Property? parentProperty = component.GetComponentInParent<Property>();
        return parentProperty ?? FindPropertyAtPoint(component.transform.position);
    }

    private static Property? FindPropertyAtPoint(Vector3 position)
    {
        for (int i = 0; i < Property.Properties.Count; i++)
        {
            Property property = Property.Properties[i];
            if (property != null
                && !string.IsNullOrWhiteSpace(property.PropertyCode)
                && property.DoBoundsContainPoint(position))
            {
                return property;
            }
        }

        return null;
    }
}
#endif
