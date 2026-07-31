using System.IO;
using UnityEngine;
using UnityEngine.Video;

public sealed class ReceptionDeathScreen : MonoBehaviour
{
    private const string VideoRelativePath = "Videos/manwithsuit.mp4";

    private EnemyFighter watchedFighter;
    private VideoPlayer videoPlayer;
    private AudioSource audioSource;
    private Material screenMaterial;
    private bool playbackStarted;

    public static ReceptionDeathScreen Create(
        Transform receptionist, EnemyFighter fighter, float floorY)
    {
        GameObject floor = GameObject.Find("Rubber Floor");
        if (floor == null || !floor.TryGetComponent(out Renderer floorRenderer))
        {
            return null;
        }

        Bounds floorBounds = floorRenderer.bounds;
        Vector3 position = receptionist.position;
        Vector3 roomFacing;
        float westDistance = Mathf.Abs(receptionist.position.x - floorBounds.min.x);
        float eastDistance = Mathf.Abs(floorBounds.max.x - receptionist.position.x);
        float southDistance = Mathf.Abs(receptionist.position.z - floorBounds.min.z);
        float northDistance = Mathf.Abs(floorBounds.max.z - receptionist.position.z);
        float nearestWall = Mathf.Min(westDistance, eastDistance, southDistance, northDistance);
        if (nearestWall == westDistance)
        {
            position.x = floorBounds.min.x + 0.24f;
            roomFacing = Vector3.right;
        }
        else if (nearestWall == eastDistance)
        {
            position.x = floorBounds.max.x - 0.24f;
            roomFacing = Vector3.left;
        }
        else if (nearestWall == southDistance)
        {
            position.z = floorBounds.min.z + 0.24f;
            roomFacing = Vector3.forward;
        }
        else
        {
            position.z = floorBounds.max.z - 0.24f;
            roomFacing = Vector3.back;
        }
        position.y = floorY + 4.15f;

        GameObject screen = new GameObject("Reception Death Video Screen");
        screen.name = "Reception Death Video Screen";
        screen.transform.SetPositionAndRotation(
            position, Quaternion.LookRotation(roomFacing, Vector3.up));
        screen.transform.localScale = new Vector3(7.6f, 4.25f, 1f);

        MeshFilter meshFilter = screen.AddComponent<MeshFilter>();
        meshFilter.sharedMesh = CreateFullUvScreenMesh();
        Renderer screenRenderer = screen.AddComponent<MeshRenderer>();
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
        {
            shader = Shader.Find("Unlit/Texture");
        }
        Material material = new Material(shader) { name = "Reception Screen Material" };
        material.color = Color.black;
        material.SetColor("_BaseColor", Color.black);
        material.SetFloat("_Cull", 0f);
        screenRenderer.sharedMaterial = material;

        ReceptionDeathScreen controller = screen.AddComponent<ReceptionDeathScreen>();
        controller.watchedFighter = fighter;
        controller.screenMaterial = material;
        controller.ConfigurePlayback(screenRenderer);
        return controller;
    }

    private void ConfigurePlayback(Renderer screenRenderer)
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.playOnAwake = false;
        audioSource.loop = false;
        audioSource.spatialBlend = 0f;
        audioSource.volume = 1f / 3f;

        videoPlayer = gameObject.AddComponent<VideoPlayer>();
        videoPlayer.playOnAwake = false;
        videoPlayer.isLooping = true;
        videoPlayer.waitForFirstFrame = true;
        videoPlayer.skipOnDrop = true;
        videoPlayer.source = VideoSource.Url;
        videoPlayer.url = Path.Combine(Application.streamingAssetsPath, VideoRelativePath);
        videoPlayer.renderMode = VideoRenderMode.MaterialOverride;
        videoPlayer.targetMaterialRenderer = screenRenderer;
        videoPlayer.targetMaterialProperty = "_BaseMap";
        videoPlayer.audioOutputMode = VideoAudioOutputMode.AudioSource;
        videoPlayer.controlledAudioTrackCount = 1;
        videoPlayer.EnableAudioTrack(0, true);
        videoPlayer.SetTargetAudioSource(0, audioSource);
        videoPlayer.loopPointReached += OnFirstLoopCompleted;
    }

    private void Update()
    {
        if (!playbackStarted && watchedFighter != null && watchedFighter.IsDead)
        {
            playbackStarted = true;
            screenMaterial.color = Color.white;
            screenMaterial.SetColor("_BaseColor", Color.white);
            videoPlayer.Play();
        }
    }

    private void OnFirstLoopCompleted(VideoPlayer source)
    {
        audioSource.mute = true;
    }

    private static Mesh CreateFullUvScreenMesh()
    {
        Mesh mesh = new Mesh { name = "Reception Full UV Video Screen Mesh" };
        mesh.vertices = new[]
        {
            new Vector3(-0.5f, -0.5f, 0f),
            new Vector3(-0.5f, 0.5f, 0f),
            new Vector3(0.5f, 0.5f, 0f),
            new Vector3(0.5f, -0.5f, 0f)
        };
        mesh.uv = new[] { Vector2.zero, Vector2.up, Vector2.one, Vector2.right };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }

    private void OnDestroy()
    {
        if (videoPlayer != null)
        {
            videoPlayer.loopPointReached -= OnFirstLoopCompleted;
        }
    }
}
