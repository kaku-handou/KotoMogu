using System;
using UnityEngine;

/// <summary>
/// Stage 4.2: Speed-independent near-head stroking detection for Leap Motion Gen1.
/// Uses the same Unity-world mapping as KotoMoguLeapHandVisualizer.
/// A detected stroke is an estimate based on palm movement, NOT physical contact.
/// Other KotoMogu scripts, the UDP receiver, and the existing Animator are untouched.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(KotoMoguLeapReceiver))]
public sealed class KotoMoguLeapHeadPetting : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private KotoMoguLeapHandVisualizer handVisualizer;
    [SerializeField] private KotoMoguController petController;
    [SerializeField] private Transform petRoot;

    [Header("Head zone (approximate, Unity world units)")]
    [Tooltip("Optional true head-center transform. When empty, uses Pet Root + Head Offset.")]
    [SerializeField] private Transform headTarget;
    [Tooltip("Offset in KotoMogu local coordinates; keep your calibrated Inspector value.")]
    [SerializeField] private Vector3 headOffsetFromPet = new Vector3(0f, 1.8f, 0f);
    [SerializeField, Min(0.05f)] private float headRadius = 0.8f;

    // Field names from Stage 4 are intentionally retained so Unity preserves
    // existing Inspector values and serialized component references.
    [Header("Stroke detection (fast AND slow)")]
    [Tooltip("Observation time for a short/slow stroke. A clear fast stroke can trigger earlier.")]
    [SerializeField, Min(0f)] private float minimumStaySeconds = 0.20f;
    [Tooltip("Minimum displacement across the head surface for a stroke (world units).")]
    [SerializeField, Min(0.01f)] private float minimumStrokeDistance = 0.06f;
    [Tooltip("Ignore small tracking jitter. Small steps accumulate until this distance is reached.")]
    [SerializeField, Min(0f)] private float noiseStep = 0.003f;
    [Tooltip("Legacy speed parameter: only rejects a very LARGE, implausible tracking jump. Does not reject normal fast strokes.")]
    [SerializeField, Min(0.1f)] private float maximumStepSpeed = 2.0f;
    [Tooltip("Delay between PET events, including when the hand stays in the zone.")]
    [SerializeField, Min(0f)] private float globalCooldownSeconds = 0.8f;

    [Header("Stability (advanced)")]
    [Tooltip("Slightly larger exit boundary prevents IN/OUT flicker near the head surface.")]
    [SerializeField, Min(0f)] private float zoneExitMargin = 0.06f;
    [Tooltip("After the palm stops moving this long, start a new stroke from its current position.")]
    [SerializeField, Min(0.1f)] private float idleResetSeconds = 1.0f;
    [Tooltip("Required straightness of the motion (0-1). Jitter and random wandering should not be interpreted as stroking.")]
    [SerializeField, Range(0f, 1f)] private float minimumDirectionConsistency = 0.60f;

    [Header("Pet reaction")]
    [Tooltip("If enabled, request Happy through KotoMoguController. Leave OFF during first testing.")]
    [SerializeField] private bool requestHappyOnPet = false;
    [SerializeField] private bool showTestHud = true;

    [Header("Live debug (Play Mode; do not edit)")]
    [SerializeField] private bool leftInZone;
    [SerializeField] private bool rightInZone;
    [SerializeField] private float leftDistanceToHead;
    [SerializeField] private float rightDistanceToHead;
    // Same labels as the previous version, now tracking accepted planar travel.
    [SerializeField] private float leftStrokeDistance;
    [SerializeField] private float rightStrokeDistance;
    [SerializeField] private float leftNetDisplacement;
    [SerializeField] private float rightNetDisplacement;
    [SerializeField] private int petEventCount;
    [SerializeField] private string lastEvent = "Waiting for hands";

    private sealed class HandSession
    {
        public bool inside;
        public Vector3 acceptedPosition; // Planar relative-to-head position, held across tiny movements.
        public Vector3 strokeStart;
        public Vector3 previousDirection;
        public float enteredAt;
        public float lastMovementAt;
        public float acceptedTravel;
        public float netDisplacement;
        public int acceptedSteps;

        public void Clear()
        {
            inside = false;
            acceptedPosition = Vector3.zero;
            strokeStart = Vector3.zero;
            previousDirection = Vector3.zero;
            enteredAt = 0f;
            lastMovementAt = 0f;
            acceptedTravel = 0f;
            netDisplacement = 0f;
            acceptedSteps = 0;
        }

        public void RestartStroke(Vector3 position, float now)
        {
            acceptedPosition = position;
            strokeStart = position;
            previousDirection = Vector3.zero;
            acceptedTravel = 0f;
            netDisplacement = 0f;
            acceptedSteps = 0;
            lastMovementAt = now;
        }
    }

    private readonly HandSession left = new HandSession();
    private readonly HandSession right = new HandSession();
    private float nextGlobalEventTime;
    private bool warnedMissingReference;
    private bool warnedUnknownHappy;

    private void Reset()
    {
        handVisualizer = GetComponent<KotoMoguLeapHandVisualizer>();
    }

    private void Awake()
    {
        if (handVisualizer == null) handVisualizer = GetComponent<KotoMoguLeapHandVisualizer>();
        if (petController == null && petRoot != null)
            petController = petRoot.GetComponent<KotoMoguController>();
        if (petRoot == null && petController != null) petRoot = petController.transform;
    }

    private void OnEnable()
    {
        left.Clear();
        right.Clear();
        leftInZone = false;
        rightInZone = false;
        leftDistanceToHead = -1f;
        rightDistanceToHead = -1f;
        leftStrokeDistance = 0f;
        rightStrokeDistance = 0f;
        leftNetDisplacement = 0f;
        rightNetDisplacement = 0f;
        petEventCount = 0;
        lastEvent = "Waiting for hands";
        nextGlobalEventTime = 0f;
        warnedMissingReference = false;
        warnedUnknownHappy = false;
    }

    private bool TryGetHeadCenter(out Vector3 center)
    {
        if (headTarget != null) { center = headTarget.position; return true; }
        if (petRoot != null) { center = petRoot.TransformPoint(headOffsetFromPet); return true; }
        center = Vector3.zero;
        return false;
    }

    private void Update()
    {
        Vector3 center;
        if (handVisualizer == null || !TryGetHeadCenter(out center))
        {
            left.Clear();
            right.Clear();
            leftInZone = rightInZone = false;
            if (!warnedMissingReference)
            {
                Debug.LogWarning("[Leap Pet] Assign Hand Visualizer and Pet Root in Inspector.", this);
                warnedMissingReference = true;
            }
            lastEvent = "Missing references";
            return;
        }

        Vector3 leftWorld, rightWorld;
        bool hasLeft = handVisualizer.TryGetPalmWorld(true, out leftWorld);
        bool hasRight = handVisualizer.TryGetPalmWorld(false, out rightWorld);

        // A fixed head-facing plane makes side-to-side / up-down strokes count
        // even if the palm moves across the center of the head sphere.
        Vector3 planeNormal = petRoot != null ? petRoot.forward : Vector3.forward;
        float now = Time.unscaledTime;
        float leftDistance, rightDistance;
        leftInZone = TrackHand(left, hasLeft, leftWorld, center, planeNormal, now, "LEFT", out leftDistance);
        rightInZone = TrackHand(right, hasRight, rightWorld, center, planeNormal, now, "RIGHT", out rightDistance);
        leftDistanceToHead = leftDistance;
        rightDistanceToHead = rightDistance;
        leftStrokeDistance = left.acceptedTravel;
        rightStrokeDistance = right.acceptedTravel;
        leftNetDisplacement = left.netDisplacement;
        rightNetDisplacement = right.netDisplacement;
    }

    private bool TrackHand(HandSession session, bool visible, Vector3 position, Vector3 center,
                           Vector3 planeNormal, float now, string side, out float distance)
    {
        distance = visible ? Vector3.Distance(position, center) : -1f;
        float allowedRadius = headRadius + (session.inside ? zoneExitMargin : 0f);
        if (!visible || distance > allowedRadius)
        {
            session.Clear();
            return false;
        }

        Vector3 planarPosition = Vector3.ProjectOnPlane(position - center, planeNormal);
        if (!session.inside)
        {
            session.inside = true;
            session.enteredAt = now;
            session.RestartStroke(planarPosition, now);
            lastEvent = side + " near head (stroke sideways or vertically)";
            return true;
        }

        // Do not accumulate movement in the tiny hysteresis-only region.
        if (distance > headRadius)
        {
            session.RestartStroke(planarPosition, now);
            return true;
        }

        if (now - session.lastMovementAt > idleResetSeconds)
            session.RestartStroke(planarPosition, now);

        // IMPORTANT: compare against the last ACCEPTED position, not the last
        // frame. Slow, sub-noise steps therefore accumulate instead of vanishing.
        Vector3 step = planarPosition - session.acceptedPosition;
        float stepLength = step.magnitude;
        if (stepLength < Mathf.Max(0.0005f, noiseStep))
            return true;

        // The old code rejected every step with speed > maximumStepSpeed.
        // Now speed only matters in combination with a very large jump: normal
        // fast strokes (including >2 units/s) remain valid.
        float dt = Mathf.Max(Time.unscaledDeltaTime, 0.001f);
        float speed = stepLength / dt;
        float teleportThreshold = Mathf.Max(0.65f, headRadius * 0.90f);
        if (stepLength > teleportThreshold && speed > maximumStepSpeed)
        {
            session.RestartStroke(planarPosition, now);
            lastEvent = side + " tracking jump ignored";
            return true;
        }

        Vector3 direction = step / stepLength;
        if (session.acceptedSteps > 0 && Vector3.Dot(direction, session.previousDirection) < -0.35f)
        {
            // A real back-and-forth stroke changes direction. Begin its next
            // pass at the turning point; don't cancel the whole hand session.
            session.RestartStroke(session.acceptedPosition, now);
        }

        session.acceptedTravel += stepLength;
        session.acceptedSteps++;
        session.acceptedPosition = planarPosition;
        session.previousDirection = direction;
        session.lastMovementAt = now;
        session.netDisplacement = Vector3.Distance(session.strokeStart, planarPosition);

        float threshold = Mathf.Max(0.01f, minimumStrokeDistance);
        float straightness = session.netDisplacement / Mathf.Max(session.acceptedTravel, 0.0001f);
        bool enoughDisplacement = session.netDisplacement >= threshold;
        bool coherentMotion = straightness >= minimumDirectionConsistency;
        bool slowStroke = (now - session.enteredAt) >= minimumStaySeconds;
        bool fastClearStroke = session.netDisplacement >= threshold * 1.35f;

        // Require two accepted movement samples: one tracking spike cannot
        // generate a PET. A clear quick swipe bypasses the slow dwell timer.
        if (session.acceptedSteps >= 2 && enoughDisplacement && coherentMotion &&
            (slowStroke || fastClearStroke) && now >= nextGlobalEventTime)
        {
            petEventCount++;
            lastEvent = "PET DETECTED: " + side;
            Debug.Log("[Leap Pet] " + lastEvent + " (event #" + petEventCount + ")", this);
            nextGlobalEventTime = now + globalCooldownSeconds;

            // Do NOT require leaving the zone; count a new, genuine stroke
            // after the cooldown while the same hand remains over the head.
            session.RestartStroke(planarPosition, now);
            RequestHappyIfEnabled();
        }
        return true;
    }

    private void RequestHappyIfEnabled()
    {
        if (!requestHappyOnPet) return;
        if (petController == null)
        {
            lastEvent += " (no Pet Controller)";
            return;
        }
        KotoAction existingAction;
        if (Enum.TryParse<KotoAction>("Happy", true, out existingAction) &&
            Enum.IsDefined(typeof(KotoAction), existingAction))
        {
            petController.SendCommand("Happy");
            lastEvent += " -> Happy requested";
        }
        else
        {
            lastEvent += " (Happy not available)";
            if (!warnedUnknownHappy)
            {
                Debug.LogWarning("[Leap Pet] KotoAction has no Happy member. Detection works; animation skipped.", this);
                warnedUnknownHappy = true;
            }
        }
    }

    private void OnGUI()
    {
        if (!showTestHud || !Application.isPlaying) return;
        GUI.Box(new Rect(12, 12, 470, 115), "LEAP PET TEST (FAST + SLOW)");
        GUI.Label(new Rect(24, 33, 445, 22),
            "Head: L=" + (leftInZone ? "IN" : "out") + " R=" + (rightInZone ? "IN" : "out") +
            "  Pets=" + petEventCount);
        GUI.Label(new Rect(24, 53, 445, 22),
            "Travel: L=" + leftStrokeDistance.ToString("F3") + " R=" + rightStrokeDistance.ToString("F3") +
            " | Net: L=" + leftNetDisplacement.ToString("F3") + " R=" + rightNetDisplacement.ToString("F3"));
        GUI.Label(new Rect(24, 74, 445, 22), lastEvent);
        GUI.Label(new Rect(24, 95, 445, 22),
            requestHappyOnPet ? "Happy request: ON" : "Happy request: OFF (safe test)");
    }

    private void OnDrawGizmosSelected()
    {
        Vector3 center;
        if (!TryGetHeadCenter(out center)) return;
        Gizmos.color = new Color(1f, 0.82f, 0.15f, 1f);
        Gizmos.DrawWireSphere(center, Mathf.Max(0.05f, headRadius));
        Gizmos.DrawSphere(center, 0.025f);
    }
}
