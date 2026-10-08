using System;
using System.IO;
using System.Linq;
using AvatarExperiments.FpsArms;
using AvatarExperiments.FpsArms.Editor;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.XR.OpenXR.Features;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
using Object = UnityEngine.Object;

namespace DisappearingHand.Editor
{
    public static class ExperimentBuild
    {
        public const string ScenePath = "Assets/Scenes/DisappearingHandTrickVR.unity";
        const string SettingsPath = "Assets/DisappearingHand/Settings/";
        static Font font;
        static Material surface, dark, accent;

        [MenuItem("Tools/Disappearing Hand/Create experiment scene")]
        public static void Prepare()
        {
            Directory.CreateDirectory(SettingsPath); Directory.CreateDirectory("Assets/Scenes");
            ConfigureProject();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=new Color(.55f,.59f,.66f);
            var key=new GameObject("Soft key light").AddComponent<Light>(); key.type=LightType.Directional;
            key.transform.rotation=Quaternion.Euler(45,-30,0);key.intensity=1.15f;key.shadows=LightShadows.None;
            surface=Material("Table",new Color(.64f,.70f,.73f));
            dark=Material("Room",new Color(.035f,.060f,.085f));
            accent=Material("Accent",new Color(.10f,.85f,.72f),true);
            Cube("Floor",null,new Vector3(0,-.045f,0),new Vector3(8,.08f,8),dark);
            Cube("Backdrop",null,new Vector3(0,1.3f,3.3f),new Vector3(8,2.6f,.08f),dark);
            var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/FPSHand/fp_male_hand.fbx");
            if(!model)throw new InvalidOperationException("Locally licensed FPS hand model is missing.");
            var profile=AssetDatabase.LoadAssetAtPath<FpsArmModelProfile>(SettingsPath+"FPSArmProfile.asset");
            if(!profile) { profile=FpsArmSetupWindow.CreateFpProfile(model);AssetDatabase.CreateAsset(profile,SettingsPath+"FPSArmProfile.asset"); }
            var root=FpsArmSetupWindow.Create(profile,null,"disappearing-hand-fps-v1");
            root.name="Disappearing Hand Experiment";
            var rig=root.GetComponent<TrackedHandsAvatarRig>();
            rig.head.name="XR Camera"; rig.head.tag="MainCamera";
            rig.head.transform.SetPositionAndRotation(new Vector3(0,1.28f,-.18f),Quaternion.Euler(26,0,0));
            rig.head.nearClipPlane=.02f;rig.head.farClipPlane=30;rig.head.fieldOfView=65;
            rig.head.clearFlags=CameraClearFlags.SolidColor;rig.head.backgroundColor=new Color(.035f,.06f,.085f);
            rig.head.stereoTargetEye=StereoTargetEyeMask.Both;
            rig.head.GetUniversalAdditionalCameraData().allowXRRendering=true;
            rig.head.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            rig.head.gameObject.AddComponent<AudioListener>();
            root.GetComponent<ArmLengthCalibration>().enabled=false;
            var skin=AssetDatabase.LoadAssetAtPath<Material>(SettingsPath+"FPSHand.mat");
            if(!skin)
            {
                var shader=Shader.Find("DisappearingHand/ShiftedHand");
                if(!shader)throw new InvalidOperationException("Shifted-hand shader is missing.");
                skin=new Material(shader);skin.mainTexture=AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/FPSHand/fp_male_hand_color_var_01_D.tga");
                // This shader's texture is _BaseMap, independent of Material.mainTexture conventions.
                skin.SetTexture("_BaseMap",skin.mainTexture);skin.SetFloat("_CuffLength",.10f);
                AssetDatabase.CreateAsset(skin,SettingsPath+"FPSHand.mat");
            }
            foreach(var arm in new[]{rig.leftArm,rig.rightArm})
            {
                arm.skin.sharedMaterials=Enumerable.Repeat(skin,arm.skin.sharedMaterials.Length).ToArray();
                arm.hideWhenUntracked=true;arm.skin.shadowCastingMode=ShadowCastingMode.Off;
                TrackedHandsAvatarRig.ApplyControllerPose(arm,new Pose(new Vector3(arm.leftHand?-.20f:.20f,.79f,.38f),Quaternion.identity),Vector3.zero,Vector3.zero,0,0);
            }
            var experiment=root.AddComponent<DisappearingHandExperiment>();experiment.rig=rig;
            experiment.tableFrame=new GameObject("Calibrated Table Frame").transform;
            experiment.tableFrame.position=new Vector3(0,.75f,.38f);
            experiment.tableTop=Cube("Physical table reference",experiment.tableFrame,new Vector3(0,-.025f,0),new Vector3(1.5f,.05f,.85f),surface).transform;
            foreach(float x in new[]{-.65f,.65f}) foreach(float z in new[]{-.32f,.32f})
                Cube("Table leg",experiment.tableFrame,new Vector3(x,-.40f,z),new Vector3(.035f,.75f,.035f),dark);
            experiment.leftMarker=Ring("Left wrist home",experiment.tableFrame,new Vector3(-.20f,.004f,0));
            experiment.rightMarker=Ring("Right wrist home",experiment.tableFrame,new Vector3(.20f,.004f,0));
            CreateInterface(experiment);
            root.AddComponent<PcvrSession>().experiment=experiment;
            root.AddComponent<PlayerSmokeCheck>().experiment=experiment;
            EditorSceneManager.SaveScene(scene,ScenePath);
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets();Debug.Log("DHT_SCENE_READY: "+ScenePath);
        }
        static void ConfigureProject()
        {
            PlayerSettings.companyName="AmemiyaLab";PlayerSettings.productName="DisappearingHandTrick";
            PlayerSettings.runInBackground=true;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;
            PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=960;
            PlayerSettings.colorSpace=ColorSpace.Linear;
            PlayerSettings.SetScriptingBackend(NamedBuildTarget.Standalone,ScriptingImplementation.Mono2x);
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
            var data=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(SettingsPath+"Renderer.asset");
            if(!data){data=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(data,SettingsPath+"Renderer.asset");}
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(SettingsPath+"Pipeline.asset");
            if(!pipeline){pipeline=UniversalRenderPipelineAsset.Create(data);AssetDatabase.CreateAsset(pipeline,SettingsPath+"Pipeline.asset");}
            pipeline.msaaSampleCount=4;pipeline.renderScale=1;pipeline.supportsHDR=false;
            pipeline.supportsCameraDepthTexture=false;pipeline.supportsCameraOpaqueTexture=false;
            GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;
            // Standalone builds need explicit references to the IMGUI fallback shaders.
            var graphics=new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")[0]);
            var included=graphics.FindProperty("m_AlwaysIncludedShaders");
            var guiShaders=ShaderUtil.GetAllShaderInfo().Where(s=>s.name.Contains("Internal-GUI")||s.name=="UI/Default")
                .Select(s=>Shader.Find(s.name)).Where(s=>s).ToArray();
            foreach(var shader in guiShaders)
            {
                bool exists=false;
                for(int i=0;i<included.arraySize;i++)exists|=included.GetArrayElementAtIndex(i).objectReferenceValue==shader;
                if(!exists){int i=included.arraySize;included.InsertArrayElementAtIndex(i);included.GetArrayElementAtIndex(i).objectReferenceValue=shader;}
            }
            graphics.ApplyModifiedPropertiesWithoutUndo();
            QualitySettings.vSyncCount=0;QualitySettings.shadows=UnityEngine.ShadowQuality.Disable;
            var general=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if(!general||!general.Manager)throw new InvalidOperationException("Standalone XR settings missing.");
            // PcvrSession starts XR on demand; --desktop never initializes a headset runtime.
            general.InitManagerOnStart=false;general.Manager.automaticLoading=false;general.Manager.automaticRunning=false;
            if(!XRPackageMetadataStore.AssignLoader(general.Manager,typeof(OpenXRLoader).FullName,BuildTargetGroup.Standalone))throw new InvalidOperationException("Cannot assign OpenXR loader.");
            FeatureHelpers.RefreshFeatures(BuildTargetGroup.Standalone);
            var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            string[] enabled={"HandTracking","OculusTouchControllerProfile","ValveIndexControllerProfile","HTCViveControllerProfile","KHRSimpleControllerProfile","MicrosoftMotionControllerProfile"};
            foreach(var feature in xr.GetFeatures<OpenXRFeature>())
            { if(!feature)continue;feature.enabled=enabled.Contains(feature.GetType().Name);EditorUtility.SetDirty(feature); }
            foreach(string name in enabled)
                if(!xr.GetFeatures<OpenXRFeature>().Any(f=>f&&f.enabled&&f.GetType().Name==name))throw new InvalidOperationException("Missing OpenXR feature: "+name);
            xr.renderMode=OpenXRSettings.RenderMode.SinglePassInstanced;
            EditorUtility.SetDirty(xr);EditorUtility.SetDirty(general);EditorUtility.SetDirty(general.Manager);
            EditorUtility.SetDirty(data);EditorUtility.SetDirty(pipeline);AssetDatabase.SaveAssets();
        }
        static Material Material(string name,Color color,bool unlit=false)
        {
            var path=SettingsPath+name+".mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(!material){material=new Material(Shader.Find(unlit?"Universal Render Pipeline/Unlit":"Universal Render Pipeline/Lit"));AssetDatabase.CreateAsset(material,path);}
            material.SetColor("_BaseColor",color);material.SetFloat("_Smoothness",.18f);return material;
        }
        static GameObject Cube(string name,Transform parent,Vector3 position,Vector3 size,Material material)
        {
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(parent,false);
            go.transform.localPosition=position;go.transform.localScale=size;go.GetComponent<Renderer>().sharedMaterial=material;
            Object.DestroyImmediate(go.GetComponent<Collider>());return go;
        }
        static Transform Ring(string name,Transform parent,Vector3 local)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);go.transform.localPosition=local;
            var line=go.AddComponent<LineRenderer>();line.useWorldSpace=false;line.loop=true;
            line.positionCount=64;line.widthMultiplier=.0025f;line.sharedMaterial=accent;
            line.shadowCastingMode=ShadowCastingMode.Off;
            for(int i=0;i<64;i++){float a=i*Mathf.PI*2/64;line.SetPosition(i,new Vector3(Mathf.Cos(a)*.052f,0,Mathf.Sin(a)*.052f));}
            return go.transform;
        }
        static void CreateInterface(DisappearingHandExperiment e)
        {
            font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var canvas=new GameObject("Experiment UI",typeof(RectTransform),typeof(Canvas));
            canvas.transform.SetParent(e.tableFrame,false);canvas.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            canvas.GetComponent<Canvas>().worldCamera=e.rig.head;canvas.transform.localScale=Vector3.one*.001f;
            canvas.transform.localPosition=new Vector3(0,.45f,.57f);
            var panel=Rect("Menu",canvas.transform,new Vector2(0,75),new Vector2(900,365));
            panel.gameObject.AddComponent<Image>().color=new Color(.025f,.045f,.065f,.97f);e.menu=panel.gameObject;
            Text("Title",panel,new Vector2(0,140),new Vector2(860,45),"DISAPPEARING HAND TRICK",29,new Color(.18f,.95f,.81f));
            e.statusText=Text("Status",panel,new Vector2(0,85),new Vector2(850,70),"FPS ARMS  /  PCVR",22,Color.white);
            var buttons=new System.Collections.Generic.List<Button>();
            buttons.Add(Button("ALIGN TABLE",panel,-295,10,e.AlignTable));
            buttons.Add(Button("START",panel,0,10,e.StartTrial));
            buttons.Add(Button("RESET",panel,295,10,e.ResetTrial));
            buttons.Add(Button("LEFT / RIGHT",panel,-295,-55,e.ToggleTarget));
            buttons.Add(Button("DRIFT / CONTROL",panel,0,-55,e.ToggleControl));
            buttons.Add(Button("CONFIRM SEARCH",panel,295,-55,e.ConfirmSearch));
            buttons.Add(FloatButton("SHIFT -2 cm",panel,-295,-120,e.ChangeShift,-.02f));
            buttons.Add(FloatButton("SHIFT +2 cm",panel,0,-120,e.ChangeShift,.02f));
            buttons.Add(Button("HIDE MENU",panel,295,-120,e.ToggleMenu));
            e.instructionText=Text("Instructions",canvas.transform,new Vector2(0,-195),new Vector2(1000,135),"Rest both hands on the table.\nAlign the table, then start.",29,new Color(.84f,.92f,.95f));
            var cursor=Rect("Pointer",panel,Vector2.zero,new Vector2(12,12));cursor.gameObject.AddComponent<Image>().color=new Color(1,.8f,.25f);
            var pointer=e.gameObject.AddComponent<ExperimentMenuPointer>();pointer.experiment=e;pointer.panel=panel;pointer.buttons=buttons.ToArray();pointer.cursor=cursor;
            var uiMaterial=AssetDatabase.LoadAssetAtPath<Material>(SettingsPath+"WorldSpaceUI.mat");
            if(!uiMaterial)
            {
                uiMaterial=new Material(Shader.Find("AvatarExperiments/URP/WorldSpaceUI"));
                AssetDatabase.CreateAsset(uiMaterial,SettingsPath+"WorldSpaceUI.mat");
            }
            foreach(var graphic in canvas.GetComponentsInChildren<Graphic>(true))graphic.material=uiMaterial;
        }
        static RectTransform Rect(string name,Transform parent,Vector2 position,Vector2 size)
        {
            var rect=new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>();rect.SetParent(parent,false);
            rect.sizeDelta=size;rect.anchoredPosition=position;return rect;
        }
        static Text Text(string name,Transform parent,Vector2 position,Vector2 size,string value,int fontSize,Color color)
        {
            var text=Rect(name,parent,position,size).gameObject.AddComponent<Text>();text.font=font;text.text=value;text.fontSize=fontSize;
            text.color=color;text.alignment=TextAnchor.MiddleCenter;text.raycastTarget=false;text.horizontalOverflow=HorizontalWrapMode.Wrap;return text;
        }
        static Button Button(string label,Transform parent,float x,float y,UnityAction action)
        {
            var rect=Rect(label,parent,new Vector2(x,y),new Vector2(275,52));var image=rect.gameObject.AddComponent<Image>();
            image.color=new Color(.12f,.25f,.30f);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
            Text("Label",rect,Vector2.zero,rect.sizeDelta,label,21,Color.white);UnityEventTools.AddPersistentListener(button.onClick,action);return button;
        }
        static Button FloatButton(string label,Transform parent,float x,float y,UnityAction<float> action,float value)
        {
            var rect=Rect(label,parent,new Vector2(x,y),new Vector2(275,52));var image=rect.gameObject.AddComponent<Image>();
            image.color=new Color(.12f,.25f,.30f);var button=rect.gameObject.AddComponent<Button>();button.targetGraphic=image;
            Text("Label",rect,Vector2.zero,rect.sizeDelta,label,21,Color.white);UnityEventTools.AddFloatPersistentListener(button.onClick,action,value);return button;
        }
        [MenuItem("Tools/Disappearing Hand/Validate and build Windows PCVR")]
        public static void Build()
        {
            Prepare();ExperimentValidation.Run();
            Directory.CreateDirectory("Builds/Windows");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {scenes=new[]{ScenePath},locationPathName="Builds/Windows/DisappearingHandTrick.exe",target=BuildTarget.StandaloneWindows64});
            if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Build failed: "+report.summary.result);
            Debug.Log("DHT_BUILD_PASS: "+Path.GetFullPath("Builds/Windows/DisappearingHandTrick.exe"));
        }
    }
}
