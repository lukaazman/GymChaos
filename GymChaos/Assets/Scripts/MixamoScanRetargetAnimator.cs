using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Samples a hidden copy of the same per-enemy Mixamo FBX that is rendered by
/// ExternalRiggedCharacterVisual. Only clip rotation deltas are copied to the
/// visible rig, keeping each scan's T-pose bind/material layout intact.
/// </summary>
public sealed class MixamoScanRetargetAnimator : MonoBehaviour
{
    public enum MotionState
    {
        Uninitialized,
        Idle,
        Running,
        Punching,
        Flying,
        Celebration,
        Downed
    }

    private sealed class BonePair
    {
        public Transform Source;
        public Transform Target;
        public Quaternion SourceRestInModel;
        public Quaternion TargetRestInOwner;
    }

    private BodybuilderEnemyVisual.Rig rig;
    private GameObject sourceModel;
    private AnimationClip runClip;
    private AnimationClip punchClip;
    private AnimationClip idleClip;
    private AnimationClip flyClip;
    private AnimationClip celebrationClip;
    private BonePair[] pairs;
    private Quaternion targetRootRestInOwner;
    private bool hasTargetRootRest;
    private bool moving;
    private bool flying;
    private bool downed;
    private float speed01;
    private float runTime;
    private float idleTime;
    private float flightTime;
    private float attackTime = -1f;
    private bool punchContactSent;
    private bool celebrating;
    private Vector3 punchDirection = Vector3.forward;
    private Vector3 punchTargetPosition;
    private bool hasPunchTarget;
    private MotionState lastMotionState = MotionState.Uninitialized;

    public bool HasRunClip => runClip != null;
    public bool HasPunchClip => punchClip != null;
    public bool HasIdleClip => idleClip != null;
    public bool HasProceduralIdle => idleClip == null && pairs != null;
    public bool HasFlightPose => rig != null && rig.RightUpperArm != null &&
        rig.RightForearm != null && rig.RightHand != null;
    public bool HasFlyClip => flyClip != null;
    public bool HasCelebrationClip => celebrationClip != null;
    public bool IsPunchComplete => attackTime >= 0.72f;
    public MotionState CurrentState => lastMotionState;
    public MotionState LastMotionState => lastMotionState;

    public bool Configure(BodybuilderIdentity identity, BodybuilderEnemyVisual.Rig bodyRig)
    {
        string resourcePath = ResourcePath(identity);
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        AnimationClip[] clips = Resources.LoadAll<AnimationClip>(resourcePath);
        idleClip = FindClip(clips, "idle");
        runClip = FindClip(clips, "run");
        punchClip = FindClip(clips, "punch");
        flyClip = identity == BodybuilderIdentity.Goku ? FindClip(clips, "fly") : null;
        celebrationClip = FindClip(clips, "celebration");
        if (prefab == null || runClip == null || punchClip == null || bodyRig == null)
        {
            return false;
        }

        rig = bodyRig;
        if (rig.Root != null)
        {
            targetRootRestInOwner = Quaternion.Inverse(transform.rotation) * rig.Root.rotation;
            hasTargetRootRest = true;
        }
        sourceModel = Instantiate(prefab, transform);
        sourceModel.name = identity + " Hidden Mixamo Motion Source";
        sourceModel.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        Renderer[] sourceRenderers = sourceModel.GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < sourceRenderers.Length; i++)
        {
            sourceRenderers[i].enabled = false;
            sourceRenderers[i].forceRenderingOff = true;
        }
        Animator[] animators = sourceModel.GetComponentsInChildren<Animator>(true);
        for (int i = 0; i < animators.Length; i++)
        {
            animators[i].enabled = false;
        }

        Transform[] sourceBones = sourceModel.GetComponentsInChildren<Transform>(true);
        List<BonePair> mapped = new List<BonePair>();
        AddPair(mapped, sourceBones, rig.Hips, "hips");
        AddPair(mapped, sourceBones, rig.Spine, "spine");
        AddPair(mapped, sourceBones, rig.Chest, "spine2", "spine1");
        AddPair(mapped, sourceBones, rig.Head, "head");
        AddPair(mapped, sourceBones, rig.LeftShoulder, "leftshoulder");
        AddPair(mapped, sourceBones, rig.LeftUpperArm, "leftarm");
        AddPair(mapped, sourceBones, rig.LeftForearm, "leftforearm");
        AddPair(mapped, sourceBones, rig.LeftHand, "lefthand");
        AddPair(mapped, sourceBones, rig.RightShoulder, "rightshoulder");
        AddPair(mapped, sourceBones, rig.RightUpperArm, "rightarm");
        AddPair(mapped, sourceBones, rig.RightForearm, "rightforearm");
        AddPair(mapped, sourceBones, rig.RightHand, "righthand");
        AddPair(mapped, sourceBones, rig.LeftThigh, "leftupleg");
        AddPair(mapped, sourceBones, rig.LeftShin, "leftleg");
        AddPair(mapped, sourceBones, rig.LeftFoot, "leftfoot");
        AddPair(mapped, sourceBones, rig.RightThigh, "rightupleg");
        AddPair(mapped, sourceBones, rig.RightShin, "rightleg");
        AddPair(mapped, sourceBones, rig.RightFoot, "rightfoot");
        pairs = mapped.ToArray();
        RestoreTargetRest();
        Debug.Log(
            $"GYMCHAOS_MIXAMO_SCAN_RETARGET_OK identity={identity} bones={pairs.Length} " +
            $"idle={idleClip?.name ?? "procedural"} run={runClip.name} punch={punchClip.name}", this);
        return pairs.Length >= 14;
    }

    public void SetMoving(bool shouldMove, float normalizedSpeed = 1f)
    {
        if (attackTime >= 0.72f)
        {
            // Let FixedUpdate consume the contact at the end pose first; the
            // next movement command can then return to Idle/Run cleanly.
            attackTime = -1f;
            punchContactSent = false;
        }
        moving = shouldMove;
        speed01 = shouldMove ? Mathf.Clamp01(normalizedSpeed) : 0f;
        celebrating = false;
        lastMotionState = shouldMove ? MotionState.Running : MotionState.Idle;
    }

    public void SetFlying(bool shouldFly)
    {
        if (shouldFly && !flying)
        {
            flightTime = 0f;
        }
        flying = shouldFly;
        celebrating = false;
        lastMotionState = shouldFly ? MotionState.Flying : MotionState.Idle;
        if (flying)
        {
            moving = false;
        }
    }

    public void TriggerAttack()
    {
        attackTime = 0f;
        punchContactSent = false;
        celebrating = false;
        hasPunchTarget = false;
        lastMotionState = MotionState.Punching;
        punchDirection = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        if (punchDirection.sqrMagnitude < 0.001f)
        {
            punchDirection = Vector3.forward;
        }
    }

    public void SetPunchDirection(Vector3 direction)
    {
        Vector3 planar = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (planar.sqrMagnitude > 0.0001f)
        {
            punchDirection = planar.normalized;
        }
    }

    public void SetPunchTarget(Vector3 worldPosition)
    {
        punchTargetPosition = worldPosition;
        hasPunchTarget = true;
    }

    public void TriggerCelebration()
    {
        if (!downed)
        {
            celebrating = true;
            flying = false;
            moving = false;
            attackTime = -1f;
            lastMotionState = MotionState.Celebration;
        }
    }

    public bool TryConsumePunchContact(out Transform leftHand, out Transform rightHand)
    {
        leftHand = rig != null ? rig.LeftHand : null;
        rightHand = rig != null ? rig.RightHand : null;
        if (attackTime < 0f || punchContactSent || punchClip == null ||
            attackTime / Mathf.Max(0.01f, 0.72f) < 0.54f)
        {
            return false;
        }
        punchContactSent = true;
        return true;
    }

    public void SetDowned(bool isDowned)
    {
        downed = isDowned;
        if (downed)
        {
            moving = false;
            flying = false;
            celebrating = false;
            attackTime = -1f;
            lastMotionState = MotionState.Downed;
        }
        else if (lastMotionState == MotionState.Downed)
        {
            lastMotionState = MotionState.Idle;
        }
    }

    private void LateUpdate()
    {
        if (sourceModel == null || pairs == null)
        {
            return;
        }
        RestoreTargetRest();
        if (downed)
        {
            lastMotionState = MotionState.Downed;
            return;
        }
        if (celebrating)
        {
            lastMotionState = MotionState.Celebration;
            if (celebrationClip != null)
            {
                SampleAndApply(
                    celebrationClip,
                    Time.time % Mathf.Max(0.01f, celebrationClip.length - 0.001f),
                    1f);
            }
            return;
        }
        if (flying)
        {
            lastMotionState = MotionState.Flying;
            flightTime += Time.deltaTime;
            if (flyClip != null)
            {
                SampleAndApply(
                    flyClip,
                    flightTime % Mathf.Max(0.01f, flyClip.length - 0.001f),
                    1f);
            }
            else
            {
                ApplyFlightPose(flightTime);
            }
            return;
        }

        AnimationClip clip = null;
        float sampleTime = 0f;
        float influence = 0.72f;
        bool idleState = false;
        float punchNormalized = 0f;
        if (attackTime >= 0f)
        {
            lastMotionState = MotionState.Punching;
            attackTime += Time.deltaTime;
            punchNormalized = Mathf.Clamp01(attackTime / 0.72f);
            clip = punchClip;
            sampleTime = punchNormalized * Mathf.Max(0.01f, clip.length - 0.001f);
            // The complete shoulder-to-hand chain is now mapped. Applying the
            // full delta keeps the imported reach instead of a half-arm pose.
            influence = 1f;
        }
        else if (moving)
        {
            lastMotionState = MotionState.Running;
            runTime += Time.deltaTime * Mathf.Lerp(0.75f, 1.35f, speed01);
            clip = runClip;
            sampleTime = runTime % Mathf.Max(0.01f, clip.length - 0.001f);
        }
        else if (idleClip != null)
        {
            lastMotionState = MotionState.Idle;
            idleState = true;
            idleTime += Time.deltaTime;
            clip = idleClip;
            sampleTime = idleTime % Mathf.Max(0.01f, clip.length - 0.001f);
            influence = 1.0f;
        }
        if (clip == null)
        {
            lastMotionState = MotionState.Idle;
            idleTime += Time.deltaTime;
            ApplyProceduralIdle(idleTime);
            return;
        }

        SampleAndApply(clip, sampleTime, influence);
        if (idleState)
        {
            ClampIdleGrounding();
        }
        else if (lastMotionState == MotionState.Punching)
        {
            ClampPunchFacing();
            ApplyPunchReach(punchNormalized);
        }
    }

    private void SampleAndApply(AnimationClip clip, float sampleTime, float influence)
    {
        Vector3 stablePosition = sourceModel.transform.localPosition;
        Quaternion stableRotation = sourceModel.transform.localRotation;
        Vector3 stableScale = sourceModel.transform.localScale;
        clip.SampleAnimation(sourceModel, sampleTime);
        sourceModel.transform.localPosition = stablePosition;
        sourceModel.transform.localRotation = stableRotation;
        sourceModel.transform.localScale = stableScale;
        ApplyRetargetedPose(influence);
    }

    private void ClampIdleGrounding()
    {
        // The downloaded Idle has a small forward root pitch and heel lift.
        // Restore the complete axial/lower-body chain to this scan's own
        // upright rest pose, rather than inheriting a shared example pose.
        RestoreTargetRestRotation(rig.Root);
        RestoreTargetRestRotation(rig.Hips);
        RestoreTargetRestRotation(rig.Spine);
        RestoreTargetRestRotation(rig.Chest);
        RestoreTargetRestRotation(rig.Head);
        RestoreTargetRestRotation(rig.LeftThigh);
        RestoreTargetRestRotation(rig.LeftShin);
        RestoreTargetRestRotation(rig.LeftFoot);
        RestoreTargetRestRotation(rig.RightThigh);
        RestoreTargetRestRotation(rig.RightShin);
        RestoreTargetRestRotation(rig.RightFoot);
    }

    private void RestoreTargetRestRotation(Transform target)
    {
        if (target == null || pairs == null)
        {
            return;
        }
        if (target == rig.Root && hasTargetRootRest)
        {
            target.rotation = transform.rotation * targetRootRestInOwner;
            return;
        }
        for (int i = 0; i < pairs.Length; i++)
        {
            if (pairs[i].Target == target)
            {
                target.rotation = transform.rotation * pairs[i].TargetRestInOwner;
                return;
            }
        }
    }

    private void ClampPunchFacing()
    {
        if (rig == null || punchDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        // The downloaded Punch clip contains a lateral torso turn. Enemies
        // are already placed facing their target, so keep the torso/head and
        // shoulder caps on that facing axis and solve the actual reach below.
        BlendTargetToRestRotation(rig.Hips, 1f);
        BlendTargetToRestRotation(rig.Spine, 1f);
        BlendTargetToRestRotation(rig.Chest, 1f);
        BlendTargetToRestRotation(rig.LeftShoulder, 1f);
        BlendTargetToRestRotation(rig.RightShoulder, 1f);
        BlendTargetToRestRotation(rig.Head, 1f);
    }

    private void BlendTargetToRestRotation(Transform target, float blend)
    {
        if (target == null || pairs == null || blend <= 0f)
        {
            return;
        }

        for (int i = 0; i < pairs.Length; i++)
        {
            if (pairs[i].Target == target)
            {
                Quaternion rest = transform.rotation * pairs[i].TargetRestInOwner;
                target.rotation = Quaternion.Slerp(
                    target.rotation, rest, Mathf.Clamp01(blend));
                return;
            }
        }
    }

    private void ApplyPunchReach(float normalized)
    {
        if (normalized < 0.12f || rig == null || punchDirection.sqrMagnitude < 0.0001f)
        {
            return;
        }

        SelectPunchArm(out Transform upperArm, out Transform forearm, out Transform hand);
        if (upperArm == null || forearm == null || hand == null)
        {
            return;
        }

        float upperLength = Vector3.Distance(upperArm.position, forearm.position);
        float forearmLength = Vector3.Distance(forearm.position, hand.position);
        float reach = Mathf.Max(0.35f, upperLength + forearmLength);
        float blend = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.48f, normalized));
        Vector3 aimDirection = punchDirection.normalized;
        float aimDistance = reach * 0.98f;
        if (hasPunchTarget)
        {
            Vector3 targetDelta = Vector3.ProjectOnPlane(
                punchTargetPosition - upperArm.position, Vector3.up);
            if (targetDelta.sqrMagnitude > 0.0001f)
            {
                // Aim from the actual shoulder at the player position. This
                // removes the old shoulder-plus-forward offset that made the
                // punch visibly miss to the side from the player's view.
                aimDirection = targetDelta.normalized;
                aimDistance = Mathf.Min(aimDistance, targetDelta.magnitude);
            }
        }
        Vector3 desired = upperArm.position + aimDirection * Mathf.Max(0.35f, aimDistance);
        desired.y = hand.position.y;
        Vector3 target = Vector3.Lerp(hand.position, desired, blend);
        for (int i = 0; i < 8; i++)
        {
            RotateJointToward(forearm, hand, target);
            RotateJointToward(upperArm, hand, target);
        }
    }

    private void SelectPunchArm(
        out Transform upperArm, out Transform forearm, out Transform hand)
    {
        upperArm = rig.RightUpperArm;
        forearm = rig.RightForearm;
        hand = rig.RightHand;
        float rightScore = PunchArmScore(rig.RightUpperArm, rig.RightHand);
        float leftScore = PunchArmScore(rig.LeftUpperArm, rig.LeftHand);
        if (leftScore > rightScore)
        {
            upperArm = rig.LeftUpperArm;
            forearm = rig.LeftForearm;
            hand = rig.LeftHand;
        }
    }

    private float PunchArmScore(Transform upperArm, Transform hand)
    {
        if (upperArm == null || hand == null)
        {
            return float.NegativeInfinity;
        }
        Vector3 reach = hand.position - upperArm.position;
        Vector3 aimDirection = punchDirection;
        if (hasPunchTarget)
        {
            Vector3 targetDelta = Vector3.ProjectOnPlane(
                punchTargetPosition - upperArm.position, Vector3.up);
            if (targetDelta.sqrMagnitude > 0.0001f)
            {
                aimDirection = targetDelta.normalized;
            }
        }
        return Vector3.Dot(Vector3.ProjectOnPlane(reach, Vector3.up), aimDirection) +
            reach.magnitude * 0.2f;
    }

    private void ApplyProceduralIdle(float time)
    {
        // Emergency fallback only when an imported Idle asset is unavailable;
        // the normal runtime path always uses the downloaded Mixamo Idle clip.
        float breath = Mathf.Sin(time * 1.7f);
        float shift = Mathf.Sin(time * 0.85f);
        ApplyTargetDelta(rig.Hips, Quaternion.Euler(0f, shift * 1.2f, 0f));
        ApplyTargetDelta(rig.Spine, Quaternion.Euler(breath * 1.1f, 0f, shift * 0.7f));
        ApplyTargetDelta(rig.Chest, Quaternion.Euler(breath * 2.2f, 0f, shift * 1.1f));
        ApplyTargetDelta(rig.Head, Quaternion.Euler(-breath * 0.8f, shift * 1.1f, 0f));
        ApplyTargetDelta(rig.LeftUpperArm, Quaternion.Euler(0f, 0f, shift * 1.4f));
        ApplyTargetDelta(rig.RightUpperArm, Quaternion.Euler(0f, 0f, -shift * 1.4f));
        Vector3 leftTarget = transform.position - transform.right * 0.55f + transform.up * 0.98f;
        Vector3 rightTarget = transform.position + transform.right * 0.55f + transform.up * 0.98f;
        ExtendArm(rig.LeftUpperArm, rig.LeftForearm, rig.LeftHand, leftTarget);
        ExtendArm(rig.RightUpperArm, rig.RightForearm, rig.RightHand, rightTarget);
    }

    private void ApplyFlightPose(float time)
    {
        // Goku's root is tilted by EnemyFighter during flight. Extend one arm
        // into that local forward direction and keep a light looping body sway.
        float pulse = Mathf.Sin(time * 3.2f);
        ApplyTargetDelta(rig.Hips, Quaternion.Euler(pulse * 1.5f, 0f, 0f));
        ApplyTargetDelta(rig.Spine, Quaternion.Euler(-8f + pulse * 1.5f, 0f, 0f));
        ApplyTargetDelta(rig.Chest, Quaternion.Euler(-12f + pulse * 2f, 0f, 0f));
        ExtendArm(rig.RightUpperArm, rig.RightForearm, rig.RightHand, transform.up * 1.2f);
        ApplyTargetDelta(rig.LeftUpperArm, Quaternion.Euler(18f, 0f, -22f));
        ApplyTargetDelta(rig.LeftForearm, Quaternion.Euler(-28f, 0f, 0f));
    }

    private void ExtendArm(Transform upperArm, Transform forearm, Transform hand, Vector3 worldTarget)
    {
        if (upperArm == null || forearm == null || hand == null)
        {
            return;
        }
        for (int i = 0; i < 5; i++)
        {
            RotateJointToward(forearm, hand, upperArm.position + worldTarget);
            RotateJointToward(upperArm, hand, upperArm.position + worldTarget);
        }
    }

    private void ApplyTargetDelta(Transform target, Quaternion delta)
    {
        if (target == null)
        {
            return;
        }
        target.rotation = transform.rotation * delta * Quaternion.Inverse(transform.rotation) * target.rotation;
    }

    private static void RotateJointToward(Transform joint, Transform endpoint, Vector3 target)
    {
        Vector3 current = endpoint.position - joint.position;
        Vector3 desired = target - joint.position;
        if (current.sqrMagnitude < 0.000001f || desired.sqrMagnitude < 0.000001f)
        {
            return;
        }
        joint.rotation = Quaternion.FromToRotation(current, desired) * joint.rotation;
    }

    private void ApplyRetargetedPose(float influence)
    {
        Quaternion ownerRotation = transform.rotation;
        Quaternion sourceRootInverse = Quaternion.Inverse(sourceModel.transform.rotation);
        for (int i = 0; i < pairs.Length; i++)
        {
            BonePair pair = pairs[i];
            Quaternion sourceCurrentInModel = sourceRootInverse * pair.Source.rotation;
            Quaternion delta = sourceCurrentInModel * Quaternion.Inverse(pair.SourceRestInModel);
            Quaternion safeDelta = Quaternion.Slerp(Quaternion.identity, delta, influence);
            pair.Target.rotation = ownerRotation * safeDelta * pair.TargetRestInOwner;
        }
    }

    private void RestoreTargetRest()
    {
        if (pairs == null)
        {
            return;
        }
        Quaternion ownerRotation = transform.rotation;
        if (hasTargetRootRest && rig.Root != null)
        {
            rig.Root.rotation = ownerRotation * targetRootRestInOwner;
        }
        for (int i = 0; i < pairs.Length; i++)
        {
            pairs[i].Target.rotation = ownerRotation * pairs[i].TargetRestInOwner;
        }
    }

    private void AddPair(
        List<BonePair> mapped, Transform[] sourceBones, Transform target,
        params string[] sourceNames)
    {
        Transform source = FindBone(sourceBones, sourceNames);
        if (source == null || target == null)
        {
            return;
        }
        mapped.Add(new BonePair
        {
            Source = source,
            Target = target,
            SourceRestInModel = Quaternion.Inverse(sourceModel.transform.rotation) * source.rotation,
            TargetRestInOwner = Quaternion.Inverse(transform.rotation) * target.rotation
        });
    }

    private static Transform FindBone(Transform[] bones, params string[] candidates)
    {
        for (int i = 0; i < bones.Length; i++)
        {
            string normalized = Normalize(bones[i].name);
            for (int candidateIndex = 0; candidateIndex < candidates.Length; candidateIndex++)
            {
                if (normalized == candidates[candidateIndex] ||
                    normalized.EndsWith(candidates[candidateIndex], StringComparison.Ordinal))
                {
                    return bones[i];
                }
            }
        }
        return null;
    }

    private static string Normalize(string value)
    {
        return value.Replace("mixamorig:", string.Empty)
            .Replace("mixamorig", string.Empty)
            .Replace("_", string.Empty)
            .Replace(" ", string.Empty)
            .ToLowerInvariant();
    }

    private static AnimationClip FindClip(AnimationClip[] clips, string marker)
    {
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i] != null &&
                !clips[i].name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase) &&
                clips[i].name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return clips[i];
            }
        }
        return null;
    }

    private static string ResourcePath(BodybuilderIdentity identity)
    {
        switch (identity)
        {
            case BodybuilderIdentity.Arnold: return "Characters/Enemies/arnold_mixamo_rigged";
            case BodybuilderIdentity.Cbum: return "Characters/Enemies/cbum_mixamo_rigged";
            case BodybuilderIdentity.Zyzz: return "Characters/Enemies/zyzz_mixamo_rigged";
            case BodybuilderIdentity.Ronnie: return "Characters/Enemies/ronnie_mixamo_rigged";
            case BodybuilderIdentity.JayCutler: return "Characters/Enemies/jay_mixamo_rigged";
            case BodybuilderIdentity.Goku: return "Characters/Enemies/goku_mixamo_rigged";
            default: return string.Empty;
        }
    }
}
