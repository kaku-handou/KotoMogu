#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using KotoMogu.InputPreview;

public static class InstallRecognitionPreview
{
    [MenuItem("KotoMogu/Recognition Preview/Install in Current Scene")]
    public static void Install()
    {
        if (EditorApplication.isPlaying) { Debug.LogWarning("Stop Play mode before installing."); return; }
        var existing = Object.FindFirstObjectByType<LeapRecognitionPreview>(FindObjectsInactive.Include);
        if (existing != null) { Selection.activeGameObject = existing.gameObject; Debug.Log("Recognition Preview already exists. Enable its object and components if needed."); return; }
        GameObject root = new GameObject("KotoMogu_RecognitionPreview");
        Undo.RegisterCreatedObjectUndo(root, "Install recognition preview");
        var receiver = Undo.AddComponent<PreviewLeapReceiver>(root);
        var preview = Undo.AddComponent<LeapRecognitionPreview>(root);
        preview.receiver = receiver;
        Selection.activeGameObject = root;
        EditorSceneManager.MarkSceneDirty(root.scene);
        Debug.Log("Recognition Preview installed. Save scene. Start protocol v2 LeapBridge, then Play. Disable any OTHER UDP receiver/udp_monitor using port 8767. No Animator needed.", root);
    }
}
#endif
