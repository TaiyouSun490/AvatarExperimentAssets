using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace DisappearingHand
{
    /// <summary>Opt-in executable smoke run; never runs during normal headset sessions.</summary>
    public sealed class PlayerSmokeCheck : MonoBehaviour
    {
        public DisappearingHandExperiment experiment;
        IEnumerator Start()
        {
            if(!Array.Exists(Environment.GetCommandLineArgs(),a=>a=="--smoke-test"))yield break;
            if(!experiment.desktopPreview)throw new InvalidOperationException("Smoke test requires --desktop.");
            string output=Path.GetFullPath(Path.Combine(Application.dataPath,"../../Smoke"));Directory.CreateDirectory(output);
            yield return new WaitForSeconds(1);
            int readyPixels=Capture(Path.Combine(output,"01-ready.png"));
            experiment.settings.baselineSeconds=.3f;experiment.settings.driftSeconds=1;
            experiment.settings.holdSeconds=.2f;experiment.settings.searchSeconds=2;experiment.settings.revealSeconds=.5f;
            experiment.StartTrial();
            float deadline=Time.realtimeSinceStartup+12;
            while(experiment.Protocol.Phase!=TrialPhase.Search && Time.realtimeSinceStartup<deadline)yield return null;
            if(experiment.Protocol.Phase!=TrialPhase.Search)throw new InvalidOperationException("Smoke trial never reached search.");
            yield return new WaitForEndOfFrame();
            var block=new MaterialPropertyBlock();experiment.rig.rightArm.skin.GetPropertyBlock(block);
            if(block.GetFloat("_Visible")!=0)throw new InvalidOperationException("Target hand is still visible.");
            int hiddenPixels=Capture(Path.Combine(output,"02-hidden.png"));
            if(hiddenPixels<readyPixels*.2f || hiddenPixels>readyPixels*.8f)throw new InvalidOperationException("Rendered hidden-hand pixel count is not approximately half of baseline.");
            experiment.ConfirmSearch();
            while(experiment.Protocol.Active && Time.realtimeSinceStartup<deadline)yield return null;
            if(experiment.Protocol.Phase!=TrialPhase.Complete)throw new InvalidOperationException("Smoke trial did not complete.");
            yield return new WaitForEndOfFrame();
            experiment.rig.rightArm.skin.GetPropertyBlock(block);
            if(block.GetFloat("_Visible")!=1 || block.GetVector("_Shift").sqrMagnitude>.000001f)throw new InvalidOperationException("Hand was not restored.");
            int restoredPixels=Capture(Path.Combine(output,"03-restored.png"));
            if(restoredPixels<readyPixels*.8f)throw new InvalidOperationException("Rendered hands were not restored.");
            string csv=File.ReadAllText(experiment.LastLogPath);
            if(!csv.Contains("phase_Search")||!csv.Contains("search_confirmed")||!csv.Contains("phase_Complete"))throw new InvalidOperationException("Trial CSV is incomplete.");
            File.WriteAllText(Path.Combine(output,"result.txt"),"PASS: built player start, drift, hide, confirm, reveal, restore, CSV.\nRendered skin pixels ready/hidden/restored: "+readyPixels+"/"+hiddenPixels+"/"+restoredPixels+"\n"+experiment.LastLogPath);
            Debug.Log("DHT_PLAYER_SMOKE_PASS");yield return new WaitForSeconds(.5f);Application.Quit(0);
        }
        int Capture(string path)
        {
            // A hidden Windows window may have a black backbuffer. Render the actual
            // scene camera to a texture so pixel verification still exercises player shaders.
            var camera=experiment.rig.head;
            var target=new RenderTexture(1440,960,24);var old=camera.targetTexture;var active=RenderTexture.active;
            var texture=new Texture2D(1440,960,TextureFormat.RGB24,false);
            try
            {
                experiment.ApplyVisuals();camera.targetTexture=target;camera.Render();RenderTexture.active=target;
                texture.ReadPixels(new Rect(0,0,1440,960),0,0);texture.Apply();
                int skinPixels=0, errorPixels=0;
                foreach(var color in texture.GetPixels32())
                {
                    if(color.r>60&&color.r>color.g*1.15f&&color.g>color.b*1.1f)skinPixels++;
                    if(color.r>220&&color.g<30&&color.b>220)errorPixels++;
                }
                if(errorPixels>20)throw new InvalidOperationException("Player render contains magenta shader errors: "+errorPixels);
                if(skinPixels<150)throw new InvalidOperationException("Player render is blank or hand shader is missing.");
                File.WriteAllBytes(path,texture.EncodeToPNG());return skinPixels;
            }
            finally {camera.targetTexture=old;RenderTexture.active=active;Destroy(texture);target.Release();Destroy(target);}
        }
    }
}
