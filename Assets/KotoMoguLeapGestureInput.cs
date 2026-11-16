using UnityEngine;

// Reads the existing receiver; never opens another UDP socket.
[DisallowMultipleComponent]
[DefaultExecutionOrder(50)]
public sealed class KotoMoguLeapGestureInput : MonoBehaviour
{
    public KotoMoguLeapReceiver receiver;
    public KotoMoguController petController;
    public KotoMoguLeapHandVisualizer handVisualizer;
    public KotoMoguLeapHeadPetting headPetting;
    public bool executeActions = false;
    public bool suppressNearHead = true;
    [Tooltip("Point/gun remain usable near the head; temporarily reserve that hand for the fun gesture.")]
    public bool allowFunGesturesNearHead = true;
    [Min(0.1f)] public float funStableSeconds = 0.45f;
    public bool BlocksPetting { get { return executeActions && isActiveAndEnabled &&
        receiver != null && receiver.ReceivingPackets && receiver.SecondsSinceLastPacket <= maximumPacketAge &&
        (IsFun(leftGesture) || IsFun(rightGesture)); } }
    private static bool IsFun(string label) { return label == "point" || label == "finger_gun" || label == "victory" || label == "rock" || label == "shaka"; }
    public static string FunAction(string label)
    {
        switch (label)
        {
            case "point": return "Bite";
            case "finger_gun": return "PlayDead";
            case "victory": return "Victory";
            case "rock": return "Roar";
            case "shaka": return "Shy";
            default: return "";
        }
    }
    [Min(0.1f)] public float stableSeconds = 0.3f;
    [Min(0.1f)] public float releaseSeconds = 0.25f;
    [Min(0.1f)] public float cooldownSeconds = 1f;
    [Min(0.05f)] public float maximumPacketAge = 0.3f;
    [Range(0f, 1f)] public float openGrabMaximum = 0.3f;
    [Range(0f, 1f)] public float fistGrabMinimum = 0.8f;
    [Range(0f, 1f)] public float thumbUpMinimumY = 0.5f;
    [SerializeField] private string status = "Waiting for protocol v2 bridge";
    [SerializeField] private string leftGesture, rightGesture;
    [SerializeField] private int acceptedCount;
    public string Status { get { return status; } }

    // Public for the Editor verification menu; no Unity lifecycle dependency.
    public sealed class PoseGate
    {
        private int handId;
        private bool hasId;
        private string candidate = "", latched = "";
        private float since, neutralSince = -1f;
        public void Reset() { hasId = false; candidate = latched = ""; neutralSince = -1f; }
        public bool Observe(int id, string label, float now, float stable, float release)
        {
            if (!hasId || handId != id) { Reset(); hasId = true; handId = id; }
            if (string.IsNullOrEmpty(label))
            {
                candidate = "";
                if (neutralSince < 0f) neutralSince = now;
                if (now - neutralSince >= release) latched = "";
                return false;
            }
            neutralSince = -1f;
            if (candidate != label) { candidate = label; since = now; }
            if (latched == label || now - since < stable) return false;
            latched = label; // Consume even if Brain subsequently refuses: no delayed retries.
            return true;
        }
    }
    private readonly PoseGate left = new PoseGate(), right = new PoseGate();
    private uint lastSerial;
    private float nextAllowedAt;

    void Awake()
    {
        if (receiver == null) receiver = GetComponent<KotoMoguLeapReceiver>();
        if (handVisualizer == null) handVisualizer = GetComponent<KotoMoguLeapHandVisualizer>();
        if (headPetting == null) headPetting = GetComponent<KotoMoguLeapHeadPetting>();
    }
    void OnEnable() { left.Reset(); right.Reset(); lastSerial = 0; nextAllowedAt = 0; }
    void OnDisable() { left.Reset(); right.Reset(); }

    public static string Classify(KotoMoguLeapReceiver.LeapHand h, float openMax, float fistMin, float thumbY)
    {
        if (h == null || float.IsNaN(h.grab) || float.IsInfinity(h.grab) || h.grab < 0 || h.grab > 1) return "";
        if (h.thumb == 0 && h.index == 1 && h.middle == 1 && h.ring == 0 && h.pinky == 0) return "victory";
        if (h.thumb == 0 && h.index == 1 && h.middle == 0 && h.ring == 0 && h.pinky == 1) return "rock";
        if (h.thumb == 1 && h.index == 0 && h.middle == 0 && h.ring == 0 && h.pinky == 1) return "shaka";
        bool threeClosed = h.middle == 0 && h.ring == 0 && h.pinky == 0;
        // An L shape (index + thumb) wins over index-only pointing. Orientation is deliberately unrestricted.
        if (h.index == 1 && threeClosed)
        {
            if (h.thumb == 1) return "finger_gun";
            if (h.thumb == 0) return "point";
        }
        bool fourClosed = h.index == 0 && h.middle == 0 && h.ring == 0 && h.pinky == 0;
        Vector3 thumbDirection = new Vector3(h.tx, h.ty, h.tz);
        if (float.IsNaN(thumbDirection.sqrMagnitude) || float.IsInfinity(thumbDirection.sqrMagnitude)) return "";
        if (h.thumb == 1 && fourClosed && thumbDirection.sqrMagnitude > 0.0001f &&
            thumbDirection.normalized.y >= thumbY) return "thumb_up";
        // Thumb must also be curled: a thumbs-up cannot fall through to fist.
        if (h.thumb == 0 && fourClosed && h.grab >= fistMin) return "fist";
        if (h.thumb == 1 && h.index == 1 && h.middle == 1 && h.ring == 1 && h.pinky == 1 &&
            h.grab <= openMax) return "open_palm";
        return "";
    }

    void Update()
    {
        if (receiver == null || !receiver.ReceivingPackets || receiver.SecondsSinceLastPacket > maximumPacketAge)
        {
            left.Reset(); right.Reset(); leftGesture = rightGesture = "";
            status = "No fresh Leap data"; return;
        }
        if (!receiver.HasGestureData)
        {
            left.Reset(); right.Reset(); status = "Old bridge: rebuild and run protocol v2 EXE"; return;
        }
        if (receiver.PacketSerial == lastSerial) return;
        lastSerial = receiver.PacketSerial;
        float now = Time.unscaledTime;
        bool r = ObserveHand(false, right, receiver.HasRightHand ? receiver.RightPose : null, now, out rightGesture);
        bool l = ObserveHand(true, left, receiver.HasLeftHand ? receiver.LeftPose : null, now, out leftGesture);
        if (!r && !l) return;
        // Both edges are consumed; right wins if they become stable in the same frame.
        string label = r ? rightGesture : leftGesture;
        if (now < nextAllowedAt) { status = label + ": cooldown (release and retry)"; return; }
        nextAllowedAt = now + Mathf.Max(0.1f, cooldownSeconds);
        if (!executeActions) { status = "Recognized: " + label + " (test only)"; Debug.Log("[Leap Gesture] " + status, this); return; }
        if (petController == null || petController.Brain == null) { status = "Assign Pet Controller"; return; }
        var brain = petController.Brain;
        // Preserve manual actions and Sleep; never queue gestures for later playback.
        if (brain.Action == KotoAction.Sleep || (brain.IsBusy && !brain.IsAutonomousAction))
        { status = label + ": pet busy/sleeping (release and retry)"; return; }
        if (IsFun(label))
        {
            var motion = petController.GetComponent<KotoMoguMotion>();
            string actionName = FunAction(label);
            if (motion == null || motion.modelAnimator == null ||
                !motion.modelAnimator.HasState(0, Animator.StringToHash("Base Layer." + actionName)))
            { status = "Install Fun Animations first: missing " + actionName; Debug.LogWarning(status, this); return; }
        }
        bool accepted = brain.ReceiveGesture(label);
        if (accepted) acceptedCount++;
        status = label + (accepted ? " -> accepted: " : " -> refused: ") + brain.Message;
        Debug.Log("[Leap Gesture] " + status, this);
    }

    private bool ObserveHand(bool isLeft, PoseGate gate, KotoMoguLeapReceiver.LeapHand hand, float now, out string label)
    {
        label = "";
        if (hand == null) { gate.Reset(); return false; }
        label = Classify(hand, openGrabMaximum, fistGrabMinimum, thumbUpMinimumY);
        if (suppressNearHead && !(allowFunGesturesNearHead && IsFun(label)))
        {
            Vector3 world;
            if (handVisualizer == null || headPetting == null)
            { status = "Assign Hand Visualizer and Head Petting, or disable Suppress Near Head for testing"; gate.Reset(); return false; }
            if (handVisualizer.TryGetPalmWorld(isLeft, out world) && headPetting.IsPalmNearHead(world))
            { gate.Reset(); label = ""; status = "Head zone: gestures paused for this hand"; return false; }
        }
        return gate.Observe(hand.id, label, now, Mathf.Max(0.1f, IsFun(label) ? funStableSeconds : stableSeconds), Mathf.Max(0.1f, releaseSeconds));
    }
}
