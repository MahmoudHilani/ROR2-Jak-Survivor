using System;

namespace JakSurvivor
{
    public enum JakBoardPhase { Off, Riding, Dismounting }

    public static class JakBoardPhysics
    {
        // board-states.gc:3104-3105: the board closes when get-off ends.
        public const float DismountSeconds = 0.3f;

        public readonly struct Spring
        {
            public readonly float Offset, Velocity, BounceGain;
            public readonly bool Bounce;

            public Spring(float offset, float velocity, bool bounce = false, float bounceGain = 0f)
            {
                Offset = offset; Velocity = velocity; Bounce = bounce; BounceGain = bounceGain;
            }
        }

        public readonly struct State
        {
            public readonly JakBoardPhase Phase;
            public readonly float Cushion, Shock, ShockVelocity, VerticalSpeed;
            public readonly float AirborneSeconds, SecondsSinceLanding, LandingBounceGain, DismountAge;
            public readonly bool Grounded, AirPose, LandingBounce;

            public State(JakBoardPhase phase, float cushion = 0f, float shock = 0f, float shockVelocity = 0f,
                bool grounded = false, float verticalSpeed = 0f, float airborneSeconds = 0f,
                float secondsSinceLanding = float.PositiveInfinity, bool airPose = false, bool landingBounce = false,
                float landingBounceGain = 0f, float dismountAge = 0f)
            {
                Phase = phase; Cushion = cushion; Shock = shock; ShockVelocity = shockVelocity;
                Grounded = grounded; VerticalSpeed = verticalSpeed; AirborneSeconds = airborneSeconds;
                SecondsSinceLanding = secondsSinceLanding; DismountAge = phase == JakBoardPhase.Dismounting ? dismountAge : 0f;
                AirPose = airPose; LandingBounce = landingBounce; LandingBounceGain = landingBounceGain;
            }

            // target-board.gc:999-1002,1610-1613: cushion-base stays zero; drawn lift cannot be negative.
            public float Lift => Phase == JakBoardPhase.Off ? 0f : MathF.Max(0f, Cushion + Shock);
            public bool GetOffFinished => Phase == JakBoardPhase.Dismounting && DismountAge >= DismountSeconds;
            public bool QuickJumpWindow => CanQuickJump(Phase, ShockVelocity, SecondsSinceLanding);
        }

        // All source paths below are relative to Jak 3 engine/target/board unless otherwise stated.
        // engine/util/types-h.gc:27-31: divide game distances and speeds by 4096 to obtain meters.
        // board-h.gc:281; target-board.gc:2297: 1 -> 0 over 12288/4096 = 3 m.
        // engine/math/math.gc:563-583: lerp-scale clamps its interpolation fraction to [0,1].
        public static float CushionTarget(JakBoardPhase phase, bool grounded, float height) =>
            phase != JakBoardPhase.Riding ? 0f : grounded ? 1f : 1f - Clamp(height / 3f, 0f, 1f);

        // board-states.gc:682-695: AND, not OR; spin is outside ordinary riding's input model.
        // kernel/gkernel-h.gc:690-692: time-elapsed? includes equality, height comparison does not.
        public static bool UsesAirPose(bool grounded, float airborneSeconds, float height) =>
            !grounded && airborneSeconds >= 0.1f && height > 1f;

        // board-states.gc:967-970: rebound means positive shock velocity, not negative shock offset.
        // board-states.gc:968: landing-age must be strictly below .5 s.
        // board-states.gc:969; target-board.gc:2704: also requires .5 s since rail contact, which cannot fail without rails.
        public static bool CanQuickJump(JakBoardPhase phase, float shockVelocity, float secondsSinceLanding) =>
            phase == JakBoardPhase.Riding && shockVelocity > 0f && secondsSinceLanding < 0.5f;

        // target-board.gc:1582-1590: normalize the 1024-scaled volume.
        public static float BounceGain(float shockVelocity) => 1f + (0.3f - 1f) * Clamp((shockVelocity + 10f) / 7f, 0f, 1f);

        // Supply post-motion observations in meters, seconds and m/s; horizontalSpeed is a magnitude.
        public static State Step(State state, float seconds, JakBoardPhase phase, bool grounded, float height,
            float verticalSpeed, float horizontalSpeed = 0f)
        {
            if (phase == JakBoardPhase.Off) return new State(phase);
            if (state.Phase == JakBoardPhase.Off)
                state = new State(phase, grounded: grounded, verticalSpeed: verticalSpeed);

            float airborne = grounded ? 0f : state.AirborneSeconds + seconds;
            // board-states.gc:517-519,2159-2161: mark contact, not the later bottom-out bounce.
            float landed = grounded && !state.Grounded ? 0f : state.SecondsSinceLanding + seconds;
            // target-board.gc:2290,2298,2309: 20480/4096 = 5 m/s; 8192/4096 = 2 m/s.
            float rate = phase == JakBoardPhase.Riding && grounded ? 2f : 5f;
            // engine/math/math.gc:472-487,665-668: seek! approaches by at most rate * dt.
            float cushion = Seek(state.Cushion, CushionTarget(phase, grounded, height), rate * seconds);

            // target-board.gc:1621,1657,1666: arg0 is velocity BEFORE collision, transv AFTER it.
            // :1530-1542: v += .5 * (pre - post) + (.5 * g - 10 * x) * dt when grounded.
            // For vertical support, pre = previous - g*dt; the two gravity terms cancel.
            // Observations cannot reconstruct the native pre-collision velocity on an arbitrary slope;
            // StepSpring also accepts that velocity directly when a collision adapter can supply it.
            // In free flight pre == post: gravity/ollie acceleration must not become a collision impulse.
            float previous = grounded && seconds > 0f ? state.VerticalSpeed : verticalSpeed;
            var spring = phase == JakBoardPhase.Dismounting ? new Spring(Seek(state.Shock, 0f, 5f * seconds), 0f)
                : StepSpring(new Spring(state.Shock, state.ShockVelocity), seconds, grounded,
                previous, verticalSpeed, 0f, horizontalSpeed);

            return new State(phase, cushion, spring.Offset, spring.Velocity, grounded, verticalSpeed, airborne, landed,
                phase == JakBoardPhase.Riding && UsesAirPose(grounded, airborne, height), spring.Bounce, spring.BounceGain,
                phase == JakBoardPhase.Dismounting ? state.DismountAge + seconds : 0f);
        }

        // target-board.gc:1529-1542: gravity is a positive magnitude in m/s^2, velocities in m/s.
        // The .5 collision gain is an impulse coefficient; it is not a per-frame rate to multiply by 60.
        public static Spring StepSpring(Spring state, float seconds, bool grounded, float preCollisionVerticalSpeed,
            float postCollisionVerticalSpeed, float gravity, float horizontalSpeed = 0f)
        {
            float velocity = state.Velocity + 0.5f * (preCollisionVerticalSpeed - postCollisionVerticalSpeed)
                + (-10f * state.Offset + (grounded ? 0.5f * gravity : 0f)) * seconds;
            // target-board.gc:1549-1555: integrate only on a non-air surface, within .2 s of
            // last surface contact OR while the shock is negative. Grounded contact resets that timer.
            float shock = state.Offset + (grounded ? velocity * seconds : 0f);
            // target-board.gc:1557-1558: .99 -> .98 over 0 -> 28672/4096 = 7 m/s.
            // kernel/gkernel-h.gc:109: native 60 Hz. Per-second decay k = -60*ln(factor),
            // so factor^(60*dt) retains the original multiplication at dt=1/60.
            // The -10 spring coefficient already multiplies dt (:1536); do not multiply it by 60.
            float damping = 0.99f + (0.98f - 0.99f) * Clamp(horizontalSpeed / 7f, 0f, 1f);
            shock *= MathF.Pow(damping, 60f * seconds);
            bool bounce = false;
            float bounceGain = 0f;
            // target-board.gc:1561-1566,1583-1596: 40960/4096 = 10 m; -3072/4096 = -.75 m;
            // board-bounce requires velocity strictly below -12288/4096 = -3 m/s before resetting it.
            if (shock > 10f) { shock = 10f; velocity = 0f; }
            else if (shock < -0.75f)
            {
                bounce = velocity < -3f;
                if (bounce) bounceGain = BounceGain(velocity);
                shock = -0.75f; velocity = 0f;
            }

            return new Spring(shock, velocity, bounce, bounceGain);
        }

        private static float Clamp(float value, float min, float max) => MathF.Min(max, MathF.Max(min, value));
        private static float Seek(float value, float target, float distance) => value + Clamp(target - value, -distance, distance);
    }
}
