using AvatarExperiments.Perspectives;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace AvatarExperiments.Perspectives.Editor
{
    public sealed class PerspectiveSetupWindow : EditorWindow
    {
        private Transform stage, renderingBranch;
        private Camera camera;
        private TextAsset catalog;
        private string status;

        [MenuItem("Tools/Avatar Experiments/Perspectives/Create observer setup")]
        public static void Open() => GetWindow<PerspectiveSetupWindow>("Perspectives");

        private void OnEnable()
        {
            var matches = AssetDatabase.FindAssets("perspective-catalog t:TextAsset");
            if (matches.Length == 1) catalog = AssetDatabase.LoadAssetAtPath<TextAsset>(AssetDatabase.GUIDToAssetPath(matches[0]));
        }

        private void OnGUI()
        {
            EditorGUILayout.HelpBox("Select the calibrated body/input frame and a rendering-only branch. The branch will be reparented under a new observer; body targets must remain in the original frame. No tracking SDK is installed by this tool.", MessageType.Info);
            stage = (Transform)EditorGUILayout.ObjectField("Body/input stage", stage, typeof(Transform), true);
            renderingBranch = (Transform)EditorGUILayout.ObjectField("Rendering branch", renderingBranch, typeof(Transform), true);
            camera = (Camera)EditorGUILayout.ObjectField("Tracked camera", camera, typeof(Camera), true);
            catalog = (TextAsset)EditorGUILayout.ObjectField("Perspective catalog", catalog, typeof(TextAsset), false);
            if (GUILayout.Button("Create observer and transition controls"))
            {
                try { Create(stage, renderingBranch, camera, catalog); status = "Created. Connect tracking validity, logging and participant UI; see Documentation~/Perspectives.md."; }
                catch (System.Exception e) { status = e.Message; }
            }
            if (!string.IsNullOrEmpty(status)) EditorGUILayout.HelpBox(status, MessageType.Info);
        }

        public static PerspectiveController Create(Transform stage, Transform branch, Camera camera, TextAsset catalog)
        {
            if (Application.isPlaying) throw new System.InvalidOperationException("Create the setup before Play.");
            if (!stage || !branch || !camera || !catalog) throw new System.ArgumentException("Assign all four references.");
            if (stage == branch || stage.IsChildOf(branch) || (camera.transform != branch && !camera.transform.IsChildOf(branch)))
                throw new System.ArgumentException("Rendering branch cannot contain the body stage and must contain the camera.");
            if (branch.GetComponentInParent<PerspectiveController>()) throw new System.ArgumentException("Rendering branch already has perspective controls.");
            if ((stage.lossyScale - Vector3.one).sqrMagnitude > 0.000001f ||
                (branch.lossyScale - Vector3.one).sqrMagnitude > 0.000001f || Vector3.Dot(stage.up, Vector3.up) < 0.99999f)
                throw new System.ArgumentException("Use unit scale and a level, yaw-only stage.");
            PerspectiveCatalog.Parse(catalog.text);
            Undo.IncrementCurrentGroup();
            int undo = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create perspective observer");
            var observer = new GameObject("Perspective Observer");
            Undo.RegisterCreatedObjectUndo(observer, "Create observer");
            observer.transform.SetPositionAndRotation(stage.position, stage.rotation);
            Undo.SetTransformParent(branch, observer.transform, "Separate rendering branch");
            var controller = Undo.AddComponent<PerspectiveController>(observer);
            controller.catalogAsset = catalog;
            controller.bodyStage = stage;
            controller.observerRoot = observer.transform;
            controller.trackedHead = camera.transform;
            var protocol = Undo.AddComponent<PerspectiveProtocol>(observer);
            protocol.perspective = controller;
            var actions = Undo.AddComponent<PerspectiveActions>(observer);
            actions.perspective = controller;
            actions.protocol = protocol;
            var fade = new GameObject("Perspective fade", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            Undo.RegisterCreatedObjectUndo(fade, "Create fade");
            fade.transform.SetParent(camera.transform, false);
            var canvas = fade.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = camera;
            canvas.planeDistance = camera.nearClipPlane + 0.02f;
            canvas.sortingOrder = short.MaxValue;
            var panel = new GameObject("Black", typeof(RectTransform), typeof(Image));
            Undo.RegisterCreatedObjectUndo(panel, "Create fade image");
            panel.transform.SetParent(fade.transform, false);
            var rect = (RectTransform)panel.transform;
            rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = Color.black;
            panel.GetComponent<Image>().raycastTarget = false;
            controller.fadeSurface = fade.GetComponent<CanvasGroup>();
            controller.fadeSurface.alpha = 0;
            controller.fadeSurface.blocksRaycasts = false;
            controller.fadeSurface.interactable = false;
            Undo.CollapseUndoOperations(undo);
            Selection.activeGameObject = observer;
            EditorUtility.SetDirty(controller);
            return controller;
        }
    }

    [CustomEditor(typeof(PerspectiveActions))]
    public sealed class PerspectiveActionsEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();
            EditorGUILayout.HelpBox("These are developer controls. Participant UI should call the same public methods. Tracking validity must come from the host adapter.", MessageType.Info);
            using (new EditorGUI.DisabledScope(!Application.isPlaying))
            {
                var actions = (PerspectiveActions)target;
                if (GUILayout.Button("Initialize")) actions.Initialize();
                if (GUILayout.Button("Resume / restart interrupted step")) actions.Resume();
                if (GUILayout.Button("Begin selected protocol")) actions.StartProtocol();
                if (GUILayout.Button("Continue after dwell")) actions.Continue();
                if (GUILayout.Button("Pause")) actions.Pause();
                if (GUILayout.Button("Cancel protocol")) actions.Cancel();
            }
        }
    }
}
