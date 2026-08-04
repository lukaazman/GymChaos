using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Samples the externally baked Mixamo skeleton, but applies only its bone
/// rotation deltas to the intact runtime GLB scan. The source FBX mesh stays
/// hidden, so its skin tears and incorrect scale can never reach gameplay.
/// </summary>
public sealed class MixamoScanRetargetAnimator : MonoBehaviour
{
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
    private BonePair[] pairs;
    private bool moving;
    private bool flying;
    private bool downed;
    private float speed01;
    private float runTime;
    private float attackTime = -1f;

    public bool Configure(BodybuilderIdentity identity, BodybuilderEnemyVisual.Rig bodyRig)
    {
        string resourcePath = ResourcePath(identity);
        GameObject prefab = Resources.Load<GameObject>(resourcePath);
        AnimationClip[] clips = Resources.LoadAll<AnimationClip>(resourcePath);
        runClip = FindClip(clips, "run");
        punchClip = FindClip(clips, "punch");
        if (prefab == null || runClip == null || punchClip == null || bodyRig == null)
        {
            return false;
        }

        rig = bodyRig;
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
        AddPair(mapped, sourceBones, rig.LeftUpperArm, "leftarm");
        AddPair(mapped, sourceBones, rig.LeftForearm, "leftforearm");
        AddPair(mapped, sourceBones, rig.LeftHand, "lefthand");
        AddPair(mapped, sourceBones, rig.RightUpperArm, "rightarm");
        AddPair(mapped, sourceBones, rig.RightForearm, "rightforearm");
        AddPair(mapped, sourceBones, rig.RightHand, "righthand");
        AddPair(mapped, sourceBones, rig.LeftThigh, "leftupleg");
        AddPair(mapped, sourceBones, rig.LeftShin, "leftleg");
        AddPair(mapped, sourceBones, rig.RightThigh, "rightupleg");
        AddPair(mapped, sourceBones, rig.RightShin, "rightleg");
        pairs = mapped.ToArray();
        RestoreTargetRest();
        Debug.Log(
            $"GYMCHAOS_MIXAMO_SCAN_RETARGET_OK identity={identity} bones={pairs.Length} " +
            $"run={runClip.name} punch={punchClip.name}", this);
        return pairs.Length >= 14;
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
        if (sourceModel == null || pairs == null)
        {
            return;
        }
        RestoreTargetRest();
        if (downed)
        {
            return;
        }
        if (flying)
        {
            return;
        }

        AnimationClip clip = null;
        float sampleTime = 0f;
        float influence = 0.72f;
        if (attackTime >= 0f)
        {
            attackTime += Time.deltaTime;
            float normalized = Mathf.Clamp01(attackTime / 0.72f);
            clip = punchClip;
            sampleTime = normalized * Mathf.Max(0.01f, clip.length - 0.001f);
            influence = 0.78f;
            if (attackTime >= 0.72f)
            {
                attackTime = -1f;
            }
        }
        else if (moving)
        {
            runTime += Time.deltaTime * Mathf.Lerp(0.75f, 1.35f, speed01);
            clip = runClip;
            sampleTime = runTime % Mathf.Max(0.01f, clip.length - 0.001f);
        }
        if (clip == null)
        {
            return;
        }

        Vector3 stablePosition = sourceModel.transform.localPosition;
        Quaternion stableRotation = sourceModel.transform.localRotation;
        Vector3 stableScale = sourceModel.transform.localScale;
        clip.SampleAnimation(sourceModel, sampleTime);
        sourceModel.transform.localPosition = stablePosition;
        sourceModel.transform.localRotation = stableRotation;
        sourceModel.transform.localScale = stableScale;
        ApplyRetargetedPose(influence);
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
