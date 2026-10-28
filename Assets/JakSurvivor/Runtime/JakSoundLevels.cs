namespace JakSurvivor
{
    internal static class JakSoundLevels
    {
        // Gameplay gains are relative to this level, which Jak.bnk normalizes each clip to.
        internal const float DefaultGain = 0.8f;
        // A raw gain for the selection display, measured against the lobby music
        // at low SFX settings. Jak 3's gun clinks sit about 17 dB under its
        // transformation sounds and vanish beneath the music at their original level.
        internal const float SelectionFormGain = 1.9f;
    }
}
