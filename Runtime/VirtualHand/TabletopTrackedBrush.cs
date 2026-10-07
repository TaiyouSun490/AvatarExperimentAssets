using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;
using CommonUsages = UnityEngine.XR.CommonUsages;

namespace AvatarExperiments.VirtualHand
{
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class TabletopTrackedBrush : MonoBehaviour
    {
        public TabletopExperiment experiment;
        public Transform trackingSpace;
        public Transform[] visualLandmarks = new Transform[3];
        [Tooltip("Controller-local brush tip in metres. Use pivot calibration or explicitly confirm a measured offset.")]
        public Vector3 tipOffset;
        public bool automaticSampling = true;
        [Min(.001f)] public float maximumAlignmentError = .01f;
        public bool calibrationPreview;
        public bool ConfigurationLocked { get; set; }
        public float AlignmentError { get; private set; }
        public event Action<string> CalibrationInvalidated;
        public bool Tracked { get; private set; }
        public bool TipConfirmed { get; private set; }
        public bool Calibrated { get; private set; }
        public bool Pivoting { get; private set; }
        public int CalibrationStep { get; private set; }
        public string Status { get; private set; } = "筆先をキャリブレーションしてください。";
        public Vector3 RawTip { get; private set; }
        public Vector3 VisualTip { get; private set; }
        public double SampleTime { get; private set; }
        public double VisualSourceTime { get; private set; }
        public bool SessionActive { get; private set; }
        public float ActiveDelay { get; private set; }
        readonly BrushPoseHistory history = new BrushPoseHistory();
        readonly List<Pose> pivotSamples = new List<Pose>();
        readonly List<XRInputSubsystem> inputSubsystems = new List<XRInputSubsystem>();
        readonly List<XRInputSubsystem> observedSubsystems = new List<XRInputSubsystem>();
        readonly Vector3[] realPoints = new Vector3[3];
        readonly Vector3[] targetPoints = new Vector3[3];
        Quaternion mappingRotation;
        Matrix4x4 calibratedWorldToHand;
        Vector3 mappingTranslation, confirmedOffset, originPosition, originScale, handPosition, handScale;
        Quaternion originRotation, handRotation;
        Pose latestLocalPose;
        bool originKnown, triggerHeld;
        double pivotStarted, previousSample = double.NaN, nextFlush;
        float nextSubsystemCheck;
        StreamWriter poseLog;
        const double MaximumGap = .1;

        void Update()
        {
            if (!automaticSampling || !experiment || !experiment.UsesTrackedBrush) return;
            if (Time.unscaledTime >= nextSubsystemCheck)
            {
                nextSubsystemCheck = Time.unscaledTime + 1;
                SubsystemManager.GetSubsystems(inputSubsystems);
                foreach (var subsystem in inputSubsystems)
                    if (!observedSubsystems.Contains(subsystem)) { subsystem.trackingOriginUpdated += OriginUpdated; observedSubsystems.Add(subsystem); }
            }
            var device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
            bool valid = device.isValid && (device.characteristics & InputDeviceCharacteristics.Controller) != 0 &&
                device.TryGetFeatureValue(CommonUsages.isTracked, out bool tracked) && tracked &&
                device.TryGetFeatureValue(CommonUsages.trackingState, out var state) &&
                (state & (InputTrackingState.Position | InputTrackingState.Rotation)) == (InputTrackingState.Position | InputTrackingState.Rotation);
            Pose? pose = null;
            if (valid && device.TryGetFeatureValue(CommonUsages.devicePosition, out var p) && device.TryGetFeatureValue(CommonUsages.deviceRotation, out var q)) pose = new Pose(p, q);
            Sample(Time.realtimeSinceStartupAsDouble, pose);
            device.TryGetFeatureValue(CommonUsages.triggerButton, out bool trigger);
            var keyboard = Keyboard.current;
            if (!experiment.Running)
            {
                if (experiment.keyboardControls && keyboard != null && keyboard.pKey.wasPressedThisFrame) BeginPivot();
                if ((experiment.keyboardControls && keyboard != null && keyboard.cKey.wasPressedThisFrame) || (experiment.controllerControls && trigger && !triggerHeld)) CaptureLandmark();
                Preview();
            }
            triggerHeld = trigger;
        }
        // Also used with explicit controller poses by the Play Mode regression tests.
        public void Sample(double time, Pose? localPose)
        {
            if (!trackingSpace || !localPose.HasValue || !double.IsFinite(time)) { LoseTracking("右コントローラーを追跡できません。"); return; }
            var value = localPose.Value;
            float norm = Quaternion.Dot(value.rotation, value.rotation);
            if (!float.IsFinite(value.position.sqrMagnitude) || !float.IsFinite(norm) || norm < .99f || norm > 1.01f)
            { LoseTracking("コントローラーの姿勢が無効です。"); return; }
            if (originKnown && (Vector3.Distance(originPosition, trackingSpace.position) > .001f || Quaternion.Angle(originRotation, trackingSpace.rotation) > .1f || Vector3.Distance(originScale, trackingSpace.lossyScale) > .001f))
                Invalidate("追跡原点が変わりました。実手の3点を登録し直してください。");
            originKnown = true; originPosition = trackingSpace.position; originRotation = trackingSpace.rotation; originScale = trackingSpace.lossyScale;
            if (TipConfirmed && tipOffset != confirmedOffset) { TipConfirmed = false; Invalidate("筆先の設定が変わりました。確認・位置合わせをやり直してください。"); }
            if (Calibrated && (experiment.placement ? !experiment.placement.Unchanged : (Vector3.Distance(handPosition, experiment.hand.transform.position) > .001f || Quaternion.Angle(handRotation, experiment.hand.transform.rotation) > .1f || Vector3.Distance(handScale, experiment.hand.transform.lossyScale) > .001f)))
                Invalidate("仮想手が移動しました。3点を登録し直してください。");
            if (!double.IsNaN(previousSample) && (time < previousSample || time - previousSample > MaximumGap))
            {
                history.Clear();
                if (SessionActive) { LoseTracking("追跡データが100 ms以上途切れたため試行を中止しました。"); previousSample = time; return; }
            }
            previousSample = time; SampleTime = time; Tracked = true; latestLocalPose = value;
            RawTip = trackingSpace.TransformPoint(value.position + value.rotation * tipOffset);
            history.Add(time, RawTip);
            if (Pivoting)
            {
                pivotSamples.Add(value);
                if (time - pivotStarted >= 3)
                {
                    Pivoting = false;
                    if (BrushCalibration.TryPivot(pivotSamples, out var offset, out float residual))
                    {
                        tipOffset = offset; ConfirmMeasuredTip();
                        Status = $"筆先の推定誤差 {residual * 1000:F1} mm。次に実手の3点を登録してください。";
                    }
                    else Status = "筆先を固定したまま、縦・横の2方向に十分傾けて再試行してください。";
                }
            }
        }
        public void BeginPivot()
        {
            if (!CanConfigure() || !Tracked) { Status = "追跡中の右コントローラーが必要です。"; return; }
            Invalidate("筆先を机の1点に固定し、コントローラーを縦・横に傾けてください（3秒）。");
            TipConfirmed = false; pivotSamples.Clear(); pivotStarted = SampleTime; Pivoting = true;
        }
        public void ConfirmMeasuredTip()
        {
            if (!CanConfigure()) return;
            if (!float.IsFinite(tipOffset.sqrMagnitude) || tipOffset.magnitude > .6f) { Status = "筆先オフセットが無効です。"; return; }
            Invalidate("実手の対応する3点を順に登録してください。");
            confirmedOffset = tipOffset; TipConfirmed = true; Pivoting = false; history.Clear();
        }
        public void ClearHandCalibration()
        {
            if (!CanConfigure()) return;
            Invalidate("実手の対応する3点を順に登録してください。");
        }
        public void CaptureLandmark()
        {
            if (!CanConfigure() || !Tracked || !TipConfirmed || Pivoting) { Status = "先に筆先のキャリブレーションと追跡を確認してください。"; return; }
            if (CalibrationStep >= 3) { Status = "登録済みです。変更する場合は「3点を登録し直す」を押してください。"; return; }
            if (visualLandmarks.Length != 3 || !visualLandmarks[CalibrationStep]) { Status = "仮想手の基準点が不足しています。"; return; }
            realPoints[CalibrationStep] = RawTip;
            targetPoints[CalibrationStep] = visualLandmarks[CalibrationStep].position;
            CalibrationStep++;
            if (CalibrationStep < 3) { Status = $"{CalibrationStep}/3 点登録。次の対応位置に筆先を置いて登録してください。"; return; }
            Calibrated = BrushCalibration.TryMap(realPoints, targetPoints, out mappingRotation, out mappingTranslation);
            if (!Calibrated) { CalibrationStep = 0; Status = "点が近すぎるか一直線です。3点を登録し直してください。"; return; }
            AlignmentError=0;
            for(int i=0;i<3;i++)AlignmentError=Mathf.Max(AlignmentError,Vector3.Distance(mappingRotation*realPoints[i]+mappingTranslation,targetPoints[i]));
            if(AlignmentError>maximumAlignmentError)
            {Calibrated=false;CalibrationStep=0;Status=$"対応点の最大誤差 {AlignmentError*1000:F1} mm。手のサイズ・基準点を確認してください。";return;}
            calibratedWorldToHand=experiment.hand.transform.worldToLocalMatrix;
            if(experiment.placement)
                experiment.placement.SetNeutral(Quaternion.Inverse(mappingRotation)*(experiment.hand.transform.position-mappingTranslation),Quaternion.Inverse(mappingRotation)*experiment.hand.transform.rotation);
            handPosition = experiment.hand.transform.position; handRotation = experiment.hand.transform.rotation; handScale = experiment.hand.transform.lossyScale;
            if (experiment.realHandAnchor)
                experiment.realHandAnchor.SetPositionAndRotation(Quaternion.Inverse(mappingRotation) * (handPosition - mappingTranslation), Quaternion.Inverse(mappingRotation) * handRotation);
            history.Clear();
            Status = $"位置合わせ完了（最大誤差 {AlignmentError*1000:F1} mm）。確認用プレビューは明示的に有効にしてください。";
        }
        bool CanConfigure() => !ConfigurationLocked && !SessionActive && (!experiment || !experiment.Running);
        public bool Ready => TipConfirmed && Calibrated && Tracked && !Pivoting && tipOffset == confirmedOffset;
        public bool BeginSession(TrialSettings settings)
        {
            if (!Ready || settings.Delay > 3) { Status = "筆先と実手の位置合わせが必要です。遅延は0〜3秒です。"; return false; }
            EndSession(); ActiveDelay = settings.Delay; SessionActive = true; history.Clear(); history.Add(SampleTime, RawTip);
            nextFlush = SampleTime + 1;
            if (experiment.writeLog)
            {
                poseLog = new StreamWriter(Path.ChangeExtension(experiment.LogPath, ".brush.csv"));
                poseLog.WriteLine("trial,phase,render_s,latest_sample_s,visual_source_s,delay_s,raw_x,raw_y,raw_z,visual_tip_x,visual_tip_y,visual_tip_z");
                File.WriteAllText(Path.ChangeExtension(experiment.LogPath, ".brush.json"), JsonUtility.ToJson(new CalibrationRecord
                { tipOffset = tipOffset, realPoints = (Vector3[])realPoints.Clone(), visualPoints = (Vector3[])targetPoints.Clone(), rotation = mappingRotation, translation = mappingTranslation, delay = ActiveDelay }, true));
            }
            return true;
        }
        [Serializable] sealed class CalibrationRecord
        {
            public string input = "Right XR controller; physical brush contact is not sensed";
            public Vector3 tipOffset, translation;
            public Vector3[] realPoints, visualPoints;
            public Quaternion rotation;
            public float delay;
        }
        public bool Render(double now)
        {
            if (!SessionActive || !Tracked || now - SampleTime > MaximumGap) { LoseTracking("右コントローラーの追跡が途切れたため試行を中止しました。"); return false; }
            VisualSourceTime = SampleTime - ActiveDelay;
            if (!history.TrySample(VisualSourceTime, MaximumGap, out var real)) { experiment.ball.gameObject.SetActive(false); return false; }
            VisualTip = MapToVisual(real);
            experiment.ball.position = VisualTip + experiment.hand.transform.up * experiment.ballRadius;
            experiment.ball.gameObject.SetActive(true);
            if (poseLog != null)
            {
                string F(double v) => v.ToString("F6", CultureInfo.InvariantCulture);
            poseLog.WriteLine(string.Join(",", experiment.TrialId, experiment.Phase, F(now), F(SampleTime), F(VisualSourceTime), F(ActiveDelay),
                    F(RawTip.x), F(RawTip.y), F(RawTip.z), F(VisualTip.x), F(VisualTip.y), F(VisualTip.z)));
                if (now >= nextFlush) { poseLog.Flush(); nextFlush = now + 1; }
            }
            return true;
        }
        public void Preview()
        {
            if (experiment.Phase != TrialPhase.Ready || !experiment.ball) return;
            if(!calibrationPreview||ConfigurationLocked){experiment.ball.gameObject.SetActive(false);return;}
            bool visible = false;
            if (Ready)
            {
                VisualTip = MapToVisual(RawTip);
                experiment.ball.position = VisualTip + experiment.hand.transform.up * experiment.ballRadius; visible = true;
            }
            else if (TipConfirmed && CalibrationStep < visualLandmarks.Length && visualLandmarks[CalibrationStep])
            { experiment.ball.position = visualLandmarks[CalibrationStep].position + experiment.hand.transform.up * experiment.ballRadius; visible = true; }
            experiment.ball.gameObject.SetActive(visible);
        }
        void Invalidate(string reason)
        {
            Calibrated = false; CalibrationStep = 0; history.Clear();
            if(experiment&&experiment.placement)experiment.placement.Invalidate();
            CalibrationInvalidated?.Invoke(reason);
            if (SessionActive) experiment.BrushTrackingFailed(reason);
            Status = reason;
        }
        public Vector3 MapToVisual(Vector3 realWorld) => experiment.hand.transform.TransformPoint(calibratedWorldToHand.MultiplyPoint3x4(mappingRotation*realWorld+mappingTranslation));
        public Vector3 MapToReal(Vector3 visualWorld) => Quaternion.Inverse(mappingRotation)*(calibratedWorldToHand.inverse.MultiplyPoint3x4(experiment.hand.transform.InverseTransformPoint(visualWorld))-mappingTranslation);
        void LoseTracking(string reason)
        {
            Tracked = false; history.Clear(); previousSample = double.NaN;
            if (Pivoting) { Pivoting = false; pivotSamples.Clear(); }
            if (experiment && experiment.ball) experiment.ball.gameObject.SetActive(false);
            if (SessionActive) experiment.BrushTrackingFailed(reason);
            Status = reason;
        }
        public void EndSession()
        {
            SessionActive = false; history.Clear();
            try { poseLog?.Dispose(); } catch(Exception error) { Debug.LogWarning("Brush log close failed: " + error.Message); }
            finally { poseLog = null; }
        }
        void OriginUpdated(XRInputSubsystem subsystem) { Invalidate("VRが再センタリングされました。実手の3点を登録し直してください。"); }
        void OnDisable()
        {
            foreach (var subsystem in observedSubsystems) subsystem.trackingOriginUpdated -= OriginUpdated;
            observedSubsystems.Clear();
            LoseTracking("筆先の追跡が無効になりました。"); EndSession();
        }
    }
}
