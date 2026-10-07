using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

namespace AvatarExperiments.VirtualHand
{
    /// <summary>A stationary virtual hand, a tracked physical brush, and independently controlled vibration.</summary>
    [DisallowMultipleComponent]
    public sealed class TabletopExperiment : MonoBehaviour
    {
        public TrialSettings settings = new TrialSettings();
        [Tooltip("Use a pseudonymous code, not a participant name.")]
        public string participantCode = "P001", sessionCode = "S01", conditionCode = "";
        public TabletopTrackedBrush trackedBrush;
        public TabletopMeltHand hand;
        public TabletopConditionVibration vibration;
        public TabletopHandPlacement placement;
        public VirtualHandStudy study;
        public Transform ball, realHandAnchor;
        [Min(.001f)] public float ballRadius = .012f;
        public bool writeLog = true;
        public bool keyboardControls = true, controllerControls = true;
        public bool desktopPreview;
        public TrialPhase Phase { get; private set; } = TrialPhase.Ready;
        public double Elapsed { get; private set; }
        public bool Running => Phase >= TrialPhase.Preparation && Phase <= TrialPhase.Observation;
        public bool UsesTrackedBrush => true;
        public string Status { get; private set; } = "Calibrate the brush tip and three hand landmarks.";
        public string LogPath { get; private set; }
        public string TrialId { get; private set; }
        public bool UseRealtimeClock { get; set; } = true;
        public TrialSettings ActiveSettings => trial?.Copy();
        public string LogDirectoryOverride { get; set; }
        public event Action<string, double> EventDispatched;
        TrialSettings trial;
        StreamWriter log;
        double started;
        bool aHeld, bHeld;

        [Serializable]
        sealed class TrialRecord
        {
            public int schemaVersion = 1;
            public string trialId, participantCode, sessionCode, conditionCode, utcStart;
            public string unityVersion, applicationVersion, platform, input;
            public string vibrationPort, vibrationChannels;
            public float ballRadius, widthScale, lengthScale, thickness, response;
            public TrialSettings settings;
        }
        void Start()
        {
            if (desktopPreview) gameObject.AddComponent<TabletopDesktopPreview>().Initialize(this);
        }
        public void StartTrial()
        {
            if(study&&study.Active&&!study.AllowStimulusStart){Status="Follow the study stages before starting stimuli.";return;}
            if (Running) return;
            if (Phase != TrialPhase.Ready) { Status = "Reset before the next trial."; return; }
            try
            {
                trial = settings.Copy(); trial.ValidateTracked();
                if (!trackedBrush || !trackedBrush.Ready) { Status = "Calibrate the brush tip and three hand landmarks first."; return; }
                if (!hand || !ball || !realHandAnchor) throw new InvalidOperationException("Experiment references are missing.");
                if(placement)placement.Apply(trial.handOffset,trial.handYaw);
                hand.ValidateConfiguration(trial.appearance);
                if (trial.vibration != VibrationCondition.None && (!vibration || !vibration.CanBegin(trial)))
                { Status = "Connect ESP32 before starting a vibration condition."; return; }
                if (!float.IsFinite(ballRadius) || ballRadius <= 0) throw new ArgumentException("Invalid brush sphere radius.");
                TrialId = DateTime.UtcNow.ToString("yyyyMMddTHHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
                Elapsed = 0; LogPath = null;
                hand.Select(trial.appearance); ball.gameObject.SetActive(false);
                CloseLog();
                if (writeLog)
                {
                    string directory = LogDirectoryOverride ?? Path.Combine(Application.persistentDataPath, "VirtualHandLogs");
                    Directory.CreateDirectory(directory);
                    LogPath = Path.Combine(directory, TrialId + ".csv");
                    log = new StreamWriter(LogPath, false);
                    log.WriteLine("trial,event,phase,planned_s,dispatch_s,realtime_s,melt_amount");
                    File.WriteAllText(Path.ChangeExtension(LogPath, ".json"), JsonUtility.ToJson(new TrialRecord
                    {
                        trialId = TrialId, participantCode = participantCode, sessionCode = sessionCode, conditionCode = conditionCode,
                        utcStart = DateTime.UtcNow.ToString("O"), unityVersion = Application.unityVersion,
                        applicationVersion = Application.version, platform = Application.platform.ToString(),
                        input = desktopPreview ? "Mouse preview; no physical contact measurement" : "Right XR controller; physical contact is not sensed",
                        vibrationPort = vibration ? vibration.portName : "", vibrationChannels = vibration ? vibration.channelList : "",
                        ballRadius = ballRadius, widthScale = hand.finalWidthScale, lengthScale = hand.finalLengthScale,
                        thickness = hand.finalThickness, response = hand.flowResponse, settings = trial
                    }, true));
                }
                Phase = TrialPhase.Preparation;
                if (!trackedBrush.BeginSession(trial)) throw new InvalidOperationException(trackedBrush.Status);
                // Start the common clock after file setup, immediately before output is scheduled.
                started = Time.realtimeSinceStartupAsDouble;
                if (vibration && !vibration.Begin(trial)) throw new InvalidOperationException(vibration.Status);
                Record("trial_start", 0);
                Status = "Trial running";
            }
            catch (Exception error) { AbortTrial("Start failed: " + error.Message); }
        }
        void Update()
        {
            if (!UseRealtimeClock) return;
            var keyboard = Keyboard.current;
            if (keyboardControls && keyboard != null)
            {
                if (keyboard.spaceKey.wasPressedThisFrame) StartTrial();
                if (keyboard.escapeKey.wasPressedThisFrame) AbortTrial();
                if (keyboard.rKey.wasPressedThisFrame) ResetTrial();
            }
            if (controllerControls)
            {
                var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
                bool controller = device.isValid && (device.characteristics & InputDeviceCharacteristics.Controller) != 0;
                bool a = controller && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.primaryButton, out bool primary) && primary;
                bool b = controller && device.TryGetFeatureValue(UnityEngine.XR.CommonUsages.secondaryButton, out bool secondary) && secondary;
                if (a && !aHeld) StartTrial(); if (b && !bHeld) AbortTrial(); aHeld = a; bHeld = b;
            }
            if (Running) Advance(Time.realtimeSinceStartupAsDouble - started);
        }
        public void Advance(double elapsed)
        {
            if (!Running) return;
            try
            {
                if (!double.IsFinite(elapsed) || elapsed < Elapsed) throw new ArgumentException("Trial clock moved backwards or became invalid.");
                Elapsed = elapsed;
                var next = TrialTimeline.Phase(trial, elapsed);
                if (next != Phase)
                {
                    // Record every crossed boundary, including skipped phases after a stalled frame.
                    while (Phase < next)
                    {
                        Phase++;
                        double planned = Phase == TrialPhase.Induction ? trial.preparation : Phase == TrialPhase.Melting ? trial.MeltOnset :
                            Phase == TrialPhase.Observation ? trial.MeltOnset + trial.melting : trial.End;
                        Record("phase", planned);
                    }
                }
                if (Phase == TrialPhase.Induction) trackedBrush.Render(UseRealtimeClock ? Time.realtimeSinceStartupAsDouble : trackedBrush.SampleTime);
                else ball.gameObject.SetActive(false);
                if (Phase == TrialPhase.Aborted) return;
                hand.SetAmount(trial.enableMelting ? Mathf.SmoothStep(0, 1, Mathf.Clamp01((float)((elapsed - trial.MeltOnset) / trial.melting))) : 0);
                if (vibration) vibration.Tick(elapsed);
                if (Phase == TrialPhase.Completed)
                {
                    StopOutputs("trial_complete"); Record("trial_complete", trial.End); CloseLog();
                    Status = "Completed. Reset before the next trial.";
                }
            }
            catch (Exception error) { AbortTrial("Trial failed: " + error.Message); }
        }
        public void BrushTrackingFailed(string reason) => AbortTrial(reason);
        public void AbortTrial() => AbortTrial("Operator abort");
        public void AbortTrial(string reason)
        {
            bool record = Running || log != null;
            StopOutputs("abort");
            if (record)
            {
                Phase = TrialPhase.Aborted;
                try { Record("aborted", Elapsed); } catch (Exception error) { Debug.LogWarning("Trial log error: " + error.Message); }
            }
            CloseLog(); Status = reason;
        }
        public void ResetTrial()
        {
            AbortTrial("Reset");
            if (hand) hand.ResetShape();
            Phase = TrialPhase.Ready; Elapsed = 0; Status = "Ready — check alignment before starting.";
        }
        void StopOutputs(string reason)
        {
            // Stop hardware first, even if a file writer fails while closing.
            if (vibration) vibration.Stop(reason);
            if (ball) ball.gameObject.SetActive(false);
            if (trackedBrush) trackedBrush.EndSession();
        }
        void Record(string kind, double planned)
        {
            string F(double n) => n.ToString("F6", CultureInfo.InvariantCulture);
            log?.WriteLine(string.Join(",", TrialId, kind, Phase, F(planned), F(Elapsed), F(Time.realtimeSinceStartupAsDouble), F(hand ? hand.Amount : 0)));
            log?.Flush(); EventDispatched?.Invoke(kind, Elapsed);
        }
        void CloseLog()
        {
            try { log?.Dispose(); } catch (Exception error) { Debug.LogWarning("Trial log close failed: " + error.Message); }
            finally { log = null; }
        }
        void OnDisable() { AbortTrial("Experiment disabled"); }
        void OnApplicationPause(bool paused) { if (paused) AbortTrial("Application paused"); }
        void OnApplicationQuit() { AbortTrial("Application quit"); }
    }
}
