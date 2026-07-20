using UnityEngine;

public sealed class ManWithSuitIdleAnimator : MonoBehaviour
{
    private BodybuilderEnemyVisual.Rig rig;
    private Quaternion spineBaseRotation;
    private Quaternion rightUpperArmBaseRotation;
    private Quaternion rightForearmBaseRotation;
    private Quaternion rightHandBaseRotation;
    private float cycle;

    public void Configure(BodybuilderEnemyVisual.Rig bodyRig)
    {
        rig = bodyRig;
        spineBaseRotation = rig.Spine.localRotation;
        rightUpperArmBaseRotation = rig.RightUpperArm.localRotation;
        rightForearmBaseRotation = rig.RightForearm.localRotation;
        rightHandBaseRotation = rig.RightHand.localRotation;
    }

    private void LateUpdate()
    {
        if (rig == null) return;
        cycle += Time.deltaTime * 16f;
        float stroke = Mathf.Sin(cycle);
        float follow = Mathf.Sin(cycle - 0.12f);
        rig.RightUpperArm.localRotation = rightUpperArmBaseRotation;
        rig.RightForearm.localRotation = rightForearmBaseRotation * Quaternion.Euler(
            stroke * 6.5f, follow * 1.5f, follow * 2.1f);
        rig.RightHand.localRotation = rightHandBaseRotation * Quaternion.Euler(
            follow * 2f, stroke * 0.8f, stroke * 1.2f);
        rig.Spine.localRotation = spineBaseRotation * Quaternion.Euler(
            Mathf.Sin(Time.time * 1.25f) * 0.55f, 0f, Mathf.Sin(Time.time * 0.9f) * 0.35f);
    }
}
