using UnityEngine;
using UnityEngine.Rendering;

// The aura is one enlarged, untextured copy of Goku's own skinned model.
// Keeping a single renderer avoids the seams and rectangular overlaps that a
// collection of procedural aura pieces creates around a transparent body.
public sealed class GokuAura : MonoBehaviour
{
    private const string ShaderName = "GymChaos/GokuAura";
    private const float AuraScale = 1.5f;
    private const float AuraOpacity = 0.45f;
    private const float AuraExpansion = 0.035f;

    private SkinnedMeshRenderer sourceRenderer;
    private SkinnedMeshRenderer auraRenderer;
    private Material auraMaterial;
    private EnemyFighter fighter;
    private float blend;

    public float CurrentBlend => blend;

    public void Configure(SkinnedMeshRenderer source)
    {
        sourceRenderer = source;
        fighter = GetComponent<EnemyFighter>();
        CreateScaledBodyCopy();
        SetVisible(false);
    }

    private void Awake()
    {
        fighter = GetComponent<EnemyFighter>();
    }

    private void LateUpdate()
    {
        if (auraRenderer == null)
        {
            return;
        }

        if (fighter == null)
        {
            fighter = GetComponent<EnemyFighter>();
        }

        // EnemyFighter already eases takeoff, flight, and landing. Reusing that
        // exact value keeps this single copy synchronized with Goku's rotation.
        blend = fighter != null ? fighter.GokuFlightAuraBlend : 0f;
        SetVisible(blend > 0.001f);
        auraMaterial?.SetFloat("_AuraBlend", blend);
    }

    private void CreateScaledBodyCopy()
    {
        if (sourceRenderer == null || sourceRenderer.sharedMesh == null)
        {
            return;
        }

        Shader auraShader = Shader.Find(ShaderName);
        if (auraShader == null)
        {
            Debug.LogWarning($"Could not find {ShaderName}; Goku aura is disabled.", this);
            return;
        }

        GameObject auraObject = new GameObject("Goku Unified Golden Aura");
        auraObject.transform.SetParent(sourceRenderer.transform, false);
        auraObject.transform.localScale = Vector3.one * AuraScale;
        auraObject.layer = sourceRenderer.gameObject.layer;

        auraRenderer = auraObject.AddComponent<SkinnedMeshRenderer>();
        auraRenderer.sharedMesh = sourceRenderer.sharedMesh;
        auraRenderer.bones = sourceRenderer.bones;
        auraRenderer.rootBone = sourceRenderer.rootBone;
        auraRenderer.localBounds = sourceRenderer.localBounds;
        auraRenderer.localBounds.Expand(Vector3.one * 0.75f);
        auraRenderer.updateWhenOffscreen = true;
        auraRenderer.quality = sourceRenderer.quality;
        auraRenderer.shadowCastingMode = ShadowCastingMode.Off;
        auraRenderer.receiveShadows = false;
        auraRenderer.skinnedMotionVectors = false;

        auraMaterial = new Material(auraShader)
        {
            name = "Goku Unified Golden Aura Material",
            renderQueue = 3120
        };
        auraMaterial.SetColor("_AuraColor", new Color(1f, 0.58f, 0.035f, 1f));
        auraMaterial.SetFloat("_Opacity", AuraOpacity);
        auraMaterial.SetFloat("_Expansion", AuraExpansion);
        auraMaterial.SetFloat("_Cull", (int)CullMode.Back);
        auraMaterial.SetFloat("_AuraBlend", 0f);
        auraMaterial.SetFloat("_PulseSpeed", 2.8f);
        auraMaterial.SetFloat("_FresnelPower", 1.75f);
        auraRenderer.sharedMaterial = auraMaterial;
    }

    private void SetVisible(bool visible)
    {
        if (auraRenderer != null)
        {
            auraRenderer.enabled = visible;
        }
    }

    private void OnDestroy()
    {
        if (auraMaterial != null)
        {
            Destroy(auraMaterial);
        }
    }
}
