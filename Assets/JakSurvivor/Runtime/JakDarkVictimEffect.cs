using System;

namespace JakSurvivor
{
    public sealed class JakDarkVictimEffect
    {
        private readonly Random random;
        private long boltTick, previousAuraTick;
        public long AgeTicks { get; private set; }
        public bool Active { get; private set; } = true;

        public JakDarkVictimEffect(Random random, double initialAgeSeconds = 0, long initialAuraTick = 0)
        {
            this.random = random ?? throw new ArgumentNullException(nameof(random));
            if (double.IsNaN(initialAgeSeconds) || double.IsInfinity(initialAgeSeconds) || initialAgeSeconds < 0)
                throw new ArgumentOutOfRangeException(nameof(initialAgeSeconds));
            if (initialAuraTick < 0) throw new ArgumentOutOfRangeException(nameof(initialAuraTick));
            AgeTicks = (long)Math.Min(4500.0, Math.Floor(initialAgeSeconds * 300.0 + 1e-6));
            previousAuraTick = initialAuraTick;
            // Replicated late entry starts at its current phase without replaying earlier particles or bolts.
            boltTick = AgeTicks - 15;
        }

        public void AdvanceFrame(JakDarkAura aura, bool reactingOrDead, bool visible,
            int jakNodeCount, int victimNodeCount, Action<JakDarkEvent> emit)
        {
            if (aura == null) throw new ArgumentNullException(nameof(aura));
            if (emit == null) throw new ArgumentNullException(nameof(emit));
            if (!Active) return;
            long elapsed = aura.AgeTicks - previousAuraTick;
            if (elapsed <= 0) return;
            previousAuraTick = aura.AgeTicks;
            AgeTicks += Math.Min(4500 - AgeTicks, elapsed);
            // target-handler.gc:410-445 ANDs reaction, the fifteen-second cap, and drawn status.
            if (!reactingOrDead || !visible || AgeTicks >= 4500)
            {
                Active = false;
                emit(new JakDarkEvent(JakDarkEventKind.End, aura.AgeTicks));
                return;
            }
            emit(new JakDarkEvent(JakDarkEventKind.Sparkle, aura.AgeTicks, JakDarkAura.RandomPointJoint(random, victimNodeCount)));
            emit(new JakDarkEvent(JakDarkEventKind.DarkTint, aura.AgeTicks));
            // target-handler.gc:276-320 consumes the gate even when the shared budget is exhausted.
            if (AgeTicks - boltTick < 15) return;
            boltTick = AgeTicks;
            if (!aura.TrySpendVictimBolt()) return;
            if (AgeTicks < 150)
                emit(new JakDarkEvent(JakDarkEventKind.Between, aura.AgeTicks,
                    JakDarkAura.BetweenJoint(random, jakNodeCount), JakDarkAura.BetweenJoint(random, victimNodeCount)));
            else
            {
                emit(new JakDarkEvent(JakDarkEventKind.ShockTint, aura.AgeTicks));
                emit(new JakDarkEvent(JakDarkEventKind.Probe, aura.AgeTicks, JakDarkAura.ProbeJoint(random, victimNodeCount)));
            }
        }
    }
}
