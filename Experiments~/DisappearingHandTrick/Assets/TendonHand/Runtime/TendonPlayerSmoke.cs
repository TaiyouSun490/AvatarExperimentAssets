using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace TendonHand
{
    public sealed class TendonPlayerSmoke : MonoBehaviour
    {
        public TendonHandExperiment experiment;
        string output;
        IEnumerator Start()
        {
            if (!Array.Exists(Environment.GetCommandLineArgs(), a => a == "--smoke-test")) yield break;
            output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../TendonSmoke")); Directory.CreateDirectory(output);
            var run = Run();
            while (true)
            {
                object current = null; bool more = false; Exception failure = null;
                try { more = run.MoveNext(); if (more) current = run.Current; } catch (Exception e) { failure = e; }
                if (failure != null)
                {
                    experiment.Abort("smoke_failure"); experiment.Disconnect();
                    File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL: " + failure);
                    Debug.LogException(failure); Application.Quit(1); yield break;
                }
                if (!more) yield break;
                yield return current;
            }
        }
        IEnumerator Run()
        {
            Require(experiment.desktopPreview, "Smoke test requires --desktop.");
            experiment.dryRun = true; experiment.Connect();
            float deadline = Time.realtimeSinceStartup + 8;
            while (!experiment.OutputReady && Time.realtimeSinceStartup < deadline) yield return null;
            Require(experiment.OutputReady, "Simulated device connection");
            var s = experiment.settings; s.baselineSeconds=.3f; s.inductionSeconds=.6f; s.recoverySeconds=1.5f;
            var firstPose = experiment.rig.rightArm.wristTarget.position;
            int count=0;
            foreach (RecoveryView view in Enum.GetValues(typeof(RecoveryView)))
            foreach (bool left in new[]{false,true})
            {
                experiment.ResetTrial(); experiment.Align(); s.targetLeft=left; s.recovery=view; s.vibration=true;
                experiment.StartTrial(); deadline=Time.realtimeSinceStartup+8;
                while (experiment.Protocol.Phase!=TendonPhase.Recovery && Time.realtimeSinceStartup<deadline) yield return null;
                Require(experiment.Protocol.Phase==TendonPhase.Recovery,"Reached recovery: " + view + " / " + experiment.Notice);
                Require(!experiment.OutputActive,"Output stopped before recovery");
                yield return new WaitForEndOfFrame();
                var arm=left?experiment.rig.leftArm:experiment.rig.rightArm;
                var properties=new MaterialPropertyBlock(); arm.skin.GetPropertyBlock(properties);
                Require(properties.GetFloat("_Visible")== (view==RecoveryView.Hidden?0:1),"Recovery visibility");
                Require(experiment.rig.rightArm.wristTarget.position==firstPose,"Raw hand remains stationary");
                if (view==RecoveryView.Veridical) Require(Vector3.Distance(experiment.DisplayedPose.position,arm.wristTarget.position)<.0001f,"Veridical position");
                else Require(Vector3.Distance(experiment.DisplayedPose.position,experiment.BaselinePose.position)>.05f,"Displaced virtual wrist");
                experiment.RecordReportedAngle(-12.5f);
                Capture(view+"-"+(left?"left":"right")+".png");
                while (experiment.Protocol.Active && Time.realtimeSinceStartup<deadline) yield return null;
                Require(experiment.Protocol.Phase==TendonPhase.Complete,"Trial completes");
                yield return new WaitForSeconds(.1f);
                string csv=File.ReadAllText(experiment.LastLogPath);
                foreach (string expected in new[]{"start_write_complete","stop_write_complete","recovery_visual_applied","phase_Complete","reported_angle","-12.500000","sample"})
                    Require(csv.Contains(expected),"CSV contains " + expected);
                count++;
            }
            experiment.ResetTrial(); experiment.Align(); s.vibration=false; s.visualMotion=false;
            experiment.StartTrial(); deadline=Time.realtimeSinceStartup+8;
            while (experiment.Protocol.Active && Time.realtimeSinceStartup<deadline) { Require(!experiment.OutputActive,"No-vibration control stays muted"); yield return null; }
            Require(experiment.Protocol.Phase==TendonPhase.Complete,"No-vibration control completes");
            experiment.ResetTrial(); experiment.Align(); s.vibration=true; s.visualMotion=true;
            experiment.StartTrial(); deadline=Time.realtimeSinceStartup+8;
            while (!experiment.OutputActive && Time.realtimeSinceStartup<deadline) yield return null;
            Require(experiment.OutputActive,"Abort test started output"); experiment.Abort("smoke_abort");
            yield return new WaitForSeconds(.15f);
            Require(!experiment.OutputActive && experiment.Protocol.Phase==TendonPhase.Aborted,"Abort mutes output");
            Require(File.ReadAllText(experiment.LastLogPath).Contains("stop_write_complete"),"Abort stop completion logged");
            experiment.Disconnect();
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: "+count+" left/right recovery trials; no-vibration control; abort stop; stationary raw tracking; player rendering; CSV reports and timestamps.\nSimulation only: no headset, physical stimulation or vibration timing tested.\n");
            Debug.Log("TENDON_PLAYER_SMOKE_PASS"); Application.Quit(0);
        }
        void Capture(string name)
        {
            var camera=experiment.rig.head;
            var target=new RenderTexture(1440,1080,24);var old=camera.targetTexture;var active=RenderTexture.active;
            var image=new Texture2D(1440,1080,TextureFormat.RGB24,false);
            try
            {
                experiment.ApplyVisuals();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                image.ReadPixels(new Rect(0,0,1440,1080),0,0);image.Apply();
                int skin=0,magenta=0;
                foreach(var c in image.GetPixels32())
                { if(c.r>60&&c.r>c.g*1.15f&&c.g>c.b*1.1f)skin++;if(c.r>220&&c.g<30&&c.b>220)magenta++; }
                Require(skin>150 && magenta<20,"Hand rendering is present and shaders compile");
                File.WriteAllBytes(Path.Combine(output,name),image.EncodeToPNG());
            }
            finally {camera.targetTexture=old;RenderTexture.active=active;Destroy(image);target.Release();Destroy(target);}
        }
        static void Require(bool pass,string message) { if(!pass)throw new InvalidOperationException(message); }
    }
}
