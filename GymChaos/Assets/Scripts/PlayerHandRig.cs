using UnityEngine;

public class PlayerHandRig : MonoBehaviour
{
    private Transform leftRoot;
    private Transform rightRoot;
    private Vector3 leftBasePosition;
    private Vector3 rightBasePosition;
    private Quaternion leftBaseRotation;
    private Quaternion rightBaseRotation;

    private float leftPunchTimer;
    private float rightPunchTimer;
    private float shoveTimer;
    private float leftThrowTimer;
    private float rightThrowTimer;
    private bool isHolding;

    public static PlayerHandRig Create(Transform cameraTransform)
    {
        Transform existing = cameraTransform.Find("PlayerHandRig");
        if (existing != null)
        {
            PlayerHandRig rig = existing.GetComponent<PlayerHandRig>();
            return rig != null ? rig : existing.gameObject.AddComponent<PlayerHandRig>();
        }

        GameObject root = new GameObject("PlayerHandRig");
        root.transform.SetParent(cameraTransform, false);
        return root.AddComponent<PlayerHandRig>();
    }

    private void Awake()
    {
        BuildHands();
    }

    public void SetHolding(bool holding)
    {
        isHolding = holding;
    }

    public void TriggerPunch(bool useRightHand)
    {
        if (useRightHand)
        {
            rightPunchTimer = 0.18f;
        }
        else
        {
            leftPunchTimer = 0.18f;
        }
    }

    public void TriggerShove()
    {
        shoveTimer = 0.22f;
    }

    public void TriggerThrow(bool useRightHand)
    {
        if (useRightHand)
        {
            rightThrowTimer = 0.25f;
        }
        else
        {
            leftThrowTimer = 0.25f;
        }
    }

    public void Tick(float moveAmount)
    {
        leftPunchTimer = Mathf.Max(0f, leftPunchTimer - Time.deltaTime);
        rightPunchTimer = Mathf.Max(0f, rightPunchTimer - Time.deltaTime);
        shoveTimer = Mathf.Max(0f, shoveTimer - Time.deltaTime);
        leftThrowTimer = Mathf.Max(0f, leftThrowTimer - Time.deltaTime);
        rightThrowTimer = Mathf.Max(0f, rightThrowTimer - Time.deltaTime);

        float bob = Mathf.Sin(Time.time * (isHolding ? 4f : 7f)) * Mathf.Clamp01(moveAmount) * 0.025f;
        float leftPunchT = 1f - (leftPunchTimer / 0.18f);
        float rightPunchT = 1f - (rightPunchTimer / 0.18f);
        float shoveT = 1f - (shoveTimer / 0.22f);
        float leftThrowT = 1f - (leftThrowTimer / 0.25f);
        float rightThrowT = 1f - (rightThrowTimer / 0.25f);

        float leftPunch = leftPunchTimer > 0f ? Mathf.Sin(leftPunchT * Mathf.PI) : 0f;
        float rightPunch = rightPunchTimer > 0f ? Mathf.Sin(rightPunchT * Mathf.PI) : 0f;
        float shove = shoveTimer > 0f ? Mathf.Sin(shoveT * Mathf.PI) : 0f;
        float leftRelease = leftThrowTimer > 0f ? Mathf.Sin(leftThrowT * Mathf.PI) : 0f;
        float rightRelease = rightThrowTimer > 0f ? Mathf.Sin(rightThrowT * Mathf.PI) : 0f;

        Vector3 leftPos = leftBasePosition + new Vector3(0f, bob, 0f);
        Vector3 rightPos = rightBasePosition + new Vector3(0f, -bob, 0f);

        if (isHolding)
        {
            leftPos += new Vector3(0.04f, -0.02f, 0.17f);
            rightPos += new Vector3(-0.04f, -0.02f, 0.2f);
        }

        leftPos += new Vector3(0f, 0f, leftPunch * 0.28f + shove * 0.18f + leftRelease * 0.2f);
        rightPos += new Vector3(0f, 0f, rightPunch * 0.28f + shove * 0.18f + rightRelease * 0.2f);

        leftRoot.localPosition = leftPos;
        rightRoot.localPosition = rightPos;

        leftRoot.localRotation = leftBaseRotation * Quaternion.Euler(-leftPunch * 65f - shove * 25f, -leftPunch * 8f, leftRelease * 22f + shove * 10f);
        rightRoot.localRotation = rightBaseRotation * Quaternion.Euler(-rightPunch * 65f - shove * 25f, rightPunch * 8f, -rightRelease * 22f - shove * 10f);
    }

    private void BuildHands()
    {
        if (leftRoot != null && rightRoot != null)
        {
            return;
        }

        leftRoot = CreateHand("LeftHand", new Vector3(-0.22f, -0.24f, 0.45f), true);
        rightRoot = CreateHand("RightHand", new Vector3(0.22f, -0.26f, 0.42f), false);
        leftBasePosition = leftRoot.localPosition;
        rightBasePosition = rightRoot.localPosition;
        leftBaseRotation = leftRoot.localRotation;
        rightBaseRotation = rightRoot.localRotation;
    }

    private Transform CreateHand(string name, Vector3 localPosition, bool isLeft)
    {
        GameObject handRoot = new GameObject(name);
        Transform handTransform = handRoot.transform;
        handTransform.SetParent(transform, false);
        handTransform.localPosition = localPosition;
        handTransform.localRotation = Quaternion.Euler(10f, 180f, isLeft ? -18f : 18f);

        GameObject forearm = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        Destroy(forearm.GetComponent<Collider>());
        forearm.name = "Forearm";
        forearm.transform.SetParent(handTransform, false);
        forearm.transform.localPosition = new Vector3(0f, -0.03f, 0f);
        forearm.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        forearm.transform.localScale = new Vector3(0.11f, 0.18f, 0.11f);

        GameObject fist = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Destroy(fist.GetComponent<Collider>());
        fist.name = "Fist";
        fist.transform.SetParent(handTransform, false);
        fist.transform.localPosition = new Vector3(0f, 0.015f, 0.12f);
        fist.transform.localRotation = Quaternion.Euler(0f, isLeft ? 0f : 180f, 0f);
        fist.transform.localScale = new Vector3(0.12f, 0.08f, 0.16f);

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader);
        material.color = new Color(0.89f, 0.72f, 0.58f);
        forearm.GetComponent<Renderer>().material = material;
        fist.GetComponent<Renderer>().material = material;

        return handTransform;
    }
}
