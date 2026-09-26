using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.Editor
{
    public static class AvatarExperimentMenu
    {
        [MenuItem("Tools/Avatar Experiments/Create setup for selected Humanoid")]
        public static void Create()
        {
            var selected=Selection.activeGameObject;
            var avatar=selected!=null?selected.GetComponentInChildren<Animator>():null;
            if(avatar==null || !avatar.isHuman)
            { EditorUtility.DisplayDialog("Avatar Experiments","Select a user-owned Humanoid avatar first.","OK");return; }
            if(Object.FindAnyObjectByType<AvatarExperimentSetup>()!=null)
            { EditorUtility.DisplayDialog("Avatar Experiments","A setup already exists. Edit its references instead.","OK");return; }
            var root=new GameObject("Avatar Experiment");
            Undo.RegisterCreatedObjectUndo(root,"Create avatar experiment");
            var setup=root.AddComponent<AvatarExperimentSetup>();
            setup.Avatar=avatar;
            // Do not silently replace the scene's XR origin or camera.
            Selection.activeGameObject=root;
            Debug.Log("Assign TrackingOrigin and XrCamera on Avatar Experiment, then enter Play. Use a separate, unit-scale XR origin.");
        }
    }
}
