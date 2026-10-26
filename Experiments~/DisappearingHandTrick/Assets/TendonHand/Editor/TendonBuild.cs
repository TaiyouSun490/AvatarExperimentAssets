using System;
using System.IO;
using DisappearingHand;
using DisappearingHand.Editor;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace TendonHand.Editor
{
    public static class TendonBuild
    {
        public const string ScenePath = "Assets/Scenes/TendonHandVR.unity";
        [MenuItem("Tools/Tendon Hand/Create experiment scene")]
        public static void Prepare()
        {
            ExperimentBuild.Prepare();
            var old = UnityEngine.Object.FindAnyObjectByType<DisappearingHandExperiment>();
            var e = old.gameObject.AddComponent<TendonHandExperiment>();
            e.rig = old.rig; e.tableFrame = old.tableFrame;
            e.leftMarker = old.leftMarker; e.rightMarker = old.rightMarker;
            e.instructionText = old.instructionText; e.statusText = old.statusText; e.menu = old.menu;
            e.name = "Tendon Vibration Hand Experiment";
            var pointer = e.GetComponent<ExperimentMenuPointer>();
            pointer.experiment = null; pointer.tendonExperiment = e;
            string[] labels = {"ALIGN", "START", "STOP / RESET", "LEFT / RIGHT", "RECOVERY VIEW", "STOP VIBRATION", "VIBRATION ON/OFF", "CONNECT", "HIDE MENU"};
            UnityAction[] actions = {e.Align,e.StartTrial,e.ResetTrial,e.ToggleTarget,e.CycleRecovery,e.StopInduction,e.ToggleVibration,e.Connect,e.ToggleMenu};
            for (int i=0;i<pointer.buttons.Length;i++)
            {
                var button = pointer.buttons[i]; button.onClick = new Button.ButtonClickedEvent();
                UnityEventTools.AddPersistentListener(button.onClick, actions[i]);
                button.GetComponentInChildren<Text>().text = labels[i];
            }
            e.menu.transform.Find("Title").GetComponent<Text>().text = "TENDON VIBRATION / VIRTUAL HAND";
            var session = e.GetComponent<PcvrSession>(); session.experiment = null; session.tendonExperiment = e;
            UnityEngine.Object.DestroyImmediate(e.GetComponent<PlayerSmokeCheck>());
            UnityEngine.Object.DestroyImmediate(old);
            e.gameObject.AddComponent<TendonPlayerSmoke>().experiment = e;
            e.UpdateLabels();
            PlayerSettings.productName = "TendonHandTrick";
            PlayerSettings.defaultScreenHeight = 1080;
            EditorSceneManager.SaveScene(e.gameObject.scene, ScenePath);
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets();
            Debug.Log("TENDON_SCENE_READY: " + ScenePath);
        }
        [MenuItem("Tools/Tendon Hand/Validate and build Windows PCVR")]
        public static void Build()
        {
            Prepare(); TendonValidation.Run();
            Directory.CreateDirectory("Builds/TendonWindows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes = new[]{ScenePath}, locationPathName = "Builds/TendonWindows/TendonHandTrick.exe", target = BuildTarget.StandaloneWindows64 });
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Build failed: " + report.summary.result);
            Debug.Log("TENDON_BUILD_PASS");
        }
    }
}
