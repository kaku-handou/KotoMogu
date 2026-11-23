using System.Net;
using System.Net.Sockets;
using System.Text;
using UnityEngine;

namespace KotoMogu.InputPreview
{
    public sealed class AliceDragonReactions : MonoBehaviour
    {
        [SerializeField] private Animator dragon;
        [SerializeField] private LeapRecognitionPreview handInput;
        [SerializeField] private bool enableFaceInput = true;

        [Header("Animator")]
        [SerializeField] private string idleState = "Base Layer.Idle";

        [Header("Hand gesture triggers (blank = display only)")]
        [SerializeField] private string openPalmTrigger = "Greet";
        [SerializeField] private string fistTrigger = "Jump";
        [SerializeField] private string thumbUpTrigger;
        [SerializeField] private string pointTrigger;
        [SerializeField] private string fingerGunTrigger;
        [SerializeField] private string victoryTrigger;
        [SerializeField] private string rockTrigger;
        [SerializeField] private string shakaTrigger;

        [Header("Face expression triggers (blank = display only)")]
        [SerializeField] private string smileTrigger = "Greet";
        [SerializeField] private string mouthOpenTrigger = "Jump";

        private const int FacePort = 8768;
        private const float InputLifetime = 0.7f;
        private readonly System.Collections.Generic.HashSet<string> warnedTriggers = new();

        private UdpClient faceSocket;
        private IPEndPoint sender = new IPEndPoint(IPAddress.Any, 0);
        private string pendingHand;
        private string pendingFace;
        private float handExpiry;
        private float faceExpiry;

        private void OnEnable()
        {
            if (handInput != null)
                handInput.onGestureRecognized.AddListener(OnHandGesture);
            if (!enableFaceInput)
                return;

            try
            {
                faceSocket = new UdpClient(new IPEndPoint(IPAddress.Loopback, FacePort));
            }
            catch (SocketException error)
            {
                Debug.LogWarning($"[Alice] Face UDP port {FacePort} unavailable: {error.Message}. Leap remains available.", this);
            }
        }

        private void OnDisable()
        {
            if (handInput != null)
                handInput.onGestureRecognized.RemoveListener(OnHandGesture);
            faceSocket?.Close();
            faceSocket = null;
            pendingHand = pendingFace = null;
        }

        private string FindTrigger(string label)
        {
            switch (label)
            {
                case "open_palm": return openPalmTrigger;
                case "fist": return fistTrigger;
                case "thumb_up": return thumbUpTrigger;
                case "point": return pointTrigger;
                case "finger_gun": return fingerGunTrigger;
                case "victory": return victoryTrigger;
                case "rock": return rockTrigger;
                case "shaka": return shakaTrigger;
                case "smile": return smileTrigger;
                case "mouth_open": return mouthOpenTrigger;
                default: return null;
            }
        }

        private void OnHandGesture(string label)
        {
            if (string.IsNullOrWhiteSpace(FindTrigger(label)))
                return;
            pendingHand = label;
            handExpiry = Time.unscaledTime + InputLifetime;
        }

        private void ReceiveFaceInput()
        {
            if (faceSocket == null)
                return;
            try
            {
                for (int i = 0; i < 16 && faceSocket.Available > 0; i++)
                {
                    byte[] data = faceSocket.Receive(ref sender);
                    if (data.Length != 5 && data.Length != 10)
                        continue;
                    string label = Encoding.ASCII.GetString(data);
                    if (label != "smile" && label != "mouth_open")
                        continue;
                    if (handInput != null)
                        handInput.ShowFaceNotice(label);
                    Debug.Log($"[Alice Camera] {label}", this);
                    if (string.IsNullOrWhiteSpace(FindTrigger(label)))
                        continue;
                    pendingFace = label;
                    faceExpiry = Time.unscaledTime + InputLifetime;
                }
            }
            catch (SocketException error)
            {
                Debug.LogWarning($"[Alice] Face input disconnected: {error.Message}", this);
                faceSocket.Close();
                faceSocket = null;
            }
        }

        private void LateUpdate()
        {
            ReceiveFaceInput();
            float now = Time.unscaledTime;
            if (now > handExpiry)
                pendingHand = null;
            if (now > faceExpiry)
                pendingFace = null;

            if (pendingHand == null && pendingFace == null)
                return;
            if (dragon == null || !dragon.isActiveAndEnabled || dragon.IsInTransition(0)
                || dragon.GetCurrentAnimatorStateInfo(0).fullPathHash != Animator.StringToHash(idleState ?? ""))
                return;

            bool fromHand = pendingHand != null;
            string label = fromHand ? pendingHand : pendingFace;
            string trigger = FindTrigger(label)?.Trim();
            if (fromHand)
                pendingHand = null;
            else
                pendingFace = null;
            if (string.IsNullOrEmpty(trigger))
                return;

            foreach (AnimatorControllerParameter parameter in dragon.parameters)
            {
                if (parameter.type != AnimatorControllerParameterType.Trigger || parameter.name != trigger)
                    continue;
                dragon.SetTrigger(parameter.nameHash);
                pendingHand = pendingFace = null;
                string source = fromHand ? "Hand" : "Camera";
                Debug.Log($"[Alice {source}] {label} -> {trigger}", this);
                return;
            }
            if (warnedTriggers.Add(trigger))
                Debug.LogWarning($"[Alice] Animator Trigger '{trigger}' was not found.", this);
        }
    }
}
