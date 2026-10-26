using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace DisappearingHand.Editor
{
    public static class OriginalDhtValidation
    {
        static int count;
        public static void PrepareAndRun() { OriginalDhtBuild.Prepare(); Run(); }
        [MenuItem("Tools/Disappearing Hand/Validate original 2011 procedure")]
        public static void Run()
        {
            count = 0;
            foreach (OriginalDhtCondition condition in Enum.GetValues(typeof(OriginalDhtCondition)))
            {
                var p = new OriginalDhtProtocol();
                Check(!p.ConfirmTableRest() && !p.BeginReach() && !p.Complete(),"no skipped start");
                Check(p.Start(condition),"start"); Check(!p.Start(condition),"no restart");
                p.Tick(float.NaN); p.Tick(float.PositiveInfinity); p.Tick(-1); Near(p.Elapsed,0,"invalid dt rejected");
                p.Tick(12.5f);
                Near(p.Shift,condition==OriginalDhtCondition.DisappearedHand?.03125f:0,"half drift");
                Near(p.BarGap,.19875f,"independent bar speed");
                Near(p.RightBrightness,1,"no early hiding");
                p.Tick(12.5f); Check(p.Phase==OriginalDhtPhase.AwaitTableRest,"25s adaptation");
                Near(p.Shift,condition==OriginalDhtCondition.DisappearedHand?.0625f:0,"6.25cm only DH");
                Near(p.BarGap,.175f,"final gap");
                p.Tick(1000); Check(p.Phase==OriginalDhtPhase.AwaitTableRest,"no automatic physical contact");
                Check(!p.BeginReach() && !p.FinishReach(),"no early reach");
                Check(p.ConfirmTableRest(),"table confirmed");
                p.Tick(3.5f); Near(p.BarsBrightness,.5f,"half bars fade");
                Near(p.RightBrightness,condition==OriginalDhtCondition.DisappearedHand?.5f:1,"simultaneous DH fade");
                p.Tick(3.5f); Near(p.BarsBrightness,0,"bars gone at 7s");
                if (condition==OriginalDhtCondition.CoveredHand)
                {
                    Check(p.Phase==OriginalDhtPhase.AwaitCover && !p.BeginReach(),"CH waits for actual cloth");
                    Near(p.RightBrightness,1,"CH visible until cloth confirmed");
                    p.Tick(100); Check(p.Phase==OriginalDhtPhase.AwaitCover,"CH no timeout substitution");
                    Check(p.ConfirmClothPlaced(),"cloth confirmation");
                }
                Check(p.Phase==OriginalDhtPhase.AwaitReach,"wait reach cue");
                Near(p.RightBrightness,condition==OriginalDhtCondition.SeenHand?1:0,"condition visibility");
                Check(p.BeginReach(),"reach cue");
                var shift = p.Shift; p.Tick(500); Check(p.Phase==OriginalDhtPhase.Reach,"no forced timed reveal");
                Near(p.Shift,shift,"left offset retained for reaching");
                Near(p.Offset(true,Vector3.right).x,shift,"left offset"); Near(p.Offset(false,Vector3.right).x,-shift,"right offset");
                Check(p.FinishReach(),"reach finished"); p.Tick(500);
                Check(p.Phase==OriginalDhtPhase.Report,"keep manipulation for report");
                Near(p.Shift,shift,"report offset");
                Check(p.Complete(),"explicit finish"); Near(p.Shift,0,"restore"); Near(p.RightBrightness,1,"restore visibility");
                p.Start(condition); p.Tick(10); p.Abort(); Near(p.Shift,0,"abort offset"); Near(p.RightBrightness,1,"abort visible");
                p.Reset(); Check(p.Phase==OriginalDhtPhase.Ready,"reset");
            }
            var overshoot = new OriginalDhtProtocol(); overshoot.Start(OriginalDhtCondition.DisappearedHand); overshoot.Tick(26);
            Near(overshoot.PhaseTime,1,"overshoot retained at manual gate");
            Check(!overshoot.ConfirmClothPlaced(),"wrong confirmation ignored");
            var handMaterial = new Material(Shader.Find("DisappearingHand/ShiftedHand"));
            Near(handMaterial.GetFloat("_Fade"),1,"legacy shader fade default"); UnityEngine.Object.DestroyImmediate(handMaterial);
            // Existing protocols and FPS rig remain usable after shared shader/session changes.
            LookAwayValidation.Run();
            EditorSceneManager.OpenScene(OriginalDhtBuild.ScenePath);
            var e = UnityEngine.Object.FindAnyObjectByType<OriginalDhtExperiment>();
            Check(e && e.rig && e.leftBar && e.centreBar && e.rightBar,"scene references");
            Check(e.GetComponent<PcvrSession>().originalExperiment==e,"PCVR binding");
            Check(e.GetComponent<ExperimentMenuPointer>().originalExperiment==e,"menu binding");
            Check(!UnityEngine.Object.FindAnyObjectByType<DisappearingHandExperiment>(),"old controller absent");
            Debug.Log("ORIGINAL_DHT_VALIDATION_PASS: "+count+" assertions + look-away/original/FPS regression");
        }
        static void Check(bool ok,string message) { count++; if (!ok) throw new InvalidOperationException("Original DHT: "+message); }
        static void Near(float actual,float expected,string message) => Check(Mathf.Abs(actual-expected)<.00001f,message+": "+actual+" vs "+expected);
    }
}
