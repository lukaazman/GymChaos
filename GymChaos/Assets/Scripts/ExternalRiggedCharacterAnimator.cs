using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Samples the model-specific Mixamo clips baked into each external FBX. No
/// runtime skin-weight or body-part rig generation is performed here.
/// </summary>
public sealed class ExternalRiggedCharacterAnimator : MonoBehaviour
{
    private GameObject modelRoot;
    private BodybuilderEnemyVisual.Rig rig;
    private BodybuilderIdentity identity;
    private AnimationClip runClip;
    private AnimationClip punchClip;
    private AnimationClip flyClip;
    private readonly Dictionary<Transform, Quaternion> restRotations = new Dictionary<Transform, Quaternion>();
    private readonly Dictionary<Transform, Vector3> restPositions = new Dictionary<Transform, Vector3>();
    private bool moving;
    private bool flying;
    private bool downed;
    private float speed01;
    private float locomotionTime;
    private float attackTime = -1f;

    public bool HasRunClip => runClip != null;
    public bool HasPunchClip => punchClip != null;
    public bool HasFlyClip => flyClip != null;

    public void Configure(
        GameObject importedModelRoot, BodybuilderEnemyVisual.Rig importedRig,
        BodybuilderIdentity bodybuilderIdentity, string resourcePath)
    {
        modelRoot = importedModelRoot;
        rig = importedRig;
        identity = bodybuilderIdentity;
        AnimationClip[] embeddedClips = Resources.LoadAll<AnimationClip>(resourcePath);
        runClip = FindEmbeddedClip(embeddedClips, "run");
        punchClip = FindEmbeddedClip(embeddedClips, "punch");
        flyClip = identity == BodybuilderIdentity.Goku
            ? LoadClip("Characters/Animations/Goku/Fly")
            : null;

        Transform[] transforms = modelRoot.GetComponentsInChildren<Transform>(true);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform target = transforms[i];
            if (target == null || target == modelRoot.transform)
            {
                continue;
            }
            restRotations[target] = target.localRotation;
            restPositions[target] = target.localPosition;
        }
    }

    public void SetMoving(bool shouldMove, float normalizedSpeed = 1f)
    {
        moving = shouldMove;
        speed01 = shouldMove ? Mathf.Clamp01(normalizedSpeed) : 0f;
    }

    public void SetFlying(bool shouldFly)
    {
        flying = shouldFly;
        if (flying)
        {
            moving = false;
        }
    }

    public void TriggerAttack()
    {
        attackTime = 0f;
    }

    public void SetDowned(bool isDowned)
    {
        downed = isDowned;
        if (downed)
        {
            moving = false;
            flying = false;
            attackTime = -1f;
        }
    }

    private void LateUpdate()
    {
        if (modelRoot == null || rig == null)
        {
            return;
        }

        RestoreRestPose();
        if (downed)
        {
            return;
        }

        locomotionTime += Time.deltaTime * Mathf.Lerp(0.75f, 1.35f, speed01);
        if (flying)
        {
            if (flyClip != null)
            {
                SampleClip(flyClip, locomotionTime % Mathf.Max(0.01f, flyClip.length - 0.001f));
            }
            else
            {
                ApplyOneArmForwardFlightPose();
            }
            return;
        }

        if (attackTime >= 0f)
        {
            const float attackDuration = 0.72f;
            attackTime += Time.deltaTime;
            if (punchClip != null)
            {
                float normalized = Mathf.Clamp01(attackTime / attackDuration);
                SampleClip(punchClip, normalized * Mathf.Max(0.01f, punchClip.length - 0.001f));
            }
            if (attackTime >= attackDuration)
            {
                attackTime = -1f;
            }
            return;
        }

        if (moving && runClip != null)
        {
            SampleClip(runClip, locomotionTime % Mathf.Max(0.01f, runClip.length - 0.001f));
        }
    }

    private void SampleClip(AnimationClip clip, float time)
    {
        Vector3 stablePosition = modelRoot.transform.localPosition;
        Quaternion stableRotation = modelRoot.transform.localRotation;
        Vector3 stableScale = modelRoot.transform.localScale;
        clip.SampleAnimation(modelRoot, time);
        modelRoot.transform.localPosition = stablePosition;
        modelRoot.transform.localRotation = stableRotation;
        modelRoot.transform.localScale = stableScale;
    }

    private void ApplyOneArmForwardFlightPose()
    {
        if (rig.RightUpperArm == null || rig.RightForearm == null || rig.RightHand == null)
        {
            return;
        }

        Vector3 forward = transform.up;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = transform.forward;
        }
        Vector3 target = rig.RightUpperArm.position + forward.normalized * 1.15f;
        for (int i = 0; i < 6; i++)
        {
            RotateJointToward(rig.RightForearm, rig.RightHand, target);
            RotateJointToward(rig.RightUpperArm, rig.RightHand, target);
        }
    }

    private void RestoreRestPose()
    {
        foreach (KeyValuePair<Transform, Quaternion> pair in restRotations)
        {
            if (pair.Key == null)
            {
                continue;
            }
            pair.Key.localRotation = pair.Value;
            pair.Key.localPosition = restPositions[pair.Key];
        }
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

    private static AnimationClip LoadClip(string resourcePath)
    {
        AnimationClip[] clips = Resources.LoadAll<AnimationClip>(resourcePath);
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null && !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase))
            {
                return clip;
            }
        }
        return null;
    }

    private static AnimationClip FindEmbeddedClip(AnimationClip[] clips, string marker)
    {
        if (clips == null)
        {
            return null;
        }
        for (int i = 0; i < clips.Length; i++)
        {
            AnimationClip clip = clips[i];
            if (clip != null &&
                !clip.name.StartsWith("__preview__", StringComparison.OrdinalIgnoreCase) &&
                clip.name.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return clip;
            }
        }
        return null;
    }
}
