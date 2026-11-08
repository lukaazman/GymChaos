using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Stand-in body for another player: a tinted capsule that follows the
/// interpolated pose and bobs while that player moves. It has no collider yet;
/// hits between players arrive with PvP.
/// </summary>
public sealed class GymRemoteAvatar : MonoBehaviour
{
    private const float WalkBobMetres = 0.05f;
    private const float WalkCycleHz = 2.2f;

    private readonly GymNetInterpolator interpolator = new GymNetInterpolator();
    private Transform body;
    private Material material;
    private float cycle;

    private ScreenSpaceCharacterLabel label;

    public int PlayerId { get; private set; }
    public int PoseCount => interpolator.Count;
    /// <summary>True while the player behind this avatar is dead: it lies where it fell.</summary>
    public bool IsCorpse { get; private set; }

    public static GymRemoteAvatar Create(int playerId)
    {
        GameObject root = new GameObject("Remote Player " + playerId);
        GymRemoteAvatar avatar = root.AddComponent<GymRemoteAvatar>();
        avatar.PlayerId = playerId;
        avatar.Build();
        return avatar;
    }

    private void Build()
    {
        Color[] tints = { PersonaMenuStyle.Accent, PersonaMenuStyle.Gold, PersonaMenuStyle.Navy, PersonaMenuStyle.Ink };
        material = GymMaterialLibrary.NewLit("Remote Player " + PlayerId, tints[PlayerId % tints.Length], 0f, 0.35f);
        GameObject capsule = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        capsule.name = "Body";
        capsule.transform.SetParent(transform, false);
        capsule.transform.localScale = new Vector3(0.6f, 0.9f, 0.6f);
        Destroy(capsule.GetComponent<Collider>());
        MeshRenderer renderer = capsule.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.shadowCastingMode = ShadowCastingMode.On;
        body = capsule.transform;
        // Name and health bar like any fighter: the label follows the capsule and hides behind walls.
        label = gameObject.AddComponent<ScreenSpaceCharacterLabel>();
        label.ConfigureStandalone(renderer, "PLAYER " + (PlayerId + 1), 0.25f);
    }

    public void AddPose(double arrival, GymNetPose pose) => interpolator.Add(arrival, pose);

    private void Update()
    {
        if (!interpolator.TrySample(Time.unscaledTimeAsDouble, out GymNetPose pose)) return;
        transform.SetPositionAndRotation(pose.Position, Quaternion.Euler(0f, pose.r, 0f));
        bool dead = pose.d > 0.5f;
        if (dead != IsCorpse)
        {
            IsCorpse = dead;
            // Lying down: the body is rolled onto its side and rests on the floor.
            body.localRotation = dead ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
            body.localPosition = new Vector3(0f, dead ? -0.55f : 0f, 0f);
            label.ConfigureStandalone(body.GetComponent<Renderer>(), "PLAYER " + (PlayerId + 1) + (dead ? " (DEAD)" : string.Empty), 0.25f);
        }
        if (dead)
        {
            label.StandaloneHealthFraction = -1f;
            return;
        }
        label.StandaloneHealthFraction = pose.h < 0.999f ? Mathf.Clamp01(pose.h) : -1f;
        bool moving = pose.s > 0.3f;
        if (moving) cycle += Time.deltaTime * WalkCycleHz;
        body.localPosition = new Vector3(0f, moving ? Mathf.Abs(Mathf.Sin(cycle * Mathf.PI)) * WalkBobMetres : 0f, 0f);
    }

    private void OnDestroy()
    {
        if (material != null) Destroy(material);
    }
}
