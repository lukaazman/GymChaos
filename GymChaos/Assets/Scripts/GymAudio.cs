using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

public enum GymSoundEffect
{
    None,
    PunchAction,
    PunchFeedback,
    GlassShatter,
    ThrownWallImpact,
    ThrownMachineImpact,
    ThrownEnemyImpact,
    ThrownBodyImpact
}

public sealed class GymAudio : MonoBehaviour
{
    private const string SfxRelativePath = "BodyBuilders/sound/sfx";
    private const float OneShotMinDistance = 1.1f;
    private const float OneShotMaxDistance = 28f;

    private sealed class ClipDefinition
    {
        public readonly string FileName;
        public readonly AudioType AudioType;
        public readonly float MaxDuration;

        public ClipDefinition(
            string fileName, AudioType audioType, float maxDuration = 0f)
        {
            FileName = fileName;
            AudioType = audioType;
            MaxDuration = maxDuration;
        }
    }

    private struct PendingPlay
    {
        public Vector3 Position;
        public float Volume;
    }

    private static readonly Dictionary<GymSoundEffect, ClipDefinition> Definitions =
        new Dictionary<GymSoundEffect, ClipDefinition>
        {
            { GymSoundEffect.PunchAction, new ClipDefinition("free_punch.wav", AudioType.WAV) },
            { GymSoundEffect.PunchFeedback, new ClipDefinition("punch_landed.wav", AudioType.WAV) },
            { GymSoundEffect.GlassShatter, new ClipDefinition("glass_shatter.ogg", AudioType.OGGVORBIS, 0.24f) },
            { GymSoundEffect.ThrownWallImpact, new ClipDefinition("throw_wall_impact.wav", AudioType.WAV) },
            { GymSoundEffect.ThrownMachineImpact, new ClipDefinition("throw_machine_impact.wav", AudioType.WAV) },
            { GymSoundEffect.ThrownEnemyImpact, new ClipDefinition("throw_enemy_impact.ogg", AudioType.OGGVORBIS) },
            { GymSoundEffect.ThrownBodyImpact, new ClipDefinition("throw_body_impact.ogg", AudioType.OGGVORBIS) }
        };

    private static GymAudio instance;

    private readonly Dictionary<GymSoundEffect, AudioClip> clips =
        new Dictionary<GymSoundEffect, AudioClip>();
    private readonly HashSet<GymSoundEffect> loading =
        new HashSet<GymSoundEffect>();
    private readonly Dictionary<GymSoundEffect, List<PendingPlay>> pendingPlays =
        new Dictionary<GymSoundEffect, List<PendingPlay>>();
    private bool preloadStarted;

    public static GymAudio CreateForScene()
    {
        if (instance != null)
        {
            return instance;
        }

        instance = FindAnyObjectByType<GymAudio>();
        if (instance == null)
        {
            GameObject audioObject = new GameObject("Gym Audio (Runtime)");
            instance = audioObject.AddComponent<GymAudio>();
        }

        instance.StartPreloading();
        Debug.Log("GYMCHAOS_SFX_READY source=BodyBuilders/sound/sfx", instance);
        return instance;
    }

    public static void Play(
        GymSoundEffect effect, Vector3 position, float volume = 1f)
    {
        if (effect == GymSoundEffect.None)
        {
            return;
        }

        GymAudio audio = EnsureInstance();
        if (audio != null)
        {
            audio.QueuePlay(effect, position, volume);
        }
    }

    public static GymSoundEffect ResolveThrownImpact(
        PickupItem source, Collider target)
    {
        if (target == null)
        {
            return GymSoundEffect.ThrownWallImpact;
        }

        if (target.GetComponentInParent<GlassShatterPanel>() != null)
        {
            return GymSoundEffect.None;
        }

        if (target.GetComponentInParent<EnemyFighter>() != null)
        {
            return GymSoundEffect.ThrownEnemyImpact;
        }

        if (target.GetComponentInParent<PlayerMovement>() != null)
        {
            return GymSoundEffect.ThrownBodyImpact;
        }

        PickupItem otherItem = target.GetComponentInParent<PickupItem>();
        if (otherItem != null && otherItem != source)
        {
            return GymSoundEffect.ThrownMachineImpact;
        }

        string objectDescription = target.name;
        if (target.transform.parent != null)
        {
            objectDescription += " " + target.transform.root.name;
        }
        if (target.sharedMaterial != null)
        {
            objectDescription += " " + target.sharedMaterial.name;
        }

        string normalized = objectDescription.ToLowerInvariant();
        if (ContainsAny(
            normalized,
            "machine", "equipment", "treadmill", "rack", "cage", "bench",
            "barbell", "ezbar", "dumbbell", "plate", "weight", "cable",
            "smith", "pulley", "kettlebell", "metal", "steel", "iron",
            "chrome", "aluminum", "aluminium"))
        {
            return GymSoundEffect.ThrownMachineImpact;
        }

        return GymSoundEffect.ThrownWallImpact;
    }

    private static GymAudio EnsureInstance()
    {
        if (instance == null)
        {
            instance = FindAnyObjectByType<GymAudio>();
        }

        if (instance == null)
        {
            instance = CreateForScene();
        }

        return instance;
    }

    private static bool ContainsAny(string value, params string[] terms)
    {
        for (int i = 0; i < terms.Length; i++)
        {
            if (value.Contains(terms[i]))
            {
                return true;
            }
        }

        return false;
    }

    private void Awake()
    {
        if (instance != null && instance != this)
        {
            Destroy(gameObject);
            return;
        }

        instance = this;
    }

    private void OnDestroy()
    {
        if (instance == this)
        {
            instance = null;
        }
    }

    private void StartPreloading()
    {
        if (preloadStarted)
        {
            return;
        }

        preloadStarted = true;
        StartCoroutine(PreloadAll());
    }

    private IEnumerator PreloadAll()
    {
        foreach (KeyValuePair<GymSoundEffect, ClipDefinition> definition in Definitions)
        {
            if (!clips.ContainsKey(definition.Key) && loading.Add(definition.Key))
            {
                yield return LoadClipAndFlush(definition.Key);
            }

            yield return null;
        }

        Debug.Log(
            $"GYMCHAOS_SFX_PRELOAD_READY count={clips.Count}/{Definitions.Count}", this);
    }

    private void QueuePlay(GymSoundEffect effect, Vector3 position, float volume)
    {
        volume = Mathf.Clamp01(volume);
        if (clips.TryGetValue(effect, out AudioClip clip) && clip != null)
        {
            PlayClip(effect, clip, position, volume);
            return;
        }

        if (!pendingPlays.TryGetValue(effect, out List<PendingPlay> plays))
        {
            plays = new List<PendingPlay>();
            pendingPlays.Add(effect, plays);
        }
        if (plays.Count < 24)
        {
            plays.Add(new PendingPlay { Position = position, Volume = volume });
        }

        if (loading.Add(effect))
        {
            StartCoroutine(LoadClipAndFlush(effect));
        }
    }

    private IEnumerator LoadClipAndFlush(GymSoundEffect effect)
    {
        if (!Definitions.TryGetValue(effect, out ClipDefinition definition))
        {
            loading.Remove(effect);
            yield break;
        }

        string relativePath = SfxRelativePath + "/" + definition.FileName;
        string path = JoinStreamingAssetsPath(relativePath);
        AudioClip clip = null;
        using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(
            path, definition.AudioType))
        {
            DownloadHandlerAudioClip handler =
                request.downloadHandler as DownloadHandlerAudioClip;
            if (handler != null)
            {
                handler.streamAudio = false;
            }

            yield return request.SendWebRequest();
            if (request.result == UnityWebRequest.Result.Success)
            {
                clip = DownloadHandlerAudioClip.GetContent(request);
            }
            else
            {
                Debug.LogWarning(
                    $"GYMCHAOS_SFX_LOAD_ERROR effect={effect} file={definition.FileName} " +
                    $"error={request.error}", this);
            }
        }

        if (clip != null)
        {
            clip.name = "Gym SFX - " + effect;
            clips[effect] = clip;
            if (pendingPlays.TryGetValue(effect, out List<PendingPlay> plays))
            {
                for (int i = 0; i < plays.Count; i++)
                {
                    PendingPlay pending = plays[i];
                    PlayClip(effect, clip, pending.Position, pending.Volume);
                }
                pendingPlays.Remove(effect);
            }
        }

        loading.Remove(effect);
    }

    private void PlayClip(
        GymSoundEffect effect, AudioClip clip, Vector3 position, float volume)
    {
        if (clip == null)
        {
            return;
        }

        GameObject oneShotObject = new GameObject("SFX - " + effect);
        oneShotObject.transform.position = position;
        AudioSource source = oneShotObject.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = false;
        source.clip = clip;
        source.volume = Mathf.Clamp01(volume);
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Linear;
        source.minDistance = OneShotMinDistance;
        source.maxDistance = OneShotMaxDistance;
        source.dopplerLevel = 0f;
        source.Play();
        float cleanupDelay = Mathf.Max(0.1f, clip.length) + 0.15f;
        if (Definitions.TryGetValue(effect, out ClipDefinition definition) &&
            definition.MaxDuration > 0f)
        {
            cleanupDelay = Mathf.Min(clip.length, definition.MaxDuration);
        }
        Destroy(oneShotObject, Mathf.Max(0.02f, cleanupDelay));
        Debug.Log(
            $"GYMCHAOS_SFX_PLAY effect={effect} position={position} volume={volume:0.00}",
            oneShotObject);
    }

    private static string JoinStreamingAssetsPath(string relativePath)
    {
        string path = Application.streamingAssetsPath.TrimEnd('/', '\\') + "/" + relativePath;
        if (path.Contains("://"))
        {
            return path;
        }

        return "file:///" + path.Replace('\\', '/').TrimStart('/');
    }
}
