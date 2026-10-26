using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using AvatarExperiments.FpsArms;
using DisappearingHand.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace TendonHand.Editor
{
    public static class TendonValidation
    {
        static int checks;
        public static void PrepareAndRun() { TendonBuild.Prepare(); Run(); }
        [MenuItem("Tools/Tendon Hand/Validate")]
        public static void Run()
        {
            checks=0;
            foreach (RecoveryView view in Enum.GetValues(typeof(RecoveryView)))
            foreach (bool visual in new[]{false,true})
            foreach (bool left in new[]{false,true})
            {
                var s=new TendonSettings{recovery=view,visualMotion=visual,targetLeft=left};
                var p=new TendonProtocol();Check(p.Start(s),"Trial starts");Check(!p.Start(s),"No restart during trial");
                s.angleDegrees=60;Check(p.Settings.angleDegrees==-25,"Frozen configuration");
                p.Tick(5);Check(p.Phase==TendonPhase.Starting,"Wait for output start");
                p.Tick(2);Check(p.Phase==TendonPhase.Starting && p.Angle==0,"No drift before write completion");
                p.OutputStarted();p.Tick(7.5f);Check(Mathf.Abs(p.Angle-(visual?-12.5f:0))<.001f,"Visual induction progression");
                p.Tick(7.5f);Check(p.Phase==TendonPhase.Stopping,"Wait for stop write");
                p.Tick(.1f);Check(p.Phase==TendonPhase.Stopping,"No automatic recovery across stop barrier");
                p.OutputStopped();float angle=p.Angle;p.Tick(10);
                Check(p.Angle==angle,"No automatic return animation during observation");
                Check(p.TargetVisible==(view!=RecoveryView.Hidden),"Condition visibility");
                Check(p.UsesLivePose==(view==RecoveryView.Veridical),"Veridical condition follows real pose");
                p.Tick(50);Check(p.Phase==TendonPhase.Complete && p.UsesLivePose,"Observation ends and restores");
            }
            var early=new TendonProtocol();early.Start(new TendonSettings());early.Tick(5);early.OutputStarted();early.Tick(3);
            float partial=early.Angle;early.StopInduction();early.OutputStopped();Check(early.Angle==partial,"Early stop keeps attained angle");
            early.Abort();Check(!early.Active&&early.TargetVisible&&early.UsesLivePose,"Abort clears visual manipulation");
            var safe=new TendonSettings{amplitude=float.NaN,inductionSeconds=-1,channel=100}.Snapshot();
            Check(!float.IsNaN(safe.amplitude)&&safe.inductionSeconds>=.5f&&safe.channel==15,"Malformed settings bounded");
            var baseline=new Pose(new Vector3(1,2,3),Quaternion.Euler(10,50,20));var pivot=baseline.position-Vector3.forward*.25f;
            var rendered=TendonProtocol.DisplayPose(baseline,pivot,Vector3.right,-30);
            Check(Mathf.Abs(Vector3.Distance(rendered.position,pivot)-.25f)<.00001f,"Rotation preserves joint radius");
            Check(TendonProtocol.DisplayPose(baseline,baseline.position,Vector3.right,-30).position==baseline.position,"Wrist pivot preserves wrist location");
            OutputChecks();
            // Regression suite for the shared FPS arm, original scene and modified shader.
            ExperimentValidation.Run();
            EditorSceneManager.OpenScene(TendonBuild.ScenePath);
            var e=UnityEngine.Object.FindAnyObjectByType<TendonHandExperiment>();
            Check(e&&e.rig&&e.menu&&e.GetComponent<DisappearingHand.PcvrSession>().tendonExperiment==e,"New scene wired");
            Check(e.dryRun&&!e.desktopPreview,"Scene defaults to PCVR with simulated output");
            Check(Esp32Device.ClientType!=null,"Upstream ESP32 runtime installed");
            RenderStateChecks(e);
            Directory.CreateDirectory("Logs");File.WriteAllText("Logs/tendon-validation.txt","PASS: "+checks+" tendon checks, plus original DHT/FPS-arm regression suite.\nNo physical hardware tested.\n");
            Debug.Log("TENDON_VALIDATION_PASS: "+checks);
            EditorSceneManager.OpenScene(TendonBuild.ScenePath);
        }
        static void OutputChecks()
        {
            var device=new FakeDevice();
            using(var output=new TendonOutput(()=>device))
            {
                Wait(()=>output.Ready,"Output connects");
                output.Start(new TendonSettings());Wait(()=>output.Active,"Output starts");
                output.Stop();Wait(()=>!output.Active,"Output stops");
                output.Start(new TendonSettings());output.Stop();Thread.Sleep(100);
                Check(!output.Active,"Stop cancels a pending start");
                output.Heartbeat();output.Start(new TendonSettings());Wait(()=>output.Active,"Watchdog test starts");
                Wait(()=>output.Failure!=null,"Worker stops when host heartbeat expires",1.5);
                Wait(()=>!output.Active,"Fault mutes output");
            }
            Check(device.mutes>=3,"Muting on lifecycle transitions");
            var broken=new FakeDevice{throwOnStart=true};
            using(var output=new TendonOutput(()=>broken))
            {
                Wait(()=>output.Ready,"Fault device connects");output.Start(new TendonSettings());
                Wait(()=>output.Failure!=null,"Write failure reported");Wait(()=>!output.Ready,"Write failure cannot remain ready");
            }
            Check(broken.mutes>=2,"Failed start still attempts mute");
        }
        static void RenderStateChecks(TendonHandExperiment e)
        {
            e.desktopPreview=true;e.writeLogs=false;e.Initialize();e.rig.enabled=false;
            e.rig.head.transform.SetPositionAndRotation(new Vector3(0,1.38f,-.72f),Quaternion.Euler(25,0,0));
            var head=new GameObject("Tendon test head").transform;head.position=new Vector3(0,1.25f,.08f);
            foreach(var arm in new[]{e.rig.leftArm,e.rig.rightArm})
            {
                arm.head=head;TrackedHandsAvatarRig.ApplyControllerPose(arm,new Pose(new Vector3(arm.leftHand?-.2f:.2f,.79f,.38f),Quaternion.identity),Vector3.zero,Vector3.zero,0,0);
            }
            e.Connect();Wait(()=>e.OutputReady,"Scene output ready");e.PollOutput();
            foreach(RecoveryView view in Enum.GetValues(typeof(RecoveryView)))
            {
                e.Align();e.settings.recovery=view;e.settings.vibration=false;e.StartTrial();
                var raw=e.rig.rightArm.wristTarget.position;
                e.Protocol.Tick(5);e.Protocol.Tick(15);e.ApplyVisuals();
                Check(e.Protocol.Phase==TendonPhase.Recovery,"Scene recovery starts");
                Check(e.rig.rightArm.wristTarget.position==raw,"Raw target unmodified");
                var props=new MaterialPropertyBlock();e.rig.rightArm.skin.GetPropertyBlock(props);
                Check(props.GetFloat("_Visible")== (view==RecoveryView.Hidden?0:1),"Scene shader visibility");
                Check(view==RecoveryView.Veridical ? Vector3.Distance(e.DisplayedPose.position,raw)<.00001f : Vector3.Distance(e.DisplayedPose.position,raw)>.05f,"Scene visual position");
                var fixedPose=e.DisplayedPose;
                e.rig.rightArm.wristTarget.position+=Vector3.right*.001f;e.ApplyVisuals();
                if(view==RecoveryView.HoldDisplaced)Check(e.DisplayedPose.position==fixedPose.position,"Hold stays at fixed world position");
                e.rig.rightArm.wristTarget.position=raw;e.ResetTrial();e.PollOutput();
            }
            e.Disconnect();
        }
        sealed class FakeDevice : ITendonDevice
        {
            public int mutes;public bool throwOnStart;
            public void Start(int channel,float hz,float amplitude) { if(throwOnStart)throw new IOException("injected write fault"); }
            public void Mute() { Interlocked.Increment(ref mutes); }
            public void Query() { }
            public IEnumerable<string> Read() { yield return "595 state=fake"; }
            public void Dispose() { }
        }
        static void Wait(Func<bool> condition,string message,double timeout=1)
        {
            double end=TendonOutput.Now+timeout;
            while(!condition()&&TendonOutput.Now<end)Thread.Sleep(5);
            Check(condition(),message);
        }
        static void Check(bool value,string message) { if(!value)throw new InvalidOperationException("Tendon validation: "+message);checks++; }
    }
}
