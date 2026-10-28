using UnityEngine;

namespace JakSurvivor
{
    internal static class JakJump
    {
        internal enum Kind { Ground, Double, Board, Roll }
        internal struct Hold
        {
            private Kind kind;
            private float takeoffHeight, minTarget, maxTarget;
            // A hold outlives one state's fixedAge, and Time.fixedTime drifts past the window's epsilon within minutes of play.
            private double startTime;
            private bool active;

            public float Start(Kind next, float height, float velocity, float extraHeight = 0f)
            {
                Definition definition = Get(next);
                // A double carries the rise it interrupts, so it peaks where an apex double would.
                float bonus = (next == Kind.Double ? Rise(velocity) : Bonus(velocity)) + extraHeight;
                kind = next; takeoffHeight = height; startTime = Time.fixedTimeAsDouble; active = true;
                minTarget = definition.MinHeight + bonus - MinOffset;
                maxTarget = definition.MaxHeight + bonus - CollideOffset;
                return InitialVelocity(minTarget);
            }

            public void Cancel() => active = false;
            public void Step(float height, bool down, bool grounded, ref float velocity)
            {
                if (!active) return;
                float elapsed = (float)(Time.fixedTimeAsDouble - startTime);
                if (!down || elapsed > HoldWindow + 0.000001f || grounded && elapsed > 0f)
                    active = false;
                else if (kind != Kind.Double || elapsed > 0f)
                    velocity = HoldVelocity(minTarget, maxTarget, elapsed, height - takeoffHeight);
            }
        }
        internal readonly struct Definition
        {
            public readonly float MinHeight, MaxHeight, Gain;
            public readonly string Sound;
            public readonly int StartAframe, DarkStartAframe;

            public Definition(float min, float max, string sound, float gain, int start, int darkStart)
            {
                MinHeight = min; MaxHeight = max; Sound = sound; Gain = gain;
                StartAframe = start; DarkStartAframe = darkStart;
            }
        }

        // Jak 3 tunes these for 60 m/s^2 gravity; heights are kept under RoR2's native gravity.
        public const float Gravity = 30f;
        // target.gc:662-695; kernel/gkernel-h.gc:105-112: 30 ticks at 300 Hz = .1 s.
        public const float HoldWindow = 0.1f;
        // target-util.gc:1336-1351,378: can-jump? uses last surface contact, including a crest.
        public const float GroundGraceWindow = 0.2f;
        // board-states.gc:1008,1020-1022: 8192/4096 = 2 m added to BOTH targets.
        public const float QuickHeight = 2f;
        public const float CollideOffset = 0.7f;
        public const float MinOffset = 0.6f;
        public const float VelocityCorrection = Gravity / 120f;
        private static readonly Definition[] Definitions = {
            new Definition(1.01f, 3.5f, "jump", 0.8f * 0.7f, 0, 0),
            new Definition(1f, 2.5f, "jump-double", 0.8f, 5, 7),
            // board/board-h.gc:274-276; board-states.gc:1017.
            new Definition(1.01f, 3.5f, "board-jump", 0.8f, 0, 0),
            // target-util.gc:370: roll-flip-height is 3.52 m.
            new Definition(1.01f, 3.52f, "roll-flip", 0.8f, 0, 0)
        };

        public static Definition Get(Kind kind) => Definitions[(int)kind];
        // CharacterMotor.OnLeaveStableGround spends the ground charge before a grace jump.
        public static int SpentCount(int jumpCount, bool grounded) => grounded ? jumpCount : Mathf.Max(0, jumpCount - 1);
        public static float QuailSpeed(int count, float acceleration, float airControl) => Mathf.Sqrt(10f * count * acceleration * airControl);
        public static float Rise(float velocity) => Mathf.Pow(Mathf.Max(0f, velocity), 2f) / (2f * Gravity);
        public static float Bonus(float velocity) => Mathf.Pow(Gravity / 600f + Mathf.Max(0f, velocity), 2f) / Gravity;
        public static float InitialVelocity(float minTarget) => Mathf.Sqrt(2f * Gravity * minTarget) + VelocityCorrection;
        public static float HoldVelocity(float minTarget, float maxTarget, float elapsed, float risen) =>
            Mathf.Sqrt(2f * Gravity * Mathf.Max(0f, Mathf.Lerp(minTarget, maxTarget, elapsed / HoldWindow) - risen)) + VelocityCorrection;
        public static float MaxGroundVelocity => InitialVelocity(Get(Kind.Ground).MaxHeight - CollideOffset);
    }
}
