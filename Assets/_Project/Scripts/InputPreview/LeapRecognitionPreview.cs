using UnityEngine;
using UnityEngine.Events;

namespace KotoMogu.InputPreview
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PreviewLeapReceiver))]
    [DefaultExecutionOrder(50)]
    public sealed class LeapRecognitionPreview : MonoBehaviour
    {
        public PreviewLeapReceiver receiver;
        [Min(0.1f)] public float stableSeconds = 0.2f;
        [Min(0.1f)] public float releaseSeconds = 0.15f;
        [Min(0.05f)] public float maximumPacketAge = 0.3f;
        [Min(1f)] public float messageSeconds = 3f;
        public bool showOverlay = true;
        public bool keyboardPreview = true;
        public bool showGestureNames;
        public enum DisplayLanguage { Chinese, Japanese, English }
        [SerializeField] private DisplayLanguage defaultLanguage = DisplayLanguage.Chinese;
        public Font uiFont;
        public UnityEvent<string> onGestureRecognized = new UnityEvent<string>();
        private enum Tracking { Waiting, Disabled, NoData, OldBridge, NoHand, Ready }
        private Tracking tracking;
        private const string LanguageKey = "KotoMogu.RecognitionPanel.Language";
        private DisplayLanguage language;
        public string Status => TranslateStatus();
        public string LastLabel { get; private set; } = "";
        public int RecognitionCount { get; private set; }
        [SerializeField] string leftPose = "", rightPose = "";
        readonly PreviewPoseLogic.Gate left = new PreviewPoseLogic.Gate();
        readonly PreviewPoseLogic.Gate right = new PreviewPoseLogic.Gate();
        uint lastSerial;
        float visibleUntil;
        string source = "", lastFace = "";
        float faceVisibleUntil;
        GUIStyle titleStyle, bodyStyle, buttonStyle;

        void Awake()
        {
            if (receiver == null) receiver = GetComponent<PreviewLeapReceiver>();
            int saved = PlayerPrefs.GetInt(LanguageKey, (int)defaultLanguage);
            language = saved >= 0 && saved <= 2 ? (DisplayLanguage)saved : defaultLanguage;
        }

        void OnEnable() { left.Reset(); right.Reset(); lastSerial = 0; }

        void Start()
        {
            if (uiFont != null) return;
            string[] installed = Font.GetOSInstalledFontNames();
            var fallback = new System.Collections.Generic.List<string>();
            foreach (string desired in new[] { "Microsoft YaHei", "Microsoft JhengHei", "Noto Sans CJK SC", "Yu Gothic", "Meiryo", "Noto Sans CJK JP", "Arial" })
                if (System.Array.IndexOf(installed, desired) >= 0) fallback.Add(desired);
            if (fallback.Count > 0)
                uiFont = Font.CreateDynamicFontFromOSFont(fallback.ToArray(), 24);
        }

        string T(string chinese, string japanese, string english)
        {
            switch (language)
            {
                case DisplayLanguage.Japanese: return japanese;
                case DisplayLanguage.English: return english;
                default: return chinese;
            }
        }

        string TranslateStatus()
        {
            switch (tracking)
            {
                case Tracking.Disabled: return T("接收器未启用，请检查 Console。", "受信が無効です。Console を確認してください。", "Receiver disabled. Check the Console.");
                case Tracking.NoData: return T("没有实时数据，请检查 LeapBridge。", "データを受信していません。LeapBridge を確認してください。", "No live data. Check LeapBridge.");
                case Tracking.OldBridge: return T("需要支持协议 v2 的 LeapBridge。", "プロトコル v2 対応の LeapBridge が必要です。", "LeapBridge protocol v2 is required.");
                case Tracking.NoHand: return T("已连接。请把手放到传感器上方。", "接続済み。センサーの上に手をかざしてください。", "Connected. Hold your hand above the sensor.");
                case Tracking.Ready:
                    string time = Mathf.Max(0.1f, stableSeconds).ToString("F2");
                    return T($"已连接。请保持手势约 {time} 秒。", $"接続済み。手の形を約 {time} 秒保ってください。", $"Connected. Hold a gesture for about {time} s.");
                default: return T("等待 LeapBridge。", "LeapBridge の接続を待っています。", "Waiting for LeapBridge.");
            }
        }

        string GestureLabel(string label)
        {
            if (!showGestureNames)
            {
                switch (label)
                {
                    case "open_palm": return T("打招呼", "あいさつ", "Greet");
                    case "fist": return T("吃东西", "食べる", "Eat");
                    case "thumb_up": return T("开心", "喜ぶ", "Happy");
                    case "point": return T("轻咬", "甘がみ", "Bite");
                    case "finger_gun": return T("装死", "死んだふり", "Play dead");
                    case "victory": return T("胜利欢呼", "勝利を喜ぶ", "Victory");
                    case "rock": return T("吼叫", "ほえる", "Roar");
                    case "shaka": return T("害羞", "照れる", "Shy");
                }
            }
            switch (label)
            {
                case "open_palm": return T("张开手掌", "パー", "Open palm");
                case "fist": return T("握拳", "グー", "Fist");
                case "thumb_up": return T("点赞", "サムズアップ", "Thumbs up");
                case "point": return T("伸食指", "人差し指", "Index finger");
                case "finger_gun": return T("L 形", "L 字", "L shape");
                case "victory": return T("V 形", "ピース", "V sign");
                case "rock": return T("食指和小指", "人差し指と小指", "Index and little finger");
                case "shaka": return T("拇指和小指", "親指と小指", "Thumb and little finger");
                default: return "–";
            }
        }

        string SourceLabel()
        {
            switch (source)
            {
                case "Leap 左手": return T("Leap 左手", "Leap 左手", "Leap left hand");
                case "Leap 右手": return T("Leap 右手", "Leap 右手", "Leap right hand");
                case "键盘模拟（非设备识别）": return T("键盘模拟", "キーボードテスト", "Keyboard test");
                case "external": return T("外部输入", "外部入力", "External input");
                default: return source;
            }
        }

        public void ShowFaceNotice(string label)
        {
            lastFace = label;
            faceVisibleUntil = Time.unscaledTime + messageSeconds;
        }

        void Update()
        {
            if (keyboardPreview) PollKeyboard();
            if (receiver == null || !receiver.isActiveAndEnabled)
            { ResetTracking(Tracking.Disabled); return; }
            if (!receiver.ReceivingPackets || receiver.SecondsSinceLastPacket > maximumPacketAge)
            { ResetTracking(Tracking.NoData); return; }
            if (!receiver.HasGestureData)
            { ResetTracking(Tracking.OldBridge); return; }
            if (!receiver.HasLeftHand && !receiver.HasRightHand)
            { ResetTracking(Tracking.NoHand); return; }
            tracking = Tracking.Ready;
            if (lastSerial == receiver.PacketSerial) return;
            lastSerial = receiver.PacketSerial;
            bool r = Observe(right, receiver.HasRightHand ? receiver.RightPose : null, out rightPose);
            bool l = Observe(left, receiver.HasLeftHand ? receiver.LeftPose : null, out leftPose);
            if (r || l) ReportGesture(r ? rightPose : leftPose, r ? "Leap 右手" : "Leap 左手");
        }

        bool Observe(PreviewPoseLogic.Gate gate, PreviewLeapReceiver.LeapHand hand, out string label)
        {
            label = "";
            if (hand == null) { gate.Reset(); return false; }
            label = PreviewPoseLogic.Classify(hand.thumb, hand.index, hand.middle, hand.ring, hand.pinky,
                hand.grab, hand.tx, hand.ty, hand.tz);
            return gate.Observe(hand.id, label, Time.unscaledTime, Mathf.Max(0.1f, stableSeconds), Mathf.Max(0.1f, releaseSeconds));
        }
        void ResetTracking(Tracking state)
        { left.Reset(); right.Reset(); leftPose = rightPose = ""; tracking = state; }

        // Notification only: no Animator, model, Brain, food or energy changes.
        public void ReportGesture(string label, string inputSource = "external")
        {
            if (System.Array.IndexOf(PreviewPoseLogic.Labels, label) < 0) return;
            LastLabel = label; source = inputSource; RecognitionCount++;
            visibleUntil = Time.unscaledTime + messageSeconds;
            Debug.Log("[Recognition Preview] " + source + " | " + GestureLabel(label) + " | " + label, this);
            onGestureRecognized.Invoke(label);
        }
        void PollKeyboard()
        {
#if ENABLE_INPUT_SYSTEM
            var k = UnityEngine.InputSystem.Keyboard.current;
            if (k == null) return;
            var keys = new[] { k.digit1Key, k.digit2Key, k.digit3Key, k.digit4Key, k.digit5Key, k.digit6Key, k.digit7Key, k.digit8Key };
            for (int i = 0; i < keys.Length; i++) if (keys[i].wasPressedThisFrame) ReportGesture(PreviewPoseLogic.Labels[i], "键盘模拟（非设备识别）");
#elif ENABLE_LEGACY_INPUT_MANAGER
            for (int i = 0; i < 8; i++) if (Input.GetKeyDown((KeyCode)((int)KeyCode.Alpha1 + i))) ReportGesture(PreviewPoseLogic.Labels[i], "键盘模拟（非设备识别）");
#endif
        }
        void OnGUI()
        {
            if (!showOverlay) return;
            if (titleStyle == null)
            {
                titleStyle = new GUIStyle(GUI.skin.label) { fontSize = 25, fontStyle = FontStyle.Bold, wordWrap = true };
                bodyStyle = new GUIStyle(GUI.skin.label) { fontSize = 17, wordWrap = true };
                buttonStyle = new GUIStyle(GUI.skin.button) { fontSize = 16 };
                if (uiFont != null) { titleStyle.font = uiFont; bodyStyle.font = uiFont; buttonStyle.font = uiFont; }
            }
            float scale = Mathf.Min(Screen.width / 960f, Screen.height / 540f);
            if (scale <= 0) return;
            Matrix4x4 oldMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1));
            GUILayout.BeginArea(new Rect(16, 16, 640, 232), GUI.skin.box);
            GUILayout.BeginHorizontal();
            GUILayout.Label(T("KotoMogu · 识别面板", "KotoMogu · 認識パネル", "KotoMogu · Input monitor"), titleStyle);
            GUILayout.FlexibleSpace();
            LanguageButton("中文", DisplayLanguage.Chinese);
            LanguageButton("日本語", DisplayLanguage.Japanese);
            LanguageButton("English", DisplayLanguage.English);
            GUILayout.EndHorizontal();
            GUILayout.Label(Status, bodyStyle);
            GUILayout.Label(Time.unscaledTime < visibleUntil
                ? T("手势：", "手の形：", "Gesture: ") + GestureLabel(LastLabel) + " · " + SourceLabel()
                : T("等待手势输入", "ジェスチャーを待っています", "Waiting for a gesture"), bodyStyle);
            GUILayout.Label(T("左手：", "左手：", "Left: ") + GestureLabel(leftPose) + "    "
                + T("右手：", "右手：", "Right: ") + GestureLabel(rightPose) + "    "
                + T("次数：", "回数：", "Count: ") + RecognitionCount, bodyStyle);
            if (Time.unscaledTime < faceVisibleUntil)
            {
                string faceLabel = lastFace == "smile" ? T("微笑", "笑顔", "Smile") : T("张嘴", "口を開ける", "Mouth open");
                GUILayout.Label(T("普通摄像头：", "Web カメラ：", "Webcam: ") + faceLabel, bodyStyle);
            }
            if (keyboardPreview)
                GUILayout.Label(T("数字 1–8：手势模拟", "数字キー 1–8：ジェスチャーのテスト", "Number keys 1–8: simulate gestures"), bodyStyle);
            GUILayout.EndArea();
            GUI.matrix = oldMatrix;
        }

        void LanguageButton(string caption, DisplayLanguage value)
        {
            Color previous = GUI.color;
            if (language == value) GUI.color = new Color(0.74f, 1f, 0.87f);
            bool clicked = GUILayout.Button(caption, buttonStyle, GUILayout.Width(77));
            GUI.color = previous;
            if (!clicked || language == value) return;
            language = value;
            PlayerPrefs.SetInt(LanguageKey, (int)value);
            PlayerPrefs.Save();
        }
    }
}
