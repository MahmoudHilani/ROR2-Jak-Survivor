using System;

namespace JakSurvivor
{
    // Simulation-only rules; no presentation, physics, skill-slot or networking side effects.
    public static class JakWeaponRules
    {
        public static float SampleRate(float rate) => float.IsNaN(rate) || float.IsInfinity(rate) ? 1f : Math.Max(0.01f, rate);
        public static float Charge(float elapsed, float rate) => Clamp01(elapsed * SampleRate(rate) / Balance.WaveFullCharge);
        public static bool CanRelease(float elapsed, float rate) => elapsed * SampleRate(rate) >= Balance.WaveMinimumCharge;
        public static float WaveRadius(float strength) => Balance.WaveBaseRadius + Balance.WaveRadiusGain * Clamp01(strength);
        public static float WaveLifetime(float strength) => Balance.WaveExpansionDuration * Clamp01(strength);
        public static float ExpandingRadius(float strength, float elapsed) => Math.Min(WaveRadius(strength),
            Balance.WaveBaseRadius + Balance.WaveRadiusGain * Math.Max(0f, elapsed) / Balance.WaveExpansionDuration);
        public static float WaveDamage(float strength, float distance)
        {
            float span = WaveRadius(strength) - Balance.WaveBaseRadius;
            float edge = span > 0f ? Clamp01((distance - Balance.WaveBaseRadius) / span) : 0f;
            return Math.Max(Balance.WaveMinimumDamage, Balance.WaveFullDamage * Clamp01(strength)) *
                (1f - (1f - Balance.WaveOuterRetention) * edge);
        }
        public static float ArcDamage(int index) => Balance.ArcDamage * (float)Math.Pow(Balance.ArcDamageRetention, index);
        public static bool Newer(uint value, uint previous) => value != 0 && unchecked((int)(value - previous)) > 0;
        private static float Clamp01(float value) => Math.Max(0f, Math.Min(1f, value));

        // Kept by the body, not the channel state. At most one pulse per simulation step;
        // after a delayed update we advance from now rather than repaying a burst of missed pulses.
        public struct PulseGate
        {
            public double Next { get; private set; }
            public bool TryPulse(double now, float sampledRate)
            {
                if (now < Next) return false;
                Next = now + Balance.ArcInterval / SampleRate(sampledRate);
                return true;
            }
        }

        // Reject stale starts, stops and duplicate commitments, including after ID wrap.
        public struct ActionSequence
        {
            public uint Latest { get; private set; }
            public uint Active { get; private set; }
            public bool Begin(uint id)
            {
                if (!Newer(id, Latest)) return false;
                Latest = Active = id;
                return true;
            }
            public bool End(uint id)
            {
                if (id == 0 || id != Active) return false;
                Active = 0;
                return true;
            }
            public bool ApplySnapshot(uint latest, uint active)
            {
                if (active != 0 && active != latest) return false;
                if (Newer(latest, Latest)) { Latest = latest; Active = active; return true; }
                if (latest != Latest || active != 0 && Active == 0) return false;
                Active = active;
                return true;
            }
        }
    }
}
