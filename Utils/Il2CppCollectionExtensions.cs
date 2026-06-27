using System.Collections.Generic;

namespace DedicatedServerMod.Organisations.Utils;

internal static class Il2CppCollectionExtensions
{
#if IL2CPP
    public static IEnumerable<T> AsManagedEnumerable<T>(this Il2CppSystem.Collections.Generic.List<T>? list)
    {
        if (list == null)
        {
            yield break;
        }

        for (int index = 0; index < list.Count; index++)
        {
            yield return list[index];
        }
    }

    public static List<T> ToManagedList<T>(this Il2CppSystem.Collections.Generic.List<T>? list)
    {
        return new List<T>(list.AsManagedEnumerable());
    }
#else
    public static IEnumerable<T> AsManagedEnumerable<T>(this IEnumerable<T>? items)
    {
        return items ?? System.Array.Empty<T>();
    }

    public static List<T> ToManagedList<T>(this List<T>? items)
    {
        return items ?? new List<T>();
    }
#endif
}
