using System;

namespace DedicatedServerMod.Organisations.Utils;

internal static class CrossRuntime
{
    public static Type Of<T>()
    {
        return typeof(T);
    }

    public static bool TryCast<T>(object? instance, out T? result) where T : class
    {
        result = instance as T;
        return result != null;
    }
}
