using UnityEngine;

public sealed class BodybuilderEnemyAnimator : MonoBehaviour
{
    private BodybuilderEnemyVisual.Rig rig;
    private BodybuilderIdentity identity;
    private bool moving;
    private bool downed;
    private float movementSpeed01;
    private float movementBlend;
    private float attackBlend;
    private float walkCycle;
    private float idleCycle;
    private bool flying;

    private Vector3 hipsBasePosition;
    private Quaternion hipsBaseRotation;
    private Quaternion spineBaseRotation;
    private Quaternion leftUpperBaseRotation;
    private Quaternion leftForearmBaseRotation;
    private Quaternion rightUpperBaseRotation;
    private Quaternion rightForearmBaseRotation;
    private Quaternion leftThighBaseRotation;
    private Quaternion leftShinBaseRotation;
    private Quaternion rightThighBaseRotation;
    private Quaternion rightShinBaseRotation;

    public void Configure(BodybuilderIdentity bodybuilderIdentity, BodybuilderEnemyVisual.Rig bodyRig)
    {
        identity = bodybuilderIdentity;
        rig = bodyRig;
        hipsBasePosition = rig.Hips.localPosition;
        hipsBaseRotation = rig.Hips.localRotation;
        spineBaseRotation = rig.Spine.localRotation;
        leftUpperBaseRotation = rig.LeftUpperArm.localRotation;
        leftForearmBaseRotation = rig.LeftForearm.localRotation;
        rightUpperBaseRotation = rig.RightUpperArm.localRotation;
        rightForearmBaseRotation = rig.RightForearm.localRotation;
        leftThighBaseRotation = rig.LeftThigh.localRotation;
        leftShinBaseRotation = rig.LeftShin.localRotation;
        rightThighBaseRotation = rig.RightThigh.localRotation;
        rightShinBaseRotation = rig.RightShin.localRotation;
        ApplyMotion(0f, 0f, 0f, 0f);
    }

    public void SetMoving(bool shouldMove, float normalizedSpeed = 1f)
    {
        moving = shouldMove;
        movementSpeed01 = shouldMove ? Mathf.Clamp01(normalizedSpeed) : 0f;
    }

    public void SetFlying(bool shouldFly)
    {
        flying = shouldFly;
        if (flying)
        {
            moving = false;
            movementBlend = 0f;
        }
    }

    public void TriggerAttack()
    {
        attackBlend = 1f;
    }

    public void SetDowned(bool isDowned)
    {
        downed = isDowned;
        if (downed)
        {
            moving = false;
        }
    }

    private void LateUpdate()
    {
        if (rig == null)
        {
            return;
        }

        float targetMovement = moving && !downed ? Mathf.Max(0.2f, movementSpeed01) : 0f;
        movementBlend = Mathf.MoveTowards(movementBlend, targetMovement, Time.deltaTime * 5.5f);
        attackBlend = Mathf.MoveTowards(attackBlend, 0f, Time.deltaTime * 4f);
        idleCycle += Time.deltaTime * 1.15f;
        // Match foot cadence to actual chase speed. At full speed this is about
        // 1.65 stride cycles per second, preventing the old one-step-then-glide look.
        walkCycle += Time.deltaTime * Mathf.Lerp(4.5f, 10.4f, movementBlend);
        ApplyMotion(movementBlend, walkCycle, idleCycle, attackBlend);
    }

    private void ApplyMotion(float blend, float walkPhase, float idlePhase, float attack)
    {
        if (flying)
        {
            rig.Hips.localPosition = hipsBasePosition;
            rig.Hips.localRotation = hipsBaseRotation;
            rig.Spine.localRotation = spineBaseRotation;
            rig.LeftUpperArm.localRotation = leftUpperBaseRotation;
            rig.LeftForearm.localRotation = leftForearmBaseRotation;
            rig.RightUpperArm.localRotation = rightUpperBaseRotation;
            rig.RightForearm.localRotation = rightForearmBaseRotation;
            rig.LeftThigh.localRotation = leftThighBaseRotation;
            rig.LeftShin.localRotation = leftShinBaseRotation;
            rig.RightThigh.localRotation = rightThighBaseRotation;
            rig.RightShin.localRotation = rightShinBaseRotation;
            return;
        }

        // Every rotation is layered over this scan's authored pose. The three
        // meshes therefore keep their original composition instead of being
        // forced into a shared humanoid rest pose.
        float step = Mathf.Sin(walkPhase);
        float oppositeStep = -step;
        float thighStride;
        float kneeBend;
        float hipTurn;
        switch (identity)
        {
            case BodybuilderIdentity.Arnold:
                thighStride = 17f;
                kneeBend = 23f;
                hipTurn = 1.8f;
                break;
            case BodybuilderIdentity.Cbum:
                thighStride = 18.5f;
                kneeBend = 25f;
                hipTurn = 2.1f;
                break;
            case BodybuilderIdentity.Ronnie:
                thighStride = 17f;
                kneeBend = 23f;
                hipTurn = 1.4f;
                break;
            case BodybuilderIdentity.JayCutler:
                // Jay's scan needs a stronger but still bounded leg separation
                // to read as walking at gameplay distance. The 24/31 degree
                // limits keep the thigh and shin joints from folding through
                // the mesh while making the alternating stride visible.
                thighStride = 24f;
                kneeBend = 31f;
                hipTurn = 2.2f;
                break;
            default:
                thighStride = 16f;
                kneeBend = 21f;
                hipTurn = 1.5f;
                break;
        }
        thighStride *= blend;
        kneeBend *= blend;
        float leftKnee = Mathf.Max(0f, -step) * kneeBend;
        float rightKnee = Mathf.Max(0f, -oppositeStep) * kneeBend;
        float armAmplitude = identity == BodybuilderIdentity.Ronnie ? 0.45f
            : identity == BodybuilderIdentity.Zyzz ? 1f : 2.4f;
        float forearmAmplitude = identity == BodybuilderIdentity.Ronnie ? 0.15f
            : identity == BodybuilderIdentity.Zyzz ? 0.35f : 0.9f;
        float armSwing = step * armAmplitude * blend;
        float idlePulse = 0.5f + 0.5f * Mathf.Sin(idlePhase);

        Vector3 leftUpperMotion = new Vector3(armSwing - attack * 3f, 0f, 0f);
        Vector3 rightUpperMotion = new Vector3(-armSwing, 0f, 0f);
        Vector3 leftForearmMotion = new Vector3(-step * forearmAmplitude * blend - attack * 1.5f, 0f, 0f);
        Vector3 rightForearmMotion = new Vector3(step * forearmAmplitude * blend, 0f, 0f);

        switch (identity)
        {
            case BodybuilderIdentity.Arnold:
                // Relax the double-biceps pose slightly and return to the full
                // flex without ever rebuilding or straightening either arm.
                leftUpperMotion.z -= idlePulse * 1.8f;
                rightUpperMotion.z += idlePulse * 1.8f;
                leftForearmMotion.z += idlePulse * 7.5f;
                rightForearmMotion.z -= idlePulse * 7.5f;
                break;
            case BodybuilderIdentity.Cbum:
                // A small outward shoulder roll makes the authored lat spread
                // flare and settle while the hands stay in their original pose.
                leftUpperMotion.z -= idlePulse * 3.6f;
                rightUpperMotion.z += idlePulse * 3.6f;
                break;
            case BodybuilderIdentity.Zyzz:
                // Keep Zyzz's asymmetric pose and add only a subtle shoulder and
                // forearm pulse specific to the arm placement in this scan.
                leftUpperMotion.z -= idlePulse * 0.8f;
                rightUpperMotion.z += idlePulse * 0.5f;
                rightForearmMotion.z -= idlePulse * 1.1f;
                break;
            case BodybuilderIdentity.Ronnie:
                // Ronnie's scan keeps its authored upper-body pose nearly fixed;
                // pursuit motion is deliberately concentrated in hips and legs.
                leftUpperMotion *= 0.35f;
                rightUpperMotion *= 0.35f;
                leftForearmMotion *= 0.25f;
                rightForearmMotion *= 0.25f;
                break;
        }

        rig.LeftUpperArm.localRotation = leftUpperBaseRotation * Quaternion.Euler(leftUpperMotion);
        rig.RightUpperArm.localRotation = rightUpperBaseRotation * Quaternion.Euler(rightUpperMotion);
        rig.LeftForearm.localRotation = leftForearmBaseRotation * Quaternion.Euler(leftForearmMotion);
        rig.RightForearm.localRotation = rightForearmBaseRotation * Quaternion.Euler(rightForearmMotion);
        rig.LeftThigh.localRotation = leftThighBaseRotation * Quaternion.Euler(step * thighStride, 0f, 0f);
        rig.RightThigh.localRotation = rightThighBaseRotation * Quaternion.Euler(oppositeStep * thighStride, 0f, 0f);
        rig.LeftShin.localRotation = leftShinBaseRotation * Quaternion.Euler(leftKnee, 0f, 0f);
        rig.RightShin.localRotation = rightShinBaseRotation * Quaternion.Euler(rightKnee, 0f, 0f);

        float bob = (0.5f - 0.5f * Mathf.Cos(walkPhase * 2f)) * 0.012f * blend;
        rig.Hips.localPosition = hipsBasePosition + Vector3.up * bob;
        rig.Hips.localRotation = hipsBaseRotation * Quaternion.Euler(
            0f, step * hipTurn * blend, -step * 0.7f * blend);
        float torsoCounterSwing = identity == BodybuilderIdentity.Ronnie ? 0.3f :
            identity == BodybuilderIdentity.Zyzz ? 0.45f :
            identity == BodybuilderIdentity.Cbum ? 0.9f : 0.7f;
        float idleAmount = 1f - blend * 0.7f;
        Vector3 spineMotion = new Vector3(
            Mathf.Sin(idlePhase * 0.5f) * 0.45f * idleAmount,
            -step * torsoCounterSwing * blend,
            Mathf.Sin(idlePhase * 0.7f) * 0.3f * idleAmount);
        rig.Spine.localRotation = spineBaseRotation * Quaternion.Euler(spineMotion);
    }
}
