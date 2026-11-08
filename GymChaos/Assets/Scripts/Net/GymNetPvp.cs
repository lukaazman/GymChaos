using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Player against player. Only the host decides hits. A guest that punches
/// sends its aim ('a'); the host puts the puncher at the avatar position it
/// knows (never a claimed position), tests range and facing against every
/// other player, and sends the damage ('d') to the guest that was hit, or
/// applies it to its own player. The host's own punches go through the same test.
/// A punch from another player hurts like an enemy punch and can kill.
/// </summary>
public static class GymNetPvp
{
    public const char AttackTag = 'a';
    public const char DamageTag = 'd';
    public const float Range = 2.4f;
    // Target body: a vertical segment around the avatar root, this wide.
    public const float BodyRadius = 0.55f;
    public const float BodyBelowRoot = 1.0f;
    public const float BodyAboveRoot = 0.85f;
    public const float EyeAboveRoot = 0.6f;
    // Under the local punch cooldown (0.28 s) so a guest that spams punches is not throttled below the host.
    public const float CooldownSeconds = 0.2f;
    public const float MaxImpulse = 8f;
    public const float BaseDamage = 5f;
    public const float MaxDamage = 25f;
    private const float FacingCosine = 0.17f; // within about 80 degrees of where the puncher faces

    [Serializable]
    private sealed class Attack
    {
        public float dx, dy, dz;
    }

    [Serializable]
    private sealed class Damage
    {
        public float amount;
        public float ix, iy, iz;
    }

    /// <summary>A player left: its id may be reused, so forget its cooldown.</summary>
    public static void Forget(int id) => lastAttack.Remove(id);

    private static readonly Dictionary<int, float> lastAttack = new Dictionary<int, float>(GymNetClient.MaxPlayers);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => lastAttack.Clear();

    /// <summary>Does a punch from <paramref name="origin"/> along <paramref name="direction"/> reach a body standing at <paramref name="root"/>?</summary>
    public static bool Reaches(Vector3 origin, Vector3 direction, Vector3 root, out float distance)
    {
        distance = float.MaxValue;
        Vector3 bottom = root + Vector3.down * BodyBelowRoot;
        Vector3 top = root + Vector3.up * BodyAboveRoot;
        for (int i = 0; i <= 4; i++)
        {
            Vector3 point = Vector3.Lerp(bottom, top, i / 4f);
            float along = Vector3.Dot(point - origin, direction);
            if (along < 0f || along > Range) continue;
            float off = Vector3.Distance(point, origin + direction * along);
            if (off <= BodyRadius && along < distance) distance = along;
        }
        return distance < float.MaxValue;
    }

    /// <summary>The local player punched. Host: resolve it now. Guest: ask the host.</summary>
    public static void LocalPunch(Vector3 origin, Vector3 direction)
    {
        GymNetSession session = GymNetSession.Instance;
        if (session == null || session.Client.CurrentPhase != GymNetClient.Phase.Lobby) return;
        direction.Normalize();
        GymExperienceService progression = GymExperienceService.Active;
        if (session.Client.IsHost) Resolve(session, 0, origin, direction,
            progression != null ? progression.ScaleStrengthDamage(BaseDamage) : BaseDamage);
        else session.Client.Send(0, AttackTag + JsonUtility.ToJson(new Attack { dx = direction.x, dy = direction.y, dz = direction.z }));
    }

    /// <summary>True when the message was part of PvP.</summary>
    public static bool Handle(GymNetSession session, int from, string body, float now)
    {
        if (string.IsNullOrEmpty(body) || (body[0] != AttackTag && body[0] != DamageTag)) return false;
        if (body[0] == AttackTag) return HandleAttack(session, from, body, now);
        // Damage comes from the host only, and only a guest takes it.
        if (from != 0 || session.Client.IsHost) return true;
        Damage damage;
        try { damage = JsonUtility.FromJson<Damage>(body.Substring(1)); }
        catch (Exception) { return true; }
        if (damage == null || !Finite(damage.amount + damage.ix + damage.iy + damage.iz)) return true;
        Apply(session.LocalPlayer, Mathf.Clamp(damage.amount, 0f, MaxDamage),
            Vector3.ClampMagnitude(new Vector3(damage.ix, damage.iy, damage.iz), MaxImpulse));
        return true;
    }

    private static bool HandleAttack(GymNetSession session, int from, string body, float now)
    {
        if (!session.Client.IsHost) return true;
        if (lastAttack.TryGetValue(from, out float last) && now - last < CooldownSeconds) return true;
        lastAttack[from] = now;
        Attack attack;
        try { attack = JsonUtility.FromJson<Attack>(body.Substring(1)); }
        catch (Exception) { return true; }
        if (attack == null || !Finite(attack.dx + attack.dy + attack.dz)) return true;
        Vector3 direction = new Vector3(attack.dx, attack.dy, attack.dz);
        if (direction.sqrMagnitude < 0.0001f) return true;
        direction.Normalize();
        // No pose yet means the avatar still stands at the origin: nobody to punch from.
        if (!session.TryGetAvatar(from, out GymRemoteAvatar puncher) || puncher == null || puncher.PoseCount == 0 || puncher.IsCorpse) return true;
        // The puncher stands where the host sees it and must face the way it punches.
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude > 0.0001f && Vector3.Dot(flat.normalized, puncher.transform.forward) < FacingCosine) return true;
        // Damage from what the host knows of this player's character, never from the message.
        float strength = session.Join.LastKnown.TryGetValue(from, out GymCharacterSave save) ? save.progression.strengthRank : 0;
        float damage = BaseDamage * (1f + Mathf.Clamp(strength, 0, GymExperienceService.MaxStatRank) * 0.08f);
        Resolve(session, from, puncher.transform.position + Vector3.up * EyeAboveRoot, direction, damage);
        return true;
    }

    private static void Resolve(GymNetSession session, int attackerId, Vector3 origin, Vector3 direction, float damage)
    {
        damage = Mathf.Clamp(damage, 0f, MaxDamage);
        float best = float.MaxValue;
        int targetId = -1;
        PlayerMovement hostPlayer = session.LocalPlayer;
        if (attackerId != 0 && hostPlayer != null && !hostPlayer.IsDead &&
            Reaches(origin, direction, hostPlayer.transform.position, out float toHost) && toHost < best)
        {
            best = toHost;
            targetId = 0;
        }
        foreach (KeyValuePair<int, GymRemoteAvatar> pair in session.AvatarsById)
        {
            // A corpse cannot be hit again.
            if (pair.Key == attackerId || pair.Value == null || pair.Value.IsCorpse) continue;
            if (Reaches(origin, direction, pair.Value.transform.position, out float distance) && distance < best)
            {
                best = distance;
                targetId = pair.Key;
            }
        }
        if (targetId < 0) return;
        Vector3 impulse = Vector3.ProjectOnPlane(direction, Vector3.up) * 4f;
        if (targetId == 0) Apply(hostPlayer, damage, impulse);
        else session.Client.Send(targetId, DamageTag + JsonUtility.ToJson(new Damage { amount = damage, ix = impulse.x, iy = impulse.y, iz = impulse.z }));
    }

    private static void Apply(PlayerMovement player, float amount, Vector3 impulse)
    {
        if (player != null) player.ReceivePlayerPunch(amount, impulse);
    }

    private static bool Finite(float sum) => !float.IsNaN(sum) && !float.IsInfinity(sum);
}
