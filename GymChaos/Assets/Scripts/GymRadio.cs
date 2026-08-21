using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.Rendering;

public sealed class GymRadio : MonoBehaviour
{
    private const string RadioRelativePath = "BodyBuilders/sound/radio.glb";
    private const string PlaylistRelativePath = "BodyBuilders/sound/playlist";
    private const float TargetRadioWidth = 0.58f;
    private const float DeskEdgeInset = 0.08f;
    private const float DeskTopGap = 0.008f;
    private const float BaseRadioVolume = 0.62f;
    private const float UserVolumeScale = 0.85f;

    private static readonly string[] FallbackPlaylist =
    {
        "11_It_Has_To_Be_This_Way_Platinum_Mix_KLICKAUD.mp3",
        "CLARITY_HARDSTYLE_SLOWED_-_AGARTHA_EDIT_OUT_ON_SPOTIFY_KLICKAUD.mp3",
        "cool_for_the_summer_hardstyle_tiktok_version_sped_up_KLICKAUD.mp3",
        "im_so_lucky_hardstyle_KLICKAUD.mp3",
        "Judas_LEOJ_Hardstyle_Bootleg_SPOTIFY_KLICKAUD.mp3",
        "pain_1993_playboi_carti_x_KLICKAUD.mp3",
        "schooling_seniors_KLICKAUD.mp3",
        "The_One_That_Got_Away_Hardstyle_SLOWED_BEST_VERSION_KLICKAUD.mp3",
        "ZYZZ_-_Safe_And_Sound_X_Wildest_Dream_Hardstyle_Remix_Tiktok_Mix_KLICKAUD.mp3"
    };

    private const uint GlbMagic = 0x46546C67;
    private const uint JsonChunk = 0x4E4F534A;
    private const uint BinaryChunk = 0x004E4942;

    [Serializable]
    private sealed class GltfRoot
    {
        public GltfAccessor[] accessors;
        public GltfMesh[] meshes;
        public GltfImage[] images;
        public GltfTexture[] textures;
        public GltfMaterial[] materials;
        public GltfBufferView[] bufferViews;
    }

    [Serializable]
    private sealed class GltfBufferView
    {
        public int byteOffset;
        public int byteLength;
        public int byteStride;
    }

    [Serializable]
    private sealed class GltfAccessor
    {
        public int bufferView;
        public int byteOffset;
        public int componentType;
        public int count;
    }

    [Serializable]
    private sealed class GltfMesh
    {
        public GltfPrimitive[] primitives;
    }

    [Serializable]
    private sealed class GltfPrimitive
    {
        public GltfAttributes attributes;
        public int indices;
    }

    [Serializable]
    private sealed class GltfAttributes
    {
        public int POSITION;
        public int NORMAL;
        public int TEXCOORD_0;
    }

    [Serializable]
    private sealed class GltfImage
    {
        public int bufferView;
        public string mimeType;
    }

    [Serializable]
    private sealed class GltfTexture
    {
        public int source;
    }

    [Serializable]
    private sealed class GltfMaterial
    {
        public GltfPbrMetallicRoughness pbrMetallicRoughness;
    }

    [Serializable]
    private sealed class GltfPbrMetallicRoughness
    {
        public GltfTextureReference baseColorTexture;
        public float metallicFactor = 0.0f;
        public float roughnessFactor = 0.8f;
    }

    [Serializable]
    private sealed class GltfTextureReference
    {
        public int index;
    }

    private readonly List<string> playlistEntries = new List<string>();
    private AudioSource audioSource;
    private Bounds deskBounds;
    private Coroutine playlistRoutine;
    private bool playbackRequested;
    private bool musicEnabled = true;
    private PlayerMovement listenerPlayer;
    private bool listenerZoneKnown;
    private bool listenerInsideGym;
    private float nextZoneCheckTime;

    public bool IsMusicEnabled => musicEnabled;

    public static GymRadio CreateForScene()
    {
        GymRadio existing = FindAnyObjectByType<GymRadio>();
        if (existing != null)
        {
            return existing;
        }

        GameObject desk = GameObject.Find("Reception desk");
        if (desk == null || !TryGetRendererBounds(desk.transform, out Bounds bounds))
        {
            Debug.LogWarning("GYMCHAOS_RADIO_SKIPPED reception desk bounds are unavailable.");
            return null;
        }

        GameObject radioObject = new GameObject("Reception radio");
        GymRadio radio = radioObject.AddComponent<GymRadio>();
        radio.deskBounds = bounds;
        radio.listenerPlayer = FindAnyObjectByType<PlayerMovement>();
        radio.ConfigureAudio();
        radio.StartCoroutine(radio.LoadRadioModel());
        return radio;
    }

    public void BeginPlayback()
    {
        playbackRequested = true;
        if (playlistRoutine == null)
        {
            playlistRoutine = StartCoroutine(PlayPlaylistLoop());
        }
    }

    public static GymRadio FindClosest(Vector3 position, float maxDistance)
    {
        GymRadio radio = FindAnyObjectByType<GymRadio>();
        if (radio == null)
        {
            return null;
        }

        Vector3 offset = radio.transform.position - position;
        offset.y = 0f;
        return offset.sqrMagnitude <= maxDistance * maxDistance ? radio : null;
    }

    public string GetInteractionPrompt()
    {
        return musicEnabled ? "[R] Turn music off" : "[R] Turn music on";
    }

    public void ToggleMusic()
    {
        musicEnabled = !musicEnabled;
        ApplyMuteState(listenerInsideGym);
        Debug.Log(
            $"GYMCHAOS_RADIO_MUSIC_TOGGLED enabled={musicEnabled}", this);
    }

    private void ConfigureAudio()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0.82f;
        audioSource.volume = BaseRadioVolume * UserVolumeScale;
        audioSource.rolloffMode = AudioRolloffMode.Custom;
        audioSource.minDistance = 2.5f;
        audioSource.maxDistance = 32f;
        audioSource.dopplerLevel = 0f;

        // Keep the source spatial and distance-aware, but hold the volume up
        // longer inside the gym so the radio does not disappear too quickly.
        AnimationCurve rolloff = new AnimationCurve(
            new Keyframe(0f, 1f),
            new Keyframe(0.32f, 0.97f),
            new Keyframe(0.68f, 0.78f),
            new Keyframe(0.88f, 0.42f),
            new Keyframe(1f, 0f));
        audioSource.SetCustomCurve(AudioSourceCurveType.CustomRolloff, rolloff);
    }

    private void Update()
    {
        if (audioSource == null || Time.unscaledTime < nextZoneCheckTime)
        {
            return;
        }

        nextZoneCheckTime = Time.unscaledTime + 0.2f;
        if (listenerPlayer == null)
        {
            listenerPlayer = FindAnyObjectByType<PlayerMovement>();
        }

        bool insideGym = listenerPlayer != null &&
            !GymOutdoorBuilder.IsPlayerOutsideGym(listenerPlayer.transform.position);
        ApplyMuteState(insideGym);
        if (!listenerZoneKnown || listenerInsideGym != insideGym)
        {
            listenerZoneKnown = true;
            listenerInsideGym = insideGym;
            Debug.Log(
                $"GYMCHAOS_RADIO_ZONE inside={insideGym} muted={!insideGym}", this);
        }
    }

    private IEnumerator LoadRadioModel()
    {
        byte[] glbBytes;
        string path = JoinStreamingAssetsPath(RadioRelativePath);
        using (UnityWebRequest request = UnityWebRequest.Get(path))
        {
            request.downloadHandler = new DownloadHandlerBuffer();
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                Debug.LogError(
                    $"GYMCHAOS_RADIO_MODEL_ERROR path={path} error={request.error}", this);
                yield break;
            }

            glbBytes = request.downloadHandler.data;
        }

        if (!TryReadGlb(glbBytes, out GltfRoot gltf, out byte[] binary) ||
            gltf.meshes[0].primitives == null || gltf.meshes[0].primitives.Length == 0)
        {
            Debug.LogError("GYMCHAOS_RADIO_MODEL_ERROR could not read radio.glb.", this);
            yield break;
        }

        yield return null;

        GltfPrimitive primitive = gltf.meshes[0].primitives[0];
        Vector3[] sourcePositions = ReadVector3Accessor(
            gltf, binary, primitive.attributes.POSITION);
        Vector3[] sourceNormals = primitive.attributes.NORMAL >= 0 &&
            primitive.attributes.NORMAL < gltf.accessors.Length
            ? ReadVector3Accessor(gltf, binary, primitive.attributes.NORMAL)
            : null;
        Vector2[] sourceUvs = primitive.attributes.TEXCOORD_0 >= 0 &&
            primitive.attributes.TEXCOORD_0 < gltf.accessors.Length
            ? ReadVector2Accessor(gltf, binary, primitive.attributes.TEXCOORD_0)
            : null;
        int[] triangles = ReadIndexAccessor(gltf, binary, primitive.indices);

        Vector3[] positions = new Vector3[sourcePositions.Length];
        Vector3[] normals = sourceNormals != null && sourceNormals.Length == sourcePositions.Length
            ? new Vector3[sourceNormals.Length]
            : null;
        Vector2[] uvs = sourceUvs != null && sourceUvs.Length == sourcePositions.Length
            ? new Vector2[sourceUvs.Length]
            : null;

        for (int i = 0; i < sourcePositions.Length; i++)
        {
            Vector3 source = sourcePositions[i];
            positions[i] = new Vector3(-source.x, source.y, source.z);

            if (normals != null)
            {
                Vector3 sourceNormal = sourceNormals[i];
                normals[i] = new Vector3(-sourceNormal.x, sourceNormal.y, sourceNormal.z).normalized;
            }

            if (uvs != null)
            {
                uvs[i] = new Vector2(sourceUvs[i].x, 1f - sourceUvs[i].y);
            }
        }

        for (int i = 0; i + 2 < triangles.Length; i += 3)
        {
            int first = triangles[i];
            triangles[i] = triangles[i + 2];
            triangles[i + 2] = first;
        }

        Mesh mesh = new Mesh
        {
            name = "Reception radio runtime mesh",
            indexFormat = IndexFormat.UInt32,
            vertices = positions,
            triangles = triangles
        };
        if (normals != null)
        {
            mesh.normals = normals;
        }
        else
        {
            mesh.RecalculateNormals();
        }
        if (uvs != null)
        {
            mesh.uv = uvs;
        }
        mesh.RecalculateBounds();

        GameObject model = new GameObject("Radio model");
        model.transform.SetParent(transform, false);
        model.transform.localRotation = Quaternion.Euler(0f, -90f, 0f);
        model.transform.localScale = Vector3.one *
            (TargetRadioWidth / Mathf.Max(0.001f, mesh.bounds.size.x));

        MeshFilter filter = model.AddComponent<MeshFilter>();
        filter.sharedMesh = mesh;
        MeshRenderer renderer = model.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = CreateRadioMaterial(gltf, binary);
        renderer.shadowCastingMode = ShadowCastingMode.On;
        renderer.receiveShadows = true;

        PlaceModelOnDesk(renderer);
        Debug.Log(
            $"GYMCHAOS_RADIO_PLACED edge=front-north position={transform.position} " +
            $"size={renderer.bounds.size} deskTop={deskBounds.max.y:F3}", this);
    }

    private void PlaceModelOnDesk(Renderer renderer)
    {
        Bounds currentBounds = renderer.bounds;
        Vector3 desiredMin = new Vector3(
            deskBounds.min.x + DeskEdgeInset,
            deskBounds.max.y + DeskTopGap,
            currentBounds.min.z);
        float desiredMaxZ = deskBounds.max.z - DeskEdgeInset;

        transform.position += new Vector3(
            desiredMin.x - currentBounds.min.x,
            desiredMin.y - currentBounds.min.y,
            desiredMaxZ - currentBounds.max.z);
    }

    private IEnumerator PlayPlaylistLoop()
    {
        playlistEntries.Clear();
        playlistEntries.AddRange(GetPlaylistEntries());
        if (playlistEntries.Count == 0)
        {
            Debug.LogWarning("GYMCHAOS_RADIO_PLAYLIST_EMPTY no supported audio files found.", this);
            playlistRoutine = null;
            yield break;
        }

        Debug.Log(
            $"GYMCHAOS_RADIO_PLAYLIST_READY count={playlistEntries.Count} " +
            $"folder={PlaylistRelativePath}", this);

        ShufflePlaylist(playlistEntries);
        Debug.Log(
            $"GYMCHAOS_RADIO_PLAYLIST_SHUFFLED count={playlistEntries.Count} " +
            $"first={Path.GetFileName(playlistEntries[0])} noRepeatsUntilCycle=true", this);

        int index = 0;
        while (playbackRequested && isActiveAndEnabled)
        {
            string relativePath = playlistEntries[index];
            AudioClip clip = null;
            string path = JoinStreamingAssetsPath(relativePath);
            using (UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(
                path, GetAudioType(relativePath)))
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
                        $"GYMCHAOS_RADIO_TRACK_ERROR index={index} " +
                        $"track={Path.GetFileName(relativePath)} error={request.error}", this);
                }
            }

            if (clip != null)
            {
                audioSource.clip = clip;
                audioSource.Play();
                Debug.Log(
                    $"GYMCHAOS_RADIO_PLAYING index={index} " +
                    $"track={Path.GetFileName(relativePath)} loop=playlist", this);

                float minimumWait = Time.unscaledTime + 0.15f;
                while (audioSource.isPlaying || Time.unscaledTime < minimumWait)
                {
                    yield return null;
                }

                audioSource.Stop();
                audioSource.clip = null;
                Destroy(clip);
            }

            index = (index + 1) % playlistEntries.Count;
            yield return null;
        }

        playlistRoutine = null;
    }

    private void ApplyMuteState(bool insideGym)
    {
        if (audioSource != null)
        {
            audioSource.mute = !musicEnabled || !insideGym;
        }
    }

    private static void ShufflePlaylist(List<string> entries)
    {
        int seed = unchecked(
            System.Environment.TickCount ^
            (int)System.DateTime.UtcNow.Ticks);
        System.Random random = new System.Random(seed);
        for (int i = entries.Count - 1; i > 0; i--)
        {
            int swapIndex = random.Next(i + 1);
            string temporary = entries[i];
            entries[i] = entries[swapIndex];
            entries[swapIndex] = temporary;
        }
    }

    private static List<string> GetPlaylistEntries()
    {
        List<string> result = new List<string>();
        string streamingPath = Application.streamingAssetsPath;
        if (!streamingPath.Contains("://"))
        {
            string folder = Path.Combine(streamingPath, "BodyBuilders", "sound", "playlist");
            if (Directory.Exists(folder))
            {
                string[] files = Directory.GetFiles(folder);
                Array.Sort(files, StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < files.Length; i++)
                {
                    if (IsSupportedAudio(files[i]))
                    {
                        result.Add(
                            PlaylistRelativePath + "/" + Path.GetFileName(files[i]));
                    }
                }
            }
        }

        if (result.Count == 0)
        {
            for (int i = 0; i < FallbackPlaylist.Length; i++)
            {
                result.Add(PlaylistRelativePath + "/" + FallbackPlaylist[i]);
            }
        }

        return result;
    }

    private static bool IsSupportedAudio(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        return extension == ".mp3" || extension == ".ogg" || extension == ".wav";
    }

    private static AudioType GetAudioType(string path)
    {
        string extension = Path.GetExtension(path).ToLowerInvariant();
        if (extension == ".ogg")
        {
            return AudioType.OGGVORBIS;
        }
        if (extension == ".wav")
        {
            return AudioType.WAV;
        }
        return AudioType.MPEG;
    }

    private static bool TryGetRendererBounds(Transform root, out Bounds bounds)
    {
        Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
        bool hasBounds = false;
        bounds = default;
        for (int i = 0; i < renderers.Length; i++)
        {
            Renderer renderer = renderers[i];
            if (renderer == null || !renderer.enabled)
            {
                continue;
            }

            if (!hasBounds)
            {
                bounds = renderer.bounds;
                hasBounds = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }
        }

        return hasBounds;
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

    private static Material CreateRadioMaterial(GltfRoot gltf, byte[] binary)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            shader = Shader.Find("Standard");
        }

        Material material = new Material(shader) { name = "Reception radio material" };
        material.SetColor("_BaseColor", Color.white);
        material.SetColor("_Color", Color.white);

        GltfPbrMetallicRoughness pbr = null;
        if (gltf.materials != null && gltf.materials.Length > 0)
        {
            pbr = gltf.materials[0].pbrMetallicRoughness;
        }

        if (pbr != null)
        {
            material.SetFloat("_Metallic", Mathf.Clamp01(pbr.metallicFactor));
            material.SetFloat("_Smoothness", 1f - Mathf.Clamp01(pbr.roughnessFactor));
            if (pbr.baseColorTexture != null)
            {
                Texture2D texture = ReadTexture(gltf, binary, pbr.baseColorTexture.index);
                if (texture != null)
                {
                    material.mainTexture = texture;
                    material.SetTexture("_BaseMap", texture);
                    material.SetTexture("_MainTex", texture);
                }
            }
        }

        return material;
    }

    private static Texture2D ReadTexture(GltfRoot gltf, byte[] binary, int textureIndex)
    {
        if (gltf.textures == null || textureIndex < 0 || textureIndex >= gltf.textures.Length)
        {
            return null;
        }

        int imageIndex = gltf.textures[textureIndex].source;
        if (gltf.images == null || imageIndex < 0 || imageIndex >= gltf.images.Length)
        {
            return null;
        }

        GltfImage image = gltf.images[imageIndex];
        if (image.bufferView < 0 || image.bufferView >= gltf.bufferViews.Length)
        {
            return null;
        }

        GltfBufferView view = gltf.bufferViews[image.bufferView];
        byte[] imageBytes = new byte[view.byteLength];
        Buffer.BlockCopy(binary, view.byteOffset, imageBytes, 0, view.byteLength);
        Texture2D texture = new Texture2D(2, 2, TextureFormat.RGBA32, true)
        {
            name = "Reception radio texture",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Repeat
        };
        if (!texture.LoadImage(imageBytes, true))
        {
            Destroy(texture);
            return null;
        }

        return texture;
    }

    private static bool TryReadGlb(byte[] bytes, out GltfRoot gltf, out byte[] binary)
    {
        gltf = null;
        binary = null;
        if (bytes == null || bytes.Length < 20 || BitConverter.ToUInt32(bytes, 0) != GlbMagic)
        {
            return false;
        }

        int offset = 12;
        string json = null;
        while (offset + 8 <= bytes.Length)
        {
            int length = (int)BitConverter.ToUInt32(bytes, offset);
            uint type = BitConverter.ToUInt32(bytes, offset + 4);
            offset += 8;
            if (length < 0 || offset + length > bytes.Length)
            {
                return false;
            }

            if (type == JsonChunk)
            {
                json = Encoding.UTF8.GetString(bytes, offset, length)
                    .TrimEnd('\0', ' ', '\n', '\r', '\t');
            }
            else if (type == BinaryChunk)
            {
                binary = new byte[length];
                Buffer.BlockCopy(bytes, offset, binary, 0, length);
            }
            offset += length;
        }

        if (string.IsNullOrEmpty(json) || binary == null)
        {
            return false;
        }

        gltf = JsonUtility.FromJson<GltfRoot>(json);
        return gltf != null && gltf.meshes != null && gltf.meshes.Length > 0 &&
            gltf.accessors != null && gltf.bufferViews != null;
    }

    private static Vector3[] ReadVector3Accessor(
        GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int stride = view.byteStride > 0 ? view.byteStride : 12;
        int start = view.byteOffset + accessor.byteOffset;
        Vector3[] result = new Vector3[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = new Vector3(
                BitConverter.ToSingle(binary, offset),
                BitConverter.ToSingle(binary, offset + 4),
                BitConverter.ToSingle(binary, offset + 8));
        }
        return result;
    }

    private static Vector2[] ReadVector2Accessor(
        GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int stride = view.byteStride > 0 ? view.byteStride : 8;
        int start = view.byteOffset + accessor.byteOffset;
        Vector2[] result = new Vector2[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = new Vector2(
                BitConverter.ToSingle(binary, offset),
                BitConverter.ToSingle(binary, offset + 4));
        }
        return result;
    }

    private static int[] ReadIndexAccessor(
        GltfRoot gltf, byte[] binary, int accessorIndex)
    {
        GltfAccessor accessor = gltf.accessors[accessorIndex];
        GltfBufferView view = gltf.bufferViews[accessor.bufferView];
        int componentSize = accessor.componentType == 5125
            ? 4
            : accessor.componentType == 5123
                ? 2
                : 1;
        int stride = view.byteStride > 0 ? view.byteStride : componentSize;
        int start = view.byteOffset + accessor.byteOffset;
        int[] result = new int[accessor.count];
        for (int i = 0; i < result.Length; i++)
        {
            int offset = start + i * stride;
            result[i] = accessor.componentType == 5125
                ? (int)BitConverter.ToUInt32(binary, offset)
                : accessor.componentType == 5123
                    ? BitConverter.ToUInt16(binary, offset)
                    : binary[offset];
        }
        return result;
    }

    private void OnDestroy()
    {
        playbackRequested = false;
    }
}
