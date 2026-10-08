using System;
using System.IO;
using System.Linq;
using AvatarExperiments.FpsArms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.OpenXR;

namespace DisappearingHand.Editor
{
    public static class ExperimentValidation
    {
        static int checks;
        [MenuItem("Tools/Disappearing Hand/Run validation (no headset)")]
        public static void Run()
        {
            checks=0;
            foreach(bool left in new[]{false,true})foreach(bool control in new[]{false,true})
            {
                var config=new TrialSettings{targetLeft=left,controlCondition=control};
                var p=new TrialProtocol();
                Check(p.Start(config),"Start accepted");Check(!p.Start(config),"Active restart refused");
                config.displacement=.20f;Check(Mathf.Approximately(p.Settings.displacement,.12f),"Settings frozen for trial");
                p.Tick(5);Check(p.Phase==TrialPhase.Drift,"Baseline duration");
                p.Tick(20);Check(Mathf.Abs(p.Shift-(control?0:.06f))<.00001f,"Linear gradual shift");
                Vector3 axis=Quaternion.Euler(0,73,0)*Vector3.right;
                Check(Vector3.Distance(p.Offset(true,axis),-p.Offset(false,axis))<.00001f,"Symmetric table-relative offsets");
                Check(control || Vector3.Dot(p.Offset(true,axis),axis)>0,"Left hand moves inward");
                p.Tick(23.1f);Check(p.Phase==TrialPhase.Search,"Timed hide");
                Check(!p.IsVisible(left)&&p.IsVisible(!left),"Only target hidden");
                Check(p.ConfirmSearch(),"Search confirmation");
                p.Tick(2);Check(p.Phase==TrialPhase.Complete&&p.Shift==0&&p.IsVisible(true)&&p.IsVisible(false),"Both restored");
                p.Start(new TrialSettings());p.Tick(20);p.Abort();
                Check(!p.Active&&p.Shift==0&&p.IsVisible(true)&&p.IsVisible(false),"Abort restores display");
                p.Reset();Check(p.Phase==TrialPhase.Ready&&p.Elapsed==0,"Reset clears timing");
            }
            var skipped=new TrialProtocol();skipped.Start(new TrialSettings());skipped.Tick(100);
            Check(skipped.Phase==TrialPhase.Complete,"Large time-step consumes phases");
            var early=new TrialProtocol();early.Start(new TrialSettings());early.Tick(15);
            float before=early.Shift;early.HideNow();Check(Mathf.Approximately(before,early.Shift),"Early hide freezes attained shift");
            early.Tick(float.NaN);Check(!float.IsNaN(early.PhaseTime),"Invalid delta rejected");
            var sanitized=new TrialSettings{driftSeconds=0,displacement=float.NaN}.Snapshot();
            Check(sanitized.driftSeconds>=1&&!float.IsNaN(sanitized.displacement),"Invalid settings sanitized");
            FpsArmSmokeTests.Run();
            EditorSceneManager.OpenScene(ExperimentBuild.ScenePath);
            var e=UnityEngine.Object.FindAnyObjectByType<DisappearingHandExperiment>();
            Check(e&&e.rig&&e.tableFrame&&e.menu,"Scene references complete");
            Check(e.rig.driveHeadPose&&!e.desktopPreview,"Default scene uses real XR");
            Check(e.GetComponent<PcvrSession>()&&e.GetComponent<PcvrSession>().experiment==e,"PCVR startup owns XR lifetime");
            Check(e.instructionText.material&&e.instructionText.material.shader.name=="AvatarExperiments/URP/WorldSpaceUI","VR text uses explicitly included stereo UI shader");
            Check(!e.rig.GetComponent<AvatarExperiments.FpsArms.ArmLengthCalibration>().enabled,"Gesture ownership explicit");
            var shader=Shader.Find("DisappearingHand/ShiftedHand");
            Check(shader&&shader.isSupported,"Hand shader available");
            Check(!ShaderUtil.GetShaderMessages(shader).Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error),"Shader compiles");
            foreach(var arm in new[]{e.rig.leftArm,e.rig.rightArm})
            {
                Check(arm.skin&&arm.skin.sharedMaterial.shader==shader,"FPS skin uses displacement shader");
                Check(arm.skin.sharedMaterial.GetTexture("_BaseMap"),"Skin texture is bound");
                Check(arm.skin.shadowCastingMode==UnityEngine.Rendering.ShadowCastingMode.Off,"No revealing shadows");
            }
            var xr=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Standalone);
            Check(xr&&xr.renderMode==OpenXRSettings.RenderMode.SinglePassInstanced,"Stereo OpenXR rendering configured");
            RenderChecks(e);
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/validation.txt","PASS: "+checks+" experiment checks; FPS arm package smoke suite (484 poses).\nHeadset hardware not tested.\n");
            Debug.Log("DHT_VALIDATION_PASS: "+checks+" checks");
            // Discard preview-only changes; builds use the saved real-XR scene.
            EditorSceneManager.OpenScene(ExperimentBuild.ScenePath);
        }
        static void RenderChecks(DisappearingHandExperiment e)
        {
            e.desktopPreview=true;e.writeLogs=false;
            var camera=e.rig.head;camera.transform.SetPositionAndRotation(new Vector3(0,1.38f,-.72f),Quaternion.Euler(25,0,0));
            camera.stereoTargetEye=StereoTargetEyeMask.None;
            var bodyHead=new GameObject("Preview body head").transform;
            bodyHead.position=new Vector3(0,1.25f,.08f);e.rig.leftArm.head=e.rig.rightArm.head=bodyHead;
            foreach(var arm in new[]{e.rig.leftArm,e.rig.rightArm})
                AvatarExperiments.FpsArms.TrackedHandsAvatarRig.ApplyControllerPose(arm,new Pose(new Vector3(arm.leftHand?-.2f:.2f,.79f,.38f),Quaternion.identity),Vector3.zero,Vector3.zero,0,0);
            e.AlignTable();e.ApplyVisuals();
            var left=e.rig.leftArm.wristTarget.position;var right=e.rig.rightArm.wristTarget.position;
            Directory.CreateDirectory("Documentation/Previews");
            Render(camera,"Documentation/Previews/ready.png");
            foreach(bool targetLeft in new[]{false,true})
            {
                e.settings.targetLeft=targetLeft;e.StartTrial();e.Protocol.Tick(49);e.ApplyVisuals();
                Check(e.Protocol.Phase==TrialPhase.Search,"Scene reaches search");
                var hidden=targetLeft?e.rig.leftArm:e.rig.rightArm;
                var visible=targetLeft?e.rig.rightArm:e.rig.leftArm;
                var properties=new MaterialPropertyBlock();hidden.skin.GetPropertyBlock(properties);
                Check(properties.GetFloat("_Visible")==0,"Hidden-hand shader discards fragments");
                visible.skin.GetPropertyBlock(properties);Check(properties.GetFloat("_Visible")==1,"Searching hand stays visible");
                Check(e.rig.leftArm.wristTarget.position==left&&e.rig.rightArm.wristTarget.position==right,"Raw targets never displaced");
                e.UpdateLabels();Render(camera,"Documentation/Previews/hidden-"+(targetLeft?"left":"right")+".png");
                e.Abort("validation");e.ApplyVisuals();hidden.skin.GetPropertyBlock(properties);
                Check(properties.GetFloat("_Visible")==1&&properties.GetVector("_Shift").sqrMagnitude<.000001f,"Abort restores material properties");
                e.ResetTrial();e.AlignTable();
            }
            Render(camera,"Documentation/Previews/restored.png");
        }
        static void Render(Camera camera,string path)
        {
            var target=new RenderTexture(1500,1000,24);var old=camera.targetTexture;var active=RenderTexture.active;
            var texture=new Texture2D(1500,1000,TextureFormat.RGB24,false);
            try
            {
                camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1500,1000),0,0);texture.Apply();File.WriteAllBytes(path,texture.EncodeToPNG());
            }
            finally {camera.targetTexture=old;RenderTexture.active=active;UnityEngine.Object.DestroyImmediate(texture);target.Release();UnityEngine.Object.DestroyImmediate(target);}
        }
        static void Check(bool value,string message)
        { if(!value)throw new InvalidOperationException("DHT validation failed: "+message);checks++; }
    }
}
