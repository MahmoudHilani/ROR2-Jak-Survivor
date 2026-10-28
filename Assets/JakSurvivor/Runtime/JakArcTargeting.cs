using System;
using System.Numerics;

namespace JakSurvivor
{
    internal struct ArcCandidate
    {
        internal readonly int Health, Box;
        internal readonly Vector3 Point;
        internal readonly float Radius;
        internal float Distance, Score;
        internal byte Flags;
        internal ArcCandidate(int health, int box, Vector3 point, float radius = 0f)
        { Health = health; Box = box; Point = point; Radius = Math.Max(0f, radius); Distance = Score = 0f; Flags = 0; }
    }
    internal struct ArcFocus { internal int Health, Box; }
    internal interface IArcWorld
    {
        bool Visible(Vector3 from, int candidate);
    }
    // Shared native gun acquisition feeds the Arc rope; it is separate from knot attraction.
    internal static class JakArcTargeting
    {
        private const byte Eligible = 1, Occluded = 2;
        // pickup-type gun-blue-2 = 33. target-gun.gc L811-814, L1672-1680:
        // track-find-range 327680 + 32768 = 88m; normal ground cull-angle = 30 degrees.
        // L1646-1654: air/direct caps range at 122880 (30m), with a 90-degree cone.
        internal const float Reach = 88f, ConeDegrees = 30f, AirReach = 30f;
        private static readonly float ConeTangent = (float)Math.Tan(ConeDegrees * Math.PI / 180.0);

        internal static int Select(Vector3 origin, Vector3 direction, ArcCandidate[] candidates, int count,
            IArcWorld world, ArcFocus focus, bool airborne = false)
        {
            float length = direction.Length();
            if (length <= 0f) return -1;
            direction /= length;
            for (int i = 0; i < count; i++)
            {
                ref ArcCandidate candidate = ref candidates[i];
                candidate.Flags = 0;
                candidate.Distance = 0f;
                Vector3 offset = candidate.Point - origin;
                float squared = offset.LengthSquared();
                if (candidate.Health == 0 || candidate.Box == 0 || squared <= 0f) continue;
                candidate.Distance = (float)Math.Sqrt(squared);
                float forward = Vector3.Dot(offset, direction);
                float cosine = Math.Max(-1f, Math.Min(1f, forward / candidate.Distance));
                float surfaceDistance = candidate.Distance - candidate.Radius;
                // find-nearest.gc L95-113: prefer-center uses surface distance times
                // max(0.01, 1-dot); air/direct omits prefer-center and uses distance.
                candidate.Score = airborne ? surfaceDistance : surfaceDistance * Math.Max(0.01f, 1f - cosine);
                if (surfaceDistance >= (airborne ? AirReach : Reach)) continue;
                if (airborne)
                {
                    if (forward < 0f) continue;
                }
                else
                {
                    // L128-155: cull-angle is a cone expanded by the target sphere,
                    // not a strict centre angle and not a ray-hit priority class.
                    float lateral = (float)Math.Sqrt(Math.Max(0f, squared - forward * forward));
                    if (lateral - candidate.Radius > forward * ConeTangent) continue;
                }
                candidate.Flags = Eligible;
            }
            while (true)
            {
                int best = -1;
                for (int i = 0; i < count; i++)
                {
                    if ((candidates[i].Flags & (Eligible | Occluded)) != Eligible) continue;
                    if (best < 0 || Ranked(candidates[i], candidates[best])) best = i;
                }
                // target-gun.gc L1688-1700: successful acquisition always replaces focus.
                if (best < 0) break;
                if (world.Visible(origin, best)) return best;
                candidates[best].Flags |= Occluded;
            }
            // L1703-1756 retains the old focus only when no new acquisition succeeds.
            // Ground releases past 120 degrees of yaw; air/direct beyond 25m AND 60 degrees.
            // RoR2 adds the cover check so retained focus cannot aim through solid world.
            for (int i = 0; i < count; i++)
            {
                ref ArcCandidate candidate = ref candidates[i];
                if (focus.Health == 0 || candidate.Health != focus.Health || candidate.Box != focus.Box ||
                    (candidate.Flags & Occluded) != 0 || candidate.Distance <= 0f) continue;
                float cosine = Vector3.Dot(candidate.Point - origin, direction) / candidate.Distance;
                if (airborne)
                {
                    if (candidate.Distance > 25f && cosine < 0.5f) return -1;
                }
                else
                {
                    Vector3 offset = candidate.Point - origin;
                    float horizontal = (float)Math.Sqrt((offset.X * offset.X + offset.Z * offset.Z) *
                        (direction.X * direction.X + direction.Z * direction.Z));
                    if (horizontal > 0f && (offset.X * direction.X + offset.Z * direction.Z) / horizontal < -0.5f) return -1;
                }
                return world.Visible(origin, i) ? i : -1;
            }
            return -1;
        }
        private static bool Ranked(in ArcCandidate a, in ArcCandidate b)
        {
            if (a.Score != b.Score) return a.Score < b.Score;
            // Stable identities replace native actor-list order for exact ties only.
            return a.Health != b.Health ? a.Health < b.Health : a.Box < b.Box;
        }
    }
}
