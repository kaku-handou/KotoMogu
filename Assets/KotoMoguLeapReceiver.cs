using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

/// <summary>
/// Stage 2 diagnostic receiver for LeapBridge_Orion41.
/// Receives JSON from 127.0.0.1:8767 and displays live palm coordinates
/// in the Unity Inspector. Does NOT control the pet or change existing inputs.
/// </summary>
public sealed class KotoMoguLeapReceiver : MonoBehaviour
{
    [Header("UDP settings")]
    [SerializeField] private int port = 8767;
    [SerializeField] private bool logOncePerSecond = true;

    [Header("Live data (Play Mode)")]
    [SerializeField] private bool receivingPackets;
    [SerializeField] private long frameId;
    [SerializeField] private int handCount;
    [SerializeField] private bool hasLeftHand;
    [SerializeField] private Vector3 leftPalmMm;
    [SerializeField] private bool hasRightHand;
    [SerializeField] private Vector3 rightPalmMm;
    [SerializeField] private float secondsSinceLastPacket;

    // Read-only access for other Unity components. Inspector values are diagnostics,
    // not user-operated switches.
    public bool ReceivingPackets { get { return receivingPackets; } }
    public int HandCount { get { return handCount; } }
    public bool HasLeftHand { get { return receivingPackets && hasLeftHand; } }
    public bool HasRightHand { get { return receivingPackets && hasRightHand; } }
    public Vector3 LeftPalmMm { get { return leftPalmMm; } }
    public Vector3 RightPalmMm { get { return rightPalmMm; } }
    public float SecondsSinceLastPacket { get { return secondsSinceLastPacket; } }

    private UdpClient client;
    private IPEndPoint remoteEndPoint;
    private float lastPacketTime = -1000f;
    private float nextLogTime;

    [Serializable]
    private class LeapPacket
    {
        public string source;
        public long frame;
        public LeapHand[] hands;
    }

    [Serializable]
    private class LeapHand
    {
        public int id;
        public string hand;
        public float x;
        public float y;
        public float z;
    }

    private void OnEnable()
    {
        // Avoid stale Play Mode / Inspector hand flags.
        receivingPackets = false;
        frameId = 0;
        handCount = 0;
        hasLeftHand = false;
        hasRightHand = false;
        leftPalmMm = Vector3.zero;
        rightPalmMm = Vector3.zero;
        secondsSinceLastPacket = 0f;
        lastPacketTime = -1000f;
        nextLogTime = 0f;
        remoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
        try
        {
            // Bind only to this PC, not to a public network interface.
            client = new UdpClient(new IPEndPoint(IPAddress.Loopback, port));
            Debug.Log("[Leap UDP] Listening on 127.0.0.1:" + port, this);
        }
        catch (SocketException e)
        {
            Debug.LogError("[Leap UDP] Cannot listen on port " + port + ". " +
                "Close udp_monitor.py first. Detail: " + e.Message, this);
            enabled = false;
        }
    }

    private void Update()
    {
        if (client == null) return;

        // Avoid blocking Unity's main thread. Take the newest packet only.
        string newestJson = null;
        try
        {
            int drained = 0;
            while (client.Available > 0 && drained++ < 128)
            {
                byte[] bytes = client.Receive(ref remoteEndPoint);
                newestJson = Encoding.UTF8.GetString(bytes);
            }
        }
        catch (SocketException e)
        {
            Debug.LogWarning("[Leap UDP] Receive error: " + e.Message, this);
        }
        catch (ObjectDisposedException)
        {
            return;
        }

        if (newestJson != null)
        {
            try
            {
                LeapPacket packet = JsonUtility.FromJson<LeapPacket>(newestJson);
                if (packet != null && packet.source == "leap-gen1")
                {
                    frameId = packet.frame;
                    handCount = packet.hands != null ? packet.hands.Length : 0;
                    hasLeftHand = false;
                    hasRightHand = false;

                    if (packet.hands != null)
                    {
                        foreach (LeapHand hand in packet.hands)
                        {
                            if (hand == null) continue;
                            Vector3 palm = new Vector3(hand.x, hand.y, hand.z);
                            if (hand.hand == "left")
                            {
                                hasLeftHand = true;
                                leftPalmMm = palm;
                            }
                            else if (hand.hand == "right")
                            {
                                hasRightHand = true;
                                rightPalmMm = palm;
                            }
                        }
                    }
                    lastPacketTime = Time.realtimeSinceStartup;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Leap UDP] Invalid JSON: " + e.Message, this);
            }
        }

        secondsSinceLastPacket = Time.realtimeSinceStartup - lastPacketTime;
        receivingPackets = secondsSinceLastPacket < 2f;
        if (!receivingPackets)
        {
            handCount = 0;
            hasLeftHand = false;
            hasRightHand = false;
        }

        if (logOncePerSecond && receivingPackets && Time.unscaledTime >= nextLogTime)
        {
            nextLogTime = Time.unscaledTime + 1f;
            Debug.Log("[Leap UDP] frame=" + frameId + " hands=" + handCount +
                (hasLeftHand ? " left(mm)=" + leftPalmMm : "") +
                (hasRightHand ? " right(mm)=" + rightPalmMm : ""), this);
        }
    }

    private void OnDisable()
    {
        if (client != null)
        {
            client.Close();
            client = null;
        }
        receivingPackets = false;
        handCount = 0;
        hasLeftHand = false;
        hasRightHand = false;
    }
}
