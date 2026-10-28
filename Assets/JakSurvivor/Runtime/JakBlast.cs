using System;

namespace JakSurvivor
{
    // Native art slot 476: base 2, step 2, speed .2500004768 at 60 Hz.
    internal static class JakBlast
    {
        internal const float SampleSeconds = 1f / (60f * 0.2500004768371582f);
        internal const float LaunchTime = 7f * SampleSeconds, ReleaseTime = 11f * SampleSeconds, RecoveryTime = 9f * SampleSeconds;
        internal const float Speed = 60f, Lifetime = 4f, InitialRadius = 2f, MaxRadius = 6f, Growth = 6f;
        internal static float Radius(float age) => Math.Min(MaxRadius, InitialRadius + Growth * Math.Max(0f, age));
        internal static float Flatten(float velocity, float dt) => Math.Sign(velocity) * Math.Max(0f, Math.Abs(velocity) - 20f * dt);
        internal static float Lift(float height, float ground, float radius, float dt) => Math.Min(Math.Max(0f, ground + radius * .5f - height), 10f * dt);
        internal static float SegmentFraction(float px, float py, float pz, float dx, float dy, float dz)
        {
            float length = dx * dx + dy * dy + dz * dz;
            return length <= 1e-8f ? 0f : Math.Max(0f, Math.Min(1f, (px * dx + py * dy + pz * dz) / length));
        }
        internal static bool CrossesLaunch(float age, float rate) => age * rate >= LaunchTime;
        // target-darkjak.gc:60-80 darkjak-ball-slide-reaction. Returns whether the contact is head-on enough to flag impact?.
        // Native mixes the unit slide y into a units/s velocity; dividing by 4096 units/m keeps that ratio in metres.
        internal static bool Slide(ref float vx, ref float vy, ref float vz, float nx, float ny, float nz)
        {
            float speed = (float)Math.Sqrt(vx * vx + vy * vy + vz * vz);
            if (speed <= 1e-8f) return false;
            float dot = vx * nx + vy * ny + vz * nz;
            bool headOn = dot / speed < -.3f;
            float sx = vx - nx * 1.2f * dot, sy = vy - ny * 1.2f * dot, sz = vz - nz * 1.2f * dot;
            float slide = (float)Math.Sqrt(sx * sx + sy * sy + sz * sz);
            float y = Math.Min(.4f, slide <= 1e-8f ? 0f : sy / slide) / 4096f;
            float scale = speed / (float)Math.Sqrt(vx * vx + y * y + vz * vz);
            if (float.IsInfinity(scale) || float.IsNaN(scale)) { vx = vz = 0f; vy = speed; return headOn; }
            vx *= scale; vy = y * scale; vz *= scale;
            return headOn;
        }
        // target-darkjak.gc:239-241: impact? is cleared within 6144 units (1.5 m) of the fire height above 16384 units/s (4 m/s).
        internal static bool Impacts(bool flagged, float height, float fireHeight, float speed) =>
            flagged && !(Math.Abs(height - fireHeight) < 1.5f && speed > 4f);
        // projectile.gc moving :trans, po4: 16 frames of 1/16 displacement, seeded with 4095996000 units; impact when touching a wall
        // and the sum is under 2048 units (.5 m per 60 Hz frame), or always under stop-speed 204.8 units (.05 m).
        internal sealed class Stall
        {
            private readonly float[] history = { Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed, Seed };
            private const float Seed = 4095996000f / 4096f;
            private int next;
            internal bool Record(float distance, bool touchingWall)
            {
                history[next] = .0625f * distance;
                next = (next + 1) & 15;
                float sum = 0f;
                foreach (float entry in history) sum += entry;
                return (touchingWall && sum < .5f) || sum < .05f;
            }
        }
        internal const float TrailStep = 1f / 60f;
        // Where launch index lands between last frame's point (0) and this one (1), given the emission carried in.
        internal static float TrailFraction(float carried, float delta, int index) =>
            delta <= 0f ? 1f : Math.Max(0f, Math.Min(1f, ((index + 1) * TrailStep - carried) / delta));
    }
}
