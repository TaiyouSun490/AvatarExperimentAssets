using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AvatarExperiments.VirtualHand.Editor
{
    [InitializeOnLoad]
    public static class VirtualHandVerification
    {
        const string Key="AvatarVirtualHandVerification";
        static readonly List<string> results=new List<string>();
        static VirtualHandVerification(){EditorApplication.playModeStateChanged+=Changed;}
        [MenuItem("Tools/Avatar Experiments/Virtual Hand/Run isolated Play Mode verification")]
        public static void Run()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("Stop Play before verification.");
            if(!Application.isBatchMode&&!UnityEditor.SceneManagement.EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            var experiment=VirtualHandSceneSetup.Create(false,"Assets/VirtualHandVerification");
            experiment.GetComponentInChildren<TabletopHeadTracking>().enabled=false;experiment.trackedBrush.automaticSampling=false;
            // The generated scene remains a VR scene; tests inject poses and mark their data as simulated in Play only.
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(experiment.gameObject.scene);
            SessionState.SetBool(Key,true);EditorApplication.EnterPlaymode();
        }
        static void Changed(PlayModeStateChange change)
        {
            if(!SessionState.GetBool(Key,false))return;
            if(change==PlayModeStateChange.EnteredPlayMode)EditorApplication.delayCall+=Exercise;
            if(change==PlayModeStateChange.EnteredEditMode)
            {SessionState.SetBool(Key,false);if(Application.isBatchMode)EditorApplication.Exit(SessionState.GetBool(Key+".Pass",false)?0:1);}
        }
        static void Check(bool value,string message){if(!value)throw new Exception(message);results.Add(message);}
        static void Exercise()
        {
            results.Clear();bool passed=false;
            try{Verify();passed=true;}catch(Exception error){results.Add(error.ToString());Debug.LogException(error);}
            File.WriteAllLines("Library/VirtualHandVerification.txt",new[]{passed?"PASS":"FAIL",DateTime.UtcNow.ToString("O")}.Concat(results));
            Debug.Log("VIRTUAL_HAND_VERIFICATION "+(passed?"PASS":"FAIL"));SessionState.SetBool(Key+".Pass",passed);EditorApplication.ExitPlaymode();
        }
        static void Verify()
        {
            var protocol=new StudyProtocol();protocol.repetitions=2;protocol.counterbalanceIndex=1;
            Check(protocol.Schedule().SequenceEqual(new[]{1,0,1,0}),"Counterbalance rotates conditions without changing repetition counts");
            protocol.order=ConditionOrder.SeededShuffle;Check(protocol.Schedule().SequenceEqual(protocol.Schedule()),"Seeded schedule is reproducible");
            var invalid=new TrialSettings{vibration=VibrationCondition.MeltDelayed,vibrationDelay=2,observation=1};
            bool rejected=false;try{invalid.ValidateTracked();}catch(ArgumentException){rejected=true;}Check(rejected,"Delayed vibration tail cannot be truncated");
            var reference=new Vector3(.3f,.75f,.4f);
            foreach(var mode in new[]{DriftMethod.InvisibleControllerPointing,DriftMethod.MarkerAdjustment,DriftMethod.RandomizedRuler})
            {
                var settings=new DriftSettings{method=mode};
                var before=new DriftMeasurement(settings,reference,8);var after=new DriftMeasurement(settings,reference,11);
                for(int i=0;i<3;i++)
                {
                    if(mode==DriftMethod.MarkerAdjustment){before.Adjust(.02f-before.Coordinate);before.ConfirmMarker(i);after.Adjust(.05f-after.Coordinate);after.ConfirmMarker(i);}
                    else if(mode==DriftMethod.RandomizedRuler)
                    {before.ConfirmRuler(before.RulerZero+(.02f-before.RulerShift)*100,i);after.ConfirmRuler(after.RulerZero+(.05f-after.RulerShift)*100,i);}
                    else {before.Record(reference+Vector3.right*.02f,i);after.Record(reference+Vector3.right*.05f,i);}
                }
                Check(Mathf.Abs(DriftMeasurement.Drift(before.Estimate(),after.Estimate())-.03f)<1e-5,mode+": known +30 mm drift and independent baseline recovered");
                Check(before.Estimate().responses.Count==3,mode+": raw repetitions preserved");
                if(mode==DriftMethod.RandomizedRuler)foreach(var response in before.Estimate().responses)
                    Check(Mathf.Abs((response.rulerReportedNumber-response.rulerZero)*.01f+response.rulerShiftMetres-response.coordinateMetres)<1e-5,
                        "Ruler raw number, shifted origin and physical response reconstruct exactly");
            }
            var staircase=new DriftMeasurement(new DriftSettings{method=DriftMethod.AlternatingStaircase},reference,4);
            for(int i=0;!staircase.Complete;i++)staircase.Judge(staircase.Coordinate>.045f,i);
            Check(staircase.Estimate().valid&&Mathf.Abs(staircase.Estimate().coordinateMetres-.045f)<.011f,"Alternating staircase converges on a synthetic 45 mm threshold");
            var biased=new DriftMeasurement(new DriftSettings{method=DriftMethod.AlternatingStaircase},reference,4);
            for(int i=0;!biased.Complete;i++)biased.Judge(true,i);
            Check(!biased.Estimate().valid,"One-sided/boundary staircase is invalid, not a zero-drift result");
            var e=UnityEngine.Object.FindFirstObjectByType<TabletopExperiment>();var brush=e.trackedBrush;
            e.UseRealtimeClock=false;e.desktopPreview=true;brush.automaticSampling=false;e.writeLog=true;
            e.LogDirectoryOverride=Path.GetFullPath("Library/VirtualHandSyntheticLogs");
            foreach(var root in e.gameObject.scene.GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))
                Check(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)==0,"No missing script: "+t.name);
            brush.tipOffset=Vector3.zero;brush.ConfirmMeasuredTip();double now=100;
            var mapping=Quaternion.Euler(0,20,0);var shift=new Vector3(.12f,0,.08f);
            var targets=brush.visualLandmarks.Select(t=>t.position).ToArray();
            for(int i=0;i<3;i++)
            {var real=Quaternion.Inverse(mapping)*(targets[i]-shift);brush.Sample(now+=.01,new Pose(brush.trackingSpace.InverseTransformPoint(real),Quaternion.identity));brush.CaptureLandmark();}
            Check(brush.Ready&&brush.AlignmentError<.0001f,"Rigid registration recovers known translation/yaw with residual under 0.1 mm");
            var realTip=Quaternion.Inverse(mapping)*(targets[0]-shift);
            Check(Vector3.Distance(brush.MapToVisual(realTip),realTip)<.0001f,"Neutral hand aligns with the actual calibrated hand");
            Check(Vector3.Distance(e.realHandAnchor.position,e.placement.NeutralPosition)<.0001f&&Quaternion.Angle(e.realHandAnchor.rotation,e.placement.NeutralRotation)<.01f,
                "Physical hand anchor uses the neutral pose without a second inverse registration");
            e.placement.Apply(new Vector3(.17f,0,.03f),25);
            var expected=e.hand.transform.TransformPoint(e.hand.transform.InverseTransformPoint(brush.visualLandmarks[0].position));
            Check(Vector3.Distance(brush.MapToVisual(realTip),expected)<.0001f,"Offset + yaw preserve anatomical brush correspondence");
            brush.Sample(now+=.01,new Pose(brush.trackingSpace.InverseTransformPoint(realTip),Quaternion.identity));
            Check(brush.Ready,"Intentional displacement does not invalidate calibration");
            brush.calibrationPreview=false;brush.Preview();Check(!e.ball.gameObject.activeSelf,"No synchronous preview leaks before a condition");
            e.settings=new TrialSettings{preparation=.1f,induction=1,melting=.3f,observation=.1f,enableMelting=false,handOffset=new Vector3(.17f,0,.03f),handYaw=25,synchrony=Synchrony.Asynchronous,asynchronousDelay=.2f};
            e.StartTrial();Check(e.Running,"Calibrated offset asynchronous trial starts");
            var velocity=new Vector3(.01f,0,.02f);double start=now;
            for(int i=1;i<=160;i++)
            {
                double elapsed=i*.01;brush.Sample(now=start+elapsed,new Pose(brush.trackingSpace.InverseTransformPoint(realTip+velocity*(float)elapsed),Quaternion.identity));e.Advance(elapsed);
                if(e.Phase==TrialPhase.Induction&&e.ball.gameObject.activeSelf)
                    Check(Vector3.Distance(brush.VisualTip,brush.MapToVisual(realTip+velocity*(float)(elapsed-.2)))<.0001f,"Delayed brush follows offset hand at the recorded source time");
            }
            Check(e.Phase==TrialPhase.Completed&&e.hand.Amount==0,"No-melt control keeps its shape for the matched interval");
            Check(File.Exists(e.LogPath)&&File.ReadAllText(e.LogPath).Contains("phase"),"Phase schedule and dispatch times are saved");
            e.ResetTrial();brush.Sample(now+=.01,new Pose(brush.trackingSpace.InverseTransformPoint(realTip),Quaternion.identity));
            TabletopConditionVibrationVerification.Run(e,Check);
            var study=e.study;study.protocol=new StudyProtocol{restSeconds=0};study.protocol.conditions=new List<StudyCondition>{new StudyCondition{code="synthetic",stimulus=new TrialSettings{preparation=0,induction=.2f,melting=.1f,observation=0,enableMelting=false,handOffset=Vector3.right*.17f}}};
            study.setupChecked=true;
            brush.Sample(now+=.01,new Pose(brush.trackingSpace.InverseTransformPoint(brush.MapToReal(study.virtualTarget.position)),Quaternion.identity));
            study.CapturePhysicalTarget();study.BeginStudy();
            Check(study.Stage==StudyStage.Baseline&&!e.hand.skin.enabled,"Study hides hand, scene and stimuli before baseline");
            for(int i=0;i<3;i++){study.Measurement.Adjust(.01f-study.Measurement.Coordinate);study.SubmitMarker();}
            Check(study.Stage==StudyStage.AwaitStimulus,"Baseline completion requires explicit operator start");
            study.StartStimuli();Check(study.Stage==StudyStage.Stimulation&&e.hand.skin.enabled,"Stimuli restore display only after baseline");
            e.Advance(1);
            // Dispatch the stage update deterministically, rather than waiting for a real clock.
            study.SendMessage("Update");Check(study.Stage==StudyStage.PostMeasurement&&!e.hand.skin.enabled,"Post-measurement hides the hand immediately after the stimulus sequence");
            for(int i=0;i<3;i++){study.Measurement.Adjust(.04f-study.Measurement.Coordinate);study.SubmitMarker();}
            Check(study.Stage==StudyStage.Ratings,"Ratings follow completed drift measurements");
            study.SubmitRatings(new[]{4,4,4,1,1,1});study.NextTrial(true);
            Check(study.Stage==StudyStage.Completed&&Mathf.Abs(study.RecordData.trials[0].driftMetres-.03f)<.0001f,"Whole study saves +30 mm drift, ratings and a completed session");
            Check(File.ReadAllText(study.RecordPath).Contains("Desktop simulation"),"Synthetic measurements cannot masquerade as physical participant data");
            string saved=File.ReadAllText(study.RecordPath);study.setupChecked=false;study.BeginStudy();study.Abort("Idle stop");
            Check(File.ReadAllText(study.RecordPath)==saved&&study.RecordData.status=="completed","Failed restart and idle stop preserve the completed study file");
            Check(e.keyboardControls&&e.controllerControls,"Completed study restores the previous standalone controls");
            e.ResetTrial();
            VirtualHandReviewVerification.Run(e,Check);
        }
    }
}
