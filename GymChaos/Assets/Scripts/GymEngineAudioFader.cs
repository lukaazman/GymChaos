using UnityEngine;

/// <summary>
/// Fades a vehicle's looped driving sound: in when the vehicle starts
/// driving (spawn or leaving a bay), out when it parks. When the vehicle is
/// switched off (despawned) while audible, a short detached tail at its last
/// position finishes the fade instead of cutting the sound.
/// </summary>
public sealed class GymEngineAudioFader : MonoBehaviour
{
    public const float FadeInSeconds = 1.2f;
    public const float FadeOutSeconds = 1.6f;

    private AudioSource source;
    private float baseVolume;
    private bool driving;

    public bool IsDriving => driving;
    public bool IsSilentOrFadingOut => source == null || !source.isPlaying || !driving;
    public float CurrentVolume => source != null ? source.volume : 0f;
    public static int CompletedFadeInsForVerification { get; private set; }
    public static int CompletedFadeOutsForVerification { get; private set; }
    public static int DespawnTailsForVerification { get; private set; }

    public static GymEngineAudioFader Attach(AudioSource engine, float volume)
    {
        if (engine == null)
        {
            return null;
        }
        GymEngineAudioFader fader = engine.gameObject.AddComponent<GymEngineAudioFader>();
        fader.source = engine;
        fader.baseVolume = Mathf.Max(0f, volume);
        engine.volume = 0f;
        return fader;
    }

    public void SetDriving(bool value)
    {
        driving = value;
        if (driving && source != null && !source.isPlaying)
        {
            source.volume = 0f;
            source.Play();
        }
    }

    private void Update()
    {
        if (source == null)
        {
            return;
        }

        float target = driving ? baseVolume : 0f;
        if (Mathf.Approximately(source.volume, target))
        {
            if (!driving && source.isPlaying)
            {
                source.Stop();
                CompletedFadeOutsForVerification++;
                if (CompletedFadeOutsForVerification == 1)
                {
                    Debug.Log($"GYMCHAOS_ENGINE_FADE_OUT_OK vehicle={name} seconds={FadeOutSeconds}");
                }
            }
            return;
        }

        float seconds = driving ? FadeInSeconds : FadeOutSeconds;
        float step = baseVolume / Mathf.Max(0.01f, seconds) * Time.deltaTime;
        source.volume = Mathf.MoveTowards(source.volume, target, step);
        if (driving && Mathf.Approximately(source.volume, target))
        {
            CompletedFadeInsForVerification++;
            if (CompletedFadeInsForVerification == 1)
            {
                Debug.Log($"GYMCHAOS_ENGINE_FADE_IN_OK vehicle={name} seconds={FadeInSeconds}");
            }
        }
    }

    private void OnDisable()
    {
        // Only a despawn (the vehicle switched off) leaves a tail; scene
        // teardown and play-mode exit disable objects that stay activeSelf.
        if (source == null || !source.isPlaying || source.volume <= 0.01f ||
            source.clip == null || !Application.isPlaying || gameObject.activeSelf)
        {
            return;
        }

        // The vehicle object is being switched off: hand the remaining fade
        // to a short-lived source so the loop does not cut off.
        GameObject tail = new GameObject("Engine Fade Tail");
        tail.transform.position = transform.position;
        AudioSource tailSource = tail.AddComponent<AudioSource>();
        tailSource.clip = source.clip;
        tailSource.loop = true;
        tailSource.volume = source.volume;
        tailSource.mute = source.mute;
        tailSource.spatialBlend = source.spatialBlend;
        tailSource.rolloffMode = source.rolloffMode;
        tailSource.minDistance = source.minDistance;
        tailSource.maxDistance = source.maxDistance;
        tailSource.dopplerLevel = 0f;
        tailSource.pitch = source.pitch;
        tailSource.timeSamples = Mathf.Clamp(source.timeSamples, 0, source.clip.samples - 1);
        tailSource.Play();
        source.Stop();
        GymEngineAudioTail fade = tail.AddComponent<GymEngineAudioTail>();
        fade.Begin(tailSource, FadeOutSeconds);
        DespawnTailsForVerification++;
        if (DespawnTailsForVerification == 1)
        {
            Debug.Log($"GYMCHAOS_ENGINE_DESPAWN_FADE_OK vehicle={name}");
        }
    }
}

/// <summary>Fades out and removes a detached engine tail.</summary>
public sealed class GymEngineAudioTail : MonoBehaviour
{
    private AudioSource source;
    private float rate;

    public void Begin(AudioSource tailSource, float seconds)
    {
        source = tailSource;
        rate = tailSource.volume / Mathf.Max(0.01f, seconds);
    }

    private void Update()
    {
        if (source == null)
        {
            Destroy(gameObject);
            return;
        }
        source.volume = Mathf.MoveTowards(source.volume, 0f, rate * Time.deltaTime);
        if (source.volume <= 0.001f)
        {
            Destroy(gameObject);
        }
    }
}
