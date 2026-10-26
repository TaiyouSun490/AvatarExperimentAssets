using System;
using UnityEngine;

namespace DisappearingHand
{
    public enum TrialPhase { Ready, Baseline, Drift, Hold, Search, Reveal, Complete, Aborted }
    public enum DriftPresentation { Continuous, LookAwaySteps }

    [Serializable]
    public sealed class TrialSettings
    {
        public bool targetLeft;
        public bool controlCondition;
        public DriftPresentation presentation;
        public float lookAwayStepMetres = .01f;
        public float lookAwayDwellSeconds = .25f;
        public float lookBackDwellSeconds = .25f;
        public float lookAwayTimeoutSeconds = 180;
        public float handBoundsRadius = .25f;
        [Range(0, .20f)] public float displacement = .12f;
        [Min(.1f)] public float baselineSeconds = 5;
        [Min(1)] public float driftSeconds = 40;
        [Min(.1f)] public float holdSeconds = 3;
        [Min(1)] public float searchSeconds = 15;
        [Min(.1f)] public float revealSeconds = 2;

        public TrialSettings Snapshot()
        {
            var s = (TrialSettings)MemberwiseClone();
            s.displacement = Safe(s.displacement, 0, .20f, .12f);
            s.baselineSeconds = Safe(s.baselineSeconds, .1f, 120, 5);
            s.driftSeconds = Safe(s.driftSeconds, 1, 180, 40);
            s.holdSeconds = Safe(s.holdSeconds, .1f, 60, 3);
            s.searchSeconds = Safe(s.searchSeconds, 1, 120, 15);
            s.revealSeconds = Safe(s.revealSeconds, .1f, 20, 2);
            s.lookAwayStepMetres = Safe(s.lookAwayStepMetres, .001f, .03f, .01f);
            s.lookAwayDwellSeconds = Safe(s.lookAwayDwellSeconds, .1f, 2, .25f);
            s.lookBackDwellSeconds = Safe(s.lookBackDwellSeconds, .1f, 2, .25f);
            s.lookAwayTimeoutSeconds = Safe(s.lookAwayTimeoutSeconds, 5, 600, 180);
            s.handBoundsRadius = Safe(s.handBoundsRadius, .15f, .5f, .25f);
            if (!Enum.IsDefined(typeof(DriftPresentation), s.presentation)) s.presentation = DriftPresentation.Continuous;
            return s;
        }
        static float Safe(float v, float min, float max, float fallback) =>
            float.IsNaN(v) || float.IsInfinity(v) ? fallback : Mathf.Clamp(v, min, max);
    }

    /// <summary>Owns trial timing only. Tracking and rendered positions remain separate.</summary>
    public sealed class TrialProtocol
    {
        public TrialPhase Phase { get; private set; } = TrialPhase.Ready;
        public TrialSettings Settings { get; private set; } = new TrialSettings();
        public float PhaseTime { get; private set; }
        public float Elapsed { get; private set; }
        public bool Active => Phase >= TrialPhase.Baseline && Phase <= TrialPhase.Reveal;
        public bool Hidden => Phase == TrialPhase.Search;
        public event Action<TrialPhase> Changed;
        float searchShift;
        public int HiddenSteps { get; private set; }
        public float NextHiddenShift => Settings.controlCondition ? 0 : Mathf.Min(Settings.displacement, (HiddenSteps + 1) * Settings.lookAwayStepMetres);
        public bool HiddenStepsComplete => HiddenSteps >= Mathf.Max(1, Mathf.CeilToInt(Settings.displacement / Settings.lookAwayStepMetres - .00001f));
        public float Shift
        {
            get
            {
                float full = Settings.controlCondition ? 0 : Settings.displacement;
                switch (Phase)
                {
                    case TrialPhase.Drift: return Settings.presentation == DriftPresentation.LookAwaySteps
                        ? Mathf.Min(full, HiddenSteps * Settings.lookAwayStepMetres) : full * Mathf.Clamp01(PhaseTime / Settings.driftSeconds);
                    case TrialPhase.Hold: return full;
                    case TrialPhase.Search: return searchShift;
                    case TrialPhase.Reveal: return searchShift * (1 - Mathf.SmoothStep(0, 1, PhaseTime / Settings.revealSeconds));
                    default: return 0;
                }
            }
        }
        public Vector3 Offset(bool left, Vector3 tableRight) => tableRight * Shift * (left ? 1 : -1);
        public bool IsVisible(bool left) => !(Hidden && left == Settings.targetLeft);
        public bool Start(TrialSettings settings)
        {
            if (Active) return false;
            Settings = settings.Snapshot(); Elapsed = 0; searchShift = 0; HiddenSteps = 0;
            Set(TrialPhase.Baseline); return true;
        }
        public void Tick(float deltaTime)
        {
            if (!Active || deltaTime <= 0 || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            // Consume overshoot so timing does not depend on display frame rate.
            while (Active && deltaTime > 0)
            {
                if (Phase == TrialPhase.Drift && Settings.presentation == DriftPresentation.LookAwaySteps)
                {
                    PhaseTime += deltaTime; Elapsed += deltaTime;
                    // No elapsed-time fallback that could shift a hand while visible.
                    if (PhaseTime >= Settings.lookAwayTimeoutSeconds) Abort();
                    break;
                }
                float remaining = Duration() - PhaseTime;
                float step = Mathf.Min(deltaTime, remaining);
                PhaseTime += step; Elapsed += step; deltaTime -= step;
                if (PhaseTime + .000001f < Duration()) break;
                switch (Phase)
                {
                    case TrialPhase.Baseline: Set(TrialPhase.Drift); break;
                    case TrialPhase.Drift: Set(TrialPhase.Hold); break;
                    case TrialPhase.Hold: HideNow(); break;
                    case TrialPhase.Search: Set(TrialPhase.Reveal); break;
                    case TrialPhase.Reveal: Set(TrialPhase.Complete); break;
                }
            }
        }
        public bool AdvanceHiddenStep()
        {
            if (Phase != TrialPhase.Drift || Settings.presentation != DriftPresentation.LookAwaySteps || HiddenStepsComplete) return false;
            HiddenSteps++; return true;
        }
        public bool FinishHiddenDrift()
        {
            if (Phase != TrialPhase.Drift || Settings.presentation != DriftPresentation.LookAwaySteps || !HiddenStepsComplete) return false;
            Set(TrialPhase.Hold); return true;
        }
        public bool HideNow()
        {
            if (Phase < TrialPhase.Baseline || Phase > TrialPhase.Hold) return false;
            searchShift = Shift; Set(TrialPhase.Search); return true;
        }
        public bool ConfirmSearch()
        {
            if (Phase != TrialPhase.Search) return false;
            Set(TrialPhase.Reveal); return true;
        }
        public void Abort() { if (Active) Set(TrialPhase.Aborted); }
        public void Reset() { searchShift = 0; Elapsed = 0; HiddenSteps = 0; Set(TrialPhase.Ready); }
        float Duration()
        {
            switch (Phase)
            {
                case TrialPhase.Baseline: return Settings.baselineSeconds;
                case TrialPhase.Drift: return Settings.driftSeconds;
                case TrialPhase.Hold: return Settings.holdSeconds;
                case TrialPhase.Search: return Settings.searchSeconds;
                case TrialPhase.Reveal: return Settings.revealSeconds;
                default: return 0;
            }
        }
        void Set(TrialPhase value) { Phase = value; PhaseTime = 0; Changed?.Invoke(value); }
    }
}
