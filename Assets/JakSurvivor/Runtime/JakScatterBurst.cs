using System;

namespace JakSurvivor
{
    internal static class JakScatterBurst
    {
        internal const float Duration = 0.2f, TrailDuration = 0.14f, FlashDuration = 0.08f;

        internal static float Fade(float age, float duration) => duration > 0f
            ? Math.Max(0f, Math.Min(1f, 1f - age / duration)) : 0f;

        // Hitscan is visible immediately; its tail catches the contact without overshooting it.
        // The tail eases in so the whole fan reads for a few frames before it retracts.
        internal static float Tail(float distance, float age)
        {
            float progress = 1f - Fade(age, TrailDuration);
            return Math.Max(0f, distance) * progress * progress;
        }
    }
}
