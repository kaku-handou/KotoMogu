using System;

// Unity に依存しない内部ロジック。入力はメインスレッドから渡す。
public enum KotoAction { Idle, Greet, Jump, Eat, Happy, Sleep, LookAround, Hungry, Weak }

[Serializable]
public class KotoMoguSaveData
{
    public int version = 1;
    public float fullness = 50f;
    public float mood = 60f;
    public float energy = 80f;
    public int meals;
}

public sealed class KotoMoguBrain
{
    public KotoMoguSaveData Data { get; private set; }
    public KotoAction Action { get; private set; }
    public float ActionTime { get; private set; }
    public string Message { get; private set; }
    public string LastInput { get; private set; }
    public bool IsBusy { get { return Action != KotoAction.Idle && Action != KotoAction.Sleep; } }
    public event System.Action<KotoAction> ActionStarted;

    // 自律行動は入力処理と同じ状態機械を使用する。
    public bool AutonomousEnabled = true;
    public bool AutonomyPaused;
    private bool automaticSleep;
    private double nextAutonomousAt = 12;
    public float IdleMinSeconds = 10f;
    public float IdleMaxSeconds = 16f;
    public float SecondsUntilIdleAction { get { return (float)Math.Max(0, nextAutonomousAt - clock); } }
    public bool IsAutonomousAction { get { return Action == KotoAction.LookAround || Action == KotoAction.Hungry || Action == KotoAction.Weak; } }
    public bool ForceLook { get; private set; }
    private float pauseSeconds;
    private double sleepAllowedAt;
    private double hungerNoticeAt;
    private readonly Random random = new Random();
    public void InterruptAutonomousAction()
    {
        if (IsAutonomousAction) StartAction(KotoAction.Idle);
    }
    private void TickAutonomy()
    {
        if (!AutonomousEnabled) return;
        // 雑音や長い認識待ちが自律動作を永久に止めないようにする。
        if (AutonomyPaused && pauseSeconds < 1.5f) return;
        if (Action == KotoAction.Sleep)
        {
            // 明示的なおやすみ指令では勝手に起床しない。
            if (automaticSleep && Data.energy >= 80f)
            {
                automaticSleep = false;
                StartAction(KotoAction.Idle);
                Message = "元気になったよ！";
                nextAutonomousAt = clock + 12;
            }
            return;
        }
        if (Action != KotoAction.Idle) return;
        if (Data.energy <= 15f && clock >= sleepAllowedAt)
        {
            automaticSleep = true;
            StartAction(KotoAction.Sleep);
            Message = "ちょっと休むね…";
            return;
        }
        if (Data.fullness < 20f && clock >= hungerNoticeAt)
        {
            bool weak = Data.fullness <= 0f;
            StartAction(weak ? KotoAction.Weak : KotoAction.Hungry);
            Message = weak ? "おなかがぺこぺこ…ことばを食べたい…" : "おなかすいた！ことばを聞かせて！";
            hungerNoticeAt = clock + 30;
            return;
        }
        if (clock >= nextAutonomousAt)
        {
            double min = Math.Max(2, IdleMinSeconds);
            double max = Math.Max(min, IdleMaxSeconds);
            nextAutonomousAt = clock + min + random.NextDouble() * (max - min);
            // 張り切る動作の報酬・消費を使わず、待機表現だけ変える。
            ForceLook = false;
            StartAction(KotoAction.LookAround);
        }
    }
    private double clock;
    private double feedAllowedAt;
    private string previousText = "";
    private double previousTextAt = -100;
    private const float FeedCooldown = 3f;

    public KotoMoguBrain(KotoMoguSaveData saved = null)
    {
        Data = saved ?? new KotoMoguSaveData();
        Sanitize();
        Action = KotoAction.Idle;
        Message = "Ready!";
        LastInput = "-";
    }

    private static float Clamp(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value)) return 50f;
        return Math.Max(0f, Math.Min(100f, value));
    }

    private void Sanitize()
    {
        Data.fullness = Clamp(Data.fullness);
        Data.mood = Clamp(Data.mood);
        Data.energy = Clamp(Data.energy);
        Data.meals = Math.Max(0, Data.meals);
    }

    public static float Duration(KotoAction action)
    {
        switch (action)
        {
            case KotoAction.Hungry:
            case KotoAction.Weak:
            case KotoAction.LookAround: return 2.5f;
            case KotoAction.Jump: return 0.7f;
            case KotoAction.Eat: return 2f;
            case KotoAction.Greet: return 1.5f;
            case KotoAction.Happy: return 1.8f;
            default: return 0f;
        }
    }

    public void Tick(float seconds)
    {
        if (seconds <= 0 || float.IsNaN(seconds) || float.IsInfinity(seconds)) return;
        clock += seconds;
        pauseSeconds = AutonomyPaused ? pauseSeconds + seconds : 0f;
        bool sleeping = Action == KotoAction.Sleep;
        Data.fullness -= seconds * 0.08f;
        Data.energy += seconds * (sleeping ? 2f : -0.04f);
        if (!sleeping && Data.fullness < 20f) Data.mood -= seconds * 0.12f;
        ActionTime += seconds;
        if (IsBusy && ActionTime >= Duration(Action))
        {
            StartAction(KotoAction.Idle);
            Message = "Ready!";
        }
        Sanitize();
        TickAutonomy();
    }

    private void StartAction(KotoAction next)
    {
        Action = next;
        ActionTime = 0f;
        if (ActionStarted != null) ActionStarted(next);
    }

    public void ResetAll()
    {
        Data = new KotoMoguSaveData();
        feedAllowedAt = 0;
        previousText = "";
        previousTextAt = -100;
        automaticSleep = false;
        pauseSeconds = 0;
        nextAutonomousAt = clock + 12;
        hungerNoticeAt = 0;
        sleepAllowedAt = 0;
        LastInput = "reset";
        StartAction(KotoAction.Idle);
        Message = "Stats reset.";
    }

    public bool Request(KotoAction next, string source = "button")
    {
        if (!Enum.IsDefined(typeof(KotoAction), next))
        { Message = "Unknown command."; return false; }
        // 入力を自律的な張望より優先する。
        InterruptAutonomousAction();
        LastInput = source + " -> " + next;
        // Idle は明示的な停止・起床指令。
        if (next == KotoAction.Idle)
        {
            automaticSleep = false;
            sleepAllowedAt = clock + 20;
            nextAutonomousAt = clock + 12;
            StartAction(next);
            Message = "Awake / ready!";
            return true;
        }
        if (IsBusy) { Message = "Busy: please wait."; return false; }
        if (Action == KotoAction.Sleep)
        {
            Message = "Sleeping. Use Wake first.";
            return false;
        }
        if (next == KotoAction.Sleep)
        {
            automaticSleep = false;
            StartAction(next); Message = "Zzz... energy is recovering."; return true;
        }
        if (next == KotoAction.LookAround || next == KotoAction.Hungry || next == KotoAction.Weak)
        {
            ForceLook = next == KotoAction.LookAround;
            StartAction(next); Message = next + " (test)"; return true;
        }
        if (next == KotoAction.Eat)
        {
            if (clock < feedAllowedAt) { Message = "Wait 3 seconds between meals."; return false; }
            if (Data.fullness >= 95f) { Message = "Full! Try again later."; return false; }
            Data.fullness += 15f; Data.mood += 5f; Data.meals++;
            feedAllowedAt = clock + FeedCooldown;
            Message = "Yummy! Words became food.";
        }
        else
        {
            float cost = next == KotoAction.Jump ? 8f : next == KotoAction.Greet ? 0f : 3f;
            if (Data.energy < cost) { Message = "Too tired. Please let me sleep."; return false; }
            Data.energy -= cost;
            Data.mood += next == KotoAction.Happy ? 8f : 2f;
            Message = next + "!";
        }
        Sanitize();
        nextAutonomousAt = clock + 12;
        StartAction(next);
        return true;
    }

    // 音声認識の最終確定テキストも、この入口へ渡す。
    public bool ReceiveText(string text)
    {
        string normalized = (text ?? "").Trim().ToLowerInvariant();
        if (normalized.Length == 0) { Message = "Please enter some words."; return false; }
        if (normalized.Length > 200) { Message = "Please use 200 characters or fewer."; return false; }
        if (normalized == previousText && clock - previousTextAt < 3)
        {
            Message = "Duplicate text ignored (3 seconds)."; return false;
        }
        if (Has(normalized, "不要", "别跳", "別跳", "しないで", "ないで", "don't", "do not", "dont "))
        { Message = "Negative instruction: no action taken."; return false; }
        // 優先順位：起床 > 睡眠 > ジャンプ > 挨拶 > ほめ言葉 > 食事。
        KotoAction next;
        if (Has(normalized, "おはよう", "good morning", "早上好", "早安"))
        {
            if (Action == KotoAction.Sleep) Request(KotoAction.Idle, "morning wake");
            next = KotoAction.Greet;
        }
        else if (Has(normalized, "起きて", "起きる", "wake", "醒来", "醒來", "起床")) next = KotoAction.Idle;
        else if (Has(normalized, "おやすみ", "寝て", "sleep", "good night", "睡觉", "睡覺", "晚安")) next = KotoAction.Sleep;
        else if (Has(normalized, "ジャンプ", "跳んで", "jump", "跳跃", "跳躍", "跳一下", "跳一跳", "跳起来", "跳起來")) next = KotoAction.Jump;
        else if (Has(normalized, "こんにちは", "こんばんは", "hello", "你好", "您好")) next = KotoAction.Greet;
        else if (Has(normalized, "かわいい", "可愛い", "ありがとう", "cute", "thank you", "可爱", "可愛", "谢谢", "謝謝")) next = KotoAction.Happy;
        else next = KotoAction.Eat;
        bool accepted = Request(next, "text: " + text.Trim());
        // 拒否された入力は、再試行できるよう重複記録に含めない。
        if (accepted) { previousText = normalized; previousTextAt = clock; }
        return accepted;
    }

    private static bool Has(string text, params string[] words)
    {
        foreach (string word in words) if (text.Contains(word)) return true;
        return false;
    }

    // 手の認識は別担当。ここでは認識後のラベルだけ受け取る。
    public bool ReceiveGesture(string label)
    {
        switch ((label ?? "").Trim().ToLowerInvariant())
        {
            case "open_palm": return Request(KotoAction.Greet, "gesture: open_palm");
            case "fist": return Request(KotoAction.Eat, "gesture: fist");
            case "thumb_up": return Request(KotoAction.Happy, "gesture: thumb_up");
            default: Message = "Unknown gesture."; return false;
        }
    }
}
