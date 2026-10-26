using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace DisappearingHand
{
    public sealed class LookAwayPlayerSmoke : MonoBehaviour
    {
        public DisappearingHandExperiment experiment;
        string output;
        IEnumerator Start()
        {
            if (!Array.Exists(Environment.GetCommandLineArgs(),x=>x=="--smoke-test")) yield break;
            output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../LookAwaySmoke"));Directory.CreateDirectory(output);
            var run=Run();
            while(true)
            {
                bool more=false;object current=null;Exception failure=null;
                try {more=run.MoveNext();if(more)current=run.Current;}catch(Exception e){failure=e;}
                if(failure!=null)
                {
                    experiment.Abort("smoke_failure");File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL: "+failure);
                    Debug.LogException(failure);Application.Quit(1);yield break;
                }
                if(!more)yield break;
                yield return current;
            }
        }
        IEnumerator Run()
        {
            Require(experiment.desktopPreview,"Desktop mode required");yield return null;
            var s=experiment.settings;
            s.presentation=DriftPresentation.LookAwaySteps;s.baselineSeconds=.3f;s.displacement=.02f;
            s.lookAwayStepMetres=.01f;s.lookAwayDwellSeconds=.1f;s.lookBackDwellSeconds=.1f;
            s.holdSeconds=.3f;s.searchSeconds=3;s.revealSeconds=.2f;
            int trials=0;
            foreach(bool left in new[]{false,true}) foreach(bool control in new[]{false,true})
            {
                experiment.ResetTrial();experiment.SetPreviewLookingAway(false);experiment.AlignTable();
                s.targetLeft=left;s.controlCondition=control;experiment.StartTrial();
                var rawLeft=experiment.rig.leftArm.wristTarget.position;var rawRight=experiment.rig.rightArm.wristTarget.position;
                yield return new WaitForSeconds(.6f);
                Require(experiment.Protocol.Phase==TrialPhase.Drift&&experiment.Protocol.Shift==0,"Visible hands never shift");
                experiment.SetPreviewLookingAway(true);yield return new WaitForSeconds(.4f);
                Require(experiment.Protocol.HiddenSteps==1,"One hidden change per episode");
                Require(experiment.HandView.bothOutside,"Actual camera view is outside");
                yield return new WaitForSeconds(.5f);
                Require(experiment.Protocol.HiddenSteps==1,"Long away episode cannot add a second step");
                experiment.SetPreviewLookingAway(false);yield return new WaitForSeconds(.3f);
                Require(experiment.Protocol.HiddenSteps==1&&experiment.LookAwayArmed,"Visible return re-arms without shifting");
                Require(Mathf.Abs(experiment.Protocol.Shift-(control?0:.01f))<.00001f,"Expected visual offset");
                Capture((left?"left":"right")+(control?"-control":"-shift")+".png");
                experiment.SetPreviewLookingAway(true);yield return new WaitForSeconds(.4f);
                Require(experiment.Protocol.HiddenSteps==2&&experiment.Protocol.Phase==TrialPhase.Drift,"Final step waits for visible return");
                Require(experiment.rig.leftArm.wristTarget.position==rawLeft&&experiment.rig.rightArm.wristTarget.position==rawRight,"Head turn never moves raw wrists");
                experiment.SetPreviewLookingAway(false);
                float deadline=Time.realtimeSinceStartup+5;
                while(experiment.Protocol.Phase!=TrialPhase.Search&&Time.realtimeSinceStartup<deadline)yield return null;
                Require(experiment.Protocol.Phase==TrialPhase.Search,"Return, hold, then search");
                yield return new WaitForEndOfFrame();
                var block=new MaterialPropertyBlock();(left?experiment.rig.leftArm:experiment.rig.rightArm).skin.GetPropertyBlock(block);
                Require(block.GetFloat("_Visible")==0,"Target hidden for search");
                experiment.ConfirmSearch();
                while(experiment.Protocol.Active&&Time.realtimeSinceStartup<deadline)yield return null;
                Require(experiment.Protocol.Phase==TrialPhase.Complete&&experiment.Protocol.Shift==0,"Complete restores hands");
                var lines=File.ReadAllLines(experiment.LastLogPath);
                var steps=lines.Where(line=>line.StartsWith("look_away_step,")).ToArray();
                Require(steps.Length==2,"Exactly two logged changes");
                var header=lines.First(line=>line.StartsWith("event,")).Split(',');
                foreach(var line in steps)
                {
                    var cells=line.Split(',');
                    Require(cells[Array.IndexOf(header,"both_outside")]=="1"&&cells[Array.IndexOf(header,"candidate_outside")]=="1","Every step logged outside view");
                }
                Require(lines.Any(line=>line.StartsWith("look_back_after_final_step,")),"Final return logged");trials++;
            }
            experiment.ResetTrial();experiment.AlignTable();s.controlCondition=false;experiment.StartTrial();
            yield return new WaitForSeconds(.5f);experiment.SetPreviewLookingAway(true);yield return new WaitForSeconds(.3f);
            Require(experiment.Protocol.Shift>0,"Abort test has displacement");experiment.Abort("smoke_abort");
            Require(experiment.Protocol.Shift==0&&!experiment.Protocol.Active,"Abort restores offset");
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: "+trials+" left/right x displaced/control trials; visible freeze; one step per away episode; re-arm; final return; raw wrists unchanged; hidden target; restore/abort; rendered images and CSV view evidence.\nHMD pose simulated. No human detection/illusion testing.\n");
            Debug.Log("LOOK_AWAY_PLAYER_SMOKE_PASS");Application.Quit(0);
        }
        void Capture(string name)
        {
            var camera=experiment.rig.head;var target=new RenderTexture(1440,960,24);
            var previous=camera.targetTexture;var active=RenderTexture.active;var texture=new Texture2D(1440,960,TextureFormat.RGB24,false);
            try
            {
                experiment.ApplyVisuals();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1440,960),0,0);texture.Apply();int skin=0,magenta=0;
                foreach(var c in texture.GetPixels32()){if(c.r>60&&c.r>c.g*1.15f&&c.g>c.b*1.1f)skin++;if(c.r>220&&c.g<30&&c.b>220)magenta++;}
                Require(skin>150&&magenta<20,"Valid hand rendering");File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());
            }
            finally {camera.targetTexture=previous;RenderTexture.active=active;Destroy(texture);target.Release();Destroy(target);}
        }
        static void Require(bool pass,string label){if(!pass)throw new InvalidOperationException(label);}
    }
}
