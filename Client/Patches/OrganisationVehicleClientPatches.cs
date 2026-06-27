#if CLIENT
using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.Interaction;
using Il2CppScheduleOne.Vehicles;
#else
using ScheduleOne.Interaction;
using ScheduleOne.Vehicles;
#endif

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationVehicleClientPatches
{
    [HarmonyPatch(typeof(LandVehicle), "Hovered")]
    [HarmonyPrefix]
    private static bool HoveredPrefix(LandVehicle __instance)
    {
        if (!ShouldDenyVehicleAccess(__instance))
        {
            return true;
        }

        InteractableObject? interactable = AccessTools.Field(typeof(LandVehicle), "intObj")?.GetValue(__instance) as InteractableObject;
        interactable?.SetMessage("Vehicle owned by another player");
        interactable?.SetInteractableState(InteractableObject.EInteractableState.Disabled);
        return false;
    }

    [HarmonyPatch(typeof(LandVehicle), "Interacted")]
    [HarmonyPrefix]
    private static bool InteractedPrefix(LandVehicle __instance)
    {
        return !ShouldDenyVehicleAccess(__instance);
    }

    private static bool ShouldDenyVehicleAccess(LandVehicle vehicle)
    {
        if (vehicle == null || !vehicle.IsPlayerOwned)
        {
            return false;
        }

        OrganisationsClientMod? clientMod = OrganisationsClientMod.ActiveInstance;
        return clientMod != null && !clientMod.CanAccessVehicle(vehicle.GUID.ToString());
    }
}
#endif
