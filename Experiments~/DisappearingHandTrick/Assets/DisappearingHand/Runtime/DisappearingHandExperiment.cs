using System;
using System.Collections.Generic;
using AvatarExperiments.FpsArms;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.Hands;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace DisappearingHand
{
    [DefaultExecutionOrder(10000)]
    public sealed class DisappearingHandExperiment : MonoBehaviour
    {
        public TrackedHandsAvatarRig rig;
        public Transform tableFrame, tableTop, leftMarker, rightMarker;
        public Text instructionText, statusText;
        public GameObject menu;
        public TrialSettings settings = new TrialSettings();
        public bool desktopPreview;
        public bool writeLogs = true;
        public TrialProtocol Protocol { get; } = new TrialProtocol();
        public bool TrackingReady => desktopPreview || (rig && rig.HeadTracked && rig.leftArm.tracked && rig.rightArm.tracked &&
            Finite(rig.leftArm.wristTarget.position) && Finite(rig.rightArm.wristTarget.position));
        public string Notice { get; private set; } = "Place both hands on the table, then align the table.";
        public string LastLogPath => log.Path;
        public bool IsCalibrated { get; private set; }
        readonly TrialLog log = new TrialLog();
        readonly List<XRInputSubsystem> originInputs = new List<XRInputSubsystem>();
        MaterialPropertyBlock block;
        Vector3 initialLeft, initialRight, previewLeft, previewRight, originPosition;
        Quaternion originRotation;
        bool priorStart, priorConfirm, priorMenu, priorReset, trialLeftController, trialRightController;
        float leftPinchTime, rightPinchTime, nextSample, nextUI;
        bool previewSearchMoved;

        void Awake()
        {
            desktopPreview |= Array.Exists(Environment.GetCommandLineArgs(), a => a == "--desktop");
            // Arm calibration gestures would conflict with the experiment's pinch controls.
            var calibration = rig.GetComponent<ArmLengthCalibration>();
            if (calibration) calibration.enabled = false;
            if (desktopPreview)
            {
                rig.enabled = false;
                rig.head.GetUniversalAdditionalCameraData().allowXRRendering=false;
                rig.head.transform.SetPositionAndRotation(new Vector3(0,1.38f,-.72f), Quaternion.Euler(25,0,0));
                var bodyHead=new GameObject("Preview body head").transform;
                bodyHead.SetParent(transform,false);bodyHead.position=new Vector3(0,1.25f,.08f);
                rig.leftArm.head=rig.rightArm.head=bodyHead;
                previewLeft = new Vector3(-.20f,.79f,.38f); previewRight = new Vector3(.20f,.79f,.38f);
                PosePreview();
            }
            foreach (var arm in new[] {rig.leftArm,rig.rightArm})
            {
                arm.hideWhenUntracked = true;
                arm.skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                arm.skin.receiveShadows = false;
                arm.skin.updateWhenOffscreen = true;
                // Vertex displacement must not be culled at the unshifted bounds.
                var bounds = arm.skin.localBounds; bounds.Expand(2); arm.skin.localBounds = bounds;
            }
            Protocol.Changed += OnPhaseChanged;
        }
        void Start() { if (desktopPreview) AlignTable(); UpdateLabels(); }
        void OnEnable() { Application.onBeforeRender += BeforeRender; }
        void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            Abort("component_disabled"); log.Dispose();
            // Clear properties even when disabled midway through disappearance.
            if (rig) { ApplyHand(rig.leftArm, Vector3.zero, true); ApplyHand(rig.rightArm, Vector3.zero, true); }
        }
        void OnDestroy()
        {
            Protocol.Changed -= OnPhaseChanged; log.Dispose();
            foreach (var input in originInputs) input.trackingOriginUpdated -= OriginUpdated;
        }
        void OriginUpdated(XRInputSubsystem input)
        { Abort("runtime_recentered"); IsCalibrated=false; }
        void OnApplicationFocus(bool focused) { if (!focused && !desktopPreview) Abort("application_focus_lost"); }
        void OnApplicationPause(bool paused) { if (paused) Abort("application_paused"); }

        void Update()
        {
            ReadInput();
            if (Protocol.Active)
            {
                if (!TrackingReady) Abort("tracking_lost");
                else if (!desktopPreview && (trialLeftController != rig.LeftUsesController || trialRightController != rig.RightUsesController)) Abort("input_source_changed");
                else if (Vector3.Distance(originPosition,rig.trackingSpace.position) > .01f || Quaternion.Angle(originRotation,rig.trackingSpace.rotation) > 1) Abort("tracking_origin_changed");
                else if (Time.unscaledDeltaTime > .5f) Abort("frame_gap");
                else Protocol.Tick(Time.unscaledDeltaTime);
            }
            if (desktopPreview) UpdatePreview();
            if (Protocol.Active && Time.unscaledTime >= nextSample)
            { nextSample = Time.unscaledTime + 1f/30; Record("sample"); }
            if (Time.unscaledTime >= nextUI) { nextUI = Time.unscaledTime + .1f; UpdateLabels(); }
        }
        void LateUpdate() { ApplyVisuals(); }
        [BeforeRenderOrder(10000)]
        void BeforeRender()
        {
            if (Protocol.Active && !TrackingReady) Abort("tracking_lost_before_render");
            ApplyVisuals();
        }

        public void AlignTable()
        {
            if (Protocol.Active) { Notice = "Reset the trial before moving the table."; return; }
            if (!TrackingReady) { Notice = "Both hands and the headset must be tracked."; return; }
            Vector3 left = rig.leftArm.wristTarget.position, right = rig.rightArm.wristTarget.position;
            Vector3 forward = Vector3.ProjectOnPlane(rig.head.transform.forward, Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f) { Notice = "Look forward while aligning the table."; return; }
            if (Vector3.Distance(left,right) < .15f || Mathf.Abs(left.y-right.y) > .12f)
            { Notice = "Place both hands level, at least 15 cm apart."; return; }
            tableFrame.SetPositionAndRotation((left+right)*.5f - Vector3.up*.04f,Quaternion.LookRotation(forward));
            CaptureMarkers(); IsCalibrated = true;
            Notice = "Table aligned. Keep your hands on the two rings.";
        }
        void CaptureMarkers()
        {
            initialLeft = rig.leftArm.wristTarget.position;
            initialRight = rig.rightArm.wristTarget.position;
            PlaceMarker(leftMarker,initialLeft); PlaceMarker(rightMarker,initialRight);
        }
        void PlaceMarker(Transform marker, Vector3 wrist)
        { marker.position = new Vector3(wrist.x,tableFrame.position.y+.004f,wrist.z); }

        public void StartTrial()
        {
            if (Protocol.Active) return;
            if (!TrackingReady) { Notice = "Cannot start: both hands and headset must be tracked."; return; }
            if (!IsCalibrated) { Notice = "Align the table first (F or ALIGN TABLE)."; return; }
            Vector3 left = rig.leftArm.wristTarget.position, right = rig.rightArm.wristTarget.position;
            if (Vector3.Dot(right-left,tableFrame.right) < .15f || Mathf.Abs(left.y-right.y) > .12f ||
                Mathf.Abs((left.y+right.y)*.5f-tableFrame.position.y-.04f) > .12f)
            { Notice = "Place uncrossed, level hands above the table before starting."; return; }
            var snapshot = settings.Snapshot();
            if (writeLogs && !log.Open(snapshot,desktopPreview))
            { Notice = "Cannot create trial log: " + log.Error; return; }
            CaptureMarkers(); trialLeftController = rig.LeftUsesController; trialRightController = rig.RightUsesController;
            originPosition = rig.trackingSpace.position; originRotation = rig.trackingSpace.rotation;
            foreach (var input in originInputs) input.trackingOriginUpdated -= OriginUpdated;
            SubsystemManager.GetSubsystems(originInputs);
            foreach (var input in originInputs) input.trackingOriginUpdated += OriginUpdated;
            previewSearchMoved = false;
            Protocol.Start(snapshot); menu.SetActive(false); nextSample = Time.unscaledTime;
        }
        public void ConfirmSearch()
        { if (Protocol.Phase == TrialPhase.Search) { Record("search_confirmed"); Protocol.ConfirmSearch(); } }
        public void HideNow()
        { if (Protocol.Phase >= TrialPhase.Baseline && Protocol.Phase <= TrialPhase.Hold) { Record("operator_hide_early"); Protocol.HideNow(); } }
        public void ResetTrial()
        {
            Abort("operator_reset"); Protocol.Reset(); menu.SetActive(true);
            Notice = "Ready. Place your hands on the table.";
        }
        public void Abort(string reason)
        {
            if (!Protocol.Active) return;
            Record(reason); Protocol.Abort();
            IsCalibrated = false; menu.SetActive(true);
            Notice = "Trial stopped: " + reason + ". Align the table before restarting.";
        }
        public void ToggleTarget() { if (!Protocol.Active) settings.targetLeft = !settings.targetLeft; UpdateLabels(); }
        public void ToggleControl() { if (!Protocol.Active) settings.controlCondition = !settings.controlCondition; UpdateLabels(); }
        public void ChangeShift(float delta) { if (!Protocol.Active) settings.displacement = Mathf.Clamp(settings.displacement+delta,0,.20f); UpdateLabels(); }
        public void ChangeDrift(float delta) { if (!Protocol.Active) settings.driftSeconds = Mathf.Clamp(settings.driftSeconds+delta,5,180); UpdateLabels(); }
        public void ToggleMenu() { menu.SetActive(!menu.activeSelf); }

        void OnPhaseChanged(TrialPhase phase)
        {
            Record("phase_" + phase);
            if (phase == TrialPhase.Complete || phase == TrialPhase.Aborted) log.Dispose();
            if (phase == TrialPhase.Complete) { Notice = "Trial complete. Both hands are back at their tracked positions."; menu.SetActive(true); }
            UpdateLabels(); ApplyVisuals();
        }
        public void ApplyVisuals()
        {
            if (!rig || !tableFrame) return;
            ApplyHand(rig.leftArm,Protocol.Offset(true,tableFrame.right),Protocol.IsVisible(true));
            ApplyHand(rig.rightArm,Protocol.Offset(false,tableFrame.right),Protocol.IsVisible(false));
            bool markers = IsCalibrated && (Protocol.Phase == TrialPhase.Ready || Protocol.Phase == TrialPhase.Baseline || Protocol.Phase == TrialPhase.Drift || Protocol.Phase == TrialPhase.Hold);
            leftMarker.gameObject.SetActive(markers); rightMarker.gameObject.SetActive(markers);
        }
        void ApplyHand(TrackedArmAvatar arm, Vector3 shift, bool visible)
        {
            if (!arm || !arm.skin || !arm.wrist || !arm.middleBase) return;
            if (block == null) block = new MaterialPropertyBlock();
            bool valid = desktopPreview || (rig.HeadTracked && arm.tracked);
            Vector3 forward = (arm.middleBase.position-arm.wrist.position).normalized;
            arm.skin.GetPropertyBlock(block);
            block.SetVector("_Shift",shift); block.SetVector("_Wrist",arm.wrist.position);
            block.SetVector("_Forward",forward); block.SetFloat("_Visible",visible && valid ? 1 : 0);
            arm.skin.SetPropertyBlock(block);
        }
        Vector3 SearchTip()
        {
            bool left = !Protocol.Settings.targetLeft;
            if (!desktopPreview && rig.TryGetTrackedJoint(left,XRHandJointID.IndexTip,out var tip)) return tip;
            // Controller conditions use the wrist/grip proxy; explicitly identified by source columns.
            return (left ? rig.leftArm : rig.rightArm).wristTarget.position;
        }
        void Record(string reason)
        {
            if (!rig || !tableFrame) return;
            log.Write(reason,Protocol,rig.leftArm.wristTarget.position,rig.rightArm.wristTarget.position,
                rig.leftArm.wrist.position+Protocol.Offset(true,tableFrame.right),rig.rightArm.wrist.position+Protocol.Offset(false,tableFrame.right),
                SearchTip(),desktopPreview || (rig.HeadTracked && rig.leftArm.tracked),desktopPreview || (rig.HeadTracked && rig.rightArm.tracked),
                desktopPreview?"simulation":rig.LeftUsesController?"controller":"hand",desktopPreview?"simulation":rig.RightUsesController?"controller":"hand");
        }
        void ReadInput()
        {
            bool start = Button(XRNode.LeftHand,CommonUsages.primaryButton);
            bool confirm = Button(XRNode.RightHand,CommonUsages.primaryButton);
            bool reset = Button(XRNode.RightHand,CommonUsages.secondaryButton);
            bool show = Button(XRNode.LeftHand,CommonUsages.menuButton);
            leftPinchTime = rig.LeftPinching ? leftPinchTime+Time.unscaledDeltaTime : 0;
            rightPinchTime = rig.RightPinching ? rightPinchTime+Time.unscaledDeltaTime : 0;
            start |= !rig.LeftUsesController && leftPinchTime >= 1;
            confirm |= !rig.RightUsesController && rightPinchTime >= 1;
            if (start && !priorStart) { if (!IsCalibrated) AlignTable(); else StartTrial(); }
            if (confirm && !priorConfirm) ConfirmSearch();
            if (reset && !priorReset) ResetTrial();
            if (show && !priorMenu) ToggleMenu();
            priorStart=start; priorConfirm=confirm; priorReset=reset; priorMenu=show;
            if (Input.GetKeyDown(KeyCode.Space)) { if (Protocol.Phase == TrialPhase.Search) ConfirmSearch(); else StartTrial(); }
            if (Input.GetKeyDown(KeyCode.F)) AlignTable();
            if (Input.GetKeyDown(KeyCode.H)) HideNow();
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Escape)) ResetTrial();
            if (Input.GetKeyDown(KeyCode.F8)) ToggleMenu();
            if (Input.GetKeyDown(KeyCode.T)) ToggleTarget();
            if (Input.GetKeyDown(KeyCode.B)) ToggleControl();
        }
        static bool Button(XRNode node,InputFeatureUsage<bool> usage)
        { var d=InputDevices.GetDeviceAtXRNode(node); return d.isValid && d.TryGetFeatureValue(usage,out bool value) && value; }

        void UpdatePreview()
        {
            if (Protocol.Phase == TrialPhase.Baseline) { previewLeft=initialLeft; previewRight=initialRight; }
            if (Protocol.Phase == TrialPhase.Drift || Protocol.Phase == TrialPhase.Hold)
            { previewLeft=initialLeft-Protocol.Offset(true,tableFrame.right); previewRight=initialRight-Protocol.Offset(false,tableFrame.right); }
            if (Input.GetKeyDown(KeyCode.P) && Protocol.Phase == TrialPhase.Search) previewSearchMoved = !previewSearchMoved;
            if (Protocol.Phase == TrialPhase.Search && previewSearchMoved)
            {
                Vector3 destination = Protocol.Settings.targetLeft ? initialLeft : initialRight;
                if (Protocol.Settings.targetLeft) previewRight=Vector3.MoveTowards(previewRight,destination,Time.unscaledDeltaTime*.2f);
                else previewLeft=Vector3.MoveTowards(previewLeft,destination,Time.unscaledDeltaTime*.2f);
            }
            Vector3 movement = new Vector3(Input.GetAxisRaw("Horizontal"),0,Input.GetAxisRaw("Vertical")) * (Time.unscaledDeltaTime*.2f);
            if (Input.GetKey(KeyCode.LeftShift)) previewLeft += movement; else previewRight += movement;
            PosePreview();
        }
        void PosePreview()
        {
            TrackedHandsAvatarRig.ApplyControllerPose(rig.leftArm,new Pose(previewLeft,Quaternion.identity),Vector3.zero,Vector3.zero,0,0);
            TrackedHandsAvatarRig.ApplyControllerPose(rig.rightArm,new Pose(previewRight,Quaternion.identity),Vector3.zero,Vector3.zero,0,0);
        }
        public void UpdateLabels()
        {
            string hand = Protocol.Settings.targetLeft ? "LEFT" : "RIGHT";
            string instruction;
            switch (Protocol.Phase)
            {
                case TrialPhase.Baseline: case TrialPhase.Drift: case TrialPhase.Hold:
                    instruction = "Keep the visible wrists above the two rings."; break;
                case TrialPhase.Search:
                    instruction = "Keep your " + hand + " hand still.\nFind it with your other hand.\nPress A / hold right pinch when finished."; break;
                case TrialPhase.Reveal: instruction = "Both hands are returning. Stay still."; break;
                case TrialPhase.Aborted: instruction = "Trial stopped.\nRealign the table to begin again."; break;
                default: instruction = TrackingReady ? "Rest both hands on the table.\nAlign the table, then start." : "Waiting for headset and both hands..."; break;
            }
            if (instructionText) instructionText.text = instruction;
            if (statusText) statusText.text = (desktopPreview?"DESKTOP PREVIEW  |  ":"PCVR  |  ") +
                (settings.targetLeft?"Hide LEFT":"Hide RIGHT") + "  |  " + (settings.controlCondition?"CONTROL":"DRIFT") +
                "\n" + (settings.displacement*100).ToString("F0") + " cm per hand  /  " + settings.driftSeconds.ToString("F0") + " s";
        }
        void OnGUI()
        {
            GUILayout.BeginArea(new Rect(16,16,340,Screen.height-32),GUI.skin.box);
            GUILayout.Label("DISAPPEARING HAND TRICK / FPS ARMS");
            GUILayout.Label(desktopPreview?"DESKTOP SIMULATION (not participant data)":"WINDOWS PCVR / OpenXR");
            if (TryGetComponent<PcvrSession>(out var session)) GUILayout.Label(session.Status);
            GUILayout.Label("Phase: " + Protocol.Phase + "  " + Protocol.Elapsed.ToString("F1") + " s");
            GUILayout.Label("Tracking: " + (TrackingReady?"ready":"waiting") + "  /  " + (IsCalibrated?"table aligned":"align table"));
            GUILayout.Label(Notice);
            GUILayout.Space(8);
            if (GUILayout.Button("F  Align table to both wrists")) AlignTable();
            if (GUILayout.Button("SPACE  Start / confirm search")) { if (Protocol.Phase==TrialPhase.Search) ConfirmSearch(); else StartTrial(); }
            if (GUILayout.Button("H  Hide now (early / logged)")) HideNow();
            if (GUILayout.Button("R / ESC  Reset and restore hands")) ResetTrial();
            GUI.enabled=!Protocol.Active;
            if (GUILayout.Button("T  Target: " + (settings.targetLeft?"LEFT":"RIGHT"))) ToggleTarget();
            if (GUILayout.Button("B  Condition: " + (settings.controlCondition?"CONTROL (no drift)":"DRIFT"))) ToggleControl();
            GUILayout.Label("Displacement / hand: " + (100*settings.displacement).ToString("F0") + " cm");
            settings.displacement=GUILayout.HorizontalSlider(settings.displacement,0,.20f);
            GUILayout.Label("Drift duration: " + settings.driftSeconds.ToString("F0") + " seconds");
            settings.driftSeconds=GUILayout.HorizontalSlider(settings.driftSeconds,5,120);
            GUI.enabled=true;
            if (GUILayout.Button("F8  Headset menu")) ToggleMenu();
            GUILayout.Label("Controllers: X start, A confirm, B reset.\nHands: left pinch 1 s align/start; right pinch 1 s confirm.\nMenu: aim controller + trigger, or look + right pinch.");
            if (desktopPreview) GUILayout.Label("Arrow keys: right hand; Shift: left hand.\nP: search movement. SPACE: confirm.");
            if (!string.IsNullOrEmpty(log.Path)) GUILayout.Label("CSV: " + System.IO.Path.GetFileName(log.Path));
            if (!string.IsNullOrEmpty(log.Error)) GUILayout.Label("LOG ERROR: " + log.Error);
            GUILayout.EndArea();
        }
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z));
    }
}
