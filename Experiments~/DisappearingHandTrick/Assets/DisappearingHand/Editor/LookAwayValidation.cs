using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DisappearingHand.Editor
{
    public static class LookAwayValidation
    {
        static int checks;
        public static void PrepareAndRun() { LookAwayBuild.Prepare(); Run(); }
        [MenuItem("Tools/Disappearing Hand/Validate look-away variant")]
        public static void Run()
        {
            checks = 0;
            var seen = new HandViewObservation { valid=true, bothVisible=true };
            var away = new HandViewObservation { valid=true, bothOutside=true, candidateOutside=true };
            var s = new TrialSettings { presentation=DriftPresentation.LookAwaySteps, baselineSeconds=.1f, displacement=.025f, lookAwayStepMetres=.01f };
            var gate = new LookAwayGate();
            Check(!gate.Tick(.3f,away,s),"Cannot shift before first looking at hands");
            gate.Tick(.3f,seen,s); Check(gate.Armed,"Stable return arms one change");
            Check(!gate.Tick(.1f,away,s),"Brief glance is insufficient");
            Check(gate.Tick(.2f,away,s),"Sustained away makes one change");
            for(int i=0;i<30;i++) Check(!gate.Tick(.1f,away,s),"Staying away does not accumulate steps");
            gate.Tick(.1f,seen,s); Check(!gate.Armed,"Short return does not re-arm");
            gate.Tick(.2f,seen,s); Check(gate.Armed,"Return dwell re-arms");
            var candidateVisible=away;candidateVisible.candidateOutside=false;
            Check(!gate.Tick(.4f,candidateVisible,s),"Cannot move an offscreen hand into view");
            gate.Tick(.1f,away,s);gate.Tick(.1f,default,s);Check(!gate.Armed,"Invalid tracking clears gate");
            gate.Tick(.3f,seen,s);Check(!gate.Tick(float.NaN,away,s)&&!gate.Armed,"Invalid time fails closed");
            foreach(bool control in new[]{false,true}) foreach(bool left in new[]{false,true})
            {
                s.controlCondition=control;s.targetLeft=left;
                var p=new TrialProtocol();p.Start(s);p.Tick(.1f);p.Tick(40);
                Check(p.Phase==TrialPhase.Drift&&p.Shift==0,"Watching never advances displacement");
                Check(!p.FinishHiddenDrift(),"Cannot finish before all steps");
                p.AdvanceHiddenStep();Check(Mathf.Abs(p.Shift-(control?0:.01f))<.000001f,"First step");
                p.Tick(10);Check(p.HiddenSteps==1,"Wall time never adds steps");
                p.AdvanceHiddenStep();p.AdvanceHiddenStep();
                Check(Mathf.Abs(p.Shift-(control?0:.025f))<.000001f,"Last step is clamped");
                Check(!p.AdvanceHiddenStep()&&p.Phase==TrialPhase.Drift,"Wait for return after final step");
                Check(p.FinishHiddenDrift()&&p.Phase==TrialPhase.Hold,"Return enters hold");
                p.Tick(3);Check(p.Phase==TrialPhase.Search&&!p.IsVisible(left),"Normal search follows hold");
                p.Abort();Check(p.Shift==0&&p.IsVisible(left),"Abort restores");
            }
            s.controlCondition=false;
            var timeout=new TrialProtocol();timeout.Start(s);timeout.Tick(s.lookAwayTimeoutSeconds+1);
            Check(timeout.Phase==TrialPhase.Aborted&&timeout.Shift==0,"Timeout aborts instead of forcing displacement");
            var partial=new TrialProtocol();partial.Start(s);partial.Tick(.1f);partial.AdvanceHiddenStep();partial.HideNow();
            Check(Mathf.Abs(partial.Shift-.01f)<.000001f,"Operator early hide freezes attained step");
            var sanitized=new TrialSettings {lookAwayStepMetres=float.NaN,lookAwayDwellSeconds=-1,handBoundsRadius=0}.Snapshot();
            Check(sanitized.lookAwayStepMetres==.01f&&sanitized.lookAwayDwellSeconds==.1f&&sanitized.handBoundsRadius==.15f,"Bounds sanitized");
            GeometryChecks();
            ExperimentValidation.Run();
            EditorSceneManager.OpenScene(LookAwayBuild.ScenePath);
            var experiment=UnityEngine.Object.FindAnyObjectByType<DisappearingHandExperiment>();
            Check(experiment&&!experiment.desktopPreview&&experiment.settings.presentation==DriftPresentation.LookAwaySteps,"Variant scene defaults");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/look-away-validation.txt","PASS: "+checks+" look-away checks; original DHT 80 checks and FPSArm 484-pose regression also passed.\nNo headset or perception testing.\n");
            Debug.Log("LOOK_AWAY_VALIDATION_PASS: "+checks);
        }
        static void GeometryChecks()
        {
            var go=new GameObject("View detector test");var camera=go.AddComponent<Camera>();
            camera.fieldOfView=70;camera.aspect=1.5f;camera.nearClipPlane=.01f;camera.farClipPlane=20;
            var detector=new HandViewDetector();var a=new Vector3(-.2f,0,1);var b=new Vector3(.2f,0,1);
            var seen=detector.Observe(camera,a,b,a,b,.25f);
            Check(seen.valid&&seen.bothVisible&&!seen.bothOutside,"Forward hand volumes visible");
            camera.transform.rotation=Quaternion.Euler(0,120,0);
            var away=detector.Observe(camera,a,b,a,b,.25f);
            Check(away.valid&&away.bothOutside&&away.candidateOutside,"Head turn moves both outside");
            var visiblePoint=camera.transform.position+camera.transform.forward;
            Check(!detector.Observe(camera,a,visiblePoint,a,visiblePoint,.25f).bothOutside,"One visible hand blocks change");
            Check(!detector.Observe(camera,a,b,visiblePoint,b,.25f).candidateOutside,"Visible destination blocks change");
            var first=GeometryUtility.CalculateFrustumPlanes(camera);
            camera.transform.rotation=Quaternion.identity;var second=GeometryUtility.CalculateFrustumPlanes(camera);
            Check(!HandViewDetector.Evaluate(first,second,a,b,a,b,.25f).bothOutside,"Either eye sees hands: no shift");
            Check(!HandViewDetector.Evaluate(new Plane[6],second,a,b,a,b,.25f).valid,"Invalid frustum fails closed");
            UnityEngine.Object.DestroyImmediate(go);
        }
        static void Check(bool pass,string label) { if(!pass)throw new InvalidOperationException("Look-away validation: "+label);checks++; }
    }
}
