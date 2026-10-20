using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.Editor
{
    public static class DesktopSpectatorMenu
    {
        [MenuItem("Tools/Avatar Experiments/Create desktop spectator")]
        public static void Create()
        {
            var existing = Object.FindAnyObjectByType<DesktopSpectator>();
            if (existing != null) { Selection.activeGameObject = existing.gameObject; return; }
            var selected = Selection.activeGameObject;
            var go = new GameObject("Desktop Spectator");
            Undo.RegisterCreatedObjectUndo(go, "Create desktop spectator");
            var spectator = go.AddComponent<DesktopSpectator>();
            spectator.ParticipantCamera = Camera.main;
            if (selected != null) spectator.Subjects = selected.GetComponentsInChildren<Renderer>();
            spectator.HeadVisibility = Object.FindAnyObjectByType<FirstPersonHeadVisibility>();
            Selection.activeGameObject = go;
            Debug.Log("Assign Participant Camera and Subjects, then Play. F2: layout, F3: close-up, F4: controls. The XR camera remains the tracking camera.");
        }
    }
}
