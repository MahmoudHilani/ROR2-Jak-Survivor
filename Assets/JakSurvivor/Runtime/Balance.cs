using UnityEngine;

namespace JakSurvivor
{
    // Coefficients multiply CharacterBody.damage; timings divide by attack speed.
    public static class Balance
    {
        public const float BlasterDamage = 2.4f;
        public const float BlasterDuration = 0.3f;
        public const float ScatterCooldown = 5f;
        public const float ArcDamage = 1.6f;
        public const float ArcInterval = 0.4f;
        public const float ArcDamageRetention = 0.75f;
        public const float ArcProc = 0.5f;
        public const int WaveStocks = 2;
        public const float WaveCooldown = 5f;
        public const float WaveMinimumCharge = 0.1f;
        public const float WaveFullCharge = 1f;
        // First imported charge-audio pass is 10 seconds. Leave a short guard
        // before its end; full charge does not itself request a release.
        public const float WaveAutoReleaseDelay = 9.95f;
        public const float WaveBaseRadius = 3f;
        public const float WaveRadiusGain = 15f;
        public const float WaveMinimumDamage = 2f;
        public const float WaveFullDamage = 10f;
        public const float WaveOuterRetention = 0.5f;
        public const float WaveProc = 1f;
        public const float WaveExpansionDuration = 0.7f;
        public const float WaveRecoveryDuration = 0.3f;
        public const float WaveForce = 500f;
        public const float SpinDamage = 3.5f, SpinProc = 0.5f, SpinCooldown = 3f, SpinForce = 800f;
        // target-util.gc:520-524 uses 2.2m; 4m adapts the sphere to RoR2 crowd spacing.
        public const float SpinRadius = 4f;
        public const float DarkBlastDamage = 8f, DarkBlastProc = 1f, DarkBlastCooldown = 3f;
        public const float RollCooldown = 4f;
        public const float BoardDuration = 4f;
        // MUL-T's Transport Mode uses 2.2x with forced sprint; the board is 20% faster.
        public const float BoardSpeed = 2.64f;
        // board/target-board.gc:1311-1332 (minimum at :1325): the board key dismounts only after 1 s.
        public const float BoardMinimumRide = 1f;
        public const float BoardCooldown = 6f;
        public const float DarkDuration = 10f;
        public const float DarkCooldown = 24f;
        public const float DarkArmor = 30f;
        public const float DarkMoveBonus = 0.2f;
        // Front edge 5.5 m; rear edge stays at -0.5 m. Width/height are unchanged.
        public static readonly Vector3 ClawHitBoxCenter = new Vector3(0f, 1.2f, 2.5f);
        public static readonly Vector3 ClawHitBoxSize = new Vector3(4f, 3f, 6f);
        public readonly struct ClawStep
        {
            public readonly string Clip;
            public readonly float Duration, HitStart, HitEnd, Damage;
            public readonly bool Finisher;
            public readonly Vector3 SlashPosition, SlashRotation, SlashScale;

            // Acrid's slash is a ring trail; its scale puts the arc on the hitbox's far edge.
            public ClawStep(string clip, float duration, float hitStart, float hitEnd, float damage, bool finisher,
                Vector3 slashPosition, Vector3 slashRotation, float slashScale)
            {
                Clip = clip; Duration = duration; HitStart = hitStart; HitEnd = hitEnd;
                Damage = damage; Finisher = finisher;
                SlashPosition = slashPosition; SlashRotation = slashRotation; SlashScale = Vector3.one * slashScale;
            }
        }

        public static readonly ClawStep[] ClawCombo =
        {
            // Strikes play the native forearm/hand/finger stretch. The slash anchors
            // and the claw hitbox are root-relative and do not follow the bones.
            new ClawStep("jakb-darkjak-attack-combo1", 0.333333f, 0.62f, 0.82f, 1.6f, false, new Vector3(0f, 1.2f, 0.2f), new Vector3(55f, 90f, 90f), 0.405429f),
            new ClawStep("jakb-darkjak-attack-combo2", 0.466666f, 0.76f, 0.92f, 1.9f, false, new Vector3(0f, 1.2f, 0.2f), new Vector3(20f, 90f, 90f), 0.405429f),
            new ClawStep("jakb-darkjak-attack-combo3c", 0.666664f, 0.70f, 0.82f, 3f, true, new Vector3(0f, 1.7f, 1f), new Vector3(0f, 90f, 0f), 0.282857f)
        };
        public const float ClawStepGrace = 0.4f;
        public const float DarkBombDamage = 8f;
        public const float DarkBombRadius = 14f;
    }
}
