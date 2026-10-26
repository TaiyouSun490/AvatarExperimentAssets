using System;
using UnityEngine;

namespace TendonHand
{
    public enum RecoveryView { HoldDisplaced, Hidden, Veridical }
    public enum PivotJoint { Forearm, Wrist }
    public enum TendonPhase { Ready, Baseline, Starting, Induction, Stopping, Recovery, Complete, Aborted }

    [Serializable]
    public sealed class TendonSettings
    {
        public bool targetLeft, vibration = true, visualMotion = true;
        public RecoveryView recovery = RecoveryView.HoldDisplaced;
        public PivotJoint joint = PivotJoint.Forearm;
        public float angleDegrees = -25, forearmMetres = .25f;
        public float baselineSeconds = 5, inductionSeconds = 15, recoverySeconds = 60;
        public float frequencyHz = 80, amplitude = .1f;
        public int channel;
        public float movementToleranceMetres = .05f, rotationToleranceDegrees = 20;
        public TendonSettings Snapshot()
        {
            var s = (TendonSettings)MemberwiseClone();
            s.angleDegrees = Safe(s.angleDegrees, -60, 60, -25);
            s.forearmMetres = Safe(s.forearmMetres, .15f, .40f, .25f);
            s.baselineSeconds = Safe(s.baselineSeconds, .2f, 120, 5);
            s.inductionSeconds = Safe(s.inductionSeconds, .5f, 120, 15);
            s.recoverySeconds = Safe(s.recoverySeconds, .5f, 300, 60);
            s.frequencyHz = Safe(s.frequencyHz, 1, 1000, 80);
            s.amplitude = Safe(s.amplitude, 0, 1, .1f);
            s.movementToleranceMetres = Safe(s.movementToleranceMetres, .01f, .2f, .05f);
            s.rotationToleranceDegrees = Safe(s.rotationToleranceDegrees, 3, 90, 20);
            s.channel = Mathf.Clamp(s.channel, 0, 15);
            if (!Enum.IsDefined(typeof(RecoveryView), s.recovery)) s.recovery = RecoveryView.HoldDisplaced;
            if (!Enum.IsDefined(typeof(PivotJoint), s.joint)) s.joint = PivotJoint.Forearm;
            return s;
        }
        static float Safe(float v, float lo, float hi, float fallback) =>
            float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp(v, lo, hi);
    }

    // Starting/Stopping are explicit barriers: serial completion is not physical onset/offset.
    public sealed class TendonProtocol
    {
        public TendonPhase Phase { get; private set; }
        public TendonSettings Settings { get; private set; } = new TendonSettings();
        public float PhaseTime { get; private set; }
        public float Elapsed { get; private set; }
        public bool Active => Phase >= TendonPhase.Baseline && Phase <= TendonPhase.Recovery;
        public event Action<TendonPhase> Changed;
        float stoppedAngle;
        public float Angle
        {
            get
            {
                if (Phase == TendonPhase.Induction)
                    return (Settings.visualMotion ? Settings.angleDegrees : 0) * Mathf.Clamp01(PhaseTime / Settings.inductionSeconds);
                if (Phase == TendonPhase.Stopping || Phase == TendonPhase.Recovery) return stoppedAngle;
                return 0;
            }
        }
        public bool TargetVisible => Phase != TendonPhase.Recovery || Settings.recovery != RecoveryView.Hidden;
        public bool UsesLivePose => !Active || Phase == TendonPhase.Baseline ||
            (Phase == TendonPhase.Recovery && Settings.recovery == RecoveryView.Veridical);
        public bool Start(TendonSettings settings)
        {
            if (Active) return false;
            Settings = settings.Snapshot(); Elapsed = stoppedAngle = 0; Set(TendonPhase.Baseline); return true;
        }
        public void Tick(float dt)
        {
            if (!Active || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            PhaseTime += dt; Elapsed += dt;
            // Never consume serial barriers or skip the visible induction on a slow frame.
            if (Phase == TendonPhase.Baseline && PhaseTime >= Settings.baselineSeconds) Set(TendonPhase.Starting);
            else if (Phase == TendonPhase.Induction && PhaseTime >= Settings.inductionSeconds) StopInduction();
            else if (Phase == TendonPhase.Recovery && PhaseTime >= Settings.recoverySeconds) Set(TendonPhase.Complete);
        }
        public void OutputStarted() { if (Phase == TendonPhase.Starting) Set(TendonPhase.Induction); }
        public void OutputStopped() { if (Phase == TendonPhase.Stopping) Set(TendonPhase.Recovery); }
        public void StopInduction()
        {
            if (Phase != TendonPhase.Induction) return;
            stoppedAngle = Angle; Set(TendonPhase.Stopping);
        }
        public void Abort() { if (Active) Set(TendonPhase.Aborted); }
        public void Reset() { Elapsed = stoppedAngle = 0; Set(TendonPhase.Ready); }
        void Set(TendonPhase phase) { Phase = phase; PhaseTime = 0; Changed?.Invoke(phase); }

        public static Pose DisplayPose(Pose baseline, Vector3 pivot, Vector3 axis, float angle)
        {
            var rotation = Quaternion.AngleAxis(angle, axis);
            return new Pose(pivot + rotation * (baseline.position - pivot), rotation * baseline.rotation);
        }
    }
}
