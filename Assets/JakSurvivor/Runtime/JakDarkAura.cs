using System;

namespace JakSurvivor
{
    public enum JakDarkEventKind { Sparkle, Probe, Between, DarkTint, ShockTint, End }

    public readonly struct JakDarkEvent
    {
        public readonly JakDarkEventKind Kind;
        public readonly long Tick;
        public readonly int SourceJoint, TargetJoint;

        public JakDarkEvent(JakDarkEventKind kind, long tick, int sourceJoint = -1, int targetJoint = -1)
        { Kind = kind; Tick = tick; SourceJoint = sourceJoint; TargetJoint = targetJoint; }
    }

    public sealed class JakDarkAura
    {
        public const int TicksPerFrame = 5;
        public const int MaxCatchUpFrames = 15;
        private readonly Random random;
        private double remainder;
        private long shockTick = -6;
        public long AgeTicks { get; private set; }
        public int LightningCount { get; private set; }

        public JakDarkAura(Random random) => this.random = random ?? throw new ArgumentNullException(nameof(random));

        public void Advance(double deltaSeconds, float darkInterp, bool visible, int nodeCount,
            Action<JakDarkEvent> emit, Action<long> frame = null)
        {
            if (double.IsNaN(deltaSeconds) || double.IsInfinity(deltaSeconds) || deltaSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (emit == null) throw new ArgumentNullException(nameof(emit));
            // Bound a stalled render's particle burst; discarded whole frames do not become later bursts.
            remainder += deltaSeconds * 60.0;
            double wholeFrames = Math.Floor(remainder + 1e-6);
            remainder = Math.Max(0.0, remainder - wholeFrames);
            int frames = (int)Math.Min(wholeFrames, MaxCatchUpFrames);
            long skipped = checked((long)wholeFrames) - frames;
            AgeTicks = checked(AgeTicks + skipped * TicksPerFrame);
            LightningCount -= (int)Math.Min(LightningCount, skipped);
            for (int i = 0; i < frames; i++)
            {
                AgeTicks += TicksPerFrame;
                // target-darkjak.gc:794,855-895: decay even when the aura is not drawn or dark.
                if (LightningCount > 0) LightningCount--;
                if (darkInterp >= 0.5f && visible)
                {
                    emit(new JakDarkEvent(JakDarkEventKind.Sparkle, AgeTicks, RandomPointJoint(random, nodeCount)));
                    // Both branch rolls precede their gate checks. The original wall function is a stub.
                    if (random.NextDouble() < 0.25)
                    {
                        if (AgeTicks - shockTick >= 6) shockTick = AgeTicks;
                    }
                    else if (random.NextDouble() < 0.1 && AgeTicks - shockTick >= 6)
                    {
                        shockTick = AgeTicks;
                        emit(new JakDarkEvent(JakDarkEventKind.Probe, AgeTicks, ProbeJoint(random, nodeCount)));
                    }
                }
                frame?.Invoke(AgeTicks);
            }
        }

        public bool TrySpendVictimBolt()
        {
            // target-handler.gc:292-313 tests the previous count, allowing nine to become eleven.
            if (LightningCount >= 10) return false;
            LightningCount += 2;
            return true;
        }

        // generic-obs.gc:3128-3149 uses a bounds-sphere fallback for short node lists.
        internal static int RandomPointJoint(Random random, int nodeCount) =>
            nodeCount >= 7 ? random.Next(3, nodeCount) : -1;

        // target-part.gc:3141-3144 samples both complete joint lists, unlike probe's half-list.
        internal static int BetweenJoint(Random random, int nodeCount) =>
            nodeCount >= 4 ? random.Next(3, nodeCount) : -1;

        // target-part.gc:3030 uses inclusive integer division: Jak's 65 nodes yield 3..32.
        internal static int ProbeJoint(Random random, int nodeCount) =>
            nodeCount >= 7 ? random.Next(3, (nodeCount - 1) / 2 + 1) : -1;
    }
}
