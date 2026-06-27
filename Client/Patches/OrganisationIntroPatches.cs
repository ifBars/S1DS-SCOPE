#if CLIENT
using System;
using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.AvatarFramework;
using Il2CppScheduleOne.AvatarFramework.Customization;
using Il2CppScheduleOne.Clothing;
using Il2CppScheduleOne.Cutscenes;
using ClothingList = Il2CppSystem.Collections.Generic.List<Il2CppScheduleOne.Clothing.ClothingInstance>;
#else
using ScheduleOne.AvatarFramework;
using ScheduleOne.AvatarFramework.Customization;
using ScheduleOne.Clothing;
using ScheduleOne.Cutscenes;
using ClothingList = System.Collections.Generic.List<ScheduleOne.Clothing.ClothingInstance>;
#endif

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationIntroPatches
{
    private static Action? _onCharacterCreatorComplete;
    private static Action<string>? _log;

    public static void Initialize(Action onCharacterCreatorComplete, Action<string> log)
    {
        _onCharacterCreatorComplete = onCharacterCreatorComplete;
        _log = log;
    }

    [HarmonyPatch(typeof(IntroManager), nameof(IntroManager.CharacterCreationDone))]
    [HarmonyPostfix]
    private static void CharacterCreationDonePostfix(BasicAvatarSettings avatar, ClothingList clothes)
    {
        _ = avatar;
        _ = clothes;
        _log?.Invoke("Observed IntroManager.CharacterCreationDone postfix.");
        _onCharacterCreatorComplete?.Invoke();
    }
}
#endif
