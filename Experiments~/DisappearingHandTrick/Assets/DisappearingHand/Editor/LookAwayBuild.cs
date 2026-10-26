using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace DisappearingHand.Editor
{
    public static class LookAwayBuild
    {
        public const string ScenePath = "Assets/Scenes/DHTLookAwayVR.unity";
        [MenuItem("Tools/Disappearing Hand/Create look-away variant")]
        public static void Prepare()
        {
            ExperimentBuild.Prepare();
            var e = UnityEngine.Object.FindAnyObjectByType<DisappearingHandExperiment>();
            e.name = "Disappearing Hand - HMD Look Away";
            e.settings.presentation = DriftPresentation.LookAwaySteps;
            e.menu.transform.Find("Title").GetComponent<Text>().text = "DISAPPEARING HAND / LOOK AWAY";
            // The original smoke component tests continuous drift in its own scene.
            UnityEngine.Object.DestroyImmediate(e.GetComponent<PlayerSmokeCheck>());
            e.gameObject.AddComponent<LookAwayPlayerSmoke>().experiment = e;
            e.UpdateLabels();
            PlayerSettings.productName = "DHTLookAway";
            EditorSceneManager.SaveScene(e.gameObject.scene,ScenePath);
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets();
        }
        [MenuItem("Tools/Disappearing Hand/Validate and build look-away Windows")]
        public static void Build()
        {
            Prepare(); LookAwayValidation.Run();
            Directory.CreateDirectory("Builds/LookAwayWindows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[]{ScenePath}, locationPathName = "Builds/LookAwayWindows/DHTLookAway.exe", target = BuildTarget.StandaloneWindows64 });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Look-away build failed: " + report.summary.result);
            Debug.Log("LOOK_AWAY_BUILD_PASS");
        }
    }
}
