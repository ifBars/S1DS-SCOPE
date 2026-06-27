#if CLIENT
using System.Collections;
using System.Reflection;
using HarmonyLib;
using MelonLoader;
#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.UI;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.UI;
#endif
using UnityEngine;

namespace DedicatedServerMod.Organisations.Client.Patches;

[HarmonyPatch]
internal static class OrganisationQuestHudSafetyPatches
{
    [HarmonyPatch(typeof(QuestEntryHUDUI), "FadeOut")]
    [HarmonyPrefix]
    private static bool SafeQuestEntryFadeOut(QuestEntryHUDUI __instance)
    {
        RunQuestEntryFadeOut(__instance);
        return false;
    }

    [HarmonyPatch(typeof(QuestEntryHUDUI), "Complete")]
    [HarmonyPrefix]
    private static bool SafeQuestEntryComplete(QuestEntryHUDUI __instance)
    {
        if (!IsAlive(__instance) || !IsAlive(__instance.gameObject))
        {
            return false;
        }

        if (!__instance.gameObject.activeSelf)
        {
            __instance.gameObject.SetActive(value: false);
            return false;
        }

        PlayAnimation(GetAnimationObject(__instance), "Quest entry complete");
        if (Singleton<CoroutineService>.InstanceExists)
        {
            MelonCoroutines.Start(QuestEntryCompleteRoutine(__instance));
        }
        else
        {
            RunQuestEntryFadeOut(__instance);
        }

        return false;
    }

    [HarmonyPatch(typeof(QuestHUDUI), "FadeOut")]
    [HarmonyPrefix]
    private static bool SafeQuestHudFadeOut(QuestHUDUI __instance)
    {
        RunQuestHudFadeOut(__instance);
        return false;
    }

    [HarmonyPatch(typeof(QuestHUDUI), "Complete")]
    [HarmonyPrefix]
    private static bool SafeQuestHudComplete(QuestHUDUI __instance)
    {
        if (!IsAlive(__instance) || !IsAlive(__instance.gameObject))
        {
            return false;
        }

        PlayAnimation(GetAnimationObject(__instance), "Quest complete");
        if (Singleton<CoroutineService>.InstanceExists)
        {
            MelonCoroutines.Start(QuestHudCompleteRoutine(__instance));
        }
        else
        {
            RunQuestHudFadeOut(__instance);
        }

        return false;
    }

    private static void RunQuestEntryFadeOut(QuestEntryHUDUI questEntryHudUi)
    {
        if (!IsAlive(questEntryHudUi))
        {
            return;
        }

        object? animation = GetAnimationObject(questEntryHudUi);
        PlayAnimation(animation, "Quest entry exit");
        float delaySeconds = GetAnimationClipLength(animation, "Quest entry exit");
        if (Singleton<CoroutineService>.InstanceExists)
        {
            MelonCoroutines.Start(QuestEntryFadeOutRoutine(questEntryHudUi, delaySeconds));
        }
        else
        {
            FinalizeQuestEntryFadeOut(questEntryHudUi);
        }
    }

    private static IEnumerator QuestEntryCompleteRoutine(QuestEntryHUDUI questEntryHudUi)
    {
        yield return new WaitForSeconds(3f);
        RunQuestEntryFadeOut(questEntryHudUi);
    }

    private static IEnumerator QuestEntryFadeOutRoutine(QuestEntryHUDUI questEntryHudUi, float delaySeconds)
    {
        if (delaySeconds > 0f)
        {
            yield return new WaitForSeconds(delaySeconds);
        }

        FinalizeQuestEntryFadeOut(questEntryHudUi);
    }

    private static void FinalizeQuestEntryFadeOut(QuestEntryHUDUI questEntryHudUi)
    {
        if (!IsAlive(questEntryHudUi) || !IsAlive(questEntryHudUi.gameObject))
        {
            return;
        }

        questEntryHudUi.gameObject.SetActive(value: false);
        questEntryHudUi.QuestEntry?.UpdateEntryUI();
    }

    private static void RunQuestHudFadeOut(QuestHUDUI questHudUi)
    {
        if (!IsAlive(questHudUi))
        {
            return;
        }

        PlayAnimation(GetAnimationObject(questHudUi), "Quest exit");
        if (Singleton<CoroutineService>.InstanceExists)
        {
            MelonCoroutines.Start(QuestHudFadeOutRoutine(questHudUi));
        }
        else
        {
            FinalizeQuestHudFadeOut(questHudUi);
        }
    }

    private static IEnumerator QuestHudCompleteRoutine(QuestHUDUI questHudUi)
    {
        yield return new WaitForSeconds(3f);
        RunQuestHudFadeOut(questHudUi);
    }

    private static IEnumerator QuestHudFadeOutRoutine(QuestHUDUI questHudUi)
    {
        yield return new WaitForSeconds(0.5f);
        FinalizeQuestHudFadeOut(questHudUi);
    }

    private static void FinalizeQuestHudFadeOut(QuestHUDUI questHudUi)
    {
        if (!IsAlive(questHudUi))
        {
            return;
        }

        questHudUi.Destroy();
    }

    private static void PlayAnimation(object? animation, string clipName)
    {
        if (animation == null)
        {
            return;
        }

        Type animationType = animation.GetType();
        PropertyInfo? isPlayingProperty = animationType.GetProperty("isPlaying", BindingFlags.Instance | BindingFlags.Public);
        bool isPlaying = isPlayingProperty?.PropertyType == typeof(bool)
            && (bool?)isPlayingProperty.GetValue(animation) == true;
        if (isPlaying)
        {
            animationType.GetMethod("Stop", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null)?.Invoke(animation, null);
        }

        object? clip = animationType.GetMethod("GetClip", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null)
            ?.Invoke(animation, new object[] { clipName });
        if (clip != null)
        {
            animationType.GetMethod("Play", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null)
                ?.Invoke(animation, new object[] { clipName });
        }
    }

    private static float GetAnimationClipLength(object? animation, string clipName)
    {
        if (animation == null)
        {
            return 0f;
        }

        object? clip = animation.GetType().GetMethod("GetClip", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null)
            ?.Invoke(animation, new object[] { clipName });
        if (clip == null)
        {
            return 0f;
        }

        PropertyInfo? lengthProperty = clip.GetType().GetProperty("length", BindingFlags.Instance | BindingFlags.Public);
        if (lengthProperty?.PropertyType == typeof(float))
        {
            return (float?)lengthProperty.GetValue(clip) ?? 0f;
        }

        return 0f;
    }

    private static object? GetAnimationObject(object instance)
    {
        return instance.GetType().GetField("Animation", BindingFlags.Instance | BindingFlags.Public)?.GetValue(instance);
    }

    private static bool IsAlive(UnityEngine.Object? unityObject)
    {
        return unityObject != null;
    }
}
#endif
