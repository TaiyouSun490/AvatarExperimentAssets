using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using AvatarExperiments.FpsArms;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Hands;

namespace DisappearingHand
{
    [DefaultExecutionOrder(10000)]
    public sealed class OriginalDhtExperiment : MonoBehaviour
    {
        public TrackedHandsAvatarRig rig;
        public Transform tableFrame;
        public Renderer leftBar, centreBar, rightBar;
        public Text instructionText, statusText;
        public GameObject menu;
        public OriginalDhtCondition condition;
        public bool desktopPreview, writeLogs = true;
        public OriginalDhtProtocol Protocol { get; } = new OriginalDhtProtocol();
        public bool TrackingReady => desktopPreview || (rig && rig.HeadTracked && rig.leftArm.tracked && rig.rightArm.tracked &&
            Finite(rig.leftArm.wristTarget.position) && Finite(rig.rightArm.wristTarget.position));
        public bool IsCalibrated { get; private set; }
        public string Notice { get; private set; } = "Rest both hands on the physical table and press F.";
        public string LastLogPath { get; private set; }
        public string LogError { get; private set; }
        public bool ShowOperatorPanel = true;
        const float BarWidth = .006f;
        // VR implementation tolerances, not values reported in the paper.
        const float PositionTolerance = .03f, RightMotionTolerance = .02f;
        StreamWriter writer;
        MaterialPropertyBlock block;
        Vector3 originPosition, initialLeft, initialRight, restingRight;
        Quaternion originRotation;
        bool trialLeftController, trialRightController, movePreview, tableConfirmed;
        float nextSample, nextUI;
        readonly List<XRInputSubsystem> originInputs = new List<XRInputSubsystem>();
        Vector2 scroll;
        static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        void Awake()
        {
            desktopPreview |= Array.Exists(Environment.GetCommandLineArgs(), a => a == "--desktop");
            var calibration = rig.GetComponent<ArmLengthCalibration>();
            if (calibration) calibration.enabled = false;
            if (desktopPreview)
            {
                rig.enabled = false;
                rig.head.GetUniversalAdditionalCameraData().allowXRRendering = false;
                rig.head.transform.SetPositionAndRotation(new Vector3(0,1.38f,-.72f), Quaternion.Euler(25,0,0));
                var bodyHead = new GameObject("Preview body head").transform;
                bodyHead.SetParent(transform,false); bodyHead.position = new Vector3(0,1.25f,.08f);
                rig.leftArm.head = rig.rightArm.head = bodyHead;
                PosePreview(new Vector3(-.08f,.79f,.38f), new Vector3(.08f,.79f,.38f));
            }
            foreach (var arm in new[]{rig.leftArm,rig.rightArm})
            {
                arm.hideWhenUntracked = true;
                arm.skin.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                arm.skin.receiveShadows = false; arm.skin.updateWhenOffscreen = true;
                var bounds = arm.skin.localBounds; bounds.Expand(2); arm.skin.localBounds = bounds;
            }
            Protocol.Changed += PhaseChanged;
        }
        void Start() { if (desktopPreview) AlignTable(); UpdateLabels(); }
        void OnEnable() { Application.onBeforeRender += BeforeRender; }
        void OnDisable()
        {
            Application.onBeforeRender -= BeforeRender;
            Abort("component_disabled"); CloseLog();
            if (rig) { Hand(rig.leftArm,Vector3.zero,1); Hand(rig.rightArm,Vector3.zero,1); }
        }
        void OnDestroy()
        {
            Protocol.Changed -= PhaseChanged; CloseLog();
            foreach (var input in originInputs) input.trackingOriginUpdated -= Recentered;
        }
        void Recentered(XRInputSubsystem input) { Abort("runtime_recentered"); IsCalibrated = false; }
        void OnApplicationFocus(bool focused) { if (!focused && !desktopPreview) Abort("application_focus_lost"); }
        void OnApplicationPause(bool paused) { if (paused) Abort("application_paused"); }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F)) AlignTable();
            if (Input.GetKeyDown(KeyCode.Space)) Advance();
            if (Input.GetKeyDown(KeyCode.C)) ConfirmCloth();
            if (Input.GetKeyDown(KeyCode.R) || Input.GetKeyDown(KeyCode.Escape)) ResetTrial();
            if (Input.GetKeyDown(KeyCode.B)) CycleCondition();
            if (Input.GetKeyDown(KeyCode.F8)) ToggleMenu();
            if (desktopPreview && Input.GetKeyDown(KeyCode.P)) movePreview = !movePreview;
            if (Protocol.Active)
            {
                if (!TrackingReady) Abort("tracking_lost");
                else if (!desktopPreview && (trialLeftController != rig.LeftUsesController || trialRightController != rig.RightUsesController)) Abort("input_source_changed");
                else if (Vector3.Distance(originPosition,rig.trackingSpace.position) > .01f || Quaternion.Angle(originRotation,rig.trackingSpace.rotation) > 1) Abort("tracking_origin_changed");
                else if (Time.unscaledDeltaTime > .5f) Abort("frame_gap");
                else if (tableConfirmed && Vector3.Distance(restingRight,rig.rightArm.wristTarget.position) > RightMotionTolerance) Abort("right_hand_moved_after_rest");
                else Protocol.Tick(Time.unscaledDeltaTime);
            }
            if (desktopPreview && Protocol.Active) UpdatePreview();
            if (Time.unscaledTime >= nextUI) { nextUI = Time.unscaledTime + .1f; UpdateLabels(); }
        }
        void LateUpdate()
        {
            ApplyVisuals();
            if (Protocol.Active && Time.unscaledTime >= nextSample)
            { nextSample = Time.unscaledTime + 1f/30; Record("sample"); }
        }
        [BeforeRenderOrder(10000)]
        void BeforeRender() { if (Protocol.Active && !TrackingReady) Abort("tracking_lost_before_render"); ApplyVisuals(); }

        public void AlignTable()
        {
            if (Protocol.Active || !TrackingReady) { Notice = "Both hands and headset must be tracked, with no active trial."; return; }
            Vector3 left = rig.leftArm.wristTarget.position, right = rig.rightArm.wristTarget.position;
            Vector3 forward = Vector3.ProjectOnPlane(rig.head.transform.forward,Vector3.up).normalized;
            if (forward.sqrMagnitude < .1f || Vector3.Distance(left,right) < .10f || Mathf.Abs(left.y-right.y) > .06f)
            { Notice = "Rest both level hands on the table and look forward."; return; }
            // Wrist-to-surface reference is a VR calibration assumption. Check against the real tabletop.
            tableFrame.SetPositionAndRotation((left+right)*.5f - Vector3.up*.04f,Quaternion.LookRotation(forward));
            IsCalibrated = true;
            Notice = "Raise hands 10 cm above the surface, each 8 cm from the midline. SPACE starts.";
            if (desktopPreview) PosePreview(Home(true),Home(false));
        }
        Vector3 Home(bool left) => tableFrame.TransformPoint(new Vector3(left?-.08f:.08f,.10f,0));
        public void StartTrial()
        {
            if (Protocol.Active) return;
            if (!IsCalibrated || !TrackingReady) { Notice = "Align the physical table first (F)."; return; }
            if (Vector3.Distance(rig.leftArm.wristTarget.position,Home(true)) > PositionTolerance ||
                Vector3.Distance(rig.rightArm.wristTarget.position,Home(false)) > PositionTolerance)
            { Notice = "Start pose: hands 16 cm apart, 10 cm above table (3 cm tracking tolerance)."; return; }
            if (writeLogs && !OpenLog()) return;
            initialLeft = rig.leftArm.wristTarget.position; initialRight = rig.rightArm.wristTarget.position;
            originPosition = rig.trackingSpace.position; originRotation = rig.trackingSpace.rotation;
            trialLeftController = rig.LeftUsesController; trialRightController = rig.RightUsesController;
            foreach (var input in originInputs) input.trackingOriginUpdated -= Recentered;
            SubsystemManager.GetSubsystems(originInputs);
            foreach (var input in originInputs) input.trackingOriginUpdated += Recentered;
            tableConfirmed = movePreview = false;
            Protocol.Start(condition); menu.SetActive(false);
            Notice = "Wait for 25 s adaptation. Ask participant to keep the visible hands still between bars.";
        }
        public void Advance()
        {
            switch (Protocol.Phase)
            {
                case OriginalDhtPhase.Ready: case OriginalDhtPhase.Complete: case OriginalDhtPhase.Aborted: StartTrial(); break;
                case OriginalDhtPhase.AwaitTableRest:
                    if (desktopPreview)
                    {
                        PosePreview(initialLeft-Protocol.Offset(true,tableFrame.right)-Vector3.up*.06f,
                            initialRight-Protocol.Offset(false,tableFrame.right)-Vector3.up*.06f);
                    }
                    if (!TrackingReady || Mathf.Abs(rig.leftArm.wristTarget.position.y-tableFrame.position.y-.04f) > PositionTolerance ||
                        Mathf.Abs(rig.rightArm.wristTarget.position.y-tableFrame.position.y-.04f) > PositionTolerance)
                    { Notice = "Confirm only after BOTH hands rest on the physical tabletop."; return; }
                    restingRight = rig.rightArm.wristTarget.position; tableConfirmed = true;
                    Record("operator_confirmed_physical_table_rest"); Protocol.ConfirmTableRest(); break;
                case OriginalDhtPhase.AwaitReach: Record("operator_reach_cue"); Protocol.BeginReach(); break;
                case OriginalDhtPhase.Reach: Record("operator_reach_finished"); Protocol.FinishReach(); break;
                case OriginalDhtPhase.Report: Record("operator_report_finished"); Protocol.Complete(); break;
            }
        }
        public void ConfirmCloth()
        {
            if (Protocol.Phase != OriginalDhtPhase.AwaitCover) return;
            Record("operator_confirmed_real_black_cloth"); Protocol.ConfirmClothPlaced();
        }
        public void CycleCondition() { if (!Protocol.Active) condition = (OriginalDhtCondition)(((int)condition+1)%3); UpdateLabels(); }
        public void ToggleMenu() { if (!Protocol.Active) menu.SetActive(!menu.activeSelf); }
        public void MarkContact() { if (Protocol.Phase == OriginalDhtPhase.Reach) Record("operator_observed_hand_contact"); }
        public void MarkMiss() { if (Protocol.Phase == OriginalDhtPhase.Reach) Record("operator_observed_reach_miss"); }
        public void ResetTrial()
        {
            Abort("operator_reset"); Protocol.Reset(); tableConfirmed = false; menu.SetActive(true);
            if (desktopPreview)
            {
                PosePreview(Home(true)-Vector3.up*.06f,Home(false)-Vector3.up*.06f);
                IsCalibrated = false;
            }
            Notice = "Rest both hands on table, align, then raise hands to the start position.";
        }
        public void Abort(string reason)
        {
            if (!Protocol.Active) return;
            Record(reason); Protocol.Abort(); IsCalibrated = false;
            Notice = "Trial stopped: " + reason + ". Realign before restarting.";
        }
        void PhaseChanged(OriginalDhtPhase phase)
        {
            Record("phase_"+phase);
            if (phase == OriginalDhtPhase.Complete || phase == OriginalDhtPhase.Aborted)
            { CloseLog(); menu.SetActive(true); }
            UpdateLabels(); ApplyVisuals();
        }
        public void ApplyVisuals()
        {
            if (!rig || !tableFrame) return;
            Hand(rig.leftArm,Protocol.Offset(true,tableFrame.right),1);
            Hand(rig.rightArm,Protocol.Offset(false,tableFrame.right),Protocol.RightBrightness);
            float gap = Protocol.BarGap;
            // Inner edge of each outer bar is gap metres from the edge of the centre bar.
            leftBar.transform.localPosition = new Vector3(-gap-BarWidth,.102f,.07f);
            rightBar.transform.localPosition = new Vector3(gap+BarWidth,.102f,.07f);
            foreach (var bar in new[]{leftBar,centreBar,rightBar})
            {
                bar.enabled = Protocol.BarsBrightness > 0;
                if (block == null) block = new MaterialPropertyBlock(); block.Clear();
                block.SetColor("_BaseColor",new Color(0,.15f,1)*Protocol.BarsBrightness); bar.SetPropertyBlock(block);
            }
        }
        void Hand(TrackedArmAvatar arm,Vector3 shift,float brightness)
        {
            if (!arm || !arm.skin || !arm.wrist || !arm.middleBase) return;
            if (block == null) block = new MaterialPropertyBlock(); block.Clear();
            block.SetVector("_Shift",shift); block.SetVector("_Wrist",arm.wrist.position);
            block.SetVector("_Forward",(arm.middleBase.position-arm.wrist.position).normalized);
            block.SetVector("_Rotation",new Vector4(0,0,0,1)); block.SetFloat("_Fade",brightness);
            block.SetFloat("_Visible",brightness > 0 && (desktopPreview || (rig.HeadTracked && arm.tracked)) ? 1 : 0);
            arm.skin.SetPropertyBlock(block);
        }
        void UpdatePreview()
        {
            Vector3 left = initialLeft-Protocol.Offset(true,tableFrame.right);
            Vector3 right = initialRight-Protocol.Offset(false,tableFrame.right);
            if (tableConfirmed) { left -= Vector3.up*.06f; right = restingRight; }
            if (movePreview && (Protocol.Phase == OriginalDhtPhase.Reach || Protocol.Phase == OriginalDhtPhase.Report))
                left = initialRight-Protocol.Offset(true,tableFrame.right)-Vector3.up*.06f;
            PosePreview(left,right);
        }
        public void PreviewReach()
        { if (desktopPreview && Protocol.Phase == OriginalDhtPhase.Reach) { movePreview = true; UpdatePreview(); } }
        void PosePreview(Vector3 left,Vector3 right)
        {
            TrackedHandsAvatarRig.ApplyControllerPose(rig.leftArm,new Pose(left,Quaternion.identity),Vector3.zero,Vector3.zero,0,0);
            TrackedHandsAvatarRig.ApplyControllerPose(rig.rightArm,new Pose(right,Quaternion.identity),Vector3.zero,Vector3.zero,0,0);
        }
        public void UpdateLabels()
        {
            string text;
            switch (Protocol.Phase)
            {
                case OriginalDhtPhase.Adaptation: text = "Keep your hands still between the blue bars.\nDo not touch the bars."; break;
                case OriginalDhtPhase.AwaitTableRest: text = "Rest both hands on the table.\nKeep your right hand still until the trial ends."; break;
                case OriginalDhtPhase.Fade: case OriginalDhtPhase.AwaitCover: case OriginalDhtPhase.AwaitReach:
                    text = "Keep your right hand still.\nWait for the instruction to reach."; break;
                case OriginalDhtPhase.Reach: text = "Reach with your LEFT hand to touch your RIGHT hand.\nKeep your right hand still."; break;
                case OriginalDhtPhase.Report: text = "Keep your right hand still.\nTell the experimenter what you experienced."; break;
                default: text = "Wait for the experimenter's instructions."; break;
            }
            if (instructionText) instructionText.text = text;
            // Participant menu does not disclose the manipulation or condition.
            if (statusText) statusText.text = "Follow the experimenter's instructions.";
        }
        void OnGUI()
        {
            if (!ShowOperatorPanel) return;
            GUILayout.BeginArea(new Rect(16,16,355,Screen.height-32),GUI.skin.box);
            scroll = GUILayout.BeginScrollView(scroll);
            GUILayout.Label("DHT / NEWPORT & GILPIN 2011 / VR");
            GUILayout.Label(desktopPreview ? "SIMULATION: not participant data" : "PCVR: real table contact required");
            GUILayout.Label(condition + " / " + Protocol.Phase + " / " + Protocol.Elapsed.ToString("F1") + " s");
            GUILayout.Label("Hand drift: 2.5 mm/s x 25 s = 6.25 cm\nBars: 1.9 mm/s x 25 s. Fade: 7 s.");
            GUILayout.Label(Notice);
            if (GUILayout.Button("F  Align physical tabletop")) AlignTable();
            if (GUILayout.Button("SPACE  Start / table rested / reach / report done")) Advance();
            GUI.enabled = Protocol.Phase == OriginalDhtPhase.AwaitCover;
            if (GUILayout.Button("C  Confirm REAL black cloth placed")) ConfirmCloth();
            GUI.enabled = Protocol.Phase == OriginalDhtPhase.Reach;
            if (GUILayout.Button("Record observed hand contact")) MarkContact();
            if (GUILayout.Button("Record observed miss")) MarkMiss();
            GUI.enabled = true;
            if (GUILayout.Button("R / ESC  Stop and restore")) ResetTrial();
            GUI.enabled = !Protocol.Active;
            if (GUILayout.Button("B  Condition: " + condition)) CycleCondition();
            GUI.enabled = true;
            if (Protocol.Phase == OriginalDhtPhase.AwaitTableRest) GUILayout.Label("Wait for physical contact; SPACE confirms it. Height alone is not a contact measurement.");
            if (Protocol.Phase == OriginalDhtPhase.AwaitCover) GUILayout.Label("Place a real black cloth over the right hand. C confirms. Then SPACE cues the reach.");
            if (Protocol.Phase == OriginalDhtPhase.AwaitReach) GUILayout.Label("SPACE gives the reach instruction.");
            if (Protocol.Phase == OriginalDhtPhase.Report) GUILayout.Label("Collect verbal ratings / questionnaire externally before SPACE restores vision. No automatic reveal.");
            if (desktopPreview && GUILayout.Button("P  Simulate reach")) PreviewReach();
            GUILayout.Label("Original procedure adapted to FPSArm + HMD.\n25 s / 7 s timing is fixed. SH / CH have no drift.\nQuestionnaire, SCR and drawing tasks are external.");
            if (!string.IsNullOrEmpty(LastLogPath)) GUILayout.Label("CSV: " + Path.GetFileName(LastLogPath));
            if (!string.IsNullOrEmpty(LogError)) GUILayout.Label("LOG ERROR: " + LogError);
            GUILayout.EndScrollView(); GUILayout.EndArea();
        }
        bool OpenLog()
        {
            CloseLog(); LogError = null;
            try
            {
                string dir = Path.Combine(Application.persistentDataPath,"OriginalDhtTrials"); Directory.CreateDirectory(dir);
                LastLogPath = Path.Combine(dir,DateTime.UtcNow.ToString("yyyyMMddTHHmmssfffZ")+"_"+Guid.NewGuid().ToString("N").Substring(0,8)+".csv");
                writer = new StreamWriter(LastLogPath,false,new System.Text.UTF8Encoding(false));
                writer.WriteLine("# schema=original_dht_v1; doi=10.1016/j.cub.2011.08.044; source=supplement_p5; utc="+DateTime.UtcNow.ToString("O"));
                writer.WriteLine("# desktop_preview="+desktopPreview+"; condition="+condition+"; adaptation_s=25; hand_speed_m_s=.0025; bar_speed_m_s=.0019; fade_s=7; right_motion_tolerance_m=.02");
                writer.WriteLine("# apparatus=FPSArm_HMD_VR_adaptation; times=software_not_photon; contact=operator_annotation_not_sensor; questionnaire=external");
                writer.WriteLine("event,time_s,phase,condition,shift_m,bar_gap_m,bars_brightness,right_brightness,left_raw_x,left_raw_y,left_raw_z,right_raw_x,right_raw_y,right_raw_z,left_visual_x,left_visual_y,left_visual_z,right_visual_x,right_visual_y,right_visual_z,left_tip_x,left_tip_y,left_tip_z,tip_source,left_source,right_source,tracking_ready");
                writer.Flush(); return true;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { LogError = ex.Message; CloseLog(); Notice = "Cannot start: log unavailable."; return false; }
        }
        void Record(string evt)
        {
            if (writer == null || !rig) return;
            bool tipTracked = !desktopPreview && rig.TryGetTrackedJoint(true,XRHandJointID.IndexTip,out _);
            Vector3 tip = rig.leftArm.wristTarget.position;
            if (tipTracked) rig.TryGetTrackedJoint(true,XRHandJointID.IndexTip,out tip);
            try
            {
                writer.WriteLine(string.Join(",",evt,F(Protocol.Elapsed),Protocol.Phase,Protocol.Condition,F(Protocol.Shift),F(Protocol.BarGap),F(Protocol.BarsBrightness),F(Protocol.RightBrightness),
                    V(rig.leftArm.wristTarget.position),V(rig.rightArm.wristTarget.position),V(rig.leftArm.wrist.position+Protocol.Offset(true,tableFrame.right)),
                    V(rig.rightArm.wrist.position+Protocol.Offset(false,tableFrame.right)),V(tip),tipTracked?"index_tip":"wrist_proxy",
                    desktopPreview?"simulation":rig.LeftUsesController?"controller":"hand",desktopPreview?"simulation":rig.RightUsesController?"controller":"hand",TrackingReady?"1":"0"));
                if (evt != "sample") writer.Flush();
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { LogError = ex.Message; CloseLog(); Abort("log_write_failed"); }
        }
        void CloseLog() { try { writer?.Dispose(); } catch (IOException ex) { LogError = ex.Message; } writer = null; }
        static string F(float value) => value.ToString("F6",Inv);
        static string V(Vector3 value) => F(value.x)+","+F(value.y)+","+F(value.z);
        static bool Finite(Vector3 v) => !(float.IsNaN(v.x)||float.IsNaN(v.y)||float.IsNaN(v.z)||float.IsInfinity(v.x)||float.IsInfinity(v.y)||float.IsInfinity(v.z));
    }
}
