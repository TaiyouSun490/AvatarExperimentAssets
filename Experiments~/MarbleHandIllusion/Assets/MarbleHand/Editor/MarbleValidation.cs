using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using AvatarExperiments.FpsArms.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MarbleHand.Editor
{
    public static class MarbleValidation
    {
        static int checks;
        public static void PrepareAndRun() { MarbleBuild.Prepare(); Run(); }
        [MenuItem("Tools/Marble Hand/Validate")]
        public static void Run()
        {
            checks = 0;
            var baseline = MarbleProtocol.Plan(new MarbleSettings(), 2);
            Check(baseline.Count > 200 && baseline.Count < 300, "Five-minute contact count");
            foreach (MarbleCondition condition in Enum.GetValues(typeof(MarbleCondition)))
            {
                var s = new MarbleSettings { condition = condition };
                var copy = s.Snapshot(); s.duration = 30;
                Check(copy.duration == 300, "Configuration snapshot is independent");
                var plan = MarbleProtocol.Plan(copy, 2);
                Check(plan.Count == baseline.Count, "Contact count matched between conditions");
                Check(plan[0].gain == 0 && plan[29].gain == 1, "Silent first hit; full gain on hit 30");
                for (int i = 0; i < plan.Count; i++)
                {
                    var hit = plan[i];
                    Check(hit.contactTime == baseline[i].contactTime && hit.clip == baseline[i].clip, "Matched stimulus sequence");
                    Check(hit.contactTime < 300, "No contact after induction");
                    Check(condition == MarbleCondition.UnchangedSkin ? hit.blend == 0 : hit.blend == hit.gain, "Condition visual contrast");
                    double delay = hit.soundTime - hit.contactTime;
                    Check(condition == MarbleCondition.DelayedSound ? delay >= .8 && delay <= 1.6 : delay == 0, "Condition sound delay");
                    if (i > 0) Check(hit.contactTime - plan[i - 1].contactTime >= .8 && hit.contactTime - plan[i - 1].contactTime <= 1.600001, "Nonrhythmic interval range");
                }
                double end = MarbleProtocol.EndTime(copy, plan, .11);
                Check(end >= 300 && end >= plan[plan.Count - 1].soundTime + .11, "Delayed tail finishes before post ratings");
            }
            foreach (float bad in new[] { float.NaN, float.PositiveInfinity, -1f, 3000f })
            { bool rejected = false; try { new MarbleSettings { duration = bad }.Snapshot(); } catch (ArgumentException) { rejected = true; } Check(rejected, "Invalid duration rejected"); }
            Check(MarbleProtocol.Plan(new MarbleSettings { seed = 4 }, 2)[1].contactTime != baseline[1].contactTime, "Seed controls sequence");
            var reordered = new List<MarbleImpact> { new MarbleImpact(1, 0, 2, 3.6, 0, 0), new MarbleImpact(2, 0, 2.4, 3.2, 0, 0) };
            Check(Math.Abs(MarbleProtocol.EndTime(new MarbleSettings { duration = 3 }, reordered, .1) - 3.7) < .00001, "Tail includes reordered delayed sounds");
            foreach (bool sound in new[] { false, true })
            foreach (bool visual in new[] { false, true })
            foreach (bool vibration in new[] { false, true })
            {
                var s = new MarbleSettings { soundEnabled = sound, visualEnabled = visual, vibrationEnabled = vibration };
                var frozen = s.Snapshot(); var plan = MarbleProtocol.Plan(frozen, 2);
                s.soundEnabled = !sound; s.visualEnabled = !visual; s.vibrationEnabled = !vibration;
                Check(frozen.soundEnabled == sound && frozen.visualEnabled == visual && frozen.vibrationEnabled == vibration, "Modality toggles are frozen for the trial");
                Check(plan.Count == baseline.Count && plan[20].contactTime == baseline[20].contactTime, "Modality switches preserve contact timing");
                Check(plan[20].blend == (visual ? baseline[20].blend : 0), "Visual switch controls marble independently");
                Check(MarbleProtocol.EndTime(frozen, reordered, 301) == (sound ? 304.6 : 300), "Sound-off skips delayed audio tail");
            }
            OutputChecks(); FpsArmSmokeTests.Run();
            EditorSceneManager.OpenScene(MarbleBuild.ScenePath);
            var e = UnityEngine.Object.FindAnyObjectByType<MarbleExperiment>();
            Check(e && e.rig && e.hammer && e.worldStatus && e.GetComponent<PcvrSession>().experiment == e, "Independent scene wired");
            Check(e.settings.backend == HapticBackend.Simulation && !e.desktopPreview, "PCVR scene defaults to simulated output");
            Check(e.settings.soundEnabled && e.settings.visualEnabled && e.settings.vibrationEnabled, "All three modalities enabled by default");
            Check(e.stoneClips.Length == 2 && e.stoneClips[0] && e.stoneClips[1], "Audio assets present");
            foreach (var clip in e.stoneClips)
            { Check(clip.channels == 1 && clip.frequency == 48000 && clip.length > .02 && clip.length < .2, "Short mono 48 kHz impact"); }
            var shader = Shader.Find("MarbleHand/SkinToStone"); Check(shader && !ShaderUtil.ShaderHasError(shader), "Marble shader compiles");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/marble-validation.txt", "PASS: " + checks + " checks; shared FPS-arm 484-pose regression. No physical device used.\n");
            Debug.Log("MARBLE_VALIDATION_PASS: " + checks);
        }
        static void OutputChecks()
        {
            var device = new FakeDevice();
            using (var output = new MarbleHapticOutput(() => device))
            {
                Wait(() => output.Ready, "Device state handshake");
                output.Heartbeat(); output.Schedule(1, MarbleHapticOutput.Now + .05, new MarbleSettings());
                Wait(() => device.starts == 1, "Scheduled pulse begins");
                Wait(() => output.Active, "Pulse is active");
                Wait(() => !output.Active, "Pulse ends without Unity frames");
                Check(device.off - device.on >= .045 && device.off - device.on < .15, "Finite pulse duration");
                output.Heartbeat(); output.Schedule(2, MarbleHapticOutput.Now + .2, new MarbleSettings()); output.Stop();
                Thread.Sleep(250); Check(device.starts == 1 && !output.Active, "Stop cancels pending pulse");
                output.Heartbeat(); output.Schedule(3, MarbleHapticOutput.Now + .8, new MarbleSettings());
                Wait(() => output.Failure != null, "Host watchdog cancels scheduled output", 1.2);
                Check(device.starts == 1, "No pulse after heartbeat expiration");
            }
            Check(device.disposed && device.mutes >= 3, "Disposal mutes and closes device");
            var broken = new FakeDevice { fail = true };
            using (var output = new MarbleHapticOutput(() => broken))
            {
                Wait(() => output.Ready, "Failure device handshake"); output.Heartbeat();
                output.Schedule(1, MarbleHapticOutput.Now + .02, new MarbleSettings());
                Wait(() => output.Failure != null, "Serial write failure surfaced");
                Wait(() => !output.Active && !output.Ready, "Failed output cannot remain active");
            }
        }
        sealed class FakeDevice : IMarbleDevice
        {
            public volatile int starts, mutes;
            public volatile bool disposed;
            public bool fail;
            public double on, off;
            public void Start(int channel, float hz, float amplitude)
            { if (fail) throw new IOException("Injected serial failure"); on = MarbleHapticOutput.Now; starts++; }
            public void Mute() { off = MarbleHapticOutput.Now; mutes++; }
            public void Query() { }
            public IEnumerable<string> Read() { yield return "595 state=fake"; }
            public void Dispose() { disposed = true; }
        }
        static void Wait(Func<bool> test, string message, double timeout = 1)
        { double until = MarbleHapticOutput.Now + timeout; while (!test() && MarbleHapticOutput.Now < until) Thread.Sleep(2); Check(test(), message); }
        static void Check(bool okay, string message)
        { checks++; if (!okay) throw new InvalidOperationException("MARBLE VALIDATION: " + message); }
    }
}
