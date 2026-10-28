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
    private static readonly Dictionary<string[], Collider[]> colliderCache =
        new Dictionary<string[], Collider[]>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cache.Clear();
        colliderCache.Clear();
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

    /// <summary>
    /// Colliders, inactive included, whose GameObject name is one of
    /// <paramref name="names"/>: the result of a name-filtered
    /// FindObjectsByType scan. Cached per names array (pass a static array);
    /// rescanned when a cached collider was destroyed or renamed. An empty
    /// result is not cached, so colliders built later are still found.
    /// </summary>
    public static Collider[] FindCollidersNamed(string[] names)
    {
        if (colliderCache.TryGetValue(names, out Collider[] cached) && AllStillNamed(cached, names))
        {
            return cached;
        }
        var found = new List<Collider>();
        foreach (Collider collider in Object.FindObjectsByType<Collider>(
            FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (System.Array.IndexOf(names, collider.gameObject.name) >= 0)
            {
                found.Add(collider);
            }
        }
        Collider[] result = found.ToArray();
        if (result.Length > 0)
        {
            colliderCache[names] = result;
        }
        else
        {
            colliderCache.Remove(names);
        }
        return result;
    }

    private static bool AllStillNamed(Collider[] colliders, string[] names)
    {
        foreach (Collider collider in colliders)
        {
            if (collider == null || System.Array.IndexOf(names, collider.gameObject.name) < 0)
            {
                return false;
            }
        }
        return true;
    }
}
