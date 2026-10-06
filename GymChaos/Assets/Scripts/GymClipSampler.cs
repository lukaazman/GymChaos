using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Player-safe replacement for AnimationClip.SampleAnimation. Unity only lets
/// SampleAnimation evaluate generic (non-legacy) clips inside the Editor; in a
/// player build (WebGL included) it logs "Non-Legacy animations cannot be
/// sampled outside the Editor without an Animator" and leaves the rig frozen.
///
/// This samples through a manual PlayableGraph on the rig root instead, so the
/// Editor and every build share one code path. Each root gets one graph with a
/// mixer; every clip it sees is added once as a mixer input and switching
/// clips only changes weights, so bindings are built once per clip rather than
/// on every switch. The graph is never played: Sample() writes the pose
/// synchronously, exactly when the caller asks, like SampleAnimation did.
/// </summary>
[DisallowMultipleComponent]
public sealed class GymClipSampler : MonoBehaviour
{
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private Animator animator;
    private readonly Dictionary<AnimationClip, int> ports =
        new Dictionary<AnimationClip, int>();
    private int activePort = -1;

    public static void Sample(AnimationClip clip, GameObject root, float time)
    {
        if (clip == null || root == null)
        {
            return;
        }

        GymClipSampler sampler = root.GetComponent<GymClipSampler>();
        if (sampler == null)
        {
            sampler = root.AddComponent<GymClipSampler>();
        }
        sampler.SampleClip(clip, time);
    }

    public static void Sample(AnimationClip clip, Component root, float time)
    {
        if (root != null)
        {
            Sample(clip, root.gameObject, time);
        }
    }

    private void EnsureGraph()
    {
        if (graph.IsValid())
        {
            return;
        }

        animator = GetComponent<Animator>();
        if (animator == null)
        {
            animator = gameObject.AddComponent<Animator>();
        }
        // The imported Animator stays controller-free; the graph below is
        // its only source and is evaluated manually.
        animator.runtimeAnimatorController = null;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        animator.enabled = true;

        graph = PlayableGraph.Create(name + " Clip Sampler");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        mixer = AnimationMixerPlayable.Create(graph, 0);
        AnimationPlayableOutput output =
            AnimationPlayableOutput.Create(graph, "Clip Sampler", animator);
        output.SetSourcePlayable(mixer);
        ports.Clear();
        activePort = -1;
    }

    private void SampleClip(AnimationClip clip, float time)
    {
        EnsureGraph();
        if (!animator.enabled)
        {
            animator.enabled = true;
        }

        if (!ports.TryGetValue(clip, out int port))
        {
            AnimationClipPlayable playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            playable.SetApplyPlayableIK(false);
            port = mixer.GetInputCount();
            mixer.SetInputCount(port + 1);
            graph.Connect(playable, 0, mixer, port);
            mixer.SetInputWeight(port, 0f);
            ports.Add(clip, port);
        }

        if (activePort != port)
        {
            if (activePort >= 0)
            {
                mixer.SetInputWeight(activePort, 0f);
            }
            mixer.SetInputWeight(port, 1f);
            activePort = port;
        }

        Playable input = mixer.GetInput(port);
        double clipTime = Mathf.Max(0f, time);
        // Setting the time twice clears the previous-time delta, so a jump
        // between unrelated times is a pure sample, not a scrub.
        input.SetTime(clipTime);
        input.SetTime(clipTime);
        bool probe = probesLeft > 0 && time > 0.05f && probeAttempts-- > 0;
        Quaternion[] before = probe ? CaptureProbe() : null;
        graph.Evaluate(0f);
        if (probe)
        {
            ReportProbe(clip, before);
        }
    }

    // Runtime evidence (players included): the first few samples report how
    // many bones the clip actually moved, once per session.
    private static int probesLeft = 6;
    private static int probeAttempts = 60;
    private Transform[] probeBones;

    private Quaternion[] CaptureProbe()
    {
        if (probeBones == null)
        {
            probeBones = GetComponentsInChildren<Transform>(true);
        }
        Quaternion[] rotations = new Quaternion[probeBones.Length];
        for (int i = 0; i < probeBones.Length; i++)
        {
            rotations[i] = probeBones[i].localRotation;
        }
        return rotations;
    }

    private void ReportProbe(AnimationClip clip, Quaternion[] before)
    {
        int moved = 0;
        for (int i = 0; i < probeBones.Length; i++)
        {
            if (Quaternion.Angle(before[i], probeBones[i].localRotation) > 0.5f)
            {
                moved++;
            }
        }
        if (moved == 0)
        {
            return;
        }
        probesLeft--;
        Debug.Log($"GYMCHAOS_CLIP_SAMPLER_OK root={name} clip={clip.name} movedBones={moved} " +
            $"platform={Application.platform}");
    }

    private void OnDestroy()
    {
        if (graph.IsValid())
        {
            graph.Destroy();
        }
    }
}
