using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player in a car seat (the race buggy). <see cref="GymVehicleDriverSeat"/>
/// drives the authored player body (<see cref="PlayerHandRig"/>, whose full
/// body renders on the mirror layer 29) through this adapter exactly as it
/// drives a visitor: same fit, rise plan, feet over the floor and door with
/// sounds. While seated the player is cinematic-locked, the character
/// controller is off and the player root follows the seat anchor.
/// </summary>
[DefaultExecutionOrder(110)]
public sealed class PlayerVehicleOccupant : MonoBehaviour, IVehicleSeatOccupant
{
    private PlayerMovement player;
    private CharacterController controller;
    private PlayerHandRig handRig;
    // On the ground under the capsule: the actor root the seat places.
    private Transform root;
    private Transform rideAnchor;
    private bool seated;
    private bool controllerWasEnabled;
    private float moveAmount;
    private BodybuilderEnemyVisual.Rig rig;
    private Transform rigModel;
    private float bodyHeight;

    public static PlayerVehicleOccupant Attach(PlayerMovement player)
    {
        if (player == null) return null;
        PlayerVehicleOccupant occupant = player.GetComponent<PlayerVehicleOccupant>();
        if (occupant == null) occupant = player.gameObject.AddComponent<PlayerVehicleOccupant>();
        occupant.player = player;
        occupant.controller = player.GetComponent<CharacterController>();
        return occupant;
    }

    public PlayerHandRig HandRig
    {
        get
        {
            // A class change replaces the avatar rig.
            if (handRig == null || !handRig.gameObject.activeInHierarchy)
            {
                handRig = GetComponentInChildren<PlayerHandRig>(false);
            }
            return handRig;
        }
    }

    public Transform Root
    {
        get
        {
            if (root == null)
            {
                root = new GameObject("Player Vehicle Seat Root").transform;
                root.SetParent(transform, false);
            }
            float feet = controller != null ? controller.center.y - controller.height * 0.5f : -1f;
            root.localPosition = new Vector3(0f, feet, 0f);
            root.localRotation = Quaternion.identity;
            return root;
        }
    }

    public string OccupantName => "Player";
    public bool IsDead => player != null && player.IsDead;
    public bool IsInVehicleSeatMode => seated;
    public bool IsSeated => seated && rideAnchor != null;
    public bool HasVehicleClips => HandRig != null && HandRig.HasVehicleClips;
    public float CarExitClipLength => HandRig != null ? HandRig.CarExitClipLength : 0f;
    public bool GroundOffsetsReady => true;
    public MixamoScanRetargetAnimator.VehiclePose CurrentVehiclePose =>
        HandRig != null ? HandRig.CurrentVehiclePose : MixamoScanRetargetAnimator.VehiclePose.None;

    public BodybuilderEnemyVisual.Rig BodyRig
    {
        get
        {
            PlayerHandRig hands = HandRig;
            Transform model = hands != null ? hands.RuntimeModelRoot : null;
            if (model == null) return null;
            if (rig == null || rigModel != model) BuildRig(hands, model);
            return rig;
        }
    }

    public void BeginVehicleSeat(Transform anchor)
    {
        if (anchor == null || IsDead) return;
        if (!seated)
        {
            controllerWasEnabled = controller != null && controller.enabled;
            player?.SetCinematicLock(true);
            if (controller != null) controller.enabled = false;
        }
        rideAnchor = anchor;
        seated = true;
        moveAmount = 0f;
        Follow();
        Physics.SyncTransforms();
    }

    public void EndVehicleSeat(Vector3 groundPosition, Quaternion rotation)
    {
        if (!seated) return;
        rideAnchor = null;
        seated = false;
        moveAmount = 0f;
        HandRig?.SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose.None, 0f, Vector3.zero);
        Quaternion yaw = Quaternion.Euler(0f, rotation.eulerAngles.y, 0f);
        PlaceRoot(groundPosition, yaw);
        if (controller != null) controller.enabled = controllerWasEnabled;
        player?.SetCinematicLock(false);
        Physics.SyncTransforms();
    }

    /// <summary>Stands the player (not seated) with Root on <paramref name="groundPosition"/>.</summary>
    public void PlaceStanding(Vector3 groundPosition, Quaternion rotation)
    {
        if (seated) return;
        bool enabled = controller != null && controller.enabled;
        if (controller != null) controller.enabled = false;
        PlaceRoot(groundPosition, Quaternion.Euler(0f, rotation.eulerAngles.y, 0f));
        if (controller != null) controller.enabled = enabled;
        Physics.SyncTransforms();
    }

    public bool SetVehiclePose(MixamoScanRetargetAnimator.VehiclePose pose, float exitPhase,
        Vector3 rootWorldOffset, float scale = 1f) =>
        HandRig != null && HandRig.SetVehiclePose(pose, exitPhase, rootWorldOffset, scale);

    public bool SampleVehiclePoseNow(MixamoScanRetargetAnimator.VehiclePose pose, float normalized, float scale = 1f) =>
        HandRig != null && HandRig.SampleVehiclePoseNow(pose, normalized, scale);

    public bool SampleWalkingPoseNow() => HandRig != null && HandRig.SampleWalkingPoseNow();

    public void SetMoving(bool moving, float normalizedSpeed)
    {
        moveAmount = moving ? Mathf.Clamp01(normalizedSpeed) : 0f;
    }

    public float BakedCrown(float radius)
    {
        BodybuilderEnemyVisual.Rig body = BodyRig;
        return body?.Head != null
            ? GymVehicleDriverSeat.BakedSkinCrown(Root, HandRig.RuntimeModelRoot, body.Head, radius)
            : 0f;
    }

    // No hitbox rig on the player: capsules along the bones, sized to the
    // authored body (radii for a 1.8 m figure, scaled to its height).
    public void GetBodyVolumes(List<(Vector3 a, Vector3 b, float radius)> into)
    {
        into.Clear();
        BodybuilderEnemyVisual.Rig body = BodyRig;
        if (body == null) return;
        float k = Mathf.Max(0.5f, bodyHeight / 1.8f);
        void Add(Transform a, Transform b, float r)
        {
            if (a != null && b != null) into.Add((a.position, b.position, r * k));
        }
        Add(body.Hips, body.Chest, 0.16f);
        Add(body.Chest, body.Neck != null ? body.Neck : body.Head, 0.17f);
        Add(body.Head, body.Head, 0.115f);
        Add(body.LeftUpperArm, body.LeftForearm, 0.065f);
        Add(body.RightUpperArm, body.RightForearm, 0.065f);
        Add(body.LeftForearm, body.LeftHand, 0.055f);
        Add(body.RightForearm, body.RightHand, 0.055f);
        Add(body.LeftHand, body.LeftHand, 0.05f);
        Add(body.RightHand, body.RightHand, 0.05f);
        Add(body.LeftThigh, body.LeftShin, 0.09f);
        Add(body.RightThigh, body.RightShin, 0.09f);
        Add(body.LeftShin, body.LeftFoot, 0.065f);
        Add(body.RightShin, body.RightFoot, 0.065f);
        Add(body.LeftFoot, body.LeftFoot, 0.055f);
        Add(body.RightFoot, body.RightFoot, 0.055f);
    }

    private void Update()
    {
        if (!seated) return;
        Follow();
        // PlayerMovement does not tick the body under the cinematic lock.
        HandRig?.Tick(moveAmount, 0f, false, true);
    }

    private void Follow()
    {
        if (rideAnchor == null) return;
        PlaceRoot(rideAnchor.position, rideAnchor.rotation);
    }

    // Moves the player so that Root lands on `position` with `rotation`.
    private void PlaceRoot(Vector3 position, Quaternion rotation)
    {
        Vector3 rootLocal = Root.localPosition;
        transform.SetPositionAndRotation(position - rotation * Vector3.Scale(rootLocal, transform.lossyScale), rotation);
    }

    private void BuildRig(PlayerHandRig hands, Transform model)
    {
        Transform[] bones = model.GetComponentsInChildren<Transform>(true);
        Transform Find(params string[] names) => FindBone(bones, names);
        rigModel = model;
        rig = new BodybuilderEnemyVisual.Rig
        {
            Root = hands.RuntimeAnimationRootBone,
            Hips = hands.RuntimeAnimationRootBone,
            Spine = Find("DEF-spine.001"),
            Chest = Find("DEF-spine.003", "DEF-spine.002"),
            Neck = Find("DEF-spine.004"),
            Head = hands.RuntimeHead,
            LeftShoulder = Find("DEF-shoulder.L"),
            LeftUpperArm = Find("DEF-upper_arm.L"),
            LeftForearm = Find("DEF-forearm.L"),
            LeftHand = hands.RuntimeLeftHand,
            RightShoulder = Find("DEF-shoulder.R"),
            RightUpperArm = Find("DEF-upper_arm.R"),
            RightForearm = Find("DEF-forearm.R"),
            RightHand = hands.RuntimeRightHand,
            LeftThigh = hands.RuntimeLeftThigh,
            LeftShin = Find("DEF-shin.L"),
            LeftFoot = Find("DEF-foot.L"),
            RightThigh = hands.RuntimeRightThigh,
            RightShin = Find("DEF-shin.R"),
            RightFoot = Find("DEF-foot.R"),
        };
        bodyHeight = hands.RuntimeVisibleHeight > 0.5f ? hands.RuntimeVisibleHeight
            : ExternalRiggedCharacterVisual.StandardGameplayHeight;
        Debug.Log(
            $"GYMCHAOS_PLAYER_VEHICLE_RIG hips={rig.Hips?.name} chest={rig.Chest?.name} head={rig.Head?.name} " +
            $"hands={rig.LeftHand?.name}/{rig.RightHand?.name} forearms={rig.LeftForearm?.name}/{rig.RightForearm?.name} " +
            $"shins={rig.LeftShin?.name}/{rig.RightShin?.name} feet={rig.LeftFoot?.name}/{rig.RightFoot?.name} " +
            $"height={bodyHeight:F2} clips={(HasVehicleClips ? 1 : 0)}", this);
    }

    private static string Normalize(string name)
    {
        var characters = new System.Text.StringBuilder(name.Length);
        foreach (char c in name.ToLowerInvariant())
            if (char.IsLetterOrDigit(c)) characters.Append(c);
        return characters.ToString();
    }

    private static Transform FindBone(Transform[] bones, params string[] names)
    {
        foreach (string name in names)
        {
            string wanted = Normalize(name);
            foreach (Transform bone in bones)
                if (Normalize(bone.name) == wanted) return bone;
        }
        foreach (string name in names)
        {
            string wanted = Normalize(name);
            foreach (Transform bone in bones)
                if (Normalize(bone.name).EndsWith(wanted, StringComparison.Ordinal)) return bone;
        }
        return null;
    }
}
