using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DisappearingHand
{
    /// <summary>Opt-in desktop-only integration check. Fast-forwards the protocol clock.</summary>
    public sealed class OriginalDhtPlayerSmoke : MonoBehaviour
    {
        public OriginalDhtExperiment experiment;
        string output;
        IEnumerator Start()
        {
            if (!experiment.desktopPreview || !Array.Exists(Environment.GetCommandLineArgs(),a=>a=="--smoke-test")) yield break;
            output = Path.GetFullPath("Builds/OriginalSmoke"); Directory.CreateDirectory(output);
            var run = Run();
            while (true)
            {
                object current;
                try { if (!run.MoveNext()) break; current = run.Current; }
                catch (Exception ex) { File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL\n"+ex); Debug.LogException(ex); Application.Quit(1); yield break; }
                yield return current;
            }
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: DH/SH/CH, manual gates, fade, reaching offset, CSV, explicit finish, abort. Desktop clock fast-forward; no HMD validation.");
            Debug.Log("ORIGINAL_DHT_PLAYER_SMOKE_PASS"); Application.Quit(0);
        }
        IEnumerator Run()
        {
            yield return new WaitForSeconds(.5f);
            foreach (OriginalDhtCondition c in Enum.GetValues(typeof(OriginalDhtCondition)))
            {
                experiment.ResetTrial(); experiment.AlignTable(); experiment.condition = c;
                experiment.ShowOperatorPanel = false; experiment.StartTrial();
                Check(experiment.Protocol.Phase==OriginalDhtPhase.Adaptation,"start "+c);
                yield return new WaitForSeconds(.1f);
                experiment.Protocol.Tick(12.5f); yield return new WaitForSeconds(.1f); yield return new WaitForEndOfFrame();
                Capture(c+"-adaptation.png");
                experiment.Protocol.Tick(25); yield return new WaitForSeconds(.1f);
                Check(experiment.Protocol.Phase==OriginalDhtPhase.AwaitTableRest,"table gate");
                experiment.Advance(); Check(experiment.Protocol.Phase==OriginalDhtPhase.Fade,"rest confirmed");
                experiment.Protocol.Tick(3.4f); yield return new WaitForEndOfFrame();
                var block = new MaterialPropertyBlock(); experiment.rig.rightArm.skin.GetPropertyBlock(block);
                Check(c!=OriginalDhtCondition.DisappearedHand || (block.GetFloat("_Fade") > .4f && block.GetFloat("_Fade") < .6f),"right fade property");
                Capture(c+"-fading.png");
                experiment.Protocol.Tick(7); yield return new WaitForSeconds(.1f);
                if (c==OriginalDhtCondition.CoveredHand)
                {
                    Check(experiment.Protocol.Phase==OriginalDhtPhase.AwaitCover,"cloth gate");
                    experiment.Advance(); Check(experiment.Protocol.Phase==OriginalDhtPhase.AwaitCover,"SPACE cannot fake cloth");
                    experiment.ConfirmCloth();
                }
                Check(experiment.Protocol.Phase==OriginalDhtPhase.AwaitReach,"reach cue gate");
                Check(!experiment.leftBar.enabled && !experiment.centreBar.enabled && !experiment.rightBar.enabled,"no remaining bars");
                experiment.Advance(); experiment.PreviewReach(); yield return new WaitForSeconds(.1f);
                var physicalGap = Vector3.Dot(experiment.rig.rightArm.wristTarget.position-experiment.rig.leftArm.wristTarget.position,experiment.tableFrame.right);
                Check(Mathf.Abs(physicalGap-(c==OriginalDhtCondition.DisappearedHand?.125f:0))<.001f,"reach undershoot includes both offsets");
                experiment.rig.rightArm.skin.GetPropertyBlock(block);
                Check(block.GetFloat("_Visible")== (c==OriginalDhtCondition.SeenHand?1:0),"condition rendering");
                Capture(c+"-reach.png");
                if (c==OriginalDhtCondition.DisappearedHand) experiment.MarkMiss(); else experiment.MarkContact();
                experiment.Advance(); experiment.Protocol.Tick(200);
                Check(experiment.Protocol.Phase==OriginalDhtPhase.Report,"no reveal during report");
                experiment.Advance(); yield return new WaitForSeconds(.1f);
                Check(experiment.Protocol.Phase==OriginalDhtPhase.Complete && experiment.Protocol.Shift==0,"complete restores");
                var csv = File.ReadAllText(experiment.LastLogPath);
                Check(csv.Contains("operator_confirmed_physical_table_rest") && csv.Contains("phase_Reach") && csv.Contains("phase_Report") && csv.Contains("phase_Complete"),"CSV events");
                Check(csv.Contains("desktop_preview=True") && csv.Contains("simulation"),"CSV simulation label");
                File.Copy(experiment.LastLogPath,Path.Combine(output,c+".csv"),true);
            }
            experiment.ResetTrial(); experiment.AlignTable(); experiment.condition = OriginalDhtCondition.DisappearedHand;
            experiment.StartTrial(); experiment.Protocol.Tick(12); yield return new WaitForSeconds(.1f);
            Vector3 raw = experiment.rig.rightArm.wristTarget.position;
            experiment.Abort("smoke_abort"); experiment.ApplyVisuals();
            Check(experiment.rig.rightArm.wristTarget.position==raw,"abort does not rewrite raw tracking");
            var restored = new MaterialPropertyBlock(); experiment.rig.rightArm.skin.GetPropertyBlock(restored);
            Check(restored.GetVector("_Shift")==Vector4.zero && restored.GetFloat("_Fade")==1 && restored.GetFloat("_Visible")==1,"abort restores renderer");
        }
        void Capture(string name)
        {
            var camera = experiment.rig.head; var target = new RenderTexture(1440,960,24);
            var previous = camera.targetTexture; var active = RenderTexture.active;
            var texture = new Texture2D(1440,960,TextureFormat.RGB24,false);
            try
            {
                experiment.ApplyVisuals(); camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0,0,1440,960),0,0); texture.Apply();
                int skin=0,magenta=0;
                foreach (var color in texture.GetPixels32())
                {
                    if (color.r>60 && color.r>color.g*1.15f && color.g>color.b*1.1f) skin++;
                    if (color.r>220 && color.g<30 && color.b>220) magenta++;
                }
                Check(skin>150 && magenta<20,"valid skin rendering: "+name);
                File.WriteAllBytes(Path.Combine(output,name),texture.EncodeToPNG());
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; Destroy(texture); target.Release(); Destroy(target); }
        }
        static void Check(bool ok,string reason) { if (!ok) throw new InvalidOperationException(reason); }
    }
}
