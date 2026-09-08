using UnityEngine;

/// <summary>
/// Blends from the pose that was visible before an authored AnimationClip
/// switch into the newly sampled pose. Generic clips are sampled directly so
/// Unity cannot provide a built-in cross-fade; keeping the snapshot here makes
/// transitions work for every character's own hierarchy.
/// </summary>
internal sealed class AuthoredPoseTransition
{
    private Transform[] transforms = new Transform[0];
    private Vector3[] positions = new Vector3[0];
    private Quaternion[] rotations = new Quaternion[0];
    private Vector3[] scales = new Vector3[0];
    private float elapsed;
    private float duration;

    public bool IsActive => duration > 0f && elapsed < duration;

    public void Begin(Transform root, float transitionDuration)
    {
        Cancel();
        if (root == null || transitionDuration <= 0f)
        {
            return;
        }

        transforms = root.GetComponentsInChildren<Transform>(true);
        positions = new Vector3[transforms.Length];
        rotations = new Quaternion[transforms.Length];
        scales = new Vector3[transforms.Length];
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == null)
            {
                continue;
            }

            positions[i] = current.localPosition;
            rotations[i] = current.localRotation;
            scales[i] = current.localScale;
        }

        elapsed = 0f;
        duration = transitionDuration;
    }

    public void Apply(float deltaTime)
    {
        if (!IsActive)
        {
            return;
        }

        elapsed += Mathf.Max(0f, deltaTime);
        float normalized = Mathf.Clamp01(elapsed / duration);
        float eased = normalized * normalized * (3f - 2f * normalized);
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform current = transforms[i];
            if (current == null)
            {
                continue;
            }

            current.localPosition = Vector3.LerpUnclamped(
                positions[i], current.localPosition, eased);
            current.localRotation = Quaternion.SlerpUnclamped(
                rotations[i], current.localRotation, eased);
            current.localScale = Vector3.LerpUnclamped(
                scales[i], current.localScale, eased);
        }

        if (normalized >= 1f)
        {
            Cancel();
        }
    }

    public void Cancel()
    {
        elapsed = 0f;
        duration = 0f;
        transforms = new Transform[0];
        positions = new Vector3[0];
        rotations = new Quaternion[0];
        scales = new Vector3[0];
    }
}
