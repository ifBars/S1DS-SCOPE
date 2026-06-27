#if CLIENT
using System;
using HarmonyLib;
#if IL2CPP
using Il2CppScheduleOne.Audio;
using Il2CppScheduleOne.UI.ATM;
#else
using ScheduleOne.Audio;
using ScheduleOne.UI.ATM;
#endif
using UnityEngine;
using UnityEngine.UI;

namespace DedicatedServerMod.Organisations.Client.UI;

internal static class AtmInterfaceAccess
{
    private static readonly System.Reflection.FieldInfo MenuScreenField = AccessTools.Field(typeof(ATMInterface), "menuScreen")
        ?? throw new MissingFieldException(typeof(ATMInterface).FullName, "menuScreen");
    private static readonly System.Reflection.FieldInfo ProcessingScreenField = AccessTools.Field(typeof(ATMInterface), "processingScreen")
        ?? throw new MissingFieldException(typeof(ATMInterface).FullName, "processingScreen");
    private static readonly System.Reflection.FieldInfo SuccessScreenField = AccessTools.Field(typeof(ATMInterface), "successScreen")
        ?? throw new MissingFieldException(typeof(ATMInterface).FullName, "successScreen");
    private static readonly System.Reflection.FieldInfo SuccessSubtitleField = AccessTools.Field(typeof(ATMInterface), "successScreenSubtitle")
        ?? throw new MissingFieldException(typeof(ATMInterface).FullName, "successScreenSubtitle");
    private static readonly System.Reflection.FieldInfo CompleteSoundField = AccessTools.Field(typeof(ATMInterface), "CompleteSound")
        ?? throw new MissingFieldException(typeof(ATMInterface).FullName, "CompleteSound");

    public static RectTransform GetMenuScreen(ATMInterface atmInterface)
    {
        return GetFieldValue<RectTransform>(atmInterface, MenuScreenField);
    }

    public static RectTransform GetProcessingScreen(ATMInterface atmInterface)
    {
        return GetFieldValue<RectTransform>(atmInterface, ProcessingScreenField);
    }

    public static RectTransform GetSuccessScreen(ATMInterface atmInterface)
    {
        return GetFieldValue<RectTransform>(atmInterface, SuccessScreenField);
    }

    public static void SetSuccessSubtitle(ATMInterface atmInterface, string message)
    {
        Text subtitle = GetFieldValue<Text>(atmInterface, SuccessSubtitleField);
        subtitle.text = message ?? string.Empty;
    }

    public static void PlayCompleteSound(ATMInterface atmInterface)
    {
        AudioSourceController controller = GetFieldValue<AudioSourceController>(atmInterface, CompleteSoundField);
        controller?.Play();
    }

    private static T GetFieldValue<T>(ATMInterface atmInterface, System.Reflection.FieldInfo field)
        where T : class
    {
        if (atmInterface == null)
        {
            throw new ArgumentNullException(nameof(atmInterface));
        }

        return field.GetValue(atmInterface) as T
            ?? throw new InvalidOperationException($"ATMInterface field '{field.Name}' was not a {typeof(T).Name}.");
    }
}
#endif
