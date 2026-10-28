using System;
using System.Collections;
using System.IO;
using System.Linq;
using UnityEngine;

namespace MarbleHand
{
    /// <summary>Opt-in built-player test; forces simulation and never connects physical output.</summary>
    public sealed class MarblePlayerSmoke : MonoBehaviour
    {
        public MarbleExperiment experiment;
        string folder;
        IEnumerator Start()
        {
            if (!Environment.GetCommandLineArgs().Contains("--smoke-test")) yield break;
            folder = Path.GetFullPath(Path.Combine(Application.dataPath, "../../Smoke")); Directory.CreateDirectory(folder);
            var test = Run();
            while (true)
            {
                object current;
                try { if (!test.MoveNext()) break; current = test.Current; }
                catch (Exception e)
                { experiment.Abort("Smoke failed"); File.WriteAllText(Path.Combine(folder, "result.txt"), "FAIL: " + e); Debug.LogException(e); Application.Quit(1); yield break; }
                yield return current;
            }
            Debug.Log("MARBLE_PLAYER_SMOKE_PASS"); Application.Quit(0);
        }
        IEnumerator Run()
        {
            var e = experiment;
            Require(e.desktopPreview && e.settings.backend == HapticBackend.Simulation, "Smoke requires --desktop simulation");
            yield return new WaitForSeconds(1);
            Require(e.OutputReady, "Simulated output ready"); e.AlignTable();
            int skin = Capture("01-skin.png"); Require(skin > 150, "Hands render in player");
            var wrist = e.rig.rightArm.wristTarget.position;
            int trials = 0;
            foreach (MarbleCondition condition in Enum.GetValues(typeof(MarbleCondition)))
            {
                e.settings.condition = condition; e.settings.duration = 6; e.settings.minInterval = .4f; e.settings.maxInterval = .5f; e.settings.rampHits = 4;
                e.StartTrial(); Require(e.Phase == TrialPhase.PreRatings, "Baseline rating gate");
                yield return new WaitForSeconds(.2f); Require(e.Contacts == 0, "No contacts before baseline confirmation");
                int pulsesBefore = e.OutputPulseCount; e.SubmitRatings();
                double until = Time.realtimeSinceStartupAsDouble + 12;
                while (e.Phase == TrialPhase.Induction && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Require(e.Phase == TrialPhase.PostRatings, "Post rating gate reached: " + e.Message);
                yield return new WaitForSeconds(.15f);
                Require(e.Contacts >= 10 && e.OutputPulseCount - pulsesBefore == e.Contacts, "One pulse per contact");
                Require(!e.OutputsActive, "Outputs stopped before ratings");
                Require(e.MarbleAmount == (condition == MarbleCondition.UnchangedSkin ? 0 : 1), "Condition visual result");
                Require(Vector3.Distance(wrist, e.rig.rightArm.wristTarget.position) < .00001f, "Raw tracking coordinates unchanged");
                int after = Capture("02-" + condition + ".png");
                Require(condition == MarbleCondition.UnchangedSkin ? Math.Abs(after - skin) < skin * .1f : after > skin * .2f && after < skin * .85f, "Rendered skin-to-marble change, skin control unchanged");
                string logPath = e.LogPath; e.SubmitRatings(); Require(e.Phase == TrialPhase.Complete, "Ratings required to complete");
                string csv = File.ReadAllText(logPath);
                Require(csv.Contains("impact_scheduled") && csv.Contains("pulse_on_written") && csv.Contains("post_ratings_begin") && csv.Contains("trial_complete"), "Stimulus and rating CSV");
                Require(csv.Split('\n').Count(line => line.Contains(",rating,")) == 8, "Four pre and four post ratings saved");
                yield return null;
                int restored = Capture("03-restored-" + condition + ".png"); Require(Math.Abs(restored - skin) < skin * .1f, "Completion restores skin");
                e.ResetTrial(); trials++;
            }
            e.StartTrial(); e.SubmitRatings();
            // First pulse has been queued (150 ms ahead) but has not started.
            yield return new WaitForSeconds(.9f); int before = e.OutputPulseCount;
            e.ResetTrial(); yield return new WaitForSeconds(1.8f);
            Require(e.OutputPulseCount == before && !e.OutputsActive && e.MarbleAmount == 0, "Abort cancels future stimuli and restores skin");
            var clips = e.stoneClips;
            for (int mask = 0; mask < 8; mask++)
            {
                bool sound = (mask & 1) != 0, visual = (mask & 2) != 0, vibration = (mask & 4) != 0;
                e.settings.condition = MarbleCondition.SynchronousMarble; e.settings.duration = 3; e.settings.rampHits = 2;
                e.settings.soundEnabled = sound; e.settings.visualEnabled = visual; e.settings.vibrationEnabled = vibration;
                // Prove sound-off trials do not require audio assets, and vibration-off trials do not require a connection.
                e.stoneClips = sound ? clips : null;
                if (vibration)
                {
                    e.ConnectOutput(); double readyBy = Time.realtimeSinceStartupAsDouble + 2;
                    while (!e.OutputReady && Time.realtimeSinceStartupAsDouble < readyBy) yield return null;
                    Require(e.OutputReady, "Simulated output reconnects");
                }
                int pulseStart = e.OutputPulseCount, audioStart = e.AudioScheduleCount;
                e.StartTrial(); Require(e.Phase == TrialPhase.PreRatings, "Combination starts without disabled devices/assets");
                string logPath = e.LogPath; e.SubmitRatings();
                // Changing the editable object mid-trial cannot alter the frozen conditions.
                e.settings.soundEnabled = !sound; e.settings.visualEnabled = !visual; e.settings.vibrationEnabled = !vibration;
                yield return new WaitForSeconds(1.3f);
                Require(e.Phase == TrialPhase.Induction && e.hammer.gameObject.activeSelf == visual, "Visual switch controls hammer");
                double until = Time.realtimeSinceStartupAsDouble + 8;
                while (e.Phase == TrialPhase.Induction && Time.realtimeSinceStartupAsDouble < until) yield return null;
                Require(e.Phase == TrialPhase.PostRatings, "Combination reaches post ratings: " + e.Message);
                yield return new WaitForSeconds(.15f);
                Require(e.Contacts >= 4 && e.OutputPulseCount - pulseStart == (vibration ? e.Contacts : 0), "Vibration switch controls pulses");
                Require(e.AudioScheduleCount - audioStart == (sound ? e.Contacts - 1 : 0), "Sound switch controls scheduled audio");
                Require(e.MarbleAmount == (visual ? 1 : 0) && e.rig.rightArm.skin.enabled, "Visual-off leaves the tracked skin hand visible");
                string json = File.ReadAllText(Path.ChangeExtension(logPath, ".json"));
                Require(json.Contains("\"soundEnabled\": " + (sound ? "true" : "false")) &&
                        json.Contains("\"visualEnabled\": " + (visual ? "true" : "false")) &&
                        json.Contains("\"vibrationEnabled\": " + (vibration ? "true" : "false")), "Frozen modality configuration saved");
                e.SubmitRatings(); Require(e.Phase == TrialPhase.Complete, "Combination completes");
                e.ResetTrial(); e.stoneClips = clips;
            }
            File.WriteAllText(Path.Combine(folder, "result.txt"), "PASS: " + trials + " original conditions and all 8 sound/visual/vibration combinations, including disabled device/assets, frozen settings, CSV, completion and queued abort.\nDesktop simulation only; no HMD/physical actuator used.\n");
        }
        int Capture(string name)
        {
            var camera = experiment.rig.head; var previous = camera.targetTexture; var active = RenderTexture.active;
            var target = new RenderTexture(1440, 960, 24); var texture = new Texture2D(1440, 960, TextureFormat.RGB24, false);
            try
            {
                camera.targetTexture = target; camera.Render(); RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, 1440, 960), 0, 0); texture.Apply();
                int skin = 0, magenta = 0;
                foreach (var c in texture.GetPixels32())
                { if (c.r > 60 && c.r > c.g * 1.15f && c.g > c.b * 1.1f) skin++; if (c.r > 220 && c.g < 30 && c.b > 220) magenta++; }
                Require(magenta < 20, "No magenta shader errors"); File.WriteAllBytes(Path.Combine(folder, name), texture.EncodeToPNG()); return skin;
            }
            finally { camera.targetTexture = previous; RenderTexture.active = active; Destroy(texture); target.Release(); Destroy(target); }
        }
        static void Require(bool okay, string message) { if (!okay) throw new InvalidOperationException(message); }
    }
}
