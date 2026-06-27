#if IL2CPP
using Il2CppScheduleOne.DevUtilities;
using Il2CppScheduleOne.Quests;
using GUIDManager = Il2Cpp.GUIDManager;
using Guid = Il2CppSystem.Guid;
#else
using ScheduleOne.DevUtilities;
using ScheduleOne.Quests;
#endif
using System;

namespace DedicatedServerMod.Organisations.Utils;

internal static class QuestScopeRules
{
    private const string VanillaManagedQuestTypeName = "Quest_WelcomeToHylandPoint";

    public static bool ShouldVirtualizeQuest(Quest? quest)
    {
        return quest != null && !string.Equals(quest.GetType().Name, VanillaManagedQuestTypeName, StringComparison.Ordinal);
    }

    public static bool ShouldVirtualizeQuest(string? guid)
    {
        if (string.IsNullOrWhiteSpace(guid) || !Guid.TryParse(guid, out Guid parsedGuid))
        {
            return true;
        }

        Quest? quest = GUIDManager.GetObject<Quest>(parsedGuid);
        return quest == null || ShouldVirtualizeQuest(quest);
    }
}
