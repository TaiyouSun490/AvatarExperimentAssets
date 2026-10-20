using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace AvatarExperiments.VirtualHand.Editor
{
    public static class TabletopSpectatorSetup
    {
        [MenuItem("Tools/Avatar Experiments/Virtual Hand/Create desktop spectator copy of current scene")]
        public static void CreateCopy()
        {
            if (Application.isPlaying) throw new InvalidOperationException("Stop Play before copying a scene.");
            var experiment = UnityEngine.Object.FindFirstObjectByType<TabletopExperiment>();
            if (!experiment || string.IsNullOrEmpty(experiment.gameObject.scene.path))
                throw new InvalidOperationException("Open a saved VirtualHand scene first.");
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            string path = experiment.gameObject.scene.path;
            var destination = AssetDatabase.GenerateUniqueAssetPath(path.Replace(".unity", "_Spectator.unity"));
            if (!AssetDatabase.CopyAsset(path, destination)) throw new InvalidOperationException("Could not copy scene.");
            var scene = EditorSceneManager.OpenScene(destination);
            Configure(UnityEngine.Object.FindFirstObjectByType<TabletopExperiment>());
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            TabletopOperatorWindow.Open();
        }
        public static TabletopDesktopSpectator Configure(TabletopExperiment experiment)
        {
            if (!experiment || !experiment.study || !experiment.study.presenter || !experiment.hand || !experiment.hand.skin)
                throw new InvalidOperationException("A complete VirtualHand scene is required.");
            var existing = experiment.GetComponentInChildren<TabletopDesktopSpectator>(true);
            if (existing) return existing;
            var camera = experiment.study.presenter.participantCamera;
            if (!camera) throw new InvalidOperationException("Participant camera is missing.");
            var go = new GameObject("PC Desktop Spectator");
            Undo.RegisterCreatedObjectUndo(go, "Create tabletop desktop spectator");
            go.SetActive(false); go.transform.SetParent(experiment.transform, false);
            var spectator = go.AddComponent<DesktopSpectator>();
            spectator.Configure(camera, new Renderer[] { experiment.hand.skin });
            spectator.DetailTarget = experiment.study.virtualTarget;
            spectator.DetailDistance = .55f;
            spectator.Layout = SpectatorLayout.PictureInPicture;
            var adapter = go.AddComponent<TabletopDesktopSpectator>();
            adapter.experiment = experiment; adapter.spectator = spectator;
            go.SetActive(true);
            EditorUtility.SetDirty(adapter); EditorUtility.SetDirty(spectator);
            return adapter;
        }
    }
}
