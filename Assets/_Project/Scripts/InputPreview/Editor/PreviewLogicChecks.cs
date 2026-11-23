#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;
using KotoMogu.InputPreview;

public static class PreviewLogicChecks
{
    static void Check(bool pass, string name) { if (!pass) throw new Exception("Preview check failed: " + name); }
    [MenuItem("KotoMogu/Recognition Preview/Run Logic Checks")]
    public static void Run()
    {
        int[][] fingers = { new[]{1,1,1,1,1}, new[]{0,0,0,0,0}, new[]{1,0,0,0,0}, new[]{0,1,0,0,0}, new[]{1,1,0,0,0}, new[]{0,1,1,0,0}, new[]{0,1,0,0,1}, new[]{1,0,0,0,1} };
        for (int i = 0; i < fingers.Length; i++)
        {
            var f = fingers[i];
            Check(PreviewPoseLogic.Classify(f[0], f[1], f[2], f[3], f[4], i == 0 ? 0.1f : 0.9f, 0, 1, 0) == PreviewPoseLogic.Labels[i], "mapping " + i);
        }
        Check(PreviewPoseLogic.Classify(1,0,0,0,0,.9f,0,-1,0) == "", "down thumb ignored");
        Check(PreviewPoseLogic.Classify(0,1,0,0,0,float.NaN,0,1,0) == "", "invalid strength");
        Check(PreviewPoseLogic.Classify(0,2,0,0,0,0,0,1,0) == "", "invalid finger");
        var g = new PreviewPoseLogic.Gate();
        Check(!g.Observe(1,"point",0,.2f,.15f), "wait for stability");
        Check(g.Observe(1,"point",.21f,.2f,.15f), "stable trigger");
        Check(!g.Observe(1,"point",3,.2f,.15f), "held pose once");
        g.Observe(1,"",3.1f,.2f,.15f); g.Observe(1,"",3.3f,.2f,.15f);
        Check(!g.Observe(1,"point",3.4f,.2f,.15f), "release rearms dwell");
        Check(g.Observe(1,"point",3.61f,.2f,.15f), "repeated gesture");
        Check(!g.Observe(1,"victory",4,.2f,.15f), "switch starts dwell");
        Check(g.Observe(1,"victory",4.21f,.2f,.15f), "switch fires");
        g.Reset();
        Check(!g.Observe(1,"point",5,.2f,.15f), "disconnect resets dwell");
        Check(!g.Observe(2,"point",5.21f,.2f,.15f), "new hand resets dwell");
        Debug.Log("Recognition preview logic checks passed. Device tracking and screen rendering still need Play mode testing.");
    }
}
#endif
