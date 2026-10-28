using System;
using BepInEx;
using BepInEx.Logging;
using R2API;
using R2API.Utils;
using RoR2;
using RoR2.ContentManagement;

namespace JakSurvivor
{
    [BepInPlugin(Guid, Name, Version)]
    [BepInDependency("com.bepis.r2api.prefab")]
    [BepInDependency("com.bepis.r2api.language")]
    [BepInDependency("com.bepis.r2api.recalculatestats")]
    [NetworkCompatibility(CompatibilityLevel.EveryoneMustHaveMod, VersionStrictness.EveryoneNeedSameModVersion)]
    public sealed class Plugin : BaseUnityPlugin
    {
        public const string Guid = "com.ror2crossover.jaksurvivor";
        public const string Name = "Jak Survivor";
        public const string Version = "0.1.2";
        internal static ManualLogSource Log { get; private set; }

        private void Awake()
        {
            Log = Logger;
            BindArcFeel();
            Localization.Register();
            ContentManager.collectContentPackProviders += AddContent;
            RecalculateStatsAPI.GetStatCoefficients += ApplyDarkStats;
            Logger.LogInfo($"{Name} {Version}: registering survivor content.");
        }

        private void BindArcFeel()
        {
            const string section = "Arc Wielder";
            JakArcRope.AimSeconds = Math.Max(0.001f, Config.Bind(section, "AimSeconds", JakArcRope.AimSeconds, "Seconds for the beam root to ease toward the aim or a newly acquired target.").Value);
            JakArcRope.GrabSeconds = Math.Max(0.001f, Config.Bind(section, "GrabSeconds", JakArcRope.GrabSeconds, "Seconds for the beam to fade onto or off a captured enemy.").Value);
            JakArcRope.ReachSpeed = Math.Max(1f, Config.Bind(section, "ReachSpeed", JakArcRope.ReachSpeed, "Metres per second the beam extends after firing starts.").Value);
            JakArcRope.FollowSeconds = Math.Max(0.001f, Config.Bind(section, "FollowSeconds", JakArcRope.FollowSeconds, "Seconds for each rope link to follow the link before it.").Value);
        }

        private static void AddContent(ContentManager.AddContentPackProviderDelegate add)
        {
            add(new JakContent());
        }

        private static void ApplyDarkStats(CharacterBody body, RecalculateStatsAPI.StatHookEventArgs stats)
        {
            if (JakContent.DarkBuff && body.HasBuff(JakContent.DarkBuff))
            {
                stats.armorAdd += Balance.DarkArmor;
                stats.moveSpeedMultAdd += Balance.DarkMoveBonus;
            }
        }

        private void OnDestroy()
        {
            ContentManager.collectContentPackProviders -= AddContent;
            RecalculateStatsAPI.GetStatCoefficients -= ApplyDarkStats;
        }
    }
}
