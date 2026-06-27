#if SERVER
using System;
using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.Persistence;
#else
using ScheduleOne.Persistence;
#endif

namespace DedicatedServerMod.Organisations.Server.Patches;

[HarmonyPatch]
internal static class OrganisationPersistencePatches
{
    [HarmonyPatch(typeof(SaveManager), "Clean")]
    [HarmonyPostfix]
    private static void SaveManagerCleanPostfix()
    {
        OrganisationsServerMod.ActiveInstance?.EnsureRepositoryRegisteredFromPersistenceHook("SaveManager.Clean postfix");
    }

#if IL2CPP
    [HarmonyPatch(typeof(SaveManager), "Save", new Type[] { typeof(string) })]
    [HarmonyPostfix]
    private static void SaveManagerSavePostfix(string saveFolderPath)
    {
        OrganisationsServerMod.ActiveInstance?.SaveRepositoryFromPersistenceHook(saveFolderPath, "SaveManager.Save postfix");
    }
#endif
}
#endif
