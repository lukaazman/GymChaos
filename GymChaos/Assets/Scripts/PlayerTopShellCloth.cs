using UnityEngine;

/// <summary>
/// Minimal cloth motion for the lofted oversized tee: the hem and the sleeve
/// cuffs trail the body as it moves, settle back onto it when it stops, and are
/// never allowed to swing into it. The shell mesh is built at runtime and stays
/// readable, so the motion is a small offset written to the cloth rings' own
/// vertices and the skinning still does the deformation; nothing is allocated
/// per frame and only the cloth ranges are re-uploaded.
///
/// The shell mesh is shared between wearers, so the offsets are shared too: the
/// game has one local player, and every mirror shows that same player.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerTopShellCloth : MonoBehaviour
{
    /// <summary>Longest the cloth may trail the body, in metres. Past this it
    /// reads as a flag rather than a heavyweight shirt.</summary>
    public const float HemMaxLag = 0.030f;

    // How fast the cloth catches up with the body, per second: cotton reaches
    // the full trail in about a fifth of a second.
    private const float Follow = 11f;
    // Trailing below this is the cloth sitting on the body again.
    private const float RestEpsilon = 0.0006f;
    // A frame longer than this (a hitch, a level load) must not fling the cloth.
    private const float MaxStep = 0.05f;

    private sealed class Ring
    {
        public int Start;
        public int Count;
        public float[] Fade;        // 1 at the open edge, 0 where it joins the body
        public Transform Anchor;    // the bone the ring hangs from
        public Matrix4x4 BindPose;
        public Vector3 RestCentre;  // bind space, so the anchor's pose drives it
        public Vector3 TargetWorld;
        public Vector3 Smoothed;
        public bool Offset;         // true while the mesh carries an offset here
    }

    private Mesh mesh;
    private Vector3[] rest;
    private Vector3[] current;
    private Ring[] rings = System.Array.Empty<Ring>();
    private bool verificationDriven;

    public bool IsConfigured => rings.Length > 0;

    /// <summary>Longest world offset the cloth carries right now, in metres.</summary>
    public float CurrentLag
    {
        get
        {
            float worst = 0f;
            for (int r = 0; r < rings.Length; r++)
                worst = Mathf.Max(worst, (rings[r].Smoothed - rings[r].TargetWorld).magnitude);
            return worst;
        }
    }

    /// <summary>
    /// Deepest the last step pushed cloth towards the body, in metres. Cloth has
    /// to stay out of the skin, so this stays at zero.
    /// </summary>
    public float InwardDepthForVerification { get; private set; }

    /// <summary>
    /// Takes the tee's cloth rings: for each one, first vertex, vertex count,
    /// vertices per row, and how many rows the motion fades out over (positive
    /// when the open edge is the first row, negative when it is the last).
    /// </summary>
    public void Configure(Mesh shellMesh, SkinnedMeshRenderer renderer, int[] layout)
    {
        Restore();
        if (shellMesh == null || renderer == null || layout == null || !shellMesh.isReadable) return;
        if (layout.Length < 4 || layout.Length % 4 != 0) return;
        mesh = shellMesh;
        Vector3[] vertices = shellMesh.vertices;
        BoneWeight[] weights = shellMesh.boneWeights;
        Matrix4x4[] bindPoses = shellMesh.bindposes;
        Transform[] bones = renderer.bones;
        if (vertices == null || weights == null || bindPoses == null || bones == null) return;
        if (rest == null || rest.Length != vertices.Length)
        {
            rest = new Vector3[vertices.Length];
            System.Array.Copy(vertices, rest, vertices.Length);
        }
        if (current == null || current.Length != rest.Length) current = new Vector3[rest.Length];
        System.Array.Copy(rest, current, rest.Length);

        int wanted = layout.Length / 4;
        if (rings.Length != wanted) rings = new Ring[wanted];
        for (int r = 0; r < wanted; r++)
        {
            Ring ring = rings[r] ?? (rings[r] = new Ring());
            ring.Start = Mathf.Clamp(layout[r * 4], 0, rest.Length);
            ring.Count = Mathf.Clamp(layout[r * 4 + 1], 0, rest.Length - ring.Start);
            int stride = Mathf.Max(1, layout[r * 4 + 2]);
            int fadeRows = layout[r * 4 + 3];
            bool fromStart = fadeRows > 0;
            int rows = Mathf.Max(1, Mathf.Abs(fadeRows));
            ring.Fade = new float[ring.Count];
            for (int i = 0; i < ring.Count; i++)
            {
                int row = fromStart ? i / stride : (ring.Count - 1 - i) / stride;
                ring.Fade[i] = Mathf.Clamp01(1f - row / (float)rows);
            }
            // The bone the ring hangs from: the heaviest weight in the range, so
            // the hem follows the hips and a cuff follows its own arm.
            int dominant = 0;
            float best = 0f;
            for (int i = 0; i < ring.Count; i++)
            {
                BoneWeight w = weights[ring.Start + i];
                AddWeight(w.boneIndex0, w.weight0, ref dominant, ref best);
                AddWeight(w.boneIndex1, w.weight1, ref dominant, ref best);
                AddWeight(w.boneIndex2, w.weight2, ref dominant, ref best);
                AddWeight(w.boneIndex3, w.weight3, ref dominant, ref best);
            }
            ring.Anchor = dominant >= 0 && dominant < bones.Length ? bones[dominant] : null;
            ring.BindPose = dominant >= 0 && dominant < bindPoses.Length ? bindPoses[dominant] : Matrix4x4.identity;
            Vector3 centre = Vector3.zero;
            for (int i = 0; i < ring.Count; i++) centre += rest[ring.Start + i];
            ring.RestCentre = ring.Count > 0 ? centre / ring.Count : Vector3.zero;
            Reset(ring);
        }
        Restore();
    }

    /// <summary>One deterministic step for the verifier: the body moves at the
    /// given world velocity instead of the rig being read every frame.</summary>
    public void AdvanceForVerification(Vector3 anchorVelocity, float dt)
    {
        if (!IsConfigured || mesh == null) return;
        verificationDriven = true;
        InwardDepthForVerification = 0f;
        dt = Mathf.Clamp(dt, 0f, MaxStep);
        for (int r = 0; r < rings.Length; r++)
        {
            rings[r].TargetWorld += anchorVelocity * dt;
            Step(rings[r], dt);
        }
    }

    /// <summary>Puts the cloth back on the body and hands the mesh back to the
    /// rig, so a verifier measures the shirt and not the motion.</summary>
    public void ResetForVerification()
    {
        verificationDriven = false;
        InwardDepthForVerification = 0f;
        for (int r = 0; r < rings.Length; r++) Reset(rings[r]);
        Restore();
    }

    private void LateUpdate()
    {
        if (verificationDriven || !IsConfigured || mesh == null || !mesh.isReadable) return;
        float dt = Mathf.Min(Time.deltaTime, MaxStep);
        if (dt <= 0f) return;
        for (int r = 0; r < rings.Length; r++)
        {
            Ring ring = rings[r];
            if (ring.Anchor == null) continue;
            ring.TargetWorld = ToWorld(ring).MultiplyPoint3x4(ring.RestCentre);
            Step(ring, dt);
        }
    }

    private void Step(Ring ring, float dt)
    {
        float follow = 1f - Mathf.Exp(-Follow * dt);
        ring.Smoothed = Vector3.Lerp(ring.Smoothed, ring.TargetWorld, follow);
        Vector3 lag = ring.Smoothed - ring.TargetWorld;
        if (lag.magnitude > HemMaxLag) lag = lag.normalized * HemMaxLag;
        ring.Smoothed = ring.TargetWorld + lag;
        if (lag.sqrMagnitude <= RestEpsilon * RestEpsilon)
        {
            // Sitting on the body again: stop dressing the mesh.
            if (ring.Offset)
            {
                System.Array.Copy(rest, ring.Start, current, ring.Start, ring.Count);
                ring.Offset = false;
                Flush();
            }
            return;
        }
        Matrix4x4 toWorld = ToWorld(ring);
        Matrix4x4 toObject = toWorld.inverse;
        Vector3 anchorWorld = ring.Anchor != null ? ring.Anchor.position : ring.TargetWorld;
        float inward = 0f;
        for (int i = 0; i < ring.Count; i++)
        {
            int v = ring.Start + i;
            Vector3 vertexLag = lag;
            // Cloth may trail along and away from the body, never into it: the
            // part of the swing that points at the joint is dropped, which is
            // also what a shirt does over a running torso.
            Vector3 radial = toWorld.MultiplyPoint3x4(rest[v]) - anchorWorld;
            if (radial.sqrMagnitude > 1e-8f)
            {
                radial /= radial.magnitude;
                float towards = Vector3.Dot(vertexLag, radial);
                if (towards < 0f)
                {
                    vertexLag -= radial * towards;
                    inward = Mathf.Max(inward, -Vector3.Dot(vertexLag, radial));
                }
            }
            current[v] = rest[v] + toObject.MultiplyVector(vertexLag) * ring.Fade[i];
        }
        InwardDepthForVerification = Mathf.Max(InwardDepthForVerification, inward);
        ring.Offset = true;
        Flush();
    }

    private Matrix4x4 ToWorld(Ring ring) => ring.Anchor.localToWorldMatrix * ring.BindPose;

    private void Reset(Ring ring)
    {
        if (ring.Anchor != null) ring.TargetWorld = ToWorld(ring).MultiplyPoint3x4(ring.RestCentre);
        ring.Smoothed = ring.TargetWorld;
    }

    // The whole vertex array goes up, not a ring range: a partial
    // SetVertexBufferData on this runtime-built mesh left vertices outside the
    // range metres off the body, which the tops verifier caught as a fit
    // failure. The upload only happens while the cloth is actually moving.
    private void Flush() => mesh.SetVertices(current);

    private void Restore()
    {
        if (mesh == null) return;
        bool restored = false;
        for (int r = 0; r < rings.Length; r++)
        {
            Ring ring = rings[r];
            if (ring == null || !ring.Offset) continue;
            System.Array.Copy(rest, ring.Start, current, ring.Start, ring.Count);
            ring.Offset = false;
            restored = true;
        }
        if (restored) Flush();
    }

    private static void AddWeight(int index, float weight, ref int dominant, ref float best)
    {
        if (weight <= best) return;
        best = weight;
        dominant = index;
    }

    private void OnDisable() => Restore();

    private void OnDestroy() => Restore();
}
