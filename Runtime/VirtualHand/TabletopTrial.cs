using System;
using UnityEngine;

namespace AvatarExperiments.VirtualHand
{
    public enum Synchrony { Synchronous, Asynchronous }
    public enum MeltAppearance { Viscous, MeltingTest, MeltShaderUrp }
    public enum TrialPhase { Ready, Preparation, Induction, Melting, Observation, Completed, Aborted }
    public enum VibrationCondition { None, MeltSynchronous, MeltDelayed, ThroughoutTrial }

    [Serializable]
    public sealed class TrialSettings
    {
        public Synchrony synchrony;
        public MeltAppearance appearance;
        public VibrationCondition vibration;
        public bool enableMelting = true;
        [Tooltip("Metres in the calibrated neutral hand frame: X lateral, Y above table, Z along fingers.")]
        public Vector3 handOffset;
        public float handYaw;
        [Min(0)] public float preparation = 3;
        [Min(.1f)] public float induction = 30;
        [Min(.1f)] public float melting = 6;
        [Min(0)] public float observation = 5;
        [Range(0, 3)] public float asynchronousDelay = .5f;
        [Min(0)] public float vibrationDelay = .5f;
        [Range(0, 1)] public float vibrationAmplitude = .2f;
        [Range(1, 1000)] public float vibrationFrequency = 70;
        public float Delay => synchrony == Synchrony.Asynchronous ? asynchronousDelay : 0;
        public double MeltOnset => (double)preparation + induction;
        public double End => MeltOnset + melting + observation;
        public TrialSettings Copy() => JsonUtility.FromJson<TrialSettings>(JsonUtility.ToJson(this));

        public void ValidateTracked()
        {
            if (!Enum.IsDefined(typeof(Synchrony), synchrony) || !Enum.IsDefined(typeof(MeltAppearance), appearance))
                throw new ArgumentException("Unknown visual condition.");
            foreach (float value in new[] { preparation, induction, melting, observation, asynchronousDelay })
                if (!float.IsFinite(value) || value < 0) throw new ArgumentException("Timings must be finite and nonnegative.");
            if (induction <= 0 || melting <= 0 || asynchronousDelay > 3)
                throw new ArgumentException("Induction/melting must be positive; visual delay is limited to 0..3 s.");
            ValidateVibration();
            if(!float.IsFinite(handOffset.sqrMagnitude)||!float.IsFinite(handYaw))throw new ArgumentException("Invalid hand offset.");
        }
        public void ValidateVibration()
        {
            if (!Enum.IsDefined(typeof(VibrationCondition), vibration) || !float.IsFinite(vibrationDelay) || vibrationDelay < 0 ||
                !float.IsFinite(vibrationAmplitude) || vibrationAmplitude < 0 || vibrationAmplitude > 1 ||
                !float.IsFinite(vibrationFrequency) || vibrationFrequency < 1 || vibrationFrequency > 1000)
                throw new ArgumentException("Invalid vibration condition, amplitude or frequency.");
            if (vibration == VibrationCondition.MeltDelayed && vibrationDelay > observation)
                throw new ArgumentException("Observation time must include the entire delayed vibration tail.");
        }
        public bool VibrationAt(double elapsed)
        {
            if (!double.IsFinite(elapsed) || elapsed < 0 || elapsed >= End || vibration == VibrationCondition.None) return false;
            if (vibration == VibrationCondition.ThroughoutTrial) return true;
            double onset = MeltOnset + (vibration == VibrationCondition.MeltDelayed ? vibrationDelay : 0);
            return elapsed >= onset && elapsed < onset + melting;
        }
    }
    public static class TrialTimeline
    {
        public static TrialPhase Phase(TrialSettings settings, double elapsed)
        {
            if (elapsed < settings.preparation) return TrialPhase.Preparation;
            if (elapsed < settings.MeltOnset) return TrialPhase.Induction;
            if (elapsed < settings.MeltOnset + settings.melting) return TrialPhase.Melting;
            return elapsed < settings.End ? TrialPhase.Observation : TrialPhase.Completed;
        }
    }
}
