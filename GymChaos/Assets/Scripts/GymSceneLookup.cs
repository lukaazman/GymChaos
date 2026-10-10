using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Cached GameObject.Find for the static, uniquely named level objects that
/// steering code queries every physics step ("Rubber Floor", "Reception
/// desk"). GameObject.Find walks the whole hierarchy; with a dozen fighters
/// probing several directions per step it dominated roaming cost. A cached
/// object is used only while it still exists, is active and keeps its name,
/// so results match GameObject.Find.
/// </summary>
public static class GymSceneLookup
{
    private static readonly Dictionary<string, GameObject> cache =
        new Dictionary<string, GameObject>(System.StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
    }

    public static GameObject Find(string name)
    {
        if (cache.TryGetValue(name, out GameObject cached) && cached != null &&
            cached.activeInHierarchy && cached.name == name)
        {
            return cached;
        }
        GameObject found = GameObject.Find(name);
        if (found != null)
        {
            cache[name] = found;
        }
        else
        {
            cache.Remove(name);
        }
        return found;
    }
}
