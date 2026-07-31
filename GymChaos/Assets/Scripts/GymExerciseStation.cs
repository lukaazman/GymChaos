using System.Collections.Generic;
using UnityEngine;

public enum GymExerciseType
{
    FlatBenchPress,
    InclineBenchPress,
    BarbellSquat,
    PreacherCurl,
    Dips,
    Treadmill,
    ExerciseBike,
    LatPulldown
}

public class GymExerciseStation : MonoBehaviour
{
    private static readonly List<GymExerciseStation> Stations = new List<GymExerciseStation>();
    private static readonly int[] SquatWeights = { 20, 60, 80, 100, 120, 140, 160, 180, 200 };
    private static readonly int[] FlatBenchWeights = { 20, 60, 70, 80, 90, 100, 110, 120, 130, 140 };
    private static readonly int[] InclineBenchWeights = { 20, 40, 50, 60, 70, 80, 90, 100 };
    private static readonly int[] PreacherWeights = { 10, 20, 30, 40, 50 };
    private static readonly int[] LatPulldownWeights = { 20, 40, 50, 60, 70, 80, 90, 100, 115, 130 };

    private GymExerciseType exerciseType;
    private string displayName;
    private Vector3 interactionPoint;
    private Vector3 playerPosition;
    private Quaternion playerRotation;
    private Transform equipmentRoot;
    private Transform visualRoot;
    private Vector3 visualBasePosition;
    private float repTimer = -1f;
    private float repDuration = 1.8f;
    private int repetitions;
    private int selectedWeight;
    private float currentSpeed;
    private float targetSpeed;
    private float previousMovingSpeed = 4f;
    private float distance;
    private bool sessionActive;
    private Renderer treadmillBeltRenderer;
    private MaterialPropertyBlock treadmillPropertyBlock;
    private float treadmillTextureOffset;
    private readonly List<Transform> bikeMovingParts = new List<Transform>();
    private readonly List<Quaternion> bikeMovingPartRotations = new List<Quaternion>();
    private float cardioPhase;
    private Transform sceneBar;
    private Transform sceneBarOriginalParent;
    private Vector3 sceneBarOriginalLocalPosition;
    private Quaternion sceneBarOriginalLocalRotation;
    private Vector3 sceneBarOriginalLocalScale;
    private Vector3 sceneBarExercisePosition;
    private Quaternion sceneBarExerciseRotation;
    private Vector3 sceneBarRendererCenterOffset;
    private Rigidbody sceneBarBody;
    private bool sceneBarWasKinematic;
    private bool sceneBarUsedGravity;
    private Transform loadedPlatesRoot;
    private Transform latPulldownBar;
    private Transform latPulldownCable;
    private Vector3 latBarOriginalLocalPosition;
    private Vector3 latCableOriginalLocalPosition;
    private Vector3 latBarExercisePosition;
    private Quaternion latBarExerciseRotation;

    public GymExerciseType ExerciseType => exerciseType;
    public string DisplayName => displayName;
    public Vector3 PlayerPosition => playerPosition;
    public Quaternion PlayerRotation => playerRotation;
    public bool IsCardio => exerciseType == GymExerciseType.Treadmill || exerciseType == GymExerciseType.ExerciseBike;
    public bool RequiresWeightSelection => exerciseType == GymExerciseType.FlatBenchPress ||
                                           exerciseType == GymExerciseType.InclineBenchPress ||
                                           exerciseType == GymExerciseType.PreacherCurl ||
                                           exerciseType == GymExerciseType.LatPulldown;
    public int[] WeightOptions => GetWeightOptions(exerciseType);
    public int SelectedWeight => selectedWeight;
    public int EmptyBarWeight => exerciseType == GymExerciseType.PreacherCurl ? 10 :
                                 (exerciseType == GymExerciseType.LatPulldown ? 0 : (RequiresWeightSelection ? 20 : 0));

    public static void CreateForScene()
    {
        Stations.Clear();
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None);
        List<Vector3> registeredPositions = new List<Vector3>();
        List<GymExerciseType> registeredTypes = new List<GymExerciseType>();

        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.GetComponentInParent<PlayerMovement>() != null || candidate.GetComponentInParent<GymExerciseStation>() != null)
            {
                continue;
            }

            if (!TryClassify(candidate.name, out GymExerciseType type))
            {
                continue;
            }

            Renderer[] renderers = candidate.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                continue;
            }

            Bounds bounds = GetCombinedBounds(renderers);
            if ((type == GymExerciseType.FlatBenchPress || type == GymExerciseType.InclineBenchPress ||
                 type == GymExerciseType.BarbellSquat) && FindNearestSceneWeight(candidate, bounds, "barbell") == null)
            {
                continue;
            }
            if (type == GymExerciseType.PreacherCurl && FindNearestSceneWeight(candidate, bounds, "ezbar") == null)
            {
                continue;
            }

            bool duplicate = false;
            for (int j = 0; j < registeredPositions.Count; j++)
            {
                float duplicateDistance = type == GymExerciseType.Treadmill || type == GymExerciseType.ExerciseBike
                    ? 0.75f : 2.2f;
                if (registeredTypes[j] == type && Vector3.Distance(registeredPositions[j], bounds.center) < duplicateDistance)
                {
                    duplicate = true;
                    break;
                }
            }

            if (duplicate)
            {
                continue;
            }

            GameObject stationObject = new GameObject($"Exercise Station - {GetDisplayName(type)}");
            stationObject.transform.SetParent(candidate, true);
            stationObject.transform.position = bounds.center;
            GymExerciseStation station = stationObject.AddComponent<GymExerciseStation>();
            station.Configure(type, candidate, bounds);
            Stations.Add(station);
            registeredPositions.Add(bounds.center);
            registeredTypes.Add(type);
        }
    }

    public static GymExerciseStation FindClosest(Vector3 position, float maxDistance)
    {
        GymExerciseStation closest = null;
        float bestDistance = maxDistance;
        for (int i = Stations.Count - 1; i >= 0; i--)
        {
            GymExerciseStation station = Stations[i];
            if (station == null)
            {
                Stations.RemoveAt(i);
                continue;
            }

            Vector3 offset = station.interactionPoint - position;
            offset.y = 0f;
            float candidateDistance = offset.magnitude;
            float interactionRange = station.GetInteractionRange();
            if (candidateDistance >= Mathf.Min(bestDistance, interactionRange))
            {
                continue;
            }
            if (candidateDistance < bestDistance)
            {
                bestDistance = candidateDistance;
                closest = station;
            }
        }

        return closest;
    }

    public void SelectWeight(int totalWeight)
    {
        int[] options = WeightOptions;
        for (int i = 0; i < options.Length; i++)
        {
            if (options[i] == totalWeight)
            {
                selectedWeight = totalWeight;
                return;
            }
        }

        selectedWeight = options.Length > 0 ? options[0] : 0;
    }

    public string GetInteractionPrompt()
    {
        return $"[F] Start exercise: {displayName}";
    }

    public string GetSessionHud()
    {
        if (exerciseType == GymExerciseType.Treadmill)
        {
            return $"TREADMILL  |  speed {currentSpeed:0.0} km/h  |  distance {distance / 1000f:0.00} km\n[W] faster   [S] slower   [SPACE] start/stop   [Q] exit";
        }

        if (exerciseType == GymExerciseType.ExerciseBike)
        {
            return $"EXERCISE BIKE  |  pace {currentSpeed:0.0}  |  distance {distance / 1000f:0.00} km\n[W] faster   [S] slower   [SPACE] start/stop   [Q] exit";
        }

        string repState = repTimer >= 0f ? "rep in progress" : "ready";
        if (!RequiresWeightSelection)
        {
            return $"{displayName.ToUpperInvariant()}  |  reps: {repetitions}  |  {repState}\n[SPACE] perform rep   [Q] exit";
        }

        return $"{displayName.ToUpperInvariant()}  |  {selectedWeight} kg  |  reps: {repetitions}  |  {repState}\n[SPACE] perform rep   [Q] exit";
    }

    public void BeginSession(Transform cameraTransform)
    {
        sessionActive = true;
        repTimer = -1f;
        repetitions = 0;
        distance = 0f;
        cardioPhase = 0f;
        if (RequiresWeightSelection && selectedWeight <= 0)
        {
            SelectWeight(WeightOptions[0]);
        }

        PrepareSceneEquipment();
        BuildFirstPersonVisual(cameraTransform);
    }

    public void EndSession()
    {
        sessionActive = false;
        repTimer = -1f;
        targetSpeed = 0f;
        currentSpeed = 0f;
        RestoreSceneEquipment();
        if (visualRoot != null)
        {
            Destroy(visualRoot.gameObject);
            visualRoot = null;
        }
    }

    public void TickSession(float deltaTime, bool actionPressed, bool increasePressed, bool decreasePressed)
    {
        if (!sessionActive)
        {
            return;
        }

        if (IsCardio)
        {
            TickCardio(deltaTime, actionPressed, increasePressed, decreasePressed);
        }
        else
        {
            TickStrength(deltaTime, actionPressed);
        }

        UpdateSceneEquipment(GetMotionCurve());
        UpdateFirstPersonVisual();
    }

    public void GetCameraPose(out Vector3 localPosition, out Quaternion localRotation)
    {
        float motion = GetMotionCurve();
        switch (exerciseType)
        {
            case GymExerciseType.FlatBenchPress:
                localPosition = new Vector3(0f, 0.64f + motion * 0.015f, -0.2f);
                localRotation = Quaternion.Euler(-58f + motion * 3f, 0f, 0f);
                break;
            case GymExerciseType.InclineBenchPress:
                localPosition = new Vector3(0.12f, 1.03f + motion * 0.02f, -0.18f);
                localRotation = Quaternion.Euler(-30f + motion * 3f, 0f, 0f);
                break;
            case GymExerciseType.BarbellSquat:
                localPosition = new Vector3(0f, 1.66f - motion * 0.54f, -0.34f);
                localRotation = Quaternion.Euler(2f + motion * 7f, 0f, 0f);
                break;
            case GymExerciseType.PreacherCurl:
                localPosition = new Vector3(0f, 1.67f - motion * 0.025f, 0.18f);
                localRotation = Quaternion.Euler(16f - motion * 4f, 0f, 0f);
                break;
            case GymExerciseType.Dips:
                localPosition = new Vector3(0f, 1.4f - motion * 0.42f, -0.32f);
                localRotation = Quaternion.Euler(9f + motion * 5f, 0f, 0f);
                break;
            case GymExerciseType.Treadmill:
                localPosition = new Vector3(GetCardioSway(0.012f), 1.62f + GetCardioBob(0.035f), 0.08f);
                localRotation = Quaternion.Euler(5f + GetCardioSway(0.7f), 0f, GetCardioSway(0.9f));
                break;
            case GymExerciseType.ExerciseBike:
                localPosition = new Vector3(GetCardioSway(0.005f), 1.9f + GetCardioBob(0.015f), -0.32f);
                localRotation = Quaternion.Euler(24f + GetCardioSway(0.25f), 0f, GetCardioSway(0.45f));
                break;
            case GymExerciseType.LatPulldown:
                localPosition = new Vector3(0f, 1.5f - motion * 0.035f, -0.12f);
                localRotation = Quaternion.Euler(-22f + motion * 6f, 0f, 0f);
                break;
            default:
                localPosition = new Vector3(0f, 1.34f, -0.32f);
                localRotation = Quaternion.Euler(17f, 0f, 0f);
                break;
        }
    }

    public float GetCameraFieldOfView(float baseFieldOfView)
    {
        if (!IsCardio)
        {
            return baseFieldOfView;
        }

        float maximum = exerciseType == GymExerciseType.Treadmill ? 18f : 14f;
        return baseFieldOfView + Mathf.Clamp01(currentSpeed / maximum) * 9f;
    }

    private float GetCardioBob(float maximumAmplitude)
    {
        float maximum = exerciseType == GymExerciseType.Treadmill ? 18f : 14f;
        return Mathf.Sin(cardioPhase) * maximumAmplitude * Mathf.Clamp01(currentSpeed / maximum);
    }

    private float GetCardioSway(float maximumAmplitude)
    {
        float maximum = exerciseType == GymExerciseType.Treadmill ? 18f : 14f;
        return Mathf.Sin(cardioPhase * 0.5f + Mathf.PI * 0.5f) * maximumAmplitude * Mathf.Clamp01(currentSpeed / maximum);
    }

    private void Configure(GymExerciseType type, Transform equipment, Bounds bounds)
    {
        exerciseType = type;
        displayName = GetDisplayName(type);
        equipmentRoot = equipment;
        sceneBar = type == GymExerciseType.PreacherCurl ? FindNearestSceneWeight(equipment, bounds, "ezbar") :
                   ((type == GymExerciseType.FlatBenchPress || type == GymExerciseType.InclineBenchPress || type == GymExerciseType.BarbellSquat)
                       ? FindNearestSceneWeight(equipment, bounds, "barbell") : null);
        Vector3 forward = Vector3.ProjectOnPlane(equipment.forward, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.01f)
        {
            forward = Vector3.forward;
        }

        float floorY = bounds.min.y + 0.06f;
        Vector3 offset = GetPlayerOffset(type, forward);
        playerPosition = new Vector3(bounds.center.x + offset.x, floorY, bounds.center.z + offset.z);
        if (type == GymExerciseType.PreacherCurl && sceneBar != null)
        {
            Renderer[] barRenderers = sceneBar.GetComponentsInChildren<Renderer>(true);
            Vector3 barCenter = barRenderers.Length > 0 ? GetCombinedBounds(barRenderers).center : sceneBar.position;
            Vector3 approach = Vector3.ProjectOnPlane(barCenter - bounds.center, Vector3.up);
            if (approach.sqrMagnitude < 0.01f) approach = forward;
            approach.Normalize();
            Vector3 seatedCenter = bounds.center - approach * 0.42f;
            playerPosition = new Vector3(seatedCenter.x, floorY, seatedCenter.z);
        }
        interactionPoint = playerPosition;
        Vector3 lookDirection = Vector3.ProjectOnPlane(bounds.center - playerPosition, Vector3.up);
        if (lookDirection.sqrMagnitude < 0.01f)
        {
            lookDirection = forward;
        }

        playerRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up) * GetFacingCorrection(type);
        if (type == GymExerciseType.InclineBenchPress && sceneBar != null)
        {
            Renderer[] barRenderers = sceneBar.GetComponentsInChildren<Renderer>(true);
            Vector3 barCenter = barRenderers.Length > 0 ? GetCombinedBounds(barRenderers).center : sceneBar.position;
            Vector3 barLook = Vector3.ProjectOnPlane(barCenter - playerPosition, Vector3.up);
            if (barLook.sqrMagnitude > 0.01f)
            {
                playerRotation = Quaternion.LookRotation(barLook.normalized, Vector3.up);
            }
        }
        if (type == GymExerciseType.PreacherCurl && sceneBar != null)
        {
            Renderer[] barRenderers = sceneBar.GetComponentsInChildren<Renderer>(true);
            Vector3 barCenter = barRenderers.Length > 0 ? GetCombinedBounds(barRenderers).center : sceneBar.position;
            Vector3 seatedLook = Vector3.ProjectOnPlane(barCenter - playerPosition, Vector3.up);
            if (seatedLook.sqrMagnitude > 0.01f)
            {
                playerRotation = Quaternion.LookRotation(seatedLook.normalized, Vector3.up);
            }
        }
        if (type == GymExerciseType.LatPulldown)
        {
            ConfigureLatPulldownParts(equipment);
        }
        repDuration = GetRepDuration(type);
        if (RequiresWeightSelection)
        {
            selectedWeight = WeightOptions[0];
        }

        ConfigureActualCardioParts(equipment);
    }

    private void TickStrength(float deltaTime, bool actionPressed)
    {
        if (actionPressed && repTimer < 0f)
        {
            repTimer = 0f;
        }

        if (repTimer < 0f)
        {
            return;
        }

        repTimer += deltaTime;
        if (repTimer >= repDuration)
        {
            repTimer = -1f;
            repetitions++;
        }
    }

    private void TickCardio(float deltaTime, bool actionPressed, bool increasePressed, bool decreasePressed)
    {
        float maximum = exerciseType == GymExerciseType.Treadmill ? 18f : 14f;
        if (increasePressed)
        {
            targetSpeed = Mathf.Min(maximum, Mathf.Max(1f, targetSpeed + 1f));
            previousMovingSpeed = targetSpeed;
        }

        if (decreasePressed)
        {
            targetSpeed = Mathf.Max(0f, targetSpeed - 1f);
            if (targetSpeed > 0f)
            {
                previousMovingSpeed = targetSpeed;
            }
        }

        if (actionPressed)
        {
            if (targetSpeed > 0f || currentSpeed > 0.15f)
            {
                if (targetSpeed > 0f)
                {
                    previousMovingSpeed = targetSpeed;
                }

                targetSpeed = 0f;
            }
            else
            {
                targetSpeed = Mathf.Max(1f, previousMovingSpeed);
            }
        }

        currentSpeed = Mathf.MoveTowards(currentSpeed, targetSpeed, deltaTime * (targetSpeed > currentSpeed ? 1.6f : 2.8f));
        distance += currentSpeed / 3.6f * deltaTime;
        cardioPhase += currentSpeed * deltaTime * (exerciseType == GymExerciseType.Treadmill ? 1.35f : 0.9f);
        AnimateActualCardioParts(deltaTime);
    }

    private void ConfigureActualCardioParts(Transform equipment)
    {
        if (exerciseType == GymExerciseType.Treadmill)
        {
            Renderer[] renderers = equipment.GetComponentsInChildren<Renderer>(true);
            float bestScore = float.MinValue;
            for (int i = 0; i < renderers.Length; i++)
            {
                string lower = renderers[i].name.ToLowerInvariant();
                float score = (lower.Contains("belt") || lower.Contains("track") || lower.Contains("tread")) ? 1000f : 0f;
                score += renderers[i].bounds.size.x * renderers[i].bounds.size.z - renderers[i].bounds.center.y * 0.05f;
                if (score > bestScore)
                {
                    bestScore = score;
                    treadmillBeltRenderer = renderers[i];
                }
            }

            treadmillPropertyBlock = new MaterialPropertyBlock();
        }
        else if (exerciseType == GymExerciseType.ExerciseBike)
        {
            Transform[] parts = equipment.GetComponentsInChildren<Transform>(true);
            Renderer[] equipmentRenderers = equipment.GetComponentsInChildren<Renderer>(true);
            Bounds equipmentBounds = GetCombinedBounds(equipmentRenderers);
            for (int i = 0; i < parts.Length; i++)
            {
                string lower = parts[i].name.ToLowerInvariant();
                Renderer renderer = parts[i].GetComponent<Renderer>();
                bool namedDrivePart = lower.Contains("pedal") || lower.Contains("crank") || lower.Contains("wheel");
                bool genericDrivePart = renderer != null && lower.Contains("cylinder") && IsLikelyBikeDrivePart(renderer.bounds, equipmentBounds);
                if (namedDrivePart || genericDrivePart)
                {
                    bikeMovingParts.Add(parts[i]);
                    bikeMovingPartRotations.Add(parts[i].localRotation);
                }
            }
        }
    }

    private static bool IsLikelyBikeDrivePart(Bounds partBounds, Bounds equipmentBounds)
    {
        float normalizedHeight = Mathf.InverseLerp(equipmentBounds.min.y, equipmentBounds.max.y, partBounds.center.y);
        Vector3 horizontalOffset = Vector3.ProjectOnPlane(partBounds.center - equipmentBounds.center, Vector3.up);
        float horizontalRadius = Mathf.Max(equipmentBounds.extents.x, equipmentBounds.extents.z);
        float partSize = Mathf.Max(partBounds.size.x, Mathf.Max(partBounds.size.y, partBounds.size.z));
        return normalizedHeight > 0.18f && normalizedHeight < 0.58f &&
               horizontalOffset.magnitude < horizontalRadius * 0.72f &&
               partSize < Mathf.Max(0.65f, horizontalRadius * 0.75f);
    }

    private void AnimateActualCardioParts(float deltaTime)
    {
        if (exerciseType == GymExerciseType.Treadmill && treadmillBeltRenderer != null)
        {
            treadmillTextureOffset = Mathf.Repeat(treadmillTextureOffset + currentSpeed * deltaTime * 0.045f, 1f);
            treadmillBeltRenderer.GetPropertyBlock(treadmillPropertyBlock);
            treadmillPropertyBlock.SetVector("_BaseMap_ST", new Vector4(1f, 1f, 0f, -treadmillTextureOffset));
            treadmillPropertyBlock.SetVector("_MainTex_ST", new Vector4(1f, 1f, 0f, -treadmillTextureOffset));
            treadmillBeltRenderer.SetPropertyBlock(treadmillPropertyBlock);
        }

        if (exerciseType == GymExerciseType.ExerciseBike && currentSpeed > 0.01f)
        {
            for (int i = 0; i < bikeMovingParts.Count; i++)
            {
                if (bikeMovingParts[i] != null)
                {
                    bikeMovingParts[i].localRotation = bikeMovingPartRotations[i] * Quaternion.AngleAxis(cardioPhase * Mathf.Rad2Deg, Vector3.forward);
                }
            }
        }
    }

    private float GetMotionCurve()
    {
        if (repTimer < 0f || IsCardio)
        {
            return 0f;
        }

        return Mathf.Sin(Mathf.Clamp01(repTimer / repDuration) * Mathf.PI);
    }

    private void BuildFirstPersonVisual(Transform cameraTransform)
    {
        if (visualRoot != null)
        {
            Destroy(visualRoot.gameObject);
        }

        if (!RequiresWeightSelection)
        {
            visualRoot = null;
            return;
        }

        GameObject root = new GameObject("Exercise First Person Visual");
        visualRoot = root.transform;
        visualRoot.SetParent(cameraTransform, false);

        switch (exerciseType)
        {
            case GymExerciseType.FlatBenchPress:
            case GymExerciseType.InclineBenchPress:
            case GymExerciseType.PreacherCurl:
            case GymExerciseType.LatPulldown:
            case GymExerciseType.BarbellSquat:
                break;
        }

        visualBasePosition = visualRoot.localPosition;
    }

    private void UpdateFirstPersonVisual()
    {
        if (visualRoot == null)
        {
            return;
        }

        float motion = GetMotionCurve();
        visualRoot.localPosition = visualBasePosition;
        visualRoot.localRotation = Quaternion.identity;
        switch (exerciseType)
        {
            case GymExerciseType.BarbellSquat:
                // The bar stays fixed relative to the camera/upper back, so it follows the same squat path.
                break;
        }
    }

    private void PrepareSceneEquipment()
    {
        if (sceneBar != null)
        {
            sceneBarOriginalParent = sceneBar.parent;
            sceneBarOriginalLocalPosition = sceneBar.localPosition;
            sceneBarOriginalLocalRotation = sceneBar.localRotation;
            sceneBarOriginalLocalScale = sceneBar.localScale;
            sceneBarExercisePosition = sceneBar.position;
            sceneBarExerciseRotation = sceneBar.rotation;
            Renderer[] sceneBarRenderers = sceneBar.GetComponentsInChildren<Renderer>(true);
            sceneBarRendererCenterOffset = sceneBarRenderers.Length > 0
                ? sceneBar.position - GetCombinedBounds(sceneBarRenderers).center
                : Vector3.zero;
            sceneBarBody = sceneBar.GetComponent<Rigidbody>();
            if (sceneBarBody == null) sceneBarBody = sceneBar.GetComponentInChildren<Rigidbody>();
            if (sceneBarBody != null)
            {
                sceneBarWasKinematic = sceneBarBody.isKinematic;
                sceneBarUsedGravity = sceneBarBody.useGravity;
                if (!sceneBarBody.isKinematic)
                {
                    sceneBarBody.linearVelocity = Vector3.zero;
                    sceneBarBody.angularVelocity = Vector3.zero;
                }
                sceneBarBody.useGravity = false;
                sceneBarBody.isKinematic = true;
            }

            Vector3 playerForward = playerRotation * Vector3.forward;
            if (exerciseType == GymExerciseType.FlatBenchPress)
            {
                // Bottom of the path is directly over the chest; the upward phase presses away from it.
                Vector3 desiredBarCenter = playerPosition + Vector3.up * 0.86f + playerForward * 0.16f;
                sceneBarExercisePosition = desiredBarCenter + sceneBarRendererCenterOffset;
            }
            else if (exerciseType == GymExerciseType.InclineBenchPress)
            {
                Vector3 desiredBarCenter = playerPosition + Vector3.up * 1.17f + playerForward * 0.2f;
                sceneBarExercisePosition = desiredBarCenter + sceneBarRendererCenterOffset;
            }
            else if (exerciseType == GymExerciseType.BarbellSquat)
            {
                // Keep the real rack bar across the upper back, behind the first-person camera.
                Vector3 desiredBarCenter = playerPosition + Vector3.up * 1.45f - playerForward * 0.2f;
                sceneBarExercisePosition = desiredBarCenter + sceneBarRendererCenterOffset;
            }
            else if (exerciseType == GymExerciseType.PreacherCurl)
            {
                Vector3 desiredBarCenter = playerPosition + Vector3.up * 1.1f + playerForward * 0.82f;
                sceneBarExercisePosition = desiredBarCenter + sceneBarRendererCenterOffset;
            }

            BuildPlatesOnSceneBar();
        }

        if (latPulldownBar != null)
        {
            latBarOriginalLocalPosition = latPulldownBar.localPosition;
            latBarExerciseRotation = latPulldownBar.rotation;
            Vector3 playerForward = playerRotation * Vector3.forward;
            latBarExercisePosition = playerPosition + Vector3.up * 2.02f + playerForward * 0.56f;
            latPulldownBar.SetPositionAndRotation(latBarExercisePosition, latBarExerciseRotation);
        }
        if (latPulldownCable != null) latCableOriginalLocalPosition = latPulldownCable.localPosition;
    }

    private void UpdateSceneEquipment(float motion)
    {
        if (sceneBar != null)
        {
            if (exerciseType == GymExerciseType.PreacherCurl)
            {
                Vector3 playerForward = playerRotation * Vector3.forward;
                float curlRadians = motion * 85f * Mathf.Deg2Rad;
                float verticalTravel = Mathf.Sin(curlRadians) * 0.43f;
                float towardPlayerTravel = (1f - Mathf.Cos(curlRadians)) * 0.22f;
                Vector3 curlPosition = sceneBarExercisePosition + Vector3.up * verticalTravel - playerForward * towardPlayerTravel;
                sceneBar.SetPositionAndRotation(curlPosition, sceneBarExerciseRotation);
            }
            else if (exerciseType == GymExerciseType.BarbellSquat)
            {
                sceneBar.SetPositionAndRotation(sceneBarExercisePosition + Vector3.down * motion * 0.54f, sceneBarExerciseRotation);
            }
            else if (exerciseType == GymExerciseType.FlatBenchPress)
            {
                Vector3 playerForward = playerRotation * Vector3.forward;
                sceneBar.SetPositionAndRotation(sceneBarExercisePosition + Vector3.up * motion * 0.5f - playerForward * motion * 0.04f, sceneBarExerciseRotation);
            }
            else if (exerciseType == GymExerciseType.InclineBenchPress)
            {
                Vector3 playerForward = playerRotation * Vector3.forward;
                sceneBar.SetPositionAndRotation(sceneBarExercisePosition + Vector3.up * motion * 0.42f - playerForward * motion * 0.05f, sceneBarExerciseRotation);
            }
            else
            {
                sceneBar.SetPositionAndRotation(sceneBarExercisePosition + Vector3.up * motion * 0.44f, sceneBarExerciseRotation);
            }

            if (loadedPlatesRoot != null)
            {
                loadedPlatesRoot.SetPositionAndRotation(sceneBar.position, sceneBar.rotation);
            }
        }

        if (exerciseType == GymExerciseType.LatPulldown)
        {
            if (latPulldownBar != null)
            {
                latPulldownBar.SetPositionAndRotation(latBarExercisePosition + Vector3.down * motion * 0.46f, latBarExerciseRotation);
            }
            if (latPulldownCable != null)
            {
                Vector3 cableTravel = latPulldownCable.parent != null ? latPulldownCable.parent.InverseTransformDirection(Vector3.down) : Vector3.down;
                latPulldownCable.localPosition = latCableOriginalLocalPosition + cableTravel * motion * 0.23f;
            }
        }
    }

    private void RestoreSceneEquipment()
    {
        if (sceneBar != null)
        {
            sceneBar.SetParent(sceneBarOriginalParent, false);
            sceneBar.localPosition = sceneBarOriginalLocalPosition;
            sceneBar.localRotation = sceneBarOriginalLocalRotation;
            sceneBar.localScale = sceneBarOriginalLocalScale;
            if (sceneBarBody != null)
            {
                sceneBarBody.isKinematic = sceneBarWasKinematic;
                sceneBarBody.useGravity = sceneBarUsedGravity;
                if (!sceneBarBody.isKinematic)
                {
                    sceneBarBody.linearVelocity = Vector3.zero;
                    sceneBarBody.angularVelocity = Vector3.zero;
                }
            }
        }

        if (loadedPlatesRoot != null)
        {
            Destroy(loadedPlatesRoot.gameObject);
            loadedPlatesRoot = null;
        }

        if (latPulldownBar != null) latPulldownBar.localPosition = latBarOriginalLocalPosition;
        if (latPulldownCable != null) latPulldownCable.localPosition = latCableOriginalLocalPosition;
        for (int i = 0; i < bikeMovingParts.Count && i < bikeMovingPartRotations.Count; i++)
        {
            if (bikeMovingParts[i] != null) bikeMovingParts[i].localRotation = bikeMovingPartRotations[i];
        }
    }

    private void BuildPlatesOnSceneBar()
    {
        List<int> platesPerSide = GetPlatesPerSide();
        if (platesPerSide.Count == 0 || sceneBar == null) return;

        Renderer[] barRenderers = sceneBar.GetComponentsInChildren<Renderer>(true);
        if (barRenderers.Length == 0) return;
        Bounds barBounds = GetCombinedBounds(barRenderers);
        Vector3 size = barBounds.size;
        Vector3 worldAxis = size.x >= size.y && size.x >= size.z ? Vector3.right : (size.y >= size.z ? Vector3.up : Vector3.forward);
        float halfLength = worldAxis == Vector3.right ? size.x * 0.5f : (worldAxis == Vector3.up ? size.y * 0.5f : size.z * 0.5f);

        GameObject platesObject = new GameObject("Exercise Loaded Plates");
        loadedPlatesRoot = platesObject.transform;
        loadedPlatesRoot.SetPositionAndRotation(sceneBar.position, sceneBar.rotation);
        Vector3 localAxis = loadedPlatesRoot.InverseTransformDirection(worldAxis).normalized;
        const float spacing = 0.065f;
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < platesPerSide.Count; i++)
            {
                int weight = platesPerSide[i];
                float diameter = weight == 20 ? 0.45f : (weight == 10 ? 0.38f : 0.32f);
                Vector3 worldPosition = barBounds.center + worldAxis * side * (Mathf.Max(0.12f, halfLength - 0.16f) + i * spacing);
                Vector3 localPosition = loadedPlatesRoot.InverseTransformPoint(worldPosition);
                Transform plate = CreateNormalizedAssetVisual($"Plate{weight}", loadedPlatesRoot, localPosition, diameter, false, $"Plate {weight}kg");
                if (plate != null) plate.localRotation = Quaternion.FromToRotation(Vector3.right, localAxis) * plate.localRotation;
            }
        }
    }

    private void BuildLoadedAssetBar(Transform parent, string barAssetName, Vector3 position, float barWidth, float plateEdge)
    {
        CreateNormalizedAssetVisual(barAssetName, parent, position, barWidth, true, $"Asset {barAssetName} Visual");
        List<int> platesPerSide = GetPlatesPerSide();
        const float stackSpacing = 0.055f;
        for (int side = -1; side <= 1; side += 2)
        {
            for (int i = 0; i < platesPerSide.Count; i++)
            {
                int plateWeight = platesPerSide[i];
                float diameter = plateWeight == 20 ? 0.34f : (plateWeight == 10 ? 0.3f : 0.26f);
                Vector3 platePosition = position + Vector3.right * side * (plateEdge + i * stackSpacing);
                CreateNormalizedAssetVisual($"Plate{plateWeight}", parent, platePosition, diameter, false, $"Asset Plate {plateWeight}kg Visual");
            }
        }
    }

    private List<int> GetPlatesPerSide()
    {
        List<int> plates = new List<int>();
        int remaining = Mathf.Max(0, selectedWeight - EmptyBarWeight) / 2;
        int[] available = { 20, 10, 5 };
        for (int i = 0; i < available.Length; i++)
        {
            while (remaining >= available[i])
            {
                plates.Add(available[i]);
                remaining -= available[i];
            }
        }

        return plates;
    }

    private static Transform CreateNormalizedAssetVisual(string assetPrefix, Transform parent, Vector3 localPosition, float desiredMajorSize, bool alignLongestToX, string visualName)
    {
        Transform template = FindAssetTemplate(assetPrefix);
        if (template == null)
        {
            Debug.LogWarning($"Gym exercise visual could not find asset template '{assetPrefix}'.");
            return null;
        }

        GameObject wrapperObject = new GameObject(visualName);
        Transform wrapper = wrapperObject.transform;
        wrapper.SetParent(parent, false);
        wrapper.localPosition = localPosition;

        GameObject clone = Object.Instantiate(template.gameObject);
        clone.name = $"{assetPrefix} Asset Clone";
        clone.SetActive(true);
        clone.transform.SetParent(wrapper, false);
        clone.transform.localPosition = Vector3.zero;
        clone.transform.localRotation = Quaternion.identity;
        clone.transform.localScale = Vector3.one;
        StripPhysics(clone);

        Renderer[] renderers = clone.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            Object.Destroy(wrapperObject);
            return null;
        }

        Bounds localBounds = GetBoundsRelativeTo(wrapper, renderers);
        clone.transform.localPosition -= localBounds.center;
        Vector3 size = localBounds.size;
        Quaternion alignment = Quaternion.identity;
        if (alignLongestToX)
        {
            if (size.y >= size.x && size.y >= size.z) alignment = Quaternion.Euler(0f, 0f, -90f);
            else if (size.z >= size.x && size.z >= size.y) alignment = Quaternion.Euler(0f, 90f, 0f);
        }
        else
        {
            if (size.y <= size.x && size.y <= size.z) alignment = Quaternion.Euler(0f, 0f, -90f);
            else if (size.z <= size.x && size.z <= size.y) alignment = Quaternion.Euler(0f, 90f, 0f);
        }

        wrapper.localRotation = alignment;
        float major = alignLongestToX ? Mathf.Max(size.x, Mathf.Max(size.y, size.z)) : Mathf.Max(size.x, Mathf.Max(size.y, size.z));
        wrapper.localScale = Vector3.one * (desiredMajorSize / Mathf.Max(major, 0.0001f));
        return wrapper;
    }

    private static Transform FindAssetTemplate(string prefix)
    {
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        string expected = prefix.ToLowerInvariant();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.GetComponentInParent<PlayerMovement>() != null || candidate.name.Contains("Asset Clone"))
            {
                continue;
            }

            string lower = candidate.name.ToLowerInvariant().Replace(" ", string.Empty);
            if (lower.StartsWith(expected.ToLowerInvariant()) && candidate.GetComponentInChildren<Renderer>(true) != null)
            {
                return candidate;
            }
        }

        return null;
    }

    private static void StripPhysics(GameObject clone)
    {
        Collider[] colliders = clone.GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < colliders.Length; i++)
        {
            colliders[i].enabled = false;
            Object.Destroy(colliders[i]);
        }
        Rigidbody[] bodies = clone.GetComponentsInChildren<Rigidbody>(true);
        for (int i = 0; i < bodies.Length; i++)
        {
            bodies[i].linearVelocity = Vector3.zero;
            bodies[i].angularVelocity = Vector3.zero;
            bodies[i].useGravity = false;
            bodies[i].isKinematic = true;
            Object.Destroy(bodies[i]);
        }
        PickupItem[] pickups = clone.GetComponentsInChildren<PickupItem>(true);
        for (int i = 0; i < pickups.Length; i++)
        {
            pickups[i].enabled = false;
            Object.Destroy(pickups[i]);
        }
    }

    private static Bounds GetBoundsRelativeTo(Transform relativeTo, Renderer[] renderers)
    {
        Bounds result = new Bounds(relativeTo.InverseTransformPoint(renderers[0].bounds.center), Vector3.zero);
        for (int i = 0; i < renderers.Length; i++)
        {
            Bounds bounds = renderers[i].bounds;
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            for (int x = 0; x <= 1; x++)
            for (int y = 0; y <= 1; y++)
            for (int z = 0; z <= 1; z++)
            {
                result.Encapsulate(relativeTo.InverseTransformPoint(new Vector3(x == 0 ? min.x : max.x, y == 0 ? min.y : max.y, z == 0 ? min.z : max.z)));
            }
        }

        return result;
    }

    private static Bounds GetCombinedBounds(Renderer[] renderers)
    {
        Bounds bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        return bounds;
    }

    private float GetInteractionRange()
    {
        return IsCardio ? 3.15f : 2.35f;
    }

    private static string NormalizeWeightName(string name)
    {
        return name.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
    }

    private static Transform FindNearestSceneWeight(Transform equipment, Bounds equipmentBounds, string prefix)
    {
        Transform best = null;
        float bestDistance = float.MaxValue;
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        Bounds searchArea = equipmentBounds;
        searchArea.Expand(new Vector3(1.8f, 1.4f, 1.8f));
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            if (candidate == null || candidate.GetComponentInParent<PlayerMovement>() != null || candidate.name.Contains("Asset Clone")) continue;
            PickupItem pickup = candidate.GetComponentInParent<PickupItem>();
            if (pickup == null || !IsExpectedWeightType(pickup.ItemType, prefix)) continue;
            Transform weightRoot = pickup.transform;
            Renderer renderer = weightRoot.GetComponentInChildren<Renderer>(true);
            if (renderer == null) continue;
            Vector3 center = renderer.bounds.center;
            if (!searchArea.Contains(center)) continue;
            float distance = (center - equipmentBounds.center).sqrMagnitude;
            if (weightRoot.IsChildOf(equipment)) distance *= 0.1f;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = weightRoot;
            }
        }

        return best;
    }

    private static bool IsExpectedWeightType(WeightType itemType, string prefix)
    {
        return prefix == "barbell" ? itemType == WeightType.Barbell : itemType == WeightType.EzBar;
    }

    private void ConfigureLatPulldownParts(Transform equipment)
    {
        latPulldownBar = CreateLatPulldownAttachmentGroup(equipment);
        if (latPulldownBar != null)
        {
            latPulldownCable = null;
            return;
        }

        Transform[] parts = equipment.GetComponentsInChildren<Transform>(true);
        Renderer[] equipmentRenderers = equipment.GetComponentsInChildren<Renderer>(true);
        Bounds equipmentBounds = GetCombinedBounds(equipmentRenderers);
        float bestBarScore = float.MinValue;
        float bestCableScore = float.MinValue;
        for (int i = 0; i < parts.Length; i++)
        {
            Transform part = parts[i];
            if (part == equipment) continue;
            string lower = part.name.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
            Renderer renderer = part.GetComponent<Renderer>();
            Bounds bounds = renderer != null ? renderer.bounds : new Bounds(part.position, Vector3.zero);
            Vector3 size = bounds.size;
            float horizontalSpan = Mathf.Max(size.x, size.z);
            float normalizedHeight = Mathf.InverseLerp(equipmentBounds.min.y, equipmentBounds.max.y, bounds.center.y);
            float centerDistance = Vector3.ProjectOnPlane(bounds.center - equipmentBounds.center, Vector3.up).magnitude;
            float barAspect = horizontalSpan / Mathf.Max(size.y, 0.01f);
            float cableAspect = size.y / Mathf.Max(horizontalSpan, 0.01f);
            Vector3 fromPlayer = Vector3.ProjectOnPlane(bounds.center - playerPosition, Vector3.up);
            float inFront = fromPlayer.sqrMagnitude > 0.01f
                ? Vector3.Dot((playerRotation * Vector3.forward).normalized, fromPlayer.normalized)
                : 0f;

            float barScore = (lower.Contains("latbar") || lower.Contains("pulldownbar") ? 2000f : 0f) +
                             (lower.Contains("bar") || lower.Contains("handle") ? 800f : 0f) +
                             barAspect * 35f + (1f - Mathf.Abs(normalizedHeight - 0.55f)) * 220f + inFront * 180f - centerDistance * 10f;
            if (!lower.Contains("cable") && !lower.Contains("weight") && normalizedHeight > 0.25f && normalizedHeight < 0.82f &&
                barAspect > 1.8f && barScore > bestBarScore && renderer != null)
            {
                bestBarScore = barScore;
                latPulldownBar = part;
            }

            float cableScore = (lower.Contains("cable") || lower.Contains("wire") || lower.Contains("rope") ? 2000f : 0f) +
                               cableAspect * 45f + normalizedHeight * 100f - centerDistance * 20f;
            if (normalizedHeight > 0.42f && cableAspect > 2.5f && cableScore > bestCableScore && renderer != null)
            {
                bestCableScore = cableScore;
                latPulldownCable = part;
            }
        }

        if (latPulldownCable == latPulldownBar) latPulldownCable = null;
    }

    private static Transform CreateLatPulldownAttachmentGroup(Transform equipment)
    {
        Transform existing = equipment.Find("Lat Pulldown Moving Attachment");
        if (existing != null)
        {
            return existing;
        }

        string[] requiredNames = { "cylinder.028", "cylinder.029", "cube.013", "cylinder.024", "beziercurve" };
        Transform[] descendants = equipment.GetComponentsInChildren<Transform>(true);
        List<Transform> attachmentParts = new List<Transform>();
        for (int nameIndex = 0; nameIndex < requiredNames.Length; nameIndex++)
        {
            Transform match = null;
            for (int i = 0; i < descendants.Length; i++)
            {
                string normalized = descendants[i].name.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
                if (normalized == requiredNames[nameIndex])
                {
                    match = descendants[i];
                    break;
                }
            }

            if (match == null)
            {
                Debug.LogWarning($"Lat pulldown attachment is missing scene part '{requiredNames[nameIndex]}'. Falling back to geometry detection.");
                return null;
            }

            attachmentParts.Add(match);
        }

        Renderer firstRenderer = attachmentParts[0].GetComponentInChildren<Renderer>(true);
        Vector3 groupCenter = firstRenderer != null ? firstRenderer.bounds.center : attachmentParts[0].position;
        Bounds groupBounds = new Bounds(groupCenter, Vector3.zero);
        for (int i = 0; i < attachmentParts.Count; i++)
        {
            Renderer[] renderers = attachmentParts[i].GetComponentsInChildren<Renderer>(true);
            for (int r = 0; r < renderers.Length; r++) groupBounds.Encapsulate(renderers[r].bounds);
        }

        GameObject groupObject = new GameObject("Lat Pulldown Moving Attachment");
        Transform group = groupObject.transform;
        group.SetPositionAndRotation(groupBounds.center, equipment.rotation);
        group.SetParent(equipment, true);
        for (int i = 0; i < attachmentParts.Count; i++) attachmentParts[i].SetParent(group, true);
        return group;
    }

    private static int[] GetWeightOptions(GymExerciseType type)
    {
        switch (type)
        {
            case GymExerciseType.FlatBenchPress: return FlatBenchWeights;
            case GymExerciseType.InclineBenchPress: return InclineBenchWeights;
            case GymExerciseType.BarbellSquat: return SquatWeights;
            case GymExerciseType.PreacherCurl: return PreacherWeights;
            case GymExerciseType.LatPulldown: return LatPulldownWeights;
            default: return new int[0];
        }
    }

    private static Vector3 GetPlayerOffset(GymExerciseType type, Vector3 forward)
    {
        switch (type)
        {
            case GymExerciseType.Treadmill: return Vector3.zero;
            case GymExerciseType.ExerciseBike: return Vector3.zero;
            case GymExerciseType.PreacherCurl: return -forward * 0.95f;
            case GymExerciseType.Dips: return Vector3.zero;
            case GymExerciseType.BarbellSquat: return -forward * 0.15f;
            case GymExerciseType.LatPulldown: return -forward * 0.3f;
            default: return -forward * 0.55f;
        }
    }

    private static Quaternion GetFacingCorrection(GymExerciseType type)
    {
        switch (type)
        {
            case GymExerciseType.FlatBenchPress:
            case GymExerciseType.InclineBenchPress:
            case GymExerciseType.BarbellSquat:
                return Quaternion.Euler(0f, 180f, 0f);
            case GymExerciseType.Treadmill:
                return Quaternion.Euler(0f, 180f, 0f);
            case GymExerciseType.ExerciseBike:
            case GymExerciseType.Dips:
                return Quaternion.Euler(0f, -90f, 0f);
            case GymExerciseType.PreacherCurl:
                return Quaternion.identity;
            case GymExerciseType.LatPulldown:
                return Quaternion.Euler(0f, 180f, 0f);
            default:
                return Quaternion.identity;
        }
    }

    private static float GetRepDuration(GymExerciseType type)
    {
        switch (type)
        {
            case GymExerciseType.BarbellSquat: return 2.35f;
            case GymExerciseType.Dips: return 1.65f;
            case GymExerciseType.PreacherCurl: return 1.9f;
            case GymExerciseType.LatPulldown: return 2.1f;
            default: return 1.8f;
        }
    }

    private static bool TryClassify(string objectName, out GymExerciseType type)
    {
        string lower = objectName.ToLowerInvariant().Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty);
        if (lower.Contains("treadmill")) { type = GymExerciseType.Treadmill; return true; }
        if (lower.Contains("bike") || lower.Contains("sobnokolo")) { type = GymExerciseType.ExerciseBike; return true; }
        if (lower.Contains("preacher")) { type = GymExerciseType.PreacherCurl; return true; }
        if (lower.Contains("latpulldown")) { type = GymExerciseType.LatPulldown; return true; }
        if (lower.Contains("dips") || lower.Contains("dipstation")) { type = GymExerciseType.Dips; return true; }
        if (lower.Contains("cage") || lower.Contains("powerrack") || lower.Contains("squatrack") || lower.Contains("smithmachine")) { type = GymExerciseType.BarbellSquat; return true; }
        if (lower.Contains("inclinebench") || lower.Contains("bench2")) { type = GymExerciseType.InclineBenchPress; return true; }
        if (lower.Contains("bench") && !lower.Contains("preacher")) { type = GymExerciseType.FlatBenchPress; return true; }
        type = GymExerciseType.FlatBenchPress;
        return false;
    }

    private static string GetDisplayName(GymExerciseType type)
    {
        switch (type)
        {
            case GymExerciseType.FlatBenchPress: return "Flat barbell bench press";
            case GymExerciseType.InclineBenchPress: return "Incline barbell bench press";
            case GymExerciseType.BarbellSquat: return "Barbell squat";
            case GymExerciseType.PreacherCurl: return "Preacher curls";
            case GymExerciseType.Dips: return "Dips";
            case GymExerciseType.Treadmill: return "Treadmill";
            case GymExerciseType.ExerciseBike: return "Exercise bike";
            case GymExerciseType.LatPulldown: return "Lat pulldown";
            default: return "Exercise";
        }
    }
}
