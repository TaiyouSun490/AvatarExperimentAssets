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

namespace MarbleHand.Editor
{
    public static class MarbleBuild
    {
        public const string ScenePath = "Assets/Scenes/MarbleHandVR.unity";
        const string SettingsPath = "Assets/MarbleHand/Settings/";
        [MenuItem("Tools/Marble Hand/Create experiment scene")]
        public static void Prepare()
        {
            Directory.CreateDirectory(SettingsPath); Directory.CreateDirectory("Assets/Scenes");
            ConfigureProject();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            RenderSettings.ambientMode = AmbientMode.Flat; RenderSettings.ambientLight = new Color(.55f,.59f,.66f);
            var key = new GameObject("Soft key light").AddComponent<Light>(); key.type = LightType.Directional;
            key.transform.rotation = Quaternion.Euler(45,-30,0); key.intensity = 1.15f; key.shadows = LightShadows.None;
            var table = Material("Table", new Color(.19f,.25f,.27f));
            var room = Material("Room", new Color(.035f,.060f,.085f));
            var wood = Material("MalletWood", new Color(.35f,.18f,.07f));
            var head = Material("MalletHead", new Color(.58f,.38f,.19f));
            Cube("Floor", null, new Vector3(0,-.045f,0), new Vector3(8,.08f,8),room);
            Cube("Backdrop", null, new Vector3(0,1.3f,3.3f),new Vector3(8,2.6f,.08f),room);
            var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ThirdParty/FPSHand/fp_male_hand.fbx");
            if (!model) throw new InvalidOperationException("Import the locally licensed FPS hand model first.");
            var profile = AssetDatabase.LoadAssetAtPath<FpsArmModelProfile>(SettingsPath+"FPSArmProfile.asset");
            if (!profile) { profile=FpsArmSetupWindow.CreateFpProfile(model); AssetDatabase.CreateAsset(profile,SettingsPath+"FPSArmProfile.asset"); }
            var root = FpsArmSetupWindow.Create(profile,null,"marble-hand-fps-v1"); root.name = "Marble Hand Experiment";
            var rig = root.GetComponent<TrackedHandsAvatarRig>();
            rig.head.name="XR Camera"; rig.head.tag="MainCamera";
            rig.head.transform.SetPositionAndRotation(new Vector3(0,1.28f,-.18f),Quaternion.Euler(26,0,0));
            rig.head.nearClipPlane=.02f; rig.head.farClipPlane=30; rig.head.fieldOfView=65;
            rig.head.clearFlags=CameraClearFlags.SolidColor; rig.head.backgroundColor=new Color(.035f,.06f,.085f);
            rig.head.stereoTargetEye=StereoTargetEyeMask.Both;
            rig.head.GetUniversalAdditionalCameraData().allowXRRendering=true;
            rig.head.GetUniversalAdditionalCameraData().renderPostProcessing=false;
            rig.head.gameObject.AddComponent<AudioListener>(); root.GetComponent<ArmLengthCalibration>().enabled=false;
            var skin = AssetDatabase.LoadAssetAtPath<Material>(SettingsPath+"MarbleHand.mat");
            if (!skin) { skin=new Material(Shader.Find("MarbleHand/SkinToStone")); AssetDatabase.CreateAsset(skin,SettingsPath+"MarbleHand.mat"); }
            skin.SetTexture("_BaseMap",AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/ThirdParty/FPSHand/fp_male_hand_color_var_01_D.tga"));
            skin.SetFloat("_CuffLength",.60f); skin.SetFloat("_Marble",0);
            foreach(var arm in new[]{rig.leftArm,rig.rightArm})
            {
                arm.skin.sharedMaterials=Enumerable.Repeat(skin,arm.skin.sharedMaterials.Length).ToArray();
                arm.hideWhenUntracked=true; arm.skin.shadowCastingMode=ShadowCastingMode.Off;
                TrackedHandsAvatarRig.ApplyControllerPose(arm,new Pose(new Vector3(arm.leftHand?-.20f:.20f,.79f,.38f),Quaternion.identity),Vector3.zero,Vector3.zero,0,0);
            }
            var e = root.AddComponent<MarbleExperiment>(); e.rig=rig;
            e.tableFrame = new GameObject("Calibrated Table Frame").transform; e.tableFrame.position=new Vector3(0,.75f,.38f);
            Cube("Physical table reference",e.tableFrame,new Vector3(0,-.025f,0),new Vector3(1.5f,.05f,.85f),table);
            foreach(float x in new[]{-.65f,.65f}) foreach(float z in new[]{-.32f,.32f})
                Cube("Table leg",e.tableFrame,new Vector3(x,-.40f,z),new Vector3(.035f,.75f,.035f),room);
            e.hammer = new GameObject("Contact mallet").transform;
            Cube("Wood handle",e.hammer,new Vector3(0,.13f,0),new Vector3(.012f,.24f,.012f),wood);
            Cube("Mallet head",e.hammer,Vector3.zero,new Vector3(.07f,.036f,.036f),head);
            e.hammer.gameObject.SetActive(false);
            e.stoneClips = new AudioClip[2];
            for(int i=0;i<2;i++)
            {
                string path="Assets/MarbleHand/Audio/MarbleTap"+(i+1)+".wav";
                var importer=(AudioImporter)AssetImporter.GetAtPath(path);
                var a=importer.defaultSampleSettings; a.loadType=AudioClipLoadType.DecompressOnLoad; a.compressionFormat=AudioCompressionFormat.PCM;
                a.sampleRateSetting=AudioSampleRateSetting.PreserveSampleRate; a.preloadAudioData=true;
                importer.defaultSampleSettings=a; importer.forceToMono=true; importer.loadInBackground=false; importer.SaveAndReimport();
                e.stoneClips[i]=AssetDatabase.LoadAssetAtPath<AudioClip>(path);
            }
            var canvas=new GameObject("Participant instructions",typeof(RectTransform),typeof(Canvas));
            canvas.transform.SetParent(e.tableFrame,false); canvas.GetComponent<Canvas>().renderMode=RenderMode.WorldSpace;
            canvas.GetComponent<Canvas>().worldCamera=rig.head; canvas.transform.localScale=Vector3.one*.001f;
            canvas.transform.localPosition=new Vector3(0,.45f,.67f);
            var text=new GameObject("Status",typeof(RectTransform),typeof(Text)).GetComponent<Text>(); text.transform.SetParent(canvas.transform,false);
            text.rectTransform.sizeDelta=new Vector2(1000,140); text.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize=29; text.color=new Color(.84f,.92f,.95f); text.alignment=TextAnchor.MiddleCenter;
            text.text="MARBLE HAND / VR"; text.raycastTarget=false;
            text.material=AssetDatabase.LoadAssetAtPath<Material>(SettingsPath+"WorldSpaceUI.mat"); e.worldStatus=text;
            root.AddComponent<PcvrSession>().experiment=e; root.AddComponent<MarblePlayerSmoke>().experiment=e;
            EditorSceneManager.SaveScene(scene,ScenePath); EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(ScenePath,true)};
            AssetDatabase.SaveAssets(); Debug.Log("MARBLE_SCENE_READY: "+ScenePath);
        }
        static void ConfigureProject()
        {
            PlayerSettings.companyName="AmemiyaLab";PlayerSettings.productName="MarbleHandIllusion";
            PlayerSettings.bundleVersion="0.1.0";
            PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Standalone,"com.amemiyalab.marblehandillusion");
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
            // A fresh checkout may have no per-target settings asset yet.
            if(!EditorBuildSettings.TryGetConfigObject<UnityEditor.XR.Management.XRGeneralSettingsPerBuildTarget>(UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey,out var targets)||!targets)
            {
                const string path="Assets/XR/XRGeneralSettingsPerBuildTarget.asset";
                targets=AssetDatabase.LoadAssetAtPath<XRGeneralSettingsPerBuildTarget>(path);
                if(!targets)
                {
                    Directory.CreateDirectory("Assets/XR");
                    targets=ScriptableObject.CreateInstance<XRGeneralSettingsPerBuildTarget>();
                    AssetDatabase.CreateAsset(targets,path);
                }
                EditorBuildSettings.AddConfigObject(UnityEngine.XR.Management.XRGeneralSettings.k_SettingsKey,targets,true);
            }
            if(!targets.HasSettingsForBuildTarget(BuildTargetGroup.Standalone)) targets.CreateDefaultSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if(!targets.HasManagerSettingsForBuildTarget(BuildTargetGroup.Standalone)) targets.CreateDefaultManagerSettingsForBuildTarget(BuildTargetGroup.Standalone);
            EditorUtility.SetDirty(targets);
            var general=XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Standalone);
            if(!general||!general.Manager)throw new InvalidOperationException("Standalone XR settings missing.");
            // PcvrSession starts subsystems on demand; desktop simulation works without a headset.
            // Unity's native OpenXR preinitialization can still probe the runtime before scene startup.
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

        [MenuItem("Tools/Marble Hand/Validate and build Windows PCVR")]
        public static void Build()
        {
            Prepare(); MarbleValidation.Run();
            Directory.CreateDirectory("Builds/Windows");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions { scenes=new[]{ScenePath}, locationPathName="Builds/Windows/MarbleHandIllusion.exe", target=BuildTarget.StandaloneWindows64 });
            if(report.summary.result!=BuildResult.Succeeded) throw new InvalidOperationException("Build failed: "+report.summary.result);
            Debug.Log("MARBLE_BUILD_PASS: "+Path.GetFullPath("Builds/Windows/MarbleHandIllusion.exe"));
        }
    }
}
