#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace KotoMogu.InputPreview
{
    public static class InstallAliceInteraction
    {
        [MenuItem("KotoMogu/Alice Interaction/Install in Alice_Test")]
        public static void Install()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogWarning("[Alice] Exit Play mode first.");
                return;
            }

            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != "Assets/Alice/Scenes/Alice_Test.unity")
            {
                Debug.LogError("[Alice] Open Assets/Alice/Scenes/Alice_Test.unity in the outer Unity project first.");
                return;
            }

            GameObject[] roots = scene.GetRootGameObjects();
            var animators = roots.Where(root => root.name == "KotoMogu_Dragon_Idle")
                .SelectMany(root => root.GetComponentsInChildren<Animator>(true))
                .Where(anim => anim.runtimeAnimatorController != null &&
                    anim.runtimeAnimatorController.name == "Alice_Dragon_Controller").ToArray();
            if (animators.Length != 1)
            {
                Debug.LogError("[Alice] Expected one KotoMogu_Dragon_Idle with Alice_Dragon_Controller.");
                return;
            }

            var previews = roots.SelectMany(root => root.GetComponentsInChildren<LeapRecognitionPreview>(true)).ToArray();
            if (previews.Length > 1)
            {
                Debug.LogError("[Alice] Multiple recognition previews found. Keep one and retry.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Install Alice interaction");

            LeapRecognitionPreview preview = previews.SingleOrDefault();
            if (preview == null)
            {
                var root = new GameObject("KotoMogu_RecognitionPreview");
                Undo.RegisterCreatedObjectUndo(root, "Create recognition preview");
                preview = Undo.AddComponent<LeapRecognitionPreview>(root);
            }

            Undo.RecordObject(preview, "Configure recognition preview");
            if (preview.receiver == null)
            {
                var receiver = preview.GetComponent<PreviewLeapReceiver>();
                preview.receiver = receiver != null ? receiver : Undo.AddComponent<PreviewLeapReceiver>(preview.gameObject);
            }
            preview.showGestureNames = true;

            var reactions = preview.GetComponent<AliceDragonReactions>();
            if (reactions == null)
                reactions = Undo.AddComponent<AliceDragonReactions>(preview.gameObject);

            var settings = new SerializedObject(reactions);
            settings.FindProperty("dragon").objectReferenceValue = animators[0];
            settings.FindProperty("handInput").objectReferenceValue = preview;
            settings.ApplyModifiedProperties();

            Undo.CollapseUndoOperations(group);
            EditorSceneManager.MarkSceneDirty(scene);
            Selection.activeGameObject = preview.gameObject;
            Debug.Log("[Alice] Installed. Save the scene, enable the preview object, and press Play.", preview);
        }
    }
}
#endif
