using System;
using System.Collections.Generic;
using System.Globalization;
using AvatarExperiments.FpsArms;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.Rendering.Universal;

namespace TendonHand
{
    [DefaultExecutionOrder(10000)]
    public sealed class TendonHandExperiment : MonoBehaviour
    {
        public TrackedHandsAvatarRig rig;
        public Transform tableFrame, leftMarker, rightMarker;
        public Text instructionText, statusText;
        public GameObject menu;
        public TendonSettings settings = new TendonSettings();
        public bool desktopPreview, dryRun = true, writeLogs = true;
        public string portName = "COM4";
        public TendonProtocol Protocol { get; } = new TendonProtocol();
        public bool TrackingReady => rig && (desktopPreview || (rig.HeadTracked && rig.leftArm.tracked && rig.rightArm.tracked)) &&
            Finite(rig.leftArm.wristTarget.position) && Finite(rig.rightArm.wristTarget.position);
        public bool Calibrated { get; private set; }
        public bool OutputReady => output != null && output.Ready;
        public bool OutputActive => output != null && output.Active;
        public string Notice { get; private set; } = "Align hands; connect simulated output to preview.";
        public string LastLogPath => log.Path;
        public Pose DisplayedPose { get; private set; }
        public Pose BaselinePose => baseline;
        public Vector3 Pivot => pivot;
        readonly TendonLog log = new TendonLog();
        readonly List<XRInputSubsystem> origins = new List<XRInputSubsystem>();
        readonly float[] probeTimes = { 0, 2, 5, 10, 20, 30, 60 };
        TendonOutput output;
        Pose baseline;
        Vector3 pivot, axis, originPosition;
        Quaternion originRotation;
        bool initialized, connectedDryRun, leftController, rightController;
        string connectedPort, reportText = "", probeLabel = "";
        MaterialPropertyBlock block;
        Vector2 operatorScroll;
        float nextSample, nextLabels;
        int nextProbe, pendingStartId = -1, pendingStopId = -1;
        TrackedArmAvatar Target => (Protocol.Phase != TendonPhase.Ready ? Protocol.Settings.targetLeft : settings.targetLeft) ? rig.leftArm : rig.rightArm;

        void Awake()
        {
            desktopPreview |= Array.Exists(Environment.GetCommandLineArgs(), a => a == "--desktop");
            // Desktop mode never drives real hardware, including command-line smoke tests.
            if (desktopPreview) dryRun = true;
            var calibration = rig.GetComponent<ArmLengthCalibration>(); if (calibration) calibration.enabled = false;
            if (desktopPreview)
            {
                rig.enabled = false;
                rig.head.GetUniversalAdditionalCameraData().allowXRRendering = false;
                rig.head.transform.SetPositionAndRotation(new Vector3(0,1.38f,-.72f), Quaternion.Euler(25,0,0));
                var head = new GameObject("Preview body head").transform;
                head.SetParent(transform, false); head.position = new Vector3(0,1.25f,.08f);
                rig.leftArm.head = rig.rightArm.head = head;
                PosePreview();
            }
            foreach (var arm in new[] {rig.leftArm, rig.rightArm})
            {
                arm.skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                arm.skin.receiveShadows = false; arm.skin.updateWhenOffscreen = true;
                var bounds = arm.skin.localBounds; bounds.Expand(2); arm.skin.localBounds = bounds;
            }
            Initialize();
        }
        public void Initialize()
        {
            if (initialized) return;
            Protocol.Changed += PhaseChanged; initialized = true;
        }
        void Start() { if (desktopPreview) Align(); UpdateLabels(); }
        void OnEnable() { Application.onBeforeRender += BeforeRender; }
        void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            Abort("component_disabled"); Disconnect();
            if (rig) Restore();
        }
        void OnDestroy()
        {
            Protocol.Changed -= PhaseChanged;
            foreach (var input in origins) input.trackingOriginUpdated -= Recentered;
            CloseLog();
        }
        void OnApplicationFocus(bool focus) { if (!focus && !desktopPreview) Abort("focus_lost"); }
        void OnApplicationPause(bool paused) { if (paused) Abort("application_paused"); }
        void Recentered(XRInputSubsystem input) { Abort("runtime_recentered"); Calibrated = false; }

        void Update()
        {
            output?.Heartbeat(); PollOutput();
            if (Input.GetKeyDown(KeyCode.Escape) || Input.GetKeyDown(KeyCode.R)) ResetTrial();
            if (Input.GetKeyDown(KeyCode.Space)) StartTrial();
            if (Input.GetKeyDown(KeyCode.F)) Align();
            if (Input.GetKeyDown(KeyCode.C)) Connect();
            if (Input.GetKeyDown(KeyCode.H)) StopInduction();
            if (Input.GetKeyDown(KeyCode.F8)) ToggleMenu();
            if (Input.GetKeyDown(KeyCode.Return)) RecordReport();
            if (Protocol.Active)
            {
                if (!TrackingReady) Abort("tracking_lost");
                else if (!desktopPreview && (leftController != rig.LeftUsesController || rightController != rig.RightUsesController)) Abort("tracking_source_changed");
                else if (Vector3.Distance(originPosition, rig.trackingSpace.position) > .01f || Quaternion.Angle(originRotation, rig.trackingSpace.rotation) > 1) Abort("origin_changed");
                else if (Time.unscaledDeltaTime > .5f) Abort("frame_gap");
                else if (Vector3.Distance(Target.wristTarget.position, baseline.position) > Protocol.Settings.movementToleranceMetres ||
                    Quaternion.Angle(Target.wristTarget.rotation, baseline.rotation) > Protocol.Settings.rotationToleranceDegrees) Abort("target_moved");
                else if (!OutputReady) Abort("output_not_ready");
                else if ((Protocol.Phase == TendonPhase.Starting || Protocol.Phase == TendonPhase.Stopping) && Protocol.PhaseTime > 2) Abort("output_transition_timeout");
                else Protocol.Tick(Time.unscaledDeltaTime);
            }
            if (Protocol.Phase == TendonPhase.Recovery)
                while (nextProbe < probeTimes.Length && probeTimes[nextProbe] <= Protocol.PhaseTime)
                {
                    probeLabel = "Report cue: " + probeTimes[nextProbe].ToString("F0") + " s after stop write";
                    Record("report_cue", probeLabel); nextProbe++;
                }
            if (desktopPreview) PosePreview();
            if (Time.unscaledTime >= nextLabels) { nextLabels = Time.unscaledTime + .1f; UpdateLabels(); }
        }
        void LateUpdate()
        {
            ApplyVisuals();
            if (Protocol.Active && Time.unscaledTime >= nextSample)
            { nextSample = Time.unscaledTime + 1f/30; Record("sample"); }
        }
        [BeforeRenderOrder(10000)]
        void BeforeRender() { if (Protocol.Active && !TrackingReady) Abort("tracking_lost_before_render"); ApplyVisuals(); }
        void PosePreview()
        {
            foreach (var arm in new[] {rig.leftArm, rig.rightArm})
                TrackedHandsAvatarRig.ApplyControllerPose(arm, new Pose(new Vector3(arm.leftHand ? -.2f : .2f, .79f, .38f), Quaternion.identity), Vector3.zero, Vector3.zero, 0, 0);
        }
        public void Align()
        {
            Initialize();
            if (Protocol.Active || !TrackingReady) { Notice = "Wait for both hands and headset; stop trial before aligning."; return; }
            var forward = Vector3.ProjectOnPlane(rig.head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) { Notice = "Look forwards to align."; return; }
            tableFrame.SetPositionAndRotation((rig.leftArm.wristTarget.position + rig.rightArm.wristTarget.position)*.5f - Vector3.up*.04f, Quaternion.LookRotation(forward));
            foreach (var pair in new[] { (leftMarker,rig.leftArm), (rightMarker,rig.rightArm) })
                pair.Item1.position = new Vector3(pair.Item2.wristTarget.position.x, tableFrame.position.y+.004f, pair.Item2.wristTarget.position.z);
            Calibrated = true; Notice = "Aligned. Keep the stimulated hand still.";
        }
        public void Connect()
        {
            if (Protocol.Active) return;
            Disconnect();
            if (desktopPreview) dryRun = true;
            connectedDryRun = dryRun; connectedPort = portName;
            if (!dryRun && Esp32Device.ClientType == null) { Notice = "Run Tools/Import-ESP32.ps1 with the controller repository path."; return; }
            var port = portName;
            output = new TendonOutput(dryRun ? (Func<ITendonDevice>)(() => new SimulatedDevice()) : () => new Esp32Device(port));
            Notice = dryRun ? "Connecting simulated output..." : "Connecting " + port + "; outputs muted...";
        }
        public void Disconnect()
        {
            if (Protocol.Active) Abort("disconnect");
            var old = output; output = null;
            try { old?.Dispose(); } catch (Exception e) { Notice = e.Message; Debug.LogError(e); }
            if (old != null) while (old.TryRead(out var item)) Record(item.name, item.detail, null, item.time);
            CloseLog();
        }
        public void PollOutput()
        {
            if (output == null) return;
            while (output.TryRead(out var item))
            {
                Record(item.name, "command=" + item.commandId + "; " + item.detail, null, item.time);
                if (item.name == "connected") Notice = connectedDryRun ? "Simulation ready; no hardware output." : "ESP32 state received; ready, muted.";
                if (item.name == "start_write_complete" && item.commandId == pendingStartId) Protocol.OutputStarted();
                if (item.name == "stop_write_complete" && item.commandId == pendingStopId) Protocol.OutputStopped();
                if (item.name == "output_fault" || item.name == "stop_write_failed") Abort(item.name + ": " + item.detail);
                if (!Protocol.Active &&
                    (item.name == "stop_write_complete" || item.name == "final_stop_write_complete" || item.name == "stop_write_failed")) CloseLog();
            }
            if (output.Failure != null) { Abort("output_fault"); Notice = output.Failure; }
        }
        public void StartTrial()
        {
            Initialize();
            if (Protocol.Active) return;
            PollOutput();
            if (!Calibrated || !TrackingReady) { Notice = "Align tracked hands first (F)."; return; }
            if (!OutputReady || connectedDryRun != dryRun || connectedPort != portName) { Notice = "Connect the selected output first (C)."; return; }
            var s = settings.Snapshot();
            if (s.vibration && !dryRun && s.amplitude <= 0) { Notice = "Vibration condition needs a nonzero amplitude, or disable vibration."; return; }
            var arm = s.targetLeft ? rig.leftArm : rig.rightArm;
            baseline = new Pose(arm.wristTarget.position, arm.wristTarget.rotation);
            // Forearm pivot is an operator-set geometric reference, not a tracked elbow.
            axis = tableFrame.right;
            pivot = baseline.position - tableFrame.forward * (s.joint == PivotJoint.Forearm ? s.forearmMetres : 0);
            originPosition = rig.trackingSpace.position; originRotation = rig.trackingSpace.rotation;
            leftController = rig.LeftUsesController; rightController = rig.RightUsesController;
            foreach (var input in origins) input.trackingOriginUpdated -= Recentered;
            SubsystemManager.GetSubsystems(origins);
            foreach (var input in origins) input.trackingOriginUpdated += Recentered;
            try { if (writeLogs) log.Open(s, desktopPreview, dryRun, portName, baseline, pivot, axis); }
            catch (Exception e) { Notice = "Cannot open trial CSV: " + e.Message; CloseLog(); return; }
            nextProbe = 0; probeLabel = reportText = "";
            Protocol.Start(s); menu.SetActive(false); nextSample = Time.unscaledTime;
        }
        public void StopInduction()
        { if (Protocol.Phase == TendonPhase.Induction) { Record("operator_stop_early"); Protocol.StopInduction(); } }
        public void ResetTrial()
        {
            Abort("operator_reset"); output?.Stop(); Protocol.Reset(); Restore(); menu.SetActive(true);
            Notice = "Reset. Align again before the next trial."; Calibrated = false;
        }
        public void Abort(string reason)
        {
            output?.Stop();
            if (!Protocol.Active) return;
            Record("abort_requested", reason); Protocol.Abort(); Calibrated = false;
            menu.SetActive(true); Notice = "Stopped: " + reason; Restore();
        }
        void PhaseChanged(TendonPhase phase)
        {
            ApplyVisuals(); Record("phase_" + phase);
            switch (phase)
            {
                case TendonPhase.Starting:
                    Record("start_requested", Protocol.Settings.vibration ? "stimulus" : "no_vibration_control");
                    try { if (Protocol.Settings.vibration) pendingStartId = output.Start(Protocol.Settings); else Protocol.OutputStarted(); }
                    catch (Exception e) { Abort("start_failed: " + e.Message); }
                    break;
                case TendonPhase.Stopping:
                    Record("stop_requested");
                    if (Protocol.Settings.vibration) pendingStopId = output.Stop(); else Protocol.OutputStopped();
                    break;
                case TendonPhase.Recovery: nextProbe = 0; Record("recovery_visual_applied", Protocol.Settings.recovery.ToString()); break;
                case TendonPhase.Complete:
                    output?.Stop(); menu.SetActive(true); Notice = "Complete. Hands restored; CSV saved."; break;
            }
            UpdateLabels();
        }
        public void RecordReportedAngle(float angle)
        {
            if (!Protocol.Active || float.IsNaN(angle) || float.IsInfinity(angle)) return;
            Record("reported_angle", "operator_entered_verbal_report", angle);
            Notice = "Recorded felt angle: " + angle.ToString("F1") + " deg";
        }
        public void RecordReport()
        {
            if (float.TryParse(reportText, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle) && angle >= -180 && angle <= 180)
            { RecordReportedAngle(angle); reportText = ""; }
            else Notice = "Enter reported angle in degrees (-180..180).";
        }
        void Record(string name, string detail = "", float? report = null, double? time = null)
        {
            if (!log.IsOpen || !rig) return;
            try
            {
                var arm = Target;
                log.Write(name, Protocol, new Pose(arm.wristTarget.position, arm.wristTarget.rotation), DisplayedPose,
                    TrackingReady, desktopPreview ? "simulation" : (arm.leftHand ? rig.LeftUsesController : rig.RightUsesController) ? "controller" : "hand",
                    OutputActive, detail, report, time);
            }
            catch (Exception e)
            {
                CloseLog(); output?.Stop(); Protocol.Abort(); Calibrated = false;
                Notice = "Trial stopped: log write failed: " + e.Message; menu.SetActive(true);
            }
        }
        void CloseLog() { try { log.Dispose(); } catch (Exception e) { Notice = "Log close failed: " + e.Message; } }
        public void ApplyVisuals()
        {
            if (!rig) return;
            foreach (var arm in new[] {rig.leftArm, rig.rightArm})
            {
                Pose raw = new Pose(arm.wristTarget.position, arm.wristTarget.rotation);
                bool target = arm == Target;
                Pose visual = target && !Protocol.UsesLivePose ? TendonProtocol.DisplayPose(baseline, pivot, axis, Protocol.Angle) : raw;
                if (target) DisplayedPose = visual;
                SetVisual(arm, visual, !target || Protocol.TargetVisible);
            }
            bool showMarkers = Calibrated && (!Protocol.Active || Protocol.Phase == TendonPhase.Baseline);
            if (leftMarker) leftMarker.gameObject.SetActive(showMarkers);
            if (rightMarker) rightMarker.gameObject.SetActive(showMarkers);
        }
        void SetVisual(TrackedArmAvatar arm, Pose visual, bool visible)
        {
            block = block ?? new MaterialPropertyBlock(); arm.skin.GetPropertyBlock(block);
            var rawFrame = arm.HandFrame();
            var rotation = visual.rotation * Quaternion.Inverse(rawFrame);
            block.SetVector("_Wrist", arm.wrist.position);
            block.SetVector("_Forward", rawFrame * Vector3.forward);
            block.SetVector("_Shift", visual.position - arm.wrist.position);
            block.SetVector("_Rotation", new Vector4(rotation.x, rotation.y, rotation.z, rotation.w));
            block.SetFloat("_Visible", visible && (desktopPreview || arm.tracked) ? 1 : 0);
            arm.skin.SetPropertyBlock(block);
        }
        void Restore()
        {
            foreach (var arm in new[] {rig.leftArm, rig.rightArm})
                SetVisual(arm, new Pose(arm.wristTarget.position, arm.wristTarget.rotation), true);
        }
        public void ToggleMenu() { menu.SetActive(!menu.activeSelf); }
        public void ToggleTarget() { if (!Protocol.Active) settings.targetLeft = !settings.targetLeft; }
        public void CycleRecovery() { if (!Protocol.Active) settings.recovery = (RecoveryView)(((int)settings.recovery+1)%3); }
        public void ToggleVibration() { if (!Protocol.Active) settings.vibration = !settings.vibration; }
        public void UpdateLabels()
        {
            if (instructionText) instructionText.text = Protocol.Active ? "Keep the stimulated hand still.\nReport its felt angle when asked." : "Place both hands comfortably.\nAlign, connect output, then start.";
            if (statusText) statusText.text = "TENDON HAND  |  " + (dryRun ? "SIMULATION" : portName) + "\n" +
                (settings.targetLeft ? "LEFT" : "RIGHT") + "  |  " + settings.recovery + "  |  " + Protocol.Phase;
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(12,12,360,Screen.height-24),GUI.skin.box);
            operatorScroll = GUILayout.BeginScrollView(operatorScroll);
            GUILayout.Label("TENDON HAND / FPS ARMS");
            GUILayout.Label(desktopPreview ? "DESKTOP SIMULATION" : "PCVR / " + (dryRun ? "SIMULATED OUTPUT" : "ESP32"));
            GUILayout.Label(Protocol.Phase + "  " + Protocol.PhaseTime.ToString("F1") + " s  |  " + (OutputActive ? "OUTPUT ON" : "OUTPUT OFF"));
            GUILayout.Label(Notice);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("F Align")) Align(); if (GUILayout.Button("SPACE Start")) StartTrial();
            GUILayout.EndHorizontal();
            if (GUILayout.Button("ESC Stop / restore")) ResetTrial();
            if (GUILayout.Button("H Stop induction; begin recovery")) StopInduction();
            GUI.enabled = !Protocol.Active;
            if (GUILayout.Button("Target: " + (settings.targetLeft ? "LEFT" : "RIGHT"))) ToggleTarget();
            if (GUILayout.Button("Recovery: " + settings.recovery)) CycleRecovery();
            settings.vibration = GUILayout.Toggle(settings.vibration, "Tendon vibration during induction");
            settings.visualMotion = GUILayout.Toggle(settings.visualMotion, "Virtual hand motion during induction");
            if (GUILayout.Button("Pivot: " + settings.joint)) settings.joint = settings.joint == PivotJoint.Forearm ? PivotJoint.Wrist : PivotJoint.Forearm;
            Slider("Angle (deg)", ref settings.angleDegrees, -60, 60);
            if (settings.joint == PivotJoint.Forearm) Slider("Forearm radius (m)", ref settings.forearmMetres, .15f, .4f);
            Slider("Baseline (s)", ref settings.baselineSeconds, .2f, 120);
            Slider("Induction (s)", ref settings.inductionSeconds, .5f, 120);
            Slider("Recovery (s)", ref settings.recoverySeconds, .5f, 300);
            Slider("Frequency (Hz)", ref settings.frequencyHz, 1, 150);
            Slider("Amplitude (0..1; not mm)", ref settings.amplitude, 0, 1);
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("CH -")) settings.channel = Mathf.Max(0,settings.channel-1);
            GUILayout.Label("Channel " + settings.channel);
            if (GUILayout.Button("CH +")) settings.channel = Mathf.Min(15,settings.channel+1);
            GUILayout.EndHorizontal();
            GUI.enabled = !Protocol.Active && !desktopPreview;
            dryRun = GUILayout.Toggle(dryRun,"Simulated output (no hardware)");
            portName = GUILayout.TextField(portName);
            GUI.enabled = !Protocol.Active;
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("C Connect")) Connect(); if (GUILayout.Button("Disconnect")) Disconnect();
            GUILayout.EndHorizontal(); GUI.enabled = true;
            GUILayout.Label(probeLabel);
            GUILayout.BeginHorizontal(); reportText = GUILayout.TextField(reportText);
            if (GUILayout.Button("Record felt deg")) RecordReport(); GUILayout.EndHorizontal();
            if (GUILayout.Button("F8 Headset menu")) ToggleMenu();
            if (!string.IsNullOrEmpty(log.Path)) GUILayout.Label(System.IO.Path.GetFileName(log.Path));
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }
        static void Slider(string label, ref float value, float low, float high)
        { GUILayout.Label(label + ": " + value.ToString("F2")); value = GUILayout.HorizontalSlider(value, low, high); }
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) || float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));
    }
}
