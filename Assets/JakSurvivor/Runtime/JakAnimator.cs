using System.Linq;

namespace JakSurvivor
{
    public enum JakWeaponMode { Blaster, ArcWielder, WaveConcussor }

    internal static class JakAnimator
    {
        public static class Layers
        {
            public const string Body = "Body";
            public const string FullBody = "FullBody, Override";
            public const string Gesture = "Gesture, Override";
            public const string Transformation = "Transformation, Override";
            public const string AimPitch = "AimPitch";
            public const string AimYaw = "AimYaw";
            public const string BodyAction = "BodyAction";
            public const string Gun = "Gun";
            public const string Board = "Board";
        }

        public static class States
        {
            public const string Empty = "Empty";
            public const string Idle = "Idle";
            public const string Run = "Run";
            public const string Jump = "Jump";
            public const string Roll = "Roll";
            public const string RollLeap = "RollLeap";
            public const string BonusJump = "BonusJump";
            public const string AscendDescend = "AscendDescend";
            public const string Land = "Land";
            public const string Death = "Death";
            public static readonly string[] ClawCombo = { "ClawCombo1", "ClawCombo2", "ClawCombo3" };
            public const string Bomb = "Bomb";
            public const string DarkEnter = "DarkEnter";
            public const string DarkExit = "DarkExit";
            public const string DarkExitEnd = "DarkExitEnd";
            public const string DarkExitBomb = "DarkExitBomb";
            public const string BoardOn = "BoardOn";
            public const string BoardOff = "BoardOff";
            public const string FireBlaster = "FireBlaster";
            public const string BlasterHold = "BlasterHold";
            public const string FireScatter = "FireScatter";
            public const string ArcStart = "ArcStart";
            public const string ArcLoop = "ArcLoop";
            public const string WaveCharge = "WaveCharge";
            public const string WaveChargeLoop = "WaveChargeLoop";
            public const string WaveRelease = "WaveRelease";
            public const string PitchControl = "PitchControl";
            public const string YawControl = "YawControl";
            public const string BoardOpen = "Open";
            public const string BoardClose = "Close";
        }

        public static class Parameters
        {
            public const string Form = "form";
            public const string MoveForward = "moveForward";
            public const string MoveRight = "moveRight";
            public const string WeaponMode = "weaponMode";
            public const string ArcRate = "Arc.playbackRate";
            public const string WaveRate = "Wave.playbackRate";
            public const string BlasterRate = "Blaster.playbackRate";
            public const string ScatterRate = "Scatter.playbackRate";
            public const string ClawRate = "Claw.playbackRate";
            public const string BombRate = "Bomb.playbackRate";
            public const string TransformRate = "Transform.playbackRate";
            public const string BoardRate = "Board.playbackRate";
            public const string SpinRate = "Spin.playbackRate";
            public const string RollRate = "Roll.playbackRate";
            public const string JumpRate = "Jump.playbackRate";
            public const string ClawHitBoxActive = "Claw.hitBoxActive";
        }

        public static readonly string[] ControllerLayers = { Layers.Body, Layers.FullBody, Layers.Transformation, Layers.Gesture, Layers.AimPitch, Layers.AimYaw, Layers.BodyAction, Layers.Gun, Layers.Board };
        public static readonly string[] PoseLayers = { Layers.BodyAction, Layers.Gesture, Layers.Transformation, Layers.FullBody, Layers.Body };
        public const float JumpAirTime = 20f / 60f, BoardAirTime = 15f / 60f;
        // Longer than the gap between held shots, short enough that a released trigger lowers the gun promptly.
        public const float BlasterHoldTime = 0.1f;

        public static class Spin
        {
            public const string DataAsset = "Jak3Spin", HitBoxGroup = "JakSpin";
            public static string State(JakSpinVariant variant) => "Spin" + variant;
            // One recovery overlay per form; air variants name their form's state but own no overlay.
            public static string EndState(JakSpinVariant variant) => "SpinEnd" + JakSpinRecipe.Get(variant).Form;
            public static readonly string[] EndStates = Enumerable.Range(0, JakSpinRecipe.Count).Select(i => (JakSpinVariant)i)
                .Where(v => JakSpinRecipe.Get(v).EndOverlay != null).Select(EndState).ToArray();
        }

        public static class Blast
        {
            public const string Charge = "BlastCharge", Release = "BlastRelease", Recovery = "BlastRecovery", Rate = "Blast.playbackRate";
            public static readonly string[] States = { Charge, Release, Recovery };
        }

        public static class Bomb
        {
            public const float Duration = 0.9f, ImpactFraction = 0.5f;
            // bomb-land starts at artist frame 34; frame 35 is one 30 Hz artist-frame step later.
            public const float ImpactSourceTime = 1f / 30f, LandingBlend = 0.05f;
            // The airborne slam holds the windup's last pose until Jak lands.
            public const float FallTime = Duration * ImpactFraction - ImpactSourceTime - LandingBlend;
            public const string Clip = "jakb-darkjak-bomb@sequence";

            public static void Sample(float time, float windupLength, float landingLength, out float windupTime, out float landingTime, out float landingWeight)
            {
                const float impact = Duration * ImpactFraction, landingStart = impact - ImpactSourceTime, blend = LandingBlend;
                windupTime = UnityEngine.Mathf.Clamp01(time / FallTime) * windupLength;
                landingTime = time <= impact ? UnityEngine.Mathf.Max(0f, time - landingStart) :
                    UnityEngine.Mathf.Lerp(ImpactSourceTime, landingLength, (time - impact) / (Duration - impact));
                landingWeight = UnityEngine.Mathf.Clamp01((time - (landingStart - blend)) / blend);
            }
        }

        public static class Weapon
        {
            // OpenGOAL v0.3.8 target-gun.gc:2648-2649,2682-2699 uses INTERNAL joint
            // frames, not artist frames. Both fire pairs have artist-step 2, speed ~0.25
            // and export at speed*60 (~15 Hz): 7 Arc samples / 21 Wave samples.
            public const int ArcFrames = 7, WaveFrames = 21;
            public const float WaveLoopStartFrame = 5f, WaveLoopEndFrame = 8.5f, WaveReleaseFrame = 10f;
            public const string ArcBody = "jakb-gun-blue-fire-2", ArcGun = "gun-gun-blue-fire-2";
            public const string ArcStance = "jakb-gun-blue-stance-2", ArcIdle = "jakb-gun-stance-blue";
            public const string WaveBody = "jakb-gun-red-fire-2", WaveGun = "gun-gun-red-fire-2";
            public const string ChargeStart = "@charge-start", ChargeLoop = "@charge-loop", Release = "@release";
            public static readonly string[] RequiredBodyClips = { ArcIdle, ArcStance, ArcBody, WaveBody };
            public static readonly string[] RequiredGunClips = { "gun-idle-blue2", ArcGun, "gun-idle-red2", WaveGun };
            public static readonly string[] ArcLocomotion = { "jakb-gun-front-run", "jakb-jump@armed", "jakb-jump-forward@armed",
                "jakb-jump-double@armed", "jakb-jump-forward-double@armed", "jakb-jump-fall@armed", "jakb-gun-jump-land" };
            public static readonly string[] ActionStates = new[] { States.ArcStart, States.ArcLoop, States.WaveCharge, States.WaveChargeLoop, States.WaveRelease }.Concat(Spin.EndStates).ToArray();
            public static float FrameTime(float duration, int frames, float frame) => duration * frame / (frames - 1);
            public static string ArcClip(string locomotion) => locomotion.Replace("@armed", "") + "@arc";
        }

        public static void ArcStart(UnityEngine.Animator animator, float sampledRate) => PlayWeaponAction(animator, States.ArcStart, Parameters.ArcRate, sampledRate);
        public static void ArcLoop(UnityEngine.Animator animator, float sampledRate) => PlayWeaponAction(animator, States.ArcLoop, Parameters.ArcRate, sampledRate);

        // normalizedTime is gameplay charge q, not a 60 Hz/artist-frame index. A joining
        // peer seeks into the same source intro/loop instead of replaying the whole shot.
        public static void WaveCharge(UnityEngine.Animator animator, float sampledRate, float normalizedTime = 0f)
        {
            if (!animator) return;
            float intro = ClipLength(animator, Weapon.WaveBody + Weapon.ChargeStart);
            float loop = ClipLength(animator, Weapon.WaveBody + Weapon.ChargeLoop);
            float elapsed = UnityEngine.Mathf.Clamp01(normalizedTime) * Balance.WaveFullCharge;
            bool looping = elapsed >= intro;
            float time = looping ? UnityEngine.Mathf.Repeat(elapsed - intro, loop) / loop : elapsed / intro;
            PlayWeaponAction(animator, looping ? States.WaveChargeLoop : States.WaveCharge, Parameters.WaveRate, sampledRate, time);
        }

        public static void WaveRelease(UnityEngine.Animator animator, float sampledRate) => PlayWeaponAction(animator, States.WaveRelease, Parameters.WaveRate, sampledRate);

        public static void StopWeaponAction(UnityEngine.Animator animator)
        {
            if (!animator) return;
            int gesture = animator.GetLayerIndex(Layers.Gesture), gun = animator.GetLayerIndex(Layers.Gun);
            if (gesture < 0 || gun < 0) return; // The survivor-select controller has no weapon actions.
            animator.SetFloat(Parameters.ArcRate, 1f);
            animator.SetFloat(Parameters.WaveRate, 1f);
            animator.Play(States.Empty, gesture, 0f);
            animator.Play(States.Idle, gun, 0f);
        }

        public static bool HasWeaponAction(UnityEngine.Animator animator)
        {
            if (!animator) return false;
            int layer = animator.GetLayerIndex(Layers.Gesture);
            if (layer < 0) return false;
            foreach (string state in Weapon.ActionStates)
                if (animator.GetCurrentAnimatorStateInfo(layer).IsName(state) ||
                    (animator.IsInTransition(layer) && animator.GetNextAnimatorStateInfo(layer).IsName(state))) return true;
            return false;
        }

        private static void PlayWeaponAction(UnityEngine.Animator animator, string state, string rate, float sampledRate, float normalizedTime = 0f)
        {
            if (!animator) return;
            animator.SetFloat(rate, UnityEngine.Mathf.Max(0.01f, sampledRate));
            animator.Play(state, animator.GetLayerIndex(Layers.Gesture), normalizedTime);
            animator.Play(state, animator.GetLayerIndex(Layers.Gun), normalizedTime);
        }

        private static float ClipLength(UnityEngine.Animator animator, string name)
        {
            foreach (UnityEngine.AnimationClip clip in animator.runtimeAnimatorController.animationClips)
                if (clip.name == name) return clip.length;
            throw new System.InvalidOperationException("Required Morph Gun animation is missing: " + name);
        }

        public const float Armed = 0f, Dark = 1f, Board = 2f;

        public const string BodyController = "animJak";
        public const string DisplayController = "animJakDisplay";
        public const string ModelPrefab = "mdlJak";
        public const string DisplayPrefab = "JakDisplay";
        public const string DrawOffset = "Jak3DrawOffset";
        public const string ClawHitBoxGroup = "JakClaws";
        public static readonly string[] ClawSlashChildren = { "ClawSlash0", "ClawSlash1", "ClawSlash2" };
    }
}
