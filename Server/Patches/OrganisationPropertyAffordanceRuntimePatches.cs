#if SERVER
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
#if IL2CPP
using Il2CppFishNet.Connection;
using Il2CppFishNet.Object;
using Il2CppFishNet.Serializing;
using Il2CppFishNet.Transporting;
using Il2CppScheduleOne.Building;
using Il2CppScheduleOne.Cartel;
using Il2CppScheduleOne.Delivery;
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Economy;
using Il2CppScheduleOne.EntityFramework;
using Il2CppScheduleOne.Employees;
using Il2CppScheduleOne.ItemFramework;
using Il2CppScheduleOne.Management;
using Il2CppScheduleOne.NPCs;
using Il2CppScheduleOne.ObjectScripts;
using Il2CppScheduleOne.Persistence.Datas;
using Il2CppScheduleOne.PlayerScripts;
using Il2CppScheduleOne.Property;
using Il2CppScheduleOne.Storage;
using Il2CppScheduleOne.StationFramework;
using Il2CppScheduleOne.Temperature;
using Il2CppScheduleOne.Tiles;
using Il2CppScheduleOne.Trash;
using Il2CppScheduleOne.Vehicles;
using GUIDManager = Il2Cpp.GUIDManager;
using Guid = Il2CppSystem.Guid;
#else
using FishNet.Connection;
using FishNet.Object;
using FishNet.Serializing;
using FishNet.Transporting;
using ScheduleOne.Building;
using ScheduleOne.Cartel;
using ScheduleOne.Delivery;
using ScheduleOne.DevUtilities;
using ScheduleOne.Economy;
using ScheduleOne.EntityFramework;
using ScheduleOne.Employees;
using ScheduleOne.ItemFramework;
using ScheduleOne.Management;
using ScheduleOne.NPCs;
using ScheduleOne.ObjectScripts;
using ScheduleOne.Persistence.Datas;
using ScheduleOne.PlayerScripts;
using ScheduleOne.Property;
using ScheduleOne.Storage;
using ScheduleOne.StationFramework;
using ScheduleOne.Temperature;
using ScheduleOne.Tiles;
using ScheduleOne.Trash;
using ScheduleOne.Vehicles;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationPropertyAffordanceRuntimePatches
{
    [HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SendAccessor_3323014238")]
    [HarmonyPrefix]
    private static bool StorageSendAccessorPrefix(StorageEntity __instance, NetworkConnection conn)
    {
        return CanConnectionAccessStorage(conn, __instance);
    }

    [HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SetStoredInstance_2652194801")]
    [HarmonyPrefix]
    private static bool StorageSetStoredInstancePrefix(StorageEntity __instance, NetworkConnection conn)
    {
        return CanConnectionAccessStorage(conn, __instance);
    }

    [HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SetStoredInstance_2652194801")]
    [HarmonyPostfix]
    private static void StorageSetStoredInstancePostfix(StorageEntity __instance, NetworkConnection conn)
    {
        RecordDeaddropStorageMutation(conn, __instance);
        NoteSupplierStashMutation(conn, __instance);
        RecordCartelDealStorageMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SetItemSlotQuantity_1692629761")]
    [HarmonyPrefix]
    private static bool StorageSetItemSlotQuantityPrefix(StorageEntity __instance, NetworkConnection conn)
    {
        return CanConnectionAccessStorage(conn, __instance);
    }

    [HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SetItemSlotQuantity_1692629761")]
    [HarmonyPostfix]
    private static void StorageSetItemSlotQuantityPostfix(StorageEntity __instance, NetworkConnection conn)
    {
        RecordDeaddropStorageMutation(conn, __instance);
        NoteSupplierStashMutation(conn, __instance);
        RecordCartelDealStorageMutation(conn, __instance);
    }

    [HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SetSlotLocked_3170825843")]
    [HarmonyPrefix]
    private static bool StorageSetSlotLockedPrefix(StorageEntity __instance, NetworkConnection conn)
    {
        return CanConnectionAccessStorage(conn, __instance);
    }

    [HarmonyPatch(typeof(StorageEntity), "RpcReader___Server_SetSlotFilter_527532783")]
    [HarmonyPrefix]
    private static bool StorageSetSlotFilterPrefix(StorageEntity __instance, NetworkConnection conn)
    {
        return CanConnectionAccessStorage(conn, __instance);
    }

    [HarmonyPatch(typeof(BuildableItem), "RpcReader___Server_Destroy_Server_2166136261")]
    [HarmonyPrefix]
    private static bool BuildableDestroyPrefix(BuildableItem __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, __instance.ParentProperty);
    }

    [HarmonyPatch(typeof(LabelledSurfaceItem), "RpcReader___Server_SendMessageToServer_3615296227")]
    [HarmonyPrefix]
    private static bool LabelledSurfaceItemSendMessagePrefix(LabelledSurfaceItem __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(ToggleableItem), "RpcReader___Server_SendIsOn_1140765316")]
    [HarmonyPrefix]
    private static bool ToggleableItemSendIsOnPrefix(ToggleableItem __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(ToggleableSurfaceItem), "RpcReader___Server_SendIsOn_1140765316")]
    [HarmonyPrefix]
    private static bool ToggleableSurfaceItemSendIsOnPrefix(ToggleableSurfaceItem __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(AirConditioner), "RpcReader___Server_SetMode_Server_3835190203")]
    [HarmonyPrefix]
    private static bool AirConditionerSetModePrefix(AirConditioner __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(Sprinkler), "RpcReader___Server_SendWater_2166136261")]
    [HarmonyPrefix]
    private static bool SprinklerSendWaterPrefix(Sprinkler __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(SoilPourer), "RpcReader___Server_SendPourSoil_2166136261")]
    [HarmonyPrefix]
    private static bool SoilPourerSendPourSoilPrefix(SoilPourer __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(SoilPourer), "RpcReader___Server_SendSoil_3615296227")]
    [HarmonyPrefix]
    private static bool SoilPourerSendSoilPrefix(SoilPourer __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(Jukebox), "RpcReader___Server_SendJukeboxState_1728100027")]
    [HarmonyPrefix]
    private static bool JukeboxSendStatePrefix(Jukebox __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(Recycler), "RpcReader___Server_SendState_3569965459")]
    [HarmonyPrefix]
    private static bool RecyclerSendStatePrefix(Recycler __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(Recycler), "RpcReader___Server_SendCashCollected_2166136261")]
    [HarmonyPrefix]
    private static bool RecyclerSendCashCollectedPrefix(Recycler __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(ChemistryStation), "RpcReader___Server_SendCookOperation_3552222198")]
    [HarmonyPrefix]
    private static bool ChemistryStationSendCookOperationPrefix(ChemistryStation __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(Cauldron), "RpcReader___Server_SendCookOperation_3536682170")]
    [HarmonyPrefix]
    private static bool CauldronSendCookOperationPrefix(Cauldron __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(LabOven), "RpcReader___Server_SendCookOperation_3708012700")]
    [HarmonyPrefix]
    private static bool LabOvenSendCookOperationPrefix(LabOven __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(MixingStation), "RpcReader___Server_SendMixingOperation_2669582547")]
    [HarmonyPrefix]
    private static bool MixingStationSendMixingOperationPrefix(MixingStation __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(MixingStation), "RpcReader___Server_TryCreateOutputItems_2166136261")]
    [HarmonyPrefix]
    private static bool MixingStationTryCreateOutputItemsPrefix(MixingStation __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(DryingRack), "RpcReader___Server_SendOperation_1307702229")]
    [HarmonyPrefix]
    private static bool DryingRackSendOperationPrefix(DryingRack __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(DryingRack), "RpcReader___Server_TryEndOperation_4146970406")]
    [HarmonyPrefix]
    private static bool DryingRackTryEndOperationPrefix(DryingRack __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(Pot), "RpcReader___Server_PlantSeed_Server_606697822")]
    [HarmonyPrefix]
    private static bool PotPlantSeedPrefix(Pot __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(Pot), "RpcReader___Server_SetGrowthProgress_Server_431000436")]
    [HarmonyPrefix]
    private static bool PotSetGrowthProgressPrefix(Pot __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(Pot), "RpcReader___Server_SetHarvestableActive_Server_3658436649")]
    [HarmonyPrefix]
    private static bool PotSetHarvestableActivePrefix(Pot __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(MushroomBed), "RpcReader___Server_CreateAndAssignColony_Server_3615296227")]
    [HarmonyPrefix]
    private static bool MushroomBedCreateAndAssignColonyPrefix(MushroomBed __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(VendingMachine), "RpcReader___Server_SendPurchase_2166136261")]
    [HarmonyPrefix]
    private static bool VendingMachineSendPurchasePrefix(VendingMachine __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(VendingMachine), "RpcReader___Server_SendBreak_2166136261")]
    [HarmonyPrefix]
    private static bool VendingMachineSendBreakPrefix(VendingMachine __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(VendingMachine), "RpcReader___Server_DropItem_2166136261")]
    [HarmonyPrefix]
    private static bool VendingMachineDropItemPrefix(VendingMachine __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(VendingMachine), "RpcReader___Server_DropCash_2166136261")]
    [HarmonyPrefix]
    private static bool VendingMachineDropCashPrefix(VendingMachine __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(Business), "RpcReader___Server_StartLaunderingOperation_1481775633")]
    [HarmonyPrefix]
    private static bool BusinessStartLaunderingOperationPrefix(Business __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient || __instance == null || !serverMod.IsTrackedProperty(__instance))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        float amount = PooledReader0.ReadSingle();
        int minutesSinceStarted = PooledReader0.ReadInt32();
        bool canStart = serverMod.TryAuthorizeBusinessLaunderingStart(conn, __instance, amount, minutesSinceStarted);
        if (canStart)
        {
            PooledReader0.Position = originalPosition;
        }

        return canStart;
    }

    [HarmonyPatch(typeof(Property), "RpcReader___Server_SendToggleableState_3658436649")]
    [HarmonyPrefix]
    private static bool PropertySendToggleableStatePrefix(Property __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, __instance);
    }

    [HarmonyPatch(typeof(Player), "RpcReader___Server_set_CurrentBed_3323014238")]
    [HarmonyPrefix]
    private static bool PlayerSetCurrentBedPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        NetworkObject bedObject = PooledReader0.ReadNetworkObject();
        Property? property = FindPropertyForNetworkObject(bedObject);
        bool canAccess = CanConnectionAccessProperty(conn, property);
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(Tap), "RpcReader___Server_SetHeldOpen_1140765316")]
    [HarmonyPrefix]
    private static bool TapSetHeldOpenPrefix(Tap __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(Tap), "RpcReader___Server_SetPlayerUser_3323014238")]
    [HarmonyPrefix]
    private static bool TapSetPlayerUserPrefix(Tap __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(Tap), "RpcReader___Server_SetNPCUser_3323014238")]
    [HarmonyPrefix]
    private static bool TapSetNpcUserPrefix(Tap __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForComponent(__instance));
    }

    [HarmonyPatch(typeof(EmployeeManager), "RpcReader___Server_CreateEmployee_311954683")]
    [HarmonyPrefix]
    private static bool EmployeeManagerCreateEmployeePrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        Property? property = ReadProperty(PooledReader0);
        if (property == null)
        {
            PooledReader0.Position = originalPosition;
            return true;
        }

        bool canAccess = CanConnectionAccessProperty(conn, property);
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(Employee), "RpcReader___Server_SendTransfer_3615296227")]
    [HarmonyPrefix]
    private static bool EmployeeSendTransferPrefix(Employee __instance, PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        string propertyCode = PooledReader0.ReadString();
        Property? targetProperty = GetPropertyByCode(propertyCode);
        bool canAccess = CanConnectionAccessProperty(conn, __instance.AssignedProperty)
            && CanConnectionAccessProperty(conn, targetProperty);
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(Employee), "RpcReader___Server_SendFire_2166136261")]
    [HarmonyPrefix]
    private static bool EmployeeSendFirePrefix(Employee __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, __instance.AssignedProperty);
    }

    [HarmonyPatch]
    private static class EmployeeConfigurerRpcPatches
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (System.Type type in GetEmployeeConfigurerRpcTypes())
            {
                MethodInfo? method = AccessTools.Method(type, "RpcReader___Server_SetConfigurer_3323014238");
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(Employee __instance, NetworkConnection conn)
        {
            return CanConnectionAccessProperty(conn, __instance.AssignedProperty);
        }
    }

    [HarmonyPatch]
    private static class EmployeeInventoryRpcPatches
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (string methodName in GetEmployeeInventoryRpcMethodNames())
            {
                MethodInfo? method = AccessTools.Method(typeof(NPCInventory), methodName);
                if (method != null)
                {
                    yield return method;
                }
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(NPCInventory __instance, NetworkConnection conn)
        {
            Employee? employee = __instance.GetComponentInParent<Employee>();
            return employee == null || CanConnectionAccessProperty(conn, employee.AssignedProperty);
        }
    }

    [HarmonyPatch]
    private static class GeneratedStationAccessRpcPatches
    {
        [HarmonyTargetMethods]
        private static IEnumerable<MethodBase> TargetMethods()
        {
            foreach (System.Type type in GetGeneratedStationAccessRpcTypes())
            {
                foreach (string methodName in GetGeneratedStationAccessRpcMethodNames())
                {
                    MethodInfo? method = AccessTools.Method(type, methodName);
                    if (method != null)
                    {
                        yield return method;
                    }
                }
            }
        }

        [HarmonyPrefix]
        private static bool Prefix(BuildableItem __instance, NetworkConnection conn)
        {
            return CanConnectionAccessBuildable(conn, __instance);
        }
    }

    [HarmonyPatch(typeof(Pot), "RpcReader___Server_SetConfigurer_3323014238")]
    [HarmonyPrefix]
    private static bool PotSetConfigurerPrefix(Pot __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(MushroomBed), "RpcReader___Server_SetConfigurer_3323014238")]
    [HarmonyPrefix]
    private static bool MushroomBedSetConfigurerPrefix(MushroomBed __instance, NetworkConnection conn)
    {
        return CanConnectionAccessBuildable(conn, __instance);
    }

    [HarmonyPatch(typeof(Toilet), "RpcReader___Server_SendFlush_2166136261")]
    [HarmonyPrefix]
    private static bool ToiletSendFlushPrefix(Toilet __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, __instance.ParentProperty);
    }

    [HarmonyPatch(typeof(TrashContainer), "RpcReader___Server_SendTrash_3643459082")]
    [HarmonyPrefix]
    private static bool TrashContainerSendTrashPrefix(TrashContainer __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForTrashContainer(__instance));
    }

    [HarmonyPatch(typeof(TrashContainer), "RpcReader___Server_SendClear_2166136261")]
    [HarmonyPrefix]
    private static bool TrashContainerSendClearPrefix(TrashContainer __instance, NetworkConnection conn)
    {
        return CanConnectionAccessProperty(conn, FindPropertyForTrashContainer(__instance));
    }

    [HarmonyPatch(typeof(TrashManager), "RpcReader___Server_SendDestroyTrash_3615296227")]
    [HarmonyPrefix]
    private static bool TrashManagerSendDestroyTrashPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        string trashGuid = PooledReader0.ReadString();
        TrashItem? trash = TryGetGuidObject<TrashItem>(trashGuid);
        bool canAccess = CanConnectionAccessProperty(conn, FindPropertyForTrashItem(trash));
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(TrashManager), "RpcReader___Server_SendTransformData_2990100769")]
    [HarmonyPrefix]
    private static bool TrashManagerSendTransformDataPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        string trashGuid = PooledReader0.ReadString();
        Vector3 targetPosition = PooledReader0.ReadVector3();
        TrashItem? trash = TryGetGuidObject<TrashItem>(trashGuid);
        bool canAccess = CanConnectionAccessProperty(conn, FindPropertyForTrashItem(trash))
            && CanConnectionAccessProperty(conn, FindTrackedPropertyAtPoint(targetPosition));
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(TrashManager), "RpcReader___Server_SendTrashItem_478112418")]
    [HarmonyPrefix]
    private static bool TrashManagerSendTrashItemPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadString();
        Vector3 position = PooledReader0.ReadVector3();
        bool canAccess = CanConnectionAccessProperty(conn, FindTrackedPropertyAtPoint(position));
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(TrashManager), "RpcReader___Server_SendTrashBag_3965031115")]
    [HarmonyPrefix]
    private static bool TrashManagerSendTrashBagPrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadString();
        Vector3 position = PooledReader0.ReadVector3();
        bool canAccess = CanConnectionAccessProperty(conn, FindTrackedPropertyAtPoint(position));
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(GridItem), "RpcReader___Server_InitializeGridItem_Server_2821640832")]
    [HarmonyPrefix]
    private static bool GridItemInitializePrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadItemInstance();
        string gridGuid = PooledReader0.ReadString();
        _ = PooledReader0.ReadVector2();
        _ = PooledReader0.ReadInt32();
        _ = PooledReader0.ReadString();

        Grid? grid = TryGetGuidObject<Grid>(gridGuid);
        bool canAccess = CanConnectionAccessProperty(conn, grid?.ParentProperty);
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(SurfaceItem), "RpcReader___Server_InitializeSurfaceItem_Server_2652836379")]
    [HarmonyPrefix]
    private static bool SurfaceItemInitializePrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadItemInstance();
        _ = PooledReader0.ReadString();
        string surfaceGuid = PooledReader0.ReadString();
        _ = PooledReader0.ReadVector3();
        _ = PooledReader0.ReadQuaternion();

        Surface? surface = TryGetGuidObject<Surface>(surfaceGuid);
        bool canAccess = CanConnectionAccessProperty(conn, surface?.ParentProperty);
        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
    }

    [HarmonyPatch(typeof(ProceduralGridItem), "RpcReader___Server_InitializeProceduralGridItem_Server_638911643")]
    [HarmonyPrefix]
    private static bool ProceduralGridItemInitializePrefix(PooledReader PooledReader0, Channel channel, NetworkConnection conn)
    {
        _ = channel;
        if (ShouldBypassPayloadInspection(conn))
        {
            return true;
        }

        int originalPosition = PooledReader0.Position;
        _ = PooledReader0.ReadItemInstance();
        _ = PooledReader0.ReadInt32();
#if MONO
        List<CoordinateProceduralTilePair>? matches = ReadProceduralTileMatches(PooledReader0);
        if (matches == null)
        {
            PooledReader0.Position = originalPosition;
            return true;
        }

        _ = PooledReader0.ReadString();

        bool canAccess = true;
        for (int i = 0; i < matches.Count; i++)
        {
            Property? property = FindPropertyForNetworkObject(matches[i].tileParent);
            if (!CanConnectionAccessProperty(conn, property))
            {
                canAccess = false;
                break;
            }
        }

        if (canAccess)
        {
            PooledReader0.Position = originalPosition;
        }

        return canAccess;
#else
        PooledReader0.Position = originalPosition;
        return true;
#endif
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendItemField_2801973956")]
    [HarmonyPrefix]
    private static bool ConfigSendItemFieldPrefix(ConfigurationReplicator __instance, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance);
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendNPCField_1687693739")]
    [HarmonyPrefix]
    private static bool ConfigSendNpcFieldPrefix(ConfigurationReplicator __instance, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance);
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendObjectField_1687693739")]
    [HarmonyPrefix]
    private static bool ConfigSendObjectFieldPrefix(ConfigurationReplicator __instance, PooledReader PooledReader0, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance) && CanConnectionAccessObjectFieldPayload(conn, PooledReader0);
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendObjectListField_690244341")]
    [HarmonyPrefix]
    private static bool ConfigSendObjectListFieldPrefix(ConfigurationReplicator __instance, PooledReader PooledReader0, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance) && CanConnectionAccessObjectListFieldPayload(conn, PooledReader0);
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendRecipeField_1692629761")]
    [HarmonyPrefix]
    private static bool ConfigSendRecipeFieldPrefix(ConfigurationReplicator __instance, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance);
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendNumberField_1293284375")]
    [HarmonyPrefix]
    private static bool ConfigSendNumberFieldPrefix(ConfigurationReplicator __instance, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance);
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendRouteListField_3226448297")]
    [HarmonyPrefix]
    private static bool ConfigSendRouteListFieldPrefix(ConfigurationReplicator __instance, PooledReader PooledReader0, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance) && CanConnectionAccessRouteListPayload(conn, PooledReader0);
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendQualityField_3536682170")]
    [HarmonyPrefix]
    private static bool ConfigSendQualityFieldPrefix(ConfigurationReplicator __instance, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance);
    }

    [HarmonyPatch(typeof(ConfigurationReplicator), "RpcReader___Server_SendStringField_2801973956")]
    [HarmonyPrefix]
    private static bool ConfigSendStringFieldPrefix(ConfigurationReplicator __instance, NetworkConnection conn)
    {
        return CanConnectionAccessConfigurable(conn, __instance);
    }

    private static bool CanConnectionAccessStorage(NetworkConnection conn, StorageEntity storage)
    {
        DeadDrop? deaddrop = FindDeaddropForStorage(storage);
        if (deaddrop != null)
        {
            return CanConnectionAccessDeaddrop(conn, deaddrop);
        }

        SupplierStash? supplierStash = FindSupplierStashForStorage(storage);
        if (supplierStash != null)
        {
            return CanConnectionAccessSupplierStash(conn, supplierStash);
        }

        if (IsCartelDealStorage(storage))
        {
            return CanConnectionAccessCartelDealStorage(conn);
        }

        if (!CanConnectionAccessDeliveryVehicleStorage(conn, storage))
        {
            return false;
        }

        return CanConnectionAccessProperty(conn, FindPropertyForStorage(storage));
    }

    private static bool ShouldBypassPayloadInspection(NetworkConnection conn)
    {
        return conn == null || conn.IsLocalClient || OrganisationsServerMod.ActiveInstance == null;
    }

    private static bool CanConnectionAccessConfigurable(NetworkConnection conn, ConfigurationReplicator replicator)
    {
        Property? property = replicator?.Configuration?.Configurable?.ParentProperty;
        return CanConnectionAccessProperty(conn, property);
    }

    private static bool CanConnectionAccessBuildable(NetworkConnection conn, BuildableItem buildable)
    {
        return CanConnectionAccessProperty(conn, buildable?.ParentProperty);
    }

    private static IEnumerable<System.Type> GetGeneratedStationAccessRpcTypes()
    {
        yield return typeof(PlaceableStorageEntity);
        yield return typeof(SurfaceStorageEntity);
        yield return typeof(ChemistryStation);
        yield return typeof(Cauldron);
        yield return typeof(DryingRack);
        yield return typeof(LabOven);
        yield return typeof(MixingStation);
        yield return typeof(OldMixingStation);
        yield return typeof(PackagingStation);
        yield return typeof(BrickPress);
        yield return typeof(MushroomSpawnStation);
    }

    private static IEnumerable<System.Type> GetEmployeeConfigurerRpcTypes()
    {
        yield return typeof(Botanist);
        yield return typeof(Chemist);
        yield return typeof(Packager);
        yield return typeof(Cleaner);
    }

    private static IEnumerable<string> GetEmployeeInventoryRpcMethodNames()
    {
        yield return "RpcReader___Server_SetStoredInstance_2652194801";
        yield return "RpcReader___Server_SetItemSlotQuantity_1692629761";
        yield return "RpcReader___Server_SetSlotLocked_3170825843";
        yield return "RpcReader___Server_SetSlotFilter_527532783";
    }

    private static IEnumerable<string> GetGeneratedStationAccessRpcMethodNames()
    {
        yield return "RpcReader___Server_SetPlayerUser_3323014238";
        yield return "RpcReader___Server_SetNPCUser_3323014238";
        yield return "RpcReader___Server_SetConfigurer_3323014238";
        yield return "RpcReader___Server_SetStoredInstance_2652194801";
        yield return "RpcReader___Server_SetItemSlotQuantity_1692629761";
        yield return "RpcReader___Server_SetSlotLocked_3170825843";
        yield return "RpcReader___Server_SetSlotFilter_527532783";
    }

    private static bool CanConnectionAccessProperty(NetworkConnection conn, Property? property)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient || property == null || !serverMod.IsTrackedProperty(property))
        {
            return true;
        }

        return serverMod.CanPlayerAccessProperty(conn, property);
    }

    private static bool CanConnectionAccessDeaddrop(NetworkConnection conn, DeadDrop deaddrop)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient || deaddrop == null)
        {
            return true;
        }

        return serverMod.CanPlayerAccessDeaddrop(conn, deaddrop.GUID.ToString());
    }

    private static bool CanConnectionAccessSupplierStash(NetworkConnection conn, SupplierStash supplierStash)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null || conn == null || conn.IsLocalClient || supplierStash?.Supplier == null)
        {
            return true;
        }

        return serverMod.CanUseSupplier(conn, supplierStash.Supplier);
    }

    private static bool CanConnectionAccessCartelDealStorage(NetworkConnection conn)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.CanAccessCartelDealStorage(conn);
    }

    private static bool CanConnectionAccessDeliveryVehicleStorage(NetworkConnection conn, StorageEntity storage)
    {
        DeliveryVehicle? deliveryVehicle = FindDeliveryVehicleForStorage(storage);
        if (deliveryVehicle?.ActiveDelivery == null)
        {
            return true;
        }

        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        return serverMod == null || serverMod.CanPlayerAccessDeliveryDestination(conn, deliveryVehicle.ActiveDelivery);
    }

    private static bool IsCartelDealStorage(StorageEntity storage)
    {
        WorldStorageEntity? deliveryEntity = NetworkSingleton<Cartel>.Instance?.DealManager?.DeliveryEntity;
        return deliveryEntity != null && ReferenceEquals(storage, deliveryEntity);
    }

    private static DeadDrop? FindDeaddropForStorage(StorageEntity storage)
    {
        if (storage == null)
        {
            return null;
        }

        return storage.GetComponentInParent<DeadDrop>();
    }

    private static SupplierStash? FindSupplierStashForStorage(StorageEntity storage)
    {
        if (storage == null)
        {
            return null;
        }

        return storage.GetComponentInParent<SupplierStash>();
    }

    private static void NoteSupplierStashMutation(NetworkConnection conn, StorageEntity storage)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return;
        }

        SupplierStash? supplierStash = FindSupplierStashForStorage(storage);
        if (supplierStash == null || !CanConnectionAccessSupplierStash(conn, supplierStash))
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.NoteSupplierStashMutation(conn, supplierStash);
    }

    private static void RecordDeaddropStorageMutation(NetworkConnection conn, StorageEntity storage)
    {
        if (conn == null || conn.IsLocalClient)
        {
            return;
        }

        DeadDrop? deaddrop = FindDeaddropForStorage(storage);
        if (deaddrop == null || !CanConnectionAccessDeaddrop(conn, deaddrop))
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.RecordDeaddropStorageMutation(conn, deaddrop.GUID.ToString());
    }

    private static void RecordCartelDealStorageMutation(NetworkConnection conn, StorageEntity storage)
    {
        if (conn == null || conn.IsLocalClient || !IsCartelDealStorage(storage))
        {
            return;
        }

        OrganisationsServerMod.ActiveInstance?.RecordCartelDealStorageMutation(conn);
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

    private static DeliveryVehicle? FindDeliveryVehicleForStorage(StorageEntity storage)
    {
        if (storage == null)
        {
            return null;
        }

        DeliveryVehicle? deliveryVehicle = storage.GetComponentInParent<DeliveryVehicle>();
        if (deliveryVehicle != null)
        {
            return deliveryVehicle;
        }

        LandVehicle? vehicle = storage.GetComponentInParent<LandVehicle>();
        return vehicle == null ? null : vehicle.GetComponent<DeliveryVehicle>();
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

    private static Property? FindPropertyForTrashItem(TrashItem? trash)
    {
        if (trash == null)
        {
            return null;
        }

        if (trash.CurrentProperty != null)
        {
            return trash.CurrentProperty;
        }

        return FindTrackedPropertyAtPoint(trash.transform.position);
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
        return parentProperty ?? FindTrackedPropertyAtPoint(component.transform.position);
    }

    private static Property? FindTrackedPropertyAtPoint(Vector3 position)
    {
        OrganisationsServerMod? serverMod = OrganisationsServerMod.ActiveInstance;
        if (serverMod == null)
        {
            return null;
        }

        for (int i = 0; i < Property.Properties.Count; i++)
        {
            Property property = Property.Properties[i];
            if (property != null
                && serverMod.IsTrackedProperty(property)
                && property.DoBoundsContainPoint(position))
            {
                return property;
            }
        }

        return null;
    }

    private static T? TryGetGuidObject<T>(string guid) where T : class
    {
        if (string.IsNullOrWhiteSpace(guid) || !Guid.TryParse(guid, out Guid parsedGuid))
        {
            return null;
        }

        return GUIDManager.GetObject<T>(parsedGuid);
    }

    private static bool CanConnectionAccessObjectFieldPayload(NetworkConnection conn, PooledReader reader)
    {
        int originalPosition = reader.Position;
        _ = reader.ReadInt32();
        NetworkObject selectedObject = reader.ReadNetworkObject();
        Property? property = FindPropertyForNetworkObject(selectedObject);
        bool canAccess = CanConnectionAccessProperty(conn, property);
        if (canAccess)
        {
            reader.Position = originalPosition;
        }

        return canAccess;
    }

    private static bool CanConnectionAccessObjectListFieldPayload(NetworkConnection conn, PooledReader reader)
    {
        int originalPosition = reader.Position;
        _ = reader.ReadInt32();
#if MONO
        List<NetworkObject>? selectedObjects = ReadNetworkObjectList(reader);
        if (selectedObjects == null)
        {
            reader.Position = originalPosition;
            return true;
        }

        bool canAccess = true;
        for (int i = 0; i < selectedObjects.Count; i++)
        {
            Property? property = FindPropertyForNetworkObject(selectedObjects[i]);
            if (!CanConnectionAccessProperty(conn, property))
            {
                canAccess = false;
                break;
            }
        }

        if (canAccess)
        {
            reader.Position = originalPosition;
        }

        return canAccess;
#else
        reader.Position = originalPosition;
        return true;
#endif
    }

    private static bool CanConnectionAccessRouteListPayload(NetworkConnection conn, PooledReader reader)
    {
        int originalPosition = reader.Position;
        _ = reader.ReadInt32();
#if MONO
        AdvancedTransitRouteData[]? routes = ReadRouteData(reader);
        if (routes == null)
        {
            reader.Position = originalPosition;
            return true;
        }

        bool canAccess = true;
        for (int i = 0; i < routes.Length; i++)
        {
            if (!CanConnectionAccessTransitGuid(conn, routes[i].SourceGUID)
                || !CanConnectionAccessTransitGuid(conn, routes[i].DestinationGUID))
            {
                canAccess = false;
                break;
            }
        }

        if (canAccess)
        {
            reader.Position = originalPosition;
        }

        return canAccess;
#else
        reader.Position = originalPosition;
        return true;
#endif
    }

    private static bool CanConnectionAccessTransitGuid(NetworkConnection conn, string guid)
    {
        ITransitEntity? transitEntity = TryGetGuidObject<ITransitEntity>(guid);
        Property? property = FindPropertyForTransitEntity(transitEntity);
        return CanConnectionAccessProperty(conn, property);
    }

    private static Property? GetPropertyByCode(string propertyCode)
    {
        if (string.IsNullOrWhiteSpace(propertyCode))
        {
            return null;
        }

        for (int i = 0; i < Property.Properties.Count; i++)
        {
            Property property = Property.Properties[i];
            if (property != null && property.PropertyCode == propertyCode)
            {
                return property;
            }
        }

        return null;
    }

    private static Property? FindPropertyForNetworkObject(NetworkObject networkObject)
    {
        if (networkObject == null)
        {
            return null;
        }

        BuildableItem? buildableItem = networkObject.GetComponent<BuildableItem>();
        if (buildableItem?.ParentProperty != null)
        {
            return buildableItem.ParentProperty;
        }

        LoadingDock? loadingDock = networkObject.GetComponent<LoadingDock>();
        if (loadingDock?.ParentProperty != null)
        {
            return loadingDock.ParentProperty;
        }

        IConfigurable? configurable = networkObject.GetComponent<IConfigurable>();
        if (configurable?.ParentProperty != null)
        {
            return configurable.ParentProperty;
        }

        PropertyContentsContainer? container = networkObject.GetComponent<PropertyContentsContainer>();
        if (container?.Property != null)
        {
            return container.Property;
        }

        Property? property = networkObject.GetComponent<Property>() ?? networkObject.GetComponentInParent<Property>();
        return property ?? FindTrackedPropertyAtPoint(networkObject.transform.position);
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

#if MONO
    private static List<NetworkObject>? ReadNetworkObjectList(PooledReader reader)
    {
        const string methodName = "Read___System_002ECollections_002EGeneric_002EList_00601_003CFishNet_002EObject_002ENetworkObject_003EFishNet_002ESerializing_002EGenerateds";
        return InvokeGeneratedReader<List<NetworkObject>>(methodName, reader);
    }

    private static List<CoordinateProceduralTilePair>? ReadProceduralTileMatches(PooledReader reader)
    {
        const string methodName = "Read___System_002ECollections_002EGeneric_002EList_00601_003CScheduleOne_002ETiles_002ECoordinateProceduralTilePair_003EFishNet_002ESerializing_002EGenerateds";
        return InvokeGeneratedReader<List<CoordinateProceduralTilePair>>(methodName, reader);
    }

    private static AdvancedTransitRouteData[]? ReadRouteData(PooledReader reader)
    {
        const string methodName = "Read___ScheduleOne_002EPersistence_002EDatas_002EAdvancedTransitRouteData_005B_005DFishNet_002ESerializing_002EGenerateds";
        return InvokeGeneratedReader<AdvancedTransitRouteData[]>(methodName, reader);
    }

    private static T? InvokeGeneratedReader<T>(string methodName, PooledReader reader) where T : class
    {
        System.Type? generatedReaders = typeof(ConfigurationReplicator).Assembly.GetType("FishNet.Serializing.Generated.GeneratedReaders___Internal");
        System.Reflection.MethodInfo? method = generatedReaders?.GetMethod(
            methodName,
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
        return method?.Invoke(null, new object[] { reader }) as T;
    }
#endif

    private static Property? ReadProperty(PooledReader reader)
    {
        return reader.ReadNetworkBehaviour() as Property;
    }
}
#endif
