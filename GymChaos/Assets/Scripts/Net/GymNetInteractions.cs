using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// World interactions go through the host: a guest asks ('i'), the host checks
/// the request and decides, and the result ('x') goes to every guest. Today
/// that is glass breaking. The host's own interactions are broadcast the same way.
/// </summary>
public static class GymNetInteractions
{
    public const char RequestTag = 'i';
    public const char ResultTag = 'x';
    // A guest may only break glass near its own avatar, and not faster than a punch.
    public const float MaxReach = 15f;
    public const float MinRequestIntervalSeconds = 0.25f;

    [Serializable]
    private sealed class GlassHit
    {
        public string id;
        public Vector3 point;
        public Vector3 normal;
        public Vector3 velocity;
        public float speed;
        public float multiplier;
    }

    private static readonly Dictionary<int, float> lastRequest = new Dictionary<int, float>(GymNetClient.MaxPlayers);

    /// <summary>A player left: its id may be reused, so forget its request time.</summary>
    public static void Forget(int id) => lastRequest.Remove(id);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => lastRequest.Clear();

    /// <summary>Guest: asks the host to break this panel. The panel stays whole until the host says so.</summary>
    public static void RequestGlass(string panelId, Vector3 point, Vector3 normal, Vector3 velocity, float speed, float multiplier)
    {
        GymNetSession session = GymNetSession.Instance;
        if (session == null) return;
        session.Client.Send(0, RequestTag + "glass|" + Encode(panelId, point, normal, velocity, speed, multiplier));
    }

    /// <summary>Host: tells every guest a panel broke here.</summary>
    public static void BroadcastGlass(string panelId, Vector3 point, Vector3 normal, Vector3 velocity, float speed, float multiplier)
    {
        GymNetSession session = GymNetSession.Instance;
        if (session == null || session.Client.Peers.Count == 0) return;
        session.Client.Send(-1, ResultTag + "glass|" + Encode(panelId, point, normal, velocity, speed, multiplier));
    }

    /// <summary>Host: a panel was repaired (Jolly Dog); tell every guest.</summary>
    public static void BroadcastRepair(string panelId)
    {
        GymNetSession session = GymNetSession.Instance;
        if (session == null || !session.Client.IsHost || session.Client.Peers.Count == 0) return;
        session.Client.Send(-1, ResultTag + RepairPrefix + panelId);
    }

    private const string RepairPrefix = "repair|";

    private static string Encode(string id, Vector3 p, Vector3 n, Vector3 v, float speed, float multiplier) =>
        JsonUtility.ToJson(new GlassHit { id = id, point = p, normal = n, velocity = v, speed = speed, multiplier = multiplier });

    /// <summary>True when the message was an interaction request or result.</summary>
    public static bool Handle(GymNetSession session, int from, string body, float now)
    {
        if (string.IsNullOrEmpty(body) || (body[0] != RequestTag && body[0] != ResultTag)) return false;
        bool request = body[0] == RequestTag;
        // Requests are for the host, results are from the host.
        if (request != session.Client.IsHost || (!request && from != 0)) return true;
        if (!request && string.Compare(body, 1, RepairPrefix, 0, RepairPrefix.Length, StringComparison.Ordinal) == 0)
        {
            string id = body.Substring(1 + RepairPrefix.Length);
            if (id.Length > 0 && id.Length <= GymNetCharacter.MaxTextLength * 2) GlassShatterPanel.FindById(id)?.ApplyNetworkRepair();
            return true;
        }
        const string glass = "glass|";
        if (string.Compare(body, 1, glass, 0, glass.Length, StringComparison.Ordinal) != 0) return true;
        // Throttle before parsing: a flood of requests costs the host nothing.
        if (request)
        {
            if (lastRequest.TryGetValue(from, out float last) && now - last < MinRequestIntervalSeconds) return true;
            lastRequest[from] = now;
        }
        GlassHit hit;
        try { hit = JsonUtility.FromJson<GlassHit>(body.Substring(1 + glass.Length)); }
        catch (Exception) { return true; }
        if (hit == null || string.IsNullOrEmpty(hit.id) || hit.id.Length > GymNetCharacter.MaxTextLength * 2 || !Finite(hit)) return true;

        if (request)
        {
            if (!session.TryGetAvatar(from, out GymRemoteAvatar avatar) || avatar == null ||
                Vector3.Distance(avatar.transform.position, hit.point) > MaxReach) return true;
        }
        GlassShatterPanel panel = GlassShatterPanel.FindById(hit.id);
        // The impact must be on the panel itself, within reach of the asking player.
        if (request && panel != null && session.TryGetAvatar(from, out GymRemoteAvatar asker) && asker != null &&
            Vector3.Distance(asker.transform.position, panel.transform.position) > MaxReach) return true;
        if (panel == null || !panel.ApplyNetworkShatter(hit.point, hit.normal, hit.velocity, Mathf.Clamp(hit.speed, 0f, 40f),
                Mathf.Clamp(hit.multiplier, 1f, 3f))) return true;
        if (request) BroadcastGlass(hit.id, hit.point, hit.normal, hit.velocity, hit.speed, hit.multiplier);
        return true;
    }

    private static bool Finite(GlassHit hit) =>
        Ok(hit.point) && Ok(hit.normal) && Ok(hit.velocity) && !float.IsNaN(hit.speed) && !float.IsNaN(hit.multiplier);

    private static bool Ok(Vector3 v) =>
        !float.IsNaN(v.x + v.y + v.z) && Mathf.Abs(v.x) < 100000f && Mathf.Abs(v.y) < 100000f && Mathf.Abs(v.z) < 100000f;
}
