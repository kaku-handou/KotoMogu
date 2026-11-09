
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Networking;

[DefaultExecutionOrder(-50)]
[DisallowMultipleComponent]
[RequireComponent(typeof(KotoMoguController))]
public class KotoMoguVoiceInput : MonoBehaviour
{
    public string serverUrl = "http://127.0.0.1:8765";
    public bool autoExecute = true;
    public bool automaticListening = true;
    public bool pauseWhenUnfocused = false;
    public Font uiFont;
    public string microphoneName = "";

    // Retained for compatibility with existing scene.
    [Range(0.001f, 0.1f)]
    public float voiceThreshold = 0.012f;

    [Range(0.4f, 2f)]
    public float silenceSeconds = 0.9f;

    [Range(0.1f, 1f)]
    public float minimumVoiceSeconds = 0.25f;

    [Range(2f, 12f)]
    public float maximumSentenceSeconds = 10f;

    public bool inputMuted;

    [Header("WebRTC VAD")]
    [Range(0.2f, 0.6f)]
    public float vadChunkSeconds = 0.3f;

    [Range(1, 4)]
    public int vadStartWindows = 2;

    public string Status { get; private set; } = "接続確認中...";
    public string Transcript { get; private set; } = "";
    public string Language { get; private set; } = "-";
    public float ApiSeconds { get; private set; }
    public float Level { get; private set; }
    public float VadRatio { get; private set; }

    public bool IsInteracting
    {
        get { return speaking || busy; }
    }

    private KotoMoguController controller;
    private AudioClip clip;
    private string device;
    private int readPosition;
    private int blockFrames;
    private float[] block;

    private bool ready;
    private bool busy;
    private bool speaking;
    private bool manual;
    private bool hasFocus = true;
    private bool vadPending;

    private int positiveWindows;
    private float retryAt;
    private float resumeAt;
    private float lastReadAt;
    private float lastVoiceAt;
    private float speechDuration;

    private readonly Queue<float> preRoll = new Queue<float>();
    private readonly List<float> sentence = new List<float>();
    private readonly List<float> vadWindow = new List<float>();

    private UnityWebRequest activeRequest;
    private UnityWebRequest vadRequest;
    private GUIStyle style;
    private Vector2 scroll;

    [Serializable]
    private class Result
    {
        public bool ok;
        public string text, language, warning, error;
        public float elapsed_seconds, audio_seconds;
    }

    [Serializable]
    private class VadResult
    {
        public bool ok;
        public bool speech;
        public float ratio;
        public int voiced_ms;
        public string error;
    }

    void Start()
    {
        controller = GetComponent<KotoMoguController>();

        var debug = GetComponent<KotoMoguDebugUI>();
        if (uiFont == null && debug != null)
            uiFont = debug.uiFont;

        if (GetComponent<KotoMoguPresentation>() == null)
            gameObject.AddComponent<KotoMoguPresentation>();
    }

    void Update()
    {
        if (controller == null || controller.Brain == null)
            return;

        controller.Brain.AutonomyPaused = IsInteracting;

        if (inputMuted || (pauseWhenUnfocused && !hasFocus))
        {
            StopMicrophone();
            Status = "マイク停止中";
            return;
        }

        if (busy)
            return;

        float now = Time.realtimeSinceStartup;

        if (!ready)
        {
            if (now >= retryAt)
                StartCoroutine(CheckServer());
            return;
        }

        if (!automaticListening && !manual)
        {
            if (clip != null)
                StopMicrophone();
            return;
        }

        if (now < resumeAt)
            return;

        if (clip == null)
        {
            if (now >= retryAt)
                StartMicrophone();
            return;
        }

        if (!manual &&
            controller.Brain.IsBusy &&
            !controller.Brain.IsAutonomousAction)
        {
            ResetSentence();
            readPosition = Microphone.GetPosition(device);
            lastReadAt = now;
            return;
        }

        try
        {
            int pos = Microphone.GetPosition(device);

            if (now - lastReadAt > 1.5f)
            {
                StopMicrophone();
                retryAt = now + 0.5f;
                Status = "マイクを再接続しています...";
                return;
            }

            int available =
                (pos - readPosition + clip.samples) % clip.samples;

            while (available >= blockFrames && !busy)
            {
                if (!clip.GetData(block, readPosition))
                    throw new IOException("Cannot read microphone.");

                readPosition =
                    (readPosition + blockFrames) % clip.samples;

                available -= blockFrames;
                lastReadAt = now;

                ProcessBlock();
            }
        }
        catch (Exception ex)
        {
            Status = "マイクエラー: " + ex.Message;
            StopMicrophone();
            retryAt = now + 3f;
        }
    }

    private void StartMicrophone()
    {
        try
        {
            if (Microphone.devices.Length == 0)
                throw new IOException("No microphone detected.");

            device = string.IsNullOrEmpty(microphoneName)
                ? null : microphoneName;

            clip = Microphone.Start(device, true, 2, 16000);

            if (clip == null)
                throw new IOException("Microphone.Start failed.");

            if (clip.frequency != 8000 &&
                clip.frequency != 16000 &&
                clip.frequency != 32000 &&
                clip.frequency != 48000)
                throw new IOException(
                    "Microphone sample rate unsupported by VAD."
                );

            blockFrames = Math.Max(1, clip.frequency / 50);
            block = new float[blockFrames * clip.channels];
            readPosition = 0;
            lastReadAt = Time.realtimeSinceStartup;

            ResetSentence();

            Status = manual
                ? "手動録音中..."
                : "WebRTC VAD 待機中...";
        }
        catch (Exception ex)
        {
            Status = "マイクエラー: " + ex.Message;
            StopMicrophone();
            retryAt = Time.realtimeSinceStartup + 3f;
        }
    }

    private void ProcessBlock()
    {
        double sum = 0;

        foreach (float sample in block)
            sum += sample * sample;

        Level = (float)Math.Sqrt(sum / block.Length);

        float dt = (float)blockFrames / clip.frequency;

        // Manual mode remains available as a fallback.
        if (manual)
        {
            if (!speaking)
            {
                speaking = true;
                controller.Brain.InterruptAutonomousAction();
            }

            sentence.AddRange(block);
            speechDuration += dt;

            if (speechDuration >= maximumSentenceSeconds)
                FinishSentence();

            return;
        }

        // Automatic mode: local WebRTC VAD controls recording.
        if (speaking)
        {
            sentence.AddRange(block);
            speechDuration += dt;
        }
        else
        {
            foreach (float sample in block)
                preRoll.Enqueue(sample);

            int capacity = (int)(
                clip.frequency * clip.channels * 1.2f
            );

            while (preRoll.Count > capacity)
                preRoll.Dequeue();
        }

        vadWindow.AddRange(block);

        int windowSamples = Mathf.Max(
            block.Length,
            Mathf.RoundToInt(
                clip.frequency *
                clip.channels *
                vadChunkSeconds
            )
        );

        if (vadWindow.Count > windowSamples * 3)
        {
            vadWindow.RemoveRange(
                0,
                vadWindow.Count - windowSamples * 3
            );
        }

        if (!vadPending && vadWindow.Count >= windowSamples)
        {
            float[] samples = vadWindow.GetRange(
                0, windowSamples
            ).ToArray();

            vadWindow.RemoveRange(0, windowSamples);
            vadPending = true;

            StartCoroutine(
                CheckVad(EncodeWav(
                    samples, clip.channels, clip.frequency
                ))
            );
        }

        if (speaking &&
            (
                speechDuration >= maximumSentenceSeconds ||
                (
                    speechDuration >= 0.4f &&
                    Time.realtimeSinceStartup - lastVoiceAt
                    >= silenceSeconds
                )
            ))
        {
            FinishSentence();
        }
    }

    private IEnumerator CheckVad(byte[] wav)
    {
        using (var req = new UnityWebRequest(
            serverUrl.TrimEnd('/') + "/vad", "POST"))
        {
            vadRequest = req;
            req.uploadHandler = new UploadHandlerRaw(wav);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "audio/wav");
            req.timeout = 3;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Status = "VAD 接続エラー: " + req.error;
                positiveWindows = 0;
            }
            else
            {
                VadResult result = null;

                try
                {
                    result = JsonUtility.FromJson<VadResult>(
                        req.downloadHandler.text
                    );
                }
                catch (Exception ex)
                {
                    Status = "VAD 応答エラー: " + ex.Message;
                }

                if (result != null && result.ok &&
                    clip != null && !busy &&
                    automaticListening && !manual)
                {
                    VadRatio = result.ratio;

                    if (result.speech)
                    {
                        positiveWindows++;
                        lastVoiceAt = Time.realtimeSinceStartup;

                        if (!speaking &&
                            positiveWindows >= vadStartWindows)
                        {
                            speaking = true;

                            sentence.Clear();
                            sentence.AddRange(preRoll);
                            preRoll.Clear();

                            speechDuration =
                                (float)sentence.Count /
                                (clip.frequency * clip.channels);

                            controller.Brain.InterruptAutonomousAction();
                            Status = "人声検出・録音中...";
                        }
                    }
                    else
                    {
                        positiveWindows = 0;

                        if (!speaking)
                            Status = "VAD 待機中（環境音を無視）";
                    }
                }
                else if (result != null && !result.ok)
                {
                    Status = "VAD エラー: " + result.error;
                }
            }

            vadRequest = null;
        }

        vadPending = false;
    }

    private void FinishSentence()
    {
        if (clip == null)
            return;

        int channels = clip.channels;
        int rate = clip.frequency;
        float[] audio = sentence.ToArray();

        manual = false;

        StopMicrophone();

        float duration =
            (float)audio.Length / (rate * channels);

        if (duration < 0.4f)
        {
            Status = "音声が短すぎます";
            return;
        }

        StartCoroutine(
            Recognize(EncodeWav(audio, channels, rate))
        );
    }

    private static byte[] EncodeWav(
        float[] samples, int channels, int rate)
    {
        using (var stream = new MemoryStream())
        using (var writer = new BinaryWriter(stream))
        {
            writer.Write(
                System.Text.Encoding.ASCII.GetBytes("RIFF")
            );
            writer.Write(36 + samples.Length * 2);

            writer.Write(
                System.Text.Encoding.ASCII.GetBytes("WAVEfmt ")
            );
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)channels);
            writer.Write(rate);
            writer.Write(rate * channels * 2);
            writer.Write((short)(channels * 2));
            writer.Write((short)16);

            writer.Write(
                System.Text.Encoding.ASCII.GetBytes("data")
            );
            writer.Write(samples.Length * 2);

            foreach (float sample in samples)
            {
                writer.Write((short)Mathf.RoundToInt(
                    Mathf.Clamp(sample, -1f, 1f) * 32767
                ));
            }

            writer.Flush();
            return stream.ToArray();
        }
    }

    private IEnumerator Recognize(byte[] wav)
    {
        busy = true;
        Status = "GPT-Transcribe 認識中...";

        using (var req = new UnityWebRequest(
            serverUrl.TrimEnd('/') + "/transcribe", "POST"))
        {
            activeRequest = req;
            req.uploadHandler = new UploadHandlerRaw(wav);
            req.downloadHandler = new DownloadHandlerBuffer();
            req.SetRequestHeader("Content-Type", "audio/wav");
            req.timeout = 120;

            yield return req.SendWebRequest();

            if (req.result != UnityWebRequest.Result.Success)
            {
                Status = "音声認識エラー: " + req.error;
            }
            else
            {
                Result result = null;

                try
                {
                    result = JsonUtility.FromJson<Result>(
                        req.downloadHandler.text
                    );
                }
                catch (Exception ex)
                {
                    Status = "応答エラー: " + ex.Message;
                }

                if (result == null)
                    Status = "応答形式エラー";
                else if (!result.ok)
                    Status = result.error ?? "認識失敗";
                else
                {
                    Transcript = (result.text ?? "").Trim();
                    Language = string.IsNullOrEmpty(result.language)
                        ? "?" : result.language;
                    ApiSeconds = result.elapsed_seconds;

                    if (string.IsNullOrWhiteSpace(Transcript))
                        Status = "人声が確認できませんでした";
                    else if (autoExecute)
                    {
                        controller.ReceiveRecognizedText(Transcript);
                        Status = controller.Brain.Message;
                    }
                    else
                        Status = "認識完了";
                }
            }

            activeRequest = null;
        }

        busy = false;
        resumeAt = Time.realtimeSinceStartup + 0.5f;
        controller.Brain.AutonomyPaused = false;
    }

    private IEnumerator CheckServer()
    {
        busy = true;
        StopMicrophone();

        using (var req = UnityWebRequest.Get(
            serverUrl.TrimEnd('/') + "/health"))
        {
            activeRequest = req;
            req.timeout = 5;
            yield return req.SendWebRequest();

            ready = false;

            if (req.result == UnityWebRequest.Result.Success)
            {
                try
                {
                    string body = req.downloadHandler.text;
                    ready = body.Contains("\"WebRTC\"");
                }
                catch
                {
                    ready = false;
                }
            }

            Status = ready
                ? "WebRTC VAD 準備完了"
                : "VAD サーバー待機中...";

            retryAt = Time.realtimeSinceStartup + 5f;
            activeRequest = null;
        }

        busy = false;
    }

    private void ResetSentence()
    {
        speaking = false;
        sentence.Clear();
        preRoll.Clear();
        vadWindow.Clear();
        speechDuration = 0;
        positiveWindows = 0;
        VadRatio = 0;
    }

    private void StopMicrophone()
    {
        if (clip != null)
        {
            Microphone.End(device);
            Destroy(clip);
            clip = null;
        }

        Level = 0;
        ResetSentence();
    }

    public void StartRecording()
    {
        if (busy || !ready)
            return;

        StopMicrophone();
        manual = true;
        StartMicrophone();
    }

    public void StopAndRecognize()
    {
        if (clip != null)
            FinishSentence();
    }

    public void SetInputMuted(bool muted)
    {
        inputMuted = muted;

        if (muted)
            StopMicrophone();
    }

    void OnApplicationFocus(bool focus)
    {
        hasFocus = focus;

        if (!focus && pauseWhenUnfocused)
        {
            manual = false;
            StopMicrophone();
        }
    }

    void OnDisable()
    {
        StopAllCoroutines();

        if (activeRequest != null)
        {
            activeRequest.Abort();
            activeRequest.Dispose();
            activeRequest = null;
        }

        if (vadRequest != null)
        {
            vadRequest.Abort();
            vadRequest.Dispose();
            vadRequest = null;
        }

        StopMicrophone();

        busy = false;
        manual = false;
        ready = false;
        vadPending = false;

        if (controller != null && controller.Brain != null)
            controller.Brain.AutonomyPaused = false;
    }

    void OnGUI()
    {
        if (!KotoMoguPresentation.DebugVisible)
            return;

        if (style == null)
        {
            style = new GUIStyle(GUI.skin.label)
            {
                fontSize = 16,
                wordWrap = true
            };

            if (uiFont != null)
                style.font = uiFont;
        }

        float width = Mathf.Min(350, Screen.width - 24);

        GUILayout.BeginArea(
            new Rect(
                Screen.width - width - 12,
                12,
                width,
                Screen.height - 24
            ),
            GUI.skin.box
        );

        scroll = GUILayout.BeginScrollView(scroll);

        GUILayout.Label(
            "KOTOMOGU / WebRTC VAD + GPT-Transcribe",
            style
        );

        GUILayout.Label(Status, style);

        GUILayout.Label(
            "Language: " + Language +
            "\nText: " + Transcript +
            "\nAPI time: " + ApiSeconds.ToString("F2") + " s",
            style
        );

        GUILayout.Label(
            "Audio level: " + Level.ToString("F4") +
            "\nVAD speech ratio: " + VadRatio.ToString("F2"),
            style
        );

        automaticListening = GUILayout.Toggle(
            automaticListening,
            "Automatic listening (WebRTC VAD)"
        );

        autoExecute = GUILayout.Toggle(
            autoExecute,
            "Auto execute recognized text"
        );

        bool muted = GUILayout.Toggle(
            inputMuted,
            "Mute microphone"
        );

        if (muted != inputMuted)
            SetInputMuted(muted);

        GUILayout.Label(
            "VAD start windows: " + vadStartWindows,
            style
        );

        vadStartWindows = Mathf.RoundToInt(
            GUILayout.HorizontalSlider(
                vadStartWindows, 1, 4
            )
        );

        GUILayout.Label(
            "Silence: " + silenceSeconds.ToString("F2") + "s",
            style
        );

        GUI.enabled = !busy;

        if (GUILayout.Button("Check server"))
            StartCoroutine(CheckServer());

        if (GUILayout.Button(
            "Microphone: " +
            (string.IsNullOrEmpty(microphoneName)
                ? "Windows default" : microphoneName)))
        {
            string[] devices = Microphone.devices;
            int i = Array.IndexOf(devices, microphoneName) + 1;
            microphoneName =
                i < devices.Length ? devices[i] : "";

            manual = false;
            StopMicrophone();
        }

        if (GUILayout.Button("Start recording (manual test)"))
            StartRecording();

        if (GUILayout.Button("Stop & recognize"))
            StopAndRecognize();

        if (GUILayout.Button("Execute text") &&
            controller != null)
        {
            controller.ReceiveRecognizedText(Transcript);
        }

        GUI.enabled = true;

        GUILayout.EndScrollView();
        GUILayout.EndArea();
    }
}
