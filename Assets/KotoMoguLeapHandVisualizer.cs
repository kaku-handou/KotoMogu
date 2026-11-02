using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Visual-only calibration of Leap Motion Gen1 palm coordinates.
/// No pet commands, no collisions, no additional UDP socket.
/// Attach to the same LeapMotionReceiver object as KotoMoguLeapReceiver.
/// </summary>
[DisallowMultipleComponent]
public sealed class KotoMoguLeapHandVisualizer : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KotoMoguLeapReceiver receiver;
    [SerializeField] private Transform petRoot;

    [Header("Calibration (visual test only)")]
    [Tooltip("Sensor coordinates in millimeters that should align with the visual origin.")]
    [SerializeField] private Vector3 sensorReferenceMm = new Vector3(0f, 180f, 0f);
    [Tooltip("Marker origin in world space relative to KotoMogu's position.")]
    [SerializeField] private Vector3 worldOffsetFromPet = new Vector3(0f, 0.6f, 0f);
    [Tooltip("Unity world units represented by one sensor millimeter.")]
    [SerializeField, Min(0.0001f)] private float unitsPerMillimeter = 0.004f;
    [SerializeField] private bool invertX;
    [SerializeField] private bool invertY;
    [SerializeField] private bool invertZ;

    [Header("Marker display")]
    [SerializeField, Min(0.01f)] private float markerDiameter = 0.12f;
    [SerializeField] private Color leftColor = new Color(0.0f, 0.85f, 1f);
    [SerializeField] private Color rightColor = new Color(1f, 0.32f, 0.75f);

    private GameObject leftMarker;
    private GameObject rightMarker;
    private Material leftMaterial;
    private Material rightMaterial;

    private void Reset()
    {
        receiver = GetComponent<KotoMoguLeapReceiver>();
    }

    private void Awake()
    {
        if (receiver == null) receiver = GetComponent<KotoMoguLeapReceiver>();
    }

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        CreateMarkers();
    }

    private void Update()
    {
        if (receiver == null || petRoot == null)
        {
            SetMarker(leftMarker, false, Vector3.zero);
            SetMarker(rightMarker, false, Vector3.zero);
            return;
        }

        bool active = receiver.ReceivingPackets;
        SetMarker(leftMarker, active && receiver.HasLeftHand, receiver.LeftPalmMm);
        SetMarker(rightMarker, active && receiver.HasRightHand, receiver.RightPalmMm);
    }

    private Vector3 MapToWorld(Vector3 palmMm)
    {
        Vector3 delta = (palmMm - sensorReferenceMm) * unitsPerMillimeter;
        if (invertX) delta.x = -delta.x;
        if (invertY) delta.y = -delta.y;
        if (invertZ) delta.z = -delta.z;
        // Deliberately use WORLD axes, not petRoot rotation (pet may face 180 degrees).
        // This is a preliminary visualization, not physical-contact calibration.
        return petRoot.position + worldOffsetFromPet + delta;
    }

    /// <summary>
    /// Get the same calibrated WORLD position used by the visible spheres.
    /// The return value is false if that hand is missing or UDP has stopped.
    /// Other interaction components can reuse the calibration without
    /// opening another UDP socket or duplicating coordinate settings.
    /// </summary>
    public bool TryGetPalmWorld(bool left, out Vector3 worldPosition)
    {
        worldPosition = Vector3.zero;
        if (receiver == null || petRoot == null || !receiver.ReceivingPackets) return false;
        if (left ? !receiver.HasLeftHand : !receiver.HasRightHand) return false;
        worldPosition = MapToWorld(left ? receiver.LeftPalmMm : receiver.RightPalmMm);
        return true;
    }

    private void SetMarker(GameObject marker, bool present, Vector3 palmMm)
    {
        if (marker == null) return;
        if (!present)
        {
            if (marker.activeSelf) marker.SetActive(false);
            return;
        }
        if (!marker.activeSelf) marker.SetActive(true);
        marker.transform.position = MapToWorld(palmMm);
        marker.transform.localScale = Vector3.one * Mathf.Max(0.01f, markerDiameter);
    }

    private void CreateMarkers()
    {
        leftMarker = CreateMarker("Leap LEFT palm (visual only)", leftColor, out leftMaterial);
        rightMarker = CreateMarker("Leap RIGHT palm (visual only)", rightColor, out rightMaterial);
    }

    private static GameObject CreateMarker(string markerName, Color color, out Material material)
    {
        GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        marker.name = markerName;
        marker.layer = 2; // Ignore Raycast
        Collider collider = marker.GetComponent<Collider>();
        if (collider != null) Object.Destroy(collider);
        Renderer renderer = marker.GetComponent<Renderer>();
        if (renderer != null)
        {
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null) shader = Shader.Find("Standard");
        if (shader == null) shader = Shader.Find("Unlit/Color");
        material = shader != null ? new Material(shader) : null;
        if (material != null && renderer != null)
        {
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            renderer.sharedMaterial = material;
        }
        marker.SetActive(false);
        return marker;
    }

    private void OnDisable()
    {
        if (leftMarker != null) Destroy(leftMarker);
        if (rightMarker != null) Destroy(rightMarker);
        if (leftMaterial != null) Destroy(leftMaterial);
        if (rightMaterial != null) Destroy(rightMaterial);
        leftMarker = null;
        rightMarker = null;
        leftMaterial = null;
        rightMaterial = null;
    }
}
