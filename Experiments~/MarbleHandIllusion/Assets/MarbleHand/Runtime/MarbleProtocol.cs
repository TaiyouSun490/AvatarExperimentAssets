using System;
using System.Collections.Generic;

namespace MarbleHand
{
    public enum MarbleCondition { SynchronousMarble, DelayedSound, UnchangedSkin }
    public enum HapticBackend { Simulation, Esp32, XrController }
    public enum TrialPhase { Ready, PreRatings, Induction, PostRatings, Complete, Aborted }

    [Serializable]
    public sealed class MarbleSettings
    {
        public MarbleCondition condition;
        public HapticBackend backend;
        public bool soundEnabled = true, visualEnabled = true, vibrationEnabled = true;
        public int seed = 281028, channel;
        public float duration = 300, minInterval = .8f, maxInterval = 1.6f;
        public int rampHits = 30;
        public float pulseSeconds = .05f, frequency = 160, amplitude = .1f, volume = .5f;
        public MarbleSettings Snapshot()
        {
            var c = (MarbleSettings)MemberwiseClone();
            Check(c.duration, 3, 1800, "duration");
            Check(c.minInterval, .4f, 5, "minInterval");
            Check(c.maxInterval, c.minInterval, 5, "maxInterval");
            Check(c.pulseSeconds, .01f, .2f, "pulseSeconds");
            Check(c.frequency, 20, 300, "frequency");
            Check(c.amplitude, 0, .3f, "amplitude");
            Check(c.volume, 0, 1, "volume");
            if (c.channel < 0 || c.channel > 15 || c.rampHits < 2 || c.rampHits > 1000 ||
                !Enum.IsDefined(typeof(MarbleCondition), c.condition) || !Enum.IsDefined(typeof(HapticBackend), c.backend))
                throw new ArgumentException("Invalid channel, ramp length or condition/backend.");
            return c;
        }
        static void Check(float v, float min, float max, string name)
        { if (float.IsNaN(v) || float.IsInfinity(v) || v < min || v > max) throw new ArgumentOutOfRangeException(name); }
    }

    public readonly struct MarbleImpact
    {
        public readonly int id, clip;
        public readonly double contactTime, soundTime;
        public readonly float blend, gain;
        public MarbleImpact(int id, int clip, double contact, double sound, float blend, float gain)
        { this.id = id; this.clip = clip; contactTime = contact; soundTime = sound; this.blend = blend; this.gain = gain; }
    }

    /// <summary>Seeded, immutable trial plan. Times are seconds from the induction start DSP timestamp.</summary>
    public static class MarbleProtocol
    {
        public static List<MarbleImpact> Plan(MarbleSettings input, int clipCount)
        {
            var s = input.Snapshot();
            if (clipCount < 1) throw new ArgumentException("At least one sound clip is required.");
            var timing = new Random(s.seed);
            // Separate streams keep contacts, clip selection and ramp identical across conditions.
            var delay = new Random(s.seed ^ 7919); var clips = new Random(s.seed ^ 104729);
            var result = new List<MarbleImpact>();
            double contact = 1;
            while (contact < s.duration)
            {
                int id = result.Count + 1;
                float gain = Math.Min(1, (id - 1f) / (s.rampHits - 1));
                double lag = .8 + .8 * delay.NextDouble();
                result.Add(new MarbleImpact(id, clips.Next(clipCount), contact,
                    contact + (s.condition == MarbleCondition.DelayedSound ? lag : 0),
                    !s.visualEnabled || s.condition == MarbleCondition.UnchangedSkin ? 0 : gain, gain));
                contact += s.minInterval + (s.maxInterval - s.minInterval) * timing.NextDouble();
            }
            return result;
        }
        public static double EndTime(MarbleSettings s, IList<MarbleImpact> plan, double clipLength)
        {
            double end = s.duration;
            if (!s.soundEnabled) return end;
            // Delayed sounds can reorder when using shorter demo intervals.
            foreach (var hit in plan) end = Math.Max(end, hit.soundTime + clipLength);
            return end;
        }
    }
}
