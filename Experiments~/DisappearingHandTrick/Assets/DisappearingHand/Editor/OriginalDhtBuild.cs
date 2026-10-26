using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace DisappearingHand.Editor
{
    public static class OriginalDhtBuild
    {
        public const string ScenePath = "Assets/Scenes/DHTOriginal2011VR.unity";
        [MenuItem("Tools/Disappearing Hand/Create original 2011 procedure VR")]
        public static void Prepare()
        {
            ExperimentBuild.Prepare();
            var old = Object.FindAnyObjectByType<DisappearingHandExperiment>();
            var root = old.gameObject; root.name = "DHT - Original 2011 procedure - VR adaptation";
            var e = root.AddComponent<OriginalDhtExperiment>();
            e.rig = old.rig; e.tableFrame = old.tableFrame; e.menu = old.menu;
            e.instructionText = old.instructionText; e.statusText = old.statusText;
            Object.DestroyImmediate(old.leftMarker.gameObject); Object.DestroyImmediate(old.rightMarker.gameObject);
            Object.DestroyImmediate(root.GetComponent<PlayerSmokeCheck>());
            var session = root.GetComponent<PcvrSession>(); session.experiment = null; session.originalExperiment = e;
            var pointer = root.GetComponent<ExperimentMenuPointer>(); pointer.experiment = null; pointer.originalExperiment = e;
            // Only neutral setup controls are visible in the headset. Trial gates are operator-only.
            UnityAction[] actions = {e.AlignTable,e.StartTrial,e.ResetTrial,e.ToggleMenu};
            string[] labels = {"ALIGN TABLE","START","RESET","HIDE MENU"};
            for (int i=0;i<pointer.buttons.Length;i++)
            {
                var button = pointer.buttons[i]; button.onClick = new Button.ButtonClickedEvent();
                if (i >= actions.Length) { button.gameObject.SetActive(false); continue; }
                button.GetComponentInChildren<Text>().text = labels[i];
                UnityEventTools.AddPersistentListener(button.onClick,actions[i]);
            }
            e.menu.transform.Find("Title").GetComponent<Text>().text = "HAND TASK";
            e.rig.head.backgroundColor = Color.black;
            var black = Mat("OriginalBlack",Color.black);
            old.tableTop.GetComponent<Renderer>().sharedMaterial = black;
            GameObject.Find("Floor").GetComponent<Renderer>().sharedMaterial = black;
            GameObject.Find("Backdrop").GetComponent<Renderer>().sharedMaterial = black;
            var blue = Mat("OriginalBlue",new Color(0,.15f,1));
            e.leftBar = Bar("Left outer blue bar",e.tableFrame,blue);
            e.centreBar = Bar("Centre blue bar",e.tableFrame,blue);
            e.rightBar = Bar("Right outer blue bar",e.tableFrame,blue);
            Object.DestroyImmediate(old);
            e.gameObject.AddComponent<OriginalDhtPlayerSmoke>().experiment = e;
            e.UpdateLabels(); e.ApplyVisuals();
            PlayerSettings.productName = "DHTOriginal2011";
            EditorSceneManager.SaveScene(root.scene,ScenePath);
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets();
        }
        static Material Mat(string name,Color color)
        {
            string path = "Assets/DisappearingHand/Settings/"+name+".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!material) { material = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(material,path); }
            material.SetColor("_BaseColor",color); EditorUtility.SetDirty(material); return material;
        }
        static Renderer Bar(string name,Transform parent,Material material)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.SetParent(parent,false);
            go.transform.localPosition = new Vector3(0,.102f,.07f); go.transform.localScale = new Vector3(.006f,.001f,.42f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            var r = go.GetComponent<Renderer>(); r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off; r.receiveShadows = false; return r;
        }
        [MenuItem("Tools/Disappearing Hand/Validate and build original 2011 Windows")]
        public static void Build()
        {
            Prepare(); OriginalDhtValidation.Run();
            Directory.CreateDirectory("Builds/OriginalWindows");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{ScenePath},
                locationPathName="Builds/OriginalWindows/DHTOriginal2011.exe",target=BuildTarget.StandaloneWindows64});
            if (report.summary.result != BuildResult.Succeeded) throw new InvalidOperationException("Original DHT build failed: "+report.summary.result);
            Debug.Log("ORIGINAL_DHT_BUILD_PASS");
        }
    }
}
