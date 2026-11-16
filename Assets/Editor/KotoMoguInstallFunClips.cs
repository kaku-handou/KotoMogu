#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class KotoMoguInstallFunClips
{
    [Serializable] private class Data { public Clip[] clips; }
    [Serializable] private class Clip { public string name; public bool loop; public float[] times; public Track[] tracks; }
    [Serializable] private class Track { public int node; public string kind; public float[] values; }
    // 元の生成モデルと同一の転軸階層。
    [MenuItem("KotoMogu/Test Fun/Bite (Play Mode)")]
    private static void TestBite() { Test(KotoAction.Bite); }
    [MenuItem("KotoMogu/Test Fun/PlayDead (Play Mode)")]
    private static void TestDead() { Test(KotoAction.PlayDead); }
    [MenuItem("KotoMogu/Test Fun/Victory (Play Mode)")]
    private static void TestVictory() { Test(KotoAction.Victory); }
    [MenuItem("KotoMogu/Test Fun/Roar (Play Mode)")]
    private static void TestRoar() { Test(KotoAction.Roar); }
    [MenuItem("KotoMogu/Test Fun/Shy (Play Mode)")]
    private static void TestShy() { Test(KotoAction.Shy); }
    private static void Test(KotoAction action)
    {
        if (!EditorApplication.isPlaying) { Debug.LogWarning("Enter Play mode and select KotoMogu first."); return; }
        GameObject selected = Selection.activeGameObject;
        var pet = selected == null ? null : selected.GetComponentInParent<KotoMoguController>();
        if (pet == null) { Debug.LogWarning("Select the KotoMogu control root in Hierarchy."); return; }
        pet.SendCommand(action.ToString());
        Debug.Log("Fun action test: " + pet.Brain.Message, pet);
    }
    private static readonly string[] Paths = {
        "MotionRoot", "MotionRoot/BodyPivot", "MotionRoot/BodyPivot/HeadPivot", "MotionRoot/BodyPivot/HeadPivot/MouthPivot",
        "MotionRoot/BodyPivot/HeadPivot/EyePivot_L", "MotionRoot/BodyPivot/ArmPivot_L", "MotionRoot/BodyPivot/WingPivot_L", "MotionRoot/FootPivot_L",
        "MotionRoot/BodyPivot/HeadPivot/EyePivot_R", "MotionRoot/BodyPivot/ArmPivot_R", "MotionRoot/BodyPivot/WingPivot_R", "MotionRoot/FootPivot_R", "MotionRoot/BodyPivot/TailPivot"
    };
    [MenuItem("KotoMogu/Install Fun Animations")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode first."); return; }
        GameObject selected = Selection.activeGameObject;
        var motion = selected == null ? null : selected.GetComponent<KotoMoguMotion>();
        if (motion == null && selected != null) motion = selected.GetComponentInParent<KotoMoguMotion>();
        Animator animator = motion != null ? motion.modelAnimator :
            (selected == null ? null : selected.GetComponentInChildren<Animator>(true));
        if (animator == null) { Debug.LogError("Select your scene KotoMogu or KotoMoguAnimated first."); return; }
        var controller = animator.runtimeAnimatorController as AnimatorController;
        if (controller == null) { Debug.LogError("The selected Animator needs the generated KotoMoguActions controller."); return; }
        foreach (string path in Paths)
            if (animator.transform.Find(path) == null) { Debug.LogError("Unexpected model hierarchy: " + path); return; }
        string[] found = AssetDatabase.FindAssets("KotoMoguFunClips t:TextAsset");
        if (found.Length != 1) { Debug.LogError("Import exactly one KotoMoguFunClips.txt."); return; }
        Data data = JsonUtility.FromJson<Data>(AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(found[0])).text);
        string folder = Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller)).Replace('\\', '/');
        if (data == null || data.clips == null || data.clips.Length != 5)
            throw new InvalidDataException("Invalid fun clip data");
        // Validate all clips before touching assets.
        foreach (Clip c in data.clips)
        {
            if ((c.name != "Bite" && c.name != "PlayDead" && c.name != "Victory" && c.name != "Roar" && c.name != "Shy") || c.times == null || c.times.Length < 2 || c.tracks == null)
                throw new InvalidDataException("Invalid fun clip definition");
            KotoAction action = (KotoAction)Enum.Parse(typeof(KotoAction), c.name);
            if (Mathf.Abs(c.times[c.times.Length - 1] - KotoMoguBrain.Duration(action)) > 0.001f)
                throw new InvalidDataException("Update KotoMoguBrain.cs: duration mismatch");
            foreach (Track track in c.tracks)
                if (track.node < 0 || track.node >= Paths.Length || track.values == null ||
                    track.values.Length != c.times.Length * (track.kind == "rotation" ? 4 : 3))
                    throw new InvalidDataException("Invalid animation track");
        }
        foreach (Clip c in data.clips)
        {
            // Preserve the working PlayDead clip/state exactly if already installed.
            if (c.name == "PlayDead")
            {
                bool installed = false;
                foreach (var item in controller.layers[0].stateMachine.states)
                    if (item.state.name == "PlayDead" && item.state.motion != null) installed = true;
                if (installed) continue;
            }
            var clip = new AnimationClip { name = c.name, frameRate = 30 };
            foreach (Track t in c.tracks)
            {
                int dims = t.kind == "rotation" ? 4 : 3;
                string property = t.kind == "rotation" ? "m_LocalRotation" : t.kind == "scale" ? "m_LocalScale" : "m_LocalPosition";
                for (int axis = 0; axis < dims; axis++)
                {
                    float sign = t.kind == "translation" && axis == 0 || t.kind == "rotation" && (axis == 1 || axis == 2) ? -1 : 1;
                    var keys = new Keyframe[c.times.Length];
                    for (int k = 0; k < keys.Length; k++) keys[k] = new Keyframe(c.times[k], sign * t.values[k * dims + axis]);
                    var curve = new AnimationCurve(keys);
                    for (int k = 0; k < keys.Length; k++)
                    {
                        AnimationUtility.SetKeyLeftTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                        AnimationUtility.SetKeyRightTangentMode(curve, k, AnimationUtility.TangentMode.Linear);
                    }
                    AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve(Paths[t.node], typeof(Transform), property + "." + "xyzw"[axis]), curve);
                }
            }
            clip.EnsureQuaternionContinuity();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = false;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            string pathName = folder + "/" + c.name + ".anim";
            var existing = AssetDatabase.LoadAssetAtPath<AnimationClip>(pathName);
            if (existing == null) AssetDatabase.CreateAsset(clip, pathName);
            else { EditorUtility.CopySerialized(clip, existing); UnityEngine.Object.DestroyImmediate(clip); clip = existing; EditorUtility.SetDirty(clip); }
            var machine = controller.layers[0].stateMachine;
            AnimatorState state = null;
            foreach (var child in machine.states) if (child.state.name == c.name) state = child.state;
            if (state == null) state = machine.AddState(c.name);
            state.motion = clip;
            state.writeDefaultValues = true;
        }
        EditorUtility.SetDirty(controller); AssetDatabase.SaveAssets();
        Debug.Log("Fun v3 installed (Bite, Victory, Roar, Shy). Existing PlayDead preserved. Folder: " + folder + ". No need to rebuild or replace the model.");
    }
}
#endif
