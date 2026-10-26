using System;
using UnityEngine;

namespace DisappearingHand
{
    public enum OriginalDhtCondition { DisappearedHand, SeenHand, CoveredHand }
    public enum OriginalDhtPhase { Ready, Adaptation, AwaitTableRest, Fade, AwaitCover, AwaitReach, Reach, Report, Complete, Aborted }

    /// <summary>Newport & Gilpin 2011, Supplemental Experimental Procedures p.5.
    /// The Fig.S1 caption says 25 mm/s; methods and 6.25 cm / 25 s agree on 2.5 mm/s.
    /// Operator gates represent physical actions; elapsed time never confirms contact.</summary>
    public sealed class OriginalDhtProtocol
    {
        public const float AdaptationSeconds = 25, FadeSeconds = 7;
        public const float HandSpeed = .0025f, BarSpeed = .0019f;
        public const float MaxShift = HandSpeed * AdaptationSeconds;
        public const float HalfSeparation = .08f, HoverHeight = .10f, FinalBarGap = .175f;
        public OriginalDhtPhase Phase { get; private set; }
        public OriginalDhtCondition Condition { get; private set; }
        public float PhaseTime { get; private set; }
        public float Elapsed { get; private set; }
        public bool Active => Phase >= OriginalDhtPhase.Adaptation && Phase <= OriginalDhtPhase.Report;
        public event Action<OriginalDhtPhase> Changed;
        public float AdaptedTime => !Active ? 0 : Phase == OriginalDhtPhase.Adaptation ? PhaseTime : AdaptationSeconds;
        public float Shift => Condition == OriginalDhtCondition.DisappearedHand ? HandSpeed * AdaptedTime : 0;
        public float BarGap => FinalBarGap + BarSpeed * (AdaptationSeconds - AdaptedTime);
        public float BarsBrightness => Phase == OriginalDhtPhase.Adaptation || Phase == OriginalDhtPhase.AwaitTableRest ? 1 :
            Phase == OriginalDhtPhase.Fade ? Mathf.Clamp01(1 - PhaseTime / FadeSeconds) : 0;
        public float RightBrightness => !Active || Condition == OriginalDhtCondition.SeenHand ? 1 :
            Condition == OriginalDhtCondition.CoveredHand ? (Phase >= OriginalDhtPhase.AwaitReach ? 0 : 1) :
            Phase < OriginalDhtPhase.Fade ? 1 : Phase == OriginalDhtPhase.Fade ? BarsBrightness : 0;
        public Vector3 Offset(bool left, Vector3 right) => right * Shift * (left ? 1 : -1);
        public bool Start(OriginalDhtCondition condition)
        {
            if (Active || !Enum.IsDefined(typeof(OriginalDhtCondition), condition)) return false;
            Condition = condition; Elapsed = 0; Set(OriginalDhtPhase.Adaptation); return true;
        }
        public void Tick(float dt)
        {
            if (!Active || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            while (dt > 0)
            {
                float duration = Phase == OriginalDhtPhase.Adaptation ? AdaptationSeconds : Phase == OriginalDhtPhase.Fade ? FadeSeconds : float.PositiveInfinity;
                float step = Mathf.Min(dt, duration - PhaseTime);
                PhaseTime += step; Elapsed += step; dt -= step;
                if (PhaseTime < duration) break;
                Set(Phase == OriginalDhtPhase.Adaptation ? OriginalDhtPhase.AwaitTableRest :
                    Condition == OriginalDhtCondition.CoveredHand ? OriginalDhtPhase.AwaitCover : OriginalDhtPhase.AwaitReach);
            }
        }
        public bool ConfirmTableRest() => Advance(OriginalDhtPhase.AwaitTableRest, OriginalDhtPhase.Fade);
        public bool ConfirmClothPlaced() => Advance(OriginalDhtPhase.AwaitCover, OriginalDhtPhase.AwaitReach);
        public bool BeginReach() => Advance(OriginalDhtPhase.AwaitReach, OriginalDhtPhase.Reach);
        public bool FinishReach() => Advance(OriginalDhtPhase.Reach, OriginalDhtPhase.Report);
        public bool Complete() => Advance(OriginalDhtPhase.Report, OriginalDhtPhase.Complete);
        public void Abort() { if (Active) Set(OriginalDhtPhase.Aborted); }
        public void Reset() { Elapsed = 0; Set(OriginalDhtPhase.Ready); }
        bool Advance(OriginalDhtPhase from, OriginalDhtPhase to)
        { if (Phase != from) return false; Set(to); return true; }
        void Set(OriginalDhtPhase phase) { Phase = phase; PhaseTime = 0; Changed?.Invoke(phase); }
    }
}
