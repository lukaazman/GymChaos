using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Allocation-free definition of the members shown by the upper-left HUD.
/// The count is spatial: outdoor/store/vehicle actors are not members of the
/// active gym room until they re-enter its main or locker volume.
/// </summary>
public static class GymMemberRoster
{
    public const float BoundaryRefreshSeconds = 0.10f;

    public static int GetDisplayCount(PlayerMovement player)
    {
        int count = 0;
        if (IsLiveMemberRoomPosition(player, player != null ? player.transform.position : Vector3.zero))
        {
            count++;
        }

        IReadOnlyList<EnemyFighter> fighters = EnemyFighter.RegisteredFighters;
        for (int index = 0; index < fighters.Count; index++)
        {
            EnemyFighter fighter = fighters[index];
            if (fighter == null || !fighter.isActiveAndEnabled ||
                !fighter.gameObject.activeInHierarchy || fighter.IsDead ||
                fighter.IsPolice || fighter.IsPassive ||
                !fighter.CountsAsOpponent)
            {
                continue;
            }

            if (IsLiveMemberRoomPosition(null, fighter.transform.position))
            {
                count++;
            }
        }

        return count;
    }

    public static bool IsLiveMemberRoomPosition(
        PlayerMovement player, Vector3 position)
    {
        if (player != null && player.IsDead)
        {
            return false;
        }

        if (GymBackRoomBuilder.IsInsideRoom(position))
        {
            return true;
        }

        return GymInteriorBuilder.TryGetMainGymBounds(out Bounds gymBounds) &&
            ContainsRoomPosition(gymBounds, position);
    }

    private static bool ContainsRoomPosition(Bounds bounds, Vector3 position)
    {
        return position.x >= bounds.min.x && position.x <= bounds.max.x &&
            position.y >= bounds.min.y - 0.75f &&
            position.y <= bounds.max.y + 0.75f &&
            position.z >= bounds.min.z && position.z <= bounds.max.z;
    }
}
