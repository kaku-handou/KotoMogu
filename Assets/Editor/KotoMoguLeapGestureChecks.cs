#if UNITY_EDITOR
using System;
using UnityEditor;
using H = KotoMoguLeapReceiver.LeapHand;
public static class KotoMoguLeapGestureChecks
{
    private static void Check(bool ok, string message) { if (!ok) throw new Exception("Leap gesture check failed: " + message); }
    private static string Label(H h) { return KotoMoguLeapGestureInput.Classify(h, 0.3f, 0.8f, 0.5f); }
    [MenuItem("KotoMogu/Run Leap Gesture Checks")]
    public static void Run()
    {
        Check(Label(new H {thumb=1,index=1,middle=1,ring=1,pinky=1,grab=0.1f}) == "open_palm", "open");
        Check(Label(new H {grab=0.95f}) == "fist", "fist");
        Check(Label(new H {thumb=1,grab=0.95f,ty=10}) == "thumb_up", "thumb precedes fist");
        Check(Label(new H {thumb=1,grab=0.95f,ty=-10}) == "", "down thumb rejected");
        Check(Label(new H {grab=float.NaN}) == "", "invalid strength rejected");
        Check(Label(new H {index=1,thumb=0}) == "point", "index-only bite");
        Check(Label(new H {index=1,thumb=1,ty=10}) == "finger_gun", "gun wins over bite/thumb up");
        Check(Label(new H {index=1,middle=1}) == "victory", "V gesture");
        Check(Label(new H {index=1,pinky=1}) == "rock", "rock gesture");
        Check(Label(new H {thumb=1,pinky=1}) == "shaka", "shaka gesture");
        Check(Label(new H {index=1,middle=1,ring=1}) == "", "three fingers ignored");
        Check(Label(new H {thumb=1,index=1,middle=1}) == "", "ambiguous V with thumb ignored");
        var brain = new KotoMoguBrain(); brain.AutonomousEnabled = false;
        int meals = brain.Data.meals;
        Check(brain.ReceiveGesture("point") && brain.Action == KotoAction.Bite, "bite request");
        Check(brain.Data.meals == meals, "bite is not feeding");
        Check(!brain.ReceiveGesture("finger_gun"), "busy refuses gun");
        brain.Tick(1.61f); Check(brain.Action == KotoAction.Bite, "longer Bite must not truncate at old v2 duration");
        brain.Tick(1.2f); Check(brain.Action == KotoAction.Idle, "bite returns idle");
        Check(brain.ReceiveGesture("finger_gun") && brain.Action == KotoAction.PlayDead, "play dead request");
        brain.Tick(4.21f); Check(brain.Action == KotoAction.Idle, "play dead returns idle");
        string[] labels = { "victory", "rock", "shaka" };
        KotoAction[] actions = { KotoAction.Victory, KotoAction.Roar, KotoAction.Shy };
        for (int i = 0; i < labels.Length; i++)
        {
            Check(brain.ReceiveGesture(labels[i]) && brain.Action == actions[i], "new action mapping " + labels[i]);
            Check(KotoMoguLeapGestureInput.FunAction(labels[i]) == actions[i].ToString(), "animator mapping " + labels[i]);
            brain.Tick(KotoMoguBrain.Duration(actions[i]) + 0.01f);
            Check(brain.Action == KotoAction.Idle, "new action restores idle " + labels[i]);
        }
        Check(brain.Data.meals == meals, "fun actions never feed");
        Check(brain.Request(KotoAction.Sleep), "sleep");
        Check(!brain.ReceiveGesture("point"), "sleep refuses bite");
        var g = new KotoMoguLeapGestureInput.PoseGate();
        Check(!g.Observe(1,"fist",0,0.3f,0.25f), "no immediate trigger");
        Check(g.Observe(1,"fist",0.31f,0.3f,0.25f), "stable trigger");
        Check(!g.Observe(1,"fist",5,0.3f,0.25f), "held pose cannot repeat");
        g.Observe(1,"",5.1f,0.3f,0.25f);
        g.Observe(1,"",5.4f,0.3f,0.25f);
        Check(!g.Observe(1,"fist",5.5f,0.3f,0.25f), "rearm waits");
        Check(g.Observe(1,"fist",5.81f,0.3f,0.25f), "neutral rearms");
        Check(!g.Observe(2,"fist",6,0.3f,0.25f), "new hand starts new dwell");
        UnityEngine.Debug.Log("Leap gesture classification and latch checks passed.");
    }
}
#endif
