using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using AvatarExperiments.FpsArms;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;

namespace MarbleHand
{
    [DefaultExecutionOrder(12000)]
    public sealed class MarbleExperiment : MonoBehaviour
    {
        public TrackedHandsAvatarRig rig;
        public Transform tableFrame, hammer;
        public Text worldStatus;
        public AudioClip[] stoneClips;
        public MarbleSettings settings = new MarbleSettings();
        public bool desktopPreview;
        public string serialPort = "COM3";
        public TrialPhase Phase { get; private set; }
        public float MarbleAmount { get; private set; }
        public int Contacts { get; private set; }
        public string LogPath { get; private set; }
        public int OutputPulseCount { get; private set; }
        public int AudioScheduleCount { get; private set; }
        public bool OutputReady => output != null && output.Ready;
        public bool OutputsActive => (output != null && output.Active) || audioPool.Any(a => a && a.isPlaying);
        public bool TrackingReady => desktopPreview || (rig.HeadTracked && rig.rightArm.tracked);
        public bool Busy => Phase == TrialPhase.PreRatings || Phase == TrialPhase.Induction || Phase == TrialPhase.PostRatings;
        public string Message { get; private set; } = "Align the table, choose a condition, then start.";
        MarbleSettings trial;
        List<MarbleImpact> plan;
        readonly List<AudioSource> audioPool = new List<AudioSource>();
        readonly List<double> audioUntil = new List<double>();
        MarbleHapticOutput output;
        StreamWriter log;
        MaterialPropertyBlock block;
        double origin, end, previousFrame, lastPoseLog;
        int scheduled, contacted;
        bool aligned, controllerPulse, destroying;
        string connectedPort;
        HapticBackend connectedBackend;
        readonly float[] ratings = { 5, 5, 5, 5 };
        readonly string[] ratingLabels = { "Hardness", "Heaviness", "Stiffness", "Feels like my hand" };
        const double LookAhead = .15;

        void Awake()
        {
            desktopPreview |= Environment.GetCommandLineArgs().Contains("--desktop");
            if (desktopPreview) { settings.backend = HapticBackend.Simulation; rig.enabled = false; }
            block = new MaterialPropertyBlock(); Phase = TrialPhase.Ready;
            for (int i = 0; i < 12; i++)
            {
                var go = new GameObject("Scheduled stone impact " + i); go.transform.SetParent(transform, false);
                var source = go.AddComponent<AudioSource>(); source.playOnAwake = false;
                source.spatialBlend = 1; source.minDistance = .2f; source.maxDistance = 3;
                source.rolloffMode = AudioRolloffMode.Linear; source.dopplerLevel = 0;
                audioPool.Add(source); audioUntil.Add(0);
            }
            if (settings.backend == HapticBackend.Simulation) ConnectOutput();
        }
        void OnEnable() { Application.onBeforeRender += BeforeRender; }
        void OnDisable() { Application.onBeforeRender -= BeforeRender; if (Busy) Abort("Experiment disabled"); else StopOutputs(); }
        void Update()
        {
            output?.Heartbeat();
            DrainOutput();
            if (Input.GetKeyDown(KeyCode.Escape)) Abort("Operator stop");
            if (Input.GetKeyDown(KeyCode.F8)) showPanel = !showPanel;
            if (InputDevices.GetDeviceAtXRNode(XRNode.RightHand).TryGetFeatureValue(CommonUsages.secondaryButton, out bool pressed) && pressed && Busy)
                Abort("Controller stop");
            if (Phase != TrialPhase.Induction) return;
            double now = AudioSettings.dspTime;
            if (!TrackingReady) { Abort("Right hand or head tracking lost"); return; }
            if (now - previousFrame > .12) { Abort("Frame gap exceeded 120 ms; restart trial"); return; }
            previousFrame = now;
            if (trial.vibrationEnabled && trial.backend != HapticBackend.XrController && (output == null || !output.Ready))
            { Abort("Haptic connection lost: " + output?.Failure); return; }
            if (trial.vibrationEnabled && trial.backend == HapticBackend.XrController && !ControllerAvailable())
            { Abort("Haptic controller disconnected"); return; }
            try
            {
                while (scheduled < plan.Count && origin + plan[scheduled].contactTime <= now + LookAhead)
                {
                    var hit = plan[scheduled];
                    if (now > origin + hit.contactTime) throw new InvalidOperationException("Missed contact scheduling deadline.");
                    Schedule(hit, now); scheduled++;
                }
                while (contacted < scheduled && origin + plan[contacted].contactTime <= now)
                {
                    var hit = plan[contacted++]; Contacts = hit.id; MarbleAmount = hit.blend;
                    if (trial.vibrationEnabled && trial.backend == HapticBackend.XrController)
                    {
                        var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                        if (!device.SendHapticImpulse(0, trial.amplitude, trial.pulseSeconds))
                            throw new InvalidOperationException("Controller rejected haptic impulse.");
                        controllerPulse = true;
                        Record("controller_impulse_requested", hit.id, "Frequency is controlled by the XR runtime", hit);
                    }
                    Record("contact_frame", hit.id, "", hit);
                }
                if (now - lastPoseLog >= .1) { Record("pose"); lastPoseLog = now; }
                if (now >= end)
                {
                    StopOutputs(); Phase = TrialPhase.PostRatings;
                    ResetRatings(); Record("post_ratings_begin"); Message = "Stimulation stopped. Record the participant's ratings.";
                }
            }
            catch (Exception e) { Abort(e.Message); }
        }
        void LateUpdate()
        {
            if (desktopPreview)
            {
                foreach (var arm in new[] { rig.leftArm, rig.rightArm })
                    TrackedHandsAvatarRig.ApplyControllerPose(arm, new Pose(new Vector3(arm.leftHand ? -.20f : .20f, .79f, .38f), Quaternion.identity), Vector3.zero, Vector3.zero, 0, 0);
            }
            UpdateVisuals();
            if (worldStatus)
            {
                worldStatus.text = Phase == TrialPhase.Induction
                    ? "Rest your right hand on the table.\nWatch the point of contact."
                    : Phase == TrialPhase.PostRatings ? "Stimulation finished.\nPlease report how your hand feels."
                    : "MARBLE HAND / VR\n" + Phase + "  |  " + (desktopPreview ? "Desktop simulation" : GetComponent<PcvrSession>()?.Status);
            }
        }
        [BeforeRenderOrder(20000)] void BeforeRender() { if (block != null) UpdateVisuals(); }
        Vector3 ContactPoint()
        {
            var arm = rig.rightArm; Quaternion frame = arm.HandFrame();
            return Vector3.Lerp(arm.wrist.position, arm.middleBase.position, .62f) + frame * Vector3.up * .016f;
        }
        void UpdateVisuals()
        {
            foreach (var arm in new[] { rig.leftArm, rig.rightArm })
            {
                var frame = arm.HandFrame();
                arm.skin.GetPropertyBlock(block);
                block.SetVector("_Wrist", arm.wrist.position);
                block.SetVector("_Forward", frame * Vector3.forward);
                block.SetFloat("_Marble", arm.leftHand ? 0 : MarbleAmount);
                arm.skin.SetPropertyBlock(block);
            }
            hammer.gameObject.SetActive(Phase == TrialPhase.Induction && TrackingReady && trial != null && trial.visualEnabled);
            if (!hammer.gameObject.activeSelf || plan == null) return;
            double t = AudioSettings.dspTime - origin;
            // Nearest contact controls approach/retraction; the head's lower surface touches the dorsal anchor.
            double distance = double.PositiveInfinity;
            if (contacted < plan.Count) distance = Math.Min(distance, Math.Abs(t - plan[contacted].contactTime));
            if (contacted > 0) distance = Math.Min(distance, Math.Abs(t - plan[contacted - 1].contactTime));
            float u = Mathf.Clamp01((float)distance / .30f);
            float height = .11f * Mathf.SmoothStep(0, 1, u);
            var handFrame = rig.rightArm.HandFrame();
            hammer.SetPositionAndRotation(ContactPoint() + handFrame * Vector3.up * (.018f + height), handFrame);
        }
        void Schedule(MarbleImpact hit, double now)
        {
            double contactDsp = origin + hit.contactTime, soundDsp = origin + hit.soundTime;
            if (trial.soundEnabled && hit.gain > 0)
            {
                int index = audioUntil.FindIndex(t => t < now);
                if (index < 0) throw new InvalidOperationException("Audio pool exhausted.");
                var source = audioPool[index]; source.transform.position = ContactPoint(); source.clip = stoneClips[hit.clip];
                source.volume = trial.volume * hit.gain; source.PlayScheduled(soundDsp);
                AudioScheduleCount++;
                audioUntil[index] = soundDsp + source.clip.length + .05;
            }
            if (trial.vibrationEnabled && trial.backend != HapticBackend.XrController)
                output.Schedule(hit.id, MarbleHapticOutput.Now + contactDsp - AudioSettings.dspTime, trial);
            Record("impact_scheduled", hit.id, "sound=" + trial.soundEnabled + "; visual=" + trial.visualEnabled + "; vibration=" + trial.vibrationEnabled + "; physical onset not measured", hit);
        }
        public void ConnectOutput()
        {
            if (Busy) return;
            if (!settings.vibrationEnabled) { Message = "Enable vibration before connecting output."; return; }
            CloseOutput();
            if (desktopPreview) settings.backend = HapticBackend.Simulation;
            try
            {
                settings.Snapshot();
                connectedBackend = settings.backend; connectedPort = serialPort.Trim();
                if (settings.backend == HapticBackend.XrController)
                { Message = ControllerAvailable() ? "XR controller haptics ready" : "No right controller with impulse support"; return; }
                string port = connectedPort;
                output = new MarbleHapticOutput(settings.backend == HapticBackend.Simulation
                    ? (Func<IMarbleDevice>)(() => new SimulatedDevice()) : () => new Esp32Device(port));
                Message = "Connecting output...";
            }
            catch (Exception e) { Message = e.Message; }
        }
        bool ControllerAvailable()
        { var d = InputDevices.GetDeviceAtXRNode(XRNode.RightHand); return d.isValid && d.TryGetHapticCapabilities(out var caps) && caps.supportsImpulse && caps.numChannels > 0; }
        public void AlignTable()
        {
            if (Busy) return;
            if (!TrackingReady) { Message = "Wait for right hand and headset tracking."; return; }
            var wrist = rig.rightArm.wristTarget.position;
            tableFrame.position = new Vector3(wrist.x - .2f, wrist.y - .04f, wrist.z);
            aligned = true; Message = "Table aligned to the resting right hand. Verify the physical table height.";
        }
        public void StartTrial()
        {
            if (Busy) return;
            if (!aligned || !TrackingReady) { Message = "Align the table with valid tracking first."; return; }
            if (desktopPreview) settings.backend = HapticBackend.Simulation;
            bool outputReady = settings.backend == HapticBackend.XrController ? ControllerAvailable() : output != null && output.Ready;
            if (settings.vibrationEnabled && (!outputReady || connectedBackend != settings.backend || (settings.backend == HapticBackend.Esp32 && serialPort.Trim() != connectedPort)))
            { Message = "Connect the selected output before starting."; return; }
            if (!settings.vibrationEnabled) CloseOutput();
            try
            {
                if (settings.soundEnabled && (stoneClips == null || stoneClips.Length == 0 || stoneClips.Any(c => !c || c.loadState != AudioDataLoadState.Loaded)))
                    throw new InvalidOperationException("Stone audio is missing or not preloaded.");
                trial = settings.Snapshot(); plan = MarbleProtocol.Plan(trial, trial.soundEnabled ? stoneClips.Length : 1);
                CloseLog();
                string folder = Path.Combine(Application.persistentDataPath, "MarbleLogs"); Directory.CreateDirectory(folder);
                string id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                LogPath = Path.Combine(folder, id + ".csv");
                File.WriteAllText(Path.Combine(folder, id + ".json"), JsonUtility.ToJson(trial, true));
                log = new StreamWriter(LogPath, false, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                log.WriteLine("utc,host_seconds,dsp_seconds,phase,event,impact,contact_dsp_target,sound_dsp_target,marble,right_x,right_y,right_z,right_qx,right_qy,right_qz,right_qw,detail");
                Contacts = contacted = scheduled = 0; MarbleAmount = 0; origin = 0;
                Phase = TrialPhase.PreRatings; ResetRatings(); Record("trial_open", 0, "port=" + connectedPort + "; desktop=" + desktopPreview);
                Message = "Record baseline ratings before stimulation.";
            }
            catch (Exception e) { Abort(e.Message); }
        }
        public void SubmitRatings()
        {
            if (Phase != TrialPhase.PreRatings && Phase != TrialPhase.PostRatings) return;
            try
            {
                for (int i = 0; i < ratings.Length; i++) Record("rating", 0, ratingLabels[i] + "=" + F(ratings[i]));
                if (Phase == TrialPhase.PreRatings)
                {
                    if (!TrackingReady) { Abort("Tracking lost before induction"); return; }
                    origin = AudioSettings.dspTime; previousFrame = origin; lastPoseLog = 0;
                    end = origin + MarbleProtocol.EndTime(trial, plan, trial.soundEnabled ? stoneClips.Max(c => c.length) : 0);
                    Phase = TrialPhase.Induction; Record("induction_begin"); Message = "Watch the right hand. ESC / right B stops immediately.";
                }
                else
                {
                    Phase = TrialPhase.Complete; Record("trial_complete"); MarbleAmount = 0;
                    CloseLog(); Message = "Complete. Saved: " + LogPath;
                }
            }
            catch (Exception e) { Abort(e.Message); }
        }
        void ResetRatings() { for (int i = 0; i < ratings.Length; i++) ratings[i] = 5; }
        public void Abort(string reason)
        {
            StopOutputs(); MarbleAmount = 0;
            if (Busy) { Phase = TrialPhase.Aborted; SafeRecord("aborted", reason); }
            CloseLog(); Message = reason;
        }
        public void ResetTrial()
        { Abort("Trial reset"); Phase = TrialPhase.Ready; Contacts = 0; }
        void StopOutputs()
        {
            output?.Stop();
            for (int i = 0; i < audioPool.Count; i++) { if (audioPool[i]) audioPool[i].Stop(); audioUntil[i] = 0; }
            if (controllerPulse) { InputDevices.GetDeviceAtXRNode(XRNode.RightHand).StopHaptics(); controllerPulse = false; }
        }
        void DrainOutput()
        {
            if (output == null) return;
            while (output.TryRead(out var e))
            {
                if (e.name == "pulse_on_written") OutputPulseCount++;
                if (e.name == "device_ready" && !Busy) Message = "Output ready: " + connectedBackend;
                if (e.name == "output_fault") { Abort(e.detail); break; }
                try { Record(e.name, e.impact, e.detail + "; worker_host_seconds=" + F(e.time)); }
                catch (Exception ex) { Abort("Log write failed: " + ex.Message); break; }
            }
        }
        void Record(string name, int id = 0, string detail = "", MarbleImpact? hit = null)
        {
            if (log == null) return;
            var p = rig.rightArm.wristTarget.position; var q = rig.rightArm.wristTarget.rotation;
            log.WriteLine(string.Join(",", new[] { DateTime.UtcNow.ToString("O"), F(MarbleHapticOutput.Now), F(AudioSettings.dspTime), Phase.ToString(), name, id.ToString(),
                hit.HasValue ? F(origin + hit.Value.contactTime) : "", hit.HasValue && trial != null && trial.soundEnabled && hit.Value.gain > 0 ? F(origin + hit.Value.soundTime) : "", F(MarbleAmount),
                F(p.x), F(p.y), F(p.z), F(q.x), F(q.y), F(q.z), F(q.w), "\"" + detail.Replace("\"", "\"\"").Replace("\r", " ").Replace("\n", " ") + "\"" }));
        }
        static string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
        void SafeRecord(string name, string detail) { try { Record(name, 0, detail); } catch (Exception e) { Debug.LogError("Marble log: " + e.Message); } }
        void CloseLog() { try { log?.Dispose(); } catch (Exception e) { Debug.LogError(e.Message); } finally { log = null; } }
        void CloseOutput() { StopOutputs(); output?.Dispose(); output = null; }
        void OnApplicationPause(bool paused) { if (paused && Busy) Abort("Application paused"); }
        void OnApplicationFocus(bool focused) { if (!focused && Busy && !desktopPreview) Abort("Application focus lost"); }
        void OnApplicationQuit() { Shutdown(); }
        void OnDestroy() { Shutdown(); }
        void Shutdown()
        {
            if (destroying) return; destroying = true;
            if (Busy) Abort("Application closing"); CloseOutput(); CloseLog();
        }

        bool showPanel = true;
        void OnGUI()
        {
            if (!showPanel) { if (GUI.Button(new Rect(12, 12, 160, 32), "Operator panel [F8]")) showPanel = true; return; }
            GUILayout.BeginArea(new Rect(12, 12, 340, Screen.height - 24), GUI.skin.box);
            GUILayout.Label("MARBLE HAND / VR EXPERIMENT");
            GUILayout.Label(Phase + "  |  " + (desktopPreview ? "SIMULATION" : "PCVR"));
            GUILayout.Label(Message);
            if (Phase == TrialPhase.Induction) GUILayout.Label("Contacts: " + Contacts + " | Remaining: " + Math.Max(0, end - AudioSettings.dspTime).ToString("F0") + " s");
            if (Phase == TrialPhase.PreRatings || Phase == TrialPhase.PostRatings)
            {
                GUILayout.Label("Participant report: 0 = not at all, 10 = extremely");
                for (int i = 0; i < ratings.Length; i++)
                { GUILayout.Label(ratingLabels[i] + ": " + ratings[i].ToString("F0")); ratings[i] = Mathf.Round(GUILayout.HorizontalSlider(ratings[i], 0, 10)); }
                if (GUILayout.Button(Phase == TrialPhase.PreRatings ? "Save baseline & begin" : "Save post ratings & finish")) SubmitRatings();
            }
            GUI.enabled = !Busy;
            settings.soundEnabled = GUILayout.Toggle(settings.soundEnabled, "Sound: " + (settings.soundEnabled ? "ON" : "OFF"));
            settings.visualEnabled = GUILayout.Toggle(settings.visualEnabled, "Visual (hammer + marble): " + (settings.visualEnabled ? "ON" : "OFF"));
            bool nextVibration = GUILayout.Toggle(settings.vibrationEnabled, "Vibration: " + (settings.vibrationEnabled ? "ON" : "OFF"));
            if (settings.vibrationEnabled && !nextVibration) CloseOutput();
            settings.vibrationEnabled = nextVibration;
            if (GUILayout.Button("Condition: " + settings.condition)) settings.condition = (MarbleCondition)(((int)settings.condition + 1) % 3);
            if (GUILayout.Button("Duration: " + settings.duration + " s (demo / 5 min)")) settings.duration = settings.duration == 300 ? 30 : 300;
            GUI.enabled = !Busy && settings.vibrationEnabled;
            if (GUILayout.Button("Output: " + settings.backend))
            { CloseOutput(); settings.backend = desktopPreview ? HapticBackend.Simulation : (HapticBackend)(((int)settings.backend + 1) % 3); }
            if (settings.backend == HapticBackend.Esp32)
            {
                GUILayout.BeginHorizontal(); GUILayout.Label("Port"); serialPort = GUILayout.TextField(serialPort, 16); GUILayout.EndHorizontal();
                GUILayout.Label("Channel: " + settings.channel); settings.channel = Mathf.RoundToInt(GUILayout.HorizontalSlider(settings.channel, 0, 15));
                GUILayout.Label("Frequency: " + settings.frequency.ToString("F0") + " Hz"); settings.frequency = Mathf.Round(GUILayout.HorizontalSlider(settings.frequency, 20, 300));
            }
            GUILayout.Label("Pulse: " + (settings.pulseSeconds * 1000).ToString("F0") + " ms | amplitude: " + settings.amplitude.ToString("F2"));
            settings.pulseSeconds = Mathf.Round(GUILayout.HorizontalSlider(settings.pulseSeconds, .01f, .2f) * 1000) / 1000;
            settings.amplitude = GUILayout.HorizontalSlider(settings.amplitude, 0, .3f);
            if (GUILayout.Button("CONNECT OUTPUT")) ConnectOutput();
            GUI.enabled = !Busy && settings.soundEnabled;
            GUILayout.Label("Audio gain: " + settings.volume.ToString("F2")); settings.volume = GUILayout.HorizontalSlider(settings.volume, 0, 1);
            GUI.enabled = !Busy;
            if (GUILayout.Button("ALIGN TABLE")) AlignTable();
            if (GUILayout.Button("START TRIAL")) StartTrial();
            GUI.enabled = true;
            if (GUILayout.Button("STOP / RESET [ESC]")) ResetTrial();
            GUILayout.Label("F8 hides panel. Right controller B stops.\nPhysical onset requires measurement; software timestamps are logged.");
            GUILayout.EndArea();
        }
    }
}
